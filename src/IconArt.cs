using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace NetworkRecovery
{
    // Vector artwork for the application, enabled and disabled icons. build-icons.ps1 renders every ICO
    // frame from this file, and the windows draw the same mark at any DPI, so there is one source of truth.
    public static class IconArt
    {
        public const int App = 0, Enabled = 1, Disabled = 2;

        static Color[] Tile(int kind)
        {
            if (kind == Enabled) return new[] { Color.FromArgb(46, 196, 106), Color.FromArgb(16, 132, 66) };
            if (kind == Disabled) return new[] { Color.FromArgb(148, 157, 172), Color.FromArgb(94, 103, 119) };
            return new[] { Color.FromArgb(56, 148, 255), Color.FromArgb(10, 92, 214) };
        }

        public static Bitmap Render(int kind, int size)
        {
            Bitmap bitmap = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.Clear(Color.Transparent);
                Draw(g, kind, new RectangleF(0, 0, size, size));
            }
            return bitmap;
        }

        public static void Draw(Graphics g, int kind, RectangleF bounds)
        {
            SmoothingMode oldSmoothing = g.SmoothingMode; PixelOffsetMode oldOffset = g.PixelOffsetMode;
            g.SmoothingMode = SmoothingMode.AntiAlias; g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            float size = Math.Min(bounds.Width, bounds.Height);
            bool small = size <= 24.5F;
            // Keep a transparent margin of at least one pixel so the shell and tray never show a hard edge.
            float inset = Math.Max(1F, size * 0.06F);
            RectangleF tile = new RectangleF(bounds.X + inset, bounds.Y + inset, size - inset * 2, size - inset * 2);
            Color[] colors = Tile(kind);
            using (GraphicsPath path = RoundRect(tile, tile.Width * 0.24F))
            using (LinearGradientBrush fill = new LinearGradientBrush(tile, colors[0], colors[1], 90F))
            {
                g.FillPath(fill, path);
                if (!small)
                    using (Pen rim = new Pen(Color.FromArgb(40, 255, 255, 255), Math.Max(1F, size / 96F)))
                    {
                        RectangleF inner = RectangleF.Inflate(tile, -rim.Width / 2, -rim.Width / 2);
                        using (GraphicsPath edge = RoundRect(inner, inner.Width * 0.24F)) g.DrawPath(rim, edge);
                    }
            }
            float unit = tile.Width;
            if (small && kind == Disabled) DrawPause(g, Color.White, tile.X + unit * 0.5F, tile.Y + unit * 0.5F, unit * 0.52F);
            else DrawWifi(g, Color.White, tile, small ? 0.5F : 0.43F, small ? 0.74F : 0.66F, small ? 1.08F : 1F);
            if (!small)
            {
                // Status badge: refresh arrow for the app, check when monitoring, pause when stopped.
                float d = unit * 0.46F, edge = d * 0.09F + Math.Max(0.5F, size * 0.01F);
                RectangleF badge = new RectangleF(bounds.X + size - edge - d, bounds.Y + size - edge - d, d, d);
                using (SolidBrush ring = new SolidBrush(colors[1])) g.FillEllipse(ring, RectangleF.Inflate(badge, d * 0.09F, d * 0.09F));
                g.FillEllipse(Brushes.White, badge);
                float cx = badge.X + d / 2, cy = badge.Y + d / 2;
                if (kind == Enabled) DrawCheck(g, colors[1], cx, cy, d * 0.56F);
                else if (kind == Disabled) DrawPause(g, colors[1], cx, cy, d * 0.5F);
                else DrawRefresh(g, colors[1], cx, cy, d * 0.56F);
            }
            g.SmoothingMode = oldSmoothing; g.PixelOffsetMode = oldOffset;
        }

        // Dot and two arcs fanning upward from (cx, cy) given as fractions of the tile.
        static void DrawWifi(Graphics g, Color color, RectangleF tile, float fx, float fy, float zoom)
        {
            float u = tile.Width * zoom;
            float cx = tile.X + tile.Width * fx, cy = tile.Y + tile.Height * fy;
            float stroke = u * 0.105F;
            using (Pen pen = new Pen(color, stroke) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                foreach (float r in new[] { u * 0.25F, u * 0.43F })
                    g.DrawArc(pen, cx - r, cy - r, r * 2, r * 2, 225F, 90F);
            float dot = u * 0.085F;
            using (SolidBrush b = new SolidBrush(color)) g.FillEllipse(b, cx - dot, cy - dot, dot * 2, dot * 2);
        }

        static void DrawCheck(Graphics g, Color color, float cx, float cy, float w)
        {
            using (Pen pen = new Pen(color, w * 0.22F) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                g.DrawLines(pen, new[] { new PointF(cx - w * 0.42F, cy + w * 0.02F), new PointF(cx - w * 0.1F, cy + w * 0.32F), new PointF(cx + w * 0.44F, cy - w * 0.3F) });
        }

        static void DrawPause(Graphics g, Color color, float cx, float cy, float h)
        {
            float bar = h * 0.28F, gap = h * 0.2F;
            using (Pen pen = new Pen(color, bar) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                float top = cy - h / 2 + bar / 2, bottom = cy + h / 2 - bar / 2;
                g.DrawLine(pen, cx - gap / 2 - bar / 2, top, cx - gap / 2 - bar / 2, bottom);
                g.DrawLine(pen, cx + gap / 2 + bar / 2, top, cx + gap / 2 + bar / 2, bottom);
            }
        }

        static void DrawRefresh(Graphics g, Color color, float cx, float cy, float w)
        {
            float r = w * 0.4F, stroke = w * 0.2F;
            using (Pen pen = new Pen(color, stroke) { StartCap = LineCap.Round })
                g.DrawArc(pen, cx - r, cy - r, r * 2, r * 2, -40F, 280F);
            // Arrow head at the end of the arc (angle 240°), pointing clockwise.
            double a = 240 * Math.PI / 180;
            float ex = cx + (float)(r * Math.Cos(a)), ey = cy + (float)(r * Math.Sin(a));
            float h = w * 0.4F;
            PointF tangent = new PointF((float)-Math.Sin(a), (float)Math.Cos(a));
            PointF normal = new PointF((float)Math.Cos(a), (float)Math.Sin(a));
            using (SolidBrush b = new SolidBrush(color))
                g.FillPolygon(b, new[] {
                    new PointF(ex + tangent.X * h * 0.9F, ey + tangent.Y * h * 0.9F),
                    new PointF(ex - tangent.X * h * 0.15F + normal.X * h * 0.75F, ey - tangent.Y * h * 0.15F + normal.Y * h * 0.75F),
                    new PointF(ex - tangent.X * h * 0.15F - normal.X * h * 0.75F, ey - tangent.Y * h * 0.15F - normal.Y * h * 0.75F) });
        }

        public static GraphicsPath RoundRect(RectangleF r, float radius)
        {
            GraphicsPath path = new GraphicsPath();
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            if (d <= 0.5F) { path.AddRectangle(r); return path; }
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
