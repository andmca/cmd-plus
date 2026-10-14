// Keyboard shortcuts. A low-level keyboard hook sees each key before the console does, and acts only while the
// CMD+ window is in front, so the shortcuts work whichever console has focus and never affect other apps.

using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

partial class MainForm
{
    IntPtr keyboardHook;
    LowLevelHookProc keyboardHookProc;   // kept in a field so it isn't garbage-collected
    int heldShortcutKey;                 // the shortcut key being held, so auto-repeat doesn't fire it again

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        BeginInvoke((MethodInvoker)delegate { FitAndFocus(tabs.SelectedTab); });
    }

    IntPtr KeyboardHook(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0 && GetForegroundWindow() == Handle)
        {
            var info = (KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(KBDLLHOOKSTRUCT));
            int msg = wParam.ToInt32();
            int vk = (int)info.vkCode;

            if ((msg == WM_KEYUP || msg == WM_SYSKEYUP) && vk == heldShortcutKey)
            {
                heldShortcutKey = 0;
                return (IntPtr)1;   // the console never saw the key go down, so it doesn't see it come up either
            }

            if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN)
            {
                if (vk == heldShortcutKey) return (IntPtr)1;   // auto-repeat
                MethodInvoker action = ShortcutFor(vk);
                if (action != null)
                {
                    heldShortcutKey = vk;
                    BeginInvoke(action);   // run it after the hook returns, as hooks must be quick
                    return (IntPtr)1;
                }
            }
        }
        return CallNextHookEx(keyboardHook, code, wParam, lParam);
    }

    // The action for a key pressed with the current modifiers, or null if it isn't a shortcut.
    MethodInvoker ShortcutFor(int vk)
    {
        bool ctrl = IsKeyDown(VK_CONTROL), shift = IsKeyDown(VK_SHIFT), alt = IsKeyDown(VK_MENU);
        if (!ctrl || alt) return null;

        switch ((Keys)vk)
        {
            case Keys.T: return shift ? (MethodInvoker)NewCopilotTab : NewCmdTab;
            case Keys.C: return shift ? (MethodInvoker)CopySelection : null;   // plain Ctrl+C stays with cmd
            case Keys.V: return shift ? (MethodInvoker)Paste : null;
            case Keys.W: return shift ? null : (MethodInvoker)delegate { CloseTab(tabs.SelectedTab); };
            case Keys.Tab: return delegate { SelectNextTab(shift ? -1 : 1); };
            default: return null;
        }
    }

    static bool IsKeyDown(int vk)
    {
        return (GetAsyncKeyState(vk) & 0x8000) != 0;
    }

    void SelectNextTab(int step)
    {
        int n = tabs.TabCount;
        if (n > 1) tabs.SelectedIndex = (tabs.SelectedIndex + step + n) % n;
    }
}
