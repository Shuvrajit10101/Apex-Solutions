using System;
using System.Collections.Generic;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Apex.Desktop.ViewModels;
using Xunit;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.Tests;

/// <summary>
/// <b>Phase 10.11 S5c — the DEFER rows lifted: the TDS RE-CARVE and the reverse-charge RE-STAMP.</b>
///
/// <para>🔴 <b>The one property this file exists to pin.</b> On a TDS-carved voucher the operator keys a GROSS and
/// the posted lines carry the DERIVED net party credit plus a separate TDS-Payable leg. So rehydration must INVERT
/// the carve to recover the gross, and Accept must RE-CARVE FROM THAT RESTORED GROSS. Re-applying the stored carve
/// to a new base instead drifts the party credit by <b>exactly the carve</b> — a silent wrong figure. A test that
/// asserts only "the carve is still there" passes for that drift, so every assertion below is on the LITERAL
/// FIGURE.</para>
///
/// <para>🔴 <b>And the detection rule, tested in BOTH directions.</b> <c>AcceptAlteration</c> runs no detection of
/// its own: the POSTED voucher decides WHETHER a leg is derived, the amended content decides HOW MUCH. So a
/// voucher posted with no carve cannot ACQUIRE one when a party master gains a <see cref="DeducteeType"/> after
/// posting, and a voucher posted with one cannot silently LOSE it when the flag is turned off — it is refused by
/// name instead.</para>
///
/// <para><b>Odd paise everywhere</b> (house rule): a ±₹0.50 defect once survived six round-number assertions.</para>
/// </summary>
public sealed class VoucherAlterReDeriveTests
{
    // §194J(b) as seeded: 10% with PAN (1000bp), 20% no-PAN, cumulative-FY threshold ₹50,000, no single-transaction
    // threshold. Every literal figure below is derived from exactly those.
    private const string Section = "194J(b)";
    private const string GstinMaharashtra = "27AAPFU0939F1ZV";
    private const string GstinGujarat = "24AAACC1206D1ZM";

    // ================================================================ helpers

    private static Voucher PostCarved(
        AlterationBook book, VoucherBaseType baseType, DomainLedger expense, DomainLedger party, string gross,
        string? narration = null) =>
        book.Post(baseType, book.On(),
            new[] { (expense, DrCr.Debit, gross), (party, DrCr.Credit, gross) }, narration);

    private static Money AmountOn(Voucher v, Guid ledgerId) =>
        v.Lines.Where(l => l.LedgerId == ledgerId).Aggregate(Money.Zero, (a, l) => a + l.Amount);

    private static DomainLedger TdsPayable(Company c) =>
        c.Ledgers.First(l => l.TdsTcsClassification == TdsTcsLedgerKind.Tds);

    private static TdsLineTax SingleTdsDetail(Voucher v) =>
        v.Lines.Single(l => l.HasTds).Tds!;

    private static VoucherEntryViewModel OpenOrThrow(AlterationBook book, Guid voucherId)
    {
        var open = book.ForAlter(voucherId);
        Assert.False(open.IsRefused, "Expected the alteration screen to open; refused with: " + open.Refusal);
        return open.Entry!;
    }

    private static string RefuseOrThrow(AlterationBook book, Guid voucherId, Action<VoucherEntryViewModel> edit)
    {
        var entry = OpenOrThrow(book, voucherId);
        edit(entry);
        Assert.False(entry.AcceptAlteration(), "Expected the alteration to be refused; it was accepted.");
        Assert.False(string.IsNullOrWhiteSpace(entry.Message));
        return entry.Message!;
    }

    /// <summary>A Regular-GST Maharashtra book with the notified reverse-charge categories + dated rates seeded.</summary>
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

    // ================================================================ (A) the carve INVERTS — the gross comes back

    /// <summary>
    /// 🔴 <b>Row 11 lifted, and the inversion measured on the grid.</b> ₹1,20,000.30 gross under §194J(b) at 10%
    /// withholds ₹12,000 (nearest rupee, round-half-up) and credits the party ₹1,08,000.30. What the operator KEYED
    /// was the gross, so that is what re-opens — and the TDS-Payable leg the engine appended must NOT appear as a
    /// grid row, or accepting would post it twice.
    /// </summary>
    [Fact]
    public void A_TDS_carved_Journal_re_opens_with_the_GROSS_restored_and_no_payable_row()
    {
        using var book = AlterationBook.New("tds-invert");
        var (expense, party) = book.EnableTds(Section);

        var posted = PostCarved(book, VoucherBaseType.Journal, expense, party, "120000.30");

        // The POSTED shape: Dr expense GROSS, Cr party NET, Cr TDS Payable = the withholding.
        Assert.Equal(3, posted.Lines.Count);
        Assert.Equal(new Money(120000.30m), AmountOn(posted, expense.Id));
        Assert.Equal(new Money(108000.30m), AmountOn(posted, party.Id));
        Assert.Equal(new Money(12000.00m), AmountOn(posted, TdsPayable(book.Company).Id));

        var entry = OpenOrThrow(book, posted.Id);

        // The KEYED shape: two rows, the party carrying the gross the operator typed.
        var rows = entry.Lines.Where(l => l.IsComplete).ToList();
        Assert.Equal(2, rows.Count);
        Assert.Equal(120000.30m, rows.Single(r => r.SelectedLedger!.Id == expense.Id).ParsedAmount);
        Assert.Equal(120000.30m, rows.Single(r => r.SelectedLedger!.Id == party.Id).ParsedAmount);
        Assert.DoesNotContain(rows, r => r.SelectedLedger!.Id == TdsPayable(book.Company).Id);

        // And the panel opens on the section that was POSTED, not on whatever the expense ledger defaults to today.
        Assert.True(entry.ShowTdsPanel);
        Assert.Equal(Section, entry.SelectedTdsNature!.SectionCode);
    }

    // ================================================================ (B) 🔴 THE DRIFT TEST

    /// <summary>
    /// 🔴 <b>THE TEST THIS SLICE IS FOR.</b> Alter ONE unrelated field — the narration — and the carve must come
    /// back <b>bit for bit</b>. The failure mode is a drift of exactly the carve (re-applying the stored ₹12,000 to
    /// the restored ₹1,20,000.30 would credit the party ₹96,000.30 instead of ₹1,08,000.30), and a presence check
    /// cannot see it. Every figure below is a literal.
    /// </summary>
    [Fact]
    public void Altering_only_the_narration_leaves_the_carve_bit_for_bit_unchanged()
    {
        using var book = AlterationBook.New("tds-drift");
        var (expense, party) = book.EnableTds(Section);

        var posted = PostCarved(book, VoucherBaseType.Journal, expense, party, "120000.30", "as posted");
        var beforeDetail = SingleTdsDetail(posted);

        var entry = OpenOrThrow(book, posted.Id);
        entry.Narration = "corrected narration only";
        Assert.True(entry.AcceptAlteration(), entry.Message);

        var after = book.Company.FindVoucher(posted.Id)!;
        Assert.Equal("corrected narration only", after.Narration);

        // ---- the carve, to the paisa and to the basis point ----
        Assert.Equal(3, after.Lines.Count);
        Assert.Equal(new Money(120000.30m), AmountOn(after, expense.Id));
        Assert.Equal(new Money(108000.30m), AmountOn(after, party.Id));   // NOT 96,000.30 (the re-applied carve)
        Assert.Equal(new Money(12000.00m), AmountOn(after, TdsPayable(book.Company).Id));

        var afterDetail = SingleTdsDetail(after);
        Assert.Equal(beforeDetail.NatureId, afterDetail.NatureId);
        Assert.Equal(Section, afterDetail.SectionCode);
        Assert.Equal(new Money(120000.30m), afterDetail.AssessableValue);
        Assert.Equal(1000, afterDetail.RateBasisPoints);
        Assert.Equal(new Money(12000.00m), afterDetail.TdsAmount);
        Assert.Equal(party.Id, afterDetail.DeducteeLedgerId);
        Assert.True(afterDetail.PanApplied);
    }

    /// <summary>The same round trip proved on the canonical export, in memory and on disk (design §8.3).</summary>
    [Fact]
    public void A_TDS_carved_Payment_round_trips_byte_identically()
    {
        using var book = AlterationBook.New("tds-rt-payment");
        var (expense, party) = book.EnableTds(Section);

        var posted = PostCarved(book, VoucherBaseType.Payment, expense, party, "120000.30");
        var before = book.Export();
        var beforeDisk = book.ExportReloaded();

        var entry = OpenOrThrow(book, posted.Id);
        Assert.True(entry.AcceptAlteration(), entry.Message);

        Assert.Equal(before, book.Export());
        Assert.Equal(beforeDisk, book.ExportReloaded());
    }

    /// <summary>Row 21's withholding arm — the same round trip on a Purchase.</summary>
    [Fact]
    public void A_TDS_carved_Purchase_round_trips_byte_identically()
    {
        using var book = AlterationBook.New("tds-rt-purchase");
        var (expense, party) = book.EnableTds(Section);

        var posted = PostCarved(book, VoucherBaseType.Purchase, expense, party, "120000.30");
        var before = book.Export();
        var beforeDisk = book.ExportReloaded();

        var entry = OpenOrThrow(book, posted.Id);
        Assert.True(entry.AcceptAlteration(), entry.Message);

        Assert.Equal(before, book.Export());
        Assert.Equal(beforeDisk, book.ExportReloaded());
    }

    // ================================================================ (C) the carve MOVES with the amended gross

    /// <summary>
    /// 🔴 <b>Re-carved from the RESTORED GROSS, not re-applied to a new base.</b> The gross is amended from
    /// ₹1,20,000.30 to ₹1,50,000.55, so §194J(b) at 10% now withholds ₹15,000 (round-half-up of ₹15,000.055) and the
    /// party is credited ₹1,35,000.55. Re-applying the STORED ₹12,000 would credit ₹1,38,000.55 instead — the exact
    /// drift this slice exists to prevent, so it is asserted against by name.
    /// </summary>
    [Fact]
    public void Amending_the_gross_re_carves_from_it_rather_than_re_applying_the_stored_carve()
    {
        using var book = AlterationBook.New("tds-recarve");
        var (expense, party) = book.EnableTds(Section);

        var posted = PostCarved(book, VoucherBaseType.Journal, expense, party, "120000.30");

        var entry = OpenOrThrow(book, posted.Id);
        foreach (var row in entry.Lines.Where(l => l.IsComplete)) row.AmountText = "150000.55";
        entry.Recalculate();
        Assert.True(entry.AcceptAlteration(), entry.Message);

        var after = book.Company.FindVoucher(posted.Id)!;
        Assert.Equal(new Money(150000.55m), AmountOn(after, expense.Id));
        Assert.Equal(new Money(135000.55m), AmountOn(after, party.Id));
        Assert.NotEqual(new Money(138000.55m), AmountOn(after, party.Id)); // the stored-carve drift
        Assert.Equal(new Money(15000.00m), AmountOn(after, TdsPayable(book.Company).Id));
        Assert.Equal(new Money(150000.55m), SingleTdsDetail(after).AssessableValue);
    }

    // ================================================================ (D) 🔴 the voucher's OWN cumulative

    /// <summary>
    /// 🔴 <b>A voucher must not be counted against its own cumulative-FY threshold — measured, and it is a defect
    /// §6.6a never names.</b> §194J(b) has a ₹50,000 cumulative threshold and no single-transaction threshold, so a
    /// ₹30,000.30 fee is correctly BELOW threshold at posting: the party is credited the full gross and the
    /// assessment (TDS 0) rides that line. At re-accept the voucher IS in <c>Company.Vouchers</c> carrying that
    /// assessment, so an unguarded <c>ProjectPriorCumulative</c> reads ₹30,000.30 back as "prior", adds the amended
    /// ₹30,000.30 current, crosses ₹50,000 and ACQUIRES a ₹3,000 withholding on a narration-only alteration.
    /// </summary>
    [Fact]
    public void A_below_threshold_withholding_does_not_acquire_one_from_its_own_cumulative()
    {
        using var book = AlterationBook.New("tds-selfcum");
        var (expense, party) = book.EnableTds(Section);

        var posted = PostCarved(book, VoucherBaseType.Journal, expense, party, "30000.30");

        // Posted BELOW threshold: two lines, the party credited the full gross, the detail riding it with TDS 0.
        Assert.Equal(2, posted.Lines.Count);
        Assert.Equal(new Money(30000.30m), AmountOn(posted, party.Id));
        Assert.Equal(Money.Zero, SingleTdsDetail(posted).TdsAmount);

        var entry = OpenOrThrow(book, posted.Id);
        entry.Narration = "narration only";
        Assert.True(entry.AcceptAlteration(), entry.Message);

        var after = book.Company.FindVoucher(posted.Id)!;
        Assert.Equal(2, after.Lines.Count);                              // NOT 3 — no TDS-Payable leg appeared
        Assert.Equal(new Money(30000.30m), AmountOn(after, party.Id));   // NOT 27,000.30
        Assert.Equal(Money.Zero, SingleTdsDetail(after).TdsAmount);
        Assert.DoesNotContain(after.Lines, l => l.LedgerId == TdsPayable(book.Company).Id);
    }

    /// <summary>
    /// The other half of the same guard: a SECOND voucher in the same FY still sees the first one as prior, so the
    /// exclusion is scoped to the voucher being altered and does not disable the cumulative threshold.
    /// </summary>
    [Fact]
    public void A_second_voucher_still_counts_the_first_towards_the_cumulative_threshold()
    {
        using var book = AlterationBook.New("tds-cum-2");
        var (expense, party) = book.EnableTds(Section);

        PostCarved(book, VoucherBaseType.Journal, expense, party, "30000.30");
        var second = PostCarved(book, VoucherBaseType.Journal, expense, party, "30000.30");

        // 30,000.30 prior + 30,000.30 current = 60,000.60 > 50,000 ⇒ the SECOND voucher withholds.
        Assert.Equal(3, second.Lines.Count);
        Assert.Equal(new Money(3000.00m), AmountOn(second, TdsPayable(book.Company).Id));
    }

    // ================================================================ (E) the DETECTION rule, both directions

    /// <summary>
    /// 🔴 <b>Direction 1 — a master turned ON after posting must not ADD a carve.</b> The voucher was posted before
    /// the party was a deductee, so it carries none; a narration-only alteration must leave it exactly as it is.
    /// This is the direction that must stay SILENT, because nothing changed: no detection is consulted for a family
    /// the posted voucher does not carry.
    /// </summary>
    [Fact]
    public void A_plain_voucher_does_not_acquire_a_carve_when_the_masters_turn_TDS_on_after_posting()
    {
        using var book = AlterationBook.New("tds-drift-on");
        new TdsTcsService(book.Company).EnableTds(new TdsConfig { Tan = "MUMA12345B" });
        var expense = book.Ledger("Professional Fees", "Indirect Expenses");
        var party = book.Ledger("Acme Consultants", "Sundry Creditors");

        var posted = PostCarved(book, VoucherBaseType.Journal, expense, party, "120000.30");
        Assert.Equal(2, posted.Lines.Count);
        Assert.DoesNotContain(posted.Lines, l => l.HasTds);

        // The masters move AFTER posting — exactly the shape that would make Accept acquire a carve. The export
        // baseline is taken AFTER the move, because the master change is itself in the canonical export: what is
        // being pinned is that the VOUCHER did not follow it.
        expense.TdsApplicable = true;
        expense.TdsNatureOfPaymentId = book.Company.FindNatureOfPaymentByCode(Section)!.Id;
        party.DeducteeType = DeducteeType.Firm;
        party.PartyPan = "AAPFU0939F";
        var before = book.Export();

        var entry = OpenOrThrow(book, posted.Id);
        Assert.True(entry.ShowTdsPanel);                    // the PANEL sees the drift — the posting must not
        Assert.True(entry.AcceptAlteration(), entry.Message);

        var after = book.Company.FindVoucher(posted.Id)!;
        Assert.Equal(2, after.Lines.Count);
        Assert.DoesNotContain(after.Lines, l => l.HasTds);
        Assert.Equal(new Money(120000.30m), AmountOn(after, party.Id));
        Assert.Equal(before, book.Export());
    }

    /// <summary>
    /// 🔴 <b>Direction 2 — a master turned OFF after posting must not silently REMOVE a carve.</b> With the
    /// expense ledger's Is-TDS-Applicable flag cleared the screen no longer detects a withholding, so re-accepting
    /// would credit the party the full gross and drop the TDS-Payable leg — a deduction that has already been
    /// reported, vanishing without a word. It is refused by name instead.
    /// </summary>
    [Fact]
    public void A_carved_voucher_is_refused_by_name_when_the_masters_turn_TDS_off_after_posting()
    {
        using var book = AlterationBook.New("tds-drift-off");
        var (expense, party) = book.EnableTds(Section);

        var posted = PostCarved(book, VoucherBaseType.Journal, expense, party, "120000.30");

        expense.TdsApplicable = false; // the master moved after the voucher was posted
        var before = book.Export();    // baseline AFTER the master move: the voucher is what must not change

        var message = RefuseOrThrow(book, posted.Id, e => e.Narration = "harmless");
        Assert.Contains(Section, message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no longer finds one", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("full gross", message, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(before, book.Export()); // and nothing was written
    }

    /// <summary>
    /// The PAN half of the same guard. §206AA: a deductee with no valid PAN is withheld at 20%, with one at 10%.
    /// Adding a PAN after posting would re-carve an already-reported deduction at half the rate, so the rate pin
    /// refuses it by name.
    /// </summary>
    [Fact]
    public void A_deductee_PAN_added_after_posting_is_refused_rather_than_re_carved_at_the_new_rate()
    {
        using var book = AlterationBook.New("tds-drift-pan");
        var (expense, party) = book.EnableTds(Section, pan: null);

        var posted = PostCarved(book, VoucherBaseType.Journal, expense, party, "120000.30");
        Assert.Equal(2000, SingleTdsDetail(posted).RateBasisPoints);          // §206AA no-PAN 20%
        Assert.Equal(new Money(24000.00m), AmountOn(posted, TdsPayable(book.Company).Id));

        party.PartyPan = "AAPFU0939F"; // the master moved after the voucher was posted

        var message = RefuseOrThrow(book, posted.Id, e => e.Narration = "harmless");
        Assert.Contains("20%", message);
        Assert.Contains("10%", message);
        Assert.Contains("206AA", message);
    }

    /// <summary>The section pin: re-sectioning a posted deduction belongs to a different challan and a different
    /// return line, so it is refused rather than silently re-computed.</summary>
    [Fact]
    public void Re_sectioning_a_posted_withholding_on_the_panel_is_refused_by_name()
    {
        using var book = AlterationBook.New("tds-resection");
        var (expense, party) = book.EnableTds(Section);

        var posted = PostCarved(book, VoucherBaseType.Journal, expense, party, "120000.30");

        var message = RefuseOrThrow(book, posted.Id, e =>
            e.SelectedTdsNature = e.TdsNatureOptions.First(n => n.SectionCode == "194J(a)"));
        Assert.Contains("194J(b)", message);
        Assert.Contains("194J(a)", message);
        Assert.Contains("never its section", message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Declining the withholding on an altering screen would drop an already-reported deduction, so the
    /// "Not Applicable" sentinel is refused here rather than obeyed.</summary>
    [Fact]
    public void Declining_the_withholding_on_an_altering_screen_is_refused_by_name()
    {
        using var book = AlterationBook.New("tds-decline");
        var (expense, party) = book.EnableTds(Section);

        var posted = PostCarved(book, VoucherBaseType.Journal, expense, party, "120000.30");

        var message = RefuseOrThrow(book, posted.Id, e => e.SelectedTdsNature = VoucherEntryViewModel.TdsNotApplicable);
        Assert.Contains("no longer finds one", message, StringComparison.OrdinalIgnoreCase);
    }

    // ================================================================ (F) the reverse-charge RE-STAMP

    /// <summary>
    /// 🔴 <b>Rows 12 and 21's reverse-charge arm lifted.</b> An inter-state legal-services purchase of ₹10,000.50
    /// self-accounts IGST @18% = ₹1,800.09 as <c>Cr RCM Output IGST</c> + <c>Dr Input IGST</c>. Those two legs are
    /// the ENGINE's, not the operator's, so they must not re-open as grid rows — and the stamped taxable value
    /// GSTR-3B reads must come back identical.
    /// </summary>
    [Fact]
    public void A_reverse_charge_Purchase_re_opens_without_the_engine_pair_and_round_trips_byte_identically()
    {
        using var book = AlterationBook.New("rcm-rt");
        EnableAdvancedGst(book);
        var fees = RcmExpense(book, "Legal Fees", "Legal", 1800);
        var advocate = RcmSupplier(book, "Advocate (Gujarat)", GstinGujarat, "24");

        var posted = book.Post(VoucherBaseType.Purchase, book.On(),
            new[] { (fees, DrCr.Debit, "10000.50"), (advocate, DrCr.Credit, "10000.50") });

        Assert.Equal(4, posted.Lines.Count);
        Assert.Equal(2, posted.Lines.Count(l => l.Gst is { IsReverseCharge: true }));
        Assert.All(posted.Lines.Where(l => l.HasGst),
            l => Assert.Equal(new Money(10000.50m), l.Gst!.TaxableValue));

        var before = book.Export();
        var beforeDisk = book.ExportReloaded();

        var entry = OpenOrThrow(book, posted.Id);
        Assert.Equal(2, entry.Lines.Count(l => l.IsComplete));  // the engine pair is NOT a grid row
        Assert.True(entry.AcceptAlteration(), entry.Message);

        Assert.Equal(before, book.Export());
        Assert.Equal(beforeDisk, book.ExportReloaded());
    }

    /// <summary>
    /// 🔴 <b>RECOMPUTED, never echoed.</b> Amend the expense from ₹10,000.50 to ₹20,000.50 and the self-accounted
    /// IGST must move to ₹3,600.09 with the STAMPED taxable value following it — GSTR-1 and GSTR-3B read the stamp,
    /// not the posted amounts, so an echoed ₹10,000.50 taxable would make a return declare a figure the book no
    /// longer holds.
    /// </summary>
    [Fact]
    public void Amending_a_reverse_charge_expense_re_stamps_the_taxable_value_and_the_tax()
    {
        using var book = AlterationBook.New("rcm-restamp");
        EnableAdvancedGst(book);
        var fees = RcmExpense(book, "Legal Fees", "Legal", 1800);
        var advocate = RcmSupplier(book, "Advocate (Gujarat)", GstinGujarat, "24");

        var posted = book.Post(VoucherBaseType.Purchase, book.On(),
            new[] { (fees, DrCr.Debit, "10000.50"), (advocate, DrCr.Credit, "10000.50") });
        Assert.All(posted.Lines.Where(l => l.HasGst), l => Assert.Equal(new Money(1800.09m), l.Amount));

        var entry = OpenOrThrow(book, posted.Id);
        foreach (var row in entry.Lines.Where(l => l.IsComplete)) row.AmountText = "20000.50";
        entry.Recalculate();
        Assert.True(entry.AcceptAlteration(), entry.Message);

        var after = book.Company.FindVoucher(posted.Id)!;
        var stamped = after.Lines.Where(l => l.Gst is { IsReverseCharge: true }).ToList();
        Assert.Equal(2, stamped.Count);
        Assert.All(stamped, l => Assert.Equal(new Money(20000.50m), l.Gst!.TaxableValue));
        Assert.All(stamped, l => Assert.Equal(new Money(3600.09m), l.Amount));
        Assert.DoesNotContain(after.Lines, l => l.Gst is { } g && g.TaxableValue == new Money(10000.50m));
    }

    /// <summary>
    /// The reverse-charge drift guard: clearing the expense ledger's reverse-charge flag after posting would drop
    /// the §49(4) liability AND its matching input credit on re-accept. Refused by name.
    /// </summary>
    [Fact]
    public void A_reverse_charge_voucher_is_refused_by_name_when_the_flag_is_cleared_after_posting()
    {
        using var book = AlterationBook.New("rcm-drift-off");
        EnableAdvancedGst(book);
        var fees = RcmExpense(book, "Legal Fees", "Legal", 1800);
        var advocate = RcmSupplier(book, "Advocate (Gujarat)", GstinGujarat, "24");

        var posted = book.Post(VoucherBaseType.Purchase, book.On(),
            new[] { (fees, DrCr.Debit, "10000.50"), (advocate, DrCr.Credit, "10000.50") });

        fees.SalesPurchaseGst!.ReverseChargeApplicable = false;
        var before = book.Export();    // baseline AFTER the master move: the voucher is what must not change

        var message = RefuseOrThrow(book, posted.Id, e => e.Narration = "harmless");
        Assert.Contains("reverse charge", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("49(4)", message);
        Assert.Equal(before, book.Export());
    }

    /// <summary>
    /// The intra/inter split is part of the SHAPE, not of the amounts: an inter-state supplier repointed to the
    /// home state after posting would re-stamp IGST as CGST+SGST — three heads where there was one — so it is
    /// refused rather than restated.
    /// </summary>
    [Fact]
    public void A_supplier_state_moved_after_posting_is_refused_rather_than_re_split()
    {
        using var book = AlterationBook.New("rcm-drift-state");
        EnableAdvancedGst(book);
        var fees = RcmExpense(book, "Legal Fees", "Legal", 1800);
        var advocate = RcmSupplier(book, "Advocate (Gujarat)", GstinGujarat, "24");

        var posted = book.Post(VoucherBaseType.Purchase, book.On(),
            new[] { (fees, DrCr.Debit, "10000.50"), (advocate, DrCr.Credit, "10000.50") });
        Assert.All(posted.Lines.Where(l => l.HasGst),
            l => Assert.Equal(GstTaxHead.Integrated, l.Gst!.TaxHead));

        advocate.PartyGst!.StateCode = "27";
        advocate.PartyGst!.Gstin = GstinMaharashtra;

        var message = RefuseOrThrow(book, posted.Id, e => e.Narration = "harmless");
        Assert.Contains("no longer re-computes to the same shape", message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Declining reverse charge on an altering screen would withdraw an already-reported §49(4)
    /// liability, so the sentinel is refused rather than obeyed.</summary>
    [Fact]
    public void Declining_reverse_charge_on_an_altering_screen_is_refused_by_name()
    {
        using var book = AlterationBook.New("rcm-decline");
        EnableAdvancedGst(book);
        var fees = RcmExpense(book, "Legal Fees", "Legal", 1800);
        var advocate = RcmSupplier(book, "Advocate (Gujarat)", GstinGujarat, "24");

        var posted = book.Post(VoucherBaseType.Purchase, book.On(),
            new[] { (fees, DrCr.Debit, "10000.50"), (advocate, DrCr.Credit, "10000.50") });

        var message = RefuseOrThrow(book, posted.Id, e => e.SelectedRcmSupplyKind = e.RcmNotApplicable);
        Assert.Contains("Not Applicable", message);
        Assert.Contains("49(4)", message);
    }

    // ================================================================ (G) the guards a round trip cannot reach

    /// <summary>Posts a hand-built voucher straight through the engine — the door the canonical importer uses, and
    /// the only way to produce the import-shaped withholdings the entry screen cannot key.</summary>
    private static Voucher PostRaw(AlterationBook book, VoucherBaseType baseType, params EntryLine[] lines)
    {
        var voucher = new Voucher(Guid.NewGuid(), book.Type(baseType).Id, book.On(), lines);
        var posted = new LedgerService(book.Company).Post(voucher);
        book.Storage.Save(book.Company);
        return posted;
    }

    /// <summary>
    /// 🔴 <b>The inversion's CONTRACT, asserted directly rather than through a round trip.</b>
    /// <c>Invert</c>'s <c>KeyedLines</c> is what a caller rebuilds a replacement from, so it must carry NO engine
    /// stamp at all — including on the below-threshold shape, where the assessment rides the deductee's own
    /// (uncarved) line and the whole inversion is the removal of the tag. The round-trip tests cannot see this:
    /// <c>VoucherLineViewModel.RehydrateFrom</c> never reads <c>EntryLine.Tds</c>, so a stamp left on a keyed line
    /// is invisible to the grid and is re-derived over on accept. It would only bite a second caller — which is
    /// exactly the shape of defect this repository keeps finding.
    /// </summary>
    [Fact]
    public void The_inverted_keyed_lines_carry_no_engine_stamp_in_either_withholding_shape()
    {
        using var book = AlterationBook.New("invert-contract");
        var (expense, party) = book.EnableTds(Section);

        // Below threshold — the detail rides the party's own full-gross credit.
        var below = PostCarved(book, VoucherBaseType.Journal, expense, party, "30000.30");
        Assert.Contains(below.Lines, l => l.HasTds);
        Assert.Null(VoucherAlterationDerivedLegs.Invert(book.Company, below, out var belowInversion));
        Assert.DoesNotContain(belowInversion!.KeyedLines, l => l.HasTds);
        Assert.Equal(2, belowInversion.KeyedLines.Count);
        Assert.Equal(new Money(30000.30m), belowInversion.Tds!.RestoredGross);
        Assert.Equal(Money.Zero, belowInversion.Tds.PostedTdsAmount);

        // Withheld — the payable leg goes and the party's gross comes back.
        var withheld = PostCarved(book, VoucherBaseType.Payment, expense, party, "120000.30");
        Assert.Null(VoucherAlterationDerivedLegs.Invert(book.Company, withheld, out var withheldInversion));
        Assert.DoesNotContain(withheldInversion!.KeyedLines, l => l.HasTds);
        Assert.Equal(2, withheldInversion.KeyedLines.Count);
        Assert.Equal(new Money(120000.30m), withheldInversion.Tds!.RestoredGross);
        Assert.Equal(new Money(12000.00m), withheldInversion.Tds.PostedTdsAmount);
    }

    /// <summary>
    /// An imported voucher whose withholding names a deductee that is not in this company: the gross cannot be
    /// restored, because the leg to add the withholding back onto cannot be identified. Refused at the door.
    /// </summary>
    [Fact]
    public void A_withholding_naming_a_deductee_that_is_not_in_the_company_is_refused_by_name()
    {
        using var book = AlterationBook.New("tds-ghost-party");
        var (expense, party) = book.EnableTds(Section);
        var nature = book.Company.FindNatureOfPaymentByCode(Section)!;
        var payable = TdsPayable(book.Company);

        var posted = PostRaw(book, VoucherBaseType.Journal,
            new EntryLine(expense.Id, new Money(120000.30m), DrCr.Debit),
            new EntryLine(party.Id, new Money(108000.30m), DrCr.Credit),
            new EntryLine(payable.Id, new Money(12000.00m), DrCr.Credit,
                tds: new TdsLineTax(nature.Id, Section, new Money(120000.30m), 1000,
                    new Money(12000.00m), Guid.NewGuid(), panApplied: true)));

        var open = book.ForAlter(posted.Id);
        Assert.True(open.IsRefused);
        Assert.Contains("no longer in this company", open.Refusal!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("gross", open.Refusal!, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The entry screen computes exactly ONE carve-out per voucher, so a voucher carrying two assessments can only
    /// have arrived from an import and has no single leg to invert. Refused at the door.
    /// </summary>
    [Fact]
    public void A_voucher_carrying_two_withholding_assessments_is_refused_by_name()
    {
        using var book = AlterationBook.New("tds-two-carves");
        var (expense, party) = book.EnableTds(Section);
        var nature = book.Company.FindNatureOfPaymentByCode(Section)!;
        var payable = TdsPayable(book.Company);

        TdsLineTax Detail(decimal assessable, decimal tds) =>
            new(nature.Id, Section, new Money(assessable), 1000, new Money(tds), party.Id, panApplied: true);

        var posted = PostRaw(book, VoucherBaseType.Journal,
            new EntryLine(expense.Id, new Money(120000.30m), DrCr.Debit),
            new EntryLine(party.Id, new Money(96000.30m), DrCr.Credit),
            new EntryLine(payable.Id, new Money(12000.00m), DrCr.Credit, tds: Detail(120000.30m, 12000m)),
            new EntryLine(payable.Id, new Money(12000.00m), DrCr.Credit, tds: Detail(120000.30m, 12000m)));

        var open = book.ForAlter(posted.Id);
        Assert.True(open.IsRefused);
        Assert.Contains("2 withholding assessments", open.Refusal!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("exactly one carve-out", open.Refusal!, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The DEDUCTEE pin. Re-pointing the party row at a DIFFERENT deductee would move an already-reported
    /// deduction to another party's Form 16A and another line of Form 26Q, so it is refused rather than re-carved
    /// onto whoever the grid now shows.
    /// </summary>
    [Fact]
    public void Re_pointing_the_party_row_at_a_different_deductee_is_refused_by_name()
    {
        using var book = AlterationBook.New("tds-party-swap");
        var (expense, party) = book.EnableTds(Section);
        var other = book.Ledger("Beta Consultants", "Sundry Creditors");
        other.DeducteeType = DeducteeType.Firm;
        other.PartyPan = "AAPFU0939F";

        var posted = PostCarved(book, VoucherBaseType.Journal, expense, party, "120000.30");

        var message = RefuseOrThrow(book, posted.Id, e =>
        {
            e.Lines.Single(l => l.SelectedLedger?.Id == party.Id).SelectedLedger = other;
            e.Recalculate();
        });
        Assert.Contains("Beta Consultants", message, StringComparison.Ordinal);
        Assert.Contains("does not move a posted withholding", message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 🔴 <b>The panel is seeded from the POSTED section, and this is the only test that can tell.</b> While the
    /// expense ledger's default section still matches what was posted, seeding it and defaulting it give the same
    /// answer — so deleting the seed reddens nothing. Move the ledger's default AFTER posting and the difference
    /// becomes the whole outcome: seeded, the alteration re-carves under the posted §194J(b); defaulted, the panel
    /// opens on §194J(a) and the section pin refuses an alteration nobody had changed.
    /// </summary>
    [Fact]
    public void The_panel_opens_on_the_POSTED_section_when_the_expense_ledgers_default_has_moved()
    {
        using var book = AlterationBook.New("tds-default-moved");
        var (expense, party) = book.EnableTds(Section);

        var posted = PostCarved(book, VoucherBaseType.Journal, expense, party, "120000.30");
        Assert.Equal(Section, SingleTdsDetail(posted).SectionCode);

        // The expense ledger's DEFAULT section moves after posting — a master edit, not a voucher edit.
        expense.TdsNatureOfPaymentId = book.Company.FindNatureOfPaymentByCode("194J(a)")!.Id;

        var entry = OpenOrThrow(book, posted.Id);
        Assert.Equal(Section, entry.SelectedTdsNature!.SectionCode);

        entry.Narration = "narration only";
        Assert.True(entry.AcceptAlteration(), entry.Message);

        var after = book.Company.FindVoucher(posted.Id)!;
        Assert.Equal(Section, SingleTdsDetail(after).SectionCode);
        Assert.Equal(new Money(108000.30m), AmountOn(after, party.Id));
        Assert.Equal(new Money(12000.00m), AmountOn(after, TdsPayable(book.Company).Id));
    }

    // ================================================================ (H) what stays refused

    /// <summary>
    /// A COMPOSITION dealer's reverse-charge pair routes its balancing debit to the non-creditable RCM tax expense
    /// and leaves it UNTAGGED (composition blocks all ITC), so the engine's leg is indistinguishable on the grid
    /// from one the operator keyed. Refused by name — the one reverse-charge shape S5c does not lift.
    /// </summary>
    [Fact]
    public void A_composition_dealers_reverse_charge_pair_stays_refused_by_name()
    {
        using var book = AlterationBook.New("rcm-composition");
        EnableAdvancedGst(book);
        var fees = RcmExpense(book, "Legal Fees", "Legal", 1800);
        var advocate = RcmSupplier(book, "Advocate (Gujarat)", GstinGujarat, "24");

        var posted = book.Post(VoucherBaseType.Purchase, book.On(),
            new[] { (fees, DrCr.Debit, "10000.50"), (advocate, DrCr.Credit, "10000.50") });

        book.Company.Gst!.RegistrationType = GstRegistrationType.Composition;

        var open = book.ForAlter(posted.Id);
        Assert.True(open.IsRefused);
        Assert.Contains("COMPOSITION", open.Refusal!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no tax tag", open.Refusal!, StringComparison.OrdinalIgnoreCase);
    }
}
