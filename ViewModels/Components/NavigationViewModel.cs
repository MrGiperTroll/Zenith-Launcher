using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ZenithLauncher.Models;
using ZenithLauncher.Services;
using ZenithLauncher.ViewModels.Common;
using ZenithLauncher.ViewModels.Pages;

using ZenithLauncher.ViewModels;

namespace ZenithLauncher.ViewModels.Components;

public partial class NavigationViewModel : ViewModelBase
{
    private readonly Func<MainWindowViewModel> _getHost;
    private readonly IInstanceService _instanceService;
    private readonly ModLoaderService _modLoaderService;

    public Func<bool>? HasUnsavedThemeChanges { get; set; }
    public Action<Action>? RequestThemeDiscardConfirmation { get; set; }
    public Action? SaveLauncherConfigRequested { get; set; }
    public Action? RefreshStateRequested { get; set; }
    public Action<string>? StatusMessageRequested { get; set; }
    public Func<ModpacksBrowserViewModel?>? GetModpacksBrowser { get; set; }
    public Func<string>? GetCurseForgeApiKey { get; set; }
    public Func<bool>? GetUseFlatVersionList { get; set; }
    public Func<bool>? GetShowReleases { get; set; }
    public Func<bool>? GetShowSnapshots { get; set; }
    public Func<bool>? GetShowBetas { get; set; }
    public Func<bool>? GetShowAlphas { get; set; }
    public Func<int>? GetDefaultRamMb { get; set; }
    public Func<IEnumerable<InstanceModel>>? GetInstances { get; set; }
    public Action<InstanceModel?>? SetSelectedInstance { get; set; }
    public Action<string>? SetSelectedInstanceId { get; set; }

    [ObservableProperty]
    private string _currentSettingsTab = "Game";

    public bool IsSettingsTabGame => CurrentSettingsTab == "Game";
    public bool IsSettingsTabGeneral => CurrentSettingsTab == "General";
    public bool IsSettingsTabAppearance => CurrentSettingsTab is "Appearance" or "Launcher";
    public bool IsSettingsTabTheme => CurrentSettingsTab == "Theme";
    public bool IsSettingsTabLauncher => IsSettingsTabAppearance;

    [RelayCommand]
    public void SwitchSettingsTab(string tab)
    {
        if (IsSettingsTabTheme && tab != "Theme" && HasUnsavedThemeChanges?.Invoke() == true)
        {
            RequestThemeDiscardConfirmation?.Invoke(() => SwitchSettingsTab(tab));
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
        SaveLauncherConfigRequested?.Invoke();
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

    public NavigationViewModel(
        Func<MainWindowViewModel> getHost,
        IInstanceService instanceService,
        ModLoaderService modLoaderService)
    {
        _getHost = getHost;
        _instanceService = instanceService;
        _modLoaderService = modLoaderService;
    }

    [RelayCommand]
    public void GoBack()
    {
        if (CurrentPage == LauncherNavPage.Settings && IsSettingsTabTheme && HasUnsavedThemeChanges?.Invoke() == true)
        {
            RequestThemeDiscardConfirmation?.Invoke(() => GoBack());
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

        var modpacks = GetModpacksBrowser?.Invoke();
        if (CurrentPage == LauncherNavPage.Modpacks && modpacks?.SelectedProject != null)
        {
            if (modpacks.IsAuthorProfileOpen)
            {
                modpacks.CloseAuthorProfile();
                return;
            }
            modpacks.SelectedProject = null;
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
        if (CurrentPage == LauncherNavPage.Settings && IsSettingsTabTheme && HasUnsavedThemeChanges?.Invoke() == true)
        {
            RequestThemeDiscardConfirmation?.Invoke(() => GoForward());
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

        var modpacks = GetModpacksBrowser?.Invoke();
        if (modpacks != null && modpacks.Results.Count == 0 && !modpacks.IsBusy)
        {
            _ = modpacks.ReloadAsync();
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
        RefreshStateRequested?.Invoke();
        PushHistory(new HistoryEntry
        {
            Page = LauncherNavPage.Accounts,
            Title = L10n.T("acc_manage")
        });
    }

    public void NavigateToCreateProfile()
    {
        var vm = new CreateProfileViewModel(
            _instanceService,
            _modLoaderService,
            GetUseFlatVersionList?.Invoke() ?? false,
            GetShowReleases?.Invoke() ?? true,
            GetShowSnapshots?.Invoke() ?? false,
            GetShowBetas?.Invoke() ?? false,
            GetShowAlphas?.Invoke() ?? false);

        var defaultRam = GetDefaultRamMb?.Invoke() ?? 4096;
        if (defaultRam > 0)
        {
            vm.SelectedRamMb = defaultRam;
        }

        vm.ProfileCreated += inst =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                _instanceService.SetSelectedInstance(inst.Id);
                SetSelectedInstanceId?.Invoke(inst.Id);
                RefreshStateRequested?.Invoke();
                var match = GetInstances?.Invoke()?.FirstOrDefault(x => x.Id == inst.Id);
                SetSelectedInstance?.Invoke(match);
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
        var vm = new EditInstanceViewModel(instance, _getHost(), _instanceService);
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

    public void RebuildActiveBreadcrumbs()
    {
        if (_historyIndex >= 0 && _historyIndex < _navHistory.Count)
            RebuildBreadcrumbs(_navHistory[_historyIndex]);
    }

    private static void DisposeHistoryEntry(HistoryEntry entry)
    {
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

    public void PushHistory(HistoryEntry entry)
    {
        if (CurrentPage == LauncherNavPage.Settings && IsSettingsTabTheme && entry.Page != LauncherNavPage.Settings && HasUnsavedThemeChanges?.Invoke() == true)
        {
            RequestThemeDiscardConfirmation?.Invoke(() => PushHistory(entry));
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
                SaveLauncherConfigRequested?.Invoke();
            }
            else if (prevPage == LauncherNavPage.Accounts)
            {
                _getHost().AccountsVm.ResetAuthUiState();
            }
            else if (prevPage == LauncherNavPage.ServerManager)
            {
                CurrentEditInstanceVm?.RefreshServers();
                try { (CurrentServerManagerVm as IDisposable)?.Dispose(); } catch { }
                CurrentServerManagerVm = null;
            }
            else if (prevPage == LauncherNavPage.Modpacks)
            {
                try { GetModpacksBrowser?.Invoke()?.Dispose(); } catch { }
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

            MemoryTrimmer.TrimMemory(force: true);
        }

        if (entry.Page == LauncherNavPage.EditInstance)
        {
            if (entry.EditInstanceVm == null && entry.Instance != null)
            {
                entry.EditInstanceVm = new EditInstanceViewModel(entry.Instance, _getHost(), _instanceService);
                if (!string.IsNullOrEmpty(entry.SubTitle))
                    entry.EditInstanceVm.SelectTab(entry.SubTitle);
            }
        }
        else if (entry.Page == LauncherNavPage.Modpacks)
        {
            var modpacks = GetModpacksBrowser?.Invoke();
            if (modpacks != null && modpacks.Results.Count == 0)
            {
                _ = modpacks.ReloadAsync();
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
                    msg => StatusMessageRequested?.Invoke(msg),
                    () => editVm?.RefreshFileLists(),
                    () => editVm?.HasModsSupport ?? true,
                    contentType)
                {
                    GetCurseForgeApiKey = () => GetCurseForgeApiKey?.Invoke() ?? string.Empty
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
                var vm = new CreateProfileViewModel(
                    _instanceService,
                    _modLoaderService,
                    GetUseFlatVersionList?.Invoke() ?? false,
                    GetShowReleases?.Invoke() ?? true,
                    GetShowSnapshots?.Invoke() ?? false,
                    GetShowBetas?.Invoke() ?? false,
                    GetShowAlphas?.Invoke() ?? false);

                var defaultRam = GetDefaultRamMb?.Invoke() ?? 4096;
                if (defaultRam > 0)
                {
                    vm.SelectedRamMb = defaultRam;
                }

                vm.ProfileCreated += inst =>
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        _instanceService.SetSelectedInstance(inst.Id);
                        SetSelectedInstanceId?.Invoke(inst.Id);
                        RefreshStateRequested?.Invoke();
                        var match = GetInstances?.Invoke()?.FirstOrDefault(x => x.Id == inst.Id);
                        SetSelectedInstance?.Invoke(match);
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
        MemoryTrimmer.TrimMemory();

        switch (entry.Page)
        {
            case LauncherNavPage.Profiles:
                DiscordPresenceService.SetBrowsingProfiles();
                break;
            case LauncherNavPage.Modpacks:
                DiscordPresenceService.SetBrowsingModpacks();
                break;
            case LauncherNavPage.EditInstance:
                if (entry.EditInstanceVm != null)
                    DiscordPresenceService.SetEditingInstance(entry.EditInstanceVm.Instance.Name);
                break;
            case LauncherNavPage.ContentBrowser:
                DiscordPresenceService.SetBrowsingMods();
                break;
            case LauncherNavPage.Settings:
                DiscordPresenceService.SetInSettings();
                break;
            case LauncherNavPage.Accounts:
                DiscordPresenceService.SetCreatingAccount();
                break;
            case LauncherNavPage.CreateProfile:
                DiscordPresenceService.SetEditingInstance("Creating Profile");
                break;
            case LauncherNavPage.CreateServer:
                DiscordPresenceService.SetEditingInstance("Creating Server");
                break;
            case LauncherNavPage.ServerManager:
                if (entry.EditInstanceVm != null)
                    DiscordPresenceService.SetEditingInstance(entry.EditInstanceVm.Instance.Name);
                break;
        }
    }

    public void RebuildBreadcrumbs(HistoryEntry entry)
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
                var modpacks = GetModpacksBrowser?.Invoke();
                if (modpacks?.SelectedProject != null)
                {
                    bool isAuthor = modpacks.IsAuthorProfileOpen && modpacks.CurrentAuthorProfileVm != null;
                    Breadcrumbs.Add(new BreadcrumbItem
                    {
                        Title = L10n.T("nav_catalog"),
                        IsLast = false,
                        OnClick = () =>
                        {
                            modpacks.CloseAuthorProfile();
                            modpacks.SelectedProject = null;
                        }
                    });
                    Breadcrumbs.Add(new BreadcrumbItem
                    {
                        Title = modpacks.SelectedProject.Title,
                        IsLast = !isAuthor,
                        OnClick = () => modpacks.CloseAuthorProfile()
                    });
                    if (isAuthor)
                    {
                        var authorName = !string.IsNullOrWhiteSpace(modpacks.CurrentAuthorProfileVm!.Username)
                            ? modpacks.CurrentAuthorProfileVm.Username
                            : modpacks.SelectedProject.Author;
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
                    OnClick = () => entry.EditInstanceVm?.SelectTab("Overview")
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
                    OnClick = () => NavigateBackToInstance(entry, "Overview")
                });

                if (!string.IsNullOrWhiteSpace(entry.SubTitle) && entry.SubTitle != "Overview")
                {
                    Breadcrumbs.Add(new BreadcrumbItem
                    {
                        Title = GetLocalizedTabTitle(entry.SubTitle),
                        IsLast = false,
                        OnClick = () => NavigateBackToInstance(entry, entry.SubTitle)
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
                        OnClick = () => CurrentContentBrowserVm?.CloseAuthorProfile()
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
        "Settings" or "General" => L10n.T("tab_settings"),
        _ => tab
    };

    [RelayCommand]
    public void NavigateToBreadcrumb(BreadcrumbItem? item)
    {
        if (item == null || item.IsLast) return;
        item.OnClick?.Invoke();
    }

    [RelayCommand]
    public void ShowProfiles() => NavigateToProfiles();

    [RelayCommand]
    public void ShowModpacks() => NavigateToModpacks();
}
