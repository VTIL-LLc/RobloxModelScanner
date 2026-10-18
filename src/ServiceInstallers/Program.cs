using System.Diagnostics;

namespace RobloxModelScanner.ServiceInstallers;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length < 2 || args[0] is not ("install" or "uninstall"))
        {
            Console.WriteLine("Usage:");
            Console.WriteLine("  ServiceInstallers install <service-name> <executable-path> [display-name]");
            Console.WriteLine("  ServiceInstallers uninstall <service-name>");
            return 2;
        }

        string action = args[0];
        string serviceName = args[1];

        if (string.IsNullOrWhiteSpace(serviceName))
        {
            Console.Error.WriteLine("A service name is required.");
            return 2;
        }

        if (action == "uninstall")
            return RunSc(["delete", serviceName]);

        if (args.Length < 3 || string.IsNullOrWhiteSpace(args[2]))
        {
            Console.Error.WriteLine("An executable path is required for installation.");
            return 2;
        }

        string executablePath = Path.GetFullPath(args[2]);
        if (!File.Exists(executablePath))
        {
            Console.Error.WriteLine($"Executable not found: {executablePath}");
            return 2;
        }

        string displayName = args.Length >= 4 ? args[3] : serviceName;
        return RunSc([
            "create", serviceName,
            $"binPath= \"{executablePath}\"",
            "start= demand",
            $"DisplayName= \"{displayName}\""
        ]);
    }

    private static int RunSc(string[] arguments)
    {
        using Process process = new()
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "sc.exe",
                UseShellExecute = false,
                CreateNoWindow = true,
                ArgumentList = { }
            }
        };

        foreach (string argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);

        process.Start();
        process.WaitForExit();
        return process.ExitCode;
    }
}
