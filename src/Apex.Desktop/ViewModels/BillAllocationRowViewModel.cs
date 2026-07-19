using System;
using System.Collections.Generic;
using System.Globalization;
using Apex.Ledger.Domain;
using CommunityToolkit.Mvvm.ComponentModel;
using Apex.Desktop.Services;

namespace Apex.Desktop.ViewModels;

/// <summary>
/// One editable row in the "Bill-wise Details" sub-panel of a voucher line (catalog §5). It captures the
/// four prompts for a single allocation: <b>Type of Ref</b> (New / Agst / Advance / On-Account),
/// <b>Name</b> (the bill reference id), <b>Due Date</b> (dd-MMM-yyyy, blank ⇒ derive from the ledger's
/// default credit period) and <b>Amount</b> (a magnitude, typed as text). Several rows may hang off one
/// line; their amounts must <b>sum to the line amount</b> ("split"). Parsing/validation is deferred to the
/// parent line — this class only holds the editable state and raises change notifications so the running
/// split total updates as the user types. No Avalonia types ⇒ headlessly unit-testable.
/// </summary>
public sealed partial class BillAllocationRowViewModel : ViewModelBase
{
    private readonly Action _onChanged;

    /// <summary>The four bill-reference types the "Type of Ref" picker offers.</summary>
    public IReadOnlyList<BillRefType> RefTypes { get; } = new[]
    {
        BillRefType.NewRef, BillRefType.AgstRef, BillRefType.Advance, BillRefType.OnAccount,
    };

    [ObservableProperty] private BillRefType _refType = BillRefType.NewRef;
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _dueDateText = string.Empty;
    [ObservableProperty] private string _amountText = string.Empty;

    public BillAllocationRowViewModel(Action onChanged, BillRefType refType = BillRefType.NewRef)
    {
        _onChanged = onChanged ?? throw new ArgumentNullException(nameof(onChanged));
        _refType = refType;
    }

    partial void OnRefTypeChanged(BillRefType value) => _onChanged();
    partial void OnNameChanged(string value) => _onChanged();
    partial void OnDueDateTextChanged(string value) => _onChanged();
    partial void OnAmountTextChanged(string value) => _onChanged();

    /// <summary>On-Account carries no bill name; a name is required for New/Agst/Advance.</summary>
    public bool NameRequired => RefType != BillRefType.OnAccount;

    /// <summary>The parsed amount magnitude (0 when unparsable/blank).</summary>
    public decimal ParsedAmount => TryParseAmount(out var amt) ? amt : 0m;

    /// <summary>
    /// The parsed explicit due date (WI-5 shared day-first parser), or null when blank/unparsable
    /// (blank legitimately means "derive from the credit period").
    /// </summary>
    public DateOnly? ParsedDueDate =>
        ApexDate.TryParse(DueDateText, out var d) ? d : (DateOnly?)null;

    /// <summary>
    /// True when a due date was TYPED but cannot be read (WI-5). Blank derives from the credit period;
    /// unreadable text must NOT silently do the same, so the parent refuses on it.
    /// </summary>
    public bool HasUnreadableDueDate =>
        !string.IsNullOrWhiteSpace(DueDateText) && ParsedDueDate is null;

    /// <summary>True once this row is touched at all (a name or an amount) — a fully-blank row is ignored.</summary>
    public bool IsBlank => string.IsNullOrWhiteSpace(Name) && string.IsNullOrWhiteSpace(AmountText);

    /// <summary>
    /// True when this row is a complete allocation: a positive amount, and (unless On-Account) a name.
    /// </summary>
    public bool IsComplete
    {
        get
        {
            if (ParsedAmount <= 0m) return false;
            return !NameRequired || !string.IsNullOrWhiteSpace(Name);
        }
    }

    /// <summary>
    /// Builds the domain <see cref="BillAllocation"/> for this row. The caller only invokes this for a
    /// complete row; an explicit due date is passed through, otherwise the ledger's default credit period
    /// derives it at posting time.
    /// </summary>
    public BillAllocation ToAllocation() => new(
        RefType,
        RefType == BillRefType.OnAccount ? (Name ?? string.Empty) : Name.Trim(),
        new Apex.Ledger.Money(ParsedAmount),
        dueDate: ParsedDueDate);

    private bool TryParseAmount(out decimal amount)
        => decimal.TryParse(
            (AmountText ?? string.Empty).Trim(),
            NumberStyles.AllowDecimalPoint | NumberStyles.AllowThousands,
            CultureInfo.InvariantCulture,
            out amount);
}
