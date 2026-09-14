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
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Desktop.Views;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Desktop.Tests;

/// <summary>
/// Census 4.17 (<c>T2-9</c>) — <b>CONVERT MEMORANDUM</b>: the route that did not exist.
///
/// <para><b>What was wrong.</b> <c>MainWindowViewModel.ConvertMemorandum</c> and
/// <c>LedgerService.ConvertToRegular</c> both shipped complete, with the audit verb
/// <see cref="VoucherEditVerb.ConvertMemorandum"/> (persisted ordinal 3) already reserved — and <b>zero production
/// callers and no key route</b>. A memorandum could be posted and never regularised by anything a user could press.
/// These tests drive the REAL headless <see cref="MainWindow"/> and the REAL store, so they fail on a tree that has
/// the engine but not the route.</para>
///
/// <para><b>Fidelity, stated honestly.</b> The capability is vendor-attested (<i>"You can alter and convert a Memo
/// voucher into a regular voucher when you decide to bring the entry into your books"</i>,
/// <c>help.tallysolutions.com/docs/te9rel65/Voucher_Entry/Optional_Non-Accounting_Vouchers/Memorandum_Voucher.htm</c>).
/// 🔴 That is a Tally.ERP 9 page; the TallyPrime-era pages describe no conversion route and its shortcut table names
/// no chord, so <b>the bare-C chord and the Memorandum-Register route are OURS</b> and are asserted here as ours —
/// which is why the chord is deliberately not in <c>ShellChordTable</c>.</para>
/// </summary>
public sealed class MemorandumConversionTests
{
    // ---------------------------------------------------------------- harness (mirrors VoucherNumberingConfigShellTests)

    private static (MainWindow Window, MainWindowViewModel Vm, string Dir) NewWindow(string company)
    {
        var dir = Path.Combine(Path.GetTempPath(), "ApexMemoConv_" + Guid.NewGuid().ToString("N"));
        var vm = new MainWindowViewModel(new CompanyStorage(dir));
        var window = new MainWindow { DataContext = vm, Width = 1920, Height = 1080 };
        window.Show();
        vm.NewCompanyName = company;
        vm.CreateCompany();
        vm.ShowGateway();
        Pump(window);
        return (window, vm, dir);
    }

    private static void Pump(Window window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    private static IEnumerable<Visual> Descendants(Visual v)
    {
        foreach (var c in v.GetVisualChildren())
        {
            yield return c;
            foreach (var g in Descendants(c)) yield return g;
        }
    }

    private static void Cleanup(Window window, string dir)
    {
        window.Close();
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
        catch (IOException) { /* best effort */ }
    }

    private static Apex.Ledger.Domain.Ledger AddCashLedger(Company c, string name, bool openingIsDebit)
    {
        var l = new Apex.Ledger.Domain.Ledger(Guid.NewGuid(), name, c.FindGroupByName("Cash-in-Hand")!.Id,
            Money.Zero, openingIsDebit);
        c.AddLedger(l);
        return l;
    }

    /// <summary>Posts one balanced memorandum for <paramref name="rupees"/> dated books-begin and returns it.</summary>
    private static Voucher PostMemo(Company c, decimal rupees, out Apex.Ledger.Domain.Ledger debit,
        out Apex.Ledger.Domain.Ledger credit, string suffix = "")
    {
        debit = AddCashLedger(c, "Memo Dr" + suffix, openingIsDebit: true);
        credit = AddCashLedger(c, "Memo Cr" + suffix, openingIsDebit: false);
        var memoType = c.FindVoucherTypeByName("Memorandum")!;
        return new LedgerService(c).Post(new Voucher(Guid.NewGuid(), memoType.Id, c.BooksBeginFrom,
            new List<EntryLine>
            {
                new(debit.Id, Money.FromRupees(rupees), DrCr.Debit),
                new(credit.Id, Money.FromRupees(rupees), DrCr.Credit),
            }, narration: "Advance to be regularised"));
    }

    /// <summary>Opens the Memorandum Register and puts the highlight on its single memo row.</summary>
    private static ReportRow OpenRegisterOnTheMemo(MainWindowViewModel vm, Window window)
    {
        vm.OpenReport(ReportKind.MemorandumRegister);
        Pump(window);
        var row = vm.Reports!.Rows.Single(r => r.DrillVoucherId != Guid.Empty);
        vm.Reports!.SelectedRow = row;
        Pump(window);
        return row;
    }

    // ======================================================= (1) the row can address its memo at all

    /// <summary>
    /// 🔴 THE ONE-FIELD BLOCKER. <c>MemorandumRegisterRow</c> has always carried <c>VoucherId</c>, but the report
    /// projection dropped it — so every memo row resolved to <see cref="Guid.Empty"/> and NOTHING that works off
    /// <c>Reports.SelectedRow.DrillVoucherId</c> could name one. This is the same "the list row carries no Guid, so
    /// no row can address a unit" shape the census records against the master lists.
    ///
    /// <para>It is asserted as an EQUALITY against the posted memo's id, not merely as "non-empty": a row carrying
    /// some other voucher's id would satisfy the weaker claim and would convert the wrong document.</para>
    /// </summary>
    [AvaloniaFact]
    public void A_memorandum_register_row_resolves_to_the_memorandum_it_displays()
    {
        var (window, vm, dir) = NewWindow("Memo Row Id Co");
        try
        {
            var memo = PostMemo(vm.Company!, 2500m, out _, out _);
            var row = OpenRegisterOnTheMemo(vm, window);

            Assert.Equal(memo.Id, row.DrillVoucherId);
            Assert.True(row.CanDrill);   // …and it therefore joins the ordinary drill/alter verbs for free

            // The total row is NOT addressable — a convert aimed at it must resolve to nothing.
            Assert.All(vm.Reports!.Rows.Where(r => r.IsTotal || r.IsHeader),
                r => Assert.Equal(Guid.Empty, r.DrillVoucherId));
        }
        finally { Cleanup(window, dir); }
    }

    // ======================================================= (2) the KEY ROUTE — bare C on the real window

    /// <summary>
    /// The census defect verbatim was "no key route". This presses the REAL key on the REAL window and requires the
    /// confirmation to come up naming the memo — so it fails on today's main, where bare C on this report does
    /// nothing at all.
    /// </summary>
    [AvaloniaFact]
    public void Bare_C_on_the_memorandum_register_raises_the_conversion_prompt()
    {
        var (window, vm, dir) = NewWindow("Memo Key Route Co");
        try
        {
            var memo = PostMemo(vm.Company!, 2500m, out _, out _);
            OpenRegisterOnTheMemo(vm, window);
            Assert.False(vm.IsAcceptPromptOpen);

            window.KeyPressQwerty(PhysicalKey.C, RawInputModifiers.None);
            Pump(window);

            Assert.True(vm.IsAcceptPromptOpen);
            // The question NAMES the document and the target type, and tells the truth about the books. A prompt
            // that said the books were unaffected would be the exact defect the cancellation prompt was fixed for.
            Assert.Contains("Convert", vm.AcceptPromptText);
            Assert.Contains(vm.Company!.FormatVoucherNumber(memo), vm.AcceptPromptText);
            Assert.Contains("move every balance", vm.AcceptPromptText);
            Assert.Contains("cannot be undone", vm.AcceptPromptText);

            // 🔴 IT ONLY ASKED. Nothing is converted until Y — the memo is still a memo and still off the books.
            Assert.NotNull(vm.Company!.FindVoucher(memo.Id));
        }
        finally { Cleanup(window, dir); }
    }

    // ======================================================= (3) Y actually posts the memo's figures

    /// <summary>
    /// The brief's own bar: <i>"test that the converted voucher posts the figures the memorandum held"</i>, and
    /// <i>"check the edit log records it"</i>. Both are asserted here on the real store.
    ///
    /// <para><b>The balance assertions are the point.</b> A memorandum is excluded from every balance by
    /// <c>LedgerBalances.IsProvisionalBaseType</c>, so before the conversion the two ledgers read ZERO and after it
    /// they read the memo's amount. Asserting only "a voucher now exists" would pass on a conversion that posted
    /// the wrong figures, or none.</para>
    /// </summary>
    [AvaloniaFact]
    public void Y_converts_the_memorandum_posting_the_figures_it_held_and_logs_the_audit_verb()
    {
        var (window, vm, dir) = NewWindow("Memo Convert Co");
        try
        {
            var c = vm.Company!;
            var memo = PostMemo(c, 2500m, out var dr, out var cr);
            var asOf = c.BooksBeginFrom;

            // BEFORE: a memorandum counts for nothing.
            Assert.Equal(0m, LedgerBalances.Closing(c, dr, asOf).Amount.Amount);
            Assert.Equal(0m, LedgerBalances.Closing(c, cr, asOf).Amount.Amount);

            OpenRegisterOnTheMemo(vm, window);
            window.KeyPressQwerty(PhysicalKey.C, RawInputModifiers.None);
            Pump(window);
            Assert.True(vm.IsAcceptPromptOpen);

            window.KeyPressQwerty(PhysicalKey.Y, RawInputModifiers.None);
            Pump(window);

            // The memo is GONE and a real voucher stands in its place, carrying the same date and the same lines.
            Assert.Null(c.FindVoucher(memo.Id));
            var regular = Assert.Single(c.Vouchers.Where(v =>
                c.FindVoucherType(v.TypeId)!.BaseType == VoucherBaseType.Journal));
            Assert.Equal(memo.Date, regular.Date);
            Assert.Equal(2500m, regular.TotalDebit.Amount);
            Assert.False(regular.Optional);

            // AFTER: the figures the memorandum HELD are now on the books, to the paisa and on the right sides.
            var drBalance = LedgerBalances.Closing(c, dr, asOf);
            var crBalance = LedgerBalances.Closing(c, cr, asOf);
            Assert.Equal(2500m, drBalance.Amount.Amount);
            Assert.Equal(DrCr.Debit, drBalance.Side);
            Assert.Equal(2500m, crBalance.Amount.Amount);
            Assert.Equal(DrCr.Credit, crBalance.Side);

            // It is an AUDIT EVENT and is logged under its OWN verb — not as a Delete, which would misdescribe it.
            var entry = Assert.Single(c.VoucherEditLog.Where(e => e.Verb == VoucherEditVerb.ConvertMemorandum));
            Assert.Equal(memo.Id, entry.VoucherId);

            // The register rebuilt: the converted memo has left it, so the operator is not looking at a row whose
            // voucher no longer exists.
            Assert.DoesNotContain(vm.Reports!.Rows, r => r.DrillVoucherId == memo.Id);
        }
        finally { Cleanup(window, dir); }
    }

    // ======================================================= (4) the armed slot dies with its prompt

    /// <summary>
    /// The disarm invariant this project has written down twice already, now with a THIRD armed action on the one
    /// confirmation channel. An armed conversion that outlived its prompt would let a plain "Y" on the next
    /// unrelated Accept confirmation, anywhere in the app, post a memorandum onto the real books.
    ///
    /// <para><b>Mutation-verified:</b> deleting <c>_pendingConvertMemorandumId = Guid.Empty;</c> from
    /// <c>ResetMasterAcceptPrompt</c> reddens exactly this test.</para>
    /// </summary>
    [AvaloniaFact]
    public void A_dismissed_conversion_cannot_be_executed_by_a_later_unrelated_Y()
    {
        var (window, vm, dir) = NewWindow("Memo Disarm Co");
        try
        {
            var c = vm.Company!;
            var memo = PostMemo(c, 900m, out _, out _);

            OpenRegisterOnTheMemo(vm, window);
            window.KeyPressQwerty(PhysicalKey.C, RawInputModifiers.None);
            Pump(window);
            Assert.True(vm.IsAcceptPromptOpen);

            // "N" — the operator declines.
            Assert.True(vm.DismissMasterAccept());
            Assert.False(vm.IsAcceptPromptOpen);
            Assert.NotNull(c.FindVoucher(memo.Id));

            // A LATER, UNRELATED accept confirmation is raised and answered with a bare Y. It must do its own work
            // and NOT inherit the declined conversion.
            vm.ShowLedgerMaster();
            Pump(window);
            vm.RequestMasterAccept();
            Assert.True(vm.ConfirmMasterAccept());

            Assert.NotNull(c.FindVoucher(memo.Id));                       // still a memorandum
            Assert.Empty(c.VoucherEditLog.Where(e => e.Verb == VoucherEditVerb.ConvertMemorandum));
        }
        finally { Cleanup(window, dir); }
    }

    // ======================================================= (5) the chord is SCOPED — it shadows nothing

    /// <summary>
    /// 🔴 The chord is ours, so the case that it costs nothing has to be MADE, not assumed. Bare C is bound on the
    /// Memorandum Register and nowhere else; on another live report it must fall straight through, leaving no
    /// prompt, no notice and nothing converted.
    ///
    /// <para>🔴 <b>THIS TEST WAS A DEAD GUARD AND IS REWRITTEN. The version it replaces asserted the right thing
    /// and could not observe it.</b> It opened the Day Book and pressed C without ever putting the highlight on a
    /// row, so <c>Reports.SelectedRow</c> was null and <c>RequestConvertHighlightedMemorandum</c> returned at its
    /// first gate — which it does with or without the arm's predicate. Measured, not inferred: replacing
    /// <c>vm.IsMemorandumRegisterReport</c> in <c>MainWindow.OnKeyDown</c>'s bare-C arm with <c>true</c> left the
    /// old test GREEN, so the scope guard this file's own comment claimed was mutation-verified was in fact
    /// covered by nothing.</para>
    ///
    /// <para>🔴 <b>And the escape it left open is the serious one.</b> <c>DayBook.Build</c> walks
    /// <c>company.Vouchers</c> with a date filter and NO type filter — memoranda list there like everything else,
    /// and <c>BuildDayBook</c> sets <c>DrillVoucherId</c> on every row. So an unscoped bare C, with the highlight
    /// standing on the memo's own Day Book row, would have converted it: real money onto the real books, from a
    /// screen that offers no such verb and shows no badge for it.</para>
    ///
    /// <para><b>Both halves of the scope are now driven.</b> (a) the highlight on an ORDINARY voucher's row — the
    /// mutation is caught by the refusal <see cref="MainWindowViewModel.Notice"/> it would have to raise; (b) the
    /// highlight on the MEMORANDUM's own row — the mutation is caught by the conversion actually happening.
    /// <b>Mutation-verified:</b> that same <c>true</c> substitution now reddens this test on case (b).</para>
    /// </summary>
    [AvaloniaFact]
    public void Bare_C_on_another_report_is_not_claimed_by_the_conversion_arm()
    {
        var (window, vm, dir) = NewWindow("Memo Scope Co");
        try
        {
            var c = vm.Company!;
            var memo = PostMemo(c, 400m, out _, out _);

            // An ORDINARY voucher too, so the Day Book carries a row of each kind.
            var jDr = AddCashLedger(c, "Plain Dr", openingIsDebit: true);
            var jCr = AddCashLedger(c, "Plain Cr", openingIsDebit: false);
            var journalType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Journal && t.IsActive);
            var plain = new LedgerService(c).Post(new Voucher(Guid.NewGuid(), journalType.Id, c.BooksBeginFrom,
                new List<EntryLine>
                {
                    new(jDr.Id, Money.FromRupees(111m), DrCr.Debit),
                    new(jCr.Id, Money.FromRupees(111m), DrCr.Credit),
                }, narration: "An ordinary journal"));

            vm.OpenReport(ReportKind.DayBook);
            Pump(window);
            Assert.False(vm.IsMemorandumRegisterReport);

            // (a) highlight an ORDINARY voucher. An arm that fired here would have to refuse it out loud.
            vm.Reports!.SelectedRow = vm.Reports!.Rows.Single(r => r.DrillVoucherId == plain.Id);
            Pump(window);
            vm.Notice = string.Empty;

            window.KeyPressQwerty(PhysicalKey.C, RawInputModifiers.None);
            Pump(window);

            Assert.False(vm.IsAcceptPromptOpen);
            Assert.Equal(string.Empty, vm.Notice);

            // (b) 🔴 THE CASE THE OLD TEST COULD NOT REACH — the highlight on the MEMORANDUM itself, on a report
            // that lists it and is not the register. An unscoped arm converts it here.
            var memoRow = vm.Reports!.Rows.Single(r => r.DrillVoucherId == memo.Id);
            vm.Reports!.SelectedRow = memoRow;
            Pump(window);

            window.KeyPressQwerty(PhysicalKey.C, RawInputModifiers.None);
            Pump(window);

            Assert.False(vm.IsAcceptPromptOpen);
            Assert.NotNull(c.FindVoucher(memo.Id));           // still a memorandum, still off the books
            Assert.Empty(c.VoucherEditLog.Where(e => e.Verb == VoucherEditVerb.ConvertMemorandum));
        }
        finally { Cleanup(window, dir); }
    }

    // ======================================================= (6) the badge appears with the key and not without it

    /// <summary>
    /// The badge and the key must appear and disappear TOGETHER. An always-present "Convert Memo" badge would
    /// advertise a key that is genuinely dead on every other screen (register defect IV-31); an always-absent one
    /// would hide the only discoverable trace of a chord no vendor page documents.
    ///
    /// <para>Asserted against the REALISED VISUAL TREE — the rendered button captions — rather than against the
    /// <c>ButtonBar</c> collection, so it holds the view accountable and not just the view model.</para>
    /// </summary>
    [AvaloniaFact]
    public void The_convert_badge_is_shown_on_the_register_and_absent_everywhere_else()
    {
        var (window, vm, dir) = NewWindow("Memo Badge Co");
        try
        {
            PostMemo(vm.Company!, 700m, out _, out _);

            vm.OpenReport(ReportKind.DayBook);
            Pump(window);
            Assert.DoesNotContain("Convert Memo", RenderedButtonCaptions(window));

            OpenRegisterOnTheMemo(vm, window);
            Assert.Contains("Convert Memo", RenderedButtonCaptions(window));

            // …and the badge runs the IDENTICAL door the key runs, so the two cannot drift apart.
            var badge = vm.ButtonBar.Single(b => b.Caption == "Convert Memo");
            Assert.Equal("C", badge.Key);
            badge.Action();
            Assert.True(vm.IsAcceptPromptOpen);
        }
        finally { Cleanup(window, dir); }
    }

    private static List<string> RenderedButtonCaptions(Window window) =>
        Descendants(window).OfType<TextBlock>()
            .Select(tb => tb.Text ?? string.Empty)
            .ToList();
}
