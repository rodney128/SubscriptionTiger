namespace SubscriptionTiger.Services;

public sealed record GmailConfiguration(
    string ClientId,
    string Scope,
    string RedirectUri)
{
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ClientId);
}
