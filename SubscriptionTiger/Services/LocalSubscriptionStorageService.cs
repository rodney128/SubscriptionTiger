using System.Text.Json;
using System.Text.Json.Serialization;
using SubscriptionTiger.Models;
using System.Globalization;

namespace SubscriptionTiger.Services;

public sealed class LocalSubscriptionStorageService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string filePath;

    public LocalSubscriptionStorageService()
    {
        filePath = Path.Combine(FileSystem.AppDataDirectory, "confirmed-subscriptions.json");
    }

    /// <summary>
    /// Loads locally persisted confirmed subscriptions.
    /// </summary>
    public async Task<IReadOnlyList<ConfirmedSubscription>> LoadConfirmedSubscriptionsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                return Array.Empty<ConfirmedSubscription>();
            }

            var json = await File.ReadAllTextAsync(filePath, cancellationToken);
            if (string.IsNullOrWhiteSpace(json))
            {
                return Array.Empty<ConfirmedSubscription>();
            }

            var subscriptions = JsonSerializer.Deserialize<List<ConfirmedSubscription>>(json, SerializerOptions);
            if (subscriptions is null)
            {
                return Array.Empty<ConfirmedSubscription>();
            }

            return SanitizeSubscriptions(subscriptions);
        }
        catch (JsonException)
        {
            return Array.Empty<ConfirmedSubscription>();
        }
        catch (IOException)
        {
            return Array.Empty<ConfirmedSubscription>();
        }
    }

    private static IReadOnlyList<ConfirmedSubscription> SanitizeSubscriptions(IEnumerable<ConfirmedSubscription> subscriptions)
    {
        var result = new List<ConfirmedSubscription>();
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var subscription in subscriptions)
        {
            if (string.IsNullOrWhiteSpace(subscription.Vendor))
            {
                continue;
            }

            if (!Enum.IsDefined(subscription.BillingCycle))
            {
                continue;
            }

            if (subscription.Price.HasValue && subscription.Price.Value <= 0)
            {
                continue;
            }

            var vendor = subscription.Vendor.Trim();
            var key = string.Create(
                CultureInfo.InvariantCulture,
                $"{vendor}|{subscription.Price?.ToString(CultureInfo.InvariantCulture) ?? string.Empty}|{subscription.BillingCycle}");

            if (!keys.Add(key))
            {
                continue;
            }

            var normalized = subscription with
            {
                Vendor = vendor,
                Id = subscription.Id == Guid.Empty ? Guid.NewGuid() : subscription.Id,
                RenewalDate = subscription.RenewalDate == default
                    ? DateTime.Today.AddMonths(subscription.BillingCycle == BillingCycle.Monthly ? 1 : 12)
                    : subscription.RenewalDate
            };

            result.Add(normalized);
        }

        return result;
    }

    /// <summary>
    /// Saves confirmed subscriptions to local app storage.
    /// </summary>
    public async Task SaveConfirmedSubscriptionsAsync(IEnumerable<ConfirmedSubscription> subscriptions, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscriptions);

        var json = JsonSerializer.Serialize(subscriptions, SerializerOptions);
        await File.WriteAllTextAsync(filePath, json, cancellationToken);
    }
}
