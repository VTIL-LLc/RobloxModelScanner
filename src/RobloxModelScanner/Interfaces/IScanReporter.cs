using RobloxModelScanner.Features.AssetAnalysis;
using RobloxModelScanner.Models;

namespace RobloxModelScanner.Interfaces;

/// <summary>
/// Publishes what the scanner found.
/// </summary>
/// <remarks>
/// <para>
/// Reporting is deliberately the only thing this interface does, and it deliberately cannot fail the
/// scan. A scan's value is the catalogue row it produces; the notification is a courtesy on top of that.
/// If reporting could throw back into the inspection, a rejected webhook would cost the detections that
/// were just computed, which is the wrong thing to lose. Implementations are therefore expected to
/// swallow their own transport failures and log them.
/// </para>
/// <para>
/// The two members are the two channels the operator watches, and they answer different questions. The
/// first says \"something with scripts exists\". The second says \"something scored highly enough that
/// its bytes were kept beside the scanner, here is where to look\". An asset can be either, both or
/// neither.
/// </para>
/// </remarks>
public interface IScanReporter
{
    /// <summary>
    /// Reports an asset that carries scripts.
    /// </summary>
    /// <param name="item">The asset's catalogue metadata.</param>
    /// <param name="detection">The analysis of its payload.</param>
    /// <param name="thumbnail">The thumbnail URL, or <see langword=\"null\"/>.</param>
    /// <param name="cancellationToken">A token used to abandon the report.</param>
    /// <returns>A task that completes when the report has been attempted.</returns>
    Task ReportFoundAsync(
        ToolboxItem item,
        DetectionResult detection,
        string? thumbnail,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reports an asset whose payload was kept.
    /// </summary>
    /// <param name="item">The asset's catalogue metadata.</param>
    /// <param name="detection">The analysis of its payload.</param>
    /// <param name="thumbnail">The thumbnail URL, or <see langword=\"null\"/>.</param>
    /// <param name="payloadBytes">The size of the kept payload.</param>
    /// <param name="cancellationToken">A token used to abandon the report.</param>
    /// <returns>A task that completes when the report has been attempted.</returns>
    Task ReportKeptAsync(
        ToolboxItem item,
        DetectionResult detection,
        string? thumbnail,
        int payloadBytes,
        CancellationToken cancellationToken);
}
