using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CustomMcLauncher.Models;

namespace CustomMcLauncher.Services;

public interface IAccountService
{
    IReadOnlyList<AccountModel> GetAccounts();
    AccountModel? GetActiveAccount();
    void SetActiveAccount(string accountId);
    Task<AccountModel> AddOfflineAccountAsync(string username);
    /// <summary>Silent refresh first; falls back to the system-browser login.</summary>
    Task<AccountModel> AuthenticateMicrosoftAsync(CancellationToken ct = default);
    /// <summary>Ely.by OAuth2 sign-in through the system browser.</summary>
    Task<AccountModel> AuthenticateElyByOAuthAsync(CancellationToken ct = default);
    /// <summary>Silent startup maintenance: renew stored sessions via refresh tokens. Returns renewed count.</summary>
    Task<int> RefreshAllSessionsAsync(CancellationToken ct = default);
    void RemoveAccount(string accountId);
}
