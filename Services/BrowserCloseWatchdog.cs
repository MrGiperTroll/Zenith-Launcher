using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CustomMcLauncher.Services;

/// <summary>
/// Watches for the user closing ALL browser windows while a browser-based OAuth
/// sign-in is waiting for its loopback callback. When no visible browser window
/// remains, the linked CancellationTokenSource is cancelled so the launcher UI
/// resets immediately instead of hanging until the sign-in timeout.
///
/// Detection is window-class based (Chromium family + Firefox) and deliberately
/// FAILS OPEN: any enumeration error or an unknown browser class keeps the
/// wait alive - the normal timeout then applies as before.
/// </summary>
public static class BrowserCloseWatchdog
{
    // Chromium family (Chrome/Edge/Brave/Vivaldi/Yandex/Opera GX...) + Firefox.
    private static readonly string[] BrowserWindowClasses =
    {
        "Chrome_WidgetWin_1",
        "MozillaWindowClass",
        "IEFrame"
    };

    private const int GraceAttempts = 15;   // 1.5 s cadence -> ~22 s for the browser to appear
    private const int NeverSeenAttemptLimit = 20; // ~30 s without any browser at all
    private const int EmptyStreakToCancel = 2;    // two consecutive "no window" checks

    /// <summary>
    /// Runs until <paramref name="cancelTarget"/> is cancelled externally OR this
    /// watchdog cancels it because every browser window disappeared.
    /// </summary>
    public static async Task RunAsync(CancellationTokenSource cancelTarget)
    {
        try
        {
            var sawBrowser = false;   // a visible browser window was observed at least once
            var emptyStreak = 0;

            for (var attempt = 0; ; attempt++)
            {
                await Task.Delay(attempt < GraceAttempts ? 1500 : 2500, cancelTarget.Token).ConfigureAwait(false);

                var visible = HasVisibleBrowserWindow(out var enumerationFailed);
                if (enumerationFailed)
                {
                    // Fail open: cannot tell - keep waiting for the normal timeout.
                    continue;
                }

                if (visible)
                {
                    sawBrowser = true;
                    emptyStreak = 0;
                    continue;
                }

                if (!sawBrowser)
                {
                    // No browser ever showed up - the tab may have been closed
                    // instantly or the default browser failed to open.
                    if (attempt >= NeverSeenAttemptLimit)
                    {
                        LauncherLog.Info("Auth watchdog: no browser window appeared - cancelling sign-in wait.");
                        cancelTarget.Cancel();
                        return;
                    }
                    continue;
                }

                if (++emptyStreak >= EmptyStreakToCancel)
                {
                    LauncherLog.Info("Auth watchdog: all browser windows closed - cancelling sign-in wait.");
                    cancelTarget.Cancel();
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // External stop (sign-in finished or dialog closed) - expected exit path.
        }
        catch (Exception ex)
        {
            LauncherLog.Error("Auth watchdog crashed (ignored)", ex);
        }
    }

    private static bool HasVisibleBrowserWindow(out bool enumerationFailed)
    {
        enumerationFailed = false;
        var found = false;
        var ok = EnumWindows((hWnd, _) =>
        {
            if (!IsWindowVisible(hWnd)) return true;

            var sb = new StringBuilder(256);
            if (GetClassName(hWnd, sb, sb.Capacity) <= 0) return true;
            var className = sb.ToString();

            foreach (var browserClass in BrowserWindowClasses)
            {
                if (!string.Equals(className, browserClass, StringComparison.OrdinalIgnoreCase)) continue;
                found = true;
                return false; // stop enumeration - a browser window exists
            }
            return true;
        }, IntPtr.Zero);

        if (!ok) enumerationFailed = true;
        return found;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
}
