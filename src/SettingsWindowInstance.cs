using System;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;

namespace NetworkRecovery
{
    internal sealed class SettingsWindowInstance : IDisposable
    {
        private readonly Mutex mutex;
        private readonly EventWaitHandle activate;
        private System.Windows.Forms.Timer timer;
        private Form window;
        internal bool IsPrimary { get; private set; }
        private static string Scope
        {
            get
            {
                using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                    return @"Local\NetworkRecoverySettingsV1-" + identity.User.Value;
            }
        }

        internal SettingsWindowInstance() : this(Scope) { }
        internal SettingsWindowInstance(string scope)
        {
            mutex = new Mutex(false, scope);
            try { IsPrimary = mutex.WaitOne(0); }
            catch (AbandonedMutexException) { IsPrimary = true; }
            if (IsPrimary)
            {
                try { activate = new EventWaitHandle(false, EventResetMode.AutoReset, scope + ".Activate"); }
                catch { mutex.ReleaseMutex(); mutex.Dispose(); throw; }
            }
            else
            {
                // The first process may still be constructing its activation event.
                for (int attempt = 0; attempt < 20; attempt++)
                {
                    if (TryActivateExisting(scope)) break;
                    Thread.Sleep(50);
                }
            }
        }

        internal static bool TryActivateExisting() { return TryActivateExisting(Scope); }
        internal static bool TryActivateExisting(string scope)
        {
            try
            {
                using (EventWaitHandle signal = EventWaitHandle.OpenExisting(scope + ".Activate"))
                {
                    // A user action in this process permits the existing settings
                    // process to take foreground focus on the same interactive desktop.
                    AllowSetForegroundWindow(unchecked((uint)-1));
                    signal.Set();
                    return true;
                }
            }
            catch (WaitHandleCannotBeOpenedException) { return false; }
        }

        internal void Attach(Form form)
        {
            if (!IsPrimary) throw new InvalidOperationException("Only the primary process owns the settings window.");
            window = form;
            timer = new System.Windows.Forms.Timer { Interval = 150 };
            timer.Tick += delegate {
                if (!window.IsDisposed && activate.WaitOne(0))
                {
                    if (!window.Visible) window.Show();
                    if (window.WindowState == FormWindowState.Minimized) ShowWindow(window.Handle, 9);
                    window.BringToFront(); window.Activate(); SetForegroundWindow(window.Handle);
                }
            };
            timer.Start();
        }

        public void Dispose()
        {
            if (timer != null) { timer.Dispose(); timer = null; }
            if (activate != null) activate.Dispose();
            if (IsPrimary) { mutex.ReleaseMutex(); IsPrimary = false; }
            mutex.Dispose();
        }

        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AllowSetForegroundWindow(uint processId);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ShowWindow(IntPtr window, int command);
    }
}
