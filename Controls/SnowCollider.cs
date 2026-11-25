using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Runtime.CompilerServices;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;

namespace ZenithLauncher.Controls;

public struct ColliderSnapshot
{
    public int RuntimeId;
    public string PageKey;
    public string SurfaceId;
    public float Left;
    public float Top;
    public float Right;
    public float Bottom;
    public float ClipLeft;
    public float ClipTop;
    public float ClipRight;
    public float ClipBottom;
    public float CornerRadiusLeft;
    public float CornerRadiusRight;
    public float ScrollOffsetX;
    public float ScrollOffsetY;
    public bool IsVisible;
    public bool IsScopeActive;
    public bool HasClipRect;
}

/// <summary>
/// Lightweight attached properties and weak-reference surface registry for snow accumulation.
/// Zero visual-tree walking on frame: controls register themselves via SnowCollider.IsEnabled="True"
/// and automatically clean up when garbage-collected or detached.
/// </summary>
public static class SnowCollider
{
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("IsEnabled", typeof(SnowCollider));

    public static readonly AttachedProperty<bool> OnlyWhenActiveProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("OnlyWhenActive", typeof(SnowCollider));

    public static readonly AttachedProperty<string?> SurfaceIdProperty =
        AvaloniaProperty.RegisterAttached<Control, string?>("SurfaceId", typeof(SnowCollider));

    public static readonly AttachedProperty<string?> PageKeyProperty =
        AvaloniaProperty.RegisterAttached<Control, string?>("PageKey", typeof(SnowCollider), null, inherits: true);

    public static readonly AttachedProperty<bool> IsPageScopeProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("IsPageScope", typeof(SnowCollider));

    public static bool GetIsEnabled(Control control) => control.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(Control control, bool value) => control.SetValue(IsEnabledProperty, value);

    public static bool GetOnlyWhenActive(Control control) => control.GetValue(OnlyWhenActiveProperty);
    public static void SetOnlyWhenActive(Control control, bool value) => control.SetValue(OnlyWhenActiveProperty, value);

    public static string? GetSurfaceId(Control control) => control.GetValue(SurfaceIdProperty);
    public static void SetSurfaceId(Control control, string? value) => control.SetValue(SurfaceIdProperty, value);

    public static string? GetPageKey(Control control) => control.GetValue(PageKeyProperty);
    public static void SetPageKey(Control control, string? value) => control.SetValue(PageKeyProperty, value);

    public static bool GetIsPageScope(Control control) => control.GetValue(IsPageScopeProperty);
    public static void SetIsPageScope(Control control, bool value) => control.SetValue(IsPageScopeProperty, value);

    static SnowCollider()
    {
        IsEnabledProperty.Changed.AddClassHandler<Control>((ctrl, e) =>
            SnowColliderRegistry.OnIsEnabledChanged(ctrl, e.GetNewValue<bool>()));

        OnlyWhenActiveProperty.Changed.AddClassHandler<Control>((ctrl, _) =>
            SnowColliderRegistry.NotifyControlChanged(ctrl));

        SurfaceIdProperty.Changed.AddClassHandler<Control>((ctrl, _) =>
            SnowColliderRegistry.NotifyControlChanged(ctrl));

        PageKeyProperty.Changed.AddClassHandler<Control>((ctrl, _) =>
            SnowColliderRegistry.NotifyControlChanged(ctrl));

        IsPageScopeProperty.Changed.AddClassHandler<Control>((ctrl, e) =>
            SnowColliderRegistry.OnIsPageScopeChanged(ctrl, e.GetNewValue<bool>()));
    }

    public static void NotifySubtreeHiding(Control? root) => SnowColliderRegistry.SetSubtreeForceHidden(root, true);
    public static void NotifySubtreeShowing(Control? root) => SnowColliderRegistry.SetSubtreeForceHidden(root, false);
}

internal static class SnowColliderRegistry
{
    private sealed class ColliderEntry
    {
        public readonly int RuntimeId = Interlocked.Increment(ref _nextRuntimeId);
        public bool IsHooked;
        public bool ClassesHooked;
        public bool WasEverVisible;
        public bool ForceHidden;
        public bool AncestorsCached;
        public Control? CachedPageScope;
        public ScrollContentPresenter? ScrollPresenter1;
        public ScrollViewer? ScrollViewer1;
        public ScrollContentPresenter? ScrollPresenter2;
        public ScrollViewer? ScrollViewer2;
        public EventHandler<VisualTreeAttachmentEventArgs>? AttachedHandler;
        public EventHandler<VisualTreeAttachmentEventArgs>? DetachedHandler;
        public EventHandler<AvaloniaPropertyChangedEventArgs>? PropChangedHandler;
        public NotifyCollectionChangedEventHandler? ClassesChangedHandler;
    }

    private sealed class ScopeEntry
    {
        public bool IsHooked;
        public EventHandler<AvaloniaPropertyChangedEventArgs>? PropChangedHandler;
    }

    private static int _nextRuntimeId;
    private static readonly ConditionalWeakTable<Control, ColliderEntry> Entries = new();
    private static readonly ConditionalWeakTable<Control, ScopeEntry> Scopes = new();
    private static readonly List<WeakReference<Control>> TrackedControls = new();

    private static bool _isCollectionActive;
    public static bool IsCollectionActive
    {
        get => _isCollectionActive;
        set
        {
            if (_isCollectionActive == value) return;
            _isCollectionActive = value;
            if (value)
                RequestSync();
        }
    }

    public static event Action? LayoutDirty;

    private static void RequestSync()
    {
        if (_isCollectionActive)
            LayoutDirty?.Invoke();
    }

    internal static void OnIsPageScopeChanged(Control ctrl, bool isScope)
    {
        if (!isScope) return;
        var scope = Scopes.GetOrCreateValue(ctrl);
        if (!scope.IsHooked)
        {
            scope.IsHooked = true;
            scope.PropChangedHandler = (_, e) =>
            {
                if (e.Property == Visual.IsVisibleProperty)
                    RequestSync();
            };
            ctrl.PropertyChanged += scope.PropChangedHandler;
        }
    }

    internal static void OnIsEnabledChanged(Control ctrl, bool enabled)
    {
        if (enabled)
        {
            var entry = Entries.GetOrCreateValue(ctrl);
            if (!entry.IsHooked)
            {
                entry.IsHooked = true;
                TrackedControls.Add(new WeakReference<Control>(ctrl));

                entry.AttachedHandler = (_, _) =>
                {
                    entry.AncestorsCached = false;
                    entry.ForceHidden = false;
                    RequestSync();
                };
                entry.DetachedHandler = (_, _) =>
                {
                    entry.AncestorsCached = false;
                    entry.CachedPageScope = null;
                    entry.ScrollPresenter1 = null;
                    entry.ScrollViewer1 = null;
                    entry.ScrollPresenter2 = null;
                    entry.ScrollViewer2 = null;
                    RequestSync();
                };
                entry.PropChangedHandler = (_, e) =>
                {
                    if (e.Property == Visual.IsVisibleProperty
                        || e.Property == Visual.OpacityProperty
                        || e.Property == Visual.BoundsProperty)
                    {
                        RequestSync();
                    }
                };
                entry.ClassesChangedHandler = (_, _) =>
                {
                    if (SnowCollider.GetOnlyWhenActive(ctrl))
                        RequestSync();
                };

                ctrl.AttachedToVisualTree += entry.AttachedHandler;
                ctrl.DetachedFromVisualTree += entry.DetachedHandler;
                ctrl.PropertyChanged += entry.PropChangedHandler;
            }

            if (!entry.ClassesHooked)
            {
                entry.ClassesHooked = true;
                ctrl.Classes.CollectionChanged += entry.ClassesChangedHandler;
            }

            if (ctrl.IsAttachedToVisualTree())
            {
                entry.AncestorsCached = false;
                RequestSync();
            }
        }
        else if (Entries.TryGetValue(ctrl, out var entry))
        {
            if (entry.IsHooked)
            {
                if (entry.AttachedHandler != null) ctrl.AttachedToVisualTree -= entry.AttachedHandler;
                if (entry.DetachedHandler != null) ctrl.DetachedFromVisualTree -= entry.DetachedHandler;
                if (entry.PropChangedHandler != null) ctrl.PropertyChanged -= entry.PropChangedHandler;
                if (entry.ClassesHooked && entry.ClassesChangedHandler != null)
                {
                    ctrl.Classes.CollectionChanged -= entry.ClassesChangedHandler;
                    entry.ClassesHooked = false;
                }
                entry.IsHooked = false;
            }
            RequestSync();
        }
    }

    internal static void NotifyControlChanged(Control ctrl)
    {
        if (Entries.TryGetValue(ctrl, out _))
            RequestSync();
    }

    internal static void SetSubtreeForceHidden(Control? root, bool hidden)
    {
        if (root == null) return;
        bool anyChanged = false;

        for (int i = TrackedControls.Count - 1; i >= 0; i--)
        {
            if (!TrackedControls[i].TryGetTarget(out var ctrl))
            {
                TrackedControls.RemoveAt(i);
                continue;
            }

            if (!Entries.TryGetValue(ctrl, out var entry) || !entry.IsHooked)
                continue;

            if (ReferenceEquals(ctrl, root) || root.IsVisualAncestorOf(ctrl))
            {
                if (entry.ForceHidden != hidden)
                {
                    entry.ForceHidden = hidden;
                    anyChanged = true;
                }
            }
        }

        if (anyChanged)
            RequestSync();
    }

    internal static void CollectSnapshots(Visual overlayRoot, List<ColliderSnapshot> output)
    {
        output.Clear();
        if (!_isCollectionActive) return;

        for (int i = TrackedControls.Count - 1; i >= 0; i--)
        {
            if (!TrackedControls[i].TryGetTarget(out var ctrl))
            {
                TrackedControls.RemoveAt(i);
                continue;
            }

            if (!Entries.TryGetValue(ctrl, out var entry) || !entry.IsHooked)
            {
                TrackedControls.RemoveAt(i);
                continue;
            }

            if (!SnowCollider.GetIsEnabled(ctrl) || !ctrl.IsAttachedToVisualTree())
                continue;

            var surfaceId = SnowCollider.GetSurfaceId(ctrl);
            if (string.IsNullOrWhiteSpace(surfaceId))
                continue;

            var pageKey = SnowCollider.GetPageKey(ctrl) ?? "Default";

            if (!entry.AncestorsCached)
                CacheAncestors(ctrl, entry, overlayRoot);

            bool scopeActive = entry.CachedPageScope == null
                || (entry.CachedPageScope.IsAttachedToVisualTree() && entry.CachedPageScope.IsEffectivelyVisible);

            bool onlyWhenActive = SnowCollider.GetOnlyWhenActive(ctrl);
            bool isClassActive = !onlyWhenActive || ctrl.Classes.Contains("active");
            bool isVisible = scopeActive
                && !entry.ForceHidden
                && isClassActive
                && ctrl.IsEffectivelyVisible
                && ctrl.Opacity > 0.05;

            if (!isVisible)
            {
                if (entry.WasEverVisible)
                {
                    output.Add(new ColliderSnapshot
                    {
                        RuntimeId = entry.RuntimeId,
                        PageKey = pageKey,
                        SurfaceId = surfaceId,
                        IsVisible = false,
                        IsScopeActive = scopeActive
                    });
                }
                continue;
            }

            var bounds = ctrl.Bounds;
            if (bounds.Width < 8 || bounds.Height < 8)
                continue;

            var matrix = ctrl.TransformToVisual(overlayRoot);
            if (!matrix.HasValue)
                continue;

            var p0 = new Point(0, 0).Transform(matrix.Value);
            var p1 = new Point(bounds.Width, bounds.Height).Transform(matrix.Value);
            float left = (float)Math.Min(p0.X, p1.X);
            float right = (float)Math.Max(p0.X, p1.X);
            float top = (float)Math.Min(p0.Y, p1.Y);
            float bottom = (float)Math.Max(p0.Y, p1.Y);

            if (right - left < 8f || bottom - top < 8f)
                continue;

            float scaleX = (float)((right - left) / Math.Max(1.0, bounds.Width));
            float crLeft = 0f;
            float crRight = 0f;
            if (ctrl is Border border)
            {
                crLeft = (float)border.CornerRadius.TopLeft * scaleX;
                crRight = (float)border.CornerRadius.TopRight * scaleX;
            }
            else if (ctrl is TemplatedControl tc)
            {
                crLeft = (float)tc.CornerRadius.TopLeft * scaleX;
                crRight = (float)tc.CornerRadius.TopRight * scaleX;
            }

            bool hasClip = false;
            float clipLeft = 0f;
            float clipTop = 0f;
            float clipRight = float.MaxValue;
            float clipBottom = float.MaxValue;
            float scrollOffsetX = 0f;
            float scrollOffsetY = 0f;

            if (entry.ScrollPresenter1 != null)
            {
                ApplyScrollClip(
                    entry.ScrollPresenter1,
                    entry.ScrollViewer1,
                    overlayRoot,
                    ref hasClip,
                    ref clipLeft,
                    ref clipTop,
                    ref clipRight,
                    ref clipBottom,
                    ref scrollOffsetX,
                    ref scrollOffsetY);
            }

            if (entry.ScrollPresenter2 != null)
            {
                ApplyScrollClip(
                    entry.ScrollPresenter2,
                    entry.ScrollViewer2,
                    overlayRoot,
                    ref hasClip,
                    ref clipLeft,
                    ref clipTop,
                    ref clipRight,
                    ref clipBottom,
                    ref scrollOffsetX,
                    ref scrollOffsetY);
            }

            entry.WasEverVisible = true;
            output.Add(new ColliderSnapshot
            {
                RuntimeId = entry.RuntimeId,
                PageKey = pageKey,
                SurfaceId = surfaceId,
                Left = left,
                Top = top,
                Right = right,
                Bottom = bottom,
                ClipLeft = clipLeft,
                ClipTop = clipTop,
                ClipRight = clipRight,
                ClipBottom = clipBottom,
                CornerRadiusLeft = crLeft,
                CornerRadiusRight = crRight,
                ScrollOffsetX = scrollOffsetX,
                ScrollOffsetY = scrollOffsetY,
                IsVisible = true,
                IsScopeActive = scopeActive,
                HasClipRect = hasClip
            });
        }
    }

    private static void CacheAncestors(Control ctrl, ColliderEntry entry, Visual overlayRoot)
    {
        entry.AncestorsCached = true;
        entry.CachedPageScope = null;
        entry.ScrollPresenter1 = null;
        entry.ScrollViewer1 = null;
        entry.ScrollPresenter2 = null;
        entry.ScrollViewer2 = null;

        Visual? cur = ctrl.GetVisualParent();
        while (cur != null && !ReferenceEquals(cur, overlayRoot))
        {
            if (entry.CachedPageScope == null && cur is Control scopeCtrl && SnowCollider.GetIsPageScope(scopeCtrl))
            {
                entry.CachedPageScope = scopeCtrl;
            }

            if (cur is ScrollContentPresenter scp)
            {
                var sv = scp.FindAncestorOfType<ScrollViewer>();
                if (entry.ScrollPresenter1 == null)
                {
                    entry.ScrollPresenter1 = scp;
                    entry.ScrollViewer1 = sv;
                }
                else if (entry.ScrollPresenter2 == null)
                {
                    entry.ScrollPresenter2 = scp;
                    entry.ScrollViewer2 = sv;
                }
            }

            cur = cur.GetVisualParent();
        }
    }

    private static void ApplyScrollClip(
        ScrollContentPresenter scp,
        ScrollViewer? sv,
        Visual overlayRoot,
        ref bool hasClip,
        ref float clipLeft,
        ref float clipTop,
        ref float clipRight,
        ref float clipBottom,
        ref float scrollOffsetX,
        ref float scrollOffsetY)
    {
        if (!scp.IsAttachedToVisualTree() || !scp.IsEffectivelyVisible)
            return;

        var sb = scp.Bounds;
        if (sb.Width <= 0 || sb.Height <= 0)
            return;

        var sm = scp.TransformToVisual(overlayRoot);
        if (!sm.HasValue)
            return;

        var sp0 = new Point(0, 0).Transform(sm.Value);
        var sp1 = new Point(sb.Width, sb.Height).Transform(sm.Value);
        float sLeft = (float)Math.Min(sp0.X, sp1.X);
        float sRight = (float)Math.Max(sp0.X, sp1.X);
        float sTop = (float)Math.Min(sp0.Y, sp1.Y);
        float sBottom = (float)Math.Max(sp0.Y, sp1.Y);

        double offY = sv?.Offset.Y ?? 0.0;
        double offX = sv?.Offset.X ?? 0.0;
        // Allow slight headroom at top when scroll is at rest (Offset.Y < 1) so snow sits above first-row buttons inside ScrollViewer.
        if (offY < 1.0)
            sTop -= 12f;

        if (!hasClip)
        {
            hasClip = true;
            clipLeft = sLeft;
            clipTop = sTop;
            clipRight = sRight;
            clipBottom = sBottom;
            scrollOffsetX = (float)offX;
            scrollOffsetY = (float)offY;
        }
        else
        {
            clipLeft = Math.Max(clipLeft, sLeft);
            clipTop = Math.Max(clipTop, sTop);
            clipRight = Math.Min(clipRight, sRight);
            clipBottom = Math.Min(clipBottom, sBottom);
            scrollOffsetX += (float)offX;
            scrollOffsetY += (float)offY;
        }
    }
}
