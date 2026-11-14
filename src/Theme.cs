using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace NetworkRecovery
{
    // Shared Fluent-style look for every window and the tray menu. The palette follows the Windows
    // "app mode" (light/dark) when a window opens. Pixel values go through S() so layouts follow the
    // monitor DPI; fonts are point-based and scale on their own.
    public static class Theme
    {
        public const string FontName = "Microsoft JhengHei UI";
        public const string GlyphFontName = "Segoe MDL2 Assets";

        public static Color Background, Surface, SurfaceAlt, Field, Line, FieldLine, Ink, Muted, Accent, AccentHover,
            AccentPressed, OnAccent, Success, Warning, Danger, Neutral, Selection, SubtleHover, SubtlePressed;
        static bool dark;
        public static bool Dark { get { return dark; } }

        static Theme() { UseDark(SystemPrefersDark()); }

        // Reads the user's "default app mode" (HKCU, read-only). Missing value or high contrast means light.
        public static bool SystemPrefersDark()
        {
            try
            {
                if (SystemInformation.HighContrast) return false;
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    object value = key == null ? null : key.GetValue("AppsUseLightTheme");
                    return value is int && (int)value == 0;
                }
            }
            catch { return false; }
        }

        public static void UseDark(bool useDark)
        {
            dark = useDark;
            if (dark)
            {
                Background = Rgb(0x202020); Surface = Rgb(0x2B2B2B); SurfaceAlt = Rgb(0x333333); Field = Rgb(0x1E1E1E);
                Line = Rgb(0x3B3B3B); FieldLine = Rgb(0x5A5A5A); Ink = Rgb(0xF3F3F3); Muted = Rgb(0xABABAB);
                Accent = Rgb(0x60CDFF); AccentHover = Rgb(0x5AB9E6); AccentPressed = Rgb(0x4FA6CF); OnAccent = Rgb(0x000000);
                Success = Rgb(0x6CCB5F); Warning = Rgb(0xF2C14E); Danger = Rgb(0xFF99A4); Neutral = Rgb(0x8B8B8B);
                Selection = Rgb(0x23394B); SubtleHover = Rgb(0x383838); SubtlePressed = Rgb(0x323232);
            }
            else
            {
                Background = Rgb(0xF3F3F3); Surface = Rgb(0xFFFFFF); SurfaceAlt = Rgb(0xF5F5F5); Field = Rgb(0xFFFFFF);
                Line = Rgb(0xE3E3E3); FieldLine = Rgb(0xC4C4C4); Ink = Rgb(0x1B1B1B); Muted = Rgb(0x5E5E5E);
                Accent = Rgb(0x005FB8); AccentHover = Rgb(0x1A6EBF); AccentPressed = Rgb(0x3380C8); OnAccent = Rgb(0xFFFFFF);
                Success = Rgb(0x0F7B0F); Warning = Rgb(0x9D5D00); Danger = Rgb(0xC42B1C); Neutral = Rgb(0x8A8A8A);
                Selection = Rgb(0xE5F0FB); SubtleHover = Rgb(0xEDEDED); SubtlePressed = Rgb(0xE5E5E5);
            }
        }

        static Color Rgb(int value) { return Color.FromArgb((value >> 16) & 255, (value >> 8) & 255, value & 255); }
        public static Color Mix(Color a, Color b, float t)
        {
            return Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }

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

        static int glyphState;
        public static bool HasGlyphs
        {
            get
            {
                if (glyphState == 0)
                    using (Font probe = new Font(GlyphFontName, 10F)) glyphState = probe.Name == GlyphFontName ? 1 : 2;
                return glyphState == 1;
            }
        }
        public static Font GlyphFont(float size) { return new Font(GlyphFontName, size * Scale / SystemScale, FontStyle.Regular); }

        // Small icon bitmap for menus, drawn from the Windows icon font.
        public static Bitmap GlyphImage(char glyph, Color color, int size)
        {
            Bitmap bitmap = new Bitmap(size, size);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.Clear(Color.Transparent);
                if (HasGlyphs)
                {
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                    using (Font font = new Font(GlyphFontName, size * 0.75F, FontStyle.Regular, GraphicsUnit.Pixel))
                    using (SolidBrush brush = new SolidBrush(color))
                    using (StringFormat format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                        g.DrawString(glyph.ToString(), font, brush, new RectangleF(0, 0, size, size), format);
                }
            }
            return bitmap;
        }

        public static Bitmap DotImage(Color color, int size)
        {
            Bitmap bitmap = new Bitmap(size, size);
            using (Graphics g = Graphics.FromImage(bitmap))
            using (SolidBrush brush = new SolidBrush(color))
            {
                g.Clear(Color.Transparent); g.SmoothingMode = SmoothingMode.AntiAlias;
                float d = size * 0.5F; g.FillEllipse(brush, (size - d) / 2, (size - d) / 2, d, d);
            }
            return bitmap;
        }

        // kind: 0 secondary, 1 primary (accent), 2 caution, 3 subtle (text-only until hovered).
        public static void StyleButton(Button button, string text, int kind)
        {
            button.Text = text;
            ThemedButton themed = button as ThemedButton;
            if (themed != null) themed.Kind = kind;
            button.Cursor = Cursors.Hand;
            button.AutoSize = true; button.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            button.MinimumSize = new Size(S(kind == 1 ? 140 : kind == 3 ? 0 : 96), S(kind == 3 ? 34 : 38));
            button.Padding = new Padding(S(kind == 3 ? 10 : 16), S(4), S(kind == 3 ? 10 : 16), S(4));
            button.Margin = Padding.Empty;
            button.Font = Font(9.5F, kind == 1);
            button.FlatStyle = FlatStyle.Flat; button.FlatAppearance.BorderSize = 0;
            button.UseVisualStyleBackColor = false;
            button.Invalidate();
        }

        // Dark title bar and scroll bars when the palette is dark; harmless on builds without support.
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr hwnd, string app, string idList);
        public static void ApplyWindow(Form form)
        {
            form.BackColor = Background; form.ForeColor = Ink;
            EventHandler apply = delegate {
                try
                {
                    int on = dark ? 1 : 0;
                    if (DwmSetWindowAttribute(form.Handle, 20, ref on, 4) != 0) DwmSetWindowAttribute(form.Handle, 19, ref on, 4);
                    SetWindowTheme(form.Handle, dark ? "DarkMode_Explorer" : "Explorer", null);
                }
                catch { }
            };
            if (form.IsHandleCreated) apply(form, EventArgs.Empty); else form.HandleCreated += apply;
        }
        public static void ApplyScrollTheme(Control control, string darkClass)
        {
            EventHandler apply = delegate {
                if (dark) try { SetWindowTheme(control.Handle, darkClass, null); } catch { }
            };
            if (control.IsHandleCreated) apply(control, EventArgs.Empty); else control.HandleCreated += apply;
        }

        public static void FillRound(Graphics g, Color color, RectangleF r, float radius)
        {
            using (GraphicsPath path = IconArt.RoundRect(r, radius))
            using (SolidBrush brush = new SolidBrush(color)) g.FillPath(brush, path);
        }
        public static void DrawRound(Graphics g, Color color, RectangleF r, float radius, float width)
        {
            using (GraphicsPath path = IconArt.RoundRect(r, radius))
            using (Pen pen = new Pen(color, width)) g.DrawPath(pen, path);
        }
        // Background of the nearest ancestor that paints one, so rounded corners blend in.
        public static Color Behind(Control control)
        {
            for (Control parent = control.Parent; parent != null; parent = parent.Parent)
                if (parent.BackColor.A == 255) return parent.BackColor;
            return Background;
        }
    }

    // Rounded, owner-painted button with hover/press states and an optional icon glyph.
    internal class ThemedButton : Button
    {
        int kind;
        bool hot, down;
        public char Glyph;
        public int Kind { get { return kind; } set { kind = value; Invalidate(); } }
        public ThemedButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }
        protected int GlyphSpace { get { return Glyph != '\0' && Theme.HasGlyphs ? Theme.S(24) : 0; } }
        public override Size GetPreferredSize(Size proposedSize)
        {
            Size text = TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            Size size = new Size(text.Width + Padding.Horizontal + GlyphSpace + 2, text.Height + Padding.Vertical + Theme.S(10));
            return new Size(Math.Max(size.Width, MinimumSize.Width), Math.Max(size.Height, MinimumSize.Height));
        }
        protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hot = false; down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { down = true; Invalidate(); } base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected void Colors(out Color fill, out Color border, out Color text)
        {
            bool primary = kind == 1;
            if (!Enabled)
            {
                fill = primary ? Theme.Mix(Theme.Surface, Theme.Neutral, 0.35F) : (kind == 3 ? Color.Empty : Theme.Surface);
                border = kind == 3 || primary ? Color.Empty : Theme.Line;
                text = primary ? Theme.Surface : Theme.Mix(Theme.Muted, Theme.Surface, 0.4F);
                return;
            }
            if (primary)
            {
                fill = down ? Theme.AccentPressed : hot ? Theme.AccentHover : Theme.Accent;
                border = Color.Empty; text = Theme.OnAccent; return;
            }
            if (kind == 3)
            {
                fill = down ? Theme.SubtlePressed : hot ? Theme.SubtleHover : Color.Empty;
                border = Color.Empty; text = Theme.Accent; return;
            }
            fill = down ? Theme.SubtlePressed : hot ? Theme.SubtleHover : Theme.SurfaceAlt;
            border = Theme.Line;
            text = kind == 2 ? Theme.Danger : Theme.Ink;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Behind(this));
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color fill, border, text;
            Colors(out fill, out border, out text);
            RectangleF r = new RectangleF(0.5F, 0.5F, Width - 1F, Height - 1F);
            float radius = Theme.S(5);
            if (!fill.IsEmpty) Theme.FillRound(g, fill, r, radius);
            if (!border.IsEmpty) Theme.DrawRound(g, border, r, radius, 1F);
            if (Focused && ShowFocusCues) Theme.DrawRound(g, Theme.Accent, RectangleF.Inflate(r, -1F, -1F), radius - 1, 2F);
            PaintContent(g, text);
        }

        protected virtual void PaintContent(Graphics g, Color text)
        {
            Size measured = TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            int total = measured.Width + GlyphSpace;
            int x = TextAlign == ContentAlignment.MiddleLeft ? Padding.Left : (Width - total) / 2;
            if (GlyphSpace > 0)
                using (Font glyph = Theme.GlyphFont(10.5F))
                    TextRenderer.DrawText(g, Glyph.ToString(), glyph, new Rectangle(x, 0, Theme.S(18), Height), text,
                        TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, Text, Font, new Rectangle(x + GlyphSpace, 0, Width - x - GlyphSpace, Height), text,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }
    }

    // Large action row for the tools window: icon, title and a wrapped one-line explanation.
    internal sealed class ActionTile : ThemedButton
    {
        public string Description = "";
        Font small;
        Font SmallFont { get { if (small == null) small = Theme.Font(8.5F, false); return small; } }
        int TextLeft { get { return Padding.Left + (Glyph != '\0' && Theme.HasGlyphs ? Theme.S(34) : 0); } }
        public override Size GetPreferredSize(Size proposedSize)
        {
            bool known = proposedSize.Width > 1 && proposedSize.Width < 100000;
            int width = known ? proposedSize.Width : Math.Max(Width, Theme.S(240));
            int textWidth = Math.Max(Theme.S(40), width - TextLeft - Padding.Right);
            Size title = TextRenderer.MeasureText(Text, Font, new Size(textWidth, 100000), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            Size note = TextRenderer.MeasureText(Description, SmallFont, new Size(textWidth, 100000), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            int height = Padding.Vertical + title.Height + Theme.S(2) + note.Height + Theme.S(16);
            return new Size(known ? width : 1, Math.Max(height, MinimumSize.Height));
        }
        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            if (Parent != null) Parent.PerformLayout(this, "Bounds");
        }
        protected override void PaintContent(Graphics g, Color text)
        {
            int left = TextLeft, width = Math.Max(1, Width - left - Padding.Right);
            if (Glyph != '\0' && Theme.HasGlyphs)
            {
                int box = Theme.S(28);
                Rectangle icon = new Rectangle(Padding.Left, (Height - box) / 2, box, box);
                Theme.FillRound(g, Enabled ? (Kind == 2 ? Theme.Mix(Theme.Surface, Theme.Danger, 0.14F) : Theme.Mix(Theme.Surface, Theme.Accent, 0.14F)) : Theme.SurfaceAlt, icon, Theme.S(6));
                using (Font glyph = Theme.GlyphFont(11F))
                    TextRenderer.DrawText(g, Glyph.ToString(), glyph, icon, Enabled ? (Kind == 2 ? Theme.Danger : Theme.Accent) : text,
                        TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
            }
            Size title = TextRenderer.MeasureText(Text, Font, new Size(width, 100000), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            Size note = TextRenderer.MeasureText(Description, SmallFont, new Size(width, 100000), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            int top = (Height - title.Height - Theme.S(2) - note.Height) / 2;
            TextRenderer.DrawText(g, Text, Font, new Rectangle(left, top, width, title.Height), text, TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, Description, SmallFont, new Rectangle(left, top + title.Height + Theme.S(2), width, note.Height),
                Enabled ? Theme.Muted : text, TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            using (Font glyph = Theme.HasGlyphs ? Theme.GlyphFont(8F) : null)
                if (glyph != null) TextRenderer.DrawText(g, "", glyph, new Rectangle(Width - Padding.Right - Theme.S(4), 0, Theme.S(12), Height), Theme.Muted,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
        }
        protected override void Dispose(bool disposing) { if (disposing && small != null) small.Dispose(); base.Dispose(disposing); }
    }

    // Rounded card with a hairline border and an optional accent stripe; lays its children out as rows.
    internal sealed class Card : TableLayoutPanel
    {
        readonly Color accent;
        public Card(Padding padding, Color accentColor)
        {
            accent = accentColor; BackColor = Theme.Surface; DoubleBuffered = true;
            Dock = DockStyle.Fill; ColumnCount = 1; Padding = padding; Margin = Padding.Empty; AutoSizeMode = AutoSizeMode.GrowAndShrink;
            ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            if (!accentColor.IsEmpty) Padding = new Padding(padding.Left + Theme.S(4), padding.Top, padding.Right, padding.Bottom);
            SetStyle(ControlStyles.ResizeRedraw, true);
        }
        // TableLayoutPanel measures its preferred height at its own (narrow) preferred width, which makes
        // wrapped text far too tall. Single-column cards stack one control per row, so measure them at the
        // width actually offered instead.
        public override Size GetPreferredSize(Size proposedSize)
        {
            int width = proposedSize.Width;
            if (ColumnCount != 1 || width <= 1 || width >= 100000) return base.GetPreferredSize(new Size(width, 0));
            int inner = Math.Max(1, width - Padding.Horizontal), height = Padding.Vertical;
            foreach (Control control in Controls)
            {
                int h = control.AutoSize ? control.GetPreferredSize(new Size(Math.Max(1, inner - control.Margin.Horizontal), 0)).Height : control.Height;
                height += Math.Max(h, control.MinimumSize.Height) + control.Margin.Vertical;
            }
            return new Size(width, height);
        }
        // An empty trailing row absorbs any surplus height. Without it the last docked label is stretched,
        // and the table then reports that stretched height as its preferred size, so the card never shrinks.
        public void AddSpacerRow()
        {
            RowCount = Math.Max(RowCount, RowStyles.Count) + 1;
            while (RowStyles.Count < RowCount) RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Behind(this));
            g.SmoothingMode = SmoothingMode.AntiAlias;
            RectangleF r = new RectangleF(0.5F, 0.5F, Width - 1F, Height - 1F);
            float radius = Theme.S(8);
            Theme.FillRound(g, Theme.Surface, r, radius);
            Theme.DrawRound(g, Theme.Line, r, radius, 1F);
            if (!accent.IsEmpty) Theme.FillRound(g, accent, new RectangleF(Theme.S(6), Theme.S(12), Theme.S(3), Math.Max(1, Height - Theme.S(24))), Theme.S(2));
        }
        protected override void OnPaint(PaintEventArgs e) { base.OnPaint(e); }
    }

    // Rounded text-box frame: the TextBox itself is borderless and the frame draws focus state.
    internal sealed class InputFrame : Panel
    {
        public readonly TextBox Box;
        public InputFrame(TextBox box)
        {
            Box = box; DoubleBuffered = true; BackColor = Theme.Field;
            SetStyle(ControlStyles.ResizeRedraw, true);
            box.BorderStyle = BorderStyle.None; box.BackColor = Theme.Field; box.ForeColor = Theme.Ink;
            Controls.Add(box);
            box.GotFocus += delegate { Invalidate(); }; box.LostFocus += delegate { Invalidate(); };
            box.EnabledChanged += delegate { Invalidate(); };
            Fit();
        }
        void Fit()
        {
            int pad = Theme.S(10);
            Height = Box.PreferredHeight + Theme.S(16);
            MinimumSize = new Size(0, Height);
            Box.SetBounds(pad, (Height - Box.PreferredHeight) / 2 + 1, Math.Max(1, Width - pad * 2), Box.PreferredHeight);
        }
        protected override void OnResize(EventArgs e) { base.OnResize(e); if (Box != null) Box.SetBounds(Theme.S(10), Box.Top, Math.Max(1, Width - Theme.S(20)), Box.Height); }
        protected override void OnClick(EventArgs e) { Box.Focus(); base.OnClick(e); }
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Behind(this));
            g.SmoothingMode = SmoothingMode.AntiAlias;
            RectangleF r = new RectangleF(0.5F, 0.5F, Width - 1F, Height - 1F);
            Theme.FillRound(g, Theme.Field, r, Theme.S(5));
            Theme.DrawRound(g, Box.Focused ? Theme.Accent : Theme.FieldLine, r, Theme.S(5), Box.Focused ? 1.5F : 1F);
        }
    }

    // Drop-down list painted to match the inputs: rounded border and a chevron instead of the 3D button.
    internal sealed class ThemedComboBox : ComboBox
    {
        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == 0x000F)
                using (Graphics g = Graphics.FromHwnd(Handle)) PaintChrome(g);
        }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        void PaintChrome(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int arrow = Theme.S(30);
            Rectangle button = new Rectangle(Width - arrow - 1, 1, arrow, Height - 2);
            using (SolidBrush b = new SolidBrush(Theme.Field)) g.FillRectangle(b, button);
            using (Pen outer = new Pen(Theme.Behind(this), 3F)) g.DrawRectangle(outer, 0, 0, Width - 1, Height - 1);
            using (Pen inner = new Pen(Theme.Field, 2F)) g.DrawRectangle(inner, 2, 2, Width - 5, Height - 5);
            RectangleF r = new RectangleF(0.5F, 0.5F, Width - 1F, Height - 1F);
            Theme.DrawRound(g, Focused || DroppedDown ? Theme.Accent : Theme.FieldLine, r, Theme.S(5), Focused ? 1.5F : 1F);
            float cx = button.X + button.Width / 2F, cy = Height / 2F, w = Theme.S(4);
            using (Pen pen = new Pen(Enabled ? Theme.Muted : Theme.Line, Math.Max(1.2F, Theme.S(1) * 1.4F)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                g.DrawLines(pen, new[] { new PointF(cx - w, cy - w / 2), new PointF(cx, cy + w / 2), new PointF(cx + w, cy - w / 2) });
        }
    }

    // Owner-painted check box that never wraps its text inside a table cell (a narrow proposed width would clip CJK text).
    internal sealed class PlainCheckBox : CheckBox
    {
        bool hot;
        public PlainCheckBox()
        {
            AutoSize = true;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }
        int Box { get { return Theme.S(18); } }
        int Gap { get { return Theme.S(10); } }
        public override Size GetPreferredSize(Size proposedSize)
        {
            Size text = TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            return new Size(Box + Gap + text.Width + Theme.S(2), Math.Max(Box, text.Height) + Theme.S(6));
        }
        protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hot = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Behind(this));
            g.SmoothingMode = SmoothingMode.AntiAlias;
            bool enabled = Enabled && AutoCheck;
            RectangleF box = new RectangleF(1.5F, (Height - Box) / 2F, Box - 1F, Box - 1F);
            if (Checked)
            {
                Color fill = !Enabled ? Theme.Neutral : !AutoCheck ? Theme.Mix(Theme.Accent, Theme.Surface, 0.35F) : hot ? Theme.AccentHover : Theme.Accent;
                Theme.FillRound(g, fill, box, Theme.S(4));
                float w = box.Width;
                using (Pen pen = new Pen(Theme.OnAccent, Math.Max(1.5F, w * 0.11F)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                    g.DrawLines(pen, new[] { new PointF(box.X + w * 0.24F, box.Y + w * 0.52F), new PointF(box.X + w * 0.43F, box.Y + w * 0.7F), new PointF(box.X + w * 0.77F, box.Y + w * 0.32F) });
            }
            else
            {
                Theme.FillRound(g, hot && enabled ? Theme.SubtleHover : Theme.Field, box, Theme.S(4));
                Theme.DrawRound(g, enabled ? Theme.Muted : Theme.Line, box, Theme.S(4), 1F);
            }
            if (Focused && ShowFocusCues) Theme.DrawRound(g, Theme.Ink, RectangleF.Inflate(box, 2.5F, 2.5F), Theme.S(5), 1F);
            TextRenderer.DrawText(g, Text, Font, new Rectangle(Box + Gap, 0, Width - Box - Gap, Height), Enabled ? ForeColor : Theme.Muted,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
        }
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

    // Small rounded badge (step numbers, "管理員模式").
    internal sealed class Pill : Label
    {
        public Color Fill, Border;
        public bool Circle;
        public Pill() { AutoSize = true; TextAlign = ContentAlignment.MiddleCenter; SetStyle(ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer, true); }
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Behind(this));
            g.SmoothingMode = SmoothingMode.AntiAlias;
            RectangleF r = new RectangleF(0.5F, 0.5F, Width - 1F, Height - 1F);
            float radius = Circle ? Math.Min(Width, Height) / 2F : Height / 2F;
            Theme.FillRound(g, Fill, r, radius);
            if (!Border.IsEmpty) Theme.DrawRound(g, Border, r, radius, 1F);
        }
    }

    // The protection sequence as chips joined by chevrons; wraps onto more lines when narrow.
    internal sealed class StepFlow : Control
    {
        public string[] Steps = new string[0];
        public StepFlow() { AutoSize = true; SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true); }
        int PadX { get { return Theme.S(12); } }
        int ChipHeight { get { return TextRenderer.MeasureText("測", Font).Height + Theme.S(12); } }
        int Arrow { get { return Theme.S(26); } }
        int RowGap { get { return Theme.S(8); } }
        // Lays out chips for a width; returns their rectangles (arrow i sits after chip i on the same row).
        Rectangle[] Place(int width)
        {
            Rectangle[] chips = new Rectangle[Steps.Length];
            int x = 0, y = 0, h = ChipHeight;
            for (int i = 0; i < Steps.Length; i++)
            {
                int w = TextRenderer.MeasureText(Steps[i], Font, Size.Empty, TextFormatFlags.NoPrefix).Width + PadX * 2;
                if (x > 0 && x + w > width) { x = 0; y += h + RowGap; }
                chips[i] = new Rectangle(x, y, Math.Min(w, Math.Max(1, width)), h);
                x += w + Arrow;
            }
            return chips;
        }
        public override Size GetPreferredSize(Size proposedSize)
        {
            bool known = proposedSize.Width > 1 && proposedSize.Width < 100000;
            int width = known ? proposedSize.Width : Math.Max(Width, 1);
            Rectangle[] chips = Place(width);
            int height = chips.Length == 0 ? 0 : chips[chips.Length - 1].Bottom + 1;
            return new Size(known ? width : 1, height);
        }
        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            if (Parent != null) Parent.PerformLayout(this, "Bounds");
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Behind(this));
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle[] chips = Place(Width);
            for (int i = 0; i < chips.Length; i++)
            {
                bool last = i == chips.Length - 1;
                Color fill = last ? Theme.Mix(Theme.Surface, Theme.Warning, 0.14F) : Theme.Mix(Theme.Surface, Theme.Accent, 0.12F);
                Color ink = last ? Theme.Warning : Theme.Accent;
                RectangleF r = new RectangleF(chips[i].X + 0.5F, chips[i].Y + 0.5F, chips[i].Width - 1F, chips[i].Height - 1F);
                Theme.FillRound(g, fill, r, r.Height / 2);
                TextRenderer.DrawText(g, Steps[i], Font, chips[i], ink, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
                if (!last && chips[i + 1].Y == chips[i].Y)
                {
                    float cx = chips[i].Right + Arrow / 2F, cy = chips[i].Y + chips[i].Height / 2F, w = Theme.S(3);
                    using (Pen pen = new Pen(Theme.Muted, Math.Max(1.2F, Theme.S(1) * 1.4F)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                        g.DrawLines(pen, new[] { new PointF(cx - w / 2, cy - w), new PointF(cx + w / 2, cy), new PointF(cx - w / 2, cy + w) });
                }
            }
        }
    }

    // Tray menu renderer: palette colors, rounded highlight, accent check marks.
    internal sealed class MenuRenderer : ToolStripProfessionalRenderer
    {
        public MenuRenderer() : base(new MenuColors()) { RoundedEdges = false; }
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        { using (SolidBrush b = new SolidBrush(Theme.Surface)) e.Graphics.FillRectangle(b, e.AffectedBounds); }
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        { using (Pen p = new Pen(Theme.Line)) e.Graphics.DrawRectangle(p, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1); }
        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { }
        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected || !e.Item.Enabled) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Theme.FillRound(e.Graphics, Theme.SubtleHover, new RectangleF(Theme.S(4), 1, e.Item.Width - Theme.S(8), e.Item.Height - 2), Theme.S(4));
        }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            // The status header is a disabled item; draw it in full ink rather than the system gray.
            if ("header".Equals(e.Item.Tag))
            {
                TextRenderer.DrawText(e.Graphics, e.Text, e.TextFont, e.TextRectangle, Theme.Ink, e.TextFormat);
                return;
            }
            e.TextColor = e.Item.Enabled ? Theme.Ink : Theme.Muted;
            base.OnRenderItemText(e);
        }
        protected override void OnRenderItemImage(ToolStripItemImageRenderEventArgs e)
        {
            if ("header".Equals(e.Item.Tag) && e.Image != null) { e.Graphics.DrawImage(e.Image, e.ImageRectangle); return; }
            base.OnRenderItemImage(e);
        }
        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            Rectangle r = e.ImageRectangle;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            float w = Math.Min(r.Width, r.Height);
            float x = r.X + (r.Width - w) / 2, y = r.Y + (r.Height - w) / 2;
            using (Pen pen = new Pen(Theme.Accent, Math.Max(1.6F, w * 0.13F)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                e.Graphics.DrawLines(pen, new[] { new PointF(x + w * 0.18F, y + w * 0.52F), new PointF(x + w * 0.42F, y + w * 0.74F), new PointF(x + w * 0.84F, y + w * 0.28F) });
        }
        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            int y = e.Item.Height / 2;
            using (Pen p = new Pen(Theme.Line)) e.Graphics.DrawLine(p, Theme.S(8), y, e.Item.Width - Theme.S(8), y);
        }
        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e) { e.ArrowColor = Theme.Muted; base.OnRenderArrow(e); }

        sealed class MenuColors : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground { get { return Theme.Surface; } }
            public override Color MenuBorder { get { return Theme.Line; } }
            public override Color MenuItemBorder { get { return Color.Transparent; } }
            public override Color MenuItemSelected { get { return Theme.SubtleHover; } }
            public override Color ImageMarginGradientBegin { get { return Theme.Surface; } }
            public override Color ImageMarginGradientMiddle { get { return Theme.Surface; } }
            public override Color ImageMarginGradientEnd { get { return Theme.Surface; } }
            public override Color SeparatorDark { get { return Theme.Line; } }
            public override Color SeparatorLight { get { return Theme.Line; } }
        }
    }
}
