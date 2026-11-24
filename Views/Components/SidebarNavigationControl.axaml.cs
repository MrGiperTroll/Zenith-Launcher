using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using ZenithLauncher.Models;
using ZenithLauncher.ViewModels;

namespace ZenithLauncher.Views.Components;

public partial class SidebarNavigationControl : UserControl
{
    public SidebarNavigationControl()
    {
        InitializeComponent();
    }

    private void OnProfileFlyoutActionClick(object? sender, RoutedEventArgs e)
    {
        var btn = this.FindControl<Button>("ProfileAccountBtn");
        btn?.Flyout?.Hide();
    }

    private void OnSwitchAccountsPointerEntered(object? sender, PointerEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.IsSwitchAccountsMenuOpen = true;
        }
    }

    private void OnSwitchAccountsClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.IsSwitchAccountsMenuOpen = !vm.IsSwitchAccountsMenuOpen;
        }
    }

    private void OnOtherProfileButtonPointerEntered(object? sender, PointerEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.IsSwitchAccountsMenuOpen = false;
        }
    }

    private void OnProfileFlyoutClosed(object? sender, EventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.IsSwitchAccountsMenuOpen = false;
        }
    }

    private void OnAccountRowClick(object? sender, RoutedEventArgs e)
    {
        AccountModel? targetModel = null;
        if (sender is Control ctrl)
        {
            if (ctrl.Tag is AccountRow r1) targetModel = r1.Model;
            else if (ctrl.Tag is AccountModel m1) targetModel = m1;
            else if (ctrl.DataContext is AccountRow r2) targetModel = r2.Model;
            else if (ctrl.DataContext is AccountModel m2) targetModel = m2;
        }

        if (targetModel == null && e.Source is Control sourceCtrl)
        {
            if (sourceCtrl.Tag is AccountRow r1) targetModel = r1.Model;
            else if (sourceCtrl.Tag is AccountModel m1) targetModel = m1;
            else if (sourceCtrl.DataContext is AccountRow r2) targetModel = r2.Model;
            else if (sourceCtrl.DataContext is AccountModel m2) targetModel = m2;
        }

        if (targetModel != null && DataContext is MainWindowViewModel vm)
        {
            vm.SwitchAccount(targetModel);
        }

        var btn = this.FindControl<Button>("ProfileAccountBtn");
        btn?.Flyout?.Hide();
    }

    private void OnManageAccountsClick(object? sender, RoutedEventArgs e)
    {
        var btn = this.FindControl<Button>("ProfileAccountBtn");
        btn?.Flyout?.Hide();

        if (DataContext is MainWindowViewModel vm)
        {
            vm.OpenAccountsDialogCommand.Execute(null);
        }
    }

    private async void OnCopyUsernameClick(object? sender, RoutedEventArgs e)
    {
        var btn = this.FindControl<Button>("ProfileAccountBtn");
        btn?.Flyout?.Hide();

        if (DataContext is MainWindowViewModel vm)
        {
            await vm.CopyActiveUsernameAsync();
        }
    }

    private void OnDeleteProfileClick(object? sender, RoutedEventArgs e)
    {
        var btn = this.FindControl<Button>("ProfileAccountBtn");
        btn?.Flyout?.Hide();

        if (DataContext is MainWindowViewModel vm)
        {
            vm.RequestDeleteActiveAccount();
        }
    }
}
