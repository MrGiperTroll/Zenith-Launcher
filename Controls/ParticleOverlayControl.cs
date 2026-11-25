using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Rendering.Composition;
using Avalonia.Skia;
using Avalonia.Threading;
using SkiaSharp;
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
/// </summary>
public class ParticleOverlayControl : Control
{
    public const int MaxParticles = 160;

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

    private CompositionCustomVisual? _customVisual;
    private Window? _hostWindow;
    private string _loadedImagePath = string.Empty;
    private SKBitmap? _pendingCustomBitmap;
    private bool _customBitmapDirty;
    private ParticleKind _loadedSpriteKind = ParticleKind.Off;
    private int _spriteLoadGeneration;

    public ParticleOverlayControl()
    {
        IsHitTestVisible = false;
        ClipToBounds = true;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        var p = change.Property;
        if (p == CustomImagePathProperty || p == ParticleTypeProperty)
        {
            if (p == ParticleTypeProperty)
                UpdateOverlayVisibility();

            RefreshCustomBitmapCache();
            EnsureSpritesForCurrentKind();
            PushStateToHandler();
        }
        else if (p == LeafPaletteProperty
              || p == ParticleCountProperty
              || p == ParticleSpeedProperty
              || p == ParticleWindProperty
              || p == ParticleSizeProperty
              || p == ParticleOpacityProperty)
        {
            PushStateToHandler();
        }
        else if (p == BoundsProperty)
        {
            if (_customVisual != null)
            {
                _customVisual.Size = new Vector(Bounds.Width, Bounds.Height);
                _customVisual.SendHandlerMessage(new ViewportSizeMsg((float)Bounds.Width, (float)Bounds.Height));
            }
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        AttachHostWindow();

        var elemVisual = ElementComposition.GetElementVisual(this);
        if (elemVisual != null)
        {
            _customVisual = elemVisual.Compositor.CreateCustomVisual(new ParticleVisualHandler());
            _customVisual.Size = new Vector(Bounds.Width, Bounds.Height);
            ElementComposition.SetElementChildVisual(this, _customVisual);
            RefreshCustomBitmapCache();
            EnsureSpritesForCurrentKind();
            PushStateToHandler();
        }

        UpdateOverlayVisibility();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        DetachHostWindow();
        _spriteLoadGeneration++;
        _loadedSpriteKind = ParticleKind.Off;

        if (_customVisual != null)
        {
            _customVisual.SendHandlerMessage(ShutdownMsg.Instance);
            ElementComposition.SetElementChildVisual(this, null);
            _customVisual = null;
        }

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

    [StructLayout(LayoutKind.Sequential)]
    private struct ParticleItem
    {
        public float X;
        public float Y;
        public float SpeedScale;
        public float SizeScale;
        public float Phase;
        public float SwayFreq;
        public float SwayAmp;
        public float Angle;
        public float SpinRate;
        public float AlphaScale;
        public byte PaletteIndex;
        public byte SpriteIndex;
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

    private sealed class ShutdownMsg
    {
        public static readonly ShutdownMsg Instance = new();
        private ShutdownMsg() { }
    }

    private sealed class ParticleVisualHandler : CompositionCustomVisualHandler
    {
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

        private readonly ParticleItem[] _items = new ParticleItem[MaxParticles];
        private readonly Random _rng = new(0x5EED42);

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
        private long _lastTicks;
        private float _elapsedSec;

        private SKPaint? _spritePaint;
        private SKImage[]? _spriteImages;
        private SKBitmap[]? _spriteBitmaps;
        private SKColorFilter[]? _leafPaletteFilters;
        private SKBitmap? _customBitmap;
        private SKImage? _customImage;

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
                case ShutdownMsg:
                    _isRunning = false;
                    _tickScheduled = false;
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
            _kind = cfg.Kind;
            _count = Math.Clamp(cfg.Count, 0, MaxParticles);
            _speed = cfg.Speed;
            _wind = cfg.Wind;
            _size = cfg.Size;
            _opacity = cfg.Opacity;
            _isPaused = cfg.Paused;

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
                for (int i = 0; i < MaxParticles; i++)
                {
                    _items[i].X *= sx;
                    _items[i].Y *= sy;
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
                StepParticles(dt);
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

            if (_kind == ParticleKind.Custom)
            {
                RenderCustomImages(canvas);
            }
            else if (_spriteImages is { Length: > 0 } && _spriteKind == _kind)
            {
                RenderMinecraftSprites(canvas, _spriteImages);
            }
        }

        private void StepParticles(float dt)
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
                p.SpeedScale = 0.65f + (float)_rng.NextDouble() * 0.7f;
                p.SizeScale = 0.70f + (float)_rng.NextDouble() * 0.65f;
                p.Phase = (float)(_rng.NextDouble() * Math.PI * 2.0);
                p.SwayFreq = 0.8f + (float)_rng.NextDouble() * 1.4f;
                p.SwayAmp = 12.0f + (float)_rng.NextDouble() * 22.0f;
                p.Angle = (float)(_rng.NextDouble() * Math.PI * 2.0);
                p.SpinRate = (float)((_rng.NextDouble() - 0.5) * 1.6);
                p.AlphaScale = 0.55f + (float)_rng.NextDouble() * 0.45f;
                p.PaletteIndex = (byte)_rng.Next(0, 5);
                p.SpriteIndex = (byte)_rng.Next(0, 12);
            }

            _seeded = true;
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

            DisposeLeafFilters();
            ReleaseEffectSpritesOnly();
            ReleaseCustomImageOnly();
        }
    }
}
