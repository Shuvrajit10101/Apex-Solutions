using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;

using SetOff = Apex.Ledger.Services.GstSetOffService;

namespace Apex.Ledger.Tests;

/// <summary>
/// Phase 9 slice 7a — the Rule-88A / §49A ITC set-off engine (RQ-21; ER-7/ER-2/ER-3; A14-CONFIRMED §11.1). The pure
/// allocator is asserted by LEGAL PROPERTIES (IGST-first-and-exhausted, head-purity walls, cess ring-fence, minimal
/// residual cash, no CGST↔SGST cross-utilisation) — NOT memorised Circular-98 numbers — plus the posting-correctness
/// invariants (balanced, idempotent, no-pollution, paisa-conservation) over the single guarded entry-point.
/// </summary>
public sealed class GstSetOffTests
{
    private static readonly DateOnly FyStart = new(2024, 4, 1);
    private const string GstinMh = "27AAPFU0939F1ZV";

    // ---------------------------------------------------------------- pure-allocator property helpers

    private static SetOff.SetOffDemand Demand(
        long lC, long lS, long lI, long lCess, long lRcm, long cC, long cS, long cI, long cCess) =>
        new(lC, lS, lI, lCess, lRcm, cC, cS, cI, cCess);

    private static void AssertNoCrossHead(SetOff.SetOffAllocation a)
    {
        foreach (var l in a.Lines)
        {
            Assert.False(l.CreditHead == GstTaxHead.Central && l.LiabilityHead == GstTaxHead.State,
                "CGST credit must never discharge SGST liability (ER-7).");
            Assert.False(l.CreditHead == GstTaxHead.State && l.LiabilityHead == GstTaxHead.Central,
                "SGST credit must never discharge CGST liability (ER-7).");
            Assert.True((l.CreditHead == GstTaxHead.Cess) == (l.LiabilityHead == GstTaxHead.Cess),
                "Cess credit is ring-fenced to cess liability (ER-2).");
        }
    }

    // ---------------------------------------------------------------- 1. Fixture (F): both splits foot to ₹0 cash

    [Theory]
    [InlineData(ResidualIgstSplit.CgstFirst)]
    [InlineData(ResidualIgstSplit.SgstFirst)]
    public void FixtureF_both_splits_pay_zero_cash_with_a_hundred_rupee_surplus(ResidualIgstSplit split)
    {
        // Liab IGST 1000 / CGST 300 / SGST 300 ; Credit IGST 1300 / CGST 200 / SGST 200 (paisa). Total credit 1700
        // exceeds total liability 1600 by ₹100 and — IGST being fungible — the whole liability discharges without cash.
        var demand = Demand(30000, 30000, 100000, 0, 0, 20000, 20000, 130000, 0);
        var a = SetOff.Allocate(demand, new SetOff.SetOffOptions(split));

        // (a) minimal residual cash = ₹0 in both valid splits; the cash ledger is untouched.
        Assert.Equal(0, a.TotalCash);
        // (b) the ₹100 (10 000 paisa) surplus lands in the expected pool per the split.
        if (split == ResidualIgstSplit.CgstFirst)
        {
            Assert.Equal(10000, a.ClosingCgst);
            Assert.Equal(0, a.ClosingSgst);
        }
        else
        {
            Assert.Equal(0, a.ClosingCgst);
            Assert.Equal(10000, a.ClosingSgst);
        }
        // (c) no CGST↔SGST cross-utilisation in any line; (d) IGST credit fully exhausted.
        AssertNoCrossHead(a);
        Assert.Equal(0, a.ClosingIgst);
        // conservation: Σ credit used + Σ closing == Σ opening credit; Σ credit-to-liab + cash == Σ liability.
        Assert.Equal(20000 + 20000 + 130000, a.TotalCreditUtilised + a.ClosingCgst + a.ClosingSgst + a.ClosingIgst);
    }

    // ---------------------------------------------------------------- 2. IGST credit exhausted before own credit

    [Fact]
    public void Igst_credit_is_exhausted_before_any_cgst_or_sgst_credit_is_used()
    {
        // Only a CGST liability, but both IGST and CGST credit available: IGST credit must be used first.
        var a = SetOff.Allocate(Demand(50000, 0, 0, 0, 0, 50000, 0, 50000, 0));
        var ownUsed = a.Lines.Any(l => l.CreditHead is GstTaxHead.Central or GstTaxHead.State);
        if (ownUsed) Assert.Equal(0, a.ClosingIgst); // own credit only after IGST is gone
        // IGST credit (50000) covers the whole CGST liability (50000); own CGST credit is carried forward untouched.
        Assert.Equal(50000, a.ClosingCgst);
        Assert.Equal(0, a.ClosingIgst);
        Assert.Equal(0, a.TotalCash);
    }

    // ---------------------------------------------------------------- 3. CGST↔SGST cross-utilisation impossible

    [Fact]
    public void No_allocation_line_pairs_cgst_credit_with_sgst_liability_or_vice_versa()
    {
        var a = SetOff.Allocate(Demand(40000, 60000, 20000, 0, 0, 70000, 10000, 5000, 0));
        AssertNoCrossHead(a);
    }

    // ---------------------------------------------------------------- 4. §49(5)(c)/(d) proviso

    [Fact]
    public void Sgst_credit_pays_igst_only_after_cgst_credit_for_igst_is_exhausted()
    {
        // IGST liability with both CGST and SGST credit (no IGST credit): CGST credit must pay IGST before SGST does.
        var a = SetOff.Allocate(Demand(0, 0, 100000, 0, 0, 40000, 40000, 0, 0));
        var sgstPaidIgst = a.Lines.Any(l => l is { CreditHead: GstTaxHead.State, LiabilityHead: GstTaxHead.Integrated });
        if (sgstPaidIgst) Assert.Equal(0, a.ClosingCgst); // CGST credit must be exhausted first
        // 40000 CGST + 40000 SGST discharge 80000 of the 100000 IGST liability; 20000 remains as cash.
        Assert.Equal(20000, a.CashIgst);
    }

    // ---------------------------------------------------------------- 5. Cess ring-fence

    [Fact]
    public void Cess_credit_discharges_only_cess_and_a_cess_surplus_never_pays_other_heads()
    {
        var a = SetOff.Allocate(Demand(10000, 10000, 10000, 5000, 0, 0, 0, 0, 20000));
        AssertNoCrossHead(a);
        Assert.Equal(15000, a.ClosingCess);                    // 20000 cess credit − 5000 cess liab
        Assert.Equal(10000 + 10000 + 10000, a.TotalGstCash);   // no cess credit leaked to CGST/SGST/IGST
    }

    // ---------------------------------------------------------------- 6. RCM / interest cash-only

    [Fact]
    public void Rcm_only_liability_pays_entirely_in_cash_with_zero_credit_utilisation()
    {
        var a = SetOff.Allocate(Demand(0, 0, 0, 0, 90000, 50000, 50000, 50000, 0));
        Assert.Empty(a.Lines);                 // no credit line
        Assert.Equal(0, a.TotalCreditUtilised);
        Assert.Equal(90000, a.CashRcm);
        Assert.Equal(50000, a.ClosingCgst);    // credit untouched
    }

    // ---------------------------------------------------------------- 7. Rule 86B (DP-17)

    [Fact]
    public void Rule86b_forces_at_least_one_percent_cash_when_on_and_has_no_effect_when_off()
    {
        // Output tax 100000 paisa (₹1000); credit fully covers it ⇒ ₹0 cash by default.
        var demand = Demand(50000, 50000, 0, 0, 0, 50000, 50000, 0, 0);
        var off = SetOff.Allocate(demand);
        Assert.Equal(0, off.TotalGstCash);

        var on = SetOff.Allocate(demand, new SetOff.SetOffOptions(Rule86B: new SetOff.Rule86BConfig(true)));
        Assert.True(on.TotalGstCash >= 1000, "≥ 1% of the ₹1000 output tax must be forced through cash.");
        // conservation still holds after the clawback.
        Assert.Equal(50000 + 50000, on.TotalCreditUtilised + on.ClosingCgst + on.ClosingSgst + on.ClosingIgst);
    }

    // ---------------------------------------------------------------- 8. Illegal override rejected (fail-fast)

    [Fact]
    public void An_illegal_override_is_rejected_and_a_legal_one_validates()
    {
        var demand = Demand(30000, 30000, 100000, 0, 0, 20000, 20000, 130000, 0);
        var legal = SetOff.Allocate(demand);
        SetOff.EnsureLegal(demand, legal); // no throw

        // A cross-head override (CGST credit → SGST liability) is illegal.
        var crossHead = legal with
        {
            Lines = new[] { new SetOff.SetOffLine(GstTaxHead.Central, GstTaxHead.State, 100) },
        };
        Assert.Throws<InvalidOperationException>(() => SetOff.EnsureLegal(demand, crossHead));

        // An IGST-not-exhausted override (own CGST credit used while IGST credit remains) is illegal.
        var igstNotExhausted = legal with
        {
            Lines = new[] { new SetOff.SetOffLine(GstTaxHead.Central, GstTaxHead.Central, 20000) },
        };
        Assert.Throws<InvalidOperationException>(() => SetOff.EnsureLegal(demand, igstNotExhausted));
    }

    // ---------------------------------------------------------------- 9. Determinism + paisa exactness

    [Fact]
    public void Allocation_is_deterministic_and_paisa_exact()
    {
        var demand = Demand(33333, 44444, 55555, 6666, 7777, 22222, 11111, 88888, 9999);
        var a = SetOff.Allocate(demand);
        var b = SetOff.Allocate(demand);
        Assert.Equal(a.Lines, b.Lines);

        // Σ credit-used-per-head + closing == opening credit (per head).
        long usedC = a.Lines.Where(l => l.CreditHead == GstTaxHead.Central).Sum(l => l.AmountPaisa);
        long usedS = a.Lines.Where(l => l.CreditHead == GstTaxHead.State).Sum(l => l.AmountPaisa);
        long usedI = a.Lines.Where(l => l.CreditHead == GstTaxHead.Integrated).Sum(l => l.AmountPaisa);
        long usedCess = a.Lines.Where(l => l.CreditHead == GstTaxHead.Cess).Sum(l => l.AmountPaisa);
        Assert.Equal(22222, usedC + a.ClosingCgst);
        Assert.Equal(11111, usedS + a.ClosingSgst);
        Assert.Equal(88888, usedI + a.ClosingIgst);
        Assert.Equal(9999, usedCess + a.ClosingCess);

        // Σ credit-to-liab-per-head + cash == liability (per head).
        long covC = a.Lines.Where(l => l.LiabilityHead == GstTaxHead.Central).Sum(l => l.AmountPaisa);
        long covS = a.Lines.Where(l => l.LiabilityHead == GstTaxHead.State).Sum(l => l.AmountPaisa);
        long covI = a.Lines.Where(l => l.LiabilityHead == GstTaxHead.Integrated).Sum(l => l.AmountPaisa);
        long covCess = a.Lines.Where(l => l.LiabilityHead == GstTaxHead.Cess).Sum(l => l.AmountPaisa);
        Assert.Equal(33333, covC + a.CashCgst);
        Assert.Equal(44444, covS + a.CashSgst);
        Assert.Equal(55555, covI + a.CashIgst);
        Assert.Equal(6666, covCess + a.CashCess);
    }

    // ---------------------------------------------------------------- posting: a full company

    private sealed record Fixture(Company Company, GstService Gst, Domain.Ledger Bank);

    private static Fixture NewGstCompany()
    {
        var c = CompanyFactory.CreateSeeded("SetOff Co", FyStart);
        var gst = new GstService(c);
        gst.EnableGst(new GstConfig
        {
            HomeStateCode = "27",
            Gstin = GstinMh,
            RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart,
            Periodicity = GstReturnPeriodicity.Monthly,
        });
        var bank = new Domain.Ledger(Guid.NewGuid(), "Bank", c.FindGroupByName("Bank Accounts")!.Id, Money.Zero, true);
        c.AddLedger(bank);
        return new Fixture(c, gst, bank);
    }

    private static Domain.Ledger Add(Company c, string name, string group, bool dr)
    {
        var l = new Domain.Ledger(Guid.NewGuid(), name, c.FindGroupByName(group)!.Id, Money.Zero, dr);
        c.AddLedger(l);
        return l;
    }

    /// <summary>Posts an intra-state sale of <paramref name="taxable"/> ⇒ Output CGST+SGST liability.</summary>
    private static void PostIntraSale(Fixture f, Domain.Ledger party, Domain.Ledger sales, decimal taxable, DateOnly d)
    {
        var tax = f.Gst.ComputeInvoiceTax(new[] { new GstService.TaxableLine(Money.FromRupees(taxable), 1800) },
            interState: false, GstTaxDirection.Output);
        var total = taxable + tax.TotalTax.Amount;
        var lines = new List<EntryLine>
        {
            new(party.Id, Money.FromRupees(total), DrCr.Debit),
            new(sales.Id, Money.FromRupees(taxable), DrCr.Credit),
        };
        lines.AddRange(tax.TaxLines);
        var type = f.Company.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id;
        new LedgerService(f.Company).Post(new Voucher(Guid.NewGuid(), type, d, lines, partyId: party.Id));
    }

    /// <summary>Posts an intra-state purchase of <paramref name="taxable"/> ⇒ Input CGST+SGST credit.</summary>
    private static void PostIntraPurchase(Fixture f, Domain.Ledger party, Domain.Ledger purchases, decimal taxable, DateOnly d)
    {
        var tax = f.Gst.ComputeInvoiceTax(new[] { new GstService.TaxableLine(Money.FromRupees(taxable), 1800) },
            interState: false, GstTaxDirection.Input);
        var total = taxable + tax.TotalTax.Amount;
        var lines = new List<EntryLine>
        {
            new(purchases.Id, Money.FromRupees(taxable), DrCr.Debit),
            new(party.Id, Money.FromRupees(total), DrCr.Credit),
        };
        lines.AddRange(tax.TaxLines);
        var type = f.Company.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase).Id;
        new LedgerService(f.Company).Post(new Voucher(Guid.NewGuid(), type, d, lines, partyId: party.Id));
    }

    // ---------------------------------------------------------------- 20/22. balanced + idempotent replace

    [Fact]
    public void PostSetOff_posts_one_balanced_journal_records_table6_1_and_is_idempotent_per_period()
    {
        var f = NewGstCompany();
        var debtor = Add(f.Company, "Debtor", "Sundry Debtors", true);
        var creditor = Add(f.Company, "Creditor", "Sundry Creditors", false);
        var sales = Add(f.Company, "Sales", "Sales Accounts", false);
        var purchases = Add(f.Company, "Purchases", "Purchase Accounts", true);
        PostIntraSale(f, debtor, sales, 10000m, new(2024, 4, 5));      // Output CGST 900 + SGST 900
        PostIntraPurchase(f, creditor, purchases, 12000m, new(2024, 4, 3)); // Input CGST 1080 + SGST 1080

        var demand = new SetOff.SetOffDemand(90000, 90000, 0, 0, 0, 108000, 108000, 0, 0);
        var alloc = SetOff.Allocate(demand);
        var svc = new SetOff(f.Company);
        var v1 = svc.PostSetOff("2024-04", alloc, new(2024, 4, 30));

        Assert.NotNull(v1);
        Assert.True(VoucherValidator.IsBalanced(v1!));
        Assert.Equal(2, f.Company.SetoffLinesForVoucher(v1!.Id).Count()); // CGST→CGST, SGST→SGST
        var before = f.Company.Vouchers.Count;

        // Re-running the same period is a confirmed REPLACE — not a second additive voucher.
        var v2 = svc.PostSetOff("2024-04", alloc, new(2024, 4, 30));
        Assert.NotNull(v2);
        Assert.Equal(before, f.Company.Vouchers.Count);                 // still one set-off voucher
        Assert.Null(f.Company.FindVoucher(v1.Id));                      // the prior voucher was deleted
        Assert.Equal(2, f.Company.GstSetoffLines.Count);               // rows replaced, not stacked
    }

    // ---------------------------------------------------------------- 21. no pollution of GSTR-3B

    [Fact]
    public void A_posted_set_off_does_not_move_the_gstr3b_outward_or_itc_figures()
    {
        var f = NewGstCompany();
        var debtor = Add(f.Company, "Debtor", "Sundry Debtors", true);
        var creditor = Add(f.Company, "Creditor", "Sundry Creditors", false);
        var sales = Add(f.Company, "Sales", "Sales Accounts", false);
        var purchases = Add(f.Company, "Purchases", "Purchase Accounts", true);
        PostIntraSale(f, debtor, sales, 10000m, new(2024, 4, 5));
        PostIntraPurchase(f, creditor, purchases, 12000m, new(2024, 4, 3));

        var from = new DateOnly(2024, 4, 1); var to = new DateOnly(2024, 4, 30);
        var before = Gstr3b.Build(f.Company, from, to);

        var alloc = SetOff.Allocate(new SetOff.SetOffDemand(90000, 90000, 0, 0, 0, 108000, 108000, 0, 0));
        new SetOff(f.Company).PostSetOff("2024-04", alloc, to);

        var after = Gstr3b.Build(f.Company, from, to);
        Assert.Equal(before.OutwardCgst, after.OutwardCgst);
        Assert.Equal(before.OutwardSgst, after.OutwardSgst);
        Assert.Equal(before.ItcCgst, after.ItcCgst);
        Assert.Equal(before.ItcSgst, after.ItcSgst);
    }

    // ---------------------------------------------------------------- 23. paisa conservation end-to-end

    [Fact]
    public void Electronic_ledger_foots_exactly_after_a_set_off_plus_cash_discharge_cycle()
    {
        var f = NewGstCompany();
        var debtor = Add(f.Company, "Debtor", "Sundry Debtors", true);
        var creditor = Add(f.Company, "Creditor", "Sundry Creditors", false);
        var sales = Add(f.Company, "Sales", "Sales Accounts", false);
        var purchases = Add(f.Company, "Purchases", "Purchase Accounts", true);
        // Output CGST 1800 + SGST 1800 (20000 @18%); Input CGST 900 + SGST 900 (10000 @18%). Net ₹900 CGST + ₹900 SGST cash.
        PostIntraSale(f, debtor, sales, 20000m, new(2024, 4, 5));
        PostIntraPurchase(f, creditor, purchases, 10000m, new(2024, 4, 3));

        var demand = new SetOff.SetOffDemand(180000, 180000, 0, 0, 0, 90000, 90000, 0, 0);
        var alloc = SetOff.Allocate(demand);
        Assert.Equal(90000 + 90000, alloc.TotalGstCash); // ₹900 CGST + ₹900 SGST cash

        var to = new DateOnly(2024, 4, 30);
        new SetOff(f.Company).PostSetOff("2024-04", alloc, to);

        // Deposit the cash and discharge the residual per head.
        var deposit = new GstDepositService(f.Company);
        deposit.PostPmt06(GstTaxHead.Central, GstMinorHead.Tax, Money.FromRupees(900m), f.Bank, to, "CPIN-C", "CIN-C");
        deposit.PostPmt06(GstTaxHead.State, GstMinorHead.Tax, Money.FromRupees(900m), f.Bank, to, "CPIN-S", "CIN-S");
        deposit.PostCashDischarge(GstTaxHead.Central, Money.FromRupees(900m), to);
        deposit.PostCashDischarge(GstTaxHead.State, Money.FromRupees(900m), to);

        // Every liability discharged: the Output ledgers foot to zero, credit is exhausted, and every voucher balances.
        var view = ElectronicLedgersView.Build(f.Company, new DateOnly(2024, 4, 1), to);
        Assert.Equal(Money.Zero, view.LiabilityCgst);
        Assert.Equal(Money.Zero, view.LiabilitySgst);
        Assert.Equal(Money.Zero, view.CreditCgst);
        Assert.Equal(Money.Zero, view.CreditSgst);
        foreach (var v in f.Company.Vouchers) Assert.True(VoucherValidator.IsBalanced(v));
    }

    // ---------------------------------------------------------------- 30. ER-13 — off byte-identical (structural)

    [Fact]
    public void A_company_that_never_sets_off_or_pays_has_no_new_records_and_an_all_zero_view()
    {
        var f = NewGstCompany();
        Assert.Empty(f.Company.GstSetoffLines);
        Assert.Empty(f.Company.GstChallans);
        Assert.Empty(f.Company.GstDrc03s);
        Assert.Empty(f.Company.ItcReversals);
        var view = ElectronicLedgersView.Build(f.Company, new DateOnly(2024, 4, 1), new DateOnly(2024, 4, 30));
        Assert.Equal(Money.Zero, view.TotalCredit);
        Assert.Equal(Money.Zero, view.TotalLiability);
        Assert.Equal(Money.Zero, view.CashBalance);
    }
}
