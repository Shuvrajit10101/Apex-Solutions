using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// Census row <b>2.6 — Voucher Class</b>, the general machinery, at the engine level (schema v62): the vendor's
/// <b>Default Accounting Allocations</b> split, its <b>Additional Accounting Entries</b>, and the <b>rounding</b>
/// that has to balance the voucher.
///
/// <para>🔴 <b>WHY THESE TESTS ASSERT POSTED LEGS AND NOT "THE CLASS SAVED".</b> A voucher class writes ledger
/// entries automatically on every voucher that uses it, and it prompts nobody. A class that mis-splits an
/// allocation, mis-rounds a total or drops a round-off leg does not fail — it posts a wrong figure, quietly,
/// forever. So every test below names the exact legs and the exact paise, and several of them are constructed
/// from figures chosen because the NAIVE implementation gets them wrong.</para>
///
/// <para><b>R7 — ATTESTED.</b> <c>help.tallysolutions.com/tally-prime/accounting/voucher-types-tally/</c>
/// (the class and its <i>"Default Accounting Allocations for all items in Invoice"</i>);
/// <c>help.tallysolutions.com/tally-prime/importer-excise-masters/ei-configure-vch-class-customs-duty-tally/</c>
/// (the <b>Additional Accounting Entries</b> columns — Ledger Name, Type of Calculation, Value Basis, Rounding
/// Method, Rounding Limit, Remove if Zero); <c>help.tallysolutions.com/round-off-invoice-and-ledger-values/</c>
/// (the four rounding methods, the rounding limit as "the nearest multiple", and the round-off ledger carrying
/// <i>"the difference between the original and rounded amounts … automatically balancing the invoice"</i>).</para>
/// </summary>
public sealed class VoucherClassPostingTests
{
    private static readonly Guid SalesA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SalesB = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid SalesC = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid Freight = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid RoundOff = Guid.Parse("55555555-5555-5555-5555-555555555555");

    private static VoucherClass Class(string name = "Retail") => new(Guid.NewGuid(), name);

    private static VoucherClass WithAllocations(params (Guid Ledger, int Bp)[] rows)
    {
        var cls = Class();
        var i = 0;
        foreach (var (ledger, bp) in rows)
            cls.AddLedgerAllocation(new VoucherClassLedgerAllocation(Guid.NewGuid(), ledger, bp, i++));
        return cls;
    }

    // ═══════════════════════════════════════════════════ 1 · the allocation split must be EXACT

    /// <summary>
    /// 🔴 <b>THE PAISA THAT GOES MISSING.</b> Three ledgers at 33.33% / 33.33% / 33.34% of <b>₹1 000.01</b>. Each
    /// share rounded INDEPENDENTLY is 333.30 + 333.30 + 333.40 = <b>₹1 000.00</b> — a paisa short of the item
    /// value, which is an UNBALANCED VOUCHER posted with no prompt. The engine gives the LAST allocation the
    /// remainder instead, so the three legs total the item value exactly.
    ///
    /// <para>This is the test that fails on a naive implementation, and the figure is chosen for that: at a round
    /// ₹1 000.00 the independent rounding happens to come out right and proves nothing.</para>
    /// </summary>
    [Fact]
    public void Three_way_allocation_of_an_odd_amount_totals_the_item_value_to_the_paisa()
    {
        var cls = WithAllocations((SalesA, 3_333), (SalesB, 3_333), (SalesC, 3_334));

        var legs = VoucherClassPosting.Allocate(cls, Money.FromRupees(1_000.01m));

        Assert.Equal(3, legs.Count);
        Assert.Equal(333.30m, legs[0].Amount.Amount);
        Assert.Equal(333.30m, legs[1].Amount.Amount);
        Assert.Equal(333.41m, legs[2].Amount.Amount);   // the remainder, NOT 333.40
        Assert.Equal(1_000.01m, legs.Sum(l => l.Amount.Amount));
    }

    /// <summary>The same exactness over a spread of awkward values — the invariant is "Σ legs == item value",
    /// never "each leg is within a paisa". A single hand-picked figure can pass by luck; a table cannot.</summary>
    [Theory]
    [InlineData("0.01")]
    [InlineData("0.02")]
    [InlineData("1000.01")]
    [InlineData("9999.99")]
    [InlineData("12345.67")]
    [InlineData("-500.05")]     // a credit note's negative item value
    public void The_allocation_split_always_totals_the_item_value(string rupees)
    {
        var value = Money.FromRupees(decimal.Parse(rupees, System.Globalization.CultureInfo.InvariantCulture));
        var cls = WithAllocations((SalesA, 3_333), (SalesB, 3_333), (SalesC, 3_334));

        var legs = VoucherClassPosting.Allocate(cls, value);

        Assert.Equal(value.Amount, legs.Sum(l => l.Amount.Amount));
        Assert.All(legs, l => Assert.True(l.Amount.IsPaisaExact, $"{l.Amount} is not paisa-exact"));
    }

    /// <summary>A single 100% allocation is the ordinary case — the whole item value on one pre-mapped ledger,
    /// which is what "the operator does not name the ledger" means for a plain sales class.</summary>
    [Fact]
    public void A_single_full_allocation_posts_the_whole_item_value_to_the_pre_mapped_ledger()
    {
        var legs = VoucherClassPosting.Allocate(
            WithAllocations((SalesA, 10_000)), Money.FromRupees(4_250.75m));

        var leg = Assert.Single(legs);
        Assert.Equal(SalesA, leg.LedgerId);
        Assert.Equal(4_250.75m, leg.Amount.Amount);
    }

    // ═══════════════════════════════════════════════════ 2 · rounding

    /// <summary>The vendor's four Rounding Methods at a limit of ₹1, on a value whose fraction is below half,
    /// exactly half, and above half. Downward/Upward must NOT depend on the fraction; Normal must.</summary>
    [Theory]
    [InlineData(VoucherClassRoundingMethod.NotApplicable, "100.40", "100.40")]
    [InlineData(VoucherClassRoundingMethod.Downward, "100.40", "100")]
    [InlineData(VoucherClassRoundingMethod.Downward, "100.60", "100")]
    [InlineData(VoucherClassRoundingMethod.Upward, "100.40", "101")]
    [InlineData(VoucherClassRoundingMethod.Upward, "100.60", "101")]
    [InlineData(VoucherClassRoundingMethod.Normal, "100.40", "100")]
    [InlineData(VoucherClassRoundingMethod.Normal, "100.50", "101")]
    [InlineData(VoucherClassRoundingMethod.Normal, "100.60", "101")]
    public void The_four_vendor_rounding_methods_round_as_the_vendor_documents(
        VoucherClassRoundingMethod method, string raw, string expected)
    {
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        var result = VoucherClassRounding.Apply(decimal.Parse(raw, ci), method, Money.FromRupees(1m));
        Assert.Equal(decimal.Parse(expected, ci), result.Amount);
    }

    /// <summary>The rounding LIMIT is the multiple, not a decimal-place count — the vendor's own example is that
    /// a limit of 5 rounds to multiples of 5.</summary>
    [Theory]
    [InlineData(VoucherClassRoundingMethod.Upward, "5", "101.00", "105")]
    [InlineData(VoucherClassRoundingMethod.Downward, "5", "104.99", "100")]
    [InlineData(VoucherClassRoundingMethod.Normal, "5", "102.49", "100")]
    [InlineData(VoucherClassRoundingMethod.Normal, "5", "102.50", "105")]
    [InlineData(VoucherClassRoundingMethod.Normal, "10", "104.99", "100")]
    public void The_rounding_limit_is_the_multiple_that_is_snapped_to(
        VoucherClassRoundingMethod method, string limit, string raw, string expected)
    {
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        var result = VoucherClassRounding.Apply(
            decimal.Parse(raw, ci), method, Money.FromRupees(decimal.Parse(limit, ci)));
        Assert.Equal(decimal.Parse(expected, ci), result.Amount);
    }

    /// <summary>
    /// 🔴 <b>NEGATIVE VALUES ROUND THE WAY THEIR NAMES SAY, NOT THE WAY A MAGNITUDE-THEN-SIGN VERSION WOULD.</b>
    /// "Downward" on −100.40 is the next LOWER multiple, −101 — not −100, which is what a
    /// <c>Truncate</c>-based or an abs-then-negate implementation returns. A credit note whose total-amount
    /// rounding went the wrong way is off by a whole rupee in the customer's favour on every document.
    /// </summary>
    [Theory]
    [InlineData(VoucherClassRoundingMethod.Downward, "-100.40", "-101")]
    [InlineData(VoucherClassRoundingMethod.Upward, "-100.40", "-100")]
    [InlineData(VoucherClassRoundingMethod.Normal, "-100.40", "-100")]
    [InlineData(VoucherClassRoundingMethod.Normal, "-100.50", "-101")]
    public void Rounding_a_negative_value_moves_in_the_direction_the_method_names(
        VoucherClassRoundingMethod method, string raw, string expected)
    {
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        var result = VoucherClassRounding.Apply(decimal.Parse(raw, ci), method, Money.FromRupees(1m));
        Assert.Equal(decimal.Parse(expected, ci), result.Amount);
    }

    /// <summary>
    /// The voucher-class rounding must agree, value for value, with the copy the pay-head engine already ships
    /// (<c>PayrollComputationService.ApplyRounding</c>) — the vendor offers the SAME four options on both screens,
    /// so a divergence would mean one of the two is wrong. This pins them together without coupling the engines.
    /// </summary>
    [Theory]
    [InlineData(VoucherClassRoundingMethod.Normal, PayHeadRoundingMethod.Normal)]
    [InlineData(VoucherClassRoundingMethod.Upward, PayHeadRoundingMethod.Upward)]
    [InlineData(VoucherClassRoundingMethod.Downward, PayHeadRoundingMethod.Downward)]
    [InlineData(VoucherClassRoundingMethod.NotApplicable, PayHeadRoundingMethod.NotApplicable)]
    public void Voucher_class_rounding_agrees_with_the_pay_head_rounding_it_mirrors(
        VoucherClassRoundingMethod mine, PayHeadRoundingMethod payroll)
    {
        var limit = Money.FromRupees(1m);
        foreach (var raw in new[] { 0m, 0.49m, 0.5m, 1.5m, 2.5m, 100.4m, 100.6m, -0.5m, -100.4m, -100.6m })
        {
            var expected = PayrollRoundingOracle(payroll, raw, limit);
            Assert.Equal(expected, VoucherClassRounding.Apply(raw, mine, limit).Amount);
        }
    }

    /// <summary>A transcription of <c>PayrollComputationService.ApplyRounding</c> (which is private), kept here so
    /// the agreement test above compares against the payroll BEHAVIOUR rather than against itself.</summary>
    private static decimal PayrollRoundingOracle(PayHeadRoundingMethod method, decimal raw, Money limit)
        => method switch
        {
            PayHeadRoundingMethod.Normal => Math.Round(raw / limit.Amount, 0, MidpointRounding.AwayFromZero) * limit.Amount,
            PayHeadRoundingMethod.Upward => Math.Ceiling(raw / limit.Amount) * limit.Amount,
            PayHeadRoundingMethod.Downward => Math.Floor(raw / limit.Amount) * limit.Amount,
            _ => Math.Round(raw, 2, MidpointRounding.AwayFromZero),
        };

    // ═══════════════════════════════════════════════════ 3 · additional entries and the round-off leg

    /// <summary>The vendor's <i>"Based on Quantity"</i>: the Value Basis is a rate PER BASE UNIT, so the leg is
    /// basis × total quantity.</summary>
    [Fact]
    public void A_based_on_quantity_entry_posts_the_value_basis_times_the_quantity()
    {
        var cls = WithAllocations((SalesA, 10_000));
        cls.AddAdditionalEntry(new VoucherClassAdditionalEntry(
            Guid.NewGuid(), Freight, VoucherClassCalculationType.BasedOnQuantity,
            valueBasis: Money.FromRupees(2.50m)));

        var result = VoucherClassPosting.Compute(cls, Money.FromRupees(1_000m), totalBaseQuantity: 12m);

        var freight = Assert.Single(result.AdditionalLegs);
        Assert.Equal(Freight, freight.LedgerId);
        Assert.Equal(30.00m, freight.Amount.Amount);          // 2.50 × 12
        Assert.Equal(1_030.00m, result.Total.Amount);
    }

    /// <summary>
    /// 🔴 <b>THE ROUND-OFF LEG IS TAKEN ON THE RUNNING TOTAL, NOT ON THE ITEM VALUE — AND THIS IS THE TEST THAT
    /// TELLS THE TWO APART.</b> Item value ₹1 000.00 plus a per-unit freight of 2.50 × 7 = ₹17.50 gives a running
    /// total of ₹1 017.50, which rounds normally to ₹1 018.00 for a round-off leg of <b>+0.50</b>. An
    /// implementation that rounded the ITEM VALUE instead would find ₹1 000.00 already round, post NO round-off
    /// leg at all, and leave a Grand Total of ₹1 017.50 on a class the operator configured to round.
    /// </summary>
    [Fact]
    public void The_round_off_leg_balances_the_total_after_every_other_additional_entry()
    {
        var cls = WithAllocations((SalesA, 10_000));
        cls.AddAdditionalEntry(new VoucherClassAdditionalEntry(
            Guid.NewGuid(), Freight, VoucherClassCalculationType.BasedOnQuantity,
            valueBasis: Money.FromRupees(2.50m), order: 0));
        cls.AddAdditionalEntry(new VoucherClassAdditionalEntry(
            Guid.NewGuid(), RoundOff, VoucherClassCalculationType.AsTotalAmountRounding,
            roundingMethod: VoucherClassRoundingMethod.Normal,
            roundingLimit: Money.FromRupees(1m), order: 1));

        var result = VoucherClassPosting.Compute(cls, Money.FromRupees(1_000m), totalBaseQuantity: 7m);

        Assert.Equal(2, result.AdditionalLegs.Count);
        Assert.Equal(17.50m, result.AdditionalLegs[0].Amount.Amount);
        Assert.Equal(RoundOff, result.AdditionalLegs[1].LedgerId);
        Assert.Equal(0.50m, result.AdditionalLegs[1].Amount.Amount);
        Assert.Equal(1_018.00m, result.Total.Amount);         // and it IS round
    }

    /// <summary>
    /// 🔴 <b>A DOWNWARD ROUNDING POSTS A NEGATIVE ROUND-OFF LEG, AND THE VOUCHER STILL BALANCES.</b> The vendor
    /// says the round-off ledger carries the difference <i>"as a positive or negative value"</i>. ₹1 000.60
    /// rounded down to ₹1 000.00 needs a leg of −0.60; dropping it, or posting +0.60, moves the invoice by 0.60
    /// or 1.20 respectively.
    /// </summary>
    [Fact]
    public void A_downward_rounding_posts_a_negative_round_off_leg_that_still_balances()
    {
        var cls = WithAllocations((SalesA, 10_000));
        cls.AddAdditionalEntry(new VoucherClassAdditionalEntry(
            Guid.NewGuid(), RoundOff, VoucherClassCalculationType.AsTotalAmountRounding,
            roundingMethod: VoucherClassRoundingMethod.Downward, roundingLimit: Money.FromRupees(1m)));

        var result = VoucherClassPosting.Compute(cls, Money.FromRupees(1_000.60m), 1m);

        var leg = Assert.Single(result.AdditionalLegs);
        Assert.Equal(-0.60m, leg.Amount.Amount);
        Assert.Equal(1_000.00m, result.Total.Amount);

        // …and the negative leg becomes a CREDIT when the class's natural side is Debit, because an EntryLine
        // carries a magnitude and the sign has to live in the side.
        var line = leg.ToEntryLine(DrCr.Debit);
        Assert.Equal(DrCr.Credit, line.Side);
        Assert.Equal(0.60m, line.Amount.Amount);
    }

    /// <summary>An already-round total needs no round-off leg, and none is posted — an explicit ₹0.00 line is not
    /// a legal <c>EntryLine</c> and would print as a nil row on every rounded invoice.</summary>
    [Fact]
    public void An_already_round_total_posts_no_round_off_leg()
    {
        var cls = WithAllocations((SalesA, 10_000));
        cls.AddAdditionalEntry(new VoucherClassAdditionalEntry(
            Guid.NewGuid(), RoundOff, VoucherClassCalculationType.AsTotalAmountRounding,
            roundingMethod: VoucherClassRoundingMethod.Normal, roundingLimit: Money.FromRupees(1m)));

        var result = VoucherClassPosting.Compute(cls, Money.FromRupees(1_000m), 1m);

        Assert.Empty(result.AdditionalLegs);
        Assert.Equal(1_000m, result.Total.Amount);
    }

    /// <summary>
    /// 🔴 <b>THE WHOLE MACHINE, END TO END, ON ONE INVOICE — the assertion that the row is actually correct
    /// rather than correct in pieces.</b> A three-way pre-map of an odd item value, a per-unit freight, and a
    /// normal round-off to the rupee: every leg is named, and the legs total the rounded Grand Total exactly.
    /// </summary>
    [Fact]
    public void A_full_class_posts_every_leg_and_the_legs_total_the_rounded_grand_total()
    {
        var cls = WithAllocations((SalesA, 5_000), (SalesB, 3_000), (SalesC, 2_000));
        cls.AddAdditionalEntry(new VoucherClassAdditionalEntry(
            Guid.NewGuid(), Freight, VoucherClassCalculationType.BasedOnQuantity,
            valueBasis: Money.FromRupees(1.75m), order: 0));
        cls.AddAdditionalEntry(new VoucherClassAdditionalEntry(
            Guid.NewGuid(), RoundOff, VoucherClassCalculationType.AsTotalAmountRounding,
            roundingMethod: VoucherClassRoundingMethod.Normal,
            roundingLimit: Money.FromRupees(1m), order: 1));

        var result = VoucherClassPosting.Compute(cls, Money.FromRupees(3_333.33m), totalBaseQuantity: 3m);

        // The pre-map: 50 / 30 / 20 of 3 333.33 — the last leg takes the remainder.
        Assert.Equal(3, result.AllocationLegs.Count);
        Assert.Equal(1_666.67m, result.AllocationLegs[0].Amount.Amount);   // 1 666.665 away from zero
        Assert.Equal(1_000.00m, result.AllocationLegs[1].Amount.Amount);   // 999.999 away from zero
        Assert.Equal(666.66m, result.AllocationLegs[2].Amount.Amount);     // remainder, not 666.67
        Assert.Equal(3_333.33m, result.AllocationLegs.Sum(l => l.Amount.Amount));

        // Freight 1.75 × 3 = 5.25 ⇒ running 3 338.58 ⇒ normal to the rupee = 3 339.00 ⇒ round-off +0.42.
        Assert.Equal(5.25m, result.AdditionalLegs[0].Amount.Amount);
        Assert.Equal(0.42m, result.AdditionalLegs[1].Amount.Amount);
        Assert.Equal(3_339.00m, result.Total.Amount);
    }

    /// <summary>A class with neither table — the shipped Stock Journal transfer class (census 9.9) — must keep
    /// posting NOTHING. v62 extended the type; it must not have given every existing class a leg.</summary>
    [Fact]
    public void A_class_with_no_allocations_and_no_entries_posts_nothing()
    {
        var transfer = new VoucherClass(Guid.NewGuid(), "Transfer", useClassForInterGodownTransfers: true);

        var result = VoucherClassPosting.Compute(transfer, Money.FromRupees(5_000m), 10m);

        Assert.Empty(result.AllocationLegs);
        Assert.Empty(result.AdditionalLegs);
        Assert.Equal(Money.Zero, result.Total);
    }

    // ═══════════════════════════════════════════════════ 4 · the master-save refusals

    /// <summary>🔴 Allocations that do not total 100% are refused AT THE MASTER. This is the single most
    /// consequential rule in the row: a 90% class posts an out-of-balance voucher every time it is used.</summary>
    [Theory]
    [InlineData(5_000, 4_000)]    // 90% — the invoice comes up short
    [InlineData(5_000, 5_001)]    // 100.01% — and over-allocation is just as unbalanced
    [InlineData(5_000, 10_000)]   // 150%
    public void Allocations_that_do_not_total_one_hundred_percent_are_refused(int firstBp, int secondBp)
    {
        var cls = WithAllocations((SalesA, firstBp), (SalesB, secondBp));
        var ex = Assert.Throws<InvalidOperationException>(() => VoucherClassService.Validate(cls));
        Assert.Contains("100%", ex.Message);
    }

    /// <summary>The same ledger twice would still total 100%, so the total rule cannot see it — and the posted
    /// result would depend on which of the two rows the remainder happened to land on.</summary>
    [Fact]
    public void The_same_ledger_cannot_take_two_allocations()
    {
        var cls = WithAllocations((SalesA, 5_000), (SalesA, 5_000));
        var ex = Assert.Throws<InvalidOperationException>(() => VoucherClassService.Validate(cls));
        Assert.Contains("same ledger", ex.Message);
    }

    /// <summary>Two round-off entries would each claim to balance the same total.</summary>
    [Fact]
    public void A_class_may_carry_at_most_one_total_amount_rounding_entry()
    {
        var cls = WithAllocations((SalesA, 10_000));
        for (var i = 0; i < 2; i++)
            cls.AddAdditionalEntry(new VoucherClassAdditionalEntry(
                Guid.NewGuid(), RoundOff, VoucherClassCalculationType.AsTotalAmountRounding,
                roundingMethod: VoucherClassRoundingMethod.Normal,
                roundingLimit: Money.FromRupees(1m), order: i));

        var ex = Assert.Throws<InvalidOperationException>(() => VoucherClassService.Validate(cls));
        Assert.Contains("at most one", ex.Message);
    }

    /// <summary>A rounding entry with no METHOD looks configured and rounds nothing — the operator believes their
    /// invoices are rounded and they are not.</summary>
    [Fact]
    public void A_rounding_entry_with_no_method_is_refused_rather_than_rounding_nothing()
    {
        var cls = WithAllocations((SalesA, 10_000));
        cls.AddAdditionalEntry(new VoucherClassAdditionalEntry(
            Guid.NewGuid(), RoundOff, VoucherClassCalculationType.AsTotalAmountRounding));

        var ex = Assert.Throws<InvalidOperationException>(() => VoucherClassService.Validate(cls));
        Assert.Contains("round nothing", ex.Message);
    }

    /// <summary>A sub-paisa rounding limit snaps amounts to a multiple the INTEGER-paisa store cannot hold — the
    /// same rule the pay-head master enforces, for the same reason.</summary>
    [Fact]
    public void A_sub_paisa_rounding_limit_is_refused_at_the_master()
    {
        var cls = WithAllocations((SalesA, 10_000));
        cls.AddAdditionalEntry(new VoucherClassAdditionalEntry(
            Guid.NewGuid(), RoundOff, VoucherClassCalculationType.AsTotalAmountRounding,
            roundingMethod: VoucherClassRoundingMethod.Normal,
            roundingLimit: Money.FromRupees(0.005m)));

        var ex = Assert.Throws<InvalidOperationException>(() => VoucherClassService.Validate(cls));
        Assert.Contains("whole number of paisa", ex.Message);
    }

    /// <summary>A rounding LIMIT with no METHOD is a figure the operator believes is in force and nothing
    /// reads.</summary>
    [Fact]
    public void A_rounding_limit_without_a_method_is_refused()
    {
        var cls = WithAllocations((SalesA, 10_000));
        cls.AddAdditionalEntry(new VoucherClassAdditionalEntry(
            Guid.NewGuid(), Freight, VoucherClassCalculationType.BasedOnQuantity,
            valueBasis: Money.FromRupees(1m), roundingLimit: Money.FromRupees(1m)));

        var ex = Assert.Throws<InvalidOperationException>(() => VoucherClassService.Validate(cls));
        Assert.Contains("rounding limit but no rounding method", ex.Message);
    }

    /// <summary>A class with NO allocations at all stays legal — that is every class written before v62, and the
    /// 100% rule must not retro-invalidate the shipped Stock Journal transfer class.</summary>
    [Fact]
    public void A_class_with_no_allocations_is_still_valid()
    {
        VoucherClassService.Validate(new VoucherClass(Guid.NewGuid(), "Transfer", true));
    }

    /// <summary>
    /// 🔴 <b>A HALF-KEYED CLASS IS STRUCTURALLY VALID BUT MUST NOT POST — and this pair is what makes the split
    /// between the two rules safe.</b> An operator part-way through a 50/30/20 pre-map has a class that
    /// <see cref="VoucherClassService.ValidateStructure"/> accepts (so the next allocation can be added at all),
    /// and <see cref="VoucherClassPosting.Compute"/> REFUSES (so the incomplete rule can never write a voucher
    /// that is 20% short). Delete either half and this test goes red.
    /// </summary>
    [Fact]
    public void A_partly_keyed_class_is_structurally_valid_but_refuses_to_post()
    {
        var half = WithAllocations((SalesA, 5_000), (SalesB, 3_000));   // 80% — still being keyed

        VoucherClassService.ValidateStructure(half);                    // accepted: the next row can be added

        var ex = Assert.Throws<InvalidOperationException>(
            () => VoucherClassPosting.Compute(half, Money.FromRupees(1_000m), 1m));
        Assert.Contains("80%", ex.Message);
        Assert.Contains("out of balance", ex.Message);
    }
}
