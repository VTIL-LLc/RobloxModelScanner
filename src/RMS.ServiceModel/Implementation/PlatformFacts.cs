using System.Runtime.InteropServices;

using RMS.ServiceModel.Enums;

namespace RMS.ServiceModel.Implementation;

/// <summary>
/// Names and detects the platform the deployer and the services distinguish between.
/// </summary>
/// <remarks>
/// Every platform question in the stack comes through here, so that the three names -- <c>windows</c>,
/// <c>linux</c>, <c>macos</c> -- are spelled in exactly one place. A manifest that writes <c>macos</c> and a
/// test that looks for <c>darwin</c> would be the same platform under two spellings, and the second spelling
/// is how a service ends up registered on a machine it was not written for.
/// </remarks>
public static class PlatformFacts
{
    /// <summary>Gets the platform the current process is running on.</summary>
    /// <exception cref="PlatformNotSupportedException">Thrown on a platform this stack does not support.</exception>
    public static PlanPlatform Current
    {
        get
        {
            if (OperatingSystem.IsWindows())
                return PlanPlatform.Windows;

            if (OperatingSystem.IsMacOS())
                return PlanPlatform.MacOs;

            if (OperatingSystem.IsLinux())
                return PlanPlatform.Linux;

            throw new PlatformNotSupportedException(
                "The RMS stack runs on Windows, Linux and macOS. This host reports " +
                RuntimeInformation.OSDescription.Trim() + ".");
        }
    }

    /// <summary>Gets the lower-case name of the current platform, as the manifests spell it.</summary>
    public static string CurrentName => NameOf(Current);

    /// <summary>
    /// Gets the lower-case name of a platform, as the manifests spell it.
    /// </summary>
    /// <param name="platform">The platform to name.</param>
    /// <returns>The name of the platform.</returns>
    public static string NameOf(PlanPlatform platform)
    {
        return platform switch
        {
            PlanPlatform.Windows => "windows",
            PlanPlatform.MacOs => "macos",
            _ => "linux",
        };
    }

    /// <summary>
    /// Gets the file name suffix an executable carries on a platform.
    /// </summary>
    /// <param name="platform">The platform to ask about.</param>
    /// <returns>The suffix, which is empty everywhere except Windows.</returns>
    public static string ExecutableSuffix(PlanPlatform platform)
    {
        return platform == PlanPlatform.Windows ? ".exe" : string.Empty;
    }

    /// <summary>
    /// Gets the directory separator a platform uses.
    /// </summary>
    /// <param name="platform">The platform to ask about.</param>
    /// <returns>The separator character.</returns>
    public static char Separator(PlanPlatform platform)
    {
        return platform == PlanPlatform.Windows ? '\\' : '/';
    }
}
