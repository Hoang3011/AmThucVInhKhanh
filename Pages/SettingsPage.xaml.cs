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
        cmsMobileKeyEntry.Text = Preferences.Default.Get(PlaceApiService.CmsMobileApiKeyPreferenceKey, string.Empty) ?? string.Empty;
        listenPayPublicBaseEntry.Text = Preferences.Default.Get(PlaceApiService.CmsListenPayPublicBaseUrlKey, string.Empty) ?? string.Empty;
        effectiveUrlLabel.Text = $"URL hiệu lực: {PlaceApiService.GetEffectiveApiUrl()}";
        effectiveListenPayLabel.Text = $"Gốc QR /Listen/Pay: {PlaceApiService.GetCmsBaseUrlForListenPayLinks()}";
        statusLabel.Text = string.Empty;
    }

    private void OnSaveClicked(object? sender, EventArgs e)
    {
        var url = (poiApiUrlEntry.Text ?? string.Empty).Trim();
        Preferences.Default.Set(PlaceApiService.PoiApiUrlPreferenceKey, url);

        var key = (cmsMobileKeyEntry.Text ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(key))
            Preferences.Default.Remove(PlaceApiService.CmsMobileApiKeyPreferenceKey);
        else
            Preferences.Default.Set(PlaceApiService.CmsMobileApiKeyPreferenceKey, key);

        var listenBase = (listenPayPublicBaseEntry.Text ?? string.Empty).Trim().TrimEnd('/');
        if (string.IsNullOrEmpty(listenBase))
            Preferences.Default.Remove(PlaceApiService.CmsListenPayPublicBaseUrlKey);
        else
            Preferences.Default.Set(PlaceApiService.CmsListenPayPublicBaseUrlKey, listenBase);

        effectiveUrlLabel.Text = $"URL hiệu lực: {PlaceApiService.GetEffectiveApiUrl()}";
        effectiveListenPayLabel.Text = $"Gốc QR /Listen/Pay: {PlaceApiService.GetCmsBaseUrlForListenPayLinks()}";
        statusLabel.Text = "Đã lưu URL và khóa đồng bộ (nếu có).";
    }

    private void OnClearClicked(object? sender, EventArgs e)
    {
        Preferences.Default.Remove(PlaceApiService.PoiApiUrlPreferenceKey);
        Preferences.Default.Remove(PlaceApiService.CmsListenPayPublicBaseUrlKey);
        poiApiUrlEntry.Text = string.Empty;
        listenPayPublicBaseEntry.Text = string.Empty;
        effectiveUrlLabel.Text = $"URL hiệu lực: {PlaceApiService.GetEffectiveApiUrl()}";
        effectiveListenPayLabel.Text = $"Gốc QR /Listen/Pay: {PlaceApiService.GetCmsBaseUrlForListenPayLinks()}";
        statusLabel.Text = "Đã xóa URL đã lưu (Preferences).";
    }

    private void OnClearMobileKeyClicked(object? sender, EventArgs e)
    {
        Preferences.Default.Remove(PlaceApiService.CmsMobileApiKeyPreferenceKey);
        cmsMobileKeyEntry.Text = string.Empty;
        statusLabel.Text = "Đã xóa khóa đồng bộ CMS.";
    }

}

