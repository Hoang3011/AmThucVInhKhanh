using TourGuideApp2.Services;

namespace TourGuideApp2;

public partial class RegisterPage : ContentPage
{
    public RegisterPage()
    {
        InitializeComponent();
    }

    private async void OnSubmitClicked(object? sender, EventArgs e)
    {
        var password = passwordEntry.Text ?? string.Empty;
        var confirm = confirmPasswordEntry.Text ?? string.Empty;
        if (!string.Equals(password, confirm, StringComparison.Ordinal))
        {
            statusLabel.Text = "Mật khẩu xác nhận chưa khớp.";
            return;
        }

        var result = await AuthService.RegisterAsync(
            fullNameEntry.Text ?? string.Empty,
            accountEntry.Text ?? string.Empty,
            password);
        statusLabel.Text = result.Message;
        if (!result.Success) return;

        await DisplayAlertAsync("Thành công", "Đăng ký thành công. Vui lòng đăng nhập.", "OK");
        await Navigation.PopModalAsync();
    }

    private async void OnCancelClicked(object? sender, EventArgs e)
    {
        await Navigation.PopModalAsync();
    }
}
