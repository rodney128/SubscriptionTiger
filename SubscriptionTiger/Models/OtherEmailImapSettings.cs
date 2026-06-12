namespace SubscriptionTiger.Models;

public sealed record OtherEmailImapSettings(
    string EmailAddress,
    string ImapServer,
    int Port,
    OtherEmailSecurityMode SecurityMode,
    string Username,
    string Password,
    int MaxMessages)
{
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(EmailAddress)
        && !string.IsNullOrWhiteSpace(ImapServer)
        && Port > 0
        && !string.IsNullOrWhiteSpace(Username)
        && !string.IsNullOrWhiteSpace(Password)
        && MaxMessages > 0;
}
