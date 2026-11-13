using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Serialization;
using NetworkRecovery;

internal static class CountryProcessGuardChecks
{
    static int passed;
    static void Check(bool value, string message)
    { if (!value) throw new Exception(message); passed++; }
    static void Wait(Func<bool> done)
    { Check(SpinWait.SpinUntil(done, 3000), "test worker did not complete"); }
    static void Cycle(string country, bool failFirstStop)
    {
        var result = new TaskCompletionSource<string>();
        var entered = new ManualResetEventSlim();
        var stopped = new List<string>();
        int failures = 0;
        var guard = new CountryProcessGuard(delegate { entered.Set(); return result.Task; }, delegate(string name) {
            stopped.Add(name); if (failFirstStop && name == "claude") throw new IOException();
        }, delegate(string message) { if (message.Contains("failed")) failures++; });
        guard.Tick(true, false, 0);
        Check(entered.Wait(3000), "lookup launched");
        guard.Tick(true, false, 1);
        Check(stopped.Count == 0, "unfinished lookup never stops anything");
        result.SetResult(country);
        var field = typeof(CountryProcessGuard).GetField("pending", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Check(((Task)field.GetValue(guard)).Wait(3000), "lookup completed");
        guard.Tick(true, false, 2);
        Check(failures == (country == null ? 1 : 0), "failed lookup is logged without using old country");
        if (CountryProcessGuard.IsRestricted(country))
            Check(stopped.Count == 2 && stopped[0] == "claude" && stopped[1] == "cowork-svc", "exact allowlist; failure of first process does not skip second");
        else Check(stopped.Count == 0, "safe country or failed lookup does nothing");
    }
    public static void Main()
    {
        foreach (string code in new[] { "CN", "MO", "HK", "US" })
            Check(CountryProcessGuard.ParseCountry("{\"success\":true,\"country_code\":\"" + code + "\"}") == code, "valid JSON");
        foreach (string body in new[] { null, "", "not JSON", "[]", "null", "{\"success\":false,\"country_code\":\"CN\"}",
            "{\"success\":\"true\",\"country_code\":\"CN\"}", "{\"country_code\":\"CN\"}", "{\"success\":true}",
            "{\"success\":true,\"country_code\":null}", "{\"success\":true,\"country_code\":12}",
            "{\"success\":true,\"country_code\":\"cn\"}", "{\"success\":true,\"country_code\":\"CN\\n\"}",
            "{\"success\":true,\"country_code\":\" CN\"}", new string('x', 32769) })
            Check(CountryProcessGuard.ParseCountry(body) == null, "invalid JSON cannot authorize process termination");
        Check(!CountryProcessGuard.IsRestricted("CNA") && !CountryProcessGuard.IsRestricted("cn") && !CountryProcessGuard.IsRestricted(null), "exact restricted codes");
        foreach (string code in new[] { "CN", "MO", "HK", "US", null }) Cycle(code, false);
        Cycle("CN", true);

        int calls = 0, stops = 0;
        var source = new TaskCompletionSource<string>();
        var guard2 = new CountryProcessGuard(delegate { Interlocked.Increment(ref calls); return source.Task; },
            delegate { stops++; }, delegate { });
        guard2.Tick(false, false, 0); guard2.Tick(true, true, 0);
        Check(calls == 0, "disabled and paused do not query");
        guard2.Tick(true, false, 0); Wait(delegate { return calls == 1; });
        guard2.Tick(true, true, 1); source.SetResult("CN");
        // Observe completion deterministically through injected synchronous task continuation.
        var pendingField = typeof(CountryProcessGuard).GetField("pending", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        ((Task)pendingField.GetValue(guard2)).Wait(3000);
        guard2.Tick(true, false, 2);
        Check(stops == 0, "result spanning a pause is discarded after resume");
        guard2.Tick(true, false, 121.999);
        Check(calls == 1, "no lookup before 120 seconds after completion");
        guard2.Tick(true, false, 122); Wait(delegate { return calls == 2; });
        ((Task)pendingField.GetValue(guard2)).Wait(3000);
        guard2.Tick(true, false, 123);
        Check(stops == 2, "fresh result after resume can act");

        int errors = 0;
        var broken = new CountryProcessGuard(delegate { throw new IOException(); }, delegate { stops++; }, delegate { errors++; });
        broken.Tick(true, false, 0);
        try { ((Task)pendingField.GetValue(broken)).Wait(3000); } catch (AggregateException) { }
        broken.Tick(true, false, 1);
        Check(errors == 1 && stops == 2, "lookup exception is isolated");
        var xml = new XmlSerializer(typeof(AppConfig));
        var old = (AppConfig)xml.Deserialize(new StringReader("<AppConfig><OfflineSeconds>180</OfflineSeconds></AppConfig>"));
        Check(!old.CountryProcessGuard, "old configuration opts out");
        var writer = new StringWriter(); xml.Serialize(writer, new AppConfig { CountryProcessGuard = true });
        Check(((AppConfig)xml.Deserialize(new StringReader(writer.ToString()))).CountryProcessGuard, "option round trips");
        Console.WriteLine("Passed " + passed + " country guard checks; fake lookups and process actions only.");
    }
}
