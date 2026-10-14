// Pasting, by typing the clipboard text into the console's input as if it came from the keyboard.
// Unlike right-click paste, this works inside Copilot too.

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Windows.Forms;

partial class MainForm
{
    void Paste()
    {
        var page = tabs.SelectedTab;
        var s = SessionOf(page);
        if (s == null) return;

        string text = null;
        try { if (Clipboard.ContainsText()) text = Clipboard.GetText(); } catch { }
        if (string.IsNullOrEmpty(text)) return;

        text = text.Replace("\r\n", "\n").Replace('\r', '\n');
        if (s.IsCopilot)
        {
            text = text.TrimEnd('\n');   // don't send the prompt just because the copied text ended with a new line
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

        if (!TypeIntoConsole(s, text))
            MessageBox.Show(this, "Couldn't paste into this tab.", "CMD+");
        FitAndFocus(page);
    }

    static bool TypeIntoConsole(Session s, string text)
    {
        var keys = new List<INPUT_RECORD>(text.Length * 2);
        foreach (char c in text) AddKeyPress(keys, c == '\n' ? '\r' : c);

        return InConsole(s.Proc, delegate
        {
            IntPtr input = CreateFile("CONIN$", GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
                                      IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (input == INVALID_HANDLE_VALUE) return false;
            try
            {
                const int chunk = 512;   // one very large write can fail
                for (int i = 0; i < keys.Count; i += chunk)
                {
                    var part = keys.GetRange(i, Math.Min(chunk, keys.Count - i)).ToArray();
                    uint written;
                    if (!WriteConsoleInput(input, part, (uint)part.Length, out written)) return false;
                }
                return true;
            }
            finally
            {
                CloseHandle(input);
            }
        });
    }

    // Adds key down and key up events for one character. A character that has no plain or Shift key on
    // this keyboard layout is sent as the character alone.
    static void AddKeyPress(List<INPUT_RECORD> keys, char c)
    {
        ushort vk = 0;
        uint state = 0;
        if (c == '\r') vk = VK_RETURN;
        else if (c == '\t') vk = VK_TAB;
        else
        {
            short scan = VkKeyScan(c);
            if (scan != -1 && ((scan >> 8) & 0xFE) == 0)
            {
                vk = (ushort)(scan & 0xFF);
                if (((scan >> 8) & 1) != 0) state = SHIFT_PRESSED;
            }
        }

        var down = new INPUT_RECORD
        {
            EventType = KEY_EVENT,
            KeyDown = 1,
            RepeatCount = 1,
            VirtualKeyCode = vk,
            VirtualScanCode = vk == 0 ? (ushort)0 : (ushort)MapVirtualKey(vk, 0),
            UnicodeChar = c,
            ControlKeyState = state
        };
        var up = down;
        up.KeyDown = 0;
        keys.Add(down);
        keys.Add(up);
    }
}
