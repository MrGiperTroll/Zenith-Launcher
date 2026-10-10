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

                desktop.MainWindow = new MainWindow
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
