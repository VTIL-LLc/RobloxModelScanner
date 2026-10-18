using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.ServiceProcess;

using RMS.ServiceHost.Enums;
using RMS.ServiceHost.Options;
using RMS.ServiceHost.Implementation;

namespace RMS.ServiceHost;

/// <summary>
/// The entry point of the service supervisor.
/// </summary>
/// <remarks>
/// <para>
/// This program is the only thing the operating system is ever asked to keep running. Every RMS service -- the
/// indexer, the API, the DevOps back end, the configuration service -- is registered against this executable,
/// which starts the real program as its child and records what it does. That arrangement is what gives all of
/// them the same log layout, the same restart policy and the same run history on Windows, on Linux and on macOS,
/// without any of them containing a line of service-management code.
/// </para>
/// <para>
/// The same binary is used three ways: as a Windows service, as a foreground process under systemd or launchd,
/// and by hand at a prompt. The only thing that decides between them is whether the operating system called it
/// from the service control manager, which <see cref="Environment.UserInteractive"/> reports.
/// </para>
/// </remarks>
internal static class Program
{
    /// <summary>Returned when the command line was not understood.</summary>
    private const int UsageExitCode = 2;

    /// <summary>
    /// Runs the host.
    /// </summary>
    /// <param name="args">The command line, without the program name.</param>
    /// <returns>The exit code of the last attempt, or a code describing why the host did not run one.</returns>
    private static int Main(string[] args)
    {
        ParseOutcome outcome = HostOptions.Parse(args, out HostOptions? options, out string? message);

        if (outcome == ParseOutcome.HelpRequested)
        {
            HostOptions.WriteUsage(Console.Out);
            return 0;
        }

        if (outcome == ParseOutcome.Failure || options is null)
        {
            Console.Error.WriteLine(message ?? "The command line could not be understood.");

            // The usage text follows the complaint rather than replacing it, because the person reading this is
            // usually an operator who typed one wrong character and does not want the whole manual first.
            Console.Error.WriteLine();
            HostOptions.WriteUsage(Console.Error);

            return UsageExitCode;
        }

        if (OperatingSystem.IsWindows() && !options.ForceConsole && !Environment.UserInteractive)
            return RunAsWindowsService(options);

        return RunInForeground(options);
    }

    /// <summary>
    /// Runs the host as a Windows service.
    /// </summary>
    /// <param name="options">The merged command line and plan the host was started with.</param>
    /// <returns>The exit code of the last attempt.</returns>
    /// <remarks>
    /// The service control manager and the type that talks to it are only referenced from here. Keeping them off
    /// the path a foreground run takes means the service-management runtime is never loaded on a platform that has
    /// no service control manager, which is what lets one binary be the service on all three systems.
    /// </remarks>
    [SupportedOSPlatform("windows")]
    private static int RunAsWindowsService(HostOptions options)
    {
        ScmHostedService service = new(options);

        // Run blocks until the manager has stopped the service and the stop handler has returned, so the exit code
        // the run produced is available by the time this returns.
        ServiceBase.Run(service);

        return service.ExitCode;
    }

    /// <summary>
    /// Runs the host in the foreground until the service stops.
    /// </summary>
    /// <param name="options">The merged command line and plan the host was started with.</param>
    /// <returns>The exit code of the last attempt.</returns>
    /// <remarks>
    /// A foreground run is what an operator gets, and also what systemd and launchd get. Both of those send a
    /// termination signal rather than talking to a control channel, so the signals are what this installs.
    /// </remarks>
    private static int RunInForeground(HostOptions options)
    {
        using ServiceSupervisor supervisor = new(options);

        // Ctrl+C and Ctrl+Break arrive here on every platform. Cancelling the default handling keeps this process
        // alive long enough to stop its child cleanly instead of vanishing and leaving the child orphaned.
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            supervisor.RequestStop("Ctrl+C");
        };

        List<PosixSignalRegistration> registrations = RegisterTerminationSignals(supervisor);

        try
        {
            // The supervisor's own stop request is the only thing that ends the run, so no token is passed here.
            // Every path that wants the run to end -- the console handler above, the signal registrations below,
            // and the stop timeout inside the supervisor -- goes through that one request.
            return supervisor.RunAsync(CancellationToken.None).GetAwaiter().GetResult();
        }
        finally
        {
            foreach (PosixSignalRegistration registration in registrations)
                registration.Dispose();
        }
    }

    /// <summary>
    /// Registers the termination signals the service managers deliver.
    /// </summary>
    /// <param name="supervisor">The supervisor to ask to stop when a signal arrives.</param>
    /// <returns>
    /// The registrations that were created. The caller keeps them alive for as long as the run lasts; a signal
    /// registration that goes out of scope stops being delivered.
    /// </returns>
    /// <remarks>
    /// SIGTERM is what systemd and launchd send to ask a service to stop, and SIGINT is what an operator's Ctrl+C
    /// becomes at a prompt. Handling them turns an abrupt end -- which would leave the child running and the run
    /// recorded as abandoned -- into an orderly stop.
    /// </remarks>
    private static List<PosixSignalRegistration> RegisterTerminationSignals(ServiceSupervisor supervisor)
    {
        List<PosixSignalRegistration> registrations = [];

        foreach (PosixSignal signal in new[] { PosixSignal.SIGINT, PosixSignal.SIGTERM, PosixSignal.SIGQUIT })
        {
            try
            {
                registrations.Add(PosixSignalRegistration.Create(signal, context =>
                {
                    // Cancelling the default handling stops the runtime from terminating this process before the
                    // child has been dealt with.
                    context.Cancel = true;
                    supervisor.RequestStop(signal.ToString());
                }));
            }
            catch (PlatformNotSupportedException)
            {
                // The signal is not delivered on this platform. The console handler above still covers the ones
                // that are.
            }
        }

        return registrations;
    }
}
