using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace NetworkRecovery
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            try
            {
                if (args.Length > 0 && args[0] == "--monitor")
                {
                    bool created;
                    using (Mutex monitor = new Mutex(true, @"Global\NetworkRecovery.Monitor", out created))
                    { if (created) RecoveryEngine.Run(); }
                    return;
                }
                if (args.Length > 0 && args[0] == "--probe")
                {
                    Environment.ExitCode = RecoveryEngine.InternetAvailable(3500) ? 0 : 2;
                    return;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                bool preview = args.Length > 0 && args[0] == "--preview";
                if (!preview && StartupPolicy.RequiresFirstRunConnectivityCheck(
                    File.Exists(AppPaths.ConfigFile), AppInstaller.IsInstalled) &&
                    !RecoveryEngine.InternetAvailable(5000))
                {
                    MessageBox.Show("首次設定前必須確認至少一個外部連線檢查服務可用。Microsoft、Cloudflare 與 Google 目前皆無成功回應。請確認網路、Proxy 或防火牆設定後再重新執行。程式將結束。",
                        "Network Recovery", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    Environment.ExitCode = 2;
                    return;
                }
                if (preview)
                {
                    if (args.Length < 2) throw new ArgumentException("Preview output path required.");
                    using (MainForm form = new MainForm(true))
                    {
                        form.Show(); Application.DoEvents();
                        using (System.Drawing.Bitmap bitmap = new System.Drawing.Bitmap(form.Width, form.Height))
                        { form.DrawToBitmap(bitmap, form.ClientRectangle); bitmap.Save(args[1], System.Drawing.Imaging.ImageFormat.Png); }
                        form.Close();
                    }
                    return;
                }
                Application.Run(new MainForm(false));
            }
            catch (Exception ex)
            {
                if (args.Length > 0 && (args[0] == "--monitor" || args[0] == "--probe")) { Environment.ExitCode = 1; return; }
                MessageBox.Show(ex.Message, "Network Recovery", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
