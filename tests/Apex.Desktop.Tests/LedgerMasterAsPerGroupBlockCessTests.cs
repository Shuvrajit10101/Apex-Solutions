using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Desktop.Views;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Io;
using Apex.Ledger.Services;
using Xunit;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.Tests;

/// <summary>
/// 🔴 <b>WAVE 12, TRACK ha, IV-316 (6) — TICKING GST APPLICABLE ON A SALES LEDGER MUST NOT DROP THE ITEM'S CESS.</b>
///
/// <para><b>The defect (wave-12 review R1, probe P3).</b> The Ledger master writes, for GST Applicable ticked with
/// every field left at its default, a block that is Taxable with no HSN/SAC and no rate — this screen's "As per
/// Company/Group". Under the shipped Ledger-first order that block was the first rung of
/// <c>GstService.ResolveDetailBlock</c>, so <c>ResolveCess</c> keyed the dated Compensation-Cess master on ITS HSN
/// (none) and an item invoice of a cess-bearing item through that ledger posted no cess, while the same invoice
/// through an unticked ledger did.</para>
///
/// <para>The sales ledger is made on the REALISED Ledger master by keys (Space on GST Applicable, Ctrl+A); the item
/// invoice is switched to by the real Ctrl+H and accepted with the real Ctrl+A; every figure is read off the book
/// re-opened through a FRESH <see cref="CompanyStorage"/>. Odd paisa: 7 Nos @ ₹1,234.57 = ₹8,641.99; cess 5% =
/// ₹432.0995 → ₹432.10.</para>
///
/// <para><b>Mutation F-M1</b> (the ledger-rung skip in <c>ResolveDetailBlock</c> removed) reddens
/// <see cref="An_item_invoice_through_a_ticked_default_sales_ledger_carries_the_items_dated_cess"/> at the posted
/// cess line (none).</para>
/// </summary>
public sealed class LedgerMasterAsPerGroupBlockCessTests : IDisposable
{
    private const string SellerGstin = "27AAPFU0939F1ZV";
    private const string BuyerGstin = "27AAACR5055K1Z5";
    private const string CessHsn = "24022090";
    private static readonly DateOnly FyStart = new(2025, 4, 1);
    private static readonly DateOnly FyEnd = new(2026, 3, 31);
    private static readonly DateOnly SaleDate = new(2025, 4, 10);

    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "ApexLedgerAsPerGroupCess_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    [AvaloniaFact]
    public void An_item_invoice_through_a_ticked_default_sales_ledger_carries_the_items_dated_cess()
    {
        Guid tickedSale, plainSale;
        var vm = new MainWindowViewModel(new CompanyStorage(_dir));
        var window = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
        window.Show();
        try
        {
            vm.NewCompanyName = "As Per Group Cess Co";
            vm.CreateCompany();
            var c = vm.Company!;
            c.FinancialYearStart = FyStart;
            c.BooksBeginFrom = FyStart;
            c.Address = "Unit 4, Fort Industrial Estate\nBallard Pier";
            c.Pin = "400001";
            new GstService(c).EnableGst(new GstConfig
            {
                HomeStateCode = "27", Gstin = SellerGstin, RegistrationType = GstRegistrationType.Regular,
                ApplicableFrom = FyStart, Periodicity = GstReturnPeriodicity.Monthly,
            });
            Assert.Equal(GstDetailSource.LedgerFirst, c.Gst!.SourceOfGstRate);   // the shipped order: ledger first
            c.Gst.AddCessRate(new GstCessRate(Guid.NewGuid(), CessHsn, CessValuationMode.AdValorem,
                cessRateBasisPoints: 500, cessPerUnit: Money.Zero, cessRspFactorMillis: 0,
                effectiveFrom: FyStart, effectiveTo: null, label: "test 5% ad valorem"));
            var inv = new InventoryService(c);
            var nos = inv.CreateSimpleUnit("Nos", "Numbers", unitQuantityCode: "NOS");
            var item = inv.CreateStockItem("Cigarettes", inv.CreateStockGroup("Tobacco").Id, nos.Id);
            item.Gst = new StockItemGstDetails
            { HsnSac = CessHsn, Taxability = GstTaxability.Taxable, RateBasisPoints = 2800 };
            inv.AddOpeningBalance(item.Id, c.MainLocation!.Id, 100m, Money.FromRupees(10m));
            var plain = new DomainLedger(Guid.NewGuid(), "Sales Plain", c.FindGroupByName("Sales Accounts")!.Id,
                Money.Zero, openingIsDebit: false);
            c.AddLedger(plain);
            var buyer = new DomainLedger(Guid.NewGuid(), "Buyer", c.FindGroupByName("Sundry Debtors")!.Id,
                Money.Zero, openingIsDebit: true)
            {
                PartyGst = new PartyGstDetails
                { RegistrationType = GstRegistrationType.Regular, Gstin = BuyerGstin, StateCode = "27" },
            };
            c.AddLedger(buyer);
            new CompanyStorage(_dir).Save(c);

            // The sales ledger, by keys: Space ticks GST Applicable, every field is left at its default, Ctrl+A.
            vm.ShowLedgerMaster();
            var master = vm.LedgerMaster!;
            master.SelectedGroup = master.Groups.First(g => g.Name == "Sales Accounts");
            master.Name = "Sales Ticked";
            Pump(window);
            var check = Realised<CheckBox>(window, "LedgerSalesPurchaseGstApplicable");
            check.Focus();
            window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);   // a CheckBox toggles on release
            Pump(window);
            Assert.True(master.SalesPurchaseGst.Enabled, "Space did not tick GST Applicable.");
            window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
            Pump(window);
            var ticked = c.FindLedgerByName("Sales Ticked");
            Assert.True(ticked is not null, master.Message);

            tickedSale = KeyItemSale(window, vm, buyer.Id, ticked!.Id, item.Id);
            plainSale = KeyItemSale(window, vm, buyer.Id, plain.Id, item.Id);
        }
        finally { window.Close(); }

        var rc = ReopenFresh();
        var block = rc.FindLedgerByName("Sales Ticked")!.SalesPurchaseGst;
        Assert.True(block is not null, "the premise: the ticked ledger carries the screen's block on the reloaded book.");
        Assert.Null(block!.HsnSac);
        Assert.Null(block.RateBasisPoints);
        Assert.Equal(GstTaxability.Taxable, block.Taxability);

        // The control first: the unticked ledger's invoice carries the cess, so the fixture can produce one.
        Assert.Equal(432.10m, CessOn(rc, plainSale));
        // THE CLAUSE: the ticked default ledger is "As per Company/Group" and must not mask the item's cess.
        Assert.Equal(432.10m, CessOn(rc, tickedSale));                              // before the fix: no cess line

        using var g1 = JsonDocument.Parse(GstReturnJson.Gstr1(rc, FyStart, FyEnd));
        var row = Assert.Single(g1.RootElement.GetProperty("hsn").EnumerateArray(),
            r => r.GetProperty("hsn_sac").GetString() == CessHsn);
        Assert.Equal(2 * 864_199L, row.GetProperty("txval_paisa").GetInt64());
        Assert.Equal(2 * 43_210L, row.GetProperty("csamt_paisa").GetInt64());     // before the fix: 43,210
    }

    private static decimal CessOn(Company c, Guid voucherId)
    {
        var v = c.FindVoucher(voucherId)!;
        var cess = v.Lines.Where(l => l.Gst is { TaxHead: GstTaxHead.Cess }).ToList();
        Assert.True(cess.Count == 1,
            $"expected one Compensation Cess line on the posted invoice, found {cess.Count}: "
            + string.Join(", ", v.Lines.Select(l => $"{c.FindLedger(l.LedgerId)?.Name}:{l.Side}:{l.Amount.Amount}")));
        return cess[0].Amount.Amount;
    }

    private static Guid KeyItemSale(MainWindow window, MainWindowViewModel vm, Guid buyerId, Guid salesLedgerId, Guid itemId)
    {
        vm.OpenVoucher(VoucherBaseType.Sales);
        var entry = vm.VoucherEntry!;
        entry.Date = SaleDate;
        window.KeyPressQwerty(PhysicalKey.H, RawInputModifiers.Control);   // As Voucher -> Item Invoice
        Pump(window);
        Assert.Equal(VoucherEntryMode.ItemInvoice, entry.Mode);
        entry.SelectedParty = entry.Parties.Single(p => p.Ledger?.Id == buyerId);
        entry.SelectedStockLedger = entry.StockLedgers.Single(l => l.Id == salesLedgerId);
        var line = entry.InventoryLines[0];
        line.SelectedItem = entry.StockItems.Single(i => i.Id == itemId);
        line.SelectedGodown = entry.Godowns.Single(g => g.Id == vm.Company!.MainLocation!.Id);
        line.QuantityText = "7";
        line.RateText = "1234.57";
        Pump(window);
        var before = vm.Company!.Vouchers.Count;
        window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
        Pump(window);
        Assert.True(vm.Company!.Vouchers.Count == before + 1, entry.Message);
        return vm.Company!.Vouchers[^1].Id;
    }

    private Company ReopenFresh()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        var fresh = new CompanyStorage(_dir);
        return fresh.Load(fresh.ListCompanies().Single());
    }

    private static T Realised<T>(Window window, string name) where T : Control
    {
        var control = window.GetVisualDescendants().OfType<T>().FirstOrDefault(x => x.Name == name);
        Assert.True(control is { IsEffectivelyVisible: true }, $"no realised, visible '{name}' on the Ledger master.");
        return control!;
    }

    private static void Pump(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.Measure(new Size(1440, 900));
        window.Arrange(new Rect(0, 0, 1440, 900));
        Dispatcher.UIThread.RunJobs();
    }
}
