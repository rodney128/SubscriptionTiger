using SubscriptionTiger.Models;

namespace SubscriptionTiger.Services.Interfaces;

public interface IOutlookAuthService
{
    Task<OutlookAuthResult> AuthenticateAsync(CancellationToken cancellationToken);
}
