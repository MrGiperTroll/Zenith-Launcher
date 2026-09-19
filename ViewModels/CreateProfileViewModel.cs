using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CustomMcLauncher.Models;
using CustomMcLauncher.Services;

namespace CustomMcLauncher.ViewModels;

public partial class VersionGroup : ObservableObject
{
    public string Major { get; set; } = "";
    public ObservableCollection<string> Versions { get; } = new();
    [ObservableProperty] private bool _isExpanded;
}

/// <summary>One row of the flat version list: version id plus its type tag.</summary>
public sealed record FlatVersionItem(string Name, string Kind);

public partial class CreateProfileViewModel : ObservableObject
{
    private readonly IInstanceService _instanceService;
    private readonly ModLoaderService _modLoaderService;
    private readonly List<(string Name, string Type, string ReleaseTime)> _cachedVersions = new();
    
    [ObservableProperty] private bool _showReleases = true;
    [ObservableProperty] private bool _showSnapshots;
    [ObservableProperty] private bool _showBetas;
    [ObservableProperty] private bool _showAlphas;

    partial void OnShowReleasesChanged(bool value) => BuildVersionGroups();
    partial void OnShowSnapshotsChanged(bool value) => BuildVersionGroups();
    partial void OnShowBetasChanged(bool value) => BuildVersionGroups();
    partial void OnShowAlphasChanged(bool value) => BuildVersionGroups();

    private string? _pickedIconPath;

    [ObservableProperty] private int _currentStep = 1;
    public bool IsStep1 => CurrentStep == 1;
    public bool IsStep2 => CurrentStep == 2;
    public string StepTitle => CurrentStep switch { 1 => "Information", 2 => "Game Version", _ => "" };

    [ObservableProperty] private string _profileName = "My Minecraft Profile";
    [ObservableProperty] private string _selectedVersion = "";
    [ObservableProperty] private string _selectedLoader = "Vanilla";
    [ObservableProperty] private string _selectedLoaderBuild = "";
    [ObservableProperty] private string _versionSearchQuery = "";
    [ObservableProperty] private string _loaderError = "";
    [ObservableProperty] private bool _loaderErrorVisible;
    [ObservableProperty] private bool _isLoadingLoaders;
    [ObservableProperty] private Bitmap? _iconPreview;
    [ObservableProperty] private string _iconPath = "";
    [ObservableProperty] private IconOption? _selectedIconOption;
    [ObservableProperty] private bool _useFlatVersionList;
    partial void OnUseFlatVersionListChanged(bool value) => BuildVersionGroups();

    public ObservableCollection<string> AvailableVersions { get; } = new();
    public ObservableCollection<FlatVersionItem> FlatVersions { get; } = new();
    public ObservableCollection<VersionGroup> VersionGroups { get; } = new();
    public ObservableCollection<string> AvailableLoaders { get; } = new();
    public ObservableCollection<string> AvailableLoaderBuilds { get; } = new();
    public ObservableCollection<IconOption> AvailableIcons { get; } = new();

    [ObservableProperty] private int _selectedRamMb = 4096;
    [ObservableProperty] private int _maxRamMb = 16384;
    [ObservableProperty] private string _selectedResolution = "1920x1080";
    [ObservableProperty] private bool _isFullscreen;
    [ObservableProperty] private string _selectedJavaMode = "Recommended";
    [ObservableProperty] private string _customJavaPath = "";
    [ObservableProperty] private string _resolvedJavaPath = "";
    public bool IsCustomJavaMode => SelectedJavaMode == "Custom";
    partial void OnSelectedJavaModeChanged(string value) => OnPropertyChanged(nameof(IsCustomJavaMode));

    public ObservableCollection<string> ResolutionOptions { get; } = new();
    public ObservableCollection<JavaModeOption> JavaOptions { get; } = new();
    [ObservableProperty] private JavaModeOption? _selectedJavaOption;

    public ObservableCollection<string> JavaModes { get; } = new()
    {
        "Recommended",
        "System",
        "Custom"
    };

    public bool CanCreate => !string.IsNullOrWhiteSpace(SelectedVersion) && !IsNameDuplicate && (SelectedLoader == "Vanilla" || !string.IsNullOrWhiteSpace(SelectedLoaderBuild));
    public bool CanNextStep1 => !string.IsNullOrWhiteSpace(ProfileName) && !IsNameDuplicate;
    public bool CanNextStep2 => !string.IsNullOrWhiteSpace(SelectedVersion);
    public bool IsLoaderTypesVisible => true;
    public bool IsLoaderBuildsVisible => SelectedLoader != "Vanilla";
    public bool CanSwapLoader => false;
    public bool IsNameDuplicate
    {
        get
        {
            if (string.IsNullOrWhiteSpace(ProfileName)) return false;
            return _instanceService.GetInstances().Any(i => i.Name.Equals(ProfileName.Trim(), StringComparison.OrdinalIgnoreCase));
        }
    }

    public event Action<InstanceModel>? ProfileCreated;
    public event Action? RequestClose;

    public CreateProfileViewModel(IInstanceService instanceService, ModLoaderService modLoaderService,
        bool flatVersionList = false, bool showReleases = true, bool showSnapshots = false,
        bool showBetas = false, bool showAlphas = false)
    {
        _instanceService = instanceService;
        _modLoaderService = modLoaderService;
        _useFlatVersionList = flatVersionList;
        _showReleases = showReleases;
        _showSnapshots = showSnapshots;
        _showBetas = showBetas;
        _showAlphas = showAlphas;
        ProfileName = GenerateUniqueProfileName();
        // Seed synchronously so the ComboBox has "Vanilla" at binding time and
        // renders it preselected on first open instead of an empty box.
        foreach (var l in _modLoaderService.GetSupportedLoaders(""))
            if (!AvailableLoaders.Contains(l)) AvailableLoaders.Add(l);
        _ = LoadVersionsAsync();
        _ = InitializeLoadersAsync();
        LoadIcons();
        InitializeResolutions();
        InitializeJavaOptions();
        try
        {
            var physMb = (int)(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024 * 1024));
            if (physMb > 4096) MaxRamMb = physMb;
        }
        catch { }
    }

    private void InitializeResolutions()
    {
        ResolutionOptions.Clear();

        var nativeWidth = 1920;
        var nativeHeight = 1080;
        try
        {
            var topLevel = Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime d ? d.MainWindow : null;
            var screen = topLevel?.Screens.Primary ?? topLevel?.Screens.All.FirstOrDefault();
            if (screen != null && screen.Bounds.Width > 0 && screen.Bounds.Height > 0)
            {
                nativeWidth = screen.Bounds.Width;
                nativeHeight = screen.Bounds.Height;
            }
        }
        catch { }

        var list = new List<(int Width, int Height)>
        {
            (1024, 768),
            (1280, 720),
            (1280, 800),
            (1366, 768),
            (1440, 900),
            (1280, 1024),
            (1600, 900),
            (1680, 1050),
            (1920, 1080),
            (1920, 1200),
            (2560, 1440),
            (2560, 1600),
            (3840, 2160)
        };

        if (!list.Any(r => r.Width == nativeWidth && r.Height == nativeHeight))
        {
            list.Add((nativeWidth, nativeHeight));
        }

        var sorted = list.OrderBy(r => r.Width * r.Height).ThenBy(r => r.Width).ToList();
        string? selectedItem = null;
        var currentSuffix = L10n.T("res_current");

        foreach (var (w, h) in sorted)
        {
            if (w == nativeWidth && h == nativeHeight)
            {
                var label = $"{w}x{h} {currentSuffix}".Trim();
                ResolutionOptions.Add(label);
                selectedItem = label;
            }
            else
            {
                ResolutionOptions.Add($"{w}x{h}");
            }
        }

        SelectedResolution = selectedItem ?? ResolutionOptions.FirstOrDefault() ?? "1920x1080";
    }

    private void InitializeJavaOptions()
    {
        var prevSelectedId = SelectedJavaOption?.Id ?? SelectedJavaMode;
        JavaOptions.Clear();
        var recMajor = JavaVersionHelper.InferRequiredJavaMajor(SelectedVersion ?? "1.21.4");
        var recPath = JavaVersionHelper.FindJavaForVersion(recMajor) ?? JavaVersionHelper.FindSystemJavaPath() ?? "javaw.exe";
        var sysPath = JavaVersionHelper.FindSystemJavaPath() ?? "javaw.exe";
        var sysMajor = JavaVersionHelper.GetJavaMajor(sysPath);

        var recOption = new JavaModeOption
        {
            Id = "Recommended",
            DisplayName = $"Recommended (Java {recMajor})",
            Path = recPath
        };

        var sysOption = new JavaModeOption
        {
            Id = "System",
            DisplayName = $"System (Java {sysMajor})",
            Path = sysPath
        };

        var customOption = new JavaModeOption
        {
            Id = "Custom",
            DisplayName = "Custom",
            Path = !string.IsNullOrWhiteSpace(CustomJavaPath) ? CustomJavaPath : sysPath
        };

        JavaOptions.Add(recOption);
        JavaOptions.Add(sysOption);
        JavaOptions.Add(customOption);

        var matched = JavaOptions.FirstOrDefault(o => o.Id == prevSelectedId) ?? recOption;
        SelectedJavaOption = matched;
        SelectedJavaMode = matched.Id;
        ResolvedJavaPath = matched.Id == "Custom" ? (string.IsNullOrWhiteSpace(CustomJavaPath) ? sysPath : CustomJavaPath) : matched.Path;
    }

    partial void OnSelectedJavaOptionChanged(JavaModeOption? value)
    {
        if (value == null) return;
        SelectedJavaMode = value.Id;
        ResolvedJavaPath = value.Id == "Custom" ? CustomJavaPath : value.Path;
        OnPropertyChanged(nameof(IsCustomJavaMode));
    }

    partial void OnCustomJavaPathChanged(string value)
    {
        if (SelectedJavaMode == "Custom")
            ResolvedJavaPath = value;
    }

    [RelayCommand]
    public void GoToStep(string stepStr)
    {
        if (int.TryParse(stepStr, out var s))
        {
            if (s == 1 && CurrentStep == 2)
                CurrentStep = 1;
            else if (s == 2 && CurrentStep == 1 && CanNextStep1)
                CurrentStep = 2;
        }
    }

    private string GenerateUniqueProfileName()
    {
        const string baseName = "My Minecraft Profile";
        var instances = _instanceService.GetInstances().ToList();
        if (!instances.Any(i => i.Name.Equals(baseName, StringComparison.OrdinalIgnoreCase)))
            return baseName;
        for (var n = 1; ; n++)
        {
            var candidate = $"{baseName} {n}";
            if (!instances.Any(i => i.Name.Equals(candidate, StringComparison.OrdinalIgnoreCase)))
                return candidate;
        }
    }

    partial void OnCurrentStepChanged(int value)
    {
        OnPropertyChanged(nameof(IsStep1));
        OnPropertyChanged(nameof(IsStep2));
        OnPropertyChanged(nameof(StepTitle));
    }
    partial void OnProfileNameChanged(string value)
    {
        OnPropertyChanged(nameof(CanCreate));
        OnPropertyChanged(nameof(CanNextStep1));
        OnPropertyChanged(nameof(IsNameDuplicate));
    }
    partial void OnSelectedVersionChanged(string value)
    {
        OnPropertyChanged(nameof(CanCreate));
        OnPropertyChanged(nameof(CanNextStep2));
        InitializeJavaOptions();
        // The loader list is static (every loader is always selectable) - it must NOT
        // be rebuilt here: rebuilding reset the ComboBox selection and visually
        // collapsed/blocked the loader dropdown whenever the user clicked a version.
        _ = UpdateLoaderBuildsAsync();
    }
    partial void OnSelectedLoaderChanged(string value)
    {
        _ = UpdateLoaderBuildsAsync();
        _ = RefreshLoaderGameFilterAsync();
        OnPropertyChanged(nameof(CanSwapLoader));
        OnPropertyChanged(nameof(CanCreate));
        OnPropertyChanged(nameof(IsLoaderBuildsVisible));
    }
    partial void OnSelectedLoaderBuildChanged(string value) => OnPropertyChanged(nameof(CanCreate));
    partial void OnVersionSearchQueryChanged(string value) => FilterVersionGroups();

    private void LoadIcons()
    {
        AvailableIcons.Clear();
        foreach (var icon in IconGalleryService.GetAllIcons())
            AvailableIcons.Add(icon);
    }

    private async Task LoadVersionsAsync()
    {
        try
        {
            var path = new CmlLib.Core.MinecraftPath(ZenithPaths.AppDataDir);
            var launcher = new CmlLib.Core.MinecraftLauncher(path);
            var versions = await LaunchService.WithManifestRetryAsync(() => launcher.GetAllVersionsAsync().AsTask());
            _cachedVersions.Clear();
            // Store the RAW type: local custom entries (modloader profiles) have no
            // Mojang type and must be classified by id shape, not forced to "release".
            foreach (var v in versions) _cachedVersions.Add((v.Name, v.Type ?? "", ""));
            // Enrich with release dates from Mojang's manifest v2 - needed to place
            // snapshots inside the folder of the release they lead up to.
            var dates = await FetchReleaseDatesAsync();
            for (var i = 0; i < _cachedVersions.Count; i++)
                if (_cachedVersions[i].ReleaseTime.Length == 0 && dates.TryGetValue(_cachedVersions[i].Name, out var dt))
                    _cachedVersions[i] = (_cachedVersions[i].Name, _cachedVersions[i].Type, dt);
            SaveVersionCache();
            BuildVersionGroups();
        }
        catch
        {
            _cachedVersions.Clear();
            LoadVersionCache();
            if (_cachedVersions.Count == 0)
            {
                _cachedVersions.Add(("1.21.1", "release", ""));
                _cachedVersions.Add(("1.20.1", "release", ""));
                _cachedVersions.Add(("1.16.5", "release", ""));
            }
            BuildVersionGroups();
        }
        // Do not auto-select version - user must click
    }

    private static string VersionCachePath => Path.Combine(ZenithPaths.AppDataDir, "versions_cache.json");

    private void SaveVersionCache()
    {
        try { File.WriteAllText(VersionCachePath, System.Text.Json.JsonSerializer.Serialize(_cachedVersions)); } catch { }
    }

    private void LoadVersionCache()
    {
        if (!File.Exists(VersionCachePath)) return;
        try
        {
            var cached = System.Text.Json.JsonSerializer.Deserialize<List<(string Name, string Type, string ReleaseTime)>>(File.ReadAllText(VersionCachePath));
            if (cached != null) _cachedVersions.AddRange(cached);
        }
        catch { }
    }

    private static async Task<Dictionary<string, string>> FetchReleaseDatesAsync()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            var json = await WebFallback.GetStringFirstAsync("https://piston-meta.mojang.com/mc/game/version_manifest_v2.json").ConfigureAwait(false);
            if (json == null) return map;
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("versions", out var arr))
            {
                foreach (var el in arr.EnumerateArray())
                {
                    var id = el.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
                    var rt = el.TryGetProperty("releaseTime", out var rtEl) ? rtEl.GetString() : null;
                    if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(rt))
                        map[id!] = rt!;
                }
            }
        }
        catch (Exception ex)
        {
            LauncherLog.Error("Failed to fetch Mojang manifest v2 release dates", ex);
        }
        return map;
    }

    private void BuildVersionGroups()
    {
        // Remember which folders the user had open so rebuilding the list (loader
        // switch, search, flat-mode toggle) never collapses the accordion state.
        var expandedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var g in VersionGroups)
            if (g.IsExpanded) expandedKeys.Add(g.Major);

        AvailableVersions.Clear();
        FlatVersions.Clear();
        VersionGroups.Clear();

        var releases = new List<(string Name, DateTimeOffset Date)>();
        var snapshots = new List<(string Name, DateTimeOffset Date)>();
        var betas = new List<(string Name, DateTimeOffset Date)>();
        var alphas = new List<(string Name, DateTimeOffset Date)>();

        // Loader profiles ("1.20.1-forge-...", "fabric-loader-...-1.21") are grouped
        // under their BASE Minecraft version folder instead of junk keys.
        var groupKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (name, rawType, relTime) in _cachedVersions)
        {
            // Locally installed modloader profiles are launcher internals,
            // not game versions - never show them in any list.
            if (ModLoaderService.IsLoaderProfileId(name)) continue;

            var date = ParseDate(relTime);
            var entry = (name, date);

            // Trust the Mojang manifest types unconditionally - special snapshots
            // ("3D Shareware v1.34", "20w14infinite", "1.14 Pre-Release 5", "19w09a")
            // are real snapshot entries and must follow their release, never spawn
            // phantom folders. Shape-based classification applies ONLY to local
            // custom entries with no manifest type.
            switch (rawType)
            {
                case "release":
                    if (ShowReleases) releases.Add(entry);
                    break;
                case "snapshot":
                    if (ShowSnapshots) snapshots.Add(entry);
                    break;
                case "old_beta":
                    if (ShowBetas) betas.Add(entry);
                    break;
                case "old_alpha":
                    if (ShowAlphas) alphas.Add(entry);
                    break;
                default:
                    // Unknown/local entry - classify by id. Genuine custom copies of a
                    // vanilla version land in their base folder; junk stays hidden.
                    if (LooksClassicOrAlphaId(name))
                    {
                        if (ShowAlphas) alphas.Add(entry);
                    }
                    else if (TryGetBaseMcVersion(name, out var loaderBase))
                    {
                        // Modloader profile -> its base Minecraft folder.
                        if (ShowReleases) releases.Add(entry);
                        groupKey[name] = GetMajorVersion(loaderBase);
                    }
                    // anything else stays hidden
                    break;
            }
        }

        releases.Sort((a, b) => b.Date.CompareTo(a.Date)); // newest first
        var releasesAsc = releases.AsEnumerable().Reverse().ToList();

        // Snapshots live inside the folder of the release they lead up to.
        var snapshotFolders = new Dictionary<string, List<(string Name, DateTimeOffset Date)>>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in snapshots.OrderByDescending(s => s.Date))
        {
            var folder = ResolveSnapshotFolder(s.Name, s.Date, releasesAsc);
            if (!snapshotFolders.TryGetValue(folder, out var list))
                snapshotFolders[folder] = list = new List<(string Name, DateTimeOffset Date)>();
            list.Add(s);
        }

        var query = VersionSearchQuery?.Trim() ?? "";
        bool SupportedByLoader(string v) =>
            _loaderSupportedGames == null || _loaderSupportedGames.Contains(v);
        bool Match(string v) =>
            (query.Length == 0 || v.Contains(query, StringComparison.OrdinalIgnoreCase)) && SupportedByLoader(v);

        // Full flat ordering, newest first (also drives ResetWizard's default pick).
        foreach (var r in releases.Where(r => Match(r.Name))) AvailableVersions.Add(r.Name);
        foreach (var s in snapshots.OrderByDescending(s => s.Date).Where(s => Match(s.Name))) AvailableVersions.Add(s.Name);
        foreach (var b in betas.OrderByDescending(b => b.Date).Where(b => Match(b.Name))) AvailableVersions.Add(b.Name);
        foreach (var a in alphas.OrderByDescending(a => a.Date).Where(a => Match(a.Name))) AvailableVersions.Add(a.Name);

        if (UseFlatVersionList)
        {
            foreach (var item in FlatOrder(releases, snapshots, betas, alphas).Where(i => Match(i.Name)))
                FlatVersions.Add(item);
            OnPropertyChanged(nameof(CanCreate));
            OnPropertyChanged(nameof(CanNextStep2));
            return;
        }

        // Grouped accordion: major folders + Beta / Alpha buckets at the very bottom.
        var buckets = new Dictionary<string, List<(string Name, DateTimeOffset Date)>>(StringComparer.OrdinalIgnoreCase);
        void AddTo(string key, (string Name, DateTimeOffset Date) e)
        {
            if (!buckets.TryGetValue(key, out var l)) buckets[key] = l = new List<(string Name, DateTimeOffset Date)>();
            l.Add(e);
        }
        foreach (var r in releases.Where(r => Match(r.Name)))
            AddTo(groupKey.TryGetValue(r.Name, out var k) ? k : GetMajorVersion(r.Name), r);
        foreach (var kv in snapshotFolders)
            foreach (var s in kv.Value.Where(s => Match(s.Name)))
                AddTo(kv.Key, s);

        foreach (var key in buckets.Keys.OrderByDescending(k => k, Comparer<string>.Create(CompareMajorVersions)))
        {
            var vg = new VersionGroup { Major = key, IsExpanded = expandedKeys.Contains(key) };
            foreach (var ver in buckets[key].OrderByDescending(e => e.Date)
                         .ThenByDescending(e => e.Name, Comparer<string>.Create(CompareVersions)))
                vg.Versions.Add(ver.Name);
            VersionGroups.Add(vg);
        }
        if (betas.Any(b => Match(b.Name)))
        {
            var vg = new VersionGroup { Major = "Beta", IsExpanded = expandedKeys.Contains("Beta") };
            foreach (var b in betas.Where(b => Match(b.Name)).OrderByDescending(b => b.Date)) vg.Versions.Add(b.Name);
            VersionGroups.Add(vg);
        }
        if (alphas.Any(a => Match(a.Name)))
        {
            var vg = new VersionGroup { Major = "Alpha", IsExpanded = expandedKeys.Contains("Alpha") };
            foreach (var a in alphas.Where(a => Match(a.Name)).OrderByDescending(a => a.Date)) vg.Versions.Add(a.Name);
            VersionGroups.Add(vg);
        }
        OnPropertyChanged(nameof(CanCreate));
        OnPropertyChanged(nameof(CanNextStep2));
    }

    private static DateTimeOffset ParseDate(string iso)
        => DateTimeOffset.TryParse(iso, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind, out var d) ? d : DateTimeOffset.MinValue;

    /// <summary>Single newest-first ordering used by the flat list mode.</summary>
    private static IEnumerable<FlatVersionItem> FlatOrder(
        List<(string Name, DateTimeOffset Date)> releases,
        List<(string Name, DateTimeOffset Date)> snapshots,
        List<(string Name, DateTimeOffset Date)> betas,
        List<(string Name, DateTimeOffset Date)> alphas)
    {
        foreach (var r in releases) yield return new FlatVersionItem(r.Name, "Release");
        foreach (var s in snapshots.OrderByDescending(s => s.Date)) yield return new FlatVersionItem(s.Name, "Snapshot");
        foreach (var b in betas.OrderByDescending(b => b.Date)) yield return new FlatVersionItem(b.Name, "Beta");
        foreach (var a in alphas.OrderByDescending(a => a.Date)) yield return new FlatVersionItem(a.Name, "Alpha");
    }

    private static string ResolveSnapshotFolder(string id, DateTimeOffset date, List<(string Name, DateTimeOffset Date)> releasesAsc)
    {
        // The release a snapshot leads up to is the next one chronologically.
        foreach (var r in releasesAsc)
            if (r.Date > date && date != DateTimeOffset.MinValue)
                return GetMajorVersion(r.Name);
        // Fallback without dates: leading numeric base covers "1.20.5-pre2",
        // "1.14 Pre-Release 5" and "26.3-snapshot-9".
        var m = NumericPrefixRegex.Match(id);
        if (m.Success && m.Value.Contains('.')) return GetMajorVersion(m.Value);
        return releasesAsc.Count > 0 ? GetMajorVersion(releasesAsc[releasesAsc.Count - 1].Name) : GetMajorVersion(id);
    }

    /// <summary>Classic-era ids only: a1.x / c0.x / rd-* / in-* (incl. inf-).</summary>
    private static bool LooksClassicOrAlphaId(string id)
    {
        if (id.Length < 2) return false;
        var lower = id.ToLowerInvariant();
        if (lower.StartsWith("rd-") || lower.StartsWith("in-")) return true;
        return (lower[0] == 'a' || lower[0] == 'c') && char.IsDigit(lower[1]);
    }

    private static readonly Regex NumericVersionRegex =
        new(@"\d+(?:\.\d+){1,3}", RegexOptions.Compiled);

    /// <summary>
    /// Extracts the base Minecraft version from a modloader profile id
    /// ("1.20.1-forge-47.3.0" -> 1.20.1, "fabric-loader-0.16.9-1.21.4" -> 1.21.4).
    /// The MC version is the LAST dotted number that starts with 1. or 26.
    /// </summary>
    private static bool TryGetBaseMcVersion(string id, out string baseVersion)
    {
        baseVersion = "";
        foreach (Match m in NumericVersionRegex.Matches(id))
        {
            var v = m.Value;
            if (v.StartsWith("1.", StringComparison.Ordinal) || v.StartsWith("26.", StringComparison.Ordinal))
                baseVersion = v;
        }
        return baseVersion.Length > 0;
    }

    // Live loader-support filter for the game-version selector (task: hide
    // versions unsupported by the currently chosen loader).
    private HashSet<string>? _loaderSupportedGames;

    private async Task RefreshLoaderGameFilterAsync()
    {
        var loader = SelectedLoader;
        HashSet<string>? supported = null;
        if (!string.IsNullOrWhiteSpace(loader) && loader != "Vanilla")
        {
            try
            {
                await _modLoaderService.InitializeDatabaseAsync().ConfigureAwait(true);
                supported = _modLoaderService.GetCachedSupportedVersions(loader);
            }
            catch (Exception ex)
            {
                LauncherLog.Error($"Failed to load {loader} support list", ex);
                supported = null; // fail-open: show everything rather than an empty list
            }
        }
        _loaderSupportedGames = supported;
        Avalonia.Threading.Dispatcher.UIThread.Post(BuildVersionGroups);
    }

    private static string GetMajorVersion(string version)
    {
        // Only the LEADING numeric run counts ("1.14 Pre-Release 5" -> "1.14"),
        // so suffix junk can never create phantom folder keys like "1.14 Pre".
        var m = NumericPrefixRegex.Match(version);
        if (!m.Success) return version;
        var num = m.Value;

        // Handle new versioning 26.x and old 1.x
        if (num.StartsWith("26.")) return "26";
        var parts = num.Split('.');
        if (parts.Length >= 2 && parts[0] == "1") return $"{parts[0]}.{parts[1]}";
        return parts[0];
    }

    private static readonly Regex NumericPrefixRegex = new(@"^\d+(?:\.\d+)*", RegexOptions.Compiled);

    private static int CompareMajorVersions(string a, string b)
    {
        // 26 > 1.21 > 1.20 > ... > 1.7
        if (a == "26" && b != "26") return 1;
        if (b == "26" && a != "26") return -1;
        return CompareVersions(a, b);
    }

    private static int CompareVersions(string a, string b)
    {
        var pa = a.Split('.', '-').Select(p => int.TryParse(p, out var n) ? n : 0).ToArray();
        var pb = b.Split('.', '-').Select(p => int.TryParse(p, out var n) ? n : 0).ToArray();
        for (int i = 0; i < Math.Max(pa.Length, pb.Length); i++)
        {
            var av = i < pa.Length ? pa[i] : 0;
            var bv = i < pb.Length ? pb[i] : 0;
            if (av != bv) return av.CompareTo(bv);
        }
        return 0;
    }

    private void FilterVersionGroups() => BuildVersionGroups();

    private async Task InitializeLoadersAsync()
    {
        await _modLoaderService.InitializeDatabaseAsync();
        // Apply the loader-support game-version filter as soon as the DB is warm,
        // so an already-selected loader filters the list without extra clicks.
        await RefreshLoaderGameFilterAsync();
    }

    // Monotonic id: only the most recent builds request may touch the UI.
    private int _buildsRequestId;

    private async Task UpdateLoaderBuildsAsync()
    {
        var requestId = ++_buildsRequestId;
        var loader = SelectedLoader;
        var version = SelectedVersion;
        if (string.IsNullOrWhiteSpace(loader) || loader == "Vanilla")
        {
            AvailableLoaderBuilds.Clear();
            SelectedLoaderBuild = "";
            OnPropertyChanged(nameof(SelectedLoaderBuild));
            LoaderErrorVisible = false;
            LoaderError = "";
            IsLoadingLoaders = false;
            return;
        }
        IsLoadingLoaders = true;
        List<string> builds;
        try
        {
            builds = await _modLoaderService.GetLoaderBuildsAsync(version, loader).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            LauncherLog.Error($"Failed to load {loader} builds for {version}", ex);
            builds = new List<string> { "Latest (Auto)" };
        }
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (requestId != _buildsRequestId) return; // stale response - a newer request owns the UI
            AvailableLoaderBuilds.Clear();
            foreach (var b in builds) AvailableLoaderBuilds.Add(b);
            SelectedLoaderBuild = AvailableLoaderBuilds.FirstOrDefault() ?? "";
            OnPropertyChanged(nameof(SelectedLoaderBuild));
            LoaderErrorVisible = false;
            LoaderError = "";
            IsLoadingLoaders = false;
        });
    }

    [RelayCommand]
    private void SelectLoader(string loader)
    {
        if (!string.IsNullOrWhiteSpace(loader))
            SelectedLoader = loader;
    }

    [RelayCommand]
    private void SelectVersion(string version)
    {
        if (!string.IsNullOrWhiteSpace(version))
            SelectedVersion = version;
    }

    [RelayCommand]
    private void ToggleVersionGroup(VersionGroup group)
    {
        if (group != null) group.IsExpanded = !group.IsExpanded;
    }

    // Toggle no longer used - loader builds shown automatically for non-Vanilla
    [RelayCommand]
    private void ToggleLoaderBuilds() { }

    [RelayCommand]
    private void NextStep()
    {
        if (CurrentStep == 1 && !CanNextStep1) return;
        if (CurrentStep < 2) CurrentStep++;
    }

    [RelayCommand]
    private void PreviousStep()
    {
        if (CurrentStep > 1) CurrentStep--;
    }

    [RelayCommand]
    private void ResetWizard()
    {
        ProfileName = GenerateUniqueProfileName();
        SelectedVersion = AvailableVersions.FirstOrDefault() ?? "";
        SelectedLoader = "Vanilla";
        SelectedIconOption = null;
        IconPreview = null;
        IconPath = "";
        _pickedIconPath = null;
        VersionSearchQuery = "";
        CurrentStep = 1;
    }

    [RelayCommand]
    private void SelectIcon(IconOption? icon)
    {
        if (icon == null) return;
        SelectedIconOption = icon;
        if (!string.IsNullOrWhiteSpace(icon.FilePath) && File.Exists(icon.FilePath))
        {
            _pickedIconPath = icon.FilePath;
            IconPath = icon.FilePath;
            try { IconPreview?.Dispose(); IconPreview = new Bitmap(icon.FilePath); } catch { }
        }
        else if (!string.IsNullOrWhiteSpace(icon.GeometryData))
        {
            try
            {
                var bmp = RenderIconToBitmap(icon.GeometryData, icon.Color, 128);
                var tmp = Path.Combine(Path.GetTempPath(), $"zenith_icon_{Guid.NewGuid():N}.png");
#pragma warning disable CS0618 // Bitmap.Save(string) is obsolete in Avalonia 12 but still works
                bmp.Save(tmp);
#pragma warning restore CS0618
                _pickedIconPath = tmp;
                IconPath = tmp;
                IconPreview?.Dispose();
                IconPreview = bmp;
            }
            catch { }
        }
    }

    [RelayCommand]
    private async Task PickCustomIconAsync()
    {
        var topLevel = Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime d ? d.MainWindow : null;
        if (topLevel?.StorageProvider == null) return;
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title = "Select Custom Icon",
            AllowMultiple = false,
            FileTypeFilter = new[] { Avalonia.Platform.Storage.FilePickerFileTypes.ImageAll }
        });
        if (files.Count > 0)
        {
            var src = files[0].Path.LocalPath;
            var ext = Path.GetExtension(src).ToLowerInvariant();
            if (ext is not (".png" or ".jpg" or ".jpeg" or ".webp" or ".bmp"))
            {
                // Invalid type
                return;
            }
            var info = new FileInfo(src);
            if (info.Length > 5 * 1024 * 1024)
            {
                // Too large
                return;
            }
            var dest = IconGalleryService.SaveCustomIcon(src);
            var newIcon = new IconOption { Name = Path.GetFileNameWithoutExtension(dest), Category = "Custom", FilePath = dest, IsCustom = true };
            AvailableIcons.Add(newIcon);
            SelectedIconOption = newIcon;
            _pickedIconPath = dest;
            IconPath = dest;
            try { IconPreview?.Dispose(); IconPreview = new Bitmap(dest); } catch { }
        }
    }

    [RelayCommand]
    private void ResetIcon()
    {
        SelectedIconOption = null;
        IconPreview = null;
        IconPath = "";
        _pickedIconPath = null;
    }

    private static Bitmap RenderIconToBitmap(string geometryData, string colorHex, int size)
    {
        var geometry = Geometry.Parse(geometryData);
        var bgColor = Color.Parse(colorHex);
        var pixelSize = new PixelSize(size, size);
        var dpi = new Vector(96, 96);
        var rtb = new RenderTargetBitmap(pixelSize, dpi);
        using (var ctx = rtb.CreateDrawingContext())
        {
            ctx.FillRectangle(new SolidColorBrush(bgColor), new Rect(0, 0, size, size));
            var bounds = geometry.Bounds;
            if (bounds.Width > 0 && bounds.Height > 0)
            {
                var scale = Math.Min((size * 0.55) / bounds.Width, (size * 0.55) / bounds.Height);
                var tx = (size - bounds.Width * scale) / 2 - bounds.X * scale;
                var ty = (size - bounds.Height * scale) / 2 - bounds.Y * scale;
                var transform = new Matrix(scale, 0, 0, scale, tx, ty);
                using (ctx.PushTransform(transform))
                {
                    ctx.DrawGeometry(Brushes.White, null, geometry);
                }
            }
        }
        return rtb;
    }

    [RelayCommand]
    private void SelectPresetIcon(IconOption option)
    {
        SelectedIconOption = option;
        if (option.IsCustom && !string.IsNullOrEmpty(option.FilePath) && File.Exists(option.FilePath))
        {
            _pickedIconPath = option.FilePath;
            IconPath = option.FilePath;
            try { IconPreview?.Dispose(); IconPreview = new Bitmap(option.FilePath); } catch { }
        }
        else if (!string.IsNullOrEmpty(option.GeometryData))
        {
            try
            {
                var bmp = RenderIconToBitmap(option.GeometryData, option.Color, 128);
                var cacheDir = Path.Combine(ZenithPaths.AppDataDir, "temp_icons");
                Directory.CreateDirectory(cacheDir);
                var safeName = Regex.Replace(option.Name, @"[^\w]", "_");
                var outPath = Path.Combine(cacheDir, $"{safeName}.png");
                bmp.Save(outPath);
                _pickedIconPath = outPath;
                IconPath = outPath;
                IconPreview?.Dispose();
                IconPreview = bmp;
            }
            catch (Exception ex)
            {
                LauncherLog.Error("Failed to select preset icon", ex);
            }
        }
    }

    [RelayCommand]
    private async Task PickCustomJavaAsync()
    {
        var topLevel = Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime d ? d.MainWindow : null;
        if (topLevel?.StorageProvider == null) return;
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title = "Select Java Binary (javaw.exe / java)",
            AllowMultiple = false
        });
        if (files.Count > 0)
        {
            CustomJavaPath = files[0].Path.LocalPath;
            SelectedJavaMode = "Custom";
        }
    }

    private static void ParseResolution(string res, out int width, out int height)
    {
        width = 1280;
        height = 720;
        var m = Regex.Match(res, @"^(\d+)x(\d+)");
        if (m.Success && int.TryParse(m.Groups[1].Value, out var w) && int.TryParse(m.Groups[2].Value, out var h))
        {
            width = w;
            height = h;
        }
    }

    [RelayCommand]
    private void Cancel() => RequestClose?.Invoke();

    [RelayCommand]
    private async Task CreateAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedVersion)) return;
        var name = ProfileName.Trim();
        if (string.IsNullOrWhiteSpace(name)) name = "My Minecraft Profile";
        var inst = await _instanceService.CreateInstanceAsync(name, SelectedVersion, SelectedLoader, SelectedLoaderBuild);
        if (inst != null)
        {
            inst.RamMb = SelectedRamMb;
            inst.MaxMemoryMb = SelectedRamMb;
            ParseResolution(SelectedResolution, out var w, out var h);
            inst.GameWidth = w;
            inst.GameHeight = h;
            inst.IsFullscreen = IsFullscreen;
            inst.JavaMode = SelectedJavaMode;
            if (SelectedJavaMode == "Custom" && !string.IsNullOrWhiteSpace(CustomJavaPath))
                inst.CustomJavaPath = CustomJavaPath;

            if (!string.IsNullOrEmpty(_pickedIconPath) && File.Exists(_pickedIconPath))
            {
                var dest = Path.Combine(inst.Path, $"icon{Path.GetExtension(_pickedIconPath)}");
                try { File.Copy(_pickedIconPath, dest, true); inst.IconPath = dest; } catch { }
            }
            await _instanceService.SaveInstanceAsync(inst);
            ProfileCreated?.Invoke(inst);
            RequestClose?.Invoke();
        }
    }
}

public class JavaModeOption
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Path { get; set; } = "";
    public override string ToString() => DisplayName;
}

