using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using ZenithLauncher.UI.Animations;
using ZenithLauncher.ViewModels;

namespace ZenithLauncher.Views;

public partial class ContentBrowserView : UserControl
{
    private bool _scrollAttached;
    private ContentBrowserViewModel? _boundVm;
    private int _lastContentTypeIndex;
    private bool _wasInitialLoading;

    public ContentBrowserView()
    {
        InitializeComponent();
        ResultsList.AttachedToVisualTree += OnResultsListAttached;
        SmoothScrollBehavior.Attach(ResultsList, wheelStep: 90.0, damping: 14.0);
        DataContextChanged += OnDataContextChanged;
        DetachedFromVisualTree += OnDetached;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        DetachVm();
        if (DataContext is ContentBrowserViewModel vm)
        {
            _boundVm = vm;
            _lastContentTypeIndex = GetContentTypeIndex(vm.SelectedContentType);
            _wasInitialLoading = vm.IsInitialLoading;
            _boundVm.PropertyChanged += OnVmPropertyChanged;
            SyncOverlays();
        }
    }

    private void OnDetached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        DetachVm();
        ZenithMotion.CancelControl(ContentResultsHost);
        ClearHost(DetailsHost);
        ClearHost(AuthorHost);
    }

    private void DetachVm()
    {
        if (_boundVm != null)
        {
            _boundVm.PropertyChanged -= OnVmPropertyChanged;
            _boundVm = null;
        }
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ContentBrowserViewModel.SelectedContentType) && _boundVm != null)
        {
            int newIndex = GetContentTypeIndex(_boundVm.SelectedContentType);
            if (newIndex != _lastContentTypeIndex)
            {
                int dir = newIndex > _lastContentTypeIndex ? 1 : -1;
                _lastContentTypeIndex = newIndex;
                var sv = ResultsList.FindDescendantOfType<ScrollViewer>();
                if (sv != null) sv.Offset = Vector.Zero;
                ZenithMotion.AnimateCatalogSwipe(ContentResultsHost, dir);
            }
        }
        else if (e.PropertyName == nameof(ContentBrowserViewModel.IsInitialLoading) && _boundVm != null)
        {
            bool nowLoading = _boundVm.IsInitialLoading;
            if (_wasInitialLoading && !nowLoading && _boundVm.Results.Count > 0)
            {
                ZenithMotion.AnimateOpacityReveal(ResultsList, durationMs: 200);
            }
            _wasInitialLoading = nowLoading;
        }
        else if (e.PropertyName is nameof(ContentBrowserViewModel.IsDetailsOpen)
            or nameof(ContentBrowserViewModel.IsAuthorProfileOpen)
            or nameof(ContentBrowserViewModel.CurrentAuthorProfileVm))
        {
            SyncOverlays();
        }
    }

    private static int GetContentTypeIndex(Models.ContentType type) => type switch
    {
        Models.ContentType.Mod => 0,
        Models.ContentType.ResourcePack => 1,
        Models.ContentType.DataPack => 2,
        Models.ContentType.Shader => 3,
        Models.ContentType.World => 4,
        _ => 0
    };

    private void SyncOverlays()
    {
        if (_boundVm == null)
        {
            ClearHost(DetailsHost);
            ClearHost(AuthorHost);
            return;
        }

        if (_boundVm.IsDetailsOpen)
        {
            if (DetailsHost.Children.Count == 0)
            {
                var view = new ProjectDetailsView { DataContext = _boundVm };
                DetailsHost.Children.Add(view);
                ZenithMotion.AnimateEntrance(view, offsetX: 46, offsetY: 0, startScale: 0.975, startOpacity: 0.0, durationMs: 295);
            }
            else if (!ReferenceEquals(DetailsHost.Children[0].DataContext, _boundVm))
            {
                DetailsHost.Children[0].DataContext = _boundVm;
            }
        }
        else if (DetailsHost.Children.Count > 0)
        {
            ClearHost(DetailsHost);
            ZenithMotion.AnimateEntrance(CatalogContentRoot, offsetX: -38, offsetY: 0, startScale: 0.985, startOpacity: 0.0, durationMs: 265);
        }

        if (_boundVm.IsAuthorProfileOpen && _boundVm.CurrentAuthorProfileVm != null)
        {
            if (AuthorHost.Children.Count == 0)
            {
                var view = new AuthorProfileView { DataContext = _boundVm.CurrentAuthorProfileVm };
                AuthorHost.Children.Add(view);
                ZenithMotion.AnimateEntrance(view, offsetX: 46, offsetY: 0, startScale: 0.975, startOpacity: 0.0, durationMs: 295);
            }
            else if (!ReferenceEquals(AuthorHost.Children[0].DataContext, _boundVm.CurrentAuthorProfileVm))
            {
                AuthorHost.Children[0].DataContext = _boundVm.CurrentAuthorProfileVm;
            }
        }
        else if (AuthorHost.Children.Count > 0)
        {
            ClearHost(AuthorHost);
            Control? revealTarget = _boundVm.IsDetailsOpen && DetailsHost.Children.Count > 0
                ? DetailsHost.Children[0] as Control
                : CatalogContentRoot;
            ZenithMotion.AnimateEntrance(revealTarget, offsetX: -38, offsetY: 0, startScale: 0.985, startOpacity: 0.0, durationMs: 265);
        }
    }

    private static void ClearHost(Panel? host)
    {
        if (host == null || host.Children.Count == 0) return;
        foreach (var child in host.Children)
            child.DataContext = null;
        host.Children.Clear();
    }

    private void OnResultsListAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (_scrollAttached) return;
        ResultsList.AddHandler(ScrollViewer.ScrollChangedEvent, OnResultsScrollChanged);
        _scrollAttached = true;
    }

    private void OnResultsScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (e.Source is not ScrollViewer sv) return;
        if (DataContext is not ContentBrowserViewModel vm) return;
        if (sv.Extent.Height <= 0 || sv.Viewport.Height <= 0) return;
        if (sv.Extent.Height - sv.Offset.Y - sv.Viewport.Height < 450)
            _ = vm.LoadMoreAsync();
    }

    private void OnInstalledButtonHover(object? sender, PointerEventArgs e)
    {
        if (sender is Button { DataContext: ViewModels.Items.ModrinthProjectItemViewModel p })
            p.IsHovered = true;
    }

    private void OnInstalledButtonHoverOut(object? sender, PointerEventArgs e)
    {
        if (sender is Button { DataContext: ViewModels.Items.ModrinthProjectItemViewModel p })
            p.IsHovered = false;
    }

    private void OnCardTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is Visual v && v.FindAncestorOfType<Button>(includeSelf: true) != null)
        {
            e.Handled = true;
            return;
        }

        if (sender is Control { DataContext: ViewModels.Items.ModrinthProjectItemViewModel p } && DataContext is ContentBrowserViewModel vm)
            vm.OpenProjectPageCommand.Execute(p);
    }

    private void OnInstallButtonTapped(object? sender, TappedEventArgs e)
    {
        e.Handled = true;
    }

    private void OnWorldOptionClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: string world } && DataContext is ContentBrowserViewModel vm)
        {
            vm.SelectWorldCommand.Execute(world);
            WorldDropdownBtn.Flyout?.Hide();
        }
    }
}
