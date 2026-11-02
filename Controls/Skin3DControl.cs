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
            // Click trigger animation
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

        var faces = new List<QuadFace>();

        // 1. Head (centered at Y=28)
        AddCuboid(faces, new Vector3(-4, 24, -4), new Vector3(8, 8, 8), _defaultSkinTone, _defaultHair, 0, breath * 0.5f);

        // 2. Torso (centered at Y=18)
        AddCuboid(faces, new Vector3(-4, 12, -2), new Vector3(8, 12, 4), _defaultShirt, _defaultShirt, 0, 0);

        // 3. Right Arm (centered at X=-6, Y=18)
        AddCuboid(faces, new Vector3(-8, 12, -2), new Vector3(4, 12, 4), _defaultShirt, _defaultSkinTone, wave, 0);

        // 4. Left Arm (centered at X=6, Y=18)
        AddCuboid(faces, new Vector3(4, 12, -2), new Vector3(4, 12, 4), _defaultShirt, _defaultSkinTone, -wave * 0.3f, 0);

        // 5. Right Leg (centered at X=-2, Y=6)
        AddCuboid(faces, new Vector3(-4, 0, -2), new Vector3(4, 12, 4), _defaultPants, _defaultPants, 0, 0);

        // 6. Left Leg (centered at X=2, Y=6)
        AddCuboid(faces, new Vector3(0, 0, -2), new Vector3(4, 12, 4), _defaultPants, _defaultPants, 0, 0);

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
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));

            context.DrawGeometry(brush, null, geom);
        }
    }

    private static Vector3 RotatePoint(Vector3 v, float yaw, float pitch)
    {
        // 1. Yaw around Y axis
        float cosY = MathF.Cos(yaw);
        float sinY = MathF.Sin(yaw);
        float x1 = v.X * cosY - v.Z * sinY;
        float z1 = v.X * sinY + v.Z * cosY;
        float y1 = v.Y;

        // 2. Pitch around X axis
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

    private static void AddCuboid(List<QuadFace> faces, Vector3 origin, Vector3 size, Color mainCol, Color topCol, float rotXDeg, float rotYDeg)
    {
        float x0 = origin.X, x1 = origin.X + size.X;
        float y0 = origin.Y, y1 = origin.Y + size.Y;
        float z0 = origin.Z, z1 = origin.Z + size.Z;

        // Vertices
        var v000 = new Vector3(x0, y0, z0);
        var v100 = new Vector3(x1, y0, z0);
        var v110 = new Vector3(x1, y1, z0);
        var v010 = new Vector3(x0, y1, z0);
        var v001 = new Vector3(x0, y0, z1);
        var v101 = new Vector3(x1, y0, z1);
        var v111 = new Vector3(x1, y1, z1);
        var v011 = new Vector3(x0, y1, z1);

        // Front (Z+)
        faces.Add(new QuadFace { P1 = v001, P2 = v101, P3 = v111, P4 = v011, BaseColor = mainCol });
        // Back (Z-)
        faces.Add(new QuadFace { P1 = v100, P2 = v000, P3 = v010, P4 = v110, BaseColor = mainCol });
        // Top (Y+)
        faces.Add(new QuadFace { P1 = v011, P2 = v111, P3 = v110, P4 = v010, BaseColor = topCol });
        // Bottom (Y-)
        faces.Add(new QuadFace { P1 = v000, P2 = v100, P3 = v101, P4 = v001, BaseColor = mainCol });
        // Left (X-)
        faces.Add(new QuadFace { P1 = v000, P2 = v001, P3 = v011, P4 = v010, BaseColor = mainCol });
        // Right (X+)
        faces.Add(new QuadFace { P1 = v101, P2 = v100, P3 = v110, P4 = v111, BaseColor = mainCol });
    }
}
