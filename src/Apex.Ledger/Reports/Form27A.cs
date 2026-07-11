using Apex.Ledger.Domain;

namespace Apex.Ledger.Reports;

/// <summary>
/// <b>Form 27A</b> — the return <b>control chart</b> (the physical/verification cover a filer cross-checks and, in the
/// paper era, signed) for a Form 26Q (TDS) or Form 27EQ (TCS) quarterly return (Phase 7 slice 7; catalog §13). It is a
/// pure projection of the return's control totals — deductee/collectee record count, challan record count, total tax,
/// total amount, total deposited — plus the FVU-style cross-check messages the return itself surfaces
/// (<see cref="Form26QControlTotals.Validate"/> / <see cref="Form27EQControlTotals.Validate"/>). Because it is built
/// from the same projection, its figures tally with the return <b>by construction</b>: this is the pre-FVU control-
/// total tally. Deterministic; no UI, no DB — the PDF renderer lives in <c>Apex.Ledger.Io</c>.
/// </summary>
/// <param name="ReturnFormName">The return this chart controls ("26Q" or "27EQ").</param>
/// <param name="FinancialYearStartYear">The FY start year (04-01) of the return.</param>
/// <param name="Quarter">The return quarter (1..4).</param>
/// <param name="From">Inclusive quarter start.</param>
/// <param name="To">Inclusive quarter end.</param>
/// <param name="Tan">The deductor/collector TAN.</param>
/// <param name="DeducteeRecordCount">Deductee/collectee detail-record count.</param>
/// <param name="ChallanRecordCount">Challan-record count.</param>
/// <param name="TotalTax">Σ TDS deducted / TCS collected for the quarter.</param>
/// <param name="TotalAmount">Σ amount paid / received for the quarter.</param>
/// <param name="TotalDeposited">Σ deposit amount across the attributed challans.</param>
/// <param name="TotalTaxDepositedForQuarter">Σ tax this quarter's deductions/collections were discharged by.</param>
/// <param name="ControlValidationMessages">The FVU-style cross-check messages (empty ⇒ the return tallies).</param>
public sealed record Form27A(
    string ReturnFormName,
    int FinancialYearStartYear,
    int Quarter,
    DateOnly From,
    DateOnly To,
    string Tan,
    int DeducteeRecordCount,
    int ChallanRecordCount,
    Money TotalTax,
    Money TotalAmount,
    Money TotalDeposited,
    Money TotalTaxDepositedForQuarter,
    IReadOnlyList<string> ControlValidationMessages)
{
    /// <summary>The financial-year label (e.g. "2025-26").</summary>
    public string FinancialYearLabel => $"{FinancialYearStartYear}-{(FinancialYearStartYear + 1) % 100:00}";

    /// <summary>The quarter label ("Q1".."Q4").</summary>
    public string QuarterLabel => $"Q{Quarter}";

    /// <summary>True iff the control totals tally (no cross-check messages) — the return is clear for FVU.</summary>
    public bool Tallies => ControlValidationMessages.Count == 0;

    /// <summary>Builds the Form 27A control chart from a Form 26Q return.</summary>
    public static Form27A FromForm26Q(Form26Q return26q)
    {
        ArgumentNullException.ThrowIfNull(return26q);
        var ct = return26q.ControlTotals;
        return new Form27A(
            "26Q", return26q.FinancialYearStartYear, return26q.Quarter, return26q.From, return26q.To,
            return26q.Deductor.Tan,
            ct.DeducteeRecordCount, ct.ChallanRecordCount,
            ct.TotalTdsDeducted, ct.TotalAmountPaid, ct.TotalDepositedAsPerChallans, ct.TotalTdsDepositedForQuarter,
            ct.Validate());
    }

    /// <summary>Builds the Form 27A control chart from a Form 27EQ return.</summary>
    public static Form27A FromForm27EQ(Form27EQ return27eq)
    {
        ArgumentNullException.ThrowIfNull(return27eq);
        var ct = return27eq.ControlTotals;
        return new Form27A(
            "27EQ", return27eq.FinancialYearStartYear, return27eq.Quarter, return27eq.From, return27eq.To,
            return27eq.Collector.Tan,
            ct.CollecteeRecordCount, ct.ChallanRecordCount,
            ct.TotalTcsCollected, ct.TotalAmountReceived, ct.TotalDepositedAsPerChallans, ct.TotalTcsDepositedForQuarter,
            ct.Validate());
    }
}
