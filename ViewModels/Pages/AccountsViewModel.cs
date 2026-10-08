using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CustomMcLauncher.Models;
using CustomMcLauncher.Services;
using CustomMcLauncher.ViewModels.Common;

namespace CustomMcLauncher.ViewModels.Pages;

public partial class AccountsViewModel : ViewModelBase
{
    private readonly IAccountService _accountService;
    private CancellationTokenSource? _authCts;

    public event Action? AccountStateChanged;
    public Action<string>? OnStatusMessage;

    public ObservableCollection<AccountModel> Accounts { get; } = new();
    public ObservableCollection<AccountRow> AccountRows { get; } = new();

    [ObservableProperty]
    private AccountModel? _selectedAccount;

    [ObservableProperty]
    private string _activeAccountName = "Steve";

    [ObservableProperty]
    private string _activeAccountType = "Offline";

    [ObservableProperty]
    private string _newOfflineUsername = string.Empty;

    [ObservableProperty]
    private bool _hasNoAccounts;

    [ObservableProperty]
    private bool _isOfflinePanelOpen;

    [ObservableProperty]
    private bool _isAccountsBusy;

    [ObservableProperty]
    private string _authErrorText = string.Empty;

    public AccountRow? ActiveAccountRow
    {
        get
        {
            foreach (var r in AccountRows)
            {
                if (r.IsActive) return r;
            }
            return null;
        }
    }

    public AccountsViewModel(IAccountService accountService)
    {
        _accountService = accountService;
    }

    public void RefreshAccounts()
    {
        var allAccs = _accountService.GetAccounts();
        Accounts.Clear();
        foreach (var acc in allAccs)
            Accounts.Add(acc);

        var activeAcc = _accountService.GetActiveAccount();
        ActiveAccountName = activeAcc?.Username ?? "Steve";
        ActiveAccountType = activeAcc?.Type.ToString() ?? "Offline";
        SelectedAccount = activeAcc;

        AccountRows.Clear();
        foreach (var acc in allAccs)
        {
            var row = new AccountRow(acc) { IsActive = activeAcc != null && acc.Id == activeAcc.Id };
            AccountRows.Add(row);
            _ = row.LoadHeadAsync();
        }
        HasNoAccounts = AccountRows.Count == 0;
        OnPropertyChanged(nameof(ActiveAccountRow));

        AccountStateChanged?.Invoke();
    }

    [RelayCommand]
    public void SwitchAccount(AccountModel? account)
    {
        if (account == null) return;
        _accountService.SetActiveAccount(account.Id);
        RefreshAccounts();
    }

    [RelayCommand]
    public void DeleteAccount(AccountModel? account)
    {
        if (account == null) return;
        _accountService.RemoveAccount(account.Id);
        RefreshAccounts();
    }

    [RelayCommand]
    public void ShowOfflinePanel() => IsOfflinePanelOpen = true;

    [RelayCommand]
    public void CancelOfflinePanel() => IsOfflinePanelOpen = false;

    [RelayCommand]
    public async Task AddOfflineAccountAsync()
    {
        if (!string.IsNullOrWhiteSpace(NewOfflineUsername))
        {
            var acc = await _accountService.AddOfflineAccountAsync(NewOfflineUsername.Trim());
            _accountService.SetActiveAccount(acc.Id);
            NewOfflineUsername = "";
            IsOfflinePanelOpen = false;
            RefreshAccounts();
        }
    }

    public void ResetAuthUiState()
    {
        IsAccountsBusy = false;
        AuthErrorText = "";
    }

    [RelayCommand]
    public async Task LoginElyByAsync()
    {
        if (IsAccountsBusy)
        {
            OnStatusMessage?.Invoke(L10n.T("mw_status_signin_busy"));
            return;
        }
        IsAccountsBusy = true;
        AuthErrorText = "";
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        _authCts = cts;
        try
        {
            OnStatusMessage?.Invoke(L10n.T("acc_status_ely_browser"));
            await _accountService.AuthenticateElyByOAuthAsync(cts.Token);
            RefreshAccounts();
            OnStatusMessage?.Invoke(L10n.T("acc_status_ely_added"));
        }
        catch (OperationCanceledException)
        {
            var msg = L10n.T("mw_status_signin_timeout");
            OnStatusMessage?.Invoke(msg);
            AuthErrorText = msg;
        }
        catch (InvalidOperationException ex)
        {
            LauncherLog.Error("Ely.by login failed", ex);
            OnStatusMessage?.Invoke(ex.Message);
            AuthErrorText = ex.Message;
        }
        catch (HttpRequestException ex)
        {
            LauncherLog.Error("Ely.by login failed", ex);
            var msg = L10n.T("acc_status_conn_unreachable");
            OnStatusMessage?.Invoke(msg);
            AuthErrorText = msg;
        }
        catch (Exception ex)
        {
            LauncherLog.Error("Ely.by login failed", ex);
            var msg = $"Ely.by login failed ({ex.GetType().Name}). See launcher.log for details.";
            OnStatusMessage?.Invoke(msg);
            AuthErrorText = msg;
        }
        finally
        {
            if (ReferenceEquals(_authCts, cts)) _authCts = null;
            IsAccountsBusy = false;
        }
    }

    [RelayCommand]
    public async Task LoginMicrosoftAsync()
    {
        if (IsAccountsBusy)
        {
            OnStatusMessage?.Invoke(L10n.T("mw_status_signin_busy"));
            return;
        }
        IsAccountsBusy = true;
        AuthErrorText = "";
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        _authCts = cts;
        try
        {
            OnStatusMessage?.Invoke("Signing in with Microsoft...");
            var acc = await _accountService.AuthenticateMicrosoftAsync(cts.Token);
            _accountService.SetActiveAccount(acc.Id);
            RefreshAccounts();
            OnStatusMessage?.Invoke(L10n.T("acc_status_ms_added"));
        }
        catch (OperationCanceledException)
        {
            var msg = L10n.T("mw_status_signin_timeout");
            OnStatusMessage?.Invoke(msg);
            AuthErrorText = msg;
        }
        catch (InvalidOperationException ex)
        {
            LauncherLog.Error("Microsoft login failed", ex);
            OnStatusMessage?.Invoke(ex.Message);
            AuthErrorText = ex.Message;
        }
        catch (Exception ex)
        {
            LauncherLog.Error("Microsoft login failed", ex);
            var msg = string.Format(L10n.T("acc_status_ms_failed"), ex.GetType().Name);
            OnStatusMessage?.Invoke(msg);
            AuthErrorText = msg;
        }
        finally
        {
            if (ReferenceEquals(_authCts, cts)) _authCts = null;
            IsAccountsBusy = false;
        }
    }
}
