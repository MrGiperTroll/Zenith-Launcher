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
    public const int MaxDynamicSnowflakes = 320;
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

    public static readonly StyledProperty<bool> WeatherSnowEnabledProperty =
        AvaloniaProperty.Register<ParticleOverlayControl, bool>(nameof(WeatherSnowEnabled), true);

    public static readonly StyledProperty<bool> WeatherLeavesEnabledProperty =
        AvaloniaProperty.Register<ParticleOverlayControl, bool>(nameof(WeatherLeavesEnabled), true);

    public static readonly StyledProperty<bool> WeatherSakuraEnabledProperty =
        AvaloniaProperty.Register<ParticleOverlayControl, bool>(nameof(WeatherSakuraEnabled), true);

    public static readonly StyledProperty<bool> SnowBottomAccumulationEnabledProperty =
        AvaloniaProperty.Register<ParticleOverlayControl, bool>(nameof(SnowBottomAccumulationEnabled), false);

    public static readonly StyledProperty<bool> SnowCursorInteractionEnabledProperty =
        AvaloniaProperty.Register<ParticleOverlayControl, bool>(nameof(SnowCursorInteractionEnabled), true);

    public static readonly StyledProperty<bool> SnowPersistenceEnabledProperty =
        AvaloniaProperty.Register<ParticleOverlayControl, bool>(nameof(SnowPersistenceEnabled), true);

    public static readonly StyledProperty<double> SnowAccumulationRateProperty =
        AvaloniaProperty.Register<ParticleOverlayControl, double>(nameof(SnowAccumulationRate), 1.0);

    public static readonly StyledProperty<double> SnowMaxHeightProperty =
        AvaloniaProperty.Register<ParticleOverlayControl, double>(nameof(SnowMaxHeight), 11.0);

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

    public bool WeatherSnowEnabled
    {
        get => GetValue(WeatherSnowEnabledProperty);
        set => SetValue(WeatherSnowEnabledProperty, value);
    }

    public bool WeatherLeavesEnabled
    {
        get => GetValue(WeatherLeavesEnabledProperty);
        set => SetValue(WeatherLeavesEnabledProperty, value);
    }

    public bool WeatherSakuraEnabled
    {
        get => GetValue(WeatherSakuraEnabledProperty);
        set => SetValue(WeatherSakuraEnabledProperty, value);
    }

    public bool SnowBottomAccumulationEnabled
    {
        get => GetValue(SnowBottomAccumulationEnabledProperty);
        set => SetValue(SnowBottomAccumulationEnabledProperty, value);
    }

    public bool SnowCursorInteractionEnabled
    {
        get => GetValue(SnowCursorInteractionEnabledProperty);
        set => SetValue(SnowCursorInteractionEnabledProperty, value);
    }

    public bool SnowPersistenceEnabled
    {
        get => GetValue(SnowPersistenceEnabledProperty);
        set => SetValue(SnowPersistenceEnabledProperty, value);
    }

    public double SnowAccumulationRate
    {
        get => GetValue(SnowAccumulationRateProperty);
        set => SetValue(SnowAccumulationRateProperty, value);
    }

    public double SnowMaxHeight
    {
        get => GetValue(SnowMaxHeightProperty);
        set => SetValue(SnowMaxHeightProperty, value);
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

    public bool IsSnowPhysicsActive
    {
        get
        {
            if (!SnowPhysicsEnabled) return false;
            return ParseKind(ParticleType) switch
            {
                ParticleKind.Snow => WeatherSnowEnabled,
                ParticleKind.Leaves => WeatherLeavesEnabled,
                ParticleKind.Sakura => WeatherSakuraEnabled,
                _ => false
            };
        }
    }

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
    private long _contextSwitchTicks;
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
        _ = Task.Run(WeatherAccumulationStore.ClearFile);
    }

    public void FlushSnowPersistence()
    {
        if (!SnowPersistenceEnabled)
            return;

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
            WeatherAccumulationStore.Save(snapshot);
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        var p = change.Property;
        if (p == CustomImagePathProperty
            || p == ParticleTypeProperty
            || p == SnowPhysicsEnabledProperty
            || p == WeatherSnowEnabledProperty
            || p == WeatherLeavesEnabledProperty
            || p == WeatherSakuraEnabledProperty)
        {
            if (p == ParticleTypeProperty)
                UpdateOverlayVisibility();

            RefreshCustomBitmapCache();
            EnsureSpritesForCurrentKind();
            UpdatePhysicsHooks();
            PushStateToHandler();
        }
        else if (p == SnowPersistenceEnabledProperty)
        {
            if (SnowPersistenceEnabled && IsSnowPhysicsActive && !_snowDataRequested)
            {
                _snowDataRequested = true;
                LoadPersistedSnowAsync();
            }
            PushStateToHandler();
        }
        else if (p == LeafPaletteProperty
              || p == ParticleCountProperty
              || p == ParticleSpeedProperty
              || p == ParticleWindProperty
              || p == ParticleSizeProperty
              || p == ParticleOpacityProperty
              || p == SnowBottomAccumulationEnabledProperty
              || p == SnowCursorInteractionEnabledProperty
              || p == SnowAccumulationRateProperty
              || p == SnowMaxHeightProperty
              || p == ActivePageKeyProperty
              || p == IsModalOccludedProperty)
        {
            if (p == ActivePageKeyProperty)
            {
                _contextSwitchTicks = Stopwatch.GetTimestamp();
                ScheduleColliderSync();
            }
            else if (p == IsModalOccludedProperty)
            {
                ScheduleColliderSync();
            }
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
            _hostWindow.Activated += OnHostWindowActivated;
            _hostWindow.Deactivated += OnHostWindowDeactivated;
        }
    }

    private void DetachHostWindow()
    {
        DetachPhysicsHooks();
        if (_hostWindow != null)
        {
            _hostWindow.PropertyChanged -= OnHostWindowPropertyChanged;
            _hostWindow.Activated -= OnHostWindowActivated;
            _hostWindow.Deactivated -= OnHostWindowDeactivated;
            _hostWindow = null;
        }
    }

    private void OnHostWindowActivated(object? sender, EventArgs e)
    {
        _bridge.ResetCursorMotion(deactivate: false);
    }

    private void OnHostWindowDeactivated(object? sender, EventArgs e)
    {
        _bridge.ResetCursorMotion(deactivate: true);
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

            if (!_snowDataRequested && SnowPersistenceEnabled)
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
            var data = WeatherAccumulationStore.Load();
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

        if (_contextSwitchTicks != 0)
        {
            double elapsedMs = Stopwatch.GetElapsedTime(_contextSwitchTicks).TotalMilliseconds;
            bool hasIncomingPageCollider = false;
            string? targetKey = ActivePageKey;
            for (int i = 0; i < _stagingSnapshots.Count; i++)
            {
                var snap = _stagingSnapshots[i];
                if (!snap.IsVisible || (snap.Right - snap.Left) < 8f || (snap.Bottom - snap.Top) < 8f)
                    continue;
                if (string.Equals(snap.PageKey, targetKey, StringComparison.Ordinal))
                {
                    hasIncomingPageCollider = true;
                    break;
                }
            }

            if (!hasIncomingPageCollider && elapsedMs < 90.0)
            {
                // Hold publishing empty intermediate frame until incoming tab finishes first Arrange pass
                ScheduleColliderSync();
                return;
            }

            _contextSwitchTicks = 0;
        }

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
            IsSnowPhysicsActive,
            SnowBottomAccumulationEnabled,
            SnowCursorInteractionEnabled,
            SnowPersistenceEnabled,
            (float)Math.Clamp(SnowAccumulationRate, 0.3, 3.0),
            (float)Math.Clamp(SnowMaxHeight, 4.0, 24.0),
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
        private bool _cursorResetPending;
        private long _lastPointerPublishTicks;
        private int _cursorVersion;

        private readonly object _persistLock = new();
        private WeatherAccumulationData? _latestPersistence;

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
            long now = Stopwatch.GetTimestamp();
            lock (_pointerLock)
            {
                if (!active || (_lastPointerPublishTicks != 0 && Stopwatch.GetElapsedTime(_lastPointerPublishTicks, now).TotalMilliseconds > 110.0))
                {
                    _cursorResetPending = true;
                }
                _cursorX = x;
                _cursorY = y;
                _cursorActive = active;
                _lastPointerPublishTicks = active ? now : 0;
                _cursorVersion++;
            }
        }

        public void ResetCursorMotion(bool deactivate)
        {
            lock (_pointerLock)
            {
                _cursorResetPending = true;
                _lastPointerPublishTicks = 0;
                if (deactivate)
                    _cursorActive = false;
                _cursorVersion++;
            }
        }

        public void ClearPointer()
        {
            ResetCursorMotion(deactivate: true);
        }

        public bool TryReadPointer(ref int lastVersion, out float x, out float y, out bool active, out bool resetVelocity)
        {
            lock (_pointerLock)
            {
                if (_cursorVersion == lastVersion)
                {
                    x = _cursorX;
                    y = _cursorY;
                    active = _cursorActive;
                    resetVelocity = false;
                    return false;
                }

                lastVersion = _cursorVersion;
                x = _cursorX;
                y = _cursorY;
                active = _cursorActive;
                resetVelocity = _cursorResetPending;
                _cursorResetPending = false;
                return true;
            }
        }

        public void SetLatestPersistence(WeatherAccumulationData data)
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

        public WeatherAccumulationData? TakeLatestPersistenceCopy()
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
        public float Mass;
        public byte PaletteIndex;
        public byte SpriteIndex;
        public bool IsDynamic;
        public bool IsChunk;
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
        public readonly bool SnowBottomAccumulationEnabled;
        public readonly bool SnowCursorInteractionEnabled;
        public readonly bool SnowPersistenceEnabled;
        public readonly float SnowAccumulationRate;
        public readonly float SnowMaxHeight;
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
            bool snowBottomAccumulationEnabled,
            bool snowCursorInteractionEnabled,
            bool snowPersistenceEnabled,
            float snowAccumulationRate,
            float snowMaxHeight,
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
            SnowBottomAccumulationEnabled = snowBottomAccumulationEnabled;
            SnowCursorInteractionEnabled = snowCursorInteractionEnabled;
            SnowPersistenceEnabled = snowPersistenceEnabled;
            SnowAccumulationRate = snowAccumulationRate;
            SnowMaxHeight = snowMaxHeight;
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
        public readonly WeatherAccumulationData Data;
        public LoadSnowMsg(WeatherAccumulationData data) => Data = data;
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
        public int SurfaceIdHash;
        public float[]? Heights;
        public byte[]? PendingSavedBytes;
        public int PendingSavedVersion = 1;
        public List<SettledLeaf>? Leaves;
        public List<SettledLeaf>? PendingSavedLeaves;
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
        public bool IsSynthetic;
        public float MaxHeight;
        public float FadeOpacity = 1.0f;

        public bool HasLeaves => (Leaves is { Count: > 0 }) || (PendingSavedLeaves is { Count: > 0 });
        public bool HasSnow => MaxHeight > 0.25f || (PendingSavedBytes is { Length: > 0 }) || HasLeaves;
    }

    private sealed class ParticleVisualHandler : CompositionCustomVisualHandler
    {
        private const float CellWidthPx = 4.0f;
        private const float LegacyDriftHeightPx = 11.0f;
        private const float PersistenceMaxHeightPx = 24.0f;
        private const float PersistenceMaxHeightV4Px = 38.0f;
        private const float MaxSlopePerCell = 2.15f;
        private const float AirwakeRadiusPx = 54.0f;
        private const float BrushRadiusPx = 11.0f;
        private const float BaseSnowDeposit = 3.25f;
        private const int CursorPixelSlots = 16;
        private const float CursorPixelMaxSnow = 3.2f;

        private static readonly float[] DepositKernel = [0.08f, 0.22f, 0.40f, 0.22f, 0.08f];
        private static readonly float[] CursorSlotOx =
        [
            0.4f, 1.35f, 2.3f, 3.25f, 4.2f, 5.15f, 6.1f, 7.05f, 8.0f, 8.95f, 9.9f, 10.85f,
            0.4f, 0.45f, 0.9f, 1.5f
        ];
        private static readonly float[] CursorSlotOy =
        [
            0.4f, 1.35f, 2.3f, 3.25f, 4.2f, 5.15f, 6.1f, 7.05f, 8.0f, 8.95f, 9.9f, 10.85f,
            2.6f, 5.1f, 7.8f, 10.4f
        ];

        private static readonly SKSamplingOptions NearestSampling = new(SKFilterMode.Nearest, SKMipmapMode.None);
        private static readonly SKSamplingOptions LinearSampling = new(SKFilterMode.Linear, SKMipmapMode.None);

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

        private readonly Dictionary<string, Dictionary<string, DriftSurface>> _snowStore = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Dictionary<string, DriftSurface>> _leavesStore = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Dictionary<string, DriftSurface>> _sakuraStore = new(StringComparer.Ordinal);
        private readonly List<DriftSurface> _visibleSurfaces = new(64);
        private readonly List<DriftSurface> _prevVisibleSurfaces = new(64);

        private Dictionary<string, Dictionary<string, DriftSurface>> GetStoreForKind(ParticleKind kind) => kind switch
        {
            ParticleKind.Leaves => _leavesStore,
            ParticleKind.Sakura => _sakuraStore,
            _ => _snowStore
        };

        private Dictionary<string, Dictionary<string, DriftSurface>> ActiveDriftStore => GetStoreForKind(_kind);

        private static bool IsWeatherPhysicsKind(ParticleKind kind) =>
            kind is ParticleKind.Snow or ParticleKind.Leaves or ParticleKind.Sakura;

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
        private bool _snowBottomAccumulationEnabled;
        private bool _snowCursorInteractionEnabled = true;
        private bool _snowPersistenceEnabled = true;
        private float _snowAccumulationRate = 1.0f;
        private float _snowMaxHeightPx = 11.0f;
        private bool _isModalOccluded;
        private string _activePageKey = "Profiles";
        private float _tabTransitionGraceSec;
        private bool _awaitingAtomicSwap;
        private long _pageSwitchTicks;
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
        private float _prevCursorVx;
        private float _prevCursorVy;
        private float _cursorAccel;
        private float _cursorShakeEnergy;
        private bool _hasPrevCursor;
        private bool _suppressCursorStepThisFrame;
        private readonly float[] _cursorPixelSnow = new float[CursorPixelSlots];
        private float _cursorPixelMax;

        // Dynamic flake ring cursor
        private int _nextDynamicSlot = MaxParticles;

        // Persistence state
        private bool _persistenceDirty;
        private long _lastSaveTicks;
        private int _pendingSaveInFlight;
        private bool _relaxReversePass;

        private SKPaint? _spritePaint;
        private SKPaint? _driftPaint;
        private SKPath? _driftPath;
        private SKPath? _driftInnerPath;
        private SKImage? _snowPuffImage;
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
            bool wasPhysicsActive = _snowPhysicsEnabled && IsWeatherPhysicsKind(_kind);
            bool nowPhysicsActive = cfg.SnowPhysicsEnabled && IsWeatherPhysicsKind(cfg.Kind);

            if (wasPhysicsActive && !nowPhysicsActive)
            {
                if (_snowPersistenceEnabled)
                    FlushPersistenceNow(syncToDisk: true);
                ReleaseActiveVisibleState(releaseStores: !cfg.SnowPhysicsEnabled);
            }
            else if (kindChanged && nowPhysicsActive)
            {
                ReleaseActiveVisibleState(releaseStores: false);
            }

            bool maxHeightShrank = cfg.SnowMaxHeight < _snowMaxHeightPx - 0.1f;

            _kind = cfg.Kind;
            _count = Math.Clamp(cfg.Count, 0, MaxParticles);
            _speed = cfg.Speed;
            _wind = cfg.Wind;
            _size = cfg.Size;
            _opacity = cfg.Opacity;
            _isPaused = cfg.Paused;
            _snowPhysicsEnabled = cfg.SnowPhysicsEnabled;
            _snowBottomAccumulationEnabled = cfg.SnowBottomAccumulationEnabled;
            _snowCursorInteractionEnabled = cfg.SnowCursorInteractionEnabled;
            _snowPersistenceEnabled = cfg.SnowPersistenceEnabled;
            _snowAccumulationRate = Math.Clamp(cfg.SnowAccumulationRate, 0.3f, 3.0f);
            _snowMaxHeightPx = Math.Clamp(cfg.SnowMaxHeight, 4.0f, 24.0f);
            if (!string.Equals(_activePageKey, cfg.ActivePageKey, StringComparison.Ordinal))
            {
                _tabTransitionGraceSec = 0.42f;
                _awaitingAtomicSwap = true;
                _pageSwitchTicks = Stopwatch.GetTimestamp();
            }
            _activePageKey = cfg.ActivePageKey;
            _isModalOccluded = cfg.IsModalOccluded;

            if (!_snowCursorInteractionEnabled && _cursorPixelMax > 0f)
            {
                Array.Clear(_cursorPixelSnow);
                _cursorPixelMax = 0f;
            }

            if (maxHeightShrank && nowPhysicsActive)
            {
                ClampAllSurfacesToMaxHeight();
            }

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
                if (_snowPhysicsEnabled && IsWeatherPhysicsKind(_kind))
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

            if (_snowPhysicsEnabled && !_isModalOccluded && IsWeatherPhysicsKind(_kind))
            {
                if (_kind == ParticleKind.Snow)
                {
                    RenderSnowDrifts(canvas);
                    RenderCursorSnowCap(canvas);
                }
                else if (_spriteImages is { Length: > 0 } && _spriteKind == _kind)
                {
                    RenderLeafOrSakuraPiles(canvas, _spriteImages);
                }
            }

            if (_kind == ParticleKind.Custom)
            {
                RenderCustomImages(canvas);
            }
            else if (_spriteImages is { Length: > 0 } && _spriteKind == _kind)
            {
                RenderMinecraftSprites(canvas, _spriteImages);
            }

            if (_kind == ParticleKind.Snow && _snowPhysicsEnabled && !_isModalOccluded)
            {
                RenderSnowChunks(canvas);
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
            if (_tabTransitionGraceSec > 0f)
                _tabTransitionGraceSec = MathF.Max(0f, _tabTransitionGraceSec - dt);

            SyncSurfacesFromBridge();
            EnsureBottomEdgeSurfaceSync();

            if (_awaitingAtomicSwap && Stopwatch.GetElapsedTime(_pageSwitchTicks, nowTicks).TotalMilliseconds >= 95.0)
            {
                _awaitingAtomicSwap = false;
            }

            if (!_awaitingAtomicSwap)
            {
                float fadeStep = dt / 0.22f;
                for (int i = 0; i < _visibleSurfaces.Count; i++)
                {
                    var s = _visibleSurfaces[i];
                    if (s.FadeOpacity < 1.0f)
                        s.FadeOpacity = MathF.Min(1.0f, s.FadeOpacity + fadeStep);
                }
            }

            _suppressCursorStepThisFrame = false;
            if (_snowCursorInteractionEnabled)
            {
                UpdateCursorKinematics(dt);

                if (!_suppressCursorStepThisFrame && !_isModalOccluded && _cursorActive)
                {
                    if (_kind == ParticleKind.Snow)
                        UpdateCursorSnowShedding(dt);

                    if (_hasPrevCursor)
                    {
                        float moveDistSq = (_cursorX - _prevCursorX) * (_cursorX - _prevCursorX)
                                         + (_cursorY - _prevCursorY) * (_cursorY - _prevCursorY);
                        if (moveDistSq > 0.36f)
                        {
                            BrushDriftsAlongCursorSegment(_prevCursorX, _prevCursorY, _cursorX, _cursorY);
                        }
                    }
                }
            }

            float baseFall = _kind switch
            {
                ParticleKind.Sakura => 52.0f,
                ParticleKind.Leaves => 58.0f,
                _ => 44.0f
            };
            float windPx = _wind * 65.0f;
            float margin = 42.0f;
            float drag = MathF.Exp(-3.4f * dt);
            float cursorSpeedSq = _cursorVx * _cursorVx + _cursorVy * _cursorVy;
            float wakeThresholdSq = _kind == ParticleKind.Snow ? 3000f : 180f;
            bool hasCursorWake = !_suppressCursorStepThisFrame && _snowCursorInteractionEnabled && _cursorActive && cursorSpeedSq > wakeThresholdSq;
            bool canLandOnCursor = _kind == ParticleKind.Snow && !_suppressCursorStepThisFrame && _snowCursorInteractionEnabled && _cursorActive && !_isModalOccluded && cursorSpeedSq < (420f * 420f);

            // Step ambient particles
            for (int i = 0; i < _count; i++)
            {
                StepSingleSnowflake(ref _items[i], dt, baseFall, windPx, margin, drag, hasCursorWake, canLandOnCursor);
            }

            // Step dynamic brushed/shed weather particles
            for (int i = MaxParticles; i < TotalParticleSlots; i++)
            {
                if (!_items[i].IsActive) continue;
                StepSingleSnowflake(ref _items[i], dt, baseFall * 1.05f, windPx * 0.45f, margin, drag, hasCursorWake, canLandOnCursor);
            }

            if (_kind is ParticleKind.Leaves or ParticleKind.Sakura)
            {
                StepSettledLeaves(dt);
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
            if (_snowPersistenceEnabled && _persistenceDirty && Stopwatch.GetElapsedTime(_lastSaveTicks, nowTicks).TotalSeconds >= 2.5)
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
            bool hasCursorWake,
            bool canLandOnCursor)
        {
            p.PrevY = p.Y;
            if (p.Cooldown > 0f)
                p.Cooldown = MathF.Max(0f, p.Cooldown - dt);

            bool isLeafOrSakura = _kind is ParticleKind.Leaves or ParticleKind.Sakura;

            // Air drag on extra impulse velocity + gravity for ballistic flung snow / light fluttering leaves
            if (p.IsDynamic)
            {
                if (p.IsChunk)
                {
                    p.Vx *= MathF.Exp(-2.25f * dt);
                    p.Vy += 265.0f * dt;
                    p.Vy *= MathF.Exp(-1.25f * dt);
                    p.Vy = Math.Clamp(p.Vy, -340f, 310f);
                }
                else if (isLeafOrSakura)
                {
                    float g = _kind == ParticleKind.Sakura ? 68.0f : 86.0f;
                    p.Vx *= MathF.Exp(-2.3f * dt);
                    p.Vy += g * dt;
                    p.Vy *= MathF.Exp(-2.1f * dt);
                    p.Vy = Math.Clamp(p.Vy, -280f, 220f);
                }
                else
                {
                    p.Vx *= drag;
                    p.Vy += 165.0f * dt;
                    p.Vy *= MathF.Exp(-1.85f * dt);
                    p.Vy = Math.Clamp(p.Vy, -260f, 220f);
                }
                p.Angle += p.SpinRate * dt;
            }
            else
            {
                if (isLeafOrSakura)
                {
                    float leafDrag = MathF.Exp(-1.45f * dt);
                    p.Vx *= leafDrag;
                    p.Vy *= leafDrag;
                    p.Angle += p.SpinRate * dt;
                }
                else
                {
                    p.Vx *= drag;
                    p.Vy *= drag;
                }
            }

            // Cursor airwake
            float wakeRadius = isLeafOrSakura ? 68.0f : AirwakeRadiusPx;
            if (hasCursorWake)
            {
                float dx = p.X - _cursorX;
                float dy = p.Y - _cursorY;
                float distSq = dx * dx + dy * dy;
                if (distSq < wakeRadius * wakeRadius && distSq > 1f)
                {
                    float dist = MathF.Sqrt(distSq);
                    float w = 1.0f - (dist / wakeRadius);
                    float w2 = w * w;
                    float invD = 1.0f / dist;
                    float radialX = dx * invD;
                    float radialY = dy * invD;
                    float cross = radialX * _cursorVy - radialY * _cursorVx;
                    float swirlSign = cross >= 0f ? 1f : -1f;
                    float swirlMag = MathF.Min(240f, MathF.Sqrt(_cursorVx * _cursorVx + _cursorVy * _cursorVy) * 0.35f);
                    float wakeScale = p.IsChunk ? 0.22f : (isLeafOrSakura ? 1.45f : 1.0f);

                    p.Vx += (_cursorVx * 0.50f * w2 + radialX * 85f * w2 - radialY * swirlSign * swirlMag * w2) * dt * 8.0f * wakeScale;
                    p.Vy += (_cursorVy * 0.38f * w2 + radialY * 65f * w2 + radialX * swirlSign * swirlMag * w2) * dt * 8.0f * wakeScale;
                    if (isLeafOrSakura)
                    {
                        p.SpinRate = Math.Clamp(p.SpinRate + swirlSign * w2 * 9.0f * dt, -6.5f, 6.5f);
                    }

                    p.Vx = Math.Clamp(p.Vx, -380f, 380f);
                    p.Vy = Math.Clamp(p.Vy, -340f, 440f);
                }
            }

            // Direct physical cursor deflection for falling leaves & sakura even when cursor moves slowly or sits still
            if (isLeafOrSakura && _snowCursorInteractionEnabled && _cursorActive && !_isModalOccluded)
            {
                float cdx = p.X - (_cursorX + 5.0f);
                float cdy = p.Y - (_cursorY + 6.5f);
                float cDistSq = cdx * cdx + cdy * cdy;
                const float contactR = 15.0f;
                if (cDistSq < contactR * contactR)
                {
                    float dist = MathF.Sqrt(cDistSq);
                    float nx, ny;
                    if (dist > 0.001f)
                    {
                        nx = cdx / dist;
                        ny = cdy / dist;
                    }
                    else
                    {
                        nx = p.X >= _cursorX + 5.0f ? 1f : -1f;
                        ny = -0.35f;
                    }

                    float overlap = contactR - dist;
                    float cSpeed = MathF.Sqrt(_cursorVx * _cursorVx + _cursorVy * _cursorVy);
                    bool nearFloor = HasUnderlyingSurface(p.X, p.Y, 16f, out float floorTopY);
                    float nextX = p.X + nx * overlap * 0.55f;
                    float nextY = p.Y + ny * overlap * 0.25f;
                    if (nearFloor)
                    {
                        nextY = MathF.Min(nextY, floorTopY - 1.5f);
                        float sideDir = MathF.Abs(nx) > 0.22f ? MathF.Sign(nx) : (p.X >= _cursorX + 5.0f ? 1f : -1f);
                        p.Vx = Math.Clamp(p.Vx + sideDir * (56f + cSpeed * 0.18f) * dt * 12f + _cursorVx * 0.18f, -340f, 340f);
                        p.Vy = Math.Clamp(p.Vy - (22f + MathF.Abs(_cursorVy) * 0.08f) * dt * 12f, -260f, 120f);
                    }
                    else
                    {
                        p.Vx = Math.Clamp(p.Vx + nx * (48f + cSpeed * 0.14f) * dt * 12f + _cursorVx * 0.16f, -340f, 340f);
                        p.Vy = Math.Clamp(p.Vy + (ny * 26f - 28f) * dt * 12f + _cursorVy * 0.12f, -290f, 320f);
                    }
                    p.X = nextX;
                    p.Y = nextY;
                    p.SpinRate = Math.Clamp(p.SpinRate + (nx >= 0f ? 1f : -1f) * 3.8f * dt * 8f, -6.8f, 6.8f);
                }
            }

            float ambientVy = baseFall * _speed * p.SpeedScale;
            float swayScale = isLeafOrSakura ? (p.IsDynamic ? 1.25f : 1.0f) : 0.6f;
            float sway = MathF.Sin(_elapsedSec * p.SwayFreq + p.Phase) * p.SwayAmp * swayScale;
            float ambientVx = windPx * p.SpeedScale + sway;

            float totalVx = (p.IsChunk ? ambientVx * 0.35f : ambientVx) + p.Vx;
            float totalVy = (p.IsChunk ? ambientVy * 0.28f : (isLeafOrSakura && p.IsDynamic ? ambientVy * 0.72f : ambientVy)) + p.Vy;

            p.X += totalVx * dt;
            p.Y += totalVy * dt;

            // Accumulate gently onto cursor cap when cursor is still or moving slowly
            if (canLandOnCursor && p.Cooldown <= 0f && totalVy > 4f)
            {
                if (TryDepositOnCursor(ref p))
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

            // Surface landing & anti-tunneling collision (synthetic bottom floor never bypasses even during cooldown)
            if (!_isModalOccluded && _visibleSurfaces.Count > 0 && (totalVy > 4f || HasSolidPenetration(p.X, p.Y)))
            {
                if (TryCollideAndDeposit(ref p, totalVx, totalVy))
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

            ClampParticleAboveSyntheticFloor(ref p);

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
            p.Mass = 1.0f;
        }

        private bool TryDepositOnCursor(ref ParticleItem p)
        {
            float dx = p.X - _cursorX;
            float dy = p.Y - _cursorY;
            if (dx < -1.5f || dx > 12.5f || dy < -2.5f || dy > 12.5f)
                return false;

            int bestSlot = -1;
            float bestDistSq = 2.4f * 2.4f;
            for (int i = 0; i < CursorPixelSlots; i++)
            {
                float sx = CursorSlotOx[i];
                float sy = CursorSlotOy[i] - _cursorPixelSnow[i] * 0.25f;
                float ddx = dx - sx;
                float ddy = dy - sy;
                float d2 = ddx * ddx + ddy * ddy;
                if (d2 < bestDistSq)
                {
                    bestDistSq = d2;
                    bestSlot = i;
                }
            }

            if (bestSlot < 0)
                return false;

            if (p.PrevY > _cursorY + CursorSlotOy[bestSlot] + 2.5f)
                return false;

            float rateScale = p.IsChunk ? 1.0f : _snowAccumulationRate;
            float amount = 1.45f * p.Mass * rateScale * Math.Clamp(_size, 0.5f, 1.5f);
            return TryAddSnowToCursorPixel(bestSlot, amount) > 0.001f;
        }

        private float TryAddSnowToCursorPixel(int slot, float totalVolume)
        {
            float addedTotal = 0f;
            float room = MathF.Max(0f, CursorPixelMaxSnow - _cursorPixelSnow[slot]);
            float mainAdd = MathF.Min(room, totalVolume * 0.72f);
            if (mainAdd > 0.001f)
            {
                _cursorPixelSnow[slot] += mainAdd;
                addedTotal += mainAdd;
            }

            // Gentle neighbor bleed along bevel slots (0..11) or left-edge slots (12..15)
            float sideShare = (totalVolume - mainAdd) * 0.5f;
            if (sideShare > 0.001f)
            {
                if (slot < 12)
                {
                    if (slot - 1 >= 0)
                    {
                        float r = MathF.Max(0f, CursorPixelMaxSnow - _cursorPixelSnow[slot - 1]);
                        float a = MathF.Min(r, sideShare);
                        _cursorPixelSnow[slot - 1] += a;
                        addedTotal += a;
                    }
                    if (slot + 1 < 12)
                    {
                        float r = MathF.Max(0f, CursorPixelMaxSnow - _cursorPixelSnow[slot + 1]);
                        float a = MathF.Min(r, sideShare);
                        _cursorPixelSnow[slot + 1] += a;
                        addedTotal += a;
                    }
                }
                else
                {
                    if (slot - 1 >= 12)
                    {
                        float r = MathF.Max(0f, CursorPixelMaxSnow - _cursorPixelSnow[slot - 1]);
                        float a = MathF.Min(r, sideShare);
                        _cursorPixelSnow[slot - 1] += a;
                        addedTotal += a;
                    }
                }
            }

            if (addedTotal > 0.001f)
                RecomputeCursorCapMax();

            return addedTotal;
        }

        private float TryCoatCursorFromDrift(float totalVolume, float moveDir)
        {
            // Distribute scooped drift snow across cursor edge pixels so it coats the cursor silhouette
            float remaining = totalVolume;
            float added = 0f;
            int startSlot = moveDir < 0f ? 12 : 0;

            for (int pass = 0; pass < 3 && remaining > 0.02f; pass++)
            {
                int slot = (startSlot + _rng.Next(0, CursorPixelSlots)) % CursorPixelSlots;
                float hit = TryAddSnowToCursorPixel(slot, remaining * 0.55f);
                if (hit > 0.001f)
                {
                    remaining -= hit;
                    added += hit;
                }
            }

            return added;
        }

        private bool IsSharpCursorFlick(float cursorSpeed)
        {
            return cursorSpeed > 1180f
                || (_cursorAccel > 8800f && cursorSpeed > 480f)
                || _cursorShakeEnergy >= 1.0f;
        }

        private void UpdateCursorSnowShedding(float dt)
        {
            if (_cursorPixelMax <= 0.14f) return;

            float speed = MathF.Sqrt(_cursorVx * _cursorVx + _cursorVy * _cursorVy);
            if (!IsSharpCursorFlick(speed))
                return;

            float intensity = MathF.Max(
                (speed - 1100f) / 700f,
                MathF.Max((_cursorAccel - 8500f) / 6000f, _cursorShakeEnergy - 0.85f));
            float shedFrac = Math.Clamp(intensity * 0.45f + 0.18f, 0.18f, 0.68f);

            float massAccum = 0f;
            int spawned = 0;
            int dustSpawned = 0;

            for (int i = 0; i < CursorPixelSlots; i++)
            {
                float h = _cursorPixelSnow[i];
                if (h <= 0.08f) continue;

                float take = h * shedFrac;
                _cursorPixelSnow[i] = h - take < 0.06f ? 0f : h - take;
                massAccum += take;

                if (massAccum >= 0.95f && spawned < 6)
                {
                    float unit = MathF.Min(massAccum, 1.45f);
                    massAccum -= unit;
                    float cx = _cursorX + CursorSlotOx[i];
                    float cy = _cursorY + CursorSlotOy[i];
                    float vx = _cursorVx * 0.40f + ((float)_rng.NextDouble() - 0.5f) * 105f;
                    float vy = MathF.Min(-24f, _cursorVy * 0.24f - 40f - (float)_rng.NextDouble() * 55f);
                    float safeCy = ClampAboveSyntheticFloor(cx, cy);
                    SpawnDynamicSnowflake(cx, safeCy, vx, vy, cooldown: 0.045f, mass: unit / BaseSnowDeposit, isChunk: true, sizeScale: 0.68f + (float)_rng.NextDouble() * 0.25f);
                    spawned++;

                    if (dustSpawned < 2 && _rng.NextDouble() < 0.45)
                    {
                        SpawnDynamicSnowflake(
                            cx,
                            safeCy,
                            vx * 0.72f + ((float)_rng.NextDouble() - 0.5f) * 65f,
                            MathF.Min(-20f, vy - 15f),
                            cooldown: 0.045f,
                            mass: 0.15f,
                            isChunk: false,
                            sizeScale: 0.45f);
                        dustSpawned++;
                    }
                }
            }

            if (massAccum >= 0.35f && spawned < 6)
            {
                float safeCy = ClampAboveSyntheticFloor(_cursorX + 3f, _cursorY + 3f);
                SpawnDynamicSnowflake(
                    _cursorX + 3f,
                    safeCy,
                    _cursorVx * 0.38f + ((float)_rng.NextDouble() - 0.5f) * 85f,
                    MathF.Min(-22f, _cursorVy * 0.22f - 35f),
                    cooldown: 0.045f,
                    mass: Math.Clamp(massAccum / BaseSnowDeposit, 0.32f, 1.1f),
                    isChunk: true,
                    sizeScale: 0.65f);
            }

            if (_cursorShakeEnergy >= 1.0f)
                _cursorShakeEnergy *= 0.45f;

            RecomputeCursorCapMax();
        }

        private void RecomputeCursorCapMax()
        {
            float max = 0f;
            for (int i = 0; i < CursorPixelSlots; i++)
            {
                if (_cursorPixelSnow[i] > max) max = _cursorPixelSnow[i];
            }
            _cursorPixelMax = max;
        }

        private bool HasUnderlyingSurface(float x, float y, float maxDistPx, out float topY)
        {
            topY = float.MaxValue;
            bool found = false;
            for (int i = 0; i < _visibleSurfaces.Count; i++)
            {
                var s = _visibleSurfaces[i];
                if (x < s.Left || x > s.Right) continue;
                if (s.HasClipRect && (s.Top < s.ClipTop - 1.5f || s.Top > s.ClipBottom + 1.5f)) continue;
                if (y <= s.Top + 2f && s.Top - y <= maxDistPx && s.Top < topY)
                {
                    topY = s.Top;
                    found = true;
                }
            }
            return found;
        }

        private bool HasSolidPenetration(float x, float y)
        {
            for (int i = 0; i < _visibleSurfaces.Count; i++)
            {
                var s = _visibleSurfaces[i];
                if (!s.IsSynthetic) continue;
                if (x >= s.Left && x <= s.Right && y >= s.Top - 2.0f)
                    return true;
            }
            return false;
        }

        private float ClampAboveSyntheticFloor(float x, float y)
        {
            for (int i = 0; i < _visibleSurfaces.Count; i++)
            {
                var s = _visibleSurfaces[i];
                if (!s.IsSynthetic) continue;
                if (x >= s.Left && x <= s.Right && y > s.Top - 2.5f)
                    return s.Top - 2.5f;
            }
            return y;
        }

        private void ClampParticleAboveSyntheticFloor(ref ParticleItem p)
        {
            if (_isModalOccluded || _visibleSurfaces.Count == 0)
                return;

            for (int i = 0; i < _visibleSurfaces.Count; i++)
            {
                var s = _visibleSurfaces[i];
                if (!s.IsSynthetic) continue;
                if (p.X < s.Left + 1f || p.X > s.Right - 1f)
                    continue;

                if (p.Y > s.Top - 1.5f)
                {
                    p.Y = s.Top - 1.5f;
                    p.PrevY = p.Y;
                    if (p.Vy > 0f) p.Vy = 0f;
                }
            }
        }

        private bool TryCollideAndDeposit(ref ParticleItem p, float totalVx, float totalVy)
        {
            bool isLeafOrSakura = _kind is ParticleKind.Leaves or ParticleKind.Sakura;

            for (int i = 0; i < _visibleSurfaces.Count; i++)
            {
                var s = _visibleSurfaces[i];
                if (p.Cooldown > 0f && !s.IsSynthetic)
                    continue;

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

                if (isLeafOrSakura)
                {
                    if (TryCollideAndDepositLeaf(s, ref p, totalVx, totalVy))
                        return true;
                    continue;
                }

                int cell = Math.Clamp((int)((p.X - s.Left) / CellWidthPx), 0, s.CellCount - 1);
                float curDriftH = s.Heights != null ? s.Heights[cell] : 0f;
                float hitY = s.Top - curDriftH + 1.5f;
                float prevLimit = s.IsSynthetic ? s.Top + 48.0f : s.Top + 8.0f;

                if (p.PrevY <= prevLimit && p.Y >= hitY)
                {
                    float sculptCap = GetSculptMaxHeightPx();
                    float maxDepositCap = p.IsDynamic ? sculptCap : _snowMaxHeightPx;
                    float totalVolume = p.IsChunk
                        ? BaseSnowDeposit * Math.Clamp(p.Mass, 0.35f, 1.85f)
                        : BaseSnowDeposit * _snowAccumulationRate * Math.Clamp(p.Mass, 0.18f, 1.8f)
                            * (0.95f + 0.35f * p.SizeScale)
                            * Math.Clamp(_size, 0.5f, 1.8f);

                    if (DepositGaussian(s, cell, totalVolume, maxDepositCap))
                    {
                        s.NeedsRelaxation = true;
                        RelaxLocal(s, cell, sculptCap);
                        _persistenceDirty = true;
                        return true;
                    }

                    // Saturated right under impact: absorb on floor surfaces; gently roll down local slope on elevated cards
                    if (s.IsSynthetic)
                        return true;

                    var hArr = s.Heights;
                    float leftH = (hArr != null && cell > 0) ? hArr[cell - 1] : float.MaxValue;
                    float rightH = (hArr != null && cell + 1 < s.CellCount) ? hArr[cell + 1] : float.MaxValue;
                    float slideSide = leftH < rightH ? -1f : (rightH < leftH ? 1f : (p.X < (s.Left + s.Right) * 0.5f ? -1f : 1f));
                    p.Vx = Math.Clamp(p.Vx * 0.45f + slideSide * (p.IsChunk ? 16f : 14f), -52f, 52f);
                    p.Vy = p.IsChunk ? MathF.Min(p.Vy * 0.15f, 22f) : MathF.Min(p.Vy * 0.35f, 28f);
                    p.Cooldown = 0.08f;
                    return false;
                }
            }

            return false;
        }

        private float GetLeafSpriteHalfHeight(byte spriteIndex, float sizeScale)
        {
            float baseScale = _kind == ParticleKind.Sakura ? 11.0f * _size : 15.5f * _size;
            var sprites = _spriteImages;
            if (sprites == null || sprites.Length == 0)
                return baseScale * sizeScale * 0.5f;

            var img = sprites[spriteIndex % sprites.Length];
            int maxDim = Math.Max(1, Math.Max(img.Width, img.Height));
            return img.Height * ((baseScale * sizeScale) / maxDim) * 0.5f;
        }

        private bool TryCollideAndDepositLeaf(DriftSurface s, ref ParticleItem p, float totalVx, float totalVy)
        {
            float cornerPadL = MathF.Max(3.0f, s.CornerRadiusLeft * 0.35f);
            float cornerPadR = MathF.Max(3.0f, s.CornerRadiusRight * 0.35f);
            float relX = p.X - s.Left;
            if (relX < cornerPadL - 1.5f || relX > s.Width - cornerPadR + 1.5f)
                return false;

            float stackTopY = 0f;
            var leaves = s.Leaves;
            if (leaves != null)
            {
                for (int j = 0; j < leaves.Count; j++)
                {
                    var existing = leaves[j];
                    if (MathF.Abs(existing.RelX - relX) < 7.0f)
                    {
                        float topCandidate = existing.TargetOffsetY + 2.35f;
                        if (topCandidate > stackTopY) stackTopY = topCandidate;
                    }
                }
            }

            float halfH = GetLeafSpriteHalfHeight(p.SpriteIndex, p.SizeScale);
            float restAnchorOffset = halfH * 0.36f - 1.0f;
            float hitCenterY = s.Top - stackTopY - restAnchorOffset;
            float prevLimit = s.IsSynthetic ? s.Top + 48.0f : hitCenterY + 9.0f;
            if (p.PrevY > prevLimit || p.Y < hitCenterY - 0.5f)
                return false;

            // Respect lower accumulation rate cleanly without splitting 1 leaf into fragments
            if (!p.IsDynamic && _snowAccumulationRate < 0.99f && _rng.NextDouble() > _snowAccumulationRate)
            {
                if (s.IsSynthetic)
                    return true;
                p.Cooldown = 0.05f;
                return false;
            }

            float maxStack = MathF.Max(2.0f, _snowMaxHeightPx * 0.92f);
            if (stackTopY > maxStack)
            {
                if (s.IsSynthetic)
                    return true;

                float slideSide = relX < s.Width * 0.5f ? -1f : 1f;
                p.Vx = Math.Clamp(p.Vx + slideSide * 24f, -85f, 85f);
                p.Cooldown = 0.12f;
                return false;
            }

            s.Leaves ??= new List<SettledLeaf>(32);
            int maxLeaves = s.IsSynthetic
                ? Math.Clamp((int)(s.Width / 3.5f * (_snowMaxHeightPx / 11.0f)), 48, 220)
                : Math.Clamp((int)(s.Width / 4.5f * (_snowMaxHeightPx / 11.0f)), 14, 110);

            if (s.Leaves.Count >= maxLeaves)
            {
                s.Leaves.RemoveAt(0);
            }

            // Capture exact falling pose/opacity on contact and smoothly settle with natural bounce & glide
            float currentAngle = MathF.IEEERemainder(p.Angle, MathF.PI * 2.0f);
            float baseHoriz = MathF.Abs(currentAngle) > MathF.PI * 0.5f
                ? (currentAngle >= 0f ? MathF.PI : -MathF.PI)
                : 0f;
            float targetRotation = baseHoriz + Math.Clamp((currentAngle - baseHoriz) * 0.34f, -0.52f, 0.52f);
            float targetOffsetY = Math.Clamp(stackTopY + (float)_rng.NextDouble() * 0.85f, 0f, maxStack);
            float startOffsetY = Math.Clamp(s.Top - restAnchorOffset - p.Y, targetOffsetY, targetOffsetY + 5.0f);

            s.Leaves.Add(new SettledLeaf
            {
                RelX = Math.Clamp(relX, cornerPadL, MathF.Max(cornerPadL, s.Width - cornerPadR)),
                OffsetY = startOffsetY,
                Rotation = currentAngle,
                Scale = p.SizeScale,
                SpriteIndex = p.SpriteIndex,
                PaletteIndex = p.PaletteIndex,
                Alpha = (byte)Math.Clamp((int)MathF.Round(p.AlphaScale * 255f), 40, 255),
                Vx = Math.Clamp(totalVx * 0.56f, -105f, 105f),
                Vy = -Math.Clamp(totalVy * 0.14f, 4f, 18f),
                AngularVel = Math.Clamp(p.SpinRate * 0.48f, -5.8f, 5.8f),
                TargetOffsetY = targetOffsetY,
                TargetRotation = targetRotation,
                IsSettling = true
            });

            _persistenceDirty = true;
            return true;
        }

        private void StepSettledLeaves(float dt)
        {
            float cursorSpeedSq = _cursorVx * _cursorVx + _cursorVy * _cursorVy;
            bool cursorRustle = _snowCursorInteractionEnabled && _cursorActive && !_isModalOccluded && cursorSpeedSq > 45f * 45f;
            float cursorSpeed = cursorRustle ? MathF.Sqrt(cursorSpeedSq) : 0f;
            float maxStack = MathF.Max(2.0f, _snowMaxHeightPx * 0.92f);
            float windAbs = MathF.Abs(_wind);
            bool windActive = windAbs > 0.08f;

            for (int i = 0; i < _visibleSurfaces.Count; i++)
            {
                var s = _visibleSurfaces[i];
                var leaves = s.Leaves;
                if (leaves == null || leaves.Count == 0)
                    continue;

                float cornerPadL = MathF.Max(3.0f, s.CornerRadiusLeft * 0.35f);
                float cornerPadR = MathF.Max(3.0f, s.CornerRadiusRight * 0.35f);
                float maxRelX = MathF.Max(cornerPadL, s.Width - cornerPadR);
                bool surfaceNearCursor = cursorRustle
                    && _cursorX >= s.Left - 48f && _cursorX <= s.Right + 48f
                    && _cursorY >= s.Top - maxStack - 48f && _cursorY <= s.Top + 32f;

                // Occasional ambient breeze gust stirs exposed settled leaves naturally
                if (windActive && _rng.NextDouble() < dt * (1.2f + windAbs * 6.5f))
                {
                    int idx = _rng.Next(leaves.Count);
                    var gustLeaf = leaves[idx];
                    if (gustLeaf.OffsetY >= 0.4f || _rng.NextDouble() < 0.35)
                    {
                        float dir = MathF.Sign(_wind);
                        gustLeaf.Vx = Math.Clamp(gustLeaf.Vx + dir * (16f + (float)_rng.NextDouble() * 26f) * windAbs, -90f, 90f);
                        gustLeaf.Vy = Math.Clamp(gustLeaf.Vy + (10f + (float)_rng.NextDouble() * 16f) * windAbs, 0f, 34f);
                        gustLeaf.AngularVel = Math.Clamp(gustLeaf.AngularVel + dir * (1.4f + (float)_rng.NextDouble() * 2.2f), -5.0f, 5.0f);
                        gustLeaf.IsSettling = true;
                        leaves[idx] = gustLeaf;
                    }
                }

                for (int j = leaves.Count - 1; j >= 0; j--)
                {
                    var leaf = leaves[j];

                    if (surfaceNearCursor)
                    {
                        float wx = s.Left + leaf.RelX;
                        float wy = s.Top - leaf.OffsetY - 2f;
                        float dx = wx - _cursorX;
                        float dy = wy - _cursorY;
                        float d2 = dx * dx + dy * dy;
                        if (d2 < 46f * 46f && d2 > 13f * 13f)
                        {
                            float falloff = 1.0f - MathF.Sqrt(d2) / 46f;
                            float gust = Math.Clamp(cursorSpeed * 0.055f, 8f, 36f) * falloff;
                            leaf.Vx = Math.Clamp(leaf.Vx + MathF.Sign(_cursorVx + (dx >= 0f ? 1f : -1f)) * gust * dt * 11f, -95f, 95f);
                            leaf.Vy = Math.Clamp(leaf.Vy + (8f + falloff * 18f) * dt * 7f, -20f, 36f);
                            leaf.AngularVel = Math.Clamp(leaf.AngularVel + (_cursorVx >= 0f ? 1f : -1f) * 2.6f * falloff * dt * 9f, -5.0f, 5.0f);
                            leaf.IsSettling = true;
                        }
                    }

                    if (!leaf.IsSettling)
                        continue;

                    leaf.RelX += leaf.Vx * dt;
                    leaf.Vx *= MathF.Exp(-6.5f * dt);

                    leaf.Vy -= 165.0f * dt;
                    leaf.OffsetY += leaf.Vy * dt;
                    if (leaf.OffsetY <= leaf.TargetOffsetY)
                    {
                        if (leaf.Vy < -24.0f)
                        {
                            leaf.OffsetY = leaf.TargetOffsetY;
                            leaf.Vy = -leaf.Vy * 0.22f;
                        }
                        else
                        {
                            leaf.OffsetY = leaf.TargetOffsetY;
                            leaf.Vy = 0f;
                        }
                    }

                    float angDiff = leaf.TargetRotation - leaf.Rotation;
                    leaf.AngularVel = (leaf.AngularVel + angDiff * 26.0f * dt) * MathF.Exp(-6.2f * dt);
                    leaf.Rotation = Math.Clamp(leaf.Rotation + leaf.AngularVel * dt, -MathF.PI, MathF.PI);

                    if (!s.IsSynthetic && (leaf.RelX < cornerPadL - 2.0f || leaf.RelX > maxRelX + 2.0f))
                    {
                        float wx = s.Left + leaf.RelX;
                        float wy = s.Top - leaf.OffsetY - 2f;
                        leaves.RemoveAt(j);
                        SpawnDynamicLeafFromSettled(in leaf, wx, MathF.Min(s.Top - 2.5f, wy), leaf.Vx * 0.85f, -18f, cooldown: 0.045f);
                        _persistenceDirty = true;
                        continue;
                    }

                    leaf.RelX = Math.Clamp(leaf.RelX, cornerPadL, maxRelX);
                    if (MathF.Abs(leaf.Vx) < 0.6f
                        && MathF.Abs(leaf.Vy) < 1.0f
                        && MathF.Abs(leaf.OffsetY - leaf.TargetOffsetY) < 0.08f
                        && MathF.Abs(angDiff) < 0.015f
                        && MathF.Abs(leaf.AngularVel) < 0.05f)
                    {
                        leaf.Vx = 0f;
                        leaf.Vy = 0f;
                        leaf.AngularVel = 0f;
                        leaf.OffsetY = leaf.TargetOffsetY;
                        leaf.Rotation = leaf.TargetRotation;
                        leaf.IsSettling = false;
                    }

                    leaves[j] = leaf;
                }
            }
        }

        private float GetSculptMaxHeightPx() =>
            MathF.Min(PersistenceMaxHeightV4Px, MathF.Max(_snowMaxHeightPx + 8.0f, _snowMaxHeightPx * 1.95f));

        private static float GetEffectiveSlopeLimit(float hA, float hB) =>
            MaxSlopePerCell + MathF.Min(0.85f, MathF.Max(hA, hB) * 0.042f);

        private static bool DepositGaussian(DriftSurface s, int centerCell, float totalVolume, float maxDriftHeightPx)
        {
            EnsureHeightsAllocated(s);
            var h = s.Heights!;
            int n = s.CellCount;
            float leftover = 0f;
            bool depositedAny = false;

            for (int k = 0; k < DepositKernel.Length; k++)
            {
                int idx = centerCell + (k - 2);
                float share = totalVolume * DepositKernel[k];
                if ((uint)idx >= (uint)n)
                {
                    leftover += share * 0.5f;
                    continue;
                }

                float cap = GetMaxHeightForCell(s, idx, maxDriftHeightPx);
                float room = MathF.Max(0f, cap - h[idx]);
                float add = MathF.Min(share, room);
                if (add > 0.001f)
                {
                    h[idx] += add;
                    depositedAny = true;
                }
                leftover += share - add;
            }

            // Settle remaining snow into nearby lower pockets without smearing wide
            if (leftover > 0.05f)
            {
                int maxDist = s.IsSynthetic ? 6 : 4;
                for (int dist = 1; dist <= maxDist && leftover > 0.05f; dist++)
                {
                    int idxL = centerCell - dist;
                    int idxR = centerCell + dist;
                    float hL = (uint)idxL < (uint)n ? h[idxL] : float.MaxValue;
                    float hR = (uint)idxR < (uint)n ? h[idxR] : float.MaxValue;
                    int firstSign = hL <= hR ? -1 : 1;

                    for (int pass = 0; pass < 2 && leftover > 0.05f; pass++)
                    {
                        int sign = pass == 0 ? firstSign : -firstSign;
                        int idx = centerCell + dist * sign;
                        if ((uint)idx >= (uint)n) continue;
                        float cap = GetMaxHeightForCell(s, idx, maxDriftHeightPx);
                        float room = MathF.Max(0f, cap - h[idx]);
                        float add = MathF.Min(leftover, room);
                        if (add > 0.001f)
                        {
                            h[idx] += add;
                            leftover -= add;
                            depositedAny = true;
                        }
                    }
                }
            }

            if (depositedAny)
                RecomputeMaxHeight(s);

            return depositedAny;
        }

        private float PushSnowIntoWave(DriftSurface s, int startCell, int dir, float amount, float maxHeightCap, bool allowEdgeSpill)
        {
            if (amount <= 0.001f || s.Heights == null || s.CellCount <= 0)
                return amount;

            var h = s.Heights;
            int n = s.CellCount;
            float remaining = amount;

            // Deposit sequentially ahead of the cursor face, piling up a dense front ridge first
            for (int step = 0; step < 6 && remaining > 0.005f; step++)
            {
                int idx = startCell + dir * step;
                if ((uint)idx >= (uint)n)
                {
                    // Synthetic bottom floor retains snow against window edges as a corner bank
                    if (!allowEdgeSpill)
                    {
                        int edgeIdx = Math.Clamp(idx, 0, n - 1);
                        float edgeCap = GetMaxHeightForCell(s, edgeIdx, maxHeightCap);
                        float edgeRoom = MathF.Max(0f, edgeCap - h[edgeIdx]);
                        float banked = MathF.Min(remaining, edgeRoom);
                        h[edgeIdx] += banked;
                        remaining -= banked;
                    }
                    break;
                }

                float cap = GetMaxHeightForCell(s, idx, maxHeightCap);
                float room = MathF.Max(0f, cap - h[idx]);
                if (room > 0.001f)
                {
                    // Place most into the crest cell and cascade forward as it builds up
                    float take = MathF.Min(remaining * (step == 0 ? 0.68f : 0.85f), room);
                    h[idx] += take;
                    remaining -= take;
                }
            }

            return remaining;
        }

        private void EmitEdgeSpillParticles(DriftSurface s, int edgeSign, float mass)
        {
            float edgeX = edgeSign < 0 ? s.Left - 3.5f : s.Right + 3.5f;
            float edgeY = s.Top - 1.5f;
            float safeY = ClampAboveSyntheticFloor(edgeX, edgeY);
            float leftover = mass;
            int spawned = 0;

            while (leftover >= 0.65f && spawned < 4)
            {
                float unit = MathF.Min(leftover, 1.85f + (float)_rng.NextDouble() * 1.15f);
                leftover -= unit;
                float vx = edgeSign * (16f + (float)_rng.NextDouble() * 34f) + _cursorVx * 0.14f;
                float vy = 16f + (float)_rng.NextDouble() * 34f;
                SpawnDynamicSnowflake(
                    edgeX + ((float)_rng.NextDouble() - 0.5f) * 3f,
                    safeY,
                    vx,
                    vy,
                    cooldown: 0.09f,
                    mass: Math.Clamp(unit / BaseSnowDeposit, 0.32f, 1.25f),
                    isChunk: true,
                    sizeScale: -1f);
                spawned++;
            }
        }

        private void BrushDriftsAlongCursorSegment(float x0, float y0, float x1, float y1)
        {
            if (_kind is ParticleKind.Leaves or ParticleKind.Sakura)
            {
                BrushLeavesAlongCursorSegment(x0, y0, x1, y1);
                return;
            }

            float segMinX = MathF.Min(x0, x1) - BrushRadiusPx;
            float segMaxX = MathF.Max(x0, x1) + BrushRadiusPx;
            float segMinY = MathF.Min(y0, y1) - BrushRadiusPx;
            float segMaxY = MathF.Max(y0, y1) + BrushRadiusPx;

            float dx = x1 - x0;
            float dy = y1 - y0;
            float segLen = MathF.Sqrt(dx * dx + dy * dy);
            int steps = Math.Clamp((int)MathF.Ceiling(segLen / 4.0f), 1, 22);
            float moveDir = MathF.Abs(_cursorVx) > 22f ? MathF.Sign(_cursorVx) : (MathF.Abs(dx) > 0.8f ? MathF.Sign(dx) : 0f);
            float cursorSpeed = MathF.Sqrt(_cursorVx * _cursorVx + _cursorVy * _cursorVy);
            bool sharpFlick = IsSharpCursorFlick(cursorSpeed);
            bool isPressingDown = !sharpFlick && _cursorVy > 24f && MathF.Abs(_cursorVy) > MathF.Abs(_cursorVx) * 0.72f;
            float sculptCap = GetSculptMaxHeightPx();

            for (int i = 0; i < _visibleSurfaces.Count; i++)
            {
                var s = _visibleSurfaces[i];
                if (s.Heights == null || s.MaxHeight <= 0.20f)
                    continue;

                if (s.Right < segMinX || s.Left > segMaxX)
                    continue;
                if (s.Top + 6f < segMinY || s.Top - sculptCap - 6f > segMaxY)
                    continue;

                float radius = s.IsSynthetic ? 10.5f : BrushRadiusPx;
                bool modified = false;
                float ejectedMass = 0f;
                float leftSpillMass = 0f;
                float rightSpillMass = 0f;
                float lastHitX = x1;
                float lastHitY = s.Top;
                int dustSpawned = 0;

                for (int step = 0; step <= steps; step++)
                {
                    float t = steps == 0 ? 1f : (float)step / steps;
                    float px = x0 + dx * t;
                    float py = y0 + dy * t;

                    if (py < s.Top - s.MaxHeight - 7f || py > s.Top + 6f)
                        continue;
                    if (px < s.Left - radius || px > s.Right + radius)
                        continue;

                    int cStart = Math.Clamp((int)MathF.Floor((px - radius - s.Left) / CellWidthPx), 0, s.CellCount - 1);
                    int cEnd = Math.Clamp((int)MathF.Ceiling((px + radius - s.Left) / CellWidthPx), 0, s.CellCount - 1);
                    int frontCell = moveDir != 0f
                        ? (int)MathF.Floor((px + moveDir * (radius * 0.85f + 2.0f) - s.Left) / CellWidthPx)
                        : -1;

                    for (int c = cStart; c <= cEnd; c++)
                    {
                        float h = s.Heights[c];
                        if (h <= 0.12f) continue;

                        float cellCenterX = s.Left + (c + 0.5f) * CellWidthPx;
                        float cellTopY = s.Top - h * 0.5f;
                        float distX = MathF.Abs(cellCenterX - px);
                        float distY = MathF.Abs(cellTopY - py);
                        float distSq = distX * distX + distY * distY;
                        if (distSq > radius * radius) continue;

                        float falloff = 1.0f - MathF.Sqrt(distSq) / radius;
                        float brushFraction = sharpFlick
                            ? (0.50f + 0.42f * falloff)
                            : (isPressingDown ? (0.34f + 0.36f * falloff) : (0.32f + 0.44f * falloff));
                        float disturbed = MathF.Min(h, h * brushFraction);
                        if (disturbed <= 0.04f) continue;

                        s.Heights[c] = h - disturbed;
                        modified = true;
                        lastHitX = cellCenterX;
                        lastHitY = s.Top - s.Heights[c];

                        float remainingShare = disturbed;

                        if (isPressingDown)
                        {
                            // Vertical press compresses and displaces snow out to both sides into a crater + twin ridges
                            int side = cellCenterX < px ? -1 : 1;
                            int startSideCell = Math.Clamp(c + side * 2, 0, s.CellCount - 1);
                            remainingShare = PushSnowIntoWave(s, startSideCell, side, disturbed * 0.86f, sculptCap, allowEdgeSpill: false)
                                           + disturbed * 0.14f;
                        }
                        else if (moveDir != 0f)
                        {
                            // Snowplow: push snow smoothly ahead of the leading edge of the cursor into a mound
                            float speedT = Math.Clamp((cursorSpeed - 180f) / 900f, 0f, 1f);
                            float plowRatio = sharpFlick ? 0.34f : (0.80f - 0.24f * speedT);
                            float plowAmount = disturbed * plowRatio;
                            remainingShare = disturbed - plowAmount;

                            int pushDir = (int)moveDir;
                            int targetStart = pushDir > 0 ? Math.Max(c + 2, frontCell) : Math.Min(c - 2, frontCell);
                            if ((uint)targetStart >= (uint)s.CellCount)
                            {
                                if (!s.IsSynthetic)
                                {
                                    if (pushDir < 0) leftSpillMass += plowAmount;
                                    else rightSpillMass += plowAmount;
                                }
                                else
                                {
                                    float unbanked = PushSnowIntoWave(s, Math.Clamp(targetStart, 0, s.CellCount - 1), -pushDir, plowAmount, sculptCap, allowEdgeSpill: false);
                                    remainingShare += unbanked;
                                }
                            }
                            else
                            {
                                float overflow = PushSnowIntoWave(s, targetStart, pushDir, plowAmount, sculptCap, allowEdgeSpill: !s.IsSynthetic);
                                if (overflow > 0.02f)
                                {
                                    if (!s.IsSynthetic && ((pushDir < 0 && targetStart <= 5) || (pushDir > 0 && targetStart >= s.CellCount - 6)))
                                    {
                                        if (pushDir < 0) leftSpillMass += overflow;
                                        else rightSpillMass += overflow;
                                    }
                                    else
                                    {
                                        remainingShare += overflow;
                                    }
                                }
                            }
                        }

                        // Coat cursor edge gently with powder without draining the drift
                        if (!sharpFlick && remainingShare > 0.02f)
                        {
                            float scoopRequest = remainingShare * 0.42f;
                            float scooped = TryCoatCursorFromDrift(scoopRequest, moveDir);
                            remainingShare -= scooped;
                        }

                        ejectedMass += remainingShare;
                        float spawnUnitThreshold = sharpFlick ? 1.85f : 2.85f;
                        while (ejectedMass >= spawnUnitThreshold)
                        {
                            float unit = MathF.Min(ejectedMass, 1.65f + (float)_rng.NextDouble() * 1.45f);
                            ejectedMass -= unit;
                            float launchVx = Math.Clamp(_cursorVx * 0.30f + ((float)_rng.NextDouble() - 0.5f) * 38f, -220f, 220f);
                            float launchVy = Math.Clamp(_cursorVy * 0.16f - 22f - cursorSpeed * 0.032f - (float)_rng.NextDouble() * 18f, -175f, -14f);
                            SpawnDynamicSnowflake(
                                lastHitX,
                                MathF.Min(s.Top - 2.0f, lastHitY - 1.0f),
                                launchVx,
                                launchVy,
                                cooldown: 0.055f,
                                mass: unit / BaseSnowDeposit,
                                isChunk: true,
                                sizeScale: -1f);

                            if (dustSpawned < 3 && _rng.NextDouble() < 0.42)
                            {
                                SpawnDynamicSnowflake(
                                    lastHitX,
                                    MathF.Min(s.Top - 2.0f, lastHitY - 1.0f),
                                    launchVx * 0.60f + ((float)_rng.NextDouble() - 0.5f) * 34f,
                                    MathF.Min(-14f, launchVy - 10f),
                                    cooldown: 0.055f,
                                    mass: 0.15f,
                                    isChunk: false,
                                    sizeScale: 0.44f);
                                dustSpawned++;
                            }
                        }
                    }
                }

                if (modified)
                {
                    if (leftSpillMass >= 0.65f)
                        EmitEdgeSpillParticles(s, -1, leftSpillMass);
                    if (rightSpillMass >= 0.65f)
                        EmitEdgeSpillParticles(s, 1, rightSpillMass);

                    if (ejectedMass >= 0.75f && (sharpFlick || cursorSpeed > 420f))
                    {
                        float launchVx = Math.Clamp(_cursorVx * 0.28f + ((float)_rng.NextDouble() - 0.5f) * 34f, -200f, 200f);
                        float launchVy = Math.Clamp(_cursorVy * 0.14f - 20f - cursorSpeed * 0.028f, -160f, -12f);
                        SpawnDynamicSnowflake(
                            lastHitX,
                            MathF.Min(s.Top - 2.0f, lastHitY - 1.0f),
                            launchVx,
                            launchVy,
                            cooldown: 0.055f,
                            mass: Math.Clamp(ejectedMass / BaseSnowDeposit, 0.35f, 1.35f),
                            isChunk: true,
                            sizeScale: -1f);
                    }
                    s.NeedsRelaxation = true;
                    RecomputeMaxHeight(s);
                    _persistenceDirty = true;
                }
            }
        }

        private void BrushLeavesAlongCursorSegment(float x0, float y0, float x1, float y1)
        {
            float dx = x1 - x0;
            float dy = y1 - y0;
            float cursorSpeed = MathF.Sqrt(_cursorVx * _cursorVx + _cursorVy * _cursorVy);
            bool liftSwirl = cursorSpeed >= 220f || _cursorAccel > 1700f;
            float radius = liftSwirl ? 16.0f : 13.0f;
            float radiusSq = radius * radius;

            float segMinX = MathF.Min(x0, x1) - radius;
            float segMaxX = MathF.Max(x0, x1) + radius;
            float segMinY = MathF.Min(y0, y1) - radius;
            float segMaxY = MathF.Max(y0, y1) + radius;
            float moveSign = MathF.Abs(_cursorVx) > 20f ? MathF.Sign(_cursorVx) : (MathF.Abs(dx) > 0.4f ? MathF.Sign(dx) : 0f);

            for (int i = 0; i < _visibleSurfaces.Count; i++)
            {
                var s = _visibleSurfaces[i];
                var leaves = s.Leaves;
                if (leaves == null || leaves.Count == 0)
                    continue;

                if (s.Right < segMinX || s.Left > segMaxX)
                    continue;
                if (s.Top + 8f < segMinY || s.Top - _snowMaxHeightPx - 10f > segMaxY)
                    continue;

                float cornerPadL = MathF.Max(3.0f, s.CornerRadiusLeft * 0.35f);
                float cornerPadR = MathF.Max(3.0f, s.CornerRadiusRight * 0.35f);
                float maxRelX = MathF.Max(cornerPadL, s.Width - cornerPadR);
                bool modified = false;

                for (int j = leaves.Count - 1; j >= 0; j--)
                {
                    var leaf = leaves[j];
                    float wx = s.Left + leaf.RelX;
                    float wy = s.Top - leaf.OffsetY - 2.0f;

                    // Closest point on cursor segment to leaf center
                    float segLenSq = dx * dx + dy * dy;
                    float t = segLenSq > 0.001f ? Math.Clamp(((wx - x0) * dx + (wy - y0) * dy) / segLenSq, 0f, 1f) : 1f;
                    float cx = x0 + dx * t;
                    float cy = y0 + dy * t;

                    float ddx = wx - cx;
                    float ddy = wy - cy;
                    float distSq = ddx * ddx + ddy * ddy;
                    if (distSq > radiusSq)
                        continue;

                    float falloff = 1.0f - MathF.Sqrt(distSq) / radius;

                    if (liftSwirl)
                    {
                        leaves.RemoveAt(j);
                        modified = true;

                        float sideSplash = MathF.Abs(ddx) > 0.5f ? MathF.Sign(ddx) * 32f : ((float)_rng.NextDouble() - 0.5f) * 64f;
                        float launchVx = Math.Clamp(_cursorVx * 0.44f + (ddx * 3.4f) + sideSplash + ((float)_rng.NextDouble() - 0.5f) * 85f, -260f, 260f);
                        float launchVy = Math.Clamp(_cursorVy * 0.18f - 68f - cursorSpeed * 0.09f - (float)_rng.NextDouble() * 54f, -240f, -28f);
                        SpawnDynamicLeafFromSettled(in leaf, wx, MathF.Min(s.Top - 2.5f, wy), launchVx, launchVy, cooldown: 0.045f);
                    }
                    else
                    {
                        // Gentle touch: glide, tilt, and give a soft upward hop + neighbor ripple
                        float pushX = (MathF.Abs(dx) > 0.1f ? dx * 0.65f : moveSign * 1.4f) * falloff;
                        if (MathF.Abs(pushX) < 0.05f && MathF.Abs(ddx) > 0.2f)
                            pushX = MathF.Sign(ddx) * 0.6f * falloff;

                        leaf.RelX += pushX;
                        leaf.Vx = Math.Clamp(leaf.Vx + pushX * 26f, -135f, 135f);
                        leaf.Vy = Math.Max(leaf.Vy, (14f + (float)_rng.NextDouble() * 16f) * falloff);
                        leaf.Rotation = Math.Clamp(leaf.Rotation + (pushX >= 0f ? 1f : -1f) * falloff * 0.15f, -MathF.PI, MathF.PI);
                        leaf.AngularVel = Math.Clamp(leaf.AngularVel + (pushX >= 0f ? 1f : -1f) * falloff * 2.2f, -5.0f, 5.0f);
                        leaf.IsSettling = true;

                        // Ripple softly to adjacent leaves in stack
                        for (int nIdx = 0; nIdx < leaves.Count; nIdx++)
                        {
                            if (nIdx == j) continue;
                            var nb = leaves[nIdx];
                            float relDist = nb.RelX - leaf.RelX;
                            if (MathF.Abs(relDist) < 14f)
                            {
                                nb.Vx = Math.Clamp(nb.Vx + pushX * 8f + MathF.Sign(relDist) * 12f, -95f, 95f);
                                nb.Vy = Math.Max(nb.Vy, 8f * falloff);
                                nb.AngularVel = Math.Clamp(nb.AngularVel + (pushX >= 0f ? 1f : -1f) * 1.1f, -4.0f, 4.0f);
                                nb.IsSettling = true;
                                leaves[nIdx] = nb;
                            }
                        }

                        // If nudged off card edge, it slips off and flutters down
                        if (!s.IsSynthetic && (leaf.RelX < cornerPadL - 2.0f || leaf.RelX > maxRelX + 2.0f))
                        {
                            leaves.RemoveAt(j);
                            modified = true;
                            float edgeVx = Math.Clamp((leaf.RelX < cornerPadL ? -42f : 42f) + _cursorVx * 0.25f, -160f, 160f);
                            SpawnDynamicLeafFromSettled(in leaf, wx, MathF.Min(s.Top - 2.5f, wy), edgeVx, -16f, cooldown: 0.045f);
                        }
                        else
                        {
                            leaf.RelX = Math.Clamp(leaf.RelX, cornerPadL, maxRelX);
                            leaves[j] = leaf;
                            modified = true;
                        }
                    }
                }

                if (modified)
                    _persistenceDirty = true;
            }
        }

        private void SpawnDynamicLeafFromSettled(in SettledLeaf leaf, float x, float y, float vx, float vy, float cooldown)
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
            p.Vx = Math.Clamp(vx, -480f, 480f);
            p.Vy = Math.Clamp(vy, -420f, 420f);
            p.SpeedScale = 0.78f + (float)_rng.NextDouble() * 0.45f;
            p.Mass = 1.0f;
            p.IsChunk = false;
            p.SizeScale = leaf.Scale;
            p.SpinRate = (float)((_rng.NextDouble() - 0.5) * 6.5);
            p.AlphaScale = Math.Clamp(leaf.Alpha / 255f, 0.15f, 1.0f);
            p.Phase = (float)(_rng.NextDouble() * Math.PI * 2.0);
            p.SwayFreq = 1.1f + (float)_rng.NextDouble() * 1.4f;
            p.SwayAmp = 10.0f + (float)_rng.NextDouble() * 16.0f;
            p.Angle = leaf.Rotation;
            p.Cooldown = cooldown;
            p.PaletteIndex = leaf.PaletteIndex;
            p.SpriteIndex = leaf.SpriteIndex;
            p.IsDynamic = true;
            p.IsActive = true;
        }

        private void ShedSnowFromSurface(DriftSurface s, float fraction, float extraVx, float extraVy)
        {
            if (s.Leaves is { Count: > 0 } && _kind is ParticleKind.Leaves or ParticleKind.Sakura)
            {
                bool fullLeafClear = fraction >= 0.99f;
                int shedCount = 0;
                for (int j = s.Leaves.Count - 1; j >= 0; j--)
                {
                    if (!fullLeafClear && _rng.NextDouble() > fraction) continue;
                    var leaf = s.Leaves[j];
                    s.Leaves.RemoveAt(j);
                    if (shedCount < 28)
                    {
                        SpawnDynamicLeafFromSettled(
                            in leaf,
                            s.Left + leaf.RelX,
                            s.Top - leaf.OffsetY,
                            extraVx + ((float)_rng.NextDouble() - 0.5f) * 45f,
                            MathF.Max(14f, extraVy) + (float)_rng.NextDouble() * 28f,
                            cooldown: 0.16f);
                        shedCount++;
                    }
                }
                if (fullLeafClear) s.Leaves = null;
                _persistenceDirty = true;
            }

            if (s.Heights == null || s.MaxHeight <= 0.20f)
                return;

            float accum = 0f;
            int spawned = 0;
            int dustSpawned = 0;
            const int maxPerShed = 28;
            bool fullClear = fraction >= 0.99f;

            for (int c = 0; c < s.CellCount; c++)
            {
                float h = s.Heights[c];
                if (h <= 0.12f) continue;

                float take = fullClear ? h : h * fraction;
                s.Heights[c] = fullClear ? 0f : h - take;
                accum += take;

                if (accum >= 1.8f && spawned < maxPerShed)
                {
                    float unit = MathF.Min(accum, 1.85f + (float)_rng.NextDouble() * 1.45f);
                    accum -= unit;
                    float cellX = s.Left + (c + 0.5f) * CellWidthPx;
                    float cellY = s.Top - h * 0.5f;
                    float vx = extraVx + ((float)_rng.NextDouble() - 0.5f) * 36f;
                    float vy = MathF.Max(14f, extraVy) + (float)_rng.NextDouble() * 32f;
                    bool shedAsChunk = _kind == ParticleKind.Snow;
                    SpawnDynamicSnowflake(
                        cellX,
                        cellY,
                        vx,
                        vy,
                        cooldown: 0.16f,
                        mass: Math.Clamp(unit / BaseSnowDeposit, 0.45f, 1.45f),
                        isChunk: shedAsChunk,
                        sizeScale: shedAsChunk ? -1f : (0.75f + (float)_rng.NextDouble() * 0.40f));
                    spawned++;

                    if (shedAsChunk && dustSpawned < 3 && _rng.NextDouble() < 0.30)
                    {
                        SpawnDynamicSnowflake(
                            cellX,
                            cellY,
                            vx * 0.72f + ((float)_rng.NextDouble() - 0.5f) * 55f,
                            vy - 14f,
                            cooldown: 0.18f,
                            mass: 0.18f,
                            isChunk: false,
                            sizeScale: 0.45f);
                        dustSpawned++;
                    }
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

        private void SpawnDynamicSnowflake(
            float x,
            float y,
            float vx,
            float vy,
            float cooldown,
            float mass = 1.0f,
            bool isChunk = false,
            float sizeScale = -1f)
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
            p.Vx = Math.Clamp(vx, -480f, 480f);
            p.Vy = Math.Clamp(vy, -420f, 420f);
            p.SpeedScale = 0.80f + (float)_rng.NextDouble() * 0.50f;
            float clampedMass = Math.Clamp(mass, 0.18f, 1.85f);
            p.Mass = clampedMass;
            p.IsChunk = isChunk;
            if (isChunk)
            {
                p.SizeScale = sizeScale > 0f
                    ? sizeScale
                    : Math.Clamp(0.68f + (clampedMass - 0.40f) * 0.52f + (float)_rng.NextDouble() * 0.30f, 0.60f, 1.55f);
                p.SpinRate = (float)((_rng.NextDouble() - 0.5) * 9.5);
                p.AlphaScale = 0.92f + (float)_rng.NextDouble() * 0.08f;
            }
            else
            {
                p.SizeScale = sizeScale > 0f ? sizeScale : 0.42f + (float)_rng.NextDouble() * 0.30f;
                p.SpinRate = (float)((_rng.NextDouble() - 0.5) * 6.5);
                p.AlphaScale = 0.72f + (float)_rng.NextDouble() * 0.24f;
            }
            p.Phase = (float)(_rng.NextDouble() * Math.PI * 2.0);
            p.SwayFreq = 1.1f + (float)_rng.NextDouble() * 1.4f;
            p.SwayAmp = 8.0f + (float)_rng.NextDouble() * 14.0f;
            p.Angle = (float)(_rng.NextDouble() * Math.PI * 2.0);
            p.Cooldown = cooldown;
            p.PaletteIndex = (byte)_rng.Next(0, SnowColors.Length);
            p.SpriteIndex = (byte)_rng.Next(0, 12);
            p.IsDynamic = true;
            p.IsActive = true;
        }

        private void UpdateCursorKinematics(float dt)
        {
            _prevCursorVx = _cursorVx;
            _prevCursorVy = _cursorVy;

            if (_bridge.TryReadPointer(ref _lastCursorVersion, out float nx, out float ny, out bool active, out bool resetVelocity))
            {
                if (!active)
                {
                    _cursorActive = false;
                    _hasPrevCursor = false;
                    _cursorVx = 0f;
                    _cursorVy = 0f;
                    _prevCursorVx = 0f;
                    _prevCursorVy = 0f;
                    _cursorAccel = 0f;
                    _cursorShakeEnergy = 0f;
                    _suppressCursorStepThisFrame = true;
                }
                else
                {
                    float jumpDistSq = (_cursorActive && _hasPrevCursor)
                        ? ((nx - _cursorX) * (nx - _cursorX) + (ny - _cursorY) * (ny - _cursorY))
                        : 0f;

                    if (resetVelocity || !_cursorActive || !_hasPrevCursor || jumpDistSq > (180f * 180f))
                    {
                        _prevCursorX = nx;
                        _prevCursorY = ny;
                        _cursorVx = 0f;
                        _cursorVy = 0f;
                        _prevCursorVx = 0f;
                        _prevCursorVy = 0f;
                        _cursorAccel = 0f;
                        _cursorShakeEnergy = 0f;
                        _hasPrevCursor = true;
                        _suppressCursorStepThisFrame = true;
                    }
                    else
                    {
                        _prevCursorX = _cursorX;
                        _prevCursorY = _cursorY;
                        float instVx = (nx - _cursorX) / dt;
                        float instVy = (ny - _cursorY) / dt;
                        float newVx = _cursorVx * 0.56f + Math.Clamp(instVx, -1350f, 1350f) * 0.44f;
                        float newVy = _cursorVy * 0.56f + Math.Clamp(instVy, -1350f, 1350f) * 0.44f;
                        float instAx = (newVx - _cursorVx) / MathF.Max(dt, 0.004f);
                        float instAy = (newVy - _cursorVy) / MathF.Max(dt, 0.004f);
                        float instAccel = MathF.Sqrt(instAx * instAx + instAy * instAy);
                        _cursorAccel = _cursorAccel * 0.72f + Math.Clamp(instAccel, 0f, 14000f) * 0.28f;

                        // Detect deliberate vigorous back-and-forth shake (direction reversal at speed)
                        float prevSpdSq = _cursorVx * _cursorVx + _cursorVy * _cursorVy;
                        float curSpdSq = newVx * newVx + newVy * newVy;
                        float dot = _cursorVx * newVx + _cursorVy * newVy;
                        if (prevSpdSq > 360f * 360f && curSpdSq > 360f * 360f && dot < -0.20f * MathF.Sqrt(prevSpdSq * curSpdSq))
                        {
                            _cursorShakeEnergy = MathF.Min(2.2f, _cursorShakeEnergy + 0.65f);
                        }
                        else
                        {
                            _cursorShakeEnergy *= MathF.Exp(-4.5f * dt);
                        }

                        _cursorVx = newVx;
                        _cursorVy = newVy;
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
                _cursorAccel *= decay;
                _cursorShakeEnergy *= MathF.Exp(-4.5f * dt);
            }
        }

        private void SyncSurfacesFromBridge()
        {
            if (!_bridge.TryCopySnapshots(ref _localSnapshots, out int newCount, ref _lastSnapshotVersion))
                return;

            _localSnapshotCount = newCount;

            if (_awaitingAtomicSwap)
            {
                bool hasTargetVisible = false;
                for (int i = 0; i < newCount; i++)
                {
                    ref readonly var snap = ref _localSnapshots[i];
                    if (snap.IsVisible && string.Equals(snap.PageKey, _activePageKey, StringComparison.Ordinal))
                    {
                        hasTargetVisible = true;
                        break;
                    }
                }

                if (hasTargetVisible || Stopwatch.GetElapsedTime(_pageSwitchTicks).TotalMilliseconds >= 95.0)
                {
                    _awaitingAtomicSwap = false;
                }
            }

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
                    if (_tabTransitionGraceSec <= 0f
                        && s.IsVisibleInFrame
                        && s.HasSnow
                        && !s.IsSynthetic
                        && !s.HasClipRect
                        && snap.IsScopeActive
                        && IsPageKeyActiveForShedding(s.PageKey))
                    {
                        ShedSnowFromSurface(s, 1.0f, 0f, 15f);
                    }
                    s.IsVisibleInFrame = false;
                    s.HasBeenPositioned = false;
                    s.FadeOpacity = 0.0f;
                    s.VisitedThisFrame = true;
                    continue;
                }

                float width = MathF.Max(8f, snap.Right - snap.Left);
                EnsureSurfaceWidth(s, width);

                if (s.PendingSavedBytes != null)
                {
                    UnpackBytesIntoSurface(s, s.PendingSavedBytes, s.PendingSavedVersion);
                    s.PendingSavedBytes = null;
                }
                if (s.PendingSavedLeaves != null)
                {
                    UnpackLeavesIntoSurface(s, s.PendingSavedLeaves);
                    s.PendingSavedLeaves = null;
                }

                if (s.HasBeenPositioned && _tabTransitionGraceSec <= 0f && !s.IsSynthetic)
                {
                    float dx = snap.Left - s.PrevLeft;
                    float dy = snap.Top - s.PrevTop;
                    float scrollDx = snap.ScrollOffsetX - s.PrevScrollX;
                    float scrollDy = snap.ScrollOffsetY - s.PrevScrollY;

                    bool isScrolling = MathF.Abs(scrollDx) > 1.5f || MathF.Abs(scrollDy) > 1.5f;
                    float layoutDx = dx + scrollDx;
                    float layoutDy = dy + scrollDy;

                    // Violent layout shift inside the active tab sheds part of pile (never during scroll or tab swap)
                    float layoutJump = MathF.Max(MathF.Abs(layoutDx), MathF.Abs(layoutDy));
                    if (!isScrolling && s.HasSnow && (layoutJump > 22.0f || MathF.Abs(dy) > 60.0f))
                    {
                        float shakeFraction = Math.Clamp((MathF.Max(layoutJump, MathF.Abs(dy)) - 18f) / 65f, 0.25f, 0.72f);
                        float shedVx = Math.Clamp(-dx * 0.55f, -110f, 110f);
                        float shedVy = Math.Clamp(-dy * 0.28f, 14f, 90f);
                        ShedSnowFromSurface(s, shakeFraction, shedVx, shedVy);
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
                if (!s.IsVisibleInFrame && !s.IsSynthetic)
                    s.FadeOpacity = 0.0f;
                s.IsVisibleInFrame = true;
                s.HasBeenPositioned = true;
                s.VisitedThisFrame = true;

                _visibleSurfaces.Add(s);
            }

            // Surfaces that disappeared completely from snapshots (detached / deactivated)
            for (int i = 0; i < _prevVisibleSurfaces.Count; i++)
            {
                var s = _prevVisibleSurfaces[i];
                if (s.VisitedThisFrame || s.IsSynthetic) continue;

                if (_tabTransitionGraceSec <= 0f
                    && !s.HasClipRect
                    && s.IsVisibleInFrame
                    && s.HasSnow
                    && IsPageKeyActiveForShedding(s.PageKey))
                {
                    ShedSnowFromSurface(s, 1.0f, 0f, 15f);
                }

                s.IsVisibleInFrame = false;
                s.HasBeenPositioned = false;
                s.FadeOpacity = 0.0f;
            }
        }

        private void EnsureBottomEdgeSurfaceSync()
        {
            const string bottomPage = "Chrome";
            const string bottomId = "window_bottom_edge";

            if (!_snowBottomAccumulationEnabled || _width < 12f || _height < 24f)
            {
                for (int i = _visibleSurfaces.Count - 1; i >= 0; i--)
                {
                    var s = _visibleSurfaces[i];
                    if (string.Equals(s.PageKey, bottomPage, StringComparison.Ordinal)
                        && string.Equals(s.SurfaceId, bottomId, StringComparison.Ordinal))
                    {
                        s.IsVisibleInFrame = false;
                        _visibleSurfaces.RemoveAt(i);
                    }
                }
                return;
            }

            var bottomSurface = GetOrCreateSurface(bottomPage, bottomId);
            EnsureSurfaceWidth(bottomSurface, _width);

            if (bottomSurface.PendingSavedBytes != null)
            {
                UnpackBytesIntoSurface(bottomSurface, bottomSurface.PendingSavedBytes, bottomSurface.PendingSavedVersion);
                bottomSurface.PendingSavedBytes = null;
            }
            if (bottomSurface.PendingSavedLeaves != null)
            {
                UnpackLeavesIntoSurface(bottomSurface, bottomSurface.PendingSavedLeaves);
                bottomSurface.PendingSavedLeaves = null;
            }

            bottomSurface.Left = 0f;
            bottomSurface.Top = _height - 1.5f;
            bottomSurface.Right = _width;
            bottomSurface.Bottom = _height;
            bottomSurface.PrevLeft = bottomSurface.Left;
            bottomSurface.PrevTop = bottomSurface.Top;
            bottomSurface.PrevRight = bottomSurface.Right;
            bottomSurface.PrevBottom = bottomSurface.Bottom;
            bottomSurface.HasClipRect = false;
            bottomSurface.CornerRadiusLeft = 8f;
            bottomSurface.CornerRadiusRight = 8f;
            bottomSurface.IsSynthetic = true;
            bottomSurface.IsVisibleInFrame = true;
            bottomSurface.HasBeenPositioned = true;
            bottomSurface.VisitedThisFrame = true;

            bool present = false;
            for (int i = 0; i < _visibleSurfaces.Count; i++)
            {
                if (ReferenceEquals(_visibleSurfaces[i], bottomSurface))
                {
                    present = true;
                    break;
                }
            }
            if (!present)
                _visibleSurfaces.Add(bottomSurface);
        }

        private void ClampAllSurfacesToMaxHeight()
        {
            foreach (var pageDict in ActiveDriftStore.Values)
            {
                foreach (var s in pageDict.Values)
                {
                    var h = s.Heights;
                    if (h != null && s.CellCount > 0)
                    {
                        bool changed = false;
                        float clampCap = GetSculptMaxHeightPx();
                        for (int i = 0; i < s.CellCount; i++)
                        {
                            float cap = GetMaxHeightForCell(s, i, clampCap);
                            if (h[i] > cap)
                            {
                                h[i] = cap;
                                changed = true;
                            }
                        }
                        if (changed)
                        {
                            RecomputeMaxHeight(s);
                            s.NeedsRelaxation = true;
                            _persistenceDirty = true;
                        }
                    }

                    if (s.Leaves is { Count: > 0 })
                    {
                        float maxStack = MathF.Max(2.0f, _snowMaxHeightPx * 0.92f);
                        for (int j = s.Leaves.Count - 1; j >= 0; j--)
                        {
                            if (s.Leaves[j].OffsetY > maxStack)
                            {
                                s.Leaves.RemoveAt(j);
                                _persistenceDirty = true;
                            }
                        }
                    }
                }
            }
        }

        private bool IsPageKeyActiveForShedding(string pageKey)
        {
            if (string.Equals(pageKey, "Chrome", StringComparison.Ordinal))
                return true;
            if (string.Equals(pageKey, "LaunchBar", StringComparison.Ordinal))
                return string.Equals(_activePageKey, "Profiles", StringComparison.Ordinal)
                    || _activePageKey.StartsWith("Catalog:", StringComparison.Ordinal)
                    || string.Equals(_activePageKey, "Modpacks", StringComparison.Ordinal);
            return string.Equals(pageKey, _activePageKey, StringComparison.Ordinal);
        }

        private DriftSurface GetOrCreateSurface(string pageKey, string surfaceId)
        {
            return GetOrCreateSurface(ActiveDriftStore, pageKey, surfaceId);
        }

        private static DriftSurface GetOrCreateSurface(
            Dictionary<string, Dictionary<string, DriftSurface>> store,
            string pageKey,
            string surfaceId)
        {
            if (!store.TryGetValue(pageKey, out var pageDict))
            {
                pageDict = new Dictionary<string, DriftSurface>(StringComparer.Ordinal);
                store[pageKey] = pageDict;
            }

            if (!pageDict.TryGetValue(surfaceId, out var s))
            {
                s = new DriftSurface
                {
                    PageKey = pageKey,
                    SurfaceId = surfaceId,
                    SurfaceIdHash = surfaceId.GetHashCode(StringComparison.Ordinal)
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

            float oldWidth = s.Width;
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

            if (s.Leaves is { Count: > 0 } && oldWidth > 8f)
            {
                float ratio = width / oldWidth;
                for (int i = 0; i < s.Leaves.Count; i++)
                {
                    var leaf = s.Leaves[i];
                    leaf.RelX = Math.Clamp(leaf.RelX * ratio, 2f, MathF.Max(2f, width - 2f));
                    s.Leaves[i] = leaf;
                }
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

        private static float GetMaxHeightForCell(DriftSurface s, int cell, float maxDriftHeightPx)
        {
            int n = s.CellCount;
            if (n <= 2) return maxDriftHeightPx * 0.55f;

            float xLeft = (cell + 0.5f) * CellWidthPx;
            float xRight = s.Width - xLeft;

            float leftCap = 1.0f;
            float crL = MathF.Max(5.0f, s.CornerRadiusLeft * 0.80f);
            if (xLeft < crL)
                leftCap = MathF.Pow(Math.Clamp(xLeft / crL, 0f, 1f), 1.15f) * 0.92f;

            float rightCap = 1.0f;
            float crR = MathF.Max(5.0f, s.CornerRadiusRight * 0.80f);
            if (xRight < crR)
                rightCap = MathF.Pow(Math.Clamp(xRight / crR, 0f, 1f), 1.15f) * 0.92f;

            return maxDriftHeightPx * MathF.Min(leftCap, rightCap);
        }

        private static void RelaxLocal(DriftSurface s, int centerCell, float maxDriftHeightPx)
        {
            var h = s.Heights;
            if (h == null) return;
            int lo = Math.Max(0, centerCell - 6);
            int hi = Math.Min(s.CellCount - 1, centerCell + 6);

            for (int pass = 0; pass < 2; pass++)
            {
                bool reverse = (pass & 1) == 1;
                int start = reverse ? hi - 1 : lo;
                int end = reverse ? lo - 1 : hi;
                int step = reverse ? -1 : 1;

                for (int i = start; i != end; i += step)
                {
                    float diff = h[i] - h[i + 1];
                    float slopeLimit = GetEffectiveSlopeLimit(h[i], h[i + 1]);
                    if (diff > slopeLimit)
                    {
                        float move = (diff - slopeLimit) * 0.34f;
                        float cap = GetMaxHeightForCell(s, i + 1, maxDriftHeightPx);
                        float actual = MathF.Min(move, MathF.Max(0f, cap - h[i + 1]));
                        h[i] -= actual;
                        h[i + 1] += actual;
                    }
                    else if (-diff > slopeLimit)
                    {
                        float move = (-diff - slopeLimit) * 0.34f;
                        float cap = GetMaxHeightForCell(s, i, maxDriftHeightPx);
                        float actual = MathF.Min(move, MathF.Max(0f, cap - h[i]));
                        h[i + 1] -= actual;
                        h[i] += actual;
                    }
                }
            }
            RecomputeMaxHeight(s);
        }

        private void RelaxSurface(DriftSurface s)
        {
            var h = s.Heights;
            if (h == null || s.CellCount < 2)
            {
                s.NeedsRelaxation = false;
                return;
            }

            float maxDriftHeightPx = GetSculptMaxHeightPx();
            bool movedAny = false;
            bool reverse = _relaxReversePass;
            _relaxReversePass = !_relaxReversePass;

            int start = reverse ? s.CellCount - 2 : 0;
            int end = reverse ? -1 : s.CellCount - 1;
            int step = reverse ? -1 : 1;

            for (int i = start; i != end; i += step)
            {
                float diff = h[i] - h[i + 1];
                float slopeLimit = GetEffectiveSlopeLimit(h[i], h[i + 1]);
                if (diff > slopeLimit)
                {
                    float move = (diff - slopeLimit) * 0.32f;
                    float cap = GetMaxHeightForCell(s, i + 1, maxDriftHeightPx);
                    float actual = MathF.Min(move, MathF.Max(0f, cap - h[i + 1]));
                    if (actual > 0.02f)
                    {
                        h[i] -= actual;
                        h[i + 1] += actual;
                        movedAny = true;
                    }
                }
                else if (-diff > slopeLimit)
                {
                    float move = (-diff - slopeLimit) * 0.32f;
                    float cap = GetMaxHeightForCell(s, i, maxDriftHeightPx);
                    float actual = MathF.Min(move, MathF.Max(0f, cap - h[i]));
                    if (actual > 0.02f)
                    {
                        h[i + 1] -= actual;
                        h[i] += actual;
                        movedAny = true;
                    }
                }
            }

            // Spill excess edge snow over elevated card corners instead of vanishing into flat water
            if (s.CellCount > 0)
            {
                float cap0 = GetMaxHeightForCell(s, 0, maxDriftHeightPx);
                if (h[0] > cap0)
                {
                    float excess0 = h[0] - cap0;
                    h[0] = cap0;
                    if (!s.IsSynthetic && excess0 > 0.45f)
                        EmitEdgeSpillParticles(s, -1, excess0);
                }

                int last = s.CellCount - 1;
                float capLast = GetMaxHeightForCell(s, last, maxDriftHeightPx);
                if (h[last] > capLast)
                {
                    float excessLast = h[last] - capLast;
                    h[last] = capLast;
                    if (!s.IsSynthetic && excessLast > 0.45f)
                        EmitEdgeSpillParticles(s, 1, excessLast);
                }
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

        private bool IsSurfaceVisibleOnCurrentPage(string pageKey)
        {
            if (string.Equals(pageKey, "Chrome", StringComparison.Ordinal))
                return true;
            if (string.Equals(pageKey, "LaunchBar", StringComparison.Ordinal))
                return string.Equals(_activePageKey, "Profiles", StringComparison.Ordinal)
                    || _activePageKey.StartsWith("Catalog:", StringComparison.Ordinal)
                    || string.Equals(_activePageKey, "Modpacks", StringComparison.Ordinal);
            if (string.Equals(pageKey, "Settings:Sidebar", StringComparison.Ordinal))
                return _activePageKey.StartsWith("Settings:", StringComparison.Ordinal);
            return string.Equals(pageKey, _activePageKey, StringComparison.Ordinal);
        }

        private void RenderSnowDrifts(SKCanvas canvas)
        {
            if (_visibleSurfaces.Count == 0) return;

            _driftPaint ??= new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
            _driftPath ??= new SKPath();
            _driftInnerPath ??= new SKPath();

            var paint = _driftPaint;
            float baseOpacityFactor = 0.80f + _opacity * 0.20f;

            for (int i = 0; i < _visibleSurfaces.Count; i++)
            {
                var s = _visibleSurfaces[i];
                var h = s.Heights;
                if (h == null || s.MaxHeight < 0.25f || s.CellCount <= 0)
                    continue;

                if (!s.IsSynthetic && !IsSurfaceVisibleOnCurrentPage(s.PageKey))
                    continue;

                float surfaceOpacity = baseOpacityFactor * (s.IsSynthetic ? 1.0f : s.FadeOpacity);
                if (surfaceOpacity < 0.02f)
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

                float baseY = s.Top + 1.5f;
                int n = s.CellCount;
                float cornerPadL = MathF.Max(3.0f, s.CornerRadiusLeft * 0.35f);
                float cornerPadR = MathF.Max(3.0f, s.CornerRadiusRight * 0.35f);
                float clampLeft = s.Left + cornerPadL;
                float clampRight = s.Right - cornerPadR;

                int c = 0;
                while (c < n)
                {
                    if (h[c] < 0.20f)
                    {
                        c++;
                        continue;
                    }

                    int runStart = c;
                    float peakH = h[c];
                    int runEnd = c;
                    while (runEnd + 1 < n && h[runEnd + 1] >= 0.20f)
                    {
                        runEnd++;
                        if (h[runEnd] > peakH) peakH = h[runEnd];
                    }

                    // Skip tiny isolated specks below visible threshold
                    if (runEnd - runStart >= 1 || peakH >= 0.45f)
                    {
                        DrawSmoothSnowPill(
                            canvas,
                            paint,
                            h,
                            runStart,
                            runEnd,
                            s.Left,
                            CellWidthPx,
                            clampLeft,
                            clampRight,
                            baseY,
                            peakH,
                            surfaceOpacity);
                    }

                    c = runEnd + 1;
                }

                if (clipped)
                {
                    canvas.Restore();
                }
            }
        }

        private void RenderCursorSnowCap(SKCanvas canvas)
        {
            if (!_snowCursorInteractionEnabled || !_cursorActive || _cursorPixelMax < 0.14f)
                return;

            EnsureSnowPuffTexture();
            if (_snowPuffImage == null) return;

            _driftPaint ??= new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
            var paint = _driftPaint;
            var img = _snowPuffImage;
            float baseOpacityFactor = 0.88f + _opacity * 0.12f;

            for (int i = 0; i < CursorPixelSlots; i++)
            {
                float h = _cursorPixelSnow[i];
                if (h < 0.12f) continue;

                float t = Math.Clamp(h / CursorPixelMaxSnow, 0.15f, 1.0f);
                float r = 1.05f + t * 1.20f;
                float px = _cursorX + CursorSlotOx[i];
                float py = _cursorY + CursorSlotOy[i] - h * 0.25f;
                byte a = (byte)Math.Clamp((int)MathF.Round(baseOpacityFactor * (0.68f + 0.32f * t) * 255f), 120, 255);

                paint.Color = new SKColor(255, 255, 255, a);
                canvas.DrawImage(img, new SKRect(px - r, py - r, px + r, py + r), LinearSampling, paint);
            }
        }

        private void EnsureSnowPuffTexture()
        {
            if (_snowPuffImage != null) return;

            using var bmp = new SKBitmap(new SKImageInfo(48, 48, SKColorType.Bgra8888, SKAlphaType.Premul));
            using var c = new SKCanvas(bmp);
            c.Clear(SKColors.Transparent);

            using var shader = SKShader.CreateRadialGradient(
                new SKPoint(24f, 24f),
                22f,
                [
                    new SKColor(253, 254, 255, 255),
                    new SKColor(250, 253, 255, 244),
                    new SKColor(238, 247, 255, 132),
                    new SKColor(224, 240, 255, 0)
                ],
                [0.0f, 0.46f, 0.74f, 1.0f],
                SKShaderTileMode.Clamp);

            using var puffPaint = new SKPaint
            {
                IsAntialias = true,
                Shader = shader
            };
            c.DrawCircle(24f, 24f, 22f, puffPaint);
            _snowPuffImage = SKImage.FromBitmap(bmp);
        }

        private void RenderSnowChunks(SKCanvas canvas)
        {
            EnsureSnowPuffTexture();
            if (_snowPuffImage == null) return;

            _driftPaint ??= new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
            var paint = _driftPaint;
            var img = _snowPuffImage;
            float baseOpacityFactor = 0.88f + _opacity * 0.12f;
            float sizeScaleGlobal = 0.86f + 0.22f * Math.Clamp(_size, 0.5f, 1.8f);

            for (int i = MaxParticles; i < TotalParticleSlots; i++)
            {
                ref readonly var p = ref _items[i];
                if (!p.IsActive)
                    continue;

                byte alpha = (byte)Math.Clamp((int)MathF.Round(baseOpacityFactor * p.AlphaScale * 255f), 115, 255);

                if (p.IsChunk)
                {
                    float r = Math.Clamp(p.SizeScale * 3.1f * sizeScaleGlobal, 1.8f, 4.9f);
                    float rx = r * (1.0f + 0.16f * MathF.Abs(MathF.Sin(p.Phase * 1.7f)));
                    float subR1 = r * 0.68f;
                    float subR2 = r * 0.56f;

                    canvas.Save();
                    canvas.Translate(p.X, p.Y);
                    canvas.RotateRadians(p.Angle);

                    // Granular multi-lobe snow clump instead of liquid drop trail
                    paint.Color = new SKColor(238, 247, 255, (byte)(alpha * 0.85f));
                    canvas.DrawImage(img, new SKRect(-rx * 0.65f - subR1, -r * 0.28f - subR1, -rx * 0.65f + subR1, -r * 0.28f + subR1), LinearSampling, paint);
                    canvas.DrawImage(img, new SKRect(rx * 0.55f - subR2, r * 0.25f - subR2, rx * 0.55f + subR2, r * 0.25f + subR2), LinearSampling, paint);

                    paint.Color = new SKColor(255, 255, 255, alpha);
                    canvas.DrawImage(img, new SKRect(-rx, -r, rx, r), LinearSampling, paint);
                    canvas.Restore();
                }
                else
                {
                    // Tiny crisp white snow dust specks alongside clumps
                    float r = Math.Clamp(p.SizeScale * 2.4f, 1.0f, 1.85f);
                    byte dustAlpha = (byte)(alpha * 0.85f);
                    paint.Color = new SKColor(255, 255, 255, dustAlpha);
                    canvas.DrawImage(img, new SKRect(p.X - r, p.Y - r, p.X + r, p.Y + r), LinearSampling, paint);
                }
            }
        }

        private void DrawSmoothSnowPill(
            SKCanvas canvas,
            SKPaint paint,
            float[] heights,
            int runStart,
            int runEnd,
            float originLeft,
            float cellW,
            float clampLeft,
            float clampRight,
            float baseY,
            float peakHeight,
            float baseOpacityFactor)
        {
            float xStart = MathF.Max(clampLeft, originLeft + runStart * cellW - cellW * 0.35f);
            float xEnd = MathF.Min(clampRight, originLeft + (runEnd + 1) * cellW + cellW * 0.35f);
            if (xEnd - xStart < 4.0f) return;

            var outer = _driftPath!;
            var inner = _driftInnerPath!;
            outer.Reset();
            inner.Reset();

            BuildSmoothRidgePath(outer, heights, runStart, runEnd, originLeft, cellW, xStart, xEnd, baseY, heightOffset: 0f);
            bool hasInner = peakHeight > 1.55f;
            if (hasInner)
            {
                BuildSmoothRidgePath(inner, heights, runStart, runEnd, originLeft, cellW, xStart, xEnd, baseY - 0.5f, heightOffset: 1.15f);
            }

            float thinFade = Math.Clamp(peakHeight / 1.45f, 0.42f, 1.0f);
            float effectiveAlpha = thinFade * baseOpacityFactor;
            byte alpha = (byte)Math.Clamp((int)MathF.Round(effectiveAlpha * 255f), 0, 255);
            byte shadowAlpha = (byte)Math.Clamp((int)MathF.Round(54f * effectiveAlpha), 0, 72);
            if (alpha < 2) return;

            // 1. Soft contact shadow underneath the snow bank
            canvas.Save();
            canvas.Translate(0f, 1.3f);
            paint.Color = new SKColor(0, 0, 0, shadowAlpha);
            canvas.DrawPath(outer, paint);
            canvas.Restore();

            // 2. Dense shaded snow base / packed slope
            paint.Color = new SKColor(208, 228, 248, alpha);
            canvas.DrawPath(outer, paint);

            // 3. Crisp bright fresh snow ridge on top
            if (hasInner)
            {
                paint.Color = new SKColor(250, 253, 255, alpha);
                canvas.DrawPath(inner, paint);
            }
            else
            {
                paint.Color = new SKColor(246, 250, 255, alpha);
                canvas.DrawPath(outer, paint);
            }
        }

        private static void BuildSmoothRidgePath(
            SKPath path,
            float[] heights,
            int runStart,
            int runEnd,
            float originLeft,
            float cellW,
            float xStart,
            float xEnd,
            float baseY,
            float heightOffset)
        {
            path.MoveTo(xStart, baseY);

            float firstH = MathF.Max(0f, heights[runStart] - heightOffset);
            float prevX = Math.Clamp(originLeft + (runStart + 0.5f) * cellW, xStart, xEnd);
            float prevY = baseY - firstH;

            // Smooth tapered start into the first peak
            float enterMidX = (xStart + prevX) * 0.5f;
            float enterMidY = baseY - firstH * 0.55f;
            path.QuadTo(xStart, baseY - firstH * 0.15f, enterMidX, enterMidY);
            path.LineTo((enterMidX + prevX) * 0.5f, (enterMidY + prevY) * 0.5f);

            for (int c = runStart + 1; c <= runEnd; c++)
            {
                float h = MathF.Max(0f, heights[c] - heightOffset);
                float cx = Math.Clamp(originLeft + (c + 0.5f) * cellW, xStart, xEnd);
                float cy = baseY - h;
                float midX = (prevX + cx) * 0.5f;
                float midY = (prevY + cy) * 0.5f;
                path.QuadTo(prevX, prevY, midX, midY);
                prevX = cx;
                prevY = cy;
            }

            // Smooth tapered tail down to xEnd
            float exitMidX = (prevX + xEnd) * 0.5f;
            float exitMidY = (prevY + baseY) * 0.5f;
            path.QuadTo(prevX, prevY, exitMidX, exitMidY);
            path.QuadTo(xEnd, baseY - (baseY - prevY) * 0.15f, xEnd, baseY);
            path.Close();
        }

        private void RenderLeafOrSakuraPiles(SKCanvas canvas, SKImage[] sprites)
        {
            if (_visibleSurfaces.Count == 0 || sprites.Length == 0)
                return;

            var paint = _spritePaint!;
            int spriteCount = sprites.Length;
            bool useLeafFilters = _kind == ParticleKind.Leaves && _leafPaletteFilters is { Length: > 0 };
            var filters = _leafPaletteFilters;
            int filterCount = filters?.Length ?? 0;

            float baseScale = _kind == ParticleKind.Sakura ? 11.0f * _size : 15.5f * _size;

            for (int i = 0; i < _visibleSurfaces.Count; i++)
            {
                var s = _visibleSurfaces[i];
                var leaves = s.Leaves;
                if (leaves == null || leaves.Count == 0)
                    continue;

                if (!s.IsSynthetic && !IsSurfaceVisibleOnCurrentPage(s.PageKey))
                    continue;

                float surfaceFade = s.IsSynthetic ? 1.0f : s.FadeOpacity;
                if (surfaceFade < 0.02f)
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

                float windAbs = MathF.Abs(_wind);
                float windMag = 0.025f + windAbs * 0.09f;

                for (int j = 0; j < leaves.Count; j++)
                {
                    var leaf = leaves[j];
                    var img = sprites[leaf.SpriteIndex % spriteCount];
                    int maxDim = Math.Max(1, Math.Max(img.Width, img.Height));
                    float scaleFactor = (baseScale * leaf.Scale) / maxDim;
                    float halfW = img.Width * scaleFactor * 0.5f;
                    float halfH = img.Height * scaleFactor * 0.5f;

                    float exposure = Math.Clamp(0.35f + leaf.OffsetY * 0.16f, 0.35f, 1.0f);
                    float liveSway = MathF.Sin(_elapsedSec * 2.1f + leaf.RelX * 0.09f + j * 1.47f) * windMag * exposure;
                    float liveHover = MathF.Abs(MathF.Cos(_elapsedSec * 1.6f + leaf.RelX * 0.07f + j * 0.91f)) * (windAbs * 0.55f) * exposure;
                    float tiltY = leaf.IsSettling
                        ? (0.76f + 0.24f * MathF.Abs(MathF.Cos(leaf.Rotation * 1.25f + _elapsedSec * 3.0f)))
                        : (0.90f + 0.10f * MathF.Cos(liveSway * 3.5f));

                    float lx = s.Left + leaf.RelX;
                    float ly = s.Top - (leaf.OffsetY + liveHover) - halfH * 0.36f + 1.0f;
                    byte alpha = (byte)Math.Clamp((int)MathF.Round(_opacity * (leaf.Alpha / 255f) * surfaceFade * 255f), 0, 255);
                    if (alpha < 2) continue;

                    byte depthShade = (byte)Math.Clamp(212 + (int)(MathF.Min(leaf.OffsetY, 8f) * 5.2f), 212, 255);
                    var tinted = new SKColor(depthShade, depthShade, depthShade, alpha);

                    canvas.Save();
                    canvas.Translate(lx, ly);
                    canvas.RotateRadians(leaf.Rotation + liveSway);

                    if (useLeafFilters && filterCount > 0)
                    {
                        paint.Color = tinted;
                        paint.ColorFilter = filters![leaf.PaletteIndex % filterCount];
                    }
                    else
                    {
                        paint.ColorFilter = null;
                        paint.Color = tinted;
                    }

                    canvas.DrawImage(img, new SKRect(-halfW, -halfH * tiltY, halfW, halfH * tiltY), NearestSampling, paint);
                    canvas.Restore();
                }

                if (clipped)
                {
                    canvas.Restore();
                }
            }

            paint.ColorFilter = null;
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
            bool includeDynamicSprites = _snowPhysicsEnabled && !isSnow && IsWeatherPhysicsKind(_kind);
            int spriteCount = sprites.Length;
            int renderLimit = includeDynamicSprites ? TotalParticleSlots : _count;

            for (int i = 0; i < renderLimit; i++)
            {
                if (i >= _count && i < MaxParticles) continue;
                ref readonly var p = ref _items[i];
                if (i >= MaxParticles && (!p.IsActive || p.IsChunk)) continue;

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
                p.Mass = 1.0f;
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

        private void ApplyLoadedSnowData(WeatherAccumulationData data)
        {
            if (!_snowPersistenceEnabled)
                return;

            bool hasHeightModes = data.Modes is { Count: > 0 };
            bool hasLeafModes = data.LeafModes is { Count: > 0 };
            if (!hasHeightModes && !hasLeafModes)
                return;

            int dataVersion = data.Version;
            if (hasHeightModes)
            {
                foreach (var (modeName, pages) in data.Modes!)
                {
                    if (pages == null || pages.Count == 0) continue;
                    var modeKind = ParseKind(modeName);
                    if (modeKind != ParticleKind.Snow) continue;
                    var store = GetStoreForKind(modeKind);

                    foreach (var (pageKey, surfaces) in pages)
                    {
                        if (string.IsNullOrWhiteSpace(pageKey) || surfaces == null) continue;
                        foreach (var (surfaceId, bytes) in surfaces)
                        {
                            if (string.IsNullOrWhiteSpace(surfaceId) || bytes == null || bytes.Length == 0)
                                continue;

                            var s = GetOrCreateSurface(store, pageKey, surfaceId);
                            if (s.CellCount > 0)
                            {
                                if (s.MaxHeight <= 0.25f)
                                {
                                    UnpackBytesIntoSurface(s, bytes, dataVersion);
                                }
                            }
                            else
                            {
                                s.PendingSavedBytes = bytes;
                                s.PendingSavedVersion = dataVersion;
                            }
                        }
                    }
                }
            }

            if (hasLeafModes)
            {
                foreach (var (modeName, pages) in data.LeafModes!)
                {
                    if (pages == null || pages.Count == 0) continue;
                    var modeKind = ParseKind(modeName);
                    if (modeKind != ParticleKind.Leaves && modeKind != ParticleKind.Sakura) continue;
                    var store = GetStoreForKind(modeKind);

                    foreach (var (pageKey, surfaces) in pages)
                    {
                        if (string.IsNullOrWhiteSpace(pageKey) || surfaces == null) continue;
                        foreach (var (surfaceId, savedLeaves) in surfaces)
                        {
                            if (string.IsNullOrWhiteSpace(surfaceId) || savedLeaves == null || savedLeaves.Count == 0)
                                continue;

                            var s = GetOrCreateSurface(store, pageKey, surfaceId);
                            if (s.CellCount > 0 && s.Width > 4f)
                            {
                                if (!s.HasLeaves)
                                {
                                    UnpackLeavesIntoSurface(s, savedLeaves);
                                }
                            }
                            else
                            {
                                s.PendingSavedLeaves = new List<SettledLeaf>(savedLeaves);
                            }
                        }
                    }
                }
            }

            _persistenceDirty = false;
            _bridge.SetLatestPersistence(BuildPersistenceSnapshot());
            Invalidate();
        }

        private void UnpackLeavesIntoSurface(DriftSurface s, List<SettledLeaf> saved)
        {
            if (saved.Count == 0 || s.Width <= 4f) return;

            float cornerPadL = MathF.Max(3.0f, s.CornerRadiusLeft * 0.35f);
            float cornerPadR = MathF.Max(3.0f, s.CornerRadiusRight * 0.35f);
            float minRelX = cornerPadL;
            float maxRelX = MathF.Max(minRelX, s.Width - cornerPadR);
            float maxStack = MathF.Max(2.0f, _snowMaxHeightPx * 0.92f);
            int maxLeaves = s.IsSynthetic
                ? Math.Clamp((int)(s.Width / 3.5f * (_snowMaxHeightPx / 11.0f)), 48, 220)
                : Math.Clamp((int)(s.Width / 4.5f * (_snowMaxHeightPx / 11.0f)), 14, 110);

            s.Leaves ??= new List<SettledLeaf>(Math.Min(saved.Count, maxLeaves));
            s.Leaves.Clear();

            int startIdx = Math.Max(0, saved.Count - maxLeaves);
            for (int i = startIdx; i < saved.Count; i++)
            {
                var item = saved[i];
                item.RelX = Math.Clamp(item.RelX, minRelX, maxRelX);
                item.OffsetY = Math.Clamp(item.OffsetY, 0f, maxStack);
                item.Scale = Math.Clamp(item.Scale, 0.45f, 1.85f);
                if (item.Alpha < 40) item.Alpha = 215;
                item.TargetOffsetY = item.OffsetY;
                item.TargetRotation = item.Rotation;
                item.IsSettling = false;
                s.Leaves.Add(item);
            }
        }

        private void UnpackBytesIntoSurface(DriftSurface s, byte[] bytes, int dataVersion)
        {
            if (bytes.Length == 0 || s.CellCount <= 0) return;
            EnsureHeightsAllocated(s);

            int n = s.CellCount;
            int srcLen = bytes.Length;
            float max = 0f;
            float srcMaxHeight = dataVersion switch
            {
                >= 4 => PersistenceMaxHeightV4Px,
                >= 2 => PersistenceMaxHeightPx,
                _ => LegacyDriftHeightPx
            };
            float scale = srcMaxHeight / 255.0f;
            float sculptCap = GetSculptMaxHeightPx();

            for (int i = 0; i < n; i++)
            {
                float srcPos = srcLen > 1 && n > 1
                    ? (float)i / (n - 1) * (srcLen - 1)
                    : 0f;
                int lo = Math.Clamp((int)MathF.Floor(srcPos), 0, srcLen - 1);
                int hi = Math.Clamp(lo + 1, 0, srcLen - 1);
                float t = srcPos - lo;
                float val = (bytes[lo] * (1f - t) + bytes[hi] * t) * scale;
                float cap = GetMaxHeightForCell(s, i, sculptCap);
                val = MathF.Min(cap, val);
                s.Heights![i] = val;
                if (val > max) max = val;
            }

            s.MaxHeight = max;
            s.NeedsRelaxation = max > 0.25f;
        }

        private WeatherAccumulationData BuildPersistenceSnapshot()
        {
            var result = new WeatherAccumulationData { Version = 4 };

            PackStoreIntoMode(result, "Snow", _snowStore);
            PackLeafStoreIntoMode(result, "Leaves", _leavesStore);
            PackLeafStoreIntoMode(result, "Sakura", _sakuraStore);

            return result;
        }

        private static void PackLeafStoreIntoMode(
            WeatherAccumulationData target,
            string modeKey,
            Dictionary<string, Dictionary<string, DriftSurface>> store)
        {
            if (store.Count == 0) return;

            Dictionary<string, Dictionary<string, List<SettledLeaf>>>? modePages = null;
            foreach (var (pageKey, pageDict) in store)
            {
                Dictionary<string, List<SettledLeaf>>? savedPage = null;
                foreach (var (surfaceId, s) in pageDict)
                {
                    List<SettledLeaf>? packed = null;
                    if (s.Leaves is { Count: > 0 })
                    {
                        packed = new List<SettledLeaf>(s.Leaves);
                    }
                    else if (s.PendingSavedLeaves is { Count: > 0 })
                    {
                        packed = new List<SettledLeaf>(s.PendingSavedLeaves);
                    }

                    if (packed != null)
                    {
                        savedPage ??= new Dictionary<string, List<SettledLeaf>>(StringComparer.Ordinal);
                        savedPage[surfaceId] = packed;
                    }
                }

                if (savedPage != null && savedPage.Count > 0)
                {
                    modePages ??= new Dictionary<string, Dictionary<string, List<SettledLeaf>>>(StringComparer.Ordinal);
                    modePages[pageKey] = savedPage;
                }
            }

            if (modePages != null && modePages.Count > 0)
            {
                target.LeafModes[modeKey] = modePages;
            }
        }

        private static void PackStoreIntoMode(
            WeatherAccumulationData target,
            string modeKey,
            Dictionary<string, Dictionary<string, DriftSurface>> store)
        {
            if (store.Count == 0) return;

            Dictionary<string, Dictionary<string, byte[]>>? modePages = null;
            foreach (var (pageKey, pageDict) in store)
            {
                Dictionary<string, byte[]>? savedPage = null;
                foreach (var (surfaceId, s) in pageDict)
                {
                    byte[]? packed = null;
                    if (s.Heights != null && s.MaxHeight > 0.25f && s.CellCount > 0)
                    {
                        packed = new byte[s.CellCount];
                        float invScale = 255.0f / PersistenceMaxHeightV4Px;
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
                    modePages ??= new Dictionary<string, Dictionary<string, byte[]>>(StringComparer.Ordinal);
                    modePages[pageKey] = savedPage;
                }
            }

            if (modePages != null && modePages.Count > 0)
            {
                target.Modes[modeKey] = modePages;
            }
        }

        private void TriggerBackgroundPersistenceSave(long nowTicks)
        {
            if (!_snowPersistenceEnabled)
            {
                _persistenceDirty = false;
                return;
            }

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
                    WeatherAccumulationStore.Save(latest);
                }
                finally
                {
                    Interlocked.Exchange(ref _pendingSaveInFlight, 0);
                }
            });
        }

        private void FlushPersistenceNow(bool syncToDisk)
        {
            if (!_snowPersistenceEnabled || (_snowStore.Count == 0 && _leavesStore.Count == 0 && _sakuraStore.Count == 0))
                return;

            _persistenceDirty = false;
            _lastSaveTicks = Stopwatch.GetTimestamp();
            var snapshot = BuildPersistenceSnapshot();
            _bridge.SetLatestPersistence(snapshot);

            if (syncToDisk)
            {
                WeatherAccumulationStore.Save(snapshot);
            }
        }

        private void ClearAllDrifts()
        {
            ClearStore(_snowStore);
            ClearStore(_leavesStore);
            ClearStore(_sakuraStore);

            for (int i = MaxParticles; i < TotalParticleSlots; i++)
            {
                _items[i].IsActive = false;
            }

            _persistenceDirty = false;
            Array.Clear(_cursorPixelSnow);
            _cursorPixelMax = 0f;
            _cursorShakeEnergy = 0f;
            _bridge.ResetLatestPersistence();
            Invalidate();
        }

        private static void ClearStore(Dictionary<string, Dictionary<string, DriftSurface>> store)
        {
            foreach (var pageDict in store.Values)
            {
                foreach (var s in pageDict.Values)
                {
                    s.Heights = null;
                    s.PendingSavedBytes = null;
                    s.Leaves = null;
                    s.PendingSavedLeaves = null;
                    s.MaxHeight = 0f;
                    s.NeedsRelaxation = false;
                }
            }
            store.Clear();
        }

        private void ReleaseActiveVisibleState(bool releaseStores)
        {
            _visibleSurfaces.Clear();
            _prevVisibleSurfaces.Clear();
            _localSnapshotCount = 0;
            _lastSnapshotVersion = -1;
            _cursorActive = false;
            _hasPrevCursor = false;
            Array.Clear(_cursorPixelSnow);
            _cursorPixelMax = 0f;
            _cursorShakeEnergy = 0f;

            for (int i = MaxParticles; i < TotalParticleSlots; i++)
            {
                _items[i].IsActive = false;
            }

            if (releaseStores)
            {
                _snowStore.Clear();
                _leavesStore.Clear();
                _sakuraStore.Clear();
                _persistenceDirty = false;
            }
        }

        private void ReleaseAllSnowPhysicsMemory()
        {
            ReleaseActiveVisibleState(releaseStores: true);
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

            if (_driftPath != null)
            {
                try { _driftPath.Dispose(); } catch { }
                _driftPath = null;
            }

            if (_driftInnerPath != null)
            {
                try { _driftInnerPath.Dispose(); } catch { }
                _driftInnerPath = null;
            }

            if (_snowPuffImage != null)
            {
                try { _snowPuffImage.Dispose(); } catch { }
                _snowPuffImage = null;
            }

            DisposeLeafFilters();
            ReleaseEffectSpritesOnly();
            ReleaseCustomImageOnly();
        }
    }
}
