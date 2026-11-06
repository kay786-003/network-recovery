using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace NetworkRecovery
{
    public sealed class MonitorToolsForm : Form
    {
        private readonly bool demo;
        private bool busy;
        public bool IsBusy { get { return busy; } }
        private readonly Label status = new WrapLabel();
        private readonly TableLayoutPanel actions = new TableLayoutPanel();
        public MonitorToolsForm(bool demoMode)
        {
            SuspendLayout();
            demo = demoMode;
            Text = "監控工具";
            Icon = AppIcons.Application;
            Font = Theme.Font(9F, false);
            BackColor = Theme.Background; ForeColor = Theme.Ink;
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(Theme.S(560), Theme.S(520));
            MinimumSize = new Size(Theme.S(360), Theme.S(300));
            AutoScroll = true;
            // Docked to the top and auto-sized: narrow windows reflow text and only then scroll vertically.
            TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1, Padding = new Padding(Theme.S(22), Theme.S(18), Theme.S(22), Theme.S(18)) };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            Label title = new Label { Text = "監控工具", AutoSize = true, Font = Theme.Font(13F, true), ForeColor = Theme.Ink, Margin = new Padding(0, 0, 0, Theme.S(6)) };
            layout.Controls.Add(title);
            status.ForeColor = Theme.Muted; status.Dock = DockStyle.Fill; status.Margin = new Padding(0, 0, 0, Theme.S(14));
            layout.Controls.Add(status);
            actions.AutoSize = true; actions.AutoSizeMode = AutoSizeMode.GrowAndShrink; actions.ColumnCount = 1; actions.Dock = DockStyle.Fill; actions.Margin = Padding.Empty;
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.Controls.Add(actions);
            Add("備份", 0, Backup);
            Add("還原備份", 0, Restore);
            Add("一鍵修復並重設", 2, Repair);
            Add("移除備份", 0, async delegate {
                if (Confirm("移除後將無法用此備份還原。確定移除？"))
                    await Execute(delegate { using (ActionLease lease = new ActionLease(1000)) ManualRepair.DiscardBackup(); });
            });
            Card warning = new Card(new Padding(Theme.S(18), Theme.S(12), Theme.S(18), Theme.S(12)), Theme.Warning) { AutoSize = true, Margin = new Padding(0, Theme.S(6), 0, 0) };
            WrapLabel note = new WrapLabel { Dock = DockStyle.Fill, ForeColor = Theme.Muted, Margin = Padding.Empty, Text =
                "備份只留在這台電腦，不會覆寫既有備份。\n修復會短暫斷線，並重設 Proxy、Winsock 與 TCP/IP，可能影響 VPN、固定 IP 與公司代理。完成後請自行重新啟動電腦。" };
            warning.Controls.Add(note);
            layout.Controls.Add(warning);
            Controls.Add(layout);
            Shown += delegate { status.Text = demo ? "網路設定備份與修復（示範）" : "備份與還原只處理這台電腦的網路設定。"; };
            FormClosing += delegate(object sender, FormClosingEventArgs e) { if (busy) e.Cancel = true; };
            ResumeLayout(true);
            status.Text = demo ? "網路設定備份與修復（示範）" : "備份與還原只處理這台電腦的網路設定。";
        }
        protected override void OnLoad(EventArgs e)
        {
            // Never open larger than the usable desktop; the form scrolls instead.
            Rectangle area = Screen.FromControl(this).WorkingArea;
            Size = new Size(Math.Min(Width, area.Width), Math.Min(Height, area.Height));
            Location = new Point(area.Left + (area.Width - Width) / 2, area.Top + (area.Height - Height) / 2);
            base.OnLoad(e);
        }        // kind: 0 normal, 2 caution (amber) for the action that resets network settings.
        private void Add(string text, int kind, Func<Task> action)
        {
            Button button = new Button { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, Theme.S(10)), TextAlign = ContentAlignment.MiddleLeft };
            Theme.StyleButton(button, text, kind);
            button.AutoSizeMode = AutoSizeMode.GrowOnly; button.MinimumSize = new Size(0, Theme.S(46)); button.Margin = new Padding(0, 0, 0, Theme.S(10));
            button.Padding = new Padding(Theme.S(18), Theme.S(4), Theme.S(18), Theme.S(4));
            button.Click += async delegate { if (!demo && !busy) await action(); };
            actions.Controls.Add(button);
        }
        private bool Confirm(string message)
        { return MessageBox.Show(this, message, Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes; }
        private async Task<bool> Execute(Action action)
        {
            busy = true; actions.Enabled = false; status.Text = "處理中，請稍候…";
            try { await Task.Run(action); status.Text = "操作完成。"; return true; }
            catch (Exception ex) { status.Text = "操作未完成。"; MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); return false; }
            finally { busy = false; actions.Enabled = true; }
        }
        private async Task Backup()
        {
            await Execute(delegate {
                using (ActionLease lease = new ActionLease(1000)) new ManualRepair(null).Backup();
            });
        }
        private async Task Repair()
        {
            if (!Confirm("將清除 DNS、修復所選網卡，並重設目前登入使用者 Proxy、WinHTTP、Winsock 及 TCP/IP。可能中斷遠端連線、影響固定 IP、VPN 與公司代理。會先驗證既有備份；若尚無備份則先建立，失敗時嘗試還原。完成後請自行重啟。確定執行？")) return;
            bool completed = await Execute(delegate {
                using (ActionLease lease = new ActionLease(1000))
                {
                    AppConfig config = AppInstaller.LoadConfig();
                    Guid id;
                    if (config == null || !Guid.TryParse(config.AdapterId, out id)) throw new InvalidOperationException("請先儲存要修復的網卡。");
                    try { RepairPlan.Run(new ManualRepair(config), true); }
                    finally { ResetObservation(); }
                }
            });
            if (completed) ShowRestartReminder("修復與重設已完成");
        }
        private async Task Restore()
        {
            if (!Confirm("以本機備份還原 Proxy、Winsock 與 IP 設定，會中斷連線並可能需要重啟。確定繼續？")) return;
            bool completed = await Execute(delegate {
                using (ActionLease lease = new ActionLease(1000))
                {
                    RecoveryEngine.WriteRequiredLog("手動還原開始。");
                    try { new ManualRepair(null).Restore(); RecoveryEngine.WriteRequiredLog("手動還原完成；需重新啟動生效。"); }
                    catch { RecoveryEngine.WriteLog("手動還原失敗，備份保留。"); throw; }
                    finally { ResetObservation(); }
                }
            });
            if (completed) ShowRestartReminder("備份還原已完成");
        }
        private static void ResetObservation()
        {
            PauseState state = MonitorControl.Read();
            state.ObservationRevision = Guid.NewGuid().ToString("N");
            StateFile.Write(AppPaths.ControlFile, state);
        }
        private void ShowRestartReminder(string operation)
        {
            status.Text = operation + "；請自行重新啟動電腦。";
            MessageBox.Show(this, operation + "，需要重新啟動電腦才能生效。請先儲存工作，再自行重新啟動。\n\n按「確定」只會關閉此提醒。",
                "需要重新啟動", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
