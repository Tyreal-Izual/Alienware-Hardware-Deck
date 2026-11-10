using System;
using System.Threading;
using HardwareDeck;

public static class StartupInstanceTests
{
    public static void Run()
    {
        string name = @"Local\HardwareDeck.Test." + Guid.NewGuid().ToString("N");
        using (var first = new DashboardInstance(name))
        {
            if (!first.Acquired) throw new Exception("First dashboard cannot acquire its gate");
            bool duplicate = true;
            var thread = new Thread(() => { using (var second = new DashboardInstance(name)) duplicate = second.Acquired; });
            thread.Start();
            if (!thread.Join(5000) || duplicate) throw new Exception("A duplicate dashboard acquired the gate");
        }
        using (var restarted = new DashboardInstance(name))
            if (!restarted.Acquired) throw new Exception("Closed dashboard did not release the gate");
        if (!DashboardInstance.Name.StartsWith(@"Local\HardwareDeck.Dashboard.S-"))
            throw new Exception("Dashboard gate is not scoped to the current user/session");
        var departing = new DashboardInstance(name);
        bool handedOff = false;
        var successor = new Thread(() => { using (var next = new DashboardInstance(name, 3000)) handedOff = next.Acquired; });
        successor.Start();
        Thread.Sleep(100);
        departing.Dispose();
        if (!successor.Join(5000) || !handedOff) throw new Exception("Elevation handoff could not acquire the released gate");
        Console.WriteLine("Dashboard instance checks passed: 5");
    }
}
