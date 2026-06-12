using SubscriptionTiger.Models;

namespace SubscriptionTiger.Services.Interfaces;

public interface IOtherEmailScanService
{
    Task<OtherEmailScanResult> ScanInboxAsync(OtherEmailImapSettings settings, CancellationToken cancellationToken);
}
