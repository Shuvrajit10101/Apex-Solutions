using System;
using System.Globalization;
using System.Threading;
using Apex.Desktop.Services;
using Xunit;

namespace Apex.Desktop.Tests;

/// <summary>
/// WI-5 — the ONE app-wide date contract: a canonical <c>dd-MMM-yyyy</c> renderer and a lenient,
/// explicitly DAY-FIRST parser.
/// <para>
/// The load-bearing test here is <see cref="Day_first_numeric_date_is_read_the_Indian_way_not_month_first"/>:
/// it pins the live correctness bug this work item exists to kill. The app used to parse dates with
/// <see cref="CultureInfo.InvariantCulture"/>, whose short-date convention is <c>MM/dd</c> — so a user typing
/// the Indian-convention <c>03/04/2024</c> silently posted <b>4-March</b> instead of <b>3-April</b>. It hid in
/// testing because it only bites when the day is ≤ 12. Reverting <see cref="ApexDate"/> to an
/// InvariantCulture parse turns that test RED (verified by mutation, not by assumption).
/// </para>
/// </summary>
public sealed class ApexDateTests
{
    // ------------------------------------------------------------------ the misread lock

    /// <summary>
    /// THE regression lock. "03/04/2024" is 3-Apr-2024 (day-first), never 4-Mar-2024 (month-first).
    /// The corpus grounds this: the Tally book instructs "Date - Type date of Purchase/Sale transactions by
    /// pressing F2, like I type '01/04/2020'" — meaning 1-Apr-2020, which an InvariantCulture parse would
    /// silently read as 4-Jan-2020, i.e. the WRONG FINANCIAL YEAR.
    /// </summary>
    [Fact]
    public void Day_first_numeric_date_is_read_the_Indian_way_not_month_first()
    {
        Assert.True(ApexDate.TryParse("03/04/2024", out var d));

        // Assert the components separately so a failure names the defect rather than just a date mismatch.
        Assert.Equal(3, d.Day);
        Assert.Equal(4, d.Month);          // April — NOT 3 (March), which is the MM/dd misread
        Assert.Equal(2024, d.Year);
        Assert.Equal(new DateOnly(2024, 4, 3), d);
    }

    /// <summary>The corpus's own worked example: "01/04/2020" is 1-Apr-2020 (FY 2020-21), not 4-Jan-2020.</summary>
    [Fact]
    public void Corpus_example_01_04_2020_is_first_of_April_not_fourth_of_January()
    {
        Assert.True(ApexDate.TryParse("01/04/2020", out var d));
        Assert.Equal(new DateOnly(2020, 4, 1), d);
        Assert.NotEqual(new DateOnly(2020, 1, 4), d);
    }

    /// <summary>Every accepted separator reaches the same day-first reading.</summary>
    [Theory]
    [InlineData("03/04/2024")]
    [InlineData("03-04-2024")]
    [InlineData("03.04.2024")]
    [InlineData("3/4/2024")]
    [InlineData("3-4-2024")]
    [InlineData("03042024")]
    public void All_day_first_numeric_spellings_agree(string input)
    {
        Assert.True(ApexDate.TryParse(input, out var d));
        Assert.Equal(new DateOnly(2024, 4, 3), d);
    }

    /// <summary>A day &gt; 12 cannot be a month, so it proves the ordering independently of the ≤12 blind spot.</summary>
    [Fact]
    public void A_day_above_twelve_parses_rather_than_failing_as_a_month()
    {
        Assert.True(ApexDate.TryParse("31/01/2021", out var d));
        Assert.Equal(new DateOnly(2021, 1, 31), d);
    }

    // ------------------------------------------------------------------ canonical round-trip

    [Fact]
    public void Canonical_format_is_dd_MMM_yyyy()
    {
        Assert.Equal("dd-MMM-yyyy", ApexDate.Canonical);
        Assert.Equal("03-Apr-2024", ApexDate.Format(new DateOnly(2024, 4, 3)));
        Assert.Equal("01-Apr-2020", ApexDate.Format(new DateOnly(2020, 4, 1)));
    }

    /// <summary>Format → parse → format is stable, and every accepted input echoes to the SAME canonical text.</summary>
    [Theory]
    [InlineData("03-Apr-2024")]
    [InlineData("3-Apr-2024")]
    [InlineData("03/04/2024")]
    [InlineData("3.4.2024")]
    [InlineData("2024-04-03")]
    [InlineData("03-Apr-24")]
    [InlineData("03/04/24")]
    public void Every_accepted_spelling_round_trips_to_the_one_canonical_rendering(string input)
    {
        Assert.True(ApexDate.TryParse(input, out var d));
        var canonical = ApexDate.Format(d);
        Assert.Equal("03-Apr-2024", canonical);

        // …and the canonical rendering itself re-parses to the same date (idempotent echo).
        Assert.True(ApexDate.TryParse(canonical, out var again));
        Assert.Equal(d, again);
        Assert.Equal(canonical, ApexDate.Format(again));
    }

    [Fact]
    public void Iso_input_is_still_accepted_so_existing_unambiguous_input_keeps_working()
    {
        Assert.True(ApexDate.TryParse("2024-04-03", out var d));
        Assert.Equal(new DateOnly(2024, 4, 3), d);
    }

    // ------------------------------------------------------------------ partial input

    [Fact]
    public void A_bare_day_completes_from_the_context_month_and_year()
    {
        var context = new DateOnly(2024, 4, 30);
        Assert.True(ApexDate.TryParse("15", context, out var d));
        Assert.Equal(new DateOnly(2024, 4, 15), d);
    }

    [Fact]
    public void A_bare_day_and_month_completes_from_the_context_year()
    {
        var context = new DateOnly(2024, 4, 30);
        Assert.True(ApexDate.TryParse("15/05", context, out var d));
        Assert.Equal(new DateOnly(2024, 5, 15), d);

        Assert.True(ApexDate.TryParse("15-Jun", context, out var m));
        Assert.Equal(new DateOnly(2024, 6, 15), m);
    }

    [Fact]
    public void A_bare_day_that_does_not_exist_in_the_context_month_is_rejected()
    {
        var context = new DateOnly(2024, 2, 1);      // February 2024 has 29 days
        Assert.False(ApexDate.TryParse("31", context, out _));
    }

    // ------------------------------------------------------------------ rejection (never a silent wrong date)

    /// <summary>
    /// Unparseable input must be REJECTED, not coerced into a plausible-looking date. This is the contract the
    /// call sites rely on to keep the prior value and surface an error instead of silently discarding input.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a date")]
    [InlineData("32/01/2024")]   // no such day
    [InlineData("03/13/2024")]   // month 13 — the MM/dd reading is NOT silently attempted
    [InlineData("2024")]         // ambiguous bare year
    [InlineData("abc/def/ghi")]
    public void Unparseable_input_is_rejected(string input)
    {
        Assert.False(ApexDate.TryParse(input, new DateOnly(2024, 4, 1), out var d));
        Assert.Equal(default, d);
    }

    /// <summary>
    /// A month-first date is not quietly re-read as day-first nonsense: "03/13/2024" has no day-first reading
    /// (month 13), so it is rejected outright rather than swapped into 13-Mar.
    /// </summary>
    [Fact]
    public void Month_first_input_is_rejected_rather_than_silently_swapped()
    {
        Assert.False(ApexDate.TryParse("03/13/2024", out _));
    }

    [Fact]
    public void Error_message_names_the_canonical_format()
    {
        var msg = ApexDate.ErrorFor("qwerty");
        Assert.Contains("dd-MMM-yyyy", msg);
        Assert.Contains("qwerty", msg);
    }

    // ------------------------------------------------------------------ culture independence (3-OS CI)

    /// <summary>
    /// The parser is EXPLICITLY day-first, not culture-dependent: it must return the same date under a
    /// month-first culture (en-US) as under a day-first one (en-IN) and under the invariant culture. This is
    /// what keeps the behaviour identical across the three CI operating systems.
    /// </summary>
    [Theory]
    [InlineData("en-US")]   // month-first culture
    [InlineData("en-IN")]   // day-first culture
    [InlineData("de-DE")]   // dot-separated, day-first
    [InlineData("")]        // invariant
    public void Parsing_is_independent_of_the_ambient_culture(string cultureName)
    {
        var original = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);

            Assert.True(ApexDate.TryParse("03/04/2024", out var d));
            Assert.Equal(new DateOnly(2024, 4, 3), d);
            Assert.Equal("03-Apr-2024", ApexDate.Format(d));
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = original;
        }
    }
}
