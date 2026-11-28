using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ZenithLauncher.Models;

namespace ZenithLauncher.Services;

public class AccountService : IAccountService
{
    private readonly string _accountsFilePath;
    private readonly ElyAuthService _elyAuth = new();
    private readonly MicrosoftAuthService _msAuth = new();
    private readonly object _lock = new();
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

    public IReadOnlyList<AccountModel> GetAccounts()
    {
        lock (_lock)
        {
            return _accounts.ToList().AsReadOnly();
        }
    }

    public AccountModel? GetActiveAccount()
    {
        lock (_lock)
        {
            return _accounts.FirstOrDefault(a => a.Id == _activeAccountId) ?? _accounts.FirstOrDefault();
        }
    }

    public void SetActiveAccount(string accountId)
    {
        lock (_lock)
        {
            _activeAccountId = accountId;
            SaveAccountsLocked();
        }
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
        lock (_lock)
        {
            _accounts.Add(account);
            _activeAccountId = account.Id;
            SaveAccountsLocked();
        }
        return Task.FromResult(account);
    }

    public async Task<AccountModel> AuthenticateMicrosoftAsync(CancellationToken ct = default)
    {
        MicrosoftAuthService.MsAuthResult result;

        // Silent path: refresh the stored token without any UI. This is the
        // common path for returning users and avoids a browser popup each time.
        string? existingUsername = null;
        string? existingRefreshToken = null;
        lock (_lock)
        {
            var existing = _accounts.LastOrDefault(a => a.Type == AccountType.Microsoft && !string.IsNullOrEmpty(a.RefreshToken));
            if (existing != null)
            {
                existingUsername = existing.Username;
                existingRefreshToken = existing.RefreshToken;
            }
        }

        if (!string.IsNullOrEmpty(existingRefreshToken))
        {
            try
            {
                LauncherLog.Info($"MS auth: refreshing session for '{existingUsername}'...");
                result = await _msAuth.RefreshAsync(existingRefreshToken, ct).ConfigureAwait(false);
                return ReplaceAccount(result.Username, AccountType.Microsoft, result);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                LauncherLog.Info($"MS auth: refresh failed ({ex.GetType().Name}: {ex.Message}); falling back to browser login.");
            }
        }

        LauncherLog.Info("MS auth: starting interactive system-browser login...");
        result = await _msAuth.AuthenticateAsync(ct).ConfigureAwait(false);
        LauncherLog.Info($"MS auth: signed in as {result.Username}.");
        return ReplaceAccount(result.Username, AccountType.Microsoft, result);
    }

    private AccountModel ReplaceAccount(string username, AccountType type, MicrosoftAuthService.MsAuthResult result)
    {
        var account = new AccountModel
        {
            Username = username,
            Uuid = result.Uuid,
            AccessToken = result.AccessToken,
            RefreshToken = result.RefreshToken,
            Type = type
        };

        lock (_lock)
        {
            _accounts.RemoveAll(a => a.Username == account.Username && a.Type == type);
            _accounts.Add(account);
            _activeAccountId = account.Id;
            SaveAccountsLocked();
        }
        return account;
    }

    public async Task<AccountModel> AuthenticateElyByOAuthAsync(CancellationToken ct = default)
    {
        var result = await _elyAuth.AuthenticateAsync(ct).ConfigureAwait(false);

        var account = new AccountModel
        {
            Username = result.Username,
            Uuid = result.Uuid,
            AccessToken = result.AccessToken,
            RefreshToken = string.IsNullOrEmpty(result.RefreshToken) ? "" : result.RefreshToken,
            Type = AccountType.ElyBy
        };

        lock (_lock)
        {
            _accounts.RemoveAll(a => a.Username == account.Username && a.Type == AccountType.ElyBy);
            _accounts.Add(account);
            _activeAccountId = account.Id;
            SaveAccountsLocked();
        }
        return account;
    }

    /// <summary>
    /// Silent startup maintenance: renew expired sessions for every stored
    /// account that has a refresh token (Microsoft and Ely.by), so returning
    /// users never see a browser popup. Returns the number of renewed accounts.
    /// </summary>
    public async Task<int> RefreshAllSessionsAsync(CancellationToken ct = default)
    {
        List<(string Id, string Username, AccountType Type, string RefreshToken)> snapshot;
        lock (_lock)
        {
            snapshot = _accounts
                .Where(a => !string.IsNullOrEmpty(a.RefreshToken))
                .Select(a => (a.Id, a.Username, a.Type, a.RefreshToken!))
                .ToList();
        }

        var renewed = 0;
        foreach (var acc in snapshot)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                switch (acc.Type)
                {
                    case AccountType.Microsoft:
                    {
                        var r = await _msAuth.RefreshAsync(acc.RefreshToken, ct).ConfigureAwait(false);
                        if (UpdateSessionTokens(acc.Id, r.AccessToken, r.RefreshToken))
                            renewed++;
                        break;
                    }
                    case AccountType.ElyBy:
                    {
                        var t = await _elyAuth.RefreshTokensAsync(acc.RefreshToken, ct).ConfigureAwait(false);
                        if (UpdateSessionTokens(acc.Id, t.AccessToken, t.RefreshToken ?? acc.RefreshToken))
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

    private bool UpdateSessionTokens(string accountId, string accessToken, string? refreshToken)
    {
        lock (_lock)
        {
            var acc = _accounts.FirstOrDefault(a => a.Id == accountId);
            if (acc == null) return false;

            acc.AccessToken = accessToken;
            if (!string.IsNullOrEmpty(refreshToken))
                acc.RefreshToken = refreshToken;
            // Persist without touching the active-account pointer.
            SaveAccountsLocked();
            return true;
        }
    }

    public void RemoveAccount(string accountId)
    {
        lock (_lock)
        {
            var acc = _accounts.FirstOrDefault(a => a.Id == accountId);
            if (acc != null)
            {
                _accounts.Remove(acc);
                if (_activeAccountId == accountId)
                    _activeAccountId = _accounts.FirstOrDefault()?.Id;
                SaveAccountsLocked();
            }
        }
    }

    public void ReorderAccounts(string sourceId, string targetId, bool dropAfter)
    {
        lock (_lock)
        {
            var source = _accounts.FirstOrDefault(a => a.Id == sourceId);
            var target = _accounts.FirstOrDefault(a => a.Id == targetId);
            if (source == null || target == null || source == target) return;

            _accounts.Remove(source);
            int targetIdx = _accounts.IndexOf(target);
            if (targetIdx < 0)
            {
                _accounts.Add(source);
            }
            else
            {
                int insertIdx = dropAfter ? targetIdx + 1 : targetIdx;
                if (insertIdx < 0) insertIdx = 0;
                if (insertIdx > _accounts.Count) insertIdx = _accounts.Count;
                _accounts.Insert(insertIdx, source);
            }
            SaveAccountsLocked();
        }
    }

    private void LoadAccounts()
    {
        lock (_lock)
        {
            if (!File.Exists(_accountsFilePath)) return;

            try
            {
                var json = File.ReadAllText(_accountsFilePath);
                AccountsStore? store = null;
                var trimmed = json.TrimStart();
                if (trimmed.StartsWith("["))
                {
                    var legacyList = JsonSerializer.Deserialize(json, LauncherJsonContext.Default.ListAccountModel);
                    if (legacyList != null)
                    {
                        store = new AccountsStore
                        {
                            Accounts = legacyList,
                            ActiveAccountId = legacyList.FirstOrDefault()?.Id
                        };
                    }
                }
                else
                {
                    store = JsonSerializer.Deserialize(json, LauncherJsonContext.Default.AccountsStore);
                }

                if (store != null)
                {
                    _accounts = store.Accounts ?? new();
                    _activeAccountId = store.ActiveAccountId ?? _accounts.FirstOrDefault()?.Id;

                    // Decrypt tokens (skip offline placeholders to avoid startup FormatException overhead)
                    foreach (var acc in _accounts)
                    {
                        if (acc.Type == AccountType.Offline) continue;
                        acc.AccessToken = TryDecrypt(acc.AccessToken, acc.Username);
                        acc.RefreshToken = TryDecrypt(acc.RefreshToken, acc.Username);
                    }
                }
            }
            catch (Exception ex)
            {
                LauncherLog.Error("Failed to load accounts file", ex);
                _accounts = new();
            }
        }
    }

    private void SaveAccountsLocked()
    {
        // Serialize with encrypted tokens (caller must hold _lock)
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

        var storeToSave = new AccountsStore
        {
            Accounts = clone,
            ActiveAccountId = _activeAccountId
        };

        var json = JsonSerializer.Serialize(storeToSave, LauncherJsonContext.Default.AccountsStore);
        AtomicFileWriter.WriteAllText(_accountsFilePath, json);
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
}
