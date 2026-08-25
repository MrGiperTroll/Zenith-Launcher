using System;

namespace CustomMcLauncher.Models;

public enum AccountType
{
    Offline,
    Microsoft,
    ElyBy
}

public class AccountModel
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Username { get; set; } = "Player";
    public string Uuid { get; set; } = Guid.NewGuid().ToString("N");
    public string AccessToken { get; set; } = string.Empty;
    /// <summary>OAuth refresh token (Microsoft) - encrypted at rest like AccessToken.</summary>
    public string RefreshToken { get; set; } = string.Empty;
    public AccountType Type { get; set; } = AccountType.Offline;
}
