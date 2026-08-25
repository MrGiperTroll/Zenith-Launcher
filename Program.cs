using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using CustomMcLauncher.Services;

namespace CustomMcLauncher;

class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Headless Discord RPC validation: connects to the real desktop client,
        // performs the handshake, sends a presence and requires the echo
        // acknowledgement. Exit code 0 = PASS, 3 = FAIL.
        if (args != null && args.Any(a => a.Equals("--rpc-selftest", StringComparison.OrdinalIgnoreCase)))
        {
            var ok = Task.Run(() => DiscordPresenceService.RunSelfTestAsync()).GetAwaiter().GetResult();
            Console.WriteLine(ok ? "RPC-SELFTEST RESULT: PASS" : "RPC-SELFTEST RESULT: FAIL");
            return ok ? 0 : 3;
        }

        var logFile = string.Empty;
        try
        {
            Directory.CreateDirectory(ZenithPaths.AppDataDir);
            logFile = ZenithPaths.CrashLogPath;
            File.WriteAllText(logFile, $"[{DateTime.Now}] Starting Zenith Launcher...\n");
        }
        catch
        {
            // crash log is best-effort — never let it block startup
        }

        try
        {
            AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
            {
                AppendLog(logFile, $"[CRITICAL UNHANDLED] {e.ExceptionObject}\n");
            };

            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args ?? Array.Empty<string>());

            AppendLog(logFile, $"[{DateTime.Now}] Application lifetime completed cleanly.\n");
        }
        catch (Exception ex)
        {
            AppendLog(logFile, $"[STARTUP EXCEPTION] {ex}\n");
        }
        finally
        {
            // Guaranteed resource release: watchdog and event-pump threads are
            // signalled to stop and the IPC pipe is disposed even if the UI
            // lifetime ended through an unexpected path.
            try { DiscordPresenceService.Shutdown(); } catch { }
        }

        return 0;
    }

    private static void AppendLog(string logFile, string text)
    {
        if (string.IsNullOrEmpty(logFile)) return;
        try { File.AppendAllText(logFile, text); }
        catch { }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
