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
    private bool _lastSelectWorldStep;
    private int _lastImportTabIndex;
    private readonly HashSet<ScrollViewer> _attachedSmoothScrolls = new();

    public ModalsHostControl()
    {
        InitializeComponent();
        AddToInstanceMainStep.PropertyChanged += OnAddStepPropertyChanged;
        AddToInstanceWorldStep.PropertyChanged += OnAddStepPropertyChanged;
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
        _lastSelectWorldStep = vm.ModpacksBrowser.IsSelectWorldStep;
        _lastImportTabIndex = GetImportTabIndex(vm.InstancesManager.ImportSourceTab);
        _subscribedInstances.PropertyChanged += OnInstancesPropertyChanged;
    }

    private void UnsubscribeAll()
    {
        if (_subscribedInstances != null)
        {
            _subscribedInstances.PropertyChanged -= OnInstancesPropertyChanged;
            _subscribedInstances = null;
        }

        _subscribedVm = null;
    }

    private void OnInstancesPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewModels.Components.InstancesViewModel.ImportSourceTab) &&
            _subscribedVm != null)
        {
            int newIndex = GetImportTabIndex(_subscribedVm.InstancesManager.ImportSourceTab);
            int dir = newIndex >= _lastImportTabIndex ? 1 : -1;
            _lastImportTabIndex = newIndex;
            if (_subscribedVm.IsImportPickerOpen)
            {
                ZenithMotion.AnimateDirectionalTab(ImportTabContentInner, dir, distance: 52.0, durationMs: 280);
            }
        }
    }

    private static int GetImportTabIndex(string? tab) => (tab ?? "").ToLowerInvariant() switch
    {
        "single" => 0,
        "modrinth" => 1,
        "curseforge" => 2,
        "multimc" => 3,
        "prism" => 4,
        _ => 0
    };

    private void OnAddStepPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != Visual.IsVisibleProperty || sender is not Control step)
            return;

        if (!e.GetNewValue<bool>())
        {
            ZenithMotion.CancelControl(step);
            return;
        }

        if (_subscribedVm?.ModpacksBrowser is { IsAddToInstanceOpen: true } mb && mb.IsSelectWorldStep != _lastSelectWorldStep)
        {
            bool toWorld = mb.IsSelectWorldStep;
            _lastSelectWorldStep = toWorld;
            ZenithMotion.AnimateDirectionalTab(step, toWorld ? 1 : -1, distance: 42.0, durationMs: 275);
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
