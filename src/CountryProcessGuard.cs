using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace NetworkRecovery
{
    // Lookup runs outside the recovery loop; only the action-lease holder consumes results.
    internal sealed class CountryProcessGuard
    {
        internal static readonly string[] LegacyNames = { "claude", "cowork-svc" };
        internal const int MaxNames = 64;
        internal const int MaxNameLength = 100;
        // Killing these takes down Windows or this monitor; they can never be selected or stopped.
        private static readonly HashSet<string> Protected = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "system", "idle", "registry", "secure system", "memory compression", "smss", "csrss", "wininit", "winlogon",
            "services", "lsass", "lsaiso", "svchost", "fontdrvhost", "dwm", "wudfhost", "taskhostw", "sihost", "conhost" };

        private readonly Func<Task<string>> lookup;
        private readonly string[] names;
        private readonly Action<string> stop;
        private readonly Action<string> log;
        private Task<string> pending;
        private bool discard;
        private double nextLookup;

        internal CountryProcessGuard(Func<Task<string>> lookup, IEnumerable<string> names, Action<string> stop, Action<string> log)
        { this.lookup = lookup; this.names = SanitizeNames(names); this.stop = stop; this.log = log; }

        // Accepts a Task Manager "Details" name such as "claude.exe" and returns the bare process name, or null.
        internal static string NormalizeName(string value)
        {
            if (value == null) return null;
            string name = value.Trim();
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name.Substring(0, name.Length - 4).TrimEnd();
            if (name.Length == 0 || name.Length > MaxNameLength || name.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0 ||
                name.IndexOfAny(new[] { '*', '?' }) >= 0 || name.Trim('.').Length == 0) return null;
            return name;
        }

        internal static bool IsProtectedName(string name)
        {
            return name == null || Protected.Contains(name) || name.StartsWith("NetworkRecovery", StringComparison.OrdinalIgnoreCase);
        }

        internal static string[] SanitizeNames(IEnumerable<string> values)
        {
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (values != null)
                foreach (string value in values)
                {
                    string name = NormalizeName(value);
                    if (name == null || IsProtectedName(name) || !seen.Add(name)) continue;
                    result.Add(name);
                    if (result.Count == MaxNames) break;
                }
            return result.ToArray();
        }

        // Configs saved before the picker existed have no list and keep the original behaviour.
        internal static string[] EffectiveNames(AppConfig config)
        {
            if (config == null || config.GuardProcessNames == null) return (string[])LegacyNames.Clone();
            return SanitizeNames(config.GuardProcessNames);
        }

        internal void Tick(bool enabled, bool paused, double now)
        {
            if (!enabled || paused) discard = true;
            if (pending != null)
            {
                if (!pending.IsCompleted) return;
                Task<string> completed = pending;
                pending = null;
                nextLookup = now + 120;
                try
                {
                    string country = completed.GetAwaiter().GetResult();
                    if (!discard && enabled && !paused)
                    {
                        if (country == null) log("Country lookup failed; retrying next cycle.");
                        else
                        {
                            log("Country: " + country);
                            if (IsRestricted(country))
                                foreach (string name in names)
                                    try { stop(name); }
                                    catch { log("Could not stop " + name + "."); }
                        }
                    }
                }
                catch { if (!discard && enabled && !paused) log("Country lookup failed; retrying next cycle."); }
                return;
            }
            if (!enabled || paused || now < nextLookup) return;
            discard = false;
            pending = Task.Run(lookup);
        }

        internal static bool IsRestricted(string country)
        { return country == "CN" || country == "MO" || country == "HK"; }

        internal static string ParseCountry(string body)
        {
            if (String.IsNullOrEmpty(body) || body.Length > 32768) return null;
            try
            {
                var serializer = new JavaScriptSerializer { MaxJsonLength = 32768, RecursionLimit = 16 };
                var data = serializer.DeserializeObject(body) as Dictionary<string, object>;
                object success, code;
                if (data == null || !data.TryGetValue("success", out success) || !(success is bool) || !(bool)success ||
                    !data.TryGetValue("country_code", out code)) return null;
                string country = code as string;
                if (country == null || country.Length != 2 || country[0] < 'A' || country[0] > 'Z' ||
                    country[1] < 'A' || country[1] > 'Z') return null;
                return country;
            }
            catch { return null; }
        }

        internal static async Task<string> LookupAsync()
        {
            try
            {
                using (var handler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
                using (var client = new HttpClient(handler))
                {
                    client.Timeout = TimeSpan.FromSeconds(10);
                    client.MaxResponseContentBufferSize = 32768;
                    using (var request = new HttpRequestMessage(HttpMethod.Get, "https://ipwho.is/"))
                    {
                        request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };
                        using (var response = await client.SendAsync(request).ConfigureAwait(false))
                        {
                            if (response.StatusCode != HttpStatusCode.OK) return null;
                            return ParseCountry(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
                        }
                    }
                }
            }
            catch { return null; }
        }

        internal static void StopNamedProcesses(string name, Action<string> log)
        {
            name = NormalizeName(name);
            if (name == null || IsProtectedName(name)) return;
            int self = Process.GetCurrentProcess().Id;
            foreach (Process process in Process.GetProcessesByName(name))
                using (process)
                {
                    if (process.Id == self || process.Id <= 4) continue;
                    try { process.Kill(); log("Stopped " + name + "."); }
                    catch { log("Could not stop " + name + "."); }
                }
        }
    }
}
