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
            if (!force && (now - _lastTrim).TotalSeconds < 3)
                return;
            _lastTrim = now;
        }

        // Run non-blocking garbage collection and LOH compaction on a background pool thread
        Task.Run(() =>
        {
            try
            {
                // Clear any in-memory icon caches
                ModrinthApiService.ClearMemoryCache();

                GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
                GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
                GC.WaitForPendingFinalizers();
                GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
            }
            catch
            {
                // Memory trim is best-effort and must never fault
            }
        });
    }
}
