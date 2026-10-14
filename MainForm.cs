// The main window: toolbar, starting and closing tabs, keeping the console sized and focused.

using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

partial class MainForm : Form
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

    void FlashTitle(string message)
    {
        Text = baseTitle + "  -  " + message;
        titleTimer.Stop();
        titleTimer.Start();
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
}
