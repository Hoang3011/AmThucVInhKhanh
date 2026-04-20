namespace TourGuideCMS;

/// <summary>Link phụ cho trang <c>/install/launch</c> (mở APK trong Chrome trên Android).</summary>
public static class InstallLaunchLinks
{
    /// <summary>Intent mở URL bằng Chrome — nếu không hợp lệ thì trả về URL gốc.</summary>
    public static string ChromeViewIntentOrFallback(string absoluteUrl)
    {
        if (!Uri.TryCreate(absoluteUrl, UriKind.Absolute, out var u))
            return absoluteUrl;
        var sch = u.Scheme;
        if (sch != "http" && sch != "https")
            return absoluteUrl;
        var pq = string.IsNullOrEmpty(u.PathAndQuery) ? "/" : u.PathAndQuery;
        return $"intent://{u.Authority}{pq}#Intent;scheme={sch};package=com.android.chrome;action=android.intent.action.VIEW;end";
    }
}
