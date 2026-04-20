namespace TourGuideApp2.Models;

/// <summary>Payload JSON từ <c>GET /api/devices/presence</c> — cùng logic online như trang CMS /Devices/Online.</summary>
public sealed class DevicePresenceResponse
{
    public string? ServerUtc { get; set; }
    public int OnlineWindowSeconds { get; set; }
    public int OnlineCount { get; set; }
    public int OfflineCount { get; set; }
    public List<DevicePresenceItem>? Devices { get; set; }
}

public sealed class DevicePresenceItem
{
    public string? DeviceInstallId { get; set; }
    public string? LastSeenUtc { get; set; }
    public bool IsOnMapTab { get; set; }
    public string? Platform { get; set; }
    public string? AppVersion { get; set; }
    public bool IsOnlineOnMap { get; set; }
}
