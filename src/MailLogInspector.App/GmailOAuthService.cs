using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MailLogInspector.Storage;

namespace MailLogInspector.App;

public sealed class GmailOAuthService : IGmailAccessTokenProvider
{
    private const string GoogleMailScope = "https://mail.google.com/";
    private static readonly Uri AuthorizeEndpoint = new("https://accounts.google.com/o/oauth2/v2/auth");
    private static readonly Uri TokenEndpoint = new("https://oauth2.googleapis.com/token");
    private readonly HttpClient _httpClient;

    public GmailOAuthService(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
    }

    public async Task<GmailOAuthTokenEnvelope> AuthorizeInteractiveAsync(GmailOAuthConfig config, CancellationToken cancellationToken)
    {
        int port = GetRandomUnusedPort();
        string redirectUri = $"http://127.0.0.1:{port}/oauth/callback/";
        string state = Guid.NewGuid().ToString("N");
        string verifier = CreatePkceVerifier();
        string challenge = CreatePkceChallenge(verifier);

        using HttpListener listener = new();
        listener.Prefixes.Add(redirectUri);
        listener.Start();

        Uri authorizationUri = BuildAuthorizationUri(config.ClientId, redirectUri, state, challenge);
        OpenSystemBrowser(authorizationUri);

        HttpListenerContext callback = await listener.GetContextAsync().WaitAsync(cancellationToken);
        string? code = callback.Request.QueryString["code"];
        string? actualState = callback.Request.QueryString["state"];
        await WriteCallbackResponseAsync(callback.Response);

        if (string.IsNullOrWhiteSpace(code) || !string.Equals(state, actualState, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Google OAuth antwoord is ongeldig.");
        }

        using HttpRequestMessage request = new(HttpMethod.Post, TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string?>
            {
                ["client_id"] = config.ClientId,
                ["client_secret"] = config.ClientSecret,
                ["code"] = code,
                ["code_verifier"] = verifier,
                ["grant_type"] = "authorization_code",
                ["redirect_uri"] = redirectUri
            })
        };

        using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        using JsonDocument json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        string accessToken = json.RootElement.GetProperty("access_token").GetString() ?? throw new InvalidOperationException("Google gaf geen access token terug.");
        string refreshToken = json.RootElement.GetProperty("refresh_token").GetString() ?? throw new InvalidOperationException("Google gaf geen refresh token terug.");
        int expiresIn = json.RootElement.GetProperty("expires_in").GetInt32();
        return new GmailOAuthTokenEnvelope(accessToken, refreshToken, DateTimeOffset.UtcNow.AddSeconds(expiresIn));
    }

    public async Task<string> GetAccessTokenAsync(GmailOAuthConfig config, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(config.RefreshToken))
        {
            throw new InvalidOperationException("Er is nog geen Gmail refresh token opgeslagen.");
        }

        using HttpRequestMessage request = new(HttpMethod.Post, TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string?>
            {
                ["client_id"] = config.ClientId,
                ["client_secret"] = config.ClientSecret,
                ["refresh_token"] = config.RefreshToken,
                ["grant_type"] = "refresh_token"
            })
        };

        using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        using JsonDocument json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return json.RootElement.GetProperty("access_token").GetString() ?? throw new InvalidOperationException("Google gaf geen access token terug.");
    }

    public static string ProtectRefreshToken(string refreshToken)
    {
        return ProtectSecret(refreshToken);
    }

    public static string UnprotectRefreshToken(string protectedRefreshToken)
    {
        return UnprotectSecret(protectedRefreshToken);
    }

    public static string ProtectClientSecret(string clientSecret)
    {
        return "dpapi:" + ProtectSecret(clientSecret);
    }

    public static string UnprotectClientSecret(string storedClientSecret)
    {
        return storedClientSecret.StartsWith("dpapi:", StringComparison.Ordinal)
            ? UnprotectSecret(storedClientSecret[6..])
            : storedClientSecret;
    }

    public static bool MigrateLegacyClientSecret(GmailReportOperationalStore store)
    {
        GmailReportConfig config = store.LoadConfig();
        if (string.IsNullOrWhiteSpace(config.ClientSecret) ||
            config.ClientSecret.StartsWith("dpapi:", StringComparison.Ordinal))
        {
            return false;
        }

        store.SaveConfig(config with
        {
            ClientSecret = ProtectClientSecret(config.ClientSecret)
        });
        return true;
    }

    public static string ProtectSecret(string secret)
    {
        byte[] plaintext = Encoding.UTF8.GetBytes(secret);
        byte[] protectedBytes = ProtectedData.Protect(plaintext, null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(protectedBytes);
    }

    public static string UnprotectSecret(string protectedSecret)
    {
        byte[] protectedBytes = Convert.FromBase64String(protectedSecret);
        byte[] plaintext = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(plaintext);
    }

    private static Uri BuildAuthorizationUri(string clientId, string redirectUri, string state, string challenge)
    {
        var builder = new UriBuilder(AuthorizeEndpoint);
        builder.Query =
            $"client_id={Uri.EscapeDataString(clientId)}&redirect_uri={Uri.EscapeDataString(redirectUri)}&response_type=code&scope={Uri.EscapeDataString(GoogleMailScope)}&access_type=offline&prompt=consent&state={Uri.EscapeDataString(state)}&code_challenge={Uri.EscapeDataString(challenge)}&code_challenge_method=S256";
        return builder.Uri;
    }

    private static void OpenSystemBrowser(Uri uri)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = uri.ToString(),
            UseShellExecute = true
        });
    }

    private static async Task WriteCallbackResponseAsync(HttpListenerResponse response)
    {
        response.StatusCode = 200;
        response.ContentType = "text/html; charset=utf-8";
        byte[] bytes = Encoding.UTF8.GetBytes("<html><body><h2>Gmail koppeling gereed</h2><p>Je kunt nu terug naar Mail Log Inspector.</p></body></html>");
        await response.OutputStream.WriteAsync(bytes);
        response.Close();
    }

    private static int GetRandomUnusedPort()
    {
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static string CreatePkceVerifier()
    {
        byte[] bytes = RandomNumberGenerator.GetBytes(32);
        return Base64UrlEncode(bytes);
    }

    private static string CreatePkceChallenge(string verifier)
    {
        byte[] hash = SHA256.HashData(Encoding.ASCII.GetBytes(verifier));
        return Base64UrlEncode(hash);
    }

    private static string Base64UrlEncode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
