namespace RMS.Devops.Service.Models;

/// <summary>
/// One point of the catalogue's publication timeline.
/// </summary>
/// <remarks>
/// The period is the year and month of the asset's date, taken from the date column of the catalogue rather
/// than from when the scanner saw it. Sorting by it therefore shows when assets were published, which is the
/// question a growth curve is being asked; sorting by the scan instead would show when the scanner happened to
/// be run, which is a curve about this machine.
/// </remarks>
public sealed class PeriodStatistic
{
    /// <summary>
    /// Gets the period, formatted as <c>yyyy-MM</c>.
    /// </summary>
    public required string Period { get; init; }

    /// <summary>
    /// Gets how many catalogue rows fall in the period.
    /// </summary>
    public long Models { get; init; }
}
