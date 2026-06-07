using System.Text.Json;
using System.Text.Json.Serialization;
using SubscriptionTiger.Models;

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

        return subscriptions;
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
