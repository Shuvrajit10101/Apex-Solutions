namespace Apex.Ledger.Domain;

/// <summary>
/// The <b>Mailing Details</b> block on a <b>party</b> (Sundry Debtor / Sundry Creditor) ledger — WI-4
/// (CA audit point 1: "input of address … along with PIN code"). Corpus:
/// <c>703679456-TALLY-PRIME-WITH-GST-Notes-PDF.pdf:754</c> ("create a ledger of NARESH TRADERS <b>under Sundry
/// Debtors with Mailing Details &amp; Tax Information</b>"), <c>696054070-TALLY-PRIME-STUDY-GUIDE.pdf:3926</c>
/// (a Sundry <i>Creditor</i>: "Enter Mailing Details and PAN No."), <c>:4142</c>/<c>:4137</c> (the same on a
/// Sundry <i>Debtor</i>), and <c>:6151</c> ("Provide other details like <b>Address, State, Pincode</b>").
///
/// <para><b>🔴 THERE IS DELIBERATELY NO "State" ON THIS BLOCK — READ THIS BEFORE ADDING ONE.</b>
/// A party's State/UT already exists exactly once, on <see cref="PartyGstDetails.StateCode"/>, and it is the
/// <b>place-of-supply driver</b> (CGST+SGST vs IGST). A second, independently-editable mailing State would let the
/// two contradict and <b>silently mis-compute tax</b> — a user could set mailing State = Maharashtra while the GST
/// State stayed Karnataka and get the wrong tax head with no warning. The orchestrator ruling for this slice is
/// <b>ONE state field</b>: the mailing screen's State field reads and writes <see cref="Ledger.MailingStateCode"/>,
/// which is a pure delegating accessor over <see cref="PartyGstDetails.StateCode"/>. Because there is only ONE
/// storage location, the two <i>cannot</i> diverge — this is enforced structurally, not by a synchronisation rule
/// that a later edit could forget to run. <c>MasterAlterationRulesTests</c> and
/// <c>PartyMailingStateSingleSourceTests</c> lock it (including a reflection assertion that this type declares no
/// State member).</para>
/// </summary>
/// <remarks>
/// Mutable value object hung off <see cref="Ledger"/> as a nullable reference, mirroring
/// <see cref="PartyGstDetails"/> / <see cref="InterestParameters"/>. <c>null</c> ⇒ no mailing details captured,
/// which is the default for every pre-v45 ledger and keeps its persisted + exported bytes identical (ER-13).
/// Framework- and DB-agnostic.
/// </remarks>
public sealed class PartyMailingDetails
{
    /// <summary>
    /// "Mailing Name" — the name printed on the invoice recipient block. Defaults from the ledger Name on the
    /// master screen but is independently editable (catalog §2 uses the same "Mailing Name (auto, editable)"
    /// convention for the Company). <c>null</c>/blank ⇒ printing falls back to the ledger's own Name.
    /// </summary>
    public string? MailingName { get; set; }

    /// <summary>
    /// The party's postal address as free text. Newline-separated; each line prints on its own line of the
    /// invoice recipient block (<c>VoucherPrintProjector.SplitAddress</c> → <c>InvoicePartyBlock.AddressLines</c>
    /// → <c>InvoicePdf</c>). <c>null</c> ⇒ no address (the pre-WI-4 behaviour: a blank recipient address).
    /// </summary>
    public string? Address { get; set; }

    /// <summary>The party's country; "India" on the overwhelming majority of masters. <c>null</c> ⇒ unset.</summary>
    public string? Country { get; set; }

    /// <summary>
    /// The party's PIN code — the field the CA called out explicitly. Six digits, first digit 1–9 (India Post
    /// allots 1–8; 9 is APO/FPO), validated by <see cref="EnsureValid"/>. <c>null</c>/blank ⇒ unset (not an
    /// error: a party whose PIN is not to hand must still be creatable).
    /// </summary>
    public string? Pincode { get; set; }

    /// <summary>True iff nothing at all was captured — the block carries no information and may be dropped to
    /// <c>null</c> so an untouched ledger stays byte-identical (ER-13).</summary>
    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(MailingName)
        && string.IsNullOrWhiteSpace(Address)
        && string.IsNullOrWhiteSpace(Country)
        && string.IsNullOrWhiteSpace(Pincode);

    /// <summary>The address split into printable lines (newline-separated, blanks dropped); empty when unset.</summary>
    public IReadOnlyList<string> AddressLines =>
        string.IsNullOrWhiteSpace(Address)
            ? Array.Empty<string>()
            : Address.Replace("\r\n", "\n").Replace('\r', '\n')
                     .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>
    /// Validates the block: a non-blank <see cref="Pincode"/> must be a six-digit Indian PIN. Throws
    /// <see cref="ArgumentException"/> on a bad value (fail-fast, ER-6). Everything else is free text.
    /// </summary>
    public void EnsureValid()
    {
        if (string.IsNullOrWhiteSpace(Pincode)) return;

        var pin = Pincode.Trim();
        if (pin.Length != 6 || pin[0] < '1' || pin[0] > '9' || !pin.All(char.IsAsciiDigit))
            throw new ArgumentException($"PIN code '{Pincode}' is not a valid 6-digit Indian PIN code.");
    }

    /// <summary>Trims every field and normalises blanks to <c>null</c>, so "  " and <c>null</c> persist alike.</summary>
    public void Normalize()
    {
        MailingName = Blank(MailingName);
        Address = Blank(Address);
        Country = Blank(Country);
        Pincode = Blank(Pincode);

        static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }
}
