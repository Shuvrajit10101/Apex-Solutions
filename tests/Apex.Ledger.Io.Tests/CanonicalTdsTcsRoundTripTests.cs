using System.Text;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Domain = Apex.Ledger.Domain;

namespace Apex.Ledger.Io.Tests;

/// <summary>
/// Phase-7 slice-1 <b>Io fold-in</b> gate (PR-4 losslessness): a company with TDS + TCS enabled — the shared
/// deductor config, the seeded Nature-of-Payment / Nature-of-Goods masters, the auto-created payable ledgers, a
/// TDS-applicable party ledger (with PAN + deductee type + a default nature), a TCS-applicable buyer, and a stock
/// item carrying a §206C nature — <b>exports and re-imports paisa + count exact in JSON AND XML</b>, both
/// byte-stable and into a fresh (differently-Guid'd) company through the engine-routed CompanyImportService.
/// This is the exact Phase-6 carry-forward defect class (new masters silently dropped on export/import), built in
/// here rather than deferred.
/// </summary>
public class CanonicalTdsTcsRoundTripTests
{
    private const string ValidTan = "MUMA12345B";
    private const string ValidPan = "AAPFU0939F";
    private const string BuyerPan = "AAQCS1234K";

    private static Company BuildTdsTcsCompany()
    {
        var c = CompanyFactory.CreateSeeded("Withholding Traders", new DateOnly(2025, 4, 1), new DateOnly(2025, 4, 1));
        var svc = new TdsTcsService(c);
        svc.EnableTds(new TdsConfig
        {
            Tan = ValidTan, DeductorType = DeductorType.Company,
            ResponsiblePersonName = "A. Sharma", ResponsiblePersonPan = ValidPan,
            ResponsiblePersonDesignation = "Director", ResponsiblePersonAddress = "12 MG Road",
            SurchargeApplicable = true, CessApplicable = true, ApplicableFrom = new DateOnly(2025, 4, 1),
        });
        svc.EnableTcs(new TcsConfig { Tan = ValidTan, CollectorType = DeductorType.Company });

        var nop = c.FindNatureOfPaymentByCode("194J(b)")!;
        var nog = c.FindNatureOfGoodsByCode("6CE")!;

        c.AddLedger(new Domain.Ledger(Guid.NewGuid(), "Consultant", c.FindGroupByName("Sundry Creditors")!.Id, Money.Zero, false)
        {
            TdsApplicable = true, TdsNatureOfPaymentId = nop.Id, DeducteeType = DeducteeType.Firm,
            PartyPan = ValidPan, DeductTdsInSameVoucher = true,
        });
        c.AddLedger(new Domain.Ledger(Guid.NewGuid(), "Scrap Buyer", c.FindGroupByName("Sundry Debtors")!.Id, Money.Zero, true)
        {
            TcsApplicable = true, TcsNatureOfGoodsId = nog.Id, CollecteeType = CollecteeType.Individual, PartyPan = BuyerPan,
        });

        var inv = new InventoryService(c);
        var scrap = inv.CreateStockItem("Scrap Metal", inv.CreateStockGroup("Waste").Id, inv.CreateSimpleUnit("Kg", "Kilogram").Id);
        scrap.TcsNatureOfGoodsId = nog.Id;

        // Phase 7 slice 2: post a real 194J withholding voucher so a tds_line rides the export/import (the exact
        // Phase-6 carry-forward defect class — a new child silently dropped on JSON/XML round-trip).
        var fees = new Domain.Ledger(Guid.NewGuid(), "Professional Fees", c.FindGroupByName("Indirect Expenses")!.Id, Money.Zero, true);
        c.AddLedger(fees);
        var vendor = c.FindLedgerByName("Consultant")!;
        var carve = new TdsService(c).BuildCarveOut(Money.FromRupees(1_00_000m), Money.FromRupees(1_00_000m), nop, vendor, new DateOnly(2025, 5, 10));
        new LedgerService(c).Post(new Voucher(Guid.NewGuid(),
            c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Journal).Id, new DateOnly(2025, 5, 10),
            new[] { new Domain.EntryLine(fees.Id, Money.FromRupees(1_00_000m), DrCr.Debit), carve.PartyLine, carve.TdsPayableLine! }));

        return c;
    }

    private static Company FreshTarget() =>
        CompanyFactory.CreateSeeded("Fresh Import Co", new DateOnly(2025, 4, 1), new DateOnly(2025, 4, 1));

    [Fact]
    public void Json_round_trips_byte_stable_and_carries_v25_schema()
    {
        var c = BuildTdsTcsCompany();
        var first = CanonicalJson.Export(c);
        Assert.Contains($"\"schemaVersion\": {CanonicalMapper.SchemaVersion}", Encoding.UTF8.GetString(first));

        var (model, errors) = CanonicalJson.Parse(first);
        Assert.Empty(errors);
        Assert.Equal(first, CanonicalJson.Export(model!)); // byte-for-byte
        AssertProjection(model!, c);
        AssertNoTally(first);
    }

    [Fact]
    public void Xml_round_trips_byte_stable()
    {
        var c = BuildTdsTcsCompany();
        var first = CanonicalXml.Export(c);

        var (model, errors) = CanonicalXml.Parse(first);
        Assert.Empty(errors);
        Assert.Equal(first, CanonicalXml.Export(model!));
        AssertProjection(model!, c);
        AssertNoTally(first);
    }

    [Fact]
    public void Json_and_xml_carry_identical_tds_tcs_payload()
    {
        var c = BuildTdsTcsCompany();
        var (jm, je) = CanonicalJson.Parse(CanonicalJson.Export(c));
        var (xm, xe) = CanonicalXml.Parse(CanonicalXml.Export(c));
        Assert.Empty(je);
        Assert.Empty(xe);
        Assert.Equal(CanonicalJson.Export(jm!), CanonicalJson.Export(xm!));
    }

    [Fact]
    public void Tds_tcs_company_export_import_into_fresh_company_reconciles_json_and_xml()
    {
        var source = BuildTdsTcsCompany();

        foreach (var bytes in new[] { CanonicalJson.Export(source), CanonicalXml.Export(source) })
        {
            var (model, errors) = bytes[0] == (byte)'{' ? CanonicalJson.Parse(bytes) : CanonicalXml.Parse(bytes);
            Assert.Empty(errors);

            var fresh = FreshTarget();
            var result = new CompanyImportService(fresh).Apply(model!);
            Assert.True(result.Applied, string.Join("; ", result.Errors));

            AssertReconciles(source, fresh);
        }
    }

    private static void AssertProjection(CanonicalModel model, Company c)
    {
        Assert.NotNull(model.Company.Tds);
        Assert.True(model.Company.Tds!.Enabled);
        Assert.Equal(ValidTan, model.Company.Tds.Tan);
        Assert.Equal("Company", model.Company.Tds.DeductorType);
        Assert.Equal(8, model.Company.Tds.NaturesOfPayment.Count);
        Assert.True(model.Company.Tds.SurchargeApplicable);

        Assert.NotNull(model.Company.Tcs);
        Assert.Equal(8, model.Company.Tcs!.NaturesOfGoods.Count);
        var legacy = model.Company.Tcs.NaturesOfGoods.Single(n => n.CollectionCode == "6CR");
        Assert.True(legacy.IsLegacy);
        Assert.Equal("2025-04-01", legacy.LegacyCutoff);

        var vendorDto = model.Payload.Ledgers.Single(l => l.Name == "Consultant");
        Assert.True(vendorDto.TdsApplicable);
        Assert.Equal("Firm", vendorDto.DeducteeType);
        Assert.Equal(ValidPan, vendorDto.PartyPan);
        Assert.True(vendorDto.DeductTdsInSameVoucher);
        var nopDto = model.Company.Tds.NaturesOfPayment.Single(n => n.SectionCode == "194J(b)");
        Assert.Equal(nopDto.Id, vendorDto.TdsNatureOfPaymentId);

        var buyerDto = model.Payload.Ledgers.Single(l => l.Name == "Scrap Buyer");
        Assert.True(buyerDto.TcsApplicable);
        Assert.Equal("Individual", buyerDto.CollecteeType);

        var itemDto = model.Payload.StockItems.Single(i => i.Name == "Scrap Metal");
        var nogDto = model.Company.Tcs.NaturesOfGoods.Single(n => n.CollectionCode == "6CE");
        Assert.Equal(nogDto.Id, itemDto.TcsNatureOfGoodsId);

        // The auto-created payable ledgers project their classification tag.
        Assert.Contains(model.Payload.Ledgers, l => l.TdsTcsClassification == "Tds");
        Assert.Contains(model.Payload.Ledgers, l => l.TdsTcsClassification == "Tcs");
    }

    private static void AssertReconciles(Company source, Company target)
    {
        Assert.True(target.TdsEnabled);
        Assert.True(target.TcsEnabled);
        Assert.Equal(source.Tds!.Tan, target.Tds!.Tan);
        Assert.Equal(source.Tds.ResponsiblePersonPan, target.Tds.ResponsiblePersonPan);
        Assert.Equal(source.Tds.SurchargeApplicable, target.Tds.SurchargeApplicable);

        // Masters reconcile count- and figure-exact (resolved by section/collection code across companies).
        Assert.Equal(source.NaturesOfPayment.Count, target.NaturesOfPayment.Count);
        Assert.Equal(source.NaturesOfGoods.Count, target.NaturesOfGoods.Count);
        var srcJ = source.FindNatureOfPaymentByCode("194J(b)")!;
        var tgtJ = target.FindNatureOfPaymentByCode("194J(b)")!;
        Assert.Equal(srcJ.RateWithPanBp, tgtJ.RateWithPanBp);
        Assert.Equal(srcJ.CumulativeThreshold, tgtJ.CumulativeThreshold);
        var tgtLegacy = target.FindNatureOfGoodsByCode("6CR")!;
        Assert.True(tgtLegacy.IsLegacy);
        Assert.Equal(source.FindNatureOfGoodsByCode("6CR")!.LegacyCutoff, tgtLegacy.LegacyCutoff);

        // Auto-created payable ledgers exist exactly once each (reused by name, never duplicated).
        Assert.Single(target.Ledgers, l => l.TdsTcsClassification == TdsTcsLedgerKind.Tds);
        Assert.Single(target.Ledgers, l => l.TdsTcsClassification == TdsTcsLedgerKind.Tcs);

        // The TDS-applicable vendor's default nature resolves to the TARGET's own re-minted nature (by code).
        var vendor = target.FindLedgerByName("Consultant")!;
        Assert.True(vendor.TdsApplicable);
        Assert.Equal(DeducteeType.Firm, vendor.DeducteeType);
        Assert.Equal(ValidPan, vendor.PartyPan);
        Assert.True(vendor.DeductTdsInSameVoucher);
        Assert.Equal(tgtJ.Id, vendor.TdsNatureOfPaymentId);

        var buyer = target.FindLedgerByName("Scrap Buyer")!;
        Assert.True(buyer.TcsApplicable);
        Assert.Equal(CollecteeType.Individual, buyer.CollecteeType);
        Assert.Equal(BuyerPan, buyer.PartyPan);

        var tgtScrap = target.FindNatureOfGoodsByCode("6CE")!;
        var item = target.FindStockItemByName("Scrap Metal")!;
        Assert.Equal(tgtScrap.Id, item.TcsNatureOfGoodsId);

        // The posted 194J withholding voucher's tds_line reconciles paisa- and count-exact, with its nature +
        // deductee re-mapped to the target's re-minted ids (no silent drop — the Phase-6 defect class).
        var tgtVendor = target.FindLedgerByName("Consultant")!;
        var tgtNop = target.FindNatureOfPaymentByCode("194J(b)")!;
        var tdsLines = target.Vouchers.SelectMany(v => v.Lines).Where(l => l.HasTds).Select(l => l.Tds!).ToList();
        var t = Assert.Single(tdsLines);
        Assert.Equal("194J(b)", t.SectionCode);
        Assert.Equal(Money.FromRupees(1_00_000m), t.AssessableValue);
        Assert.Equal(1000, t.RateBasisPoints);
        Assert.Equal(Money.FromRupees(10_000m), t.TdsAmount);
        Assert.True(t.PanApplied);
        Assert.Equal(tgtNop.Id, t.NatureId);           // re-mapped to the target nature
        Assert.Equal(tgtVendor.Id, t.DeducteeLedgerId); // re-mapped to the target ledger
    }

    private static void AssertNoTally(byte[] bytes)
    {
        var text = Encoding.UTF8.GetString(bytes);
        Assert.DoesNotContain("tally", text, StringComparison.OrdinalIgnoreCase);
    }
}
