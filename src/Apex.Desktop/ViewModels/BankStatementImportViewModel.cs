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
        string instrument, string bankDate)
    {
        Kind = kind;
        Date = date;
        Description = description;
        Amount = amount;
        Instrument = instrument;
        BankDate = bankDate;
    }

    public StatementRowKind Kind { get; }
    public string Date { get; }
    public string Description { get; }
    public string Amount { get; }
    public string Instrument { get; }
    public string BankDate { get; }

    public bool IsMatched => Kind == StatementRowKind.Matched;
    public bool IsUnmatchedStatement => Kind == StatementRowKind.UnmatchedStatement;
    public bool IsUnmatchedBook => Kind == StatementRowKind.UnmatchedBook;

    /// <summary>A short label for the row's status ("Matched" / "Unmatched (statement)" / "Unmatched (book)").</summary>
    public string StatusLabel => Kind switch
    {
        StatementRowKind.Matched => "Matched",
        StatementRowKind.UnmatchedStatement => "Unmatched (statement)",
        _ => "Unmatched (book)",
    };
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
                row.Date.ToString("dd-MMM-yyyy"),
                row.Description,
                IndianFormat.AmountAlways(Math.Abs(row.Amount.Amount)) + (row.Signed >= 0 ? " Cr(in)" : " Dr(out)"),
                string.IsNullOrWhiteSpace(row.InstrumentNumber) ? "—" : row.InstrumentNumber,
                tx.BankDate?.ToString("dd-MMM-yyyy") ?? row.Date.ToString("dd-MMM-yyyy")));

        foreach (var row in result.UnmatchedStatementRows)
            Results.Add(new StatementResultRow(
                StatementRowKind.UnmatchedStatement,
                row.Date.ToString("dd-MMM-yyyy"),
                row.Description,
                IndianFormat.AmountAlways(Math.Abs(row.Amount.Amount)) + (row.Signed >= 0 ? " Cr(in)" : " Dr(out)"),
                string.IsNullOrWhiteSpace(row.InstrumentNumber) ? "—" : row.InstrumentNumber,
                "—"));

        foreach (var tx in result.UnmatchedBookTransactions)
            Results.Add(new StatementResultRow(
                StatementRowKind.UnmatchedBook,
                tx.Date.ToString("dd-MMM-yyyy"),
                $"Voucher No. {tx.VoucherNumber} ({tx.TransactionType})",
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

        return result.MatchedCount;
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
