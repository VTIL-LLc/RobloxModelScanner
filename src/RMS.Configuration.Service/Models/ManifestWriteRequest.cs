namespace RMS.Configuration.Service.Models;

/// <summary>
/// A request to write a manifest, or to check one without writing it.
/// </summary>
/// <remarks>
/// <para>
/// The whole file is sent rather than a change to it. These files are a hundred and twenty lines each, the
/// operator is looking at all of them, and a description of a diff would be a second syntax to get wrong on top
/// of the one being edited -- and one that could not express a comment.
/// </para>
/// <para>
/// The hash is the file's state when it was read. It is what makes two operators editing one file a refusal
/// rather than a silent last-writer-wins, and the refusal names the file's current hash so that the page can
/// offer to reload rather than making the operator guess what changed.
/// </para>
/// </remarks>
public sealed class ManifestWriteRequest
{
    /// <summary>
    /// Gets or sets the text the file should have.
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the SHA-256 of the file as the page read it.
    /// </summary>
    /// <remarks>
    /// Empty means "write regardless", which is accepted only when the file does not exist yet. A request that
    /// named no hash for a file that does exist is refused, because the alternative is a request that could
    /// clobber a change it never saw.
    /// </remarks>
    public string ExpectedSha256 { get; set; } = string.Empty;
}
