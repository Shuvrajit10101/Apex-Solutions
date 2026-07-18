using System;
using System.Collections.ObjectModel;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Apex.Desktop.ViewModels;

/// <summary>
/// The RQ-7 ledger-vouchers drill target: the page column opened when a Trial-Balance / Balance-Sheet /
/// Profit-&amp;-Loss ledger row is drilled into (Enter). It hosts the engine <see cref="LedgerBook"/> projection —
/// the drilled ledger's opening balance, then every posting to it in the report period with a running balance,
/// then its closing (which equals the TB/BS/P&amp;L figure the user drilled from). Each posting row is itself
/// drillable into the underlying voucher's detail, so the cascade continues one level deeper.
/// <para>Rendered as a clean Miller column beside the report it drilled from (the report pane persists); it is
/// UI-toolkit-free so it is unit-testable.</para>
/// </summary>
public sealed partial class LedgerVouchersViewModel : ViewModelBase
{
    private readonly Company _company;

    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private string _subtitle = string.Empty;

    /// <summary>The drilled ledger's id — the identity the header/tests key on.</summary>
    public Guid LedgerId { get; }

    /// <summary>The presentation rows: an opening line, one line per posting (drillable), then the closing line.</summary>
    public ObservableCollection<ReportRow> Rows { get; } = new();

    /// <summary>
    /// The highlighted posting row (two-way bound to the ledger-vouchers ListBox <c>SelectedItem</c>). The
    /// shell reads this on Enter so the keyboard drill into the voucher detail does not depend on which control
    /// holds focus (RQ-7 defect-1).
    /// </summary>
    [ObservableProperty] private ReportRow? _selectedRow;

    /// <summary>
    /// Raised when a posting row is drilled into (Enter): carries the underlying voucher id so the shell opens
    /// that voucher's read-only detail as a new cascading column (one level deeper than this ledger-book).
    /// </summary>
    public event Action<Guid>? DrillToVoucherRequested;

    /// <param name="movement">
    /// True when this book was opened from a Profit-&amp;-Loss (flow) line: the running balance starts at 0 and
    /// the closing equals the in-window period movement — reconciling to the P&amp;L figure the user drilled.
    /// False (Trial-Balance / Balance-Sheet point-in-time drill): the running balance carries the opening
    /// forward and the closing is the cumulative closing-as-at-To (the displayed closing balance).
    /// </param>
    public LedgerVouchersViewModel(Company company, Guid ledgerId, DateOnly from, DateOnly to, bool movement = false)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));
        LedgerId = ledgerId;

        var book = LedgerBook.Build(company, ledgerId, from, to, movement);
        Title = book.LedgerName.Length == 0 ? "Ledger Vouchers" : book.LedgerName;
        Subtitle = $"{company.Name}  —  {FormatDate(from)} to {FormatDate(to)}";

        // Opening line (running balance origin) — a heading, never drillable. A movement book starts at 0,
        // which is a meaningful "period opening" so it is always rendered (AmountAlways), never blanked.
        Rows.Add(new ReportRow
        {
            Particulars = FormatDate(from),
            Secondary = movement ? "Opening (period)" : "Opening Balance",
            Amount = movement
                ? IndianFormat.SignedAlways(book.OpeningAmount, book.OpeningSide)
                : IndianFormat.Signed(book.OpeningAmount, book.OpeningSide),
            IsHeader = true,
        });

        foreach (var r in book.Rows)
        {
            var counter = string.IsNullOrEmpty(r.CounterParticulars) ? string.Empty : r.CounterParticulars;
            Rows.Add(new ReportRow
            {
                Particulars = $"{FormatDate(r.Date)}  {r.VoucherTypeName} No. {r.Number}",
                Secondary = counter,
                Debit = r.Debit != Money.Zero ? IndianFormat.Amount(r.Debit) : string.Empty,
                Credit = r.Credit != Money.Zero ? IndianFormat.Amount(r.Credit) : string.Empty,
                Amount = IndianFormat.Signed(r.RunningAmount, r.RunningSide),
                IsTwoColumn = true,
                DrillVoucherId = r.VoucherId,   // RQ-7: Enter drills one level deeper into the voucher detail
            });
        }

        // Closing line (== the report figure the user drilled from) — a total, never drillable. For a P&L
        // (movement) drill this is the in-window period movement; for a TB/BS drill it is the closing balance.
        Rows.Add(new ReportRow
        {
            Particulars = movement ? "Period Movement" : "Closing Balance",
            Amount = IndianFormat.SignedAlways(book.ClosingAmount, book.ClosingSide),
            IsTotal = true,
        });
    }

    /// <summary>
    /// The RQ-7 drill on a ledger-vouchers row: Enter on a posting row opens the underlying voucher's detail.
    /// A safe no-op on the opening/closing lines (they carry no voucher id).
    /// </summary>
    public void Drill(ReportRow? row)
    {
        if (row?.DrillVoucherId is { } id && id != Guid.Empty)
            DrillToVoucherRequested?.Invoke(id);
    }

    private static string FormatDate(DateOnly d) => d.ToString("dd-MMM-yyyy");
}
