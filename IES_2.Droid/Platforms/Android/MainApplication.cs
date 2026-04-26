using Android.App;
using Android.Runtime;

namespace IES_2.Droid
{
    [Application]
    public class MainApplication : MauiApplication
    {
        public MainApplication(IntPtr handle, JniHandleOwnership ownership)
            : base(handle, ownership)
        {
            // Catch unhandled exceptions on background threads so they are logged
            // before the process is terminated (Android kills the app silently otherwise).
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                var ex = e.ExceptionObject as Exception;
                AppLogger.LogError($"UNHANDLED EXCEPTION (IsTerminating={e.IsTerminating})", ex);
            };

            TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                AppLogger.LogError("UNOBSERVED TASK EXCEPTION", e.Exception);
                e.SetObserved(); // prevent process crash where possible
            };
        }

        protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
    }
}