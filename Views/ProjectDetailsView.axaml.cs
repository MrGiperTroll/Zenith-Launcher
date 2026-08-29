using Avalonia.Controls;
using Avalonia.Interactivity;
using CustomMcLauncher.Models;

namespace CustomMcLauncher.Views;

public partial class ProjectDetailsView : UserControl
{
    public ProjectDetailsView()
    {
        InitializeComponent();
    }

    private void OnVersionRowTapped(object? sender, RoutedEventArgs e)
    {
        if (sender is Border border && border.Tag is ModrinthProjectVersion version)
        {
            var parent = this.Parent;
            while (parent != null)
            {
                if (parent is Window window)
                {
                    var dc = window.DataContext;
                    if (dc is ViewModels.ContentBrowserViewModel cbVm)
                    {
                        cbVm.SelectedVersionForDetails = version;
                    }
                    else if (dc is ViewModels.MainWindowViewModel mwVm && mwVm.ModpacksBrowser != null)
                    {
                        mwVm.ModpacksBrowser.SelectedVersionForDetails = version;
                    }
                    break;
                }
                parent = (parent as Control)?.Parent;
            }
        }
    }

    private void OnCompatibilityVersionTapped(object? sender, RoutedEventArgs e)
    {
        if (sender is Border border && border.Tag is string version)
        {
            var parent = this.Parent;
            while (parent != null)
            {
                if (parent is Window window)
                {
                    var dc = window.DataContext;
                    if (dc is ViewModels.ContentBrowserViewModel cbVm)
                    {
                        cbVm.SelectCompatibilityVersionCommand.Execute(version);
                    }
                    else if (dc is ViewModels.MainWindowViewModel mwVm && mwVm.ModpacksBrowser != null)
                    {
                        mwVm.ModpacksBrowser.SelectCompatibilityVersionCommand.Execute(version);
                    }
                    break;
                }
                parent = (parent as Control)?.Parent;
            }
        }
    }
}
