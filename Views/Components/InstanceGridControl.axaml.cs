using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using ZenithLauncher.Models;
using ZenithLauncher.UI.Animations;
using ZenithLauncher.ViewModels;

namespace ZenithLauncher.Views.Components;

public partial class InstanceGridControl : UserControl
{
    public bool SuppressNextTap { get; set; }

    public InstanceGridControl()
    {
        InitializeComponent();
        SmoothScrollBehavior.Attach(ProfilesGridScroll, wheelStep: 80.0, damping: 16.0);
    }

    private void OnInstanceCardPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border border && border.DataContext is InstanceModel instance)
        {
            this.FindAncestorOfType<Pages.ProfilesView>()?.StartDrag(border, instance, e);
        }
    }

    private void OnInstanceCardTapped(object? sender, TappedEventArgs e)
    {
        var profilesView = this.FindAncestorOfType<Pages.ProfilesView>();
        if (profilesView != null && profilesView.SuppressNextTap)
        {
            profilesView.SuppressNextTap = false;
            e.Handled = true;
            return;
        }

        if (SuppressNextTap)
        {
            SuppressNextTap = false;
            e.Handled = true;
            return;
        }

        if (sender is Control { DataContext: InstanceModel instance } && DataContext is MainWindowViewModel vm)
        {
            vm.SelectInstanceCommand.Execute(instance);
        }
    }

    private void OnInstanceCardDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: InstanceModel instance } && DataContext is MainWindowViewModel vm)
        {
            vm.SelectInstanceCommand.Execute(instance);
            vm.NavigateToEditInstance(instance, "Overview");
        }
    }

    private void OnGroupFilterOptionClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm && sender is Button btn)
        {
            vm.SelectProfileGroupCommand.Execute(btn.Tag as string ?? "");
        }
        this.FindControl<Button>("GroupFilterBtn")?.Flyout?.Hide();
    }

    private void OnGroupSectionHeaderTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: ViewModels.Components.InstanceGroupSection section })
        {
            section.ToggleExpandCommand.Execute(null);
        }
    }
}
