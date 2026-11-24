using System;

using System.Collections.Generic;

using System.Collections.ObjectModel;

using System.Diagnostics;

using System.IO;

using System.IO.Compression;

using System.Linq;

using System.Net.Http;

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
using ZenithLauncher.ViewModels.Components;
using ZenithLauncher.Views;

namespace ZenithLauncher.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    public ConsoleViewModel Console { get; } = new();

    public string ConsoleLogs
    {
        get => Console.ConsoleLogs;
        set => Console.ConsoleLogs = value;
    }

    public bool IsConsoleDialogOpen
    {
        get => Console.IsConsoleDialogOpen;
        set => Console.IsConsoleDialogOpen = value;
    }

    public IRelayCommand OpenConsoleDialogCommand => Console.OpenConsoleDialogCommand;
    public IRelayCommand CloseConsoleDialogCommand => Console.CloseConsoleDialogCommand;
    public IAsyncRelayCommand CopyConsoleLogsCommand => Console.CopyConsoleLogsCommand;

    public LaunchViewModel Launch { get; }

    public bool IsLaunching
    {
        get => Launch.IsLaunching;
        set => Launch.IsLaunching = value;
    }

    public double LaunchProgress
    {
        get => Launch.LaunchProgress;
        set => Launch.LaunchProgress = value;
    }

    public string CurrentDownloadFile
    {
        get => Launch.CurrentDownloadFile;
        set => Launch.CurrentDownloadFile = value;
    }

    public bool CanLaunch => Launch.CanLaunch;
    public bool CanLaunchOrCancel => Launch.CanLaunchOrCancel;
    public string LaunchButtonText => Launch.LaunchButtonText;

    public IAsyncRelayCommand LaunchOrCancelCommand => Launch.LaunchOrCancelCommand;
    public IAsyncRelayCommand LaunchGameCommand => Launch.LaunchGameCommand;

    public Task LaunchGameAsync() => Launch.LaunchGameAsync();
    public Task LaunchGameWithArgsAsync(string? extraArgs) => Launch.LaunchGameWithArgsAsync(extraArgs);

    public string CustomJavaPath
    {
        get => Launch.CustomJavaPath;
        set => Launch.CustomJavaPath = value;
    }

    public string CurrentJavaDisplay => Launch.CurrentJavaDisplay;
    public string ResolvedJavaPath => Launch.ResolvedJavaPath;
    public bool IsCustomJavaMode => Launch.IsCustomJavaMode;

    public string JvmArgs
    {
        get => Launch.JvmArgs;
        set => Launch.JvmArgs = value;
    }

    public int MinRamMb
    {
        get => Launch.MinRamMb;
        set => Launch.MinRamMb = value;
    }

    public int RamMb
    {
        get => Launch.RamMb;
        set => Launch.RamMb = value;
    }

    public int MaxRamMb
    {
        get => Launch.MaxRamMb;
        set => Launch.MaxRamMb = value;
    }

    public string SelectedLaunchBehavior
    {
        get => Launch.SelectedLaunchBehavior;
        set => Launch.SelectedLaunchBehavior = value;
    }

    public ObservableCollection<string> LaunchBehaviors => Launch.LaunchBehaviors;

    public int GameWidth
    {
        get => Launch.GameWidth;
        set => Launch.GameWidth = value;
    }

    public int GameHeight
    {
        get => Launch.GameHeight;
        set => Launch.GameHeight = value;
    }

    public bool IsFullscreen
    {
        get => Launch.IsFullscreen;
        set => Launch.IsFullscreen = value;
    }

    public bool IsElyByErrorOpen
    {
        get => Launch.IsElyByErrorOpen;
        set => Launch.IsElyByErrorOpen = value;
    }

    public string ElyByErrorMessage
    {
        get => Launch.ElyByErrorMessage;
        set => Launch.ElyByErrorMessage = value;
    }

    public string JavaDownloadStatus => Launch.JavaDownloadStatus;

    public int IsJavaDownloading
    {
        get => Launch.IsJavaDownloading;
        set => Launch.IsJavaDownloading = value;
    }

    public bool CanDownloadJava => Launch.CanDownloadJava;
    public bool IsJavaInstallerOpen
    {
        get => Launch.IsJavaInstallerOpen;
        set => Launch.IsJavaInstallerOpen = value;
    }

    public bool IsEffectiveJavaPathInvalid
    {
        get => Launch.IsEffectiveJavaPathInvalid;
        set => Launch.IsEffectiveJavaPathInvalid = value;
    }

    public string EffectiveJavaPath
    {
        get => Launch.EffectiveJavaPath;
        set => Launch.EffectiveJavaPath = value;
    }

    public bool IsDownloading => Launch.IsDownloading;

    public IRelayCommand StepMinRamDownCommand => Launch.StepMinRamDownCommand;
    public IRelayCommand StepMinRamUpCommand => Launch.StepMinRamUpCommand;
    public IRelayCommand StepRamDownCommand => Launch.StepRamDownCommand;
    public IRelayCommand StepRamUpCommand => Launch.StepRamUpCommand;
    public IAsyncRelayCommand<object?> DownloadJavaCommand => Launch.DownloadJavaCommand;
    public IRelayCommand OpenJavaInstallerCommand => Launch.OpenJavaInstallerCommand;
    public IRelayCommand CloseJavaInstallerCommand => Launch.CloseJavaInstallerCommand;
    public IAsyncRelayCommand PickCustomJavaCommand => Launch.PickCustomJavaCommand;
    public IAsyncRelayCommand BrowseJavaPathCommand => Launch.BrowseJavaPathCommand;
    public IAsyncRelayCommand SwitchToOfflineAccountCommand => Launch.SwitchToOfflineAccountCommand;
    public IAsyncRelayCommand<InstanceModel> LaunchSpecificInstanceCommand => Launch.LaunchSpecificInstanceCommand;
    public IRelayCommand<InstanceModel> KillInstanceCommand => Launch.KillInstanceCommand;

    public void UpdateJavaDisplay() => Launch.UpdateJavaDisplay();
    public Task<bool> DownloadJavaCoreAsync(object? parameter, bool force = false, IProgress<double>? progress = null) =>
        Launch.DownloadJavaCoreAsync(parameter, force, progress);
    public Task<string?> ResolveJavaForEditorAsync(string? instanceVersion) => Launch.ResolveJavaForEditorAsync(instanceVersion);
    public Task<bool> CheckCommandLineLaunchAsync(string[]? args) => Launch.CheckCommandLineLaunchAsync(args);
    public bool HasProfile(string idOrName) => Launch.HasProfile(idOrName);
    public Task LaunchSpecificInstanceWithArgsAsync(InstanceModel? instance, string? extraArgs) =>
        Launch.LaunchSpecificInstanceWithArgsAsync(instance, extraArgs);
    public void OpenJavaInstaller() => Launch.OpenJavaInstaller();
    public void CloseJavaInstaller() => Launch.CloseJavaInstaller();

    // Theming Forwarders
    [ObservableProperty] private double _launcherScale = 1.0;
    partial void OnLauncherScaleChanged(double value) => SaveLauncherConfig();

    public ObservableCollection<ThemePreset> AvailableThemes => ThemeSettingsVm.AvailableThemes;

    public string CurrentAccentHex
    {
        get => ThemeSettingsVm.CurrentAccentHex;
        set => ThemeSettingsVm.CurrentAccentHex = value;
    }

    public string CustomThemeHex
    {
        get => ThemeSettingsVm.CustomThemeHex;
        set => ThemeSettingsVm.CustomThemeHex = value;
    }

    public string SimpleAccentHex
    {
        get => ThemeSettingsVm.SimpleAccentHex;
        set => ThemeSettingsVm.SimpleAccentHex = value;
    }

    public string BgHex
    {
        get => ThemeSettingsVm.BgHex;
        set => ThemeSettingsVm.BgHex = value;
    }

    public string SurfaceHex
    {
        get => ThemeSettingsVm.SurfaceHex;
        set => ThemeSettingsVm.SurfaceHex = value;
    }

    public string PanelHex
    {
        get => ThemeSettingsVm.PanelHex;
        set => ThemeSettingsVm.PanelHex = value;
    }

    public string FieldHex
    {
        get => ThemeSettingsVm.FieldHex;
        set => ThemeSettingsVm.FieldHex = value;
    }

    public string HoverHex
    {
        get => ThemeSettingsVm.HoverHex;
        set => ThemeSettingsVm.HoverHex = value;
    }

    public string SidebarBgHex
    {
        get => ThemeSettingsVm.SidebarBgHex;
        set => ThemeSettingsVm.SidebarBgHex = value;
    }

    public string CardHex
    {
        get => ThemeSettingsVm.CardHex;
        set => ThemeSettingsVm.CardHex = value;
    }

    public string TextPrimaryHex
    {
        get => ThemeSettingsVm.TextPrimaryHex;
        set => ThemeSettingsVm.TextPrimaryHex = value;
    }

    public string TextSecondaryHex
    {
        get => ThemeSettingsVm.TextSecondaryHex;
        set => ThemeSettingsVm.TextSecondaryHex = value;
    }

    public string BorderDarkHex
    {
        get => ThemeSettingsVm.BorderDarkHex;
        set => ThemeSettingsVm.BorderDarkHex = value;
    }

    public IBrush PreviewAccentBrush => ThemeSettingsVm.PreviewAccentBrush;
    public IBrush PreviewAccentHoverBrush => ThemeSettingsVm.PreviewAccentHoverBrush;
    public IBrush PreviewAccentDimBrush => ThemeSettingsVm.PreviewAccentDimBrush;
    public IBrush PreviewAccentBorderBrush => ThemeSettingsVm.PreviewAccentBorderBrush;

    public bool IsRestartConfirmOpen
    {
        get => ThemeSettingsVm.IsRestartConfirmOpen;
        set => ThemeSettingsVm.IsRestartConfirmOpen = value;
    }

    public string PendingRestartThemeHex
    {
        get => ThemeSettingsVm.PendingRestartThemeHex;
        set => ThemeSettingsVm.PendingRestartThemeHex = value;
    }

    public bool IsThemeDiscardConfirmOpen
    {
        get => ThemeSettingsVm.IsThemeDiscardConfirmOpen;
        set => ThemeSettingsVm.IsThemeDiscardConfirmOpen = value;
    }

    public bool IsApplyButtonHighlighted
    {
        get => ThemeSettingsVm.IsApplyButtonHighlighted;
        set => ThemeSettingsVm.IsApplyButtonHighlighted = value;
    }

    public string ThemeSubTab
    {
        get => ThemeSettingsVm.ThemeSubTab;
        set => ThemeSettingsVm.ThemeSubTab = value;
    }

    public bool IsThemeSubTabSimple => ThemeSettingsVm.IsThemeSubTabSimple;
    public bool IsThemeSubTabAdvanced => ThemeSettingsVm.IsThemeSubTabAdvanced;

    public string SelectedParticle
    {
        get => ThemeSettingsVm.SelectedParticle;
        set => ThemeSettingsVm.SelectedParticle = value;
    }

    public string[] ParticleOptions => ThemeSettingsVm.ParticleOptions;

    public int ParticleCount
    {
        get => ThemeSettingsVm.ParticleCount;
        set => ThemeSettingsVm.ParticleCount = value;
    }

    public double ParticleScale
    {
        get => ThemeSettingsVm.ParticleScale;
        set => ThemeSettingsVm.ParticleScale = value;
    }

    public double ParticleSpeed
    {
        get => ThemeSettingsVm.ParticleSpeed;
        set => ThemeSettingsVm.ParticleSpeed = value;
    }

    public string ParticleColorHex
    {
        get => ThemeSettingsVm.ParticleColorHex;
        set => ThemeSettingsVm.ParticleColorHex = value;
    }

    public double ParticleAngleDeg
    {
        get => ThemeSettingsVm.ParticleAngleDeg;
        set => ThemeSettingsVm.ParticleAngleDeg = value;
    }

    public string ParticleTexturePath
    {
        get => ThemeSettingsVm.ParticleTexturePath;
        set => ThemeSettingsVm.ParticleTexturePath = value;
    }

    public IRelayCommand ChooseThemeCommand => ThemeSettingsVm.ChooseThemeCommand;
    public IRelayCommand SelectThemeCommand => ThemeSettingsVm.SelectThemeCommand;
    public IRelayCommand RequestApplyThemeCommand => ThemeSettingsVm.RequestApplyThemeCommand;
    public IRelayCommand CancelRestartConfirmCommand => ThemeSettingsVm.CancelRestartConfirmCommand;
    public IRelayCommand ConfirmRestartAndApplyThemeCommand => ThemeSettingsVm.ConfirmRestartAndApplyThemeCommand;
    public IRelayCommand ConfirmDiscardThemeChangesCommand => ThemeSettingsVm.ConfirmDiscardThemeChangesCommand;
    public IRelayCommand CancelDiscardThemeChangesCommand => ThemeSettingsVm.CancelDiscardThemeChangesCommand;
    public IRelayCommand ApplyCustomThemeCommand => ThemeSettingsVm.ApplyCustomThemeCommand;
    public IRelayCommand SwitchThemeSubTabCommand => ThemeSettingsVm.SwitchThemeSubTabCommand;
    public IRelayCommand CloseThemeEditorCommand => ThemeSettingsVm.CloseThemeEditorCommand;

    public void SnapshotCurrentSavedTheme() => ThemeSettingsVm.SnapshotCurrentSavedTheme();
    public bool HasUnsavedThemeChanges() => ThemeSettingsVm.HasUnsavedThemeChanges();
    public void ApplySimpleTheme(string hex) => ThemeSettingsVm.ApplySimpleTheme(hex);
    public void ApplyAdvancedSavedTheme() => ThemeSettingsVm.ApplyAdvancedSavedTheme();
    public void ApplySimpleSavedTheme() => ThemeSettingsVm.ApplySimpleSavedTheme();
    public void RevertThemeToSaved() => ThemeSettingsVm.RevertThemeToSaved();
    public void ChooseTheme(string hex) => ThemeSettingsVm.ChooseTheme(hex);
    public void SelectTheme(string hex) => ThemeSettingsVm.SelectTheme(hex);
    public void RequestApplyTheme(string? hex = null) => ThemeSettingsVm.RequestApplyTheme(hex);
    public void SwitchThemeSubTab(string tab) => ThemeSettingsVm.SwitchThemeSubTab(tab);

    public NavigationViewModel Navigation { get; }

    public bool IsLockAspectRatio
    {
        get => Launch.IsLockAspectRatio;
        set => Launch.IsLockAspectRatio = value;
    }

    public LauncherNavPage CurrentPage => Navigation.CurrentPage;
    public bool IsProfilesView => Navigation.IsProfilesView;
    public bool IsModpacksView => Navigation.IsModpacksView;
    public bool IsEditInstanceView => Navigation.IsEditInstanceView;
    public bool IsContentBrowserView => Navigation.IsContentBrowserView;
    public bool IsSettingsView => Navigation.IsSettingsView;
    public bool IsServerManagerView => Navigation.IsServerManagerView;
    public bool IsAccountsView => Navigation.IsAccountsView;
    public bool IsCreateProfileView => Navigation.IsCreateProfileView;
    public bool IsCreateServerView => Navigation.IsCreateServerView;
    public bool IsLaunchBarVisible => Navigation.IsLaunchBarVisible;

    public bool IsSidebarExpanded
    {
        get => Navigation?.IsSidebarExpanded ?? false;
        set
        {
            if (Navigation != null)
                Navigation.IsSidebarExpanded = value;
        }
    }

    public double SidebarWidth => Navigation?.SidebarWidth ?? 220;
    public bool CanGoBack => Navigation.CanGoBack;
    public bool CanGoForward => Navigation.CanGoForward;
    public ObservableCollection<BreadcrumbItem> Breadcrumbs => Navigation.Breadcrumbs;

    public EditInstanceViewModel? CurrentEditInstanceVm
    {
        get => Navigation.CurrentEditInstanceVm;
        set => Navigation.CurrentEditInstanceVm = value;
    }

    public ContentBrowserViewModel? CurrentContentBrowserVm
    {
        get => Navigation.CurrentContentBrowserVm;
        set => Navigation.CurrentContentBrowserVm = value;
    }

    public ServerManagerViewModel? CurrentServerManagerVm
    {
        get => Navigation.CurrentServerManagerVm;
        set => Navigation.CurrentServerManagerVm = value;
    }

    public CreateProfileViewModel? CreateProfileVm
    {
        get => Navigation.CreateProfileVm;
        set => Navigation.CreateProfileVm = value;
    }

    public CreateServerViewModel? CreateServerVm
    {
        get => Navigation.CreateServerVm;
        set => Navigation.CreateServerVm = value;
    }

    public string CurrentSettingsTab
    {
        get => Navigation.CurrentSettingsTab;
        set => Navigation.CurrentSettingsTab = value;
    }

    public bool IsSettingsTabGame => Navigation.IsSettingsTabGame;
    public bool IsSettingsTabGeneral => Navigation.IsSettingsTabGeneral;
    public bool IsSettingsTabAppearance => Navigation.IsSettingsTabAppearance;
    public bool IsSettingsTabTheme => Navigation.IsSettingsTabTheme;
    public bool IsSettingsTabLauncher => Navigation.IsSettingsTabLauncher;

    public bool IsServerUnderDevDialogOpen
    {
        get => Navigation.IsServerUnderDevDialogOpen;
        set => Navigation.IsServerUnderDevDialogOpen = value;
    }

    public bool HasExistingServers => Navigation.HasExistingServers;

    public IRelayCommand GoBackCommand => Navigation.GoBackCommand;
    public IRelayCommand GoForwardCommand => Navigation.GoForwardCommand;
    public IRelayCommand ToggleSidebarCommand => Navigation.ToggleSidebarCommand;
    public IRelayCommand ShowProfilesCommand => Navigation.ShowProfilesCommand;
    public IRelayCommand ShowModpacksCommand => Navigation.ShowModpacksCommand;
    public IRelayCommand SwitchSettingsTabCommand => Navigation.SwitchSettingsTabCommand;
    public void SwitchSettingsTab(string tab) => Navigation.SwitchSettingsTab(tab);
    public IRelayCommand NavigateToCreateServerCommand => Navigation.NavigateToCreateServerCommand;
    public IRelayCommand OpenServersDirectoryCommand => Navigation.OpenServersDirectoryCommand;
    public IRelayCommand NavigateToBreadcrumbCommand => Navigation.NavigateToBreadcrumbCommand;

    public void NavigateToProfiles() => Navigation.NavigateToProfiles();
    public void NavigateToModpacks() => Navigation.NavigateToModpacks();
    public void NavigateToSettings() => Navigation.NavigateToSettings();
    public void NavigateToAccounts() => Navigation.NavigateToAccounts();
    public void NavigateToCreateProfile() => Navigation.NavigateToCreateProfile();
    public void NavigateToCreateServer() => Navigation.NavigateToCreateServer();
    public void NavigateToEditInstance(InstanceModel i, string? tab = "Overview") => Navigation.NavigateToEditInstance(i, tab);
    public void NavigateToContentBrowser(ContentBrowserViewModel vm, InstanceModel? instance, string parentTitle, string sectionTitle, EditInstanceViewModel? editVm) =>
        Navigation.NavigateToContentBrowser(vm, instance, parentTitle, sectionTitle, editVm);
    public void NavigateToServerManager(ServerManagerViewModel vm, InstanceModel? instance, EditInstanceViewModel? editVm) =>
        Navigation.NavigateToServerManager(vm, instance, editVm);
    public void UpdateActiveInstanceTab(string n, string t) => Navigation.UpdateActiveInstanceTab(n, t);
    public void GoBack() => Navigation.GoBack();
    public void GoForward() => Navigation.GoForward();
    public void ToggleSidebar() => Navigation.ToggleSidebar();

    public InstancesViewModel InstancesManager { get; }

    public ObservableCollection<InstanceModel> Instances => InstancesManager.Instances;

    public InstanceModel? SelectedInstance
    {
        get => InstancesManager?.SelectedInstance;
        set
        {
            if (InstancesManager != null)
                InstancesManager.SelectedInstance = value;
        }
    }

    public string SelectedInstanceId
    {
        get => InstancesManager.SelectedInstanceId;
        set => InstancesManager.SelectedInstanceId = value;
    }

    public string SelectedInstanceName
    {
        get => InstancesManager.SelectedInstanceName;
        set => InstancesManager.SelectedInstanceName = value;
    }
    public string SelectedInstanceDetails
    {
        get => InstancesManager.SelectedInstanceDetails;
        set => InstancesManager.SelectedInstanceDetails = value;
    }
    public bool HasInstances
    {
        get => InstancesManager.HasInstances;
        set => InstancesManager.HasInstances = value;
    }

    public bool ShowReleases
    {
        get => InstancesManager.ShowReleases;
        set => InstancesManager.ShowReleases = value;
    }

    public bool ShowSnapshots
    {
        get => InstancesManager.ShowSnapshots;
        set => InstancesManager.ShowSnapshots = value;
    }

    public bool ShowBetas
    {
        get => InstancesManager.ShowBetas;
        set => InstancesManager.ShowBetas = value;
    }

    public bool ShowAlphas
    {
        get => InstancesManager.ShowAlphas;
        set => InstancesManager.ShowAlphas = value;
    }

    public bool UseFlatVersionList
    {
        get => InstancesManager.UseFlatVersionList;
        set => InstancesManager.UseFlatVersionList = value;
    }

    public ObservableCollection<string> AvailableVersions => InstancesManager.AvailableVersions;

    public string SelectedVersion
    {
        get => InstancesManager.SelectedVersion;
        set => InstancesManager.SelectedVersion = value;
    }

    public bool CanCreateInstance => InstancesManager.CanCreateInstance;

    public ObservableCollection<string> AvailableLoaders => InstancesManager.AvailableLoaders;

    public string SelectedLoader
    {
        get => InstancesManager.SelectedLoader;
        set => InstancesManager.SelectedLoader = value;
    }

    public ObservableCollection<string> AvailableLoaderBuilds => InstancesManager.AvailableLoaderBuilds;

    public string SelectedLoaderBuild
    {
        get => InstancesManager.SelectedLoaderBuild;
        set => InstancesManager.SelectedLoaderBuild = value;
    }

    public bool ShowLoaderBuilds
    {
        get => InstancesManager.ShowLoaderBuilds;
        set => InstancesManager.ShowLoaderBuilds = value;
    }

    public string LoaderError => InstancesManager.LoaderError;
    public bool LoaderErrorVisible => InstancesManager.LoaderErrorVisible;
    public bool IsLoaderTypesVisible => InstancesManager.IsLoaderTypesVisible;
    public bool IsLoaderBuildsVisible => InstancesManager.IsLoaderBuildsVisible;
    public bool CanSwapLoader => InstancesManager.CanSwapLoader;

    public ObservableCollection<string> DirectoryModes
    {
        get => InstancesManager.DirectoryModes;
        set => InstancesManager.DirectoryModes = value;
    }

    public string SelectedDirectoryMode
    {
        get => InstancesManager.SelectedDirectoryMode;
        set => InstancesManager.SelectedDirectoryMode = value;
    }

    public ObservableCollection<string> JavaModes
    {
        get => InstancesManager.JavaModes;
        set => InstancesManager.JavaModes = value;
    }

    [ObservableProperty]
    private string _selectedJavaMode = "Recommended";

    public bool IsRightSidebarOpen
    {
        get => InstancesManager.IsRightSidebarOpen;
        set => InstancesManager.IsRightSidebarOpen = value;
    }

    public bool IsVersionPickerOpen
    {
        get => InstancesManager.IsVersionPickerOpen;
        set => InstancesManager.IsVersionPickerOpen = value;
    }

    public bool IsCreateDialogOpen
    {
        get => InstancesManager.IsCreateDialogOpen;
        set => InstancesManager.IsCreateDialogOpen = value;
    }

    public bool IsImportPickerOpen
    {
        get => InstancesManager.IsImportPickerOpen;
        set => InstancesManager.IsImportPickerOpen = value;
    }

    public string NewInstanceName
    {
        get => InstancesManager.NewInstanceName;
        set => InstancesManager.NewInstanceName = value;
    }

    public bool IsNewInstanceNameDuplicate => InstancesManager.IsNewInstanceNameDuplicate;

    public IRelayCommand<InstanceModel> SelectInstanceCommand => InstancesManager.SelectInstanceCommand;
    public IRelayCommand CloseRightSidebarCommand => InstancesManager.CloseRightSidebarCommand;
    public IAsyncRelayCommand<InstanceModel> CreateDesktopShortcutCommand => InstancesManager.CreateDesktopShortcutCommand;
    public IRelayCommand<InstanceModel> EditInstanceCommand => InstancesManager.EditInstanceCommand;
    public IAsyncRelayCommand<InstanceModel> OpenCopyDialogCommand => InstancesManager.OpenCopyDialogCommand;
    public IRelayCommand<InstanceModel> StartRenameInstanceCommand => InstancesManager.StartRenameInstanceCommand;
    public IAsyncRelayCommand<InstanceModel> FinishRenameInstanceCommand => InstancesManager.FinishRenameInstanceCommand;
    public IAsyncRelayCommand<InstanceModel> ChangeInstanceIconCommand => InstancesManager.ChangeInstanceIconCommand;
    public IRelayCommand<InstanceModel> OpenProfileFolderCommand => InstancesManager.OpenProfileFolderCommand;
    public IRelayCommand<InstanceModel> OpenProfileBrowseModsCommand => InstancesManager.OpenProfileBrowseModsCommand;
    public IRelayCommand<InstanceModel> OpenProfileServerManagerCommand => InstancesManager.OpenProfileServerManagerCommand;
    public IRelayCommand<InstanceModel> OpenProfileSectionModsCommand => InstancesManager.OpenProfileSectionModsCommand;
    public IRelayCommand<InstanceModel> OpenProfileSectionResourcePacksCommand => InstancesManager.OpenProfileSectionResourcePacksCommand;
    public IRelayCommand<InstanceModel> OpenProfileSectionShaderPacksCommand => InstancesManager.OpenProfileSectionShaderPacksCommand;
    public IRelayCommand<InstanceModel> OpenProfileSectionWorldsCommand => InstancesManager.OpenProfileSectionWorldsCommand;
    public IRelayCommand<InstanceModel> OpenProfileSectionScreenshotsCommand => InstancesManager.OpenProfileSectionScreenshotsCommand;
    public IRelayCommand<InstanceModel> OpenProfileSectionLogsCommand => InstancesManager.OpenProfileSectionLogsCommand;
    public IRelayCommand<InstanceModel> OpenProfileModsFolderCommand => InstancesManager.OpenProfileModsFolderCommand;
    public IRelayCommand<InstanceModel> OpenProfileResourcePacksFolderCommand => InstancesManager.OpenProfileResourcePacksFolderCommand;
    public IRelayCommand<InstanceModel> OpenProfileShaderPacksFolderCommand => InstancesManager.OpenProfileShaderPacksFolderCommand;
    public IRelayCommand<InstanceModel> OpenProfileWorldsFolderCommand => InstancesManager.OpenProfileWorldsFolderCommand;
    public IRelayCommand<InstanceModel> OpenProfileScreenshotsFolderCommand => InstancesManager.OpenProfileScreenshotsFolderCommand;
    public IRelayCommand<InstanceModel> OpenProfileLogsFolderCommand => InstancesManager.OpenProfileLogsFolderCommand;
    public IAsyncRelayCommand<InstanceModel> ExportCurseForgeCommand => InstancesManager.ExportCurseForgeCommand;
    public IAsyncRelayCommand<InstanceModel> ExportModrinthCommand => InstancesManager.ExportModrinthCommand;
    public IAsyncRelayCommand<InstanceModel> ExportPrismCommand => InstancesManager.ExportPrismCommand;
    public IRelayCommand<InstanceModel> RequestDeleteProfileCommand => InstancesManager.RequestDeleteProfileCommand;
    public IAsyncRelayCommand ConfirmDeleteProfileCommand => InstancesManager.ConfirmDeleteProfileCommand;
    public IRelayCommand CancelDeleteProfileCommand => InstancesManager.CancelDeleteProfileCommand;
    public IRelayCommand OpenRootDirectoryCommand => InstancesManager.OpenRootDirectoryCommand;
    public IRelayCommand ToggleVersionPickerCommand => InstancesManager.ToggleVersionPickerCommand;
    public IRelayCommand<string> SelectVersionCommand => InstancesManager.SelectVersionCommand;
    public IRelayCommand ClearVersionCommand => InstancesManager.ClearVersionCommand;
    public IAsyncRelayCommand ImportInstancesCommand => InstancesManager.ImportInstancesCommand;
    public IRelayCommand CloseImportPickerCommand => InstancesManager.CloseImportPickerCommand;
    public IAsyncRelayCommand PickImportSingleCommand => InstancesManager.PickImportSingleCommand;
    public IAsyncRelayCommand PickImportFileCommand => InstancesManager.PickImportFileCommand;
    public IAsyncRelayCommand<string?> PickImportFolderCommand => InstancesManager.PickImportFolderCommand;
    public IRelayCommand OpenNewInstanceDialogCommand => InstancesManager.OpenNewInstanceDialogCommand;
    public IAsyncRelayCommand ConfirmCreateInstanceCommand => InstancesManager.ConfirmCreateInstanceCommand;
    public IRelayCommand ToggleLoaderBuildsCommand => InstancesManager.ToggleLoaderBuildsCommand;

    public void SelectInstance(InstanceModel i) => InstancesManager.SelectInstance(i);
    public void ReorderInstance(string movedId, string targetId, bool dropAfter, bool save = true) =>
        InstancesManager.ReorderInstance(movedId, targetId, dropAfter, save);
    public void PersistInstanceOrder() => InstancesManager.PersistInstanceOrder();
    public void FilterVersions() => InstancesManager.FilterVersions();
    public Task LoadMojangVersionsAsync() => InstancesManager.LoadMojangVersionsAsync();
    public Task UpdateAvailableLoadersAsync(string version) => InstancesManager.UpdateAvailableLoadersAsync(version);
    public void OnInstanceNameLostFocus() => InstancesManager.OnInstanceNameLostFocus();
    public void CloseEditInstanceWindow(EditInstanceViewModel vm) => InstancesManager.CloseEditInstanceWindow(vm);

    private bool _isLoadingConfig = false;

    private readonly IAccountService _accountService;

    private readonly IInstanceService _instanceService;

    private readonly ILaunchService _launchService;

    private readonly ModLoaderService _modLoaderService;



    [ObservableProperty]

    private string _statusText = string.Empty;



    public string VersionLabel { get; } =

        System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";



    [ObservableProperty]

    private string _launcherName = "Zenith Launcher";



    partial void OnLauncherNameChanged(string value)

    {

        OnPropertyChanged(nameof(WindowTitle));

        SaveLauncherConfig();

    }



    public string WindowTitle => $"{LauncherName} v{VersionLabel}";



    [RelayCommand]

    public void ResetLauncherName() => LauncherName = "Zenith Launcher";

    public ObservableCollection<AccountModel> Accounts => AccountsVm.Accounts;



    public AccountModel? SelectedAccount

    {

        get => AccountsVm.SelectedAccount;

        set => AccountsVm.SelectedAccount = value;

    }



    public string ActiveAccountName => AccountsVm.ActiveAccountName;



    public string ActiveAccountType => AccountsVm.ActiveAccountType;



    [ObservableProperty] private string _settingsSearchQuery = string.Empty;



    partial void OnSettingsSearchQueryChanged(string value)

    {

        UpdateSearchVisibility();

    }



    [ObservableProperty] private bool _isSearchMemoryVisible = true;

    [ObservableProperty] private bool _isSearchResolutionVisible = true;

    [ObservableProperty] private bool _isSearchFullscreenVisible = true;

    [ObservableProperty] private bool _isSearchJavaVisible = true;

    [ObservableProperty] private bool _isSearchDirectoryVisible = true;

    [ObservableProperty] private bool _isSearchVersionFiltersVisible = true;

    [ObservableProperty] private bool _isSearchJvmArgsVisible = true;

    [ObservableProperty] private bool _isSearchLanguageVisible = true;

    [ObservableProperty] private bool _isSearchDiscordVisible = true;
    [ObservableProperty] private bool _discordRpcEnabled = true;

    partial void OnDiscordRpcEnabledChanged(bool value)
    {
        if (!value)
        {
            Services.DiscordPresenceService.Shutdown();
            LauncherLog.Info("Discord RPC: disabled via settings toggle.");
        }
        else
        {
            Services.DiscordPresenceService.Initialize(true);
            Services.DiscordPresenceService.SetBrowsingProfiles();
            LauncherLog.Info("Discord RPC: enabled via settings toggle.");
        }
    }

    [ObservableProperty] private bool _isSearchAppearanceVisible = true;



    private void UpdateSearchVisibility()

    {

        var q = SettingsSearchQuery?.Trim().ToLowerInvariant() ?? "";

        if (string.IsNullOrEmpty(q))

        {

            IsSearchMemoryVisible = IsSearchResolutionVisible = IsSearchFullscreenVisible = IsSearchJavaVisible = true;

            IsSearchDirectoryVisible = IsSearchVersionFiltersVisible = IsSearchJvmArgsVisible = true;

            IsSearchLanguageVisible = IsSearchDiscordVisible = IsSearchAppearanceVisible = true;

            return;

        }

        IsSearchMemoryVisible = "memory ram allocated память озу".Contains(q);

        IsSearchResolutionVisible = "resolution window size разрешение экран".Contains(q);

        IsSearchFullscreenVisible = "fullscreen mode полноэкранный".Contains(q);

        IsSearchJavaVisible = "java runtime manage automatic download джава".Contains(q);

        IsSearchDirectoryVisible = "directory strategy isolation директория папка".Contains(q);

        IsSearchVersionFiltersVisible = "version filters releases snapshots версии фильтры".Contains(q);

        IsSearchJvmArgsVisible = "jvm arguments flags аргументы jvm".Contains(q);

        IsSearchLanguageVisible = "language localization english russian ukrainian turkish german french язык".Contains(q);

        IsSearchDiscordVisible = "discord rpc rich presence application id дискорд".Contains(q);

        IsSearchAppearanceVisible = "appearance theme color accent name branding внешний вид тема цвет акцент оформление launcher лаунчер настройки".Contains(q);

    }



    [ObservableProperty] private bool _autoManageJava = true;



    partial void OnAutoManageJavaChanged(bool value)

    {

        LauncherLog.Info($"Automatic Java management {(value ? "enabled" : "disabled")}.");

    }



    [ObservableProperty] private string _selectedLanguage = "English";



    public string[] LanguageOptions => Services.L10n.LanguageDisplayNames;



    partial void OnSelectedLanguageChanged(string value)

    {

        Services.L10n.ApplyDisplay(value);

        // The Discord status is rendered from the current language - push the

        // same logical activity again so it switches language instantly.

        Services.DiscordPresenceService.RefreshForLanguageChange();

        // Rebuild converter-driven collections so ComboBox items re-evaluate

        // their display converters with the new language.

        JavaModes = new ObservableCollection<string>(new[] { "Recommended", "System", "Custom" });

        DirectoryModes = new ObservableCollection<string>(new[] { "Separate directory for each instance", "Family", "Do not use separate directories" });

        ModpacksBrowser?.RefreshTranslations();

    }



    // --- Manual Java install (Settings "+" button) ---



    public static string AppVersion =>

        typeof(MainWindowViewModel).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";



    public ModpacksBrowserViewModel ModpacksBrowser { get; private set; } = null!;



    [ObservableProperty]

    private bool _isDeleteConfirmOpen;



    [ObservableProperty]

    private InstanceModel? _pendingDeleteInstance;

    [ObservableProperty] private bool _isCloneDialogOpen;

    [ObservableProperty] private CloneProfileViewModel? _cloneDialogVm;



    public ObservableCollection<AccountRow> AccountRows => AccountsVm.AccountRows;



    public AccountRow? ActiveAccountRow => AccountsVm.ActiveAccountRow;



    public ThemeSettingsViewModel ThemeSettingsVm { get; }

    public LauncherSettingsViewModel LauncherSettingsVm { get; }

    public AccountsViewModel AccountsVm { get; }



    public Avalonia.Media.Imaging.Bitmap? PrismImportIcon { get; } = ImportIconService.GetPrismIcon();

    public Avalonia.Media.Imaging.Bitmap? MultiMCImportIcon { get; } = ImportIconService.GetMultiMCIcon();



    public MainWindowViewModel()
    {
        _accountService = new AccountService();
        AccountsVm = new AccountsViewModel(_accountService);
        AccountsVm.AccountStateChanged += () =>
        {
            OnPropertyChanged(nameof(ActiveAccountName));
            OnPropertyChanged(nameof(ActiveAccountType));
            OnPropertyChanged(nameof(ActiveAccountRow));
            OnPropertyChanged(nameof(SelectedAccount));
            OnPropertyChanged(nameof(Accounts));
            OnPropertyChanged(nameof(AccountRows));
        };
        AccountsVm.OnStatusMessage += msg => StatusText = msg;

        _instanceService = new InstanceService();
        _launchService = new LaunchService((IInstanceService)_instanceService);

        Launch = new LaunchViewModel(_launchService, _accountService, _instanceService, Console)
        {
            GetSelectedInstance = () => SelectedInstance ?? _instanceService.GetSelectedInstance(),
            GetSelectedAccount = () => SelectedAccount,
            GetActiveAccountName = () => ActiveAccountName,
            GetSelectedJavaMode = () => SelectedJavaMode,
            SetSelectedJavaMode = mode => SelectedJavaMode = mode,
            SetElyByErrorOpen = open => IsElyByErrorOpen = open,
            OnStatusChanged = text => StatusText = text,
            SaveLauncherConfigRequested = () => SaveLauncherConfig(),
            RefreshStateRequested = () => RefreshState(),
            OnJavaLocationDetected = (major, path) => LauncherSettingsVm?.SetJavaLocation(major, path),
            OnLauncherSettingsUpdateJavaDisplayRequested = () => LauncherSettingsVm?.UpdateJavaDisplay(),
            SelectInstanceRequested = inst => SelectInstance(inst),
            GetInstances = () => Instances
        };

        Launch.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != null)
                OnPropertyChanged(e.PropertyName);
        };

        _modLoaderService = new ModLoaderService();

        InstancesManager = new InstancesViewModel(
            _instanceService,
            _modLoaderService,
            () => Launch,
            () => Navigation!,
            () => this)
        {
            OnStatusChanged = msg => StatusText = msg,
            SaveLauncherConfigRequested = () => SaveLauncherConfig(),
            RefreshStateRequested = () => RefreshState(),
            UpdateJavaDisplayRequested = () => UpdateJavaDisplay(),
            GetCurseForgeApiKey = () => LauncherSettingsVm?.CurseForgeApiKey ?? "",
            CloseDialogsRequested = () => CloseDialogs(),
            GetIsDeleteConfirmOpen = () => IsDeleteConfirmOpen,
            SetIsDeleteConfirmOpen = v => IsDeleteConfirmOpen = v,
            GetPendingDeleteInstance = () => PendingDeleteInstance,
            SetPendingDeleteInstance = v => PendingDeleteInstance = v
        };

        InstancesManager.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(InstancesManager.SelectedInstance))
            {
                OnPropertyChanged(nameof(SelectedInstance));
                OnPropertyChanged(nameof(SelectedInstanceName));
                OnPropertyChanged(nameof(SelectedInstanceDetails));
                OnPropertyChanged(nameof(SelectedInstanceId));
                OnPropertyChanged(nameof(HasInstances));
            }
            else if (e.PropertyName == nameof(InstancesManager.SelectedInstanceName))
                OnPropertyChanged(nameof(SelectedInstanceName));
            else if (e.PropertyName == nameof(InstancesManager.SelectedInstanceDetails))
                OnPropertyChanged(nameof(SelectedInstanceDetails));
            else if (e.PropertyName == nameof(InstancesManager.HasInstances))
                OnPropertyChanged(nameof(HasInstances));
            else if (e.PropertyName == nameof(InstancesManager.IsRightSidebarOpen))
                OnPropertyChanged(nameof(IsRightSidebarOpen));
            else if (e.PropertyName == nameof(InstancesManager.IsVersionPickerOpen))
                OnPropertyChanged(nameof(IsVersionPickerOpen));
            else if (e.PropertyName == nameof(InstancesManager.IsCreateDialogOpen))
                OnPropertyChanged(nameof(IsCreateDialogOpen));
            else if (e.PropertyName == nameof(InstancesManager.IsImportPickerOpen))
                OnPropertyChanged(nameof(IsImportPickerOpen));
            else if (e.PropertyName == nameof(InstancesManager.EffectiveJavaPath))
                OnPropertyChanged(nameof(EffectiveJavaPath));
            else if (e.PropertyName == nameof(InstancesManager.SelectedVersion))
                OnPropertyChanged(nameof(SelectedVersion));
            else if (e.PropertyName == nameof(InstancesManager.SelectedLoader))
                OnPropertyChanged(nameof(SelectedLoader));
            else if (e.PropertyName == nameof(InstancesManager.SelectedLoaderBuild))
                OnPropertyChanged(nameof(SelectedLoaderBuild));
            else if (e.PropertyName == nameof(InstancesManager.ShowLoaderBuilds))
                OnPropertyChanged(nameof(ShowLoaderBuilds));
            else if (e.PropertyName == nameof(InstancesManager.CanCreateInstance))
                OnPropertyChanged(nameof(CanCreateInstance));
            else if (e.PropertyName == nameof(InstancesManager.CanSwapLoader))
                OnPropertyChanged(nameof(CanSwapLoader));
            else if (e.PropertyName == nameof(InstancesManager.LoaderError))
                OnPropertyChanged(nameof(LoaderError));
            else if (e.PropertyName == nameof(InstancesManager.LoaderErrorVisible))
                OnPropertyChanged(nameof(LoaderErrorVisible));
            else if (e.PropertyName == nameof(InstancesManager.IsLoaderTypesVisible))
                OnPropertyChanged(nameof(IsLoaderTypesVisible));
            else if (e.PropertyName == nameof(InstancesManager.IsLoaderBuildsVisible))
                OnPropertyChanged(nameof(IsLoaderBuildsVisible));
            else if (e.PropertyName == nameof(InstancesManager.ShowReleases))
                OnPropertyChanged(nameof(ShowReleases));
            else if (e.PropertyName == nameof(InstancesManager.ShowSnapshots))
                OnPropertyChanged(nameof(ShowSnapshots));
            else if (e.PropertyName == nameof(InstancesManager.ShowBetas))
                OnPropertyChanged(nameof(ShowBetas));
            else if (e.PropertyName == nameof(InstancesManager.ShowAlphas))
                OnPropertyChanged(nameof(ShowAlphas));
            else if (e.PropertyName == nameof(InstancesManager.UseFlatVersionList))
                OnPropertyChanged(nameof(UseFlatVersionList));
            else if (e.PropertyName == nameof(InstancesManager.SelectedDirectoryMode))
                OnPropertyChanged(nameof(SelectedDirectoryMode));
            else if (e.PropertyName == nameof(InstancesManager.SelectedJavaMode))
                OnPropertyChanged(nameof(SelectedJavaMode));
        };

        ThemeSettingsVm = new ThemeSettingsViewModel
        {
            GetConfigSnapshot = () => GetLauncherConfigSnapshot(),
            SaveConfigRequested = () => SaveLauncherConfig(),
            CloseRequested = () => SwitchSettingsTab("Appearance")
        };

        ThemeSettingsVm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != null)
                OnPropertyChanged(e.PropertyName);
        };

        LauncherSettingsVm = new LauncherSettingsViewModel(ThemeSettingsVm)

        {

            SaveConfigRequested = () => SaveLauncherConfig(),

            VersionFiltersChanged = () => FilterVersions(),

            LanguageChanged = lang => SelectedLanguage = lang,

            DiscordRpcChanged = enabled => DiscordRpcEnabled = enabled,

            ScaleChanged = scale => LauncherScale = scale,

            OpenJavaInstallerRequested = () => OpenJavaInstaller(),
            DownloadJavaRequested = async (major, prog) =>
            {
                return await DownloadJavaCoreAsync(major, force: true, progress: prog);
            },

            GetRecommendedJavaDisplay = () =>

            {

                var inst = SelectedInstance ?? _instanceService?.GetSelectedInstance();

                return inst != null

                    ? Services.JavaVersionHelper.RecommendedDisplay(inst.Version)

                    : L10n.T("java_display_auto");

            },

            GetWindowTitle = () => WindowTitle
        };

        Navigation = new NavigationViewModel(
            () => this,
            _instanceService,
            _modLoaderService)
        {
            HasUnsavedThemeChanges = () => HasUnsavedThemeChanges(),
            RequestThemeDiscardConfirmation = action => ThemeSettingsVm.RequestDiscardConfirmation(action),
            SaveLauncherConfigRequested = () => SaveLauncherConfig(),
            RefreshStateRequested = () => RefreshState(),
            StatusMessageRequested = msg => StatusText = msg,
            GetModpacksBrowser = () => ModpacksBrowser,
            GetCurseForgeApiKey = () => LauncherSettingsVm?.CurseForgeApiKey ?? "",
            GetUseFlatVersionList = () => UseFlatVersionList,
            GetShowReleases = () => ShowReleases,
            GetShowSnapshots = () => ShowSnapshots,
            GetShowBetas = () => ShowBetas,
            GetShowAlphas = () => ShowAlphas,
            GetDefaultRamMb = () => LauncherSettingsVm?.RamMb > 0 ? LauncherSettingsVm.RamMb : (Launch?.RamMb > 0 ? Launch.RamMb : 4096),
            GetInstances = () => Instances,
            SetSelectedInstance = inst => SelectedInstance = inst,
            SetSelectedInstanceId = id => SelectedInstanceId = id
        };

        Navigation.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Navigation.CurrentPage))
            {
                OnPropertyChanged(nameof(CurrentPage));
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
            }
            else if (e.PropertyName == nameof(Navigation.SidebarWidth))
                OnPropertyChanged(nameof(SidebarWidth));
            else if (e.PropertyName == nameof(Navigation.IsSidebarExpanded))
                OnPropertyChanged(nameof(IsSidebarExpanded));
            else if (e.PropertyName == nameof(Navigation.CanGoBack))
                OnPropertyChanged(nameof(CanGoBack));
            else if (e.PropertyName == nameof(Navigation.CanGoForward))
                OnPropertyChanged(nameof(CanGoForward));
            else if (e.PropertyName == nameof(Navigation.CurrentEditInstanceVm))
                OnPropertyChanged(nameof(CurrentEditInstanceVm));
            else if (e.PropertyName == nameof(Navigation.CurrentContentBrowserVm))
                OnPropertyChanged(nameof(CurrentContentBrowserVm));
            else if (e.PropertyName == nameof(Navigation.CurrentServerManagerVm))
                OnPropertyChanged(nameof(CurrentServerManagerVm));
            else if (e.PropertyName == nameof(Navigation.CreateProfileVm))
                OnPropertyChanged(nameof(CreateProfileVm));
            else if (e.PropertyName == nameof(Navigation.CreateServerVm))
                OnPropertyChanged(nameof(CreateServerVm));
            else if (e.PropertyName == nameof(Navigation.CurrentSettingsTab))
            {
                OnPropertyChanged(nameof(CurrentSettingsTab));
                OnPropertyChanged(nameof(IsSettingsTabGame));
                OnPropertyChanged(nameof(IsSettingsTabGeneral));
                OnPropertyChanged(nameof(IsSettingsTabAppearance));
                OnPropertyChanged(nameof(IsSettingsTabTheme));
                OnPropertyChanged(nameof(IsSettingsTabLauncher));
            }
        };

        Navigation.NavigateToProfiles();

        LoadLauncherConfig();

        // Force-apply the language even when the loaded value equals the
        // field default ("English") — CommunityToolkit skips the change
        // notification in that case, so OnSelectedLanguageChanged never
        // fires and the UI stays on DetectSystemCode() (the OS language).
        Services.L10n.ApplyDisplay(SelectedLanguage);

        // Recompute language-dependent display strings AFTER the final language
        // is applied — LoadFromConfig above may have rendered them in the OS language.
        LauncherSettingsVm.UpdateJavaDisplay();



        // Re-translate computed UI strings when the language changes.

        L10n.LanguageChanged += () => OnPropertyChanged(nameof(LaunchButtonText));

        L10n.LanguageChanged += () =>
        {
            Navigation.RebuildActiveBreadcrumbs();
            CreateServerVm?.UpdatePluginsTabTitle();
        };

        // Console log lines are enqueued cheaply from any (reader) thread and flushed

        // to the UI in batches every 250 ms. Posting every single line to the UI thread

        // freezes the launcher for chatty modern versions (huge log floods) and lets the

        // game's stdout/stderr pipe buffer fill up. Batching keeps both the UI responsive

        // and the pipe drained so the game never blocks waiting on the launcher.

        Console.HasActiveProcesses = () => Instances.Any(i => i.IsRunning || i.IsLaunching || Launch.IsInstanceLaunching(i.Id));



        ModLoaderService.Diagnostic += msg =>

        {

            Dispatcher.UIThread.Post(() =>

            {

                ConsoleLogs += $"[ModLoader] {msg}" + Environment.NewLine;

            });

        };



        // Discord Rich Presence lifecycle - initialize in background to avoid UI thread pipe blocking

        Task.Run(() =>
        {
            try
            {
                Services.DiscordPresenceService.Initialize(DiscordRpcEnabled);
                Services.DiscordPresenceService.SetBrowsingProfiles();
            }
            catch (Exception ex)
            {
                LauncherLog.Error("Discord RPC background init failed", ex);
            }
        });

        _launchService.GameLaunched += (instance, accountType) =>

            Services.DiscordPresenceService.SetPlaying(instance.Name, instance.Version, accountType, DateTime.UtcNow);

        _launchService.GameExited += (_, _) =>
        {
            Services.DiscordPresenceService.SetBrowsingProfiles();
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (!IsConsoleDialogOpen)
                {
                    Console.ClearLogs();
                }
                Services.MemoryTrimmer.TrimMemory(force: true);
            });
        };

        



        RefreshState();



        // Silent startup session maintenance: renew expired tokens in the

        // background so returning users never see a re-login prompt.

        _ = Task.Run(async () =>

        {

            try

            {

                await Task.Delay(3000).ConfigureAwait(false);

                var renewed = await _accountService.RefreshAllSessionsAsync().ConfigureAwait(false);

                if (renewed > 0)

                    Dispatcher.UIThread.Post(RefreshState);

            }

            catch (Exception ex)

            {

                LauncherLog.Error("Startup session refresh crashed.", ex);

            }

        });



        _ = LoadMojangVersionsAsync();

        _ = InitializeModLoadersAsync();

        UpdateJavaDisplay();

        ModpacksBrowser = new ModpacksBrowserViewModel(_instanceService, _modLoaderService, () =>

        {

            Avalonia.Threading.Dispatcher.UIThread.Post(() =>

            {

                RefreshState();

                ModpacksBrowser.UpdateInstalledFlags();

            });

        });

        ModpacksBrowser.GetCurseForgeApiKey = () => LauncherSettingsVm?.CurseForgeApiKey ?? "";

        ModpacksBrowser.OnProjectSelectedChanged = _ =>
        {
            if (Navigation.CurrentPage == LauncherNavPage.Modpacks)
                Navigation.RebuildActiveBreadcrumbs();
        };

        ModpacksBrowser.OnAuthorProfileChanged = _ =>
        {
            if (Navigation.CurrentPage == LauncherNavPage.Modpacks)
                Navigation.RebuildActiveBreadcrumbs();
        };



        NavigateToProfiles();

        _ = CheckForUpdatesBackgroundAsync();



        // Final guarantee: re-apply the saved theme after all startup loading,

        // so no intermediate default state can stick.

        try { ZenithTheme.Apply(GetLauncherConfigSnapshot()); } catch { }

    }



    private async Task CheckForUpdatesBackgroundAsync()

    {

        try

        {

            var args = Environment.GetCommandLineArgs();
            if (args.Any(a => a.Equals("--test-update", StringComparison.OrdinalIgnoreCase) || a.Equals("--mock-update", StringComparison.OrdinalIgnoreCase)))
            {
                await Task.Delay(400);
                Avalonia.Threading.Dispatcher.UIThread.Post(OpenMockUpdate);
                return;
            }

            await Task.Delay(5000);

            var service = new GitHubUpdateService();

            var result = await service.CheckForUpdatesAsync();

            if (result is { IsNewerThanCurrent: true })

            {

                Avalonia.Threading.Dispatcher.UIThread.Post(() => OpenUpdate(result));

            }

        }

        catch (OperationCanceledException) { }

        catch (Exception ex)

        {

            LauncherLog.Error("Background update check failed.", ex);

        }

    }



    [ObservableProperty]

    private Views.UpdateNotificationViewModel? _updateVm;



    [ObservableProperty]

    private bool _isUpdateOpen;



    [ObservableProperty]

    private bool _isUpdateAvailable;



    [ObservableProperty]

    private bool _isUpdateChecking;



    [ObservableProperty]

    private bool _isInfoDialogOpen;



    [ObservableProperty]

    private string _infoDialogTitle = "";



    [ObservableProperty]

    private string _infoDialogMessage = "";



    [RelayCommand]

    private void CloseInfoDialog() => IsInfoDialogOpen = false;



    public void ShowInfoDialog(string title, string message)

    {

        InfoDialogTitle = title;

        InfoDialogMessage = message;

        IsInfoDialogOpen = true;

    }



    [RelayCommand]

    private void CloseUpdate()

    {

        IsUpdateOpen = false;

        IsUpdateAvailable = false;

    }



    [RelayCommand]

    private async Task CheckForUpdatesNowAsync()

    {

        if (IsUpdateChecking) return;

        IsUpdateChecking = true;

        StatusText = L10n.T("update_checking");

        try

        {

            var args = Environment.GetCommandLineArgs();
            if (args.Any(a => a.Equals("--test-update", StringComparison.OrdinalIgnoreCase) || a.Equals("--mock-update", StringComparison.OrdinalIgnoreCase)))
            {
                await Task.Delay(200);
                OpenMockUpdate();
                return;
            }

            var result = await new GitHubUpdateService().CheckForUpdatesAsync();

            if (result?.IsNewerThanCurrent == true)

            {

                OpenUpdate(result);

            }

            else

            {

                StatusText = L10n.T("update_uptodate");

                ShowInfoDialog(L10n.T("update_btn"), L10n.T("update_uptodate"));

            }

        }

        catch (Exception ex)

        {

            LauncherLog.Error("Manual update check failed.", ex);

            StatusText = L10n.T("update_failed");

            ShowInfoDialog(L10n.T("update_btn"), L10n.T("update_failed"));

        }

        finally

        {

            IsUpdateChecking = false;

        }

    }



    public void OpenMockUpdate()
    {
        var result = new UpdateCheckResult(
            Version: "1.2.0",
            DownloadUrl: "https://github.com/MrGiperTroll/Zenith-Launcher/releases/tag/v1.2.0",
            ReleaseNotes: "## ru\n• Переработан интерфейс лаунчера и дизайн окон\n• Улучшена производительность загрузки профилей\n• Добавлена поддержка новых версий Fabric и Forge\n• Оптимизировано потребление оперативной памяти\n\n## en\n• Redesigned launcher interface and window styles\n• Improved profile loading performance\n• Added support for newer Fabric and Forge builds\n• Optimized memory consumption\n\n## uk\n• Оновлено інтерфейс лаунчера та оформлення вікон\n• Покращено швидкодію завантаження профілів\n• Додано підтримку нових версій Fabric та Forge\n• Оптимізовано споживання оперативної пам'яті",
            IsNewerThanCurrent: true);

        OpenUpdate(result);
    }

    private void OpenUpdate(UpdateCheckResult result)

    {

        UpdateVm = new Views.UpdateNotificationViewModel(result, () =>

        {

            IsUpdateOpen = false;

            IsUpdateAvailable = false;

        });

        IsUpdateAvailable = true;

        IsUpdateOpen = true;

    }



    private async Task InitializeModLoadersAsync()

    {

        await Task.Delay(2000).ConfigureAwait(false);

        await _modLoaderService.InitializeDatabaseAsync().ConfigureAwait(false);

        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            await UpdateAvailableLoadersAsync(SelectedVersion);
        });

    }


    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)

    {

        base.OnPropertyChanged(e);

        if (e.PropertyName is "JvmArgs" or "MinRamMb" or "RamMb" or "SelectedLaunchBehavior" or "GameWidth" or "GameHeight" or "IsFullscreen" or "CustomJavaPath" or "SelectedJavaMode"

            or "AutoManageJava" or "DiscordRpcEnabled" or "SelectedLanguage" or "LauncherScale" or "SelectedParticle")

        {

            SaveLauncherConfig();

        }

    }



    public void SaveLauncherConfig()
    {
        if (_isLoadingConfig) return;

        try
        {
            ZenithPaths.EnsureAppDataExists();
            var configPath = ZenithPaths.ConfigFilePath;

            var configData = new LauncherConfigData
            {
                LauncherName = LauncherName,
                SelectedDirectoryMode = SelectedDirectoryMode,
                JavaMode = SelectedJavaMode,
                UseFlatVersionList = UseFlatVersionList,
                AutoManageJava = AutoManageJava,
                DiscordRpcEnabled = DiscordRpcEnabled,
                LauncherScale = LauncherScale,
                Language = SelectedLanguage,
                IsSidebarExpanded = IsSidebarExpanded
            };

            if (Launch != null && LauncherSettingsVm != null)
            {
                Launch.RamMb = LauncherSettingsVm.RamMb;
                Launch.MinRamMb = LauncherSettingsVm.MinRamMb;
            }

            Launch?.ApplyToConfig(configData);
            LauncherSettingsVm?.ApplyToConfig(configData);
            ThemeSettingsVm?.ApplyToConfig(configData);

            var json = System.Text.Json.JsonSerializer.Serialize(configData, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            using (var fs = new FileStream(configPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                using var writer = new StreamWriter(fs, System.Text.Encoding.UTF8);
                writer.Write(json);
                writer.Flush();
                fs.Flush(flushToDisk: true);
            }
        }
        catch { }
    }

    public LauncherConfigData GetLauncherConfigSnapshot()
    {
        var snapshot = new LauncherConfigData
        {
            LauncherName = LauncherName,
            SelectedDirectoryMode = SelectedDirectoryMode,
            JavaMode = SelectedJavaMode,
            UseFlatVersionList = UseFlatVersionList,
            AutoManageJava = AutoManageJava,
            DiscordRpcEnabled = DiscordRpcEnabled,
            LauncherScale = LauncherScale,
            Language = SelectedLanguage,
            IsSidebarExpanded = IsSidebarExpanded
        };

        if (Launch != null && LauncherSettingsVm != null)
        {
            Launch.RamMb = LauncherSettingsVm.RamMb;
            Launch.MinRamMb = LauncherSettingsVm.MinRamMb;
        }

        Launch?.ApplyToConfig(snapshot);
        LauncherSettingsVm?.ApplyToConfig(snapshot);
        ThemeSettingsVm?.ApplyToConfig(snapshot);

        return snapshot;
    }

    public void LoadLauncherConfig()
    {
        _isLoadingConfig = true;
        try
        {
            var configPath = ZenithPaths.ConfigFilePath;
            if (File.Exists(configPath))
            {
                var json = File.ReadAllText(configPath);
                var cfg = System.Text.Json.JsonSerializer.Deserialize<LauncherConfigData>(json);
                if (cfg != null)
                {
                    if (!string.IsNullOrWhiteSpace(cfg.LauncherName))
                        LauncherName = cfg.LauncherName;

                    if (string.IsNullOrWhiteSpace(cfg.Language))
                    {
                        SelectedLanguage = L10n.CurrentDisplay;
                        SaveLauncherConfig();
                        LauncherLog.Info($"First run: system language '{L10n.CurrentDisplay}' stored as default.");
                    }
                    else
                    {
                        SelectedLanguage = NormalizeStoredLanguage(cfg.Language);
                    }

                    if (!string.IsNullOrWhiteSpace(cfg.SelectedDirectoryMode))
                        SelectedDirectoryMode = cfg.SelectedDirectoryMode;

                    if (!string.IsNullOrWhiteSpace(cfg.JavaMode))
                        SelectedJavaMode = cfg.JavaMode;

                    UseFlatVersionList = cfg.UseFlatVersionList;
                    AutoManageJava = cfg.AutoManageJava;
                    DiscordRpcEnabled = cfg.DiscordRpcEnabled;
                    if (cfg.LauncherScale > 0.4 && cfg.LauncherScale < 3.0) LauncherScale = cfg.LauncherScale;
                    IsSidebarExpanded = cfg.IsSidebarExpanded;

                    Launch?.LoadFromConfig(cfg);
                    LauncherSettingsVm?.LoadFromConfig(cfg);
                    ThemeSettingsVm?.LoadFromConfig(cfg);
                }
            }
        }
        catch (Exception ex)
        {
            LauncherLog.Error("Failed to load launcher config - keeping current settings.", ex);
        }
        finally
        {
            _isLoadingConfig = false;
        }
    }



    /// <summary>

    /// Normalizes a stored language value (display name or legacy 2-letter code)

    /// into a display name that ApplyDisplay and the config file both understand.

    /// Returns "Auto" for "auto", empty, or completely unknown values.

    /// </summary>

    private static string NormalizeStoredLanguage(string stored)

    {

        return (stored ?? "").Trim() switch

        {

            "English" or "en" => "English",

            "Українська" or "uk" => "Українська",

            "Русский" or "ru" => "Русский",

            "Türkçe" or "tr" => "Türkçe",

            "Deutsch" or "de" => "Deutsch",

            "Français" or "fr" => "Français",

            "Auto" or "auto" => "Auto",

            _ => "Auto",

        };

    }


    public void RefreshState()

    {

        AccountsVm.RefreshAccounts();



        Instances.Clear();

        foreach (var inst in _instanceService.GetInstances())

        {

            inst.IsSelected = (inst.Id == SelectedInstanceId);

            inst.IsRunning = false;

            inst.IsLaunching = false;

            Instances.Add(inst);

        }



        HasInstances = Instances.Count > 0;



        var selected = _instanceService.GetSelectedInstance();

        if (selected != null)

        {

            SelectedInstance = selected;

            SelectedInstanceId = selected.Id;

            SelectedInstanceName = selected.Name;

            SelectedInstanceDetails = selected.DisplayVersion;

        }

        else

            {

                SelectedInstance = null;

                SelectedInstanceId = "";

                SelectedInstanceName = L10n.T("mw_no_profile");

                SelectedInstanceDetails = L10n.T("ei_click_create");

                IsRightSidebarOpen = false;

            }

    }



    [ObservableProperty] private bool _isToastVisible;
    [ObservableProperty] private string _toastMessage = string.Empty;
    private System.Threading.CancellationTokenSource? _toastCts;

    public void ShowToast(string message)
    {
        _toastCts?.Cancel();
        _toastCts?.Dispose();
        _toastCts = new System.Threading.CancellationTokenSource();
        var token = _toastCts.Token;

        ToastMessage = message;
        IsToastVisible = true;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(2500, token);
                if (!token.IsCancellationRequested)
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (!token.IsCancellationRequested)
                        {
                            IsToastVisible = false;
                        }
                    });
                }
            }
            catch (TaskCanceledException)
            {
                // Ignored - superseded by subsequent toast
            }
        });
    }

    [ObservableProperty] private bool _isSwitchAccountsMenuOpen;

    [RelayCommand]
    public void OpenSwitchAccountsMenu() => IsSwitchAccountsMenuOpen = true;

    [RelayCommand]
    public void CloseSwitchAccountsMenu() => IsSwitchAccountsMenuOpen = false;

    [RelayCommand]
    public void ToggleSwitchAccountsMenu() => IsSwitchAccountsMenuOpen = !IsSwitchAccountsMenuOpen;

    [RelayCommand]
    public void SwitchAccount(AccountModel? account)
    {
        if (account == null) return;
        AccountsVm.SwitchAccount(account);
        RefreshState();
        OnPropertyChanged(nameof(ActiveAccountName));
        OnPropertyChanged(nameof(ActiveAccountType));
        OnPropertyChanged(nameof(ActiveAccountRow));
        ShowToast(string.Format(L10n.T("acc_switched_toast"), account.Username));
    }

    [RelayCommand]
    public async Task CopyActiveUsernameAsync()
    {
        if (string.IsNullOrWhiteSpace(ActiveAccountName)) return;
        try
        {
            if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } window })
            {
                var topLevel = Avalonia.Controls.TopLevel.GetTopLevel(window);
                if (topLevel?.Clipboard != null)
                {
                    var transfer = new DataTransfer();
                    transfer.Add(DataTransferItem.CreateText(ActiveAccountName));
                    await topLevel.Clipboard.SetDataAsync(transfer);
                    ShowToast(string.Format(L10n.T("acc_copied_toast"), ActiveAccountName));
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to copy username: {ex.Message}");
        }
    }

    [ObservableProperty] private bool _isDeleteAccountConfirmOpen;
    [ObservableProperty] private string _pendingDeleteAccountName = string.Empty;
    [ObservableProperty] private AccountModel? _pendingDeleteAccount;

    [RelayCommand]
    public void RequestDeleteActiveAccount()
    {
        var active = ActiveAccountRow?.Model
            ?? AccountsVm.SelectedAccount
            ?? _accountService.GetActiveAccount()
            ?? _accountService.GetAccounts().FirstOrDefault(a => a.Username == ActiveAccountName)
            ?? _accountService.GetAccounts().FirstOrDefault();

        if (active == null)
        {
            ShowToast(L10n.T("acc_no_active_toast"));
            return;
        }

        PendingDeleteAccount = active;
        PendingDeleteAccountName = active.Username;
        IsDeleteAccountConfirmOpen = true;
    }

    [RelayCommand]
    private void CancelDeleteAccountConfirm()
    {
        IsDeleteAccountConfirmOpen = false;
        PendingDeleteAccount = null;
        PendingDeleteAccountName = string.Empty;
    }

    [RelayCommand]
    private void ConfirmDeleteAccount()
    {
        if (PendingDeleteAccount != null)
        {
            var name = PendingDeleteAccount.Username;
            AccountsVm.DeleteAccount(PendingDeleteAccount);
            RefreshState();
            OnPropertyChanged(nameof(ActiveAccountName));
            OnPropertyChanged(nameof(ActiveAccountType));
            OnPropertyChanged(nameof(ActiveAccountRow));
            ShowToast(string.Format(L10n.T("acc_deleted_toast"), name));
        }
        IsDeleteAccountConfirmOpen = false;
        PendingDeleteAccount = null;
        PendingDeleteAccountName = string.Empty;
    }

    [RelayCommand]

    private void OpenSettingsDialog()

    {

        NavigateToSettings();

    }







    [RelayCommand]

    private void OpenAccountsDialog()

    {

        NavigateToAccounts();

    }



    [RelayCommand]

    private void CloseDialogs()

    {

        SaveLauncherConfig();

        IsCreateDialogOpen = false;

        IsJavaInstallerOpen = false;

        IsVersionPickerOpen = false;

        IsElyByErrorOpen = false;

        IsDeleteConfirmOpen = false;

        IsRestartConfirmOpen = false;

        IsCloneDialogOpen = false;

        IsServerUnderDevDialogOpen = false;

        PendingDeleteInstance = null;

        FilterVersions();

    }




}
