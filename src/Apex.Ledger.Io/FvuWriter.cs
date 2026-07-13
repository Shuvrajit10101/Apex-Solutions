using System.Globalization;
using System.Text;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;

namespace Apex.Ledger.Io;

/// <summary>
/// Serializes a <see cref="Form26Q"/> (TDS) or <see cref="Form27EQ"/> (TCS) return to the NSDL/Protean
/// <b>FVU-compatible</b> offline flat-file (Phase 7 slices 4/6; catalog §13). The layout is the caret
/// (<c>^</c>)-delimited, one-record-per-line eTDS/eTCS text format the government File Validation Utility consumes:
/// a <b>File Header</b> (FH), a <b>Batch/collector(deductor) Header</b> (BH), then per attributed challan a
/// <b>Challan Detail</b> (CD) record followed by its detail records — <b>Deductee Detail</b> (DD) for 26Q or
/// <b>Collectee Detail</b> (CL) for 27EQ — and a <b>File Trailer</b> (FT) carrying the control totals. This mirrors
/// the deterministic, byte-stable discipline of <see cref="CsvWriter"/>/<see cref="TabularExport"/>: no clock, no
/// RNG, invariant-culture number and date formatting, and every free-text field de-branded (ER-11) so the produced
/// file can never leak a third-party accounting brand. The output is pinned to <see cref="FvuVersion"/>.
/// <para>
/// This produces the upload file offline and <b>emulates</b> the FVU control-total checks (see
/// <see cref="Form26QControlTotals"/> / <see cref="Form27EQControlTotals"/>); it does not bundle the government
/// FVU/RPU JARs and performs no online TRACES/portal upload (project decision D4). An empty return still yields a
/// valid header-only file.
/// </para>
/// </summary>
public static class FvuWriter
{
    /// <summary>The pinned FVU file-format version this writer targets. The eTDS/FVU layout is versioned by
    /// Protean (NSDL) and revised periodically — this constant pins the build to one version.</summary>
    public const string FvuVersion = "8.9";

    /// <summary>The TDS statement (form) type.</summary>
    private const string FormType = "26Q";

    /// <summary>The TCS statement (form) type.</summary>
    private const string FormType27EQ = "27EQ";

    /// <summary>The §192 salary-TDS statement (form) type.</summary>
    private const string FormType24Q = "24Q";

    /// <summary>The record-field delimiter (caret) and the record separator (LF) of the flat file.</summary>
    private const char Delimiter = '^';
    private const string RecordSeparator = "\n";

    /// <summary>Serializes <paramref name="return26Q"/> to FVU-compatible flat-file bytes (UTF-8, no BOM). Pure,
    /// deterministic and byte-stable for a fixed return.</summary>
    public static byte[] Write(Form26Q return26Q)
    {
        ArgumentNullException.ThrowIfNull(return26Q);

        var sb = new StringBuilder();

        // Control totals are derived from the DD (deductee) records ACTUALLY written — the per-challan rows — not
        // from the full-quarter Deductees projection: an undeposited / short-deposited in-quarter deduction produces
        // no (or a smaller) DD line, so counting the projection would overstate the DD rows in the file and make the
        // FH / BH / FT record counts disagree with the file's own contents. The report-level reconciliation gap
        // (gross deducted vs. deposited) lives in Form26QControlTotals; the file totals below describe the file.
        var totals = return26Q.ControlTotals;
        int ddRecordCount = return26Q.Challans.Sum(c => c.DeducteeRows.Count);
        var ddTotalTds = new Money(return26Q.Challans.Sum(c => c.DeducteeRows.Sum(r => r.TdsAmount.Amount)));
        var ddTotalPaid = new Money(return26Q.Challans.Sum(c => c.DeducteeRows.Sum(r => r.AmountPaid.Amount)));

        // ---- File Header (FH): record type, form, pinned version, TAN, FY, quarter, deductor type, total records.
        int totalRecords = 2 + return26Q.Challans.Count + ddRecordCount + 1; // FH + BH + CD* + DD* + FT
        WriteRecord(sb,
            "FH", FormType, FvuVersion, Text(return26Q.Deductor.Tan),
            return26Q.FinancialYearLabel, return26Q.QuarterLabel,
            return26Q.Deductor.DeductorType.ToString(), Int(totalRecords));

        // ---- Batch / deductor Header (BH): the person-responsible identity from F11.
        var d = return26Q.Deductor;
        WriteRecord(sb,
            "BH", Text(d.Tan), Text(d.ResponsiblePersonName), Text(d.ResponsiblePersonPan),
            Text(d.ResponsiblePersonDesignation), Text(d.ResponsiblePersonAddress),
            Int(return26Q.Challans.Count), Int(ddRecordCount));

        // ---- Per-challan: a Challan Detail (CD) then its Deductee Detail (DD) rows.
        int challanSeq = 0;
        foreach (var ch in return26Q.Challans)
        {
            challanSeq++;
            WriteRecord(sb,
                "CD", Int(challanSeq), Text(ch.ChallanNo), Text(ch.BsrCode),
                Date(ch.DepositDate), Money(ch.Amount), Text(ch.Section), Text(ch.MinorHead),
                Int(ch.DeducteeRows.Count));

            int deducteeSeq = 0;
            foreach (var row in ch.DeducteeRows)
            {
                deducteeSeq++;
                WriteDeductee(sb, challanSeq, deducteeSeq, row);
            }
        }

        // ---- File Trailer (FT): the file's own control totals — deductee/challan record counts and money totals
        // over the DD rows actually written, so the trailer is internally consistent with the file's contents.
        WriteRecord(sb,
            "FT", Int(ddRecordCount), Int(totals.ChallanRecordCount),
            Money(ddTotalTds), Money(ddTotalPaid),
            Money(totals.TotalDepositedAsPerChallans));

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    /// <summary>Serializes a <see cref="Form24Q"/> §192 salary-TDS return to FVU-compatible flat-file bytes (UTF-8,
    /// no BOM). Pure, deterministic and byte-stable for a fixed return, sharing the 26Q/27EQ FH/BH/DD/FT record
    /// framing and field encoders (this <b>extends</b> the one writer — it does not fork a parallel serializer;
    /// Phase 8 slice 7, F4). Salary-TDS has no Phase-7 challan/deposit block yet (that integration is a documented
    /// carry-forward), so the file carries the Annexure I deductee rows <b>directly</b> — no CD (challan) record — with
    /// the trailer's control totals derived from the DD rows ACTUALLY written, so the file trailer's total §192 TDS
    /// equals the Annexure I control total by construction. Every free-text field is de-branded (ER-11). An empty
    /// return still yields a valid header-only file.</summary>
    public static byte[] Write(Form24Q return24Q)
    {
        ArgumentNullException.ThrowIfNull(return24Q);

        var sb = new StringBuilder();
        var d = return24Q.Deductor;

        int ddRecordCount = return24Q.Deductees.Count;
        var ddTotalTds = new Money(return24Q.Deductees.Sum(r => r.TdsAmount.Amount));

        // ---- File Header (FH): record type, form 24Q, pinned version, TAN, FY, quarter, deductor type, total records.
        int totalRecords = 2 + ddRecordCount + 1; // FH + BH + DD* + FT
        WriteRecord(sb,
            "FH", FormType24Q, FvuVersion, Text(d.Tan),
            return24Q.FinancialYearLabel, return24Q.QuarterLabel,
            d.DeductorType.ToString(), Int(totalRecords));

        // ---- Batch / deductor Header (BH): the person-responsible identity from F11; no challan block (0).
        WriteRecord(sb,
            "BH", Text(d.Tan), Text(d.ResponsiblePersonName), Text(d.ResponsiblePersonPan),
            Text(d.ResponsiblePersonDesignation), Text(d.ResponsiblePersonAddress),
            Int(0), Int(ddRecordCount));

        // ---- Annexure I: one salary Deductee Detail (DD) per §192 withholding — PAN, name, section, date, TDS.
        int seq = 0;
        foreach (var row in return24Q.Deductees)
        {
            seq++;
            WriteRecord(sb,
                "DD", Int(seq), Text(row.Pan), Text(row.EmployeeName),
                Text(row.SectionCode), Date(row.DeductionDate), Money(row.TdsAmount));
        }

        // ---- File Trailer (FT): the file's own control totals — deductee record count, Σ §192 TDS (== the Annexure I
        // control total), and the Q4 Annexure II record count + tax deducted — so the trailer describes the file.
        var totals = return24Q.ControlTotals;
        WriteRecord(sb,
            "FT", Int(ddRecordCount), Money(ddTotalTds),
            Int(totals.AnnexureIIRecordCount), Money(totals.AnnexureIITaxDeducted));

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static void WriteDeductee(StringBuilder sb, int challanSeq, int seq, Form26QDeducteeRow row) =>
        WriteRecord(sb,
            "DD", Int(challanSeq), Int(seq), Text(row.DeducteePan), Text(row.DeducteeName),
            Text(row.SectionCode), Text(row.FvuSectionCode), Date(row.DeductionDate),
            Money(row.AmountPaid), Money(row.TdsAmount), Rate(row.RateBasisPoints),
            row.PanApplied ? "Y" : "N", Text(row.Section197Reason));

    /// <summary>Serializes <paramref name="return27EQ"/> to FVU-compatible flat-file bytes (UTF-8, no BOM). Pure,
    /// deterministic and byte-stable for a fixed return. The exact mirror of the 26Q <see cref="Write(Form26Q)"/>:
    /// FH (form 27EQ) / BH (collector) / per-challan CD + its CL (Collectee Detail) rows / FT — with the file's
    /// control totals derived from the CL rows ACTUALLY written (not the full-quarter projection), so an undeposited /
    /// short-deposited in-quarter collection never overstates the file's own record counts or money totals.</summary>
    public static byte[] Write(Form27EQ return27EQ)
    {
        ArgumentNullException.ThrowIfNull(return27EQ);

        var sb = new StringBuilder();

        // Control totals are derived from the CL (collectee) records ACTUALLY written — the per-challan rows — not
        // from the full-quarter Collectees projection: an undeposited / short-deposited in-quarter collection produces
        // no (or a smaller) CL line, so counting the projection would overstate the CL rows in the file and make the
        // FH / BH / FT record counts disagree with the file's own contents. The report-level reconciliation gap
        // (gross collected vs. deposited) lives in Form27EQControlTotals; the file totals below describe the file.
        var totals = return27EQ.ControlTotals;
        int clRecordCount = return27EQ.Challans.Sum(c => c.CollecteeRows.Count);
        var clTotalTcs = new Money(return27EQ.Challans.Sum(c => c.CollecteeRows.Sum(r => r.TcsAmount.Amount)));
        var clTotalReceived = new Money(return27EQ.Challans.Sum(c => c.CollecteeRows.Sum(r => r.AmountReceived.Amount)));

        // ---- File Header (FH): record type, form, pinned version, TAN, FY, quarter, collector type, total records.
        int totalRecords = 2 + return27EQ.Challans.Count + clRecordCount + 1; // FH + BH + CD* + CL* + FT
        WriteRecord(sb,
            "FH", FormType27EQ, FvuVersion, Text(return27EQ.Collector.Tan),
            return27EQ.FinancialYearLabel, return27EQ.QuarterLabel,
            return27EQ.Collector.CollectorType.ToString(), Int(totalRecords));

        // ---- Batch / collector Header (BH): the person-responsible identity from F11.
        var col = return27EQ.Collector;
        WriteRecord(sb,
            "BH", Text(col.Tan), Text(col.ResponsiblePersonName), Text(col.ResponsiblePersonPan),
            Text(col.ResponsiblePersonDesignation), Text(col.ResponsiblePersonAddress),
            Int(return27EQ.Challans.Count), Int(clRecordCount));

        // ---- Per-challan: a Challan Detail (CD) then its Collectee Detail (CL) rows.
        int challanSeq = 0;
        foreach (var ch in return27EQ.Challans)
        {
            challanSeq++;
            WriteRecord(sb,
                "CD", Int(challanSeq), Text(ch.ChallanNo), Text(ch.BsrCode),
                Date(ch.DepositDate), Money(ch.Amount), Text(ch.CollectionCode), Text(ch.MinorHead),
                Int(ch.CollecteeRows.Count));

            int collecteeSeq = 0;
            foreach (var row in ch.CollecteeRows)
            {
                collecteeSeq++;
                WriteCollectee(sb, challanSeq, collecteeSeq, row);
            }
        }

        // ---- File Trailer (FT): the file's own control totals — collectee/challan record counts and money totals
        // over the CL rows actually written, so the trailer is internally consistent with the file's contents.
        WriteRecord(sb,
            "FT", Int(clRecordCount), Int(totals.ChallanRecordCount),
            Money(clTotalTcs), Money(clTotalReceived),
            Money(totals.TotalDepositedAsPerChallans));

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static void WriteCollectee(StringBuilder sb, int challanSeq, int seq, Form27EQCollecteeRow row) =>
        WriteRecord(sb,
            "CL", Int(challanSeq), Int(seq), Text(row.CollecteePan), Text(row.CollecteeName),
            Text(row.CollectionCode), Text(row.FvuCollectionCode), Date(row.CollectionDate),
            Money(row.AmountReceived), Money(row.TcsAmount), Rate(row.RateBasisPoints),
            row.PanApplied ? "Y" : "N", Text(row.LowerCollectionReason));

    // ---- field encoders (invariant, de-branded, delimiter-safe) ----

    private static string Text(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        // De-brand (ER-11) then strip the delimiter/record separators so a stray caret or newline in a user field
        // can never corrupt the record framing. Deterministic; no culture leak.
        var cleaned = Debrand.Text(value);
        return cleaned.Replace(Delimiter, ' ').Replace('\r', ' ').Replace('\n', ' ');
    }

    private static string Int(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>Money as an invariant 2-dp figure (paisa-exact), matching the CSV/XLSX number discipline.</summary>
    private static string Money(Money value) => value.Amount.ToString("0.00", CultureInfo.InvariantCulture);

    /// <summary>Rate as an invariant percentage with 2 dp (e.g. 1000 bp ⇒ "10.00").</summary>
    private static string Rate(int basisPoints) => (basisPoints / 100m).ToString("0.00", CultureInfo.InvariantCulture);

    /// <summary>Date in the eTDS <c>ddMMyyyy</c> form (invariant), the FVU deposit/deduction date format.</summary>
    private static string Date(DateOnly value) => value.ToString("ddMMyyyy", CultureInfo.InvariantCulture);

    private static void WriteRecord(StringBuilder sb, params string[] fields)
    {
        for (int i = 0; i < fields.Length; i++)
        {
            if (i > 0) sb.Append(Delimiter);
            sb.Append(fields[i]);
        }
        sb.Append(RecordSeparator);
    }
}
