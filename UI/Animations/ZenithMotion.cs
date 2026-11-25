using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Rendering.Composition;
using Avalonia.Rendering.Composition.Animations;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace ZenithLauncher.UI.Animations;

/// <summary>
/// Render-thread UI motion engine powered by Avalonia 12 Composition API.
/// Supports mid-flight interruption via monotonic generation tokens, layout-safe CenterPoint
/// initialization, lazy-view attachment safety, and instant global disablement without restart.
/// </summary>
public static class ZenithMotion
{
    public static readonly AttachedProperty<bool> ModalOpenProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("ModalOpen", typeof(ZenithMotion));

    public static readonly AttachedProperty<bool> SlidePanelOpenProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("SlidePanelOpen", typeof(ZenithMotion));

    public static readonly AttachedProperty<bool> ToastOpenProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("ToastOpen", typeof(ZenithMotion));

    public static readonly AttachedProperty<bool> RevealOnAttachProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("RevealOnAttach", typeof(ZenithMotion));

    // Fluent/Apple decelerate enter & accelerate exit + critically-damped spatial spring.
    private static readonly SplineEasing EnterDecelerate = new(0.05, 0.9, 0.1, 1.0);
    private static readonly SplineEasing ExitAccelerate = new(0.3, 0.0, 0.8, 0.15);
    private static readonly SpringEasing SpatialSpring = new(1.0, 320.0, 35.0, 0.0);

    private static readonly Vector3D ZeroTranslation = default;
    private static readonly Vector3D UnitScale = new(1.0, 1.0, 1.0);

    private sealed class MotionTrack
    {
        public uint Generation;
        public bool HasInitializedOpen;
        public bool IsOpen;
        public bool IsAnimating;
        public long StartTicks;
        public double DurationMs;
        public Vector3D FromTranslation;
        public Vector3D ToTranslation;
        public Vector3D FromScale = UnitScale;
        public Vector3D ToScale = UnitScale;
        public float FromOpacity = 1f;
        public float ToOpacity = 1f;
        public Func<double, double> SpatialCurve = Linear;
        public Func<double, double> OpacityCurve = Linear;
        public Action? CleanupPending;
        public Control? SecondaryTarget;
    }

    private static readonly ConditionalWeakTable<Control, MotionTrack> Tracks = new();
    private static readonly List<WeakReference<Control>> ActiveControls = new();
    private static readonly List<WeakReference<Window>> RegisteredWindows = new();

    private static bool _isEnabled = true;

    public static bool IsEnabled => _isEnabled;
    public static event Action<bool>? EnabledChanged;

    static ZenithMotion()
    {
        ModalOpenProperty.Changed.AddClassHandler<Control>((ctrl, e) =>
            HandleModalOpenChanged(ctrl, e.GetNewValue<bool>()));
        SlidePanelOpenProperty.Changed.AddClassHandler<Control>((ctrl, e) =>
            AnimateSlidePanel(ctrl, e.GetNewValue<bool>()));
        ToastOpenProperty.Changed.AddClassHandler<Control>((ctrl, e) =>
            HandleToastOpenChanged(ctrl, e.GetNewValue<bool>()));
        RevealOnAttachProperty.Changed.AddClassHandler<Control>(OnRevealOnAttachChanged);
    }

    public static bool GetModalOpen(Control control) => control.GetValue(ModalOpenProperty);
    public static void SetModalOpen(Control control, bool value) => control.SetValue(ModalOpenProperty, value);

    public static bool GetSlidePanelOpen(Control control) => control.GetValue(SlidePanelOpenProperty);
    public static void SetSlidePanelOpen(Control control, bool value) =>
        control.SetValue(SlidePanelOpenProperty, value);

    public static bool GetToastOpen(Control control) => control.GetValue(ToastOpenProperty);
    public static void SetToastOpen(Control control, bool value) =>
        control.SetValue(ToastOpenProperty, value);

    public static bool GetRevealOnAttach(Control control) => control.GetValue(RevealOnAttachProperty);
    public static void SetRevealOnAttach(Control control, bool value) =>
        control.SetValue(RevealOnAttachProperty, value);

    public static void RegisterWindow(Window window)
    {
        if (window == null) return;
        PruneDeadWindows();
        if (!RegisteredWindows.Any(w => w.TryGetTarget(out var target) && ReferenceEquals(target, window)))
        {
            RegisteredWindows.Add(new WeakReference<Window>(window));
            window.Closed += (_, _) => PruneDeadWindows();
        }
        window.Classes.Set("noMotion", !_isEnabled);
    }

    public static void SetEnabled(bool enabled)
    {
        if (_isEnabled == enabled) return;
        _isEnabled = enabled;

        void ApplyToggle()
        {
            SyncWindowClasses(enabled);

            if (!enabled)
            {
                CancelAllActiveAnimations();
            }

            EnabledChanged?.Invoke(enabled);
        }

        if (Dispatcher.UIThread.CheckAccess())
            ApplyToggle();
        else
            Dispatcher.UIThread.Post(ApplyToggle);
    }

    /// <summary>
    /// Animates a newly opened/navigated view into place using render-thread Translation/Scale/Opacity.
    /// Safe for lazy-mounted controls not yet attached or laid out.
    /// </summary>
    public static void AnimateEntrance(
        Control? control,
        double offsetX = 0,
        double offsetY = 0,
        double startScale = 1.0,
        double startOpacity = 0.2,
        int durationMs = 175)
    {
        if (control == null || control is Window) return;
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => AnimateEntrance(control, offsetX, offsetY, startScale, startOpacity, durationMs));
            return;
        }

        var track = GetOrCreateTrack(control);
        CancelPending(track);
        uint gen = ++track.Generation;
        track.IsOpen = true;
        track.HasInitializedOpen = true;

        if (!_isEnabled || durationMs <= 0)
        {
            SnapToRest(control, track);
            return;
        }

        bool needsBounds = Math.Abs(startScale - 1.0) > 0.001;
        RunWhenReady(control, track, gen, needsBounds, (ctrl, comp) =>
        {
            if (!_isEnabled || track.Generation != gen)
            {
                if (!_isEnabled) SnapToRest(ctrl, track);
                return;
            }

            Vector3D fromTrans;
            Vector3D fromScale;
            float fromOp;
            if (track.IsAnimating)
            {
                SampleCurrent(track, out fromTrans, out fromScale, out fromOp);
            }
            else
            {
                fromTrans = new Vector3D(offsetX, offsetY, 0);
                fromScale = new Vector3D(startScale, startScale, 1.0);
                fromOp = (float)Math.Clamp(startOpacity, 0.0, 1.0);
            }

            if (needsBounds && ctrl.Bounds.Width > 0 && ctrl.Bounds.Height > 0)
                comp.CenterPoint = new Vector3D(ctrl.Bounds.Width * 0.5, ctrl.Bounds.Height * 0.5, 0);

            Easing spatialEasing = needsBounds || Math.Abs(offsetX) >= 12.0 || Math.Abs(offsetY) >= 12.0
                ? SpatialSpring
                : EnterDecelerate;

            StartCompositionTween(
                ctrl,
                comp,
                track,
                durationMs,
                fromTrans,
                ZeroTranslation,
                fromScale,
                UnitScale,
                fromOp,
                1f,
                spatialEasing,
                EnterDecelerate,
                animateScale: needsBounds || Math.Abs(fromScale.X - 1.0) > 0.001);
        });
    }

    /// <summary>
    /// Subtle micro-shift for in-page tabs (Settings, Instance Edit, Project Details, Import tabs).
    /// </summary>
    public static void TransitionTab(
        Control? control,
        double offsetX = 0,
        double offsetY = 6,
        double startOpacity = 0.35,
        int durationMs = 115)
    {
        AnimateEntrance(control, offsetX, offsetY, startScale: 1.0, startOpacity: startOpacity, durationMs: durationMs);
    }

    /// <summary>
    /// Interruptible modal open/close animation coordinating backdrop fade and centered card lift/scale.
    /// </summary>
    public static void HandleModalOpenChanged(Control? overlay, bool isOpen)
    {
        if (overlay == null) return;
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => HandleModalOpenChanged(overlay, isOpen));
            return;
        }

        var track = GetOrCreateTrack(overlay);
        CancelPending(track);

        var card = ResolveModalCard(overlay);
        track.SecondaryTarget = card;

        if (!track.HasInitializedOpen)
        {
            track.HasInitializedOpen = true;
            if (!isOpen)
            {
                track.Generation++;
                track.IsOpen = false;
                track.IsAnimating = false;
                overlay.IsVisible = false;
                overlay.IsHitTestVisible = false;
                return;
            }
        }

        if (track.HasInitializedOpen && track.IsOpen == isOpen && (isOpen == overlay.IsVisible) && !track.IsAnimating)
        {
            return;
        }

        uint gen = ++track.Generation;
        track.IsOpen = isOpen;

        var cardTrack = card != null ? GetOrCreateTrack(card) : null;
        if (cardTrack != null)
        {
            CancelPending(cardTrack);
            cardTrack.Generation++;
            cardTrack.IsOpen = isOpen;
        }

        if (!_isEnabled)
        {
            overlay.IsVisible = isOpen;
            overlay.IsHitTestVisible = isOpen;
            SnapToRest(overlay, track);
            if (card != null && cardTrack != null) SnapToRest(card, cardTrack);
            return;
        }

        if (isOpen)
        {
            overlay.IsVisible = true;
            overlay.IsHitTestVisible = true;

            Control layoutWaitTarget = card ?? overlay;
            RunWhenReady(layoutWaitTarget, track, gen, needsBounds: card != null, (targetCtrl, unusedComp) =>
            {
                if (!_isEnabled || track.Generation != gen || !track.IsOpen)
                {
                    if (!_isEnabled)
                    {
                        overlay.IsVisible = track.IsOpen;
                        overlay.IsHitTestVisible = track.IsOpen;
                        SnapToRest(overlay, track);
                        if (card != null && cardTrack != null) SnapToRest(card, cardTrack);
                    }
                    return;
                }

                var ovComp = ElementComposition.GetElementVisual(overlay);
                if (ovComp == null)
                {
                    SnapToRest(overlay, track);
                    return;
                }

                SampleCurrent(track, out _, out _, out float ovFromOp);
                if (!track.IsAnimating) ovFromOp = 0f;
                int ovMs = ScaledDuration(165, 1.0 - ovFromOp, 0.25);

                StartCompositionTween(
                    overlay,
                    ovComp,
                    track,
                    ovMs,
                    ZeroTranslation,
                    ZeroTranslation,
                    UnitScale,
                    UnitScale,
                    ovFromOp,
                    1f,
                    EnterDecelerate,
                    EnterDecelerate,
                    animateScale: false,
                    animateTranslation: false);

                if (card != null && cardTrack != null)
                {
                    var cardComp = ElementComposition.GetElementVisual(card);
                    if (cardComp != null)
                    {
                        if (card.Bounds.Width > 0 && card.Bounds.Height > 0)
                            cardComp.CenterPoint = new Vector3D(card.Bounds.Width * 0.5, card.Bounds.Height * 0.5, 0);

                        Vector3D fromTrans;
                        Vector3D fromScale;
                        float cardFromOp;
                        if (cardTrack.IsAnimating)
                        {
                            SampleCurrent(cardTrack, out fromTrans, out fromScale, out cardFromOp);
                        }
                        else
                        {
                            fromTrans = new Vector3D(0, 8, 0);
                            fromScale = new Vector3D(0.96, 0.96, 1.0);
                            cardFromOp = 0f;
                        }

                        int cardMs = ScaledDuration(185, 1.0 - cardFromOp, 0.25);
                        StartCompositionTween(
                            card,
                            cardComp,
                            cardTrack,
                            cardMs,
                            fromTrans,
                            ZeroTranslation,
                            fromScale,
                            UnitScale,
                            cardFromOp,
                            1f,
                            SpatialSpring,
                            EnterDecelerate,
                            animateScale: true);
                    }
                }
            });
        }
        else
        {
            // Exit immediately yields input so clicks pass through to underlying UI.
            overlay.IsHitTestVisible = false;

            var ovComp = overlay.IsAttachedToVisualTree() ? ElementComposition.GetElementVisual(overlay) : null;
            if (!overlay.IsVisible || ovComp == null)
            {
                overlay.IsVisible = false;
                SnapToRest(overlay, track);
                if (card != null && cardTrack != null) SnapToRest(card, cardTrack);
                return;
            }

            SampleCurrent(track, out _, out _, out float ovFromOp);
            if (!track.IsAnimating) ovFromOp = 1f;
            int exitMs = ScaledDuration(105, ovFromOp, 0.3);

            StartCompositionTween(
                overlay,
                ovComp,
                track,
                exitMs,
                ZeroTranslation,
                ZeroTranslation,
                UnitScale,
                UnitScale,
                ovFromOp,
                0f,
                ExitAccelerate,
                ExitAccelerate,
                animateScale: false,
                animateTranslation: false);

            if (card != null && cardTrack != null)
            {
                var cardComp = card.IsAttachedToVisualTree() ? ElementComposition.GetElementVisual(card) : null;
                if (cardComp != null)
                {
                    if (card.Bounds.Width > 0 && card.Bounds.Height > 0)
                        cardComp.CenterPoint = new Vector3D(card.Bounds.Width * 0.5, card.Bounds.Height * 0.5, 0);

                    Vector3D fromTrans;
                    Vector3D fromScale;
                    float cardFromOp;
                    if (cardTrack.IsAnimating)
                    {
                        SampleCurrent(cardTrack, out fromTrans, out fromScale, out cardFromOp);
                    }
                    else
                    {
                        fromTrans = ZeroTranslation;
                        fromScale = UnitScale;
                        cardFromOp = 1f;
                    }

                    StartCompositionTween(
                        card,
                        cardComp,
                        cardTrack,
                        exitMs,
                        fromTrans,
                        new Vector3D(0, 5, 0),
                        fromScale,
                        new Vector3D(0.97, 0.97, 1.0),
                        cardFromOp,
                        0f,
                        ExitAccelerate,
                        ExitAccelerate,
                        animateScale: true);
                }
            }

            _ = FinishExitAfterAsync(overlay, track, card, cardTrack, gen, ovComp.Compositor, exitMs);
        }
    }

    /// <summary>
    /// Interruptible slide/fade animation for persistent sidebars and sliding sub-panels.
    /// </summary>
    public static void AnimateSlidePanel(
        Control? panel,
        bool isOpen,
        double slideX = 24,
        double slideY = 0,
        int enterMs = 185,
        int exitMs = 115)
    {
        if (panel == null) return;
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => AnimateSlidePanel(panel, isOpen, slideX, slideY, enterMs, exitMs));
            return;
        }

        var track = GetOrCreateTrack(panel);
        CancelPending(track);

        if (!track.HasInitializedOpen)
        {
            track.HasInitializedOpen = true;
            if (!isOpen)
            {
                track.Generation++;
                track.IsOpen = false;
                track.IsAnimating = false;
                panel.IsVisible = false;
                panel.IsHitTestVisible = false;
                return;
            }
        }

        uint gen = ++track.Generation;
        track.IsOpen = isOpen;

        if (!_isEnabled)
        {
            panel.IsVisible = isOpen;
            panel.IsHitTestVisible = isOpen;
            SnapToRest(panel, track);
            return;
        }

        if (isOpen)
        {
            panel.IsVisible = true;
            panel.IsHitTestVisible = true;

            RunWhenReady(panel, track, gen, needsBounds: false, (ctrl, comp) =>
            {
                if (!_isEnabled || track.Generation != gen || !track.IsOpen)
                {
                    if (!_isEnabled)
                    {
                        panel.IsVisible = track.IsOpen;
                        panel.IsHitTestVisible = track.IsOpen;
                        SnapToRest(panel, track);
                    }
                    return;
                }

                Vector3D fromTrans;
                float fromOp;
                if (track.IsAnimating)
                {
                    SampleCurrent(track, out fromTrans, out _, out fromOp);
                }
                else
                {
                    fromTrans = new Vector3D(slideX, slideY, 0);
                    fromOp = 0f;
                }

                int ms = ScaledDuration(enterMs, 1.0 - fromOp, 0.25);
                StartCompositionTween(
                    ctrl,
                    comp,
                    track,
                    ms,
                    fromTrans,
                    ZeroTranslation,
                    UnitScale,
                    UnitScale,
                    fromOp,
                    1f,
                    SpatialSpring,
                    EnterDecelerate,
                    animateScale: false);
            });
        }
        else
        {
            panel.IsHitTestVisible = false;

            var comp = panel.IsAttachedToVisualTree() ? ElementComposition.GetElementVisual(panel) : null;
            if (!panel.IsVisible || comp == null)
            {
                panel.IsVisible = false;
                SnapToRest(panel, track);
                return;
            }

            Vector3D fromTrans;
            float fromOp;
            if (track.IsAnimating)
            {
                SampleCurrent(track, out fromTrans, out _, out fromOp);
            }
            else
            {
                fromTrans = ZeroTranslation;
                fromOp = 1f;
            }

            int ms = ScaledDuration(exitMs, fromOp, 0.3);
            StartCompositionTween(
                panel,
                comp,
                track,
                ms,
                fromTrans,
                new Vector3D(slideX * 0.75, slideY * 0.75, 0),
                UnitScale,
                UnitScale,
                fromOp,
                0f,
                ExitAccelerate,
                ExitAccelerate,
                animateScale: false);

            _ = FinishExitAfterAsync(panel, track, null, null, gen, comp.Compositor, ms);
        }
    }

    /// <summary>
    /// Non-intrusive centered toast reveal &amp; dismiss animation.
    /// </summary>
    public static void HandleToastOpenChanged(Control? toast, bool isVisible)
    {
        if (toast == null) return;
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => HandleToastOpenChanged(toast, isVisible));
            return;
        }

        var track = GetOrCreateTrack(toast);
        CancelPending(track);

        if (!track.HasInitializedOpen)
        {
            track.HasInitializedOpen = true;
            if (!isVisible)
            {
                track.Generation++;
                track.IsOpen = false;
                track.IsAnimating = false;
                toast.IsVisible = false;
                return;
            }
        }

        uint gen = ++track.Generation;
        track.IsOpen = isVisible;

        if (!_isEnabled)
        {
            toast.IsVisible = isVisible;
            SnapToRest(toast, track);
            return;
        }

        if (isVisible)
        {
            toast.IsVisible = true;
            RunWhenReady(toast, track, gen, needsBounds: true, (ctrl, comp) =>
            {
                if (!_isEnabled || track.Generation != gen || !track.IsOpen)
                {
                    if (!_isEnabled)
                    {
                        toast.IsVisible = track.IsOpen;
                        SnapToRest(toast, track);
                    }
                    return;
                }

                if (ctrl.Bounds.Width > 0 && ctrl.Bounds.Height > 0)
                    comp.CenterPoint = new Vector3D(ctrl.Bounds.Width * 0.5, ctrl.Bounds.Height * 0.5, 0);

                Vector3D fromTrans;
                Vector3D fromScale;
                float fromOp;
                if (track.IsAnimating)
                {
                    SampleCurrent(track, out fromTrans, out fromScale, out fromOp);
                }
                else
                {
                    fromTrans = new Vector3D(0, 12, 0);
                    fromScale = new Vector3D(0.97, 0.97, 1.0);
                    fromOp = 0f;
                }

                int ms = ScaledDuration(175, 1.0 - fromOp, 0.25);
                StartCompositionTween(
                    ctrl,
                    comp,
                    track,
                    ms,
                    fromTrans,
                    ZeroTranslation,
                    fromScale,
                    UnitScale,
                    fromOp,
                    1f,
                    EnterDecelerate,
                    EnterDecelerate,
                    animateScale: true);
            });
        }
        else
        {
            var comp = toast.IsAttachedToVisualTree() ? ElementComposition.GetElementVisual(toast) : null;
            if (!toast.IsVisible || comp == null)
            {
                toast.IsVisible = false;
                SnapToRest(toast, track);
                return;
            }

            if (toast.Bounds.Width > 0 && toast.Bounds.Height > 0)
                comp.CenterPoint = new Vector3D(toast.Bounds.Width * 0.5, toast.Bounds.Height * 0.5, 0);

            Vector3D fromTrans;
            Vector3D fromScale;
            float fromOp;
            if (track.IsAnimating)
            {
                SampleCurrent(track, out fromTrans, out fromScale, out fromOp);
            }
            else
            {
                fromTrans = ZeroTranslation;
                fromScale = UnitScale;
                fromOp = 1f;
            }

            int ms = ScaledDuration(115, fromOp, 0.3);
            StartCompositionTween(
                toast,
                comp,
                track,
                ms,
                fromTrans,
                new Vector3D(0, 8, 0),
                fromScale,
                new Vector3D(0.98, 0.98, 1.0),
                fromOp,
                0f,
                ExitAccelerate,
                ExitAccelerate,
                animateScale: true);

            _ = FinishExitAfterAsync(toast, track, null, null, gen, comp.Compositor, ms);
        }
    }

    private static void OnRevealOnAttachChanged(Control control, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.GetNewValue<bool>())
        {
            control.AttachedToVisualTree -= OnRevealControlAttached;
            control.AttachedToVisualTree += OnRevealControlAttached;

            if (control.IsAttachedToVisualTree() && _isEnabled)
            {
                AnimateEntrance(control, offsetX: 0, offsetY: 4, startScale: 1.0, startOpacity: 0.15, durationMs: 105);
            }
        }
        else
        {
            control.AttachedToVisualTree -= OnRevealControlAttached;
        }
    }

    private static void OnRevealControlAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is Control control && _isEnabled)
        {
            AnimateEntrance(control, offsetX: 0, offsetY: 4, startScale: 1.0, startOpacity: 0.15, durationMs: 105);
        }
    }

    private static Control? ResolveModalCard(Control overlay)
    {
        if (overlay is Panel panel && panel.Children.Count == 1 && panel.Children[0] is Control child)
            return child;
        if (overlay is Decorator decorator && decorator.Child is Control decChild)
            return decChild;
        return null;
    }

    private static void RunWhenReady(
        Control target,
        MotionTrack track,
        uint gen,
        bool needsBounds,
        Action<Control, CompositionVisual> onReady)
    {
        bool IsSizeSatisfied() => !needsBounds || (target.Bounds.Width > 0 && target.Bounds.Height > 0);

        if (target.IsAttachedToVisualTree() && IsSizeSatisfied())
        {
            var comp = ElementComposition.GetElementVisual(target);
            if (comp != null)
            {
                onReady(target, comp);
                return;
            }
        }

        EventHandler<VisualTreeAttachmentEventArgs>? onAttached = null;
        EventHandler<VisualTreeAttachmentEventArgs>? onDetached = null;
        EventHandler? onLayoutUpdated = null;
        Action? detachAction = null;

        void DetachHandlers()
        {
            target.AttachedToVisualTree -= onAttached;
            target.DetachedFromVisualTree -= onDetached;
            target.LayoutUpdated -= onLayoutUpdated;
            if (ReferenceEquals(track.CleanupPending, detachAction))
                track.CleanupPending = null;
        }

        detachAction = DetachHandlers;

        void TryComplete()
        {
            if (track.Generation != gen)
            {
                DetachHandlers();
                return;
            }

            if (!target.IsAttachedToVisualTree() || !IsSizeSatisfied())
                return;

            var comp = ElementComposition.GetElementVisual(target);
            if (comp == null) return;

            DetachHandlers();
            onReady(target, comp);
        }

        onAttached = (_, _) => TryComplete();
        onLayoutUpdated = (_, _) => TryComplete();
        onDetached = (_, _) => DetachHandlers();

        track.CleanupPending = detachAction;
        target.AttachedToVisualTree += onAttached;
        target.LayoutUpdated += onLayoutUpdated;
        target.DetachedFromVisualTree += onDetached;
    }

    private static async Task FinishExitAfterAsync(
        Control target,
        MotionTrack track,
        Control? secondary,
        MotionTrack? secondaryTrack,
        uint gen,
        Compositor compositor,
        int durationMs)
    {
        try
        {
            await compositor.RequestCommitAsync();
            await Task.Delay(durationMs + 16);
        }
        catch
        {
            return;
        }

        void Complete()
        {
            if (track.Generation != gen || track.IsOpen)
                return;

            target.IsVisible = false;
            target.IsHitTestVisible = false;
            SnapToRest(target, track);
            if (secondary != null && secondaryTrack != null)
                SnapToRest(secondary, secondaryTrack);
        }

        if (Dispatcher.UIThread.CheckAccess())
            Complete();
        else
            Dispatcher.UIThread.Post(Complete);
    }

    private static void StartCompositionTween(
        Control control,
        CompositionVisual comp,
        MotionTrack track,
        int durationMs,
        Vector3D fromTranslation,
        Vector3D toTranslation,
        Vector3D fromScale,
        Vector3D toScale,
        float fromOpacity,
        float toOpacity,
        Easing spatialEasing,
        Easing opacityEasing,
        bool animateScale,
        bool animateTranslation = true)
    {
        comp.StopAnimation("Translation");
        comp.StopAnimation("Scale");
        comp.StopAnimation("Opacity");

        var dur = TimeSpan.FromMilliseconds(Math.Max(16, durationMs));
        var compositor = comp.Compositor;

        track.StartTicks = Stopwatch.GetTimestamp();
        track.DurationMs = durationMs;
        track.FromTranslation = fromTranslation;
        track.ToTranslation = toTranslation;
        track.FromScale = fromScale;
        track.ToScale = toScale;
        track.FromOpacity = fromOpacity;
        track.ToOpacity = toOpacity;
        track.SpatialCurve = t => spatialEasing.Ease(t);
        track.OpacityCurve = t => opacityEasing.Ease(t);
        track.IsAnimating = true;

        if (animateTranslation)
        {
            var transAnim = compositor.CreateVector3DKeyFrameAnimation();
            transAnim.Target = "Translation";
            transAnim.Duration = dur;
            transAnim.StopBehavior = AnimationStopBehavior.SetToFinalValue;
            transAnim.InsertKeyFrame(0f, fromTranslation);
            transAnim.InsertKeyFrame(1f, toTranslation, spatialEasing);
            comp.StartAnimation("Translation", transAnim);
        }

        if (animateScale)
        {
            var scaleAnim = compositor.CreateVector3DKeyFrameAnimation();
            scaleAnim.Target = "Scale";
            scaleAnim.Duration = dur;
            scaleAnim.StopBehavior = AnimationStopBehavior.SetToFinalValue;
            scaleAnim.InsertKeyFrame(0f, fromScale);
            scaleAnim.InsertKeyFrame(1f, toScale, spatialEasing);
            comp.StartAnimation("Scale", scaleAnim);
        }

        if (Math.Abs(fromOpacity - toOpacity) > 0.001f)
        {
            var opAnim = compositor.CreateScalarKeyFrameAnimation();
            opAnim.Target = "Opacity";
            opAnim.Duration = dur;
            opAnim.StopBehavior = AnimationStopBehavior.SetToFinalValue;
            opAnim.InsertKeyFrame(0f, fromOpacity);
            opAnim.InsertKeyFrame(1f, toOpacity, opacityEasing);
            comp.StartAnimation("Opacity", opAnim);
        }

        uint capturedGen = track.Generation;
        _ = MarkAnimationSettledAsync(control, track, capturedGen, durationMs);
    }

    private static async Task MarkAnimationSettledAsync(Control control, MotionTrack track, uint gen, int durationMs)
    {
        try
        {
            await Task.Delay(durationMs + 20);
        }
        catch
        {
            return;
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            if (track.Generation == gen)
                track.IsAnimating = false;
        }
        else
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (track.Generation == gen)
                    track.IsAnimating = false;
            });
        }
    }

    private static void SnapToRest(Control control, MotionTrack track)
    {
        track.IsAnimating = false;
        CancelPending(track);

        var comp = control.IsAttachedToVisualTree() ? ElementComposition.GetElementVisual(control) : null;
        if (comp != null)
        {
            comp.StopAnimation("Translation");
            comp.StopAnimation("Scale");
            comp.StopAnimation("Opacity");
            comp.Translation = ZeroTranslation;
            comp.Scale = UnitScale;
            comp.Opacity = 1f;
        }
    }

    private static void SampleCurrent(MotionTrack track, out Vector3D translation, out Vector3D scale, out float opacity)
    {
        if (!track.IsAnimating || track.DurationMs <= 0)
        {
            translation = track.ToTranslation;
            scale = track.ToScale;
            opacity = track.ToOpacity;
            return;
        }

        double elapsedMs = Stopwatch.GetElapsedTime(track.StartTicks).TotalMilliseconds;
        double rawT = Math.Clamp(elapsedMs / track.DurationMs, 0.0, 1.0);
        double st = track.SpatialCurve(rawT);
        double ot = track.OpacityCurve(rawT);

        translation = LerpVector(track.FromTranslation, track.ToTranslation, st);
        scale = LerpVector(track.FromScale, track.ToScale, st);
        opacity = (float)Math.Clamp(track.FromOpacity + (track.ToOpacity - track.FromOpacity) * ot, 0.0, 1.0);
    }

    private static Vector3D LerpVector(Vector3D a, Vector3D b, double t) =>
        new(
            a.X + (b.X - a.X) * t,
            a.Y + (b.Y - a.Y) * t,
            a.Z + (b.Z - a.Z) * t);

    private static int ScaledDuration(int baseMs, double remainingFraction, double minFraction) =>
        Math.Max(40, (int)Math.Round(baseMs * Math.Clamp(remainingFraction, minFraction, 1.0)));

    private static double Linear(double t) => t;

    private static void CancelPending(MotionTrack track)
    {
        var pending = track.CleanupPending;
        track.CleanupPending = null;
        pending?.Invoke();
    }

    private static MotionTrack GetOrCreateTrack(Control control)
    {
        if (Tracks.TryGetValue(control, out var existing))
            return existing;

        var created = new MotionTrack();
        Tracks.Add(control, created);
        ActiveControls.Add(new WeakReference<Control>(control));
        return created;
    }

    private static void CancelAllActiveAnimations()
    {
        for (int i = ActiveControls.Count - 1; i >= 0; i--)
        {
            if (!ActiveControls[i].TryGetTarget(out var control))
            {
                ActiveControls.RemoveAt(i);
                continue;
            }

            if (!Tracks.TryGetValue(control, out var track))
                continue;

            track.Generation++;
            CancelPending(track);

            if (track.HasInitializedOpen)
            {
                control.IsVisible = track.IsOpen;
                if (track.SecondaryTarget != null || !track.IsOpen)
                    control.IsHitTestVisible = track.IsOpen;
            }

            SnapToRest(control, track);
        }
    }

    private static void SyncWindowClasses(bool enabled)
    {
        for (int i = RegisteredWindows.Count - 1; i >= 0; i--)
        {
            if (!RegisteredWindows[i].TryGetTarget(out var win))
            {
                RegisteredWindows.RemoveAt(i);
                continue;
            }

            win.Classes.Set("noMotion", !enabled);
        }
    }

    private static void PruneDeadWindows()
    {
        for (int i = RegisteredWindows.Count - 1; i >= 0; i--)
        {
            if (!RegisteredWindows[i].TryGetTarget(out _))
                RegisteredWindows.RemoveAt(i);
        }
    }
}
