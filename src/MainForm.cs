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
        readonly Color ink = Color.FromArgb(24, 38, 56);
        readonly Color muted = Color.FromArgb(99, 114, 134);
        readonly Color blue = Color.FromArgb(37, 99, 235);
        readonly bool preview;
        readonly Panel content = new Panel();
        Size contentMinimum;
        readonly ListView adapters = new ListView();
        readonly ComboBox networks = new ComboBox();
        readonly CheckBox reconnect = new CheckBox();
        readonly CheckBox autoConnect = new CheckBox();
        readonly CheckBox showPassword = new CheckBox();
        readonly CheckBox replacePassword = new CheckBox();
        readonly TextBox password = new TextBox();
        readonly Label passwordLabel = new Label();
        readonly Label wifiInfo = new Label();
        readonly Label adapterInfo = new Label();
        readonly Label status = new Label();
        readonly Label notice = new Label();
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
        MonitorToolsForm toolsWindow;

        public MainForm(bool previewMode)
        {
            SuspendLayout();
            preview = previewMode;
            Text = "Network Recovery · 連線守護";
            Font = new Font("Microsoft JhengHei UI", 9F);
            BackColor = Color.FromArgb(244, 247, 251);
            ForeColor = ink;
            ClientSize = new Size(1080, 790);
            MinimumSize = new Size(640, 480);
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScroll = true;
            content.Size = ClientSize;
            Controls.Add(content);
            content.SuspendLayout();
            BuildLayout();
            content.ResumeLayout(false);
            ResumeLayout(false);
            adapters.SelectedIndexChanged += delegate { if (!populating) AdapterChanged(); };
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
                if (busy || (toolsWindow != null && !toolsWindow.IsDisposed && toolsWindow.IsBusy)) { e.Cancel = true; ShowNotice("正在完成網路操作，請稍候再關閉視窗。", false); }
            };
        }

        protected override void OnLoad(EventArgs e)
        {
            // Preserve the scaled design canvas and scroll on smaller desktops.
            contentMinimum = content.Size;
            float dpiScale = CurrentAutoScaleDimensions.Width / 96F;
            adapters.Columns[0].Width = (int)(165 * dpiScale);
            adapters.Columns[1].Width = (int)(90 * dpiScale);
            adapters.Columns[2].Width = (int)(90 * dpiScale);
            Rectangle area = Screen.FromControl(this).WorkingArea;
            Size = new Size(Math.Min(Width, area.Width), Math.Min(Height, area.Height));
            Location = new Point(area.Left + (area.Width - Width) / 2, area.Top + (area.Height - Height) / 2);
            ResizeContent();
            base.OnLoad(e);
        }

        protected override void OnClientSizeChanged(EventArgs e)
        {
            base.OnClientSizeChanged(e);
            ResizeContent();
        }

        void ResizeContent()
        {
            if (contentMinimum.IsEmpty) return;
            content.Size = new Size(Math.Max(contentMinimum.Width, ClientSize.Width),
                Math.Max(contentMinimum.Height, ClientSize.Height));
        }

        Label TextLabel(Control parent, string text, int x, int y, int w, int h, float size, Color color, bool bold)
        {
            Label label = new Label { Text = text, Location = new Point(x, y), Size = new Size(w, h), ForeColor = color,
                Font = new Font("Microsoft JhengHei UI", size, bold ? FontStyle.Bold : FontStyle.Regular) };
            parent.Controls.Add(label); return label;
        }

        void StyleButton(Button button, string text, bool primary)
        {
            button.Text = text; button.FlatStyle = FlatStyle.Flat; button.Cursor = Cursors.Hand;
            button.FlatAppearance.BorderSize = primary ? 0 : 1;
            button.FlatAppearance.BorderColor = Color.FromArgb(213, 222, 235);
            button.BackColor = primary ? blue : Color.White;
            button.ForeColor = primary ? Color.White : ink;
            button.Font = new Font("Microsoft JhengHei UI", 9.5F, FontStyle.Bold);
            button.UseVisualStyleBackColor = false;
        }

        void BuildLayout()
        {
            Panel brand = new Panel { BackColor = blue, Location = new Point(28, 27), Size = new Size(38, 38) };
            brand.Paint += delegate(object sender, PaintEventArgs e) {
                e.Graphics.ScaleTransform(brand.ClientSize.Width / 38F, brand.ClientSize.Height / 38F);
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (Pen p = new Pen(Color.White, 2F)) {
                    e.Graphics.DrawArc(p, 7, 7, 23, 23, 214, 112);
                    e.Graphics.DrawArc(p, 12, 14, 13, 13, 210, 120);
                }
                e.Graphics.FillEllipse(Brushes.White, 17, 25, 4, 4);
            };
            content.Controls.Add(brand);
            TextLabel(content, "NETWORK RECOVERY", 78, 26, 330, 25, 12F, ink, true);
            TextLabel(content, "連線守護", 78, 52, 300, 20, 9F, muted, false);
            Label tag = TextLabel(content, "管理員模式", 915, 36, 128, 28, 9F, blue, true);
            tag.TextAlign = ContentAlignment.MiddleCenter; tag.BackColor = Color.FromArgb(228, 237, 255);
            tag.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            TextLabel(content, "讓連線自行恢復。", 28, 88, 800, 39, 22F, ink, true);
            TextLabel(content, "選擇要保護的網卡，設定斷網後的 Wi-Fi 重連方式。", 30, 136, 970, 26, 10F, muted, false);

            Card left = new Card { Location = new Point(28, 181), Size = new Size(420, 397), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Bottom };
            Card right = new Card { Location = new Point(464, 181), Size = new Size(588, 397), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom };
            content.Controls.Add(left); content.Controls.Add(right);
            TextLabel(left, "01  選擇網卡", 20, 20, 260, 30, 13F, ink, true);
            StyleButton(refreshAdapters, "重新整理", false); refreshAdapters.SetBounds(307, 18, 92, 33); left.Controls.Add(refreshAdapters);
            TextLabel(left, "所有網卡都會列出，包含 VPN 與虛擬網卡。", 20, 57, 380, 25, 9F, muted, false);
            adapters.SetBounds(20, 91, 379, 222);
            adapters.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            adapters.View = View.Details; adapters.FullRowSelect = true; adapters.HideSelection = false; adapters.MultiSelect = false;
            adapters.BorderStyle = BorderStyle.FixedSingle; adapters.HeaderStyle = ColumnHeaderStyle.Nonclickable;
            adapters.ShowItemToolTips = true;
            adapters.Columns.Add("網卡", 165); adapters.Columns.Add("類型", 90); adapters.Columns.Add("狀態", 90);
            left.Controls.Add(adapters);
            adapterInfo.SetBounds(20, 327, 379, 49); adapterInfo.ForeColor = muted; adapterInfo.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            adapterInfo.Text = "選擇實際用來上網的網卡。"; left.Controls.Add(adapterInfo);

            TextLabel(right, "02  Wi-Fi 重連", 20, 20, 420, 30, 13F, ink, true);
            reconnect.Text = "斷網修復後，重新連接指定 Wi-Fi"; reconnect.SetBounds(21, 63, 540, 29); reconnect.Checked = true; right.Controls.Add(reconnect);
            TextLabel(right, "Wi-Fi 網路", 22, 108, 300, 22, 9.5F, ink, true);
            networks.SetBounds(22, 136, 414, 32); networks.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right; networks.DropDownStyle = ComboBoxStyle.DropDownList; right.Controls.Add(networks);
            StyleButton(refreshWifi, "重新掃描", false); refreshWifi.SetBounds(450, 133, 115, 35); refreshWifi.Anchor = AnchorStyles.Top | AnchorStyles.Right; right.Controls.Add(refreshWifi);
            wifiInfo.SetBounds(22, 182, 542, 40); wifiInfo.ForeColor = muted; wifiInfo.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right; right.Controls.Add(wifiInfo);
            passwordLabel.Text = "Wi-Fi 密碼"; passwordLabel.SetBounds(22, 226, 300, 22); passwordLabel.Font = new Font(Font, FontStyle.Bold); right.Controls.Add(passwordLabel);
            replacePassword.Text = "更新已儲存的密碼"; replacePassword.SetBounds(345, 220, 220, 28); replacePassword.Anchor = AnchorStyles.Top | AnchorStyles.Right; right.Controls.Add(replacePassword); replacePassword.BringToFront();
            password.SetBounds(22, 253, 414, 28); password.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right; password.UseSystemPasswordChar = true; password.MaxLength = 64; right.Controls.Add(password);
            showPassword.Text = "顯示密碼"; showPassword.SetBounds(450, 253, 115, 28); showPassword.Anchor = AnchorStyles.Top | AnchorStyles.Right; right.Controls.Add(showPassword);
            autoConnect.Text = "使用 Windows 自動連線"; autoConnect.Checked = true; autoConnect.Enabled = false; autoConnect.SetBounds(22, 296, 500, 26); right.Controls.Add(autoConnect);
            TextLabel(right, "密碼由 Windows 保存，不會寫入本應用的設定或紀錄。", 22, 338, 540, 34, 9F, muted, false).Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;

            Card timing = new Card { Location = new Point(28, 595), Size = new Size(1024, 69), Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom };
            content.Controls.Add(timing);
            TextLabel(timing, "目前的保護流程", 20, 22, 175, 25, 10F, ink, true);
            TextLabel(timing, "斷網 3 分鐘 → 修復網卡 → 再等 60 秒 → 重啟電腦", 199, 15, 790, 24, 10F, blue, true);
            TextLabel(timing, "開機後給網路 5 分鐘恢復；持續無網將再次觸發修復。", 199, 40, 790, 21, 8.5F, muted, false);
            // Layout cells keep inherited-font scaling from making labels cover buttons.
            TableLayoutPanel footer = new TableLayoutPanel {
                Location = new Point(28, 675), Size = new Size(1024, 106),
                Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
                ColumnCount = 3, RowCount = 3, Margin = Padding.Empty, Padding = Padding.Empty
            };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 146F));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 218F));
            footer.RowStyles.Add(new RowStyle(SizeType.Percent, 27F));
            footer.RowStyles.Add(new RowStyle(SizeType.Percent, 46F));
            footer.RowStyles.Add(new RowStyle(SizeType.Percent, 27F));
            content.Controls.Add(footer);
            status.ForeColor = ink; status.Dock = DockStyle.Fill; status.Margin = Padding.Empty;
            footer.Controls.Add(status, 0, 0); footer.SetColumnSpan(status, 3);
            notice.ForeColor = muted; notice.Dock = DockStyle.Fill; notice.Margin = new Padding(0, 0, 12, 0);
            notice.Text = "啟用後會在背景持續運作。自動重啟會關閉程式，請定期儲存工作。";
            footer.Controls.Add(notice, 0, 1); footer.SetRowSpan(notice, 2);
            StyleButton(stop, "停止監控", false); stop.Dock = DockStyle.Fill; stop.Margin = new Padding(16, 0, 0, 0); footer.Controls.Add(stop, 1, 1);
            StyleButton(start, "儲存並啟用", true); start.Dock = DockStyle.Fill; start.Margin = new Padding(16, 0, 0, 0); footer.Controls.Add(start, 2, 1);
            Button settings = new Button { Text = "Windows Wi-Fi 設定", FlatStyle = FlatStyle.Flat, ForeColor = muted, Cursor = Cursors.Hand, Dock = DockStyle.Fill, Margin = Padding.Empty };
            settings.FlatAppearance.BorderSize = 0;
            settings.Click += delegate { if (!preview) Process.Start(new ProcessStartInfo("explorer.exe", "ms-settings:network-wifi") { UseShellExecute = true }); };
            footer.Controls.Add(settings, 2, 2);
            Button tools = new Button { Text = "監控工具", Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat, Margin = Padding.Empty };
            tools.FlatAppearance.BorderSize = 0;
            tools.Click += delegate {
                if (busy) return;
                if (toolsWindow == null || toolsWindow.IsDisposed) { toolsWindow = new MonitorToolsForm(preview); toolsWindow.Show(this); }
                else toolsWindow.Activate();
            };
            footer.Controls.Add(tools, 1, 2);
            UpdateWifiControls();
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
                AppConfig config = new AppConfig { AdapterId = adapter.Id, AdapterName = adapter.Name, ReconnectWifi = useWifi,
                    WifiSsidHex = useWifi ? wifi.SsidHex : "" };
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
            try { status.Text = "●  " + AppInstaller.GetStatus(); }
            catch { status.Text = "●  無法讀取監控狀態"; }
        }

        void ShowNotice(string message, bool error) { notice.Text = message; notice.ForeColor = error ? Color.FromArgb(180, 58, 48) : muted; }

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

        sealed class Card : Panel
        {
            public Card() { BackColor = Color.White; DoubleBuffered = true; }
            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                using (Pen p = new Pen(Color.FromArgb(225, 232, 242))) e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
            }
        }
    }
}
