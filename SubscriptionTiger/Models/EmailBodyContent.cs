namespace SubscriptionTiger.Models;

public sealed record EmailBodyContent(
    string? Html,
    string? PlainText,
    bool IsFullBody)
{
    public bool HasHtml => !string.IsNullOrWhiteSpace(Html);

    public bool HasPlainText => !string.IsNullOrWhiteSpace(PlainText);

    public bool HasContent => HasHtml || HasPlainText;
}
