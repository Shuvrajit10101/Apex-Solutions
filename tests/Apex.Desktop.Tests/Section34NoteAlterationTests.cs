using System;
using System.IO;
using System.Linq;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Desktop.Views;
using Xunit;
using DomainLedger = Apex.Ledger.Domain.Ledger;
using Path = System.IO.Path;

namespace Apex.Desktop.Tests;

/// <summary>
/// 🔴 <b>CENSUS 4.7 / 4.8 — THE RETURN-NOTE ALTERATION ARM, AND THE §34 LINK'S ALTERATION INVERSE IT WAITED
/// ON.</b>
///
/// <para><b>What was refused, and the precondition it wrote for itself.</b>
/// <c>VoucherAlterationEligibility.EntryModeRefusal</c> refused every return note entered as an item invoice,
/// and said exactly what would lift it: <i>"Lifting this arm needs the §34 link's alteration inverse first, and
/// a test that a re-accepted note still reaches Table 9B exactly once."</i> This file is that test.</para>
///
/// <para>🔴 <b>WHY THE DOUBLE LINK WAS NEVER A DISPLAY BUG.</b> <c>Gstr1.BuildTable9B</c> walks
/// <c>company.CreditDebitNoteLinks</c> and folds one row AND one signed CGST/SGST/IGST contribution PER LINK.
/// <c>RegisterSection34Link</c> mints a fresh <see cref="Guid"/> on every accept, so a re-accept would not have
/// duplicated a line — it would have DOUBLED the note's reversal in a filed return. Every assertion below is
/// therefore made on the return read back off the RELOADED book, never on the in-memory aggregate the screen
/// is holding.</para>
///
/// <para><b>Vendor route (R7, ruling 14).</b> The note is entered and re-opened on the vendor's own documented
/// route — <i>"Press Ctrl+H (Change Mode) &gt; select Item Invoice"</i>
/// (help.tallysolutions.com, recording a sales return using a credit note under GST), and the drill/alter
/// keystroke is the shell's Ctrl+Enter on the Day Book row.</para>
/// </summary>
public sealed class Section34NoteAlterationTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public Section34NoteAlterationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexS34Alter_" + Guid.NewGuid().ToString("N"));
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

    private sealed class Kit
    {
        public required MainWindowViewModel Vm { get; init; }
        public required string CompanyName { get; init; }
        public required Guid ItemId { get; init; }
        public required Guid GodownId { get; init; }
        public required Guid SalesId { get; init; }
        public required Guid SalesReturnsId { get; init; }
        public required Guid PurchasesId { get; init; }
        public required Guid PurchaseReturnsId { get; init; }
        public required Guid CustomerId { get; init; }
        public required Guid SupplierId { get; init; }
    }

    private static DomainLedger AddLedger(Company c, string name, string groupName)
    {
        var l = new DomainLedger(Guid.NewGuid(), name, c.FindGroupByName(groupName)!.Id,
            Money.Zero, openingIsDebit: false);
        c.AddLedger(l);
        return l;
    }

    /// <summary>A GST trading company with opening stock and the four value ledgers a return needs.</summary>
    private Kit NewKit(string companyName)
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

        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = "27",
            Gstin = "27AAPFU0939F1ZV",
            RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = c.FinancialYearStart,
            Periodicity = GstReturnPeriodicity.Monthly,
        });
        item.Gst = new StockItemGstDetails
        {
            HsnSac = "847130", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800,
        };

        var kit = new Kit
        {
            Vm = vm,
            CompanyName = companyName,
            ItemId = item.Id,
            GodownId = c.MainLocation!.Id,
            SalesId = AddLedger(c, "Sales", "Sales Accounts").Id,
            SalesReturnsId = AddLedger(c, "Sales Returns", "Sales Accounts").Id,
            PurchasesId = AddLedger(c, "Purchases", "Purchase Accounts").Id,
            PurchaseReturnsId = AddLedger(c, "Purchase Returns", "Purchase Accounts").Id,
            CustomerId = AddLedger(c, "Beta Buyers", "Sundry Debtors").Id,
            SupplierId = AddLedger(c, "Acme Supplies", "Sundry Creditors").Id,
        };
        _storage.Save(c);
        return kit;
    }

    private static void FillItemLine(VoucherEntryViewModel entry, Kit k, decimal qty, string rate)
    {
        var line = entry.InventoryLines[0];
        line.SelectedItem = entry.StockItems.Single(i => i.Id == k.ItemId);
        line.SelectedGodown = entry.Godowns.Single(g => g.Id == k.GodownId);
        line.QuantityText = qty.ToString(System.Globalization.CultureInfo.InvariantCulture);
        line.RateText = rate;
    }

    /// <summary>Posts the ORIGINAL inter-party invoice the note will adjust — an item invoice, so the §34 link
    /// points at a document that itself carries stock and tax.</summary>
    private static Voucher PostOriginalInvoice(Kit k, VoucherBaseType nature, decimal qty, string rate)
    {
        k.Vm.OpenVoucher(nature);
        var entry = k.Vm.VoucherEntry!;
        k.Vm.ChangeMode();
        Assert.True(entry.IsItemInvoice);

        entry.SelectedParty = entry.Parties.Single(p =>
            p.Ledger?.Id == (nature == VoucherBaseType.Sales ? k.CustomerId : k.SupplierId));
        entry.SelectedStockLedger = entry.StockLedgers.Single(l =>
            l.Id == (nature == VoucherBaseType.Sales ? k.SalesId : k.PurchasesId));
        FillItemLine(entry, k, qty, rate);
        Assert.True(entry.Accept(), entry.Message);

        var typeId = k.Vm.Company!.VoucherTypes.Single(t => t.BaseType == nature && t.IsActive).Id;
        return k.Vm.Company!.Vouchers.Last(v => v.TypeId == typeId);
    }

    /// <summary>
    /// Posts a §34 RETURN NOTE as an ITEM INVOICE, linked to <paramref name="original"/>. Drives the real
    /// screen — Alt+F6/Alt+F5's view model, Ctrl+H's Change Mode, and the §34 panel's own controls.
    /// </summary>
    private static Voucher PostSection34Note(
        Kit k, VoucherBaseType nature, Voucher original, decimal qty, string rate, string reason = "01 Sales return")
    {
        k.Vm.OpenVoucher(nature);
        var entry = k.Vm.VoucherEntry!;
        k.Vm.ChangeMode();
        Assert.True(entry.IsItemInvoice);

        entry.SelectedParty = entry.Parties.Single(p =>
            p.Ledger?.Id == (nature == VoucherBaseType.CreditNote ? k.CustomerId : k.SupplierId));
        entry.SelectedStockLedger = entry.StockLedgers.Single(l =>
            l.Id == (nature == VoucherBaseType.CreditNote ? k.SalesReturnsId : k.PurchaseReturnsId));
        FillItemLine(entry, k, qty, rate);

        Assert.True(entry.CanBeSection34Note);
        entry.IsSection34Note = true;
        Assert.True(entry.ShowSection34Details);
        entry.SelectedCdnOriginalInvoice =
            entry.CdnOriginalInvoices.Single(o => o.Invoice is { } v && v.Id == original.Id);
        entry.SelectedCdnReasonCode = reason;

        Assert.True(entry.Accept(), entry.Message);

        var typeId = k.Vm.Company!.VoucherTypes.Single(t => t.BaseType == nature && t.IsActive).Id;
        return k.Vm.Company!.Vouchers.Last(v => v.TypeId == typeId);
    }

    private Company Reload(Kit k) =>
        _storage.Load(_storage.ListCompanies().Single(e => e.Name == k.CompanyName));

    private static Gstr1 Return(Company c) =>
        Gstr1.Build(c, c.FinancialYearStart, c.FinancialYearStart.AddYears(1).AddDays(-1));

    private static void Pump(MainWindow w)
    {
        w.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        w.UpdateLayout();
    }

    // ================================================================= 1 — the arm is lifted

    /// <summary>
    /// 🔴 <b>THE CENTRAL ROW.</b> A posted §34 sales-return credit note entered as an ITEM INVOICE is no longer
    /// refused: it opens, it re-opens with its §34 panel holding what the BOOK holds, and it re-accepts.
    ///
    /// <para><b>Mutation:</b> restore the struck <c>EntryModeRefusal</c> return-note arm and this reddens on
    /// <c>Assert.Null(open.Refusal)</c>; restore the un-narrowed <c>OffLineSideEffectRefusal</c> CDN arm and it
    /// reddens on the same line with the double-link sentence.</para>
    /// </summary>
    [Fact]
    public void A_posted_section34_return_note_item_invoice_opens_for_alteration_with_its_link_on_the_panel()
    {
        var k = NewKit("S34 Open Co");
        var sale = PostOriginalInvoice(k, VoucherBaseType.Sales, 10m, "150.00");
        var note = PostSection34Note(k, VoucherBaseType.CreditNote, sale, 4m, "150.00");

        Assert.Null(VoucherAlterationEligibility.RefusalFor(k.Vm.Company!, note.Id));

        var open = VoucherEntryViewModel.ForAlter(k.Vm.Company!, note.Id, _storage, () => { }, () => { });
        Assert.Null(open.Refusal);
        var entry = open.Entry!;

        Assert.True(entry.IsAltering);
        Assert.True(entry.IsItemInvoice);                       // the posted shape decided the grid

        // 🔴 The §34 panel holds the POSTED link, not a blank form. Without RehydrateSection34From the operator
        // would be shown a note that is not the one on the book, and the inverse would write that blank.
        Assert.True(entry.IsSection34Note);
        Assert.True(entry.ShowSection34Details);
        Assert.Equal(sale.Id, entry.SelectedCdnOriginalInvoice!.Invoice!.Id);
        Assert.Equal("01 Sales return", entry.SelectedCdnReasonCode);
        Assert.True(entry.CdnIs9BTarget);

        // The override is REPRODUCED, never granted: this note is inside its §34(2) cut-off, so it is OFF.
        Assert.False(entry.CdnOverrideTimeLimit);
    }

    /// <summary>
    /// 🔴 <b>REACHABLE BY A USER.</b> The arm is lifted on the predicate the SHELL asks, so the real
    /// <c>Ctrl+Enter</c> on the Day Book row opens the note — not merely a direct <c>ForAlter</c> call.
    /// </summary>
    [AvaloniaFact]
    public void Ctrl_Enter_on_the_Day_Book_row_opens_the_posted_section34_note_for_alteration()
    {
        var k = NewKit("S34 Reach Co");
        var sale = PostOriginalInvoice(k, VoucherBaseType.Sales, 10m, "150.00");
        var note = PostSection34Note(k, VoucherBaseType.CreditNote, sale, 4m, "150.00");

        var window = new MainWindow { DataContext = k.Vm, Width = 1440, Height = 900 };
        window.Show();
        Pump(window);

        k.Vm.OpenReport(ReportKind.DayBook);
        Pump(window);
        k.Vm.Reports!.SelectedRow = k.Vm.Reports!.Rows.First(r => r.DrillVoucherId == note.Id);
        Pump(window);

        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.Control);
        Pump(window);

        Assert.Equal(Screen.VoucherEntry, k.Vm.CurrentScreen);
        var entry = k.Vm.VoucherEntry!;
        Assert.True(entry.IsAltering);
        Assert.Equal(note.Id, entry.AlteringVoucherId);
        Assert.True(entry.IsItemInvoice);
        Assert.True(entry.IsSection34Note);

        window.Close();
    }

    // ================================================================= 2 — the filed return still nets

    /// <summary>
    /// 🔴🔴 <b>THE ROW THIS SLICE EXISTS FOR.</b> Alter the note's quantity and the FILED RETURN still nets
    /// correctly: ONE link, ONE Table 9B row, and the return's output tax equal to the invoice's tax less the
    /// AMENDED note's tax — all read off the RELOADED book.
    ///
    /// <para>Figures: sale 10 @ ₹150 = ₹1,500 @ 18% intra ⇒ CGST ₹135 / SGST ₹135. Note 4 @ ₹150 = ₹600 ⇒
    /// ₹54 / ₹54, so the return nets to ₹81 / ₹81. Amend the note to 3 @ ₹150 = ₹450 ⇒ ₹40.50 / ₹40.50, so the
    /// return must net to ₹94.50 / ₹94.50. A second link would net it to ₹40.50 — half the true figure — which
    /// is exactly the shape the refusal named.</para>
    ///
    /// <para><b>Mutation:</b> change <c>ReplaceSection34Link</c> to call <c>RegisterSection34Link</c> instead
    /// (i.e. mint a fresh Guid and keep the old link) and this reddens on the link count and on TotalCgst.</para>
    /// </summary>
    [Fact]
    public void Altering_a_section34_credit_note_leaves_ONE_link_and_a_return_that_nets_correctly()
    {
        var k = NewKit("S34 Net Co");
        var sale = PostOriginalInvoice(k, VoucherBaseType.Sales, 10m, "150.00");
        var note = PostSection34Note(k, VoucherBaseType.CreditNote, sale, 4m, "150.00");

        var postedLinkId = k.Vm.Company!.CreditDebitNoteLinks.Single(l => l.CdnVoucherId == note.Id).Id;

        var before = Return(Reload(k));
        Assert.Equal(81m, before.TotalCgst.Amount);
        Assert.Equal(81m, before.TotalSgst.Amount);
        Assert.Single(before.Table9B);

        // ---- alter: 4 → 3 ----
        var open = VoucherEntryViewModel.ForAlter(k.Vm.Company!, note.Id, _storage, () => { }, () => { });
        Assert.Null(open.Refusal);
        var entry = open.Entry!;
        entry.InventoryLines[0].QuantityText = "3";
        Assert.True(entry.AcceptAlteration(), entry.Message);

        // ---- everything below is read off the RELOADED book ----
        var reloaded = Reload(k);

        // 🔴 THE FILED FIGURE FIRST, deliberately: a second link nets this to ₹54.00 (135 − 40.50 × 2), so the
        // headline evidence for this row is a number in the return rather than a collection count.
        var after = Return(reloaded);
        Assert.Equal(94.50m, after.TotalCgst.Amount);
        Assert.Equal(94.50m, after.TotalSgst.Amount);

        var row = Assert.Single(after.Table9B);
        Assert.Equal(-450m, row.TaxableValue.Amount);            // signed: a credit note reduces
        Assert.Equal(-40.50m, row.Cgst.Amount);
        Assert.Equal(-40.50m, row.Sgst.Amount);

        var links = reloaded.CreditDebitNoteLinks.Where(l => l.CdnVoucherId == note.Id).ToList();
        var link = Assert.Single(links);
        Assert.Equal(postedLinkId, link.Id);                     // carried, not re-minted
        Assert.Equal(sale.Id, link.OriginalInvoiceVoucherId);
        Assert.Equal("01 Sales return", link.ReasonCode);
        Assert.Equal(CdnType.Credit, link.CdnType);

        // And the amended note on the book agrees with what the return declares.
        var amended = reloaded.FindVoucher(note.Id)!;
        Assert.Equal(450m, amended.InventoryLinesValue.Amount);
        Assert.Equal(3m, amended.InventoryLines.Single().Quantity);
        var outputCgst = reloaded.FindLedgerByName(
            GstService.TaxLedgerName(GstTaxHead.Central, GstTaxDirection.Output))!;
        var cgstLeg = amended.Lines.Single(l => l.LedgerId == outputCgst.Id);
        Assert.Equal(DrCr.Debit, cgstLeg.Side);                  // the side did not flip with the amendment
        Assert.Equal(40.50m, cgstLeg.Amount.Amount);
    }

    /// <summary>
    /// 🔴 <b>THE PURCHASE MIRROR, ASSERTED SEPARATELY AND NEVER INFERRED FROM THE CREDIT-NOTE CASE.</b> A §34
    /// DEBIT note is signed the OTHER way in Table 9B (it increases the declared tax) and credits INPUT tax, so
    /// a "by symmetry" argument is exactly what would get it wrong.
    /// </summary>
    [Fact]
    public void Altering_a_section34_debit_note_repoints_its_own_link_and_keeps_the_debit_sign()
    {
        var k = NewKit("S34 Debit Co");
        var purchase = PostOriginalInvoice(k, VoucherBaseType.Purchase, 10m, "150.00");
        var note = PostSection34Note(
            k, VoucherBaseType.DebitNote, purchase, 4m, "150.00", reason: "04 Correction in invoice");

        var postedLinkId = k.Vm.Company!.CreditDebitNoteLinks.Single(l => l.CdnVoucherId == note.Id).Id;

        var open = VoucherEntryViewModel.ForAlter(k.Vm.Company!, note.Id, _storage, () => { }, () => { });
        Assert.Null(open.Refusal);
        var entry = open.Entry!;
        Assert.True(entry.IsSection34Note);
        entry.InventoryLines[0].QuantityText = "6";
        Assert.True(entry.AcceptAlteration(), entry.Message);

        var reloaded = Reload(k);
        var link = Assert.Single(reloaded.CreditDebitNoteLinks.Where(l => l.CdnVoucherId == note.Id));
        Assert.Equal(postedLinkId, link.Id);
        Assert.Equal(CdnType.Debit, link.CdnType);
        Assert.Equal(purchase.Id, link.OriginalInvoiceVoucherId);
        Assert.Equal("04 Correction in invoice", link.ReasonCode);

        // 6 @ 150 = 900 ⇒ CGST/SGST 81 each, and the row is POSITIVE — a debit note increases the declaration.
        var row = Assert.Single(Return(reloaded).Table9B);
        Assert.Equal(CdnType.Debit, row.NoteType);
        Assert.Equal(900m, row.TaxableValue.Amount);
        Assert.Equal(81m, row.Cgst.Amount);

        // The head did not flip with the amendment: a purchase return still CREDITS Input tax.
        var inputCgst = reloaded.FindLedgerByName(
            GstService.TaxLedgerName(GstTaxHead.Central, GstTaxDirection.Input))!;
        var amended = reloaded.FindVoucher(note.Id)!;
        Assert.Equal(DrCr.Credit, amended.Lines.Single(l => l.LedgerId == inputCgst.Id).Side);
        var outputCgst = reloaded.FindLedgerByName(
            GstService.TaxLedgerName(GstTaxHead.Central, GstTaxDirection.Output));
        Assert.DoesNotContain(amended.Lines, l => outputCgst is not null && l.LedgerId == outputCgst.Id);
    }

    // ================================================================= 3 — the audit log

    /// <summary>
    /// 🔴 <b>THE EDIT LOG, READ BACK OFF THE RELOADED BOOK.</b> A past wave put a FALSIFIED audit log on main,
    /// so the before-state is proved to hold the PRE-alteration figure and the book to hold the POST one — the
    /// two halves the log's own design splits between the entry and the voucher
    /// (<see cref="VoucherEditLogEntry"/>: "the after-state of an Alter is on the book").
    /// </summary>
    [Fact]
    public void The_edit_log_holds_the_pre_alteration_note_and_the_book_holds_the_amended_one()
    {
        var k = NewKit("S34 Audit Co");
        var sale = PostOriginalInvoice(k, VoucherBaseType.Sales, 10m, "150.00");
        var note = PostSection34Note(k, VoucherBaseType.CreditNote, sale, 4m, "150.00");

        var open = VoucherEntryViewModel.ForAlter(k.Vm.Company!, note.Id, _storage, () => { }, () => { });
        var entry = open.Entry!;
        entry.InventoryLines[0].QuantityText = "3";
        Assert.True(entry.AcceptAlteration(), entry.Message);

        var reloaded = Reload(k);

        var altered = Assert.Single(reloaded.VoucherEditLog.Where(
            e => e.VoucherId == note.Id && e.Verb == VoucherEditVerb.Alter));

        // BEFORE: the 4-unit, ₹600 note — the state that is no longer anywhere else.
        Assert.Contains("\"Quantity\":4", altered.BeforeSnapshot);
        Assert.Contains("600", altered.BeforeSnapshot);
        Assert.DoesNotContain("\"Quantity\":3", altered.BeforeSnapshot);

        // AFTER: on the book, which is where the log's design puts it.
        var amended = reloaded.FindVoucher(note.Id)!;
        Assert.Equal(3m, amended.InventoryLines.Single().Quantity);
        Assert.Equal(450m, amended.InventoryLinesValue.Amount);
    }

    // ================================================================= 4 — what is STILL refused

    /// <summary>
    /// 🔴 <b>CLEARING THE §34 TICK ON AN ALTERATION IS REFUSED BY NAME</b> — it would withdraw a note from a
    /// filed return with nothing recording the withdrawal. The refusal is a SENTENCE, and the book is proved
    /// untouched afterwards: the refused alteration must leave neither a half-applied replacement nor a
    /// fictitious edit-log entry.
    /// </summary>
    [Fact]
    public void Clearing_the_section34_tick_during_an_alteration_is_refused_and_changes_nothing()
    {
        var k = NewKit("S34 Clear Co");
        var sale = PostOriginalInvoice(k, VoucherBaseType.Sales, 10m, "150.00");
        var note = PostSection34Note(k, VoucherBaseType.CreditNote, sale, 4m, "150.00");

        var beforeReturn = Return(Reload(k));
        var beforeLogCount = Reload(k).VoucherEditLog.Count;

        var open = VoucherEntryViewModel.ForAlter(k.Vm.Company!, note.Id, _storage, () => { }, () => { });
        var entry = open.Entry!;
        entry.InventoryLines[0].QuantityText = "3";
        entry.IsSection34Note = false;                            // the operator clears the tick

        Assert.False(entry.AcceptAlteration());
        Assert.Contains("withdraw it from a filed return", entry.Message);

        var reloaded = Reload(k);
        Assert.Single(reloaded.CreditDebitNoteLinks.Where(l => l.CdnVoucherId == note.Id));
        Assert.Equal(4m, reloaded.FindVoucher(note.Id)!.InventoryLines.Single().Quantity);
        Assert.Equal(beforeReturn.TotalCgst.Amount, Return(reloaded).TotalCgst.Amount);
        Assert.Equal(beforeLogCount, reloaded.VoucherEditLog.Count);   // no fictitious Alter entry
    }

    /// <summary>
    /// 🔴 <b>TICKING §34 ON DURING AN ALTERATION IS REFUSED BY NAME</b> — it would create a statutory
    /// declaration from an alteration screen. Before this slice the same keystroke was a SILENT no-op: the
    /// alteration path called nothing at all, so the tick posted and no link appeared.
    /// </summary>
    [Fact]
    public void Ticking_section34_on_during_an_alteration_is_refused_and_creates_no_link()
    {
        var k = NewKit("S34 Add Co");
        PostOriginalInvoice(k, VoucherBaseType.Sales, 10m, "150.00");

        // A plain (non-§34) sales-return note — no link, so the arm it meets is the ADD one.
        k.Vm.OpenVoucher(VoucherBaseType.CreditNote);
        var fresh = k.Vm.VoucherEntry!;
        k.Vm.ChangeMode();
        fresh.SelectedParty = fresh.Parties.Single(p => p.Ledger?.Id == k.CustomerId);
        fresh.SelectedStockLedger = fresh.StockLedgers.Single(l => l.Id == k.SalesReturnsId);
        FillItemLine(fresh, k, 4m, "150.00");
        Assert.True(fresh.Accept(), fresh.Message);

        var typeId = k.Vm.Company!.VoucherTypes
            .Single(t => t.BaseType == VoucherBaseType.CreditNote && t.IsActive).Id;
        var note = k.Vm.Company!.Vouchers.Last(v => v.TypeId == typeId);
        Assert.Empty(k.Vm.Company!.CreditDebitNoteLinks);

        var open = VoucherEntryViewModel.ForAlter(k.Vm.Company!, note.Id, _storage, () => { }, () => { });
        Assert.Null(open.Refusal);                                 // a note with no link alters freely…
        var entry = open.Entry!;
        Assert.False(entry.IsSection34Note);                       // …and rehydrates with the panel OFF

        entry.IsSection34Note = true;                              // …but may not acquire a declaration here
        entry.SelectedCdnOriginalInvoice = entry.CdnOriginalInvoices.First(o => o.Invoice is not null);
        entry.SelectedCdnReasonCode = "01 Sales return";

        Assert.False(entry.AcceptAlteration());
        Assert.Contains("posted WITHOUT §34 GST details", entry.Message);
        Assert.Empty(Reload(k).CreditDebitNoteLinks);
    }

    /// <summary>
    /// 🔴 <b>THE NARROWING IS EXACTLY AS WIDE AS THE INVERSE.</b> A §34 note keyed on the PLAIN Dr/Cr grid is
    /// still refused by the double-link sentence: only the item-invoice door has been driven end-to-end through
    /// <c>CommitAlteration</c>'s inverse, and the plain-grid door's own re-derivation chain has not been proved
    /// over a note's hand-keyed Output-tax legs.
    /// </summary>
    [Fact]
    public void A_plain_grid_section34_note_is_still_refused_by_the_double_link_sentence()
    {
        var k = NewKit("S34 Plain Co");
        var sale = PostOriginalInvoice(k, VoucherBaseType.Sales, 10m, "150.00");

        // The plain Dr/Cr grid — no Ctrl+H, so the note carries no inventory lines.
        k.Vm.OpenVoucher(VoucherBaseType.CreditNote);
        var entry = k.Vm.VoucherEntry!;
        Assert.False(entry.IsItemInvoice);
        entry.Lines[0].SelectedLedger = entry.Ledgers.Single(l => l.Id == k.SalesReturnsId);
        entry.Lines[0].AmountText = "600.00";
        entry.Lines[1].SelectedLedger = entry.Ledgers.Single(l => l.Id == k.CustomerId);
        entry.Lines[1].AmountText = "600.00";
        entry.IsSection34Note = true;
        entry.SelectedCdnOriginalInvoice =
            entry.CdnOriginalInvoices.Single(o => o.Invoice is { } v && v.Id == sale.Id);
        entry.SelectedCdnReasonCode = "01 Sales return";
        Assert.True(entry.Accept(), entry.Message);

        var typeId = k.Vm.Company!.VoucherTypes
            .Single(t => t.BaseType == VoucherBaseType.CreditNote && t.IsActive).Id;
        var note = k.Vm.Company!.Vouchers.Last(v => v.TypeId == typeId);
        Assert.False(note.HasInventoryLines);

        var refusal = VoucherAlterationEligibility.RefusalFor(k.Vm.Company!, note.Id);
        Assert.NotNull(refusal);
        Assert.Contains("two links", refusal!);
    }
}
