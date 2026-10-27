using System;
using System.Reflection;
using NetworkRecovery;
class SchedulerReadChecks
{
    static void Main()
    {
        MethodInfo read = typeof(AppInstaller).GetMethod("ReadTask", BindingFlags.Static | BindingFlags.NonPublic);
        object snapshot = read.Invoke(null, new object[] { "NetworkRecovery-Missing-Test-" + Guid.NewGuid().ToString("N") });
        if ((bool)snapshot.GetType().GetField("Exists").GetValue(snapshot))
            throw new Exception("Missing task must return Exists=false.");
        if (AppInstaller.GetStatus() == "無法讀取排程狀態")
            throw new Exception("Scheduler status could not be read.");
        Console.WriteLine("Passed 2 read-only scheduler checks. No tasks registered or changed.");
    }
}
