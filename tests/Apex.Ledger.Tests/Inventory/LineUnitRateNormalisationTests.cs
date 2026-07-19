using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests.Inventory;

/// <summary>
/// WI-10 slice C follow-up — <b>the rate must be normalised at EVERY site that multiplies a base-normalised
/// quantity by it</b> (adversarial-review defects 1–3).
///
/// <para>The semantic (locked by <see cref="Unit.RateInBaseMeasure"/> and the corpus): a line's rate is PER
/// THE UNIT THE LINE IS STATED IN. "2 Doz @ ₹10" is ₹20 — 24 Nos of stock worth ₹20, i.e. ₹0.8333…/Nos.
/// A site that pairs <see cref="Unit.QuantityInBaseMeasure"/> (24) with the RAW rate (₹10) reports ₹240 —
/// exactly the conversion factor (12×) too much. Three such sites were missed by the original slice:</para>
/// <list type="bullet">
///   <item><b>D1</b> — <see cref="NegativeStock"/>'s reference unit cost (money).</item>
///   <item><b>D2</b> — <see cref="BatchStockService"/>'s per-batch unit cost (money).</item>
///   <item><b>D3</b> — the rate COLUMN on <see cref="InventoryRegisters"/>, <see cref="JobWorkReports"/> and
///     <see cref="InventoryMovements"/> (presentation of money: a per-displayed-unit rate printed beside a
///     base-unit quantity, so rate × qty ≠ the value in the next column).</item>
/// </list>
/// Every case below uses the same deterministic seed the reviewer specified: a Nos-measured item, a
/// Doz-Nos compound (1 Doz = 12 Nos), and a line entered as "2 Doz @ ₹10".
/// </summary>
public class LineUnitRateNormalisationTests
{
    private static readonly DateOnly Start = new(2024, 4, 1);
    private static readonly DateOnly D1 = new(2024, 4, 10);
    private static readonly DateOnly D2 = new(2024, 4, 20);
    private static readonly DateOnly AsOf = new(2024, 4, 30);

    private sealed class Kit
    {
        public required Company Company { get; init; }
        public required InventoryService Masters { get; init; }
        public required Guid ItemId { get; init; }
        public required Guid GodownId { get; init; }
        public required Unit DozNos { get; init; }
        public required Unit Nos { get; init; }
    }

    /// <summary>A Nos-measured item plus a Doz-Nos compound unit (1 Doz = 12 Nos). No opening, no cost.</summary>
    private static Kit NewKit(string name)
    {
        var c = CompanyFactory.CreateSeeded(name, Start);
        var masters = new InventoryService(c);
        var grp = masters.CreateStockGroup("Goods");
        var nos = masters.CreateSimpleUnit("Nos", "Numbers");
        var doz = masters.CreateSimpleUnit("Doz", "Dozens");
        var dozNos = masters.CreateCompoundUnit("Doz-Nos", "Dozen of 12 Numbers", doz.Id, nos.Id, 12);
        var item = masters.CreateStockItem("Egg", grp.Id, nos.Id);
        return new Kit
        {
            Company = c,
            Masters = masters,
            ItemId = item.Id,
            GodownId = c.MainLocation!.Id,
            DozNos = dozNos,
            Nos = nos,
        };
    }

    private static Guid TypeId(Company c, VoucherBaseType baseType) =>
        c.VoucherTypes.First(t => t.BaseType == baseType).Id;

    // ============================================================ D1 — NegativeStock reference unit cost

    /// <summary>
    /// A rated inward of "2 Doz @ ₹10" (= 24 Nos worth ₹20, i.e. ₹0.8333…/Nos) followed by an oversell to
    /// −12 Nos must value the shortfall at <b>−₹10.00</b> (12 × ₹0.8333…). Pairing the base quantity with the
    /// raw per-Dozen rate reports <b>−₹120.00</b> — 12× the truth.
    /// </summary>
    [Fact]
    public void D1_negative_stock_values_a_shortfall_at_the_per_base_unit_rate_not_the_per_dozen_rate()
    {
        var kit = NewKit("Neg Unit Co");
        var c = kit.Company;

        // Inward 2 Doz-Nos @ ₹10 per Dozen  ⇒  24 Nos on hand, worth ₹20.00.
        c.AddInventoryVoucher(new InventoryVoucher(
            Guid.NewGuid(), TypeId(c, VoucherBaseType.ReceiptNote), D1,
            new[]
            {
                new InventoryAllocation(kit.ItemId, kit.GodownId, 2m, StockDirection.Inward,
                    Money.FromRupees(10m), null, kit.DozNos.Id),
            }));

        // Oversell 36 Nos ⇒ on-hand −12 Nos (appended raw, bypassing the no-negative posting guard exactly as
        // the existing exception-report tests do — a real file carries negatives via imports/config).
        c.AddInventoryVoucher(new InventoryVoucher(
            Guid.NewGuid(), TypeId(c, VoucherBaseType.DeliveryNote), D2,
            new[] { new InventoryAllocation(kit.ItemId, kit.GodownId, 36m, StockDirection.Outward) }));

        var row = Assert.Single(NegativeStock.Build(c, AsOf).Rows);

        Assert.Equal(-12m, row.Quantity);                 // the quantity is (correctly) in BASE units
        Assert.Equal(-10.00m, row.Value.Amount);          // −12 Nos × ₹0.8333…/Nos
        Assert.NotEqual(-120.00m, row.Value.Amount);      // the 12× defect
    }

    // ============================================================ D2 — BatchStockService per-batch cost

    /// <summary>
    /// A batch inward of "2 Doz @ ₹10" must value the batch at ₹20.00 (24 Nos × ₹0.8333…/Nos). Pairing the
    /// base quantity with the raw per-Dozen rate reports ₹240.00 — 12× the truth.
    /// </summary>
    [Fact]
    public void D2_batch_unit_cost_is_per_base_unit_so_a_dozen_priced_batch_values_at_the_line_total()
    {
        var kit = NewKit("Batch Unit Co");
        var c = kit.Company;

        // A rated batch inward stated in DOZENS: 2 Doz-Nos @ ₹10 into batch "B1".
        c.AddInventoryVoucher(new InventoryVoucher(
            Guid.NewGuid(), TypeId(c, VoucherBaseType.ReceiptNote), D1,
            new[]
            {
                new InventoryAllocation(kit.ItemId, kit.GodownId, 2m, StockDirection.Inward,
                    Money.FromRupees(10m), "B1", kit.DozNos.Id),
            }));

        var batch = Assert.Single(new BatchStockService(c).BatchOnHands(kit.ItemId, AsOf));

        Assert.Equal("B1", batch.Batch);
        Assert.Equal(24m, batch.Quantity);                       // base units — 2 Doz = 24 Nos
        Assert.Equal(20.00m, batch.Value.Amount);                // 24 × ₹0.8333…/Nos
        Assert.NotEqual(240.00m, batch.Value.Amount);            // the 12× defect
    }

    // ============================================================ D3 — the rate COLUMN in three reports

    /// <summary>
    /// The rate column must be expressed in the SAME unit as the quantity column beside it (base units), so a
    /// reader can multiply the two and land on the value column. A per-Dozen ₹10 printed beside 24 Nos claims
    /// ₹240 against a ₹20 value line.
    /// </summary>
    [Fact]
    public void D3_inventory_register_rate_column_agrees_with_its_base_unit_quantity_and_value()
    {
        var kit = NewKit("Register Unit Co");
        var c = kit.Company;

        c.AddInventoryVoucher(new InventoryVoucher(
            Guid.NewGuid(), TypeId(c, VoucherBaseType.ReceiptNote), D1,
            new[]
            {
                new InventoryAllocation(kit.ItemId, kit.GodownId, 2m, StockDirection.Inward,
                    Money.FromRupees(10m), null, kit.DozNos.Id),
            }));

        var row = Assert.Single(InventoryRegisters.BuildReceiptNotes(c, Start, AsOf));

        Assert.Equal(24m, row.Quantity);                 // base units
        Assert.Equal(20.00m, row.Value.Amount);          // the line total
        // The defect: Rate was the raw ₹10.00 per DOZEN printed beside a quantity of 24 NOS.
        Assert.NotEqual(10.00m, row.Rate!.Value.Amount);
        // Rate × Quantity must reconcile to the Value column to the paisa.
        Assert.Equal(row.Value.Amount,
            decimal.Round(row.Rate!.Value.Amount * row.Quantity, 2, MidpointRounding.AwayFromZero));
    }

    /// <summary>
    /// Same invariant on the public Stock-Item Movement journal, which surfaces the shared
    /// <c>InventoryMovements</c> rate straight into its Rate column beside a base-unit inward quantity.
    /// </summary>
    [Fact]
    public void D3_stock_item_movement_rate_column_agrees_with_its_base_unit_quantity()
    {
        var kit = NewKit("Movement Unit Co");
        var c = kit.Company;

        c.AddInventoryVoucher(new InventoryVoucher(
            Guid.NewGuid(), TypeId(c, VoucherBaseType.ReceiptNote), D1,
            new[]
            {
                new InventoryAllocation(kit.ItemId, kit.GodownId, 2m, StockDirection.Inward,
                    Money.FromRupees(10m), null, kit.DozNos.Id),
            }));

        var row = Assert.Single(StockItemMovement.Build(c, kit.ItemId, AsOf, Start).Rows);

        Assert.Equal(24m, row.InwardQuantity);
        Assert.NotEqual(10.00m, row.Rate!.Value.Amount);          // the 12× defect
        Assert.Equal(20.00m,
            decimal.Round(row.Rate!.Value.Amount * row.InwardQuantity, 2, MidpointRounding.AwayFromZero));
    }

    /// <summary>
    /// Same invariant on the Job-Work material register, the third report the review named.
    /// </summary>
    [Fact]
    public void D3_job_work_material_register_rate_column_agrees_with_its_base_unit_quantity()
    {
        var kit = NewKit("Job Work Unit Co");
        var c = kit.Company;

        // A Material-Out movement stated in DOZENS at ₹10 per dozen.
        c.AddInventoryVoucher(new InventoryVoucher(
            Guid.NewGuid(), TypeId(c, VoucherBaseType.MaterialOut), D1,
            new[]
            {
                new InventoryAllocation(kit.ItemId, kit.GodownId, 2m, StockDirection.Outward,
                    Money.FromRupees(10m), null, kit.DozNos.Id),
            }));

        var row = Assert.Single(JobWorkReports.BuildMaterialOutRegister(c, Start, AsOf));

        Assert.Equal(24m, row.Quantity);
        Assert.Equal(20.00m, row.Value.Amount);
        Assert.NotEqual(10.00m, row.Rate!.Value.Amount);          // the 12× defect
        Assert.Equal(row.Value.Amount,
            decimal.Round(row.Rate!.Value.Amount * row.Quantity, 2, MidpointRounding.AwayFromZero));
    }

    // ============================================================ D4 — job-work material movement (the INVERSE error)

    /// <summary>
    /// <b>D4 — the mirror-image of D1/D2/D3, introduced by the D3 fix itself.</b> A site is correct only when the
    /// QUANTITY and the RATE are expressed in the SAME unit, and there are TWO ways to break that — both of which
    /// misstate money by exactly the conversion factor:
    /// <list type="bullet">
    ///   <item><b>(a)</b> a base-normalised quantity (24 Nos) paired with a per-DISPLAYED-unit rate (₹10/Doz) —
    ///     a 12× <b>OVER</b>statement. That was D1/D2/D3.</item>
    ///   <item><b>(b)</b> a per-displayed-unit quantity (2 Doz) paired with a per-BASE rate, or equivalently a
    ///     rate that is converted TWICE — a 12× <b>UNDER</b>statement. That is D4.</item>
    /// </list>
    /// <para><see cref="JobWorkService.BuildMaterialOutTransfer"/> re-priced its outward lines at the live per-BASE
    /// issue rate while keeping the line's DISPLAYED unit id, so the register — which correctly converts a line's
    /// rate to per-base before pairing it with the base quantity (D3) — divided by the factor a SECOND time. Goods
    /// worth <b>₹24.00</b> sent to a third-party job worker were reported at <b>₹2.00</b>, and the documented
    /// value-neutrality invariant (a pure location move changes no company total, so goods-out value must equal
    /// goods-in value) silently broke: ₹2.00 out against ₹24.00 in.</para>
    /// </summary>
    [Fact]
    public void D4_job_work_material_out_reports_goods_sent_at_the_line_total_not_a_twice_converted_rate()
    {
        var kit = NewKit("Job Work Unit Co");
        var c = kit.Company;
        new JobWorkService(c).SetEnabled(true);

        // 100 Nos on hand at ₹1.00/Nos, so 24 Nos issue at exactly ₹24.00 (no rounding anywhere).
        kit.Masters.AddOpeningBalance(kit.ItemId, kit.GodownId, 100m, Money.FromRupees(1m));
        var worker = kit.Masters.CreateGodown("Worker Site", thirdParty: true);

        // Send "2 Doz-Nos" to the job worker = 24 Nos, truth ₹24.00.
        var transfer = new JobWorkService(c).BuildMaterialOutTransfer(
            TypeId(c, VoucherBaseType.MaterialOut), D1,
            source: new[]
            {
                new InventoryAllocation(kit.ItemId, kit.GodownId, 2m, StockDirection.Outward,
                    Money.FromRupees(10m), null, kit.DozNos.Id),
            },
            destination: new[]
            {
                new InventoryAllocation(kit.ItemId, worker.Id, 2m, StockDirection.Inward,
                    Money.FromRupees(10m), null, kit.DozNos.Id),
            });
        new InventoryPostingService(c).Post(transfer);

        var reg = JobWorkReports.BuildMaterialOutRegister(c, Start, AsOf);
        var outRow = Assert.Single(reg, r => r.Direction == StockDirection.Outward);
        var inRow = Assert.Single(reg, r => r.Direction == StockDirection.Inward);

        Assert.Equal(24m, outRow.Quantity);                 // base units on both sides
        Assert.Equal(24m, inRow.Quantity);

        Assert.Equal(24.00m, outRow.Value.Amount);          // the value actually issued
        Assert.NotEqual(2.00m, outRow.Value.Amount);        // the 12× UNDERstatement (D4)

        // Rate × Quantity must foot to Value on the outward leg too (the D3 column rule).
        Assert.Equal(outRow.Value.Amount,
            decimal.Round(outRow.Rate!.Value.Amount * outRow.Quantity, 2, MidpointRounding.AwayFromZero));

        // VALUE NEUTRALITY — a pure location move to a third-party godown changes no company total.
        Assert.Equal(outRow.Value.Amount, inRow.Value.Amount);
    }

    /// <summary>
    /// The same value-neutrality invariant proven at the COMPANY level rather than in the register: a compound-unit
    /// Material Out transfer to a third-party godown must leave total Stock-in-Hand untouched (the goods are still
    /// ours, merely at the worker's site). This is the invariant D4 broke.
    /// </summary>
    [Fact]
    public void D4_compound_unit_material_out_transfer_is_value_neutral_for_total_stock_in_hand()
    {
        var kit = NewKit("Job Work Neutral Co");
        var c = kit.Company;
        new JobWorkService(c).SetEnabled(true);

        kit.Masters.AddOpeningBalance(kit.ItemId, kit.GodownId, 100m, Money.FromRupees(1m));
        var worker = kit.Masters.CreateGodown("Worker Site", thirdParty: true);

        var valuation = new StockValuationService(c);
        var before = valuation.ClosingValue(kit.ItemId, AsOf).Value.Amount;

        var transfer = new JobWorkService(c).BuildMaterialOutTransfer(
            TypeId(c, VoucherBaseType.MaterialOut), D1,
            source: new[]
            {
                new InventoryAllocation(kit.ItemId, kit.GodownId, 2m, StockDirection.Outward,
                    Money.FromRupees(10m), null, kit.DozNos.Id),
            },
            destination: new[]
            {
                new InventoryAllocation(kit.ItemId, worker.Id, 2m, StockDirection.Inward,
                    Money.FromRupees(10m), null, kit.DozNos.Id),
            });
        new InventoryPostingService(c).Post(transfer);

        Assert.Equal(100.00m, before);
        Assert.Equal(before, new StockValuationService(c).ClosingValue(kit.ItemId, AsOf).Value.Amount);
    }

    /// <summary>
    /// The SAME D4 error at the other named site — <see cref="JobWorkService.BuildConsumingMaterialIn"/>, which
    /// re-prices its CONSUMPTION (outward) lines from the live per-base issue rate. Consuming "2 Doz-Nos" of a
    /// component held at ₹1.00/Nos consumes ₹24.00 of stock; the Material In register must say so, not ₹2.00.
    /// The finished good is valued from that same consumed cost, so the two legs must reconcile.
    /// </summary>
    [Fact]
    public void D4_consuming_material_in_reports_consumed_components_at_the_line_total_not_a_twice_converted_rate()
    {
        var kit = NewKit("Job Work Consume Co");
        var c = kit.Company;
        new JobWorkService(c).SetEnabled(true);

        kit.Masters.AddOpeningBalance(kit.ItemId, kit.GodownId, 100m, Money.FromRupees(1m));
        var fg = kit.Masters.CreateStockItem("Omelette", c.StockGroups.First().Id, kit.Nos.Id);

        var voucher = new JobWorkService(c).BuildConsumingMaterialIn(
            TypeId(c, VoucherBaseType.MaterialIn), D1,
            consume: new[]
            {
                new InventoryAllocation(kit.ItemId, kit.GodownId, 2m, StockDirection.Outward,
                    Money.FromRupees(10m), null, kit.DozNos.Id),
            },
            produce: new[]
            {
                new InventoryAllocation(fg.Id, kit.GodownId, 24m, StockDirection.Inward),
            });
        new InventoryPostingService(c).Post(voucher);

        var reg = JobWorkReports.BuildMaterialInRegister(c, Start, AsOf);
        var consumed = Assert.Single(reg, r => r.Direction == StockDirection.Outward);
        var produced = Assert.Single(reg, r => r.Direction == StockDirection.Inward);

        Assert.Equal(24m, consumed.Quantity);               // 2 Doz normalised to 24 Nos
        Assert.Equal(24.00m, consumed.Value.Amount);        // the value actually consumed
        Assert.NotEqual(2.00m, consumed.Value.Amount);      // the 12x UNDERstatement (D4)

        // Rate x Quantity foots to Value on the consumption leg.
        Assert.Equal(consumed.Value.Amount,
            decimal.Round(consumed.Rate!.Value.Amount * consumed.Quantity, 2, MidpointRounding.AwayFromZero));

        // The finished good absorbs exactly the consumed cost (no phantom stock value).
        Assert.Equal(consumed.Value.Amount, produced.Value.Amount);
    }
}
