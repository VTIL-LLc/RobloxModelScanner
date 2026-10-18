namespace RMS.Configuration.Service.Implementation;

/// <summary>
/// The ceilings the deployer's parser enforces.
/// </summary>
/// <remarks>
/// <para>
/// Every one of these is a fixed-size buffer in <c>rmsdeploy.c</c>, and every one is therefore a limit that a
/// manifest can exceed in a way that is invisible until the deployer runs on a machine where the failure is
/// expensive. The names match the C constants deliberately, so that a manifest that fails here can be traced to
/// the line in the parser that refused it.
/// </para>
/// <para>
/// They are duplicated rather than shared because the deployer is a single C translation unit with no
/// dependencies, and making it export its limits would mean giving it an interface it does not otherwise need.
/// The duplication is safe in a way that most duplication is not: these values change when the buffers they
/// describe change, which is to say almost never, and a manifest that exceeded one would fail loudly on the
/// first run rather than silently.
/// </para>
/// </remarks>
public static class DeployerLimits
{
    /// <summary>
    /// The largest number of sections one manifest may hold.
    /// </summary>
    public const int MaximumSections = 48;

    /// <summary>
    /// The largest number of keys one section may hold.
    /// </summary>
    public const int MaximumKeysPerSection = 24;

    /// <summary>
    /// The longest a section name or an identifier may be.
    /// </summary>
    public const int MaximumIdentifierLength = 96;

    /// <summary>
    /// The longest a setting's value may be.
    /// </summary>
    public const int MaximumValueLength = 2048;

    /// <summary>
    /// The longest a short kind, configuration or restart-policy name may be.
    /// </summary>
    public const int MaximumKindLength = 48;

    /// <summary>
    /// The longest a version may be.
    /// </summary>
    public const int MaximumVersionLength = 96;

    /// <summary>
    /// The longest a free-text description or note may be.
    /// </summary>
    public const int MaximumNoteLength = 256;

    /// <summary>
    /// The longest a path may be.
    /// </summary>
    public const int MaximumPathLength = 4096;

    /// <summary>
    /// The longest a service's human-readable description may be.
    /// </summary>
    /// <remarks>
    /// The deployer gives a description four times the room it gives a note, because a description is read by the
    /// service control manager, by <c>systemctl status</c> and by a page, whereas a note is read by one command's
    /// output.
    /// </remarks>
    public const int MaximumDescriptionLength = MaximumNoteLength * 4;

    /// <summary>
    /// The longest one line of a manifest may be.
    /// </summary>
    /// <remarks>
    /// The deployer reads a file a line at a time into a buffer of this size. A longer line is therefore split
    /// in two by the reader rather than rejected, and the second half becomes a key with no value -- which is
    /// why the validator reports it here, where it can still be attributed to the line that caused it.
    /// </remarks>
    public const int MaximumLineLength = 8192;

    /// <summary>
    /// The values a platform-qualified setting may be qualified with.
    /// </summary>
    public static readonly string[] Platforms = ["windows", "linux", "macos"];
}
