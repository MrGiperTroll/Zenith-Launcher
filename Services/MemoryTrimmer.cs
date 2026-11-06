using System;
using System.Diagnostics;
using System.Runtime;
using System.Threading.Tasks;

namespace CustomMcLauncher.Services;

public static class MemoryTrimmer
{
    private static DateTime _lastTrim = DateTime.MinValue;
    private static readonly object Gate = new();

    public static void TrimMemory(bool force = false)
    {
        lock (Gate)
        {
            var now = DateTime.UtcNow;
            if (!force && (now - _lastTrim).TotalSeconds < 5)
                return;
            _lastTrim = now;
        }

        // Run non-blocking garbage collection on a background pool thread
        // Never call SetProcessWorkingSetSize as it forces page faults and UI thread freezes.
        Task.Run(() =>
        {
            try
            {
                var wsMb = Process.GetCurrentProcess().WorkingSet64 / (1024 * 1024);
                if (force || wsMb > 160)
                {
                    GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
                    GC.Collect(2, GCCollectionMode.Default, false);
                }
                else
                {
                    GC.Collect(1, GCCollectionMode.Default, false);
                }
            }
            catch
            {
                // Memory trim is best-effort and must never fault
            }
        });
    }
}
