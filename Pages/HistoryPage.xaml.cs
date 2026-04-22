using System.Diagnostics;
using Microsoft.Maui.ApplicationModel;
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
        HistoryLogService.PlaybackLogChanged -= OnPlaybackLogChanged;
        HistoryLogService.PlaybackLogChanged += OnPlaybackLogChanged;
        CustomerAppWarmSyncService.Schedule();
        _ = LoadHistoryAsync();
    }

    protected override void OnDisappearing()
    {
        HistoryLogService.PlaybackLogChanged -= OnPlaybackLogChanged;
        base.OnDisappearing();
    }

    private void OnPlaybackLogChanged()
    {
        _ = LoadHistoryAsync();
    }

    private async Task LoadHistoryAsync()
    {
        try
        {
            await LoadHistoryCoreAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"HistoryPage.LoadHistoryAsync: {ex}");
            try
            {
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    syncStatusLabel.Text =
                        "Đang hiển thị lịch sử trên máy bạn. Nếu vừa mở app, chờ vài giây rồi bấm Làm mới.";
                    syncStatusLabel.TextColor = Colors.DimGray;
                    emptyStateLabel.IsVisible = true;
                    historyList.ItemsSource = Array.Empty<HistoryEntryViewItem>();
                });
            }
            catch
            {
                // bỏ qua
            }
        }
    }

    private async Task LoadHistoryCoreAsync()
    {
        // === Tự động đồng bộ log đang chờ lên CMS (nếu CMS đang bật) ===
        await PlaySyncService.FlushPendingAsync().ConfigureAwait(false);

        // Lấy log cục bộ
        var local = await HistoryLogService.GetForCurrentUserAsync();

        // Thử lấy từ server (nếu CMS đang chạy)
        var fetch = await RemotePlayHistoryService.FetchForCurrentCustomerAsync();

        var serverList = fetch.Status == RemoteHistoryFetchStatus.Ok
            ? fetch.Items.ToList()
            : new List<HistoryEntry>();

        // Đẩy lại những log chỉ có cục bộ (để khớp với CMS)
        if (fetch.Status == RemoteHistoryFetchStatus.Ok
            && AuthService.GetCustomerIdForServerSync() is not null
            && local.Count > 0)
        {
            var pushed = false;
            foreach (var l in local)
            {
                if (!serverList.Any(s => IsNearDuplicate(s, l)))
                {
                    PlaySyncService.Enqueue(l.PlaceName, l.Source, l.Language, l.DurationSeconds, l.Timestamp);
                    pushed = true;
                }
            }

            if (pushed)
            {
                // Đợi một chút để CMS ghi dữ liệu xong
                await Task.Delay(800).ConfigureAwait(false);
                fetch = await RemotePlayHistoryService.FetchForCurrentCustomerAsync().ConfigureAwait(false);
                if (fetch.Status == RemoteHistoryFetchStatus.Ok)
                    serverList = fetch.Items.ToList();
            }
        }

        var entries = MergeHistoryForDisplay(serverList, local);

        // === Xây dựng thông báo đồng bộ rõ ràng hơn ===
        var syncHint = BuildSyncStatusText(fetch);

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            syncStatusLabel.Text = syncHint.Text;
            syncStatusLabel.TextColor = syncHint.Color;

            BindStatistics(entries);

            _items = entries
                .OrderByDescending(x => x.Timestamp)
                .Select(x => new HistoryEntryViewItem
                {
                    PlaceName = x.PlaceName,
                    SourceLine = $"Nguồn: {x.Source} | Ngôn ngữ: {(x.Language ?? "").ToUpperInvariant()}",
                    TimeLine = $"Thời gian: {x.Timestamp:dd/MM/yyyy HH:mm:ss} | Độ dài: {FormatDurationShort(x.DurationSeconds)}"
                })
                .ToList();

            historyList.ItemsSource = _items;
            emptyStateLabel.IsVisible = _items.Count == 0;
        });
    }


    private static (string Text, Color Color) BuildSyncStatusText(RemoteHistoryFetchResult fetch)
    {
        const string queuedHint =
            "Lượt nghe luôn được lưu trên máy và xếp hàng; khi web CMS chạy và mạng tới được máy chủ, app tự gửi — không cần mở web trước khi dùng app.";

        return fetch.Status switch
        {
            RemoteHistoryFetchStatus.Ok =>
                ($"Đồng bộ nền: đã ghép {fetch.Items.Count} lượt từ máy chủ với lịch sử trên máy bạn.",
                    Color.FromArgb("#2E7D32")),

            RemoteHistoryFetchStatus.SkippedNoCmsUrl =>
                ("Đồng bộ nền đang chạy: lịch sử chỉ trên máy bạn. Khi có URL trong Cài đặt, app sẽ tự ghép thêm dữ liệu từ web.",
                    Color.FromArgb("#1565C0")),

            RemoteHistoryFetchStatus.Unauthorized =>
                ("Đồng bộ nền: lịch sử trên máy vẫn đầy đủ. Nếu cần xem thêm trên web: mở Cài đặt và nhập «Khóa đồng bộ CMS» trùng MobileApiKey trên CMS (hoặc tắt khóa trên CMS).",
                    Color.FromArgb("#1565C0")),

            RemoteHistoryFetchStatus.Failed =>
                ("Đồng bộ nền luôn bật — " + queuedHint,
                    Color.FromArgb("#1565C0")),

            _ => ("Đồng bộ nền: " + queuedHint, Color.FromArgb("#1565C0"))
        };
    }

    /// <summary>Gộp máy chủ (ưu tiên) với file local, bỏ trùng gần giờ để khớp trang Lượt phát CMS.</summary>
    private static List<HistoryEntry> MergeHistoryForDisplay(IReadOnlyList<HistoryEntry> remote, List<HistoryEntry> local)
    {
        var merged = new List<HistoryEntry>();
        foreach (var r in remote)
            merged.Add(r);

        foreach (var l in local)
        {
            if (!merged.Any(m => IsNearDuplicate(m, l)))
                merged.Add(l);
        }

        return merged.OrderByDescending(x => x.Timestamp).ToList();
    }

    private static bool IsNearDuplicate(HistoryEntry a, HistoryEntry b)
    {
        if (!string.Equals(a.PlaceName?.Trim(), b.PlaceName?.Trim(), StringComparison.OrdinalIgnoreCase))
            return false;
        if (!string.Equals(a.Source?.Trim(), b.Source?.Trim(), StringComparison.OrdinalIgnoreCase))
            return false;
        if (!string.Equals((a.Language ?? "").Trim(), (b.Language ?? "").Trim(), StringComparison.OrdinalIgnoreCase))
            return false;

        var ua = a.Timestamp.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(a.Timestamp, DateTimeKind.Local).ToUniversalTime()
            : a.Timestamp.ToUniversalTime();
        var ub = b.Timestamp.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(b.Timestamp, DateTimeKind.Local).ToUniversalTime()
            : b.Timestamp.ToUniversalTime();
        // Trước đây 5s làm “dính” nhiều lượt nghe gần nhau thành một — tab Lịch sử mất chi tiết sau khi sync CMS.
        return Math.Abs((ua - ub).TotalSeconds) < 1.05;
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

    private class HistoryEntryViewItem
    {
        public string PlaceName { get; set; } = string.Empty;
        public string SourceLine { get; set; } = string.Empty;
        public string TimeLine { get; set; } = string.Empty;
    }
}