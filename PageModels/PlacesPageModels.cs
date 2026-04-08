using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TourGuideApp2.Models;
using TourGuideApp2.Services;           // ← thêm
using System.Collections.ObjectModel;
using System.Threading.Tasks;

namespace TourGuideApp2.PageModels;

public partial class PlacesPageModel : ObservableObject
{
    [ObservableProperty]
    private ObservableCollection<Place> places = new();

    public PlacesPageModel()
    {
        _ = LoadPlacesAsync();   // fire-and-forget async
    }

    private async Task LoadPlacesAsync()
    {
        try
        {
            // Ưu tiên remote (nếu có cấu hình API)
            var remote = await PlaceApiService.TryGetRemotePlacesAsync();
            if (remote?.Count > 0)
            {
                places = new ObservableCollection<Place>(SanitizePlaces(remote));
                return;
            }

            // Load từ local DB (đã được liên kết từ CMS)
            var result = await PlaceLocalRepository.TryLoadAsync(forceRecopyFromPackage: false);

            if (result.Error == PlaceLocalRepository.LoadError.None)
            {
                places = new ObservableCollection<Place>(result.Places);
            }
            else
            {
                await Shell.Current.DisplayAlertAsync("Lỗi Database",
                    $"Không load được dữ liệu: {result.Error}\n\n{result.Message}", "OK");
                places.Clear();
            }
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlertAsync("Lỗi", $"Load Places thất bại: {ex.Message}", "OK");
            places.Clear();
        }
    }

    // Copy từ MapPage (để Sanitize giống hệt)
    private static List<Place> SanitizePlaces(List<Place> places)
    {
        foreach (var p in places)
        {
            if (p is null) continue;
            p.VietnameseAudioText = CleanupNarrationNoise(p.VietnameseAudioText);
            p.EnglishAudioText = CleanupNarrationNoise(p.EnglishAudioText);
            p.ChineseAudioText = CleanupNarrationNoise(p.ChineseAudioText);
            p.JapaneseAudioText = CleanupNarrationNoise(p.JapaneseAudioText);
        }
        return places;
    }

    private static string CleanupNarrationNoise(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var cleaned = System.Text.RegularExpressions.Regex.Replace(
            text.Trim(),
            @"^\s*(?:\d[\d\s\-_.,;:\|]*){3,}",
            string.Empty,
            System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        return cleaned.TrimStart();
    }
}