using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;
using Domain = Apex.Ledger.Domain;

namespace Apex.Ledger.Tests;

/// <summary>
/// Phase 7 slice 2 — the TDS <b>compute + auto-deduct</b> engine (<see cref="TdsService"/>). Pure, deterministic,
/// paisa/rupee-exact. Proves the LOAD-BEARING contract: TDS <b>withholds</b> from the party leg — compute-once
/// (nearest rupee, round-half-up) then <b>derive</b> the party net = gross − TDS — so <c>Dr Expense (GROSS) == Cr
/// Party (NET) + Cr TDS Payable (TDS)</c> to the paisa BY CONSTRUCTION, and <see cref="VoucherValidator"/> accepts
/// the carve-out (the TDS Payable D&amp;T credit + net party credit foot to the gross), while a leaky
/// independently-computed net is rejected by the balance invariant. Also: PAN⇒with-PAN / no-PAN⇒§206AA-20%/§194Q-5%;
/// single + cumulative-FY threshold as a pure projection; TDS assessed on the GST-exclusive base (Circular
/// 23/2017); and the S1 carry-forward fixes (§194A ₹10,000 generic default; payable relocated under Duties &amp; Taxes).
/// </summary>
public class TdsServiceTests
{
    private const string ValidTan = "MUMA12345B";
    private const string DeducteePan = "AAPFU0939F";

    private static readonly DateOnly Fy = new(2025, 4, 1);
    private static readonly DateOnly D1 = new(2025, 5, 10);

    private static Company NewTdsCompany()
    {
        var c = CompanyFactory.CreateSeeded("Withholding Co", Fy);
        new TdsTcsService(c).EnableTds(new TdsConfig { Tan = ValidTan });
        return c;
    }

    private static Domain.Ledger AddLedger(Company c, string name, string groupName, bool openingIsDebit)
    {
        var l = new Domain.Ledger(Guid.NewGuid(), name, c.FindGroupByName(groupName)!.Id, Money.Zero, openingIsDebit);
        c.AddLedger(l);
        return l;
    }

    private static Domain.Ledger Vendor(Company c, string? pan)
    {
        var nop = c.FindNatureOfPaymentByCode("194J(b)")!;
        var v = AddLedger(c, $"Vendor-{Guid.NewGuid():N}", "Sundry Creditors", false);
        v.TdsApplicable = true; v.TdsNatureOfPaymentId = nop.Id; v.DeducteeType = DeducteeType.Firm; v.PartyPan = pan;
        return v;
    }

    private static Guid JournalTypeId(Company c) => c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Journal).Id;

    // ---- the golden worked example: 194J ₹1,00,000 with PAN → gross = net + TDS to the paisa ----

    [Fact]
    public void Worked_194J_deduction_conserves_gross_exactly()
    {
        var c = NewTdsCompany();
        var fees = AddLedger(c, "Professional Fees", "Indirect Expenses", true);
        var vendor = Vendor(c, DeducteePan);
        var nop = c.FindNatureOfPaymentByCode("194J(b)")!;

        var gross = Money.FromRupees(1_00_000m);
        var carve = new TdsService(c).BuildCarveOut(gross, gross, nop, vendor, D1);

        Assert.True(carve.Applies);
        Assert.Equal(Money.FromRupees(10_000m), carve.TdsAmount);      // 10% of 1,00,000
        Assert.Equal(Money.FromRupees(90_000m), carve.NetPartyAmount); // derived: 1,00,000 − 10,000
        Assert.True(carve.Withholding.PanApplied);

        // Dr Fees GROSS / Cr Vendor NET / Cr TDS Payable TDS.
        var v = new LedgerService(c).Post(new Voucher(Guid.NewGuid(), JournalTypeId(c), D1,
            new[] { new EntryLine(fees.Id, gross, DrCr.Debit), carve.PartyLine, carve.TdsPayableLine! }));

        Assert.True(VoucherValidator.IsBalanced(v));
        // GROSS Dr == NET Cr + TDS Cr, to the paisa — the load-bearing invariant.
        Assert.Equal(gross, carve.NetPartyAmount + carve.TdsAmount);
        // The TDS Payable ledger accrues the withheld amount (credit → liability).
        var payable = new TdsService(c).RequirePayableLedger();
        Assert.Equal(-10_000m, LedgerBalances.SignedClosing(c, payable, D1));
    }

    [Fact]
    public void No_pan_194J_withholds_20_percent_under_206AA()
    {
        var c = NewTdsCompany();
        var vendor = Vendor(c, pan: null); // no PAN → §206AA 20%
        var nop = c.FindNatureOfPaymentByCode("194J(b)")!;
        var carve = new TdsService(c).BuildCarveOut(Money.FromRupees(1_00_000m), Money.FromRupees(1_00_000m), nop, vendor, D1);

        Assert.False(carve.Withholding.PanApplied);
        Assert.Equal(2000, carve.Withholding.RateBasisPoints);
        Assert.Equal(Money.FromRupees(20_000m), carve.TdsAmount);
        Assert.Equal(Money.FromRupees(80_000m), carve.NetPartyAmount);
    }

    [Theory]
    [InlineData(DeducteePan, 10, 10_000)]  // with PAN: 194Q 0.1% of 1 crore = 10,000
    [InlineData(null, 500, 5_00_000)]       // no PAN: §194Q special cap 5% (NOT 20%) = 5,00,000
    public void Section_194Q_resolves_special_no_pan_cap(string? pan, int expectedRateBp, decimal expectedTds)
    {
        var c = NewTdsCompany();
        var nop = c.FindNatureOfPaymentByCode("194Q")!;
        var buyer = AddLedger(c, "Goods Seller", "Sundry Creditors", false);
        buyer.TdsApplicable = true; buyer.TdsNatureOfPaymentId = nop.Id; buyer.PartyPan = pan;

        // Assessable above the ₹50 lakh cumulative threshold (uniform rule: TDS on the full current assessable —
        // §194Q's "only on the value exceeding ₹50 lakh" base is a documented later refinement).
        var w = new TdsService(c).ComputeWithholding(Money.FromRupees(1_00_00_000m), nop, buyer, D1);
        Assert.True(w.Applies);
        Assert.Equal(expectedRateBp, w.RateBasisPoints);
        Assert.Equal(Money.FromRupees(expectedTds), w.TdsAmount);
    }

    // ---- threshold: below / at / crossing (cumulative-FY pure projection) ----

    [Fact]
    public void Cumulative_threshold_below_at_and_just_above()
    {
        var c = NewTdsCompany();
        var vendor = Vendor(c, DeducteePan);
        var nop = c.FindNatureOfPaymentByCode("194J(b)")!; // cumulative ₹50,000, no single threshold
        var svc = new TdsService(c);

        Assert.False(svc.ComputeWithholding(Money.FromRupees(50_000m), nop, vendor, D1).Applies);  // at exactly → no TDS
        var above = svc.ComputeWithholding(Money.FromRupees(50_001m), nop, vendor, D1);
        Assert.True(above.Applies);                                                                  // just above → TDS
        Assert.Equal(Money.FromRupees(5_000m), above.TdsAmount);                                     // round(50,001 × 10%) = 5,000
    }

    [Fact]
    public void Cumulative_threshold_crosses_across_two_posted_vouchers()
    {
        var c = NewTdsCompany();
        var fees = AddLedger(c, "Professional Fees", "Indirect Expenses", true);
        var vendor = Vendor(c, DeducteePan);
        var nop = c.FindNatureOfPaymentByCode("194J(b)")!;
        var svc = new TdsService(c);
        var post = new LedgerService(c);

        // Voucher A: ₹40,000 (below the ₹50,000 FY threshold) — no withholding, but the assessment is recorded.
        var a = svc.BuildCarveOut(Money.FromRupees(40_000m), Money.FromRupees(40_000m), nop, vendor, D1);
        Assert.False(a.Applies);
        Assert.Null(a.TdsPayableLine);
        post.Post(new Voucher(Guid.NewGuid(), JournalTypeId(c), D1,
            new[] { new EntryLine(fees.Id, Money.FromRupees(40_000m), DrCr.Debit), a.PartyLine }));

        // The prior projection now sees ₹40,000 for this party×nature in the FY.
        Assert.Equal(Money.FromRupees(40_000m), svc.ProjectPriorCumulative(vendor.Id, nop.Id, new DateOnly(2025, 6, 1)));

        // Voucher B: another ₹40,000 → FY aggregate 80,000 > 50,000 → TDS applies on the current ₹40,000.
        var b = svc.BuildCarveOut(Money.FromRupees(40_000m), Money.FromRupees(40_000m), nop, vendor, new DateOnly(2025, 6, 1));
        Assert.True(b.Applies);
        Assert.Equal(Money.FromRupees(4_000m), b.TdsAmount); // 10% of 40,000
    }

    [Fact]
    public void Projection_is_pure_ignores_cancelled_and_other_financial_years()
    {
        var c = NewTdsCompany();
        var fees = AddLedger(c, "Professional Fees", "Indirect Expenses", true);
        var vendor = Vendor(c, DeducteePan);
        var nop = c.FindNatureOfPaymentByCode("194J(b)")!;
        var svc = new TdsService(c);
        var post = new LedgerService(c);

        // A cancelled voucher must not count toward the cumulative.
        var carve = svc.BuildCarveOut(Money.FromRupees(30_000m), Money.FromRupees(30_000m), nop, vendor, D1);
        post.Post(new Voucher(Guid.NewGuid(), JournalTypeId(c), D1,
            new[] { new EntryLine(fees.Id, Money.FromRupees(30_000m), DrCr.Debit), carve.PartyLine })
        { Cancelled = true });

        Assert.Equal(Money.Zero, svc.ProjectPriorCumulative(vendor.Id, nop.Id, new DateOnly(2025, 12, 31)));
        // A next-FY date sees nothing from this FY.
        Assert.Equal(Money.Zero, svc.ProjectPriorCumulative(vendor.Id, nop.Id, new DateOnly(2026, 5, 1)));
    }

    // ---- TDS assessed on the GST-exclusive base (CBDT Circular 23/2017) ----

    [Fact]
    public void Tds_is_computed_on_the_gst_exclusive_base()
    {
        var c = NewTdsCompany();
        var vendor = Vendor(c, DeducteePan);
        var nop = c.FindNatureOfPaymentByCode("194J(b)")!;

        // Fee ₹1,00,000 + 18% GST ₹18,000 → party owes ₹1,18,000; TDS is on ₹1,00,000 only.
        var carve = new TdsService(c).BuildCarveOut(
            partyGrossObligation: Money.FromRupees(1_18_000m), assessableValue: Money.FromRupees(1_00_000m),
            nop, vendor, D1);

        Assert.Equal(Money.FromRupees(10_000m), carve.TdsAmount);        // 10% of 1,00,000 (NOT 1,18,000)
        Assert.Equal(Money.FromRupees(1_08_000m), carve.NetPartyAmount); // 1,18,000 − 10,000
    }

    // ---- carve-out on a Purchase item-invoice: pairing invariant still foots (TOP regression risk) ----

    [Fact]
    public void Purchase_item_invoice_carve_out_keeps_the_pairing_invariant()
    {
        var c = NewTdsCompany();
        var post = new LedgerService(c);
        var inv = new InventoryService(c);
        var item = inv.CreateStockItem("Widget", inv.CreateStockGroup("Goods").Id, inv.CreateSimpleUnit("Nos", "Numbers").Id);
        var main = c.MainLocation!.Id;
        var purchases = AddLedger(c, "Purchases", "Purchase Accounts", true);
        var supplier = AddLedger(c, "Contract Supplier", "Sundry Creditors", false);
        supplier.TdsApplicable = true;
        supplier.TdsNatureOfPaymentId = c.FindNatureOfPaymentByCode("194C")!.Id; // no single threshold hit here
        supplier.DeducteeType = DeducteeType.Company; supplier.PartyPan = DeducteePan;

        var nop = c.FindNatureOfPaymentByCode("194C")!;
        var gross = Money.FromRupees(2_00_000m); // > ₹1,00,000 194C aggregate ⇒ TDS applies at 1% (with-PAN Ind/HUF base)
        var carve = new TdsService(c).BuildCarveOut(gross, gross, nop, supplier, D1);
        Assert.True(carve.Applies);
        Assert.Equal(Money.FromRupees(2_000m), carve.TdsAmount); // 1% of 2,00,000

        var v = post.Post(new Voucher(Guid.NewGuid(), c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase).Id, D1,
            new[] { new EntryLine(purchases.Id, gross, DrCr.Debit), carve.PartyLine, carve.TdsPayableLine! },
            inventoryLines: new[] { new VoucherInventoryLine(item.Id, main, 100m, Money.FromRupees(2_000m)) }));

        Assert.True(VoucherValidator.IsBalanced(v));
        // Item lines Σ qty×rate == the Purchase (Stock-leg) debit GROSS — the pairing foots; the net party leg and
        // the TDS Payable (Duties & Taxes) credit are excluded from the pairing sum.
        Assert.Equal(gross, v.InventoryLinesValue);
    }

    // ---- the guard is REAL: a leaky independently-computed net is rejected by the balance invariant ----

    [Fact]
    public void Leaky_independently_computed_net_is_rejected_while_the_derived_carve_out_balances()
    {
        var c = NewTdsCompany();
        var fees = AddLedger(c, "Professional Fees", "Indirect Expenses", true);
        var vendor = Vendor(c, DeducteePan);
        var nop = c.FindNatureOfPaymentByCode("194J(b)")!;
        var post = new LedgerService(c);

        // A sub-rupee tail: ₹1,00,005 @ 10% ⇒ 10,000.50 ⇒ nearest-rupee (half-up) = ₹10,001.
        var gross = Money.FromRupees(1_00_005m);
        var carve = new TdsService(c).BuildCarveOut(gross, gross, nop, vendor, D1);
        Assert.Equal(Money.FromRupees(10_001m), carve.TdsAmount);
        Assert.Equal(Money.FromRupees(90_004m), carve.NetPartyAmount); // derived: gross − TDS, exact

        // The DERIVED carve-out balances and posts.
        var ok = post.Post(new Voucher(Guid.NewGuid(), JournalTypeId(c), D1,
            new[] { new EntryLine(fees.Id, gross, DrCr.Debit), carve.PartyLine, carve.TdsPayableLine! }));
        Assert.True(VoucherValidator.IsBalanced(ok));

        // A LEAKY net computed independently as gross × (1 − rate) = 90,004.50 with TDS 10,001 sums to 1,00,005.50
        // ≠ 1,00,005 — the balance invariant rejects it (no green suite can hide the pairing leak).
        var payable = new TdsService(c).RequirePayableLedger();
        var leakyNet = new Money(gross.Amount * (1m - 1000m / 10000m)).RoundToPaisa(); // 90,004.50 (independent, WRONG)
        Assert.Throws<UnbalancedVoucherException>(() => post.Post(new Voucher(Guid.NewGuid(), JournalTypeId(c), D1,
            new[]
            {
                new EntryLine(fees.Id, gross, DrCr.Debit),
                new EntryLine(vendor.Id, leakyNet, DrCr.Credit),
                new EntryLine(payable.Id, Money.FromRupees(10_001m), DrCr.Credit),
            })));
    }

    [Fact]
    public void Requires_tds_enabled_before_carving_out()
    {
        var c = CompanyFactory.CreateSeeded("No-TDS Co", Fy); // TDS not enabled
        Assert.Throws<InvalidOperationException>(() => new TdsService(c).RequirePayableLedger());
    }

    // ---- S1 carry-forward: payable pre-created under the WRONG group is relocated under Duties & Taxes ----

    [Fact]
    public void Payable_precreated_under_wrong_group_is_relocated_under_duties_and_taxes()
    {
        var c = CompanyFactory.CreateSeeded("Pre-created Co", Fy);
        // User pre-creates "TDS Payable" under Sundry Creditors (a wrong, incompatible primary group).
        var wrong = AddLedger(c, TdsTcsService.TdsPayableLedgerName, "Sundry Creditors", false);
        Assert.False(ClassificationRules.IsDutiesAndTaxesLedger(wrong, c));

        new TdsTcsService(c).EnableTds(new TdsConfig { Tan = ValidTan });

        // It is reused (not duplicated), tagged, AND relocated so the group-based classification now holds — else
        // the carve-out credit would be mis-counted in the item-invoice pairing.
        Assert.Single(c.Ledgers, l => l.Name == TdsTcsService.TdsPayableLedgerName);
        var payable = new TdsService(c).RequirePayableLedger();
        Assert.Same(wrong, payable);
        Assert.True(ClassificationRules.IsDutiesAndTaxesLedger(payable, c));
    }

    // ---- §194A generic default is ₹10,000 (S1 carry-forward), and drives the threshold ----

    [Fact]
    public void Section_194A_generic_threshold_is_ten_thousand()
    {
        var c = NewTdsCompany();
        var nop = c.FindNatureOfPaymentByCode("194A")!;
        Assert.Equal(Money.FromRupees(10_000m), nop.CumulativeThreshold);

        var lender = AddLedger(c, "Lender", "Sundry Creditors", false);
        lender.TdsApplicable = true; lender.TdsNatureOfPaymentId = nop.Id; lender.PartyPan = DeducteePan;
        var svc = new TdsService(c);
        Assert.False(svc.ComputeWithholding(Money.FromRupees(10_000m), nop, lender, D1).Applies); // at → no TDS
        Assert.True(svc.ComputeWithholding(Money.FromRupees(12_000m), nop, lender, D1).Applies);  // above → TDS
    }

    // ---- ER-13: a voucher with no TDS-applicable ledger carries no withholding detail ----

    [Fact]
    public void A_plain_voucher_carries_no_tds_detail()
    {
        var c = NewTdsCompany();
        var a = AddLedger(c, "Rent", "Indirect Expenses", true);
        var b = AddLedger(c, "Cash Box", "Cash-in-Hand", true);
        var v = new LedgerService(c).Post(new Voucher(Guid.NewGuid(), JournalTypeId(c), D1,
            new[] { new EntryLine(a.Id, Money.FromRupees(500m), DrCr.Debit), new EntryLine(b.Id, Money.FromRupees(500m), DrCr.Credit) }));
        Assert.All(v.Lines, l => Assert.False(l.HasTds));
    }
}
