// The main window: the toolbar, and starting, sizing, focusing and closing tabs.

using System;
using System.Diagnostics;
using System.Drawing;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;

partial class MainForm : Form
{
    static readonly string UserProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    readonly TabControl tabs = new TabControl();
    readonly string baseTitle = IsElevated() ? "CMD+ (Administrator)" : "CMD+";
    readonly System.Windows.Forms.Timer titleTimer = new System.Windows.Forms.Timer { Interval = 1500 };
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

        var strip = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden };
        strip.Items.Add(new ToolStripButton("+ cmd  (Ctrl+T)", null, delegate { NewCmdTab(); }));

        copilotButton.Text = "+ Copilot  (Ctrl+Shift+T)";
        copilotButton.ButtonClick += delegate { NewCopilotTab(); };
        copilotButton.DropDownItems.Add("Start Copilot in another folder...", null, delegate { ChooseFolderAndStartCopilot(); });
        UpdateCopilotTooltip();
        strip.Items.Add(copilotButton);

        strip.Items.Add(new ToolStripSeparator());
        strip.Items.Add(new ToolStripButton("Copy  (Ctrl+Shift+C)", null, delegate { CopySelection(); })
        {
            ToolTipText = "Drag over text to select it, then copy. Esc clears the selection."
        });
        strip.Items.Add(new ToolStripButton("Paste  (Ctrl+Shift+V)", null, delegate { Paste(); }));
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

    // Tabs opened from an elevated window are elevated too, so the title says so.
    static bool IsElevated()
    {
        using (var identity = WindowsIdentity.GetCurrent())
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    void FlashTitle(string message)
    {
        Text = baseTitle + "  -  " + message;
        titleTimer.Stop();
        titleTimer.Start();
    }

    // ---------- Starting tabs ----------

    void NewCmdTab()
    {
        NewTab("cmd " + (++cmdCount), "", UserProfile, false);
    }

    void NewTab(string title, string arguments, string folder, bool isCopilot)
    {
        var p = Process.Start(new ProcessStartInfo("cmd.exe", arguments)
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

        const int frame = WS_CAPTION | WS_THICKFRAME | WS_SYSMENU | WS_MINIMIZEBOX | WS_MAXIMIZEBOX;
        SetWindowLong(h, GWL_STYLE, GetWindowLong(h, GWL_STYLE) & ~frame);
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
            SetParent(s.Hwnd, IntPtr.Zero);   // detach first so its teardown never waits on this window
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

        string message = tabs.TabCount == 1 ? "Close Tabs and its open tab?" : "Close Tabs and all " + tabs.TabCount + " open tabs?";
        if (copilotTabs == 1) message += "\n\nThis will also end the Copilot session.";
        if (copilotTabs > 1) message += "\n\nThis will also end all " + copilotTabs + " Copilot sessions.";
        return Confirm(message, "Close Tabs", copilotTabs > 0);
    }

    // A yes/no question with No as the default. The warning icon is for when Copilot sessions would end.
    bool Confirm(string message, string title, bool warn)
    {
        var icon = warn ? MessageBoxIcon.Warning : MessageBoxIcon.Question;
        return MessageBox.Show(this, message, title, MessageBoxButtons.YesNo, icon, MessageBoxDefaultButton.Button2)
            == DialogResult.Yes;
    }
}
