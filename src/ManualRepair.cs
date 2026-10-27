using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;
using Microsoft.Win32;

namespace NetworkRecovery
{
    public sealed class ProxyValue
    {
        public string Name;
        public bool Exists;
        public int Kind;
        public object Value;
    }
    public sealed class RepairBackup
    {
        public string UserSid;
        public List<ProxyValue> Proxy = new List<ProxyValue>();
        public List<string> Hashes = new List<string>();
    }

    public interface IRepairSteps
    {
        void Log(string message);
        void EnsureBackup();
        void FlushDns();
        void Reconnect();
        void ResetProxy();
        void ResetWinsock();
        void ResetIp();
        void Restore();
    }
    public static class RepairPlan
    {
        // Testable ordering; a backup failure never reaches a mutating step.
        public static void Run(IRepairSteps steps, bool advanced)
        {
            steps.Log(advanced ? "手動進階修復開始。" : "手動基本修復開始。");
            bool changed = false;
            try
            {
                if (advanced) steps.EnsureBackup();
                steps.FlushDns();
                steps.Reconnect();
                if (advanced)
                {
                    changed = true;
                    steps.ResetProxy(); steps.ResetWinsock(); steps.ResetIp();
                }
                steps.Log(advanced ? "手動進階修復完成；需重新啟動生效。" : "手動基本修復完成。");
            }
            catch
            {
                if (changed)
                {
                    try { steps.Restore(); steps.Log("修復失敗，已執行備份還原；需重新啟動生效。"); }
                    catch { throw new InvalidOperationException("修復失敗且還原未完成，備份已保留。請使用還原備份重試。"); }
                }
                steps.Log("手動修復失敗；未要求重新啟動。");
                throw;
            }
        }
    }

    public sealed class ManualRepair : IRepairSteps
    {
        private const string ProxyKey = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";
        private static readonly string[] Files = { "winhttp.txt", "ipv4.txt", "ipv6.txt", "winsock.reg", "winsock2.reg" };
        private readonly AppConfig config;
        public ManualRepair(AppConfig config) { this.config = config; }
        private static string PathFor(string name)
        {
            string path = Path.Combine(AppPaths.RepairBackup, name);
            AppInstaller.ProtectBackupFile(path);
            return path;
        }

        [DllImport("wtsapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool WTSQuerySessionInformation(IntPtr server, int session, int info, out IntPtr buffer, out int bytes);
        [DllImport("wtsapi32.dll")] private static extern void WTSFreeMemory(IntPtr memory);
        private static string SessionText(int info)
        {
            IntPtr buffer; int bytes;
            if (!WTSQuerySessionInformation(IntPtr.Zero, Process.GetCurrentProcess().SessionId, info, out buffer, out bytes))
                throw new InvalidOperationException("無法確認目前登入的使用者。");
            try { return Marshal.PtrToStringUni(buffer); } finally { WTSFreeMemory(buffer); }
        }
        private static string InteractiveSid()
        {
            WindowsIdentity identity = WindowsIdentity.GetCurrent();
            if (identity.IsSystem || Process.GetCurrentProcess().SessionId == 0)
                throw new InvalidOperationException("手動修復只能在登入的使用者桌面執行。");
            string sid = ((SecurityIdentifier)new NTAccount(SessionText(7), SessionText(5)).Translate(typeof(SecurityIdentifier))).Value;
            if (sid != identity.User.Value)
                throw new InvalidOperationException("請使用目前登入帳戶的管理員權限；不能以其他管理員帳戶修改此使用者的 Proxy。");
            return sid;
        }

        private static string Command(string program, string args)
        {
            ProcessStartInfo start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), program), args) {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            using (Process process = Process.Start(start))
            {
                Task<string> output = process.StandardOutput.ReadToEndAsync();
                Task<string> error = process.StandardError.ReadToEndAsync();
                // Keep the action lease until the child exits; do not let automatic repair
                // overlap a still-running reset or abandon its restore path on a timeout.
                process.WaitForExit(); Task.WaitAll(output, error);
                if (process.ExitCode != 0) throw new InvalidOperationException(program + " 操作失敗（" + process.ExitCode + "）。");
                return output.Result;
            }
        }
        private static string Hash(string path)
        {
            using (SHA256 hash = SHA256.Create())
            using (FileStream stream = File.OpenRead(path)) return Convert.ToBase64String(hash.ComputeHash(stream));
        }
        public void Log(string message) { RecoveryEngine.WriteRequiredLog(message); }
        public void EnsureBackup()
        {
            AppInstaller.PreparePrivateBackup();
            if (File.Exists(PathFor("backup.xml"))) LoadValidatedBackup();
            else Backup();
        }
        public void Backup()
        {
            string sid = InteractiveSid();
            AppInstaller.PreparePrivateBackup();
            if (File.Exists(PathFor("backup.xml"))) throw new InvalidOperationException("已有修復備份。請先還原，或確認不再需要後移除舊備份。");
            RepairBackup backup = new RepairBackup { UserSid = sid };
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(ProxyKey, false))
                foreach (string name in new[] { "ProxyEnable", "ProxyServer", "AutoConfigURL" })
                {
                    object value = key == null ? null : key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                    backup.Proxy.Add(new ProxyValue { Name = name, Exists = value != null,
                        Kind = value == null ? 0 : (int)key.GetValueKind(name), Value = value });
                }
            for (int i = 0; i < 3; i++)
            {
                string context = i == 0 ? "winhttp" : i == 1 ? "interface ipv4" : "interface ipv6";
                string dump = Command("netsh.exe", context + " dump");
                if (String.IsNullOrWhiteSpace(dump) || dump.IndexOf("pushd", StringComparison.OrdinalIgnoreCase) < 0)
                    throw new InvalidDataException("網路設定備份不完整，未執行重設。");
                File.WriteAllText(PathFor(Files[i]), dump, Encoding.Unicode);
            }
            Command("reg.exe", "export HKLM\\SYSTEM\\CurrentControlSet\\Services\\WinSock \"" + PathFor(Files[3]) + "\" /y");
            Command("reg.exe", "export HKLM\\SYSTEM\\CurrentControlSet\\Services\\WinSock2 \"" + PathFor(Files[4]) + "\" /y");
            foreach (string name in Files)
            {
                string path = PathFor(name);
                using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite)) stream.Flush(true);
                backup.Hashes.Add(Hash(path));
            }
            using (FileStream stream = new FileStream(PathFor("backup.xml"), FileMode.CreateNew, FileAccess.Write))
            { new XmlSerializer(typeof(RepairBackup)).Serialize(stream, backup); stream.Flush(true); }
        }
        public void FlushDns() { InteractiveSid(); Command("ipconfig.exe", "/flushdns"); }
        public void Reconnect()
        {
            Guid id;
            if (config == null || !Guid.TryParse(config.AdapterId, out id)) throw new InvalidOperationException("請先儲存要修復的網卡。");
            NetworkManager.RestartAdapter(id);
            if (config.ReconnectWifi) NetworkManager.Connect(id, config.WifiProfileName);
        }
        public void ResetProxy()
        {
            InteractiveSid();
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(ProxyKey))
            {
                key.SetValue("ProxyEnable", 0, RegistryValueKind.DWord);
                key.DeleteValue("ProxyServer", false); key.DeleteValue("AutoConfigURL", false);
            }
            Command("netsh.exe", "winhttp reset proxy");
        }
        public void ResetWinsock() { Command("netsh.exe", "winsock reset"); }
        public void ResetIp() { Command("netsh.exe", "interface ip reset \"" + PathFor("reset.log") + "\""); }
        public void Restore()
        {
            RepairBackup backup = LoadValidatedBackup();
            List<string> failed = new List<string>();
            for (int i = 0; i < Files.Length; i++)
            {
                try { Command(i < 3 ? "netsh.exe" : "reg.exe", (i < 3 ? "exec" : "import") + " \"" + PathFor(Files[i]) + "\""); }
                catch { failed.Add(Files[i]); }
            }
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(ProxyKey))
                foreach (ProxyValue value in backup.Proxy)
                {
                    if (value.Name != "ProxyEnable" && value.Name != "ProxyServer" && value.Name != "AutoConfigURL") throw new InvalidDataException("Invalid proxy backup.");
                    if (value.Exists) key.SetValue(value.Name, value.Value, (RegistryValueKind)value.Kind);
                    else key.DeleteValue(value.Name, false);
                }
            if (failed.Count > 0) throw new IOException("部分還原未完成，請保留備份並重試。");
        }
        private static RepairBackup LoadValidatedBackup()
        {
            string sid = InteractiveSid();
            AppInstaller.PreparePrivateBackup();
            RepairBackup backup = StateFile.Read<RepairBackup>(PathFor("backup.xml"));
            if (backup == null || backup.UserSid != sid || backup.Hashes == null || backup.Hashes.Count != Files.Length ||
                backup.Proxy == null || backup.Proxy.Count != 3)
                throw new InvalidDataException("無可用於目前登入使用者的完整備份。");
            HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);
            foreach (ProxyValue value in backup.Proxy)
                if (value == null || !names.Add(value.Name) ||
                    (value.Name != "ProxyEnable" && value.Name != "ProxyServer" && value.Name != "AutoConfigURL") ||
                    (value.Exists && value.Value == null)) throw new InvalidDataException("備份的 Proxy 設定不完整。");
            for (int i = 0; i < Files.Length; i++)
                if (Hash(PathFor(Files[i])) != backup.Hashes[i]) throw new InvalidDataException("備份校驗失敗，未執行重設或還原。");
            return backup;
        }
        public static void DiscardBackup()
        {
            InteractiveSid();
            AppInstaller.PreparePrivateBackup();
            // Explicit fixed files only; never enumerate or recursively delete local data.
            foreach (string name in Files) File.Delete(PathFor(name));
            File.Delete(PathFor("reset.log")); File.Delete(PathFor("backup.xml"));
        }
    }
}
