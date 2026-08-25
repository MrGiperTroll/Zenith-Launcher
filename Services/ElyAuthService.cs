using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CustomMcLauncher.Services;

/// <summary>
/// Ely.by OAuth2 (authorization code) sign-in.
/// Performs the entire OAuth flow locally — the launcher opens the system browser,
/// catches the redirect on a local loopback port, and exchanges the authorization
/// code for tokens directly with Ely.by. The client_secret is embedded in this
/// binary (safe: it is a public-type app registration on Ely.by).
/// Docs: https://docs.ely.by/en/oauth.html
/// </summary>
public class ElyAuthService
{
    // ---- credentials (fill in from your Ely.by app registration) ----
    public const string ClientId = "zenith-launcher2";
    public const string ClientSecret = "A8oanlDVdf2pfvzHSCIC3rXqZ5FDqQ2rfRggieaiaY2X55-SG2ThTQygzbJxJK0z";

    private const string RedirectUri = "http://localhost:7356/auth/callback";
    private const int CallbackPort = 7356;
    private const string Scope = "minecraft_server_session account_info offline_access";

    /// <summary>Base Ely.by OAuth endpoint. prompt=select_account forces the
    /// account picker instead of silently continuing the last session.</summary>
    private const string AuthUrlBase = "https://account.ely.by/oauth2/v1";

    /// <summary>Token exchange endpoint — direct POST, no proxy.</summary>
    private const string TokenUrl = "https://account.ely.by/api/oauth2/v1/token";

    /// <summary>Account info endpoint (user details after token exchange).</summary>
    private const string UserInfoUrl = "https://account.ely.by/api/account/v1/info";

    public static bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ClientId) &&
        !string.IsNullOrWhiteSpace(ClientSecret);

    public sealed record ElyOAuthResult(string Username, string Uuid, string AccessToken, string RefreshToken);

    public async Task<ElyOAuthResult> AuthenticateAsync(CancellationToken ct = default)
    {
        if (!IsConfigured)
            throw new InvalidOperationException(
                "Ely.by OAuth is not configured. Register an app at https://account.ely.by/dev/applications/new " +
                "(type \"Website\", redirect URI \"" + RedirectUri + "\"), set ElyAuthService.ClientId / ClientSecret " +
                "and ensure the redirect URI exactly matches.");

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromMinutes(3));

        var state = GenerateState();
        LauncherLog.Info($"ElyOAuth: opening browser (state={state})...");
        var code = await RequestAuthorizationCodeAsync(state, timeoutCts.Token).ConfigureAwait(false);
        LauncherLog.Info("ElyOAuth: authorization code received, exchanging tokens directly...");
        var token = await ExchangeCodeForTokensAsync(code, timeoutCts.Token).ConfigureAwait(false);
        LauncherLog.Info("ElyOAuth: token exchange OK, fetching account info...");
        var (username, uuid) = await FetchUserInfoAsync(token.AccessToken, timeoutCts.Token).ConfigureAwait(false);
        LauncherLog.Info($"ElyOAuth: signed in as {username}.");

        return new ElyOAuthResult(username, uuid, token.AccessToken, token.RefreshToken ?? "");
    }

    // ---------------------------------------------------------------- auth request

    private static async Task<string> RequestAuthorizationCodeAsync(string state, CancellationToken ct)
    {
        var url = AuthUrlBase +
                  "?client_id=" + Uri.EscapeDataString(ClientId) +
                  "&redirect_uri=" + Uri.EscapeDataString(RedirectUri) +
                  "&response_type=code" +
                  "&scope=" + Uri.EscapeDataString(Scope) +
                  "&prompt=select_account" +
                  "&state=" + Uri.EscapeDataString(state);

        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Could not open the system browser for Ely.by login.", ex);
        }

        const string successHtml =
            "<html><head><meta charset='utf-8'><title>Zenith Launcher</title></head>" +
            "<body style=\"font-family:'Segoe UI',sans-serif;background:#101319;color:#e5e7eb;" +
            "display:flex;align-items:center;justify-content:center;height:100vh;margin:0\">" +
            "<div style='text-align:center'><h2 style='font-weight:600;margin-bottom:8px'>Login successful</h2>" +
            "<p style='color:#9ca3af'>You can close this tab and return to the launcher.</p></div></body></html>";

        // Watchdog: abort the callback wait as soon as every browser window is
        // closed, so the accounts UI resets instead of hanging until the timeout.
        using var phaseCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var watchdog = BrowserCloseWatchdog.RunAsync(phaseCts);

        string code;
        try
        {
            var query = await LoopbackCodeListener.WaitForCallbackAsync(CallbackPort, state, successHtml, phaseCts.Token).ConfigureAwait(false);
            if (!query.TryGetValue("code", out code!))
                throw new InvalidOperationException("Ely.by OAuth: callback did not contain a code.");
        }
        finally
        {
            phaseCts.Cancel(); // stop the watchdog
        }
        return code;
    }

    // ------------------------------------------------------------------ token exchange

    /// <summary>Result of token exchange or refresh from Ely.by.</summary>
    public sealed record TokenResponse(string AccessToken, string? RefreshToken);

    private async Task<TokenResponse> ExchangeCodeForTokensAsync(string code, CancellationToken ct)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        var payload = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = ClientId,
            ["client_secret"] = ClientSecret,
            ["code"] = code,
            ["redirect_uri"] = RedirectUri
        };
        using var content = new FormUrlEncodedContent(payload);

        using var response = await http.PostAsync(TokenUrl, content, ct).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var detail = body.Length > 200 ? body[..200] : body;
            throw new InvalidOperationException(
                $"Ely.by token exchange failed (HTTP {(int)response.StatusCode}). {detail}".Trim());
        }

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        var accessToken = root.TryGetProperty("access_token", out var at) ? at.GetString() : null;
        if (string.IsNullOrEmpty(accessToken))
            throw new InvalidOperationException("Ely.by token response did not contain access_token.");

        var refreshToken = root.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null;
        return new TokenResponse(accessToken!, refreshToken);
    }

    /// <summary>Refresh a stored Ely refresh token.</summary>
    public async Task<TokenResponse> RefreshTokensAsync(string refreshToken, CancellationToken ct = default)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        var payload = new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = ClientId,
            ["client_secret"] = ClientSecret,
            ["refresh_token"] = refreshToken
        };
        using var content = new FormUrlEncodedContent(payload);

        using var response = await http.PostAsync(TokenUrl, content, ct).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var detail = body.Length > 200 ? body[..200] : body;
            throw new InvalidOperationException(
                $"Ely.by token refresh failed (HTTP {(int)response.StatusCode}). {detail}".Trim());
        }

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        var accessToken = root.TryGetProperty("access_token", out var at) ? at.GetString() : null;
        if (string.IsNullOrEmpty(accessToken))
            throw new InvalidOperationException("Ely.by token refresh response did not contain access_token.");

        var newRefreshToken = root.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null;
        return new TokenResponse(accessToken!, newRefreshToken);
    }

    // --------------------------------------------------------------------- user info

    private async Task<(string Username, string Uuid)> FetchUserInfoAsync(string accessToken, CancellationToken ct)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await http.GetAsync(UserInfoUrl, ct).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException("Ely.by account info request failed.");
        }

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        var uuid = root.TryGetProperty("uuid", out var u) ? FormatUuid(u.GetString()) : null;
        var username = root.TryGetProperty("username", out var n) ? n.GetString() : null;

        if (string.IsNullOrEmpty(username))
            throw new InvalidOperationException("Ely.by account info did not contain a username.");

        return (username!, string.IsNullOrEmpty(uuid) ? Guid.NewGuid().ToString("N") : uuid!);
    }

    /// <summary>Ely.by returns dashed UUIDs; Minecraft expects the compact form.</summary>
    private static string FormatUuid(string? dashedUuid)
    {
        if (string.IsNullOrWhiteSpace(dashedUuid)) return Guid.NewGuid().ToString("N");
        return dashedUuid.Replace("-", "").ToLowerInvariant();
    }

    private static string GenerateState()
    {
        var bytes = RandomNumberGenerator.GetBytes(16);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}