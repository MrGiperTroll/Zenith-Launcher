using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CustomMcLauncher.Models;
using CustomMcLauncher.Services;

namespace CustomMcLauncher.ViewModels.Pages;

public sealed partial class SkinItem : ObservableObject
{
    public string Name { get; }
    public string FilePath { get; }
    public bool IsDefault { get; }

    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private Bitmap? _previewBitmap;

    public SkinItem(string name, string filePath, bool isDefault = false)
    {
        Name = name;
        FilePath = filePath;
        IsDefault = isDefault;
        if (File.Exists(filePath))
        {
            try
            {
                PreviewBitmap = new Bitmap(filePath);
            }
            catch { }
        }
    }
}

public sealed partial class SkinSelectorViewModel : ObservableObject
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly IAccountService? _accountService;

    private static readonly byte[] EmptyPngBytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAEAAAABACAYAAACqaXHeAAAAPUlEQVR42u3BAQ0AAADCoPdPbQ43oAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAD4GwpQAAEZsB3XAAAAAElFTkSuQmCC");

    [ObservableProperty] private SkinItem? _selectedSkin;
    [ObservableProperty] private Bitmap? _activeSkinBitmap;
    [ObservableProperty] private string _statusMessage = "";

    public ObservableCollection<SkinItem> SavedSkins { get; } = new();
    public ObservableCollection<SkinItem> DefaultSkins { get; } = new();

    public SkinSelectorViewModel(IAccountService? accountService = null)
    {
        _accountService = accountService;
        InitializeDefaultSkins();
        _ = LoadSavedAndAccountSkinsAsync();
    }

    private void InitializeDefaultSkins()
    {
        var defaults = new[]
        {
            ("Steve", "steve"),
            ("Alex", "alex"),
            ("Ari", "ari"),
            ("Makena", "makena"),
            ("Zuri", "zuri"),
            ("Sunny", "sunny"),
            ("Kai", "kai"),
            ("Noor", "noor"),
            ("Efe", "efe")
        };

        var skinDir = Path.Combine(ZenithPaths.AppDataDir, "skins", "defaults");
        Directory.CreateDirectory(skinDir);

        foreach (var (name, key) in defaults)
        {
            var p = Path.Combine(skinDir, $"{key}.png");
            // If file doesn't exist, create an empty 64x64 placeholder or use if present
            if (!File.Exists(p))
            {
                try
                {
                    File.WriteAllBytes(p, EmptyPngBytes);
                }
                catch { }
            }
            var item = new SkinItem(name, p, isDefault: true);
            DefaultSkins.Add(item);
        }

        if (DefaultSkins.Count > 0)
        {
            SelectSkin(DefaultSkins[0]);
        }
    }

    public async Task LoadSavedAndAccountSkinsAsync()
    {
        // 1. Load saved skins from AppData/skins/saved
        var savedDir = Path.Combine(ZenithPaths.AppDataDir, "skins", "saved");
        Directory.CreateDirectory(savedDir);
        foreach (var file in Directory.GetFiles(savedDir, "*.png"))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (!SavedSkins.Any(s => s.FilePath.Equals(file, StringComparison.OrdinalIgnoreCase)))
            {
                SavedSkins.Add(new SkinItem(name, file));
            }
        }

        // 2. Fetch skin for active Microsoft or Ely.by account
        if (_accountService != null)
        {
            var active = _accountService.GetActiveAccount();
            if (active != null && !string.IsNullOrWhiteSpace(active.Username))
            {
                await FetchAccountSkinAsync(active.Username, active.Type);
            }
        }
    }

    public async Task FetchAccountSkinAsync(string username, AccountType type)
    {
        try
        {
            StatusMessage = $"Loading skin for {username}...";
            string skinUrl = type switch
            {
                AccountType.ElyBy => $"http://skinsystem.ely.by/skins/{Uri.EscapeDataString(username)}.png",
                _ => $"https://minotar.net/skin/{Uri.EscapeDataString(username)}"
            };

            var bytes = await Http.GetByteArrayAsync(skinUrl);
            if (bytes is { Length: > 0 })
            {
                var savedDir = Path.Combine(ZenithPaths.AppDataDir, "skins", "saved");
                Directory.CreateDirectory(savedDir);
                var dest = Path.Combine(savedDir, $"{username}.png");
                await File.WriteAllBytesAsync(dest, bytes);

                var existing = SavedSkins.FirstOrDefault(s => s.FilePath.Equals(dest, StringComparison.OrdinalIgnoreCase));
                if (existing != null) SavedSkins.Remove(existing);

                var newItem = new SkinItem(username, dest);
                SavedSkins.Insert(0, newItem);
                SelectSkin(newItem);
                StatusMessage = $"Loaded skin for {username}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not fetch skin: {ex.Message}";
        }
    }

    [RelayCommand]
    public void SelectSkin(SkinItem? skin)
    {
        if (skin == null) return;
        foreach (var s in DefaultSkins) s.IsSelected = false;
        foreach (var s in SavedSkins) s.IsSelected = false;

        skin.IsSelected = true;
        SelectedSkin = skin;

        if (File.Exists(skin.FilePath))
        {
            try
            {
                ActiveSkinBitmap = new Bitmap(skin.FilePath);
            }
            catch { }
        }
    }

    [RelayCommand]
    public async Task AddSkinAsync()
    {
        var topLevel = Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;

        if (topLevel?.StorageProvider == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select Minecraft Skin PNG (64x64)",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Minecraft Skin (*.png)") { Patterns = new[] { "*.png" } }
            }
        });

        if (files.Count > 0)
        {
            var src = files[0].Path.LocalPath;
            var name = Path.GetFileNameWithoutExtension(src);
            var savedDir = Path.Combine(ZenithPaths.AppDataDir, "skins", "saved");
            Directory.CreateDirectory(savedDir);
            var dest = Path.Combine(savedDir, Path.GetFileName(src));
            File.Copy(src, dest, true);

            var newItem = new SkinItem(name, dest);
            SavedSkins.Insert(0, newItem);
            SelectSkin(newItem);
        }
    }

    [RelayCommand]
    public void EditSkin()
    {
        if (SelectedSkin == null || !File.Exists(SelectedSkin.FilePath)) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(SelectedSkin.FilePath)
            {
                UseShellExecute = true
            });
        }
        catch { }
    }
}
