using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// Phase 9 slice 7a — GST payment (RQ-22; A14-CONFIRMED §11.2/§11.3). Proves PMT-06 deposit + cash discharge, the
/// DRC-03 voluntary payment + record, and the two HARD invariants the poster rejects fail-fast: cash minor-head
/// isolation (a Tax deposit cannot discharge an Interest liability) and the credit TAX-ONLY rule (the credit ledger
/// never settles interest / penalty / fee).
/// </summary>
public sealed class GstPaymentTests
{
    private static readonly DateOnly FyStart = new(2024, 4, 1);
    private static readonly DateOnly D = new(2024, 4, 15);

    private static (Company Company, GstService Gst, Domain.Ledger Bank) NewCompany()
    {
        var c = CompanyFactory.CreateSeeded("Pay Co", FyStart);
        var gst = new GstService(c);
        gst.EnableGst(new GstConfig
        {
            HomeStateCode = "27", Gstin = "27AAPFU0939F1ZV", RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart, Periodicity = GstReturnPeriodicity.Monthly,
        });
        var bank = new Domain.Ledger(Guid.NewGuid(), "Bank", c.FindGroupByName("Bank Accounts")!.Id, Money.Zero, true);
        c.AddLedger(bank);
        return (c, gst, bank);
    }

    // ---------------------------------------------------------------- 10. PMT-06 deposit + discharge

    [Fact]
    public void Pmt06_deposits_into_the_cash_ledger_records_the_challan_and_the_discharge_draws_it_down()
    {
        var (c, gst, bank) = NewCompany();
        var deposit = new GstDepositService(c);

        var (dep, challan) = deposit.PostPmt06(
            GstTaxHead.Central, GstMinorHead.Tax, Money.FromRupees(900m), bank, D, "24CPIN0000001234", "CIN123", "BRN999");
        Assert.True(VoucherValidator.IsBalanced(dep));           // Dr Cash Ledger / Cr Bank
        Assert.Equal(challan.VoucherId, dep.Id);                  // challan links to its deposit voucher
        Assert.Equal("24CPIN0000001234", challan.Cpin);
        Assert.Equal("CIN123", challan.Cin);
        Assert.Equal(GstMinorHead.Tax, challan.MinorHead);

        var cash = c.FindLedgerByName(GstService.ElectronicCashLedgerName)!;
        Assert.Equal(900m, LedgerBalances.SignedClosing(c, cash, D)); // ₹900 sitting in the cash ledger

        var discharge = deposit.PostCashDischarge(GstTaxHead.Central, Money.FromRupees(900m), D);
        Assert.True(VoucherValidator.IsBalanced(discharge));      // Dr Output CGST / Cr Cash Ledger
        Assert.Equal(0m, LedgerBalances.SignedClosing(c, cash, D)); // drawn back to zero
    }

    [Fact]
    public void Pmt06_requires_a_cin_because_the_cash_ledger_is_credited_only_on_cin()
    {
        var (c, _, bank) = NewCompany();
        var deposit = new GstDepositService(c);
        Assert.Throws<InvalidOperationException>(() =>
            deposit.PostPmt06(GstTaxHead.Central, GstMinorHead.Tax, Money.FromRupees(900m), bank, D, "CPIN", cin: ""));
    }

    // ---------------------------------------------------------------- 11. DRC-03 voluntary / off-return

    [Fact]
    public void Drc03_posts_the_payment_and_records_the_cause_period_and_flag_only_interest()
    {
        var (c, _, bank) = NewCompany();
        var deposit = new GstDepositService(c);

        var (v, rec) = deposit.PostDrc03(
            "Rule 37 — 180-day non-payment", "2024-04", D,
            cgstPaisa: 9000, sgstPaisa: 9000, igstPaisa: 0, cessPaisa: 0, interestPaisa: 500,
            GstDepositService.PaymentMethod.Bank, bank, drc03Ref: "AD2404240000123");

        Assert.True(VoucherValidator.IsBalanced(v));
        Assert.Single(c.GstDrc03s);
        Assert.Equal("Rule 37 — 180-day non-payment", rec.Cause);
        Assert.Equal("2024-04", rec.Period);
        Assert.Equal(18000, rec.TotalTaxPaisa);
        Assert.Equal(500, rec.InterestPaisa);   // §50 interest is a passed field, never auto-computed (18% if surfaced)
        Assert.Equal(v.Id, rec.VoucherId);
    }

    // ---------------------------------------------------------------- 12. cash minor-head isolation + credit tax-only

    [Fact]
    public void Cash_deposited_under_tax_cannot_discharge_an_interest_head()
    {
        var (c, _, bank) = NewCompany();
        var deposit = new GstDepositService(c);
        // Deposit ₹1000 under (CGST, Tax) only.
        deposit.PostPmt06(GstTaxHead.Central, GstMinorHead.Tax, Money.FromRupees(1000m), bank, D, "CPIN", "CIN");

        // Paying an Interest liability from cash requires (CGST/…, Interest) cash — there is none ⇒ reject.
        Assert.Throws<InvalidOperationException>(() =>
            deposit.PostDrc03("interest", "2024-04", D, 0, 0, 0, 0, interestPaisa: 10000,
                GstDepositService.PaymentMethod.Cash));
    }

    [Fact]
    public void The_credit_ledger_can_never_settle_interest_penalty_or_fee_tax_only()
    {
        // Direct rule (§49(4) / Rule 86(2)).
        Assert.Throws<InvalidOperationException>(() => GstDepositService.EnsureCreditCanSettle(GstMinorHead.Interest));
        Assert.Throws<InvalidOperationException>(() => GstDepositService.EnsureCreditCanSettle(GstMinorHead.Penalty));
        Assert.Throws<InvalidOperationException>(() => GstDepositService.EnsureCreditCanSettle(GstMinorHead.Fee));
        GstDepositService.EnsureCreditCanSettle(GstMinorHead.Tax); // Tax is fine (no throw)

        // And a DRC-03 that tries to discharge interest from CREDIT is rejected by the poster.
        var (c, _, _) = NewCompany();
        Assert.Throws<InvalidOperationException>(() => new GstDepositService(c).PostDrc03(
            "voluntary", "2024-04", D, 5000, 0, 0, 0, interestPaisa: 1000,
            GstDepositService.PaymentMethod.Credit));
    }

    // ------------------------------------------------ 13. FIX 1 (HIGH) — a cash-funded DRC-03 draws the cash ledger
    //  Regression: a DRC-03 cash payment must NET its (major, minor) cell so the same deposit cannot be drawn twice
    //  and the Electronic Cash Ledger (a Current-Asset) can never be overdrawn to a credit balance.

    [Fact]
    public void A_cash_funded_drc03_draws_down_the_cash_cell_and_cannot_be_double_spent()
    {
        var (c, _, bank) = NewCompany();
        var deposit = new GstDepositService(c);
        var cash = new GstService(c).EnsureElectronicCashLedger();

        // Deposit ₹1000 into (CGST, Tax).
        deposit.PostPmt06(GstTaxHead.Central, GstMinorHead.Tax, Money.FromRupees(1000m), bank, D, "24CPIN00000001000", "CIN1000");
        Assert.Equal(1000m, deposit.AvailableCash(GstTaxHead.Central, GstMinorHead.Tax).Amount);

        // One cash-funded DRC-03 for the whole ₹1000 CGST tax draws the (CGST, Tax) cell to zero.
        deposit.PostDrc03("voluntary", "2024-04", D, cgstPaisa: 100000, sgstPaisa: 0, igstPaisa: 0, cessPaisa: 0,
            interestPaisa: 0, GstDepositService.PaymentMethod.Cash);
        Assert.Equal(0m, deposit.AvailableCash(GstTaxHead.Central, GstMinorHead.Tax).Amount); // FAILS pre-fix (returns 1000)

        // A SECOND identical cash DRC-03 has no unutilised cash left — it MUST be refused (no double-spend).
        Assert.Throws<InvalidOperationException>(() =>                                        // FAILS pre-fix (it posts)
            deposit.PostDrc03("voluntary again", "2024-04", D, cgstPaisa: 100000, sgstPaisa: 0, igstPaisa: 0,
                cessPaisa: 0, interestPaisa: 0, GstDepositService.PaymentMethod.Cash));

        // The Electronic Cash Ledger (debit-normal Current-Asset) never goes to a credit (negative signed) balance.
        Assert.True(LedgerBalances.SignedClosing(c, cash, D) >= 0m);                          // FAILS pre-fix (goes to −1000)
    }

    [Fact]
    public void The_cash_cell_projection_foots_to_the_real_cash_ledger_balance_after_a_cash_drc03()
    {
        var (c, _, bank) = NewCompany();
        var deposit = new GstDepositService(c);
        var cash = new GstService(c).EnsureElectronicCashLedger();

        deposit.PostPmt06(GstTaxHead.Central, GstMinorHead.Tax, Money.FromRupees(1000m), bank, D, "24CPIN00000002000", "CIN2000");
        deposit.PostDrc03("voluntary", "2024-04", D, cgstPaisa: 100000, sgstPaisa: 0, igstPaisa: 0, cessPaisa: 0,
            interestPaisa: 0, GstDepositService.PaymentMethod.Cash);

        var view = ElectronicLedgersView.Build(c, FyStart, D);
        var cell = view.CashCells[(GstTaxHead.Central, GstMinorHead.Tax)];
        var realBalance = new Money(LedgerBalances.SignedClosing(c, cash, D));
        Assert.Equal(realBalance, cell);            // (CGST, Tax) cell foots to the ledger. FAILS pre-fix (1000 vs 0)
        Assert.Equal(view.CashBalance, cell);       // and to the reported cash balance.
    }

    [Fact]
    public void A_cash_funded_drc03_on_an_interest_deposit_nets_the_interest_cell_not_the_tax_cell()
    {
        var (c, _, bank) = NewCompany();
        var deposit = new GstDepositService(c);

        // Deposit ₹500 into (IGST, Interest) AND ₹700 into (IGST, Tax) — same major head, different minor heads.
        deposit.PostPmt06(GstTaxHead.Integrated, GstMinorHead.Interest, Money.FromRupees(500m), bank, D, "24CPIN00000003001", "CINI");
        deposit.PostPmt06(GstTaxHead.Integrated, GstMinorHead.Tax, Money.FromRupees(700m), bank, D, "24CPIN00000003002", "CINT");

        // A cash DRC-03 paying ₹500 IGST interest must net ONLY the (IGST, Interest) cell (the dropped minor==Tax gate).
        deposit.PostDrc03("interest", "2024-04", D, cgstPaisa: 0, sgstPaisa: 0, igstPaisa: 0, cessPaisa: 0,
            interestPaisa: 50000, GstDepositService.PaymentMethod.Cash);

        Assert.Equal(0m, deposit.AvailableCash(GstTaxHead.Integrated, GstMinorHead.Interest).Amount); // netted (FAILS pre-fix)
        Assert.Equal(700m, deposit.AvailableCash(GstTaxHead.Integrated, GstMinorHead.Tax).Amount);    // untouched (no cross-minor bleed)
    }

    // ------------------------------------------------ FIX 2 (LOW) — a credit-funded DRC-03 mis-tags the Input reduction
    //  Regression: the credit-ledger movement decomposition (ElectronicLedgersView) must FOOT to the closing Input
    //  balance (Σ movements == closing); a CashPayment tag hid the reduction from InputMovement so it no longer footed.

    [Fact]
    public void A_credit_funded_drc03_input_reduction_foots_the_credit_pool_decomposition()
    {
        var (c, gst, bank) = NewCompany();
        var deposit = new GstDepositService(c);

        // Accrue ₹900 CGST + ₹900 SGST input credit via an intra-state purchase (₹10 000 @18%).
        AccrueIntraPurchaseItc(c, gst, bank, 10000m);

        // A credit-funded DRC-03 discharges ₹400 CGST from the electronic credit ledger (Cr Input CGST).
        deposit.PostDrc03("voluntary — credit", "2024-04", D, cgstPaisa: 40000, sgstPaisa: 0, igstPaisa: 0,
            cessPaisa: 0, interestPaisa: 0, GstDepositService.PaymentMethod.Credit);

        var view = ElectronicLedgersView.Build(c, FyStart, D);
        // The credit-pool decomposition must foot: closing == additions − utilised − reversed.
        Assert.Equal(
            view.CreditAdditionsCgst.Amount - view.CreditUtilisedCgst.Amount - view.CreditReversedCgst.Amount,
            view.CreditCgst.Amount);                 // FAILS pre-fix: 900 − 0 − 0 = 900 ≠ closing 500
        Assert.Equal(500m, view.CreditCgst.Amount);  // ₹900 accrued − ₹400 discharged
        Assert.Equal(400m, view.CreditUtilisedCgst.Amount); // the DRC-03 credit draw shows as utilisation, not lost
    }

    /// <summary>Accrues Input CGST+SGST credit by posting an intra-state purchase of <paramref name="taxable"/> @18%.</summary>
    private static void AccrueIntraPurchaseItc(Company c, GstService gst, Domain.Ledger bank, decimal taxable)
    {
        var purchases = new Domain.Ledger(Guid.NewGuid(), "Purchases", c.FindGroupByName("Purchase Accounts")!.Id, Money.Zero, true);
        c.AddLedger(purchases);
        var tax = gst.ComputeInvoiceTax(
            new[] { new GstService.TaxableLine(Money.FromRupees(taxable), 1800) }, interState: false, GstTaxDirection.Input);
        var total = taxable + tax.TotalTax.Amount;
        var lines = new List<EntryLine>
        {
            new(purchases.Id, Money.FromRupees(taxable), DrCr.Debit),
            new(bank.Id, Money.FromRupees(total), DrCr.Credit),
        };
        lines.AddRange(tax.TaxLines);
        var type = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase).Id;
        new LedgerService(c).Post(new Voucher(Guid.NewGuid(), type, D, lines));
    }
}
