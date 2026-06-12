using SubscriptionTiger.Models;

namespace SubscriptionTiger.Services.Interfaces;

public interface IOutlookScanService
{
    Task<OutlookScanResult> ScanInboxAsync(CancellationToken cancellationToken);

    Task<EmailBodyContent?> GetMessageContentAsync(string messageId, CancellationToken cancellationToken);
}
