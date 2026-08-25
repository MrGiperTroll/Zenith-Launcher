using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Markup.Xaml;
using CustomMcLauncher.Services;

namespace CustomMcLauncher.Extensions;

/// <summary>
/// XAML translation hook:  Text="{l10n:Tr set_memory}"
///
/// Deliberately avoids binding-to-indexer tricks: the extension returns the
/// translated string immediately and registers the (element, property, key)
/// triple in a weak table. When the language changes, L10n raises
/// LanguageChanged and every registered live element is re-set directly.
/// This can never produce blank labels - T() always returns current ->
/// English -> key, never null or empty.
/// </summary>
public class TrExtension : MarkupExtension
{
    public string Key { get; set; } = string.Empty;

    public TrExtension() { }

    public TrExtension(string key) => Key = key;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var translated = SafeTranslate(Key);

        // Register for automatic refresh on language switch when we know the
        // target object/property (styles / design mode provide neither).
        try
        {
            var target = serviceProvider.GetService(typeof(Avalonia.Markup.Xaml.IProvideValueTarget))
                as Avalonia.Markup.Xaml.IProvideValueTarget;

            if (target?.TargetObject is AvaloniaObject ao && target.TargetProperty is AvaloniaProperty prop)
            {
                RegisterLive(ao, prop, Key);
            }
        }
        catch { }

        return translated;
    }

    internal static string SafeTranslate(string key)
    {
        try { return L10n.T(key); }
        catch { return key ?? string.Empty; }
    }

    // ------------------------------------------------------------ live table

    private sealed class Entry
    {
        public AvaloniaProperty Property;
        public string Key;
        public Entry(AvaloniaProperty p, string k) { Property = p; Key = k; }
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<AvaloniaObject, List<Entry>> Live =
        new();

    private static bool _hooked;

    private static void RegisterLive(AvaloniaObject target, AvaloniaProperty property, string key)
    {
        HookOnce();

        var list = Live.GetOrCreateValue(target);
        lock (list)
        {
            if (!list.Any(e => e.Property == property && e.Key == key))
                list.Add(new Entry(property, key));
        }
    }

    private static void HookOnce()
    {
        if (_hooked) return;
        _hooked = true;
        L10n.LanguageChanged += RefreshAll;
    }

    /// <summary>Re-applies every registered translation (called on language change).</summary>
    private static void RefreshAll()
    {
        foreach (var pair in Live)
        {
            var list = pair.Value;
            lock (list)
            {
                foreach (var entry in list)
                {
                    try { pair.Key.SetValue(entry.Property, SafeTranslate(entry.Key)); }
                    catch { }
                }
            }
        }
    }
}
