using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Xml;

namespace NetworkRecovery
{
    public class AdapterInfo
    {
        public string Id, Name, Description, State;
        public bool IsWireless;
        public override string ToString() { return Name + " — " + Description + " (" + State + ")"; }
    }

    public class WifiNetwork
    {
        public string Ssid, ProfileName, Security, SsidHex;
        public bool HasSavedProfile, IsConnected, IsConnectable, IsOpen, IsSupportedPersonal;
        public int Signal;
        public uint Auth, Cipher;
        public override string ToString()
        {
            return Ssid + " — " + Security + " · " + Signal + "%" + (IsConnected ? " · 已連接" : HasSavedProfile ? " · 已儲存" : "");
        }
    }

    public static class NetworkManager
    {
        public static List<AdapterInfo> GetAdapters()
        {
            Dictionary<string, AdapterInfo> managed = new Dictionary<string, AdapterInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (NetworkInterface item in NetworkInterface.GetAllNetworkInterfaces())
            {
                Guid id;
                if (!Guid.TryParse(item.Id, out id)) continue;
                managed[id.ToString()] = new AdapterInfo { Id = id.ToString(), Name = item.Name, Description = item.Description,
                    State = item.OperationalStatus.ToString(), IsWireless = item.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 };
            }
            // Get-NetAdapter includes administratively disabled and hidden adapters, unlike
            // NetworkInterface on some drivers. XML preserves names without locale parsing.
            Dictionary<string, AdapterInfo> found = new Dictionary<string, AdapterInfo>(StringComparer.OrdinalIgnoreCase);
            string script = "Get-NetAdapter -IncludeHidden -ErrorAction Stop | Select-Object InterfaceGuid,Name,InterfaceDescription,Status,NdisPhysicalMedium,Virtual | ConvertTo-Xml -As String -NoTypeInformation";
            string output = RunPowerShell(script, 20000);
            XmlDocument doc = new XmlDocument { XmlResolver = null };
            doc.LoadXml(output);
            foreach (XmlNode obj in doc.SelectNodes("/Objects/Object"))
            {
                Dictionary<string, string> values = new Dictionary<string, string>();
                foreach (XmlNode property in obj.SelectNodes("Property")) values[property.Attributes["Name"].Value] = property.InnerText;
                Guid id;
                if (!values.ContainsKey("InterfaceGuid") || !Guid.TryParse(values["InterfaceGuid"], out id)) continue;
                AdapterInfo existing;
                managed.TryGetValue(id.ToString(), out existing);
                string medium = values.ContainsKey("NdisPhysicalMedium") ? values["NdisPhysicalMedium"] : "";
                found[id.ToString()] = new AdapterInfo { Id = id.ToString(), Name = values["Name"], Description = values["InterfaceDescription"],
                    State = values["Status"], IsWireless = values["Virtual"] != "True" && ((existing != null && existing.IsWireless) || medium == "9" || medium == "1" || medium.IndexOf("802.11", StringComparison.OrdinalIgnoreCase) >= 0 || medium.IndexOf("Wireless", StringComparison.OrdinalIgnoreCase) >= 0) };
            }
            try
            {
                using (WlanClient client = new WlanClient())
                {
                    IntPtr list = IntPtr.Zero;
                    try
                    {
                        Check(WlanEnumInterfaces(client.Handle, IntPtr.Zero, out list), "讀取無線網卡");
                        int count = Marshal.ReadInt32(list), size = Marshal.SizeOf(typeof(WlanInterfaceInfo));
                        for (int i = 0; i < count; i++)
                        {
                            WlanInterfaceInfo info = (WlanInterfaceInfo)Marshal.PtrToStructure(IntPtr.Add(list, 8 + i * size), typeof(WlanInterfaceInfo));
                            AdapterInfo adapter;
                            if (found.TryGetValue(info.Id.ToString(), out adapter)) adapter.IsWireless = true;
                        }
                    }
                    finally { if (list != IntPtr.Zero) WlanFreeMemory(list); }
                }
            }
            catch (Win32Exception) { /* Wired-only PCs can have WLAN AutoConfig stopped. */ }
            List<AdapterInfo> result = new List<AdapterInfo>(found.Values);
            result.Sort(delegate(AdapterInfo a, AdapterInfo b) { return string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase); });
            return result;
        }

        public static bool IsAdapterAvailable(Guid id)
        {
            foreach (AdapterInfo adapter in GetAdapters()) if (new Guid(adapter.Id) == id) return true;
            return false;
        }

        public static void Scan(Guid id)
        {
            using (WlanClient client = new WlanClient()) Check(WlanScan(client.Handle, ref id, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero), "掃描 Wi-Fi");
        }

        public static List<WifiNetwork> GetNetworks(Guid id)
        {
            using (WlanClient client = new WlanClient())
            {
                IntPtr list = IntPtr.Zero;
                try
                {
                    Check(WlanGetAvailableNetworkList(client.Handle, ref id, 2, IntPtr.Zero, out list), "取得 Wi-Fi 清單");
                    int size = Marshal.SizeOf(typeof(WlanAvailableNetwork));
                    if (size != 628) throw new InvalidOperationException("Windows WLAN 結構大小不符。");
                    int count = Marshal.ReadInt32(list);
                    Dictionary<string, WifiNetwork> unique = new Dictionary<string, WifiNetwork>(StringComparer.Ordinal);
                    for (int i = 0; i < count; i++)
                    {
                        WlanAvailableNetwork raw = (WlanAvailableNetwork)Marshal.PtrToStructure(IntPtr.Add(list, 8 + i * size), typeof(WlanAvailableNetwork));
                        if (raw.Ssid.Length == 0 || raw.Ssid.Length > 32 || raw.BssType != 1) continue;
                        byte[] bytes = new byte[raw.Ssid.Length];
                        Array.Copy(raw.Ssid.Bytes, bytes, bytes.Length);
                        string hex = BitConverter.ToString(bytes).Replace("-", "");
                        string name;
                        try { name = new UTF8Encoding(false, true).GetString(bytes); }
                        catch (DecoderFallbackException) { name = "SSID " + hex; }
                        bool open = !raw.SecurityEnabled && raw.Auth == 1;
                        WifiNetwork network = new WifiNetwork { Ssid = name, SsidHex = hex, ProfileName = raw.ProfileName, Auth = raw.Auth, Cipher = raw.Cipher,
                            HasSavedProfile = (raw.Flags & 2) != 0, IsConnected = (raw.Flags & 1) != 0, IsConnectable = raw.Connectable,
                            Signal = (int)raw.Signal, IsOpen = open, IsSupportedPersonal = (raw.Auth == 7 || raw.Auth == 9) && raw.Cipher == 4,
                            Security = open ? "開放網絡" : raw.Auth == 7 ? "WPA2-Personal" : raw.Auth == 9 ? "WPA3-Personal" : "企業／其他安全性（須使用 Windows 已存設定）" };
                        string key = hex + ":" + raw.Auth + ":" + raw.Cipher;
                        WifiNetwork previous;
                        if (!unique.TryGetValue(key, out previous) || network.IsConnected || (!previous.IsConnected && network.HasSavedProfile)) unique[key] = network;
                    }
                    List<WifiNetwork> result = new List<WifiNetwork>(unique.Values);
                    result.Sort(delegate(WifiNetwork a, WifiNetwork b) { return b.Signal.CompareTo(a.Signal); });
                    return result;
                }
                finally { if (list != IntPtr.Zero) WlanFreeMemory(list); }
            }
        }

        public static string ConfigureWifi(Guid id, WifiNetwork network, string password)
        {
            if (network == null) throw new ArgumentNullException("network");
            using (WlanClient client = new WlanClient())
            {
                if (network.HasSavedProfile && !string.IsNullOrEmpty(network.ProfileName))
                {
                    IntPtr ptr = IntPtr.Zero;
                    uint flags = 0, access;
                    try
                    {
                        Check(WlanGetProfile(client.Handle, ref id, network.ProfileName, IntPtr.Zero, out ptr, ref flags, out access), "讀取已存 Wi-Fi 設定");
                        // Never export plaintext keys or silently replace per-user/enterprise profiles.
                        if ((flags & 2) != 0) throw new InvalidOperationException("此 Wi-Fi 設定只供目前使用者使用，開機背景服務無法使用。請在 Windows 將它設為所有使用者可用，或以管理員重新建立 Wi-Fi 連線，再重試。");
                        if (!string.IsNullOrEmpty(password))
                        {
                            if ((flags & 1) != 0) throw new InvalidOperationException("此 Wi-Fi 由公司原則管理，無法在此修改密碼。");
                            if (!network.IsSupportedPersonal) throw new InvalidOperationException("只有 WPA2／WPA3 個人網絡能在此更新密碼；企業 Wi-Fi 請在 Windows 修改。");
                            string replacement = BuildNewProfileXml(network, password, network.ProfileName);
                            uint replacementReason;
                            CheckProfile(WlanSetProfile(client.Handle, ref id, 0, replacement, null, true, IntPtr.Zero, out replacementReason), replacementReason);
                            return network.ProfileName;
                        }
                        XmlDocument doc = new XmlDocument { XmlResolver = null };
                        doc.LoadXml(Marshal.PtrToStringUni(ptr));
                        XmlNamespaceManager ns = new XmlNamespaceManager(doc.NameTable);
                        ns.AddNamespace("w", "http://www.microsoft.com/networking/WLAN/profile/v1");
                        XmlNode mode = doc.SelectSingleNode("/w:WLANProfile/w:connectionMode", ns);
                        if (mode == null) throw new InvalidOperationException("已存 Wi-Fi 設定缺少 connectionMode，請先在 Windows 啟用自動連線。");
                        if (mode.InnerText != "auto")
                        {
                            if ((flags & 1) != 0) throw new InvalidOperationException("此 Wi-Fi 由公司原則管理，請由管理員啟用自動連線。");
                            mode.InnerText = "auto";
                            uint reason;
                            CheckProfile(WlanSetProfile(client.Handle, ref id, 0, doc.OuterXml, null, true, IntPtr.Zero, out reason), reason);
                        }
                        return network.ProfileName;
                    }
                    finally { if (ptr != IntPtr.Zero) WlanFreeMemory(ptr); }
                }
                string profileName = "NetworkRecovery-" + network.SsidHex;
                string xml = BuildNewProfileXml(network, password, profileName);
                uint profileReason;
                // Flags=0 creates an all-user profile usable by the SYSTEM startup monitor.
                // No overwrite: a same-name profile owned by someone else is preserved.
                CheckProfile(WlanSetProfile(client.Handle, ref id, 0, xml, null, false, IntPtr.Zero, out profileReason), profileReason);
                return profileName;
            }
        }

        // Pure construction: validation and escaping can be tested without changing Windows.
        internal static string BuildNewProfileXml(WifiNetwork network, string password, string profileName)
        {
                if (network == null) throw new ArgumentNullException("network");
                if (!network.IsOpen && !network.IsSupportedPersonal) throw new InvalidOperationException("此網絡使用企業驗證、WEP 或其他安全性。請先在 Windows 完成連線並儲存所有使用者設定，然後重新掃描。");
                ValidateSsidHex(network.SsidHex);
                if (string.IsNullOrEmpty(profileName) || profileName.Length > 255) throw new ArgumentException("Wi-Fi 設定名稱長度無效。");
                XmlConvert.VerifyXmlChars(profileName);
                string keyType = "passPhrase";
                if (!network.IsOpen)
                {
                    if (string.IsNullOrEmpty(password)) throw new ArgumentException("請輸入 Wi-Fi 密碼。");
                    bool isHex = password.Length == 64 && IsHex(password);
                    int length = Encoding.UTF8.GetByteCount(password);
                    if (network.Auth == 7 && isHex) keyType = "networkKey";
                    else if (length < (network.Auth == 9 ? 1 : 8) || length > 63) throw new ArgumentException("WPA2 密碼須為 8–63 個 UTF-8 位元組（或 64 位十六進位金鑰）；WPA3 密碼須為 1–63 個 UTF-8 位元組。");
                    XmlConvert.VerifyXmlChars(password);
                }
                XmlDocument profile = new XmlDocument { XmlResolver = null };
                profile.LoadXml("<WLANProfile xmlns=\"http://www.microsoft.com/networking/WLAN/profile/v1\"><name/><SSIDConfig><SSID><hex/></SSID></SSIDConfig><connectionType>ESS</connectionType><connectionMode>auto</connectionMode><MSM><security><authEncryption><authentication/><encryption/><useOneX>false</useOneX></authEncryption></security></MSM></WLANProfile>");
                XmlNamespaceManager manager = new XmlNamespaceManager(profile.NameTable);
                manager.AddNamespace("w", "http://www.microsoft.com/networking/WLAN/profile/v1");
                profile.SelectSingleNode("/w:WLANProfile/w:name", manager).InnerText = profileName;
                profile.SelectSingleNode("//w:SSID/w:hex", manager).InnerText = network.SsidHex;
                profile.SelectSingleNode("//w:authentication", manager).InnerText = network.IsOpen ? "open" : network.Auth == 9 ? "WPA3SAE" : "WPA2PSK";
                profile.SelectSingleNode("//w:encryption", manager).InnerText = network.IsOpen ? "none" : "AES";
                if (!network.IsOpen)
                {
                    XmlElement shared = profile.CreateElement("sharedKey", manager.LookupNamespace("w"));
                    foreach (string field in new[] { "keyType", "protected", "keyMaterial" })
                    {
                        XmlElement element = profile.CreateElement(field, manager.LookupNamespace("w"));
                        element.InnerText = field == "keyType" ? keyType : field == "protected" ? "false" : password;
                        shared.AppendChild(element);
                    }
                    profile.SelectSingleNode("//w:security", manager).AppendChild(shared);
                }
                return profile.OuterXml;
        }

        public static void Connect(Guid id, string profileName)
        {
            if (string.IsNullOrEmpty(profileName)) throw new ArgumentException("未指定 Wi-Fi 設定名稱。");
            using (WlanClient client = new WlanClient())
            {
                ConnectionParameters parameters = new ConnectionParameters { Mode = 0, Profile = profileName, BssType = 1 };
                Check(WlanConnect(client.Handle, ref id, ref parameters, IntPtr.Zero), "連接 Wi-Fi");
            }
        }

        public static bool WaitForConnection(Guid id, string profileName, int timeoutMs)
        {
            if (string.IsNullOrEmpty(profileName)) throw new ArgumentException("未指定 Wi-Fi 設定名稱。");
            if (timeoutMs < 0) throw new ArgumentOutOfRangeException("timeoutMs");
            Stopwatch timer = Stopwatch.StartNew();
            while (true)
            {
                foreach (WifiNetwork network in GetNetworks(id))
                    if (network.IsConnected && string.Equals(network.ProfileName, profileName, StringComparison.Ordinal)) return true;
                if (timer.ElapsedMilliseconds >= timeoutMs) return false;
                Thread.Sleep((int)Math.Max(0, Math.Min(500, timeoutMs - timer.ElapsedMilliseconds)));
            }
        }

        public static void RestartAdapter(Guid id)
        {
            // Only a parsed GUID enters this fixed command; names/SSID/password never enter a shell.
            string script = "$ErrorActionPreference='Stop'; $a = Get-NetAdapter -IncludeHidden | Where-Object { [guid]$_.InterfaceGuid -eq [guid]'" + id.ToString("D") + "' }; if (@($a).Count -ne 1) { throw 'Selected network adapter no longer exists.' }; try { $a | Disable-NetAdapter -Confirm:$false -ErrorAction Stop; Start-Sleep -Seconds 2 } finally { $a | Enable-NetAdapter -Confirm:$false -ErrorAction Stop }";
            RunPowerShell(script, 60000);
            // Enabling the NIC does not necessarily enable the software Wi-Fi radio.
            Exception last = null;
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try { EnableRadio(id); return; }
                catch (Win32Exception ex) { last = ex; Thread.Sleep(1000); }
            }
            // Wired adapters have no WLAN interface; the NIC restart already succeeded.
            foreach (AdapterInfo adapter in GetAdapters())
                if (new Guid(adapter.Id) == id && !adapter.IsWireless) return;
            if (last != null) throw new InvalidOperationException("網卡已啟用，但無法開啟 Wi-Fi 無線電。請檢查飛航模式、實體開關或驅動程式。", last);
        }

        private static void EnableRadio(Guid id)
        {
            using (WlanClient client = new WlanClient())
            {
                IntPtr ptr = IntPtr.Zero;
                uint size, type;
                try
                {
                    Check(WlanQueryInterface(client.Handle, ref id, 4, IntPtr.Zero, out size, out ptr, out type), "讀取 Wi-Fi 無線電");
                    int count = Marshal.ReadInt32(ptr);
                    if (count < 0 || count > 64 || size < 4 + count * 12) throw new InvalidOperationException("無線電狀態格式錯誤。");
                    for (int i = 0; i < count; i++)
                    {
                        PhyRadioState radio = (PhyRadioState)Marshal.PtrToStructure(IntPtr.Add(ptr, 4 + i * 12), typeof(PhyRadioState));
                        radio.SoftwareState = 1;
                        Check(WlanSetInterface(client.Handle, ref id, 4, 12, ref radio, IntPtr.Zero), "開啟 Wi-Fi 無線電");
                    }
                }
                finally { if (ptr != IntPtr.Zero) WlanFreeMemory(ptr); }
            }
        }

        private static string RunPowerShell(string script, int timeout)
        {
            ProcessStartInfo start = new ProcessStartInfo(Environment.GetFolderPath(Environment.SpecialFolder.System) + "\\WindowsPowerShell\\v1.0\\powershell.exe",
                "-NoLogo -NoProfile -NonInteractive -OutputFormat Text -ExecutionPolicy Bypass -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes("[Console]::OutputEncoding=[System.Text.Encoding]::UTF8; $ProgressPreference='SilentlyContinue'; trap { [Console]::Error.WriteLine($_.Exception.Message); exit 1 }; " + script)));
            start.UseShellExecute = false; start.CreateNoWindow = true;
            start.RedirectStandardOutput = true; start.RedirectStandardError = true;
            start.StandardOutputEncoding = Encoding.UTF8; start.StandardErrorEncoding = Encoding.UTF8;
            StringBuilder output = new StringBuilder(), error = new StringBuilder();
            using (Process process = new Process { StartInfo = start })
            {
                process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e) { if (e.Data != null) output.AppendLine(e.Data); };
                process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e) { if (e.Data != null) error.AppendLine(e.Data); };
                process.Start(); process.BeginOutputReadLine(); process.BeginErrorReadLine();
                if (!process.WaitForExit(timeout))
                {
                    // Do not kill a disable/enable sequence: its finally block must run.
                    throw new TimeoutException("Windows 網卡操作尚未完成，請稍候再檢查網卡狀態。");
                }
                process.WaitForExit();
                if (process.ExitCode != 0) throw new InvalidOperationException("Windows 網卡操作失敗：" + error.ToString().Trim());
                return output.ToString();
            }
        }

        private static bool IsHex(string value) { foreach (char c in value) if (!Uri.IsHexDigit(c)) return false; return true; }
        private static void ValidateSsidHex(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 64 || value.Length % 2 != 0 || !IsHex(value)) throw new ArgumentException("無效的 Wi-Fi SSID。");
        }
        private static void Check(uint code, string action)
        {
            if (code == 0) return;
            string hint = code == 5 ? " 請以系統管理員執行，並在 Windows 設定 → 隱私權與安全性 → 位置，開啟位置服務及桌面應用程式存取位置。公司原則也可能禁止此操作。" : code == 1062 ? " 請在 Windows 服務中啟動 WLAN AutoConfig。" : "";
            throw new Win32Exception((int)code, action + "失敗：" + new Win32Exception((int)code).Message + hint);
        }
        private static void CheckProfile(uint code, uint reason)
        {
            if (code == 0) return;
            StringBuilder text = new StringBuilder(1024);
            WlanReasonCodeToString(reason, 1024, text, IntPtr.Zero);
            throw new Win32Exception((int)code, "儲存 Wi-Fi 設定失敗：" + text + "（Windows 錯誤 " + code.ToString(CultureInfo.InvariantCulture) + "）。請確認密碼格式、安全性支援與公司原則；已有同名設定時請先在 Windows 連線，再重新掃描。");
        }

        private sealed class WlanClient : IDisposable
        {
            public IntPtr Handle;
            public WlanClient() { uint version; Check(WlanOpenHandle(2, IntPtr.Zero, out version, out Handle), "開啟 Windows Wi-Fi 服務"); }
            public void Dispose() { if (Handle != IntPtr.Zero) { WlanCloseHandle(Handle, IntPtr.Zero); Handle = IntPtr.Zero; } }
        }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WlanInterfaceInfo { public Guid Id; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Description; public uint State; }
        [StructLayout(LayoutKind.Sequential)]
        private struct Dot11Ssid { public uint Length; [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] Bytes; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WlanAvailableNetwork
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string ProfileName;
            public Dot11Ssid Ssid; public uint BssType, BssidCount;
            [MarshalAs(UnmanagedType.Bool)] public bool Connectable;
            public uint NotConnectableReason, PhyCount;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)] public uint[] PhyTypes;
            [MarshalAs(UnmanagedType.Bool)] public bool MorePhyTypes;
            public uint Signal;
            [MarshalAs(UnmanagedType.Bool)] public bool SecurityEnabled;
            public uint Auth, Cipher, Flags, Reserved;
        }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct ConnectionParameters { public uint Mode; [MarshalAs(UnmanagedType.LPWStr)] public string Profile; public IntPtr Ssid, DesiredBssidList; public uint BssType, Flags; }
        [StructLayout(LayoutKind.Sequential)]
        private struct PhyRadioState { public uint Index, SoftwareState, HardwareState; }
        [DllImport("wlanapi.dll")] private static extern uint WlanOpenHandle(uint version, IntPtr reserved, out uint negotiated, out IntPtr handle);
        [DllImport("wlanapi.dll")] private static extern uint WlanCloseHandle(IntPtr handle, IntPtr reserved);
        [DllImport("wlanapi.dll")] private static extern void WlanFreeMemory(IntPtr memory);
        [DllImport("wlanapi.dll")] private static extern uint WlanEnumInterfaces(IntPtr handle, IntPtr reserved, out IntPtr list);
        [DllImport("wlanapi.dll")] private static extern uint WlanScan(IntPtr handle, ref Guid id, IntPtr ssid, IntPtr ie, IntPtr reserved);
        [DllImport("wlanapi.dll")] private static extern uint WlanGetAvailableNetworkList(IntPtr handle, ref Guid id, uint flags, IntPtr reserved, out IntPtr list);
        [DllImport("wlanapi.dll", CharSet = CharSet.Unicode)] private static extern uint WlanGetProfile(IntPtr handle, ref Guid id, string name, IntPtr reserved, out IntPtr xml, ref uint flags, out uint access);
        [DllImport("wlanapi.dll", CharSet = CharSet.Unicode)] private static extern uint WlanSetProfile(IntPtr handle, ref Guid id, uint flags, string xml, string security, [MarshalAs(UnmanagedType.Bool)] bool overwrite, IntPtr reserved, out uint reason);
        [DllImport("wlanapi.dll", CharSet = CharSet.Unicode)] private static extern uint WlanConnect(IntPtr handle, ref Guid id, ref ConnectionParameters parameters, IntPtr reserved);
        [DllImport("wlanapi.dll")] private static extern uint WlanQueryInterface(IntPtr handle, ref Guid id, uint opcode, IntPtr reserved, out uint size, out IntPtr data, out uint type);
        [DllImport("wlanapi.dll")] private static extern uint WlanSetInterface(IntPtr handle, ref Guid id, uint opcode, uint size, ref PhyRadioState data, IntPtr reserved);
        [DllImport("wlanapi.dll", CharSet = CharSet.Unicode)] private static extern uint WlanReasonCodeToString(uint reason, uint size, StringBuilder text, IntPtr reserved);
    }
}
