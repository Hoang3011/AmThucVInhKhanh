using System.Text.Json;
using Microsoft.Maui.Storage;

namespace TourGuideApp2.Services;

public sealed class RouteTrackPoint
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public DateTime TimestampUtc { get; set; }
    public string Source { get; set; } = string.Empty;
}

public static class RouteTrackService
{
    private const double MinDistanceMeters = 6.0;
    private static readonly TimeSpan MinTimeGap = TimeSpan.FromSeconds(4);
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    static RouteTrackService()
    {
        TryMigrateLegacySharedRouteFile();
    }

    /// <summary>Bản cũ <c>route_points.json</c> dùng chung mọi tài khoản — chuyển thành tuyến <c>guest</c> một lần.</summary>
    private static void TryMigrateLegacySharedRouteFile()
    {
        try
        {
            var legacy = Path.Combine(FileSystem.AppDataDirectory, "route_points.json");
            var guest = Path.Combine(FileSystem.AppDataDirectory, "route_track_guest.json");
            if (File.Exists(legacy) && !File.Exists(guest))
                File.Move(legacy, guest);
        }
        catch
        {
            // bỏ qua
        }
    }

    private static string GetRouteFilePath()
    {
        var owner = AuthService.GetRouteOwnerKey();
        var tag = string.IsNullOrEmpty(owner) ? "guest" : owner;
        foreach (var ch in Path.GetInvalidFileNameChars())
            tag = tag.Replace(ch, '_');
        return Path.Combine(FileSystem.AppDataDirectory, $"route_track_{tag}.json");
    }

    public static async Task<List<RouteTrackPoint>> GetPointsAsync()
    {
        await Gate.WaitAsync();
        try
        {
            return await ReadPointsUnsafeAsync();
        }
        finally
        {
            Gate.Release();
        }
    }

    public static async Task AppendPointAsync(double latitude, double longitude, string source)
    {
        var shouldSync = false;
        await Gate.WaitAsync();
        try
        {
            var points = await ReadPointsUnsafeAsync();
            var now = DateTime.UtcNow;

            if (points.Count > 0)
            {
                var last = points[^1];
                var dist = CalculateDistanceMeters(last.Latitude, last.Longitude, latitude, longitude);
                var dt = now - last.TimestampUtc;
                if (dist < MinDistanceMeters && dt < MinTimeGap)
                    return;
            }

            points.Add(new RouteTrackPoint
            {
                Latitude = latitude,
                Longitude = longitude,
                TimestampUtc = now,
                Source = source ?? string.Empty
            });

            await SavePointsUnsafeAsync(points);
            shouldSync = true;
        }
        finally
        {
            Gate.Release();
        }

        if (shouldSync)
            CustomerRouteSyncService.ScheduleUploadAfterLocalSave();
    }

    public static async Task ClearAsync()
    {
        await Gate.WaitAsync();
        try
        {
            var path = GetRouteFilePath();
            if (File.Exists(path))
                File.Delete(path);
        }
        finally
        {
            Gate.Release();
        }

        CustomerRouteSyncService.ScheduleUploadAfterLocalSave();
    }

    private static async Task<List<RouteTrackPoint>> ReadPointsUnsafeAsync()
    {
        try
        {
            var path = GetRouteFilePath();
            if (!File.Exists(path))
                return [];

            var json = await File.ReadAllTextAsync(path);
            if (string.IsNullOrWhiteSpace(json))
                return [];

            return JsonSerializer.Deserialize<List<RouteTrackPoint>>(json, JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static async Task SavePointsUnsafeAsync(List<RouteTrackPoint> points)
    {
        var path = GetRouteFilePath();
        var json = JsonSerializer.Serialize(points);
        await File.WriteAllTextAsync(path, json);
    }

    private static double CalculateDistanceMeters(double lat1, double lon1, double lat2, double lon2)
    {
        const double earthRadius = 6371000.0;
        var dLat = ToRadians(lat2 - lat1);
        var dLon = ToRadians(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return earthRadius * c;
    }

    private static double ToRadians(double deg) => deg * Math.PI / 180.0;
}
