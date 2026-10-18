namespace RMS.Configuration.Service.Models;

/// <summary>
/// What happened to a write, or to a check that was asked not to write.
/// </summary>
/// <remarks>
/// A refusal inside this shape is a successful response. "The file is one line wrong" is a thing the page has
/// to render line by line, and delivering it as a transport failure would mean the page had to render an error
/// body as though it were a result. Only the guard and the hash precondition are answered as HTTP errors,
/// because those are the two cases where there is nothing useful to render.
/// </remarks>
public sealed class ManifestWriteResult
{
    /// <summary>
    /// Gets or sets a value indicating whether the file was written.
    /// </summary>
    public bool Written { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the request was to check rather than to write.
    /// </summary>
    public bool CheckedOnly { get; set; }

    /// <summary>
    /// Gets or sets the manifest's name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the absolute path the write was made to.
    /// </summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the SHA-256 of the file as it now stands.
    /// </summary>
    public string Sha256 { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the path of the copy of the previous contents, when one was made.
    /// </summary>
    public string BackupPath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a sentence describing what happened.
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the document as it stands after the request, so that a page can render the result of what
    /// it just did without a second round trip.
    /// </summary>
    public ManifestDocument? Document { get; set; }

    /// <summary>
    /// Gets or sets every problem found in the text that was sent.
    /// </summary>
    public List<ManifestProblem> Problems { get; set; } = [];
}
