// CMDPlus.cs - CMD+, a minimal tabbed host for real cmd.exe windows, with a shortcut for GitHub Copilot CLI.
// Written in C# 5 so it builds with the csc.exe that ships with Windows (.NET Framework 4.x):
//
//   C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /target:winexe /win32icon:CMDPlus.ico /resource:CMDPlus.ico,CMDPlus.ico /r:System.Windows.Forms.dll /r:System.Drawing.dll /out:CMDPlus.exe CMDPlus.cs
//
// (build.cmd runs this for you.)
//
// Each tab is a genuine cmd.exe console window, stripped of its frame and re-parented into the tab.
//
//   Ctrl+T            new cmd tab
//   Ctrl+Shift+T      new Copilot tab (in the last folder used; the toolbar dropdown picks another)
//   drag with mouse   select text (works inside Copilot too; output pauses while text is selected)
//   Ctrl+Shift+C      copy the selected text (Enter also copies, Esc clears the selection)
//   Ctrl+Shift+V      paste the clipboard into the current tab (works inside Copilot too)
//   Ctrl+W            close tab (or middle-click it)
//   Ctrl+Tab          next tab / Ctrl+Shift+Tab previous tab

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

static class Program
{
    [STAThread]
    static void Main()
    {
        // Use real pixel coordinates, so mouse positions line up with the console windows at any display scaling.
        try { SetProcessDPIAware(); } catch { }
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm());
    }

    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
}

class Session
{
    public Process Proc;
    public IntPtr Hwnd;
    public bool IsCopilot;
}

class MainForm : Form
{
    readonly TabControl tabs = new TabControl();
    readonly ToolStripSplitButton copilotButton = new ToolStripSplitButton();
    static readonly string AppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    readonly string settingsFile = Path.Combine(AppData, "CMDPlus", "copilot-folder.txt");
    // Where the setting lived before the app was renamed from Tabs to CMD+; read once if the new file isn't there yet.
    readonly string legacySettingsFile = Path.Combine(AppData, "Tabs", "copilot-folder.txt");
    static readonly string UserProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    readonly string baseTitle = IsElevated() ? "CMD+ (Administrator)" : "CMD+";
    readonly System.Windows.Forms.Timer titleTimer = new System.Windows.Forms.Timer { Interval = 1500 };
    string copilotFolder;
    int counter;
    bool closing;

    // Mouse selection: CMD+ watches left-button drags over the console itself, so that dragging
    // selects text even when a program like Copilot has taken over the mouse.
    enum Drag { None, Pending, Selecting }
    Drag drag = Drag.None;
    Session dragSession;
    POINT dragStart;
    IntPtr mouseHook = IntPtr.Zero;
    LowLevelMouseProc mouseHookProc;   // kept in a field so it isn't garbage-collected

    const int HK_NEW = 1, HK_CLOSE = 2, HK_NEXT = 3, HK_PREV = 4, HK_COPILOT = 5, HK_COPY = 6, HK_PASTE = 7;

    public MainForm()
    {
        Text = baseTitle;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);
        Size = new Size(1100, 650);
        titleTimer.Tick += delegate { titleTimer.Stop(); Text = baseTitle; };
        StartPosition = FormStartPosition.CenterScreen;
        Icon = LoadAppIcon();

        copilotFolder = LoadCopilotFolder();

        var strip = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden };
        strip.Items.Add(new ToolStripButton("+ cmd  (Ctrl+T)", null, delegate { NewCmdTab(); }));

        copilotButton.Text = "+ Copilot  (Ctrl+Shift+T)";
        copilotButton.ButtonClick += delegate { NewCopilotTab(copilotFolder); };
        copilotButton.DropDownItems.Add("Start Copilot in another folder...", null, delegate { ChooseFolderAndStartCopilot(); });
        UpdateCopilotTooltip();
        strip.Items.Add(copilotButton);

        strip.Items.Add(new ToolStripSeparator());
        var copyButton = new ToolStripButton("Copy  (Ctrl+Shift+C)", null, delegate { CopySelection(tabs.SelectedTab); });
        copyButton.ToolTipText = "Drag over text to select it, then copy. Esc clears the selection.";
        strip.Items.Add(copyButton);
        strip.Items.Add(new ToolStripButton("Paste  (Ctrl+Shift+V)", null, delegate { Paste(tabs.SelectedTab); }));
        strip.Items.Add(new ToolStripSeparator());
        strip.Items.Add(new ToolStripButton("Close tab  (Ctrl+W)", null, delegate { CloseTab(tabs.SelectedTab); }));

        tabs.Dock = DockStyle.Fill;
        tabs.SelectedIndexChanged += delegate { BeginInvoke((MethodInvoker)delegate { FitAndFocus(tabs.SelectedTab); }); };
        tabs.MouseUp += OnTabMouseUp;

        Controls.Add(tabs);
        Controls.Add(strip);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        mouseHookProc = MouseHook;
        mouseHook = SetWindowsHookEx(WH_MOUSE_LL, mouseHookProc, GetModuleHandle(null), 0);
        NewCmdTab();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        if (mouseHook != IntPtr.Zero) UnhookWindowsHookEx(mouseHook);
        base.OnFormClosed(e);
    }

    // The icon is embedded in the exe (see build.cmd); fall back to cmd's icon if it's missing.
    static Icon LoadAppIcon()
    {
        try
        {
            var stream = typeof(MainForm).Assembly.GetManifestResourceStream("CMDPlus.ico");
            if (stream != null) return new Icon(stream);
        }
        catch { }
        try { return Icon.ExtractAssociatedIcon(Path.Combine(Environment.SystemDirectory, "cmd.exe")); } catch { }
        return null;
    }

    // Tabs opened from an elevated window are elevated too, so say so in the title.
    static bool IsElevated()
    {
        try
        {
            using (var identity = System.Security.Principal.WindowsIdentity.GetCurrent())
                return new System.Security.Principal.WindowsPrincipal(identity)
                    .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }

    // ---------- Starting tabs ----------

    void NewCmdTab()
    {
        NewTab("cmd " + (++counter), null, UserProfile, false);
    }

    void NewCopilotTab(string folder)
    {
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) folder = UserProfile;
        string name = Path.GetFileName(folder.TrimEnd('\\'));
        if (name.Length == 0) name = folder;
        // /k keeps the cmd prompt open when Copilot exits, so the tab stays usable.
        NewTab("copilot: " + name, "/k copilot", folder, true);
    }

    void ChooseFolderAndStartCopilot()
    {
        using (var dlg = new FolderBrowserDialog())
        {
            dlg.Description = "Folder to start Copilot in";
            dlg.SelectedPath = copilotFolder;
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            copilotFolder = dlg.SelectedPath;
        }
        SaveCopilotFolder();
        UpdateCopilotTooltip();
        NewCopilotTab(copilotFolder);
    }

    void NewTab(string title, string arguments, string folder, bool isCopilot)
    {
        var psi = new ProcessStartInfo("cmd.exe");
        if (arguments != null) psi.Arguments = arguments;
        psi.UseShellExecute = true;
        psi.WindowStyle = ProcessWindowStyle.Hidden;   // avoid a flash before it's moved into the tab
        psi.WorkingDirectory = folder;

        Process p = Process.Start(psi);
        IntPtr h = FindConsoleWindow(p);
        if (h == IntPtr.Zero)
        {
            try { p.Kill(); } catch { }
            MessageBox.Show(this, "Couldn't attach to the cmd.exe window.", "CMD+");
            return;
        }

        var page = new TabPage(title);
        page.BackColor = Color.Black;
        page.Tag = new Session { Proc = p, Hwnd = h, IsCopilot = isCopilot };
        tabs.TabPages.Add(page);

        int style = GetWindowLong(h, GWL_STYLE);
        style &= ~(WS_CAPTION | WS_THICKFRAME | WS_SYSMENU | WS_MINIMIZEBOX | WS_MAXIMIZEBOX);
        SetWindowLong(h, GWL_STYLE, style);
        SetParent(h, page.Handle);
        ShowWindow(h, SW_SHOW);

        page.Resize += delegate { Fit(page); };

        // When cmd exits (typing "exit", or the tab being closed), remove its tab.
        p.EnableRaisingEvents = true;
        p.Exited += delegate
        {
            if (closing) return;
            try { BeginInvoke((MethodInvoker)delegate { RemoveTab(page); }); } catch { }
        };

        tabs.SelectedTab = page;
        FitAndFocus(page);
    }

    // Ask Windows for the console window that belongs to the new cmd process.
    static IntPtr FindConsoleWindow(Process p)
    {
        for (int i = 0; i < 100; i++)
        {
            if (p.HasExited) return IntPtr.Zero;
            if (AttachConsole((uint)p.Id))
            {
                IntPtr h = GetConsoleWindow();
                FreeConsole();
                if (h != IntPtr.Zero) return h;
            }
            Thread.Sleep(50);
        }
        return IntPtr.Zero;
    }

    // ---------- Copy and paste ----------

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

    void FlashTitle(string message)
    {
        Text = baseTitle + "  -  " + message;
        titleTimer.Stop();
        titleTimer.Start();
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

    // ---------- Tab housekeeping ----------

    static Session SessionOf(TabPage page)
    {
        return page == null ? null : page.Tag as Session;
    }

    void Fit(TabPage page)
    {
        var s = SessionOf(page);
        if (s == null) return;
        MoveWindow(s.Hwnd, 0, 0, page.ClientSize.Width, page.ClientSize.Height, true);
    }

    void FitAndFocus(TabPage page)
    {
        var s = SessionOf(page);
        if (s == null) return;
        Fit(page);

        // The console lives on another process's thread, so borrow its input queue to hand it focus.
        uint target = GetWindowThreadProcessId(s.Hwnd, IntPtr.Zero);
        uint me = GetCurrentThreadId();
        AttachThreadInput(me, target, true);
        SetFocus(s.Hwnd);
        AttachThreadInput(me, target, false);
    }

    void CloseTab(TabPage page)
    {
        var s = SessionOf(page);
        if (s == null) return;

        tabs.SelectedTab = page;   // make sure the user can see which tab they're closing
        string message = s.IsCopilot
            ? "Close \"" + page.Text + "\"?\n\nThis will also end the Copilot session running in it."
            : "Close \"" + page.Text + "\"?";
        var answer = MessageBox.Show(this, message, "Close tab", MessageBoxButtons.YesNo,
            s.IsCopilot ? MessageBoxIcon.Warning : MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
        if (answer != DialogResult.Yes)
        {
            FitAndFocus(page);
            return;
        }

        // Closing the console window ends cmd and anything running in it; the Exited handler removes the tab.
        PostMessage(s.Hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
    }

    void RemoveTab(TabPage page)
    {
        if (!tabs.TabPages.Contains(page)) return;
        tabs.TabPages.Remove(page);
        page.Dispose();
        if (tabs.TabPages.Count == 0) Close();
        else FitAndFocus(tabs.SelectedTab);
    }

    void OnTabMouseUp(object sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Middle) return;
        for (int i = 0; i < tabs.TabCount; i++)
        {
            if (tabs.GetTabRect(i).Contains(e.Location))
            {
                CloseTab(tabs.TabPages[i]);
                return;
            }
        }
    }

    // ---------- Remembered Copilot folder ----------

    string LoadCopilotFolder()
    {
        try
        {
            string file = File.Exists(settingsFile) ? settingsFile : legacySettingsFile;
            if (File.Exists(file))
            {
                string f = File.ReadAllText(file).Trim();
                if (Directory.Exists(f)) return f;
            }
        }
        catch { }
        return UserProfile;
    }

    void SaveCopilotFolder()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(settingsFile));
            File.WriteAllText(settingsFile, copilotFolder);
        }
        catch { }
    }

    void UpdateCopilotTooltip()
    {
        copilotButton.ToolTipText = "Start Copilot in " + copilotFolder;
    }

    // ---------- Keyboard shortcuts ----------

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

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Closing the window ends every tab, so confirm that too (not when Windows is shutting down).
        if (tabs.TabCount > 0 && e.CloseReason == CloseReason.UserClosing)
        {
            int copilotTabs = 0;
            foreach (TabPage p in tabs.TabPages)
            {
                var ps = SessionOf(p);
                if (ps != null && ps.IsCopilot) copilotTabs++;
            }
            string message = tabs.TabCount == 1 ? "Close CMD+ and its open tab?" : "Close CMD+ and all " + tabs.TabCount + " open tabs?";
            if (copilotTabs > 0)
                message += "\n\nThis will also end " + (copilotTabs == 1 ? "the Copilot session" : "all " + copilotTabs + " Copilot sessions") + ".";
            var answer = MessageBox.Show(this, message, "Close CMD+", MessageBoxButtons.YesNo,
                copilotTabs > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
            if (answer != DialogResult.Yes)
            {
                e.Cancel = true;
                FitAndFocus(tabs.SelectedTab);
                return;
            }
        }

        closing = true;
        foreach (TabPage page in tabs.TabPages)
        {
            var s = SessionOf(page);
            if (s == null) continue;
            ShowWindow(s.Hwnd, SW_HIDE);
            SetParent(s.Hwnd, IntPtr.Zero);   // detach first so its teardown never waits on this window
            PostMessage(s.Hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        }
        base.OnFormClosing(e);
    }

    // ---------- Win32 ----------
    const int GWL_STYLE = -16;
    const int WS_CAPTION = 0x00C00000, WS_THICKFRAME = 0x00040000, WS_SYSMENU = 0x00080000;
    const int WS_MINIMIZEBOX = 0x00020000, WS_MAXIMIZEBOX = 0x00010000;
    const int SW_HIDE = 0, SW_SHOW = 5;
    const int WM_CLOSE = 0x0010, WM_SYSCOMMAND = 0x0112, WM_HOTKEY = 0x0312;
    const int ID_CONSOLE_COPY = 0xFFF0, ID_CONSOLE_MARK = 0xFFF2;   // console window's Edit > Copy / Mark
    const int WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101;
    const int WM_MOUSEMOVE = 0x0200, WM_LBUTTONDOWN = 0x0201, WM_LBUTTONUP = 0x0202;
    const int WH_MOUSE_LL = 14;
    const uint LLMHF_INJECTED = 0x1, CONSOLE_SELECTION_IN_PROGRESS = 0x1;
    const int VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12, VK_ESCAPE = 0x1B;
    const uint INPUT_MOUSE = 0;
    const uint MOUSEEVENTF_MOVE = 0x0001, MOUSEEVENTF_LEFTDOWN = 0x0002, MOUSEEVENTF_LEFTUP = 0x0004;
    const uint MOUSEEVENTF_VIRTUALDESK = 0x4000, MOUSEEVENTF_ABSOLUTE = 0x8000;
    const int SM_XVIRTUALSCREEN = 76, SM_YVIRTUALSCREEN = 77, SM_CXVIRTUALSCREEN = 78, SM_CYVIRTUALSCREEN = 79;

    delegate IntPtr LowLevelMouseProc(int code, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    struct MSLLHOOKSTRUCT { public POINT pt; public uint mouseData, flags, time; public IntPtr extraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    struct INPUT { public uint type; public MOUSEINPUT mi; }

    [StructLayout(LayoutKind.Sequential)]
    struct CONSOLE_SELECTION_INFO { public uint Flags; public short AnchorX, AnchorY, Left, Top, Right, Bottom; }
    const uint MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_NOREPEAT = 0x4000;
    const ushort KEY_EVENT = 0x0001, VK_RETURN = 0x0D, VK_TAB = 0x09;
    const uint SHIFT_PRESSED = 0x0010;
    const uint GENERIC_READ = 0x80000000, GENERIC_WRITE = 0x40000000;
    const uint FILE_SHARE_READ = 1, FILE_SHARE_WRITE = 2, OPEN_EXISTING = 3;
    static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);

    [StructLayout(LayoutKind.Explicit, Size = 20, CharSet = CharSet.Unicode)]
    struct INPUT_RECORD
    {
        [FieldOffset(0)] public ushort EventType;
        [FieldOffset(4)] public int KeyDown;
        [FieldOffset(8)] public ushort RepeatCount;
        [FieldOffset(10)] public ushort VirtualKeyCode;
        [FieldOffset(12)] public ushort VirtualScanCode;
        [FieldOffset(14)] public char UnicodeChar;
        [FieldOffset(16)] public uint ControlKeyState;
    }

    [DllImport("user32.dll")] static extern IntPtr SetParent(IntPtr child, IntPtr parent);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int index);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int index, int value);
    [DllImport("user32.dll")] static extern bool MoveWindow(IntPtr h, int x, int y, int w, int hgt, bool repaint);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, int msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern IntPtr SetFocus(IntPtr h);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, IntPtr pid);
    [DllImport("user32.dll")] static extern bool AttachThreadInput(uint from, uint to, bool attach);
    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint vk);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr h, int id);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern short VkKeyScan(char c);
    [DllImport("user32.dll")] static extern uint MapVirtualKey(uint code, uint mapType);
    [DllImport("user32.dll")] static extern IntPtr SetWindowsHookEx(int id, LowLevelMouseProc proc, IntPtr module, uint threadId);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern uint SendInput(uint count, INPUT[] inputs, int size);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(POINT p);
    [DllImport("user32.dll")] static extern bool ScreenToClient(IntPtr h, ref POINT p);
    [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);
    [DllImport("user32.dll")] static extern int GetSystemMetrics(int index);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string name);
    [DllImport("kernel32.dll")] static extern bool GetConsoleSelectionInfo(out CONSOLE_SELECTION_INFO info);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll")] static extern bool AttachConsole(uint pid);
    [DllImport("kernel32.dll")] static extern bool FreeConsole();
    [DllImport("kernel32.dll")] static extern IntPtr GetConsoleWindow();
    [DllImport("kernel32.dll")] static extern bool SetConsoleCtrlHandler(IntPtr handler, bool add);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr CreateFile(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "WriteConsoleInputW", SetLastError = true)]
    static extern bool WriteConsoleInput(IntPtr input, [In] INPUT_RECORD[] records, uint length, out uint written);
}
