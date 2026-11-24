using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ZenithLauncher.ViewModels;
using ZenithLauncher.Views;
using ZenithLauncher.Services;

namespace ZenithLauncher;

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

                SetupTrayIcon(desktop);

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

    private void SetupTrayIcon(IClassicDesktopStyleApplicationLifetime desktop)
    {
        try
        {
            var icons = TrayIcon.GetIcons(this);
            if (icons == null)
            {
                icons = new TrayIcons();
                TrayIcon.SetIcons(this, icons);
            }

            var trayIcon = new TrayIcon
            {
                ToolTipText = "Zenith Launcher",
                IsVisible = true,
            };

            try
            {
                trayIcon.Icon = new WindowIcon(Avalonia.Platform.AssetLoader.Open(new Uri("avares://ZenithLauncher/Assets/app.ico")));
            }
            catch { }

            trayIcon.Clicked += (_, _) =>
            {
                if (desktop.MainWindow is MainWindow mainWin)
                {
                    mainWin.ShowAndActivate();
                }
            };

            var menu = new NativeMenu();
            var openItem = new NativeMenuItem(L10n.T("tray_open"));
            openItem.Click += (_, _) =>
            {
                if (desktop.MainWindow is MainWindow mainWin)
                {
                    mainWin.ShowAndActivate();
                }
            };

            var exitItem = new NativeMenuItem(L10n.T("tray_exit"));
            exitItem.Click += (_, _) =>
            {
                if (desktop.MainWindow is MainWindow mainWin)
                {
                    mainWin.ForceClose();
                }
                else
                {
                    desktop.Shutdown();
                }
            };

            menu.Items.Add(openItem);
            menu.Items.Add(new NativeMenuItemSeparator());
            menu.Items.Add(exitItem);

            trayIcon.Menu = menu;
            icons.Add(trayIcon);
        }
        catch (Exception ex)
        {
            LauncherLog.Warn($"Tray icon initialization skipped or failed: {ex.Message}");
        }
    }
}
