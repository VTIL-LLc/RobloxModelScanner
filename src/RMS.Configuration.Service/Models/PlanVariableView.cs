namespace RMS.Configuration.Service.Models;

/// <summary>
/// One environment variable the plan sets for a service.
/// </summary>
/// <remarks>
/// The value is reported as the plan carries it, resolved but not redacted, because the plan itself is a file
/// on the machine that anybody who can read the deployment can read. What is deliberately absent is any key
/// this stack keeps in a file of its own: the plan declares the <em>path</em> of a token file and never its
/// contents, and the console reports the plan rather than going and looking inside it.
/// </remarks>
public sealed class PlanVariableView
{
    /// <summary>
    /// Gets or sets the variable's name.
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the variable's value.
    /// </summary>
    public string Value { get; set; } = string.Empty;
}
