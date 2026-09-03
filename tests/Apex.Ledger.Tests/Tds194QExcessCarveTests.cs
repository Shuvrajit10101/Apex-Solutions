using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Xunit;
using Domain = Apex.Ledger.Domain;

namespace Apex.Ledger.Tests;

/// <summary>
/// <b>T0-1 — §194Q is charged on the value EXCEEDING ₹50 lakh, never on the whole transaction.</b>
/// <para>
/// Statute: Income-tax Act 1961 <b>§194Q(1)</b> — a buyer whose purchases from a resident seller exceed fifty lakh
/// rupees in a previous year shall deduct an amount equal to <b>0.1 per cent of such sum exceeding fifty lakh
/// rupees</b> as income-tax. The charge is therefore on the EXCESS, not on the gross bill that crossed the gate.
/// The product's own TCS twin already implements exactly this arithmetic for the mirror section §206C(1H) — see
/// <see cref="TcsService"/>'s <c>ChargeableBase</c>, whose comment calls §194Q "the mirror" — so before this file
/// the two sibling engines disagreed about the same statutory shape.
/// </para>
/// <para>
/// 🔴 <b>The limb trap this file also pins.</b> §194C carries TWO limbs — a ₹30,000 single-transaction threshold and
/// a ₹1,00,000 cumulative-FY one. Carving the excess against the CUMULATIVE limb of a bill that became liable
/// through the SINGLE limb yields a negative excess, clamps to zero, and returns <b>₹0 TDS on a liable bill</b>.
/// The carve is therefore limb-aware and section-gated, and
/// <see cref="Section_194C_single_limb_bill_still_deducts_on_the_full_value"/> fails if either guard is removed.
/// </para>
/// </summary>
public class Tds194QExcessCarveTests
{
    private const string ValidTan = "MUMA12345B";
    private const string DeducteePan = "AAPFU0939F";

    private static readonly DateOnly Fy = new(2025, 4, 1);
    private static readonly DateOnly D1 = new(2025, 5, 10);
    private static readonly DateOnly D2 = new(2025, 6, 14);
    private static readonly DateOnly D3 = new(2025, 7, 21);

    private static Company NewTdsCompany()
    {
        var c = CompanyFactory.CreateSeeded("Excess Carve Co", Fy);
        new TdsTcsService(c).EnableTds(new TdsConfig { Tan = ValidTan });
        return c;
    }

    private static Domain.Ledger AddLedger(Company c, string name, string groupName, bool openingIsDebit)
    {
        var l = new Domain.Ledger(Guid.NewGuid(), name, c.FindGroupByName(groupName)!.Id, Money.Zero, openingIsDebit);
        c.AddLedger(l);
        return l;
    }

    private static Domain.Ledger Seller(Company c, string sectionCode, string? pan)
    {
        var nop = c.FindNatureOfPaymentByCode(sectionCode)!;
        var v = AddLedger(c, $"Seller-{Guid.NewGuid():N}", "Sundry Creditors", false);
        v.TdsApplicable = true; v.TdsNatureOfPaymentId = nop.Id; v.DeducteeType = DeducteeType.Company; v.PartyPan = pan;
        return v;
    }

    private static Guid PurchaseTypeId(Company c) => c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase).Id;

    /// <summary>
    /// 🔴 THE CONSTRUCTED FAILURE. A single ₹60,00,000 purchase of goods from a PAN-holding resident seller, the
    /// first of the financial year. §194Q charges 0.1% of the sum EXCEEDING ₹50,00,000 = 0.1% × ₹10,00,000 =
    /// <b>₹1,000</b>. Before the fix the engine returned <b>₹6,000</b> (0.1% × the whole ₹60,00,000) — an
    /// over-deduction of exactly ₹5,000 that the deductor owes the deductee.
    /// </summary>
    [Fact]
    public void Section_194Q_on_a_sixty_lakh_purchase_deducts_one_thousand_not_six_thousand()
    {
        var c = NewTdsCompany();
        var seller = Seller(c, "194Q", DeducteePan);
        var nop = c.FindNatureOfPaymentByCode("194Q")!;

        var bill = Money.FromRupees(60_00_000m);
        var w = new TdsService(c).ComputeWithholding(bill, nop, seller, D1);

        Assert.True(w.Applies);
        Assert.Equal(10, w.RateBasisPoints);                            // 0.1%
        Assert.Equal(Money.FromRupees(60_00_000m), w.AssessableValue);  // the FULL value is still recorded
        Assert.Equal(Money.FromRupees(1_000m), w.TdsAmount);            // 0.1% of the ₹10,00,000 EXCESS
        Assert.NotEqual(Money.FromRupees(6_000m), w.TdsAmount);         // the pre-fix figure
    }

    /// <summary>
    /// The carve-out legs a posted voucher actually carries: the seller is credited ₹59,99,000 and TDS Payable
    /// ₹1,000, and the two foot to the ₹60,00,000 gross to the paisa. Before the fix the seller was short-credited
    /// by ₹5,000 (₹59,94,000).
    /// </summary>
    [Fact]
    public void Section_194Q_carve_out_credits_the_seller_the_excess_based_net()
    {
        var c = NewTdsCompany();
        var purchases = AddLedger(c, "Purchases", "Purchase Accounts", true);
        var seller = Seller(c, "194Q", DeducteePan);
        var nop = c.FindNatureOfPaymentByCode("194Q")!;

        var gross = Money.FromRupees(60_00_000m);
        var carve = new TdsService(c).BuildCarveOut(gross, gross, nop, seller, D1);

        Assert.True(carve.Applies);
        Assert.Equal(Money.FromRupees(1_000m), carve.TdsAmount);
        Assert.Equal(Money.FromRupees(59_99_000m), carve.NetPartyAmount);
        Assert.Equal(gross, carve.NetPartyAmount + carve.TdsAmount);

        var v = new LedgerService(c).Post(new Voucher(Guid.NewGuid(), PurchaseTypeId(c), D1,
            new[] { new EntryLine(purchases.Id, gross, DrCr.Debit), carve.PartyLine, carve.TdsPayableLine! }));
        Assert.True(VoucherValidator.IsBalanced(v));
    }

    /// <summary>
    /// The straddling bill and the compounding the census names. Three purchases in one FY: ₹40,00,000 (below the
    /// gate — no TDS), ₹30,00,000 (the bill that straddles ₹50 lakh: only ₹20,00,000 of it is above, so ₹2,000),
    /// then ₹10,00,000 (entirely above the gate, so the excess clamps to the whole bill: ₹1,000). Pre-fix the
    /// second bill withheld ₹3,000 — ₹1,000 too much — while the third was already right, which is why a
    /// presence check or a single-case test would have passed.
    /// </summary>
    [Fact]
    public void Section_194Q_straddling_bill_charges_only_the_part_above_fifty_lakh()
    {
        var c = NewTdsCompany();
        var purchases = AddLedger(c, "Purchases", "Purchase Accounts", true);
        var seller = Seller(c, "194Q", DeducteePan);
        var nop = c.FindNatureOfPaymentByCode("194Q")!;
        var svc = new TdsService(c);
        var posting = new LedgerService(c);

        void PostBill(Money amount, DateOnly on)
        {
            var carve = svc.BuildCarveOut(amount, amount, nop, seller, on);
            var lines = new List<EntryLine> { new(purchases.Id, amount, DrCr.Debit), carve.PartyLine };
            if (carve.TdsPayableLine is { } t) lines.Add(t);
            posting.Post(new Voucher(Guid.NewGuid(), PurchaseTypeId(c), on, lines));
        }

        // Bill 1 — ₹40,00,000, below the ₹50,00,000 gate: nothing withheld.
        var b1 = svc.ComputeWithholding(Money.FromRupees(40_00_000m), nop, seller, D1);
        Assert.False(b1.Applies);
        Assert.Equal(Money.Zero, b1.TdsAmount);
        PostBill(Money.FromRupees(40_00_000m), D1);

        // Bill 2 — ₹30,00,000 straddles the gate: cumulative ₹70,00,000, excess ₹20,00,000 ⇒ ₹2,000 (was ₹3,000).
        var b2 = svc.ComputeWithholding(Money.FromRupees(30_00_000m), nop, seller, D2);
        Assert.True(b2.Applies);
        Assert.Equal(Money.FromRupees(40_00_000m), b2.PriorCumulativeInFy);
        Assert.Equal(Money.FromRupees(2_000m), b2.TdsAmount);
        Assert.NotEqual(Money.FromRupees(3_000m), b2.TdsAmount);
        PostBill(Money.FromRupees(30_00_000m), D2);

        // Bill 3 — ₹10,00,000 wholly above the gate: the excess clamps to the bill ⇒ ₹1,000, unchanged by the fix.
        var b3 = svc.ComputeWithholding(Money.FromRupees(10_00_000m), nop, seller, D3);
        Assert.True(b3.Applies);
        Assert.Equal(Money.FromRupees(70_00_000m), b3.PriorCumulativeInFy);
        Assert.Equal(Money.FromRupees(1_000m), b3.TdsAmount);
    }

    /// <summary>
    /// No PAN ⇒ the §206AA second-proviso §194Q cap of 5% — and it too applies to the EXCESS only:
    /// 5% × ₹10,00,000 = ₹50,000, not 5% × ₹60,00,000 = ₹3,00,000.
    /// </summary>
    [Fact]
    public void Section_194Q_no_pan_five_percent_also_charges_only_the_excess()
    {
        var c = NewTdsCompany();
        var seller = Seller(c, "194Q", pan: null);
        var nop = c.FindNatureOfPaymentByCode("194Q")!;

        var w = new TdsService(c).ComputeWithholding(Money.FromRupees(60_00_000m), nop, seller, D1);

        Assert.False(w.PanApplied);
        Assert.Equal(500, w.RateBasisPoints);
        Assert.Equal(Money.FromRupees(50_000m), w.TdsAmount);
        Assert.NotEqual(Money.FromRupees(3_00_000m), w.TdsAmount);
    }

    /// <summary>
    /// 🔴 THE LIMB GUARD. §194C's ₹50,000 bill is liable through its ₹30,000 SINGLE-transaction limb while the
    /// FY cumulative (₹1,00,000) is nowhere near crossed. TDS is charged on the FULL ₹50,000. If the
    /// excess carve were applied to every threshold section (or against the cumulative limb regardless of which
    /// limb fired) this bill would compute (0 + 50,000 − 1,00,000) → clamp 0 → <b>₹0 on a liable bill</b>.
    /// <para>
    /// 🔴 <b>THIS TEST USED TO ASSERT THE WRONG MONEY, AND IT IS CORRECTED HERE, NOT RELAXED.</b> The
    /// <see cref="Seller"/> fixture makes the contractor a <c>DeducteeType.Company</c>, and §194C(1)(ii) charges a
    /// person other than an individual or a HUF at <b>2%</b> — <b>₹1,000.00</b>. It asserted <c>100</c> bp and
    /// <b>₹500.00</b> because <c>ComputeWithholding</c> read <c>Ledger.DeducteeType</c> nowhere; the limb it exists
    /// to guard is unchanged, only the arm of the rate it lands on. See <see cref="Tds194CDeducteeTypeTests"/>.
    /// </para>
    /// </summary>
    [Fact]
    public void Section_194C_single_limb_bill_still_deducts_on_the_full_value()
    {
        var c = NewTdsCompany();
        var contractor = Seller(c, "194C", DeducteePan);   // DeducteeType.Company ⇒ §194C(1)(ii) 2%
        var nop = c.FindNatureOfPaymentByCode("194C")!;

        var w = new TdsService(c).ComputeWithholding(Money.FromRupees(50_000m), nop, contractor, D1);

        Assert.True(w.Applies);
        Assert.Equal(200, w.RateBasisPoints);
        Assert.Equal(Money.FromRupees(1_000m), w.TdsAmount);   // 2% of the FULL 50,000, not of a cumulative excess
        Assert.NotEqual(Money.Zero, w.TdsAmount);
    }

    /// <summary>
    /// §194J(b) — a plain cumulative-threshold section that is NOT excess-charging: once the ₹50,000 FY aggregate
    /// is exceeded the whole bill is charged. ₹1,00,000 ⇒ ₹10,000, not 10% of some ₹50,000 excess (₹5,000).
    /// This is the regression the §194Q carve must not cause.
    /// </summary>
    [Fact]
    public void Section_194J_cumulative_section_still_deducts_on_the_full_value()
    {
        var c = NewTdsCompany();
        var vendor = Seller(c, "194J(b)", DeducteePan);
        var nop = c.FindNatureOfPaymentByCode("194J(b)")!;

        var w = new TdsService(c).ComputeWithholding(Money.FromRupees(1_00_000m), nop, vendor, D1);

        Assert.True(w.Applies);
        Assert.Equal(Money.FromRupees(10_000m), w.TdsAmount);
        Assert.NotEqual(Money.FromRupees(5_000m), w.TdsAmount);
    }

    /// <summary>
    /// 🔴 THE LIMB GUARD, MADE LIVE. The seeded §194Q has no single-transaction limb, so on the seeded set alone the
    /// limb guard inside <c>ChargeableBase</c> could never fire and would be dead code. It IS reachable: the Nature
    /// of Payment master is user-authorable, so an operator can hand-author a §194Q row carrying BOTH a ₹30,000
    /// single-transaction limb and the ₹50,00,000 cumulative one. A ₹40,000 bill against it is liable through the
    /// SINGLE limb while the cumulative is ₹49,60,000 away; the cumulative excess is (0 + 40,000) − 50,00,000 =
    /// −49,60,000, which clamps to ₹0 and would withhold <b>NOTHING on a liable bill</b>. The guard refuses the carve
    /// on that branch and charges the full ₹40,000 ⇒ ₹40.
    /// <para><b>OURS BY DESIGN, and labelled as such.</b> No statute and no corpus page describes a two-limb §194Q —
    /// this configuration is only reachable because the master is editable. The chosen behaviour (refuse the carve,
    /// charge the full value) is deliberately the conservative one: never under-withhold on a bill the gate has
    /// already declared liable. It is NOT a narrowing of any attested TallyPrime behaviour.</para>
    /// </summary>
    [Fact]
    public void A_hand_authored_two_limb_194Q_liable_through_its_single_limb_charges_the_full_value()
    {
        var c = NewTdsCompany();
        var seller = Seller(c, "194Q", DeducteePan);

        // Hand-authored §194Q master with BOTH limbs — the shape only a user can create.
        var twoLimb = new NatureOfPayment(
            Guid.NewGuid(), "194Q", "Purchase of goods (hand-authored, two limbs)", 10, 500, "94Q",
            singleTransactionThreshold: Money.FromRupees(30_000m),
            cumulativeThreshold: Money.FromRupees(50_00_000m));
        c.Tds!.AddNatureOfPayment(twoLimb);

        Assert.True(twoLimb.ChargesOnlyExcessOverCumulativeThreshold);

        var w = new TdsService(c).ComputeWithholding(Money.FromRupees(40_000m), twoLimb, seller, D1);

        Assert.True(w.Applies);                                  // liable via the ₹30,000 single limb
        Assert.Equal(Money.FromRupees(40m), w.TdsAmount);        // 0.1% of the FULL ₹40,000
        Assert.NotEqual(Money.Zero, w.TdsAmount);                // the unguarded carve would return ₹0 here
    }

    /// <summary>
    /// The predicate itself: exactly one seeded Nature of Payment charges on the excess, and it is §194Q. A future
    /// seed row that quietly acquired the behaviour — or lost it — reddens here.
    /// </summary>
    [Fact]
    public void Exactly_one_seeded_nature_of_payment_charges_on_the_excess_and_it_is_194Q()
    {
        var c = NewTdsCompany();
        var excessCharging = c.NaturesOfPayment.Where(n => n.ChargesOnlyExcessOverCumulativeThreshold).ToList();

        Assert.Single(excessCharging);
        Assert.Equal("194Q", excessCharging[0].SectionCode);
    }
}
