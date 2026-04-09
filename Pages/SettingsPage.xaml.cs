using Microsoft.Maui.Storage;
using TourGuideApp2.Services;

namespace TourGuideApp2.Pages;

public partial class SettingsPage : ContentPage
{
    public SettingsPage()
    {
        InitializeComponent();
        LoadUi();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        LoadUi();
    }

    private void LoadUi()
    {
        var saved = Preferences.Default.Get(PlaceApiService.PoiApiUrlPreferenceKey, string.Empty) ?? string.Empty;
        poiApiUrlEntry.Text = saved;
        effectiveUrlLabel.Text = $"URL hiệu lực: {PlaceApiService.GetEffectiveApiUrl()}";
        statusLabel.Text = string.Empty;
    }

    private void OnSaveClicked(object? sender, EventArgs e)
    {
        var url = (poiApiUrlEntry.Text ?? string.Empty).Trim();
        Preferences.Default.Set(PlaceApiService.PoiApiUrlPreferenceKey, url);
        effectiveUrlLabel.Text = $"URL hiệu lực: {PlaceApiService.GetEffectiveApiUrl()}";
        statusLabel.Text = "Đã lưu.";
    }

    private void OnClearClicked(object? sender, EventArgs e)
    {
        Preferences.Default.Remove(PlaceApiService.PoiApiUrlPreferenceKey);
        poiApiUrlEntry.Text = string.Empty;
        effectiveUrlLabel.Text = $"URL hiệu lực: {PlaceApiService.GetEffectiveApiUrl()}";
        statusLabel.Text = "Đã xóa URL đã lưu (Preferences).";
    }

    private async void OnTestClicked(object? sender, EventArgs e)
    {
        try
        {
            var places = await PlaceApiService.TryGetRemotePlacesAsync();
            if (places is null)
            {
                statusLabel.Text = "Test thất bại: không tải được POI từ server (kiểm tra URL + mạng LAN).";
                effectiveUrlLabel.Text = $"URL hiệu lực: {PlaceApiService.GetEffectiveApiUrl()}";
                return;
            }

            statusLabel.Text = $"OK: tải được {places.Count} POI từ server.";
            effectiveUrlLabel.Text = $"URL hiệu lực: {PlaceApiService.GetEffectiveApiUrl()}";
        }
        catch (Exception ex)
        {
            statusLabel.Text = $"Test lỗi: {ex.Message}";
        }
    }
}

