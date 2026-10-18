using System.Text;

namespace RMS.ServiceInstaller.Implementation;

/// <summary>
/// Quotes a single argument so that the platform's own parser reads it back unchanged.
/// </summary>
/// <remarks>
/// <para>
/// This is only needed when an argument has to be written into something that is itself parsed as a command line --
/// the service control manager's binary path, a systemd unit's <c>ExecStart</c>, a launchd plist's program
/// arguments. Everywhere else the arguments are handed over as a list and the runtime does the quoting.
/// </para>
/// <para>
/// The two rules are genuinely different and are kept apart. Windows uses double quotes and backslash escapes
/// understood by the C runtime's argument splitter, and its service manager passes the whole binary path to that
/// splitter; POSIX uses single quotes, in which nothing is special except the single quote itself, which cannot
/// appear at all and is therefore closed, escaped and reopened.
/// </para>
/// </remarks>
public static class ArgumentQuoting
{
    /// <summary>
    /// Quotes an argument for a Windows command line.
    /// </summary>
    /// <param name="value">The argument to quote.</param>
    /// <returns>The argument, quoted only when it needs to be.</returns>
    /// <remarks>
    /// The escaping rule is the one the C runtime's parser implements, which is what the service control manager
    /// hands a binary path to: a run of backslashes before a quote is doubled, and the quote itself is escaped.
    /// Applying the rule even when the argument looks simple would add noise to a command line that operators read
    /// in the services console, so arguments that need nothing are returned as they are.
    /// </remarks>
    public static string ForWindows(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (value.Length == 0)
            return "\"\"";

        if (value.IndexOfAny([' ', '\t', '"']) < 0)
            return value;

        StringBuilder builder = new(value.Length + 8);

        builder.Append('"');

        int backslashes = 0;

        foreach (char character in value)
        {
            if (character == '\\')
            {
                backslashes++;
                continue;
            }

            if (character == '"')
            {
                // Every backslash that precedes the quote is doubled, and the quote is escaped. A backslash here
                // would otherwise join the escape and swallow the quote.
                builder.Append('\\', (backslashes * 2) + 1);
                builder.Append('"');
                backslashes = 0;
                continue;
            }

            builder.Append('\\', backslashes);
            backslashes = 0;
            builder.Append(character);
        }

        // Trailing backslashes are doubled so that they do not escape the closing quote.
        builder.Append('\\', backslashes * 2);
        builder.Append('"');

        return builder.ToString();
    }

    /// <summary>
    /// Quotes an argument for a POSIX command line.
    /// </summary>
    /// <param name="value">The argument to quote.</param>
    /// <returns>The argument, quoted only when it needs to be.</returns>
    public static string ForPosix(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (value.Length == 0)
            return "''";

        bool needsQuoting = false;

        foreach (char character in value)
        {
            // Everything outside this set is either special to a shell, special to the reader of the unit file, or
            // outside the printable range. Rather than enumerate the escapes for each of them, the whole argument is
            // quoted, where none of them mean anything.
            bool safe = char.IsAsciiLetterOrDigit(character) ||
                        character is '/' or '.' or '_' or '-' or ':' or '@' or '%' or '+' or '=' or ',';

            if (!safe)
            {
                needsQuoting = true;
                break;
            }
        }

        if (!needsQuoting)
            return value;

        StringBuilder builder = new(value.Length + 8);
        builder.Append('\'');

        foreach (char character in value)
        {
            // A single quote cannot be escaped inside a run of single quotes, so the run is closed, the quote is
            // emitted outside it with a backslash, and the run is reopened. Inside single quotes nothing else is
            // special, which is why no other character needs a case here.
            if (character == '\'')
                builder.Append("'\\''");
            else
                builder.Append(character);
        }

        builder.Append('\'');

        return builder.ToString();
    }

    /// <summary>
    /// Joins quoted arguments into a command line.
    /// </summary>
    /// <param name="executable">The program, quoted for the platform.</param>
    /// <param name="arguments">The arguments, which are quoted here.</param>
    /// <param name="forWindows">Whether the platform is Windows.</param>
    /// <returns>The command line.</returns>
    public static string Join(string executable, IReadOnlyList<string> arguments, bool forWindows)
    {
        ArgumentNullException.ThrowIfNull(executable);
        ArgumentNullException.ThrowIfNull(arguments);

        StringBuilder builder = new();
        builder.Append(forWindows ? ForWindows(executable) : ForPosix(executable));

        foreach (string argument in arguments)
            builder.Append(' ').Append(forWindows ? ForWindows(argument) : ForPosix(argument));

        return builder.ToString();
    }
}
