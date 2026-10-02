using System;
using System.Collections.Generic;
using System.Linq;
using Apex.Desktop.Services;
using Apex.Desktop.Tests.Fixtures;
using Apex.Desktop.ViewModels;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Io;
using Apex.Ledger.Services;
using Xunit;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.Tests;

/// <summary>
/// 🔴 THE GUARD ON THE COLUMN CAPTIONS OF EVERY REPORT THAT LEAVES THE BUILDING.
///
/// <para>A report's printed page and its spreadsheet are the copies that go to a bank, an auditor or a tax
/// officer, and before this file three separate defects rode out on them with a green suite:</para>
/// <list type="number">
///   <item><b>Entirely blank pages.</b> <c>ReportsViewModel.IsAccountingReport</c> is a NEGATION over the
///   report families, so VAT Computation and the two CST declaration-forms reports — in none of the family
///   lists — fell into the accounting branch. Both projectors then read <c>Particulars</c>/<c>Amount</c>,
///   which those builders never set, so the documents printed and exported with every cell blank, row count
///   and totals intact. Nothing failed.</item>
///   <item><b>A money column silently dropped.</b> Batchwise, Batch Age Analysis and Price List keep their
///   last money column (Value / Net Rate) in <see cref="ReportRow.Secondary"/>, and the generic projectors
///   only ever read <c>Col1..Col8</c> — so that column was on screen and absent from every document.</item>
///   <item><b>Blank headings.</b> Export captioned 16 of the 37 generic kinds; print captioned <b>none</b>.</item>
/// </list>
///
/// <para><b>Why these tests assert the PROJECTED ARTEFACT, not a map's size.</b> A kind added to a caption
/// table while still emitting blanks is not fixed, and counting entries in a table cannot tell the two apart.
/// Every assertion below runs a real builder over a real company, projects it through the real
/// <see cref="ReportPrintProjector"/> / <see cref="ReportTabularProjector"/>, and reads the columns and cells
/// that actually come out.</para>
/// </summary>
public sealed class ReportColumnBandCoverageTests
{
    // ===================================================================== the structural guard

    public static TheoryData<ReportKind> AllKinds()
    {
        var data = new TheoryData<ReportKind>();
        foreach (var k in Enum.GetValues<ReportKind>()) data.Add(k);
        return data;
    }

    /// <summary>
    /// 🔴 THE TEST THAT STOPS THIS CLUSTER SILENTLY RE-OPENING. Every report kind that renders through the
    /// generic <c>Col1..Col8</c> cells must DECLARE a column band. A new <see cref="ReportKind"/> added to a
    /// builder and forgotten in <see cref="ReportColumnBands"/> fails here by name, at the moment it ships,
    /// rather than printing a blank header band that only a customer ever sees.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllKinds))]
    public void Every_kind_on_the_generic_cell_path_declares_a_column_band(ReportKind kind)
    {
        var vm = new ReportsViewModel(VatEnabledCompany(), kind);

        if (!ReportColumnBands.NeedsBand(vm)) return; // accounting or wide-matrix: carries its own captions

        var band = ReportColumnBands.For(vm);
        Assert.True(
            band.Count > 0,
            $"{kind} renders through the generic Col1..Col8 cells but declares NO column band, so its printed "
            + "header band and its exported header row are both BLANK. Add it to ReportColumnBands.For — "
            + "transcribing the captions from its own colHdr headers in MainWindow.axaml and the source cell "
            + "from its builder, NOT by guessing a plausible heading.");

        Assert.All(band, c => Assert.False(
            string.IsNullOrWhiteSpace(c.Caption),
            $"{kind} declares a band with a blank caption, which is the defect the band exists to remove."));
    }

    /// <summary>
    /// The converse, and the one that would have caught the blank-page defect: for every kind, no cell a
    /// builder POPULATED may be missing from the projected document. This compares the set of non-empty
    /// generic cells the builder wrote against the set of non-empty cells the export actually carries — so a
    /// report routed down the wrong projector branch (VAT / CST), or one whose money column lives somewhere
    /// the projector never reads (Batchwise / Price List), fails here naming the text that went missing.
    ///
    /// <para>🔴 <b>IT DELIBERATELY DOES NOT SKIP THE ACCOUNTING BRANCH, AND THAT IS THE WHOLE POINT OF THIS
    /// TEST RATHER THAN A NEAR-DUPLICATE OF THE ONE ABOVE.</b> An earlier draft gated itself on
    /// <see cref="ReportColumnBands.NeedsBand"/>, which is built on the SAME
    /// <see cref="ReportsViewModel.IsAccountingReport"/> negation that caused the blank-page defect — so the
    /// guard could not see the very family it was written for: a kind wrongly claimed by the negation was
    /// skipped by the assertion that would have caught it. Every kind is swept here except the dynamic-matrix
    /// and payslip ones, whose documents are built from a DIFFERENT row collection
    /// (<c>PayrollRows</c>), so a row-index comparison against <c>vm.Rows</c> would be meaningless rather than
    /// merely permissive. Measured on this fixture: 67 of the 93 kinds are swept, and every accounting-family
    /// kind among them populates zero generic cells today — which is exactly the fact this test pins, so the
    /// day a builder starts writing <c>Col</c> cells for a family still inside the negation, it reddens.</para>
    /// </summary>
    [Theory]
    [MemberData(nameof(AllKinds))]
    public void No_populated_cell_is_dropped_from_the_exported_document(ReportKind kind)
    {
        var vm = new ReportsViewModel(VatEnabledCompany(), kind);
        if (vm.IsPayrollMatrix || vm.IsPayslipReport) return;
        if (vm.Rows.Count == 0) return;

        var export = ReportTabularProjector.Project(vm);

        // Row-for-row, or the index comparison below is comparing two different documents. Asserted rather
        // than assumed so a projector that ever drops or folds rows fails by name instead of throwing.
        Assert.Equal(vm.Rows.Count, export.Rows.Count);

        foreach (var (row, i) in vm.Rows.Select((r, i) => (r, i)))
        {
            var written = GenericCells(row);
            if (written.Count == 0) continue;

            var cells = export.Rows[i].Cells;
            var lost = written.Where(w => !Carried(cells, w)).ToList();
            Assert.True(
                lost.Count == 0,
                $"{kind} row {i}: the builder populated {string.Join(" | ", lost)} but the EXPORTED document "
                + "does not carry it. Either the kind is routed down the wrong projector branch (the "
                + "IsAccountingReport negation), or its band does not name the cell the builder writes to.");
        }
    }

    // ================================================= the accounting family's dropped label suffix (T1-19 (3))

    /// <summary>
    /// 🔴 FAILS ON TODAY'S MAIN, ON EVERY ACCOUNTING REPORT THAT USES <see cref="ReportRow.Secondary"/>.
    ///
    /// <para>The accounting family takes no column band, so the two tests above skip it — and that is where the
    /// third defect of T1-19 lives, the one the census recorded after correctly REFUTING the prediction that
    /// banking rows 8.5–8.10 carried the blank-heading defect. Their printed and exported copies do carry real
    /// captions. What they lose is a cell: on screen the label column is <c>Particulars</c> followed by
    /// <c>Secondary</c> in grey (two runs of one <c>TextBlock</c>), and both projectors emitted only the first.
    /// So the Cheque Register's reconciled/unreconciled counts, the Deposit Slip's and e-Payments' voucher
    /// numbers and the Ledger Monthly Summary's opening and closing MONEY were on screen and on no document.</para>
    ///
    /// <para>Swept over every kind rather than a named list, so a builder that starts using <c>Secondary</c>
    /// tomorrow is covered without editing this test. Both egress paths are asserted, because they had two
    /// separate hand-rolled copies of this logic and only the export twin was ever tested.</para>
    /// </summary>
    [Theory]
    [MemberData(nameof(AllKinds))]
    public void The_accounting_label_cell_carries_the_suffix_the_screen_shows(ReportKind kind)
    {
        var vm = new ReportsViewModel(VatEnabledCompany(), kind);
        if (!vm.IsAccountingReport) return;            // the banded and matrix families are covered above
        if (vm.Rows.Count == 0) return;

        var withSuffix = vm.Rows
            .Select((r, i) => (Row: r, Index: i))
            .Where(x => !string.IsNullOrWhiteSpace(x.Row.Secondary))
            .ToList();
        if (withSuffix.Count == 0) return;             // this kind's builder does not use Secondary

        var print = ReportPrintProjector.Project(vm);
        var export = ReportTabularProjector.Project(vm);

        foreach (var (row, i) in withSuffix)
        {
            Assert.Contains(ReportPrintProjector.Ascii(row.Secondary), print.Rows[i].Cells[0]);
            Assert.Contains(row.Secondary, export.Rows[i].Cells[0].TextValue);

            // And the label itself is still there — carrying the suffix must not have displaced it.
            if (!string.IsNullOrWhiteSpace(row.Particulars))
                Assert.Contains(row.Particulars, export.Rows[i].Cells[0].TextValue);
        }
    }

    /// <summary>
    /// 🔴 AND ON THE PAGE, THE FACT IS EITHER THERE OR VISIBLY CUT — NEVER SILENTLY GONE.
    ///
    /// <para><b>What rendering the real PDF measured, and it is a residual this track does NOT close.</b>
    /// <see cref="ReportPdf"/> clips every cell to its column's inner width with an ellipsis
    /// (<c>ReportPdf.cs:267-270</c>), and the accounting print band is two columns wide — Particulars and Amount.
    /// So where a report's label is ALREADY long, appending the suffix pushes it past the column and the
    /// ellipsis takes the suffix straight back off the page. Measured here: a report whose <c>Secondary</c> is a
    /// full party name (<c>"Jindal Steel Rounds &amp; Bright Bars Depot (Raipur …"</c>) does not fit, and the
    /// printed copy still ends at the ellipsis. The export (CSV/XLSX) carries it in full — that copy has no
    /// column width — and the projection carries it for both paths, which is what the test above pins.</para>
    ///
    /// <para><b>So this asserts the invariant that IS true and is worth having, rather than the one I wanted.</b>
    /// A suffix is either present on the page, or the page shows the row was elided. The failure it forbids is
    /// the one that shipped: the fact absent with the row looking complete. Giving the accounting print band a
    /// real second column for this detail is the fix for the printed copy; it re-lays-out every accounting
    /// report and is a layout decision for the user, recorded and not taken here.</para>
    /// </summary>
    [Fact]
    public void An_accounting_label_suffix_is_on_the_printed_page_or_the_row_is_visibly_elided()
    {
        var onPage = new List<string>();
        var elided = new List<string>();

        foreach (var kind in Enum.GetValues<ReportKind>())
        {
            var vm = new ReportsViewModel(VatEnabledCompany(), kind);
            if (!vm.IsAccountingReport || vm.Rows.Count == 0) continue;

            var row = vm.Rows.FirstOrDefault(r =>
                !string.IsNullOrWhiteSpace(r.Secondary) && r.Secondary.Length >= 4);
            if (row is null) continue;

            var pdf = ReportPdf.Render(
                ReportPrintProjector.Project(vm),
                new PageConfig { FooterText = "Apex Solutions  -  Page {page} of {pages}" });
            var page = System.Text.Encoding.Latin1.GetString(pdf);

            if (page.Contains(ReportPrintProjector.Ascii(row.Secondary), StringComparison.Ordinal))
            {
                onPage.Add(kind.ToString());
                continue;
            }

            // Not on the page: the only acceptable reason is that the writer ran out of column and said so.
            // "..." is PdfWriter's own ellipsis token (PdfWriter.Ellipsis), matched as a literal because that
            // constant is internal to Apex.Ledger.Io.
            Assert.Contains("...", page);
            elided.Add(kind.ToString());
        }

        // Anti-vacuity, both ways: the sweep must have rendered reports that use the cell, and at least one must
        // actually carry its suffix onto the page — otherwise this would pass with the arm entirely inert.
        Assert.True(onPage.Count + elided.Count > 0,
            "no accounting report with a label suffix was rendered, so this proved nothing.");
        Assert.True(onPage.Count > 0,
            "not one accounting report carried its label suffix onto the printed page, so the composition is "
            + "not reaching the page at all: " + string.Join(", ", elided));
    }

    // ===================================================================== the blank-page defect, by rendering

    public static TheoryData<ReportKind> BlankPageKinds() => new()
    {
        ReportKind.VatComputation,
        ReportKind.CstFormsReceivable,
        ReportKind.CstFormsIssuable,
    };

    /// <summary>
    /// 🔴 FAILS ON TODAY'S MAIN. These three printed and exported as pages of entirely blank cells. The
    /// assertion is deliberately about CONTENT, not about captions: a document whose every body cell is empty
    /// is a document that says nothing, which is strictly worse than one with an unlabelled column.
    /// </summary>
    [Theory]
    [MemberData(nameof(BlankPageKinds))]
    public void The_statutory_VAT_and_CST_documents_are_not_pages_of_blank_cells(ReportKind kind)
    {
        var vm = BuildVatReport(kind);

        var print = ReportPrintProjector.Project(vm);
        var export = ReportTabularProjector.Project(vm);

        var printedText = print.Rows.SelectMany(r => r.Cells).Where(c => !string.IsNullOrWhiteSpace(c)).ToList();
        var exportedText = export.Rows
            .SelectMany(r => r.Cells)
            .Select(CellText)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToList();

        Assert.True(printedText.Count > 0, $"{kind} PRINTS as a page of entirely blank cells.");
        Assert.True(exportedText.Count > 0, $"{kind} EXPORTS as a sheet of entirely blank cells.");

        // And not merely one stray label: the report's own figures have to be on it.
        Assert.Contains(printedText, t => t.Any(char.IsDigit));
        Assert.Contains(exportedText, t => t.Any(char.IsDigit));
    }

    /// <summary>The same three reports must also no longer claim to be accounting reports — which is what
    /// routed them into the blank branch, and what put an empty Particulars/Dr/Cr table on screen stacked
    /// above their own grid.</summary>
    [Theory]
    [MemberData(nameof(BlankPageKinds))]
    public void The_VAT_and_CST_reports_do_not_render_the_accounting_grid(ReportKind kind)
    {
        var vm = BuildVatReport(kind);

        Assert.False(
            vm.IsAccountingReport,
            $"{kind} still reports as an accounting report, so ShowSingleAccountingGrid renders an EMPTY "
            + "Particulars/Debit/Credit table stacked above the report's own grid, and both egress projectors "
            + "read Particulars/Amount cells that this builder never writes.");
        Assert.False(vm.ShowSingleAccountingGrid);
    }

    /// <summary>The CST band must skip <c>Col4</c> exactly as the builder and the grid do. Captioning that
    /// band positionally would slide "Gross Amount" onto the empty cell and every column after it by one —
    /// a believable heading over the wrong figures, which is worse than a blank one.</summary>
    [Fact]
    public void The_CST_forms_band_reads_the_gross_amount_from_the_cell_the_builder_writes()
    {
        var vm = BuildVatReport(ReportKind.CstFormsReceivable);
        var band = ReportColumnBands.For(vm);

        var captions = band.Select(c => c.Caption).ToArray();
        Assert.Equal(
            new[] { "Date", "Vch No.", "Party", "Gross Amount", "Form", "Form No.", "Form Date" },
            captions);

        // The Gross Amount column must read Col5 — the cell BuildCstForms actually writes — not Col4, which
        // the builder deliberately leaves empty.
        var gross = band[Array.IndexOf(captions, "Gross Amount")];
        var probe = new ReportRow { Col4 = "WRONG-CELL", Col5 = "1,23,456.00" };
        Assert.Equal("1,23,456.00", gross.Cell(probe));
    }

    // ===================================================================== the dropped money column

    public static TheoryData<ReportKind, string> SecondaryMoneyKinds() => new()
    {
        // The captions carry the screen's ₹ verbatim; the print path folds it to "Rs." on its own.
        { ReportKind.Batchwise, "Value ₹" },
        { ReportKind.BatchAgeAnalysis, "Value ₹" },
        { ReportKind.PriceList, "Net Rate ₹" },
    };

    /// <summary>
    /// 🔴 FAILS ON TODAY'S MAIN. These three reports show a money column on screen that the generic projectors
    /// could not see, because the builder parks it in <see cref="ReportRow.Secondary"/> rather than a
    /// <c>Col</c>. The printed and exported copies therefore had no Value / Net Rate column at all — a figure
    /// missing from the document, not merely an unlabelled one.
    /// </summary>
    [Theory]
    [MemberData(nameof(SecondaryMoneyKinds))]
    public void The_money_column_kept_in_Secondary_reaches_the_document(ReportKind kind, string caption)
    {
        var vm = new ReportsViewModel(PopulatedCompanyFixture.BuildRegular(), kind);

        var print = ReportPrintProjector.Project(vm);
        var export = ReportTabularProjector.Project(vm);

        // Print is ASCII-only and folds ₹ to "Rs." on its way to the PDF writer; the Unicode export keeps it.
        // Both are the screen's caption — asserting the folded form here rather than dropping the glyph from
        // the band is what keeps the document and the grid saying the same thing about the column's unit.
        Assert.Contains(ReportPrintProjector.Ascii(caption), print.Columns.Select(c => c.Header));
        Assert.Contains(caption, export.Columns.Select(c => c.Header));

        // The column must carry the figures, not just the heading: at least one row's Secondary text has to
        // appear in the projected cells.
        var secondary = vm.Rows.Select(r => r.Secondary).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s));
        if (secondary is null) return; // the fixture has no batch/price data for this kind

        Assert.Contains(print.Rows, r => r.Cells.Contains(ReportPrintProjector.Ascii(secondary)));
    }

    // ===================================================================== print captions, which were all blank

    /// <summary>
    /// 🔴 FAILS ON TODAY'S MAIN. <c>ReportPrintProjector</c> had no per-kind caption table at all: it printed
    /// "Particulars" and then an empty caption for every remaining column, on every non-accounting,
    /// non-payroll report. This asserts the PRINTED band, across the whole generic-path surface.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllKinds))]
    public void Every_printed_column_carries_a_caption(ReportKind kind)
    {
        var vm = new ReportsViewModel(VatEnabledCompany(), kind);
        if (!ReportColumnBands.NeedsBand(vm)) return;

        var print = ReportPrintProjector.Project(vm);

        Assert.All(print.Columns, c => Assert.False(
            string.IsNullOrWhiteSpace(c.Header),
            $"{kind} prints a column with a BLANK caption. A printed statutory or inventory page whose "
            + "figures sit under unlabelled columns cannot be read by the auditor it was printed for."));
    }

    /// <summary>The printed and exported headings of one report must be the SAME headings — the whole reason
    /// both projectors read one band. Two caption tables is how they drifted apart before.</summary>
    [Theory]
    [MemberData(nameof(AllKinds))]
    public void The_printed_and_exported_headings_of_a_report_agree(ReportKind kind)
    {
        var vm = new ReportsViewModel(VatEnabledCompany(), kind);
        if (!ReportColumnBands.NeedsBand(vm)) return;

        var print = ReportPrintProjector.Project(vm);
        var export = ReportTabularProjector.Project(vm);

        Assert.Equal(
            print.Columns.Select(c => ReportPrintProjector.Ascii(c.Header)).ToArray(),
            export.Columns.Select(c => ReportPrintProjector.Ascii(c.Header)).ToArray());
    }

    /// <summary>The statutory pair's captions must follow the kind: a TCS report says "Coll. Code" and
    /// "Collected" where its TDS twin says "Section" and "Deducted". A static per-kind caption literal could
    /// not express that and would have printed the TDS wording over TCS figures.</summary>
    [Fact]
    public void The_TCS_document_is_captioned_for_TCS_not_for_its_TDS_twin()
    {
        var tds = ReportPrintProjector.Project(
            new ReportsViewModel(VatEnabledCompany(), ReportKind.TdsOutstanding));
        var tcs = ReportPrintProjector.Project(
            new ReportsViewModel(VatEnabledCompany(), ReportKind.TcsOutstanding));

        Assert.Contains("Section", tds.Columns.Select(c => c.Header));
        Assert.Contains("Deducted", tds.Columns.Select(c => c.Header));

        Assert.Contains("Coll. Code", tcs.Columns.Select(c => c.Header));
        Assert.Contains("Collected", tcs.Columns.Select(c => c.Header));
        Assert.DoesNotContain("Deducted", tcs.Columns.Select(c => c.Header));
    }

    // ===================================================================== helpers

    /// <summary>
    /// True when the exported row carries <paramref name="written"/>. A money column is exported as a real
    /// spreadsheet <b>Number</b> — <see cref="TabularCell.TextValue"/> is empty and the exact decimal sits in
    /// <see cref="TabularCell.NumberValue"/> — so a plain string comparison would report every figure in the
    /// book as "dropped". Text cells compare verbatim; figure cells compare by value, parsed with the
    /// projector's own parser so the two agree on Indian grouping and accounting parentheses.
    /// </summary>
    private static bool Carried(IReadOnlyList<TabularCell> cells, string written)
    {
        foreach (var c in cells)
        {
            if (c.HasNumber)
            {
                if (ReportTabularProjector.TryParseAmount(written, out var v) && v == c.NumberValue) return true;
            }
            else if (string.Equals(c.TextValue, written, StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>A cell's text for a "is there anything on this page at all" assertion: the stored text, or the
    /// figure rendered back, so a Number cell counts as content rather than as a blank.</summary>
    private static string CellText(TabularCell c) =>
        c.HasNumber ? c.NumberValue.ToString(System.Globalization.CultureInfo.InvariantCulture) : c.TextValue;

    /// <summary>
    /// Every non-empty <c>Col1..Col8</c> cell a builder wrote on a row.
    ///
    /// <para>🔴 <b><see cref="ReportRow.Secondary"/> IS DELIBERATELY EXCLUDED, AND THE REASON IS A SEPARATE
    /// FINDING RATHER THAN A CONVENIENCE.</b> <c>Secondary</c> is a general-purpose slot, and the builders use
    /// it two different ways. Batchwise, Batch Age Analysis and Price List <b>display</b> it — it is their
    /// Value / Net Rate column, bound at <c>MainWindow.axaml:1610/1667/1721</c> — so for those three it must
    /// reach the document, which <see cref="The_money_column_kept_in_Secondary_reaches_the_document"/> asserts
    /// directly. The three allocation registers, the two material registers and the Physical Stock Register
    /// instead set <c>Secondary</c> to a batch/lot label that <b>nothing renders anywhere</b>: their grids bind
    /// exactly <c>Col1..Col8</c> and no <c>Secondary</c>, so the label is computed and then dropped on screen
    /// as well as in print and export. Requiring it in the document here would have made this test demand that
    /// the exported copy carry a column the screen does not have — inventing a divergence rather than catching
    /// one. Giving those registers a real Batch column is a product decision, recorded for the user, not one to
    /// take inside a captions fix.</para>
    /// </summary>
    private static IReadOnlyList<string> GenericCells(ReportRow r) =>
        new[] { r.Col1, r.Col2, r.Col3, r.Col4, r.Col5, r.Col6, r.Col7, r.Col8 }
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();

    /// <summary>
    /// 🔴 THE POPULATED FIXTURE, WITH STATE VAT ADDED — AND THE CHOICE IS LOAD-BEARING, NOT CONVENIENCE.
    ///
    /// <para>These sweeps were first written against a bare seeded company and were <b>very nearly vacuous</b>:
    /// with no data, every builder emits a single empty-state row, <c>MaxUsedGenericCells</c> returns 1, and a
    /// report projects to ONE "Particulars" column — which is never blank, so a test asserting "no printed
    /// column is blank" passed on reports that print eight blank captions the moment they have rows. Measured:
    /// on the bare company the whole-surface caption sweep caught <b>3</b> kinds; on this one it catches
    /// <b>19</b>. A sweep over an empty book proves nothing here, which is this project's most expensive
    /// recurring lesson.</para>
    ///
    /// <para>Cached per class because the fixture posts a full book and these are theories over every kind.</para>
    /// </summary>
    private static Company VatEnabledCompany() => _populated.Value;

    private static readonly Lazy<Company> _populated = new(() =>
    {
        var c = PopulatedCompanyFixture.BuildRegular();
        // State VAT covers the goods GST never absorbed, so a Regular-GST book can carry both; this is what
        // lets the VAT/CST kinds get past their F11 empty state inside the same sweep as everything else.
        new VatService(c).EnableVat(tin: "29123456789");
        return c;
    });

    /// <summary>
    /// A VAT company carrying one inter-State sale and one inter-State purchase, so the VAT Computation has
    /// figures and both declaration-forms reports have a row.
    ///
    /// <para>🔴 Builds its OWN company rather than reusing the cached populated one, because it POSTS to it.
    /// Sharing the cached instance would have had seven theory cases add the same ledgers and vouchers to one
    /// book in whatever order the runner chose — an order-dependent test that passes today and fails on a
    /// runner that shards differently. A test that mutates a shared fixture is not a fixture, it is a race.</para>
    /// </summary>
    private static ReportsViewModel BuildVatReport(ReportKind kind)
    {
        var c = CompanyFactory.CreateSeeded(
            "Band Coverage VAT Private Limited",
            PopulatedCompanyFixture.FyStart,
            PopulatedCompanyFixture.FyStart);
        new VatService(c).EnableVat(tin: "29123456789");

        var customer = new DomainLedger(
            Guid.NewGuid(), "Highway Fuels (Interstate)", c.FindGroupByName("Sundry Debtors")!.Id,
            Money.Zero, openingIsDebit: true)
        { PartyCstNumber = "CST/07/1234" };
        c.AddLedger(customer);

        var supplier = new DomainLedger(
            Guid.NewGuid(), "Northern Petro Supplies", c.FindGroupByName("Sundry Creditors")!.Id,
            Money.Zero, openingIsDebit: false)
        { PartyCstNumber = "CST/09/9876" };
        c.AddLedger(supplier);

        var sales = new DomainLedger(
            Guid.NewGuid(), "Fuel Sales", c.FindGroupByName("Sales Accounts")!.Id,
            Money.Zero, openingIsDebit: false);
        c.AddLedger(sales);

        var purchases = new DomainLedger(
            Guid.NewGuid(), "Fuel Purchases", c.FindGroupByName("Purchase Accounts")!.Id,
            Money.Zero, openingIsDebit: true);
        c.AddLedger(purchases);

        var ledgerService = new LedgerService(c);
        var date = c.FinancialYearStart.AddMonths(2);

        var salesType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales);
        ledgerService.Post(new Voucher(
            Guid.NewGuid(), salesType.Id, date,
            new[]
            {
                new EntryLine(customer.Id, Money.FromRupees(125000m), DrCr.Debit),
                new EntryLine(sales.Id, Money.FromRupees(125000m), DrCr.Credit),
            },
            partyId: customer.Id));

        var purchaseType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase);
        ledgerService.Post(new Voucher(
            Guid.NewGuid(), purchaseType.Id, date,
            new[]
            {
                new EntryLine(purchases.Id, Money.FromRupees(64000m), DrCr.Debit),
                new EntryLine(supplier.Id, Money.FromRupees(64000m), DrCr.Credit),
            },
            partyId: supplier.Id));

        var vm = new ReportsViewModel(c, kind);
        vm.SetPeriod(new DateOnly(date.Year - 1, 4, 1), new DateOnly(date.Year + 1, 3, 31));
        return vm;
    }
}
