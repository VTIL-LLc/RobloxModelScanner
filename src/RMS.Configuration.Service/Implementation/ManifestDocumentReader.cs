using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using Microsoft.Extensions.Options;

using RMS.Configuration.Service.Enums;
using RMS.Configuration.Service.Models;
using RMS.Configuration.Service.Options;

namespace RMS.Configuration.Service.Implementation;

/// <summary>
/// Turns a manifest's text into a document, and a file into its text.
/// </summary>
/// <remarks>
/// <para>
/// This is a faithful reimplementation of the part of <c>rmsdeploy.c</c> that reads a manifest, and it is
/// deliberately faithful rather than lenient. Where the deployer refuses a line, this records the refusal
/// instead of stopping, so that an operator can see every complaint at once; where the deployer would silently
/// do something other than what the line says, this records what it would do. A reader that accepted a little
/// more than the deployer would be a reader that reported a file as fine and then watched it fail on a
/// different machine.
/// </para>
/// <para>
/// Both readings of the text come out of one pass: the lines exactly as written, for the editor, and the
/// sections as the deployer would parse them, for everything else. Two passes would be two chances to disagree.
/// </para>
/// </remarks>
public sealed class ManifestDocumentReader
{
    /// <summary>
    /// The line ending used for a manifest that does not exist yet.
    /// </summary>
    /// <remarks>
    /// A line feed, on every platform. These files are checked in beside the code that deploys from them, and a
    /// repository whose manifests change their line ending depending on which host last saved them produces a
    /// diff in every line of a file whose whole value is that its diffs are readable.
    /// </remarks>
    public const string DefaultLineEnding = "\n";

    private readonly int _maximumBytes;

    /// <summary>
    /// Initializes a new instance of the <see cref="ManifestDocumentReader"/> class.
    /// </summary>
    /// <param name="options">The bound options, which carry the largest document that may be read.</param>
    public ManifestDocumentReader(IOptions<ConfigurationOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _maximumBytes = options.Value.MaximumDocumentBytes;
    }

    /// <summary>
    /// Reads a manifest from disk.
    /// </summary>
    /// <param name="kind">The kind of manifest to read.</param>
    /// <param name="name">The name to report the document under.</param>
    /// <param name="path">The absolute path to read.</param>
    /// <returns>
    /// The document. A file that is absent, unreadable or larger than the configured ceiling produces a document
    /// that says so rather than an exception, because the console's most useful answer on a freshly checked-out
    /// machine is which files are not there yet.
    /// </returns>
    public ManifestDocument Read(ManifestKind kind, string name, string path)
    {
        ManifestDocument document = new()
        {
            Kind = kind,
            Name = name,
            Path = path,
            LineEnding = DefaultLineEnding,
        };

        FileInfo info = new(path);

        if (!info.Exists)
        {
            document.Exists = false;
            return document;
        }

        document.Exists = true;
        document.Length = info.Length;
        document.ModifiedUtc = new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero);

        if (info.Length > _maximumBytes)
        {
            document.Unreadable =
                $"The file is {FormatBytes(info.Length)}, which is larger than the {FormatBytes(_maximumBytes)} " +
                "this console will read. A manifest this size is not a manifest; check that the path is right.";

            return document;
        }

        try
        {
            byte[] bytes = File.ReadAllBytes(path);

            document.Sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

            Fill(document, Decode(bytes));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            document.Unreadable = exception.Message;
        }

        return document;
    }

    /// <summary>
    /// Parses text as a manifest, without touching the file system.
    /// </summary>
    /// <param name="kind">The kind the text is being read as.</param>
    /// <param name="name">The name to report the document under.</param>
    /// <param name="text">The text to parse.</param>
    /// <returns>The parsed document, marked as not existing on disk.</returns>
    /// <remarks>
    /// The line ending is taken from the text itself, so a document parsed here describes what was sent. That is
    /// what makes the result of a check describe the text an operator typed rather than the file it did not
    /// replace.
    /// </remarks>
    public ManifestDocument Parse(ManifestKind kind, string name, string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        ManifestDocument document = new()
        {
            Kind = kind,
            Name = name,
            Path = string.Empty,
            Exists = false,
            LineEnding = DetectLineEnding(text),
        };

        if (Encoding.UTF8.GetByteCount(text) > _maximumBytes)
        {
            document.Unreadable =
                $"This is larger than the {FormatBytes(_maximumBytes)} this console will accept as a manifest.";

            return document;
        }

        Fill(document, text);

        return document;
    }

    /// <summary>
    /// Detects the line ending a document uses.
    /// </summary>
    /// <param name="text">The text to examine.</param>
    /// <returns>The first line ending that appears, or the default when there is none.</returns>
    /// <remarks>
    /// The first one rather than the most common one, because a file with mixed endings is a file whose author
    /// has already been surprised once, and reporting the ending of its first line is the answer that lets the
    /// rest of the file be recognised as the anomaly.
    /// </remarks>
    public static string DetectLineEnding(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        for (int index = 0; index < text.Length; index++)
        {
            if (text[index] == '\r')
                return index + 1 < text.Length && text[index + 1] == '\n' ? "\r\n" : "\r";

            if (text[index] == '\n')
                return "\n";
        }

        return DefaultLineEnding;
    }

    /// <summary>
    /// Rewrites every line ending in a text to one spelling.
    /// </summary>
    /// <param name="text">The text to rewrite.</param>
    /// <param name="lineEnding">The ending to use.</param>
    /// <returns>The normalised text.</returns>
    /// <remarks>
    /// A browser's text area submits CRLF whatever the page contained, so a file saved through the console would
    /// otherwise change ending the first time it was edited and show a diff in every line. Normalising on the way
    /// in is what keeps a save a change to the lines that were actually changed.
    /// </remarks>
    public static string NormaliseLineEndings(string text, string lineEnding)
    {
        ArgumentNullException.ThrowIfNull(text);

        StringBuilder builder = new(text.Length + 16);

        for (int index = 0; index < text.Length; index++)
        {
            char character = text[index];

            if (character == '\r')
            {
                if (index + 1 < text.Length && text[index + 1] == '\n') index++;
                builder.Append(lineEnding);
                continue;
            }

            if (character == '\n')
            {
                builder.Append(lineEnding);
                continue;
            }

            builder.Append(character);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Parses text into a document that has already been created.
    /// </summary>
    /// <param name="document">The document to fill.</param>
    /// <param name="text">The text to parse.</param>
    private static void Fill(ManifestDocument document, string text)
    {
        // A byte order mark is not whitespace, so the deployer strips it before anything else, and so does this.
        // A document read through the console therefore loses it -- which is correct: the deployer skips it
        // anyway, and a mark carried through an edit would otherwise become part of the first key.
        string body = text.Length > 0 && text[0] == '\uFEFF' ? text[1..] : text;

        // A StringReader treats CRLF, a bare CR and a bare LF all as one break, which is what the deployer's own
        // reader does and what a file with mixed endings needs.
        using StringReader reader = new(body);

        int number = 0;
        ManifestSectionView? section = null;

        while (reader.ReadLine() is { } raw)
        {
            number++;

            ManifestLine line = new() { Number = number, Text = raw };
            document.Lines.Add(line);

            // The deployer reads a line into a buffer of RMS_MAX_LINE and cannot tell that it has read half of
            // one: the second half becomes a key with no equals sign and the file is refused, at a line that
            // looks correct in an editor. Measured in bytes, because the buffer is bytes.
            if (raw.Length >= DeployerLimits.MaximumLineLength / 2 &&
                Encoding.UTF8.GetByteCount(raw) >= DeployerLimits.MaximumLineLength)
            {
                line.Kind = ManifestLineKind.Malformed;
                line.Problem =
                    $"the line is longer than the {DeployerLimits.MaximumLineLength} bytes the deployer reads at "
                    + "a time, so it will be read as two lines and the file refused";

                continue;
            }

            string trimmed = raw.TrimStart();

            if (trimmed.Length == 0)
            {
                line.Kind = ManifestLineKind.Blank;
            }
            else if (trimmed[0] is '#' or ';')
            {
                line.Kind = ManifestLineKind.Comment;
            }
            else if (trimmed[0] == '[')
            {
                section = ReadHeader(document, line, trimmed, section);
            }
            else
            {
                ReadEntry(line, trimmed, section);
            }

            if (section is not null && line.Kind != ManifestLineKind.Header)
                line.Section = section.Id;
        }
    }

    /// <summary>
    /// Reads a section header.
    /// </summary>
    /// <param name="document">The document being filled.</param>
    /// <param name="line">The line being read.</param>
    /// <param name="trimmed">The line with its leading whitespace removed.</param>
    /// <param name="previous">The section in force before this line.</param>
    /// <returns>The section in force after this line.</returns>
    private static ManifestSectionView? ReadHeader(
        ManifestDocument document,
        ManifestLine line,
        string trimmed,
        ManifestSectionView? previous)
    {
        int closing = trimmed.IndexOf(']');

        if (closing < 0)
        {
            line.Kind = ManifestLineKind.Malformed;
            line.Problem = "a section header is missing its closing bracket, and the deployer refuses the whole "
                           + "file when it sees one";

            return previous;
        }

        line.Kind = ManifestLineKind.Header;

        ManifestSectionView section = new()
        {
            Id = trimmed[1..closing].Trim(),
            Line = line.Number,
        };

        document.Sections.Add(section);

        return section;
    }

    /// <summary>
    /// Reads a key and its value.
    /// </summary>
    /// <param name="line">The line being read.</param>
    /// <param name="trimmed">The line with its leading whitespace removed.</param>
    /// <param name="section">The section in force, or <see langword="null"/> when there is none yet.</param>
    private static void ReadEntry(ManifestLine line, string trimmed, ManifestSectionView? section)
    {
        if (section is null)
        {
            line.Kind = ManifestLineKind.Malformed;
            line.Problem = "a key appears before any section header, and the deployer refuses the file when it "
                           + "sees one";

            return;
        }

        int separator = trimmed.IndexOf('=');

        if (separator < 0)
        {
            line.Kind = ManifestLineKind.Malformed;
            line.Problem = "the deployer expects 'key = value', and refuses the file when it sees a line without "
                           + "an equals sign";

            return;
        }

        string key = trimmed[..separator].Trim();
        string value = trimmed[(separator + 1)..].Trim();

        if (key.Length == 0)
        {
            line.Kind = ManifestLineKind.Malformed;
            line.Problem = "the deployer does not allow a key that is empty, and refuses the file when it sees "
                           + "one";

            return;
        }

        line.Kind = ManifestLineKind.Entry;

        if (DeployerVocabulary.TrySplitKey(key, out string platform, out string unqualified))
        {
            line.Platform = platform;
            line.Key = unqualified;
        }
        else
        {
            line.Key = key;
        }

        line.Value = value;

        section.Entries.Add(new ManifestEntryView
        {
            Key = line.Key,
            Value = value,
            Platform = line.Platform,
            Line = line.Number,
        });
    }

    /// <summary>
    /// Decodes a manifest's bytes as text.
    /// </summary>
    /// <param name="bytes">The bytes to decode.</param>
    /// <returns>The decoded text.</returns>
    /// <remarks>
    /// A strict UTF-8 decode, with a fall back to a permissive one. A file written by an editor with the wrong
    /// encoding would otherwise make the whole console fail on one byte, and being able to open it in order to
    /// see what is wrong with it is the reason the console exists. Invalid sequences are replaced rather than
    /// removed, so that a line's reported length still matches the file's.
    /// </remarks>
    private static string Decode(byte[] bytes)
    {
        try
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
                .GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false)
                .GetString(bytes);
        }
    }

    /// <summary>
    /// Formats a byte count for a message.
    /// </summary>
    /// <param name="bytes">The count to format.</param>
    /// <returns>The count in bytes, in kibibytes or in mebibytes, whichever reads best.</returns>
    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return bytes.ToString(CultureInfo.InvariantCulture) + " bytes";

        if (bytes < 1024 * 1024)
            return (bytes / 1024.0).ToString("0.#", CultureInfo.InvariantCulture) + " KiB";

        return (bytes / (1024.0 * 1024.0)).ToString("0.#", CultureInfo.InvariantCulture) + " MiB";
    }
}
