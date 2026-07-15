using System.Reflection;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// Phase-9 slice-5 <b>e-Way consignment-value + coverage + lifecycle</b> gate (Rule 138; §2.6; ER-5, ER-9). Proves the
/// dated consignment value is a pure read of the posted lines (taxable + forward tax + posted cess, exempt/RCM excluded),
/// the STRICT <c>&gt;</c> threshold boundary + per-state override, the inter-state job-work "mandatory irrespective of
/// value" carve-out, the Part-B ≤ 50 km relaxation, the 24-h cancel + 72-h advisory, EWB-02 consolidation, Ship-To GSTIN
/// gating, and the ER-5 twin (the EWB number is never computed locally — inbound only). All figures worked by hand.
/// </summary>
public sealed class EWayValueTests
{
    private const string GstinMaharashtra = "27AAPFU0939F1ZV";
    private static readonly DateOnly FyStart = new(2025, 4, 1);
    private static readonly DateOnly SaleDate = new(2025, 4, 10);

    private sealed class Fx
    {
        public required Company Company { get; init; }
        public required EWayBillService Service { get; init; }
        public required Guid SalesTypeId { get; init; }
        public required Guid SalesLedgerId { get; init; }
        public required Guid GodownId { get; init; }
        public required Guid WidgetId { get; init; }   // taxable
        public required Guid BookId { get; init; }     // exempt
        public required Guid IntraPartyId { get; init; }
        public required Guid InterPartyId { get; init; }
    }

    // Build a company with GST + e-Way enabled. homeState drives inter/intra; overrides seed per-state thresholds.
    // registration lets a caller build a Composition dealer (a Bill-of-Supply seller with NO forward tax lines).
    private static Fx Build(
        string homeState = "27", bool intraApplicable = true,
        EWayConsignmentBasis basis = EWayConsignmentBasis.Rule138Default,
        GstRegistrationType registration = GstRegistrationType.Regular,
        CompositionSubType compositionSubType = CompositionSubType.Trader,
        params EWayStateThreshold[] overrides)
    {
        var c = CompanyFactory.CreateSeeded("e-Way Co", FyStart);
        var gst = new GstService(c);
        var config = new GstConfig
        {
            HomeStateCode = homeState, Gstin = GstinMaharashtra, RegistrationType = registration,
            CompositionSubType = registration == GstRegistrationType.Composition ? compositionSubType : null,
            ApplicableFrom = FyStart, Periodicity = GstReturnPeriodicity.Monthly,
            EWayBillEnabled = true, EWayApplicableFrom = FyStart, ConsignmentBasis = basis,
            EWayIntraStateApplicable = intraApplicable,
        };
        foreach (var o in overrides) config.AddEWayStateThreshold(o);
        gst.EnableGst(config);

        var inv = new InventoryService(c);
        var grp = inv.CreateStockGroup("Goods");
        var nos = inv.CreateSimpleUnit("Nos", "Numbers", unitQuantityCode: "NOS");
        var widget = inv.CreateStockItem("Widget", grp.Id, nos.Id);
        widget.Gst = new StockItemGstDetails { HsnSac = "847130", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800 };
        var book = inv.CreateStockItem("Book", grp.Id, nos.Id);
        book.Gst = new StockItemGstDetails { HsnSac = "4901", Taxability = GstTaxability.Exempt };

        var sales = Add(c, "Sales", "Sales Accounts", false);
        var intra = Add(c, "Local Debtor", "Sundry Debtors", true);
        intra.PartyGst = new PartyGstDetails { RegistrationType = GstRegistrationType.Consumer, StateCode = homeState };
        var inter = Add(c, "Out-of-State Debtor", "Sundry Debtors", true);
        inter.PartyGst = new PartyGstDetails { RegistrationType = GstRegistrationType.Consumer, StateCode = homeState == "24" ? "27" : "24" };

        return new Fx
        {
            Company = c, Service = new EWayBillService(c),
            SalesTypeId = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id,
            SalesLedgerId = sales.Id, GodownId = c.MainLocation!.Id,
            WidgetId = widget.Id, BookId = book.Id, IntraPartyId = intra.Id, InterPartyId = inter.Id,
        };
    }

    private static Domain.Ledger Add(Company c, string name, string groupName, bool openingIsDebit)
    {
        var l = new Domain.Ledger(Guid.NewGuid(), name, c.FindGroupByName(groupName)!.Id, Money.Zero, openingIsDebit);
        c.AddLedger(l);
        return l;
    }

    // A movement voucher with explicit posted item + tax lines (values fully controlled, so the read is hand-verifiable).
    private static Voucher Movement(
        Fx f, Guid partyId, IReadOnlyList<(Guid ItemId, decimal Value)> items, IReadOnlyList<EntryLine> taxLines,
        DateOnly? date = null)
    {
        var totalValue = items.Sum(i => i.Value);
        var totalTax = taxLines.Sum(l => l.Amount.Amount);
        var lines = new List<EntryLine>
        {
            new(partyId, new Money(totalValue + totalTax), DrCr.Debit),
            new(f.SalesLedgerId, new Money(totalValue), DrCr.Credit),
        };
        lines.AddRange(taxLines);
        var invLines = items.Select(i => new VoucherInventoryLine(i.ItemId, f.GodownId, 1m, new Money(i.Value))).ToList();
        return new Voucher(Guid.NewGuid(), f.SalesTypeId, date ?? SaleDate, lines, partyId: partyId, inventoryLines: invLines);
    }

    private static EntryLine Tax(Fx f, GstTaxHead head, int rateBp, decimal taxable, decimal amount, bool rcm = false) =>
        new(f.SalesLedgerId, new Money(amount), DrCr.Credit,
            gst: new GstLineTax(head, rateBp, new Money(taxable), isReverseCharge: rcm));

    // ================================================================ A. consignment value

    [Fact]
    public void Intra_sale_value_is_taxable_plus_cgst_plus_sgst()
    {
        var f = Build();
        // ₹40,000 @18% intra ⇒ CGST ₹3,600 + SGST ₹3,600 ⇒ consignment ₹47,200.
        var v = Movement(f, f.IntraPartyId, new[] { (f.WidgetId, 40000m) },
            new[] { Tax(f, GstTaxHead.Central, 900, 40000m, 3600m), Tax(f, GstTaxHead.State, 900, 40000m, 3600m) });
        Assert.Equal(new Money(47200m), f.Service.ConsignmentValue(v));
    }

    [Fact]
    public void Inter_sale_value_is_taxable_plus_igst()
    {
        var f = Build();
        var v = Movement(f, f.InterPartyId, new[] { (f.WidgetId, 40000m) },
            new[] { Tax(f, GstTaxHead.Integrated, 1800, 40000m, 7200m) });
        Assert.Equal(new Money(47200m), f.Service.ConsignmentValue(v));
    }

    [Fact]
    public void Strict_boundary_at_the_effective_threshold_50000_is_not_covered_and_a_paisa_more_is()
    {
        var f = Build(); // default threshold ₹50,000
        // consignment = ₹45,000 taxable + ₹2,500 CGST + ₹2,500 SGST = EXACTLY ₹50,000.
        var atBoundary = Movement(f, f.IntraPartyId, new[] { (f.WidgetId, 45000m) },
            new[] { Tax(f, GstTaxHead.Central, 900, 45000m, 2500m), Tax(f, GstTaxHead.State, 900, 45000m, 2500m) });
        Assert.Equal(new Money(50000m), f.Service.ConsignmentValue(atBoundary));
        Assert.Equal(EWayCoverage.NotRequired, f.Service.CoverageOf(atBoundary)); // ₹50,000.00 is NOT covered (STRICT >)

        // ₹50,000.01 (one paisa over) ⇒ Required.
        var overBoundary = Movement(f, f.IntraPartyId, new[] { (f.WidgetId, 45000m) },
            new[] { Tax(f, GstTaxHead.Central, 900, 45000m, 2500.01m), Tax(f, GstTaxHead.State, 900, 45000m, 2500m) });
        Assert.Equal(new Money(50000.01m), f.Service.ConsignmentValue(overBoundary));
        Assert.Equal(EWayCoverage.Required, f.Service.CoverageOf(overBoundary));
    }

    [Fact]
    public void Per_state_override_raises_or_lowers_the_effective_threshold_for_intra_state()
    {
        // consignment ₹61,000 (₹55,000 + ₹3,000 + ₹3,000).
        // Delhi-style ₹1,00,000 override ⇒ ₹61,000 ≤ ₹1,00,000 ⇒ NotRequired.
        var high = Build(overrides: new EWayStateThreshold(Guid.NewGuid(), "27", EWayTransactionType.Regular, new Money(100000m)));
        var vHigh = Movement(high, high.IntraPartyId, new[] { (high.WidgetId, 55000m) },
            new[] { Tax(high, GstTaxHead.Central, 900, 55000m, 3000m), Tax(high, GstTaxHead.State, 900, 55000m, 3000m) });
        Assert.Equal(EWayCoverage.NotRequired, high.Service.CoverageOf(vHigh));

        // WB-style ₹50,000 override ⇒ ₹61,000 > ₹50,000 ⇒ Required.
        var low = Build(overrides: new EWayStateThreshold(Guid.NewGuid(), "27", EWayTransactionType.Regular, new Money(50000m)));
        var vLow = Movement(low, low.IntraPartyId, new[] { (low.WidgetId, 55000m) },
            new[] { Tax(low, GstTaxHead.Central, 900, 55000m, 3000m), Tax(low, GstTaxHead.State, 900, 55000m, 3000m) });
        Assert.Equal(EWayCoverage.Required, low.Service.CoverageOf(vLow));
    }

    [Fact]
    public void Cess_is_date_aware_by_reading_the_posted_line_with_no_S5_date_logic()
    {
        var f = Build();
        // 20-Sep car: a cess line IS posted (pre-22-Sep) ⇒ its ₹4,000 lands in the consignment value.
        var withCess = Movement(f, f.IntraPartyId, new[] { (f.WidgetId, 40000m) }, new[]
        {
            Tax(f, GstTaxHead.Central, 900, 40000m, 3600m), Tax(f, GstTaxHead.State, 900, 40000m, 3600m),
            Tax(f, GstTaxHead.Cess, 0, 40000m, 4000m),
        }, date: new DateOnly(2025, 9, 20));
        // 25-Sep car: S1's cess compute posts NO cess line on/after 22-Sep ⇒ the value carries no cess. Same forward tax.
        var noCess = Movement(f, f.IntraPartyId, new[] { (f.WidgetId, 40000m) },
            new[] { Tax(f, GstTaxHead.Central, 900, 40000m, 3600m), Tax(f, GstTaxHead.State, 900, 40000m, 3600m) },
            date: new DateOnly(2025, 9, 25));

        Assert.Equal(new Money(51200m), f.Service.ConsignmentValue(withCess)); // 47,200 + 4,000 cess
        Assert.Equal(new Money(47200m), f.Service.ConsignmentValue(noCess));   // no cess line ⇒ excluded
    }

    [Fact]
    public void Exempt_portion_is_excluded_by_default_and_added_under_taxable_plus_exempt()
    {
        // Mixed invoice: taxable widget ₹40,000 (CGST+SGST) + exempt book ₹10,000 (no tax line).
        var def = Build(basis: EWayConsignmentBasis.Rule138Default);
        var vDef = Movement(def, def.IntraPartyId, new[] { (def.WidgetId, 40000m), (def.BookId, 10000m) },
            new[] { Tax(def, GstTaxHead.Central, 900, 40000m, 3600m), Tax(def, GstTaxHead.State, 900, 40000m, 3600m) });
        Assert.Equal(new Money(47200m), def.Service.ConsignmentValue(vDef)); // exempt ₹10,000 EXCLUDED

        var incl = Build(basis: EWayConsignmentBasis.TaxablePlusExempt);
        var vIncl = Movement(incl, incl.IntraPartyId, new[] { (incl.WidgetId, 40000m), (incl.BookId, 10000m) },
            new[] { Tax(incl, GstTaxHead.Central, 900, 40000m, 3600m), Tax(incl, GstTaxHead.State, 900, 40000m, 3600m) });
        Assert.Equal(new Money(57200m), incl.Service.ConsignmentValue(vIncl)); // + exempt ₹10,000
    }

    [Fact]
    public void Rcm_lines_never_inflate_the_forward_consignment_value()
    {
        var f = Build();
        var v = Movement(f, f.IntraPartyId, new[] { (f.WidgetId, 40000m) }, new[]
        {
            Tax(f, GstTaxHead.Central, 900, 40000m, 3600m), Tax(f, GstTaxHead.State, 900, 40000m, 3600m),
            Tax(f, GstTaxHead.Integrated, 1800, 10000m, 1800m, rcm: true), // RCM leg — excluded
        });
        Assert.Equal(new Money(47200m), f.Service.ConsignmentValue(v)); // RCM ₹10,000 + ₹1,800 NOT counted
    }

    [Fact]
    public void Composition_bill_of_supply_values_off_the_posted_stock_value_not_zero()
    {
        // A Composition dealer issues a Bill of Supply — it posts inventory/sales lines but NO forward tax lines (it
        // collects no GST). The consignment value must fall back to the posted stock/sales VALUE (finding #1); reading
        // tax lines (InvoiceTaxableValue / PostedForwardTaxTotal / PostedCessTotal) would collapse it to ₹0, CoverageOf ⇒
        // NotRequired, and PrepareRecord would throw — an entire class of registered dealers could never generate a
        // legally-required e-Way Bill.
        var comp = Build(registration: GstRegistrationType.Composition, compositionSubType: CompositionSubType.Trader);
        var bos = Movement(comp, comp.IntraPartyId, new[] { (comp.WidgetId, 200000m) }, Array.Empty<EntryLine>());
        Assert.Equal(new Money(200000m), comp.Service.ConsignmentValue(bos)); // ₹2,00,000 goods moved, NOT 0
        Assert.Equal(EWayCoverage.Required, comp.Service.CoverageOf(bos));    // > ₹50,000 ⇒ Required (pre-fix: 0 ⇒ NotRequired)

        // …and it can actually be prepared now (pre-fix PrepareRecord threw on a NotRequired coverage).
        var record = comp.Service.PrepareRecord(bos, SaleDate);
        Assert.Equal(20_000_000L, record.ConsignmentValuePaisa); // ₹2,00,000 in paisa

        // A REGULAR taxable supply keeps the tax-line path unchanged (byte-identical): ₹2,00,000 @18% ⇒ +₹36,000 tax.
        var reg = Build();
        var taxable = Movement(reg, reg.IntraPartyId, new[] { (reg.WidgetId, 200000m) },
            new[] { Tax(reg, GstTaxHead.Central, 900, 200000m, 18000m), Tax(reg, GstTaxHead.State, 900, 200000m, 18000m) });
        Assert.Equal(new Money(236000m), reg.Service.ConsignmentValue(taxable)); // 2,00,000 + 18,000 + 18,000
    }

    [Fact]
    public void Inter_state_job_work_below_threshold_is_mandatory_irrespective_of_value()
    {
        var f = Build();
        // ₹5,900 inter-state (well below ₹50,000) — but inter-state job-work ⇒ mandatory regardless of value.
        var v = Movement(f, f.InterPartyId, new[] { (f.WidgetId, 5000m) },
            new[] { Tax(f, GstTaxHead.Integrated, 1800, 5000m, 900m) });
        Assert.Equal(EWayCoverage.MandatoryIrrespectiveOfValue, f.Service.CoverageOf(v, EWayTransactionType.JobWork));
        // The same movement as a Regular supply is Not-Required (below the threshold).
        Assert.Equal(EWayCoverage.NotRequired, f.Service.CoverageOf(v, EWayTransactionType.Regular));
    }

    [Fact]
    public void Intra_state_movement_in_a_state_that_exempts_intra_state_is_not_required()
    {
        var f = Build(intraApplicable: false);
        // A big intra-state consignment (₹59,000) that would otherwise be Required.
        var v = Movement(f, f.IntraPartyId, new[] { (f.WidgetId, 50000m) },
            new[] { Tax(f, GstTaxHead.Central, 900, 50000m, 4500m), Tax(f, GstTaxHead.State, 900, 50000m, 4500m) });
        Assert.Equal(EWayCoverage.NotRequired, f.Service.CoverageOf(v));
    }

    [Fact]
    public void Coverage_is_not_applicable_for_a_pure_service_voucher_with_no_inventory_movement()
    {
        var f = Build();
        var serviceInvoice = new Voucher(Guid.NewGuid(), f.SalesTypeId, SaleDate, new List<EntryLine>
        {
            new(f.IntraPartyId, new Money(59000m), DrCr.Debit),
            new(f.SalesLedgerId, new Money(50000m), DrCr.Credit),
            Tax(f, GstTaxHead.Central, 900, 50000m, 4500m), Tax(f, GstTaxHead.State, 900, 50000m, 4500m),
        }, partyId: f.IntraPartyId); // NO inventory lines
        Assert.Equal(EWayCoverage.NotApplicable, f.Service.CoverageOf(serviceInvoice));
    }

    // ================================================================ C. lifecycle

    [Fact]
    public void Part_b_is_relaxed_for_intra_state_up_to_50km_and_mandatory_otherwise()
    {
        var f = Build();
        var intra = Required(f, f.IntraPartyId);
        var rIntra = f.Service.PrepareRecord(intra, SaleDate);

        // Intra + ≤ 50 km ⇒ submittable with NO Part-B.
        f.Service.SetPartB(rIntra, transporterId: null, mode: null, vehicleNumber: null, distanceKm: 50);
        f.Service.EnsureReadyToGenerate(rIntra); // does not throw

        // > 50 km intra ⇒ Part-B mandatory (throws without a mode/vehicle/doc).
        f.Service.SetPartB(rIntra, transporterId: null, mode: null, vehicleNumber: null, distanceKm: 51);
        Assert.Throws<InvalidOperationException>(() => f.Service.EnsureReadyToGenerate(rIntra));

        // Inter-state ⇒ Part-B mandatory regardless of distance.
        var inter = Required(f, f.InterPartyId);
        var rInter = f.Service.PrepareRecord(inter, SaleDate);
        f.Service.SetPartB(rInter, transporterId: null, mode: null, vehicleNumber: null, distanceKm: 10);
        Assert.Throws<InvalidOperationException>(() => f.Service.EnsureReadyToGenerate(rInter));
        // Supplying Part-B satisfies it.
        f.Service.SetPartB(rInter, "TRANSIN01", EWayTransportMode.Road, "MH12AB1234", 10);
        f.Service.EnsureReadyToGenerate(rInter);
    }

    [Fact]
    public void Cancel_within_24h_succeeds_and_after_throws_and_72h_action_window_is_advisory()
    {
        var f = Build();
        var v = Required(f, f.IntraPartyId);
        var record = f.Service.PrepareRecord(v, SaleDate);
        var gen = new DateTimeOffset(2025, 4, 10, 9, 0, 0, TimeSpan.FromHours(5.5));
        f.Service.RecordPortalResponse(record, "231000000123", gen, EWayValidity.ValidUpto(gen, 100, false));

        // 72-h other-party action window is advisory (a pure computation; no auto-transition).
        Assert.True(f.Service.IsOtherPartyActionWindowOpen(record, gen.AddHours(71)));
        Assert.False(f.Service.IsOtherPartyActionWindowOpen(record, gen.AddHours(73)));
        Assert.Equal(EWayStatus.Generated, record.Status); // still Generated — no auto-transition

        // Cancel at gen-date + 1 day succeeds; a second (fresh) record cancelled at + 2 days throws.
        var other = f.Service.PrepareRecord(Required(f, f.InterPartyId, "TRANSIN", EWayTransportMode.Road, "MH01X1"), SaleDate);
        f.Service.RecordPortalResponse(other, "231000000999", gen, EWayValidity.ValidUpto(gen, 100, false));
        Assert.Throws<InvalidOperationException>(() => f.Service.Cancel(other, SaleDate.AddDays(2), "2"));

        f.Service.Cancel(record, SaleDate.AddDays(1), "2");
        Assert.Equal(EWayStatus.Cancelled, record.Status);
    }

    [Fact]
    public void Consolidated_ewb02_references_generated_children_and_recomputes_nothing()
    {
        var f = Build();
        var gen = new DateTimeOffset(2025, 4, 10, 9, 0, 0, TimeSpan.FromHours(5.5));
        var c1 = f.Service.PrepareRecord(Required(f, f.IntraPartyId), SaleDate);
        f.Service.RecordPortalResponse(c1, "231000000001", gen, EWayValidity.ValidUpto(gen, 100, false));
        var c2 = f.Service.PrepareRecord(Required(f, f.InterPartyId, "T", EWayTransportMode.Road, "MH02Y2"), SaleDate);
        f.Service.RecordPortalResponse(c2, "231000000002", gen, EWayValidity.ValidUpto(gen, 100, false));

        var cewb = f.Service.PrepareConsolidated(new[] { c1, c2 }, EWayTransportMode.Road, "MH01Z9999", "27");
        Assert.Equal(new[] { "231000000001", "231000000002" }, cewb.ChildEwbNumbers);

        // A not-yet-generated child is refused (no value recomputation, generated-only).
        var pending = f.Service.PrepareRecord(Required(f, f.IntraPartyId), SaleDate);
        Assert.Throws<InvalidOperationException>(() =>
            f.Service.PrepareConsolidated(new[] { c1, pending }, EWayTransportMode.Road, "MH01Z9999", "27"));
    }

    [Fact]
    public void Ship_to_gstin_is_mandatory_only_from_1_aug_2026()
    {
        var f = Build();
        // Before 01-Aug-2026 ⇒ Ship-To GSTIN optional/inert.
        var early = Required(f, f.IntraPartyId, date: new DateOnly(2026, 7, 31));
        var rEarly = f.Service.PrepareRecord(early, new DateOnly(2026, 7, 31));
        Assert.Null(rEarly.ShipToGstin);

        // On/after 01-Aug-2026 ⇒ a movement with no Ship-To GSTIN is refused.
        var late = Required(f, f.InterPartyId, "T", EWayTransportMode.Road, "MH03A", date: new DateOnly(2026, 8, 1));
        Assert.Throws<InvalidOperationException>(() => f.Service.PrepareRecord(late, new DateOnly(2026, 8, 1)));
        // Supplying it succeeds.
        var late2 = Required(f, f.InterPartyId, "T", EWayTransportMode.Road, "MH03A", date: new DateOnly(2026, 8, 1));
        var rLate = f.Service.PrepareRecord(late2, new DateOnly(2026, 8, 1), shipToGstin: "24AAACC1206D1ZM");
        Assert.Equal("24AAACC1206D1ZM", rLate.ShipToGstin);
    }

    [Fact]
    public void Base_document_older_than_180_days_is_refused()
    {
        var f = Build();
        var old = Required(f, f.IntraPartyId, date: new DateOnly(2025, 1, 1));
        Assert.Throws<InvalidOperationException>(() => f.Service.PrepareRecord(old, new DateOnly(2025, 7, 15))); // > 180 d
    }

    // ================================================================ D. ER-5 twin (EWB number never local)

    [Fact]
    public void Ewb_number_is_inbound_only_and_never_derived_locally()
    {
        var f = Build();
        var record = f.Service.PrepareRecord(Required(f, f.IntraPartyId), SaleDate);
        Assert.Equal(EWayStatus.Pending, record.Status);
        Assert.Null(record.EwbNumber);
        Assert.Null(record.ValidUpto);

        var gen = new DateTimeOffset(2025, 4, 10, 9, 0, 0, TimeSpan.FromHours(5.5));
        f.Service.RecordPortalResponse(record, "231000000123", gen, gen.AddDays(1));
        Assert.Equal(EWayStatus.Generated, record.Status);
        Assert.Equal("231000000123", record.EwbNumber);

        // Structural (ER-5 twin): no method on the engine / record derives an EWB number or a validity.
        foreach (var t in new[] { typeof(EWayBillService), typeof(EWayBillRecord) })
            foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                var name = m.Name.ToLowerInvariant();
                Assert.False(name.Contains("generate") && name.Contains("number"),
                    $"{t.Name}.{m.Name} looks like it derives an EWB number — ER-5 forbids local generation.");
                Assert.DoesNotContain("computeewb", name);
            }

        // Rehydrating a Generated record with no EWB number / validity throws (all-or-nothing on import).
        Assert.Throws<ArgumentException>(() => EWayBillRecord.Rehydrate(
            Guid.NewGuid(), Guid.NewGuid(), "INV1", EWayStatus.Generated, "Outward", "Supply", "INV", 5_900_000,
            null, null, null, 0, null, "27", "24", false, null, false, null,
            ewbNumber: null, generatedAt: gen, validUpto: gen.AddDays(1), null, null));
    }

    [Fact]
    public void Coverage_and_value_do_not_depend_on_the_connector_mode()
    {
        var offline = Build();
        offline.Company.Gst!.ConnectorMode = GstConnectorMode.OfflineJson;
        var live = Build();
        live.Company.Gst!.ConnectorMode = GstConnectorMode.CustomerNicDirect;

        var vOff = Required(offline, offline.IntraPartyId);
        var vLive = Required(live, live.IntraPartyId);
        Assert.Equal(offline.Service.ConsignmentValue(vOff), live.Service.ConsignmentValue(vLive));
        Assert.Equal(offline.Service.CoverageOf(vOff), live.Service.CoverageOf(vLive));
    }

    // A Required (₹59,000) covered movement, optionally with Part-B pre-filled and a chosen date.
    private static Voucher Required(
        Fx f, Guid partyId, string? transporterId = null, EWayTransportMode? mode = null, string? vehicle = null,
        DateOnly? date = null)
    {
        _ = transporterId; _ = mode; _ = vehicle; // Part-B is set on the record, not the voucher; params keep call-sites readable
        return Movement(f, partyId, new[] { (f.WidgetId, 50000m) },
            new[] { Tax(f, GstTaxHead.Central, 900, 50000m, 4500m), Tax(f, GstTaxHead.State, 900, 50000m, 4500m) }, date);
    }
}
