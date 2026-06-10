using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Util;
using Microsoft.Identity.Client;

namespace SubscriptionTiger;

[Activity(Exported = true, NoHistory = true, LaunchMode = LaunchMode.SingleTop)]
[IntentFilter(
    [Intent.ActionView],
    Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable],
    DataScheme = "msauth",
    DataHost = "com.farenoughnorth.subscriptiontiger",
    DataPathPrefix = "/2UQiJMnHLa6o3108s0CxJ15A1gY")]
public class MicrosoftAuthenticationActivity : BrowserTabActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        var intentAction = Intent?.Action ?? "(null)";
        var redirectArrived = Intent?.Data is not null;
        var redirectBase = redirectArrived
            ? $"{Intent?.Data?.Scheme}://{Intent?.Data?.Host}{Intent?.Data?.Path}"
            : "(none)";

        Log.Debug("OutlookAuth", $"MicrosoftAuthenticationActivity OnCreate action={intentAction} redirectArrived={redirectArrived} redirectBase={redirectBase}");
        base.OnCreate(savedInstanceState);
    }
}
