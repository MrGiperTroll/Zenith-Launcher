using System;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using ZenithLauncher.Services;

namespace ZenithLauncher;

class Program
{
    public const string SingleInstancePipeName = "ZenithLauncher_SingleInstance_IPC";
    private const string SingleInstanceMutexName = @"Local\ZenithLauncher_SingleInstance_Mutex";
    private static Mutex? _singleInstanceMutex;
    public static bool StartupHardwareAcceleration { get; private set; } = true;

    public static void ReleaseSingleInstanceMutexForRestart()
    {
        var mutex = _singleInstanceMutex;
        _singleInstanceMutex = null;
        if (mutex == null) return;
        try { mutex.ReleaseMutex(); } catch { }
        try { mutex.Dispose(); } catch { }
    }

    [System.Runtime.InteropServices.DllImport("shell32.dll", SetLastError = true)]
    private static extern void SetCurrentProcessExplicitAppUserModelID([System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)] string AppID);

    [STAThread]
    public static int Main(string[] args)
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                SetCurrentProcessExplicitAppUserModelID("Zenith.Launcher");
            }
            catch
            {
            }
        }

        // Headless Discord RPC validation: connects to the real desktop client,
        // performs the handshake, sends a presence and requires the echo
        // acknowledgement. Exit code 0 = PASS, 3 = FAIL.
        if (args != null && args.Any(a => a.Equals("--rpc-selftest", StringComparison.OrdinalIgnoreCase)))
        {
            var ok = Task.Run(() => DiscordPresenceService.RunSelfTestAsync()).GetAwaiter().GetResult();
            Console.WriteLine(ok ? "RPC-SELFTEST RESULT: PASS" : "RPC-SELFTEST RESULT: FAIL");
            return ok ? 0 : 3;
        }

        bool isDirectGameLaunch = args != null && args.Length >= 2 &&
            args[0].Equals("--launch", StringComparison.OrdinalIgnoreCase);

        if (args != null && args.Length >= 2 &&
            args[0].Equals("--restart", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(args[1], out var oldPid))
        {
            try
            {
                using var oldProc = System.Diagnostics.Process.GetProcessById(oldPid);
                if (!oldProc.HasExited)
                    oldProc.WaitForExit(4000);
            }
            catch { }
        }

        if (!isDirectGameLaunch)
        {
            bool createdNew = false;
            Mutex? candidate = null;
            bool isRestart = args != null && args.Any(a => a.Equals("--restart", StringComparison.OrdinalIgnoreCase));
            int attempts = isRestart ? 25 : 1;

            for (int attempt = 0; attempt < attempts; attempt++)
            {
                candidate = new Mutex(true, SingleInstanceMutexName, out createdNew);
                if (createdNew) break;
                candidate.Dispose();
                candidate = null;
                if (attempt + 1 < attempts) Thread.Sleep(80);
            }

            if (!createdNew)
            {
                NotifyExistingInstance();
                return 0;
            }
            _singleInstanceMutex = candidate;
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
                LauncherLog.Flush();
            };

            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args ?? Array.Empty<string>());

            AppendLog(logFile, $"[{DateTime.Now}] Application lifetime completed cleanly.\n");
        }
        catch (Exception ex)
        {
            AppendLog(logFile, $"[STARTUP EXCEPTION] {ex}\n");
            return 1;
        }
        finally
        {
            // Guaranteed resource release: watchdog and event-pump threads are
            // signalled to stop and the IPC pipe is disposed even if the UI
            // lifetime ended through an unexpected path.
            try { LauncherLog.Flush(); } catch { }
            try { DiscordPresenceService.Shutdown(); } catch { }
            ReleaseSingleInstanceMutexForRestart();
        }

        return 0;
    }

    private static void NotifyExistingInstance()
    {
        try
        {
            using var client = new NamedPipeClientStream(".", SingleInstancePipeName, PipeDirection.Out);
            client.Connect(1500);
            var payload = Encoding.UTF8.GetBytes("SHOW\n");
            client.Write(payload, 0, payload.Length);
            client.Flush();
        }
        catch
        {
            // Existing instance may be starting up or shutting down; fail silently instead of spawning a duplicate tray.
        }
    }

    private static void AppendLog(string logFile, string text)
    {
        if (string.IsNullOrEmpty(logFile)) return;
        try { File.AppendAllText(logFile, text); }
        catch { }
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        var builder = AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont();
#if DEBUG
        builder = builder.LogToTrace();
#endif

        try
        {
            var cfg = Models.LauncherConfigData.Load();
            StartupHardwareAcceleration = cfg.HardwareAcceleration;
            if (!cfg.HardwareAcceleration && OperatingSystem.IsWindows())
            {
                builder = builder.With(new Win32PlatformOptions
                {
                    RenderingMode = new[] { Win32RenderingMode.Software },
                    CompositionMode = new[] { Win32CompositionMode.RedirectionSurface }
                });
            }
            builder = builder.With(new SkiaOptions
            {
                MaxGpuResourceSizeBytes = cfg.HardwareAcceleration ? 8L * 1024 * 1024 : 4L * 1024 * 1024
            });
        }
        catch { }

        return builder;
    }
}
