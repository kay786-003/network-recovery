using System;
using System.IO;
using System.Xml.Serialization;

namespace NetworkRecovery
{
    [Serializable]
    public class AppConfig
    {
        public string AdapterId = "";
        public string AdapterName = "";
        public string WifiProfileName = "";
        public string WifiSsidHex = "";
        public bool ReconnectWifi;
        public int OfflineSeconds = 180;
        public int RepairSeconds = 60;
        public int StartupGraceSeconds = 300;
    }

    public static class AppPaths
    {
        public static string Root { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NetworkRecovery"); } }
        public static string Data { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "NetworkRecovery"); } }
        public static string ConfigFile { get { return Path.Combine(Data, "config.xml"); } }
        public static string LogFile { get { return Path.Combine(Data, "events.log"); } }
        public static string RestartHistoryFile { get { return Path.Combine(Data, "restart-history.xml"); } }
        public static string ControlFile { get { return Path.Combine(Data, "control.xml"); } }
        public static string StatusFile { get { return Path.Combine(Data, "status.xml"); } }
        public static string RepairBackup { get { return Path.Combine(Data, "repair-backup"); } }
    }

    public static class StartupPolicy
    {
        public static double GraceDeadline(double uptime, int startupSeconds)
        { return uptime < startupSeconds ? startupSeconds : 0; }

        public static bool ShouldRepair(double now, double failureSince, double graceDeadline, bool everOnline, int offlineSeconds)
        { return now >= graceDeadline && ((!everOnline && graceDeadline > 0) || now - failureSince >= offlineSeconds); }

        public static bool RequiresFirstRunConnectivityCheck(bool hasSavedConfiguration, Func<bool> isMonitorInstalled)
        {
            if (hasSavedConfiguration) return false;
            if (isMonitorInstalled == null) throw new ArgumentNullException("isMonitorInstalled");
            return !isMonitorInstalled();
        }
    }
}
