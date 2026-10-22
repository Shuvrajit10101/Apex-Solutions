using System;
using System.IO;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Xunit;

namespace Apex.Desktop.Tests;

/// <summary>
/// 🔴 <b>CENSUS 5.11 — the <c>Print voucher after saving</c> flag STOPS BEING A DEAD CHECKBOX.</b>
///
/// <para><b>The defect these close, quoted from the census cell.</b> <i>"THE OTHER TWO FLAGS ARE WRITE-ONLY …
/// returns setters, persistence and export only — not one reader that changes behaviour. Nothing on any save
/// path opens the Voucher Printing screen when Print voucher after saving is ticked … They are checkboxes the
/// operator can tick that do nothing."</i> That was measured and true: before this slice
/// <c>grep -rn "PrintAfterSaving" src/</c> returned fifteen hits and every one of them was a setter, a
/// persistence parameter, a canonical-export field or the master screen's own rehydration.</para>
///
/// <para><b>Fidelity (R7, ruling 14).</b> help.tallysolutions.com/voucher-types-tally/, opened and verified BY
/// CONTENT on 2026-10-22: <i>"Enable Print voucher after saving to automatically open the Voucher Printing
/// screen for the voucher, once it is saved."</i> The same page scopes the option to the
/// <i>"vouchers/invoices created using that particular voucher type"</i>, which is why the creation door is
/// wired and the alteration door is not.</para>
///
/// <para><b>🔴 WHY A CONTROL TEST IS THE FIRST TEST IN THIS FILE.</b> A single "the preview opened" assertion
/// would stay green if the preview opened after EVERY save — which would be a worse defect than the dead
/// checkbox, and the operator would meet it on every voucher they key. The pair is the evidence: identical
/// company, identical voucher, identical accept, the ONLY difference being the flag.</para>
///
/// <para><b>Mutation, recorded rather than claimed</b> — see the per-test notes. Deleting the two-line reader in
/// <c>MainWindowViewModel.OpenVoucher</c> reddens
/// <see cref="Ticking_print_voucher_after_saving_opens_the_voucher_printing_screen_on_the_saved_voucher"/> and
/// <see cref="The_printing_screen_that_opens_renders_the_voucher_that_was_actually_committed"/>; inverting its
/// condition to ignore the flag reddens
/// <see cref="A_voucher_type_without_the_flag_saves_straight_through_with_no_printing_screen"/>.</para>
/// </summary>
public sealed class PrintAfterSavingTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public PrintAfterSavingTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexPrintAfterSave_" + Guid.NewGuid().ToString("N"));
        _storage = new CompanyStorage(_tempDir);
    }

    private MainWindowViewModel NewSeededCompany(string name)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = name;
        vm.CreateCompany();
        Assert.Equal(Screen.Gateway, vm.CurrentScreen);
        return vm;
    }

    /// <summary>
    /// Posts Dr Cash 5,000 / Cr Capital 5,000 as a Receipt through the real entry screen and accepts it. Returns
    /// the entry screen so the caller can read what it committed.
    /// </summary>
    private static VoucherEntryViewModel PostAReceipt(MainWindowViewModel vm, decimal amount = 5000m)
    {
        var capital = vm.Company!.FindLedgerByName("Capital A/c")
                      ?? throw new InvalidOperationException("fixture did not create Capital A/c");
        var cash = vm.Company!.FindLedgerByName("Cash")
                   ?? throw new InvalidOperationException("seed is missing Cash");

        vm.OpenVoucher(VoucherBaseType.Receipt);
        Assert.Equal(Screen.VoucherEntry, vm.CurrentScreen);
        var entry = vm.VoucherEntry!;

        entry.Lines[0].SelectedLedger = cash;
        entry.Lines[0].Side = DrCr.Debit;
        entry.Lines[0].AmountText = amount.ToString(System.Globalization.CultureInfo.InvariantCulture);

        entry.Lines[1].SelectedLedger = capital;
        entry.Lines[1].Side = DrCr.Credit;
        entry.Lines[1].AmountText = amount.ToString(System.Globalization.CultureInfo.InvariantCulture);

        Assert.True(entry.IsBalanced);
        Assert.True(entry.Accept());
        return entry;
    }

    private static void AddCapitalLedger(MainWindowViewModel vm)
    {
        vm.ShowLedgerMaster();
        var master = vm.LedgerMaster!;
        master.Name = "Capital A/c";
        master.SelectedGroup = vm.Company!.FindGroupByName("Capital Account");
        Assert.True(master.Create());
        vm.ShowGateway();
    }

    private static VoucherType ReceiptType(MainWindowViewModel vm) =>
        vm.Company!.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Receipt && t.IsActive);

    // ================================================================= (a) the control

    /// <summary>
    /// 🔴 <b>THE CONTROL.</b> The seeded Receipt type has the flag OFF (ER-13: both new flags default off so
    /// every book that has never opened the master is byte-identical), so accepting must land on the Gateway with
    /// NO printing screen anywhere in the cascade.
    ///
    /// <para><b>Mutation:</b> changing the reader's condition from <c>type.PrintAfterSaving &amp;&amp; …</c> to
    /// an unconditional open reddens this test — <c>PrintPreview</c> comes back non-null and
    /// <c>CurrentScreen</c> is <c>PrintPreview</c> instead of <c>Gateway</c>.</para>
    /// </summary>
    [Fact]
    public void A_voucher_type_without_the_flag_saves_straight_through_with_no_printing_screen()
    {
        var vm = NewSeededCompany("PAS Control Co");
        AddCapitalLedger(vm);

        // The flag really is off on the seed — asserted, not assumed, so this control cannot silently become
        // a second copy of the positive test if the seed ever changes.
        Assert.False(ReceiptType(vm).PrintAfterSaving);

        PostAReceipt(vm);

        Assert.Null(vm.PrintPreview);
        Assert.Equal(Screen.Gateway, vm.CurrentScreen);
        Assert.DoesNotContain(vm.Columns, c => c.Title == "Print Preview");
    }

    // ================================================================= (b) the flag does something

    /// <summary>
    /// 🔴 <b>THE ROW-5.11 TEST.</b> Tick the flag on the voucher TYPE through the real master service route the
    /// screen uses, then key and accept a voucher of that type: the Voucher Printing screen opens by itself.
    ///
    /// <para><b>Mutation:</b> deleting the two-line reader in <c>MainWindowViewModel.OpenVoucher</c> (the
    /// <c>if (type.PrintAfterSaving …) OpenVoucherPrintingAfterSave(saved)</c> call) reddens this on the first
    /// assertion — <c>PrintPreview</c> is null, exactly as it was before the slice.</para>
    /// </summary>
    [Fact]
    public void Ticking_print_voucher_after_saving_opens_the_voucher_printing_screen_on_the_saved_voucher()
    {
        var vm = NewSeededCompany("PAS Co");
        AddCapitalLedger(vm);

        ReceiptType(vm).PrintAfterSaving = true;

        PostAReceipt(vm);

        // The printing screen is OPEN, is the active column, and is the realised print-preview pane — not merely
        // a flag somebody set.
        Assert.NotNull(vm.PrintPreview);
        Assert.Equal(Screen.PrintPreview, vm.CurrentScreen);
        Assert.Same(vm.PrintPreview, vm.Columns[vm.ActiveColumnIndex].Page);

        // And it is a real rendered document, not an empty shell.
        Assert.NotEmpty(vm.PrintPreview!.PdfBytes);
        Assert.NotEmpty(vm.PrintPreview!.Pages);
    }

    // ================================================================= (c) it prints the COMMITTED voucher

    /// <summary>
    /// 🔴 <b>THE DOCUMENT IS FOR THE VOUCHER THE BOOK ACTUALLY HOLDS.</b> The whole hazard of an automatic print
    /// door is that it issues a document for the wrong voucher — the class <c>PickAddVoucherType</c> records for
    /// resolving a chosen type back to its base kind, and the class row 5.3's number-reuse residual makes real.
    /// So the voucher is read back off the <b>RELOADED</b> book and the preview's own rendered bytes are required
    /// to carry that voucher's number.
    ///
    /// <para><b>Mutation:</b> deleting the reader reddens this at <c>Assert.NotNull(vm.PrintPreview)</c>; making
    /// <c>OpenVoucherPrintingAfterSave</c> print a DIFFERENT voucher (e.g. <c>Vouchers[0]</c> on a book where the
    /// second receipt is the one just saved) reddens the byte assertion below.</para>
    /// </summary>
    [Fact]
    public void The_printing_screen_that_opens_renders_the_voucher_that_was_actually_committed()
    {
        var vm = NewSeededCompany("PAS Doc Co");
        AddCapitalLedger(vm);
        ReceiptType(vm).PrintAfterSaving = true;

        // Post a FIRST receipt so the one under test is not the only — nor the first — voucher in the book. A
        // door that printed "the first voucher" would otherwise pass.
        PostAReceipt(vm, 1111m);
        vm.ShowGateway();

        var entry = PostAReceipt(vm, 2222m);
        var savedId = entry.SavedVoucherId;
        Assert.NotEqual(Guid.Empty, savedId);

        // 🔴 OFF THE RELOADED BOOK — the voucher the document is for is really committed to the .db, and it is
        // the SECOND receipt, not the first.
        var reloaded = _storage.Load(_storage.ListCompanies().Single(e => e.Name == "PAS Doc Co"));
        var persisted = reloaded.Vouchers.Single(v => v.Id == savedId);
        Assert.Equal(2222m, persisted.TotalDebit.Amount);

        var receipts = reloaded.Vouchers
            .Where(v => v.TypeId == ReceiptType(vm).Id)
            .OrderBy(v => v.Number)
            .ToList();
        Assert.Equal(2, receipts.Count);
        Assert.Equal(persisted.Id, receipts[1].Id);          // the saved one is the SECOND, not the first

        // The preview is open and its rendered page text names the voucher that was just saved — the observable
        // artefact, read off the realised pane rather than off anything this test set.
        Assert.NotNull(vm.PrintPreview);
        var rendered = string.Join(
            "\n",
            vm.PrintPreview!.Pages.SelectMany(p =>
                p.Lines.Select(l => string.Join(" ", l.Cells))));
        Assert.Contains("2222", rendered.Replace(",", string.Empty).Replace(".00", string.Empty));
        Assert.DoesNotContain("1111", rendered.Replace(",", string.Empty).Replace(".00", string.Empty));
    }

    // ================================================================= (d) the pane beneath survives

    /// <summary>
    /// 🔴 <b>THE PRINTING COLUMN IS APPENDED, NOT SUBSTITUTED.</b> The flag must not cost the operator the screen
    /// their save returned to: Esc off the printing screen leaves them on the Gateway the accept landed on, with
    /// the voucher still posted. This is the same append-without-trim contract
    /// <c>OpenPrintPreview</c> carries, and the reason <c>OpenVoucherPrintingAfterSave</c> copies its tail
    /// verbatim instead of calling <c>OpenPageColumn</c>.
    /// </summary>
    [Fact]
    public void Escaping_the_printing_screen_returns_to_the_screen_the_save_landed_on_and_keeps_the_voucher()
    {
        var vm = NewSeededCompany("PAS Esc Co");
        AddCapitalLedger(vm);
        ReceiptType(vm).PrintAfterSaving = true;

        var entry = PostAReceipt(vm);
        Assert.Equal(Screen.PrintPreview, vm.CurrentScreen);

        vm.Back();

        Assert.Null(vm.PrintPreview);
        Assert.Equal(Screen.Gateway, vm.CurrentScreen);

        // The voucher is still on the book and still on the .db — the printing detour committed nothing and
        // destroyed nothing.
        Assert.Contains(vm.Company!.Vouchers, v => v.Id == entry.SavedVoucherId);
        var reloaded = _storage.Load(_storage.ListCompanies().Single(e => e.Name == "PAS Esc Co"));
        Assert.Contains(reloaded.Vouchers, v => v.Id == entry.SavedVoucherId);
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
}
