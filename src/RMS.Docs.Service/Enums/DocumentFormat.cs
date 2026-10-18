namespace RMS.Docs.Service.Enums;

/// <summary>
/// How a document's body is meant to be read.
/// </summary>
/// <remarks>
/// <para>
/// The format travels with the document rather than being chosen by the caller, because the caller is a page
/// renderer that has a single chance to get it right. A Markdown body rendered as plain text shows its own
/// punctuation; a plain text body rendered as Markdown loses any line that happens to begin with a punctuation
/// character a Markdown reader gives a meaning to.
/// </para>
/// <para>
/// It is published by name rather than as a number so that a reader of the API's own output can tell which is
/// which without a lookup table.
/// </para>
/// </remarks>
public enum DocumentFormat
{
    /// <summary>
    /// The body is Markdown and is meant to be rendered.
    /// </summary>
    Markdown,

    /// <summary>
    /// The body is plain text and is meant to be shown exactly as it is.
    /// </summary>
    Text
}
