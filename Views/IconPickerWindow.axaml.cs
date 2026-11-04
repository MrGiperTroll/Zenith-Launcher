using System;
using Avalonia.Controls;
using CustomMcLauncher.Services;
using CustomMcLauncher.ViewModels;

namespace CustomMcLauncher.Views;

public partial class IconPickerWindow : Window
{
    private Action<IconOption>? _onSelected;
    private Action<string>? _onCustomPicked;
    private IconPickerViewModel? _subscribedVm;

    public IconPickerWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        Unsubscribe();
        if (DataContext is IconPickerViewModel vm)
        {
            _subscribedVm = vm;
            _onSelected = _ => Close();
            _onCustomPicked = _ => Close();
            vm.IconSelected += _onSelected;
            vm.CustomIconPicked += _onCustomPicked;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        Unsubscribe();
        if (DataContext is IDisposable d)
        {
            d.Dispose();
        }
        DataContext = null;
        base.OnClosed(e);
    }

    private void Unsubscribe()
    {
        if (_subscribedVm != null)
        {
            if (_onSelected != null) _subscribedVm.IconSelected -= _onSelected;
            if (_onCustomPicked != null) _subscribedVm.CustomIconPicked -= _onCustomPicked;
            _subscribedVm = null;
        }
        _onSelected = null;
        _onCustomPicked = null;
    }
}
