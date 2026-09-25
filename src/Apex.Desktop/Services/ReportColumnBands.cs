using System;
using System.Collections.Generic;
using Apex.Desktop.ViewModels;

namespace Apex.Desktop.Services;

/// <summary>
/// The ONE column band for every report that renders through the generic <see cref="ReportRow"/>
/// <c>Col1..Col8</c> cells — read by <see cref="ReportTabularProjector"/> (export) and
/// <see cref="ReportPrintProjector"/> (print) alike, so a report's screen, its spreadsheet and its printed page
/// cannot disagree about what a column is called or which cell it holds.
///
/// <para><b>Why this type exists.</b> Before it, the two egress paths captioned the generic reports from two
/// different places and neither was complete:
/// <list type="bullet">
///   <item>EXPORT captioned from a private <c>HeadersFor</c> switch in the tabular projector that mapped
///   <b>16</b> of the <b>37</b> kinds that reach this path — the other 21 exported a blank header row.</item>
///   <item>PRINT had no per-kind caption table <i>at all</i>: it emitted a hardcoded "Particulars" and then
///   <c>new PrintColumn(string.Empty, …)</c> for every remaining column, so <b>zero</b> of the 37 carried a
///   printed caption.</item>
/// </list></para>
///
/// <para>🔴 <b>A BAND IS A LIST OF CELL SELECTORS, NOT A LIST OF STRINGS, AND THAT IS THE WHOLE POINT.</b> A
/// caption array indexed positionally over <c>Col1..Col8</c> cannot express what three shipped reports actually
/// do, and captioning them positionally would have put a believable heading over the wrong figures — which is
/// worse than the blank one it replaced:
/// <list type="bullet">
///   <item><b>CST declaration forms deliberately skip <c>Col4</c></b> (it held the party's CST No. until the
///   XAML layout invariants caught eight columns starving the party-name star column to 6px). A positional
///   band would caption Gross Amount over an empty cell and shift every column after it.</item>
///   <item><b>Batchwise, Batch Age Analysis and Price List keep their last MONEY column in
///   <see cref="ReportRow.Secondary"/></b>, not in a <c>Col</c>. The generic projectors only ever read
///   <c>Col1..Col8</c>, so that column — Value / Net Rate — was <b>silently absent from every export and every
///   printed copy</b> while being present on screen. A selector reaches it; an index cannot.</item>
/// </list></para>
///
/// <para><b>Captions are the screen's own, verbatim.</b> Each band below is transcribed from that report's
/// <c>colHdr</c> headers in <c>MainWindow.axaml</c> and its builder's cell assignments in
/// <see cref="ReportsViewModel"/>, which is why this file names the source cell beside every caption: a reviewer
/// can check a row of this table against the grid and the builder without running anything. The statutory bands
/// take their TDS/TCS-varying captions from the view model's own <c>Stat*Header</c> properties rather than
/// restating them, so the pair cannot drift.</para>
///
/// <para><b>Verbatim includes the ₹ in "Value ₹" / "Rate ₹" / "Net Rate ₹".</b> An earlier draft of this file
/// quietly shortened those three because the PDF writer is ASCII-only — which would have made the document
/// disagree with the screen about a column's unit for this file's convenience rather than the reader's. The
/// fold is the print path's job and it already does it: <see cref="ReportPrintProjector.Ascii"/> renders ₹ as
/// "Rs.", so the page prints "Value Rs." while the Unicode CSV/XLSX keeps "Value ₹", and both agree with the
/// grid.</para>
///
/// <para>These captions are compile-time chrome this product authored, never book data, so they are safe to
/// carry into a de-branded export under ruling 18. No brand text is introduced.</para>
/// </summary>
public static class ReportColumnBands
{
    /// <summary>One column of a generic report: its caption, the cell it reads, and whether it is a figure
    /// column (right-aligned when printed; typed as a spreadsheet Number when the cell parses as one).</summary>
    public readonly record struct Spec(string Caption, Func<ReportRow, string> Cell, bool IsNumeric);

    private static Spec Text(string caption, Func<ReportRow, string> cell) => new(caption, cell, false);
    private static Spec Num(string caption, Func<ReportRow, string> cell) => new(caption, cell, true);

    // The eight generic cells, named once so a band reads like the builder it mirrors.
    private static readonly Func<ReportRow, string> C1 = r => r.Col1;
    private static readonly Func<ReportRow, string> C2 = r => r.Col2;
    private static readonly Func<ReportRow, string> C3 = r => r.Col3;
    private static readonly Func<ReportRow, string> C4 = r => r.Col4;
    private static readonly Func<ReportRow, string> C5 = r => r.Col5;
    private static readonly Func<ReportRow, string> C6 = r => r.Col6;
    private static readonly Func<ReportRow, string> C7 = r => r.Col7;
    private static readonly Func<ReportRow, string> C8 = r => r.Col8;

    /// <summary>The cell three inventory reports park their last money column in. Named, because reading
    /// <see cref="ReportRow.Secondary"/> from a column band is surprising unless you know why.</summary>
    private static readonly Func<ReportRow, string> Sec = r => r.Secondary;

    private static readonly Spec[] None = Array.Empty<Spec>();

    /// <summary>
    /// 🔴 THE LABEL CELL OF AN <b>ACCOUNTING</b> REPORT — COMPOSED ONCE, FOR BOTH EGRESS PATHS, THE WAY THE
    /// SCREEN COMPOSES IT.
    ///
    /// <para>The accounting family does not take a band: its columns are the fixed
    /// Particulars/Debit/Credit/Amount set. But its LABEL column is not just <see cref="ReportRow.Particulars"/>.
    /// On screen that cell is two runs — <c>Particulars</c> then <c>Secondary</c> in smaller grey ink, one
    /// <c>TextBlock</c> at <c>MainWindow.axaml:782-786</c> — and <b>both projectors emitted only the first</b>,
    /// because <c>Secondary</c> has no column of its own in that projection. Everything the builders put there
    /// was therefore on screen and absent from every printed page, PDF, CSV, XLSX and emailed copy.</para>
    ///
    /// <para><b>FOURTEEN kinds were MEASURED losing a suffix</b> — by reverting this method to its predecessor and
    /// running the sweep in <c>ReportColumnBandCoverageTests</c> over the populated fixture: Trial Balance,
    /// Balance Sheet, Day Book, Cash Flow, Funds Flow, the Optional / Reversing-Journal / Memorandum registers,
    /// POS Register, and the Sales / Purchase / Journal / Credit-Note / Debit-Note registers.</para>
    ///
    /// <para><b>These further call sites were found by READING the builders and are NOT exercised by that
    /// fixture</b>, which posts no cheque book and no bank statement, so those reports sit in their empty state.
    /// They are named because the loss is in the same one line — not because a test proved each one:
    /// <b>Cheque Register</b> (census 8.5, <c>:4876</c>) <c>"Unreconciled N · Reconciled M"</c>, the status counts
    /// that are the summary's entire purpose; <b>Cheque Register</b> detail (<c>:4931</c>); <b>Deposit Slip</b>
    /// (8.6, <c>:5031</c>) and <b>e-Payments</b> (8.10, <c>:5146</c>), the <c>"Vch No. …"</c> that ties a line on
    /// the slip back to a voucher; <b>Ledger Monthly Summary</b> (<c>:7428</c>, <c>:7436</c>, <c>:7446</c>),
    /// opening and closing <b>money</b>; <b>Negative Stock</b> (<c>:4364</c>), godown and negative quantity;
    /// <b>Statistics</b> (<c>:7470</c>, <c>:7478</c>), cancelled-voucher counts. A fixture that posts banking
    /// data would extend the measured set; that is a fixture job, recorded rather than claimed.</para>
    ///
    /// <para><b>It replaces a narrower special case rather than sitting beside one.</b> Both projectors already
    /// carried ONE fact out of <c>Secondary</c> by hand: a cancelled Day Book row got <c>Particulars + "
    /// (Cancelled)"</c> appended, because a cancelled ₹50,000 receipt printing identically to a live one is
    /// indefensible. That was the right instinct applied to one row type out of dozens, and it also
    /// <i>reworded</i> the fact — the builder writes <c>"(Cancelled) " + secondary</c>, so the hand-rolled
    /// version dropped whatever else that row's <c>Secondary</c> held. Carrying the cell verbatim subsumes it
    /// exactly and keeps the document reading like the screen.</para>
    /// </summary>
    public static string AccountingLabel(ReportRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return string.IsNullOrWhiteSpace(row.Secondary)
            ? row.Particulars
            : row.Particulars + "  " + row.Secondary;
    }

    /// <summary>True when <paramref name="kind"/> renders through the generic <c>Col1..Col8</c> path and must
    /// therefore declare a band. Mirrors the projectors' own routing: not accounting (those carry
    /// Particulars/Debit/Credit/Amount), and not the wide matrix (that carries its own live column band).</summary>
    public static bool NeedsBand(ReportsViewModel vm)
    {
        ArgumentNullException.ThrowIfNull(vm);
        return !vm.IsAccountingReport && !vm.IsPayrollMatrix && !vm.IsPayslipReport;
    }

    /// <summary>
    /// The column band for the report <paramref name="vm"/> currently holds, or an empty list when the kind does
    /// not render through the generic cells. Takes the view model rather than the bare kind because several
    /// statutory captions are a function of view-model state (Section vs Coll. Code, Deducted vs Collected).
    /// </summary>
    public static IReadOnlyList<Spec> For(ReportsViewModel vm)
    {
        ArgumentNullException.ThrowIfNull(vm);

        return vm.Kind switch
        {
            // ---------------------------------------------------------------- inventory (slice 3.4b and later)
            ReportKind.StockSummary => new[]
            {
                Text("Stock Item", C1), Num("Inward", C2), Num("Outward", C3),
                Num("Closing Qty", C4), Num("Rate", C5), Num("Value", C6),
            },

            ReportKind.GodownSummary => new[]
            {
                Text("Godown", C1), Text("Stock Item", C2), Num("Quantity", C3), Num("Value", C4),
            },

            ReportKind.StockItemMovement => new[]
            {
                Text("Date", C1), Text("Voucher Type", C2), Num("Inward", C3),
                Num("Outward", C4), Num("Balance", C5), Num("Value", C6),
            },

            ReportKind.ReorderStatus => new[]
            {
                Text("Stock Item", C1), Num("Closing", C2), Num("Reorder Level", C3),
                Num("Pending POs", C4), Num("SOs Due", C5), Num("Shortfall", C6),
                Num("Order to be Placed", C7),
            },

            ReportKind.PhysicalStockRegister => new[]
            {
                Text("Date", C1), Text("Stock Item", C2), Text("Godown", C3),
                Num("Book", C4), Num("Counted", C5), Num("Variance", C6),
            },

            ReportKind.OrderRegister => new[]
            {
                Text("Date", C1), Text("Voucher", C2), Text("Party", C3), Text("Stock Item", C4),
                Text("Godown", C5), Num("Ordered", C6), Num("Pending", C7), Num("Rate", C8),
            },

            // The three allocation registers and the two material registers share one builder, so they share
            // one band (BuildAllocationRegister / BuildMaterialRegister: Date | No. | Party | Item | Godown |
            // Qty | Rate | Value).
            ReportKind.ReceiptNoteRegister or ReportKind.DeliveryNoteRegister or ReportKind.RejectionRegister
            or ReportKind.MaterialInRegister or ReportKind.MaterialOutRegister => new[]
            {
                Text("Date", C1), Text("No.", C2), Text("Party", C3), Text("Stock Item", C4),
                Text("Godown", C5), Num("Qty", C6), Num("Rate", C7), Num("Value", C8),
            },

            ReportKind.JobWorkInOrderBook or ReportKind.JobWorkOutOrderBook => new[]
            {
                Text("Date", C1), Text("Order No.", C2), Text("Party", C3), Text("Item", C4),
                Text("Track", C5), Num("Ordered", C6), Num("Fulfilled", C7), Num("Pending", C8),
            },

            // 🔴 Value comes from Secondary, not a Col — see the type doc. Before this band the exported and
            // printed Batchwise had no value column at all.
            ReportKind.Batchwise => new[]
            {
                Text("Item", C1), Text("Batch", C2), Text("Mfg", C3), Text("Expiry", C4),
                Text("Godown", C5), Num("Inward", C6), Num("Outward", C7), Num("Closing", C8),
                Num("Value ₹", Sec),
            },

            ReportKind.BatchAgeAnalysis => new[]
            {
                Text("Item", C1), Text("Batch", C2), Text("Mfg", C3), Text("Expiry", C4),
                Text("Days", C5), Text("Godown", C6), Num("Qty", C7),
                Num("Value ₹", Sec),
            },

            // "onwards" is a legitimate value in the To-quantity column, so it stays a figure column and the
            // projectors' parse guard leaves that one cell as text rather than coercing it.
            ReportKind.PriceList => new[]
            {
                Text("Price Level", C1), Text("Item", C2), Text("Applicable From", C3),
                Num("From", C4), Num("To", C5), Num("Rate ₹", C6), Num("Disc %", C7),
                Num("Net Rate ₹", Sec),
            },

            ReportKind.PurchaseBillsPending or ReportKind.SalesBillsPending => new[]
            {
                Text("Date", C1), Text("Tracking No.", C2), Text("Stock Item", C3), Text("Party", C4),
                Num("Recd.", C5), Num("Billed", C6), Num("Pending", C7),
            },

            ReportKind.StockItemCostAnalysis or ReportKind.StockGroupCostAnalysis
            or ReportKind.CostTrackBreakup => new[]
            {
                Text("Particulars", C1), Num("Cost (Expense)", C2), Num("Revenue (Income)", C3),
                Num("Balance at Cost", C4), Num("Profit/Loss", C5),
            },

            ReportKind.JobWorkAnalysis => new[]
            {
                Text("Particulars", C1), Num("Amount", C2),
            },

            // ---------------------------------------------------------------- GST (slice 4d)
            ReportKind.TaxAnalysis => new[]
            {
                Text("Rate / Head", C1), Num("CGST", C2), Num("SGST", C3),
                Num("IGST", C4), Num("Taxable", C5), Num("Tax", C6),
            },

            ReportKind.Gstr1 => new[]
            {
                Text("Party / HSN", C1), Text("GSTIN / Description", C2), Text("Invoice / UQC", C3),
                Text("POS / Qty", C4), Num("Taxable", C5), Num("CGST", C6), Num("SGST", C7), Num("IGST", C8),
            },

            // Census 6.9 — Cess is a real column of the form (Table 3.1(d) and Table 4(B)). It was once missing
            // here as well as on the grid, so an exported GSTR-3B dropped a cess figure the screen had computed.
            ReportKind.Gstr3b => new[]
            {
                Text("Particulars", C1), Num("Taxable Value", C2), Num("CGST", C3),
                Num("SGST", C4), Num("IGST", C5), Num("Cess", C6),
            },

            // ---------------------------------------------------------------- statutory TDS/TCS (Phase 7 slice 8)
            // The captions that differ between the TDS and TCS twins come from the view model's own properties,
            // which are what the shared XAML grid binds, so grid and document cannot drift.
            ReportKind.TdsOutstanding or ReportKind.TcsOutstanding => new[]
            {
                Text(vm.StatCodeHeader, C1), Text("Nature", C2), Num(vm.StatWithheldHeader, C3),
                Num("Deposited", C4), Num("Outstanding", C5), Text("Overdue Days", C6),
            },

            ReportKind.TdsNotDeducted or ReportKind.TcsNotCollected => new[]
            {
                Text("Date", C1), Text("Party", C2), Text("Sec./Nature", C3), Num("Assessable", C4),
                Num("Cumulative", C5), Num("Threshold", C6), Num("Shortfall", C7),
            },

            ReportKind.TdsInterest or ReportKind.TcsInterest => new[]
            {
                Text("Party", C1), Text(vm.StatCodeHeader, C2), Num(vm.StatTaxHeader, C3),
                Text(vm.StatEventDateHeader, C4), Text("Deposit Date", C5), Text("Due Date", C6),
                Num("Months", C7), Num(vm.StatInterestHeader, C8),
            },

            ReportKind.TdsNatureSummary or ReportKind.TcsNatureSummary => new[]
            {
                Text(vm.StatCodeHeader, C1), Text("Nature", C2), Num("Assessable", C3),
                Num(vm.StatWithheldHeader, C4), Num("Deposited", C5), Num("Outstanding", C6),
                Num("Below Threshold", C7),
            },

            ReportKind.LedgersWithoutPan => new[]
            {
                Text("Party", C1), Text("Deductee/Collectee", C2), Text("PAN?", C3),
                Text("Sections / Codes", C4), Num("Tax @ No-PAN", C5),
            },

            // ---------------------------------------------------------------- State VAT & CST (census 15.5 / 15.6)
            ReportKind.VatComputation => new[]
            {
                Text("Particulars", C1), Num("Assessable Value", C2), Num("Tax", C3), Num("Transactions", C4),
            },

            // 🔴 Col4 IS SKIPPED, exactly as the builder and the grid skip it — see the type doc. Captioning this
            // band positionally would slide Gross Amount onto an empty cell and every column after it by one.
            ReportKind.CstFormsReceivable or ReportKind.CstFormsIssuable => new[]
            {
                Text("Date", C1), Text("Vch No.", C2), Text("Party", C3),
                Num("Gross Amount", C5), Text("Form", C6), Text("Form No.", C7), Text("Form Date", C8),
            },

            _ => None,
        };
    }
}
