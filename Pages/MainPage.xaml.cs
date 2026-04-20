using Microsoft.Maui.ApplicationModel;
using TourGuideApp2.Models;
using TourGuideApp2.Services;

namespace TourGuideApp2;

public partial class MainPage : ContentPage
{
    private static bool _hasPlayedAppWelcome;

    public MainPage()
    {
        InitializeComponent();
        try
        {
            // Tránh CollectionView ItemsSource = null trên một số máy Android (crash khi đo layout lần đầu).
            presenceSummaryLabel.Text = OperatingSystem.IsAndroid()
                ? "Bấm «Tải lại» bên dưới để đồng bộ danh sách thiết bị từ CMS."
                : string.Empty;
            presenceStatusLabel.Text = string.Empty;
            presenceCollection.ItemsSource = new List<string>
            {
                OperatingSystem.IsAndroid()
                    ? "Chưa tải — bấm nút xanh «Tải lại»."
                    : " "
            };
        }
        catch (System.Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"MainPage ctor: {ex}");
        }
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            _ = PlayAppWelcomeOnceAsync();
            // Android: không tự gọi API/CMS lúc mở app — tránh crash cold start sau cài APK; dùng nút «Tải lại».
            if (!OperatingSystem.IsAndroid())
                _ = DeferredLoadPresenceAsync();
        }
        catch (System.Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"MainPage.OnAppearing: {ex}");
        }
    }

    private async void OnPresenceRefreshClicked(object? sender, EventArgs e)
    {
        await LoadPresenceAsync();
    }

    private async Task DeferredLoadPresenceAsync()
    {
        try
        {
            // Android: lùi lâu hơn để tránh chồng tải với WebView tab Bản đồ khi user vừa mở app.
            await Task.Delay(OperatingSystem.IsAndroid() ? 1800 : 450);
            await LoadPresenceAsync();
        }
        catch (System.Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"DeferredLoadPresenceAsync: {ex}");
        }
    }

    private async Task LoadPresenceAsync()
    {
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            presenceSummaryLabel.Text = "Đang tải…";
            presenceStatusLabel.Text = string.Empty;
        });

        try
        {
            var data = await PlaceApiService.TryGetDevicePresenceAsync();
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                if (data is null)
                {
                    presenceSummaryLabel.Text = "Không lấy được danh sách từ CMS.";
                    presenceStatusLabel.Text =
                        "Kiểm tra URL CMS (QR / Cài đặt) và khóa X-Mobile-Key trùng appsettings CMS.";
                    presenceCollection.ItemsSource = null;
                    return;
                }

                presenceSummaryLabel.Text =
                    $"Đang online (tab Bản đồ): {data.OnlineCount}  ·  Offline: {data.OfflineCount}  ·  Cửa sổ: {data.OnlineWindowSeconds}s";
                presenceStatusLabel.Text = $"Máy chủ (UTC): {data.ServerUtc}";
                presenceCollection.ItemsSource = data.Devices is { Count: > 0 } rows
                    ? rows.Take(120).Select(FormatPresenceLine).ToList()
                    : new List<string> { "Chưa có thiết bị nào gửi heartbeat." };
            });
        }
        catch (System.Exception ex)
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                presenceSummaryLabel.Text = "Lỗi khi tải danh sách.";
                presenceStatusLabel.Text = ex.Message;
                presenceCollection.ItemsSource = null;
            });
        }
    }

    private static string FormatPresenceLine(DevicePresenceItem d)
    {
        var st = d.IsOnlineOnMap ? "● Online" : "○ Offline";
        var id = ShortId(d.DeviceInstallId);
        var plat = string.IsNullOrWhiteSpace(d.Platform) ? "?" : d.Platform;
        var ver = string.IsNullOrWhiteSpace(d.AppVersion) ? "?" : d.AppVersion;
        return $"{st}  {id}  ·  {plat}  ·  app {ver}  ·  ping {FormatUtc(d.LastSeenUtc)}";
    }

    private static string ShortId(string? id)
    {
        var s = (id ?? string.Empty).Trim();
        if (s.Length <= 14)
            return string.IsNullOrEmpty(s) ? "-" : s;
        return s[..6] + "…" + s[^6..];
    }

    private static string FormatUtc(string? iso)
    {
        if (string.IsNullOrWhiteSpace(iso))
            return "-";
        return System.DateTime.TryParse(iso, null, System.Globalization.DateTimeStyles.RoundtripKind, out var t)
            ? t.ToUniversalTime().ToString("MM/dd HH:mm") + " UTC"
            : iso;
    }

    private static async Task PlayAppWelcomeOnceAsync()
    {
        if (_hasPlayedAppWelcome)
            return;

        _hasPlayedAppWelcome = true;

        if (OperatingSystem.IsAndroid())
            return;

        try
        {
            await Task.Delay(350);
            _ = await NarrationQueueService.EnqueuePoiOrTtsAsync(
                -1,
                "vi",
                "Xin chào, chào mừng bạn đến với phố ẩm thực Vĩnh Khánh.");
        }
        catch (System.Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"PlayAppWelcomeOnceAsync: {ex.Message}");
        }
    }
}
