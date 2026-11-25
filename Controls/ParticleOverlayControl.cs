using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Rendering.Composition;
using Avalonia.Skia;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SkiaSharp;
using ZenithLauncher.Models;
using ZenithLauncher.Services;

namespace ZenithLauncher.Controls;

public enum ParticleKind
{
    Off = 0,
    Snow = 1,
    Leaves = 2,
    Sakura = 3,
    Custom = 4
}

/// <summary>
/// Render-thread ambient particle layer powered by Avalonia 12 CompositionCustomVisualHandler.
/// Loads crisp Minecraft pixel-art sprites from local client JAR caches for the active mode only,
/// renders with SKFilterMode.Nearest and SKColorFilter tinting, uses zero per-frame allocations,
/// and disposes textures immediately when disabled or hidden.
/// Includes an optional Beta snow physics &amp; 1D heightmap drift accumulation system.
/// </summary>
public class ParticleOverlayControl : Control
{
    public const int MaxParticles = 160;
    public const int MaxDynamicSnowflakes = 240;
    private const int TotalParticleSlots = MaxParticles + MaxDynamicSnowflakes;

    public static readonly StyledProperty<string> ParticleTypeProperty =
        AvaloniaProperty.Register<ParticleOverlayControl, string>(nameof(ParticleType), "Off");

    public static readonly StyledProperty<string> LeafPaletteProperty =
        AvaloniaProperty.Register<ParticleOverlayControl, string>(nameof(LeafPalette), "Autumn");

    public static readonly StyledProperty<string> CustomImagePathProperty =
        AvaloniaProperty.Register<ParticleOverlayControl, string>(nameof(CustomImagePath), string.Empty);

    public static readonly StyledProperty<int> ParticleCountProperty =
        AvaloniaProperty.Register<ParticleOverlayControl, int>(nameof(ParticleCount), 45);

    public static readonly StyledProperty<double> ParticleSpeedProperty =
        AvaloniaProperty.Register<ParticleOverlayControl, double>(nameof(ParticleSpeed), 1.0);

    public static readonly StyledProperty<double> ParticleWindProperty =
        AvaloniaProperty.Register<ParticleOverlayControl, double>(nameof(ParticleWind), 0.25);

    public static readonly StyledProperty<double> ParticleSizeProperty =
        AvaloniaProperty.Register<ParticleOverlayControl, double>(nameof(ParticleSize), 1.0);

    public static readonly StyledProperty<double> ParticleOpacityProperty =
        AvaloniaProperty.Register<ParticleOverlayControl, double>(nameof(ParticleOpacity), 0.65);

    public static readonly StyledProperty<bool> SnowPhysicsEnabledProperty =
        AvaloniaProperty.Register<ParticleOverlayControl, bool>(nameof(SnowPhysicsEnabled), false);

    public static readonly StyledProperty<string> ActivePageKeyProperty =
        AvaloniaProperty.Register<ParticleOverlayControl, string>(nameof(ActivePageKey), "Profiles");

    public static readonly StyledProperty<bool> IsModalOccludedProperty =
        AvaloniaProperty.Register<ParticleOverlayControl, bool>(nameof(IsModalOccluded), false);

    public string ParticleType
    {
        get => GetValue(ParticleTypeProperty);
        set => SetValue(ParticleTypeProperty, value);
    }

    public string LeafPalette
    {
        get => GetValue(LeafPaletteProperty);
        set => SetValue(LeafPaletteProperty, value);
    }

    public string CustomImagePath
    {
        get => GetValue(CustomImagePathProperty);
        set => SetValue(CustomImagePathProperty, value);
    }

    public int ParticleCount
    {
        get => GetValue(ParticleCountProperty);
        set => SetValue(ParticleCountProperty, value);
    }

    public double ParticleSpeed
    {
        get => GetValue(ParticleSpeedProperty);
        set => SetValue(ParticleSpeedProperty, value);
    }

    public double ParticleWind
    {
        get => GetValue(ParticleWindProperty);
        set => SetValue(ParticleWindProperty, value);
    }

    public double ParticleSize
    {
        get => GetValue(ParticleSizeProperty);
        set => SetValue(ParticleSizeProperty, value);
    }

    public double ParticleOpacity
    {
        get => GetValue(ParticleOpacityProperty);
        set => SetValue(ParticleOpacityProperty, value);
    }

    public bool SnowPhysicsEnabled
    {
        get => GetValue(SnowPhysicsEnabledProperty);
        set => SetValue(SnowPhysicsEnabledProperty, value);
    }

    public string ActivePageKey
    {
        get => GetValue(ActivePageKeyProperty);
        set => SetValue(ActivePageKeyProperty, value);
    }

    public bool IsModalOccluded
    {
        get => GetValue(IsModalOccludedProperty);
        set => SetValue(IsModalOccludedProperty, value);
    }

    public bool IsSnowPhysicsActive =>
        ParseKind(ParticleType) == ParticleKind.Snow && SnowPhysicsEnabled;

    private CompositionCustomVisual? _customVisual;
    private Window? _hostWindow;
    private string _loadedImagePath = string.Empty;
    private SKBitmap? _pendingCustomBitmap;
    private bool _customBitmapDirty;
    private ParticleKind _loadedSpriteKind = ParticleKind.Off;
    private int _spriteLoadGeneration;

    private bool _physicsHooksAttached;
    private bool _syncQueued;
    private bool _snowDataRequested;
    private readonly List<ColliderSnapshot> _stagingSnapshots = new(64);
    private readonly SharedPhysicsBridge _bridge = new();
    private ParticleVisualHandler? _handler;

    public ParticleOverlayControl()
    {
        IsHitTestVisible = false;
        ClipToBounds = true;
    }

    public void ClearSnowAccumulation()
    {
        _bridge.ResetLatestPersistence();
        _customVisual?.SendHandlerMessage(ClearSnowMsg.Instance);
        _ = Task.Run(SnowAccumulationStore.ClearFile);
    }

    public void FlushSnowPersistence()
    {
        if (!IsSnowPhysicsActive && !_bridge.HasLatestPersistence())
            return;

        if (_customVisual != null)
        {
            using var signal = new ManualResetEventSlim(false);
            _customVisual.SendHandlerMessage(new FlushSnowMsg(signal));
            signal.Wait(80);
        }

        var snapshot = _bridge.TakeLatestPersistenceCopy();
        if (snapshot != null)
        {
            SnowAccumulationStore.Save(snapshot);
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        var p = change.Property;
        if (p == CustomImagePathProperty || p == ParticleTypeProperty || p == SnowPhysicsEnabledProperty)
        {
            if (p == ParticleTypeProperty)
                UpdateOverlayVisibility();

            RefreshCustomBitmapCache();
            EnsureSpritesForCurrentKind();
            UpdatePhysicsHooks();
            PushStateToHandler();
        }
        else if (p == LeafPaletteProperty
              || p == ParticleCountProperty
              || p == ParticleSpeedProperty
              || p == ParticleWindProperty
              || p == ParticleSizeProperty
              || p == ParticleOpacityProperty
              || p == ActivePageKeyProperty
              || p == IsModalOccludedProperty)
        {
            if (p == ActivePageKeyProperty || p == IsModalOccludedProperty)
                ScheduleColliderSync();
            PushStateToHandler();
        }
        else if (p == BoundsProperty)
        {
            if (_customVisual != null)
            {
                _customVisual.Size = new Vector(Bounds.Width, Bounds.Height);
                _customVisual.SendHandlerMessage(new ViewportSizeMsg((float)Bounds.Width, (float)Bounds.Height));
            }
            if (IsSnowPhysicsActive)
                ScheduleColliderSync();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        AttachHostWindow();

        var elemVisual = ElementComposition.GetElementVisual(this);
        if (elemVisual != null)
        {
            _handler = new ParticleVisualHandler(_bridge);
            _customVisual = elemVisual.Compositor.CreateCustomVisual(_handler);
            _customVisual.Size = new Vector(Bounds.Width, Bounds.Height);
            ElementComposition.SetElementChildVisual(this, _customVisual);
            RefreshCustomBitmapCache();
            EnsureSpritesForCurrentKind();
            UpdatePhysicsHooks();
            PushStateToHandler();
        }

        UpdateOverlayVisibility();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        FlushSnowPersistence();
        DetachPhysicsHooks();
        DetachHostWindow();
        _spriteLoadGeneration++;
        _loadedSpriteKind = ParticleKind.Off;
        _snowDataRequested = false;

        if (_customVisual != null)
        {
            _customVisual.SendHandlerMessage(ShutdownMsg.Instance);
            ElementComposition.SetElementChildVisual(this, null);
            _customVisual = null;
        }
        _handler = null;

        DisposePendingBitmap();
        _loadedImagePath = string.Empty;
        base.OnDetachedFromVisualTree(e);
    }

    private void AttachHostWindow()
    {
        DetachHostWindow();
        if (TopLevel.GetTopLevel(this) is Window win)
        {
            _hostWindow = win;
            _hostWindow.PropertyChanged += OnHostWindowPropertyChanged;
        }
    }

    private void DetachHostWindow()
    {
        DetachPhysicsHooks();
        if (_hostWindow != null)
        {
            _hostWindow.PropertyChanged -= OnHostWindowPropertyChanged;
            _hostWindow = null;
        }
    }

    private void OnHostWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Window.WindowStateProperty || e.Property == Visual.IsVisibleProperty)
        {
            PushPauseState();
        }
    }

    private void UpdatePhysicsHooks()
    {
        bool shouldAttach = IsSnowPhysicsActive && _hostWindow != null && _customVisual != null;
        if (shouldAttach)
        {
            if (!_physicsHooksAttached && _hostWindow != null)
            {
                _physicsHooksAttached = true;
                SnowColliderRegistry.IsCollectionActive = true;
                SnowColliderRegistry.LayoutDirty += OnColliderLayoutDirty;
                _hostWindow.LayoutUpdated += OnHostWindowLayoutUpdated;
                _hostWindow.AddHandler(PointerMovedEvent, OnHostPointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
                _hostWindow.AddHandler(PointerExitedEvent, OnHostPointerExited, RoutingStrategies.Tunnel, handledEventsToo: true);
            }

            if (!_snowDataRequested)
            {
                _snowDataRequested = true;
                LoadPersistedSnowAsync();
            }

            ScheduleColliderSync();
        }
        else
        {
            DetachPhysicsHooks();
            if (!IsSnowPhysicsActive)
                _snowDataRequested = false;
        }
    }

    private void DetachPhysicsHooks()
    {
        if (!_physicsHooksAttached) return;
        _physicsHooksAttached = false;
        SnowColliderRegistry.IsCollectionActive = false;
        SnowColliderRegistry.LayoutDirty -= OnColliderLayoutDirty;
        if (_hostWindow != null)
        {
            _hostWindow.LayoutUpdated -= OnHostWindowLayoutUpdated;
            _hostWindow.RemoveHandler(PointerMovedEvent, OnHostPointerMoved);
            _hostWindow.RemoveHandler(PointerExitedEvent, OnHostPointerExited);
        }
        _bridge.ClearPointer();
    }

    private void LoadPersistedSnowAsync()
    {
        _ = Task.Run(() =>
        {
            var data = SnowAccumulationStore.Load();
            Dispatcher.UIThread.Post(() =>
            {
                if (_customVisual != null && IsSnowPhysicsActive)
                {
                    _customVisual.SendHandlerMessage(new LoadSnowMsg(data));
                }
            });
        });
    }

    private void OnColliderLayoutDirty() => ScheduleColliderSync();

    private void OnHostWindowLayoutUpdated(object? sender, EventArgs e)
    {
        if (IsSnowPhysicsActive)
            ScheduleColliderSync();
    }

    private void ScheduleColliderSync()
    {
        if (_syncQueued || !IsSnowPhysicsActive || !this.IsAttachedToVisualTree())
            return;

        _syncQueued = true;
        Dispatcher.UIThread.Post(PerformColliderSync, DispatcherPriority.Render);
    }

    private void PerformColliderSync()
    {
        _syncQueued = false;
        if (!IsSnowPhysicsActive || !this.IsAttachedToVisualTree() || _customVisual == null)
            return;

        SnowColliderRegistry.CollectSnapshots(this, _stagingSnapshots);
        _bridge.PublishSnapshots(_stagingSnapshots);
    }

    private void OnHostPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!IsSnowPhysicsActive) return;
        var pos = e.GetPosition(this);
        _bridge.PublishPointer((float)pos.X, (float)pos.Y, active: true);
    }

    private void OnHostPointerExited(object? sender, PointerEventArgs e)
    {
        _bridge.ClearPointer();
    }

    private void UpdateOverlayVisibility()
    {
        var kind = ParseKind(ParticleType);
        IsVisible = kind != ParticleKind.Off;
    }

    private bool IsHostPaused()
    {
        if (_hostWindow == null) return false;
        return !_hostWindow.IsVisible || _hostWindow.WindowState == WindowState.Minimized;
    }

    private void PushPauseState()
    {
        _customVisual?.SendHandlerMessage(new PauseMsg(IsHostPaused()));
    }

    private void EnsureSpritesForCurrentKind()
    {
        var kind = ParseKind(ParticleType);
        if (kind is ParticleKind.Off or ParticleKind.Custom)
        {
            _spriteLoadGeneration++;
            _loadedSpriteKind = ParticleKind.Off;
            return;
        }

        if (_customVisual == null || _loadedSpriteKind == kind)
            return;

        _loadedSpriteKind = kind;
        int gen = ++_spriteLoadGeneration;

        _ = Task.Run(() =>
        {
            List<SKBitmap>? loaded = null;
            try
            {
                loaded = MinecraftParticleSpriteCache.LoadSpritesForMode(kind);
            }
            catch
            {
                loaded = null;
            }

            if (loaded == null || loaded.Count == 0)
                return;

            Dispatcher.UIThread.Post(() =>
            {
                if (_spriteLoadGeneration != gen || _customVisual == null || ParseKind(ParticleType) != kind)
                {
                    DisposeBitmapList(loaded);
                    return;
                }

                _customVisual.SendHandlerMessage(new SpriteBatchMsg(kind, loaded.ToArray()));
            });
        });
    }

    private static void DisposeBitmapList(List<SKBitmap> list)
    {
        foreach (var b in list)
        {
            try { b.Dispose(); } catch { }
        }
    }

    private void RefreshCustomBitmapCache()
    {
        var kind = ParseKind(ParticleType);
        if (kind != ParticleKind.Custom)
        {
            if (!string.IsNullOrEmpty(_loadedImagePath) || _pendingCustomBitmap != null)
            {
                DisposePendingBitmap();
                _loadedImagePath = string.Empty;
                _customBitmapDirty = true;
            }
            return;
        }

        var path = (CustomImagePath ?? string.Empty).Trim();
        if (string.Equals(path, _loadedImagePath, StringComparison.OrdinalIgnoreCase) && !_customBitmapDirty)
            return;

        DisposePendingBitmap();
        _loadedImagePath = path;
        _customBitmapDirty = true;

        if (!string.IsNullOrEmpty(path) && File.Exists(path))
        {
            _pendingCustomBitmap = LoadScaledCustomBitmap(path);
        }
    }

    private void DisposePendingBitmap()
    {
        try { _pendingCustomBitmap?.Dispose(); } catch { }
        _pendingCustomBitmap = null;
    }

    private void PushStateToHandler()
    {
        if (_customVisual == null) return;

        var kind = ParseKind(ParticleType);
        SKBitmap? bitmapTransfer = null;
        bool hasBitmapUpdate = _customBitmapDirty;
        if (hasBitmapUpdate)
        {
            bitmapTransfer = _pendingCustomBitmap;
            _pendingCustomBitmap = null;
            _customBitmapDirty = false;
        }

        var msg = new ConfigMsg(
            kind,
            LeafPalette ?? "Autumn",
            Math.Clamp(ParticleCount, 0, MaxParticles),
            (float)Math.Clamp(ParticleSpeed, 0.2, 3.0),
            (float)Math.Clamp(ParticleWind, -2.0, 2.0),
            (float)Math.Clamp(ParticleSize, 0.3, 2.5),
            (float)Math.Clamp(ParticleOpacity, 0.05, 1.0),
            (float)Bounds.Width,
            (float)Bounds.Height,
            IsHostPaused(),
            SnowPhysicsEnabled && kind == ParticleKind.Snow,
            ActivePageKey ?? "Profiles",
            IsModalOccluded,
            hasBitmapUpdate,
            bitmapTransfer);

        _customVisual.SendHandlerMessage(msg);
    }

    private static ParticleKind ParseKind(string? type) => (type ?? "").Trim().ToLowerInvariant() switch
    {
        "snow" => ParticleKind.Snow,
        "leaves" => ParticleKind.Leaves,
        "sakura" => ParticleKind.Sakura,
        "custom" => ParticleKind.Custom,
        _ => ParticleKind.Off
    };

    private static SKBitmap? LoadScaledCustomBitmap(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            using var decoded = Bitmap.DecodeToWidth(fs, 64, BitmapInterpolationMode.HighQuality);
            int w = decoded.PixelSize.Width;
            int h = decoded.PixelSize.Height;
            if (w <= 0 || h <= 0) return null;

            SKColorType colorType = decoded.Format == PixelFormats.Rgba8888
                ? SKColorType.Rgba8888
                : SKColorType.Bgra8888;
            SKAlphaType alphaType = decoded.AlphaFormat switch
            {
                AlphaFormat.Unpremul => SKAlphaType.Unpremul,
                AlphaFormat.Opaque => SKAlphaType.Opaque,
                _ => SKAlphaType.Premul
            };

            var info = new SKImageInfo(w, h, colorType, alphaType);
            var skBmp = new SKBitmap(info);
            try
            {
                decoded.CopyPixels(new PixelRect(0, 0, w, h), skBmp.GetPixels(), skBmp.ByteCount, skBmp.RowBytes);
                return skBmp;
            }
            catch
            {
                skBmp.Dispose();
                return null;
            }
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Lock-protected double-buffered bridge between UI thread and render thread.
    /// Eliminates per-frame and per-pointer-move heap allocations.
    /// </summary>
    private sealed class SharedPhysicsBridge
    {
        private readonly object _snapshotLock = new();
        private ColliderSnapshot[] _buffer = new ColliderSnapshot[64];
        private int _count;
        private int _version;

        private readonly object _pointerLock = new();
        private float _cursorX;
        private float _cursorY;
        private bool _cursorActive;
        private int _cursorVersion;

        private readonly object _persistLock = new();
        private SnowAccumulationData? _latestPersistence;

        public void PublishSnapshots(List<ColliderSnapshot> list)
        {
            lock (_snapshotLock)
            {
                int needed = list.Count;
                if (_buffer.Length < needed)
                {
                    _buffer = new ColliderSnapshot[Math.Max(needed, _buffer.Length * 2)];
                }
                for (int i = 0; i < needed; i++)
                {
                    _buffer[i] = list[i];
                }
                _count = needed;
                _version++;
            }
        }

        public bool TryCopySnapshots(ref ColliderSnapshot[] target, out int count, ref int lastVersion)
        {
            lock (_snapshotLock)
            {
                if (_version == lastVersion)
                {
                    count = 0;
                    return false;
                }

                lastVersion = _version;
                count = _count;
                if (target.Length < count)
                {
                    target = new ColliderSnapshot[Math.Max(count, target.Length * 2)];
                }
                if (count > 0)
                {
                    Array.Copy(_buffer, 0, target, 0, count);
                }
                return true;
            }
        }

        public void PublishPointer(float x, float y, bool active)
        {
            lock (_pointerLock)
            {
                _cursorX = x;
                _cursorY = y;
                _cursorActive = active;
                _cursorVersion++;
            }
        }

        public void ClearPointer()
        {
            lock (_pointerLock)
            {
                if (!_cursorActive) return;
                _cursorActive = false;
                _cursorVersion++;
            }
        }

        public bool TryReadPointer(ref int lastVersion, out float x, out float y, out bool active)
        {
            lock (_pointerLock)
            {
                if (_cursorVersion == lastVersion)
                {
                    x = _cursorX;
                    y = _cursorY;
                    active = _cursorActive;
                    return false;
                }

                lastVersion = _cursorVersion;
                x = _cursorX;
                y = _cursorY;
                active = _cursorActive;
                return true;
            }
        }

        public void SetLatestPersistence(SnowAccumulationData data)
        {
            lock (_persistLock)
            {
                _latestPersistence = data;
            }
        }

        public bool HasLatestPersistence()
        {
            lock (_persistLock)
            {
                return _latestPersistence != null;
            }
        }

        public SnowAccumulationData? TakeLatestPersistenceCopy()
        {
            lock (_persistLock)
            {
                return _latestPersistence;
            }
        }

        public void ResetLatestPersistence()
        {
            lock (_persistLock)
            {
                _latestPersistence = null;
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ParticleItem
    {
        public float X;
        public float Y;
        public float PrevY;
        public float Vx;
        public float Vy;
        public float SpeedScale;
        public float SizeScale;
        public float Phase;
        public float SwayFreq;
        public float SwayAmp;
        public float Angle;
        public float SpinRate;
        public float AlphaScale;
        public float Cooldown;
        public byte PaletteIndex;
        public byte SpriteIndex;
        public bool IsDynamic;
        public bool IsActive;
    }

    private sealed class ConfigMsg
    {
        public readonly ParticleKind Kind;
        public readonly string LeafPalette;
        public readonly int Count;
        public readonly float Speed;
        public readonly float Wind;
        public readonly float Size;
        public readonly float Opacity;
        public readonly float Width;
        public readonly float Height;
        public readonly bool Paused;
        public readonly bool SnowPhysicsEnabled;
        public readonly string ActivePageKey;
        public readonly bool IsModalOccluded;
        public readonly bool HasBitmapUpdate;
        public readonly SKBitmap? CustomBitmap;

        public ConfigMsg(
            ParticleKind kind,
            string leafPalette,
            int count,
            float speed,
            float wind,
            float size,
            float opacity,
            float width,
            float height,
            bool paused,
            bool snowPhysicsEnabled,
            string activePageKey,
            bool isModalOccluded,
            bool hasBitmapUpdate,
            SKBitmap? customBitmap)
        {
            Kind = kind;
            LeafPalette = leafPalette;
            Count = count;
            Speed = speed;
            Wind = wind;
            Size = size;
            Opacity = opacity;
            Width = width;
            Height = height;
            Paused = paused;
            SnowPhysicsEnabled = snowPhysicsEnabled;
            ActivePageKey = activePageKey;
            IsModalOccluded = isModalOccluded;
            HasBitmapUpdate = hasBitmapUpdate;
            CustomBitmap = customBitmap;
        }
    }

    private sealed class SpriteBatchMsg
    {
        public readonly ParticleKind Kind;
        public readonly SKBitmap[] Bitmaps;

        public SpriteBatchMsg(ParticleKind kind, SKBitmap[] bitmaps)
        {
            Kind = kind;
            Bitmaps = bitmaps;
        }
    }

    private sealed class ViewportSizeMsg
    {
        public readonly float Width;
        public readonly float Height;

        public ViewportSizeMsg(float width, float height)
        {
            Width = width;
            Height = height;
        }
    }

    private sealed class PauseMsg
    {
        public readonly bool Paused;
        public PauseMsg(bool paused) => Paused = paused;
    }

    private sealed class LoadSnowMsg
    {
        public readonly SnowAccumulationData Data;
        public LoadSnowMsg(SnowAccumulationData data) => Data = data;
    }

    private sealed class ClearSnowMsg
    {
        public static readonly ClearSnowMsg Instance = new();
        private ClearSnowMsg() { }
    }

    private sealed class FlushSnowMsg
    {
        public readonly ManualResetEventSlim? Signal;
        public FlushSnowMsg(ManualResetEventSlim? signal = null) => Signal = signal;
    }

    private sealed class ShutdownMsg
    {
        public static readonly ShutdownMsg Instance = new();
        private ShutdownMsg() { }
    }

    private sealed class DriftSurface
    {
        public string PageKey = string.Empty;
        public string SurfaceId = string.Empty;
        public float[]? Heights;
        public byte[]? PendingSavedBytes;
        public int CellCount;
        public float Width;
        public float Left;
        public float Top;
        public float Right;
        public float Bottom;
        public float PrevLeft;
        public float PrevTop;
        public float PrevRight;
        public float PrevBottom;
        public float PrevScrollX;
        public float PrevScrollY;
        public float ClipLeft;
        public float ClipTop;
        public float ClipRight;
        public float ClipBottom;
        public float CornerRadiusLeft;
        public float CornerRadiusRight;
        public bool HasClipRect;
        public bool IsVisibleInFrame;
        public bool HasBeenPositioned;
        public bool VisitedThisFrame;
        public bool NeedsRelaxation;
        public float MaxHeight;

        public bool HasSnow => MaxHeight > 0.25f || (PendingSavedBytes is { Length: > 0 });
    }

    private sealed class ParticleVisualHandler : CompositionCustomVisualHandler
    {
        private const float CellWidthPx = 4.0f;
        private const float MaxDriftHeightPx = 10.0f;
        private const float MaxSlopePerCell = 0.85f;
        private const float AirwakeRadiusPx = 82.0f;
        private const float BrushRadiusPx = 15.0f;

        private static readonly SKSamplingOptions NearestSampling = new(SKFilterMode.Nearest, SKMipmapMode.None);

        private static readonly SKColor[] SnowColors =
        [
            new SKColor(255, 255, 255),
            new SKColor(246, 250, 255),
            new SKColor(255, 255, 255),
            new SKColor(240, 247, 255),
            new SKColor(255, 255, 255)
        ];

        private static readonly SKColor[] LeafSpringColors =
        [
            new SKColor(124, 212, 92),
            new SKColor(92, 186, 80),
            new SKColor(152, 224, 108),
            new SKColor(174, 232, 118),
            new SKColor(110, 198, 122)
        ];

        private static readonly SKColor[] LeafGoldenColors =
        [
            new SKColor(242, 190, 26),
            new SKColor(250, 168, 30),
            new SKColor(255, 214, 48),
            new SKColor(226, 134, 22),
            new SKColor(255, 230, 96)
        ];

        private static readonly SKColor[] LeafAutumnColors =
        [
            new SKColor(242, 100, 26),
            new SKColor(228, 52, 52),
            new SKColor(224, 130, 24),
            new SKColor(196, 94, 24),
            new SKColor(235, 48, 88)
        ];

        private static readonly SKColor[] LeafForestColors =
        [
            new SKColor(94, 188, 82),
            new SKColor(174, 212, 72),
            new SKColor(246, 170, 28),
            new SKColor(236, 100, 24),
            new SKColor(208, 46, 46)
        ];

        private readonly SharedPhysicsBridge _bridge;
        private readonly ParticleItem[] _items = new ParticleItem[TotalParticleSlots];
        private readonly Random _rng = new(0x5EED42);

        private ColliderSnapshot[] _localSnapshots = new ColliderSnapshot[64];
        private int _localSnapshotCount;
        private int _lastSnapshotVersion = -1;

        private readonly Dictionary<string, Dictionary<string, DriftSurface>> _driftStore = new(StringComparer.Ordinal);
        private readonly List<DriftSurface> _visibleSurfaces = new(64);
        private readonly List<DriftSurface> _prevVisibleSurfaces = new(64);

        private ParticleKind _kind = ParticleKind.Off;
        private ParticleKind _spriteKind = ParticleKind.Off;
        private string _leafPaletteKey = "autumn";
        private int _count;
        private float _speed = 1.0f;
        private float _wind = 0.25f;
        private float _size = 1.0f;
        private float _opacity = 0.65f;
        private float _width;
        private float _height;
        private bool _isRunning;
        private bool _isPaused;
        private bool _tickScheduled;
        private bool _seeded;
        private bool _snowPhysicsEnabled;
        private bool _isModalOccluded;
        private string _activePageKey = "Profiles";
        private long _lastTicks;
        private float _elapsedSec;

        // Cursor state (render-thread)
        private int _lastCursorVersion = -1;
        private bool _cursorActive;
        private float _cursorX;
        private float _cursorY;
        private float _prevCursorX;
        private float _prevCursorY;
        private float _cursorVx;
        private float _cursorVy;
        private bool _hasPrevCursor;

        // Dynamic flake ring cursor
        private int _nextDynamicSlot = MaxParticles;

        // Persistence state
        private bool _persistenceDirty;
        private long _lastSaveTicks;
        private int _pendingSaveInFlight;

        private SKPaint? _spritePaint;
        private SKPaint? _driftPaint;
        private SKImage[]? _spriteImages;
        private SKBitmap[]? _spriteBitmaps;
        private SKColorFilter[]? _leafPaletteFilters;
        private SKBitmap? _customBitmap;
        private SKImage? _customImage;

        public ParticleVisualHandler(SharedPhysicsBridge bridge)
        {
            _bridge = bridge;
            _lastSaveTicks = Stopwatch.GetTimestamp();
        }

        public override void OnMessage(object message)
        {
            switch (message)
            {
                case ConfigMsg cfg:
                    ApplyConfig(cfg);
                    break;
                case SpriteBatchMsg batch:
                    ApplySpriteBatch(batch);
                    break;
                case ViewportSizeMsg vp:
                    UpdateViewportSize(vp.Width, vp.Height);
                    break;
                case PauseMsg p:
                    _isPaused = p.Paused;
                    if (_isRunning && !_isPaused)
                        EnsureAnimationScheduled();
                    break;
                case LoadSnowMsg load:
                    ApplyLoadedSnowData(load.Data);
                    break;
                case ClearSnowMsg:
                    ClearAllDrifts();
                    break;
                case FlushSnowMsg flush:
                    FlushPersistenceNow(syncToDisk: false);
                    try { flush.Signal?.Set(); } catch { }
                    break;
                case ShutdownMsg:
                    FlushPersistenceNow(syncToDisk: true);
                    _isRunning = false;
                    _tickScheduled = false;
                    ReleaseAllSnowPhysicsMemory();
                    ReleaseRenderResources();
                    break;
            }
        }

        private void ApplyConfig(ConfigMsg cfg)
        {
            if (cfg.HasBitmapUpdate)
            {
                ReleaseCustomImageOnly();
                _customBitmap = cfg.CustomBitmap;
                if (_customBitmap != null)
                {
                    _customImage = SKImage.FromBitmap(_customBitmap);
                }
            }

            if (cfg.Width > 0 && cfg.Height > 0)
            {
                _width = cfg.Width;
                _height = cfg.Height;
            }

            bool kindChanged = _kind != cfg.Kind;
            bool wasPhysicsActive = _snowPhysicsEnabled && _kind == ParticleKind.Snow;
            bool nowPhysicsActive = cfg.SnowPhysicsEnabled && cfg.Kind == ParticleKind.Snow;

            if (wasPhysicsActive && !nowPhysicsActive)
            {
                FlushPersistenceNow(syncToDisk: true);
                ReleaseAllSnowPhysicsMemory();
            }

            _kind = cfg.Kind;
            _count = Math.Clamp(cfg.Count, 0, MaxParticles);
            _speed = cfg.Speed;
            _wind = cfg.Wind;
            _size = cfg.Size;
            _opacity = cfg.Opacity;
            _isPaused = cfg.Paused;
            _snowPhysicsEnabled = cfg.SnowPhysicsEnabled;
            _activePageKey = cfg.ActivePageKey;
            _isModalOccluded = cfg.IsModalOccluded;

            string normalizedPalette = (cfg.LeafPalette ?? "Autumn").Trim().ToLowerInvariant();
            if (kindChanged && _spriteKind != _kind)
            {
                ReleaseEffectSpritesOnly();
            }

            if (_kind == ParticleKind.Leaves)
            {
                if (_leafPaletteFilters == null || !string.Equals(_leafPaletteKey, normalizedPalette, StringComparison.Ordinal))
                {
                    _leafPaletteKey = normalizedPalette;
                    RebuildLeafPaletteFilters(ResolveLeafColors(normalizedPalette));
                }
            }
            else if (_leafPaletteFilters != null)
            {
                DisposeLeafFilters();
            }

            if (_kind == ParticleKind.Off || _count <= 0)
            {
                _isRunning = false;
                _tickScheduled = false;
                ReleaseRenderResources();
                Invalidate();
                return;
            }

            EnsurePaint();
            if (!_seeded || kindChanged)
            {
                SeedAllParticles();
            }

            _isRunning = true;
            _lastTicks = Stopwatch.GetTimestamp();
            if (!_isPaused)
                EnsureAnimationScheduled();
            Invalidate();
        }

        private void ApplySpriteBatch(SpriteBatchMsg batch)
        {
            if (batch.Kind != _kind)
            {
                foreach (var b in batch.Bitmaps)
                {
                    try { b.Dispose(); } catch { }
                }
                return;
            }

            ReleaseEffectSpritesOnly();
            _spriteKind = batch.Kind;
            _spriteBitmaps = batch.Bitmaps;
            _spriteImages = new SKImage[batch.Bitmaps.Length];
            for (int i = 0; i < batch.Bitmaps.Length; i++)
            {
                _spriteImages[i] = SKImage.FromBitmap(batch.Bitmaps[i]);
            }

            Invalidate();
        }

        private void UpdateViewportSize(float w, float h)
        {
            if (w <= 0 || h <= 0) return;
            float prevW = _width;
            float prevH = _height;
            _width = w;
            _height = h;

            if (!_seeded && _kind != ParticleKind.Off && _count > 0)
            {
                SeedAllParticles();
            }
            else if (prevW > 0 && prevH > 0)
            {
                float sx = w / prevW;
                float sy = h / prevH;
                for (int i = 0; i < TotalParticleSlots; i++)
                {
                    if (!_items[i].IsActive && i >= MaxParticles) continue;
                    _items[i].X *= sx;
                    _items[i].Y *= sy;
                    _items[i].PrevY *= sy;
                }
            }
        }

        private void EnsureAnimationScheduled()
        {
            if (!_isRunning || _isPaused || _tickScheduled) return;
            _tickScheduled = true;
            _lastTicks = Stopwatch.GetTimestamp();
            RegisterForNextAnimationFrameUpdate();
        }

        public override void OnAnimationFrameUpdate()
        {
            _tickScheduled = false;
            if (!_isRunning || _isPaused || _count <= 0)
                return;

            long now = Stopwatch.GetTimestamp();
            double dtRaw = Stopwatch.GetElapsedTime(_lastTicks, now).TotalSeconds;
            _lastTicks = now;

            float dt = (float)Math.Clamp(dtRaw, 0.001, 0.05);
            _elapsedSec += dt;

            if (_width > 0 && _height > 0)
            {
                if (_kind == ParticleKind.Snow && _snowPhysicsEnabled)
                {
                    StepSnowWithPhysics(dt, now);
                }
                else
                {
                    StepParticlesSimple(dt);
                }
                Invalidate();
            }

            _tickScheduled = true;
            RegisterForNextAnimationFrameUpdate();
        }

        public override void OnRender(ImmediateDrawingContext drawingContext)
        {
            if (!_isRunning || _isPaused || _count <= 0 || _width <= 0 || _height <= 0)
                return;

            if (drawingContext.TryGetFeature(typeof(ISkiaSharpApiLeaseFeature)) is not ISkiaSharpApiLeaseFeature leaseFeature)
                return;

            using var lease = leaseFeature.Lease();
            var canvas = lease?.SkCanvas;
            if (canvas == null) return;

            EnsurePaint();
            if (_spritePaint == null) return;

            if (_kind == ParticleKind.Snow && _snowPhysicsEnabled && !_isModalOccluded)
            {
                RenderSnowDrifts(canvas);
            }

            if (_kind == ParticleKind.Custom)
            {
                RenderCustomImages(canvas);
            }
            else if (_spriteImages is { Length: > 0 } && _spriteKind == _kind)
            {
                RenderMinecraftSprites(canvas, _spriteImages);
            }
        }

        private void StepParticlesSimple(float dt)
        {
            float baseFall = _kind switch
            {
                ParticleKind.Snow => 44.0f,
                ParticleKind.Sakura => 52.0f,
                ParticleKind.Leaves => 58.0f,
                ParticleKind.Custom => 54.0f,
                _ => 48.0f
            };

            float windPx = _wind * 65.0f;
            float margin = 42.0f;

            for (int i = 0; i < _count; i++)
            {
                ref var p = ref _items[i];
                float vy = baseFall * _speed * p.SpeedScale;
                float sway = MathF.Sin(_elapsedSec * p.SwayFreq + p.Phase) * p.SwayAmp * (_kind == ParticleKind.Snow ? 0.6f : 1.0f);
                float vx = windPx * p.SpeedScale + sway;

                p.X += vx * dt;
                p.Y += vy * dt;
                p.Angle += p.SpinRate * dt;

                if (p.Y > _height + margin)
                {
                    p.Y = -margin * (0.6f + (float)_rng.NextDouble() * 0.8f);
                    p.X = (float)_rng.NextDouble() * _width;
                }

                if (p.X < -margin)
                    p.X = _width + margin;
                else if (p.X > _width + margin)
                    p.X = -margin;
            }
        }

        private void StepSnowWithPhysics(float dt, long nowTicks)
        {
            SyncSurfacesFromBridge();
            UpdateCursorKinematics(dt);

            if (!_isModalOccluded && _cursorActive && _hasPrevCursor)
            {
                float moveDistSq = (_cursorX - _prevCursorX) * (_cursorX - _prevCursorX)
                                 + (_cursorY - _prevCursorY) * (_cursorY - _prevCursorY);
                if (moveDistSq > 0.36f)
                {
                    BrushDriftsAlongCursorSegment(_prevCursorX, _prevCursorY, _cursorX, _cursorY);
                }
            }

            float baseFall = 44.0f;
            float windPx = _wind * 65.0f;
            float margin = 42.0f;
            float drag = MathF.Exp(-3.8f * dt);
            bool hasCursorWake = _cursorActive && (_cursorVx * _cursorVx + _cursorVy * _cursorVy) > 400f;

            // Step ambient flakes
            for (int i = 0; i < _count; i++)
            {
                StepSingleSnowflake(ref _items[i], dt, baseFall, windPx, margin, drag, hasCursorWake);
            }

            // Step dynamic brushed/shed snowflakes
            for (int i = MaxParticles; i < TotalParticleSlots; i++)
            {
                if (!_items[i].IsActive) continue;
                StepSingleSnowflake(ref _items[i], dt, baseFall * 1.35f, windPx * 0.75f, margin, drag, hasCursorWake);
            }

            // Relax slopes on modified visible surfaces
            for (int i = 0; i < _visibleSurfaces.Count; i++)
            {
                var s = _visibleSurfaces[i];
                if (s.NeedsRelaxation)
                {
                    RelaxSurface(s);
                }
            }

            // Debounced disk persistence (~2.5s)
            if (_persistenceDirty && Stopwatch.GetElapsedTime(_lastSaveTicks, nowTicks).TotalSeconds >= 2.5)
            {
                TriggerBackgroundPersistenceSave(nowTicks);
            }
        }

        private void StepSingleSnowflake(
            ref ParticleItem p,
            float dt,
            float baseFall,
            float windPx,
            float margin,
            float drag,
            bool hasCursorWake)
        {
            p.PrevY = p.Y;
            if (p.Cooldown > 0f)
                p.Cooldown = MathF.Max(0f, p.Cooldown - dt);

            // Air drag on extra impulse velocity
            p.Vx *= drag;
            p.Vy *= drag;

            // Cursor airwake
            if (hasCursorWake)
            {
                float dx = p.X - _cursorX;
                float dy = p.Y - _cursorY;
                float distSq = dx * dx + dy * dy;
                if (distSq < AirwakeRadiusPx * AirwakeRadiusPx && distSq > 1f)
                {
                    float dist = MathF.Sqrt(distSq);
                    float w = 1.0f - (dist / AirwakeRadiusPx);
                    float w2 = w * w;
                    // Push along cursor velocity + swirl outward
                    float invD = 1.0f / dist;
                    float radialX = dx * invD;
                    float radialY = dy * invD;
                    // Tangential swirl sign from cross product
                    float cross = radialX * _cursorVy - radialY * _cursorVx;
                    float swirlSign = cross >= 0f ? 1f : -1f;
                    float swirlMag = MathF.Min(260f, MathF.Sqrt(_cursorVx * _cursorVx + _cursorVy * _cursorVy) * 0.35f);

                    p.Vx += (_cursorVx * 0.55f * w2 + radialX * 95f * w2 - radialY * swirlSign * swirlMag * w2) * dt * 8.5f;
                    p.Vy += (_cursorVy * 0.42f * w2 + radialY * 70f * w2 + radialX * swirlSign * swirlMag * w2) * dt * 8.5f;

                    p.Vx = Math.Clamp(p.Vx, -460f, 460f);
                    p.Vy = Math.Clamp(p.Vy, -360f, 420f);
                }
            }

            float ambientVy = baseFall * _speed * p.SpeedScale;
            float sway = MathF.Sin(_elapsedSec * p.SwayFreq + p.Phase) * p.SwayAmp * 0.6f;
            float ambientVx = windPx * p.SpeedScale + sway;

            float totalVx = ambientVx + p.Vx;
            float totalVy = ambientVy + p.Vy;

            p.X += totalVx * dt;
            p.Y += totalVy * dt;

            // Surface landing collision
            if (!_isModalOccluded && p.Cooldown <= 0f && totalVy > 4f && _visibleSurfaces.Count > 0)
            {
                if (TryCollideAndDeposit(ref p))
                {
                    if (p.IsDynamic)
                    {
                        p.IsActive = false;
                        return;
                    }
                    RespawnAmbientTop(ref p, margin);
                    return;
                }
            }

            if (p.Y > _height + margin)
            {
                if (p.IsDynamic)
                {
                    p.IsActive = false;
                    return;
                }
                RespawnAmbientTop(ref p, margin);
            }
            else if (p.Y < -margin * 2.5f && p.IsDynamic)
            {
                p.IsActive = false;
                return;
            }

            if (p.X < -margin)
            {
                if (p.IsDynamic) p.IsActive = false;
                else p.X = _width + margin;
            }
            else if (p.X > _width + margin)
            {
                if (p.IsDynamic) p.IsActive = false;
                else p.X = -margin;
            }
        }

        private void RespawnAmbientTop(ref ParticleItem p, float margin)
        {
            p.Y = -margin * (0.6f + (float)_rng.NextDouble() * 0.8f);
            p.PrevY = p.Y;
            p.X = (float)_rng.NextDouble() * MathF.Max(1f, _width);
            p.Vx = 0f;
            p.Vy = 0f;
            p.Cooldown = 0f;
        }

        private bool TryCollideAndDeposit(ref ParticleItem p)
        {
            for (int i = 0; i < _visibleSurfaces.Count; i++)
            {
                var s = _visibleSurfaces[i];
                float visLeft = s.Left + 2f;
                float visRight = s.Right - 2f;
                if (s.HasClipRect)
                {
                    visLeft = MathF.Max(visLeft, s.ClipLeft);
                    visRight = MathF.Min(visRight, s.ClipRight);
                    if (s.Top < s.ClipTop - 1.5f || s.Top > s.ClipBottom + 1.5f)
                        continue;
                }

                if (p.X < visLeft || p.X > visRight)
                    continue;

                if (s.CellCount <= 0)
                    continue;

                int cell = Math.Clamp((int)((p.X - s.Left) / CellWidthPx), 0, s.CellCount - 1);
                float curDriftH = s.Heights != null ? s.Heights[cell] : 0f;
                float hitY = s.Top - curDriftH + 1.5f;

                if (p.PrevY <= s.Top + 3.0f && p.Y >= hitY)
                {
                    float maxCellH = GetMaxHeightForCell(s, cell);
                    if (curDriftH < maxCellH)
                    {
                        EnsureHeightsAllocated(s);
                        float add = (0.95f + 0.35f * p.SizeScale) * Math.Clamp(_size, 0.5f, 1.8f);
                        s.Heights![cell] = MathF.Min(maxCellH, curDriftH + add);
                        if (s.Heights[cell] > s.MaxHeight)
                            s.MaxHeight = s.Heights[cell];
                        s.NeedsRelaxation = true;
                        RelaxLocal(s, cell);
                        _persistenceDirty = true;
                        return true;
                    }

                    // Drift cell is full: check adjacent cell or gently slide off
                    int alt = -1;
                    if (cell > 0 && s.Heights![cell - 1] < GetMaxHeightForCell(s, cell - 1) - 0.3f)
                        alt = cell - 1;
                    else if (cell < s.CellCount - 1 && s.Heights![cell + 1] < GetMaxHeightForCell(s, cell + 1) - 0.3f)
                        alt = cell + 1;

                    if (alt >= 0)
                    {
                        EnsureHeightsAllocated(s);
                        float maxAlt = GetMaxHeightForCell(s, alt);
                        s.Heights![alt] = MathF.Min(maxAlt, s.Heights[alt] + 0.85f);
                        if (s.Heights[alt] > s.MaxHeight)
                            s.MaxHeight = s.Heights[alt];
                        s.NeedsRelaxation = true;
                        RelaxLocal(s, alt);
                        _persistenceDirty = true;
                        return true;
                    }

                    // Surface is completely saturated here: slide sideways off the pile!
                    p.Vx += (p.X < (s.Left + s.Right) * 0.5f ? -1f : 1f) * 35f;
                    p.Cooldown = 0.08f;
                    return false;
                }
            }

            return false;
        }

        private void BrushDriftsAlongCursorSegment(float x0, float y0, float x1, float y1)
        {
            float segMinX = MathF.Min(x0, x1) - BrushRadiusPx;
            float segMaxX = MathF.Max(x0, x1) + BrushRadiusPx;
            float segMinY = MathF.Min(y0, y1) - BrushRadiusPx;
            float segMaxY = MathF.Max(y0, y1) + BrushRadiusPx;

            float dx = x1 - x0;
            float dy = y1 - y0;
            float segLen = MathF.Sqrt(dx * dx + dy * dy);
            int steps = Math.Clamp((int)MathF.Ceiling(segLen / 8.0f), 1, 16);

            for (int i = 0; i < _visibleSurfaces.Count; i++)
            {
                var s = _visibleSurfaces[i];
                if (s.Heights == null || s.MaxHeight <= 0.25f)
                    continue;

                if (s.Right < segMinX || s.Left > segMaxX)
                    continue;
                if (s.Top + 6f < segMinY || s.Top - MaxDriftHeightPx - 6f > segMaxY)
                    continue;

                bool modified = false;
                float removedPool = 0f;
                float lastHitX = x1;
                float lastHitY = s.Top;

                for (int step = 0; step <= steps; step++)
                {
                    float t = steps == 0 ? 1f : (float)step / steps;
                    float px = x0 + dx * t;
                    float py = y0 + dy * t;

                    if (py < s.Top - s.MaxHeight - 8f || py > s.Top + 7f)
                        continue;
                    if (px < s.Left - BrushRadiusPx || px > s.Right + BrushRadiusPx)
                        continue;

                    int cStart = Math.Clamp((int)MathF.Floor((px - BrushRadiusPx - s.Left) / CellWidthPx), 0, s.CellCount - 1);
                    int cEnd = Math.Clamp((int)MathF.Ceiling((px + BrushRadiusPx - s.Left) / CellWidthPx), 0, s.CellCount - 1);

                    for (int c = cStart; c <= cEnd; c++)
                    {
                        float h = s.Heights[c];
                        if (h <= 0.15f) continue;

                        float cellCenterX = s.Left + (c + 0.5f) * CellWidthPx;
                        float cellTopY = s.Top - h * 0.5f;
                        float distX = MathF.Abs(cellCenterX - px);
                        float distY = MathF.Abs(cellTopY - py);
                        float distSq = distX * distX + distY * distY;
                        if (distSq > BrushRadiusPx * BrushRadiusPx) continue;

                        float falloff = 1.0f - MathF.Sqrt(distSq) / BrushRadiusPx;
                        float remove = MathF.Min(h, h * (0.45f + 0.50f * falloff));
                        if (remove > 0.05f)
                        {
                            s.Heights[c] = h - remove;
                            removedPool += remove;
                            lastHitX = cellCenterX;
                            lastHitY = s.Top - s.Heights[c];
                            modified = true;

                            if (removedPool >= 1.1f)
                            {
                                removedPool -= 1.1f;
                                SpawnDynamicSnowflake(
                                    lastHitX,
                                    lastHitY - 1f,
                                    _cursorVx * 0.36f + ((float)_rng.NextDouble() - 0.5f) * 90f,
                                    MathF.Min(_cursorVy * 0.22f, 30f) - 25f - (float)_rng.NextDouble() * 45f,
                                    cooldown: 0.14f);
                            }
                        }
                    }
                }

                if (modified)
                {
                    if (removedPool >= 0.45f)
                    {
                        SpawnDynamicSnowflake(
                            lastHitX,
                            lastHitY - 1f,
                            _cursorVx * 0.32f + ((float)_rng.NextDouble() - 0.5f) * 70f,
                            -20f - (float)_rng.NextDouble() * 35f,
                            cooldown: 0.14f);
                    }
                    s.NeedsRelaxation = true;
                    RecomputeMaxHeight(s);
                    _persistenceDirty = true;
                }
            }
        }

        private void ShedSnowFromSurface(DriftSurface s, float fraction, float extraVx, float extraVy)
        {
            if (s.Heights == null || s.MaxHeight <= 0.25f)
                return;

            float accum = 0f;
            int spawned = 0;
            const int maxPerShed = 28;
            bool fullClear = fraction >= 0.99f;

            for (int c = 0; c < s.CellCount; c++)
            {
                float h = s.Heights[c];
                if (h <= 0.15f) continue;

                float take = fullClear ? h : h * fraction;
                s.Heights[c] = fullClear ? 0f : h - take;
                accum += take;

                if (accum >= 1.5f && spawned < maxPerShed)
                {
                    accum -= 1.5f;
                    float cellX = s.Left + (c + 0.5f) * CellWidthPx;
                    float cellY = s.Top - h * 0.5f;
                    SpawnDynamicSnowflake(
                        cellX,
                        cellY,
                        extraVx + ((float)_rng.NextDouble() - 0.5f) * 55f,
                        extraVy + (float)_rng.NextDouble() * 30f,
                        cooldown: 0.16f);
                    spawned++;
                }
            }

            if (fullClear)
            {
                s.MaxHeight = 0f;
                s.Heights = null;
                s.NeedsRelaxation = false;
            }
            else
            {
                s.NeedsRelaxation = true;
                RecomputeMaxHeight(s);
            }

            _persistenceDirty = true;
        }

        private void SpawnDynamicSnowflake(float x, float y, float vx, float vy, float cooldown)
        {
            if (_width <= 0 || _height <= 0) return;
            if (x < -30f || x > _width + 30f || y < -30f || y > _height + 30f) return;

            int slot = _nextDynamicSlot;
            _nextDynamicSlot++;
            if (_nextDynamicSlot >= TotalParticleSlots)
                _nextDynamicSlot = MaxParticles;

            ref var p = ref _items[slot];
            p.X = x;
            p.Y = y;
            p.PrevY = y;
            p.Vx = Math.Clamp(vx, -420f, 420f);
            p.Vy = Math.Clamp(vy, -320f, 360f);
            p.SpeedScale = 0.75f + (float)_rng.NextDouble() * 0.55f;
            p.SizeScale = 0.65f + (float)_rng.NextDouble() * 0.50f;
            p.Phase = (float)(_rng.NextDouble() * Math.PI * 2.0);
            p.SwayFreq = 1.1f + (float)_rng.NextDouble() * 1.4f;
            p.SwayAmp = 10.0f + (float)_rng.NextDouble() * 18.0f;
            p.Angle = 0f;
            p.SpinRate = 0f;
            p.AlphaScale = 0.75f + (float)_rng.NextDouble() * 0.25f;
            p.Cooldown = cooldown;
            p.PaletteIndex = (byte)_rng.Next(0, SnowColors.Length);
            p.SpriteIndex = (byte)_rng.Next(0, 12);
            p.IsDynamic = true;
            p.IsActive = true;
        }

        private void UpdateCursorKinematics(float dt)
        {
            if (_bridge.TryReadPointer(ref _lastCursorVersion, out float nx, out float ny, out bool active))
            {
                if (!active)
                {
                    _cursorActive = false;
                    _hasPrevCursor = false;
                }
                else
                {
                    if (_cursorActive && _hasPrevCursor)
                    {
                        _prevCursorX = _cursorX;
                        _prevCursorY = _cursorY;
                        float instVx = (nx - _cursorX) / dt;
                        float instVy = (ny - _cursorY) / dt;
                        _cursorVx = _cursorVx * 0.45f + Math.Clamp(instVx, -1800f, 1800f) * 0.55f;
                        _cursorVy = _cursorVy * 0.45f + Math.Clamp(instVy, -1800f, 1800f) * 0.55f;
                    }
                    else
                    {
                        _prevCursorX = nx;
                        _prevCursorY = ny;
                        _cursorVx = 0f;
                        _cursorVy = 0f;
                        _hasPrevCursor = true;
                    }

                    _cursorX = nx;
                    _cursorY = ny;
                    _cursorActive = true;
                }
            }
            else if (_cursorActive)
            {
                _prevCursorX = _cursorX;
                _prevCursorY = _cursorY;
                float decay = MathF.Exp(-14.0f * dt);
                _cursorVx *= decay;
                _cursorVy *= decay;
            }
        }

        private void SyncSurfacesFromBridge()
        {
            if (!_bridge.TryCopySnapshots(ref _localSnapshots, out int newCount, ref _lastSnapshotVersion))
                return;

            _localSnapshotCount = newCount;

            // Swap visible tracking lists
            _prevVisibleSurfaces.Clear();
            for (int i = 0; i < _visibleSurfaces.Count; i++)
            {
                var s = _visibleSurfaces[i];
                s.VisitedThisFrame = false;
                _prevVisibleSurfaces.Add(s);
            }
            _visibleSurfaces.Clear();

            for (int i = 0; i < _localSnapshotCount; i++)
            {
                ref readonly var snap = ref _localSnapshots[i];
                var s = GetOrCreateSurface(snap.PageKey, snap.SurfaceId);

                if (!snap.IsVisible)
                {
                    if (s.IsVisibleInFrame && s.HasSnow && snap.IsScopeActive && !IsVirtualizedScrollOut(s))
                    {
                        ShedSnowFromSurface(s, 1.0f, 0f, 15f);
                    }
                    s.IsVisibleInFrame = false;
                    s.HasBeenPositioned = false;
                    s.VisitedThisFrame = true;
                    continue;
                }

                float width = MathF.Max(8f, snap.Right - snap.Left);
                EnsureSurfaceWidth(s, width);

                if (s.PendingSavedBytes != null)
                {
                    UnpackBytesIntoSurface(s, s.PendingSavedBytes);
                    s.PendingSavedBytes = null;
                }

                if (s.HasBeenPositioned)
                {
                    float dx = snap.Left - s.PrevLeft;
                    float dy = snap.Top - s.PrevTop;
                    float scrollDx = snap.ScrollOffsetX - s.PrevScrollX;
                    float scrollDy = snap.ScrollOffsetY - s.PrevScrollY;

                    float layoutDx = dx + scrollDx;
                    float layoutDy = dy + scrollDy;

                    // Violent or sudden layout shift (e.g. card reorder, collapse, window resize) sheds part of snow
                    float layoutJump = MathF.Max(MathF.Abs(layoutDx), MathF.Abs(layoutDy));
                    if (s.HasSnow && (layoutJump > 14.0f || MathF.Abs(dy) > 55.0f))
                    {
                        float shakeFraction = Math.Clamp((MathF.Max(layoutJump, MathF.Abs(dy)) - 12f) / 65f, 0.25f, 0.72f);
                        ShedSnowFromSurface(s, shakeFraction, -dx * 1.4f, MathF.Max(10f, -dy * 0.5f));
                    }
                }

                s.Left = snap.Left;
                s.Top = snap.Top;
                s.Right = snap.Right;
                s.Bottom = snap.Bottom;
                s.PrevLeft = snap.Left;
                s.PrevTop = snap.Top;
                s.PrevRight = snap.Right;
                s.PrevBottom = snap.Bottom;
                s.PrevScrollX = snap.ScrollOffsetX;
                s.PrevScrollY = snap.ScrollOffsetY;
                s.ClipLeft = snap.ClipLeft;
                s.ClipTop = snap.ClipTop;
                s.ClipRight = snap.ClipRight;
                s.ClipBottom = snap.ClipBottom;
                s.HasClipRect = snap.HasClipRect;
                s.CornerRadiusLeft = snap.CornerRadiusLeft;
                s.CornerRadiusRight = snap.CornerRadiusRight;
                s.IsVisibleInFrame = true;
                s.HasBeenPositioned = true;
                s.VisitedThisFrame = true;

                _visibleSurfaces.Add(s);
            }

            // Surfaces that disappeared completely from snapshots (detached / deactivated)
            for (int i = 0; i < _prevVisibleSurfaces.Count; i++)
            {
                var s = _prevVisibleSurfaces[i];
                if (s.VisitedThisFrame) continue;

                if (s.IsVisibleInFrame && s.HasSnow && IsPageKeyActiveForShedding(s.PageKey) && !IsVirtualizedScrollOut(s))
                {
                    ShedSnowFromSurface(s, 1.0f, 0f, 15f);
                }

                s.IsVisibleInFrame = false;
                s.HasBeenPositioned = false;
            }
        }

        private bool IsPageKeyActiveForShedding(string pageKey)
        {
            if (string.Equals(pageKey, "Chrome", StringComparison.Ordinal))
                return true;
            if (string.Equals(pageKey, "LaunchBar", StringComparison.Ordinal))
                return _activePageKey is "Profiles" or "Modpacks";
            if (pageKey.StartsWith("Settings", StringComparison.Ordinal))
                return string.Equals(_activePageKey, "Settings", StringComparison.Ordinal);
            if (pageKey.StartsWith("Modpacks", StringComparison.Ordinal))
                return string.Equals(_activePageKey, "Modpacks", StringComparison.Ordinal);
            if (pageKey.StartsWith("EditInstance", StringComparison.Ordinal))
                return string.Equals(_activePageKey, "EditInstance", StringComparison.Ordinal);
            return string.Equals(pageKey, _activePageKey, StringComparison.Ordinal);
        }

        private static bool IsVirtualizedScrollOut(DriftSurface s)
        {
            if (!s.HasClipRect) return false;
            return s.Top <= s.ClipTop + 42f || s.Top >= s.ClipBottom - 42f;
        }

        private DriftSurface GetOrCreateSurface(string pageKey, string surfaceId)
        {
            if (!_driftStore.TryGetValue(pageKey, out var pageDict))
            {
                pageDict = new Dictionary<string, DriftSurface>(StringComparer.Ordinal);
                _driftStore[pageKey] = pageDict;
            }

            if (!pageDict.TryGetValue(surfaceId, out var s))
            {
                s = new DriftSurface
                {
                    PageKey = pageKey,
                    SurfaceId = surfaceId
                };
                pageDict[surfaceId] = s;
            }

            return s;
        }

        private static void EnsureSurfaceWidth(DriftSurface s, float width)
        {
            int newCells = Math.Max(2, (int)MathF.Ceiling(width / CellWidthPx));
            if (s.CellCount == newCells && MathF.Abs(s.Width - width) < 2f)
                return;

            if (s.Heights != null && s.CellCount > 0 && s.CellCount != newCells)
            {
                var resampled = new float[newCells];
                int oldLen = s.CellCount;
                for (int i = 0; i < newCells; i++)
                {
                    float srcPos = oldLen > 1 && newCells > 1
                        ? (float)i / (newCells - 1) * (oldLen - 1)
                        : 0f;
                    int lo = Math.Clamp((int)MathF.Floor(srcPos), 0, oldLen - 1);
                    int hi = Math.Clamp(lo + 1, 0, oldLen - 1);
                    float t = srcPos - lo;
                    resampled[i] = s.Heights[lo] * (1f - t) + s.Heights[hi] * t;
                }
                s.Heights = resampled;
                RecomputeMaxHeight(s);
            }

            s.Width = width;
            s.CellCount = newCells;
        }

        private static void EnsureHeightsAllocated(DriftSurface s)
        {
            if (s.Heights == null || s.Heights.Length != s.CellCount)
            {
                s.Heights = new float[Math.Max(2, s.CellCount)];
            }
        }

        private static float GetMaxHeightForCell(DriftSurface s, int cell)
        {
            int n = s.CellCount;
            if (n <= 2) return MaxDriftHeightPx * 0.6f;

            float xLeft = (cell + 0.5f) * CellWidthPx;
            float xRight = s.Width - xLeft;

            float leftCap = 1.0f;
            float crL = MathF.Max(4.0f, s.CornerRadiusLeft);
            if (xLeft < crL)
                leftCap = 0.28f + 0.72f * (xLeft / crL);

            float rightCap = 1.0f;
            float crR = MathF.Max(4.0f, s.CornerRadiusRight);
            if (xRight < crR)
                rightCap = 0.28f + 0.72f * (xRight / crR);

            return MaxDriftHeightPx * MathF.Min(leftCap, rightCap);
        }

        private static void RelaxLocal(DriftSurface s, int centerCell)
        {
            var h = s.Heights;
            if (h == null) return;
            int lo = Math.Max(0, centerCell - 5);
            int hi = Math.Min(s.CellCount - 1, centerCell + 5);

            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = lo; i < hi; i++)
                {
                    float diff = h[i] - h[i + 1];
                    if (diff > MaxSlopePerCell)
                    {
                        float move = (diff - MaxSlopePerCell) * 0.38f;
                        float cap = GetMaxHeightForCell(s, i + 1);
                        float actual = MathF.Min(move, MathF.Max(0f, cap - h[i + 1]));
                        h[i] -= actual;
                        h[i + 1] += actual;
                    }
                    else if (-diff > MaxSlopePerCell)
                    {
                        float move = (-diff - MaxSlopePerCell) * 0.38f;
                        float cap = GetMaxHeightForCell(s, i);
                        float actual = MathF.Min(move, MathF.Max(0f, cap - h[i]));
                        h[i + 1] -= actual;
                        h[i] += actual;
                    }
                }
            }
            RecomputeMaxHeight(s);
        }

        private static void RelaxSurface(DriftSurface s)
        {
            var h = s.Heights;
            if (h == null || s.CellCount < 2)
            {
                s.NeedsRelaxation = false;
                return;
            }

            bool movedAny = false;
            for (int i = 0; i < s.CellCount - 1; i++)
            {
                float diff = h[i] - h[i + 1];
                if (diff > MaxSlopePerCell)
                {
                    float move = (diff - MaxSlopePerCell) * 0.35f;
                    float cap = GetMaxHeightForCell(s, i + 1);
                    float actual = MathF.Min(move, MathF.Max(0f, cap - h[i + 1]));
                    if (actual > 0.02f)
                    {
                        h[i] -= actual;
                        h[i + 1] += actual;
                        movedAny = true;
                    }
                }
                else if (-diff > MaxSlopePerCell)
                {
                    float move = (-diff - MaxSlopePerCell) * 0.35f;
                    float cap = GetMaxHeightForCell(s, i);
                    float actual = MathF.Min(move, MathF.Max(0f, cap - h[i]));
                    if (actual > 0.02f)
                    {
                        h[i + 1] -= actual;
                        h[i] += actual;
                        movedAny = true;
                    }
                }
            }

            // Taper outer edge cells to corner caps
            if (s.CellCount > 0)
            {
                h[0] = MathF.Min(h[0], GetMaxHeightForCell(s, 0));
                h[s.CellCount - 1] = MathF.Min(h[s.CellCount - 1], GetMaxHeightForCell(s, s.CellCount - 1));
            }

            RecomputeMaxHeight(s);
            if (!movedAny)
                s.NeedsRelaxation = false;
        }

        private static void RecomputeMaxHeight(DriftSurface s)
        {
            var h = s.Heights;
            if (h == null)
            {
                s.MaxHeight = 0f;
                return;
            }
            float max = 0f;
            for (int i = 0; i < s.CellCount; i++)
            {
                if (h[i] > max) max = h[i];
            }
            s.MaxHeight = max;
        }

        private void RenderSnowDrifts(SKCanvas canvas)
        {
            if (_visibleSurfaces.Count == 0) return;

            _driftPaint ??= new SKPaint { IsAntialias = false, Style = SKPaintStyle.Fill };
            var paint = _driftPaint;

            byte alpha = (byte)Math.Clamp((int)MathF.Round((0.78f + _opacity * 0.22f) * 255f), 160, 255);
            byte shadowAlpha = (byte)Math.Clamp((int)MathF.Round(56f * (0.65f + _opacity * 0.35f)), 20, 80);

            SKColor shadowColor = new(0, 0, 0, shadowAlpha);
            SKColor iceBaseColor = new(214, 232, 250, alpha);
            SKColor bodyColor = new(244, 249, 255, alpha);
            SKColor capColor = new(255, 255, 255, alpha);

            for (int i = 0; i < _visibleSurfaces.Count; i++)
            {
                var s = _visibleSurfaces[i];
                var h = s.Heights;
                if (h == null || s.MaxHeight < 0.45f || s.CellCount <= 0)
                    continue;

                if (s.HasClipRect)
                {
                    if (s.Top < s.ClipTop - 1.5f || s.Top > s.ClipBottom + 1.5f)
                        continue;
                    if (s.Right <= s.ClipLeft || s.Left >= s.ClipRight)
                        continue;
                }

                bool clipped = false;
                if (s.HasClipRect)
                {
                    canvas.Save();
                    canvas.ClipRect(new SKRect(s.ClipLeft, s.ClipTop, s.ClipRight, s.ClipBottom));
                    clipped = true;
                }

                float baseY = MathF.Round(s.Top) + 1.5f;
                int n = s.CellCount;
                float insetLeft = s.Left + 2.0f;
                float insetRight = s.Right - 2.0f;

                int c = 0;
                while (c < n)
                {
                    float qh = MathF.Round(h[c]);
                    if (qh < 1.0f)
                    {
                        c++;
                        continue;
                    }

                    // Merge adjacent cells with identical quantized pixel height to minimize DrawRect calls
                    int spanEnd = c + 1;
                    while (spanEnd < n && MathF.Round(h[spanEnd]) == qh)
                        spanEnd++;

                    float rx0 = MathF.Max(insetLeft, s.Left + c * CellWidthPx);
                    float rx1 = MathF.Min(insetRight, s.Left + spanEnd * CellWidthPx);
                    if (rx1 > rx0)
                    {
                        float topY = baseY - qh;

                        // 1. Subtle contact shadow below drift edge
                        paint.Color = shadowColor;
                        canvas.DrawRect(rx0, baseY, rx1 - rx0, 1.5f, paint);

                        // 2. Crisp icy crust along the bottom
                        paint.Color = iceBaseColor;
                        canvas.DrawRect(rx0, baseY - MathF.Min(qh, 2.0f), rx1 - rx0, MathF.Min(qh, 2.0f), paint);

                        // 3. Main snow body
                        if (qh > 2.0f)
                        {
                            paint.Color = bodyColor;
                            canvas.DrawRect(rx0, topY, rx1 - rx0, qh - 2.0f, paint);
                        }

                        // 4. Pure bright snow highlight rim on top
                        paint.Color = capColor;
                        canvas.DrawRect(rx0, topY, rx1 - rx0, MathF.Min(qh, 1.5f), paint);
                    }

                    c = spanEnd;
                }

                if (clipped)
                {
                    canvas.Restore();
                }
            }
        }

        private void RenderMinecraftSprites(SKCanvas canvas, SKImage[] sprites)
        {
            var paint = _spritePaint!;
            float baseScale = _kind switch
            {
                ParticleKind.Snow => 10.5f * _size,
                ParticleKind.Sakura => 11.0f * _size,
                ParticleKind.Leaves => 15.5f * _size,
                _ => 12.0f * _size
            };

            bool useLeafFilters = _kind == ParticleKind.Leaves && _leafPaletteFilters is { Length: > 0 };
            bool isSnow = _kind == ParticleKind.Snow;
            int spriteCount = sprites.Length;

            int renderLimit = (isSnow && _snowPhysicsEnabled) ? TotalParticleSlots : _count;

            for (int i = 0; i < renderLimit; i++)
            {
                if (i >= _count && i < MaxParticles) continue;
                ref readonly var p = ref _items[i];
                if (i >= MaxParticles && !p.IsActive) continue;

                byte a = (byte)Math.Clamp((int)MathF.Round(_opacity * p.AlphaScale * 255f), 0, 255);
                if (a < 2) continue;

                var img = sprites[p.SpriteIndex % spriteCount];
                int maxDim = Math.Max(1, Math.Max(img.Width, img.Height));
                float scaleFactor = (baseScale * p.SizeScale) / maxDim;
                float halfW = img.Width * scaleFactor * 0.5f;
                float halfH = img.Height * scaleFactor * 0.5f;

                canvas.Save();
                canvas.Translate(p.X, p.Y);
                if (!isSnow)
                {
                    canvas.RotateRadians(p.Angle);
                }

                if (useLeafFilters)
                {
                    var filters = _leafPaletteFilters!;
                    paint.Color = SKColors.White.WithAlpha(a);
                    paint.ColorFilter = filters[p.PaletteIndex % filters.Length];
                }
                else
                {
                    paint.ColorFilter = null;
                    paint.Color = isSnow
                        ? SnowColors[p.PaletteIndex % SnowColors.Length].WithAlpha(a)
                        : SKColors.White.WithAlpha(a);
                }

                canvas.DrawImage(img, new SKRect(-halfW, -halfH, halfW, halfH), NearestSampling, paint);
                canvas.Restore();
            }

            paint.ColorFilter = null;
        }

        private void RenderCustomImages(SKCanvas canvas)
        {
            if (_customImage == null || _spritePaint == null) return;
            var paint = _spritePaint;
            paint.ColorFilter = null;

            float baseDim = 24.0f * _size;
            float aspect = _customImage.Width > 0 ? (float)_customImage.Height / _customImage.Width : 1.0f;

            for (int i = 0; i < _count; i++)
            {
                ref readonly var p = ref _items[i];
                byte a = (byte)Math.Clamp((int)MathF.Round(_opacity * p.AlphaScale * 255f), 0, 255);
                if (a < 2) continue;

                float halfW = (baseDim * p.SizeScale) * 0.5f;
                float halfH = halfW * aspect;

                canvas.Save();
                canvas.Translate(p.X, p.Y);
                canvas.RotateRadians(p.Angle);

                paint.Color = SKColors.White.WithAlpha(a);
                canvas.DrawImage(_customImage, new SKRect(-halfW, -halfH, halfW, halfH), NearestSampling, paint);

                canvas.Restore();
            }
        }

        private void SeedAllParticles()
        {
            float w = _width > 0 ? _width : 1280f;
            float h = _height > 0 ? _height : 780f;

            for (int i = 0; i < MaxParticles; i++)
            {
                ref var p = ref _items[i];
                p.X = (float)_rng.NextDouble() * w;
                p.Y = (float)_rng.NextDouble() * h;
                p.PrevY = p.Y;
                p.Vx = 0f;
                p.Vy = 0f;
                p.SpeedScale = 0.65f + (float)_rng.NextDouble() * 0.7f;
                p.SizeScale = 0.70f + (float)_rng.NextDouble() * 0.65f;
                p.Phase = (float)(_rng.NextDouble() * Math.PI * 2.0);
                p.SwayFreq = 0.8f + (float)_rng.NextDouble() * 1.4f;
                p.SwayAmp = 12.0f + (float)_rng.NextDouble() * 22.0f;
                p.Angle = (float)(_rng.NextDouble() * Math.PI * 2.0);
                p.SpinRate = (float)((_rng.NextDouble() - 0.5) * 1.6);
                p.AlphaScale = 0.55f + (float)_rng.NextDouble() * 0.45f;
                p.Cooldown = 0f;
                p.PaletteIndex = (byte)_rng.Next(0, 5);
                p.SpriteIndex = (byte)_rng.Next(0, 12);
                p.IsDynamic = false;
                p.IsActive = true;
            }

            for (int i = MaxParticles; i < TotalParticleSlots; i++)
            {
                _items[i].IsActive = false;
                _items[i].IsDynamic = true;
            }

            _seeded = true;
        }

        private void ApplyLoadedSnowData(SnowAccumulationData data)
        {
            if (data.Pages == null || data.Pages.Count == 0)
                return;

            foreach (var (pageKey, surfaces) in data.Pages)
            {
                if (string.IsNullOrWhiteSpace(pageKey) || surfaces == null) continue;
                foreach (var (surfaceId, bytes) in surfaces)
                {
                    if (string.IsNullOrWhiteSpace(surfaceId) || bytes == null || bytes.Length == 0)
                        continue;

                    var s = GetOrCreateSurface(pageKey, surfaceId);
                    if (s.CellCount > 0)
                    {
                        // Surface already visible: unpack & merge/apply if currently empty
                        if (!s.HasSnow)
                        {
                            UnpackBytesIntoSurface(s, bytes);
                        }
                    }
                    else
                    {
                        s.PendingSavedBytes = bytes;
                    }
                }
            }

            _persistenceDirty = false;
            _bridge.SetLatestPersistence(BuildPersistenceSnapshot());
            Invalidate();
        }

        private static void UnpackBytesIntoSurface(DriftSurface s, byte[] bytes)
        {
            if (bytes.Length == 0 || s.CellCount <= 0) return;
            EnsureHeightsAllocated(s);

            int n = s.CellCount;
            int srcLen = bytes.Length;
            float max = 0f;
            float scale = MaxDriftHeightPx / 255.0f;

            for (int i = 0; i < n; i++)
            {
                float srcPos = srcLen > 1 && n > 1
                    ? (float)i / (n - 1) * (srcLen - 1)
                    : 0f;
                int lo = Math.Clamp((int)MathF.Floor(srcPos), 0, srcLen - 1);
                int hi = Math.Clamp(lo + 1, 0, srcLen - 1);
                float t = srcPos - lo;
                float val = (bytes[lo] * (1f - t) + bytes[hi] * t) * scale;
                float cap = GetMaxHeightForCell(s, i);
                val = MathF.Min(cap, val);
                s.Heights![i] = val;
                if (val > max) max = val;
            }

            s.MaxHeight = max;
            s.NeedsRelaxation = max > 0.25f;
        }

        private SnowAccumulationData BuildPersistenceSnapshot()
        {
            var result = new SnowAccumulationData { Version = 1 };

            foreach (var (pageKey, pageDict) in _driftStore)
            {
                Dictionary<string, byte[]>? savedPage = null;
                foreach (var (surfaceId, s) in pageDict)
                {
                    byte[]? packed = null;
                    if (s.Heights != null && s.MaxHeight > 0.25f && s.CellCount > 0)
                    {
                        packed = new byte[s.CellCount];
                        float invScale = 255.0f / MaxDriftHeightPx;
                        bool anyNonZero = false;
                        for (int i = 0; i < s.CellCount; i++)
                        {
                            byte q = (byte)Math.Clamp((int)MathF.Round(s.Heights[i] * invScale), 0, 255);
                            packed[i] = q;
                            if (q > 0) anyNonZero = true;
                        }
                        if (!anyNonZero) packed = null;
                    }
                    else if (s.PendingSavedBytes is { Length: > 0 })
                    {
                        packed = s.PendingSavedBytes;
                    }

                    if (packed != null)
                    {
                        savedPage ??= new Dictionary<string, byte[]>(StringComparer.Ordinal);
                        savedPage[surfaceId] = packed;
                    }
                }

                if (savedPage != null && savedPage.Count > 0)
                {
                    result.Pages[pageKey] = savedPage;
                }
            }

            return result;
        }

        private void TriggerBackgroundPersistenceSave(long nowTicks)
        {
            _persistenceDirty = false;
            _lastSaveTicks = nowTicks;
            var snapshot = BuildPersistenceSnapshot();
            _bridge.SetLatestPersistence(snapshot);

            if (Interlocked.CompareExchange(ref _pendingSaveInFlight, 1, 0) != 0)
                return;

            _ = Task.Run(() =>
            {
                try
                {
                    var latest = _bridge.TakeLatestPersistenceCopy() ?? snapshot;
                    SnowAccumulationStore.Save(latest);
                }
                finally
                {
                    Interlocked.Exchange(ref _pendingSaveInFlight, 0);
                }
            });
        }

        private void FlushPersistenceNow(bool syncToDisk)
        {
            if (_driftStore.Count == 0)
                return;

            _persistenceDirty = false;
            _lastSaveTicks = Stopwatch.GetTimestamp();
            var snapshot = BuildPersistenceSnapshot();
            _bridge.SetLatestPersistence(snapshot);

            if (syncToDisk)
            {
                SnowAccumulationStore.Save(snapshot);
            }
        }

        private void ClearAllDrifts()
        {
            foreach (var pageDict in _driftStore.Values)
            {
                foreach (var s in pageDict.Values)
                {
                    s.Heights = null;
                    s.PendingSavedBytes = null;
                    s.MaxHeight = 0f;
                    s.NeedsRelaxation = false;
                }
            }
            _driftStore.Clear();

            for (int i = MaxParticles; i < TotalParticleSlots; i++)
            {
                _items[i].IsActive = false;
            }

            _persistenceDirty = false;
            _bridge.ResetLatestPersistence();
            Invalidate();
        }

        private void ReleaseAllSnowPhysicsMemory()
        {
            _visibleSurfaces.Clear();
            _prevVisibleSurfaces.Clear();
            _driftStore.Clear();
            _localSnapshotCount = 0;
            _lastSnapshotVersion = -1;
            _persistenceDirty = false;
            _cursorActive = false;
            _hasPrevCursor = false;

            for (int i = MaxParticles; i < TotalParticleSlots; i++)
            {
                _items[i].IsActive = false;
            }
        }

        private static SKColor[] ResolveLeafColors(string paletteKey) => paletteKey switch
        {
            "spring" => LeafSpringColors,
            "golden" => LeafGoldenColors,
            "forest" => LeafForestColors,
            _ => LeafAutumnColors
        };

        private void RebuildLeafPaletteFilters(SKColor[] colors)
        {
            DisposeLeafFilters();
            _leafPaletteFilters = new SKColorFilter[colors.Length];
            for (int i = 0; i < colors.Length; i++)
            {
                _leafPaletteFilters[i] = SKColorFilter.CreateBlendMode(colors[i], SKBlendMode.Modulate);
            }
        }

        private void DisposeLeafFilters()
        {
            if (_leafPaletteFilters == null) return;
            foreach (var f in _leafPaletteFilters)
            {
                try { f?.Dispose(); } catch { }
            }
            _leafPaletteFilters = null;
        }

        private void EnsurePaint()
        {
            _spritePaint ??= new SKPaint
            {
                IsAntialias = false
            };
        }

        private void ReleaseEffectSpritesOnly()
        {
            _spriteKind = ParticleKind.Off;

            if (_spriteImages != null)
            {
                foreach (var img in _spriteImages)
                {
                    try { img?.Dispose(); } catch { }
                }
                _spriteImages = null;
            }

            if (_spriteBitmaps != null)
            {
                foreach (var bmp in _spriteBitmaps)
                {
                    try { bmp?.Dispose(); } catch { }
                }
                _spriteBitmaps = null;
            }
        }

        private void ReleaseCustomImageOnly()
        {
            try { _customImage?.Dispose(); } catch { }
            _customImage = null;

            try { _customBitmap?.Dispose(); } catch { }
            _customBitmap = null;
        }

        private void ReleaseRenderResources()
        {
            if (_spritePaint != null)
            {
                _spritePaint.ColorFilter = null;
                try { _spritePaint.Dispose(); } catch { }
                _spritePaint = null;
            }

            if (_driftPaint != null)
            {
                try { _driftPaint.Dispose(); } catch { }
                _driftPaint = null;
            }

            DisposeLeafFilters();
            ReleaseEffectSpritesOnly();
            ReleaseCustomImageOnly();
        }
    }
}
