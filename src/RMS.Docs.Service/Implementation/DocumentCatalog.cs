using System.Text;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using RMS.Docs.Service.Enums;
using RMS.Docs.Service.Interfaces;
using RMS.Docs.Service.Models;
using RMS.Docs.Service.Options;

namespace RMS.Docs.Service.Implementation;

/// <summary>
/// Reads the documents in the configured directory.
/// </summary>
/// <remarks>
/// <para>
/// A document is a file in the documentation directory. There is no database, no index and no cache: the
/// directory is enumerated on every call, because a documentation directory holds tens of files of a few
/// kilobytes and the cost of being current is a handful of directory reads. A cache here would buy microseconds
/// and cost the one property that matters -- that a corrected document is corrected everywhere at once.
/// </para>
/// <para>
/// <strong>No path is ever built from caller text.</strong> A caller supplies a slug, and a slug only ever
/// selects from the list this class built by walking the directory itself. A request naming
/// <c>../../etc/passwd</c> does not reach a file system call; it fails to match a slug and is answered as absent.
/// This is the same rule the Configuration service follows for manifest names, and it is the reason neither
/// service needs to sanitise a path.
/// </para>
/// <para>
/// <strong>The title lives with the document.</strong> A front matter block at the top of a file can declare a
/// <c>title</c> and an <c>order</c>; when it does not, the title comes from the first heading and the order from a
/// default that sorts last. Any other key is ignored rather than refused, so a document may carry front matter
/// for another tool without this service objecting to it.
/// </para>
/// <para>
/// <strong>Front matter is a header, not a fence.</strong> A file that opens with <c>---</c> but never closes it
/// is treated as having no front matter at all. The alternative -- treating everything after the first line as
/// metadata -- would silently serve an empty document, and an empty document looks like a document that has not
/// been written yet rather than like a mistake.
/// </para>
/// </remarks>
public sealed class DocumentCatalog : IDocumentCatalog
{
    /// <summary>
    /// The extensions that are treated as documents, and how each one is read.
    /// </summary>
    /// <remarks>
    /// Markdown is the default because it is what documentation is written in; plain text is accepted because a
    /// file that is a list of ports or a transcript should not have to pretend to be prose. Anything else in the
    /// directory is ignored rather than reported: a screenshot beside a document is not a document that failed
    /// to load.
    /// </remarks>
    private static readonly Dictionary<string, DocumentFormat> ExtensionFormats =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [".md"] = DocumentFormat.Markdown,
            [".markdown"] = DocumentFormat.Markdown,
            [".txt"] = DocumentFormat.Text,
        };

    /// <summary>
    /// The line that opens and closes a front matter block.
    /// </summary>
    private const string FrontMatterFence = "---";

    /// <summary>
    /// The position an undeclared document takes within its section.
    /// </summary>
    /// <remarks>
    /// Late rather than early. A document that has just been added should not push an announcement off the top of
    /// the section; it should appear after the documents somebody took the trouble to position.
    /// </remarks>
    private const int DefaultOrder = 1000;

    /// <summary>
    /// How many characters of a document are read to resolve its title.
    /// </summary>
    /// <remarks>
    /// Enough for a front matter block and a heading, which is all a title needs, and small enough that listing a
    /// hundred documents does not read a hundred documents.
    /// </remarks>
    private const int HeadCharacters = 4096;

    /// <summary>
    /// How much text is shown either side of a search match.
    /// </summary>
    private const int SnippetRadius = 90;

    /// <summary>
    /// The largest number of search results a caller may ask for.
    /// </summary>
    /// <remarks>
    /// A ceiling on the work one request can cause rather than a product decision. A caller that asks for more
    /// gets the ceiling, and the response says which limit was applied so the two are distinguishable.
    /// </remarks>
    private const int MaximumResults = 200;

    /// <summary>
    /// A UTF-8 encoding that refuses invalid input instead of replacing it.
    /// </summary>
    /// <remarks>
    /// <c>File.ReadAllText</c> and the default encoding both substitute U+FFFD for bytes that are not valid UTF-8,
    /// which turns a file saved in the wrong encoding into a page of replacement characters that looks like a
    /// rendering fault. Refusing makes it a problem this service can report by name.
    /// </remarks>
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    private readonly DocsOptions _options;
    private readonly ILogger<DocumentCatalog> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DocumentCatalog"/> class.
    /// </summary>
    /// <param name="options">The bound documentation options.</param>
    /// <param name="logger">The logger used to record documents that could not be served.</param>
    public DocumentCatalog(IOptions<DocsOptions> options, ILogger<DocumentCatalog> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public DocumentListView List()
    {
        DocumentListView view = new() { DocumentDirectory = _options.DocumentDirectory };

        foreach (DocumentEntry entry in Enumerate(view.Problems))
        {
            DocumentSummaryView? summary = Summarise(entry, view.Problems);
            if (summary is not null) view.Documents.Add(summary);
        }

        view.Documents.Sort(CompareDocuments);
        view.Summary = Describe(view);

        return view;
    }

    /// <inheritdoc />
    public DocumentView? Read(string slug)
    {
        if (string.IsNullOrWhiteSpace(slug)) return null;

        List<DocumentProblemView> ignored = [];

        DocumentEntry? match = Enumerate(ignored)
            .FirstOrDefault(entry => string.Equals(entry.Slug, slug.Trim(), StringComparison.OrdinalIgnoreCase));

        // A slug that matches nothing is answered as absent. It is not an error and it is not a 400: the caller
        // asked for a document by name and there is no document by that name.
        if (match is null) return null;

        string text;

        try
        {
            text = ReadDocumentText(match.FullPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DecoderFallbackException)
        {
            _logger.LogError(exception, "The document {Slug} could not be read.", match.Slug);

            return null;
        }

        FrontMatter matter = ParseFrontMatter(text);
        string title = matter.Title ?? FirstHeading(text, matter.BodyOffset, match.Format) ?? DeSlug(match.Slug);

        string body = matter.BodyOffset > 0 && matter.BodyOffset < text.Length
            ? text[matter.BodyOffset..]
            : matter.BodyOffset > 0 ? string.Empty : text;

        return new DocumentView
        {
            Summary = new DocumentSummaryView
            {
                Slug = match.Slug,
                Title = title,
                Section = SectionOf(match.Slug),
                Format = match.Format,
                Order = matter.Order,
                LengthBytes = match.LengthBytes,
                LastWriteUtc = match.LastWriteUtc,
            },
            Body = body,
        };
    }

    /// <inheritdoc />
    public SearchResponseView Search(string query, int limit)
    {
        string phrase = query.Trim();
        int ceiling = Math.Clamp(limit, 1, MaximumResults);

        SearchResponseView response = new() { Query = phrase, Limit = ceiling };

        if (phrase.Length == 0) return response;

        List<DocumentProblemView> ignored = [];

        foreach (DocumentEntry entry in Enumerate(ignored))
        {
            if (response.Results.Count >= ceiling) break;

            string text;

            try
            {
                text = ReadDocumentText(entry.FullPath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DecoderFallbackException)
            {
                _logger.LogWarning(exception, "The document {Slug} was skipped while searching.", entry.Slug);

                continue;
            }

            FrontMatter matter = ParseFrontMatter(text);
            string title = matter.Title ?? FirstHeading(text, matter.BodyOffset, entry.Format) ?? DeSlug(entry.Slug);

            string body = matter.BodyOffset > 0 && matter.BodyOffset < text.Length
                ? text[matter.BodyOffset..]
                : matter.BodyOffset > 0 ? string.Empty : text;

            int occurrence = body.IndexOf(phrase, StringComparison.OrdinalIgnoreCase);

            /*
             * A document whose title matches is a match even when its body does not mention the phrase -- which
             * happens whenever somebody searches for the name of a page. The snippet then comes from the start of
             * the body, because there is no occurrence to centre it on and the opening line is the next most
             * useful thing to show.
             */
            bool titleMatch = title.Contains(phrase, StringComparison.OrdinalIgnoreCase);

            if (occurrence < 0 && !titleMatch) continue;

            response.Results.Add(new SearchResultView
            {
                Slug = entry.Slug,
                Title = title,
                Section = SectionOf(entry.Slug),
                Snippet = occurrence >= 0
                    ? BuildSnippet(body, occurrence, phrase.Length)
                    : BuildSnippet(body, 0, 0),
            });
        }

        response.Count = response.Results.Count;

        return response;
    }

    /// <summary>
    /// Builds the list of documents the directory currently holds.
    /// </summary>
    /// <param name="problems">A list the files that cannot be served are reported into.</param>
    /// <returns>The documents that can be served, in no particular order.</returns>
    /// <remarks>
    /// A file that cannot be served is reported and skipped rather than failing the enumeration. One unreadable
    /// document out of twenty must not cost the reader the other nineteen.
    /// </remarks>
    private List<DocumentEntry> Enumerate(List<DocumentProblemView> problems)
    {
        List<DocumentEntry> entries = [];

        if (_options.DocumentDirectory.Length == 0)
        {
            problems.Add(Describe(
                string.Empty,
                ProblemSeverity.Error,
                $"The document directory is not configured. Set the {DocsOptions.DocumentDirectoryVariable} "
                + "environment variable."));

            return entries;
        }

        if (!Directory.Exists(_options.DocumentDirectory))
        {
            problems.Add(Describe(
                string.Empty,
                ProblemSeverity.Error,
                $"The document directory '{_options.DocumentDirectory}' does not exist."));

            return entries;
        }

        HashSet<string> slugs = new(StringComparer.OrdinalIgnoreCase);

        foreach (string path in SafeEnumerate(_options.DocumentDirectory, problems))
        {
            FileInfo info;

            try
            {
                info = new FileInfo(path);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            /*
             * A reparse point inside the tree can point anywhere on the machine. Following one would mean serving
             * a file that was never in the documentation directory, which is the one way a directory enumeration
             * could be talked into reading something else.
             */
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0) continue;

            /*
             * Hidden files and editor leftovers are not documentation. An editor that writes "document.md~" beside
             * the document it is saving would otherwise put a second, stale copy of every page into the catalogue
             * for as long as the editor had it open.
             */
            if (info.Name.StartsWith('.') || info.Name.EndsWith('~')) continue;

            if (!ExtensionFormats.TryGetValue(info.Extension, out DocumentFormat format)) continue;

            string slug = ToSlug(_options.DocumentDirectory, path);

            if (slug.Length == 0) continue;

            if (!slugs.Add(slug))
            {
                problems.Add(Describe(
                    slug,
                    ProblemSeverity.Warning,
                    "Another file already answers to this name, so this one is ignored. Remove one of them or "
                    + "rename it."));

                continue;
            }

            if (info.Length > _options.MaximumDocumentBytes)
            {
                problems.Add(Describe(
                    slug,
                    ProblemSeverity.Error,
                    $"The document is {info.Length} bytes, which is over the {_options.MaximumDocumentBytes} byte "
                    + "limit, so it is not served."));

                continue;
            }

            entries.Add(new DocumentEntry(slug, info.FullName, format, info.Length, info.LastWriteTimeUtc));
        }

        return entries;
    }

    /// <summary>
    /// Enumerates the files below a directory, reporting a failure instead of throwing one.
    /// </summary>
    /// <param name="directory">The directory to enumerate.</param>
    /// <param name="problems">A list an enumeration failure is reported into.</param>
    /// <returns>The file paths, or an empty sequence when the directory could not be read.</returns>
    private static IEnumerable<string> SafeEnumerate(string directory, List<DocumentProblemView> problems)
    {
        try
        {
            return Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).ToList();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            problems.Add(Describe(
                string.Empty,
                ProblemSeverity.Error,
                $"The document directory could not be read: {exception.Message}"));

            return [];
        }
    }

    /// <summary>
    /// Resolves a document's metadata, reading only as much of it as a title needs.
    /// </summary>
    /// <param name="entry">The document.</param>
    /// <param name="problems">A list the document is reported into when it cannot be read.</param>
    /// <returns>The metadata, or <see langword="null"/> when the document could not be read.</returns>
    private DocumentSummaryView? Summarise(DocumentEntry entry, List<DocumentProblemView> problems)
    {
        string head;

        try
        {
            head = ReadHead(entry.FullPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DecoderFallbackException)
        {
            problems.Add(Describe(
                entry.Slug,
                ProblemSeverity.Error,
                $"The document could not be read: {exception.Message}"));

            return null;
        }

        FrontMatter matter = ParseFrontMatter(head);

        return new DocumentSummaryView
        {
            Slug = entry.Slug,
            Title = matter.Title ?? FirstHeading(head, matter.BodyOffset, entry.Format) ?? DeSlug(entry.Slug),
            Section = SectionOf(entry.Slug),
            Format = entry.Format,
            Order = matter.Order,
            LengthBytes = entry.LengthBytes,
            LastWriteUtc = entry.LastWriteUtc,
        };
    }

    /// <summary>
    /// Reads the beginning of a document.
    /// </summary>
    /// <param name="path">The document's path, taken from the enumeration.</param>
    /// <returns>The first few thousand characters.</returns>
    /// <remarks>
    /// The read goes through a <see cref="StreamReader"/> rather than over raw bytes so that a boundary is never
    /// placed in the middle of a multi-byte character: a strict decoder handed half a code point would refuse a
    /// document that is perfectly valid, and the refusal would depend on where the buffer happened to end.
    /// </remarks>
    private static string ReadHead(string path)
    {
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using StreamReader reader = new(stream, StrictUtf8, detectEncodingFromByteOrderMarks: true);

        char[] buffer = new char[HeadCharacters];
        int read = reader.Read(buffer, 0, buffer.Length);

        return new string(buffer, 0, read);
    }

    /// <summary>
    /// Reads a whole document.
    /// </summary>
    /// <param name="path">The document's path, taken from the enumeration.</param>
    /// <returns>The document's text.</returns>
    /// <remarks>
    /// The share mode permits other readers and writers, including a delete. A documentation directory is edited
    /// while it is being served, and a save that has to wait for a page render is a save that fails at the worst
    /// possible moment.
    /// </remarks>
    private static string ReadDocumentText(string path)
    {
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using StreamReader reader = new(stream, StrictUtf8, detectEncodingFromByteOrderMarks: true);

        return reader.ReadToEnd();
    }

    /// <summary>
    /// Reads a document's front matter.
    /// </summary>
    /// <param name="text">The document's text, or as much of it as was read.</param>
    /// <returns>The declared title and order, and where the body starts.</returns>
    /// <remarks>
    /// An unterminated block yields no front matter and a body offset of zero, so a document that opens with a
    /// horizontal rule is served whole rather than being treated as metadata that swallowed the page.
    /// </remarks>
    private static FrontMatter ParseFrontMatter(string text)
    {
        FrontMatter none = new(null, DefaultOrder, 0);

        int position = text.Length > 0 && text[0] == '\uFEFF' ? 1 : 0;
        int end = LineEnd(text, position);

        if (!text[position..end].Trim().Equals(FrontMatterFence, StringComparison.Ordinal)) return none;

        string? title = null;
        int order = DefaultOrder;

        int line = NextLine(text, end);

        while (line < text.Length)
        {
            int lineEnd = LineEnd(text, line);
            string content = text[line..lineEnd].Trim();

            if (content.Equals(FrontMatterFence, StringComparison.Ordinal))
            {
                return new FrontMatter(title, order, NextLine(text, lineEnd));
            }

            int separator = content.IndexOf(':');

            if (separator > 0)
            {
                string key = content[..separator].Trim();
                string value = content[(separator + 1)..].Trim();

                if (key.Equals("title", StringComparison.OrdinalIgnoreCase))
                {
                    title = Unquote(value);
                }
                else if (key.Equals("order", StringComparison.OrdinalIgnoreCase)
                         && int.TryParse(value, out int declared))
                {
                    order = declared;
                }
            }
            else if (content.Length > 0 && !content.StartsWith('#'))
            {
                /*
                 * A line that is neither a key, a comment, nor a fence means this was never a front matter block.
                 * A document that begins with a rule and a paragraph would otherwise have its first line read as a
                 * key and its body offset lost.
                 */
                return none;
            }

            line = NextLine(text, lineEnd);
        }

        return none;
    }

    /// <summary>
    /// Finds a document's title in its own first heading.
    /// </summary>
    /// <param name="text">The text to read.</param>
    /// <param name="offset">Where the body starts.</param>
    /// <param name="format">How the body is meant to be read.</param>
    /// <returns>The heading, or <see langword="null"/> when there is none.</returns>
    /// <remarks>
    /// For Markdown the title is the first level-one heading, and nothing else counts: a document that opens with
    /// a paragraph is a document whose title is its name, which is honest, whereas taking the first line would
    /// make the title the first sentence. For plain text the first non-empty line is used when it is short enough
    /// to be a heading rather than the opening of a paragraph.
    /// </remarks>
    private static string? FirstHeading(string text, int offset, DocumentFormat format)
    {
        int line = offset;

        while (line < text.Length)
        {
            int end = LineEnd(text, line);
            string content = text[line..end].Trim();

            if (content.Length > 0)
            {
                if (format == DocumentFormat.Markdown)
                {
                    return content.StartsWith("# ", StringComparison.Ordinal) ? content[2..].Trim() : null;
                }

                return content.Length <= 120 ? content : null;
            }

            line = NextLine(text, end);
        }

        return null;
    }

    /// <summary>
    /// Turns a slug into something that reads like a title.
    /// </summary>
    /// <param name="slug">The document's slug.</param>
    /// <returns>The last path segment with its separators replaced by spaces.</returns>
    /// <remarks>
    /// Only the first letter is capitalised. Title-casing every word would turn "api-reference" into "Api
    /// Reference" and "sqlite" into "Sqlite", and a fallback title is not worth mangling an acronym over.
    /// </remarks>
    private static string DeSlug(string slug)
    {
        int separator = slug.LastIndexOf('/');
        string name = separator >= 0 ? slug[(separator + 1)..] : slug;

        name = name.Replace('-', ' ').Replace('_', ' ').Trim();

        return name.Length == 0 ? slug : char.ToUpperInvariant(name[0]) + name[1..];
    }

    /// <summary>
    /// Derives a document's section from its slug.
    /// </summary>
    /// <param name="slug">The document's slug.</param>
    /// <returns>The first path segment, or an empty string for a top level document.</returns>
    private static string SectionOf(string slug)
    {
        int separator = slug.IndexOf('/');

        return separator > 0 ? slug[..separator] : string.Empty;
    }

    /// <summary>
    /// Converts a file's path into its slug.
    /// </summary>
    /// <param name="directory">The document directory.</param>
    /// <param name="path">The file's path, taken from the enumeration.</param>
    /// <returns>The slug, or an empty string when the path is not below the directory.</returns>
    private static string ToSlug(string directory, string path)
    {
        string relative = Path.GetRelativePath(directory, path);

        // A path outside the directory would still produce a relative path full of parents. It cannot happen for
        // an enumerated entry, and returning nothing is a better answer than publishing such a slug if it ever did.
        if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative)) return string.Empty;

        string extension = Path.GetExtension(relative);
        string withoutExtension = extension.Length > 0 ? relative[..^extension.Length] : relative;

        return withoutExtension.Replace('\\', '/').ToLowerInvariant();
    }

    /// <summary>
    /// Cuts a snippet around a match.
    /// </summary>
    /// <param name="text">The document's body.</param>
    /// <param name="index">Where the match starts, or zero when there is no match.</param>
    /// <param name="length">The match's length.</param>
    /// <returns>One line of text with the match somewhere inside it.</returns>
    /// <remarks>
    /// The cut is nudged forward to the next space when one is close, so a snippet does not begin in the middle of
    /// a word and read like a typo. Whitespace is then collapsed, because a snippet is rendered on one line and a
    /// fragment of a code block shown with its indentation would look like a layout fault rather than an excerpt.
    /// </remarks>
    private static string BuildSnippet(string text, int index, int length)
    {
        int start = Math.Max(0, index - SnippetRadius);
        int end = Math.Min(text.Length, index + length + SnippetRadius);

        if (start > 0)
        {
            int space = text.LastIndexOf(' ', start);

            if (space >= 0 && start - space < 20) start = space + 1;
        }

        string fragment = CollapseWhitespace(text[start..end]);

        if (start > 0) fragment = "..." + fragment;
        if (end < text.Length) fragment += "...";

        return fragment;
    }

    /// <summary>
    /// Replaces every run of whitespace with a single space.
    /// </summary>
    /// <param name="value">The text to collapse.</param>
    /// <returns>The text on one line.</returns>
    private static string CollapseWhitespace(string value)
    {
        StringBuilder builder = new(value.Length);

        bool pending = false;

        foreach (char character in value)
        {
            if (char.IsWhiteSpace(character))
            {
                pending = builder.Length > 0;
                continue;
            }

            if (pending)
            {
                builder.Append(' ');
                pending = false;
            }

            builder.Append(character);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Finds the end of the line that starts at the given position.
    /// </summary>
    /// <param name="text">The text to scan.</param>
    /// <param name="start">Where the line starts.</param>
    /// <returns>The position of the first carriage return, line feed, or the end of the text.</returns>
    private static int LineEnd(string text, int start)
    {
        int end = start;

        while (end < text.Length && text[end] != '\r' && text[end] != '\n') end++;

        return end;
    }

    /// <summary>
    /// Finds the start of the line after the one that ends at the given position.
    /// </summary>
    /// <param name="text">The text to scan.</param>
    /// <param name="lineEnd">Where the previous line ends.</param>
    /// <returns>The position of the next line's first character, or the end of the text.</returns>
    private static int NextLine(string text, int lineEnd)
    {
        int next = lineEnd;

        if (next < text.Length && text[next] == '\r') next++;
        if (next < text.Length && text[next] == '\n') next++;

        return next;
    }

    /// <summary>
    /// Removes a matching pair of quotes from a front matter value.
    /// </summary>
    /// <param name="value">The value as written.</param>
    /// <returns>The value without its surrounding quotes.</returns>
    private static string Unquote(string value)
    {
        if (value.Length >= 2
            && ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
        {
            return value[1..^1];
        }

        return value;
    }

    /// <summary>
    /// Creates a problem entry.
    /// </summary>
    /// <param name="slug">The document's slug, or an empty string when the problem is about the directory.</param>
    /// <param name="severity">How badly the problem affects the catalogue.</param>
    /// <param name="message">What is wrong, in terms the operator can act on.</param>
    /// <returns>The problem.</returns>
    private static DocumentProblemView Describe(string slug, ProblemSeverity severity, string message) =>
        new() { Slug = slug, Severity = severity, Message = message };

    /// <summary>
    /// Orders two documents for a navigation.
    /// </summary>
    /// <param name="left">The first document.</param>
    /// <param name="right">The second document.</param>
    /// <returns>A negative number when the first sorts earlier, positive when the second does.</returns>
    /// <remarks>
    /// Section, then declared order, then title. Sorting by title last is what makes two documents sharing an
    /// order stable rather than dependent on the file system's enumeration order.
    /// </remarks>
    private static int CompareDocuments(DocumentSummaryView left, DocumentSummaryView right)
    {
        int bySection = string.Compare(left.Section, right.Section, StringComparison.OrdinalIgnoreCase);

        if (bySection != 0) return bySection;

        int byOrder = left.Order.CompareTo(right.Order);

        if (byOrder != 0) return byOrder;

        return string.Compare(left.Title, right.Title, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Writes the one-line description of a catalogue.
    /// </summary>
    /// <param name="view">The catalogue.</param>
    /// <returns>A sentence.</returns>
    private static string Describe(DocumentListView view)
    {
        string summary = view.Documents.Count switch
        {
            0 => "No documents are being served.",
            1 => "One document.",
            _ => $"{view.Documents.Count} documents.",
        };

        int errors = view.Problems.Count(problem => problem.Severity == ProblemSeverity.Error);

        if (errors > 0) summary += $" {errors} could not be read.";

        return summary;
    }

    /// <summary>
    /// One document as the directory described it.
    /// </summary>
    /// <param name="Slug">The document's slug.</param>
    /// <param name="FullPath">The path the enumeration returned.</param>
    /// <param name="Format">How the body is meant to be read.</param>
    /// <param name="LengthBytes">The file's size.</param>
    /// <param name="LastWriteUtc">When the file was last written.</param>
    /// <remarks>
    /// The path is named <c>FullPath</c> rather than <c>Path</c> because a member called <c>Path</c> shadows
    /// <see cref="System.IO.Path"/> inside its own type, and the resulting failure is a compile error far from the
    /// line that caused it.
    /// </remarks>
    private sealed record DocumentEntry(
        string Slug,
        string FullPath,
        DocumentFormat Format,
        long LengthBytes,
        DateTime LastWriteUtc);

    /// <summary>
    /// What a document's front matter declared.
    /// </summary>
    /// <param name="Title">The declared title, or <see langword="null"/> when it declared none.</param>
    /// <param name="Order">The declared position, or the default.</param>
    /// <param name="BodyOffset">Where the body starts; zero when there was no front matter.</param>
    private sealed record FrontMatter(string? Title, int Order, int BodyOffset);
}
