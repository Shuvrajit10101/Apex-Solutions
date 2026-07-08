namespace Apex.Ledger.Io;

/// <summary>
/// The document-copy marking printed on an invoice (GST Rule 46 / catalog §17): a printed label naming which
/// copy a physical print is. The reference product prints "Original for Recipient / Duplicate for Supplier /
/// Triplicate for Transporter" per Rule 46(1) proviso on the number of copies of a tax invoice.
/// </summary>
public enum CopyMarking
{
    /// <summary>No copy label is printed.</summary>
    None,

    /// <summary>ORIGINAL FOR RECIPIENT (the buyer's copy).</summary>
    Original,

    /// <summary>DUPLICATE FOR SUPPLIER (the seller's / transporter-of-record copy).</summary>
    Duplicate,

    /// <summary>TRIPLICATE FOR TRANSPORTER.</summary>
    Triplicate,
}

/// <summary>
/// The print-time (F12) configuration knobs a voucher / invoice print honours (RQ-12): an optional title
/// override, whether the narration line prints, and the copy-marking label. Pure data — the thin Avalonia
/// layer builds this from the F12 dialog and hands it to the framework-agnostic renderer.
///
/// <para><b>Deferred (DP-9):</b> company-logo image embedding is a later polish slice and is intentionally not
/// modelled here.</para>
/// </summary>
public sealed class PrintConfig
{
    /// <summary>Overrides the printed document title (e.g. "TAX INVOICE" ⇒ "PROFORMA INVOICE"); blank ⇒ use the
    /// template default.</summary>
    public string? TitleOverride { get; init; }

    /// <summary>When true (default) the narration line prints; F12 can suppress it.</summary>
    public bool ShowNarration { get; init; } = true;

    /// <summary>The copy-marking label to print (Original/Duplicate/Triplicate), or None for no label.</summary>
    public CopyMarking CopyMarking { get; init; } = CopyMarking.None;

    /// <summary>The human-readable copy-marking label, or an empty string for <see cref="CopyMarking.None"/>.</summary>
    public string CopyMarkingLabel => CopyMarking switch
    {
        CopyMarking.Original => "ORIGINAL FOR RECIPIENT",
        CopyMarking.Duplicate => "DUPLICATE FOR SUPPLIER",
        CopyMarking.Triplicate => "TRIPLICATE FOR TRANSPORTER",
        _ => string.Empty,
    };
}
