namespace TourGuideApp2.Services;

/// <summary>
/// Giả lập kiểm tra cấu hình thiết bị sau khi người dùng tải/cài app (demo).
/// Trả về ngẫu nhiên 0 (mạnh) hoặc 1 (yếu).
/// </summary>
public static class DeviceConfigSimulator
{
    /// <summary>Trả về 0 (cấu hình mạnh) hoặc 1 (cấu hình yếu), ngẫu nhiên.</summary>
    public static int SimulateRandomDeviceTier() => Random.Shared.Next(2);

    public static string DescribeTierVi(int tier) =>
        tier == 0 ? "Cấu hình mạnh (giả lập)" : "Cấu hình yếu (giả lập)";
}
