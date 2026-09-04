using System.Text.Json;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;

namespace Apex.Ledger.Io.Tests;

/// <summary>
/// <b>T0-17 — the AGREEMENT assertion drift lock D9 deliberately declined to make.</b>
///
/// <para>D9 pins that five master-block rate readers exist and how many; its own doc says the decision about whether
/// they must AGREE with <c>GstService.ResolveRate</c> "must not be taken by omission". This file takes it. Every one of
/// the five answers the same question — <b>which posted rate group does this line/leg belong to?</b> — and the posting
/// engine answered that question with the resolver, so a reader that answers it with a single hard-wired rung
/// mis-buckets the line and the money follows the wrong HSN.</para>
///
/// <para><b>Why the five agreed before, and why they cannot now.</b> Until T0-4 S2b the resolver was itself
/// item-then-ledger, so an item-only reader agreed with it by coincidence. S2b shipped the five-level walk with
/// <see cref="GstDetailSource.LedgerFirst"/> as the default order, and Phase 9 slice 1 shipped the HSN-dated
/// <see cref="GstConfig.RateHistory"/> override on top. Either one alone breaks the coincidence. This fixture uses the
/// SEEDED, SHIPPED history rows — cement HSN 2523 (28% legacy → 18% from 22-Sep-2025) and car HSN 8703 (28% → 40%) —
/// so the divergence is not contrived: it is what an ordinary GST 2.0 book does.</para>
///
/// <para><b>Two of the five feed FILED documents,</b> which is why this is a wrong-money defect and not a reporting
/// nicety. <c>EInvoiceJson</c>'s reader becomes the INV-01 <c>ItemList.Item.GstRt</c> and <c>EWayBillJson</c>'s becomes
/// the EWB-01 item rate. NIC defines that field as a property of the SUPPLY AS INVOICED, not of the master as it stands
/// today: <c>GstRt</c> is "The GST rate, represented as percentage that applies to the invoiced item", validated by
/// <c>CGST Value = Taxable Value × GST Rate ÷ 2</c> and <c>IGST Value = Taxable Value × GST Rate</c>
/// (<c>https://einv-apisandbox.nic.in/version1.01/generate-irn.html</c>); the e-Way item rate is the SAME quantity by
/// NIC's own mapping — <c>igstRate = Item.GstRt</c>, <c>cgstRate = sgstRate = Item.GstRt/2</c>,
/// <c>taxableAmount = Item.AssAmt</c> (<c>https://einv-apisandbox.nic.in/Mapping_of_ewaybill_schema.html</c>); and the
/// portal schema states the same identity as <c>TotItemVal = AssAmt × [1 + (CGST Rate + SGST Rate + …)]</c>
/// (<c>https://einvoice1.gst.gov.in/Documents/E-INVOICE-SCHEMA.pdf</c>). A rate that does not reproduce the line's own
/// posted tax from the line's own assessable amount is therefore not merely inconvenient — it is a false declaration.
///
/// <para><b>It lives in the Io test project on purpose:</b> ONE fixture has to prove all five readers agree, and two of
/// them are in <c>Apex.Ledger.Io</c> while three are in <c>Apex.Ledger</c>. Splitting the fixture would let the two
/// halves drift, which is the exact failure being locked.</para>
///
/// <para><b>Every figure below is derived by hand from the fixture, never read off the code.</b></para>
/// </summary>
public sealed class RateReaderResolverAgreementTests
{
    private const string GstinMaharashtra = "27AAPFU0939F1ZV";
    private static readonly DateOnly FyStart = new(2025, 4, 1);

    /// <summary>After the GST 2.0 cut-over (22-Sep-2025), so the seeded 2523/8703 rows in force are the NEW ones.</summary>
    private static readonly DateOnly SaleDate = new(2025, 10, 6);

    private static readonly DateOnly From = new(2025, 10, 1);
    private static readonly DateOnly To = new(2025, 10, 31);
    private static readonly DateTimeOffset Gen = new(2025, 10, 6, 9, 0, 0, TimeSpan.FromHours(5.5));

    // ============================================================ the item-invoice fixture

    /// <summary>
    /// A book on the SHIPPED default order (<see cref="GstDetailSource.LedgerFirst"/>) whose stock-item blocks and
    /// whose resolver give DIFFERENT answers, arranged as an exact SWAP so that neither answer is "unbucketable" and
    /// the only symptom is money on the wrong HSN row:
    /// <list type="bullet">
    ///   <item><b>Cement</b>, HSN 2523 — item block declares 40%. Resolver: the sales ledger (5%) wins the LedgerFirst
    ///     walk, then the seeded HSN-2523 window in force on 06-Oct-2025 overrides it to <b>18%</b>.</item>
    ///   <item><b>Car</b>, HSN 8703 — item block declares 18%. Resolver: ledger 5%, then the seeded HSN-8703 window
    ///     overrides to <b>40%</b>.</item>
    /// </list>
    /// The invoice is posted at the RESOLVER's rates, because that is what every posting path does
    /// (<c>VoucherEntryViewModel</c> calls <c>ResolveRate</c>). So the posted groups are 18% and 40%, and an item-only
    /// reader buckets each line into the OTHER line's group.
    ///
    /// <para><b>Money, by hand.</b> Cement 400 × ₹100 = ₹40,000 @ 18% intra ⇒ CGST ₹3,600 + SGST ₹3,600.
    /// Car 1 × ₹60,000 = ₹60,000 @ 40% intra ⇒ CGST ₹12,000 + SGST ₹12,000. Invoice taxable ₹1,00,000,
    /// tax ₹31,200, party debit ₹1,31,200. Consignment value clears the ₹50,000 Rule-138 threshold.</para>
    /// </summary>
    private static (Company Company, Voucher Sale, EWayBillService EWay) BuildItemInvoiceBook()
    {
        var c = CompanyFactory.CreateSeeded("Rate Reader Agreement Co", FyStart);
        var gst = new GstService(c);
        gst.EnableGst(new GstConfig
        {
            HomeStateCode = "27",
            Gstin = GstinMaharashtra,
            RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart,
            Periodicity = GstReturnPeriodicity.Monthly,
            EWayBillEnabled = true,
            EWayApplicableFrom = FyStart,
        });
        // The dated rate-history windows are the ADVANCED opt-in, not EnableGst. Seeding them is what puts the shipped
        // 2523 (18% from 22-Sep-2025) and 8703 (40%) rows in the book.
        gst.SeedAdvancedGst();

        var inv = new InventoryService(c);
        var grp = inv.CreateStockGroup("Goods");
        var nos = inv.CreateSimpleUnit("Nos", "Numbers", unitQuantityCode: "NOS");
        var main = c.MainLocation!.Id;

        var cement = inv.CreateStockItem("Cement", grp.Id, nos.Id);
        cement.Gst = new StockItemGstDetails
        {
            HsnSac = "2523", Taxability = GstTaxability.Taxable, RateBasisPoints = 4000,
        };
        var car = inv.CreateStockItem("Car", grp.Id, nos.Id);
        car.Gst = new StockItemGstDetails
        {
            HsnSac = "8703", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800,
        };
        inv.AddOpeningBalance(cement.Id, main, 1000m, Money.FromRupees(50m));
        inv.AddOpeningBalance(car.Id, main, 10m, Money.FromRupees(40000m));

        // The sales ledger declares its own block, so under LedgerFirst it — not the item — wins the base walk.
        var sales = Add(c, "Sales", "Sales Accounts", false);
        sales.SalesPurchaseGst = new StockItemGstDetails
        {
            Taxability = GstTaxability.Taxable, RateBasisPoints = 500,
        };
        var debtor = Add(c, "Local Debtor", "Sundry Debtors", true);
        debtor.PartyGst = new PartyGstDetails
        {
            RegistrationType = GstRegistrationType.Regular, Gstin = GstinMaharashtra, StateCode = "27",
        };

        var tax = gst.ComputeInvoiceTax(new[]
        {
            new GstService.TaxableLine(Money.FromRupees(40000m), 1800),
            new GstService.TaxableLine(Money.FromRupees(60000m), 4000),
        }, interState: false, GstTaxDirection.Output);

        var lines = new List<EntryLine>
        {
            new(debtor.Id, Money.FromRupees(1_31_200m), DrCr.Debit),
            new(sales.Id, Money.FromRupees(1_00_000m), DrCr.Credit),
        };
        lines.AddRange(tax.TaxLines);

        var sale = new LedgerService(c).Post(new Voucher(
            Guid.NewGuid(), c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id, SaleDate, lines,
            partyId: debtor.Id,
            inventoryLines: new[]
            {
                new VoucherInventoryLine(cement.Id, main, 400m, Money.FromRupees(100m)),   // 400 × 100 = 40,000
                new VoucherInventoryLine(car.Id, main, 1m, Money.FromRupees(60000m)),      //   1 × 60,000 = 60,000
            }));

        return (c, sale, new EWayBillService(c));
    }

    // ============================================================ non-vacuity: the fixture really does diverge

    /// <summary>
    /// <b>The guard that makes every assertion below meaningful.</b> A bucketing test on a book where the master and
    /// the resolver happen to agree proves nothing — it is green before and after the fix. So state the divergence
    /// outright, from both sides, in literal basis points.
    /// </summary>
    [Fact]
    public void The_fixture_book_really_does_make_the_item_masters_and_the_resolver_disagree()
    {
        var (c, sale, _) = BuildItemInvoiceBook();
        var gst = new GstService(c);
        var valueLedger = GstReportSupport.ResolveValueLedger(c, sale, sale.PartyId);
        var cement = c.StockItems.Single(i => i.Name == "Cement");
        var car = c.StockItems.Single(i => i.Name == "Car");

        // The shipped default order is LedgerFirst — that is the premise the whole defect rests on.
        Assert.Equal(GstDetailSource.LedgerFirst, c.Gst!.SourceOfGstRate);

        // What each ITEM MASTER says (what the five bypass readers read).
        Assert.Equal(4000, cement.Gst!.RateBasisPoints);
        Assert.Equal(1800, car.Gst!.RateBasisPoints);

        // What the RESOLVER says on the voucher date (what the posting engine used) — the exact swap.
        Assert.Equal(1800, gst.ResolveRate(cement, valueLedger, sale.Date).RateBasisPoints);
        Assert.Equal(4000, gst.ResolveRate(car, valueLedger, sale.Date).RateBasisPoints);

        // And the invoice really is MULTI-rate, so the single-rate collapse cannot hide the disagreement.
        Assert.Equal(2, sale.Lines.Count(l => l.Gst is { TaxHead: GstTaxHead.Central }));
    }

    // ============================================================ D9 reader #1 — Gstr1.LineIntegratedRate

    /// <summary>
    /// GSTR-1 Table 12 must give each HSN row the tax of the rate group its own line resolved into.
    /// <para><b>By hand:</b> the posted groups are (18%: taxable 40,000, CGST 3,600, SGST 3,600) and
    /// (40%: taxable 60,000, CGST 12,000, SGST 12,000). Cement resolves to 18% ⇒ HSN 2523 takes ₹7,200 of tax on
    /// ₹40,000; Car resolves to 40% ⇒ HSN 8703 takes ₹24,000 on ₹60,000.</para>
    /// <para><b>Before the fix</b> the item-only reader bucketed Cement into the 40% group and Car into the 18% group,
    /// so 2523 filed ₹24,000 of tax and 8703 filed ₹7,200 — ₹16,800 of tax on the wrong HSN, in a filed return.</para>
    /// </summary>
    [Fact]
    public void Gstr1_hsn_rows_carry_the_tax_of_the_rate_the_line_actually_resolved()
    {
        var (c, _, _) = BuildItemInvoiceBook();
        var r = Gstr1.Build(c, From, To);

        var cement = r.HsnSummary.Single(h => h.HsnSac == "2523");
        Assert.Equal(Money.FromRupees(40000m), cement.TaxableValue);
        Assert.Equal(Money.FromRupees(7200m), cement.TotalTax);

        var car = r.HsnSummary.Single(h => h.HsnSac == "8703");
        Assert.Equal(Money.FromRupees(60000m), car.TaxableValue);
        Assert.Equal(Money.FromRupees(24000m), car.TotalTax);

        // The rate-wise summary reads the POSTED legs and must be untouched by the bucketing fix — the control.
        Assert.Equal(Money.FromRupees(7200m), r.RateSummary.Single(x => x.RateBasisPoints == 1800).TotalTax);
        Assert.Equal(Money.FromRupees(24000m), r.RateSummary.Single(x => x.RateBasisPoints == 4000).TotalTax);
    }

    // ============================================================ D9 reader #3 — EInvoiceJson.LineIntegratedRate

    /// <summary>
    /// The INV-01 the IRP signs must state, per item, the rate that reproduces THAT item's own posted tax from its own
    /// assessable amount (NIC: <c>CGST Value = Taxable Value × GST Rate ÷ 2</c>).
    /// <para><b>By hand:</b> Cement AssAmt ₹40,000 with CGST ₹3,600 ⇒ 40,000 × 18 ÷ 2 ÷ 100 = 3,600 ✓, so GstRt = 18.
    /// Car AssAmt ₹60,000 with CGST ₹12,000 ⇒ 60,000 × 40 ÷ 2 ÷ 100 = 12,000 ✓, so GstRt = 40.</para>
    /// <para><b>Before the fix</b> Cement was declared at 40 and Car at 18 — each item's stated rate contradicted its
    /// own stated tax, and the e-invoice named a different rate than the invoice in the customer's hand.</para>
    /// </summary>
    [Fact]
    public void Inv01_items_state_the_rate_that_reproduces_their_own_posted_tax()
    {
        var (c, sale, _) = BuildItemInvoiceBook();
        using var doc = JsonDocument.Parse(EInvoiceJson.BuildInv01(c, sale));
        var items = doc.RootElement.GetProperty("ItemList");

        var cement = ItemWithHsn(items, "2523");
        Assert.Equal(18m, cement.GetProperty("GstRt").GetDecimal());
        Assert.Equal(40000m, cement.GetProperty("AssAmt").GetDecimal());
        Assert.Equal(3600m, cement.GetProperty("CgstAmt").GetDecimal());
        Assert.Equal(3600m, cement.GetProperty("SgstAmt").GetDecimal());

        var car = ItemWithHsn(items, "8703");
        Assert.Equal(40m, car.GetProperty("GstRt").GetDecimal());
        Assert.Equal(60000m, car.GetProperty("AssAmt").GetDecimal());
        Assert.Equal(12000m, car.GetProperty("CgstAmt").GetDecimal());
        Assert.Equal(12000m, car.GetProperty("SgstAmt").GetDecimal());
    }

    // ============================================================ D9 reader #5 — EWayBillJson.LineIntegratedRate

    /// <summary>
    /// The EWB-01 item rate is the SAME quantity as the INV-01 <c>GstRt</c> by NIC's own schema mapping
    /// (<c>igstRate = Item.GstRt</c>, <c>cgstRate = sgstRate = Item.GstRt/2</c>), so it must state the same figures —
    /// here in the basis points our writer emits (a recorded naming/unit divergence of ours, unchanged by this fix).
    /// <para><b>Before the fix</b> the e-way bill declared cement at 40% and the car at 18%, so a checkpoint reading
    /// the EWB and the customer reading the invoice saw two different rates on the same consignment.</para>
    /// </summary>
    [Fact]
    public void Ewb01_items_state_the_same_rate_the_e_invoice_does()
    {
        var (c, sale, eway) = BuildItemInvoiceBook();
        var record = eway.PrepareRecord(sale, SaleDate);
        eway.SetPartB(record, "TRANSIN01", EWayTransportMode.Road, "MH12AB1234", 250);
        eway.RecordPortalResponse(record, "231000000123", Gen, EWayValidity.ValidUpto(Gen, 250, false));

        using var doc = JsonDocument.Parse(EWayBillJson.BuildEwb01(c, sale, record));
        var items = doc.RootElement.GetProperty("itemList");

        var cement = ItemWithHsn(items, "2523");
        Assert.Equal(1800, cement.GetProperty("GstRt").GetInt32());
        Assert.Equal(4000000L, cement.GetProperty("taxable_amt_paisa").GetInt64());
        Assert.Equal(360000L, cement.GetProperty("cgst_amt_paisa").GetInt64());
        Assert.Equal(360000L, cement.GetProperty("sgst_amt_paisa").GetInt64());

        var car = ItemWithHsn(items, "8703");
        Assert.Equal(4000, car.GetProperty("GstRt").GetInt32());
        Assert.Equal(6000000L, car.GetProperty("taxable_amt_paisa").GetInt64());
        Assert.Equal(1200000L, car.GetProperty("cgst_amt_paisa").GetInt64());
        Assert.Equal(1200000L, car.GetProperty("sgst_amt_paisa").GetInt64());
    }

    // ============================================================ the service-invoice fixture

    /// <summary>
    /// The ledger-only mirror, for the two SERVICE readers. A service leg is always its own value ledger, so the walk
    /// ORDER cannot separate reader from resolver here — the dated <see cref="GstConfig.RateHistory"/> override does,
    /// and it is the same mechanism GST 2.0 uses on services.
    /// <list type="bullet">
    ///   <item><b>Consultancy</b>, SAC 998311 — ledger declares 18%, no dated row ⇒ resolver <b>18%</b>.</item>
    ///   <item><b>Works Contract</b>, SAC 995411 — ledger declares 18%, a dated row for that SAC in force on the
    ///     voucher date ⇒ resolver <b>5%</b>. The row is FIXTURE data exercising the mechanism; it asserts no rate.</item>
    /// </list>
    /// <para><b>Money, by hand.</b> Consultancy ₹10,000 @ 18% intra ⇒ CGST ₹900 + SGST ₹900. Works Contract ₹20,000
    /// @ 5% intra ⇒ CGST ₹500 + SGST ₹500. Invoice taxable ₹30,000, tax ₹2,800, party debit ₹32,800.</para>
    /// </summary>
    private static (Company Company, Voucher Sale) BuildServiceInvoiceBook()
    {
        var c = CompanyFactory.CreateSeeded("Rate Reader Agreement Services Co", FyStart);
        var gst = new GstService(c);
        gst.EnableGst(new GstConfig
        {
            HomeStateCode = "27",
            Gstin = GstinMaharashtra,
            RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart,
            Periodicity = GstReturnPeriodicity.Monthly,
        });
        gst.SeedAdvancedGst();
        c.Gst!.AddRateHistory(new GstRateHistoryEntry(
            Guid.NewGuid(), "995411", 500, GstRateClass.Merit,
            new DateOnly(2025, 9, 22), null, GstValuationBasis.TransactionValue,
            "Fixture window: SAC 995411 at 5%"));

        var consultancy = Add(c, "Consultancy Income", "Sales Accounts", false);
        consultancy.SalesPurchaseGst = new StockItemGstDetails
        {
            HsnSac = "998311", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800,
            SupplyType = GstSupplyType.Services,
        };
        var works = Add(c, "Works Contract Income", "Sales Accounts", false);
        works.SalesPurchaseGst = new StockItemGstDetails
        {
            HsnSac = "995411", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800,
            SupplyType = GstSupplyType.Services,
        };
        var debtor = Add(c, "Local Debtor", "Sundry Debtors", true);
        debtor.PartyGst = new PartyGstDetails
        {
            RegistrationType = GstRegistrationType.Regular, Gstin = GstinMaharashtra, StateCode = "27",
        };

        var tax = gst.ComputeInvoiceTax(new[]
        {
            new GstService.TaxableLine(Money.FromRupees(10000m), 1800),
            new GstService.TaxableLine(Money.FromRupees(20000m), 500),
        }, interState: false, GstTaxDirection.Output);

        var lines = new List<EntryLine>
        {
            new(debtor.Id, Money.FromRupees(32800m), DrCr.Debit),
            new(consultancy.Id, Money.FromRupees(10000m), DrCr.Credit),
            new(works.Id, Money.FromRupees(20000m), DrCr.Credit),
        };
        lines.AddRange(tax.TaxLines);

        var sale = new LedgerService(c).Post(new Voucher(
            Guid.NewGuid(), c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id, SaleDate, lines,
            partyId: debtor.Id, isAccountingInvoice: true));

        return (c, sale);
    }

    /// <summary>The service twin of the item non-vacuity guard.</summary>
    [Fact]
    public void The_fixture_book_really_does_make_the_service_ledgers_and_the_resolver_disagree()
    {
        var (c, sale) = BuildServiceInvoiceBook();
        var gst = new GstService(c);
        var consultancy = c.Ledgers.Single(l => l.Name == "Consultancy Income");
        var works = c.Ledgers.Single(l => l.Name == "Works Contract Income");

        Assert.Equal(1800, consultancy.SalesPurchaseGst!.RateBasisPoints);
        Assert.Equal(1800, works.SalesPurchaseGst!.RateBasisPoints);

        Assert.Equal(1800, gst.ResolveRate(null, consultancy, sale.Date).RateBasisPoints);
        Assert.Equal(500, gst.ResolveRate(null, works, sale.Date).RateBasisPoints);
    }

    // ============================================================ D9 reader #2 — Gstr1.LedgerIntegratedRate

    /// <summary>
    /// <b>This one loses filed tax outright, not merely misplaces it.</b> With both legs read at the ledger's declared
    /// 18%, the posted 5% group matched no leg at all and its ₹1,000 of tax was silently dropped from Table 12, while
    /// the 18% group's ₹1,800 was spread across both legs by value share (₹600 / ₹1,200).
    /// <para><b>By hand, correct:</b> SAC 998311 takes the whole 18% group — value ₹10,000, tax ₹1,800; SAC 995411
    /// takes the whole 5% group — value ₹20,000, tax ₹1,000. Σ = ₹2,800, exactly the posted forward tax.</para>
    /// </summary>
    [Fact]
    public void Gstr1_sac_rows_carry_the_tax_of_the_rate_the_service_leg_actually_resolved()
    {
        var (c, _) = BuildServiceInvoiceBook();
        var r = Gstr1.Build(c, From, To);

        var consultancy = r.HsnSummary.Single(h => h.HsnSac == "998311");
        Assert.Equal(Money.FromRupees(10000m), consultancy.TaxableValue);
        Assert.Equal(Money.FromRupees(1800m), consultancy.TotalTax);

        var works = r.HsnSummary.Single(h => h.HsnSac == "995411");
        Assert.Equal(Money.FromRupees(20000m), works.TaxableValue);
        Assert.Equal(Money.FromRupees(1000m), works.TotalTax);

        // Nothing may be lost: Table 12's tax must foot to the posted forward tax.
        Assert.Equal(2800m, r.HsnSummary.Sum(h => h.TotalTax.Amount));
    }

    // ============================================================ D9 reader #4 — EInvoiceJson.ServiceLegsByRate

    /// <summary>
    /// The INV-01 mirror. Each SAC-bearing leg must be declared at the rate that reproduces its own posted tax.
    /// <para><b>Before the fix</b> both legs bucketed to 18%, so the 5% group found no leg and fell through to the
    /// synthetic "plain As-Voucher" item with an EMPTY <c>HsnCd</c> — an e-invoice line declaring ₹20,000 of supply
    /// with no SAC at all, beside a 998311 line whose stated tax was ₹1,200 against its own ₹20,000.</para>
    /// </summary>
    [Fact]
    public void Inv01_service_items_state_the_rate_that_reproduces_their_own_posted_tax()
    {
        var (c, sale) = BuildServiceInvoiceBook();
        using var doc = JsonDocument.Parse(EInvoiceJson.BuildInv01(c, sale));
        var items = doc.RootElement.GetProperty("ItemList");

        // Exactly two items, each SAC-bearing — no synthetic HsnCd "" line invented for an unmatched group.
        Assert.Equal(2, items.GetArrayLength());

        var consultancy = ItemWithHsn(items, "998311");
        Assert.Equal(18m, consultancy.GetProperty("GstRt").GetDecimal());
        Assert.Equal(10000m, consultancy.GetProperty("AssAmt").GetDecimal());
        Assert.Equal(900m, consultancy.GetProperty("CgstAmt").GetDecimal());
        Assert.Equal(900m, consultancy.GetProperty("SgstAmt").GetDecimal());

        var works = ItemWithHsn(items, "995411");
        Assert.Equal(5m, works.GetProperty("GstRt").GetDecimal());
        Assert.Equal(20000m, works.GetProperty("AssAmt").GetDecimal());
        Assert.Equal(500m, works.GetProperty("CgstAmt").GetDecimal());
        Assert.Equal(500m, works.GetProperty("SgstAmt").GetDecimal());
    }

    // ============================================================ helpers

    private static JsonElement ItemWithHsn(JsonElement items, string hsn)
    {
        foreach (var item in items.EnumerateArray())
        {
            var code = item.TryGetProperty("HsnCd", out var a) ? a.GetString() : item.GetProperty("HsnCd").GetString();
            if (code == hsn) return item;
        }

        Assert.Fail($"No payload item declares HSN/SAC {hsn}. Items: {items}");
        return default;
    }

    private static Domain.Ledger Add(Company c, string name, string groupName, bool openingIsDebit)
    {
        var l = new Domain.Ledger(Guid.NewGuid(), name, c.FindGroupByName(groupName)!.Id, Money.Zero, openingIsDebit);
        c.AddLedger(l);
        return l;
    }
}
