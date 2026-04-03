using TourGuideApp2.Models;

namespace TourGuideApp2.Services;

public interface ISimulationService
{
    Task<List<SimulatedUserLocation>> GenerateSimulatedLocationsAsync(int numberOfUsers = 150);
}