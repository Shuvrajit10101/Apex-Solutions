using Apex.Ledger.Domain;

namespace Apex.Ledger.Reports;

/// <summary>
/// The <b>deductor</b> block of a <see cref="Form16A"/> TDS certificate — the deductor's legal name, TAN, income-tax
/// PAN (derived from the company GSTIN when available; embedded chars 3–12), legal type and the person-responsible-
/// for-deduction identity captured on F11 (<see cref="TdsConfig"/>). Denormalised so the certificate renderer needs
/// no further company lookup. Mirrors <see cref="Form26QDeductor"/> but additionally carries the deductor's own name
/// and PAN (a certificate names the deductor; a return keys on the TAN).
/// </summary>
public sealed record Form16ADeductorBlock(
    string Name,
    string Tan,
    string? Pan,
    DeductorType DeductorType,
    string? ResponsiblePersonName,
    string? ResponsiblePersonPan,
    string? ResponsiblePersonDesignation,
    string? ResponsiblePersonAddress);

/// <summary>The <b>deductee</b> block of a Form 16A — the party the certificate is issued to (PAN + name).</summary>
public sealed record Form16ADeducteeBlock(Guid LedgerId, string Name, string? Pan);

/// <summary>
/// One <b>TDS summary</b> line of a Form 16A — a single withholding on the deductee within the certificate quarter:
/// the income-tax <see cref="SectionCode"/> + its Form-26Q/FVU <see cref="FvuSectionCode"/>, the
/// <see cref="Date"/> (voucher date), the <see cref="AmountPaid"/> (the GST-exclusive assessable base credited), the
/// <see cref="TdsAmount"/> withheld and the applied <see cref="RateBasisPoints"/>. Read verbatim off the
/// corresponding <see cref="Form26QDeducteeRow"/> so the certificate reconciles to the return by construction.
/// </summary>
public sealed record Form16ADeductionRow(
    string SectionCode,
    string FvuSectionCode,
    DateOnly Date,
    Money AmountPaid,
    Money TdsAmount,
    int RateBasisPoints,
    bool PanApplied)
{
    /// <summary>The applied rate as a percentage (e.g. 10.00 for 1000 bp).</summary>
    public decimal RatePercent => RateBasisPoints / 100m;
}

/// <summary>One <b>challan / deposit</b> line of a Form 16A — the ITNS-281 identification (serial + BSR + deposit
/// date) and the portion of TDS this challan deposited <b>for this deductee</b>.</summary>
public sealed record Form16AChallanRow(
    string ChallanNo,
    string BsrCode,
    DateOnly DepositDate,
    Money TdsDeposited);

/// <summary>
/// <b>Form 16A</b> — the quarterly <b>TDS certificate</b> a deductor issues to a deductee (Phase 7 slice 7; catalog
/// §13), as a pure, read-only projection over <see cref="Form26Q"/> filtered to one deductee. It carries the
/// deductor block, the deductee block, the quarter's TDS summary rows and the challan/deposit rows — all read verbatim
/// from the return so the certificate figures match Form 26Q <b>to the paisa, by construction</b>. Deterministic (no
/// clock/RNG), ordered for byte-stability. No UI, no DB — the PDF renderer lives in <c>Apex.Ledger.Io</c>.
/// </summary>
public sealed record Form16A(
    int FinancialYearStartYear,
    int Quarter,
    DateOnly From,
    DateOnly To,
    Form16ADeductorBlock Deductor,
    Form16ADeducteeBlock Deductee,
    IReadOnlyList<Form16ADeductionRow> Deductions,
    IReadOnlyList<Form16AChallanRow> Challans)
{
    /// <summary>The financial-year label (e.g. "2025-26").</summary>
    public string FinancialYearLabel => $"{FinancialYearStartYear}-{(FinancialYearStartYear + 1) % 100:00}";

    /// <summary>The quarter label ("Q1".."Q4").</summary>
    public string QuarterLabel => $"Q{Quarter}";

    /// <summary>Σ assessable amount paid/credited across the certificate's deduction rows.</summary>
    public Money TotalAmountPaid => new(Deductions.Sum(d => d.AmountPaid.Amount));

    /// <summary>Σ TDS withheld across the certificate's deduction rows.</summary>
    public Money TotalTdsDeducted => new(Deductions.Sum(d => d.TdsAmount.Amount));

    /// <summary>Σ TDS deposited (for this deductee) across the certificate's challan rows.</summary>
    public Money TotalTdsDeposited => new(Challans.Sum(c => c.TdsDeposited.Amount));

    /// <summary>True iff there is nothing to certify (no withholding on the deductee this quarter).</summary>
    public bool IsEmpty => Deductions.Count == 0;

    /// <summary>Builds the Form 16A certificate for <paramref name="deducteeLedgerId"/> for quarter
    /// <paramref name="quarter"/> (1..4) of the financial year starting <paramref name="fyStartYear"/>-04-01.</summary>
    public static Form16A Build(Company company, int fyStartYear, int quarter, Guid deducteeLedgerId)
    {
        ArgumentNullException.ThrowIfNull(company);
        var return26q = Form26Q.Build(company, fyStartYear, quarter);
        return FromReturn(company, return26q, deducteeLedgerId);
    }

    /// <summary>Builds one Form 16A per deductee that has a withholding in the quarter, in first-appearance order.</summary>
    public static IReadOnlyList<Form16A> BuildAll(Company company, int fyStartYear, int quarter)
    {
        ArgumentNullException.ThrowIfNull(company);
        var return26q = Form26Q.Build(company, fyStartYear, quarter);

        var seen = new HashSet<Guid>();
        var order = new List<Guid>();
        foreach (var row in return26q.Deductees)
            if (seen.Add(row.DeducteeLedgerId))
                order.Add(row.DeducteeLedgerId);

        return order.Select(id => FromReturn(company, return26q, id)).ToList();
    }

    private static Form16A FromReturn(Company company, Form26Q return26q, Guid deducteeLedgerId)
    {
        var deductor = BuildDeductor(company, return26q.Deductor);

        var deductions = return26q.Deductees
            .Where(d => d.DeducteeLedgerId == deducteeLedgerId)
            .Select(d => new Form16ADeductionRow(
                d.SectionCode, d.FvuSectionCode, d.DeductionDate, d.AmountPaid, d.TdsAmount, d.RateBasisPoints, d.PanApplied))
            .ToList();

        var challans = new List<Form16AChallanRow>();
        foreach (var ch in return26q.Challans)
        {
            var forDeductee = ch.DeducteeRows.Where(r => r.DeducteeLedgerId == deducteeLedgerId).ToList();
            if (forDeductee.Count == 0) continue;
            challans.Add(new Form16AChallanRow(
                ch.ChallanNo, ch.BsrCode, ch.DepositDate, new Money(forDeductee.Sum(r => r.TdsAmount.Amount))));
        }

        // Identity: prefer the return row (denormalised name/PAN); fall back to the ledger master when the deductee
        // has no withholding this quarter (an empty certificate still names the party).
        var firstRow = return26q.Deductees.FirstOrDefault(d => d.DeducteeLedgerId == deducteeLedgerId);
        string name; string? pan;
        if (firstRow is not null) { name = firstRow.DeducteeName; pan = firstRow.DeducteePan; }
        else
        {
            var ledger = company.FindLedger(deducteeLedgerId);
            name = ledger?.Name ?? "(unknown)";
            pan = ledger?.PartyPan;
        }
        var deductee = new Form16ADeducteeBlock(deducteeLedgerId, name, pan);

        return new Form16A(
            return26q.FinancialYearStartYear, return26q.Quarter, return26q.From, return26q.To,
            deductor, deductee, deductions, challans);
    }

    private static Form16ADeductorBlock BuildDeductor(Company company, Form26QDeductor d) => new(
        company.Name,
        d.Tan,
        DeductorPan(company),
        d.DeductorType,
        d.ResponsiblePersonName,
        d.ResponsiblePersonPan,
        d.ResponsiblePersonDesignation,
        d.ResponsiblePersonAddress);

    /// <summary>The deductor's income-tax PAN, derived from the company GSTIN (chars 3–12 of a 15-char GSTIN) when a
    /// registration is present; <c>null</c> otherwise (the clone does not model a standalone company PAN field).</summary>
    internal static string? DeductorPan(Company company)
    {
        var gstin = company.Gst?.Gstin;
        if (gstin is { Length: 15 }) return gstin.Substring(2, 10);
        return null;
    }
}
