using System;
using System.IO;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Xunit;
using DomainLedger = Apex.Ledger.Domain.Ledger;
using Path = System.IO.Path;

namespace Apex.Desktop.Tests;

/// <summary>
/// Census <b>5.7</b>, T2-174 review (wave 13, IV-388). The §34 original-invoice picker stopped offering an Optional
/// sale to a NEW note. A note posted while its original was Regular must still keep that link when the original is
/// later marked Optional (Ctrl+L in alteration) and the note is then re-opened and re-accepted: without the
/// rehydration in <c>RehydrateSection34From</c> the panel fell through to the consolidated branch and the re-accept
/// rewrote the posted link to a consolidated reference (<c>OriginalInvoiceVoucherId</c> null), which regularising
/// the original afterwards did not undo.
/// </summary>
public sealed class OptionalOriginalNoteAlterationTests : IDisposable
{
    private const string CompanyName = "Optional Original Note Co";
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "ApexOptOrigNote_" + Guid.NewGuid().ToString("N"));
    private readonly CompanyStorage _storage;

    public OptionalOriginalNoteAlterationTests() => _storage = new CompanyStorage(_tempDir);

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

    private static DomainLedger AddLedger(Company c, string name, string groupName)
    {
        var l = new DomainLedger(Guid.NewGuid(), name, c.FindGroupByName(groupName)!.Id, Money.Zero, openingIsDebit: false);
        c.AddLedger(l);
        return l;
    }

    private (Company Book, CompanyStorage Store) Reload()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        var fresh = new CompanyStorage(_tempDir);
        return (fresh.Load(fresh.ListCompanies().Single(e => e.Name == CompanyName)), fresh);
    }

    /// <summary>
    /// A Regular sale and a §34 credit note linked to it, posted through the voucher-entry screen, then the sale is
    /// marked Optional (the engine verb Ctrl+L calls) and the book saved. Returns the two voucher ids.
    /// </summary>
    private (Guid SaleId, Guid NoteId) SeedNoteWhoseOriginalIsLaterMadeOptional()
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = CompanyName;
        vm.CreateCompany();
        var c = vm.Company!;
        var masters = new InventoryService(c);
        var item = masters.CreateStockItem("Widget", masters.CreateStockGroup("Goods").Id,
            masters.CreateSimpleUnit("Nos", "Numbers").Id);
        masters.AddOpeningBalance(item.Id, c.MainLocation!.Id, 100m, Money.FromRupees(100m));
        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = "27", Gstin = "27AAPFU0939F1ZV", RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = c.FinancialYearStart, Periodicity = GstReturnPeriodicity.Monthly,
        });
        item.Gst = new StockItemGstDetails { HsnSac = "847130", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800 };
        var sales = AddLedger(c, "Sales", "Sales Accounts");
        var salesReturns = AddLedger(c, "Sales Returns", "Sales Accounts");
        var customer = AddLedger(c, "Beta Buyers", "Sundry Debtors");
        _storage.Save(c);

        void Fill(VoucherEntryViewModel e, Guid stockLedger, string qty)
        {
            e.SelectedParty = e.Parties.Single(p => p.Ledger?.Id == customer.Id);
            e.SelectedStockLedger = e.StockLedgers.Single(l => l.Id == stockLedger);
            e.InventoryLines[0].SelectedItem = e.StockItems.Single(i => i.Id == item.Id);
            e.InventoryLines[0].SelectedGodown = e.Godowns.Single(g => g.Id == c.MainLocation!.Id);
            e.InventoryLines[0].QuantityText = qty;
            e.InventoryLines[0].RateText = "150.37";
        }

        // The original sale, Regular, and a §34 credit note linked to it.
        vm.OpenVoucher(VoucherBaseType.Sales);
        vm.ChangeMode();
        var saleEntry = vm.VoucherEntry!;
        Fill(saleEntry, sales.Id, "10");
        Assert.True(saleEntry.Accept(), saleEntry.Message);
        var sale = c.Vouchers.Last(v => c.FindVoucherType(v.TypeId)!.BaseType == VoucherBaseType.Sales);

        vm.OpenVoucher(VoucherBaseType.CreditNote);
        vm.ChangeMode();
        var noteEntry = vm.VoucherEntry!;
        Fill(noteEntry, salesReturns.Id, "4");
        noteEntry.IsSection34Note = true;
        noteEntry.SelectedCdnOriginalInvoice = noteEntry.CdnOriginalInvoices.Single(o => o.Invoice is { } v && v.Id == sale.Id);
        noteEntry.SelectedCdnReasonCode = "01 Sales return";
        Assert.True(noteEntry.Accept(), noteEntry.Message);
        var note = c.Vouchers.Last(v => c.FindVoucherType(v.TypeId)!.BaseType == VoucherBaseType.CreditNote);

        // The original is later marked Optional (Ctrl+L in alteration — the engine verb it calls), saved.
        new LedgerService(c).SetOptional(sale.Id, true);
        _storage.Save(c);
        return (sale.Id, note.Id);
    }

    [Fact]
    public void Altering_a_note_whose_original_sale_was_later_made_optional_keeps_the_posted_link()
    {
        var (saleId, noteId) = SeedNoteWhoseOriginalIsLaterMadeOptional();
        var sale = new { Id = saleId };
        var note = new { Id = noteId };

        // A NEW note is still not offered the Optional sale (IV-388 holds).
        var (fresh, freshStore) = Reload();
        var newNote = new VoucherEntryViewModel(fresh, fresh.VoucherTypes.First(t => t.BaseType == VoucherBaseType.CreditNote),
            freshStore, onSaved: () => { }, onCancelled: () => { });
        Assert.DoesNotContain(newNote.CdnOriginalInvoices, o => o.Invoice?.Id == sale.Id);

        // Re-open the POSTED note on the reloaded book and re-accept it after a narration change.
        var open = VoucherEntryViewModel.ForAlter(fresh, note.Id, freshStore, () => { }, () => { });
        Assert.Null(open.Refusal);
        var entry = open.Entry!;
        Assert.Equal(sale.Id, entry.SelectedCdnOriginalInvoice?.Invoice?.Id);
        entry.Narration = "re-accepted after the original became Optional";
        Assert.True(entry.AcceptAlteration(), entry.Message);

        var (afterAlter, afterStore) = Reload();
        Assert.Equal(sale.Id, afterAlter.CreditDebitNoteLinks.Single(l => l.CdnVoucherId == note.Id).OriginalInvoiceVoucherId);

        // Regularising the original leaves the link where it was.
        new LedgerService(afterAlter).SetOptional(sale.Id, false);
        afterStore.Save(afterAlter);
        var (afterRegular, _) = Reload();
        Assert.Equal(sale.Id, afterRegular.CreditDebitNoteLinks.Single(l => l.CdnVoucherId == note.Id).OriginalInvoiceVoucherId);
    }

    /// <summary>
    /// T2-174 verifier (wave 13, IV-388). The rehydration above puts the note's own Optional original back on the
    /// picker so an ALTERATION keeps its link. A DUPLICATE (Alt+2) runs the same rehydration and is a NEW note, so
    /// without <c>DetachAsDuplicate</c> taking that original off again, Ctrl+A posted a second note with a fresh link
    /// naming the Optional sale by id: the exact leak IV-388 closed for a new note. Driven through
    /// <see cref="MainWindowViewModel.RequestDuplicateHighlightedVoucher"/> (the Alt+2 handler) on the Day Book of a
    /// book reloaded through a fresh <see cref="CompanyStorage"/>, and read back off a second reload.
    /// </summary>
    [Fact]
    public void Duplicating_a_note_whose_original_sale_was_later_made_optional_does_not_link_the_optional_sale()
    {
        var (saleId, noteId) = SeedNoteWhoseOriginalIsLaterMadeOptional();

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        var vm = new MainWindowViewModel(new CompanyStorage(_tempDir));
        vm.ShowCompanySelect();
        vm.Menu.Single(m => m.Label == CompanyName).Activate();
        Assert.True(vm.Company!.FindVoucher(saleId)!.Optional);

        vm.OpenReport(ReportKind.DayBook);
        vm.Reports!.SelectedRow = vm.Reports.Rows.First(r => r.DrillVoucherId == noteId);
        Assert.Equal(VoucherAlterationRequest.Opened, vm.RequestDuplicateHighlightedVoucher());
        var dup = vm.VoucherEntry!;
        Assert.False(dup.IsAltering);

        // The copy is not offered the Optional sale, and falls to the consolidated reference carrying the
        // original's number and date, which is where a duplicate of a note whose original is no longer offered lands.
        Assert.DoesNotContain(dup.CdnOriginalInvoices, o => o.Invoice?.Id == saleId);
        Assert.True(dup.SelectedCdnOriginalInvoice?.IsConsolidated);
        Assert.Equal(vm.Company!.FormatVoucherNumber(vm.Company.FindVoucher(saleId)!), dup.CdnOriginalInvoiceNumber);
        Assert.True(dup.Accept(), dup.Message);

        var (after, _) = Reload();
        var copyLink = after.CreditDebitNoteLinks.Single(l => l.CdnVoucherId != noteId);
        Assert.Null(copyLink.OriginalInvoiceVoucherId);
        Assert.Equal(after.FindVoucher(saleId)!.Date, copyLink.OriginalInvoiceDate);
        // The source note keeps its own link to the sale.
        Assert.Equal(saleId, after.CreditDebitNoteLinks.Single(l => l.CdnVoucherId == noteId).OriginalInvoiceVoucherId);
    }
}
