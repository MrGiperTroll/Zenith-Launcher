using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace CustomMcLauncher.Views;

public partial class RamCardControl : UserControl
{
    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<RamCardControl, string>(nameof(Title), "Allocated RAM");

    public static readonly StyledProperty<string> SubtitleProperty =
        AvaloniaProperty.Register<RamCardControl, string>(nameof(Subtitle), "");

    public static readonly StyledProperty<int> ValueProperty =
        AvaloniaProperty.Register<RamCardControl, int>(nameof(Value), 4096, defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public static readonly StyledProperty<int> MinimumProperty =
        AvaloniaProperty.Register<RamCardControl, int>(nameof(Minimum), 1024);

    public static readonly StyledProperty<int> MaximumProperty =
        AvaloniaProperty.Register<RamCardControl, int>(nameof(Maximum), 16384);

    public static readonly StyledProperty<int> TickFrequencyProperty =
        AvaloniaProperty.Register<RamCardControl, int>(nameof(TickFrequency), 512);

    public static readonly StyledProperty<bool> IsGbModeProperty =
        AvaloniaProperty.Register<RamCardControl, bool>(nameof(IsGbMode), false);

    private bool _isUpdatingFromSlider;
    private bool _isEditing;

    public string Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Subtitle
    {
        get => GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }

    public int Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public int Minimum
    {
        get => GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public int Maximum
    {
        get => GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public int TickFrequency
    {
        get => GetValue(TickFrequencyProperty);
        set => SetValue(TickFrequencyProperty, value);
    }

    public bool IsGbMode
    {
        get => GetValue(IsGbModeProperty);
        set => SetValue(IsGbModeProperty, value);
    }

    public RamCardControl()
    {
        InitializeComponent();

        BadgeText.DoubleTapped += OnBadgeDoubleTapped;
        EditTextBox.KeyDown += OnEditTextBoxKeyDown;
        EditTextBox.LostFocus += OnEditTextBoxLostFocus;

        RamSlider.PropertyChanged += (s, e) =>
        {
            if (e.Property == Slider.ValueProperty && !_isUpdatingFromSlider)
            {
                var val = (int)Math.Round(RamSlider.Value);
                if (val != Value)
                {
                    _isUpdatingFromSlider = true;
                    Value = val;
                    _isUpdatingFromSlider = false;
                }
            }
        };
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == TitleProperty)
        {
            TitleBlock.Text = Title;
        }
        else if (change.Property == SubtitleProperty)
        {
            SubtitleBlock.Text = Subtitle;
            SubtitleBlock.IsVisible = !string.IsNullOrEmpty(Subtitle);
        }
        else if (change.Property == ValueProperty)
        {
            UpdateBadgeText();
            if (!_isUpdatingFromSlider)
            {
                _isUpdatingFromSlider = true;
                RamSlider.Value = Value;
                _isUpdatingFromSlider = false;
            }
        }
        else if (change.Property == MinimumProperty)
        {
            RamSlider.Minimum = Minimum;
        }
        else if (change.Property == MaximumProperty)
        {
            RamSlider.Maximum = Maximum;
        }
        else if (change.Property == TickFrequencyProperty)
        {
            RamSlider.TickFrequency = TickFrequency;
        }
        else if (change.Property == IsGbModeProperty)
        {
            UpdateBadgeText();
        }
    }

    private void UpdateBadgeText()
    {
        BadgeText.Text = IsGbMode ? $"{Value} GB" : $"{Value} MB";
    }

    private void OnBadgeDoubleTapped(object? sender, TappedEventArgs e)
    {
        BeginEdit();
    }

    private void BeginEdit()
    {
        _isEditing = true;
        BadgeText.IsVisible = false;
        EditTextBox.IsVisible = true;
        EditTextBox.Text = Value.ToString();
        EditTextBox.Focus();
        EditTextBox.SelectAll();
    }

    private void OnEditTextBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitEdit();
            EndEdit();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            EndEdit();
            e.Handled = true;
        }
    }

    private void OnEditTextBoxLostFocus(object? sender, RoutedEventArgs e)
    {
        if (_isEditing)
        {
            CommitEdit();
            EndEdit();
        }
    }

    private void EndEdit()
    {
        _isEditing = false;
        EditTextBox.IsVisible = false;
        BadgeText.IsVisible = true;
    }

    private void CommitEdit()
    {
        var raw = EditTextBox.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(raw)) return;

        var isGb = raw.EndsWith("GB", StringComparison.OrdinalIgnoreCase) ||
                   raw.EndsWith("G", StringComparison.OrdinalIgnoreCase);
        var isMb = raw.EndsWith("MB", StringComparison.OrdinalIgnoreCase) ||
                   raw.EndsWith("M", StringComparison.OrdinalIgnoreCase);

        var digitsOnly = new string(raw.Where(char.IsDigit).ToArray());
        if (!int.TryParse(digitsOnly, out var parsed)) return;

        if (IsGbMode)
        {
            if (isMb || parsed >= 512)
            {
                parsed = Math.Max(1, (int)Math.Round(parsed / 1024.0));
            }
        }
        else
        {
            if (isGb || (parsed <= 64 && parsed > 0 && !isMb && parsed < Minimum))
            {
                parsed *= 1024;
            }
        }

        parsed = Math.Clamp(parsed, Minimum, Maximum);
        Value = parsed;
    }
}
