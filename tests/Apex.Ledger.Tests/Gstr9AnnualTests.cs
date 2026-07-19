using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;

using Rev = Apex.Ledger.Services.GstReversalService;
using SetOff = Apex.Ledger.Services.GstSetOffService;

namespace Apex.Ledger.Tests;

/// <summary>
/// Phase 9 slice 8a — the <b>annual returns</b> GSTR-9 / GSTR-9A / GSTR-9C (RQ-17; DP-18). Pure, read-only projections;
/// nothing posts, nothing persists (no schema change). The single risk the whole slice guards is a <i>silent
/// misstatement</i> — an annual return that does not foot to the monthly returns, or a 9C that does not reconcile to the
/// books — so every test is adversarial on <b>reconciliation</b>:
/// <list type="bullet">
///   <item>GSTR-9 foots EXACTLY, paisa-for-paisa, to Σ(the year's GSTR-3B) tax/ITC/reversal AND Σ(the year's GSTR-1)
///     outward + HSN — proving the <b>Σ-of-already-rounded-periods</b> construction (no whole-FY re-round).</item>
///   <item>Table 7 reversals bucket by rule (Rule 37/37A → 7A, Rule 42 → 7C, Rule 43 → 7D, §17(5) → 7E); Table 8A pulls
///     the imported GSTR-2B figure; Table 9 splits credit-utilised vs cash-discharged.</item>
///   <item>GSTR-9A foots to Σ the four CMP-08 (the composition Σ-of-quarters template).</item>
///   <item>GSTR-9C reconciles GSTR-9 turnover/tax/ITC to the books (P&amp;L income + Input-ledger closings) and the
///     <b>unreconciled-difference line is computed AND shown, never forced to zero</b>.</item>
///   <item>ER-13: a Composition / GST-off company yields not-applicable/empty; the accounts-only Robert fixture stays a
///     no-op; the <c>Gstr9a</c> quarter-sum correction leaves <c>Gstr4</c>/<c>Cmp08</c> byte-identical.</item>
/// </list>
/// </summary>
public sealed class Gstr9AnnualTests
{
    private static readonly DateOnly FyStart = new(2025, 4, 1);
    private static readonly DateOnly FyEnd = new(2026, 3, 31);

    private const string GstinMaharashtra = "27AAPFU0939F1ZV";
    private const string GstinGujarat = "24AAACC1206D1ZM";

    // ---- The 12 calendar-month sub-windows of the FY (the Σ-of-periods convention GSTR-9 aggregates over). ----
    private static IReadOnlyList<(DateOnly From, DateOnly To)> MonthlyWindows(DateOnly fyFrom)
    {
        var w = new List<(DateOnly, DateOnly)>(12);
        for (var i = 0; i < 12; i++)
            w.Add((fyFrom.AddMonths(i), fyFrom.AddMonths(i + 1).AddDays(-1)));
        return w;
    }

    /// <summary>
    /// Re-expresses an HSN row's <b>DECLARED</b> quantity in the item's base unit, using only the company's UNIT
    /// MASTERS. Deliberately independent of anything either report derives, so an expectation built on it checks
    /// the reports rather than restating them: a row declaring "5 DOZ" and a row declaring "60 NOS" describe the
    /// same supply and both land on 60 here, while a row declaring "8" of a catch-all does not.
    /// </summary>
    private static decimal ToBaseMeasure(Company c, Gstr1HsnRow row)
    {
        if (row.DeclaredUnitId is not { } declared || row.BaseUnitId is not { } baseUnit || declared == baseUnit)
            return row.Quantity;
        var compound = c.Units.Single(u =>
            u.IsCompound && u.FirstUnitId == declared && u.BaseMeasureUnitId == baseUnit);
        return compound.QuantityInBaseMeasure(row.Quantity);
    }

    private static Domain.Ledger Add(Company c, string name, string groupName, bool openingIsDebit)
    {
        var l = new Domain.Ledger(Guid.NewGuid(), name, c.FindGroupByName(groupName)!.Id, Money.Zero, openingIsDebit);
        c.AddLedger(l);
        return l;
    }

    private static Company NewRegular(string name = "Annual Co")
    {
        var c = CompanyFactory.CreateSeeded(name, FyStart);
        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = "27", Gstin = GstinMaharashtra, RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart, Periodicity = GstReturnPeriodicity.Monthly,
        });
        return c;
    }

    // ================================================================ full-year outward/inward fixture

    /// <summary>A regular GST company with outward supplies + a purchase spread across April/July/October, plus an
    /// imported GSTR-2B snapshot — the fixture the 9↔3B / 9↔1 / Table-8A foots assert against.</summary>
    private static Company BuildFullYear()
    {
        var c = NewRegular();
        var gst = new GstService(c);
        var inv = new InventoryService(c);
        var grp = inv.CreateStockGroup("Goods");
        var nos = inv.CreateSimpleUnit("Nos", "Numbers", unitQuantityCode: "NOS");
        var main = c.MainLocation!.Id;

        var widget = inv.CreateStockItem("Widget", grp.Id, nos.Id);
        widget.Gst = new StockItemGstDetails { HsnSac = "847130", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800 };
        var gadget = inv.CreateStockItem("Gadget", grp.Id, nos.Id);
        gadget.Gst = new StockItemGstDetails { HsnSac = "852990", Taxability = GstTaxability.Taxable, RateBasisPoints = 500 };
        var book = inv.CreateStockItem("Book", grp.Id, nos.Id);
        book.Gst = new StockItemGstDetails { HsnSac = "490199", Taxability = GstTaxability.Exempt };
        inv.AddOpeningBalance(widget.Id, main, 100m, Money.FromRupees(50m));
        inv.AddOpeningBalance(gadget.Id, main, 40m, Money.FromRupees(20m));
        inv.AddOpeningBalance(book.Id, main, 5m, Money.FromRupees(150m));

        var sales = Add(c, "Sales", "Sales Accounts", false);
        var purchases = Add(c, "Purchases", "Purchase Accounts", true);
        var localDebtor = Add(c, "Local Debtor", "Sundry Debtors", true);
        localDebtor.PartyGst = new PartyGstDetails { RegistrationType = GstRegistrationType.Regular, Gstin = GstinMaharashtra, StateCode = "27" };
        var gujaratDebtor = Add(c, "Gujarat Debtor", "Sundry Debtors", true);
        gujaratDebtor.PartyGst = new PartyGstDetails { RegistrationType = GstRegistrationType.Regular, Gstin = GstinGujarat, StateCode = "24" };
        var consumer = Add(c, "Walk-in Consumer", "Sundry Debtors", true);
        consumer.PartyGst = new PartyGstDetails { RegistrationType = GstRegistrationType.Consumer, StateCode = "27" };
        var supplier = Add(c, "Local Supplier", "Sundry Creditors", false);
        supplier.PartyGst = new PartyGstDetails { RegistrationType = GstRegistrationType.Regular, Gstin = GstinMaharashtra, StateCode = "27" };

        var ledgers = new LedgerService(c);
        var salesType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id;
        var purchaseType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase).Id;

        // April — purchase (ITC) ₹5000 @18% intra ⇒ Input CGST 450 + SGST 450.
        PostPurchase(c, gst, ledgers, purchaseType, purchases, supplier, widget, main, 5000m, 50m, 100m, new(2025, 4, 3));
        // April — intra B2B sale 10 Widget @₹100 = ₹1000 @18% ⇒ CGST 90 + SGST 90.
        PostSale(c, gst, ledgers, salesType, sales, localDebtor, widget, main, 1000m, 10m, 100m, 1800, false, new(2025, 4, 5));
        // July — inter B2B sale 20 Widget @₹100 = ₹2000 @18% ⇒ IGST 360.
        PostSale(c, gst, ledgers, salesType, sales, gujaratDebtor, widget, main, 2000m, 20m, 100m, 1800, true, new(2025, 7, 7));
        // July — B2C intra sale 40 Gadget @₹25 = ₹1000 @5% ⇒ CGST 25 + SGST 25.
        PostSale(c, gst, ledgers, salesType, sales, consumer, gadget, main, 1000m, 40m, 25m, 500, false, new(2025, 7, 9));
        // October — exempt sale 5 Book @₹200 = ₹1000 ⇒ zero tax.
        ledgers.Post(new Voucher(Guid.NewGuid(), salesType, new(2025, 10, 11), new[]
        {
            new EntryLine(localDebtor.Id, Money.FromRupees(1000m), DrCr.Debit),
            new EntryLine(sales.Id, Money.FromRupees(1000m), DrCr.Credit),
        }, partyId: localDebtor.Id, inventoryLines: new[] { new VoucherInventoryLine(book.Id, main, 5m, Money.FromRupees(200m)) }));

        // An imported GSTR-2B snapshot (period 2025-07): one ITC-available B2B line (IGST ₹1800) + one NOT-available line
        // (excluded from 8A). Table 8A = Σ the ITC-available GST tax = ₹1800.
        var available = new Gstr2bLine(Guid.NewGuid(), GstinMaharashtra, null, Gstr2bDocType.B2b, "2BINV1", "2BINV1",
            new(2025, 7, 15), "27", 1_000_000, 180_000, 0, 0, 0, itcAvailable: true, null, false);
        var blocked = new Gstr2bLine(Guid.NewGuid(), GstinGujarat, null, Gstr2bDocType.B2b, "2BINV2", "2BINV2",
            new(2025, 7, 16), "24", 500_000, 90_000, 0, 0, 0, itcAvailable: false, "POS", false);
        c.AddGstr2bSnapshot(new Gstr2bSnapshot(Guid.NewGuid(), GstStatementType.Gstr2b, "2025-07", GstinMaharashtra,
            new(2025, 8, 14), "HASH2B", new DateTimeOffset(2025, 8, 14, 9, 0, 0, TimeSpan.FromHours(5.5)),
            180_000, 0, 0, 0, new[] { available, blocked }));

        return c;
    }

    private static void PostPurchase(Company c, GstService gst, LedgerService ledgers, Guid purchaseType,
        Domain.Ledger purchases, Domain.Ledger supplier, StockItem item, Guid main, decimal taxable, decimal rate,
        decimal qty, DateOnly date)
    {
        var tax = gst.ComputeInvoiceTax(new[] { new GstService.TaxableLine(Money.FromRupees(taxable), 1800) },
            interState: false, GstTaxDirection.Input);
        var lines = new List<EntryLine>
        {
            new(purchases.Id, Money.FromRupees(taxable), DrCr.Debit),
            new(supplier.Id, new Money(taxable + tax.TotalTax.Amount), DrCr.Credit),
        };
        lines.AddRange(tax.TaxLines);
        ledgers.Post(new Voucher(Guid.NewGuid(), purchaseType, date, lines, partyId: supplier.Id,
            inventoryLines: new[] { new VoucherInventoryLine(item.Id, main, qty, Money.FromRupees(rate)) }));
    }

    private static void PostSale(Company c, GstService gst, LedgerService ledgers, Guid salesType, Domain.Ledger sales,
        Domain.Ledger party, StockItem item, Guid main, decimal taxable, decimal qty, decimal rate, int rateBp,
        bool interState, DateOnly date)
    {
        var tax = gst.ComputeInvoiceTax(new[] { new GstService.TaxableLine(Money.FromRupees(taxable), rateBp) },
            interState, GstTaxDirection.Output);
        var lines = new List<EntryLine>
        {
            new(party.Id, new Money(taxable + tax.TotalTax.Amount), DrCr.Debit),
            new(sales.Id, Money.FromRupees(taxable), DrCr.Credit),
        };
        lines.AddRange(tax.TaxLines);
        ledgers.Post(new Voucher(Guid.NewGuid(), salesType, date, lines, partyId: party.Id,
            inventoryLines: new[] { new VoucherInventoryLine(item.Id, main, qty, Money.FromRupees(rate)) }));
    }

    // ---------------------------------------------------------------- TEST 1: GSTR-9 foots to Σ(3B) tax/ITC/reversal

    [Fact]
    public void Gstr9_foots_exactly_to_the_years_gstr3b_tax_itc_and_reversal()
    {
        var c = BuildFullYear();
        var g9 = Gstr9.Build(c, FyStart, FyEnd);
        Assert.True(g9.Applicable);

        // Independent Σ of the twelve already-rounded monthly GSTR-3B — the annual return must foot to it, paisa-exact.
        decimal sumOutwardTax = 0m, sumItc = 0m, sumReversed = 0m;
        foreach (var (from, to) in MonthlyWindows(FyStart))
        {
            var m = Gstr3b.Build(c, from, to);
            sumOutwardTax += m.TotalOutwardTax.Amount + m.TotalRcmOutward.Amount;
            sumItc += m.TotalItc.Amount + m.TotalRcmItc.Amount;
            sumReversed += m.TotalItcReversed.Amount;
        }

        Assert.Equal(new Money(sumOutwardTax), g9.Table4TotalTax);
        Assert.Equal(new Money(sumItc), g9.Table6ItcAvailed);
        Assert.Equal(new Money(sumReversed), g9.Table7ItcReversed);

        // Pinned figures (hand-worked): outward CGST 90+25, SGST 90+25, IGST 360; ITC 450+450; exempt ₹1000.
        Assert.Equal(Money.FromRupees(590m), g9.Table4TotalTax);   // 115 + 115 + 360
        Assert.Equal(Money.FromRupees(900m), g9.Table6ItcAvailed); // 450 + 450
        Assert.Equal(Money.FromRupees(4000m), g9.Table4TaxableValue);
        Assert.Equal(Money.FromRupees(1000m), g9.Table5ExemptNilNonGst);
        Assert.Equal(Money.FromRupees(5000m), g9.Table5NTurnover);
    }

    // ---------------------------------------------------------------- TEST 2: GSTR-9 foots to Σ(GSTR-1) outward + HSN

    [Fact]
    public void Gstr9_table17_hsn_foots_to_the_years_gstr1_hsn_summary()
    {
        var c = BuildFullYear();
        var g9 = Gstr9.Build(c, FyStart, FyEnd);

        // Independent Σ of the twelve monthly GSTR-1 HSN summaries, merged by HSN.
        //
        // The quantity expectation is stated PHYSICALLY, not by restating the aggregator's rule. The previous
        // version recomputed `Mixed` here as `!string.Equals(cur.Uqc, h.Uqc)` — a verbatim copy of the
        // implementation's own predicate — so it agreed with the code by construction and went on passing while
        // the annual row declared "8" for 81 Nos. A test that mirrors the rule can only ever confirm that the
        // code does what the code does.
        //
        // What is asserted instead: whatever label the annual row chooses, the quantity it declares must convert
        // — through the COMPANY'S UNIT MASTERS, which neither report is involved in defining — to the same base
        // measure the twelve monthly rows physically supplied. That statement stays true under any future change
        // to how the label is chosen, and is false whenever the declared pair misdescribes the supply.
        var expected = new Dictionary<string, (decimal Taxable, decimal Tax, decimal BaseQty)>();
        foreach (var (from, to) in MonthlyWindows(FyStart))
            foreach (var h in Gstr1.Build(c, from, to).HsnSummary)
            {
                var cur = expected.TryGetValue(h.HsnSac, out var v) ? v : (Taxable: 0m, Tax: 0m, BaseQty: 0m);
                expected[h.HsnSac] = (
                    cur.Taxable + h.TaxableValue.Amount,
                    cur.Tax + h.TotalTax.Amount,
                    cur.BaseQty + h.BaseQuantity);
            }

        var actual = g9.Table17Hsn.ToDictionary(h => h.HsnSac);
        Assert.Equal(expected.Count, actual.Count);
        foreach (var (hsn, exp) in expected)
        {
            var row = actual[hsn];
            Assert.Equal(new Money(exp.Taxable), row.TaxableValue);
            Assert.Equal(new Money(exp.Tax), row.TotalTax);
            // The declared (Quantity, unit) pair, converted to the item's base unit, IS the physical measure.
            Assert.Equal(exp.BaseQty, ToBaseMeasure(c, row));
            Assert.Equal(exp.BaseQty, row.BaseQuantity);
        }

        // Pinned: Widget 30 nos taxable ₹3000 tax 540; Gadget 40 taxable ₹1000 tax 50; Book (exempt) taxable ₹1000 tax 0.
        // (These are hand-worked from the fixture, independent of both reports.)
        Assert.Equal(Money.FromRupees(5000m), g9.Table17TaxableValue);
        Assert.Equal(Money.FromRupees(590m), g9.Table17TotalTax);
        var widget = actual["847130"];
        Assert.Equal(30m, widget.Quantity);
        Assert.Equal(Money.FromRupees(3000m), widget.TaxableValue);
        Assert.Equal(Money.FromRupees(540m), widget.TotalTax);
    }

    // ---------------------------------------------------------------- TEST (8A): GSTR-9 Table 8A pulls the 2B figure

    [Fact]
    public void Gstr9_table8a_pulls_the_imported_gstr2b_itc_and_reports_the_difference()
    {
        var c = BuildFullYear();
        var g9 = Gstr9.Build(c, FyStart, FyEnd);

        // 8A = Σ the ITC-available GSTR-2B lines' GST tax (the NOT-available line is excluded) = ₹1800.
        Assert.Equal(Money.FromRupees(1800m), g9.Table8A);
        Assert.Equal(g9.Table6ItcAvailed, g9.Table8B);                 // 8B = ITC availed per Table 6 (₹900)
        Assert.Equal(Money.FromRupees(900m), g9.Table8D);              // 8D = 8A − 8B = 1800 − 900 (reported, not zeroed)
    }

    // ---------------------------------------------------------------- TEST 3: GSTR-9 Table 9 credit-vs-cash split

    [Fact]
    public void Gstr9_table9_splits_tax_paid_through_itc_versus_in_cash()
    {
        var c = NewRegular("Table9 Co");
        var gst = new GstService(c);
        var bank = Add(c, "Bank", "Bank Accounts", true);
        var debtor = Add(c, "Debtor", "Sundry Debtors", true);
        var creditor = Add(c, "Creditor", "Sundry Creditors", false);
        var sales = Add(c, "Sales", "Sales Accounts", false);
        var purchases = Add(c, "Purchases", "Purchase Accounts", true);

        // Output CGST 1800 + SGST 1800 (₹20000 @18%); Input CGST 900 + SGST 900 (₹10000 @18%). Net ₹900 CGST + SGST cash.
        PostIntraSale(c, gst, debtor, sales, 20000m, new(2025, 4, 5));
        PostIntraPurchase(c, gst, creditor, purchases, 10000m, new(2025, 4, 3));

        var alloc = SetOff.Allocate(new SetOff.SetOffDemand(180000, 180000, 0, 0, 0, 90000, 90000, 0, 0));
        new SetOff(c).PostSetOff("2025-04", alloc, new(2025, 4, 30));

        var deposit = new GstDepositService(c);
        deposit.PostPmt06(GstTaxHead.Central, GstMinorHead.Tax, Money.FromRupees(900m), bank, new(2025, 4, 30), "CPIN-C", "CIN-C");
        deposit.PostPmt06(GstTaxHead.State, GstMinorHead.Tax, Money.FromRupees(900m), bank, new(2025, 4, 30), "CPIN-S", "CIN-S");

        var g9 = Gstr9.Build(c, FyStart, FyEnd);

        // Paid-through-ITC = Σ the non-cash Table-6.1 set-off lines (credit utilisation: CGST 900 + SGST 900 = ₹1800).
        var creditUtilised = c.GstSetoffLines.Where(l => !l.IsCash).Sum(l => l.AmountPaisa);
        Assert.Equal(180000, creditUtilised);
        Assert.Equal(Money.FromRupees(1800m), g9.Table9PaidThroughItc);

        // Paid-in-cash = Σ the PMT-06 challan deposits (₹900 + ₹900 = ₹1800).
        Assert.Equal(Money.FromRupees(1800m), g9.Table9PaidInCash);
    }

    private static void PostIntraSale(Company c, GstService gst, Domain.Ledger party, Domain.Ledger sales, decimal taxable, DateOnly d)
    {
        var tax = gst.ComputeInvoiceTax(new[] { new GstService.TaxableLine(Money.FromRupees(taxable), 1800) }, false, GstTaxDirection.Output);
        var lines = new List<EntryLine> { new(party.Id, Money.FromRupees(taxable + tax.TotalTax.Amount), DrCr.Debit), new(sales.Id, Money.FromRupees(taxable), DrCr.Credit) };
        lines.AddRange(tax.TaxLines);
        new LedgerService(c).Post(new Voucher(Guid.NewGuid(), c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id, d, lines, partyId: party.Id));
    }

    private static void PostIntraPurchase(Company c, GstService gst, Domain.Ledger party, Domain.Ledger purchases, decimal taxable, DateOnly d)
    {
        var tax = gst.ComputeInvoiceTax(new[] { new GstService.TaxableLine(Money.FromRupees(taxable), 1800) }, false, GstTaxDirection.Input);
        var lines = new List<EntryLine> { new(purchases.Id, Money.FromRupees(taxable), DrCr.Debit), new(party.Id, Money.FromRupees(taxable + tax.TotalTax.Amount), DrCr.Credit) };
        lines.AddRange(tax.TaxLines);
        new LedgerService(c).Post(new Voucher(Guid.NewGuid(), c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase).Id, d, lines, partyId: party.Id));
    }

    // ---------------------------------------------------------------- TEST 4: GSTR-9 Table 7 rule-split

    [Fact]
    public void Gstr9_table7_buckets_reversals_by_rule_and_foots_to_the_years_gstr3b()
    {
        var c = NewRegular("Table7 Co");
        var gst = new GstService(c);
        var bank = Add(c, "Bank", "Bank Accounts", true);
        var supplier = Add(c, "Supplier", "Sundry Creditors", false);
        supplier.PartyGst = new PartyGstDetails { RegistrationType = GstRegistrationType.Regular, Gstin = GstinGujarat, StateCode = "24" };
        supplier.MaintainBillByBill = true;
        var svc = new Rev(c);

        // Purchase A: intra ₹100000 @18% ⇒ Input CGST 9000 + SGST 9000 (the Rule 42/43 pool).
        var poolVid = PostIntraPurchaseVid(c, gst, bank, 100000m, new(2025, 10, 3));
        // Purchase B: inter ₹10000 @18% ⇒ Input IGST 1800 (the Rule 37 source).
        var rule37Vid = PostInterPurchaseVid(c, gst, supplier, 10000m, "INV-37", new(2025, 10, 4));

        var oct = new DateOnly(2025, 10, 15);
        // Rule 42 (D1 0.4×1000 + D2 50 = ₹450/head) ⇒ CGST 450 + SGST 450 → 7C.
        svc.PostRule42("2025-10", new Rev.Rule42Basis(new Rev.ReversalAmount(100_000, 100_000, 0, 0), 40_000_000, 100_000_000), oct);
        // Rule 43 (Tc ₹60000 ⇒ Tm 1000 ⇒ Te 0.4×1000 = ₹400 CGST) → 7D.
        svc.PostRule43("2025-10", poolVid, new Rev.Rule43Basis(new Rev.ReversalAmount(6_000_000, 0, 0, 0), 40_000_000, 100_000_000), oct);
        // Rule 37 (full forward ITC ₹1800 IGST) → 7A.
        svc.PostRule37(rule37Vid, "2025-10", oct);

        var g9 = Gstr9.Build(c, FyStart, FyEnd);
        Assert.Equal(Money.FromRupees(900m), g9.Table7Rule42);        // 7C: 450 + 450
        Assert.Equal(Money.FromRupees(400m), g9.Table7Rule43);        // 7D
        Assert.Equal(Money.FromRupees(1800m), g9.Table7Rule37);      // 7A (Rule 37 + 37A)
        Assert.Equal(Money.Zero, g9.Table7Section17_5);              // no §17(5) reversal posted
        Assert.Equal(Money.Zero, g9.Table7Other);

        // Foots to Σ the year's GSTR-3B ITC reversed (4(B)(1) Rule 42/43 + 4(B)(2) Rule 37).
        decimal sumReversed = 0m;
        foreach (var (from, to) in MonthlyWindows(FyStart))
            sumReversed += Gstr3b.Build(c, from, to).TotalItcReversed.Amount;
        Assert.Equal(Money.FromRupees(3100m), new Money(sumReversed)); // 900 + 400 + 1800
        Assert.Equal(new Money(sumReversed), g9.Table7ItcReversed);
    }

    private static Guid PostIntraPurchaseVid(Company c, GstService gst, Domain.Ledger bank, decimal taxable, DateOnly d)
    {
        var purchases = c.FindLedgerByName("Purchases") ?? Add(c, "Purchases", "Purchase Accounts", true);
        var tax = gst.ComputeInvoiceTax(new[] { new GstService.TaxableLine(Money.FromRupees(taxable), 1800) }, false, GstTaxDirection.Input);
        var lines = new List<EntryLine> { new(purchases.Id, Money.FromRupees(taxable), DrCr.Debit), new(bank.Id, Money.FromRupees(taxable + tax.TotalTax.Amount), DrCr.Credit) };
        lines.AddRange(tax.TaxLines);
        return new LedgerService(c).Post(new Voucher(Guid.NewGuid(), c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase).Id, d, lines)).Id;
    }

    private static Guid PostInterPurchaseVid(Company c, GstService gst, Domain.Ledger supplier, decimal taxable, string billRef, DateOnly d)
    {
        var purchases = c.FindLedgerByName("Purchases") ?? Add(c, "Purchases", "Purchase Accounts", true);
        var tax = gst.ComputeInvoiceTax(new[] { new GstService.TaxableLine(Money.FromRupees(taxable), 1800) }, true, GstTaxDirection.Input);
        var credit = new Money(taxable + tax.TotalTax.Amount);
        var lines = new List<EntryLine>
        {
            new(purchases.Id, Money.FromRupees(taxable), DrCr.Debit),
            new(supplier.Id, credit, DrCr.Credit, billAllocations: new[] { new BillAllocation(BillRefType.NewRef, billRef, credit) }),
        };
        lines.AddRange(tax.TaxLines);
        return new LedgerService(c).Post(new Voucher(Guid.NewGuid(), c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase).Id, d, lines, partyId: supplier.Id)).Id;
    }

    // ---------------------------------------------------------------- TEST 5: Composition / GST-off ⇒ not-applicable 9

    [Fact]
    public void Gstr9_is_not_applicable_for_a_composition_or_gst_off_company()
    {
        var comp = CompanyFactory.CreateSeeded("Comp Co", FyStart);
        new GstService(comp).EnableGst(new GstConfig
        {
            HomeStateCode = "27", Gstin = GstinMaharashtra, RegistrationType = GstRegistrationType.Composition,
            CompositionSubType = CompositionSubType.Trader, ApplicableFrom = FyStart, Periodicity = GstReturnPeriodicity.Quarterly,
        });
        var gc = Gstr9.Build(comp, FyStart, FyEnd);
        Assert.False(gc.Applicable);
        Assert.Equal(Money.Zero, gc.Table4TotalTax);
        Assert.Equal(Money.Zero, gc.Table6ItcAvailed);
        Assert.Empty(gc.Table17Hsn);

        var off = CompanyFactory.CreateSeeded("Plain Co", FyStart);
        Assert.False(off.GstEnabled);
        var go = Gstr9.Build(off, FyStart, FyEnd);
        Assert.False(go.Applicable);
        Assert.Equal(Money.Zero, go.Table5NTurnover);
        Assert.Equal(Money.Zero, go.Table9PaidInCash);
    }

    // ---------------------------------------------------------------- TEST 6: determinism

    [Fact]
    public void Gstr9_is_deterministic_across_runs()
    {
        var c = BuildFullYear();
        var a = Gstr9.Build(c, FyStart, FyEnd);
        var b = Gstr9.Build(c, FyStart, FyEnd);

        Assert.Equal(a.Table4TotalTax, b.Table4TotalTax);
        Assert.Equal(a.Table6ItcAvailed, b.Table6ItcAvailed);
        Assert.Equal(a.Table7ItcReversed, b.Table7ItcReversed);
        Assert.Equal(a.Table5NTurnover, b.Table5NTurnover);
        Assert.Equal(a.Table8A, b.Table8A);
        Assert.Equal(a.Table17TotalTax, b.Table17TotalTax);
        Assert.Equal(a.Table17Hsn.Count, b.Table17Hsn.Count);
        for (var i = 0; i < a.Table17Hsn.Count; i++)
            Assert.Equal(a.Table17Hsn[i].HsnSac, b.Table17Hsn[i].HsnSac);
    }

    // ---------------------------------------------------------------- TEST 7: GSTR-9A foots to Σ CMP-08 (quarter-sum)

    /// <summary>With odd-paisa turnover of ₹1,250.50 in EACH quarter, a whole-FY re-round gives round(5002.00 × 1%) =
    /// ₹50.02, but Σ the four rounded CMP-08 (₹12.51 each) = ₹50.04. GSTR-9A composition-tax-paid must equal Σ CMP-08 —
    /// the <c>Gstr4</c> Σ-of-quarters template — NOT the whole-FY re-round. Fails pre-fix (₹50.02 ≠ ₹50.04).</summary>
    [Fact]
    public void Gstr9a_composition_tax_paid_equals_the_sum_of_the_four_cmp08_quarters()
    {
        var c = CompanyFactory.CreateSeeded("9A Co", FyStart);
        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = "27", Gstin = GstinMaharashtra, RegistrationType = GstRegistrationType.Composition,
            CompositionSubType = CompositionSubType.Trader, ApplicableFrom = FyStart, Periodicity = GstReturnPeriodicity.Quarterly,
        });
        var party = Add(c, "Walk-in", "Sundry Debtors", true);
        var sales = new Domain.Ledger(Guid.NewGuid(), "Sales", c.FindGroupByName("Sales Accounts")!.Id, Money.Zero, false)
        {
            SalesPurchaseGst = new StockItemGstDetails { Taxability = GstTaxability.Taxable, RateBasisPoints = 1800 },
        };
        c.AddLedger(sales);
        var ledgers = new LedgerService(c);
        var salesType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id;
        foreach (var date in new[] { new DateOnly(2025, 4, 15), new DateOnly(2025, 8, 15), new DateOnly(2025, 11, 15), new DateOnly(2026, 2, 15) })
            ledgers.Post(new Voucher(Guid.NewGuid(), salesType, date, new[]
            {
                new EntryLine(party.Id, Money.FromRupees(1250.50m), DrCr.Debit),
                new EntryLine(sales.Id, Money.FromRupees(1250.50m), DrCr.Credit),
            }, partyId: party.Id));

        var g9a = Gstr9a.Build(c, FyStart, FyEnd);
        Assert.True(g9a.Applicable);

        decimal sumOfQuarters = 0m;
        for (var i = 0; i < 4; i++)
        {
            var q = Cmp08.Build(c, FyStart.AddMonths(3 * i), FyStart.AddMonths(3 * (i + 1)).AddDays(-1));
            sumOfQuarters += q.OutwardTurnoverTax.Amount + q.InwardRcmTax.Amount;
        }
        Assert.Equal(Money.FromRupees(50.04m), new Money(sumOfQuarters));           // Σ CMP-08
        Assert.Equal(new Money(sumOfQuarters), g9a.CompositionTaxPaid);             // 9A == Σ CMP-08 (reconciles)
        Assert.Equal(Money.FromRupees(50.04m), g9a.CompositionTaxPaid);            // NOT the ₹50.02 whole-FY re-round

        Assert.False(Gstr9a.Build(NewRegular(), FyStart, FyEnd).Applicable);
    }

    // ---------------------------------------------------------------- TEST 8/10: GSTR-9C reconciles to the books

    /// <summary>A GST-enabled trading company (Bright is GST-off, so a GST twin is used): 9C reconciles GSTR-9 turnover +
    /// ITC to the audited books (P&amp;L income + Input-ledger closings). The unreconciled difference is computed and
    /// shown — here a deliberate book-only (Schedule-III) income surfaces as a non-zero 5R, never silently absorbed.</summary>
    private static Company BuildGstTrading9c(decimal bookOnlyIncome)
    {
        var c = NewRegular("9C Trading Co");
        var gst = new GstService(c);
        var inv = new InventoryService(c);
        var grp = inv.CreateStockGroup("Goods");
        var nos = inv.CreateSimpleUnit("Nos", "Numbers", unitQuantityCode: "NOS");
        var main = c.MainLocation!.Id;
        var widget = inv.CreateStockItem("Widget", grp.Id, nos.Id);
        widget.Gst = new StockItemGstDetails { HsnSac = "847130", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800 };
        inv.AddOpeningBalance(widget.Id, main, 100m, Money.FromRupees(50m));

        var sales = Add(c, "Sales", "Sales Accounts", false);
        var purchases = Add(c, "Purchases", "Purchase Accounts", true);
        var debtor = Add(c, "Debtor", "Sundry Debtors", true);
        debtor.PartyGst = new PartyGstDetails { RegistrationType = GstRegistrationType.Regular, Gstin = GstinMaharashtra, StateCode = "27" };
        var supplier = Add(c, "Supplier", "Sundry Creditors", false);
        supplier.PartyGst = new PartyGstDetails { RegistrationType = GstRegistrationType.Regular, Gstin = GstinMaharashtra, StateCode = "27" };
        var ledgers = new LedgerService(c);
        var salesType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id;
        var purchaseType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase).Id;

        // Purchase ₹5000 @18% intra ⇒ Input CGST 450 + SGST 450 (books ITC).
        PostPurchase(c, gst, ledgers, purchaseType, purchases, supplier, widget, main, 5000m, 50m, 100m, new(2025, 4, 3));
        // Sale ₹8000 @18% intra ⇒ output CGST 720 + SGST 720; turnover ₹8000.
        PostSale(c, gst, ledgers, salesType, sales, debtor, widget, main, 8000m, 80m, 100m, 1800, false, new(2025, 4, 5));

        // An optional book-only income (a Schedule-III supply) — in the books' P&L but NOT in the GST return turnover.
        if (bookOnlyIncome != 0m)
        {
            var other = Add(c, "Schedule-III Income", "Indirect Incomes", false);
            var cash = c.FindLedgerByName("Cash") ?? Add(c, "Cash", "Cash-in-Hand", true);
            ledgers.Post(new Voucher(Guid.NewGuid(), c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Receipt).Id, new(2025, 5, 10), new[]
            {
                new EntryLine(cash.Id, Money.FromRupees(bookOnlyIncome), DrCr.Debit),
                new EntryLine(other.Id, Money.FromRupees(bookOnlyIncome), DrCr.Credit),
            }));
        }
        return c;
    }

    [Fact]
    public void Gstr9c_reconciles_gstr9_turnover_and_itc_to_the_books()
    {
        var c = BuildGstTrading9c(bookOnlyIncome: 0m);
        var g9c = Gstr9c.Build(c, FyStart, FyEnd);
        Assert.True(g9c.Applicable);

        // 5A = the audited P&L total income; 5Q = the GSTR-9 total turnover; 5R = 5A − 5Q (reported).
        var pl = ProfitAndLoss.Build(c, FyEnd);
        var g9 = Gstr9.Build(c, FyStart, FyEnd);
        Assert.Equal(pl.TotalIncome, g9c.Table5ABooksTurnover);
        Assert.Equal(g9.Table5NTurnover, g9c.Table5QReturnTurnover);
        Assert.Equal(new Money(pl.TotalIncome.Amount - g9.Table5NTurnover.Amount), g9c.Table5RUnreconciledTurnover);
        Assert.Equal(Money.FromRupees(8000m), g9c.Table5QReturnTurnover); // the single ₹8000 taxable sale
        Assert.Equal(Money.FromRupees(8000m), g9c.Table5ABooksTurnover); // books income == return turnover (no book-only income)
        Assert.Equal(Money.Zero, g9c.Table5RUnreconciledTurnover);

        // 12A = ITC per books (Input-ledger closings ₹900); 12E = net ITC per GSTR-9 (₹900); 12F = 0.
        Assert.Equal(Money.FromRupees(900m), g9c.Table12ABooksItc);
        Assert.Equal(g9.NetItc, g9c.Table12EReturnItc);
        Assert.Equal(Money.FromRupees(900m), g9c.Table12EReturnItc);
        Assert.Equal(new Money(g9c.Table12ABooksItc.Amount - g9c.Table12EReturnItc.Amount), g9c.Table12FUnreconciledItc);
    }

    [Fact]
    public void Gstr9c_shows_a_genuine_book_versus_return_difference_on_the_unreconciled_line()
    {
        var c = BuildGstTrading9c(bookOnlyIncome: 1500m); // a ₹1500 Schedule-III income in the books only
        var g9c = Gstr9c.Build(c, FyStart, FyEnd);

        // The books show ₹8000 sale + ₹1500 other income = ₹9500; the return turnover is only the ₹8000 GST sale.
        Assert.Equal(Money.FromRupees(9500m), g9c.Table5ABooksTurnover);
        Assert.Equal(Money.FromRupees(8000m), g9c.Table5QReturnTurnover);
        // 5R surfaces the ₹1500 difference — computed and SHOWN, never silently absorbed to zero.
        Assert.Equal(Money.FromRupees(1500m), g9c.Table5RUnreconciledTurnover);
        Assert.NotEqual(Money.Zero, g9c.Table5RUnreconciledTurnover);
    }

    // ------------------------------------------------------ REGRESSION (FIX 1): 5A books turnover is FY-WINDOW, not cumulative

    /// <summary>A multi-year company (books begin a full FY before the return FY): 9C Table 5A "books turnover" must be
    /// the FY-WINDOW revenue, NOT the cumulative books-begin→To income — else a prior-FY sale spuriously inflates 5A and
    /// makes 5R non-zero for a perfectly reconciled year. The return side (5Q) is strictly FY-scoped, so the books side
    /// must be too. Pre-fix: 5A = ₹13,000 (cumulative) and 5R = ₹5,000 (the prior-FY sale) — both FAIL.</summary>
    [Fact]
    public void Gstr9c_table5a_books_turnover_is_the_fy_window_not_the_cumulative_income()
    {
        var booksBegin = new DateOnly(2024, 4, 1);
        var c = CompanyFactory.CreateSeeded("MultiYear 9C Co", booksBegin);
        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = "27", Gstin = GstinMaharashtra, RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = booksBegin, Periodicity = GstReturnPeriodicity.Monthly,
        });
        var gst = new GstService(c);
        var debtor = Add(c, "Debtor", "Sundry Debtors", true);
        debtor.PartyGst = new PartyGstDetails { RegistrationType = GstRegistrationType.Regular, Gstin = GstinMaharashtra, StateCode = "27" };
        var sales = Add(c, "Sales", "Sales Accounts", false);

        // Prior FY (2024-25): a ₹5,000 intra sale — in the books' CUMULATIVE P&L but OUTSIDE FY 2025-26.
        PostIntraSale(c, gst, debtor, sales, 5000m, new(2024, 6, 1));
        // Current FY (2025-26): a ₹8,000 intra sale — the only turnover the return FY should reflect.
        PostIntraSale(c, gst, debtor, sales, 8000m, new(2025, 4, 5));

        var g9c = Gstr9c.Build(c, FyStart, FyEnd);

        // 5A must be the FY-window books revenue (₹8,000), NOT the cumulative ₹13,000; 5Q is already FY-scoped; 5R = 0.
        Assert.Equal(Money.FromRupees(8000m), g9c.Table5ABooksTurnover);
        Assert.Equal(Money.FromRupees(8000m), g9c.Table5QReturnTurnover);
        Assert.Equal(Money.Zero, g9c.Table5RUnreconciledTurnover);
    }

    // ------------------------------------ REGRESSION (FIX 2): tax/ITC per-books anchors are FY GROSS ACCRUAL, not net closing

    /// <summary>A company whose full ₹1,440 output liability is DISCHARGED (Rule-88A set-off of ₹900 ITC + ₹540 PMT-06
    /// cash): the 9C "per books" tax (Table 9) and ITC (Table 12A) anchors must be the FY GROSS ACCRUAL flows (Output
    /// credit legs / Input debit legs), NOT the from-inception NET closing balances — which the set-off + cash discharge
    /// draw toward zero. Pre-fix Table9TaxPerBooks→0 (so Table 11 = full ₹1,440 liability) and Table12ABooksItc→0 (so
    /// Table 12F = −₹900) — both spurious. Post-fix the routine discharge reconciles to ~zero.</summary>
    [Fact]
    public void Gstr9c_reconciles_a_fully_discharged_company_tax_and_itc_to_zero()
    {
        var c = BuildGstTrading9c(bookOnlyIncome: 0m);           // sale ₹8000⇒output 1440; purchase ₹5000⇒ITC 900
        var bank = Add(c, "Bank", "Bank Accounts", true);

        // Rule-88A set-off — ITC ₹900 (CGST 450 + SGST 450) discharges ₹900 of the ₹1440 output liability.
        var alloc = SetOff.Allocate(new SetOff.SetOffDemand(72000, 72000, 0, 0, 0, 45000, 45000, 0, 0));
        new SetOff(c).PostSetOff("2025-04", alloc, new(2025, 4, 30));

        // PMT-06 cash ₹540 (CGST 270 + SGST 270) then discharge the residual output liability from cash.
        var deposit = new GstDepositService(c);
        deposit.PostPmt06(GstTaxHead.Central, GstMinorHead.Tax, Money.FromRupees(270m), bank, new(2025, 4, 30), "CPIN-C", "CIN-C");
        deposit.PostPmt06(GstTaxHead.State, GstMinorHead.Tax, Money.FromRupees(270m), bank, new(2025, 4, 30), "CPIN-S", "CIN-S");
        deposit.PostCashDischarge(GstTaxHead.Central, Money.FromRupees(270m), new(2025, 4, 30));
        deposit.PostCashDischarge(GstTaxHead.State, Money.FromRupees(270m), new(2025, 4, 30));

        var g9c = Gstr9c.Build(c, FyStart, FyEnd);

        // The per-books anchors are the FY accrual, so the routine discharge does NOT surface as an unreconciled delta.
        Assert.Equal(g9c.Table9TaxPerReturn, g9c.Table9TaxPerBooks);   // ₹1440 == ₹1440 (pre-fix: 1440 vs 0)
        Assert.Equal(Money.Zero, g9c.Table11UnreconciledTax);         // pre-fix: ₹1440
        Assert.Equal(Money.FromRupees(900m), g9c.Table12ABooksItc);   // FY ITC availed (pre-fix: 0)
        Assert.Equal(Money.Zero, g9c.Table12FUnreconciledItc);       // pre-fix: −₹900
    }

    // ------------------------------------ REGRESSION (FIX 3): Table 9 "paid in cash" counts only the Tax minor head

    /// <summary>GSTR-9 Table 9 "tax paid in cash" must sum only the <b>Tax</b> minor-head PMT-06 challans — an
    /// interest / late-fee / penalty deposit is NOT tax paid (DP-34). Pre-fix the accumulation added every challan's
    /// amount regardless of minor head, so the ₹200 interest deposit inflated the figure to ₹2,000 — FAIL.</summary>
    [Fact]
    public void Gstr9_table9_paid_in_cash_counts_only_the_tax_minor_head_challans()
    {
        var c = NewRegular("Table9 Cash Co");
        var bank = Add(c, "Bank", "Bank Accounts", true);
        var deposit = new GstDepositService(c);

        deposit.PostPmt06(GstTaxHead.Central, GstMinorHead.Tax, Money.FromRupees(1800m), bank, new(2025, 4, 30), "CPIN-T", "CIN-T");
        deposit.PostPmt06(GstTaxHead.Central, GstMinorHead.Interest, Money.FromRupees(200m), bank, new(2025, 4, 30), "CPIN-I", "CIN-I");

        var g9 = Gstr9.Build(c, FyStart, FyEnd);
        // Only the ₹1,800 Tax deposit is "tax paid in cash"; the ₹200 §50 interest deposit is excluded.
        Assert.Equal(Money.FromRupees(1800m), g9.Table9PaidInCash);
    }

    // ------------------------------------ REGRESSION (Table 8A): a re-imported 2B snapshot for the same period is deduped

    /// <summary>A re-import of GSTR-2B for the SAME return period creates a fresh snapshot (the old one untouched, ER-6).
    /// Table 8A must take the LATEST snapshot per period (by <c>ImportedAt</c>), never SUM both — else a routine
    /// revision double-counts the year's ITC. Pre-fix Table 8A = ₹1,800 + ₹2,000 = ₹3,800 — FAIL.</summary>
    [Fact]
    public void Gstr9_table8a_dedups_a_reimported_2b_snapshot_for_the_same_period()
    {
        var c = NewRegular("8A Reimport Co");

        // Original 2B for 2025-07 (ITC ₹1,800), imported 2025-08-14.
        var v1 = new Gstr2bLine(Guid.NewGuid(), GstinMaharashtra, null, Gstr2bDocType.B2b, "INV1", "INV1",
            new(2025, 7, 15), "27", 1_000_000, 180_000, 0, 0, 0, itcAvailable: true, null, false);
        c.AddGstr2bSnapshot(new Gstr2bSnapshot(Guid.NewGuid(), GstStatementType.Gstr2b, "2025-07", GstinMaharashtra,
            new(2025, 8, 14), "HASH-V1", new DateTimeOffset(2025, 8, 14, 9, 0, 0, TimeSpan.FromHours(5.5)),
            180_000, 0, 0, 0, new[] { v1 }));

        // A REVISED re-import for the SAME 2025-07 period (ITC ₹2,000), imported later (2025-08-20) ⇒ supersedes v1.
        var v2 = new Gstr2bLine(Guid.NewGuid(), GstinMaharashtra, null, Gstr2bDocType.B2b, "INV1", "INV1",
            new(2025, 7, 15), "27", 1_000_000, 200_000, 0, 0, 0, itcAvailable: true, null, false);
        c.AddGstr2bSnapshot(new Gstr2bSnapshot(Guid.NewGuid(), GstStatementType.Gstr2b, "2025-07", GstinMaharashtra,
            new(2025, 8, 20), "HASH-V2", new DateTimeOffset(2025, 8, 20, 9, 0, 0, TimeSpan.FromHours(5.5)),
            200_000, 0, 0, 0, new[] { v2 }));

        var g9 = Gstr9.Build(c, FyStart, FyEnd);
        // Table 8A = the latest snapshot's ITC (₹2,000) — NOT the ₹1,800 + ₹2,000 = ₹3,800 double-count.
        Assert.Equal(Money.FromRupees(2000m), g9.Table8A);
    }

    // ---------------------------------------------------------------- TEST 9: GSTR-9C on Robert ⇒ not-applicable

    [Fact]
    public void Gstr9c_is_not_applicable_for_the_accounts_only_robert_fixture()
    {
        var f = FixtureLoader.Load("robert.json");
        Assert.False(f.Company.GstEnabled);
        var g9c = Gstr9c.Build(f.Company, FyStart, FyEnd);
        Assert.False(g9c.Applicable);
        Assert.Equal(Money.Zero, g9c.Table5ABooksTurnover);
        Assert.Equal(Money.Zero, g9c.Table12FUnreconciledItc);
    }

    // ---------------------------------------------------------------- TEST 20: ER-13 — off byte-identical + empty

    [Fact]
    public void Er13_a_gst_off_company_yields_empty_annual_returns_and_unchanged_phase4_returns()
    {
        // An ordinary GST-off company: the existing Phase-4 GSTR-1/3B are untouched, and the new annual returns are
        // not-applicable/all-zero (S8a adds only new entry-points; it edits no existing Build).
        var off = CompanyFactory.CreateSeeded("Off Co", FyStart);
        var g1Before = Gstr1.Build(off, FyStart, FyEnd);
        var g3Before = Gstr3b.Build(off, FyStart, FyEnd);

        Assert.False(Gstr9.Build(off, FyStart, FyEnd).Applicable);
        Assert.False(Gstr9c.Build(off, FyStart, FyEnd).Applicable);

        var g1After = Gstr1.Build(off, FyStart, FyEnd);
        var g3After = Gstr3b.Build(off, FyStart, FyEnd);
        Assert.Equal(g1Before.TotalTax, g1After.TotalTax);
        Assert.Equal(g3Before.TotalOutwardTax, g3After.TotalOutwardTax);
        Assert.Equal(g3Before.TotalItc, g3After.TotalItc);
    }

    // ---------------------------------------------------------------- TEST 21: Gstr9a fix leaves Gstr4/Cmp08 identical

    /// <summary>The §2 correction switches <c>Gstr9a.Build</c> to the CMP-08 quarter-sum; it must leave the shared
    /// <c>Gstr4</c> / <c>Cmp08</c> projections byte-identical (they are not edited). Uses the CMP-08 odd-paisa fixture.</summary>
    [Fact]
    public void The_gstr9a_quarter_sum_correction_leaves_gstr4_and_cmp08_byte_identical()
    {
        var c = CompanyFactory.CreateSeeded("Reg Bright", FyStart);
        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = "27", Gstin = GstinMaharashtra, RegistrationType = GstRegistrationType.Composition,
            CompositionSubType = CompositionSubType.Trader, ApplicableFrom = FyStart, Periodicity = GstReturnPeriodicity.Quarterly,
        });
        var party = Add(c, "Walk-in", "Sundry Debtors", true);
        var sales = new Domain.Ledger(Guid.NewGuid(), "Sales", c.FindGroupByName("Sales Accounts")!.Id, Money.Zero, false)
        {
            SalesPurchaseGst = new StockItemGstDetails { Taxability = GstTaxability.Taxable, RateBasisPoints = 1800 },
        };
        c.AddLedger(sales);
        var ledgers = new LedgerService(c);
        var salesType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id;
        foreach (var date in new[] { new DateOnly(2025, 4, 15), new DateOnly(2025, 8, 15) })
            ledgers.Post(new Voucher(Guid.NewGuid(), salesType, date, new[]
            {
                new EntryLine(party.Id, Money.FromRupees(1250.50m), DrCr.Debit),
                new EntryLine(sales.Id, Money.FromRupees(1250.50m), DrCr.Credit),
            }, partyId: party.Id));

        // GSTR-4 Table 6 still equals Σ its quarterly CMP-08 Table 5 (the shared projection is unchanged), and the new
        // GSTR-9A composition tax paid reconciles to the SAME Σ.
        var g4 = Gstr4.Build(c, FyStart, FyEnd);
        var g9a = Gstr9a.Build(c, FyStart, FyEnd);
        var sumQuarters = new Money(g4.Quarters.Sum(q => q.OutwardTurnoverTax.Amount + q.InwardRcmTax.Amount));
        Assert.Equal(sumQuarters, g4.AnnualCompositionTax + g4.AnnualRcmTax);
        Assert.Equal(sumQuarters, g9a.CompositionTaxPaid + g9a.RcmInwardTax);
    }
}
