using System.Text.Json.Serialization;

namespace TourGuideApp2.Models;

/// <summary>Payload JSON từ <c>GET /api/devices/presence</c> — cùng logic online như trang CMS /Devices/Online.</summary>
public sealed class DevicePresenceResponse
{
    [JsonPropertyName("serverUtc")]
    public string? ServerUtc { get; set; }

    [JsonPropertyName("onlineWindowSeconds")]
    public int OnlineWindowSeconds { get; set; }

    [JsonPropertyName("onlineCount")]
    public int OnlineCount { get; set; }

    [JsonPropertyName("offlineCount")]
    public int OfflineCount { get; set; }

    [JsonPropertyName("devices")]
    public List<DevicePresenceItem>? Devices { get; set; }
}

public sealed class DevicePresenceItem
{
    [JsonPropertyName("deviceInstallId")]
    public string? DeviceInstallId { get; set; }

    [JsonPropertyName("lastSeenUtc")]
    public string? LastSeenUtc { get; set; }

    [JsonPropertyName("isOnMapTab")]
    public bool IsOnMapTab { get; set; }

    [JsonPropertyName("platform")]
    public string? Platform { get; set; }

    [JsonPropertyName("appVersion")]
    public string? AppVersion { get; set; }

    [JsonPropertyName("isOnlineOnMap")]
    public bool IsOnlineOnMap { get; set; }
}
