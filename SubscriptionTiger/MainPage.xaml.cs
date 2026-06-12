using SubscriptionTiger.Models;
using SubscriptionTiger.Services;
using SubscriptionTiger.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.ApplicationModel;
using System.Globalization;
using System.Threading;

namespace SubscriptionTiger;

public partial class MainPage : ContentPage
{
    private const string GmailOAuthDiagnosticsMessage = "Google OAuth reads Gmail using read-only scope after successful sign-in.";
    private const string SourcePendingMessage = "Automatic scanning is coming soon. This source is not enabled in this test build yet.";

    private readonly InMemorySubscriptionRepository repository;
    private readonly LocalSubscriptionStorageService localSubscriptionStorageService;
    private readonly DiagnosticsService diagnosticsService;
    private readonly IGmailScanService gmailScanService;
    private readonly IOutlookScanService outlookScanService;
    private readonly IOtherEmailScanService otherEmailScanService;
    private readonly IBankFileScanService bankFileScanService;
    private readonly SemaphoreSlim sampleAddLock = new(1, 1);
    private ScanResultSummary? lastScanResult;
    private string lastAction = "App opened";
    private string lastScanStatus = "No scan activity yet.";
    private bool isHelpVisible;
    private bool isDiagnosticsVisible;
    private bool isMoreOptionsVisible;
    private bool isManualEntryVisible;
    private bool isOtherEmailSetupVisible;
    private bool isSuspectedReviewVisible;
    private bool isConfirmedReviewVisible;
    private bool isGmailScanning;
    private bool isOutlookScanning;
    private bool isOtherEmailScanning;
    private bool isBankFileScanning;

    private Entry? ManualVendorInput => this.FindByName<Entry>("ManualVendorEntry");
    private Entry? ManualPriceInput => this.FindByName<Entry>("ManualPriceEntry");
    private Picker? ManualBillingCycleInput => this.FindByName<Picker>("ManualBillingCyclePicker");
    private DatePicker? ManualRenewalDateInput => this.FindByName<DatePicker>("ManualRenewalDatePicker");
    private Button? AddSampleSubscriptionInput => this.FindByName<Button>("AddSampleSubscriptionButton");
    private Frame? OtherEmailSetupPanelInput => this.FindByName<Frame>("OtherEmailSetupPanel");
    private Picker? OtherEmailPresetPickerInput => this.FindByName<Picker>("OtherEmailPresetPicker");
    private Entry? OtherEmailAddressEntryInput => this.FindByName<Entry>("OtherEmailAddressEntry");
    private Entry? OtherEmailServerEntryInput => this.FindByName<Entry>("OtherEmailServerEntry");
    private Entry? OtherEmailPortEntryInput => this.FindByName<Entry>("OtherEmailPortEntry");
    private Picker? OtherEmailSecurityPickerInput => this.FindByName<Picker>("OtherEmailSecurityPicker");
    private Entry? OtherEmailUsernameEntryInput => this.FindByName<Entry>("OtherEmailUsernameEntry");
    private Entry? OtherEmailPasswordEntryInput => this.FindByName<Entry>("OtherEmailPasswordEntry");
    private Entry? OtherEmailMaxMessagesEntryInput => this.FindByName<Entry>("OtherEmailMaxMessagesEntry");
    private Button? OtherEmailStartScanButtonInput => this.FindByName<Button>("OtherEmailStartScanButton");
    private Label? SuspectedReviewCountLabel => this.FindByName<Label>("SuspectedReviewCountValue");
    private Label? ConfirmedReviewCountLabel => this.FindByName<Label>("ConfirmedReviewCountValue");
    private Button? GmailScanButtonInput => this.FindByName<Button>("GmailScanButton");
    private ActivityIndicator? GmailScanActivityIndicatorInput => this.FindByName<ActivityIndicator>("GmailScanActivityIndicator");
    private Label? GmailScanProgressStatusValueInput => this.FindByName<Label>("GmailScanProgressStatusValue");
    private Button? OutlookScanButtonInput => this.FindByName<Button>("OutlookScanButton");
    private ActivityIndicator? OutlookScanActivityIndicatorInput => this.FindByName<ActivityIndicator>("OutlookScanActivityIndicator");
    private Label? OutlookScanProgressStatusValueInput => this.FindByName<Label>("OutlookScanProgressStatusValue");
    private Button? BankFileScanButtonInput => this.FindByName<Button>("BankFileScanButton");
    private ScrollView? MainScrollViewInput => this.FindByName<ScrollView>("MainScrollView");
    private Label? ConfirmedSummaryMonthlyTotalValueInput => this.FindByName<Label>("ConfirmedSummaryMonthlyTotalValue");
    private Label? SuspectedSummaryMonthlyTotalValueInput => this.FindByName<Label>("SuspectedSummaryMonthlyTotalValue");
    private Label? PossibleSummaryMonthlyTotalValueInput => this.FindByName<Label>("PossibleSummaryMonthlyTotalValue");

    public MainPage()
    {
        var services = MauiProgram.Services ?? throw new InvalidOperationException("Application services are not initialized.");
        gmailScanService = services.GetRequiredService<IGmailScanService>();
        outlookScanService = services.GetRequiredService<IOutlookScanService>();
        otherEmailScanService = services.GetRequiredService<IOtherEmailScanService>();
        bankFileScanService = services.GetRequiredService<IBankFileScanService>();
        repository = services.GetRequiredService<InMemorySubscriptionRepository>();
        localSubscriptionStorageService = services.GetRequiredService<LocalSubscriptionStorageService>();
        diagnosticsService = services.GetRequiredService<DiagnosticsService>();

        InitializeComponent();
        InitializeManualInputs();
        InitializeOtherEmailInputs();
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
        if (isGmailScanning)
        {
            return;
        }

        isGmailScanning = true;
        diagnosticsService.RecordGmailOAuthStatus("Gmail Scan tapped");
        UpdateGmailScanVisualState(isRunning: true, "Gmail scan starting...");
        RefreshUi();

        try
        {
            diagnosticsService.RecordGmailOAuthStatus("Waiting for Google sign-in");
            UpdateGmailScanVisualState(isRunning: true, "Waiting for Google sign-in...");

            var gmailScanTask = gmailScanService.ScanInboxAsync(CancellationToken.None);
            while (!gmailScanTask.IsCompleted)
            {
                var currentStatus = diagnosticsService.GmailOAuthStatus;
                if (!string.IsNullOrWhiteSpace(currentStatus))
                {
                    UpdateGmailScanVisualState(isRunning: true, currentStatus);
                }

                await Task.Delay(200);
            }

            var gmailResult = await gmailScanTask;
            var addResult = repository.AddCandidates(gmailResult.Candidates);
            diagnosticsService.RecordEvent("OAuthDiag", $"Gmail scan completed: checked={gmailResult.MessagesChecked} found={addResult.AddedCount} duplicates={addResult.DuplicateCount}");

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

            var isOAuthSuccess = gmailResult.IsConfigured && !string.IsNullOrWhiteSpace(gmailResult.AccessToken);

            lastAction = isOAuthSuccess
                ? "Completed Gmail OAuth sign-in"
                : "Attempted Gmail OAuth sign-in";
            lastScanStatus = $"{gmailResult.ResultMessage} Checked {gmailResult.MessagesChecked}; found {addResult.AddedCount}; duplicates {addResult.DuplicateCount}; mode {gmailResult.ScanMode}; at {gmailResult.ScanTime:yyyy-MM-dd HH:mm}.";

            var finalGmailStatus = isOAuthSuccess
                ? $"Gmail scan completed. Checked {gmailResult.MessagesChecked} messages. Found {addResult.AddedCount} suspected subscriptions."
                : gmailResult.ResultMessage;

            diagnosticsService.RecordGmailOAuthStatus("Updating UI with Gmail scan result");
            UpdateGmailScanVisualState(isRunning: false, finalGmailStatus);
            RefreshUi();

            if (!isOAuthSuccess)
            {
                await DisplayAlert("Gmail Sign-In", gmailResult.ResultMessage, "OK");
            }
        }
        catch
        {
            UpdateGmailScanVisualState(isRunning: false, "Gmail scan failed. Please try again.");
            lastAction = "Gmail scan failed";
            lastScanStatus = "Gmail scan failed. Please try again.";
            RefreshUi();
        }
        finally
        {
            isGmailScanning = false;
        }
    }

    private void OnToggleManualEntryClicked(object sender, EventArgs e)
    {
        isManualEntryVisible = !isManualEntryVisible;
        lastAction = isManualEntryVisible ? "Expanded manual entry" : "Collapsed manual entry";
        UpdateCollapsibleSectionState();
        RefreshUi();
    }

    private void OnManualBillingCycleChanged(object sender, EventArgs e)
    {
        if (ManualRenewalDateInput is null || ManualBillingCycleInput is null)
        {
            return;
        }

        var selectedCycle = ManualBillingCycleInput.SelectedItem?.ToString() == "Yearly"
            ? BillingCycle.Yearly
            : BillingCycle.Monthly;

        ManualRenewalDateInput.Date = DateTime.Today.AddMonths(selectedCycle == BillingCycle.Yearly ? 12 : 1);
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

    private async void OnViewSuspectedClicked(object sender, EventArgs e)
    {
        isSuspectedReviewVisible = true;
        isConfirmedReviewVisible = false;
        UpdateReviewSectionVisibility();
        await ScrollToSectionAsync(SuspectedSectionFrame);
    }

    private async void OnViewConfirmedClicked(object sender, EventArgs e)
    {
        isSuspectedReviewVisible = false;
        isConfirmedReviewVisible = true;
        UpdateReviewSectionVisibility();
        await ScrollToSectionAsync(ConfirmedSectionFrame);
    }

    private async Task ScrollToSectionAsync(VisualElement? section)
    {
        if (section is null || MainScrollViewInput is null)
        {
            return;
        }

        await Task.Delay(50);
        await MainScrollViewInput.ScrollToAsync(section, ScrollToPosition.Start, true);
    }

    private void OnHideReviewClicked(object sender, EventArgs e)
    {
        isSuspectedReviewVisible = false;
        isConfirmedReviewVisible = false;
        UpdateReviewSectionVisibility();
    }

    private async void OnScanOutlookClicked(object sender, EventArgs e)
    {
        if (isOutlookScanning)
        {
            return;
        }

        isOutlookScanning = true;
        diagnosticsService.RecordEvent("OutlookScan", "Outlook Scan tapped");
        diagnosticsService.RecordEvent("OutlookScan", "Outlook scan starting...");
        UpdateOutlookScanVisualState(isRunning: true, "Outlook scan starting...");
        RefreshUi();

        try
        {
            var outlookScanTask = outlookScanService.ScanInboxAsync(CancellationToken.None);
            while (!outlookScanTask.IsCompleted)
            {
                var currentStatus = diagnosticsService.LastEventCategory == "OutlookScan"
                    ? diagnosticsService.LastEventMessage
                    : null;

                if (!string.IsNullOrWhiteSpace(currentStatus))
                {
                    UpdateOutlookScanVisualState(isRunning: true, currentStatus);
                }

                await Task.Delay(200);
            }

            var outlookResult = await outlookScanTask;
            var addResult = repository.AddCandidates(outlookResult.Candidates);

            diagnosticsService.RecordScan(SubscriptionSource.Outlook);
            lastScanResult = new ScanResultSummary(
                SourceName: "Outlook",
                ScanMode: outlookResult.ScanMode,
                ItemsChecked: outlookResult.MessagesChecked,
                ItemsCheckedLabel: "Messages checked",
                NewCandidatesFound: addResult.AddedCount,
                DuplicatesSkipped: addResult.DuplicateCount,
                ResultMessage: outlookResult.ResultMessage,
                ScanTime: outlookResult.ScanTime);

            var isAuthSuccess = outlookResult.IsConfigured && !string.IsNullOrWhiteSpace(outlookResult.AccessToken);

            lastAction = isAuthSuccess
                ? "Completed Outlook OAuth sign-in"
                : "Attempted Outlook OAuth sign-in";
            lastScanStatus = $"{outlookResult.ResultMessage} Checked {outlookResult.MessagesChecked}; found {addResult.AddedCount}; duplicates {addResult.DuplicateCount}; mode {outlookResult.ScanMode}; at {outlookResult.ScanTime:yyyy-MM-dd HH:mm}.";

            var finalOutlookStatus = isAuthSuccess
                ? $"Outlook scan completed. Checked {outlookResult.MessagesChecked} messages. Found {addResult.AddedCount} suspected subscriptions."
                : outlookResult.ResultMessage;

            diagnosticsService.RecordEvent("OutlookScan", "Updating UI with Outlook scan result");
            UpdateOutlookScanVisualState(isRunning: false, finalOutlookStatus);
            RefreshUi();

            if (!isAuthSuccess)
            {
                await DisplayAlert("Outlook Sign-In", outlookResult.ResultMessage, "OK");
            }
        }
        catch
        {
            UpdateOutlookScanVisualState(isRunning: false, "Outlook scan failed. Please try again.");
            lastAction = "Outlook scan failed";
            lastScanStatus = "Outlook scan failed. Please try again.";
            RefreshUi();
        }
        finally
        {
            isOutlookScanning = false;
        }
    }

    private async void OnScanOtherEmailClicked(object sender, EventArgs e)
    {
        isOtherEmailSetupVisible = true;
        UpdateCollapsibleSectionState();
        lastAction = "Opened Other Email IMAP setup";
        lastScanStatus = "Configure IMAP settings. Scan is read-only.";
        RefreshUi();
    }

    private void OnCancelOtherEmailSetupClicked(object sender, EventArgs e)
    {
        isOtherEmailSetupVisible = false;
        if (OtherEmailPasswordEntryInput is not null)
        {
            OtherEmailPasswordEntryInput.Text = string.Empty;
        }

        UpdateCollapsibleSectionState();
        lastAction = "Canceled Other Email IMAP setup";
        lastScanStatus = "Other Email scan canceled before authentication.";
        RefreshUi();
    }

    private void OnOtherEmailPresetChanged(object sender, EventArgs e)
    {
        if (OtherEmailPresetPickerInput is null)
        {
            return;
        }

        var selected = OtherEmailPresetPickerInput.SelectedItem?.ToString() ?? "Generic custom";
        ApplyOtherEmailPreset(selected);
    }

    private void OnOtherEmailSecurityModeChanged(object sender, EventArgs e)
    {
        if (OtherEmailSecurityPickerInput is null || OtherEmailPortEntryInput is null)
        {
            return;
        }

        var selected = OtherEmailSecurityPickerInput.SelectedItem?.ToString();
        if (selected == "SSL/TLS")
        {
            OtherEmailPortEntryInput.Text = "993";
        }
        else if (selected == "STARTTLS")
        {
            OtherEmailPortEntryInput.Text = "143";
        }
    }

    private async void OnStartOtherEmailImapScanClicked(object sender, EventArgs e)
    {
        if (isOtherEmailScanning)
        {
            return;
        }

        var validationError = ValidateOtherEmailInputs();
        if (!string.IsNullOrWhiteSpace(validationError))
        {
            await DisplayAlert("Other Email Setup", validationError, "OK");
            return;
        }

        isOtherEmailScanning = true;
        if (OtherEmailStartScanButtonInput is not null)
        {
            OtherEmailStartScanButtonInput.IsEnabled = false;
        }

        try
        {
            var settings = CreateOtherEmailSettings();
            var scanResult = await otherEmailScanService.ScanInboxAsync(settings, CancellationToken.None);
            var addResult = repository.AddCandidates(scanResult.Candidates);

            diagnosticsService.RecordEvent(
                "OtherEmailScan",
                $"Scan result mode={scanResult.ScanMode}; configured={scanResult.IsConfigured}; checked={scanResult.MessagesChecked}; found={addResult.AddedCount}; duplicates={addResult.DuplicateCount}; message={scanResult.ResultMessage}");

            diagnosticsService.RecordScan(SubscriptionSource.OtherEmail);
            lastScanResult = new ScanResultSummary(
                SourceName: "Other Email",
                ScanMode: scanResult.ScanMode,
                ItemsChecked: scanResult.MessagesChecked,
                ItemsCheckedLabel: "Messages checked",
                NewCandidatesFound: addResult.AddedCount,
                DuplicatesSkipped: addResult.DuplicateCount,
                ResultMessage: scanResult.ResultMessage,
                ScanTime: scanResult.ScanTime);

            lastAction = scanResult.IsConfigured
                ? "Completed Other Email IMAP scan"
                : "Attempted Other Email IMAP scan";
            lastScanStatus = $"{scanResult.ResultMessage} Checked {scanResult.MessagesChecked}; found {addResult.AddedCount}; duplicates {addResult.DuplicateCount}; mode {scanResult.ScanMode}; at {scanResult.ScanTime:yyyy-MM-dd HH:mm}.";

            isOtherEmailSetupVisible = false;
            if (OtherEmailPasswordEntryInput is not null)
            {
                OtherEmailPasswordEntryInput.Text = string.Empty;
            }

            UpdateCollapsibleSectionState();
            RefreshUi();

            if (!scanResult.IsConfigured)
            {
                await DisplayAlert("Other Email Scan", scanResult.ResultMessage, "OK");
            }
        }
        finally
        {
            isOtherEmailScanning = false;
            if (OtherEmailStartScanButtonInput is not null)
            {
                OtherEmailStartScanButtonInput.IsEnabled = true;
            }
        }
    }

    private async void OnScanBankFileClicked(object sender, EventArgs e)
    {
        if (isBankFileScanning)
        {
            return;
        }

        FileResult? fileResult;

        try
        {
            fileResult = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Select CSV bank file",
                FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    { DevicePlatform.Android, ["text/csv", "text/comma-separated-values", "application/csv", "application/vnd.ms-excel", "text/plain"] },
                    { DevicePlatform.iOS, ["public.comma-separated-values-text"] },
                    { DevicePlatform.MacCatalyst, ["public.comma-separated-values-text"] },
                    { DevicePlatform.WinUI, [".csv"] }
                })
            });
        }
        catch (Exception)
        {
            lastAction = "Bank file picker failed to open";
            lastScanStatus = "Unable to open file picker.";
            RefreshUi();
            await DisplayAlert("Bank File", "Unable to open file picker on this device.", "OK");
            return;
        }

        if (fileResult is null)
        {
            lastAction = "Bank file scan canceled";
            lastScanStatus = "No file selected.";
            RefreshUi();
            return;
        }

        if (!fileResult.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
        {
            lastAction = "Bank file rejected";
            lastScanStatus = "Only CSV files are supported.";
            RefreshUi();
            await DisplayAlert("Bank File", "Please select a .csv bank file.", "OK");
            return;
        }

        isBankFileScanning = true;
        if (BankFileScanButtonInput is not null)
        {
            BankFileScanButtonInput.IsEnabled = false;
        }

        try
        {
            await using var stream = await fileResult.OpenReadAsync();
            var scanResult = await bankFileScanService.ScanCsvAsync(stream, fileResult.FileName, CancellationToken.None);
            var addResult = repository.AddCandidates(scanResult.Candidates);

            diagnosticsService.RecordScan(SubscriptionSource.BankFile);

            var summaryMessage = $"Bank CSV scan completed. rows={scanResult.RowsChecked}; parsed={scanResult.TransactionsParsed}; suspected={scanResult.Candidates.Count}; duplicates={addResult.DuplicateCount}; parseErrors={scanResult.ParseErrors}; oldest={scanResult.OldestTransactionDate:yyyy-MM-dd}; newest={scanResult.NewestTransactionDate:yyyy-MM-dd}.";
            diagnosticsService.RecordEvent("BankFileScan", summaryMessage);

            lastScanResult = new ScanResultSummary(
                SourceName: "Bank File",
                ScanMode: scanResult.ScanMode,
                ItemsChecked: scanResult.RowsChecked,
                ItemsCheckedLabel: "Rows checked",
                NewCandidatesFound: addResult.AddedCount,
                DuplicatesSkipped: addResult.DuplicateCount,
                ResultMessage: scanResult.ResultMessage,
                ScanTime: scanResult.ScanTime);

            lastAction = scanResult.IsConfigured
                ? "Completed Bank File CSV scan"
                : "Attempted Bank File CSV scan";
            lastScanStatus = $"{scanResult.ResultMessage} File type {scanResult.FileType}; rows checked {scanResult.RowsChecked}; parsed {scanResult.TransactionsParsed}; found {addResult.AddedCount}; duplicates {addResult.DuplicateCount}; parse errors {scanResult.ParseErrors}.";
            RefreshUi();

            if (!scanResult.IsConfigured)
            {
                await DisplayAlert("Bank File", scanResult.ResultMessage, "OK");
            }
        }
        finally
        {
            isBankFileScanning = false;
            if (BankFileScanButtonInput is not null)
            {
                BankFileScanButtonInput.IsEnabled = true;
            }
        }
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
            "Clear Data?",
            "This will remove saved local subscriptions and scan results from this device so you can start over. Your email accounts and bank accounts will not be changed.",
            "Clear Data",
            "Cancel");

        if (!shouldClear)
        {
            return;
        }

        repository.ClearAllTestData();
        await SaveConfirmedSubscriptionsAsync();

        lastAction = "Local data cleared.";
        lastScanStatus = "Saved local subscriptions and scan results were cleared.";
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

    private async void OnViewEmailClicked(object? sender, EventArgs e)
    {
        if (sender is not Button button || button.CommandParameter is not string rawId || !Guid.TryParse(rawId, out var id))
        {
            return;
        }

        var candidate = repository.SuspectedCandidates.FirstOrDefault(x => x.Id == id);
        if (candidate is null)
        {
            return;
        }

        await ShowEmailForCandidateAsync(candidate);
    }

    private async Task ShowEmailForCandidateAsync(SubscriptionCandidate candidate)
    {
        EmailBodyContent? body = null;
        try
        {
            body = await TryFetchEmailBodyAsync(candidate, CancellationToken.None);
        }
        catch (Exception)
        {
            body = null;
        }

        if (body is null || !body.HasContent)
        {
            await ShowEmailEvidenceFallbackAsync(candidate);
            return;
        }

        await Navigation.PushModalAsync(new EmailViewerPage(candidate, body));
    }

    private async void OnSuspectedCancelHelpClicked(object? sender, EventArgs e)
    {
        if (sender is not Button button || button.CommandParameter is not string rawId || !Guid.TryParse(rawId, out var id))
        {
            return;
        }

        var candidate = repository.SuspectedCandidates.FirstOrDefault(x => x.Id == id);
        if (candidate is null)
        {
            return;
        }

        await Navigation.PushModalAsync(new CancelHelpPage(BuildSuspectedCancelHelpContext(candidate)));
    }

    private async void OnConfirmedCancelHelpClicked(object? sender, EventArgs e)
    {
        if (sender is not Button button || button.CommandParameter is not string rawId || !Guid.TryParse(rawId, out var id))
        {
            return;
        }

        var subscription = repository.ConfirmedSubscriptions.FirstOrDefault(x => x.Id == id);
        if (subscription is null)
        {
            return;
        }

        await Navigation.PushModalAsync(new CancelHelpPage(BuildConfirmedCancelHelpContext(subscription)));
    }

    private CancelHelpContext BuildSuspectedCancelHelpContext(SubscriptionCandidate candidate)
    {
        var priceText = candidate.Price.HasValue
            ? candidate.Price.Value.ToString("C", CultureInfo.CurrentCulture)
            : "Unknown";
        var cycleText = ToCycleText(candidate.BillingCycle);
        var sourceText = ToSourceText(candidate.Source);
        var confidenceBand = ToConfidenceBand(candidate.ConfidenceScore);
        var confidenceText = confidenceBand == "Low" ? "Low confidence — review manually" : confidenceBand;

        var latestDate = candidate.LastSourceEmailDate ?? candidate.SourceEmailDate?.LocalDateTime;
        var latestDateText = latestDate.HasValue
            ? latestDate.Value.ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture)
            : null;

        return new CancelHelpContext(
            Vendor: candidate.Vendor,
            PriceText: priceText,
            CycleText: cycleText,
            SourceText: sourceText,
            ConfidenceText: confidenceText,
            Sender: string.IsNullOrWhiteSpace(candidate.SourceEmailSender) ? null : candidate.SourceEmailSender,
            LatestEmailDateText: latestDateText,
            RepeatEvidence: string.IsNullOrWhiteSpace(candidate.RecurringEvidenceSummary) ? null : candidate.RecurringEvidenceSummary,
            DetectionReason: string.IsNullOrWhiteSpace(candidate.DetectionReason) ? null : candidate.DetectionReason,
            SafeWebsiteUrl: ResolveSafeWebsiteUrl(candidate.Vendor),
            ViewSourceEmailAsync: () => ShowEmailForCandidateAsync(candidate));
    }

    private CancelHelpContext BuildConfirmedCancelHelpContext(ConfirmedSubscription subscription)
    {
        var priceText = subscription.Price.HasValue
            ? subscription.Price.Value.ToString("C", CultureInfo.CurrentCulture)
            : "Unknown";

        return new CancelHelpContext(
            Vendor: subscription.Vendor,
            PriceText: priceText,
            CycleText: ToCycleText(subscription.BillingCycle),
            SourceText: ToSourceText(subscription.Source),
            SafeWebsiteUrl: ResolveSafeWebsiteUrl(subscription.Vendor));
    }

    private static string? ResolveSafeWebsiteUrl(string vendor)
        => SubscriptionSignalAnalyzer.TryGetTrustedVendorDomain(vendor, out var domain)
            ? $"https://{domain}"
            : null;

    private static string ToCycleText(BillingCycle billingCycle) => billingCycle switch
    {
        BillingCycle.Yearly => "Yearly/Annual",
        BillingCycle.Monthly => "Monthly",
        _ => "Unknown"
    };

    private static string ToSourceText(SubscriptionSource source) => source switch
    {
        SubscriptionSource.BankFile => "Bank File",
        SubscriptionSource.OtherEmail => "Other Email",
        _ => source.ToString()
    };

    private async Task<EmailBodyContent?> TryFetchEmailBodyAsync(SubscriptionCandidate candidate, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(candidate.SourceMessageId))
        {
            return null;
        }

        return candidate.Source switch
        {
            SubscriptionSource.Gmail => await gmailScanService.GetMessageContentAsync(candidate.SourceMessageId, cancellationToken),
            SubscriptionSource.Outlook => await outlookScanService.GetMessageContentAsync(candidate.SourceMessageId, cancellationToken),
            _ => null
        };
    }

    private async Task ShowEmailEvidenceFallbackAsync(SubscriptionCandidate candidate)
    {
        var sourceText = candidate.Source switch
        {
            SubscriptionSource.BankFile => "Bank File",
            SubscriptionSource.OtherEmail => "Other Email",
            _ => candidate.Source.ToString()
        };

        var details = new List<string>
        {
            $"Vendor: {candidate.Vendor}",
            $"Source: {sourceText}"
        };

        if (!string.IsNullOrWhiteSpace(candidate.SourceEmailSender))
        {
            details.Add($"From: {candidate.SourceEmailSender}");
        }

        if (!string.IsNullOrWhiteSpace(candidate.SourceEmailSubject))
        {
            details.Add($"Subject: {candidate.SourceEmailSubject}");
        }

        if (candidate.SourceEmailDate.HasValue)
        {
            details.Add($"Date: {candidate.SourceEmailDate.Value.LocalDateTime:yyyy-MM-dd HH:mm}");
        }

        if (!string.IsNullOrWhiteSpace(candidate.SourceEmailSnippet))
        {
            details.Add($"Snippet: {candidate.SourceEmailSnippet}");
        }

        if (!string.IsNullOrWhiteSpace(candidate.DetectionReason))
        {
            details.Add($"Matched reason: {candidate.DetectionReason}");
        }

        await DisplayAlert("Email Evidence", string.Join(Environment.NewLine + Environment.NewLine, details), "Close");
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
        if (!MainThread.IsMainThread)
        {
            MainThread.BeginInvokeOnMainThread(RefreshUi);
            return;
        }

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
        GmailOAuthStatusValue.Text = diagnosticsService.GmailOAuthStatus;
        StorageStatusValue.Text = "Local storage ready";
        if (GmailScanProgressStatusValueInput is not null && string.IsNullOrWhiteSpace(GmailScanProgressStatusValueInput.Text))
        {
            GmailScanProgressStatusValueInput.Text = "Idle";
        }

        if (OutlookScanProgressStatusValueInput is not null && string.IsNullOrWhiteSpace(OutlookScanProgressStatusValueInput.Text))
        {
            OutlookScanProgressStatusValueInput.Text = "Idle";
        }

        UpdateLastScanSummaryCard();
    }

    private void UpdateGmailScanVisualState(bool isRunning, string statusText)
    {
        if (!MainThread.IsMainThread)
        {
            MainThread.BeginInvokeOnMainThread(() => UpdateGmailScanVisualState(isRunning, statusText));
            return;
        }

        if (GmailScanButtonInput is not null)
        {
            GmailScanButtonInput.IsEnabled = !isRunning;
        }

        if (GmailScanActivityIndicatorInput is not null)
        {
            GmailScanActivityIndicatorInput.IsVisible = isRunning;
            GmailScanActivityIndicatorInput.IsRunning = isRunning;
        }

        if (GmailScanProgressStatusValueInput is not null)
        {
            GmailScanProgressStatusValueInput.Text = statusText;
            GmailScanProgressStatusValueInput.TextColor = isRunning
                ? Color.FromArgb("#F5C452")
                : statusText.Contains("failed", StringComparison.OrdinalIgnoreCase)
                    || statusText.Contains("canceled", StringComparison.OrdinalIgnoreCase)
                    ? Color.FromArgb("#E57373")
                    : Color.FromArgb("#E0E0E0");
        }
    }

    private void UpdateOutlookScanVisualState(bool isRunning, string statusText)
    {
        if (!MainThread.IsMainThread)
        {
            MainThread.BeginInvokeOnMainThread(() => UpdateOutlookScanVisualState(isRunning, statusText));
            return;
        }

        if (OutlookScanButtonInput is not null)
        {
            OutlookScanButtonInput.IsEnabled = !isRunning;
        }

        if (OutlookScanActivityIndicatorInput is not null)
        {
            OutlookScanActivityIndicatorInput.IsVisible = isRunning;
            OutlookScanActivityIndicatorInput.IsRunning = isRunning;
        }

        if (OutlookScanProgressStatusValueInput is not null)
        {
            OutlookScanProgressStatusValueInput.Text = statusText;
            OutlookScanProgressStatusValueInput.TextColor = isRunning
                ? Color.FromArgb("#F5C452")
                : statusText.Contains("failed", StringComparison.OrdinalIgnoreCase)
                    || statusText.Contains("canceled", StringComparison.OrdinalIgnoreCase)
                    ? Color.FromArgb("#E57373")
                    : Color.FromArgb("#E0E0E0");
        }
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
            return $"Gmail {summary.ScanMode}; checked {summary.ItemsChecked}; found {summary.NewCandidatesFound}; duplicates {summary.DuplicatesSkipped}; {summary.ScanTime:yyyy-MM-dd HH:mm}.";
        }

        if (summary.SourceName == "Bank File")
        {
            return $"Bank File {summary.ScanMode}; rows {summary.ItemsChecked}; found {summary.NewCandidatesFound}; duplicates {summary.DuplicatesSkipped}; {summary.ScanTime:yyyy-MM-dd HH:mm}.";
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
        var cycleText = candidate.BillingCycle switch
        {
            BillingCycle.Yearly => "Yearly/Annual",
            BillingCycle.Monthly => "Monthly",
            _ => "Unknown"
        };
        stack.Children.Add(CreateDetailLabel($"Price: {candidatePrice} | Cycle: {cycleText}"));
        var confidenceBand = ToConfidenceBand(candidate.ConfidenceScore);
        var confidenceText = confidenceBand == "Low" ? "Low confidence — review manually" : confidenceBand;
        var sourceText = candidate.Source == SubscriptionSource.BankFile ? "Bank File" : candidate.Source.ToString();
        stack.Children.Add(CreateDetailLabel($"Confidence: {confidenceText} | Source: {sourceText}"));
        stack.Children.Add(CreateDetailLabel($"Reason: {CreateShortReason(candidate.DetectionReason)}"));

        if (candidate.OccurrenceCount > 1 || !string.IsNullOrWhiteSpace(candidate.RecurringEvidenceSummary))
        {
            var repeatText = !string.IsNullOrWhiteSpace(candidate.RecurringEvidenceSummary)
                ? candidate.RecurringEvidenceSummary
                : $"Seen {candidate.OccurrenceCount} times — repeat billing evidence.";

            stack.Children.Add(new Label
            {
                Text = $"🔁 {repeatText}",
                TextColor = Color.FromArgb("#CFAF57"),
                FontSize = 12,
                FontAttributes = FontAttributes.Bold
            });
        }

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

        var viewEmailButton = new Button
        {
            Text = "View",
            BackgroundColor = Color.FromArgb("#2E3440"),
            TextColor = Colors.White,
            CornerRadius = 10,
            FontSize = 12,
            HeightRequest = 36,
            MinimumWidthRequest = 76,
            Padding = new Thickness(10, 4),
            HorizontalOptions = LayoutOptions.Fill,
            CommandParameter = candidate.Id.ToString()
        };
        viewEmailButton.Clicked += OnViewEmailClicked;

        saveButton.FontSize = 12;
        saveButton.MinimumWidthRequest = 76;
        saveButton.Padding = new Thickness(10, 4);
        saveButton.HorizontalOptions = LayoutOptions.Fill;

        dismissButton.FontSize = 12;
        dismissButton.MinimumWidthRequest = 76;
        dismissButton.Padding = new Thickness(10, 4);
        dismissButton.HorizontalOptions = LayoutOptions.Fill;

        actions.Children.Add(saveButton);
        actions.Children.Add(dismissButton);
        actions.Children.Add(viewEmailButton);

        var cancelHelpButton = new Button
        {
            Text = "Cancel Assistance",
            BackgroundColor = Color.FromArgb("#2E3440"),
            TextColor = Colors.White,
            CornerRadius = 10,
            HeightRequest = 36,
            FontSize = 12,
            HorizontalOptions = LayoutOptions.Fill,
            CommandParameter = candidate.Id.ToString()
        };
        cancelHelpButton.Clicked += OnSuspectedCancelHelpClicked;

        var actionGroup = new VerticalStackLayout
        {
            Spacing = 8,
            HorizontalOptions = LayoutOptions.Start
        };
        actionGroup.Children.Add(actions);
        actionGroup.Children.Add(cancelHelpButton);
        stack.Children.Add(actionGroup);

        card.Content = stack;
        return card;
    }

    private View CreateConfirmedCard(ConfirmedSubscription subscription)
    {
        var card = CreateCardContainer();
        var stack = new VerticalStackLayout { Spacing = 3 };

        var sourceLabel = subscription.Source switch
        {
            SubscriptionSource.Manual => "Manual",
            SubscriptionSource.BankFile => "Bank File",
            SubscriptionSource.OtherEmail => "Other Email",
            _ => subscription.Source.ToString()
        };

        if (subscription.Source == SubscriptionSource.Manual)
        {
            stack.Children.Add(CreateDetailLabel("Type: Manual"));
        }
        else
        {
            stack.Children.Add(CreateDetailLabel($"Type: Detected ({sourceLabel})"));
        }

        stack.Children.Add(CreateTitleLabel(subscription.Vendor));
        var confirmedPrice = subscription.Price.HasValue ? subscription.Price.Value.ToString("C", CultureInfo.CurrentCulture) : "Unknown";
        stack.Children.Add(CreateDetailLabel($"{confirmedPrice} {subscription.BillingCycle.ToString().ToLowerInvariant()}"));
        stack.Children.Add(CreateDetailLabel(subscription.BillingCycle == BillingCycle.Unknown
            ? "Renews: Unknown"
            : $"Renews: {subscription.RenewalDate:yyyy-MM-dd}"));
        var monthlyEquivalent = CalculateMonthlyEquivalent(subscription);
        stack.Children.Add(CreateDetailLabel($"Monthly equivalent: {monthlyEquivalent.ToString("C", CultureInfo.CurrentCulture)}"));

        var cancelHelpButton = new Button
        {
            Text = "Cancel Assistance",
            BackgroundColor = Color.FromArgb("#2E3440"),
            TextColor = Colors.White,
            CornerRadius = 10,
            HeightRequest = 36,
            Padding = new Thickness(10, 5),
            CommandParameter = subscription.Id.ToString()
        };
        cancelHelpButton.Clicked += OnConfirmedCancelHelpClicked;
        stack.Children.Add(cancelHelpButton);

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
        => CalculateMonthlyEquivalent(subscription.Price, subscription.BillingCycle);

    private static decimal CalculateMonthlyEquivalent(decimal? price, BillingCycle billingCycle)
    {
        if (!price.HasValue)
        {
            return 0m;
        }

        return billingCycle == BillingCycle.Yearly
            ? decimal.Round(price.Value / 12m, 2, MidpointRounding.AwayFromZero)
            : billingCycle == BillingCycle.Monthly
                ? price.Value
                : 0m;
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
        var confirmedMonthlyTotal = repository.ConfirmedSubscriptions
            .Sum(CalculateMonthlyEquivalent);

        var suspectedMonthlyTotal = repository.SuspectedCandidates
            .Sum(candidate => CalculateMonthlyEquivalent(candidate.Price, candidate.BillingCycle));

        var possibleMonthlyTotal = confirmedMonthlyTotal + suspectedMonthlyTotal;

        if (ConfirmedSummaryMonthlyTotalValueInput is not null)
        {
            ConfirmedSummaryMonthlyTotalValueInput.Text = confirmedMonthlyTotal.ToString("C", CultureInfo.CurrentCulture);
        }

        if (SuspectedSummaryMonthlyTotalValueInput is not null)
        {
            SuspectedSummaryMonthlyTotalValueInput.Text = suspectedMonthlyTotal.ToString("C", CultureInfo.CurrentCulture);
        }

        if (PossibleSummaryMonthlyTotalValueInput is not null)
        {
            PossibleSummaryMonthlyTotalValueInput.Text = possibleMonthlyTotal.ToString("C", CultureInfo.CurrentCulture);
        }
    }

    private void UpdateCollapsibleSectionState()
    {
        MoreOptionsContent.IsVisible = isMoreOptionsVisible;
        HelpSection.IsVisible = isHelpVisible;
        DiagnosticsSection.IsVisible = isDiagnosticsVisible;
        ManualEntryPanel.IsVisible = isManualEntryVisible;
        if (OtherEmailSetupPanelInput is not null)
        {
            OtherEmailSetupPanelInput.IsVisible = isOtherEmailSetupVisible;
        }

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
            ManualBillingCycleInput.SelectedIndexChanged -= OnManualBillingCycleChanged;
            ManualBillingCycleInput.SelectedIndexChanged += OnManualBillingCycleChanged;
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

    private void InitializeOtherEmailInputs()
    {
        isOtherEmailSetupVisible = false;

        if (OtherEmailPresetPickerInput is not null)
        {
            OtherEmailPresetPickerInput.SelectedIndex = 0;
        }

        if (OtherEmailSecurityPickerInput is not null)
        {
            OtherEmailSecurityPickerInput.SelectedIndex = 0;
        }

        if (OtherEmailPortEntryInput is not null)
        {
            OtherEmailPortEntryInput.Text = "993";
        }

        if (OtherEmailMaxMessagesEntryInput is not null)
        {
            OtherEmailMaxMessagesEntryInput.Text = "40";
        }
    }

    private void ApplyOtherEmailPreset(string preset)
    {
        if (OtherEmailServerEntryInput is null || OtherEmailPortEntryInput is null || OtherEmailSecurityPickerInput is null)
        {
            return;
        }

        switch (preset)
        {
            case "iCloud":
                OtherEmailServerEntryInput.Text = "imap.mail.me.com";
                OtherEmailPortEntryInput.Text = "993";
                OtherEmailSecurityPickerInput.SelectedIndex = 0;
                break;
            case "Yahoo":
                OtherEmailServerEntryInput.Text = "imap.mail.yahoo.com";
                OtherEmailPortEntryInput.Text = "993";
                OtherEmailSecurityPickerInput.SelectedIndex = 0;
                break;
            case "AOL":
                OtherEmailServerEntryInput.Text = "imap.aol.com";
                OtherEmailPortEntryInput.Text = "993";
                OtherEmailSecurityPickerInput.SelectedIndex = 0;
                break;
            default:
                if (string.IsNullOrWhiteSpace(OtherEmailPortEntryInput.Text))
                {
                    OtherEmailPortEntryInput.Text = "993";
                }

                if (OtherEmailSecurityPickerInput.SelectedIndex < 0)
                {
                    OtherEmailSecurityPickerInput.SelectedIndex = 0;
                }
                break;
        }
    }

    private string? ValidateOtherEmailInputs()
    {
        if (string.IsNullOrWhiteSpace(OtherEmailAddressEntryInput?.Text))
        {
            return "Email address is required.";
        }

        if (string.IsNullOrWhiteSpace(OtherEmailServerEntryInput?.Text))
        {
            return "IMAP server is required.";
        }

        if (!int.TryParse(OtherEmailPortEntryInput?.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var port)
            || port <= 0
            || port > 65535)
        {
            return "Port must be between 1 and 65535.";
        }

        if (OtherEmailSecurityPickerInput?.SelectedIndex < 0)
        {
            return "Select a security mode.";
        }

        if (string.IsNullOrWhiteSpace(OtherEmailUsernameEntryInput?.Text))
        {
            return "Username is required.";
        }

        if (string.IsNullOrWhiteSpace(OtherEmailPasswordEntryInput?.Text))
        {
            return "Password or app password is required.";
        }

        if (!int.TryParse(OtherEmailMaxMessagesEntryInput?.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxMessages)
            || maxMessages <= 0
            || maxMessages > 200)
        {
            return "Max messages must be between 1 and 200.";
        }

        return null;
    }

    private OtherEmailImapSettings CreateOtherEmailSettings()
    {
        var securityMode = OtherEmailSecurityPickerInput?.SelectedIndex switch
        {
            1 => OtherEmailSecurityMode.StartTls,
            2 => OtherEmailSecurityMode.None,
            _ => OtherEmailSecurityMode.SslTls
        };

        var emailAddress = OtherEmailAddressEntryInput?.Text?.Trim() ?? string.Empty;
        var username = string.IsNullOrWhiteSpace(OtherEmailUsernameEntryInput?.Text)
            ? emailAddress
            : OtherEmailUsernameEntryInput!.Text.Trim();

        return new OtherEmailImapSettings(
            EmailAddress: emailAddress,
            ImapServer: OtherEmailServerEntryInput?.Text?.Trim() ?? string.Empty,
            Port: int.Parse(OtherEmailPortEntryInput?.Text ?? "0", CultureInfo.InvariantCulture),
            SecurityMode: securityMode,
            Username: username,
            Password: OtherEmailPasswordEntryInput?.Text ?? string.Empty,
            MaxMessages: int.Parse(OtherEmailMaxMessagesEntryInput?.Text ?? "40", CultureInfo.InvariantCulture));
    }

    private async Task ShowPendingSourceMessageAsync(string sourceName)
    {
        lastAction = $"Tapped {sourceName} coming soon";
        lastScanStatus = SourcePendingMessage;
        RefreshUi();

        await DisplayAlert(sourceName, SourcePendingMessage, "OK");
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

    private static string ToConfidenceBand(int score)
    {
        if (score >= 75)
        {
            return "High";
        }

        if (score >= 50)
        {
            return "Medium";
        }

        return "Low";
    }

    private static string CreateShortReason(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return "matched subscription clues";
        }

        return reason.Length > 96 ? reason[..96] + "..." : reason;
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
