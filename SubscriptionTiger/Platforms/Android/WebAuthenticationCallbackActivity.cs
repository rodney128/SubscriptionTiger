using Android.App;
using Android.Content;
using Android.Content.PM;
using Microsoft.Maui.Authentication;

namespace SubscriptionTiger;

[Activity(NoHistory = true, Exported = true, LaunchMode = LaunchMode.SingleTop)]
[IntentFilter(
    [Intent.ActionView],
    Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable],
    DataScheme = CallbackScheme,
    DataPathPrefix = CallbackPath)]
public class WebAuthenticationCallbackActivity : WebAuthenticatorCallbackActivity
{
    public const string CallbackScheme = "com.googleusercontent.apps.449735589472-shqeavauf9mrhn0o92khgif14s7hm9j0";
    public const string CallbackPath = "/oauth2redirect";
}
