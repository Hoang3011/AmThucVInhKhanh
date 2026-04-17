using System;
using TourGuideApp2.Services;

namespace TourGuideApp2;

public partial class MainPage : ContentPage
{
    private bool _isLoginPasswordVisible;
    private static bool _hasPlayedAppWelcome;

    public MainPage()
    {
        InitializeComponent();
        RefreshAuthUi();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            RefreshAuthUi();
            _ = PlayAppWelcomeOnceAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"MainPage.OnAppearing: {ex}");
        }
    }

    private static async Task PlayAppWelcomeOnceAsync()
    {
        if (_hasPlayedAppWelcome)
            return;

        _hasPlayedAppWelcome = true;

        // Android: bỏ TTS chào tự động — nhiều máy crash native khi mở app (cài qua QR). UI vẫn có dòng chữ chào.
        // Thuyết minh POI / map / chỉ đường vẫn dùng NarrationQueue khi người dùng thao tác.
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
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"PlayAppWelcomeOnceAsync: {ex.Message}");
        }
    }

    private async void OnLoginClicked(object? sender, EventArgs e)
    {
        var result = await AuthService.LoginAsync(
            accountEntry.Text ?? string.Empty,
            passwordEntry.Text ?? string.Empty);

        authStatusLabel.Text = result.Message;
        if (!result.Success) return;

        passwordEntry.Text = string.Empty;
        RefreshAuthUi();
    }

    private async void OnOpenRegisterClicked(object? sender, EventArgs e)
    {
        await Navigation.PushModalAsync(new RegisterPage());
    }

    private void OnToggleLoginPasswordClicked(object? sender, EventArgs e)
    {
        _isLoginPasswordVisible = !_isLoginPasswordVisible;
        passwordEntry.IsPassword = !_isLoginPasswordVisible;
        toggleLoginPasswordButton.Text = _isLoginPasswordVisible ? "🙈" : "👁";
    }

    private void OnLogoutClicked(object? sender, EventArgs e)
    {
        AuthService.Logout();
        authStatusLabel.Text = "Đã đăng xuất.";
        RefreshAuthUi();
    }

    private void RefreshAuthUi()
    {
        var loggedIn = AuthService.IsLoggedIn;
        loggedOutPanel.IsVisible = !loggedIn;
        loggedInPanel.IsVisible = loggedIn;
        if (loggedIn)
        {
            var name = AuthService.CurrentUserName;
            welcomeUserLabel.Text = string.IsNullOrWhiteSpace(name)
                ? "Xin chào, khách hàng!"
                : $"Xin chào, {name}!";
            accountInfoLabel.Text = string.IsNullOrWhiteSpace(AuthService.CurrentUserAccount)
                ? "Tài khoản: -"
                : $"Tài khoản: {AuthService.CurrentUserAccount}";
            createdAtInfoLabel.Text = AuthService.CurrentUserCreatedAt is DateTime createdAt
                ? $"Ngày tạo: {createdAt:dd/MM/yyyy HH:mm}"
                : "Ngày tạo: -";
        }
        else
        {
            accountEntry.Text ??= string.Empty;
            passwordEntry.Text = string.Empty;
            accountInfoLabel.Text = "Tài khoản: -";
            createdAtInfoLabel.Text = "Ngày tạo: -";
        }
    }
}