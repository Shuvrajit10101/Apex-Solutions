using System;
using System.IO;
using System.Linq;
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
using Apex.Ledger.Services;
using Xunit;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.Tests;

/// <summary>
/// 🔴 <b>CENSUS 5.11 (v68), the invoice modes and the no-silent-loss rule.</b> The vendor's sentence names the
/// modes the option serves — help.tallysolutions.com/voucher-types-tally/, Set up transaction options, opened by
/// content 2026-10-26: <i>"Enable Provide narration for each ledger in voucher to add narration for individual
/// ledgers"</i>, continuing that it applies by voucher mode, such as Accounting/Item Invoice or As Voucher.
///
/// <para>This file pins three things the Dr/Cr-grid tests in <see cref="LineNarrationEntryTests"/> cannot:
/// <list type="number">
///   <item>The <b>Accounting Invoice</b> (service) grid offers the field and posts it, through the real keys,
///     read back off a RELOADED book.</item>
///   <item>An <b>item invoice</b> alteration CARRIES a narration that an import put on its party, value or
///     additional-cost leg — the item screen has no narration field, so before this the rebuild dropped it.</item>
///   <item>A narration on a leg the altering screen RE-DERIVES (a GST tax leg) is REFUSED BY NAME rather than
///     dropped under "altered." — and the reloaded book still holds it.</item>
/// </list></para>
/// </summary>
public sealed class LineNarrationInvoiceModesTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ApexLineNarrInv_" + Guid.NewGuid().ToString("N"));

    private static void Pump(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.Measure(new Size(1280, 800));
        window.Arrange(new Rect(0, 0, 1280, 800));
        Dispatcher.UIThread.RunJobs();
    }

    private MainWindowViewModel NewServiceCompany(string name)
    {
        var vm = new MainWindowViewModel(new CompanyStorage(_dir));
        vm.NewCompanyName = name;
        vm.CreateCompany();
        var c = vm.Company!;
        c.AddLedger(new DomainLedger(Guid.NewGuid(), "Consultancy Income", c.FindGroupByName("Indirect Incomes")!.Id,
            Money.Zero, openingIsDebit: false));
        c.AddLedger(new DomainLedger(Guid.NewGuid(), "Nonce Clients", c.FindGroupByName("Sundry Debtors")!.Id,
            Money.Zero, openingIsDebit: true));
        return vm;
    }

    private static VoucherType SalesType(MainWindowViewModel vm) =>
        vm.Company!.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Sales && t.IsActive);

    private Company Reload(string name)
    {
        var fresh = new CompanyStorage(_dir);   // a FRESH store handle
        return fresh.Load(fresh.ListCompanies().Single(e => e.Name == name));
    }

    /// <summary>Opens a Sales voucher in Accounting Invoice mode and keys Consultancy Income 4,321.07 for the
    /// client. Returns the screen and its first particulars line.</summary>
    private static (VoucherEntryViewModel Entry, AccountingInvoiceLineViewModel Line) KeyServiceSale(
        MainWindowViewModel vm)
    {
        vm.OpenVoucher(SalesType(vm));
        var e = vm.VoucherEntry!;
        e.ChangeMode();   // As Voucher   -> Item Invoice
        e.ChangeMode();   // Item Invoice -> Accounting Invoice
        Assert.True(e.IsAccountingInvoice);
        e.SelectedParty = e.Parties.Single(p => p.Ledger?.Name == "Nonce Clients");
        while (e.AccountingInvoiceLines.Count == 0) e.AddAccountingInvoiceLine();
        var line = e.AccountingInvoiceLines[0];
        line.SelectedLedger = e.AccountingInvoiceLedgers.Single(l => l.Name == "Consultancy Income");
        line.AmountText = "4321.07";
        return (e, line);
    }

    private static TextBox[] AccountingNarrationBoxes(Window w) =>
        w.GetVisualDescendants().OfType<TextBox>().Where(t => t.Name == "AccountingLineNarrationBox").ToArray();

    // ============================================================== (1) the Accounting Invoice grid

    /// <summary>
    /// The control: with the flag OFF the service grid's narration field is realised and NOT visible.
    /// <b>Mutation:</b> making <c>AddAccountingInvoiceLine</c> set <c>ShowLineNarration = true</c> reddens this.
    /// </summary>
    [AvaloniaFact]
    public void Without_the_flag_the_service_grid_offers_no_narration_field()
    {
        var vm = NewServiceCompany("LNI Control Co");
        var w = new MainWindow { DataContext = vm };
        try
        {
            w.Show();
            Assert.False(SalesType(vm).ProvideNarrationForEachLedger);
            KeyServiceSale(vm);
            Pump(w);
            Assert.NotEmpty(AccountingNarrationBoxes(w));   // realised, so the absence is not vacuous
            Assert.All(AccountingNarrationBoxes(w), b => Assert.False(b.IsEffectivelyVisible));
        }
        finally { w.Close(); }
    }

    /// <summary>
    /// 🔴 <b>THROUGH THE REAL KEYS.</b> With the flag on, the REAL Tab off the particulars line's Amount lands in
    /// that line's narration field; the words are typed as real text input; the REAL Ctrl+A accepts; and the
    /// narration is read back off a book RELOADED through a fresh store handle — on the income line, not on the
    /// party line.
    ///
    /// <para><b>Mutations:</b> dropping <c>narration: l.ToLineNarration()</c> from <c>AcceptAccountingInvoice</c>'s
    /// income-leg build reddens the reloaded-line assertion; deleting the <c>ShowLineNarration =
    /// IsLineNarrationEnabled</c> initialiser in <c>AddAccountingInvoiceLine</c> reddens the focus assertion.</para>
    /// </summary>
    [AvaloniaFact]
    public void With_the_flag_on_a_service_invoice_line_takes_a_narration_from_the_keyboard_and_keeps_it()
    {
        var vm = NewServiceCompany("LNI Keys Co");
        SalesType(vm).ProvideNarrationForEachLedger = true;
        var w = new MainWindow { DataContext = vm };
        try
        {
            w.Show();
            var (e, line) = KeyServiceSale(vm);
            Pump(w);

            var amount = w.GetVisualDescendants().OfType<TextBox>()
                .First(t => ReferenceEquals(t.DataContext, line) && t.PlaceholderText == "0.00");
            amount.Focus();
            Pump(w);
            w.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
            Pump(w);
            var focused = TopLevel.GetTopLevel(w)?.FocusManager?.GetFocusedElement() as TextBox;
            Assert.NotNull(focused);
            Assert.Equal("AccountingLineNarrationBox", focused!.Name);
            Assert.Same(line, focused.DataContext);
            w.KeyTextInput("Retainer for Q3, audit support");
            Pump(w);
            Assert.Equal("Retainer for Q3, audit support", line.LineNarration);

            w.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);   // accept
            Pump(w);
            Assert.Single(vm.Company!.Vouchers);
            Assert.True(e.SavedVoucherId != Guid.Empty, e.Message);

            var book = Reload("LNI Keys Co");
            var v = Assert.Single(book.Vouchers);
            Assert.True(v.IsAccountingInvoice);
            var incomeId = book.FindLedgerByName("Consultancy Income")!.Id;
            var partyId = book.FindLedgerByName("Nonce Clients")!.Id;
            Assert.Equal("Retainer for Q3, audit support", v.Lines.Single(l => l.LedgerId == incomeId).Narration);
            Assert.Null(v.Lines.Single(l => l.LedgerId == partyId).Narration);
        }
        finally { w.Close(); }
    }

    /// <summary>
    /// 🔴 <b>w8 fix (review ec LOW-2) — A HEADER NARRATION LEFT BEHIND BY Ctrl+H IS REFUSED, NOT DROPPED.</b> The
    /// value-leg narration is typed in Item Invoice mode, then Ctrl+H moves to Accounting Invoice, where no field
    /// reads it. The REAL Ctrl+A is refused by name and NOTHING reaches the reloaded book; the way out (back to Item
    /// Invoice and clear it) lets the same service invoice post. <b>Mutation (L2):</b> deleting the
    /// <c>HeaderNarrationOrphanedByModeError</c> check in <c>Accept</c> posts on the first Ctrl+A and reddens this.
    /// </summary>
    [AvaloniaFact]
    public void A_value_narration_left_behind_by_a_mode_switch_is_refused_by_name_not_dropped()
    {
        var vm = NewServiceCompany("LNI Orphan Mode Co");
        SalesType(vm).ProvideNarrationForEachLedger = true;
        var w = new MainWindow { DataContext = vm };
        try
        {
            w.Show();
            vm.OpenVoucher(SalesType(vm));
            var e = vm.VoucherEntry!;
            e.ChangeMode();   // As Voucher -> Item Invoice
            Assert.True(e.IsItemInvoice);
            e.ValueLineNarration = "VALUE-NARR-ORPHAN";
            e.ChangeMode();   // Item Invoice -> Accounting Invoice
            Assert.True(e.IsAccountingInvoice);
            e.SelectedParty = e.Parties.Single(p => p.Ledger?.Name == "Nonce Clients");
            while (e.AccountingInvoiceLines.Count == 0) e.AddAccountingInvoiceLine();
            var line = e.AccountingInvoiceLines[0];
            line.SelectedLedger = e.AccountingInvoiceLedgers.Single(l => l.Name == "Consultancy Income");
            line.AmountText = "4321.07";
            Pump(w);

            w.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
            Pump(w);
            Assert.Empty(vm.Company!.Vouchers);
            Assert.Empty(Reload("LNI Orphan Mode Co").Vouchers);
            Assert.Contains("VALUE-NARR-ORPHAN", e.Message ?? string.Empty, StringComparison.Ordinal);
            Assert.Contains("would be lost", e.Message ?? string.Empty, StringComparison.Ordinal);

            // The way out the message names: clear it, and the same service invoice posts.
            e.ValueLineNarration = string.Empty;
            w.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
            Pump(w);
            Assert.Single(Reload("LNI Orphan Mode Co").Vouchers);
        }
        finally { w.Close(); }
    }

    // ============================================================== (2)/(3) item invoice alteration

    private const string WidgetRate = "1234.57";
    private const string WidgetQty = "2";

    private sealed class Kit
    {
        public required AlterationBook Book { get; init; }
        public required StockItem Widget { get; init; }
        public required DomainLedger Supplier { get; init; }
        public required DomainLedger Purchases { get; init; }
        public required DomainLedger Freight { get; init; }
        public required VoucherType PurchaseType { get; init; }
    }

    private static Kit Seed(AlterationBook book)
    {
        var c = book.Company;
        book.EnableGst();
        var masters = new InventoryService(c);
        var group = masters.CreateStockGroup("Goods");
        var nos = masters.CreateSimpleUnit("Nos", "Numbers", decimalPlaces: 3);
        var widget = masters.CreateStockItem("Widget", group.Id, nos.Id);
        widget.Gst = new StockItemGstDetails { Taxability = GstTaxability.Taxable, RateBasisPoints = 1800 };
        // The additional-cost ledger must exist BEFORE the entry screen is built (AdditionalCostLedgers is read once
        // in the constructor, from Ledger.IsAdditionalCostLedger).
        var freight = new DomainLedger(
            Guid.NewGuid(), "Inward Freight", c.FindGroupByName("Direct Expenses")!.Id, Money.Zero,
            openingIsDebit: true, methodOfAppropriation: MethodOfAppropriation.ByValue);
        c.AddLedger(freight);
        var kit = new Kit
        {
            Book = book,
            Freight = freight,
            Widget = widget,
            Supplier = book.Ledger("Nonce Suppliers", "Sundry Creditors"),
            Purchases = book.Ledger("Purchases", "Purchase Accounts"),
            PurchaseType = book.Type(VoucherBaseType.Purchase),
        };
        book.Storage.Save(c);
        return kit;
    }

    private static Voucher PostInvoice(Kit kit, string? freightAmount = null)
    {
        var entry = new VoucherEntryViewModel(
            kit.Book.Company, kit.PurchaseType, kit.Book.Storage,
            onSaved: () => { }, onCancelled: () => { }, kit.Book.On());
        entry.Mode = VoucherEntryMode.ItemInvoice;
        entry.SelectedParty = entry.Parties.Single(p => p.Ledger?.Id == kit.Supplier.Id);
        entry.SelectedStockLedger = entry.StockLedgers.Single(l => l.Id == kit.Purchases.Id);
        entry.Narration = "Nonce narration ONE";
        var row = entry.InventoryLines[0];
        row.SelectedItem = entry.StockItems.Single(i => i.Id == kit.Widget.Id);
        row.SelectedGodown = entry.Godowns.Single(g => g.Id == kit.Book.Company.MainLocation!.Id);
        row.QuantityText = WidgetQty;
        row.RateText = WidgetRate;
        if (freightAmount is not null)
        {
            entry.TrackAdditionalCosts = true;   // the F12 knob, as PurchaseAndPosAlterationTests keys it
            Assert.True(entry.ShowAdditionalCosts);
            entry.AdditionalCosts[0].SelectedLedger = kit.Freight;
            entry.AdditionalCosts[0].AmountText = freightAmount;
        }
        Assert.True(entry.Accept(), entry.Message);
        return kit.Book.Company.Vouchers.Last(v => v.TypeId == kit.PurchaseType.Id);
    }

    /// <summary>Re-posts through <see cref="LedgerService.Replace"/> — the door <c>ImportPlan</c> uses — with the
    /// narration <paramref name="pick"/> chooses stamped on each line (null keeps the line unnarrated).</summary>
    private static Voucher StampNarrations(AlterationBook book, Voucher posted, Func<EntryLine, string?> pick)
    {
        var lines = posted.Lines.Select(l => new EntryLine(
            l.LedgerId, l.Amount, l.Side,
            l.BillAllocations.Count > 0 ? l.BillAllocations : null,
            l.CostAllocations.Count > 0 ? l.CostAllocations : null,
            l.BankAllocation, l.Forex, l.Gst, l.Tds, l.Tcs, l.Payroll,
            narration: pick(l))).ToList();
        var replacement = new Voucher(
            posted.Id, posted.TypeId, posted.Date, lines,
            number: posted.Number, narration: posted.Narration, partyId: posted.PartyId,
            cancelled: posted.Cancelled, optional: posted.Optional, postDated: posted.PostDated,
            applicableUpto: posted.ApplicableUpto, inventoryLines: posted.InventoryLines,
            posTenders: posted.PosTenders, referenceNo: posted.ReferenceNo,
            referenceDate: posted.ReferenceDate, isAccountingInvoice: posted.IsAccountingInvoice);
        new LedgerService(book.Company).Replace(posted.Id, replacement);
        book.Storage.Save(book.Company);
        return book.Company.FindVoucher(posted.Id)!;
    }

    private static Company ReloadOf(AlterationBook book)
    {
        var fresh = new CompanyStorage(book.Dir);
        return fresh.Load(fresh.ListCompanies().Single(e => e.Name == book.Company.Name));
    }

    /// <summary>
    /// 🔴 <b>CARRY.</b> An item invoice whose PARTY and VALUE legs carry narrations (put there by an import) is
    /// re-opened and re-accepted with only the voucher narration changed: both line narrations are still on the
    /// right legs of the RELOADED book, and the operator was told only "altered.".
    ///
    /// <para><b>Mutations:</b> dropping <c>narration: partyNarration</c> from the party-leg build, or
    /// <c>narration: valueNarration</c> from the value-leg build, reddens this (the guard then refuses the
    /// accept, so the first assertion fails).</para>
    /// </summary>
    [Fact]
    public void An_item_invoice_re_accept_keeps_the_narrations_an_import_put_on_its_party_and_value_legs()
    {
        using var book = AlterationBook.New("ln_item_carry");
        var kit = Seed(book);
        var posted = PostInvoice(kit);
        posted = StampNarrations(book, posted, l =>
            l.LedgerId == kit.Supplier.Id ? "Supplier bill SB-771, 30 days"
            : l.LedgerId == kit.Purchases.Id ? "Widgets for the Pune order"
            : null);

        var open = book.ForAlter(posted.Id);
        Assert.False(open.IsRefused, open.Refusal);
        var entry = open.Entry!;
        entry.Narration = "Nonce narration TWO";
        Assert.True(entry.AcceptAlteration(), entry.Message);

        var reloaded = ReloadOf(book).Vouchers.Single(v => v.Id == posted.Id);
        Assert.Equal("Nonce narration TWO", reloaded.Narration);
        Assert.Equal("Supplier bill SB-771, 30 days",
            reloaded.Lines.Single(l => l.LedgerId == kit.Supplier.Id).Narration);
        Assert.Equal("Widgets for the Pune order",
            reloaded.Lines.Single(l => l.LedgerId == kit.Purchases.Id).Narration);
        Assert.All(reloaded.Lines.Where(l => l.LedgerId != kit.Supplier.Id && l.LedgerId != kit.Purchases.Id),
            l => Assert.Null(l.Narration));
    }

    /// <summary>
    /// 🔴 <b>REFUSE, DON'T DROP.</b> A narration on a GST tax leg — a leg the item screen RE-DERIVES on accept and
    /// has no field for — makes the re-accept refuse BY NAME (the ledger and the words), and the reloaded book
    /// still holds the narration. Measured before the guard: accepted as "altered." with the narration gone.
    ///
    /// <para><b>Mutation:</b> deleting the <c>DroppedLineNarrationRefusal</c> check at the top of
    /// <c>VoucherEntryViewModel.CommitAlteration</c> reddens this at the first assertion.</para>
    /// </summary>
    [Fact]
    public void A_narration_on_a_re_derived_tax_leg_is_refused_by_name_and_left_on_the_book()
    {
        using var book = AlterationBook.New("ln_item_refuse");
        var kit = Seed(book);
        var posted = PostInvoice(kit);
        var taxLeg = posted.Lines.First(l => l.Gst is not null);
        var taxLedgerName = book.Company.FindLedger(taxLeg.LedgerId)!.Name;
        posted = StampNarrations(book, posted, l =>
            l.LedgerId == taxLeg.LedgerId && l.Side == taxLeg.Side ? "Input credit claimed in GSTR-3B May" : null);

        var open = book.ForAlter(posted.Id);
        Assert.False(open.IsRefused, open.Refusal);
        var entry = open.Entry!;
        entry.Narration = "Nonce narration TWO";
        Assert.False(entry.AcceptAlteration());
        Assert.Contains(taxLedgerName, entry.Message);
        Assert.Contains("Input credit claimed in GSTR-3B May", entry.Message);

        var reloaded = ReloadOf(book).Vouchers.Single(v => v.Id == posted.Id);
        Assert.Equal("Nonce narration ONE", reloaded.Narration);   // nothing was written
        Assert.Equal("Input credit claimed in GSTR-3B May",
            reloaded.Lines.Single(l => l.LedgerId == taxLeg.LedgerId && l.Side == taxLeg.Side).Narration);
    }

    /// <summary>
    /// 🔴 <b>CARRY ACROSS AN AMENDMENT.</b> A narration on an ADDITIONAL-COST leg is matched by ledger alone — it
    /// moves no figure — so amending the freight from 555.53 to 777.71 keeps it on the reloaded book's freight leg.
    /// <b>Mutation:</b> replacing <c>carriedCostNarration = r.ToLineNarration();</c> in <c>BuildItemInvoice</c> with
    /// <c>null</c> reddens this (wave 8: the row's field, re-loaded on alteration, is now the one source).
    /// </summary>
    [Fact]
    public void An_additional_cost_leg_keeps_its_narration_when_its_amount_is_amended()
    {
        using var book = AlterationBook.New("ln_item_cost");
        var kit = Seed(book);
        var posted = PostInvoice(kit, freightAmount: "555.53");
        posted = StampNarrations(book, posted, l =>
            l.LedgerId == kit.Freight.Id ? "Lorry MH-12 AB 4471, two trips" : null);

        var open = book.ForAlter(posted.Id);
        Assert.False(open.IsRefused, open.Refusal);
        var entry = open.Entry!;
        var freightRow = entry.AdditionalCosts.Single(r => r.SelectedLedger?.Id == kit.Freight.Id);
        Assert.Equal("555.53", freightRow.AmountText);
        freightRow.AmountText = "777.71";
        Assert.True(entry.AcceptAlteration(), entry.Message);

        var reloaded = ReloadOf(book).Vouchers.Single(v => v.Id == posted.Id);
        var freightLeg = reloaded.Lines.Single(l => l.LedgerId == kit.Freight.Id);
        Assert.Equal(777.71m, freightLeg.Amount.Amount);
        Assert.Equal("Lorry MH-12 AB 4471, two trips", freightLeg.Narration);
    }

    // ============================================================== (4) the POS bill alteration

    /// <summary>
    /// 🔴 <b>THE POS BILL REBUILDS EVERY LINE</b>, and its re-derived GST legs have no narration field, so a
    /// narration an import put on a GST leg is refused BY NAME on alteration and left on the reloaded book.
    /// ⚠️ <i>Wave 8 (IV-236) moved this test off the Sales leg:</i> the Sales and tender legs now HAVE fields, so a
    /// narration there is re-loaded and carried (pinned by <c>LineNarrationPosBillTests</c>); the refusal survives
    /// for the legs that still have none. GST is on (Counter Widget at 18%, 2 x 1,234.57), tendered exactly in cash.
    /// <b>Mutation:</b> deleting the <c>DroppedLineNarrationRefusal</c> check in
    /// <c>PosBillingViewModel</c>'s alteration accept reddens this at the first assertion.
    /// </summary>
    [Fact]
    public void A_pos_bill_alteration_that_would_drop_a_line_narration_is_refused_by_name()
    {
        using var book = AlterationBook.New("ln_pos_refuse");
        var c = book.Company;
        book.EnableGst();
        var masters = new InventoryService(c);
        var widget = masters.CreateStockItem("Counter Widget", masters.CreateStockGroup("Retail").Id,
            masters.CreateSimpleUnit("Nos", "Numbers", decimalPlaces: 3).Id);
        widget.Gst = new StockItemGstDetails { Taxability = GstTaxability.Taxable, RateBasisPoints = 1800 };
        var sales = book.Ledger("Retail Sales", "Sales Accounts");
        var cash = book.Ledger("Till Cash", "Cash-in-Hand");
        var posType = new VoucherType(Guid.NewGuid(), "Sales (POS)", VoucherBaseType.Sales, useForPos: true,
            posConfig: new PosConfig());
        c.AddVoucherType(posType);
        book.Storage.Save(c);

        var vm = new PosBillingViewModel(c, posType, book.Storage, onSaved: () => { }, onCancelled: () => { });
        vm.Date = book.On(4);
        vm.SelectedSalesLedger = vm.SalesLedgers.Single(l => l.Id == sales.Id);
        vm.SelectedGodown = vm.Godowns.Single(g => g.Id == c.MainLocation!.Id);
        var row = vm.Items[0];
        row.SelectedItem = vm.StockItems.Single(i => i.Id == widget.Id);
        row.SelectedGodown = vm.Godowns.Single(g => g.Id == c.MainLocation!.Id);
        row.QuantityText = WidgetQty;
        row.RateText = WidgetRate;
        vm.Narration = "Counter nonce ONE";
        vm.CashRow.SelectedLedger = cash;
        Assert.True(vm.Accept(), vm.Message);
        var posted = c.Vouchers.Last(v => v.TypeId == posType.Id);
        var taxLeg = posted.Lines.First(l => l.Gst is not null);
        var taxLedgerName = c.FindLedger(taxLeg.LedgerId)!.Name;

        posted = StampNarrations(book, posted, l =>
            l.LedgerId == taxLeg.LedgerId && l.Side == taxLeg.Side ? "Festival counter, stall 9" : null);

        var open = PosBillingViewModel.ForAlter(c, posted.Id, book.Storage, onSaved: () => { }, onCancelled: () => { });
        Assert.False(open.IsRefused, open.Refusal);
        var alter = open.Entry!;
        alter.Narration = "Counter nonce TWO";
        Assert.False(alter.AcceptAlteration());
        Assert.Contains(taxLedgerName, alter.Message);
        Assert.Contains("Festival counter, stall 9", alter.Message);

        var reloaded = ReloadOf(book).Vouchers.Single(v => v.Id == posted.Id);
        Assert.Equal("Counter nonce ONE", reloaded.Narration);
        Assert.Equal("Festival counter, stall 9",
            reloaded.Lines.Single(l => l.LedgerId == taxLeg.LedgerId && l.Side == taxLeg.Side).Narration);
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
