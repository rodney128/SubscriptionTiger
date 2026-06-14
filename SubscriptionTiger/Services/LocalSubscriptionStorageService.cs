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
    private readonly string ignoredFilePath;

    public LocalSubscriptionStorageService()
    {
        filePath = Path.Combine(FileSystem.AppDataDirectory, "confirmed-subscriptions.json");
        ignoredFilePath = Path.Combine(FileSystem.AppDataDirectory, "ignored-suspects.json");
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

    /// <summary>
    /// Loads the locally persisted signatures of suspects the user marked as "not a subscription".
    /// </summary>
    public async Task<IReadOnlyList<string>> LoadIgnoredSignaturesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(ignoredFilePath))
            {
                return Array.Empty<string>();
            }

            var json = await File.ReadAllTextAsync(ignoredFilePath, cancellationToken);
            if (string.IsNullOrWhiteSpace(json))
            {
                return Array.Empty<string>();
            }

            var signatures = JsonSerializer.Deserialize<List<string>>(json, SerializerOptions);
            if (signatures is null)
            {
                return Array.Empty<string>();
            }

            return signatures
                .Where(signature => !string.IsNullOrWhiteSpace(signature))
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
        catch (IOException)
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// Saves the signatures of ignored suspects to local app storage.
    /// </summary>
    public async Task SaveIgnoredSignaturesAsync(IEnumerable<string> signatures, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(signatures);

        var json = JsonSerializer.Serialize(signatures, SerializerOptions);
        await File.WriteAllTextAsync(ignoredFilePath, json, cancellationToken);
    }
}
