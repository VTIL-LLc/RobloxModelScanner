namespace RMS.Configuration.Service.Models;

/// <summary>
/// One of the fixed-size buffer limits the deployer's parser enforces.
/// </summary>
/// <remarks>
/// The C constant's name is carried alongside the number so that an operator who has just been refused by the
/// deployer has somewhere to look. A message that says "too long" and a message that says "too long for
/// RMS_MAX_VALUE" are the same length and only one of them can be found in the source.
/// </remarks>
public sealed class DeployerLimitView
{
    /// <summary>
    /// Gets or sets the C constant's name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the limit.
    /// </summary>
    public int Value { get; set; }

    /// <summary>
    /// Gets or sets a sentence saying what the limit applies to.
    /// </summary>
    public string Summary { get; set; } = string.Empty;
}
