using RMS.Configuration.Service.Models;

namespace RMS.Configuration.Service.Interfaces;

/// <summary>
/// Checks a parsed manifest against the format the deployer reads.
/// </summary>
/// <remarks>
/// <para>
/// The validator's job is to be the one place that knows what a manifest may contain. Every rule it applies is a
/// rule the deployer enforces, or a rule the deployer silently does not -- and the second kind matters more,
/// because a misspelled key, a duplicate port or a step whose project does not exist all produce a deployment
/// that runs and does the wrong thing.
/// </para>
/// <para>
/// It annotates the document it is given rather than returning a parallel list, so that a page can put a
/// problem's sentence next to the line it is about. A list of problems detached from the lines they describe
/// leaves an operator counting from the top of a hundred and twenty lines.
/// </para>
/// </remarks>
public interface IManifestValidator
{
    /// <summary>
    /// Checks a document and records what is wrong with it.
    /// </summary>
    /// <param name="document">The document to check, whose problems are replaced.</param>
    /// <remarks>
    /// The document is modified in place, and its problems are replaced rather than appended to, so that checking
    /// the same document twice does not double every message.
    /// </remarks>
    void Validate(ManifestDocument document);
}
