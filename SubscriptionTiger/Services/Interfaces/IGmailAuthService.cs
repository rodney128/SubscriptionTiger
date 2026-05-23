using SubscriptionTiger.Models;

namespace SubscriptionTiger.Services.Interfaces;

public interface IGmailAuthService
{
    Task<GmailAuthResult> AuthenticateAsync(CancellationToken cancellationToken);
}
