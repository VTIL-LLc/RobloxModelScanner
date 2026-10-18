using System.Text;

namespace RMS.ServiceInstaller.Implementation;

/// <summary>
/// Joins paths the way POSIX does, whatever the host happens to be.
/// </summary>
/// <remarks>
/// <para>
/// The definitions for Linux and macOS are text that a different operating system will read, and the paths inside
/// them belong to that operating system rather than to the one that produced them. Using the platform's own
/// <see cref="Path.Combine(string, string)"/> here would write a backslash into a unit file whenever the text was
/// produced on Windows, which is exactly the situation a build machine is in.
/// </para>
/// <para>
/// Keeping the join explicit also makes these renderers testable on a machine that has neither systemd nor launchd,
/// which is the only kind of machine most of this code is ever compiled on.
/// </para>
/// </remarks>
public static class PosixPath
{
    /// <summary>Adds a trailing directory separator.</summary>
    private const char Separator = '/';

    /// <summary>
    /// Joins path components with a forward slash.
    /// </summary>
    /// <param name="parts">The components, in order.</param>
    /// <returns>The joined path.</returns>
    /// <remarks>
    /// Redundant separators are collapsed and a leading root is preserved, so joining an absolute root with a
    /// relative name produces an absolute path rather than an accidental double slash. A component that is
    /// absolute replaces everything before it, which is the rule POSIX itself uses and the reason a plan can carry
    /// either an absolute path or a path relative to one.
    /// </remarks>
    public static string Join(params string[] parts)
    {
        ArgumentNullException.ThrowIfNull(parts);

        StringBuilder builder = new();

        foreach (string part in parts)
        {
            if (part.Length == 0)
                continue;

            if (part[0] == Separator)
                builder.Clear();

            if (builder.Length > 0 && builder[^1] != Separator)
                builder.Append(Separator);

            // Trailing separators are dropped here and re-added by the next component, so that the join never emits
            // a doubled separator.
            builder.Append(part.TrimEnd(Separator));
        }

        return builder.Length == 0 ? string.Empty : builder.ToString();
    }
}
