using System.Text;
using SubscriptionTiger.Models;
using SubscriptionTiger.Services;
using Xunit;

namespace SubscriptionTiger.Tests;

public class BankFileScanServiceTests
{
    private static BankFileScanService CreateService()
    {
        return new BankFileScanService(new DiagnosticsService(), new SubscriptionSignalAnalyzer());
    }

    private static async Task<BankFileScanResult> ScanAsync(string csv)
    {
        var service = CreateService();
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        return await service.ScanCsvAsync(stream, "test.csv", CancellationToken.None);
    }

    private static bool ContainsVendor(BankFileScanResult result, string fragment)
    {
        foreach (var candidate in result.Candidates)
        {
            if (candidate.Vendor.Contains(fragment, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    [Fact]
    public async Task WhenRbcStyleColumnsUsedThenRowsAreParsed()
    {
        const string csv = "Account Type,Account Number,Transaction Date,Cheque Number,Description 1,Description 2,CAD$,USD$\n" +
                           "Visa,xxxx,9/18/2025,,GITHUB INC GITHUB.COM,,-4.00,\n" +
                           "Visa,xxxx,10/18/2025,,GITHUB INC GITHUB.COM,,-4.00,\n" +
                           "Visa,xxxx,11/18/2025,,GITHUB INC GITHUB.COM,,-4.00,\n";

        var result = await ScanAsync(csv);

        Assert.Equal(3, result.TransactionsParsed);
    }

    [Fact]
    public async Task WhenMonthlyConsistentChargeThenVendorIsSuspected()
    {
        const string csv = "Transaction Date,Description 1,CAD$\n" +
                           "9/18/2025,GITHUB INC GITHUB.COM,-4.00\n" +
                           "10/18/2025,GITHUB INC GITHUB.COM,-4.00\n" +
                           "11/18/2025,GITHUB INC GITHUB.COM,-4.00\n";

        var result = await ScanAsync(csv);

        Assert.True(ContainsVendor(result, "github"));
    }

    [Fact]
    public async Task WhenVendingMachineRepeatsMonthlyThenItIsNotSuspected()
    {
        const string csv = "Transaction Date,Description 1,CAD$\n" +
                           "9/03/2025,CAD SODA SNACK VENDING TORONTO,-2.50\n" +
                           "10/05/2025,CAD SODA SNACK VENDING TORONTO,-3.00\n" +
                           "11/04/2025,CAD SODA SNACK VENDING TORONTO,-2.75\n";

        var result = await ScanAsync(csv);

        Assert.False(ContainsVendor(result, "vending"));
    }

    [Fact]
    public async Task WhenParkingRepeatsMonthlyThenItIsNotSuspected()
    {
        const string csv = "Transaction Date,Description 1,CAD$\n" +
                           "9/10/2025,R PARKING - VIHA VICTORIA,-12.00\n" +
                           "10/11/2025,R PARKING - VIHA VICTORIA,-8.00\n" +
                           "11/09/2025,R PARKING - VIHA VICTORIA,-15.00\n";

        var result = await ScanAsync(csv);

        Assert.False(ContainsVendor(result, "parking"));
    }

    [Fact]
    public async Task WhenInterestChargeRepeatsMonthlyThenItIsNotSuspected()
    {
        const string csv = "Transaction Date,Description 1,CAD$\n" +
                           "9/21/2025,PURCHASE INTEREST 20.99%,-30.00\n" +
                           "10/21/2025,PURCHASE INTEREST 20.99%,-32.50\n" +
                           "11/21/2025,PURCHASE INTEREST 20.99%,-28.75\n";

        var result = await ScanAsync(csv);

        Assert.False(ContainsVendor(result, "interest"));
    }

    [Fact]
    public async Task WhenFastFoodRepeatsMonthlyThenItIsNotSuspected()
    {
        const string csv = "Transaction Date,Description 1,CAD$\n" +
                           "9/12/2025,PENINSULA CO-OP MCDONALD SIDNEY,-9.50\n" +
                           "10/14/2025,PENINSULA CO-OP MCDONALD SIDNEY,-11.25\n" +
                           "11/13/2025,PENINSULA CO-OP MCDONALD SIDNEY,-7.80\n";

        var result = await ScanAsync(csv);

        Assert.False(ContainsVendor(result, "mcdonald"));
    }

    [Fact]
    public async Task WhenSubscriptionVendorChargesMonthlyThenItIsSuspectedEvenWithCategoryWord()
    {
        // "WL *elderscrollsonline.co" is a real game subscription; the trailing reference must not split it
        // and no exclusion category applies, so it should be detected.
        const string csv = "Transaction Date,Description 1,CAD$\n" +
                           "9/15/2025,WL *ELDERSCROLLSONLINE.CO 410-568-3200,-20.15\n" +
                           "10/15/2025,WL *elderscrollsonline.co 410-5683390,-20.15\n" +
                           "11/15/2025,WL *elderscrollsonline.co 410-5683200,-20.15\n";

        var result = await ScanAsync(csv);

        Assert.True(ContainsVendor(result, "elderscrollsonline"));
    }

    [Fact]
    public async Task WhenSameVendorHasVaryingReferenceDigitsThenItIsGroupedOnce()
    {
        const string csv = "Transaction Date,Description 1,CAD$\n" +
                           "9/15/2025,WL *ELDERSCROLLSONLINE.CO 410-568-3200,-20.15\n" +
                           "10/15/2025,WL *elderscrollsonline.co 410-5683390,-20.15\n" +
                           "11/15/2025,WL *elderscrollsonline.co 410-5683200,-20.15\n";

        var result = await ScanAsync(csv);

        var matches = result.Candidates.Count(c => c.Vendor.Contains("elderscrollsonline", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(1, matches);
    }

    [Fact]
    public async Task WhenLeadingReferenceDigitsPrecedeVendorThenItIsGroupedAndSuspected()
    {
        // Some banks insert a per-transaction auth/store id BEFORE the vendor name (for example
        // "MSP *0001 NETFLIX"). The varying interior digit token must not split an otherwise monthly,
        // consistent subscription.
        const string csv = "Transaction Date,Description 1,CAD$\n" +
                           "9/15/2025,MSP *0001 NETFLIX,-16.99\n" +
                           "10/15/2025,MSP *0002 NETFLIX,-16.99\n" +
                           "11/15/2025,MSP *0003 NETFLIX,-16.99\n" +
                           "12/15/2025,MSP *0004 NETFLIX,-16.99\n" +
                           "1/15/2026,MSP *0005 NETFLIX,-16.99\n";

        var result = await ScanAsync(csv);

        var matches = result.Candidates.Count(c => c.Vendor.Contains("netflix", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(1, matches);
    }

    [Fact]
    public async Task WhenInteriorAlphanumericReferenceFollowsVendorThenItIsGroupedAndSuspected()
    {
        // An interior alphanumeric reference id (for example "AB123456") with a long digit run must be
        // treated as reference noise so the vendor still groups into a single monthly subscription.
        const string csv = "Transaction Date,Description 1,CAD$\n" +
                           "9/03/2025,AUDIBLE AB100001 CA,-14.95\n" +
                           "10/03/2025,AUDIBLE AB100002 CA,-14.95\n" +
                           "11/03/2025,AUDIBLE AB100003 CA,-14.95\n" +
                           "12/03/2025,AUDIBLE AB100004 CA,-14.95\n" +
                           "1/03/2026,AUDIBLE AB100005 CA,-14.95\n";

        var result = await ScanAsync(csv);

        var matches = result.Candidates.Count(c => c.Vendor.Contains("audible", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(1, matches);
    }

    [Fact]
    public async Task WhenVendorTokenHasShortDigitRunThenItIsNotStrippedAsNoise()
    {
        // A short digit-bearing brand token (for example "O2") must be preserved, not removed as a
        // reference id, so a legitimate vendor name is never emptied.
        const string csv = "Transaction Date,Description 1,CAD$\n" +
                           "9/20/2025,O2 MOBILE UK,-12.00\n" +
                           "10/20/2025,O2 MOBILE UK,-12.00\n" +
                           "11/20/2025,O2 MOBILE UK,-12.00\n";

        var result = await ScanAsync(csv);

        Assert.True(ContainsVendor(result, "o2 mobile"));
    }

    [Fact]
    public async Task WhenAmountsAreInconsistentAndNonSubscriptionThenNotSuspected()
    {
        // A frequented retailer with irregular amounts and a category word should not be flagged.
        const string csv = "Transaction Date,Description 1,CAD$\n" +
                           "9/02/2025,WAL-MART SUPERCENTER#1214 VICTORIA,-15.00\n" +
                           "10/03/2025,WAL-MART SUPERCENTER#1214 VICTORIA,-84.27\n" +
                           "11/01/2025,WAL-MART SUPERCENTER#1214 VICTORIA,-42.10\n";

        var result = await ScanAsync(csv);

        Assert.False(ContainsVendor(result, "wal-mart"));
    }

    [Fact]
    public async Task WhenSingleOneTimeChargeThenNotSuspected()
    {
        const string csv = "Transaction Date,Description 1,CAD$\n" +
                           "9/11/2025,THE LOCAL BISTRO RESTAURANT,-43.65\n";

        var result = await ScanAsync(csv);

        Assert.Empty(result.Candidates);
    }

    [Fact]
    public async Task WhenIncomingDepositThenNotSuspected()
    {
        const string csv = "Transaction Date,Description 1,CAD$\n" +
                           "9/25/2025,PAYROLL DIRECT DEPOSIT,2450.00\n" +
                           "10/25/2025,PAYROLL DIRECT DEPOSIT,2450.00\n" +
                           "11/25/2025,PAYROLL DIRECT DEPOSIT,2450.00\n";

        var result = await ScanAsync(csv);

        Assert.Empty(result.Candidates);
    }

    [Fact]
    public async Task WhenMalformedRowPresentThenScanStillSucceedsAndCountsError()
    {
        const string csv = "Transaction Date,Description 1,CAD$\n" +
                           "9/18/2025,GITHUB INC GITHUB.COM,-4.00\n" +
                           "10/18/2025,GITHUB INC GITHUB.COM,-4.00\n" +
                           "11/18/2025,GITHUB INC GITHUB.COM,-4.00\n" +
                           "13/99/2025,BROKEN ROW MISSING AMOUNT,\n";

        var result = await ScanAsync(csv);

        Assert.True(result.IsConfigured);
        Assert.Equal(1, result.ParseErrors);
    }

    [Fact]
    public async Task WhenAnnualSubscriptionChargedTwelveMonthsApartThenSuspectedYearly()
    {
        const string csv = "Transaction Date,Description 1,CAD$\n" +
                           "5/15/2025,MICROSOFT*MICROSOFT 365 annual subscription,-99.99\n" +
                           "5/15/2026,MICROSOFT*MICROSOFT 365 annual subscription,-99.99\n";

        var result = await ScanAsync(csv);

        var match = result.Candidates.FirstOrDefault(c => c.Vendor.Contains("microsoft", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(match);
        Assert.Equal(BillingCycle.Yearly, match!.BillingCycle);
    }

    [Fact]
    public async Task WhenVaryingAmountChargedMonthlyForManyMonthsThenSuspectedMonthly()
    {
        // A genuine subscription billed monthly whose amount varies (usage/tax/FX) over six months.
        // The vendor name has no subscription keyword, so only the sustained cadence can detect it.
        const string csv = "Transaction Date,Description 1,CAD$\n" +
                           "9/05/2025,CLOUDSTORE HOSTING PLAN,-6.50\n" +
                           "10/05/2025,CLOUDSTORE HOSTING PLAN,-7.10\n" +
                           "11/05/2025,CLOUDSTORE HOSTING PLAN,-6.98\n" +
                           "12/05/2025,CLOUDSTORE HOSTING PLAN,-9.40\n" +
                           "1/05/2026,CLOUDSTORE HOSTING PLAN,-7.05\n" +
                           "2/05/2026,CLOUDSTORE HOSTING PLAN,-12.80\n";

        var result = await ScanAsync(csv);

        var match = result.Candidates.FirstOrDefault(c => c.Vendor.Contains("CLOUDSTORE", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(match);
        Assert.Equal(BillingCycle.Monthly, match!.BillingCycle);
    }

    [Fact]
    public async Task WhenVaryingAmountChargedMonthlyWithCategoryWordThenStillSuspected()
    {
        // A sustained monthly cadence must survive an ordinary-category word in the description.
        const string csv = "Transaction Date,Description 1,CAD$\n" +
                           "9/08/2025,METEREDAPP RECURRING BILL PAYMENT,-180.00\n" +
                           "10/08/2025,METEREDAPP RECURRING BILL PAYMENT,-205.50\n" +
                           "11/08/2025,METEREDAPP RECURRING BILL PAYMENT,-160.25\n" +
                           "12/08/2025,METEREDAPP RECURRING BILL PAYMENT,-240.00\n" +
                           "1/08/2026,METEREDAPP RECURRING BILL PAYMENT,-203.75\n" +
                           "2/08/2026,METEREDAPP RECURRING BILL PAYMENT,-190.10\n";

        var result = await ScanAsync(csv);

        Assert.True(ContainsVendor(result, "METEREDAPP"));
    }

    [Fact]
    public async Task WhenVaryingAmountWithCategoryWordChargedOnlyThreeTimesThenNotSuspected()
    {
        // Three charges (two intervals) are not a sustained series, so a varying-amount retailer with a
        // category word must still be excluded - guards against over-eager detection.
        const string csv = "Transaction Date,Description 1,CAD$\n" +
                           "9/08/2025,CORNER GROCERY MARKET,-180.00\n" +
                           "10/08/2025,CORNER GROCERY MARKET,-42.10\n" +
                           "11/08/2025,CORNER GROCERY MARKET,-205.50\n";

        var result = await ScanAsync(csv);

        Assert.False(ContainsVendor(result, "GROCERY"));
    }

    [Fact]
    public async Task WhenVaryingAmountChargedMonthlyForFiveMonthsThenSuspectedAtSustainedThreshold()
    {
        // Exactly five charges = four near-monthly intervals, the minimum sustained cadence.
        const string csv = "Transaction Date,Description 1,CAD$\n" +
                           "9/20/2025,DATASYNC SERVICE,-15.00\n" +
                           "10/20/2025,DATASYNC SERVICE,-18.40\n" +
                           "11/20/2025,DATASYNC SERVICE,-15.60\n" +
                           "12/20/2025,DATASYNC SERVICE,-21.10\n" +
                           "1/20/2026,DATASYNC SERVICE,-16.75\n";

        var result = await ScanAsync(csv);

        Assert.True(ContainsVendor(result, "DATASYNC"));
    }
}
