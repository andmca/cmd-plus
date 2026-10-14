// CMD+ - a minimal tabbed host for real cmd.exe windows, with a shortcut for GitHub Copilot CLI.
// Written in C# 5 so it builds with the csc.exe that ships with Windows (.NET Framework 4.x):
//
//   C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /target:winexe /win32icon:CMDPlus.ico /resource:CMDPlus.ico,CMDPlus.ico /r:System.Windows.Forms.dll /r:System.Drawing.dll /out:CMDPlus.exe *.cs
//
// (build.cmd runs this for you.)
//
// Each tab is a genuine cmd.exe console window, stripped of its frame and re-parented into the tab.
//
//   Ctrl+T            new cmd tab
//   Ctrl+Shift+T      new Copilot tab (in the last folder used; the toolbar dropdown picks another)
//   drag with mouse   select text (works inside Copilot too; output pauses while text is selected)
//   Ctrl+Shift+C      copy the selected text (Enter also copies, Esc clears the selection)
//   Ctrl+Shift+V      paste the clipboard into the current tab (works inside Copilot too)
//   Ctrl+W            close tab (or middle-click it)
//   Ctrl+Tab          next tab / Ctrl+Shift+Tab previous tab
//
// Where things are (MainForm is one class split into partial files):
//
//   Program.cs                  entry point
//   Session.cs                  what each tab holds: the cmd process and its console window
//   MainForm.cs                 the window, toolbar, starting/closing tabs, sizing and focus
//   MainForm.Selection.cs       mouse-drag selection (works inside Copilot) and copy
//   MainForm.Paste.cs           paste, by typing the clipboard into the console's input
//   MainForm.Hotkeys.cs         keyboard shortcuts
//   MainForm.CopilotFolder.cs   the remembered folder for new Copilot tabs
//   MainForm.Native.cs          Win32 constants, structs and functions

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
