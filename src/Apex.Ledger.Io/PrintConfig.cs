namespace Apex.Ledger.Io;

/// <summary>
/// The document-copy marking printed on an invoice (<b>CGST Rule 48(1)</b>; catalog §17): a printed label naming
/// which of the statutory copies a physical print is.
///
/// <para><b>🔴 THE RULE, VERBATIM.</b> Rule 48 "Manner of issuing invoice", sub-rule (1): <i>"The invoice shall be
/// prepared in triplicate, in the case of supply of goods, in the following manner, namely,- (a) the original copy
/// being marked as ORIGINAL FOR RECIPIENT; (b) the duplicate copy being marked as DUPLICATE FOR TRANSPORTER; and
/// (c) the triplicate copy being marked as TRIPLICATE FOR SUPPLIER."</i> — CGST Rules 2017 as published by CBIC,
/// <c>https://cbic-gst.gov.in/pdf/cgst-rules-30122017.pdf</c>, PDF p.40 (printed p.37). RQ-12
/// (<c>docs/phase5-reports-io-requirements.md:306</c>) states the same order.</para>
///
/// <para><b>The duplicate and triplicate captions shipped TRANSPOSED</b> (T0-11 review C10/L1-10) — the model, the
/// F12 radio captions and two green tests all paired Duplicate with the supplier and Triplicate with the
/// transporter, so the copy handed to a transporter was marked, on its face, as the one Rule 48(1) does not give
/// him. Corrected here against the rule text above, not against the app's own comment, which also miscited the
/// requirement to "Rule 46(1) proviso" (Rule 46 prescribes invoice CONTENTS; the copies are Rule 48).</para>
///
/// <para><b>Rule 48(2) — the services set, deliberately NOT modelled.</b> For a supply of services the invoice is
/// prepared in duplicate: "(a) … ORIGINAL FOR RECIPIENT; and (b) the duplicate copy being marked as DUPLICATE FOR
/// SUPPLIER". So "DUPLICATE FOR SUPPLIER" is a real marking — of a TWO-copy set with no triplicate in it. This
/// enum is one three-valued set offering a triplicate beside the duplicate, i.e. the goods set of Rule 48(1), and
/// the labels below are that set's. A goods/services split of the marking is not offered today.</para>
/// </summary>
public enum CopyMarking
{
    /// <summary>No copy label is printed.</summary>
    None,

    /// <summary>ORIGINAL FOR RECIPIENT — Rule 48(1)(a), the recipient's copy.</summary>
    Original,

    /// <summary>DUPLICATE FOR TRANSPORTER — Rule 48(1)(b), the copy that travels with the goods.</summary>
    Duplicate,

    /// <summary>TRIPLICATE FOR SUPPLIER — Rule 48(1)(c), the issuer's own retained copy.</summary>
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

    /// <summary>The human-readable copy-marking label, or an empty string for <see cref="CopyMarking.None"/>.
    /// The three literals are CGST Rule 48(1)(a)/(b)/(c) as the rule spells them — see <see cref="CopyMarking"/>
    /// for the verbatim text and its CBIC source.</summary>
    public string CopyMarkingLabel => CopyMarking switch
    {
        CopyMarking.Original => "ORIGINAL FOR RECIPIENT",
        CopyMarking.Duplicate => "DUPLICATE FOR TRANSPORTER",
        CopyMarking.Triplicate => "TRIPLICATE FOR SUPPLIER",
        _ => string.Empty,
    };
}
