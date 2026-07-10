using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Domain = Apex.Ledger.Domain;

namespace Apex.Ledger.Io.Tests;

/// <summary>
/// Phase-7 slice-5 <b>Io fold-in</b> gate (PR-5 losslessness): a company with TCS enabled that posts a real §206C
/// collection line — the additive <b>tcs_line</b> child of an entry line — <b>exports and re-imports paisa + count
/// exact in JSON AND XML</b>, both byte-stable and into a fresh (differently-Guid'd) company through the
/// engine-routed CompanyImportService (re-mapping the Nature-of-Goods id and the collectee-ledger id). This is the
/// exact Phase-6 carry-forward defect class (a new child silently dropped on export/import), built in here rather
/// than deferred — the mirror of the slice-2 tds_line gate.
/// </summary>
public class CanonicalTcsLineRoundTripTests
{
    private const string ValidTan = "MUMA12345B";
    private const string BuyerPan = "AAQCS1234K";

    private static Company BuildTcsCompany()
    {
        var c = CompanyFactory.CreateSeeded("Scrap Traders", new DateOnly(2025, 4, 1), new DateOnly(2025, 4, 1));
        new TdsTcsService(c).EnableTcs(new TcsConfig { Tan = ValidTan, CollectorType = DeductorType.Company });

        var nog = c.FindNatureOfGoodsByCode("6CE")!;
        var buyer = new Domain.Ledger(Guid.NewGuid(), "Scrap Buyer", c.FindGroupByName("Sundry Debtors")!.Id, Money.Zero, true)
        {
            TcsApplicable = true, TcsNatureOfGoodsId = nog.Id, CollecteeType = CollecteeType.Individual, PartyPan = BuyerPan,
        };
        c.AddLedger(buyer);

        // Post a §206C collection line (scrap ₹1,00,000 + 18% GST ₹18,000 → TCS 1% of ₹1,18,000 = ₹1,180). A balanced
        // journal that rides the TCS Payable credit line, so the tcs_line rides the JSON/XML round-trip.
        var col = new TcsService(c).BuildCollection(Money.FromRupees(1_00_000m), Money.FromRupees(18_000m), nog, buyer, new DateOnly(2025, 5, 10));
        new LedgerService(c).Post(new Voucher(Guid.NewGuid(),
            c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Journal).Id, new DateOnly(2025, 5, 10),
            new[] { new Domain.EntryLine(buyer.Id, col.TcsAmount, DrCr.Debit), col.TcsPayableLine! }));

        return c;
    }

    private static Company FreshTarget() =>
        CompanyFactory.CreateSeeded("Fresh Import Co", new DateOnly(2025, 4, 1), new DateOnly(2025, 4, 1));

    [Fact]
    public void Json_round_trips_byte_stable_with_the_tcs_line()
    {
        var c = BuildTcsCompany();
        var first = CanonicalJson.Export(c);
        var (model, errors) = CanonicalJson.Parse(first);
        Assert.Empty(errors);
        Assert.Equal(first, CanonicalJson.Export(model!)); // byte-for-byte
    }

    [Fact]
    public void Xml_round_trips_byte_stable_with_the_tcs_line()
    {
        var c = BuildTcsCompany();
        var first = CanonicalXml.Export(c);
        var (model, errors) = CanonicalXml.Parse(first);
        Assert.Empty(errors);
        Assert.Equal(first, CanonicalXml.Export(model!));
    }

    [Fact]
    public void Json_and_xml_carry_identical_tcs_payload()
    {
        var c = BuildTcsCompany();
        var (jm, je) = CanonicalJson.Parse(CanonicalJson.Export(c));
        var (xm, xe) = CanonicalXml.Parse(CanonicalXml.Export(c));
        Assert.Empty(je);
        Assert.Empty(xe);
        Assert.Equal(CanonicalJson.Export(jm!), CanonicalJson.Export(xm!));
    }

    [Fact]
    public void Tcs_line_import_into_fresh_company_reconciles_json_and_xml_paisa_and_count_exact()
    {
        var source = BuildTcsCompany();

        foreach (var bytes in new[] { CanonicalJson.Export(source), CanonicalXml.Export(source) })
        {
            var (model, errors) = bytes[0] == (byte)'{' ? CanonicalJson.Parse(bytes) : CanonicalXml.Parse(bytes);
            Assert.Empty(errors);

            var fresh = FreshTarget();
            var result = new CompanyImportService(fresh).Apply(model!);
            Assert.True(result.Applied, string.Join("; ", result.Errors));

            var tgtBuyer = fresh.FindLedgerByName("Scrap Buyer")!;
            var tgtNature = fresh.FindNatureOfGoodsByCode("6CE")!;
            var tcsLines = fresh.Vouchers.SelectMany(v => v.Lines).Where(l => l.HasTcs).Select(l => l.Tcs!).ToList();
            var t = Assert.Single(tcsLines);
            Assert.Equal("6CE", t.CollectionCode);
            Assert.Equal(Money.FromRupees(1_18_000m), t.AssessableValue); // GST-inclusive base
            Assert.Equal(100, t.RateBasisPoints);
            Assert.Equal(Money.FromRupees(1_180m), t.TcsAmount);
            Assert.True(t.PanApplied);
            Assert.Equal(tgtNature.Id, t.NatureId);        // re-mapped to the target nature
            Assert.Equal(tgtBuyer.Id, t.CollecteeLedgerId); // re-mapped to the target ledger
        }
    }
}
