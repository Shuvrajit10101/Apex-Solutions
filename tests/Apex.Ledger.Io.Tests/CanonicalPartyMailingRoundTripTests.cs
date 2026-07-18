using System.Text;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;

namespace Apex.Ledger.Io.Tests;

/// <summary>
/// WI-4 (schema v45) <b>party Mailing Details Io fold-in</b> gate. A party ledger carrying Mailing Name / Address /
/// Country / PIN exports and re-imports exact in JSON <i>and</i> XML, byte-stably, and into a fresh
/// (differently-Guid'd) company through the engine-routed <see cref="CompanyImportService"/>.
///
/// <para><b>ER-13 is the sharp edge here.</b> The canonical JSON writer is configured
/// <c>DefaultIgnoreCondition = Never</c>, so a naively-added <c>Mailing</c> property would emit
/// <c>"mailing": null</c> on <i>every ledger of every existing company</i> and change the bytes of exports that
/// have nothing to do with this feature. <see cref="Er13_a_company_with_no_mailing_details_exports_byte_identically"/>
/// pins that: the JSON and XML of a company without mailing details must contain no trace of the block at all.</para>
///
/// <para>The <b>State</b> is deliberately absent from the mailing block — it rides on <c>partyGst/@stateCode</c>,
/// the single stored value that drives GST place of supply, and
/// <see cref="The_party_State_travels_once_on_the_gst_block_not_twice"/> proves the document cannot carry two
/// contradicting States.</para>
/// </summary>
public sealed class CanonicalPartyMailingRoundTripTests
{
    private static readonly DateOnly FyStart = new(2025, 4, 1);

    private const string Address = "12 Park Street\nBallygunge\nKolkata";

    private static Company BuildCompanyWithPartyAddress()
    {
        var c = CompanyFactory.CreateSeeded("Mailing Traders", FyStart);
        var party = new Domain.Ledger(
            Guid.NewGuid(), "Naresh Traders", c.FindGroupByName("Sundry Debtors")!.Id, Money.Zero, true);
        party.Mailing = new PartyMailingDetails
        {
            MailingName = "Naresh Traders Pvt Ltd",
            Address = Address,
            Country = "India",
            Pincode = "700019",
        };
        party.MailingStateCode = "19";   // West Bengal — the ONE stored State
        c.AddLedger(party);
        return c;
    }

    private static Company Fresh() => CompanyFactory.CreateSeeded("Fresh Mailing Co", FyStart);

    private static Domain.Ledger PartyIn(Company c) => c.FindLedgerByName("Naresh Traders")!;

    // ================================================================= lossless round-trip

    [Fact]
    public void Json_round_trips_byte_stable_and_lossless()
    {
        var c = BuildCompanyWithPartyAddress();
        var first = CanonicalJson.Export(c);

        var (model, errors) = CanonicalJson.Parse(first);
        Assert.Empty(errors);
        Assert.NotNull(model);
        Assert.Equal(first, CanonicalJson.Export(model!));   // byte-stable

        var target = Fresh();
        Assert.True(new CompanyImportService(target).Apply(model!, DuplicatePolicy.Skip).Applied);
        AssertMailingSurvived(PartyIn(target));

        Assert.DoesNotContain("Tally", Encoding.UTF8.GetString(first), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Xml_round_trips_byte_stable_and_lossless()
    {
        var c = BuildCompanyWithPartyAddress();
        var first = CanonicalXml.Export(c);

        var (model, errors) = CanonicalXml.Parse(first);
        Assert.Empty(errors);
        Assert.NotNull(model);
        Assert.Equal(first, CanonicalXml.Export(model!));     // byte-stable

        var target = Fresh();
        Assert.True(new CompanyImportService(target).Apply(model!, DuplicatePolicy.Skip).Applied);
        AssertMailingSurvived(PartyIn(target));
    }

    [Fact]
    public void Json_and_Xml_carry_the_identical_payload()
    {
        // The two writers must agree — a fold-in that reached one serialiser and not the other would make an
        // export's fidelity depend on which button the user pressed.
        var c = BuildCompanyWithPartyAddress();

        var (jsonModel, jsonErrors) = CanonicalJson.Parse(CanonicalJson.Export(c));
        var (xmlModel, xmlErrors) = CanonicalXml.Parse(CanonicalXml.Export(c));
        Assert.Empty(jsonErrors);
        Assert.Empty(xmlErrors);

        var fromJson = Fresh();
        var fromXml = Fresh();
        Assert.True(new CompanyImportService(fromJson).Apply(jsonModel!, DuplicatePolicy.Skip).Applied);
        Assert.True(new CompanyImportService(fromXml).Apply(xmlModel!, DuplicatePolicy.Skip).Applied);

        AssertMailingSurvived(PartyIn(fromJson));
        AssertMailingSurvived(PartyIn(fromXml));
    }

    private static void AssertMailingSurvived(Domain.Ledger party)
    {
        Assert.NotNull(party.Mailing);
        Assert.Equal("Naresh Traders Pvt Ltd", party.Mailing!.MailingName);
        Assert.Equal(Address, party.Mailing!.Address);
        Assert.Equal("India", party.Mailing!.Country);
        Assert.Equal("700019", party.Mailing!.Pincode);
        // Each address line survives individually — this is what actually prints on the invoice.
        Assert.Equal(new[] { "12 Park Street", "Ballygunge", "Kolkata" }, party.Mailing!.AddressLines);
        // And the single State came across on the GST block.
        Assert.Equal("19", party.MailingStateCode);
        Assert.Equal("19", party.PartyGst!.StateCode);
    }

    // ================================================================= ER-13

    [Fact]
    public void Er13_a_company_with_no_mailing_details_exports_byte_identically()
    {
        // A company whose parties carry no mailing details must serialise EXACTLY as it did before v45 — no
        // "mailing": null key in JSON, no <mailing/> element in XML. Without the WhenWritingNull attribute on
        // LedgerDto.Mailing, the JSON assertion below fails on every ledger of every existing company.
        var c = CompanyFactory.CreateSeeded("No Address Co", FyStart);
        var party = new Domain.Ledger(
            Guid.NewGuid(), "Plain Party", c.FindGroupByName("Sundry Debtors")!.Id, Money.Zero, true);
        c.AddLedger(party);
        Assert.Null(party.Mailing);

        var json = Encoding.UTF8.GetString(CanonicalJson.Export(c));
        var xml = Encoding.UTF8.GetString(CanonicalXml.Export(c));

        // The LedgerDto member serialises as the key "mailing" (the COMPANY's long-standing "mailingName" is a
        // different, pre-existing key — hence the exact-token match rather than a substring sweep).
        Assert.DoesNotContain("\"mailing\":", json, StringComparison.Ordinal);
        Assert.DoesNotContain("<mailing", xml, StringComparison.Ordinal);

        // Belt-and-braces: attaching a block and re-exporting DOES change the bytes, so the assertion above is
        // detecting a real absence rather than passing because the writer never emits the block at all.
        party.Mailing = new PartyMailingDetails { Address = "1 High St" };
        var withBlock = Encoding.UTF8.GetString(CanonicalJson.Export(c));
        Assert.Contains("\"mailing\":", withBlock, StringComparison.Ordinal);
        Assert.NotEqual(json, withBlock);
    }

    [Fact]
    public void Er13_an_all_blank_mailing_block_imports_as_no_block_at_all()
    {
        // A document carrying an empty block must not materialise one — otherwise a round-trip through a form
        // that merely displayed the section would start emitting bytes for it.
        var c = CompanyFactory.CreateSeeded("Blank Block Co", FyStart);
        var party = new Domain.Ledger(
            Guid.NewGuid(), "Blank Party", c.FindGroupByName("Sundry Debtors")!.Id, Money.Zero, true);
        party.Mailing = new PartyMailingDetails { MailingName = "   ", Address = null };
        c.AddLedger(party);

        var (model, errors) = CanonicalJson.Parse(CanonicalJson.Export(c));
        Assert.Empty(errors);

        var target = Fresh();
        Assert.True(new CompanyImportService(target).Apply(model!, DuplicatePolicy.Skip).Applied);
        Assert.Null(target.FindLedgerByName("Blank Party")!.Mailing);
    }

    // ================================================================= the single-State invariant on the wire

    [Fact]
    public void The_party_State_travels_once_on_the_gst_block_not_twice()
    {
        var c = BuildCompanyWithPartyAddress();
        var xml = Encoding.UTF8.GetString(CanonicalXml.Export(c));

        // The mailing element exists and carries the address fields …
        Assert.Contains("<mailing", xml, StringComparison.Ordinal);
        Assert.Contains("pincode=\"700019\"", xml, StringComparison.Ordinal);
        // … but no State of its own. The only State on the wire is the GST place-of-supply code, so an exported
        // document cannot describe a party whose mailing State disagrees with its tax State.
        var mailingElement = xml[xml.IndexOf("<mailing", StringComparison.Ordinal)..];
        mailingElement = mailingElement[..mailingElement.IndexOf('>')];
        Assert.DoesNotContain("state", mailingElement, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("stateCode=\"19\"", xml, StringComparison.Ordinal);
    }

    // ================================================================= validation at the import boundary

    [Fact]
    public void An_invalid_PIN_rejects_the_whole_import_leaving_the_target_untouched()
    {
        var c = BuildCompanyWithPartyAddress();
        var (model, errors) = CanonicalJson.Parse(CanonicalJson.Export(c));
        Assert.Empty(errors);

        // Corrupt the PIN on the wire (a hand-edited or third-party document).
        var ledgers = model!.Payload.Ledgers
            .Select(l => l.Mailing is null ? l : l with { Mailing = l.Mailing with { Pincode = "12" } })
            .ToList();
        var tampered = model with { Payload = model.Payload with { Ledgers = ledgers } };

        var target = Fresh();
        var ledgersBefore = target.Ledgers.Count;
        var result = new CompanyImportService(target).Apply(tampered, DuplicatePolicy.Skip);

        Assert.False(result.Applied);
        Assert.Equal(ledgersBefore, target.Ledgers.Count);   // all-or-nothing: nothing landed
    }
}
