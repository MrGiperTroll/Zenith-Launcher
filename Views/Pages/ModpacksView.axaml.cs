using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using ZenithLauncher.UI.Animations;
using ZenithLauncher.ViewModels;
using ZenithLauncher.ViewModels.Items;

namespace ZenithLauncher.Views.Pages;

public partial class ModpacksView : UserControl
{
    private bool _modpacksScrollAttached;
    private ModpacksBrowserViewModel? _boundBrowser;

    public ModpacksView()
    {
        InitializeComponent();
        SmoothScrollBehavior.Attach(ModpacksList, wheelStep: 90.0, damping: 14.0);
        DataContextChanged += OnDataContextChanged;
        DetachedFromVisualTree += OnDetached;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        DetachBrowser();
        if (DataContext is MainWindowViewModel vm)
        {
            _boundBrowser = vm.ModpacksBrowser;
            _boundBrowser.PropertyChanged += OnBrowserPropertyChanged;
            SyncOverlays();
        }
    }

    private void OnDetached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        DetachBrowser();
        ClearHost(DetailsHost);
        ClearHost(AuthorHost);
    }

    private void DetachBrowser()
    {
        if (_boundBrowser != null)
        {
            _boundBrowser.PropertyChanged -= OnBrowserPropertyChanged;
            _boundBrowser = null;
        }
    }

    private void OnBrowserPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ModpacksBrowserViewModel.IsDetailsOpen)
            or nameof(ModpacksBrowserViewModel.IsAuthorProfileOpen)
            or nameof(ModpacksBrowserViewModel.CurrentAuthorProfileVm))
        {
            SyncOverlays();
        }
    }

    private void SyncOverlays()
    {
        if (_boundBrowser == null)
        {
            ClearHost(DetailsHost);
            ClearHost(AuthorHost);
            return;
        }

        if (_boundBrowser.IsDetailsOpen)
        {
            if (DetailsHost.Children.Count == 0)
                DetailsHost.Children.Add(new Views.ProjectDetailsView { DataContext = _boundBrowser });
            else if (!ReferenceEquals(DetailsHost.Children[0].DataContext, _boundBrowser))
                DetailsHost.Children[0].DataContext = _boundBrowser;
        }
        else
        {
            ClearHost(DetailsHost);
        }

        if (_boundBrowser.IsAuthorProfileOpen && _boundBrowser.CurrentAuthorProfileVm != null)
        {
            if (AuthorHost.Children.Count == 0)
                AuthorHost.Children.Add(new Views.AuthorProfileView { DataContext = _boundBrowser.CurrentAuthorProfileVm });
            else if (!ReferenceEquals(AuthorHost.Children[0].DataContext, _boundBrowser.CurrentAuthorProfileVm))
                AuthorHost.Children[0].DataContext = _boundBrowser.CurrentAuthorProfileVm;
        }
        else
        {
            ClearHost(AuthorHost);
        }
    }

    private static void ClearHost(Panel? host)
    {
        if (host == null || host.Children.Count == 0) return;
        foreach (var child in host.Children)
            child.DataContext = null;
        host.Children.Clear();
    }

    private void OnModpackButtonHover(object? sender, PointerEventArgs e)
    {
        if (sender is Button { DataContext: ModrinthProjectItemViewModel p })
            p.IsHovered = true;
    }

    private void OnModpackButtonHoverOut(object? sender, PointerEventArgs e)
    {
        if (sender is Button { DataContext: ModrinthProjectItemViewModel p })
            p.IsHovered = false;
    }

    private void OnModpackCardTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is Visual v && v.FindAncestorOfType<Button>(includeSelf: true) != null)
        {
            e.Handled = true;
            return;
        }

        if (sender is Control { DataContext: ModrinthProjectItemViewModel p } && DataContext is MainWindowViewModel vm)
            vm.ModpacksBrowser.OpenProjectPageCommand.Execute(p);
    }

    private void OnModpackButtonTapped(object? sender, TappedEventArgs e)
    {
        e.Handled = true;
    }

    private void OnModpacksListAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (_modpacksScrollAttached) return;
        if (sender is ListBox list)
        {
            list.AddHandler(ScrollViewer.ScrollChangedEvent, OnModpacksScrollChanged);
            _modpacksScrollAttached = true;
        }
    }

    private void OnModpacksScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (e.Source is not ScrollViewer sv) return;
        if (DataContext is not MainWindowViewModel vm) return;
        if (sv.Extent.Height <= 0 || sv.Viewport.Height <= 0) return;
        if (sv.Extent.Height - sv.Offset.Y - sv.Viewport.Height < 450)
            _ = vm.ModpacksBrowser.LoadMoreAsync();
    }
}
