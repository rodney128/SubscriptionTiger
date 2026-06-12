using Microsoft.Identity.Client;
using Microsoft.Maui.ApplicationModel;
using SubscriptionTiger.Models;
using SubscriptionTiger.Services.Interfaces;
#if ANDROID
using Android.Util;
#endif

namespace SubscriptionTiger.Services;

public sealed class OutlookAuthService : IOutlookAuthService
{
    private readonly OutlookConfiguration configuration;
    private readonly DiagnosticsService diagnosticsService;
    private readonly IPublicClientApplication app;

    public OutlookAuthService(OutlookConfiguration configuration, DiagnosticsService diagnosticsService)
    {
        this.configuration = configuration;
        this.diagnosticsService = diagnosticsService;

        app = PublicClientApplicationBuilder
            .Create(configuration.ClientId)
            .WithAuthority(configuration.Authority)
            .WithRedirectUri(configuration.RedirectUri)
            .WithLogging((level, message, containsPii) =>
            {
                if (containsPii)
                {
                    return;
                }

                diagnosticsService.RecordEvent("OutlookMsal", $"{level}: {message}");
            },
            LogLevel.Verbose,
            enablePiiLogging: false,
            enableDefaultPlatformLogging: true)
            .Build();
    }

    public async Task<OutlookAuthResult> AuthenticateAsync(CancellationToken cancellationToken)
    {
        diagnosticsService.RecordEvent("OutlookAuth", "Outlook auth service started");
        diagnosticsService.RecordEvent("OutlookAuth", $"Configured redirect URI: {configuration.RedirectUri}");
#if ANDROID
        Log.Debug("OutlookAuth", $"Configured redirect URI: {configuration.RedirectUri}");
#endif

        if (!configuration.IsConfigured)
        {
            return new OutlookAuthResult(
                IsConfigured: false,
                IsSuccess: false,
                AccessToken: null,
                ResultMessage: "Microsoft Outlook configuration is incomplete.",
                ScanMode: "Microsoft OAuth setup required");
        }

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var accounts = await app.GetAccountsAsync().ConfigureAwait(false);
            diagnosticsService.RecordEvent("OutlookAuth", $"Cached account count: {accounts.Count()}");
            var account = accounts.FirstOrDefault();

            if (account is not null)
            {
                diagnosticsService.RecordEvent("OutlookScan", "Connecting to Outlook...");
                diagnosticsService.RecordEvent("OutlookAuth", "Attempting silent token acquisition");
                try
                {
                    var silentResult = await app
                        .AcquireTokenSilent(configuration.Scopes, account)
                        .ExecuteAsync(cancellationToken)
                        .ConfigureAwait(false);

                    diagnosticsService.RecordEvent("OutlookAuth", "Outlook silent auth succeeded");
                    diagnosticsService.RecordEvent("OutlookScan", "Outlook token acquired");
                    return new OutlookAuthResult(
                        IsConfigured: true,
                        IsSuccess: true,
                        AccessToken: silentResult.AccessToken,
                        ResultMessage: "Outlook connected (silent).",
                        ScanMode: "Microsoft OAuth token acquired (silent)");
                }
                catch (MsalUiRequiredException ex)
                {
                    diagnosticsService.RecordEvent("OutlookAuth", $"Silent auth requires interactive sign-in: {ex.ErrorCode}");
                }
                catch (MsalException ex)
                {
                    diagnosticsService.RecordEvent("OutlookAuth", $"Silent auth failed, will try interactive: {ex.GetType().Name}: {ex.ErrorCode}: {ex.Message}");
                }
            }
            else
            {
                diagnosticsService.RecordEvent("OutlookAuth", "No cached account found; interactive sign-in required");
                diagnosticsService.RecordEvent("OutlookScan", "Waiting for Microsoft sign-in...");
            }

            diagnosticsService.RecordEvent("OutlookAuth", "Preparing interactive token acquisition");

#if ANDROID
            var parentActivity = Platform.CurrentActivity;
            diagnosticsService.RecordEvent(
                "OutlookAuth",
                $"Parent activity {(parentActivity is null ? "is null" : $"type={parentActivity.Class?.CanonicalName}")}");

            var interactiveBuilder = app
                .AcquireTokenInteractive(configuration.Scopes)
                .WithPrompt(Prompt.SelectAccount)
                .WithParentActivityOrWindow(parentActivity ?? throw new InvalidOperationException("Current Android activity is unavailable for Microsoft sign-in."));
#else
            var interactiveBuilder = app
                .AcquireTokenInteractive(configuration.Scopes)
                .WithPrompt(Prompt.SelectAccount);
#endif

            diagnosticsService.RecordEvent("OutlookAuth", "Starting interactive auth request");
            diagnosticsService.RecordEvent("OutlookScan", "Waiting for Microsoft sign-in...");
            var interactiveResult = await interactiveBuilder
                .ExecuteAsync(cancellationToken)
                .ConfigureAwait(false);

            diagnosticsService.RecordEvent("OutlookAuth", "Outlook interactive auth succeeded");
            diagnosticsService.RecordEvent("OutlookScan", "Outlook token acquired");
            return new OutlookAuthResult(
                IsConfigured: true,
                IsSuccess: true,
                AccessToken: interactiveResult.AccessToken,
                ResultMessage: "Outlook connected.",
                ScanMode: "Microsoft OAuth token acquired (interactive)");
        }
        catch (OperationCanceledException)
        {
            diagnosticsService.RecordEvent("OutlookAuth", "Outlook sign-in canceled");
            return new OutlookAuthResult(
                IsConfigured: true,
                IsSuccess: false,
                AccessToken: null,
                ResultMessage: "Outlook sign-in was canceled.",
                ScanMode: "Microsoft OAuth canceled");
        }
        catch (MsalException ex)
        {
            diagnosticsService.RecordEvent("OutlookAuth", $"Outlook auth failed: {ex.GetType().Name}: {ex.ErrorCode}: {ex.Message}");
            return new OutlookAuthResult(
                IsConfigured: true,
                IsSuccess: false,
                AccessToken: null,
                ResultMessage: "Outlook token acquisition failed.",
                ScanMode: "Microsoft OAuth failed");
        }
        catch (InvalidOperationException ex)
        {
            diagnosticsService.RecordEvent("OutlookAuth", $"Outlook auth invalid state: {ex.Message}");
            return new OutlookAuthResult(
                IsConfigured: true,
                IsSuccess: false,
                AccessToken: null,
                ResultMessage: "Outlook sign-in could not start from the current app state.",
                ScanMode: "Microsoft OAuth failed");
        }
    }
}
