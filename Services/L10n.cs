using System;
using System.Collections.Generic;
using System.Globalization;
using ZenithLauncher.Services.Localization.Locales;

namespace ZenithLauncher.Services;

/// <summary>
/// Lightweight UI localization engine.
///
/// XAML usage:   Text="{l10n:Tr set_memory}"
/// Code usage:   L10n.T("crash_generic_title")
///
/// The engine exposes a string indexer; every live binding re-reads its value
/// when the language changes (PropertyChanged(null) refreshes all bindings).
/// Missing keys fall back to English and finally to the key itself, so new UI
/// is never blank while translations catch up.
/// </summary>
public sealed class L10n : System.ComponentModel.INotifyPropertyChanged
{
    public static L10n Instance { get; } = new();

    /// <summary>Display names exactly as shown in the settings combo.</summary>
    public static readonly string[] LanguageDisplayNames =
    {
        "Auto", "English", "Українська", "Русский", "Türkçe", "Deutsch", "Français",
    };

    private static Dictionary<string, Dictionary<string, string>>? _strings;

    /// <summary>Language code -> dictionary, built lazily on first access.</summary>
    private static Dictionary<string, Dictionary<string, string>> Strings =>
        _strings ??= new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal)
        {
            ["en"] = LocaleEn.Strings,
            ["uk"] = LocaleUk.Strings,
            ["ru"] = LocaleRu.Strings,
            ["tr"] = LocaleTr.Strings,
            ["de"] = LocaleDe.Strings,
            ["fr"] = LocaleFr.Strings,
        };

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Raised after the active language changed. TrExtension subscribes and
    /// re-applies every translated label in open windows.
    /// </summary>
    public static event Action? LanguageChanged;

    private static string _currentCode = DetectSystemCode();
    public static string CurrentLanguage => _currentCode;

    /// <summary>
    /// Translate a key in the current language. Never returns null or empty:
    /// falls back current dictionary -> English -> the key itself.
    /// </summary>
    public static string T(string key)
    {
        if (!string.IsNullOrEmpty(key))
        {
            var strings = Strings; // lazy, never null
            if (strings.TryGetValue(_currentCode, out var dict) && dict != null &&
                dict.TryGetValue(key, out var value) && !string.IsNullOrEmpty(value))
                return value;

            if (LocaleEn.Strings.TryGetValue(key, out var enValue) && !string.IsNullOrEmpty(enValue))
                return enValue;
        }
        return key ?? string.Empty;
    }

    /// <summary>Avalonia indexer binding source.</summary>
    public string this[string key] => T(key);

    /// <summary>Apply language by display name ("Auto" detects from OS).</summary>
    public static void ApplyDisplay(string? displayName)
    {
        string code = (displayName ?? "").Trim() switch
        {
            "English" or "en" => "en",
            "Українська" or "uk" => "uk",
            "Русский" or "ru" => "ru",
            "Türkçe" or "tr" => "tr",
            "Deutsch" or "de" => "de",
            "Français" or "fr" => "fr",
            "Auto" or "auto" => DetectSystemCode(),
            "" => DetectSystemCode(),
            _ => _currentCode,
        };

        bool changed = code != _currentCode;
        _currentCode = code;

        // Refresh every live {l10n:Tr} label, then notify plain indexer bindings.
        try { LanguageChanged?.Invoke(); } catch { }
        Instance.PropertyChanged?.Invoke(Instance, new System.ComponentModel.PropertyChangedEventArgs(null));
        if (changed)
            LauncherLog.Info($"UI language set to '{displayName}' ({code}).");
    }

    public static string CurrentDisplay =>
        _currentCode switch
        {
            "en" => "English",
            "uk" => "Українська",
            "ru" => "Русский",
            "tr" => "Türkçe",
            "de" => "Deutsch",
            "fr" => "Français",
            _ => "Auto",
        };

    private static string DetectSystemCode()
    {
        try
        {
            return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLowerInvariant() switch
            {
                "uk" => "uk",
                "ru" => "ru",
                "tr" => "tr",
                "de" => "de",
                "fr" => "fr",
                _ => "en",
            };
        }
        catch { return "en"; }
    }
}
