using System;
using System.Collections.Generic;
using System.ComponentModel;
using Microsoft.Win32.SafeHandles;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Serialization;

namespace NetworkRecovery
{
    public static class RecoveryEngine
    {
        [DllImport("kernel32.dll")] private static extern ulong GetTickCount64();

        public static void Run()
        {
            bool created;
            using (Mutex mutex = new Mutex(true, @"Global\NetworkRecoveryMonitorV1", out created))
            {
                if (!created) return;
                try { RunMonitor(); }
                catch (Exception ex) { WriteLog("Monitor stopped: " + ex.GetType().Name); }
                finally { mutex.ReleaseMutex(); }
            }
        }

        private static void RunMonitor()
        {
            AppConfig config = AppInstaller.LoadConfig();
            Guid adapterId;
            if (config == null || !Guid.TryParse(config.AdapterId, out adapterId) ||
                config.OfflineSeconds < 5 || config.RepairSeconds < 5 || config.StartupGraceSeconds < 0)
                throw new InvalidDataException("Missing or invalid monitor configuration.");
            string bootId = BootClock.Id;
            double graceEnd = StartupPolicy.GraceDeadline(BootClock.Seconds, config.StartupGraceSeconds);
            double? failureSince = null, repairedAt = null;
            bool everOnline = false, targetAbsent = false;
            double nextScan = 0;
            int candidateIndex = 0;
            string observationRevision = "";
            string target = config.WifiSsidHex;
            RestartBudget budget = LoadRestartBudget(bootId);
            bool dirty = !SaveRestartBudget(budget, bootId);
            WriteLog("Monitor started; offline=" + config.OfflineSeconds + "s, recovery=" + config.RepairSeconds + "s.");
            while (true)
            {
                bool online = InternetAvailable(3000);
                double now = BootClock.Seconds;
                try
                {
                    using (ActionLease action = new ActionLease(1000))
                    {
                        budget = LoadRestartBudget(bootId);
                        PauseState pause = MonitorControl.Read();
                        if (pause.Hours > 0 && pause.BootId != bootId)
                        {
                            pause.Anchor(bootId, BootClock.Seconds, DateTime.UtcNow);
                            StateFile.Write(AppPaths.ControlFile, pause);
                        }
                        if (observationRevision != pause.ObservationRevision)
                        {
                            observationRevision = pause.ObservationRevision;
                            failureSince = null; repairedAt = null; graceEnd = 0;
                        }
                        bool paused = MonitorControl.IsPaused(pause);
                        if (!paused && budget.ResumeRevision != pause.Revision)
                        {
                            budget.ObserveInternet(true); // Explicit D0012 resume resets the window.
                            budget.ResumeRevision = pause.Revision;
                            dirty = true;
                            failureSince = null; repairedAt = null; graceEnd = 0;
                        }
                        if (online)
                        {
                            if (failureSince.HasValue) WriteLog("Internet connection restored; recovery cancelled.");
                            everOnline = true; failureSince = null; repairedAt = null; nextScan = 0; candidateIndex = 0;
                            if (budget.HasHistory) { budget.ObserveInternet(true); dirty = true; }
                        }
                        if (dirty) dirty = !SaveRestartBudget(budget, bootId);
                        if (paused)
                        {
                            failureSince = null; repairedAt = null;
                            MonitorControl.Publish("已暫停", online);
                        }
                        else if (online) MonitorControl.Publish("正常", true);
                        else
                        {
                            if (config.ReconnectWifi && now >= graceEnd && now >= nextScan)
                            {
                                bool previousAbsent = targetAbsent;
                                targetAbsent = false;
                                try
                                {
                                    NetworkManager.Scan(adapterId);
                                    Thread.Sleep(2000);
                                    List<WifiNetwork> networks = NetworkManager.GetNetworks(adapterId);
                                    if (String.IsNullOrEmpty(target))
                                    {
                                        try { target = NetworkManager.GetProfileSsid(adapterId, config.WifiProfileName); }
                                        catch (Exception ex) { WriteLog("Primary profile identity unavailable: " + ex.GetType().Name); }
                                    }
                                    targetAbsent = WifiRecoveryPolicy.TargetAbsent(networks, target, config.WifiProfileName);
                                    List<string> candidates = WifiRecoveryPolicy.Candidates(networks, config.WifiProfileName);
                                    if (previousAbsent && !targetAbsent)
                                    {
                                        candidateIndex = 0;
                                        failureSince = BootClock.Seconds; repairedAt = null; graceEnd = 0;
                                    }
                                    if (candidates.Count > 0)
                                    {
                                        string profile = candidates[candidateIndex++ % candidates.Count];
                                        NetworkManager.Connect(adapterId, profile); // Existing Windows credentials only.
                                    }
                                }
                                catch (Exception ex)
                                {
                                    // A failed connection must not undo a successful absent-target scan.
                                    WriteLog("Wi-Fi scan/reconnect failed: " + ex.GetType().Name);
                                }
                                nextScan = BootClock.Seconds + 30;
                            }
                            now = BootClock.Seconds;
                            if (targetAbsent)
                            {
                                failureSince = null; repairedAt = null;
                                MonitorControl.Publish("等待目標 Wi-Fi（持續掃描）", false);
                            }
                            else
                            {
                                if (!failureSince.HasValue) failureSince = !everOnline && graceEnd > 0 ? 0 : now;
                                if (!repairedAt.HasValue && StartupPolicy.ShouldRepair(now, failureSince.Value,
                                    graceEnd, everOnline, config.OfflineSeconds))
                                {
                                    MonitorControl.Publish("修復中", false);
                                    try
                                    {
                                        NetworkManager.RestartAdapter(adapterId);
                                        if (config.ReconnectWifi && !String.IsNullOrWhiteSpace(config.WifiProfileName))
                                            NetworkManager.Connect(adapterId, config.WifiProfileName);
                                    }
                                    catch (Exception ex) { WriteLog("Adapter repair failed: " + ex.GetType().Name); }
                                    repairedAt = BootClock.Seconds;
                                }
                                else if (repairedAt.HasValue && now - repairedAt.Value >= config.RepairSeconds)
                                {
                                    // Recheck after scan/connect operations; Wi-Fi alone never clears the budget.
                                    if (InternetAvailable(3000))
                                    {
                                        budget.ObserveInternet(true); dirty = true;
                                        failureSince = null; repairedAt = null; everOnline = true;
                                    }
                                    else
                                    {
                                        RequestRestart(budget, bootId, "修復後 " + config.RepairSeconds + " 秒仍無外網");
                                        failureSince = BootClock.Seconds; repairedAt = null; graceEnd = 0;
                                    }
                                }
                                else MonitorControl.Publish(repairedAt.HasValue ? "修復後觀察中" : "無外網，等待修復", false);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Invalid control state cannot accidentally resume destructive actions.
                    WriteLog("Automatic actions deferred: " + ex.GetType().Name);
                    MonitorControl.Publish("等待操作完成或檢查控制狀態", online);
                }
                Thread.Sleep(5000);
            }
        }

        internal static RestartBudget LoadRestartBudget(string bootId)
        {
            try
            {
                AppInstaller.PrepareRestartHistory();
                return new RestartBudget(RestartHistoryFile.Read(AppPaths.RestartHistoryFile), bootId, BootClock.Seconds, DateTime.UtcNow);
            }
            catch (Exception ex)
            {
                WriteLog("Restart history unavailable: " + ex.GetType().Name);
                return RestartBudget.Blocked(BootClock.Seconds);
            }
        }

        internal static bool RequestRestart(RestartBudget budget, string bootId, string reason)
        {
            try
            {
                return RestartRequest.Execute(delegate {
                    if (budget.TryReserve(BootClock.Seconds)) return true;
                    WriteLog("已達 4 小時重啟上限，改為修復網卡。"); return false;
                }, delegate { return SaveRestartBudget(budget, bootId); },
                delegate { WriteRequiredLog("準備重新啟動：" + reason); },
                delegate {
                    int result = AppInstaller.ExecuteSystem("shutdown.exe", "/r /f /t 0", 10000);
                    if (result != 0) WriteLog("Windows restart request failed, exit=" + result + ".");
                    return result;
                });
            }
            catch (Exception ex) { WriteLog("Restart deferred: " + ex.GetType().Name); return false; }
        }

        internal static void WriteRequiredLog(string message)
        {
            AppInstaller.ProtectStatePath(AppPaths.LogFile);
            using (FileStream stream = new FileStream(AppPaths.LogFile, FileMode.Append, FileAccess.Write, FileShare.Read))
            {
                byte[] bytes = Encoding.UTF8.GetBytes(DateTime.UtcNow.ToString("o") + " " + message + Environment.NewLine);
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
        }

        private static bool SaveRestartBudget(RestartBudget budget, string bootId)
        {
            try
            {
                if (String.IsNullOrEmpty(bootId)) return false;
                AppInstaller.PrepareRestartHistory();
                RestartHistoryFile.Write(AppPaths.RestartHistoryFile,
                    budget.Snapshot(bootId, GetTickCount64() / 1000.0, DateTime.UtcNow));
                return true;
            }
            catch (Exception ex)
            {
                WriteLog("Cannot persist restart history; restart denied: " + ex.GetType().Name);
                return false;
            }
        }

        public static bool InternetAvailable(int timeoutMs)
        {
            if (timeoutMs <= 0) return false;
            using (CancellationTokenSource cancellation = new CancellationTokenSource())
            {
                Stopwatch timer = Stopwatch.StartNew();
                try
                {
                    List<Task<bool>> pending = new List<Task<bool>>();
                    for (int index = 0; index < 3; index++)
                    {
                        int probeIndex = index;
                        pending.Add(Task.Run(delegate { return ProbeAsync(probeIndex, cancellation.Token); }));
                    }
                    while (pending.Count > 0)
                    {
                        long remaining = timeoutMs - timer.ElapsedMilliseconds;
                        if (remaining <= 0) return false;
                        int completed = Task.WaitAny(pending.ToArray(), (int)remaining);
                        if (completed < 0) return false;
                        Task<bool> result = pending[completed];
                        pending.RemoveAt(completed);
                        if (result.Status == TaskStatus.RanToCompletion && result.Result) return true;
                    }
                    return false;
                }
                catch { return false; }
                finally { try { cancellation.Cancel(); } catch { } }
            }
        }

        private static async Task<bool> ProbeAsync(int index, CancellationToken token)
        {
            try
            {
                string[] urls = { "http://www.msftconnecttest.com/connecttest.txt",
                    "https://www.cloudflare.com/cdn-cgi/trace", "https://www.google.com/generate_204" };
                using (HttpClientHandler handler = new HttpClientHandler())
                {
                    handler.AllowAutoRedirect = false;
                    handler.UseCookies = false;
                    using (HttpClient client = new HttpClient(handler))
                    {
                        client.Timeout = Timeout.InfiniteTimeSpan;
                        client.MaxResponseContentBufferSize = 32768;
                        using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, urls[index]))
                        {
                            request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };
                            using (HttpResponseMessage response = await client.SendAsync(request,
                                HttpCompletionOption.ResponseContentRead, token).ConfigureAwait(false))
                            {
                                if (index == 2) return response.StatusCode == HttpStatusCode.NoContent;
                                if (response.StatusCode != HttpStatusCode.OK) return false;
                                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                                if (index == 0) return body == "Microsoft Connect Test";
                                foreach (string line in body.Split('\n'))
                                {
                                    if (!line.StartsWith("ip=", StringComparison.Ordinal)) continue;
                                    IPAddress address;
                                    return IPAddress.TryParse(line.Substring(3).Trim(), out address);
                                }
                            }
                        }
                    }
                }
            }
            catch { }
            return false;
        }

        internal static void WriteLog(string message)
        {
            try
            {
                if (!Directory.Exists(AppPaths.Data)) return;
                if (File.Exists(AppPaths.LogFile) && new FileInfo(AppPaths.LogFile).Length > 1048576)
                {
                    string previous = AppPaths.LogFile + ".1";
                    if (File.Exists(previous)) File.Delete(previous);
                    File.Move(AppPaths.LogFile, previous);
                }
                File.AppendAllText(AppPaths.LogFile, DateTime.UtcNow.ToString("o") + " " + message + Environment.NewLine);
            }
            catch { /* A logging error must not stop recovery. */ }
        }
    }

    public static class AppInstaller
    {
        private const string TaskName = "NetworkRecovery Monitor";
        private const string LegacyTaskName = "Codex Network Recovery";
        private const string TrayTaskName = "NetworkRecovery Tray";
        private static string InstalledExe { get { return Path.Combine(AppPaths.Root, "NetworkRecovery.exe"); } }

        public static AppConfig LoadConfig()
        {
            ValidateInstallPaths();
            if (!File.Exists(AppPaths.ConfigFile)) return null;
            try
            {
                using (FileStream stream = File.OpenRead(AppPaths.ConfigFile))
                using (XmlReader reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null }))
                    return (AppConfig)new XmlSerializer(typeof(AppConfig)).Deserialize(reader);
            }
            catch (Exception ex)
            {
                throw new InvalidDataException("無法讀取監控設定，請重新選擇網卡並按下儲存及啟用。", ex);
            }
        }

        public static void Install(AppConfig config)
        {
            RequireAdministrator();
            bool acquired;
            using (Mutex setup = new Mutex(true, @"Global\NetworkRecoverySetupV1", out acquired))
            {
                if (!acquired) throw new InvalidOperationException("另一個安裝程序正在執行，請稍後再試。");
                try { using (ActionLease action = new ActionLease(1000)) InstallCore(config); }
                finally { setup.ReleaseMutex(); }
            }
        }

        private static void InstallCore(AppConfig config)
        {
            Guid id;
            if (config == null || !Guid.TryParse(config.AdapterId, out id) || !NetworkManager.IsAdapterAvailable(id))
                throw new InvalidOperationException("所選網卡已無法使用，請重新選擇。");
            if (config.OfflineSeconds < 5 || config.RepairSeconds < 5 || config.StartupGraceSeconds < 0)
                throw new InvalidOperationException("修復時間設定無效。");
            ValidateInstallPaths();
            TaskSnapshot previous = ReadTask(TaskName);
            TaskSnapshot legacy = ReadTask(LegacyTaskName);
            TaskSnapshot tray = ReadTask(TrayTaskName);
            CreateProtectedDirectory(AppPaths.Root);
            CreateProtectedDirectory(AppPaths.Data);
            // Existing files can retain explicit write permissions after File.Copy overwrites.
            ProtectExistingFile(InstalledExe);
            ProtectExistingFile(AppPaths.ConfigFile);
            ProtectExistingFile(AppPaths.LogFile);
            ProtectExistingFile(AppPaths.LogFile + ".1");
            ProtectExistingFile(AppPaths.RestartHistoryFile);
            ProtectExistingFile(AppPaths.ControlFile);
            ProtectExistingFile(AppPaths.StatusFile);
            ValidateInstallPaths();
            string suffix = ".install-" + Guid.NewGuid().ToString("N");
            string stagedExe = InstalledExe + suffix;
            string savedExe = InstalledExe + suffix + ".backup";
            string stagedConfig = AppPaths.ConfigFile + suffix;
            string savedConfig = AppPaths.ConfigFile + suffix + ".backup";
            string taskXml = Path.Combine(AppPaths.Data, "task" + suffix + ".xml");
            string savedTask = taskXml + ".backup";
            string savedLegacyTask = taskXml + ".legacy-backup";
            string trayXml = taskXml + ".tray";
            string savedTrayTask = taskXml + ".tray-backup";
            bool copyExe = !String.Equals(Path.GetFullPath(Assembly.GetEntryAssembly().Location),
                Path.GetFullPath(InstalledExe), StringComparison.OrdinalIgnoreCase);
            // Settings opened by the tray can belong to its scheduler process tree.
            // Re-saving settings from the installed binary must not end its own parent task.
            bool updateTray = copyExe || !tray.Exists;
            bool hadExe = File.Exists(InstalledExe), hadConfig = File.Exists(AppPaths.ConfigFile);
            bool touched = false, legacyTouched = false, exeChanged = false, configChanged = false, preserveBackups = false;
            try
            {
                // Stage and validate all files before interrupting either existing monitor.
                if (copyExe) File.Copy(Assembly.GetEntryAssembly().Location, stagedExe, true);
                if (hadExe && copyExe) File.Copy(InstalledExe, savedExe, true);
                if (hadConfig) File.Copy(AppPaths.ConfigFile, savedConfig, true);
                if (previous.Exists) File.WriteAllText(savedTask, previous.Xml, Encoding.Unicode);
                if (legacy.Exists) File.WriteAllText(savedLegacyTask, legacy.Xml, Encoding.Unicode);
                if (tray.Exists) File.WriteAllText(savedTrayTask, tray.Xml, Encoding.Unicode);
                using (FileStream stream = File.Create(stagedConfig))
                    new XmlSerializer(typeof(AppConfig)).Serialize(stream, config);
                File.WriteAllText(taskXml, BuildTaskXml(false), Encoding.Unicode);
                ValidateTaskXml(taskXml);
                string userSid = WindowsIdentity.GetCurrent().User.Value;
                File.WriteAllText(trayXml, BuildTrayTaskXml(userSid, false), Encoding.Unicode);
                ValidateTaskXml(trayXml, TrayTaskName, userSid, 3);

                touched = true;
                StopTask(TaskName);
                if (updateTray) StopTask(TrayTaskName);
                if (copyExe)
                {
                    exeChanged = true;
                    CopyWithRetry(stagedExe, InstalledExe);
                }
                configChanged = true;
                File.Copy(stagedConfig, AppPaths.ConfigFile, true);
                RequireSuccess(ExecuteSystem("schtasks.exe", "/Create /TN \"" + TaskName + "\" /XML \"" + taskXml + "\" /F", 20000), "無法建立監控排程");
                if (updateTray) RequireSuccess(ExecuteSystem("schtasks.exe", "/Create /TN \"" + TrayTaskName + "\" /XML \"" + trayXml + "\" /F", 20000), "無法建立系統匣排程");
                // The replacement is registered disabled, so legacy remains the sole active
                // monitor until all setup work is ready. Only the final handover stops it.
                legacyTouched = true;
                StopTask(LegacyTaskName);
                RequireSuccess(ExecuteSystem("schtasks.exe", "/Change /TN \"" + TaskName + "\" /ENABLE", 15000), "無法啟用監控排程");
                RequireSuccess(ExecuteSystem("schtasks.exe", "/Run /TN \"" + TaskName + "\"", 15000), "無法啟動監控排程");
                WaitForRunning();
                if (updateTray)
                {
                    RequireSuccess(ExecuteSystem("schtasks.exe", "/Change /TN \"" + TrayTaskName + "\" /ENABLE", 15000), "無法啟用系統匣排程");
                    RequireSuccess(ExecuteSystem("schtasks.exe", "/Run /TN \"" + TrayTaskName + "\"", 15000), "無法啟動系統匣");
                    WaitForRunning(TrayTaskName);
                }
                RecoveryEngine.WriteLog("Recovery task installed and running; previous monitor disabled.");
            }
            catch (Exception error)
            {
                if (!touched) throw;
                List<string> failures = new List<string>();
                TryRestore(delegate { StopTask(TaskName); }, "停止新監控", failures);
                if (updateTray) TryRestore(delegate { StopTask(TrayTaskName); }, "停止新系統匣", failures);
                if (exeChanged) TryRestore(delegate
                {
                    if (hadExe) CopyWithRetry(savedExe, InstalledExe);
                    else File.Delete(InstalledExe);
                }, "還原程式", failures);
                if (configChanged) TryRestore(delegate
                {
                    if (hadConfig) File.Copy(savedConfig, AppPaths.ConfigFile, true);
                    else File.Delete(AppPaths.ConfigFile);
                }, "還原設定", failures);
                TryRestore(delegate { RestoreTask(TaskName, previous, taskXml); }, "還原原有監控", failures);
                if (legacyTouched) TryRestore(delegate { RestoreTask(LegacyTaskName, legacy, taskXml, false); }, "還原舊版監控", failures);
                if (updateTray) TryRestore(delegate { RestoreTask(TrayTaskName, tray, trayXml); }, "還原系統匣", failures);
                preserveBackups = failures.Count > 0;
                RecoveryEngine.WriteLog("Installation failed; rollback " + (preserveBackups ? "incomplete." : "completed."));
                throw new InvalidOperationException("安裝失敗：" + error.Message +
                    (preserveBackups ? "；部分還原失敗：" + String.Join("、", failures) + "。備份保留於程式及設定資料夾。" : "；已還原原有監控與設定。"), error);
            }
            finally
            {
                foreach (string path in new[] { stagedExe, stagedConfig, taskXml, trayXml }) SafeDelete(path);
                if (!preserveBackups)
                {
                    SafeDelete(savedExe); SafeDelete(savedConfig);
                    SafeDelete(savedTask); SafeDelete(savedLegacyTask);
                    SafeDelete(savedTrayTask);
                }
            }
        }

        public static void Stop()
        {
            RequireAdministrator();
            using (ActionLease action = new ActionLease(1000)) StopTask(TaskName);
        }

        public static void Uninstall()
        {
            Stop();
            StopTask(TrayTaskName);
            if (ReadTask(TrayTaskName).Exists)
                RequireSuccess(ExecuteSystem("schtasks.exe", "/Delete /TN \"" + TrayTaskName + "\" /F", 15000), "無法移除系統匣排程");
            int result = ExecuteSystem("schtasks.exe", "/Delete /TN \"" + TaskName + "\" /F", 15000);
            if (result != 0 && IsInstalled()) throw new InvalidOperationException("Could not remove recovery task.");
            // Retain config and logs for review. Never delete the running executable.
            RecoveryEngine.WriteLog("Recovery task removed; configuration and logs retained.");
        }

        public static bool IsInstalled()
        {
            return ExecuteSystem("schtasks.exe", "/Query /TN \"" + TaskName + "\"", 10000) == 0;
        }

        public static string GetStatus()
        {
            try
            {
                TaskSnapshot task = ReadTask(TaskName);
                if (!task.Exists) return "尚未安裝";
                if (!task.Enabled) return "監控已停止";
                return task.Running ? MonitorControl.Describe() : "監控已啟用（等待執行）";
            }
            catch { return "無法讀取排程狀態"; }
        }

        private sealed class TaskSnapshot
        {
            public bool Exists, Enabled, Running;
            public int State;
            public string Xml;
        }

        private static TaskSnapshot ReadTask(string name)
        {
            dynamic service = null, folder = null, task = null;
            try
            {
                service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service"));
                service.Connect();
                folder = service.GetFolder("\\");
                try { task = folder.GetTask(name); }
                // GetTask can map the COM missing-task HRESULT to FileNotFoundException.
                catch (Exception ex)
                {
                    if (ex.HResult == unchecked((int)0x80070002) || ex.HResult == unchecked((int)0x8004130F))
                        return new TaskSnapshot();
                    throw;
                }
                int state = (int)task.State;
                return new TaskSnapshot { Exists = true, Enabled = (bool)task.Enabled,
                    Running = state == 4 || state == 2, State = state, Xml = (string)task.Xml };
            }
            finally
            {
                if (task != null) Marshal.FinalReleaseComObject(task);
                if (folder != null) Marshal.FinalReleaseComObject(folder);
                if (service != null) Marshal.FinalReleaseComObject(service);
            }
        }

        private static void ValidateTaskXml(string path, string name = TaskName, string user = "SYSTEM", int logon = 5)
        {
            dynamic service = null, folder = null, result = null;
            try
            {
                service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service"));
                service.Connect();
                folder = service.GetFolder("\\");
                // TASK_VALIDATE_ONLY (1) checks schema and settings without registering a task.
                result = folder.RegisterTask(name, File.ReadAllText(path), 1, user, null, logon, null);
            }
            finally
            {
                if (result != null) Marshal.FinalReleaseComObject(result);
                if (folder != null) Marshal.FinalReleaseComObject(folder);
                if (service != null) Marshal.FinalReleaseComObject(service);
            }
        }

        private static void StopTask(string name)
        {
            TaskSnapshot previous = ReadTask(name);
            if (!previous.Exists) return;
            RequireSuccess(ExecuteSystem("schtasks.exe", "/Change /TN \"" + name + "\" /DISABLE", 15000), "無法停用監控排程");
            if (!ReadTask(name).Running) return;
            RequireSuccess(ExecuteSystem("schtasks.exe", "/End /TN \"" + name + "\"", 15000), "無法結束監控排程");
            Stopwatch wait = Stopwatch.StartNew();
            while (ReadTask(name).Running)
            {
                if (wait.ElapsedMilliseconds > 10000) throw new IOException("監控程序未能結束，未更新程式檔案。");
                Thread.Sleep(200);
            }
        }

        private static void WaitForRunning(string name = TaskName)
        {
            Stopwatch wait = Stopwatch.StartNew();
            while (wait.ElapsedMilliseconds < 10000)
            {
                if (ReadTask(name).State == 4)
                {
                    Thread.Sleep(1200);
                    if (ReadTask(name).State == 4) return;
                }
                Thread.Sleep(200);
            }
            throw new InvalidOperationException("監控程序啟動後沒有持續執行。");
        }

        private static void RestoreTask(string name, TaskSnapshot previous, string xmlPath, bool restoreDefinition = true)
        {
            if (!previous.Exists)
            {
                if (ReadTask(name).Exists)
                    RequireSuccess(ExecuteSystem("schtasks.exe", "/Delete /TN \"" + name + "\" /F", 15000), "無法移除未完成的排程");
                return;
            }
            if (restoreDefinition)
            {
                // Restore disabled first to prevent its trigger racing file rollback.
                XmlDocument xml = new XmlDocument { XmlResolver = null };
                xml.LoadXml(previous.Xml);
                XmlNamespaceManager ns = new XmlNamespaceManager(xml.NameTable);
                ns.AddNamespace("t", "http://schemas.microsoft.com/windows/2004/02/mit/task");
                XmlNode enabled = xml.SelectSingleNode("/t:Task/t:Settings/t:Enabled", ns);
                if (enabled == null) throw new InvalidDataException("原有排程缺少 Enabled 設定。");
                enabled.InnerText = "false";
                File.WriteAllText(xmlPath, xml.OuterXml, Encoding.Unicode);
                RequireSuccess(ExecuteSystem("schtasks.exe", "/Create /TN \"" + name + "\" /XML \"" + xmlPath + "\" /F", 20000), "無法還原排程定義");
            }
            bool shouldRun = previous.Running && !ReadTask(name).Running;
            RequireSuccess(ExecuteSystem("schtasks.exe", "/Change /TN \"" + name + "\" " +
                (previous.Enabled || shouldRun ? "/ENABLE" : "/DISABLE"), 15000), "無法還原排程狀態");
            if (shouldRun)
                RequireSuccess(ExecuteSystem("schtasks.exe", "/Run /TN \"" + name + "\"", 15000), "無法重新啟動原有監控");
            if (!previous.Enabled && shouldRun)
                RequireSuccess(ExecuteSystem("schtasks.exe", "/Change /TN \"" + name + "\" /DISABLE", 15000), "無法還原停用狀態");
        }

        private static void CopyWithRetry(string source, string destination)
        {
            Stopwatch wait = Stopwatch.StartNew();
            while (true)
            {
                try { File.Copy(source, destination, true); return; }
                catch (IOException)
                {
                    if (wait.ElapsedMilliseconds >= 10000) throw;
                    Thread.Sleep(250);
                }
            }
        }

        private static void RequireSuccess(int exit, string message)
        {
            if (exit != 0) throw new InvalidOperationException(message + " (" + exit + ")");
        }

        private static void TryRestore(Action action, string label, List<string> failures)
        {
            try { action(); }
            catch { failures.Add(label); }
        }

        private static void SafeDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }

        private static string BuildTaskXml(bool enabled)
        {
            string command = System.Security.SecurityElement.Escape(InstalledExe);
            string working = System.Security.SecurityElement.Escape(AppPaths.Root);
            string start = DateTime.Now.AddSeconds(10).ToString("yyyy-MM-ddTHH:mm:ss");
            return "<?xml version=\"1.0\" encoding=\"UTF-16\"?>" +
                "<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">" +
                "<RegistrationInfo><Description>Recover the user-selected network adapter after a sustained Internet outage.</Description></RegistrationInfo>" +
                "<Triggers><BootTrigger><Enabled>true</Enabled></BootTrigger><CalendarTrigger>" +
                "<Repetition><Interval>PT1M</Interval><Duration>P1D</Duration><StopAtDurationEnd>false</StopAtDurationEnd></Repetition>" +
                "<StartBoundary>" + start + "</StartBoundary><Enabled>true</Enabled><ScheduleByDay><DaysInterval>1</DaysInterval></ScheduleByDay>" +
                "</CalendarTrigger></Triggers><Principals><Principal id=\"System\"><UserId>S-1-5-18</UserId>" +
                "<RunLevel>HighestAvailable</RunLevel></Principal></Principals>" +
                "<Settings><MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy><DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>" +
                "<StopIfGoingOnBatteries>false</StopIfGoingOnBatteries><AllowHardTerminate>true</AllowHardTerminate>" +
                "<StartWhenAvailable>true</StartWhenAvailable><RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>" +
                "<AllowStartOnDemand>true</AllowStartOnDemand><Enabled>" + (enabled ? "true" : "false") + "</Enabled><Hidden>false</Hidden>" +
                "<ExecutionTimeLimit>PT0S</ExecutionTimeLimit><Priority>7</Priority></Settings>" +
                "<Actions Context=\"System\"><Exec><Command>" + command + "</Command><Arguments>--monitor</Arguments>" +
                "<WorkingDirectory>" + working + "</WorkingDirectory></Exec></Actions></Task>";
        }

        internal static string BuildTrayTaskXml(string sid, bool enabled)
        {
            string xml = BuildTaskXml(enabled);
            int start = xml.IndexOf("<Triggers>", StringComparison.Ordinal);
            int end = xml.IndexOf("</Triggers>", StringComparison.Ordinal) + "</Triggers>".Length;
            xml = xml.Substring(0, start) + "<Triggers><LogonTrigger><Enabled>true</Enabled><UserId>" +
                System.Security.SecurityElement.Escape(sid) + "</UserId></LogonTrigger></Triggers>" + xml.Substring(end);
            return xml.Replace("<UserId>S-1-5-18</UserId>", "<UserId>" + System.Security.SecurityElement.Escape(sid) +
                "</UserId><LogonType>InteractiveToken</LogonType>").Replace("<Arguments>--monitor</Arguments>", "<Arguments>--tray</Arguments>");
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct FileHandleInfo
        {
            public uint Attributes;
            public System.Runtime.InteropServices.ComTypes.FILETIME Created, Accessed, Written;
            public uint VolumeSerial, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security,
            uint creation, uint flags, IntPtr template);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileHandleInfo info);
        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetSecurityDescriptorOwner(IntPtr descriptor, out IntPtr owner, out bool defaulted);
        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetSecurityDescriptorDacl(IntPtr descriptor, out bool present, out IntPtr dacl, out bool defaulted);
        [DllImport("advapi32.dll")]
        private static extern uint SetSecurityInfo(SafeFileHandle handle, int objectType, uint information,
            IntPtr owner, IntPtr group, IntPtr dacl, IntPtr sacl);

        private static void ValidateInstallPaths()
        {
            RejectReparsePath(AppPaths.Root, true);
            RejectReparsePath(AppPaths.Data, true);
            RejectReparsePath(InstalledExe, false);
            RejectReparsePath(AppPaths.ConfigFile, false);
            RejectReparsePath(AppPaths.LogFile, false);
            RejectReparsePath(AppPaths.LogFile + ".1", false);
            RejectReparsePath(AppPaths.RestartHistoryFile, false);
            RejectReparsePath(AppPaths.ControlFile, false);
            RejectReparsePath(AppPaths.StatusFile, false);
        }

        internal static void PrepareRestartHistory()
        {
            ValidateInstallPaths();
            CreateProtectedDirectory(AppPaths.Data);
            ProtectExistingFile(AppPaths.RestartHistoryFile);
        }

        internal static void CheckStatePath(string path)
        {
            RejectReparsePath(AppPaths.Data, true);
            RejectReparsePath(path, false);
        }

        internal static void ProtectStatePath(string path)
        {
            CheckStatePath(path);
            CreateProtectedDirectory(AppPaths.Data);
            ProtectExistingFile(path);
        }

        internal static void PreparePrivateBackup()
        {
            RejectReparsePath(AppPaths.Data, true);
            CreateProtectedDirectory(AppPaths.Data);
            RejectReparsePath(AppPaths.RepairBackup, true);
            Directory.CreateDirectory(AppPaths.RepairBackup);
            ApplyProtectedAcl(AppPaths.RepairBackup, true, false);
        }

        internal static void ProtectBackupFile(string path)
        {
            RejectReparsePath(AppPaths.RepairBackup, true);
            RejectReparsePath(path, false);
            if (File.Exists(path)) ApplyProtectedAcl(path, false, false);
        }

        private static void RejectReparsePath(string path, bool directory)
        {
            try
            {
                FileAttributes attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0 ||
                    ((attributes & FileAttributes.Directory) != 0) != directory)
                    throw new IOException("安裝路徑包含連結或類型不符，請先由管理員檢查：" + path);
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }

        private static void CreateProtectedDirectory(string path)
        {
            RejectReparsePath(path, true);
            Directory.CreateDirectory(path);
            ApplyProtectedAcl(path, true);
        }

        private static void ProtectExistingFile(string path)
        {
            RejectReparsePath(path, false);
            if (File.Exists(path)) ApplyProtectedAcl(path, false);
        }

        private static void ApplyProtectedAcl(string path, bool directory, bool readable = true)
        {
            // Open the object itself, never a reparse target. Excluding delete sharing
            // prevents renaming/replacing it between validation and the ACL write.
            const uint MaximumAllowed = 0x02000000;
            const uint OpenReparsePoint = 0x00200000, BackupSemantics = 0x02000000;
            using (SafeFileHandle handle = CreateFile(path, MaximumAllowed,
                3, IntPtr.Zero, 3, OpenReparsePoint | BackupSemantics, IntPtr.Zero))
            {
                if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error(), "無法保護安裝路徑：" + path);
                FileHandleInfo info;
                if (!GetFileInformationByHandle(handle, out info)) throw new Win32Exception(Marshal.GetLastWin32Error());
                if ((info.Attributes & (uint)FileAttributes.ReparsePoint) != 0 ||
                    ((info.Attributes & (uint)FileAttributes.Directory) != 0) != directory ||
                    (!directory && info.Links > 1))
                    throw new IOException("拒絕使用連結或類型不符的安裝路徑：" + path);
                FileSystemSecurity security = directory ? (FileSystemSecurity)new DirectorySecurity() : new FileSecurity();
                security.SetOwner(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null));
                security.SetAccessRuleProtection(true, false);
                InheritanceFlags inheritance = directory ? InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit : InheritanceFlags.None;
                foreach (WellKnownSidType sid in new[] { WellKnownSidType.LocalSystemSid, WellKnownSidType.BuiltinAdministratorsSid })
                    security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid, null), FileSystemRights.FullControl,
                        inheritance, PropagationFlags.None, AccessControlType.Allow));
                if (readable) security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
                    FileSystemRights.ReadAndExecute, inheritance, PropagationFlags.None, AccessControlType.Allow));
                // MAXIMUM_ALLOWED suppresses SetSecurityInfo propagation to existing children;
                // only these specific objects change. Newly created files still inherit this DACL.
                GCHandle descriptor = GCHandle.Alloc(security.GetSecurityDescriptorBinaryForm(), GCHandleType.Pinned);
                try
                {
                    IntPtr owner, dacl;
                    bool defaulted, present;
                    IntPtr address = descriptor.AddrOfPinnedObject();
                    if (!GetSecurityDescriptorOwner(address, out owner, out defaulted) ||
                        !GetSecurityDescriptorDacl(address, out present, out dacl, out defaulted) || !present || dacl == IntPtr.Zero)
                        throw new InvalidOperationException("無法建立安全的安裝權限設定。");
                    // SE_FILE_OBJECT = 1; OWNER | DACL | PROTECTED_DACL information.
                    uint error = SetSecurityInfo(handle, 1, 0x80000005, owner, IntPtr.Zero, dacl, IntPtr.Zero);
                    if (error != 0) throw new Win32Exception((int)error, "無法設定管理員專用的寫入權限：" + path);
                }
                finally { descriptor.Free(); }
            }
        }

        private static void RequireAdministrator()
        {
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
                    throw new UnauthorizedAccessException("Administrator permission is required.");
        }

        internal static int ExecuteSystem(string filename, string arguments, int timeoutMs)
        {
            using (Process process = new Process())
            {
                process.StartInfo = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, filename), arguments)
                { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
                process.Start();
                if (!process.WaitForExit(timeoutMs))
                {
                    try { process.Kill(); } catch { }
                    return -1;
                }
                return process.ExitCode;
            }
        }
    }
}
