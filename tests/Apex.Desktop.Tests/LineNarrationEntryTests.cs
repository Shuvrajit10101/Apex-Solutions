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
using Xunit;

namespace Apex.Desktop.Tests;

/// <summary>
/// 🔴 <b>CENSUS 5.11 — <i>Provide narration for each ledger in voucher</i> STOPS BEING WRITE-ONLY.</b> The census
/// cell: <i>"the SECOND FLAG IS STILL ENTIRELY WRITE-ONLY … no behaviour reader at all."</i>
///
/// <para><b>Fidelity (R7, ruling 14).</b> help.tallysolutions.com/voucher-types-tally/, opened by content
/// 2026-10-25, Configure Voucher Types → Set up transaction options: <i>"Enable Provide narration for each ledger
/// in voucher to add narration for individual ledgers"</i>. The page continues "based on the voucher mode, such as
/// Accounting/Item Invoice or As Voucher" — this build offers it on the As-Voucher Dr/Cr grid only; the invoice
/// modes are a documented divergence (see <c>docs/invented-vs-cloned.md</c>, IV-196).</para>
///
/// <para>Every assertion reads an OBSERVABLE artefact: a realised control on the real window, a row off a book
/// RELOADED through a fresh <see cref="CompanyStorage"/>, emitted PDF bytes, or the canonical export of the
/// reloaded book imported into a second company and reloaded again.</para>
/// </summary>
public sealed class LineNarrationEntryTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ApexLineNarr_" + Guid.NewGuid().ToString("N"));

    private static void Pump(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.Measure(new Size(1280, 800));
        window.Arrange(new Rect(0, 0, 1280, 800));
        Dispatcher.UIThread.RunJobs();
    }

    private MainWindowViewModel NewCompany(string name)
    {
        var vm = new MainWindowViewModel(new CompanyStorage(_dir));
        vm.NewCompanyName = name;
        vm.CreateCompany();
        var c = vm.Company!;
        c.AddLedger(new Ledger.Domain.Ledger(Guid.NewGuid(), "Rent", c.FindGroupByName("Indirect Expenses")!.Id,
            Money.Zero, openingIsDebit: true));
        return vm;
    }

    private static VoucherType PaymentType(MainWindowViewModel vm) =>
        vm.Company!.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Payment && t.IsActive);

    private Company Reload(string name)
    {
        var fresh = new CompanyStorage(_dir);   // a FRESH store handle
        return fresh.Load(fresh.ListCompanies().Single(e => e.Name == name));
    }

    /// <summary>Keys a 12,000 rent Payment on the screen it opens in — SINGLE ENTRY (Account = Cash, one
    /// particular = Rent, which is <c>Lines[1]</c>).</summary>
    private static VoucherEntryViewModel KeyRentPayment(MainWindowViewModel vm)
    {
        vm.OpenVoucher(PaymentType(vm));
        var e = vm.VoucherEntry!;
        Assert.True(e.IsSingleEntry);
        e.SingleEntryAccount = vm.Company!.FindLedgerByName("Cash");
        e.Lines[1].SelectedLedger = vm.Company!.FindLedgerByName("Rent");
        e.Lines[1].AmountText = "12000";
        return e;
    }

    /// <summary>Keys Dr Rent / Cr Cash 12,000 on a Journal — the As-Voucher Dr/Cr grid.</summary>
    private static VoucherEntryViewModel KeyRentJournal(MainWindowViewModel vm)
    {
        vm.OpenVoucher(JournalType(vm));
        var e = vm.VoucherEntry!;
        Assert.True(e.ShowPlainDrCrGrid);
        e.Lines[0].SelectedLedger = vm.Company!.FindLedgerByName("Rent");
        e.Lines[0].Side = DrCr.Debit;
        e.Lines[0].AmountText = "12000";
        e.Lines[1].SelectedLedger = vm.Company!.FindLedgerByName("Cash");
        e.Lines[1].Side = DrCr.Credit;
        e.Lines[1].AmountText = "12000";
        return e;
    }

    private static VoucherType JournalType(MainWindowViewModel vm) =>
        vm.Company!.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Journal && t.IsActive);

    private static TextBox[] NarrationBoxes(Window w) =>
        w.GetVisualDescendants().OfType<TextBox>().Where(t => t.Name == "LineNarrationBox").ToArray();

    private static TextBox AmountBoxOf(Window w, VoucherLineViewModel line) =>
        w.GetVisualDescendants().OfType<TextBox>()
         .First(t => ReferenceEquals(t.DataContext, line) && t.PlaceholderText == "0.00");

    // ============================================================== the control

    /// <summary>
    /// 🔴 <b>THE CONTROL.</b> The seeded Payment and Journal types have the flag OFF, and no line on the realised
    /// Dr/Cr grid offers a narration field. <b>Mutation:</b> making <c>IsLineNarrationEnabled</c> return
    /// <c>true</c> reddens this.
    /// </summary>
    [AvaloniaFact]
    public void Without_the_flag_no_line_offers_a_narration_field()
    {
        var vm = NewCompany("LN Control Co");
        var w = new MainWindow { DataContext = vm };
        try
        {
            w.Show();
            Assert.False(PaymentType(vm).ProvideNarrationForEachLedger);
            Assert.False(JournalType(vm).ProvideNarrationForEachLedger);
            KeyRentJournal(vm);
            Pump(w);
            Assert.NotEmpty(NarrationBoxes(w));   // realised, so the absence below is not vacuous
            Assert.All(NarrationBoxes(w), b => Assert.False(b.IsEffectivelyVisible));
        }
        finally { w.Close(); }
    }

    // ============================================================== (b)+(c) keyboard → SQLite

    /// <summary>
    /// 🔴 <b>THE ROW-5.11 TEST ON THE DR/CR GRID, THROUGH THE REAL KEYS.</b> With the flag on, Tab off the first
    /// line's Amount lands in that line's narration field; the text is typed with real key input; Ctrl+A on the
    /// real window accepts; and the narration is read back off a book RELOADED through a fresh store handle — on
    /// the Rent line and not on the Cash line.
    ///
    /// <para><b>Mutations:</b> deleting <c>line.ShowLineNarration = IsLineNarrationEnabled</c> in
    /// <c>VoucherEntryViewModel.AddLine</c> reddens this at the visibility assertion; dropping
    /// <c>narration: l.ToLineNarration()</c> from the Accept builder reddens it at the reloaded-line
    /// assertion.</para>
    /// </summary>
    [AvaloniaFact]
    public void With_the_flag_on_a_narration_keyed_under_a_journal_line_is_saved_on_that_line()
    {
        var vm = NewCompany("LN Keys Co");
        JournalType(vm).ProvideNarrationForEachLedger = true;
        var w = new MainWindow { DataContext = vm };
        try
        {
            w.Show();
            var e = KeyRentJournal(vm);
            Pump(w);

            var visible = NarrationBoxes(w).Where(b => b.IsEffectivelyVisible).ToArray();
            Assert.Equal(e.Lines.Count, visible.Length);

            TypeUnder(w, e.Lines[0], "Rent for May shop 4");
            Assert.Equal("Rent for May shop 4", e.Lines[0].LineNarration);

            w.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);   // accept
            Pump(w);
            Assert.Single(vm.Company!.Vouchers);

            var book = Reload("LN Keys Co");
            var v = Assert.Single(book.Vouchers);
            var rentId = book.FindLedgerByName("Rent")!.Id;
            var cashId = book.FindLedgerByName("Cash")!.Id;
            Assert.Equal("Rent for May shop 4", v.Lines.Single(l => l.LedgerId == rentId).Narration);
            Assert.Null(v.Lines.Single(l => l.LedgerId == cashId).Narration);
        }
        finally { w.Close(); }
    }

    /// <summary>
    /// The SAME on the layout Payment / Receipt / Contra actually OPEN in — Single Entry. Without this the field
    /// would be unreachable on the three types an operator keys most. <b>Mutation:</b> removing the
    /// <c>LineNarrationBox</c> from the Single-Entry particulars template reddens this at the Tab assertion.
    /// </summary>
    [AvaloniaFact]
    public void With_the_flag_on_a_single_entry_payment_particular_takes_a_narration_from_the_keyboard()
    {
        var vm = NewCompany("LN Single Co");
        PaymentType(vm).ProvideNarrationForEachLedger = true;
        var w = new MainWindow { DataContext = vm };
        try
        {
            w.Show();
            var e = KeyRentPayment(vm);
            Pump(w);

            TypeUnder(w, e.Lines[1], "Rent for May");
            w.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);   // accept
            Pump(w);

            var book = Reload("LN Single Co");
            var v = Assert.Single(book.Vouchers);
            var rentId = book.FindLedgerByName("Rent")!.Id;
            Assert.Equal("Rent for May", v.Lines.Single(l => l.LedgerId == rentId).Narration);
        }
        finally { w.Close(); }
    }

    /// <summary>Focuses <paramref name="line"/>'s realised Amount box, presses the REAL Tab key, requires focus to
    /// land in that line's narration box, then types <paramref name="text"/> as real text input.</summary>
    private static void TypeUnder(Window w, VoucherLineViewModel line, string text)
    {
        AmountBoxOf(w, line).Focus();
        Pump(w);
        w.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
        Pump(w);
        var focused = TopLevel.GetTopLevel(w)?.FocusManager?.GetFocusedElement() as TextBox;
        Assert.NotNull(focused);
        Assert.Equal("LineNarrationBox", focused!.Name);
        Assert.Same(line, focused.DataContext);
        w.KeyTextInput(text);
        Pump(w);
    }

    // ============================================================== reload on alteration

    /// <summary>
    /// The narration RELOADS when the voucher is opened for alteration, and an edit to it is what the reloaded
    /// book then holds. <b>Mutation:</b> deleting <c>LineNarration = posted.Narration ?? string.Empty</c> in
    /// <c>VoucherLineViewModel.RehydrateFrom</c> reddens the first assertion.
    /// </summary>
    [Fact]
    public void The_line_narration_reloads_on_alteration_and_an_edit_to_it_persists()
    {
        var vm = NewCompany("LN Alter Co");
        JournalType(vm).ProvideNarrationForEachLedger = true;
        var e = KeyRentJournal(vm);
        e.Lines[0].LineNarration = "Rent for May";
        e.Lines[1].LineNarration = "From counter cash";
        Assert.True(e.Accept());
        var id = e.SavedVoucherId;

        var open = VoucherEntryViewModel.ForAlter(vm.Company!, id, new CompanyStorage(_dir), () => { }, () => { });
        Assert.Null(open.Refusal);
        var alter = open.Entry!;
        var rentLine = alter.Lines.Single(l => l.SelectedLedger?.Name == "Rent");
        Assert.Equal("Rent for May", rentLine.LineNarration);
        Assert.True(rentLine.ShowLineNarration);
        Assert.Equal("From counter cash", alter.Lines.Single(l => l.SelectedLedger?.Name == "Cash").LineNarration);

        rentLine.LineNarration = "Rent for May and June";
        Assert.True(alter.AcceptAlteration());

        var book = Reload("LN Alter Co");
        var v = book.Vouchers.Single(x => x.Id == id);
        Assert.Equal("Rent for May and June",
            v.Lines.Single(l => l.LedgerId == book.FindLedgerByName("Rent")!.Id).Narration);
        Assert.Equal("From counter cash",
            v.Lines.Single(l => l.LedgerId == book.FindLedgerByName("Cash")!.Id).Narration);
    }

    // ============================================================== it prints

    /// <summary>
    /// The narration PRINTS under its line, in the emitted PDF bytes of the voucher read back off the RELOADED
    /// book, and in the on-screen mirror. <b>Mutation:</b> dropping <c>Narration = …</c> from
    /// <c>VoucherPrintProjector</c>'s line projection reddens this.
    /// </summary>
    [Fact]
    public void The_line_narration_prints_on_the_voucher_pdf()
    {
        var vm = NewCompany("LN Print Co");
        JournalType(vm).ProvideNarrationForEachLedger = true;
        var e = KeyRentJournal(vm);
        e.Lines[0].LineNarration = "Rent for May shop four";
        Assert.True(e.Accept());

        var book = Reload("LN Print Co");
        var v = Assert.Single(book.Vouchers);
        var preview = new VoucherDetailViewModel(book, v).BuildPrintPreview();
        var pdf = Encoding.Latin1.GetString(preview.PdfBytes);
        Assert.Contains("(Rent for May shop four) Tj", pdf, StringComparison.Ordinal);

        var mirror = string.Join("\n", preview.Pages.SelectMany(p => p.Lines.Select(l => string.Join(" ", l.Cells))));
        Assert.Contains("Rent for May shop four", mirror, StringComparison.Ordinal);
    }

    // ============================================================== canonical export → import, reloaded

    /// <summary>
    /// 🔴 <b>EXPORT → IMPORT, READ OFF THE RELOADED BOOK.</b> The book is reloaded from SQLite, exported to canonical
    /// JSON and XML, each export imported into a second company that is SAVED and RELOADED through a fresh handle;
    /// the narration is on the right line of the reloaded import.
    /// </summary>
    [Fact]
    public void The_line_narration_survives_canonical_export_and_import_into_a_reloaded_book()
    {
        var vm = NewCompany("LN Export Co");
        JournalType(vm).ProvideNarrationForEachLedger = true;
        var e = KeyRentJournal(vm);
        e.Lines[0].LineNarration = "Rent for May";
        Assert.True(e.Accept());

        var source = Reload("LN Export Co");
        Assert.Single(source.Vouchers);
        foreach (var (format, bytes) in new[]
                 {
                     ("json", CanonicalJson.Export(source)),
                     ("xml", CanonicalXml.Export(source)),
                 })
        {
            var (model, errors) = format == "json" ? CanonicalJson.Parse(bytes) : CanonicalXml.Parse(bytes);
            Assert.Empty(errors);

            // ⚠️ A SEPARATE companies directory, deliberately. ImportPlan.ApplyCompanyHeader copies the SOURCE
            // company's NAME onto the target, and CompanyStorage derives the file path from the name — so saving
            // the import into the same directory writes into the SOURCE book's file, not the target's (measured
            // while writing this test; reported, not fixed here — it is outside census 5.11).
            var targetDir = Path.Combine(_dir, "import-" + format);
            var targetStorage = new CompanyStorage(targetDir);
            var tvm = new MainWindowViewModel(targetStorage);
            tvm.NewCompanyName = "LN Import " + format;
            tvm.CreateCompany();
            var result = new CompanyImportService(tvm.Company!).Apply(model!, DuplicatePolicy.Skip);
            Assert.True(result.Applied);
            Assert.Single(tvm.Company!.Vouchers);
            targetStorage.Save(tvm.Company!);

            var fresh = new CompanyStorage(targetDir);   // a FRESH store handle on the import's directory
            var imported = fresh.Load(fresh.ListCompanies().Single(x => x.Name == tvm.Company!.Name));
            var v = Assert.Single(imported.Vouchers);
            var rentId = imported.FindLedgerByName("Rent")!.Id;
            Assert.Equal("Rent for May", v.Lines.Single(l => l.LedgerId == rentId).Narration);
            Assert.Null(v.Lines.Single(l => l.LedgerId != rentId).Narration);
        }
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
