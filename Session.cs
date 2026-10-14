// One tab: the cmd.exe process and its console window.

using System;
using System.Diagnostics;

class Session
{
    public Process Proc;
    public IntPtr Hwnd;
    public bool IsCopilot;
}
