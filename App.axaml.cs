using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using CustomMcLauncher.ViewModels;
using CustomMcLauncher.Views;

namespace CustomMcLauncher;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        Name = "Zenith Launcher";
    }

    public override async void OnFrameworkInitializationCompleted()
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
}
