using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// CA slice S9 — the <see cref="StatuteVocabulary"/> Income-tax Act <b>1961 → 2025 cutover gate</b>.
///
/// <para>The contract under test is deliberately narrow: this type maps <b>display labels</b> and nothing else. The
/// tests below therefore prove three things — the gate flips at exactly the right financial year, <b>confirmed</b>
/// renumberings apply on the far side of it, and <b>unverified</b> ones are left strictly alone. That last group is
/// the important one: a blanket rename would falsify already-filed prior-year certificates and would re-cite sections
/// nobody retrieved from a primary source.</para>
/// </summary>
public sealed class StatuteVocabularyTests
{
    // ------------------------------------------------------------------ the gate itself

    [Theory]
    [InlineData(2019, false)]
    [InlineData(2024, false)]
    [InlineData(2025, false)]   // FY 2025-26 — the last year of the 1961 Act
    [InlineData(2026, true)]    // FY 2026-27 — the 1961 Act stands repealed on 01.04.2026
    [InlineData(2027, true)]
    [InlineData(2030, true)]
    public void The_act_gate_flips_between_fy_2025_26_and_fy_2026_27(int fyStartYear, bool expected2025Act)
        => Assert.Equal(expected2025Act, StatuteVocabulary.IsAct2025(fyStartYear));

    // ------------------------------------------------------------------ confirmed renumbering

    [Theory]
    [InlineData("192", "392")]
    [InlineData("192A", "392(7)")]
    [InlineData("200", "397")]
    [InlineData("200A", "399")]
    [InlineData("201", "398")]
    [InlineData("203", "395")]
    [InlineData("87A", "156")]
    [InlineData("206C", "394")]
    [InlineData("234E", "427")]
    [InlineData("271H", "461")]
    [InlineData("276B", "476")]
    public void Confirmed_sections_renumber_only_from_fy_2026_27(string legacy, string renumbered)
    {
        Assert.Equal(legacy, StatuteVocabulary.SectionLabel(legacy, 2025));      // 1961 Act — untouched
        Assert.Equal(renumbered, StatuteVocabulary.SectionLabel(legacy, 2026));  // 2025 Act — renumbered
    }

    [Theory]
    [InlineData("24Q", "138")]
    [InlineData("26Q", "140")]
    [InlineData("27Q", "144")]
    [InlineData("27EQ", "143")]
    [InlineData("16", "130")]
    [InlineData("16A", "131")]
    [InlineData("12BB", "124")]
    [InlineData("12BA", "123")]
    [InlineData("27D", "133")]
    [InlineData("24G", "137")]
    public void Confirmed_forms_renumber_only_from_fy_2026_27(string legacy, string renumbered)
    {
        Assert.Equal(legacy, StatuteVocabulary.FormLabel(legacy, 2025));
        Assert.Equal(renumbered, StatuteVocabulary.FormLabel(legacy, 2026));
    }

    // ------------------------------------------------------------------ THE GUARD: unverified stays unverified

    /// <summary>
    /// <b>The load-bearing test.</b> None of these was retrieved from a primary source, so none may be renamed or
    /// re-cited — <b>in either direction of the gate</b>. An unknown key must fall through unchanged, which is what
    /// makes "absence of a mapping" a working state rather than a hole. If someone later adds an unsourced entry to
    /// the dictionaries, this test goes red.
    /// </summary>
    [Theory]
    // the entire §194x family
    [InlineData("194C")]
    [InlineData("194I")]
    [InlineData("194J")]
    [InlineData("194Q")]
    [InlineData("194A")]
    [InlineData("194H")]
    // no-PAN higher-rate sections — DIFFERENT statutes from §206C, and unverified
    [InlineData("206AA")]
    [InlineData("206CC")]
    // regime / deduction sections
    [InlineData("115BAC")]
    [InlineData("80C")]
    [InlineData("80CCD(1B)")]
    [InlineData("80D")]
    public void Unverified_sections_are_never_renamed_on_either_side_of_the_gate(string unverified)
    {
        Assert.Equal(unverified, StatuteVocabulary.SectionLabel(unverified, 2025));
        Assert.Equal(unverified, StatuteVocabulary.SectionLabel(unverified, 2026));
        Assert.Equal(unverified, StatuteVocabulary.SectionLabel(unverified, 2030));
    }

    /// <summary>§206AA/§206CC must not be reachable via their confirmed look-alikes §206C — the pairs differ by a
    /// single character and carry different rates, so a sloppy prefix/StartsWith mapping would silently mis-cite.</summary>
    [Fact]
    public void The_206_family_look_alikes_do_not_collide()
    {
        Assert.Equal("394", StatuteVocabulary.SectionLabel("206C", 2026));    // confirmed → renumbered
        Assert.Equal("206AA", StatuteVocabulary.SectionLabel("206AA", 2026)); // unverified → untouched
        Assert.Equal("206CC", StatuteVocabulary.SectionLabel("206CC", 2026)); // unverified → untouched
    }

    /// <summary>Form 27A carries no confirmed renumbering, so it must survive the gate unchanged.</summary>
    [Fact]
    public void Unverified_forms_are_never_renamed()
    {
        Assert.Equal("27A", StatuteVocabulary.FormLabel("27A", 2026));
        Assert.Equal("27A", StatuteVocabulary.FormLabelDual("27A"));
    }

    // ------------------------------------------------------------------ the period label + the date trap

    [Fact]
    public void Period_caption_follows_the_gate()
    {
        Assert.Equal("Assessment Year", StatuteVocabulary.PeriodCaption(2025));
        Assert.Equal("Tax Year", StatuteVocabulary.PeriodCaption(2026));
        Assert.Equal("AY", StatuteVocabulary.PeriodCaptionShort(2025));
        Assert.Equal("Tax Year", StatuteVocabulary.PeriodCaptionShort(2026));
    }

    /// <summary>
    /// Under the 2025 Act the <b>tax year IS the financial year</b>, so the period is a <b>value</b> change, not just
    /// a caption change: FY 2026-27 shows "2026-27", where the retired assessment-year framing would have shown
    /// "2027-28".
    /// </summary>
    [Theory]
    [InlineData(2024, "2025-26")]   // AY = FY + 1
    [InlineData(2025, "2026-27")]   // AY = FY + 1
    [InlineData(2026, "2026-27")]   // tax year == FY
    [InlineData(2027, "2027-28")]   // tax year == FY
    public void Period_label_is_a_value_change_not_only_a_caption_change(int fyStartYear, string expected)
        => Assert.Equal(expected, StatuteVocabulary.PeriodLabel(fyStartYear));

    /// <summary>
    /// <b>The date trap, pinned.</b> FY 2025-26 ("AY 2026-27") and FY 2026-27 ("Tax Year 2026-27") produce the
    /// <b>same numerals</b> for <b>different years</b>. The values collide; the captions must not. Any future change
    /// that renders a bare period value without its caption is losing information, and this test says so.
    /// </summary>
    [Fact]
    public void The_two_vocabularies_collide_numerically_and_are_separated_only_by_the_caption()
    {
        Assert.Equal(StatuteVocabulary.PeriodLabel(2025), StatuteVocabulary.PeriodLabel(2026)); // "2026-27" both
        Assert.NotEqual(StatuteVocabulary.PeriodCaption(2025), StatuteVocabulary.PeriodCaption(2026));
        Assert.Equal("Assessment Year 2026-27", StatuteVocabulary.PeriodCaptionAndLabel(2025));
        Assert.Equal("Tax Year 2026-27", StatuteVocabulary.PeriodCaptionAndLabel(2026));
    }

    [Fact]
    public void The_dual_form_is_used_where_no_financial_year_is_in_scope()
    {
        Assert.Equal("24Q / 138", StatuteVocabulary.FormLabelDual("24Q"));
        Assert.Equal("16 / 130", StatuteVocabulary.FormLabelDual("16"));
    }

    [Fact]
    public void Null_keys_are_rejected_rather_than_silently_mapped()
    {
        Assert.Throws<ArgumentNullException>(() => StatuteVocabulary.SectionLabel(null!, 2026));
        Assert.Throws<ArgumentNullException>(() => StatuteVocabulary.FormLabel(null!, 2026));
        Assert.Throws<ArgumentNullException>(() => StatuteVocabulary.FormLabelDual(null!));
    }

    // ------------------------------------------------------------------ S9 changes no number

    /// <summary>
    /// S9 is a <b>presentation-layer</b> slice: the vocabulary gate must not have moved a single figure in the tax
    /// engine. These are the constants the CONFIRMED table was checked against — s.202(1) slabs, s.19(1) standard
    /// deduction, s.156(2) rebate — plus the 4% cess, which stays put because no cess levy was found in Finance Act
    /// 2026 Part III and the app holds FY 2025-26 data under FA 2025.
    /// </summary>
    [Fact]
    public void The_vocabulary_gate_moves_no_number_in_the_tax_engine()
    {
        Assert.Equal(75_000m, SalaryIncomeTax.NewRegimeStandardDeduction);
        Assert.Equal(50_000m, SalaryIncomeTax.OldRegimeStandardDeduction);
        Assert.Equal(12_00_000m, SalaryIncomeTax.NewRegimeRebateTaxableCeiling);
        Assert.Equal(60_000m, SalaryIncomeTax.NewRegimeRebateCap);
        Assert.Equal(0.04m, SalaryIncomeTax.CessRate);

        // The s.202(1) slab boundaries, exercised through the public marginal-tax entry point (new regime):
        // 0 up to ₹4L, then 5/10/15/20/25/30% — unchanged by anything in S9.
        Assert.Equal(0m, SalaryIncomeTax.SlabTax(4_00_000m, TaxRegime.New));
        Assert.Equal(20_000m, SalaryIncomeTax.SlabTax(8_00_000m, TaxRegime.New));
        Assert.Equal(60_000m, SalaryIncomeTax.SlabTax(12_00_000m, TaxRegime.New));
        Assert.Equal(1_20_000m, SalaryIncomeTax.SlabTax(16_00_000m, TaxRegime.New));
        Assert.Equal(2_00_000m, SalaryIncomeTax.SlabTax(20_00_000m, TaxRegime.New));
        Assert.Equal(3_00_000m, SalaryIncomeTax.SlabTax(24_00_000m, TaxRegime.New));
    }
}
