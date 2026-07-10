using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Microsoft.Data.Sqlite;
using Xunit;
using Domain = Apex.Ledger.Domain;

namespace Apex.Persistence.Sqlite.Tests;

/// <summary>
/// Phase-7 slice-1 TDS/TCS persistence contract (ER-1): a company with TDS+TCS enabled — the shared deductor
/// config (TAN/type/responsible person/surcharge/cess/periodicity), the seeded Nature-of-Payment /
/// Nature-of-Goods masters, the auto-created "TDS Payable"/"TCS Payable" ledgers, a party ledger with TDS
/// applicability + PAN + deductee type, and a stock item with a TCS nature — SAVES and RELOADS at
/// <see cref="Schema.CurrentVersion"/>, preserving every field. A non-TDS/TCS company round-trips with no TDS/TCS
/// state (ER-13).
/// </summary>
public sealed class TdsTcsRoundTripTests
{
    private const string ValidTan = "MUMA12345B";
    private const string ValidPan = "AAPFU0939F";

    private static Company SeedTdsTcsCompany()
    {
        var c = CompanyFactory.CreateSeeded("Withholding Persist Co", new DateOnly(2025, 4, 1));
        var svc = new TdsTcsService(c);
        svc.EnableTds(new TdsConfig
        {
            Tan = ValidTan, DeductorType = DeductorType.Company,
            ResponsiblePersonName = "A. Sharma", ResponsiblePersonPan = ValidPan,
            ResponsiblePersonDesignation = "Director", ResponsiblePersonAddress = "12 MG Road",
            SurchargeApplicable = true, CessApplicable = true,
            ApplicableFrom = new DateOnly(2025, 4, 1),
        });
        svc.EnableTcs(new TcsConfig { Tan = ValidTan, CollectorType = DeductorType.Company });

        var nop = c.FindNatureOfPaymentByCode("194J(b)")!;
        var nog = c.FindNatureOfGoodsByCode("6CE")!;

        var vendor = new Domain.Ledger(Guid.NewGuid(), "Consultant", c.FindGroupByName("Sundry Creditors")!.Id, Money.Zero, false)
        {
            TdsApplicable = true, TdsNatureOfPaymentId = nop.Id, DeducteeType = DeducteeType.Firm,
            PartyPan = ValidPan, DeductTdsInSameVoucher = true,
        };
        c.AddLedger(vendor);

        var buyer = new Domain.Ledger(Guid.NewGuid(), "Scrap Buyer", c.FindGroupByName("Sundry Debtors")!.Id, Money.Zero, true)
        {
            TcsApplicable = true, TcsNatureOfGoodsId = nog.Id, CollecteeType = CollecteeType.Individual, PartyPan = ValidPan,
        };
        c.AddLedger(buyer);

        var inv = new InventoryService(c);
        var scrap = inv.CreateStockItem("Scrap Metal", inv.CreateStockGroup("Waste").Id, inv.CreateSimpleUnit("Kg", "Kilogram").Id);
        scrap.TcsNatureOfGoodsId = nog.Id;

        return c;
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Tds_tcs_config_masters_and_ledger_flags_survive_save_reload()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"apex-tdstcs-{Guid.NewGuid():N}.db");
        try
        {
            var original = SeedTdsTcsCompany();
            using (var write = new SqliteCompanyStore(dbPath))
            {
                write.Save(original);
                Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));
                write.Save(original); // re-save (delete-then-insert) must not trip an FK
            }

            Company r;
            using (var read = new SqliteCompanyStore(dbPath))
                r = read.Load(original.Id)!;

            // Config (shared deductor identity read into both TDS + TCS).
            Assert.True(r.TdsEnabled);
            Assert.True(r.TcsEnabled);
            Assert.Equal(ValidTan, r.Tds!.Tan);
            Assert.Equal(DeductorType.Company, r.Tds.DeductorType);
            Assert.Equal("A. Sharma", r.Tds.ResponsiblePersonName);
            Assert.Equal(ValidPan, r.Tds.ResponsiblePersonPan);
            Assert.Equal("Director", r.Tds.ResponsiblePersonDesignation);
            Assert.True(r.Tds.SurchargeApplicable);
            Assert.True(r.Tds.CessApplicable);
            Assert.Equal(new DateOnly(2025, 4, 1), r.Tds.ApplicableFrom);
            Assert.Equal(ValidTan, r.Tcs!.Tan);

            // Seeded masters survived count- and figure-exact.
            Assert.Equal(8, r.NaturesOfPayment.Count);
            Assert.Equal(8, r.NaturesOfGoods.Count);
            var j = r.FindNatureOfPaymentByCode("194J(b)")!;
            Assert.Equal(1000, j.RateWithPanBp);
            Assert.Equal(2000, j.RateWithoutPanBp);
            Assert.Equal("94J-B", j.FvuSectionCode);
            Assert.Equal(Money.FromRupees(50_000m), j.CumulativeThreshold);
            var mv = r.FindNatureOfGoodsByCode("6CL")!;
            Assert.Equal(Money.FromRupees(10_00_000m), mv.Threshold);
            var legacy = r.FindNatureOfGoodsByCode("6CR")!;
            Assert.True(legacy.IsLegacy);
            Assert.Equal(new DateOnly(2025, 4, 1), legacy.LegacyCutoff);

            // Auto-created payable ledgers survived with their classification.
            var svc = new TdsTcsService(r);
            Assert.NotNull(svc.FindPayableLedger(TdsTcsLedgerKind.Tds));
            Assert.NotNull(svc.FindPayableLedger(TdsTcsLedgerKind.Tcs));

            // Ledger + item applicability flags survived, and the nature ids still resolve.
            var vendor = r.FindLedgerByName("Consultant")!;
            Assert.True(vendor.TdsApplicable);
            Assert.Equal(DeducteeType.Firm, vendor.DeducteeType);
            Assert.Equal(ValidPan, vendor.PartyPan);
            Assert.True(vendor.DeductTdsInSameVoucher);
            Assert.NotNull(r.FindNatureOfPayment(vendor.TdsNatureOfPaymentId!.Value));

            var buyer = r.FindLedgerByName("Scrap Buyer")!;
            Assert.True(buyer.TcsApplicable);
            Assert.Equal(CollecteeType.Individual, buyer.CollecteeType);
            Assert.NotNull(r.FindNatureOfGoods(buyer.TcsNatureOfGoodsId!.Value));

            var item = r.FindStockItemByName("Scrap Metal")!;
            Assert.NotNull(r.FindNatureOfGoods(item.TcsNatureOfGoodsId!.Value));
        }
        finally { TempDbFile.Delete(dbPath); }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void A_non_tds_tcs_company_round_trips_with_no_state()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"apex-notdstcs-{Guid.NewGuid():N}.db");
        try
        {
            var c = CompanyFactory.CreateSeeded("Plain Co", new DateOnly(2025, 4, 1));
            using (var write = new SqliteCompanyStore(dbPath)) write.Save(c);
            Company r;
            using (var read = new SqliteCompanyStore(dbPath)) r = read.Load(c.Id)!;
            Assert.False(r.TdsEnabled);
            Assert.False(r.TcsEnabled);
            Assert.Null(r.Tds);
            Assert.Null(r.Tcs);
            Assert.DoesNotContain(r.Ledgers, l => l.TdsTcsClassification is not null);
            Assert.DoesNotContain(r.Ledgers, l => l.TdsApplicable || l.TcsApplicable);
        }
        finally { TempDbFile.Delete(dbPath); }
    }

    private static long ReadSchemaVersion(string dbPath)
    {
        using var conn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadWrite }.ToString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT version FROM schema_version LIMIT 1;";
        var v = Convert.ToInt64(cmd.ExecuteScalar());
        SqliteConnection.ClearPool(conn);
        return v;
    }
}
