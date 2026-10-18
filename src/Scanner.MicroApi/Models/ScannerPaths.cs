namespace Scanner.MicroApi.Models;

/// <summary>
/// The resolved, absolute file locations the API operates on.
/// </summary>
/// <remarks>
/// Resolving the paths once, at start-up, removes a whole class of errors: the API can no longer
/// read a relative catalogue from an unpredictable working directory, and the control file
/// is guaranteed to sit beside the catalogue the scanner actually writes.
/// </remarks>
/// <param name="CataloguePath">The absolute path to the scanner's catalogue database.</param>
/// <param name="IndexPath">The absolute path to the SQLite index database.</param>
/// <param name="ControlPath">The absolute path to the scanner's <c>control.json</c> file.</param>
public sealed record ScannerPaths(string CataloguePath, string IndexPath, string ControlPath);
