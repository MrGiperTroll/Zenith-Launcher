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
    public string BustPath { get; }
    public bool IsDefault { get; }

    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private Bitmap? _previewBitmap;

    public SkinItem(string name, string filePath, string bustPath = "", bool isDefault = false)
    {
        Name = name;
        FilePath = filePath;
        BustPath = bustPath;
        IsDefault = isDefault;

        LoadPreview();
    }

    public void LoadPreview()
    {
        try
        {
            if (!string.IsNullOrEmpty(BustPath) && File.Exists(BustPath))
            {
                PreviewBitmap = new Bitmap(BustPath);
            }
            else if (File.Exists(FilePath))
            {
                PreviewBitmap = new Bitmap(FilePath);
            }
        }
        catch { }
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

    // --- In-App Skin Editor Modal ---
    [ObservableProperty] private bool _isSkinEditorOpen;
    [ObservableProperty] private bool _isClassicModel = true;
    [ObservableProperty] private bool _isSlimModel = false;
    [ObservableProperty] private bool _showHat = true;
    [ObservableProperty] private bool _showJacket = true;
    [ObservableProperty] private bool _showLeftSleeve = true;
    [ObservableProperty] private bool _showRightSleeve = true;
    [ObservableProperty] private bool _showLeftPants = true;
    [ObservableProperty] private bool _showRightPants = true;
    [ObservableProperty] private string _selectedCape = "None";

    public ObservableCollection<string> AvailableCapes { get; } = new()
    {
        "None",
        "Migrator Cape",
        "Vanilla Cape",
        "15th Anniversary Cape",
        "Cherry Blossom Cape",
        "Founder's Cape"
    };

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
        var bustDir = Path.Combine(ZenithPaths.AppDataDir, "skins", "busts");
        Directory.CreateDirectory(skinDir);
        Directory.CreateDirectory(bustDir);

        foreach (var (name, key) in defaults)
        {
            var p = Path.Combine(skinDir, $"{key}.png");
            var b = Path.Combine(bustDir, $"{key}.png");

            if (!File.Exists(p))
            {
                try
                {
                    File.WriteAllBytes(p, EmptyPngBytes);
                }
                catch { }
            }

            var item = new SkinItem(name, p, b, isDefault: true);
            DefaultSkins.Add(item);

            // Fetch 3D bust if not cached locally
            if (!File.Exists(b))
            {
                _ = DownloadBustAsync(name, b, item);
            }
        }

        if (DefaultSkins.Count > 0)
        {
            SelectSkin(DefaultSkins[0]);
        }
    }

    private async Task DownloadBustAsync(string name, string destPath, SkinItem item)
    {
        try
        {
            var url = $"https://minotar.net/bust/{Uri.EscapeDataString(name)}/150.png";
            var bytes = await Http.GetByteArrayAsync(url);
            if (bytes is { Length: > 0 })
            {
                await File.WriteAllBytesAsync(destPath, bytes);
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    item.LoadPreview();
                });
            }
        }
        catch { }
    }

    public async Task LoadSavedAndAccountSkinsAsync()
    {
        var savedDir = Path.Combine(ZenithPaths.AppDataDir, "skins", "saved");
        var bustDir = Path.Combine(ZenithPaths.AppDataDir, "skins", "busts");
        Directory.CreateDirectory(savedDir);
        Directory.CreateDirectory(bustDir);

        foreach (var file in Directory.GetFiles(savedDir, "*.png"))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            var bustFile = Path.Combine(bustDir, $"{name}.png");
            if (!SavedSkins.Any(s => s.FilePath.Equals(file, StringComparison.OrdinalIgnoreCase)))
            {
                var item = new SkinItem(name, file, bustFile);
                SavedSkins.Add(item);
                if (!File.Exists(bustFile))
                {
                    _ = DownloadBustAsync(name, bustFile, item);
                }
            }
        }

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
                var bustDir = Path.Combine(ZenithPaths.AppDataDir, "skins", "busts");
                Directory.CreateDirectory(savedDir);
                Directory.CreateDirectory(bustDir);

                var dest = Path.Combine(savedDir, $"{username}.png");
                var bustDest = Path.Combine(bustDir, $"{username}.png");
                await File.WriteAllBytesAsync(dest, bytes);

                var existing = SavedSkins.FirstOrDefault(s => s.FilePath.Equals(dest, StringComparison.OrdinalIgnoreCase));
                if (existing != null) SavedSkins.Remove(existing);

                var newItem = new SkinItem(username, dest, bustDest);
                SavedSkins.Insert(0, newItem);
                SelectSkin(newItem);
                StatusMessage = $"Loaded skin for {username}";

                _ = DownloadBustAsync(username, bustDest, newItem);
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
            var bustDir = Path.Combine(ZenithPaths.AppDataDir, "skins", "busts");
            Directory.CreateDirectory(savedDir);
            Directory.CreateDirectory(bustDir);

            var dest = Path.Combine(savedDir, Path.GetFileName(src));
            var bustDest = Path.Combine(bustDir, $"{name}.png");
            File.Copy(src, dest, true);

            var newItem = new SkinItem(name, dest, bustDest);
            SavedSkins.Insert(0, newItem);
            SelectSkin(newItem);

            _ = DownloadBustAsync(name, bustDest, newItem);
        }
    }

    [RelayCommand]
    public void EditSkin()
    {
        IsSkinEditorOpen = true;
    }

    [RelayCommand]
    public void CloseSkinEditor()
    {
        IsSkinEditorOpen = false;
    }

    [RelayCommand]
    public void SaveSkinSettings()
    {
        IsSkinEditorOpen = false;
        StatusMessage = "Skin settings applied.";
    }

    [RelayCommand]
    public void SetClassicModel()
    {
        IsClassicModel = true;
        IsSlimModel = false;
    }

    [RelayCommand]
    public void SetSlimModel()
    {
        IsClassicModel = false;
        IsSlimModel = true;
    }
}
