// Pasting: types the clipboard text into the console as keyboard input.

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Windows.Forms;

partial class MainForm
{
    // Types the clipboard text straight into the console's input, as if it were typed on the keyboard.
    void Paste(TabPage page)
    {
        var s = SessionOf(page);
        if (s == null) return;

        string text = null;
        try { if (Clipboard.ContainsText()) text = Clipboard.GetText(); } catch { }
        if (string.IsNullOrEmpty(text)) return;

        text = text.Replace("\r\n", "\n").Replace('\r', '\n');
        if (s.IsCopilot)
        {
            text = text.TrimEnd('\n');   // don't send the prompt just because the copied text ended with a newline
            if (text.Contains("\n"))
            {
                var answer = MessageBox.Show(this,
                    "The clipboard has several lines. In Copilot, a new line may send the prompt early.\n\n" +
                    "Yes = join it into one line\nNo = paste it as it is",
                    "Paste", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (answer == DialogResult.Cancel) return;
                if (answer == DialogResult.Yes) text = Regex.Replace(text.Trim(), @"\s*\n\s*", " ");
            }
        }
        if (text.Length == 0) return;

        if (!SendText(s, text))
            MessageBox.Show(this, "Couldn't paste into this tab.", "CMD+");
        FitAndFocus(page);
    }

    static bool SendText(Session s, string text)
    {
        if (!AttachConsole((uint)s.Proc.Id)) return false;
        SetConsoleCtrlHandler(IntPtr.Zero, true);   // ignore a stray Ctrl+C while briefly attached
        IntPtr input = CreateFile("CONIN$", GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
                                  IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
        try
        {
            if (input == INVALID_HANDLE_VALUE) return false;

            var records = new List<INPUT_RECORD>(text.Length * 2);
            foreach (char ch in text)
            {
                char c = ch == '\n' ? '\r' : ch;
                ushort vk = 0;
                uint state = 0;
                if (c == '\r') vk = VK_RETURN;
                else if (c == '\t') vk = VK_TAB;
                else
                {
                    short scan = VkKeyScan(c);
                    if (scan != -1 && ((scan >> 8) & 0xFE) == 0)   // a plain or Shift key on this layout
                    {
                        vk = (ushort)(scan & 0xFF);
                        if (((scan >> 8) & 1) != 0) state = SHIFT_PRESSED;
                    }
                }
                ushort sc = vk == 0 ? (ushort)0 : (ushort)MapVirtualKey(vk, 0);
                records.Add(Key(true, vk, sc, c, state));
                records.Add(Key(false, vk, sc, c, state));
            }

            var all = records.ToArray();
            const int chunk = 512;
            for (int i = 0; i < all.Length; i += chunk)
            {
                int n = Math.Min(chunk, all.Length - i);
                var part = new INPUT_RECORD[n];
                Array.Copy(all, i, part, 0, n);
                uint written;
                if (!WriteConsoleInput(input, part, (uint)n, out written)) return false;
            }
            return true;
        }
        finally
        {
            if (input != INVALID_HANDLE_VALUE) CloseHandle(input);
            FreeConsole();
            SetConsoleCtrlHandler(IntPtr.Zero, false);
        }
    }

    static INPUT_RECORD Key(bool down, ushort vk, ushort scan, char c, uint state)
    {
        var r = new INPUT_RECORD();
        r.EventType = KEY_EVENT;
        r.KeyDown = down ? 1 : 0;
        r.RepeatCount = 1;
        r.VirtualKeyCode = vk;
        r.VirtualScanCode = scan;
        r.UnicodeChar = c;
        r.ControlKeyState = state;
        return r;
    }
}
