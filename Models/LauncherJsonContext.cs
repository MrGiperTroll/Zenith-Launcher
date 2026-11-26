using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ZenithLauncher.Models;

public sealed class CachedVersionEntry
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string ReleaseTime { get; set; } = string.Empty;
}

public sealed class InstancesStore
{
    public List<InstanceModel> Instances { get; set; } = new();
    public string? SelectedInstanceId { get; set; }
}

public sealed class AccountsStore
{
    public List<AccountModel> Accounts { get; set; } = new();
    public string? ActiveAccountId { get; set; }
}

[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    WriteIndented = true)]
[JsonSerializable(typeof(LauncherConfigData))]
[JsonSerializable(typeof(InstanceModel))]
[JsonSerializable(typeof(InstancesStore))]
[JsonSerializable(typeof(AccountModel))]
[JsonSerializable(typeof(List<AccountModel>))]
[JsonSerializable(typeof(AccountsStore))]
[JsonSerializable(typeof(List<CachedVersionEntry>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(SnowAccumulationData))]
[JsonSerializable(typeof(WeatherAccumulationData))]
[JsonSerializable(typeof(SettledLeaf))]
[JsonSerializable(typeof(List<SettledLeaf>))]
[JsonSerializable(typeof(Dictionary<string, List<SettledLeaf>>))]
[JsonSerializable(typeof(Dictionary<string, Dictionary<string, List<SettledLeaf>>>))]
[JsonSerializable(typeof(Dictionary<string, Dictionary<string, Dictionary<string, List<SettledLeaf>>>>))]
[JsonSerializable(typeof(Dictionary<string, Dictionary<string, Dictionary<string, byte[]>>>))]
[JsonSerializable(typeof(Dictionary<string, Dictionary<string, byte[]>>))]
[JsonSerializable(typeof(Dictionary<string, byte[]>))]
internal partial class LauncherJsonContext : JsonSerializerContext
{
    private static readonly LauncherJsonContext CompactContext = new(new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    });

    public static LauncherJsonContext Compact => CompactContext;
}
