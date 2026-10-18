using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using RMS.Configuration.Service.Enums;
using RMS.Configuration.Service.Interfaces;
using RMS.Configuration.Service.Models;
using RMS.Configuration.Service.Options;

namespace RMS.Configuration.Service.Implementation;

/// <summary>
/// Knows where the deployment's manifests are and reads them.
/// </summary>
/// <remarks>
/// <para>
/// A caller never supplies a path. It supplies the name of one of the four files this stack has, and the catalog
/// turns that into a configured directory plus a fixed file name. That is the whole of the traversal defence:
/// nothing here is built from caller text, so there is nothing to sanitise and no filter that can be forgotten.
/// </para>
/// <para>
/// Every document this hands out has been checked, so that a page cannot render a manifest's section table
/// without its problems. The one exception is the overview's file list, which stops parsing above a size
/// threshold and says so by reporting a negative count rather than a zero.
/// </para>
/// </remarks>
public sealed class ManifestCatalog : IManifestCatalog
{
    /// <summary>
    /// The size above which the overview stops parsing a file to count its problems.
    /// </summary>
    /// <remarks>
    /// The real manifests are between six and eleven kilobytes, so this is two or three orders of magnitude of
    /// headroom. It exists so that a path mistake -- a database, an archive, a log that grew -- produces a page
    /// that loads rather than a page that reads a hundred megabytes four times.
    /// </remarks>
    private const long ParseThresholdBytes = 64 * 1024;

    private readonly ConfigurationOptions _options;
    private readonly ManifestDocumentReader _reader;
    private readonly IManifestValidator _validator;
    private readonly ILogger<ManifestCatalog> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ManifestCatalog"/> class.
    /// </summary>
    /// <param name="options">The bound options, which carry the addresses.</param>
    /// <param name="reader">The reader that turns a file into a document.</param>
    /// <param name="validator">The validator that checks a document.</param>
    /// <param name="logger">The logger used to record an unreadable manifest.</param>
    public ManifestCatalog(
        IOptions<ConfigurationOptions> options,
        ManifestDocumentReader reader,
        IManifestValidator validator,
        ILogger<ManifestCatalog> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options.Value;
        _reader = reader;
        _validator = validator;
        _logger = logger;
    }

    /// <inheritdoc />
    public string RepositoryRoot => _options.RepositoryRoot;

    /// <inheritdoc />
    public string ManifestDirectory => _options.ManifestDirectory;

    /// <inheritdoc />
    public string PlanFile => _options.PlanFile;

    /// <inheritdoc />
    public bool TryResolve(string name, out ManifestKind kind)
    {
        kind = ManifestKind.Requirements;

        if (string.IsNullOrWhiteSpace(name)) return false;

        string trimmed = name.Trim();

        // Both the short name and the file name are accepted. An operator who has just read a message from the
        // deployer has the file name in front of them, and one who is looking at a page has the short name.
        foreach (ManifestKind candidate in Enum.GetValues<ManifestKind>())
        {
            if (Describe(candidate).Equals(trimmed, StringComparison.OrdinalIgnoreCase) ||
                FileNameFor(candidate).Equals(trimmed, StringComparison.OrdinalIgnoreCase))
            {
                kind = candidate;
                return true;
            }
        }

        return false;
    }

    /// <inheritdoc />
    public string FileNameFor(ManifestKind kind) => DeployerVocabulary.For(kind).FileName;

    /// <inheritdoc />
    public string PathFor(ManifestKind kind)
    {
        string fileName = FileNameFor(kind);

        if (kind == ManifestKind.Plan)
        {
            // The plan has a configured path of its own, because it is generated into the publish output rather
            // than living beside the hand-written manifests it came from.
            if (_options.PlanFile.Length > 0) return _options.PlanFile;
        }

        if (_options.ManifestDirectory.Length == 0) return string.Empty;

        return Path.Combine(_options.ManifestDirectory, fileName);
    }

    /// <inheritdoc />
    public ManifestListView List()
    {
        ManifestListView view = new()
        {
            RepositoryRoot = _options.RepositoryRoot,
            ManifestDirectory = _options.ManifestDirectory,
            PlanFile = _options.PlanFile,
        };

        CheckAddresses(view);

        foreach (ManifestKind kind in Enum.GetValues<ManifestKind>())
            view.Files.Add(DescribeFile(kind));

        int present = view.PresentCount;
        int errors = view.ErrorCount;

        view.Summary = present == 0
            ? "None of the four files are present, so this service is pointed at a directory that is not a "
              + "deployment."
            : $"{present} of 4 files present"
              + (errors > 0
                  ? $", with {errors} error{(errors == 1 ? string.Empty : "s")} across them."
                  : ", and nothing wrong that this console can see.");

        return view;
    }

    /// <inheritdoc />
    public ManifestDocument Read(ManifestKind kind)
    {
        string path = PathFor(kind);

        if (path.Length == 0)
        {
            ManifestDocument unlocated = _reader.Parse(kind, FileNameFor(kind), string.Empty);

            unlocated.Unreadable = kind == ManifestKind.Plan
                ? "No plan file is configured. Set the " + ConfigurationOptions.PlanFileVariable
                  + " environment variable to the plan the deployer wrote."
                : "No manifest directory is configured. Set the " + ConfigurationOptions.ManifestDirectoryVariable
                  + " environment variable to the directory holding the deployer's manifests.";

            _validator.Validate(unlocated);

            return unlocated;
        }

        ManifestDocument document = _reader.Read(kind, FileNameFor(kind), path);

        _validator.Validate(document);

        if (document.Unreadable.Length > 0)
            _logger.LogWarning("The manifest {Path} could not be read: {Reason}", path, document.Unreadable);

        return document;
    }

    /// <inheritdoc />
    public ManifestDocument Parse(ManifestKind kind, string text)
    {
        ManifestDocument document = _reader.Parse(kind, FileNameFor(kind), text);

        _validator.Validate(document);

        return document;
    }

    /// <summary>
    /// Builds one file's overview entry.
    /// </summary>
    /// <param name="kind">The manifest to describe.</param>
    /// <returns>The entry.</returns>
    private ManifestFileView DescribeFile(ManifestKind kind)
    {
        string name = FileNameFor(kind);
        string path = PathFor(kind);

        ManifestFileView file = new()
        {
            Kind = kind,
            Name = name,
            Path = path,
            Editable = kind != ManifestKind.Plan,
        };

        if (path.Length == 0)
        {
            file.Exists = false;
            file.Unreadable = "No directory is configured for this file.";
            file.Summary = kind == ManifestKind.Plan
                ? "No plan file is configured."
                : "No manifest directory is configured, so this file cannot be looked for.";

            return file;
        }

        FileInfo info = new(path);

        if (!info.Exists)
        {
            file.Exists = false;
            file.Summary = kind == ManifestKind.Plan
                ? $"There is no plan at {path}. It is generated by the deployer's services command."
                : $"There is no {name} at {path}, so the deployer reports this part of the plan as absent.";

            return file;
        }

        file.Exists = true;
        file.Length = info.Length;
        file.ModifiedUtc = new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero);

        if (info.Length > ParseThresholdBytes)
        {
            // The counts stay negative, which means "not asked". Zero would mean "asked, and nothing is wrong",
            // and that is a claim this page has no business making about a file it did not read.
            file.Summary =
                $"The file is {info.Length / 1024} KiB, which is larger than the {ParseThresholdBytes / 1024} KiB "
                + "this overview will parse. Its size and time are shown; open it to see whether it is right.";

            return file;
        }

        ManifestDocument document = Read(kind);

        file.Sha256 = document.Sha256;
        file.SectionCount = document.SectionCount;
        file.ErrorCount = document.ErrorCount;
        file.WarningCount = document.WarningCount;
        file.Unreadable = document.Unreadable;
        file.Summary = document.Describe();

        return file;
    }

    /// <summary>
    /// Reports the addresses themselves when they are not usable.
    /// </summary>
    /// <param name="view">The list to add to.</param>
    /// <remarks>
    /// Problems about the addresses rather than about a file, because there is no file to attribute them to and a
    /// page that showed nothing at all would leave an operator with no idea which of the three addresses was
    /// wrong.
    /// </remarks>
    private void CheckAddresses(ManifestListView view)
    {
        if (_options.ManifestDirectory.Length == 0)
        {
            view.Problems.Add(Problem(
                ProblemSeverity.Error,
                $"No manifest directory is configured, so none of the three hand-written manifests can be read. "
                + $"Set the {ConfigurationOptions.ManifestDirectoryVariable} environment variable."));
        }
        else if (!Directory.Exists(_options.ManifestDirectory))
        {
            view.Problems.Add(Problem(
                ProblemSeverity.Error,
                $"The manifest directory {_options.ManifestDirectory} does not exist."));
        }

        if (_options.RepositoryRoot.Length == 0)
        {
            view.Problems.Add(Problem(
                ProblemSeverity.Warning,
                $"No repository root is configured, so {ConfigurationOptions.RootPlaceholder} is reported as "
                + "written rather than expanded, and a build step's project cannot be looked for."));
        }
        else if (!Directory.Exists(_options.RepositoryRoot))
        {
            view.Problems.Add(Problem(
                ProblemSeverity.Error,
                $"The repository root {_options.RepositoryRoot} does not exist."));
        }

        if (_options.PlanFile.Length == 0)
        {
            view.Problems.Add(Problem(
                ProblemSeverity.Note,
                $"No plan file is configured. Set the {ConfigurationOptions.PlanFileVariable} environment "
                + "variable to see what the last deployment produced."));
        }
        else if (!File.Exists(_options.PlanFile))
        {
            view.Problems.Add(Problem(
                ProblemSeverity.Note,
                $"There is no plan at {_options.PlanFile} yet. It is written by the deployer's services command, "
                + "so this is what a machine looks like between being provisioned and being deployed."));
        }
    }

    /// <summary>
    /// Builds a problem about the addresses rather than about a file.
    /// </summary>
    /// <param name="severity">How badly it matters.</param>
    /// <param name="message">The sentence to show.</param>
    /// <returns>The problem.</returns>
    private static ManifestProblem Problem(ProblemSeverity severity, string message)
    {
        return new ManifestProblem
        {
            Severity = severity,
            Line = 0,
            Section = string.Empty,
            Key = string.Empty,
            Message = message,
        };
    }

    /// <summary>
    /// Names a manifest kind the way a request and a page spell it.
    /// </summary>
    /// <param name="kind">The kind to name.</param>
    /// <returns>The short name.</returns>
    private static string Describe(ManifestKind kind) => kind switch
    {
        ManifestKind.Requirements => "requirements",
        ManifestKind.Build => "build",
        ManifestKind.Services => "services",
        _ => "plan",
    };
}
