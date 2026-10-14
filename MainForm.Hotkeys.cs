// Keyboard shortcuts. They're registered only while the CMD+ window is active, so they never affect other apps.

using System;
using System.Windows.Forms;

partial class MainForm
{
    const int HK_NEW = 1, HK_COPILOT = 2, HK_COPY = 3, HK_PASTE = 4, HK_CLOSE = 5, HK_NEXT = 6, HK_PREV = 7;

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        RegisterHotKey(Handle, HK_NEW, MOD_CONTROL | MOD_NOREPEAT, (uint)Keys.T);
        RegisterHotKey(Handle, HK_COPILOT, MOD_CONTROL | MOD_SHIFT | MOD_NOREPEAT, (uint)Keys.T);
        RegisterHotKey(Handle, HK_COPY, MOD_CONTROL | MOD_SHIFT | MOD_NOREPEAT, (uint)Keys.C);
        RegisterHotKey(Handle, HK_PASTE, MOD_CONTROL | MOD_SHIFT | MOD_NOREPEAT, (uint)Keys.V);
        RegisterHotKey(Handle, HK_CLOSE, MOD_CONTROL | MOD_NOREPEAT, (uint)Keys.W);
        RegisterHotKey(Handle, HK_NEXT, MOD_CONTROL | MOD_NOREPEAT, (uint)Keys.Tab);
        RegisterHotKey(Handle, HK_PREV, MOD_CONTROL | MOD_SHIFT | MOD_NOREPEAT, (uint)Keys.Tab);
        BeginInvoke((MethodInvoker)delegate { FitAndFocus(tabs.SelectedTab); });
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        for (int id = HK_NEW; id <= HK_PREV; id++) UnregisterHotKey(Handle, id);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY)
        {
            switch (m.WParam.ToInt32())
            {
                case HK_NEW: NewCmdTab(); break;
                case HK_COPILOT: NewCopilotTab(); break;
                case HK_COPY: CopySelection(); break;
                case HK_PASTE: Paste(); break;
                case HK_CLOSE: CloseTab(tabs.SelectedTab); break;
                case HK_NEXT: SelectNextTab(1); break;
                case HK_PREV: SelectNextTab(-1); break;
            }
            return;
        }
        base.WndProc(ref m);
    }

    void SelectNextTab(int step)
    {
        int n = tabs.TabCount;
        if (n > 1) tabs.SelectedIndex = (tabs.SelectedIndex + step + n) % n;
    }
}
