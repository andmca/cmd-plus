// Selecting text by dragging the mouse, which works even when a program like Copilot has taken over the mouse,
// and copying it.

using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

partial class MainForm
{
    enum Drag { None, Pending, Selecting }
    Drag drag;
    Session dragSession;
    POINT dragStart;
    IntPtr mouseHook;
    LowLevelMouseProc mouseHookProc;   // kept in a field so it isn't garbage-collected

    void CopySelection()
    {
        var page = tabs.SelectedTab;
        var s = SessionOf(page);
        if (s == null) return;
        if (SelectionActive(s))
        {
            PostMessage(s.Hwnd, WM_SYSCOMMAND, (IntPtr)ID_CONSOLE_COPY, IntPtr.Zero);   // the console's own Edit > Copy
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
        return InConsole(s.Proc, delegate
        {
            CONSOLE_SELECTION_INFO info;
            return GetConsoleSelectionInfo(out info) && (info.Flags & CONSOLE_SELECTION_IN_PROGRESS) != 0;
        });
    }

    IntPtr MouseHook(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            var info = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(MSLLHOOKSTRUCT));
            bool injected = (info.flags & LLMHF_INJECTED) != 0;   // e.g. our own replayed clicks
            if (!injected && HoldBackMouse(wParam.ToInt32(), info.pt)) return (IntPtr)1;
        }
        return CallNextHookEx(mouseHook, code, wParam, lParam);
    }

    // How a drag over the console becomes a selection:
    //  - A left press over the console's text is held back.
    //  - If the mouse then moves a few pixels, the console is switched into its selection ("Mark") mode and the
    //    press is replayed, so the console selects text instead of passing the mouse to the program.
    //  - If it's released without moving, it was a click: it's replayed unchanged, or clears the selection if
    //    there is one.
    // Returns true to keep the event from the console.
    bool HoldBackMouse(int msg, POINT pt)
    {
        if (msg == WM_LBUTTONDOWN && drag == Drag.None)
        {
            var s = SessionOf(tabs.SelectedTab);
            if (s == null || !OverConsoleText(s, pt) || ModifierHeld()) return false;
            drag = Drag.Pending;
            dragSession = s;
            dragStart = pt;
            return true;
        }

        if (msg == WM_MOUSEMOVE && drag == Drag.Pending)
        {
            if (Math.Abs(pt.X - dragStart.X) <= 3 && Math.Abs(pt.Y - dragStart.Y) <= 3) return false;
            drag = Drag.Selecting;
            PostMessage(dragSession.Hwnd, WM_SYSCOMMAND, (IntPtr)ID_CONSOLE_MARK, IntPtr.Zero);
            // Replay the press where the drag started, then put the pointer back where it is now.
            SendMouse(dragStart, MOUSEEVENTF_LEFTDOWN, pt, 0);
            return true;
        }

        if (msg == WM_LBUTTONUP && drag == Drag.Pending)
        {
            drag = Drag.None;
            var s = dragSession;
            var at = dragStart;
            BeginInvoke((MethodInvoker)delegate
            {
                if (SelectionActive(s)) ClearSelection(s);
                else SendMouse(at, MOUSEEVENTF_LEFTDOWN, at, MOUSEEVENTF_LEFTUP);
            });
            return true;
        }

        if (msg == WM_LBUTTONUP && drag == Drag.Selecting)
            drag = Drag.None;   // let the release through so the console finishes the selection
        return false;
    }

    // Only the console's text area, so the scrollbar still works normally.
    bool OverConsoleText(Session s, POINT screen)
    {
        if (GetForegroundWindow() != Handle || WindowFromPoint(screen) != s.Hwnd) return false;
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
        // Esc pressed and released (repeat count 1, scan code 1).
        PostMessage(s.Hwnd, WM_KEYDOWN, (IntPtr)VK_ESCAPE, (IntPtr)0x00010001);
        PostMessage(s.Hwnd, WM_KEYUP, (IntPtr)VK_ESCAPE, unchecked((IntPtr)(int)0xC0010001));
    }

    // Sends a button event at one point, then moves the pointer to another (optionally with a second button event).
    static void SendMouse(POINT first, uint firstButton, POINT then, uint thenButton)
    {
        var inputs = new[] { MouseInput(first, firstButton), MouseInput(then, thenButton) };
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));
    }

    // Absolute positions are scaled to 0-65535 across the whole virtual desktop, so this works on any monitor.
    static INPUT MouseInput(POINT p, uint button)
    {
        int vx = GetSystemMetrics(SM_XVIRTUALSCREEN), vy = GetSystemMetrics(SM_YVIRTUALSCREEN);
        int vw = Math.Max(2, GetSystemMetrics(SM_CXVIRTUALSCREEN)), vh = Math.Max(2, GetSystemMetrics(SM_CYVIRTUALSCREEN));
        var input = new INPUT { type = INPUT_MOUSE };
        input.mi.dx = (int)(((long)(p.X - vx) * 65535) / (vw - 1));
        input.mi.dy = (int)(((long)(p.Y - vy) * 65535) / (vh - 1));
        input.mi.dwFlags = MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK | button;
        return input;
    }
}
