using Microsoft.Maui.Authentication;
using SubscriptionTiger.Models;
using SubscriptionTiger.Services.Interfaces;

namespace SubscriptionTiger.Services;

public sealed class GmailAuthService : IGmailAuthService
{
    private const string MissingClientIdMessage = "Add a Google OAuth client ID before Gmail can be scanned.";

    private readonly GmailConfiguration configuration;
    private string? accessToken;

    public GmailAuthService(GmailConfiguration configuration)
    {
        this.configuration = configuration;
    }

    public async Task<GmailAuthResult> AuthenticateAsync(CancellationToken cancellationToken)
    {
        if (!configuration.IsConfigured)
        {
            return new GmailAuthResult(
                IsConfigured: false,
                IsSuccess: false,
                AccessToken: null,
                ResultMessage: MissingClientIdMessage,
                ScanMode: "Google OAuth setup required");
        }

        if (!string.IsNullOrWhiteSpace(accessToken))
        {
            return new GmailAuthResult(
                IsConfigured: true,
                IsSuccess: true,
                AccessToken: accessToken,
                ResultMessage: "Gmail OAuth sign-in already active for this session.",
                ScanMode: "Real Gmail read-only scan");
        }

        try
        {
            var authorizationUri = BuildAuthorizationUri();
            var callbackUri = new Uri(configuration.RedirectUri, UriKind.Absolute);

            var authResult = await WebAuthenticator.Default.AuthenticateAsync(
                    authorizationUri,
                    callbackUri)
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);

            var token = authResult?.AccessToken;
            if (string.IsNullOrWhiteSpace(token)
                && authResult is not null
                && authResult.Properties.TryGetValue("access_token", out var fallbackToken))
            {
                token = fallbackToken;
            }

            if (string.IsNullOrWhiteSpace(token))
            {
                return new GmailAuthResult(
                    IsConfigured: true,
                    IsSuccess: false,
                    AccessToken: null,
                    ResultMessage: "Google sign-in completed but no access token was returned.",
                    ScanMode: "Google OAuth sign-in failed");
            }

            accessToken = token;

            // TODO: Store and refresh tokens securely for production use.
            return new GmailAuthResult(
                IsConfigured: true,
                IsSuccess: true,
                AccessToken: accessToken,
                ResultMessage: "Google OAuth sign-in succeeded.",
                ScanMode: "Real Gmail read-only scan");
        }
        catch (OperationCanceledException)
        {
            return new GmailAuthResult(
                IsConfigured: true,
                IsSuccess: false,
                AccessToken: null,
                ResultMessage: "Google sign-in was canceled.",
                ScanMode: "Google OAuth sign-in failed");
        }
        catch (Exception ex)
        {
            return new GmailAuthResult(
                IsConfigured: true,
                IsSuccess: false,
                AccessToken: null,
                ResultMessage: $"Google sign-in failed: {ex.Message}",
                ScanMode: "Google OAuth sign-in failed");
        }
    }

    private Uri BuildAuthorizationUri()
    {
        var encodedRedirect = Uri.EscapeDataString(configuration.RedirectUri);
        var encodedScope = Uri.EscapeDataString(configuration.Scope);
        var encodedClientId = Uri.EscapeDataString(configuration.ClientId);

        var url =
            $"https://accounts.google.com/o/oauth2/v2/auth?response_type=token&client_id={encodedClientId}&redirect_uri={encodedRedirect}&scope={encodedScope}&prompt=consent";

        return new Uri(url, UriKind.Absolute);
    }
}
