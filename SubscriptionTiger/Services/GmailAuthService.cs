using SubscriptionTiger.Models;
using SubscriptionTiger.Services.Interfaces;

namespace SubscriptionTiger.Services;

public sealed class GmailAuthService : IGmailAuthService
{
    private const string MissingClientIdMessage = "Add a Google OAuth client ID before Gmail can be scanned.";
    private const string DisabledForInternalTestMessage = "Gmail connection is not enabled in this internal test build yet. You can still test SubscriptionTiger using sample/manual subscriptions while Google OAuth setup is completed.";
    private const string DisabledScanMode = "Gmail OAuth disabled for internal test: previous custom redirect URI subscriptiontiger://oauth2redirect was rejected by Google with Error 400 invalid_request.";

    private readonly GmailConfiguration configuration;

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

        cancellationToken.ThrowIfCancellationRequested();

        return new GmailAuthResult(
            IsConfigured: true,
            IsSuccess: false,
            AccessToken: null,
            ResultMessage: DisabledForInternalTestMessage,
            ScanMode: DisabledScanMode);
    }
}
