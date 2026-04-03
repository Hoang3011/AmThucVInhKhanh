using Microsoft.Maui.Graphics;

namespace TourGuideApp2.Pages;

/// <summary>Hàng hiển thị trong Top địa điểm được nghe nhiều nhất (Lịch sử).</summary>
public sealed class TopPlaceRow
{
    public int Rank { get; set; }
    public string PlaceName { get; set; } = string.Empty;
    public int Count { get; set; }
    public string RankText => $"{Rank}.";
    public string CountText => $"{Count} lượt";

    public Color RankColor { get; set; } = Color.FromArgb("#1565C0");
    public Color CountColor { get; set; } = Color.FromArgb("#666666");

    public static TopPlaceRow Create(int rank, string placeName, int count)
    {
        var row = new TopPlaceRow
        {
            Rank = rank,
            PlaceName = placeName,
            Count = count
        };

        // Tên điểm dùng màu cố định trên HistoryPage (#212121) để đọc được trên nền kem khi theme Dark.
        (row.RankColor, row.CountColor) = rank switch
        {
            1 => (Color.FromArgb("#FFB300"), Color.FromArgb("#F57C00")),
            2 => (Color.FromArgb("#78909C"), Color.FromArgb("#546E7A")),
            3 => (Color.FromArgb("#BF6C2F"), Color.FromArgb("#8D6E63")),
            4 => (Color.FromArgb("#7E57C2"), Color.FromArgb("#9575CD")),
            _ => (Color.FromArgb("#00897B"), Color.FromArgb("#26A69A"))
        };

        return row;
    }
}
