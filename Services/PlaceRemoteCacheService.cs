using System.Text.Json;
using Microsoft.Maui.Storage;
using TourGuideApp2.Models;

namespace TourGuideApp2.Services;

/// <summary>
/// Cache POI lần sync remote gần nhất để khi CMS/tunnel tắt vẫn không quay lại POI bundle cũ (đã xóa trên admin).
/// </summary>
public static class PlaceRemoteCacheService
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static string? _path;

    private static string CachePath => _path ??= Path.Combine(FileSystem.AppDataDirectory, "places-remote-cache.json");

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static async Task SaveAsync(List<Place> places)
    {
        if (places is null || places.Count == 0)
            return;

        await Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await using var s = File.Create(CachePath);
            await JsonSerializer.SerializeAsync(s, places, Json).ConfigureAwait(false);
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

    public static async Task<List<Place>> TryLoadAsync()
    {
        await Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!File.Exists(CachePath))
                return [];
            await using var s = File.OpenRead(CachePath);
            return await JsonSerializer.DeserializeAsync<List<Place>>(s, Json).ConfigureAwait(false) ?? [];
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
}

