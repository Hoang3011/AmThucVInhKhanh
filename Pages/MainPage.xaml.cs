using TourGuideApp2.Services;

namespace TourGuideApp2;

public partial class MainPage : ContentPage
{
    public MainPage()
    {
        InitializeComponent();
        RefreshAuthUi();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        RefreshAuthUi();
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