using System;
using System.Collections.Generic;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// 🔴 <b>Q-B — THE DOCUMENT-TITLE FLIP, AND THE ANCHOR THAT STOPS IT. THIS FILE PINS AN ASSUMPTION, NOT A RULING.</b>
///
/// <para><b>What was measured</b> (recorded in <c>docs/full-clone-census.md</c> §1.3 item 15, open R12 question 2, and
/// pinned before this file existed by
/// <c>GstSourceOrderExistingBookTests.Flipping_the_source_order_DOES_move_the_document_title_on_an_untaxed_voucher</c>):
/// no taxability is stamped on a posted line, so <see cref="GstReportSupport.IsBillOfSupply"/> re-resolves every stock
/// line <b>live</b>. With the item Exempt and the sales ledger Taxable at 18%, the SAME already-issued paper was a
/// <b>BILL OF SUPPLY</b> under <see cref="GstDetailSource.StockItemFirst"/> and a <b>TAX INVOICE</b> under
/// <see cref="GstDetailSource.LedgerFirst"/> — re-titled by a master option, months later, carrying no tax because
/// none was ever posted.</para>
///
/// <para>🔴 <b>THE ASSUMPTION BUILT HERE (A-QB) — AN ASSUMPTION, NOT A USER RULING AND NOT A CORPUS FACT.</b>
/// <i>An issued document must not change its statutory character retroactively.</i> It is one-line reversible at
/// <c>GstReportSupport.AnchorIssuedDocumentCharacter</c>; setting that constant to <c>false</c> restores, exactly, the
/// behaviour this file's first test used to pin. The R12 question stays open.</para>
///
/// <para>🔴 <b>NO SCHEMA COLUMN WAS TAKEN, AND THE CENSUS'S REASON FOR THINKING ONE WAS NEEDED IS NARROWED RATHER THAN
/// REFUTED.</b> The census records: <i>"Anchoring the title to posted data is unavailable at this schema — a
/// zero-rated LUT/export supply is <c>IsTaxable = true</c> at 0 bp and also posts no tax legs, so 'no tax legs' cannot
/// tell the two apart."</i> That is true, and it remains true — see
/// <see cref="The_zero_rate_versus_exempt_residual_still_moves_with_the_option_and_that_needs_a_column"/>, which pins
/// the residual instead of hiding it. What the census did not separate is that the ambiguity only bites where the
/// taxable reading carries <b>no rate to post</b>. Where the taxable reading carries a <b>POSITIVE</b> rate, the
/// posted ledger is decisive by arithmetic: an 18% supply posts tax legs, and this voucher has none, so it cannot have
/// been issued under the taxable reading. That is a derivation from data already stored, so the stamp the assumption
/// asks for already exists for the measured defect and only the zero-rate sub-case needs a column.</para>
///
/// <para><b>Money is untouched either way</b> — <see cref="GstLineTax"/> stamps the rate and the taxable value at post
/// time and every report reads them back (pinned by <c>GstSourceOrderExistingBookTests</c>). This file is about the
/// statutory TITLE on the paper and nothing else.</para>
/// </summary>
public sealed class GstIssuedDocumentCharacterTests
{
    private static readonly DateOnly FyStart = new(2024, 4, 1);
    private static readonly DateOnly SaleDate = new(2024, 4, 5);
    private const string GstinMaharashtra = "27AAPFU0939F1ZV";

    // ================================================================= the fix

    /// <summary>
    /// 🔴 <b>THE MEASURED DEFECT, INVERTED.</b> Item Exempt, sales ledger Taxable at 1800 bp. The sale is posted on a
    /// migrated (<see cref="GstDetailSource.StockItemFirst"/>) book, so it resolves Exempt and posts <b>no tax legs at
    /// all</b> — the party is debited the bare 1,000.00 and the Sales ledger credited 1,000.00, and no Output
    /// CGST/SGST/IGST/Cess line exists.
    ///
    /// <para>The book is then moved onto <see cref="GstDetailSource.LedgerFirst"/>. <b>Before A-QB the title flipped
    /// to TAX INVOICE</b>; the assertion below is that it does not, because the posted ledger contradicts the taxable
    /// reading: 1800 bp of 1,000.00 is 180.00 of tax and the voucher carries 0.00. DERIVED, not observed — an 18%
    /// supply cannot have been issued with no tax stated on it.</para>
    /// </summary>
    [Fact]
    public void An_issued_bill_of_supply_keeps_its_character_when_the_source_order_flips()
    {
        var (c, voucher) = UntaxedItemSale(
            itemTaxability: GstTaxability.Exempt, itemRateBp: null,
            ledgerTaxability: GstTaxability.Taxable, ledgerRateBp: 1800,
            postedUnder: GstDetailSource.StockItemFirst);

        Assert.True(GstReportSupport.IsBillOfSupply(c, voucher));   // §31(3)(c) as issued

        c.Gst!.SourceOfGstRate = GstDetailSource.LedgerFirst;

        Assert.True(GstReportSupport.IsBillOfSupply(c, voucher));   // A-QB: the SAME paper, still a bill of supply
    }

    /// <summary>
    /// The mirror direction, which was already anchored and must stay anchored. The book posts under
    /// <see cref="GstDetailSource.LedgerFirst"/> with the ledger's 1800 bp, so the voucher carries CGST 90.00 + SGST
    /// 90.00 on a taxable value of 1,000.00 (1,000.00 x 1800/10000 = 180.00, split 90.00 / 90.00) and the party is
    /// debited 1,180.00. Flipping to <see cref="GstDetailSource.StockItemFirst"/> makes the live resolver say Exempt,
    /// but <c>CarriesForwardTax</c> is <see cref="GstReportSupport.IsBillOfSupply"/>'s first gate, so a document
    /// stating collected tax can never be re-titled a bill of supply. Guard, not a new behaviour.
    /// </summary>
    [Fact]
    public void An_issued_tax_invoice_keeps_its_character_when_the_source_order_flips()
    {
        var c = SeededGstCompany();
        c.Gst!.SourceOfGstRate = GstDetailSource.LedgerFirst;
        var (item, salesLedger, debtor) = Masters(c,
            itemTaxability: GstTaxability.Exempt, itemRateBp: null,
            ledgerTaxability: GstTaxability.Taxable, ledgerRateBp: 1800);

        var gst = new GstService(c);
        var res = gst.ResolveRate(item, salesLedger, SaleDate);
        Assert.True(res.IsTaxable);
        Assert.Equal(1800, res.RateBasisPoints);

        var tax = gst.ComputeInvoiceTax(
            new[] { new GstService.TaxableLine(new Money(1_000.00m), 1800) },
            interState: false, GstTaxDirection.Output);
        Assert.Equal(new Money(180.00m), new Money(tax.TaxLines.Sum(l => l.Amount.Amount)));

        var lines = new List<EntryLine>
        {
            new(debtor.Id, new Money(1_180.00m), DrCr.Debit),
            new(salesLedger.Id, new Money(1_000.00m), DrCr.Credit),
        };
        lines.AddRange(tax.TaxLines);
        var voucher = PostSale(c, item, debtor, lines);

        Assert.False(GstReportSupport.IsBillOfSupply(c, voucher));

        c.Gst!.SourceOfGstRate = GstDetailSource.StockItemFirst;

        Assert.False(GstReportSupport.IsBillOfSupply(c, voucher));
    }

    // ================================================================= the anchor does not over-reach

    /// <summary>
    /// 🔴 <b>A-QB MUST NOT TURN EVERY UNTAXED VOUCHER INTO A BILL OF SUPPLY.</b> Both masters declare Taxable at
    /// 1800 bp, so the two published orders AGREE: the taxability is not order-dependent and there is nothing for the
    /// posted ledger to arbitrate. The voucher is nevertheless posted with no tax legs (the shape a hand-keyed
    /// As-Voucher sale produces). A wider anchor — "any untaxed voucher was issued exempt" — would title this
    /// BILL OF SUPPLY and print a false statutory statement about an 18% supply. It must stay a tax invoice under
    /// BOTH orders.
    /// </summary>
    [Theory]
    [InlineData(GstDetailSource.StockItemFirst)]
    [InlineData(GstDetailSource.LedgerFirst)]
    public void An_unambiguously_taxable_untaxed_voucher_is_never_re_titled(GstDetailSource source)
    {
        var (c, voucher) = UntaxedItemSale(
            itemTaxability: GstTaxability.Taxable, itemRateBp: 1800,
            ledgerTaxability: GstTaxability.Taxable, ledgerRateBp: 1800,
            postedUnder: source);

        Assert.False(GstReportSupport.IsBillOfSupply(c, voucher));
    }

    /// <summary>
    /// The other half of "does not over-reach": where both masters agree the supply is EXEMPT, the answer is a bill of
    /// supply under both orders and A-QB never enters the picture at all.
    /// </summary>
    [Theory]
    [InlineData(GstDetailSource.StockItemFirst)]
    [InlineData(GstDetailSource.LedgerFirst)]
    public void An_unambiguously_exempt_voucher_is_a_bill_of_supply_under_either_order(GstDetailSource source)
    {
        var (c, voucher) = UntaxedItemSale(
            itemTaxability: GstTaxability.Exempt, itemRateBp: null,
            ledgerTaxability: GstTaxability.Exempt, ledgerRateBp: null,
            postedUnder: source);

        Assert.True(GstReportSupport.IsBillOfSupply(c, voucher));
    }

    // ================================================================= the residual, pinned rather than hidden

    /// <summary>
    /// 🔴 <b>THE RESIDUAL A-QB CANNOT REACH, AND IT IS THE ONE THE CENSUS NAMED.</b> Item Exempt, sales ledger Taxable
    /// at <b>0 bp</b> (the zero-rated LUT/export shape). The two orders disagree on taxability — Exempt versus
    /// Taxable — but the taxable reading posts <b>no tax legs either</b>, so the posted ledger holds no evidence that
    /// can separate the two readings. A-QB deliberately does NOT fire here: an anchor that guessed would be a
    /// coin-flip dressed as a derivation.
    ///
    /// <para>So this voucher's title STILL moves with the master option, and that is what a posted taxability column
    /// would fix. <b>The column is NOT taken here</b> (three sibling tracks share this v52 base); the escalation is
    /// stated in the report and in <c>docs/full-clone-census.md</c>. This test exists so the residual is a recorded
    /// figure rather than a surprise on a reprint.</para>
    /// </summary>
    [Fact]
    public void The_zero_rate_versus_exempt_residual_still_moves_with_the_option_and_that_needs_a_column()
    {
        var (c, voucher) = UntaxedItemSale(
            itemTaxability: GstTaxability.Exempt, itemRateBp: null,
            ledgerTaxability: GstTaxability.Taxable, ledgerRateBp: 0,
            postedUnder: GstDetailSource.StockItemFirst);

        Assert.True(GstReportSupport.IsBillOfSupply(c, voucher));

        c.Gst!.SourceOfGstRate = GstDetailSource.LedgerFirst;

        // A zero-rated supply is a TAXABLE supply (§2(47) does not reach it), so the taxable reading is a tax
        // invoice — and nothing posted can say which reading issued the paper.
        Assert.False(GstReportSupport.IsBillOfSupply(c, voucher));
    }

    // ================================================================= fixture

    private static (Company Company, Voucher Voucher) UntaxedItemSale(
        GstTaxability itemTaxability, int? itemRateBp,
        GstTaxability ledgerTaxability, int? ledgerRateBp,
        GstDetailSource postedUnder)
    {
        var c = SeededGstCompany();
        c.Gst!.SourceOfGstRate = postedUnder;
        var (item, salesLedger, debtor) = Masters(c, itemTaxability, itemRateBp, ledgerTaxability, ledgerRateBp);

        var voucher = PostSale(c, item, debtor, new List<EntryLine>
        {
            new(debtor.Id, new Money(1_000.00m), DrCr.Debit),
            new(salesLedger.Id, new Money(1_000.00m), DrCr.Credit),
        });

        return (c, voucher);
    }

    private static Voucher PostSale(Company c, StockItem item, Domain.Ledger debtor, List<EntryLine> lines)
    {
        var salesType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id;
        var voucher = new Voucher(Guid.NewGuid(), salesType, SaleDate, lines, partyId: debtor.Id,
            inventoryLines: new[]
            {
                new VoucherInventoryLine(item.Id, c.MainLocation!.Id, 5m, new Money(200.00m)),
            });
        new LedgerService(c).Post(voucher);
        return voucher;
    }

    private static (StockItem Item, Domain.Ledger SalesLedger, Domain.Ledger Debtor) Masters(
        Company c, GstTaxability itemTaxability, int? itemRateBp,
        GstTaxability ledgerTaxability, int? ledgerRateBp)
    {
        var inv = new InventoryService(c);
        var stockGroup = inv.CreateStockGroup("Goods");
        var nos = inv.CreateSimpleUnit("Nos", "Numbers", unitQuantityCode: "NOS");
        var item = inv.CreateStockItem("Printed Book", stockGroup.Id, nos.Id);
        item.Gst = new StockItemGstDetails
        {
            HsnSac = "490199", Taxability = itemTaxability, RateBasisPoints = itemRateBp,
        };
        inv.AddOpeningBalance(item.Id, c.MainLocation!.Id, 100m, new Money(150.00m));

        var salesLedger = Add(c, "Sales", "Sales Accounts", openingIsDebit: false);
        salesLedger.SalesPurchaseGst = new StockItemGstDetails
        {
            Taxability = ledgerTaxability, RateBasisPoints = ledgerRateBp,
        };

        var debtor = Add(c, "Local Debtor", "Sundry Debtors", openingIsDebit: true);
        debtor.PartyGst = new PartyGstDetails
        {
            RegistrationType = GstRegistrationType.Regular, Gstin = GstinMaharashtra, StateCode = "27",
        };

        return (item, salesLedger, debtor);
    }

    private static Company SeededGstCompany()
    {
        var c = CompanyFactory.CreateSeeded("Issued Character Co", FyStart);
        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = "27",
            Gstin = GstinMaharashtra,
            RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart,
            Periodicity = GstReturnPeriodicity.Monthly,
        });
        return c;
    }

    private static Domain.Ledger Add(Company c, string name, string groupName, bool openingIsDebit)
    {
        var l = new Domain.Ledger(Guid.NewGuid(), name, c.FindGroupByName(groupName)!.Id, Money.Zero, openingIsDebit);
        c.AddLedger(l);
        return l;
    }
}
