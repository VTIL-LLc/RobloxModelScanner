namespace RMS.Configuration.Service.Enums;

/// <summary>
/// One of the files the deployer reads.
/// </summary>
/// <remarks>
/// <para>
/// There are four, and they are not interchangeable: three are written by hand and one is written by the
/// deployer. The distinction matters to the console, because a hand-written file is one it may edit and a
/// generated one is one it must not -- an edit to a generated file survives until the next deployment and then
/// disappears, which is the worst of both worlds. Keeping the kind explicit is what makes that rule a property
/// of the type rather than a convention somebody has to remember.
/// </para>
/// <para>
/// The three hand-written files are described by the same shape of vocabulary and validated by the same parser,
/// so they share a kind rather than each getting a reader of its own.
/// </para>
/// </remarks>
public enum ManifestKind
{
    /// <summary>
    /// <c>rmsdeploy.requirements</c>: every prerequisite the stack needs, one section per requirement.
    /// </summary>
    Requirements,

    /// <summary>
    /// <c>rmsdeploy.build</c>: the ordered list of things to build.
    /// </summary>
    Build,

    /// <summary>
    /// <c>rmsdeploy.services</c>: what to run, and how each platform should run it.
    /// </summary>
    Services,

    /// <summary>
    /// The generated <c>services.ini</c>. Read-only, and reported as such.
    /// </summary>
    Plan,
}
