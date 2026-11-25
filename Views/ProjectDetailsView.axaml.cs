using System;
using System.ComponentModel;
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

    public ProjectDetailsView()
    {
        InitializeComponent();
        SmoothScrollBehavior.Attach(DetailsScroll, wheelStep: 90.0, damping: 14.0);
        DataContextChanged += OnDataContextChanged;
        DetachedFromVisualTree += (_, _) => UnsubscribeVm();
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        UnsubscribeVm();
        if (DataContext is INotifyPropertyChanged npc)
        {
            _boundVm = npc;
            _boundVm.PropertyChanged += OnVmPropertyChanged;
        }
    }

    private void UnsubscribeVm()
    {
        if (_boundVm != null)
        {
            _boundVm.PropertyChanged -= OnVmPropertyChanged;
            _boundVm = null;
        }
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ViewModels.ContentBrowserViewModel.IsVersionDetailActive)
            or nameof(ViewModels.ModpacksBrowserViewModel.IsVersionDetailActive))
        {
            bool versionDetail = DataContext switch
            {
                ViewModels.ContentBrowserViewModel cb => cb.IsVersionDetailActive,
                ViewModels.ModpacksBrowserViewModel mp => mp.IsVersionDetailActive,
                _ => false
            };

            if (versionDetail)
                ZenithMotion.AnimateEntrance(VersionDetailPanel, offsetX: 16, offsetY: 0, durationMs: 165);
            else
                ZenithMotion.AnimateEntrance(RegularTabsPanel, offsetX: -14, offsetY: 0, startOpacity: 0.35, durationMs: 155);
        }
        else if (e.PropertyName is nameof(ViewModels.ContentBrowserViewModel.SelectedDetailTab)
            or nameof(ViewModels.ModpacksBrowserViewModel.SelectedDetailTab))
        {
            string tab = DataContext switch
            {
                ViewModels.ContentBrowserViewModel cb when !cb.IsVersionDetailActive => cb.SelectedDetailTab,
                ViewModels.ModpacksBrowserViewModel mp when !mp.IsVersionDetailActive => mp.SelectedDetailTab,
                _ => string.Empty
            };

            Control? panel = tab.ToLowerInvariant() switch
            {
                "description" => DescriptionPanel,
                "gallery" => GalleryPanel,
                "changelog" => ChangelogPanel,
                "versions" => VersionsPanel,
                _ => null
            };
            ZenithMotion.TransitionTab(panel);
        }
    }

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
