namespace SubscriptionTiger.Models;

public sealed record OtherEmailScanResult(
    bool IsConfigured,
    string ScanMode,
    int MessagesChecked,
    IReadOnlyList<SubscriptionCandidate> Candidates,
    string ResultMessage,
    DateTime ScanTime);
