using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Seed;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// Phase 7 slice 1 (TDS/TCS masters + config + duty-ledger auto-create). Pure, deterministic. Proves: the TAN/PAN
/// validators; the Nature-of-Payment / Nature-of-Goods master CRUD + validation; the A14-verified seed table
/// (FY 2025-26 rates/thresholds/FVU codes); EnableTds/EnableTcs idempotency + the auto-created payable ledgers
/// under Duties &amp; Taxes (tagged, and covered by <see cref="ClassificationRules.IsDutiesAndTaxesLedger"/>); the
/// §206C(1H) legacy year-gate; and that a non-TDS/TCS company is unaffected. No tax computation ships in this slice.
/// </summary>
public class TdsTcsTests
{
    private const string ValidTan = "MUMA12345B";
    private const string ValidPan = "AAPFU0939F";

    private static Company NewCompany() => CompanyFactory.CreateSeeded("Withholding Co", new DateOnly(2025, 4, 1));

    // ---- validators ----

    [Theory]
    [InlineData("MUMA12345B", true)]
    [InlineData("DELB99999Z", true)]
    [InlineData("MUM12345B", false)]   // too short
    [InlineData("MUMA12345BB", false)] // too long
    [InlineData("1UMA12345B", false)]  // first char not a letter
    [InlineData("MUMA1234AB", false)]  // digit block has a letter
    [InlineData("MUMA123451", false)]  // last char not a letter
    [InlineData(null, false)]
    public void Tan_validates_structure(string? tan, bool expected)
    {
        Assert.Equal(expected, Tan.IsValid(tan));
        if (expected) Tan.Validate(tan);
        else Assert.Throws<ArgumentException>(() => Tan.Validate(tan));
    }

    [Theory]
    [InlineData("AAPFU0939F", true)]
    [InlineData("ABCDE1234Z", true)]
    [InlineData("AAPFU0939", false)]   // too short
    [InlineData("AAPF10939F", false)]  // 4th char a digit (letters block)
    [InlineData("AAPFU093AF", false)]  // digit block has a letter
    [InlineData("AAPFU09391", false)]  // last char not a letter
    [InlineData(null, false)]
    public void Pan_validates_structure(string? pan, bool expected)
    {
        Assert.Equal(expected, Pan.IsValid(pan));
        if (expected) Pan.Validate(pan);
        else Assert.Throws<ArgumentException>(() => Pan.Validate(pan));
    }

    // ---- config validation (fail-fast) ----

    [Fact]
    public void Enabling_tds_without_tan_throws()
    {
        var c = NewCompany();
        Assert.Throws<ArgumentException>(() => new TdsTcsService(c).EnableTds(new TdsConfig()));
    }

    [Fact]
    public void Enabling_tds_with_invalid_tan_throws()
    {
        var c = NewCompany();
        Assert.Throws<ArgumentException>(() => new TdsTcsService(c).EnableTds(new TdsConfig { Tan = "BADTAN" }));
    }

    [Fact]
    public void Enabling_tds_with_invalid_responsible_pan_throws()
    {
        var c = NewCompany();
        Assert.Throws<ArgumentException>(() => new TdsTcsService(c).EnableTds(
            new TdsConfig { Tan = ValidTan, ResponsiblePersonPan = "NOTPAN" }));
    }

    // ---- enable TDS: seed + auto-ledger + idempotency ----

    [Fact]
    public void Enable_tds_seeds_natures_and_auto_creates_payable_ledger()
    {
        var c = NewCompany();
        var svc = new TdsTcsService(c);
        svc.EnableTds(new TdsConfig { Tan = ValidTan, ResponsiblePersonName = "A. Sharma", ResponsiblePersonPan = ValidPan });

        Assert.True(c.TdsEnabled);
        // The 8 predefined Nature-of-Payment masters (194I/194J bifurcated) are seeded.
        Assert.Equal(8, c.NaturesOfPayment.Count);

        // "TDS Payable" auto-created under Duties & Taxes, tagged, and excluded from the item-invoice pairing sum.
        var payable = svc.FindPayableLedger(TdsTcsLedgerKind.Tds)!;
        Assert.Equal(TdsTcsService.TdsPayableLedgerName, payable.Name);
        Assert.False(payable.OpeningIsDebit); // a liability opens on the credit side
        Assert.Equal("Duties & Taxes", c.FindGroup(payable.GroupId)!.Name);
        Assert.True(ClassificationRules.IsDutiesAndTaxesLedger(payable, c));
    }

    [Fact]
    public void Enable_tds_is_idempotent()
    {
        var c = NewCompany();
        var svc = new TdsTcsService(c);
        svc.EnableTds(new TdsConfig { Tan = ValidTan });
        var ledgerCount = c.Ledgers.Count;
        var natureCount = c.NaturesOfPayment.Count;

        svc.EnableTds(c.Tds!); // re-enable the same config
        Assert.Equal(ledgerCount, c.Ledgers.Count);   // no duplicate payable ledger
        Assert.Equal(natureCount, c.NaturesOfPayment.Count); // slabs preserved, not re-seeded
        Assert.Single(c.Ledgers, l => l.TdsTcsClassification == TdsTcsLedgerKind.Tds);
    }

    [Fact]
    public void Enable_tcs_seeds_natures_and_auto_creates_payable_ledger()
    {
        var c = NewCompany();
        var svc = new TdsTcsService(c);
        svc.EnableTcs(new TcsConfig { Tan = ValidTan });

        Assert.True(c.TcsEnabled);
        Assert.Equal(8, c.NaturesOfGoods.Count);
        var payable = svc.FindPayableLedger(TdsTcsLedgerKind.Tcs)!;
        Assert.Equal(TdsTcsService.TcsPayableLedgerName, payable.Name);
        Assert.True(ClassificationRules.IsDutiesAndTaxesLedger(payable, c));
    }

    [Fact]
    public void Enable_tcs_is_idempotent()
    {
        var c = NewCompany();
        var svc = new TdsTcsService(c);
        svc.EnableTcs(new TcsConfig { Tan = ValidTan });
        var ledgerCount = c.Ledgers.Count;
        svc.EnableTcs(c.Tcs!);
        Assert.Equal(ledgerCount, c.Ledgers.Count);
        Assert.Single(c.Ledgers, l => l.TdsTcsClassification == TdsTcsLedgerKind.Tcs);
    }

    [Fact]
    public void Tds_and_tcs_can_be_enabled_together_with_two_payable_ledgers()
    {
        var c = NewCompany();
        var svc = new TdsTcsService(c);
        svc.EnableTds(new TdsConfig { Tan = ValidTan });
        svc.EnableTcs(new TcsConfig { Tan = ValidTan });
        Assert.NotNull(svc.FindPayableLedger(TdsTcsLedgerKind.Tds));
        Assert.NotNull(svc.FindPayableLedger(TdsTcsLedgerKind.Tcs));
    }

    // ---- seed correctness vs the A14 verified table (FY 2025-26) ----

    [Fact]
    public void Tds_seed_matches_a14_verified_table()
    {
        var byCode = SeedTdsTcsRates.BuildTdsDefaults().ToDictionary(n => n.SectionCode);

        void Check(string section, int withPan, int withoutPan, string fvu, decimal? single, decimal? cumulative)
        {
            var n = byCode[section];
            Assert.Equal(withPan, n.RateWithPanBp);
            Assert.Equal(withoutPan, n.RateWithoutPanBp);
            Assert.Equal(fvu, n.FvuSectionCode);
            Assert.Equal(single is { } s ? Money.FromRupees(s) : (Money?)null, n.SingleTransactionThreshold);
            Assert.Equal(cumulative is { } cu ? Money.FromRupees(cu) : (Money?)null, n.CumulativeThreshold);
            Assert.True(n.IsPredefined);
        }

        Assert.Equal(8, byCode.Count);
        Check("194A", 1000, 2000, "94A", null, 50_000m);
        Check("194C", 100, 2000, "94C", 30_000m, 1_00_000m);
        Check("194H", 200, 2000, "94H", null, 20_000m);
        Check("194I(a)", 200, 2000, "4IA", null, 6_00_000m);
        Check("194I(b)", 1000, 2000, "4IB", null, 6_00_000m);
        Check("194J(a)", 200, 2000, "94J-A", null, 50_000m);
        Check("194J(b)", 1000, 2000, "94J-B", null, 50_000m);
        Check("194Q", 10, 500, "94Q", null, 50_00_000m); // no-PAN = 5% special §206AA cap, NOT 20%
    }

    [Fact]
    public void Tcs_seed_matches_a14_verified_table_with_correct_collection_codes()
    {
        var byCode = SeedTdsTcsRates.BuildTcsDefaults().ToDictionary(n => n.CollectionCode);

        void Check(string code, string name, int withPan, int withoutPan, decimal? threshold, bool legacy)
        {
            var n = byCode[code];
            Assert.Contains(name, n.Name);
            Assert.Equal(withPan, n.RateWithPanBp);
            Assert.Equal(withoutPan, n.RateWithoutPanBp);
            Assert.True(n.BaseIncludesGst); // every §206C base includes GST (Circular 17/2020)
            Assert.Equal(threshold is { } th ? Money.FromRupees(th) : (Money?)null, n.Threshold);
            Assert.Equal(legacy, n.IsLegacy);
        }

        Assert.Equal(8, byCode.Count);
        // The corrected verified collection codes (scrap=6CE, liquor=6CA, tendu=6CI, timber-lease=6CB, …).
        Check("6CE", "Scrap", 100, 500, null, false);
        Check("6CB", "Timber", 200, 500, null, false);
        Check("6CC", "Timber", 200, 500, null, false);
        Check("6CI", "Tendu", 500, 1000, null, false);
        Check("6CA", "Alcoholic liquor", 100, 500, null, false);
        Check("6CJ", "Minerals", 100, 500, null, false);
        Check("6CL", "Motor vehicle", 100, 500, 10_00_000m, false);
        Check("6CR", "Sale of goods", 10, 100, 50_00_000m, true); // 1H no-PAN capped at 1% (§206CC special proviso)
    }

    // ---- §206C(1H) legacy year-gate ----

    [Fact]
    public void Legacy_206c_1h_is_non_selectable_on_or_after_the_fa2025_cutoff()
    {
        var legacy = SeedTdsTcsRates.BuildTcsDefaults().Single(n => n.CollectionCode == "6CR");
        Assert.True(legacy.IsLegacy);
        Assert.Equal(SeedTdsTcsRates.LegacyGoodsCutoff, legacy.LegacyCutoff);

        // Selectable for FY 2024-25 (historical) but OFF from 01-Apr-2025 onward.
        Assert.True(legacy.IsSelectableOn(new DateOnly(2025, 3, 31)));
        Assert.False(legacy.IsSelectableOn(new DateOnly(2025, 4, 1)));
        Assert.False(legacy.IsSelectableOn(new DateOnly(2026, 1, 1)));

        // A non-legacy nature is always selectable.
        var scrap = SeedTdsTcsRates.BuildTcsDefaults().Single(n => n.CollectionCode == "6CE");
        Assert.True(scrap.IsSelectableOn(new DateOnly(2025, 4, 1)));
    }

    // ---- master CRUD + lookups ----

    [Fact]
    public void Company_resolves_natures_by_id_and_code()
    {
        var c = NewCompany();
        new TdsTcsService(c).EnableTds(new TdsConfig { Tan = ValidTan });
        new TdsTcsService(c).EnableTcs(new TcsConfig { Tan = ValidTan });

        var byCode = c.FindNatureOfPaymentByCode("194J(b)")!;
        Assert.Equal(1000, byCode.RateWithPanBp);
        Assert.Same(byCode, c.FindNatureOfPayment(byCode.Id));

        var goods = c.FindNatureOfGoodsByCode("6CE")!;
        Assert.Equal("Scrap", goods.Name);
        Assert.Same(goods, c.FindNatureOfGoods(goods.Id));
    }

    [Fact]
    public void Nature_of_payment_rejects_negative_rate_and_blank_codes()
    {
        Assert.Throws<ArgumentException>(() => new NatureOfPayment(Guid.NewGuid(), "194J", "x", -1, 2000, "94J"));
        Assert.Throws<ArgumentException>(() => new NatureOfPayment(Guid.NewGuid(), " ", "x", 1000, 2000, "94J"));
        Assert.Throws<ArgumentException>(() => new NatureOfPayment(Guid.NewGuid(), "194J", "x", 1000, 2000, " "));
    }

    // ---- ledger / item TDS/TCS applicability flags ----

    [Fact]
    public void Ledger_and_item_carry_tds_tcs_applicability_flags()
    {
        var c = NewCompany();
        var svc = new TdsTcsService(c);
        svc.EnableTds(new TdsConfig { Tan = ValidTan });
        svc.EnableTcs(new TcsConfig { Tan = ValidTan });

        var nop = c.FindNatureOfPaymentByCode("194J(b)")!;
        var nog = c.FindNatureOfGoodsByCode("6CE")!;

        var vendor = new Domain.Ledger(Guid.NewGuid(), "Consultant", c.FindGroupByName("Sundry Creditors")!.Id, Money.Zero, false)
        {
            TdsApplicable = true, TdsNatureOfPaymentId = nop.Id, DeducteeType = DeducteeType.Firm,
            PartyPan = ValidPan, DeductTdsInSameVoucher = true,
        };
        c.AddLedger(vendor);
        Assert.True(vendor.TdsApplicable);
        Assert.Equal(nop.Id, vendor.TdsNatureOfPaymentId);
        Assert.Equal(DeducteeType.Firm, vendor.DeducteeType);

        var inv = new InventoryService(c);
        var scrap = inv.CreateStockItem("Scrap Metal", inv.CreateStockGroup("Waste").Id, inv.CreateSimpleUnit("Kg", "Kilogram").Id);
        scrap.TcsNatureOfGoodsId = nog.Id;
        Assert.Equal(nog.Id, scrap.TcsNatureOfGoodsId);
    }

    // ---- a non-TDS/TCS company is unaffected ----

    [Fact]
    public void A_plain_company_has_no_tds_tcs_state()
    {
        var c = NewCompany();
        Assert.False(c.TdsEnabled);
        Assert.False(c.TcsEnabled);
        Assert.Null(c.Tds);
        Assert.Null(c.Tcs);
        Assert.Empty(c.NaturesOfPayment);
        Assert.Empty(c.NaturesOfGoods);
        Assert.DoesNotContain(c.Ledgers, l => l.TdsTcsClassification is not null);
    }
}
