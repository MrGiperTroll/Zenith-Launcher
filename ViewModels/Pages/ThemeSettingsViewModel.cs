using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CustomMcLauncher.Models;
using CustomMcLauncher.UI.Theming;
using CustomMcLauncher.ViewModels.Common;

namespace CustomMcLauncher.ViewModels.Pages;

public partial class ThemeSettingsViewModel : ViewModelBase
{
    private bool _isLoadingConfig;
    private bool _isHarmonizingPalette;

    public Func<LauncherConfigData>? GetConfigSnapshot { get; set; }
    public Action? SaveConfigRequested { get; set; }
    public Action? CloseRequested { get; set; }

    public ObservableCollection<ThemePreset> AvailableThemes { get; } = new(ZenithTheme.Presets);

    [ObservableProperty]
    private string _currentAccentHex = ZenithTheme.DefaultAccent;

    [ObservableProperty]
    private string _customThemeHex = ZenithTheme.DefaultAccent;

    [ObservableProperty]
    private string _simpleAccentHex = ZenithTheme.DefaultAccent;

    [ObservableProperty] private string _bgHex = "#080A0F";
    [ObservableProperty] private string _surfaceHex = "#0D111A";
    [ObservableProperty] private string _panelHex = "#121722";
    [ObservableProperty] private string _fieldHex = "#121724";
    [ObservableProperty] private string _hoverHex = "#1B2338";
    [ObservableProperty] private string _sidebarBgHex = "#06080D";
    [ObservableProperty] private string _cardHex = "#0D111A";
    [ObservableProperty] private string _textPrimaryHex = "#FFFFFF";
    [ObservableProperty] private string _textSecondaryHex = "#C5CBD6";
    [ObservableProperty] private string _borderDarkHex = "#121622";

    [ObservableProperty]
    private IBrush _previewAccentBrush = new SolidColorBrush(Color.Parse(ZenithTheme.DefaultAccent));

    [ObservableProperty]
    private IBrush _previewAccentHoverBrush = new SolidColorBrush(Color.Parse("#60A5FA"));

    [ObservableProperty]
    private IBrush _previewAccentDimBrush = new SolidColorBrush(Color.FromArgb(45, 59, 130, 246));

    [ObservableProperty]
    private IBrush _previewAccentBorderBrush = new SolidColorBrush(Color.FromArgb(90, 59, 130, 246));

    [ObservableProperty]
    private bool _isRestartConfirmOpen;

    [ObservableProperty]
    private string _pendingRestartThemeHex = "";

    [ObservableProperty]
    private bool _isThemeDiscardConfirmOpen;

    [ObservableProperty]
    private bool _isApplyButtonHighlighted;

    private Action? _pendingThemeDiscardNavAction;

    [ObservableProperty]
    private string _themeSubTab = "Simple";

    public bool IsThemeSubTabSimple => ThemeSubTab == "Simple";
    public bool IsThemeSubTabAdvanced => ThemeSubTab == "Advanced";

    [ObservableProperty]
    private string _selectedParticle = "None";

    public string[] ParticleOptions { get; } = { "None", "Snowflakes", "Leaves", "Sakura" };

    // Saved Baselines
    private string _savedSimpleAccentHex = ZenithTheme.DefaultAccent;
    private string _savedBgHex = "#080A0F";
    private string _savedSidebarBgHex = "#06080D";
    private string _savedSurfaceHex = "#0D111A";
    private string _savedCardHex = "#0D111A";
    private string _savedPanelHex = "#121722";
    private string _savedFieldHex = "#121724";
    private string _savedHoverHex = "#1B2338";
    private string _savedBorderDarkHex = "#121622";
    private string _savedTextPrimaryHex = "#FFFFFF";
    private string _savedTextSecondaryHex = "#C5CBD6";
    private string _savedCustomThemeHex = ZenithTheme.DefaultAccent;

    public ThemeSettingsViewModel()
    {
        UpdatePreviewBrushes(ZenithTheme.DefaultAccent);
    }

    partial void OnFieldHexChanged(string value)
    {
        if (!_isLoadingConfig) ApplyCurrentTheme();
    }

    partial void OnHoverHexChanged(string value)
    {
        if (!_isLoadingConfig) ApplyCurrentTheme();
    }

    partial void OnSidebarBgHexChanged(string value)
    {
        if (!_isLoadingConfig) ApplyCurrentTheme();
    }

    partial void OnCardHexChanged(string value)
    {
        if (!_isLoadingConfig) ApplyCurrentTheme();
    }

    partial void OnTextPrimaryHexChanged(string value)
    {
        if (!_isLoadingConfig) ApplyCurrentTheme();
    }

    partial void OnTextSecondaryHexChanged(string value)
    {
        if (!_isLoadingConfig) ApplyCurrentTheme();
    }

    partial void OnBorderDarkHexChanged(string value)
    {
        if (!_isLoadingConfig) ApplyCurrentTheme();
    }

    partial void OnBgHexChanged(string value)
    {
        if (!_isLoadingConfig) ApplyCurrentTheme();
    }

    partial void OnSurfaceHexChanged(string value)
    {
        if (!_isLoadingConfig) ApplyCurrentTheme();
    }

    partial void OnPanelHexChanged(string value)
    {
        if (!_isLoadingConfig) ApplyCurrentTheme();
    }

    partial void OnCustomThemeHexChanged(string value)
    {
        UpdatePreviewBrushes(value);

        if (!_isLoadingConfig && !_isHarmonizingPalette && !string.IsNullOrWhiteSpace(value) && IsThemeSubTabSimple)
        {
            _isHarmonizingPalette = true;
            try
            {
                var harmonized = ZenithTheme.GenerateHarmonizedPalette(value);
                BgHex = harmonized.BgColor!;
                SidebarBgHex = harmonized.SidebarBgColor!;
                SurfaceHex = harmonized.SurfaceColor!;
                CardHex = harmonized.CardColor!;
                PanelHex = harmonized.PanelColor!;
                FieldHex = harmonized.FieldColor!;
                HoverHex = harmonized.HoverColor!;
                BorderDarkHex = harmonized.BorderDarkColor!;
                TextPrimaryHex = harmonized.TextPrimaryColor!;
                TextSecondaryHex = harmonized.TextSecondaryColor!;
            }
            finally
            {
                _isHarmonizingPalette = false;
            }
            ApplyCurrentTheme();
        }
        else if (!_isLoadingConfig && !string.IsNullOrWhiteSpace(value))
        {
            ApplyCurrentTheme();
        }
    }

    partial void OnSelectedParticleChanged(string value)
    {
        if (!_isLoadingConfig)
            SaveConfigRequested?.Invoke();
    }

    [RelayCommand]
    public void SwitchThemeSubTab(string tab)
    {
        if (ThemeSubTab == tab) return;

        ThemeSubTab = tab;
        OnPropertyChanged(nameof(IsThemeSubTabSimple));
        OnPropertyChanged(nameof(IsThemeSubTabAdvanced));

        if (IsThemeSubTabSimple)
        {
            ApplySimpleSavedTheme();
        }
    }

    public void UpdatePreviewBrushes(string? hex)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(hex)) return;
            hex = hex.Trim();
            if (!hex.StartsWith("#")) hex = "#" + hex;
            if (Color.TryParse(hex, out var c))
            {
                PreviewAccentBrush = new SolidColorBrush(c);
                var hoverColor = Color.FromRgb(
                    (byte)Math.Clamp(c.R + (255 - c.R) * 0.16f, 0, 255),
                    (byte)Math.Clamp(c.G + (255 - c.G) * 0.16f, 0, 255),
                    (byte)Math.Clamp(c.B + (255 - c.B) * 0.16f, 0, 255));
                PreviewAccentHoverBrush = new SolidColorBrush(hoverColor);
                PreviewAccentDimBrush = new SolidColorBrush(Color.FromArgb(45, c.R, c.G, c.B));
                PreviewAccentBorderBrush = new SolidColorBrush(Color.FromArgb(90, c.R, c.G, c.B));
            }
        }
        catch { }
    }

    public void SnapshotCurrentSavedTheme()
    {
        _savedSimpleAccentHex = SimpleAccentHex;

        _savedBgHex = BgHex;
        _savedSidebarBgHex = SidebarBgHex;
        _savedSurfaceHex = SurfaceHex;
        _savedCardHex = CardHex;
        _savedPanelHex = PanelHex;
        _savedFieldHex = FieldHex;
        _savedHoverHex = HoverHex;
        _savedBorderDarkHex = BorderDarkHex;
        _savedTextPrimaryHex = TextPrimaryHex;
        _savedTextSecondaryHex = TextSecondaryHex;
        _savedCustomThemeHex = CustomThemeHex;
    }

    public bool HasUnsavedThemeChanges()
    {
        if (IsThemeSubTabSimple)
        {
            return !string.Equals(_savedSimpleAccentHex, SimpleAccentHex, StringComparison.OrdinalIgnoreCase);
        }

        return !string.Equals(_savedBgHex, BgHex, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(_savedSidebarBgHex, SidebarBgHex, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(_savedSurfaceHex, SurfaceHex, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(_savedCardHex, CardHex, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(_savedPanelHex, PanelHex, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(_savedFieldHex, FieldHex, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(_savedHoverHex, HoverHex, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(_savedBorderDarkHex, BorderDarkHex, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(_savedTextPrimaryHex, TextPrimaryHex, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(_savedTextSecondaryHex, TextSecondaryHex, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(_savedCustomThemeHex, CustomThemeHex, StringComparison.OrdinalIgnoreCase);
    }

    public void ApplyCurrentTheme()
    {
        var cfg = GetConfigSnapshot?.Invoke() ?? new LauncherConfigData();
        ApplyToConfig(cfg);
        ZenithTheme.Apply(cfg);
    }

    public void ApplySimpleTheme(string hex)
    {
        _isLoadingConfig = true;
        try
        {
            SimpleAccentHex = hex;
            CurrentAccentHex = hex;
            CustomThemeHex = hex;
            var harmonized = ZenithTheme.GenerateHarmonizedPalette(hex);
            BgHex = harmonized.BgColor!;
            SidebarBgHex = harmonized.SidebarBgColor!;
            SurfaceHex = harmonized.SurfaceColor!;
            CardHex = harmonized.CardColor!;
            PanelHex = harmonized.PanelColor!;
            FieldHex = harmonized.FieldColor!;
            HoverHex = harmonized.HoverColor!;
            BorderDarkHex = harmonized.BorderDarkColor!;
            TextPrimaryHex = harmonized.TextPrimaryColor!;
            TextSecondaryHex = harmonized.TextSecondaryColor!;
            UpdatePreviewBrushes(hex);

            ApplyCurrentTheme();
        }
        finally
        {
            _isLoadingConfig = false;
        }
    }

    public void ApplyAdvancedSavedTheme()
    {
        _isLoadingConfig = true;
        try
        {
            BgHex = _savedBgHex;
            SidebarBgHex = _savedSidebarBgHex;
            SurfaceHex = _savedSurfaceHex;
            CardHex = _savedCardHex;
            PanelHex = _savedPanelHex;
            FieldHex = _savedFieldHex;
            HoverHex = _savedHoverHex;
            BorderDarkHex = _savedBorderDarkHex;
            TextPrimaryHex = _savedTextPrimaryHex;
            TextSecondaryHex = _savedTextSecondaryHex;
            CustomThemeHex = _savedCustomThemeHex;
            CurrentAccentHex = _savedCustomThemeHex;
            UpdatePreviewBrushes(_savedCustomThemeHex);

            ApplyCurrentTheme();
        }
        finally
        {
            _isLoadingConfig = false;
        }
    }

    public void ApplySimpleSavedTheme()
    {
        ApplySimpleTheme(_savedSimpleAccentHex);
    }

    public void RevertThemeToSaved()
    {
        if (IsThemeSubTabSimple)
        {
            ApplySimpleSavedTheme();
        }
        else
        {
            ApplyAdvancedSavedTheme();
        }
    }

    [RelayCommand]
    public void ChooseTheme(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return;
        hex = hex.Trim();
        if (!hex.StartsWith("#")) hex = "#" + hex;
        ApplySimpleTheme(hex);
    }

    [RelayCommand]
    public void SelectTheme(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return;
        hex = hex.Trim();
        if (!hex.StartsWith("#")) hex = "#" + hex;

        if (IsThemeSubTabSimple)
        {
            ApplySimpleTheme(hex);
            _savedSimpleAccentHex = hex;
        }
        else
        {
            CurrentAccentHex = hex;
            CustomThemeHex = hex;
            ApplyCurrentTheme();
            SnapshotCurrentSavedTheme();
        }

        SaveConfigRequested?.Invoke();
    }

    [RelayCommand]
    public void RequestApplyTheme(string? hex = null)
    {
        var targetHex = !string.IsNullOrWhiteSpace(hex) ? hex : (IsThemeSubTabSimple ? SimpleAccentHex : CustomThemeHex);
        if (string.IsNullOrWhiteSpace(targetHex)) return;
        targetHex = targetHex.Trim();
        if (!targetHex.StartsWith("#")) targetHex = "#" + targetHex;

        PendingRestartThemeHex = targetHex;
        IsRestartConfirmOpen = true;
    }

    [RelayCommand]
    public void CancelRestartConfirm()
    {
        IsRestartConfirmOpen = false;
        PendingRestartThemeHex = "";
    }

    [RelayCommand]
    public void ConfirmRestartAndApplyTheme()
    {
        IsRestartConfirmOpen = false;
        var hex = !string.IsNullOrWhiteSpace(PendingRestartThemeHex) ? PendingRestartThemeHex : (IsThemeSubTabSimple ? SimpleAccentHex : CustomThemeHex);
        SelectTheme(hex);

        try
        {
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath))
                exePath = Process.GetCurrentProcess().MainModule?.FileName;

            if (!string.IsNullOrEmpty(exePath) && System.IO.File.Exists(exePath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = exePath,
                    UseShellExecute = true,
                    WorkingDirectory = System.IO.Path.GetDirectoryName(exePath)
                });
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to restart launcher: {ex.Message}");
        }

        Environment.Exit(0);
    }

    [RelayCommand]
    public void ConfirmDiscardThemeChanges()
    {
        IsThemeDiscardConfirmOpen = false;
        RevertThemeToSaved();
        var action = _pendingThemeDiscardNavAction;
        _pendingThemeDiscardNavAction = null;
        action?.Invoke();
    }

    [RelayCommand]
    public void CancelDiscardThemeChanges()
    {
        IsThemeDiscardConfirmOpen = false;
        _pendingThemeDiscardNavAction = null;
        IsApplyButtonHighlighted = true;
        _ = Task.Delay(1200).ContinueWith(_ =>
        {
            Dispatcher.UIThread.Post(() => IsApplyButtonHighlighted = false);
        });
    }

    [RelayCommand]
    public void ApplyCustomTheme()
    {
        RequestApplyTheme(CustomThemeHex);
    }

    [RelayCommand]
    public void CloseThemeEditor()
    {
        if (HasUnsavedThemeChanges())
        {
            _pendingThemeDiscardNavAction = () => CloseRequested?.Invoke();
            IsThemeDiscardConfirmOpen = true;
            return;
        }
        CloseRequested?.Invoke();
    }

    public void LoadFromConfig(LauncherConfigData cfg)
    {
        _isLoadingConfig = true;
        try
        {
            if (!string.IsNullOrWhiteSpace(cfg.ThemeSubTab))
            {
                ThemeSubTab = cfg.ThemeSubTab;
                OnPropertyChanged(nameof(IsThemeSubTabSimple));
                OnPropertyChanged(nameof(IsThemeSubTabAdvanced));
            }

            if (!string.IsNullOrWhiteSpace(cfg.BgColor)) BgHex = cfg.BgColor;
            if (!string.IsNullOrWhiteSpace(cfg.SurfaceColor)) SurfaceHex = cfg.SurfaceColor;
            if (!string.IsNullOrWhiteSpace(cfg.PanelColor)) PanelHex = cfg.PanelColor;
            if (!string.IsNullOrWhiteSpace(cfg.SidebarBgColor)) SidebarBgHex = cfg.SidebarBgColor;
            if (!string.IsNullOrWhiteSpace(cfg.CardColor)) CardHex = cfg.CardColor;
            if (!string.IsNullOrWhiteSpace(cfg.FieldColor)) FieldHex = cfg.FieldColor;
            if (!string.IsNullOrWhiteSpace(cfg.HoverColor)) HoverHex = cfg.HoverColor;
            if (!string.IsNullOrWhiteSpace(cfg.TextPrimaryColor)) TextPrimaryHex = cfg.TextPrimaryColor;
            if (!string.IsNullOrWhiteSpace(cfg.TextSecondaryColor)) TextSecondaryHex = cfg.TextSecondaryColor;
            if (!string.IsNullOrWhiteSpace(cfg.BorderDarkColor)) BorderDarkHex = cfg.BorderDarkColor;

            if (!string.IsNullOrWhiteSpace(cfg.SimpleAccentColor))
            {
                SimpleAccentHex = cfg.SimpleAccentColor;
                _savedSimpleAccentHex = cfg.SimpleAccentColor;
            }
            else if (!string.IsNullOrWhiteSpace(cfg.AccentColor))
            {
                SimpleAccentHex = cfg.AccentColor;
                _savedSimpleAccentHex = cfg.AccentColor;
            }

            if (!string.IsNullOrWhiteSpace(cfg.AccentColor))
            {
                CurrentAccentHex = cfg.AccentColor;
                CustomThemeHex = cfg.AccentColor;
                UpdatePreviewBrushes(cfg.AccentColor);
            }

            if (!string.IsNullOrEmpty(cfg.SelectedParticle))
                SelectedParticle = cfg.SelectedParticle;

            if (IsThemeSubTabSimple)
            {
                ApplySimpleTheme(SimpleAccentHex);
            }
            else
            {
                ApplyCurrentTheme();
            }

            SnapshotCurrentSavedTheme();
        }
        finally
        {
            _isLoadingConfig = false;
        }
    }

    public void ApplyToConfig(LauncherConfigData cfg)
    {
        cfg.AccentColor = CurrentAccentHex;
        cfg.SimpleAccentColor = _savedSimpleAccentHex;
        cfg.BgColor = BgHex;
        cfg.SurfaceColor = SurfaceHex;
        cfg.PanelColor = PanelHex;
        cfg.SidebarBgColor = SidebarBgHex;
        cfg.CardColor = CardHex;
        cfg.FieldColor = FieldHex;
        cfg.HoverColor = HoverHex;
        cfg.TextPrimaryColor = TextPrimaryHex;
        cfg.TextSecondaryColor = TextSecondaryHex;
        cfg.BorderDarkColor = BorderDarkHex;
        cfg.ThemeSubTab = ThemeSubTab;
        cfg.SelectedParticle = SelectedParticle;
    }
}
