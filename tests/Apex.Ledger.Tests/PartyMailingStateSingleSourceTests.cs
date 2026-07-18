using System;
using System.Linq;
using System.Reflection;
using Apex.Ledger.Domain;
using DomainLedger = Apex.Ledger.Domain.Ledger;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// 🔴 THE ONE-STATE TAX-SAFETY INVARIANT, locked for real.
///
/// <para><b>Why this file exists.</b> <see cref="PartyMailingDetails"/>'s own documentation cited a class of this
/// name as the guard on the invariant — "including a reflection assertion that this type declares no State
/// member" — and <b>no such class existed anywhere in the repo</b>. The invariant did hold, but the named guard
/// was fictional: a future reader would trust a test that could never fail, and the first person to add a
/// convenient <c>State</c> property to the mailing block would meet no resistance at all. The invariant is a
/// tax-safety property, so it gets a real lock rather than a corrected comment.</para>
///
/// <para><b>The invariant.</b> A party's State/UT exists <b>exactly once</b>, on
/// <see cref="PartyGstDetails.StateCode"/>, and it is the place-of-supply driver (CGST+SGST vs IGST). The mailing
/// screen's State field reads and writes <see cref="Ledger.MailingStateCode"/>, a pure delegating accessor over
/// that one storage location. Because there is only ONE place to store it, a mailing State and a place-of-supply
/// State <b>cannot</b> diverge — so a user cannot set mailing State = Maharashtra while the GST State stays
/// Karnataka and silently get the wrong tax head. That is a structural guarantee, not a synchronisation rule
/// somebody has to remember to run, and these tests are what keep it structural.</para>
/// </summary>
public sealed class PartyMailingStateSingleSourceTests
{
    private static DomainLedger NewParty() =>
        new(Guid.NewGuid(), "Naresh Traders", Guid.NewGuid(), Money.Zero, openingIsDebit: true);

    // ================================================================= (1) THE REFLECTION ASSERTION

    /// <summary>
    /// 🔴 THE STRUCTURAL LOCK. <see cref="PartyMailingDetails"/> must declare <b>no</b> State-ish member of any
    /// kind — property, field or method. Adding <c>public string? State { get; set; }</c> (or a
    /// <c>StateCode</c>, or a <c>SetState</c>) creates a SECOND storage location for the place-of-supply driver
    /// and fails this test immediately, which is the entire point: the failure message tells the author why the
    /// convenient-looking addition is a tax hazard.
    ///
    /// <para><b>This test bites.</b> Adding <c>public string? StateCode { get; set; }</c> to
    /// <c>PartyMailingDetails</c> fails it with the member named in the message — verified by doing exactly that
    /// against a checksummed backup and restoring byte-exact.</para>
    /// </summary>
    [Fact]
    public void PartyMailingDetails_declares_no_State_member_of_any_kind()
    {
        var offenders = typeof(PartyMailingDetails)
            .GetMembers(BindingFlags.Public | BindingFlags.NonPublic
                        | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(m => m.Name.Contains("state", StringComparison.OrdinalIgnoreCase))
            .Select(m => $"{m.MemberType} {m.Name}")
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

        Assert.True(offenders.Count == 0,
            "PartyMailingDetails must declare NO State member — a party's State/UT is the GST place-of-supply "
            + "driver and lives exactly once on PartyGstDetails.StateCode, reached through Ledger.MailingStateCode. "
            + "A second, independently-editable State would let the mailing State and the place-of-supply State "
            + "diverge and SILENTLY MIS-COMPUTE TAX (CGST+SGST vs IGST). Offending member(s): "
            + string.Join(", ", offenders));
    }

    /// <summary>
    /// The other half of the structural claim: the single source really is where the documentation says it is.
    /// If someone moves <c>StateCode</c> off <see cref="PartyGstDetails"/>, or drops
    /// <see cref="Ledger.MailingStateCode"/>, the test above would still pass vacuously — this one would not.
    /// </summary>
    [Fact]
    public void The_single_State_lives_on_PartyGstDetails_and_is_reached_through_Ledger_MailingStateCode()
    {
        var stored = typeof(PartyGstDetails).GetProperty("StateCode",
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        Assert.NotNull(stored);
        Assert.True(stored!.CanRead && stored.CanWrite, "PartyGstDetails.StateCode must be readable and writable.");

        var accessor = typeof(DomainLedger).GetProperty("MailingStateCode",
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        Assert.NotNull(accessor);
        Assert.True(accessor!.CanRead && accessor.CanWrite, "Ledger.MailingStateCode must be readable and writable.");

        // The accessor must be a pure DELEGATE, not a second field: a Ledger has no backing field for it.
        var backing = typeof(DomainLedger).GetFields(BindingFlags.Public | BindingFlags.NonPublic
                                               | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(f => f.Name.Contains("MailingState", StringComparison.OrdinalIgnoreCase))
            .Select(f => f.Name)
            .ToList();
        Assert.True(backing.Count == 0,
            "Ledger.MailingStateCode must be a pure delegating accessor over PartyGstDetails.StateCode, with no "
            + "backing field of its own — a field here IS the second storage location the invariant forbids. "
            + "Found: " + string.Join(", ", backing));
    }

    // ================================================================= (2) THE BEHAVIOUR

    /// <summary>Writing the mailing State stores it on the GST block — materialising that block on first write.</summary>
    [Fact]
    public void Writing_the_mailing_State_stores_it_on_the_party_GST_block()
    {
        var party = NewParty();
        Assert.Null(party.PartyGst);

        party.MailingStateCode = "19";

        Assert.NotNull(party.PartyGst);
        Assert.Equal("19", party.PartyGst!.StateCode);
        Assert.Equal("19", party.MailingStateCode);
    }

    /// <summary>
    /// THE DIVERGENCE-IS-IMPOSSIBLE TEST. Writing through EITHER face is immediately visible through the other,
    /// because there is only one storage location. This is what makes the wrong-tax-head scenario unreachable.
    /// </summary>
    [Fact]
    public void The_mailing_State_and_the_place_of_supply_State_cannot_diverge()
    {
        var party = NewParty();

        // Write through the mailing face → visible on the GST face.
        party.MailingStateCode = "19";                 // West Bengal
        Assert.Equal("19", party.PartyGst!.StateCode);

        // Write through the GST face → visible on the mailing face.
        party.PartyGst!.StateCode = "27";              // Maharashtra
        Assert.Equal("27", party.MailingStateCode);

        // And back again — they agree after every write, in both directions.
        party.MailingStateCode = "29";                 // Karnataka
        Assert.Equal("29", party.PartyGst!.StateCode);
        Assert.Equal(party.PartyGst!.StateCode, party.MailingStateCode);
    }

    /// <summary>Clearing the State clears the ONE stored value but keeps the rest of the GST block intact.</summary>
    [Fact]
    public void Clearing_the_mailing_State_clears_the_stored_State_without_dropping_the_GST_block()
    {
        var party = NewParty();
        party.PartyGst = new PartyGstDetails
        {
            Gstin = "19AAAAA0000A1Z" + Gstin.ComputeCheckDigit("19AAAAA0000A1Z0"),
            RegistrationType = GstRegistrationType.Regular,
            StateCode = "19",
        };

        party.MailingStateCode = null;

        Assert.NotNull(party.PartyGst);
        Assert.Null(party.PartyGst!.StateCode);
        Assert.Null(party.MailingStateCode);
        Assert.Equal(GstRegistrationType.Regular, party.PartyGst!.RegistrationType);
        Assert.NotNull(party.PartyGst!.Gstin);
    }

    /// <summary>
    /// Clearing a State that was never set must not FABRICATE a GST block — otherwise merely opening the mailing
    /// screen on an address-only party would give it a party-GST block and change its persisted and exported
    /// bytes (ER-13).
    /// </summary>
    [Fact]
    public void Clearing_an_unset_mailing_State_does_not_fabricate_a_GST_block()
    {
        var party = NewParty();

        party.MailingStateCode = null;
        Assert.Null(party.PartyGst);

        party.MailingStateCode = "   ";   // blank is a clear, not a value
        Assert.Null(party.PartyGst);
        Assert.Null(party.MailingStateCode);
    }

    /// <summary>A typed State is trimmed on the way in, so "19" and " 19 " persist as the same one value.</summary>
    [Fact]
    public void A_mailing_State_is_trimmed_on_write()
    {
        var party = NewParty();

        party.MailingStateCode = "  19  ";

        Assert.Equal("19", party.PartyGst!.StateCode);
        Assert.Equal("19", party.MailingStateCode);
    }

    /// <summary>
    /// The mailing block round-trips its own four fields without ever carrying a State — the positive companion
    /// to the reflection assertion, expressed as behaviour a reader can follow.
    /// </summary>
    [Fact]
    public void The_mailing_block_carries_its_own_fields_and_the_State_stays_on_the_GST_block()
    {
        var party = NewParty();
        party.Mailing = new PartyMailingDetails
        {
            MailingName = "Naresh Traders Pvt Ltd",
            Address = "12 Park Street\nKolkata",
            Country = "India",
            Pincode = "700019",
        };
        party.MailingStateCode = "19";

        Assert.Equal("Naresh Traders Pvt Ltd", party.Mailing!.MailingName);
        Assert.Equal("700019", party.Mailing!.Pincode);
        Assert.Equal(new[] { "12 Park Street", "Kolkata" }, party.Mailing!.AddressLines);

        // The State is NOT on the mailing block — it is on the GST block, once.
        Assert.Equal("19", party.PartyGst!.StateCode);
        Assert.Equal("19", party.MailingStateCode);
    }
}
