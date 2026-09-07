using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Banking;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.ViewModels;

/// <summary>What a result row on the statement-import page is: an auto-matched pair, an unmatched
/// statement row, or an unmatched book transaction.</summary>
public enum StatementRowKind
{
    Matched,
    UnmatchedStatement,
    UnmatchedBook,
}

/// <summary>
/// One result row on the Import Bank Statement page: a statement line and/or a book transaction, tagged by
/// <see cref="Kind"/> (matched / unmatched-statement / unmatched-book). Amount/date are pre-formatted so
/// the view binds strings directly. A matched row shows the Bank Date that was stamped onto the book line.
/// </summary>
public sealed partial class StatementResultRow : ViewModelBase
{
    public StatementResultRow(StatementRowKind kind, string date, string description, string amount,
        string instrument, string bankDate,
        BankStatementRow? source = null,
        IReadOnlyList<DomainLedger>? ledgerChoices = null)
    {
        Kind = kind;
        Date = date;
        Description = description;
        Amount = amount;
        Instrument = instrument;
        BankDate = bankDate;
        Source = source;
        LedgerChoices = ledgerChoices ?? Array.Empty<DomainLedger>();
    }

    public StatementRowKind Kind { get; }
    public string Date { get; }
    public string Description { get; }
    public string Amount { get; }
    public string Instrument { get; }
    public string BankDate { get; }

    /// <summary>The parsed statement line behind this row — non-null only for an unmatched statement row, the
    /// only kind row 8.13 can turn into a voucher.</summary>
    public BankStatementRow? Source { get; }

    /// <summary>The ledgers the <b>Ledger Name</b> column offers (every ledger but the bank being reconciled).</summary>
    public IReadOnlyList<DomainLedger> LedgerChoices { get; }

    /// <summary>Ticked by the operator (Spacebar / click) to include this line in the next F7 or Alt+F7.</summary>
    [ObservableProperty] private bool _isSelected;

    /// <summary>
    /// The vendor's <b>Ledger Name</b> column (<c>help.tallysolutions.com/auto-create-vouchers/</c>): the other
    /// side of the entry this bank line becomes. There is deliberately NO default — a statement line says money
    /// moved, never what for, and a guessed ledger is a misposting the operator never chose.
    /// </summary>
    [ObservableProperty] private DomainLedger? _selectedLedger;

    public bool IsMatched => Kind == StatementRowKind.Matched;
    public bool IsUnmatchedStatement => Kind == StatementRowKind.UnmatchedStatement;
    public bool IsUnmatchedBook => Kind == StatementRowKind.UnmatchedBook;

    /// <summary>Only an unmatched STATEMENT line can become a voucher: a matched line already has one and an
    /// unmatched BOOK line is a voucher waiting for a statement, which is the opposite problem.</summary>
    public bool CanCreateVoucher => Kind == StatementRowKind.UnmatchedStatement && Source is not null;

    /// <summary>A short label for the row's status ("Matched" / "Unmatched (statement)" / "Unmatched (book)").</summary>
    public string StatusLabel => Kind switch
    {
        StatementRowKind.Matched => "Matched",
        StatementRowKind.UnmatchedStatement => "Unmatched (statement)",
        _ => "Unmatched (book)",
    };
}

/// <summary>
/// One line of the <b>"Bank Reconciliation – Optional Vouchers"</b> section (census row 8.13,
/// <c>help.tallysolutions.com/auto-create-vouchers/</c>): a voucher created from the statement that is still
/// Optional, and therefore still outside every balance, until the operator presses <b>R</b>.
/// </summary>
public sealed partial class OptionalVoucherRow : ViewModelBase
{
    public OptionalVoucherRow(Guid voucherId, string date, string number, string voucherType,
        string ledgerName, string amount, string narration)
    {
        VoucherId = voucherId;
        Date = date;
        Number = number;
        VoucherType = voucherType;
        LedgerName = ledgerName;
        Amount = amount;
        Narration = narration;
    }

    public Guid VoucherId { get; }
    public string Date { get; }
    public string Number { get; }
    public string VoucherType { get; }
    public string LedgerName { get; }
    public string Amount { get; }
    public string Narration { get; }

    /// <summary>Ticked to include this voucher in the next <b>R</b> (Mark as Regular &amp; Reconcile).</summary>
    [ObservableProperty] private bool _isSelected;
}

/// <summary>
/// The Import Bank Statement page (catalog §8, Banking → Import Bank Statement): pick a bank ledger, point
/// to a CSV file (date, description, amount[, instrument]), run the engine's auto-match
/// (<see cref="BankStatementImport.MatchAndReconcile"/>) which stamps the Bank Date on every matched book
/// transaction, then show the matched pairs and the unmatched statement rows / book transactions. On a
/// successful import the company is persisted so the applied bank dates survive.
///
/// <para>The engine parser is file-IO-free (it takes text); this page performs the single
/// <see cref="File.ReadAllText(string)"/> and hands the text to the engine, keeping the core clean.</para>
/// </summary>
public sealed partial class BankStatementImportViewModel : ViewModelBase
{
    private readonly Company _company;
    private readonly CompanyStorage _storage;
    private readonly DateOnly _asOf;
    private readonly Action _onChanged;

    [ObservableProperty] private string _title = "Import Bank Statement";
    [ObservableProperty] private string _subtitle = string.Empty;
    [ObservableProperty] private DomainLedger? _selectedBank;
    [ObservableProperty] private string _statementPath = string.Empty;
    [ObservableProperty] private string _summaryText = string.Empty;
    [ObservableProperty] private string? _message;

    /// <summary>The company's bank ledgers the picker chooses from.</summary>
    public IReadOnlyList<DomainLedger> BankLedgers { get; }

    /// <summary>The matched + unmatched result rows from the last import (empty before importing).</summary>
    public ObservableCollection<StatementResultRow> Results { get; } = new();

    /// <summary>
    /// The <b>"Bank Reconciliation – Optional Vouchers"</b> queue for the selected bank (census row 8.13): the
    /// Optional vouchers awaiting review. Refreshed after every import, every create and every regularise, so the
    /// section is a live queue rather than a record of what this session happened to do.
    /// </summary>
    public ObservableCollection<OptionalVoucherRow> OptionalVouchers { get; } = new();

    /// <summary>The ledgers offered in the <b>Ledger Name</b> column — every ledger except the bank being
    /// reconciled, which the engine refuses as its own other side.</summary>
    public IReadOnlyList<DomainLedger> ContraLedgerChoices =>
        _company.Ledgers
            .Where(l => SelectedBank is null || l.Id != SelectedBank.Id)
            .OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public BankStatementImportViewModel(Company company, CompanyStorage storage, Action? onChanged = null)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _onChanged = onChanged ?? (() => { });
        _asOf = ComputeAsOf(company);

        BankLedgers = company.Ledgers
            .Where(l => ClassificationRules.IsBankLedger(l, company))
            .OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Subtitle = $"{company.Name}  —  auto-match by amount + instrument / nearby date";
        SelectedBank = BankLedgers.FirstOrDefault();
        if (SelectedBank is null)
            Message = "No bank ledgers found. Create a ledger under Bank Accounts or Bank OD A/c first.";
    }

    public DateOnly AsOf => _asOf;

    /// <summary>
    /// Reads the CSV at <see cref="StatementPath"/> and runs the auto-match against the selected bank
    /// ledger, applying the matched statement dates as Bank Dates through the engine, then persisting the
    /// company. Returns the number of transactions reconciled. Surfaces a clear message on any failure
    /// (no bank picked, missing/blank path, unreadable file, malformed CSV).
    /// </summary>
    public int Import()
    {
        Message = null;
        Results.Clear();
        SummaryText = string.Empty;

        if (SelectedBank is null)
        {
            Message = "Select a bank ledger to reconcile against.";
            return 0;
        }

        var path = (StatementPath ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(path))
        {
            Message = "Enter or choose the path to a bank-statement CSV file.";
            return 0;
        }

        string csv;
        try
        {
            csv = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Message = $"Could not read '{path}': {ex.Message}";
            return 0;
        }

        return ImportFromText(csv);
    }

    /// <summary>
    /// Runs the auto-match over already-read CSV <paramref name="csv"/> text (the file-IO-free path — used
    /// by the file <see cref="Import"/> and directly by tests). Populates <see cref="Results"/> with the
    /// matched pairs + unmatched rows and persists the company. Returns the number reconciled.
    /// </summary>
    public int ImportFromText(string csv)
    {
        Message = null;
        Results.Clear();
        SummaryText = string.Empty;

        if (SelectedBank is null)
        {
            Message = "Select a bank ledger to reconcile against.";
            return 0;
        }

        IReadOnlyList<BankStatementRow> rows;
        try
        {
            rows = BankStatementImport.ParseCsv(csv);
        }
        catch (FormatException ex)
        {
            Message = $"The statement CSV is malformed: {ex.Message}";
            return 0;
        }

        var result = BankStatementImport.MatchAndReconcile(_company, SelectedBank, _asOf, rows);

        foreach (var (tx, row) in result.Matched)
            Results.Add(new StatementResultRow(
                StatementRowKind.Matched,
                ApexDate.Format(row.Date),
                row.Description,
                IndianFormat.AmountAlways(Math.Abs(row.Amount.Amount)) + (row.Signed >= 0 ? " Cr(in)" : " Dr(out)"),
                string.IsNullOrWhiteSpace(row.InstrumentNumber) ? "—" : row.InstrumentNumber,
                tx.BankDate is { } bd ? ApexDate.Format(bd) : ApexDate.Format(row.Date)));

        var choices = ContraLedgerChoices;
        foreach (var row in result.UnmatchedStatementRows)
            Results.Add(new StatementResultRow(
                StatementRowKind.UnmatchedStatement,
                ApexDate.Format(row.Date),
                row.Description,
                IndianFormat.AmountAlways(Math.Abs(row.Amount.Amount)) + (row.Signed >= 0 ? " Cr(in)" : " Dr(out)"),
                string.IsNullOrWhiteSpace(row.InstrumentNumber) ? "—" : row.InstrumentNumber,
                "—",
                source: row,
                ledgerChoices: choices));

        foreach (var tx in result.UnmatchedBookTransactions)
            Results.Add(new StatementResultRow(
                StatementRowKind.UnmatchedBook,
                ApexDate.Format(tx.Date),
                $"Voucher No. {tx.FormattedNumber} ({tx.TransactionType})",
                IndianFormat.AmountAlways(Math.Abs(tx.Amount.Amount)) + (tx.Side == DrCr.Debit ? " Dr(in)" : " Cr(out)"),
                string.IsNullOrWhiteSpace(tx.InstrumentNumber) ? "—" : tx.InstrumentNumber,
                "—"));

        SummaryText =
            $"Matched {result.MatchedCount}  ·  {result.UnmatchedStatementRows.Count} statement row(s) unmatched  ·  " +
            $"{result.UnmatchedBookTransactions.Count} book transaction(s) unmatched";

        if (result.MatchedCount > 0)
        {
            _storage.Save(_company);
            _onChanged();
            Message = $"Applied bank dates to {result.MatchedCount} matched transaction(s).";
        }
        else
        {
            Message = "No statement rows matched an unreconciled book transaction.";
        }

        RefreshOptionalVouchers();
        return result.MatchedCount;
    }

    // ------------------------------------------------------------------ census 8.13: auto-create vouchers

    /// <summary>
    /// <b>F7 — "Create Vch/Multi-Vch"</b> (<c>help.tallysolutions.com/auto-create-vouchers/</c>): one voucher per
    /// ticked unmatched statement line, each using that line's own <b>Ledger Name</b>. Selecting a single line is
    /// the vendor's "single voucher from a bank entry" case.
    /// </summary>
    public int CreateVouchersForSelection() => Create(BankStatementVoucherMode.Multiple);

    /// <summary>
    /// <b>Alt+F7 — "Create Voucher(Consolidate)"</b>: ONE voucher for the whole ticked selection, whose amount is
    /// the combined amount of those bank entries.
    /// </summary>
    public int CreateConsolidatedVoucherForSelection() => Create(BankStatementVoucherMode.Consolidate);

    /// <summary>
    /// The shared body of F7 / Alt+F7. Every voucher it creates is <b>Optional</b>, so nothing reaches the books
    /// here; the company is saved so the pending queue survives a restart, exactly as an unreviewed batch should.
    /// </summary>
    private int Create(BankStatementVoucherMode mode)
    {
        Message = null;

        if (SelectedBank is null)
        {
            Message = "Select a bank ledger first.";
            return 0;
        }

        var picked = Results.Where(r => r.IsSelected && r.CanCreateVoucher).ToList();
        if (picked.Count == 0)
        {
            Message = "Tick the unmatched statement lines you want to turn into vouchers "
                      + "(Spacebar on the row's checkbox), then press F7.";
            return 0;
        }

        var missing = picked.Count(r => r.SelectedLedger is null);
        if (missing > 0)
        {
            Message = $"{missing} selected line(s) have no Ledger Name. A bank line says money moved, not what "
                      + "for, so choose the ledger for every line before creating vouchers.";
            return 0;
        }

        var requests = picked
            .Select(r => new BankStatementVoucherRequest(r.Source!, r.SelectedLedger!.Id))
            .ToList();

        IReadOnlyList<AutoCreatedVoucherRow> created;
        try
        {
            created = BankStatementVoucherCreation.CreateOptionalVouchers(
                new LedgerService(_company), _company, SelectedBank, requests, mode);
        }
        // InvalidVoucherException / UnbalancedVoucherException derive from Exception, NOT from
        // InvalidOperationException, so they are named explicitly. A page that let one of them escape would take
        // the whole app down on a statement the engine merely declined to post.
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException
                                      or InvalidVoucherException or UnbalancedVoucherException)
        {
            Message = ex.Message;
            return 0;
        }

        foreach (var r in picked)
        {
            r.IsSelected = false;
            r.SelectedLedger = null;
        }

        _storage.Save(_company);
        _onChanged();
        RefreshOptionalVouchers();

        Message = $"Created {created.Count} OPTIONAL voucher(s). They are NOT in your balances yet — review them "
                  + "below and press R (Mark as Regular & Reconcile) to post them.";
        return created.Count;
    }

    /// <summary>
    /// <b>R — "Mark as Regular &amp; Reconcile"</b>: regularises every ticked pending voucher (which is the moment
    /// it reaches the books) and stamps the statement's date on it as the Bank Date.
    /// </summary>
    public int MarkSelectedAsRegular()
    {
        Message = null;

        if (SelectedBank is null)
        {
            Message = "Select a bank ledger first.";
            return 0;
        }

        var picked = OptionalVouchers.Where(v => v.IsSelected).ToList();
        if (picked.Count == 0)
        {
            Message = "Tick the pending vouchers you have reviewed, then press R to mark them regular and "
                      + "reconcile them.";
            return 0;
        }

        var service = new LedgerService(_company);
        var done = 0;
        foreach (var row in picked)
            if (BankStatementVoucherCreation.MarkAsRegularAndReconcile(service, _company, row.VoucherId, SelectedBank.Id))
                done++;

        if (done > 0)
        {
            _storage.Save(_company);
            _onChanged();
        }

        RefreshOptionalVouchers();
        Message = done == 0
            ? "Nothing was regularised — those vouchers are no longer pending."
            : $"Marked {done} voucher(s) as regular and reconciled. They are now in your balances.";
        return done;
    }

    /// <summary>Re-reads the pending queue for the selected bank from the book (never from what this screen
    /// remembers doing), so an entry regularised anywhere else disappears from it too.</summary>
    public void RefreshOptionalVouchers()
    {
        OptionalVouchers.Clear();
        if (SelectedBank is null) return;

        foreach (var row in BankStatementVoucherCreation.OptionalVouchersFor(_company, SelectedBank))
            OptionalVouchers.Add(new OptionalVoucherRow(
                row.VoucherId,
                ApexDate.Format(row.Date),
                row.FormattedNumber,
                row.VoucherTypeName,
                row.ContraLedgerName,
                IndianFormat.AmountAlways(row.Amount.Amount) + (row.IsMoneyIn ? " Dr(in)" : " Cr(out)"),
                _company.FindVoucher(row.VoucherId)?.Narration ?? string.Empty));

        OnPropertyChanged(nameof(OptionalVouchersHeading));
        OnPropertyChanged(nameof(HasOptionalVouchers));
    }

    /// <summary>True while anything is pending review — drives the section's visibility.</summary>
    public bool HasOptionalVouchers => OptionalVouchers.Count > 0;

    /// <summary>The pending-queue caption, which states the count AND what it means for the books.</summary>
    public string OptionalVouchersHeading =>
        OptionalVouchers.Count == 0
            ? "Bank Reconciliation – Optional Vouchers  —  nothing pending review"
            : $"Bank Reconciliation – Optional Vouchers  —  {OptionalVouchers.Count} pending review; "
              + "not in your balances yet";

    partial void OnSelectedBankChanged(DomainLedger? value)
    {
        OnPropertyChanged(nameof(ContraLedgerChoices));
        RefreshOptionalVouchers();
        OnPropertyChanged(nameof(OptionalVouchersHeading));
    }

    private static DateOnly ComputeAsOf(Company company)
    {
        DateOnly? last = null;
        foreach (var v in company.Vouchers)
            if (last is null || v.Date > last.Value)
                last = v.Date;
        return last ?? company.FinancialYearStart.AddYears(1).AddDays(-1);
    }
}
