using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TourGuideApp2.Models;
using TourGuideApp2.Services;

namespace TourGuideApp2.PageModels;

public partial class HeatmapPageModel : ObservableObject
{
    private readonly ISimulationService _simulationService;

    [ObservableProperty]
    private List<SimulatedUserLocation> locations = new();

    public HeatmapPageModel(ISimulationService simulationService)
    {
        _simulationService = simulationService;
    }

    [RelayCommand]
    public async Task GenerateNewDataAsync()
    {
        var routePoints = await RouteTrackService.GetPointsAsync();
        if (routePoints.Count >= 2)
        {
            // Giới hạn số điểm gửi WebView (Leaflet heat) để vẫn mượt.
            var ordered = routePoints.OrderBy(p => p.TimestampUtc).ToList();
            if (ordered.Count > 600)
                ordered = ordered.Skip(ordered.Count - 600).ToList();

            Locations = ordered
                .Select((p, i) => new SimulatedUserLocation
                {
                    Latitude = p.Latitude,
                    Longitude = p.Longitude,
                    Intensity = 0.42 + (i % 7) * 0.07
                })
                .ToList();
            return;
        }

        Locations = await _simulationService.GenerateSimulatedLocationsAsync(200);
    }
}