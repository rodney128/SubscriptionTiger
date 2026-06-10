using SubscriptionTiger.Models;
using System.Diagnostics;

namespace SubscriptionTiger.Services;

public sealed class DiagnosticsService
{
    public SubscriptionSource? LastScanSource { get; private set; }

    public DateTime? LastScanTimeUtc { get; private set; }

    public string? LastEventCategory { get; private set; }

    public string? LastEventMessage { get; private set; }

    public DateTime? LastEventTimeUtc { get; private set; }

    public string GmailOAuthStatus { get; private set; } = "Idle";

    public void RecordScan(SubscriptionSource source)
    {
        LastScanSource = source;
        LastScanTimeUtc = DateTime.UtcNow;
    }

    public void RecordEvent(string category, string message)
    {
        LastEventCategory = category;
        LastEventMessage = message;
        LastEventTimeUtc = DateTime.UtcNow;
        Debug.WriteLine($"[{category}] {message}");
    }

    public void RecordGmailOAuthStatus(string status)
    {
        GmailOAuthStatus = status;
        RecordEvent("OAuthDiag", status);
    }

    public string GetLastScanSourceText()
    {
        return LastScanSource?.ToString() ?? "None";
    }

    public string GetLastScanTimeText()
    {
        return LastScanTimeUtc?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "Never";
    }
}
