using SubscriptionTiger.Models;
using SubscriptionTiger.Services;
using SubscriptionTiger.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using System.Globalization;

namespace SubscriptionTiger;

public partial class MainPage : ContentPage
{
    private const string GmailOAuthDiagnosticsMessage = "Disabled pending native Google authorization implementation.";

    private readonly InMemorySubscriptionRepository repository;
    private readonly LocalSubscriptionStorageService localSubscriptionStorageService;
    private readonly SubscriptionDetectionService detectionService = new();
    private readonly DiagnosticsService diagnosticsService = new();
    private readonly IGmailScanService gmailScanService;
    private ScanResultSummary? lastScanResult;
    private string lastAction = "App opened";
    private string lastScanStatus = "No scan activity yet.";
    private bool isHelpVisible;
    private bool isDiagnosticsVisible;
    private bool isMoreOptionsVisible;

    private Entry? ManualVendorInput => this.FindByName<Entry>("ManualVendorEntry");
    private Entry? ManualPriceInput => this.FindByName<Entry>("ManualPriceEntry");
    private Picker? ManualBillingCycleInput => this.FindByName<Picker>("ManualBillingCyclePicker");
    private DatePicker? ManualRenewalDateInput => this.FindByName<DatePicker>("ManualRenewalDatePicker");

    public MainPage()
    {
        var services = MauiProgram.Services ?? throw new InvalidOperationException("Application services are not initialized.");
        gmailScanService = services.GetRequiredService<IGmailScanService>();
        repository = services.GetRequiredService<InMemorySubscriptionRepository>();
        localSubscriptionStorageService = services.GetRequiredService<LocalSubscriptionStorageService>();

        InitializeComponent();
        InitializeManualInputs();
        UpdateCollapsibleSectionState();

        _ = LoadConfirmedSubscriptionsAsync();
        RefreshUi();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadConfirmedSubscriptionsAsync();
    }

    private async void OnScanGmailClicked(object sender, EventArgs e)
    {
        var gmailResult = await gmailScanService.ScanInboxAsync(CancellationToken.None);
        var addResult = repository.AddCandidates(gmailResult.Candidates);

        diagnosticsService.RecordScan(SubscriptionSource.Gmail);
        lastScanResult = new ScanResultSummary(
            SourceName: "Gmail",
            ScanMode: gmailResult.ScanMode,
            ItemsChecked: gmailResult.MessagesChecked,
            ItemsCheckedLabel: "Messages checked",
            NewCandidatesFound: addResult.AddedCount,
            DuplicatesSkipped: addResult.DuplicateCount,
            ResultMessage: gmailResult.ResultMessage,
            ScanTime: gmailResult.ScanTime);

        lastAction = "Tapped Gmail connection pending";
        lastScanStatus = "Gmail connection pending for this test build.";

        RefreshUi();

        await DisplayAlert(
            "Gmail Pending",
            "Gmail connection is not enabled in this test build yet. You can still test SubscriptionTiger using sample or manual subscriptions.",
            "OK");
    }

    private void OnToggleHelpClicked(object sender, EventArgs e)
    {
        isHelpVisible = !isHelpVisible;
        UpdateCollapsibleSectionState();
    }

    private void OnToggleDiagnosticsClicked(object sender, EventArgs e)
    {
        isDiagnosticsVisible = !isDiagnosticsVisible;
        UpdateCollapsibleSectionState();
    }

    private void OnToggleMoreOptionsClicked(object sender, EventArgs e)
    {
        isMoreOptionsVisible = !isMoreOptionsVisible;
        UpdateCollapsibleSectionState();
    }

    private void OnScanOutlookClicked(object sender, EventArgs e)
    {
        lastAction = "Tapped Outlook coming soon";
        AddDetectedCandidates(SubscriptionSource.Outlook, 18, "Messages checked");
    }

    private void OnScanOtherEmailClicked(object sender, EventArgs e)
    {
        lastAction = "Tapped Other email coming soon";
        AddDetectedCandidates(SubscriptionSource.OtherEmail, 12, "Messages checked");
    }

    private void OnScanBankFileClicked(object sender, EventArgs e)
    {
        lastAction = "Tapped Bank file coming soon";
        AddDetectedCandidates(SubscriptionSource.BankFile, 40, "Transactions checked");
    }

    private void OnAddSampleManualClicked(object? sender, EventArgs e)
    {
        repository.AddManualSample();
        _ = SaveConfirmedSubscriptionsAsync();
        diagnosticsService.RecordScan(SubscriptionSource.Manual);
        lastAction = "Added sample subscription";
        lastScanStatus = "Sample subscription added.";
        RefreshUi();
    }

    private async void OnAddManualSubscriptionClicked(object sender, EventArgs e)
    {
        var vendor = ManualVendorInput?.Text;
        if (string.IsNullOrWhiteSpace(vendor))
        {
            await DisplayAlert("Validation", "Vendor name is required.", "OK");
            return;
        }

        var manualPriceText = ManualPriceInput?.Text;
        if (!decimal.TryParse(manualPriceText, NumberStyles.Number, CultureInfo.CurrentCulture, out var price)
            && !decimal.TryParse(manualPriceText, NumberStyles.Number, CultureInfo.InvariantCulture, out price))
        {
            await DisplayAlert("Validation", "Price must be a valid number.", "OK");
            return;
        }

        if (price <= 0)
        {
            await DisplayAlert("Validation", "Price must be greater than 0.", "OK");
            return;
        }

        var cycle = ManualBillingCycleInput?.SelectedItem?.ToString() == "Yearly"
            ? BillingCycle.Yearly
            : BillingCycle.Monthly;
        var renewalDate = ManualRenewalDateInput?.Date ?? DateTime.Today.AddMonths(cycle == BillingCycle.Monthly ? 1 : 12);

        try
        {
            repository.AddManualSubscription(vendor, price, cycle, renewalDate);
        }
        catch (InvalidOperationException)
        {
            await DisplayAlert("Validation", "A subscription with this vendor and billing cycle already exists.", "OK");
            return;
        }

        diagnosticsService.RecordScan(SubscriptionSource.Manual);
        await SaveConfirmedSubscriptionsAsync();
        lastAction = "Added manual subscription";
        lastScanStatus = "Manual subscription added.";

        if (ManualVendorInput is not null)
        {
            ManualVendorInput.Text = string.Empty;
        }

        if (ManualPriceInput is not null)
        {
            ManualPriceInput.Text = string.Empty;
        }

        if (ManualBillingCycleInput is not null)
        {
            ManualBillingCycleInput.SelectedIndex = 0;
        }

        if (ManualRenewalDateInput is not null)
        {
            ManualRenewalDateInput.Date = DateTime.Today.AddMonths(1);
        }

        RefreshUi();
    }

    private async void OnSaveAsSubscriptionClicked(object? sender, EventArgs e)
    {
        if (sender is not Button button || button.CommandParameter is not string rawId || !Guid.TryParse(rawId, out var id))
        {
            return;
        }

        repository.SaveCandidate(id);
        await SaveConfirmedSubscriptionsAsync();
        lastAction = "Confirmed suspected subscription";
        lastScanStatus = "Suspected subscription moved to confirmed.";
        RefreshUi();
    }

    private void OnDismissClicked(object? sender, EventArgs e)
    {
        if (sender is not Button button || button.CommandParameter is not string rawId || !Guid.TryParse(rawId, out var id))
        {
            return;
        }

        repository.DismissCandidate(id);
        lastAction = "Dismissed suspected subscription";
        lastScanStatus = "Suspected subscription dismissed.";
        RefreshUi();
    }

    private async void OnDeleteConfirmedClicked(object? sender, EventArgs e)
    {
        if (sender is not Button button || button.CommandParameter is not string rawId || !Guid.TryParse(rawId, out var id))
        {
            return;
        }

        repository.DeleteConfirmedSubscription(id);
        await SaveConfirmedSubscriptionsAsync();
        lastAction = "Removed confirmed subscription";
        lastScanStatus = "Confirmed subscription removed.";
        RefreshUi();
    }

    private void AddDetectedCandidates(SubscriptionSource source, int itemsChecked, string itemsCheckedLabel)
    {
        var candidates = detectionService.Scan(source);
        var addResult = repository.AddCandidates(candidates);
        diagnosticsService.RecordScan(source);
        lastScanStatus = "Sample source scan completed.";
        lastScanResult = new ScanResultSummary(
            GetScanSourceDisplayName(source),
            "Sample scan",
            itemsChecked,
            itemsCheckedLabel,
            addResult.AddedCount,
            addResult.DuplicateCount,
            "Sample scan completed.",
            DateTime.Now);

        RefreshUi();
    }

    private void RefreshUi()
    {
        BuildSuspectedSection();
        BuildConfirmedSection();
        UpdateConfirmedSummary();

        SuspectedCountValue.Text = repository.SuspectedCandidates.Count.ToString(CultureInfo.InvariantCulture);
        ConfirmedCountValue.Text = repository.ConfirmedSubscriptions.Count.ToString(CultureInfo.InvariantCulture);
        LastActionValue.Text = lastAction;
        LastScanStatusValue.Text = lastScanStatus;
        GmailOAuthStatusValue.Text = GmailOAuthDiagnosticsMessage;
        StorageStatusValue.Text = "Local storage ready";

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

        ScanSummarySourceValue.Text = $"Source: {lastScanResult.SourceName}";
        ScanSummaryResultValue.Text = BuildCompactResultMessage(lastScanResult);
        ScanSummaryTimeValue.Text = $"Updated: {lastScanResult.ScanTime:yyyy-MM-dd HH:mm}";
    }

    private static string BuildCompactResultMessage(ScanResultSummary summary)
    {
        if (summary.SourceName == "Gmail")
        {
            return "Gmail connection pending for this test build.";
        }

        return $"{summary.NewCandidatesFound} new, {summary.DuplicatesSkipped} skipped.";
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
            SuspectedContainer.Children.Add(CreateEmptyStateLabel("No suspected subscriptions yet. Add a sample subscription or connect a source to begin."));
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
            ConfirmedContainer.Children.Add(CreateEmptyStateLabel("No confirmed subscriptions yet."));
            return;
        }

        foreach (var subscription in repository.ConfirmedSubscriptions.OrderBy(x => x.Vendor, StringComparer.CurrentCultureIgnoreCase))
        {
            ConfirmedContainer.Children.Add(CreateConfirmedCard(subscription));
        }
    }

    private View CreateSuspectedCard(SubscriptionCandidate candidate)
    {
        var card = CreateCardContainer();
        var stack = new VerticalStackLayout { Spacing = 4 };

        stack.Children.Add(CreateTitleLabel(candidate.Vendor));
        var candidatePrice = candidate.Price.HasValue ? candidate.Price.Value.ToString("C", CultureInfo.CurrentCulture) : "Unknown";
        stack.Children.Add(CreateDetailLabel($"Price: {candidatePrice} | Cycle: {candidate.BillingCycle}"));
        stack.Children.Add(CreateDetailLabel($"Confidence: {candidate.ConfidenceScore}% | Source: {candidate.Source}"));

        var actions = new HorizontalStackLayout { Spacing = 8 };

        var saveButton = new Button
        {
            Text = "Confirm",
            BackgroundColor = Color.FromArgb("#CFAF57"),
            TextColor = Colors.Black,
            CornerRadius = 10,
            HeightRequest = 36,
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
            HeightRequest = 36,
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
        var stack = new VerticalStackLayout { Spacing = 3 };

        stack.Children.Add(CreateTitleLabel(subscription.Vendor));
        var confirmedPrice = subscription.Price.HasValue ? subscription.Price.Value.ToString("C", CultureInfo.CurrentCulture) : "Unknown";
        stack.Children.Add(CreateDetailLabel($"{confirmedPrice} {subscription.BillingCycle.ToString().ToLowerInvariant()}"));
        stack.Children.Add(CreateDetailLabel($"Renews: {subscription.RenewalDate:yyyy-MM-dd}"));
        var monthlyEquivalent = CalculateMonthlyEquivalent(subscription);
        stack.Children.Add(CreateDetailLabel($"Monthly equivalent: {monthlyEquivalent.ToString("C", CultureInfo.CurrentCulture)}"));

        var deleteButton = new Button
        {
            Text = "Remove",
            BackgroundColor = Color.FromArgb("#2E3440"),
            TextColor = Colors.White,
            CornerRadius = 10,
            HeightRequest = 36,
            Padding = new Thickness(10, 5),
            CommandParameter = subscription.Id.ToString()
        };
        deleteButton.Clicked += OnDeleteConfirmedClicked;
        stack.Children.Add(deleteButton);

        card.Content = stack;
        return card;
    }

    private static decimal CalculateMonthlyEquivalent(ConfirmedSubscription subscription)
    {
        if (!subscription.Price.HasValue)
        {
            return 0m;
        }

        return subscription.BillingCycle == BillingCycle.Yearly
            ? decimal.Round(subscription.Price.Value / 12m, 2, MidpointRounding.AwayFromZero)
            : subscription.Price.Value;
    }

    private async Task LoadConfirmedSubscriptionsAsync()
    {
        var stored = await localSubscriptionStorageService.LoadConfirmedSubscriptionsAsync();
        repository.SetConfirmedSubscriptions(stored);
        RefreshUi();
    }

    private async Task SaveConfirmedSubscriptionsAsync()
    {
        await localSubscriptionStorageService.SaveConfirmedSubscriptionsAsync(repository.ConfirmedSubscriptions);
    }

    private void UpdateConfirmedSummary()
    {
        ConfirmedSummaryCountValue.Text = repository.ConfirmedSubscriptions.Count.ToString(CultureInfo.InvariantCulture);

        var estimatedMonthlyTotal = repository.ConfirmedSubscriptions
            .Sum(CalculateMonthlyEquivalent);
        ConfirmedSummaryMonthlyTotalValue.Text = estimatedMonthlyTotal.ToString("C", CultureInfo.CurrentCulture);
    }

    private void UpdateCollapsibleSectionState()
    {
        MoreOptionsContent.IsVisible = isMoreOptionsVisible;
        HelpSection.IsVisible = isHelpVisible;
        DiagnosticsSection.IsVisible = isDiagnosticsVisible;

        ToggleMoreOptionsButton.Text = isMoreOptionsVisible ? "Hide More Options" : "Show More Options";
        ToggleHelpButton.Text = isHelpVisible ? "Hide Help" : "Show Help";
        ToggleDiagnosticsButton.Text = isDiagnosticsVisible ? "Hide Diagnostics / Developer Info" : "Show Diagnostics / Developer Info";
    }

    private void InitializeManualInputs()
    {
        if (ManualBillingCycleInput is not null)
        {
            ManualBillingCycleInput.SelectedIndex = 0;
        }

        if (ManualRenewalDateInput is not null)
        {
            ManualRenewalDateInput.Date = DateTime.Today.AddMonths(1);
        }
    }

    private static Frame CreateCardContainer()
    {
        return new Frame
        {
            CornerRadius = 12,
            Padding = new Thickness(10),
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
            FontSize = 16,
            FontAttributes = FontAttributes.Bold
        };
    }

    private static Label CreateDetailLabel(string text)
    {
        return new Label
        {
            Text = text,
            TextColor = Color.FromArgb("#E0E0E0"),
            FontSize = 12
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
