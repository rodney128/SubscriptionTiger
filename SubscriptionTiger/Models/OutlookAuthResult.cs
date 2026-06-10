namespace SubscriptionTiger.Models;

public sealed record OutlookAuthResult(
    bool IsConfigured,
    bool IsSuccess,
    string? AccessToken,
    string ResultMessage,
    string ScanMode);
