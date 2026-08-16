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
}
