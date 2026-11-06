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
        const string FontName = "Microsoft JhengHei UI";
        readonly Color ink = Color.FromArgb(226, 235, 248);
        readonly Color muted = Color.FromArgb(127, 146, 176);
        readonly Color blue = Color.FromArgb(34, 211, 238);
        readonly Color line = Color.FromArgb(34, 52, 82);
        readonly bool preview;
        readonly float scale;
        readonly Panel content = new Panel();
        TableLayoutPanel root;
        TableLayoutPanel body;
        Card adapterCard;
        Card wifiCard;
        bool wideLayout;
        bool layoutReady;
        readonly ListView adapters = new ListView();
        readonly ComboBox networks = new ComboBox();
        readonly CheckBox reconnect = new PlainCheckBox();
        readonly CheckBox autoConnect = new PlainCheckBox();
        readonly CheckBox showPassword = new PlainCheckBox();
        readonly CheckBox replacePassword = new PlainCheckBox();
        readonly TextBox password = new TextBox();
        readonly Label passwordLabel = new Label();
        readonly Label wifiInfo = new WrapLabel();
        readonly Label adapterInfo = new WrapLabel();
        readonly Label status = new WrapLabel();
        readonly Label notice = new WrapLabel();
        readonly Button start = new Button();
        readonly Button stop = new Button();
        readonly Button refreshAdapters = new Button();
        readonly Button refreshWifi = new Button();
        readonly System.Windows.Forms.Timer statusTimer = new System.Windows.Forms.Timer();
        AppConfig saved;
        bool busy;
        bool loading;
        bool populating;
        int selectionVersion;
        int networkRequest;

        public MainForm(bool previewMode)
        {
            SuspendLayout();
            preview = previewMode;
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) scale = g.DpiX / 96F;
            Text = "Network Recovery · 連線守護";
            Font = new Font(FontName, 9F);
            BackColor = Color.FromArgb(9, 14, 26);
            ForeColor = ink;
            // Every pixel value goes through S(); fonts are point-based, so text and layout scale together.
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(S(1000), S(680));
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
            start.Click += async delegate { await EnableMonitoring(); };
            stop.Click += async delegate { await StopMonitoring(); };
            statusTimer.Interval = 5000;
            statusTimer.Tick += delegate { RefreshStatus(); };
            Shown += delegate
            {
                if (preview) { PopulatePreview(); return; }
                try { saved = AppInstaller.LoadConfig(); }
                catch (Exception ex) { ShowNotice(ex.Message, true); }
                LoadAdapters(); RefreshStatus(); statusTimer.Start();
            };
            FormClosed += delegate { statusTimer.Stop(); statusTimer.Dispose(); password.Clear(); };
            FormClosing += delegate(object sender, FormClosingEventArgs e) {
                if (busy) { e.Cancel = true; ShowNotice("正在完成監控設定，請稍候再關閉視窗。", false); }
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
            Size minimum = wide ? new Size(S(860), S(700)) : new Size(S(420), S(1080));
            SuspendLayout();
            content.SuspendLayout();
            if (wide != wideLayout) ApplyBodyMode(wide);
            content.Size = new Size(Math.Max(minimum.Width, ClientSize.Width), Math.Max(minimum.Height, ClientSize.Height));
            // Very wide screens keep a readable column instead of stretching the cards edge to edge.
            int rootWidth = Math.Min(content.Width, S(1240));
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
                body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40F));
                body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60F));
                body.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
                adapterCard.Margin = new Padding(0, 0, S(7), 0); wifiCard.Margin = new Padding(S(7), 0, 0, 0);
                body.Controls.Add(adapterCard, 0, 0); body.Controls.Add(wifiCard, 1, 0);
            }
            else
            {
                body.ColumnCount = 1; body.RowCount = 2;
                body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
                body.RowStyles.Add(new RowStyle(SizeType.Percent, 42F));
                body.RowStyles.Add(new RowStyle(SizeType.Percent, 58F));
                adapterCard.Margin = new Padding(0, 0, 0, S(7)); wifiCard.Margin = new Padding(0, S(7), 0, 0);
                body.Controls.Add(adapterCard, 0, 0); body.Controls.Add(wifiCard, 0, 1);
            }
            body.ResumeLayout(true);
        }

        void FitAdapterColumns()
        {
            if (adapters.Columns.Count < 3) return;
            int total = adapters.ClientSize.Width;
            if (total <= 0) return;
            int first = (int)(total * 0.46), second = (int)(total * 0.24);
            adapters.Columns[0].Width = first; adapters.Columns[1].Width = second;
            adapters.Columns[2].Width = Math.Max(S(40), total - first - second);
        }

        // Single-line text that sizes itself from its font.
        Label Text1(string text, float size, Color color, bool bold)
        {
            return new Label { Text = text, AutoSize = true, ForeColor = color, Anchor = AnchorStyles.Left, Margin = Padding.Empty,
                Font = new Font(FontName, size, bold ? FontStyle.Bold : FontStyle.Regular) };
        }

        // Paragraph text that wraps to the width of its cell and grows the row instead of overlapping neighbours.
        WrapLabel Para(string text, float size, Color color, bool bold)
        {
            return new WrapLabel { Text = text, ForeColor = color, Dock = DockStyle.Fill, Margin = Padding.Empty,
                Font = new Font(FontName, size, bold ? FontStyle.Bold : FontStyle.Regular) };
        }

        void StyleButton(Button button, string text, bool primary)
        {
            button.Text = text; button.FlatStyle = FlatStyle.Flat; button.Cursor = Cursors.Hand;
            button.AutoSize = true; button.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            button.MinimumSize = new Size(S(primary ? 150 : 104), S(40));
            button.Padding = new Padding(S(14), S(4), S(14), S(4));
            button.Margin = Padding.Empty;
            button.FlatAppearance.BorderSize = primary ? 0 : 1;
            button.FlatAppearance.BorderColor = Color.FromArgb(52, 74, 112);
            button.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(103, 232, 249) : Color.FromArgb(30, 45, 76);
            button.FlatAppearance.MouseDownBackColor = primary ? Color.FromArgb(6, 182, 212) : Color.FromArgb(38, 56, 92);
            button.Font = new Font(FontName, 9.5F, FontStyle.Bold);
            button.UseVisualStyleBackColor = false;
            Action paint = delegate {
                button.BackColor = button.Enabled ? (primary ? blue : Color.FromArgb(20, 30, 50)) : Color.FromArgb(22, 32, 50);
                button.ForeColor = button.Enabled ? (primary ? Color.FromArgb(4, 18, 32) : ink) : Color.FromArgb(88, 104, 130);
            };
            button.EnabledChanged += delegate { paint(); };
            paint();
        }

        void BuildLayout()
        {
            root = new TableLayoutPanel {
                ColumnCount = 1, RowCount = 5, Margin = Padding.Empty,
                Padding = new Padding(S(24), S(18), S(24), S(18))
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            content.Controls.Add(root);

            // Header: brand mark, product name and administrator badge.
            TableLayoutPanel header = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 3, RowCount = 1, Margin = new Padding(0, 0, 0, S(14)) };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            Panel brand = new Panel { BackColor = blue, Size = new Size(S(42), S(42)), Margin = new Padding(0, 0, S(12), 0), Anchor = AnchorStyles.Left };
            brand.Paint += delegate(object sender, PaintEventArgs e) {
                e.Graphics.ScaleTransform(brand.ClientSize.Width / 38F, brand.ClientSize.Height / 38F);
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (Pen p = new Pen(Color.FromArgb(4, 18, 32), 2F)) {
                    e.Graphics.DrawArc(p, 7, 7, 23, 23, 214, 112);
                    e.Graphics.DrawArc(p, 12, 14, 13, 13, 210, 120);
                }
                e.Graphics.FillEllipse(Brushes.Black, 17, 25, 4, 4);
            };
            TableLayoutPanel titles = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty, Anchor = AnchorStyles.Left };
            titles.Controls.Add(Text1("NETWORK RECOVERY", 13F, ink, true), 0, 0);
            titles.Controls.Add(Text1("連線守護 · 斷網時讓連線自行恢復", 9F, muted, false), 0, 1);
            Label tag = Text1("管理員模式", 9F, blue, true);
            tag.BackColor = Color.FromArgb(10, 52, 68); tag.Padding = new Padding(S(12), S(5), S(12), S(5)); tag.Anchor = AnchorStyles.Right;
            header.Controls.Add(brand, 0, 0); header.Controls.Add(titles, 1, 0); header.Controls.Add(tag, 2, 0);
            root.Controls.Add(header, 0, 0);

            // Status banner: always visible at the top so the monitoring state is the first thing read.
            Card banner = new Card(new Padding(S(20), S(12), S(20), S(12)), blue) { AutoSize = true, Margin = new Padding(0, 0, 0, S(14)) };
            banner.RowCount = 1; banner.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            status.Font = new Font(FontName, 10F, FontStyle.Bold); status.ForeColor = ink; status.Dock = DockStyle.Fill; status.Margin = Padding.Empty;
            banner.Controls.Add(status, 0, 0);
            root.Controls.Add(banner, 0, 1);

            // Body: adapter card and Wi-Fi card.
            body = new TableLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty };
            adapterCard = BuildAdapterCard();
            wifiCard = BuildWifiCard();
            ApplyBodyMode(true);
            root.Controls.Add(body, 0, 2);

            // Protection flow summary.
            Card flow = new Card(new Padding(S(20), S(12), S(20), S(12)), Color.Empty) { AutoSize = true, Margin = new Padding(0, S(14), 0, S(14)) };
            flow.RowCount = 3;
            for (int i = 0; i < 3; i++) flow.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Label flowTitle = Para("目前的保護流程", 9F, muted, true); flowTitle.Margin = new Padding(0, 0, 0, S(3));
            flow.Controls.Add(flowTitle, 0, 0);
            flow.Controls.Add(Para("斷網 3 分鐘 → 修復網卡 → 再等 60 秒 → 重啟電腦", 10.5F, blue, true), 0, 1);
            Label flowNote = Para("開機後給網路 5 分鐘恢復；持續無網將再次觸發修復。", 9F, muted, false); flowNote.Margin = new Padding(0, S(3), 0, 0);
            flow.Controls.Add(flowNote, 0, 2);
            root.Controls.Add(flow, 0, 3);

            // Footer: notice on top, buttons below. Rows can only grow downward, so nothing can cover a button.
            TableLayoutPanel footer = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 3, RowCount = 2, Margin = Padding.Empty };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            footer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            footer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            notice.ForeColor = muted; notice.Dock = DockStyle.Fill; notice.Margin = new Padding(0, 0, 0, S(10));
            notice.Text = "啟用後會在背景持續運作。自動重啟會關閉程式，請定期儲存工作。";
            footer.Controls.Add(notice, 0, 0); footer.SetColumnSpan(notice, 3);
            Button settings = new Button { Text = "Windows Wi-Fi 設定", FlatStyle = FlatStyle.Flat, ForeColor = blue, Cursor = Cursors.Hand,
                AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Anchor = AnchorStyles.Left, Margin = Padding.Empty,
                Padding = new Padding(S(2), S(6), S(10), S(6)), Font = new Font(FontName, 9F, FontStyle.Underline) };
            settings.FlatAppearance.BorderSize = 0; settings.FlatAppearance.MouseOverBackColor = Color.FromArgb(16, 38, 62);
            settings.Click += delegate { if (!preview) Process.Start(new ProcessStartInfo("explorer.exe", "ms-settings:network-wifi") { UseShellExecute = true }); };
            footer.Controls.Add(settings, 0, 1);
            StyleButton(stop, "停止監控", false); stop.Anchor = AnchorStyles.Right; stop.Margin = new Padding(S(12), 0, 0, 0); footer.Controls.Add(stop, 1, 1);
            StyleButton(start, "儲存並啟用", true); start.Anchor = AnchorStyles.Right; start.Margin = new Padding(S(12), 0, 0, 0); footer.Controls.Add(start, 2, 1);
            root.Controls.Add(footer, 0, 4);
            StyleDark();
            UpdateWifiControls();
        }

        // Dark "console" styling for inputs and the adapter list (owner-drawn so the header is dark too).
        void StyleDark()
        {
            Color field = Color.FromArgb(9, 14, 26), card = Color.FromArgb(16, 24, 41), sel = Color.FromArgb(12, 62, 80);
            networks.FlatStyle = FlatStyle.Flat; networks.BackColor = field; networks.ForeColor = ink;
            networks.DrawMode = DrawMode.OwnerDrawFixed; networks.ItemHeight = S(22);
            networks.DrawItem += delegate(object s, DrawItemEventArgs e) {
                if (e.Index < 0) return;
                bool hot = (e.State & DrawItemState.Selected) != 0 && (e.State & DrawItemState.ComboBoxEdit) == 0;
                using (SolidBrush b = new SolidBrush(hot ? sel : field)) e.Graphics.FillRectangle(b, e.Bounds);
                TextRenderer.DrawText(e.Graphics, networks.GetItemText(networks.Items[e.Index]), networks.Font,
                    new Rectangle(e.Bounds.X + S(4), e.Bounds.Y, e.Bounds.Width - S(4), e.Bounds.Height), networks.Enabled ? ink : muted,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            };
            password.BackColor = field; password.ForeColor = ink; password.BorderStyle = BorderStyle.FixedSingle;
            foreach (CheckBox box in new[] { reconnect, autoConnect, showPassword, replacePassword }) { box.FlatStyle = FlatStyle.Flat; box.ForeColor = ink; box.FlatAppearance.BorderColor = Color.FromArgb(52, 74, 112); }
            adapters.BackColor = field; adapters.ForeColor = ink; adapters.BorderStyle = BorderStyle.FixedSingle;
            adapters.OwnerDraw = true;
            adapters.DrawColumnHeader += delegate(object s, DrawListViewColumnHeaderEventArgs e) {
                using (SolidBrush b = new SolidBrush(card)) e.Graphics.FillRectangle(b, e.Bounds);
                using (Pen p = new Pen(line)) e.Graphics.DrawLine(p, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
                TextRenderer.DrawText(e.Graphics, e.Header.Text, adapters.Font, new Rectangle(e.Bounds.X + S(6), e.Bounds.Y, e.Bounds.Width - S(6), e.Bounds.Height), muted,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            };
            adapters.DrawItem += delegate(object s, DrawListViewItemEventArgs e) { };
            adapters.DrawSubItem += delegate(object s, DrawListViewSubItemEventArgs e) {
                Rectangle row = new Rectangle(e.Bounds.X, e.Bounds.Y, e.Bounds.Width, e.Bounds.Height);
                using (SolidBrush b = new SolidBrush(e.Item.Selected ? sel : field)) e.Graphics.FillRectangle(b, row);
                if (e.Item.Selected && e.ColumnIndex == 0) using (SolidBrush a = new SolidBrush(blue)) e.Graphics.FillRectangle(a, row.X, row.Y, 3, row.Height);
                Color text = e.ColumnIndex == 0 ? ink : muted;
                if (e.ColumnIndex == 2) text = e.SubItem.Text == "已連線" ? Color.FromArgb(74, 222, 128) : muted;
                TextRenderer.DrawText(e.Graphics, e.SubItem.Text, adapters.Font, new Rectangle(row.X + S(6), row.Y, row.Width - S(6), row.Height), text,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            };
        }

        Card BuildAdapterCard()
        {
            Card card = new Card(new Padding(S(20), S(16), S(20), S(16)), Color.Empty);
            card.ColumnCount = 2; card.RowCount = 4;
            card.ColumnStyles.Clear();
            card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            card.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            card.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            card.Controls.Add(Text1("01  選擇網卡", 13F, ink, true), 0, 0);
            StyleButton(refreshAdapters, "重新整理", false); refreshAdapters.Anchor = AnchorStyles.Right; card.Controls.Add(refreshAdapters, 1, 0);
            Label hint = Para("所有網卡都會列出，包含 VPN 與虛擬網卡。", 9F, muted, false); hint.Margin = new Padding(0, S(6), 0, S(10));
            card.Controls.Add(hint, 0, 1); card.SetColumnSpan(hint, 2);
            adapters.Dock = DockStyle.Fill; adapters.Margin = Padding.Empty; adapters.MinimumSize = new Size(0, S(90));
            adapters.View = View.Details; adapters.FullRowSelect = true; adapters.HideSelection = false; adapters.MultiSelect = false;
            adapters.BorderStyle = BorderStyle.FixedSingle; adapters.HeaderStyle = ColumnHeaderStyle.Nonclickable;
            adapters.ShowItemToolTips = true;
            adapters.Columns.Add("網卡", S(160)); adapters.Columns.Add("類型", S(90)); adapters.Columns.Add("狀態", S(90));
            card.Controls.Add(adapters, 0, 2); card.SetColumnSpan(adapters, 2);
            adapterInfo.Dock = DockStyle.Fill; adapterInfo.ForeColor = muted; adapterInfo.Margin = new Padding(0, S(10), 0, 0);
            adapterInfo.Text = "選擇實際用來上網的網卡。";
            card.Controls.Add(adapterInfo, 0, 3); card.SetColumnSpan(adapterInfo, 2);
            return card;
        }

        Card BuildWifiCard()
        {
            Card card = new Card(new Padding(S(20), S(16), S(20), S(16)), Color.Empty);
            card.ColumnCount = 2; card.RowCount = 10;
            card.ColumnStyles.Clear();
            card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            card.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            for (int i = 0; i < 9; i++) card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            card.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            Label title = Text1("02  Wi-Fi 重連", 13F, ink, true); title.Margin = new Padding(0, 0, 0, S(10));
            card.Controls.Add(title, 0, 0); card.SetColumnSpan(title, 2);

            reconnect.Text = "斷網修復後，重新連接指定 Wi-Fi"; reconnect.Checked = true; reconnect.Anchor = AnchorStyles.Left; reconnect.Margin = new Padding(0, 0, 0, S(10));
            card.Controls.Add(reconnect, 0, 1); card.SetColumnSpan(reconnect, 2);

            Label networkLabel = Text1("Wi-Fi 網路", 9.5F, ink, true); networkLabel.Margin = new Padding(0, 0, 0, S(4));
            card.Controls.Add(networkLabel, 0, 2); card.SetColumnSpan(networkLabel, 2);
            networks.DropDownStyle = ComboBoxStyle.DropDownList; networks.Dock = DockStyle.Fill; networks.Margin = new Padding(0, 0, S(10), 0);
            card.Controls.Add(networks, 0, 3);
            StyleButton(refreshWifi, "重新掃描", false); refreshWifi.Anchor = AnchorStyles.Right; card.Controls.Add(refreshWifi, 1, 3);

            wifiInfo.Dock = DockStyle.Fill; wifiInfo.ForeColor = muted; wifiInfo.Margin = new Padding(0, S(10), 0, S(10));
            card.Controls.Add(wifiInfo, 0, 4); card.SetColumnSpan(wifiInfo, 2);

            passwordLabel.Text = "Wi-Fi 密碼"; passwordLabel.AutoSize = true; passwordLabel.Anchor = AnchorStyles.Left; passwordLabel.Margin = new Padding(0, 0, 0, S(4));
            passwordLabel.Font = new Font(Font, FontStyle.Bold);
            card.Controls.Add(passwordLabel, 0, 5);
            replacePassword.Text = "更新已儲存的密碼"; replacePassword.Anchor = AnchorStyles.Right; replacePassword.Margin = Padding.Empty;
            card.Controls.Add(replacePassword, 1, 5);
            password.UseSystemPasswordChar = true; password.MaxLength = 64; password.Dock = DockStyle.Fill; password.Margin = new Padding(0, 0, S(10), 0);
            card.Controls.Add(password, 0, 6);
            showPassword.Text = "顯示密碼"; showPassword.Anchor = AnchorStyles.Right; showPassword.Margin = Padding.Empty;
            card.Controls.Add(showPassword, 1, 6);

            autoConnect.Text = "使用 Windows 自動連線"; autoConnect.Checked = true; autoConnect.AutoCheck = false; autoConnect.Anchor = AnchorStyles.Left; autoConnect.Margin = new Padding(0, S(10), 0, 0);
            card.Controls.Add(autoConnect, 0, 7); card.SetColumnSpan(autoConnect, 2);

            Label note = Para("密碼由 Windows 保存，不會寫入本應用的設定或紀錄。", 9F, muted, false); note.Margin = new Padding(0, S(12), 0, 0);
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
            passwordLabel.Visible = password.Visible = showPassword.Visible = needsPassword;
            password.Enabled = showPassword.Enabled = needsPassword && !busy;
            autoConnect.Visible = wireless && reconnect.Checked;
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
            string enteredPassword = password.Visible ? password.Text : "";
            busy = true; networkRequest++; adapters.Enabled = false; refreshAdapters.Enabled = false; stop.Enabled = false; UpdateWifiControls(); start.Text = "正在啟用…";
            ShowNotice("正在儲存設定並啟用背景監控…", false);
            try
            {
                AppConfig config = new AppConfig { AdapterId = adapter.Id, AdapterName = adapter.Name, ReconnectWifi = useWifi };
                await Task.Run(delegate {
                    if (useWifi)
                    {
                        config.WifiProfileName = NetworkManager.ConfigureWifi(new Guid(adapter.Id), wifi, enteredPassword);
                        NetworkManager.Connect(new Guid(adapter.Id), config.WifiProfileName);
                        if (!NetworkManager.WaitForConnection(new Guid(adapter.Id), config.WifiProfileName, 30000))
                            throw new InvalidOperationException("Wi-Fi 尚未成功連線，監控未切換。請確認訊號與密碼；若密碼錯誤，請重新掃描、選擇該 Wi-Fi，勾選『更新已儲存的密碼』後再試。" );
                    }
                    AppInstaller.Install(config);
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
            try { status.Text = "●  " + AppInstaller.GetStatus(); }
            catch { status.Text = "●  無法讀取監控狀態"; }
        }

        void ShowNotice(string message, bool error) { notice.Text = message; notice.ForeColor = error ? Color.FromArgb(255, 128, 128) : muted; }

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
            status.Text = "●  尚未啟用 · 完成設定後即可開始保護";
        }

        // White card with a hairline border and an optional accent bar; lays its children out as rows.
        sealed class Card : TableLayoutPanel
        {
            readonly Color accent;
            public Card(Padding padding, Color accentColor)
            {
                accent = accentColor; BackColor = Color.FromArgb(16, 24, 41); DoubleBuffered = true;
                Dock = DockStyle.Fill; ColumnCount = 1; Padding = padding; Margin = Padding.Empty;
                if (!accentColor.IsEmpty) Padding = new Padding(padding.Left + 4, padding.Top, padding.Right, padding.Bottom);
            }
            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                using (Pen p = new Pen(Color.FromArgb(34, 52, 82))) e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
                if (!accent.IsEmpty) using (SolidBrush b = new SolidBrush(accent)) e.Graphics.FillRectangle(b, 0, 0, 4, Height);
            }
        }

        // CheckBox that never wraps its text inside a table cell (a narrow proposed width would clip CJK text).
        sealed class PlainCheckBox : CheckBox
        {
            public PlainCheckBox() { AutoSize = true; }
            public override Size GetPreferredSize(Size proposedSize) { return base.GetPreferredSize(Size.Empty); }
        }

        // Label whose height follows its wrapped text at the width the table cell offers, so long messages
        // push content down instead of overlapping it. It reports a tiny width so it never forces a cell wider.
        sealed class WrapLabel : Label
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
}
