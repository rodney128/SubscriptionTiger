using System.Globalization;
using SubscriptionTiger.Models;
using SubscriptionTiger.Services.Interfaces;

namespace SubscriptionTiger.Services;

public sealed class BankFileScanService : IBankFileScanService
{
    private const int MaxRowsToAnalyze = 2000;

    private static readonly string[] DateColumns =
    [
        "date",
        "transactiondate",
        "posteddate",
        "postdate"
    ];

    private static readonly string[] DescriptionColumns =
    [
        "description",
        "payee",
        "merchant",
        "name",
        "memo",
        "transaction",
        "transactiondescription"
    ];

    private static readonly string[] AmountColumns =
    [
        "amount",
        "transactionamount"
    ];

    private static readonly string[] DebitColumns =
    [
        "debit",
        "withdrawal"
    ];

    private static readonly string[] CreditColumns =
    [
        "credit",
        "deposit"
    ];

    private static readonly string[] AnnualClues =
    [
        "annual",
        "annually",
        "yearly",
        "per year",
        "/year",
        "billed yearly",
        "billed annually",
        "renews yearly",
        "renews annually",
        "annual plan",
        "yearly plan",
        "annual subscription",
        "yearly subscription",
        "one year",
        "12 months",
        "next year",
        "yearly renewal",
        "annual renewal"
    ];

    private readonly DiagnosticsService diagnosticsService;
    private readonly SubscriptionSignalAnalyzer signalAnalyzer;

    public BankFileScanService(DiagnosticsService diagnosticsService, SubscriptionSignalAnalyzer signalAnalyzer)
    {
        this.diagnosticsService = diagnosticsService;
        this.signalAnalyzer = signalAnalyzer;
    }

    /// <summary>
    /// Scans a local CSV bank file and returns subscription candidates without uploading file content.
    /// </summary>
    public async Task<BankFileScanResult> ScanCsvAsync(Stream csvStream, string fileName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(csvStream);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new ArgumentException("CSV file name is required.", nameof(fileName));
        }

        var parseErrors = 0;

        try
        {
            using var reader = new StreamReader(csvStream, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
            var allLines = new List<string>();
            while (!reader.EndOfStream)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line is not null)
                {
                    allLines.Add(line);
                }
            }

            if (allLines.Count == 0)
            {
                return CreateFailureResult("Bank CSV file is empty.", parseErrors);
            }

            var headerCells = ParseCsvLine(allLines[0]);
            if (headerCells.Count == 0)
            {
                return CreateFailureResult("Bank CSV header row is invalid.", parseErrors + 1);
            }

            var headerLookup = BuildHeaderLookup(headerCells);

            if (!TryResolveColumns(headerLookup, out var dateColumn, out var descriptionColumn, out var amountColumn, out var debitColumn, out var creditColumn))
            {
                return CreateFailureResult(
                    "Bank CSV is missing required columns. Include date, description/payee, and amount or debit/credit columns.",
                    parseErrors + 1);
            }

            var parsedTransactions = new List<BankTransaction>();
            var rowsChecked = 0;

            for (var i = 1; i < allLines.Count && rowsChecked < MaxRowsToAnalyze; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var row = allLines[i];
                if (string.IsNullOrWhiteSpace(row))
                {
                    continue;
                }

                rowsChecked++;
                var cells = ParseCsvLine(row);
                if (cells.Count == 0)
                {
                    parseErrors++;
                    continue;
                }

                if (!TryParseTransaction(cells, dateColumn, descriptionColumn, amountColumn, debitColumn, creditColumn, out var transaction))
                {
                    parseErrors++;
                    continue;
                }

                parsedTransactions.Add(transaction);
            }

            var outgoingTransactions = parsedTransactions
                .Where(x => x.Amount < 0m)
                .OrderBy(x => x.Date)
                .ToList();

            var candidates = BuildCandidates(outgoingTransactions);
            DateTimeOffset? oldest = parsedTransactions.Count == 0 ? null : parsedTransactions.Min(x => x.Date);
            DateTimeOffset? newest = parsedTransactions.Count == 0 ? null : parsedTransactions.Max(x => x.Date);

            diagnosticsService.RecordEvent(
                "BankFileScan",
                $"Bank CSV scan summary fileType=csv; fileName={fileName}; rowsChecked={rowsChecked}; transactionsParsed={parsedTransactions.Count}; suspectedFound={candidates.Count}; parseErrors={parseErrors}; oldest={oldest:yyyy-MM-dd}; newest={newest:yyyy-MM-dd}");

            return new BankFileScanResult(
                IsConfigured: true,
                ScanMode: "Bank File CSV local scan",
                FileType: "csv",
                RowsChecked: rowsChecked,
                TransactionsParsed: parsedTransactions.Count,
                ParseErrors: parseErrors,
                OldestTransactionDate: oldest,
                NewestTransactionDate: newest,
                Candidates: candidates,
                ResultMessage: "Bank CSV scan completed.",
                ScanTime: DateTime.Now);
        }
        catch (OperationCanceledException)
        {
            return CreateFailureResult("Bank CSV scan canceled.", parseErrors);
        }
        catch (IOException ex)
        {
            diagnosticsService.RecordEvent("BankFileScan", $"Bank CSV scan failed: {ex.GetType().Name}: {ex.Message}");
            return CreateFailureResult($"Bank CSV scan failed: {ex.Message}", parseErrors);
        }
    }

    private BankFileScanResult CreateFailureResult(string message, int parseErrors)
    {
        return new BankFileScanResult(
            IsConfigured: false,
            ScanMode: "Bank File CSV local scan",
            FileType: "csv",
            RowsChecked: 0,
            TransactionsParsed: 0,
            ParseErrors: parseErrors,
            OldestTransactionDate: null,
            NewestTransactionDate: null,
            Candidates: Array.Empty<SubscriptionCandidate>(),
            ResultMessage: message,
            ScanTime: DateTime.Now);
    }

    private IReadOnlyList<SubscriptionCandidate> BuildCandidates(IReadOnlyList<BankTransaction> transactions)
    {
        var results = new List<SubscriptionCandidate>();
        var grouped = transactions
            .GroupBy(x => NormalizeVendorKey(x.Description))
            .Where(g => !string.IsNullOrWhiteSpace(g.Key))
            .ToList();

        foreach (var group in grouped)
        {
            var ordered = group.OrderBy(x => x.Date).ToList();
            if (ordered.Count == 0)
            {
                continue;
            }

            var latest = ordered[^1];
            var description = latest.Description;
            var explicitAnnual = ContainsAny(description, AnnualClues);
            var annualPattern = HasRecurringGapMonths(ordered, 10, 15);
            var monthlyPattern = HasRecurringGapDays(ordered, 25, 40);

            var analyzerMatched = signalAnalyzer.TryAnalyze(
                sender: latest.Description,
                subject: latest.Description,
                previewText: latest.Description,
                out var analyzerConfidence,
                out var analyzerReason,
                out var analyzerPrice,
                out var analyzerCycle,
                out var analyzerVendor);

            if (!analyzerMatched && !annualPattern && !monthlyPattern && !explicitAnnual)
            {
                continue;
            }

            var reason = analyzerMatched
                ? analyzerReason
                : "Recurring charge pattern detected in bank history.";

            var billingCycle = analyzerCycle;
            var confidence = analyzerConfidence;
            var amount = Math.Abs(latest.Amount);

            if (annualPattern || explicitAnnual)
            {
                billingCycle = BillingCycle.Yearly;

                if (annualPattern)
                {
                    reason = "Similar charge found about 12 months apart.";
                    confidence = explicitAnnual ? 84 : 66;
                }
                else
                {
                    reason = ordered.Count == 1
                        ? "Possible annual subscription — only one charge found in imported history."
                        : "Description contains annual renewal.";
                    confidence = ordered.Count == 1 ? 44 : 58;
                }
            }
            else if (monthlyPattern)
            {
                billingCycle = BillingCycle.Monthly;
                reason = "Similar charge found on a monthly pattern.";
                confidence = Math.Max(confidence, 62);
            }

            if (billingCycle == BillingCycle.Unknown && confidence < 45)
            {
                reason = "Low confidence — review manually.";
            }

            var vendor = DeriveVendorName(analyzerVendor, description, group.Key);

            results.Add(new SubscriptionCandidate(
                Guid.NewGuid(),
                vendor,
                analyzerPrice ?? amount,
                billingCycle,
                confidence,
                SubscriptionSource.BankFile,
                reason,
                SourceEmailSubject: description,
                SourceEmailSender: "Bank CSV",
                SourceEmailDate: latest.Date,
                SourceEmailSnippet: description));
        }

        return results;
    }

    private static bool HasRecurringGapMonths(IReadOnlyList<BankTransaction> transactions, int minMonths, int maxMonths)
    {
        for (var i = 1; i < transactions.Count; i++)
        {
            var gapMonths = Math.Abs(((transactions[i].Date.Year - transactions[i - 1].Date.Year) * 12) + transactions[i].Date.Month - transactions[i - 1].Date.Month);
            if (gapMonths >= minMonths && gapMonths <= maxMonths)
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasRecurringGapDays(IReadOnlyList<BankTransaction> transactions, int minDays, int maxDays)
    {
        for (var i = 1; i < transactions.Count; i++)
        {
            var gapDays = Math.Abs((transactions[i].Date - transactions[i - 1].Date).TotalDays);
            if (gapDays >= minDays && gapDays <= maxDays)
            {
                return true;
            }
        }

        return false;
    }

    private static string DeriveVendorName(string analyzerVendor, string description, string fallbackKey)
    {
        if (!string.IsNullOrWhiteSpace(analyzerVendor))
        {
            return analyzerVendor;
        }

        var cleaned = description.Trim();
        if (cleaned.Length > 64)
        {
            cleaned = cleaned[..64];
        }

        if (!string.IsNullOrWhiteSpace(cleaned))
        {
            return cleaned;
        }

        return fallbackKey;
    }

    private static bool ContainsAny(string input, IEnumerable<string> patterns)
    {
        foreach (var pattern in patterns)
        {
            if (input.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryParseTransaction(
        IReadOnlyList<string> cells,
        int dateColumn,
        int descriptionColumn,
        int? amountColumn,
        int? debitColumn,
        int? creditColumn,
        out BankTransaction transaction)
    {
        transaction = default;

        var dateText = GetCell(cells, dateColumn);
        var description = GetCell(cells, descriptionColumn);

        if (!TryParseDate(dateText, out var date) || string.IsNullOrWhiteSpace(description))
        {
            return false;
        }

        if (!TryParseAmount(cells, amountColumn, debitColumn, creditColumn, out var amount))
        {
            return false;
        }

        transaction = new BankTransaction(date, description.Trim(), amount);
        return true;
    }

    private static bool TryParseAmount(
        IReadOnlyList<string> cells,
        int? amountColumn,
        int? debitColumn,
        int? creditColumn,
        out decimal amount)
    {
        amount = 0m;

        if (amountColumn.HasValue)
        {
            var raw = GetCell(cells, amountColumn.Value);
            return TryParseDecimal(raw, out amount);
        }

        var debit = 0m;
        var credit = 0m;
        var hasDebit = debitColumn.HasValue && TryParseDecimal(GetCell(cells, debitColumn.Value), out debit);
        var hasCredit = creditColumn.HasValue && TryParseDecimal(GetCell(cells, creditColumn.Value), out credit);

        if (!hasDebit && !hasCredit)
        {
            return false;
        }

        amount = 0m;
        if (hasDebit && debit > 0)
        {
            amount -= debit;
        }

        if (hasCredit && credit > 0)
        {
            amount += credit;
        }

        return amount != 0m;
    }

    private static bool TryParseDecimal(string raw, out decimal value)
    {
        value = 0m;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var cleaned = raw.Trim();
        cleaned = cleaned.Replace("$", string.Empty, StringComparison.Ordinal)
            .Replace(",", string.Empty, StringComparison.Ordinal)
            .Replace("(", "-", StringComparison.Ordinal)
            .Replace(")", string.Empty, StringComparison.Ordinal);

        return decimal.TryParse(cleaned, NumberStyles.Number | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value)
            || decimal.TryParse(cleaned, NumberStyles.Number | NumberStyles.AllowLeadingSign, CultureInfo.CurrentCulture, out value);
    }

    private static bool TryParseDate(string raw, out DateTimeOffset value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        return DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out value)
            || DateTimeOffset.TryParse(raw, CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces, out value);
    }

    private static string NormalizeVendorKey(string input)
    {
        var cleaned = input.Trim().ToLowerInvariant();
        foreach (var token in new[] { " debit", " credit", " purchase", " payment", " online", " pos " })
        {
            cleaned = cleaned.Replace(token, string.Empty, StringComparison.Ordinal);
        }

        return cleaned.Length > 64 ? cleaned[..64] : cleaned;
    }

    private static string GetCell(IReadOnlyList<string> cells, int index)
    {
        return index >= 0 && index < cells.Count ? cells[index] : string.Empty;
    }

    private static bool TryResolveColumns(
        IReadOnlyDictionary<string, int> headers,
        out int dateColumn,
        out int descriptionColumn,
        out int? amountColumn,
        out int? debitColumn,
        out int? creditColumn)
    {
        dateColumn = FindColumn(headers, DateColumns);
        descriptionColumn = FindColumn(headers, DescriptionColumns);
        amountColumn = FindColumnNullable(headers, AmountColumns);
        debitColumn = FindColumnNullable(headers, DebitColumns);
        creditColumn = FindColumnNullable(headers, CreditColumns);

        var amountSupported = amountColumn.HasValue || debitColumn.HasValue || creditColumn.HasValue;
        return dateColumn >= 0 && descriptionColumn >= 0 && amountSupported;
    }

    private static int FindColumn(IReadOnlyDictionary<string, int> headers, IEnumerable<string> aliases)
    {
        foreach (var alias in aliases)
        {
            if (headers.TryGetValue(alias, out var index))
            {
                return index;
            }
        }

        return -1;
    }

    private static int? FindColumnNullable(IReadOnlyDictionary<string, int> headers, IEnumerable<string> aliases)
    {
        var index = FindColumn(headers, aliases);
        return index >= 0 ? index : null;
    }

    private static Dictionary<string, int> BuildHeaderLookup(IReadOnlyList<string> headers)
    {
        var lookup = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < headers.Count; i++)
        {
            var normalized = NormalizeHeader(headers[i]);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                continue;
            }

            lookup[normalized] = i;
        }

        return lookup;
    }

    private static string NormalizeHeader(string header)
    {
        return header
            .Trim()
            .ToLowerInvariant()
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal);
    }

    private static List<string> ParseCsvLine(string line)
    {
        var result = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }

                continue;
            }

            if (c == ',' && !inQuotes)
            {
                result.Add(current.ToString());
                current.Clear();
                continue;
            }

            current.Append(c);
        }

        result.Add(current.ToString());
        return result;
    }

    private readonly record struct BankTransaction(DateTimeOffset Date, string Description, decimal Amount);
}
