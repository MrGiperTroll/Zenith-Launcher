using System;
using CommunityToolkit.Mvvm.ComponentModel;
using ZenithLauncher.Models;
using ZenithLauncher.ViewModels.Pages;

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

public enum NavTransitionKind
{
    LateralTab,
    HistoryBack,
    HistoryForward,
    DrillIn,
    DrillOut
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
