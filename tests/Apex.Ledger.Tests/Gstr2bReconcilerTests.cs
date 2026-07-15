using System.Reflection;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;

namespace Apex.Ledger.Tests;

/// <summary>
/// Phase-9 slice-6 <b>GSTR-2B reconciliation</b> gate (RQ-13; DP-13; DP-15; ER-14). The reconciler is pure + advisory:
/// it buckets each 2B line into Matched / PartialMismatch / InPortalOnly / InBooksOnly against the booked purchase
/// register, with a configurable tolerance + normalised match key, a deterministic 1-to-1 pass, and it EXCLUDES
/// RCM-tagged inward + composition from the register. It has no posting surface — a full import→reconcile cycle leaves
/// the ledger + every GSTR-3B figure byte-identical (ER-14, structural).
/// </summary>
public sealed class Gstr2bReconcilerTests
{
    private const string GstinMe = "27AAPFU0939F1ZV";
    private const string GstinSupplierA = "24AAACC1206D1ZM";
    private const string GstinSupplierB = "29AAAAA0000A1Z5";
    private static readonly DateOnly FyStart = new(2025, 4, 1);
    private static readonly DateOnly From = new(2025, 4, 1);
    private static readonly DateOnly To = new(2026, 3, 31);
    private static readonly DateOnly D1 = new(2025, 10, 10);
    private static readonly DateTimeOffset Imported = new(2025, 11, 15, 9, 0, 0, TimeSpan.FromHours(5.5));

    // ---- fixture ----

    private static Company BuildReconCompany(out Domain.Ledger supplierA, out Domain.Ledger supplierB,
        GstRegistrationType regType = GstRegistrationType.Regular)
    {
        var c = CompanyFactory.CreateSeeded("Recon Co", FyStart);
        var gst = new GstService(c);
        gst.EnableGst(new GstConfig
        {
            HomeStateCode = "27", Gstin = GstinMe, RegistrationType = regType,
            ApplicableFrom = FyStart, Periodicity = GstReturnPeriodicity.Monthly,
            CompositionSubType = regType == GstRegistrationType.Composition ? CompositionSubType.Trader : null,
        });

        supplierA = AddCreditor(c, "Supplier A", GstinSupplierA, "24");
        supplierB = AddCreditor(c, "Supplier B", GstinSupplierB, "29");
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

    /// <summary>Posts a purchase from <paramref name="supplier"/> for <paramref name="taxable"/> @ 18% inter-state
    /// (IGST), carrying the supplier's bill reference as a bill-wise New-Ref allocation. Returns the voucher id.</summary>
    private static Guid PostPurchase(Company c, Domain.Ledger supplier, decimal taxable, string billRef, DateOnly date)
    {
        var gst = new GstService(c);
        var purchases = c.FindLedgerByName("Purchases") ?? Add(c, "Purchases", "Purchase Accounts", true);
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

    /// <summary>Posts a purchase from <paramref name="supplier"/> for <paramref name="taxable"/> @ 18% inter-state with
    /// <b>no</b> bill-wise ref and no narration ⇒ the books entry has no recoverable supplier doc-no. Returns the id.</summary>
    private static Guid PostPurchaseNoRef(Company c, Domain.Ledger supplier, decimal taxable, DateOnly date)
    {
        var gst = new GstService(c);
        var purchases = c.FindLedgerByName("Purchases") ?? Add(c, "Purchases", "Purchase Accounts", true);
        var tax = gst.ComputeInvoiceTax(new[] { new GstService.TaxableLine(new Money(taxable), 1800) },
            interState: true, GstTaxDirection.Input);
        var credit = new Money(taxable + tax.TotalTax.Amount);
        var lines = new List<EntryLine>
        {
            new(purchases.Id, new Money(taxable), DrCr.Debit),
            new(supplier.Id, credit, DrCr.Credit),   // NO bill-wise ref ⇒ no recoverable supplier doc-no
        };
        lines.AddRange(tax.TaxLines);
        return new LedgerService(c).Post(new Voucher(Guid.NewGuid(),
            c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase).Id, date, lines, partyId: supplier.Id)).Id;
    }

    private static Domain.Ledger Add(Company c, string name, string group, bool dr)
    {
        var l = new Domain.Ledger(Guid.NewGuid(), name, c.FindGroupByName(group)!.Id, Money.Zero, dr);
        c.AddLedger(l);
        return l;
    }

    private static Gstr2bLine Line(string gstin, string docNo, decimal taxable, decimal igst, DateOnly date,
        Gstr2bDocType type = Gstr2bDocType.B2b) =>
        new(Guid.NewGuid(), gstin, null, type, docNo, Gstr2bReconciler.NormaliseDocNo(docNo), date, "27",
            (long)(taxable * 100), (long)(igst * 100), 0, 0, 0, true, null, false);

    private static Gstr2bSnapshot Snapshot(Company c, params Gstr2bLine[] lines) =>
        new(Guid.NewGuid(), GstStatementType.Gstr2b, "2025-10", GstinMe, new DateOnly(2025, 11, 14),
            "HASH", Imported, 0, 0, 0, 0, lines);

    // ---- tests ----

    [Fact]
    public void Reconcile_yields_all_four_buckets()
    {
        var c = BuildReconCompany(out var a, out var b);
        var p1 = PostPurchase(c, a, 10000m, "INV-001", D1);   // matches L1 exactly
        PostPurchase(c, b, 5000m, "BILL/22", D1);             // matches L2 keys, value differs by ₹1 (partial at tol 0)
        PostPurchase(c, a, 2000m, "INV-777", D1);             // no 2B line ⇒ InBooksOnly

        var snapshot = Snapshot(c,
            Line(GstinSupplierA, "INV001", 10000m, 1800m, D1),                     // Matched (normalised doc-no)
            Line(GstinSupplierB, "BILL22", 5001m, 900.18m, D1),                    // PartialMismatch (₹1 over)
            Line("06ZZZZZ0000Z1Z5", "XYZ-1", 3000m, 540m, D1));                    // InPortalOnly (supplier not in books)

        var r = Gstr2bReconciler.Reconcile(c, snapshot, From, To, ReconTolerance.Exact);

        var matched = Assert.Single(r.Matched);
        Assert.Equal(p1, matched.MatchedVoucherId);
        Assert.Equal("INV001", matched.Line.DocNumberNorm);

        var partial = Assert.Single(r.PartialMismatches);
        Assert.Equal("BILL22", partial.Line.DocNumberNorm);
        Assert.Equal(100, partial.TaxableVariancePaisa); // portal − books = +₹1

        var portalOnly = Assert.Single(r.InPortalOnly);
        Assert.Equal("XYZ-1", portalOnly.DocNumber);

        var booksOnly = Assert.Single(r.InBooksOnly);
        Assert.Equal("INV-777", booksOnly.SupplierDocNumber);
    }

    [Fact]
    public void Tolerance_promotes_a_near_match_from_partial_to_matched()
    {
        var c = BuildReconCompany(out _, out var b);
        PostPurchase(c, b, 5000m, "BILL/22", D1);
        var snapshot = Snapshot(c, Line(GstinSupplierB, "BILL22", 5001m, 900.18m, D1)); // ₹1 over on taxable

        Assert.Equal(ReconBucket.PartialMismatch,
            Bucket(Gstr2bReconciler.Reconcile(c, snapshot, From, To, ReconTolerance.Exact)));
        Assert.Equal(ReconBucket.Matched,
            Bucket(Gstr2bReconciler.Reconcile(c, snapshot, From, To, new ReconTolerance(100, 0))));
    }

    private static ReconBucket Bucket(Gstr2bReconciliationReport r) =>
        r.MatchedCount == 1 ? ReconBucket.Matched
        : r.PartialMismatchCount == 1 ? ReconBucket.PartialMismatch
        : r.InPortalOnlyCount == 1 ? ReconBucket.InPortalOnly : ReconBucket.InBooksOnly;

    [Fact]
    public void Match_is_deterministic_and_one_to_one_when_two_vouchers_are_candidates()
    {
        var c = BuildReconCompany(out var a, out _);
        var p1 = PostPurchase(c, a, 10000m, "INV-001", D1);
        var p2 = PostPurchase(c, a, 10000m, "INV-001", D1); // an identical second candidate

        var snapshot = Snapshot(c, Line(GstinSupplierA, "INV001", 10000m, 1800m, D1));

        var r1 = Gstr2bReconciler.Reconcile(c, snapshot, From, To, ReconTolerance.Exact);
        var r2 = Gstr2bReconciler.Reconcile(c, snapshot, From, To, ReconTolerance.Exact);

        // Exactly one voucher matched; the other is InBooksOnly (a consumed voucher can't re-match).
        Assert.Single(r1.Matched);
        Assert.Single(r1.InBooksOnly);
        Assert.Contains(new[] { p1, p2 }, id => id == r1.Matched[0].MatchedVoucherId);
        // Re-running yields the identical pairing (stable).
        Assert.Equal(r1.Matched[0].MatchedVoucherId, r2.Matched[0].MatchedVoucherId);
    }

    [Fact]
    public void Reverse_charge_inward_is_excluded_from_the_books_register()
    {
        var c = BuildReconCompany(out var a, out _);
        PostRcmPurchase(c, a, 10000m);                        // an RCM inward supply (no forward tax line)
        PostPurchase(c, a, 4000m, "FWD-1", D1);              // a normal forward-charge purchase

        // Reconcile against an EMPTY snapshot: only the forward-charge purchase is a candidate register entry.
        var r = Gstr2bReconciler.Reconcile(c, Snapshot(c), From, To, ReconTolerance.Exact);
        var only = Assert.Single(r.InBooksOnly);
        Assert.Equal("FWD-1", only.SupplierDocNumber); // the RCM purchase never appears
    }

    private static void PostRcmPurchase(Company c, Domain.Ledger supplier, decimal taxable)
    {
        // A minimal balanced RCM dual leg: expense + party (no invoice tax) + an RCM output-liability leg + an RCM ITC
        // leg, both reverse-charge tagged, so the voucher carries NO forward (non-RCM) GST line.
        var expense = c.FindLedgerByName("RCM Expense") ?? Add(c, "RCM Expense", "Indirect Expenses", true);
        var rcmOut = c.FindLedgerByName("RCM Output IGST") ?? Add(c, "RCM Output IGST", "Duties & Taxes", false);
        var rcmItc = c.FindLedgerByName("RCM Input IGST") ?? Add(c, "RCM Input IGST", "Duties & Taxes", true);
        var igst = new Money(taxable * 0.18m);
        var lines = new List<EntryLine>
        {
            new(expense.Id, new Money(taxable), DrCr.Debit),
            new(supplier.Id, new Money(taxable), DrCr.Credit),
            new(rcmItc.Id, igst, DrCr.Debit, gst: new GstLineTax(GstTaxHead.Integrated, 1800, new Money(taxable), isReverseCharge: true, RcmItcScheme.OtherRcm)),
            new(rcmOut.Id, igst, DrCr.Credit, gst: new GstLineTax(GstTaxHead.Integrated, 1800, new Money(taxable), isReverseCharge: true)),
        };
        new LedgerService(c).Post(new Voucher(Guid.NewGuid(),
            c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase).Id, D1, lines, partyId: supplier.Id));
    }

    [Fact]
    public void Composition_company_has_no_books_register()
    {
        var c = BuildReconCompany(out var a, out _, GstRegistrationType.Composition);
        PostPurchase(c, a, 10000m, "INV-001", D1);
        var snapshot = Snapshot(c, Line(GstinSupplierA, "INV001", 10000m, 1800m, D1));

        var r = Gstr2bReconciler.Reconcile(c, snapshot, From, To, ReconTolerance.Exact);
        Assert.Empty(r.InBooksOnly);        // composition takes no ITC ⇒ no register
        Assert.Empty(r.Matched);
        Assert.Single(r.InPortalOnly);      // the 2B line has nothing to match against
    }

    [Fact]
    public void Exact_doc_no_voucher_wins_the_match_over_a_ref_less_value_twin()
    {
        // Finding #1: a doc-LESS value/date twin must NOT greedily steal the pairing from the exact-doc-no voucher.
        var c = BuildReconCompany(out var a, out _);
        var withRef = PostPurchase(c, a, 10000m, "INV-1", D1);   // the exact supplier ref
        var refless = PostPurchaseNoRef(c, a, 10000m, D1);       // same value/date, NO ref

        var snapshot = Snapshot(c, Line(GstinSupplierA, "INV1", 10000m, 1800m, D1));

        var r = Gstr2bReconciler.Reconcile(c, snapshot, From, To, ReconTolerance.Exact);

        // The exact-doc-no voucher is a clean Match; the ref-less twin is InBooksOnly (not a partial that stole it, and
        // not a spurious S7 ITC-reversal candidate for the real match).
        var matched = Assert.Single(r.Matched);
        Assert.Equal(withRef, matched.MatchedVoucherId);
        Assert.Equal("INV1", matched.Line.DocNumberNorm);
        Assert.Empty(r.PartialMismatches);
        var booksOnly = Assert.Single(r.InBooksOnly);
        Assert.Equal(refless, booksOnly.VoucherId);
        Assert.Null(booksOnly.SupplierDocNumber);
    }

    [Fact]
    public void Reverse_charge_2b_line_is_excluded_not_flagged_in_portal_only()
    {
        // Finding #2: a supplier-flagged RCM 2B line is bypassed SYMMETRICALLY — like the RCM books entry, it is excluded
        // from reconciliation (handled by the S2a self-invoice path), never surfaced as InPortalOnly.
        var c = BuildReconCompany(out var a, out _);
        PostRcmPurchase(c, a, 10000m);   // a booked RCM inward supply (excluded from the books register)

        var rcmLine = new Gstr2bLine(Guid.NewGuid(), GstinSupplierA, null, Gstr2bDocType.B2b, "RCM-1",
            Gstr2bReconciler.NormaliseDocNo("RCM-1"), D1, "27", 1_000_000, 180_000, 0, 0, 0, itcAvailable: true, null,
            reverseCharge: true);
        var r = Gstr2bReconciler.Reconcile(c, Snapshot(c, rcmLine), From, To, ReconTolerance.Exact);

        Assert.Empty(r.InPortalOnly);   // the RCM 2B line is NOT flagged as "supplier filed, you didn't book"
        Assert.Empty(r.Matched);
        Assert.Empty(r.PartialMismatches);
    }

    [Fact]
    public void Pairing_is_deterministic_across_reimports_on_a_doc_no_date_collision()
    {
        // Finding #4: two 2B lines sharing GSTIN + normalised doc-no + date must pair deterministically across re-imports.
        // Pre-fix the tiebreak was the random per-line Guid, so the exact-value line only sometimes won the single books
        // voucher. Each iteration re-materialises the two lines with FRESH Guids; the exact-value line must ALWAYS win.
        var c = BuildReconCompany(out var a, out _);
        PostPurchase(c, a, 10000m, "INV-1", D1);   // one books voucher: ₹10,000

        for (var i = 0; i < 40; i++)
        {
            var exact = Line(GstinSupplierA, "INV1", 10000m, 1800m, D1);
            var other = Line(GstinSupplierA, "INV1", 20000m, 3600m, D1);
            var r = Gstr2bReconciler.Reconcile(c, Snapshot(c, exact, other), From, To, ReconTolerance.Exact);

            var matched = Assert.Single(r.Matched);
            Assert.Equal(1_000_000, matched.Line.TaxableValuePaisa);   // the ₹10,000 line, never the ₹20,000 one
            Assert.Single(r.InPortalOnly);
        }
    }

    [Fact]
    public void Advisory_only_import_and_reconcile_leaves_the_ledger_byte_identical()
    {
        var c = BuildReconCompany(out var a, out _);
        PostPurchase(c, a, 10000m, "INV-001", D1);
        PostPurchase(c, a, 2000m, "INV-777", D1);

        var beforeGstr3b = Gstr3b.Build(c, From, To);
        var beforeLedger = LedgerFingerprint(c);

        // Full inbound cycle: stage a snapshot, reconcile, persist the advisory results.
        var snapshot = Snapshot(c,
            Line(GstinSupplierA, "INV001", 10000m, 1800m, D1),
            Line(GstinSupplierA, "OTHER-1", 999m, 179.82m, D1));
        c.AddGstr2bSnapshot(snapshot);
        var report = Gstr2bReconciler.Reconcile(c, snapshot, From, To, ReconTolerance.Exact);
        foreach (var result in report.ToPersistedResults(Guid.NewGuid, Imported))
            c.AddGstr2bReconResult(result);

        // Nothing was posted: GSTR-3B + the ledger fingerprint are byte-identical to before.
        Assert.Equal(beforeGstr3b, Gstr3b.Build(c, From, To));
        Assert.Equal(beforeLedger, LedgerFingerprint(c));
        // The advisory results exist in the staging collection only.
        Assert.NotEmpty(c.Gstr2bReconResults);
    }

    private static string LedgerFingerprint(Company c) =>
        string.Join("|", c.Vouchers
            .OrderBy(v => v.Id)
            .SelectMany(v => v.Lines.Select(l =>
                $"{v.Id}:{l.LedgerId}:{l.Amount}:{l.Side}:{(l.Gst is { } g ? $"{g.TaxHead}/{g.RateBasisPoints}/{g.IsReverseCharge}" : "-")}")));

    [Fact]
    public void No_S6_type_takes_a_ledger_service_or_emits_an_entry_line()
    {
        // ER-14 structural: the advisory guarantee is the ABSENCE of any posting surface. No S6 engine/record may accept
        // a LedgerService or emit an EntryLine. Reflection-driven over the WHOLE Apex.Ledger assembly (finding #7) — every
        // type whose name starts with "Gstr2b" or "Recon" — so a NEW S6 type is covered automatically, not just a
        // hard-coded list of eight.
        var ledgerService = typeof(LedgerService);
        var s6Types = ledgerService.Assembly.GetTypes()
            .Where(t => t.Name.StartsWith("Gstr2b", StringComparison.Ordinal)
                     || t.Name.StartsWith("Recon", StringComparison.Ordinal))
            .ToList();

        // Sanity-guard the scan actually found the known S6 surface (a prefix typo would otherwise silently pass).
        foreach (var known in new[]
        {
            typeof(Gstr2bReconciler), typeof(Gstr2bReconciliationReport), typeof(Gstr2bSnapshot), typeof(Gstr2bLine),
            typeof(Gstr2bReconResult), typeof(ReconTolerance), typeof(ReconMatch), typeof(ReconBooksEntry),
        })
            Assert.Contains(known, s6Types);

        foreach (var t in s6Types)
        {
            foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                Assert.DoesNotContain(m.GetParameters(), p => p.ParameterType == ledgerService);
                Assert.DoesNotContain("EntryLine", m.ReturnType.FullName ?? "");
            }
            foreach (var ctor in t.GetConstructors())
                Assert.DoesNotContain(ctor.GetParameters(), p => p.ParameterType == ledgerService);
        }
    }
}
