using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Avalonia.Rendering.Composition.Animations;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace ZenithLauncher.UI.Animations;

public enum MotionCategory
{
    Auto,
    PageAndCatalog,
    InternalTabs,
    Modals,
    Sidebars,
    Popups
}

/// <summary>
/// High-fluidity UI motion engine combining render-thread Avalonia Composition API
/// spatial animations (Translation / Scale around layout CenterPoint) with synchronized
/// VSync opacity envelopes so CompositingRenderer property syncs never cancel fades.
/// Supports granular category toggles and independent smooth scrolling.
/// </summary>
public static class ZenithMotion
{
    public const string MotionCtrlBgTransitionsKey = "MotionCtrlBgTransitions";
    public const string MotionCtrlBgFgTransitionsKey = "MotionCtrlBgFgTransitions";
    public const string MotionCtrlBgBorderTransitionsKey = "MotionCtrlBgBorderTransitions";
    public const string MotionBorderBgTransitionsKey = "MotionBorderBgTransitions";
    public const string MotionBorderBgBorderTransitionsKey = "MotionBorderBgBorderTransitions";
    public const string MotionPresenterBgTransitionsKey = "MotionPresenterBgTransitions";

    public static readonly AttachedProperty<bool> ModalOpenProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("ModalOpen", typeof(ZenithMotion));

    public static readonly AttachedProperty<bool> SlidePanelOpenProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("SlidePanelOpen", typeof(ZenithMotion));

    public static readonly AttachedProperty<bool> ToastOpenProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("ToastOpen", typeof(ZenithMotion));

    public static readonly AttachedProperty<bool> RevealOnAttachProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("RevealOnAttach", typeof(ZenithMotion));

    public static readonly AttachedProperty<MotionCategory> CategoryProperty =
        AvaloniaProperty.RegisterAttached<Control, MotionCategory>("Category", typeof(ZenithMotion), MotionCategory.Auto);

    // Expressive Apple/Android curves: silky velocity across full 240-320ms travel, zero jitter.
    private static readonly SplineEasing FluidEnter = new(0.22, 1.0, 0.36, 1.0);
    private static readonly SplineEasing SpatialGlide = new(0.16, 1.0, 0.3, 1.0);
    private static readonly SplineEasing SoftTabCurve = new(0.24, 1.0, 0.42, 1.0);
    private static readonly SplineEasing SilkFade = new(0.25, 0.92, 0.38, 1.0);
    private static readonly SplineEasing SoftTabFade = new(0.28, 0.92, 0.42, 1.0);
    private static readonly SplineEasing ExitAccelerate = new(0.4, 0.0, 0.8, 0.25);
    private static readonly SpringEasing SheetSpring = new(1.0, 125.0, 18.5, 0.0);

    private static readonly Vector3D ZeroTranslation = default;
    private static readonly Vector3D UnitScale = new(1.0, 1.0, 1.0);

    private static readonly Transitions EmptyTransitions = new();
    private static readonly Transitions EnabledCtrlBgTransitions = CreateCtrlBgTransitions();
    private static readonly Transitions EnabledCtrlBgFgTransitions = CreateCtrlBgFgTransitions();
    private static readonly Transitions EnabledCtrlBgBorderTransitions = CreateCtrlBgBorderTransitions();
    private static readonly Transitions EnabledBorderBgTransitions = CreateBorderBgTransitions();
    private static readonly Transitions EnabledBorderBgBorderTransitions = CreateBorderBgBorderTransitions();
    private static readonly Transitions EnabledPresenterBgTransitions = CreatePresenterBgTransitions();

    private sealed class MotionTrack
    {
        public uint Generation;
        public uint OpacityGeneration;
        public MotionCategory Category = MotionCategory.Auto;
        public bool ControlsVisibility;
        public bool HasInitializedOpen;
        public bool IsOpen;
        public bool IsAnimating;
        public bool IsOpacityAnimating;
        public long StartTicks;
        public long OpacityStartTicks;
        public double DurationMs;
        public double OpacityDurationMs;
        public Vector3D FromTranslation;
        public Vector3D ToTranslation;
        public Vector3D FromScale = UnitScale;
        public Vector3D ToScale = UnitScale;
        public double FromOpacity = 1.0;
        public double ToOpacity = 1.0;
        public Func<double, double> SpatialCurve = Linear;
        public Func<double, double> OpacityCurve = Linear;
        public Action? CleanupPending;
        public Control? SecondaryTarget;
    }

    private static readonly ConditionalWeakTable<Control, MotionTrack> Tracks = new();
    private static readonly List<WeakReference<Control>> ActiveControls = new();

    private static bool _isEnabled = true;
    private static bool _pageTransitionsEnabled = true;
    private static bool _tabTransitionsEnabled = true;
    private static bool _modalsEnabled = true;
    private static bool _sidebarsEnabled = true;
    private static bool _popupsEnabled = true;
    private static bool _smoothScrollEnabled = true;

    public static bool IsEnabled => _isEnabled;
    public static bool IsSmoothScrollEnabled => _smoothScrollEnabled;
    public static event Action<bool>? EnabledChanged;
    public static event Action<bool>? SmoothScrollEnabledChanged;
    public static event Action? MotionConfigChanged;

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

    public static MotionCategory GetCategory(Control control) => control.GetValue(CategoryProperty);
    public static void SetCategory(Control control, MotionCategory value) =>
        control.SetValue(CategoryProperty, value);

    public static bool IsCategoryEnabled(MotionCategory category) => category switch
    {
        MotionCategory.PageAndCatalog => _isEnabled && _pageTransitionsEnabled,
        MotionCategory.InternalTabs => _isEnabled && _tabTransitionsEnabled,
        MotionCategory.Modals => _isEnabled && _modalsEnabled,
        MotionCategory.Sidebars => _isEnabled && _sidebarsEnabled,
        MotionCategory.Popups => _isEnabled && _popupsEnabled,
        _ => _isEnabled
    };

    private static MotionCategory ResolveCategory(Control control, MotionCategory defaultCategory)
    {
        var explicitCat = control.GetValue(CategoryProperty);
        return explicitCat == MotionCategory.Auto ? defaultCategory : explicitCat;
    }

    /// <summary>
    /// Registers top-level window lifecycle and ensures hover transition resources reflect the toggle state.
    /// </summary>
    public static void RegisterWindow(Window window)
    {
        if (window == null) return;
        ApplyHoverTransitionResources(_isEnabled);
    }

    /// <summary>
    /// Updates O(1) application resources so Styles toggle hover BrushTransitions without tree-wide selectors.
    /// </summary>
    public static void ApplyHoverTransitionResources(bool enabled, Application? app = null)
    {
        app ??= Application.Current;
        if (app == null) return;

        app.Resources[MotionCtrlBgTransitionsKey] = enabled ? EnabledCtrlBgTransitions : EmptyTransitions;
        app.Resources[MotionCtrlBgFgTransitionsKey] = enabled ? EnabledCtrlBgFgTransitions : EmptyTransitions;
        app.Resources[MotionCtrlBgBorderTransitionsKey] = enabled ? EnabledCtrlBgBorderTransitions : EmptyTransitions;
        app.Resources[MotionBorderBgTransitionsKey] = enabled ? EnabledBorderBgTransitions : EmptyTransitions;
        app.Resources[MotionBorderBgBorderTransitionsKey] = enabled ? EnabledBorderBgBorderTransitions : EmptyTransitions;
        app.Resources[MotionPresenterBgTransitionsKey] = enabled ? EnabledPresenterBgTransitions : EmptyTransitions;
    }

    public static void SetEnabled(bool enabled)
    {
        Configure(
            enabled,
            _pageTransitionsEnabled,
            _tabTransitionsEnabled,
            _modalsEnabled,
            _sidebarsEnabled,
            _popupsEnabled,
            _smoothScrollEnabled);
    }

    /// <summary>
    /// Applies master UI animation toggle, per-category toggles, and independent smooth scroll setting immediately.
    /// </summary>
    public static void Configure(
        bool masterEnabled,
        bool pageTransitions,
        bool tabTransitions,
        bool modals,
        bool sidebars,
        bool popups,
        bool smoothScroll)
    {
        void Apply()
        {
            bool masterChanged = _isEnabled != masterEnabled;
            bool scrollChanged = _smoothScrollEnabled != smoothScroll;
            bool anyMotionChanged = masterChanged
                || _pageTransitionsEnabled != pageTransitions
                || _tabTransitionsEnabled != tabTransitions
                || _modalsEnabled != modals
                || _sidebarsEnabled != sidebars
                || _popupsEnabled != popups;

            _isEnabled = masterEnabled;
            _pageTransitionsEnabled = pageTransitions;
            _tabTransitionsEnabled = tabTransitions;
            _modalsEnabled = modals;
            _sidebarsEnabled = sidebars;
            _popupsEnabled = popups;
            _smoothScrollEnabled = smoothScroll;

            ApplyHoverTransitionResources(masterEnabled);
            CancelDisabledAnimations();

            if (masterChanged)
                EnabledChanged?.Invoke(masterEnabled);

            if (anyMotionChanged)
                MotionConfigChanged?.Invoke();

            if (scrollChanged)
                SmoothScrollEnabledChanged?.Invoke(smoothScroll);
        }

        if (Dispatcher.UIThread.CheckAccess())
            Apply();
        else
            Dispatcher.UIThread.Post(Apply);
    }

    /// <summary>
    /// Synchronizes initial visibility and initialized state for a panel controlled by AnimateSlidePanel
    /// without running an entrance/exit transition.
    /// </summary>
    public static void SyncVisibilityImmediate(Control? control, bool isVisible)
    {
        if (control == null) return;
        var track = GetOrCreateTrack(control);
        track.Category = MotionCategory.Sidebars;
        track.ControlsVisibility = true;
        track.HasInitializedOpen = true;
        track.IsOpen = isVisible;
        track.Generation++;
        track.OpacityGeneration++;
        CancelPending(track);
        control.IsVisible = isVisible;
        control.IsHitTestVisible = isVisible;
        SnapToRest(control, track);
    }

    /// <summary>
    /// Cancels any active animation on a control (for example, when its host page hides) and restores neutral visual state.
    /// </summary>
    public static void CancelControl(Control? control)
    {
        if (control == null) return;
        if (!Tracks.TryGetValue(control, out var track)) return;

        track.Generation++;
        track.OpacityGeneration++;
        CancelPending(track);
        SnapToRest(control, track);
    }

    /// <summary>
    /// Animates a newly opened/navigated view into place using render-thread Scale/Translation
    /// around true centered bounds paired with synchronized VSync Opacity dissolve.
    /// </summary>
    public static void AnimateEntrance(
        Control? control,
        double offsetX = 0,
        double offsetY = 0,
        double startScale = 0.96,
        double startOpacity = 0.0,
        int durationMs = 280,
        MotionCategory category = MotionCategory.PageAndCatalog)
    {
        if (control == null || control is Window) return;
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => AnimateEntrance(control, offsetX, offsetY, startScale, startOpacity, durationMs, category));
            return;
        }

        var track = GetOrCreateTrack(control);
        var resolvedCategory = ResolveCategory(control, category);
        track.Category = resolvedCategory;
        CancelPending(track);
        uint gen = ++track.Generation;

        if (!IsCategoryEnabled(resolvedCategory) || durationMs <= 0)
        {
            SnapToRest(control, track);
            return;
        }

        bool needsScale = Math.Abs(startScale - 1.0) > 0.001;
        RunWhenReady(control, track, gen, needsBounds: needsScale, (ctrl, comp) =>
        {
            if (!IsCategoryEnabled(resolvedCategory) || track.Generation != gen)
            {
                if (!IsCategoryEnabled(resolvedCategory)) SnapToRest(ctrl, track);
                return;
            }

            Vector3D fromTrans;
            Vector3D fromScale;
            double fromOp;
            if (track.IsAnimating)
            {
                SampleCurrent(track, out fromTrans, out fromScale, out fromOp);
            }
            else
            {
                fromTrans = new Vector3D(offsetX, offsetY, 0);
                fromScale = new Vector3D(startScale, startScale, 1.0);
                fromOp = Math.Clamp(startOpacity, 0.0, 1.0);
            }

            if (ctrl.Bounds.Width > 0 && ctrl.Bounds.Height > 0)
                comp.CenterPoint = new Vector3D(ctrl.Bounds.Width * 0.5, ctrl.Bounds.Height * 0.5, 0);

            bool hasSlide = Math.Abs(offsetX) >= 16.0 || Math.Abs(offsetY) >= 16.0;
            bool isTabCategory = resolvedCategory == MotionCategory.InternalTabs;
            Easing spatialEasing = isTabCategory
                ? SoftTabCurve
                : (hasSlide ? SpatialGlide : FluidEnter);
            Easing opacityEasing = isTabCategory ? SoftTabFade : SilkFade;
            double opRatio = isTabCategory ? 0.90 : 0.88;
            int opacityMs = Math.Clamp((int)Math.Round(durationMs * opRatio), 160, 275);

            StartCompositeMotion(
                ctrl,
                comp,
                track,
                gen,
                durationMs,
                opacityMs,
                fromTrans,
                ZeroTranslation,
                fromScale,
                UnitScale,
                fromOp,
                1.0,
                spatialEasing,
                opacityEasing,
                animateScale: needsScale || Math.Abs(fromScale.X - 1.0) > 0.001,
                animateTranslation: hasSlide || Math.Abs(offsetX) > 0.1 || Math.Abs(offsetY) > 0.1 || Math.Abs(fromTrans.X) > 0.1 || Math.Abs(fromTrans.Y) > 0.1);
        });
    }

    /// <summary>
    /// Expressive directional horizontal swipe for catalog category pills (Modpacks | Mods | Resource Packs | Data Packs | Shader Packs | Worlds).
    /// Positive directionSign slides in from the right; negative slides in from the left.
    /// </summary>
    public static void AnimateCatalogSwipe(Control? control, int directionSign, int durationMs = 310)
    {
        double offsetX = directionSign >= 0 ? 96.0 : -96.0;
        AnimateEntrance(
            control,
            offsetX: offsetX,
            offsetY: 0,
            startScale: 0.986,
            startOpacity: 0.0,
            durationMs: durationMs,
            category: MotionCategory.PageAndCatalog);
    }

    /// <summary>
    /// Gentle pure-opacity reveal (zero spatial translation) when real catalog cards replace skeleton placeholders.
    /// </summary>
    public static void AnimateOpacityReveal(Control? control, int durationMs = 200)
    {
        AnimateEntrance(
            control,
            offsetX: 0,
            offsetY: 0,
            startScale: 1.0,
            startOpacity: 0.0,
            durationMs: durationMs,
            category: MotionCategory.PageAndCatalog);
    }

    /// <summary>
    /// Soft, unhurried directional slide for internal section tabs (Settings, Instance Edit, Project Details, Import Modal).
    /// </summary>
    public static void AnimateDirectionalTab(
        Control? control,
        int directionSign,
        double distance = 44.0,
        int durationMs = 280)
    {
        double offsetX = directionSign == 0 ? 0.0 : (directionSign > 0 ? distance : -distance);
        AnimateEntrance(
            control,
            offsetX: offsetX,
            offsetY: 0,
            startScale: directionSign == 0 ? 0.976 : 0.986,
            startOpacity: 0.0,
            durationMs: durationMs,
            category: MotionCategory.InternalTabs);
    }

    /// <summary>
    /// Soft centered depth-scale + cross-dissolve fallback for internal tabs.
    /// </summary>
    public static void TransitionTab(
        Control? control,
        double offsetX = 0,
        double offsetY = 0,
        double startOpacity = 0.0,
        int durationMs = 275)
    {
        AnimateEntrance(
            control,
            offsetX,
            offsetY,
            startScale: 0.976,
            startOpacity: startOpacity,
            durationMs: durationMs,
            category: MotionCategory.InternalTabs);
    }

    /// <summary>
    /// Interruptible modal open/close animation coordinating backdrop dissolve and spring sheet card reveal.
    /// </summary>
    public static void HandleModalOpenChanged(Control? overlay, bool isOpen)
    {
        if (overlay == null) return;
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => HandleModalOpenChanged(overlay, isOpen));
            return;
        }

        var category = ResolveCategory(overlay, MotionCategory.Modals);
        var track = GetOrCreateTrack(overlay);
        track.Category = category;
        track.ControlsVisibility = true;
        CancelPending(track);

        var card = ResolveModalCard(overlay);
        track.SecondaryTarget = card;

        if (!track.HasInitializedOpen)
        {
            track.HasInitializedOpen = true;
            if (!isOpen)
            {
                track.Generation++;
                track.OpacityGeneration++;
                track.IsOpen = false;
                track.IsAnimating = false;
                track.IsOpacityAnimating = false;
                overlay.IsVisible = false;
                overlay.IsHitTestVisible = false;
                overlay.Opacity = 1.0;
                if (card != null) card.Opacity = 1.0;
                return;
            }
        }

        if (track.HasInitializedOpen && track.IsOpen == isOpen && isOpen == overlay.IsVisible && !track.IsAnimating)
        {
            return;
        }

        uint gen = ++track.Generation;
        track.IsOpen = isOpen;

        var cardTrack = card != null ? GetOrCreateTrack(card) : null;
        uint cardGen = 0;
        if (cardTrack != null)
        {
            cardTrack.Category = category;
            CancelPending(cardTrack);
            cardGen = ++cardTrack.Generation;
            cardTrack.IsOpen = isOpen;
        }

        if (!IsCategoryEnabled(category))
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
                if (!IsCategoryEnabled(category) || track.Generation != gen || !track.IsOpen)
                {
                    if (!IsCategoryEnabled(category))
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
                    if (card != null && cardTrack != null) SnapToRest(card, cardTrack);
                    return;
                }

                SampleCurrent(track, out _, out _, out double ovFromOp);
                if (!track.IsAnimating) ovFromOp = 0.0;
                int ovMs = ScaledDuration(240, 1.0 - ovFromOp, 0.25);

                StartCompositeMotion(
                    overlay,
                    ovComp,
                    track,
                    gen,
                    ovMs,
                    ovMs,
                    ZeroTranslation,
                    ZeroTranslation,
                    UnitScale,
                    UnitScale,
                    ovFromOp,
                    1.0,
                    FluidEnter,
                    SilkFade,
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
                        double cardFromOp;
                        if (cardTrack.IsAnimating)
                        {
                            SampleCurrent(cardTrack, out fromTrans, out fromScale, out cardFromOp);
                        }
                        else
                        {
                            fromTrans = new Vector3D(0, 24, 0);
                            fromScale = new Vector3D(0.905, 0.905, 1.0);
                            cardFromOp = 0.0;
                        }

                        int cardMs = ScaledDuration(315, 1.0 - cardFromOp, 0.25);
                        int cardOpMs = Math.Clamp((int)Math.Round(cardMs * 0.72), 140, 230);
                        StartCompositeMotion(
                            card,
                            cardComp,
                            cardTrack,
                            cardGen,
                            cardMs,
                            cardOpMs,
                            fromTrans,
                            ZeroTranslation,
                            fromScale,
                            UnitScale,
                            cardFromOp,
                            1.0,
                            SheetSpring,
                            SilkFade,
                            animateScale: true,
                            animateTranslation: true);
                    }
                }
            });
        }
        else
        {
            overlay.IsHitTestVisible = false;

            var ovComp = overlay.IsAttachedToVisualTree() ? ElementComposition.GetElementVisual(overlay) : null;
            if (!overlay.IsVisible || ovComp == null)
            {
                overlay.IsVisible = false;
                SnapToRest(overlay, track);
                if (card != null && cardTrack != null) SnapToRest(card, cardTrack);
                return;
            }

            SampleCurrent(track, out _, out _, out double ovFromOp);
            if (!track.IsAnimating) ovFromOp = 1.0;
            int exitMs = ScaledDuration(185, ovFromOp, 0.3);

            StartCompositeMotion(
                overlay,
                ovComp,
                track,
                gen,
                exitMs,
                exitMs,
                ZeroTranslation,
                ZeroTranslation,
                UnitScale,
                UnitScale,
                ovFromOp,
                0.0,
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
                    double cardFromOp;
                    if (cardTrack.IsAnimating)
                    {
                        SampleCurrent(cardTrack, out fromTrans, out fromScale, out cardFromOp);
                    }
                    else
                    {
                        fromTrans = ZeroTranslation;
                        fromScale = UnitScale;
                        cardFromOp = 1.0;
                    }

                    StartCompositeMotion(
                        card,
                        cardComp,
                        cardTrack,
                        cardGen,
                        exitMs,
                        exitMs,
                        fromTrans,
                        new Vector3D(0, 14, 0),
                        fromScale,
                        new Vector3D(0.935, 0.935, 1.0),
                        cardFromOp,
                        0.0,
                        ExitAccelerate,
                        ExitAccelerate,
                        animateScale: true,
                        animateTranslation: true);
                }
            }

            _ = FinishExitAfterAsync(overlay, track, card, cardTrack, gen, ovComp.Compositor, exitMs);
        }
    }

    /// <summary>
    /// Interruptible slide/dissolve transition for inspector drawers and sliding panels (e.g. InstanceDetailsSidebar).
    /// </summary>
    public static void AnimateSlidePanel(
        Control? panel,
        bool isOpen,
        double slideX = 84,
        double slideY = 0,
        int enterMs = 285,
        int exitMs = 190)
    {
        if (panel == null) return;
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => AnimateSlidePanel(panel, isOpen, slideX, slideY, enterMs, exitMs));
            return;
        }

        var category = ResolveCategory(panel, MotionCategory.Sidebars);
        var track = GetOrCreateTrack(panel);
        track.Category = category;
        track.ControlsVisibility = true;
        CancelPending(track);

        if (!track.HasInitializedOpen)
        {
            track.HasInitializedOpen = true;
            if (!isOpen)
            {
                track.Generation++;
                track.OpacityGeneration++;
                track.IsOpen = false;
                track.IsAnimating = false;
                track.IsOpacityAnimating = false;
                panel.IsVisible = false;
                panel.IsHitTestVisible = false;
                panel.Opacity = 1.0;
                return;
            }
        }

        uint gen = ++track.Generation;
        track.IsOpen = isOpen;

        if (!IsCategoryEnabled(category))
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
                if (!IsCategoryEnabled(category) || track.Generation != gen || !track.IsOpen)
                {
                    if (!IsCategoryEnabled(category))
                    {
                        panel.IsVisible = track.IsOpen;
                        panel.IsHitTestVisible = track.IsOpen;
                        SnapToRest(panel, track);
                    }
                    return;
                }

                Vector3D fromTrans;
                double fromOp;
                if (track.IsAnimating)
                {
                    SampleCurrent(track, out fromTrans, out _, out fromOp);
                }
                else
                {
                    fromTrans = new Vector3D(slideX, slideY, 0);
                    fromOp = 0.0;
                }

                int ms = ScaledDuration(enterMs, 1.0 - fromOp, 0.25);
                int opMs = Math.Clamp((int)Math.Round(ms * 0.85), 150, 250);
                StartCompositeMotion(
                    ctrl,
                    comp,
                    track,
                    gen,
                    ms,
                    opMs,
                    fromTrans,
                    ZeroTranslation,
                    UnitScale,
                    UnitScale,
                    fromOp,
                    1.0,
                    SpatialGlide,
                    SilkFade,
                    animateScale: false,
                    animateTranslation: true);
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
            double fromOp;
            if (track.IsAnimating)
            {
                SampleCurrent(track, out fromTrans, out _, out fromOp);
            }
            else
            {
                fromTrans = ZeroTranslation;
                fromOp = 1.0;
            }

            int ms = ScaledDuration(exitMs, fromOp, 0.3);
            StartCompositeMotion(
                panel,
                comp,
                track,
                gen,
                ms,
                ms,
                fromTrans,
                new Vector3D(slideX * 0.85, slideY * 0.85, 0),
                UnitScale,
                UnitScale,
                fromOp,
                0.0,
                ExitAccelerate,
                ExitAccelerate,
                animateScale: false,
                animateTranslation: true);

            _ = FinishExitAfterAsync(panel, track, null, null, gen, comp.Compositor, ms);
        }
    }

    /// <summary>
    /// Expressive centered spring reveal &amp; smooth dismiss for floating toast notifications.
    /// </summary>
    public static void HandleToastOpenChanged(Control? toast, bool isVisible)
    {
        if (toast == null) return;
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => HandleToastOpenChanged(toast, isVisible));
            return;
        }

        var category = ResolveCategory(toast, MotionCategory.Popups);
        var track = GetOrCreateTrack(toast);
        track.Category = category;
        track.ControlsVisibility = true;
        CancelPending(track);

        if (!track.HasInitializedOpen)
        {
            track.HasInitializedOpen = true;
            if (!isVisible)
            {
                track.Generation++;
                track.OpacityGeneration++;
                track.IsOpen = false;
                track.IsAnimating = false;
                track.IsOpacityAnimating = false;
                toast.IsVisible = false;
                toast.Opacity = 1.0;
                return;
            }
        }

        uint gen = ++track.Generation;
        track.IsOpen = isVisible;

        if (!IsCategoryEnabled(category))
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
                if (!IsCategoryEnabled(category) || track.Generation != gen || !track.IsOpen)
                {
                    if (!IsCategoryEnabled(category))
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
                double fromOp;
                if (track.IsAnimating)
                {
                    SampleCurrent(track, out fromTrans, out fromScale, out fromOp);
                }
                else
                {
                    fromTrans = new Vector3D(0, 24, 0);
                    fromScale = new Vector3D(0.91, 0.91, 1.0);
                    fromOp = 0.0;
                }

                int ms = ScaledDuration(275, 1.0 - fromOp, 0.25);
                int opMs = Math.Clamp((int)Math.Round(ms * 0.78), 140, 215);
                StartCompositeMotion(
                    ctrl,
                    comp,
                    track,
                    gen,
                    ms,
                    opMs,
                    fromTrans,
                    ZeroTranslation,
                    fromScale,
                    UnitScale,
                    fromOp,
                    1.0,
                    SheetSpring,
                    SilkFade,
                    animateScale: true,
                    animateTranslation: true);
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
            double fromOp;
            if (track.IsAnimating)
            {
                SampleCurrent(track, out fromTrans, out fromScale, out fromOp);
            }
            else
            {
                fromTrans = ZeroTranslation;
                fromScale = UnitScale;
                fromOp = 1.0;
            }

            int ms = ScaledDuration(180, fromOp, 0.3);
            StartCompositeMotion(
                toast,
                comp,
                track,
                gen,
                ms,
                ms,
                fromTrans,
                new Vector3D(0, 14, 0),
                fromScale,
                new Vector3D(0.95, 0.95, 1.0),
                fromOp,
                0.0,
                ExitAccelerate,
                ExitAccelerate,
                animateScale: true,
                animateTranslation: true);

            _ = FinishExitAfterAsync(toast, track, null, null, gen, comp.Compositor, ms);
        }
    }

    private static void OnRevealOnAttachChanged(Control control, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.GetNewValue<bool>())
        {
            control.AttachedToVisualTree -= OnRevealControlAttached;
            control.AttachedToVisualTree += OnRevealControlAttached;

            if (control.IsAttachedToVisualTree() && control.IsEffectivelyVisible && IsCategoryEnabled(MotionCategory.Popups))
            {
                AnimateEntrance(control, offsetX: 0, offsetY: -8, startScale: 0.95, startOpacity: 0.0, durationMs: 205, category: MotionCategory.Popups);
            }
        }
        else
        {
            control.AttachedToVisualTree -= OnRevealControlAttached;
        }
    }

    private static void OnRevealControlAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is Control control && IsCategoryEnabled(MotionCategory.Popups))
        {
            AnimateEntrance(control, offsetX: 0, offsetY: -8, startScale: 0.95, startOpacity: 0.0, durationMs: 205, category: MotionCategory.Popups);
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

        bool IsReadyNow() =>
            target.IsAttachedToVisualTree() &&
            target.IsVisible &&
            target.IsEffectivelyVisible &&
            IsSizeSatisfied();

        if (IsReadyNow())
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
        EventHandler<AvaloniaPropertyChangedEventArgs>? onPropChanged = null;
        Action? detachAction = null;

        void DetachHandlers()
        {
            target.AttachedToVisualTree -= onAttached;
            target.DetachedFromVisualTree -= onDetached;
            target.LayoutUpdated -= onLayoutUpdated;
            target.PropertyChanged -= onPropChanged;
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

            if (!IsReadyNow())
                return;

            var comp = ElementComposition.GetElementVisual(target);
            if (comp == null) return;

            DetachHandlers();
            onReady(target, comp);
        }

        onAttached = (_, _) => TryComplete();
        onLayoutUpdated = (_, _) => TryComplete();
        onPropChanged = (_, ev) =>
        {
            if (ev.Property == Visual.IsVisibleProperty || ev.Property == Visual.BoundsProperty)
                TryComplete();
        };
        onDetached = (_, _) => DetachHandlers();

        track.CleanupPending = detachAction;
        target.AttachedToVisualTree += onAttached;
        target.LayoutUpdated += onLayoutUpdated;
        target.PropertyChanged += onPropChanged;
        target.DetachedFromVisualTree += onDetached;

        Dispatcher.UIThread.Post(TryComplete, DispatcherPriority.Loaded);
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
            await Task.Delay(durationMs + 20);
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

    private static void StartCompositeMotion(
        Control control,
        CompositionVisual comp,
        MotionTrack track,
        uint gen,
        int spatialMs,
        int opacityMs,
        Vector3D fromTranslation,
        Vector3D toTranslation,
        Vector3D fromScale,
        Vector3D toScale,
        double fromOpacity,
        double toOpacity,
        Easing spatialEasing,
        Easing opacityEasing,
        bool animateScale,
        bool animateTranslation)
    {
        comp.StopAnimation("Translation");
        comp.StopAnimation("Scale");
        comp.StopAnimation("Opacity");

        var compositor = comp.Compositor;
        int clampedSpatialMs = Math.Max(16, spatialMs);
        int clampedOpacityMs = Math.Max(16, opacityMs);

        track.StartTicks = Stopwatch.GetTimestamp();
        track.OpacityStartTicks = track.StartTicks;
        track.DurationMs = clampedSpatialMs;
        track.OpacityDurationMs = clampedOpacityMs;
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
            transAnim.Duration = TimeSpan.FromMilliseconds(clampedSpatialMs);
            transAnim.StopBehavior = AnimationStopBehavior.SetToFinalValue;
            transAnim.InsertKeyFrame(0f, fromTranslation);
            transAnim.InsertKeyFrame(1f, toTranslation, spatialEasing);
            comp.StartAnimation("Translation", transAnim);
        }
        else
        {
            comp.Translation = toTranslation;
        }

        if (animateScale)
        {
            var scaleAnim = compositor.CreateVector3DKeyFrameAnimation();
            scaleAnim.Target = "Scale";
            scaleAnim.Duration = TimeSpan.FromMilliseconds(clampedSpatialMs);
            scaleAnim.StopBehavior = AnimationStopBehavior.SetToFinalValue;
            scaleAnim.InsertKeyFrame(0f, fromScale);
            scaleAnim.InsertKeyFrame(1f, toScale, spatialEasing);
            comp.StartAnimation("Scale", scaleAnim);
        }
        else
        {
            comp.Scale = toScale;
        }

        if (Math.Abs(fromOpacity - toOpacity) > 0.001)
        {
            StartOpacityFade(control, track, gen, clampedOpacityMs, fromOpacity, toOpacity, opacityEasing);
        }
        else
        {
            track.OpacityGeneration++;
            track.IsOpacityAnimating = false;
            control.Opacity = toOpacity;
        }

        _ = MarkAnimationSettledAsync(track, gen, Math.Max(clampedSpatialMs, clampedOpacityMs));
    }

    private static void StartOpacityFade(
        Control control,
        MotionTrack track,
        uint trackGen,
        int durationMs,
        double fromOpacity,
        double toOpacity,
        Easing easing)
    {
        uint opGen = ++track.OpacityGeneration;
        track.IsOpacityAnimating = true;

        control.Opacity = Math.Clamp(fromOpacity, 0.0, 1.0);

        var topLevel = TopLevel.GetTopLevel(control);
        if (topLevel == null)
        {
            track.IsOpacityAnimating = false;
            control.Opacity = Math.Clamp(toOpacity, 0.0, 1.0);
            return;
        }

        long startTicks = track.OpacityStartTicks;
        MotionCategory capturedCategory = track.Category;

        void Step(TimeSpan _)
        {
            if (!IsCategoryEnabled(capturedCategory) || track.Generation != trackGen || track.OpacityGeneration != opGen)
            {
                track.IsOpacityAnimating = false;
                return;
            }

            var tl = TopLevel.GetTopLevel(control);
            if (tl == null)
            {
                track.IsOpacityAnimating = false;
                control.Opacity = Math.Clamp(toOpacity, 0.0, 1.0);
                return;
            }

            double elapsedMs = Stopwatch.GetElapsedTime(startTicks).TotalMilliseconds;
            double rawT = Math.Clamp(elapsedMs / durationMs, 0.0, 1.0);

            if (rawT >= 1.0)
            {
                track.IsOpacityAnimating = false;
                control.Opacity = Math.Clamp(toOpacity, 0.0, 1.0);
                return;
            }

            double eased = easing.Ease(rawT);
            double nextOpacity = fromOpacity + (toOpacity - fromOpacity) * eased;
            control.Opacity = Math.Clamp(nextOpacity, 0.0, 1.0);
            tl.RequestAnimationFrame(Step);
        }

        topLevel.RequestAnimationFrame(Step);
    }

    private static async Task MarkAnimationSettledAsync(MotionTrack track, uint gen, int durationMs)
    {
        try
        {
            await Task.Delay(durationMs + 24);
        }
        catch
        {
            return;
        }

        void MarkDone()
        {
            if (track.Generation == gen)
                track.IsAnimating = false;
        }

        if (Dispatcher.UIThread.CheckAccess())
            MarkDone();
        else
            Dispatcher.UIThread.Post(MarkDone);
    }

    private static void SnapToRest(Control control, MotionTrack track)
    {
        track.IsAnimating = false;
        track.IsOpacityAnimating = false;
        track.OpacityGeneration++;
        CancelPending(track);

        control.Opacity = 1.0;

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

    private static void SampleCurrent(MotionTrack track, out Vector3D translation, out Vector3D scale, out double opacity)
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

        double opElapsedMs = Stopwatch.GetElapsedTime(track.OpacityStartTicks).TotalMilliseconds;
        double opRawT = track.OpacityDurationMs > 0 ? Math.Clamp(opElapsedMs / track.OpacityDurationMs, 0.0, 1.0) : 1.0;
        double ot = track.OpacityCurve(opRawT);

        translation = LerpVector(track.FromTranslation, track.ToTranslation, st);
        scale = LerpVector(track.FromScale, track.ToScale, st);
        opacity = Math.Clamp(track.FromOpacity + (track.ToOpacity - track.FromOpacity) * ot, 0.0, 1.0);
    }

    private static Vector3D LerpVector(Vector3D a, Vector3D b, double t) =>
        new(
            a.X + (b.X - a.X) * t,
            a.Y + (b.Y - a.Y) * t,
            a.Z + (b.Z - a.Z) * t);

    private static int ScaledDuration(int baseMs, double remainingFraction, double minFraction) =>
        Math.Max(60, (int)Math.Round(baseMs * Math.Clamp(remainingFraction, minFraction, 1.0)));

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

    private static void CancelDisabledAnimations()
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

            if (IsCategoryEnabled(track.Category))
                continue;

            track.Generation++;
            track.OpacityGeneration++;
            CancelPending(track);

            if (track.ControlsVisibility && track.HasInitializedOpen)
            {
                control.IsVisible = track.IsOpen;
                if (track.SecondaryTarget != null || !track.IsOpen)
                    control.IsHitTestVisible = track.IsOpen;
            }

            SnapToRest(control, track);
        }
    }

    private static Transitions CreateCtrlBgTransitions() => new()
    {
        new BrushTransition
        {
            Property = TemplatedControl.BackgroundProperty,
            Duration = TimeSpan.FromMilliseconds(150),
            Easing = new SplineEasing(0.16, 1.0, 0.3, 1.0)
        }
    };

    private static Transitions CreateCtrlBgFgTransitions() => new()
    {
        new BrushTransition
        {
            Property = TemplatedControl.BackgroundProperty,
            Duration = TimeSpan.FromMilliseconds(150),
            Easing = new SplineEasing(0.16, 1.0, 0.3, 1.0)
        },
        new BrushTransition
        {
            Property = TemplatedControl.ForegroundProperty,
            Duration = TimeSpan.FromMilliseconds(150),
            Easing = new SplineEasing(0.16, 1.0, 0.3, 1.0)
        }
    };

    private static Transitions CreateCtrlBgBorderTransitions() => new()
    {
        new BrushTransition
        {
            Property = TemplatedControl.BackgroundProperty,
            Duration = TimeSpan.FromMilliseconds(150),
            Easing = new SplineEasing(0.16, 1.0, 0.3, 1.0)
        },
        new BrushTransition
        {
            Property = TemplatedControl.BorderBrushProperty,
            Duration = TimeSpan.FromMilliseconds(150),
            Easing = new SplineEasing(0.16, 1.0, 0.3, 1.0)
        }
    };

    private static Transitions CreateBorderBgTransitions() => new()
    {
        new BrushTransition
        {
            Property = Border.BackgroundProperty,
            Duration = TimeSpan.FromMilliseconds(150),
            Easing = new SplineEasing(0.16, 1.0, 0.3, 1.0)
        }
    };

    private static Transitions CreateBorderBgBorderTransitions() => new()
    {
        new BrushTransition
        {
            Property = Border.BackgroundProperty,
            Duration = TimeSpan.FromMilliseconds(150),
            Easing = new SplineEasing(0.16, 1.0, 0.3, 1.0)
        },
        new BrushTransition
        {
            Property = Border.BorderBrushProperty,
            Duration = TimeSpan.FromMilliseconds(150),
            Easing = new SplineEasing(0.16, 1.0, 0.3, 1.0)
        }
    };

    private static Transitions CreatePresenterBgTransitions() => new()
    {
        new BrushTransition
        {
            Property = ContentPresenter.BackgroundProperty,
            Duration = TimeSpan.FromMilliseconds(150),
            Easing = new SplineEasing(0.16, 1.0, 0.3, 1.0)
        }
    };
}
