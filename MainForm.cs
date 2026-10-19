// The main window: the tab strip and toolbar, and starting, sizing, focusing and closing tabs.

using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;

partial class MainForm : Form
{
    static readonly string UserProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    // The full path, so Windows can't pick up a different cmd.exe from another folder.
    static readonly string CmdExe = Path.Combine(Environment.SystemDirectory, "cmd.exe");

    readonly TabControl tabs = new PageHost();
    readonly string baseTitle = IsElevated() ? "CMD+ (Administrator)" : "CMD+";
    readonly System.Windows.Forms.Timer titleTimer = new System.Windows.Forms.Timer();
    int cmdCount;
    bool closing;

    public MainForm()
    {
        Text = baseTitle;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);
        Size = new Size(1100, 650);
        StartPosition = FormStartPosition.CenterScreen;

        var icon = typeof(MainForm).Assembly.GetManifestResourceStream("CMDPlus.ico");   // embedded by build.cmd
        if (icon != null) Icon = new Icon(icon);

        titleTimer.Tick += delegate { titleTimer.Stop(); Text = baseTitle; };

        var tabStrip = new TabStrip(tabs);
        tabStrip.NewTabRequested += NewCmdTab;
        tabStrip.CloseRequested += CloseTab;

        // The menu under the arrow beside +. The shortcuts are only shown here; the hotkeys handle the keys.
        var newTabMenu = new ContextMenuStrip { Renderer = new ToolbarRenderer() };
        newTabMenu.Items.Add(new ToolStripMenuItem("cmd", null, delegate { NewCmdTab(); }) { ShortcutKeyDisplayString = "Ctrl+T" });
        newTabMenu.Items.Add(new ToolStripMenuItem("Copilot", null, delegate { NewCopilotTab(); }) { ShortcutKeyDisplayString = "Ctrl+Shift+T" });
        // Give the console its focus back if the menu is dismissed without choosing anything.
        newTabMenu.Closed += delegate { BeginInvoke((MethodInvoker)delegate { FitAndFocus(tabs.SelectedTab); }); };
        tabStrip.NewTabMenu = newTabMenu;

        // Button text is "Name  Shortcut"; the renderer draws the shortcut in grey.
        var toolbar = new ToolStrip
        {
            GripStyle = ToolStripGripStyle.Hidden,
            Renderer = new ToolbarRenderer(),
            Font = new Font("Consolas", 9F),
            Padding = new Padding(6, 3, 6, 3)
        };
        copilotButton.Text = "+ Copilot  Ctrl+Shift+T";
        copilotButton.ButtonClick += delegate { NewCopilotTab(); };
        copilotButton.DropDownItems.Add("Start Copilot in another folder...", null, delegate { ChooseFolderAndStartCopilot(); });
        UpdateCopilotTooltip();
        toolbar.Items.Add(copilotButton);

        toolbar.Items.Add(new ToolStripSeparator());
        toolbar.Items.Add(new ToolStripButton("Copy  Ctrl+Shift+C", null, delegate { CopySelection(); })
        {
            ToolTipText = "Copies the selected text. With nothing selected, starts selecting (works in Copilot too)."
        });
        toolbar.Items.Add(new ToolStripButton("Paste  Ctrl+Shift+V", null, delegate { Paste(); }));

        foreach (ToolStripItem item in toolbar.Items)
        {
            item.Padding = new Padding(8, 4, 8, 4);
            item.Margin = new Padding(item is ToolStripSeparator ? 6 : 2, 0, item is ToolStripSeparator ? 6 : 2, 0);
        }

        tabs.Dock = DockStyle.Fill;
        tabs.SelectedIndexChanged += delegate { BeginInvoke((MethodInvoker)delegate { FitAndFocus(tabs.SelectedTab); }); };

        // Docked controls stack from the last one added, so this puts the toolbar on top, then the tab strip.
        Controls.Add(tabs);
        Controls.Add(tabStrip);
        Controls.Add(toolbar);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        NewCmdTab();
    }

    // Tabs opened from an elevated window are elevated too, so the title says so.
    static bool IsElevated()
    {
        using (var identity = WindowsIdentity.GetCurrent())
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    // Shows a short message in the title bar for a moment (1.5 seconds unless said otherwise).
    void FlashTitle(string message, int milliseconds = 1500)
    {
        Text = baseTitle + "  -  " + message;
        titleTimer.Stop();
        titleTimer.Interval = milliseconds;
        titleTimer.Start();
    }

    // ---------- Starting tabs ----------

    void NewCmdTab()
    {
        NewTab("cmd " + (++cmdCount), "", UserProfile, false);
    }

    void NewTab(string title, string arguments, string folder, bool isCopilot)
    {
        var p = Process.Start(new ProcessStartInfo(CmdExe, arguments)
        {
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden,   // avoids a flash before it's moved into the tab
            WorkingDirectory = folder
        });
        IntPtr h = FindConsoleWindow(p);
        if (h == IntPtr.Zero)
        {
            try { p.Kill(); } catch { }
            MessageBox.Show(this, "Couldn't attach to the cmd.exe window.", "CMD+");
            return;
        }

        var page = new TabPage(title)
        {
            BackColor = Color.Black,
            Tag = new Session { Proc = p, Hwnd = h, IsCopilot = isCopilot }
        };
        tabs.TabPages.Add(page);

        // Make it a true child window (WS_CHILD), not just a top-level window placed inside the tab. Without this,
        // giving the console focus also made it the active window, so Windows didn't treat CMD+ itself as active,
        // and clicking CMD+ on the taskbar didn't bring it to the front.
        const int frame = WS_CAPTION | WS_THICKFRAME | WS_SYSMENU | WS_MINIMIZEBOX | WS_MAXIMIZEBOX | WS_POPUP;
        SetWindowLong(h, GWL_STYLE, (GetWindowLong(h, GWL_STYLE) & ~frame) | WS_CHILD);
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

    // Waits up to 5 seconds for the new cmd process's console window.
    static IntPtr FindConsoleWindow(Process p)
    {
        for (int i = 0; i < 100 && !p.HasExited; i++)
        {
            IntPtr h = InConsole(p, GetConsoleWindow);
            if (h != IntPtr.Zero) return h;
            Thread.Sleep(50);
        }
        return IntPtr.Zero;
    }

    // Briefly attaches CMD+ to another process's console, so console functions act on that console.
    // Ctrl+C is ignored meanwhile so it can't end CMD+. Returns the default value if it can't attach.
    static T InConsole<T>(Process p, Func<T> action)
    {
        if (!AttachConsole((uint)p.Id)) return default(T);
        SetConsoleCtrlHandler(IntPtr.Zero, true);
        try
        {
            return action();
        }
        finally
        {
            FreeConsole();
            SetConsoleCtrlHandler(IntPtr.Zero, false);
        }
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

        // The console belongs to another process's thread, so borrow its input queue to give it focus.
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

        tabs.SelectedTab = page;   // show which tab is being closed
        string message = "Close \"" + page.Text + "\"?";
        if (s.IsCopilot) message += "\n\nThis will also end the Copilot session running in it.";
        if (!Confirm(message, "Close tab", s.IsCopilot))
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
        if (tabs.TabCount == 0) Close();
        else FitAndFocus(tabs.SelectedTab);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Closing the window ends every tab, so confirm first (but not when Windows is shutting down).
        if (tabs.TabCount > 0 && e.CloseReason == CloseReason.UserClosing && !ConfirmCloseAll())
        {
            e.Cancel = true;
            FitAndFocus(tabs.SelectedTab);
            return;
        }

        closing = true;
        foreach (TabPage page in tabs.TabPages)
        {
            var s = SessionOf(page);
            if (s == null) continue;
            ShowWindow(s.Hwnd, SW_HIDE);
            // Detach first so its teardown never waits on this window. A window without a parent can't be a child.
            SetWindowLong(s.Hwnd, GWL_STYLE, GetWindowLong(s.Hwnd, GWL_STYLE) & ~WS_CHILD);
            SetParent(s.Hwnd, IntPtr.Zero);
            PostMessage(s.Hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        }
        base.OnFormClosing(e);
    }

    bool ConfirmCloseAll()
    {
        int copilotTabs = 0;
        foreach (TabPage page in tabs.TabPages)
        {
            var s = SessionOf(page);
            if (s != null && s.IsCopilot) copilotTabs++;
        }

        string message = tabs.TabCount == 1 ? "Close CMD+ and its open tab?" : "Close CMD+ and all " + tabs.TabCount + " open tabs?";
        if (copilotTabs == 1) message += "\n\nThis will also end the Copilot session.";
        if (copilotTabs > 1) message += "\n\nThis will also end all " + copilotTabs + " Copilot sessions.";
        return Confirm(message, "Close CMD+", copilotTabs > 0);
    }

    // A yes/no question with No as the default. The warning icon is for when Copilot sessions would end.
    bool Confirm(string message, string title, bool warn)
    {
        var icon = warn ? MessageBoxIcon.Warning : MessageBoxIcon.Question;
        return MessageBox.Show(this, message, title, MessageBoxButtons.YesNo, icon, MessageBoxDefaultButton.Button2)
            == DialogResult.Yes;
    }
}
