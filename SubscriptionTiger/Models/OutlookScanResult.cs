namespace SubscriptionTiger.Models;

public sealed record OutlookScanResult(
    bool IsConfigured,
    string ScanMode,
    int MessagesChecked,
    IReadOnlyList<SubscriptionCandidate> Candidates,
    string? AccessToken,
    string ResultMessage,
    DateTime ScanTime);
