// The row of tabs above the consoles, drawn in the style of Windows 11 Notepad: each tab has an X to close it,
// and + after the last tab opens a new cmd tab. It drives a PageHost, which holds the consoles.

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

// A TabControl with its own headers and border hidden, so TabStrip can draw the tabs instead.
class PageHost : TabControl
{
    const int TCM_ADJUSTRECT = 0x1328;

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == TCM_ADJUSTRECT) { m.Result = (IntPtr)1; return; }   // pages fill the whole control
        base.WndProc(ref m);
    }
}

class TabStrip : Control
{
    static readonly Color StripBack = Color.FromArgb(243, 243, 243);
    static readonly Color TabHover = Color.FromArgb(233, 233, 233);
    static readonly Color ButtonHover = Color.FromArgb(218, 218, 218);
    static readonly Color Ink = Color.FromArgb(26, 26, 26);
    // The selected tab is console black, so it runs straight into the console below it.
    static readonly Color SelectedBack = Color.Black;
    static readonly Color SelectedInk = Color.FromArgb(242, 242, 242);
    static readonly Color SelectedButtonHover = Color.FromArgb(64, 64, 64);
    static readonly Color Divider = Color.FromArgb(208, 208, 208);

    readonly TabControl tabs;
    readonly ToolTip tip = new ToolTip();
    int hoverTab = -1;
    bool hoverClose, hoverPlus;

    public event Action<TabPage> CloseRequested;
    public event Action NewTabRequested;

    public TabStrip(TabControl tabs)
    {
        this.tabs = tabs;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Selectable, false);   // clicking a tab shouldn't take focus from the console
        Dock = DockStyle.Top;
        Height = 36;   // at 96 DPI; the form scales it
        Font = new Font("Segoe UI", 9F);
        tabs.ControlAdded += delegate { Invalidate(); };
        tabs.ControlRemoved += delegate { Invalidate(); };
        tabs.SelectedIndexChanged += delegate { Invalidate(); };
    }

    // Converts a size in 96-DPI pixels to this control's scale.
    int Px(float n)
    {
        return (int)(n * Height / 36F + 0.5F);
    }

    // ---------- Layout ----------

    // Every tab is the same width, shrinking as more are opened, like Notepad.
    Rectangle[] TabRects()
    {
        int n = tabs.TabCount;
        int left = Px(8), top = Px(6);
        int width = n == 0 ? 0 : Math.Max(Px(72), Math.Min(Px(220), (Width - left - Px(44)) / n));
        var rects = new Rectangle[n];
        for (int i = 0; i < n; i++) rects[i] = new Rectangle(left + i * width, top, width, Height - top);
        return rects;
    }

    Rectangle CloseRect(Rectangle tab)
    {
        int size = Px(22);
        return new Rectangle(tab.Right - size - Px(6), tab.Top + (tab.Height - size) / 2, size, size);
    }

    Rectangle PlusRect(Rectangle[] rects)
    {
        int size = Px(28), top = Px(6);
        int x = rects.Length == 0 ? Px(8) : rects[rects.Length - 1].Right + Px(4);
        return new Rectangle(x, top + (Height - top - size) / 2, size, size);
    }

    // Which tab is at a point, and whether it's on that tab's X or on the + button.
    void HitTest(Point p, out int tab, out bool onClose, out bool onPlus)
    {
        var rects = TabRects();
        tab = -1;
        onClose = false;
        onPlus = PlusRect(rects).Contains(p);
        for (int i = 0; i < rects.Length; i++)
        {
            if (!rects[i].Contains(p)) continue;
            tab = i;
            onClose = CloseRect(rects[i]).Contains(p);
        }
    }

    // ---------- Mouse ----------

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int tab;
        bool onClose, onPlus;
        HitTest(e.Location, out tab, out onClose, out onPlus);
        if (tab == hoverTab && onClose == hoverClose && onPlus == hoverPlus) return;

        hoverTab = tab;
        hoverClose = onClose;
        hoverPlus = onPlus;
        tip.SetToolTip(this, onPlus ? "New cmd tab  (Ctrl+T)"
                           : onClose ? "Close tab  (Ctrl+W)"
                           : tab >= 0 ? tabs.TabPages[tab].Text
                           : null);
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        hoverTab = -1;
        hoverClose = hoverPlus = false;
        tip.SetToolTip(this, null);
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        int tab;
        bool onClose, onPlus;
        HitTest(e.Location, out tab, out onClose, out onPlus);
        if (e.Button == MouseButtons.Left && tab >= 0 && !onClose) tabs.SelectedIndex = tab;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        int tab;
        bool onClose, onPlus;
        HitTest(e.Location, out tab, out onClose, out onPlus);

        if (e.Button == MouseButtons.Left && onPlus && NewTabRequested != null) NewTabRequested();
        else if (tab >= 0 && CloseRequested != null &&
                 ((e.Button == MouseButtons.Left && onClose) || e.Button == MouseButtons.Middle))
            CloseRequested(tabs.TabPages[tab]);
    }

    // ---------- Drawing ----------

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(StripBack);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var rects = TabRects();
        int selected = tabs.SelectedIndex;
        for (int i = 0; i < rects.Length; i++)
        {
            Rectangle r = rects[i];
            if (i == selected || i == hoverTab)
                using (var path = Rounded(r, Px(7), true))
                using (var brush = new SolidBrush(i == selected ? SelectedBack : TabHover))
                    g.FillPath(brush, path);

            // A thin divider between two tabs that are both plain.
            bool plain = i != selected && i != hoverTab;
            bool nextPlain = i + 1 < rects.Length && i + 1 != selected && i + 1 != hoverTab;
            if (plain && nextPlain)
                using (var pen = new Pen(Divider))
                    g.DrawLine(pen, r.Right, r.Top + r.Height / 4, r.Right, r.Bottom - r.Height / 4);

            Color ink = i == selected ? SelectedInk : Ink;
            Rectangle close = CloseRect(r);
            var textRect = new Rectangle(r.Left + Px(12), r.Top, close.Left - r.Left - Px(14), r.Height);
            TextRenderer.DrawText(g, tabs.TabPages[i].Text, Font, textRect, ink,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

            if (i == hoverTab && hoverClose) FillButton(g, close, i == selected ? SelectedButtonHover : ButtonHover);
            float arm = Px(4.5F);
            PointF c = Centre(close);
            using (var pen = new Pen(ink, Px(1.2F)))
            {
                g.DrawLine(pen, c.X - arm, c.Y - arm, c.X + arm, c.Y + arm);
                g.DrawLine(pen, c.X - arm, c.Y + arm, c.X + arm, c.Y - arm);
            }
        }

        Rectangle plus = PlusRect(rects);
        if (hoverPlus) FillButton(g, plus, ButtonHover);
        float half = Px(6);
        PointF p = Centre(plus);
        using (var pen = new Pen(Ink, Px(1.2F)))
        {
            g.DrawLine(pen, p.X - half, p.Y, p.X + half, p.Y);
            g.DrawLine(pen, p.X, p.Y - half, p.X, p.Y + half);
        }
    }

    void FillButton(Graphics g, Rectangle r, Color colour)
    {
        using (var path = Rounded(r, Px(4), false))
        using (var brush = new SolidBrush(colour))
            g.FillPath(brush, path);
    }

    static PointF Centre(Rectangle r)
    {
        return new PointF(r.Left + r.Width / 2F, r.Top + r.Height / 2F);
    }

    // A rectangle with rounded corners: all four, or just the top two for a tab that runs into the console.
    static GraphicsPath Rounded(Rectangle r, int radius, bool topOnly)
    {
        int d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.Left, r.Top, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        if (topOnly)
        {
            path.AddLine(r.Right, r.Bottom, r.Left, r.Bottom);
        }
        else
        {
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        }
        path.CloseFigure();
        return path;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) tip.Dispose();
        base.Dispose(disposing);
    }
}
