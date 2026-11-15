using System;
using System.Linq;
using Avalonia.Headless.XUnit;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Io;
using Apex.Ledger.Services;
using Apex.Desktop.ViewModels;
using Xunit;

namespace Apex.Desktop.Tests;

/// <summary>
/// 🔴 <b>Wave-10 review (track gd) — THE ROLLBACK ARMS OF THE CTRL+L / CTRL+T SPLITS, WHICH THE BRANCH SHIPPED
/// UNTESTED.</b> Census 5.7 / 5.8 added <c>SetOptional</c> / <c>SetPostDated</c> moves ahead of <c>Replace</c> on
/// BOTH alteration screens, and every failure path after them must put the flags back AND discard their edit-log
/// lines newest-first. <c>RollBackFlags</c> (stock) and <c>RollBackPostDated</c> (accounting) SWALLOW their
/// exceptions, so a mistake there is silent: the in-memory book holds a voucher current (or live) while the .db
/// holds it post-dated (or Optional), and the next successful save on any screen persists fictitious log lines.
/// No test reached either arm: no test file in the branch names a post-dated or two-flag save failure or refusal.
///
/// <para>The save failure is provoked the way <see cref="VoucherOptionalSaveFailureRollbackTests"/> provokes it:
/// a bad PIN, which <c>CompanyStorage.Save</c> throws on out of <c>Company.EnsureValid()</c>. Every test asserts the
/// flags off the book, the edit log by IDENTITY, and a byte-identical canonical export.</para>
/// </summary>
public sealed class ProvisionalFlagRollbackTests
{
    /// <summary>
    /// A post-dated Journal opened in alteration, Ctrl+T (Current), Ctrl+A with a save that throws. <b>Mutation:</b>
    /// emptying <c>VoucherEntryViewModel.RollBackPostDated</c> leaves the voucher current with a SetPostDated line
    /// on the book, reddening the flag and log assertions.
    /// </summary>
    [AvaloniaFact]
    public void A_failed_save_after_Ctrl_T_in_accounting_alteration_puts_the_post_dated_flag_back_and_leaves_no_log()
    {
        using var book = AlterationBook.New("pdsavefail");
        var dr = book.Ledger("PD Rent", "Indirect Expenses");
        var cr = book.Ledger("PD Income", "Indirect Incomes");
        var posted = book.Post(VoucherBaseType.Journal, book.On(),
            new[] { (dr, DrCr.Debit, "4120.37"), (cr, DrCr.Credit, "4120.37") }, "cheque",
            configure: e => e.TogglePostDated());
        Assert.True(book.Company.FindVoucher(posted.Id)!.PostDated);
        var before = book.Export();
        var logBefore = book.Company.VoucherEditLog.Select(e => e.Id).ToList();

        var open = book.ForAlter(posted.Id);
        Assert.True(open.Entry!.IsPostDated);
        open.Entry.TogglePostDated();                // Ctrl+T (Current)
        Assert.False(open.Entry.IsPostDated);

        book.Company.Pin = "NOT-A-PIN";              // the next Save throws out of EnsureValid
        Assert.False(open.Entry.AcceptAlteration());
        Assert.Contains("Could not save the company", open.Entry.Message!, StringComparison.Ordinal);
        book.Company.Pin = null;

        Assert.True(book.Company.FindVoucher(posted.Id)!.PostDated);
        Assert.Equal(logBefore, book.Company.VoucherEditLog.Select(e => e.Id).ToList());
        Assert.Equal(before, book.Export());
    }

    private static (Guid VoucherId, Guid ItemId, Guid GodownId) SeedStock(AlterationBook book)
    {
        var c = book.Company;
        var masters = new InventoryService(c);
        var item = masters.CreateStockItem("Rollback Bolt", masters.CreateStockGroup("Rollback Goods").Id,
            masters.CreateSimpleUnit("RNos", "Rollback Numbers").Id);
        var godown = c.MainLocation!.Id;
        masters.AddOpeningBalance(item.Id, godown, 10m, Money.FromRupees(10.37m));
        var receipt = new InventoryVoucher(Guid.NewGuid(),
            c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.ReceiptNote).Id, book.On(),
            new[] { new InventoryAllocation(item.Id, godown, 7m, StockDirection.Inward, Money.FromRupees(55.01m)) },
            postDated: true) { Optional = true };
        new InventoryPostingService(c).Post(receipt);
        book.Storage.Save(c);
        return (receipt.Id, item.Id, godown);
    }

    /// <summary>
    /// An Optional AND post-dated Receipt Note opened in alteration, Ctrl+L and Ctrl+T, Ctrl+A with a save that
    /// throws: TWO flag moves, two log lines (plus the Replace pair), all undone in LIFO order. <b>Mutation:</b>
    /// emptying <c>InventoryVoucherEntryViewModel.RollBackFlags</c> leaves the receipt live and current on the book
    /// with two fictitious log lines, reddening every assertion below the call.
    /// </summary>
    [AvaloniaFact]
    public void A_failed_save_after_Ctrl_L_and_Ctrl_T_in_stock_alteration_puts_both_flags_back_and_leaves_no_log()
    {
        using var book = AlterationBook.New("stockflagsave");
        var (id, itemId, godown) = SeedStock(book);
        var c = book.Company;
        var before = book.Export();
        var logBefore = c.VoucherEditLog.Select(e => e.Id).ToList();
        var asOf = c.FinancialYearStart.AddYears(1).AddDays(-1);

        var open = InventoryVoucherEntryViewModel.ForAlter(c, id, book.Storage, onSaved: () => { },
            onCancelled: () => { });
        var entry = open.Entry!;
        Assert.True(entry.IsOptional);
        Assert.True(entry.IsPostDated);
        entry.ToggleOptional();                      // Ctrl+L (Regular)
        entry.TogglePostDated();                     // Ctrl+T (Current)

        c.Pin = "NOT-A-PIN";
        Assert.False(entry.AcceptAlteration());
        Assert.Contains("Could not save the company", entry.Message!, StringComparison.Ordinal);
        c.Pin = null;

        var onBook = c.FindInventoryVoucher(id)!;
        Assert.True(onBook.Optional);
        Assert.True(onBook.PostDated);
        Assert.Equal(10m, new InventoryLedger(c).OnHand(itemId, godown, asOf));
        Assert.Equal(logBefore, c.VoucherEditLog.Select(e => e.Id).ToList());
        Assert.Equal(before, book.Export());
    }

    /// <summary>
    /// The same two flag moves, but <c>Replace</c> refuses (the date is moved before Books Begin From), so the
    /// alteration fails BEFORE any save. <b>Mutation:</b> removing the <c>RollBackFlags</c> call from the Replace
    /// catch leaves the receipt live and current with two SetOptional / SetPostDated lines.
    /// </summary>
    [AvaloniaFact]
    public void A_refused_replace_after_Ctrl_L_and_Ctrl_T_in_stock_alteration_puts_both_flags_back()
    {
        using var book = AlterationBook.New("stockflagrefuse");
        var (id, _, _) = SeedStock(book);
        var c = book.Company;
        var before = book.Export();
        var logBefore = c.VoucherEditLog.Select(e => e.Id).ToList();

        var entry = InventoryVoucherEntryViewModel.ForAlter(c, id, book.Storage, onSaved: () => { },
            onCancelled: () => { }).Entry!;
        entry.ToggleOptional();
        entry.TogglePostDated();
        entry.Date = c.BooksBeginFrom.AddDays(-1);

        Assert.False(entry.AcceptAlteration());
        Assert.Contains("Cannot alter", entry.Message!, StringComparison.Ordinal);

        var onBook = c.FindInventoryVoucher(id)!;
        Assert.True(onBook.Optional);
        Assert.True(onBook.PostDated);
        Assert.Equal(logBefore, c.VoucherEditLog.Select(e => e.Id).ToList());
        Assert.Equal(before, book.Export());
    }
}
