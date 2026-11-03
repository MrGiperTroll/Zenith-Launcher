using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

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
            if (!force && (now - _lastTrim).TotalSeconds < 5)
                return;
            _lastTrim = now;
        }

        Task.Run(() =>
        {
            try
            {
                GC.Collect(2, GCCollectionMode.Aggressive, false, true);
                GC.WaitForPendingFinalizers();
                GC.Collect(2, GCCollectionMode.Aggressive, false, true);

                if (OperatingSystem.IsWindows())
                {
                    SetProcessWorkingSetSize(Process.GetCurrentProcess().Handle, -1, -1);
                }
            }
            catch { }
        });
    }
}
