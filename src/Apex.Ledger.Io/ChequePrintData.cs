using Apex.Ledger.Domain;

namespace Apex.Ledger.Io;

/// <summary>
/// Everything that gets inked onto one pre-printed cheque leaf, already resolved from the books
/// (catalog §8 Banking; census row 8.4). Framework-agnostic and DB-agnostic: the Desktop layer projects a
/// Payment voucher's bank line into this, and <see cref="ChequePdf"/> turns it into bytes.
///
/// <para><b>Vendor grounding.</b> <c>help.tallysolutions.com/print-cheques/</c>, "Print Cheque from Payment
/// Voucher": the cheque number is the voucher's <i>Inst. no.</i> and the cheque date its <i>Inst. date</i>, and
/// the cheque is printed straight off the voucher. The favouring name, the amount in words and the amount in
/// figures are the elements the Cheque Dimensions screen places (see <see cref="ChequeLayout"/>).</para>
///
/// <para><b>The per-print nudge (<see cref="NudgeTopTmm"/> / <see cref="NudgeLeftTmm"/>) lives HERE, not on the
/// layout.</b> <c>help.tallysolutions.com/docs/te9rel53/Banking/Cheque_Printing.htm</c> gives "Adjust Distance
/// From Top Edge (in mm)" and "Adjust Distance From Left Edge (in mm)" and states that the adjustment "does not
/// affect the settings of cheque dimensions pre-configured for the selected cheque format". So it is a
/// render-time addend applied to every element and is never written back into the stored dimensions.</para>
/// </summary>
public sealed class ChequePrintData
{
    /// <summary>The favouring / payee name — who the cheque is drawn in favour of.</summary>
    public string PayeeName { get; init; } = string.Empty;

    /// <summary>The cheque amount. Rendered both in words and in figures.</summary>
    public Money Amount { get; init; } = Money.Zero;

    /// <summary>The instrument date ("Inst. date"), printed one glyph per NPCI box. <c>null</c> ⇒ no date.</summary>
    public DateOnly? ChequeDate { get; init; }

    /// <summary>The instrument number ("Inst. no."). Never inked on the leaf — the leaf is pre-numbered — but
    /// carried so the preview heading and the print log can name the cheque.</summary>
    public string InstrumentNumber { get; init; } = string.Empty;

    /// <summary>The bank ledger's name, for the preview heading. Not inked.</summary>
    public string BankName { get; init; } = string.Empty;

    /// <summary>The drawer company's name. Inked only when <see cref="PrintCompanyName"/> is on.</summary>
    public string CompanyName { get; init; } = string.Empty;

    /// <summary>
    /// <c>help.tallysolutions.com/cheque-payments-set-up/</c>, "Disable Company Name in the Pre-printed Cheques" —
    /// a leaf that is already printed with the drawer's name must not have it printed again. Default off.
    /// </summary>
    public bool PrintCompanyName { get; init; }

    /// <summary>The base currency's formal name ("Rupees") used when the layout's "Print Currency Formal Name"
    /// is on. Falls back to "Rupees" when blank.</summary>
    public string CurrencyFormalName { get; init; } = "Rupees";

    /// <summary>The base currency's fractional-unit name ("Paise").</summary>
    public string CurrencyMinorName { get; init; } = "Paise";

    /// <summary>The base currency's symbol, prefixed to the figures when the layout's "Print Currency Symbol"
    /// is on. Only WinAnsi-representable text reaches the page, so a caller passing "₹" is folded to "Rs.".</summary>
    public string CurrencySymbol { get; init; } = "Rs.";

    /// <summary>Per-print vertical nudge in tenths of a millimetre, added to every element's top offset.</summary>
    public int NudgeTopTmm { get; init; }

    /// <summary>Per-print horizontal nudge in tenths of a millimetre, added to every element's left offset.</summary>
    public int NudgeLeftTmm { get; init; }
}
