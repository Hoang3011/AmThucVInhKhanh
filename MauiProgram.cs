using CommunityToolkit.Maui;  // Đảm bảo using này có
using Microsoft.Extensions.Logging;  // Bắt buộc cho LoggingBuilder
using ZXing.Net.Maui.Controls;
namespace TourGuideApp2;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();

        builder
            .UseMauiApp<App>()
            .UseMauiCommunityToolkit()  // Chain NGAY SAU UseMauiApp<App>(), viết liền không space trước ()
            .UseBarcodeReader()
                                        // .UseMauiMaps() nếu dùng map gốc sau này
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                // ...
            });

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}