using System.Collections.ObjectModel;
using QRCoder;
using TourGuideApp2.Services;

namespace TourGuideApp2;

public partial class QrDemoPage : ContentPage
{
    public ObservableCollection<QrDemoItem> QrItems { get; } = [];

    public QrDemoPage()
    {
        InitializeComponent();
        BindingContext = this;
        _ = BuildQrItemsAsync();
    }

    private async Task BuildQrItemsAsync()
    {
        var places = await PlaceApiService.GetPlacesAsync();
        QrItems.Clear();
        for (var i = 0; i < places.Count; i++)
        {
            var payload = $"app://poi?id={i}";
            var imageSource = GenerateQrImage(payload);
            QrItems.Add(new QrDemoItem
            {
                Name = $"{i}. {places[i].Name}",
                Payload = payload,
                QrImage = imageSource
            });
        }
    }

    private static ImageSource GenerateQrImage(string content)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(content, QRCodeGenerator.ECCLevel.Q);
        var qr = new PngByteQRCode(data);
        var bytes = qr.GetGraphic(8);

        return ImageSource.FromStream(() => new MemoryStream(bytes));
    }

    public class QrDemoItem
    {
        public string Name { get; set; } = string.Empty;
        public string Payload { get; set; } = string.Empty;
        public ImageSource QrImage { get; set; } = null!;
    }
}
