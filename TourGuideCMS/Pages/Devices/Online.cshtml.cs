using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using TourGuideCMS.Services;

namespace TourGuideCMS.Pages.Devices;

[Authorize(Roles = "Admin")]
public class OnlineModel : PageModel
{
    private readonly CustomerAccountRepository _repo;

    public OnlineModel(CustomerAccountRepository repo) => _repo = repo;

    /// <summary>Thiết bị vừa ping khi đang ở tab Bản đồ trong khoảng này được coi là online.</summary>
    public static TimeSpan OnlineWindow { get; } = TimeSpan.FromMinutes(2);

    public int OnlineWindowSeconds => (int)OnlineWindow.TotalSeconds;

    public IReadOnlyList<DevicePresenceRow> Devices { get; private set; } = Array.Empty<DevicePresenceRow>();

    public int OnlineCount { get; private set; }

    public int OfflineCount { get; private set; }

    public DateTime CutoffUtc { get; private set; }

    public bool IsRowOnlineOnMap(DevicePresenceRow d) => d.IsOnMapTab && d.LastSeenUtc >= CutoffUtc;

    public async Task OnGetAsync()
    {
        CutoffUtc = DateTime.UtcNow - OnlineWindow;
        Devices = await _repo.ListDevicePresenceAsync(500);
        OnlineCount = Devices.Count(IsRowOnlineOnMap);
        OfflineCount = Devices.Count - OnlineCount;
    }
}
