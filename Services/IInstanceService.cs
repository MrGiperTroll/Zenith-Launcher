using System.Collections.Generic;
using System.Threading.Tasks;
using CustomMcLauncher.Models;

namespace CustomMcLauncher.Services;

public interface IInstanceService
{
    IReadOnlyList<InstanceModel> GetInstances();
    InstanceModel? GetSelectedInstance();
    void SetSelectedInstance(string instanceId);
    string? SelectedInstanceId { get; }
    Task SaveInstanceAsync(InstanceModel instance);
    Task<InstanceModel?> CreateInstanceAsync(string name, string version, string loaderType = "Vanilla", string loaderBuild = "");
    void DeleteInstance(string instanceId);
    Task<(bool ok, string? error)> RenameInstanceAsync(InstanceModel instance, string newName);
    void ReorderInstances(string movedId, string targetId, bool dropAfter);
}

