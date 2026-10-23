using System;
using System.Collections.Generic;
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
using Domain = Apex.Ledger.Domain;

namespace Apex.Desktop.Tests;

/// <summary>
/// 🔴 <b>CENSUS 12.7's EXPORT ARM — ASSERTED ON THE EMITTED PDF BYTES OF EVERY FILE THE JOB WRITES.</b>
///
/// <para>Nothing here asserts a <see cref="PrintReport"/> field or a view-model property that the test itself
/// just watched something set. Every claim is read out of the Latin-1 bytes that actually left
/// <see cref="ReportPdf"/> and were handed to the write seam, keyed by the FILE NAME the operator will see in
/// his folder. That is the project's own scar twice over: a sibling branch was withheld because its figures did
/// not survive to the PDF, and this file's subject — a letter posted to a counterparty — is the document class
/// on which "the model was right" is worth nothing.</para>
///
/// <para><b>GROUNDING (R7, ruling 14 — vendor documentation, opened BY CONTENT 2026-10-23).</b>
/// <c>help.tallysolutions.com/printing-multi-account-reports</c> ("Configure for Print, Export, E-mail &amp;
/// WhatsApp"), under <i>Configure multi-account reports</i>: <i>"For Export: <b>Alt+E</b> (Export) &gt;
/// <b>Others</b> &gt; select a report from the List of Reports."</i> The same page's opening sentence —
/// <i>"Before printing, exporting, or sharing documents, like your invoices, financial reports, payment
/// reminder letters,"</i> — attests that a payment reminder letter is a document the reference product exports.
/// <b>That is the whole of the fidelity claim.</b> The one-file-per-account shape, the file-name stem and the
/// PDF-only format are OURS (<c>docs/invented-vs-cloned.md</c> IV-162, IV-163) and the tests below say so where
/// they lock them.</para>
/// </summary>
public sealed class MultiAccountExportTests
{
    private static readonly DateOnly FyStart = new(2025, 4, 1);
    private static readonly DateOnly BillDate = new(2025, 5, 10);
    private static readonly DateOnly AsOf = new(2026, 3, 31);

    private static string AsLatin1(byte[] bytes) => Encoding.Latin1.GetString(bytes);

    // ---------------------------------------------------------------- books

    private static Company Books(string companyName = "Apex Retail")
        => CompanyFactory.CreateSeeded(companyName, FyStart);

    private static Domain.Ledger Debtor(Company c, string name, decimal opening = 0m)
    {
        var l = new Domain.Ledger(
            Guid.NewGuid(), name, c.FindGroupByName("Sundry Debtors")!.Id,
            Money.FromRupees(opening), openingIsDebit: true)
        {
            // The reminder letter is built from Outstandings.OpenBillsFor, so the party has to be able to carry
            // bills at all; without this the validator refuses the fixture rather than the letter being wrong.
            MaintainBillByBill = true,
        };
        c.AddLedger(l);
        return l;
    }

    /// <summary>A revenue ledger to credit, created once per book.</summary>
    private static Domain.Ledger Sales(Company c)
    {
        if (c.FindLedgerByName("Sales A/c") is { } existing) return existing;
        var l = new Domain.Ledger(
            Guid.NewGuid(), "Sales A/c", c.FindGroupByName("Sales Accounts")!.Id,
            Money.Zero, openingIsDebit: false);
        c.AddLedger(l);
        return l;
    }

    /// <summary>
    /// Posts Dr party (one New-Ref bill) / Cr Sales straight onto the aggregate, so the party owns exactly one
    /// open bill with a reference nobody else in the book carries. The reference is the needle the cross-party
    /// assertions look for.
    /// </summary>
    private static void PostBill(Company c, Domain.Ledger party, string reference, decimal amount)
    {
        var sales = Sales(c);
        var type = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales);
        var money = Money.FromRupees(amount);

        var partyLine = new EntryLine(
            party.Id, money, DrCr.Debit,
            billAllocations: new[]
            {
                new BillAllocation(BillRefType.NewRef, reference, money, dueDate: BillDate.AddDays(30)),
            });
        var salesLine = new EntryLine(sales.Id, money, DrCr.Credit);

        new LedgerService(c).Post(new Voucher(
            Guid.NewGuid(), type.Id, BillDate, new[] { partyLine, salesLine }, number: 1, partyId: party.Id));
    }

    // ---------------------------------------------------------------- the job under test

    /// <summary>
    /// Runs a real export job through the real panel pair and returns what the write seam RECEIVED: file name
    /// to emitted bytes. No disk is touched, and nothing is read back from the view model.
    /// </summary>
    private static Dictionary<string, byte[]> RunExport(
        Company c, MultiAccountDocumentKind kind, out MultiAccountExportViewModel export,
        bool appendTimestamp = false, Func<MultiAccountRowViewModel, bool>? select = null)
    {
        var panel = new MultiAccountPrintViewModel(c, FyStart, AsOf) { DocumentKind = kind };
        foreach (var row in panel.Accounts)
            row.IsSelected = select is null || select(row);

        var documents = panel.BuildExportJob();

        var written = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        export = new MultiAccountExportViewModel(
            panel.JobTitle, () => documents, folder: "X:/out",
            now: new DateTime(2026, 10, 23, 14, 30, 0),
            writeBytes: (path, bytes) => written[Path.GetFileName(path)] = bytes)
        {
            AppendTimestamp = appendTimestamp,
        };

        export.Apply();
        return written;
    }

    // ================================================================ 1 — the cross-party safety property

    /// <summary>
    /// 🔴 <b>THE HEADLINE PROPERTY OF THIS WHOLE TRACK: each exported file contains the party it is named for
    /// and NOBODY ELSE'S FIGURES.</b>
    ///
    /// <para>A reminder letter is a document an operator attaches to a mail addressed to one counterparty. This
    /// application has already SHARED A WHOLE REGISTER TO A THIRD PARTY once when a single voucher was meant, so
    /// "the right rows are in the job somewhere" is not the property worth proving — "Acme's file cannot contain
    /// Bharat's bill" is. Asserted on the bytes of each file, keyed by the file name.</para>
    ///
    /// <para><b>MUTATION THAT REDDENS IT:</b> change <c>MultiAccountExportViewModel.Apply</c> to render the whole
    /// document SET into one file (<c>ReportPdf.Render(documents.Select(d =&gt; d.Report).ToList(), …)</c>) —
    /// i.e. adopt the combined-file design this panel deliberately refused. Every file then contains both
    /// parties and the four <c>DoesNotContain</c> assertions below fail.</para>
    /// </summary>
    [Fact]
    public void Each_exported_file_carries_its_OWN_party_and_no_other_partys_figures()
    {
        var c = Books();
        var acme = Debtor(c, "Acme Traders");
        var bharat = Debtor(c, "Bharat Heavy Electricals");
        PostBill(c, acme, "INV-ACME-7", 11_111m);
        PostBill(c, bharat, "INV-BHARAT-9", 22_222m);

        var written = RunExport(c, MultiAccountDocumentKind.ReminderLetter, out _);

        Assert.Equal(2, written.Count);
        var acmeFile = Assert.Single(written.Keys, k => k.Contains("Acme", StringComparison.Ordinal));
        var bharatFile = Assert.Single(written.Keys, k => k.Contains("Bharat", StringComparison.Ordinal));

        var acmePage = AsLatin1(written[acmeFile]);
        var bharatPage = AsLatin1(written[bharatFile]);

        // Each letter names its OWN addressee and carries its OWN bill and figure ...
        Assert.Contains("Acme Traders", acmePage, StringComparison.Ordinal);
        Assert.Contains("INV-ACME-7", acmePage, StringComparison.Ordinal);
        Assert.Contains("11,111.00", acmePage, StringComparison.Ordinal);
        Assert.Contains("Bharat Heavy Electricals", bharatPage, StringComparison.Ordinal);
        Assert.Contains("INV-BHARAT-9", bharatPage, StringComparison.Ordinal);
        Assert.Contains("22,222.00", bharatPage, StringComparison.Ordinal);

        // ... and NEITHER carries a trace of the other. This is the assertion the track exists for.
        Assert.DoesNotContain("Bharat", acmePage, StringComparison.Ordinal);
        Assert.DoesNotContain("INV-BHARAT-9", acmePage, StringComparison.Ordinal);
        Assert.DoesNotContain("Acme", bharatPage, StringComparison.Ordinal);
        Assert.DoesNotContain("INV-ACME-7", bharatPage, StringComparison.Ordinal);
    }

    /// <summary>
    /// The same property for the CONFIRMATION OF ACCOUNTS, which is the other document of row 12.7 that leaves
    /// the building. Its figure comes from the closing balance rather than from bills, so it is a genuinely
    /// different projection and not a re-run of the test above with one enum changed.
    /// </summary>
    [Fact]
    public void A_confirmation_of_accounts_is_exported_per_party_with_only_that_partys_balance()
    {
        var c = Books();
        Debtor(c, "Acme Traders", opening: 31_313m);
        Debtor(c, "Bharat Heavy Electricals", opening: 47_474m);

        var written = RunExport(c, MultiAccountDocumentKind.ConfirmationOfAccounts, out _);

        Assert.Equal(2, written.Count);
        var acmePage = AsLatin1(written[Assert.Single(written.Keys, k => k.Contains("Acme", StringComparison.Ordinal))]);
        var bharatPage = AsLatin1(written[Assert.Single(written.Keys, k => k.Contains("Bharat", StringComparison.Ordinal))]);

        Assert.Contains("31,313.00", acmePage, StringComparison.Ordinal);
        Assert.DoesNotContain("47,474.00", acmePage, StringComparison.Ordinal);

        Assert.Contains("47,474.00", bharatPage, StringComparison.Ordinal);
        Assert.DoesNotContain("31,313.00", bharatPage, StringComparison.Ordinal);
    }

    /// <summary>
    /// The file an operator picks out of the folder is the file addressed to the party whose name is on it.
    ///
    /// <para><b>MUTATION THAT REDDENS IT:</b> drop the account from the stem in
    /// <c>MultiAccountExportViewModel.FileNameFor</c> (return the document title alone). Both letters then land
    /// on "Reminder Letter" and "Reminder Letter (2)", the name no longer identifies the party, and the lookups
    /// below throw.</para>
    /// </summary>
    [Fact]
    public void The_file_name_identifies_the_party_whose_letter_is_inside_it()
    {
        var c = Books();
        PostBill(c, Debtor(c, "Acme Traders"), "INV-ACME-7", 11_111m);
        PostBill(c, Debtor(c, "Bharat Heavy Electricals"), "INV-BHARAT-9", 22_222m);

        var written = RunExport(c, MultiAccountDocumentKind.ReminderLetter, out _);

        Assert.Contains("Reminder Letter - Acme Traders.pdf", written.Keys);
        Assert.Contains("Reminder Letter - Bharat Heavy Electricals.pdf", written.Keys);

        // The names are not merely present — each file's BYTES are the letter to the party it names.
        Assert.Contains("Acme Traders",
            AsLatin1(written["Reminder Letter - Acme Traders.pdf"]), StringComparison.Ordinal);
        Assert.Contains("Bharat Heavy Electricals",
            AsLatin1(written["Reminder Letter - Bharat Heavy Electricals.pdf"]), StringComparison.Ordinal);
    }

    /// <summary>
    /// Two different accounts whose names SANITISE to one stem still produce two files. Without the uniqueness
    /// pass the second <c>WriteAllBytes</c> overwrites the first party's letter and the operator is left with
    /// one file where he counted two, with no indication whose went missing.
    ///
    /// <para><b>MUTATION THAT REDDENS IT:</b> delete the <c>while (!used.Add(candidate))</c> loop in
    /// <c>FileNameFor</c>. The two accounts collide on one key and <c>written.Count</c> is 1.</para>
    /// </summary>
    [Fact]
    public void Two_accounts_that_sanitise_to_one_file_name_still_produce_two_files()
    {
        var c = Books();
        // '/' is an invalid file-name char and maps to '_', so these two collide on the stem.
        Debtor(c, "A/c Holdings", opening: 1_000m);
        Debtor(c, "A_c Holdings", opening: 2_000m);

        var written = RunExport(c, MultiAccountDocumentKind.ConfirmationOfAccounts, out _);

        Assert.Equal(2, written.Count);
        var pages = written.Values.Select(AsLatin1).ToArray();
        Assert.Contains(pages, p => p.Contains("1,000.00", StringComparison.Ordinal));
        Assert.Contains(pages, p => p.Contains("2,000.00", StringComparison.Ordinal));
    }

    // ================================================================ 2 — the non-ASCII fold, end to end

    /// <summary>
    /// 🔴 <b>THE RUPEE SIGN AND A NON-LATIN PARTY NAME REACH THE PAGE AS THE FOLD'S OUTPUT AND NEVER AS THE PDF
    /// WRITER'S '?' FALLBACK</b> — on a document that is posted to a third party and therefore cannot carry a
    /// row of question marks where a name belongs.
    ///
    /// <para>This asserts the fold's CONTRACT, not a wish: <c>ReportPrintProjector.Ascii</c> maps U+20B9 to
    /// "Rs." and maps a character the standard-14 faces cannot draw (there is no font embedding — census 12.8)
    /// to '-' deterministically, precisely so the writer's '?' never lands on paper. The export path renders the
    /// SAME projected <see cref="PrintReport"/> the print path does, so it inherits that fold rather than
    /// re-implementing it; a future export path that built its own rows would fail here.</para>
    ///
    /// <para><b>MUTATION THAT REDDENS IT:</b> in <c>MultiAccountPrintProjector.ReminderLetter</c>, drop the
    /// <c>ReportPrintProjector.Ascii</c> call around <c>ledger.Name</c>. The Devanagari then reaches
    /// <c>PdfWriter</c> unfolded and the page gains the '?' this test forbids.</para>
    /// </summary>
    [Fact]
    public void A_rupee_sign_and_a_non_Latin_name_fold_onto_the_page_and_never_reach_it_as_a_question_mark()
    {
        var c = Books();
        // U+20B9 in the master name, and a Devanagari word the standard-14 faces have no glyph for.
        var party = Debtor(c, "\u20B9 \u0938\u0941\u0928\u0940\u0932 Traders");
        PostBill(c, party, "INV-1", 5_000m);

        var written = RunExport(c, MultiAccountDocumentKind.ReminderLetter, out _);
        var page = AsLatin1(Assert.Single(written.Values));

        // The rupee sign took the fold's own branch ...
        Assert.Contains("Rs.", page, StringComparison.Ordinal);
        // ... the unmapped script took the deterministic hyphen branch, ONE hyphen per unrenderable char
        // (the fixture's Devanagari is five code units), so the whole addressee reads as one folded line ...
        Assert.Contains("Rs. ----- Traders", page, StringComparison.Ordinal);
        // ... and the writer's fallback never appeared on a document addressed to a counterparty.
        Assert.DoesNotContain("?", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// The other half of the same seam, and the half a blanket "fold everything above ASCII" would silently
    /// break: a WinAnsi-representable party name survives to the page AS ITSELF, because the fold ASKS
    /// <see cref="PdfWriter.CanRender"/> rather than guessing. An accented counterparty has their own name on
    /// the letter we post them.
    ///
    /// <para><b>MUTATION THAT REDDENS IT:</b> replace the <c>else if (PdfWriter.CanRender(c))</c> arm of
    /// <c>ReportPrintProjector.Ascii</c> with an unconditional <c>sb.Append('-')</c> — i.e. route around
    /// <c>CanRender</c>. "Müller" becomes "M-ller" and the assertion fails.</para>
    /// </summary>
    [Fact]
    public void A_WinAnsi_party_name_survives_to_the_exported_page_as_itself_through_CanRender()
    {
        const string Name = "M\u00FCller Handels Gmb\u00DF";   // u-umlaut and sharp s: both WinAnsi
        foreach (var ch in Name)
            Assert.True(ch <= 126 || PdfWriter.CanRender(ch),
                "the fixture must be made of characters the writer CAN render, or this test proves nothing.");

        var c = Books();
        PostBill(c, Debtor(c, Name), "INV-1", 9_000m);

        var written = RunExport(c, MultiAccountDocumentKind.ReminderLetter, out _);
        var page = AsLatin1(Assert.Single(written.Values));

        Assert.Contains(Name, page, StringComparison.Ordinal);
        Assert.DoesNotContain("?", page, StringComparison.Ordinal);
    }

    // ================================================================ 3 — refusals

    /// <summary>
    /// An empty selection writes NOTHING and says why. A job of nothing is a mistake to report, not a set of
    /// blank sheets to leave in the operator's folder.
    /// </summary>
    [Fact]
    public void An_empty_selection_writes_no_file_and_says_so()
    {
        var c = Books();
        PostBill(c, Debtor(c, "Acme Traders"), "INV-ACME-7", 11_111m);

        var written = RunExport(c, MultiAccountDocumentKind.ReminderLetter, out var export, select: _ => false);

        Assert.Empty(written);
        Assert.False(export.Apply());
        Assert.Contains("nothing to export", export.Status, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 🔴 A BLANK FOLDER WRITES NOTHING. <c>Path.Combine("", name)</c> collapses to a bare file name, so without
    /// this guard a cleared folder box scatters letters naming counterparties into the process working
    /// directory, where the operator will never think to clear them.
    ///
    /// <para><b>MUTATION THAT REDDENS IT:</b> delete the <c>string.IsNullOrWhiteSpace(Folder)</c> guard from
    /// <c>Apply</c>. It then returns true and the seam receives a bare "Reminder Letter - Acme Traders.pdf"
    /// with no directory at all.</para>
    /// </summary>
    [Fact]
    public void A_blank_folder_is_refused_rather_than_writing_bare_names_into_the_working_directory()
    {
        var c = Books();
        PostBill(c, Debtor(c, "Acme Traders"), "INV-ACME-7", 11_111m);

        var panel = new MultiAccountPrintViewModel(c, FyStart, AsOf)
        {
            DocumentKind = MultiAccountDocumentKind.ReminderLetter,
        };
        panel.SelectAll();
        var documents = panel.BuildExportJob();
        Assert.NotEmpty(documents);

        var written = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var export = new MultiAccountExportViewModel(
            panel.JobTitle, () => documents, folder: "   ",
            now: new DateTime(2026, 10, 23, 14, 30, 0),
            writeBytes: (path, bytes) => written[path] = bytes);

        Assert.False(export.Apply());
        Assert.Empty(written);
        Assert.Contains("folder", export.Status, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The timestamp toggle reaches the FILE NAME, and the panel's preview list says so before Apply runs. The
    /// preview and the write share <c>FileNameFor</c>, so they cannot promise one name and write another.
    /// </summary>
    [Fact]
    public void The_timestamp_toggle_moves_both_the_previewed_name_and_the_written_one()
    {
        var c = Books();
        PostBill(c, Debtor(c, "Acme Traders"), "INV-ACME-7", 11_111m);

        var written = RunExport(c, MultiAccountDocumentKind.ReminderLetter, out var export, appendTimestamp: true);

        var name = Assert.Single(written.Keys);
        Assert.Equal("Reminder Letter - Acme Traders_20261023-1430.pdf", name);
        Assert.Equal(name, Assert.Single(export.PlannedFileNames));
    }

    // ================================================================ 4 — the vendor's route, end to end

    /// <summary>
    /// 🔴 <b>THE VENDOR'S OWN EXPORT ROUTE, WALKED THROUGH THE REAL KEY HANDLER.</b>
    /// <c>help.tallysolutions.com/printing-multi-account-reports</c>: <i>"For Export: <b>Alt+E</b> (Export) &gt;
    /// <b>Others</b> &gt; select a report from the List of Reports."</i>
    ///
    /// <para>The row is activated by its painted hot key off the LIVE column, not by calling the opener, so this
    /// bites if the menu stops offering the row or the row stops being wired.</para>
    ///
    /// <para><b>MUTATION THAT REDDENS IT:</b> remove the <c>others:</c> argument from the
    /// <c>ReportExportMenu.BuildColumn</c> call in <c>MainWindowViewModel.OpenExportMenu</c> (and the parameter)
    /// — the Others row disappears and the first assertion fails.</para>
    /// </summary>
    [AvaloniaFact]
    public void Alt_E_offers_Others_and_it_reaches_the_multi_account_panel()
    {
        var dir = Path.Combine(Path.GetTempPath(), "apex-mae-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var vm = new MainWindowViewModel(new CompanyStorage(dir));
            vm.LoadRobertDemo();
            var window = new MainWindow { DataContext = vm, Width = 1280, Height = 720 };
            window.Show();
            Pump(window);

            vm.OpenReport(ReportKind.TrialBalance);
            Pump(window);
            Assert.True(vm.IsExportablePage);

            window.KeyPressQwerty(PhysicalKey.E, RawInputModifiers.Alt);
            Pump(window);
            Assert.Equal(Screen.ExportMenu, vm.CurrentScreen);

            var others = vm.Columns[^1].Items
                .FirstOrDefault(i => i.IsSelectable && i.Label == ReportExportMenu.OthersVerb);
            Assert.True(others is not null,
                "Alt+E offers no Others row, so the vendor's multi-account export route does not exist.");

            others!.Activate();
            Pump(window);

            Assert.Equal(Screen.MultiAccountPrint, vm.CurrentScreen);
            Assert.NotNull(vm.MultiAccountPrint);
        }
        finally { TryDelete(dir); }
    }

    /// <summary>
    /// 🔴 <b>THE LAST MILE — a realised Export button and a realised file list, on the real window.</b>
    ///
    /// <para>T2-40 was filed on this exact pair of types shipping complete, correct and reachable by nobody. A
    /// view-model test would stay green with the <c>DataTemplate</c> deleted, so this one realises the shipped
    /// <see cref="MainWindow"/>, opens the column the way the operator does, and asks the REALISED controls what
    /// they offer. It also exercises <c>Ctrl+A</c> through the window's own handler, which needed its own arm:
    /// <c>ApplyExport()</c> dereferences <c>ExportPanel</c>, null on this screen.</para>
    ///
    /// <para><b>MUTATION THAT REDDENS IT:</b> delete the <c>MultiAccountExportViewModel</c> <c>DataTemplate</c>
    /// from <c>MainWindow.axaml</c>. The column still opens and the view model is still correct, and this test
    /// fails because no realised Export button exists.</para>
    /// </summary>
    [AvaloniaFact]
    public void The_export_column_realises_a_file_list_and_an_Export_button_that_writes()
    {
        var dir = Path.Combine(Path.GetTempPath(), "apex-mae-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var vm = new MainWindowViewModel(new CompanyStorage(dir));
            vm.LoadRobertDemo();
            vm.OpenMultiAccountPrint();
            var panel = vm.MultiAccountPrint!;
            panel.DocumentKind = MultiAccountDocumentKind.ConfirmationOfAccounts;
            panel.SelectAll();
            Assert.True(panel.SelectedCount > 0,
                "the Robert fixture offers no party account, so this test would prove nothing.");

            var window = new MainWindow { DataContext = vm, Width = 1280, Height = 800 };
            window.Show();
            Pump(window);

            vm.ExportMultiAccountJob();
            Pump(window);

            Assert.Equal(Screen.MultiAccountExport, vm.CurrentScreen);
            var export = vm.MultiAccountExportPanel;
            Assert.NotNull(export);

            // The file list is on screen, one realised row per file, bound to the panel's own names.
            var shown = window.GetVisualDescendants()
                              .OfType<TextBlock>()
                              .Select(t => t.Text ?? string.Empty)
                              .Where(t => t.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                              .ToArray();
            Assert.NotEmpty(shown);
            foreach (var name in export!.PlannedFileNames) Assert.Contains(name, shown);

            // The Export button exists, is realised over THIS panel, and its click handler writes.
            var button = window.GetVisualDescendants()
                               .OfType<Button>()
                               .FirstOrDefault(b => b.DataContext is MultiAccountExportViewModel
                                                 && (b.Content as string) == "Export (Ctrl+A)");
            Assert.True(button is not null,
                "no realised Export button over the export panel — the column is unreachable by mouse.");

            // Write into the temp folder through the REAL path (no seam), then prove the files exist on disk.
            export.Folder = dir;
            window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
            Pump(window);

            var onDisk = Directory.GetFiles(dir, "*.pdf").Select(Path.GetFileName).ToArray();
            Assert.NotEmpty(onDisk);
            foreach (var name in export.PlannedFileNames) Assert.Contains(name, onDisk);
            Assert.Contains("Exported", export.Status, StringComparison.Ordinal);
        }
        finally { TryDelete(dir); }
    }

    // ================================================================ 5 — the disclosure must stay true

    /// <summary>
    /// 🔴 The Alt+E menu's honest disclosure must not keep naming a row the menu now OFFERS. An under-claim a
    /// reader can check against the menu and find contradicted is how the other disclosures stop being trusted.
    ///
    /// <para>Both directions are asserted, so this is equally red if someone removes the Others row and leaves
    /// the string, or adds bulk Masters/Transactions export and leaves the string.</para>
    /// </summary>
    [Fact]
    public void The_export_menu_disclosure_agrees_with_the_rows_the_menu_actually_offers()
    {
        var offered = ReportExportMenu.OfferedVerbs;
        var disclosure = ReportExportMenu.Disclosure;

        Assert.Contains(ReportExportMenu.OthersVerb, offered);
        Assert.DoesNotContain("Others", disclosure, StringComparison.OrdinalIgnoreCase);

        // The two families that really are absent are still named.
        Assert.Contains("Masters", disclosure, StringComparison.Ordinal);
        Assert.Contains("Transactions", disclosure, StringComparison.Ordinal);
        Assert.DoesNotContain(offered, v => v is "Masters" or "Transactions");
    }

    // ---------------------------------------------------------------- harness

    private static void Pump(Window w)
    {
        Dispatcher.UIThread.RunJobs();
        w.Measure(new Size(w.Width, w.Height));
        w.Arrange(new Rect(0, 0, w.Width, w.Height));
        Dispatcher.UIThread.RunJobs();
    }

    private static void TryDelete(string dir)
    {
        try { Directory.Delete(dir, recursive: true); } catch { /* a temp folder is not worth failing a test */ }
    }
}
