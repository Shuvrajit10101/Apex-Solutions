using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;

using Rev = Apex.Ledger.Services.GstReversalService;

namespace Apex.Ledger.Tests;

/// <summary>
/// Phase 9 slice 7b — the <b>ITC-reversal engine</b> (RQ-27; DP-30/DP-31; ER-14; A14-CONFIRMED §11.4/§11.5/§11.7). The
/// <see cref="GstReversalService"/> is the <b>SOLE POSTER</b> of the reversal candidates S6 surfaced: it computes each
/// rule's amount (Rule 42 D1+D2; Rule 43 60-month tranche; Rule 37/37A + reclaim; §17(5)/ineligible/credit-note), posts
/// each as a balanced stat-adjustment Journal reducing the electronic credit ledger, records an idempotent
/// <c>itc_reversals</c> row, and routes it to GSTR-3B Table 4(B)(1)/4(B)(2)/4(D)(1). The reclaim path is ECRS-capped;
/// nothing auto-posts; the reversal never pollutes the 3.1 outward / 4(A) ITC sums.
/// </summary>
public sealed class GstReversalTests
{
    private const string GstinMe = "27AAPFU0939F1ZV";
    private const string GstinSupplierA = "24AAACC1206D1ZM";
    private const string GstinSupplierB = "29AAAAA0000A1Z5";
    private static readonly DateOnly FyStart = new(2025, 4, 1);
    private static readonly DateOnly From = new(2025, 4, 1);
    private static readonly DateOnly To = new(2026, 3, 31);
    private static readonly DateOnly D = new(2025, 4, 15);
    private static readonly DateOnly D9 = new(2025, 9, 15);
    private static readonly DateOnly D10 = new(2025, 10, 15);
    private static readonly DateTimeOffset Imported = new(2025, 11, 15, 9, 0, 0, TimeSpan.FromHours(5.5));

    // ---- fixture ----

    private static Company NewCompany(out Domain.Ledger supplierA, out Domain.Ledger bank)
    {
        var c = CompanyFactory.CreateSeeded("Reversal Co", FyStart);
        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = "27", Gstin = GstinMe, RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart, Periodicity = GstReturnPeriodicity.Monthly,
        });
        supplierA = AddCreditor(c, "Supplier A", GstinSupplierA, "24");
        bank = new Domain.Ledger(Guid.NewGuid(), "Bank", c.FindGroupByName("Bank Accounts")!.Id, Money.Zero, true);
        c.AddLedger(bank);
        return c;
    }

    private static Domain.Ledger AddCreditor(Company c, string name, string gstin, string state)
    {
        var l = new Domain.Ledger(Guid.NewGuid(), name, c.FindGroupByName("Sundry Creditors")!.Id, Money.Zero,
            openingIsDebit: false, maintainBillByBill: true)
        {
            PartyGst = new PartyGstDetails { RegistrationType = GstRegistrationType.Regular, Gstin = gstin, StateCode = state },
        };
        c.AddLedger(l);
        return l;
    }

    private static Domain.Ledger AddLedger(Company c, string name, string group, bool dr)
    {
        var l = new Domain.Ledger(Guid.NewGuid(), name, c.FindGroupByName(group)!.Id, Money.Zero, dr);
        c.AddLedger(l);
        return l;
    }

    /// <summary>A Purchase-Accounts ledger tagged §17(5)-blocked (motor vehicles) on its GST block.</summary>
    private static Domain.Ledger BlockedPurchaseLedger(Company c, string name)
    {
        var l = new Domain.Ledger(Guid.NewGuid(), name, c.FindGroupByName("Purchase Accounts")!.Id, Money.Zero, openingIsDebit: true)
        {
            SalesPurchaseGst = new StockItemGstDetails
            {
                HsnSac = "8703", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800,
                ItcEligibility = ItcEligibility.BlockedSection17_5, BlockedCreditCategory = BlockedCreditCategory.MotorVehicles,
            },
        };
        c.AddLedger(l);
        return l;
    }

    /// <summary>Posts an inter-state Purchase for <paramref name="taxable"/> @ 18% (IGST) from <paramref name="supplier"/>,
    /// carrying a bill-wise New-Ref allocation. Returns the posted voucher id (its ITC lands in Input IGST).</summary>
    private static Guid PostInterPurchase(Company c, Domain.Ledger supplier, decimal taxable, string billRef, DateOnly date,
        Domain.Ledger? purchaseLedger = null)
    {
        var gst = new GstService(c);
        var purchases = purchaseLedger ?? c.FindLedgerByName("Purchases") ?? AddLedger(c, "Purchases", "Purchase Accounts", true);
        var tax = gst.ComputeInvoiceTax(new[] { new GstService.TaxableLine(new Money(taxable), 1800) },
            interState: true, GstTaxDirection.Input);
        var credit = new Money(taxable + tax.TotalTax.Amount);
        var lines = new List<EntryLine>
        {
            new(purchases.Id, new Money(taxable), DrCr.Debit),
            new(supplier.Id, credit, DrCr.Credit, billAllocations: new[] { new BillAllocation(BillRefType.NewRef, billRef, credit) }),
        };
        lines.AddRange(tax.TaxLines);
        return new LedgerService(c).Post(new Voucher(Guid.NewGuid(),
            c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase).Id, date, lines, partyId: supplier.Id)).Id;
    }

    /// <summary>Posts an intra-state (bank-funded) Purchase for <paramref name="taxable"/> @ 18% (CGST+SGST) so Input CGST
    /// and Input SGST both accrue. Returns the posted voucher id.</summary>
    private static Guid PostIntraPurchase(Company c, Domain.Ledger bank, decimal taxable, DateOnly date)
    {
        var gst = new GstService(c);
        var purchases = c.FindLedgerByName("Purchases") ?? AddLedger(c, "Purchases", "Purchase Accounts", true);
        var tax = gst.ComputeInvoiceTax(new[] { new GstService.TaxableLine(new Money(taxable), 1800) },
            interState: false, GstTaxDirection.Input);
        var total = taxable + tax.TotalTax.Amount;
        var lines = new List<EntryLine>
        {
            new(purchases.Id, new Money(taxable), DrCr.Debit),
            new(bank.Id, new Money(total), DrCr.Credit),
        };
        lines.AddRange(tax.TaxLines);
        return new LedgerService(c).Post(new Voucher(Guid.NewGuid(),
            c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase).Id, date, lines)).Id;
    }

    private static Gstr2bLine Line(string gstin, string docNo, decimal taxable, decimal igst, DateOnly date,
        Gstr2bDocType type = Gstr2bDocType.B2b) =>
        new(Guid.NewGuid(), gstin, null, type, docNo, Gstr2bReconciler.NormaliseDocNo(docNo), date, "27",
            (long)(taxable * 100), (long)(igst * 100), 0, 0, 0, true, null, false);

    private static Gstr2bSnapshot Snapshot(params Gstr2bLine[] lines) =>
        new(Guid.NewGuid(), GstStatementType.Gstr2b, "2025-10", GstinMe, new DateOnly(2025, 11, 14),
            "HASH", Imported, 0, 0, 0, 0, lines);

    private static string Fingerprint(Company c) =>
        string.Join("|", c.Vouchers
            .OrderBy(v => v.Id)
            .SelectMany(v => v.Lines.Select(l =>
                $"{v.Id}:{l.LedgerId}:{l.Amount}:{l.Side}:{(l.Gst is { } g ? $"{g.TaxHead}/{g.Adjustment}" : "-")}")));

    private static Money InputClosing(Company c, GstTaxHead head, DateOnly asOf)
    {
        var l = new GstService(c).FindTaxLedger(head, GstTaxDirection.Input)!;
        return new Money(LedgerBalances.SignedClosing(c, l, asOf));
    }

    /// <summary>Σ the posted Cr (credit) legs on a voucher that hit the Input {head} ledger, in rupees.</summary>
    private static decimal CrInput(Company c, Voucher v, GstTaxHead head)
    {
        var id = new GstService(c).FindTaxLedger(head, GstTaxDirection.Input)!.Id;
        return v.Lines.Where(l => l.LedgerId == id && l.Side == DrCr.Credit).Sum(l => l.Amount.Amount);
    }

    /// <summary>Σ the posted Dr (debit) legs on a voucher that hit the Input {head} ledger, in rupees.</summary>
    private static decimal DrInput(Company c, Voucher v, GstTaxHead head)
    {
        var id = new GstService(c).FindTaxLedger(head, GstTaxDirection.Input)!.Id;
        return v.Lines.Where(l => l.LedgerId == id && l.Side == DrCr.Debit).Sum(l => l.Amount.Amount);
    }

    /// <summary>An intra-state (CGST+SGST, optionally with Compensation Cess) 2B line — the cess-bearing case the
    /// per-head-split reversal bug hid (the S6b cess ring-fence, ER-2).</summary>
    private static Gstr2bLine IntraLine(string gstin, string docNo, decimal taxable, long cgstPaisa, long sgstPaisa,
        long cessPaisa, DateOnly date, Gstr2bDocType type = Gstr2bDocType.B2b) =>
        new(Guid.NewGuid(), gstin, null, type, docNo, Gstr2bReconciler.NormaliseDocNo(docNo), date, "27",
            (long)(taxable * 100), 0, cgstPaisa, sgstPaisa, cessPaisa, true, null, false);

    /// <summary>Posts a §17(5)-blocked motor-vehicle inter-state Purchase carrying BOTH forward Input IGST and forward
    /// Input Compensation Cess (a motor vehicle is always cess-bearing), so the whole voucher's ITC is blocked. Returns
    /// the posted voucher id (its forward Input tax lands in Input IGST + Input Cess).</summary>
    private static Guid PostBlockedMotorVehicleWithCess(Company c, Domain.Ledger supplier, DateOnly date,
        decimal taxable, long igstPaisa, long cessPaisa)
    {
        var gst = new GstService(c);
        gst.EnsureCessLedgers();
        var blocked = BlockedPurchaseLedger(c, "Motor Car Purchase");
        var inputIgst = gst.FindTaxLedger(GstTaxHead.Integrated, GstTaxDirection.Input)!;
        var inputCess = gst.FindTaxLedger(GstTaxHead.Cess, GstTaxDirection.Input)!;
        var igst = new Money(igstPaisa / 100m);
        var cess = new Money(cessPaisa / 100m);
        var total = new Money(taxable + igst.Amount + cess.Amount);
        var lines = new List<EntryLine>
        {
            new(blocked.Id, new Money(taxable), DrCr.Debit),
            new(inputIgst.Id, igst, DrCr.Debit, gst: new GstLineTax(GstTaxHead.Integrated, 2800, new Money(taxable))),
            new(inputCess.Id, cess, DrCr.Debit, gst: new GstLineTax(GstTaxHead.Cess, 0, new Money(taxable))),
            new(supplier.Id, total, DrCr.Credit,
                billAllocations: new[] { new BillAllocation(BillRefType.NewRef, "CAR-CESS", total) }),
        };
        return new LedgerService(c).Post(new Voucher(Guid.NewGuid(),
            c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase).Id, date, lines, partyId: supplier.Id)).Id;
    }

    // ================================================================ Rule 42 (D1 + D2)  [test 15]

    [Fact]
    public void Rule42_reverses_D1_plus_D2_per_head_to_table_4B1()
    {
        var c = NewCompany(out _, out var bank);
        var svc = new Rev(c);
        PostIntraPurchase(c, bank, 100000m, D);   // Input CGST 9000 + SGST 9000

        // C2 = ₹1,000 per head ; E/F = 0.4  ⇒  D1 = 0.4×1000 = ₹400 ; D2 = 5%×1000 = ₹50 ; reversal = ₹450 per head.
        var basis = new Rev.Rule42Basis(new Rev.ReversalAmount(100_000, 100_000, 0, 0), 40_000_000, 100_000_000);
        var row = svc.PostRule42("2025-04", basis, D)!;

        Assert.Equal(ItcReversalRule.Rule42, row.Rule);
        Assert.Equal(Table4bBucket.Table4B1, row.Table4bBucket);   // non-reclaimable (§11.5)
        Assert.Equal(45_000, row.CgstPaisa);
        Assert.Equal(45_000, row.SgstPaisa);
        Assert.Equal(0, row.IgstPaisa);
        Assert.Equal(80_000, row.D1BasisPaisa);   // ΣD1 = 40000 + 40000
        Assert.Equal(10_000, row.D2BasisPaisa);   // ΣD2 = 5000 + 5000

        var v = c.FindVoucher(row.ReversalVoucherId)!;
        Assert.True(VoucherValidator.IsBalanced(v));

        var g3b = Gstr3b.Build(c, From, To);
        Assert.Equal(450m, g3b.ItcReversed4B1Cgst.Amount);
        Assert.Equal(450m, g3b.ItcReversed4B1Sgst.Amount);
        // The credit ledger CGST pool is reduced by the reversal (9000 accrued − 450 reversed).
        Assert.Equal(8550m, InputClosing(c, GstTaxHead.Central, To).Amount);
        Assert.Equal(450m, ElectronicLedgersView.Build(c, From, To).CreditReversedCgst.Amount);
    }

    [Fact]
    public void Rule42_annual_trueup_posts_only_the_delta_not_a_stacked_second_reversal()
    {
        var c = NewCompany(out _, out var bank);
        var svc = new Rev(c);
        PostIntraPurchase(c, bank, 100000m, D);

        // Monthly on the interim E/F = 0.4 ⇒ ₹450 CGST (D1 400 + D2 50).
        var monthly = new Rev.Rule42Basis(new Rev.ReversalAmount(100_000, 0, 0, 0), 40_000_000, 100_000_000);
        var m = svc.PostRule42("2025-04", monthly, D)!;
        Assert.Equal(45_000, m.CgstPaisa);

        // Annual true-up recomputed on the full-year E/F = 0.55 ⇒ full-year ₹600 (D1 550 + D2 50); delta = 600 − 450 = ₹150.
        var fullYear = new Rev.Rule42Basis(new Rev.ReversalAmount(100_000, 0, 0, 0), 55_000_000, 100_000_000);
        var trueUp = svc.PostRule42AnnualTrueUp("2025-26", fullYear, D)!;

        Assert.Equal(15_000, trueUp.CgstPaisa);            // the DELTA, not another full ₹600
        Assert.Equal("2025-26", trueUp.Period);
        Assert.Equal(60_000, m.CgstPaisa + trueUp.CgstPaisa); // monthly + true-up == full-year reversal
        Assert.Equal(2, c.ItcReversals.Count(r => r.Rule == ItcReversalRule.Rule42));
    }

    // ============================================ FIX 2 — Rule 42 annual true-up FY-scoping (multi-FY)

    [Fact]
    public void Rule42_annual_trueup_scopes_the_monthly_accumulation_to_the_fy_and_does_not_zero_out_a_later_year()
    {
        var c = NewCompany(out _, out var bank);
        var svc = new Rev(c);
        PostIntraPurchase(c, bank, 100000m, D);

        // FY 2025-26 (year 1): a monthly Rule 42 (E/F 0.4 ⇒ ₹450) then its annual true-up on the SAME E/F ⇒ zero delta.
        var y1 = new Rev.Rule42Basis(new Rev.ReversalAmount(100_000, 0, 0, 0), 40_000_000, 100_000_000);
        svc.PostRule42("2025-04", y1, D);
        Assert.Null(svc.PostRule42AnnualTrueUp("2025-26", y1, D9)); // year 1 already fully reversed ⇒ null

        // FY 2026-27 (year 2): a monthly Rule 42 (interim E/F 0.4 ⇒ ₹450).
        var y2monthly = new Rev.Rule42Basis(new Rev.ReversalAmount(100_000, 0, 0, 0), 40_000_000, 100_000_000);
        var m2 = svc.PostRule42("2026-04", y2monthly, new DateOnly(2026, 4, 15))!;
        Assert.Equal(45_000, m2.CgstPaisa);

        // FY 2026-27 true-up on the full-year E/F 0.55 ⇒ ₹600; delta = 600 − 450 = ₹150. The BUG summed EVERY Rule-42
        // month across ALL FYs (2025-04 ₹450 + 2026-04 ₹450 = ₹900), so 600 − 900 floored to 0 ⇒ null (silent
        // under-reversal). FY-scoping subtracts ONLY FY-2026-27's own 2026-04 ₹450.
        var y2full = new Rev.Rule42Basis(new Rev.ReversalAmount(100_000, 0, 0, 0), 55_000_000, 100_000_000);
        var trueUp = svc.PostRule42AnnualTrueUp("2026-27", y2full, new DateOnly(2026, 9, 15));

        Assert.NotNull(trueUp);
        Assert.Equal(15_000, trueUp!.CgstPaisa);       // the correct FY-2026-27 delta, NOT zero
        Assert.Equal("2026-27", trueUp.Period);
        Assert.Equal(Table4bBucket.Table4B1, trueUp.Table4bBucket);
    }

    // ============================================ FIX 3 — Rule 42 true-up RE-CREDITS an over-reversal (Rule 42(2)(b))

    [Fact]
    public void Rule42_annual_trueup_recredits_an_over_reversal_instead_of_forfeiting_it()
    {
        var c = NewCompany(out _, out var bank);
        var svc = new Rev(c);
        PostIntraPurchase(c, bank, 100000m, D);   // Input CGST 9000

        // Monthly OVER-reverses on the interim E/F 0.55 ⇒ ₹600 (D1 550 + D2 50).
        var monthly = new Rev.Rule42Basis(new Rev.ReversalAmount(100_000, 0, 0, 0), 55_000_000, 100_000_000);
        var m = svc.PostRule42("2025-04", monthly, D)!;
        Assert.Equal(60_000, m.CgstPaisa);
        var afterMonthly = InputClosing(c, GstTaxHead.Central, To).Amount;   // 9000 − 600 = ₹8400

        // Full-year recompute on E/F 0.40 ⇒ ₹450: the year OVER-reversed by ₹150, which Rule 42(2)(b) RE-CREDITS.
        // The BUG did Math.Max(0, 450 − 600) = 0 ⇒ null: the ₹150 excess ITC was permanently forfeited.
        var fullYear = new Rev.Rule42Basis(new Rev.ReversalAmount(100_000, 0, 0, 0), 40_000_000, 100_000_000);
        var trueUp = svc.PostRule42AnnualTrueUp("2025-26", fullYear, new DateOnly(2025, 9, 15));

        Assert.NotNull(trueUp);
        Assert.Equal(15_000, trueUp!.CgstPaisa);                    // the ₹150 excess (magnitude), re-credited
        Assert.Equal(Table4bBucket.Table4D1, trueUp.Table4bBucket); // routed into net ITC (4(D)(1) / 4(A)(5))

        var v = c.FindVoucher(trueUp.ReversalVoucherId)!;
        Assert.True(VoucherValidator.IsBalanced(v));
        Assert.Equal(150m, DrInput(c, v, GstTaxHead.Central));       // Dr Input CGST / Cr cost — restores the credit pool
        Assert.Equal(afterMonthly + 150m, InputClosing(c, GstTaxHead.Central, To).Amount); // credit ledger restored
        Assert.Equal(150m, Gstr3b.Build(c, From, To).ItcReclaimed4D1Cgst.Amount);
    }

    // ============================================ FIX 1 — per-head candidate reversal (cess ring-fence, no bleed)

    [Fact]
    public void Ims_accepted_credit_note_with_cess_reverses_gst_heads_head_for_head_no_cess_bleed()
    {
        var c = NewCompany(out var a, out var bank);
        var svc = new Rev(c);
        PostIntraPurchase(c, bank, 100000m, D);   // accrue Input CGST/SGST so the CN reversal has credit to reduce
        var cn = IntraLine(GstinSupplierA, "CN-CESS", 100000m, 9000, 9000, 2000, D, Gstr2bDocType.CreditNote);
        c.AddGstr2bSnapshot(Snapshot(cn));   // no IMS action ⇒ deemed-accept ⇒ full forward-tax reversal

        var cand = ItcGateView.Build(c, c.Gstr2bSnapshots[0], From, To).ReversalCandidates
            .Single(x => x.Reason == ItcReversalReason.ImsAcceptedCreditNote);
        var row = svc.PostFromCandidate(cand, "2025-10", D)!;

        // Head-for-head — NOT the buggy 81/81 with a spurious ₹18 cess carved out of the GST pool.
        Assert.Equal(9000, row.CgstPaisa);
        Assert.Equal(9000, row.SgstPaisa);
        Assert.Equal(0, row.IgstPaisa);

        var v = c.FindVoucher(row.ReversalVoucherId)!;
        Assert.True(VoucherValidator.IsBalanced(v));
        Assert.Equal(90m, CrInput(c, v, GstTaxHead.Central));   // Cr Input CGST ₹90
        Assert.Equal(90m, CrInput(c, v, GstTaxHead.State));     // Cr Input SGST ₹90
        // The cess leg is the source's own ring-fenced cess (₹20) — never a value carved out of the GST pool.
        Assert.Equal(2000, row.CessPaisa);
        Assert.Equal(20m, CrInput(c, v, GstTaxHead.Cess));
    }

    [Fact]
    public void Section17_5_motor_vehicle_with_cess_reverses_igst_head_for_head_no_cess_bleed()
    {
        var c = NewCompany(out var a, out _);
        var svc = new Rev(c);
        var blocked = PostBlockedMotorVehicleWithCess(c, a, D, 10000m, 280_000, 200_000); // §17(5): IGST ₹2800 + Cess ₹2000
        var snap = Snapshot(Line(GstinSupplierA, "CAR-CESS", 10000m, 2800m, D));
        c.AddGstr2bSnapshot(snap);

        var cand = ItcGateView.Build(c, snap, From, To).ReversalCandidates
            .Single(x => x.Reason == ItcReversalReason.Section17_5Blocked);
        var row = svc.PostFromCandidate(cand, "2025-04", D)!;

        Assert.Equal(ItcReversalRule.Section17_5, row.Rule);
        // IGST reversed head-for-head = ₹2800 — NOT the buggy ₹1633.33 diluted by cess.
        Assert.Equal(280_000, row.IgstPaisa);
        Assert.Equal(blocked, row.SourceVoucherId);

        var v = c.FindVoucher(row.ReversalVoucherId)!;
        Assert.True(VoucherValidator.IsBalanced(v));
        Assert.Equal(2800m, CrInput(c, v, GstTaxHead.Integrated));
        // The cess leg is the source's own ring-fenced blocked cess (₹2000) — never carved from the GST pool (no ₹1166.67).
        Assert.Equal(200_000, row.CessPaisa);
        Assert.Equal(2000m, CrInput(c, v, GstTaxHead.Cess));
    }

    // ================================================================ Rule 43 (60-month tranche)  [test 16]

    [Fact]
    public void Rule43_reverses_the_monthly_tranche_over_sixty_months_and_each_month_posts_once()
    {
        var c = NewCompany(out _, out var bank);
        var svc = new Rev(c);
        var capitalVid = PostIntraPurchase(c, bank, 100000m, D);   // Input CGST 9000 (ample for the small tranches)

        // Tc = ₹60,000 capital-goods common credit ⇒ Tm = Tc/60 = ₹1,000 ; Te = (E/F)×Tm = 0.4×1000 = ₹400/month.
        var basis = new Rev.Rule43Basis(new Rev.ReversalAmount(6_000_000, 0, 0, 0), 40_000_000, 100_000_000);

        var m1 = svc.PostRule43("2025-04", capitalVid, basis, D)!;
        Assert.Equal(40_000, m1.CgstPaisa);
        Assert.Equal(Table4bBucket.Table4B1, m1.Table4bBucket);

        // Re-running the SAME month is idempotent: the existing row is returned, no second voucher/row (UNIQUE key).
        var vouchersAfterM1 = c.Vouchers.Count;
        var again = svc.PostRule43("2025-04", capitalVid, basis, D)!;
        Assert.Equal(m1.Id, again.Id);
        Assert.Equal(vouchersAfterM1, c.Vouchers.Count);
        Assert.Single(c.ItcReversals, r => r.Rule == ItcReversalRule.Rule43 && r.Period == "2025-04");

        // The next month's tranche posts once more (the 60-month schedule advances one step per period).
        var m2 = svc.PostRule43("2025-05", capitalVid, basis, D)!;
        Assert.Equal(40_000, m2.CgstPaisa);
        Assert.Equal(2, c.ItcReversals.Count(r => r.Rule == ItcReversalRule.Rule43));
    }

    // ================================================================ Rule 37 (180-day) + reclaim + ECRS  [test 13]

    [Fact]
    public void Rule37_reverses_to_4B2_reduces_the_credit_ledger_and_reclaims_to_4D1()
    {
        var c = NewCompany(out var supplier, out _);
        var svc = new Rev(c);
        var vid = PostInterPurchase(c, supplier, 100000m, "INV-H", D);   // fixture (H): ITC ₹18,000 IGST

        // A >180-day-unpaid invoice ⇒ reverse the full ITC availed to Table 4(B)(2) (reclaimable, §11.5).
        var row = svc.PostRule37(vid, "2025-04", D);
        Assert.Equal(ItcReversalRule.Rule37, row.Rule);
        Assert.Equal(Table4bBucket.Table4B2, row.Table4bBucket);
        Assert.Equal(1_800_000, row.IgstPaisa);
        Assert.Equal(vid, row.SourceVoucherId);
        Assert.True(VoucherValidator.IsBalanced(c.FindVoucher(row.ReversalVoucherId)!));
        Assert.Equal(0m, InputClosing(c, GstTaxHead.Integrated, To).Amount);           // 18000 accrued − 18000 reversed
        Assert.Equal(18000m, Gstr3b.Build(c, From, To).ItcReversed4B2Igst.Amount);

        // Reclaim on later payment: re-avail the ITC → Table 4(D)(1); the credit ledger is restored.
        var reclaim = svc.Reclaim(row.Id, "2025-09", D9);
        Assert.Equal(1_800_000, reclaim.IgstPaisa);
        Assert.Equal(row.Id, reclaim.ReclaimOfId);
        Assert.Equal(Table4bBucket.Table4D1, reclaim.Table4bBucket);
        Assert.True(VoucherValidator.IsBalanced(c.FindVoucher(reclaim.ReversalVoucherId)!));
        Assert.Equal(18000m, InputClosing(c, GstTaxHead.Integrated, To).Amount);       // restored
        Assert.Equal(18000m, Gstr3b.Build(c, From, To).ItcReclaimed4D1Igst.Amount);

        // The reclaim posts ONCE: a second reclaim of the same reversal is refused.
        Assert.Throws<InvalidOperationException>(() => svc.Reclaim(row.Id, "2025-10", D10));
    }

    // ================================================================ Rule 37A (supplier non-filing)  [test 14]

    [Fact]
    public void Rule37A_reverses_to_4B2_and_reclaims_on_supplier_filing()
    {
        var c = NewCompany(out var supplier, out _);
        var svc = new Rev(c);
        var vid = PostInterPurchase(c, supplier, 50000m, "INV-37A", D);   // ITC ₹9,000 IGST

        // Supplier had not filed GSTR-3B by 30-Sep ⇒ recipient reverses by 30-Nov to Table 4(B)(2).
        var row = svc.PostRule37A(vid, "2025-11", new DateOnly(2025, 11, 30));
        Assert.Equal(ItcReversalRule.Rule37A, row.Rule);
        Assert.Equal(Table4bBucket.Table4B2, row.Table4bBucket);
        Assert.Equal(900_000, row.IgstPaisa);

        // On the supplier subsequently filing, the recipient re-avails → Table 4(D)(1).
        var reclaim = svc.Reclaim(row.Id, "2026-01", new DateOnly(2026, 1, 20));
        Assert.Equal(900_000, reclaim.IgstPaisa);
        Assert.Equal(Table4bBucket.Table4D1, reclaim.Table4bBucket);
        Assert.Equal(9000m, Gstr3b.Build(c, From, To).ItcReclaimed4D1Igst.Amount);
    }

    // ================================================================ ECRS reclaim cap  [test 13 tail / §11.7]

    [Fact]
    public void Ecrs_rejects_a_reclaim_that_exceeds_the_tracked_reversal_balance()
    {
        var c = NewCompany(out var supplier, out _);
        var svc = new Rev(c);
        var vid = PostInterPurchase(c, supplier, 100000m, "INV-E", D);   // ITC ₹18,000 IGST
        var row = svc.PostRule37(vid, "2025-04", D);                     // tracked reversal balance IGST = ₹18,000

        // A reclaim of ₹20,000 exceeds the ₹18,000 tracked balance ⇒ the portal hard-validation refuses it (ECRS).
        Assert.Throws<InvalidOperationException>(() =>
            svc.Reclaim(row.Id, "2025-09", D9, new Rev.ReversalAmount(0, 0, 2_000_000, 0)));

        // A reclaim within the balance succeeds and drives the tracked balance to zero.
        var ok = svc.Reclaim(row.Id, "2025-09", D9, new Rev.ReversalAmount(0, 0, 1_800_000, 0));
        Assert.Equal(1_800_000, ok.IgstPaisa);
        Assert.Equal(0, svc.OutstandingReversalBalance().IgstPaisa);
    }

    // ================================================================ §17(5) candidate → 4B1 (S6 surfaces, S7b posts)  [test 17]

    [Fact]
    public void Section17_5_candidate_is_surfaced_by_S6_and_posted_by_S7b_to_4B1()
    {
        var c = NewCompany(out var a, out _);
        var svc = new Rev(c);
        var blockedLedger = BlockedPurchaseLedger(c, "Motor Car Purchase");
        var blocked = PostInterPurchase(c, a, 10000m, "CAR-1", D, blockedLedger);   // §17(5) motor vehicle, ITC ₹1,800
        var snap = Snapshot(Line(GstinSupplierA, "CAR-1", 10000m, 1800m, D));
        c.AddGstr2bSnapshot(snap);

        // S6 SURFACES the candidate but posts nothing (ER-14, structural) — the ledger is byte-identical after Build.
        var beforeGate = Fingerprint(c);
        var view = ItcGateView.Build(c, snap, From, To);
        Assert.Equal(beforeGate, Fingerprint(c));
        var cand = view.ReversalCandidates.Single(x => x.Reason == ItcReversalReason.Section17_5Blocked);

        // S7b is the SOLE poster: it consumes the candidate and posts the reversal to Table 4(B)(1) (non-reclaimable).
        var row = svc.PostFromCandidate(cand, "2025-04", D)!;
        Assert.Equal(ItcReversalRule.Section17_5, row.Rule);
        Assert.Equal(Table4bBucket.Table4B1, row.Table4bBucket);
        Assert.Equal(180_000, row.IgstPaisa);
        Assert.Equal(blocked, row.SourceVoucherId);
        Assert.True(VoucherValidator.IsBalanced(c.FindVoucher(row.ReversalVoucherId)!));
        Assert.Equal(1800m, Gstr3b.Build(c, From, To).ItcReversed4B1Igst.Amount);
        Assert.Equal(0m, InputClosing(c, GstTaxHead.Integrated, To).Amount);   // the availed blocked ITC reversed out
    }

    // ================================================================ §16(2)(aa) not-in-portal is a DEFERRAL  [test 18]

    [Fact]
    public void Section16_2aa_not_in_portal_candidate_is_a_deferral_and_posts_nothing()
    {
        var c = NewCompany(out var a, out _);
        var svc = new Rev(c);
        PostInterPurchase(c, a, 10000m, "INV-777", D);   // booked, but no matching 2B line ⇒ §16(2)(aa) hold
        var snap = Snapshot(Line(GstinSupplierB, "OTHER-1", 3000m, 540m, D)); // an unrelated supplier's line
        c.AddGstr2bSnapshot(snap);

        var view = ItcGateView.Build(c, snap, From, To);
        var cand = view.ReversalCandidates.Single(x => x.Reason == ItcReversalReason.Section16_2aaNotInPortal);

        // A §16(2)(aa) hold is a DEFERRAL, not a reversal — confirming it posts NOTHING (it only escalates to Rule 37A
        // at the 30-Nov cut-off). The ledger + the itc_reversals table are byte-identical.
        var before = Fingerprint(c);
        var beforeRows = c.ItcReversals.Count;
        var result = svc.PostFromCandidate(cand, "2025-04", D);
        Assert.Null(result);
        Assert.Equal(before, Fingerprint(c));
        Assert.Equal(beforeRows, c.ItcReversals.Count);
    }

    // ================================================================ IMS-accepted credit note  [test 19]

    [Fact]
    public void Ims_accepted_credit_note_reverses_the_declared_amount_full_or_none()
    {
        // (a) partial declared reversal ₹300.
        {
            var c = NewCompany(out var a, out _);
            var svc = new Rev(c);
            PostInterPurchase(c, a, 100000m, "BASE", D);   // accrue Input IGST 18000 so the CN reversal has credit to reduce
            var cn = Line(GstinSupplierA, "CN-5", 4000m, 720m, D, Gstr2bDocType.CreditNote);
            c.AddGstr2bSnapshot(Snapshot(cn));
            ImsService.SetAction(c, cn.Id, ImsStatus.Accepted, remarks: "partial", declaredReversalPaisa: 30_000);

            var cand = ItcGateView.Build(c, c.Gstr2bSnapshots[0], From, To).ReversalCandidates
                .Single(x => x.Reason == ItcReversalReason.ImsAcceptedCreditNote);
            var row = svc.PostFromCandidate(cand, "2025-10", D)!;
            Assert.Equal(ItcReversalRule.CreditNote, row.Rule);
            Assert.Equal(Table4bBucket.Table4B1, row.Table4bBucket);
            Assert.Equal(30_000, row.IgstPaisa);   // the DECLARED ₹300, not the full ₹720 forward tax
            Assert.Equal(cn.Id, row.SourceLineId);
            Assert.True(VoucherValidator.IsBalanced(c.FindVoucher(row.ReversalVoucherId)!));
        }

        // (b) a "no reversal required" declaration reverses nothing.
        {
            var c = NewCompany(out var a, out _);
            var svc = new Rev(c);
            PostInterPurchase(c, a, 100000m, "BASE", D);
            var cn = Line(GstinSupplierA, "CN-8", 4000m, 720m, D, Gstr2bDocType.CreditNote);
            c.AddGstr2bSnapshot(Snapshot(cn));
            ImsService.SetAction(c, cn.Id, ImsStatus.Accepted, noReversalDeclared: true);

            var cand = ItcGateView.Build(c, c.Gstr2bSnapshots[0], From, To).ReversalCandidates
                .Single(x => x.Reason == ItcReversalReason.ImsAcceptedCreditNote);
            Assert.Null(svc.PostFromCandidate(cand, "2025-10", D));
            Assert.Empty(c.ItcReversals);
        }

        // (c) a deemed-accepted CN (no IMS action) reverses the FULL forward tax ₹720.
        {
            var c = NewCompany(out var a, out _);
            var svc = new Rev(c);
            PostInterPurchase(c, a, 100000m, "BASE", D);
            var cn = Line(GstinSupplierA, "CN-6", 4000m, 720m, D, Gstr2bDocType.CreditNote);
            c.AddGstr2bSnapshot(Snapshot(cn));   // no SetAction ⇒ deemed-accept

            var cand = ItcGateView.Build(c, c.Gstr2bSnapshots[0], From, To).ReversalCandidates
                .Single(x => x.Reason == ItcReversalReason.ImsAcceptedCreditNote);
            var row = svc.PostFromCandidate(cand, "2025-10", D)!;
            Assert.Equal(72_000, row.IgstPaisa);
        }
    }

    // ================================================================ Idempotency / no-double-post  [test 22]

    [Fact]
    public void A_period_reversal_re_run_does_not_double_post()
    {
        var c = NewCompany(out var supplier, out _);
        var svc = new Rev(c);
        var vid = PostInterPurchase(c, supplier, 100000m, "INV-I", D);

        var r1 = svc.PostRule37(vid, "2025-04", D);
        var vouchersAfter1 = c.Vouchers.Count;

        // Re-running the SAME (rule, period, source) is idempotent — the UNIQUE key returns the existing row, no duplicate.
        var r2 = svc.PostRule37(vid, "2025-04", D);
        Assert.Equal(r1.Id, r2.Id);
        Assert.Equal(vouchersAfter1, c.Vouchers.Count);
        Assert.Single(c.ItcReversals, r => r.Rule == ItcReversalRule.Rule37 && r.SourceVoucherId == vid);
    }

    // ================================================================ No pollution of 3.1 / 4(A)  [test 21]

    [Fact]
    public void A_reversal_does_not_pollute_the_3_1_outward_or_4A_itc_sums()
    {
        var c = NewCompany(out var supplier, out _);
        var svc = new Rev(c);
        var vid = PostInterPurchase(c, supplier, 100000m, "INV-P", D);   // 4(A) ITC IGST 18000

        var before = Gstr3b.Build(c, From, To);
        svc.PostRule37(vid, "2025-04", D);   // reverse 18000 via a stat-adjustment Journal
        var after = Gstr3b.Build(c, From, To);

        // The Journal-based reversal is excluded from PostedGstVouchers ⇒ 3.1 outward + 4(A) ITC are byte-identical.
        Assert.Equal(before.OutwardIgst, after.OutwardIgst);
        Assert.Equal(before.ItcIgst, after.ItcIgst);
        Assert.Equal(before.ItcCgst, after.ItcCgst);
        Assert.Equal(before.TaxableOutwardValue, after.TaxableOutwardValue);
        Assert.Equal(before.TotalItc, after.TotalItc);
        // …but Table 4(B)(2) now reflects the reversal (net eligible ITC = 4(A) − 4(B)).
        Assert.Equal(18000m, after.ItcReversed4B2Igst.Amount);
    }

    // ================================================================ POSITIVE posting proof (ER-14 deliverable)

    [Fact]
    public void The_reversal_service_posts_a_balanced_paisa_conserved_reversal_and_records_the_audit_row()
    {
        var c = NewCompany(out _, out var bank);
        var svc = new Rev(c);
        var vid = PostIntraPurchase(c, bank, 100000m, D);   // Input CGST 9000 + SGST 9000

        var row = svc.PostRule37(vid, "2025-04", D);        // reverse the full forward ITC per head
        var v = c.FindVoucher(row.ReversalVoucherId)!;

        // Balanced (Σ Dr == Σ Cr through the single guarded entry-point) and paisa-conserved (Dr cost == Σ Cr Input).
        Assert.True(VoucherValidator.IsBalanced(v));
        var dr = v.Lines.Where(l => l.Side == DrCr.Debit).Sum(l => l.Amount.Amount);
        var cr = v.Lines.Where(l => l.Side == DrCr.Credit).Sum(l => l.Amount.Amount);
        Assert.Equal(dr, cr);
        Assert.Equal(18000m, dr);
        Assert.Equal(900_000, row.CgstPaisa);
        Assert.Equal(900_000, row.SgstPaisa);
        Assert.Single(c.ItcReversals);

        // The reversed credit becomes a non-creditable cost (Dr ITC Reversal (Non-creditable)).
        var cost = c.FindLedgerByName(GstService.ItcReversalCostLedgerName)!;
        Assert.Equal(18000m, LedgerBalances.SignedClosing(c, cost, To));
    }

    // ================================================================ Table 4(B) bucket routing

    [Fact]
    public void BucketFor_routes_each_rule_to_its_table_4B_bucket()
    {
        Assert.Equal(Table4bBucket.Table4B2, Rev.BucketFor(ItcReversalRule.Rule37));
        Assert.Equal(Table4bBucket.Table4B2, Rev.BucketFor(ItcReversalRule.Rule37A));
        Assert.Equal(Table4bBucket.Table4B1, Rev.BucketFor(ItcReversalRule.Rule42));
        Assert.Equal(Table4bBucket.Table4B1, Rev.BucketFor(ItcReversalRule.Rule43));
        Assert.Equal(Table4bBucket.Table4B1, Rev.BucketFor(ItcReversalRule.Section17_5));
        Assert.Equal(Table4bBucket.Table4B1, Rev.BucketFor(ItcReversalRule.Ineligible));
        Assert.Equal(Table4bBucket.Table4B1, Rev.BucketFor(ItcReversalRule.CreditNote));
    }
}
