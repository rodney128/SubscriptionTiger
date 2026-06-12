using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.DataTransfer;

namespace SubscriptionTiger;

/// <summary>
/// Immutable data passed to <see cref="CancelHelpPage"/>. The caller decides which fields and
/// actions are available, so the page itself stays free of services, repository, or trust logic.
/// </summary>
public sealed record CancelHelpContext(
    string Vendor,
    string PriceText,
    string CycleText,
    string SourceText,
    string? ConfidenceText = null,
    string? Sender = null,
    string? LatestEmailDateText = null,
    string? RepeatEvidence = null,
    string? DetectionReason = null,
    string? SafeWebsiteUrl = null,
    Func<Task>? ViewSourceEmailAsync = null);

/// <summary>
/// Lightweight, self-service helper that makes it easier for a user to cancel a subscription.
/// It never cancels anything, sends email, clicks unsubscribe links, opens raw sender domains,
/// or parses links from email bodies. All actions are user-initiated.
/// </summary>
public sealed class CancelHelpPage : ContentPage
{
    private readonly CancelHelpContext context;

    public CancelHelpPage(CancelHelpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        this.context = context;

        Title = "Cancel Assistance";
        BackgroundColor = Color.FromArgb("#121212");
        Padding = new Thickness(12);

        var content = new VerticalStackLayout { Spacing = 14 };
        content.Children.Add(BuildHeader(context));
        content.Children.Add(BuildActions(context));

        var closeButton = new Button
        {
            Text = "Close",
            CornerRadius = 10,
            HeightRequest = 44,
            BackgroundColor = Color.FromArgb("#2E3440"),
            TextColor = Colors.White
        };
        closeButton.Clicked += async (_, _) => await Navigation.PopModalAsync();
        content.Children.Add(closeButton);

        Content = new ScrollView { Content = content };
    }

    private static View BuildHeader(CancelHelpContext context)
    {
        var stack = new VerticalStackLayout { Spacing = 2 };

        stack.Children.Add(CreateLabel(context.Vendor, 17, FontAttributes.Bold, "#F5C452"));
        stack.Children.Add(CreateLabel($"Price: {context.PriceText} | Cycle: {context.CycleText}", 12, FontAttributes.None, "#E0E0E0"));
        stack.Children.Add(CreateLabel($"Source: {context.SourceText}", 12, FontAttributes.None, "#E0E0E0"));

        AddOptional(stack, context.ConfidenceText, value => $"Confidence: {value}");
        AddOptional(stack, context.Sender, value => $"From: {value}");
        AddOptional(stack, context.LatestEmailDateText, value => $"Latest email: {value}");
        AddOptional(stack, context.RepeatEvidence, value => $"Repeat evidence: {value}");
        AddOptional(stack, context.DetectionReason, value => $"Reason: {value}");

        stack.Children.Add(CreateLabel(
            "This is a self-service helper. It does not cancel, unsubscribe, or contact anyone for you.",
            11,
            FontAttributes.Italic,
            "#9AA0A6"));

        return stack;
    }

    private View BuildActions(CancelHelpContext context)
    {
        var stack = new VerticalStackLayout { Spacing = 8 };

        var copyButton = CreateActionButton("Copy Cancellation Script", "#CFAF57", Colors.Black);
        copyButton.Clicked += OnCopyScriptClicked;
        stack.Children.Add(copyButton);

        var searchButton = CreateActionButton("Search Web", "#2E3440", Colors.White);
        searchButton.Clicked += OnWebSearchClicked;
        stack.Children.Add(searchButton);

        if (!string.IsNullOrWhiteSpace(context.SafeWebsiteUrl))
        {
            var websiteButton = CreateActionButton("Open Official Website", "#2E3440", Colors.White);
            websiteButton.Clicked += OnSafeWebsiteClicked;
            stack.Children.Add(websiteButton);
        }

        if (context.ViewSourceEmailAsync is not null)
        {
            var viewButton = CreateActionButton("View Source Email", "#2E3440", Colors.White);
            viewButton.Clicked += OnViewSourceEmailClicked;
            stack.Children.Add(viewButton);
        }

        return stack;
    }

    private async void OnCopyScriptClicked(object? sender, EventArgs e)
    {
        var script =
            $"Hi, I want to cancel my subscription/account for {context.Vendor}. " +
            "Please confirm cancellation and stop future billing.";

        await Clipboard.SetTextAsync(script);
        await DisplayAlert("Copied", "Cancellation message copied to clipboard.", "OK");
    }

    private async void OnWebSearchClicked(object? sender, EventArgs e)
    {
        var query = Uri.EscapeDataString($"cancel {context.Vendor} subscription");
        await OpenUrlAsync($"https://www.google.com/search?q={query}");
    }

    private async void OnSafeWebsiteClicked(object? sender, EventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(context.SafeWebsiteUrl))
        {
            await OpenUrlAsync(context.SafeWebsiteUrl);
        }
    }

    private async void OnViewSourceEmailClicked(object? sender, EventArgs e)
    {
        if (context.ViewSourceEmailAsync is not null)
        {
            await context.ViewSourceEmailAsync();
        }
    }

    private static async Task OpenUrlAsync(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return;
        }

        try
        {
            await Browser.Default.OpenAsync(uri, BrowserLaunchMode.SystemPreferred);
        }
        catch (Exception)
        {
            // Opening an external link is best-effort; ignore launch failures.
        }
    }

    private static void AddOptional(Layout stack, string? value, Func<string, string> format)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            stack.Children.Add(CreateLabel(format(value), 12, FontAttributes.None, "#E0E0E0"));
        }
    }

    private static Button CreateActionButton(string text, string backgroundHex, Color textColor)
    {
        return new Button
        {
            Text = text,
            CornerRadius = 10,
            HeightRequest = 44,
            BackgroundColor = Color.FromArgb(backgroundHex),
            TextColor = textColor
        };
    }

    private static Label CreateLabel(string text, double fontSize, FontAttributes attributes, string colorHex)
    {
        return new Label
        {
            Text = text,
            FontSize = fontSize,
            FontAttributes = attributes,
            TextColor = Color.FromArgb(colorHex)
        };
    }
}
