using SubscriptionTiger.Models;
using SubscriptionTiger.Services.Interfaces;
using Microsoft.Maui.Authentication;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SubscriptionTiger.Services;

public sealed class GmailAuthService : IGmailAuthService
{
    private const string MissingClientIdMessage = "Add a Google OAuth client ID before Gmail can be scanned.";
    private const string OAuthScope = "https://www.googleapis.com/auth/gmail.readonly";
    private const string AuthorizationEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
    private const string TokenEndpoint = "https://oauth2.googleapis.com/token";

    private readonly GmailConfiguration configuration;
    private readonly HttpClient httpClient;
    private readonly DiagnosticsService diagnosticsService;

    public GmailAuthService(GmailConfiguration configuration, HttpClient httpClient, DiagnosticsService diagnosticsService)
    {
        this.configuration = configuration;
        this.httpClient = httpClient;
        this.diagnosticsService = diagnosticsService;
    }

    public async Task<GmailAuthResult> AuthenticateAsync(CancellationToken cancellationToken)
    {
        diagnosticsService.RecordGmailOAuthStatus("Gmail auth service started");
        if (!configuration.IsConfigured)
        {
            diagnosticsService.RecordGmailOAuthStatus("Google OAuth failed: MissingClientConfiguration");
            return new GmailAuthResult(
                IsConfigured: false,
                IsSuccess: false,
                AccessToken: null,
                ResultMessage: MissingClientIdMessage,
                ScanMode: "Google OAuth setup required");
        }

        cancellationToken.ThrowIfCancellationRequested();

        diagnosticsService.RecordGmailOAuthStatus(
            $"OAuth config client_id={configuration.ClientId} redirect_uri={configuration.RedirectUri}");

        var codeVerifier = CreateCodeVerifier();
        var codeChallenge = CreateCodeChallenge(codeVerifier);
        var authorizationUrl = BuildAuthorizationUrl(configuration, codeChallenge);
        var callbackUrl = new Uri(configuration.RedirectUri);
        diagnosticsService.RecordGmailOAuthStatus("About to launch Google OAuth");

        WebAuthenticatorResult authResult;
        try
        {
            diagnosticsService.RecordGmailOAuthStatus("Google OAuth launched / awaiting result");
            authResult = await WebAuthenticator.Default
                .AuthenticateAsync(authorizationUrl, callbackUrl)
                .WaitAsync(cancellationToken);
            diagnosticsService.RecordGmailOAuthStatus("App returned from OAuth callback");
        }
        catch (OperationCanceledException)
        {
            diagnosticsService.RecordGmailOAuthStatus("Google OAuth cancelled");
            return new GmailAuthResult(
                IsConfigured: true,
                IsSuccess: false,
                AccessToken: null,
                ResultMessage: "Gmail sign-in was canceled.",
                ScanMode: "Google OAuth canceled");
        }
        catch (Exception ex)
        {
            diagnosticsService.RecordGmailOAuthStatus($"Google OAuth failed: {ex.GetType().Name}: {ex.Message}");
            return new GmailAuthResult(
                IsConfigured: true,
                IsSuccess: false,
                AccessToken: null,
                ResultMessage: $"Google OAuth launch failed: {ex.GetType().Name}: {ex.Message}",
                ScanMode: "Google OAuth launch failed");
        }

        if (TryGetValue(authResult, "error", out var oauthError))
        {
            diagnosticsService.RecordGmailOAuthStatus($"Google OAuth failed: {oauthError}");
            TryGetValue(authResult, "error_description", out var oauthErrorDescription);
            var message = string.IsNullOrWhiteSpace(oauthErrorDescription)
                ? $"Google OAuth error: {oauthError}."
                : $"Google OAuth error: {oauthErrorDescription}.";

            return new GmailAuthResult(
                IsConfigured: true,
                IsSuccess: false,
                AccessToken: null,
                ResultMessage: message,
                ScanMode: "Google OAuth failed");
        }

        if (!TryGetValue(authResult, "code", out var authorizationCode) || string.IsNullOrWhiteSpace(authorizationCode))
        {
            diagnosticsService.RecordGmailOAuthStatus("Google OAuth failed: AuthorizationCodeMissing");
            return new GmailAuthResult(
                IsConfigured: true,
                IsSuccess: false,
                AccessToken: null,
                ResultMessage: "Google OAuth did not return an authorization code.",
                ScanMode: "Google OAuth failed");
        }

        var accessToken = await ExchangeCodeForAccessTokenAsync(authorizationCode, codeVerifier, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            diagnosticsService.RecordGmailOAuthStatus("Google OAuth failed: TokenExchangeFailed");
            return new GmailAuthResult(
                IsConfigured: true,
                IsSuccess: false,
                AccessToken: null,
                ResultMessage: "Google OAuth token exchange failed.",
                ScanMode: "Google OAuth failed");
        }

        return new GmailAuthResult(
            IsConfigured: true,
            IsSuccess: true,
            AccessToken: accessToken,
            ResultMessage: "Google OAuth connected.",
            ScanMode: "Google OAuth token acquired");
    }

    private static Uri BuildAuthorizationUrl(GmailConfiguration configuration, string codeChallenge)
    {
        var query = new Dictionary<string, string>
        {
            ["client_id"] = configuration.ClientId,
            ["redirect_uri"] = configuration.RedirectUri,
            ["response_type"] = "code",
            ["scope"] = string.IsNullOrWhiteSpace(configuration.Scope) ? OAuthScope : configuration.Scope,
            ["code_challenge"] = codeChallenge,
            ["code_challenge_method"] = "S256",
            ["access_type"] = "online",
            ["include_granted_scopes"] = "true",
            ["prompt"] = "consent"
        };

        var queryString = string.Join("&", query.Select(kvp => $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}"));
        return new Uri($"{AuthorizationEndpoint}?{queryString}");
    }

    private async Task<string?> ExchangeCodeForAccessTokenAsync(string authorizationCode, string codeVerifier, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = authorizationCode,
                ["client_id"] = configuration.ClientId,
                ["redirect_uri"] = configuration.RedirectUri,
                ["code_verifier"] = codeVerifier
            })
        };

        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

        return json.RootElement.TryGetProperty("access_token", out var accessToken)
            ? accessToken.GetString()
            : null;
    }

    private static string CreateCodeVerifier()
    {
        Span<byte> bytes = stackalloc byte[64];
        RandomNumberGenerator.Fill(bytes);
        return Base64UrlEncode(bytes);
    }

    private static string CreateCodeChallenge(string codeVerifier)
    {
        var bytes = Encoding.ASCII.GetBytes(codeVerifier);
        var hash = SHA256.HashData(bytes);
        return Base64UrlEncode(hash);
    }

    private static string Base64UrlEncode(ReadOnlySpan<byte> bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static bool TryGetValue(WebAuthenticatorResult result, string key, out string? value)
    {
        if (result.Properties.TryGetValue(key, out var directValue) && !string.IsNullOrWhiteSpace(directValue))
        {
            value = directValue;
            return true;
        }

        value = null;
        return false;
    }
}
