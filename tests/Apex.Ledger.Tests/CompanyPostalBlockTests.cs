using Apex.Ledger.Domain;
using Apex.Ledger.Services;

namespace Apex.Ledger.Tests;

/// <summary>
/// <b>W0-2a — the supplier postal block's floor.</b> Once <c>Company.Pin</c> began printing on a tax invoice, the
/// recipient/supplier asymmetry that mattered stopped being "which fields print" and became "which fields are
/// VALIDATED": <c>PartyMailingDetails.EnsureValid</c> has rejected a malformed recipient PIN since v45, while
/// <c>Company</c> had <b>no validation call site anywhere in <c>src/</c></b> — so a canonical document carrying
/// <c>pin="abcdef"</c> would print <c>PIN: abcdef</c> on a statutory document.
///
/// <para>Both sides now apply the one rule in <see cref="IndianPinCode"/>. These tests pin the company half; the
/// party half is pinned by <c>CanonicalPartyMailingRoundTripTests</c>, and the import boundary by
/// <c>CanonicalCompanyPostalRoundTripTests</c>.</para>
/// </summary>
public sealed class CompanyPostalBlockTests
{
    private static readonly DateOnly FyStart = new(2025, 4, 1);

    private static Company Fresh() => CompanyFactory.CreateSeeded("Pin Floor Traders", FyStart);

    [Fact]
    public void A_well_formed_six_digit_PIN_is_accepted()
    {
        var c = Fresh();
        c.Pin = "411037";
        c.EnsureValid();          // does not throw
        Assert.Equal("411037", c.Pin);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void An_unset_PIN_is_not_an_invalid_one(string? pin)
    {
        var c = Fresh();
        c.Pin = pin;
        c.EnsureValid();          // unset is legal — the field is optional
    }

    [Theory]
    [InlineData("abcdef")]        // the canonical-import case: six chars, no digits
    [InlineData("41103")]         // five digits
    [InlineData("4110377")]       // seven digits
    [InlineData("011037")]        // leading zero — India Post allots 1-9
    [InlineData("41 037")]        // embedded space
    [InlineData("4110३7")]        // non-ASCII digit (Devanagari 3) — char.IsDigit would accept this
    public void A_malformed_PIN_is_refused(string pin)
    {
        var c = Fresh();
        c.Pin = pin;

        var ex = Assert.Throws<ArgumentException>(() => c.EnsureValid());

        // Discriminating: the party-side message for the same bad value is "PIN code '…' is not a valid …".
        // Asserting only the shared tail would pass against the party message too.
        Assert.StartsWith($"Company PIN code '{pin}' is not a valid 6-digit Indian PIN code.", ex.Message);
    }

    /// <summary>
    /// The guard is on <c>Company</c> itself, not on a caller — so it holds for every save path, including ones
    /// that do not exist yet (the W0-2b Company Alter screen). Removing <c>Company.EnsureValid</c>'s body reddens
    /// <see cref="A_malformed_PIN_is_refused"/> six times over.
    /// </summary>
    [Fact]
    public void The_company_and_party_PIN_rules_are_the_same_rule()
    {
        foreach (var bad in new[] { "abcdef", "41103", "011037" })
        {
            var c = Fresh();
            c.Pin = bad;
            Assert.Throws<ArgumentException>(() => c.EnsureValid());

            var party = new PartyMailingDetails { Address = "12 MG Road", Pincode = bad };
            Assert.Throws<ArgumentException>(() => party.EnsureValid());
        }

        foreach (var good in new[] { "411037", "700019", "110001" })
        {
            var c = Fresh();
            c.Pin = good;
            c.EnsureValid();

            var party = new PartyMailingDetails { Address = "12 MG Road", Pincode = good };
            party.EnsureValid();
        }
    }
    // ===================================================================== the second header invariant (W0-2b)

    /// <summary>
    /// 🔴 <c>EnsureValid</c> ALSO holds <c>BooksBeginFrom &gt;= FinancialYearStart</c>, and it had to, because the
    /// CONSTRUCTOR could not. Both dates are plain settable properties, so any caller can assign a pair
    /// <c>new Company(...)</c> would have refused — and <c>SqliteCompanyStore.Load</c> rebuilds the aggregate
    /// through that very constructor. The measured consequence: a company saved in that state wrote to disk
    /// without complaint and then threw on the way back IN, leaving the book permanently unopenable with no UI
    /// recovery. <b>Save and Load now refuse the same states.</b>
    /// <para><i>Mutation that reddens it:</i> delete the date clause from <c>Company.EnsureValid</c>.</para>
    /// </summary>
    [Fact]
    public void Books_beginning_before_the_financial_year_start_is_refused_by_EnsureValid()
    {
        var c = Fresh();
        c.BooksBeginFrom = FyStart.AddDays(-1);

        var ex = Assert.Throws<ArgumentException>(() => c.EnsureValid());
        Assert.Contains("earlier than the financial-year start", ex.Message, StringComparison.Ordinal);

        // The constructor's own refusal is unchanged, and worded differently on purpose.
        Assert.Throws<ArgumentException>(() =>
            new Company(Guid.NewGuid(), "Impossible Co", FyStart, FyStart.AddDays(-1)));
    }

    /// <summary>The accepted end of the same rule, so it cannot be satisfied by refusing every pair.</summary>
    [Theory]
    [InlineData(0)]      // same day — the ordinary case
    [InlineData(91)]     // a mid-year books start, which the corpus names explicitly (Book p.13)
    public void A_books_date_on_or_after_the_financial_year_start_is_accepted(int daysAfter)
    {
        var c = Fresh();
        c.BooksBeginFrom = FyStart.AddDays(daysAfter);
        c.EnsureValid();          // does not throw
    }
}
