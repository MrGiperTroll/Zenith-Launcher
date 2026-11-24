using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CmlLib.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ZenithLauncher.Models;
using ZenithLauncher.Services;
using ZenithLauncher.UI.Theming;
using ZenithLauncher.ViewModels.Common;
using ZenithLauncher.ViewModels.Pages;
using ZenithLauncher.Views;

namespace ZenithLauncher.ViewModels;

public enum LauncherNavPage

{

    Profiles,

    Modpacks,

    EditInstance,

    ContentBrowser,

    Settings,

    ServerManager,

    Accounts,

    CreateProfile,

    CreateServer,

}



public class BreadcrumbItem : ObservableObject

{

    public string Title { get; set; } = string.Empty;

    public bool IsLast { get; set; }

    public bool ShowSeparator { get; set; }

    public bool IsActive => IsLast;

    public Action? OnClick { get; set; }

}



public class HistoryEntry

{

    public LauncherNavPage Page { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? SubTitle { get; set; }

    public InstanceModel? Instance { get; set; }

    public EditInstanceViewModel? EditInstanceVm { get; set; }

    public ContentBrowserViewModel? ContentBrowserVm { get; set; }

    public ServerManagerViewModel? ServerManagerVm { get; set; }

    public CreateProfileViewModel? CreateProfileVm { get; set; }

    public CreateServerViewModel? CreateServerVm { get; set; }

}




public partial class MainWindowViewModel
{
    [ObservableProperty] private string _currentSettingsTab = "Game";

    public bool IsSettingsTabGame => CurrentSettingsTab == "Game";

    public bool IsSettingsTabGeneral => CurrentSettingsTab == "General";

    public bool IsSettingsTabAppearance => CurrentSettingsTab is "Appearance" or "Launcher";

    public bool IsSettingsTabTheme => CurrentSettingsTab == "Theme";

    public bool IsSettingsTabLauncher => IsSettingsTabAppearance;



    [ObservableProperty] private bool _isLockAspectRatio = false;

    [ObservableProperty] private int _maxRamMb = 16384;



    [RelayCommand]

    private void SwitchSettingsTab(string tab)

    {

        if (IsSettingsTabTheme && tab != "Theme" && HasUnsavedThemeChanges())

        {

            _pendingThemeDiscardNavAction = () => SwitchSettingsTab(tab);

            IsThemeDiscardConfirmOpen = true;

            return;

        }



        CurrentSettingsTab = tab;

        OnPropertyChanged(nameof(IsSettingsTabGame));

        OnPropertyChanged(nameof(IsSettingsTabGeneral));

        OnPropertyChanged(nameof(IsSettingsTabAppearance));

        OnPropertyChanged(nameof(IsSettingsTabTheme));

        OnPropertyChanged(nameof(IsSettingsTabLauncher));

    }



    [ObservableProperty]

    private LauncherNavPage _currentPage = LauncherNavPage.Profiles;



    [ObservableProperty]

    private EditInstanceViewModel? _currentEditInstanceVm;



    [ObservableProperty]

    private ContentBrowserViewModel? _currentContentBrowserVm;



    [ObservableProperty]

    private ServerManagerViewModel? _currentServerManagerVm;



    [ObservableProperty]

    private CreateProfileViewModel? _createProfileVm;



    [ObservableProperty]

    private CreateServerViewModel? _createServerVm;



    partial void OnCreateServerVmChanged(CreateServerViewModel? oldValue, CreateServerViewModel? newValue)

    {

        if (oldValue != null)

        {

            oldValue.StateChanged -= OnCreateServerStateChanged;

        }

        if (newValue != null)

        {

            newValue.StateChanged += OnCreateServerStateChanged;

        }

    }



    private void OnCreateServerStateChanged()

    {

        if (CurrentPage == LauncherNavPage.CreateServer)

        {

            if (_historyIndex >= 0 && _historyIndex < _navHistory.Count)

            {

                var cur = _navHistory[_historyIndex];

                if (cur.Page == LauncherNavPage.CreateServer)

                    RebuildBreadcrumbs(cur);

            }

        }

    }



    public bool IsProfilesView => CurrentPage == LauncherNavPage.Profiles;

    public bool IsModpacksView => CurrentPage == LauncherNavPage.Modpacks;

    public bool IsEditInstanceView => CurrentPage == LauncherNavPage.EditInstance;

    public bool IsContentBrowserView => CurrentPage == LauncherNavPage.ContentBrowser;

    public bool IsSettingsView => CurrentPage == LauncherNavPage.Settings;

    public bool IsServerManagerView => CurrentPage == LauncherNavPage.ServerManager;

    public bool IsAccountsView => CurrentPage == LauncherNavPage.Accounts;

    public bool IsCreateProfileView => CurrentPage == LauncherNavPage.CreateProfile;

    public bool IsCreateServerView => CurrentPage == LauncherNavPage.CreateServer;



    [ObservableProperty]

    private bool _isSidebarExpanded;



    public double SidebarWidth => IsSidebarExpanded ? 188 : 54;



    partial void OnIsSidebarExpandedChanged(bool value)

    {

        OnPropertyChanged(nameof(SidebarWidth));

        SaveLauncherConfig();

    }



    [RelayCommand]

    public void ToggleSidebar()

    {

        IsSidebarExpanded = !IsSidebarExpanded;

    }



    public bool IsLaunchBarVisible => IsProfilesView || IsModpacksView;



    public ObservableCollection<BreadcrumbItem> Breadcrumbs { get; } = new();



    private readonly List<HistoryEntry> _navHistory = new();

    private int _historyIndex = -1;



    [ObservableProperty]

    private bool _canGoBack;



    [ObservableProperty]

    private bool _canGoForward;



    [RelayCommand]

    public void GoBack()

    {

        if (CurrentPage == LauncherNavPage.Settings && IsSettingsTabTheme && HasUnsavedThemeChanges())

        {

            _pendingThemeDiscardNavAction = () => GoBack();

            IsThemeDiscardConfirmOpen = true;

            return;

        }



        if (CurrentPage == LauncherNavPage.ContentBrowser && CurrentContentBrowserVm?.SelectedProject != null)

        {

            if (CurrentContentBrowserVm.IsAuthorProfileOpen)

            {

                CurrentContentBrowserVm.CloseAuthorProfile();

                return;

            }

            CurrentContentBrowserVm.SelectedProject = null;

            return;

        }



        if (CurrentPage == LauncherNavPage.Modpacks && ModpacksBrowser?.SelectedProject != null)

        {

            if (ModpacksBrowser.IsAuthorProfileOpen)

            {

                ModpacksBrowser.CloseAuthorProfile();

                return;

            }

            ModpacksBrowser.SelectedProject = null;

            return;

        }



        if (_historyIndex > 0)

        {

            if (_historyIndex >= 0 && _historyIndex < _navHistory.Count)
                DisposeHistoryEntry(_navHistory[_historyIndex]);
            _historyIndex--;

            ApplyHistoryEntry(_navHistory[_historyIndex]);

            UpdateHistoryButtons();

        }

    }



    [RelayCommand]

    public void GoForward()

    {

        if (CurrentPage == LauncherNavPage.Settings && IsSettingsTabTheme && HasUnsavedThemeChanges())

        {

            _pendingThemeDiscardNavAction = () => GoForward();

            IsThemeDiscardConfirmOpen = true;

            return;

        }



        if (_historyIndex >= 0 && _historyIndex < _navHistory.Count - 1)

        {

            if (_historyIndex >= 0 && _historyIndex < _navHistory.Count)
                DisposeHistoryEntry(_navHistory[_historyIndex]);
            _historyIndex++;

            ApplyHistoryEntry(_navHistory[_historyIndex]);

            UpdateHistoryButtons();

        }

    }



    private void UpdateHistoryButtons()

    {

        CanGoBack = _historyIndex > 0;

        CanGoForward = _historyIndex >= 0 && _historyIndex < _navHistory.Count - 1;

    }



    public void NavigateToProfiles()

    {

        PushHistory(new HistoryEntry

        {

            Page = LauncherNavPage.Profiles,

            Title = L10n.T("nav_home")

        });

    }



    public void NavigateToModpacks()

    {

        PushHistory(new HistoryEntry

        {

            Page = LauncherNavPage.Modpacks,

            Title = L10n.T("nav_catalog")

        });

        if (ModpacksBrowser != null && ModpacksBrowser.Results.Count == 0 && !ModpacksBrowser.IsBusy)

        {

            _ = ModpacksBrowser.ReloadAsync();

        }

    }



    public void NavigateToSettings()

    {

        PushHistory(new HistoryEntry

        {

            Page = LauncherNavPage.Settings,

            Title = L10n.T("nav_settings")

        });

    }



    public void NavigateToAccounts()

    {

        RefreshState();

        PushHistory(new HistoryEntry

        {

            Page = LauncherNavPage.Accounts,

            Title = L10n.T("acc_manage")

        });

    }



    public void NavigateToCreateProfile()

    {

        var vm = new CreateProfileViewModel(_instanceService, _modLoaderService,

            UseFlatVersionList, ShowReleases, ShowSnapshots, ShowBetas, ShowAlphas);



        vm.ProfileCreated += inst =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                _instanceService.SetSelectedInstance(inst.Id);
                SelectedInstanceId = inst.Id;
                RefreshState();
                SelectedInstance = Instances.FirstOrDefault(x => x.Id == inst.Id);
                NavigateToProfiles();
            });
        };



        vm.RequestClose += () =>

        {

            Dispatcher.UIThread.Post(() =>

            {

                if (CanGoBack) GoBack();

                else NavigateToProfiles();

            });

        };



        PushHistory(new HistoryEntry

        {

            Page = LauncherNavPage.CreateProfile,

            Title = L10n.T("cp_title"),

            CreateProfileVm = vm

        });

    }



    [ObservableProperty]

    private bool _isServerUnderDevDialogOpen;



    public bool HasExistingServers

    {

        get

        {

            try

            {

                var dir = ServerCreatorService.DefaultServersDirectory;

                return Directory.Exists(dir) && Directory.GetDirectories(dir).Length > 0;

            }

            catch

            {

                return false;

            }

        }

    }



    [RelayCommand]

    public void NavigateToCreateServer()

    {

        IsServerUnderDevDialogOpen = true;

    }



    [RelayCommand]

    public void OpenServersDirectory()

    {

        try

        {

            var dir = ServerCreatorService.DefaultServersDirectory;

            Directory.CreateDirectory(dir);

            Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true, Verb = "open" });

        }

        catch { }

    }



    private void NavigateBackToInstance(HistoryEntry entry, string? tab = null)

    {

        int foundIndex = -1;

        for (int i = _historyIndex - 1; i >= 0; i--)

        {

            if (_navHistory[i].Page == LauncherNavPage.EditInstance && _navHistory[i].Instance == entry.Instance)

            {

                foundIndex = i;

                break;

            }

        }



        if (foundIndex >= 0)

        {

            _historyIndex = foundIndex;

            var hist = _navHistory[_historyIndex];

            if (!string.IsNullOrEmpty(tab) && hist.EditInstanceVm != null)

            {

                hist.SubTitle = tab;

                hist.EditInstanceVm.SelectTab(tab);

            }

            ApplyHistoryEntry(hist);

            UpdateHistoryButtons();

        }

        else if (entry.EditInstanceVm != null)

        {

            var targetTab = tab ?? entry.SubTitle ?? "Overview";

            PushHistory(new HistoryEntry

            {

                Page = LauncherNavPage.EditInstance,

                Title = entry.Instance?.Name ?? entry.Title,

                SubTitle = targetTab,

                Instance = entry.Instance,

                EditInstanceVm = entry.EditInstanceVm

            });

            entry.EditInstanceVm.SelectTab(targetTab);

        }

        else

        {

            GoBack();

        }

    }



    public void NavigateToServerManager(ServerManagerViewModel vm, InstanceModel? instance, EditInstanceViewModel? editVm)

    {

        PushHistory(new HistoryEntry

        {

            Page = LauncherNavPage.ServerManager,

            Title = instance?.Name ?? L10n.T("sm_title"),

            SubTitle = L10n.T("tab_servers"),

            Instance = instance,

            EditInstanceVm = editVm,

            ServerManagerVm = vm

        });

    }



    public void NavigateToEditInstance(InstanceModel instance, string? tab = "Overview")

    {

        var vm = new EditInstanceViewModel(instance, this, _instanceService);

        if (!string.IsNullOrEmpty(tab))

            vm.SelectTab(tab);



        PushHistory(new HistoryEntry

        {

            Page = LauncherNavPage.EditInstance,

            Title = instance.Name,

            SubTitle = vm.SelectedTab,

            Instance = instance,

            EditInstanceVm = vm

        });

    }



    public void NavigateToContentBrowser(ContentBrowserViewModel vm, InstanceModel? instance, string parentTitle, string sectionTitle, EditInstanceViewModel? editVm)

    {

        vm.OnProjectSelectedChanged = _ =>

        {

            if (_historyIndex >= 0 && _historyIndex < _navHistory.Count)

            {

                var cur = _navHistory[_historyIndex];

                if (cur.Page == LauncherNavPage.ContentBrowser)

                    RebuildBreadcrumbs(cur);

            }

        };

        vm.OnAuthorProfileChanged = _ =>

        {

            if (_historyIndex >= 0 && _historyIndex < _navHistory.Count)

            {

                var cur = _navHistory[_historyIndex];

                if (cur.Page == LauncherNavPage.ContentBrowser)

                    RebuildBreadcrumbs(cur);

            }

        };



        PushHistory(new HistoryEntry

        {

            Page = LauncherNavPage.ContentBrowser,

            Title = parentTitle,

            SubTitle = sectionTitle,

            Instance = instance,

            EditInstanceVm = editVm,

            ContentBrowserVm = vm

        });

    }



    public void UpdateActiveInstanceTab(string instanceName, string tab)

    {

        if (_historyIndex >= 0 && _historyIndex < _navHistory.Count)

        {

            var cur = _navHistory[_historyIndex];

            if (cur.Page == LauncherNavPage.EditInstance)

            {

                cur.SubTitle = tab;

                RebuildBreadcrumbs(cur);

            }

        }

    }



    private DateTime _lastForcedGc = DateTime.MinValue;



    private static void DisposeHistoryEntry(HistoryEntry entry)

    {

        // Break event roots (timers, host/instance subscriptions) so GC can reclaim the page.

        try { (entry.EditInstanceVm as IDisposable)?.Dispose(); } catch { }
        entry.EditInstanceVm = null;
        try { (entry.ContentBrowserVm as IDisposable)?.Dispose(); } catch { }
        entry.ContentBrowserVm = null;
        try { (entry.CreateServerVm as IDisposable)?.Dispose(); } catch { }
        entry.CreateServerVm = null;
        try { (entry.CreateProfileVm as IDisposable)?.Dispose(); } catch { }
        entry.CreateProfileVm = null;
        entry.ServerManagerVm = null;

    }



    private void PushHistory(HistoryEntry entry)

    {

        if (CurrentPage == LauncherNavPage.Settings && IsSettingsTabTheme && entry.Page != LauncherNavPage.Settings && HasUnsavedThemeChanges())

        {

            _pendingThemeDiscardNavAction = () => PushHistory(entry);

            IsThemeDiscardConfirmOpen = true;

            return;

        }



        if (_historyIndex >= 0 && _historyIndex < _navHistory.Count - 1)

        {

            var dropped = _navHistory.Skip(_historyIndex + 1).ToList();

            _navHistory.RemoveRange(_historyIndex + 1, _navHistory.Count - (_historyIndex + 1));

            foreach (var d in dropped) DisposeHistoryEntry(d);

        }



        if (_historyIndex >= 0 && _historyIndex < _navHistory.Count)
        {
            DisposeHistoryEntry(_navHistory[_historyIndex]);
        }

        _navHistory.Add(entry);



        while (_navHistory.Count > 10)

        {

            var evicted = _navHistory[0];

            _navHistory.RemoveAt(0);

            DisposeHistoryEntry(evicted);

        }

        _historyIndex = _navHistory.Count - 1;

        ApplyHistoryEntry(entry);

        UpdateHistoryButtons();

    }



    private void ApplyHistoryEntry(HistoryEntry entry)

    {

        var prevPage = CurrentPage;
        if (prevPage != entry.Page)
        {
            if (prevPage == LauncherNavPage.Settings)
            {
                SaveLauncherConfig();
            }
            else if (prevPage == LauncherNavPage.Accounts)
            {
                AccountsVm.ResetAuthUiState();
            }
            else if (prevPage == LauncherNavPage.ServerManager)
            {
                CurrentEditInstanceVm?.RefreshServers();
                try { (CurrentServerManagerVm as IDisposable)?.Dispose(); } catch { }
                CurrentServerManagerVm = null;
            }
            else if (prevPage == LauncherNavPage.Modpacks)
            {
                try { ModpacksBrowser?.Dispose(); } catch { }
            }
            else if (prevPage == LauncherNavPage.ContentBrowser)
            {
                try { CurrentContentBrowserVm?.Dispose(); } catch { }
                CurrentContentBrowserVm = null;
            }
            else if (prevPage == LauncherNavPage.EditInstance)
            {
                try { CurrentEditInstanceVm?.Dispose(); } catch { }
                CurrentEditInstanceVm = null;
            }
            else if (prevPage == LauncherNavPage.CreateServer)
            {
                try { CreateServerVm?.Dispose(); } catch { }
                CreateServerVm = null;
            }
            else if (prevPage == LauncherNavPage.CreateProfile)
            {
                try { CreateProfileVm?.Dispose(); } catch { }
                CreateProfileVm = null;
            }

            Services.MemoryTrimmer.TrimMemory(force: true);
        }

        // Recreate or restore view models for entering page if disposed
        if (entry.Page == LauncherNavPage.EditInstance)
        {
            if (entry.EditInstanceVm == null && entry.Instance != null)
            {
                entry.EditInstanceVm = new EditInstanceViewModel(entry.Instance, this, _instanceService);
                if (!string.IsNullOrEmpty(entry.SubTitle))
                    entry.EditInstanceVm.SelectTab(entry.SubTitle);
            }
        }
        else if (entry.Page == LauncherNavPage.Modpacks)
        {
            if (ModpacksBrowser != null && ModpacksBrowser.Results.Count == 0)
            {
                _ = ModpacksBrowser.ReloadAsync();
            }
        }
        else if (entry.Page == LauncherNavPage.ContentBrowser)
        {
            if (entry.ContentBrowserVm == null && entry.Instance != null)
            {
                var editVm = entry.EditInstanceVm ?? CurrentEditInstanceVm;
                var contentType = entry.SubTitle switch
                {
                    "Resource Packs" => Models.ContentType.ResourcePack,
                    "Shader Packs" => Models.ContentType.Shader,
                    "Data Packs" => Models.ContentType.DataPack,
                    _ => Models.ContentType.Mod
                };
                entry.ContentBrowserVm = new ContentBrowserViewModel(
                    entry.Instance.Version,
                    entry.Instance.LoaderType,
                    entry.Instance.Path,
                    msg => StatusText = msg,
                    () => editVm?.RefreshFileLists(),
                    () => editVm?.HasModsSupport ?? true,
                    contentType)
                {
                    GetCurseForgeApiKey = () => LauncherSettingsVm?.CurseForgeApiKey ?? ""
                };
            }
        }
        else if (entry.Page == LauncherNavPage.ServerManager)
        {
            if (entry.ServerManagerVm == null && entry.Instance != null)
            {
                entry.ServerManagerVm = new ServerManagerViewModel(Path.Combine(entry.Instance.Path, "servers.dat"));
            }
        }
        else if (entry.Page == LauncherNavPage.CreateProfile)
        {
            if (entry.CreateProfileVm == null)
            {
                var vm = new CreateProfileViewModel(_instanceService, _modLoaderService,
                    UseFlatVersionList, ShowReleases, ShowSnapshots, ShowBetas, ShowAlphas);
                vm.ProfileCreated += inst =>
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        _instanceService.SetSelectedInstance(inst.Id);
                        SelectedInstanceId = inst.Id;
                        RefreshState();
                        SelectedInstance = Instances.FirstOrDefault(x => x.Id == inst.Id);
                        NavigateToProfiles();
                    });
                };
                vm.RequestClose += () =>
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (CanGoBack) GoBack();
                        else NavigateToProfiles();
                    });
                };
                entry.CreateProfileVm = vm;
            }
        }



        CurrentPage = entry.Page;

        CurrentEditInstanceVm = entry.EditInstanceVm;

        CurrentContentBrowserVm = entry.ContentBrowserVm;

        CurrentServerManagerVm = entry.ServerManagerVm;

        CreateProfileVm = entry.CreateProfileVm;

        CreateServerVm = entry.CreateServerVm;



        if (CurrentContentBrowserVm != null)

        {

            CurrentContentBrowserVm.OnProjectSelectedChanged = _ =>

            {

                if (_historyIndex >= 0 && _historyIndex < _navHistory.Count)

                {

                    var cur = _navHistory[_historyIndex];

                    if (cur.Page == LauncherNavPage.ContentBrowser)

                        RebuildBreadcrumbs(cur);

                }

            };

            CurrentContentBrowserVm.OnAuthorProfileChanged = _ =>

            {

                if (_historyIndex >= 0 && _historyIndex < _navHistory.Count)

                {

                    var cur = _navHistory[_historyIndex];

                    if (cur.Page == LauncherNavPage.ContentBrowser)

                        RebuildBreadcrumbs(cur);

                }

            };

        }



        OnPropertyChanged(nameof(IsProfilesView));

        OnPropertyChanged(nameof(IsModpacksView));

        OnPropertyChanged(nameof(IsEditInstanceView));

        OnPropertyChanged(nameof(IsContentBrowserView));

        OnPropertyChanged(nameof(IsSettingsView));

        OnPropertyChanged(nameof(IsServerManagerView));

        OnPropertyChanged(nameof(IsAccountsView));

        OnPropertyChanged(nameof(IsCreateProfileView));

        OnPropertyChanged(nameof(IsCreateServerView));

        OnPropertyChanged(nameof(IsLaunchBarVisible));



        RebuildBreadcrumbs(entry);
        Services.MemoryTrimmer.TrimMemory();

        switch (entry.Page)

        {

            case LauncherNavPage.Profiles:

                Services.DiscordPresenceService.SetBrowsingProfiles();

                break;

            case LauncherNavPage.Modpacks:

                Services.DiscordPresenceService.SetBrowsingModpacks();

                break;

            case LauncherNavPage.EditInstance:

                if (entry.EditInstanceVm != null)

                    Services.DiscordPresenceService.SetEditingInstance(entry.EditInstanceVm.Instance.Name);

                break;

            case LauncherNavPage.ContentBrowser:

                Services.DiscordPresenceService.SetBrowsingMods();

                break;

            case LauncherNavPage.Settings:

                Services.DiscordPresenceService.SetInSettings();

                break;

            case LauncherNavPage.Accounts:

                Services.DiscordPresenceService.SetCreatingAccount();

                break;

            case LauncherNavPage.CreateProfile:

                Services.DiscordPresenceService.SetEditingInstance("Creating Profile");

                break;

            case LauncherNavPage.CreateServer:

                Services.DiscordPresenceService.SetEditingInstance("Creating Server");

                break;

            case LauncherNavPage.ServerManager:

                if (entry.EditInstanceVm != null)

                    Services.DiscordPresenceService.SetEditingInstance(entry.EditInstanceVm.Instance.Name);

                break;

        }

    }



    private void RebuildBreadcrumbs(HistoryEntry entry)

    {

        Breadcrumbs.Clear();

        var homeItem = new BreadcrumbItem

        {

            Title = L10n.T("nav_home"),

            IsLast = entry.Page == LauncherNavPage.Profiles,

            OnClick = () => NavigateToProfiles()

        };

        Breadcrumbs.Add(homeItem);



        switch (entry.Page)

        {

            case LauncherNavPage.Modpacks:

                if (ModpacksBrowser?.SelectedProject != null)

                {

                    bool isAuthor = ModpacksBrowser.IsAuthorProfileOpen && ModpacksBrowser.CurrentAuthorProfileVm != null;

                    Breadcrumbs.Add(new BreadcrumbItem

                    {

                        Title = L10n.T("nav_catalog"),

                        IsLast = false,

                        OnClick = () =>

                        {

                            if (ModpacksBrowser != null)

                            {

                                ModpacksBrowser.CloseAuthorProfile();

                                ModpacksBrowser.SelectedProject = null;

                            }

                        }

                    });

                    Breadcrumbs.Add(new BreadcrumbItem

                    {

                        Title = ModpacksBrowser.SelectedProject.Title,

                        IsLast = !isAuthor,

                        OnClick = () =>

                        {

                            if (ModpacksBrowser != null)

                                ModpacksBrowser.CloseAuthorProfile();

                        }

                    });

                    if (isAuthor)

                    {

                        var authorName = !string.IsNullOrWhiteSpace(ModpacksBrowser.CurrentAuthorProfileVm!.Username)

                            ? ModpacksBrowser.CurrentAuthorProfileVm.Username

                            : ModpacksBrowser.SelectedProject.Author;

                        Breadcrumbs.Add(new BreadcrumbItem

                        {

                            Title = authorName,

                            IsLast = true

                        });

                    }

                }

                else

                {

                    Breadcrumbs.Add(new BreadcrumbItem

                    {

                        Title = L10n.T("nav_catalog"),

                        IsLast = true,

                        OnClick = () => NavigateToModpacks()

                    });

                }

                break;



            case LauncherNavPage.EditInstance:

                var instName = entry.Instance?.Name ?? entry.Title;

                var currentTab = string.IsNullOrWhiteSpace(entry.SubTitle) ? "Overview" : entry.SubTitle;

                Breadcrumbs.Add(new BreadcrumbItem

                {

                    Title = instName,

                    IsLast = false,

                    OnClick = () =>

                    {

                        if (entry.EditInstanceVm != null)

                            entry.EditInstanceVm.SelectTab("Overview");

                    }

                });

                Breadcrumbs.Add(new BreadcrumbItem

                {

                    Title = GetLocalizedTabTitle(currentTab),

                    IsLast = true,

                    OnClick = () => entry.EditInstanceVm?.SelectTab(currentTab)

                });

                break;



            case LauncherNavPage.ContentBrowser:

                var parentName = entry.Title;

                Breadcrumbs.Add(new BreadcrumbItem

                {

                    Title = parentName,

                    IsLast = false,

                    OnClick = () =>

                    {

                        NavigateBackToInstance(entry, "Overview");

                    }

                });



                if (!string.IsNullOrWhiteSpace(entry.SubTitle) && entry.SubTitle != "Overview")

                {

                    Breadcrumbs.Add(new BreadcrumbItem

                    {

                        Title = GetLocalizedTabTitle(entry.SubTitle),

                        IsLast = false,

                        OnClick = () =>

                        {

                            NavigateBackToInstance(entry, entry.SubTitle);

                        }

                    });

                }



                var hasSelectedProj = CurrentContentBrowserVm?.SelectedProject != null;

                bool isAuthorContent = hasSelectedProj && CurrentContentBrowserVm!.IsAuthorProfileOpen && CurrentContentBrowserVm.CurrentAuthorProfileVm != null;

                Breadcrumbs.Add(new BreadcrumbItem

                {

                    Title = L10n.T("cb_title"),

                    IsLast = !hasSelectedProj,

                    OnClick = () =>

                    {

                        if (CurrentContentBrowserVm != null)

                        {

                            CurrentContentBrowserVm.CloseAuthorProfile();

                            CurrentContentBrowserVm.SelectedProject = null;

                        }

                    }

                });



                if (hasSelectedProj && CurrentContentBrowserVm?.SelectedProject != null)

                {

                    Breadcrumbs.Add(new BreadcrumbItem

                    {

                        Title = CurrentContentBrowserVm.SelectedProject.Title,

                        IsLast = !isAuthorContent,

                        OnClick = () =>

                        {

                            if (CurrentContentBrowserVm != null)

                                CurrentContentBrowserVm.CloseAuthorProfile();

                        }

                    });



                    if (isAuthorContent)

                    {

                        var authorName = !string.IsNullOrWhiteSpace(CurrentContentBrowserVm.CurrentAuthorProfileVm!.Username)

                            ? CurrentContentBrowserVm.CurrentAuthorProfileVm.Username

                            : CurrentContentBrowserVm.SelectedProject.Author;

                        Breadcrumbs.Add(new BreadcrumbItem

                        {

                            Title = authorName,

                            IsLast = true

                        });

                    }

                }

                break;



            case LauncherNavPage.Settings:

                Breadcrumbs.Add(new BreadcrumbItem

                {

                    Title = L10n.T("nav_settings"),

                    IsLast = true,

                    OnClick = () => NavigateToSettings()

                });

                break;



            case LauncherNavPage.Accounts:

                Breadcrumbs.Add(new BreadcrumbItem

                {

                    Title = L10n.T("acc_manage"),

                    IsLast = true,

                    OnClick = () => NavigateToAccounts()

                });

                break;



            case LauncherNavPage.CreateProfile:

                Breadcrumbs.Add(new BreadcrumbItem

                {

                    Title = L10n.T("cp_title"),

                    IsLast = true,

                    OnClick = () => NavigateToCreateProfile()

                });

                break;



            case LauncherNavPage.CreateServer:

                Breadcrumbs.Add(new BreadcrumbItem

                {

                    Title = L10n.T("nav_create_server"),

                    IsLast = false,

                    OnClick = () =>

                    {

                        if (CreateServerVm != null && CreateServerVm.ExistingServers.Count > 0)

                        {

                            CreateServerVm.IsDashboardMode = true;

                            CreateServerVm.IsCreatingNewServer = false;

                        }

                        RebuildBreadcrumbs(entry);

                    }

                });



                if (CreateServerVm != null && CreateServerVm.IsDashboardMode && !CreateServerVm.IsCreatingNewServer && !string.IsNullOrWhiteSpace(CreateServerVm.SelectedServerName))

                {

                    Breadcrumbs.Add(new BreadcrumbItem

                    {

                        Title = CreateServerVm.SelectedServerName,

                        IsLast = false,

                        OnClick = () =>

                        {

                            CreateServerVm.SelectedTab = 0;

                            RebuildBreadcrumbs(entry);

                        }

                    });



                    string tabTitle = CreateServerVm.SelectedTab switch

                    {

                        0 => L10n.T("cs_tab_overview"),

                        1 => L10n.T("cs_tab_properties"),

                        2 => CreateServerVm.PluginsTabTitle,

                        3 => L10n.T("cs_tab_settings"),

                        4 => L10n.T("cs_tab_players"),

                        5 => L10n.T("cs_tab_files"),

                        6 => L10n.T("cs_tab_console"),

                        7 => L10n.T("cs_tab_world"),

                        _ => L10n.T("nav_create_server")

                    };



                    Breadcrumbs.Add(new BreadcrumbItem

                    {

                        Title = tabTitle,

                        IsLast = true

                    });

                }

                else

                {

                    Breadcrumbs.Add(new BreadcrumbItem

                    {

                        Title = L10n.T("cs_title"),

                        IsLast = true

                    });

                }

                break;



            case LauncherNavPage.ServerManager:

                if (entry.Instance != null)

                {

                    Breadcrumbs.Add(new BreadcrumbItem

                    {

                        Title = entry.Instance.Name,

                        IsLast = false,

                        OnClick = () =>

                        {

                            entry.EditInstanceVm?.RefreshServers();

                            NavigateBackToInstance(entry, "Servers");

                        }

                    });

                }

                Breadcrumbs.Add(new BreadcrumbItem

                {

                    Title = L10n.T("tab_servers"),

                    IsLast = true

                });

                break;

        }



        for (int i = 0; i < Breadcrumbs.Count; i++)

        {

            Breadcrumbs[i].ShowSeparator = (i > 0);

        }

    }



    private static string GetLocalizedTabTitle(string tab) => tab switch

    {

        "Overview" => L10n.T("tab_overview"),

        "Logs" => L10n.T("tab_logs"),

        "Mods" => L10n.T("tab_mods"),

        "Resource Packs" => L10n.T("tab_resourcepacks"),

        "Shader Packs" => L10n.T("tab_shaders"),

        "Data Packs" => L10n.T("tab_datapacks"),

        "Worlds" => L10n.T("tab_worlds"),

        "Servers" => L10n.T("tab_servers"),

        "Screenshots" => L10n.T("tab_screenshots"),

        "Settings" or "General" => L10n.T("set_general"),

        _ => tab

    };



    [RelayCommand]

    private void NavigateToBreadcrumb(BreadcrumbItem? item)

    {

        if (item == null || item.IsLast) return;

        item.OnClick?.Invoke();

    }



    [RelayCommand]

    private void ShowProfiles()

    {

        NavigateToProfiles();

    }



    [RelayCommand]

    private void ShowModpacks()

    {

        NavigateToModpacks();

    }






}
