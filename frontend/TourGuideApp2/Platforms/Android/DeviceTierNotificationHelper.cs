using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.App;
using AndroidX.Core.Content;
using Microsoft.Maui.ApplicationModel;

namespace TourGuideApp2;

/// <summary>Thông báo hệ thống Android sau khi cài/mở app (kết quả giả lập kiểm tra cấu hình).</summary>
public static class DeviceTierNotificationHelper
{
    private const int NotificationId = 92001;
    private const string ChannelId = "device_config_demo";

    public static void TryNotifyInstallConfigCheck(int tier)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            try
            {
                var ctx = Platform.CurrentActivity ?? global::Android.App.Application.Context;
                if (ctx is not Context context)
                    return;

                var textLong = tier == 0
                    ? "Kết quả: 0 — Cấu hình mạnh (giả lập)."
                    : "Kết quả: 1 — Cấu hình yếu (giả lập).";

                if (OperatingSystem.IsAndroidVersionAtLeast(33)
                    && Platform.CurrentActivity is Activity activity)
                {
                    if (ContextCompat.CheckSelfPermission(activity, Android.Manifest.Permission.PostNotifications) != Permission.Granted)
                        ActivityCompat.RequestPermissions(activity, new[] { Android.Manifest.Permission.PostNotifications }, 992);
                }

                var nm = NotificationManagerCompat.From(context);

                if (OperatingSystem.IsAndroidVersionAtLeast(26))
                {
                    var mgr = context.GetSystemService(Context.NotificationService) as NotificationManager;
                    if (mgr?.GetNotificationChannel(ChannelId) is null)
                    {
                        var ch = new NotificationChannel(ChannelId, "Kiểm tra cấu hình (demo)", NotificationImportance.Default);
                        mgr?.CreateNotificationChannel(ch);
                    }
                }

                var smallIcon = context.ApplicationInfo?.Icon ?? 0;
                var builder = new NotificationCompat.Builder(context, ChannelId)
                    .SetContentTitle("AmThucVinhKhanh — kiểm tra cấu hình")
                    .SetContentText(textLong)
                    .SetStyle(new NotificationCompat.BigTextStyle().BigText(textLong))
                    .SetAutoCancel(true)
                    .SetPriority(NotificationCompat.PriorityDefault);

                if (smallIcon != 0)
                    builder.SetSmallIcon(smallIcon);
                else
                    builder.SetSmallIcon(global::Android.Resource.Drawable.IcDialogInfo);

                nm.Notify(NotificationId, builder.Build());
            }
            catch
            {
                // Không làm văng app nếu thiết bị chặn thông báo.
            }
        });
    }
}
