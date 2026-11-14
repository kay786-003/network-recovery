using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace NetworkRecovery
{
    public sealed class TrayContext : ApplicationContext
    {
        private readonly NotifyIcon icon;
        private readonly Timer timer = new Timer();
        private bool updating;
        private bool disposed;
        private readonly ToolStripMenuItem[] pauses;
        private MonitorToolsForm toolsWindow;
        private Process settingsProcess;
        private readonly ToolStripMenuItem status = new ToolStripMenuItem("等待監控回報") { Enabled = false };
        public TrayContext()
        {
            ContextMenuStrip menu = new ContextMenuStrip { Renderer = new MenuRenderer(), Font = Theme.Font(9.5F, false), ShowCheckMargin = false, ShowImageMargin = true,
                ImageScalingSize = new Size(Theme.S(16), Theme.S(16)), Padding = new Padding(0, Theme.S(4), 0, Theme.S(4)) };
            int glyph = Theme.S(16);
            status.Tag = "header"; status.Font = Theme.Font(9.5F, true); status.Image = Theme.DotImage(Theme.Neutral, glyph);
            menu.Items.Add(status); menu.Items.Add(new ToolStripSeparator());
            pauses = TrayPauseMenu.Create(async delegate(int duration) { await Run(delegate { MonitorControl.TogglePause(duration); }); });
            menu.Items.AddRange(pauses);
            menu.Items.Add(new ToolStripSeparator());
            menu.Opening += delegate { RefreshPauseChecks(); };
            menu.Items.Add("監控工具", Theme.GlyphImage('', Theme.Ink, glyph), delegate { ShowTools(); });
            menu.Items.Add("開啟設定", Theme.GlyphImage('', Theme.Ink, glyph), delegate { OpenSettings(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("關閉系統匣（監控繼續）", Theme.GlyphImage('', Theme.Ink, glyph), delegate {
                if (toolsWindow != null && !toolsWindow.IsDisposed && toolsWindow.IsBusy)
                    MessageBox.Show("網路操作進行中，請完成後再關閉系統匣。", "Network Recovery");
                else ExitThread();
            });
            icon = new NotifyIcon { Icon = AppIcons.Application, Text = "Network Recovery", ContextMenuStrip = menu, Visible = true };
            icon.DoubleClick += delegate { OpenSettings(); };
            timer.Interval = 5000;
            timer.Tick += async delegate { await UpdateStatus(); };
            timer.Start();
        }
        private void OpenSettings()
        {
            try
            {
                if (SettingsWindowInstance.TryActivateExisting()) return;
                // Suppress repeated launches while the first child is starting.
                if (settingsProcess != null && !settingsProcess.HasExited) return;
                if (settingsProcess != null) settingsProcess.Dispose();
                settingsProcess = Process.Start(new ProcessStartInfo(Application.ExecutablePath) { UseShellExecute = true });
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Network Recovery", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }
        private void ShowTools()
        {
            if (toolsWindow == null || toolsWindow.IsDisposed) { toolsWindow = new MonitorToolsForm(false); toolsWindow.Show(); }
            else toolsWindow.Activate();
        }
        private async Task Run(Action action)
        {
            try { await Task.Run(action); if (!disposed) { RefreshPauseChecks(); await UpdateStatus(); } }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Network Recovery", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }
        private async Task UpdateStatus()
        {
            if (updating || disposed) return;
            updating = true;
            try
            {
                string text = await Task.Run<string>(delegate { return AppInstaller.GetStatus(); });
                if (disposed) return;
                RefreshPauseChecks();
                status.Text = text; icon.Text = Truncate("Network Recovery · " + text);
                icon.Icon = AppIcons.ForStatus(text);
                Color tone = icon.Icon == AppIcons.Enabled ? Theme.Success : icon.Icon == AppIcons.Disabled ? Theme.Neutral : Theme.Warning;
                Image old = status.Image; status.Image = Theme.DotImage(tone, Theme.S(16)); if (old != null) old.Dispose();
            }
            finally { updating = false; }
        }
        // NotifyIcon tooltips are limited to 63 characters.
        private static string Truncate(string text) { return text.Length <= 63 ? text : text.Substring(0, 62) + "…"; }
        private void RefreshPauseChecks()
        {
            try
            {
                PauseState pause = MonitorControl.Read();
                TrayPauseMenu.Apply(pauses, pause.SelectedHours(BootClock.Id, BootClock.Seconds, DateTime.UtcNow));
                foreach (ToolStripMenuItem item in pauses) item.Enabled = true;
            }
            catch
            {
                TrayPauseMenu.Apply(pauses, 0);
                foreach (ToolStripMenuItem item in pauses) item.Enabled = false;
                status.Text = "暫停狀態無法讀取";
            }
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { disposed = true; timer.Dispose(); icon.Visible = false; icon.ContextMenuStrip.Dispose(); icon.Dispose(); if (settingsProcess != null) settingsProcess.Dispose(); }
            base.Dispose(disposing);
        }
    }
    public static class TrayPauseMenu
    {
        public static ToolStripMenuItem[] Create(Action<int> click)
        {
            int[] hours = { 1, 2, 4, 24 };
            ToolStripMenuItem[] items = new ToolStripMenuItem[hours.Length];
            for (int i = 0; i < hours.Length; i++)
            {
                int duration = hours[i];
                items[i] = new ToolStripMenuItem("暫停 " + duration + " 小時") { Tag = duration, CheckOnClick = false };
                items[i].Click += delegate { click(duration); };
            }
            return items;
        }
        public static void Apply(ToolStripMenuItem[] items, int selectedHours)
        { foreach (ToolStripMenuItem item in items) item.Checked = (int)item.Tag == selectedHours; }
    }
}
