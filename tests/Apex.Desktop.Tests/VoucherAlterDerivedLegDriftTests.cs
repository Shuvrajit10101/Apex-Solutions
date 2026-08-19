using System;
using System.Collections.Generic;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Xunit;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.Tests;

/// <summary>
/// <b>Phase 10.11 S5c — the drift the re-derivation had NO guard for, and the pins nothing exercised.</b>
///
/// <para>🔴 <b>The property this file exists to pin, stated once.</b> While the operator has not moved the party's
/// GROSS, the re-carve MUST reproduce the posted withholding — whatever moved underneath it. The shipped detection
/// compared the deductee, the section and the rate and nothing else, so the applies/not-applies transition and
/// every input to the assessable base were unguarded: a posted, reported ₹3,000 deduction was silently DELETED by
/// an alteration that changed nothing but a narration, and a ₹12,000 one was silently RESTATED to ₹10,000 by
/// re-grouping an unrelated ledger. The class doc says the opposite in bold — "can never silently LOSE it …
/// Silence is the one outcome that is not available."</para>
///
/// <para>🔴 <b>And the mirror of it.</b> The cumulative-FY projection selects by DATE, so a sibling posted LATER and
/// dated on or before the voucher counted as "prior" at re-carve although it was not in the book at posting — a
/// narration edit on the FIRST of two same-day journals ACQUIRED a withholding and moved real money. The
/// projection is now taken at the voucher's POSTING MOMENT, so that case simply re-computes to what was posted and
/// is accepted unchanged rather than refused.</para>
///
/// <para><b>Odd paise everywhere</b> (house rule): a ±₹0.50 defect once survived six round-number assertions.</para>
/// </summary>
public sealed class VoucherAlterDerivedLegDriftTests
{
    private const string Section = "194J(b)";
    private const string GstinMaharashtra = "27AAPFU0939F1ZV";
    private const string GstinGujarat = "24AAACC1206D1ZM";

    // ================================================================ helpers

    private static Money AmountOn(Voucher v, Guid ledgerId) =>
        v.Lines.Where(l => l.LedgerId == ledgerId).Aggregate(Money.Zero, (a, l) => a + l.Amount);

    private static DomainLedger TdsPayable(Company c) =>
        c.Ledgers.First(l => l.TdsTcsClassification == TdsTcsLedgerKind.Tds);

    private static VoucherEntryViewModel OpenOrThrow(AlterationBook book, Guid voucherId)
    {
        var open = book.ForAlter(voucherId);
        Assert.False(open.IsRefused, "Expected the alteration screen to open; refused with: " + open.Refusal);
        return open.Entry!;
    }

    /// <summary>Refuses, and proves the BOOK did not move — in memory and on disk.</summary>
    private static string RefuseOrThrow(AlterationBook book, Guid voucherId, Action<VoucherEntryViewModel> edit)
    {
        var before = book.Export();
        var beforeDisk = book.ExportReloaded();

        var entry = OpenOrThrow(book, voucherId);
        edit(entry);
        Assert.False(entry.AcceptAlteration(), "Expected the alteration to be refused; it was accepted.");
        Assert.False(string.IsNullOrWhiteSpace(entry.Message));

        Assert.Equal(before, book.Export());
        Assert.Equal(beforeDisk, book.ExportReloaded());
        return entry.Message!;
    }

    private static Voucher PostCarved(
        AlterationBook book, VoucherBaseType baseType, DomainLedger expense, DomainLedger party, string gross,
        DateOnly? on = null, string? narration = null) =>
        book.Post(baseType, on ?? book.On(),
            new[] { (expense, DrCr.Debit, gross), (party, DrCr.Credit, gross) }, narration);

    /// <summary>
    /// Two §194J(b) journals of ₹30,000.30 each: the first below the ₹50,000 cumulative threshold (2 lines, party
    /// credited the full gross), the second crossing it (3 lines, party ₹27,000.30, TDS Payable ₹3,000.00).
    /// </summary>
    private static (AlterationBook Book, DomainLedger Expense, DomainLedger Party, Voucher First, Voucher Second)
        TwoAssessments(string tag, int firstDay = 5, int secondDay = 5)
    {
        var book = AlterationBook.New(tag);
        var (expense, party) = book.EnableTds(Section);

        var first = PostCarved(book, VoucherBaseType.Journal, expense, party, "30000.30", book.On(firstDay), "first");
        Assert.Equal(2, first.Lines.Count);
        Assert.Equal(new Money(30000.30m), AmountOn(first, party.Id));

        var second = PostCarved(book, VoucherBaseType.Journal, expense, party, "30000.30", book.On(secondDay), "second");
        Assert.Equal(3, second.Lines.Count);
        Assert.Equal(new Money(27000.30m), AmountOn(second, party.Id));
        Assert.Equal(new Money(3000.00m), AmountOn(second, TdsPayable(book.Company).Id));

        return (book, expense, party, first, second);
    }

    private static void AssertSecondUnmoved(AlterationBook book, Voucher second, DomainLedger party)
    {
        var after = book.Company.FindVoucher(second.Id)!;
        Assert.Equal(3, after.Lines.Count);
        Assert.Equal(new Money(27000.30m), AmountOn(after, party.Id));
        Assert.Equal(new Money(3000.00m), AmountOn(after, TdsPayable(book.Company).Id));
    }

    // ================================================================ (A) the ACQUIRE direction — posting order

    /// <summary>
    /// 🔴 <b>A narration-only alteration of the FIRST of two same-dated journals must change nothing.</b> The second
    /// voucher was posted LATER but is dated the SAME DAY, so a projection that selects by date counted it as
    /// "prior" to the first — and the first, correctly below threshold at posting, ACQUIRED a ₹3,000 withholding:
    /// 2 lines became 3, the party's credit fell from ₹30,000.30 to ₹27,000.30 and a statutory liability was
    /// created by editing a narration.
    /// </summary>
    [Fact]
    public void Altering_the_first_of_two_same_dated_assessments_does_not_acquire_a_withholding()
    {
        var (book, _, party, first, second) = TwoAssessments("acquire-same-day");
        using var _book = book;

        var entry = OpenOrThrow(book, first.Id);

        // The panel is read BEFORE any keystroke — RehydrateFrom ends in Recalculate().
        Assert.Equal("0.00", entry.TdsAmountText);
        Assert.Equal("30,000.30", entry.TdsNetPayableText);

        entry.Narration = "narration only";
        Assert.True(entry.AcceptAlteration(), entry.Message);

        var after = book.Company.FindVoucher(first.Id)!;
        Assert.Equal(2, after.Lines.Count);
        Assert.Equal(new Money(30000.30m), AmountOn(after, party.Id));
        Assert.DoesNotContain(after.Lines, l => l.LedgerId == TdsPayable(book.Company).Id);
        Assert.Equal(Money.Zero, after.Lines.Single(l => l.HasTds).Tds!.TdsAmount);

        // And the sibling that DOES cross is untouched by all of this.
        AssertSecondUnmoved(book, second, party);
    }

    /// <summary>
    /// 🔴 <b>The panel on an ALTERING screen must state the figure Accept will post.</b> The panel called the carve
    /// engine without the voucher's own posting moment, so a below-threshold ₹30,000.30 fee re-opened reading
    /// "TDS 194J(b) @ 10%: ₹3,000.00 withheld · Net payable … ₹27,000.30" while Accept posted the full gross and no
    /// payable leg at all — a wrong figure on screen before any keystroke.
    /// </summary>
    [Fact]
    public void The_withholding_panel_on_an_altering_screen_states_what_accept_will_post()
    {
        using var book = AlterationBook.New("panel-self");
        var (expense, party) = book.EnableTds(Section);
        var posted = PostCarved(book, VoucherBaseType.Journal, expense, party, "30000.30");

        var entry = OpenOrThrow(book, posted.Id);
        Assert.True(entry.ShowTdsPanel);
        Assert.Equal("0.00", entry.TdsAmountText);
        Assert.Equal("30,000.30", entry.TdsNetPayableText);
        Assert.Contains("below threshold", entry.TdsSummary, StringComparison.OrdinalIgnoreCase);

        Assert.True(entry.AcceptAlteration(), entry.Message);
        var after = book.Company.FindVoucher(posted.Id)!;
        Assert.Equal(2, after.Lines.Count);
        Assert.Equal(new Money(30000.30m), AmountOn(after, party.Id));
    }

    // ================================================================ (B) the LOSE direction — the applies bit

    /// <summary>🔴 Route 1: an ordinary operator action — cancelling a duplicate — moves the year's aggregate under a
    /// voucher that has already withheld. Refused by name, with the figures intact.</summary>
    [Fact]
    public void Cancelling_a_sibling_after_posting_does_not_silently_withdraw_the_withholding()
    {
        var (book, _, party, first, second) = TwoAssessments("lose-cancel");
        using var _book = book;

        new LedgerService(book.Company).Cancel(first.Id);
        book.Storage.Save(book.Company);

        var message = RefuseOrThrow(book, second.Id, e => e.Narration = "narration only");
        Assert.Contains("3000.00", message.Replace(",", ""), StringComparison.Ordinal);
        Assert.Contains("already been reported", message, StringComparison.OrdinalIgnoreCase);
        AssertSecondUnmoved(book, second, party);
    }

    /// <summary>🔴 Route 2: the same, by DELETING the sibling rather than cancelling it.</summary>
    [Fact]
    public void Deleting_a_sibling_after_posting_does_not_silently_withdraw_the_withholding()
    {
        var (book, _, party, first, second) = TwoAssessments("lose-delete");
        using var _book = book;

        new LedgerService(book.Company).Delete(first.Id);
        book.Storage.Save(book.Company);

        var message = RefuseOrThrow(book, second.Id, e => e.Narration = "narration only");
        Assert.Contains("already been reported", message, StringComparison.OrdinalIgnoreCase);
        AssertSecondUnmoved(book, second, party);
    }

    /// <summary>
    /// 🔴 Route 3, which needs no sibling manipulation at all: moving the voucher's own DATE into the next financial
    /// year empties the cumulative aggregate. A date move is warn-and-proceed by S5a's contract, so the alteration
    /// reported success and reported the date change — while saying nothing about the ₹3,000 statutory liability it
    /// had just removed.
    /// </summary>
    [Fact]
    public void Re_dating_a_withholding_into_the_next_financial_year_is_refused_rather_than_silently_dropped()
    {
        var (book, _, party, _, second) = TwoAssessments("lose-redate", firstDay: 5, secondDay: 6);
        using var _book = book;

        var nextFy = book.Company.FinancialYearStart.AddYears(1).AddDays(10);
        var message = RefuseOrThrow(book, second.Id, e => e.DateText = ApexDate.Format(nextFy));
        Assert.Contains("already been reported", message, StringComparison.OrdinalIgnoreCase);
        AssertSecondUnmoved(book, second, party);
        Assert.Equal(book.On(6), book.Company.FindVoucher(second.Id)!.Date);
    }

    /// <summary>
    /// 🔴 <b>The assessable base is part of the answer, and it is not on the pin.</b> <c>AssessableExGst</c> re-reads
    /// the chart of accounts, so re-grouping an ordinary debit ledger under Duties &amp; Taxes shrinks the base:
    /// measured, a filed ₹12,000.00 deduction was restated to ₹10,000.00 and ₹2,000 moved back to the party, with
    /// the rate, the section and the deductee all unchanged so none of the three shipped refusals fired.
    /// </summary>
    [Fact]
    public void Re_grouping_a_debit_ledger_under_duties_and_taxes_cannot_restate_a_filed_deduction()
    {
        using var book = AlterationBook.New("lose-assessable");
        var (expense, party) = book.EnableTds(Section);
        var levy = book.Ledger("Reimbursable Levy", "Indirect Expenses");

        var posted = book.Post(VoucherBaseType.Journal, book.On(), new[]
        {
            (expense, DrCr.Debit, "100000.30"),
            (levy, DrCr.Debit, "20000.00"),
            (party, DrCr.Credit, "120000.30"),
        });
        Assert.Equal(new Money(108000.30m), AmountOn(posted, party.Id));
        Assert.Equal(new Money(12000.00m), AmountOn(posted, TdsPayable(book.Company).Id));
        Assert.Equal(new Money(120000.30m), posted.Lines.Single(l => l.HasTds).Tds!.AssessableValue);

        levy.GroupId = book.Company.FindGroupByName("Duties & Taxes")!.Id;

        var message = RefuseOrThrow(book, posted.Id, e => e.Narration = "narration only");
        Assert.Contains("assessable base", message, StringComparison.OrdinalIgnoreCase);

        var after = book.Company.FindVoucher(posted.Id)!;
        Assert.Equal(new Money(108000.30m), AmountOn(after, party.Id));
        Assert.Equal(new Money(12000.00m), AmountOn(after, TdsPayable(book.Company).Id));
        Assert.Equal(new Money(120000.30m), after.Lines.Single(l => l.HasTds).Tds!.AssessableValue);
    }

    /// <summary>
    /// The paired SILENCE case: when nothing has moved, the guard must not fire. A withholding voucher whose whole
    /// world is unchanged re-accepts, and it re-accepts to the SAME figures — so the refusal above cannot become a
    /// rule that refuses every alteration.
    /// </summary>
    [Fact]
    public void A_withholding_whose_world_has_not_moved_re_accepts_unchanged()
    {
        var (book, _, party, _, second) = TwoAssessments("silence");
        using var _book = book;

        var before = book.Export();
        var beforeDisk = book.ExportReloaded();

        var entry = OpenOrThrow(book, second.Id);
        Assert.True(entry.AcceptAlteration(), entry.Message);

        AssertSecondUnmoved(book, second, party);
        Assert.Equal(before, book.Export());
        Assert.Equal(beforeDisk, book.ExportReloaded());
    }

    /// <summary>
    /// And the guard is stated on the OPERATOR'S input, not on "nothing may ever change": amending the gross is a
    /// legitimate re-carve and still goes through, even in a book where a sibling makes the cumulative aggregate
    /// move. ₹30,000.30 → ₹80,000.55 crosses on its own, so §194J(b) at 10% withholds ₹8,000 (₹8,000.055 to the
    /// nearest rupee, round-half-up) and the party is credited ₹72,000.55.
    /// </summary>
    [Fact]
    public void Amending_the_gross_still_re_carves_while_the_drift_guard_is_in_place()
    {
        var (book, _, party, _, second) = TwoAssessments("amend-through-guard");
        using var _book = book;

        var entry = OpenOrThrow(book, second.Id);
        foreach (var row in entry.Lines.Where(l => l.IsComplete)) row.AmountText = "80000.55";
        entry.Recalculate();
        Assert.True(entry.AcceptAlteration(), entry.Message);

        var after = book.Company.FindVoucher(second.Id)!;
        Assert.Equal(new Money(72000.55m), AmountOn(after, party.Id));
        Assert.Equal(new Money(8000.00m), AmountOn(after, TdsPayable(book.Company).Id));
    }

    // ================================================================ (C) the reverse-charge pin's five clauses

    private static void EnableAdvancedGst(AlterationBook book)
    {
        var gst = new GstService(book.Company);
        gst.EnableGst(new GstConfig
        {
            HomeStateCode = "27",
            Gstin = GstinMaharashtra,
            RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = book.Company.FinancialYearStart,
            Periodicity = GstReturnPeriodicity.Monthly,
        });
        gst.SeedAdvancedGst();
    }

    private static DomainLedger RcmExpense(AlterationBook book, string name, string nature, int rateBp)
    {
        var l = book.Ledger(name, "Indirect Expenses");
        l.SalesPurchaseGst = new StockItemGstDetails
        {
            Taxability = GstTaxability.Taxable,
            RateBasisPoints = rateBp,
            SupplyType = GstSupplyType.Services,
            ReverseChargeApplicable = true,
            RcmCategoryId = book.Company.Gst!.RcmCategories.First(x => x.SupplyNature == nature).Id,
        };
        return l;
    }

    private static DomainLedger RcmSupplier(AlterationBook book, string name, string gstin, string state)
    {
        var l = book.Ledger(name, "Sundry Creditors");
        l.PartyGst = new PartyGstDetails
        {
            RegistrationType = GstRegistrationType.Regular,
            Gstin = gstin,
            StateCode = state,
        };
        return l;
    }

    /// <summary>
    /// 🔴 <b>The NOTIFIED RATE arm of the reverse-charge pin, with the leg count held CONSTANT.</b> The only shipped
    /// test of the pin moved the supplier's state, which turns one IGST pair into a CGST+SGST split — 2 legs into 4
    /// — so it passes on leg count alone and never exercises ledger, side, head, rate or ITC scheme. Here the
    /// notified rate moves from 18% to 12% with the same ledger, the same side, the same head and the same number
    /// of legs: only <c>RateBasisPoints</c> differs, and the alteration must still be refused by name.
    /// </summary>
    [Fact]
    public void A_notified_rate_that_moved_after_posting_is_refused_though_the_leg_count_is_unchanged()
    {
        using var book = AlterationBook.New("rcm-rate-drift");
        EnableAdvancedGst(book);
        var fees = RcmExpense(book, "Legal Fees", "Legal", 1800);
        var advocate = RcmSupplier(book, "Advocate (Gujarat)", GstinGujarat, "24");

        var posted = book.Post(VoucherBaseType.Purchase, book.On(),
            new[] { (fees, DrCr.Debit, "10000.50"), (advocate, DrCr.Credit, "10000.50") });
        var postedRcm = posted.Lines.Where(l => l.Gst is { IsReverseCharge: true }).ToList();
        Assert.Equal(2, postedRcm.Count);
        Assert.All(postedRcm, l => Assert.Equal(1800, l.Gst!.RateBasisPoints));

        // The SAME notified category, re-notified at 12% — the shape a rate revision produces.
        var original = book.Company.Gst!.RcmCategories.First(c => c.SupplyNature == "Legal");
        var revised = new RcmCategory(
            Guid.NewGuid(), original.Notification, original.Stream, original.SupplyNature, original.SupplyType,
            original.HsnSac, 1200, original.SupplierQualifier, original.RecipientQualifier,
            original.EffectiveFrom, original.EffectiveTo, original.Label + " (revised)");
        book.Company.Gst!.AddRcmCategory(revised);
        fees.SalesPurchaseGst!.RcmCategoryId = revised.Id;

        var message = RefuseOrThrow(book, posted.Id, e => e.Narration = "harmless");
        Assert.Contains("no longer re-computes to the same shape", message, StringComparison.OrdinalIgnoreCase);

        var after = book.Company.FindVoucher(posted.Id)!;
        Assert.All(after.Lines.Where(l => l.Gst is { IsReverseCharge: true }),
            l => Assert.Equal(new Money(1800.09m), l.Amount));
    }

    /// <summary>
    /// 🔴 <b>The pin's five clauses, asserted directly.</b> Each of ledger, side, head, rate and ITC scheme is moved
    /// on its own with the LEG COUNT held constant, so no clause can be dropped from the signature without one of
    /// these failing. A round trip cannot reach this: the shapes that differ in exactly one clause are not all
    /// constructible through the screen.
    /// </summary>
    [Fact]
    public void Every_clause_of_the_reverse_charge_signature_is_compared()
    {
        var output = Guid.NewGuid();
        var input = Guid.NewGuid();
        var other = Guid.NewGuid();
        var taxable = new Money(10000.50m);

        static EntryLine Rcm(Guid ledgerId, DrCr side, GstTaxHead head, int rateBp, RcmItcScheme? scheme) =>
            new(ledgerId, new Money(1800.09m), side,
                gst: new GstLineTax(head, rateBp, new Money(10000.50m), isReverseCharge: true, rcmScheme: scheme));

        var posted = new[]
        {
            Rcm(output, DrCr.Credit, GstTaxHead.Integrated, 1800, null),
            Rcm(input, DrCr.Debit, GstTaxHead.Integrated, 1800, RcmItcScheme.OtherRcm),
        };
        var pin = new VoucherAlterationDerivedLegs.RcmPin(VoucherAlterationDerivedLegs.SignatureOf(posted));

        Assert.True(pin.Matches(posted));
        Assert.True(pin.Matches(posted.Reverse()));                       // order-independent, by construction
        Assert.True(pin.Matches(new[]                                     // amounts are free to move
        {
            new EntryLine(output, new Money(3600.09m), DrCr.Credit,
                gst: new GstLineTax(GstTaxHead.Integrated, 1800, new Money(20000.50m), isReverseCharge: true)),
            new EntryLine(input, new Money(3600.09m), DrCr.Debit,
                gst: new GstLineTax(GstTaxHead.Integrated, 1800, new Money(20000.50m), isReverseCharge: true,
                    rcmScheme: RcmItcScheme.OtherRcm)),
        }));

        // LEDGER — the input leg re-pointed at a different ledger.
        Assert.False(pin.Matches(new[]
        {
            Rcm(output, DrCr.Credit, GstTaxHead.Integrated, 1800, null),
            Rcm(other, DrCr.Debit, GstTaxHead.Integrated, 1800, RcmItcScheme.OtherRcm),
        }));

        // SIDE — the liability and the credit swapped.
        Assert.False(pin.Matches(new[]
        {
            Rcm(output, DrCr.Debit, GstTaxHead.Integrated, 1800, null),
            Rcm(input, DrCr.Credit, GstTaxHead.Integrated, 1800, RcmItcScheme.OtherRcm),
        }));

        // TAX HEAD — Integrated re-stamped as Central, same two legs.
        Assert.False(pin.Matches(new[]
        {
            Rcm(output, DrCr.Credit, GstTaxHead.Central, 1800, null),
            Rcm(input, DrCr.Debit, GstTaxHead.Central, 1800, RcmItcScheme.OtherRcm),
        }));

        // RATE — 18% re-notified at 12%.
        Assert.False(pin.Matches(new[]
        {
            Rcm(output, DrCr.Credit, GstTaxHead.Integrated, 1200, null),
            Rcm(input, DrCr.Debit, GstTaxHead.Integrated, 1200, RcmItcScheme.OtherRcm),
        }));

        // ITC SCHEME — 4A(3) re-routed to 4A(2), which is a different table of GSTR-3B.
        Assert.False(pin.Matches(new[]
        {
            Rcm(output, DrCr.Credit, GstTaxHead.Integrated, 1800, null),
            Rcm(input, DrCr.Debit, GstTaxHead.Integrated, 1800, RcmItcScheme.ImportOfServices),
        }));

        Assert.Equal(new Money(10000.50m), taxable);   // (the taxable value is deliberately NOT in the signature)
    }

    /// <summary>
    /// 🔴 <b>Import of services is refused at the DOOR, and the refusal writes nothing.</b> The supply KIND is keyed
    /// at entry and persisted nowhere, so the screen used to re-open a §5(3) voucher with the routing selector
    /// silently reset to "Domestic inward supply (§9(3) / §9(4))" — a wrong routing on screen — and then refuse at
    /// accept, by which point the re-resolution had already ADDED "RCM Output CGST" and "RCM Output SGST" to the
    /// chart of accounts on a book that had never held them.
    /// </summary>
    [Fact]
    public void An_import_of_services_reverse_charge_is_refused_at_the_door_and_conjures_no_ledger()
    {
        using var book = AlterationBook.New("rcm-import");
        EnableAdvancedGst(book);
        var fees = RcmExpense(book, "Consulting (Imported)", "Legal", 1800);
        // Deliberately an INTRA-state supplier, so a domestic re-resolution would want CGST+SGST heads this book
        // has never created — the shape that made the ledger-conjuring visible.
        var supplier = RcmSupplier(book, "Overseas Consultant", GstinMaharashtra, "27");

        var posted = book.Post(VoucherBaseType.Purchase, book.On(),
            new[] { (fees, DrCr.Debit, "20000.55"), (supplier, DrCr.Credit, "20000.55") },
            configure: e => e.SelectedRcmSupplyKind =
                e.RcmSupplyKinds.First(k => k.Kind == RcmService.SupplyKind.ImportOfServices));

        Assert.Contains(posted.Lines, l => l.Gst is { RcmScheme: RcmItcScheme.ImportOfServices });

        var before = book.Export();
        var ledgersBefore = book.Company.Ledgers.Select(l => l.Name).OrderBy(n => n).ToList();

        var open = book.ForAlter(posted.Id);
        Assert.True(open.IsRefused);
        Assert.Contains("IMPORT OF SERVICES", open.Refusal!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("4A(2)", open.Refusal!);

        Assert.Equal(before, book.Export());
        Assert.Equal(ledgersBefore, book.Company.Ledgers.Select(l => l.Name).OrderBy(n => n).ToList());
    }

    /// <summary>
    /// 🔴 <b>A REFUSED alteration leaves the chart of accounts exactly as it found it.</b> The state-move refusal
    /// re-resolves an inter-state pair as an intra-state one, so the builder creates the CGST/SGST RCM output
    /// ledgers before the shape check can refuse — measured, they survived the refusal AND were then persisted by
    /// the next unrelated save. Both halves are asserted here: nothing new on the company, and nothing new on disk
    /// after an ordinary later save.
    /// </summary>
    [Fact]
    public void A_refused_re_stamp_leaves_no_new_ledger_behind_even_after_a_later_save()
    {
        using var book = AlterationBook.New("rcm-refusal-leak");
        EnableAdvancedGst(book);
        var fees = RcmExpense(book, "Legal Fees", "Legal", 1800);
        var advocate = RcmSupplier(book, "Advocate (Gujarat)", GstinGujarat, "24");

        var posted = book.Post(VoucherBaseType.Purchase, book.On(),
            new[] { (fees, DrCr.Debit, "10000.50"), (advocate, DrCr.Credit, "10000.50") });
        var plain = book.PostPlainPair(VoucherBaseType.Journal, 5555.55m, "unrelated");

        advocate.PartyGst!.StateCode = "27";
        advocate.PartyGst!.Gstin = GstinMaharashtra;

        var ledgersBefore = book.Company.Ledgers.Select(l => l.Name).OrderBy(n => n).ToList();
        var message = RefuseOrThrow(book, posted.Id, e => e.Narration = "harmless");
        Assert.Contains("no longer re-computes to the same shape", message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(ledgersBefore, book.Company.Ledgers.Select(l => l.Name).OrderBy(n => n).ToList());

        // Now do something ordinary and legitimate that SAVES: any leak would ride to disk on this.
        var other = OpenOrThrow(book, plain.Id);
        other.Narration = "unrelated, altered";
        Assert.True(other.AcceptAlteration(), other.Message);

        var reloaded = book.Storage.Load(book.Storage.ListCompanies().Single(e => e.Name == book.Company.Name));
        Assert.Equal(ledgersBefore, reloaded.Ledgers.Select(l => l.Name).OrderBy(n => n).ToList());
    }

    // ================================================================ (D) the import-only guards, on import shapes

    /// <summary>Posts a hand-built voucher straight through the engine — the door the canonical importer uses, and
    /// the only way to produce the withholding shapes the entry screen cannot key.</summary>
    private static Voucher PostRaw(AlterationBook book, VoucherBaseType baseType, params EntryLine[] lines)
    {
        var voucher = new Voucher(Guid.NewGuid(), book.Type(baseType).Id, book.On(), lines);
        var posted = new LedgerService(book.Company).Post(voucher);
        book.Storage.Save(book.Company);
        return posted;
    }

    private static TdsLineTax Detail(Company c, DomainLedger party, Money assessable, Money tds) =>
        new(c.FindNatureOfPaymentByCode(Section)!.Id, Section, assessable, 1000, tds, party.Id, panApplied: true);

    /// <summary>
    /// 🔴 <b>The four "this can only have arrived from an import" refusals, each on a fixture that reaches it.</b>
    /// Every one of them writes a distinct operator-facing sentence that no test had ever seen — and the
    /// party-line one is the sharpest, because with the guard weakened the zero-party case indexes an empty list
    /// and would THROW rather than refuse, so the difference between a named refusal and an unhandled exception
    /// was untested.
    /// </summary>
    [Fact]
    public void The_import_only_withholding_shapes_are_each_refused_by_their_own_name()
    {
        using var book = AlterationBook.New("import-guards");
        var (expense, party) = book.EnableTds(Section);
        var payable = TdsPayable(book.Company);
        var other = book.Ledger("Other Creditor", "Sundry Creditors");
        var gross = new Money(120000.30m);

        // (1) below-threshold shape (the detail on the deductee's own line) recording a DEDUCTED amount.
        var nonZeroOnParty = PostRaw(book, VoucherBaseType.Journal,
            new EntryLine(expense.Id, gross, DrCr.Debit),
            new EntryLine(party.Id, gross, DrCr.Credit, tds: Detail(book.Company, party, gross, new Money(12000m))));
        Assert.Contains("always sits on a separate TDS Payable leg",
            VoucherAlterationDerivedLegs.Invert(book.Company, nonZeroOnParty, out _)!, StringComparison.Ordinal);

        // (2) a ZERO withholding on a leg that is not the deductee's.
        var zeroOffParty = PostRaw(book, VoucherBaseType.Journal,
            new EntryLine(expense.Id, gross, DrCr.Debit),
            new EntryLine(party.Id, new Money(108000.30m), DrCr.Credit),
            new EntryLine(payable.Id, new Money(12000m), DrCr.Credit,
                tds: Detail(book.Company, party, gross, Money.Zero)));
        Assert.Contains("records a zero withholding on a leg that is not the deductee's",
            VoucherAlterationDerivedLegs.Invert(book.Company, zeroOffParty, out _)!, StringComparison.Ordinal);

        // (3) the payable leg's AMOUNT disagreeing with the detail it carries.
        var payableDisagrees = PostRaw(book, VoucherBaseType.Journal,
            new EntryLine(expense.Id, gross, DrCr.Debit),
            new EntryLine(party.Id, new Money(108000.30m), DrCr.Credit),
            new EntryLine(payable.Id, new Money(12000m), DrCr.Credit,
                tds: Detail(book.Company, party, gross, new Money(11000m))));
        Assert.Contains("The two disagree",
            VoucherAlterationDerivedLegs.Invert(book.Company, payableDisagrees, out _)!, StringComparison.Ordinal);

        // (4a) the deductee credited on NO line at all — the arm that would throw if the guard were weakened.
        var noPartyLine = PostRaw(book, VoucherBaseType.Journal,
            new EntryLine(expense.Id, gross, DrCr.Debit),
            new EntryLine(other.Id, new Money(108000.30m), DrCr.Credit),
            new EntryLine(payable.Id, new Money(12000m), DrCr.Credit,
                tds: Detail(book.Company, party, gross, new Money(12000m))));
        Assert.Contains("has no credit line left on it",
            VoucherAlterationDerivedLegs.Invert(book.Company, noPartyLine, out _)!, StringComparison.Ordinal);

        // (4b) the deductee credited on TWO lines — no single leg to add the withholding back onto.
        var twoPartyLines = PostRaw(book, VoucherBaseType.Journal,
            new EntryLine(expense.Id, gross, DrCr.Debit),
            new EntryLine(party.Id, new Money(54000.15m), DrCr.Credit),
            new EntryLine(party.Id, new Money(54000.15m), DrCr.Credit),
            new EntryLine(payable.Id, new Money(12000m), DrCr.Credit,
                tds: Detail(book.Company, party, gross, new Money(12000m))));
        Assert.Contains("credited on 2 separate lines",
            VoucherAlterationDerivedLegs.Invert(book.Company, twoPartyLines, out _)!, StringComparison.Ordinal);
    }
}
