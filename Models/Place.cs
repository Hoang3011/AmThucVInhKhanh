namespace TourGuideApp2.Models;

public class Place
{
    /// <summary>Id ổn định của POI (từ DB/CMS). Không dùng index trong list để làm QR/sync.</summary>
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string Specialty { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;
    /// <summary>Link mở bản đồ ngoài (Google/OSM). Trống thì app dùng tọa độ để tạo link Google Maps.</summary>
    public string MapUrl { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string Description { get; set; } = string.Empty;

    // Text dùng để thuyết minh (TTS) theo từng ngôn ngữ.
    public string VietnameseAudioText { get; set; } = string.Empty;
    public string EnglishAudioText { get; set; } = string.Empty;
    public string ChineseAudioText { get; set; } = string.Empty;
    public string JapaneseAudioText { get; set; } = string.Empty;
    // Còn để tương thích dữ liệu cũ (không dùng cho bản tiếng Anh).
    public string KoreanAudioText { get; set; } = string.Empty;

    // Geofence theo từng POI.
    public double ActivationRadiusMeters { get; set; } = 35;
    public int Priority { get; set; } = 0;
}