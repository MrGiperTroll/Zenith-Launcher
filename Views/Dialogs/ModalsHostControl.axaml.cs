using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using ZenithLauncher.UI.Animations;
using ZenithLauncher.ViewModels;
using ZenithLauncher.ViewModels.Dialogs;

namespace ZenithLauncher.Views.Dialogs;

public partial class ModalsHostControl : UserControl
{
    private MainWindowViewModel? _subscribedVm;
    private readonly HashSet<ScrollViewer> _attachedSmoothScrolls = new();

    public ModalsHostControl()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnSmoothScrollLoaded(object? sender, RoutedEventArgs e)
    {
        if (sender is not ScrollViewer sv || !_attachedSmoothScrolls.Add(sv)) return;
        SmoothScrollBehavior.Attach(sv, wheelStep: 42.0, damping: 16.0);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_subscribedVm != null)
            _subscribedVm.PropertyChanged -= OnVmPropertyChanged;
        _subscribedVm = null;

        if (DataContext is not MainWindowViewModel vm) return;
        _subscribedVm = vm;
        vm.PropertyChanged += OnVmPropertyChanged;
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.IsUpdateOpen) && _subscribedVm!.IsUpdateOpen)
        {
            var overlay = this.FindControl<Control>("UpdateOverlay");
            if (overlay != null)
                UiFx.FadeInNow(overlay, 240, 4);
        }
        else if (e.PropertyName == nameof(MainWindowViewModel.IsJavaInstallerOpen))
        {
            if (_subscribedVm!.IsJavaInstallerOpen)
            {
                var card = this.FindControl<Border>("JavaInstallerCard");
                if (card != null)
                    UiFx.FadeInNow(card, 240, 6);
            }
        }
    }

    private void OnInstanceTargetCardTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is Visual v && v.FindAncestorOfType<Button>(includeSelf: true) != null)
        {
            return;
        }
        if (sender is Control { DataContext: InstanceTargetItem item } && item.CanClickButton && !item.IsInstalled)
        {
            item.InstallCommand.Execute(null);
        }
    }

    private void OnWorldTargetCardTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is Visual v && v.FindAncestorOfType<Button>(includeSelf: true) != null)
        {
            return;
        }
        if (sender is Control { DataContext: WorldTargetItem item } && !item.IsInstalled)
        {
            item.InstallCommand.Execute(null);
        }
    }

    private void OnTargetButtonHover(object? sender, PointerEventArgs e)
    {
        if (sender is Button { DataContext: InstanceTargetItem instItem })
            instItem.IsHovered = true;
        else if (sender is Button { DataContext: WorldTargetItem worldItem })
            worldItem.IsHovered = true;
    }

    private void OnTargetButtonHoverOut(object? sender, PointerEventArgs e)
    {
        if (sender is Button { DataContext: InstanceTargetItem instItem })
            instItem.IsHovered = false;
        else if (sender is Button { DataContext: WorldTargetItem worldItem })
            worldItem.IsHovered = false;
    }

    private void OnImportGroupOptionClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm && sender is Button btn)
        {
            vm.InstancesManager.PickImportGroupCommand.Execute(btn.Tag as string ?? "");
        }
        this.FindControl<Button>("ImportGroupFlyoutBtn")?.Flyout?.Hide();
    }

    private void OnAddVersionOptionClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm && sender is Button { DataContext: AddVersionOption opt })
        {
            vm.ModpacksBrowser.SelectAddVersionCommand.Execute(opt);
        }
        this.FindControl<Button>("AddVersionFlyoutBtn")?.Flyout?.Hide();
    }

    private void OnAddGroupOptionClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm && sender is Button btn)
        {
            vm.ModpacksBrowser.SelectAddGroupCommand.Execute(btn.Tag as string ?? "");
        }
        this.FindControl<Button>("AddGroupFlyoutBtn")?.Flyout?.Hide();
    }

    private void OnCompatibleToggleRowTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is Visual v && v.FindAncestorOfType<ToggleSwitch>(includeSelf: true) != null)
            return;
        if (DataContext is MainWindowViewModel vm)
            vm.ModpacksBrowser.FilterCompatibleOnly = !vm.ModpacksBrowser.FilterCompatibleOnly;
    }
}
