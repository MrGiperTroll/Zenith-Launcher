using System.ComponentModel;
using Avalonia.Controls;
using CustomMcLauncher.Services;
using CustomMcLauncher.UI.Animations;
using CustomMcLauncher.ViewModels;

namespace CustomMcLauncher.Views;

public partial class LauncherSettingsView : UserControl
{
    public LauncherSettingsView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is MainWindowViewModel vm)
            {
                vm.PropertyChanged -= OnVmPropertyChanged;
                vm.PropertyChanged += OnVmPropertyChanged;
            }
        };
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.CurrentSettingsTab))
        {
            var sc = this.FindControl<ScrollViewer>("SettingsContentScroll");
            if (sc != null && sc.IsVisible && sc.Content is StackPanel sp)
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    if (this.VisualRoot == null || !sc.IsVisible) return;
                    foreach (var child in sp.Children)
                    {
                        if (child.IsVisible)
                            UiFx.FadeInNow(child, 180, 2);
                    }
                }, Avalonia.Threading.DispatcherPriority.Render);
            }
        }
    }
}
