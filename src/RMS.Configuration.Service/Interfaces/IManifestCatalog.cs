using RMS.Configuration.Service.Enums;
using RMS.Configuration.Service.Models;

namespace RMS.Configuration.Service.Interfaces;

/// <summary>
/// Knows where the deployment's manifests are and reads them.
/// </summary>
/// <remarks>
/// <para>
/// A caller never supplies a path. It supplies the name of one of the four files this stack has, and the catalog
/// turns that into a configured directory plus a fixed file name. That is the whole of the traversal defence:
/// there is no string built from caller text, so there is nothing to sanitise and no filter that can be
/// forgotten.
/// </para>
/// <para>
/// The catalog is also where the repository root is resolved, once, so that every value an operator sees
/// expanded has been expanded against the same root.
/// </para>
/// </remarks>
public interface IManifestCatalog
{
    /// <summary>
    /// Gets the absolute path of the repository root in force.
    /// </summary>
    string RepositoryRoot { get; }

    /// <summary>
    /// Gets the absolute path of the directory holding the hand-written manifests.
    /// </summary>
    string ManifestDirectory { get; }

    /// <summary>
    /// Gets the absolute path of the generated plan file.
    /// </summary>
    string PlanFile { get; }

    /// <summary>
    /// Resolves a manifest's name to the kind it denotes.
    /// </summary>
    /// <param name="name">The name the caller used, for example <c>requirements</c>.</param>
    /// <param name="kind">The kind, when the name is one this stack knows.</param>
    /// <returns><see langword="true"/> when the name denotes a manifest.</returns>
    bool TryResolve(string name, out ManifestKind kind);

    /// <summary>
    /// Gets the file name the deployer looks for, for a kind.
    /// </summary>
    /// <param name="kind">The kind to name.</param>
    /// <returns>The file name.</returns>
    string FileNameFor(ManifestKind kind);

    /// <summary>
    /// Gets the absolute path of a kind's file.
    /// </summary>
    /// <param name="kind">The kind to locate.</param>
    /// <returns>The absolute path, whether or not the file exists.</returns>
    string PathFor(ManifestKind kind);

    /// <summary>
    /// Reports where every manifest is and what state it is in.
    /// </summary>
    /// <returns>The overview.</returns>
    ManifestListView List();

    /// <summary>
    /// Reads one manifest and checks it.
    /// </summary>
    /// <param name="kind">The manifest to read.</param>
    /// <returns>The document, which describes itself as absent when the file is not there.</returns>
    /// <remarks>
    /// A missing file is an ordinary document rather than an exception, because the console's most useful answer
    /// on a freshly checked-out machine is which of the four files are not there yet.
    /// </remarks>
    ManifestDocument Read(ManifestKind kind);

    /// <summary>
    /// Parses text as though it were a manifest, without touching the file system, and checks it.
    /// </summary>
    /// <param name="kind">The kind the text is being checked against.</param>
    /// <param name="text">The text to parse.</param>
    /// <returns>The parsed document, marked as not existing on disk.</returns>
    /// <remarks>
    /// This is what a check runs against, and what a write validates before it commits. Parsing the text that was
    /// sent, rather than re-reading the file afterwards, is what makes the problems an operator is shown the
    /// problems with what they typed.
    /// </remarks>
    ManifestDocument Parse(ManifestKind kind, string text);
}
