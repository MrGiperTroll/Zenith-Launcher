using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CustomMcLauncher.Services;

/// <summary>
/// Minecraft sign-in with a Microsoft account through the SYSTEM BROWSER and a
/// local HttpListener loopback redirect (standard OAuth2 authorization-code flow).
///
/// Chain: browser sign-in -> authorization code -> MS access + refresh tokens ->
/// Xbox Live (XBL) -> XSTS -> minecraftservices.com session token -> profile.
///
/// The XBL request MUST carry the access token as RpsTicket with the "d=" prefix.
/// </summary>
public class MicrosoftAuthService
{
    private const string ClientId = "a332a5f6-c4dc-45b6-9fe3-d881490252b2";

    private const string AuthorizeUrl = "https://login.live.com/oauth20_authorize.srf";
    private const string TokenUrl = "https://login.live.com/oauth20_token.srf";

    private const string XblAuthUrl = "https://user.auth.xboxlive.com/user/authenticate";
    private const string XstsAuthUrl = "https://xsts.auth.xboxlive.com/xsts/authorize";
    private const string McLoginUrl = "https://api.minecraftservices.com/authentication/login_with_xbox";
    private const string McProfileUrl = "https://api.minecraftservices.com/minecraft/profile";

    private const string Scope = "XboxLive.signin offline_access";

    public sealed record MsAuthResult(string Username, string Uuid, string AccessToken, string RefreshToken);

    public async Task<MsAuthResult> AuthenticateAsync(CancellationToken ct = default)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromMinutes(5)); // user has to finish inside the browser

        var tokens = await AcquireTokensViaBrowserAsync(timeoutCts.Token).ConfigureAwait(false);
        LauncherLog.Info("MSOAuth: Microsoft tokens obtained via system browser, authenticating with Xbox Live...");

        return await BuildSessionFromAccessTokenAsync(tokens.AccessToken, tokens.RefreshToken ?? "", timeoutCts.Token)
            .ConfigureAwait(false);
    }

    /// <summary>Silent path: exchange a stored refresh token for a fresh MC session.</summary>
    public async Task<MsAuthResult> RefreshAsync(string refreshToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
            throw new InvalidOperationException("No stored Microsoft refresh token.");

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(60));

        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = ClientId,
            ["refresh_token"] = refreshToken,
            ["scope"] = Scope
        };

        var tokens = await PostTokenRequestAsync(form, timeoutCts.Token).ConfigureAwait(false);
        LauncherLog.Info("MSOAuth: refresh OK, rebuilding Minecraft session...");

        return await BuildSessionFromAccessTokenAsync(tokens.AccessToken, tokens.RefreshToken ?? refreshToken, timeoutCts.Token)
            .ConfigureAwait(false);
    }

    // ------------------------------------------------------------------ browser + loopback step

    private static async Task<MsTokens> AcquireTokensViaBrowserAsync(CancellationToken ct)
    {
        // 1. Reserve a free localhost port.
        var port = GetFreePort();
        var redirectUri = $"http://localhost:{port}";
        var listenerPrefix = redirectUri + "/";

        // 2. Start listening BEFORE the browser opens so the very first
        //    redirect cannot be missed.
        var listener = new HttpListener();
        listener.Prefixes.Add(listenerPrefix);
        listener.Start();

        // Watchdog: if the user closes every browser window, abort the wait at
        // once so the launcher UI resets instead of hanging until the timeout.
        using var phaseCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var watchdog = BrowserCloseWatchdog.RunAsync(phaseCts);

        try
        {
            var state = Guid.NewGuid().ToString("N");
            var authUrl =
                AuthorizeUrl +
                "?client_id=" + Uri.EscapeDataString(ClientId) +
                "&response_type=code" +
                "&redirect_uri=" + Uri.EscapeDataString(redirectUri) +
                "&scope=" + Uri.EscapeDataString(Scope) +
                "&prompt=select_account" +
                "&state=" + state;

            LauncherLog.Info($"MSOAuth: opening system browser, loopback waiting on {listenerPrefix} ...");
            Process.Start(new ProcessStartInfo { FileName = authUrl, UseShellExecute = true });

            // 3. Wait for the loopback redirect containing ?code=...
            while (true)
            {
                phaseCts.Token.ThrowIfCancellationRequested();

                var ctx = await GetContextAsync(listener, phaseCts.Token).ConfigureAwait(false);
                if (ctx == null)
                    throw new OperationCanceledException("Microsoft sign-in was cancelled.");

                var query = ctx.Request.QueryString;
                var code = query["code"];
                var error = query["error"];

                if (!string.IsNullOrEmpty(error))
                    throw new InvalidOperationException(
                        $"Microsoft sign-in failed: {error}" +
                        (string.IsNullOrEmpty(query["error_description"])
                            ? ""
                            : $" ({query["error_description"]})"));

                if (string.IsNullOrEmpty(code) || query["state"] != state)
                {
                    // Stray request (favicon.ico etc.) - answer and keep waiting.
                    await WriteLoopbackResponse(ctx.Response, 404,
                        "<html><body style='font-family:sans-serif'>Not found.</body></html>").ConfigureAwait(false);
                    continue;
                }

                await WriteLoopbackResponse(ctx.Response, 200,
                    "<html><body style='font-family:sans-serif;background:#0f1420;color:#e6edf7;" +
                    "text-align:center;padding-top:60px;'>" +
                    "<h2>Sign-in complete</h2>" +
                    "<p>You can close this tab and return to the launcher.</p>" +
                    "</body></html>").ConfigureAwait(false);

                // 4. Exchange the authorization code for tokens.
                var form = new Dictionary<string, string>
                {
                    ["grant_type"] = "authorization_code",
                    ["code"] = code!,
                    ["client_id"] = ClientId,
                    ["redirect_uri"] = redirectUri
                };
                return await PostTokenRequestAsync(form, phaseCts.Token).ConfigureAwait(false);
            }
        }
        finally
        {
            phaseCts.Cancel(); // stop the watchdog; it exits via its own catch
            try { listener.Stop(); } catch { /* already closed */ }
        }
    }

    /// <summary>GetContextAsync that honours cancellation.</summary>
    private static async Task<HttpListenerContext?> GetContextAsync(HttpListener listener, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<HttpListenerContext>(TaskCreationOptions.RunContinuationsAsynchronously);
        using (ct.Register(() => tcs.TrySetCanceled()))
        {
            var ctxTask = listener.GetContextAsync();
            // Observe faults of the abandoned task when we cancel first.
            _ = ctxTask.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);

            var completed = await Task.WhenAny(ctxTask, tcs.Task).ConfigureAwait(false);
            return completed == ctxTask ? await ctxTask.ConfigureAwait(false) : null;
        }
    }

    private static async Task WriteLoopbackResponse(HttpListenerResponse response, int statusCode, string htmlBody)
    {
        try
        {
            response.StatusCode = statusCode;
            response.ContentType = "text/html; charset=utf-8";
            var buffer = Encoding.UTF8.GetBytes(htmlBody);
            response.ContentLength64 = buffer.Length;
            await response.OutputStream.WriteAsync(buffer).ConfigureAwait(false);
        }
        catch { /* the browser tab may already be gone */ }
        finally
        {
            try { response.Close(); } catch { }
        }
    }

    private static int GetFreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        try
        {
            return ((IPEndPoint)probe.LocalEndpoint).Port;
        }
        finally
        {
            probe.Stop();
        }
    }

    // ------------------------------------------------------------------ token steps

    private sealed record MsTokens(string AccessToken, string? RefreshToken);

    private static async Task<MsTokens> PostTokenRequestAsync(Dictionary<string, string> form, CancellationToken ct)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        using var content = new FormUrlEncodedContent(form);

        using var response = await http.PostAsync(TokenUrl, content, ct).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            LauncherLog.Error($"[MSOAuth] Token request failed ({(int)response.StatusCode}): {body.Length} bytes response (contents omitted for security).");
            throw new InvalidOperationException($"Microsoft token request failed (HTTP {(int)response.StatusCode}).");
        }

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        var accessToken = root.TryGetProperty("access_token", out var at) ? at.GetString() : null;
        if (string.IsNullOrEmpty(accessToken))
            throw new InvalidOperationException("Microsoft token response did not contain access_token.");

        var refreshToken = root.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null;
        return new MsTokens(accessToken!, refreshToken);
    }

    /// <summary>XBL -> XSTS -> Minecraft session -> profile.</summary>
    private static async Task<MsAuthResult> BuildSessionFromAccessTokenAsync(
        string msAccessToken, string refreshToken, CancellationToken ct)
    {
        var (xblToken, uhs1) = await XboxLiveAuthenticateAsync(msAccessToken, ct).ConfigureAwait(false);
        var (xstsToken, uhs2, xstsError) = await XstsAuthorizeAsync(xblToken, ct).ConfigureAwait(false);
        ThrowIfXstsBlocked(uhs2, xstsError);

        var mcToken = await LoginWithXboxAsync(uhs2, xstsToken, ct).ConfigureAwait(false);
        var (username, uuid) = await GetProfileAsync(mcToken, ct).ConfigureAwait(false);

        return new MsAuthResult(username, uuid, mcToken, refreshToken);
    }

    private static async Task<(string Token, string Uhs)> XboxLiveAuthenticateAsync(string msAccessToken, CancellationToken ct)
    {
        var payload = JsonSerializer.Serialize(new
        {
            Properties = new
            {
                AuthMethod = "RPS",
                SiteName = "user.auth.xboxlive.com",
                RpsTicket = "d=" + msAccessToken
            },
            RelyingParty = "http://auth.xboxlive.com",
            TokenType = "JWT"
        });

        var json = await PostJsonAsync(XblAuthUrl, payload, ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var token = root.GetProperty("Token").GetString();
        var uhs = root.GetProperty("DisplayClaims").GetProperty("xui")[0].GetProperty("uhs").GetString();
        if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(uhs))
            throw new InvalidOperationException("Xbox Live authentication returned an empty token.");
        return (token!, uhs!);
    }

    private static async Task<(string Token, string Uhs, long ErrorCode)> XstsAuthorizeAsync(string xblToken, CancellationToken ct)
    {
        var payload = JsonSerializer.Serialize(new
        {
            Properties = new
            {
                SandboxId = "RETAIL",
                UserTokens = new[] { xblToken }
            },
            RelyingParty = "rp://api.minecraftservices.com/",
            TokenType = "JWT"
        });

        var json = await PostJsonAsync(XstsAuthUrl, payload, ct, allowErrorStatus: true).ConfigureAwait(false);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var token = root.GetProperty("Token").GetString();
            var uhs = root.GetProperty("DisplayClaims").GetProperty("xui")[0].GetProperty("uhs").GetString();
            if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(uhs))
                throw new InvalidOperationException("XSTS authorization returned an empty token.");
            return (token!, uhs!, 0);
        }
        catch (KeyNotFoundException)
        {
            // Error shape: {"XErr":2148916233,"Identity":"","Message":"","Redirect":""}
            using var doc = JsonDocument.Parse(json);
            var xerr = doc.RootElement.TryGetProperty("XErr", out var xe) ? xe.GetInt64() : -1;
            return ("", "", xerr);
        }
    }

    private static void ThrowIfXstsBlocked(string uhs, long errorCode)
    {
        if (!string.IsNullOrEmpty(uhs)) return;

        var reason = errorCode switch
        {
            2148916233 => "This Microsoft account has no Xbox profile. Create one at xbox.com first.",
            2148916238 => "This account is a child account and cannot play Minecraft.",
            2148916235 => "Xbox Live is not available in your country.",
            _ => $"Xbox authorization failed (code {errorCode})."
        };
        throw new InvalidOperationException(reason);
    }

    private static async Task<string> LoginWithXboxAsync(string uhs, string xstsToken, CancellationToken ct)
    {
        var payload = JsonSerializer.Serialize(new { identityToken = $"XBL3.0 x={uhs};{xstsToken}" });
        var json = await PostJsonAsync(McLoginUrl, payload, ct).ConfigureAwait(false);

        using var doc = JsonDocument.Parse(json);
        var token = doc.RootElement.TryGetProperty("access_token", out var at) ? at.GetString() : null;
        if (string.IsNullOrEmpty(token))
            throw new InvalidOperationException("Minecraft session response did not contain access_token.");
        return token!;
    }

    private static async Task<(string Username, string Uuid)> GetProfileAsync(string mcToken, CancellationToken ct)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", mcToken);

        using var response = await http.GetAsync(McProfileUrl, ct).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            throw new InvalidOperationException(
                "This Microsoft account does not own Minecraft. Buy the game or use another account.");
        if (!response.IsSuccessStatusCode)
        {
            LauncherLog.Error($"[MSOAuth] Profile fetch failed ({(int)response.StatusCode}): {body.Length} bytes response (contents omitted for security).");
            throw new InvalidOperationException("Could not fetch the Minecraft profile.");
        }

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var name = root.TryGetProperty("name", out var n) ? n.GetString() : null;
        var id = root.TryGetProperty("id", out var i) ? i.GetString() : null;
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(id))
            throw new InvalidOperationException("Minecraft profile response was incomplete.");
        return (name!, id!);
    }

    // ------------------------------------------------------------------ helpers

    private static async Task<string> PostJsonAsync(string url, string payload, CancellationToken ct, bool allowErrorStatus = false)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");

        using var response = await http.PostAsync(url, content, ct).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!allowErrorStatus && !response.IsSuccessStatusCode)
        {
            LauncherLog.Error($"[MSOAuth] POST {url} failed ({(int)response.StatusCode}): {body.Length} bytes response (contents omitted for security).");
            throw new InvalidOperationException($"Request to {new Uri(url).Host} failed (HTTP {(int)response.StatusCode}).");
        }
        return body;
    }
}
