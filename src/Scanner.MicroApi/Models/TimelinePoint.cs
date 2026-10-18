namespace Scanner.MicroApi.Models;

/// <summary>
/// The number of models first recorded in a single period.
/// </summary>
/// <remarks>
/// The period is a calendar month formatted as <c>yyyy-MM</c>. Months are used rather than days
/// because the scanner indexes assets in identifier order rather than chronologically, so a daily
/// series would be dominated by noise from how the identifier ranges are walked.
/// </remarks>
/// <param name="Period">The calendar month, formatted as <c>yyyy-MM</c>.</param>
/// <param name="Count">The number of models recorded in the period.</param>
public sealed record TimelinePoint(string Period, int Count);
