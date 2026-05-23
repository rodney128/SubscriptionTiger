namespace SubscriptionTiger.Models;

public sealed record ScanResultSummary(
    string SourceName,
    string ScanMode,
    int ItemsChecked,
    string ItemsCheckedLabel,
    int NewCandidatesFound,
    int DuplicatesSkipped,
    string ResultMessage,
    DateTime ScanTime);
