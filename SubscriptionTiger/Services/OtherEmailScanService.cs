using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;
using MimeKit;
using SubscriptionTiger.Models;
using SubscriptionTiger.Services.Interfaces;

namespace SubscriptionTiger.Services;

public sealed class OtherEmailScanService : IOtherEmailScanService
{
    private readonly DiagnosticsService diagnosticsService;
    private readonly SubscriptionSignalAnalyzer signalAnalyzer;

    public OtherEmailScanService(
        DiagnosticsService diagnosticsService,
        SubscriptionSignalAnalyzer signalAnalyzer)
    {
        this.diagnosticsService = diagnosticsService;
        this.signalAnalyzer = signalAnalyzer;
    }

    /// <summary>
    /// Scans the IMAP inbox in read-only mode using temporary runtime credentials.
    /// </summary>
    public async Task<OtherEmailScanResult> ScanInboxAsync(OtherEmailImapSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (!settings.IsConfigured)
        {
            return new OtherEmailScanResult(
                IsConfigured: false,
                ScanMode: "Other Email IMAP scan",
                MessagesChecked: 0,
                Candidates: Array.Empty<SubscriptionCandidate>(),
                ResultMessage: "IMAP setup is incomplete.",
                ScanTime: DateTime.Now);
        }

        diagnosticsService.RecordEvent(
            "OtherEmailScan",
            $"IMAP scan started server={settings.ImapServer}; port={settings.Port}; security={settings.SecurityMode}; max={settings.MaxMessages}; readOnly=True");

        try
        {
            using var client = new ImapClient();
            var secureSocketOption = ToSecureSocketOptions(settings.SecurityMode);

            await client.ConnectAsync(settings.ImapServer, settings.Port, secureSocketOption, cancellationToken).ConfigureAwait(false);
            await client.AuthenticateAsync(settings.Username, settings.Password, cancellationToken).ConfigureAwait(false);

            var inbox = client.Inbox;
            await inbox.OpenAsync(FolderAccess.ReadOnly, cancellationToken).ConfigureAwait(false);

            var uids = await inbox.SearchAsync(SearchQuery.All, cancellationToken).ConfigureAwait(false);
            var recentUids = uids
                .OrderByDescending(x => x.Id)
                .Take(settings.MaxMessages)
                .ToList();

            var candidates = new List<SubscriptionCandidate>();
            var checkedCount = 0;

            foreach (var uid in recentUids)
            {
                cancellationToken.ThrowIfCancellationRequested();
                checkedCount++;

                var message = await inbox.GetMessageAsync(uid, cancellationToken).ConfigureAwait(false);
                var sender = GetSender(message);
                var subject = string.IsNullOrWhiteSpace(message.Subject) ? "(No Subject)" : message.Subject;
                var snippet = GetSnippet(message);

                if (!signalAnalyzer.TryAnalyze(
                        sender,
                        subject,
                        snippet,
                        out var confidence,
                        out var reason,
                        out var price,
                        out var billingCycle,
                        out var vendor))
                {
                    continue;
                }

                candidates.Add(new SubscriptionCandidate(
                    Guid.NewGuid(),
                    vendor,
                    price,
                    billingCycle,
                    confidence,
                    SubscriptionSource.OtherEmail,
                    reason,
                    SourceEmailSubject: subject,
                    SourceEmailSender: sender,
                    SourceEmailDate: message.Date != DateTimeOffset.MinValue ? message.Date : null,
                    SourceEmailSnippet: snippet));
            }

            await client.DisconnectAsync(true, cancellationToken).ConfigureAwait(false);

            return new OtherEmailScanResult(
                IsConfigured: true,
                ScanMode: "Other Email IMAP scan",
                MessagesChecked: checkedCount,
                Candidates: candidates,
                ResultMessage: "Other Email IMAP scan completed.",
                ScanTime: DateTime.Now);
        }
        catch (OperationCanceledException)
        {
            return new OtherEmailScanResult(
                IsConfigured: true,
                ScanMode: "Other Email IMAP scan",
                MessagesChecked: 0,
                Candidates: Array.Empty<SubscriptionCandidate>(),
                ResultMessage: "Other Email IMAP scan canceled.",
                ScanTime: DateTime.Now);
        }
        catch (Exception ex)
        {
            diagnosticsService.RecordEvent("OtherEmailScan", $"IMAP scan failed: {ex.GetType().Name}: {ex.Message}");
            return new OtherEmailScanResult(
                IsConfigured: true,
                ScanMode: "Other Email IMAP scan",
                MessagesChecked: 0,
                Candidates: Array.Empty<SubscriptionCandidate>(),
                ResultMessage: $"Other Email IMAP scan failed: {ex.Message}",
                ScanTime: DateTime.Now);
        }
    }

    private static SecureSocketOptions ToSecureSocketOptions(OtherEmailSecurityMode mode)
    {
        return mode switch
        {
            OtherEmailSecurityMode.SslTls => SecureSocketOptions.SslOnConnect,
            OtherEmailSecurityMode.StartTls => SecureSocketOptions.StartTls,
            _ => SecureSocketOptions.None
        };
    }

    private static string GetSender(MimeMessage message)
    {
        var mailbox = message.From.Mailboxes.FirstOrDefault();
        if (mailbox is null)
        {
            return "Unknown Sender";
        }

        if (!string.IsNullOrWhiteSpace(mailbox.Name) && !string.IsNullOrWhiteSpace(mailbox.Address))
        {
            return $"{mailbox.Name} <{mailbox.Address}>";
        }

        return mailbox.Address ?? mailbox.Name ?? "Unknown Sender";
    }

    private static string GetSnippet(MimeMessage message)
    {
        var body = message.TextBody;
        if (string.IsNullOrWhiteSpace(body))
        {
            body = message.HtmlBody;
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            return string.Empty;
        }

        var collapsed = body.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return collapsed.Length <= 280 ? collapsed : collapsed[..280];
    }
}
