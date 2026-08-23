using System;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Apex.Desktop.ViewModels;
using Xunit;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.Tests;

/// <summary>
/// <b>§194-I's per-month threshold at the ALTERATION door — and the grandfathering that keeps history open.</b>
///
/// <para>🔴 <b>The problem this file pins.</b> §194-I's first proviso (FY 2025-26) tests the rent "credited or paid
/// for a month or part of a month" against <b>₹50,000</b> and carries no annual limb at all; the engine used to test
/// an annualised <b>₹6,00,000 financial-year</b> aggregate instead. <c>ApplyReCarve</c> refuses an alteration whose
/// re-carve produces a different TDS while the party's gross has not moved — deliberately, because a re-computed
/// figure would restate a deduction already deposited and reported. Flipping the window without a pin would trip
/// that refusal on <b>every §194-I voucher in every existing book</b>: a narration fix, a cost-centre correction, a
/// date typo, all refused. The user's ruling is that such a voucher keeps its posted figure <b>and stays
/// editable</b>.</para>
///
/// <para>🔴 <b>AND THE PIN IS NOT §194C's.</b> §194C's grandfathering carries a <b>rate</b>; here the drift is in
/// whether the threshold was <b>crossed at all</b> — a ₹60,000 rent bill posted under the annualised rule withheld
/// NOTHING where the statute takes ₹6,000.00, and twelve ₹40,000 months withheld ₹4,000 partway through the year
/// where the statute takes nothing. So the posted <b>outcome</b> is what travels: the voucher's own stamped
/// <c>TdsLineTax.AssessableValue</c> and <c>TdsLineTax.TdsAmount</c>. See <c>TdsService.GrandfatheredLiability</c>.
/// </para>
///
/// <para><b>How a "pre-window" voucher is produced here, stated openly.</b> The monthly window is live, so the
/// entry screen can no longer post a ₹60,000.60 rent bill with no withholding. Each such fixture is posted
/// <b>raw</b> through <c>LedgerService</c> — the same door the canonical importer uses — carrying exactly the
/// persisted shape the old engine produced: the assessment detail (₹0.00 withheld) riding the party's own full
/// gross credit. That is not a workaround; it is byte-for-byte the state an existing book holds.</para>
///
/// <para><b>Odd paise everywhere</b> (house rule): a ±₹0.50 defect once survived six round-number assertions.</para>
/// </summary>
public sealed class VoucherAlter194IGrandfatherTests
{
    private const string Section = "194I(b)";

    /// <summary>₹60,000.60 — one month's rent, above the statutory ₹50,000 limb. 10% ⇒ ₹6,000.00 (60,000.60 × 10%
    /// = 6,000.06, nearest rupee), leaving ₹54,000.60 payable.</summary>
    private const decimal AboveLimb = 60_000.60m;

    /// <summary>₹40,000.40 — one month's rent BELOW the statutory ₹50,000 limb, so today's rule takes nothing. The
    /// annualised rule took 10% = ₹4,000.00 of it once the year's aggregate crossed ₹6,00,000.</summary>
    private const decimal BelowLimb = 40_000.40m;

    private static Money AmountOn(Voucher v, Guid ledgerId) =>
        v.Lines.Where(l => l.LedgerId == ledgerId).Aggregate(Money.Zero, (a, l) => a + l.Amount);

    private static DomainLedger TdsPayable(Company c) =>
        c.Ledgers.First(l => l.TdsTcsClassification == TdsTcsLedgerKind.Tds);

    private static TdsLineTax PostedDetail(Voucher v) =>
        v.Lines.Select(l => l.Tds).First(t => t is not null)!;

    private static VoucherEntryViewModel OpenOrThrow(AlterationBook book, Guid voucherId)
    {
        var open = book.ForAlter(voucherId);
        Assert.False(open.IsRefused, "Expected the alteration screen to open; refused with: " + open.Refusal);
        return open.Entry!;
    }

    /// <summary>Posts a hand-built voucher straight through the engine — the door the canonical importer uses, and
    /// the only way to produce the pre-window withholding shapes the entry screen can no longer key.</summary>
    private static Voucher PostRaw(AlterationBook book, params EntryLine[] lines)
    {
        var voucher = new Voucher(
            Guid.NewGuid(), book.Type(VoucherBaseType.Journal).Id, book.On(), lines, narration: "pre-window");
        var posted = new LedgerService(book.Company).Post(voucher);
        book.Storage.Save(book.Company);
        return posted;
    }

    /// <summary>
    /// A §194-I book holding ONE voucher in exactly the shape the annualised rule persisted:
    /// <paramref name="postedTds"/> withheld from a <paramref name="gross"/> rent bill, whatever today's per-month
    /// rule would say about it.
    /// </summary>
    private static (AlterationBook Book, DomainLedger Expense, DomainLedger Party, Voucher V) PreWindowVoucher(
        string tag, decimal gross, decimal postedTds)
    {
        var book = AlterationBook.New(tag);
        var (expense, party) = book.EnableTds(Section, expenseName: "Office Rent", partyName: "Estate Holdings");
        var nature = book.Company.FindNatureOfPaymentByCode(Section)!;
        var grossMoney = new Money(gross);
        var tdsMoney = new Money(postedTds);
        var detail = new TdsLineTax(
            nature.Id, nature.SectionCode, grossMoney, nature.RateWithPanBp, tdsMoney, party.Id, panApplied: true);

        var v = postedTds == 0m
            ? PostRaw(book,
                new EntryLine(expense.Id, grossMoney, DrCr.Debit),
                new EntryLine(party.Id, grossMoney, DrCr.Credit, tds: detail))
            : PostRaw(book,
                new EntryLine(expense.Id, grossMoney, DrCr.Debit),
                new EntryLine(party.Id, grossMoney - tdsMoney, DrCr.Credit),
                new EntryLine(TdsPayable(book.Company).Id, tdsMoney, DrCr.Credit, tds: detail));

        Assert.Equal(tdsMoney, PostedDetail(v).TdsAmount);
        return (book, expense, party, v);
    }

    // ================================================================ the window reaches the screen

    /// <summary>
    /// 🔴 A FRESH §194-I(b) rent bill of ₹60,000.60 in one month is posted with the statutory deduction:
    /// <b>₹6,000.00</b> withheld and ₹54,000.60 credited. Under the annualised ₹6,00,000 rule the screen posted two
    /// lines, ₹0.00 withheld and the full ₹60,000.60 credited.
    /// </summary>
    [Fact]
    public void A_fresh_194I_rent_bill_above_the_monthly_limb_is_posted_with_the_deduction()
    {
        using var book = AlterationBook.New("194i-fresh");
        var (expense, party) = book.EnableTds(Section, expenseName: "Office Rent", partyName: "Estate Holdings");

        var v = book.Post(VoucherBaseType.Journal, book.On(), new[]
        {
            (expense, DrCr.Debit, "60000.60"),
            (party, DrCr.Credit, "60000.60"),
        });

        Assert.Equal(3, v.Lines.Count);
        Assert.Equal(new Money(6_000.00m), AmountOn(v, TdsPayable(book.Company).Id));
        Assert.Equal(new Money(54_000.60m), AmountOn(v, party.Id));
    }

    /// <summary>A fresh rent bill BELOW the monthly limb still withholds nothing — ₹40,000.40, two lines, the full
    /// gross to the party, and the assessment detail riding that leg at ₹0.00.</summary>
    [Fact]
    public void A_fresh_194I_rent_bill_below_the_monthly_limb_withholds_nothing()
    {
        using var book = AlterationBook.New("194i-fresh-below");
        var (expense, party) = book.EnableTds(Section, expenseName: "Office Rent", partyName: "Estate Holdings");

        var v = book.Post(VoucherBaseType.Journal, book.On(), new[]
        {
            (expense, DrCr.Debit, "40000.40"),
            (party, DrCr.Credit, "40000.40"),
        });

        Assert.Equal(2, v.Lines.Count);
        Assert.Equal(new Money(40_000.40m), AmountOn(v, party.Id));
        Assert.Equal(Money.Zero, PostedDetail(v).TdsAmount);
    }

    // ================================================================ grandfathering: history stays ALTERABLE

    /// <summary>
    /// 🔴 <b>THE RULING, AND THE WHOLE POINT OF IT.</b> A ₹60,000.60 rent voucher posted under the annualised rule
    /// withheld <b>nothing</b>. The per-month rule says ₹6,000.00 — the disagreement is asserted here directly,
    /// against the live engine, before the alteration is attempted. The voucher nevertheless <b>opens, accepts and
    /// keeps its posted figures</b>. Without the grandfathering the re-carve would compute ₹6,000.00, the
    /// "nothing on this grid has moved the party's gross" refusal would fire, and this alteration — a narration fix
    /// — would be refused.
    /// </summary>
    [Fact]
    public void A_pre_window_194I_voucher_that_withheld_nothing_can_still_be_altered()
    {
        var (book, _, party, v) = PreWindowVoucher("194i-grandfather-zero", AboveLimb, postedTds: 0m);
        using var _book = book;

        // The disagreement this grandfathering exists to absorb, in figures, against the live engine.
        var statutory = new TdsService(book.Company).ComputeWithholding(
            new Money(AboveLimb), book.Company.FindNatureOfPaymentByCode(Section)!, party, book.On());
        Assert.Equal(new Money(6_000.00m), statutory.TdsAmount);
        Assert.Equal(Money.Zero, PostedDetail(v).TdsAmount);

        var entry = OpenOrThrow(book, v.Id);
        entry.Narration = "pre-window, re-narrated";
        Assert.True(entry.AcceptAlteration(), "Expected the alteration to be accepted; refused with: " + entry.Message);

        var after = book.Company.FindVoucher(v.Id)!;
        Assert.Equal("pre-window, re-narrated", after.Narration);
        Assert.Equal(2, after.Lines.Count);
        Assert.Equal(new Money(AboveLimb), AmountOn(after, party.Id));
        Assert.Equal(Money.Zero, PostedDetail(after).TdsAmount);
        Assert.DoesNotContain(after.Lines, l => l.LedgerId == TdsPayable(book.Company).Id);
    }

    /// <summary>
    /// 🔴 <b>AND IT RUNS THE OTHER WAY, WHICH IS WHY A RATE PIN COULD NOT HAVE CARRIED IT.</b> A ₹40,000.40 rent
    /// voucher DID withhold ₹4,000.00 under the annualised rule (the year's aggregate had crossed ₹6,00,000);
    /// today's per-month rule leaves ₹40,000.40 alone. The alteration is accepted and the ₹4,000.00 already
    /// deposited and reported is <b>not restated</b> back to the party.
    /// </summary>
    [Fact]
    public void A_pre_window_194I_voucher_that_did_withhold_can_still_be_altered()
    {
        var (book, _, party, v) = PreWindowVoucher("194i-grandfather-withheld", BelowLimb, postedTds: 4_000.00m);
        using var _book = book;

        // Taken at THIS voucher's own posting moment, exactly as the re-carve takes it — otherwise the voucher
        // reads its own ₹40,000.40 back as prior, the month reaches ₹80,000.80 and the comparison would be with a
        // liability the posting never faced.
        var statutory = new TdsService(book.Company).ComputeWithholding(
            new Money(BelowLimb), book.Company.FindNatureOfPaymentByCode(Section)!, party, book.On(),
            asPostedBefore: v.Id);
        Assert.False(statutory.Applies);                            // today: nothing is due on this bill
        Assert.Equal(Money.Zero, statutory.TdsAmount);

        var entry = OpenOrThrow(book, v.Id);
        entry.Narration = "pre-window withholding, re-narrated";
        Assert.True(entry.AcceptAlteration(), "Expected the alteration to be accepted; refused with: " + entry.Message);

        var after = book.Company.FindVoucher(v.Id)!;
        Assert.Equal(3, after.Lines.Count);
        Assert.Equal(new Money(4_000.00m), AmountOn(after, TdsPayable(book.Company).Id));
        Assert.Equal(new Money(36_000.40m), AmountOn(after, party.Id));
        Assert.Equal(new Money(BelowLimb),
            AmountOn(after, party.Id) + AmountOn(after, TdsPayable(book.Company).Id));
    }

    /// <summary>
    /// <b>The advisory panel must state what Accept will post</b> — ₹0.00 and the full ₹60,000.60 payable, not the
    /// ₹6,000.00 today's rule would take on a fresh bill. A panel reading "₹6,000.00 withheld" over an Accept that
    /// posts nothing is the exact defect
    /// <c>VoucherAlterDerivedLegDriftTests.The_withholding_panel_on_an_altering_screen_states_what_accept_will_post</c>
    /// exists to catch, so the pin has to reach the panel and the bill-wise preview as well as the accept path.
    /// </summary>
    [Fact]
    public void The_panel_on_a_grandfathered_194I_alteration_states_the_posted_outcome_not_todays()
    {
        var (book, _, _, v) = PreWindowVoucher("194i-grandfather-panel", AboveLimb, postedTds: 0m);
        using var _book = book;

        var entry = OpenOrThrow(book, v.Id);

        Assert.True(entry.ShowTdsPanel);
        Assert.Equal("194I(b)", entry.TdsSectionText);
        Assert.Equal("0.00", entry.TdsAmountText);
        Assert.Equal("60,000.60", entry.TdsNetPayableText);
        Assert.Contains("below threshold", entry.TdsSummary);
    }

    /// <summary>
    /// 🔴 <b>THE PIN RELEASES THE MOMENT THE OPERATOR AMENDS THE BASE — grandfathering protects a POSTED figure, it
    /// is not a licence for an AMENDED bill to keep answering for a different one.</b> The rent moves ₹60,000.60 →
    /// ₹70,000.70 and the carve is re-derived under the <b>statute</b>: ₹7,000.00 withheld (70,000.70 × 10% =
    /// 7,000.07, nearest rupee) and ₹63,000.70 credited, with the TDS-Payable leg appended.
    /// </summary>
    [Fact]
    public void A_grandfathered_194I_voucher_re_carves_an_amended_gross_under_the_statute()
    {
        var (book, _, party, v) = PreWindowVoucher("194i-grandfather-amend-up", AboveLimb, postedTds: 0m);
        using var _book = book;

        var entry = OpenOrThrow(book, v.Id);
        foreach (var line in entry.Lines.Where(l => l.SelectedLedger is not null))
            line.AmountText = "70000.70";
        Assert.True(entry.AcceptAlteration(), "Expected the alteration to be accepted; refused with: " + entry.Message);

        var after = book.Company.FindVoucher(v.Id)!;
        Assert.Equal(3, after.Lines.Count);
        Assert.Equal(new Money(7_000.00m), AmountOn(after, TdsPayable(book.Company).Id));
        Assert.Equal(new Money(63_000.70m), AmountOn(after, party.Id));
        Assert.Equal(new Money(70_000.70m),
            AmountOn(after, party.Id) + AmountOn(after, TdsPayable(book.Company).Id));
    }

    /// <summary>
    /// The grandfathered outcome survives the round trip: re-open and re-accept three times and the voucher still
    /// carries ₹0.00 against the full ₹60,000.60, so the concession is not a one-shot that decays on the next edit.
    /// </summary>
    [Fact]
    public void A_grandfathered_194I_voucher_stays_grandfathered_across_repeated_alterations()
    {
        var (book, _, party, v) = PreWindowVoucher("194i-grandfather-twice", AboveLimb, postedTds: 0m);
        using var _book = book;

        for (var pass = 1; pass <= 3; pass++)
        {
            var entry = OpenOrThrow(book, v.Id);
            entry.Narration = "pass " + pass;
            Assert.True(entry.AcceptAlteration(), $"Pass {pass} refused with: {entry.Message}");
        }

        var after = book.Company.FindVoucher(v.Id)!;
        Assert.Equal(2, after.Lines.Count);
        Assert.Equal(new Money(AboveLimb), AmountOn(after, party.Id));
        Assert.Equal(Money.Zero, PostedDetail(after).TdsAmount);
    }

    // ================================================================ the master states the window, not the figure

    /// <summary>
    /// <b>The Nature-of-Payment master must state the WINDOW, because the figure alone is ambiguous.</b> §194-I
    /// reads "₹50,000/month"; a financial-year section still reads "/FY"; §194C still shows both of its limbs. And
    /// a nature carrying the superseded annualised ₹6,00,000 in its stored FY field — the shape every existing book
    /// holds — reads "₹50,000/month" too, because that stored figure is inert and printing it would tell the
    /// operator a threshold the engine does not apply.
    /// </summary>
    [Fact]
    public void The_nature_of_payment_master_states_the_monthly_window_and_hides_the_inert_annual_figure()
    {
        using var book = AlterationBook.New("194i-master-threshold");
        book.EnableTds(Section);                                    // seeds the predefined Nature-of-Payment set
        var vm = new NatureOfPaymentMasterViewModel(book.Company, book.Storage, () => { });

        string ThresholdOf(string code) => vm.Natures.First(n => n.SectionCode == code).Threshold;

        Assert.Equal("₹50,000/month", ThresholdOf("194I(a)"));
        Assert.Equal("₹50,000/month", ThresholdOf("194I(b)"));
        Assert.Equal("₹50,000/FY", ThresholdOf("194J(b)"));
        Assert.Equal("₹30,000 single · ₹1,00,000/FY", ThresholdOf("194C"));

        // A legacy book's persisted row: the annualised ₹6,00,000 is still stored, and still not shown.
        var legacy = new NatureOfPayment(
            Guid.NewGuid(), "194-I", "Rent (legacy book)", 1000, 2000, "4IB",
            cumulativeThreshold: Money.FromRupees(6_00_000m));
        book.Company.Tds!.AddNatureOfPayment(legacy);
        var reloaded = new NatureOfPaymentMasterViewModel(book.Company, book.Storage, () => { });

        Assert.Equal("₹50,000/month", reloaded.Natures.First(n => n.SectionCode == "194-I").Threshold);
    }

    /// <summary>
    /// 🔴 <b>A Cumulative-FY threshold typed onto a PER-MONTH section is refused by name, not stored and ignored.</b>
    /// §194-I has no financial-year aggregate limb, so such a figure would be persisted and never applied — and the
    /// list column now states the window rather than the figure, so it would not even show up to contradict itself.
    /// Silently discarding what the operator typed is the one outcome not available here. A financial-year section
    /// is untouched: the same figure on a custom §194K row is accepted.
    /// </summary>
    [Fact]
    public void A_cumulative_fy_threshold_typed_onto_a_per_month_section_is_refused_by_name()
    {
        using var book = AlterationBook.New("194i-master-refuse-fy");
        book.EnableTds(Section);
        var vm = new NatureOfPaymentMasterViewModel(book.Company, book.Storage, () => { });

        vm.SectionCode = "194-I";
        vm.Name = "Rent, hand-authored";
        vm.RateWithPanText = "10";
        vm.RateWithoutPanText = "20";
        vm.FvuSectionCode = "4IB";
        vm.CumulativeThresholdText = "600000";

        Assert.False(vm.Create(), "Expected the stored FY threshold to be refused; it was accepted.");
        Assert.Contains("PER MONTH", vm.Message!);
        Assert.Contains("stored and never applied", vm.Message!);
        Assert.DoesNotContain(book.Company.NaturesOfPayment, n => n.SectionCode == "194-I");

        // Blank it and the same section is created — the refusal is about the FIELD, not about §194-I.
        vm.CumulativeThresholdText = string.Empty;
        Assert.True(vm.Create(), vm.Message);
        var created = book.Company.NaturesOfPayment.First(n => n.SectionCode == "194-I");
        Assert.Null(created.CumulativeThreshold);
        Assert.Equal(Money.FromRupees(50_000m), created.MonthlyThreshold);

        // A financial-year section still takes the very same figure.
        vm.SectionCode = "194K";
        vm.Name = "Income in respect of units";
        vm.FvuSectionCode = "94K";
        vm.CumulativeThresholdText = "600000";
        Assert.True(vm.Create(), vm.Message);
        Assert.Equal(
            Money.FromRupees(6_00_000m),
            book.Company.NaturesOfPayment.First(n => n.SectionCode == "194K").CumulativeThreshold);
    }
}
