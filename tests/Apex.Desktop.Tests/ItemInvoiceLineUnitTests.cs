using System;
using System.IO;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.Tests;

/// <summary>
/// WI-10 <b>Gap 2</b> — the <b>item-INVOICE</b> line unit (schema v46).
///
/// <para>S8 made line units work on pure-stock (inventory) vouchers, but the CA's literal example is an
/// INVOICE line — "2 Dozen @ ₹10" — and an item-invoice line could not carry a unit at all, so that exact
/// entry was unreachable. This closes it, and locks the money.</para>
///
/// <para><b>The one proof that matters</b> (<see cref="Two_dozen_at_ten_rupees_on_a_sales_invoice"/>): from a
/// SINGLE posted sales invoice, asserted together — the line total is <b>₹20.00</b>, the stock movement is
/// <b>24 Nos</b>, and the <b>GST taxable value is ₹20.00</b>. Those three numbers are what the two-directional
/// money risk class threatens: pairing a base-normalised quantity (24) with a per-displayed rate (₹10) gives
/// ₹240 — the 12× overstatement; converting the rate into the value gives ₹1.67 — the 12× understatement. Both
/// are asserted against explicitly, so a future "fix" in either direction fails here.</para>
///
/// <para>Reachability is proven by DRIVING THE REAL UI — the Gateway menu to Sales, then Ctrl+I — never by
/// constructing a view model. A <c>ShowXMenu()</c> call proves nothing about reachability.</para>
///
/// Drives the real shell + entry view models over a throwaway <c>.db</c> — no UI toolkit.
/// </summary>
public sealed class ItemInvoiceLineUnitTests : IDisposable
{
    private const string GstinMaharashtra = "27AAPFU0939F1ZV";
    private static readonly DateOnly FyStart = new(2024, 4, 1);

    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public ItemInvoiceLineUnitTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexItemInvoiceUnitTests_" + Guid.NewGuid().ToString("N"));
        _storage = new CompanyStorage(_tempDir);
    }

    // ---------------------------------------------------------------- scaffolding

    private sealed class Kit
    {
        public required MainWindowViewModel Vm { get; init; }
        public required string CompanyName { get; init; }
        public required Guid EggId { get; init; }
        public required Guid MainGodownId { get; init; }
        public required Guid SalesLedgerId { get; init; }
        public required Guid CustomerId { get; init; }
        public required Guid NosId { get; init; }
        public required Guid DozNosId { get; init; }
    }

    /// <summary>
    /// A GST-enabled (home Maharashtra 27) company holding one Nos-measured item ("Egg") with a Doz-Nos
    /// compound unit (1 Doz = 12 Nos), opening stock, a Sales ledger and one in-state B2B customer.
    /// </summary>
    private Kit NewKit(string companyName, decimal openingQty = 240m)
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
        });

        var inv = new InventoryService(c);
        var grp = inv.CreateStockGroup("Goods");
        var nos = inv.CreateSimpleUnit("Nos", "Numbers", unitQuantityCode: "NOS");
        var doz = inv.CreateSimpleUnit("Doz", "Dozens", unitQuantityCode: "DOZ");
        // The corpus model: 1 × FIRST (the larger unit) = factor × TAIL (the base). 1 Doz = 12 Nos.
        var dozNos = inv.CreateCompoundUnit("Doz-Nos", "Dozen of 12 Numbers", doz.Id, nos.Id, 12);
        var main = c.MainLocation!.Id;

        var egg = inv.CreateStockItem("Egg", grp.Id, nos.Id);
        egg.Gst = new StockItemGstDetails
        {
            HsnSac = "040721", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800,
        };
        if (openingQty > 0m) inv.AddOpeningBalance(egg.Id, main, openingQty, Money.FromRupees(1m));

        var sales = AddLedger(c, "Sales", "Sales Accounts");
        var customer = AddLedger(c, "Local Customer", "Sundry Debtors");
        customer.PartyGst = new PartyGstDetails
        {
            RegistrationType = GstRegistrationType.Regular, Gstin = GstinMaharashtra, StateCode = "27",
        };

        _storage.Save(c);

        return new Kit
        {
            Vm = vm, CompanyName = companyName, EggId = egg.Id, MainGodownId = main,
            SalesLedgerId = sales.Id, CustomerId = customer.Id, NosId = nos.Id, DozNosId = dozNos.Id,
        };
    }

    private static DomainLedger AddLedger(Company c, string name, string groupName)
    {
        var group = c.FindGroupByName(groupName) ?? throw new InvalidOperationException($"No group '{groupName}'.");
        var ledger = new DomainLedger(Guid.NewGuid(), name, group.Id, Money.Zero, openingIsDebit: false);
        c.AddLedger(ledger);
        return ledger;
    }

    private Company Reload(string companyName) =>
        _storage.Load(_storage.ListCompanies().Single(e => e.Name == companyName));

    private static DateOnly AsOf(Company c) => c.FinancialYearStart.AddYears(1).AddDays(-1);

    private static decimal OnHand(Company c, Guid itemId, Guid godownId) =>
        new InventoryLedger(c).OnHand(itemId, godownId, AsOf(c));

    /// <summary>
    /// Highlights the menu row with <paramref name="label"/> using the REAL arrow API and activates it — the
    /// same path a keyboard user walks. Fails loudly, listing the rows, if it is not reachable.
    /// </summary>
    private static void DriveMenuTo(MainWindowViewModel vm, string label)
    {
        var target = -1;
        for (var i = 0; i < vm.Menu.Count; i++)
            if (vm.Menu[i].IsSelectable && vm.Menu[i].Label == label) { target = i; break; }
        Assert.True(target >= 0,
            $"\"{label}\" is not a selectable row in the current menu — the screen is UNREACHABLE. "
            + $"Rows: {string.Join(" | ", vm.Menu.Where(m => m.IsSelectable).Select(m => m.Label))}");

        var guard = 0;
        while (vm.SelectedIndex != target)
        {
            vm.MoveDown();
            Assert.True(++guard <= vm.Menu.Count * 2, $"Could not arrow onto \"{label}\".");
        }
        vm.ActivateSelected();
    }

    /// <summary>Walks Gateway → Vouchers → Sales → Ctrl+I, returning the live item-invoice entry VM.</summary>
    private static VoucherEntryViewModel DriveToSalesItemInvoice(MainWindowViewModel vm)
    {
        vm.ShowVouchersMenu();
        DriveMenuTo(vm, "Sales");
        Assert.Equal(Screen.VoucherEntry, vm.CurrentScreen);
        vm.ToggleItemInvoice();                       // Ctrl+I
        var entry = vm.VoucherEntry!;
        Assert.True(entry.IsItemInvoice);
        return entry;
    }

    // ================================================================ reachability (drive the real UI)

    [Fact]
    public void The_unit_picker_is_reachable_on_a_real_menu_reached_item_invoice_line()
    {
        var k = NewKit("Reachable Invoice Co");
        var entry = DriveToSalesItemInvoice(k.Vm);
        var line = entry.InventoryLines.First();

        // Before an item is picked there is nothing to state a unit in.
        Assert.False(line.ShowUnit);

        // Picking the Nos-measured item surfaces its base unit AND every compound reducing to it. Before this
        // slice the item-invoice grid passed no units at all, so UnitOptions was empty and ShowUnit stayed
        // false forever — the whole reason "2 Dozen @ ₹10" could not be entered on an invoice.
        line.SelectedItem = entry.StockItems.Single(i => i.Id == k.EggId);
        Assert.True(line.ShowUnit,
            "The unit picker never became visible on a real, menu-reached item-invoice line.");
        Assert.Equal(new[] { "Nos", "Doz-Nos" }, line.UnitOptions.Select(u => u.Symbol).ToArray());

        // It defaults to the item's own base unit, so an untouched line is byte-identical to a pre-v46 line.
        Assert.Equal(k.NosId, line.SelectedUnit!.Id);
        Assert.Null(line.UnitId);

        // Choosing the compound stamps it and converts the quantity.
        line.SelectedUnit = line.UnitOptions.Single(u => u.Id == k.DozNosId);
        line.QuantityText = "2";
        Assert.Equal(k.DozNosId, line.UnitId);
        Assert.Equal(24m, line.ParsedQuantityInBaseUnit);
    }

    // ================================================================ THE PROOF

    /// <summary>
    /// ONE posting of "2 Dozen @ ₹10" on a sales item invoice, with all three numbers asserted together:
    /// line total ₹20.00, stock movement 24 Nos, GST taxable value ₹20.00.
    /// </summary>
    [Fact]
    public void Two_dozen_at_ten_rupees_on_a_sales_invoice()
    {
        var k = NewKit("Two Dozen Co");
        var before = OnHand(k.Vm.Company!, k.EggId, k.MainGodownId);
        Assert.Equal(240m, before);

        var entry = DriveToSalesItemInvoice(k.Vm);
        Assert.True(entry.IsGstInvoice);
        entry.SelectedParty = entry.Parties.Single(p => p.Ledger?.Id == k.CustomerId);
        Assert.Equal(k.SalesLedgerId, entry.SelectedStockLedger!.Id);

        var line = entry.InventoryLines.First();
        line.SelectedItem = entry.StockItems.Single(i => i.Id == k.EggId);
        line.SelectedGodown = entry.Godowns.Single(g => g.Id == k.MainGodownId);
        line.SelectedUnit = line.UnitOptions.Single(u => u.Id == k.DozNosId);
        line.QuantityText = "2";
        line.RateText = "10.00";

        // The screen already agrees before the post: ₹20 goods, 18% ⇒ ₹1.80 + ₹1.80, party ₹23.60.
        Assert.Equal("20.00", entry.ItemsTotalText);
        Assert.Equal("1.80", entry.GstCgstText);
        Assert.Equal("1.80", entry.GstSgstText);
        Assert.Equal("23.60", entry.PartyTotalText);

        Assert.True(entry.CanAccept);
        Assert.True(entry.Accept(), entry.Message);

        var c = k.Vm.Company!;
        var asOf = AsOf(c);
        var type = c.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Sales && t.IsActive);
        var posted = c.Vouchers.Single(v => v.TypeId == type.Id);
        var il = Assert.Single(posted.InventoryLines);

        // The line is stored AS ENTERED: 2, per Doz-Nos, at ₹10 per Doz-Nos.
        Assert.Equal(2m, il.Quantity);
        Assert.Equal(k.DozNosId, il.UnitId);
        Assert.Equal(Money.FromRupees(10m), il.Rate);

        // ---- (1) LINE TOTAL = ₹20.00 -------------------------------------------------------------
        Assert.Equal(Money.FromRupees(20m), il.Value);
        Assert.Equal(20m, posted.InventoryLinesValue.Amount);
        Assert.NotEqual(Money.FromRupees(240m), il.Value);    // the 12× OVERSTATEMENT
        Assert.NotEqual(Money.FromRupees(1.67m), il.Value);   // the 12× UNDERSTATEMENT

        // ---- (2) STOCK MOVEMENT = 24 Nos ---------------------------------------------------------
        Assert.Equal(before - 24m, OnHand(c, k.EggId, k.MainGodownId));

        // ---- (3) GST TAXABLE VALUE = ₹20.00 ------------------------------------------------------
        // Read from the GST reporting path the returns actually use, not from the entry screen.
        var gstr1 = Report.BuildGstr1(c, FyStart, asOf);
        var b2b = gstr1.B2B.Single(r => r.InvoiceNumber == posted.Number);
        Assert.Equal(Money.FromRupees(20m), b2b.TaxableValue);
        Assert.Equal(Money.FromRupees(1.80m), b2b.Cgst);
        Assert.Equal(Money.FromRupees(1.80m), b2b.Sgst);

        var g3b = Report.BuildGstr3b(c, FyStart, asOf);
        Assert.Equal(Money.FromRupees(20m), g3b.TaxableOutwardValue);

        // The pairing invariant held (the engine would have refused the post otherwise): the Sales credit
        // equals the item-lines value to the paisa — this is precisely why Value must NOT be unit-converted.
        Assert.True(VoucherValidator.IsBalanced(posted));
        Assert.Equal(20m, -LedgerBalances.SignedClosing(c, c.FindLedger(k.SalesLedgerId)!, asOf));
        // Dr Customer = taxable + tax (a debtor closes debit-positive under SignedClosing).
        Assert.Equal(23.60m, LedgerBalances.SignedClosing(c, c.FindLedger(k.CustomerId)!, asOf));
    }

    /// <summary>
    /// The PURCHASE side of the same proof — the direction that carries the stock VALUE, and the exact site
    /// where S8 shipped a 12× overstatement before review caught it. Buying "2 Dozen @ ₹10" must add 24 Nos
    /// worth ₹20.00 to closing stock, not ₹240.00: the valuation normalises the quantity to the base unit, so
    /// it must divide the rate by the same factor. Asserted on a company with NO opening stock, so the closing
    /// value is attributable to this one invoice alone.
    /// </summary>
    [Fact]
    public void Two_dozen_at_ten_rupees_on_a_purchase_invoice_values_closing_stock_at_twenty_rupees()
    {
        var k = NewKit("Two Dozen Purchase Co", openingQty: 0m);
        var c0 = k.Vm.Company!;
        var supplier = AddLedger(c0, "Local Supplier", "Sundry Creditors");
        supplier.PartyGst = new PartyGstDetails
        {
            RegistrationType = GstRegistrationType.Regular, Gstin = GstinMaharashtra, StateCode = "27",
        };
        AddLedger(c0, "Purchases", "Purchase Accounts");

        k.Vm.ShowVouchersMenu();
        DriveMenuTo(k.Vm, "Purchase");
        Assert.Equal(Screen.VoucherEntry, k.Vm.CurrentScreen);
        k.Vm.ToggleItemInvoice();
        var entry = k.Vm.VoucherEntry!;
        Assert.True(entry.IsItemInvoice);
        entry.SelectedParty = entry.Parties.Single(p => p.Ledger?.Id == supplier.Id);

        var line = entry.InventoryLines.First();
        line.SelectedItem = entry.StockItems.Single(i => i.Id == k.EggId);
        line.SelectedGodown = entry.Godowns.Single(g => g.Id == k.MainGodownId);
        line.SelectedUnit = line.UnitOptions.Single(u => u.Id == k.DozNosId);
        line.QuantityText = "2";
        line.RateText = "10.00";
        Assert.Equal("20.00", entry.ItemsTotalText);
        Assert.True(entry.Accept(), entry.Message);

        var c = k.Vm.Company!;
        var asOf = AsOf(c);

        // Quantity in the item's base unit …
        Assert.Equal(24m, OnHand(c, k.EggId, k.MainGodownId));
        // … and the VALUE of exactly that quantity is the line total. ₹240 is the 12× overstatement that comes
        // from pairing the base quantity (24) with the per-Dozen rate (₹10); ₹1.67 is the understatement from
        // converting the rate twice. Both are named so neither can creep back in.
        var closing = new StockValuationService(c).ClosingValue(k.EggId, asOf);
        Assert.Equal(24m, closing.Quantity);
        Assert.Equal(Money.FromRupees(20m), closing.Value);
        Assert.NotEqual(Money.FromRupees(240m), closing.Value);
        Assert.NotEqual(Money.FromRupees(1.67m), closing.Value);
    }

    // ================================================================ persistence (v46) + print

    [Fact]
    public void The_line_unit_survives_a_save_and_reload_and_the_numbers_still_hold()
    {
        var k = NewKit("Persist Unit Co");
        var entry = DriveToSalesItemInvoice(k.Vm);
        entry.SelectedParty = entry.Parties.Single(p => p.Ledger?.Id == k.CustomerId);

        var line = entry.InventoryLines.First();
        line.SelectedItem = entry.StockItems.Single(i => i.Id == k.EggId);
        line.SelectedGodown = entry.Godowns.Single(g => g.Id == k.MainGodownId);
        line.SelectedUnit = line.UnitOptions.Single(u => u.Id == k.DozNosId);
        line.QuantityText = "2";
        line.RateText = "10.00";
        Assert.True(entry.Accept(), entry.Message);

        // Reopened from the v46 store, the unit is still on the line and every number is unchanged.
        var reloaded = Reload(k.CompanyName);
        var type = reloaded.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Sales && t.IsActive);
        var posted = reloaded.Vouchers.Single(v => v.TypeId == type.Id);
        var il = Assert.Single(posted.InventoryLines);

        Assert.Equal(k.DozNosId, il.UnitId);
        Assert.Equal(2m, il.Quantity);
        Assert.Equal(Money.FromRupees(20m), il.Value);
        Assert.Equal(240m - 24m, OnHand(reloaded, k.EggId, k.MainGodownId));
    }

    [Fact]
    public void The_printed_invoice_states_the_quantity_in_the_line_unit_it_was_billed_in()
    {
        var k = NewKit("Print Unit Co");
        var entry = DriveToSalesItemInvoice(k.Vm);
        entry.SelectedParty = entry.Parties.Single(p => p.Ledger?.Id == k.CustomerId);

        var line = entry.InventoryLines.First();
        line.SelectedItem = entry.StockItems.Single(i => i.Id == k.EggId);
        line.SelectedGodown = entry.Godowns.Single(g => g.Id == k.MainGodownId);
        line.SelectedUnit = line.UnitOptions.Single(u => u.Id == k.DozNosId);
        line.QuantityText = "2";
        line.RateText = "10.00";
        Assert.True(entry.Accept(), entry.Message);

        var c = k.Vm.Company!;
        var type = c.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Sales && t.IsActive);
        var posted = c.Vouchers.Single(v => v.TypeId == type.Id);

        var print = VoucherPrintProjector.ProjectInvoice(c, posted);
        var row = Assert.Single(print.Items);

        // "2 Doz-Nos" — NOT "2 Nos". Printing the base-unit symbol beside the line quantity would state a
        // quantity that never moved (24 Nos did) and the document would contradict the stock ledger.
        Assert.Equal("2 Doz-Nos", row.QuantityText);
        Assert.Equal(Money.FromRupees(20m), row.TaxableValue);
        Assert.Equal(Money.FromRupees(20m), print.TotalTaxable);
    }

    // ================================================================ ER-13 — nothing changes when unused

    [Fact]
    public void An_invoice_with_no_line_unit_is_completely_unchanged()
    {
        var k = NewKit("Unchanged Co");
        var entry = DriveToSalesItemInvoice(k.Vm);
        entry.SelectedParty = entry.Parties.Single(p => p.Ledger?.Id == k.CustomerId);

        // Same economics, entered the old way: 24 Nos @ ₹0.8333… is not expressible to the paisa, so use the
        // plain case the pre-v46 screen supported — 24 Nos @ ₹10 — and prove the line carries NO unit.
        var line = entry.InventoryLines.First();
        line.SelectedItem = entry.StockItems.Single(i => i.Id == k.EggId);
        line.SelectedGodown = entry.Godowns.Single(g => g.Id == k.MainGodownId);
        line.QuantityText = "24";
        line.RateText = "10.00";
        Assert.True(entry.Accept(), entry.Message);

        var c = k.Vm.Company!;
        var type = c.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Sales && t.IsActive);
        var il = Assert.Single(c.Vouchers.Single(v => v.TypeId == type.Id).InventoryLines);

        Assert.Null(il.UnitId);                                  // the picker defaulted to the base unit
        Assert.Equal(24m, il.Quantity);
        Assert.Equal(Money.FromRupees(240m), il.Value);
        Assert.Equal(240m - 24m, OnHand(c, k.EggId, k.MainGodownId));
    }

    // ================================================================ the guard

    [Fact]
    public void The_engine_refuses_an_item_invoice_line_whose_unit_does_not_reduce_to_the_items_base_unit()
    {
        var k = NewKit("Guarded Invoice Co");
        var c = k.Vm.Company!;

        var inv = new InventoryService(c);
        var g = inv.CreateSimpleUnit("g", "Grams");
        var kg = inv.CreateSimpleUnit("Kg", "Kilograms");
        var kgG = inv.CreateCompoundUnit("Kg-g", "Kilogram of 1000 Grams", kg.Id, g.Id, 1000);

        var sales = c.FindLedger(k.SalesLedgerId)!;
        var customer = c.FindLedger(k.CustomerId)!;

        // "1 Kg-g" of a Nos-measured item would silently scale on-hand by 1000 — and the value leg would
        // still foot, so the pairing invariant alone would never catch it.
        var voucher = new Voucher(
            Guid.NewGuid(),
            c.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Sales && t.IsActive).Id,
            FyStart,
            new[]
            {
                new EntryLine(customer.Id, Money.FromRupees(10m), DrCr.Debit),
                new EntryLine(sales.Id, Money.FromRupees(10m), DrCr.Credit),
            },
            partyId: customer.Id,
            inventoryLines: new[]
            {
                new VoucherInventoryLine(k.EggId, k.MainGodownId, 1m, Money.FromRupees(10m),
                    StockDirection.Outward, null, null, kgG.Id),
            });

        var ex = Assert.Throws<InvalidVoucherException>(() => new LedgerService(c).Post(voucher));
        Assert.Contains("does not reduce to the item's base unit", ex.Message);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch { /* best effort */ }
    }
}
