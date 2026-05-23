using SubscriptionTiger.Models;
using SubscriptionTiger.Services;
using System.Globalization;

namespace SubscriptionTiger;

public partial class MainPage : ContentPage
{
    private readonly InMemorySubscriptionRepository repository = new();
    private readonly SubscriptionDetectionService detectionService = new();
    private readonly DiagnosticsService diagnosticsService = new();
    private ScanResultSummary? lastScanResult;

    public MainPage()
    {
        InitializeComponent();
        RefreshUi();
    }

    private void OnScanGmailClicked(object? sender, EventArgs e)
    {
        AddDetectedCandidates(SubscriptionSource.Gmail, 25, "Messages checked");
    }

    private void OnScanOutlookClicked(object? sender, EventArgs e)
    {
        AddDetectedCandidates(SubscriptionSource.Outlook, 18, "Messages checked");
    }

    private void OnScanOtherEmailClicked(object? sender, EventArgs e)
    {
        AddDetectedCandidates(SubscriptionSource.OtherEmail, 12, "Messages checked");
    }

    private void OnScanBankFileClicked(object? sender, EventArgs e)
    {
        AddDetectedCandidates(SubscriptionSource.BankFile, 40, "Transactions checked");
    }

    private void OnAddSampleManualClicked(object? sender, EventArgs e)
    {
        repository.AddManualSample();
        diagnosticsService.RecordScan(SubscriptionSource.Manual);
        RefreshUi();
    }

    private void OnSaveAsSubscriptionClicked(object? sender, EventArgs e)
    {
        if (sender is not Button button || button.CommandParameter is not string rawId || !Guid.TryParse(rawId, out var id))
        {
            return;
        }

        repository.SaveCandidate(id);
        RefreshUi();
    }

    private void OnDismissClicked(object? sender, EventArgs e)
    {
        if (sender is not Button button || button.CommandParameter is not string rawId || !Guid.TryParse(rawId, out var id))
        {
            return;
        }

        repository.DismissCandidate(id);
        RefreshUi();
    }

    private void AddDetectedCandidates(SubscriptionSource source, int itemsChecked, string itemsCheckedLabel)
    {
        var candidates = detectionService.Scan(source);
        var addResult = repository.AddCandidates(candidates);
        diagnosticsService.RecordScan(source);
        lastScanResult = new ScanResultSummary(
            GetScanSourceDisplayName(source),
            itemsChecked,
            itemsCheckedLabel,
            addResult.AddedCount,
            addResult.DuplicateCount,
            DateTime.Now);

        RefreshUi();
    }

    private void RefreshUi()
    {
        BuildSuspectedSection();
        BuildConfirmedSection();

        SuspectedCountValue.Text = repository.SuspectedCandidates.Count.ToString(CultureInfo.InvariantCulture);
        ConfirmedCountValue.Text = repository.ConfirmedSubscriptions.Count.ToString(CultureInfo.InvariantCulture);
        LastScanSourceValue.Text = diagnosticsService.GetLastScanSourceText();
        LastScanTimeValue.Text = diagnosticsService.GetLastScanTimeText();

        UpdateLastScanSummaryCard();
    }

    private void UpdateLastScanSummaryCard()
    {
        if (lastScanResult is null)
        {
            LastScanResultEmptyState.IsVisible = true;
            LastScanResultGrid.IsVisible = false;
            return;
        }

        LastScanResultEmptyState.IsVisible = false;
        LastScanResultGrid.IsVisible = true;

        ScanSummarySourceValue.Text = lastScanResult.SourceName;
        ScanSummaryItemsCheckedLabel.Text = $"{lastScanResult.ItemsCheckedLabel}:";
        ScanSummaryItemsCheckedValue.Text = lastScanResult.ItemsChecked.ToString(CultureInfo.InvariantCulture);
        ScanSummaryNewCandidatesValue.Text = lastScanResult.NewCandidatesFound.ToString(CultureInfo.InvariantCulture);
        ScanSummaryDuplicatesValue.Text = lastScanResult.DuplicatesSkipped.ToString(CultureInfo.InvariantCulture);
        ScanSummaryTimeValue.Text = lastScanResult.ScanTime.ToString("yyyy-MM-dd HH:mm:ss");
    }

    private static string GetScanSourceDisplayName(SubscriptionSource source)
    {
        return source switch
        {
            SubscriptionSource.OtherEmail => "Other Email",
            SubscriptionSource.BankFile => "Bank File",
            _ => source.ToString()
        };
    }

    private void BuildSuspectedSection()
    {
        SuspectedContainer.Children.Clear();

        if (repository.SuspectedCandidates.Count == 0)
        {
            SuspectedContainer.Children.Add(CreateEmptyStateLabel("No suspected subscriptions yet. Run a scan to populate results."));
            return;
        }

        foreach (var candidate in repository.SuspectedCandidates)
        {
            SuspectedContainer.Children.Add(CreateSuspectedCard(candidate));
        }
    }

    private void BuildConfirmedSection()
    {
        ConfirmedContainer.Children.Clear();

        if (repository.ConfirmedSubscriptions.Count == 0)
        {
            ConfirmedContainer.Children.Add(CreateEmptyStateLabel("No confirmed subscriptions yet. Save a suspected item or add a manual sample."));
            return;
        }

        foreach (var subscription in repository.ConfirmedSubscriptions)
        {
            ConfirmedContainer.Children.Add(CreateConfirmedCard(subscription));
        }
    }

    private View CreateSuspectedCard(SubscriptionCandidate candidate)
    {
        var card = CreateCardContainer();
        var stack = new VerticalStackLayout { Spacing = 6 };

        stack.Children.Add(CreateTitleLabel(candidate.Vendor));
        stack.Children.Add(CreateDetailLabel($"Price: {candidate.Price:C} | Cycle: {candidate.BillingCycle}"));
        stack.Children.Add(CreateDetailLabel($"Confidence: {candidate.ConfidenceScore}% | Source: {candidate.Source}"));
        stack.Children.Add(CreateDetailLabel($"Reason: {candidate.DetectionReason}"));

        var actions = new HorizontalStackLayout { Spacing = 10 };

        var saveButton = new Button
        {
            Text = "Save as Subscription",
            BackgroundColor = Color.FromArgb("#CFAF57"),
            TextColor = Colors.Black,
            CornerRadius = 10,
            FontAttributes = FontAttributes.Bold,
            CommandParameter = candidate.Id.ToString()
        };
        saveButton.Clicked += OnSaveAsSubscriptionClicked;

        var dismissButton = new Button
        {
            Text = "Dismiss",
            BackgroundColor = Color.FromArgb("#2E3440"),
            TextColor = Colors.White,
            CornerRadius = 10,
            CommandParameter = candidate.Id.ToString()
        };
        dismissButton.Clicked += OnDismissClicked;

        actions.Children.Add(saveButton);
        actions.Children.Add(dismissButton);
        stack.Children.Add(actions);

        card.Content = stack;
        return card;
    }

    private View CreateConfirmedCard(ConfirmedSubscription subscription)
    {
        var card = CreateCardContainer();
        var stack = new VerticalStackLayout { Spacing = 6 };

        stack.Children.Add(CreateTitleLabel(subscription.Vendor));
        stack.Children.Add(CreateDetailLabel($"Price: {subscription.Price:C} | Cycle: {subscription.BillingCycle}"));
        stack.Children.Add(CreateDetailLabel($"Renewal: {subscription.RenewalDate:yyyy-MM-dd} | Status: {subscription.Status}"));
        stack.Children.Add(CreateDetailLabel($"Source: {subscription.Source}"));

        card.Content = stack;
        return card;
    }

    private static Frame CreateCardContainer()
    {
        return new Frame
        {
            CornerRadius = 12,
            Padding = new Thickness(14),
            BorderColor = Color.FromArgb("#3A2E17"),
            BackgroundColor = Color.FromArgb("#1D1D1D"),
            HasShadow = false
        };
    }

    private static Label CreateTitleLabel(string text)
    {
        return new Label
        {
            Text = text,
            TextColor = Color.FromArgb("#F5C452"),
            FontSize = 18,
            FontAttributes = FontAttributes.Bold
        };
    }

    private static Label CreateDetailLabel(string text)
    {
        return new Label
        {
            Text = text,
            TextColor = Color.FromArgb("#E0E0E0"),
            FontSize = 13
        };
    }

    private static Label CreateEmptyStateLabel(string text)
    {
        return new Label
        {
            Text = text,
            TextColor = Color.FromArgb("#B0B0B0"),
            FontSize = 13,
            Margin = new Thickness(0, 6, 0, 0)
        };
    }
}
