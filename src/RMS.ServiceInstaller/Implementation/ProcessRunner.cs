using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace RMS.ServiceInstaller.Implementation;

/// <summary>
/// Runs a helper program and captures what it said.
/// </summary>
/// <remarks>
/// <para>
/// Three of the four mechanisms the installer drives are command-line tools -- <c>appcmd</c> on Windows,
/// <c>systemctl</c> on Linux, <c>launchctl</c> on macOS -- and each of them reports a refusal on one of the two
/// output streams and sometimes returns a success code anyway. Both streams are therefore captured together and
/// both are returned, so a backend can decide what a message means rather than having thrown half of it away.
/// </para>
/// <para>
/// The programs are never launched through a shell. A shell would introduce a second quoting language, and the
/// arguments here are paths that a deployment chose; a path with a space in it should reach the tool intact rather
/// than being re-parsed by something that was not asked to be involved.
/// </para>
/// </remarks>
public static class ProcessRunner
{
    /// <summary>How long a helper program is given before it is treated as hung.</summary>
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Runs a program and waits for it.
    /// </summary>
    /// <param name="executable">The program to run.</param>
    /// <param name="arguments">Its arguments, one entry each.</param>
    /// <param name="timeout">How long to wait, or <see langword="null"/> for the default.</param>
    /// <returns>The exit code and the combined output.</returns>
    public static (int ExitCode, string Output) Run(
        string executable, IReadOnlyList<string> arguments, TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(executable);
        ArgumentNullException.ThrowIfNull(arguments);

        ProcessStartInfo startInfo = new()
        {
            FileName = executable,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (string argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using Process process = new() { StartInfo = startInfo };

        try
        {
            if (!process.Start())
                return (-1, "The operating system refused to start '" + executable + "'.");
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or UnauthorizedAccessException)
        {
            return (-1, "Could not run '" + executable + "': " + exception.Message);
        }

        // Both streams are read on the pool at once. Reading one to the end before starting the other is the
        // classic way to deadlock against a program that fills the pipe it is not being read from, and
        // 'systemctl status' writes enough to fill one.
        Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
        Task<string> standardError = process.StandardError.ReadToEndAsync();

        TimeSpan allowance = timeout ?? DefaultTimeout;

        if (!process.WaitForExit((int)allowance.TotalMilliseconds))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // The process died on its own between the timeout and the kill, which is the outcome the timeout was
                // waiting for anyway.
            }

            return (
                -1,
                "'" + executable + "' did not finish within " +
                allowance.TotalSeconds.ToString(CultureInfo.InvariantCulture) + " second(s).");
        }

        StringBuilder combined = new();

        Append(combined, standardOutput.GetAwaiter().GetResult());
        Append(combined, standardError.GetAwaiter().GetResult());

        return (process.ExitCode, combined.ToString().TrimEnd());
    }

    /// <summary>
    /// Finds a program on the platform's search path.
    /// </summary>
    /// <param name="name">The program's name, without a directory.</param>
    /// <returns>The full path, or <see langword="null"/> when it is not on the path.</returns>
    /// <remarks>
    /// This exists because a service manager is started with a minimal environment and cannot be assumed to have a
    /// fully populated search path. Resolving the helper to an absolute path once, while a deployment is running
    /// with the operator's own environment, is what stops the service from failing later for a reason that is
    /// invisible from the service's own log.
    /// </remarks>
    public static string? FindOnPath(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (name.Length == 0)
            return null;

        if (Path.IsPathRooted(name))
            return File.Exists(name) ? Path.GetFullPath(name) : null;

        string[] suffixes = OperatingSystem.IsWindows()
            ? [string.Empty, ".exe", ".cmd", ".bat"]
            : [string.Empty];

        string[] directories = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (string directory in directories)
        {
            foreach (string suffix in suffixes)
            {
                string candidate;

                try
                {
                    candidate = Path.Combine(directory, name + suffix);
                }
                catch (ArgumentException)
                {
                    // A malformed entry on the search path is skipped rather than fatal: the path is a shared piece
                    // of machine state and one bad entry must not stop a program from starting.
                    continue;
                }

                if (File.Exists(candidate))
                    return Path.GetFullPath(candidate);
            }
        }

        return null;
    }

    private static void Append(StringBuilder builder, string text)
    {
        if (text.Length == 0)
            return;

        if (builder.Length > 0 && builder[^1] != '\n')
            builder.Append('\n');

        builder.Append(text);
    }
}
