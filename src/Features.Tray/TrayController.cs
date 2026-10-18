
using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace RobloxModelScanner.Features.Tray;

public static class TrayController
{
    private static readonly IntPtr ConsoleHandle = GetConsoleWindow();
    private static NotifyIcon? Icon;
    private static volatile bool Running = true;
    private static bool Hidden;

    
    /// <summary>Starts the notification-area icon and initially hides the console.</summary>
    public static void Start()
    {
        if (ConsoleHandle != IntPtr.Zero)
        {
            ShowWindow(ConsoleHandle, 0);
            Hidden = true;
        }

        Thread Thread = new(() =>
        {
            Icon = new NotifyIcon
            {
                Text = "Roblox Model Scanner",
                Icon = SystemIcons.Application,
                Visible = true,
                ContextMenuStrip = CreateMenu()
            };

            Icon.DoubleClick += (_, _) => ShowConsole();

            Application.Run();
        });

        Thread.IsBackground = true;
        Thread.SetApartmentState(ApartmentState.STA);
        Thread.Start();

        _ = MonitorWindowAsync();
    }


    private static ContextMenuStrip CreateMenu()
    {
        ContextMenuStrip Menu = new();

        Menu.Items.Add("Show Console", null, (_, _) => ShowConsole());

        Menu.Items.Add("Hide Console", null, (_, _) =>
        {
            if (ConsoleHandle != IntPtr.Zero)
            {
                ShowWindow(ConsoleHandle, 0);
                Hidden = true;
            }
        });

        Menu.Items.Add(new ToolStripSeparator());

        Menu.Items.Add("Exit", null, (_, _) =>
        {
            Running = false;
            Icon?.Dispose();
            Environment.Exit(0);
        });

        return Menu;
    }

    private static void ShowConsole()
    {
        if (ConsoleHandle == IntPtr.Zero)
            return;

        ShowWindow(ConsoleHandle, 9);
        SetForegroundWindow(ConsoleHandle);
        Hidden = false;
    }

    private static async Task MonitorWindowAsync()
    {
        while (Running)
        {
            await Task.Delay(200);

            if (ConsoleHandle == IntPtr.Zero || Hidden)
                continue;

            if (IsIconic(ConsoleHandle))
            {
                ShowWindow(ConsoleHandle, 0);
                Hidden = true;
            }
        }
    }

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr Window);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr Window, int Command);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr Window);
}
