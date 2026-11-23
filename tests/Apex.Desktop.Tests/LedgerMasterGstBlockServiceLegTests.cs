using System;
using System.IO;
using System.Linq;
using System.Text;
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
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.Tests;

/// <summary>
/// 🔴 <b>CENSUS 6.4, IV-254 — THE NO-BLOCK HALF (wave 12, track ha, §2AB, IV-315..IV-317).</b>
///
/// <para><b>The defect, on <c>origin/main</c> 33ade75.</b> The Ledger master never wrote
/// <see cref="DomainLedger.SalesPurchaseGst"/>, so every ledger an operator created in the app carried no GST block.
/// <c>Gstr1.ServiceLegs</c> (and the INV-01 writer and the print projector after it) skips a ledger with no block, so
/// a service ledger under a group declaring 998311 / 18% was taxed by the rate walk and then filed NO GSTR-1 Table 12
/// row, GSTR-9 Table 17 = 0 against Table 4, INV-01 <c>HsnCd ""</c>, and the tax-invoice print was refused. The fix
/// is the vendor's shape, not a wider predicate: the Ledger master captures the block.</para>
///
/// <para><b>VENDOR (R7, opened by content 2026-11-22).</b>
/// <c>help.tallysolutions.com/india-gst-manage-hsn-code-sac-and-tax-rates-tally/</c>: "There are different places in
/// TallyPrime where you can define the GST rate and HSN/SAC details", listing "Ledger (sales/purchase)" and "Ledger
/// Group". <c>help.tallysolutions.com/set-up-gst-rate-hsn-sac-details/</c>: "In the master creation or alteration
/// screen, set GST applicability to Applicable", with "As per Company/Group" among the HSN/SAC and GST Rate options.
/// <c>help.tallysolutions.com/set-up-service-ledgers-for-gst/</c>: choose "Services" as the "Type of Supply".</para>
///
/// <para>Every ledger here is made on the REALISED Ledger master by keys through the real window tunnel (Space on the
/// GST Applicable box, Tab between fields, arrow keys in the pickers, Ctrl+A); every voucher is accepted with the real
/// Ctrl+A; every assertion is read off an emitted artefact of the book re-opened through a FRESH
/// <see cref="CompanyStorage"/>. Odd paisa: ₹12,345.67 @ 18% (CGST 1,111.11 + SGST 1,111.11), exempt ₹1,234.57.</para>
/// </summary>
public sealed class LedgerMasterGstBlockServiceLegTests : IDisposable
{
    private const string SellerGstin = "27AAPFU0939F1ZV";
    private const string BuyerGstin = "27AAACR5055K1Z5";
    private const string GroupSac = "998311";
    private const string ExemptSac = "999293";
    private static readonly DateOnly FyStart = new(2025, 4, 1);
    private static readonly DateOnly FyEnd = new(2026, 3, 31);
    private static readonly DateOnly SaleDate = new(2025, 4, 10);

    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "ApexLedgerGstBlock_" + Guid.NewGuid().ToString("N"));

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

    // ================================================================ the artefact

    /// <summary>
    /// 🔴 <b>THE CLAUSE.</b> A service ledger made by keys with GST Applicable, Type of Supply Services and NO SAC or
    /// rate of its own, under Sales Accounts declaring 998311 / 18%: GSTR-1 Table 12 carries a 998311 row of
    /// ₹12,345.67 / 1,111.11 / 1,111.11, GSTR-9 Table 17 equals Table 4 head by head, INV-01 files
    /// <c>HsnCd 998311</c> with <c>IsServc "Y"</c>, and the tax-invoice PDF draws <c>(998311) Tj</c>.
    /// <para><b>Mutation H1</b> (the write <c>if (ShowSalesPurchaseGst) target.SalesPurchaseGst = …</c> removed, which
    /// is main's behaviour) reddens this at the premise read-back (the reloaded ledger has no block).</para>
    /// </summary>
    [AvaloniaFact]
    public void A_service_ledger_keyed_with_no_sac_files_its_groups_sac_on_every_reloaded_document()
    {
        const string name = "Ledger GST Block Co";
        var saleId = KeyTheBook(name, out _);

        var c = ReopenFresh();
        var consultancy = c.FindLedgerByName("Consultancy Income")!;
        Assert.True(consultancy.SalesPurchaseGst is not null,
            "the Ledger master wrote no GST block: the ledger is not a service leg and every document below is empty.");
        Assert.Null(consultancy.SalesPurchaseGst!.HsnSac);                    // the premise: the ledger is silent
        Assert.Null(consultancy.SalesPurchaseGst.RateBasisPoints);
        Assert.Equal(GstSupplyType.Services, consultancy.SalesPurchaseGst.SupplyType);
        Assert.Equal(GstTaxability.Taxable, consultancy.SalesPurchaseGst.Taxability);
        var sale = c.FindVoucher(saleId)!;

        using (var g1 = JsonDocument.Parse(GstReturnJson.Gstr1(c, FyStart, FyEnd)))
        {
            var row = Assert.Single(g1.RootElement.GetProperty("hsn").EnumerateArray(),
                r => r.GetProperty("hsn_sac").GetString() == GroupSac);           // main: no row at all
            Assert.Equal(1_234_567L, row.GetProperty("txval_paisa").GetInt64());
            Assert.Equal(111_111L, row.GetProperty("camt_paisa").GetInt64());
            Assert.Equal(111_111L, row.GetProperty("samt_paisa").GetInt64());
        }
        using (var g9 = JsonDocument.Parse(GstReturnJson.Gstr9(c, FyStart, FyEnd)))
        {
            var root = g9.RootElement;
            var taxed = root.GetProperty("tbl17_hsn").EnumerateArray()
                .Where(r => r.GetProperty("cgst_paisa").GetInt64() != 0).ToList();
            var t17 = Assert.Single(taxed);
            Assert.Equal(GroupSac, t17.GetProperty("hsn_sac").GetString());
            Assert.Equal(root.GetProperty("tbl4_out_cgst_paisa").GetInt64(), t17.GetProperty("cgst_paisa").GetInt64());
            Assert.Equal(root.GetProperty("tbl4_out_sgst_paisa").GetInt64(), t17.GetProperty("sgst_paisa").GetInt64());
            Assert.Equal(root.GetProperty("tbl4n_taxable_value_paisa").GetInt64(),
                t17.GetProperty("taxable_value_paisa").GetInt64());               // main: Table 17 empty, Table 4 1,234,567
            Assert.Equal(1_234_567L, t17.GetProperty("taxable_value_paisa").GetInt64());
        }
        using (var inv = JsonDocument.Parse(Encoding.UTF8.GetString(EInvoiceJson.BuildInv01(c, sale))))
        {
            var item = Assert.Single(inv.RootElement.GetProperty("ItemList").EnumerateArray());
            Assert.Equal(GroupSac, item.GetProperty("HsnCd").GetString());   // main: ""
            Assert.Equal("Y", item.GetProperty("IsServc").GetString());
        }
        var projection = VoucherPrintProjector.ProjectInvoice(c, sale);
        var pdf = Encoding.Latin1.GetString(InvoicePdf.Render(projection, new PrintConfig(), new PageConfig()));
        Assert.Contains($"({GroupSac}) Tj", pdf, StringComparison.Ordinal);  // main: refused
        Assert.Contains("(12,345.67) Tj", pdf, StringComparison.Ordinal);
    }

    /// <summary>
    /// 🔴 <b>CENSUS 6.12 RESIDUAL (IV-317) — AN EXEMPT SERVICE LEG REACHES 3.1(c) AND TABLE 5.</b> A second ledger keyed
    /// with Taxability Exempt and SAC 999293 is sold for ₹1,234.57 in the same book. GSTR-1's exempt bucket already
    /// counted it; GSTR-3B 3.1(c) read stock lines only, so 3.1(c) and GSTR-9 Table 5 (Σ 3B) filed 0 and GSTR-9C 5R read
    /// 1,234.57. Now 3.1(c) = GSTR-1 exempt = Table 5 = 1,234.57 and 5R = 0.
    /// <para><b>Mutation H2</b> (the new service-leg loop in <c>Gstr3b.ExemptValueOf</c> removed) reddens this at 3.1(c)
    /// (expected 123,457, actual 0).</para>
    /// </summary>
    [AvaloniaFact]
    public void An_exempt_service_ledger_keyed_on_the_master_reaches_3_1_c_and_table_5_so_9c_5r_is_zero()
    {
        KeyTheBook("Exempt Service Leg Co", out _);
        var c = ReopenFresh();
        var training = c.FindLedgerByName("Training Income")!;
        Assert.Equal(GstTaxability.Exempt, training.SalesPurchaseGst!.Taxability);
        Assert.Equal(ExemptSac, training.SalesPurchaseGst.HsnSac);

        using var g3b = JsonDocument.Parse(GstReturnJson.Gstr3b(c, FyStart, FyEnd));
        Assert.Equal(123_457L, g3b.RootElement.GetProperty("tbl3_1c_nil_exempt_nongst_paisa").GetInt64()); // main: 0
        using var g1 = JsonDocument.Parse(GstReturnJson.Gstr1(c, FyStart, FyEnd));
        Assert.Equal(123_457L, g1.RootElement.GetProperty("nil_exempt_nongst_paisa").GetInt64());
        var exemptRow = Assert.Single(g1.RootElement.GetProperty("hsn").EnumerateArray(),
            r => r.GetProperty("hsn_sac").GetString() == ExemptSac);
        Assert.Equal(123_457L, exemptRow.GetProperty("txval_paisa").GetInt64());
        using var g9 = JsonDocument.Parse(GstReturnJson.Gstr9(c, FyStart, FyEnd));
        Assert.Equal(123_457L, g9.RootElement.GetProperty("tbl5_exempt_paisa").GetInt64());           // main: 0
        using var g9c = JsonDocument.Parse(GstReturnJson.Gstr9c(c, FyStart, FyEnd));
        Assert.Equal(0L, g9c.RootElement.GetProperty("tbl5r_unreconciled_turnover_paisa").GetInt64()); // main: 123,457
    }

    // ================================================================ the screen's write rules

    /// <summary>
    /// 🔴 <b>AN ALTERATION MUST NOT STRIP WHAT THE SCREEN DOES NOT SHOW.</b> An imported expense ledger carries reverse
    /// charge, an RCM category and a §17(5) block on its GST block. Altering only its SAC on the master keeps all three
    /// on the reloaded book.
    /// <para><b>Mutation H3</b> (<c>MergeSalesPurchaseGst</c> stops carrying the stored fields) reddens this at
    /// <c>ReverseChargeApplicable</c>.</para>
    /// </summary>
    [AvaloniaFact]
    public void Altering_the_sac_on_the_master_keeps_reverse_charge_and_itc_fields_the_screen_does_not_show()
    {
        var vm = NewGstCompany("Ledger GST Carry Co", out var window);
        try
        {
            var c = vm.Company!;
            var category = Guid.NewGuid();
            var expense = new DomainLedger(Guid.NewGuid(), "Legal Fees", c.FindGroupByName("Indirect Expenses")!.Id,
                Money.Zero, openingIsDebit: true)
            {
                SalesPurchaseGst = new StockItemGstDetails
                {
                    HsnSac = "998211", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800,
                    SupplyType = GstSupplyType.Services, ReverseChargeApplicable = true, RcmCategoryId = category,
                    ItcEligibility = ItcEligibility.BlockedSection17_5,
                    BlockedCreditCategory = BlockedCreditCategory.FoodBeveragesHealth,
                },
            };
            c.AddLedger(expense);
            new CompanyStorage(_dir).Save(c);

            vm.ShowLedgerAlter(expense.Id);
            var master = vm.LedgerMaster!;
            Assert.True(master.IsAltering);
            Pump(window);
            Assert.True(master.ShowSalesPurchaseGst);
            Assert.True(master.SalesPurchaseGst.Enabled);
            Assert.Equal("998211", master.SalesPurchaseGst.HsnSac);

            var hsn = Realised<TextBox>(window, "LedgerSalesPurchaseGstHsn");
            hsn.Focus();
            hsn.SelectAll();
            window.KeyTextInput("998212");
            Pump(window);
            Assert.Equal("998212", master.SalesPurchaseGst.HsnSac);
            window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
            Pump(window);
            Assert.True(master.Message?.Contains("altered", StringComparison.Ordinal) == true, master.Message);
        }
        finally { window.Close(); }

        var reloaded = ReopenFresh().FindLedgerByName("Legal Fees")!.SalesPurchaseGst!;
        Assert.Equal("998212", reloaded.HsnSac);
        Assert.True(reloaded.ReverseChargeApplicable);
        Assert.NotNull(reloaded.RcmCategoryId);
        Assert.Equal(ItcEligibility.BlockedSection17_5, reloaded.ItcEligibility);
        Assert.Equal(BlockedCreditCategory.FoodBeveragesHealth, reloaded.BlockedCreditCategory);
        Assert.Equal(1800, reloaded.RateBasisPoints);
    }

    /// <summary>
    /// The block is scoped: it is off screen on a party ledger, and a malformed SAC refuses the whole save (the
    /// domain's 4/6/8-digit rule), adding no ledger.
    /// <para><b>Mutation H4</b> (<c>ShowSalesPurchaseGst</c> drops the <c>!IsPartyGroup</c> term) reddens the first
    /// assertion; <b>H5</b> (the <c>TryBuild</c> refusal ignored) reddens the second.</para>
    /// </summary>
    [AvaloniaFact]
    public void The_block_is_absent_on_a_party_ledger_and_a_malformed_sac_refuses_the_save()
    {
        var vm = NewGstCompany("Ledger GST Scope Co", out var window);
        try
        {
            vm.ShowLedgerMaster();
            var master = vm.LedgerMaster!;
            master.SelectedGroup = master.Groups.First(g => g.Name == "Sundry Debtors");
            Pump(window);
            var box = window.GetVisualDescendants().OfType<CheckBox>()
                .FirstOrDefault(x => x.Name == "LedgerSalesPurchaseGstApplicable");
            Assert.True(box is null || !box.IsEffectivelyVisible,
                "the sales/purchase GST block must be off screen on a party ledger.");

            master.SelectedGroup = master.Groups.First(g => g.Name == "Sales Accounts");
            Pump(window);
            master.Name = "Bad SAC Income";
            var check = Realised<CheckBox>(window, "LedgerSalesPurchaseGstApplicable");
            check.Focus();
            window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);   // a CheckBox toggles on release
            Pump(window);
            Assert.True(master.SalesPurchaseGst.Enabled);
            var hsn = Realised<TextBox>(window, "LedgerSalesPurchaseGstHsn");
            hsn.Focus();
            window.KeyTextInput("99831");                                        // five digits
            Pump(window);
            window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
            Pump(window);
            Assert.Null(vm.Company!.FindLedgerByName("Bad SAC Income"));
            Assert.Contains("4, 6 or 8 digits", master.Message ?? string.Empty, StringComparison.Ordinal);
        }
        finally { window.Close(); }
    }

    // ================================================================ keys

    /// <summary>
    /// Keys the whole book: company GST, Sales Accounts' group block (998311 / 18%), the two service ledgers on the
    /// realised Ledger master by keys, a party, then the two accounting invoices with the real Ctrl+A. Returns the
    /// taxed sale's id.
    /// </summary>
    private Guid KeyTheBook(string name, out Guid exemptSaleId)
    {
        var vm = NewGstCompany(name, out var window);
        try
        {
            var c = vm.Company!;
            var sales = c.FindGroupByName("Sales Accounts")!;
            sales.Gst = new MasterGstDetails
            { HsnSac = GroupSac, Taxability = GstTaxability.Taxable, RateBasisPoints = 1800 };
            var buyer = new DomainLedger(Guid.NewGuid(), "Buyer", c.FindGroupByName("Sundry Debtors")!.Id,
                Money.Zero, openingIsDebit: true)
            {
                PartyGst = new PartyGstDetails
                { RegistrationType = GstRegistrationType.Regular, Gstin = BuyerGstin, StateCode = "27" },
                Mailing = new PartyMailingDetails
                {
                    MailingName = "Buyer Trading Co", Address = "12 Residency Road\nShivajinagar",
                    Country = "India", Pincode = "400012",
                },
            };
            c.AddLedger(buyer);
            new CompanyStorage(_dir).Save(c);

            vm.ShowLedgerMaster();
            Pump(window);
            KeyServiceLedger(window, vm, "Consultancy Income", sac: null, exempt: false);
            KeyServiceLedger(window, vm, "Training Income", sac: ExemptSac, exempt: true);

            var saleId = KeyServiceSale(window, vm, buyer.Id, "Consultancy Income", "12345.67");
            exemptSaleId = KeyServiceSale(window, vm, buyer.Id, "Training Income", "1234.57");
            return saleId;
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// One service ledger on the realised master: the Under group is Sales Accounts; then Space ticks GST Applicable,
    /// Tab reaches HSN/SAC (typed, or left blank), Tab the Taxability picker (Down for Exempt), Tab the rate (blank),
    /// Tab the Type of Supply picker (Down for Services), and Ctrl+A creates. Each hop asserts where focus landed, so a
    /// field the keyboard cannot reach fails here and not in a report.
    /// </summary>
    private static void KeyServiceLedger(MainWindow window, MainWindowViewModel vm, string name, string? sac, bool exempt)
    {
        vm.ShowLedgerMaster();
        var master = vm.LedgerMaster!;
        master.SelectedGroup = master.Groups.First(g => g.Name == "Sales Accounts");
        master.Name = name;
        Pump(window);
        Assert.True(master.ShowSalesPurchaseGst, "the GST block is not offered on a Sales Accounts ledger.");

        var check = Realised<CheckBox>(window, "LedgerSalesPurchaseGstApplicable");
        check.Focus();
        window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);   // a CheckBox toggles on release
        Pump(window);
        Assert.True(master.SalesPurchaseGst.Enabled, "Space did not tick GST Applicable.");

        Tab(window, "LedgerSalesPurchaseGstHsn");
        if (sac is not null) window.KeyTextInput(sac);
        Tab(window, "LedgerSalesPurchaseGstTaxability");
        Tab(window, "LedgerSalesPurchaseGstRate");
        Tab(window, "LedgerSalesPurchaseGstSupplyType");
        // The pickers, as an operator picks: Enter opens the closed picker, the arrow moves, Enter commits.
        StepDown(window, Realised<ComboBox>(window, "LedgerSalesPurchaseGstSupplyType"));      // Goods -> Services
        if (exempt) StepDown(window, Realised<ComboBox>(window, "LedgerSalesPurchaseGstTaxability")); // -> Exempt
        Assert.Equal(GstSupplyType.Services, master.SalesPurchaseGst.SelectedSupplyType!.Value);
        Assert.Equal(exempt ? GstTaxability.Exempt : GstTaxability.Taxable,
            master.SalesPurchaseGst.SelectedTaxability!.Value);
        Assert.Equal(sac ?? string.Empty, master.SalesPurchaseGst.HsnSac);

        window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
        Pump(window);
        Assert.True(vm.Company!.FindLedgerByName(name) is not null, master.Message);
        Assert.False(master.SalesPurchaseGst.Enabled, "the block carried into the next ledger.");
    }

    private static Guid KeyServiceSale(MainWindow window, MainWindowViewModel vm, Guid buyerId, string ledger, string amount)
    {
        vm.OpenVoucher(VoucherBaseType.Sales);
        var entry = vm.VoucherEntry!;
        entry.Date = SaleDate;
        window.KeyPressQwerty(PhysicalKey.H, RawInputModifiers.Control);   // As Voucher   -> Item Invoice
        window.KeyPressQwerty(PhysicalKey.H, RawInputModifiers.Control);   // Item Invoice -> Accounting Invoice
        Assert.True(entry.IsAccountingInvoice);
        entry.SelectedParty = entry.Parties.Single(p => p.Ledger?.Id == buyerId);
        while (entry.AccountingInvoiceLines.Count == 0) entry.AddAccountingInvoiceLine();
        var line = entry.AccountingInvoiceLines[0];
        line.SelectedLedger = entry.AccountingInvoiceLedgers.Single(l => l.Name == ledger);
        line.AmountText = amount;
        Pump(window);
        var before = vm.Company!.Vouchers.Count;
        window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
        Pump(window);
        Assert.True(vm.Company!.Vouchers.Count == before + 1, entry.Message);
        return vm.Company!.Vouchers[^1].Id;
    }

    private static void StepDown(MainWindow window, ComboBox box)
    {
        Assert.True(box.Focus(), $"{box.Name} cannot take keyboard focus.");
        Pump(window);
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Pump(window);
        Assert.True(box.IsDropDownOpen, $"Enter did not open {box.Name}.");
        window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
        Pump(window);
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Pump(window);
        Assert.False(box.IsDropDownOpen, $"Enter did not commit {box.Name}.");
    }

    private static void Tab(MainWindow window, string expected)
    {
        window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
        Pump(window);
        var focused = TopLevel.GetTopLevel(window)!.FocusManager!.GetFocusedElement() as Control;
        Assert.True(focused?.Name == expected,
            $"Tab landed on '{focused?.Name ?? focused?.GetType().Name ?? "(nothing)"}', expected '{expected}': the "
          + "field is not reachable from the keyboard in the order the screen shows it.");
    }

    // ================================================================ fixture

    private MainWindowViewModel NewGstCompany(string name, out MainWindow window)
    {
        var vm = new MainWindowViewModel(new CompanyStorage(_dir));
        window = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
        window.Show();
        vm.NewCompanyName = name;
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
            EInvoicingEnabled = true, EInvoiceApplicableFrom = FyStart,
        });
        new CompanyStorage(_dir).Save(c);
        return vm;
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
