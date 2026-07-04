using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Apex.Desktop.ViewModels;

/// <summary>The report kinds surfaced in the reports viewer — the four Phase-1 accounting reports plus the
/// nine Phase-3 inventory reports (slice 3.4b).</summary>
public enum ReportKind
{
    TrialBalance,
    BalanceSheet,
    ProfitAndLoss,
    DayBook,

    // ---- inventory reports (slice 3.4b) ----
    StockSummary,
    GodownSummary,
    StockItemMovement,
    ReceiptNoteRegister,
    DeliveryNoteRegister,
    RejectionRegister,
    PhysicalStockRegister,
    OrderRegister,
    ReorderStatus,
}

/// <summary>
/// Builds the report content for the current company, reading the numbers
/// straight from the <see cref="Apex.Ledger.Reports"/> pure projections. The as-of date is the
/// last voucher date (or the financial-year end when there are no vouchers), so a freshly loaded
/// demo shows its full picture. Exposes the row lists the report views bind to.
/// </summary>
public sealed partial class ReportsViewModel : ViewModelBase
{
    private readonly Company _company;
    private readonly DateOnly _asOf;

    /// <summary>The stock item a Stock Item Movement report is scoped to (null for the other reports).</summary>
    private Guid? _movementItemId;

    [ObservableProperty] private ReportKind _kind;
    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private string _subtitle = string.Empty;
    [ObservableProperty] private bool _isTwoColumn; // Dr/Cr grid (TB, BS) vs single-amount (P&L, DayBook)

    public ObservableCollection<ReportRow> Rows { get; } = new();

    // ---- inventory-report layout flags (drive which DataTemplate the view shows; slice 3.4b) ----

    /// <summary>True for any of the nine inventory reports (they use the wide inventory grids, not the
    /// accounting Particulars/Dr/Cr/Amount grid).</summary>
    public bool IsInventoryReport => Kind is ReportKind.StockSummary or ReportKind.GodownSummary
        or ReportKind.StockItemMovement or ReportKind.ReceiptNoteRegister or ReportKind.DeliveryNoteRegister
        or ReportKind.RejectionRegister or ReportKind.PhysicalStockRegister or ReportKind.OrderRegister
        or ReportKind.ReorderStatus;

    /// <summary>True to show the accounting (Particulars/Dr/Cr/Amount) grid — every non-inventory report.</summary>
    public bool IsAccountingReport => !IsInventoryReport;

    public bool IsStockSummary => Kind == ReportKind.StockSummary;
    public bool IsGodownSummary => Kind == ReportKind.GodownSummary;
    public bool IsStockMovement => Kind == ReportKind.StockItemMovement;
    public bool IsReorderStatus => Kind == ReportKind.ReorderStatus;
    public bool IsPhysicalStockRegister => Kind == ReportKind.PhysicalStockRegister;
    public bool IsOrderRegister => Kind == ReportKind.OrderRegister;

    /// <summary>True for the three allocation registers (Receipt Note / Delivery Note / Rejection), which
    /// share the same wide Date | No. | Party | Item | Godown | Qty | Rate | Value | Batch layout.</summary>
    public bool IsAllocationRegister => Kind is ReportKind.ReceiptNoteRegister
        or ReportKind.DeliveryNoteRegister or ReportKind.RejectionRegister;

    /// <summary>
    /// Raised when a Stock-Summary row is drilled into (Enter / double-click a stock item): carries the
    /// stock item id so the shell can open that item's Stock Item Movement report. The shell (not this VM)
    /// owns opening a new page column, so the drill is surfaced as an event.
    /// </summary>
    public event Action<Guid>? DrillToMovementRequested;

    /// <summary>
    /// The scenario picker options: "Actual (no scenario)" first (a null <see cref="ScenarioOption.Scenario"/>),
    /// then every scenario defined on the company. Only meaningful for the balance reports (TB / P&amp;L / BS);
    /// the Day Book always shows the real books. Empty of scenarios ⇒ only the Actual option is offered.
    /// </summary>
    public ObservableCollection<ScenarioOption> Scenarios { get; } = new();

    /// <summary>The chosen scenario option; changing it rebuilds the current report under that scenario.</summary>
    [ObservableProperty] private ScenarioOption? _selectedScenario;

    /// <summary>True when this report kind can be viewed under a scenario (TB / P&amp;L / Balance Sheet).</summary>
    public bool SupportsScenario => Kind is ReportKind.TrialBalance or ReportKind.BalanceSheet or ReportKind.ProfitAndLoss;

    /// <summary>The scenario currently applied (null = actual books).</summary>
    private Scenario? CurrentScenario => SupportsScenario ? SelectedScenario?.Scenario : null;

    /// <summary>The company display name (for the header line).</summary>
    public string CompanyName => _company.Name;

    /// <summary>
    /// Builds the report of <paramref name="kind"/> for <paramref name="company"/>. For a
    /// <see cref="ReportKind.StockItemMovement"/> report, <paramref name="stockItemId"/> names the item to
    /// scope it to (the Stock-Summary drill target); it is ignored by the other report kinds.
    /// </summary>
    public ReportsViewModel(Company company, ReportKind kind, Guid? stockItemId = null)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));
        _asOf = ComputeAsOf(company);
        _movementItemId = stockItemId;

        Scenarios.Add(ScenarioOption.Actual);
        foreach (var s in company.Scenarios.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase))
            Scenarios.Add(new ScenarioOption(s));
        _selectedScenario = Scenarios[0];

        Show(kind);
    }

    /// <summary>Rebuilds the current report whenever the scenario picker changes.</summary>
    partial void OnSelectedScenarioChanged(ScenarioOption? value) => Show(Kind);

    /// <summary>Switches the displayed report and rebuilds its rows (under the selected scenario, if any).</summary>
    public void Show(ReportKind kind)
    {
        Kind = kind;
        OnPropertyChanged(nameof(SupportsScenario));
        // The layout flags are computed from Kind; notify the view so the right DataTemplate shows.
        OnPropertyChanged(nameof(IsInventoryReport));
        OnPropertyChanged(nameof(IsAccountingReport));
        OnPropertyChanged(nameof(IsStockSummary));
        OnPropertyChanged(nameof(IsGodownSummary));
        OnPropertyChanged(nameof(IsStockMovement));
        OnPropertyChanged(nameof(IsReorderStatus));
        OnPropertyChanged(nameof(IsPhysicalStockRegister));
        OnPropertyChanged(nameof(IsOrderRegister));
        OnPropertyChanged(nameof(IsAllocationRegister));
        Rows.Clear();

        switch (kind)
        {
            case ReportKind.TrialBalance: BuildTrialBalance(); break;
            case ReportKind.BalanceSheet: BuildBalanceSheet(); break;
            case ReportKind.ProfitAndLoss: BuildProfitAndLoss(); break;
            case ReportKind.DayBook: BuildDayBook(); break;

            case ReportKind.StockSummary: BuildStockSummary(); break;
            case ReportKind.GodownSummary: BuildGodownSummary(); break;
            case ReportKind.StockItemMovement: BuildStockItemMovement(); break;
            case ReportKind.ReceiptNoteRegister: BuildAllocationRegister("Receipt Note Register", Report.BuildReceiptNoteRegister); break;
            case ReportKind.DeliveryNoteRegister: BuildAllocationRegister("Delivery Note Register", Report.BuildDeliveryNoteRegister); break;
            case ReportKind.RejectionRegister: BuildAllocationRegister("Rejection Register", Report.BuildRejectionRegister); break;
            case ReportKind.PhysicalStockRegister: BuildPhysicalStockRegister(); break;
            case ReportKind.OrderRegister: BuildOrderRegister(); break;
            case ReportKind.ReorderStatus: BuildReorderStatus(); break;
        }
    }

    /// <summary>
    /// Drills a Stock-Summary item row into that item's Stock Item Movement report — the keyboard-first
    /// drill (Enter on a highlighted item row) and the double-click drill both route here. Raises
    /// <see cref="DrillToMovementRequested"/> so the shell opens the movement report as a new page column.
    /// A no-op unless this is the Stock Summary and the row carries a drill item id.
    /// </summary>
    public void Drill(ReportRow? row)
    {
        if (Kind != ReportKind.StockSummary) return;
        if (row?.DrillStockItemId is { } itemId)
            DrillToMovementRequested?.Invoke(itemId);
    }

    /// <summary>" under scenario <name>" suffix for the subtitle, or empty when showing the actual books.</summary>
    private string ScenarioSuffix =>
        CurrentScenario is { } s ? $"  —  under scenario “{s.Name}”" : string.Empty;

    // --------------------------------------------------------------- Trial Balance

    private void BuildTrialBalance()
    {
        var tb = TrialBalance.Build(_company, _asOf, CurrentScenario);
        Title = "Trial Balance";
        Subtitle = $"{CompanyName}  —  as at {FormatDate(_asOf)}{ScenarioSuffix}";
        IsTwoColumn = true;

        foreach (var row in tb.Rows.OrderBy(r => r.GroupName).ThenBy(r => r.LedgerName))
            Rows.Add(ReportRow.DrCrLine(row.LedgerName, row.Debit, row.Credit, row.GroupName));

        Rows.Add(ReportRow.DrCrTotal("Grand Total", tb.TotalDebit, tb.TotalCredit));
    }

    // --------------------------------------------------------------- Balance Sheet

    private void BuildBalanceSheet()
    {
        var bs = BalanceSheet.Build(_company, _asOf, scenario: CurrentScenario);
        Title = "Balance Sheet";
        Subtitle = $"{CompanyName}  —  as at {FormatDate(_asOf)}{ScenarioSuffix}";
        IsTwoColumn = false;

        Rows.Add(new ReportRow { Particulars = "Liabilities", IsHeader = true });
        foreach (var l in bs.Liabilities.Where(l => l.Amount != Money.Zero))
            Rows.Add(ReportRow.Line(l.Name, l.Amount, l.GroupName));
        Rows.Add(ReportRow.Total("Total Liabilities", bs.TotalLiabilities));

        Rows.Add(new ReportRow { Particulars = "Assets", IsHeader = true });
        foreach (var a in bs.Assets.Where(a => a.Amount != Money.Zero))
            Rows.Add(ReportRow.Line(a.Name, a.Amount, a.GroupName));
        Rows.Add(ReportRow.Total("Total Assets", bs.TotalAssets));
    }

    // --------------------------------------------------------------- Profit & Loss

    private void BuildProfitAndLoss()
    {
        var pl = ProfitAndLoss.Build(_company, _asOf, ClosingStockMode.AsPostedLedger, CurrentScenario);
        Title = "Profit & Loss A/c";
        Subtitle = $"{CompanyName}  —  for the period ending {FormatDate(_asOf)}{ScenarioSuffix}";
        IsTwoColumn = false;

        Rows.Add(new ReportRow { Particulars = "Income", IsHeader = true });
        foreach (var i in pl.Income)
            Rows.Add(ReportRow.Line(i.LedgerName, i.Amount));
        Rows.Add(ReportRow.Total("Total Income", pl.TotalIncome));

        Rows.Add(new ReportRow { Particulars = "Expenses", IsHeader = true });
        foreach (var e in pl.Expenses)
            Rows.Add(ReportRow.Line(e.LedgerName, e.Amount));
        Rows.Add(ReportRow.Total("Total Expenses", pl.TotalExpenses));

        var isProfit = pl.NetProfit.Amount >= 0m;
        var label = isProfit ? "Net Profit" : "Net Loss";
        var magnitude = new Money(Math.Abs(pl.NetProfit.Amount));
        Rows.Add(ReportRow.Total(label, magnitude));
    }

    // --------------------------------------------------------------- Day Book

    private void BuildDayBook()
    {
        var from = _company.BooksBeginFrom;
        var rows = DayBook.Build(_company, from, _asOf);
        Title = "Day Book";
        Subtitle = $"{CompanyName}  —  {FormatDate(from)} to {FormatDate(_asOf)}";
        IsTwoColumn = false;

        foreach (var r in rows)
        {
            var particulars = $"{r.VoucherTypeName} No. {r.Number}";
            var secondary = r.PartyOrParticulars ?? string.Empty;
            var amt = IndianFormat.Amount(r.Amount);
            Rows.Add(new ReportRow
            {
                Particulars = $"{FormatDate(r.Date)}  {particulars}",
                Secondary = r.IsCancelled ? "(Cancelled) " + secondary : secondary,
                Amount = amt,
            });
        }

        if (rows.Count == 0)
            Rows.Add(new ReportRow { Particulars = "No vouchers in this period.", IsHeader = true });
    }

    // =============================================================== inventory reports (slice 3.4b)

    private DateOnly BooksFrom => _company.BooksBeginFrom;

    // --------------------------------------------------------------- Stock Summary  (Item | Closing Qty | Rate | Value)

    private void BuildStockSummary()
    {
        var ss = Report.BuildStockSummary(_company, _asOf);
        Title = "Stock Summary";
        Subtitle = $"{CompanyName}  —  as at {FormatDate(_asOf)}";

        foreach (var r in ss.Rows)
        {
            var unitRate = r.ClosingQuantity != 0m
                ? IndianFormat.Amount(r.ClosingValue.Amount / r.ClosingQuantity)
                : string.Empty;
            Rows.Add(new ReportRow
            {
                // Col1 Item, Col2 Inward, Col3 Outward, Col4 Closing Qty, Col5 Rate, Col6 Closing Value.
                Col1 = r.ItemName,
                Col2 = IndianFormat.Quantity(r.InwardQuantity),
                Col3 = IndianFormat.Quantity(r.OutwardQuantity),
                Col4 = IndianFormat.Quantity(r.ClosingQuantity),
                Col5 = unitRate,
                Col6 = IndianFormat.Amount(r.ClosingValue),
                DrillStockItemId = r.StockItemId,   // Enter / double-click → Stock Item Movement
            });
        }

        Rows.Add(new ReportRow
        {
            Col1 = "Grand Total",
            Col6 = IndianFormat.AmountAlways(ss.TotalClosingValue),
            IsTotal = true,
        });
    }

    // --------------------------------------------------------------- Godown Summary  (Godown | Item | Qty | Value)

    private void BuildGodownSummary()
    {
        var gs = Report.BuildGodownSummary(_company, _asOf);
        Title = "Godown Summary";
        Subtitle = $"{CompanyName}  —  as at {FormatDate(_asOf)}";

        foreach (var r in gs.Rows)
            Rows.Add(new ReportRow
            {
                Col1 = r.GodownName,
                Col2 = r.ItemName,
                Col3 = IndianFormat.Quantity(r.ClosingQuantity),
                Col4 = IndianFormat.Amount(r.ClosingValue),
            });

        Rows.Add(new ReportRow
        {
            Col1 = "Grand Total",
            Col4 = IndianFormat.AmountAlways(gs.TotalClosingValue),
            IsTotal = true,
        });
    }

    // --------------------------------------------------------------- Stock Item Movement  (Date | Voucher | In | Out | Balance | Value)

    private void BuildStockItemMovement()
    {
        var itemId = _movementItemId ?? _company.StockItems.FirstOrDefault()?.Id;
        if (itemId is not { } id)
        {
            Title = "Stock Item Movement";
            Subtitle = $"{CompanyName}  —  no stock items";
            Rows.Add(new ReportRow { Particulars = "No stock items.", IsHeader = true });
            return;
        }

        var mv = Report.BuildStockItemMovement(_company, id, _asOf);
        Title = "Stock Item Movement";
        Subtitle = $"{CompanyName}  —  {mv.ItemName}  —  {FormatDate(mv.From)} to {FormatDate(mv.To)}";

        // Opening line so the running balance reads from the carried-forward on-hand.
        Rows.Add(new ReportRow
        {
            Col1 = FormatDate(mv.From),
            Col2 = "Opening Balance",
            Col5 = IndianFormat.Quantity(mv.OpeningQuantity),
            IsHeader = true,
        });

        foreach (var r in mv.Rows)
        {
            var voucher = r.Number > 0 ? $"{r.VoucherTypeName} No. {r.Number}" : r.VoucherTypeName;
            Rows.Add(new ReportRow
            {
                Col1 = FormatDate(r.Date),
                Col2 = voucher,
                Col3 = r.InwardQuantity != 0m ? IndianFormat.Quantity(r.InwardQuantity) : string.Empty,
                Col4 = r.OutwardQuantity != 0m ? IndianFormat.Quantity(r.OutwardQuantity) : string.Empty,
                Col5 = IndianFormat.Quantity(r.RunningQuantity),
                Col6 = IndianFormat.Amount(r.RunningValue),
            });
        }

        Rows.Add(new ReportRow
        {
            Col1 = "Closing Balance",
            Col5 = IndianFormat.Quantity(mv.ClosingQuantity),
            Col6 = IndianFormat.AmountAlways(mv.ClosingValue),
            IsTotal = true,
        });
    }

    // --------------------------------------------------------------- Allocation registers (Receipt/Delivery/Rejection)
    //   Date | No. | Party | Item | Godown | Qty | Rate | Value | Batch

    private void BuildAllocationRegister(
        string title, Func<Company, DateOnly, DateOnly, IReadOnlyList<InventoryRegisterRow>> build)
    {
        var rows = build(_company, BooksFrom, _asOf);
        Title = title;
        Subtitle = $"{CompanyName}  —  {FormatDate(BooksFrom)} to {FormatDate(_asOf)}";

        var total = Money.Zero;
        foreach (var r in rows)
        {
            var qtySigned = r.Direction == StockDirection.Inward ? r.Quantity : -r.Quantity;
            Rows.Add(new ReportRow
            {
                Col1 = FormatDate(r.Date),
                Col2 = r.Number.ToString(),
                Col3 = r.PartyName ?? string.Empty,
                Col4 = r.ItemName,
                Col5 = r.GodownName,
                Col6 = IndianFormat.Quantity(qtySigned),
                Col7 = r.Rate is { } rate ? IndianFormat.Amount(rate) : string.Empty,
                Col8 = IndianFormat.Amount(r.Value),
                Secondary = r.BatchLabel ?? string.Empty,
            });
            total += r.Value;
        }

        if (rows.Count == 0)
            Rows.Add(new ReportRow { Col4 = "No entries in this period.", IsHeader = true });
        else
            Rows.Add(new ReportRow { Col4 = "Grand Total", Col8 = IndianFormat.AmountAlways(total), IsTotal = true });
    }

    // --------------------------------------------------------------- Physical Stock register (Date | Item | Godown | Book | Counted | Variance)

    private void BuildPhysicalStockRegister()
    {
        var rows = Report.BuildPhysicalStockRegister(_company, BooksFrom, _asOf);
        Title = "Physical Stock Register";
        Subtitle = $"{CompanyName}  —  {FormatDate(BooksFrom)} to {FormatDate(_asOf)}";

        foreach (var r in rows)
            Rows.Add(new ReportRow
            {
                Col1 = FormatDate(r.Date),
                Col2 = r.ItemName,
                Col3 = r.GodownName,
                Col4 = IndianFormat.Quantity(r.BookQuantity),
                Col5 = IndianFormat.Quantity(r.CountedQuantity),
                Col6 = IndianFormat.Quantity(r.Variance),
                Secondary = r.BatchLabel ?? string.Empty,
            });

        if (rows.Count == 0)
            Rows.Add(new ReportRow { Col2 = "No physical-stock counts in this period.", IsHeader = true });
    }

    // --------------------------------------------------------------- Order register (Date | No. | Party | Item | Godown | Ordered | Pending | Rate)

    private void BuildOrderRegister()
    {
        var rows = Report.BuildOrderRegister(_company, BooksFrom, _asOf);
        Title = "Order Register";
        Subtitle = $"{CompanyName}  —  {FormatDate(BooksFrom)} to {FormatDate(_asOf)}";

        foreach (var r in rows)
            Rows.Add(new ReportRow
            {
                Col1 = FormatDate(r.Date),
                Col2 = $"{r.VoucherTypeName} No. {r.Number}",
                Col3 = r.PartyName ?? string.Empty,
                Col4 = r.ItemName,
                Col5 = r.GodownName,
                Col6 = IndianFormat.Quantity(r.OrderedQuantity),
                Col7 = IndianFormat.Quantity(r.OutstandingQuantity),
                Col8 = r.Rate is { } rate ? IndianFormat.Amount(rate) : string.Empty,
            });

        if (rows.Count == 0)
            Rows.Add(new ReportRow { Col4 = "No orders in this period.", IsHeader = true });
    }

    // --------------------------------------------------------------- Reorder Status (Item | Closing | Reorder Level | Shortfall | Suggested)

    private void BuildReorderStatus()
    {
        var rs = Report.BuildReorderStatus(_company, _asOf);
        Title = "Reorder Status";
        Subtitle = $"{CompanyName}  —  as at {FormatDate(_asOf)}";

        foreach (var r in rs.Rows)
            Rows.Add(new ReportRow
            {
                Col1 = r.ItemName,
                Col2 = IndianFormat.Quantity(r.ClosingQuantity),
                Col3 = IndianFormat.Quantity(r.ReorderLevel),
                Col4 = IndianFormat.Quantity(r.Shortfall),
                Col5 = IndianFormat.Quantity(r.SuggestedOrderQuantity),
            });

        if (rs.Rows.Count == 0)
            Rows.Add(new ReportRow { Col1 = "All items are above their reorder levels.", IsHeader = true });
    }

    // --------------------------------------------------------------- helpers

    private static DateOnly ComputeAsOf(Company company)
    {
        DateOnly? last = null;
        foreach (var v in company.Vouchers)
            if (last is null || v.Date > last.Value)
                last = v.Date;

        // Default to the financial-year end when there are no vouchers.
        return last ?? company.FinancialYearStart.AddYears(1).AddDays(-1);
    }

    private static string FormatDate(DateOnly d) => d.ToString("dd-MMM-yyyy");
}

/// <summary>
/// One entry in a report's scenario picker: either the actual books (<see cref="Scenario"/> is null,
/// shown as "Actual (no scenario)") or a defined <see cref="Domain.Scenario"/>. The <see cref="Display"/>
/// is what the combo shows.
/// </summary>
public sealed class ScenarioOption
{
    /// <summary>The wrapped scenario, or null for the real (actual-books) option.</summary>
    public Scenario? Scenario { get; }

    /// <summary>The combo label ("Actual (no scenario)" or the scenario name).</summary>
    public string Display { get; }

    public ScenarioOption(Scenario? scenario)
    {
        Scenario = scenario;
        Display = scenario is null ? "Actual (no scenario)" : scenario.Name;
    }

    /// <summary>The shared "actual books" option (a null scenario).</summary>
    public static ScenarioOption Actual { get; } = new((Scenario?)null);
}
