using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Io;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Desktop.Views;
using DomainLedger = Apex.Ledger.Domain.Ledger;
using Path = System.IO.Path;
using Xunit;

namespace Apex.Desktop.Tests;

/// <summary>
/// 🔴 <b>Wave 11, track hb — census 6.12 residuals T2-164, T2-165, T2-168 (IV-320..IV-322).</b>
///
/// <para><b>Sources, opened by content (R7, ruling 14).</b> help.tallysolutions.com/increase-in-sale-value/ (updated
/// 4 Nov 2026): a Debit Note for an increase in sale value — "Select the party to whom the goods were originally
/// sold." and "Select the Sales ledger." help.tallysolutions.com/tally-prime/gst-purchase/record-a-debit-note-under-gst/
/// (updated 23 Jan 2026): purchase returns are recorded "using a Debit Note", entering "the credit note number provided
/// by the supplier". CGST Act §34(1)/(3) at taxinformation.cbic.gov.in: both notes are issued by "the registered
/// person, who has supplied".</para>
///
/// <para>Every posted figure is read off a book RELOADED through a fresh <see cref="CompanyStorage"/>; the returns are
/// read as emitted JSON bytes. Odd-paisa figures, worked by hand: 3 × ₹41.67 = ₹125.01, 18% = 22.5018 ⇒ ₹22.50 ⇒
/// CGST 11.25 + SGST 11.25, party ₹147.51. The sale: 6 × ₹41.67 = ₹250.02, 18% = 45.0036 ⇒ ₹45.00 ⇒ 22.50 / 22.50.</para>
/// </summary>
public sealed class Section34NoteDirectionW11Tests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ApexS34Dir_" + Guid.NewGuid().ToString("N"));

    private const string GstinHome = "27AAPFU0939F1ZV";
    private const string GstinParty = "27AAACR5055K1Z5";
    private const string GstinSupplier = "27AABCU9603R1ZM";

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

    private static void Pump(MainWindow w)
    {
        w.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        w.UpdateLayout();
    }

    private sealed record Kit(MainWindowViewModel Vm, CompanyStorage Storage, Guid WidgetId, Guid RiceId, Guid GodownId,
        Guid SalesId, Guid SalesReturnsId, Guid PurchasesId, Guid PurchaseReturnsId, Guid CustomerId, Guid SupplierId);

    private static DomainLedger AddLedger(Company c, string name, string group, string? gstin = null)
    {
        var l = new DomainLedger(Guid.NewGuid(), name, c.FindGroupByName(group)!.Id, Money.Zero, openingIsDebit: false);
        if (gstin is not null)
            l.PartyGst = new PartyGstDetails { RegistrationType = GstRegistrationType.Regular, Gstin = gstin, StateCode = "27" };
        c.AddLedger(l);
        return l;
    }

    private Kit Seed(string name, bool secondSalesLedger = false)
    {
        var storage = new CompanyStorage(_dir);
        var vm = new MainWindowViewModel(storage);
        vm.NewCompanyName = name;
        vm.CreateCompany();
        var c = vm.Company!;
        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = "27", Gstin = GstinHome, RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = c.FinancialYearStart, Periodicity = GstReturnPeriodicity.Monthly,
        });
        var masters = new InventoryService(c);
        var nos = masters.CreateSimpleUnit("Nos", "Numbers");
        var grp = masters.CreateStockGroup("Goods");
        var widget = masters.CreateStockItem("Widget", grp.Id, nos.Id);
        widget.Gst = new StockItemGstDetails { HsnSac = "847130", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800 };
        masters.AddOpeningBalance(widget.Id, c.MainLocation!.Id, 100m, Money.FromRupees(20m));
        var rice = masters.CreateStockItem("Rice", grp.Id, nos.Id);
        rice.Gst = new StockItemGstDetails { HsnSac = "100630", Taxability = GstTaxability.Exempt };
        masters.AddOpeningBalance(rice.Id, c.MainLocation!.Id, 100m, Money.FromRupees(20m));
        var purchases = AddLedger(c, "Purchases", "Purchase Accounts");
        var pr = AddLedger(c, "Purchase Returns", "Purchase Accounts");
        var sales = AddLedger(c, "Sales", "Sales Accounts");
        var sr = AddLedger(c, "Sales Returns", "Sales Accounts");
        // Sorts FIRST in the Sales family, so a family refresh that falls back to the first ledger picks it.
        if (secondSalesLedger) AddLedger(c, "Alpha Sales", "Sales Accounts");
        var customer = AddLedger(c, "Beta Buyers", "Sundry Debtors", GstinParty);
        var supplier = AddLedger(c, "Acme Supplies", "Sundry Creditors", GstinSupplier);
        storage.Save(c);
        return new Kit(vm, storage, widget.Id, rice.Id, c.MainLocation!.Id, sales.Id, sr.Id, purchases.Id, pr.Id,
            customer.Id, supplier.Id);
    }

    private Company Reload(string name)
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        var fresh = new CompanyStorage(_dir);
        return fresh.Load(fresh.ListCompanies().Single(e => e.Name == name));
    }

    /// <summary>Posts a Sales or Purchase item invoice (Ctrl+H, Ctrl+A through the window) and returns it.</summary>
    private static Voucher PostInvoice(MainWindow w, Kit k, VoucherBaseType nature, Guid partyId, Guid valueLedgerId,
        Guid itemId, string qty)
    {
        var c = k.Vm.Company!;
        var type = c.VoucherTypes.First(t => t.BaseType == nature && t.IsActive);
        k.Vm.OpenVoucher(type);
        Pump(w);
        var e = k.Vm.VoucherEntry!;
        w.KeyPressQwerty(PhysicalKey.H, RawInputModifiers.Control);
        Pump(w);
        Assert.True(e.IsItemInvoice);
        e.SelectedParty = e.Parties.Single(p => p.Ledger?.Id == partyId);
        e.SelectedStockLedger = e.StockLedgers.Single(l => l.Id == valueLedgerId);
        var line = e.InventoryLines[0];
        line.SelectedItem = e.StockItems.Single(i => i.Id == itemId);
        line.SelectedGodown = e.Godowns.Single(g => g.Id == k.GodownId);
        line.QuantityText = qty;
        line.RateText = "41.67";
        Pump(w);
        var before = c.Vouchers.Count;
        w.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
        Pump(w);
        Assert.True(c.Vouchers.Count == before + 1, "invoice not accepted: " + e.Message);
        var posted = c.Vouchers.Last(v => v.TypeId == type.Id);
        if (k.Vm.VoucherEntry is not null) { k.Vm.Back(); Pump(w); }
        return posted;
    }

    /// <summary>Opens a note (Alt+F5 Debit, Alt+F6 Credit) in item-invoice mode with §34 ticked, linked to
    /// <paramref name="original"/>. Returns the entry screen, line keyed, NOT yet accepted.</summary>
    private static VoucherEntryViewModel OpenNote(MainWindow w, Kit k, VoucherBaseType nature, Guid partyId,
        Guid itemId, Voucher? original)
    {
        w.KeyPressQwerty(nature == VoucherBaseType.DebitNote ? PhysicalKey.F5 : PhysicalKey.F6, RawInputModifiers.Alt);
        Pump(w);
        var e = k.Vm.VoucherEntry!;
        Assert.Equal(nature, e.Type.BaseType);
        w.KeyPressQwerty(PhysicalKey.H, RawInputModifiers.Control);
        Pump(w);
        Assert.True(e.IsItemInvoice);
        e.SelectedParty = e.Parties.Single(p => p.Ledger?.Id == partyId);
        var line = e.InventoryLines[0];
        line.SelectedItem = e.StockItems.Single(i => i.Id == itemId);
        line.SelectedGodown = e.Godowns.Single(g => g.Id == k.GodownId);
        line.QuantityText = "3";
        line.RateText = "41.67";
        e.IsSection34Note = true;
        if (original is not null)
            e.SelectedCdnOriginalInvoice = e.CdnOriginalInvoices.Single(o => o.Invoice?.Id == original.Id);
        e.SelectedCdnReasonCode = nature == VoucherBaseType.DebitNote ? "04 Correction in invoice" : "01 Sales return";
        Pump(w);
        return e;
    }

    private static Voucher AcceptNote(MainWindow w, Kit k, VoucherEntryViewModel e)
    {
        var c = k.Vm.Company!;
        var before = c.Vouchers.Count;
        w.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
        Pump(w);
        Assert.True(c.Vouchers.Count == before + 1, "note not accepted: " + e.Message);
        return c.Vouchers.Last(v => v.TypeId == e.Type.Id);
    }

    private static (DateOnly From, DateOnly To) Fy(Company c) =>
        (c.FinancialYearStart, c.FinancialYearStart.AddYears(1).AddDays(-1));

    private static JsonElement[] Cdnr(Company c)
    {
        var (from, to) = Fy(c);
        using var doc = JsonDocument.Parse(GstReturnJson.Gstr1(c, from, to));
        return doc.RootElement.GetProperty("cdnr").EnumerateArray().Select(e => e.Clone()).ToArray();
    }

    /// <summary>Re-points a posted note's link at another original, keeping its id: the shape of a note the screen
    /// posted BEFORE wave 11, when a Credit Note could still be linked to a Purchase.</summary>
    private static void RepointLink(Company c, Guid noteId, Voucher original)
    {
        var link = c.CreditDebitNoteLinks.Single(l => l.CdnVoucherId == noteId);
        c.RemoveCreditDebitNoteLink(link);
        c.AddCreditDebitNoteLink(new GstCreditDebitNoteLink(link.Id, noteId, link.CdnType, original.Id,
            c.FormatVoucherNumber(original), original.Date, link.ReasonCode, link.Is9BTarget));
    }

    // ================================================================================================= T2-164

    /// <summary>
    /// 🔴 <b>T2-164 — a CREDIT note is not offered a PURCHASE as its original; a DEBIT note still is.</b> The realised
    /// ComboBox the operator opens is read, not only the view-model list.
    /// <para><b>Mutation H1</b> (the <c>IsOfferableSection34Original</c> filter reverted to "Sales or Purchase")
    /// reddens this at the Credit Note's purchase assertion.</para>
    /// </summary>
    [AvaloniaFact]
    public void A_credit_note_is_offered_only_sales_originals_and_a_debit_note_both()
    {
        var k = Seed("S34 Picker Co");
        var w = new MainWindow { DataContext = k.Vm, Width = 1440, Height = 900 };
        try
        {
            w.Show();
            Pump(w);
            var sale = PostInvoice(w, k, VoucherBaseType.Sales, k.CustomerId, k.SalesId, k.WidgetId, "6");
            var purchase = PostInvoice(w, k, VoucherBaseType.Purchase, k.SupplierId, k.PurchasesId, k.WidgetId, "6");

            var cn = OpenNote(w, k, VoucherBaseType.CreditNote, k.CustomerId, k.WidgetId, original: null);
            var realised = w.GetVisualDescendants().OfType<ComboBox>()
                .Single(cb => ReferenceEquals(cb.ItemsSource, cn.CdnOriginalInvoices));
            var offered = realised.Items.OfType<CdnOriginalInvoiceOption>().Select(o => o.Invoice?.Id).ToList();
            Assert.Contains(sale.Id, offered);
            Assert.DoesNotContain(purchase.Id, offered);
            k.Vm.Back();
            Pump(w);

            var dn = OpenNote(w, k, VoucherBaseType.DebitNote, k.SupplierId, k.WidgetId, original: null);
            var dnRealised = w.GetVisualDescendants().OfType<ComboBox>()
                .Single(cb => ReferenceEquals(cb.ItemsSource, dn.CdnOriginalInvoices));
            var dnOffered = dnRealised.Items.OfType<CdnOriginalInvoiceOption>().Select(o => o.Invoice?.Id).ToList();
            Assert.Contains(sale.Id, dnOffered);
            Assert.Contains(purchase.Id, dnOffered);
        }
        finally { w.Close(); }
    }

    /// <summary>
    /// 🔴 <b>T2-164 — the supplier's credit note on our purchase, keyed the vendor's way (Debit Note), is an INWARD
    /// document on the reloaded book:</b> Input CGST/SGST credited 11.25 / 11.25, no Output leg, Purchase Returns
    /// credited ₹125.01, no GSTR-1 cdnr row, 3.1(a) carries the sale alone, and 4(A)(5) is net of the reversal.
    /// </summary>
    [AvaloniaFact]
    public void The_suppliers_credit_note_keyed_as_a_debit_note_is_inward_on_the_reloaded_book()
    {
        const string name = "S34 Supplier CN Co";
        var k = Seed(name);
        var w = new MainWindow { DataContext = k.Vm, Width = 1440, Height = 900 };
        Guid noteId;
        try
        {
            w.Show();
            Pump(w);
            PostInvoice(w, k, VoucherBaseType.Sales, k.CustomerId, k.SalesId, k.WidgetId, "6");
            var purchase = PostInvoice(w, k, VoucherBaseType.Purchase, k.SupplierId, k.PurchasesId, k.WidgetId, "6");
            var e = OpenNote(w, k, VoucherBaseType.DebitNote, k.SupplierId, k.WidgetId, purchase);
            Assert.Equal("Purchase Returns", e.StockLedgerCaption);
            e.SelectedStockLedger = e.StockLedgers.Single(l => l.Id == k.PurchaseReturnsId);
            noteId = AcceptNote(w, k, e).Id;
        }
        finally { w.Close(); }

        var c = Reload(name);
        var note = c.FindVoucher(noteId)!;
        var inCgst = c.FindLedgerByName(GstService.TaxLedgerName(GstTaxHead.Central, GstTaxDirection.Input))!.Id;
        var outCgst = c.FindLedgerByName(GstService.TaxLedgerName(GstTaxHead.Central, GstTaxDirection.Output))!.Id;
        var cg = Assert.Single(note.Lines, l => l.LedgerId == inCgst);
        Assert.Equal((DrCr.Credit, 11.25m), (cg.Side, cg.Amount.Amount));
        Assert.DoesNotContain(note.Lines, l => l.LedgerId == outCgst);
        var value = Assert.Single(note.Lines, l => l.LedgerId == k.PurchaseReturnsId);
        Assert.Equal((DrCr.Credit, 125.01m), (value.Side, value.Amount.Amount));

        Assert.Empty(Cdnr(c));
        var (from, to) = Fy(c);
        using var g3b = JsonDocument.Parse(GstReturnJson.Gstr3b(c, from, to));
        Assert.Equal(25_002L, g3b.RootElement.GetProperty("tbl3_1a_txval_paisa").GetInt64());
        Assert.Equal(2_250L, g3b.RootElement.GetProperty("tbl3_1a_camt_paisa").GetInt64());
        Assert.Equal(1_125L, g3b.RootElement.GetProperty("tbl4a5_camt_paisa").GetInt64());   // 22.50 − 11.25
    }

    /// <summary>
    /// 🔴 <b>T2-164 — a credit note posted BEFORE wave 11 against a purchase is refused on alteration BY NAME, and is
    /// not silently re-linked.</b> The book is built the way the old screen left it (a credit note whose link names a
    /// purchase), saved, and re-opened for alteration. The panel shows the purchase it is linked to, and accepting is
    /// refused with <see cref="VoucherEntryViewModel.CreditNoteOnPurchaseRefusal"/>; the reloaded link still names the
    /// purchase.
    /// <para><b>Mutation H2</b> (the ValidateSection34 refusal removed) reddens this at the refusal assertion.
    /// <b>Mutation H3</b> (the rehydrate's legacy-original insertion removed) reddens it at the panel assertion,
    /// because the note then falls through to the consolidated branch.</para>
    /// </summary>
    [AvaloniaFact]
    public void A_pre_wave_11_credit_note_on_a_purchase_is_refused_on_alteration_and_keeps_its_link()
    {
        const string name = "S34 Legacy CN Co";
        var k = Seed(name);
        var w = new MainWindow { DataContext = k.Vm, Width = 1440, Height = 900 };
        Guid noteId, purchaseId;
        try
        {
            w.Show();
            Pump(w);
            var sale = PostInvoice(w, k, VoucherBaseType.Sales, k.CustomerId, k.SalesId, k.WidgetId, "6");
            var purchase = PostInvoice(w, k, VoucherBaseType.Purchase, k.SupplierId, k.PurchasesId, k.WidgetId, "6");
            purchaseId = purchase.Id;
            var e = OpenNote(w, k, VoucherBaseType.CreditNote, k.SupplierId, k.WidgetId, sale);
            e.SelectedStockLedger = e.StockLedgers.Single(l => l.Id == k.SalesReturnsId);
            noteId = AcceptNote(w, k, e).Id;
        }
        finally { w.Close(); }

        var legacy = Reload(name);
        RepointLink(legacy, noteId, legacy.FindVoucher(purchaseId)!);
        var storage = new CompanyStorage(_dir);
        storage.Save(legacy);

        var book = Reload(name);

        // R3 (fix pass) — THE TAXED TWIN, measured. On this legacy book the credit note on a purchase still posts
        // Output CGST Dr 11.25, and its GSTR-1 cdnr row says exactly that: one Credit row naming the supplier with
        // camt 11.25, and 3.1(a) CGST is the sale's 22.50 less the same 11.25. The return agrees with the books for
        // this note (IV-253's Output-leg clause); the row's original is the purchase, so the filed row names the
        // supplier's invoice as if it were ours. What it does NOT agree with is the zero-tax twin, which is absent
        // from cdnr (Zero_tax_notes_…): T2-168's inconsistency survives on a legacy book and is closed by
        // prevention only (IV-322). A "fix" that dropped this row while the Output legs stay posted would make the
        // return disagree with the books, which is what this assertion refuses.
        var outCgstId = book.FindLedgerByName(GstService.TaxLedgerName(GstTaxHead.Central, GstTaxDirection.Output))!.Id;
        var postedOut = Assert.Single(book.FindVoucher(noteId)!.Lines, l => l.LedgerId == outCgstId);
        Assert.Equal((DrCr.Debit, 11.25m), (postedOut.Side, postedOut.Amount.Amount));
        var twin = Assert.Single(Cdnr(book));
        Assert.Equal("Credit", twin.GetProperty("ntty").GetString());
        Assert.Equal(book.FormatVoucherNumber(book.FindVoucher(purchaseId)!), twin.GetProperty("orig_inum").GetString());
        Assert.Equal(-1_125L, twin.GetProperty("camt_paisa").GetInt64());   // the emitter signs a Credit row negative
        var (from3b, to3b) = Fy(book);
        using (var g3b = JsonDocument.Parse(GstReturnJson.Gstr3b(book, from3b, to3b)))
            Assert.Equal(1_125L, g3b.RootElement.GetProperty("tbl3_1a_camt_paisa").GetInt64());   // 22.50 − 11.25

        var open = VoucherEntryViewModel.ForAlter(book, noteId, new CompanyStorage(_dir), () => { }, () => { });
        Assert.Null(open.Refusal);
        var entry = open.Entry!;
        Assert.Equal(purchaseId, entry.SelectedCdnOriginalInvoice?.Invoice?.Id);
        Assert.False(entry.AcceptAlteration());
        Assert.Equal(VoucherEntryViewModel.CreditNoteOnPurchaseRefusal, entry.Message);

        var after = Reload(name);
        Assert.Equal(purchaseId, after.CreditDebitNoteLinks.Single(l => l.CdnVoucherId == noteId).OriginalInvoiceVoucherId);
    }

    // ================================================================================================= T2-165

    /// <summary>
    /// 🔴 <b>T2-165 — an outward §34 debit note (linked to our sale) takes a SALES ledger, and GSTR-9C 5R is 0.</b>
    /// The picker offers the Sales family and the caption reads "Sales" (the vendor's "Select the Sales ledger.").
    /// On the reloaded book: Sales Cr ₹125.01, no purchase-family leg, Output CGST/SGST Cr 11.25 / 11.25, customer Dr
    /// ₹147.51. GSTR-3B 3.1(a) = 250.02 + 125.01 = ₹375.03 and CGST 22.50 + 11.25 = ₹33.75; GSTR-1 lists one Debit note
    /// of ₹125.01; GSTR-9C 5R = 0.
    /// <para><b>Mutation H4</b> (<c>ValueLegIsPurchaseFamily</c> back to <c>IsPurchaseSideInvoice</c>) reddens this at
    /// the picker. <b>Mutation H5</b> (the validator's Debit-Note sales-family arm removed) reddens it at Ctrl+A,
    /// which the engine then refuses on the item pairing.</para>
    /// </summary>
    [AvaloniaFact]
    public void An_outward_debit_note_linked_to_a_sale_posts_its_value_to_sales_and_9c_5r_is_zero()
    {
        const string name = "S34 Outward Value Co";
        var k = Seed(name);
        var w = new MainWindow { DataContext = k.Vm, Width = 1440, Height = 900 };
        Guid noteId;
        try
        {
            w.Show();
            Pump(w);
            var sale = PostInvoice(w, k, VoucherBaseType.Sales, k.CustomerId, k.SalesId, k.WidgetId, "6");
            var e = OpenNote(w, k, VoucherBaseType.DebitNote, k.CustomerId, k.WidgetId, sale);
            Assert.Equal("Sales", e.StockLedgerCaption);
            Assert.Contains(e.StockLedgers, l => l.Id == k.SalesId);
            Assert.DoesNotContain(e.StockLedgers, l => l.Id == k.PurchaseReturnsId || l.Id == k.PurchasesId);
            e.SelectedStockLedger = e.StockLedgers.Single(l => l.Id == k.SalesId);
            noteId = AcceptNote(w, k, e).Id;
        }
        finally { w.Close(); }

        var c = Reload(name);
        var note = c.FindVoucher(noteId)!;
        var value = Assert.Single(note.Lines, l => l.LedgerId == k.SalesId);
        Assert.Equal((DrCr.Credit, 125.01m), (value.Side, value.Amount.Amount));
        Assert.DoesNotContain(note.Lines, l => l.LedgerId == k.PurchaseReturnsId || l.LedgerId == k.PurchasesId);
        var outCgst = c.FindLedgerByName(GstService.TaxLedgerName(GstTaxHead.Central, GstTaxDirection.Output))!.Id;
        var cg = Assert.Single(note.Lines, l => l.LedgerId == outCgst);
        Assert.Equal((DrCr.Credit, 11.25m), (cg.Side, cg.Amount.Amount));
        var party = Assert.Single(note.Lines, l => l.LedgerId == k.CustomerId);
        Assert.Equal((DrCr.Debit, 147.51m), (party.Side, party.Amount.Amount));

        var (from, to) = Fy(c);
        using (var g9c = JsonDocument.Parse(GstReturnJson.Gstr9c(c, from, to)))
            Assert.Equal(0L, g9c.RootElement.GetProperty("tbl5r_unreconciled_turnover_paisa").GetInt64());
        using (var g3b = JsonDocument.Parse(GstReturnJson.Gstr3b(c, from, to)))
        {
            Assert.Equal(37_503L, g3b.RootElement.GetProperty("tbl3_1a_txval_paisa").GetInt64());
            Assert.Equal(3_375L, g3b.RootElement.GetProperty("tbl3_1a_camt_paisa").GetInt64());
        }
        var row = Assert.Single(Cdnr(c));
        Assert.Equal("Debit", row.GetProperty("ntty").GetString());
        Assert.Equal(12_501L, row.GetProperty("txval_paisa").GetInt64());
        Assert.Equal(1_125L, row.GetProperty("camt_paisa").GetInt64());
    }

    /// <summary>
    /// <b>T2-165, the consolidated limb, reached through the PARTY.</b> §34 is ticked and "Consolidated…" chosen while
    /// the party is still the default; only choosing the debtor afterwards makes the note outward, so only the party
    /// handler's family refresh can move the picker. Reloaded: Sales Cr ₹125.01, 9C 5R 0.
    /// <para><b>Mutation H6</b> (the <c>RefreshValueLedgerFamily</c> call in <c>OnSelectedPartyChanged</c> removed)
    /// reddens this at the picker assertion.</para>
    /// </summary>
    [AvaloniaFact]
    public void A_consolidated_debit_note_moves_to_the_sales_family_when_the_debtor_is_chosen()
    {
        const string name = "S34 Consolidated Value Co";
        var k = Seed(name);
        var w = new MainWindow { DataContext = k.Vm, Width = 1440, Height = 900 };
        Guid noteId;
        try
        {
            w.Show();
            Pump(w);
            w.KeyPressQwerty(PhysicalKey.F5, RawInputModifiers.Alt);
            Pump(w);
            var e = k.Vm.VoucherEntry!;
            w.KeyPressQwerty(PhysicalKey.H, RawInputModifiers.Control);
            Pump(w);
            Assert.True(e.IsItemInvoice);
            e.IsSection34Note = true;
            e.SelectedCdnOriginalInvoice = e.CdnOriginalInvoices.Single(o => o.IsConsolidated);
            e.CdnOriginalInvoiceNumber = "INV-77";
            e.SelectedCdnReasonCode = "04 Correction in invoice";
            Assert.Contains(e.StockLedgers, l => l.Id == k.PurchaseReturnsId);   // no party yet: a purchase return

            e.SelectedParty = e.Parties.Single(p => p.Ledger?.Id == k.CustomerId);
            Assert.Contains(e.StockLedgers, l => l.Id == k.SalesId);
            Assert.DoesNotContain(e.StockLedgers, l => l.Id == k.PurchaseReturnsId);
            e.SelectedStockLedger = e.StockLedgers.Single(l => l.Id == k.SalesId);
            var line = e.InventoryLines[0];
            line.SelectedItem = e.StockItems.Single(i => i.Id == k.WidgetId);
            line.SelectedGodown = e.Godowns.Single(g => g.Id == k.GodownId);
            line.QuantityText = "3";
            line.RateText = "41.67";
            Pump(w);
            noteId = AcceptNote(w, k, e).Id;
        }
        finally { w.Close(); }

        var c = Reload(name);
        var value = Assert.Single(c.FindVoucher(noteId)!.Lines, l => l.LedgerId == k.SalesId);
        Assert.Equal((DrCr.Credit, 125.01m), (value.Side, value.Amount.Amount));
        var (from, to) = Fy(c);
        using var g9c = JsonDocument.Parse(GstReturnJson.Gstr9c(c, from, to));
        Assert.Equal(0L, g9c.RootElement.GetProperty("tbl5r_unreconciled_turnover_paisa").GetInt64());
    }

    /// <summary>Re-points one posted line's ledger in the saved file: the shape an outward debit note had when the
    /// screen posted it before wave 11 (value on Purchase Returns). Returns the number of rows changed.</summary>
    private static int RewriteLineLedger(string dbPath, Guid voucherId, Guid fromLedgerId, Guid toLedgerId)
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        using var cn = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + dbPath);
        cn.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "UPDATE entry_lines SET ledger_id = $to WHERE voucher_id = $v AND ledger_id = $from";
        cmd.Parameters.AddWithValue("$to", toLedgerId.ToString("D"));
        cmd.Parameters.AddWithValue("$v", voucherId.ToString("D"));
        cmd.Parameters.AddWithValue("$from", fromLedgerId.ToString("D"));
        var n = cmd.ExecuteNonQuery();
        cn.Close();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        return n;
    }

    /// <summary>
    /// 🔴 <b>R2 (fix pass, IV-324) — an outward debit note POSTED BEFORE WAVE 11, value on Purchase Returns, re-opens
    /// for alteration with its own figures, and the operator can then move it to Sales.</b> The legacy book is built
    /// by re-pointing the saved note's value line from Sales to Purchase Returns in the file (the exact rows the old
    /// screen wrote: Purchase Returns Cr 125.01, Output CGST/SGST Cr 11.25/11.25, customer Dr 147.51), then reloaded
    /// through a fresh store handle. Re-opening is not refused; the picker holds the Sales family PLUS the carried
    /// Purchase Returns, which is selected; an unchanged Accept leaves the reloaded book byte-for-byte on the same
    /// ledgers (nothing moved silently); a second alteration choosing Sales posts Sales Cr 125.01 and 9C 5R reads 0.
    /// <para><b>Mutation M1</b> (the legacy-leg acceptance in <c>RehydrateItemInvoiceFrom</c> removed) reddens this at
    /// the refusal assertion. <b>Mutation M2</b> (<c>IsStockLegLedger</c> ignoring the carried id) reddens it too:
    /// the rebuilt picker no longer lists Purchase Returns, so the rehydrate's selection throws.</para>
    /// </summary>
    [AvaloniaFact]
    public void A_pre_wave_11_outward_debit_note_valued_on_purchase_returns_reopens_and_can_move_to_sales()
    {
        const string name = "S34 Legacy Outward DN Co";
        var k = Seed(name);
        var w = new MainWindow { DataContext = k.Vm, Width = 1440, Height = 900 };
        Guid noteId, saleId;
        try
        {
            w.Show();
            Pump(w);
            var sale = PostInvoice(w, k, VoucherBaseType.Sales, k.CustomerId, k.SalesId, k.WidgetId, "6");
            saleId = sale.Id;
            var e = OpenNote(w, k, VoucherBaseType.DebitNote, k.CustomerId, k.WidgetId, sale);
            e.SelectedStockLedger = e.StockLedgers.Single(l => l.Id == k.SalesId);
            noteId = AcceptNote(w, k, e).Id;
        }
        finally { w.Close(); }

        var dbPath = new CompanyStorage(_dir).ListCompanies().Single(x => x.Name == name).DatabasePath;
        Assert.Equal(1, RewriteLineLedger(dbPath, noteId, k.SalesId, k.PurchaseReturnsId));

        var legacy = Reload(name);
        var outCgst = legacy.FindLedgerByName(GstService.TaxLedgerName(GstTaxHead.Central, GstTaxDirection.Output))!.Id;
        var legacyNote = legacy.FindVoucher(noteId)!;
        var pr0 = Assert.Single(legacyNote.Lines, l => l.LedgerId == k.PurchaseReturnsId);
        Assert.Equal((DrCr.Credit, 125.01m), (pr0.Side, pr0.Amount.Amount));
        Assert.DoesNotContain(legacyNote.Lines, l => l.LedgerId == k.SalesId);   // premise: the legacy shape

        var open = VoucherEntryViewModel.ForAlter(legacy, noteId, new CompanyStorage(_dir), () => { }, () => { });
        Assert.Null(open.Refusal);
        var entry = open.Entry!;
        Assert.Equal(saleId, entry.SelectedCdnOriginalInvoice?.Invoice?.Id);
        Assert.Equal("Sales", entry.StockLedgerCaption);
        Assert.Equal(k.PurchaseReturnsId, entry.SelectedStockLedger?.Id);
        Assert.Contains(entry.StockLedgers, l => l.Id == k.SalesId);
        Assert.DoesNotContain(entry.StockLedgers, l => l.Id == k.PurchasesId);   // only the carried ledger, not its family
        Assert.True(entry.AcceptAlteration(), "unchanged alteration refused: " + entry.Message);

        var kept = Reload(name);
        var keptNote = kept.FindVoucher(noteId)!;
        var pr = Assert.Single(keptNote.Lines, l => l.LedgerId == k.PurchaseReturnsId);
        Assert.Equal((DrCr.Credit, 125.01m), (pr.Side, pr.Amount.Amount));
        Assert.DoesNotContain(keptNote.Lines, l => l.LedgerId == k.SalesId);
        var cg = Assert.Single(keptNote.Lines, l => l.LedgerId == outCgst);
        Assert.Equal((DrCr.Credit, 11.25m), (cg.Side, cg.Amount.Amount));
        var party = Assert.Single(keptNote.Lines, l => l.LedgerId == k.CustomerId);
        Assert.Equal((DrCr.Debit, 147.51m), (party.Side, party.Amount.Amount));
        Assert.Equal(saleId, kept.CreditDebitNoteLinks.Single(l => l.CdnVoucherId == noteId).OriginalInvoiceVoucherId);

        var reopen = VoucherEntryViewModel.ForAlter(kept, noteId, new CompanyStorage(_dir), () => { }, () => { });
        Assert.Null(reopen.Refusal);
        var again = reopen.Entry!;
        again.SelectedStockLedger = again.StockLedgers.Single(l => l.Id == k.SalesId);
        Assert.True(again.AcceptAlteration(), "move to Sales refused: " + again.Message);

        var moved = Reload(name);
        var movedNote = moved.FindVoucher(noteId)!;
        var value = Assert.Single(movedNote.Lines, l => l.LedgerId == k.SalesId);
        Assert.Equal((DrCr.Credit, 125.01m), (value.Side, value.Amount.Amount));
        Assert.DoesNotContain(movedNote.Lines, l => l.LedgerId == k.PurchaseReturnsId);
        var (from, to) = Fy(moved);
        using var g9c = JsonDocument.Parse(GstReturnJson.Gstr9c(moved, from, to));
        Assert.Equal(0L, g9c.RootElement.GetProperty("tbl5r_unreconciled_turnover_paisa").GetInt64());
    }

    /// <summary>
    /// 🔴 <b>R5 (fix pass) — a NEW outward debit note re-opens on the Sales ledger it was posted on, not the first
    /// Sales ledger alphabetically.</b> The book has two Sales-family ledgers, "Alpha Sales" (first) and "Sales"; the
    /// note is keyed on "Sales" in the order that leaves the ORIGINAL picker as the only handler that can move the
    /// family (§34 ticked with no party, then the Sale picked, then the customer), saved, reloaded, re-opened: the
    /// picker shows "Sales" selected. An unchanged Accept keeps Sales Cr 125.01 on the reloaded book.
    /// <para><b>Mutation M3</b> (the original picker's <c>RefreshValueLedgerFamily</c> removed) reddens this at the
    /// first family assertion. <b>Mutation M4</b> (the rehydrate's <c>SelectedStockLedger</c> assignment removed)
    /// reddens it at the re-opened selection, which falls to "Alpha Sales".</para>
    /// </summary>
    [AvaloniaFact]
    public void A_new_outward_debit_note_reopens_on_the_sales_ledger_it_was_posted_on()
    {
        const string name = "S34 Two Sales Co";
        var k = Seed(name, secondSalesLedger: true);
        var alphaId = k.Vm.Company!.FindLedgerByName("Alpha Sales")!.Id;
        var w = new MainWindow { DataContext = k.Vm, Width = 1440, Height = 900 };
        Guid noteId;
        try
        {
            w.Show();
            Pump(w);
            var sale = PostInvoice(w, k, VoucherBaseType.Sales, k.CustomerId, k.SalesId, k.WidgetId, "6");
            w.KeyPressQwerty(PhysicalKey.F5, RawInputModifiers.Alt);
            Pump(w);
            var e = k.Vm.VoucherEntry!;
            w.KeyPressQwerty(PhysicalKey.H, RawInputModifiers.Control);
            Pump(w);
            Assert.True(e.IsItemInvoice);
            e.IsSection34Note = true;                                             // no party, no original: inward
            Assert.Contains(e.StockLedgers, l => l.Id == k.PurchaseReturnsId);
            e.SelectedCdnOriginalInvoice = e.CdnOriginalInvoices.Single(o => o.Invoice?.Id == sale.Id);
            Assert.Contains(e.StockLedgers, l => l.Id == k.SalesId);              // only the original handler moved it
            Assert.DoesNotContain(e.StockLedgers, l => l.Id == k.PurchaseReturnsId);
            e.SelectedParty = e.Parties.Single(p => p.Ledger?.Id == k.CustomerId);
            e.SelectedCdnReasonCode = "04 Correction in invoice";
            e.SelectedStockLedger = e.StockLedgers.Single(l => l.Id == k.SalesId);
            var line = e.InventoryLines[0];
            line.SelectedItem = e.StockItems.Single(i => i.Id == k.WidgetId);
            line.SelectedGodown = e.Godowns.Single(g => g.Id == k.GodownId);
            line.QuantityText = "3";
            line.RateText = "41.67";
            Pump(w);
            noteId = AcceptNote(w, k, e).Id;
        }
        finally { w.Close(); }

        var book = Reload(name);
        var open = VoucherEntryViewModel.ForAlter(book, noteId, new CompanyStorage(_dir), () => { }, () => { });
        Assert.Null(open.Refusal);
        var entry = open.Entry!;
        Assert.Equal("Sales", entry.StockLedgerCaption);
        Assert.Contains(entry.StockLedgers, l => l.Id == alphaId);
        Assert.Equal(k.SalesId, entry.SelectedStockLedger?.Id);
        Assert.True(entry.AcceptAlteration(), "unchanged alteration refused: " + entry.Message);

        var after = Reload(name);
        var note = after.FindVoucher(noteId)!;
        var value = Assert.Single(note.Lines, l => l.LedgerId == k.SalesId);
        Assert.Equal((DrCr.Credit, 125.01m), (value.Side, value.Amount.Amount));
        Assert.DoesNotContain(note.Lines, l => l.LedgerId == alphaId);
    }

    /// <summary>
    /// <b>R5 (fix pass) — the §34 TICK alone moves the family.</b> The customer is chosen while §34 is off (an
    /// ordinary purchase-side Debit Note: Purchase family), then §34 is ticked with no original picked, which makes
    /// the note outward through the party's Sundry Debtors group (IV-278); no other handler fires between the two
    /// assertions. <b>Mutation M5</b> (the tick's <c>RefreshValueLedgerFamily</c> removed) reddens this.
    /// </summary>
    [AvaloniaFact]
    public void Ticking_section_34_on_a_debit_note_to_a_debtor_moves_the_picker_to_sales()
    {
        var k = Seed("S34 Tick Family Co", secondSalesLedger: true);
        var w = new MainWindow { DataContext = k.Vm, Width = 1440, Height = 900 };
        try
        {
            w.Show();
            Pump(w);
            w.KeyPressQwerty(PhysicalKey.F5, RawInputModifiers.Alt);
            Pump(w);
            var e = k.Vm.VoucherEntry!;
            w.KeyPressQwerty(PhysicalKey.H, RawInputModifiers.Control);
            Pump(w);
            Assert.True(e.IsItemInvoice);
            e.SelectedParty = e.Parties.Single(p => p.Ledger?.Id == k.CustomerId);
            Assert.Contains(e.StockLedgers, l => l.Id == k.PurchaseReturnsId);   // §34 off: a purchase-side note
            Assert.DoesNotContain(e.StockLedgers, l => l.Id == k.SalesId);
            e.IsSection34Note = true;
            Pump(w);
            Assert.Equal("Sales", e.StockLedgerCaption);
            Assert.Contains(e.StockLedgers, l => l.Id == k.SalesId);
            Assert.DoesNotContain(e.StockLedgers, l => l.Id == k.PurchaseReturnsId);
        }
        finally { w.Close(); }
    }

    /// <summary>
    /// 🔴 <b>Re-review (w11c) — a DUPLICATE (Alt+2) of a pre-wave-11 outward debit note is a NEW note and takes a
    /// Sales ledger; the legacy Purchase Returns carry is for ALTERATION only.</b> <c>ForDuplicate</c> re-keys the
    /// screen through the same rehydrate as <c>ForAlter</c>, so the carried ledger used to survive
    /// <c>DetachAsDuplicate</c> and stay selected: Ctrl+A then posted a FRESH outward note with its value on Purchase
    /// Returns, the exact T2-165 shape (GSTR-9C 5R = −value) the wave closed for new notes. A duplicate "is a NEW
    /// document and must be stamped by today's masters" (<c>DetachAsDuplicate</c>'s own rule). On the reloaded book
    /// the copy posts Sales Cr 125.01 and no Purchase Returns leg; the legacy source is untouched.
    /// <para><b>Mutation R1</b> (the carry-clear in <c>DetachAsDuplicate</c> removed) reddens this at the picker
    /// assertion.</para>
    /// </summary>
    [AvaloniaFact]
    public void A_duplicate_of_a_pre_wave_11_outward_debit_note_posts_its_value_to_sales()
    {
        const string name = "S34 Legacy Dup DN Co";
        var k = Seed(name);
        var w = new MainWindow { DataContext = k.Vm, Width = 1440, Height = 900 };
        Guid noteId;
        try
        {
            w.Show();
            Pump(w);
            var sale = PostInvoice(w, k, VoucherBaseType.Sales, k.CustomerId, k.SalesId, k.WidgetId, "6");
            var e = OpenNote(w, k, VoucherBaseType.DebitNote, k.CustomerId, k.WidgetId, sale);
            e.SelectedStockLedger = e.StockLedgers.Single(l => l.Id == k.SalesId);
            noteId = AcceptNote(w, k, e).Id;
        }
        finally { w.Close(); }

        var dbPath = new CompanyStorage(_dir).ListCompanies().Single(x => x.Name == name).DatabasePath;
        Assert.Equal(1, RewriteLineLedger(dbPath, noteId, k.SalesId, k.PurchaseReturnsId));

        var legacy = Reload(name);
        var open = VoucherEntryViewModel.ForDuplicate(legacy, noteId, new CompanyStorage(_dir), () => { }, () => { });
        Assert.Null(open.Refusal);
        var dup = open.Entry!;
        Assert.False(dup.IsAltering);
        Assert.Equal("Sales", dup.StockLedgerCaption);
        Assert.DoesNotContain(dup.StockLedgers, l => l.Id == k.PurchaseReturnsId || l.Id == k.PurchasesId);
        Assert.Equal(k.SalesId, dup.SelectedStockLedger?.Id);
        Assert.True(dup.Accept(), "duplicate refused: " + dup.Message);

        var book = Reload(name);
        var noteTypeId = book.FindVoucher(noteId)!.TypeId;
        var notes = book.Vouchers.Where(v => v.TypeId == noteTypeId).ToList();
        Assert.Equal(2, notes.Count);
        var copy = Assert.Single(notes, v => v.Id != noteId);
        var value = Assert.Single(copy.Lines, l => l.LedgerId == k.SalesId);
        Assert.Equal((DrCr.Credit, 125.01m), (value.Side, value.Amount.Amount));
        Assert.DoesNotContain(copy.Lines, l => l.LedgerId == k.PurchaseReturnsId || l.LedgerId == k.PurchasesId);
        var source = Assert.Single(book.FindVoucher(noteId)!.Lines, l => l.LedgerId == k.PurchaseReturnsId);
        Assert.Equal((DrCr.Credit, 125.01m), (source.Side, source.Amount.Amount));
    }

    // ================================================================================================= T2-168

    /// <summary>
    /// 🔴 <b>T2-168 — the zero-tax notes classify by the ORIGINAL, the same way their taxed twins now do.</b> On one
    /// reloaded book: a zero-tax (exempt Rice) Debit Note against a PURCHASE is not a GSTR-1 cdnr row; a zero-tax Credit
    /// Note against a SALE is (the control, no tax); and a pre-wave-11 zero-tax Credit Note re-linked to a
    /// purchase is not. Its taxed twin, the Output-legged credit note on a purchase, can no longer be keyed (T2-164),
    /// which is what removes the disagreement T2-168 recorded.
    /// <para><b>Mutation H7</b> — the "positive test" (<c>IsInwardLinkedCreditDebitNote</c> keyed on "posted an Input
    /// leg" instead of "posted no Output leg") — reddens this: the zero-tax purchase return posts no Input leg and is
    /// then filed in GSTR-1 as an outward debit note.</para>
    /// </summary>
    [AvaloniaFact]
    public void Zero_tax_notes_are_classified_by_their_original_in_gstr1_cdnr()
    {
        const string name = "S34 Zero Tax Co";
        var k = Seed(name);
        var w = new MainWindow { DataContext = k.Vm, Width = 1440, Height = 900 };
        Guid legacyNoteId, purchaseId;
        try
        {
            w.Show();
            Pump(w);
            var sale = PostInvoice(w, k, VoucherBaseType.Sales, k.CustomerId, k.SalesId, k.RiceId, "6");
            var purchase = PostInvoice(w, k, VoucherBaseType.Purchase, k.SupplierId, k.PurchasesId, k.RiceId, "6");
            purchaseId = purchase.Id;

            var dn = OpenNote(w, k, VoucherBaseType.DebitNote, k.SupplierId, k.RiceId, purchase);
            dn.SelectedStockLedger = dn.StockLedgers.Single(l => l.Id == k.PurchaseReturnsId);
            AcceptNote(w, k, dn);
            k.Vm.Back();
            Pump(w);

            var cn = OpenNote(w, k, VoucherBaseType.CreditNote, k.CustomerId, k.RiceId, sale);
            cn.SelectedStockLedger = cn.StockLedgers.Single(l => l.Id == k.SalesReturnsId);
            AcceptNote(w, k, cn);
            k.Vm.Back();
            Pump(w);

            var cn2 = OpenNote(w, k, VoucherBaseType.CreditNote, k.SupplierId, k.RiceId, sale);
            cn2.SelectedStockLedger = cn2.StockLedgers.Single(l => l.Id == k.SalesReturnsId);
            legacyNoteId = AcceptNote(w, k, cn2).Id;
        }
        finally { w.Close(); }

        var legacy = Reload(name);
        RepointLink(legacy, legacyNoteId, legacy.FindVoucher(purchaseId)!);
        new CompanyStorage(_dir).Save(legacy);

        var c = Reload(name);
        Assert.Equal(3, c.CreditDebitNoteLinks.Count);
        Assert.All(c.Vouchers.Where(v => c.FindVoucherType(v.TypeId)!.BaseType
                is VoucherBaseType.CreditNote or VoucherBaseType.DebitNote),
            v => Assert.DoesNotContain(v.Lines, l => l.Gst is not null));          // premise: all three are zero-tax

        var row = Assert.Single(Cdnr(c));
        Assert.Equal("Credit", row.GetProperty("ntty").GetString());
        // txval_paisa is deliberately NOT asserted: the emitter files 0 for an exempt note, not ₹125.01. That is a
        // separate residual (Gstr1.cs, not this track's file; IV-322) and pinning either figure here would pin it.
        Assert.Equal(0L, row.GetProperty("camt_paisa").GetInt64());
    }
}
