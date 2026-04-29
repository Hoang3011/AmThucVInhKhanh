using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using TourGuideCMS.Models;
using TourGuideCMS.Services;

namespace TourGuideCMS.Pages.Places;

[Authorize(Roles = "Admin")]
public class HeatmapModel : PageModel
{
    private readonly PlaceRepository _places;
    private readonly CustomerAccountRepository _accounts;

    public HeatmapModel(PlaceRepository places, CustomerAccountRepository accounts)
    {
        _places = places;
        _accounts = accounts;
    }

    [BindProperty(SupportsGet = true)]
    public int Id { get; set; }

    public PlaceRow? Place { get; private set; }
    public string HeatPointsJson { get; private set; } = "[]";
    public double RadiusMeters { get; private set; }
    public int MatchedPointCount { get; private set; }
    public int MatchedDeviceCount { get; private set; }
    public int TotalPlays { get; private set; }
    public IReadOnlyList<PlayAggregateRow> PlayBySource { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync()
    {
        if (Id <= 0)
            return RedirectToPage("Index");

        Place = await _places.GetAsync(Id);
        if (Place is null)
            return NotFound();

        RadiusMeters = Math.Clamp(Place.ActivationRadiusMeters * 3, 50, 300);
        var routeRows = await _accounts.ListDeviceRouteJsonAsync(3000);
        var matches = new List<double[]>();
        var deviceSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in routeRows)
        {
            var pointsForDevice = 0;
            foreach (var pt in ParsePoints(row.PointsJson))
            {
                var d = DistanceMeters(Place.Latitude, Place.Longitude, pt.Latitude, pt.Longitude);
                if (d <= RadiusMeters)
                {
                    pointsForDevice++;
                    // lower distance => stronger intensity for heatmap
                    var intensity = Math.Max(0.25, 1.0 - (d / RadiusMeters));
                    matches.Add([pt.Latitude, pt.Longitude, intensity]);
                }
            }

            if (pointsForDevice > 0)
                deviceSet.Add(row.DeviceInstallId);
        }

        MatchedPointCount = matches.Count;
        MatchedDeviceCount = deviceSet.Count;
        HeatPointsJson = JsonSerializer.Serialize(matches);

        PlayBySource = await _accounts.GetAggregatesForPlaceAsync(Place.Name);
        TotalPlays = PlayBySource.Sum(x => x.Count);
        return Page();
    }

    private static IEnumerable<(double Latitude, double Longitude)> ParsePoints(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            yield break;

        JsonDocument? doc = null;
        try
        {
            doc = JsonDocument.Parse(raw);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                yield break;

            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (!TryGetCoordinate(item, "latitude", "Latitude", out var lat) ||
                    !TryGetCoordinate(item, "longitude", "Longitude", out var lng))
                {
                    continue;
                }

                yield return (lat, lng);
            }
        }
        finally
        {
            doc?.Dispose();
        }
    }

    private static bool TryGetCoordinate(JsonElement obj, string camelName, string pascalName, out double value)
    {
        value = 0;
        if (obj.TryGetProperty(camelName, out var c) && c.TryGetDouble(out value))
            return true;
        if (obj.TryGetProperty(pascalName, out var p) && p.TryGetDouble(out value))
            return true;
        return false;
    }

    private static double DistanceMeters(double lat1, double lon1, double lat2, double lon2)
    {
        const double earthRadius = 6371000;
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
