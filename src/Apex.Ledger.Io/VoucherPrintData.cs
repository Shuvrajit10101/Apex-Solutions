using Apex.Ledger;

namespace Apex.Ledger.Io;

/// <summary>
/// A single Dr/Cr posting line as it appears on a printed voucher: the ledger name, its side ("Dr"/"Cr")
/// and the amount (paisa-exact <see cref="Money"/>). Already resolved to display strings by the UI so the
/// framework-agnostic renderer never touches GUID→name lookups.
/// </summary>
public sealed class VoucherPrintLine
{
    /// <summary>The ledger name shown in the Particulars column.</summary>
    public required string LedgerName { get; init; }

    /// <summary>True for a debit line ("Dr"), false for a credit line ("Cr").</summary>
    public required bool IsDebit { get; init; }

    /// <summary>The line amount (paisa-exact).</summary>
    public required Money Amount { get; init; }
}

/// <summary>
/// A framework-agnostic projection of a posted <c>Voucher</c> ready to render to PDF (RQ-10). The thin
/// Avalonia layer resolves the voucher type name, party name and per-line ledger names from the engine and
/// fills this DTO; the renderer only lays it out. Deterministic — no clock: the <see cref="Date"/> is the
/// voucher's own date, already formatted.
/// </summary>
public sealed class VoucherPrintData
{
    /// <summary>Company / mailing name printed as the document header.</summary>
    public string CompanyName { get; init; } = string.Empty;

    /// <summary>The voucher type name (e.g. "Payment", "Sales", "Journal").</summary>
    public string VoucherTypeName { get; init; } = string.Empty;

    /// <summary>The voucher number as displayed (blank when the type has no numbering).</summary>
    public string VoucherNumber { get; init; } = string.Empty;

    /// <summary>The voucher date, already formatted (e.g. "31-03-2025") — the renderer never reads the clock.</summary>
    public string DateText { get; init; } = string.Empty;

    /// <summary>Optional party / particulars name (invoice-type vouchers); blank for a plain voucher.</summary>
    public string PartyName { get; init; } = string.Empty;

    /// <summary>The Dr/Cr posting lines in entry order.</summary>
    public IReadOnlyList<VoucherPrintLine> Lines { get; init; } = Array.Empty<VoucherPrintLine>();

    /// <summary>Free-text narration; printed only when <see cref="PrintConfig.ShowNarration"/> is set.</summary>
    public string Narration { get; init; } = string.Empty;

    /// <summary>Σ of the debit-line amounts (paisa-exact).</summary>
    public Money TotalDebit
    {
        get { var s = Money.Zero; foreach (var l in Lines) if (l.IsDebit) s += l.Amount; return s; }
    }

    /// <summary>Σ of the credit-line amounts (paisa-exact).</summary>
    public Money TotalCredit
    {
        get { var s = Money.Zero; foreach (var l in Lines) if (!l.IsDebit) s += l.Amount; return s; }
    }
}
