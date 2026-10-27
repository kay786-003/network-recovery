using System;
using System.IO;
using NetworkRecovery;

internal static class RestartBudgetChecks
{
    private static int checks;
    private static readonly DateTime Utc = new DateTime(2026, 10, 28, 0, 0, 0, DateTimeKind.Utc);
    private static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
        checks++;
    }
    private static RestartBudget Full()
    {
        RestartBudget budget = new RestartBudget(null, "boot-a", 0, Utc);
        for (int i = 0; i < 5; i++) Check(budget.TryReserve(i * 100), "first five allowed");
        return budget;
    }
    public static void Main()
    {
        RestartBudget budget = Full();
        Check(!budget.TryReserve(500), "sixth denied");
        budget.ObserveInternet(false); // Includes Wi-Fi connected / LAN-only / failed external probes.
        Check(!budget.TryReserve(600), "Wi-Fi without external response never resets");
        Check(!budget.TryReserve(14399.999), "window not yet elapsed");
        Check(budget.TryReserve(14400), "oldest expires exactly at four hours");
        Check(!budget.TryReserve(14401), "rolling window does not reset all entries");
        Check(budget.TryReserve(14500), "second oldest independently expires");
        budget.ObserveInternet(true);
        for (int i = 0; i < 5; i++) Check(budget.TryReserve(14501), "actual external response resets immediately");
        Check(!budget.TryReserve(14502), "fresh recovery budget limited to five");

        RestartHistory state = Full().Snapshot("boot-a", 500, Utc.AddSeconds(500));
        budget = new RestartBudget(state, "boot-a", 600, Utc.AddDays(10));
        Check(!budget.TryReserve(600), "same-boot process restart ignores forward wall-clock change");
        budget = new RestartBudget(state, "boot-a", 14400, Utc.AddDays(-10));
        Check(budget.TryReserve(14400), "same-boot elapsed time ignores backward wall-clock change");
        budget = new RestartBudget(state, "boot-b", 300, Utc.AddSeconds(800));
        Check(!budget.TryReserve(300), "Windows restart retains all five reservations");
        budget = new RestartBudget(state, "boot-b", 300, Utc.AddSeconds(14400));
        Check(budget.TryReserve(300), "cross-boot oldest expires");
        Check(!budget.TryReserve(301), "cross-boot rolling expiry retains later requests");
        budget = new RestartBudget(state, "boot-b", 300, Utc.AddDays(-1));
        Check(!budget.TryReserve(300), "backward UTC bridge grants no extra quota");
        budget = RestartBudget.Blocked(10);
        Check(!budget.TryReserve(11), "unknown/corrupt history blocks restart");
        Check(budget.TryReserve(14410), "unknown history continues monotonic cooldown");
        budget = RestartBudget.Blocked(10);
        budget.ObserveInternet(true);
        Check(budget.TryReserve(11), "external recovery clears conservative state");

        string folder = Path.Combine(Path.GetTempPath(), "NetworkRecovery-budget-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "history.xml");
        try
        {
            Check(RestartHistoryFile.Read(path) == null, "first install has no history");
            RestartHistoryFile.Write(path, state);
            budget = new RestartBudget(RestartHistoryFile.Read(path), "boot-b", 300, Utc.AddSeconds(800));
            Check(!budget.TryReserve(300), "disk roundtrip retains quota across reboot");
            budget.ObserveInternet(true);
            RestartHistoryFile.Write(path, budget.Snapshot("boot-b", 300, Utc.AddSeconds(800)));
            Check(!new RestartBudget(RestartHistoryFile.Read(path), "boot-b", 301, Utc).HasHistory, "atomic replacement persists reset");
            Check(Directory.GetFiles(folder).Length == 1, "no staging files remain");
            File.WriteAllText(path, "broken");
            bool rejected = false;
            try { RestartHistoryFile.Read(path); } catch { rejected = true; }
            Check(rejected, "corrupt XML is not treated as empty");
            rejected = false;
            state.Ages[0] = Double.NaN;
            try { new RestartBudget(state, "boot-a", 600, Utc); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, "non-finite persisted age rejected");
            rejected = false;
            try { RestartHistoryFile.Write(Path.Combine(folder, "missing", "history.xml"), state); } catch (IOException) { rejected = true; }
            Check(rejected, "write failure propagates to restart gate");
        }
        finally
        {
            foreach (string file in Directory.GetFiles(folder)) File.Delete(file);
            Directory.Delete(folder);
        }
        Console.WriteLine("PASS: " + checks + " isolated restart budget checks.");
    }
}
