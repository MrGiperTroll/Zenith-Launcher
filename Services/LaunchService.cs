using System.IO.Compression;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CmlLib.Core;
using CmlLib.Core.Auth;
using CmlLib.Core.ProcessBuilder;
using CmlLib.Core.Version;
using CustomMcLauncher.Models;
using Avalonia.Threading;

namespace CustomMcLauncher.Services;

public class LaunchService : ILaunchService
{
    private readonly IInstanceService _instanceService;
    private readonly ConcurrentDictionary<string, Process> _runningProcesses = new();
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> InstallLocks = new();
    private static readonly HttpClientHandler HttpHandler = new()
        {
            AllowAutoRedirect = true,
        };

        private static readonly HttpClient Http = new(HttpHandler)
        {
            Timeout = TimeSpan.FromSeconds(120),
        };

        static LaunchService()
        {
            Http.DefaultRequestHeaders.UserAgent.ParseAdd("ZenithLauncher");
            Http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));
        }

    // CmlLib rewrites <root>\version_manifest_v2.json on every InstallAsync /
    // GetAllVersionsAsync / GetVersionAsync call. When two flows run at once
    // (e.g. a background "--launch" shortcut start while the main window loads
    // its version list, or two launcher processes) they fight over that file:
    // "The process cannot access the file ... because it is being used by
    // another process". The retry below rides out those transient conflicts.
    public static async Task<T> WithManifestRetryAsync<T>(Func<Task<T>> action)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                return await action().ConfigureAwait(false);
            }
            catch (IOException) when (attempt < 6)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt)).ConfigureAwait(false);
            }
        }
    }

    public static Task WithManifestRetryAsync(Func<Task> action) =>
        WithManifestRetryAsync<object?>(async () => { await action().ConfigureAwait(false); return null; });

    // Safe JVM args for modern Java (17/21) — no deprecated/removed flags
    private static readonly MArgument[] ModernSafeJvmArgs =
    [
        new MArgument("-XX:+UseG1GC"),
        new MArgument("-XX:MaxGCPauseMillis=50"),
        new MArgument("-Dlog4j2.formatMsgNoLookups=true"),
    ];

    // Safe JVM args for legacy Java (8/11) — standard flags, compatible with Java 8
    private static readonly MArgument[] LegacySafeJvmArgs =
    [
        new MArgument("-XX:+UseG1GC"),
        new MArgument("-Dlog4j2.formatMsgNoLookups=true"),
    ];

    private static IEnumerable<MArgument> SanitizeJvmArgs(IEnumerable<MArgument> args, int javaMajor)
    {
        var obsoleteOnModern = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "-XX:+UseConcMarkSweepGC",
            "-XX:+CMSIncrementalMode",
            "-XX:+CMSClassUnloadingEnabled",
            "-XX:+CMSPermGenSweepingEnabled",
            "-XX:+UseParNewGC",
            "-Xincgc",
            "-XX:+AggressiveOpts",
        };

        var modernOnlyPrefixes = new[]
        {
            "--add-opens",
            "--add-exports",
            "--add-modules",
            "--add-reads",
            "--patch-module",
            "--enable-preview",
            "-XX:+UseZGC",
            "-XX:+UseShenandoahGC",
        };

        foreach (var arg in args)
        {
            var val = arg.ToString()?.Trim() ?? "";
            if (string.IsNullOrEmpty(val)) continue;

            if (javaMajor >= 17)
            {
                if (obsoleteOnModern.Any(o => val.StartsWith(o, StringComparison.OrdinalIgnoreCase)) ||
                    val.StartsWith("-XX:PermSize=", StringComparison.OrdinalIgnoreCase) ||
                    val.StartsWith("-XX:MaxPermSize=", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
            }
            else if (javaMajor <= 11)
            {
                if (modernOnlyPrefixes.Any(p => val.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }
            }

            yield return arg;
        }
    }

    private LauncherConfigData? _cachedConfig;
    private DateTime _configLastRead = DateTime.MinValue;

    private LauncherConfigData? GetConfig()
    {
        var configPath = ZenithPaths.ConfigFilePath;
        if (!File.Exists(configPath)) return null;
        var lastWrite = File.GetLastWriteTimeUtc(configPath);
        if (_cachedConfig != null && _configLastRead >= lastWrite)
            return _cachedConfig;
        try
        {
            var json = File.ReadAllText(configPath);
            _cachedConfig = System.Text.Json.JsonSerializer.Deserialize<LauncherConfigData>(json);
            _configLastRead = lastWrite;
        }
        catch { _cachedConfig = null; }
        return _cachedConfig;
    }

    public event Action<int>? ProgressChanged;
    public event Action<string>? LogReceived;
    public event Action<string>? CurrentFileChanged;

    /// <summary>Raised on a background thread right after the game process started.</summary>
    public event Action<InstanceModel, string>? GameLaunched;

    /// <summary>Raised on a background thread when the game process exits (any exit code).</summary>
    public event Action<InstanceModel, int>? GameExited;

    /// <summary>Raised on a background thread on non-zero exit with crash analysis.</summary>
    public event Action<InstanceModel, int, CrashAnalysis>? CrashDetected;

    // Writes to the per-instance log file can be triggered from several background
    // threads at once (stdout reader, stderr reader, Exited handler). Synchronous
    // File.AppendAllText blocks the AsyncStreamReader worker thread, and under heavy
    // log output the OS pipe buffer (≈4 KB) fills up, deadlocking the child process.
    // Use a concurrent queue flushed by a timer to keep reader threads unblocked.
    private static readonly object LogWriteGate = new();
    private static readonly ConcurrentDictionary<string, ConcurrentQueue<string>> _logQueues = new();
    private static readonly System.Timers.Timer _logFlushTimer = InitLogFlushTimer();

    private static System.Timers.Timer InitLogFlushTimer()
    {
        var timer = new System.Timers.Timer(200) { AutoReset = true, Enabled = true };
        timer.Elapsed += (_, _) => FlushAllLogQueues();
        return timer;
    }

    private static void FlushAllLogQueues()
    {
        foreach (var kvp in _logQueues)
        {
            if (kvp.Value.IsEmpty) continue;
            var lines = new List<string>();
            while (kvp.Value.TryDequeue(out var line)) lines.Add(line);
            if (lines.Count == 0) continue;
            lock (LogWriteGate)
            {
                try { File.AppendAllText(kvp.Key, string.Concat(lines)); }
                catch { /* transient file lock is non-fatal */ }
            }
        }
    }

    private static void SafeAppendLog(string path, string text)
    {
        var queue = _logQueues.GetOrAdd(path, _ => new ConcurrentQueue<string>());
        queue.Enqueue(text);
    }

    private static void SafeWriteLog(string path, string text)
    {
        lock (LogWriteGate)
        {
            try { File.WriteAllText(path, text); }
            catch { }
        }
    }

    private DateTime _lastProgressUpdate = DateTime.MinValue;
    private void ThrottledProgress(int percent)
    {
        var now = DateTime.UtcNow;
        if ((now - _lastProgressUpdate).TotalMilliseconds < 50) return;
        _lastProgressUpdate = now;
        ProgressChanged?.Invoke(percent);
    }

    // CmlLib exposes the currently-downloading file under different property names across
    // versions (FileName / Name). Read it reflectively so we don't hard-depend on one API.
    private static string? TryGetFileProgressName(object e)
    {
        var t = e.GetType();
        foreach (var propName in new[] { "FileName", "Name" })
        {
            var p = t.GetProperty(propName);
            if (p == null || p.PropertyType != typeof(string)) continue;
            try
            {
                var v = p.GetValue(e) as string;
                if (!string.IsNullOrWhiteSpace(v)) return v;
            }
            catch { }
        }
        return null;
    }

    public LaunchService(IInstanceService instanceService)
    {
        _instanceService = instanceService;
    }

    public void KillProcess(string instanceId)
    {
        if (_runningProcesses.TryRemove(instanceId, out var proc) && !proc.HasExited)
        {
            try
            {
                proc.Kill(entireProcessTree: true);
                LogReceived?.Invoke($"Process {instanceId} terminated.");
            }
            catch (Exception ex)
            {
                LogReceived?.Invoke($"Failed to kill process: {ex.Message}");
            }
        }
    }

    public async Task<Process?> LaunchAsync(InstanceModel instance, AccountModel account)
    {
        var config = GetConfig();

        var zenithRoot = ZenithPaths.AppDataDir;

        string basePath = !string.IsNullOrWhiteSpace(instance.Path) ? instance.Path : Path.Combine(ZenithPaths.AppDataDir, "instances", instance.Id);
        Directory.CreateDirectory(basePath);

        var modsDir = Path.Combine(basePath, "mods");
        Directory.CreateDirectory(modsDir);
        Directory.CreateDirectory(Path.Combine(basePath, "config"));
        Directory.CreateDirectory(Path.Combine(basePath, "shaderpacks"));
        Directory.CreateDirectory(Path.Combine(basePath, "resourcepacks"));
        Directory.CreateDirectory(Path.Combine(basePath, "saves"));

        var logsDir = Path.Combine(ZenithPaths.AppDataDir, "logs");
        Directory.CreateDirectory(logsDir);
        var logFilePath = Path.Combine(logsDir, $"instance_{instance.Id}.log");
        SafeWriteLog(logFilePath, $"--- Launching {instance.Name} ({instance.LoaderType} {instance.Version}) ---\n");

        var mcPath = new MinecraftPath(zenithRoot)
        {
            BasePath = basePath
        };
        var launcher = new MinecraftLauncher(mcPath);

        launcher.FileProgressChanged += (s, e) =>
        {
            if (e.TotalTasks > 0)
                ThrottledProgress((int)((double)e.ProgressedTasks / e.TotalTasks * 100));

            var file = TryGetFileProgressName(e);
            if (file != null)
                CurrentFileChanged?.Invoke(file);
        };

        launcher.ByteProgressChanged += (s, e) =>
        {
            if (e.TotalBytes > 0)
                ThrottledProgress((int)((double)e.ProgressedBytes / e.TotalBytes * 100));
        };

        LogReceived?.Invoke($"Preparing {instance.Name} ({instance.LoaderType} {instance.Version})...");

        // Serialize install of the same Minecraft version to avoid file conflicts
        // when launching multiple instances of the same version in parallel.
        var installLock = InstallLocks.GetOrAdd($"{instance.Version}:{instance.LoaderType}:{instance.LoaderVersion}", _ => new SemaphoreSlim(1, 1));
        await installLock.WaitAsync();
        string targetVersion;
        IVersion version;
        try
        {
            await WithManifestRetryAsync(() => launcher.InstallAsync(instance.Version).AsTask());
            targetVersion = instance.Version;

            var reqJavaForInstaller = InferRequiredJavaMajor(instance.Version);
            var resolvedInstallerJava = await ResolveJavaAsync(reqJavaForInstaller, zenithRoot);
            var javaExeToUse = "java";
            if (!string.IsNullOrWhiteSpace(resolvedInstallerJava))
            {
                var consoleExe = Path.Combine(Path.GetDirectoryName(resolvedInstallerJava)!, "java.exe");
                javaExeToUse = File.Exists(consoleExe) ? consoleExe : resolvedInstallerJava;
            }

            if (!instance.LoaderType.Equals("Vanilla", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(resolvedInstallerJava) && !TryFindJava())
                {
                    LogReceived?.Invoke("[ERROR] Java not found on PATH or runtime. Cannot install mod loader.");
                    throw new InvalidOperationException($"Java {reqJavaForInstaller} is required but was not found on PATH or runtime.");
                }
            }

            if (instance.LoaderType.Equals("Forge", StringComparison.OrdinalIgnoreCase))
            {
                targetVersion = await InstallForgeAsync(instance, zenithRoot, javaExeToUse);
            }
            else if (instance.LoaderType.Equals("Fabric", StringComparison.OrdinalIgnoreCase))
            {
                targetVersion = await InstallFabricAsync(instance, zenithRoot);
            }
            else if (instance.LoaderType.Equals("NeoForge", StringComparison.OrdinalIgnoreCase))
            {
                targetVersion = await InstallNeoForgeAsync(instance, zenithRoot, javaExeToUse);
            }
            else if (instance.LoaderType.Equals("Quilt", StringComparison.OrdinalIgnoreCase))
            {
                targetVersion = await InstallQuiltAsync(instance, zenithRoot);
            }
            else if (instance.LoaderType.Equals("OptiFine", StringComparison.OrdinalIgnoreCase))
            {
                targetVersion = await InstallOptiFineAsync(instance, zenithRoot, basePath, javaExeToUse);
            }

            await WithManifestRetryAsync(() => launcher.GetAllVersionsAsync().AsTask());
            version = await WithManifestRetryAsync(() => launcher.GetVersionAsync(targetVersion).AsTask());
        }
        catch (Exception ex)
        {
            LogReceived?.Invoke($"[FATAL] Loader installation failed: {ex.Message}");
            LogReceived?.Invoke($"[FATAL] Stack trace: {ex}");
            SafeAppendLog(logFilePath, $"[FATAL] Loader installation failed: {ex}\n");
            throw;
        }
        finally
        {
            installLock.Release();
        }

        var session = account.Type == AccountType.Offline
            ? MSession.CreateOfflineSession(account.Username)
            : new MSession
            {
                Username = account.Username,
                UUID = account.Uuid,
                AccessToken = account.AccessToken,
                UserType = account.Type == AccountType.Microsoft ? "msa" : "mojang"
            };

        // Ensure authlib-injector.jar exists and is valid for Ely.by accounts
        if (account.Type == AccountType.ElyBy)
        {
            var authlibPath = Path.Combine(ZenithPaths.AppDataDir, "authlib-injector.jar");
            if (!File.Exists(authlibPath) || new FileInfo(authlibPath).Length <= 0)
            {
                LauncherLog.Info("authlib-injector.jar missing or empty, downloading early...");
                try
                {
                    await DownloadAuthlibInjectorAsync(authlibPath).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    LauncherLog.Error($"Early authlib-injector.jar download failed: {ex.Message}");
                    // Final hard check happens right before javaw.exe below.
                }
            }
        }

        var launchOption = new MLaunchOption
        {
            Path = mcPath,
            StartVersion = version,
            Session = session,
            MaximumRamMb = instance.RamMb ?? ResolveDefaultRamMb(config, instance.Version),
            ScreenWidth = instance.GameWidth ?? (config?.GameWidth > 0 ? config.GameWidth : 1280),
            ScreenHeight = instance.GameHeight ?? (config?.GameHeight > 0 ? config.GameHeight : 720),
            FullScreen = instance.IsFullscreen ?? (config?.IsFullscreen ?? false)
        };

        // Use a per-instance natives directory so two instances of the same version
        // (e.g. two vanilla 1.20.1) can run in parallel without fighting over the
        // shared extracted-native files under zenithRoot\versions\<ver>\natives.
        var nativesDir = Path.Combine(basePath, "natives");
        Directory.CreateDirectory(nativesDir);
        launchOption.NativesDirectory = nativesDir;

        // Apply JVM args from config
        var jvmArgs = new List<MArgument>();

        // For offline profiles on 1.16.x, bypass multiplayer session check
        if (account.Type == AccountType.Offline && instance.Version.StartsWith("1.16"))
        {
            jvmArgs.Add(new MArgument("-Dminecraft.api.session.host=http://localhost"));
            jvmArgs.Add(new MArgument("-Dminecraft.api.secure=false"));
        }

        // For offline profiles, allow multiplayer by spoofing launcher brand
        if (account.Type == AccountType.Offline)
        {
            jvmArgs.Add(new MArgument("-Dminecraft.launcher.brand=vanilla"));
            jvmArgs.Add(new MArgument("-Dminecraft.launcher.version=2.0"));
        }

        var minRam = instance.MinRamMb ?? (config?.MinRamMb > 0 ? config.MinRamMb : 1024);
        if (minRam > 0)
        {
            jvmArgs.Add(new MArgument($"-Xms{minRam}m"));
        }

        if (!string.IsNullOrWhiteSpace(instance.JvmArgs))
        {
            var args = instance.JvmArgs.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(a => new MArgument(a))
                .ToArray();
            jvmArgs.AddRange(args);
        }
        else if (config != null && !string.IsNullOrWhiteSpace(config.JvmArgs))
        {
            var args = config.JvmArgs.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(a => new MArgument(a))
                .ToArray();
            jvmArgs.AddRange(args);
        }

        // For Forge/NeoForge 1.18+, add --add-opens to fix InaccessibleObjectException
        if (instance.LoaderType.Equals("Forge", StringComparison.OrdinalIgnoreCase) ||
            instance.LoaderType.Equals("NeoForge", StringComparison.OrdinalIgnoreCase))
        {
            var versionParts = instance.Version.Split('.');
            if (versionParts.Length >= 2)
            {
                if (int.TryParse(versionParts[0], out var major) && int.TryParse(versionParts[1], out var minor) && (major > 1 || (major == 1 && minor >= 18)))
                {
                    jvmArgs.Add(new MArgument("--add-opens=java.base/java.lang.invoke=ALL-UNNAMED"));
                    jvmArgs.Add(new MArgument("--add-opens=java.base/java.lang.reflect=ALL-UNNAMED"));
                    jvmArgs.Add(new MArgument("--add-opens=java.base/java.io=ALL-UNNAMED"));
                    jvmArgs.Add(new MArgument("--add-opens=java.base/java.util=ALL-UNNAMED"));
                }
            }
        }

        if (account.Type == AccountType.ElyBy)
        {
            try
            {
                var authlibPath = Path.Combine(ZenithPaths.AppDataDir, "authlib-injector.jar");

                // Hard requirement: the jar MUST exist on disk with size > 0
                // BEFORE javaw.exe starts, otherwise the JVM dies with
                // "Error opening zip file or JAR manifest missing".
                if (!File.Exists(authlibPath) || new FileInfo(authlibPath).Length <= 0)
                {
                    LauncherLog.Info("authlib-injector.jar missing or empty - downloading now...");
                    await DownloadAuthlibInjectorAsync(authlibPath).ConfigureAwait(false);
                }

                var authlibFile = new FileInfo(authlibPath);
                if (!authlibFile.Exists || authlibFile.Length <= 0)
                    throw new InvalidOperationException(
                        $"authlib-injector.jar is not available at {authlibPath} " +
                        $"(exists={authlibFile.Exists}, size={authlibFile.Length}). Ely.by launch aborted.");

                LauncherLog.Info(
                    $"authlib-injector.jar verified: {authlibPath} ({authlibFile.Length} bytes) - starting game.");
                // Base URL of the Ely.by authlib-injector metadata endpoint.
                // authlib-injector GETs this URL for its metadata JSON; concrete
                // sub-routes like /auth/authenticate only accept POST and would
                // fail with HTTP 405.
                jvmArgs.Add(new MArgument($"-javaagent:{authlibPath}=https://authserver.ely.by/api/authlib-injector"));
            }
            catch
            {
                LauncherLog.Error("Ely.by launch preparation failed - aborting launch (see log above).");
                throw;
            }
        }

        var requiredJava = JavaVersionHelper.InferRequiredJavaMajor(instance.Version);
        if (version.JavaVersion != null)
        {
            var jv = version.JavaVersion.MajorVersion;
            if (!string.IsNullOrEmpty(jv) && int.TryParse(jv, out var parsed))
                requiredJava = parsed;
        }

        // Java resolution
        var javaMode = instance.JavaMode ?? config?.JavaMode ?? "Recommended";
        var customJava = !string.IsNullOrWhiteSpace(instance.CustomJavaPath) ? instance.CustomJavaPath : (config?.CustomJavaPath ?? "");
        if (javaMode == "Custom")
        {
            if (string.IsNullOrWhiteSpace(customJava) || !File.Exists(customJava))
                throw new InvalidOperationException("Custom Java path is not set or file does not exist. Configure it in Settings.");
            launchOption.JavaPath = customJava;
        }
        else if (javaMode == "Recommended")
        {
            LogReceived?.Invoke($"Required Java version: {requiredJava}");
            var found = await ResolveJavaAsync(requiredJava, zenithRoot);
            if (found != null)
            {
                launchOption.JavaPath = found;
                LogReceived?.Invoke($"Using Java: {found}");
            }
        }

        if (string.IsNullOrEmpty(launchOption.JavaPath))
            throw new InvalidOperationException("No suitable Java runtime found. Please install Java or set a custom path in Settings.");

        var targetJavaMajor = requiredJava;
        var safeArgs = targetJavaMajor <= 11 ? LegacySafeJvmArgs : ModernSafeJvmArgs;

        // Get version-specific JVM args (from version.json), filter bad flags
        var versionJvmArgs = version.ConcatInheritedJvmArguments()
            .Where(a => a.Values == null || a.Values.All(v => !v.Contains("sun-misc-unsafe-memory-access")))
            .ToList();

        var combinedJvmArgs = versionJvmArgs
            .Concat(jvmArgs)
            .Concat(safeArgs);

        launchOption.JvmArgumentOverrides = SanitizeJvmArgs(combinedJvmArgs, targetJavaMajor).ToArray();

        // Ensure all required native libraries (.dll) are physically present in the instance's nativesDir
        EnsureNativesExtracted(version, zenithRoot, nativesDir, targetVersion, instance.Version, msg => LogReceived?.Invoke(msg));

        var process = await launcher.BuildProcessAsync(targetVersion, launchOption);

        // Log the full command line for debugging — redact access token for security
        var fullCommandLine = $"\"{process.StartInfo.FileName}\" {process.StartInfo.Arguments}";
        var safeCommandLine = System.Text.RegularExpressions.Regex.Replace(
            fullCommandLine, @"--accessToken\s+\S+", "--accessToken [REDACTED]");
        LogReceived?.Invoke($"[DEBUG] Full command line:");
        LogReceived?.Invoke($"[DEBUG] {safeCommandLine}");
        SafeAppendLog(logFilePath, $"[DEBUG] Full command line:\n[DEBUG] {safeCommandLine}\n");

        process.StartInfo.UseShellExecute = false;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.EnableRaisingEvents = true;

        process.OutputDataReceived += (s, e) => {
            if (e.Data != null) {
                try
                {
                    LogReceived?.Invoke(e.Data);
                    SafeAppendLog(logFilePath, e.Data + "\n");
                }
                catch { }
            }
        };
        process.ErrorDataReceived += (s, e) => {
            if (e.Data != null) {
                try
                {
                    LogReceived?.Invoke(e.Data);
                    SafeAppendLog(logFilePath, "[STDERR] " + e.Data + "\n");
                }
                catch { }
            }
        };

        var launchStartTime = DateTime.UtcNow;
        instance.LastPlayed = DateTime.UtcNow;
        _ = _instanceService.SaveInstanceAsync(instance);

        process.Exited += (s, e) =>
        {
            _runningProcesses.TryRemove(instance.Id, out _);
            Dispatcher.UIThread.Post(() => instance.IsRunning = false);
            var exitCode = 1;
            try { exitCode = process.ExitCode; } catch { }

            var sessionDuration = (DateTime.UtcNow - launchStartTime).TotalSeconds;
            if (sessionDuration > 0)
            {
                instance.PlaytimeSeconds += (long)sessionDuration;
                _ = _instanceService.SaveInstanceAsync(instance);
            }

            try
            {
                LogReceived?.Invoke($"Game {instance.Name} exited with code {exitCode}.");
                SafeAppendLog(logFilePath, $"[DEBUG] Process exited with code {exitCode}\n");
            }
            catch { }

            GameExited?.Invoke(instance, exitCode);

            // Non-zero exit: analyze logs/crash-reports and surface a
            // human-readable explanation window (the view layer subscribes).
            if (exitCode != 0)
            {
                var analysis = CrashAnalyzer.Analyze(exitCode, logFilePath, basePath);
                CrashDetected?.Invoke(instance, exitCode, analysis);
            }
        };

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }
        catch (Exception ex)
        {
            _runningProcesses.TryRemove(instance.Id, out _);
            Dispatcher.UIThread.Post(() => instance.IsRunning = false);
            throw new InvalidOperationException($"Failed to start Minecraft process: {ex.Message}", ex);
        }

        _runningProcesses[instance.Id] = process;
        instance.IsRunning = true;

        GameLaunched?.Invoke(instance, account.Type.ToString());

        return process;
    }

    /// <summary>
    /// Heuristic Java requirement by game version, used when the manifest does
    /// not declare a javaVersion: 26.x+ -> 25, 1.20.5+ -> 21,
    /// 1.17 - 1.20.4 -> 17, anything older -> 8.
    /// </summary>
    private static int InferRequiredJavaMajor(string gameVersion)
    {
        // Leading numeric run ("26.3", "1.20.4", "3D Shareware v1.34" -> "3").
        var chars = new List<char>();
        foreach (var c in gameVersion)
        {
            if (char.IsDigit(c) || c == '.') chars.Add(c);
            else if (chars.Count > 0) break;
        }
        var numeric = new string(chars.ToArray()).TrimEnd('.');

        var parts = numeric.Split('.');
        if (parts.Length == 0 || !int.TryParse(parts[0], out var first))
            return 8;

        // New versioning scheme: 26.x and later -> newest LTS line.
        if (first >= 26) return 25;

        if (first != 1) return 8; // ancient / special one-off versions

        if (parts.Length >= 2 && int.TryParse(parts[1], out var minor))
        {
            if (minor >= 21) return 21;
            if (minor == 20)
            {
                if (parts.Length >= 3 && int.TryParse(parts[2], out var patch) && patch >= 5)
                    return 21;
                return 17;
            }
            if (minor >= 17) return 17;
        }
        return 8;
    }

    private static bool TryFindJava()
    {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("JAVA_HOME"))) return true;
        try
        {
            using var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "java",
                    Arguments = "-version",
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };
            proc.Start();
            return proc.StandardError.ReadToEnd().Contains("version");
        }
        catch { return false; }
    }

    private async Task<string?> ResolveJavaAsync(int major, string zenithRoot)
    {
        var found = await FindLocalJavaAsync(major, zenithRoot);
        if (found != null) return found;

        // Automatic download is a user-visible setting; explicit downloads
        // (the "+" button in Settings) bypass this gate.
        var cfg = GetConfig();
        if (cfg != null && !cfg.AutoManageJava)
        {
            LogReceived?.Invoke($"Java {major} not found locally and automatic Java download is disabled (Settings).");
            return null;
        }

        return await DownloadAdoptiumRuntimeAsync(major, zenithRoot);
    }

    /// <summary>
    /// Explicit one-click install from the Settings "+" button. Runs regardless
    /// of the AutoManageJava toggle. Returns true when the runtime is usable.
    /// </summary>
    public async Task<bool> DownloadJavaRuntimeAsync(int major, string zenithRoot)
    {
        var found = await FindLocalJavaAsync(major, zenithRoot);
        if (found != null)
        {
            LogReceived?.Invoke($"Java {major} is already installed: {found}");
            return true;
        }

        var downloaded = await DownloadAdoptiumRuntimeAsync(major, zenithRoot);
        if (downloaded == null)
            LogReceived?.Invoke($"Failed to install Java {major}. See the log above for details.");
        else
            LogReceived?.Invoke($"Java {major} installed successfully.");
        return downloaded != null;
    }

    /// <summary>
    /// Read-only resolution used by the settings UI so it can always show the
    /// concrete javaw.exe path that Recommended mode would launch for the
    /// selected profile. Mirrors launch-time lookup (managed runtimes first,
    /// then JAVA_HOME and installed JDKs) but never downloads anything.
    /// </summary>
    public async Task<string?> ResolveJavaForDisplayAsync(string? instanceVersion)
    {
        var major = string.IsNullOrWhiteSpace(instanceVersion)
            ? 21 // modern default line when no profile is selected yet
            : InferRequiredJavaMajor(instanceVersion);
        return await FindLocalJavaAsync(major, ZenithPaths.AppDataDir);
    }

    private async Task<string?> FindLocalJavaAsync(int major, string zenithRoot)
    {
        var javaDir = Path.Combine(zenithRoot, "runtime");
        Directory.CreateDirectory(javaDir);

        // Any already-downloaded runtime matching jdk-{major}* or jdk{major}u*?
        // (folder names look like jdk-17.0.20+8-jre or legacy jdk8u452-b05-jre)
        foreach (var pattern in RuntimeDirPatterns(major))
        {
            foreach (var dir in Directory.GetDirectories(javaDir, pattern, SearchOption.TopDirectoryOnly))
            {
                var javaw = Path.Combine(dir, "bin", "javaw.exe");
                if (File.Exists(javaw)) return javaw;
                var javaExe = Path.Combine(dir, "bin", "java.exe");
                if (File.Exists(javaExe)) return javaExe;
            }
        }

        // First check JAVA_HOME
        var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
        if (!string.IsNullOrEmpty(javaHome))
        {
            var jh = Path.Combine(javaHome, "bin", "javaw.exe");
            if (File.Exists(jh) && CheckJavaVersion(jh, major)) return jh;
            jh = Path.Combine(javaHome, "bin", "java.exe");
            if (File.Exists(jh) && CheckJavaVersion(jh, major)) return jh;
        }

        // Check installed JDKs in Program Files with wildcards
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var vendorBases = new[]
        {
            Path.Combine(programFiles, "Eclipse Adoptium"),
            Path.Combine(programFiles, "Java"),
            Path.Combine(programFiles, "Amazon Corretto"),
            Path.Combine(programFiles, "Zulu"),
            Path.Combine(programFiles, "BellSoft"),
            Path.Combine(programFiles, "Microsoft"),
            Path.Combine(programFilesX86, "Eclipse Adoptium"),
            Path.Combine(programFilesX86, "Java")
        };

        var searchPatterns = new[] { $"jdk-{major}*", $"jdk{major}*", $"*jdk*{major}*", $"*jre*{major}*" };
        foreach (var vBase in vendorBases)
        {
            if (!Directory.Exists(vBase)) continue;
            foreach (var pattern in searchPatterns)
            {
                try
                {
                    foreach (var dir in Directory.GetDirectories(vBase, pattern, SearchOption.TopDirectoryOnly))
                    {
                        var jw = Path.Combine(dir, "bin", "javaw.exe");
                        if (File.Exists(jw) && CheckJavaVersion(jw, major)) return jw;
                        var je = Path.Combine(dir, "bin", "java.exe");
                        if (File.Exists(je) && CheckJavaVersion(je, major)) return je;
                    }
                }
                catch { }
            }
        }
        // Check PATH
        try
        {
            using var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "java",
                    Arguments = "-version",
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };
            proc.Start();
            var output = proc.StandardError.ReadToEnd();
            proc.WaitForExit(3000);
            if (output.Contains($"java version \"{major}") || output.Contains($"openjdk version \"{major}"))
            {
                // Find actual path
                using var whereProc = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "where",
                        Arguments = "java",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    }
                };
                whereProc.Start();
                var javaPath = (await whereProc.StandardOutput.ReadLineAsync())?.Trim();
                whereProc.WaitForExit(3000);
                if (!string.IsNullOrEmpty(javaPath) && File.Exists(javaPath))
                    return javaPath;
            }
        }
        catch { }

        return null;
    }

    /// <summary>
    /// Downloads the matching Adoptium JRE into &lt;zenithRoot&gt;/runtime and
    /// returns the path to javaw.exe/java.exe, or null on failure.
    /// </summary>
    private async Task<string?> DownloadAdoptiumRuntimeAsync(int major, string zenithRoot)
    {
        var javaDir = Path.Combine(zenithRoot, "runtime");
        Directory.CreateDirectory(javaDir);

        LogReceived?.Invoke($"Java {major} not found locally. Downloading...");
        try
        {
            var runtimeDir = Path.Combine(javaDir, $"{GetRuntimeFolderName(major)}");
            var existingPatterns = RuntimeDirPatterns(major);
            if (!Directory.Exists(runtimeDir) &&
                !existingPatterns.Any(p => Directory.GetDirectories(javaDir, p, SearchOption.TopDirectoryOnly).Any()))
            {
                // Try GA first, then JDK-image, then EA fallback.
                // IMPORTANT: the API returns .msi installers as separate binary
                // entries - only accept entries whose package link ends in .zip,
                // otherwise extraction fails ("unable to install").
                string? link = null;
                string? expectedSha256 = null;
                var adoptiumUrls = new[]
                {
                    $"https://api.adoptium.net/v3/assets/feature_releases/{major}/ga?release_type=ga&jvm_impl=hotspot&os=windows&arch=x64&image_type=jre",
                    $"https://api.adoptium.net/v3/assets/feature_releases/{major}/ga?release_type=ga&jvm_impl=hotspot&os=windows&arch=x64&image_type=jdk",
                    $"https://api.adoptium.net/v3/assets/feature_releases/{major}/ea?release_type=ea&jvm_impl=hotspot&os=windows&arch=x64&image_type=jre",
                };
                foreach (var url in adoptiumUrls)
                {
                    if (!string.IsNullOrEmpty(link)) break;
                    try
                    {
                        var json = await Http.GetStringAsync(url);
                        using var doc = System.Text.Json.JsonDocument.Parse(json);
                        foreach (var release in doc.RootElement.EnumerateArray())
                        {
                            if (!release.TryGetProperty("binaries", out var binaries)) continue;
                            foreach (var binary in binaries.EnumerateArray())
                            {
                                if (!binary.TryGetProperty("package", out var pkg)) continue;
                                var pkgLink = pkg.TryGetProperty("link", out var l) ? l.GetString() : null;
                                if (string.IsNullOrEmpty(pkgLink) ||
                                    !pkgLink.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                                    continue;
                                link = pkgLink;
                                expectedSha256 = pkg.TryGetProperty("checksum", out var c) ? c.GetString() : null;
                                break;
                            }
                            if (!string.IsNullOrEmpty(link)) break;
                        }
                    }
                    catch { }
                }

                // Some versions (notably Java 16) have no Adoptium GA builds left
                // - fall back to the official temurin{major}-binaries GitHub repo,
                // which still hosts every historical Windows x64 JRE zip.
                if (string.IsNullOrEmpty(link))
                    (link, expectedSha256) = await FindTemurinGitHubReleaseAsync(major);

                if (string.IsNullOrEmpty(link))
                    throw new Exception($"No Adoptium build found for Java {major}");

                var temp = Path.Combine(Path.GetTempPath(), $"adoptium-jdk{major}.zip");
                LogReceived?.Invoke($"Downloading Java {major} from Adoptium...");
                // Resilient: retries, Range-resume and SHA-256 validation against
                // the checksum published by the Adoptium API.
                await ResilientDownloadService.DownloadFileAsync(new[] { link! }, temp, expectedSha256);

                // Overwrite existing files in case a partial/older runtime is present
                System.IO.Compression.ZipFile.ExtractToDirectory(temp, javaDir, overwriteFiles: true);
                File.Delete(temp);
            }

            // Find extracted JDK folder (name varies: jdk-25.0.1+9, jdk8u452-b05-jre, ...)
            var extracted = RuntimeDirPatterns(major)
                .SelectMany(p => Directory.GetDirectories(javaDir, p, SearchOption.TopDirectoryOnly))
                .OrderByDescending(d => d).FirstOrDefault();
            if (extracted == null) extracted = runtimeDir;

            var javaw = Path.Combine(extracted, "bin", "javaw.exe");
            if (File.Exists(javaw)) return javaw;
            var javaExe = Path.Combine(extracted, "bin", "java.exe");
            if (File.Exists(javaExe)) return javaExe;
        }
        catch (Exception ex)
        {
            LogReceived?.Invoke($"Failed to download Java {major}: {ex.Message}");
        }

        return null;
    }

    // Adoptium exposes JDKs as "jdk-{major}*" and JRE builds unpack to "jdk-{major}.0+{build}-jre".
    // Legacy Temurin 8 zips unpack to "jdk8u452-b05-jre" instead - cover both layouts.
    private static string GetRuntimeFolderName(int major) => $"jdk-{major}";

    private static string[] RuntimeDirPatterns(int major) => new[]
    {
        $"jdk-{major}*",
        $"jdk{major}u*",
    };

    /// <summary>
    /// Fallback source when the Adoptium API has no GA build left for a version
    /// (notably Java 16): the official adoptium/temurin{major}-binaries GitHub
    /// repository still hosts every historical Windows x64 JRE zip.
    /// Returns (download url, sha256 or null).
    /// </summary>
    private static async Task<(string? Link, string? Sha256)> FindTemurinGitHubReleaseAsync(int major)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get,
                $"https://api.github.com/repos/adoptium/temurin{major}-binaries/releases?per_page=40");
            req.Headers.UserAgent.ParseAdd("Zenith-Launcher");
            using var resp = await Http.SendAsync(req);
            resp.EnsureSuccessStatusCode();
            var json = await resp.Content.ReadAsStringAsync();
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            foreach (var release in doc.RootElement.EnumerateArray())
            {
                if (!release.TryGetProperty("assets", out var assets)) continue;

                string? zipUrl = null;
                string? zipName = null;
                string? shaUrl = null;
                foreach (var asset in assets.EnumerateArray())
                {
                    var name = asset.TryGetProperty("name", out var n) ? n.GetString() : null;
                    var url = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
                    if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(url)) continue;

                    if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) &&
                        name.IndexOf("jre_x64_windows_hotspot", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        if (zipUrl == null || name.Contains("_jre_", StringComparison.OrdinalIgnoreCase))
                        {
                            zipUrl = url;
                            zipName = name;
                        }
                    }
                    else if (shaUrl == null &&
                             name.EndsWith(".zip.sha256.txt", StringComparison.OrdinalIgnoreCase) &&
                             name.IndexOf("jre_x64_windows_hotspot", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        shaUrl = url;
                    }
                }

                if (string.IsNullOrEmpty(zipUrl)) continue;

                string? sha = null;
                if (!string.IsNullOrEmpty(shaUrl))
                {
                    try
                    {
                        var txt = await Http.GetStringAsync(shaUrl);
                        sha = txt.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)[0].Trim();
                    }
                    catch { }
                }
                return (zipUrl, sha);
            }
        }
        catch { }
        return (null, null);
    }

    private static bool CheckJavaVersion(string javaPath, int requiredMajor)
    {
        try
        {
            using var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = javaPath,
                    Arguments = "-version",
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };
            proc.Start();
            var output = proc.StandardError.ReadToEnd();
            proc.WaitForExit(3000);
            // Java 8 reports the legacy scheme: openjdk version "1.8.0_452".
            var m = System.Text.RegularExpressions.Regex.Match(output, "version \"([^\"]+)\"");
            if (!m.Success) return false;
            var parts = m.Groups[1].Value.Split('.');
            var reported = parts.Length > 1 && parts[0] == "1"
                ? parts.Skip(1).FirstOrDefault()
                : parts.FirstOrDefault();
            return int.TryParse(reported, out var actualMajor) && actualMajor == requiredMajor;
        }
        catch { return false; }
    }

    private Task RunJavaInstallerAsync(string arguments, int timeoutSec = 180) =>
        RunJavaInstallerAsync("java", arguments, timeoutSec);

    private async Task RunJavaInstallerAsync(string javaExe, string arguments, int timeoutSec = 180)
    {
        LogReceived?.Invoke($"Running: {javaExe} {arguments}");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSec));
        var tcs = new TaskCompletionSource<bool>();
        using var proc = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = javaExe,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            },
            EnableRaisingEvents = true
        };

        proc.OutputDataReceived += (s, e) => { if (e.Data != null) LogReceived?.Invoke($"[INSTALLER] {e.Data}"); };
        proc.ErrorDataReceived += (s, e) => { if (e.Data != null) LogReceived?.Invoke($"[INSTALLER ERR] {e.Data}"); };
        proc.Exited += (s, e) => tcs.TrySetResult(true);

        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        using (cts.Token.Register(() => { try { proc.Kill(entireProcessTree: true); } catch { } }))
        {
            await tcs.Task;
        }

        if (!proc.HasExited) proc.WaitForExit(5000);
        if (proc.ExitCode != 0)
            throw new Exception($"Java installer exited with code {proc.ExitCode}. See logs for details.");
    }

    private async Task<string> InstallForgeAsync(InstanceModel instance, string zenithRoot, string javaExe = "java")
    {
        LogReceived?.Invoke("Checking Forge version info...");

        string forgeVer;
        if (!string.IsNullOrEmpty(instance.LoaderVersion) && instance.LoaderVersion != "Latest (Auto)")
        {
            forgeVer = instance.LoaderVersion;
        }
        else
        {
            var candidates = new[]
            {
                "https://files.minecraftforge.net/net/minecraftforge/forge/maven-metadata.json",
                "https://bmclapi2.bangbang93.com/maven/net/minecraftforge/forge/maven-metadata.json",
                "https://mirror.sjtu.edu.cn/bmclapi/net/minecraftforge/forge/maven-metadata.json",
                "https://maven.minecraftforge.net/net/minecraftforge/forge/maven-metadata.json",
                $"https://bmclapi2.bangbang93.com/forge/minecraft/{instance.Version}"
            };

            var forgeListRes = await WebFallback.GetStringFirstAsync(candidates);
            if (forgeListRes is null)
                throw new Exception($"Failed to fetch Forge version list for {instance.Version} (all mirrors failed).");

            forgeVer = ResolveForgeBuildFromList(forgeListRes, instance.Version)
                       ?? throw new Exception("No Forge versions available for " + instance.Version);
        }

        var forgeFullVer = $"{instance.Version}-{forgeVer}";
        var targetVersion = $"{instance.Version}-forge-{forgeVer}";

        var targetJson = Path.Combine(zenithRoot, "versions", targetVersion, $"{targetVersion}.json");
        if (!File.Exists(targetJson))
        {
            var profilesJsonPath = Path.Combine(zenithRoot, "launcher_profiles.json");
            if (!File.Exists(profilesJsonPath)) File.WriteAllText(profilesJsonPath, "{\n  \"profiles\": {}\n}");

            LogReceived?.Invoke($"Downloading Forge installer {forgeVer}...");
            var tempInstaller = Path.Combine(Path.GetTempPath(), $"forge-{forgeFullVer}-installer.jar");
            await ResilientDownloadService.DownloadFileAsync(new[]
            {
                $"https://maven.minecraftforge.net/net/minecraftforge/forge/{forgeFullVer}/forge-{forgeFullVer}-installer.jar",
                $"https://files.minecraftforge.net/maven/net/minecraftforge/forge/{forgeFullVer}/forge-{forgeFullVer}-installer.jar",
                $"https://bmclapi2.bangbang93.com/maven/net/minecraftforge/forge/{forgeFullVer}/forge-{forgeFullVer}-installer.jar",
                $"https://mirror.sjtu.edu.cn/bmclapi/net/minecraftforge/forge/{forgeFullVer}/forge-{forgeFullVer}-installer.jar"
            }, tempInstaller);

            LogReceived?.Invoke("Installing Forge...");
            await RunJavaInstallerAsync(javaExe, $"-jar \"{tempInstaller}\" --installClient \"{zenithRoot}\"");

            try { File.Delete(tempInstaller); } catch { }
        }

        LogReceived?.Invoke($"Validating {targetVersion} files...");
        var launcher = new MinecraftLauncher(new MinecraftPath(zenithRoot) { BasePath = zenithRoot });
        await WithManifestRetryAsync(() => launcher.InstallAsync(targetVersion).AsTask());
        return targetVersion;
    }

    private static int ResolveDefaultRamMb(LauncherConfigData? config, string version)
    {
        if (config?.RamMb > 0) return config.RamMb;
        return version.StartsWith("1.7") || version.StartsWith("1.8") ? 2048 : 4096;
    }

    private static void EnsureNativesExtracted(
        IVersion version,
        string zenithRoot,
        string nativesDir,
        string targetVersion,
        string gameVersion,
        Action<string>? log = null)
    {
        try
        {
            Directory.CreateDirectory(nativesDir);

            // 1. Copy from existing versions/.../natives directories if available
            var candidateNativeDirs = new[]
            {
                Path.Combine(zenithRoot, "versions", targetVersion, "natives"),
                Path.Combine(zenithRoot, "versions", gameVersion, "natives")
            };

            foreach (var candidateDir in candidateNativeDirs)
            {
                if (Directory.Exists(candidateDir))
                {
                    foreach (var dll in Directory.EnumerateFiles(candidateDir, "*.dll"))
                    {
                        var dest = Path.Combine(nativesDir, Path.GetFileName(dll));
                        if (!File.Exists(dest) || new FileInfo(dest).Length == 0)
                        {
                            try { File.Copy(dll, dest, overwrite: true); } catch { }
                        }
                    }
                }
            }

            // 2. Extract from version libraries classifiers and artifacts
            if (version.Libraries != null)
            {
                foreach (var lib in version.Libraries)
                {
                    // Check classifiers (e.g. natives-windows)
                    if (lib.Classifiers != null)
                    {
                        foreach (var kv in lib.Classifiers)
                        {
                            var key = kv.Key?.ToLowerInvariant() ?? "";
                            var path = kv.Value?.Path;
                            if (string.IsNullOrEmpty(path)) continue;

                            if (key.Contains("windows") || path.Contains("natives-windows") || path.Contains("natives_windows"))
                            {
                                var jarPath = Path.Combine(zenithRoot, "libraries", path.Replace('/', Path.DirectorySeparatorChar));
                                ExtractDllsFromJar(jarPath, nativesDir);
                            }
                        }
                    }

                    // Check artifact path
                    if (lib.Artifact?.Path != null)
                    {
                        var path = lib.Artifact.Path;
                        if (path.Contains("natives-windows") || path.Contains("natives_windows"))
                        {
                            var jarPath = Path.Combine(zenithRoot, "libraries", path.Replace('/', Path.DirectorySeparatorChar));
                            ExtractDllsFromJar(jarPath, nativesDir);
                        }
                    }
                }
            }

            // 3. Heuristic fallback for legacy Minecraft (1.12.2, 1.7.10, etc.):
            // If lwjgl.dll / lwjgl64.dll is still missing, search for lwjgl-platform native jars in libraries
            var hasLwjgl = File.Exists(Path.Combine(nativesDir, "lwjgl64.dll")) || File.Exists(Path.Combine(nativesDir, "lwjgl.dll"));
            if (!hasLwjgl)
            {
                var librariesDir = Path.Combine(zenithRoot, "libraries");
                if (Directory.Exists(librariesDir))
                {
                    var lwjglJars = Directory.EnumerateFiles(librariesDir, "*natives-windows*.jar", SearchOption.AllDirectories);
                    foreach (var jar in lwjglJars)
                    {
                        ExtractDllsFromJar(jar, nativesDir);
                    }
                }
            }

            log?.Invoke($"Natives verified in: {nativesDir}");
        }
        catch (Exception ex)
        {
            log?.Invoke($"[WARN] EnsureNativesExtracted encountered an issue: {ex.Message}");
        }
    }

    private static void ExtractDllsFromJar(string jarPath, string destDir)
    {
        if (!File.Exists(jarPath)) return;
        try
        {
            using var archive = ZipFile.OpenRead(jarPath);
            foreach (var entry in archive.Entries)
            {
                if (entry.FullName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                {
                    var fileName = Path.GetFileName(entry.FullName);
                    if (string.IsNullOrEmpty(fileName)) continue;
                    var destFile = Path.Combine(destDir, fileName);
                    if (!File.Exists(destFile) || new FileInfo(destFile).Length == 0)
                    {
                        entry.ExtractToFile(destFile, overwrite: true);
                    }
                }
            }
        }
        catch { }
    }

    private static string? ResolveForgeBuildFromList(string json, string gameVersion)
    {
        var prefix = gameVersion + "-";
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);

            // files.minecraftforge.net / BMCLAPI shape: game-keyed object.
            if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object &&
                doc.RootElement.TryGetProperty(gameVersion, out var gameArr) &&
                gameArr.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                var builds = gameArr.EnumerateArray()
                    .Select(e =>
                    {
                        try { return e.GetString(); }
                        catch { return null; }
                    })
                    .Where(s => s is not null && s.StartsWith(prefix) && !s.Contains("installer") && !s.Contains("universal"))
                    .Select(s => s![prefix.Length..])
                    .ToList();
                if (builds.Count > 0)
                    return builds.OrderByDescending(s => s, StringComparer.OrdinalIgnoreCase).First();
            }

            // Legacy maven.minecraftforge.net shape: { "versions": ["1.20.1-47.1.0", ...] }
            if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("versions", out var versions))
            {
                var builds = versions.EnumerateArray()
                    .Select(e =>
                    {
                        try { return e.GetString(); }
                        catch { return null; }
                    })
                    .Where(s => s is not null && s.StartsWith(prefix) && !s.Contains("installer") && !s.Contains("universal"))
                    .Select(s => s![prefix.Length..])
                    .ToList();
                if (builds.Count > 0)
                    return builds.OrderByDescending(s => s, StringComparer.OrdinalIgnoreCase).First();
            }

            var perGameBuilds = new List<string>();
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                try
                {
                    string? v;
                    if (el.ValueKind == System.Text.Json.JsonValueKind.String) v = el.GetString();
                    else if (el.TryGetProperty("version", out var pv)) v = pv.GetString();
                    else continue;

                    if (v is not null && v.StartsWith(prefix) && !v.Contains("installer") && !v.Contains("universal"))
                        perGameBuilds.Add(v[prefix.Length..]);
                }
                catch { }
            }
            if (perGameBuilds.Count > 0)
                return perGameBuilds.OrderByDescending(s => s, StringComparer.OrdinalIgnoreCase).First();
        }
        catch { }
        return null;
    }

    private static string? ResolveNeoForgeBuildFromList(string text, string gameVersion)
    {
        var versions = ModLoaderService.ParseMavenVersionList(text);
        if (versions is null || versions.Count == 0) return null;

        var target = ModLoaderService.ParseNeoForgeTarget(gameVersion);
        if (target != null)
        {
            var prefix = $"{target.Value.Major}.{target.Value.Minor}.";
            var matching = versions.Where(v => v.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();
            if (matching.Count > 0)
            {
                matching.Sort((a, b) => ModLoaderService.CompareBuildTokens(b, a));
                return matching[0];
            }
        }

        // Fallback prefix matching
        var legacyPrefix = (gameVersion.StartsWith("1.") ? gameVersion[2..] : gameVersion) + ".";
        var legacyMatching = versions.Where(v => v.StartsWith(legacyPrefix, StringComparison.OrdinalIgnoreCase) || v.StartsWith(gameVersion, StringComparison.OrdinalIgnoreCase)).ToList();
        if (legacyMatching.Count > 0)
        {
            legacyMatching.Sort((a, b) => ModLoaderService.CompareBuildTokens(b, a));
            return legacyMatching[0];
        }

        // NeoForge purged the 1.20.1 era (20.1.x) from all reachable mirrors; pin the
        // community-standard last 1.20.1 release so the most-modded MC version stays installable.
        if (gameVersion.Equals("1.20.1", StringComparison.OrdinalIgnoreCase))
            return "20.1.63";
        return null;
    }

    private static async Task<string?> FetchFabricProfileJsonAsync(string gameVersion, string loaderVer)
    {
        return await WebFallback.GetStringFirstAsync(
            $"https://meta.fabricmc.net/v2/versions/loader/{gameVersion}/{loaderVer}/profile/json",
            $"https://bmclapi2.bangbang93.com/fabric-meta/v2/versions/loader/{gameVersion}/{loaderVer}/profile/json").ConfigureAwait(false);
    }

    private static async Task<string?> FetchFabricLoaderListAsync(string gameVersion)
    {
        return await WebFallback.GetStringFirstAsync(
            $"https://meta.fabricmc.net/v2/versions/loader/{gameVersion}",
            $"https://bmclapi2.bangbang93.com/fabric-meta/v2/versions/loader/{gameVersion}").ConfigureAwait(false);
    }

    private static async Task<string?> FetchQuiltProfileJsonAsync(string gameVersion, string loaderVer)
    {
        return await WebFallback.GetStringFirstAsync(
            $"https://meta.quiltmc.org/v3/versions/loader/{gameVersion}/{loaderVer}/profile/json",
            $"https://meta.quiltmc.org/v2/versions/loader/{gameVersion}/{loaderVer}/profile/json",
            $"https://bmclapi2.bangbang93.com/quilt-meta/v3/versions/loader/{gameVersion}/{loaderVer}/profile/json").ConfigureAwait(false);
    }

    private static async Task<string?> FetchQuiltLoaderListAsync(string gameVersion)
    {
        return await WebFallback.GetStringFirstAsync(
            $"https://meta.quiltmc.org/v3/versions/loader/{gameVersion}",
            $"https://meta.quiltmc.org/v2/versions/loader/{gameVersion}",
            $"https://bmclapi2.bangbang93.com/quilt-meta/v3/versions/loader/{gameVersion}").ConfigureAwait(false);
    }

    private async Task<string> InstallFabricAsync(InstanceModel instance, string zenithRoot)
    {
        LogReceived?.Invoke("Fetching Fabric Loader profile...");

        string loaderVer;
        if (!string.IsNullOrEmpty(instance.LoaderVersion) && instance.LoaderVersion != "Latest (Auto)")
        {
            loaderVer = instance.LoaderVersion;
        }
        else
        {
            var metaRes = await FetchFabricLoaderListAsync(instance.Version)
                          ?? throw new Exception($"Failed to fetch Fabric loader list for {instance.Version} (all mirrors failed).");
            using var doc = System.Text.Json.JsonDocument.Parse(metaRes);
            if (doc.RootElement.GetArrayLength() == 0)
                throw new Exception("No Fabric versions available for " + instance.Version);
            loaderVer = doc.RootElement[0].GetProperty("loader").GetProperty("version").GetString()!;
            if (string.IsNullOrEmpty(loaderVer))
                throw new Exception("Failed to resolve Fabric loader version.");
        }

        var profileJson = await FetchFabricProfileJsonAsync(instance.Version, loaderVer)
                          ?? throw new Exception($"Failed to fetch Fabric profile json for {loaderVer} (all mirrors failed).");
        var targetVersion = $"fabric-loader-{loaderVer}-{instance.Version}";
        var verDir = Path.Combine(zenithRoot, "versions", targetVersion);
        Directory.CreateDirectory(verDir);
        await File.WriteAllTextAsync(Path.Combine(verDir, $"{targetVersion}.json"), profileJson);
        var launcher = new MinecraftLauncher(new MinecraftPath(zenithRoot) { BasePath = zenithRoot });
        await WithManifestRetryAsync(() => launcher.InstallAsync(targetVersion).AsTask());
        return targetVersion;
    }

    private async Task<string> InstallNeoForgeAsync(InstanceModel instance, string zenithRoot, string javaExe = "java")
    {
        LogReceived?.Invoke("Checking NeoForge version info...");

        string matchedVer;
        if (!string.IsNullOrEmpty(instance.LoaderVersion) && instance.LoaderVersion != "Latest (Auto)")
        {
            matchedVer = instance.LoaderVersion;
        }
        else
        {
            var neoRes = await WebFallback.GetStringFirstAsync(
                "https://maven.neoforged.net/api/maven/versions/releases/net/neoforged/neoforge",
                "https://maven.neoforged.net/api/maven/versions/net/neoforged/neoforge")
              ?? throw new Exception("Failed to fetch NeoForge versions (all mirrors failed).");

            matchedVer = ResolveNeoForgeBuildFromList(neoRes, instance.Version)
                         ?? throw new Exception("No matching NeoForge version for " + instance.Version);
        }

        var targetVersion = $"neoforge-{matchedVer}";
        var targetJson = Path.Combine(zenithRoot, "versions", targetVersion, $"{targetVersion}.json");
        if (!File.Exists(targetJson))
        {
            var profilesJsonPath = Path.Combine(zenithRoot, "launcher_profiles.json");
            if (!File.Exists(profilesJsonPath)) File.WriteAllText(profilesJsonPath, "{\n  \"profiles\": {}\n}");

            LogReceived?.Invoke($"Downloading NeoForge installer {matchedVer}...");
            var tempInstaller = Path.Combine(Path.GetTempPath(), $"neoforge-{matchedVer}-installer.jar");
            await ResilientDownloadService.DownloadFileAsync(new[]
            {
                $"https://maven.neoforged.net/releases/net/neoforged/neoforge/{matchedVer}/neoforge-{matchedVer}-installer.jar",
                $"https://bmclapi2.bangbang93.com/maven/net/neoforged/neoforge/{matchedVer}/neoforge-{matchedVer}-installer.jar",
                $"https://mirror.sjtu.edu.cn/bmclapi/net/neoforged/neoforge/{matchedVer}/neoforge-{matchedVer}-installer.jar",
                $"https://web.archive.org/web/2id_/https://maven.neoforged.net/releases/net/neoforged/neoforge/{matchedVer}/neoforge-{matchedVer}-installer.jar"
            }, tempInstaller);

            LogReceived?.Invoke("Running NeoForge Installer...");
            await RunJavaInstallerAsync(javaExe, $"-jar \"{tempInstaller}\" --installClient \"{zenithRoot}\"");

            try { File.Delete(tempInstaller); } catch { }
        }

        LogReceived?.Invoke($"Validating {targetVersion} files...");
        var launcher = new MinecraftLauncher(new MinecraftPath(zenithRoot) { BasePath = zenithRoot });
        await WithManifestRetryAsync(() => launcher.InstallAsync(targetVersion).AsTask());
        return targetVersion;
    }

    private async Task<string> InstallQuiltAsync(InstanceModel instance, string zenithRoot)
    {
        LogReceived?.Invoke("Fetching Quilt Loader profile...");

        string loaderVer;
        if (!string.IsNullOrEmpty(instance.LoaderVersion) && instance.LoaderVersion != "Latest (Auto)")
        {
            loaderVer = instance.LoaderVersion;
        }
        else
        {
            var metaRes = await FetchQuiltLoaderListAsync(instance.Version)
                          ?? throw new Exception($"Failed to fetch Quilt loader list for {instance.Version} (all mirrors failed).");
            using var doc = System.Text.Json.JsonDocument.Parse(metaRes);
            if (doc.RootElement.GetArrayLength() == 0)
                throw new Exception("No Quilt versions available for " + instance.Version);
            loaderVer = doc.RootElement[0].GetProperty("loader").GetProperty("version").GetString()!;
            if (string.IsNullOrEmpty(loaderVer))
                throw new Exception("Failed to resolve Quilt loader version.");
        }

        var profileJson = await FetchQuiltProfileJsonAsync(instance.Version, loaderVer)
                          ?? throw new Exception($"Failed to fetch Quilt profile json for {loaderVer} (all mirrors failed).");
        var targetVersion = $"quilt-loader-{loaderVer}-{instance.Version}";
        var verDir = Path.Combine(zenithRoot, "versions", targetVersion);
        Directory.CreateDirectory(verDir);
        await File.WriteAllTextAsync(Path.Combine(verDir, $"{targetVersion}.json"), profileJson);
        var launcher = new MinecraftLauncher(new MinecraftPath(zenithRoot) { BasePath = zenithRoot });
        await WithManifestRetryAsync(() => launcher.InstallAsync(targetVersion).AsTask());
        return targetVersion;
    }

    private async Task<string> InstallOptiFineAsync(InstanceModel instance, string zenithRoot, string basePath, string javaExe = "java")
    {
        LogReceived?.Invoke("Fetching OptiFine metadata...");

        string type, patch;
        if (!string.IsNullOrEmpty(instance.LoaderVersion) && instance.LoaderVersion != "Latest (Auto)")
        {
            var parts = instance.LoaderVersion.Split('_', 2);
            if (parts.Length == 2)
            {
                type = parts[0];
                patch = parts[1];
            }
            else
            {
                var ofRes = await WebFallback.GetStringFirstAsync(
                    $"https://bmclapi2.bangbang93.com/optifine/{instance.Version}",
                    $"https://bmclapi.bangbang93.com/optifine/{instance.Version}");
                if (ofRes is null)
                    throw new Exception("Failed to fetch OptiFine versions for " + instance.Version);
                using var doc = System.Text.Json.JsonDocument.Parse(ofRes);
                if (doc.RootElement.GetArrayLength() == 0)
                    throw new Exception("No OptiFine versions available for " + instance.Version);
                type = doc.RootElement[0].GetProperty("type").GetString()!;
                patch = doc.RootElement[0].GetProperty("patch").GetString()!;
            }
        }
        else
        {
            var ofRes = await WebFallback.GetStringFirstAsync(
                $"https://bmclapi2.bangbang93.com/optifine/{instance.Version}",
                $"https://bmclapi.bangbang93.com/optifine/{instance.Version}");
            if (ofRes is null)
                throw new Exception("Failed to fetch OptiFine versions for " + instance.Version);
            using var doc = System.Text.Json.JsonDocument.Parse(ofRes);
            if (doc.RootElement.GetArrayLength() == 0)
                throw new Exception("No OptiFine versions available for " + instance.Version);
            type = doc.RootElement[0].GetProperty("type").GetString()!;
            patch = doc.RootElement[0].GetProperty("patch").GetString()!;
        }

        var ofEdition = $"{instance.Version}_{type}_{patch}";
        var targetVersion = $"{instance.Version}-OptiFine_{type}_{patch}";

        var verDir = Path.Combine(zenithRoot, "versions", targetVersion);
        Directory.CreateDirectory(verDir);
        var targetJson = Path.Combine(verDir, $"{targetVersion}.json");

        if (!File.Exists(targetJson))
        {
            LogReceived?.Invoke($"Downloading OptiFine {type} {patch}...");
            var ofUrl = $"https://bmclapi2.bangbang93.com/optifine/{instance.Version}/{type}/{patch}";
            var tempInstaller = Path.Combine(Path.GetTempPath(), $"OptiFine_{ofEdition}.jar");
            await ResilientDownloadService.DownloadFileAsync(new[]
            {
                ofUrl,
                $"https://bmclapi.bangbang93.com/optifine/{instance.Version}/{type}/{patch}"
            }, tempInstaller);

            var vanillaJar = Path.Combine(zenithRoot, "versions", instance.Version, $"{instance.Version}.jar");
            if (!File.Exists(vanillaJar))
            {
                var launcher = new MinecraftLauncher(new MinecraftPath(zenithRoot) { BasePath = zenithRoot });
                await WithManifestRetryAsync(() => launcher.InstallAsync(instance.Version).AsTask());
            }

            // Place patched jars inside the standard .zenith/libraries Maven layout
            var mavenLibDir = Path.Combine(zenithRoot, "libraries");
            var ofLibDir = Path.Combine(mavenLibDir, "optifine", "OptiFine", ofEdition);
            Directory.CreateDirectory(ofLibDir);
            var destOptifineJar = Path.Combine(ofLibDir, $"OptiFine-{ofEdition}.jar");

            LogReceived?.Invoke("Patching Minecraft with OptiFine (headless)...");
            await RunJavaInstallerAsync(javaExe, $"-cp \"{tempInstaller}\" optifine.Patcher \"{vanillaJar}\" \"{tempInstaller}\" \"{destOptifineJar}\"");

            string wrapperVersion = "2.3";
            string? versionJson = null;

            using (var archive = ZipFile.OpenRead(tempInstaller))
            {
                foreach (var entry in archive.Entries)
                {
                    if (entry.Name.StartsWith("launchwrapper-of-") && entry.Name.EndsWith(".jar"))
                    {
                        wrapperVersion = entry.Name.Replace("launchwrapper-of-", "").Replace(".jar", "");
                        var wrapperDir = Path.Combine(mavenLibDir, "optifine", "launchwrapper-of", wrapperVersion);
                        Directory.CreateDirectory(wrapperDir);
                        var wrapperPath = Path.Combine(wrapperDir, entry.Name);
                        if (!File.Exists(wrapperPath))
                            entry.ExtractToFile(wrapperPath, true);
                    }
                    else if (entry.Name == "launch.json")
                    {
                        using var stream = entry.Open();
                        using var reader = new StreamReader(stream);
                        versionJson = await reader.ReadToEndAsync();
                    }
                }
            }

            if (string.IsNullOrEmpty(versionJson))
            {
                versionJson = @"{
  ""id"": """ + targetVersion + @""",
  ""inheritsFrom"": """ + instance.Version + @""",
  ""type"": ""release"",
  ""mainClass"": ""net.minecraft.launchwrapper.Launch"",
  ""arguments"": {
    ""game"": [ ""--tweakClass"", ""optifine.OptiFineTweaker"" ]
  },
  ""libraries"": [
    { ""name"": ""optifine:OptiFine:" + ofEdition + @""" },
    { ""name"": ""optifine:launchwrapper-of:" + wrapperVersion + @""" }
  ]
}";
            }

            await File.WriteAllTextAsync(targetJson, versionJson);
            try { File.Delete(tempInstaller); } catch { }
        }

        LogReceived?.Invoke($"Validating {targetVersion} files...");
        var finalLauncher = new MinecraftLauncher(new MinecraftPath(zenithRoot) { BasePath = zenithRoot });
        await WithManifestRetryAsync(() => finalLauncher.InstallAsync(targetVersion).AsTask());
        return targetVersion;
    }

    private static async Task DownloadAuthlibInjectorAsync(string destPath)
    {
        // Try metadata mirrors first — they always point to the latest release.
        var metaUrls = new[]
        {
            "https://authlib-injector.yushi.moe/artifact/latest.json",
            "https://bmclapi2.bangbang93.com/mirrors/authlib-injector/artifact/latest.json"
        };

        foreach (var metaUrl in metaUrls)
        {
            try
            {
                LauncherLog.Info($"Fetching authlib-injector metadata: {metaUrl}");
                var json = await Http.GetStringAsync(metaUrl).ConfigureAwait(false);

                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                string? dlUrl = null;
                if (root.TryGetProperty("download_url", out var du) && du.ValueKind == JsonValueKind.String)
                    dlUrl = du.GetString();

                // Legacy shape fallback ("urls": [ ... ]).
                if (string.IsNullOrWhiteSpace(dlUrl) &&
                    root.TryGetProperty("urls", out var urlsEl) && urlsEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var u in urlsEl.EnumerateArray())
                    {
                        var s = u.GetString();
                        if (!string.IsNullOrWhiteSpace(s))
                        {
                            dlUrl = s;
                            break;
                        }
                    }
                }

                if (string.IsNullOrWhiteSpace(dlUrl))
                {
                    LauncherLog.Error("authlib-injector metadata contains no download_url.");
                    continue;
                }

                if (await TryDownloadAuthlibJarAsync(dlUrl!, destPath).ConfigureAwait(false))
                    return;
            }
            catch (Exception ex)
            {
                LauncherLog.Error($"Metadata source failed ({metaUrl}): {ex.Message}");
            }
        }

        // Fallback — direct GitHub release asset for the latest known version.
        var directUrls = new[]
        {
            "https://github.com/yushijinhun/authlib-injector/releases/download/v1.2.8/authlib-injector-1.2.8.jar",
            "https://github.com/yawk/authlib-injector/releases/download/v1.2.8/authlib-injector-1.2.8.jar"
        };

        foreach (var url in directUrls)
        {
            if (await TryDownloadAuthlibJarAsync(url, destPath).ConfigureAwait(false))
                return;
        }

        throw new InvalidOperationException(
            "All authlib-injector download sources failed (metadata mirrors, GitHub v1.2.8).");
    }

    private static async Task<bool> TryDownloadAuthlibJarAsync(string url, string destPath)
    {
        try
        {
            LauncherLog.Info($"Downloading {url} ...");

            // Resilient path: retries + Range-resume keep flaky connections from
            // producing half-written jars.
            await ResilientDownloadService.DownloadFileAsync(new[] { url }, destPath).ConfigureAwait(false);

            // Real jar is ~340 KB; anything smaller is an error page or junk.
            var fi = new FileInfo(destPath);
            if (!fi.Exists || fi.Length < 51200)
            {
                LauncherLog.Error($"Rejected {url}: got {(fi.Exists ? fi.Length : 0)} bytes (too small for the jar).");
                try { File.Delete(destPath); } catch { }
                return false;
            }

            LauncherLog.Info($"authlib-injector.jar saved ({fi.Length} bytes).");
            return true;
        }
        catch (Exception ex)
        {
            LauncherLog.Error($"Download failed ({url}): {ex.Message}");
            return false;
        }
    }
}
