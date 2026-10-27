using System;
using System.Reflection;
using System.Security.Principal;
using NetworkRecovery;
class SchedulerReadChecks
{
    static void Main()
    {
        MethodInfo read = typeof(AppInstaller).GetMethod("ReadTask", BindingFlags.Static | BindingFlags.NonPublic);
        object snapshot = read.Invoke(null, new object[] { "NetworkRecovery-Missing-Test-" + Guid.NewGuid().ToString("N") });
        if ((bool)snapshot.GetType().GetField("Exists").GetValue(snapshot))
            throw new Exception("Missing task must return Exists=false.");
        string status = AppInstaller.GetStatus();
        bool elevated;
        using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
            elevated = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        if (status == "無法讀取排程狀態" && elevated)
            throw new Exception("Elevated scheduler status could not be read.");
        Console.WriteLine(status == "無法讀取排程狀態"
            ? "Passed missing-task check; skipped protected task status without elevation. No tasks changed."
            : "Passed 2 read-only scheduler checks. No tasks registered or changed.");
    }
}
