using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace NetworkRecovery
{
    internal sealed class RunningProcess
    {
        public string Name;
        public int Count;
        public string Description;
    }

    // Live list of running processes, like Task Manager's Details tab. Only bare names leave this window, never paths.
    internal sealed class ProcessPickerForm : Form
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, string lParam);

        readonly bool demo;
        readonly HashSet<string> selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, string> descriptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        readonly ListView list = new ListView();
        readonly TextBox search = new TextBox();
        readonly Label summary = new WrapLabel();
        readonly Button refresh = new ThemedButton { Glyph = '' };
        readonly Button ok = new ThemedButton { Glyph = '' };
        readonly Button cancel = new ThemedButton();
        readonly Timer timer = new Timer();
        readonly TableLayoutPanel layout;
        List<RunningProcess> snapshot = new List<RunningProcess>();
        string signature = "";
        bool loading, refreshing;

        internal ProcessPickerForm(bool demoMode, IEnumerable<string> current)
        {
            SuspendLayout();
            demo = demoMode;
            foreach (string name in CountryProcessGuard.SanitizeNames(current)) selected.Add(name);
            Text = "選擇要結束的程序";
            Icon = AppIcons.Application;
            Font = Theme.Font(9.5F, false);
            Theme.ApplyWindow(this);
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(Theme.S(640), Theme.S(620));
            MinimumSize = new Size(Theme.S(320), Theme.S(260));
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false; MinimizeBox = false;

            // Fills the window but never shrinks below a usable height; smaller windows scroll instead of overlapping.
            AutoScroll = true;
            layout = new TableLayoutPanel { Location = Point.Empty, ColumnCount = 1, RowCount = 6,
                Padding = new Padding(Theme.S(24), Theme.S(20), Theme.S(24), Theme.S(20)) };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            for (int i = 0; i < 6; i++) layout.RowStyles.Add(new RowStyle(i == 3 ? SizeType.Percent : SizeType.AutoSize, 100F));

            Label title = new Label { Text = "選擇要結束的程序", AutoSize = true, Font = Theme.Font(14F, true), ForeColor = Theme.Ink, Margin = new Padding(0, 0, 0, Theme.S(4)) };
            layout.Controls.Add(title, 0, 0);
            WrapLabel note = new WrapLabel { Dock = DockStyle.Fill, ForeColor = Theme.Muted, Margin = new Padding(0, 0, 0, Theme.S(14)),
                Text = "即時列出目前執行中的程序（同工作管理員「詳細資料」）。只記錄程序名稱，不記錄路徑；Windows 核心程序與本工具不會列出。" };
            layout.Controls.Add(note, 0, 1);

            TableLayoutPanel bar = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, RowCount = 1, Margin = new Padding(0, 0, 0, Theme.S(10)) };
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F)); bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            search.Font = Theme.Font(10F, false);
            InputFrame frame = new InputFrame(search) { Dock = DockStyle.Fill, Margin = new Padding(0, 0, Theme.S(10), 0) };
            bar.Controls.Add(frame, 0, 0);
            Theme.StyleButton(refresh, "重新整理", 0); refresh.Anchor = AnchorStyles.Right;
            bar.Controls.Add(refresh, 1, 0);
            layout.Controls.Add(bar, 0, 2);

            list.Dock = DockStyle.Fill; list.Margin = Padding.Empty; list.MinimumSize = new Size(0, Theme.S(140));
            list.View = View.Details; list.CheckBoxes = true; list.FullRowSelect = true; list.MultiSelect = false; list.HideSelection = false;
            list.HeaderStyle = ColumnHeaderStyle.Nonclickable; list.BorderStyle = BorderStyle.None;
            list.BackColor = Theme.Surface; list.ForeColor = Theme.Ink; list.Font = Theme.Font(9.5F, false);
            list.Columns.Add("名稱", Theme.S(200)); list.Columns.Add("執行中", Theme.S(80)); list.Columns.Add("描述", Theme.S(240));
            Theme.ApplyScrollTheme(list, "DarkMode_Explorer");
            layout.Controls.Add(list, 0, 3);

            summary.Dock = DockStyle.Fill; summary.ForeColor = Theme.Muted; summary.Margin = new Padding(0, Theme.S(12), 0, Theme.S(12));
            layout.Controls.Add(summary, 0, 4);

            TableLayoutPanel footer = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            Theme.StyleButton(cancel, "取消", 0); cancel.Anchor = AnchorStyles.Right;
            Theme.StyleButton(ok, "確定", 1); ok.Anchor = AnchorStyles.Right; ok.Margin = new Padding(Theme.S(10), 0, 0, 0);
            footer.Controls.Add(cancel, 1, 0); footer.Controls.Add(ok, 2, 0);
            layout.Controls.Add(footer, 0, 5);
            Controls.Add(layout);
            CancelButton = cancel;
            FitLayout();
            ResumeLayout(true);

            list.SizeChanged += delegate { FitColumns(); };
            summary.TextChanged += delegate { FitLayout(); };
            list.ItemChecked += delegate(object sender, ItemCheckedEventArgs e) { if (!loading) Toggle(e.Item); };
            search.TextChanged += delegate { Render(); };
            search.HandleCreated += delegate { SendMessage(search.Handle, 0x1501, (IntPtr)1, "搜尋名稱或描述"); };
            refresh.Click += async delegate { await RefreshList(); };
            ok.Click += delegate { DialogResult = DialogResult.OK; Close(); };
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            timer.Interval = 3000;
            timer.Tick += async delegate { await RefreshList(); };
            Shown += async delegate { await RefreshList(); if (!demo && !IsDisposed) timer.Start(); search.Focus(); };
            FormClosed += delegate { timer.Stop(); timer.Dispose(); };
            UpdateSummary();
        }

        internal string[] SelectedNames
        { get { return CountryProcessGuard.SanitizeNames(selected.OrderBy(n => n, StringComparer.OrdinalIgnoreCase)); } }

        protected override void OnLoad(EventArgs e)
        {
            // Never open larger than the usable desktop.
            Rectangle area = Screen.FromControl(this).WorkingArea;
            Size = new Size(Math.Min(Width, area.Width), Math.Min(Height, area.Height));
            base.OnLoad(e);
        }

        protected override void OnClientSizeChanged(EventArgs e)
        {
            base.OnClientSizeChanged(e);
            FitLayout();
        }

        void FitLayout()
        {
            if (layout == null) return;
            Point scroll = AutoScrollPosition;
            int width = Math.Max(Theme.S(320), ClientSize.Width);
            // Minimum height comes from the measured text, so other fonts or scales still cannot squeeze rows together.
            int inner = width - layout.Padding.Horizontal, need = layout.Padding.Vertical + Theme.S(8);
            foreach (Control child in layout.Controls)
                need += child.Margin.Vertical + (child == list ? list.MinimumSize.Height :
                    child.GetPreferredSize(new Size(Math.Max(1, inner - child.Margin.Horizontal), 0)).Height);
            int height = Math.Max(need, ClientSize.Height);
            layout.SetBounds(scroll.X, scroll.Y, width, height);
        }

        void FitColumns()
        {
            int total = list.ClientSize.Width;
            if (total <= 0 || list.Columns.Count < 3) return;
            int first = (int)(total * 0.38), second = (int)(total * 0.16);
            list.Columns[0].Width = first; list.Columns[1].Width = second;
            list.Columns[2].Width = Math.Max(Theme.S(40), total - first - second);
        }

        void Toggle(ListViewItem item)
        {
            string name = (string)item.Tag;
            if (!item.Checked) selected.Remove(name);
            else if (selected.Count >= CountryProcessGuard.MaxNames && !selected.Contains(name))
            {
                loading = true; item.Checked = false; loading = false;
                summary.Text = "最多只能選擇 " + CountryProcessGuard.MaxNames + " 個程序。"; summary.ForeColor = Theme.Danger;
                return;
            }
            else selected.Add(name);
            UpdateSummary();
        }

        void UpdateSummary()
        {
            summary.ForeColor = Theme.Muted;
            if (selected.Count == 0) { summary.Text = "尚未選擇程序。"; return; }
            var names = selected.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).Select(n => n + ".exe").ToList();
            summary.Text = "已選擇 " + names.Count + " 個：" + String.Join("、", names.Take(10)) + (names.Count > 10 ? "…" : "");
            FitLayout();
        }

        async Task RefreshList()
        {
            if (refreshing || IsDisposed) return;
            refreshing = true; refresh.Enabled = false;
            try
            {
                List<RunningProcess> items = demo ? DemoProcesses() : await Task.Run(delegate { return Snapshot(descriptions); });
                if (IsDisposed) return;
                string next = String.Join("|", items.Select(p => p.Name + ":" + p.Count));
                snapshot = items;
                if (next != signature) { signature = next; Render(); }
            }
            catch (Exception ex) { if (!IsDisposed) { summary.Text = "無法讀取程序清單：" + ex.Message; summary.ForeColor = Theme.Danger; } }
            finally { refreshing = false; if (!IsDisposed) refresh.Enabled = true; }
        }

        // Rebuilds the visible rows, keeping checked names that are not running so they can still be unchecked.
        void Render()
        {
            string filter = search.Text.Trim();
            var rows = new Dictionary<string, RunningProcess>(StringComparer.OrdinalIgnoreCase);
            foreach (RunningProcess process in snapshot) rows[process.Name] = process;
            foreach (string name in selected) if (!rows.ContainsKey(name)) rows[name] = new RunningProcess { Name = name, Description = "" };
            var visible = rows.Values.Where(r => filter.Length == 0 || r.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                (r.Description ?? "").IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();
            string top = list.TopItem == null ? null : list.TopItem.Name;
            string focused = list.FocusedItem == null ? null : list.FocusedItem.Name;
            loading = true;
            list.BeginUpdate();
            try
            {
                list.Items.Clear();
                foreach (RunningProcess r in visible)
                {
                    ListViewItem item = new ListViewItem(r.Name + ".exe") { Name = r.Name, Tag = r.Name, Checked = selected.Contains(r.Name) };
                    item.SubItems.Add(r.Count == 0 ? "未執行" : r.Count.ToString());
                    item.SubItems.Add(r.Description ?? "");
                    if (r.Count == 0) item.ForeColor = Theme.Muted;
                    list.Items.Add(item);
                }
            }
            finally { list.EndUpdate(); loading = false; }
            if (top != null && list.Items.ContainsKey(top)) try { list.TopItem = list.Items[top]; } catch { }
            if (focused != null && list.Items.ContainsKey(focused)) list.Items[focused].Focused = true;
            UpdateSummary();
        }

        static List<RunningProcess> Snapshot(Dictionary<string, string> cache)
        {
            var groups = new Dictionary<string, RunningProcess>(StringComparer.OrdinalIgnoreCase);
            foreach (Process process in Process.GetProcesses())
                using (process)
                {
                    string name;
                    try { if (process.Id <= 4) continue; name = process.ProcessName; }
                    catch { continue; }
                    if (CountryProcessGuard.NormalizeName(name) != name || CountryProcessGuard.IsProtectedName(name)) continue;
                    RunningProcess entry;
                    if (!groups.TryGetValue(name, out entry)) { entry = new RunningProcess { Name = name }; groups[name] = entry; }
                    entry.Count++;
                    if (entry.Description == null)
                    {
                        string description;
                        if (!cache.TryGetValue(name, out description)) { description = Describe(process); cache[name] = description; }
                        entry.Description = description;
                    }
                }
            return groups.Values.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        // The module path is read only to show the file description and is discarded immediately.
        static string Describe(Process process)
        {
            try { return process.MainModule.FileVersionInfo.FileDescription ?? ""; }
            catch { return ""; }
        }

        static List<RunningProcess> DemoProcesses()
        {
            return new List<RunningProcess> {
                new RunningProcess { Name = "chrome", Count = 14, Description = "Google Chrome" },
                new RunningProcess { Name = "claude", Count = 3, Description = "Claude" },
                new RunningProcess { Name = "Code", Count = 9, Description = "Visual Studio Code" },
                new RunningProcess { Name = "cowork-svc", Count = 1, Description = "" },
                new RunningProcess { Name = "explorer", Count = 1, Description = "Windows 檔案總管" },
                new RunningProcess { Name = "Telegram", Count = 1, Description = "Telegram Desktop" } };
        }
    }
}
