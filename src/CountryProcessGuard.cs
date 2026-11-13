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
        private readonly Func<Task<string>> lookup;
        private readonly Action<string> stop;
        private readonly Action<string> log;
        private Task<string> pending;
        private bool discard;
        private double nextLookup;

        internal CountryProcessGuard(Func<Task<string>> lookup, Action<string> stop, Action<string> log)
        { this.lookup = lookup; this.stop = stop; this.log = log; }

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
                                foreach (string name in new[] { "claude", "cowork-svc" })
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
            if (name != "claude" && name != "cowork-svc") return;
            foreach (Process process in Process.GetProcessesByName(name))
                using (process)
                {
                    try { process.Kill(); log("Stopped " + name + "."); }
                    catch { log("Could not stop " + name + "."); }
                }
        }
    }
}
