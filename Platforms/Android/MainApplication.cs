using Android.App;
using Android.Runtime;
using Android.Util;

namespace TourGuideApp2
{
    [Application]
    public class MainApplication : MauiApplication
    {
        public MainApplication(IntPtr handle, JniHandleOwnership ownership)
            : base(handle, ownership)
        {
        }

        public override void OnCreate()
        {
            // Bắt buộc với Microsoft.Data.Sqlite + bundle_green trên nhiều máy — thiếu hay crash native khi mở DB.
            SQLitePCL.Batteries_V2.Init();

            TaskScheduler.UnobservedTaskException += (_, args) =>
            {
                try
                {
                    Log.Error("AmThucVinhKhanh", args.Exception?.ToString() ?? "UnobservedTaskException");
                }
                catch
                {
                    // bỏ qua
                }

                args.SetObserved();
            };

            base.OnCreate();
        }

        protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
    }
}
