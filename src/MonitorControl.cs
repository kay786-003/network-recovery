using System;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using System.Xml;
using System.Xml.Serialization;

namespace NetworkRecovery
{
    public static class BootClock
    {
        [DllImport("kernel32.dll")] private static extern ulong GetTickCount64();
        private static string boot;
        public static double Seconds { get { return GetTickCount64() / 1000.0; } }
        public static string Id
        {
            get
            {
                if (boot == null)
                    using (ManagementObject os = new ManagementObject("Win32_OperatingSystem=@"))
                    { os.Get(); boot = Convert.ToString(os["LastBootUpTime"]); }
                if (String.IsNullOrEmpty(boot)) throw new InvalidDataException("Missing boot identity.");
                return boot;
            }
        }
    }

    public sealed class PauseState
    {
        public string Revision = "";
        public string ObservationRevision = "";
        public string BootId = "";
        public double Started;
        public DateTime StartedUtc;
        public int Hours;
        public double ElapsedBeforeBoot;
        public double Remaining(string bootId, double now, DateTime utc)
        {
            if (Hours == 0) return 0;
            if (Hours != 1 && Hours != 2 && Hours != 4 && Hours != 24)
                throw new InvalidDataException("Invalid pause duration.");
            if (String.IsNullOrEmpty(BootId) || String.IsNullOrEmpty(Revision) ||
                Started < 0 || Double.IsNaN(Started) || Double.IsInfinity(Started) || StartedUtc.Kind != DateTimeKind.Utc)
                throw new InvalidDataException("Invalid pause state.");
            if (ElapsedBeforeBoot < 0 || Double.IsNaN(ElapsedBeforeBoot) || Double.IsInfinity(ElapsedBeforeBoot))
                throw new InvalidDataException("Invalid pause elapsed time.");
            double elapsed = bootId == BootId ? now - Started : (utc - StartedUtc).TotalSeconds;
            return Math.Max(0, Hours * 3600.0 - ElapsedBeforeBoot - Math.Max(0, elapsed));
        }
        public void Anchor(string bootId, double now, DateTime utc)
        {
            double remaining = Remaining(bootId, now, utc);
            ElapsedBeforeBoot = Hours * 3600.0 - remaining;
            BootId = bootId; Started = now; StartedUtc = utc;
        }
        public int SelectedHours(string bootId, double now, DateTime utc)
        { return Remaining(bootId, now, utc) > 0 ? Hours : 0; }
        public int ToggleHours(int clickedHours, string bootId, double now, DateTime utc)
        {
            if (clickedHours != 1 && clickedHours != 2 && clickedHours != 4 && clickedHours != 24)
                throw new ArgumentOutOfRangeException("clickedHours");
            return SelectedHours(bootId, now, utc) == clickedHours ? 0 : clickedHours;
        }
    }

    public sealed class MonitorStatus
    {
        public string State = "等待監控";
        public string BootId;
        public double Updated;
        public bool Online;
    }

    // Serializes interactive repairs/pause requests with background network actions.
    // A completed pause request means no new automatic action can start.
    public sealed class ActionLease : IDisposable
    {
        private Mutex mutex;
        public ActionLease(int timeout)
        {
            MutexSecurity security = new MutexSecurity();
            foreach (WellKnownSidType sid in new[] { WellKnownSidType.BuiltinAdministratorsSid, WellKnownSidType.LocalSystemSid })
                security.AddAccessRule(new MutexAccessRule(new SecurityIdentifier(sid, null), MutexRights.FullControl, AccessControlType.Allow));
            bool created;
            mutex = new Mutex(false, @"Global\NetworkRecoveryActionsV1", out created, security);
            bool acquired = false;
            try { acquired = mutex.WaitOne(timeout); }
            catch (AbandonedMutexException) { acquired = true; }
            if (!acquired) { mutex.Dispose(); mutex = null; throw new TimeoutException("網路操作進行中，請稍後重試。"); }
        }
        public void Dispose() { if (mutex != null) { mutex.ReleaseMutex(); mutex.Dispose(); mutex = null; } }
    }

    public static class StateFile
    {
        public static T Read<T>(string path) where T : class
        {
            AppInstaller.CheckStatePath(path);
            if (!File.Exists(path)) return null;
            using (FileStream stream = File.OpenRead(path))
            using (XmlReader reader = XmlReader.Create(stream, new XmlReaderSettings {
                DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 65536 }))
                return (T)new XmlSerializer(typeof(T)).Deserialize(reader);
        }
        public static void Write<T>(string path, T value)
        {
            AppInstaller.ProtectStatePath(path);
            string staging = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (FileStream stream = new FileStream(staging, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { new XmlSerializer(typeof(T)).Serialize(stream, value); stream.Flush(true); }
                if (File.Exists(path)) File.Replace(staging, path, null);
                else File.Move(staging, path);
            }
            finally { if (File.Exists(staging)) File.Delete(staging); }
        }
    }

    public static class MonitorControl
    {
        public static PauseState Read() { return StateFile.Read<PauseState>(AppPaths.ControlFile) ?? new PauseState(); }
        public static bool IsPaused(PauseState state)
        { return state.Remaining(BootClock.Id, BootClock.Seconds, DateTime.UtcNow) > 0; }
        public static void SetPause(int hours)
        {
            if (hours != 0 && hours != 1 && hours != 2 && hours != 4 && hours != 24) throw new ArgumentOutOfRangeException("hours");
            using (ActionLease action = new ActionLease(1000))
                WritePause(hours);
        }
        public static void TogglePause(int hours)
        {
            using (ActionLease action = new ActionLease(1000))
                WritePause(Read().ToggleHours(hours, BootClock.Id, BootClock.Seconds, DateTime.UtcNow));
        }
        private static void WritePause(int hours)
        {
            StateFile.Write(AppPaths.ControlFile, new PauseState { Revision = Guid.NewGuid().ToString("N"),
                BootId = BootClock.Id, Started = BootClock.Seconds, StartedUtc = DateTime.UtcNow, Hours = hours });
        }
        public static void Publish(string state, bool online)
        {
            try { StateFile.Write(AppPaths.StatusFile, new MonitorStatus {
                State = state, Online = online, BootId = BootClock.Id, Updated = BootClock.Seconds }); }
            catch (Exception ex) { RecoveryEngine.WriteLog("Status write failed: " + ex.GetType().Name); }
        }
        public static string Describe()
        {
            PauseState pause = Read();
            double remaining = pause.Remaining(BootClock.Id, BootClock.Seconds, DateTime.UtcNow);
            if (remaining > 0) return "已暫停（剩餘 " + Math.Ceiling(remaining / 60) + " 分鐘）";
            MonitorStatus status = StateFile.Read<MonitorStatus>(AppPaths.StatusFile);
            if (status == null || status.BootId != BootClock.Id || BootClock.Seconds - status.Updated > 90) return "等待監控回報";
            return status.State;
        }
    }
}
