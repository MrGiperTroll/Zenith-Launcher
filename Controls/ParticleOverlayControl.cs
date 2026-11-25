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
        public int PendingSavedVersion = 1;
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
        private const float LegacyDriftHeightPx = 11.0f;
        private const float PersistenceMaxHeightPx = 24.0f;
        private const float MaxSlopePerCell = 0.58f;
        private const float AirwakeRadiusPx = 88.0f;
        private const float BrushRadiusPx = 17.0f;
        private const float BaseSnowDeposit = 3.25f;
        private const int CursorCapCells = 5;
        private const float CursorCapCellW = 3.6f;
        private const float CursorCapMaxHeightPx = 7.5f;

        private static readonly float[] DepositKernel = [0.05f, 0.12f, 0.20f, 0.26f, 0.20f, 0.12f, 0.05f];
        private static readonly float[] CursorCapProfile = [0.55f, 0.90f, 1.00f, 0.90f, 0.55f];

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
        private bool _snowBottomAccumulationEnabled;
        private bool _snowCursorInteractionEnabled = true;
        private bool _snowPersistenceEnabled = true;
        private float _snowAccumulationRate = 1.0f;
        private float _snowMaxHeightPx = 11.0f;
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
        private float _prevCursorVx;
        private float _prevCursorVy;
        private float _cursorAccel;
        private bool _hasPrevCursor;
        private readonly float[] _cursorCapHeights = new float[CursorCapCells];
        private float _cursorCapMaxH;

        // Dynamic flake ring cursor
        private int _nextDynamicSlot = MaxParticles;

        // Persistence state
        private bool _persistenceDirty;
        private long _lastSaveTicks;
        private int _pendingSaveInFlight;

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
            bool wasPhysicsActive = _snowPhysicsEnabled && _kind == ParticleKind.Snow;
            bool nowPhysicsActive = cfg.SnowPhysicsEnabled && cfg.Kind == ParticleKind.Snow;

            if (wasPhysicsActive && !nowPhysicsActive)
            {
                if (_snowPersistenceEnabled)
                    FlushPersistenceNow(syncToDisk: true);
                ReleaseAllSnowPhysicsMemory();
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
            _activePageKey = cfg.ActivePageKey;
            _isModalOccluded = cfg.IsModalOccluded;

            if (!_snowCursorInteractionEnabled && _cursorCapMaxH > 0f)
            {
                Array.Clear(_cursorCapHeights);
                _cursorCapMaxH = 0f;
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
                RenderCursorSnowCap(canvas);
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
            SyncSurfacesFromBridge();
            EnsureBottomEdgeSurfaceSync();

            if (_snowCursorInteractionEnabled)
            {
                UpdateCursorKinematics(dt);

                if (!_isModalOccluded && _cursorActive)
                {
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

            float baseFall = 44.0f;
            float windPx = _wind * 65.0f;
            float margin = 42.0f;
            float drag = MathF.Exp(-3.4f * dt);
            float cursorSpeedSq = _cursorVx * _cursorVx + _cursorVy * _cursorVy;
            bool hasCursorWake = _snowCursorInteractionEnabled && _cursorActive && cursorSpeedSq > 400f;
            bool canLandOnCursor = _snowCursorInteractionEnabled && _cursorActive && !_isModalOccluded && cursorSpeedSq < (220f * 220f);

            // Step ambient flakes
            for (int i = 0; i < _count; i++)
            {
                StepSingleSnowflake(ref _items[i], dt, baseFall, windPx, margin, drag, hasCursorWake, canLandOnCursor);
            }

            // Step dynamic brushed/shed snowflakes
            for (int i = MaxParticles; i < TotalParticleSlots; i++)
            {
                if (!_items[i].IsActive) continue;
                StepSingleSnowflake(ref _items[i], dt, baseFall * 1.15f, windPx * 0.65f, margin, drag, hasCursorWake, canLandOnCursor);
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

            // Air drag on extra impulse velocity + gravity for ballistic flung snow
            if (p.IsDynamic)
            {
                if (p.IsChunk)
                {
                    p.Vx *= MathF.Exp(-0.85f * dt);
                    p.Vy += 430.0f * dt;
                    p.Vy *= MathF.Exp(-0.50f * dt);
                    p.Vy = Math.Clamp(p.Vy, -420f, 520f);
                }
                else
                {
                    p.Vx *= drag;
                    p.Vy += 240.0f * dt;
                    p.Vy *= MathF.Exp(-1.55f * dt);
                    p.Vy = Math.Clamp(p.Vy, -360f, 340f);
                }
                p.Angle += p.SpinRate * dt;
            }
            else
            {
                p.Vx *= drag;
                p.Vy *= drag;
            }

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
                    float invD = 1.0f / dist;
                    float radialX = dx * invD;
                    float radialY = dy * invD;
                    float cross = radialX * _cursorVy - radialY * _cursorVx;
                    float swirlSign = cross >= 0f ? 1f : -1f;
                    float swirlMag = MathF.Min(260f, MathF.Sqrt(_cursorVx * _cursorVx + _cursorVy * _cursorVy) * 0.35f);
                    float wakeScale = p.IsChunk ? 0.22f : 1.0f;

                    p.Vx += (_cursorVx * 0.55f * w2 + radialX * 95f * w2 - radialY * swirlSign * swirlMag * w2) * dt * 8.5f * wakeScale;
                    p.Vy += (_cursorVy * 0.42f * w2 + radialY * 70f * w2 + radialX * swirlSign * swirlMag * w2) * dt * 8.5f * wakeScale;

                    p.Vx = Math.Clamp(p.Vx, -480f, 480f);
                    p.Vy = Math.Clamp(p.Vy, -420f, 520f);
                }
            }

            float ambientVy = baseFall * _speed * p.SpeedScale;
            float sway = MathF.Sin(_elapsedSec * p.SwayFreq + p.Phase) * p.SwayAmp * 0.6f;
            float ambientVx = windPx * p.SpeedScale + sway;

            float totalVx = (p.IsChunk ? ambientVx * 0.15f : ambientVx) + p.Vx;
            float totalVy = (p.IsChunk ? ambientVy * 0.25f : ambientVy) + p.Vy;

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
            p.Mass = 1.0f;
        }

        private bool TryDepositOnCursor(ref ParticleItem p)
        {
            float capLeft = _cursorX - 7.0f;
            float capWidth = CursorCapCells * CursorCapCellW;
            float capRight = capLeft + capWidth;
            float capBaseY = _cursorY + 1.0f;

            if (p.X < capLeft - 1.5f || p.X > capRight + 1.5f)
                return false;

            int cell = Math.Clamp((int)((p.X - capLeft) / CursorCapCellW), 0, CursorCapCells - 1);
            float hitY = capBaseY - _cursorCapHeights[cell];
            if (p.PrevY > capBaseY + 3f || p.Y < hitY)
                return false;

            float rateScale = p.IsChunk ? 1.0f : _snowAccumulationRate;
            float amount = 1.65f * p.Mass * rateScale * Math.Clamp(_size, 0.5f, 1.5f);
            return TryAddSnowToCursorCapAtCell(cell, amount) > 0.001f;
        }

        private float TryAddSnowToCursorCapAtCell(int centerCell, float totalVolume)
        {
            float addedTotal = 0f;
            for (int k = -1; k <= 1; k++)
            {
                int idx = centerCell + k;
                if ((uint)idx >= CursorCapCells) continue;
                float weight = k == 0 ? 0.50f : 0.25f;
                float maxH = CursorCapMaxHeightPx * CursorCapProfile[idx];
                float room = MathF.Max(0f, maxH - _cursorCapHeights[idx]);
                float add = MathF.Min(room, totalVolume * weight);
                if (add > 0.001f)
                {
                    _cursorCapHeights[idx] += add;
                    addedTotal += add;
                }
            }

            if (addedTotal > 0.001f)
                RecomputeCursorCapMax();

            return addedTotal;
        }

        private bool IsSharpCursorFlick(float cursorSpeed)
        {
            return cursorSpeed > 540f || (_cursorAccel > 3200f && cursorSpeed > 180f);
        }

        private void UpdateCursorSnowShedding(float dt)
        {
            if (_cursorCapMaxH <= 0.18f) return;

            float speed = MathF.Sqrt(_cursorVx * _cursorVx + _cursorVy * _cursorVy);
            if (!IsSharpCursorFlick(speed))
                return;

            float shedFrac = Math.Clamp(MathF.Max((speed - 500f) / 480f, (_cursorAccel - 3000f) / 3600f), 0.24f, 1.0f);
            float capLeft = _cursorX - 7.0f;
            float capBaseY = _cursorY + 1.0f;
            float massAccum = 0f;
            int spawned = 0;
            int dustSpawned = 0;

            for (int i = 0; i < CursorCapCells; i++)
            {
                float h = _cursorCapHeights[i];
                if (h <= 0.08f) continue;

                float take = shedFrac >= 0.98f ? h : h * shedFrac;
                _cursorCapHeights[i] = shedFrac >= 0.98f ? 0f : h - take;
                massAccum += take;

                if (massAccum >= 1.05f && spawned < 7)
                {
                    float unit = MathF.Min(massAccum, 1.50f + (float)_rng.NextDouble() * 0.65f);
                    massAccum -= unit;
                    float cx = capLeft + (i + 0.5f) * CursorCapCellW;
                    float cy = capBaseY - h * 0.5f;
                    float vx = _cursorVx * 0.44f + ((float)_rng.NextDouble() - 0.5f) * 110f;
                    float vy = _cursorVy * 0.30f - 45f - (float)_rng.NextDouble() * 55f;
                    SpawnDynamicSnowflake(cx, cy, vx, vy, cooldown: 0.15f, mass: unit / BaseSnowDeposit, isChunk: true);
                    spawned++;

                    if (dustSpawned < 2 && _rng.NextDouble() < 0.45)
                    {
                        SpawnDynamicSnowflake(
                            cx,
                            cy,
                            vx * 0.72f + ((float)_rng.NextDouble() - 0.5f) * 70f,
                            vy - 16f,
                            cooldown: 0.16f,
                            mass: 0.16f,
                            isChunk: false,
                            sizeScale: 0.48f);
                        dustSpawned++;
                    }
                }
            }

            if (massAccum >= 0.30f && spawned < 7)
            {
                SpawnDynamicSnowflake(
                    _cursorX,
                    _cursorY - 2f,
                    _cursorVx * 0.40f + ((float)_rng.NextDouble() - 0.5f) * 90f,
                    _cursorVy * 0.28f - 40f,
                    cooldown: 0.15f,
                    mass: Math.Clamp(massAccum / BaseSnowDeposit, 0.38f, 1.35f),
                    isChunk: true);
            }

            RecomputeCursorCapMax();
        }

        private void RecomputeCursorCapMax()
        {
            float max = 0f;
            for (int i = 0; i < CursorCapCells; i++)
            {
                if (_cursorCapHeights[i] > max) max = _cursorCapHeights[i];
            }
            _cursorCapMaxH = max;
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
                    float totalVolume = p.IsChunk
                        ? BaseSnowDeposit * Math.Clamp(p.Mass, 0.35f, 1.85f)
                        : BaseSnowDeposit * _snowAccumulationRate * Math.Clamp(p.Mass, 0.18f, 1.8f)
                            * (0.95f + 0.35f * p.SizeScale)
                            * Math.Clamp(_size, 0.5f, 1.8f);

                    if (DepositGaussian(s, cell, totalVolume, _snowMaxHeightPx))
                    {
                        s.NeedsRelaxation = true;
                        RelaxLocal(s, cell, _snowMaxHeightPx);
                        if (p.IsChunk && totalVolume > 2.4f)
                        {
                            RelaxLocal(s, cell, _snowMaxHeightPx);
                        }
                        _persistenceDirty = true;
                        return true;
                    }

                    // Saturated right under impact: roll/slide sideways off the pile
                    p.Vx += (p.X < (s.Left + s.Right) * 0.5f ? -1f : 1f) * (p.IsChunk ? 64f : 48f);
                    if (p.IsChunk) p.Vy *= 0.35f;
                    p.Cooldown = 0.07f;
                    return false;
                }
            }

            return false;
        }

        private static bool DepositGaussian(DriftSurface s, int centerCell, float totalVolume, float maxDriftHeightPx)
        {
            EnsureHeightsAllocated(s);
            var h = s.Heights!;
            int n = s.CellCount;
            float leftover = 0f;
            bool depositedAny = false;

            for (int k = 0; k < DepositKernel.Length; k++)
            {
                int idx = centerCell + (k - 3);
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

            // Redistribute any clipped/overflow volume to nearby unsaturated cells
            if (leftover > 0.05f)
            {
                for (int dist = 1; dist <= 4 && leftover > 0.05f; dist++)
                {
                    for (int sign = -1; sign <= 1; sign += 2)
                    {
                        int idx = centerCell + dist * sign;
                        if ((uint)idx >= (uint)n) continue;
                        float cap = GetMaxHeightForCell(s, idx, maxDriftHeightPx);
                        float room = MathF.Max(0f, cap - h[idx]);
                        float add = MathF.Min(leftover * 0.5f, room);
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
            float moveDir = MathF.Abs(dx) > 0.25f ? MathF.Sign(dx) : MathF.Sign(_cursorVx);
            float cursorSpeed = MathF.Sqrt(_cursorVx * _cursorVx + _cursorVy * _cursorVy);
            bool sharpFlick = IsSharpCursorFlick(cursorSpeed);

            for (int i = 0; i < _visibleSurfaces.Count; i++)
            {
                var s = _visibleSurfaces[i];
                if (s.Heights == null || s.MaxHeight <= 0.20f)
                    continue;

                if (s.Right < segMinX || s.Left > segMaxX)
                    continue;
                if (s.Top + 6f < segMinY || s.Top - _snowMaxHeightPx - 6f > segMaxY)
                    continue;

                bool modified = false;
                float ejectedMass = 0f;
                float lastHitX = x1;
                float lastHitY = s.Top;
                int dustSpawned = 0;

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
                        if (h <= 0.12f) continue;

                        float cellCenterX = s.Left + (c + 0.5f) * CellWidthPx;
                        float cellTopY = s.Top - h * 0.5f;
                        float distX = MathF.Abs(cellCenterX - px);
                        float distY = MathF.Abs(cellTopY - py);
                        float distSq = distX * distX + distY * distY;
                        if (distSq > BrushRadiusPx * BrushRadiusPx) continue;

                        float falloff = 1.0f - MathF.Sqrt(distSq) / BrushRadiusPx;
                        float brushFraction = sharpFlick ? (0.48f + 0.46f * falloff) : (0.34f + 0.44f * falloff);
                        float disturbed = MathF.Min(h, h * brushFraction);
                        if (disturbed <= 0.04f) continue;

                        s.Heights[c] = h - disturbed;
                        modified = true;
                        lastHitX = cellCenterX;
                        lastHitY = s.Top - s.Heights[c];

                        // 1. Plow part of disturbed snow forward along the card edge in cursor direction
                        float plowRatio = sharpFlick ? 0.25f : 0.34f;
                        float plowedShare = moveDir != 0f ? disturbed * plowRatio : 0f;
                        float remainingShare = disturbed - plowedShare;

                        if (plowedShare > 0.01f)
                        {
                            int pushDir = (int)moveDir;
                            int targetCell = c + pushDir * 3;
                            if ((uint)targetCell < (uint)s.CellCount)
                            {
                                float cap = GetMaxHeightForCell(s, targetCell, _snowMaxHeightPx);
                                float room = MathF.Max(0f, cap - s.Heights[targetCell]);
                                float placed = MathF.Min(plowedShare, room);
                                s.Heights[targetCell] += placed;
                                remainingShare += plowedShare - placed;
                            }
                            else
                            {
                                remainingShare += plowedShare;
                            }
                        }

                        // 2. Smooth pass scoops snow directly onto cursor cap; overflow/flick launches soft white clumps
                        if (!sharpFlick && remainingShare > 0.01f)
                        {
                            float scoopRequest = remainingShare * 0.72f;
                            float scooped = TryAddSnowToCursorCapAtCell(CursorCapCells / 2, scoopRequest);
                            remainingShare -= scooped;
                        }

                        ejectedMass += remainingShare;
                        while (ejectedMass >= 2.0f)
                        {
                            float unit = MathF.Min(ejectedMass, 1.95f + (float)_rng.NextDouble() * 1.65f);
                            ejectedMass -= unit;
                            float launchVx = _cursorVx * 0.54f + moveDir * (45f + (float)_rng.NextDouble() * 55f) + ((float)_rng.NextDouble() - 0.5f) * 65f;
                            float launchVy = Math.Clamp(_cursorVy * 0.30f - 78f - cursorSpeed * 0.10f - (float)_rng.NextDouble() * 50f, -340f, 110f);
                            SpawnDynamicSnowflake(
                                lastHitX,
                                MathF.Min(s.Top - 1f, lastHitY - 1f),
                                launchVx,
                                launchVy,
                                cooldown: 0.16f,
                                mass: unit / BaseSnowDeposit,
                                isChunk: true);

                            if (dustSpawned < 2 && _rng.NextDouble() < 0.35)
                            {
                                SpawnDynamicSnowflake(
                                    lastHitX,
                                    MathF.Min(s.Top - 1f, lastHitY - 1f),
                                    launchVx * 0.7f + ((float)_rng.NextDouble() - 0.5f) * 65f,
                                    launchVy - 15f,
                                    cooldown: 0.18f,
                                    mass: 0.16f,
                                    isChunk: false,
                                    sizeScale: 0.46f);
                                dustSpawned++;
                            }
                        }
                    }
                }

                if (modified)
                {
                    if (ejectedMass >= 0.45f)
                    {
                        float launchVx = _cursorVx * 0.50f + moveDir * 38f + ((float)_rng.NextDouble() - 0.5f) * 58f;
                        float launchVy = Math.Clamp(_cursorVy * 0.28f - 70f - cursorSpeed * 0.08f, -310f, 95f);
                        SpawnDynamicSnowflake(
                            lastHitX,
                            MathF.Min(s.Top - 1f, lastHitY - 1f),
                            launchVx,
                            launchVy,
                            cooldown: 0.16f,
                            mass: Math.Clamp(ejectedMass / BaseSnowDeposit, 0.38f, 1.45f),
                            isChunk: true);
                    }
                    s.NeedsRelaxation = true;
                    RecomputeMaxHeight(s);
                    _persistenceDirty = true;
                }
            }
        }

        private void ShedSnowFromSurface(DriftSurface s, float fraction, float extraVx, float extraVy)
        {
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
                    float vx = extraVx + ((float)_rng.NextDouble() - 0.5f) * 70f;
                    float vy = extraVy - 22f + (float)_rng.NextDouble() * 40f;
                    SpawnDynamicSnowflake(
                        cellX,
                        cellY,
                        vx,
                        vy,
                        cooldown: 0.16f,
                        mass: Math.Clamp(unit / BaseSnowDeposit, 0.45f, 1.45f),
                        isChunk: true);
                    spawned++;

                    if (dustSpawned < 3 && _rng.NextDouble() < 0.30)
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

            if (_bridge.TryReadPointer(ref _lastCursorVersion, out float nx, out float ny, out bool active))
            {
                if (!active)
                {
                    _cursorActive = false;
                    _hasPrevCursor = false;
                    _cursorAccel = 0f;
                }
                else
                {
                    if (_cursorActive && _hasPrevCursor)
                    {
                        _prevCursorX = _cursorX;
                        _prevCursorY = _cursorY;
                        float instVx = (nx - _cursorX) / dt;
                        float instVy = (ny - _cursorY) / dt;
                        float newVx = _cursorVx * 0.42f + Math.Clamp(instVx, -1800f, 1800f) * 0.58f;
                        float newVy = _cursorVy * 0.42f + Math.Clamp(instVy, -1800f, 1800f) * 0.58f;
                        float instAx = (newVx - _cursorVx) / MathF.Max(dt, 0.004f);
                        float instAy = (newVy - _cursorVy) / MathF.Max(dt, 0.004f);
                        float instAccel = MathF.Sqrt(instAx * instAx + instAy * instAy);
                        _cursorAccel = _cursorAccel * 0.58f + Math.Clamp(instAccel, 0f, 12000f) * 0.42f;
                        _cursorVx = newVx;
                        _cursorVy = newVy;
                    }
                    else
                    {
                        _prevCursorX = nx;
                        _prevCursorY = ny;
                        _cursorVx = 0f;
                        _cursorVy = 0f;
                        _prevCursorVx = 0f;
                        _prevCursorVy = 0f;
                        _cursorAccel = 0f;
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
                _cursorAccel *= decay;
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
                    UnpackBytesIntoSurface(s, s.PendingSavedBytes, s.PendingSavedVersion);
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
            foreach (var pageDict in _driftStore.Values)
            {
                foreach (var s in pageDict.Values)
                {
                    var h = s.Heights;
                    if (h == null || s.CellCount <= 0) continue;
                    bool changed = false;
                    for (int i = 0; i < s.CellCount; i++)
                    {
                        float cap = GetMaxHeightForCell(s, i, _snowMaxHeightPx);
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

            for (int pass = 0; pass < 3; pass++)
            {
                for (int i = lo; i < hi; i++)
                {
                    float diff = h[i] - h[i + 1];
                    if (diff > MaxSlopePerCell)
                    {
                        float move = (diff - MaxSlopePerCell) * 0.42f;
                        float cap = GetMaxHeightForCell(s, i + 1, maxDriftHeightPx);
                        float actual = MathF.Min(move, MathF.Max(0f, cap - h[i + 1]));
                        h[i] -= actual;
                        h[i + 1] += actual;
                    }
                    else if (-diff > MaxSlopePerCell)
                    {
                        float move = (-diff - MaxSlopePerCell) * 0.42f;
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

            float maxDriftHeightPx = _snowMaxHeightPx;
            bool movedAny = false;
            for (int i = 0; i < s.CellCount - 1; i++)
            {
                float diff = h[i] - h[i + 1];
                if (diff > MaxSlopePerCell)
                {
                    float move = (diff - MaxSlopePerCell) * 0.38f;
                    float cap = GetMaxHeightForCell(s, i + 1, maxDriftHeightPx);
                    float actual = MathF.Min(move, MathF.Max(0f, cap - h[i + 1]));
                    if (actual > 0.015f)
                    {
                        h[i] -= actual;
                        h[i + 1] += actual;
                        movedAny = true;
                    }
                }
                else if (-diff > MaxSlopePerCell)
                {
                    float move = (-diff - MaxSlopePerCell) * 0.38f;
                    float cap = GetMaxHeightForCell(s, i, maxDriftHeightPx);
                    float actual = MathF.Min(move, MathF.Max(0f, cap - h[i]));
                    if (actual > 0.015f)
                    {
                        h[i + 1] -= actual;
                        h[i] += actual;
                        movedAny = true;
                    }
                }
            }

            // Gentle Laplacian smoothing for isolated spikes
            for (int i = 1; i < s.CellCount - 1; i++)
            {
                float avg = (h[i - 1] + h[i + 1]) * 0.5f;
                float spike = h[i] - avg;
                if (spike > 0.25f)
                {
                    float d = spike * 0.22f;
                    h[i] -= d;
                    h[i - 1] = MathF.Min(GetMaxHeightForCell(s, i - 1, maxDriftHeightPx), h[i - 1] + d * 0.5f);
                    h[i + 1] = MathF.Min(GetMaxHeightForCell(s, i + 1, maxDriftHeightPx), h[i + 1] + d * 0.5f);
                    movedAny = true;
                }
            }

            if (s.CellCount > 0)
            {
                h[0] = MathF.Min(h[0], GetMaxHeightForCell(s, 0, maxDriftHeightPx));
                h[s.CellCount - 1] = MathF.Min(h[s.CellCount - 1], GetMaxHeightForCell(s, s.CellCount - 1, maxDriftHeightPx));
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
                            baseOpacityFactor);
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
            if (!_snowCursorInteractionEnabled || !_cursorActive || _cursorCapMaxH < 0.22f)
                return;

            _driftPaint ??= new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
            _driftPath ??= new SKPath();
            _driftInnerPath ??= new SKPath();

            float capLeft = _cursorX - 7.0f;
            float capWidth = CursorCapCells * CursorCapCellW;
            float baseY = _cursorY + 1.0f;

            DrawSmoothSnowPill(
                canvas,
                _driftPaint,
                _cursorCapHeights,
                0,
                CursorCapCells - 1,
                capLeft,
                CursorCapCellW,
                capLeft,
                capLeft + capWidth,
                baseY,
                _cursorCapMaxH,
                0.88f + _opacity * 0.12f);
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
                    float r = Math.Clamp(p.SizeScale * 3.7f * sizeScaleGlobal, 2.0f, 6.0f);
                    float speedSq = p.Vx * p.Vx + p.Vy * p.Vy;

                    // Soft trailing snow puff wake behind fast-flying clumps
                    if (speedSq > 3600f)
                    {
                        byte trailA1 = (byte)(alpha * 0.34f);
                        float r1 = r * 0.72f;
                        float tx1 = p.X - p.Vx * 0.017f;
                        float ty1 = p.Y - p.Vy * 0.017f;
                        paint.Color = new SKColor(246, 251, 255, trailA1);
                        canvas.DrawImage(img, new SKRect(tx1 - r1, ty1 - r1, tx1 + r1, ty1 + r1), LinearSampling, paint);

                        byte trailA2 = (byte)(alpha * 0.15f);
                        float r2 = r * 0.50f;
                        float tx2 = p.X - p.Vx * 0.034f;
                        float ty2 = p.Y - p.Vy * 0.034f;
                        paint.Color = new SKColor(238, 247, 255, trailA2);
                        canvas.DrawImage(img, new SKRect(tx2 - r2, ty2 - r2, tx2 + r2, ty2 + r2), LinearSampling, paint);
                    }

                    // Fluffy bright-white snow clump (2-6 px soft radius)
                    float ovalFactor = 1.0f + 0.18f * MathF.Abs(MathF.Sin(p.Phase * 1.7f));
                    float rx = r * ovalFactor;
                    float ry = r;

                    canvas.Save();
                    canvas.Translate(p.X, p.Y);
                    canvas.RotateRadians(p.Angle);
                    paint.Color = new SKColor(255, 255, 255, alpha);
                    canvas.DrawImage(img, new SKRect(-rx, -ry, rx, ry), LinearSampling, paint);
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
            byte alpha = (byte)Math.Clamp((int)MathF.Round(baseOpacityFactor * thinFade * 255f), 75, 255);
            byte shadowAlpha = (byte)Math.Clamp((int)MathF.Round(52f * baseOpacityFactor * thinFade), 14, 72);

            // 1. Soft contact shadow underneath the snow pillow
            canvas.Save();
            canvas.Translate(0f, 1.5f);
            paint.Color = new SKColor(0, 0, 0, shadowAlpha);
            canvas.DrawPath(outer, paint);
            canvas.Restore();

            // 2. Soft icy-blue underside tint
            paint.Color = new SKColor(214, 232, 252, alpha);
            canvas.DrawPath(outer, paint);

            // 3. Crisp bright white snow cap on top
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

            for (int i = 0; i < _count; i++)
            {
                ref readonly var p = ref _items[i];

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

        private void ApplyLoadedSnowData(SnowAccumulationData data)
        {
            if (!_snowPersistenceEnabled || data.Pages == null || data.Pages.Count == 0)
                return;

            int dataVersion = data.Version;
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
                        if (!s.HasSnow)
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

            _persistenceDirty = false;
            _bridge.SetLatestPersistence(BuildPersistenceSnapshot());
            Invalidate();
        }

        private void UnpackBytesIntoSurface(DriftSurface s, byte[] bytes, int dataVersion)
        {
            if (bytes.Length == 0 || s.CellCount <= 0) return;
            EnsureHeightsAllocated(s);

            int n = s.CellCount;
            int srcLen = bytes.Length;
            float max = 0f;
            float srcMaxHeight = dataVersion >= 2 ? PersistenceMaxHeightPx : LegacyDriftHeightPx;
            float scale = srcMaxHeight / 255.0f;

            for (int i = 0; i < n; i++)
            {
                float srcPos = srcLen > 1 && n > 1
                    ? (float)i / (n - 1) * (srcLen - 1)
                    : 0f;
                int lo = Math.Clamp((int)MathF.Floor(srcPos), 0, srcLen - 1);
                int hi = Math.Clamp(lo + 1, 0, srcLen - 1);
                float t = srcPos - lo;
                float val = (bytes[lo] * (1f - t) + bytes[hi] * t) * scale;
                float cap = GetMaxHeightForCell(s, i, _snowMaxHeightPx);
                val = MathF.Min(cap, val);
                s.Heights![i] = val;
                if (val > max) max = val;
            }

            s.MaxHeight = max;
            s.NeedsRelaxation = max > 0.25f;
        }

        private SnowAccumulationData BuildPersistenceSnapshot()
        {
            var result = new SnowAccumulationData { Version = 2 };

            foreach (var (pageKey, pageDict) in _driftStore)
            {
                Dictionary<string, byte[]>? savedPage = null;
                foreach (var (surfaceId, s) in pageDict)
                {
                    byte[]? packed = null;
                    if (s.Heights != null && s.MaxHeight > 0.25f && s.CellCount > 0)
                    {
                        packed = new byte[s.CellCount];
                        float invScale = 255.0f / PersistenceMaxHeightPx;
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
            if (!_snowPersistenceEnabled || _driftStore.Count == 0)
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
            Array.Clear(_cursorCapHeights);
            _cursorCapMaxH = 0f;
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
            Array.Clear(_cursorCapHeights);
            _cursorCapMaxH = 0f;

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
