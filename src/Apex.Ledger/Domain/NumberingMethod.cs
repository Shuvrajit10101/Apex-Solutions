namespace Apex.Ledger.Domain;

/// <summary>
/// How a <see cref="VoucherType"/> assigns voucher numbers (catalog §4; census 5.10).
///
/// <para>🔴 <b>THE ORDINALS ARE PERSISTED</b> in <c>voucher_types.numbering</c> as a plain INTEGER, so the three
/// original members keep ordinals 0/1/2 forever and every later method is APPENDED. Renumbering would silently
/// re-interpret every stored row — a book saved as <c>Manual</c> would come back as something else.</para>
///
/// <para><b>R7 — ATTESTED</b> (help.tallysolutions.com, fetched 2026-09-05). The Voucher Type master page offers
/// the <i>Method of Voucher Numbering</i> as <i>"Automatic, Automatic (Manual Override), Manual, or Multi-user
/// Auto"</i>; the voucher-numbering-methods page adds the fifth — <i>"you can also disable the voucher numbering
/// by selecting the None option"</i>. All five are therefore vendor-attested; none is ours.</para>
/// </summary>
public enum NumberingMethod
{
    /// <summary>
    /// Engine assigns the next sequential number per type. <i>"When you add or delete new vouchers to the company
    /// data, the vouchers numbers are updated automatically based on the existing voucher numbers."</i>
    /// </summary>
    Automatic,

    /// <summary>
    /// Caller supplies the number; uniqueness is checked. <i>"Manually enter the voucher number in each
    /// voucher."</i> — the engine never numbers a Manual voucher, so an entry screen that offers this method must
    /// give the operator a way to type one.
    /// </summary>
    Manual,

    /// <summary>No number is assigned (<c>Number = 0</c>) — <i>"disable the voucher numbering"</i>.</summary>
    None,

    /// <summary>
    /// <b>Automatic (Manual Override)</b> — <i>"automate the number for vouchers, and manually override the
    /// automated number, if required"</i>. Appended at ordinal 3.
    ///
    /// <para>🔴 <b>DIVERGENCE, LABELLED AS OURS (ruling 9).</b> This engine's <see cref="Automatic"/> only
    /// numbers a voucher whose <c>Number</c> the caller left at 0 — it has never overwritten a supplied number —
    /// so at the ENGINE level <see cref="Automatic"/> and this member are indistinguishable today. What separates
    /// them in this build is the UI contract: the entry screen offers an editable Voucher No. box under this
    /// method (and under <see cref="Manual"/>) and a read-only one under <see cref="Automatic"/>. The vendor
    /// pages do not state what a plain-Automatic type does with an operator-supplied number, so tightening
    /// <see cref="Automatic"/> to overwrite would be an unsourced behaviour change to every book already posted.
    /// </para>
    /// </summary>
    AutomaticManualOverride,

    /// <summary>
    /// <b>Multi-user Auto</b> — <i>"enable the allotment of subsequent voucher numbers in a multi-user
    /// environment … an extension of the Automatic Numbering method"</i>. Appended at ordinal 4.
    ///
    /// <para>🔴 <b>DIVERGENCE, LABELLED AS OURS (ruling 9).</b> Apex Solutions is a single-user, single-process
    /// desktop application with no shared data server, so there is no second writer to contend with: this method
    /// numbers exactly as <see cref="Automatic"/> does. It is carried as a distinct member because it is a
    /// persisted operator CHOICE — a book configured this way must round-trip as this method rather than be
    /// silently rewritten to <see cref="Automatic"/> — and because the contention behaviour becomes real the day
    /// a shared store lands. The vendor's <i>"Renumber Vouchers / Retain Original Voucher No."</i> insertion
    /// option is NOT built and is not claimed.</para>
    /// </summary>
    MultiUserAuto,
}
