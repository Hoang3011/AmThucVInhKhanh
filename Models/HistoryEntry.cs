namespace TourGuideApp2.Models;

public class HistoryEntry
{
    public string PlaceName { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty; // QR / Map
    public string Language { get; set; } = "vi";
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public double? DurationSeconds { get; set; }
    public string? AccountKey { get; set; }
    public int? CustomerUserId { get; set; }
}

