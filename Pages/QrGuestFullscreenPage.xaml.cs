using QRCoder;

namespace TourGuideApp2;

public partial class QrGuestFullscreenPage : ContentPage
{
    private readonly string _placeName;
    private readonly string _payload;

    public QrGuestFullscreenPage(string placeName, string payload)
    {
        InitializeComponent();
        _placeName = placeName;
        _payload = payload;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        lblTitle.Text = _placeName;
        lblPayload.Text = _payload;
        imgQrLarge.Source = GenerateQrImageLarge(_payload);
    }

    private static ImageSource GenerateQrImageLarge(string payload)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.Q);
        var qr = new PngByteQRCode(data);
        // Module lớn hơn để khách quét từ màn hình dễ hơn
        var bytes = qr.GetGraphic(14, drawQuietZones: true);
        return ImageSource.FromStream(() => new MemoryStream(bytes));
    }

    private async void OnCloseClicked(object? sender, EventArgs e)
    {
        await Navigation.PopModalAsync();
    }
}
