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
/// 🔴 T0-4 SLICE S2b — WHAT THE FLIP DOES TO A BOOK THAT ALREADY HAS VOUCHERS IN IT.
///
/// <para>Honouring <c>GstConfig.SourceOfGstRate</c> is the one money-moving change in the T0-4 design: on a book
/// where the stock item AND the resolved sales/purchase ledger BOTH declare a GST block, the rate a NEW line
/// resolves changes the day the slice lands. The question this file answers is the one the project has already
/// paid for twice (T0-14, T0-15 and the cess blocker): <b>does an ALREADY-POSTED voucher silently re-rate when a
/// report is re-run?</b></para>
///
/// <para><b>The answer is no, and it is proved rather than asserted.</b> <c>GstLineTax</c> stamps the rate and the
/// taxable value onto the tax <see cref="EntryLine"/> at post time and every report, payload and print reads them
/// back; nothing downstream re-resolves a rate for money. The first test posts a voucher under one source order,
/// flips the order underneath it, and shows every posted figure byte-identical while the LIVE resolver visibly
/// moves — which is what makes the immutability claim non-vacuous.</para>
///
/// <para>🔴 <b>The exception WAS real and is now anchored — the second test's final assertion is inverted from what
/// it originally pinned.</b> The DOCUMENT TITLE is not posted data: <c>GstReportSupport.IsBillOfSupply</c>
/// re-resolves every stock line LIVE, so on a voucher that posted NO tax the flip used to change an already-issued
/// document from BILL OF SUPPLY to TAX INVOICE. <b>Assumption A-QB</b>
/// (<c>GstReportSupport.AnchorIssuedDocumentCharacter</c>, one line, reversible) now reads the posted ledger as the
/// stamp: a positive-rate supply posts tax legs, so a voucher with none cannot have been issued under the taxable
/// reading. Money was always immune; the statutory title now is too, <b>except</b> where the taxable reading is
/// zero-rated — see <c>GstIssuedDocumentCharacterTests</c>, which owns the whole assumption and its residual.</para>
/// </summary>
public sealed class GstSourceOrderExistingBookTests
{
    private static readonly DateOnly FyStart = new(2024, 4, 1);
    private static readonly DateOnly SaleDate = new(2024, 4, 5);
    private static readonly DateOnly From = new(2024, 4, 1);
    private static readonly DateOnly To = new(2024, 4, 30);

    private const string GstinMaharashtra = "27AAPFU0939F1ZV";

    // ================================================================= posted money is immune

    /// <summary>
    /// 🔴 THE MASTER-DRIFT ANSWER (design test T14, narrowed to the drift THIS slice can cause). A migrated book
    /// (<see cref="GstDetailSource.StockItemFirst"/>) whose Widget declares 18% and whose Sales ledger declares 5%
    /// posts an intra-state B2B sale of 10,000.00. Item-first resolution gives 1800 bp, so the voucher carries
    /// CGST 900.00 + SGST 900.00 and the party is debited 11,800.00.
    ///
    /// <para>The book is then moved onto the shipped default order — exactly what a v51+ book already carries, and
    /// exactly what an operator does by changing the F11 option. <b>The live resolver moves to the LEDGER's 500 bp,
    /// which is asserted first so the flip cannot be silently absent</b>, and then every posted figure is asserted
    /// unchanged to the paisa: the GSTR-1 B2B row, the rate-wise summary (one row, still keyed 1800), GSTR-3B's
    /// outward heads, and the Output CGST/SGST ledger closings.</para>
    ///
    /// <para>DERIVED, not observed. 10,000.00 x 1800/10000 = 1,800.00, split intra-state into 900.00 + 900.00
    /// (§9(1) CGST + the mirror SGST levy), party debit 10,000.00 + 1,800.00 = 11,800.00. Had the report re-resolved
    /// live it would have produced a 500 bp bucket of 500.00 — a figure that appears nowhere below, which is what
    /// makes the assertions discriminating rather than merely green.</para>
    /// </summary>
    [Fact]
    public void Flipping_the_source_order_moves_no_posted_figure()
    {
        var (c, gst, item, salesLedger, debtor) = MigratedBookWithBothBlocks();

        // The book resolves item-first today: 18%, not the ledger's 5%.
        Assert.Equal(1800, gst.ResolveRate(item, salesLedger, SaleDate).RateBasisPoints);

        PostIntraB2bSale(c, gst, item, salesLedger, debtor, taxable: 10_000.00m, rateBasisPoints: 1800);

        var outCgst = gst.FindTaxLedger(GstTaxHead.Central, GstTaxDirection.Output)!;
        var outSgst = gst.FindTaxLedger(GstTaxHead.State, GstTaxDirection.Output)!;

        // ---- THE FLIP. This is what a v51+ book already carries and what the F11 option writes.
        c.Gst!.SourceOfGstRate = GstDetailSource.LedgerFirst;

        // The LIVE resolver really did move — without this the immutability assertions below would be vacuous.
        Assert.Equal(500, gst.ResolveRate(item, salesLedger, SaleDate).RateBasisPoints);

        // ---- ...and not one posted figure moved with it.
        var r1 = Gstr1.Build(c, From, To);

        var b2b = Assert.Single(r1.B2B);
        Assert.Equal(new Money(10_000.00m), b2b.TaxableValue);
        Assert.Equal(new Money(900.00m), b2b.Cgst);
        Assert.Equal(new Money(900.00m), b2b.Sgst);
        Assert.Equal(Money.Zero, b2b.Igst);

        var rateRow = Assert.Single(r1.RateSummary);
        Assert.Equal(1800, rateRow.RateBasisPoints);              // the POSTED rate, not the newly-resolvable 500
        Assert.Equal(new Money(10_000.00m), rateRow.TaxableValue);
        Assert.Equal(new Money(1_800.00m), rateRow.TotalTax);

        var r3b = Gstr3b.Build(c, From, To);
        Assert.Equal(new Money(900.00m), r3b.OutwardCgst);
        Assert.Equal(new Money(900.00m), r3b.OutwardSgst);
        Assert.Equal(Money.Zero, r3b.OutwardIgst);
        Assert.Equal(new Money(1_800.00m), r3b.TotalOutwardTax);

        Assert.Equal(900.00m, -LedgerBalances.SignedClosing(c, outCgst, To));
        Assert.Equal(900.00m, -LedgerBalances.SignedClosing(c, outSgst, To));
    }

    // ================================================================= the title is NOT posted data

    /// <summary>
    /// 🔴 THE ONE PLACE THE FLIP REACHED ALREADY-ISSUED PAPER — <b>NOW ANCHORED UNDER ASSUMPTION A-QB, AND THE
    /// ASSERTION ON THE LAST LINE IS INVERTED FROM WHAT THIS TEST USED TO PIN.</b>
    /// <c>GstReportSupport.IsBillOfSupply</c>'s exempt limb calls <c>IsWhollyExemptItemSupply</c>, which resolves
    /// every stock line LIVE against the current masters. The fixture is the minimal shape that exposed it: the
    /// Widget is declared <b>Exempt</b> and the Sales ledger <b>Taxable at 18%</b>. Under the migrated order the item
    /// answers first, the supply is wholly exempt, no tax is posted, and §31(3)(c) makes the document a BILL OF
    /// SUPPLY. <b>Before A-QB, flipping to the shipped default re-printed the same paper as a TAX INVOICE</b> — with
    /// no tax on it, because none was ever posted.
    ///
    /// <para>🔴 <b>WHAT CHANGED, AND WHAT DID NOT.</b> The name and the final assertion moved because the behaviour
    /// moved; the fixture is untouched, so the two versions are directly comparable. A-QB
    /// (<c>GstReportSupport.AnchorIssuedDocumentCharacter</c>) reads the posted ledger as the stamp: an 18% supply
    /// posts tax legs, this voucher has none, so it cannot have been issued under the taxable reading. The claim
    /// this test's doc used to carry — <i>"anchoring the title to posted data is unavailable at this schema"</i> —
    /// is <b>narrowed, not refuted</b>: it still holds where the taxable reading is <b>zero-rated</b> (0 bp posts no
    /// legs either), and that residual is pinned in
    /// <c>GstIssuedDocumentCharacterTests.The_zero_rate_versus_exempt_residual_still_moves_with_the_option_and_that_needs_a_column</c>
    /// and escalated rather than fixed. <b>A-QB is an ASSUMPTION, not a user ruling</b>; the R12 question stays
    /// open, and flipping the one constant restores exactly what this test used to assert.</para>
    /// </summary>
    [Fact]
    public void An_issued_untaxed_document_keeps_its_title_when_the_source_order_flips()
    {
        var c = CompanyFactory.CreateSeeded("Title Drift Co", FyStart);
        var gst = new GstService(c);
        EnableGst(gst);
        c.Gst!.SourceOfGstRate = GstDetailSource.StockItemFirst;

        var inv = new InventoryService(c);
        var stockGroup = inv.CreateStockGroup("Goods");
        var nos = inv.CreateSimpleUnit("Nos", "Numbers", unitQuantityCode: "NOS");
        var item = inv.CreateStockItem("Printed Book", stockGroup.Id, nos.Id);
        item.Gst = new StockItemGstDetails { HsnSac = "490199", Taxability = GstTaxability.Exempt };
        inv.AddOpeningBalance(item.Id, c.MainLocation!.Id, 100m, new Money(150.00m));

        var salesLedger = Add(c, "Sales", "Sales Accounts", openingIsDebit: false);
        salesLedger.SalesPurchaseGst =
            new StockItemGstDetails { Taxability = GstTaxability.Taxable, RateBasisPoints = 1800 };

        var debtor = Add(c, "Local Debtor", "Sundry Debtors", openingIsDebit: true);
        debtor.PartyGst = new PartyGstDetails
        {
            RegistrationType = GstRegistrationType.Regular, Gstin = GstinMaharashtra, StateCode = "27",
        };

        // An exempt sale under the migrated order: no tax computed, no tax legs posted.
        Assert.False(gst.ResolveRate(item, salesLedger, SaleDate).IsTaxable);

        var salesType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id;
        var voucher = new Voucher(Guid.NewGuid(), salesType, SaleDate,
            new List<EntryLine>
            {
                new(debtor.Id, new Money(1_000.00m), DrCr.Debit),
                new(salesLedger.Id, new Money(1_000.00m), DrCr.Credit),
            },
            partyId: debtor.Id,
            inventoryLines: new[] { new VoucherInventoryLine(item.Id, c.MainLocation!.Id, 5m, new Money(200.00m)) });
        new LedgerService(c).Post(voucher);

        Assert.True(GstReportSupport.IsBillOfSupply(c, voucher));   // §31(3)(c): a wholly exempt supply

        c.Gst!.SourceOfGstRate = GstDetailSource.LedgerFirst;

        // A-QB: the SAME paper, NOT re-titled by a master option. Was Assert.False before the anchor shipped.
        Assert.True(GstReportSupport.IsBillOfSupply(c, voucher));
    }

    // ================================================================= fixture

    private static (Company Company, GstService Gst, StockItem Item, Domain.Ledger SalesLedger, Domain.Ledger Debtor)
        MigratedBookWithBothBlocks()
    {
        var c = CompanyFactory.CreateSeeded("Migrated Book Co", FyStart);
        var gst = new GstService(c);
        EnableGst(gst);

        // Every pre-v51 book is back-filled to StockItemFirst by Schema.MigrateV50ToV51.
        c.Gst!.SourceOfGstRate = GstDetailSource.StockItemFirst;

        var inv = new InventoryService(c);
        var stockGroup = inv.CreateStockGroup("Goods");
        var nos = inv.CreateSimpleUnit("Nos", "Numbers", unitQuantityCode: "NOS");
        var item = inv.CreateStockItem("Widget", stockGroup.Id, nos.Id);
        item.Gst = new StockItemGstDetails
        {
            HsnSac = "847130", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800,
        };
        inv.AddOpeningBalance(item.Id, c.MainLocation!.Id, 100m, new Money(50.00m));

        // 🔴 The at-risk shape the design named: the item AND the resolved value ledger BOTH declare a block, with
        // DIFFERENT rates. This is the only shape on which slice S2b can move a figure.
        var salesLedger = Add(c, "Sales", "Sales Accounts", openingIsDebit: false);
        salesLedger.SalesPurchaseGst =
            new StockItemGstDetails { Taxability = GstTaxability.Taxable, RateBasisPoints = 500 };

        var debtor = Add(c, "Local Debtor", "Sundry Debtors", openingIsDebit: true);
        debtor.PartyGst = new PartyGstDetails
        {
            RegistrationType = GstRegistrationType.Regular, Gstin = GstinMaharashtra, StateCode = "27",
        };

        return (c, gst, item, salesLedger, debtor);
    }

    private static void PostIntraB2bSale(
        Company c, GstService gst, StockItem item, Domain.Ledger salesLedger, Domain.Ledger debtor,
        decimal taxable, int rateBasisPoints)
    {
        var tax = gst.ComputeInvoiceTax(
            new[] { new GstService.TaxableLine(new Money(taxable), rateBasisPoints) },
            interState: false, GstTaxDirection.Output);

        var gross = new Money(taxable + tax.TaxLines.Sum(l => l.Amount.Amount));
        var lines = new List<EntryLine>
        {
            new(debtor.Id, gross, DrCr.Debit),
            new(salesLedger.Id, new Money(taxable), DrCr.Credit),
        };
        lines.AddRange(tax.TaxLines);

        var salesType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id;
        new LedgerService(c).Post(new Voucher(Guid.NewGuid(), salesType, SaleDate, lines, partyId: debtor.Id,
            inventoryLines: new[]
            {
                new VoucherInventoryLine(item.Id, c.MainLocation!.Id, 100m, new Money(taxable / 100m)),
            }));

        // The party leg is the arithmetic, stated once: 10,000.00 + 1,800.00 = 11,800.00 on the 18% fixture.
        Assert.Equal(new Money(taxable + taxable * rateBasisPoints / 10_000m), gross);
    }

    private static void EnableGst(GstService gst) => gst.EnableGst(new GstConfig
    {
        HomeStateCode = "27",
        Gstin = GstinMaharashtra,
        RegistrationType = GstRegistrationType.Regular,
        ApplicableFrom = FyStart,
        Periodicity = GstReturnPeriodicity.Monthly,
    });

    private static Domain.Ledger Add(Company c, string name, string groupName, bool openingIsDebit)
    {
        var l = new Domain.Ledger(Guid.NewGuid(), name, c.FindGroupByName(groupName)!.Id, Money.Zero, openingIsDebit);
        c.AddLedger(l);
        return l;
    }
}
