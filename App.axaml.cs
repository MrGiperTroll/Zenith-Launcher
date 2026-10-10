using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using CustomMcLauncher.ViewModels;
using CustomMcLauncher.Views;
using CustomMcLauncher.Services;

namespace CustomMcLauncher;

public static class ExperimentalMode
{
    /// <summary>
    /// Separate experimental shell: enabled via --experimental flag or by
    /// running a copy of the exe named *Experimental*. Classic UI untouched.
    /// </summary>
    public static bool IsEnabled(string[] args)
    {
        if (args.Any(a => a.Equals("--experimental", StringComparison.OrdinalIgnoreCase) ||
                          a.Equals("--exp", StringComparison.OrdinalIgnoreCase)))
            return true;
        try
        {
            var exe = Environment.GetCommandLineArgs().FirstOrDefault() ?? "";
            if (exe.Contains("experimental", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        catch { }
        return false;
    }
}

public partial class App : Application
{
    public override void Initialize()
    {
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            try
            {
                var crashPath = System.IO.Path.Combine(ZenithPaths.AppDataDir, "logs", "crash.log");
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(crashPath)!);
                System.IO.File.AppendAllText(crashPath, $"\n[{DateTime.Now}] UNHANDLED CRASH: {e.ExceptionObject}\n");
                LauncherLog.Error("Unhandled crash", e.ExceptionObject as Exception ?? new Exception($"{e.ExceptionObject}"));
            }
            catch { }
        };
        TaskScheduler.UnobservedTaskException += (s, e) =>
        {
            try
            {
                var crashPath = System.IO.Path.Combine(ZenithPaths.AppDataDir, "logs", "crash.log");
                System.IO.File.AppendAllText(crashPath, $"\n[{DateTime.Now}] UNOBSERVED TASK: {e.Exception}\n");
                LauncherLog.Error("Unobserved task exception", e.Exception);
            }
            catch { }
        };

        AvaloniaXamlLoader.Load(this);
        Name = "Zenith Launcher";
    }

    public override async void OnFrameworkInitializationCompleted()
    {
        try
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var vm = new MainWindowViewModel();
                var args = Environment.GetCommandLineArgs().Skip(1).ToArray();
                var wantsDirectLaunch = args.Length >= 2 &&
                    args[0].Equals("--launch", StringComparison.OrdinalIgnoreCase);

                if (wantsDirectLaunch && vm.HasProfile(args[1].Trim('\"')))
                {
                    // Desktop-shortcut launch: start the game straight away without
                    // ever showing the main launcher window, then exit.
                    desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                    try
                    {
                        await vm.CheckCommandLineLaunchAsync(args);
                    }
                    finally
                    {
                        desktop.Shutdown();
                    }
                    return;
                }

                desktop.MainWindow = ExperimentalMode.IsEnabled(args)
                    ? new Views.Experimental.ExperimentalMainWindow
                    {
                        DataContext = new ExperimentalShellViewModel(vm)
                    }
                    : new MainWindow
                    {
                        DataContext = vm,
                    };

                // Handle command-line launch for desktop shortcuts
                if (wantsDirectLaunch)
                {
                    await vm.CheckCommandLineLaunchAsync(args);
                }
            }

            base.OnFrameworkInitializationCompleted();
        }
        catch (Exception ex)
        {
            try { System.IO.File.AppendAllText(ZenithPaths.CrashLogPath, $"[FRAMEWORK INIT EXCEPTION] {ex}\n"); } catch { }
            throw;
        }
    }
}
