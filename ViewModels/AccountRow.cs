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
using ZenithLauncher.Models;
using ZenithLauncher.Services;

namespace ZenithLauncher.ViewModels;

public partial class AccountRow : ObservableObject, IDisposable
{
    public static void ClearMemoryCache() => MemoryCache.Clear();
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    // Bounded process-wide skin-head cache: username -> raw PNG bytes.
    private static readonly ZenithLauncher.Common.Utils.LruCache<string, byte[]> MemoryCache =
        new(32, comparer: StringComparer.OrdinalIgnoreCase);

    private volatile bool _disposed;

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
    [ObservableProperty] private bool _isDragging;
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
                _headImage = Bitmap.DecodeToWidth(ms, 64, BitmapInterpolationMode.MediumQuality);
            }
            catch
            {
                // Corrupted cache entry - drop it and fall back to the letter avatar.
                MemoryCache.Remove(Username);
            }
        }
    }

    partial void OnHeadImageChanged(Bitmap? value) => OnPropertyChanged(nameof(HasHead));

    /// <summary>Loads the Minecraft skin head (32px face) for this account.
    /// Uses the memory/disk caches first; only hits the network on a real miss.</summary>
    public async Task LoadHeadAsync()
    {
        if (_disposed || string.IsNullOrWhiteSpace(Username)) return;
        if (HeadImage != null && MemoryCache.TryGet(Username, out _)) return;
        if (MemoryCache.TryGet(Username, out var mem))
        {
            SetFromBytes(mem);
            return;
        }

        try
        {
            var url = $"https://mc-heads.net/avatar/{Uri.EscapeDataString(Username)}/32";
            var bytes = await Http.GetByteArrayAsync(url).ConfigureAwait(true);
            if (_disposed || bytes.Length == 0) return;

            MemoryCache.Set(Username, bytes);
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
        if (_disposed) return;
        _ = Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (_disposed) return;
            try
            {
                using var ms = new MemoryStream(bytes);
                var next = Bitmap.DecodeToWidth(ms, 64, BitmapInterpolationMode.MediumQuality);
                var old = HeadImage;
                HeadImage = next;
                try { old?.Dispose(); } catch { }
            }
            catch { }
        });
    }

    private static bool TryGetCachedBytes(string username, out byte[] bytes)
    {
        if (MemoryCache.TryGet(username, out bytes!)) return true;
        try
        {
            var path = DiskPath(username);
            if (File.Exists(path))
            {
                bytes = File.ReadAllBytes(path);
                if (bytes.Length > 0)
                {
                    MemoryCache.Set(username, bytes);
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

    public void Dispose()
    {
        _disposed = true;
        try
        {
            var old = HeadImage;
            HeadImage = null;
            old?.Dispose();
        }
        catch { }
    }
}
