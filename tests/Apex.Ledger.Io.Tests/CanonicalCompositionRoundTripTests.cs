using System.Text;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;

namespace Apex.Ledger.Io.Tests;

/// <summary>
/// Phase-9 slice-3 <b>Composition Io fold-in</b> gate (RQ-4; RQ-23; ER-13): a composition company — sub-type + opt-in
/// date + a posted Bill-of-Supply sale (no tax) — exports and re-imports exact in JSON AND XML, both byte-stable and
/// into a fresh (differently-Guid'd) company through the engine-routed <see cref="CompanyImportService"/>
/// (all-or-nothing). A malformed composition config (sub-type missing, or Composition without a GSTIN) rejects
/// wholesale (<c>GstConfig.EnsureValid</c> in pre-flight), leaving the target untouched. A Regular company serialises
/// with the two composition members inert (null/omitted) — byte-stable, no "Tally".
/// </summary>
public sealed class CanonicalCompositionRoundTripTests
{
    private const string GstinMaharashtra = "27AAPFU0939F1ZV";
    private static readonly DateOnly FyStart = new(2025, 4, 1);
    private static readonly DateOnly D1 = new(2025, 4, 10);

    private static Company BuildCompositionCompany()
    {
        var c = CompanyFactory.CreateSeeded("Composition Traders", FyStart);
        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = "27", Gstin = GstinMaharashtra, RegistrationType = GstRegistrationType.Composition,
            CompositionSubType = CompositionSubType.Trader, CompositionOptInDate = new DateOnly(2025, 4, 1),
            ApplicableFrom = FyStart, Periodicity = GstReturnPeriodicity.Quarterly,
        });

        // A Bill-of-Supply sale: party Dr = supply value, sales Cr = supply value, no tax leg.
        var sales = new Domain.Ledger(Guid.NewGuid(), "Sales", c.FindGroupByName("Sales Accounts")!.Id, Money.Zero, false);
        c.AddLedger(sales);
        var party = new Domain.Ledger(Guid.NewGuid(), "Walk-in", c.FindGroupByName("Sundry Debtors")!.Id, Money.Zero, true);
        c.AddLedger(party);
        new LedgerService(c).Post(new Voucher(Guid.NewGuid(), c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id, D1, new[]
        {
            new EntryLine(party.Id, Money.FromRupees(1000m), DrCr.Debit),
            new EntryLine(sales.Id, Money.FromRupees(1000m), DrCr.Credit),
        }, partyId: party.Id));
        return c;
    }

    private static Company Fresh() => CompanyFactory.CreateSeeded("Fresh Composition Co", FyStart);

    [Fact]
    public void Json_round_trips_byte_stable()
    {
        var c = BuildCompositionCompany();
        var first = CanonicalJson.Export(c);
        var (model, errors) = CanonicalJson.Parse(first);
        Assert.Empty(errors);
        Assert.Equal(first, CanonicalJson.Export(model!));
        Assert.DoesNotContain("Tally", Encoding.UTF8.GetString(first), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Xml_round_trips_byte_stable()
    {
        var c = BuildCompositionCompany();
        var first = CanonicalXml.Export(c);
        var (model, errors) = CanonicalXml.Parse(first);
        Assert.Empty(errors);
        Assert.Equal(first, CanonicalXml.Export(model!));
    }

    [Fact]
    public void Json_and_xml_carry_identical_payload()
    {
        var c = BuildCompositionCompany();
        var (jm, je) = CanonicalJson.Parse(CanonicalJson.Export(c));
        var (xm, xe) = CanonicalXml.Parse(CanonicalXml.Export(c));
        Assert.Empty(je);
        Assert.Empty(xe);
        Assert.Equal(CanonicalJson.Export(jm!), CanonicalJson.Export(xm!));
    }

    [Fact]
    public void Export_import_into_fresh_company_reconciles_and_issues_a_bill_of_supply()
    {
        var source = BuildCompositionCompany();

        foreach (var bytes in new[] { CanonicalJson.Export(source), CanonicalXml.Export(source) })
        {
            var (model, errors) = bytes[0] == (byte)'{' ? CanonicalJson.Parse(bytes) : CanonicalXml.Parse(bytes);
            Assert.Empty(errors);

            var fresh = Fresh();
            var result = new CompanyImportService(fresh).Apply(model!);
            Assert.True(result.Applied, string.Join("; ", result.Errors));

            Assert.Equal(GstRegistrationType.Composition, fresh.Gst!.RegistrationType);
            Assert.Equal(CompositionSubType.Trader, fresh.Gst!.CompositionSubType);
            Assert.Equal(new DateOnly(2025, 4, 1), fresh.Gst!.CompositionOptInDate);

            // No GST tax ledgers (EnableGst gated them off); the sale posted no tax leg.
            Assert.DoesNotContain(fresh.Ledgers, l => l.GstClassification is not null);
            var v = fresh.Vouchers.Single(x => fresh.FindVoucherType(x.TypeId)!.BaseType == VoucherBaseType.Sales);
            Assert.DoesNotContain(v.Lines, l => l.HasGst);
        }
    }

    [Fact]
    public void Import_rejects_a_composition_config_missing_its_sub_type()
    {
        var model = CanonicalJson.Parse(CanonicalJson.Export(BuildCompositionCompany())).Model!;
        // Corrupt the model: a Composition config with NO sub-type must be rejected in pre-flight (EnsureValid).
        var bad = model with { Company = model.Company with { Gst = model.Company.Gst! with { CompositionSubType = null } } };

        var fresh = Fresh();
        var result = new CompanyImportService(fresh).Apply(bad);
        Assert.False(result.Applied);
        Assert.Null(fresh.Gst); // target byte-unchanged (all-or-nothing)
    }

    [Fact]
    public void Import_rejects_a_composition_config_missing_its_gstin()
    {
        var model = CanonicalJson.Parse(CanonicalJson.Export(BuildCompositionCompany())).Model!;
        var bad = model with { Company = model.Company with { Gst = model.Company.Gst! with { Gstin = null } } };

        var fresh = Fresh();
        var result = new CompanyImportService(fresh).Apply(bad);
        Assert.False(result.Applied);
        Assert.Null(fresh.Gst);
    }

    [Fact]
    public void Regular_company_serialises_with_the_composition_members_inert()
    {
        // ER-13: a Regular company exports with compositionSubType/compositionOptInDate null (JSON) / omitted (XML),
        // byte-stable, and reconstructs with a null composition config.
        var c = CompanyFactory.CreateSeeded("Regular GST Co", FyStart);
        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = "27", Gstin = GstinMaharashtra, RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart, Periodicity = GstReturnPeriodicity.Monthly,
        });

        var json = CanonicalJson.Export(c);
        var (jm, je) = CanonicalJson.Parse(json);
        Assert.Empty(je);
        Assert.Equal(json, CanonicalJson.Export(jm!));
        Assert.Null(jm!.Company.Gst!.CompositionSubType);
        Assert.Null(jm.Company.Gst!.CompositionOptInDate);

        // XML omits the two attributes entirely (Opt is null-omitting).
        var xml = Encoding.UTF8.GetString(CanonicalXml.Export(c));
        Assert.DoesNotContain("compositionSubType", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("compositionOptInDate", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("Tally", Encoding.UTF8.GetString(json), StringComparison.OrdinalIgnoreCase);
    }
}
