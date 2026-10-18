namespace RMS.Configuration.Service.Models;

/// <summary>
/// The generated plan, as the installer will read it.
/// </summary>
/// <remarks>
/// <para>
/// The plan is read with the same reader the installer uses, which is the only way the console can claim that
/// what it shows is what will be installed rather than what a second implementation believes will be installed.
/// </para>
/// <para>
/// A plan that is absent is reported in a sentence rather than as a failure. A machine that has been provisioned
/// but not yet deployed has no plan, and that is an ordinary state for this page to describe -- and one an
/// operator is quite likely to be looking at it in order to diagnose.
/// </para>
/// </remarks>
public sealed class PlanView
{
    /// <summary>
    /// Gets or sets a value indicating whether the plan could be read.
    /// </summary>
    public bool Available { get; set; }

    /// <summary>
    /// Gets or sets why the plan could not be read, when it could not.
    /// </summary>
    public string Detail { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the absolute path of the plan file.
    /// </summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the plan's schema version.
    /// </summary>
    public int Version { get; set; }

    /// <summary>
    /// Gets or sets the platform the plan was written for.
    /// </summary>
    public string Platform { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the repository root the plan was generated against.
    /// </summary>
    public string Root { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the directory the deployment's output lives under.
    /// </summary>
    public string OutputRoot { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the root of the run store.
    /// </summary>
    public string LogDirectory { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the supervisor the installer will register.
    /// </summary>
    public string HostExecutable { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the services the plan declares.
    /// </summary>
    public List<PlanServiceView> Services { get; set; } = [];

    /// <summary>
    /// Gets or sets every problem found in the plan.
    /// </summary>
    public List<ManifestProblem> Problems { get; set; } = [];

    /// <summary>
    /// Gets or sets a sentence describing the plan.
    /// </summary>
    public string Summary { get; set; } = string.Empty;
}
