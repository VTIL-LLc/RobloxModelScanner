using System;
using System.Collections;
using System.ComponentModel;
using System.Configuration.Install;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.ServiceProcess;
using Roblox.Wcf;

namespace RobloxModelScanner.WcfServiceHost
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            if (args.Length > 0)
            {
                string command = args[0].TrimStart('/', '-').ToLowerInvariant();
                if (command == "install") return Install(args);
                if (command == "uninstall") return Uninstall();
                if (command == "console")
                {
                    if (args.Length < 2 || string.IsNullOrWhiteSpace(args[1]))
                    {
                        PrintUsage();
                        return 2;
                    }
                    File.WriteAllText(ApiPathFile, Path.GetFullPath(args[1]));
                    using (ApiBridgeService service = new ApiBridgeService())
                    {
                        service.OnStartPublic(new string[0]);
                        Console.WriteLine("Service is running. Press Enter to stop.");
                        Console.ReadLine();
                        service.OnStopPublic();
                    }
                    return 0;
                }

                PrintUsage();
                return 2;
            }

            ServiceBase.Run(new ApiBridgeService());
            return 0;
        }

        private static int Install(string[] args)
        {
            if (args.Length < 2 || string.IsNullOrWhiteSpace(args[1]))
            {
                PrintUsage();
                return 2;
            }

            string apiPath = Path.GetFullPath(args[1]);
            if (!File.Exists(apiPath))
            {
                Console.Error.WriteLine("MicroAPI executable not found: " + apiPath);
                return 2;
            }

            try
            {
                File.WriteAllText(ApiPathFile, apiPath);
                AssemblyInstaller installer = CreateInstaller();
                IDictionary state = new Hashtable();
                installer.Install(state);
                installer.Commit(state);
                Console.WriteLine("Installed and started Roblox Model Scanner API through Roblox.Wcf.");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Service installation failed: " + (ex.InnerException ?? ex).Message);
                return 1;
            }
        }

        private static int Uninstall()
        {
            try
            {
                AssemblyInstaller installer = CreateInstaller();
                installer.Uninstall(new Hashtable());
                Console.WriteLine("Roblox Model Scanner API service uninstalled.");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Service uninstallation failed: " + (ex.InnerException ?? ex).Message);
                return 1;
            }
        }

        private static AssemblyInstaller CreateInstaller()
        {
            return new AssemblyInstaller(Assembly.GetExecutingAssembly(), new string[0])
            {
                UseNewContext = true
            };
        }

        internal static string ApiPathFile
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ScannerApiPath.txt"); }
        }

        private static void PrintUsage()
        {
            Console.WriteLine("Usage:");
            Console.WriteLine("  Scanner.WcfServiceHost.exe install <path-to-Scanner.MicroApi.exe>");
            Console.WriteLine("  Scanner.WcfServiceHost.exe uninstall");
            Console.WriteLine("  Scanner.WcfServiceHost.exe console <path-to-Scanner.MicroApi.exe>");
        }
    }

    [RunInstaller(true)]
    public sealed class ScannerApiServiceInstaller : ServiceHostInstaller
    {
        public override string ServiceName { get { return "RMSAS.IIS"; } }
        public override string DisplayName { get { return "Roblox Model Scanner API"; } }
        public override string Description { get { return "Hosts the Roblox Model Scanner .NET 8 HTTP API."; } }
    }

    internal sealed class ApiBridgeService : ServiceBasePublic
    {
        private Process _apiProcess;

        public ApiBridgeService()
        {
            ServiceName = "RMSAS.IIS";
            CanStop = true;
            AutoLog = true;
        }

        protected override void OnStart(string[] args)
        {
            string apiPath = File.Exists(Program.ApiPathFile) ? File.ReadAllText(Program.ApiPathFile).Trim() : string.Empty;
            if (string.IsNullOrWhiteSpace(apiPath) || !File.Exists(apiPath))
                throw new FileNotFoundException("The configured Scanner.MicroApi executable could not be found. Reinstall the service with its full path.", apiPath);

            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = apiPath,
                WorkingDirectory = Path.GetDirectoryName(apiPath),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            _apiProcess = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            _apiProcess.OutputDataReceived += LogOutput;
            _apiProcess.ErrorDataReceived += LogOutput;
            _apiProcess.Start();
            _apiProcess.BeginOutputReadLine();
            _apiProcess.BeginErrorReadLine();
        }

        private void LogOutput(object sender, DataReceivedEventArgs args)
        {
            // Drain redirected streams so the child process cannot block on a full pipe.
        }

        protected override void OnStop()
        {
            if (_apiProcess == null) return;
            try
            {
                if (!_apiProcess.HasExited)
                {
                    _apiProcess.CloseMainWindow();
                    if (!_apiProcess.WaitForExit(10000)) _apiProcess.Kill();
                }
            }
            catch (InvalidOperationException) { }
            finally
            {
                _apiProcess.Dispose();
                _apiProcess = null;
            }
        }
    }
}
