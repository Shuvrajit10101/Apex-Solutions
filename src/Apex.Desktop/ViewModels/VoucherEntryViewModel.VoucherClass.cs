using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.ViewModels;

/// <summary>One row of the item-invoice header's <b>Voucher Class</b> picker. A <c>null</c>
/// <see cref="Class"/> is the "not applicable" lead row — entry without a class, which must post byte-identically
/// to a build that had never heard of census row 2.6.</summary>
public sealed class VoucherClassOption
{
    public VoucherClass? Class { get; init; }
    public string Display { get; init; } = string.Empty;
}

/// <summary>
/// 🔴 <b>CENSUS ROW 2.6 — THE CONSUMPTION HALF OF THE VOUCHER CLASS.</b>
/// </summary>
/// <remarks>
/// <para><b>What was already here, and what was missing.</b> Schema v62 landed the whole <i>definition</i> side of
/// the row: the <see cref="VoucherClass"/> aggregate with its two child tables, the SQLite tables behind them, the
/// Voucher Type master screen that keys them, and <see cref="VoucherClassPosting"/>, which turns a class and an
/// invoice value into ledger legs. It landed <b>no caller</b>. <c>VoucherClassPosting.Compute</c> had zero
/// references anywhere in <c>src/</c> — its only callers were three test files — so an operator could define a
/// class in full and then had no way whatever to use one: no picker at voucher entry, and no code path that turned
/// a class into a posted leg. That is this project's own documented "complete, correct, tested and COMPLETELY
/// DEAD" shape. This file is the caller.</para>
///
/// <para><b>R7 — ATTESTED.</b>
/// <c>help.tallysolutions.com/tally-prime/accounting/voucher-types-tally/</c> — <i>"Use a Voucher Class when you
/// regularly record similar transactions and want to automate entries"</i>, and for a Sales type <i>"you can also
/// specify the ledgers to be allocated automatically for inventory items under <b>Default Accounting Allocations
/// for all items in Invoice</b>"</i>. Rounding vocabulary — the four method names, the <i>"Rounding limit"</i> field
/// and the round-off ledger carrying the difference as a positive or negative value, with the worked example that
/// rounds an invoice of <b>125.60 down to 125 leaving (-)0.60</b> on the round-off ledger:
/// <c>help.tallysolutions.com/tally-prime/accounting/round-off-invoice-and-ledger-values/</c>.</para>
///
/// <para>🔴 <b>HIDING THE SALES-LEDGER FIELD — THE CITATION, AND THE DIVERGENCE IT EXPOSES.</b> The vendor's
/// statement is on <c>help.tallysolutions.com/accounting-faq/</c>, NOT on the voucher-types page, and it is
/// CONDITIONAL on two things, not one: the field to select the Sales ledger is not available <i>if you have
/// selected a voucher class</i> to automate the sales details <b>AND</b> if <c>F12 &gt; "Select common ledger
/// account for Item allocation"</c> is set to <b>No</b>. This application does not model that F12 configuration at
/// all, so <see cref="ShowStockLedgerPicker"/> implements only the first condition and hides the field whenever the
/// class pre-maps ledgers. That is the vendor's behaviour at the shipped default and never contradicts it, but it
/// is a NARROWER rule than the vendor's — an operator who, in the reference application, had turned that F12 flag
/// ON would there keep a common-ledger field this screen does not offer. <b>Recorded as OURS, not as attested.</b>
/// Modelling the F12 flag is the work that would close it.</para>
///
/// <para>🔴 <b>THE GST ANCHOR, AND WHY IT IS NOT A GUESS.</b> Without a class the GST resolver falls back to the
/// single Sales/Purchases ledger when the item itself carries no rate. A class replaces that one ledger with a
/// table of them. When the table names exactly ONE ledger — the ordinary "pre-map the sales ledger" case — the
/// anchor is that ledger and nothing changes. When it names several, <b>no</b> ledger-level rate can be the right
/// one, because the invoice value is deliberately being split across ledgers that may disagree; so the anchor is
/// dropped and the rate must come from the item or the company. An item that then cannot resolve is refused BY
/// NAME by the existing message, which is loud. Picking the first row, or the biggest row, would be silent and
/// would be ours rather than the vendor's.</para>
/// </remarks>
public sealed partial class VoucherEntryViewModel
{
    /// <summary>The classes this voucher type offers at entry, "◦ Not Applicable" first.</summary>
    public ObservableCollection<VoucherClassOption> VoucherClassOptions { get; } = new();

    [ObservableProperty] private VoucherClassOption? _selectedVoucherClass;

    /// <summary>
    /// The accounting classes on this type — every class that pre-maps a ledger or adds one. A class that does
    /// NEITHER is not offered: on a Sales/Purchase type that is an empty shell which would post nothing, and the
    /// inter-godown transfer class (census 9.9) is a Stock Journal's and never reaches this screen at all.
    /// </summary>
    private IEnumerable<VoucherClass> AccountingClasses =>
        _type.Classes.Where(c => c.LedgerAllocations.Count > 0 || c.AdditionalEntries.Count > 0);

    /// <summary>Gate for the header picker: an item invoice on a type that actually has a class to offer. A type
    /// with none renders exactly as it did before this file existed (ER-13).</summary>
    public bool ShowVoucherClassSelector => IsItemInvoice && VoucherClassOptions.Count > 1;

    /// <summary>The class in force on this voucher, or <c>null</c>. Only ever non-null in item-invoice mode — a
    /// Ctrl+H out of the mode must not leave a class silently posting on a plain Dr/Cr grid that never showed it.</summary>
    public VoucherClass? ActiveVoucherClass =>
        IsItemInvoice ? SelectedVoucherClass?.Class : null;

    /// <summary>True when the class supplies the value-leg ledgers, so the Sales/Purchases picker stands down.</summary>
    public bool ClassSuppliesValueLedgers => ActiveVoucherClass is { LedgerAllocations.Count: > 0 };

    /// <summary>Gate for the Sales/Purchases value-ledger field. See the R7 note above for why a class hides it.</summary>
    public bool ShowStockLedgerPicker => IsItemInvoice && !ClassSuppliesValueLedgers;

    /// <summary>The ledger the GST resolver falls back to for a rate. See the anchor note in the remarks.</summary>
    private DomainLedger? GstAnchorLedger
    {
        get
        {
            if (ActiveVoucherClass is not { LedgerAllocations.Count: > 0 } cls) return SelectedStockLedger;
            if (cls.LedgerAllocations.Count > 1) return null;
            return _company.FindLedger(cls.LedgerAllocations[0].LedgerId);
        }
    }

    /// <summary>
    /// Fills the picker from the type. Called from the constructor and again whenever the class tables could have
    /// moved under the screen; re-resolves the current selection BY ID so a class already chosen stays chosen.
    /// </summary>
    public void BuildVoucherClassOptions()
    {
        var chosen = SelectedVoucherClass?.Class?.Id;

        VoucherClassOptions.Clear();
        VoucherClassOptions.Add(new VoucherClassOption { Class = null, Display = "◦ Not Applicable" });
        foreach (var c in AccountingClasses.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
            VoucherClassOptions.Add(new VoucherClassOption { Class = c, Display = c.Name });

        SelectedVoucherClass = VoucherClassOptions.FirstOrDefault(o => o.Class?.Id == chosen)
                               ?? VoucherClassOptions[0];
        NotifyVoucherClassGates();
    }

    partial void OnSelectedVoucherClassChanged(VoucherClassOption? value)
    {
        // 🔴 A class that pre-maps ledgers takes the value-leg field off the screen, so the field's own selection
        // must not go on silently deciding anything. It is left intact (a Ctrl+H back out, or clearing the class,
        // restores it untouched) but BuildItemInvoice reads ClassSuppliesValueLedgers, never this field, when a
        // class is in force.
        NotifyVoucherClassGates();
        if (IsItemInvoice) RecalculateItemInvoice();
    }

    /// <summary>Pushes every gate this file owns. Called on selection change and from <c>OnModeChanged</c> — a mode
    /// flip changes <see cref="ActiveVoucherClass"/> itself, so omitting it leaves the value-ledger field hidden on
    /// a grid that has no class.</summary>
    private void NotifyVoucherClassGates()
    {
        OnPropertyChanged(nameof(ShowVoucherClassSelector));
        OnPropertyChanged(nameof(ActiveVoucherClass));
        OnPropertyChanged(nameof(ClassSuppliesValueLedgers));
        OnPropertyChanged(nameof(ShowStockLedgerPicker));
        OnPropertyChanged(nameof(VoucherClassSummary));
    }

    /// <summary>A one-line statement, under the picker, of what this class will post without asking. A class posts
    /// money with no prompt; the operator is at least told which ledgers and on what basis before they accept.</summary>
    public string VoucherClassSummary
    {
        get
        {
            if (ActiveVoucherClass is not { } cls) return string.Empty;

            var parts = new List<string>();
            if (cls.LedgerAllocations.Count > 0)
            {
                var names = cls.LedgerAllocations
                    .OrderBy(a => a.Order).ThenBy(a => a.Id)
                    .Select(a => $"{_company.FindLedger(a.LedgerId)?.Name ?? "?"} "
                               + $"{a.PercentBasisPoints / 100m:0.##}%");
                parts.Add("Value allocated to " + string.Join(", ", names));
            }
            if (cls.AdditionalEntries.Count > 0)
            {
                var names = cls.AdditionalEntries
                    .OrderBy(e => e.Order).ThenBy(e => e.Id)
                    .Select(e => _company.FindLedger(e.LedgerId)?.Name ?? "?");
                parts.Add("Additional entries on " + string.Join(", ", names));
            }
            return parts.Count == 0 ? string.Empty : string.Join(" · ", parts) + ".";
        }
    }

    /// <summary>
    /// The legs <see cref="ActiveVoucherClass"/> posts on an item invoice whose items total
    /// <paramref name="itemValue"/> and whose other charges (GST, cess, TCS, the additional-cost pool) total
    /// <paramref name="otherCharges"/>. Returns <c>null</c> when no class is in force.
    ///
    /// <para><paramref name="otherCharges"/> is what makes the round-off round the INVOICE rather than a pre-tax
    /// subtotal — see <see cref="VoucherClassPosting.Compute"/>.</para>
    /// </summary>
    private VoucherClassPostingResult? ComputeVoucherClassLegs(
        Money itemValue, Money otherCharges, IReadOnlyList<VoucherInventoryLine> inventoryLines)
    {
        if (ActiveVoucherClass is not { } cls) return null;

        // The Based-on-Quantity basis is a rate per BASE unit, so the quantity handed to the engine has to be in
        // base units too. A line keyed in an alternate unit (a dozen) posts its typed quantity plus the unit it was
        // typed in; BaseQuantityOf is the one conversion this screen already trusts for stock.
        var totalBaseQuantity = inventoryLines.Sum(
            l => BaseQuantityOf(_company.FindStockItem(l.StockItemId), l.UnitId, l.BilledQuantity));

        return VoucherClassPosting.Compute(cls, itemValue, totalBaseQuantity, otherCharges);
    }

    /// <summary>
    /// 🔴 <b>WHAT THE CLASS ADDS TO WHAT THE PARTY OWES — FOR THE LIVE TOTALS BAND.</b>
    ///
    /// <para>The figure on screen and the figure that posts must be the same figure. A class's freight and its
    /// round-off move the party total, so a totals band that ignored them would show ₹1 182.36 on an invoice that
    /// posts ₹1 182.00 — and, worse, the Bill-wise panel foots against that band, so a bill-wise invoice under a
    /// class would be REFUSED at Accept for a mismatch the operator could not see the cause of.</para>
    ///
    /// <para>Computed off the SCREEN's own lines rather than the posted ones (which do not exist yet); a
    /// batch-split line posts as several rows whose quantities sum to the line's, so the base quantity is the same
    /// either way. Zero when no class is in force (ER-13).</para>
    /// </summary>
    private decimal ClassAdditionalTotalForDisplay(Money itemValue, Money otherCharges)
    {
        if (ActiveVoucherClass is not { } cls) return 0m;

        var totalBaseQuantity = InventoryLines
            .Where(l => l.IsComplete)
            .Sum(l => BaseQuantityOf(l.SelectedItem, l.UnitId, l.ParsedBilledQuantity));

        // An incomplete class (allocations not yet at 100%) throws rather than posting short — see
        // VoucherClassPosting.Compute. On the LIVE band that must not take the screen down: show the class as
        // adding nothing and let Accept deliver the refusal with its own message.
        try
        {
            var result = VoucherClassPosting.Compute(cls, itemValue, totalBaseQuantity, otherCharges);
            return result.AdditionalLegs.Sum(l => l.Amount.Amount);
        }
        catch (InvalidOperationException) { return 0m; }
        catch (ArgumentException) { return 0m; }
    }

    /// <summary>
    /// An inventory line's BILLED quantity in the item's BASE unit — the same conversion
    /// <c>BillsPending.BaseQuantity</c> makes, and for the same reason: a line keyed in dozens and one keyed in
    /// pieces are not addable, so a per-unit Value Basis summed over a mixed invoice without this would charge
    /// twelve times too little. Billed, not Actual, because the Value Basis is a charge and a short-billed line is
    /// charged on what was billed — the same rule the GST base already follows (RQ-23).
    /// <para>A line with no alternate unit passes through untouched, which is every line on a company that never
    /// defined one (ER-13).</para>
    /// </summary>
    private decimal BaseQuantityOf(StockItem? item, Guid? unitId, decimal billedQuantity)
    {
        if (unitId is not { } uid) return billedQuantity;

        var unit = _company.FindUnit(uid);
        if (unit is null) return billedQuantity;

        // A line whose unit IS the item's base unit needs no conversion — converting it would apply a compound
        // base unit's factor twice.
        if (item is not null && item.BaseUnitId == uid) return billedQuantity;

        return unit.QuantityInBaseMeasure(billedQuantity);
    }
}
