using System;
using System.Diagnostics;
using System.Threading.Tasks;
using CustomMcLauncher.Models;

namespace CustomMcLauncher.Services;

public interface ILaunchService
{
event Action<int>? ProgressChanged;
    event Action<string>? CurrentFileChanged;
    event Action<string>? LogReceived;
    /// <summary>Raised after the game process started (instance, account type label).</summary>
    event Action<InstanceModel, string>? GameLaunched;
    /// <summary>Raised when the game process exits (any exit code).</summary>
    event Action<InstanceModel, int>? GameExited;
Task<Process?> LaunchAsync(InstanceModel instance, AccountModel account, string? extraGameArgs = null);
    void KillProcess(string instanceId);
    /// <summary>Explicit one-click Java install (Settings "+" button). Bypasses AutoManageJava.</summary>
    Task<bool> DownloadJavaRuntimeAsync(int major, string zenithRoot, bool forceRedownload = false, IProgress<double>? progress = null);
    /// <summary>Read-only javaw.exe lookup for UI display (Recommended mode, no downloads).</summary>
    Task<string?> ResolveJavaForDisplayAsync(string? instanceVersion);
}
