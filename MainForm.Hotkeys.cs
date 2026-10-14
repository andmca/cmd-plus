// Keyboard shortcuts, registered only while the CMD+ window is active.

using System;
using System.Windows.Forms;

partial class MainForm
{
    const int HK_NEW = 1, HK_CLOSE = 2, HK_NEXT = 3, HK_PREV = 4, HK_COPILOT = 5, HK_COPY = 6, HK_PASTE = 7;

    // Registered only while this window is active, so they never affect other apps.
    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        RegisterHotKey(Handle, HK_NEW, MOD_CONTROL | MOD_NOREPEAT, (uint)Keys.T);
        RegisterHotKey(Handle, HK_COPILOT, MOD_CONTROL | MOD_SHIFT | MOD_NOREPEAT, (uint)Keys.T);
        RegisterHotKey(Handle, HK_CLOSE, MOD_CONTROL | MOD_NOREPEAT, (uint)Keys.W);
        RegisterHotKey(Handle, HK_NEXT, MOD_CONTROL | MOD_NOREPEAT, (uint)Keys.Tab);
        RegisterHotKey(Handle, HK_PREV, MOD_CONTROL | MOD_SHIFT | MOD_NOREPEAT, (uint)Keys.Tab);
        RegisterHotKey(Handle, HK_COPY, MOD_CONTROL | MOD_SHIFT | MOD_NOREPEAT, (uint)Keys.C);
        RegisterHotKey(Handle, HK_PASTE, MOD_CONTROL | MOD_SHIFT | MOD_NOREPEAT, (uint)Keys.V);
        BeginInvoke((MethodInvoker)delegate { FitAndFocus(tabs.SelectedTab); });
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        for (int id = HK_NEW; id <= HK_PASTE; id++) UnregisterHotKey(Handle, id);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY)
        {
            int n = tabs.TabCount;
            switch (m.WParam.ToInt32())
            {
                case HK_NEW: NewCmdTab(); break;
                case HK_COPILOT: NewCopilotTab(copilotFolder); break;
                case HK_CLOSE: CloseTab(tabs.SelectedTab); break;
                case HK_NEXT: if (n > 1) tabs.SelectedIndex = (tabs.SelectedIndex + 1) % n; break;
                case HK_PREV: if (n > 1) tabs.SelectedIndex = (tabs.SelectedIndex - 1 + n) % n; break;
                case HK_COPY: CopySelection(tabs.SelectedTab); break;
                case HK_PASTE: Paste(tabs.SelectedTab); break;
            }
            return;
        }
        base.WndProc(ref m);
    }
}
