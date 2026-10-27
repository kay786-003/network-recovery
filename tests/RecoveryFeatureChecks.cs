using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Xml;
using System.Xml.Serialization;
using NetworkRecovery;

internal static class RecoveryFeatureChecks
{
    private static int passed;
    private static void Check(bool condition, string message)
    { if (!condition) throw new Exception(message); passed++; }
    private sealed class Steps : IRepairSteps
    {
        public readonly List<string> Calls = new List<string>();
        public string Fail;
        public bool RestoreFails;
        private void Call(string name) { Calls.Add(name); if (Fail == name) throw new IOException("simulated"); }
        public void Log(string message) { Call("log"); }
        public void EnsureBackup() { Call("backup"); }
        public void FlushDns() { Call("dns"); }
        public void Reconnect() { Call("reconnect"); }
        public void ResetProxy() { Call("proxy"); }
        public void ResetWinsock() { Call("winsock"); }
        public void ResetIp() { Call("ip"); }
        public void Restore() { Call("restore"); if (RestoreFails) throw new IOException("restore failed"); }
    }
    private static void ExpectFailure(Action action)
    { bool failed = false; try { action(); } catch { failed = true; } Check(failed, "expected failure"); }
    public static void Main()
    {
        double deadline = StartupPolicy.GraceDeadline(20, 300);
        Check(!StartupPolicy.ShouldRepair(299.99, 0, deadline, false, 180), "no repair during startup grace");
        Check(StartupPolicy.ShouldRepair(300, 0, deadline, false, 180), "continuous startup outage repairs at 300 seconds");
        deadline = StartupPolicy.GraceDeadline(10000, 300);
        Check(!StartupPolicy.ShouldRepair(10001, 10000, deadline, false, 180), "late monitor start does not masquerade as startup outage");
        Check(StartupPolicy.ShouldRepair(10180, 10000, deadline, false, 180), "late start waits ordinary offline interval");
        Check(!StartupPolicy.ShouldRepair(300, 290, 300, true, 180), "early Internet success cancels startup outage shortcut");
        List<WifiNetwork> networks = new List<WifiNetwork> {
            new WifiNetwork { ProfileName = "primary", SsidHex = "41", HasSavedProfile = true },
            new WifiNetwork { ProfileName = "backup-b", SsidHex = "42", HasSavedProfile = true, IsVisible = true, IsConnectable = true },
            new WifiNetwork { ProfileName = "unsaved", SsidHex = "43", IsVisible = true, IsConnectable = true } };
        Check(WifiRecoveryPolicy.TargetAbsent(networks, "41", "primary"), "saved but invisible primary is absent");
        Check(WifiRecoveryPolicy.Candidates(networks, "primary").Count == 1, "never try unsaved or invisible profiles");
        networks[0].IsVisible = true; networks[0].IsConnectable = true;
        Check(!WifiRecoveryPolicy.TargetAbsent(networks, "41", "renamed-profile"), "match actual SSID independently of profile name");
        Check(WifiRecoveryPolicy.Candidates(networks, "primary")[0] == "primary", "primary preferred");
        networks[0].IsConnected = true;
        RestartBudget budget = RestartBudget.Blocked(0); budget.ObserveInternet(false);
        Check(!budget.TryReserve(1), "Wi-Fi connected without Internet never resets budget");
        Check(!WifiRecoveryPolicy.TargetAbsent(new List<WifiNetwork>(), "41", "primary"), "zero visible SSIDs follows adapter recovery");
        networks[1].IsConnectable = false;
        Check(WifiRecoveryPolicy.Candidates(networks, "primary").Count == 1, "skip unavailable saved credential candidates");

        DateTime utc = new DateTime(2026, 10, 28, 0, 0, 0, DateTimeKind.Utc);
        foreach (int hours in new[] { 1, 2, 4, 24 })
        {
            PauseState state = new PauseState { Revision = "revision", BootId = "a", Started = 100, StartedUtc = utc, Hours = hours };
            Check(state.Remaining("a", 101, utc.AddDays(1)) == hours * 3600 - 1, "pause uses monotonic time in same boot");
            Check(state.Remaining("a", 100 + hours * 3600, utc) == 0, "pause expires at exact boundary");
            Check(state.Remaining("b", 10, utc.AddSeconds(60)) == hours * 3600 - 60, "pause survives reboot and logoff");
            Check(state.Remaining("b", 10, utc.AddSeconds(-60)) == hours * 3600, "backward bridge cannot skip pause");
            using (StringWriter writer = new StringWriter())
            {
                new XmlSerializer(typeof(PauseState)).Serialize(writer, state);
                PauseState restored = (PauseState)new XmlSerializer(typeof(PauseState)).Deserialize(new StringReader(writer.ToString()));
                Check(restored.Remaining("a", 102, utc) == hours * 3600 - 2, "pause file roundtrip");
            }
            state.Anchor("b", 10, utc.AddSeconds(60));
            Check(state.Remaining("b", 11, utc.AddDays(10)) == hours * 3600 - 61, "cross-boot pause switches back to monotonic time after anchoring");
        }
        ExpectFailure(delegate { new PauseState { Hours = 3 }.Remaining("a", 100, utc); });
        Check(new PauseState().Remaining("a", 100, utc) == 0, "missing control state is not paused");
        budget.ResumeRevision = "applied";
        Check(new RestartBudget(budget.Snapshot("a", 1, utc), "a", 2, utc).ResumeRevision == "applied", "resume reset marker persists across process restart");

        List<string> order = new List<string>();
        Check(RestartRequest.Execute(delegate { order.Add("reserve"); return true; }, delegate { order.Add("save"); return true; },
            delegate { order.Add("log"); }, delegate { order.Add("shutdown"); return 0; }), "restart allowed after durable prerequisites");
        Check(String.Join(",", order) == "reserve,save,log,shutdown", "reason is durable before shutdown");
        bool shutdown = false;
        Check(!RestartRequest.Execute(delegate { return false; }, delegate { throw new Exception(); }, delegate { throw new Exception(); }, delegate { shutdown = true; return 0; }), "full budget blocks all later steps");
        Check(!RestartRequest.Execute(delegate { return true; }, delegate { return false; }, delegate { throw new Exception(); }, delegate { shutdown = true; return 0; }), "save failure blocks shutdown");
        ExpectFailure(delegate { RestartRequest.Execute(delegate { return true; }, delegate { return true; }, delegate { throw new IOException(); }, delegate { shutdown = true; return 0; }); });
        Check(!shutdown, "logging failure cannot reboot");
        Check(!RestartRequest.Execute(delegate { return true; }, delegate { return true; }, delegate { }, delegate { return 5; }), "Windows refusal reported");

        Steps steps = new Steps(); RepairPlan.Run(steps, false);
        Check(String.Join(",", steps.Calls) == "log,dns,reconnect,log", "basic repair never resets proxy or system stack");
        steps = new Steps(); RepairPlan.Run(steps, true);
        Check(String.Join(",", steps.Calls) == "log,backup,dns,reconnect,proxy,winsock,ip,log", "advanced repair backs up first and follows ordered stages");
        steps = new Steps { Fail = "backup" }; ExpectFailure(delegate { RepairPlan.Run(steps, true); });
        Check(!steps.Calls.Contains("dns") && !steps.Calls.Contains("proxy"), "backup failure makes no network changes");
        foreach (string stage in new[] { "proxy", "winsock", "ip" })
        {
            steps = new Steps { Fail = stage }; ExpectFailure(delegate { RepairPlan.Run(steps, true); });
            Check(steps.Calls.Contains("restore"), "partial advanced failure restores backup");
        }
        steps = new Steps { Fail = "ip", RestoreFails = true }; ExpectFailure(delegate { RepairPlan.Run(steps, true); });

        RepairBackup backup = new RepairBackup { UserSid = "demo" };
        backup.Proxy.Add(new ProxyValue { Name = "ProxyEnable", Exists = true, Kind = 4, Value = 1 });
        backup.Proxy.Add(new ProxyValue { Name = "AutoConfigURL", Exists = false });
        using (StringWriter writer = new StringWriter())
        {
            new XmlSerializer(typeof(RepairBackup)).Serialize(writer, backup);
            RepairBackup restored = (RepairBackup)new XmlSerializer(typeof(RepairBackup)).Deserialize(new StringReader(writer.ToString()));
            Check((int)restored.Proxy[0].Value == 1 && !restored.Proxy[1].Exists, "proxy backup preserves type and absent values");
        }

        string xml = (string)typeof(AppInstaller).GetMethod("BuildTrayTaskXml", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, new object[] { "S-1-5-21-1-2-3-1001", false });
        XmlDocument doc = new XmlDocument(); doc.LoadXml(xml);
        XmlNamespaceManager ns = new XmlNamespaceManager(doc.NameTable); ns.AddNamespace("t", "http://schemas.microsoft.com/windows/2004/02/mit/task");
        Check(doc.SelectSingleNode("//t:LogonType", ns).InnerText == "InteractiveToken", "tray uses interactive user token, not SYSTEM desktop");
        Check(doc.SelectSingleNode("//t:LogonTrigger/t:UserId", ns).InnerText == "S-1-5-21-1-2-3-1001", "tray logon restricted to installer account");
        Check(doc.SelectSingleNode("//t:Actions/t:Exec/t:Arguments", ns).InnerText == "--tray", "tray task launches tray mode");
        Check(doc.SelectSingleNode("//t:Settings/t:Enabled", ns).InnerText == "false", "stage tray disabled until handover");
        Check(doc.SelectSingleNode("//t:Actions/t:Exec/t:Command", ns).InnerText.EndsWith("NetworkRecovery.exe"), "installed internal path stays compatible");
        // Validate the actual tray task with Windows without registering or starting it.
        string validationPath = Path.Combine(Path.GetTempPath(), "NetworkRecovery-task-validation-" + Guid.NewGuid().ToString("N") + ".xml");
        try
        {
            string sid = System.Security.Principal.WindowsIdentity.GetCurrent().User.Value;
            string actualXml = (string)typeof(AppInstaller).GetMethod("BuildTrayTaskXml", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { sid, false });
            File.WriteAllText(validationPath, actualXml, System.Text.Encoding.Unicode);
            typeof(AppInstaller).GetMethod("ValidateTaskXml", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { validationPath, "NetworkRecovery Tray", sid, 3 });
            Check(true, "Windows TASK_VALIDATE_ONLY accepted interactive tray task XML");
        }
        finally { File.Delete(validationPath); }
        Console.WriteLine("PASS: " + passed + " isolated D0008-D0014 checks; no production actions.");
    }
}
