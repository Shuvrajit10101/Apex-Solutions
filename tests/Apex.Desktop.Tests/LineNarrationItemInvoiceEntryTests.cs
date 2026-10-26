using System;
using System.IO;
using System.Linq;
using System.Text;
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
/// 🔴 <b>CENSUS 5.11, WAVE 8 — per-ledger narration ENTRY in ITEM INVOICE mode (IV-235) and its print (IV-237).</b>
/// The vendor names the mode: help.tallysolutions.com/voucher-types-tally/, Set up transaction options, opened by
/// content 2026-10-26: <i>"Enable Provide narration for each ledger in voucher to add narration for individual
/// ledgers based on the voucher mode, such as Accounting/Item Invoice or As Voucher."</i>
///
/// <para>An item invoice's KEYED ledgers are the party, the Sales/Purchases value ledger and each additional-cost
/// row; each now takes a narration that is keyed through the real keys, posted, read back off a book RELOADED
/// through a fresh store handle, re-loaded into its field on alteration, carried through canonical export and
/// import, and printed on the tax invoice.</para>
/// </summary>
public sealed class LineNarrationItemInvoiceEntryTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ApexLnItemW8_" + Guid.NewGuid().ToString("N"));

    private static void Pump(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.Measure(new Size(1280, 800));
        window.Arrange(new Rect(0, 0, 1280, 800));
        Dispatcher.UIThread.RunJobs();
    }

    private static void Key(Window w, PhysicalKey key, RawInputModifiers mods = RawInputModifiers.None)
    {
        w.KeyPressQwerty(key, mods);
        Pump(w);
    }

    private static TextBox? Focused(Window w) =>
        TopLevel.GetTopLevel(w)?.FocusManager?.GetFocusedElement() as TextBox;

    private static TextBox[] Boxes(Window w, string name) =>
        w.GetVisualDescendants().OfType<TextBox>().Where(t => t.Name == name).ToArray();

    private sealed record Kit(MainWindowViewModel Vm, Guid WidgetId, DomainLedger Party, DomainLedger Value,
        DomainLedger? Freight);

    /// <summary>A plain (non-GST) company with a Widget in stock, a debtor, a creditor, the two value ledgers and
    /// an additional-cost ledger (which must exist BEFORE an entry screen is built).</summary>
    private Kit NewBook(string name, bool purchase)
    {
        var vm = new MainWindowViewModel(new CompanyStorage(_dir));
        vm.NewCompanyName = name;
        vm.CreateCompany();
        var c = vm.Company!;
        var masters = new InventoryService(c);
        var widget = masters.CreateStockItem("Widget", masters.CreateStockGroup("Goods").Id,
            masters.CreateSimpleUnit("Nos", "Numbers", decimalPlaces: 3).Id);
        masters.AddOpeningBalance(widget.Id, c.MainLocation!.Id, 50m, Money.FromRupees(900m));
        DomainLedger Add(string n, string g, bool dr)
        {
            var l = new DomainLedger(Guid.NewGuid(), n, c.FindGroupByName(g)!.Id, Money.Zero, openingIsDebit: dr);
            c.AddLedger(l);
            return l;
        }
        var party = purchase ? Add("Nonce Suppliers", "Sundry Creditors", false) : Add("Nonce Clients", "Sundry Debtors", true);
        var value = purchase ? Add("Purchase Account", "Purchase Accounts", true) : Add("Sales Account", "Sales Accounts", false);
        DomainLedger? freight = null;
        if (purchase)
        {
            freight = new DomainLedger(Guid.NewGuid(), "Inward Freight", c.FindGroupByName("Direct Expenses")!.Id,
                Money.Zero, openingIsDebit: true, methodOfAppropriation: MethodOfAppropriation.ByValue);
            c.AddLedger(freight);
        }
        return new Kit(vm, widget.Id, party, value, freight);
    }

    private static VoucherType TypeOf(MainWindowViewModel vm, VoucherBaseType b) =>
        vm.Company!.VoucherTypes.Single(t => t.BaseType == b && t.IsActive && !t.IsPosSales);

    private Company Reload(string name)
    {
        var fresh = new CompanyStorage(_dir);
        return fresh.Load(fresh.ListCompanies().Single(e => e.Name == name));
    }

    /// <summary>Opens the type's voucher, presses the REAL Ctrl+H until the screen is in Item Invoice mode, and
    /// fills party, value ledger and one Widget line (3 x 1,234.57 = 3,703.71).</summary>
    private static VoucherEntryViewModel OpenItemInvoice(Window w, Kit k, VoucherBaseType b)
    {
        k.Vm.OpenVoucher(TypeOf(k.Vm, b));
        var e = k.Vm.VoucherEntry!;
        Pump(w);
        for (var i = 0; i < 3 && !e.IsItemInvoice; i++) Key(w, PhysicalKey.H, RawInputModifiers.Control);
        Assert.True(e.IsItemInvoice);
        e.SelectedParty = e.Parties.Single(p => p.Ledger?.Id == k.Party.Id);
        e.SelectedStockLedger = e.StockLedgers.Single(l => l.Id == k.Value.Id);
        var row = e.InventoryLines[0];
        row.SelectedItem = e.StockItems.Single(i => i.Id == k.WidgetId);
        row.SelectedGodown = e.Godowns.Single(g => g.Id == k.Vm.Company!.MainLocation!.Id);
        row.QuantityText = "3";
        row.RateText = "1234.57";
        Pump(w);
        return e;
    }

    // ============================================================== the control

    /// <summary>
    /// THE CONTROL: with the flag OFF the item invoice's header narration row is realised and NOT visible, and the
    /// accepted invoice carries no line narration. <b>Mutation:</b> making <c>ShowInvoiceLineNarration</c> ignore
    /// the flag (<c>=&gt; IsItemInvoice || IsAccountingInvoice</c>) reddens this.
    /// </summary>
    [AvaloniaFact]
    public void Without_the_flag_the_item_invoice_offers_no_ledger_narration()
    {
        var k = NewBook("LNI8 Control Co", purchase: false);
        var w = new MainWindow { DataContext = k.Vm };
        try
        {
            w.Show();
            Assert.False(TypeOf(k.Vm, VoucherBaseType.Sales).ProvideNarrationForEachLedger);
            var e = OpenItemInvoice(w, k, VoucherBaseType.Sales);
            var party = Assert.Single(Boxes(w, "InvoicePartyNarrationBox"));
            Assert.False(party.IsEffectivelyVisible);
            Assert.False(Assert.Single(Boxes(w, "InvoiceValueNarrationBox")).IsEffectivelyVisible);
            Key(w, PhysicalKey.A, RawInputModifiers.Control);
            Assert.True(e.SavedVoucherId != Guid.Empty, e.Message);
            Assert.All(Reload("LNI8 Control Co").Vouchers.Single().Lines, l => Assert.Null(l.Narration));
        }
        finally { w.Close(); }
    }

    // ============================================================== through the real keys

    /// <summary>
    /// 🔴 <b>THROUGH THE REAL KEYS.</b> With the flag on, the REAL Tab off the value-ledger picker lands in the
    /// party's narration field, the next REAL Tab in the value ledger's; the words are typed as real text input; the
    /// REAL Ctrl+A accepts; and each narration is read off the book RELOADED through a fresh store handle on ITS
    /// leg.
    ///
    /// <para><b>Mutations:</b> (M1) replacing <c>NormaliseLegNarration(PartyLineNarration)</c> in
    /// <c>BuildItemInvoice</c> with <c>null</c> reddens the party assertion; (M2) dropping the
    /// <c>valueNarration = NormaliseLegNarration(ValueLineNarration)</c> assignment reddens the value assertion;
    /// (M3) removing the header narration <c>Grid</c> from the view reddens the focus assertions.</para>
    /// </summary>
    [AvaloniaFact]
    public void With_the_flag_on_an_item_invoice_takes_party_and_value_narrations_from_the_keyboard()
    {
        var k = NewBook("LNI8 Keys Co", purchase: false);
        TypeOf(k.Vm, VoucherBaseType.Sales).ProvideNarrationForEachLedger = true;
        var w = new MainWindow { DataContext = k.Vm };
        try
        {
            w.Show();
            var e = OpenItemInvoice(w, k, VoucherBaseType.Sales);

            var valuePicker = w.GetVisualDescendants().OfType<ComboBox>()
                .Single(c => ReferenceEquals(c.ItemsSource, e.StockLedgers) && c.IsEffectivelyVisible);
            valuePicker.Focus();
            Pump(w);
            Key(w, PhysicalKey.Tab);
            Assert.Equal("InvoicePartyNarrationBox", Focused(w)?.Name);
            w.KeyTextInput("Counter sale, cheque to follow");
            Pump(w);
            Key(w, PhysicalKey.Tab);
            Assert.Equal("InvoiceValueNarrationBox", Focused(w)?.Name);
            w.KeyTextInput("Widgets for the Pune order");
            Pump(w);
            Assert.Equal("Counter sale, cheque to follow", e.PartyLineNarration);
            Assert.Equal("Widgets for the Pune order", e.ValueLineNarration);

            Key(w, PhysicalKey.A, RawInputModifiers.Control);
            Assert.True(e.SavedVoucherId != Guid.Empty, e.Message);

            var book = Reload("LNI8 Keys Co");
            var v = book.Vouchers.Single();
            Assert.True(v.HasInventoryLines);
            Assert.Equal("Counter sale, cheque to follow", v.Lines.Single(l => l.LedgerId == k.Party.Id).Narration);
            Assert.Equal("Widgets for the Pune order", v.Lines.Single(l => l.LedgerId == k.Value.Id).Narration);
            Assert.Equal(3703.71m, v.Lines.Single(l => l.LedgerId == k.Value.Id).Amount.Amount);
        }
        finally { w.Close(); }
    }

    /// <summary>
    /// 🔴 <b>THE ADDITIONAL-COST ROW.</b> On a Purchase item invoice with additional costs tracked, the REAL Tab off a
    /// cost row's amount lands in that row's narration field; the words post on the freight leg of the reloaded
    /// book. <b>Mutation (M4):</b> replacing <c>carriedCostNarration = r.ToLineNarration();</c> with <c>null</c>
    /// reddens the reloaded assertion; (M5) removing <c>AdditionalCostNarrationBox</c> from the row template reddens
    /// the focus assertion.
    /// </summary>
    [AvaloniaFact]
    public void An_additional_cost_row_takes_its_own_narration_from_the_keyboard()
    {
        var k = NewBook("LNI8 Cost Co", purchase: true);
        TypeOf(k.Vm, VoucherBaseType.Purchase).ProvideNarrationForEachLedger = true;
        var w = new MainWindow { DataContext = k.Vm };
        try
        {
            w.Show();
            var e = OpenItemInvoice(w, k, VoucherBaseType.Purchase);
            e.TrackAdditionalCosts = true;
            Assert.True(e.ShowAdditionalCosts);
            var row = e.AdditionalCosts[0];
            row.SelectedLedger = k.Freight;
            row.AmountText = "555.53";
            Pump(w);

            var amount = w.GetVisualDescendants().OfType<TextBox>()
                .Single(t => ReferenceEquals(t.DataContext, row) && t.PlaceholderText == "0.00");
            amount.Focus();
            Pump(w);
            Key(w, PhysicalKey.Tab);
            var focused = Focused(w);
            Assert.Equal("AdditionalCostNarrationBox", focused?.Name);
            Assert.Same(row, focused!.DataContext);
            w.KeyTextInput("Lorry MH-12 AB 4471");
            Pump(w);

            Key(w, PhysicalKey.A, RawInputModifiers.Control);
            Assert.True(e.SavedVoucherId != Guid.Empty, e.Message);
            var v = Reload("LNI8 Cost Co").Vouchers.Single();
            var freightLeg = v.Lines.Single(l => l.LedgerId == k.Freight!.Id);
            Assert.Equal(555.53m, freightLeg.Amount.Amount);
            Assert.Equal("Lorry MH-12 AB 4471", freightLeg.Narration);
        }
        finally { w.Close(); }
    }

    /// <summary>
    /// A cost row carrying ONLY a narration is refused as incomplete rather than its words being dropped
    /// silently. <b>Mutation (M6):</b> reverting <c>AdditionalCostRowViewModel.IsBlank</c> to ignore the narration
    /// reddens this (the invoice posts and the words vanish).
    /// </summary>
    [AvaloniaFact]
    public void A_cost_row_with_only_a_narration_is_refused_not_dropped()
    {
        var k = NewBook("LNI8 Orphan Co", purchase: true);
        TypeOf(k.Vm, VoucherBaseType.Purchase).ProvideNarrationForEachLedger = true;
        var w = new MainWindow { DataContext = k.Vm };
        try
        {
            w.Show();
            var e = OpenItemInvoice(w, k, VoucherBaseType.Purchase);
            e.TrackAdditionalCosts = true;
            e.AdditionalCosts[0].LineNarration = "Octroi receipt pending";
            Key(w, PhysicalKey.A, RawInputModifiers.Control);
            Assert.Equal(Guid.Empty, e.SavedVoucherId);
            Assert.Empty(k.Vm.Company!.Vouchers);
            Assert.Contains("additional-cost line", e.Message);
        }
        finally { w.Close(); }
    }

    // ============================================================== alteration

    /// <summary>
    /// 🔴 <b>RELOAD ON ALTERATION.</b> A posted item invoice re-opens with each narration IN ITS FIELD; an edit to
    /// one and a deliberate CLEAR of another both reach the reloaded book, and the untouched cost-row narration
    /// survives. <b>Mutations:</b> (M7) deleting <c>LoadInvoiceLegNarrations(...)</c> in
    /// <c>RehydrateItemInvoiceFrom</c> reddens the field assertions; (M8) passing <c>Array.Empty&lt;string&gt;()</c>
    /// instead of <c>CurrentInvoiceLegNarrations()</c> to the guard does NOT redden this (the guard only excuses),
    /// so (M9) — dropping the narrations from <c>_editableLineNarrations</c> — is the one that reddens the clear.
    /// </summary>
    [AvaloniaFact]
    public void An_item_invoice_alteration_reloads_its_narrations_and_honours_an_edit_and_a_clear()
    {
        var k = NewBook("LNI8 Alter Co", purchase: true);
        TypeOf(k.Vm, VoucherBaseType.Purchase).ProvideNarrationForEachLedger = true;
        var w = new MainWindow { DataContext = k.Vm };
        Guid id;
        try
        {
            w.Show();
            var e = OpenItemInvoice(w, k, VoucherBaseType.Purchase);
            e.TrackAdditionalCosts = true;
            e.AdditionalCosts[0].SelectedLedger = k.Freight;
            e.AdditionalCosts[0].AmountText = "555.53";
            e.AdditionalCosts[0].LineNarration = "Lorry MH-12 AB 4471";
            e.PartyLineNarration = "Supplier bill SB-771";
            e.ValueLineNarration = "Widgets for the Pune order";
            Assert.True(e.Accept(), e.Message);
            id = e.SavedVoucherId;
        }
        finally { w.Close(); }

        var c = k.Vm.Company!;
        var open = VoucherEntryViewModel.ForAlter(c, id, new CompanyStorage(_dir), () => { }, () => { });
        Assert.False(open.IsRefused, open.Refusal);
        var alter = open.Entry!;
        Assert.True(alter.ShowInvoiceLineNarration);
        Assert.Equal("Supplier bill SB-771", alter.PartyLineNarration);
        Assert.Equal("Widgets for the Pune order", alter.ValueLineNarration);
        var costRow = alter.AdditionalCosts.Single(r => r.SelectedLedger?.Id == k.Freight!.Id);
        Assert.Equal("Lorry MH-12 AB 4471", costRow.LineNarration);
        Assert.True(costRow.ShowLineNarration);

        alter.PartyLineNarration = "";                                   // a deliberate clear
        alter.ValueLineNarration = "Widgets for the Nashik order";       // an edit
        Assert.True(alter.AcceptAlteration(), alter.Message);

        var v = Reload("LNI8 Alter Co").Vouchers.Single(x => x.Id == id);
        Assert.Null(v.Lines.Single(l => l.LedgerId == k.Party.Id).Narration);
        Assert.Equal("Widgets for the Nashik order", v.Lines.Single(l => l.LedgerId == k.Value.Id).Narration);
        Assert.Equal("Lorry MH-12 AB 4471", v.Lines.Single(l => l.LedgerId == k.Freight!.Id).Narration);
    }

    // ============================================================== canonical export → import

    /// <summary>
    /// 🔴 <b>EXPORT → IMPORT, READ OFF THE RELOADED IMPORT.</b> The keyed item invoice is reloaded, exported to
    /// canonical JSON and XML, each imported into a separate company that is saved and reloaded through a fresh
    /// handle; the three narrations sit on their own legs.
    /// </summary>
    [AvaloniaFact]
    public void Item_invoice_narrations_survive_canonical_export_and_import()
    {
        var k = NewBook("LNI8 Export Co", purchase: true);
        TypeOf(k.Vm, VoucherBaseType.Purchase).ProvideNarrationForEachLedger = true;
        var w = new MainWindow { DataContext = k.Vm };
        try
        {
            w.Show();
            var e = OpenItemInvoice(w, k, VoucherBaseType.Purchase);
            e.TrackAdditionalCosts = true;
            e.AdditionalCosts[0].SelectedLedger = k.Freight;
            e.AdditionalCosts[0].AmountText = "555.53";
            e.AdditionalCosts[0].LineNarration = "Lorry MH-12 AB 4471";
            e.PartyLineNarration = "Supplier bill SB-771";
            e.ValueLineNarration = "Widgets for the Pune order";
            Assert.True(e.Accept(), e.Message);
        }
        finally { w.Close(); }

        var source = Reload("LNI8 Export Co");
        foreach (var (format, bytes) in new[]
                 {
                     ("json", CanonicalJson.Export(source)),
                     ("xml", CanonicalXml.Export(source)),
                 })
        {
            var (model, errors) = format == "json" ? CanonicalJson.Parse(bytes) : CanonicalXml.Parse(bytes);
            Assert.Empty(errors);
            var targetDir = Path.Combine(_dir, "import-" + format);   // separate dir: see LineNarrationEntryTests
            var targetStorage = new CompanyStorage(targetDir);
            var tvm = new MainWindowViewModel(targetStorage);
            tvm.NewCompanyName = "LNI8 Import " + format;
            tvm.CreateCompany();
            var result = new CompanyImportService(tvm.Company!).Apply(model!, DuplicatePolicy.Skip);
            Assert.True(result.Applied);
            targetStorage.Save(tvm.Company!);

            var fresh = new CompanyStorage(targetDir);
            var imported = fresh.Load(fresh.ListCompanies().Single(x => x.Name == tvm.Company!.Name));
            var v = Assert.Single(imported.Vouchers);
            Assert.Equal("Supplier bill SB-771",
                v.Lines.Single(l => l.LedgerId == imported.FindLedgerByName("Nonce Suppliers")!.Id).Narration);
            Assert.Equal("Widgets for the Pune order",
                v.Lines.Single(l => l.LedgerId == imported.FindLedgerByName("Purchase Account")!.Id).Narration);
            Assert.Equal("Lorry MH-12 AB 4471",
                v.Lines.Single(l => l.LedgerId == imported.FindLedgerByName("Inward Freight")!.Id).Narration);
        }
    }

    // ============================================================== printed on the tax invoice

    private static string PdfText(PrintPreviewViewModel p) => Encoding.Latin1.GetString(p.PdfBytes);

    /// <summary>Posts a GST Sales item invoice (Widget at 18%, 3 x 1,234.57) through the shell's own voucher door
    /// with <i>Print voucher after saving</i> on, so the printing screen opens on the saved invoice.</summary>
    private static PrintPreviewViewModel PostGstSaleAndPrint(AlterationBook book, string? partyNarration,
        string? valueNarration)
    {
        var c = book.Company;
        book.EnableGst();
        var masters = new InventoryService(c);
        var widget = masters.CreateStockItem("Widget", masters.CreateStockGroup("Goods").Id,
            masters.CreateSimpleUnit("Nos", "Numbers", decimalPlaces: 3).Id);
        widget.Gst = new StockItemGstDetails { Taxability = GstTaxability.Taxable, RateBasisPoints = 1800 };
        masters.AddOpeningBalance(widget.Id, c.MainLocation!.Id, 50m, Money.FromRupees(900m));
        var client = book.Ledger("Nonce Clients", "Sundry Debtors");
        var sales = book.Ledger("Sales Account", "Sales Accounts");
        var type = book.Type(VoucherBaseType.Sales);
        type.ProvideNarrationForEachLedger = true;
        type.PrintAfterSaving = true;

        book.Shell.OpenVoucher(type);
        var e = book.Shell.VoucherEntry!;
        e.Mode = VoucherEntryMode.ItemInvoice;
        e.Date = book.On();
        e.SelectedParty = e.Parties.Single(p => p.Ledger?.Id == client.Id);
        e.SelectedStockLedger = e.StockLedgers.Single(l => l.Id == sales.Id);
        var row = e.InventoryLines[0];
        row.SelectedItem = e.StockItems.Single(i => i.Id == widget.Id);
        row.SelectedGodown = e.Godowns.Single(g => g.Id == c.MainLocation!.Id);
        row.QuantityText = "3";
        row.RateText = "1234.57";
        e.PartyLineNarration = partyNarration ?? "";
        e.ValueLineNarration = valueNarration ?? "";
        Assert.True(e.Accept(), e.Message);
        Assert.NotNull(book.Shell.PrintPreview);
        Assert.Equal(e.SavedVoucherId, book.Shell.PrintPreview!.SourceVoucherId);
        return book.Shell.PrintPreview!;
    }

    /// <summary>
    /// 🔴 <b>PRINTED.</b> The saved GST item invoice's printing screen is the TAX INVOICE, and its emitted PDF bytes
    /// carry each ledger narration named by its ledger. <b>Mutations:</b> (M10) deleting the LedgerNarrations loop in
    /// <c>InvoicePdf.BuildClosing</c> reddens the byte assertions; (M11) dropping
    /// <c>LedgerNarrations = LedgerNarrationsOf(company, voucher)</c> from <c>ProjectInvoice</c> reddens them too.
    /// </summary>
    [AvaloniaFact]
    public void The_item_invoice_ledger_narrations_print_on_its_tax_invoice()
    {
        using var book = AlterationBook.New("lni8_print");
        var preview = PostGstSaleAndPrint(book, "Counter sale", "Widgets for Pune");
        var pdf = PdfText(preview);
        Assert.Contains("TAX INVOICE", pdf, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("(Nonce Clients: Counter sale) Tj", pdf, StringComparison.Ordinal);
        Assert.Contains("(Sales Account: Widgets for Pune) Tj", pdf, StringComparison.Ordinal);
    }

    /// <summary>THE CONTROL for the print: the same invoice without narrations prints no ledger-narration line.</summary>
    [AvaloniaFact]
    public void An_item_invoice_without_ledger_narrations_prints_none()
    {
        using var book = AlterationBook.New("lni8_print_ctl");
        var pdf = PdfText(PostGstSaleAndPrint(book, null, null));
        Assert.Contains("TAX INVOICE", pdf, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("(Nonce Clients: ", pdf, StringComparison.Ordinal);
        Assert.DoesNotContain("(Sales Account: ", pdf, StringComparison.Ordinal);
    }

    // ============================================================== the Accounting Invoice's party leg

    /// <summary>
    /// The shared invoice header's party narration also serves the ACCOUNTING invoice's party leg, which wave 7 left
    /// without a field. <b>Mutation (M12):</b> dropping <c>narration: partyNarration</c> from the Sales arm of
    /// <c>AcceptAccountingInvoice</c>'s party line reddens this.
    /// </summary>
    [AvaloniaFact]
    public void An_accounting_invoice_party_leg_takes_the_header_narration()
    {
        var vm = new MainWindowViewModel(new CompanyStorage(_dir));
        vm.NewCompanyName = "LNI8 Service Co";
        vm.CreateCompany();
        var c = vm.Company!;
        var income = new DomainLedger(Guid.NewGuid(), "Consultancy Income", c.FindGroupByName("Indirect Incomes")!.Id,
            Money.Zero, openingIsDebit: false);
        var client = new DomainLedger(Guid.NewGuid(), "Nonce Clients", c.FindGroupByName("Sundry Debtors")!.Id,
            Money.Zero, openingIsDebit: true);
        c.AddLedger(income);
        c.AddLedger(client);
        var type = TypeOf(vm, VoucherBaseType.Sales);
        type.ProvideNarrationForEachLedger = true;
        var w = new MainWindow { DataContext = vm };
        try
        {
            w.Show();
            vm.OpenVoucher(type);
            var e = vm.VoucherEntry!;
            Pump(w);
            for (var i = 0; i < 3 && !e.IsAccountingInvoice; i++) Key(w, PhysicalKey.H, RawInputModifiers.Control);
            Assert.True(e.IsAccountingInvoice);
            Assert.True(e.ShowInvoiceLineNarration);
            e.SelectedParty = e.Parties.Single(p => p.Ledger?.Id == client.Id);
            while (e.AccountingInvoiceLines.Count == 0) e.AddAccountingInvoiceLine();
            e.AccountingInvoiceLines[0].SelectedLedger = e.AccountingInvoiceLedgers.Single(l => l.Id == income.Id);
            e.AccountingInvoiceLines[0].AmountText = "4321.07";
            Pump(w);
            var box = Assert.Single(Boxes(w, "InvoicePartyNarrationBox"));
            Assert.True(box.IsEffectivelyVisible);
            Assert.False(Assert.Single(Boxes(w, "InvoiceValueNarrationBox")).IsEffectivelyVisible);
            box.Focus();
            Pump(w);
            w.KeyTextInput("Retainer invoice Q3");
            Pump(w);
            Key(w, PhysicalKey.A, RawInputModifiers.Control);
            Assert.True(e.SavedVoucherId != Guid.Empty, e.Message);
            var v = Reload("LNI8 Service Co").Vouchers.Single();
            Assert.True(v.IsAccountingInvoice);
            Assert.Equal("Retainer invoice Q3", v.Lines.Single(l => l.LedgerId == client.Id).Narration);
        }
        finally { w.Close(); }
    }

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
}
