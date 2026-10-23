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
    }
}
