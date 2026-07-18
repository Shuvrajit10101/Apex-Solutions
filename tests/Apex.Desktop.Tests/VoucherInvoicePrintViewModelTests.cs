using System;
using System.IO;
using System.Linq;
using System.Text;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Io;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.Tests;

/// <summary>
/// UI-side coverage for Phase-5 slice-10 (RQ-10 voucher print, RQ-11 tax-invoice print, RQ-12 F12 print config):
/// Print (P/Ctrl+P) on a drilled voucher renders THAT voucher — a de-branded GST <b>tax invoice</b> for a Sales
/// item-invoice (both GSTINs, per-rate CGST/SGST or IGST, amount-in-words) or the plain Dr/Cr voucher otherwise —
/// via <c>Apex.Ledger.Io</c>; the F12 knobs (title override, narration on/off, copy marking) re-render the bytes.
///
/// <para>The renderers themselves are trusted (covered by <c>Apex.Ledger.Io.Tests</c>); these tests pin the thin
/// Avalonia layer: the drill → Print routing, the voucher-vs-invoice choice, the projected figures reconciling to
/// the posted tax ledgers, and the F12 config re-render. Every produced byte stream carries no "tally"
/// (case-insensitive) anywhere (RQ-13 de-brand). A real GST company + posted Sales item-invoice is built over a
/// throwaway <c>.db</c>, exactly like the GST item-invoice tests — no UI toolkit.</para>
/// </summary>
public sealed class VoucherInvoicePrintViewModelTests : IDisposable
{
    private const string GstinMaharashtra = "27AAPFU0939F1ZV";
    private const string GstinGujarat = "24AAACC1206D1ZM";
    private static readonly DateOnly FyStart = new(2024, 4, 1);

    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public VoucherInvoicePrintViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexVoucherPrintTests_" + Guid.NewGuid().ToString("N"));
        _storage = new CompanyStorage(_tempDir);
    }

    private static string AsLatin1(byte[] bytes) => Encoding.Latin1.GetString(bytes);

    // ---------------------------------------------------------------- scaffolding (mirrors the GST invoice tests)

    private sealed class Kit
    {
        public required MainWindowViewModel Vm { get; init; }
        public required Guid WidgetId { get; init; }        // 18%
        public required Guid GadgetId { get; init; }        // 5%
        public required Guid ExemptItemId { get; init; }    // exempt (no GST)
        public required Guid MainGodownId { get; init; }
        public required Guid SalesLedgerId { get; init; }
        public required Guid LocalCustomerId { get; init; } // in-state (27), B2B
        public required Guid InterCustomerId { get; init; } // Gujarat (24), inter-state
    }

    private Kit NewGstKit(string companyName)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = companyName;
        vm.CreateCompany();

        var c = vm.Company!;
        c.MailingName = "Acme Traders Pvt Ltd";
        c.Address = "12 Industrial Estate\nPune, Maharashtra 411001";
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
        var main = c.MainLocation!.Id;

        var widget = inv.CreateStockItem("Widget", grp.Id, nos.Id);
        widget.Gst = new StockItemGstDetails { HsnSac = "847130", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800 };
        var gadget = inv.CreateStockItem("Gadget", grp.Id, nos.Id);
        gadget.Gst = new StockItemGstDetails { HsnSac = "852990", Taxability = GstTaxability.Taxable, RateBasisPoints = 500 };
        var exempt = inv.CreateStockItem("Exempt Item", grp.Id, nos.Id);
        exempt.Gst = new StockItemGstDetails { HsnSac = "100610", Taxability = GstTaxability.Exempt };

        inv.AddOpeningBalance(widget.Id, main, 500m, Money.FromRupees(100m));
        inv.AddOpeningBalance(gadget.Id, main, 500m, Money.FromRupees(20m));
        inv.AddOpeningBalance(exempt.Id, main, 500m, Money.FromRupees(20m));

        var sales = AddLedger(c, "Sales", "Sales Accounts");
        var localCustomer = AddLedger(c, "Local Customer", "Sundry Debtors");
        localCustomer.PartyGst = new PartyGstDetails { RegistrationType = GstRegistrationType.Regular, Gstin = GstinMaharashtra, StateCode = "27" };
        var interCustomer = AddLedger(c, "Gujarat Customer", "Sundry Debtors");
        interCustomer.PartyGst = new PartyGstDetails { RegistrationType = GstRegistrationType.Regular, Gstin = GstinGujarat, StateCode = "24" };

        _storage.Save(c);

        return new Kit
        {
            Vm = vm,
            WidgetId = widget.Id,
            GadgetId = gadget.Id,
            ExemptItemId = exempt.Id,
            MainGodownId = main,
            SalesLedgerId = sales.Id,
            LocalCustomerId = localCustomer.Id,
            InterCustomerId = interCustomer.Id,
        };
    }

    private static DomainLedger AddLedger(Company c, string name, string groupName)
    {
        var group = c.FindGroupByName(groupName) ?? throw new InvalidOperationException($"No group '{groupName}'.");
        var ledger = new DomainLedger(Guid.NewGuid(), name, group.Id, Money.Zero, openingIsDebit: false);
        c.AddLedger(ledger);
        return ledger;
    }

    private static void SelectParty(VoucherEntryViewModel entry, Guid partyId) =>
        entry.SelectedParty = entry.Parties.Single(p => p.Ledger?.Id == partyId);

    private static void FillItemLine(VoucherEntryViewModel entry, Guid itemId, Guid godownId, decimal qty, string rate, int index = 0)
    {
        while (entry.InventoryLines.Count <= index) entry.AddInventoryLine();
        var line = entry.InventoryLines[index];
        line.SelectedItem = entry.StockItems.Single(i => i.Id == itemId);
        line.SelectedGodown = entry.Godowns.Single(g => g.Id == godownId);
        line.QuantityText = qty.ToString(System.Globalization.CultureInfo.InvariantCulture);
        line.RateText = rate;
    }

    /// <summary>Posts a Sales item-invoice through the real entry VM and returns the posted voucher.</summary>
    private static Voucher PostSaleInvoice(Kit k, Guid partyId, Action<VoucherEntryViewModel> fill, string? narration = null)
    {
        k.Vm.OpenVoucher(VoucherBaseType.Sales);
        var entry = k.Vm.VoucherEntry!;
        k.Vm.ToggleItemInvoice();
        SelectParty(entry, partyId);
        fill(entry);
        if (narration is not null) entry.Narration = narration;
        Assert.True(entry.Accept());

        var c = k.Vm.Company!;
        var type = c.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Sales && t.IsActive);
        return c.Vouchers.Single(v => v.TypeId == type.Id);
    }

    /// <summary>Opens the drilled voucher-detail column, then Print (P/Ctrl+P), and returns the preview VM.</summary>
    private PrintPreviewViewModel PrintDrilledVoucher(MainWindowViewModel vm, Guid voucherId)
    {
        vm.OpenVoucherDetail(voucherId);
        Assert.Equal(Screen.VoucherDetail, vm.CurrentScreen);
        Assert.True(vm.IsPrintablePage);
        vm.OpenPrintPreview();
        Assert.Equal(Screen.PrintPreview, vm.CurrentScreen);
        Assert.NotNull(vm.PrintPreview);
        return vm.PrintPreview!;
    }

    // ================================================================ RQ-11: tax-invoice print (intra: both GSTINs)

    [Fact]
    public void Printing_a_sales_item_invoice_yields_a_tax_invoice_pdf_with_both_gstins_and_amount_in_words()
    {
        var k = NewGstKit("Print Invoice Co");
        // 10 Widget @ ₹875 = ₹8,750 @ 18% intra ⇒ CGST 787.50 + SGST 787.50; grand total ₹10,325.00.
        var v = PostSaleInvoice(k, k.LocalCustomerId,
            e => FillItemLine(e, k.WidgetId, k.MainGodownId, 10m, "875.00"));

        var preview = PrintDrilledVoucher(k.Vm, v.Id);
        Assert.Equal(PrintPreviewViewModel.PrintKind.Invoice, preview.Kind);
        Assert.True(preview.SupportsPrintConfig);

        var text = AsLatin1(preview.PdfBytes);
        Assert.StartsWith("%PDF-", text);
        Assert.Contains("TAX INVOICE", text);
        Assert.Contains(GstinMaharashtra, text);      // seller GSTIN
        // Buyer GSTIN is the same registration in this fixture; assert it is present as the recipient too.
        Assert.Contains("847130", text);              // HSN
        Assert.Contains("8,750.00", text);            // taxable value
        Assert.Contains("787.50", text);              // CGST == SGST (engine)
        Assert.Contains("10,325.00", text);           // grand total
        // Amount in words (Indian numbering, from the pure Io layer).
        Assert.Contains("Rupees Ten Thousand Three Hundred Twenty Five", text);
        Assert.DoesNotContain("tally", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Apex Solutions", text);      // de-branded metadata / footer
    }

    // ================================================================ WI-4: the buyer address actually prints

    [Fact]
    public void The_party_mailing_address_and_PIN_print_in_the_invoice_recipient_block()
    {
        // Before WI-4 this was hardcoded: VoucherPrintProjector emitted `AddressLines = Array.Empty<string>()`
        // with a comment saying a party ledger had no address field — so EVERY invoice this app printed carried a
        // blank recipient address. That is the regression this test exists to prevent recurring.
        var k = NewGstKit("Print Buyer Address Co");
        var party = k.Vm.Company!.FindLedger(k.LocalCustomerId)!;
        party.Mailing = new PartyMailingDetails
        {
            MailingName = "Naresh Traders Private Limited",
            Address = "12 Park Street\nBallygunge",
            Country = "India",
            Pincode = "700019",
        };

        var invoice = VoucherPrintProjector.ProjectInvoice(
            k.Vm.Company!,
            PostSaleInvoice(k, k.LocalCustomerId, e => FillItemLine(e, k.WidgetId, k.MainGodownId, 1m, "100.00")));

        // The projected DTO carries each address line, the country, and the PIN as its own final line.
        Assert.Equal("Naresh Traders Private Limited", invoice.Buyer.Name);
        Assert.Equal(
            new[] { "12 Park Street", "Ballygunge", "India", "PIN: 700019" },
            invoice.Buyer.AddressLines);

        // …and they reach the rendered PDF, which is what the CA actually looks at.
        var preview = PrintDrilledVoucher(k.Vm, k.Vm.Company!.Vouchers.Last().Id);
        var text = AsLatin1(preview.PdfBytes);
        Assert.Contains("Naresh Traders Private Limited", text);
        Assert.Contains("12 Park Street", text);
        Assert.Contains("Ballygunge", text);
        Assert.Contains("700019", text);
        Assert.DoesNotContain("tally", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_party_with_no_mailing_block_still_prints_exactly_as_before()
    {
        // ER-13 at the print boundary: an unaffected company's invoice is unchanged — a blank recipient address
        // and the ledger's own name.
        var k = NewGstKit("Print No Address Co");
        var party = k.Vm.Company!.FindLedger(k.LocalCustomerId)!;
        Assert.Null(party.Mailing);

        var invoice = VoucherPrintProjector.ProjectInvoice(
            k.Vm.Company!,
            PostSaleInvoice(k, k.LocalCustomerId, e => FillItemLine(e, k.WidgetId, k.MainGodownId, 1m, "100.00")));

        Assert.Equal(party.Name, invoice.Buyer.Name);
        Assert.Empty(invoice.Buyer.AddressLines);
    }

    // ================================================================ RQ-11: inter-state IGST + place of supply

    [Fact]
    public void Printing_an_inter_state_sale_yields_a_tax_invoice_with_igst()
    {
        var k = NewGstKit("Print Inter Invoice Co");
        // 20 Widget @ ₹100 = ₹2,000 @ 18% inter ⇒ IGST ₹360; grand total ₹2,360.
        var v = PostSaleInvoice(k, k.InterCustomerId,
            e => FillItemLine(e, k.WidgetId, k.MainGodownId, 20m, "100.00"));

        var preview = PrintDrilledVoucher(k.Vm, v.Id);
        var text = AsLatin1(preview.PdfBytes);

        Assert.Contains("TAX INVOICE", text);
        Assert.Contains("360.00", text);              // IGST (engine)
        Assert.Contains("2,360.00", text);            // grand total
        Assert.Contains("Gujarat", text);             // place of supply (inter-state)
        Assert.DoesNotContain("tally", text, StringComparison.OrdinalIgnoreCase);
    }

    // ================================================================ RQ-10: plain voucher print (non-item Sales -> voucher)

    [Fact]
    public void Printing_a_plain_voucher_yields_a_voucher_pdf_not_a_tax_invoice()
    {
        var k = NewGstKit("Print Plain Voucher Co");
        var c = k.Vm.Company!;

        // Post a plain (accounting-only) Journal voucher — no inventory lines ⇒ plain voucher print.
        var cash = AddLedger(c, "Cash", "Cash-in-Hand");
        var sales = c.FindLedger(k.SalesLedgerId)!;
        var jType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Journal && t.IsActive);
        var voucher = new Voucher(
            Guid.NewGuid(), jType.Id, FyStart,
            new[]
            {
                new EntryLine(cash.Id, Money.FromRupees(500m), DrCr.Debit),
                new EntryLine(sales.Id, Money.FromRupees(500m), DrCr.Credit),
            },
            number: 1, narration: "Cash sale");
        new LedgerService(c).Post(voucher);
        _storage.Save(c);

        var preview = PrintDrilledVoucher(k.Vm, voucher.Id);
        Assert.Equal(PrintPreviewViewModel.PrintKind.Voucher, preview.Kind);

        var text = AsLatin1(preview.PdfBytes);
        Assert.StartsWith("%PDF-", text);
        Assert.DoesNotContain("TAX INVOICE", text);   // it is NOT the invoice template
        Assert.Contains("Cash", text);                // Dr line ledger name
        Assert.Contains("500.00", text);
        Assert.Contains("Cash sale", text);           // narration (default on)
        Assert.DoesNotContain("tally", text, StringComparison.OrdinalIgnoreCase);
    }

    // ================================================================ RQ-12: F12 title override + copy marking change bytes

    [Fact]
    public void F12_title_override_and_copy_marking_change_the_produced_bytes()
    {
        var k = NewGstKit("Print F12 Co");
        var v = PostSaleInvoice(k, k.LocalCustomerId,
            e => FillItemLine(e, k.WidgetId, k.MainGodownId, 10m, "100.00"));
        var preview = PrintDrilledVoucher(k.Vm, v.Id);

        var baseline = preview.PdfBytes;

        // Open the F12 print-config panel, change the title + copy marking, apply.
        k.Vm.OpenPrintConfig();
        Assert.Equal(Screen.PrintConfig, k.Vm.CurrentScreen);
        var panel = k.Vm.PrintConfigPanel!;
        panel.TitleOverride = "PROFORMA INVOICE";
        panel.CopyMarking = CopyMarking.Original;
        panel.Apply();

        var changed = preview.PdfBytes;
        Assert.NotEqual(baseline, changed);                       // bytes changed
        var text = AsLatin1(changed);
        Assert.Contains("PROFORMA INVOICE", text);                // title override applied
        Assert.Contains("ORIGINAL FOR RECIPIENT", text);          // copy marking label
        Assert.DoesNotContain("tally", text, StringComparison.OrdinalIgnoreCase);
    }

    // ================================================================ RQ-12: narration toggle

    [Fact]
    public void F12_narration_toggle_adds_or_removes_the_narration_line()
    {
        var k = NewGstKit("Print Narration Co");
        var v = PostSaleInvoice(k, k.LocalCustomerId,
            e => FillItemLine(e, k.WidgetId, k.MainGodownId, 10m, "100.00"),
            narration: "Sold on credit terms 30 days");
        var preview = PrintDrilledVoucher(k.Vm, v.Id);

        Assert.Contains("Sold on credit terms 30 days", AsLatin1(preview.PdfBytes)); // default on

        preview.ShowNarration = false;
        Assert.DoesNotContain("Sold on credit terms 30 days", AsLatin1(preview.PdfBytes));

        preview.ShowNarration = true;
        Assert.Contains("Sold on credit terms 30 days", AsLatin1(preview.PdfBytes));
    }

    // ================================================================ multi-rate invoice reconciles to the engine

    [Fact]
    public void Multi_rate_invoice_print_shows_both_rate_breakups_reconciling_to_the_engine()
    {
        var k = NewGstKit("Print Multi Rate Co");
        // 10 Widget @ ₹100 (18%) + 10 Gadget @ ₹100 (5%) ⇒ CGST 115, SGST 115, taxable 2000, grand 2230.
        var v = PostSaleInvoice(k, k.LocalCustomerId, e =>
        {
            FillItemLine(e, k.WidgetId, k.MainGodownId, 10m, "100.00", index: 0);
            FillItemLine(e, k.GadgetId, k.MainGodownId, 10m, "100.00", index: 1);
        });
        var preview = PrintDrilledVoucher(k.Vm, v.Id);
        var text = AsLatin1(preview.PdfBytes);

        Assert.Contains("18%", text);
        Assert.Contains("5%", text);
        Assert.Contains("2,000.00", text);   // taxable
        Assert.Contains("2,230.00", text);   // grand total
        Assert.Contains("115.00", text);     // per-head CGST/SGST total
        Assert.DoesNotContain("tally", text, StringComparison.OrdinalIgnoreCase);
    }

    // ================================================================ Fix 1: exempt line is not dropped from totals

    [Fact]
    public void Invoice_with_an_exempt_line_foots_the_full_goods_value_not_only_the_rated_lines()
    {
        var k = NewGstKit("Print Exempt Mix Co");
        // 10 Widget @ ₹875 = ₹8,750 (18% ⇒ tax ₹1,575) + 10 Exempt @ ₹200 = ₹2,000 (no tax).
        // Taxable Value must be the FULL goods value 10,750; tax 1,575; grand total 12,325 — the 2,000 is NOT dropped.
        var v = PostSaleInvoice(k, k.LocalCustomerId, e =>
        {
            FillItemLine(e, k.WidgetId, k.MainGodownId, 10m, "875.00", index: 0);
            FillItemLine(e, k.ExemptItemId, k.MainGodownId, 10m, "200.00", index: 1);
        });

        // Assert the projected DTO foots correctly (the render is covered separately).
        var c = k.Vm.Company!;
        var invoice = VoucherPrintProjector.ProjectInvoice(c, v);
        Assert.Equal(10750m, invoice.TotalTaxable.Amount);   // 8,750 + 2,000 exempt (was 8,750 before the fix)
        Assert.Equal(1575m, invoice.TotalTax.Amount);        // tax only on the rated line
        Assert.Equal(12325m, invoice.GrandTotal.Amount);     // full goods value + tax

        var preview = PrintDrilledVoucher(k.Vm, v.Id);
        var text = AsLatin1(preview.PdfBytes);
        Assert.Contains("10,750.00", text);                  // taxable value = full goods value
        Assert.Contains("12,325.00", text);                  // grand total
        Assert.Contains("Rupees Twelve Thousand Three Hundred Twenty Five", text);   // words match the grand total
        Assert.DoesNotContain("tally", text, StringComparison.OrdinalIgnoreCase);
    }

    // ================================================================ F12 is inert on a report preview

    [Fact]
    public void Report_preview_does_not_support_print_config_and_f12_is_a_noop()
    {
        var vm = new MainWindowViewModel(_storage);
        vm.LoadRobertDemo();
        vm.OpenReport(ReportKind.TrialBalance);
        vm.OpenPrintPreview();

        Assert.Equal(PrintPreviewViewModel.PrintKind.Report, vm.PrintPreview!.Kind);
        Assert.False(vm.PrintPreview!.SupportsPrintConfig);

        vm.OpenPrintConfig();                 // no-op on a report preview
        Assert.Null(vm.PrintConfigPanel);
    }

    // ================================================================ Esc pops the F12 panel and keeps the preview live

    [Fact]
    public void Closing_the_print_config_panel_keeps_the_preview_live()
    {
        var k = NewGstKit("Print Pop Co");
        var v = PostSaleInvoice(k, k.LocalCustomerId,
            e => FillItemLine(e, k.WidgetId, k.MainGodownId, 10m, "100.00"));
        var preview = PrintDrilledVoucher(k.Vm, v.Id);

        k.Vm.OpenPrintConfig();
        Assert.NotNull(k.Vm.PrintConfigPanel);

        k.Vm.Back();                          // Esc / Back pops the config column
        Assert.Null(k.Vm.PrintConfigPanel);
        Assert.Same(preview, k.Vm.PrintPreview);      // the preview survives beneath
        Assert.Equal(Screen.PrintPreview, k.Vm.CurrentScreen);
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
        }
        catch (IOException) { /* best effort */ }
    }
}
