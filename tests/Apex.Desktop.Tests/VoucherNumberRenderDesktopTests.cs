using System;
using System.Collections.Generic;
using System.ComponentModel;
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
using Xunit;

namespace Apex.Desktop.Tests;

/// <summary>
/// Numbering slice S2 (numbering-design-v2 §2, §4) — the display-side render-everywhere locks that need the
/// Desktop projector / view models. Proves the ONE policy makes the printed invoice number, the portal doc-no
/// and the Day Book row all read the SAME affixed string, and that the entry-screen preview equals what Accept
/// posts and refreshes when the date crosses an affix boundary (review corr-F5).
/// </summary>
public sealed class VoucherNumberRenderDesktopTests
{
    private const string GstinMaharashtra = "27AAPFU0939F1ZV";
    private static readonly DateOnly FyStart = new(2025, 4, 1);
    private static readonly DateOnly SaleDate = new(2025, 4, 10);
    private static readonly DateOnly AsOf = new(2026, 3, 31);

    // ================================================================ printed == portal doc-no, under an affix

    [Fact]
    public void EInvoiceDocNo_equalsPrintedNumber_underAffix()
    {
        var (c, gst, ledgers, sales, debtor, widgetId, main) = BuildGstCo();
        var affixSales = AffixSalesType("25-26/", width: 3);
        c.AddVoucherType(affixSales);

        var sale = PostB2BSale(c, gst, ledgers, affixSales.Id, sales, debtor, widgetId, main);

        var docNo = EInvoiceService.DocumentNumberOf(c, sale);
        var printed = VoucherPrintProjector.ProjectInvoice(c, sale).InvoiceNumber;
        Assert.Equal("25-26/001", docNo);
        Assert.Equal("25-26/001", printed);
        Assert.Equal(docNo, printed); // paper == portal — the r1-F1/r2-F1 divergence is closed
    }

    // ================================================================ Day Book == the printed invoice number

    [Fact]
    public void DayBookNumber_equalsPrintedNumber()
    {
        var (c, gst, ledgers, sales, debtor, widgetId, main) = BuildGstCo();
        var affixSales = AffixSalesType("25-26/", width: 3);
        c.AddVoucherType(affixSales);

        var sale = PostB2BSale(c, gst, ledgers, affixSales.Id, sales, debtor, widgetId, main);

        var printed = VoucherPrintProjector.ProjectInvoice(c, sale).InvoiceNumber;
        var dayBookRow = Assert.Single(DayBook.Build(c, FyStart, AsOf), r => r.VoucherId == sale.Id);
        Assert.Equal("25-26/001", dayBookRow.FormattedNumber);
        Assert.Equal(printed, dayBookRow.FormattedNumber); // one voucher renders one string in every view
    }

    // ================================================================ preview == posted, refreshes on date change

    [Fact]
    public void EntryPreview_equalsPosted_afterDateChange()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ApexNumPreview_" + Guid.NewGuid().ToString("N"));
        try
        {
            // Books open 1-Apr-2025; a new-FY prefix takes effect 1-Apr-2026.
            var c = CompanyFactory.CreateSeeded("Preview Co", FyStart, FyStart);
            var storage = new CompanyStorage(tempDir);
            var salesType = new VoucherType(Guid.NewGuid(), "Affix Sales", VoucherBaseType.Sales,
                prefixes: new[] { new VoucherNumberAffix(Guid.NewGuid(), new DateOnly(2026, 4, 1), "26-27/") });
            c.AddVoucherType(salesType);
            var sales = Add(c, "Sales", "Sales Accounts", false);
            var debtor = Add(c, "Debtor", "Sundry Debtors", true);

            var entry = new VoucherEntryViewModel(c, salesType, storage, onSaved: () => { }, onCancelled: () => { });

            // Default date = books-begin (1-Apr-2025): the new-FY prefix is NOT yet in force ⇒ bare "1".
            Assert.Equal("1", entry.FormattedVoucherNumber);

            var seen = new List<string>();
            entry.PropertyChanged += (_, e) => seen.Add(e.PropertyName ?? string.Empty);

            // Cross the affix boundary: the previewed prefix must flip in lock-step (numbering-design-v2 §4).
            entry.Date = new DateOnly(2026, 4, 2);
            Assert.Equal("26-27/1", entry.FormattedVoucherNumber);
            Assert.Contains(nameof(entry.FormattedVoucherNumber), seen); // the refresh fired (mutation: skip ⇒ RED)

            // Accept-equivalent: a real post of this type on the same date renders EXACTLY the previewed string.
            var posted = new LedgerService(c).Post(new Voucher(Guid.NewGuid(), salesType.Id, new DateOnly(2026, 4, 2),
                new List<EntryLine>
                {
                    new(debtor.Id, Money.FromRupees(100m), DrCr.Debit),
                    new(sales.Id, Money.FromRupees(100m), DrCr.Credit),
                }, partyId: debtor.Id));
            Assert.Equal(entry.FormattedVoucherNumber, c.FormatVoucherNumber(posted));
            Assert.Equal("26-27/1", c.FormatVoucherNumber(posted));
        }
        finally
        {
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { /* best-effort */ }
        }
    }

    // ================================================================ FIX-1 / STR-3: print == IRP == e-Way == GSTR-1 ==
    // Day Book == DocumentNumberOf, all the AS-TYPED string, under a LOWERCASE prefix — and case-insensitive dedup survives.

    [Fact]
    public void PrintPortalDayBook_allAsTyped_underLowercasePrefix()
    {
        var (c, gst, ledgers, sales, debtor, widgetId, main) = BuildGstCo();
        c.Gst!.EWayBillEnabled = true;                  // so the movement is e-Way-covered (₹59,000 > ₹50,000)
        c.Gst.EWayApplicableFrom = FyStart;
        var lower = AffixSalesType("inv/", width: 3);   // a LOWERCASE prefix — the case the old .ToUpperInvariant() broke
        c.AddVoucherType(lower);
        var sale = PostB2BSale(c, gst, ledgers, lower.Id, sales, debtor, widgetId, main);
        Assert.Equal(1, sale.Number);

        var docNo = EInvoiceService.DocumentNumberOf(c, sale);
        var printed = VoucherPrintProjector.ProjectInvoice(c, sale).InvoiceNumber;
        var b2bRow = Assert.Single(Gstr1.Build(c, FyStart, AsOf).B2B).InvoiceNumber;
        var dayBook = Assert.Single(DayBook.Build(c, FyStart, AsOf), r => r.VoucherId == sale.Id).FormattedNumber;

        var inv01No = JsonDocument.Parse(Encoding.UTF8.GetString(EInvoiceJson.BuildInv01(c, sale)))
            .RootElement.GetProperty("DocDtls").GetProperty("No").GetString();

        var ewbSvc = new EWayBillService(c);
        var ewbRec = ewbSvc.PrepareRecord(sale, sale.Date);
        ewbSvc.SetPartB(ewbRec, "TRANSIN01", EWayTransportMode.Road, "MH12AB1234", 250);
        var ewbNo = JsonDocument.Parse(Encoding.UTF8.GetString(EWayBillJson.BuildEwb01(c, sale, ewbRec)))
            .RootElement.GetProperty("docNo").GetString();

        // paper == IRP == e-Way == GSTR-1 == Day Book == DocumentNumberOf — every one the AS-TYPED "inv/001".
        Assert.Equal("inv/001", docNo);
        Assert.Equal("inv/001", printed);
        Assert.Equal("inv/001", inv01No);
        Assert.Equal("inv/001", ewbNo);
        Assert.Equal("inv/001", b2bRow);
        Assert.Equal("inv/001", dayBook);

        // The EMITTED/stored DocNo is as-typed, yet the reuse guard still blocks a genuine same-number reuse under an
        // UPPERCASE-prefixed type (the reuse key compares case-insensitively).
        var svc = new EInvoiceService(c);
        var rec = svc.PrepareRecord(sale);
        Assert.Equal("inv/001", rec.DocumentNumberUpper);                       // stored AS-TYPED, not uppercased
        var upper = AffixSalesType("INV/", width: 3);                           // renders "INV/001" — same number, other case
        c.AddVoucherType(upper);
        var sale2 = PostB2BSale(c, gst, ledgers, upper.Id, sales, debtor, widgetId, main);
        Assert.Equal("INV/001", EInvoiceService.DocumentNumberOf(c, sale2));
        Assert.Throws<InvalidOperationException>(() => svc.PrepareRecord(sale2)); // "inv/001" ≈ "INV/001" ⇒ still blocked
    }

    // ================================================================ FIX-2(a): the POS accept toast renders the affix.

    [Fact]
    public void PosToast_rendersAffix()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ApexNumPos_" + Guid.NewGuid().ToString("N"));
        try
        {
            var c = CompanyFactory.CreateSeeded("POS Num Co", FyStart, FyStart);
            new GstService(c).EnableGst(new GstConfig
            {
                HomeStateCode = "27", Gstin = GstinMaharashtra, RegistrationType = GstRegistrationType.Regular,
                ApplicableFrom = FyStart, Periodicity = GstReturnPeriodicity.Monthly,
            });
            var masters = new InventoryService(c);
            var grp = masters.CreateStockGroup("Goods");
            var nos = masters.CreateSimpleUnit("Nos", "Numbers");
            var item = masters.CreateStockItem("Widget", grp.Id, nos.Id, valuationMethod: StockValuationMethod.Fifo);
            item.Gst = new StockItemGstDetails { Taxability = GstTaxability.Taxable, RateBasisPoints = 1800 };
            var main = c.MainLocation!.Id;
            masters.AddOpeningBalance(item.Id, main, 100m, Money.FromRupees(500m));

            Add(c, "Sales (POS)", "Sales Accounts", false);
            Add(c, "Cash", "Cash-in-Hand", true);

            var posType = new VoucherType(Guid.NewGuid(), "Sales POS", VoucherBaseType.Sales, useForPos: true,
                posConfig: new PosConfig { Message1 = "Thanks" },
                prefixes: new[] { new VoucherNumberAffix(Guid.NewGuid(), FyStart, "POS/") });
            c.AddVoucherType(posType);

            var storage = new CompanyStorage(tempDir);
            var vm = new PosBillingViewModel(c, posType, storage, () => { }, () => { });
            var line = vm.Items[0];
            line.SelectedItem = c.StockItems.First(i => i.Id == item.Id);
            line.QuantityText = "1";
            line.RateText = "1000";
            vm.CashRow.CashTenderedText = "1180";
            Assert.True(vm.Accept(), vm.Message);

            var posted = c.Vouchers.Single(v => v.TypeId == posType.Id);
            Assert.Equal(1, posted.Number);
            Assert.Contains("POS/1", vm.Message);          // renders the affix …
            Assert.DoesNotContain("No. 1 ", vm.Message);   // … never the bare int
        }
        finally { try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { /* best-effort */ } }
    }

    // ================================================================ FIX-2(b): the drill-detail title renders the affix
    // and equals the Day Book row it was opened from.

    [Fact]
    public void VoucherDetailTitle_rendersAffix_equalsDayBook()
    {
        var (c, gst, ledgers, sales, debtor, widgetId, main) = BuildGstCo();
        var affix = AffixSalesType("25-26/", width: 3);
        c.AddVoucherType(affix);
        var sale = PostB2BSale(c, gst, ledgers, affix.Id, sales, debtor, widgetId, main);

        var detail = new VoucherDetailViewModel(c, sale);
        var typeName = c.FindVoucherType(sale.TypeId)!.Name;
        Assert.Equal($"{typeName} No. 25-26/001", detail.Title);
        // One voucher, one string: the drill target equals the Day Book row that opened it.
        var dayBook = Assert.Single(DayBook.Build(c, FyStart, AsOf), r => r.VoucherId == sale.Id).FormattedNumber;
        Assert.Equal("25-26/001", dayBook);
        Assert.Contains(dayBook, detail.Title);
    }

    // ================================================================ FIX-2(c): the outstanding-advance picker renders
    // the receipt's affix.

    [Fact]
    public void AdvancePicker_rendersReceiptAffix()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ApexNumAdv_" + Guid.NewGuid().ToString("N"));
        try
        {
            var c = CompanyFactory.CreateSeeded("Adv Num Co", FyStart, FyStart);
            new GstService(c).EnableGst(new GstConfig
            {
                HomeStateCode = "27", Gstin = GstinMaharashtra, RegistrationType = GstRegistrationType.Regular,
                ApplicableFrom = FyStart, Periodicity = GstReturnPeriodicity.Monthly,
            });
            var bank = Add(c, "Bank", "Bank Accounts", true);
            var advLedger = Add(c, "Advance from customer", "Current Liabilities", false);

            // Post the receipt under a PREFIXED Receipt type so its number renders "R/1".
            var receiptType = new VoucherType(Guid.NewGuid(), "Prefixed Receipt", VoucherBaseType.Receipt,
                prefixes: new[] { new VoucherNumberAffix(Guid.NewGuid(), FyStart, "R/") });
            c.AddVoucherType(receiptType);
            var ledgers = new LedgerService(c);
            var receipt = ledgers.Post(new Voucher(Guid.NewGuid(), receiptType.Id, SaleDate,
                new List<EntryLine>
                {
                    new(bank.Id, Money.FromRupees(11800m), DrCr.Debit),
                    new(advLedger.Id, Money.FromRupees(11800m), DrCr.Credit),
                }));
            Assert.Equal("R/1", c.FormatVoucherNumber(receipt));

            // Register the service-advance record against that receipt voucher.
            new AdvanceReceiptService(c).BuildAdvanceReceipt(receipt.Id, isService: true,
                Money.FromRupees(10000m), 1800, interState: true);
            var advance = Assert.Single(c.AdvanceReceipts);

            // A Journal surfaces the outstanding-advance picker; the display renders the receipt's AFFIX, not the bare int.
            var journalType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Journal);
            var entry = new VoucherEntryViewModel(c, journalType, new CompanyStorage(tempDir),
                onSaved: () => { }, onCancelled: () => { });
            var option = entry.OutstandingAdvances.Single(o => o.Receipt?.Id == advance.Id);
            Assert.Contains("R/1", option.Display);
            Assert.DoesNotContain("Receipt 1 ", option.Display);
        }
        finally { try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { /* best-effort */ } }
    }

    // ================================================================ STR-1: whole-surface bite across BOTH assemblies —
    // under an AFFIX every SITE reads the affix (reverting any repoint turns this RED); an empty type reads the bare int.

    [Fact]
    public void WholeSurface_emptyAndAffix_printAndToast()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ApexNumSurface_" + Guid.NewGuid().ToString("N"));
        try
        {
            var (c, gst, ledgers, sales, debtor, widgetId, main) = BuildGstCo();
            var storage = new CompanyStorage(tempDir);

            // --- AFFIX type: every render SITE must read the affix (bite half) ---
            var affix = AffixSalesType("25-26/", width: 3);
            c.AddVoucherType(affix);
            var affixSale = PostB2BSale(c, gst, ledgers, affix.Id, sales, debtor, widgetId, main);
            Assert.NotEqual("25-26/001", affixSale.Number.ToString(System.Globalization.CultureInfo.InvariantCulture));

            Assert.Equal("25-26/001", VoucherPrintProjector.ProjectInvoice(c, affixSale).InvoiceNumber);            // print
            Assert.Equal("25-26/001", Assert.Single(Gstr1.Build(c, FyStart, AsOf).B2B).InvoiceNumber);              // GSTR-1
            Assert.Equal("25-26/001", Assert.Single(DayBook.Build(c, FyStart, AsOf), r => r.VoucherId == affixSale.Id).FormattedNumber); // Day Book
            Assert.Contains(StockItemMovement.Build(c, widgetId, AsOf).Rows, r => r.FormattedNumber == "25-26/001"); // inventory register

            // Accept-toast SITE: a plain Journal accept renders the affix in its toast.
            var jType = new VoucherType(Guid.NewGuid(), "Affix Journal", VoucherBaseType.Journal,
                prefixes: new[] { new VoucherNumberAffix(Guid.NewGuid(), FyStart, "J/") });
            c.AddVoucherType(jType);
            var cashA = Add(c, "Cash A", "Cash-in-Hand", true);
            var cashB = Add(c, "Cash B", "Cash-in-Hand", true);
            var jvm = new VoucherEntryViewModel(c, jType, storage, onSaved: () => { }, onCancelled: () => { });
            jvm.Lines[0].SelectedLedger = cashA; jvm.Lines[0].Side = DrCr.Debit; jvm.Lines[0].AmountText = "100";
            jvm.Lines[1].SelectedLedger = cashB; jvm.Lines[1].Side = DrCr.Credit; jvm.Lines[1].AmountText = "100";
            jvm.Recalculate();
            Assert.True(jvm.Accept(), jvm.Message);
            Assert.Contains("J/1", jvm.Message);

            // --- EMPTY-config type: byte-identity — the same sites read the bare int ---
            var plain = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales && t.Prefixes.Count == 0);
            var plainSale = PostB2BSale(c, gst, ledgers, plain.Id, sales, debtor, widgetId, main);
            var bare = plainSale.Number.ToString(System.Globalization.CultureInfo.InvariantCulture);
            Assert.Equal(bare, VoucherPrintProjector.ProjectInvoice(c, plainSale).InvoiceNumber);
            Assert.Equal(bare, Assert.Single(DayBook.Build(c, FyStart, AsOf), r => r.VoucherId == plainSale.Id).FormattedNumber);
        }
        finally { try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { /* best-effort */ } }
    }

    // ---------------------------------------------------------------- helpers

    private static (Company c, GstService gst, LedgerService ledgers, DomainLedger sales,
        DomainLedger debtor, Guid widgetId, Guid main) BuildGstCo()
    {
        var c = CompanyFactory.CreateSeeded("Numbering GST Co", FyStart);
        var gst = new GstService(c);
        gst.EnableGst(new GstConfig
        {
            HomeStateCode = "27", Gstin = GstinMaharashtra, RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart, Periodicity = GstReturnPeriodicity.Monthly,
            EInvoicingEnabled = true, EInvoiceApplicableFrom = FyStart,
        });

        var inv = new InventoryService(c);
        var grp = inv.CreateStockGroup("Goods");
        var nos = inv.CreateSimpleUnit("Nos", "Numbers", unitQuantityCode: "NOS");
        var main = c.MainLocation!.Id;
        var widget = inv.CreateStockItem("Widget", grp.Id, nos.Id);
        widget.Gst = new StockItemGstDetails { HsnSac = "847130", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800 };
        inv.AddOpeningBalance(widget.Id, main, 1000m, Money.FromRupees(40000m));

        var sales = Add(c, "Sales", "Sales Accounts", false);
        var debtor = Add(c, "Local Debtor", "Sundry Debtors", true);
        debtor.PartyGst = new PartyGstDetails { RegistrationType = GstRegistrationType.Regular, Gstin = GstinMaharashtra, StateCode = "27" };

        return (c, gst, new LedgerService(c), sales, debtor, widget.Id, main);
    }

    private static VoucherType AffixSalesType(string prefix, int width) =>
        new(Guid.NewGuid(), $"Sales {prefix}", VoucherBaseType.Sales,
            numberWidth: width, prefillWithZero: width > 0,
            prefixes: new[] { new VoucherNumberAffix(Guid.NewGuid(), FyStart, prefix) });

    private static Voucher PostB2BSale(Company c, GstService gst, LedgerService ledgers, Guid salesTypeId,
        DomainLedger sales, DomainLedger debtor, Guid widgetId, Guid main)
    {
        var tax = gst.ComputeInvoiceTax(new[] { new GstService.TaxableLine(Money.FromRupees(50000m), 1800) },
            interState: false, GstTaxDirection.Output);
        var lines = new List<EntryLine>
        {
            new(debtor.Id, Money.FromRupees(59000m), DrCr.Debit),
            new(sales.Id, Money.FromRupees(50000m), DrCr.Credit),
        };
        lines.AddRange(tax.TaxLines);
        return ledgers.Post(new Voucher(Guid.NewGuid(), salesTypeId, SaleDate, lines, partyId: debtor.Id,
            inventoryLines: new[] { new VoucherInventoryLine(widgetId, main, 1m, Money.FromRupees(50000m)) }));
    }

    private static DomainLedger Add(Company c, string name, string groupName, bool openingIsDebit)
    {
        var l = new DomainLedger(Guid.NewGuid(), name, c.FindGroupByName(groupName)!.Id, Money.Zero, openingIsDebit);
        c.AddLedger(l);
        return l;
    }
}
