using SubscriptionTiger.Models;

namespace SubscriptionTiger.Services.Interfaces;

public interface IGmailScanService
{
    Task<GmailScanResult> ScanInboxAsync(CancellationToken cancellationToken);
}
