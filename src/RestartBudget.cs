using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using System.Xml.Serialization;

namespace NetworkRecovery
{
    public sealed class RestartHistory
    {
        public int Version = 1;
        public string BootId;
        public double SavedUptime;
        public DateTime SavedUtc;
        public List<double> Ages = new List<double>();
        public string ResumeRevision = "";
    }

    // All live decisions use uptime. UTC is used only to bridge different boots,
    // because a Windows monotonic clock cannot survive a machine restart.
    public sealed class RestartBudget
    {
        public const int Limit = 5;
        public const double WindowSeconds = 4 * 60 * 60;
        private readonly List<double> requests = new List<double>();
        public string ResumeRevision = "";

        public RestartBudget(RestartHistory history, string bootId, double uptime, DateTime utc)
        {
            if (history == null) return;
            ResumeRevision = history.ResumeRevision ?? "";
            if (history.Version != 1 || String.IsNullOrEmpty(history.BootId) ||
                history.Ages == null || history.Ages.Count > Limit ||
                !Valid(history.SavedUptime) || history.SavedUtc.Kind != DateTimeKind.Utc)
                throw new InvalidDataException("Invalid restart history.");
            double elapsed = history.BootId == bootId
                ? uptime - history.SavedUptime : (utc - history.SavedUtc).TotalSeconds;
            elapsed = Math.Max(0, elapsed);
            foreach (double age in history.Ages)
            {
                if (!Valid(age)) throw new InvalidDataException("Invalid restart age.");
                if (age + elapsed < WindowSeconds) requests.Add(uptime - age - elapsed);
            }
        }

        private static bool Valid(double value)
        {
            return !Double.IsNaN(value) && !Double.IsInfinity(value) && value >= 0;
        }

        public bool HasHistory { get { return requests.Count != 0; } }
        public void ObserveInternet(bool externalResponse)
        {
            if (externalResponse) requests.Clear();
        }

        public bool TryReserve(double uptime)
        {
            requests.RemoveAll(delegate(double request) { return uptime - request >= WindowSeconds; });
            if (requests.Count >= Limit) return false;
            requests.Add(uptime);
            return true;
        }

        public RestartHistory Snapshot(string bootId, double uptime, DateTime utc)
        {
            RestartHistory result = new RestartHistory { BootId = bootId, SavedUptime = uptime, SavedUtc = utc };
            result.ResumeRevision = ResumeRevision;
            foreach (double request in requests)
                if (uptime - request < WindowSeconds) result.Ages.Add(Math.Max(0, uptime - request));
            return result;
        }

        public static RestartBudget Blocked(double uptime)
        {
            RestartBudget result = new RestartBudget(null, null, uptime, DateTime.UtcNow);
            for (int i = 0; i < Limit; i++) result.requests.Add(uptime);
            return result;
        }
    }

    public static class RestartHistoryFile
    {
        public static RestartHistory Read(string path)
        {
            if (!File.Exists(path)) return null;
            using (FileStream stream = File.OpenRead(path))
            using (XmlReader reader = XmlReader.Create(stream, new XmlReaderSettings {
                DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 16384 }))
                return (RestartHistory)new XmlSerializer(typeof(RestartHistory)).Deserialize(reader);
        }

        public static void Write(string path, RestartHistory history)
        {
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (FileStream stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    new XmlSerializer(typeof(RestartHistory)).Serialize(stream, history);
                    stream.Flush(true);
                }
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
