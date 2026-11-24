using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CmlLib.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ZenithLauncher.Models;
using ZenithLauncher.Services;
using ZenithLauncher.UI.Theming;
using ZenithLauncher.ViewModels.Common;
using ZenithLauncher.ViewModels.Pages;
using ZenithLauncher.Views;

namespace ZenithLauncher.ViewModels;


public partial class MainWindowViewModel
{
    [ObservableProperty] private double _launcherScale = 1.0;



    [ObservableProperty] private string _selectedParticle = "None";



    public string[] ParticleOptions { get; } = new[] { "None", "Snowflakes", "Leaves", "Sakura", "Custom" };



    partial void OnSelectedParticleChanged(string value) => SaveLauncherConfig();



    [ObservableProperty] private int _particleCount = 50;

    [ObservableProperty] private double _particleScale = 1.0;

    [ObservableProperty] private double _particleSpeed = 1.0;

    [ObservableProperty] private string _particleColorHex = "";

    [ObservableProperty] private double _particleAngleDeg = 90.0;

    [ObservableProperty] private string _particleTexturePath = "";



    partial void OnParticleCountChanged(int value) => SaveLauncherConfig();

    partial void OnParticleScaleChanged(double value) => SaveLauncherConfig();

    partial void OnParticleSpeedChanged(double value) => SaveLauncherConfig();

    partial void OnParticleColorHexChanged(string value) => SaveLauncherConfig();

    partial void OnParticleAngleDegChanged(double value) => SaveLauncherConfig();

    partial void OnParticleTexturePathChanged(string value) => SaveLauncherConfig();



    partial void OnLauncherScaleChanged(double value) => SaveLauncherConfig();



    // Theme Customization

    public ObservableCollection<ThemePreset> AvailableThemes { get; } = new(ZenithTheme.Presets);



    [ObservableProperty]

    private string _currentAccentHex = ZenithTheme.DefaultAccent;



    [ObservableProperty]

    private string _customThemeHex = ZenithTheme.DefaultAccent;

    public Avalonia.Media.Color BgColorValue {

        get => Avalonia.Media.Color.TryParse(BgHex, out var c) ? c : Avalonia.Media.Colors.Black;

        set { BgHex = value.ToString(); OnPropertyChanged(nameof(BgColorValue)); }

    }

    public Avalonia.Media.Color SurfaceColorValue {

        get => Avalonia.Media.Color.TryParse(SurfaceHex, out var c) ? c : Avalonia.Media.Colors.Black;

        set { SurfaceHex = value.ToString(); OnPropertyChanged(nameof(SurfaceColorValue)); }

    }

    public Avalonia.Media.Color PanelColorValue {

        get => Avalonia.Media.Color.TryParse(PanelHex, out var c) ? c : Avalonia.Media.Colors.Black;

        set { PanelHex = value.ToString(); OnPropertyChanged(nameof(PanelColorValue)); }

    }

    public Avalonia.Media.Color CustomThemeColorValue {

        get => Avalonia.Media.Color.TryParse(CustomThemeHex, out var c) ? c : Avalonia.Media.Colors.Black;

        set { CustomThemeHex = value.ToString(); OnPropertyChanged(nameof(CustomThemeColorValue)); }

    }

    [ObservableProperty] private string _bgHex = "#080A0F";

    [ObservableProperty] private string _surfaceHex = "#0D111A";

    [ObservableProperty] private string _panelHex = "#121722";

    [ObservableProperty] private string _fieldHex = "#121724";

    public Avalonia.Media.Color FieldColorValue {

        get => Avalonia.Media.Color.TryParse(FieldHex, out var c) ? c : Avalonia.Media.Colors.Black;

        set { FieldHex = value.ToString(); OnPropertyChanged(nameof(FieldColorValue)); }

    }

    partial void OnFieldHexChanged(string value) { OnPropertyChanged(nameof(FieldColorValue)); if (!_isLoadingConfig) ZenithTheme.Apply(GetLauncherConfigSnapshot()); }



    [ObservableProperty] private string _hoverHex = "#1B2338";

    public Avalonia.Media.Color HoverColorValue {

        get => Avalonia.Media.Color.TryParse(HoverHex, out var c) ? c : Avalonia.Media.Colors.Black;

        set { HoverHex = value.ToString(); OnPropertyChanged(nameof(HoverColorValue)); }

    }

    partial void OnHoverHexChanged(string value) { OnPropertyChanged(nameof(HoverColorValue)); if (!_isLoadingConfig) ZenithTheme.Apply(GetLauncherConfigSnapshot()); }



    [ObservableProperty]

    private string _sidebarBgHex = "#06080D";

    partial void OnSidebarBgHexChanged(string value) { if (!_isLoadingConfig) ZenithTheme.Apply(GetLauncherConfigSnapshot()); }



    [ObservableProperty]

    private string _cardHex = "#0D111A";

    partial void OnCardHexChanged(string value) { if (!_isLoadingConfig) ZenithTheme.Apply(GetLauncherConfigSnapshot()); }



    [ObservableProperty]

    private string _textPrimaryHex = "#FFFFFF";

    partial void OnTextPrimaryHexChanged(string value) { if (!_isLoadingConfig) ZenithTheme.Apply(GetLauncherConfigSnapshot()); }



    [ObservableProperty]

    private string _textSecondaryHex = "#C5CBD6";

    partial void OnTextSecondaryHexChanged(string value) { if (!_isLoadingConfig) ZenithTheme.Apply(GetLauncherConfigSnapshot()); }



    [ObservableProperty]

    private string _borderDarkHex = "#222834";

    partial void OnBorderDarkHexChanged(string value) { if (!_isLoadingConfig) ZenithTheme.Apply(GetLauncherConfigSnapshot()); }



    [ObservableProperty]

    private IBrush _previewAccentBrush = new SolidColorBrush(Color.Parse(ZenithTheme.DefaultAccent));



    [ObservableProperty]

    private IBrush _previewAccentHoverBrush = new SolidColorBrush(Color.Parse("#60A5FA"));



    [ObservableProperty]

    private IBrush _previewAccentDimBrush = new SolidColorBrush(Color.FromArgb(45, 59, 130, 246));



    [ObservableProperty]

    private IBrush _previewAccentBorderBrush = new SolidColorBrush(Color.FromArgb(90, 59, 130, 246));



    private bool _isHarmonizingPalette = false;



    partial void OnCustomThemeHexChanged(string value)

    {

        UpdatePreviewBrushes(value);

        OnPropertyChanged(nameof(CustomThemeColorValue));



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

            ZenithTheme.Apply(GetLauncherConfigSnapshot());

        }

        else if (!_isLoadingConfig && !string.IsNullOrWhiteSpace(value))

        {

            ZenithTheme.Apply(GetLauncherConfigSnapshot());

        }

    }

    partial void OnBgHexChanged(string value) { OnPropertyChanged(nameof(BgColorValue)); if (!_isLoadingConfig) ZenithTheme.Apply(GetLauncherConfigSnapshot()); }

    partial void OnSurfaceHexChanged(string value) { OnPropertyChanged(nameof(SurfaceColorValue)); if (!_isLoadingConfig) ZenithTheme.Apply(GetLauncherConfigSnapshot()); }

    partial void OnPanelHexChanged(string value) { OnPropertyChanged(nameof(PanelColorValue)); if (!_isLoadingConfig) ZenithTheme.Apply(GetLauncherConfigSnapshot()); }



    private void UpdatePreviewBrushes(string? hex)

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

    private string _simpleAccentHex = ZenithTheme.DefaultAccent;



    // Saved Simple baseline

    private string _savedSimpleAccentHex = ZenithTheme.DefaultAccent;



    // Saved Advanced baseline

    private string _savedBgHex = "#080A0F";

    private string _savedSidebarBgHex = "#06080D";

    private string _savedSurfaceHex = "#0D111A";

    private string _savedCardHex = "#0D111A";

    private string _savedPanelHex = "#121722";

    private string _savedFieldHex = "#121724";

    private string _savedHoverHex = "#1B2338";

    private string _savedBorderDarkHex = "#222834";

    private string _savedTextPrimaryHex = "#FFFFFF";

    private string _savedTextSecondaryHex = "#C5CBD6";

    private string _savedCustomThemeHex = ZenithTheme.DefaultAccent;



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

            ZenithTheme.Apply(GetLauncherConfigSnapshot());

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

            ZenithTheme.Apply(GetLauncherConfigSnapshot());

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

            ZenithTheme.Apply(GetLauncherConfigSnapshot());

            SnapshotCurrentSavedTheme();

        }



        SaveLauncherConfig();

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

        IsCloneDialogOpen = false;

        PendingRestartThemeHex = "";

    }



    [RelayCommand]

    public void ConfirmRestartAndApplyTheme()

    {

        IsRestartConfirmOpen = false;

        IsCloneDialogOpen = false;

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

        // Highlight/flash Apply button for ~1.2s to guide the user

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



    [ObservableProperty]

    private string _themeSubTab = "Simple";



    public bool IsThemeSubTabSimple => ThemeSubTab == "Simple";

    public bool IsThemeSubTabAdvanced => ThemeSubTab == "Advanced";



    [RelayCommand]

    public void SwitchThemeSubTab(string tab)

    {

        if (ThemeSubTab == tab) return;



        ThemeSubTab = tab;

        OnPropertyChanged(nameof(IsThemeSubTabSimple));

        OnPropertyChanged(nameof(IsThemeSubTabAdvanced));



        SaveLauncherConfig();

    }



    [RelayCommand]

    private void OpenThemeEditor()

    {

        SnapshotCurrentSavedTheme();

        SwitchSettingsTab("Theme");

    }

    

    [RelayCommand]

    private void CloseThemeEditor()

    {

        if (HasUnsavedThemeChanges())

        {

            _pendingThemeDiscardNavAction = () => SwitchSettingsTab("Appearance");

            IsThemeDiscardConfirmOpen = true;

            return;

        }

        SwitchSettingsTab("Appearance");

    }




}
