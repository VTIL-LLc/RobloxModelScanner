namespace RMS.ServiceModel.Models;

/// <summary>
/// One <c>key = value</c> pair inside an <see cref="IniSection"/>.
/// </summary>
/// <param name="Key">The name to the left of the first <c>=</c>, trimmed.</param>
/// <param name="Value">Everything to the right of the first <c>=</c>, trimmed.</param>
/// <param name="LineNumber">The one-based line the pair was read from, for diagnostics.</param>
public readonly record struct IniEntry(string Key, string Value, int LineNumber);
