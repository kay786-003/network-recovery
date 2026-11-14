using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace NetworkRecovery
{
    public sealed class MainForm : Form
    {
        readonly Color ink = Theme.Ink;
        readonly Color muted = Theme.Muted;
        readonly Color accent = Theme.Accent;
        readonly bool preview;
        readonly float scale;
        readonly Panel content = new Panel();
        TableLayoutPanel root;
        TableLayoutPanel body;
        TableLayoutPanel footerPanel;
        Card flowCard;
        FlowLayoutPanel linkPanel;
        Card adapterCard;
        Card wifiCard;
        Control statusDot;
        Color statusTone;
        bool wideLayout;
        bool layoutReady;
        readonly ListView adapters = new ListView();
        readonly ComboBox networks = new ThemedComboBox();
        readonly CheckBox reconnect = new PlainCheckBox();
        readonly CheckBox autoConnect = new PlainCheckBox();
        readonly CheckBox showPassword = new PlainCheckBox();
        readonly CheckBox replacePassword = new PlainCheckBox();
        readonly CheckBox countryGuard = new PlainCheckBox();
        readonly Label guardTargets = new WrapLabel();
        readonly Button pickProcesses = new ThemedButton { Glyph = '' };
        string[] guardNames = (string[])CountryProcessGuard.LegacyNames.Clone();
        readonly TextBox password = new TextBox();
        InputFrame passwordFrame;
        readonly Label passwordLabel = new Label();
        readonly Label wifiInfo = new WrapLabel();
        readonly Label adapterInfo = new WrapLabel();
        readonly Label status = new WrapLabel();
        readonly Label notice = new WrapLabel();
        readonly Button start = new ThemedButton { Glyph = '' };
        readonly Button stop = new ThemedButton { Glyph = '' };
        readonly Button refreshAdapters = new ThemedButton { Glyph = '' };
        readonly Button refreshWifi = new ThemedButton { Glyph = '' };
        readonly System.Windows.Forms.Timer statusTimer = new System.Windows.Forms.Timer();
        AppConfig saved;
        bool busy;
        bool loading;
        bool populating;
        int selectionVersion;
        int networkRequest;
        MonitorToolsForm toolsWindow;

        public MainForm(bool previewMode)
        {
            SuspendLayout();
            preview = previewMode;
            scale = Theme.Scale;
            Text = "Network Recovery · 連線守護";
            Icon = AppIcons.Application;
            Font = Theme.Font(9F, FontStyle.Regular);
            Theme.ApplyWindow(this);
            // Every pixel value goes through S(); fonts are point-based, so text and layout scale together.
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(S(1000), S(720));
            MinimumSize = new Size(S(360), S(300));
            StartPosition = FormStartPosition.CenterScreen;
            AutoScroll = true;
            content.Location = Point.Empty;
            content.Size = ClientSize;
            Controls.Add(content);
            content.SuspendLayout();
            BuildLayout();
            content.ResumeLayout(true);
            ResumeLayout(false);
            adapters.SelectedIndexChanged += delegate { if (!populating) AdapterChanged(); };
            adapters.SizeChanged += delegate { FitAdapterColumns(); };
            networks.SelectedIndexChanged += delegate { if (!loading) NetworkChanged(); };
            reconnect.CheckedChanged += delegate { UpdateWifiControls(); };
            showPassword.CheckedChanged += delegate { password.UseSystemPasswordChar = !showPassword.Checked; };
            replacePassword.CheckedChanged += delegate { password.Clear(); UpdateWifiControls(); if (password.Visible) password.Focus(); };
            refreshAdapters.Click += delegate { LoadAdapters(); };
            refreshWifi.Click += async delegate { await LoadNetworks(true); };
            pickProcesses.Click += delegate { PickProcesses(); };
            start.Click += async delegate { await EnableMonitoring(); };
            stop.Click += async delegate { await StopMonitoring(); };
            statusTimer.Interval = 5000;
            statusTimer.Tick += delegate { RefreshStatus(); };
            Shown += delegate
            {
                if (preview) { PopulatePreview(); return; }
                try { saved = AppInstaller.LoadConfig(); }
                catch (Exception ex) { ShowNotice(ex.Message, true); }
                countryGuard.Checked = saved != null && saved.CountryProcessGuard;
                guardNames = CountryProcessGuard.EffectiveNames(saved); UpdateGuardTargets();
                LoadAdapters(); RefreshStatus(); statusTimer.Start();
            };
            FormClosed += delegate { statusTimer.Stop(); statusTimer.Dispose(); password.Clear(); };
            FormClosing += delegate(object sender, FormClosingEventArgs e) {
                if (busy || (toolsWindow != null && !toolsWindow.IsDisposed && toolsWindow.IsBusy)) { e.Cancel = true; ShowNotice("正在完成網路操作，請稍候再關閉視窗。", false); }
            };
        }

        int S(int value) { return (int)Math.Round(value * scale); }

        protected override void OnLoad(EventArgs e)
        {
            // Never open larger than the usable desktop; smaller windows scroll instead of overlapping.
            Rectangle area = Screen.FromControl(this).WorkingArea;
            Size = new Size(Math.Min(Width, area.Width), Math.Min(Height, area.Height));
            Location = new Point(area.Left + (area.Width - Width) / 2, area.Top + (area.Height - Height) / 2);
            layoutReady = true;
            ResizeContent();
            base.OnLoad(e);
        }

        protected override void OnClientSizeChanged(EventArgs e)
        {
            base.OnClientSizeChanged(e);
            ResizeContent();
        }

        // Two columns on wide windows, stacked cards on narrow ones; below the minimum the form scrolls.
        void ResizeContent()
        {
            if (!layoutReady) return;
            int available = ClientSize.Width + (VerticalScroll.Visible ? SystemInformation.VerticalScrollBarWidth : 0);
            bool wide = available >= S(900);
            Size minimum = wide ? new Size(S(860), S(1010)) : new Size(S(420), S(1460));
            SuspendLayout();
            content.SuspendLayout();
            if (wide != wideLayout) ApplyBodyMode(wide);
            content.Size = new Size(Math.Max(minimum.Width, ClientSize.Width), Math.Max(minimum.Height, ClientSize.Height));
            // Very wide screens keep a readable column instead of stretching the cards edge to edge.
            int rootWidth = Math.Min(content.Width, S(1180));
            root.SetBounds((content.Width - rootWidth) / 2, 0, rootWidth, content.Height);
            content.ResumeLayout(true);
            ResumeLayout(true);
            // Wrapped labels fix their height once they know their width; a second pass lets rows settle.
            content.PerformLayout();
        }

        void ApplyBodyMode(bool wide)
        {
            wideLayout = wide;
            body.SuspendLayout();
            body.Controls.Clear(); body.ColumnStyles.Clear(); body.RowStyles.Clear();
            if (wide)
            {
                body.ColumnCount = 2; body.RowCount = 1;
                body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 44F));
                body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 56F));
                body.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
                adapterCard.Margin = new Padding(0, 0, S(6), 0); wifiCard.Margin = new Padding(S(6), 0, 0, 0);
                body.Controls.Add(adapterCard, 0, 0); body.Controls.Add(wifiCard, 1, 0);
            }
            else
            {
                body.ColumnCount = 1; body.RowCount = 2;
                body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
                body.RowStyles.Add(new RowStyle(SizeType.Percent, 42F));
                body.RowStyles.Add(new RowStyle(SizeType.Percent, 58F));
                adapterCard.Margin = new Padding(0, 0, 0, S(6)); wifiCard.Margin = new Padding(0, S(6), 0, 0);
                body.Controls.Add(adapterCard, 0, 0); body.Controls.Add(wifiCard, 0, 1);
            }
            body.ResumeLayout(true);
            PlaceLinks(wide);
        }

        // Link buttons share the button row on wide windows; on narrow ones they move under the flow summary.
        void PlaceLinks(bool wide)
        {
            if (linkPanel == null || footerPanel == null || flowCard == null) return;
            if (linkPanel.Parent != null) linkPanel.Parent.Controls.Remove(linkPanel);
            if (wide) { linkPanel.Margin = Padding.Empty; footerPanel.Controls.Add(linkPanel, 0, 1); }
            else { linkPanel.Margin = new Padding(0, S(10), 0, 0); flowCard.Controls.Add(linkPanel, 0, 7); }
        }

        void FitAdapterColumns()
        {
            if (adapters.Columns.Count < 3) return;
            int total = adapters.ClientSize.Width;
            if (total <= 0) return;
            int first = (int)(total * 0.5), second = (int)(total * 0.22);
            adapters.Columns[0].Width = first; adapters.Columns[1].Width = second;
            adapters.Columns[2].Width = Math.Max(S(40), total - first - second);
        }

        // Single-line text that sizes itself from its font.
        Label Text1(string text, float size, Color color, bool bold)
        {
            return new Label { Text = text, AutoSize = true, ForeColor = color, Anchor = AnchorStyles.Left, Margin = Padding.Empty,
                Font = Theme.Font(size, bold) };
        }

        // Paragraph text that wraps to the width of its cell and grows the row instead of overlapping neighbours.
        WrapLabel Para(string text, float size, Color color, bool bold)
        {
            return new WrapLabel { Text = text, ForeColor = color, Dock = DockStyle.Fill, Margin = Padding.Empty,
                Font = Theme.Font(size, bold) };
        }

        void StyleButton(Button button, string text, bool primary) { Theme.StyleButton(button, text, primary ? 1 : 0); }

        // Card heading: numbered accent badge followed by the title.
        TableLayoutPanel Heading(string number, string title)
        {
            TableLayoutPanel heading = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty, Anchor = AnchorStyles.Left };
            heading.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); heading.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            Pill badge = new Pill { Text = number, Circle = true, Fill = Theme.Accent, ForeColor = Theme.OnAccent, AutoSize = false,
                Size = new Size(S(24), S(24)), Font = Theme.Font(9F, true), Margin = new Padding(0, 0, S(10), 0), Anchor = AnchorStyles.Left };
            heading.Controls.Add(badge, 0, 0);
            heading.Controls.Add(Text1(title, 12F, ink, true), 1, 0);
            return heading;
        }

        void BuildLayout()
        {
            root = new TableLayoutPanel {
                ColumnCount = 1, RowCount = 5, Margin = Padding.Empty,
                Padding = new Padding(S(28), S(22), S(28), S(20))
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            content.Controls.Add(root);

            // Header: app mark, product name and administrator badge.
            TableLayoutPanel header = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 3, RowCount = 1, Margin = new Padding(0, 0, 0, S(18)) };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            Panel brand = new Panel { Size = new Size(S(48), S(48)), Margin = new Padding(0, 0, S(14), 0), Anchor = AnchorStyles.Left };
            brand.Paint += delegate(object sender, PaintEventArgs e) { IconArt.Draw(e.Graphics, IconArt.App, new RectangleF(0, 0, brand.Width, brand.Height)); };
            TableLayoutPanel titles = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty, Anchor = AnchorStyles.Left };
            titles.Controls.Add(Text1("Network Recovery", 16F, ink, true), 0, 0);
            Label subtitle = Text1("連線守護 · 斷網時讓連線自行恢復", 9.5F, muted, false); subtitle.Margin = new Padding(0, S(2), 0, 0);
            titles.Controls.Add(subtitle, 0, 1);
            Pill tag = new Pill { Text = "管理員模式", Fill = Theme.SurfaceAlt, Border = Theme.Line, ForeColor = muted, Font = Theme.Font(8.5F, false),
                Padding = new Padding(S(12), S(5), S(12), S(5)), Anchor = AnchorStyles.Right, Margin = Padding.Empty };
            header.Controls.Add(brand, 0, 0); header.Controls.Add(titles, 1, 0); header.Controls.Add(tag, 2, 0);
            root.Controls.Add(header, 0, 0);

            // Status banner: always visible at the top so the monitoring state is the first thing read.
            // The indicator is painted in the card's left padding, so the text column keeps the full width to wrap in.
            Card banner = new Card(new Padding(S(70), S(14), S(20), S(14)), Color.Empty) { AutoSize = true, Margin = new Padding(0, 0, 0, S(12)) };
            banner.RowCount = 2;
            banner.RowStyles.Add(new RowStyle(SizeType.AutoSize)); banner.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            statusTone = Theme.Neutral;
            statusDot = banner;
            banner.Paint += delegate(object sender, PaintEventArgs e) {
                Graphics g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
                float d = S(36), x = S(20), y = (banner.Height - d) / 2F;
                using (SolidBrush halo = new SolidBrush(Theme.Mix(Theme.Surface, statusTone, 0.18F))) g.FillEllipse(halo, x, y, d, d);
                float c = d * 0.36F;
                using (SolidBrush core = new SolidBrush(statusTone)) g.FillEllipse(core, x + (d - c) / 2, y + (d - c) / 2, c, c);
            };
            Label caption = Text1("監控狀態", 8.5F, muted, false); caption.Margin = new Padding(0, 0, 0, S(2));
            banner.Controls.Add(caption, 0, 0);
            status.Font = Theme.Font(11F, FontStyle.Bold); status.ForeColor = ink; status.Dock = DockStyle.Fill; status.Margin = Padding.Empty;
            banner.Controls.Add(status, 0, 1);
            banner.AddSpacerRow();
            root.Controls.Add(banner, 0, 1);

            // Body: adapter card and Wi-Fi card.
            body = new TableLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty };
            adapterCard = BuildAdapterCard();
            wifiCard = BuildWifiCard();
            ApplyBodyMode(true);
            root.Controls.Add(body, 0, 2);

            // Protection flow summary and the optional country guard.
            Card flow = new Card(new Padding(S(20), S(16), S(20), S(16)), Color.Empty) { AutoSize = true, Margin = new Padding(0, S(12), 0, S(14)) };
            flow.RowCount = 8;
            for (int i = 0; i < 8; i++) flow.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Label flowTitle = Para("自動保護流程", 9F, muted, true); flowTitle.Margin = new Padding(0, 0, 0, S(10));
            flow.Controls.Add(flowTitle, 0, 0);
            StepFlow steps = new StepFlow { Steps = new[] { "斷網 3 分鐘", "修復網卡", "再等 60 秒", "重新啟動電腦" }, Dock = DockStyle.Fill,
                Margin = Padding.Empty, Font = Theme.Font(9F, true) };
            flow.Controls.Add(steps, 0, 1);
            Label flowNote = Para("開機後先給網路 5 分鐘恢復；持續無網將再次觸發修復。", 9F, muted, false); flowNote.Margin = new Padding(0, S(10), 0, 0);
            flow.Controls.Add(flowNote, 0, 2);
            Panel divider = new Panel { Height = 1, Dock = DockStyle.Fill, BackColor = Theme.Line, Margin = new Padding(0, S(14), 0, S(12)) };
            flow.Controls.Add(divider, 0, 3);
            countryGuard.Text = "啟用地區程序保護";
            countryGuard.Margin = new Padding(0, 0, 0, S(2));
            flow.Controls.Add(countryGuard, 0, 4);
            Label guardNote = Para("每 120 秒查詢外網 IP 地區；CN／MO／HK 時強制結束下列程序，未儲存內容可能遺失。", 9F, muted, false);
            guardNote.Margin = new Padding(S(28), 0, 0, 0);
            flow.Controls.Add(guardNote, 0, 5);
            TableLayoutPanel targets = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, RowCount = 1, Margin = new Padding(S(28), S(10), 0, 0) };
            targets.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F)); targets.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            guardTargets.Dock = DockStyle.Fill; guardTargets.ForeColor = ink; guardTargets.Margin = new Padding(0, 0, S(10), 0);
            guardTargets.Font = Theme.Font(9F, false); guardTargets.TextAlign = ContentAlignment.MiddleLeft;
            targets.Controls.Add(guardTargets, 0, 0);
            StyleButton(pickProcesses, "選擇程序", false); pickProcesses.Anchor = AnchorStyles.Right;
            targets.Controls.Add(pickProcesses, 1, 0);
            flow.Controls.Add(targets, 0, 6);
            UpdateGuardTargets();
            flow.AddSpacerRow();
            root.Controls.Add(flow, 0, 3);

            // Footer: notice on top, buttons below. Rows can only grow downward, so nothing can cover a button.
            TableLayoutPanel footer = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 3, RowCount = 2, Margin = Padding.Empty };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            footer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            footer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            notice.ForeColor = muted; notice.Dock = DockStyle.Fill; notice.Margin = new Padding(0, 0, 0, S(12));
            notice.Text = "啟用後會在背景持續運作。自動重啟會關閉程式，請定期儲存工作。";
            footer.Controls.Add(notice, 0, 0); footer.SetColumnSpan(notice, 3);
            ThemedButton settings = new ThemedButton { Glyph = '', Anchor = AnchorStyles.Left };
            Theme.StyleButton(settings, "Windows Wi-Fi 設定", 3);
            settings.Click += delegate { if (!preview) Process.Start(new ProcessStartInfo("explorer.exe", "ms-settings:network-wifi") { UseShellExecute = true }); };
            ThemedButton tools = new ThemedButton { Glyph = '', Anchor = AnchorStyles.Left };
            Theme.StyleButton(tools, "監控工具", 3);
            tools.Margin = new Padding(S(4), 0, 0, 0);
            tools.Click += delegate {
                if (busy) return;
                if (toolsWindow == null || toolsWindow.IsDisposed) { toolsWindow = new MonitorToolsForm(preview); toolsWindow.Show(this); }
                else toolsWindow.Activate();
            };
            FlowLayoutPanel links = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = Padding.Empty, Anchor = AnchorStyles.Left };

            links.Controls.Add(settings); links.Controls.Add(tools);
            linkPanel = links; flowCard = flow; footerPanel = footer;
            StyleButton(stop, "停止監控", false); stop.Anchor = AnchorStyles.Right; stop.Margin = new Padding(S(12), 0, 0, 0); footer.Controls.Add(stop, 1, 1);
            StyleButton(start, "儲存並啟用", true); start.Anchor = AnchorStyles.Right; start.Margin = new Padding(S(10), 0, 0, 0); footer.Controls.Add(start, 2, 1);
            root.Controls.Add(footer, 0, 4);
            PlaceLinks(wideLayout);
            StyleInputs();
            UpdateWifiControls();
        }

        // Inputs and the adapter list are owner-drawn so both palettes stay consistent.
        void StyleInputs()
        {
            Color field = Theme.Field, card = Theme.Surface, sel = Theme.Selection;
            networks.FlatStyle = FlatStyle.Flat; networks.BackColor = field; networks.ForeColor = ink;
            networks.DrawMode = DrawMode.OwnerDrawFixed; networks.ItemHeight = S(26);
            Theme.ApplyScrollTheme(networks, "DarkMode_CFD");
            networks.DrawItem += delegate(object s, DrawItemEventArgs e) {
                if (e.Index < 0) { using (SolidBrush b = new SolidBrush(field)) e.Graphics.FillRectangle(b, e.Bounds); return; }
                bool edit = (e.State & DrawItemState.ComboBoxEdit) != 0;
                bool hot = (e.State & DrawItemState.Selected) != 0 && !edit;
                using (SolidBrush b = new SolidBrush(hot ? sel : field)) e.Graphics.FillRectangle(b, e.Bounds);
                TextRenderer.DrawText(e.Graphics, networks.GetItemText(networks.Items[e.Index]), networks.Font,
                    new Rectangle(e.Bounds.X + S(edit ? 6 : 10), e.Bounds.Y, e.Bounds.Width - S(12), e.Bounds.Height), networks.Enabled ? ink : muted,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            };
            foreach (CheckBox box in new[] { reconnect, autoConnect, showPassword, replacePassword, countryGuard }) box.ForeColor = ink;
            adapters.BackColor = card; adapters.ForeColor = ink; adapters.BorderStyle = BorderStyle.None;
            adapters.OwnerDraw = true;
            // A tall, empty image list is the standard way to give ListView rows breathing room.
            adapters.SmallImageList = new ImageList { ImageSize = new Size(1, S(34)) };
            Theme.ApplyScrollTheme(adapters, "DarkMode_Explorer");
            adapters.DrawColumnHeader += delegate(object s, DrawListViewColumnHeaderEventArgs e) {
                using (SolidBrush b = new SolidBrush(card)) e.Graphics.FillRectangle(b, e.Bounds);
                using (Pen p = new Pen(Theme.Line)) e.Graphics.DrawLine(p, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
                TextRenderer.DrawText(e.Graphics, e.Header.Text, adapters.Font, new Rectangle(e.Bounds.X + S(12), e.Bounds.Y, e.Bounds.Width - S(12), e.Bounds.Height), muted,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            };
            adapters.DrawItem += delegate(object s, DrawListViewItemEventArgs e) { };
            adapters.DrawSubItem += delegate(object s, DrawListViewSubItemEventArgs e) {
                Graphics g = e.Graphics;
                Rectangle row = e.Bounds;
                using (SolidBrush b = new SolidBrush(card)) g.FillRectangle(b, row);
                if (e.Item.Selected)
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    // One rounded highlight across the whole row: each column paints its slice of it.
                    Rectangle full = e.Item.Bounds;
                    RectangleF pill = new RectangleF(full.X + 0.5F, full.Y + S(2) + 0.5F, adapters.ClientSize.Width - 1F, full.Height - S(4) - 1F);
                    g.SetClip(row);
                    Theme.FillRound(g, sel, pill, S(5));
                    if (e.ColumnIndex == 0) Theme.FillRound(g, accent, new RectangleF(full.X + S(2), full.Y + S(10), S(3), full.Height - S(20)), S(2));
                    g.ResetClip();
                }
                int left = row.X + S(12);
                Color text = e.ColumnIndex == 0 ? ink : muted;
                if (e.ColumnIndex == 2)
                {
                    bool connected = e.SubItem.Text == "已連線";
                    Color dot = connected ? Theme.Success : Theme.Neutral;
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    int d = S(8);
                    using (SolidBrush b = new SolidBrush(dot)) g.FillEllipse(b, left, row.Y + (row.Height - d) / 2, d, d);
                    left += d + S(8);
                    text = connected ? Theme.Success : muted;
                }

                TextRenderer.DrawText(g, e.SubItem.Text, adapters.Font, new Rectangle(left, row.Y, row.Right - left - S(4), row.Height), text,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            };
        }

        Card BuildAdapterCard()
        {
            Card card = new Card(new Padding(S(20), S(18), S(20), S(18)), Color.Empty);
            card.ColumnCount = 2; card.RowCount = 4;
            card.ColumnStyles.Clear();
            card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            card.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            card.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            card.Controls.Add(Heading("1", "選擇網卡"), 0, 0);
            StyleButton(refreshAdapters, "重新整理", false); refreshAdapters.Anchor = AnchorStyles.Right; card.Controls.Add(refreshAdapters, 1, 0);
            Label hint = Para("所有網卡都會列出，包含 VPN 與虛擬網卡。", 9F, muted, false); hint.Margin = new Padding(0, S(8), 0, S(12));
            card.Controls.Add(hint, 0, 1); card.SetColumnSpan(hint, 2);
            adapters.Dock = DockStyle.Fill; adapters.Margin = Padding.Empty; adapters.MinimumSize = new Size(0, S(110));
            adapters.View = View.Details; adapters.FullRowSelect = true; adapters.HideSelection = false; adapters.MultiSelect = false;
            adapters.HeaderStyle = ColumnHeaderStyle.Nonclickable;
            adapters.ShowItemToolTips = true;
            adapters.Columns.Add("網卡", S(160)); adapters.Columns.Add("類型", S(90)); adapters.Columns.Add("狀態", S(90));
            card.Controls.Add(adapters, 0, 2); card.SetColumnSpan(adapters, 2);
            adapterInfo.Dock = DockStyle.Fill; adapterInfo.ForeColor = muted; adapterInfo.Margin = new Padding(0, S(12), 0, 0);
            adapterInfo.Text = "選擇實際用來上網的網卡。";
            card.Controls.Add(adapterInfo, 0, 3); card.SetColumnSpan(adapterInfo, 2);
            return card;
        }

        Card BuildWifiCard()
        {
            Card card = new Card(new Padding(S(20), S(18), S(20), S(18)), Color.Empty);
            card.ColumnCount = 2; card.RowCount = 10;
            card.ColumnStyles.Clear();
            card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            card.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            for (int i = 0; i < 9; i++) card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            card.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            TableLayoutPanel title = Heading("2", "Wi-Fi 重連"); title.Margin = new Padding(0, 0, 0, S(14));
            card.Controls.Add(title, 0, 0); card.SetColumnSpan(title, 2);

            reconnect.Text = "斷網修復後，重新連接指定 Wi-Fi"; reconnect.Checked = true; reconnect.Anchor = AnchorStyles.Left; reconnect.Margin = new Padding(0, 0, 0, S(14));
            card.Controls.Add(reconnect, 0, 1); card.SetColumnSpan(reconnect, 2);

            Label networkLabel = Text1("Wi-Fi 網路", 9F, muted, true); networkLabel.Margin = new Padding(0, 0, 0, S(6));
            card.Controls.Add(networkLabel, 0, 2); card.SetColumnSpan(networkLabel, 2);
            networks.DropDownStyle = ComboBoxStyle.DropDownList; networks.Dock = DockStyle.Fill; networks.Margin = new Padding(0, 0, S(10), 0);
            card.Controls.Add(networks, 0, 3);
            StyleButton(refreshWifi, "重新掃描", false); refreshWifi.Anchor = AnchorStyles.Right; card.Controls.Add(refreshWifi, 1, 3);

            wifiInfo.Dock = DockStyle.Fill; wifiInfo.ForeColor = muted; wifiInfo.Margin = new Padding(0, S(10), 0, S(12));
            card.Controls.Add(wifiInfo, 0, 4); card.SetColumnSpan(wifiInfo, 2);

            passwordLabel.Text = "Wi-Fi 密碼"; passwordLabel.AutoSize = true; passwordLabel.Anchor = AnchorStyles.Left; passwordLabel.Margin = new Padding(0, 0, 0, S(6));
            passwordLabel.Font = Theme.Font(9F, true); passwordLabel.ForeColor = muted;
            card.Controls.Add(passwordLabel, 0, 5);
            replacePassword.Text = "更新已儲存的密碼"; replacePassword.Anchor = AnchorStyles.Right; replacePassword.Margin = new Padding(0, 0, 0, S(4));
            card.Controls.Add(replacePassword, 1, 5);
            password.UseSystemPasswordChar = true; password.MaxLength = 64;
            password.Font = Theme.Font(10F, false);
            passwordFrame = new InputFrame(password) { Dock = DockStyle.Fill, Margin = new Padding(0, 0, S(10), 0) };
            card.Controls.Add(passwordFrame, 0, 6);
            showPassword.Text = "顯示密碼"; showPassword.Anchor = AnchorStyles.Right; showPassword.Margin = Padding.Empty;
            card.Controls.Add(showPassword, 1, 6);

            autoConnect.Text = "使用 Windows 自動連線"; autoConnect.Checked = true; autoConnect.AutoCheck = false; autoConnect.Anchor = AnchorStyles.Left; autoConnect.Margin = new Padding(0, S(12), 0, 0);
            card.Controls.Add(autoConnect, 0, 7); card.SetColumnSpan(autoConnect, 2);

            Label note = Para("密碼由 Windows 保存，不會寫入本應用的設定或紀錄。", 9F, muted, false); note.Margin = new Padding(0, S(14), 0, 0);
            card.Controls.Add(note, 0, 8); card.SetColumnSpan(note, 2);
            return card;
        }

        AdapterInfo SelectedAdapter { get { return adapters.SelectedItems.Count == 0 ? null : adapters.SelectedItems[0].Tag as AdapterInfo; } }
        WifiNetwork SelectedNetwork { get { return networks.SelectedItem as WifiNetwork; } }

        async void LoadAdapters()
        {
            if (busy || preview) return;
            refreshAdapters.Enabled = false; adapters.Enabled = false;
            try
            {
                string selected = SelectedAdapter == null ? (saved == null ? "" : saved.AdapterId) : SelectedAdapter.Id;
                List<AdapterInfo> items = await Task.Run(delegate { return NetworkManager.GetAdapters(); });
                if (IsDisposed) return;
                populating = true; adapters.Items.Clear();
                foreach (AdapterInfo adapter in items.OrderByDescending(a => a.IsWireless).ThenBy(a => a.Name))
                {
                    ListViewItem item = new ListViewItem(adapter.Name); item.SubItems.Add(adapter.IsWireless ? "Wi-Fi" : "其他網卡"); item.SubItems.Add(adapter.State);
                    item.Tag = adapter; item.ToolTipText = adapter.Description; adapters.Items.Add(item);
                    if (String.Equals(adapter.Id, selected, StringComparison.OrdinalIgnoreCase)) item.Selected = true;
                }
                if (adapters.SelectedItems.Count == 0 && adapters.Items.Count > 0) adapters.Items[0].Selected = true;
                populating = false; AdapterChanged();
                if (items.Count == 0) ShowNotice("找不到網卡。請確認裝置管理員中有可用的網路介面。", true);
            }
            catch (Exception ex) { populating = false; ShowNotice(ex.Message, true); }
            finally { if (!IsDisposed) { refreshAdapters.Enabled = !busy; adapters.Enabled = !busy; } }
        }

        async void AdapterChanged()
        {
            selectionVersion++; password.Clear();
            AdapterInfo adapter = SelectedAdapter;
            adapterInfo.Text = adapter == null ? "請選擇網卡。" : adapter.Description;
            reconnect.Checked = adapter != null && adapter.IsWireless && (saved == null || !String.Equals(saved.AdapterId, adapter.Id, StringComparison.OrdinalIgnoreCase) || saved.ReconnectWifi);
            loading = true; networks.Items.Clear(); loading = false;
            UpdateWifiControls();
            if (adapter != null && adapter.IsWireless) await LoadNetworks(false);
        }

        async Task LoadNetworks(bool requestScan)
        {
            AdapterInfo adapter = SelectedAdapter;
            if (preview || adapter == null || !adapter.IsWireless || busy) return;
            int version = selectionVersion;
            int request = ++networkRequest;
            Guid id;
            if (!Guid.TryParse(adapter.Id, out id)) { ShowNotice("這個介面沒有有效的網卡識別碼。", true); return; }
            refreshWifi.Enabled = false; wifiInfo.Text = "正在讀取可用與已儲存的 Wi-Fi…";
            try
            {
                List<WifiNetwork> items = await Task.Run(delegate {
                    if (requestScan) { NetworkManager.Scan(id); System.Threading.Thread.Sleep(1500); }
                    return NetworkManager.GetNetworks(id);
                });
                if (IsDisposed || busy || version != selectionVersion || request != networkRequest) return;
                loading = true; networks.Items.Clear();
                foreach (WifiNetwork network in items.OrderByDescending(n => n.IsConnected).ThenByDescending(n => n.HasSavedProfile).ThenByDescending(n => n.Signal)) networks.Items.Add(network);
                WifiNetwork chosen = items.FirstOrDefault(n => saved != null && n.ProfileName == saved.WifiProfileName) ?? items.FirstOrDefault(n => n.IsConnected) ?? items.FirstOrDefault(n => n.HasSavedProfile) ?? items.FirstOrDefault();
                if (chosen != null) networks.SelectedItem = chosen;
                loading = false; NetworkChanged();
                if (items.Count == 0) wifiInfo.Text = "找不到 Wi-Fi。請開啟 Wi-Fi 開關後重新掃描；也可先在 Windows 完成連線。";
            }
            catch (Exception ex) { loading = false; if (!IsDisposed && !busy && version == selectionVersion && request == networkRequest) { wifiInfo.Text = "無法讀取 Wi-Fi 清單。"; ShowNotice(ex.Message, true); } }
            finally { if (!IsDisposed && request == networkRequest) UpdateWifiControls(); }
        }

        void NetworkChanged()
        {
            password.Clear(); showPassword.Checked = false; replacePassword.Checked = false;
            WifiNetwork wifi = SelectedNetwork;
            if (wifi != null)
            {
                wifiInfo.Text = wifi.HasSavedProfile ? "已儲存 · 使用 Windows 現有的連線設定，不需要再次輸入密碼。" :
                    wifi.IsOpen ? "開放網路 · 不需要密碼。" : wifi.IsSupportedPersonal ? "尚未儲存 · 請輸入密碼，啟用時會一併設定自動連線。" : "此網路需要額外的登入或企業驗證，請先在 Windows Wi-Fi 設定完成連線。";
            }
            else wifiInfo.Text = SelectedAdapter != null && SelectedAdapter.IsWireless ? "請選擇一個 Wi-Fi。" : "所選網卡不需要 Wi-Fi 設定，修復時只會重啟該網卡。";
            UpdateWifiControls();
            if (password.Visible && password.Enabled) password.Focus();
        }

        void UpdateWifiControls()
        {
            AdapterInfo adapter = SelectedAdapter;
            bool wireless = adapter != null && adapter.IsWireless;
            reconnect.Enabled = wireless && !busy;
            networks.Enabled = wireless && reconnect.Checked && !busy;
            refreshWifi.Enabled = wireless && !busy;
            WifiNetwork wifi = SelectedNetwork;
            replacePassword.Visible = wireless && reconnect.Checked && wifi != null && wifi.HasSavedProfile && !wifi.IsOpen && wifi.IsSupportedPersonal;
            replacePassword.Enabled = !busy;
            bool needsPassword = wireless && reconnect.Checked && wifi != null && (!wifi.HasSavedProfile || replacePassword.Checked) && !wifi.IsOpen && wifi.IsSupportedPersonal;
            passwordLabel.Visible = passwordFrame.Visible = password.Visible = showPassword.Visible = needsPassword;
            password.Enabled = showPassword.Enabled = needsPassword && !busy;
            autoConnect.Visible = wireless && reconnect.Checked;
            countryGuard.Enabled = !busy;
            pickProcesses.Enabled = !busy;
            start.Enabled = !busy && adapter != null;
        }

        async Task EnableMonitoring()
        {
            AdapterInfo adapter = SelectedAdapter;
            if (adapter == null || busy || preview) return;
            WifiNetwork wifi = SelectedNetwork;
            bool useWifi = adapter.IsWireless && reconnect.Checked;
            if (useWifi && wifi == null) { ShowNotice("請先選擇要自動重連的 Wi-Fi。", true); return; }
            if (useWifi && !wifi.HasSavedProfile && !wifi.IsOpen && !wifi.IsSupportedPersonal) { ShowNotice("請先使用 Windows 完成這個 Wi-Fi 的驗證，再重新整理清單。", true); return; }
            if (useWifi && (!wifi.HasSavedProfile || replacePassword.Checked) && !wifi.IsOpen && String.IsNullOrEmpty(password.Text)) { ShowNotice("請輸入 Wi-Fi 密碼。", true); password.Focus(); return; }
            if (countryGuard.Checked && guardNames.Length == 0) { ShowNotice("已啟用地區程序保護，請先按「選擇程序」選擇至少一個要結束的程序。", true); return; }
            string enteredPassword = password.Visible ? password.Text : "";
            busy = true; networkRequest++; adapters.Enabled = false; refreshAdapters.Enabled = false; stop.Enabled = false; UpdateWifiControls(); start.Text = "正在啟用…";
            ShowNotice("正在儲存設定並啟用背景監控…", false);
            try
            {
                AppConfig config = new AppConfig { AdapterId = adapter.Id, AdapterName = adapter.Name, ReconnectWifi = useWifi, WifiSsidHex = useWifi ? wifi.SsidHex : "", CountryProcessGuard = countryGuard.Checked, GuardProcessNames = (string[])guardNames.Clone() };
                await Task.Run(delegate {
                    using (ActionLease action = new ActionLease(1000))
                    {
                    if (useWifi)
                    {
                        config.WifiProfileName = NetworkManager.ConfigureWifi(new Guid(adapter.Id), wifi, enteredPassword);
                        NetworkManager.Connect(new Guid(adapter.Id), config.WifiProfileName);
                        if (!NetworkManager.WaitForConnection(new Guid(adapter.Id), config.WifiProfileName, 30000))
                            throw new InvalidOperationException("Wi-Fi 尚未成功連線，監控未切換。請確認訊號與密碼；若密碼錯誤，請重新掃描、選擇該 Wi-Fi，勾選『更新已儲存的密碼』後再試。" );
                    }
                    AppInstaller.Install(config);
                    }
                });
                saved = config; password.Clear();
                ShowNotice("已啟用。關閉這個視窗後，監控仍會在背景和每次開機後執行。", false);
                RefreshStatus();
            }
            catch (Exception ex) { ShowNotice(ex.Message, true); }
            finally { enteredPassword = null; busy = false; adapters.Enabled = true; refreshAdapters.Enabled = true; stop.Enabled = true; start.Text = "儲存並啟用"; UpdateWifiControls(); RefreshStatus(); }
        }

        async Task StopMonitoring()
        {
            if (preview || busy) return;
            busy = true; stop.Enabled = false; UpdateWifiControls();
            try { await Task.Run(delegate { AppInstaller.Stop(); }); ShowNotice("已停止本應用的自動修復與重啟。", false); RefreshStatus(); }
            catch (Exception ex) { ShowNotice(ex.Message, true); }
            finally { busy = false; stop.Enabled = true; UpdateWifiControls(); }
        }

        void RefreshStatus()
        {
            if (preview || IsDisposed || busy) return;
            try { SetStatus(AppInstaller.GetStatus()); }
            catch { SetStatus("無法讀取監控狀態"); }
        }

        // Status text plus a colored indicator: green while monitoring, gray when off, amber otherwise.
        void SetStatus(string text)
        {
            status.Text = text;
            Icon icon = AppIcons.ForStatus(text);
            statusTone = text.StartsWith("無法", StringComparison.Ordinal) ? Theme.Danger :
                icon == AppIcons.Enabled ? Theme.Success : icon == AppIcons.Disabled || text.StartsWith("尚未", StringComparison.Ordinal) ? Theme.Neutral : Theme.Warning;
            if (statusDot != null) statusDot.Invalidate();
        }

        void UpdateGuardTargets()
        {
            if (guardNames.Length == 0) { guardTargets.Text = "尚未選擇要結束的程序。"; return; }
            string shown = String.Join("、", guardNames.Take(8).Select(n => n + ".exe"));
            guardTargets.Text = "要結束的程序：" + shown + (guardNames.Length > 8 ? " 等 " + guardNames.Length + " 個" : "");
        }

        // Names only are kept; the picker reads the live process list each time it opens.
        void PickProcesses()
        {
            if (busy) return;
            using (ProcessPickerForm picker = new ProcessPickerForm(preview, guardNames))
            {
                if (picker.ShowDialog(this) != DialogResult.OK) return;
                guardNames = picker.SelectedNames;
            }
            UpdateGuardTargets();
            ShowNotice("已更新要結束的程序；按「儲存並啟用」後生效。", false);
        }

        void ShowNotice(string message, bool error) { notice.Text = message; notice.ForeColor = error ? Theme.Danger : muted; }

        void PopulatePreview()
        {
            populating = true;
            AdapterInfo demo = new AdapterInfo { Id = Guid.Empty.ToString(), Name = "Wi-Fi", Description = "Intel(R) Wi-Fi 6 AX200 160MHz", State = "已連線", IsWireless = true };
            foreach (AdapterInfo a in new[] { demo, new AdapterInfo { Name = "Ethernet", Description = "Realtek PCIe GbE Family Controller", State = "未連線" }, new AdapterInfo { Name = "ProtonVPN", Description = "WireGuard Tunnel", State = "已連線" } })
            {
                ListViewItem item = new ListViewItem(a.Name); item.SubItems.Add(a.IsWireless ? "Wi-Fi" : "其他網卡"); item.SubItems.Add(a.State); item.Tag = a; adapters.Items.Add(item);
            }
            adapters.Items[0].Selected = true; populating = false;
            loading = true;
            networks.Items.Add(new WifiNetwork { Ssid = "Office Wi-Fi", ProfileName = "Office Wi-Fi", Security = "WPA2-Personal", HasSavedProfile = true, IsConnected = true, IsSupportedPersonal = true, Signal = 92 });
            networks.SelectedIndex = 0; loading = false;
            adapterInfo.Text = demo.Description; NetworkChanged();
            SetStatus("尚未啟用 · 完成設定後即可開始保護");
        }
    }
}
