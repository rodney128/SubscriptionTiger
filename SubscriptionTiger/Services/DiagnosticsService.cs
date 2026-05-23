using SubscriptionTiger.Models;

namespace SubscriptionTiger.Services;

public sealed class DiagnosticsService
{
    public SubscriptionSource? LastScanSource { get; private set; }

    public DateTime? LastScanTimeUtc { get; private set; }

    public void RecordScan(SubscriptionSource source)
    {
        LastScanSource = source;
        LastScanTimeUtc = DateTime.UtcNow;
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
