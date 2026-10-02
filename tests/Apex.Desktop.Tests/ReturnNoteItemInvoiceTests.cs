using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Desktop.Views;
using DomainLedger = Apex.Ledger.Domain.Ledger;
using Path = System.IO.Path;
using Xunit;

namespace Apex.Desktop.Tests;

/// <summary>
/// 🔴 <b>CENSUS 4.7 / 4.8 — DEFECT T0-10, THE SCREEN HALF. A CREDIT OR DEBIT NOTE IS ENTERED AS AN ITEM INVOICE.</b>
///
/// <para><b>The route, from the vendor's own documentation (ruling 14).</b> help.tallysolutions.com, "How to Record
/// a Sales Return Using Credit Note Under GST in TallyPrime": <i>"Press Ctrl+H (Change Mode) &gt; select Item
/// Invoice"</i>, then <i>"Select the stock item that you initially sold and specify the Quantity and Rate based on
/// the returns received"</i>. The purchase-return page says the same for a Debit Note. So the route this file
/// drives is <b>Alt+F6 (Credit Note) / Alt+F5 (Debit Note) → Ctrl+H → the item grid</b>, and every test here
/// presses those keys rather than calling the view-model method behind them: a route proved only by calling
/// <c>ToggleItemInvoice()</c> is a route no keyboard reaches.</para>
///
/// <para>🔴 <b>WHY THE SIDES ARE THE POINT.</b> A return is not a second invoice — every derived leg is on the
/// opposite side of the one it reverses, and the stock moves the opposite way, while the GST HEAD does not change
/// (a sales return still touches <b>Output</b> tax; it just debits it). Get the head wrong instead of the side and
/// the note claims input credit on a sale — a wrong filed return rather than a wrong screen. Both are asserted
/// against the posted voucher, never against a view-model flag.</para>
/// </summary>
public sealed class ReturnNoteItemInvoiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public ReturnNoteItemInvoiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexReturnNoteTests_" + Guid.NewGuid().ToString("N"));
        _storage = new CompanyStorage(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    // ================================================================= fixture

    private const string GstinHome = "27AAPFU0939F1ZV";

    private sealed class Kit
    {
        public required MainWindowViewModel Vm { get; init; }
        public required string CompanyName { get; init; }
        public required Guid ItemId { get; init; }
        public required Guid MainGodownId { get; init; }
        public required Guid PurchasesId { get; init; }
        public required Guid PurchaseReturnsId { get; init; }
        public required Guid SalesId { get; init; }
        public required Guid SalesReturnsId { get; init; }
        public required Guid SupplierId { get; init; }
        public required Guid CustomerId { get; init; }
    }

    private static DomainLedger AddLedger(Company c, string name, string groupName)
    {
        var group = c.FindGroupByName(groupName) ?? throw new InvalidOperationException($"No group '{groupName}'.");
        var l = new DomainLedger(Guid.NewGuid(), name, group.Id, Money.Zero, openingIsDebit: false);
        c.AddLedger(l);
        return l;
    }

    /// <summary>
    /// A trading company with opening stock and the four value ledgers a return needs: "Sales Returns" under
    /// <b>Sales Accounts</b> and "Purchase Returns" under <b>Purchase Accounts</b>, beside the ordinary Sales and
    /// Purchases. That placement is the whole reason a note needs no new accounting family — only the opposite side.
    /// </summary>
    private Kit NewKit(string companyName, bool withGst = false)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = companyName;
        vm.CreateCompany();
        var c = vm.Company!;

        var masters = new InventoryService(c);
        var grp = masters.CreateStockGroup("Goods");
        var nos = masters.CreateSimpleUnit("Nos", "Numbers");
        var item = masters.CreateStockItem("Widget", grp.Id, nos.Id);
        masters.AddOpeningBalance(item.Id, c.MainLocation!.Id, 100m, Money.FromRupees(100m));

        if (withGst)
        {
            new GstService(c).EnableGst(new GstConfig
            {
                HomeStateCode = "27",
                Gstin = GstinHome,
                RegistrationType = GstRegistrationType.Regular,
                ApplicableFrom = c.FinancialYearStart,
                Periodicity = GstReturnPeriodicity.Monthly,
            });
            item.Gst = new StockItemGstDetails
            {
                HsnSac = "847130", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800,
            };
        }

        var kit = new Kit
        {
            Vm = vm,
            CompanyName = companyName,
            ItemId = item.Id,
            MainGodownId = c.MainLocation!.Id,
            PurchasesId = AddLedger(c, "Purchases", "Purchase Accounts").Id,
            PurchaseReturnsId = AddLedger(c, "Purchase Returns", "Purchase Accounts").Id,
            SalesId = AddLedger(c, "Sales", "Sales Accounts").Id,
            SalesReturnsId = AddLedger(c, "Sales Returns", "Sales Accounts").Id,
            SupplierId = AddLedger(c, "Acme Supplies", "Sundry Creditors").Id,
            CustomerId = AddLedger(c, "Beta Buyers", "Sundry Debtors").Id,
        };
        _storage.Save(c);
        return kit;
    }

    private static DateOnly AsOf(Company c) => c.FinancialYearStart.AddYears(1).AddDays(-1);

    private static decimal OnHand(Kit k) =>
        new InventoryLedger(k.Vm.Company!).OnHand(k.ItemId, k.MainGodownId, AsOf(k.Vm.Company!));

    private static void FillItemLine(VoucherEntryViewModel entry, Kit k, decimal qty, string rate)
    {
        var line = entry.InventoryLines[0];
        line.SelectedItem = entry.StockItems.Single(i => i.Id == k.ItemId);
        line.SelectedGodown = entry.Godowns.Single(g => g.Id == k.MainGodownId);
        line.QuantityText = qty.ToString(System.Globalization.CultureInfo.InvariantCulture);
        line.RateText = rate;
    }

    private static Voucher PostedNote(Kit k, VoucherBaseType nature)
    {
        var type = k.Vm.Company!.VoucherTypes.Single(t => t.BaseType == nature && t.IsActive);
        return k.Vm.Company!.Vouchers.Single(v => v.TypeId == type.Id);
    }

    // ================================================================= 1 — the keyboard route, in the realised tree

    private static IEnumerable<Visual> Descendants(Visual v)
    {
        foreach (var child in v.GetVisualChildren())
        {
            yield return child;
            foreach (var g in Descendants(child)) yield return g;
        }
    }

    private static void Pump(MainWindow w)
    {
        w.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        w.UpdateLayout();
    }

    /// <summary>
    /// 🔴 <b>THE DOOR, OPENED WITH THE KEYS AN OPERATOR ACTUALLY PRESSES.</b> Alt+F6 opens the Credit Note page and
    /// Ctrl+H changes its mode to Item Invoice; the item grid then REALISES, and its value-ledger caption reads
    /// <b>"Sales Returns"</b> — the sales-side family, on a document that reverses a sale.
    ///
    /// <para><b>Red on today's main</b>, and red in two independent ways: <c>CanBeItemInvoice</c> was Purchase-or-Sales
    /// so <c>IsChangeModeEntry</c> was false and Ctrl+H did nothing at all, and even had the mode been forced the
    /// grid's caption would have read "Sales" because <c>StockLedgerCaption</c> was a two-way test.</para>
    ///
    /// <para>The caption is looked up BY ITS RENDERED TEXT in the visual tree and asserted EFFECTIVELY visible —
    /// not <c>IsVisible</c>, which is a control's own flag and reports true inside a collapsed parent. A test
    /// asserting <c>entry.IsItemInvoice</c> would pass against a build whose grid never renders, which is the
    /// defect class this repository has now filed several times.</para>
    /// </summary>
    [AvaloniaFact]
    public void Ctrl_H_reaches_item_invoice_mode_on_a_credit_note_and_the_grid_realises()
    {
        var k = NewKit("Credit Note Door Co");
        var window = new MainWindow { DataContext = k.Vm, Width = 1440, Height = 900 };
        window.Show();
        Pump(window);

        // Alt+F6 — the Credit Note page.
        window.KeyPressQwerty(PhysicalKey.F6, RawInputModifiers.Alt);
        Pump(window);
        Assert.Equal(Screen.VoucherEntry, k.Vm.CurrentScreen);
        var entry = k.Vm.VoucherEntry!;
        Assert.Equal(VoucherBaseType.CreditNote, entry.Type.BaseType);

        // The chord is live on this page at all — the gate that used to exclude it.
        Assert.True(entry.CanBeItemInvoice);
        Assert.True(k.Vm.IsChangeModeEntry);
        Assert.False(entry.IsItemInvoice);

        // Ctrl+H — Change Mode.
        window.KeyPressQwerty(PhysicalKey.H, RawInputModifiers.Control);
        Pump(window);
        Assert.True(entry.IsItemInvoice);

        // The grid is on screen, and it names the SALES-side return family.
        Assert.Equal("Sales Returns", entry.StockLedgerCaption);
        Assert.Equal("Customer", entry.PartyCaption);
        var caption = Descendants(window).OfType<TextBlock>()
            .FirstOrDefault(t => t.Text == "Sales Returns" && t.IsEffectivelyVisible);
        Assert.True(caption is not null,
            "The item-invoice value-ledger caption did not render on the Credit Note page — the grid has no door.");

        window.Close();
    }

    /// <summary>The Debit Note's mirror: Alt+F5, Ctrl+H, and the caption names the PURCHASE-side return family.</summary>
    [AvaloniaFact]
    public void Ctrl_H_reaches_item_invoice_mode_on_a_debit_note_and_names_the_purchase_family()
    {
        var k = NewKit("Debit Note Door Co");
        var window = new MainWindow { DataContext = k.Vm, Width = 1440, Height = 900 };
        window.Show();
        Pump(window);

        window.KeyPressQwerty(PhysicalKey.F5, RawInputModifiers.Alt);
        Pump(window);
        var entry = k.Vm.VoucherEntry!;
        Assert.Equal(VoucherBaseType.DebitNote, entry.Type.BaseType);

        window.KeyPressQwerty(PhysicalKey.H, RawInputModifiers.Control);
        Pump(window);

        Assert.True(entry.IsItemInvoice);
        Assert.Equal("Purchase Returns", entry.StockLedgerCaption);
        Assert.Equal("Supplier", entry.PartyCaption);
        Assert.True(Descendants(window).OfType<TextBlock>()
                .Any(t => t.Text == "Purchase Returns" && t.IsEffectivelyVisible),
            "The item-invoice value-ledger caption did not render on the Debit Note page.");

        window.Close();
    }

    // ================================================================= 2 — the money and the stock, posted

    /// <summary>
    /// 🔴 <b>THE ROW, END TO END THROUGH THE SCREEN.</b> A sales return of 4 Widgets @ ₹150 posts
    /// <b>Dr Sales Returns 600 / Cr Beta Buyers 600</b> — the exact opposite sides from the Sales invoice it
    /// mirrors — and brings the goods back: on-hand 100 → <b>104</b>.
    ///
    /// <para>The sides are read off the POSTED voucher, not off the preview string, because the preview and the
    /// build derive them separately and a note is precisely where they could disagree.</para>
    /// </summary>
    [Fact]
    public void A_credit_note_item_invoice_posts_reversed_legs_and_brings_stock_back_in()
    {
        var k = NewKit("Credit Note Money Co");

        k.Vm.OpenVoucher(VoucherBaseType.CreditNote);
        var entry = k.Vm.VoucherEntry!;
        k.Vm.ChangeMode();
        Assert.True(entry.IsItemInvoice);

        entry.SelectedParty = entry.Parties.Single(p => p.Ledger?.Id == k.CustomerId);
        entry.SelectedStockLedger = entry.StockLedgers.Single(l => l.Id == k.SalesReturnsId);
        FillItemLine(entry, k, 4m, "150.00");
        Assert.Equal("600.00", entry.ItemsTotalText);
        Assert.True(entry.Accept());

        var posted = PostedNote(k, VoucherBaseType.CreditNote);
        var dr = posted.Lines.Single(l => l.Side == DrCr.Debit);
        var cr = posted.Lines.Single(l => l.Side == DrCr.Credit);
        Assert.Equal(k.SalesReturnsId, dr.LedgerId);   // the stock leg — a DEBIT, because the goods come IN
        Assert.Equal(k.CustomerId, cr.LedgerId);       // the customer is CREDITED — we owe them back
        Assert.Equal(600m, dr.Amount.Amount);

        Assert.Equal(StockDirection.Inward, posted.InventoryLines.Single().Direction);
        Assert.Equal(104m, OnHand(k));
    }

    /// <summary>
    /// The Debit Note mirror: a purchase return of 6 @ ₹100 posts <b>Dr Acme Supplies 600 / Cr Purchase Returns
    /// 600</b> and sends the goods back out — on-hand 100 → <b>94</b>.
    /// </summary>
    [Fact]
    public void A_debit_note_item_invoice_posts_reversed_legs_and_sends_stock_back_out()
    {
        var k = NewKit("Debit Note Money Co");

        k.Vm.OpenVoucher(VoucherBaseType.DebitNote);
        var entry = k.Vm.VoucherEntry!;
        k.Vm.ChangeMode();

        entry.SelectedParty = entry.Parties.Single(p => p.Ledger?.Id == k.SupplierId);
        entry.SelectedStockLedger = entry.StockLedgers.Single(l => l.Id == k.PurchaseReturnsId);
        FillItemLine(entry, k, 6m, "100.00");
        Assert.True(entry.Accept());

        var posted = PostedNote(k, VoucherBaseType.DebitNote);
        Assert.Equal(k.SupplierId, posted.Lines.Single(l => l.Side == DrCr.Debit).LedgerId);
        Assert.Equal(k.PurchaseReturnsId, posted.Lines.Single(l => l.Side == DrCr.Credit).LedgerId);

        Assert.Equal(StockDirection.Outward, posted.InventoryLines.Single().Direction);
        Assert.Equal(94m, OnHand(k));
    }

    /// <summary>The note survives a save/reload with both halves intact — the stock movement is not a session artefact.</summary>
    [Fact]
    public void A_posted_credit_note_keeps_its_stock_and_its_legs_across_a_reload()
    {
        var k = NewKit("Credit Note Persist Co");

        k.Vm.OpenVoucher(VoucherBaseType.CreditNote);
        var entry = k.Vm.VoucherEntry!;
        k.Vm.ChangeMode();
        entry.SelectedParty = entry.Parties.Single(p => p.Ledger?.Id == k.CustomerId);
        entry.SelectedStockLedger = entry.StockLedgers.Single(l => l.Id == k.SalesReturnsId);
        FillItemLine(entry, k, 4m, "150.00");
        Assert.True(entry.Accept());

        var reloaded = _storage.Load(_storage.ListCompanies().Single(e => e.Name == k.CompanyName));
        var type = reloaded.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.CreditNote && t.IsActive);
        var note = reloaded.Vouchers.Single(v => v.TypeId == type.Id);

        Assert.True(note.HasInventoryLines);
        Assert.Equal(StockDirection.Inward, note.InventoryLines.Single().Direction);
        Assert.Equal(600m, note.InventoryLinesValue.Amount);
        Assert.Equal(104m, new InventoryLedger(reloaded).OnHand(k.ItemId, k.MainGodownId, AsOf(reloaded)));
    }

    // ================================================================= 3 — the tax, which is the filed document

    /// <summary>
    /// 🔴🔴 <b>THE GST ON A CREDIT NOTE REVERSES — THE SIDE FLIPS, THE HEAD DOES NOT.</b>
    ///
    /// <para>4 Widgets @ ₹150 = ₹600 taxable at 18% intra-state ⇒ CGST ₹54 and SGST ₹54. On the SALES invoice
    /// those are credits to <b>Output</b> CGST/SGST. On the sales-return credit note they must be <b>DEBITS to the
    /// very same Output ledgers</b> — we are un-charging tax we charged. The party leg carries 600 + 54 + 54 = ₹708.</para>
    ///
    /// <para><b>The failure this exists to catch is not a display bug.</b> If the note flipped the DIRECTION instead
    /// of the side it would post against <i>Input</i> CGST — claiming input credit on a sale, in a document that
    /// goes into a filed return. So the test asserts the ledger by NAME as well as the side; asserting the side
    /// alone would pass on exactly that mistake.</para>
    /// </summary>
    [Fact]
    public void A_gst_credit_note_debits_the_OUTPUT_tax_ledgers_it_is_reversing()
    {
        var k = NewKit("Credit Note Gst Co", withGst: true);
        var c = k.Vm.Company!;

        k.Vm.OpenVoucher(VoucherBaseType.CreditNote);
        var entry = k.Vm.VoucherEntry!;
        k.Vm.ChangeMode();
        entry.SelectedParty = entry.Parties.Single(p => p.Ledger?.Id == k.CustomerId);
        entry.SelectedStockLedger = entry.StockLedgers.Single(l => l.Id == k.SalesReturnsId);
        FillItemLine(entry, k, 4m, "150.00");
        Assert.True(entry.Accept(), entry.Message);

        var posted = PostedNote(k, VoucherBaseType.CreditNote);
        var outputCgst = c.FindLedgerByName(GstService.TaxLedgerName(GstTaxHead.Central, GstTaxDirection.Output))!;
        var outputSgst = c.FindLedgerByName(GstService.TaxLedgerName(GstTaxHead.State, GstTaxDirection.Output))!;

        var cgstLeg = posted.Lines.Single(l => l.LedgerId == outputCgst.Id);
        var sgstLeg = posted.Lines.Single(l => l.LedgerId == outputSgst.Id);
        Assert.Equal(DrCr.Debit, cgstLeg.Side);          // reversed — a sale credits these
        Assert.Equal(DrCr.Debit, sgstLeg.Side);
        Assert.Equal(54m, cgstLeg.Amount.Amount);
        Assert.Equal(54m, sgstLeg.Amount.Amount);

        // No INPUT ledger is touched: the head did not flip with the side.
        var inputCgst = c.FindLedgerByName(GstService.TaxLedgerName(GstTaxHead.Central, GstTaxDirection.Input));
        Assert.DoesNotContain(posted.Lines, l => inputCgst is not null && l.LedgerId == inputCgst.Id);

        // The customer is credited the full note value, and the voucher balances.
        var partyLeg = posted.Lines.Single(l => l.LedgerId == k.CustomerId);
        Assert.Equal(DrCr.Credit, partyLeg.Side);
        Assert.Equal(708m, partyLeg.Amount.Amount);
        Assert.Equal(posted.TotalDebit.Amount, posted.TotalCredit.Amount);

        // And the stock came back with it.
        Assert.Equal(104m, OnHand(k));
    }

    /// <summary>
    /// The Debit Note mirror, and the other half of the head-does-not-flip rule: a purchase return <b>CREDITS
    /// Input</b> CGST/SGST — giving back credit we claimed — and never touches an Output ledger.
    /// </summary>
    [Fact]
    public void A_gst_debit_note_credits_the_INPUT_tax_ledgers_it_is_reversing()
    {
        var k = NewKit("Debit Note Gst Co", withGst: true);
        var c = k.Vm.Company!;

        k.Vm.OpenVoucher(VoucherBaseType.DebitNote);
        var entry = k.Vm.VoucherEntry!;
        k.Vm.ChangeMode();
        entry.SelectedParty = entry.Parties.Single(p => p.Ledger?.Id == k.SupplierId);
        entry.SelectedStockLedger = entry.StockLedgers.Single(l => l.Id == k.PurchaseReturnsId);
        FillItemLine(entry, k, 4m, "150.00");
        Assert.True(entry.Accept(), entry.Message);

        var posted = PostedNote(k, VoucherBaseType.DebitNote);
        var inputCgst = c.FindLedgerByName(GstService.TaxLedgerName(GstTaxHead.Central, GstTaxDirection.Input))!;
        var inputSgst = c.FindLedgerByName(GstService.TaxLedgerName(GstTaxHead.State, GstTaxDirection.Input))!;

        Assert.Equal(DrCr.Credit, posted.Lines.Single(l => l.LedgerId == inputCgst.Id).Side);
        Assert.Equal(DrCr.Credit, posted.Lines.Single(l => l.LedgerId == inputSgst.Id).Side);

        var outputCgst = c.FindLedgerByName(GstService.TaxLedgerName(GstTaxHead.Central, GstTaxDirection.Output));
        Assert.DoesNotContain(posted.Lines, l => outputCgst is not null && l.LedgerId == outputCgst.Id);

        var partyLeg = posted.Lines.Single(l => l.LedgerId == k.SupplierId);
        Assert.Equal(DrCr.Debit, partyLeg.Side);
        Assert.Equal(708m, partyLeg.Amount.Amount);
        Assert.Equal(96m, OnHand(k));
    }

    // ================================================================= 4 — what did NOT widen with it

    /// <summary>
    /// 🔴 <b>THE INVOICE-ONLY FEATURES STAYED BEHIND, and this is not pedantry — two of them read
    /// <c>!IsPurchaseInvoice</c>, which silently becomes TRUE on both notes the moment the carrier set widens.</b>
    ///
    /// <para>Price levels would have re-priced a return off a current price list instead of at what was invoiced,
    /// and the TCS band would have collected §206C tax on a sales RETURN. Both now ask for the Sales base type by
    /// name. The Tracking Number column is excluded for its own stated reason — a tracking number billed twice
    /// nets to zero in <c>BillsPending</c>, a reversal reading as a reconciliation.</para>
    /// </summary>
    [Fact]
    public void The_invoice_only_bands_do_not_open_on_a_return_note()
    {
        var k = NewKit("Return Note Bands Co");
        var c = k.Vm.Company!;
        c.EnableMultiplePriceLevels = true;
        c.UseTrackingNumbers = true;

        k.Vm.OpenVoucher(VoucherBaseType.CreditNote);
        var note = k.Vm.VoucherEntry!;
        k.Vm.ChangeMode();
        Assert.True(note.IsItemInvoice);

        Assert.False(note.ShowPriceLevelSelector);
        Assert.False(note.IsTcsSalesInvoice);
        Assert.False(note.ShowItemTrackingNumber);
        Assert.False(note.CanTrackAdditionalCosts);
        Assert.False(note.ShowReferenceCapture);

        // The control: the same flags on a real Sales invoice still open the price-level band, so the assertions
        // above are proving a base-type rule rather than a company flag that never took effect.
        k.Vm.OpenVoucher(VoucherBaseType.Sales);
        var sale = k.Vm.VoucherEntry!;
        k.Vm.ChangeMode();
        Assert.True(sale.IsItemInvoice);
        Assert.True(sale.ShowPriceLevelSelector);
        Assert.True(sale.ShowItemTrackingNumber);
    }

    /// <summary>
    /// 🔴 <b>THE INVERSE IS REFUSED BY NAME, IN THE SAME SLICE THAT MADE IT REACHABLE.</b>
    ///
    /// <para>Altering a posted return-note item invoice is a path nothing has exercised, and one hazard is visible
    /// without running it: the §34 link that carries a note into GSTR-1 Table 9B is registered on ACCEPT and has no
    /// alteration inverse, so re-accepting could leave it pointing at a superseded voucher. Before this slice the
    /// case was unreachable — a note could not carry stock — so it fell through to "alterable" harmlessly. It
    /// would not have fallen through harmlessly today.</para>
    ///
    /// <para>The refusal is asserted as a MESSAGE the operator can act on ("cancel the note and enter a fresh
    /// one"), not merely as a false flag: a dead end with no sentence is how an operator concludes the product is
    /// broken.</para>
    /// </summary>
    [Fact]
    public void Altering_a_posted_return_note_item_invoice_is_refused_with_a_reason()
    {
        var k = NewKit("Return Note Alter Co");

        k.Vm.OpenVoucher(VoucherBaseType.CreditNote);
        var entry = k.Vm.VoucherEntry!;
        k.Vm.ChangeMode();
        entry.SelectedParty = entry.Parties.Single(p => p.Ledger?.Id == k.CustomerId);
        entry.SelectedStockLedger = entry.StockLedgers.Single(l => l.Id == k.SalesReturnsId);
        FillItemLine(entry, k, 4m, "150.00");
        Assert.True(entry.Accept());

        var posted = PostedNote(k, VoucherBaseType.CreditNote);
        var refusal = VoucherAlterationEligibility.RefusalFor(k.Vm.Company!, posted.Id);

        Assert.NotNull(refusal);
        Assert.Contains("CREDIT / DEBIT NOTE", refusal!);
        Assert.Contains("later slice", refusal);
    }

    /// <summary>
    /// Ctrl+H on a note cycles <b>As Voucher ⟷ Item Invoice</b> only. The Accounting (service) Invoice mode is
    /// NOT offered: a service credit note has no stock leg to derive and its §194J/RCM detection is wired to the
    /// Particulars grid, so admitting it here would repeat the defect that gated the purchase arm off once before.
    /// </summary>
    [Fact]
    public void The_change_mode_cycle_on_a_note_is_two_way_and_never_offers_the_accounting_invoice()
    {
        var k = NewKit("Return Note Cycle Co");
        k.Vm.OpenVoucher(VoucherBaseType.CreditNote);
        var entry = k.Vm.VoucherEntry!;

        Assert.False(entry.CanBeAccountingInvoice);
        Assert.False(entry.IsItemInvoice);

        k.Vm.ChangeMode();
        Assert.True(entry.IsItemInvoice);
        k.Vm.ChangeMode();
        Assert.False(entry.IsItemInvoice);       // straight back, never through Accounting Invoice
        Assert.False(entry.IsAccountingInvoice);
    }
}
