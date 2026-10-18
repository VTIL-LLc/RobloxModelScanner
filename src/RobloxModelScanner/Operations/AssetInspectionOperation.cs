using Microsoft.Extensions.Logging;

using RobloxModelScanner.Enums;
using RobloxModelScanner.Features.AssetAnalysis;
using RobloxModelScanner.Interfaces;
using RobloxModelScanner.Models;

namespace RobloxModelScanner.Operations;

/// <summary>
/// Downloads one asset, analyses it and produces its catalogue row.
/// </summary>
/// <remarks>
/// <para>
/// This is the only place an asset's outcome is decided, and both passes funnel through it. A sweep and
/// a repair differ in what they do with the row and in whether they announce it, not in how the row is
/// built; splitting the decision across two implementations would guarantee that a model found by one
/// pass and re-examined by the other disagreed about its own contents.
/// </para>
/// <para>
/// The operation performs no bookkeeping of its own. It returns the row and the caller commits it, which
/// is what lets the same code append during a sweep and replace during a repair.
/// </para>
/// <para>
/// It is safe to run many instances of at once. It holds no mutable state; the settings, the source and
/// the reporter are all shared and all safe to share.
/// </para>
/// </remarks>
public sealed class AssetInspectionOperation
{
    /// <summary>
    /// The extension given to a kept payload.
    /// </summary>
    /// <remarks>
    /// The payload is the binary form of a Roblox model as the client receives it, which is what the
    /// extension denotes. It is kept so that a suspicious asset can be opened and read after the fact
    /// rather than only described.
    /// </remarks>
    private const string PayloadExtension = ".rbxm";

    /// <summary>
    /// The number of bytes examined when deciding whether a payload is a JSON refusal.
    /// </summary>
    private const int ProbeBytes = 8;

    /// <summary>
    /// The source the payload and thumbnail are read from.
    /// </summary>
    private readonly IRobloxAssetSource _source;

    /// <summary>
    /// The reporter the outcome is published through.
    /// </summary>
    private readonly IScanReporter _reporter;

    /// <summary>
    /// The settings that carry the keep threshold and the directories.
    /// </summary>
    private readonly ScannerSettings _settings;

    /// <summary>
    /// The logger that records refusals and kept payloads.
    /// </summary>
    private readonly ILogger<AssetInspectionOperation> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AssetInspectionOperation"/> class.
    /// </summary>
    /// <param name="source">The source the payload and thumbnail are read from.</param>
    /// <param name="reporter">The reporter the outcome is published through.</param>
    /// <param name="settings">The settings that carry the keep threshold and the directories.</param>
    /// <param name="logger">The logger that records refusals and kept payloads.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
    public AssetInspectionOperation(
        IRobloxAssetSource source,
        IScanReporter reporter,
        ScannerSettings settings,
        ILogger<AssetInspectionOperation> logger)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(reporter);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(logger);

        _source = source;
        _reporter = reporter;
        _settings = settings;
        _logger = logger;
    }

    /// <summary>
    /// Examines one asset.
    /// </summary>
    /// <param name="item">The asset's catalogue metadata.</param>
    /// <param name="purpose">Why the asset is being examined, which decides whether the outcome is published.</param>
    /// <param name="cancellationToken">A token used to abandon the examination.</param>
    /// <returns>The row to record, or <see langword="null"/> when the asset could not be examined.</returns>
    /// <remarks>
    /// A null result is the ordinary outcome for an asset that has been moderated, deleted or made
    /// private. It means only that nothing new was learned; the caller leaves the catalogue alone rather
    /// than recording a row that says the asset is empty, because an unexamined asset and an asset with
    /// no scripts are different claims.
    /// </remarks>
    public async Task<CatalogueRecord?> InspectAsync(
        ToolboxItem item,
        InspectionPurpose purpose,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);

        AssetDownload download = await _source.DownloadAsync(item.ModelId, cancellationToken);
        if (!download.Succeeded || download.Bytes is null)
        {
            _logger.LogDebug(
                "Asset {ModelId} was not examined: {Reason}",
                item.ModelId,
                download.FailureReason);

            return null;
        }

        byte[] payload = download.Bytes;

        if (LooksLikeJson(payload))
        {
            // The delivery host answers a request it will not serve with a JSON document rather than with
            // bytes, and an asset of any kind is never a JSON document at its outermost level. Treating
            // the body as an asset would analyse an error message and record the result as a finding.
            _logger.LogDebug("Asset {ModelId} answered with a refusal document rather than a payload.", item.ModelId);
            return null;
        }

        DetectionResult detection = AssetAnalyzer.Analyze(payload);

        // The scanner's own marker scan is the primary evidence; the catalogue's claim is corroboration.
        // Scripts the analyzer found are certainly there, and a script the catalogue declares but the
        // markers do not show is still a script -- the marker set is a heuristic and can miss a container
        // written in a form it does not recognise, whereas Roblox knows what it stored.
        bool hasScripts = detection.HasScript || item.DeclaresScripts;

        bool keep = detection.Score >= _settings.KeepScore;
        string? thumbnail = hasScripts || keep
            ? await _source.GetThumbnailAsync(item.ModelId, cancellationToken)
            : null;

        if (keep && _settings.KeepModels) KeepPayload(item.ModelId, payload);

        if (purpose == InspectionPurpose.Discovery)
        {
            await PublishAsync(item, detection, thumbnail, keep ? payload.Length : 0, cancellationToken);
        }

        if (_settings.Verbose)
        {
            _logger.LogInformation(
                "Asset {ModelId} score {Score} scripts {Scripts} tags \"{Tags}\"",
                item.ModelId,
                detection.FormatScore(),
                hasScripts,
                detection.Tags);
        }

        return new CatalogueRecord(
            Model: item.ModelId,
            Date: item.CreatedUtc,
            Name: item.Name,
            Description: item.Description,
            CreatorName: item.CreatorName,
            CreatorId: item.CreatorId,
            HasScripts: hasScripts,
            Tags: detection.Tags,
            Score: detection.Score,
            AssetUrl: item.StoreAddress,
            Thumbnail: thumbnail);
    }

    /// <summary>
    /// Writes a kept payload beside the scanner.
    /// </summary>
    /// <param name="modelId">The asset identifier.</param>
    /// <param name="payload">The bytes to write.</param>
    /// <remarks>
    /// A payload that cannot be written is logged and otherwise ignored. The catalogue row, the score and
    /// the detection are the durable findings and they have already been computed; failing the whole
    /// inspection because a disk filled up would throw them away to protect a convenience copy.
    /// </remarks>
    private void KeepPayload(long modelId, byte[] payload)
    {
        string path = Path.Combine(
            _settings.ModelDirectory,
            string.Concat(modelId.ToString(System.Globalization.CultureInfo.InvariantCulture), PayloadExtension));

        try
        {
            Directory.CreateDirectory(_settings.ModelDirectory);
            File.WriteAllBytes(path, payload);
        }
        catch (IOException exception)
        {
            _logger.LogWarning(exception, "The payload for asset {ModelId} could not be saved.", modelId);
        }
        catch (UnauthorizedAccessException exception)
        {
            _logger.LogWarning(exception, "The payload for asset {ModelId} could not be saved.", modelId);
        }
    }

    /// <summary>
    /// Publishes an asset's outcome to the channels.
    /// </summary>
    /// <param name="item">The asset's catalogue metadata.</param>
    /// <param name="detection">The analysis of its payload.</param>
    /// <param name="thumbnail">The thumbnail URL, or <see langword="null"/>.</param>
    /// <param name="payloadBytes">The size of the kept payload, or zero when it was not kept.</param>
    /// <param name="cancellationToken">A token used to abandon the reports.</param>
    /// <returns>A task that completes when both reports have been attempted.</returns>
    private async Task PublishAsync(
        ToolboxItem item,
        DetectionResult detection,
        string? thumbnail,
        int payloadBytes,
        CancellationToken cancellationToken)
    {
        if (detection.HasScript || item.DeclaresScripts)
        {
            await _reporter.ReportFoundAsync(item, detection, thumbnail, cancellationToken);
        }

        if (payloadBytes > 0)
        {
            await _reporter.ReportKeptAsync(item, detection, thumbnail, payloadBytes, cancellationToken);
        }
    }

    /// <summary>
    /// Determines whether a payload is a JSON document rather than an asset.
    /// </summary>
    /// <param name="payload">The downloaded bytes.</param>
    /// <returns><see langword="true"/> when the payload opens with a JSON structure.</returns>
    /// <remarks>
    /// The test reads the first few bytes rather than parsing the document. A refusal is small and would
    /// parse either way, but a payload that is genuinely an asset can be megabytes, and handing it to a
    /// parser to discover that it is not JSON means reading all of it to learn something that the first
    /// non-whitespace byte already said.
    /// </remarks>
    private static bool LooksLikeJson(byte[] payload)
    {
        int start = payload.Length >= 3 && payload[0] == 0xEF && payload[1] == 0xBB && payload[2] == 0xBF
            ? 3
            : 0;

        int limit = Math.Min(payload.Length, start + ProbeBytes);
        for (int index = start; index < limit; index++)
        {
            byte value = payload[index];

            // Leading whitespace is skipped so that a document preceded by it is still recognised.
            if (value is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n') continue;

            return value is (byte)'{' or (byte)'[';
        }

        return false;
    }
}
