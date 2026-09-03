using System;
using System.Collections.Generic;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Apex.Desktop.ViewModels;
using Xunit;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.Tests;

/// <summary>
/// <b>Phase 10.11 S5c — the withholding carve-out must not DESTROY what the operator keyed on the deductee's row.</b>
///
/// <para>🔴 <b>The defect, and how far it reached.</b> <c>TdsService.BuildCarveOut</c> built the derived party leg
/// from <c>(ledgerId, amount, side)</c> alone, and both accept paths then SPLICED that leg over the whole keyed
/// row — so every bill-wise reference, cost-centre allocation, bank instrument and forex detail the operator had
/// keyed vanished at posting, with no message. Measured across all six shapes
/// (Journal / Payment / Purchase × withheld / below-threshold) the keyed New Ref and the keyed cost allocation
/// both posted as ZERO allocations, and the consequence reached a report:
/// <c>Outstandings.OpenBillsFor</c> returned NO rows for a creditor the company owed ₹1,08,000.30.</para>
///
/// <para>🔴 <b>And it is not only a posting defect.</b> With no allocation on the posted line,
/// <c>VoucherLineViewModel.RehydrateBillAllocations</c> refuses the whole voucher — permanently, and with a
/// sentence blaming a master change that never happened ("<i>now</i> maintains balances bill-by-bill") — so S5c's
/// headline lift of the TDS rows did not reach the ORDINARY configuration of a professional-fees vendor. The
/// trigger was not even "a withholding": the below-threshold arm, where nothing is carved at all, lost the
/// children just the same.</para>
///
/// <para>🔴 <b>Where the carve genuinely cannot be carried, it is REFUSED BY NAME, never dropped.</b> The party is
/// credited the NET, so a split keyed against the gross has to lose the withheld amount out of ONE reference;
/// with more than one there is no way to decide which, and in a foreign currency there is no exact amount to
/// state at all (the withholding is rounded to the nearest rupee in the base currency).</para>
///
/// <para><b>Odd paise everywhere</b> (house rule).</para>
/// </summary>
public sealed class VoucherAlterCarveChildrenTests
{
    private const string Section = "194J(b)";

    private static DomainLedger TdsPayable(Company c) =>
        c.Ledgers.First(l => l.TdsTcsClassification == TdsTcsLedgerKind.Tds);

    private static EntryLine PartyLine(Voucher v, DomainLedger party) =>
        v.Lines.Single(l => l.LedgerId == party.Id);

    /// <summary>
    /// Posts <c>Dr Professional Fees / Cr deductee</c> at <paramref name="gross"/> with ONE New Ref and ONE cost
    /// allocation keyed on the deductee's row, through the real screen.
    /// </summary>
    private static Voucher PostWithChildren(
        AlterationBook book, VoucherBaseType baseType, DomainLedger expense, DomainLedger party,
        string gross, CostCategory category, CostCentre centre, string reference = "INV-77") =>
        book.Post(baseType, book.On(),
            new[] { (expense, DrCr.Debit, gross), (party, DrCr.Credit, gross) },
            configure: e =>
            {
                var row = e.Lines[1];
                var bill = row.AddBillAllocation(BillRefType.NewRef);
                bill.Name = reference;
                bill.AmountText = gross;
                var cost = row.AddCostAllocation();
                cost.SelectedCategory = category;
                cost.SelectedCentre = centre;
                cost.AmountText = gross;
            });

    private static (AlterationBook Book, DomainLedger Expense, DomainLedger Party, CostCategory Category,
        CostCentre Centre) BillWiseDeductee(string tag)
    {
        var book = AlterationBook.New(tag);
        var (expense, party) = book.EnableTds(Section);
        party.MaintainBillByBill = true;
        party.CostCentresApplicable = true;
        var (category, centre) = book.CostAxis("Branches", "Mumbai");
        return (book, expense, party, category, centre);
    }

    // ================================================================ the WITHHELD arm

    /// <summary>
    /// 🔴 The withheld arm on the ordinary shape of a professional-fees vendor. ₹1,20,000.30 gross under §194J(b)
    /// at 10% withholds ₹12,000, so the party is credited ₹1,08,000.30 — and the keyed bill reference and cost
    /// allocation come with it, carved to the SAME net so the line still foots. The voucher then re-opens, which it
    /// could not do at all while the allocations were being dropped.
    /// </summary>
    [Theory]
    [InlineData(VoucherBaseType.Journal)]
    [InlineData(VoucherBaseType.Payment)]
    [InlineData(VoucherBaseType.Purchase)]
    public void A_withheld_carve_keeps_the_keyed_bill_and_cost_allocation_at_the_net(VoucherBaseType baseType)
    {
        var (book, expense, party, category, centre) = BillWiseDeductee("children-withheld-" + baseType);
        using var _book = book;

        var posted = PostWithChildren(book, baseType, expense, party, "120000.30", category, centre);
        Assert.Equal(3, posted.Lines.Count);
        Assert.Equal(new Money(12000.00m), posted.Lines.Single(l => l.LedgerId == TdsPayable(book.Company).Id).Amount);

        var line = PartyLine(posted, party);
        Assert.Equal(new Money(108000.30m), line.Amount);

        var bill = Assert.Single(line.BillAllocations);
        Assert.Equal("INV-77", bill.Name);
        Assert.Equal(BillRefType.NewRef, bill.RefType);
        Assert.Equal(new Money(108000.30m), bill.Amount);

        var cost = Assert.Single(line.CostAllocations);
        Assert.Equal(centre.Id, cost.CentreId);
        Assert.Equal(category.Id, cost.CategoryId);
        Assert.Equal(new Money(108000.30m), cost.Amount);

        // It re-opens, and a no-edit accept is a byte-identical round trip.
        var before = book.Export();
        var beforeDisk = book.ExportReloaded();
        var open = book.ForAlter(posted.Id);
        Assert.False(open.IsRefused, "Expected the screen to open; refused with: " + open.Refusal);
        Assert.True(open.Entry!.AcceptAlteration(), open.Entry.Message);
        Assert.Equal(before, book.Export());
        Assert.Equal(beforeDisk, book.ExportReloaded());
    }

    /// <summary>
    /// 🔴 The report the loss reached. A creditor owed ₹1,08,000.30 net of TDS must appear in Outstandings against
    /// the reference the operator keyed — it returned NO rows at all.
    /// </summary>
    [Fact]
    public void The_deductees_open_bill_reaches_Outstandings_at_the_net()
    {
        var (book, expense, party, category, centre) = BillWiseDeductee("children-outstandings");
        using var _book = book;

        PostWithChildren(book, VoucherBaseType.Journal, expense, party, "120000.30", category, centre);

        var rows = Outstandings.OpenBillsFor(book.Company, party, book.On(400));
        var row = Assert.Single(rows);
        Assert.Equal("INV-77", row.Reference);
        Assert.Equal(new Money(108000.30m), row.Pending);
    }

    // ================================================================ the BELOW-THRESHOLD arm

    /// <summary>
    /// 🔴 The arm nobody was looking at: ₹30,000.30 is BELOW the ₹50,000 cumulative threshold, so nothing is carved
    /// at all — and the children were destroyed just the same, because the below-threshold leg is rebuilt too. The
    /// amount is unchanged here, so the split rides across verbatim.
    /// </summary>
    [Theory]
    [InlineData(VoucherBaseType.Journal)]
    [InlineData(VoucherBaseType.Payment)]
    [InlineData(VoucherBaseType.Purchase)]
    public void A_below_threshold_assessment_keeps_the_keyed_bill_and_cost_allocation(VoucherBaseType baseType)
    {
        var (book, expense, party, category, centre) = BillWiseDeductee("children-below-" + baseType);
        using var _book = book;

        var posted = PostWithChildren(book, baseType, expense, party, "30000.30", category, centre, "INV-11");
        Assert.Equal(2, posted.Lines.Count);                       // no payable leg — nothing was withheld

        var line = PartyLine(posted, party);
        Assert.Equal(new Money(30000.30m), line.Amount);
        Assert.Equal(Money.Zero, line.Tds!.TdsAmount);             // the assessment still rides the party's line

        var bill = Assert.Single(line.BillAllocations);
        Assert.Equal("INV-11", bill.Name);
        Assert.Equal(new Money(30000.30m), bill.Amount);
        Assert.Equal(new Money(30000.30m), Assert.Single(line.CostAllocations).Amount);

        var open = book.ForAlter(posted.Id);
        Assert.False(open.IsRefused, "Expected the screen to open; refused with: " + open.Refusal);
    }

    /// <summary>
    /// The control that identifies the trigger: the SAME bill-wise party, credited beside a debit leg that is NOT
    /// Is-TDS-Applicable, always kept its allocation. The only difference was the expense ledger's flag.
    /// </summary>
    [Fact]
    public void A_deductee_credited_beside_a_non_TDS_expense_keeps_its_bill_as_it_always_did()
    {
        var (book, _, party, category, centre) = BillWiseDeductee("children-control");
        using var _book = book;
        var ordinary = book.Ledger("Office Rent", "Indirect Expenses");   // no TdsApplicable flag

        var posted = PostWithChildren(book, VoucherBaseType.Journal, ordinary, party, "120000.30", category, centre);
        Assert.Equal(2, posted.Lines.Count);
        Assert.Equal(new Money(120000.30m), Assert.Single(PartyLine(posted, party).BillAllocations).Amount);
    }

    // ================================================================ where it cannot be carried: REFUSED BY NAME

    /// <summary>
    /// 🔴 Two bill references on a withheld carve: the party's credit falls to the net and there is no way to
    /// decide which reference the deduction comes out of. Refused by name at Accept — never silently flattened,
    /// and never posted with a split that does not foot.
    /// </summary>
    [Fact]
    public void Two_bill_references_on_a_withheld_carve_are_refused_by_name()
    {
        var (book, expense, party, _, _) = BillWiseDeductee("children-two-bills");
        using var _book = book;

        var entry = book.Entry(VoucherBaseType.Journal);
        entry.AddLine();
        entry.Lines[0].SelectedLedger = expense;
        entry.Lines[0].Side = DrCr.Debit;
        entry.Lines[0].AmountText = "120000.30";
        entry.Lines[1].SelectedLedger = party;
        entry.Lines[1].Side = DrCr.Credit;
        entry.Lines[1].AmountText = "120000.30";
        var first = entry.Lines[1].AddBillAllocation(BillRefType.NewRef);
        first.Name = "INV-77";
        first.AmountText = "70000.15";
        var second = entry.Lines[1].AddBillAllocation(BillRefType.NewRef);
        second.Name = "INV-78";
        second.AmountText = "50000.15";
        entry.Recalculate();

        Assert.False(entry.Accept(), "Expected the post to be refused; it was accepted.");
        Assert.Contains("bill references", entry.Message!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ONE bill reference", entry.Message!, StringComparison.Ordinal);
        Assert.Empty(book.Company.Vouchers);
    }

    /// <summary>
    /// 🔴 A deductee credited in a FOREIGN CURRENCY while TDS is withheld. <c>ForexInfo</c>'s contract is
    /// <c>ForexAmount × Rate == the line's base amount</c> and the withholding is rounded to the nearest RUPEE in
    /// the base currency, so the net leg has no exact foreign amount to state. It used to be dropped silently,
    /// which left the posted line disagreeing with what the operator keyed and no message anywhere.
    /// </summary>
    [Fact]
    public void A_forex_deductee_leg_on_a_withheld_carve_is_refused_by_name()
    {
        using var book = AlterationBook.New("children-forex");
        var (expense, _) = book.EnableTds(Section);
        var usd = book.ForeignCurrency();
        var party = book.Ledger("Overseas Consultant", "Sundry Creditors", currencyId: usd.Id);
        party.DeducteeType = DeducteeType.Firm;
        party.PartyPan = "AAPFU0939F";

        var entry = book.Entry(VoucherBaseType.Journal);
        entry.AddLine();
        entry.Lines[0].SelectedLedger = expense;
        entry.Lines[0].Side = DrCr.Debit;
        entry.Lines[0].AmountText = "120000.30";
        entry.Lines[1].SelectedLedger = party;
        entry.Lines[1].Side = DrCr.Credit;
        entry.Lines[1].ForexAmountText = "1500.00";
        entry.Lines[1].ForexRateText = "80.0002";
        entry.Recalculate();
        Assert.Equal(new Money(120000.30m), new Money(entry.Lines[1].ParsedAmount));

        Assert.False(entry.Accept(), "Expected the post to be refused; it was accepted.");
        Assert.Contains("foreign currency", entry.Message!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("nearest RUPEE", entry.Message!, StringComparison.Ordinal);
        Assert.Empty(book.Company.Vouchers);
    }
}
