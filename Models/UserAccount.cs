namespace TourGuideApp2.Models;

public class UserAccount
{
    public int Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string PhoneOrEmail { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string PasswordSalt { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
