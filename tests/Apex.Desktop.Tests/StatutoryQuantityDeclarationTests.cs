using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Io;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.Tests;

/// <summary>
/// WI-10 Gap 2 <b>follow-on — the STATUTORY QUANTITY DECLARATION</b>.
///
/// <para><b>The defect.</b> Before item-invoice line units a line's quantity was always in the item's base unit,
/// so labelling it with the item's base UQC was correct by construction. Once a line may state "2 Doz" of a
/// Nos-measured item, three writers still emitted the LINE quantity beside the BASE UQC and therefore declared
/// <b>2 NOS</b> for a supply in which <b>24 Nos</b> physically moved: <c>Gstr1</c>'s Table-12 HSN summary (a FILED
/// field), <c>EInvoiceJson</c>'s INV-01, and <c>EWayBillJson</c>'s EWB-01 (a quantity a checkpoint verifies).</para>
///
/// <para><b>Why nothing caught it.</b> The taxable value is correct at all three — ₹20.00 either way — so no money
/// assertion could fail. The slice had already fixed this exact pattern in PRINT, which is what made the state
/// indefensible: for one voucher the printed invoice said "2 Doz-Nos" while the e-invoice said "2 NOS".</para>
///
/// <para><b>The test that matters</b> is
/// <see cref="Print_einvoice_eway_and_gstr1_all_declare_the_same_physical_quantity_for_one_voucher"/>: FOUR
/// documents built from ONE posted voucher, asserted together. It is the only assertion that can fail when two
/// documents drift apart, which is precisely how this defect survived.</para>
///
/// Drives the real shell + entry view models over a throwaway <c>.db</c> — no UI toolkit, no rendering.
/// </summary>
public sealed class StatutoryQuantityDeclarationTests : IDisposable
{
    private const string GstinMaharashtra = "27AAPFU0939F1ZV";
    private static readonly DateOnly FyStart = new(2024, 4, 1);

    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public StatutoryQuantityDeclarationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexUqcTests_" + Guid.NewGuid().ToString("N"));
        _storage = new CompanyStorage(_tempDir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    // ---------------------------------------------------------------- scaffolding

    private sealed class Kit
    {
        public required MainWindowViewModel Vm { get; init; }
        public required Guid EggId { get; init; }
        public required Guid MainGodownId { get; init; }
        public required Guid CustomerId { get; init; }
        public required Guid NosId { get; init; }
        public required Guid DozNosId { get; init; }
        /// <summary>1 Crate = 12 Nos, where "Crate" carries NO UQC — the fallback path.</summary>
        public required Guid CrateNosId { get; init; }
        /// <summary>1 Pallet = 7 Nos, where "Pallet" ALSO carries no UQC — a SECOND unmapped unit, so two lines
        /// can collide on the synthesized "OTH" label while counting different things.</summary>
        public required Guid PalletNosId { get; init; }
        /// <summary>1 Case = 3 Nos, where "Case" is LEGITIMATELY mapped to the published code "OTH" — the
        /// preferred path, colliding with the fallback path's synthesized "OTH".</summary>
        public required Guid CaseNosId { get; init; }
        /// <summary>1 Dz = 12 Nos — a SECOND unit master mapped to the real code "DOZ", so two distinct masters
        /// share one genuine (non-catch-all) label and remain commensurable.</summary>
        public required Guid DzNosId { get; init; }
        /// <summary>A Kgs-measured item sharing Egg's HSN — the degrade target's own known limitation.</summary>
        public required Guid RiceId { get; init; }
    }

    /// <summary>
    /// A GST + e-Way enabled (home Maharashtra 27) company holding one Nos-measured item ("Egg") with two
    /// 12-per compound units — <b>Doz-Nos</b>, whose first unit maps to the valid UQC "DOZ", and
    /// <b>Crate-Nos</b>, whose first unit maps to nothing — plus stock, a Sales ledger and an in-state customer.
    /// The e-Way threshold is dropped to ₹1 so the SAME ₹20.00 invoice the money assertions use is also a covered
    /// consignment; otherwise the cross-document test would have to inflate the value and lose the ₹20.00 anchor.
    /// </summary>
    private Kit NewKit(string companyName)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = companyName;
        vm.CreateCompany();
        Assert.Equal(Screen.Gateway, vm.CurrentScreen);

        var c = vm.Company!;
        c.FinancialYearStart = FyStart;
        c.BooksBeginFrom = FyStart;

        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = "27",
            Gstin = GstinMaharashtra,
            RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart,
            Periodicity = GstReturnPeriodicity.Monthly,
            EWayBillEnabled = true,
            EWayApplicableFrom = FyStart,
            EWayThreshold = new Money(1m),
        });

        var inv = new InventoryService(c);
        var grp = inv.CreateStockGroup("Goods");
        var nos = inv.CreateSimpleUnit("Nos", "Numbers", unitQuantityCode: "NOS");
        var doz = inv.CreateSimpleUnit("Doz", "Dozens", unitQuantityCode: "DOZ");
        // Deliberately UQC-less: the department's list has no code for a crate, which is the real-world reason
        // the fallback branch has to exist at all.
        var crate = inv.CreateSimpleUnit("Crate", "Crates");
        // A SECOND UQC-less unit. One unmapped unit is enough to reach the "OTH" fallback; two are needed to
        // show that the fallback's label does not distinguish them.
        var pallet = inv.CreateSimpleUnit("Pallet", "Pallets");
        // Legitimately mapped BY THE USER to a code the department publishes — the preferred path, not a fallback.
        var caseUnit = inv.CreateSimpleUnit("Case", "Cases", unitQuantityCode: "OTH");
        // A second master carrying the SAME real code as "Doz".
        var dz = inv.CreateSimpleUnit("Dz", "Dozens (alt)", unitQuantityCode: "DOZ");
        var dozNos = inv.CreateCompoundUnit("Doz-Nos", "Dozen of 12 Numbers", doz.Id, nos.Id, 12);
        var crateNos = inv.CreateCompoundUnit("Crate-Nos", "Crate of 12 Numbers", crate.Id, nos.Id, 12);
        var palletNos = inv.CreateCompoundUnit("Pallet-Nos", "Pallet of 7 Numbers", pallet.Id, nos.Id, 7);
        var caseNos = inv.CreateCompoundUnit("Case-Nos", "Case of 3 Numbers", caseUnit.Id, nos.Id, 3);
        var dzNos = inv.CreateCompoundUnit("Dz-Nos", "Dozen of 12 Numbers (alt)", dz.Id, nos.Id, 12);
        var kgs = inv.CreateSimpleUnit("Kgs", "Kilograms", unitQuantityCode: "KGS");
        var main = c.MainLocation!.Id;

        var egg = inv.CreateStockItem("Egg", grp.Id, nos.Id);
        egg.Gst = new StockItemGstDetails
        {
            HsnSac = "040721", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800,
        };
        inv.AddOpeningBalance(egg.Id, main, 240m, Money.FromRupees(1m));

        // Same HSN as Egg, measured in an UNRELATED base unit. A master only; no test sells it unless it is
        // characterizing the degrade target, so every existing single-row expectation is untouched.
        var rice = inv.CreateStockItem("Rice", grp.Id, kgs.Id);
        rice.Gst = new StockItemGstDetails
        {
            HsnSac = "040721", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800,
        };
        inv.AddOpeningBalance(rice.Id, main, 240m, Money.FromRupees(1m));

        AddLedger(c, "Sales", "Sales Accounts");
        var customer = AddLedger(c, "Local Customer", "Sundry Debtors");
        customer.PartyGst = new PartyGstDetails
        {
            RegistrationType = GstRegistrationType.Regular, Gstin = GstinMaharashtra, StateCode = "27",
        };

        _storage.Save(c);

        return new Kit
        {
            Vm = vm, EggId = egg.Id, MainGodownId = main, CustomerId = customer.Id,
            NosId = nos.Id, DozNosId = dozNos.Id, CrateNosId = crateNos.Id,
            PalletNosId = palletNos.Id, CaseNosId = caseNos.Id, DzNosId = dzNos.Id, RiceId = rice.Id,
        };
    }

    private static DomainLedger AddLedger(Company c, string name, string groupName)
    {
        var group = c.FindGroupByName(groupName) ?? throw new InvalidOperationException($"No group '{groupName}'.");
        var ledger = new DomainLedger(Guid.NewGuid(), name, group.Id, Money.Zero, openingIsDebit: false);
        c.AddLedger(ledger);
        return ledger;
    }

    /// <summary>Walks Gateway → Vouchers → Sales → Ctrl+I on the REAL menu, returning the live entry VM.</summary>
    private static VoucherEntryViewModel DriveToSalesItemInvoice(MainWindowViewModel vm)
    {
        vm.ShowVouchersMenu();
        var target = -1;
        for (var i = 0; i < vm.Menu.Count; i++)
            if (vm.Menu[i].IsSelectable && vm.Menu[i].Label == "Sales") { target = i; break; }
        Assert.True(target >= 0,
            "\"Sales\" is not a selectable row in the Vouchers menu — the screen is UNREACHABLE. "
            + $"Rows: {string.Join(" | ", vm.Menu.Where(m => m.IsSelectable).Select(m => m.Label))}");

        var guard = 0;
        while (vm.SelectedIndex != target)
        {
            vm.MoveDown();
            Assert.True(++guard <= vm.Menu.Count * 2, "Could not arrow onto \"Sales\".");
        }
        vm.ActivateSelected();
        Assert.Equal(Screen.VoucherEntry, vm.CurrentScreen);

        vm.ToggleItemInvoice();                       // Ctrl+I
        var entry = vm.VoucherEntry!;
        Assert.True(entry.IsItemInvoice);
        return entry;
    }

    /// <summary>
    /// Enters and accepts a one-line item invoice DATED <paramref name="date"/>, so a test can place supplies in
    /// DIFFERENT return periods. Returns nothing: the callers that need multiple vouchers identify them by date.
    /// </summary>
    private static void PostOneLineOn(Kit k, Guid? unitId, string quantity, string rate, DateOnly date)
    {
        var entry = DriveToSalesItemInvoice(k.Vm);
        entry.DateText = date.ToString("dd-MM-yyyy");
        entry.SelectedParty = entry.Parties.Single(p => p.Ledger?.Id == k.CustomerId);

        var line = entry.InventoryLines.First();
        line.SelectedItem = entry.StockItems.Single(i => i.Id == k.EggId);
        line.SelectedGodown = entry.Godowns.Single(g => g.Id == k.MainGodownId);
        if (unitId is { } u) line.SelectedUnit = line.UnitOptions.Single(o => o.Id == u);
        line.QuantityText = quantity;
        line.RateText = rate;
        Assert.True(entry.Accept(), entry.Message);
    }

    /// <summary>One line of a multi-line item invoice: which item, in which unit, how much, at what rate.</summary>
    private sealed record Ln(Guid ItemId, Guid? UnitId, string Quantity, string Rate);

    /// <summary>
    /// Enters and accepts a MULTI-line item invoice dated <paramref name="date"/> through the real entry view
    /// model, so every line lands on one voucher and therefore in one GSTR-1 period.
    /// </summary>
    private static void PostLinesOn(Kit k, DateOnly date, params Ln[] lines)
    {
        var entry = DriveToSalesItemInvoice(k.Vm);
        entry.DateText = date.ToString("dd-MM-yyyy");
        entry.SelectedParty = entry.Parties.Single(p => p.Ledger?.Id == k.CustomerId);
        for (var i = 0; i < lines.Length; i++)
        {
            var line = i == 0 ? entry.InventoryLines.First() : entry.AddInventoryLine();
            line.SelectedItem = entry.StockItems.Single(x => x.Id == lines[i].ItemId);
            line.SelectedGodown = entry.Godowns.Single(g => g.Id == k.MainGodownId);
            if (lines[i].UnitId is { } u) line.SelectedUnit = line.UnitOptions.Single(o => o.Id == u);
            line.QuantityText = lines[i].Quantity;
            line.RateText = lines[i].Rate;
        }
        Assert.True(entry.Accept(), entry.Message);
    }

    /// <summary>The UQC every posted line of the voucher dated <paramref name="date"/> resolves to, in line order.</summary>
    private static List<string?> DeclaredCodesOn(Kit k, DateOnly date)
    {
        var c = k.Vm.Company!;
        var type = c.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Sales && t.IsActive);
        var v = c.Vouchers.Single(x => x.TypeId == type.Id && x.Date == date);
        return v.InventoryLines.Select(il => UqcResolver.Declare(c, il, il.Quantity).Code).ToList();
    }

    /// <summary>Enters and accepts a one-line item invoice, returning the posted voucher.</summary>
    private static Voucher PostOneLine(Kit k, Guid? unitId, string quantity, string rate)
    {
        var entry = DriveToSalesItemInvoice(k.Vm);
        entry.SelectedParty = entry.Parties.Single(p => p.Ledger?.Id == k.CustomerId);

        var line = entry.InventoryLines.First();
        line.SelectedItem = entry.StockItems.Single(i => i.Id == k.EggId);
        line.SelectedGodown = entry.Godowns.Single(g => g.Id == k.MainGodownId);
        if (unitId is { } u) line.SelectedUnit = line.UnitOptions.Single(o => o.Id == u);
        line.QuantityText = quantity;
        line.RateText = rate;
        Assert.True(entry.Accept(), entry.Message);

        var c = k.Vm.Company!;
        var type = c.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Sales && t.IsActive);
        return c.Vouchers.Single(v => v.TypeId == type.Id);
    }

    private static decimal OnHand(Company c, Guid itemId, Guid godownId) =>
        new InventoryLedger(c).OnHand(itemId, godownId, c.FinancialYearStart.AddYears(1).AddDays(-1));

    private static JsonElement FirstEInvoiceItem(Company c, Voucher v) =>
        JsonDocument.Parse(Encoding.UTF8.GetString(EInvoiceJson.BuildInv01(c, v)))
            .RootElement.GetProperty("ItemList")[0];

    private static JsonElement FirstEWayItem(Company c, Voucher v)
    {
        var service = new EWayBillService(c);
        var record = service.PrepareRecord(v, v.Date);
        service.SetPartB(record, "TRANSIN01", EWayTransportMode.Road, "MH12AB1234", 250);
        return JsonDocument.Parse(Encoding.UTF8.GetString(EWayBillJson.BuildEwb01(c, v, record)))
            .RootElement.GetProperty("itemList")[0];
    }

    // ================================================================ THE CROSS-DOCUMENT PROOF

    /// <summary>
    /// <b>ONE posted voucher; FOUR documents; one physical quantity.</b> "2 Doz-Nos @ ₹10.00" moves 24 Nos of
    /// stock and is worth ₹20.00. The printed invoice, the INV-01 e-invoice, the EWB-01 e-way bill and the GSTR-1
    /// Table-12 HSN row must every one of them describe THAT supply — and the money must stay ₹20.00 taxable.
    ///
    /// <para>Each declaration is asserted as a matched (quantity, unit) PAIR, because either half alone is
    /// meaningless: "2" is right beside DOZ and wrong beside NOS. The pre-fix code emitted exactly that mismatch —
    /// the quantity 2 beside the base UQC NOS — at three of the four sites while print said "2 Doz-Nos", so this
    /// test fails on the old code at Site 1, Site 2 and Site 3 independently.</para>
    /// </summary>
    [Fact]
    public void Print_einvoice_eway_and_gstr1_all_declare_the_same_physical_quantity_for_one_voucher()
    {
        var k = NewKit("Cross Doc Co");
        var posted = PostOneLine(k, k.DozNosId, quantity: "2", rate: "10.00");
        var c = k.Vm.Company!;

        // ---- the physical + monetary ground truth this whole test is anchored to ----
        var il = Assert.Single(posted.InventoryLines);
        Assert.Equal(k.DozNosId, il.UnitId);
        Assert.Equal(2m, il.Quantity);                            // stated as 2 Doz
        Assert.Equal(Money.FromRupees(20m), il.Value);            // worth ₹20.00
        Assert.Equal(240m - 24m, OnHand(c, k.EggId, k.MainGodownId));  // 24 Nos PHYSICALLY MOVED

        // ---- DOCUMENT 1 — the printed invoice ----
        var print = VoucherPrintProjector.ProjectInvoice(c, posted);
        var printed = Assert.Single(print.Items);
        Assert.Equal("2 Doz-Nos", printed.QuantityText);
        Assert.Equal(Money.FromRupees(20m), printed.TaxableValue);

        // ---- DOCUMENT 2 — the INV-01 e-invoice ----
        var eInvoice = FirstEInvoiceItem(c, posted);
        Assert.Equal("DOZ", eInvoice.GetProperty("Unit").GetString());
        Assert.Equal(2000L, eInvoice.GetProperty("qty_millis").GetInt64());          // 2.000 DOZ
        Assert.Equal(1000L, eInvoice.GetProperty("unit_price_paisa").GetInt64());    // ₹10.00 PER DOZ
        Assert.Equal(2000L, eInvoice.GetProperty("ass_amt_paisa").GetInt64());       // ₹20.00 assessable
        // Quantity × unit price must recompose the assessable value — the invariant that catches a quantity
        // converted without its rate (24 × ₹10 = ₹240) or a rate converted without its quantity (2 × ₹0.83).
        Assert.Equal(
            eInvoice.GetProperty("ass_amt_paisa").GetInt64(),
            eInvoice.GetProperty("qty_millis").GetInt64() * eInvoice.GetProperty("unit_price_paisa").GetInt64() / 1000L);

        // ---- DOCUMENT 3 — the EWB-01 e-way bill ----
        var eWay = FirstEWayItem(c, posted);
        Assert.Equal("DOZ", eWay.GetProperty("Unit").GetString());
        Assert.Equal(2000L, eWay.GetProperty("qty_millis").GetInt64());
        Assert.Equal(2000L, eWay.GetProperty("taxable_amt_paisa").GetInt64());

        // ---- DOCUMENT 4 — the GSTR-1 Table-12 HSN summary (a FILED field) ----
        var hsn = Assert.Single(Gstr1.Build(c, posted.Date, posted.Date).HsnSummary);
        Assert.Equal("DOZ", hsn.Uqc);
        Assert.Equal(2m, hsn.Quantity);
        Assert.Equal(20m, hsn.TaxableValue.Amount);

        // ---- and the four agree with EACH OTHER, not merely with a literal ----
        Assert.Equal(hsn.Uqc, eInvoice.GetProperty("Unit").GetString());
        Assert.Equal(hsn.Uqc, eWay.GetProperty("Unit").GetString());
        Assert.Equal(eInvoice.GetProperty("Unit").GetString(), eWay.GetProperty("Unit").GetString());
        Assert.Equal(eInvoice.GetProperty("qty_millis").GetInt64(), eWay.GetProperty("qty_millis").GetInt64());
        Assert.Equal(hsn.Quantity * 1000m, eInvoice.GetProperty("qty_millis").GetInt64());
        Assert.StartsWith(hsn.Quantity.ToString("0.##"), printed.QuantityText, StringComparison.Ordinal);

        // ---- the money is untouched at every site ----
        Assert.Equal(20m, hsn.TaxableValue.Amount);
        Assert.Equal(2000L, eWay.GetProperty("taxable_amt_paisa").GetInt64());
        Assert.Equal(Money.FromRupees(20m), print.TotalTaxable);
    }

    // ================================================================ the fallback branch

    /// <summary>
    /// A compound unit whose first unit maps to NO valid UQC ("Crate") cannot be declared raw — the portal rejects
    /// an unknown code, so the filing would fail. It falls back to the item's BASE UQC with the quantity converted
    /// into it, <b>and the unit price converted by the same factor</b>. Converting one without the other is the
    /// 12× money defect; here it would inflate the declared assessable value from ₹240 to ₹2,880.
    /// </summary>
    [Fact]
    public void An_unmapped_compound_unit_declares_the_base_uqc_with_quantity_AND_rate_converted_together()
    {
        var k = NewKit("Fallback Co");
        var posted = PostOneLine(k, k.CrateNosId, quantity: "2", rate: "120.00");
        var c = k.Vm.Company!;

        var il = Assert.Single(posted.InventoryLines);
        Assert.Equal(2m, il.Quantity);
        Assert.Equal(Money.FromRupees(240m), il.Value);
        Assert.Equal(240m - 24m, OnHand(c, k.EggId, k.MainGodownId));   // still 24 Nos

        // "CRATE" is not a code the department publishes, so it is never emitted.
        Assert.False(UqcResolver.IsValid("CRATE"));

        var eInvoice = FirstEInvoiceItem(c, posted);
        Assert.Equal("NOS", eInvoice.GetProperty("Unit").GetString());
        Assert.Equal(24000L, eInvoice.GetProperty("qty_millis").GetInt64());        // 24 Nos, not 2
        Assert.Equal(1000L, eInvoice.GetProperty("unit_price_paisa").GetInt64());   // ₹10.00 PER NOS, not ₹120
        Assert.Equal(24000L, eInvoice.GetProperty("ass_amt_paisa").GetInt64());     // ₹240.00 — unchanged
        Assert.Equal(
            eInvoice.GetProperty("ass_amt_paisa").GetInt64(),
            eInvoice.GetProperty("qty_millis").GetInt64() * eInvoice.GetProperty("unit_price_paisa").GetInt64() / 1000L);

        var eWay = FirstEWayItem(c, posted);
        Assert.Equal("NOS", eWay.GetProperty("Unit").GetString());
        Assert.Equal(24000L, eWay.GetProperty("qty_millis").GetInt64());
        Assert.Equal(24000L, eWay.GetProperty("taxable_amt_paisa").GetInt64());

        var hsn = Assert.Single(Gstr1.Build(c, posted.Date, posted.Date).HsnSummary);
        Assert.Equal("NOS", hsn.Uqc);
        Assert.Equal(24m, hsn.Quantity);
        Assert.Equal(240m, hsn.TaxableValue.Amount);

        // The printed invoice states the same supply in the line's own unit — 2 Crate-Nos IS 24 Nos.
        Assert.Equal("2 Crate-Nos", Assert.Single(VoucherPrintProjector.ProjectInvoice(c, posted).Items).QuantityText);
    }

    /// <summary>
    /// <b>The e-invoice footing identity: <c>qty × unit_price == ass_amt</c>, EXACTLY, on every path.</b>
    ///
    /// <para>Converting to the base unit is only possible when the per-base rate is representable in paisa.
    /// ₹10 per Crate of 12 is ₹0.8333…/Nos, and the NIC unit-price field is integer paisa — so a converted
    /// declaration of 600 NOS would have to round to ₹0.83 and foot to ₹498.00 against an assessable ₹500.00.
    /// That residual is IRREDUCIBLE (the exact price would be 83.33… paisa, so no rounding rule fixes it) and
    /// it scales with quantity — half a paisa per base unit, ₹2.00 here, ~₹500 on 100,000 units — which can
    /// hard-reject the invoice at the IRP.</para>
    ///
    /// <para>So the resolver declines to convert and declares the line's own quantity and raw rate under
    /// <c>"OTH"</c>, the department's code for a unit absent from the master list. The identity then holds
    /// exactly. It is asserted AS an identity and never as a tolerance: the test this replaces asserted the
    /// ₹0.08 residual as acceptable, which pinned the defect in place instead of catching it.</para>
    ///
    /// <para>Deriving <c>ass_amt</c> from qty × price would also make the identity hold and is inadmissible —
    /// that field is the amount actually TAXED and must reconcile to the posted Sales leg and to GSTR-1. Hence
    /// the assertion that it is still exactly ₹500.00.</para>
    /// </summary>
    [Fact]
    public void The_einvoice_unit_price_foots_to_the_assessable_value_exactly_on_the_fallback_path()
    {
        var k = NewKit("Footing Co");
        var posted = PostOneLine(k, k.CrateNosId, quantity: "20", rate: "10.00");
        var c = k.Vm.Company!;

        // Ground truth: 20 Crate @ ₹10.00 = ₹200.00, and 240 Nos physically move (the whole opening stock).
        Assert.Equal(Money.FromRupees(200m), Assert.Single(posted.InventoryLines).Value);
        Assert.Equal(0m, OnHand(c, k.EggId, k.MainGodownId));

        var eInvoice = FirstEInvoiceItem(c, posted);
        var qty = eInvoice.GetProperty("qty_millis").GetInt64();
        var price = eInvoice.GetProperty("unit_price_paisa").GetInt64();
        var ass = eInvoice.GetProperty("ass_amt_paisa").GetInt64();

        Assert.Equal("OTH", eInvoice.GetProperty("Unit").GetString());
        Assert.Equal(20000L, qty);          // 20.000 of the line's own unit, not 240 NOS
        Assert.Equal(1000L, price);         // ₹10.00 per Crate — the entered rate, untouched
        Assert.Equal(20000L, ass);          // ₹200.00 — the amount actually taxed, NOT derived
        Assert.Equal(ass, qty * price / 1000L);   // the identity, exact — pre-fix this was 19920 vs 20000

        // The taxable value is untouched by any of this, and the money map is preserved.
        Assert.Equal(200m, Assert.Single(Gstr1.Build(c, posted.Date, posted.Date).HsnSummary).TaxableValue.Amount);

        // The e-way bill and the printed invoice state the SAME 20 — where before the fix the payloads said
        // 240 NOS while print said "20 Crate-Nos", the four documents now agree.
        var eWay = FirstEWayItem(c, posted);
        Assert.Equal("OTH", eWay.GetProperty("Unit").GetString());
        Assert.Equal(20000L, eWay.GetProperty("qty_millis").GetInt64());
        Assert.Equal("20 Crate-Nos", Assert.Single(VoucherPrintProjector.ProjectInvoice(c, posted).Items).QuantityText);
    }

    // ================================================================ ER-13 — no line unit ⇒ nothing changes

    /// <summary>
    /// ER-13. A line carrying NO unit is already stated in the item's base unit, so every declaration is exactly
    /// what it was before line units existed: the item's base UQC, the raw quantity, the raw rate — no conversion,
    /// no re-casing. This is the assertion that would catch a "fix" that blanket-converted every line.
    ///
    /// <para><b>The rate here (₹10.00) is a fixed point of every plausible rounding</b>, so the
    /// <c>unit_price_paisa</c> assertion below pins the LABEL and the QUANTITY but cannot discriminate a rate that
    /// was quietly rounded on the way out. That job belongs to
    /// <see cref="A_unit_less_line_at_a_sub_rupee_rate_foots_exactly_on_the_er13_path"/>, which drives a rate that
    /// is not — do not delete it believing this test covers the same ground.</para>
    /// </summary>
    [Fact]
    public void A_line_with_no_unit_declares_exactly_what_it_always_declared()
    {
        var k = NewKit("Unchanged Co");
        var posted = PostOneLine(k, unitId: null, quantity: "24", rate: "10.00");
        var c = k.Vm.Company!;

        var il = Assert.Single(posted.InventoryLines);
        Assert.Null(il.UnitId);                                     // the picker defaulted to the base unit
        Assert.Equal(Money.FromRupees(240m), il.Value);

        var eInvoice = FirstEInvoiceItem(c, posted);
        Assert.Equal("NOS", eInvoice.GetProperty("Unit").GetString());
        Assert.Equal(24000L, eInvoice.GetProperty("qty_millis").GetInt64());
        Assert.Equal(1000L, eInvoice.GetProperty("unit_price_paisa").GetInt64());   // the RAW rate, untouched

        var eWay = FirstEWayItem(c, posted);
        Assert.Equal("NOS", eWay.GetProperty("Unit").GetString());
        Assert.Equal(24000L, eWay.GetProperty("qty_millis").GetInt64());

        var hsn = Assert.Single(Gstr1.Build(c, posted.Date, posted.Date).HsnSummary);
        Assert.Equal("NOS", hsn.Uqc);
        Assert.Equal(24m, hsn.Quantity);
        Assert.Equal(240m, hsn.TaxableValue.Amount);
    }

    /// <summary>
    /// <b>ER-13's rate passthrough, asserted at a rate that can actually detect a rounding.</b>
    ///
    /// <para>The no-line-unit branch of <see cref="UqcResolver.Declare"/> returns the entered rate RAW — that is the
    /// whole of the ER-13 guarantee, and it is what makes <c>qty × unit_price == ass_amt</c> hold on INV-01 for
    /// every pre-v46 line. Nothing tested it: the sibling ER-13 test drives ₹10.00, a fixed point of every
    /// plausible rounding, so its <c>unit_price_paisa</c> assertion survives a rate mutation unchanged.
    /// <b>Verified by mutation</b> — inserting <c>Math.Round(rate, 1)</c> on that branch left the entire suite
    /// green (Desktop 1053 + Io 344), while emitting ₹3.30 for a ₹3.33 line: 7 × ₹3.30 = ₹23.10 against an
    /// assessable ₹23.31, a 21-paisa INV-01 footing break on the commonest line shape in the product.</para>
    ///
    /// <para>₹3.33 is chosen because it is a fixed point of NO rounding coarser than paisa, so both assertions
    /// below are genuinely discriminating: the rate is checked directly at the resolver, and the footing identity
    /// is checked on the emitted payload. The assessable amount is asserted independently at ₹23.31 so the
    /// identity cannot be satisfied by deriving <c>ass_amt</c> from the price — that field is the amount actually
    /// TAXED and must reconcile to the posted Sales leg.</para>
    /// </summary>
    [Fact]
    public void A_unit_less_line_at_a_sub_rupee_rate_foots_exactly_on_the_er13_path()
    {
        var k = NewKit("Raw Rate Co");
        var posted = PostOneLine(k, unitId: null, quantity: "7", rate: "3.33");
        var c = k.Vm.Company!;

        // Ground truth: 7 Nos @ ₹3.33 = ₹23.31, stated in the item's own base unit with no line unit at all.
        var il = Assert.Single(posted.InventoryLines);
        Assert.Null(il.UnitId);
        Assert.Equal(Money.FromRupees(23.31m), il.Value);

        var eInvoice = FirstEInvoiceItem(c, posted);
        var qty = eInvoice.GetProperty("qty_millis").GetInt64();
        var price = eInvoice.GetProperty("unit_price_paisa").GetInt64();
        var ass = eInvoice.GetProperty("ass_amt_paisa").GetInt64();

        Assert.Equal("NOS", eInvoice.GetProperty("Unit").GetString());
        Assert.Equal(7000L, qty);
        Assert.Equal(333L, price);      // ₹3.33 per Nos — the RAW rate; ₹3.30 here is the mutation this catches
        Assert.Equal(2331L, ass);       // ₹23.31 — the amount actually taxed, asserted independently
        Assert.Equal(ass, qty * price / 1000L);   // the INV-01 footing identity, EXACT — 2310 vs 2331 when rounded

        // And the same guarantee stated at its source: the resolver hands back the entered rate UNTOUCHED.
        var decl = UqcResolver.Declare(c, il, il.Quantity);
        Assert.Equal("NOS", decl.Code);
        Assert.Equal(7m, decl.Quantity);
        Assert.Equal(3.33m, decl.Rate);

        // The e-way bill and GSTR-1 state the same supply; the money map is untouched.
        Assert.Equal(7000L, FirstEWayItem(c, posted).GetProperty("qty_millis").GetInt64());
        var hsn = Assert.Single(Gstr1.Build(c, posted.Date, posted.Date).HsnSummary);
        Assert.Equal("NOS", hsn.Uqc);
        Assert.Equal(7m, hsn.Quantity);
        Assert.Equal(23.31m, hsn.TaxableValue.Amount);
    }

    // ================================================================ the aggregation guard

    /// <summary>
    /// GSTR-1 carries one row per HSN, hence one UQC per row — so two lines of the same HSN declared in DIFFERENT
    /// units cannot be summed as stated ("2 DOZ + 5 NOS = 7" is nonsense under either label). The row degrades to
    /// the base unit, in which both lines ARE commensurable: 24 + 5 = 29 NOS. Without this the fix would trade a
    /// mislabelled single-line row for a silently wrong multi-line total.
    /// </summary>
    [Fact]
    public void Gstr1_degrades_to_the_base_unit_when_one_hsn_mixes_declared_units()
    {
        var k = NewKit("Mixed Unit Co");
        var entry = DriveToSalesItemInvoice(k.Vm);
        entry.SelectedParty = entry.Parties.Single(p => p.Ledger?.Id == k.CustomerId);

        var first = entry.InventoryLines.First();
        first.SelectedItem = entry.StockItems.Single(i => i.Id == k.EggId);
        first.SelectedGodown = entry.Godowns.Single(g => g.Id == k.MainGodownId);
        first.SelectedUnit = first.UnitOptions.Single(o => o.Id == k.DozNosId);
        first.QuantityText = "2";                                   // 2 Doz  = 24 Nos, ₹20.00
        first.RateText = "10.00";

        var second = entry.AddInventoryLine();
        second.SelectedItem = entry.StockItems.Single(i => i.Id == k.EggId);
        second.SelectedGodown = entry.Godowns.Single(g => g.Id == k.MainGodownId);
        second.SelectedUnit = second.UnitOptions.Single(o => o.Id == k.NosId);
        second.QuantityText = "5";                                  // 5 Nos, ₹50.00
        second.RateText = "10.00";
        Assert.True(entry.Accept(), entry.Message);

        var c = k.Vm.Company!;
        var type = c.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Sales && t.IsActive);
        var posted = c.Vouchers.Single(v => v.TypeId == type.Id);
        Assert.Equal(240m - 29m, OnHand(c, k.EggId, k.MainGodownId));   // 29 Nos moved

        var hsn = Assert.Single(Gstr1.Build(c, posted.Date, posted.Date).HsnSummary);
        Assert.Equal("NOS", hsn.Uqc);      // NOT "DOZ" — the first line's label must not annex the second
        Assert.Equal(29m, hsn.Quantity);   // NOT 7 (2 + 5) and NOT 29 mislabelled DOZ
        Assert.Equal(70m, hsn.TaxableValue.Amount);
    }

    // ================================================================ GSTR-9 Table 17 — ACROSS return periods

    private static readonly DateOnly FyEnd = new(2025, 3, 31);

    /// <summary>
    /// <b>GSTR-9 Table 17 must not sum quantities stated in different units — the regression this fix exists for.</b>
    ///
    /// <para>Table 17 is built by Σ-ing the periodic GSTR-1 HSN rows, and a period row states its quantity in its
    /// OWN UQC. Once a line may declare its own unit those UQCs vary period to period, so the naive sum added
    /// April's "2 DOZ" to May's "5 NOS" and filed <b>7 DOZ = 84 Nos for a year in which 29 Nos moved</b> — a 2.9×
    /// overstatement on a MANDATORY filed field (HSN summary of outward supplies, mandatory from FY 2021-22).</para>
    ///
    /// <para>The money is right either way (₹70.00 = ₹20 + ₹50), which is exactly why the green suite could not
    /// catch it. The 29 is anchored to PHYSICAL STOCK rather than written as a literal, so the expectation is
    /// derived from what actually moved.</para>
    /// </summary>
    [Fact]
    public void Gstr9_table17_degrades_to_the_base_unit_when_periods_declare_different_units()
    {
        var k = NewKit("Annual Mixed Co");
        PostOneLineOn(k, k.DozNosId, "2", "10.00", new DateOnly(2024, 4, 10));   // April: 2 Doz = 24 Nos, ₹20.00
        PostOneLineOn(k, null, "5", "10.00", new DateOnly(2024, 5, 10));         // May:   5 Nos,          ₹50.00
        var c = k.Vm.Company!;

        var moved = 240m - OnHand(c, k.EggId, k.MainGodownId);
        Assert.Equal(29m, moved);                                               // 24 + 5 Nos physically moved

        // The periods themselves are correct and DO differ in label — that is legitimate, not the bug.
        var apr = Assert.Single(Gstr1.Build(c, new DateOnly(2024, 4, 1), new DateOnly(2024, 4, 30)).HsnSummary);
        var may = Assert.Single(Gstr1.Build(c, new DateOnly(2024, 5, 1), new DateOnly(2024, 5, 31)).HsnSummary);
        Assert.Equal("DOZ", apr.Uqc);
        Assert.Equal(2m, apr.Quantity);
        Assert.Equal("NOS", may.Uqc);
        Assert.Equal(5m, may.Quantity);

        var annual = Assert.Single(Gstr9.Build(c, FyStart, FyEnd).Table17Hsn);
        Assert.Equal("NOS", annual.Uqc);        // NOT "DOZ" — April's label must not annex May's quantity
        Assert.Equal(moved, annual.Quantity);   // 29, NOT 7
        Assert.Equal(70m, annual.TaxableValue.Amount);   // money unchanged — this is what stayed green pre-fix
    }

    /// <summary>
    /// The GENERAL statement of the guarantee <c>Gstr9.Build</c> documents: every annual figure is a Σ of already
    /// rounded periods and therefore <b>cannot diverge from the periodic returns</b>. Quantity was the one column
    /// where that had become false. Asserting the identity rather than a literal catches any FUTURE divergence,
    /// not merely this one.
    /// </summary>
    [Fact]
    public void Gstr9_table17_agrees_with_the_full_year_gstr1_hsn_summary_on_unit_and_quantity()
    {
        var k = NewKit("Annual Identity Co");
        PostOneLineOn(k, k.DozNosId, "2", "10.00", new DateOnly(2024, 4, 10));
        PostOneLineOn(k, null, "5", "10.00", new DateOnly(2024, 5, 10));
        var c = k.Vm.Company!;

        var periodic = Gstr1.Build(c, FyStart, FyEnd).HsnSummary.ToDictionary(h => h.HsnSac);
        var annual = Gstr9.Build(c, FyStart, FyEnd).Table17Hsn.ToDictionary(h => h.HsnSac);

        Assert.NotEmpty(annual);
        Assert.Equal(periodic.Keys.OrderBy(x => x, StringComparer.Ordinal),
            annual.Keys.OrderBy(x => x, StringComparer.Ordinal));
        foreach (var (hsn, p) in periodic)
        {
            Assert.Equal(p.Uqc, annual[hsn].Uqc);
            Assert.Equal(p.Quantity, annual[hsn].Quantity);
            Assert.Equal(p.TaxableValue, annual[hsn].TaxableValue);
        }
    }

    /// <summary>
    /// <b>Degrading is transitive: an ALREADY-degraded period row must be able to degrade again.</b>
    ///
    /// <para>April here is itself mixed (2 Doz + 5 Nos ⇒ the row degrades to NOS 29); May is a clean DOZ 3
    /// (36 Nos). The annual row must reach NOS 65, which is only possible if April's row carried its raw base
    /// measure through the projection instead of collapsing to the degraded pair.</para>
    ///
    /// <para>This is the test that catches an INCOMPLETE fix. A naive "first-seen label wins" implementation
    /// yields NOS 32 (29 + 3) — right label, nonsense number — and would sail past the simpler case above.</para>
    /// </summary>
    [Fact]
    public void Gstr9_table17_degrades_a_period_row_that_was_itself_already_degraded()
    {
        var k = NewKit("Annual Transitive Co");

        // April — ONE invoice, TWO lines in different units ⇒ the April row is already degraded to NOS 29.
        var entry = DriveToSalesItemInvoice(k.Vm);
        entry.DateText = new DateOnly(2024, 4, 10).ToString("dd-MM-yyyy");
        entry.SelectedParty = entry.Parties.Single(p => p.Ledger?.Id == k.CustomerId);
        var first = entry.InventoryLines.First();
        first.SelectedItem = entry.StockItems.Single(i => i.Id == k.EggId);
        first.SelectedGodown = entry.Godowns.Single(g => g.Id == k.MainGodownId);
        first.SelectedUnit = first.UnitOptions.Single(o => o.Id == k.DozNosId);
        first.QuantityText = "2";                                   // 24 Nos
        first.RateText = "10.00";
        var second = entry.AddInventoryLine();
        second.SelectedItem = entry.StockItems.Single(i => i.Id == k.EggId);
        second.SelectedGodown = entry.Godowns.Single(g => g.Id == k.MainGodownId);
        second.SelectedUnit = second.UnitOptions.Single(o => o.Id == k.NosId);
        second.QuantityText = "5";                                  // 5 Nos
        second.RateText = "10.00";
        Assert.True(entry.Accept(), entry.Message);

        // May — a clean single-unit row, 3 Doz = 36 Nos.
        PostOneLineOn(k, k.DozNosId, "3", "10.00", new DateOnly(2024, 5, 10));
        var c = k.Vm.Company!;

        var apr = Assert.Single(Gstr1.Build(c, new DateOnly(2024, 4, 1), new DateOnly(2024, 4, 30)).HsnSummary);
        Assert.Equal("NOS", apr.Uqc);
        Assert.Equal(29m, apr.Quantity);     // April is ALREADY degraded
        var may = Assert.Single(Gstr1.Build(c, new DateOnly(2024, 5, 1), new DateOnly(2024, 5, 31)).HsnSummary);
        Assert.Equal("DOZ", may.Uqc);
        Assert.Equal(3m, may.Quantity);

        var moved = 240m - OnHand(c, k.EggId, k.MainGodownId);
        Assert.Equal(65m, moved);                                   // 24 + 5 + 36 Nos

        var annual = Assert.Single(Gstr9.Build(c, FyStart, FyEnd).Table17Hsn);
        Assert.Equal("NOS", annual.Uqc);
        Assert.Equal(moved, annual.Quantity);   // 65 — NOT 32 (a first-seen-wins fix) and NOT 7 (the raw sum)
        Assert.Equal(100m, annual.TaxableValue.Amount);   // ₹20 + ₹50 + ₹30
    }

    // ================================================================ the controlled vocabulary

    /// <summary>
    /// UQC is a controlled statutory vocabulary — a code outside it is a rejected return, not a cosmetic nit. The
    /// set is transcribed from the official NIC e-invoice master list (R7); this locks the codes the corpus
    /// actually exercises and proves the set is closed against anything else.
    /// </summary>
    [Fact]
    public void The_valid_uqc_set_is_closed_and_contains_the_codes_the_corpus_uses()
    {
        // The corpus selects these by name: "NOS-NUMBERS" and "BOX-BOX" (TALLY-PRIME-WITH-GST-Notes), "PCS"
        // (STUDY-GUIDE); "DOZ" is what a Dozen line unit resolves to.
        Assert.True(UqcResolver.IsValid("NOS"));
        Assert.True(UqcResolver.IsValid("BOX"));
        Assert.True(UqcResolver.IsValid("PCS"));
        Assert.True(UqcResolver.IsValid("DOZ"));
        Assert.True(UqcResolver.IsValid("KGS"));
        Assert.True(UqcResolver.IsValid("OTH"));
        // Deliberately NO `Count == 45` pin. A cardinality assertion proves nothing about correctness — any
        // wrong 45-code set satisfies it — while REDing the build on a legitimate NIC addition to the master
        // list. What is worth asserting is membership of the codes we actually rely on, closure against
        // everything else, and the shape of the field; all three are below.

        // Closed: a plausible-looking invented code, a unit SYMBOL, and empties are all rejected.
        Assert.False(UqcResolver.IsValid("DOZEN"));
        Assert.False(UqcResolver.IsValid("Doz-Nos"));
        Assert.False(UqcResolver.IsValid("CRATE"));
        Assert.False(UqcResolver.IsValid(null));
        Assert.False(UqcResolver.IsValid("   "));

        // Every published code is upper-case and exactly three characters — the shape the NIC field accepts.
        Assert.All(UqcResolver.ValidCodes, code =>
        {
            Assert.Equal(3, code.Length);
            Assert.Equal(code.ToUpperInvariant(), code);
        });
    }

    // ================================================================ "OTH" IS A LABEL, NOT A UNIT

    /// <summary>
    /// <b>Two DIFFERENT unmapped units must not be summed just because both are labelled "OTH".</b>
    ///
    /// <para><c>"OTH"</c> is the department's CATCH-ALL for a unit absent from its master list. The resolver
    /// synthesizes it whenever a compound's first unit maps to no code and the per-base rate is not paisa-exact —
    /// so 5 Crate-Nos and 3 Pallet-Nos, counting 12-per and 7-per respectively, BOTH declare "OTH". An aggregator
    /// deciding commensurability by comparing UQC <i>labels</i> finds them equal, never sets its mixed flag, and
    /// files <b>"8"</b> for a supply in which <b>81 Nos</b> physically moved — a 10× understatement on the
    /// mandatory Table-12 HSN summary.</para>
    ///
    /// <para>The money is right either way (₹80.00), which is exactly the signature of the defect this slice
    /// exists to remove and why a green suite could not catch it. The expected quantity is anchored to PHYSICAL
    /// STOCK rather than written as a literal.</para>
    /// </summary>
    [Fact]
    public void Gstr1_does_not_sum_two_different_unmapped_units_under_the_shared_oth_label()
    {
        var k = NewKit("Oth Collision Co");
        var day = new DateOnly(2024, 4, 5);
        PostLinesOn(k, day,
            new Ln(k.EggId, k.CrateNosId, "5", "10.00"),     // 5 Crate = 60 Nos
            new Ln(k.EggId, k.PalletNosId, "3", "10.00"));   // 3 Pallet = 21 Nos
        var c = k.Vm.Company!;

        // The PREMISE: both lines really do declare the same label. Without this the test could pass for the
        // wrong reason — e.g. if the resolver stopped emitting "OTH" at all.
        Assert.Equal(new string?[] { "OTH", "OTH" }, DeclaredCodesOn(k, day));

        var moved = 240m - OnHand(c, k.EggId, k.MainGodownId);
        Assert.Equal(81m, moved);                            // 60 + 21 Nos PHYSICALLY moved

        var hsn = Assert.Single(Gstr1.Build(c, day, day).HsnSummary);
        Assert.Equal("NOS", hsn.Uqc);          // degraded to the commensurable base unit, NOT the catch-all
        Assert.Equal(moved, hsn.Quantity);     // 81, NOT 8
        Assert.Equal(80m, hsn.TaxableValue.Amount);   // money untouched — right before the fix and after it
    }

    /// <summary>
    /// The same collision ACROSS return periods: April billed in Crates, May in Pallets. Each period row is
    /// legitimately "OTH" — within a period only one unmapped unit appears, so nothing is being added — but
    /// GSTR-9 Table 17 Σ-s the period rows, and comparing their labels sums 5 + 3 into an annual <b>"8"</b> for a
    /// year in which <b>81 Nos</b> moved.
    /// </summary>
    [Fact]
    public void Gstr9_table17_does_not_sum_two_periods_that_both_declared_oth()
    {
        var k = NewKit("Annual Oth Collision Co");
        PostLinesOn(k, new DateOnly(2024, 4, 5), new Ln(k.EggId, k.CrateNosId, "5", "10.00"));
        PostLinesOn(k, new DateOnly(2024, 5, 5), new Ln(k.EggId, k.PalletNosId, "3", "10.00"));
        var c = k.Vm.Company!;

        // The periods are each CORRECT and each labelled "OTH" — that is the trap, not the bug.
        var apr = Assert.Single(Gstr1.Build(c, new DateOnly(2024, 4, 1), new DateOnly(2024, 4, 30)).HsnSummary);
        var may = Assert.Single(Gstr1.Build(c, new DateOnly(2024, 5, 1), new DateOnly(2024, 5, 31)).HsnSummary);
        Assert.Equal("OTH", apr.Uqc);
        Assert.Equal(5m, apr.Quantity);
        Assert.Equal("OTH", may.Uqc);
        Assert.Equal(3m, may.Quantity);

        var moved = 240m - OnHand(c, k.EggId, k.MainGodownId);
        Assert.Equal(81m, moved);

        var annual = Assert.Single(Gstr9.Build(c, FyStart, FyEnd).Table17Hsn);
        Assert.Equal("NOS", annual.Uqc);
        Assert.Equal(moved, annual.Quantity);   // 81, NOT 8
        Assert.Equal(80m, annual.TaxableValue.Amount);
    }

    /// <summary>
    /// <b>The collision is reachable with only ONE unmapped unit</b>, because <c>"OTH"</c> is itself a member of
    /// the published vocabulary: a user may LEGITIMATELY map a unit master to it. "Case" here is mapped to "OTH"
    /// by the user and therefore takes the PREFERRED path — its code is valid, so no fallback is involved at all —
    /// where it meets the fallback path's synthesized "OTH" for "Crate".
    ///
    /// <para>This is what makes a <c>!= "OTH"</c> patch on the fallback branch insufficient: the two "OTH"s arrive
    /// from different branches and neither is wrong on its own. Only comparing unit IDENTITY separates them.</para>
    /// </summary>
    [Fact]
    public void A_unit_legitimately_mapped_to_oth_does_not_annex_an_unmapped_unit()
    {
        var k = NewKit("Mapped Oth Co");
        var day = new DateOnly(2024, 4, 5);
        PostLinesOn(k, day,
            new Ln(k.EggId, k.CaseNosId, "4", "10.00"),     // 4 Case = 12 Nos — "OTH" via the PREFERRED path
            new Ln(k.EggId, k.CrateNosId, "5", "10.00"));   // 5 Crate = 60 Nos — "OTH" via the FALLBACK
        var c = k.Vm.Company!;

        Assert.Equal(new string?[] { "OTH", "OTH" }, DeclaredCodesOn(k, day));
        // "Case" reaches OTH because the user mapped it there and the code IS valid — not because it fell back.
        Assert.True(UqcResolver.IsValid("OTH"));

        var moved = 240m - OnHand(c, k.EggId, k.MainGodownId);
        Assert.Equal(72m, moved);                            // 12 + 60 Nos

        var hsn = Assert.Single(Gstr1.Build(c, day, day).HsnSummary);
        Assert.Equal("NOS", hsn.Uqc);
        Assert.Equal(moved, hsn.Quantity);     // 72, NOT 9
        Assert.Equal(90m, hsn.TaxableValue.Amount);
    }

    /// <summary>
    /// <b>The guard against the LAZY fix.</b> "Never sum anything labelled OTH" would also refuse two lines stated
    /// in the SAME unmapped unit, degrading a perfectly good "8 OTH" (8 Crates) into "96 NOS" — replacing an
    /// understatement with a needless loss of the unit the supply was actually billed in.
    ///
    /// <para>The rule is about unit IDENTITY, not about the string "OTH". Same unit ⇒ still summed under its own
    /// declaration, with the commensurable base measure carried alongside regardless.</para>
    /// </summary>
    [Fact]
    public void Two_lines_in_the_same_unmapped_unit_still_declare_that_unit()
    {
        var k = NewKit("Same Unmapped Co");
        var day = new DateOnly(2024, 4, 5);
        PostLinesOn(k, day,
            new Ln(k.EggId, k.CrateNosId, "5", "10.00"),
            new Ln(k.EggId, k.CrateNosId, "3", "10.00"));
        var c = k.Vm.Company!;

        var moved = 240m - OnHand(c, k.EggId, k.MainGodownId);
        Assert.Equal(96m, moved);                            // 8 Crate = 96 Nos

        var hsn = Assert.Single(Gstr1.Build(c, day, day).HsnSummary);
        Assert.Equal("OTH", hsn.Uqc);          // NOT degraded — one unit, so the label still means something
        Assert.Equal(8m, hsn.Quantity);        // 8 Crates, NOT 96
        Assert.Equal(moved, hsn.BaseQuantity); // and the commensurable measure rides along
        Assert.Equal(80m, hsn.TaxableValue.Amount);
    }

    /// <summary>
    /// <b>The guard against OVER-degrading.</b> Two DIFFERENT unit masters ("Doz" and "Dz") legitimately mapped to
    /// the same REAL code "DOZ" genuinely count the same unit, so they must still sum under it. An identity-only
    /// rule would refuse and degrade to NOS 60 — right number, but it discards the unit the supply was billed in
    /// for no reason, and the degrade path is itself unsound when base units differ.
    ///
    /// <para>With the test above, this pins the rule as <i>identity OR real label</i> — not either one alone.</para>
    /// </summary>
    [Fact]
    public void Two_different_unit_masters_sharing_a_real_uqc_still_sum_under_it()
    {
        var k = NewKit("Shared Doz Co");
        var day = new DateOnly(2024, 4, 5);
        PostLinesOn(k, day,
            new Ln(k.EggId, k.DozNosId, "2", "10.00"),      // 2 Doz = 24 Nos
            new Ln(k.EggId, k.DzNosId, "3", "10.00"));      // 3 Dz  = 36 Nos, a DIFFERENT master, same code
        var c = k.Vm.Company!;

        Assert.Equal(new string?[] { "DOZ", "DOZ" }, DeclaredCodesOn(k, day));

        var moved = 240m - OnHand(c, k.EggId, k.MainGodownId);
        Assert.Equal(60m, moved);

        var hsn = Assert.Single(Gstr1.Build(c, day, day).HsnSummary);
        Assert.Equal("DOZ", hsn.Uqc);          // NOT degraded to NOS
        Assert.Equal(5m, hsn.Quantity);        // 5 DOZ
        Assert.Equal(moved, hsn.BaseQuantity); // == 60 Nos
        Assert.Equal(50m, hsn.TaxableValue.Amount);
    }

    /// <summary>
    /// The catch-all set is a SUBSET of the published vocabulary — a catch-all is a real code the portal accepts,
    /// which is precisely why comparing it is dangerous. Membership, not cardinality: a future departmental
    /// catch-all is a one-line addition to the set rather than a hunt for <c>"OTH"</c> literals in the aggregators.
    /// </summary>
    [Fact]
    public void The_catch_all_codes_are_themselves_valid_uqcs()
    {
        Assert.NotEmpty(UqcResolver.CatchAllCodes);
        Assert.Subset(UqcResolver.ValidCodes.ToHashSet(StringComparer.Ordinal),
            UqcResolver.CatchAllCodes.ToHashSet(StringComparer.Ordinal));
        Assert.All(UqcResolver.CatchAllCodes, code =>
        {
            Assert.True(UqcResolver.IsValid(code));
            Assert.True(UqcResolver.IsCatchAll(code));
        });

        Assert.True(UqcResolver.IsCatchAll("OTH"));
        Assert.True(UqcResolver.IsCatchAll(" oth "));      // canonicalised on input, exactly like IsValid
        Assert.False(UqcResolver.IsCatchAll("NOS"));
        Assert.False(UqcResolver.IsCatchAll("DOZ"));
        Assert.False(UqcResolver.IsCatchAll(null));
        Assert.False(UqcResolver.IsCatchAll("   "));
    }

    /// <summary>
    /// The commensurability predicate itself, as a truth table. Every aggregator delegates to it, so its behaviour
    /// on the awkward inputs — a catch-all on both sides, a null identity, a null code — IS the contract.
    /// </summary>
    [Fact]
    public void Are_commensurable_decides_on_identity_or_a_real_label_and_never_on_a_catch_all()
    {
        var crate = Guid.NewGuid();
        var pallet = Guid.NewGuid();

        // Same identity wins regardless of the label — including when the label is a catch-all or missing.
        Assert.True(UqcResolver.AreCommensurable(crate, "OTH", crate, "OTH"));
        Assert.True(UqcResolver.AreCommensurable(crate, null, crate, null));

        // Different identities, same REAL code ⇒ commensurable (two masters, one genuine unit).
        Assert.True(UqcResolver.AreCommensurable(crate, "DOZ", pallet, "DOZ"));

        // Different identities, both CATCH-ALL ⇒ NOT commensurable. This is the defect.
        Assert.False(UqcResolver.AreCommensurable(crate, "OTH", pallet, "OTH"));

        // Different identities, different codes ⇒ not commensurable.
        Assert.False(UqcResolver.AreCommensurable(crate, "DOZ", pallet, "NOS"));

        // Label equality is case-INSENSITIVE, matching IsValid/IsCatchAll. Declare canonicalises firstCode but
        // passes the base UQC through RAW (ER-13 forbids re-casing a unit-less line), so a company storing a
        // lowercase base code must still compare equal to a canonical one.
        Assert.True(UqcResolver.AreCommensurable(crate, "DOZ", pallet, "doz"));
        Assert.True(UqcResolver.AreCommensurable(crate, "nos", pallet, "NOS"));
        // ...and the catch-all guard survives every casing of it — the safety property, not a formatting nit.
        Assert.False(UqcResolver.AreCommensurable(crate, "OTH", pallet, "oth"));
        Assert.False(UqcResolver.AreCommensurable(crate, "oth", pallet, "oth"));

        // Two unknowns are not known to be equal: neither a null/blank code nor a null identity is evidence of
        // sameness. (The superseded label comparison treated null == null as agreement.)
        //
        // The all-null case is NOT reflexive, ON PURPOSE — both aggregators run this predicate on their SEEDING
        // iteration, so this False is what makes a lone row with no identity and no label degrade to its base
        // declaration instead of asserting a unit it cannot name. See the Gstr1/Gstr9 call sites; do not "fix"
        // this to True.
        Assert.False(UqcResolver.AreCommensurable(null, null, null, null));
        Assert.False(UqcResolver.AreCommensurable(null, "DOZ", crate, null));
        Assert.False(UqcResolver.AreCommensurable(crate, "   ", pallet, "   "));
        Assert.False(UqcResolver.AreCommensurable(null, "NOS", null, null));

        // A null identity on ONE side still leaves the real-label ground available — but not the catch-all one.
        Assert.True(UqcResolver.AreCommensurable(null, "NOS", crate, "NOS"));
        Assert.False(UqcResolver.AreCommensurable(null, "OTH", crate, "OTH"));
    }

    /// <summary>
    /// <b>KNOWN LIMITATION, characterized rather than silently carried.</b> When one HSN spans items measured in
    /// UNRELATED base units — 5 Nos of eggs and 3 Kgs of rice — the DEGRADE TARGET is itself incommensurable, so
    /// the row states "8 NOS" for a supply that is 5 Nos and 3 Kgs.
    ///
    /// <para>This is <b>pre-existing</b> and is NOT caused by the catch-all fix — but the fix widens traffic onto
    /// the degrade path (it correctly refuses to sum strictly more pairs than before, and every refusal routes
    /// here), so the hole becomes more load-bearing and must be visible. <c>MixedBases</c> makes it detectable; no
    /// number is invented, because there is no correct one while Table 12 is keyed on HSN alone. Real Table-12
    /// rows appear to be keyed on (HSN, UQC, rate), which would remove the need to degrade at all — recorded as a
    /// HYPOTHESIS, not a particular: under R7 that must be verified against an official primary source before the
    /// shape of a FILED output is changed.</para>
    ///
    /// <para>This test pins TODAY'S behaviour. When the limitation is fixed it is expected to fail, and that fix
    /// must update it deliberately.</para>
    /// </summary>
    [Fact]
    public void Gstr1_cannot_yet_state_one_quantity_when_one_hsn_spans_two_base_units()
    {
        var k = NewKit("Two Bases Co");
        // April is CLEAN (one base unit); MAY is the mixed one. Deliberately in that order: it makes the annual
        // roll-up carry the flag through its ACCUMULATE step rather than merely inherit it when seeding from the
        // first period, which is where a dropped propagation would hide.
        var april = new DateOnly(2024, 4, 5);
        var may = new DateOnly(2024, 5, 5);
        PostLinesOn(k, april, new Ln(k.EggId, null, "2", "10.00"));   // 2 Nos of Egg
        PostLinesOn(k, may,
            new Ln(k.EggId, null, "5", "10.00"),      // 5 Nos of Egg
            new Ln(k.RiceId, null, "3", "10.00"));    // 3 Kgs of Rice — same HSN, unrelated base unit
        var c = k.Vm.Company!;

        Assert.Equal(7m, 240m - OnHand(c, k.EggId, k.MainGodownId));
        Assert.Equal(3m, 240m - OnHand(c, k.RiceId, k.MainGodownId));

        var aprRow = Assert.Single(Gstr1.Build(c, new DateOnly(2024, 4, 1), new DateOnly(2024, 4, 30)).HsnSummary);
        Assert.False(aprRow.MixedBases);   // one base unit — nothing wrong with April
        Assert.Equal(2m, aprRow.Quantity);

        var hsn = Assert.Single(Gstr1.Build(c, new DateOnly(2024, 5, 1), new DateOnly(2024, 5, 31)).HsnSummary);
        Assert.True(hsn.MixedBases, "The row folds two unrelated base units and must say so.");
        Assert.Equal(80m, hsn.TaxableValue.Amount);   // the MONEY is correct and is not what is in question

        // Characterization of the defect, NOT an endorsement: "8 NOS" describes neither 5 Nos nor 3 Kgs.
        Assert.Equal("NOS", hsn.Uqc);
        Assert.Equal(8m, hsn.Quantity);

        // The annual roll-up must PROPAGATE the flag, not quietly drop it: a limitation that vanishes one
        // aggregation later is worse than one never flagged, because Table 17 is the FILED figure.
        var annual = Assert.Single(Gstr9.Build(c, FyStart, FyEnd).Table17Hsn);
        Assert.True(annual.MixedBases, "GSTR-9 Table 17 must carry the period row's mixed-base limitation forward.");
        Assert.Equal(10m, annual.Quantity);            // 2 + 8, still under the same unusable label
        Assert.Equal(100m, annual.TaxableValue.Amount);
    }

    // ================================================================ ER-13 — the RAW base-code passthrough

    /// <summary>
    /// <b>ER-13's base-UQC passthrough, asserted at a code that can actually detect a re-casing.</b>
    ///
    /// <para><see cref="UqcResolver.Declare"/> reads the item's base UQC <b>RAW</b> — deliberately, and stated as
    /// such in a comment: re-casing it would change the output of a line that carries no unit at all, which is
    /// exactly what ER-13 forbids. <b>Nothing tested it.</b> Every other fixture in this file stores an UPPERCASE
    /// base code ("NOS", "KGS"), and uppercase is a FIXED POINT of canonicalisation, so no assertion in the suite
    /// could discriminate raw from canonicalised. <b>Verified by mutation</b> — wrapping that read in
    /// <c>Canonical(...)</c> built clean and left the whole suite green. That is the same trap that made the
    /// sibling ₹10.00 ER-13 rate assertion vacuous; see
    /// <see cref="A_unit_less_line_at_a_sub_rupee_rate_foots_exactly_on_the_er13_path"/>.</para>
    ///
    /// <para><b>The gap is reachable in production, not hypothetical.</b> <c>Unit</c>'s constructor and
    /// <c>UnitMasterViewModel</c> both only <c>Trim()</c> the entered UQC and never upper-case it, so a user
    /// typing "doz" in the Unit master stores <b>"doz"</b>. This test drives that stored-lowercase code end to end
    /// and pins what the shipped code does with it: it is passed through UNCHANGED, into the resolver, into
    /// INV-01, into EWB-01 and into the FILED GSTR-1 Table-12 row — all three writers emit <c>decl.Code</c>
    /// verbatim.</para>
    ///
    /// <para><b>This test locks the CURRENT documented behaviour and does not endorse it.</b> Whether a
    /// lowercase code is ACCEPTED by the portal is a separate, open question — UQC is a controlled vocabulary,
    /// and <see cref="UqcResolver.IsValid"/> canonicalises before checking, so a lowercase stored code passes
    /// validation here yet is emitted lowercase downstream. Resolving that is not a test change: ER-13's
    /// byte-identical export for a unit-less line rests on this passthrough, so any change to it must be a
    /// deliberate decision that updates this test.</para>
    /// </summary>
    [Fact]
    public void A_base_uqc_stored_lowercase_is_declared_exactly_as_stored()
    {
        var k = NewKit("Lowercase Uqc Co");
        var c = k.Vm.Company!;
        var inv = new InventoryService(c);

        // The Unit master stores the UQC TRIMMED ONLY — never upper-cased — so this is what a user who typed
        // "doz" actually has on disk. Asserted, because the whole test is vacuous if the store canonicalises.
        var lower = inv.CreateSimpleUnit("Dzn", "Dozens (entered lowercase)", unitQuantityCode: "doz");
        Assert.Equal("doz", c.FindUnit(lower.Id)!.UnitQuantityCode);

        // A distinct HSN so this item owns its own Table-12 row and no other fixture's row absorbs it.
        var grp = inv.CreateStockGroup("Lowercase Goods");
        var item = inv.CreateStockItem("Bagel", grp.Id, lower.Id);
        item.Gst = new StockItemGstDetails
        {
            HsnSac = "190590", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800,
        };
        inv.AddOpeningBalance(item.Id, k.MainGodownId, 240m, Money.FromRupees(1m));
        _storage.Save(c);

        // A line with NO unit — the ER-13 path, where the base code is the ONLY label available.
        var date = new DateOnly(2024, 4, 9);
        PostLinesOn(k, date, new Ln(item.Id, null, "5", "10.00"));

        var type = c.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Sales && t.IsActive);
        var posted = c.Vouchers.Single(v => v.TypeId == type.Id && v.Date == date);
        var il = Assert.Single(posted.InventoryLines);
        Assert.Null(il.UnitId);
        Assert.Equal(Money.FromRupees(50m), il.Value);

        // At the source: the declared code is EXACTLY what was stored — "DOZ" here is the re-casing mutation
        // this test exists to catch.
        var decl = UqcResolver.Declare(c, il, il.Quantity);
        Assert.Equal("doz", decl.Code);
        Assert.Equal("doz", decl.BaseCode);
        Assert.Equal(5m, decl.Quantity);
        Assert.Equal(10m, decl.Rate);

        // And at all three emission sites, which write decl.Code verbatim into filed/verified documents.
        Assert.Equal("doz", FirstEInvoiceItem(c, posted).GetProperty("Unit").GetString());
        Assert.Equal("doz", FirstEWayItem(c, posted).GetProperty("Unit").GetString());

        var hsn = Assert.Single(Gstr1.Build(c, date, date).HsnSummary, h => h.HsnSac == "190590");
        Assert.Equal("doz", hsn.Uqc);
        Assert.Equal("doz", hsn.BaseCode);
        Assert.Equal(5m, hsn.Quantity);
        Assert.Equal(50m, hsn.TaxableValue.Amount);
    }
}
