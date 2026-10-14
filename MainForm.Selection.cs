// Selecting text with the mouse (even inside Copilot) and copying it.

using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

partial class MainForm
{
    // Mouse selection: CMD+ watches left-button drags over the console itself, so that dragging
    // selects text even when a program like Copilot has taken over the mouse.
    enum Drag { None, Pending, Selecting }
    Drag drag = Drag.None;
    Session dragSession;
    POINT dragStart;
    IntPtr mouseHook = IntPtr.Zero;
    LowLevelMouseProc mouseHookProc;   // kept in a field so it isn't garbage-collected

    // Copies whatever is selected in the tab, using the console's own Edit > Copy.
    void CopySelection(TabPage page)
    {
        var s = SessionOf(page);
        if (s == null) return;
        if (SelectionActive(s))
        {
            PostMessage(s.Hwnd, WM_SYSCOMMAND, (IntPtr)ID_CONSOLE_COPY, IntPtr.Zero);
            FlashTitle("Copied");
        }
        else
        {
            FlashTitle("Nothing selected - drag over text first");
        }
        FitAndFocus(page);
    }

    static bool SelectionActive(Session s)
    {
        if (!AttachConsole((uint)s.Proc.Id)) return false;
        SetConsoleCtrlHandler(IntPtr.Zero, true);
        try
        {
            CONSOLE_SELECTION_INFO info;
            return GetConsoleSelectionInfo(out info) && (info.Flags & CONSOLE_SELECTION_IN_PROGRESS) != 0;
        }
        finally
        {
            FreeConsole();
            SetConsoleCtrlHandler(IntPtr.Zero, false);
        }
    }

    // How a drag over the console becomes a selection:
    //  - Button down over the console's text area is held back for a moment.
    //  - If the mouse then moves a few pixels, it's a drag: the console is switched into its selection
    //    ("Mark") mode and the button press is replayed, so the console selects text instead of passing
    //    the mouse to the program. This works even when Copilot has taken over the mouse.
    //  - If the button comes back up without moving, it was just a click: it's replayed unchanged
    //    (or, if text is already selected, the click clears the selection).
    IntPtr MouseHook(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            var info = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(MSLLHOOKSTRUCT));
            int msg = wParam.ToInt32();
            bool injected = (info.flags & LLMHF_INJECTED) != 0;

            if (!injected)
            {
                if (msg == WM_LBUTTONDOWN && drag == Drag.None)
                {
                    var s = SessionOf(tabs.SelectedTab);
                    if (s != null && OverConsoleText(s, info.pt) && !ModifierHeld())
                    {
                        drag = Drag.Pending;
                        dragSession = s;
                        dragStart = info.pt;
                        return (IntPtr)1;   // hold the press back for now
                    }
                }
                else if (msg == WM_MOUSEMOVE && drag == Drag.Pending)
                {
                    if (Math.Abs(info.pt.X - dragStart.X) > 3 || Math.Abs(info.pt.Y - dragStart.Y) > 3)
                    {
                        drag = Drag.Selecting;
                        PostMessage(dragSession.Hwnd, WM_SYSCOMMAND, (IntPtr)ID_CONSOLE_MARK, IntPtr.Zero);
                        // Replay the press where the drag started, then put the pointer back where it is now.
                        SendMouse(dragStart, MOUSEEVENTF_LEFTDOWN, info.pt, 0);
                        return (IntPtr)1;
                    }
                }
                else if (msg == WM_LBUTTONUP && drag == Drag.Pending)
                {
                    drag = Drag.None;
                    var s = dragSession;
                    var at = dragStart;
                    BeginInvoke((MethodInvoker)delegate
                    {
                        if (SelectionActive(s)) ClearSelection(s);
                        else SendMouse(at, MOUSEEVENTF_LEFTDOWN, at, MOUSEEVENTF_LEFTUP);
                    });
                    return (IntPtr)1;
                }
                else if (msg == WM_LBUTTONUP && drag == Drag.Selecting)
                {
                    drag = Drag.None;   // let the release through so the console finishes the selection
                }
            }
        }
        return CallNextHookEx(mouseHook, code, wParam, lParam);
    }

    bool OverConsoleText(Session s, POINT screen)
    {
        if (GetForegroundWindow() != Handle) return false;
        if (WindowFromPoint(screen) != s.Hwnd) return false;
        // Only the text area; leave the scrollbar alone.
        POINT client = screen;
        ScreenToClient(s.Hwnd, ref client);
        RECT r;
        GetClientRect(s.Hwnd, out r);
        return client.X >= r.Left && client.X < r.Right && client.Y >= r.Top && client.Y < r.Bottom;
    }

    static bool ModifierHeld()
    {
        return (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0
            || (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0
            || (GetAsyncKeyState(VK_MENU) & 0x8000) != 0;
    }

    static void ClearSelection(Session s)
    {
        PostMessage(s.Hwnd, WM_KEYDOWN, (IntPtr)VK_ESCAPE, (IntPtr)0x00010001);
        PostMessage(s.Hwnd, WM_KEYUP, (IntPtr)VK_ESCAPE, unchecked((IntPtr)(int)0xC0010001));
    }

    // Sends a button event at one point, then moves the pointer to another (optionally with a second button event).
    static void SendMouse(POINT first, uint firstButton, POINT then, uint thenButton)
    {
        var inputs = new INPUT[] { MouseInput(first, firstButton), MouseInput(then, thenButton) };
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));
    }

    static INPUT MouseInput(POINT p, uint button)
    {
        int vx = GetSystemMetrics(SM_XVIRTUALSCREEN), vy = GetSystemMetrics(SM_YVIRTUALSCREEN);
        int vw = Math.Max(2, GetSystemMetrics(SM_CXVIRTUALSCREEN)), vh = Math.Max(2, GetSystemMetrics(SM_CYVIRTUALSCREEN));
        var input = new INPUT();
        input.type = INPUT_MOUSE;
        input.mi.dx = (int)(((long)(p.X - vx) * 65535) / (vw - 1));
        input.mi.dy = (int)(((long)(p.Y - vy) * 65535) / (vh - 1));
        input.mi.dwFlags = MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK | button;
        return input;
    }
}
