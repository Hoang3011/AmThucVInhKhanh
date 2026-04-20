using ZXing.Net.Maui;

namespace TourGuideApp2;

public partial class QrScannerPage : ContentPage
{
    private readonly Action<string> _onScanned;
    private bool _isHandled;
    private bool _cameraStarted;

    public QrScannerPage(Action<string> onScanned)
    {
        InitializeComponent();
        _onScanned = onScanned;
        cameraView.Options = new BarcodeReaderOptions
        {
            Formats = BarcodeFormat.QrCode,
            AutoRotate = true,
            Multiple = false
        };
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        StartCameraSafely();
    }

    private async void StartCameraSafely()
    {
        if (_cameraStarted) return;

        try
        {
            cameraView.IsDetecting = true;
            _cameraStarted = true;
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Không mở được camera", $"Bạn có thể nhập mã thủ công.\n{ex.Message}", "OK");
        }
    }

    private void CameraView_BarcodesDetected(object? sender, BarcodeDetectionEventArgs e)
    {
        if (_isHandled) return;

        try
        {
            var code = e.Results?.FirstOrDefault()?.Value;
            if (string.IsNullOrWhiteSpace(code)) return;

            _isHandled = true;
            cameraView.IsDetecting = false;

            // Đóng modal trước rồi mới chạy callback — tránh Map/WebView + PopModal chồng nhau (Android hay văng app).
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                try
                {
                    await Navigation.PopModalAsync();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"QrScanner PopModal: {ex}");
                }

                try
                {
                    _onScanned?.Invoke(code);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"QrScanner OnScanned: {ex}");
                }
            });
        }
        catch
        {
            _isHandled = false;
        }
    }

    private async void OnCancelClicked(object? sender, EventArgs e)
    {
        cameraView.IsDetecting = false;
        await Navigation.PopModalAsync();
    }

    private async void OnManualInputClicked(object? sender, EventArgs e)
    {
        cameraView.IsDetecting = false;
        var manual = await DisplayPromptAsync("Nhập mã QR", "Dán hoặc nhập mã QR (ví dụ: app://poi?id=0)", "OK", "Hủy");
        if (string.IsNullOrWhiteSpace(manual))
        {
            cameraView.IsDetecting = true;
            return;
        }

        _isHandled = true;
        try
        {
            await Navigation.PopModalAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"QrScanner manual PopModal: {ex}");
        }

        try
        {
            _onScanned?.Invoke(manual.Trim());
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"QrScanner manual OnScanned: {ex}");
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        cameraView.IsDetecting = false;
    }
}
