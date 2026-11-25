using System;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using ZenithLauncher.ViewModels;
using ZenithLauncher.Views;
using ZenithLauncher.Services;

namespace ZenithLauncher;

public partial class App : Application
{
    private CancellationTokenSource? _singleInstancePipeCts;
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
                StartSingleInstanceListener(desktop);

                try
                {
                    var skiaCpuLimit = Program.StartupHardwareAcceleration ? 16 * 1024 * 1024 : 8 * 1024 * 1024;
                    SkiaSharp.SKGraphics.SetResourceCacheTotalByteLimit(skiaCpuLimit);
                    SkiaSharp.SKGraphics.SetResourceCacheSingleAllocationByteLimit(4 * 1024 * 1024);
                }
                catch { }

                desktop.Exit += (_, _) =>
                {
                    try { _singleInstancePipeCts?.Cancel(); } catch { }
                    _singleInstancePipeCts?.Dispose();
                    _singleInstancePipeCts = null;
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
                var iconUri = new Uri("avares://Zenith Launcher/Assets/app.ico");
                if (Avalonia.Platform.AssetLoader.Exists(iconUri))
                {
                    using var iconStream = Avalonia.Platform.AssetLoader.Open(iconUri);
                    trayIcon.Icon = new WindowIcon(iconStream);
                }
                else
                {
                    var diskIconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
                    if (System.IO.File.Exists(diskIconPath))
                    {
                        trayIcon.Icon = new WindowIcon(diskIconPath);
                    }
                }
            }
            catch (Exception ex)
            {
                LauncherLog.Warn($"Failed to load tray icon: {ex.Message}");
            }

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

        L10n.LanguageChanged += () =>
            {
                openItem.Header = L10n.T("tray_open");
                exitItem.Header = L10n.T("tray_exit");
            };

            menu.Items.Add(openItem);
            menu.Items.Add(exitItem);

            trayIcon.Menu = menu;
            icons.Add(trayIcon);
        }
        catch (Exception ex)
        {
            LauncherLog.Warn($"Tray icon initialization skipped or failed: {ex.Message}");
        }
    }

    public void PrepareForRestart()
    {
        try { _singleInstancePipeCts?.Cancel(); } catch { }
        try { _singleInstancePipeCts?.Dispose(); } catch { }
        _singleInstancePipeCts = null;

        try
        {
            var icons = TrayIcon.GetIcons(this);
            if (icons != null)
            {
                foreach (var icon in icons)
                {
                    icon.IsVisible = false;
                    icon.Dispose();
                }
                icons.Clear();
            }
        }
        catch { }
    }

    private void StartSingleInstanceListener(IClassicDesktopStyleApplicationLifetime desktop)
    {
        _singleInstancePipeCts?.Cancel();
        _singleInstancePipeCts?.Dispose();
        var cts = new CancellationTokenSource();
        _singleInstancePipeCts = cts;

        _ = Task.Run(async () =>
        {
            while (!cts.IsCancellationRequested)
            {
                try
                {
                    using var server = new NamedPipeServerStream(
                        Program.SingleInstancePipeName,
                        PipeDirection.In,
                        1,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);

                    await server.WaitForConnectionAsync(cts.Token).ConfigureAwait(false);

                    var buf = new byte[64];
                    int read = await server.ReadAsync(buf, cts.Token).ConfigureAwait(false);
                    var msg = read > 0 ? Encoding.UTF8.GetString(buf, 0, read).Trim() : string.Empty;

                    if (msg.StartsWith("SHOW", StringComparison.OrdinalIgnoreCase))
                    {
                        Dispatcher.UIThread.Post(() =>
                        {
                            if (desktop.MainWindow is MainWindow mainWin)
                            {
                                mainWin.ShowAndActivate();
                            }
                        });
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    LauncherLog.Warn($"Single-instance listener error: {ex.Message}");
                    try { await Task.Delay(500, cts.Token).ConfigureAwait(false); }
                    catch { break; }
                }
            }
        }, cts.Token);
    }
}
