using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using TourGuideCMS.Services;

namespace TourGuideCMS.Pages.Customers;

[Authorize(Roles = "Admin")]
public class HeatmapModel : PageModel
{
    private readonly CustomerAccountRepository _repo;

    public HeatmapModel(CustomerAccountRepository repo) => _repo = repo;

    [BindProperty(SupportsGet = true)]
    public int Id { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? DeviceInstallId { get; set; }

    public CustomerUserRow? Customer { get; private set; }
    public DeviceRouteSnapshotRow? DeviceSnapshot { get; private set; }

    public string RoutePointsJson { get; private set; } = "[]";

    public int PointCount { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var raw = string.Empty;
        if (Id > 0)
        {
            Customer = await _repo.GetUserByIdAsync(Id);
            if (Customer is null)
                return NotFound();
            raw = await _repo.GetRouteSnapshotJsonAsync(Id) ?? "[]";
        }
        else if (!string.IsNullOrWhiteSpace(DeviceInstallId))
        {
            DeviceInstallId = DeviceInstallId.Trim();
            DeviceSnapshot = await _repo.GetDeviceRouteSnapshotAsync(DeviceInstallId);
            if (DeviceSnapshot is null)
                return NotFound();
            raw = await _repo.GetRouteSnapshotJsonByDeviceAsync(DeviceInstallId) ?? "[]";
            if (DeviceSnapshot.CustomerUserId is > 0)
                Customer = await _repo.GetUserByIdAsync(DeviceSnapshot.CustomerUserId.Value);
        }
        else
        {
            return RedirectToPage("Index");
        }

        RoutePointsJson = string.IsNullOrWhiteSpace(raw) ? "[]" : raw;
        try
        {
            using var doc = JsonDocument.Parse(RoutePointsJson);
            PointCount = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement.GetArrayLength() : 0;
        }
        catch
        {
            PointCount = 0;
            RoutePointsJson = "[]";
        }

        return Page();
    }
}
