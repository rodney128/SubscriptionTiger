using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Util;
using Microsoft.Maui.Authentication;

namespace SubscriptionTiger;

[Activity(NoHistory = true, Exported = true, LaunchMode = LaunchMode.SingleTop)]
[IntentFilter(
    [Intent.ActionView],
    Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable],
    DataScheme = CallbackScheme)]
public class WebAuthenticationCallbackActivity : WebAuthenticatorCallbackActivity
{
    public const string CallbackScheme = "com.googleusercontent.apps.449735589472-shqeavauf9mrhn0o92khgif14s7hm9j0";
    public const string CallbackPath = "/oauth2redirect";

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        var intentAction = Intent?.Action ?? "(null)";
        var dataScheme = Intent?.Data?.Scheme ?? "(none)";
        var dataHost = Intent?.Data?.Host ?? "(none)";
        var dataPath = Intent?.Data?.Path ?? "(none)";
        var matchesExpectedPath = string.Equals(dataPath, CallbackPath, StringComparison.OrdinalIgnoreCase);

        Log.Debug("OAuthDiag", $"Gmail callback activity reached. action={intentAction}; scheme={dataScheme}; host={dataHost}; path={dataPath}; pathMatch={matchesExpectedPath}");
        base.OnCreate(savedInstanceState);
    }
}
