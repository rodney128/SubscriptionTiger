namespace SubscriptionTiger.Models;

public sealed record BankFileScanResult(
    bool IsConfigured,
    string ScanMode,
    string FileType,
    int RowsChecked,
    int TransactionsParsed,
    int ParseErrors,
    DateTimeOffset? OldestTransactionDate,
    DateTimeOffset? NewestTransactionDate,
    IReadOnlyList<SubscriptionCandidate> Candidates,
    string ResultMessage,
    DateTime ScanTime);
