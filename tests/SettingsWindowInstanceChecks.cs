using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using NetworkRecovery;

internal static class SettingsWindowInstanceChecks
{
    private static int checks;
    private static void Check(bool value, string message)
    { if (!value) throw new Exception(message); checks++; }
    private static Process Secondary(string scope)
    {
        return Process.Start(new ProcessStartInfo(Assembly.GetExecutingAssembly().Location, "--secondary \"" + scope + "\"") {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true });
    }
    private static void Finish(Process process)
    {
        if (!process.WaitForExit(5000)) throw new Exception("Secondary settings process did not exit.");
        Check(process.ExitCode == 0, "secondary activates existing instance and exits without creating a form");
    }
    private static void PumpUntil(Func<bool> condition)
    {
        Stopwatch watch = Stopwatch.StartNew();
        while (!condition() && watch.ElapsedMilliseconds < 3000)
        { Application.DoEvents(); Thread.Sleep(15); }
        Check(condition(), "existing minimized window restored through the activation signal");
    }
    [STAThread] private static void Main(string[] args)
    {
        try
        {
            if (args.Length == 2 && args[0] == "--secondary")
            {
                using (SettingsWindowInstance secondary = new SettingsWindowInstance(args[1]))
                    if (secondary.IsPrimary) throw new Exception("Duplicate process incorrectly acquired ownership.");
                return;
            }
            Run();
        }
        catch (Exception ex) { Console.Error.WriteLine(ex.GetType().Name + ": " + ex.Message); Environment.ExitCode = 1; }
    }
    private static void Run()
    {
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        // Unique names and a generic test form; never attach to the real settings
        // session, start the product EXE, query WLAN, or operate on scheduled tasks.
        string scope = @"Local\NetworkRecoverySettings-test-" + Guid.NewGuid().ToString("N");
        Check(!SettingsWindowInstance.TryActivateExisting(scope), "no existing settings window before startup");
        using (SettingsWindowInstance primary = new SettingsWindowInstance(scope))
        {
            Check(primary.IsPrimary, "first process owns settings instance");
            // Activation while startup is pending is retained until the UI is attached.
            using (Process child = Secondary(scope)) Finish(child);
            using (Form form = new Form { Text = "Settings activation demo", Width = 480, Height = 280 })
            {
                primary.Attach(form);
                form.Show(); Application.DoEvents();
                form.WindowState = FormWindowState.Minimized; Application.DoEvents();
                PumpUntil(delegate { return form.WindowState != FormWindowState.Minimized; });
                Check(Application.OpenForms.Count == 1, "startup activation creates only one window");

                form.WindowState = FormWindowState.Minimized; Application.DoEvents();
                List<Process> repeated = new List<Process>();
                try
                {
                    for (int i = 0; i < 8; i++) repeated.Add(Secondary(scope));
                    foreach (Process child in repeated) Finish(child);
                }
                finally { foreach (Process child in repeated) child.Dispose(); }
                PumpUntil(delegate { return form.WindowState != FormWindowState.Minimized; });
                Check(Application.OpenForms.Count == 1, "rapid repeated launches keep one settings window");

                using (SettingsWindowInstance independent = new SettingsWindowInstance(scope + "-other"))
                    Check(independent.IsPrimary, "independent test session remains independent");
                form.Close(); Application.DoEvents();
            }
        }
        Check(!SettingsWindowInstance.TryActivateExisting(scope), "closing settings releases its activation event");
        using (SettingsWindowInstance reopened = new SettingsWindowInstance(scope))
            Check(reopened.IsPrimary, "settings can reopen after original instance exits");
        Console.WriteLine("PASS: " + checks + " isolated multi-process settings-window checks; no product monitoring or network actions.");
    }
}
