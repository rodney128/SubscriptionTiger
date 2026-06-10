using Microsoft.Extensions.Logging;
using SubscriptionTiger.Services;
using SubscriptionTiger.Services.Interfaces;

namespace SubscriptionTiger
{
    public static class MauiProgram
    {
        public static IServiceProvider? Services { get; private set; }

        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                });

            // Gmail setup required before real scan works:
            // 1) Create Google Cloud project and enable Gmail API.
            // 2) Configure OAuth consent screen and add test user.
            // 3) Create Android OAuth client ID.
            // 4) Use package name from csproj: com.farenoughnorth.subscriptiontiger
            // 5) Use this machine debug SHA-1.
            builder.Services.AddSingleton(
                new GmailConfiguration(
                    ClientId: "449735589472-shqeavauf9mrhn0o92khgif14s7hm9j0.apps.googleusercontent.com",
                    Scope: "https://www.googleapis.com/auth/gmail.readonly",
                    RedirectUri: "com.googleusercontent.apps.449735589472-shqeavauf9mrhn0o92khgif14s7hm9j0:/oauth2redirect"));

            builder.Services.AddSingleton<IGmailAuthService>(provider =>
                new GmailAuthService(
                    provider.GetRequiredService<GmailConfiguration>(),
                    new HttpClient(),
                    provider.GetRequiredService<DiagnosticsService>()));
            builder.Services.AddSingleton<IGmailScanService>(provider =>
                new GmailScanService(
                    provider.GetRequiredService<IGmailAuthService>(),
                    new HttpClient(),
                    provider.GetRequiredService<DiagnosticsService>()));
            builder.Services.AddSingleton<InMemorySubscriptionRepository>();
            builder.Services.AddSingleton<LocalSubscriptionStorageService>();
            builder.Services.AddSingleton<DiagnosticsService>();

#if DEBUG
    		builder.Logging.AddDebug();
#endif

            var app = builder.Build();
            Services = app.Services;
            return app;
        }
    }
}
