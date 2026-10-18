namespace RMS.Configuration.Service.Enums;

/// <summary>
/// What one line of a manifest is.
/// </summary>
/// <remarks>
/// The classification exists so that the console can show a manifest as it is written rather than as it is
/// understood. An operator editing a file needs to see the comments, the blank lines and the order, because
/// those are the things a round trip through a model would quietly discard -- and a console that reformatted
/// the file it was pointed at would be a console nobody would dare point at anything.
/// </remarks>
public enum ManifestLineKind
{
    /// <summary>
    /// An empty line, kept so that the rendered document matches the file.
    /// </summary>
    Blank,

    /// <summary>
    /// A comment, beginning with <c>#</c> or <c>;</c>.
    /// </summary>
    Comment,

    /// <summary>
    /// A <c>[section]</c> header.
    /// </summary>
    Header,

    /// <summary>
    /// A <c>key = value</c> pair. A bare word is one of these with an empty value.
    /// </summary>
    Entry,

    /// <summary>
    /// A line that is neither, and that the deployer would refuse. Carries the complaint.
    /// </summary>
    Malformed,
}
