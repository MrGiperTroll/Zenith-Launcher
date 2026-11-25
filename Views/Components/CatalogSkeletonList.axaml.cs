using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ZenithLauncher.UI.Animations;

namespace ZenithLauncher.Views.Components;

/// <summary>
/// Lightweight placeholder skeleton cards matching the layout of catalog results.
/// Uses a single VSync pulse callback when visible and enabled, with zero Skia allocations
/// or DispatcherTimers, and halts immediately when hidden or animations are turned off.
/// </summary>
public partial class CatalogSkeletonList : UserControl
{
    public static readonly StyledProperty<int> CardCountProperty =
        AvaloniaProperty.Register<CatalogSkeletonList, int>(nameof(CardCount), 6);

    public int CardCount
    {
        get => GetValue(CardCountProperty);
        set => SetValue(CardCountProperty, value);
    }

    private long _pulseGen;
    private long _pulseStartTicks;
    private TopLevel? _topLevel;

    public CatalogSkeletonList()
    {
        InitializeComponent();
        RebuildSkeletonCards();
        AttachedToVisualTree += OnAttached;
        DetachedFromVisualTree += OnDetached;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == CardCountProperty)
        {
            RebuildSkeletonCards();
        }
        else if (change.Property == IsVisibleProperty)
        {
            SyncPulseState();
        }
    }

    private void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _topLevel = TopLevel.GetTopLevel(this);
        ZenithMotion.MotionConfigChanged += OnMotionConfigChanged;
        SyncPulseState();
    }

    private void OnDetached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        ZenithMotion.MotionConfigChanged -= OnMotionConfigChanged;
        StopPulse();
        _topLevel = null;
    }

    private void OnMotionConfigChanged() => SyncPulseState();

    private void SyncPulseState()
    {
        bool shouldPulse = IsVisible
            && _topLevel != null
            && ZenithMotion.IsEnabled
            && ZenithMotion.IsCategoryEnabled(MotionCategory.PageAndCatalog);

        if (!shouldPulse)
        {
            StopPulse();
            return;
        }

        StartPulse();
    }

    private void StartPulse()
    {
        if (_topLevel == null) return;
        long gen = ++_pulseGen;
        _pulseStartTicks = Stopwatch.GetTimestamp();

        void Step(TimeSpan _)
        {
            if (_pulseGen != gen || !IsVisible || _topLevel == null
                || !ZenithMotion.IsEnabled || !ZenithMotion.IsCategoryEnabled(MotionCategory.PageAndCatalog))
            {
                return;
            }

            double sec = Stopwatch.GetElapsedTime(_pulseStartTicks).TotalSeconds;
            double wave = 0.5 + 0.5 * Math.Sin(sec * Math.PI * 2.0 / 1.35 - Math.PI * 0.5);
            SkeletonStack.Opacity = 0.44 + 0.46 * wave;

            _topLevel.RequestAnimationFrame(Step);
        }

        SkeletonStack.Opacity = 0.44;
        _topLevel.RequestAnimationFrame(Step);
    }

    private void StopPulse()
    {
        _pulseGen++;
        SkeletonStack.Opacity = IsVisible ? 0.65 : 1.0;
    }

    private void RebuildSkeletonCards()
    {
        int count = Math.Clamp(CardCount, 1, 10);
        SkeletonStack.Children.Clear();
        for (int i = 0; i < count; i++)
        {
            SkeletonStack.Children.Add(BuildSkeletonCard(i));
        }
    }

    private static Border BuildSkeletonCard(int index)
    {
        // Slightly vary bar widths so the list looks organic and naturally spaced.
        double titleWidth = 155 + (index % 3) * 32;
        double line2Width = 230 + ((index + 1) % 3) * 45;

        var rootGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("56,*,Auto"),
            ColumnSpacing = 14
        };

        var iconPlaceholder = new Border
        {
            Width = 56,
            Height = 56,
            CornerRadius = new CornerRadius(10),
            VerticalAlignment = VerticalAlignment.Top,
            [!Border.BackgroundProperty] = new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("SurfaceBrush")
        };
        Grid.SetColumn(iconPlaceholder, 0);
        rootGrid.Children.Add(iconPlaceholder);

        var centerStack = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 7
        };

        var titleRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8
        };
        titleRow.Children.Add(MakeBlock(titleWidth, 14, 4, "ChipBrush"));
        titleRow.Children.Add(MakeBlock(68, 11, 4, "SurfaceBrush", VerticalAlignment.Center));
        centerStack.Children.Add(titleRow);

        centerStack.Children.Add(MakeStretchBlock(11, 3, "SurfaceBrush", 420));
        centerStack.Children.Add(MakeBlock(line2Width, 11, 3, "SurfaceBrush"));

        var chipsRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Margin = new Thickness(0, 1, 0, 0)
        };
        chipsRow.Children.Add(MakeBlock(52, 17, 6, "ChipBrush"));
        chipsRow.Children.Add(MakeBlock(66, 17, 6, "ChipBrush"));
        chipsRow.Children.Add(MakeBlock(46, 17, 6, "ChipBrush"));
        centerStack.Children.Add(chipsRow);

        Grid.SetColumn(centerStack, 1);
        rootGrid.Children.Add(centerStack);

        var rightRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        rightRow.Children.Add(MakeBlock(104, 30, 8, "ChipBrush", VerticalAlignment.Center));
        rightRow.Children.Add(MakeBlock(26, 26, 6, "SurfaceBrush", VerticalAlignment.Center));
        Grid.SetColumn(rightRow, 2);
        rootGrid.Children.Add(rightRow);

        return new Border
        {
            Margin = new Thickness(0, 0, 0, 8),
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(14, 12),
            Child = rootGrid,
            [!Border.BackgroundProperty] = new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("CardAltBrush"),
            [!Border.BorderBrushProperty] = new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("BorderCardSoftBrush")
        };
    }

    private static Border MakeBlock(double w, double h, double radius, string brushKey, VerticalAlignment va = VerticalAlignment.Top) => new()
    {
        Width = w,
        Height = h,
        CornerRadius = new CornerRadius(radius),
        HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = va,
        [!Border.BackgroundProperty] = new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension(brushKey)
    };

    private static Border MakeStretchBlock(double h, double radius, string brushKey, double maxWidth) => new()
    {
        Height = h,
        MaxWidth = maxWidth,
        CornerRadius = new CornerRadius(radius),
        HorizontalAlignment = HorizontalAlignment.Stretch,
        [!Border.BackgroundProperty] = new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension(brushKey)
    };
}
