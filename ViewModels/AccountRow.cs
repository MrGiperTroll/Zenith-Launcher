using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CustomMcLauncher.Models;
using CustomMcLauncher.Services;

namespace CustomMcLauncher.ViewModels;

public partial class AccountRow : ObservableObject
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    // Process-wide skin-head cache: username → raw PNG bytes. Account rows are
    // recreated on every RefreshState / account switch; without this cache each
    // rebuild refetched the head over the network, so avatars flickered
    // (letter placeholder → image) every single time.
    private static readonly ConcurrentDictionary<string, byte[]> MemoryCache = new(StringComparer.OrdinalIgnoreCase);

    private static string DiskCacheDir => Path.Combine(ZenithPaths.AppDataDir, "avatars");

    public AccountModel Model { get; }

    public string Username => Model.Username;

    public string TypeDisplay => Model.Type switch
    {
        AccountType.Microsoft => "Microsoft",
        AccountType.ElyBy => "Ely.by",
        _ => "Offline"
    };

    public string Initial => string.IsNullOrWhiteSpace(Username) ? "?" : Username[..1].ToUpperInvariant();

    [ObservableProperty] private bool _isActive;
    [ObservableProperty] private Bitmap? _headImage;

    public bool HasHead => HeadImage != null;

    public AccountRow(AccountModel model)
    {
        Model = model;
        // Restore from cache synchronously so the avatar is visible the moment
        // the row appears — no placeholder flash, no network wait.
        if (!string.IsNullOrWhiteSpace(Username) && TryGetCachedBytes(Username, out var cached))
        {
            try
            {
                using var ms = new MemoryStream(cached);
                _headImage = new Bitmap(ms);
            }
            catch
            {
                // Corrupted cache entry - drop it and fall back to the letter avatar.
                MemoryCache.TryRemove(Username, out _);
            }
        }
    }

    partial void OnHeadImageChanged(Bitmap? value) => OnPropertyChanged(nameof(HasHead));

    /// <summary>Loads the Minecraft skin head (32px face) for this account.
    /// Uses the memory/disk caches first; only hits the network on a real miss.</summary>
    public async Task LoadHeadAsync()
    {
        if (string.IsNullOrWhiteSpace(Username)) return;
        if (HeadImage != null && MemoryCache.ContainsKey(Username)) return;
        if (MemoryCache.TryGetValue(Username, out var mem))
        {
            SetFromBytes(mem);
            return;
        }

        try
        {
            var url = $"https://mc-heads.net/avatar/{Uri.EscapeDataString(Username)}/32";
            var bytes = await Http.GetByteArrayAsync(url).ConfigureAwait(true);
            if (bytes.Length == 0) return;

            MemoryCache[Username] = bytes;
            try
            {
                Directory.CreateDirectory(DiskCacheDir);
                File.WriteAllBytes(DiskPath(Username), bytes);
            }
            catch { }

            SetFromBytes(bytes);
        }
        catch
        {
            // No internet / unknown skin - keep the letter avatar
        }
    }

    private void SetFromBytes(byte[] bytes)
    {
        _ = Dispatcher.UIThread.InvokeAsync(() =>
        {
            try
            {
                using var ms = new MemoryStream(bytes);
                HeadImage?.Dispose();
                HeadImage = new Bitmap(ms);
            }
            catch { }
        });
    }

    private static bool TryGetCachedBytes(string username, out byte[] bytes)
    {
        if (MemoryCache.TryGetValue(username, out bytes!)) return true;
        try
        {
            var path = DiskPath(username);
            if (File.Exists(path))
            {
                bytes = File.ReadAllBytes(path);
                if (bytes.Length > 0)
                {
                    MemoryCache[username] = bytes;
                    return true;
                }
            }
        }
        catch { }
        return false;
    }

    private static string DiskPath(string username)
    {
        // Sanitize the name and append a short stable hash so different names
        // can never collide after sanitization.
        var sb = new StringBuilder();
        foreach (var c in username)
            sb.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_');
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(username));
        return Path.Combine(DiskCacheDir, $"{sb}_{Convert.ToHexString(hash)[..8].ToLowerInvariant()}.png");
    }
}
