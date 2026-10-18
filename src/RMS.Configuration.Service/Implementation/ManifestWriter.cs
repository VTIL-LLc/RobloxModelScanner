using System.Text;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using RMS.Configuration.Service.Enums;
using RMS.Configuration.Service.Exceptions;
using RMS.Configuration.Service.Interfaces;
using RMS.Configuration.Service.Models;
using RMS.Configuration.Service.Options;

namespace RMS.Configuration.Service.Implementation;

/// <summary>
/// Writes a manifest, once, with a precondition.
/// </summary>
/// <remarks>
/// <para>
/// The file is written whole. A manifest is a hundred and twenty lines of which sixty are a comment explaining
/// the format, the operator is looking at all of them, and a description of a change would be a second syntax to
/// get right on top of the one being edited -- and one that could not express a comment at all.
/// </para>
/// <para>
/// Two things make writing whole safe. The first is the precondition: the request names the hash the page read,
/// so a file that changed underneath is a refusal rather than a silent overwrite of somebody else's work. The
/// second is the copy: the previous contents are kept beside the file, so a wrong write is a moment's work to
/// undo rather than a search through whatever the file was before.
/// </para>
/// <para>
/// Nothing here decides whether a change is a good idea. It decides whether the change is one the deployer could
/// read, which is a different question with an answer that does not depend on what anybody intends.
/// </para>
/// </remarks>
public sealed class ManifestWriter
{
    /// <summary>
    /// The suffix of the copy kept of a file's previous contents.
    /// </summary>
    public const string BackupSuffix = ".backup";

    private readonly ConfigurationOptions _options;
    private readonly IManifestCatalog _catalog;
    private readonly ManifestDocumentReader _reader;
    private readonly ILogger<ManifestWriter> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ManifestWriter"/> class.
    /// </summary>
    /// <param name="options">The bound options, which carry the addresses and the size ceiling.</param>
    /// <param name="catalog">The catalog that resolves a kind to a path.</param>
    /// <param name="reader">The reader that hashes the file the write is conditioned on.</param>
    /// <param name="logger">The logger that records every write.</param>
    public ManifestWriter(
        IOptions<ConfigurationOptions> options,
        IManifestCatalog catalog,
        ManifestDocumentReader reader,
        ILogger<ManifestWriter> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options.Value;
        _catalog = catalog;
        _reader = reader;
        _logger = logger;
    }

    /// <summary>
    /// Checks text, and writes it when it is a manifest the deployer could read.
    /// </summary>
    /// <param name="kind">The manifest to write. The plan is refused.</param>
    /// <param name="request">The text and the hash it was read at.</param>
    /// <returns>What happened, which is a result even when nothing was written.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the manifest is generated rather than written, or when no directory is configured for it.
    /// </exception>
    /// <exception cref="ManifestPreconditionException">Thrown when the file changed since it was read.</exception>
    public ManifestWriteResult Write(ManifestKind kind, ManifestWriteRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (kind == ManifestKind.Plan)
        {
            throw new InvalidOperationException(
                "The plan is written by the deployer, so editing it here would produce a change that disappears "
                + "at the next deployment. Edit the services manifest it is generated from.");
        }

        string name = _catalog.FileNameFor(kind);
        string path = _catalog.PathFor(kind);

        if (path.Length == 0)
        {
            throw new InvalidOperationException(
                $"No directory is configured for {name}, so there is nothing to write to. Set the "
                + $"{ConfigurationOptions.ManifestDirectoryVariable} environment variable.");
        }

        // The current state, read once: the hash the precondition is tested against and the line ending the new
        // text is normalised to both come out of this one read.
        ManifestDocument existing = _reader.Read(kind, name, path);

        if (existing.Exists && existing.Sha256.Length == 0)
        {
            throw new InvalidOperationException(
                $"{path} exists but could not be read{Reason(existing)}, so this console will not write over it.");
        }

        if (!string.IsNullOrEmpty(request.ExpectedSha256) && !existing.Exists)
        {
            throw new ManifestPreconditionException(
                "The request named a version of " + name + " to replace, but there is no file there any more.",
                string.Empty);
        }

        if (existing.Exists &&
            !string.Equals(request.ExpectedSha256, existing.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new ManifestPreconditionException(
                request.ExpectedSha256.Length == 0
                    ? $"{name} already exists, and the request did not say which version of it was read. Reload "
                      + "the file and try again."
                    : $"{name} has changed since it was read. Reload it and apply your change again.",
                existing.Sha256);
        }

        string text = Normalise(request.Text ?? string.Empty, existing.LineEnding);

        if (Encoding.UTF8.GetByteCount(text) > _options.MaximumDocumentBytes)
        {
            return Result(
                name,
                path,
                existing,
                document: null,
                written: false,
                checkedOnly: false,
                "Not written: the text is larger than the largest manifest this service accepts.",
                [
                    new ManifestProblem
                    {
                        Severity = ProblemSeverity.Error,
                        Line = 0,
                        Message =
                            $"The text is {Encoding.UTF8.GetByteCount(text) / 1024} KiB and this service accepts "
                            + $"at most {_options.MaximumDocumentBytes / 1024} KiB. The file on disk is unchanged.",
                    },
                ]);
        }

        ManifestDocument parsed = _catalog.Parse(kind, text);

        if (parsed.ErrorCount > 0)
        {
            return Result(
                name,
                path,
                existing,
                document: parsed,
                written: false,
                checkedOnly: false,
                $"Not written: {parsed.ErrorCount} error{(parsed.ErrorCount == 1 ? string.Empty : "s")} would "
                + "make this a manifest the deployer refuses. The file on disk is unchanged.",
                parsed.Problems);
        }

        ManifestDocument written = WriteFile(kind, path, text, existing);

        _logger.LogInformation(
            "Wrote {Name} at {Path}: {Sections} sections, {Warnings} warnings. The operator's identity is not "
            + "known to this service; the site records it.",
            name,
            path,
            written.SectionCount,
            written.WarningCount);

        return Result(
            name,
            path,
            written,
            document: written,
            written: true,
            checkedOnly: false,
            $"Written: {written.SectionCount} section{(written.SectionCount == 1 ? string.Empty : "s")}, "
            + $"{written.ErrorCount} error{(written.ErrorCount == 1 ? string.Empty : "s")} and "
            + $"{written.WarningCount} warning{(written.WarningCount == 1 ? string.Empty : "s")}. "
            + "Run the deployer's services command to turn it into a plan.",
            written.Problems);
    }

    /// <summary>
    /// Checks text without writing it.
    /// </summary>
    /// <param name="kind">The manifest to check the text against.</param>
    /// <param name="request">The text to check. The hash is ignored.</param>
    /// <returns>The problems the text would have, and the counts.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the kind is generated rather than written.</exception>
    /// <remarks>
    /// A check touches nothing, not even the file: it parses the text that was sent. That is what makes it usable
    /// while an operator is still typing, and what makes its answer a statement about what they wrote rather than
    /// about what is on disk.
    /// </remarks>
    public ManifestWriteResult Check(ManifestKind kind, ManifestWriteRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (kind == ManifestKind.Plan)
        {
            throw new InvalidOperationException(
                "The plan is generated by the deployer and has no writable form to check.");
        }

        string name = _catalog.FileNameFor(kind);
        ManifestDocument document = _catalog.Parse(kind, request.Text ?? string.Empty);

        return new ManifestWriteResult
        {
            Written = false,
            CheckedOnly = true,
            Name = name,
            Path = _catalog.PathFor(kind),
            Sha256 = document.Sha256,
            BackupPath = string.Empty,
            Message = document.ErrorCount > 0
                ? $"{document.ErrorCount} error{(document.ErrorCount == 1 ? " would" : "s would")} stop the "
                  + "deployer reading this."
                : $"Nothing here would stop the deployer reading this. "
                  + $"{document.WarningCount} warning{(document.WarningCount == 1 ? string.Empty : "s")}.",
            Document = document,
            Problems = document.Problems,
        };
    }

    /// <summary>
    /// Writes the text, keeping a copy of what was there.
    /// </summary>
    /// <param name="kind">The manifest being written.</param>
    /// <param name="path">The path to write to.</param>
    /// <param name="text">The text to write.</param>
    /// <param name="existing">The file as it was read, which says whether there is anything to copy.</param>
    /// <returns>The document as it now stands on disk.</returns>
    private ManifestDocument WriteFile(
        ManifestKind kind,
        string path,
        string text,
        ManifestDocument existing)
    {
        string? directory = Path.GetDirectoryName(path);

        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        if (existing.Exists)
        {
            // The copy is of the file's bytes, not of a re-rendering of them, so a restore is exact.
            File.Copy(path, path + BackupSuffix, overwrite: true);
        }

        // Written to a neighbour and moved into place, so that a failure part way through leaves the manifest
        // exactly as it was. A half-written manifest is a deployment that fails on a machine that has the file
        // and no way to tell that it is wrong.
        string temporary = path + ".writing";

        try
        {
            File.WriteAllText(temporary, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }

        // Re-read rather than reuse the parsed text: the file is the authority, and the hash the page is told
        // about has to be the hash of what is now on disk. Read through the catalog, so the document that comes
        // back carries its problems and the counts in the log line are the counts an operator would be shown.
        ManifestDocument document = _catalog.Read(kind);

        return document;
    }

    /// <summary>
    /// Builds a result for a request that did not reach the file.
    /// </summary>
    /// <param name="name">The manifest's file name.</param>
    /// <param name="path">The path the request would have written to.</param>
    /// <param name="existing">The file as it was read.</param>
    /// <param name="document">
    /// The document the request produced, or <see langword="null"/> when there is nothing to show.
    /// </param>
    /// <param name="written">Whether the file was written.</param>
    /// <param name="checkedOnly">Whether the request was a check.</param>
    /// <param name="message">The sentence describing what happened.</param>
    /// <param name="problems">The problems the text has.</param>
    /// <returns>The result.</returns>
    private static ManifestWriteResult Result(
        string name,
        string path,
        ManifestDocument existing,
        ManifestDocument? document,
        bool written,
        bool checkedOnly,
        string message,
        List<ManifestProblem> problems)
    {
        return new ManifestWriteResult
        {
            Written = written,
            CheckedOnly = checkedOnly,
            Name = name,
            Path = path,

            // The hash the page should send with its next attempt is the file's, which a refusal leaves
            // unchanged. A page that resends the hash it read the first time therefore keeps working.
            Sha256 = existing.Sha256,
            BackupPath = existing.Exists ? path + BackupSuffix : string.Empty,
            Message = message,
            Document = document,
            Problems = problems,
        };
    }

    /// <summary>
    /// Rewrites a text's line endings to the ones the file uses.
    /// </summary>
    /// <param name="text">The text to normalise.</param>
    /// <param name="lineEnding">The ending to use.</param>
    /// <returns>The normalised text.</returns>
    private static string Normalise(string text, string lineEnding)
    {
        string ending = lineEnding.Length > 0 ? lineEnding : ManifestDocumentReader.DefaultLineEnding;
        string normalised = ManifestDocumentReader.NormaliseLineEndings(text, ending);

        // An editor and a browser between them drop a trailing newline on the way through a text area. A file
        // whose last line has no terminator is a file that many tools report as changed, so one is put back.
        return normalised.EndsWith(ending, StringComparison.Ordinal) ? normalised : normalised + ending;
    }

    /// <summary>
    /// Renders the reason a file could not be read as part of a sentence.
    /// </summary>
    /// <param name="document">The document that could not be read.</param>
    /// <returns>The reason, punctuated, or an empty string.</returns>
    private static string Reason(ManifestDocument document)
    {
        return document.Unreadable.Length > 0 ? " (" + document.Unreadable + ")" : string.Empty;
    }
}
