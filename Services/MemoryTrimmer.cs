using System;
using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace CustomMcLauncher.Services;

public static class MemoryTrimmer
{
    private static DateTime _lastTrim = DateTime.MinValue;
    private static readonly object Gate = new();
    private static readonly Timer _idleWatchdog;

    [DllImport("kernel32.dll", EntryPoint = "SetProcessWorkingSetSize")]
    private static extern bool SetProcessWorkingSetSize(IntPtr proc, IntPtr min, IntPtr max);

    static MemoryTrimmer()
    {
        // Periodic background trim when idle every 30s
        _idleWatchdog = new Timer(_ =>
        {
            try
            {
                var wsMb = Process.GetCurrentProcess().WorkingSet64 / (1024 * 1024);
                if (wsMb > 60)
                {
                    TrimMemory(force: false);
                }
            }
            catch { }
        }, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }

    public static void TrimMemory(bool force = false)
    {
        lock (Gate)
        {
            var now = DateTime.UtcNow;
            if (!force && (now - _lastTrim).TotalSeconds < 3)
                return;
            _lastTrim = now;
        }

        Task.Run(() =>
        {
            try
            {
                GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
                GC.Collect(2, GCCollectionMode.Aggressive, true, true);
                GC.WaitForPendingFinalizers();
                GC.Collect(2, GCCollectionMode.Aggressive, true, true);

                if (OperatingSystem.IsWindows())
                {
                    SetProcessWorkingSetSize(Process.GetCurrentProcess().Handle, (IntPtr)(-1), (IntPtr)(-1));
                }
            }
            catch
            {
                // Memory trim is best-effort and must never fault
            }
        });
    }
}
