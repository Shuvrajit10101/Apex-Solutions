namespace Apex.Ledger.Domain;

/// <summary>
/// The <b>operator-settable</b> status of one cheque leaf (catalog §8 Banking; census row 8.5 Cheque Register).
///
/// <para><b>Vendor grounding.</b> <c>help.tallysolutions.com/cheque-register/</c>, "View Cheques with Specific
/// Statuses": <i>Available</i> is a leaf <i>"still unused and can be issued"</i>, <i>Blank</i> is the
/// <i>"physical inventory of the cheques"</i>, and <i>Cancelled</i> is a leaf <i>"voided due to errors"</i>.</para>
///
/// <para><b>🔴 ONLY THESE THREE ARE STORED, AND THAT IS THE WHOLE DESIGN.</b> The register's other three buckets —
/// <i>Reconciled</i>, <i>Unreconciled</i> and <i>Out of Period</i> — are <b>computed</b> from the posted bank
/// allocations and the report period. Storing them would make the register lie the moment a reconciliation date is
/// keyed on the voucher, because the stored copy would not move.</para>
/// </summary>
public enum ChequeStatus
{
    /// <summary>Unused and issuable — the state of every leaf in a book nobody has touched.</summary>
    Available = 0,

    /// <summary>Counted in the physical inventory but not issuable — the operator has set it aside.</summary>
    Blank = 1,

    /// <summary>Voided. The leaf exists, is spoiled, and must never be reported as issuable.</summary>
    Cancelled = 2,
}

/// <summary>
/// One <b>cheque book</b> held against a bank ledger: a named, contiguous range of pre-printed leaf numbers
/// (catalog §8 Banking; census row 8.5).
///
/// <para><b>Vendor grounding.</b> <c>help.tallysolutions.com/cheque-payments-set-up/</c>, "Specify Cheque Range
/// and Format in Bank Ledger" — the bank ledger carries a cheque book's <i>Name of Cheque Book</i>, <i>From
/// Number</i>, <i>To Number</i> and a <i>Number of Cheques</i> that the vendor auto-calculates. That last one is
/// <see cref="Count"/> here and is deliberately <b>not stored</b>: a stored count obliged to agree with a stored
/// range is a drift bug waiting to be written.</para>
///
/// <para><b>🔴 WHY THIS TYPE HAS TO EXIST AT ALL, AND WHY NO PROJECTION COULD REPLACE IT.</b> The Cheque
/// Register's Available / Blank / Cancelled / Out-of-Period buckets are facts about <b>paper you are holding</b>,
/// not about entries you posted. Nothing in a set of books says which leaf numbers the bank gave you or which of
/// them you spoiled, so there is no derivation from the ledger — which is exactly why row 8.5 needed storage and
/// the rest of the banking wave did not.</para>
///
/// <para><b>🔴 THE NUMBERS ARE STRINGS, NOT INTEGERS.</b> Cheque numbers carry leading zeros; a leaf printed
/// <c>000123</c> is not the string <c>123</c> on the paper an operator is holding, and matching a posted
/// instrument number against a range has to compare what was actually keyed. <see cref="Contains"/> therefore
/// compares on the NUMERIC value where both ends and the candidate are all-digits (so <c>000123</c> is inside
/// <c>000001</c>–<c>000500</c>), and falls back to an ordinal comparison when they are not.</para>
/// </summary>
public sealed class ChequeBook
{
    /// <summary>Creates a cheque book. <paramref name="fromNumber"/> and <paramref name="toNumber"/> are stored
    /// exactly as keyed, trimmed only — the leading zeros are part of the leaf number.</summary>
    public ChequeBook(Guid id, Guid ledgerId, string name, string fromNumber, string toNumber)
    {
        if (id == Guid.Empty) throw new ArgumentException("A cheque book needs an id.", nameof(id));
        if (ledgerId == Guid.Empty) throw new ArgumentException("A cheque book belongs to a bank ledger.", nameof(ledgerId));
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("A cheque book needs a name.", nameof(name));
        if (string.IsNullOrWhiteSpace(fromNumber)) throw new ArgumentException("A cheque book needs a From number.", nameof(fromNumber));
        if (string.IsNullOrWhiteSpace(toNumber)) throw new ArgumentException("A cheque book needs a To number.", nameof(toNumber));

        Id = id;
        LedgerId = ledgerId;
        Name = name.Trim();
        FromNumber = fromNumber.Trim();
        ToNumber = toNumber.Trim();
    }

    /// <summary>Stable identity.</summary>
    public Guid Id { get; }

    /// <summary>The bank ledger this book is held against.</summary>
    public Guid LedgerId { get; }

    /// <summary>"Name of Cheque Book" — free text, e.g. "SBI current — book 3".</summary>
    public string Name { get; set; }

    /// <summary>"From Number", exactly as printed on the first leaf (leading zeros preserved).</summary>
    public string FromNumber { get; set; }

    /// <summary>"To Number", exactly as printed on the last leaf.</summary>
    public string ToNumber { get; set; }

    /// <summary>
    /// The vendor's auto-calculated "Number of Cheques" — DERIVED, never stored. <c>0</c> when either end is not
    /// numeric, which is the honest answer: a range whose ends cannot be counted has no count, and reporting a
    /// guess would put a wrong leaf total in front of an operator doing a physical stock-take of their cheques.
    /// </summary>
    public int Count =>
        TryNumber(FromNumber, out var from) && TryNumber(ToNumber, out var to) && to >= from
            ? (int)Math.Min(to - from + 1, int.MaxValue)
            : 0;

    /// <summary>Every leaf number in the range, in order, formatted with the same width as
    /// <see cref="FromNumber"/> so <c>000123</c> stays <c>000123</c>. Empty when the range is not countable.</summary>
    public IEnumerable<string> LeafNumbers()
    {
        if (!TryNumber(FromNumber, out var from) || !TryNumber(ToNumber, out var to) || to < from) yield break;
        var width = FromNumber.Length;
        for (var n = from; n <= to; n++)
            yield return n.ToString(System.Globalization.CultureInfo.InvariantCulture).PadLeft(width, '0');
    }

    /// <summary>
    /// True when <paramref name="chequeNumber"/> falls inside this book's range. Numeric when all three are
    /// all-digits (so leading zeros do not matter), ordinal otherwise — never a silent int.Parse that would throw
    /// on a bank that letters its series.
    /// </summary>
    public bool Contains(string? chequeNumber)
    {
        var candidate = (chequeNumber ?? string.Empty).Trim();
        if (candidate.Length == 0) return false;

        if (TryNumber(candidate, out var n) && TryNumber(FromNumber, out var from) && TryNumber(ToNumber, out var to))
            return n >= from && n <= to;

        return string.CompareOrdinal(candidate, FromNumber) >= 0
            && string.CompareOrdinal(candidate, ToNumber) <= 0;
    }

    /// <summary>All-digit parse, invariant. Non-digits (a lettered series, a blank) are not an error — they are
    /// simply not numerically comparable, and the caller falls back to an ordinal comparison.</summary>
    private static bool TryNumber(string text, out long value)
    {
        value = 0;
        if (text.Length == 0 || text.Length > 18) return false;
        foreach (var ch in text) if (ch is < '0' or > '9') return false;
        return long.TryParse(text, System.Globalization.NumberStyles.None,
                             System.Globalization.CultureInfo.InvariantCulture, out value);
    }
}

/// <summary>
/// One operator-set status against one leaf of one cheque book (census row 8.5), plus the "already printed" flag
/// the Cheque Printing report's <b>F8 Include Printed</b> filters on
/// (<c>help.tallysolutions.com/print-cheques/</c>).
///
/// <para>A leaf with no override is <see cref="ChequeStatus.Available"/> — the absence of a row IS the default, so
/// a fresh cheque book stores nothing at all.</para>
/// </summary>
public sealed class ChequeStatusOverride
{
    /// <summary>Creates an override for one leaf.</summary>
    public ChequeStatusOverride(Guid id, Guid chequeBookId, string chequeNumber, ChequeStatus status, bool printed = false)
    {
        if (id == Guid.Empty) throw new ArgumentException("A cheque status needs an id.", nameof(id));
        if (chequeBookId == Guid.Empty) throw new ArgumentException("A cheque status belongs to a cheque book.", nameof(chequeBookId));
        if (string.IsNullOrWhiteSpace(chequeNumber)) throw new ArgumentException("A cheque status needs a leaf number.", nameof(chequeNumber));

        Id = id;
        ChequeBookId = chequeBookId;
        ChequeNumber = chequeNumber.Trim();
        Status = status;
        Printed = printed;
    }

    /// <summary>Stable identity.</summary>
    public Guid Id { get; }

    /// <summary>The book this leaf belongs to.</summary>
    public Guid ChequeBookId { get; }

    /// <summary>The leaf number, exactly as printed.</summary>
    public string ChequeNumber { get; }

    /// <summary>The operator-set status. Never Reconciled / Unreconciled / Out-of-Period — those are computed.</summary>
    public ChequeStatus Status { get; set; }

    /// <summary>Whether this leaf has already been inked. Drives F8 "Include Printed".</summary>
    public bool Printed { get; set; }
}
