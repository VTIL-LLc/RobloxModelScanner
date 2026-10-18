namespace RMS.Configuration.Service.Models;

/// <summary>
/// One service as the generated plan describes it.
/// </summary>
/// <remarks>
/// This is the plan's reading of a service, not the manifest's: every <c>%ROOT%</c> has been expanded, every
/// platform-qualified value has been chosen, every default has been filled in, and the arguments are a list
/// rather than a semicolon separated string. Showing it beside the manifest section it came from is the point
/// of the plan page -- it is where an operator sees what their edit actually became.
/// </remarks>
public sealed class PlanServiceView
{
    /// <summary>
    /// Gets or sets the identifier, which is the manifest's section name.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the service's name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the description.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the executable, which is empty for a web site the platform serves.
    /// </summary>
    public string Executable { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the arguments, one entry each.
    /// </summary>
    public List<string> Arguments { get; set; } = [];

    /// <summary>
    /// Gets or sets the working directory.
    /// </summary>
    public string WorkingDirectory { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the restart policy's name.
    /// </summary>
    public string Restart { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the seconds before the first restart.
    /// </summary>
    public int RestartDelaySeconds { get; set; }

    /// <summary>
    /// Gets or sets the ceiling the delay doubles up to.
    /// </summary>
    public int MaximumRestartDelaySeconds { get; set; }

    /// <summary>
    /// Gets or sets the seconds between a stop request and a kill.
    /// </summary>
    public int StopTimeoutSeconds { get; set; }

    /// <summary>
    /// Gets or sets the line written to the child's input to ask it to stop.
    /// </summary>
    public string ShutdownLine { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the directory the platform serves, for a web site entry.
    /// </summary>
    public string Website { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the port, as text, because the plan carries it that way.
    /// </summary>
    public string Port { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the platforms the entry applies to.
    /// </summary>
    public string Platforms { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the account to run as.
    /// </summary>
    public string User { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the environment the entry declares, in the order the plan sorted it.
    /// </summary>
    public List<PlanVariableView> Environment { get; set; } = [];

    /// <summary>
    /// Gets or sets the directory this service's logs are written to.
    /// </summary>
    public string LogDirectory { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the plan's own platform list includes this entry.
    /// </summary>
    public bool AppliesToThisPlan { get; set; } = true;

    /// <summary>
    /// Gets or sets the command the supervisor would run, for an operator who wants to try it by hand.
    /// </summary>
    public string Command { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets every problem attributed to this entry.
    /// </summary>
    public List<ManifestProblem> Problems { get; set; } = [];
}
