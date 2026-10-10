using System;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CustomMcLauncher.Models;
using CustomMcLauncher.Services;
using CustomMcLauncher.ViewModels.Items;
using CustomMcLauncher.Views.Experimental;

namespace CustomMcLauncher.ViewModels;

public enum ExpSection
{
    Library,
    Discover,
    Servers,
    Accounts,
    Settings
}

/// <summary>
/// Experimental shell: same engine (MainWindowViewModel + all services),
/// completely separate layout. Nothing here touches the classic UI.
/// </summary>
public partial class ExperimentalShellViewModel : ObservableObject
{
    public MainWindowViewModel Core { get; }

    [ObservableProperty]
    private ExpSection _section = ExpSection.Library;

    [ObservableProperty]
    private string _librarySearch = "";

    public ObservableCollection<InstanceModel> FilteredInstances { get; } = new();

    public bool IsLibrary => Section == ExpSection.Library;
    public bool IsDiscover => Section == ExpSection.Discover;
    public bool IsServers => Section == ExpSection.Servers;
    public bool IsAccounts => Section == ExpSection.Accounts;
    public bool IsSettings => Section == ExpSection.Settings;

    public ExperimentalShellViewModel(MainWindowViewModel? core = null)
    {
        Core = core ?? new MainWindowViewModel();
        Core.Instances.CollectionChanged += (_, _) => RefreshLibrary();
        RefreshLibrary();
        Services.LauncherLog.Info("Experimental shell started.");
    }

    partial void OnSectionChanged(ExpSection value)
    {
        OnPropertyChanged(nameof(IsLibrary));
        OnPropertyChanged(nameof(IsDiscover));
        OnPropertyChanged(nameof(IsServers));
        OnPropertyChanged(nameof(IsAccounts));
        OnPropertyChanged(nameof(IsSettings));
        if (value == ExpSection.Servers && Core.CreateServerVm == null)
            Core.NavigateToCreateServer();
    }

    partial void OnLibrarySearchChanged(string value) => RefreshLibrary();

    public void RefreshLibrary()
    {
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                var q = (LibrarySearch ?? "").Trim();
                FilteredInstances.Clear();
                foreach (var inst in Core.Instances)
                {
                    if (q.Length > 0 && !inst.Name.Contains(q, StringComparison.OrdinalIgnoreCase))
                        continue;
                    FilteredInstances.Add(inst);
                }
            }
            catch { }
        });
    }

    [RelayCommand]
    public void SelectSection(string name)
    {
        if (Enum.TryParse<ExpSection>(name, out var s))
            Section = s;
    }

    [RelayCommand]
    public void SelectInstance(InstanceModel? inst)
    {
        if (inst != null)
            Core.SelectedInstance = inst;
    }

    [RelayCommand]
    public void PlayInstance(InstanceModel? inst)
    {
        if (inst == null) return;
        Core.SelectedInstance = inst;
        if (Core.LaunchOrCancelCommand.CanExecute(null))
            Core.LaunchOrCancelCommand.Execute(null);
    }

    [RelayCommand]
    public void ManageInstance(InstanceModel? inst)
    {
        if (inst == null) return;
        Core.NavigateToEditInstance(inst);
        var vm = Core.CurrentEditInstanceVm;
        if (vm == null) return;
        var win = new ExperimentalEditorWindow
        {
            Title = inst.Name,
            Content = new Views.EditInstanceView { DataContext = vm }
        };
        win.Show();
    }

    [RelayCommand]
    public void CreateInstance()
    {
        Core.NavigateToCreateProfile();
        var vm = Core.CreateProfileVm;
        if (vm == null) return;
        var win = new Window
        {
            Title = "New Instance",
            Width = 560,
            Height = 660,
            MinWidth = 480,
            MinHeight = 560,
            WindowStartupLocation = Avalonia.Controls.WindowStartupLocation.CenterOwner,
            Content = new Views.CreateProfileView { DataContext = vm }
        };
        void Close()
        {
            try { win.Close(); } catch { }
        }
        vm.RequestClose += Close;
        vm.ProfileCreated += _ => Close();
        win.Closed += (_, _) =>
        {
            try { vm.RequestClose -= Close; } catch { }
        };
        try { win.Show(); } catch { Close(); }
    }

    [RelayCommand]
    public void OpenDiscoverDetails(ModrinthProjectItemViewModel? item)
    {
        if (item == null) return;
        Core.ModpacksBrowser.SelectedProject = item;
    }

    [RelayCommand]
    public void CloseDiscoverDetails()
    {
        Core.ModpacksBrowser.SelectedProject = null;
    }
}
