namespace SubscriptionTiger.Services;

public sealed record OutlookConfiguration(
    string ClientId,
    string Authority,
    string RedirectUri,
    IReadOnlyList<string> Scopes)
{
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ClientId)
        && !string.IsNullOrWhiteSpace(Authority)
        && !string.IsNullOrWhiteSpace(RedirectUri)
        && Scopes.Count > 0;
}
