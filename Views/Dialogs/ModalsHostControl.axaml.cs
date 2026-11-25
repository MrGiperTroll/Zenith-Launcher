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
    private INotifyPropertyChanged? _subscribedInstances;
    private INotifyPropertyChanged? _subscribedBrowser;
    private readonly HashSet<ScrollViewer> _attachedSmoothScrolls = new();

    public ModalsHostControl()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        DetachedFromVisualTree += (_, _) => UnsubscribeAll();
    }

    private void OnSmoothScrollLoaded(object? sender, RoutedEventArgs e)
    {
        if (sender is not ScrollViewer sv || !_attachedSmoothScrolls.Add(sv)) return;
        SmoothScrollBehavior.Attach(sv, wheelStep: 42.0, damping: 16.0);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        UnsubscribeAll();

        if (DataContext is not MainWindowViewModel vm) return;
        _subscribedVm = vm;
        _subscribedInstances = vm.InstancesManager;
        _subscribedBrowser = vm.ModpacksBrowser;
        _subscribedInstances.PropertyChanged += OnInstancesPropertyChanged;
        _subscribedBrowser.PropertyChanged += OnBrowserPropertyChanged;
    }

    private void UnsubscribeAll()
    {
        if (_subscribedInstances != null)
        {
            _subscribedInstances.PropertyChanged -= OnInstancesPropertyChanged;
            _subscribedInstances = null;
        }

        if (_subscribedBrowser != null)
        {
            _subscribedBrowser.PropertyChanged -= OnBrowserPropertyChanged;
            _subscribedBrowser = null;
        }

        _subscribedVm = null;
    }

    private void OnInstancesPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewModels.Components.InstancesViewModel.ImportSourceTab) &&
            _subscribedVm?.IsImportPickerOpen == true)
        {
            ZenithMotion.TransitionTab(ImportTabContentCard, offsetX: 0, offsetY: 5, durationMs: 115);
        }
    }

    private void OnBrowserPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ModpacksBrowserViewModel.IsSelectWorldStep) &&
            _subscribedVm?.ModpacksBrowser.IsAddToInstanceOpen == true)
        {
            bool toWorld = _subscribedVm.ModpacksBrowser.IsSelectWorldStep;
            var target = toWorld ? AddToInstanceWorldStep : AddToInstanceMainStep;
            ZenithMotion.AnimateEntrance(target, offsetX: toWorld ? 16 : -16, offsetY: 0, startScale: 1.0, startOpacity: 0.25, durationMs: 145);
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
