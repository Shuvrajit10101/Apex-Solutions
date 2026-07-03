using Apex.Ledger.Reports;
using Apex.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Apex.Desktop.ViewModels;

/// <summary>
/// One open-bill row in the Outstandings page (catalog §5): the party, the bill reference, its date and
/// due date, the pending amount and its ageing (overdue days), plus a <see cref="IsSelected"/> flag the
/// spacebar toggles for Ctrl+B bill settlement. Amount/date fields are pre-formatted (right-aligned
/// Indian grouping) so the view binds strings directly.
/// </summary>
public sealed partial class OutstandingRowViewModel : ViewModelBase
{
    /// <summary>The underlying open bill (kept so settlement can knock it off by reference/amount).</summary>
    public OutstandingBill Bill { get; }

    /// <summary>Toggled by the spacebar in the Outstandings page; drives multi-select for Ctrl+B.</summary>
    [ObservableProperty] private bool _isSelected;

    /// <summary>True for the row currently under the keyboard highlight (set by the parent page VM).</summary>
    [ObservableProperty] private bool _isHighlighted;

    public OutstandingRowViewModel(OutstandingBill bill)
    {
        Bill = bill;
        Party = bill.LedgerName;
        Reference = bill.Reference;
        Date = bill.Date.ToString("dd-MMM-yyyy");
        DueDate = bill.DueDate.ToString("dd-MMM-yyyy");
        Pending = IndianFormat.Amount(bill.Pending);
    }

    public string Party { get; }
    public string Reference { get; }
    public string Date { get; }
    public string DueDate { get; }
    public string Pending { get; }

    /// <summary>Overdue days as of the report date (0 when not yet due), formatted for the Ageing column.</summary>
    public string Ageing { get; private set; } = string.Empty;

    /// <summary>Sets the Ageing text from the report's as-of date (kept out of the ctor so it can refresh).</summary>
    public OutstandingRowViewModel WithAgeing(System.DateOnly asOf)
    {
        var days = Bill.OverdueDays(asOf);
        Ageing = days <= 0 ? "Not due" : $"{days} day{(days == 1 ? string.Empty : "s")}";
        return this;
    }
}
