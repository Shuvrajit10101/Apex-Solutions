using System;
using System.Collections.Generic;
using System.Linq;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Seed;
using Apex.Ledger.Services;
using Xunit;
using Domain = Apex.Ledger.Domain;

namespace Apex.Ledger.Tests;

/// <summary>
/// 🔴 <b>THE PRODUCT WAS EMITTING A SECTION CODE THAT IS NOT IN THE NOTIFIED FORM, INTO RETURNS FILED WITH THE
/// DEPARTMENT.</b> The seed shipped <c>"4IA"</c> and <c>"4IB"</c> for the two §194-I rent arms. The notified codes
/// are <c>"4-IA"</c> and <c>"4-IB"</c>, <b>with a hyphen</b>.
///
/// <para><b>The source, read first-hand 2026-09-08.</b> Income-tax Department, notified <b>FORM NO. 26Q</b>
/// ("Quarterly statement of deduction of tax under sub-section (3) of section 200 in respect of payments other
/// than salary"), <c>https://www.incometaxindia.gov.in/documents/d/guest/103120000000007861-pdf-2</c> — the
/// official three-column table under note 16, "List of section codes is as under". The two rows read verbatim:
/// <c>194-I(a) Rent <b>4-IA</b></c> and <c>194-I (b) Rent <b>4-IB</b></c>.</para>
///
/// <para>🔴 <b>WHY THIS IS NOT COSMETIC.</b> <c>FvuWriter</c> writes the code into the FVU flat file uploaded to
/// the Department, <c>Form26Q</c> reports it, and <c>Form16APdf</c> prints it on the certificate handed to the
/// deductee. Every §194-I rent deduction was being reported under a code the notified form does not carry.</para>
///
/// <para>🔴 <b>AND THE FIX HAD TO SURVIVE EXISTING BOOKS, WHICH IS THE HALF THAT NEEDED THE ENGINEERING.</b> The
/// code is <b>persisted per nature</b>, so correcting the seed alone would leave every book created before this
/// change still holding — and still filing — <c>"4IA"</c>. There is no schema budget for a data migration (v61
/// belongs to the GST track), so the value is normalised at the point of emission instead:
/// <see cref="NatureOfPayment.NotifiedFvuSectionCode"/> maps both spellings to the notified one and
/// <see cref="Form26Q"/> emits that. A legacy book and a freshly seeded book now file the identical code.</para>
///
/// <para><b>Every assertion below fails on the tree as it stood before this slice</b>, where the seed returned
/// <c>"4IA"</c>/<c>"4IB"</c>, <c>NotifiedFvuSectionCode</c> did not exist, and <c>Form26Q</c> emitted the stored
/// field verbatim.</para>
/// </summary>
public class Tds194IFvuSectionCodeTests
{
    private const string ValidTan = "MUMA12345B";
    private const string DeducteePan = "AAPFU0939F";

    private static readonly DateOnly Fy = new(2025, 4, 1);
    private static readonly DateOnly May = new(2025, 5, 10);

    /// <summary>
    /// 🔴 <b>THE NOTIFIED TABLE, TRANSCRIBED FROM THE FORM ITSELF</b> (see the class summary for the URL and the
    /// exact table). Section code ⇒ Form-26Q section code, for every section this product seeds. Transcribed once,
    /// here, so a single test can hold the WHOLE seed against it — a per-row assertion would have let a future row
    /// arrive uncompared, which is exactly how "4IA" survived eight sections' worth of review.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> NotifiedForm26QSectionCodes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["192A"] = "192A",      // "192A  Payment of accumulated balance due to an employee  192A"
            ["194A"] = "94A",       // "194A  Interest other than interest on securities  94A"
            ["194C"] = "94C",       // "194C  Payment of contractors and sub-contractors  94C"
            ["194EE"] = "4EE",      // "194EE  Payments in respect of deposits under National Savings Schemes  4EE"
            ["194G"] = "94G",       // "194G  Commission, prize etc., on sale of lottery tickets  94G"
            ["194H"] = "94H",       // "194H  Commission or Brokerage  94H"
            ["194I(a)"] = "4-IA",   // 🔴 "194-I(a)  Rent  4-IA"  — HYPHENATED
            ["194I(b)"] = "4-IB",   // 🔴 "194-I (b)  Rent  4-IB" — HYPHENATED
            ["194J(a)"] = "94J-A",  // "194J(a)  Fees for Technical Services ... and call center (@2%)  94J-A"
            ["194J(b)"] = "94J-B",  // "194J(b)  Fee for professional service or royalty etc (@10%)  94J-B"
            ["194K"] = "94K",       // "194K  Income in respect of units  94K"
            ["194LA"] = "4LA",      // "194LA  Payment of Compensation on acquisition of ... property  4LA"
            ["194-O"] = "94O",      // "194-O  Payment of certain sums by e-commerce operator ...  94O"
            ["194Q"] = "94Q",       // "194Q  Payment of certain sums for purchase of goods  94Q"
            ["194R"] = "94R",       // "194R  Benefits or perquisites of business or profession  94R"
            ["194S"] = "94S",       // "194S  Payment of consideration for transfer of virtual digital asset ...  94S"
            ["194T"] = "94T",       // "194T  Payment of salary, remuneration, commission, bonus or interest ...  94T"
        };

    // =================================================================================================
    //  1. The seed
    // =================================================================================================

    /// <summary>
    /// 🔴 <b>THE LITERAL WRONG VALUES, NAMED SO A REVERT IS UNMISTAKABLE.</b> §194-I(a) files <c>4-IA</c> and
    /// §194-I(b) files <c>4-IB</c>, and neither files the unhyphenated spelling the seed used to ship.
    /// </summary>
    [Fact]
    public void The_two_194I_arms_are_seeded_with_the_hyphenated_notified_codes()
    {
        var seeded = SeedTdsTcsRates.BuildTdsDefaults();

        var a = seeded.Single(n => n.SectionCode == "194I(a)");
        var b = seeded.Single(n => n.SectionCode == "194I(b)");

        Assert.Equal("4-IA", a.FvuSectionCode);
        Assert.Equal("4-IB", b.FvuSectionCode);
        Assert.NotEqual("4IA", a.FvuSectionCode);   // the literal pre-fix value
        Assert.NotEqual("4IB", b.FvuSectionCode);
        // And what actually reaches a return agrees with what is stored, on a freshly seeded book.
        Assert.Equal("4-IA", a.NotifiedFvuSectionCode);
        Assert.Equal("4-IB", b.NotifiedFvuSectionCode);
    }

    /// <summary>
    /// 🔴 <b>THE WHOLE SEED AGAINST THE NOTIFIED TABLE, IN ONE ASSERTION.</b> Every seeded nature must file the
    /// code the Department's own form gives its section — no exceptions, no allow-list. This is the test that
    /// would have caught <c>"4IA"</c> the day it was written, and it is also what stops the next long-tail
    /// instalment inventing a code from a pattern ("4EE" and "94G" are not derivable from each other).
    /// </summary>
    [Fact]
    public void Every_seeded_nature_files_the_section_code_the_notified_form_26Q_gives_it()
    {
        var mismatches = new List<string>();
        foreach (var n in SeedTdsTcsRates.BuildTdsDefaults())
        {
            if (!NotifiedForm26QSectionCodes.TryGetValue(n.SectionCode, out var notified))
            {
                mismatches.Add(
                    $"§{n.SectionCode} is seeded but has NO row transcribed from the notified Form 26Q. Read the "
                    + "form's own 'List of section codes' table and add it here BEFORE seeding the section — the "
                    + "FvuSectionCode is emitted into a filed return, so an invented code is a wrong figure.");
                continue;
            }
            if (!string.Equals(n.NotifiedFvuSectionCode, notified, StringComparison.Ordinal))
                mismatches.Add(
                    $"§{n.SectionCode} files '{n.NotifiedFvuSectionCode}' but the notified Form 26Q says "
                    + $"'{notified}'.");
        }

        Assert.True(mismatches.Count == 0, string.Join("\n", mismatches));
    }

    // =================================================================================================
    //  2. The normaliser, in both directions
    // =================================================================================================

    /// <summary>
    /// The tolerance is what lets an existing book keep working: a stored legacy spelling, in any casing or
    /// spacing, maps to the notified code — and a book already holding the notified code is returned unchanged, so
    /// re-running the normaliser on its own output is a no-op.
    /// </summary>
    [Theory]
    [InlineData("4IA", "4-IA")]
    [InlineData("4ia", "4-IA")]
    [InlineData(" 4IA ", "4-IA")]
    [InlineData("4 I A", "4-IA")]
    [InlineData("4-IA", "4-IA")]   // already notified ⇒ unchanged (idempotent)
    [InlineData("4-ia", "4-IA")]
    [InlineData("4IB", "4-IB")]
    [InlineData("4ib", "4-IB")]
    [InlineData("4-IB", "4-IB")]   // already notified ⇒ unchanged (idempotent)
    public void The_194I_family_codes_normalise_to_the_notified_spelling_from_either_direction(
        string stored, string expected)
    {
        Assert.Equal(expected, NatureOfPayment.NormalizeFvuSectionCode(stored));
        // Idempotent: normalising the output again cannot move it.
        Assert.Equal(expected, NatureOfPayment.NormalizeFvuSectionCode(expected));
    }

    /// <summary>
    /// 🔴 <b>THE NORMALISER IS SCOPED TO THE §194-I FAMILY AND MUST NEVER INVENT A CODE.</b> Every other section's
    /// code is returned verbatim — including ones that already contain a hyphen (<c>94J-A</c>) and ones whose
    /// notified spelling has <b>no</b> hyphen where a naive "insert a hyphen before the letters" rule would add
    /// one (<c>4EE</c>, <c>4LA</c>, <c>4BB</c>, <c>4DA</c>). A rule that reshaped those would file four more wrong
    /// codes while fixing two.
    /// </summary>
    [Theory]
    [InlineData("94A")]
    [InlineData("94C")]
    [InlineData("94H")]
    [InlineData("94J-A")]
    [InlineData("94J-B")]
    [InlineData("94Q")]
    [InlineData("94T")]
    [InlineData("94R")]
    [InlineData("94S")]
    [InlineData("94G")]
    [InlineData("94K")]
    [InlineData("94O")]
    [InlineData("192A")]
    [InlineData("4EE")]   // NOT "4-EE"
    [InlineData("4LA")]   // NOT "4-LA"
    [InlineData("4BB")]   // NOT "4-BB" — §194BB, not seeded but in the notified table
    [InlineData("4DA")]   // NOT "4-DA" — §194DA, not seeded but in the notified table
    [InlineData("94R-P")]
    [InlineData("94N-FT")]
    public void Every_code_outside_the_194I_family_is_returned_verbatim(string code)
    {
        Assert.Equal(code, NatureOfPayment.NormalizeFvuSectionCode(code));
    }

    // =================================================================================================
    //  3. The legacy book — the half that needed no migration
    // =================================================================================================

    /// <summary>A book whose §194-I natures persist the SUPERSEDED spelling, which is every book created before
    /// this slice. Seeded explicitly onto the config before <c>EnableTds</c>, which preserves natures already
    /// present rather than re-seeding — the same path a restored backup takes.</summary>
    private static Company LegacyBook()
    {
        var c = CompanyFactory.CreateSeeded("Legacy Rent Co", Fy);
        var cfg = new TdsConfig { Tan = ValidTan };
        foreach (var n in SeedTdsTcsRates.BuildTdsDefaults())
        {
            // Rewind exactly the two rows this slice moved; everything else is seeded as-is.
            var stored = n.SectionCode switch { "194I(a)" => "4IA", "194I(b)" => "4IB", _ => n.FvuSectionCode };
            cfg.AddNatureOfPayment(new NatureOfPayment(
                n.Id, n.SectionCode, n.Name, n.RateWithPanBp, n.RateWithoutPanBp, stored,
                n.SingleTransactionThreshold, n.CumulativeThreshold, n.EffectiveFrom, n.IsPredefined));
        }
        new TdsTcsService(c).EnableTds(cfg);
        return c;
    }

    private static Domain.Ledger AddLedger(Company c, string name, string groupName, bool openingIsDebit)
    {
        var l = new Domain.Ledger(Guid.NewGuid(), name, c.FindGroupByName(groupName)!.Id, Money.Zero, openingIsDebit);
        c.AddLedger(l);
        return l;
    }

    /// <summary>Posts one ₹60,000 §194-I(b) rent assessment (liable through the ₹50,000 monthly limb) and returns
    /// the book, so a real Form 26Q can be built over a real deduction.</summary>
    private static Company BookOneRentDeduction(Company c)
    {
        var rent = AddLedger(c, "Office Rent", "Indirect Expenses", true);
        var landlord = AddLedger(c, "Landlord", "Sundry Creditors", false);
        landlord.DeducteeType = DeducteeType.Individual;
        landlord.PartyPan = DeducteePan;

        var nop = c.FindNatureOfPaymentByCode("194I(b)")!;
        rent.TdsApplicable = true;
        rent.TdsNatureOfPaymentId = nop.Id;

        var gross = Money.FromRupees(60_000m);
        var carve = new TdsService(c).BuildCarveOut(gross, gross, nop, landlord, May);
        var lines = new List<EntryLine> { new(rent.Id, gross, DrCr.Debit), carve.PartyLine };
        if (carve.TdsPayableLine is { } payable) lines.Add(payable);
        var typeId = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Journal).Id;
        new LedgerService(c).Post(new Voucher(Guid.NewGuid(), typeId, May, lines));
        return c;
    }

    /// <summary>
    /// 🔴 <b>THE DEFECT, MEASURED THROUGH THE REAL RETURN BUILDER ON A BOOK THAT STILL STORES THE OLD SPELLING.</b>
    /// The stored field is untouched — deliberately, since normalising it needs a migration — and the return
    /// nevertheless carries the notified code. This is the assertion that says the fix reaches EXISTING users, not
    /// only new ones, and it is the one that fails on today's main.
    /// </summary>
    [Fact]
    public void A_legacy_book_still_storing_4IB_files_the_notified_4_IB()
    {
        var c = BookOneRentDeduction(LegacyBook());

        // The book really is legacy: the persisted value is the superseded one, and stays that way.
        Assert.Equal("4IB", c.FindNatureOfPaymentByCode("194I(b)")!.FvuSectionCode);

        var q1 = Form26Q.Build(c, 2025, 1);
        var row = Assert.Single(q1.Deductees);

        Assert.Equal("4-IB", row.FvuSectionCode);
        Assert.NotEqual("4IB", row.FvuSectionCode);          // the literal pre-fix filed value
        Assert.Equal(Money.FromRupees(6_000m), row.TdsAmount); // and the money is untouched by the code fix
    }

    /// <summary>
    /// The same deduction booked in a <b>fresh</b> book files the identical code — so the two populations are not
    /// split, which is the objection that kept the previous pass from changing the seed at all.
    /// </summary>
    [Fact]
    public void A_fresh_book_and_a_legacy_book_file_the_identical_code_for_the_same_deduction()
    {
        var fresh = CompanyFactory.CreateSeeded("Fresh Rent Co", Fy);
        new TdsTcsService(fresh).EnableTds(new TdsConfig { Tan = ValidTan });

        var freshRow = Assert.Single(Form26Q.Build(BookOneRentDeduction(fresh), 2025, 1).Deductees);
        var legacyRow = Assert.Single(Form26Q.Build(BookOneRentDeduction(LegacyBook()), 2025, 1).Deductees);

        Assert.Equal(freshRow.FvuSectionCode, legacyRow.FvuSectionCode);
        Assert.Equal("4-IB", freshRow.FvuSectionCode);
    }

    /// <summary>
    /// The §194-I(a) arm through the same path — <c>4-IA</c> on a legacy book. Booked at ₹60,000 of plant rent,
    /// two per cent, ₹1,200.00.
    /// </summary>
    [Fact]
    public void The_plant_and_machinery_arm_files_the_notified_4_IA_from_a_legacy_book()
    {
        var c = LegacyBook();
        var rent = AddLedger(c, "Plant Hire", "Indirect Expenses", true);
        var lessor = AddLedger(c, "Lessor", "Sundry Creditors", false);
        lessor.DeducteeType = DeducteeType.Individual;
        lessor.PartyPan = DeducteePan;
        var nop = c.FindNatureOfPaymentByCode("194I(a)")!;
        Assert.Equal("4IA", nop.FvuSectionCode);   // legacy stored value

        rent.TdsApplicable = true;
        rent.TdsNatureOfPaymentId = nop.Id;
        var gross = Money.FromRupees(60_000m);
        var carve = new TdsService(c).BuildCarveOut(gross, gross, nop, lessor, May);
        var lines = new List<EntryLine> { new(rent.Id, gross, DrCr.Debit), carve.PartyLine };
        if (carve.TdsPayableLine is { } payable) lines.Add(payable);
        var typeId = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Journal).Id;
        new LedgerService(c).Post(new Voucher(Guid.NewGuid(), typeId, May, lines));

        var row = Assert.Single(Form26Q.Build(c, 2025, 1).Deductees);
        Assert.Equal("4-IA", row.FvuSectionCode);
        Assert.Equal(Money.FromRupees(1_200m), row.TdsAmount);
    }
}
