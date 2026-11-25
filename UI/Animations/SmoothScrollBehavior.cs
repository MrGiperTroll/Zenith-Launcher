using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace ZenithLauncher.UI.Animations;

/// <summary>
/// VSync-driven smooth wheel scroller using TopLevel.RequestAnimationFrame for 60-240Hz scrolling.
/// Handles VirtualizingStackPanel container height recalculations cleanly, yields to manual thumb drags,
/// and switches instantaneously to native wheel scrolling when UI animations are disabled.
/// </summary>
public static class SmoothScrollBehavior
{
    private sealed class ScrollState
    {
        public double CurrentY;
        public double TargetY;
        public double LastWrittenY;
        public double LastLimitY;
        public TimeSpan? LastTimestamp;
        public bool IsRunning;
    }

    private static readonly ConditionalWeakTable<ScrollViewer, ScrollState> States = new();
    private static readonly List<WeakReference<ScrollViewer>> ActiveViewers = new();

    static SmoothScrollBehavior()
    {
        ZenithMotion.EnabledChanged += OnMotionEnabledChanged;
    }

    private static void OnMotionEnabledChanged(bool enabled)
    {
        if (enabled) return;

        for (int i = ActiveViewers.Count - 1; i >= 0; i--)
        {
            if (!ActiveViewers[i].TryGetTarget(out var sv))
            {
                ActiveViewers.RemoveAt(i);
                continue;
            }

            if (States.TryGetValue(sv, out var state))
            {
                state.IsRunning = false;
                state.CurrentY = sv.Offset.Y;
                state.TargetY = sv.Offset.Y;
                state.LastWrittenY = sv.Offset.Y;
            }
        }
    }

    public static void Attach(Control host, double wheelStep = 72.0, double damping = 16.0)
    {
        if (host == null) return;

        host.AddHandler(InputElement.PointerWheelChangedEvent, (sender, e) =>
        {
            if (!ZenithMotion.IsEnabled)
                return;

            if (e.Delta.Y == 0) return;

            var sv = FindScrollableViewer(e.Source as Visual, host);
            if (sv == null) return;

            double maxScroll = GetMaxVerticalScroll(sv);
            if (maxScroll <= 0) return;

            e.Handled = true;

            if (!States.TryGetValue(sv, out var state))
            {
                state = new ScrollState
                {
                    CurrentY = sv.Offset.Y,
                    TargetY = sv.Offset.Y,
                    LastWrittenY = sv.Offset.Y,
                    LastLimitY = maxScroll
                };
                States.AddOrUpdate(sv, state);
                ActiveViewers.Add(new WeakReference<ScrollViewer>(sv));
            }
            else if (!state.IsRunning || IsExternalJump(sv.Offset.Y, state.LastWrittenY, state.LastLimitY, maxScroll))
            {
                state.CurrentY = sv.Offset.Y;
                state.TargetY = sv.Offset.Y;
                state.LastWrittenY = sv.Offset.Y;
            }

            state.LastLimitY = maxScroll;
            state.TargetY = Math.Clamp(state.TargetY - e.Delta.Y * wheelStep, 0, maxScroll);

            if (state.IsRunning) return;

            var topLevel = TopLevel.GetTopLevel(sv);
            if (topLevel == null)
            {
                sv.Offset = new Vector(sv.Offset.X, state.TargetY);
                return;
            }

            state.IsRunning = true;
            state.LastTimestamp = null;

            void Step(TimeSpan ts)
            {
                if (!state.IsRunning || !ZenithMotion.IsEnabled)
                {
                    state.IsRunning = false;
                    return;
                }

                var tl = TopLevel.GetTopLevel(sv);
                if (tl == null || !sv.IsEffectivelyVisible)
                {
                    state.IsRunning = false;
                    return;
                }

                double limit = GetMaxVerticalScroll(sv);
                if (limit <= 0)
                {
                    state.IsRunning = false;
                    return;
                }

                // Stop inertial tween immediately if scrollbar thumb / keyboard / code reset moved the offset externally,
                // while tolerating dynamic clamping from VirtualizingStackPanel container estimation.
                if (IsExternalJump(sv.Offset.Y, state.LastWrittenY, state.LastLimitY, limit))
                {
                    state.IsRunning = false;
                    return;
                }

                state.LastLimitY = limit;
                state.TargetY = Math.Clamp(state.TargetY, 0, limit);

                double dt;
                if (state.LastTimestamp.HasValue)
                    dt = Math.Clamp((ts - state.LastTimestamp.Value).TotalSeconds, 0.001, 0.05);
                else
                    dt = 1.0 / 120.0;
                state.LastTimestamp = ts;

                double alpha = 1.0 - Math.Exp(-damping * dt);
                double nextY = state.CurrentY + (state.TargetY - state.CurrentY) * alpha;

                if (Math.Abs(state.TargetY - nextY) < 0.35)
                {
                    state.CurrentY = state.TargetY;
                    state.LastWrittenY = state.TargetY;
                    sv.Offset = new Vector(sv.Offset.X, state.TargetY);
                    state.IsRunning = false;
                    return;
                }

                state.CurrentY = nextY;
                state.LastWrittenY = nextY;
                sv.Offset = new Vector(sv.Offset.X, nextY);
                tl.RequestAnimationFrame(Step);
            }

            topLevel.RequestAnimationFrame(Step);
        }, RoutingStrategies.Tunnel);

        host.DetachedFromVisualTree += (_, _) =>
        {
            if (host is ScrollViewer sv && States.TryGetValue(sv, out var state))
            {
                state.IsRunning = false;
                States.Remove(sv);
            }
            else
            {
                var inner = host.FindDescendantOfType<ScrollViewer>();
                if (inner != null && States.TryGetValue(inner, out var innerState))
                {
                    innerState.IsRunning = false;
                    States.Remove(inner);
                }
            }
        };
    }

    private static double GetMaxVerticalScroll(ScrollViewer sv) =>
        Math.Max(0, Math.Max(sv.ScrollBarMaximum.Y, sv.Extent.Height - sv.Viewport.Height));

    private static bool IsExternalJump(double actualY, double lastWrittenY, double prevLimitY, double currentLimitY)
    {
        if (Math.Abs(actualY - lastWrittenY) <= 8.0)
            return false;

        // VirtualizingStackPanel container estimation can dynamically shrink Extent near the bottom,
        // clamping ScrollViewer.Offset down to currentLimitY; that is not an external user jump.
        if (Math.Abs(actualY - currentLimitY) <= 8.0 && lastWrittenY > currentLimitY - 8.0)
            return false;

        if (prevLimitY > 0 && Math.Abs(currentLimitY - prevLimitY) > 8.0 && Math.Abs(actualY - Math.Min(lastWrittenY, currentLimitY)) <= 10.0)
            return false;

        return true;
    }

    private static ScrollViewer? FindScrollableViewer(Visual? source, Control rootHost)
    {
        for (var v = source; v != null; v = v.GetVisualParent())
        {
            if (v is ScrollViewer sv && GetMaxVerticalScroll(sv) > 0)
                return sv;
            if (ReferenceEquals(v, rootHost))
                break;
        }

        if (rootHost is ScrollViewer rootSv && GetMaxVerticalScroll(rootSv) > 0)
            return rootSv;

        return rootHost.FindDescendantOfType<ScrollViewer>();
    }
}
