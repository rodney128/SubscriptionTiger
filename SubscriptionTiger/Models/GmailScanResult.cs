using SubscriptionTiger.Models;

namespace SubscriptionTiger.Models;

public sealed record GmailScanResult(
    bool IsConfigured,
    string ScanMode,
    int MessagesChecked,
    IReadOnlyList<SubscriptionCandidate> Candidates,
    string? AccessToken,
    string ResultMessage,
    DateTime ScanTime);
