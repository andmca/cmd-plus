// Copying text, using the console's own Edit menu. With text selected, Copy copies it. With nothing selected,
// Copy switches the console into its selection mode (Edit > Mark), which works even when a program like Copilot
// has taken over the mouse: then drag or use Shift+arrows to select, and press Enter or Copy again.

using System;

partial class MainForm
{
    void CopySelection()
    {
        var page = tabs.SelectedTab;
        var s = SessionOf(page);
        if (s == null) return;
        if (SelectionActive(s))
        {
            PostMessage(s.Hwnd, WM_SYSCOMMAND, (IntPtr)ID_CONSOLE_COPY, IntPtr.Zero);
            FlashTitle("Copied");
        }
        else
        {
            PostMessage(s.Hwnd, WM_SYSCOMMAND, (IntPtr)ID_CONSOLE_MARK, IntPtr.Zero);
            FlashTitle("Select text by dragging or with Shift+arrows, then press Enter to copy (Esc cancels)", 6000);
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
}
