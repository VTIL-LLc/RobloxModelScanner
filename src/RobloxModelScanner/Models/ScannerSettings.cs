using RobloxModelScanner.Enums;

namespace RobloxModelScanner.Models;

/// <summary>
/// Every value the scanner reads to decide what to do and how hard to do it.
/// </summary>
/// <remarks>
/// <para>
/// The settings are read once, at start-up, and are immutable thereafter. Work that is already in
/// flight cannot be reconfigured halfway through, and a value that changed under a running scan would
/// produce a catalogue written to two different rules, so there is nothing to gain from making them
/// live and a great deal of confusion to avoid.
/// </para>
/// <para>
/// They live in one type rather than being read at their point of use because the alternative --
/// static fields scattered through the program -- makes it impossible to say what the scanner depends
/// on without reading all of it, and makes a second entry point, such as the backfill, impossible to
/// arm with anything but the ambient values.
/// </para>
/// </remarks>
public sealed record ScannerSettings
{
    /// <summary>
    /// The pass to perform.
    /// </summary>
    public required ScannerMode Mode { get; init; }

    /// <summary>
    /// The first identifier of the next sweep batch.
    /// </summary>
    /// <remarks>
    /// This is the sweep's own cursor and it is written back to the environment file as the sweep
    /// advances, so an interrupted run resumes where it stopped rather than re-walking from the
    /// beginning. The identifier space above the starting point is far too large to re-walk, which
    /// makes the cursor the difference between resuming and starting over.
    /// </remarks>
    public required long StartId { get; init; }

    /// <summary>
    /// How many identifiers one details request asks about.
    /// </summary>
    /// <remarks>
    /// Roblox accepts batches comfortably larger than this, but a batch is also the unit of loss when a
    /// request fails, and a large batch makes the response large enough to spend real time parsing.
    /// </remarks>
    public required int BatchSize { get; init; }

    /// <summary>
    /// How many details requests may be in flight at once.
    /// </summary>
    public required int ScanConcurrency { get; init; }

    /// <summary>
    /// How many assets may be downloaded and analysed at once.
    /// </summary>
    /// <remarks>
    /// This is separate from, and much larger than, <see cref="ScanConcurrency"/>, because the two
    /// stages have different costs: finding candidates is a handful of large responses, while
    /// inspecting one is a small download and a scan of it. The split lets the scanner keep the
    /// bandwidth busy without issuing more catalogue requests than Roblox will tolerate.
    /// </remarks>
    public required int AssetConcurrency { get; init; }

    /// <summary>
    /// The least time allowed between two catalogue requests.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A sweep at full speed asks the catalogue about a block of identifiers as fast as the answers
    /// arrive, and the endpoint throttles that: a run against the live service is refused after roughly
    /// two requests a second, which costs the sweep a retry and a ten-second wait for every refusal. A
    /// pace below that limit asks fewer questions in total, because it is never refused and never repeats
    /// a block.
    /// </para>
    /// <para>
    /// The default is no pacing, which is the previous revision's behaviour and the fastest a run can go.
    /// The limit is not a documented figure and it may differ per account, so a value chosen here would
    /// be a guess; the operator can measure it by watching for the refusal in the log.
    /// </para>
    /// </remarks>
    public required TimeSpan RequestInterval { get; init; }

    /// <summary>
    /// The longest a change may sit unsaved before the catalogue is rewritten.
    /// </summary>
    /// <remarks>
    /// The catalogue is a single JSON array, so saving it means rewriting all of it. Saving per model is
    /// therefore quadratic in the size of the document and is what made the previous revision spend its
    /// time serialising. Saving on a timer bounds the loss from a crash to this many seconds of work
    /// while keeping the write volume proportional to the scan's rate rather than to its history.
    /// </remarks>
    public required TimeSpan FlushInterval { get; init; }

    /// <summary>
    /// The number of unsaved rows that forces the catalogue to be rewritten early.
    /// </summary>
    /// <remarks>
    /// The timer alone would let a burst of findings accumulate for a whole interval; this bounds the
    /// loss in rows as well as in time, so a fast-moving sweep cannot discard very much.
    /// </remarks>
    public required int FlushRows { get; init; }

    /// <summary>
    /// The score at or above which an asset is treated as worth keeping.
    /// </summary>
    /// <remarks>
    /// Reaching this score both saves the payload beside the scanner and reports it to the download
    /// channel, so the threshold is the line between "recorded" and "brought back for a look".
    /// </remarks>
    public required double KeepScore { get; init; }

    /// <summary>
    /// Whether a payload at or above <see cref="KeepScore"/> is written to disk.
    /// </summary>
    public required bool KeepModels { get; init; }

    /// <summary>
    /// The absolute directory that kept payloads are written into.
    /// </summary>
    public required string ModelDirectory { get; init; }

    /// <summary>
    /// The absolute path of the catalogue database.
    /// </summary>
    public required string CataloguePath { get; init; }

    /// <summary>
    /// The absolute path of the control file the API writes and the scanner reads.
    /// </summary>
    public required string ControlPath { get; init; }

    /// <summary>
    /// The webhook that receives every asset carrying scripts.
    /// </summary>
    public required string FoundWebhook { get; init; }

    /// <summary>
    /// The webhook that receives assets at or above <see cref="KeepScore"/>.
    /// </summary>
    public required string DownloadWebhook { get; init; }

    /// <summary>
    /// The session cookie used for the asset delivery and toolbox endpoints.
    /// </summary>
    public required string RobloxCookie { get; init; }

    /// <summary>
    /// The file the session cookie is published to for the services that need it.
    /// </summary>
    /// <remarks>
    /// The scanner is the only program here that holds a Roblox session, and the API needs the same
    /// session to resolve a download. This names the file it is written to. It is empty when nothing
    /// named one, in which case the scanner uses its own cookie and publishes nothing.
    /// </remarks>
    public required string RobloxCookieFilePath { get; init; }

    /// <summary>
    /// The creators whose assets are ignored entirely.
    /// </summary>
    /// <remarks>
    /// These are the accounts whose work is either already known or is deliberately excluded, so the
    /// filter is applied before an asset is downloaded rather than after it is analysed. The default set
    /// is preserved from the previous revision, which carried it in the source; it is a setting now
    /// because a list of account identifiers written into a program is a list nobody can change without
    /// rebuilding it.
    /// </remarks>
    public required IReadOnlySet<long> IgnoredCreators { get; init; }

    /// <summary>
    /// Whether per-asset progress is written to the log.
    /// </summary>
    /// <remarks>
    /// Every asset produces a line, so on a bulk backfill the detailed log is orders of magnitude
    /// larger than the summary. It is off by default for that reason and can be turned on when a
    /// specific asset's outcome has to be traced.
    /// </remarks>
    public required bool Verbose { get; init; }
}
