using SubscriptionTiger.Models;

namespace SubscriptionTiger.Services.Interfaces;

public interface IBankFileScanService
{
    Task<BankFileScanResult> ScanCsvAsync(Stream csvStream, string fileName, CancellationToken cancellationToken);
}
