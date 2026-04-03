using TourGuideApp2.Models;
using TourGuideApp2.Services;

namespace TourGuideApp2.Pages;

public partial class HistoryPage : ContentPage
{
    private List<HistoryEntryViewItem> _items = [];

    public HistoryPage()
    {
        InitializeComponent();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _ = LoadHistoryAsync();
    }

    private async Task LoadHistoryAsync()
    {
        var entries = await HistoryLogService.GetAllAsync();
        BindStatistics(entries);
        _items = entries
            .OrderByDescending(x => x.Timestamp)
            .Select(x => new HistoryEntryViewItem
            {
                PlaceName = x.PlaceName,
                SourceLine = $"Nguồn: {x.Source} | Ngôn ngữ: {x.Language.ToUpperInvariant()}",
                TimeLine = $"Thời gian: {x.Timestamp:dd/MM/yyyy HH:mm:ss} | Độ dài: {FormatDurationShort(x.DurationSeconds)}"
            })
            .ToList();

        historyList.ItemsSource = _items;
        emptyStateLabel.IsVisible = _items.Count == 0;
    }

    private void BindStatistics(List<HistoryEntry> entries)
    {
        var total = entries.Count;
        var qrCount = entries.Count(x => string.Equals(x.Source, "QR", StringComparison.OrdinalIgnoreCase));
        var mapCount = entries.Count(x => string.Equals(x.Source, "Map", StringComparison.OrdinalIgnoreCase));
        var autoGeoCount = entries.Count(x => string.Equals(x.Source, "AutoGeo", StringComparison.OrdinalIgnoreCase));
        var busStopCount = entries.Count(x => string.Equals(x.Source, "BusStop", StringComparison.OrdinalIgnoreCase));

        totalCountLabel.Text = $"Tổng lượt nghe: {total}";
        sourceCountLabel.Text = $"Theo nguồn: QR {qrCount} | Map {mapCount} | AutoGeo {autoGeoCount} | BusStop {busStopCount}";
        sourceAvgLabel.Text = $"TB theo nguồn (giây): QR {FormatAverageBySource(entries, "QR")} | Map {FormatAverageBySource(entries, "Map")} | AutoGeo {FormatAverageBySource(entries, "AutoGeo")} | BusStop {FormatAverageBySource(entries, "BusStop")}";
        avgListenTimeLabel.Text = $"Thời gian nghe TB (thực tế): {FormatAverageSeconds(entries)}";
        var today = DateTime.Now.Date;
        var todayCount = entries.Count(x => x.Timestamp.Date == today);
        todayCountLabel.Text = $"Lượt nghe hôm nay: {todayCount}";

        var topLanguage = entries
            .GroupBy(x => (x.Language ?? "vi").Trim().ToLowerInvariant())
            .Select(g => new { Lang = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.Lang)
            .FirstOrDefault();
        topLanguageLabel.Text = topLanguage is null
            ? "Ngôn ngữ nghe nhiều nhất: -"
            : $"Ngôn ngữ nghe nhiều nhất: {NormalizeLanguage(topLanguage.Lang)} ({topLanguage.Count} lượt)";

        var topPlaces = HistoryLogService.GetTopPlacesByListenCount(entries, 5);
        var rows = topPlaces
            .Select((x, i) => TopPlaceRow.Create(i + 1, x.PlaceName, x.Count))
            .ToList();

        topPlacesList.ItemsSource = rows;
        var hasTop = rows.Count > 0;
        topPlacesEmptyLabel.IsVisible = !hasTop;
        topPlacesList.IsVisible = hasTop;
        topPlacesList.HeightRequest = hasTop ? rows.Count * 40 + 12 : 0;
    }

    private static string FormatAverageSeconds(List<HistoryEntry> entries)
    {
        var measuredDurations = entries
            .Where(x => x.DurationSeconds.HasValue && x.DurationSeconds.Value > 0)
            .Select(x => x.DurationSeconds!.Value)
            .ToList();

        if (measuredDurations.Count == 0)
            return "Chưa có dữ liệu";

        var avgSec = measuredDurations.Average();
        return $"{avgSec:0.#} giây/lượt";
    }

    private static string FormatAverageBySource(IEnumerable<HistoryEntry> entries, string source)
    {
        var values = entries
            .Where(x => string.Equals(x.Source, source, StringComparison.OrdinalIgnoreCase))
            .Where(x => x.DurationSeconds.HasValue && x.DurationSeconds.Value > 0)
            .Select(x => x.DurationSeconds!.Value)
            .ToList();

        return values.Count == 0 ? "-" : $"{values.Average():0.#}";
    }

    private static string FormatDurationShort(double? seconds)
    {
        return seconds.HasValue && seconds.Value > 0 ? $"{seconds.Value:0.#}s" : "-";
    }

    private static string NormalizeLanguage(string lang) => lang switch
    {
        "vi" => "VI",
        "en" => "EN",
        "zh" => "ZH",
        "ja" => "JA",
        _ => lang.ToUpperInvariant()
    };

    private async void OnRefreshClicked(object? sender, EventArgs e)
    {
        await LoadHistoryAsync();
    }

    private async void OnClearAllClicked(object? sender, EventArgs e)
    {
        var confirm = await DisplayAlertAsync("Xóa lịch sử", "Bạn có chắc muốn xóa toàn bộ lịch sử nghe/quét?", "Xóa", "Hủy");
        if (!confirm) return;

        await HistoryLogService.ClearAsync();
        await LoadHistoryAsync();
    }

    private class HistoryEntryViewItem
    {
        public string PlaceName { get; set; } = string.Empty;
        public string SourceLine { get; set; } = string.Empty;
        public string TimeLine { get; set; } = string.Empty;
    }
}