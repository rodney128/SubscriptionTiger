namespace SubscriptionTiger.Models;

public sealed record GmailAuthResult(
    bool IsConfigured,
    bool IsSuccess,
    string? AccessToken,
    string ResultMessage,
    string ScanMode);
