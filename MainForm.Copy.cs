// Copying the text selected in the current tab, using the console's own Edit > Copy.

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
}
