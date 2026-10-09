using TourGuideApp2.Models;

namespace TourGuideApp2.Services;

public class SimulationService : ISimulationService
{
    private readonly Random _random = new();

    // Trung tâm khu Vĩnh Khánh (Quận 4) - bạn có thể chỉnh sau
    private const double CenterLat = 10.7625;
    private const double CenterLng = 106.705;

    public Task<List<SimulatedUserLocation>> GenerateSimulatedLocationsAsync(int numberOfUsers = 150)
    {
        var locations = new List<SimulatedUserLocation>();

        for (int i = 0; i < numberOfUsers; i++)
        {
            // Tạo vị trí ngẫu nhiên quanh khu Vĩnh Khánh (có thể cluster ở vài hotspot)
            double lat = CenterLat + (_random.NextDouble() - 0.5) * 0.008;   // ~800m
            double lng = CenterLng + (_random.NextDouble() - 0.5) * 0.008;

            // Intensity cao hơn ở giữa phố (giả lập người tập trung)
            double intensity = 0.4 + _random.NextDouble() * 0.6;

            locations.Add(new SimulatedUserLocation
            {
                Latitude = lat,
                Longitude = lng,
                Intensity = intensity
            });
        }

        return Task.FromResult(locations);
    }
}