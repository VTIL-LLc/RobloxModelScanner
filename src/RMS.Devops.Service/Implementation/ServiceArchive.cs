using System.Globalization;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using RMS.Devops.Service.Enums;
using RMS.Devops.Service.Interfaces;
using RMS.Devops.Service.Models;
using RMS.Devops.Service.Options;
using RMS.ServiceModel.Enums;
using RMS.ServiceModel.Implementation;
using RMS.ServiceModel.Models;

namespace RMS.Devops.Service.Implementation;

/// <summary>
/// Reads the plan and the run store, and answers everything the site's pages ask about this machine.
/// </summary>
/// <remarks>
/// <para>
/// There is no index of its own. A listing walks the log root and reads the small files the supervisor leaves
/// there, which means the service has no state to keep in step with the supervisor and cannot report a run that
/// no longer exists. The cost of that choice is a directory walk per request, and the answer to it is the two
/// caches below rather than a database: one for parsed records, and one that keeps the plan until the file it
/// came from actually changes.
/// </para>
/// <para>
/// The plan is re-read when its identity changes, not on a timer. A deployment rewrites the plan and then starts
/// the services it describes, so a reader that only noticed on a timer would spend part of every deployment
/// showing a machine that no longer exists.
/// </para>
/// <para>
/// Every path this class hands to the file system is built from a validated component. A service name comes
/// either from the plan -- which the deployer and installer both validate -- or from a directory it just
/// enumerated, and a run identifier is checked against the layout's own rule. Neither is ever taken from a
/// query string without passing through that check, which is what stops a request for
/// <c>../../Windows/System32</c> from being a request at all.
/// </para>
/// </remarks>
public sealed class ServiceArchive : IServiceArchive
{
    /// <summary>The number of run records the plan state tolerates before it is rebuilt.</summary>
    private const int MaximumRecentRunsPerService = 200;

    private readonly DevopsOptions _options;
    private readonly ILogger<ServiceArchive> _logger;
    private readonly RunRecordCache _cache = new();
    private readonly object _gate = new();

    private PlanState? _state;

    /// <summary>
    /// Initializes a new instance of the <see cref="ServiceArchive"/> class.
    /// </summary>
    /// <param name="options">The bound DevOps options, which name the plan.</param>
    /// <param name="logger">The logger used to report a plan that could not be read.</param>
    public ServiceArchive(IOptions<DevopsOptions> options, ILogger<ServiceArchive> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public bool IsAvailable => Current().Loaded;

    /// <inheritdoc />
    public string? UnavailableReason => Current().Loaded ? null : Current().Reason;

    /// <inheritdoc />
    public string PlanFile => Current().PlanPath;

    /// <inheritdoc />
    public string LogRoot => Current().LogRoot;

    /// <inheritdoc />
    public string OutputRoot => Current().OutputRoot;

    /// <inheritdoc />
    public IReadOnlyList<ServiceSnapshot> ListServices(int recentRuns)
    {
        PlanState state = Current();

        Dictionary<string, ServicePlan> planned = state.Loaded
            ? new Dictionary<string, ServicePlan>(state.Services, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, ServicePlan>(StringComparer.OrdinalIgnoreCase);

        Dictionary<string, string> recorded = state.Loaded
            ? DiscoverRunDirectories(state.LogRoot)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        HashSet<string> names = new(planned.Keys, StringComparer.OrdinalIgnoreCase);
        names.UnionWith(recorded.Keys);

        List<ServiceSnapshot> snapshots = [];

        foreach (string name in names)
        {
            planned.TryGetValue(name, out ServicePlan? plan);
            recorded.TryGetValue(name, out string? directory);
            snapshots.Add(BuildSnapshot(name, plan, directory, state.LogRoot, Math.Clamp(recentRuns, 0, MaximumRecentRunsPerService)));
        }

        snapshots.Sort(static (left, right) =>
        {
            DateTimeOffset leftActivity = left.LatestCreated ?? DateTimeOffset.MinValue;
            DateTimeOffset rightActivity = right.LatestCreated ?? DateTimeOffset.MinValue;

            int byActivity = rightActivity.CompareTo(leftActivity);
            if (byActivity != 0) return byActivity;

            return string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
        });

        return snapshots;
    }

    /// <inheritdoc />
    public ServiceSnapshot? FindService(string name)
    {
        if (!IsSafeComponent(name)) return null;

        PlanState state = Current();

        state.Services.TryGetValue(name, out ServicePlan? plan);
        string? directory = null;

        if (state.Loaded)
            DiscoverRunDirectories(state.LogRoot).TryGetValue(name, out directory);

        if (plan is null && directory is null) return null;

        return BuildSnapshot(name, plan, directory, state.LogRoot, MaximumRecentRunsPerService);
    }

    /// <inheritdoc />
    public RunPage ListRuns(string? service, int skip, int take)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(skip);

        int size = take > 0 ? Math.Min(take, _options.PageSize) : _options.PageSize;
        PlanState state = Current();

        if (!state.Loaded)
        {
            return new RunPage
            {
                Service = service ?? string.Empty,
                Skip = skip,
                Take = size,
                Total = 0,
            };
        }

        List<RunSnapshot> runs = [];

        if (service is null)
        {
            foreach ((string name, string directory) in DiscoverRunDirectories(state.LogRoot))
                runs.AddRange(ReadRuns(name, directory));
        }
        else
        {
            if (!IsSafeComponent(service)) return Page(service, skip, size, []);

            Dictionary<string, string> directories = DiscoverRunDirectories(state.LogRoot);

            if (directories.TryGetValue(service, out string? directory))
                runs.AddRange(ReadRuns(service, directory));
        }

        runs.Sort(static (left, right) =>
        {
            int byCreated = right.Created.CompareTo(left.Created);
            if (byCreated != 0) return byCreated;

            int byService = string.Compare(left.Service, right.Service, StringComparison.OrdinalIgnoreCase);
            if (byService != 0) return byService;

            return string.CompareOrdinal(right.Run, left.Run);
        });

        return Page(service, skip, size, runs);
    }

    /// <inheritdoc />
    public RunDetail? FindRun(string service, string runId)
    {
        if (!IsSafeComponent(service) || !RunStoreLayout.IsValidRunId(runId)) return null;

        PlanState state = Current();
        if (!state.Loaded) return null;

        if (!DiscoverRunDirectories(state.LogRoot).TryGetValue(service, out string? directory)) return null;

        string recordPath = Path.Combine(directory, runId + RunStoreLayout.RecordExtension);
        FileInfo record = new(recordPath);

        if (!record.Exists) return null;

        RunRecord loaded = _cache.Read(recordPath, new RunRecordCache.Stamp(record.LastWriteTimeUtc, record.Length))
                           ?? throw new InvalidDataException($"The run record '{recordPath}' could not be read.");

        FileInfo log = new(Path.Combine(directory, runId + RunStoreLayout.LogExtension));
        long logLength = log.Exists ? log.Length : 0;

        int tailBytes = (int)Math.Min(_options.MaximumLogBytes, 64L * 1024L);
        string tail = string.Empty;
        bool complete = true;

        if (logLength > 0)
        {
            (tail, _, _, complete) = ReadSlice(log, Math.Max(0, logLength - tailBytes), tailBytes);
        }

        return new RunDetail
        {
            Summary = RunSnapshot.From(loaded, logLength),
            Command = loaded.Command,
            ProcessId = loaded.ProcessId,
            LogFile = log.Exists ? log.FullName : string.Empty,
            LogTail = tail,
            LogTruncated = !complete || logLength > tailBytes,
        };
    }

    /// <summary>
    /// Assembles one service's snapshot from whatever is known about it.
    /// </summary>
    /// <param name="name">The service's name.</param>
    /// <param name="plan">The plan entry, when the service is in the current plan.</param>
    /// <param name="directory">The run store directory, when the service has ever run.</param>
    /// <param name="logRoot">The log root the plan names.</param>
    /// <param name="recentRuns">How many recent runs to attach.</param>
    /// <returns>The snapshot.</returns>
    /// <remarks>
    /// The plan wins wherever both sources describe the same field. It is the statement of what the deployment
    /// intends, and a service whose executable has just been moved must be reported at its new path even though
    /// its last run says otherwise. The run store fills in what the plan cannot know: how many runs there have
    /// been, when the last one started, and how it ended.
    /// </remarks>
    private ServiceSnapshot BuildSnapshot(
        string name,
        ServicePlan? plan,
        string? directory,
        string logRoot,
        int recentRuns)
    {
        List<RunSnapshot> runs = directory is not null ? ReadRuns(name, directory) : [];
        runs.Sort(static (left, right) => right.Created.CompareTo(left.Created));

        RunSnapshot? latest = runs.Count > 0 ? runs[0] : null;
        IndexFacts index = directory is not null ? ReadIndex(directory) : IndexFacts.Empty;

        return new ServiceSnapshot
        {
            Name = name,
            PlanId = plan?.Id ?? string.Empty,
            IsInPlan = plan is not null,
            Description = Prefer(plan?.Description, index.Description),
            Command = Prefer(plan?.DescribeCommand(), index.Command),
            WorkingDirectory = Prefer(plan?.WorkingDirectory, index.WorkingDirectory),
            LogDirectory = directory ?? (plan is not null ? plan.LogDirectoryFor(logRoot) : string.Empty),
            Executable = plan?.Executable ?? string.Empty,
            Website = plan?.Website ?? string.Empty,
            Port = plan?.Port ?? string.Empty,
            Restart = Prefer(plan?.RestartKeyword, index.Restart),
            RetainedRuns = index.RetainedRuns,
            RunCount = runs.Count,
            State = latest is null ? FleetState.Idle : RunSnapshot.StateOf(RunRecord.ParseStatus(latest.Status)),
            LatestRun = latest?.Run,
            LatestStatus = latest?.Status ?? string.Empty,
            LatestCreated = latest?.Created,
            LatestStarted = latest?.Started,
            LatestCompleted = latest?.Completed,
            LatestExitCode = latest?.ExitCode,
            LatestAttempts = latest?.Attempts ?? 0,
            LatestRuntimeText = latest?.RuntimeText ?? string.Empty,
            LatestLogBytes = latest?.LogBytes ?? 0,
            RecentRuns = [.. runs.Take(recentRuns)],
        };
    }

    /// <summary>
    /// Reads a service's index file.
    /// </summary>
    /// <param name="directory">The service's run directory.</param>
    /// <returns>The facts the index carries, or an empty set when it cannot be read.</returns>
    private IndexFacts ReadIndex(string directory)
    {
        try
        {
            IniDocument document = IniDocument.Load(Path.Combine(directory, RunStoreLayout.IndexFileName));
            IniSection? section = document.FindSection(RunStoreLayout.IndexSectionName);

            if (section is null) return IndexFacts.Empty;

            return new IndexFacts(
                section.Get(RunStoreLayout.IndexDescriptionKey, string.Empty),
                section.Get(RunStoreLayout.IndexCommandKey, string.Empty),
                section.Get(RunStoreLayout.IndexWorkingDirectoryKey, string.Empty),
                section.Get(RunStoreLayout.IndexRestartKey, string.Empty),
                section.GetInt32(RunStoreLayout.IndexRetainedRunsKey, 0));
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException)
        {
            _logger.LogDebug(exception, "The run store index under {Directory} could not be read.", directory);
            return IndexFacts.Empty;
        }
    }

    /// <summary>
    /// Returns the first of two values that is not blank.
    /// </summary>
    /// <param name="preferred">The value that wins when it carries anything.</param>
    /// <param name="fallback">The value used otherwise.</param>
    /// <returns>The chosen value.</returns>
    private static string Prefer(string? preferred, string? fallback) =>
        string.IsNullOrWhiteSpace(preferred) ? fallback ?? string.Empty : preferred;

    /// <summary>
    /// Reads every run recorded in one directory.
    /// </summary>
    /// <param name="service">The service's name.</param>
    /// <param name="directory">The service's run directory.</param>
    /// <returns>The runs, unordered.</returns>
    private List<RunSnapshot> ReadRuns(string service, string directory)
    {
        List<RunSnapshot> runs = [];

        FileInfo[] files = EnumerateFiles(directory);
        if (files.Length == 0) return runs;

        Dictionary<string, long> logSizes = new(StringComparer.OrdinalIgnoreCase);

        foreach (FileInfo file in files)
        {
            if (string.Equals(file.Extension, RunStoreLayout.LogExtension, StringComparison.OrdinalIgnoreCase))
                logSizes[Path.GetFileNameWithoutExtension(file.Name)] = file.Length;
        }

        foreach (FileInfo file in files)
        {
            if (!string.Equals(file.Extension, RunStoreLayout.RecordExtension, StringComparison.OrdinalIgnoreCase))
                continue;

            string runId = Path.GetFileNameWithoutExtension(file.Name);
            if (!RunStoreLayout.IsValidRunId(runId)) continue;

            RunRecord? record = _cache.Read(
                file.FullName,
                new RunRecordCache.Stamp(file.LastWriteTimeUtc, file.Length));

            if (record is null) continue;

            logSizes.TryGetValue(runId, out long logBytes);
            runs.Add(RunSnapshot.From(record, logBytes));
        }

        return runs;
    }

    /// <summary>
    /// Wraps a run list in a page.
    /// </summary>
    /// <param name="service">The service the page was drawn from.</param>
    /// <param name="skip">How many runs were skipped.</param>
    /// <param name="take">How many runs the page holds.</param>
    /// <param name="runs">The runs, already ordered newest first.</param>
    /// <returns>The page.</returns>
    private static RunPage Page(string? service, int skip, int take, List<RunSnapshot> runs)
    {
        return new RunPage
        {
            Service = service ?? string.Empty,
            Skip = skip,
            Take = take,
            Total = runs.Count,
            Runs = skip >= runs.Count ? [] : [.. runs.GetRange(skip, Math.Min(take, runs.Count - skip))],
        };
    }

    /// <summary>
    /// Finds the run directories below the log root.
    /// </summary>
    /// <param name="logRoot">The log root.</param>
    /// <returns>A map from service name to run directory.</returns>
    /// <remarks>
    /// A directory counts as a run store only when it holds an index file. Anything else under the log root was
    /// put there by someone else, and reporting it as a service would mean a listing that changed shape
    /// depending on what an operator had left lying around.
    /// </remarks>
    private Dictionary<string, string> DiscoverRunDirectories(string logRoot)
    {
        Dictionary<string, string> directories = new(StringComparer.OrdinalIgnoreCase);
        if (logRoot.Length == 0) return directories;

        string[] found;

        try
        {
            found = Directory.GetDirectories(logRoot);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "The log root {LogRoot} could not be enumerated.", logRoot);
            return directories;
        }

        foreach (string directory in found)
        {
            string name = Path.GetFileName(directory);
            if (!IsSafeComponent(name)) continue;
            if (!File.Exists(Path.Combine(directory, RunStoreLayout.IndexFileName))) continue;

            directories[name] = directory;
        }

        return directories;
    }

    /// <summary>
    /// Enumerates a directory, treating an unreadable one as an empty one.
    /// </summary>
    /// <param name="directory">The directory to enumerate.</param>
    /// <returns>The files, or an empty array.</returns>
    private FileInfo[] EnumerateFiles(string directory)
    {
        try
        {
            return new DirectoryInfo(directory).GetFiles();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "The run directory {Directory} could not be enumerated.", directory);
            return [];
        }
    }

    /// <inheritdoc />
    public string? FindRunDirectory(string service)
    {
        if (!IsSafeComponent(service)) return null;

        PlanState state = Current();
        if (!state.Loaded) return null;

        return DiscoverRunDirectories(state.LogRoot).TryGetValue(service, out string? directory)
            ? directory
            : null;
    }

    /// <summary>
    /// Reads a byte range from a file.
    /// </summary>
    /// <param name="file">The file to read.</param>
    /// <param name="offset">The byte offset to start at.</param>
    /// <param name="count">The greatest number of bytes to read.</param>
    /// <returns>
    /// The decoded text, the offset read from, how many bytes were read and whether the end of the file was
    /// reached.
    /// </returns>
    /// <remarks>
    /// The file is opened with sharing that permits the writer to keep appending. A log that is being written
    /// while it is read may therefore yield one more byte than it had when the read began; that is the price of
    /// not locking a running service out of its own log, and it costs the caller nothing, because the text it
    /// receives was in the file either way.
    /// </remarks>
    internal static (string Text, long Offset, int Bytes, bool Complete) ReadSlice(
        FileInfo file,
        long offset,
        int count)
    {
        using FileStream stream = new(
            file.FullName,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 4096,
            FileOptions.SequentialScan);

        long start = Math.Clamp(offset, 0, stream.Length);
        stream.Seek(start, SeekOrigin.Begin);

        int available = (int)Math.Min(count, stream.Length - start);
        if (available <= 0) return (string.Empty, start, 0, start >= stream.Length);

        byte[] buffer = new byte[available];
        int read = stream.ReadAtLeast(buffer, available, throwOnEndOfStream: false);

        string text = System.Text.Encoding.UTF8.GetString(buffer, 0, read);

        return (text, start, read, start + read >= stream.Length);
    }

    /// <summary>
    /// Determines whether a string may be used as a path component.
    /// </summary>
    /// <param name="value">The candidate.</param>
    /// <returns><see langword="true"/> when the value is usable.</returns>
    /// <remarks>
    /// This is the single gate between a query string and the file system. Letters, digits, hyphens, underscores
    /// and dots are admitted, with the two directory entries refused by name, so no value that passes can name
    /// anything other than a child of the directory it is joined to.
    /// </remarks>
    private static bool IsSafeComponent(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 128) return false;
        if (value is "." or "..") return false;

        foreach (char character in value)
        {
            if (char.IsAsciiLetterOrDigit(character)) continue;
            if (character is '-' or '_' or '.') continue;
            return false;
        }

        return true;
    }

    /// <summary>
    /// Returns the current plan state, re-reading the plan when the file has changed.
    /// </summary>
    /// <returns>The state.</returns>
    private PlanState Current()
    {
        string path = _options.PlanFile;
        (bool exists, DateTime written, long length) = Fingerprint(path);

        lock (_gate)
        {
            if (_state is PlanState cached
                && string.Equals(cached.PlanPath, path, StringComparison.OrdinalIgnoreCase)
                && cached.Exists == exists
                && (!exists || (cached.Written == written && cached.Length == length)))
            {
                return cached;
            }

            _state = Probe(path, exists, written, length);
            return _state;
        }
    }

    /// <summary>
    /// Reads a file's identity.
    /// </summary>
    /// <param name="path">The file to stat.</param>
    /// <returns>Whether the file exists, when it was last written and its length.</returns>
    private static (bool Exists, DateTime Written, long Length) Fingerprint(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return (false, default, 0);

        try
        {
            FileInfo file = new(path);
            return file.Exists ? (true, file.LastWriteTimeUtc, file.Length) : (false, default, 0);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return (false, default, 0);
        }
    }

    /// <summary>
    /// Reads the plan into a state, reporting the reason when it cannot be read.
    /// </summary>
    /// <param name="path">The plan's path.</param>
    /// <param name="exists">Whether the file exists.</param>
    /// <param name="written">When the file was last written.</param>
    /// <param name="length">The file's length.</param>
    /// <returns>The state.</returns>
    private PlanState Probe(string path, bool exists, DateTime written, long length)
    {
        PlanState state = new()
        {
            PlanPath = path,
            Exists = exists,
            Written = written,
            Length = length,
        };

        if (string.IsNullOrWhiteSpace(path))
        {
            state.Reason = $"No plan file is configured. Set the {DevopsOptions.PlanFileVariable} environment variable.";
            return state;
        }

        if (!exists)
        {
            state.Reason = $"The plan file '{path}' has not been written yet.";
            return state;
        }

        ServicePlanSet set;

        try
        {
            set = RMS.ServiceModel.Implementation.PlanFile.Load(path);
            set.ValidateSettings();
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or InvalidOperationException)
        {
            state.Reason = $"The plan file '{path}' could not be read: {exception.Message}";
            _logger.LogWarning(exception, "The plan file {PlanFile} could not be read.", path);
            return state;
        }

        foreach (ServicePlan service in set.Services)
        {
            string name = service.EffectiveName;
            if (!IsSafeComponent(name)) continue;

            state.Services[name] = service;
        }

        state.Loaded = true;
        state.LogRoot = set.LogDirectory;
        state.OutputRoot = set.OutputRoot;

        _logger.LogInformation(
            "Loaded {Count} services for platform {Platform} from {PlanFile}.",
            state.Services.Count,
            set.Platform,
            path);

        return state;
    }

    /// <summary>
    /// The plan, as the archive last read it.
    /// </summary>
    /// <remarks>
    /// The file's identity is part of the state so that a re-read can be avoided cheaply. Without it a request
    /// would have to parse the plan to find out whether the plan had changed, which is the whole cost the cache
    /// exists to remove.
    /// </remarks>
    private sealed class PlanState
    {
        /// <summary>Gets or sets a value indicating whether the plan was read.</summary>
        public bool Loaded { get; set; }

        /// <summary>Gets or sets the reason the plan could not be read.</summary>
        public string Reason { get; set; } = string.Empty;

        /// <summary>Gets or sets the plan's path.</summary>
        public string PlanPath { get; set; } = string.Empty;

        /// <summary>Gets or sets a value indicating whether the plan file exists.</summary>
        public bool Exists { get; set; }

        /// <summary>Gets or sets when the plan file was last written.</summary>
        public DateTime Written { get; set; }

        /// <summary>Gets or sets the plan file's length.</summary>
        public long Length { get; set; }

        /// <summary>Gets or sets the log root the plan names.</summary>
        public string LogRoot { get; set; } = string.Empty;

        /// <summary>Gets or sets the publish root the plan names.</summary>
        public string OutputRoot { get; set; } = string.Empty;

        /// <summary>Gets the plan's services, keyed by effective name.</summary>
        public Dictionary<string, ServicePlan> Services { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The facts a run store's index file carries.
    /// </summary>
    /// <param name="Description">The service's description.</param>
    /// <param name="Command">The command line the supervisor runs.</param>
    /// <param name="WorkingDirectory">The directory the service runs in.</param>
    /// <param name="Restart">The restart policy.</param>
    /// <param name="RetainedRuns">How many runs are kept.</param>
    /// <remarks>
    /// The index is the only source for a service that has been removed from the plan but whose history is
    /// still on disk, which is exactly the case where an operator most wants to see what it used to be.
    /// </remarks>
    private readonly record struct IndexFacts(
        string Description,
        string Command,
        string WorkingDirectory,
        string Restart,
        int RetainedRuns)
    {
        /// <summary>An index that carries nothing.</summary>
        public static IndexFacts Empty { get; } = new(string.Empty, string.Empty, string.Empty, string.Empty, 0);
    }
}
