namespace Apex.Ledger.Domain;

/// <summary>
/// One <b>date-effective affix row</b> on a <see cref="VoucherType"/>'s numbering — used identically for a Prefix
/// (<see cref="VoucherType.Prefixes"/>) or a Suffix (<see cref="VoucherType.Suffixes"/>) (numbering-design-v2 §1.2;
/// catalog §4). A voucher's rendered number selects the row whose <see cref="ApplicableFrom"/> is the latest that is
/// on/before the voucher date (tie-break: greatest <see cref="Id"/>), so a future-dated row cannot change a past
/// voucher's display — this is Tally's "roll the FY prefix" workflow expressed as data.
/// </summary>
/// <param name="Id">Stable surrogate key; also the deterministic secondary sort key that resolves a same-date tie
/// (<see cref="Services.VoucherNumberFormatter.Render"/>).</param>
/// <param name="ApplicableFrom">The first date on which this affix is in force.</param>
/// <param name="Particulars">The <b>entire</b> affix text, separators included (e.g. <c>"25-26/"</c>, <c>"INV-"</c>,
/// <c>"/A"</c>) — there is no implicit separator. May be empty.</param>
public sealed record VoucherNumberAffix(Guid Id, DateOnly ApplicableFrom, string Particulars);
