using System.Text;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;

namespace Apex.Ledger.Io.Tests;

/// <summary>
/// Phase-9 slice-1 <b>GST 2.0 rate-history + Compensation-Cess Io fold-in</b> gate (RQ-1/RQ-2; RQ-23; ER-13): an
/// advanced-GST company — dated rate history + three cess windows + a cess-bearing RSP item — <b>exports and
/// re-imports exact in JSON AND XML</b>, both byte-stable and into a fresh (differently-Guid'd) company through the
/// engine-routed <see cref="CompanyImportService"/> (all-or-nothing). A malformed cess row rejects the whole batch. A
/// plain GST company (no advanced data) serialises with empty <c>rateHistory</c>/<c>cessRates</c> and inert item cess
/// fields — byte-identical shape to a Phase-8 company (ER-13). De-brand clean (no "Tally").
/// </summary>
public sealed class CanonicalGstAdvancedRoundTripTests
{
    private const string GstinMaharashtra = "27AAPFU0939F1ZV";

    private static Company BuildAdvancedCompany()
    {
        var c = CompanyFactory.CreateSeeded("GST 2.0 Traders", new DateOnly(2025, 4, 1));
        var gst = new GstService(c);
        gst.EnableGst(new GstConfig
        {
            HomeStateCode = "27", Gstin = GstinMaharashtra, RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = new DateOnly(2025, 4, 1), Periodicity = GstReturnPeriodicity.Monthly,
        });
        gst.SeedAdvancedGst();

        var inv = new InventoryService(c);
        var pan = inv.CreateStockItem("Pan Masala", inv.CreateStockGroup("Tobacco").Id, inv.CreateSimpleUnit("Pkt", "Packets").Id);
        pan.Gst = new StockItemGstDetails
        {
            HsnSac = "21069020", Taxability = GstTaxability.Taxable, RateBasisPoints = 2800,
            ValuationBasis = GstValuationBasis.RetailSalePrice, CessApplicable = true,
            CessValuationMode = CessValuationMode.RetailSalePriceFactor, CessRspFactorMillis = 320,
            RetailSalePrice = new Money(100m),
        };
        return c;
    }

    private static Company Fresh() => CompanyFactory.CreateSeeded("Fresh GST2 Co", new DateOnly(2025, 4, 1));

    [Fact]
    public void Json_round_trips_byte_stable()
    {
        var c = BuildAdvancedCompany();
        var first = CanonicalJson.Export(c);
        var (model, errors) = CanonicalJson.Parse(first);
        Assert.Empty(errors);
        Assert.Equal(first, CanonicalJson.Export(model!));
        Assert.DoesNotContain("Tally", Encoding.UTF8.GetString(first), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Xml_round_trips_byte_stable()
    {
        var c = BuildAdvancedCompany();
        var first = CanonicalXml.Export(c);
        var (model, errors) = CanonicalXml.Parse(first);
        Assert.Empty(errors);
        Assert.Equal(first, CanonicalXml.Export(model!));
    }

    [Fact]
    public void Json_and_xml_carry_identical_payload()
    {
        var c = BuildAdvancedCompany();
        var (jm, je) = CanonicalJson.Parse(CanonicalJson.Export(c));
        var (xm, xe) = CanonicalXml.Parse(CanonicalXml.Export(c));
        Assert.Empty(je);
        Assert.Empty(xe);
        Assert.Equal(CanonicalJson.Export(jm!), CanonicalJson.Export(xm!));
    }

    [Fact]
    public void Export_import_into_fresh_company_reconciles_json_and_xml()
    {
        var source = BuildAdvancedCompany();
        var expectedHistory = source.Gst!.RateHistory.Count;
        var expectedCess = source.Gst!.CessRates.Count;

        foreach (var bytes in new[] { CanonicalJson.Export(source), CanonicalXml.Export(source) })
        {
            var (model, errors) = bytes[0] == (byte)'{' ? CanonicalJson.Parse(bytes) : CanonicalXml.Parse(bytes);
            Assert.Empty(errors);

            var fresh = Fresh();
            var result = new CompanyImportService(fresh).Apply(model!);
            Assert.True(result.Applied, string.Join("; ", result.Errors));

            Assert.Equal(expectedHistory, fresh.Gst!.RateHistory.Count);
            Assert.Equal(expectedCess, fresh.Gst!.CessRates.Count);
            Assert.Contains(fresh.Gst!.RateHistory, h => h.HsnSac == "8703" && h.RateBasisPoints == 4000
                && h.EffectiveFrom == new DateOnly(2025, 9, 22) && h.EffectiveTo is null);
            Assert.Contains(fresh.Gst!.CessRates, r => r.HsnSac == "2701"
                && r.ValuationMode == CessValuationMode.Specific && r.CessPerUnit == new Money(400m));

            var pan = fresh.FindStockItemByName("Pan Masala")!;
            Assert.Equal(GstValuationBasis.RetailSalePrice, pan.Gst!.ValuationBasis);
            Assert.True(pan.Gst.CessApplicable);
            Assert.Equal(CessValuationMode.RetailSalePriceFactor, pan.Gst.CessValuationMode);
            Assert.Equal(320, pan.Gst.CessRspFactorMillis);
            Assert.Equal(new Money(100m), pan.Gst.RetailSalePrice);
        }
    }

    [Fact]
    public void Import_rejects_a_cess_item_with_rsp_factor_mode_and_no_rsp()
    {
        var source = BuildAdvancedCompany();
        // Corrupt directly (bypass the service): RSP-factor cess with no declared RSP is invalid.
        source.FindStockItemByName("Pan Masala")!.Gst!.RetailSalePrice = null;

        var (model, errors) = CanonicalJson.Parse(CanonicalJson.Export(source));
        Assert.Empty(errors);

        var fresh = Fresh();
        var result = new CompanyImportService(fresh).Apply(model!);

        Assert.False(result.Applied);
        Assert.Contains(result.Errors, e => e.Contains("RSP", StringComparison.OrdinalIgnoreCase)
            || e.Contains("Retail Sale Price", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Plain_gst_company_serialises_with_empty_advanced_gst_fields()
    {
        // ER-13: an advanced-GST-off company exports rateHistory/cessRates empty, item cess inert.
        var c = CompanyFactory.CreateSeeded("Plain GST Co", new DateOnly(2025, 4, 1));
        var gst = new GstService(c);
        gst.EnableGst(new GstConfig
        {
            HomeStateCode = "27", Gstin = GstinMaharashtra, RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = new DateOnly(2025, 4, 1), Periodicity = GstReturnPeriodicity.Monthly,
        });
        var inv = new InventoryService(c);
        var w = inv.CreateStockItem("Widget", inv.CreateStockGroup("Goods").Id, inv.CreateSimpleUnit("Nos", "Numbers").Id);
        w.Gst = new StockItemGstDetails { HsnSac = "1234", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800 };

        var (model, errors) = CanonicalJson.Parse(CanonicalJson.Export(c));
        Assert.Empty(errors);
        Assert.Empty(model!.Company.Gst!.RateHistory);
        Assert.Empty(model!.Company.Gst!.CessRates);

        var item = model.Payload.StockItems.Single(i => i.Name == "Widget");
        Assert.False(item.Gst!.CessApplicable);
        Assert.Equal("TransactionValue", item.Gst!.ValuationBasis);
        Assert.Null(item.Gst!.CessValuationMode);
        Assert.Null(item.Gst!.RspPaisa);
    }

    [Fact]
    public void A_non_gst_company_carries_no_advanced_gst_and_serialises_de_branded()
    {
        // Robert-shaped: accounts-only, no GST at all.
        var c = CompanyFactory.CreateSeeded("Robert Transport", new DateOnly(2025, 4, 1));
        var (model, errors) = CanonicalJson.Parse(CanonicalJson.Export(c));
        Assert.Empty(errors);
        Assert.Null(model!.Company.Gst);
        Assert.DoesNotContain("Tally", Encoding.UTF8.GetString(CanonicalJson.Export(c)), StringComparison.OrdinalIgnoreCase);
    }
}
