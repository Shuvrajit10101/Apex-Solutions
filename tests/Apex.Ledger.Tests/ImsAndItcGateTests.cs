using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;

namespace Apex.Ledger.Tests;

/// <summary>
/// Phase-9 slice-6b <b>IMS offline mirror + §17(5) blocked-credit + ITC-gate</b> gate (RQ-14/RQ-15/RQ-26; DP-14/DP-31;
/// §16(2)(aa); ER-14). Every S6b type is <b>advisory-only</b> — the IMS mirror records the user's Accept/Reject/Pending
/// decision + the Oct-2025 credit-note reversal declaration but posts nothing; <see cref="ItcGateView"/> surfaces
/// books-vs-2B-vs-3B ITC and reversal <b>candidates</b> for S7 but never posts a reversal. §17(5) is a per-purchase
/// master flag with a fail-fast both-directions invariant. Deemed-accept is a derived view over an absent action row.
/// </summary>
public sealed class ImsAndItcGateTests
{
    private const string GstinMe = "27AAPFU0939F1ZV";
    private const string GstinSupplierA = "24AAACC1206D1ZM";
    private const string GstinSupplierB = "29AAAAA0000A1Z5";
    private static readonly DateOnly FyStart = new(2025, 4, 1);
    private static readonly DateOnly From = new(2025, 4, 1);
    private static readonly DateOnly To = new(2026, 3, 31);
    private static readonly DateOnly D1 = new(2025, 10, 10);
    private static readonly DateOnly Acted = new(2025, 11, 20);
    private static readonly DateTimeOffset Imported = new(2025, 11, 15, 9, 0, 0, TimeSpan.FromHours(5.5));

    // ---- fixture ----

    private static Company BuildCompany(out Domain.Ledger supplierA, out Domain.Ledger supplierB)
    {
        var c = CompanyFactory.CreateSeeded("Gate Co", FyStart);
        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = "27", Gstin = GstinMe, RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart, Periodicity = GstReturnPeriodicity.Monthly,
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

    /// <summary>A Purchase-Accounts ledger flagged §17(5)-agnostic <b>Ineligible</b> (Table 4(D): non-business / personal /
    /// time-barred) on its GST block. Ineligible with a <see cref="BlockedCreditCategory.None"/> category is valid.</summary>
    private static Domain.Ledger IneligiblePurchaseLedger(Company c, string name)
    {
        var l = new Domain.Ledger(Guid.NewGuid(), name, c.FindGroupByName("Purchase Accounts")!.Id, Money.Zero, openingIsDebit: true)
        {
            SalesPurchaseGst = new StockItemGstDetails
            {
                HsnSac = "9973", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800,
                ItcEligibility = ItcEligibility.Ineligible, // BlockedCreditCategory stays None (valid — not a §17(5) block)
            },
        };
        c.AddLedger(l);
        return l;
    }

    /// <summary>A Sundry-Creditor supplier with <b>no</b> GST registration (no GSTIN) — a B2C/URD supplier whose purchase
    /// can never appear in GSTR-2B.</summary>
    private static Domain.Ledger AddCreditorNoGst(Company c, string name)
    {
        var l = new Domain.Ledger(Guid.NewGuid(), name, c.FindGroupByName("Sundry Creditors")!.Id, Money.Zero,
            openingIsDebit: false, maintainBillByBill: true);
        c.AddLedger(l);
        return l;
    }

    private static Guid PostPurchase(Company c, Domain.Ledger supplier, decimal taxable, string billRef, DateOnly date,
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

    /// <summary>Posts ONE as-voucher Purchase bill with two purchase legs (<paramref name="led1"/>/<paramref name="led2"/>)
    /// at 18% inter-state — the two same-rate legs aggregate into a single IGST tax line, so the ITC-gate must split that
    /// tax between the legs' §17(5)/eligibility pools by taxable-value share.</summary>
    private static Guid PostMixedPurchase(Company c, Domain.Ledger supplier,
        Domain.Ledger led1, decimal amt1, Domain.Ledger led2, decimal amt2, string billRef, DateOnly date)
    {
        var gst = new GstService(c);
        var tax = gst.ComputeInvoiceTax(new[]
        {
            new GstService.TaxableLine(new Money(amt1), 1800),
            new GstService.TaxableLine(new Money(amt2), 1800),
        }, interState: true, GstTaxDirection.Input);
        var credit = new Money(amt1 + amt2 + tax.TotalTax.Amount);
        var lines = new List<EntryLine>
        {
            new(led1.Id, new Money(amt1), DrCr.Debit),
            new(led2.Id, new Money(amt2), DrCr.Debit),
            new(supplier.Id, credit, DrCr.Credit, billAllocations: new[] { new BillAllocation(BillRefType.NewRef, billRef, credit) }),
        };
        lines.AddRange(tax.TaxLines);
        return new LedgerService(c).Post(new Voucher(Guid.NewGuid(),
            c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase).Id, date, lines, partyId: supplier.Id)).Id;
    }

    private static Gstr2bLine Line(string gstin, string docNo, decimal taxable, decimal igst, DateOnly date,
        Gstr2bDocType type = Gstr2bDocType.B2b, bool rcm = false, bool itcAvailable = true) =>
        new(Guid.NewGuid(), gstin, null, type, docNo, Gstr2bReconciler.NormaliseDocNo(docNo), date, "27",
            (long)(taxable * 100), (long)(igst * 100), 0, 0, 0, itcAvailable, null, rcm);

    private static Gstr2bSnapshot Snapshot(params Gstr2bLine[] lines) =>
        new(Guid.NewGuid(), GstStatementType.Gstr2b, "2025-10", GstinMe, new DateOnly(2025, 11, 14),
            "HASH", Imported, 0, 0, 0, 0, lines);

    // ---- §17(5) EnsureValid (test 14) ----

    [Fact]
    public void Section17_5_EnsureValid_rejects_contradictory_eligibility_and_category_both_directions()
    {
        // Case A: a blocked eligibility with NO §17(5) sub-clause is a contradiction (a blocked item must name its clause).
        var blockedNoCat = new StockItemGstDetails
        {
            ItcEligibility = ItcEligibility.BlockedSection17_5, BlockedCreditCategory = BlockedCreditCategory.None,
        };
        Assert.Throws<ArgumentException>(() => blockedNoCat.EnsureValid());

        // Case B: an Eligible block with a non-None §17(5) category is a contradiction.
        var eligibleWithCat = new StockItemGstDetails
        {
            ItcEligibility = ItcEligibility.Eligible, BlockedCreditCategory = BlockedCreditCategory.MotorVehicles,
        };
        Assert.Throws<ArgumentException>(() => eligibleWithCat.EnsureValid());

        // A consistent blocked block validates; the default (Eligible/None) validates (byte-identical, ER-13).
        new StockItemGstDetails
        {
            ItcEligibility = ItcEligibility.BlockedSection17_5, BlockedCreditCategory = BlockedCreditCategory.MotorVehicles,
        }.EnsureValid();
        new StockItemGstDetails().EnsureValid();
    }

    // ---- IMS mirror (tests 15, 16, 17, 18) ----

    [Fact]
    public void Ims_SetAction_records_status_and_ClearAction_reverts_to_no_action()
    {
        var c = BuildCompany(out _, out _);
        var line = Line(GstinSupplierA, "INV-1", 10000m, 1800m, D1);
        c.AddGstr2bSnapshot(Snapshot(line));

        ImsService.SetAction(c, line.Id, ImsStatus.Accepted, actedOn: Acted);
        var acc = c.FindImsActionForLine(line.Id)!;
        Assert.Equal(ImsStatus.Accepted, acc.Status);
        Assert.Equal(Acted, acc.ActedOn);

        // Re-decide the SAME line — updates in place, never a second row.
        ImsService.SetAction(c, line.Id, ImsStatus.Rejected, remarks: "disputed", actedOn: Acted);
        Assert.Equal(ImsStatus.Rejected, c.FindImsActionForLine(line.Id)!.Status);
        Assert.Single(c.ImsActions);

        ImsService.SetAction(c, line.Id, ImsStatus.Pending);
        Assert.Equal(ImsStatus.Pending, c.FindImsActionForLine(line.Id)!.Status);

        ImsService.ClearAction(c, line.Id);
        Assert.Null(c.FindImsActionForLine(line.Id));
        Assert.Empty(c.ImsActions);
    }

    [Fact]
    public void Ims_line_with_no_action_reads_deemed_accepted()
    {
        var c = BuildCompany(out _, out _);
        var line = Line(GstinSupplierA, "INV-1", 10000m, 1800m, D1);
        c.AddGstr2bSnapshot(Snapshot(line));

        // A fresh import needs ZERO ims rows and still reads "all deemed accepted" (derived, not stored).
        Assert.Empty(c.ImsActions);
        Assert.Equal(ImsStatus.Accepted, ImsService.EffectiveStatus(c, line));

        // A Pending decision is NOT deemed-accepted (the user deferred it).
        ImsService.SetAction(c, line.Id, ImsStatus.Pending);
        Assert.Equal(ImsStatus.Pending, ImsService.EffectiveStatus(c, line));
    }

    [Fact]
    public void Ims_accept_of_cdn_with_partial_reversal_stores_paisa_and_requires_remarks()
    {
        var c = BuildCompany(out _, out _);
        var cn = Line(GstinSupplierA, "CN-1", 5000m, 900m, D1, Gstr2bDocType.CreditNote);
        c.AddGstr2bSnapshot(Snapshot(cn));

        // A partial declared reversal WITHOUT remarks is rejected (remarks mandatory when partial, §3.2) — nothing stored.
        Assert.Throws<ArgumentException>(() =>
            ImsService.SetAction(c, cn.Id, ImsStatus.Accepted, remarks: null, declaredReversalPaisa: 40_000));
        Assert.Empty(c.ImsActions);

        // A partial declared reversal WITH remarks stores the paisa. No reversal is posted (advisory only).
        ImsService.SetAction(c, cn.Id, ImsStatus.Accepted, remarks: "partial per contract",
            declaredReversalPaisa: 40_000, actedOn: Acted);
        var acc = c.FindImsActionForLine(cn.Id)!;
        Assert.Equal(40_000, acc.DeclaredReversalPaisa);
        Assert.False(acc.NoReversalDeclared);

        // A reversal declaration only accompanies an ACCEPT — Reject + declared reversal is rejected.
        Assert.Throws<ArgumentException>(() =>
            ImsService.SetAction(c, cn.Id, ImsStatus.Rejected, remarks: "x", declaredReversalPaisa: 10_000));

        // The no-reversal declaration path.
        ImsService.SetAction(c, cn.Id, ImsStatus.Accepted, noReversalDeclared: true);
        var acc2 = c.FindImsActionForLine(cn.Id)!;
        Assert.True(acc2.NoReversalDeclared);
        Assert.Null(acc2.DeclaredReversalPaisa);

        // A partial reversal AND a no-reversal declaration are mutually exclusive.
        Assert.Throws<ArgumentException>(() =>
            ImsService.SetAction(c, cn.Id, ImsStatus.Accepted, remarks: "x", declaredReversalPaisa: 5_000, noReversalDeclared: true));
    }

    [Fact]
    public void Ims_SetAction_on_a_reverse_charge_line_is_rejected()
    {
        var c = BuildCompany(out _, out _);
        var rcmLine = Line(GstinSupplierA, "RCM-1", 10000m, 1800m, D1, rcm: true);
        c.AddGstr2bSnapshot(Snapshot(rcmLine));

        // A supplier-flagged reverse-charge 2B line bypasses IMS (§3.3) — it is not IMS-actionable.
        Assert.Throws<InvalidOperationException>(() => ImsService.SetAction(c, rcmLine.Id, ImsStatus.Accepted));
        Assert.Empty(c.ImsActions);
    }

    // ---- §16(2)(aa) + §17(5) ITC gate (tests 12, 13) ----

    [Fact]
    public void ItcGate_flags_a_booked_purchase_not_in_2b_as_ineligible_this_period()
    {
        var c = BuildCompany(out var a, out var b);
        var booked = PostPurchase(c, a, 10000m, "INV-777", D1); // no matching 2B line ⇒ InBooksOnly (§16(2)(aa))

        var snap = Snapshot(Line(GstinSupplierB, "OTHER-1", 3000m, 540m, D1)); // an unrelated supplier line
        c.AddGstr2bSnapshot(snap);

        var view = ItcGateView.Build(c, snap, From, To);

        // The booked ₹1,800 IGST is eligible but NOT reflected in 2B → not claimable this period, surfaced as a candidate.
        Assert.Equal(1800m, view.BooksEligible.Igst.Amount);
        Assert.Equal(0m, view.Claimable.Igst.Amount);
        Assert.Equal(1800m, view.NotInPortal.Igst.Amount);
        var cand = view.ReversalCandidates.Single(x => x.Reason == ItcReversalReason.Section16_2aaNotInPortal);
        Assert.Equal(booked, cand.VoucherId);
        Assert.Equal(1800m, cand.SuggestedReversal.Amount);
    }

    [Fact]
    public void ItcGate_excludes_a_section17_5_blocked_purchase_from_the_eligible_pool()
    {
        var c = BuildCompany(out var a, out _);
        var blockedLedger = BlockedPurchaseLedger(c, "Motor Car Purchase");
        var eligLedger = AddLedger(c, "Raw Material Purchase", "Purchase Accounts", true);

        var blocked = PostPurchase(c, a, 10000m, "CAR-1", D1, blockedLedger); // §17(5) motor vehicle
        PostPurchase(c, a, 20000m, "RM-1", D1, eligLedger);                    // fully eligible

        // Both are in 2B (matched) so §16(2)(aa) does not interfere with the §17(5) test.
        var snap = Snapshot(
            Line(GstinSupplierA, "CAR-1", 10000m, 1800m, D1),
            Line(GstinSupplierA, "RM-1", 20000m, 3600m, D1));
        c.AddGstr2bSnapshot(snap);

        var view = ItcGateView.Build(c, snap, From, To);

        // The blocked ₹1,800 is on the Table 4(B)(1)/4(D) blocked line, NOT in the eligible pool; the eligible ₹3,600 is.
        Assert.Equal(1800m, view.BlockedItc.Igst.Amount);
        Assert.Equal(3600m, view.BooksEligible.Igst.Amount);
        Assert.Equal(3600m, view.Claimable.Igst.Amount);
        var cand = view.ReversalCandidates.Single(x => x.Reason == ItcReversalReason.Section17_5Blocked);
        Assert.Equal(blocked, cand.VoucherId);
        Assert.Equal(1800m, cand.SuggestedReversal.Amount);
    }

    [Fact]
    public void ItcGate_surfaces_an_accepted_credit_note_with_a_partial_declared_reversal()
    {
        // FIX 1: ACCEPTING a supplier credit note IS the recipient's ITC-reversal event (not rejecting it). An Accept with
        // an Oct-2025 partial declared reversal (₹300) surfaces exactly ONE CDN candidate suggesting that declared amount.
        var c = BuildCompany(out _, out _);
        var cn = Line(GstinSupplierA, "CN-5", 4000m, 720m, D1, Gstr2bDocType.CreditNote); // non-RCM CDN line
        c.AddGstr2bSnapshot(Snapshot(cn));
        ImsService.SetAction(c, cn.Id, ImsStatus.Accepted, remarks: "partial per contract", declaredReversalPaisa: 30_000);

        var view = ItcGateView.Build(c, c.Gstr2bSnapshots[0], From, To);

        var cand = view.ReversalCandidates.Single(x => x.Reason == ItcReversalReason.ImsAcceptedCreditNote);
        Assert.Equal(cn.Id, cand.LineId);
        Assert.Equal(300m, cand.SuggestedReversal.Amount); // the DECLARED partial reversal, not the full ₹720 forward tax
    }

    [Fact]
    public void ItcGate_does_not_surface_a_rejected_credit_note()
    {
        // FIX 1: REJECTING a supplier credit note means no reversal is due from the recipient ⇒ zero CDN candidates.
        var c = BuildCompany(out _, out _);
        var cn = Line(GstinSupplierA, "CN-5", 4000m, 720m, D1, Gstr2bDocType.CreditNote);
        c.AddGstr2bSnapshot(Snapshot(cn));
        ImsService.SetAction(c, cn.Id, ImsStatus.Rejected, remarks: "disputed CN");

        var view = ItcGateView.Build(c, c.Gstr2bSnapshots[0], From, To);

        Assert.DoesNotContain(view.ReversalCandidates, x => x.Reason == ItcReversalReason.ImsAcceptedCreditNote);
    }

    [Fact]
    public void ItcGate_surfaces_a_deemed_accepted_credit_note_at_full_forward_tax()
    {
        // FIX 1: a CDN line with NO IMS action is deemed-accepted ⇒ the reversal event fires at the full forward tax.
        var c = BuildCompany(out _, out _);
        var cn = Line(GstinSupplierA, "CN-6", 4000m, 720m, D1, Gstr2bDocType.CreditNote);
        c.AddGstr2bSnapshot(Snapshot(cn)); // no SetAction ⇒ deemed-accept

        var view = ItcGateView.Build(c, c.Gstr2bSnapshots[0], From, To);

        var cand = view.ReversalCandidates.Single(x => x.Reason == ItcReversalReason.ImsAcceptedCreditNote);
        Assert.Equal(cn.Id, cand.LineId);
        Assert.Equal(720m, cand.SuggestedReversal.Amount);
    }

    [Fact]
    public void ItcGate_surfaces_an_accepted_credit_note_with_a_no_reversal_declaration_at_zero()
    {
        // FIX 1: an Accept with an explicit "no ITC reversal required" declaration surfaces a candidate suggesting ₹0.
        var c = BuildCompany(out _, out _);
        var cn = Line(GstinSupplierA, "CN-8", 4000m, 720m, D1, Gstr2bDocType.CreditNote);
        c.AddGstr2bSnapshot(Snapshot(cn));
        ImsService.SetAction(c, cn.Id, ImsStatus.Accepted, noReversalDeclared: true);

        var view = ItcGateView.Build(c, c.Gstr2bSnapshots[0], From, To);

        var cand = view.ReversalCandidates.Single(x => x.Reason == ItcReversalReason.ImsAcceptedCreditNote);
        Assert.Equal(0m, cand.SuggestedReversal.Amount);
    }

    [Fact]
    public void ItcGate_splits_a_mixed_bill_between_blocked_and_eligible_per_line()
    {
        // FIX 2: §17(5) blocked-ness is PER LINE, not per voucher. ONE bill with a blocked motor-vehicle leg (₹10,000 →
        // ₹1,800 IGST) + an eligible raw-material leg (₹20,000 → ₹3,600 IGST) must split the aggregated ₹5,400 IGST by
        // taxable-value share — ₹1,800 blocked, ₹3,600 eligible — NOT route the whole ₹5,400 to the blocked pool.
        var c = BuildCompany(out var a, out _);
        var blockedLedger = BlockedPurchaseLedger(c, "Motor Car Purchase");
        var eligLedger = AddLedger(c, "Raw Material Purchase", "Purchase Accounts", true);

        var vid = PostMixedPurchase(c, a, blockedLedger, 10000m, eligLedger, 20000m, "MIX-1", D1);

        var snap = Snapshot(Line(GstinSupplierA, "MIX-1", 30000m, 5400m, D1)); // matched ⇒ the eligible share is claimable
        c.AddGstr2bSnapshot(snap);

        var view = ItcGateView.Build(c, snap, From, To);

        Assert.Equal(1800m, view.BlockedItc.Igst.Amount);
        Assert.Equal(3600m, view.BooksEligible.Igst.Amount);
        Assert.Equal(3600m, view.Claimable.Igst.Amount);
        var cand = view.ReversalCandidates.Single(x => x.Reason == ItcReversalReason.Section17_5Blocked);
        Assert.Equal(vid, cand.VoucherId);
        Assert.Equal(1800m, cand.SuggestedReversal.Amount); // only the blocked portion, not the whole voucher's tax
    }

    [Fact]
    public void ItcGate_routes_an_ineligible_purchase_to_the_table_4d_line_not_the_eligible_pool()
    {
        // FIX 3: an ItcEligibility.Ineligible purchase (non-business / personal / time-barred) must be excluded from the
        // eligible/claimable pool and surfaced on a DISTINCT Table 4(D) advisory line — not silently treated as eligible.
        var c = BuildCompany(out var a, out _);
        var ineligLedger = IneligiblePurchaseLedger(c, "Personal Use Purchase");
        var vid = PostPurchase(c, a, 10000m, "IN-1", D1, ineligLedger); // ₹1,800 IGST, Ineligible

        var snap = Snapshot(Line(GstinSupplierA, "IN-1", 10000m, 1800m, D1)); // matched ⇒ §16(2)(aa) does not interfere
        c.AddGstr2bSnapshot(snap);

        var view = ItcGateView.Build(c, snap, From, To);

        Assert.Equal(1800m, view.IneligibleItc.Igst.Amount);   // Table 4(D)
        Assert.Equal(0m, view.BooksEligible.Igst.Amount);       // excluded from the eligible pool
        Assert.Equal(0m, view.Claimable.Igst.Amount);
        Assert.Equal(0m, view.BlockedItc.Igst.Amount);          // NOT the §17(5) Table 4(B)(1) line
        var cand = view.ReversalCandidates.Single(x => x.Reason == ItcReversalReason.Ineligible);
        Assert.Equal(vid, cand.VoucherId);
        Assert.Equal(1800m, cand.SuggestedReversal.Amount);
    }

    [Fact]
    public void ItcGate_routes_a_no_gstin_forward_purchase_to_not_in_portal_so_the_identity_holds()
    {
        // FIX 4: a forward-GST purchase whose supplier carries no GSTIN can NEVER appear in 2B (the reconciler excludes it
        // from the register), so its eligible ITC must land in NotInPortal (§16(2)(aa)) — otherwise it is stranded in
        // BooksEligible alone and the identity BooksEligible = Claimable + NotInPortal breaks.
        var c = BuildCompany(out _, out _);
        var urd = AddCreditorNoGst(c, "Unregistered Supplier");
        var vid = PostPurchase(c, urd, 10000m, "URD-1", D1); // forward ₹1,800 IGST, no supplier GSTIN

        var snap = Snapshot(Line(GstinSupplierA, "OTHER-1", 3000m, 540m, D1)); // an unrelated 2B line
        c.AddGstr2bSnapshot(snap);

        var view = ItcGateView.Build(c, snap, From, To);

        Assert.Equal(1800m, view.NotInPortal.Igst.Amount);
        Assert.Equal(0m, view.Claimable.Igst.Amount);
        Assert.Equal(view.BooksEligible.Total.Amount, view.Claimable.Total.Amount + view.NotInPortal.Total.Amount);
        Assert.Contains(view.ReversalCandidates,
            x => x.Reason == ItcReversalReason.Section16_2aaNotInPortal && x.VoucherId == vid);
    }
}
