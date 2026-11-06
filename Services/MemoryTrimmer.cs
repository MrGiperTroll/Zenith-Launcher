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

        Task.Run(() =>
        {
            try
            {
                GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
                GC.Collect(1, GCCollectionMode.Default, false);
            }
            catch
            {
                // Memory trim is best-effort and must never fault
            }
        });
    }
}
