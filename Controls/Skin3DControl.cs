using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace CustomMcLauncher.Controls;

public class Skin3DControl : Control
{
    public static readonly StyledProperty<Bitmap?> SkinBitmapProperty =
        AvaloniaProperty.Register<Skin3DControl, Bitmap?>(nameof(SkinBitmap));

    public Bitmap? SkinBitmap
    {
        get => GetValue(SkinBitmapProperty);
        set => SetValue(SkinBitmapProperty, value);
    }

    static Skin3DControl()
    {
        SkinBitmapProperty.Changed.AddClassHandler<Skin3DControl>((ctl, args) =>
        {
            ctl.OnSkinBitmapChanged(args.NewValue as Bitmap);
        });
    }

    private Color[,]? _skinPixels;

    private void OnSkinBitmapChanged(Bitmap? bmp)
    {
        if (bmp != null)
        {
            _skinPixels = SkinPixelExtractor.ExtractPixels(bmp);
        }
        else
        {
            _skinPixels = null;
        }
        InvalidateVisual();
    }

    private float _yaw = 25f;
    private float _pitch = 10f;
    private bool _isDragging;
    private Point _lastPos;

    // Animation state
    private float _animTime = 0f;
    private bool _isActionPlaying = false;
    private float _actionTime = 0f;
    private readonly DispatcherTimer _animTimer;

    private readonly Color _defaultSkinTone = Color.FromRgb(187, 137, 105);
    private readonly Color _defaultShirt = Color.FromRgb(0, 168, 168);
    private readonly Color _defaultPants = Color.FromRgb(43, 50, 140);
    private readonly Color _defaultHair = Color.FromRgb(74, 44, 23);

    public Skin3DControl()
    {
        ClipToBounds = true;
        _animTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16) // ~60fps
        };
        _animTimer.Tick += (s, e) =>
        {
            _animTime += 0.03f;
            if (_isActionPlaying)
            {
                _actionTime += 0.08f;
                if (_actionTime > MathF.PI * 2)
                {
                    _isActionPlaying = false;
                    _actionTime = 0;
                }
            }
            InvalidateVisual();
        };
        _animTimer.Start();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _isDragging = true;
            _lastPos = e.GetPosition(this);
            e.Pointer.Capture(this);
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_isDragging)
        {
            var cur = e.GetPosition(this);
            var dx = (float)(cur.X - _lastPos.X);
            var dy = (float)(cur.Y - _lastPos.Y);
            _yaw += dx * 1.2f;
            _pitch = Math.Clamp(_pitch - dy * 0.8f, -45f, 45f);
            _lastPos = cur;
            InvalidateVisual();
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_isDragging)
        {
            _isDragging = false;
            e.Pointer.Capture(null);
            _isActionPlaying = true;
            _actionTime = 0;
        }
    }

    private struct QuadFace
    {
        public Vector3 P1, P2, P3, P4;
        public Color BaseColor;
        public float Depth;
        public float Light;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var bounds = Bounds;
        if (bounds.Width < 10 || bounds.Height < 10) return;

        var center = new Vector2((float)bounds.Width / 2f, (float)bounds.Height / 2f + 25f);
        float scale = MathF.Min((float)bounds.Width, (float)bounds.Height) / 46f;

        float radYaw = _yaw * MathF.PI / 180f;
        float radPitch = _pitch * MathF.PI / 180f;

        // Limb animation angles
        float breath = MathF.Sin(_animTime * 1.5f) * 1.5f;
        float wave = _isActionPlaying ? MathF.Sin(_actionTime) * 45f : 0f;

        // 1. Draw contact drop shadow under feet
        var shadowPoint = Project(new Vector3(0, 0, 0), center, scale);
        var shadowBrush = new RadialGradientBrush
        {
            GradientStops = new GradientStops
            {
                new GradientStop(Color.FromArgb(90, 0, 0, 0), 0.0),
                new GradientStop(Color.FromArgb(40, 0, 0, 0), 0.5),
                new GradientStop(Color.FromArgb(0, 0, 0, 0), 1.0)
            }
        };
        context.DrawEllipse(shadowBrush, null, new Point(shadowPoint.X, shadowPoint.Y + 2), scale * 8.5, scale * 3.8);

        var faces = new List<QuadFace>();

        // 2. Head (8x8x8) at Y=24..32
        AddTexturedBox(faces, new Vector3(-4, 24, -4), new Vector3(8, 8, 8),
            u0: 8, v0: 0, uWidth: 8, vHeight: 8,
            topU: 8, topV: 0, btmU: 16, btmV: 0,
            rightU: 0, rightV: 8, frontU: 8, frontV: 8, leftU: 16, leftV: 8, backU: 24, backV: 8);

        // Hat layer (expanded by 0.4)
        AddTexturedBox(faces, new Vector3(-4.4f, 23.6f, -4.4f), new Vector3(8.8f, 8.8f, 8.8f),
            u0: 40, v0: 0, uWidth: 8, vHeight: 8,
            topU: 40, topV: 0, btmU: 48, btmV: 0,
            rightU: 32, rightV: 8, frontU: 40, frontV: 8, leftU: 48, leftV: 8, backU: 56, backV: 8, isOuterLayer: true);

        // 3. Torso (8x12x4) at Y=12..24
        AddTexturedBox(faces, new Vector3(-4, 12, -2), new Vector3(8, 12, 4),
            u0: 20, v0: 16, uWidth: 8, vHeight: 12,
            topU: 20, topV: 16, btmU: 28, btmV: 16,
            rightU: 16, rightV: 20, frontU: 20, frontV: 20, leftU: 28, leftV: 20, backU: 32, backV: 20);

        // Jacket layer (expanded by 0.35)
        AddTexturedBox(faces, new Vector3(-4.35f, 11.65f, -2.35f), new Vector3(8.7f, 12.7f, 4.7f),
            u0: 20, v0: 32, uWidth: 8, vHeight: 12,
            topU: 20, topV: 32, btmU: 28, btmV: 32,
            rightU: 16, rightV: 36, frontU: 20, frontV: 36, leftU: 28, leftV: 36, backU: 32, backV: 36, isOuterLayer: true);

        // 4. Right Arm (4x12x4) at X=-8..-4, Y=12..24
        AddTexturedBox(faces, new Vector3(-8, 12, -2), new Vector3(4, 12, 4),
            u0: 44, v0: 16, uWidth: 4, vHeight: 12,
            topU: 44, topV: 16, btmU: 48, btmV: 16,
            rightU: 40, rightV: 20, frontU: 44, frontV: 20, leftU: 48, leftV: 20, backU: 52, backV: 20,
            rotXDeg: wave);

        // 5. Left Arm (4x12x4) at X=4..8, Y=12..24
        AddTexturedBox(faces, new Vector3(4, 12, -2), new Vector3(4, 12, 4),
            u0: 36, v0: 48, uWidth: 4, vHeight: 12,
            topU: 36, topV: 48, btmU: 40, btmV: 48,
            rightU: 32, rightV: 52, frontU: 36, frontV: 52, leftU: 40, leftV: 52, backU: 44, backV: 52,
            rotXDeg: -wave * 0.3f);

        // 6. Right Leg (4x12x4) at X=-4..0, Y=0..12
        AddTexturedBox(faces, new Vector3(-4, 0, -2), new Vector3(4, 12, 4),
            u0: 4, v0: 16, uWidth: 4, vHeight: 12,
            topU: 4, topV: 16, btmU: 8, btmV: 16,
            rightU: 0, rightV: 20, frontU: 4, frontV: 20, leftU: 8, leftV: 20, backU: 12, backV: 20);

        // 7. Left Leg (4x12x4) at X=0..4, Y=0..12
        AddTexturedBox(faces, new Vector3(0, 0, -2), new Vector3(4, 12, 4),
            u0: 20, v0: 48, uWidth: 4, vHeight: 12,
            topU: 20, topV: 48, btmU: 24, btmV: 48,
            rightU: 16, rightV: 52, frontU: 20, frontV: 52, leftU: 24, leftV: 52, backU: 28, backV: 52);

        // Transform vertices and calculate lighting
        var lightDir = Vector3.Normalize(new Vector3(0.4f, 0.8f, 0.6f));
        var rotFaces = new List<QuadFace>();

        foreach (var face in faces)
        {
            var p1 = RotatePoint(face.P1, radYaw, radPitch);
            var p2 = RotatePoint(face.P2, radYaw, radPitch);
            var p3 = RotatePoint(face.P3, radYaw, radPitch);
            var p4 = RotatePoint(face.P4, radYaw, radPitch);

            // Compute normal in camera space
            var normal = Vector3.Normalize(Vector3.Cross(p2 - p1, p3 - p1));
            if (normal.Z >= 0) continue; // Back-face culling

            float light = 0.5f + 0.5f * MathF.Max(0, Vector3.Dot(normal, lightDir));
            float depth = (p1.Z + p2.Z + p3.Z + p4.Z) / 4f;

            rotFaces.Add(new QuadFace
            {
                P1 = p1,
                P2 = p2,
                P3 = p3,
                P4 = p4,
                BaseColor = face.BaseColor,
                Depth = depth,
                Light = light
            });
        }

        // Painter's algorithm: sort back to front (largest depth first)
        rotFaces.Sort((a, b) => b.Depth.CompareTo(a.Depth));

        // Render sorted faces
        foreach (var face in rotFaces)
        {
            var sp1 = Project(face.P1, center, scale);
            var sp2 = Project(face.P2, center, scale);
            var sp3 = Project(face.P3, center, scale);
            var sp4 = Project(face.P4, center, scale);

            var geom = new StreamGeometry();
            using (var ctx = geom.Open())
            {
                ctx.BeginFigure(sp1, isFilled: true);
                ctx.LineTo(sp2);
                ctx.LineTo(sp3);
                ctx.LineTo(sp4);
                ctx.EndFigure(isClosed: true);
            }

            var c = face.BaseColor;
            byte r = (byte)Math.Clamp((int)(c.R * face.Light), 0, 255);
            byte g = (byte)Math.Clamp((int)(c.G * face.Light), 0, 255);
            byte b = (byte)Math.Clamp((int)(c.B * face.Light), 0, 255);
            var brush = new SolidColorBrush(Color.FromArgb(c.A, r, g, b));

            context.DrawGeometry(brush, null, geom);
        }
    }

    private static Vector3 RotatePoint(Vector3 v, float yaw, float pitch)
    {
        float cosY = MathF.Cos(yaw);
        float sinY = MathF.Sin(yaw);
        float x1 = v.X * cosY - v.Z * sinY;
        float z1 = v.X * sinY + v.Z * cosY;
        float y1 = v.Y;

        float cosP = MathF.Cos(pitch);
        float sinP = MathF.Sin(pitch);
        float y2 = y1 * cosP - z1 * sinP;
        float z2 = y1 * sinP + z1 * cosP;

        return new Vector3(x1, y2, z2);
    }

    private static Point Project(Vector3 v, Vector2 center, float scale)
    {
        return new Point(center.X + v.X * scale, center.Y - v.Y * scale);
    }

    private void AddTexturedBox(List<QuadFace> faces, Vector3 origin, Vector3 size,
        int u0, int v0, int uWidth, int vHeight,
        int topU, int topV, int btmU, int btmV,
        int rightU, int rightV, int frontU, int frontV, int leftU, int leftV, int backU, int backV,
        float rotXDeg = 0f, bool isOuterLayer = false)
    {
        int sx = (int)MathF.Round(size.X);
        int sy = (int)MathF.Round(size.Y);
        int sz = (int)MathF.Round(size.Z);

        // Front (Z+)
        AddPlane(faces, new Vector3(origin.X, origin.Y + size.Y, origin.Z + size.Z),
            new Vector3(size.X / sx, 0, 0), new Vector3(0, -size.Y / sy, 0),
            sx, sy, frontU, frontV, rotXDeg, isOuterLayer);

        // Back (Z-)
        AddPlane(faces, new Vector3(origin.X + size.X, origin.Y + size.Y, origin.Z),
            new Vector3(-size.X / sx, 0, 0), new Vector3(0, -size.Y / sy, 0),
            sx, sy, backU, backV, rotXDeg, isOuterLayer);

        // Top (Y+)
        AddPlane(faces, new Vector3(origin.X, origin.Y + size.Y, origin.Z),
            new Vector3(size.X / sx, 0, 0), new Vector3(0, 0, size.Z / sz),
            sx, sz, topU, topV, rotXDeg, isOuterLayer);

        // Bottom (Y-)
        AddPlane(faces, new Vector3(origin.X, origin.Y, origin.Z + size.Z),
            new Vector3(size.X / sx, 0, 0), new Vector3(0, 0, -size.Z / sz),
            sx, sz, btmU, btmV, rotXDeg, isOuterLayer);

        // Right (X-)
        AddPlane(faces, new Vector3(origin.X, origin.Y + size.Y, origin.Z),
            new Vector3(0, 0, size.Z / sz), new Vector3(0, -size.Y / sy, 0),
            sz, sy, rightU, rightV, rotXDeg, isOuterLayer);

        // Left (X+)
        AddPlane(faces, new Vector3(origin.X + size.X, origin.Y + size.Y, origin.Z + size.Z),
            new Vector3(0, 0, -size.Z / sz), new Vector3(0, -size.Y / sy, 0),
            sz, sy, leftU, leftV, rotXDeg, isOuterLayer);
    }

    private void AddPlane(List<QuadFace> faces, Vector3 start, Vector3 uDir, Vector3 vDir,
        int countU, int countV, int texU, int texV, float rotXDeg, bool isOuterLayer)
    {
        for (int y = 0; y < countV; y++)
        {
            for (int x = 0; x < countU; x++)
            {
                int u = texU + x;
                int v = texV + y;

                Color c;
                if (_skinPixels != null && u >= 0 && u < 64 && v >= 0 && v < 64)
                {
                    c = _skinPixels[u, v];
                    if (isOuterLayer && c.A < 20) continue;
                    if (c.A < 20) c = _defaultSkinTone;
                }
                else
                {
                    if (isOuterLayer) continue;
                    c = (texV < 16) ? _defaultSkinTone : (texV < 32 ? _defaultShirt : _defaultPants);
                }

                var p0 = start + uDir * x + vDir * y;
                var p1 = start + uDir * (x + 1) + vDir * y;
                var p2 = start + uDir * (x + 1) + vDir * (y + 1);
                var p3 = start + uDir * x + vDir * (y + 1);

                if (MathF.Abs(rotXDeg) > 0.01f)
                {
                    float rad = rotXDeg * MathF.PI / 180f;
                    float pivotY = start.Y;
                    p0 = RotateLimbX(p0, pivotY, rad);
                    p1 = RotateLimbX(p1, pivotY, rad);
                    p2 = RotateLimbX(p2, pivotY, rad);
                    p3 = RotateLimbX(p3, pivotY, rad);
                }

                faces.Add(new QuadFace { P1 = p0, P2 = p1, P3 = p2, P4 = p3, BaseColor = c });
            }
        }
    }

    private static Vector3 RotateLimbX(Vector3 p, float pivotY, float rad)
    {
        float dy = p.Y - pivotY;
        float dz = p.Z;
        float cos = MathF.Cos(rad);
        float sin = MathF.Sin(rad);
        return new Vector3(p.X, pivotY + dy * cos - dz * sin, dy * sin + dz * cos);
    }
}
