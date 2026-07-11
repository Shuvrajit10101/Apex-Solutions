namespace Apex.Ledger.Domain;

/// <summary>
/// The legal status of a deductor / deductee / collectee for income-tax withholding (Phase 7 slice 1; TDS/TCS
/// masters). Drives the deductor block on Form 26Q/27EQ and — at compute time (Phase 7 slice 2/5) — the
/// section-conditional rate branches (e.g. §194C 1% for Individual/HUF vs 2% for others). The same set of legal
/// persons applies to all three roles; three enums keep the roles distinct at the type level without conflating
/// a company's own deductor status with a party's deductee/collectee status.
/// </summary>
public enum DeductorType
{
    /// <summary>Company (domestic).</summary>
    Company,
    /// <summary>Individual.</summary>
    Individual,
    /// <summary>Hindu Undivided Family.</summary>
    HinduUndividedFamily,
    /// <summary>Partnership firm (incl. LLP).</summary>
    Firm,
    /// <summary>Association of Persons.</summary>
    AssociationOfPersons,
    /// <summary>Body of Individuals.</summary>
    BodyOfIndividuals,
    /// <summary>Local authority.</summary>
    LocalAuthority,
    /// <summary>Government (central / state).</summary>
    Government,
    /// <summary>Artificial juridical person.</summary>
    ArtificialJuridicalPerson,
}

/// <summary>
/// The legal status of a <b>deductee</b> (the party whose payment is subject to TDS). Persisted on a party ledger
/// (Phase 7 slice 1). At compute time it selects the section-conditional rate (e.g. §194C Individual/HUF 1% vs 2%).
/// </summary>
public enum DeducteeType
{
    Company,
    Individual,
    HinduUndividedFamily,
    Firm,
    AssociationOfPersons,
    BodyOfIndividuals,
    LocalAuthority,
    Government,
    ArtificialJuridicalPerson,
}

/// <summary>
/// The legal status of a <b>collectee</b> (the buyer from whom TCS is collected). Persisted on a party ledger
/// (Phase 7 slice 1). Mirrors <see cref="DeducteeType"/> on the TCS side.
/// </summary>
public enum CollecteeType
{
    Company,
    Individual,
    HinduUndividedFamily,
    Firm,
    AssociationOfPersons,
    BodyOfIndividuals,
    LocalAuthority,
    Government,
    ArtificialJuridicalPerson,
}

/// <summary>
/// Return-filing periodicity for TDS/TCS statements (Phase 7 slice 1). Income-tax withholding returns
/// (Form 26Q / 27EQ) are <b>quarterly</b> — the single working value; the enum leaves room for future variants
/// without a schema change.
/// </summary>
public enum TdsTcsPeriodicity
{
    /// <summary>Quarterly filing (Form 26Q / 27EQ) — the only working periodicity.</summary>
    Quarterly,
}

/// <summary>
/// Marks a Duties &amp; Taxes ledger as the auto-created <b>TDS Payable</b> or <b>TCS Payable</b> liability ledger
/// (Phase 7 slice 1), mirroring <see cref="LedgerGstClassification"/>. Reports map the ledger to its withholding
/// head without parsing the ledger name.
/// </summary>
public enum TdsTcsLedgerKind
{
    /// <summary>The TDS Payable liability ledger (income-tax deducted at source).</summary>
    Tds,
    /// <summary>The TCS Payable liability ledger (income-tax collected at source).</summary>
    Tcs,
}
