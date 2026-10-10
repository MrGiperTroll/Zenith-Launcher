using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace CustomMcLauncher.Controls;

public class ParticleSystemControl : Control
{
    public static readonly StyledProperty<string> ParticleTypeProperty =
        AvaloniaProperty.Register<ParticleSystemControl, string>(nameof(ParticleType), "None");

    public string ParticleType
    {
        get => GetValue(ParticleTypeProperty);
        set => SetValue(ParticleTypeProperty, value);
    }

    private DispatcherTimer? _timer;
    private List<Particle> _particles = new();
    private Random _rand = new();

    public static readonly StyledProperty<int> ParticleCountProperty =
        AvaloniaProperty.Register<ParticleSystemControl, int>(nameof(ParticleCount), 50);

    public int ParticleCount
    {
        get => GetValue(ParticleCountProperty);
        set => SetValue(ParticleCountProperty, value);
    }

    public static readonly StyledProperty<double> ParticleScaleProperty =
        AvaloniaProperty.Register<ParticleSystemControl, double>(nameof(ParticleScale), 1.0);

    public double ParticleScale
    {
        get => GetValue(ParticleScaleProperty);
        set => SetValue(ParticleScaleProperty, value);
    }

    public static readonly StyledProperty<double> ParticleSpeedProperty =
        AvaloniaProperty.Register<ParticleSystemControl, double>(nameof(ParticleSpeed), 1.0);

    public double ParticleSpeed
    {
        get => GetValue(ParticleSpeedProperty);
        set => SetValue(ParticleSpeedProperty, value);
    }

    public static readonly StyledProperty<string> ParticleColorHexProperty =
        AvaloniaProperty.Register<ParticleSystemControl, string>(nameof(ParticleColorHex), "");

    public string ParticleColorHex
    {
        get => GetValue(ParticleColorHexProperty);
        set => SetValue(ParticleColorHexProperty, value);
    }

    public static readonly StyledProperty<double> ParticleAngleDegProperty =
        AvaloniaProperty.Register<ParticleSystemControl, double>(nameof(ParticleAngleDeg), 90.0);

    public double ParticleAngleDeg
    {
        get => GetValue(ParticleAngleDegProperty);
        set => SetValue(ParticleAngleDegProperty, value);
    }

    public static readonly StyledProperty<string> ParticleTexturePathProperty =
        AvaloniaProperty.Register<ParticleSystemControl, string>(nameof(ParticleTexturePath), "");

    public string ParticleTexturePath
    {
        get => GetValue(ParticleTexturePathProperty);
        set => SetValue(ParticleTexturePathProperty, value);
    }

    private Bitmap? _customTexture;
    private string _customBrushKey = "";
    private IBrush _customBrush = DefaultBrush;

    private static readonly SolidColorBrush SnowBrush = new(Colors.White, 0.45);
    private static readonly SolidColorBrush LeavesBrush = new(Color.Parse("#88E28743"));
    private static readonly SolidColorBrush SakuraBrush = new(Color.Parse("#99FFB7C5"));
    private static readonly SolidColorBrush DefaultBrush = new(Colors.White, 0.4);

    private class Particle
    {
        public double X, Y, Vx, Vy, Size, Rotation, RotationSpeed;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ParticleTexturePathProperty)
        {
            ReloadCustomTexture();
            InvalidateVisual();
            return;
        }
        if (change.Property == ParticleColorHexProperty)
        {
            _customBrushKey = "";
            InvalidateVisual();
            return;
        }
        if (change.Property == ParticleTypeProperty)
        {
            _particles.Clear();
            if (ParticleType == "None")
            {
                StopTimer();
            }
            else
            {
                if (_timer == null)
                {
                    _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
                    _timer.Tick += OnTimerTick;
                    _timer.Start();
                }
            }
            InvalidateVisual();
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        StopTimer();
        _particles.Clear();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (ParticleType != "None" && _timer == null && IsVisible)
        {
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _timer.Tick += OnTimerTick;
            _timer.Start();
        }
    }

    private void OnTimerTick(object? sender, EventArgs e) => UpdateParticles();

    private void ReloadCustomTexture()
    {
        try { _customTexture?.Dispose(); } catch { }
        _customTexture = null;
        var path = ParticleTexturePath;
        if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path)) return;
        try
        {
            using var fs = System.IO.File.OpenRead(path);
            _customTexture = Bitmap.DecodeToWidth(fs, 48);
        }
        catch { _customTexture = null; }
    }

    private IBrush ResolveCustomBrush()
    {
        string key = ParticleColorHex ?? "";
        if (_customBrushKey != key)
        {
            _customBrushKey = key;
            IBrush resolved = DefaultBrush;
            try
            {
                if (!string.IsNullOrWhiteSpace(key) && Color.TryParse(key.StartsWith("#") ? key : "#" + key, out var c))
                    resolved = new SolidColorBrush(Color.FromArgb(0xCC, c.R, c.G, c.B));
                else if (Application.Current?.TryGetResource("AccentBrush", null, out var v) == true && v is ISolidColorBrush sb)
                    resolved = new SolidColorBrush(Color.FromArgb(0xCC, sb.Color.R, sb.Color.G, sb.Color.B));
            }
            catch { }
            _customBrush = resolved;
        }
        return _customBrush;
    }

    private void StopTimer()
    {
        if (_timer != null)
        {
            _timer.Tick -= OnTimerTick;
            _timer.Stop();
            _timer = null;
        }
    }

    private void UpdateParticles()
    {
        if (Bounds.Width <= 0 || Bounds.Height <= 0) return;

        // Spawn
        int maxParticles = Math.Max(10, Math.Min(150, ParticleCount));
        if (_particles.Count < maxParticles && _rand.NextDouble() < 0.2)
        {
            double scale = Math.Max(0.3, Math.Min(2.5, ParticleScale));
            double speed = Math.Max(0.2, Math.Min(3.0, ParticleSpeed));
            double vx = (_rand.NextDouble() - 0.5) * 2 * speed;
            double vy = (_rand.NextDouble() * 2 + 1) * speed;
            double sx = _rand.NextDouble() * Bounds.Width;
            double sy = -20;
            if (ParticleType == "Custom")
            {
                // Ambient field: drift along the chosen angle across the whole area.
                double ang = (ParticleAngleDeg % 360.0) * Math.PI / 180.0;
                double sp = (_rand.NextDouble() * 2 + 1) * speed;
                vx = Math.Cos(ang) * sp;
                vy = Math.Sin(ang) * sp;
                sx = _rand.NextDouble() * Bounds.Width;
                sy = _rand.NextDouble() * Math.Max(1.0, Bounds.Height);
            }
            _particles.Add(new Particle
            {
                X = sx,
                Y = sy,
                Vx = vx,
                Vy = vy,
                Size = (_rand.NextDouble() * 5 + 3) * scale,
                Rotation = _rand.NextDouble() * 360,
                RotationSpeed = (_rand.NextDouble() - 0.5) * 5 * speed
            });
        }

        // Update
        for (int i = _particles.Count - 1; i >= 0; i--)
        {
            var p = _particles[i];
            p.X += p.Vx;
            p.Y += p.Vy;
            p.Rotation += p.RotationSpeed;

            if (ParticleType == "Snowflakes")
            {
                p.Vx += (_rand.NextDouble() - 0.5) * 0.1; // Drift
            }
            else if (ParticleType == "Leaves")
            {
                p.Vx = Math.Sin(p.Y / 30.0) * 2.0; // Wiggle
            }
            else if (ParticleType == "Sakura")
            {
                p.Vx = Math.Sin(p.Y / 22.0) * 1.5 + 0.4; // Gentle breeze wiggle
            }
            else if (ParticleType == "Custom")
            {
                p.Vx += (_rand.NextDouble() - 0.5) * 0.15; // Soft drift
            }

            if (p.Y > Bounds.Height + 20 || p.X < -20 || p.X > Bounds.Width + 20)
            {
                _particles.RemoveAt(i);
            }
        }

        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        if (ParticleType == "None") return;

        var brush = ParticleType switch
        {
            "Snowflakes" => SnowBrush,
            "Leaves" => LeavesBrush,
            "Sakura" => SakuraBrush,
            "Custom" => ResolveCustomBrush(),
            _ => DefaultBrush
        };

        foreach (var p in _particles)
        {
            using (context.PushTransform(Matrix.CreateTranslation(-p.Size / 2, -p.Size / 2) *
                                            Matrix.CreateRotation(p.Rotation * Math.PI / 180.0) *
                                            Matrix.CreateTranslation(p.X, p.Y)))
            {
                var tex = _customTexture;
                if (ParticleType == "Custom" && tex != null)
                    context.DrawImage(tex, new Rect(0, 0, tex.Size.Width, tex.Size.Height), new Rect(0, 0, p.Size, p.Size));
                else
                    context.DrawRectangle(brush, null, new Rect(0, 0, p.Size, p.Size));
            }
        }
    }
}
