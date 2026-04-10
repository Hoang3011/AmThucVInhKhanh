using System.Text.Json;
using TourGuideApp2.Models;

namespace TourGuideApp2.Services;

public static class HistoryLogService
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly string FilePath = Path.Combine(FileSystem.AppDataDirectory, "history-log.json");

    private static string GetCurrentAccountKey()
    {
        var raw = AuthService.CurrentUserAccount?.Trim().ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(raw))
            return raw;
        return "__guest__";
    }

    public static async Task<List<HistoryEntry>> GetForCurrentUserAsync()
    {
        await Gate.WaitAsync();
        try
        {
            if (!File.Exists(FilePath))
                return [];

            await using var stream = File.OpenRead(FilePath);
            var items = await JsonSerializer.DeserializeAsync<List<HistoryEntry>>(stream);
            var currentKey = GetCurrentAccountKey();
            return (items ?? [])
                .Where(x => string.Equals(
                    (x.AccountKey ?? string.Empty).Trim().ToLowerInvariant(),
                    currentKey,
                    StringComparison.Ordinal))
                .ToList();
        }
        finally
        {
            Gate.Release();
        }
    }

    public static async Task AddAsync(string placeName, string source, string language, double? durationSeconds = null)
    {
        if (string.IsNullOrWhiteSpace(placeName))
            return;

        await Gate.WaitAsync();
        try
        {
            List<HistoryEntry> items = [];
            if (File.Exists(FilePath))
            {
                await using var read = File.OpenRead(FilePath);
                items = await JsonSerializer.DeserializeAsync<List<HistoryEntry>>(read) ?? [];
            }

            var playedAt = DateTime.Now;
            items.Add(new HistoryEntry
            {
                PlaceName = placeName,
                Source = source,
                Language = language,
                Timestamp = playedAt,
                DurationSeconds = durationSeconds.HasValue ? Math.Round(durationSeconds.Value, 1) : null,
                AccountKey = GetCurrentAccountKey(),
                CustomerUserId = AuthService.GetCustomerIdForServerSync()
            });

            await using var write = File.Create(FilePath);
            await JsonSerializer.SerializeAsync(write, items);

            PlaySyncService.Enqueue(placeName, source, language, durationSeconds, playedAt);
        }
        finally
        {
            Gate.Release();
        }
    }

    public static async Task ClearAsync()
    {
        await Gate.WaitAsync();
        try
        {
            if (!File.Exists(FilePath))
                return;

            await using var read = File.OpenRead(FilePath);
            var items = await JsonSerializer.DeserializeAsync<List<HistoryEntry>>(read) ?? [];
            var currentKey = GetCurrentAccountKey();
            var remaining = items
                .Where(x => !string.Equals(
                    (x.AccountKey ?? string.Empty).Trim().ToLowerInvariant(),
                    currentKey,
                    StringComparison.Ordinal))
                .ToList();

            await using var write = File.Create(FilePath);
            await JsonSerializer.SerializeAsync(write, remaining);
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>Top địa điểm được nghe nhiều nhất (theo PlaceName trong log).</summary>
    public static List<(string PlaceName, int Count)> GetTopPlacesByListenCount(
        IReadOnlyList<HistoryEntry> entries,
        int top = 5)
    {
        return entries
            .Where(x => !string.IsNullOrWhiteSpace(x.PlaceName))
            .GroupBy(x => x.PlaceName.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => (PlaceName: g.Key, Count: g.Count()))
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.PlaceName, StringComparer.OrdinalIgnoreCase)
            .Take(top)
            .ToList();
    }
}

