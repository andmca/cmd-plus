// The toolbar's look: flat and white so it joins up with the selected tab, a light grey hover, and each
// button's shortcut drawn in grey after its name (button text is "Name  Shortcut", split at the double space).

using System.Drawing;
using System.Windows.Forms;

class ToolbarRenderer : ToolStripProfessionalRenderer
{
    static readonly Color ShortcutInk = Color.FromArgb(118, 118, 118);

    public ToolbarRenderer() : base(new ToolbarColors())
    {
        RoundedEdges = false;
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        int split = e.Text.IndexOf("  ");
        if (split < 0 || e.Item.IsOnDropDown)
        {
            base.OnRenderItemText(e);
            return;
        }

        string name = e.Text.Substring(0, split);
        string shortcut = e.Text.Substring(split + 2);
        var flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;

        // Where the shortcut starts: the width of the name and the two spaces (the "x" stops the spaces being trimmed).
        var noPadding = flags | TextFormatFlags.NoPadding;
        int offset = TextRenderer.MeasureText(e.Graphics, name + "  x", e.TextFont, Size.Empty, noPadding).Width
                   - TextRenderer.MeasureText(e.Graphics, "x", e.TextFont, Size.Empty, noPadding).Width;

        Rectangle r = e.TextRectangle;
        TextRenderer.DrawText(e.Graphics, name, e.TextFont, r, e.TextColor, flags);
        r.X += offset;
        r.Width -= offset;
        TextRenderer.DrawText(e.Graphics, shortcut, e.TextFont, r, ShortcutInk, flags);
    }
}

class ToolbarColors : ProfessionalColorTable
{
    static readonly Color Hover = Color.FromArgb(237, 237, 237);
    static readonly Color Pressed = Color.FromArgb(224, 224, 224);
    static readonly Color Line = Color.FromArgb(229, 229, 229);

    public override Color ToolStripGradientBegin { get { return Color.White; } }
    public override Color ToolStripGradientMiddle { get { return Color.White; } }
    public override Color ToolStripGradientEnd { get { return Color.White; } }
    public override Color ToolStripBorder { get { return Line; } }
    public override Color SeparatorDark { get { return Line; } }
    public override Color SeparatorLight { get { return Color.White; } }

    public override Color ButtonSelectedHighlight { get { return Hover; } }
    public override Color ButtonSelectedBorder { get { return Hover; } }
    public override Color ButtonSelectedGradientBegin { get { return Hover; } }
    public override Color ButtonSelectedGradientMiddle { get { return Hover; } }
    public override Color ButtonSelectedGradientEnd { get { return Hover; } }

    public override Color ButtonPressedHighlight { get { return Pressed; } }
    public override Color ButtonPressedBorder { get { return Pressed; } }
    public override Color ButtonPressedGradientBegin { get { return Pressed; } }
    public override Color ButtonPressedGradientMiddle { get { return Pressed; } }
    public override Color ButtonPressedGradientEnd { get { return Pressed; } }

    // The Copilot button's dropdown arrow, and its menu.
    public override Color MenuItemSelected { get { return Hover; } }
    public override Color MenuItemBorder { get { return Hover; } }
    public override Color MenuItemSelectedGradientBegin { get { return Hover; } }
    public override Color MenuItemSelectedGradientEnd { get { return Hover; } }
    public override Color MenuItemPressedGradientBegin { get { return Pressed; } }
    public override Color MenuItemPressedGradientMiddle { get { return Pressed; } }
    public override Color MenuItemPressedGradientEnd { get { return Pressed; } }
    public override Color MenuBorder { get { return Color.FromArgb(210, 210, 210); } }
    public override Color ToolStripDropDownBackground { get { return Color.White; } }
    public override Color ImageMarginGradientBegin { get { return Color.White; } }
    public override Color ImageMarginGradientMiddle { get { return Color.White; } }
    public override Color ImageMarginGradientEnd { get { return Color.White; } }
}
