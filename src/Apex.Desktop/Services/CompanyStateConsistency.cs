using Apex.Ledger.Domain;

namespace Apex.Desktop.Services;

/// <summary>
/// The ONE computation behind the postal-State / GST-registration-State advisory, and the one place its
/// wording is written.
///
/// <para><b>The rule it serves.</b> The postal <see cref="Company.State"/> is the source of truth; the GST
/// home State (<see cref="GstConfig.HomeStateCode"/>) takes its initial value from it and then stays
/// editable, because a registration in a State other than the postal one is legal. So the two CAN differ
/// legitimately — and when they do, the operator is told, because the GST one is what decides intra- versus
/// inter-state supply (CGST+SGST versus IGST) on every invoice, and it is what the printed supplier block
/// carries. <b>A warning, never a refusal:</b> divergence is legal; silence about it is not. This method
/// returns a string and mutates nothing.</para>
///
/// <para><b>Why it is shared rather than computed on each screen.</b> Either screen can create the
/// divergence — the company profile screen by moving the postal State, the statutory screen by moving the
/// GST one — so both must say the same thing. Two private copies of a three-term predicate is exactly the
/// drift this codebase has been paying down; the wording lives here once and both screens read it.</para>
///
/// <para><b>Three deliberate silences, each with a reason.</b> GST off — there is no second State to
/// disagree with. Postal State blank — nothing was claimed, and clearing an advisory input must never be
/// reported as a conflict. Postal State present but not a recognised State/UT — that gets its OWN message
/// rather than the divergence one, because comparing an unresolvable name against a code and announcing
/// "they differ" would report a divergence that may not exist. That last case is reachable today: canonical
/// import assigns <c>Company.State</c> verbatim with no list check, so a book on disk can hold an
/// abbreviation or a trailing-space value.</para>
/// </summary>
public static class CompanyStateConsistency
{
    /// <summary>
    /// The GST registration State code a company must be checked against, or <c>null</c> when there is none.
    ///
    /// <para><b>The "GST is off" silence lives here, once.</b> A <see cref="GstConfig"/> whose
    /// <see cref="GstConfig.Enabled"/> is false is not a registration — there is no second State to disagree
    /// with, and warning on every book that does not use GST would be noise on most of them. The company
    /// profile screen composes this with <see cref="Advisory(string?, string?)"/>; extracting it as a named
    /// method is what stops the rule being re-derived per caller.</para>
    ///
    /// <para>This replaced an <c>Advisory(Company?)</c> convenience overload that no screen ever called — six
    /// of the seven advisory assertions were exercising a door production does not use.</para>
    /// </summary>
    public static string? RegisteredStateCodeOf(Company? company) =>
        company?.Gst is { Enabled: true } gst ? gst.HomeStateCode : null;

    /// <summary>
    /// The advisory for a CANDIDATE pair — the postal State text and the GST home-State code as they stand in
    /// the controls, which is not necessarily what is stored yet.
    ///
    /// <para><b>Why the candidate overload exists.</b> Either screen can create the divergence while the
    /// operator is still typing, and the warning has to track the picker rather than lag a save behind it. Both
    /// screens pass their own live values here instead of temporarily writing them onto the shared aggregate to
    /// ask a question — mutating a company to compute an advisory is how a failed edit leaves the in-memory
    /// book holding a value the database does not have.</para>
    /// </summary>
    public static string Advisory(string? postalState, string? gstHomeStateCode)
    {
        if (string.IsNullOrWhiteSpace(gstHomeStateCode)) return string.Empty;

        var postalText = (postalState ?? string.Empty).Trim();
        if (postalText.Length == 0) return string.Empty;

        var gstState = IndianState.FromCode(gstHomeStateCode);
        var gstLabel = gstState is null ? gstHomeStateCode : $"{gstState.Name} ({gstState.Code})";

        var postal = IndianState.FromName(postalState);
        if (postal is null)
        {
            // 🔴 REPORT THE VALUE AS STORED, NOT A TIDIED COPY OF IT. The lookup is deliberately UNTRIMMED —
            // "West Bengal " with a trailing space really is not in the list, and the whole point of tolerating
            // it is that the stored bytes are what matter. Quoting the TRIMMED text back while refusing the
            // untrimmed one produced "Postal State 'West Bengal' is not a recognised State/UT" on a book whose
            // State reads West Bengal: a message that contradicts itself. And since the difference is
            // whitespace — invisible in any quotation — the message has to say so in words.
            var onlyWhitespaceApart = postalText.Length != (postalState ?? string.Empty).Length
                                      && IndianState.FromName(postalText) is not null;
            return $"Postal State '{postalState}' is not a recognised State/UT"
                 + (onlyWhitespaceApart ? " — it has leading or trailing spaces" : string.Empty)
                 + $", so it cannot be checked against the GST registration State '{gstLabel}'.";
        }

        if (postal.Code == gstHomeStateCode) return string.Empty;

        return $"Postal State '{postal.Name}' differs from the GST registration State '{gstLabel}'. "
             + "Printed invoices and tax calculation use the GST State.";
    }
}
