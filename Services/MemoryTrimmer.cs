using System;
using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using CustomMcLauncher.Converters;

namespace CustomMcLauncher.Services;

public static class MemoryTrimmer
{
    [DllImport("kernel32.dll", EntryPoint = "SetProcessWorkingSetSize")]
    private static extern bool SetProcessWorkingSetSize(IntPtr proc, IntPtr min, IntPtr max);

    private static DateTime _lastTrim = DateTime.MinValue;
    private static readonly object Gate = new();

    public static void TrimMemory(bool force = false)
    {
        lock (Gate)
        {
            var now = DateTime.UtcNow;
            if (!force && (now - _lastTrim).TotalSeconds < 2)
                return;
            _lastTrim = now;
        }

        Task.Run(() =>
        {
            try
            {
                // Purge all transient in-memory bitmap and byte caches
                IconPathToBitmapConverter.ClearCache();
                BytesToBitmapConverter.ClearCache();
                ModrinthApiService.ClearMemoryCache();

                // Instruct GC to compact Large Object Heap (LOH) on this pass to prevent fragmentation from image buffers
                GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;

                GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
                GC.WaitForPendingFinalizers();
                GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);

                if (OperatingSystem.IsWindows())
                {
                    SetProcessWorkingSetSize(Process.GetCurrentProcess().Handle, -1, -1);
                }
            }
            catch
            {
                // Memory trim is best-effort and must never fault
            }
        });
    }
}
