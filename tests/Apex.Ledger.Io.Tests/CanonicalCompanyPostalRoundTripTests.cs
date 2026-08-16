using System.Text;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;

namespace Apex.Ledger.Io.Tests;

/// <summary>
/// <b>The company postal block on the wire</b> — <c>Address</c>, <c>Country</c>, <c>State</c> and <c>Pin</c>.
///
/// <para><b>Why this file exists.</b> The W0-2 grounding document, and the R12 user gate in <c>plan.md</c> that
/// rests on it, described <c>companies.state</c> as a column that "goes nowhere" — a dormant duplicate of the GST
/// home State that no code reads. That is true of the <b>print</b> path and false of the product: the canonical
/// export/import round-trip has always carried <c>state</c> and <c>pin</c>, so every book imported from canonical
/// XML holds real values in them. The distinction decides whether "suppress the postal one" is a free column drop
/// or a silent data loss, so it is pinned here rather than left as prose.</para>
///
/// <para><c>CanonicalRoundTripTests</c> already asserts <c>State</c> survives export; <c>Pin</c> was set by the
/// fixtures and asserted by nothing. Both are asserted end-to-end below, through the engine-routed
/// <see cref="CompanyImportService"/> into a fresh, differently-Guid'd company.</para>
/// </summary>
public sealed class CanonicalCompanyPostalRoundTripTests
{
    private static readonly DateOnly FyStart = new(2025, 4, 1);

    // Odd, non-default values throughout: a State that is NOT the GST home State, and a PIN that belongs to
    // neither. Defaults would let a mapper that dropped the field pass by accident.
    private const string PostalAddress = "37B Kalyani Nagar\nYerawada";
    private const string PostalState = "Kerala";     // GST home State below is Maharashtra (27)
    private const string PostalPin = "411037";

    private static Company BuildCompanyWithPostalBlock()
    {
        var c = CompanyFactory.CreateSeeded("Postal Traders", FyStart);
        c.MailingName = "Postal Traders Pvt Ltd";
        c.Address = PostalAddress;
        c.Country = "India";
        c.State = PostalState;
        c.Pin = PostalPin;

        new GstService(c).EnableGst(new GstConfig
        {
            Gstin = "27AAPFU0939F1ZV",
            HomeStateCode = "27",
            RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart,
            Periodicity = GstReturnPeriodicity.Monthly,
        });

        return c;
    }

    private static Company Fresh() => CompanyFactory.CreateSeeded("Fresh Postal Co", FyStart);

    /// <summary>
    /// The postal block survives export → parse → apply into a different company, byte-for-byte. This is the test
    /// that makes "companies.state is dormant" false: a consolidating migration that dropped or merged the column
    /// would have to make this test's expectations wrong on purpose.
    /// </summary>
    [Fact]
    public void The_company_postal_block_round_trips_lossless_including_State_and_Pin()
    {
        var source = BuildCompanyWithPostalBlock();

        var (model, errors) = CanonicalJson.Parse(CanonicalJson.Export(source));
        Assert.Empty(errors);
        Assert.NotNull(model);

        // On the wire.
        Assert.Equal(PostalAddress, model!.Company.Address);
        Assert.Equal(PostalState, model.Company.State);
        Assert.Equal(PostalPin, model.Company.Pin);

        // And into a fresh company through the engine-routed importer.
        var target = Fresh();
        var result = new CompanyImportService(target).Apply(model, DuplicatePolicy.Skip);
        Assert.True(result.Applied);

        Assert.Equal(PostalAddress, target.Address);
        Assert.Equal("India", target.Country);
        Assert.Equal(PostalState, target.State);
        Assert.Equal(PostalPin, target.Pin);

        // The postal State is NOT the GST State, and importing did not conflate them.
        Assert.Equal("27", target.Gst!.HomeStateCode);
        Assert.NotEqual(target.State, IndianState.FromCode(target.Gst.HomeStateCode)!.Name);
    }

    /// <summary>
    /// The XML carries the company postal attributes explicitly. Asserted on the serialized text so that dropping
    /// an attribute from the writer is caught here even if a matching reader change hid it from the round-trip.
    /// </summary>
    [Fact]
    public void The_company_element_carries_state_and_pin_attributes_on_the_wire()
    {
        var xml = Encoding.UTF8.GetString(CanonicalXml.Export(BuildCompanyWithPostalBlock()));

        Assert.Contains($"state=\"{PostalState}\"", xml, StringComparison.Ordinal);
        Assert.Contains($"pin=\"{PostalPin}\"", xml, StringComparison.Ordinal);
    }

    /// <summary>
    /// The import boundary applies the company PIN floor, exactly as it has applied the party one since v45. A
    /// hand-edited or third-party document carrying a malformed company PIN is refused whole, leaving the target
    /// untouched — it does not land and then print "PIN: abcdef" on a tax invoice.
    /// </summary>
    [Fact]
    public void An_invalid_company_PIN_rejects_the_whole_import_leaving_the_target_untouched()
    {
        var (model, errors) = CanonicalJson.Parse(CanonicalJson.Export(BuildCompanyWithPostalBlock()));
        Assert.Empty(errors);

        var tampered = model! with { Company = model.Company with { Pin = "abcdef" } };

        var target = Fresh();
        var nameBefore = target.Name;
        var pinBefore = target.Pin;

        var result = new CompanyImportService(target).Apply(tampered, DuplicatePolicy.Skip);

        Assert.False(result.Applied);
        Assert.Equal(nameBefore, target.Name);   // all-or-nothing: the header did not land either
        Assert.Equal(pinBefore, target.Pin);
    }
}
