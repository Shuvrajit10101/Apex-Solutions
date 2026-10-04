using System.Globalization;

namespace Apex.Ledger.Io;

/// <summary>
/// Renders a <see cref="PrintReport"/> (already-formatted title / subtitle / columns / rows) to a PDF
/// document via the hand-rolled <see cref="PdfWriter"/>. Lays out a title block and a running page
/// header/footer, draws the column-header band on every page, right-aligns amount columns, bolds/rules
/// section headers and totals, and paginates when rows overflow the content height.
///
/// <para>Deterministic and culture-invariant: no clock, no RNG, invariant number formatting. Metadata is
/// de-branded ("Apex Solutions"). Given the same report + config it produces byte-identical output.</para>
/// </summary>
public static class ReportPdf
{
    /// <summary>Renders the report to PDF bytes using the given page configuration.</summary>
    public static byte[] Render(PrintReport report, PageConfig config)
    {
        ArgumentNullException.ThrowIfNull(report);
        return Render(new[] { report }, config);
    }

    /// <summary>
    /// Renders a SET of already-formatted documents into ONE PDF (W2-32 / census 12.6 — multi-account /
    /// multi-voucher range printing). Each document starts on a fresh sheet and the page numbering runs across
    /// the whole job, so a stack of printed ledger accounts reads as one document an operator can collate.
    ///
    /// <para>The W2-31 knobs apply to the JOB, not to each member: the F10 range selects sheets of the job, the
    /// F5 copy count repeats the whole job collated, and the F8/F9 format and paper apply throughout. Rendering
    /// a one-document job is byte-identical to rendering that document alone (ER-13), which is why the
    /// single-document overload above simply delegates here.</para>
    /// </summary>
    public static byte[] Render(IReadOnlyList<PrintReport> documents, PageConfig config)
    {
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(config);

        // First pass: paginate EVERY document so the footer can show the job-wide "Page x of N". Each document's
        // pages are kept with the document they belong to, because the column geometry is per document (a ledger
        // account and a reminder letter do not share a column layout).
        var laid = new List<(PrintReport Report, PageConfig Config, double[] ColX, List<PrintRow> Rows)>();
        foreach (var report in documents)
        {
            if (report is null) continue;
            // Each document carries its OWN effective page: a report too wide for portrait is turned onto its
            // side rather than printed with its figures cut. The orientation is per document because the column
            // geometry already is — a ledger account and a nine-column GST return do not share a page shape.
            var (effective, pages) = LayOutCore(report, config);
            double[] colX = ComputeColumnX(report, effective);
            foreach (var rows in pages) laid.Add((report, effective, colX, rows));
        }
        int total = laid.Count == 0 ? 1 : laid.Count;

        // W2-31 (census 12.4) F10: the job keeps its OWN numbering. StartPageNumber renumbers sheet 1 (so a
        // continuation report reads "Page 7 of 10"), and the page RANGE selects which of those sheets are drawn —
        // it never renumbers them, because the operator is holding sheet 3 of a 4-sheet job.
        int firstNumber = config.StartPageNumber < 1 ? 1 : config.StartPageNumber;
        int lastNumber = firstNumber + total - 1;

        // The PDF /Title names the job. A single-document job keeps that document's title, so the one-document
        // path is byte-identical to the single-document render it replaced.
        var writer = new PdfWriter { DocumentTitle = SafeTitle(JobTitle(documents)) };

        int drawn = 0;
        for (int p = 0; p < laid.Count; p++)
        {
            if (!config.IncludesPage(p + 1)) continue;   // outside the F10 range — not drawn at all
            var (report, pageConfig, colX, rows) = laid[p];
            writer.BeginPage(pageConfig.PageWidth, pageConfig.PageHeight);
            DrawPage(writer, report, pageConfig, colX, rows, firstNumber + p, lastNumber, isFirstPage: p == 0);
            drawn++;
        }

        // A PDF must carry at least one page. An out-of-bounds range (or an empty job) therefore yields ONE BLANK
        // sheet rather than the whole report — silently falling back to "print everything" is the failure this
        // guards against.
        if (drawn == 0)
            writer.BeginPage(config.PageWidth, config.PageHeight);

        // W2-31 F5: collated copies of the WHOLE job (1,2,1,2 — never 1,1,2,2). One copy repeats nothing, so the
        // shipped byte stream is untouched (ER-13).
        writer.RepeatAllPages(config.EffectiveCopies);

        return writer.Build();
    }

    /// <summary>
    /// The page this document will actually be printed on, and the rows that fall on each of its sheets —
    /// <b>the very layout <see cref="Render(IReadOnlyList{PrintReport}, PageConfig)"/> is about to draw</b>,
    /// because both go through <c>LayOutCore</c> and there is no second copy of the arithmetic.
    ///
    /// <para>🔴 <b>WHY IT IS PUBLIC: A PREVIEW THAT PAGINATES A DIFFERENT DOCUMENT FROM THE ONE IT PRINTS TELLS
    /// THE OPERATOR THE WRONG NUMBER OF SHEETS.</b> <c>PrintPreviewViewModel.PaginateForPreview</c> used to
    /// re-derive rows-per-page from the config it was handed. That was close enough while the renderer always
    /// honoured that config verbatim, and became wrong the moment <see cref="FitOrientation"/> started turning a
    /// wide report onto its side: the pane and its visible "Pages: N" counted 53 rows to a portrait sheet while
    /// the emitted PDF was landscape at 34, so a 40-row return showed ONE sheet and printed TWO. An operator
    /// counting sheets or setting a page range off that readout sets it off a wrong number. Asking the renderer
    /// is the only answer that cannot drift — a change to the pitch, the banner or the orientation rule moves
    /// both at once.</para>
    ///
    /// <para>Presentation only: the <b>bytes</b> still come from <c>Render</c>, which is called separately. This
    /// method draws nothing and allocates no PDF.</para>
    /// </summary>
    public static (PageConfig Page, IReadOnlyList<IReadOnlyList<PrintRow>> Sheets) LayOut(
        PrintReport report, PageConfig config)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(config);

        var (page, sheets) = LayOutCore(report, config);
        return (page, sheets.ConvertAll(rows => (IReadOnlyList<PrintRow>)rows));
    }

    /// <summary>
    /// The effective page and the sheet-by-sheet row split for one document. The single place that pairing is
    /// computed, so <see cref="Render(IReadOnlyList{PrintReport}, PageConfig)"/> and <see cref="LayOut"/> cannot
    /// disagree about how many sheets a document occupies or which way up they are.
    ///
    /// <para>A document with no rows still gets ONE sheet: a PDF page is emitted for it, so a preview must show
    /// one too.</para>
    /// </summary>
    private static (PageConfig Page, List<List<PrintRow>> Sheets) LayOutCore(PrintReport report, PageConfig config)
    {
        var effective = FitOrientation(report, config);
        var sheets = Paginate(report, effective);
        if (sheets.Count == 0) sheets.Add(new List<PrintRow>());
        return (effective, sheets);
    }

    /// <summary>
    /// The page this document will actually be drawn on: the configured one, or the same page turned onto its
    /// side when the columns cannot otherwise hold their contents.
    ///
    /// <para>🔴 <b>WHY REDISTRIBUTING THE WIDTH IS NOT ALWAYS ENOUGH, MEASURED.</b>
    /// <see cref="ComputeColumnX"/> can abbreviate prose to free width for the figures, but it cannot create
    /// page. GSTR-1 asks for more than exists: four label columns at the <see cref="MinProseWidth"/> floor plus
    /// five figure columns that must each hold <c>99,99,99,999.00</c> at 69pt come to ~585pt against A4
    /// portrait's 523.276pt of content, and with the captions at full width ~535pt even before the prose is
    /// counted. No allocation of 523.276pt fits that. Turning the sheet gives 769.89pt and the return prints
    /// whole, so this is a PAGE-SIZE answer to a page-size problem.</para>
    ///
    /// <para><b>The four cases where this deliberately does nothing.</b> The content already fits (the common
    /// case — the orientation is untouched and the bytes are byte-identical to before this existed, ER-13); the
    /// caller pinned the orientation with <c>AutoFitOrientation = false</c>, or had already asked for landscape;
    /// the paper is PRE-PRINTED stationery, which must never be turned (see the guard below for why); or the
    /// document would not fit sideways either, where turning the page would change the output's shape without
    /// curing the cut. In that last case the portrait page is kept and the shortfall is left visible in the
    /// output rather than half-hidden by a rotation that did not work.</para>
    /// </summary>
    private static PageConfig FitOrientation(PrintReport report, PageConfig config)
    {
        if (!config.AutoFitOrientation) return config;
        if (config.Orientation != PageOrientation.Portrait) return config;
        if (report.Columns.Count == 0) return config;

        // 🔴 NEVER TURN PRE-PRINTED STATIONERY. The sheet is already in the tray the right way up with a
        // letterhead and a ruled grid printed on it, and the operator aligned it there. Rotating the image we
        // send would print the report across the letterhead sideways — a worse outcome than the clipped figure
        // it was trying to avoid, and one the operator cannot correct from the application. On pre-printed paper
        // the width redistribution still applies (a cut figure is wrong on any paper); only the rotation is off.
        if (config.Paper == PaperKind.PrePrinted) return config;

        double required = MinimumViableWidth(report, config);
        if (required <= config.ContentWidth) return config;        // fits as configured — change nothing

        var landscape = config.WithOrientation(PageOrientation.Landscape);
        return required <= landscape.ContentWidth ? landscape : config;
    }

    /// <summary>
    /// The narrowest page on which this report can be drawn with <b>every figure whole</b>: each figure column's
    /// full requirement, plus each prose column's requirement capped at <see cref="MinProseWidth"/>.
    ///
    /// <para>🔴 <b>IT IS DELIBERATELY NOT THE SUM OF EVERY COLUMN'S IDEAL WIDTH, AND USING THAT TURNED THE WRONG
    /// PAGES.</b> With a realistic 42-character party name in four label columns the ideal sum is over 1100pt,
    /// which exceeds even A4 landscape's 769.89pt — so an ideal-width test concludes "it will not fit either
    /// way", keeps portrait, and the figures cut. But the report fits landscape perfectly well once the prose is
    /// allowed to abbreviate, which is exactly what <see cref="ComputeColumnX"/> will then do. The question this
    /// method is asked is not "can every column have everything it wants" but "is there a page on which no
    /// FIGURE has to be cut", and those have different answers.</para>
    ///
    /// <para>Measured UNCAPPED (<c>double.MaxValue</c>), because <see cref="MeasureRequirements"/> otherwise caps
    /// each column at the content width and summing capped figures would understate a genuine overflow.</para>
    /// </summary>
    private static double MinimumViableWidth(PrintReport report, PageConfig config)
    {
        var need = MeasureRequirements(report, config, double.MaxValue);
        double total = 0;
        for (int i = 0; i < need.Length; i++)
            total += IsFigureColumn(report.Columns[i]) ? need[i] : Math.Min(need[i], MinProseWidth);
        return total;
    }

    /// <summary>The /Title for a job: the lone document's title, or a neutral label for a set.</summary>
    private static string JobTitle(IReadOnlyList<PrintReport> documents)
        => documents.Count == 1 && documents[0] is { } only ? only.Title : "Print Job";

    // ---- pagination ----

    private static List<List<PrintRow>> Paginate(PrintReport report, PageConfig config)
    {
        double top = config.PageHeight - config.MarginTop;
        double bottom = config.MarginBottom + config.FooterFontSize + 6;

        // Height consumed by the fixed banner (title block + column-header band) at the top of each page.
        double firstBanner = BannerHeight(config, includeTitle: true);
        double restBanner = BannerHeight(config, includeTitle: true); // title repeats on every page for context

        var pages = new List<List<PrintRow>>();
        var current = new List<PrintRow>();
        double y = top - firstBanner;

        foreach (var row in report.Rows)
        {
            double h = config.FormattedRowHeight;   // W2-31: dot matrix condenses the pitch, so more rows fit
            if (y - h < bottom && current.Count > 0)
            {
                pages.Add(current);
                current = new List<PrintRow>();
                y = top - restBanner;
            }
            current.Add(row);
            y -= h;
        }
        if (current.Count > 0 || pages.Count == 0)
            pages.Add(current);
        return pages;
    }

    private static double BannerHeight(PageConfig config, bool includeTitle)
    {
        // W2-31: the banner is measured with the FORMAT's metrics so pagination and drawing agree. It is NOT
        // measured with the PAPER's: pre-printed stationery physically occupies that space with a letterhead, so
        // the band is suppressed from the ink but its height is still reserved — otherwise the figures would
        // overprint the letterhead.
        double h = 0;
        if (includeTitle)
        {
            h += config.FormattedTitleFontSize + 6;
            h += config.SubtitleFontSize + 8;
        }
        h += config.FormattedHeaderFontSize + 6; // column header band + its rule
        return h;
    }

    // ---- column geometry ----

    /// <summary>The padding inside each cell, on both sides. Shared with <see cref="DrawRowCells"/> so the width a
    /// column is MEASURED against is the width its text is actually DRAWN into — the two drifting apart is how a
    /// figure that "fits" gets clipped anyway.</summary>
    private const double CellPad = 2;

    /// <summary>
    /// A hair of width added to every measured requirement, on top of the padding.
    ///
    /// <para>🔴 <b>GRANTING A COLUMN EXACTLY ITS MEASURED WIDTH IS NOT ENOUGH, MEASURED.</b> Two reasons, and the
    /// first was observed rather than predicted: topping a column up by <c>(need − share) ÷ deficit × take</c> is a
    /// divide followed by a multiply, so when <c>take == deficit</c> the result is <i>almost</i> but not exactly
    /// <c>need</c>, and <see cref="PdfWriter.FitToWidth"/> clips on <c>&lt;=</c>. Five of Reorder Status's six
    /// figure columns drew <c>99,99,99,999....</c> — the figure short by its paise alone — while the sixth drew
    /// whole. Second, and more fundamental: <see cref="PdfWriter.MeasureHelvetica"/> documents itself as a coarse
    /// per-class average, not the real font's advance widths, so a requirement derived from it is an estimate and
    /// budgeting zero margin against an estimate is the wrong side of the error to stand on. One point is far below
    /// a glyph and cannot change a layout that already fits.</para>
    /// </summary>
    private const double MeasureSafety = 1.0;

    /// <summary>
    /// The least width a prose (left-aligned) column is squeezed to when the page cannot hold every column's full
    /// requirement: 60pt, about thirteen characters of 9pt Helvetica.
    ///
    /// <para><b>Why prose has a floor at all and figures do not.</b> A figure column's floor is its full
    /// requirement, because a clipped figure is a wrong figure. Prose is the opposite — a party name or a
    /// narration has no correct width, and a reader who sees "Brightline Ind…" can ask for the rest — so prose is
    /// what gives way. But it may not give way to NOTHING: a label column squeezed to zero prints an empty cell,
    /// and a row that does not say what it is about is worse than a row whose subject is abbreviated. Thirteen
    /// characters is enough to tell two parties apart in practice, which is the job this floor has.</para>
    /// </summary>
    private const double MinProseWidth = 60.0;

    /// <summary>
    /// The x boundary of every column, left to right.
    ///
    /// <para>🔴 <b>THE WEIGHTS ALONE CUT MONEY FIGURES MID-NUMBER, SO THEY ARE NO LONGER THE LAST WORD.</b> A
    /// <see cref="PrintColumn.Weight"/> is a fixed relative number chosen when a band was written; it knows nothing
    /// about how wide a rupee figure is. Splitting the content width purely in proportion to those weights and then
    /// clipping each cell with <see cref="PdfWriter.FitToWidth"/> — which appends an ellipsis — meant that on a band
    /// with enough columns the figure column came out narrower than the figure, and the number was cut mid-number
    /// on the printed page. Measured at the shipped A4-portrait default (523.276pt of content, 9pt Helvetica):
    /// <c>99,99,99,999.00</c> needs 64.04pt, and <b>26 of the 37 banded report kinds gave their figure columns
    /// less</b>. GSTR-1 gave them 41.90pt, so a filed return printed its taxable value and all four tax figures cut
    /// from <c>1,00,000.00</c> upward. Eight kinds also cut their own column CAPTIONS ("Order to be Placed",
    /// "Below Threshold"), so a reader could not tell which figure a column held.</para>
    ///
    /// <para><b>The rule now applied.</b> The weights still set the opening share, and when that share already
    /// gives every column what it needs this method takes an exact FAST PATH and returns it unchanged — so the
    /// overwhelming majority of reports render byte-identically to before any of this existed (ER-13). If nothing
    /// is short, nothing moves. Otherwise priority decides, through a FLOOR per column: a figure column's floor is
    /// its <i>full requirement</i>, a prose column's floor is <see cref="MinProseWidth"/>. Every column starts at
    /// its floor, the surplus is handed out by weight with no column taking more than it needs, and any residue
    /// goes out by weight so a short report still looks as it always did.</para>
    ///
    /// <para><b>Why the floors are asymmetric.</b> A clipped party name or narration is prose a reader can ask for
    /// again; a clipped figure is a WRONG figure — <c>99,99,99,999.00</c> drawn as <c>99,99,99,...</c> reads as a
    /// real, smaller amount on a document that goes to a bank, an auditor or a tax officer. So prose is what gives
    /// way. 🔴 <b>AN EARLIER VERSION OF THIS METHOD REDISTRIBUTED ONLY THE SLACK</b> — width held by columns
    /// needing less than their share — and that is NOT enough: on real data with real party names the prose
    /// columns hold no slack at all, so there is nothing to move and the figures cut anyway. It passed a 112-case
    /// suite that happened to feed it short labels.
    /// <c>PrintedMoneyColumnWidthTests.Every_banded_report_prints_the_widest_money_figure_whole_beside_realistic_labels</c>
    /// is the case that catches it, and it fails on 28 kinds if the prose floor is removed.</para>
    ///
    /// <para><b>What this cannot do, stated rather than hidden.</b> It abbreviates prose to free width for the
    /// figures; it does not create page. Where even the floors exceed the content width — nine columns of which
    /// five must each hold a crore — nothing can be printed whole, and the columns share the page in proportion to
    /// what they asked for. <see cref="FitOrientation"/> has already had its chance to turn the sheet by then, so
    /// reaching that branch means sideways would not have fitted either. That residual is a PAGE-SIZE limit, not a
    /// weighting one, and it is left visible in the output rather than papered over by starving the captions.</para>
    /// </summary>
    private static double[] ComputeColumnX(PrintReport report, PageConfig config)
    {
        int n = report.Columns.Count;
        var xs = new double[Math.Max(n, 1) + 1];
        double left = config.MarginLeft;
        if (n == 0)
        {
            xs[0] = left;
            xs[1] = left + config.ContentWidth;
            return xs;
        }

        double content = config.ContentWidth;
        var weight = new double[n];
        double totalWeight = 0;
        for (int i = 0; i < n; i++)
        {
            weight[i] = report.Columns[i].Weight <= 0 ? 1 : report.Columns[i].Weight;
            totalWeight += weight[i];
        }

        // The opening share: exactly what the weights have always produced.
        var width = new double[n];
        for (int i = 0; i < n; i++) width[i] = content * (weight[i] / totalWeight);

        var need = MeasureRequirements(report, config, content);

        // ---- fast path: the weights already give every column what it needs -------------------------------
        // This is the overwhelming majority of reports, and taking it means their bytes are IDENTICAL to what
        // this renderer produced before any of this existed (ER-13). It is an exact test, not an approximation:
        // if nothing is short, nothing moves.
        bool allFit = true;
        for (int i = 0; i < n; i++) if (need[i] > width[i]) { allFit = false; break; }

        if (!allFit)
        {
            // ---- the page cannot satisfy every column from its weight share, so PRIORITY decides ----------
            //
            // 🔴 THE FLOOR IS WHERE THE INVARIANT LIVES. A figure column's floor is its full requirement: a
            // clipped figure is a WRONG figure — 99,99,99,999.00 drawn as 99,99,99,... reads as a real, smaller
            // amount. A prose column's floor is MinProseWidth, because a party name or a narration has no
            // correct width and a reader who sees its first dozen characters can ask for the rest; it is the one
            // thing on the page that can give way. Redistributing only the SLACK (which is what this method did
            // first) was not enough — when the prose columns hold no slack, as they do not on real data with
            // real party names, there is nothing to move and the figures cut anyway.
            var floor = new double[n];
            double sumFloor = 0;
            for (int i = 0; i < n; i++)
            {
                floor[i] = IsFigureColumn(report.Columns[i]) ? need[i] : Math.Min(need[i], MinProseWidth);
                sumFloor += floor[i];
            }

            if (sumFloor >= content)
            {
                // Not even the floors fit. Nothing can be printed whole, so share the page in proportion to what
                // each column asked for and let the clipping fall where it must. FitOrientation has already had
                // its chance to turn the page; reaching here means sideways would not have fitted either, and
                // that residual is reported as a page-size limit rather than disguised.
                for (int i = 0; i < n; i++) width[i] = content * (floor[i] / sumFloor);
            }
            else
            {
                // Everyone starts at their floor; the surplus is then handed out BY WEIGHT, no column taking
                // more than it needs, until either the page or the appetite runs out.
                Array.Copy(floor, width, n);
                double remaining = content - sumFloor;

                // Capping redistributes, so this iterates: a column that hits its need stops taking and its
                // share passes to the others. Bounded by n + 1 rounds — each round retires at least one column
                // or exhausts the remainder.
                for (int round = 0; round <= n && remaining > 1e-9; round++)
                {
                    double hungryWeight = 0;
                    for (int i = 0; i < n; i++)
                        if (need[i] - width[i] > 1e-9) hungryWeight += weight[i];
                    if (hungryWeight <= 0) break;

                    double handedOut = 0;
                    for (int i = 0; i < n; i++)
                    {
                        double capacity = need[i] - width[i];
                        if (capacity <= 1e-9) continue;
                        double grant = Math.Min(capacity, remaining * (weight[i] / hungryWeight));
                        width[i] += grant;
                        handedOut += grant;
                    }
                    if (handedOut <= 1e-9) break;
                    remaining -= handedOut;
                }

                // Every column is at its need and the page is still not full: the leftover goes out by weight,
                // which is what the weights are for and keeps a short report looking as it always did.
                if (remaining > 1e-9)
                    for (int i = 0; i < n; i++) width[i] += remaining * (weight[i] / totalWeight);
            }
        }

        double x = left;
        xs[0] = x;
        for (int i = 0; i < n; i++)
        {
            x += width[i];
            xs[i + 1] = x;
        }
        // The widths sum to the content width by construction; pin the right edge so accumulated floating-point
        // drift can never place the last column a hair past the right margin.
        xs[n] = left + content;
        return xs;
    }

    /// <summary>
    /// A figure column: right-aligned. Every money and quantity column of every band and of the accounting and
    /// payroll projections is right-aligned, and nothing else is, which is why alignment is the test rather than a
    /// second flag that could disagree with it.
    /// </summary>
    private static bool IsFigureColumn(PrintColumn column) => column.Align == CellAlign.Right;

    /// <summary>
    /// The width each column actually requires: the widest of its caption and all of its cells, measured through
    /// the same <see cref="PdfWriter.MeasureHelvetica"/> the renderer aligns and clips by, at the same font sizes
    /// this configuration will draw them, plus the cell padding. Capped at the content width so one very long
    /// narration cannot ask for more page than exists.
    /// </summary>
    private static double[] MeasureRequirements(PrintReport report, PageConfig config, double content)
    {
        int n = report.Columns.Count;
        var need = new double[n];
        double headerSize = config.FormattedHeaderFontSize;
        double bodySize = config.FormattedBodyFontSize;

        bool drawsHeaders = config.DrawsColumnHeaderBand;
        for (int i = 0; i < n; i++)
        {
            // A pre-printed run draws no header band, so a caption that is never printed must not claim width.
            double header = drawsHeaders
                ? PdfWriter.MeasureHelvetica(Scrub(report.Columns[i].Header), headerSize)
                : 0;
            need[i] = header;
        }

        foreach (var row in report.Rows)
        {
            if (row is null) continue;
            int cells = Math.Min(n, row.Cells.Count);
            for (int i = 0; i < cells; i++)
            {
                string text = row.Cells[i] ?? string.Empty;
                if (text.Length == 0) continue;
                if (i == 0 && row.Indent > 0) text = new string(' ', row.Indent) + text;
                double w = PdfWriter.MeasureHelvetica(text, bodySize);
                if (w > need[i]) need[i] = w;
            }
        }

        for (int i = 0; i < n; i++)
        {
            need[i] += CellPad * 2 + MeasureSafety;
            if (need[i] > content) need[i] = content;
        }
        return need;
    }

    // ---- drawing ----

    private static void DrawPage(
        PdfWriter writer, PrintReport report, PageConfig config, double[] colX,
        List<PrintRow> rows, int pageNo, int pageCount, bool isFirstPage)
    {
        double left = config.MarginLeft;
        double right = config.PageWidth - config.MarginRight;
        double y = config.PageHeight - config.MarginTop;

        // Running header text (optional). 🔴 THIS IS OURS AND IT WAS THE ONE PIECE OF CHROME THAT SCRUBBED
        // NOTHING — a hole in the "keep de-branding our own strings" half of Ruling 18, found while closing the
        // other half. It is live: Form16ViewModel renders the salary-TDS certificate through
        // ReportPdf.Render(..., CertificatePages.Build(_company.Name)), and CertificatePages puts OUR COMPANY NAME
        // in HeaderText — whose own doc-comment claims it is "already de-branded by the writers". It was not. The
        // same company name in the SUBTITLE of that same page went through the guard, so the two halves of one
        // certificate disagreed. Conditional, so a clean header is byte-identical (ER-13).
        if (!string.IsNullOrEmpty(config.HeaderText))
        {
            writer.Text(left, config.PageHeight - config.MarginTop + 4, Scrub(config.HeaderText), config.FooterFontSize);
        }

        // Title block (centered title + subtitle), repeated on every page for context. W2-31 F9: on pre-printed
        // stationery the letterhead is already there, so the band is SKIPPED but its height is still consumed —
        // the figures must land where the stationery leaves room for them, not slide up over the letterhead.
        y -= config.FormattedTitleFontSize;
        if (config.DrawsTitleBand)
            DrawCentered(writer, HeadingText(report), left, right, y, config.FormattedTitleFontSize);
        y -= 6;
        y -= config.SubtitleFontSize;
        if (config.DrawsTitleBand && !string.IsNullOrEmpty(report.Subtitle))
            DrawCentered(writer, Scrub(report.Subtitle), left, right, y, config.SubtitleFontSize);
        y -= 8;

        // Column header band + rule (the caption row is always bold). Suppressed on pre-printed stationery for
        // the same reason; the rule is also dropped by a Quick/Draft pass, which draws no ruling ink at all.
        y -= config.FormattedHeaderFontSize;
        if (config.DrawsColumnHeaderBand)
            DrawRowCells(writer, report, config, colX, HeaderRow(report), y, config.FormattedHeaderFontSize);
        double ruleY = y - 3;
        if (config.DrawsRules && config.DrawsColumnHeaderBand)
            writer.Line(left, ruleY, right, ruleY, 0.7);
        y -= 6;

        // Body rows.
        foreach (var row in rows)
        {
            y -= config.FormattedRowHeight;
            double baseline = y + (config.FormattedRowHeight - config.FormattedBodyFontSize) / 2.0;
            if (row.IsTotal && config.DrawsRules)
                writer.Line(left, y + config.FormattedRowHeight - 2, right, y + config.FormattedRowHeight - 2, 0.5);
            DrawRowCells(writer, report, config, colX, row, baseline, config.FormattedBodyFontSize);
        }

        // Footer.
        string footer = (config.FooterText ?? string.Empty)
            .Replace("{page}", pageNo.ToString(CultureInfo.InvariantCulture))
            .Replace("{pages}", pageCount.ToString(CultureInfo.InvariantCulture));
        if (footer.Length > 0)
        {
            double fy = config.MarginBottom;
            DrawCentered(writer, footer, left, right, fy, config.FooterFontSize);
        }
    }

    /// <summary>
    /// The column-caption band, which is CHROME: every caption is a compile-time string this product authored
    /// ("Particulars", "Debit", "Closing Qty"), so it keeps the ER-11 scrub that <see cref="DrawRowCells"/> no
    /// longer applies to body cells (Ruling 18). The scrub is conditional, so a clean caption — which is all of
    /// them today — is byte-identical and the band's measured widths do not move (ER-13).
    /// </summary>
    private static PrintRow HeaderRow(PrintReport report)
    {
        var cells = new string[report.Columns.Count];
        for (int i = 0; i < cells.Length; i++) cells[i] = Scrub(report.Columns[i].Header);
        return new PrintRow { Cells = cells, IsHeader = true };
    }

    private static void DrawRowCells(
        PdfWriter writer, PrintReport report, PageConfig config, double[] colX,
        PrintRow row, double baseline, double fontSize)
    {
        int n = report.Columns.Count;
        // 🔴 The SAME constant ComputeColumnX measures against. These were two independent literal 2s; a column
        // measured against one padding and drawn into another is a figure that "fits" and is clipped anyway.
        double pad = CellPad;
        // Section headers and total rows render bold so they stand out from body rows (RQ-9 fidelity).
        bool bold = row.IsHeader || row.IsTotal;
        for (int i = 0; i < n; i++)
        {
            // 🔴 RULING 18: a body cell is BOOK DATA and is drawn VERBATIM. Every one of them is projected from a
            // master or a voucher — a party or bank name, a narration, a bill reference, a formatted amount — so
            // the de-brand that used to run here rewrote a real customer's legal name on the printed page of the
            // very document sent to them, and on every report about them. The header BAND still goes through the
            // scrub (see HeaderRow), because those captions are strings this product authored; it shares this
            // method purely for geometry.
            string text = i < row.Cells.Count ? (row.Cells[i] ?? string.Empty) : string.Empty;
            if (i == 0 && row.Indent > 0)
                text = new string(' ', row.Indent) + text;
            if (text.Length == 0) continue;

            var align = report.Columns[i].Align;
            double cellLeft = colX[i] + pad;
            double cellRight = colX[i + 1] - pad;
            double cellWidth = cellRight - cellLeft;

            // Clip long text to the column's inner width so it never overflows into the next column or past
            // the page's right edge (viewers otherwise just draw it clipped and columns misalign).
            text = PdfWriter.FitToWidth(text, cellWidth, fontSize);
            if (text.Length == 0) continue;

            double textW = PdfWriter.MeasureHelvetica(text, fontSize);
            double x = align switch
            {
                CellAlign.Right => cellRight - textW,
                CellAlign.Center => (cellLeft + cellRight) / 2.0 - textW / 2.0,
                _ => cellLeft,
            };
            if (x < cellLeft) x = cellLeft;
            writer.Text(x, baseline, text, fontSize, bold);
        }
    }

    private static void DrawCentered(PdfWriter writer, string text, double left, double right, double y, double fontSize)
    {
        if (string.IsNullOrEmpty(text)) return;
        double w = PdfWriter.MeasureHelvetica(text, fontSize);
        double x = (left + right) / 2.0 - w / 2.0;
        if (x < left) x = left;
        writer.Text(x, y, text, fontSize);
    }

    /// <summary>
    /// The heading actually drawn on the page. A product-authored title keeps the ER-11 scrub; a title the
    /// producer has marked as carrying a master name (<see cref="PrintReport.TitleCarriesMasterName"/>) is drawn
    /// VERBATIM, because under Ruling 18 the name of a customer, supplier or bank is theirs and not ours to
    /// rewrite. The product-authored half of such a heading ("Ledger Account - ") is a compile-time constant that
    /// can never carry the brand, so waiving the scrub over the whole string leaks nothing of ours.
    /// </summary>
    private static string HeadingText(PrintReport report)
        => report.TitleCarriesMasterName ? report.Title ?? string.Empty : Scrub(report.Title);

    /// <summary>
    /// Keeps the <c>/Title</c> metadata brand-safe (never emits a third-party brand into the PDF).
    ///
    /// <para>🔴 <b>Deliberately still unconditional under Ruling 18</b>, which lists PDF <c>/Title</c> metadata
    /// among the strings this product authors about itself and must keep de-branding. The consequence is worth
    /// stating plainly: for a ledger-account sheet whose heading is a master name, the VISIBLE heading now keeps
    /// that name intact while the document-properties <c>/Title</c> still has the token stripped. Nothing a
    /// counterparty reads on the page is altered; only the file's metadata differs. If that divergence is not
    /// wanted, the fix is to route this through <see cref="HeadingText"/> too — a user decision, not a silent
    /// one.</para>
    /// </summary>
    private static string SafeTitle(string title)
        => string.IsNullOrWhiteSpace(title) ? "Apex Solutions Report" : Scrub(title) + " — Apex Solutions";

    /// <summary>
    /// 🔴 <b>ER-11 de-branding for CHROME ONLY.</b> W2-32 added this because nothing scrubbed the report's own
    /// title or subtitle, so a heading carrying the forbidden brand printed it on the page and in the
    /// <c>/Title</c>. W2-32 then applied it far too widely — to the title unconditionally and to every body cell
    /// — and the reachable case its own note described ("a multi-account job titles each sheet with a LEDGER NAME
    /// the user typed") is precisely the case Ruling 18 says must NOT be scrubbed. So the guard now runs over
    /// what this product wrote: the column captions, the subtitle (a company name and a date range — ours), a
    /// product-authored title, and the <c>/Title</c> metadata. It does NOT run over body cells or over a title
    /// the producer marked as a master name.
    ///
    /// <para>The scrub is applied only when the brand is actually present, because
    /// <see cref="Debrand.Text"/> also collapses whitespace runs — an unconditional call would have moved the
    /// bytes of every clean document that has a double space in its subtitle, and this suite's goldens with it
    /// (ER-13).</para>
    /// </summary>
    private static string Scrub(string? text)
        => Debrand.Contains(text) ? Debrand.Text(text) : (text ?? string.Empty);
}
