using Apex.Ledger.Domain;

namespace Apex.Ledger.Reports;

/// <summary>
/// The <b>collector</b> block of a <see cref="Form27D"/> TCS certificate — the collector's legal name, TAN, income-tax
/// PAN (derived from the company GSTIN when available), legal type and the person-responsible-for-collection identity
/// captured on F11 (<see cref="TcsConfig"/>). The exact mirror of <see cref="Form16ADeductorBlock"/> for the
/// collector's side.
/// </summary>
public sealed record Form27DCollectorBlock(
    string Name,
    string Tan,
    string? Pan,
    DeductorType CollectorType,
    string? ResponsiblePersonName,
    string? ResponsiblePersonPan,
    string? ResponsiblePersonDesignation,
    string? ResponsiblePersonAddress);

/// <summary>The <b>collectee</b> block of a Form 27D — the party the certificate is issued to (PAN + name).</summary>
public sealed record Form27DCollecteeBlock(Guid LedgerId, string Name, string? Pan);

/// <summary>
/// One <b>TCS summary</b> line of a Form 27D — a single collection on the collectee within the certificate quarter:
/// the §206C <see cref="CollectionCode"/> + its Form-27EQ/FVU <see cref="FvuCollectionCode"/>, the <see cref="Date"/>
/// (voucher date), the <see cref="AmountReceived"/> (the GST-<b>inclusive</b> assessable base — Circular 17/2020), the
/// <see cref="TcsAmount"/> collected and the applied <see cref="RateBasisPoints"/>. Read verbatim off the
/// corresponding <see cref="Form27EQCollecteeRow"/>. Mirrors <see cref="Form16ADeductionRow"/>, but TCS is
/// <b>additive</b>.
/// </summary>
public sealed record Form27DCollectionRow(
    string CollectionCode,
    string FvuCollectionCode,
    DateOnly Date,
    Money AmountReceived,
    Money TcsAmount,
    int RateBasisPoints,
    bool PanApplied)
{
    /// <summary>The applied rate as a percentage (e.g. 1.00 for 100 bp).</summary>
    public decimal RatePercent => RateBasisPoints / 100m;
}

/// <summary>One <b>challan / deposit</b> line of a Form 27D — the ITNS-281 identification and the portion of TCS this
/// challan deposited <b>for this collectee</b>. Mirrors <see cref="Form16AChallanRow"/>.</summary>
public sealed record Form27DChallanRow(
    string ChallanNo,
    string BsrCode,
    DateOnly DepositDate,
    Money TcsDeposited);

/// <summary>
/// <b>Form 27D</b> — the quarterly <b>TCS certificate</b> a collector issues to a collectee (Phase 7 slice 7; catalog
/// §13), as a pure, read-only projection over <see cref="Form27EQ"/> filtered to one collectee. The exact mirror of
/// <see cref="Form16A"/> for the collector's side: it carries the collector block, the collectee block, the quarter's
/// TCS summary rows and the challan/deposit rows — all read verbatim from the return so the certificate figures match
/// Form 27EQ <b>to the paisa, by construction</b>. Deterministic; ordered for byte-stability. No UI, no DB.
/// </summary>
public sealed record Form27D(
    int FinancialYearStartYear,
    int Quarter,
    DateOnly From,
    DateOnly To,
    Form27DCollectorBlock Collector,
    Form27DCollecteeBlock Collectee,
    IReadOnlyList<Form27DCollectionRow> Collections,
    IReadOnlyList<Form27DChallanRow> Challans)
{
    /// <summary>The financial-year label (e.g. "2025-26").</summary>
    public string FinancialYearLabel => $"{FinancialYearStartYear}-{(FinancialYearStartYear + 1) % 100:00}";

    /// <summary>The quarter label ("Q1".."Q4").</summary>
    public string QuarterLabel => $"Q{Quarter}";

    /// <summary>Σ assessable amount received/credited across the certificate's collection rows.</summary>
    public Money TotalAmountReceived => new(Collections.Sum(d => d.AmountReceived.Amount));

    /// <summary>Σ TCS collected across the certificate's collection rows.</summary>
    public Money TotalTcsCollected => new(Collections.Sum(d => d.TcsAmount.Amount));

    /// <summary>Σ TCS deposited (for this collectee) across the certificate's challan rows.</summary>
    public Money TotalTcsDeposited => new(Challans.Sum(c => c.TcsDeposited.Amount));

    /// <summary>True iff there is nothing to certify (no collection on the collectee this quarter).</summary>
    public bool IsEmpty => Collections.Count == 0;

    /// <summary>Builds the Form 27D certificate for <paramref name="collecteeLedgerId"/> for quarter
    /// <paramref name="quarter"/> (1..4) of the financial year starting <paramref name="fyStartYear"/>-04-01.</summary>
    public static Form27D Build(Company company, int fyStartYear, int quarter, Guid collecteeLedgerId)
    {
        ArgumentNullException.ThrowIfNull(company);
        var return27eq = Form27EQ.Build(company, fyStartYear, quarter);
        return FromReturn(company, return27eq, collecteeLedgerId);
    }

    /// <summary>Builds one Form 27D per collectee that has a collection in the quarter, in first-appearance order.</summary>
    public static IReadOnlyList<Form27D> BuildAll(Company company, int fyStartYear, int quarter)
    {
        ArgumentNullException.ThrowIfNull(company);
        var return27eq = Form27EQ.Build(company, fyStartYear, quarter);

        var seen = new HashSet<Guid>();
        var order = new List<Guid>();
        foreach (var row in return27eq.Collectees)
            if (seen.Add(row.CollecteeLedgerId))
                order.Add(row.CollecteeLedgerId);

        return order.Select(id => FromReturn(company, return27eq, id)).ToList();
    }

    private static Form27D FromReturn(Company company, Form27EQ return27eq, Guid collecteeLedgerId)
    {
        var collector = BuildCollector(company, return27eq.Collector);

        var collections = return27eq.Collectees
            .Where(d => d.CollecteeLedgerId == collecteeLedgerId)
            .Select(d => new Form27DCollectionRow(
                d.CollectionCode, d.FvuCollectionCode, d.CollectionDate, d.AmountReceived, d.TcsAmount, d.RateBasisPoints, d.PanApplied))
            .ToList();

        var challans = new List<Form27DChallanRow>();
        foreach (var ch in return27eq.Challans)
        {
            var forCollectee = ch.CollecteeRows.Where(r => r.CollecteeLedgerId == collecteeLedgerId).ToList();
            if (forCollectee.Count == 0) continue;
            challans.Add(new Form27DChallanRow(
                ch.ChallanNo, ch.BsrCode, ch.DepositDate, new Money(forCollectee.Sum(r => r.TcsAmount.Amount))));
        }

        var firstRow = return27eq.Collectees.FirstOrDefault(d => d.CollecteeLedgerId == collecteeLedgerId);
        string name; string? pan;
        if (firstRow is not null) { name = firstRow.CollecteeName; pan = firstRow.CollecteePan; }
        else
        {
            var ledger = company.FindLedger(collecteeLedgerId);
            name = ledger?.Name ?? "(unknown)";
            pan = ledger?.PartyPan;
        }
        var collectee = new Form27DCollecteeBlock(collecteeLedgerId, name, pan);

        return new Form27D(
            return27eq.FinancialYearStartYear, return27eq.Quarter, return27eq.From, return27eq.To,
            collector, collectee, collections, challans);
    }

    private static Form27DCollectorBlock BuildCollector(Company company, Form27EQCollector c) => new(
        company.Name,
        c.Tan,
        Form16A.DeductorPan(company),
        c.CollectorType,
        c.ResponsiblePersonName,
        c.ResponsiblePersonPan,
        c.ResponsiblePersonDesignation,
        c.ResponsiblePersonAddress);
}
