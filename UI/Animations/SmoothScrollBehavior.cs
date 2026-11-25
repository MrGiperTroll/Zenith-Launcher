using System;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace ZenithLauncher.UI.Animations;

/// <summary>
/// VSync-driven smooth wheel scroller using TopLevel.RequestAnimationFrame for silky 60-240Hz scrolling.
/// Yields immediately if the user drags the scrollbar manually.
/// </summary>
public static class SmoothScrollBehavior
{
    private sealed class ScrollState
    {
        public double CurrentY;
        public double TargetY;
        public double LastWrittenY;
        public TimeSpan? LastTimestamp;
        public bool IsRunning;
    }

    private static readonly ConditionalWeakTable<ScrollViewer, ScrollState> States = new();

    public static void Attach(Control host, double wheelStep = 72.0, double damping = 16.0)
    {
        if (host == null) return;

        host.AddHandler(InputElement.PointerWheelChangedEvent, (sender, e) =>
        {
            if (e.Delta.Y == 0) return;

            var sv = host as ScrollViewer ?? FindInnerScrollViewer(e.Source as Visual, host);
            if (sv == null) return;

            double maxScroll = Math.Max(0, sv.Extent.Height - sv.Viewport.Height);
            if (maxScroll <= 0) return;

            e.Handled = true;

            if (!States.TryGetValue(sv, out var state))
            {
                state = new ScrollState
                {
                    CurrentY = sv.Offset.Y,
                    TargetY = sv.Offset.Y,
                    LastWrittenY = sv.Offset.Y
                };
                States.AddOrUpdate(sv, state);
            }
            else if (!state.IsRunning || Math.Abs(sv.Offset.Y - state.LastWrittenY) > 6.0)
            {
                state.CurrentY = sv.Offset.Y;
                state.TargetY = sv.Offset.Y;
                state.LastWrittenY = sv.Offset.Y;
            }

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
                if (!state.IsRunning) return;

                var tl = TopLevel.GetTopLevel(sv);
                if (tl == null || !sv.IsEffectivelyVisible)
                {
                    state.IsRunning = false;
                    return;
                }

                // Stop inertial tween immediately if scrollbar thumb / keyboard moved the offset externally.
                if (Math.Abs(sv.Offset.Y - state.LastWrittenY) > 6.0)
                {
                    state.IsRunning = false;
                    return;
                }

                double limit = Math.Max(0, sv.Extent.Height - sv.Viewport.Height);
                state.TargetY = Math.Clamp(state.TargetY, 0, limit);

                double dt;
                if (state.LastTimestamp.HasValue)
                    dt = Math.Clamp((ts - state.LastTimestamp.Value).TotalSeconds, 0.001, 0.05);
                else
                    dt = 1.0 / 120.0;
                state.LastTimestamp = ts;

                double alpha = 1.0 - Math.Exp(-damping * dt);
                double nextY = state.CurrentY + (state.TargetY - state.CurrentY) * alpha;

                if (Math.Abs(state.TargetY - nextY) < 0.3)
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

    private static ScrollViewer? FindInnerScrollViewer(Visual? source, Control rootHost)
    {
        for (var v = source; v != null; v = v.GetVisualParent())
        {
            if (v is ScrollViewer sv) return sv;
            if (ReferenceEquals(v, rootHost)) break;
        }
        return rootHost.FindDescendantOfType<ScrollViewer>();
    }
}
