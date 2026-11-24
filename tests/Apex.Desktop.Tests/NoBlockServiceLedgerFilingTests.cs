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
/// 🔴 <b>CENSUS 6.4, IV-319(a) — THE LEDGER MASTER'S DEFAULT STATE (wave 13, track ib, §2AI, IV-368 / IV-369).</b>
///
/// <para><b>The defect, on <c>origin/main</c> 6f8c936.</b> A service ledger created on the Ledger master WITHOUT
/// ticking GST Applicable (the box opens unticked) under Sales Accounts declaring 998311 / 18% carries no GST block.
/// The rate walk taxes it (CGST 1,111.11 + SGST 1,111.11 on ₹12,345.67) and the Accounting Invoice posts that tax,
/// but <c>Gstr1.ServiceLegs</c> skipped every ledger with no block, so on the reloaded book GSTR-1 filed no Table 12
/// row, GSTR-9 Table 17 was empty against Table 4, INV-01 filed <c>HsnCd ""</c> / <c>IsServc "N"</c> and the tax
/// invoice print was refused.</para>
///
/// <para><b>VENDOR (R7, opened by content 2026-11-24).</b> <c>help.tallysolutions.com/india-gst-manage-hsn-code-sac-and-tax-rates-tally/</c>
/// ("HSN/SAC &amp; GST Rate Hierarchy in TallyPrime"): GST rate and HSN/SAC details can be defined on ledgers and on
/// ledger groups, and "TallyPrime first checks the ledger for the details" before moving on.
/// <c>help.tallysolutions.com/set-up-service-ledgers-for-gst/</c> tells the operator to set GST applicability to
/// Applicable and states no default — so the box is NOT ticked by default; the readers widen instead.</para>
///
/// <para>The ledger is made on the REALISED Ledger master and saved with the real Ctrl+A (the GST box untouched); the
/// sale is accepted with the real Ctrl+A in Accounting Invoice mode reached by Ctrl+H; every assertion reads an
/// emitted artefact of the book re-opened through a FRESH <see cref="CompanyStorage"/>. The group's block is written
/// on the domain, as <c>LedgerMasterGstBlockServiceLegTests</c> does.</para>
/// </summary>
public sealed class NoBlockServiceLedgerFilingTests : IDisposable
{
    private const string SellerGstin = "27AAPFU0939F1ZV";
    private const string BuyerGstin = "27AAACR5055K1Z5";
    private const string GroupSac = "998311";
    private const string ExemptSac = "999293";
    private static readonly DateOnly FyStart = new(2025, 4, 1);
    private static readonly DateOnly FyEnd = new(2026, 3, 31);
    private static readonly DateOnly SaleDate = new(2025, 4, 10);

    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "ApexNoBlockService_" + Guid.NewGuid().ToString("N"));

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

    /// <summary>
    /// 🔴 <b>THE CLAUSE.</b> GSTR-1 Table 12 carries 998311 ₹12,345.67 / 1,111.11 / 1,111.11; GSTR-9 Table 17 ==
    /// Table 4 == GSTR-3B 3.1(a) head by head; INV-01 files <c>HsnCd 998311</c> with <c>IsServc "Y"</c>; the tax
    /// invoice PDF draws <c>(998311) Tj</c>.
    /// <para><b>Mutation B1</b> (<c>Gstr1.IsNoBlockServiceLedger</c> returns false — main's behaviour) reddens this at the
    /// Table 12 row. <b>B2</b> (<c>IsServiceSupplyLeg</c> back to the block's own SupplyType) reddens it at
    /// <c>IsServc</c>.</para>
    /// </summary>
    [AvaloniaFact]
    public void An_unticked_service_ledger_under_a_sac_group_files_on_every_reloaded_document()
    {
        var saleId = KeyTheBook("No Block Service Co", groupExempt: false, amount: "12345.67");

        var c = ReopenFresh();
        var consultancy = c.FindLedgerByName("Consultancy Income")!;
        Assert.Null(consultancy.SalesPurchaseGst);                   // the premise: the box was never ticked
        var sale = c.FindVoucher(saleId)!;
        Assert.True(sale.IsAccountingInvoice);

        using (var g1 = JsonDocument.Parse(GstReturnJson.Gstr1(c, FyStart, FyEnd)))
        {
            var row = Assert.Single(g1.RootElement.GetProperty("hsn").EnumerateArray(),
                r => r.GetProperty("hsn_sac").GetString() == GroupSac);           // main: no row at all
            Assert.Equal(1_234_567L, row.GetProperty("txval_paisa").GetInt64());
            Assert.Equal(111_111L, row.GetProperty("camt_paisa").GetInt64());
            Assert.Equal(111_111L, row.GetProperty("samt_paisa").GetInt64());
        }
        long cgst31a, sgst31a;
        using (var g3b = JsonDocument.Parse(GstReturnJson.Gstr3b(c, FyStart, FyEnd)))
        {
            cgst31a = g3b.RootElement.GetProperty("tbl3_1a_camt_paisa").GetInt64();
            sgst31a = g3b.RootElement.GetProperty("tbl3_1a_samt_paisa").GetInt64();
        }
        Assert.Equal(111_111L, cgst31a);
        using (var g9 = JsonDocument.Parse(GstReturnJson.Gstr9(c, FyStart, FyEnd)))
        {
            var root = g9.RootElement;
            var t17 = Assert.Single(root.GetProperty("tbl17_hsn").EnumerateArray());     // main: empty
            Assert.Equal(GroupSac, t17.GetProperty("hsn_sac").GetString());
            Assert.Equal(root.GetProperty("tbl4_out_cgst_paisa").GetInt64(), t17.GetProperty("cgst_paisa").GetInt64());
            Assert.Equal(root.GetProperty("tbl4_out_sgst_paisa").GetInt64(), t17.GetProperty("sgst_paisa").GetInt64());
            Assert.Equal(root.GetProperty("tbl4n_taxable_value_paisa").GetInt64(),
                t17.GetProperty("taxable_value_paisa").GetInt64());
            Assert.Equal(cgst31a, t17.GetProperty("cgst_paisa").GetInt64());               // Table 17 == 3.1(a)
            Assert.Equal(sgst31a, t17.GetProperty("sgst_paisa").GetInt64());
        }
        using (var inv = JsonDocument.Parse(Encoding.UTF8.GetString(EInvoiceJson.BuildInv01(c, sale))))
        {
            var item = Assert.Single(inv.RootElement.GetProperty("ItemList").EnumerateArray());
            Assert.Equal(GroupSac, item.GetProperty("HsnCd").GetString());   // main: ""
            Assert.Equal("Y", item.GetProperty("IsServc").GetString());     // main: "N"
        }
        Assert.True(GstReportSupport.IsServiceAccountingInvoice(c, sale));   // main: false (print refused)
        var projection = VoucherPrintProjector.ProjectInvoice(c, sale);
        var pdf = Encoding.Latin1.GetString(InvoicePdf.Render(projection, new PrintConfig(), new PageConfig()));
        Assert.Contains($"({GroupSac}) Tj", pdf, StringComparison.Ordinal);
        Assert.Contains("(12,345.67) Tj", pdf, StringComparison.Ordinal);
    }

    /// <summary>
    /// IV-369: the same unticked ledger under a group declaring Exempt / 999293 posts no tax, and its value is filed as
    /// exempt — GSTR-1's exempt bucket == GSTR-3B 3.1(c) == GSTR-9 Table 5, a 999293 zero-tax Table 12 row, 9C 5R 0.
    /// <para><b>Mutation B3</b> (the no-block arm of <c>IsNonTaxableServiceLedger</c> returns false) reddens this at the
    /// GSTR-1 exempt bucket (expected 123,457, actual 0).</para>
    /// </summary>
    [AvaloniaFact]
    public void An_unticked_service_ledger_under_an_exempt_sac_group_files_as_exempt_in_every_return()
    {
        KeyTheBook("No Block Exempt Co", groupExempt: true, amount: "1234.57");
        var c = ReopenFresh();
        Assert.Null(c.FindLedgerByName("Consultancy Income")!.SalesPurchaseGst);

        using var g1 = JsonDocument.Parse(GstReturnJson.Gstr1(c, FyStart, FyEnd));
        Assert.Equal(123_457L, g1.RootElement.GetProperty("nil_exempt_nongst_paisa").GetInt64());
        var row = Assert.Single(g1.RootElement.GetProperty("hsn").EnumerateArray(),
            r => r.GetProperty("hsn_sac").GetString() == ExemptSac);
        Assert.Equal(123_457L, row.GetProperty("txval_paisa").GetInt64());
        Assert.Equal(0L, row.GetProperty("camt_paisa").GetInt64());
        using var g3b = JsonDocument.Parse(GstReturnJson.Gstr3b(c, FyStart, FyEnd));
        Assert.Equal(123_457L, g3b.RootElement.GetProperty("tbl3_1c_nil_exempt_nongst_paisa").GetInt64());
        Assert.Equal(0L, g3b.RootElement.GetProperty("tbl3_1a_camt_paisa").GetInt64());
        using var g9 = JsonDocument.Parse(GstReturnJson.Gstr9(c, FyStart, FyEnd));
        Assert.Equal(123_457L, g9.RootElement.GetProperty("tbl5_exempt_paisa").GetInt64());
        using var g9c = JsonDocument.Parse(GstReturnJson.Gstr9c(c, FyStart, FyEnd));
        Assert.Equal(0L, g9c.RootElement.GetProperty("tbl5r_unreconciled_turnover_paisa").GetInt64());
    }

    // ================================================================ keys

    private Guid KeyTheBook(string name, bool groupExempt, string amount)
    {
        var vm = NewGstCompany(name, out var window);
        try
        {
            var c = vm.Company!;
            var sales = c.FindGroupByName("Sales Accounts")!;
            sales.Gst = groupExempt
                ? new MasterGstDetails { HsnSac = ExemptSac, Taxability = GstTaxability.Exempt }
                : new MasterGstDetails { HsnSac = GroupSac, Taxability = GstTaxability.Taxable, RateBasisPoints = 1800 };
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

            // The ledger, on the realised master, GST Applicable left as the screen opens it (unticked).
            vm.ShowLedgerMaster();
            Pump(window);
            var master = vm.LedgerMaster!;
            master.SelectedGroup = master.Groups.First(g => g.Name == "Sales Accounts");
            master.Name = "Consultancy Income";
            Pump(window);
            Assert.True(master.ShowSalesPurchaseGst, "the GST block is not offered on a Sales Accounts ledger.");
            Assert.False(master.SalesPurchaseGst.Enabled, "GST Applicable must open unticked (the default state).");
            window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
            Pump(window);
            Assert.True(vm.Company!.FindLedgerByName("Consultancy Income") is not null, master.Message);

            // The sale: Ctrl+H twice to Accounting Invoice, then Ctrl+A.
            vm.OpenVoucher(VoucherBaseType.Sales);
            var entry = vm.VoucherEntry!;
            entry.Date = SaleDate;
            for (var i = 0; i < 3 && !entry.IsAccountingInvoice; i++)
            {
                window.KeyPressQwerty(PhysicalKey.H, RawInputModifiers.Control);
                Pump(window);
            }
            Assert.True(entry.IsAccountingInvoice, "Ctrl+H did not reach Accounting Invoice mode within 3 presses.");
            entry.SelectedParty = entry.Parties.Single(p => p.Ledger?.Id == buyer.Id);
            for (var i = 0; i < 3 && entry.AccountingInvoiceLines.Count == 0; i++) entry.AddAccountingInvoiceLine();
            Assert.NotEmpty(entry.AccountingInvoiceLines);
            var line = entry.AccountingInvoiceLines[0];
            line.SelectedLedger = entry.AccountingInvoiceLedgers.Single(l => l.Name == "Consultancy Income");
            line.AmountText = amount;
            Pump(window);
            var before = vm.Company!.Vouchers.Count;
            window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
            Pump(window);
            Assert.True(vm.Company!.Vouchers.Count == before + 1, entry.Message);
            return vm.Company!.Vouchers[^1].Id;
        }
        finally { window.Close(); }
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

    private static void Pump(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.Measure(new Size(1440, 900));
        window.Arrange(new Rect(0, 0, 1440, 900));
        Dispatcher.UIThread.RunJobs();
    }
}
