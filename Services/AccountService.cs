using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CustomMcLauncher.Models;

namespace CustomMcLauncher.Services;

public class AccountService : IAccountService
{
    private readonly string _accountsFilePath;
    private readonly ElyAuthService _elyAuth = new();
    private readonly MicrosoftAuthService _msAuth = new();
    private List<AccountModel> _accounts = new();
    private string? _activeAccountId;

    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("ZenithLauncher_v1");

    public AccountService()
    {
        var baseDir = ZenithPaths.AppDataDir;
        Directory.CreateDirectory(baseDir);
        _accountsFilePath = Path.Combine(baseDir, "accounts.json");
        LoadAccounts();
    }

    public IReadOnlyList<AccountModel> GetAccounts() => _accounts.AsReadOnly();
    public AccountModel? GetActiveAccount() => _accounts.FirstOrDefault(a => a.Id == _activeAccountId) ?? _accounts.FirstOrDefault();

    public void SetActiveAccount(string accountId)
    {
        _activeAccountId = accountId;
        SaveAccounts();
    }

    public Task<AccountModel> AddOfflineAccountAsync(string username)
    {
        var account = new AccountModel
        {
            Username = username,
            Type = AccountType.Offline,
            Uuid = Guid.NewGuid().ToString("N"),
            AccessToken = "offline_token"
        };
        _accounts.Add(account);
        _activeAccountId = account.Id;
        SaveAccounts();
        return Task.FromResult(account);
    }

    public async Task<AccountModel> AuthenticateMicrosoftAsync(CancellationToken ct = default)
    {
        MicrosoftAuthService.MsAuthResult result;

        // Silent path: refresh the stored token without any UI. This is the
        // common path for returning users and avoids a browser popup each time.
        var existing = _accounts.LastOrDefault(a => a.Type == AccountType.Microsoft && !string.IsNullOrEmpty(a.RefreshToken));
        if (existing != null)
        {
            try
            {
                LauncherLog.Info($"MS auth: refreshing session for '{existing.Username}'...");
                result = await _msAuth.RefreshAsync(existing.RefreshToken, ct);
                ReplaceAccount(existing.Username, AccountType.Microsoft, result);
                return GetAccounts().First(a => a.Username == result.Username && a.Type == AccountType.Microsoft);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                LauncherLog.Info($"MS auth: refresh failed ({ex.GetType().Name}: {ex.Message}); falling back to browser login.");
            }
        }

        LauncherLog.Info("MS auth: starting interactive system-browser login...");
        result = await _msAuth.AuthenticateAsync(ct);
        LauncherLog.Info($"MS auth: signed in as {result.Username}.");
        ReplaceAccount(result.Username, AccountType.Microsoft, result);
        return GetAccounts().First(a => a.Username == result.Username && a.Type == AccountType.Microsoft);
    }

    private void ReplaceAccount(string username, AccountType type, MicrosoftAuthService.MsAuthResult result)
    {
        var account = new AccountModel
        {
            Username = username,
            Uuid = result.Uuid,
            AccessToken = result.AccessToken,
            RefreshToken = result.RefreshToken,
            Type = type
        };

        _accounts.RemoveAll(a => a.Username == account.Username && a.Type == type);
        _accounts.Add(account);
        _activeAccountId = account.Id;
        SaveAccounts();
    }

    public async Task<AccountModel> AuthenticateElyByOAuthAsync(CancellationToken ct = default)
    {
        var result = await _elyAuth.AuthenticateAsync(ct);

        var account = new AccountModel
        {
            Username = result.Username,
            Uuid = result.Uuid,
            AccessToken = result.AccessToken,
            RefreshToken = string.IsNullOrEmpty(result.RefreshToken) ? "" : result.RefreshToken,
            Type = AccountType.ElyBy
        };

        _accounts.RemoveAll(a => a.Username == account.Username && a.Type == AccountType.ElyBy);
        _accounts.Add(account);
        _activeAccountId = account.Id;
        SaveAccounts();
        return account;
    }

    /// <summary>
    /// Silent startup maintenance: renew expired sessions for every stored
    /// account that has a refresh token (Microsoft and Ely.by), so returning
    /// users never see a browser popup. Returns the number of renewed accounts.
    /// </summary>
    public async Task<int> RefreshAllSessionsAsync(CancellationToken ct = default)
    {
        var renewed = 0;
        foreach (var acc in _accounts.ToList())
        {
            if (string.IsNullOrEmpty(acc.RefreshToken))
                continue;

            try
            {
                switch (acc.Type)
                {
                    case AccountType.Microsoft:
                    {
                        var r = await _msAuth.RefreshAsync(acc.RefreshToken!, ct).ConfigureAwait(false);
                        UpdateSessionTokens(acc.Id, r.AccessToken, r.RefreshToken);
                        renewed++;
                        break;
                    }
                    case AccountType.ElyBy:
                    {
                        var t = await _elyAuth.RefreshTokensAsync(acc.RefreshToken!, ct).ConfigureAwait(false);
                        UpdateSessionTokens(acc.Id, t.AccessToken, t.RefreshToken ?? acc.RefreshToken);
                        renewed++;
                        break;
                    }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                LauncherLog.Error($"Silent session refresh failed for '{acc.Username}' ({acc.Type}) - re-login required on next use.", ex);
            }
        }

        if (renewed > 0)
            LauncherLog.Info($"Silent session refresh: {renewed} account(s) renewed.");
        return renewed;
    }

    private void UpdateSessionTokens(string accountId, string accessToken, string? refreshToken)
    {
        var acc = _accounts.FirstOrDefault(a => a.Id == accountId);
        if (acc == null) return;

        acc.AccessToken = accessToken;
        if (!string.IsNullOrEmpty(refreshToken))
            acc.RefreshToken = refreshToken;
        // Persist without touching the active-account pointer.
        SaveAccounts();
    }

    public void RemoveAccount(string accountId)
    {
        var acc = _accounts.FirstOrDefault(a => a.Id == accountId);
        if (acc != null)
        {
            _accounts.Remove(acc);
            if (_activeAccountId == accountId)
                _activeAccountId = _accounts.FirstOrDefault()?.Id;
            SaveAccounts();
        }
    }

    private void LoadAccounts()
    {
        if (File.Exists(_accountsFilePath))
        {
            try
            {
                var json = File.ReadAllText(_accountsFilePath);
                var store = JsonSerializer.Deserialize<AccountStore>(json);
                if (store != null)
                {
                    _accounts = store.Accounts;
                    _activeAccountId = store.ActiveAccountId ?? _accounts.FirstOrDefault()?.Id;

                    // Decrypt tokens
                    foreach (var acc in _accounts)
                    {
                        acc.AccessToken = TryDecrypt(acc.AccessToken, acc.Username);
                        acc.RefreshToken = TryDecrypt(acc.RefreshToken, acc.Username);
                    }
                }
            }
            catch (Exception ex) { LauncherLog.Error("Failed to load accounts file", ex); _accounts = new(); }
        }
    }

    private void SaveAccounts()
    {
        // Serialize with encrypted tokens
        var clone = _accounts.Select(a => new AccountModel
        {
            Id = a.Id,
            Username = a.Username,
            Uuid = a.Uuid,
            AccessToken = a.Type != AccountType.Offline && !string.IsNullOrEmpty(a.AccessToken)
                ? EncryptToken(a.AccessToken)
                : a.AccessToken,
            RefreshToken = a.Type != AccountType.Offline && !string.IsNullOrEmpty(a.RefreshToken)
                ? EncryptToken(a.RefreshToken)
                : a.RefreshToken,
            Type = a.Type
        }).ToList();

        var storeToSave = new AccountStore
        {
            Accounts = clone,
            ActiveAccountId = _activeAccountId
        };

        var json = JsonSerializer.Serialize(storeToSave, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_accountsFilePath, json);
    }

    private static string TryDecrypt(string value, string username)
    {
        if (string.IsNullOrEmpty(value)) return value;
        try
        {
            var encrypted = Convert.FromBase64String(value);
            var decrypted = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(decrypted);
        }
        catch (Exception ex)
        {
            LauncherLog.Error($"Failed to decrypt token for account '{username}' (leaving as-is)", ex);
            return value;
        }
    }

    private static string EncryptToken(string token)
    {
        try
        {
            var plaintext = Encoding.UTF8.GetBytes(token);
            var encrypted = ProtectedData.Protect(plaintext, Entropy, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(encrypted);
        }
        catch (Exception ex)
        {
            LauncherLog.Error("DPAPI encryption failed — storing token as plaintext (less secure).", ex);
            return token;
        }
    }

    private class AccountStore
    {
        public List<AccountModel> Accounts { get; set; } = new();
        public string? ActiveAccountId { get; set; }
    }
}
