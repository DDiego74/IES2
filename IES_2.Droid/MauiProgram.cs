using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using SkiaSharp.Views.Maui.Controls.Hosting;

namespace IES_2.Droid
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .UseSkiaSharp()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                });

            LiveCharts.Configure(config =>
                config.AddSkiaSharp()
                      .AddDefaultMappers()
                      .AddLightTheme());

            builder.Services.AddSingleton<DiagnosticViewModel>();
            builder.Services.AddSingleton<AppShell>();
            builder.Services.AddTransient<MainPage>();
            builder.Services.AddTransient<DiagPage>();
            builder.Services.AddTransient<GraphPage>();

            return builder.Build();
        }
    }
}
