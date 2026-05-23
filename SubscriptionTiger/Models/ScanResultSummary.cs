namespace SubscriptionTiger.Models;

public sealed record ScanResultSummary(
    string SourceName,
    int ItemsChecked,
    string ItemsCheckedLabel,
    int NewCandidatesFound,
    int DuplicatesSkipped,
    DateTime ScanTime);
