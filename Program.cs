// CMD+: a tabbed window for cmd.exe, with a shortcut for GitHub Copilot CLI.
// Each tab holds a real cmd.exe console window, stripped of its frame and moved into the tab.
//
// Keep to C# 5, so it builds with the csc.exe that ships with Windows (see build.cmd).

using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

static class Program
{
    [STAThread]
    static void Main()
    {
        // Use real pixel coordinates, so mouse positions line up with the console windows at any display scaling.
        try { SetProcessDPIAware(); } catch { }
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm());
    }

    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
}
