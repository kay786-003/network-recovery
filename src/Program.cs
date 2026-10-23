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
