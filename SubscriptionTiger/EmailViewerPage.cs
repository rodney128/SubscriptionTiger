using System.Globalization;
using Microsoft.Maui.ApplicationModel;
using SubscriptionTiger.Models;

namespace SubscriptionTiger;

public sealed class EmailViewerPage : ContentPage
{
    private bool initialHtmlLoaded;

    public EmailViewerPage(SubscriptionCandidate candidate, EmailBodyContent? body)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        Title = "Email Detail";
        BackgroundColor = Color.FromArgb("#121212");
        Padding = new Thickness(12);

        var header = BuildHeader(candidate, body);
        var bodyView = BuildBodyView(body);

        var closeButton = new Button
        {
            Text = "Close",
            CornerRadius = 10,
            HeightRequest = 44,
            BackgroundColor = Color.FromArgb("#2E3440"),
            TextColor = Colors.White
        };
        closeButton.Clicked += async (_, _) => await Navigation.PopModalAsync();

        var layout = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Star },
                new RowDefinition { Height = GridLength.Auto }
            },
            RowSpacing = 10
        };

        layout.Add(header, 0, 0);
        layout.Add(bodyView, 0, 1);
        layout.Add(closeButton, 0, 2);

        Content = layout;
    }

    private static View BuildHeader(SubscriptionCandidate candidate, EmailBodyContent? body)
    {
        var stack = new VerticalStackLayout { Spacing = 2 };

        var sourceText = candidate.Source switch
        {
            SubscriptionSource.BankFile => "Bank File",
            SubscriptionSource.OtherEmail => "Other Email",
            _ => candidate.Source.ToString()
        };

        stack.Children.Add(CreateHeaderLabel(candidate.Vendor, 17, FontAttributes.Bold, "#F5C452"));
        stack.Children.Add(CreateHeaderLabel($"Source: {sourceText}", 12, FontAttributes.None, "#E0E0E0"));

        if (!string.IsNullOrWhiteSpace(candidate.SourceEmailSender))
        {
            stack.Children.Add(CreateHeaderLabel($"From: {candidate.SourceEmailSender}", 12, FontAttributes.None, "#E0E0E0"));
        }

        if (!string.IsNullOrWhiteSpace(candidate.SourceEmailSubject))
        {
            stack.Children.Add(CreateHeaderLabel($"Subject: {candidate.SourceEmailSubject}", 12, FontAttributes.None, "#E0E0E0"));
        }

        if (candidate.SourceEmailDate.HasValue)
        {
            var dateText = candidate.SourceEmailDate.Value.LocalDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture);
            stack.Children.Add(CreateHeaderLabel($"Date: {dateText}", 12, FontAttributes.None, "#E0E0E0"));
        }

        if (!string.IsNullOrWhiteSpace(candidate.DetectionReason))
        {
            stack.Children.Add(CreateHeaderLabel($"Reason: {candidate.DetectionReason}", 12, FontAttributes.None, "#E0E0E0"));
        }

        stack.Children.Add(CreateHeaderLabel($"Confidence: {candidate.ConfidenceScore}", 12, FontAttributes.None, "#E0E0E0"));

        if (body is null || !body.IsFullBody)
        {
            stack.Children.Add(CreateHeaderLabel("Full email could not be loaded. Showing available evidence.", 11, FontAttributes.Italic, "#F0A0A0"));
        }

        return stack;
    }

    private View BuildBodyView(EmailBodyContent? body)
    {
        if (body is not null && body.HasHtml)
        {
            var webView = new WebView
            {
                Source = new HtmlWebViewSource { Html = WrapHtml(body.Html!) }
            };

            webView.Navigating += OnWebViewNavigating;
            return webView;
        }

        var plainText = body?.PlainText;
        if (string.IsNullOrWhiteSpace(plainText))
        {
            plainText = "No email content available.";
        }

        return new ScrollView
        {
            Content = new Label
            {
                Text = plainText,
                TextColor = Color.FromArgb("#FFFFFF"),
                FontSize = 14
            }
        };
    }

    private async void OnWebViewNavigating(object? sender, WebNavigatingEventArgs e)
    {
        if (!initialHtmlLoaded)
        {
            initialHtmlLoaded = true;
            return;
        }

        if (Uri.TryCreate(e.Url, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            e.Cancel = true;
            try
            {
                await Browser.Default.OpenAsync(uri, BrowserLaunchMode.SystemPreferred);
            }
            catch (Exception)
            {
                // Opening an external link is best-effort; ignore launch failures.
            }
        }
    }

    private static string WrapHtml(string innerHtml)
    {
        return "<!DOCTYPE html><html><head><meta charset=\"utf-8\">"
            + "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">"
            + "<style>body{font-family:sans-serif;margin:12px;word-wrap:break-word;}img{max-width:100%;height:auto;}</style>"
            + "</head><body>"
            + innerHtml
            + "</body></html>";
    }

    private static Label CreateHeaderLabel(string text, double fontSize, FontAttributes attributes, string colorHex)
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
