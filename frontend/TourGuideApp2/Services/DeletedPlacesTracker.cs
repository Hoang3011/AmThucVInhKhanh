using System.Text.Json;
using Microsoft.Maui.Storage;
using TourGuideApp2.Models;

namespace TourGuideApp2.Services;

/// <summary>
/// Ghi nhận POI đã biến mất khỏi danh sách remote (so với cache trước) để khi offline không hiện lại từ DB bundle cũ.
/// </summary>
public static class DeletedPlacesTracker
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static string? _path;
    private static string FilePath => _path ??= Path.Combine(FileSystem.AppDataDirectory, "removed-place-ids.json");

    private sealed class Payload
    {
        public List<int> Ids { get; set; } = [];
    }

    public static async Task<HashSet<int>> LoadAsync()
    {
        await Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!File.Exists(FilePath))
                return [];
            await using var s = File.OpenRead(FilePath);
            var p = await JsonSerializer.DeserializeAsync<Payload>(s).ConfigureAwait(false);
            return (p?.Ids ?? []).Where(id => id > 0).ToHashSet();
        }
        catch
        {
            return [];
        }
        finally
        {
            Gate.Release();
        }
    }

    private static async Task SaveAsync(HashSet<int> ids)
    {
        await Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var payload = new Payload { Ids = ids.Where(id => id > 0).Distinct().OrderBy(x => x).ToList() };
            await using var s = File.Create(FilePath);
            await JsonSerializer.SerializeAsync(s, payload).ConfigureAwait(false);
        }
        catch
        {
            // best-effort
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>So cache POI trước vs danh sách mới từ server — id mất khỏi server → ẩn offline; id xuất hiện lại → bỏ ẩn.</summary>
    public static async Task ApplyRemoteDiffAsync(IReadOnlyList<Place> previous, IReadOnlyList<Place> current)
    {
        var removed = await LoadAsync().ConfigureAwait(false);
        var curIds = current.Where(p => p is not null && p.Id > 0).Select(p => p.Id).ToHashSet();

        foreach (var p in previous)
        {
            if (p is null || p.Id <= 0)
                continue;
            if (!curIds.Contains(p.Id))
                removed.Add(p.Id);
        }

        foreach (var id in curIds)
            removed.Remove(id);

        await SaveAsync(removed).ConfigureAwait(false);
    }

    public static async Task<List<Place>> FilterPlacesAsync(IReadOnlyList<Place> places)
    {
        if (places.Count == 0)
            return [];

        var removed = await LoadAsync().ConfigureAwait(false);
        if (removed.Count == 0)
            return places.ToList();

        return places.Where(p => p is not null && (p.Id <= 0 || !removed.Contains(p.Id))).ToList();
    }
}
