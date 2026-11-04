namespace CustomMcLauncher.Models;

public enum ContentType
{
    Mod,
    ResourcePack,
    Shader,
    DataPack,
    World
}

public enum ContentSource
{
    All,
    Modrinth,
    CurseForge
}

public enum ContentSortOption
{
    Relevance,
    Downloads,
    Newest,
    RecentlyUpdated
}

public static class ContentTypeExtensions
{
    public static string DisplayName(this ContentType type) => type switch
    {
        ContentType.Mod => "Mods",
        ContentType.ResourcePack => "Resource Packs",
        ContentType.Shader => "Shaders",
        ContentType.DataPack => "Data Packs",
        ContentType.World => "Worlds",
        _ => type.ToString()
    };

    public static string ModrinthProjectType(this ContentType type) => type switch
    {
        ContentType.Mod => "mod",
        ContentType.ResourcePack => "resourcepack",
        ContentType.Shader => "shader",
        ContentType.DataPack => "datapack",
        ContentType.World => "world",
        _ => "mod"
    };

    public static string InstallFolder(this ContentType type, string instancePath, string? selectedWorld = null) => type switch
    {
        ContentType.Mod => System.IO.Path.Combine(instancePath, "mods"),
        ContentType.ResourcePack => System.IO.Path.Combine(instancePath, "resourcepacks"),
        ContentType.Shader => System.IO.Path.Combine(instancePath, "shaderpacks"),
        ContentType.DataPack when !string.IsNullOrWhiteSpace(selectedWorld) => System.IO.Path.Combine(instancePath, "saves", selectedWorld, "datapacks"),
        ContentType.DataPack => System.IO.Path.Combine(instancePath, "saves"),
        ContentType.World => System.IO.Path.Combine(instancePath, "saves"),
        _ => instancePath
    };

    public static string FileExtension(this ContentType type) => type switch
    {
        ContentType.Mod => ".jar",
        ContentType.ResourcePack => ".zip",
        ContentType.Shader => ".zip",
        ContentType.DataPack => ".zip",
        ContentType.World => ".zip",
        _ => ".zip"
    };
}
