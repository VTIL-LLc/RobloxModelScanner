using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace RMS.ServiceInstaller.Implementation;

/// <summary>
/// The Windows service control manager, called directly.
/// </summary>
/// <remarks>
/// <para>
/// The framework's own wrapper is not used. It is built around <c>System.Configuration.Install</c> and the
/// <c>installutil</c> host, which have no equivalent on the other two platforms and would make the Windows path the
/// only one that looks different; the calls that actually register a service are six stable functions that have
/// been exported by <c>advapi32</c> since Windows 2000.
/// </para>
/// <para>
/// Every method here assumes the caller has already established that the platform is Windows. The attribute on the
/// class records that, and the installer only ever reaches this type from a branch that has tested for it.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
internal static class NativeServiceControl
{
    /// <summary>Access required to create a service.</summary>
    private const int ScManagerCreateService = 0x0002;

    /// <summary>Access required to enumerate and connect to services.</summary>
    private const int ScManagerConnect = 0x0001;

    /// <summary>The full access mask, used to keep the code short rather than to express intent.</summary>
    private const int ScManagerAllAccess = 0xF003F;

    /// <summary>Access required to open, start, stop and delete a service.</summary>
    private const int ServiceAllAccess = 0xF01FF;

    /// <summary>A service that runs in its own process.</summary>
    private const int ServiceWin32OwnProcess = 0x00000010;

    /// <summary>A service started by the service control manager at boot.</summary>
    private const int ServiceAutoStart = 0x00000002;

    /// <summary>Report a start failure to the event log and carry on.</summary>
    private const int ServiceErrorNormal = 0x00000001;

    /// <summary>The stop control.</summary>
    private const int ServiceControlStop = 0x00000001;

    /// <summary>The state of a service that is not running.</summary>
    private const int ServiceStopped = 0x00000001;

    /// <summary>The state of a service whose start is in progress.</summary>
    private const int ServiceStartPending = 0x00000002;

    /// <summary>The state of a service whose stop is in progress.</summary>
    private const int ServiceStopPending = 0x00000003;

    /// <summary>The state of a service that is running.</summary>
    private const int ServiceRunning = 0x00000004;

    /// <summary>The information level that returns the hosting process identifier.</summary>
    private const int ScStatusProcessInfo = 0;

    /// <summary>The change kind that sets a service's description.</summary>
    private const int ServiceConfigDescription = 1;

    /// <summary>The change kind that sets what happens when a service fails.</summary>
    private const int ServiceConfigFailureActions = 2;

    /// <summary>Restart the service.</summary>
    private const int ScActionRestart = 1;

    /// <summary>Do nothing.</summary>
    private const int ScActionNone = 0;

    /// <summary>The service already exists.</summary>
    private const int ErrorServiceExists = 1073;

    /// <summary>No service of that name is registered.</summary>
    private const int ErrorServiceDoesNotExist = 1060;

    /// <summary>The service is already running.</summary>
    private const int ErrorServiceAlreadyRunning = 1056;

    /// <summary>The service is not running.</summary>
    private const int ErrorServiceNotActive = 1062;

    /// <summary>The name is already in use by another service.</summary>
    private const int ErrorDuplicateServiceName = 1072;

    /// <summary>
    /// Registers or updates a service.
    /// </summary>
    /// <param name="name">The service name.</param>
    /// <param name="displayName">The name a person sees in the services console.</param>
    /// <param name="commandLine">The full command line, already quoted.</param>
    /// <param name="description">The text shown beside the service in the console.</param>
    /// <returns><see langword="null"/> on success, otherwise the reason.</returns>
    public static string? Register(string name, string displayName, string commandLine, string description)
    {
        IntPtr manager = OpenScManagerW(null, null, ScManagerAllAccess);

        if (manager == IntPtr.Zero)
            return Describe("open the service control manager", Marshal.GetLastWin32Error());

        IntPtr service = IntPtr.Zero;

        try
        {
            // The service is created first and, when it turns out to exist already, reconfigured instead. Creating
            // blindly and reconfiguring on the duplicate error would be shorter, but the same discrimination is
            // needed either way to decide whether a failed deployment has to undo this step, so it is made explicit.
            service = CreateServiceW(
                manager,
                name,
                displayName,
                ServiceAllAccess,
                ServiceWin32OwnProcess,
                ServiceAutoStart,
                ServiceErrorNormal,
                commandLine,
                null,
                IntPtr.Zero,
                null,
                null,
                null);

            if (service == IntPtr.Zero)
            {
                int error = Marshal.GetLastWin32Error();

                if (error != ErrorServiceExists && error != ErrorDuplicateServiceName)
                    return Describe("create the service", error);

                // An existing service is reconfigured rather than left as it was. The definition lives in the plan
                // file, which the supervisor reads, but the command line and the description are recorded in the
                // service database, and an operator reading the services console must see the current values.
                service = OpenServiceW(manager, name, ServiceAllAccess);

                if (service == IntPtr.Zero)
                    return Describe("open the existing service", Marshal.GetLastWin32Error());

                ServiceStatus ignored = default;

                // Stopped before it is reconfigured: a running service holds the old command line until it is
                // restarted, and the restart below is what makes the change take effect.
                if (QueryState(service) == ServiceRunning)
                    ControlService(service, ServiceControlStop, ref ignored);

                if (!ChangeServiceConfigW(
                        service,
                        ServiceWin32OwnProcess,
                        ServiceAutoStart,
                        ServiceErrorNormal,
                        commandLine,
                        null,
                        IntPtr.Zero,
                        null,
                        null,
                        displayName,
                        null))
                {
                    return Describe("update the service's command line", Marshal.GetLastWin32Error());
                }
            }

            if (description.Length > 0)
            {
                ServiceDescription text = new() { Description = description };

                if (!ChangeServiceConfig2W(service, ServiceConfigDescription, ref text))
                    return Describe("set the service's description", Marshal.GetLastWin32Error());
            }

            ApplyFailureActions(service);

            return null;
        }
        finally
        {
            if (service != IntPtr.Zero)
                CloseServiceHandle(service);

            CloseServiceHandle(manager);
        }
    }

    /// <summary>
    /// Stops and unregisters a service.
    /// </summary>
    /// <param name="name">The service name.</param>
    /// <returns><see langword="null"/> on success, otherwise the reason.</returns>
    /// <remarks>
    /// A service that was never registered is not an error. The verb means "make sure this is gone", and a
    /// deployment that is re-run after a partial failure must be able to say that about something it has already
    /// removed.
    /// </remarks>
    public static string? Unregister(string name)
    {
        IntPtr manager = OpenScManagerW(null, null, ScManagerAllAccess);

        if (manager == IntPtr.Zero)
            return Describe("open the service control manager", Marshal.GetLastWin32Error());

        IntPtr service = IntPtr.Zero;

        try
        {
            service = OpenServiceW(manager, name, ServiceAllAccess);

            if (service == IntPtr.Zero)
            {
                int error = Marshal.GetLastWin32Error();
                return error == ErrorServiceDoesNotExist
                    ? null
                    : Describe("open the service", error);
            }

            if (QueryState(service) != ServiceStopped)
            {
                ServiceStatus ignored = default;

                ControlService(service, ServiceControlStop, ref ignored);

                // The delete is issued before the stop has necessarily finished, and the manager accepts that: it
                // marks the service for deletion and completes it once the process is gone. Waiting here instead
                // would block for as long as the service's own stop timeout, which the supervisor is already
                // honouring at the other end.
                WaitForState(service, ServiceStopped, TimeSpan.FromSeconds(30));
            }

            if (!DeleteService(service))
            {
                int error = Marshal.GetLastWin32Error();

                // Being deleted twice is not a failure. The manager reports the pending deletion as "does not
                // exist", which is exactly the state the caller asked for.
                if (error != ErrorServiceDoesNotExist)
                    return Describe("delete the service", error);
            }

            return null;
        }
        finally
        {
            if (service != IntPtr.Zero)
                CloseServiceHandle(service);

            CloseServiceHandle(manager);
        }
    }

    /// <summary>
    /// Starts a service.
    /// </summary>
    /// <param name="name">The service name.</param>
    /// <returns><see langword="null"/> on success, otherwise the reason.</returns>
    public static string? Start(string name)
    {
        IntPtr manager = OpenScManagerW(null, null, ScManagerAllAccess);

        if (manager == IntPtr.Zero)
            return Describe("open the service control manager", Marshal.GetLastWin32Error());

        IntPtr service = IntPtr.Zero;

        try
        {
            service = OpenServiceW(manager, name, ServiceAllAccess);

            if (service == IntPtr.Zero)
                return Describe("open the service", Marshal.GetLastWin32Error());

            if (!StartServiceW(service, 0, IntPtr.Zero))
            {
                int error = Marshal.GetLastWin32Error();

                // Already running is the state the caller asked for, so it is not reported as a failure.
                if (error != ErrorServiceAlreadyRunning)
                    return Describe("start the service", error);
            }

            return null;
        }
        finally
        {
            if (service != IntPtr.Zero)
                CloseServiceHandle(service);

            CloseServiceHandle(manager);
        }
    }

    /// <summary>
    /// Stops a service and waits for it to actually stop.
    /// </summary>
    /// <param name="name">The service name.</param>
    /// <param name="timeout">How long to wait for the stop to complete.</param>
    /// <returns><see langword="null"/> on success, otherwise the reason.</returns>
    /// <remarks>
    /// Waiting is the point of this method. The stop control is asynchronous: it asks the service's own stop handler
    /// to run, and that handler is where the supervisor shuts its child down. Returning as soon as the control was
    /// accepted would let a deployment start the replacement service while the old one still holds its port.
    /// </remarks>
    public static string? Stop(string name, TimeSpan timeout)
    {
        IntPtr manager = OpenScManagerW(null, null, ScManagerAllAccess);

        if (manager == IntPtr.Zero)
            return Describe("open the service control manager", Marshal.GetLastWin32Error());

        IntPtr service = IntPtr.Zero;

        try
        {
            service = OpenServiceW(manager, name, ServiceAllAccess);

            if (service == IntPtr.Zero)
            {
                int error = Marshal.GetLastWin32Error();
                return error == ErrorServiceDoesNotExist ? null : Describe("open the service", error);
            }

            if (QueryState(service) == ServiceStopped)
                return null;

            ServiceStatus ignored = default;

            if (!ControlService(service, ServiceControlStop, ref ignored))
            {
                int error = Marshal.GetLastWin32Error();

                if (error != ErrorServiceNotActive)
                    return Describe("stop the service", error);
            }

            return WaitForState(service, ServiceStopped, timeout);
        }
        finally
        {
            if (service != IntPtr.Zero)
                CloseServiceHandle(service);

            CloseServiceHandle(manager);
        }
    }

    /// <summary>
    /// Reads a service's state and process identifier.
    /// </summary>
    /// <param name="name">The service name.</param>
    /// <returns>The state and process identifier, or <see langword="null"/> when the service is not registered.</returns>
    public static (int State, int ProcessId)? Query(string name)
    {
        IntPtr manager = OpenScManagerW(null, null, ScManagerConnect);

        if (manager == IntPtr.Zero)
            return null;

        IntPtr service = IntPtr.Zero;

        try
        {
            service = OpenServiceW(manager, name, ServiceAllAccess);

            if (service == IntPtr.Zero)
                return null;

            return QueryDetails(service);
        }
        finally
        {
            if (service != IntPtr.Zero)
                CloseServiceHandle(service);

            CloseServiceHandle(manager);
        }
    }

    /// <summary>
    /// Turns a service state constant into the word the platform's own tools use.
    /// </summary>
    /// <param name="state">The state constant.</param>
    /// <returns>The word, lower-cased.</returns>
    public static string StateName(int state)
    {
        return state switch
        {
            ServiceStopped => "stopped",
            ServiceStartPending => "start-pending",
            ServiceStopPending => "stop-pending",
            ServiceRunning => "running",
            0x00000005 => "continue-pending",
            0x00000006 => "pause-pending",
            0x00000007 => "paused",
            _ => "unknown",
        };
    }

    /// <summary>
    /// Configures the service to be restarted when it dies unexpectedly.
    /// </summary>
    /// <param name="service">The open service handle.</param>
    /// <remarks>
    /// This is the manager's own safety net, underneath the supervisor's. The supervisor restarts the program it is
    /// watching, but if the supervisor itself is killed -- by an out-of-memory condition, by an operator -- the
    /// manager is the only thing left that notices. Two restarts a few seconds apart, then a third later, and then
    /// nothing: a service that is failing fast is worth one quick retry, not an endless one.
    /// </remarks>
    private static void ApplyFailureActions(IntPtr service)
    {
        const int resetPeriodSeconds = 86400;

        ScAction[] actions =
        [
            new ScAction { Type = ScActionRestart, Delay = 5000 },
            new ScAction { Type = ScActionRestart, Delay = 15000 },
            new ScAction { Type = ScActionNone, Delay = 0 },
        ];

        int size = Marshal.SizeOf<ScAction>();
        IntPtr buffer = Marshal.AllocHGlobal(size * actions.Length);

        try
        {
            for (int index = 0; index < actions.Length; index++)
                Marshal.StructureToPtr(actions[index], buffer + (index * size), fDeleteOld: false);

            ServiceFailureActions failure = new()
            {
                ResetPeriod = resetPeriodSeconds,
                RebootMessage = null,
                Command = null,
                ActionCount = actions.Length,
                Actions = buffer,
            };

            // A refusal here is not propagated. The failure actions are a second line of defence; a service that is
            // registered and running without them is still correct, and failing the whole install over an optional
            // hardening step would be worse than the hardening is worth.
            ChangeServiceConfig2W(service, ServiceConfigFailureActions, ref failure);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static int QueryState(IntPtr service)
    {
        return QueryDetails(service)?.State ?? ServiceStopped;
    }

    private static (int State, int ProcessId)? QueryDetails(IntPtr service)
    {
        int size = Marshal.SizeOf<ServiceStatusProcess>();
        IntPtr buffer = Marshal.AllocHGlobal(size);

        try
        {
            if (!QueryServiceStatusEx(service, ScStatusProcessInfo, buffer, size, out _))
                return null;

            ServiceStatusProcess status = Marshal.PtrToStructure<ServiceStatusProcess>(buffer);

            return (status.CurrentState, status.ProcessId);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static string? WaitForState(IntPtr service, int wanted, TimeSpan timeout)
    {
        long deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;

        while (Environment.TickCount64 < deadline)
        {
            if (QueryState(service) == wanted)
                return null;

            Thread.Sleep(200);
        }

        return "the service did not reach '" + StateName(wanted) + "' within " +
               timeout.TotalSeconds.ToString(CultureInfo.InvariantCulture) + " second(s).";
    }

    private static string Describe(string attempt, int error)
    {
        return "Could not " + attempt + ": " +
               new Win32Exception(error).Message +
               " (error " + error.ToString(CultureInfo.InvariantCulture) + ").";
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceStatus
    {
        public int ServiceType;
        public int CurrentState;
        public int ControlsAccepted;
        public int Win32ExitCode;
        public int ServiceSpecificExitCode;
        public int CheckPoint;
        public int WaitHint;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceStatusProcess
    {
        public int ServiceType;
        public int CurrentState;
        public int ControlsAccepted;
        public int Win32ExitCode;
        public int ServiceSpecificExitCode;
        public int CheckPoint;
        public int WaitHint;
        public int ProcessId;
        public int ServiceFlags;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ServiceDescription
    {
        [MarshalAs(UnmanagedType.LPWStr)]
        public string Description;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ScAction
    {
        public int Type;
        public int Delay;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ServiceFailureActions
    {
        public int ResetPeriod;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? RebootMessage;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? Command;

        public int ActionCount;
        public IntPtr Actions;
    }

    [DllImport("advapi32.dll", EntryPoint = "OpenSCManagerW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr OpenScManagerW(string? machineName, string? databaseName, int access);

    [DllImport("advapi32.dll", EntryPoint = "CreateServiceW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateServiceW(
        IntPtr manager,
        string serviceName,
        string displayName,
        int access,
        int serviceType,
        int startType,
        int errorControl,
        string binaryPathName,
        string? loadOrderGroup,
        IntPtr tagId,
        string? dependencies,
        string? accountName,
        string? password);

    [DllImport("advapi32.dll", EntryPoint = "OpenServiceW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr OpenServiceW(IntPtr manager, string serviceName, int access);

    [DllImport("advapi32.dll", EntryPoint = "ChangeServiceConfigW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ChangeServiceConfigW(
        IntPtr service,
        int serviceType,
        int startType,
        int errorControl,
        string? binaryPathName,
        string? loadOrderGroup,
        IntPtr tagId,
        string? dependencies,
        string? accountName,
        string? password,
        string? displayName);

    [DllImport("advapi32.dll", EntryPoint = "ChangeServiceConfig2W", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ChangeServiceConfig2W(IntPtr service, int infoLevel, ref ServiceDescription info);

    [DllImport("advapi32.dll", EntryPoint = "ChangeServiceConfig2W", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ChangeServiceConfig2W(IntPtr service, int infoLevel, ref ServiceFailureActions info);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteService(IntPtr service);

    [DllImport("advapi32.dll", EntryPoint = "StartServiceW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool StartServiceW(IntPtr service, int argumentCount, IntPtr arguments);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ControlService(IntPtr service, int control, ref ServiceStatus status);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryServiceStatusEx(
        IntPtr service, int infoLevel, IntPtr buffer, int bufferSize, out int bytesNeeded);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseServiceHandle(IntPtr handle);
}
