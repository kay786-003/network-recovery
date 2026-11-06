using System;
using System.Drawing;
using System.Windows.Forms;

namespace NetworkRecovery
{
    // Shared dark "console" look for every window. Pixel values go through S() so layouts follow the
    // monitor DPI; fonts are point-based and scale on their own.
    public static class Theme
    {
        public const string FontName = "Microsoft JhengHei UI";
        public static readonly Color Background = Color.FromArgb(9, 14, 26);
        public static readonly Color Surface = Color.FromArgb(16, 24, 41);
        public static readonly Color Field = Color.FromArgb(9, 14, 26);
        public static readonly Color Line = Color.FromArgb(34, 52, 82);
        public static readonly Color Ink = Color.FromArgb(226, 235, 248);
        public static readonly Color Muted = Color.FromArgb(127, 146, 176);
        public static readonly Color Accent = Color.FromArgb(34, 211, 238);
        public static readonly Color Warning = Color.FromArgb(251, 191, 36);
        public static readonly Color Danger = Color.FromArgb(255, 128, 128);
        public static readonly Color Selection = Color.FromArgb(12, 62, 80);

        static float systemScale;
        static float scale;
        static float SystemScale
        {
            get
            {
                if (systemScale <= 0) using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) systemScale = g.DpiX / 96F;
                return systemScale;
            }
        }
        // Current UI scale (1.0 = 96 DPI). Tests may set it to rehearse 125%/150%/200% layouts before creating a form.
        public static float Scale
        {
            get { return scale > 0 ? scale : SystemScale; }
            set { scale = value; }
        }
        public static int S(int value) { return (int)Math.Round(value * Scale); }

        // Point sizes follow the system DPI on their own; only a rehearsed scale needs the extra factor.
        public static Font Font(float size, FontStyle style) { return new Font(FontName, size * Scale / SystemScale, style); }
        public static Font Font(float size, bool bold) { return Font(size, bold ? FontStyle.Bold : FontStyle.Regular); }
        // kind: 0 secondary, 1 primary, 2 caution (outlined in amber).
        public static void StyleButton(Button button, string text, int kind)
        {
            bool primary = kind == 1;
            Color border = kind == 2 ? Color.FromArgb(146, 110, 20) : Color.FromArgb(52, 74, 112);
            button.Text = text; button.FlatStyle = FlatStyle.Flat; button.Cursor = Cursors.Hand;
            button.AutoSize = true; button.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            button.MinimumSize = new Size(S(primary ? 150 : 104), S(40));
            button.Padding = new Padding(S(14), S(4), S(14), S(4));
            button.Margin = Padding.Empty;
            button.FlatAppearance.BorderSize = primary ? 0 : 1;
            button.FlatAppearance.BorderColor = border;
            button.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(103, 232, 249) : Color.FromArgb(30, 45, 76);
            button.FlatAppearance.MouseDownBackColor = primary ? Color.FromArgb(6, 182, 212) : Color.FromArgb(38, 56, 92);
            button.Font = Font(9.5F, true);
            button.UseVisualStyleBackColor = false;
            Action paint = delegate {
                button.BackColor = button.Enabled ? (primary ? Accent : Color.FromArgb(20, 30, 50)) : Color.FromArgb(22, 32, 50);
                button.ForeColor = button.Enabled ? (primary ? Color.FromArgb(4, 18, 32) : (kind == 2 ? Warning : Ink)) : Color.FromArgb(88, 104, 130);
            };
            button.EnabledChanged += delegate { paint(); };
            paint();
        }
    }

    // White-on-navy card with a hairline border and an optional accent bar; lays its children out as rows.
    internal sealed class Card : TableLayoutPanel
    {
        readonly Color accent;
        public Card(Padding padding, Color accentColor)
        {
            accent = accentColor; BackColor = Theme.Surface; DoubleBuffered = true;
            Dock = DockStyle.Fill; ColumnCount = 1; Padding = padding; Margin = Padding.Empty;
            if (!accentColor.IsEmpty) Padding = new Padding(padding.Left + 4, padding.Top, padding.Right, padding.Bottom);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (Pen p = new Pen(Theme.Line)) e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
            if (!accent.IsEmpty) using (SolidBrush b = new SolidBrush(accent)) e.Graphics.FillRectangle(b, 0, 0, 4, Height);
        }
    }

    // CheckBox that never wraps its text inside a table cell (a narrow proposed width would clip CJK text).
    internal sealed class PlainCheckBox : CheckBox
    {
        public PlainCheckBox() { AutoSize = true; }
        public override Size GetPreferredSize(Size proposedSize) { return base.GetPreferredSize(Size.Empty); }
    }

    // Label whose height follows its wrapped text at the width the table cell offers, so long messages
    // push content down instead of overlapping it. It reports a tiny width so it never forces a cell wider.
    internal sealed class WrapLabel : Label
    {
        public WrapLabel() { AutoSize = true; }
        public override Size GetPreferredSize(Size proposedSize)
        {
            bool known = proposedSize.Width > 1 && proposedSize.Width < 100000;
            int width = known ? proposedSize.Width : Math.Max(Width, 1);
            Size text = TextRenderer.MeasureText(Text, Font, new Size(width, 100000),
                TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.Left | TextFormatFlags.Top);
            return new Size(known ? width : 1, text.Height + 2);
        }
        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            if (Parent != null) Parent.PerformLayout(this, "Bounds");
        }
    }
}
