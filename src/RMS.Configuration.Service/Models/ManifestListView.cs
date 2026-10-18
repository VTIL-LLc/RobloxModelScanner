using RMS.Configuration.Service.Enums;

namespace RMS.Configuration.Service.Models;

/// <summary>
/// Every manifest the deployment is described by, as a list.
/// </summary>
/// <remarks>
/// The addresses are reported alongside the files so that an operator can see what the service is actually
/// reading rather than what the manifest claims about itself. A console pointed at the wrong checkout produces
/// a complete and entirely wrong answer, and the only thing that makes that visible is showing the paths it
/// resolved.
/// </remarks>
public sealed class ManifestListView
{
    /// <summary>
    /// Gets or sets the repository root in force.
    /// </summary>
    public string RepositoryRoot { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the directory the hand-written manifests are read from.
    /// </summary>
    public string ManifestDirectory { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the plan file in force.
    /// </summary>
    public string PlanFile { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the manifests, in the order the deployer reads them.
    /// </summary>
    public List<ManifestFileView> Files { get; set; } = [];

    /// <summary>
    /// Gets or sets every problem that is about the set of files rather than about one of them.
    /// </summary>
    /// <remarks>
    /// A missing manifest directory, a root that does not exist, a plan that has never been generated. Each is
    /// reported here rather than against a file, because there is no file to attribute it to and a page that
    /// showed nothing at all would leave the operator with no idea which of the addresses was wrong.
    /// </remarks>
    public List<ManifestProblem> Problems { get; set; } = [];

    /// <summary>
    /// Gets or sets a sentence describing the state of the deployment description.
    /// </summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>
    /// Gets the number of manifests present.
    /// </summary>
    public int PresentCount => Files.Count(file => file.Exists);

    /// <summary>
    /// Gets the number of errors across every manifest that was parsed.
    /// </summary>
    public int ErrorCount => Files.Where(file => file.ErrorCount > 0).Sum(file => file.ErrorCount)
                                  + Problems.Count(problem => problem.Severity == ProblemSeverity.Error);
}
