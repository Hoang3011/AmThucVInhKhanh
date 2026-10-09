using Microsoft.Maui.Media;

namespace TourGuideApp2;

public partial class ProjectListPage : ContentPage
{
    const string IntroSpeechVi =
        "Phố Ẩm Thực Vĩnh Khánh là một trong những khu phố ẩm thực nổi tiếng tại Quận 4, Thành phố Hồ Chí Minh. "
        + "Nơi đây tập trung nhiều quán ốc, hải sản hấp dẫn, thu hút đông đảo du khách và người dân địa phương.";

    public ProjectListPage()
    {
        InitializeComponent();
    }

    async void OnIntroSpeakClicked(object sender, EventArgs e)
    {
        introSpeakButton.IsEnabled = false;
        try
        {
            Locale? vi = null;
            foreach (var loc in await TextToSpeech.Default.GetLocalesAsync())
            {
                if (loc.Language.StartsWith("vi", StringComparison.OrdinalIgnoreCase))
                {
                    vi = loc;
                    break;
                }
            }

            await TextToSpeech.Default.SpeakAsync(IntroSpeechVi,
                vi is null ? null : new SpeechOptions { Locale = vi });
        }
        finally
        {
            introSpeakButton.IsEnabled = true;
        }
    }
}