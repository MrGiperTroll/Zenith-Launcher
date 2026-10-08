using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
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

    private class Particle
    {
        public double X, Y, Vx, Vy, Size, Rotation, RotationSpeed;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ParticleTypeProperty)
        {
            _particles.Clear();
            if (ParticleType == "None")
            {
                _timer?.Stop();
                _timer = null;
            }
            else
            {
                if (_timer == null)
                {
                    _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
                    _timer.Tick += (s, e) => UpdateParticles();
                    _timer.Start();
                }
            }
            InvalidateVisual();
        }
    }

    private void UpdateParticles()
    {
        if (Bounds.Width <= 0 || Bounds.Height <= 0) return;

        // Spawn
        if (_particles.Count < 50 && _rand.NextDouble() < 0.2)
        {
            _particles.Add(new Particle
            {
                X = _rand.NextDouble() * Bounds.Width,
                Y = -20,
                Vx = (_rand.NextDouble() - 0.5) * 2,
                Vy = _rand.NextDouble() * 2 + 1,
                Size = _rand.NextDouble() * 5 + 3,
                Rotation = _rand.NextDouble() * 360,
                RotationSpeed = (_rand.NextDouble() - 0.5) * 5
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
            "Snowflakes" => new SolidColorBrush(Colors.White, 0.45),
            "Leaves" => new SolidColorBrush(Color.Parse("#88E28743")),
            "Sakura" => new SolidColorBrush(Color.Parse("#99FFB7C5")), // Soft pastel sakura petal
            _ => new SolidColorBrush(Colors.White, 0.4)
        };

        foreach (var p in _particles)
        {
            using (context.PushTransform(Matrix.CreateTranslation(-p.Size / 2, -p.Size / 2) *
                                            Matrix.CreateRotation(p.Rotation * Math.PI / 180.0) *
                                            Matrix.CreateTranslation(p.X, p.Y)))
            {
                context.DrawRectangle(brush, null, new Rect(0, 0, p.Size, p.Size));
            }
        }
    }
}
