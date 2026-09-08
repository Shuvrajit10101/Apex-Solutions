using System;
using System.Collections.Generic;
using System.Linq;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Xunit;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Ledger.Tests;

/// <summary>
/// Census row 5.5 — <b>Insert Voucher (Alt+I)</b>, the number-series half: <see cref="VoucherInsertion"/>.
///
/// <para><b>FIDELITY (R7 / RULING 14 tier 1 — the corpus is gone, so this is the vendor's own help).</b>
/// <c>help.tallysolutions.com/tally-prime/accounting-financial-reports/day-book-tally/</c> states the consequence
/// verbatim: <i>"If you insert a voucher between two vouchers with the method of voucher numbering set to
/// automatic, then all the existing vouchers of that particular type recorded on all subsequent days are
/// renumbered."</i> and gives the worked example this file's first test is a direct transcription of:
/// <i>"if you insert a sales voucher between sales voucher numbers 2 and 3, then the inserted voucher will attain
/// voucher number 3 and the existing voucher number 3 will be listed as voucher number 4 … Consequently, all the
/// existing sales vouchers recorded on all subsequent days will be renumbered."</i></para>
///
/// <para>The per-method scoping is the vendor's too, from
/// <c>help.tallysolutions.com/use-voucher-numbering-methods/</c>: the insertion/deletion numbering behaviour is
/// <i>"applicable, only if you have selected <b>Automatic</b> or <b>Multi-user Auto</b> numbering method"</i>.
/// The three tests that pin Manual / None / Automatic-Manual-Override are therefore pinning an ATTESTED
/// non-behaviour, not an assumption.</para>
///
/// <para>🔴 <b>WHY EVERY ASSERTION IS ON THE WHOLE SERIES AND NOT ON ONE VOUCHER.</b> The defect this row can
/// produce is a DUPLICATE or a GAP in a statutory document series — a wrong-document defect on a filed return,
/// not a UI nit. Asserting "voucher 3 became 4" would pass on an engine that also left two vouchers on 5.
/// <see cref="AssertContiguousAndUnique"/> is therefore run after every mutating test: it re-derives the whole
/// type's number set and fails on any repeat or any hole.</para>
/// </summary>
public sealed class VoucherInsertionTests
{
    private static readonly DateOnly FyStart = new(2024, 4, 1);

    private static Company Seed(string name = "Insert Co") =>
        CompanyFactory.CreateSeeded(name, FyStart, FyStart);

    private static DomainLedger Party(Company c) =>
        c.FindLedgerByName("Acme Traders") ?? AddParty(c, "Acme Traders");

    private static DomainLedger AddParty(Company c, string name)
    {
        var l = new DomainLedger(Guid.NewGuid(), name, c.FindGroupByName("Sundry Debtors")!.Id,
                                 Money.Zero, openingIsDebit: true);
        c.AddLedger(l);
        return l;
    }

    private static DomainLedger Sales(Company c)
    {
        if (c.FindLedgerByName("Sales") is { } existing) return existing;
        var l = new DomainLedger(Guid.NewGuid(), "Sales", c.FindGroupByName("Sales Accounts")!.Id,
                                 Money.Zero, openingIsDebit: false);
        c.AddLedger(l);
        return l;
    }

    /// <summary>Posts a balanced Journal of <paramref name="type"/> on <paramref name="on"/> through the real
    /// engine, so the voucher is genuinely posted and genuinely numbered by the shipped numbering path.</summary>
    private static Voucher Post(Company c, VoucherType type, DateOnly on, decimal rupees = 1000m)
    {
        var v = new Voucher(Guid.NewGuid(), type.Id, on, new[]
        {
            new EntryLine(Party(c).Id, Money.FromRupees(rupees), DrCr.Debit),
            new EntryLine(Sales(c).Id, Money.FromRupees(rupees), DrCr.Credit),
        });
        new LedgerService(c).Post(v);
        return v;
    }

    /// <summary>
    /// Replaces the company's Journal type with one carrying <paramref name="method"/>, and returns it. The
    /// numbering method is a property of the TYPE, so a per-method test has to configure the type rather than
    /// the voucher.
    /// </summary>
    private static VoucherType TypeWith(Company c, NumberingMethod method)
    {
        var journal = c.FindVoucherTypeByName("Journal")!;
        journal.Numbering = method;
        return journal;
    }

    /// <summary>
    /// 🔴 <b>THE INVARIANT THIS ROW EXISTS TO PROTECT.</b> Every numbered voucher of <paramref name="type"/> in
    /// the book carries a DISTINCT number, and those numbers form a contiguous run from 1 — no duplicate (two
    /// documents claiming one identity) and no gap (a missing document on a statutory series). Run after every
    /// mutating test, because a per-voucher assertion cannot see either failure.
    /// </summary>
    private static void AssertContiguousAndUnique(Company c, VoucherType type)
    {
        var numbers = c.Vouchers.Where(v => v.TypeId == type.Id && v.Number > 0)
                                .Select(v => v.Number).OrderBy(n => n).ToList();
        Assert.Equal(numbers.Count, numbers.Distinct().Count());          // no duplicate
        Assert.Equal(Enumerable.Range(1, numbers.Count).ToList(), numbers); // no gap, and starts at 1
    }

    // ============================================================ (a) THE VENDOR'S WORKED EXAMPLE, TRANSCRIBED

    /// <summary>
    /// 🔴 <b>THE VENDOR'S OWN EXAMPLE, ASSERTED LITERALLY.</b> Five sales vouchers numbered 1..5 on five
    /// consecutive days; insert above the one numbered 3. The vendor: the newcomer <i>"will attain voucher
    /// number 3"</i>, the existing 3 <i>"will be listed as voucher number 4"</i>, and <i>"all the existing sales
    /// vouchers recorded on all subsequent days will be renumbered"</i> — so 4→5 and 5→6 as well, while 1 and 2
    /// (recorded BEFORE the insertion point) are untouched.
    ///
    /// <para>This fails on today's <c>main</c> at the first line: <c>VoucherInsertion</c> does not exist there,
    /// and no insert-at-position code of any kind does (the census re-measured that on 2026-09-07).</para>
    /// </summary>
    [Fact]
    public void The_vendors_worked_example_renumbers_the_anchor_and_every_later_voucher()
    {
        var c = Seed();
        var type = TypeWith(c, NumberingMethod.Automatic);
        var posted = Enumerable.Range(0, 5).Select(i => Post(c, type, FyStart.AddDays(i))).ToList();
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, posted.Select(v => v.Number).ToArray());

        var plan = VoucherInsertion.Plan(c, type, anchor: posted[2]);   // the voucher numbered 3

        Assert.True(plan.IsAllowed);
        Assert.Null(plan.Refusal);
        Assert.Equal(3, plan.InsertedNumber);                           // "will attain voucher number 3"
        Assert.True(plan.RewritesExistingNumbers);

        // 3→4, 4→5, 5→6 — and NOTHING else. Vouchers 1 and 2 are not in the plan at all.
        Assert.Equal(
            new[] { (3, 4), (4, 5), (5, 6) },
            plan.Renumbers.Select(r => (r.From, r.To)).ToArray());
        Assert.DoesNotContain(plan.Renumbers, r => r.VoucherId == posted[0].Id || r.VoucherId == posted[1].Id);

        VoucherInsertion.Apply(c, plan);

        Assert.Equal(1, posted[0].Number);
        Assert.Equal(2, posted[1].Number);
        Assert.Equal(4, posted[2].Number);
        Assert.Equal(5, posted[3].Number);
        Assert.Equal(6, posted[4].Number);

        // The newcomer has not been posted yet, so 3 is deliberately VACANT here — the shell fills it in the same
        // breath (MainWindowViewModel.ApplyPendingInsertion). Asserting contiguity now would assert the opposite
        // of the design, so what is asserted instead is that the vacancy is exactly one number wide and is the
        // planned one.
        var after = c.Vouchers.Where(v => v.TypeId == type.Id).Select(v => v.Number).OrderBy(n => n).ToList();
        Assert.Equal(new[] { 1, 2, 4, 5, 6 }, after);
        Assert.DoesNotContain(plan.InsertedNumber, after);
    }

    /// <summary>
    /// The complete round trip: plan, post the newcomer into the vacated number, and the series is once again a
    /// contiguous run 1..6 with no repeat. This is the test that would catch an off-by-one in either direction —
    /// a planner that vacated the wrong number would leave a gap AND a duplicate here, and neither shows up in a
    /// per-voucher assertion.
    /// </summary>
    [Fact]
    public void After_the_newcomer_takes_the_vacated_number_the_series_is_contiguous_and_unique()
    {
        var c = Seed();
        var type = TypeWith(c, NumberingMethod.Automatic);
        var posted = Enumerable.Range(0, 5).Select(i => Post(c, type, FyStart.AddDays(i))).ToList();

        var plan = VoucherInsertion.Plan(c, type, anchor: posted[2]);
        var newcomer = Post(c, type, posted[2].Date);      // the ordinary path gives it max + 1 = 6
        Assert.Equal(6, newcomer.Number);

        // The shell's order, and the reason for it: move the newcomer DOWN first, then shift the successors up.
        // Doing it the other way round puts voucher 5 onto 6 while the newcomer still holds 6.
        newcomer.Number = plan.InsertedNumber;
        VoucherInsertion.Apply(c, plan);

        Assert.Equal(3, newcomer.Number);
        AssertContiguousAndUnique(c, type);
        Assert.Equal(6, c.Vouchers.Count(v => v.TypeId == type.Id));
    }

    // ============================================================ (b) THE THREE METHODS THAT MUST NOT RENUMBER

    /// <summary>
    /// <b>Manual and None do not renumber, and assign no number here</b> — the vendor puts both outside the
    /// insertion/deletion behaviour ("Set/Alter additional numbering details" is unavailable "if you have
    /// selected Manual or None as the method of voucher numbering"). Under Manual the operator types the number;
    /// under None the type carries none at all.
    /// </summary>
    [Theory]
    [InlineData(NumberingMethod.Manual)]
    [InlineData(NumberingMethod.None)]
    public void Manual_and_None_renumber_nothing_and_assign_nothing(NumberingMethod method)
    {
        var c = Seed();
        var type = TypeWith(c, NumberingMethod.Automatic);
        var posted = Enumerable.Range(0, 4).Select(i => Post(c, type, FyStart.AddDays(i))).ToList();
        var before = posted.Select(v => v.Number).ToArray();

        type.Numbering = method;
        var plan = VoucherInsertion.Plan(c, type, anchor: posted[1]);

        Assert.True(plan.IsAllowed);
        Assert.Equal(method, plan.Method);
        Assert.Equal(0, plan.InsertedNumber);
        Assert.Empty(plan.Renumbers);
        Assert.False(plan.RewritesExistingNumbers);

        VoucherInsertion.Apply(c, plan);                       // a no-op by construction
        Assert.Equal(before, posted.Select(v => v.Number).ToArray());
    }

    /// <summary>
    /// <b>Automatic (Manual Override) appends and renumbers nothing</b> — it is outside the vendor's stated
    /// scope, so the newcomer goes to max + 1 and every existing document keeps the number it was issued under.
    /// The documented consequence, stated here rather than hidden: the inserted voucher then sits
    /// chronologically before vouchers carrying LOWER numbers. That is the method's own behaviour, and the shell
    /// says so; it is not a defect of the planner.
    /// </summary>
    [Fact]
    public void AutomaticManualOverride_appends_at_max_plus_one_and_renumbers_nothing()
    {
        var c = Seed();
        var type = TypeWith(c, NumberingMethod.Automatic);
        var posted = Enumerable.Range(0, 4).Select(i => Post(c, type, FyStart.AddDays(i))).ToList();

        type.Numbering = NumberingMethod.AutomaticManualOverride;
        var plan = VoucherInsertion.Plan(c, type, anchor: posted[1]);

        Assert.True(plan.IsAllowed);
        Assert.Equal(5, plan.InsertedNumber);                  // max (4) + 1 — a plain append
        Assert.Empty(plan.Renumbers);
        Assert.Equal(new[] { 1, 2, 3, 4 }, posted.Select(v => v.Number).ToArray());
    }

    /// <summary>
    /// <b>Multi-user Auto renumbers exactly as Automatic does</b> — the vendor names both methods in the same
    /// scoping sentence, so this is attested behaviour rather than an inference from our single-user divergence.
    /// </summary>
    [Fact]
    public void MultiUserAuto_renumbers_exactly_as_Automatic_does()
    {
        var c = Seed();
        var type = TypeWith(c, NumberingMethod.Automatic);
        var posted = Enumerable.Range(0, 4).Select(i => Post(c, type, FyStart.AddDays(i))).ToList();

        type.Numbering = NumberingMethod.MultiUserAuto;
        var plan = VoucherInsertion.Plan(c, type, anchor: posted[1]);

        Assert.Equal(NumberingMethod.MultiUserAuto, plan.Method);
        Assert.Equal(2, plan.InsertedNumber);
        Assert.Equal(new[] { (2, 3), (3, 4), (4, 5) }, plan.Renumbers.Select(r => (r.From, r.To)).ToArray());
    }

    // ============================================================ (c) THE STATUTORY REFUSAL

    /// <summary>
    /// 🔴 <b>THE REFUSAL THAT MAKES THIS ROW SAFE TO SHIP.</b> Renumbering rewrites the DOCUMENT NUMBER of
    /// already-posted vouchers. When one of those has been filed — a generated IRN here — its number is burned on
    /// a return that has left the building, and changing it makes the books disagree with the filing. The planner
    /// refuses outright, names the remedy, and leaves every number where it was.
    ///
    /// <para>The e-invoice is attached to voucher 4, which is INSIDE the shift range of an insert above voucher
    /// 2 — that is what makes this test bite rather than pass vacuously.</para>
    /// </summary>
    [Fact]
    public void An_insert_that_would_renumber_a_FILED_document_is_refused_and_moves_nothing()
    {
        var c = Seed();
        var type = TypeWith(c, NumberingMethod.Automatic);
        var posted = Enumerable.Range(0, 5).Select(i => Post(c, type, FyStart.AddDays(i))).ToList();

        c.AddEInvoiceRecord(EInvoiceRecord.Rehydrate(
            Guid.NewGuid(), posted[3].Id, "JV/4", EInvoiceStatus.Generated,
            irn: new string('a', 64), ackNo: "112400000000001", ackDate: FyStart.AddDays(3),
            signedQr: "qr", signedJson: null, cancelledOn: null, cancelReasonCode: null));
        Assert.True(MasterDeletionRules.IsFiledStatutoryDocument(c, posted[3].Id));

        var plan = VoucherInsertion.Plan(c, type, anchor: posted[1]);

        Assert.False(plan.IsAllowed);
        Assert.Equal(VoucherInsertion.FiledDocumentRefusal, plan.Refusal);
        Assert.Empty(plan.Renumbers);
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, posted.Select(v => v.Number).ToArray());
        AssertContiguousAndUnique(c, type);

        // A caller that ignores IsAllowed fails LOUDLY rather than quietly rewriting a filed number.
        var ex = Assert.Throws<InvalidOperationException>(() => VoucherInsertion.Apply(c, plan));
        Assert.Contains("Refused insertion plan applied", ex.Message);
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, posted.Select(v => v.Number).ToArray());
    }

    /// <summary>
    /// The mirror of the refusal, and the reason it is scoped to the SHIFT RANGE rather than to the book: a filed
    /// document that sits BEFORE the insertion point is not renumbered by it, so the insert proceeds. A guard
    /// that refused on "the book contains any filed document" would make Insert unusable in every real company
    /// after the first e-invoice — the failure mode of an over-broad freeze.
    /// </summary>
    [Fact]
    public void A_filed_document_BEFORE_the_insertion_point_does_not_block_it()
    {
        var c = Seed();
        var type = TypeWith(c, NumberingMethod.Automatic);
        var posted = Enumerable.Range(0, 5).Select(i => Post(c, type, FyStart.AddDays(i))).ToList();

        c.AddEInvoiceRecord(EInvoiceRecord.Rehydrate(
            Guid.NewGuid(), posted[0].Id, "JV/1", EInvoiceStatus.Generated,
            irn: new string('b', 64), ackNo: "112400000000002", ackDate: FyStart,
            signedQr: "qr", signedJson: null, cancelledOn: null, cancelReasonCode: null));

        var plan = VoucherInsertion.Plan(c, type, anchor: posted[3]);   // shift range is {4, 5} only

        Assert.True(plan.IsAllowed);
        Assert.Equal(4, plan.InsertedNumber);
        Assert.Equal(new[] { (4, 5), (5, 6) }, plan.Renumbers.Select(r => (r.From, r.To)).ToArray());
        Assert.Equal(1, posted[0].Number);                              // the filed one never moves
    }

    // ============================================================ (d) THE EDGES

    /// <summary>
    /// An insert above the LAST voucher of the series is an ordinary append: there is no successor to shift, so
    /// nothing is renumbered and the newcomer takes max + 1 — the same number Add Voucher would have given it.
    /// </summary>
    [Fact]
    public void An_insert_after_the_last_voucher_of_the_type_is_a_plain_append()
    {
        var c = Seed();
        var type = TypeWith(c, NumberingMethod.Automatic);
        var posted = Enumerable.Range(0, 3).Select(i => Post(c, type, FyStart.AddDays(i))).ToList();

        // The anchor is the last voucher; its own number is at/after the point, so it DOES shift.
        var onLast = VoucherInsertion.Plan(c, type, anchor: posted[2]);
        Assert.Equal(3, onLast.InsertedNumber);
        Assert.Equal(new[] { (3, 4) }, onLast.Renumbers.Select(r => (r.From, r.To)).ToArray());
    }

    /// <summary>
    /// 🔴 <b>THE CROSS-TYPE CASE, WHICH IS THE ONE THE VENDOR'S EXAMPLE DOES NOT COVER.</b> The vendor inserts a
    /// SALES voucher above a SALES voucher, so "the number the newcomer takes" is unambiguous there. Inserting a
    /// voucher of type B above a voucher of type A is not stated, and the planner's labelled derivation is the
    /// one sentence that IS attested — renumbering is <i>"of that particular type"</i>. So type A's numbers are
    /// NEVER touched, and the newcomer is placed within its OWN series at the anchor's position in Day Book
    /// order.
    /// </summary>
    [Fact]
    public void Inserting_above_a_voucher_of_ANOTHER_type_never_touches_that_types_numbers()
    {
        var c = Seed();
        var journal = TypeWith(c, NumberingMethod.Automatic);
        var payment = c.FindVoucherTypeByName("Payment")!;
        payment.Numbering = NumberingMethod.Automatic;

        // Interleaved by date: J1 J2 P1 J3 P2, one per day.
        var j1 = Post(c, journal, FyStart);
        var j2 = Post(c, journal, FyStart.AddDays(1));
        var p1 = Post(c, payment, FyStart.AddDays(2));
        var j3 = Post(c, journal, FyStart.AddDays(3));
        var p2 = Post(c, payment, FyStart.AddDays(4));
        Assert.Equal(new[] { 1, 2, 3 }, new[] { j1.Number, j2.Number, j3.Number });
        Assert.Equal(new[] { 1, 2 }, new[] { p1.Number, p2.Number });

        // Insert a PAYMENT above the JOURNAL dated day 1 (j2). The first payment at/after that point is p1.
        var plan = VoucherInsertion.Plan(c, payment, anchor: j2);

        Assert.True(plan.IsAllowed);
        Assert.Equal(1, plan.InsertedNumber);
        Assert.Equal(new[] { (1, 2), (2, 3) }, plan.Renumbers.Select(r => (r.From, r.To)).ToArray());

        VoucherInsertion.Apply(c, plan);

        // The JOURNAL series is untouched — "of that particular type".
        Assert.Equal(new[] { 1, 2, 3 }, new[] { j1.Number, j2.Number, j3.Number });
        Assert.Equal(new[] { 2, 3 }, new[] { p1.Number, p2.Number });
    }

    /// <summary>
    /// <b>Apply reads every value from the plan and re-derives none</b>, so the contiguous <c>n → n+1</c> run
    /// cannot cascade an earlier write into a later read. This is asserted by construction: a plan whose targets
    /// are applied in REVERSE order produces the identical book, which is only true if no step reads a number
    /// another step wrote.
    /// </summary>
    [Fact]
    public void Apply_is_order_independent_because_it_never_re_derives_a_number()
    {
        var c = Seed();
        var type = TypeWith(c, NumberingMethod.Automatic);
        var posted = Enumerable.Range(0, 6).Select(i => Post(c, type, FyStart.AddDays(i))).ToList();
        var plan = VoucherInsertion.Plan(c, type, anchor: posted[1]);

        // Apply the SAME plan with its renumbers reversed.
        var reversed = new VoucherInsertionPlan(
            plan.Refusal, plan.InsertedNumber,
            plan.Renumbers.Reverse().ToList(), plan.Method);
        VoucherInsertion.Apply(c, reversed);

        Assert.Equal(new[] { 1, 3, 4, 5, 6, 7 }, posted.Select(v => v.Number).ToArray());
        Assert.Equal(posted.Select(v => v.Number).Distinct().Count(), posted.Count);
    }
}
