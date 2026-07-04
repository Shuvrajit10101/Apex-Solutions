using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.ViewModels;

/// <summary>
/// One transaction row on the Bank Reconciliation page (catalog §8): a single posting to the chosen bank
/// ledger, with its date/voucher context, instrument details, signed movement and an <b>editable Bank
/// Date</b> (dd-MMM-yyyy). Typing a Bank Date and reconciling stamps the underlying bank allocation via
/// the engine so the Balance-as-per-Bank updates. Parsing/validation is deferred to the parent page — this
/// row only holds the editable Bank Date text and the source transaction. No Avalonia types ⇒ headlessly
/// testable.
/// </summary>
public sealed partial class BankReconRowViewModel : ViewModelBase
{
    /// <summary>The source bank transaction this row edits (carries the voucher id to reconcile against).</summary>
    public BankTransactionRow Transaction { get; }

    [ObservableProperty] private string _bankDateText = string.Empty;

    public BankReconRowViewModel(BankTransactionRow tx)
    {
        Transaction = tx;
        Date = tx.Date.ToString("dd-MMM-yyyy");
        VoucherNo = tx.VoucherNumber.ToString(CultureInfo.InvariantCulture);
        Instrument = string.IsNullOrWhiteSpace(tx.InstrumentNumber) ? "—" : tx.InstrumentNumber;
        Kind = tx.TransactionType.ToString();
        // Signed movement: a debit (+) is money into the bank, a credit (−) is money out.
        Amount = IndianFormat.AmountAlways(Math.Abs(tx.Amount.Amount));
        Direction = tx.Side == DrCr.Debit ? "Dr" : "Cr";
        BankDateText = tx.BankDate?.ToString("dd-MMM-yyyy") ?? string.Empty;
    }

    public string Date { get; }
    public string VoucherNo { get; }
    public string Instrument { get; }
    public string Kind { get; }
    public string Amount { get; }
    public string Direction { get; }

    /// <summary>The parsed Bank Date, or null when blank/unparsable (un-reconciled).</summary>
    public DateOnly? ParsedBankDate =>
        DateOnly.TryParse(BankDateText, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d
            : (DateOnly?)null;
}

/// <summary>
/// The Bank Reconciliation (BRS) page (catalog §8, Banking → Bank Reconciliation): pick a bank ledger, see
/// each of its transactions with an <b>editable Bank Date</b>, and the <b>Balance as per Company Books</b>
/// vs <b>Balance as per Bank</b> pair. Setting a transaction's Bank Date and pressing Reconcile stamps the
/// bank allocation through the engine (<see cref="BankReconciliation.SetBankDate"/>), persists the company,
/// and refreshes the book-vs-bank figures so a cleared cheque drops out of the outstanding amount.
///
/// <para>MVVM boundary: references the engine + persistence but no Avalonia types ⇒ headlessly testable.</para>
/// </summary>
public sealed partial class BankReconciliationViewModel : ViewModelBase
{
    private readonly Company _company;
    private readonly CompanyStorage _storage;
    private readonly DateOnly _asOf;
    private readonly Action _onChanged;

    [ObservableProperty] private string _title = "Bank Reconciliation";
    [ObservableProperty] private string _subtitle = string.Empty;
    [ObservableProperty] private DomainLedger? _selectedBank;
    [ObservableProperty] private string _balanceAsPerBooksText = string.Empty;
    [ObservableProperty] private string _balanceAsPerBankText = string.Empty;
    [ObservableProperty] private string _amountNotReflectedText = string.Empty;
    [ObservableProperty] private string? _message;

    /// <summary>The company's bank ledgers (Bank Accounts / Bank OD A/c) the picker chooses from.</summary>
    public IReadOnlyList<DomainLedger> BankLedgers { get; }

    /// <summary>The reconciliation rows for the chosen bank ledger (empty until a bank is picked).</summary>
    public ObservableCollection<BankReconRowViewModel> Rows { get; } = new();

    public BankReconciliationViewModel(Company company, CompanyStorage storage, Action? onChanged = null)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _onChanged = onChanged ?? (() => { });
        _asOf = ComputeAsOf(company);

        BankLedgers = company.Ledgers
            .Where(l => ClassificationRules.IsBankLedger(l, company))
            .OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Subtitle = $"{company.Name}  —  as at {_asOf:dd-MMM-yyyy}";
        SelectedBank = BankLedgers.FirstOrDefault();
        if (SelectedBank is null)
            Message = "No bank ledgers found. Create a ledger under Bank Accounts or Bank OD A/c first.";
    }

    /// <summary>The report's as-of date (last voucher date, else the financial-year end).</summary>
    public DateOnly AsOf => _asOf;

    partial void OnSelectedBankChanged(DomainLedger? value) => Rebuild();

    /// <summary>(Re)builds the transaction rows and book-vs-bank figures for the chosen bank ledger.</summary>
    public void Rebuild()
    {
        Rows.Clear();
        if (SelectedBank is null)
        {
            BalanceAsPerBooksText = BalanceAsPerBankText = AmountNotReflectedText = string.Empty;
            return;
        }

        var brs = BankReconciliation.Build(_company, SelectedBank, _asOf);
        foreach (var t in brs.Transactions)
            Rows.Add(new BankReconRowViewModel(t));

        BalanceAsPerBooksText = FormatBalance(brs.BalanceAsPerBooks);
        BalanceAsPerBankText = FormatBalance(brs.BalanceAsPerBank);
        AmountNotReflectedText = IndianFormat.AmountAlways(Math.Abs(brs.AmountNotReflectedInBank.Amount));

        if (Rows.Count == 0)
            Message = $"No bank transactions on '{SelectedBank.Name}' as at {_asOf:dd-MMM-yyyy}.";
        else
            Message = $"{brs.Unreconciled.Count} of {Rows.Count} transaction(s) not yet cleared.";
    }

    /// <summary>
    /// Reconciles the whole page: for every row that has a (changed) Bank Date typed, stamp it on the bank
    /// allocation through the engine; for a row whose Bank Date has been cleared, un-reconcile it. Then
    /// persist the company and rebuild so the book-vs-bank figures reflect the newly cleared amounts.
    /// Returns the number of transactions whose Bank Date changed.
    /// </summary>
    public int Reconcile()
    {
        Message = null;
        if (SelectedBank is null) return 0;

        var changed = 0;
        foreach (var row in Rows)
        {
            var current = row.Transaction.BankDate;
            var wanted = row.ParsedBankDate;
            if (Nullable.Equals(current, wanted)) continue;

            BankReconciliation.SetBankDate(_company, row.Transaction.VoucherId, SelectedBank.Id, wanted);
            changed++;
        }

        if (changed == 0)
        {
            Message = "Type a Bank Date on one or more transactions, then Reconcile.";
            Rebuild();
            return 0;
        }

        _storage.Save(_company);
        Rebuild();
        _onChanged();
        Message = $"Reconciled {changed} transaction{(changed == 1 ? string.Empty : "s")}.";
        return changed;
    }

    private static string FormatBalance(LedgerBalance b) =>
        $"{IndianFormat.AmountAlways(b.Amount.Amount)} {(b.Side == DrCr.Debit ? "Dr" : "Cr")}";

    private static DateOnly ComputeAsOf(Company company)
    {
        DateOnly? last = null;
        foreach (var v in company.Vouchers)
            if (last is null || v.Date > last.Value)
                last = v.Date;
        return last ?? company.FinancialYearStart.AddYears(1).AddDays(-1);
    }
}
