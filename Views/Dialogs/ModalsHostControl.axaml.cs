using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using System;
using System.ComponentModel;
using ZenithLauncher.UI.Animations;
using ZenithLauncher.ViewModels;
using ZenithLauncher.ViewModels.Dialogs;

namespace ZenithLauncher.Views.Dialogs;

public partial class ModalsHostControl : UserControl
{
    private MainWindowViewModel? _subscribedVm;

    public ModalsHostControl()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
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
        if (sender is Control { DataContext: InstanceTargetItem item } && item.CanClickButton)
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
}
