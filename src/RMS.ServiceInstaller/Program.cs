using RMS.ServiceInstaller.Enums;
using RMS.ServiceInstaller.Options;

namespace RMS.ServiceInstaller;

/// <summary>
/// The entry point of the service installer.
/// </summary>
/// <remarks>
/// <para>
/// This program takes a plan the deployer generated and makes the machine match it: register the services, start
/// them, stop them, remove them, or report on them. It is the only part of the stack that knows about service
/// managers, and it is deliberately a separate program from the supervisor so that the supervisor can be a
/// completely ordinary process on every platform and still contain no code that differs by platform beyond which
/// signals it listens for.
/// </para>
/// <para>
/// The verb and the plan directory are all the deployer passes, which keeps the deployer's own code free of any
/// knowledge of service managers -- including the POSIX ones it cannot test on the machine it is built on. The
/// definitions it used to render in C are rendered here instead, by code that is compiled and exercised on every
/// platform the stack is built on.
/// </para>
/// </remarks>
internal static class Program
{
    /// <summary>Returned when the command line was not understood.</summary>
    private const int UsageExitCode = 2;

    /// <summary>
    /// Runs the installer.
    /// </summary>
    /// <param name="args">The command line, without the program name.</param>
    /// <returns>Zero when everything asked for was done, otherwise non-zero.</returns>
    private static int Main(string[] args)
    {
        ParseOutcome outcome = InstallerOptions.Parse(args, out InstallerOptions? options, out string? message);

        if (outcome == ParseOutcome.HelpRequested)
        {
            InstallerOptions.WriteUsage(Console.Out);
            return 0;
        }

        if (outcome == ParseOutcome.Failure || options is null)
        {
            Console.Error.WriteLine(message ?? "The command line could not be understood.");
            Console.Error.WriteLine();
            InstallerOptions.WriteUsage(Console.Error);

            return UsageExitCode;
        }

        // Written out in full: inside the namespace RMS.ServiceInstaller, the unqualified name ServiceInstaller
        // resolves to the namespace itself rather than to the type in Implementation.
        return Implementation.ServiceInstaller.Execute(options, Console.Out, Console.Error);
    }
}
