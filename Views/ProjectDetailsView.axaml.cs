using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using ZenithLauncher.Models;
using ZenithLauncher.UI.Animations;

namespace ZenithLauncher.Views;

public partial class ProjectDetailsView : UserControl
{
    private INotifyPropertyChanged? _boundVm;
    private bool _lastVersionDetailActive;
    private string _lastDetailTab = string.Empty;
    private int _lastDetailTabIndex;

    public ProjectDetailsView()
    {
        InitializeComponent();
        SmoothScrollBehavior.Attach(DetailsScroll, wheelStep: 90.0, damping: 14.0);

        VersionDetailPanel.PropertyChanged += OnVersionSectionPropertyChanged;
        RegularTabsPanel.PropertyChanged += OnVersionSectionPropertyChanged;
        DescriptionPanel.PropertyChanged += OnDetailTabPanelPropertyChanged;
        GalleryPanel.PropertyChanged += OnDetailTabPanelPropertyChanged;
        ChangelogPanel.PropertyChanged += OnDetailTabPanelPropertyChanged;
        VersionsPanel.PropertyChanged += OnDetailTabPanelPropertyChanged;

        DataContextChanged += OnDataContextChanged;
        DetachedFromVisualTree += (_, _) => UnsubscribeVm();
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        UnsubscribeVm();
        if (DataContext is INotifyPropertyChanged npc)
        {
            _boundVm = npc;
            _lastVersionDetailActive = GetIsVersionDetailActive();
            _lastDetailTab = GetSelectedDetailTab();
            _lastDetailTabIndex = GetDetailTabIndex(_lastDetailTab);
        }
    }

    private void UnsubscribeVm()
    {
        _boundVm = null;
    }

    private bool GetIsVersionDetailActive() => DataContext switch
    {
        ViewModels.ContentBrowserViewModel cb => cb.IsVersionDetailActive,
        ViewModels.ModpacksBrowserViewModel mp => mp.IsVersionDetailActive,
        _ => false
    };

    private string GetSelectedDetailTab() => DataContext switch
    {
        ViewModels.ContentBrowserViewModel cb => cb.SelectedDetailTab,
        ViewModels.ModpacksBrowserViewModel mp => mp.SelectedDetailTab,
        _ => string.Empty
    };

    private void OnVersionSectionPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != Visual.IsVisibleProperty || sender is not Control panel)
            return;

        if (!e.GetNewValue<bool>())
        {
            ZenithMotion.CancelControl(panel);
            return;
        }

        if (_boundVm == null) return;
        bool isVersion = GetIsVersionDetailActive();
        if (isVersion == _lastVersionDetailActive) return;

        _lastVersionDetailActive = isVersion;
        if (ReferenceEquals(panel, VersionDetailPanel) && isVersion)
        {
            ZenithMotion.AnimateEntrance(VersionDetailPanel, offsetX: 46, offsetY: 0, startScale: 0.98, startOpacity: 0.0, durationMs: 290, category: MotionCategory.InternalTabs);
        }
        else if (ReferenceEquals(panel, RegularTabsPanel) && !isVersion)
        {
            ZenithMotion.AnimateEntrance(RegularTabsPanel, offsetX: -38, offsetY: 0, startScale: 0.985, startOpacity: 0.0, durationMs: 265, category: MotionCategory.InternalTabs);
        }
    }

    private void OnDetailTabPanelPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != Visual.IsVisibleProperty || sender is not Control panel)
            return;

        if (!e.GetNewValue<bool>())
        {
            ZenithMotion.CancelControl(panel);
            return;
        }

        if (_boundVm == null || GetIsVersionDetailActive()) return;
        string currentTab = GetSelectedDetailTab();
        if (!string.Equals(currentTab, _lastDetailTab, StringComparison.OrdinalIgnoreCase))
        {
            int newIndex = GetDetailTabIndex(currentTab);
            int dir = newIndex >= _lastDetailTabIndex ? 1 : -1;
            _lastDetailTab = currentTab;
            _lastDetailTabIndex = newIndex;
            ZenithMotion.AnimateDirectionalTab(panel, dir, distance: 46.0, durationMs: 280);
        }
    }

    private static int GetDetailTabIndex(string? tab) => (tab ?? "").Trim().ToLowerInvariant() switch
    {
        "description" => 0,
        "gallery" => 1,
        "changelog" => 2,
        "versions" => 3,
        _ => 0
    };

    private void OnVersionRowTapped(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: ModrinthProjectVersion version })
        {
            if (DataContext is ViewModels.ContentBrowserViewModel cbVm)
            {
                cbVm.OpenVersionDetailCommand.Execute(version);
            }
            else if (DataContext is ViewModels.ModpacksBrowserViewModel mpVm)
            {
                mpVm.OpenVersionDetailCommand.Execute(version);
            }
        }
    }

    private void OnDownloadVersionClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: ModrinthProjectVersion version })
        {
            if (DataContext is ViewModels.ContentBrowserViewModel cbVm)
            {
                cbVm.DownloadVersionCommand.Execute(version);
            }
            else if (DataContext is ViewModels.ModpacksBrowserViewModel mpVm)
            {
                mpVm.DownloadVersionCommand.Execute(version);
            }
        }
    }

    private void OnPageNumberClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: PaginationPageItem item } && !item.IsEllipsis && item.PageNumber > 0)
        {
            if (DataContext is ViewModels.ContentBrowserViewModel cbVm)
            {
                cbVm.GoToVersionPageCommand.Execute(item.PageNumber);
            }
            else if (DataContext is ViewModels.ModpacksBrowserViewModel mpVm)
            {
                mpVm.GoToVersionPageCommand.Execute(item.PageNumber);
            }
        }
    }

    private void OnPrevPageClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ViewModels.ContentBrowserViewModel cbVm)
        {
            cbVm.PrevVersionPageCommand.Execute(null);
        }
        else if (DataContext is ViewModels.ModpacksBrowserViewModel mpVm)
        {
            mpVm.PrevVersionPageCommand.Execute(null);
        }
    }

    private void OnNextPageClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ViewModels.ContentBrowserViewModel cbVm)
        {
            cbVm.NextVersionPageCommand.Execute(null);
        }
        else if (DataContext is ViewModels.ModpacksBrowserViewModel mpVm)
        {
            mpVm.NextVersionPageCommand.Execute(null);
        }
    }

    private ModrinthProjectVersion? GetActiveDetailVersion()
    {
        if (DataContext is ViewModels.ContentBrowserViewModel cbVm)
            return cbVm.SelectedDetailVersion;
        if (DataContext is ViewModels.ModpacksBrowserViewModel mpVm)
            return mpVm.SelectedDetailVersion;
        return null;
    }

    private async void CopyTextToClipboard(string text)
    {
        try
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel?.Clipboard != null)
            {
                var transfer = new DataTransfer();
                transfer.Add(DataTransferItem.CreateText(text));
                await topLevel.Clipboard.SetDataAsync(transfer);
            }
        }
        catch { }
    }

    private void OnCopyMavenClicked(object? sender, RoutedEventArgs e)
    {
        var ver = GetActiveDetailVersion();
        if (ver != null && !string.IsNullOrEmpty(ver.MavenCoordinates))
        {
            CopyTextToClipboard(ver.MavenCoordinates);
        }
    }

    private void OnCopyVersionIdClicked(object? sender, RoutedEventArgs e)
    {
        var ver = GetActiveDetailVersion();
        if (ver != null && !string.IsNullOrEmpty(ver.Id))
        {
            CopyTextToClipboard(ver.Id);
        }
    }

    private void OnCopyGradleClicked(object? sender, RoutedEventArgs e)
    {
        var ver = GetActiveDetailVersion();
        if (ver != null && !string.IsNullOrEmpty(ver.GradleSnippet))
        {
            CopyTextToClipboard(ver.GradleSnippet);
        }
    }

    private void OnGalleryItemTapped(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: ModrinthGalleryItem item })
        {
            if (DataContext is ViewModels.ContentBrowserViewModel cbVm)
            {
                cbVm.OpenGalleryModalCommand.Execute(item);
            }
            else if (DataContext is ViewModels.ModpacksBrowserViewModel mpVm)
            {
                mpVm.OpenGalleryModalCommand.Execute(item);
            }
        }
    }

    private void OnGalleryViewerPointerWheel(object? sender, PointerWheelEventArgs e)
    {
        if (DataContext is ViewModels.ContentBrowserViewModel cbVm)
        {
            if (e.Delta.Y > 0) cbVm.GalleryZoomInCommand.Execute(null);
            else if (e.Delta.Y < 0) cbVm.GalleryZoomOutCommand.Execute(null);
            e.Handled = true;
        }
        else if (DataContext is ViewModels.ModpacksBrowserViewModel mpVm)
        {
            if (e.Delta.Y > 0) mpVm.GalleryZoomInCommand.Execute(null);
            else if (e.Delta.Y < 0) mpVm.GalleryZoomOutCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnActionButtonHover(object? sender, PointerEventArgs e)
    {
        if (DataContext is ViewModels.ContentBrowserViewModel cbVm)
        {
            cbVm.IsDetailsInstallButtonHovered = true;
        }
        else if (DataContext is ViewModels.ModpacksBrowserViewModel mpVm)
        {
            mpVm.IsDetailsInstallButtonHovered = true;
        }
    }

    private void OnActionButtonHoverOut(object? sender, PointerEventArgs e)
    {
        if (DataContext is ViewModels.ContentBrowserViewModel cbVm)
        {
            cbVm.IsDetailsInstallButtonHovered = false;
        }
        else if (DataContext is ViewModels.ModpacksBrowserViewModel mpVm)
        {
            mpVm.IsDetailsInstallButtonHovered = false;
        }
    }
    private void OnAuthorTapped(object? sender, RoutedEventArgs e)
    {
        string? author = null;
        if (sender is Control { Tag: string tagStr } && !string.IsNullOrWhiteSpace(tagStr))
        {
            author = tagStr;
        }
        else if (sender is Control { DataContext: ModrinthTeamMember member })
        {
            author = member.Username;
        }
        else if (sender is Control { DataContext: ModrinthProjectVersion version })
        {
            author = version.AuthorName;
        }
        else if (DataContext is ViewModels.ContentBrowserViewModel cbVm)
        {
            author = cbVm.DetailsProject?.Author;
        }
        else if (DataContext is ViewModels.ModpacksBrowserViewModel mpVm)
        {
            author = mpVm.DetailsProject?.Author;
        }

        if (!string.IsNullOrWhiteSpace(author))
        {
            if (DataContext is ViewModels.ContentBrowserViewModel cb)
            {
                cb.OpenAuthorProfileCommand.Execute(author);
            }
            else if (DataContext is ViewModels.ModpacksBrowserViewModel mp)
            {
                mp.OpenAuthorProfileCommand.Execute(author);
            }
        }
    }

    private void OnExternalLinkTagClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: string url } && !string.IsNullOrWhiteSpace(url))
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch { }
        }
    }
}
