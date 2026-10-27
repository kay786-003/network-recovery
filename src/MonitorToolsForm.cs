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
        private bool resizing;
        public bool IsBusy { get { return busy; } }
        private readonly Label status = new Label();
        private readonly FlowLayoutPanel actions = new FlowLayoutPanel();
        public MonitorToolsForm(bool demoMode)
        {
            SuspendLayout();
            demo = demoMode;
            Text = "監控工具";
            Font = new Font("Microsoft JhengHei UI", 10F);
            AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(560, 390); MinimumSize = new Size(440, 360);
            TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Padding = new Padding(16) };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 105));
            status.Dock = DockStyle.Fill; layout.Controls.Add(status, 0, 0);
            actions.Dock = DockStyle.Fill; actions.AutoScroll = true; actions.FlowDirection = FlowDirection.TopDown; actions.WrapContents = false;
            actions.ClientSizeChanged += delegate { ResizeButtons(); };
            layout.Controls.Add(actions, 0, 1); Controls.Add(layout);
            Add("備份", Backup);
            Add("還原備份", Restore);
            Add("一鍵修復並重設", Repair);
            Add("移除備份", async delegate {
                if (Confirm("移除後將無法用此備份還原。確定移除？"))
                    await Execute(delegate { using (ActionLease lease = new ActionLease(1000)) ManualRepair.DiscardBackup(); });
            });
            Label note = new Label { Dock = DockStyle.Fill, Text =
                "備份只留在這台電腦，不會覆寫既有備份。\n修復會短暫斷線，並重設 Proxy、Winsock 與 TCP/IP，可能影響 VPN、固定 IP 與公司代理。完成後請自行重新啟動電腦。", AutoEllipsis = true };
            layout.Controls.Add(note, 0, 2);
            Shown += delegate { ResizeButtons(); status.Text = demo ? "網路設定備份與修復（示範）" : "備份與還原只處理這台電腦的網路設定。"; };
            FormClosing += delegate(object sender, FormClosingEventArgs e) { if (busy) e.Cancel = true; };
            ResumeLayout(true);
        }
        private void ResizeButtons()
        {
            if (resizing) return;
            resizing = true;
            try
            {
                int width = Math.Max(150, actions.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 12);
                foreach (Control control in actions.Controls)
                {
                    Size text = TextRenderer.MeasureText(control.Text, control.Font, new Size(width - 24, 0), TextFormatFlags.WordBreak);
                    control.Size = new Size(width, Math.Max(38, text.Height + 20));
                }
            }
            finally { resizing = false; }
        }
        private void Add(string text, Func<Task> action)
        {
            Button button = new Button { Text = text, AutoSize = false, Size = new Size(350, 38), Margin = new Padding(0, 0, 0, 8) };
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
