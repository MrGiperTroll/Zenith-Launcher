using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Rendering.Composition;
using Avalonia.Skia;
using SkiaSharp;

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
/// Uses pre-allocated structs and reused Skia paints/paths with zero per-frame managed allocations.
/// Automatically halts animation updates and disposes textures/paints when Off or window is minimized/hidden.
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
            PushStateToHandler();
        }

        UpdateOverlayVisibility();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        DetachHostWindow();

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
        private static readonly SKColor[] SnowColors =
        [
            new SKColor(245, 250, 255),
            new SKColor(225, 239, 255),
            new SKColor(210, 230, 255),
            new SKColor(255, 255, 255),
            new SKColor(235, 244, 255)
        ];

        private static readonly SKColor[] SakuraColors =
        [
            new SKColor(253, 164, 175),
            new SKColor(251, 207, 232),
            new SKColor(244, 114, 182),
            new SKColor(255, 228, 230),
            new SKColor(249, 168, 212)
        ];

        private static readonly SKColor[] LeafSpringColors =
        [
            new SKColor(104, 184, 82),
            new SKColor(76, 163, 68),
            new SKColor(132, 204, 92),
            new SKColor(159, 224, 107),
            new SKColor(92, 176, 107)
        ];

        private static readonly SKColor[] LeafGoldenColors =
        [
            new SKColor(234, 179, 8),
            new SKColor(245, 158, 11),
            new SKColor(250, 204, 21),
            new SKColor(217, 119, 6),
            new SKColor(253, 224, 71)
        ];

        private static readonly SKColor[] LeafAutumnColors =
        [
            new SKColor(234, 88, 12),
            new SKColor(220, 38, 38),
            new SKColor(217, 119, 6),
            new SKColor(180, 83, 9),
            new SKColor(225, 29, 72)
        ];

        private static readonly SKColor[] LeafForestColors =
        [
            new SKColor(76, 163, 68),
            new SKColor(163, 198, 57),
            new SKColor(245, 158, 11),
            new SKColor(234, 88, 12),
            new SKColor(185, 28, 28)
        ];

        private readonly ParticleItem[] _items = new ParticleItem[MaxParticles];
        private readonly Random _rng = new(0x5EED42);

        private ParticleKind _kind = ParticleKind.Off;
        private SKColor[] _activePalette = SnowColors;
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

        private SKPaint? _fillPaint;
        private SKPaint? _veinPaint;
        private SKPaint? _bitmapPaint;
        private SKPath? _leafPath;
        private SKPath? _petalPath;
        private SKBitmap? _customBitmap;

        public override void OnMessage(object message)
        {
            switch (message)
            {
                case ConfigMsg cfg:
                    ApplyConfig(cfg);
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
                if (!ReferenceEquals(_customBitmap, cfg.CustomBitmap))
                {
                    try { _customBitmap?.Dispose(); } catch { }
                    _customBitmap = cfg.CustomBitmap;
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
            _activePalette = ResolvePalette(_kind, cfg.LeafPalette);

            if (_kind == ParticleKind.Off || _count <= 0)
            {
                _isRunning = false;
                _tickScheduled = false;
                ReleaseRenderResources();
                Invalidate();
                return;
            }

            EnsureRenderResources();
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

            EnsureRenderResources();
            if (_fillPaint == null) return;

            switch (_kind)
            {
                case ParticleKind.Snow:
                    RenderSnow(canvas);
                    break;
                case ParticleKind.Leaves:
                    RenderLeaves(canvas);
                    break;
                case ParticleKind.Sakura:
                    RenderSakura(canvas);
                    break;
                case ParticleKind.Custom:
                    RenderCustomBitmaps(canvas);
                    break;
            }
        }

        private void StepParticles(float dt)
        {
            float baseFall = _kind switch
            {
                ParticleKind.Snow => 42.0f,
                ParticleKind.Sakura => 52.0f,
                ParticleKind.Leaves => 60.0f,
                ParticleKind.Custom => 54.0f,
                _ => 48.0f
            };

            float windPx = _wind * 65.0f;
            float margin = 42.0f;

            for (int i = 0; i < _count; i++)
            {
                ref var p = ref _items[i];
                float vy = baseFall * _speed * p.SpeedScale;
                float sway = MathF.Sin(_elapsedSec * p.SwayFreq + p.Phase) * p.SwayAmp * (_kind == ParticleKind.Snow ? 0.65f : 1.0f);
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

        private void RenderSnow(SKCanvas canvas)
        {
            var paint = _fillPaint!;
            float baseRadius = 3.2f * _size;

            for (int i = 0; i < _count; i++)
            {
                ref readonly var p = ref _items[i];
                float r = baseRadius * p.SizeScale;
                byte a = (byte)Math.Clamp((int)MathF.Round(_opacity * p.AlphaScale * 255f), 0, 255);
                if (a < 2) continue;

                SKColor c = _activePalette[p.PaletteIndex];
                // Soft outer halo without expensive per-frame SKMaskFilter blurs
                paint.Color = c.WithAlpha((byte)(a >> 2));
                canvas.DrawCircle(p.X, p.Y, r * 1.85f, paint);

                paint.Color = c.WithAlpha(a);
                canvas.DrawCircle(p.X, p.Y, r, paint);
            }
        }

        private void RenderLeaves(SKCanvas canvas)
        {
            if (_leafPath == null || _fillPaint == null || _veinPaint == null) return;
            var fill = _fillPaint;
            var vein = _veinPaint;
            float scaleBase = 1.05f * _size;

            for (int i = 0; i < _count; i++)
            {
                ref readonly var p = ref _items[i];
                byte a = (byte)Math.Clamp((int)MathF.Round(_opacity * p.AlphaScale * 255f), 0, 255);
                if (a < 2) continue;

                SKColor c = _activePalette[p.PaletteIndex];
                float sc = scaleBase * p.SizeScale;
                float tiltY = 0.65f + 0.35f * MathF.Abs(MathF.Cos(p.Angle * 0.7f + p.Phase));

                canvas.Save();
                canvas.Translate(p.X, p.Y);
                canvas.RotateRadians(p.Angle);
                canvas.Scale(sc, sc * tiltY);

                fill.Color = c.WithAlpha(a);
                canvas.DrawPath(_leafPath, fill);

                vein.Color = new SKColor(20, 12, 6, (byte)(a * 0.32f));
                canvas.DrawLine(0f, -6.5f, 0f, 6.2f, vein);

                canvas.Restore();
            }
        }

        private void RenderSakura(SKCanvas canvas)
        {
            if (_petalPath == null || _fillPaint == null) return;
            var fill = _fillPaint;
            float scaleBase = 1.0f * _size;

            for (int i = 0; i < _count; i++)
            {
                ref readonly var p = ref _items[i];
                byte a = (byte)Math.Clamp((int)MathF.Round(_opacity * p.AlphaScale * 255f), 0, 255);
                if (a < 2) continue;

                SKColor c = _activePalette[p.PaletteIndex];
                float sc = scaleBase * p.SizeScale;
                float flutter = 0.55f + 0.45f * MathF.Abs(MathF.Sin(p.Angle * 0.85f + p.Phase));

                canvas.Save();
                canvas.Translate(p.X, p.Y);
                canvas.RotateRadians(p.Angle);
                canvas.Scale(sc, sc * flutter);

                fill.Color = c.WithAlpha(a);
                canvas.DrawPath(_petalPath, fill);

                canvas.Restore();
            }
        }

        private void RenderCustomBitmaps(SKCanvas canvas)
        {
            if (_customBitmap == null || _bitmapPaint == null) return;
            var paint = _bitmapPaint;
            float baseDim = 24.0f * _size;
            float aspect = _customBitmap.Height > 0 ? (float)_customBitmap.Height / _customBitmap.Width : 1.0f;

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
                canvas.DrawBitmap(_customBitmap, new SKRect(-halfW, -halfH, halfW, halfH), paint);

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
                p.SizeScale = 0.6f + (float)_rng.NextDouble() * 0.75f;
                p.Phase = (float)(_rng.NextDouble() * Math.PI * 2.0);
                p.SwayFreq = 0.8f + (float)_rng.NextDouble() * 1.4f;
                p.SwayAmp = 12.0f + (float)_rng.NextDouble() * 24.0f;
                p.Angle = (float)(_rng.NextDouble() * Math.PI * 2.0);
                p.SpinRate = (float)((_rng.NextDouble() - 0.5) * 1.9);
                p.AlphaScale = 0.50f + (float)_rng.NextDouble() * 0.50f;
                p.PaletteIndex = (byte)_rng.Next(0, 5);
            }

            _seeded = true;
        }

        private static SKColor[] ResolvePalette(ParticleKind kind, string paletteName) => kind switch
        {
            ParticleKind.Snow => SnowColors,
            ParticleKind.Sakura => SakuraColors,
            ParticleKind.Leaves => (paletteName ?? "").Trim().ToLowerInvariant() switch
            {
                "spring" => LeafSpringColors,
                "golden" => LeafGoldenColors,
                "forest" => LeafForestColors,
                _ => LeafAutumnColors
            },
            _ => SnowColors
        };

        private void EnsureRenderResources()
        {
            _fillPaint ??= new SKPaint
            {
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };

            _veinPaint ??= new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 0.9f,
                StrokeCap = SKStrokeCap.Round,
                IsAntialias = true
            };

            _bitmapPaint ??= new SKPaint
            {
                IsAntialias = true
            };

            if (_leafPath == null)
            {
                var leaf = new SKPath();
                leaf.MoveTo(0f, -8f);
                leaf.CubicTo(5.2f, -4.5f, 5.8f, 3.2f, 0f, 8f);
                leaf.CubicTo(-5.8f, 3.2f, -5.2f, -4.5f, 0f, -8f);
                leaf.Close();
                _leafPath = leaf;
            }

            if (_petalPath == null)
            {
                var petal = new SKPath();
                petal.MoveTo(0f, -7.5f);
                petal.CubicTo(4.8f, -5.0f, 5.4f, 2.0f, 1.4f, 6.4f);
                petal.LineTo(0f, 4.4f);
                petal.LineTo(-1.4f, 6.4f);
                petal.CubicTo(-5.4f, 2.0f, -4.8f, -5.0f, 0f, -7.5f);
                petal.Close();
                _petalPath = petal;
            }
        }

        private void ReleaseRenderResources()
        {
            try { _fillPaint?.Dispose(); } catch { }
            _fillPaint = null;

            try { _veinPaint?.Dispose(); } catch { }
            _veinPaint = null;

            try { _bitmapPaint?.Dispose(); } catch { }
            _bitmapPaint = null;

            try { _leafPath?.Dispose(); } catch { }
            _leafPath = null;

            try { _petalPath?.Dispose(); } catch { }
            _petalPath = null;

            try { _customBitmap?.Dispose(); } catch { }
            _customBitmap = null;
        }
    }
}
