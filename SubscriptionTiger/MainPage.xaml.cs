using SubscriptionTiger.Models;
using SubscriptionTiger.Services;
using SubscriptionTiger.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using System.Globalization;
using System.Threading;

namespace SubscriptionTiger;

public partial class MainPage : ContentPage
{
    private const string GmailOAuthDiagnosticsMessage = "Disabled pending native Google authorization implementation.";
    private const string SourcePendingMessage = "Automatic scanning is coming soon. This source is not enabled in this test build yet.";

    private readonly InMemorySubscriptionRepository repository;
    private readonly LocalSubscriptionStorageService localSubscriptionStorageService;
    private readonly DiagnosticsService diagnosticsService = new();
    private readonly IGmailScanService gmailScanService;
    private readonly SemaphoreSlim sampleAddLock = new(1, 1);
    private ScanResultSummary? lastScanResult;
    private string lastAction = "App opened";
    private string lastScanStatus = "No scan activity yet.";
    private bool isHelpVisible;
    private bool isDiagnosticsVisible;
    private bool isMoreOptionsVisible;
    private bool isManualEntryVisible;
    private bool isSuspectedReviewVisible;
    private bool isConfirmedReviewVisible;

    private Entry? ManualVendorInput => this.FindByName<Entry>("ManualVendorEntry");
    private Entry? ManualPriceInput => this.FindByName<Entry>("ManualPriceEntry");
    private Picker? ManualBillingCycleInput => this.FindByName<Picker>("ManualBillingCyclePicker");
    private DatePicker? ManualRenewalDateInput => this.FindByName<DatePicker>("ManualRenewalDatePicker");
    private Button? AddSampleSubscriptionInput => this.FindByName<Button>("AddSampleSubscriptionButton");
    private Label? SuspectedReviewCountLabel => this.FindByName<Label>("SuspectedReviewCountValue");
    private Label? ConfirmedReviewCountLabel => this.FindByName<Label>("ConfirmedReviewCountValue");

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
            "Gmail connection is not enabled in this test build yet. Use a demo or manual subscription to test the Subscription Tiger review workflow.",
            "OK");
    }

    private void OnToggleManualEntryClicked(object sender, EventArgs e)
    {
        isManualEntryVisible = !isManualEntryVisible;
        lastAction = isManualEntryVisible ? "Expanded manual entry" : "Collapsed manual entry";
        UpdateCollapsibleSectionState();
        RefreshUi();
    }

    private void OnCancelManualEntryClicked(object sender, EventArgs e)
    {
        ClearManualEntryInputs();
        isManualEntryVisible = false;
        lastAction = "Canceled manual entry";
        lastScanStatus = "Manual entry canceled.";
        UpdateCollapsibleSectionState();
        RefreshUi();
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

    private void OnViewSuspectedClicked(object sender, EventArgs e)
    {
        isSuspectedReviewVisible = true;
        isConfirmedReviewVisible = false;
        UpdateReviewSectionVisibility();
    }

    private void OnViewConfirmedClicked(object sender, EventArgs e)
    {
        isSuspectedReviewVisible = false;
        isConfirmedReviewVisible = true;
        UpdateReviewSectionVisibility();
    }

    private void OnHideReviewClicked(object sender, EventArgs e)
    {
        isSuspectedReviewVisible = false;
        isConfirmedReviewVisible = false;
        UpdateReviewSectionVisibility();
    }

    private async void OnOutlookSourceTapped(object? sender, TappedEventArgs e)
    {
        await ShowPendingSourceMessageAsync("Outlook");
    }

    private async void OnOtherEmailSourceTapped(object? sender, TappedEventArgs e)
    {
        await ShowPendingSourceMessageAsync("Other email");
    }

    private async void OnBankFileSourceTapped(object? sender, TappedEventArgs e)
    {
        await ShowPendingSourceMessageAsync("Bank file");
    }

    private void OnGmailSourceTapped(object? sender, TappedEventArgs e)
    {
        OnScanGmailClicked(this, EventArgs.Empty);
    }

    private void OnAddSampleManualClicked(object? sender, EventArgs e)
    {
        _ = HandleAddSampleAsync();
    }

    private async Task HandleAddSampleAsync()
    {
        await sampleAddLock.WaitAsync();

        if (AddSampleSubscriptionInput is not null)
        {
            AddSampleSubscriptionInput.IsEnabled = false;
        }

        try
        {
            var storedConfirmed = await localSubscriptionStorageService.LoadConfirmedSubscriptionsAsync();
            repository.SetConfirmedSubscriptions(storedConfirmed);

            repository.RemoveDuplicateSampleSubscriptions();

            var added = repository.AddManualSampleIfMissing();

            repository.RemoveDuplicateSampleSubscriptions();
            await SaveConfirmedSubscriptionsAsync();

            if (!added)
            {
                lastAction = "Demo subscription already exists.";
                lastScanStatus = "Demo subscription already exists.";
                RefreshUi();
                await DisplayAlert("Demo Exists", "Demo subscription already exists.", "OK");
                return;
            }

            diagnosticsService.RecordScan(SubscriptionSource.Manual);
            lastAction = "Added demo subscription";
            lastScanStatus = "Demo subscription added.";
            RefreshUi();
        }
        finally
        {
            if (AddSampleSubscriptionInput is not null)
            {
                AddSampleSubscriptionInput.IsEnabled = true;
            }

            sampleAddLock.Release();
        }
    }

    private async void OnClearTestDataClicked(object sender, EventArgs e)
    {
        var shouldClear = await DisplayAlert(
            "Clear Test Data",
            "Clear all local suspected and confirmed subscriptions?",
            "Clear",
            "Cancel");

        if (!shouldClear)
        {
            return;
        }

        repository.ClearAllTestData();
        await SaveConfirmedSubscriptionsAsync();

        lastAction = "Demo/test data cleared.";
        lastScanStatus = "Demo/test data cleared.";
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

        ClearManualEntryInputs();
        isManualEntryVisible = false;
        UpdateCollapsibleSectionState();

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

    private void RefreshUi()
    {
        BuildSuspectedSection();
        BuildConfirmedSection();
        UpdateConfirmedSummary();

        var suspectedCount = repository.SuspectedCandidates.Count.ToString(CultureInfo.InvariantCulture);
        var confirmedCount = repository.ConfirmedSubscriptions.Count.ToString(CultureInfo.InvariantCulture);
        SuspectedCountValue.Text = suspectedCount;
        ConfirmedCountValue.Text = confirmedCount;
        if (SuspectedReviewCountLabel is not null)
        {
            SuspectedReviewCountLabel.Text = suspectedCount;
        }

        if (ConfirmedReviewCountLabel is not null)
        {
            ConfirmedReviewCountLabel.Text = confirmedCount;
        }
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
            LastActivityValue.Text = "Last activity: No scan yet.";
            return;
        }

        var summaryText = BuildCompactResultMessage(lastScanResult);
        LastActivityValue.Text = $"Last activity: {summaryText}";
    }

    private static string BuildCompactResultMessage(ScanResultSummary summary)
    {
        if (summary.SourceName == "Gmail")
        {
            return "Gmail connection pending.";
        }

        return $"{summary.SourceName} scan added {summary.NewCandidatesFound} items.";
    }

    private void BuildSuspectedSection()
    {
        SuspectedContainer.Children.Clear();

        if (repository.SuspectedCandidates.Count == 0)
        {
            SuspectedContainer.Children.Add(CreateEmptyStateLabel("No suspected subscriptions yet. Connect a source when scanning is available."));
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
            ConfirmedContainer.Children.Add(CreateEmptyStateLabel("No confirmed subscriptions yet. Add a subscription manually to begin."));
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
        var estimatedMonthlyTotal = repository.ConfirmedSubscriptions
            .Sum(CalculateMonthlyEquivalent);
        ConfirmedSummaryMonthlyTotalValue.Text = estimatedMonthlyTotal.ToString("C", CultureInfo.CurrentCulture);
    }

    private void UpdateCollapsibleSectionState()
    {
        MoreOptionsContent.IsVisible = isMoreOptionsVisible;
        HelpSection.IsVisible = isHelpVisible;
        DiagnosticsSection.IsVisible = isDiagnosticsVisible;
        ManualEntryPanel.IsVisible = isManualEntryVisible;
        UpdateReviewSectionVisibility();

        ToggleMoreOptionsButton.Text = isMoreOptionsVisible ? "Hide More Options" : "Show More Options";
        ToggleHelpButton.Text = isHelpVisible ? "Hide Help" : "Show Help";
        ToggleDiagnosticsButton.Text = isDiagnosticsVisible ? "Hide Diagnostics / Developer Info" : "Show Diagnostics / Developer Info";
        ToggleManualEntryButton.Text = isManualEntryVisible ? "Cancel Manual Entry" : "Add Subscription Manually";
    }

    private void InitializeManualInputs()
    {
        isManualEntryVisible = false;

        if (ManualBillingCycleInput is not null)
        {
            ManualBillingCycleInput.SelectedIndex = 0;
        }

        if (ManualRenewalDateInput is not null)
        {
            ManualRenewalDateInput.Date = DateTime.Today.AddMonths(1);
        }
    }

    private async Task ShowPendingSourceMessageAsync(string sourceName)
    {
        lastAction = $"Tapped {sourceName} coming soon";
        lastScanStatus = SourcePendingMessage;
        RefreshUi();

        await DisplayAlert(sourceName, SourcePendingMessage, "OK");
    }

    private void ClearManualEntryInputs()
    {
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

    private void UpdateReviewSectionVisibility()
    {
        SuspectedSectionFrame.IsVisible = isSuspectedReviewVisible;
        ConfirmedSectionFrame.IsVisible = isConfirmedReviewVisible;
        HideReviewButton.IsVisible = isSuspectedReviewVisible || isConfirmedReviewVisible;
    }
}
