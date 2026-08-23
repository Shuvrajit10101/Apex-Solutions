using System;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Desktop.ViewModels;
using Xunit;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.Tests;

/// <summary>
/// <b>§194C's deductee-type branch, at the ALTERATION door — and the grandfathering that keeps history open.</b>
///
/// <para>🔴 <b>The problem this file pins.</b> §194C(1) charges <b>1%</b> to an individual or a Hindu undivided
/// family and <b>2%</b> to anyone else, and until the bifurcation shipped the engine charged 1% to everybody.
/// <c>ApplyReCarve</c> pins <c>RateBasisPoints</c> off the POSTED voucher and refuses a disagreement, so switching
/// the branch on would have made <b>every already-posted non-Ind/HUF §194C voucher unalterable</b> — refused by a
/// message about a rate the operator never chose. The ruling is that such a voucher carries the rate it was posted
/// with, carried by an <b>explicit argument</b> (<c>postedRateBasisPoints</c>, read off the voucher's own stamped
/// <c>TdsLineTax</c>) and never by a date comparison.</para>
///
/// <para><b>How a "pre-bifurcation" voucher is simulated here, stated openly.</b> The branch is live, so the screen
/// can no longer post 100 bp to a company. Every fixture below posts to an <b>Individual</b> party (100 bp, the
/// only rate the pre-bifurcation engine could produce) and then re-types the party. That is not a workaround — it
/// is the <b>identical persisted state</b>: a genuinely pre-bifurcation company voucher and a re-typed individual
/// voucher are the same stamped rate against the same ledger, and no field distinguishes them without a schema
/// change. <c>TdsService.GrandfatheredRate</c> says so in its own doc, and resolves the ambiguity towards keeping
/// history alterable while never restating a posted, reported figure.</para>
///
/// <para><b>Odd paise everywhere</b> (house rule): a ±₹0.50 defect once survived six round-number assertions.</para>
/// </summary>
public sealed class VoucherAlter194CGrandfatherTests
{
    private const string Section = "194C";

    /// <summary>₹50,000.30 — liable through §194C's ₹30,000 SINGLE-transaction limb on the very first bill, so no
    /// fixture has to build up a cumulative. 1% ⇒ ₹500.00; 2% ⇒ ₹1,000.00 (nearest rupee, half-up).</summary>
    private const string Gross = "50000.30";

    private static Money AmountOn(Voucher v, Guid ledgerId) =>
        v.Lines.Where(l => l.LedgerId == ledgerId).Aggregate(Money.Zero, (a, l) => a + l.Amount);

    private static DomainLedger TdsPayable(Company c) =>
        c.Ledgers.First(l => l.TdsTcsClassification == TdsTcsLedgerKind.Tds);

    private static int PostedRateBp(Voucher v) =>
        v.Lines.Select(l => l.Tds).First(t => t is not null)!.RateBasisPoints;

    private static VoucherEntryViewModel OpenOrThrow(AlterationBook book, Guid voucherId)
    {
        var open = book.ForAlter(voucherId);
        Assert.False(open.IsRefused, "Expected the alteration screen to open; refused with: " + open.Refusal);
        return open.Entry!;
    }

    private static Voucher PostCarved(
        AlterationBook book, DomainLedger expense, DomainLedger party, string gross, string? narration = null) =>
        book.Post(VoucherBaseType.Journal, book.On(),
            new[] { (expense, DrCr.Debit, gross), (party, DrCr.Credit, gross) }, narration);

    /// <summary>
    /// A §194C book whose party is an <b>Individual</b>, posts one ₹50,000.30 contract bill at the §194C(1)(i)
    /// 1% arm, and then re-types the party to <paramref name="afterPosting"/> — the persisted shape of a voucher
    /// posted before the deductee-type branch existed.
    /// </summary>
    private static (AlterationBook Book, DomainLedger Expense, DomainLedger Party, Voucher V)
        LegacyOnePercentVoucher(string tag, DeducteeType afterPosting = DeducteeType.Company)
    {
        var book = AlterationBook.New(tag);
        var (expense, party) = book.EnableTds(Section, expenseName: "Contract Work", partyName: "Bharat Builders");
        party.DeducteeType = DeducteeType.Individual;

        var v = PostCarved(book, expense, party, Gross, "legacy");
        Assert.Equal(3, v.Lines.Count);
        Assert.Equal(100, PostedRateBp(v));
        Assert.Equal(new Money(500.00m), AmountOn(v, TdsPayable(book.Company).Id));
        Assert.Equal(new Money(49_500.30m), AmountOn(v, party.Id));

        party.DeducteeType = afterPosting;
        return (book, expense, party, v);
    }

    // ================================================================ the branch reaches the screen

    /// <summary>
    /// 🔴 A FRESH §194C bill to a <b>company</b> is posted at the §194C(1)(ii) 2% arm: ₹1,000.00 withheld and
    /// ₹49,000.30 credited to the party. Before the branch the screen posted ₹500.00 and ₹49,500.30.
    /// </summary>
    [Fact]
    public void A_fresh_194C_bill_to_a_company_is_posted_at_two_percent()
    {
        using var book = AlterationBook.New("194c-fresh-company");
        var (expense, party) = book.EnableTds(Section, expenseName: "Contract Work", partyName: "Bharat Builders");
        party.DeducteeType = DeducteeType.Company;

        var v = PostCarved(book, expense, party, Gross);

        Assert.Equal(200, PostedRateBp(v));
        Assert.Equal(new Money(1_000.00m), AmountOn(v, TdsPayable(book.Company).Id));
        Assert.Equal(new Money(49_000.30m), AmountOn(v, party.Id));
    }

    /// <summary>A fresh §194C bill to an <b>individual</b> is posted at the 1% arm — ₹500.00 — unchanged.</summary>
    [Fact]
    public void A_fresh_194C_bill_to_an_individual_is_posted_at_one_percent()
    {
        using var book = AlterationBook.New("194c-fresh-individual");
        var (expense, party) = book.EnableTds(Section, expenseName: "Contract Work", partyName: "R Kumar");
        party.DeducteeType = DeducteeType.Individual;

        var v = PostCarved(book, expense, party, Gross);

        Assert.Equal(100, PostedRateBp(v));
        Assert.Equal(new Money(500.00m), AmountOn(v, TdsPayable(book.Company).Id));
    }

    // ================================================================ grandfathering: history stays ALTERABLE

    /// <summary>
    /// 🔴 <b>THE RULING, AND THE WHOLE POINT OF IT.</b> A §194C voucher carrying the pre-bifurcation 100 bp against
    /// a company deductee <b>opens, accepts and keeps its posted figures</b>. Without the grandfathering the
    /// re-carve would resolve 200 bp, the rate pin would disagree, and this alteration would be refused with
    /// "…now resolves to 2%" — every §194C voucher in every existing book, unalterable.
    /// </summary>
    [Fact]
    public void A_pre_bifurcation_194C_voucher_can_still_be_altered()
    {
        var (book, _, party, v) = LegacyOnePercentVoucher("194c-grandfather-alter");
        using var _book = book;

        var entry = OpenOrThrow(book, v.Id);
        entry.Narration = "legacy, re-narrated";
        Assert.True(entry.AcceptAlteration(), "Expected the alteration to be accepted; refused with: " + entry.Message);

        var after = book.Company.FindVoucher(v.Id)!;
        Assert.Equal("legacy, re-narrated", after.Narration);
        Assert.Equal(100, PostedRateBp(after));
        Assert.Equal(new Money(500.00m), AmountOn(after, TdsPayable(book.Company).Id));
        Assert.Equal(new Money(49_500.30m), AmountOn(after, party.Id));
    }

    /// <summary>
    /// 🔴 <b>And it can be AMENDED, not merely re-saved.</b> The gross moves ₹50,000.30 → ₹80,000.70 and the carve
    /// is re-derived <b>at the grandfathered 1%</b>: ₹800.00 withheld (80,000.70 × 1% = 800.007, nearest rupee) and
    /// ₹79,200.70 credited. At the un-grandfathered 2% it would have been ₹1,600.00 — and would have been refused
    /// before ever reaching the arithmetic.
    /// </summary>
    [Fact]
    public void A_grandfathered_194C_voucher_re_carves_from_an_amended_gross_at_its_posted_rate()
    {
        var (book, _, party, v) = LegacyOnePercentVoucher("194c-grandfather-amend");
        using var _book = book;

        var entry = OpenOrThrow(book, v.Id);
        foreach (var line in entry.Lines.Where(l => l.SelectedLedger is not null))
            line.AmountText = "80000.70";
        Assert.True(entry.AcceptAlteration(), "Expected the alteration to be accepted; refused with: " + entry.Message);

        var after = book.Company.FindVoucher(v.Id)!;
        Assert.Equal(100, PostedRateBp(after));
        Assert.Equal(new Money(800.00m), AmountOn(after, TdsPayable(book.Company).Id));
        Assert.Equal(new Money(79_200.70m), AmountOn(after, party.Id));
        Assert.Equal(new Money(80_000.70m),
            AmountOn(after, party.Id) + AmountOn(after, TdsPayable(book.Company).Id));
    }

    /// <summary>
    /// <b>The advisory panel must state what Accept will post</b> — 1%, not the 2% today's masters would resolve
    /// for this company. A panel reading "@ 2%: ₹1,000.00" over an Accept that posts ₹500.00 is the exact defect
    /// <c>VoucherAlterDerivedLegDriftTests.The_withholding_panel_on_an_altering_screen_states_what_accept_will_post</c>
    /// exists to catch, and the grandfathering had to reach the panel and the bill-wise preview as well as the
    /// accept path.
    /// </summary>
    [Fact]
    public void The_panel_on_a_grandfathered_alteration_states_the_posted_rate_not_todays()
    {
        var (book, _, _, v) = LegacyOnePercentVoucher("194c-grandfather-panel");
        using var _book = book;

        var entry = OpenOrThrow(book, v.Id);

        Assert.True(entry.ShowTdsPanel);
        Assert.Equal("194C", entry.TdsSectionText);
        Assert.Equal("1%", entry.TdsRateText);
        Assert.Equal("500.00", entry.TdsAmountText);
        Assert.Equal("49,500.30", entry.TdsNetPayableText);
    }

    /// <summary>
    /// The grandfathered alteration survives the round trip: re-open the altered voucher and it still carries
    /// 100 bp, so the concession is not a one-shot that decays on the next edit.
    /// </summary>
    [Fact]
    public void A_grandfathered_voucher_stays_grandfathered_across_repeated_alterations()
    {
        var (book, _, party, v) = LegacyOnePercentVoucher("194c-grandfather-twice");
        using var _book = book;

        for (var pass = 1; pass <= 3; pass++)
        {
            var entry = OpenOrThrow(book, v.Id);
            entry.Narration = "pass " + pass;
            Assert.True(entry.AcceptAlteration(), $"Pass {pass} refused with: {entry.Message}");
        }

        var after = book.Company.FindVoucher(v.Id)!;
        Assert.Equal(100, PostedRateBp(after));
        Assert.Equal(new Money(500.00m), AmountOn(after, TdsPayable(book.Company).Id));
        Assert.Equal(new Money(49_500.30m), AmountOn(after, party.Id));
    }

    // ================================================================ what grandfathering must NOT absorb

    /// <summary>
    /// 🔴 <b>The drift guard is not weakened in the other direction.</b> A voucher posted at the 2% arm against a
    /// company whose party is RE-TYPED to an individual afterwards now resolves 1% — that is drift, not history,
    /// and the rate pin still refuses it by name. Grandfathering is one-directional by construction.
    /// </summary>
    [Fact]
    public void A_194C_party_re_typed_down_to_an_individual_after_posting_is_still_refused()
    {
        using var book = AlterationBook.New("194c-retype-down");
        var (expense, party) = book.EnableTds(Section, expenseName: "Contract Work", partyName: "Bharat Builders");
        party.DeducteeType = DeducteeType.Company;

        var v = PostCarved(book, expense, party, Gross);
        Assert.Equal(200, PostedRateBp(v));

        // 🔴 The snapshot is taken AFTER the master moves, so the comparison isolates the VOUCHER. Taking it
        // before would have "proved" a refusal by detecting the test's own edit to the ledger master.
        party.DeducteeType = DeducteeType.Individual;
        var before = book.Export();
        var beforeDisk = book.ExportReloaded();

        var entry = OpenOrThrow(book, v.Id);
        entry.Narration = "re-typed";
        Assert.False(entry.AcceptAlteration(), "Expected the re-typed party to be refused; it was accepted.");
        Assert.Contains("withheld 194C TDS at 2%", entry.Message);
        Assert.Contains("now resolves to 1%", entry.Message);

        Assert.Equal(before, book.Export());
        Assert.Equal(beforeDisk, book.ExportReloaded());
    }

    /// <summary>
    /// <b>The §206AA arm is never grandfathered.</b> A §194C voucher posted WITHOUT a PAN carries 2000 bp; adding
    /// the PAN afterwards resolves a with-PAN rate and is still refused, exactly as the §194J(b) case
    /// <c>VoucherAlterReDeriveTests.A_deductee_PAN_added_after_posting_is_refused_rather_than_re_carved_at_the_new_rate</c>
    /// pins. Adding the deductee-type branch must not open a door through the PAN guard.
    /// </summary>
    [Fact]
    public void A_pan_added_after_posting_is_still_refused_on_194C()
    {
        using var book = AlterationBook.New("194c-pan-added");
        var (expense, party) = book.EnableTds(Section, pan: null, expenseName: "Contract Work", partyName: "No PAN Co");
        party.DeducteeType = DeducteeType.Individual;

        var v = PostCarved(book, expense, party, Gross);
        Assert.Equal(2000, PostedRateBp(v));

        party.PartyPan = "AAPFU0939F";
        var before = book.Export();   // after the master moves — see the note in the re-typed case above

        var entry = OpenOrThrow(book, v.Id);
        entry.Narration = "pan added";
        Assert.False(entry.AcceptAlteration(), "Expected the added PAN to be refused; it was accepted.");
        Assert.Contains("206AA", entry.Message);
        Assert.Equal(before, book.Export());
    }
}
