using Apex.Ledger.Domain;

namespace Apex.Ledger.Services;

/// <summary>
/// The <b>§34 credit/debit-note (CDN)</b> engine (catalog §12; Phase 9 slice 2b; RQ-24; ER-12; DP-27). Framework-, DB-,
/// clock- and RNG-free: a pure, deterministic computation over the <see cref="Company"/> aggregate, reusing the
/// <see cref="GstService"/> total-then-split + <c>ComputeInvoiceTax</c>.
/// <para>
/// A §34 note is the <b>first-class GST document</b> that adjusts an original supply's output tax (DP-27): it is
/// <b>not</b> a new base type — it reuses the existing <see cref="VoucherBaseType.CreditNote"/> /
/// <see cref="VoucherBaseType.DebitNote"/> and <see cref="GstService.ComputeInvoiceTax"/>. The novelty this service
/// formalises is the four §34 essentials the shipped Phase-4 return voucher lacks:
/// <list type="bullet">
///   <item><b>The original-invoice link</b> — a <see cref="GstCreditDebitNoteLink"/> record (or a consolidated-party
///     reference); a note is never a free-floating tax delta (ER-12).</item>
///   <item><b>The directional output-tax adjustment</b> — a <b>credit</b> note <b>reduces</b> the supplier's output
///     liability (its Output tax legs post on the <b>Debit</b> / reducing side, opposite a sale); a <b>debit</b> note
///     <b>increases</b> it (Output tax on the <b>Credit</b> / increasing side, like a sale). The amounts/split reuse
///     <see cref="GstService.ComputeInvoiceTax"/> so CGST+SGST == IGST to the paisa.</item>
///   <item><b>The §34(2) 30-November guard</b> — a credit note that reduces liability declared after 30-Nov of the FY
///     <b>following</b> the original supply's FY is blocked (unless explicitly overridden). Debit notes are uncapped.</item>
///   <item><b>The GSTR-1 Table 9B feed</b> — driven off the link record's <see cref="GstCreditDebitNoteLink.CdnType"/>,
///     decoupled from the base-type→direction map so a §34 debit note (whose base type maps to Input) still nets the
///     supplier's <b>output</b> tax up and appears in the outward 9B table (see <c>Gstr1</c>/<c>Gstr3b</c>).</item>
/// </list>
/// A company that never issues a §34 note (no <c>gst_cdn_links</c> record) posts/serialises/projects byte-identically
/// to a v38 company (ER-13): the reports skip the CDN section entirely when the link collection is empty.
/// </para>
/// </summary>
public sealed class CreditDebitNoteService
{
    private readonly Company _company;
    private readonly GstService _gst;

    public CreditDebitNoteService(Company company)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));
        _gst = new GstService(company);
    }

    /// <summary>
    /// The §34(2) declaration cut-off for a credit note that reduces liability: <b>30-November of the FY immediately
    /// following</b> the original supply's financial year (or the annual-return date, whichever is earlier — the
    /// annual-return leg is a downstream concern, so this returns the 30-Nov limb, the operative one for the clone).
    /// Reuses the Indian-FY convention (<see cref="TdsService.FinancialYearOf"/>): a supply in FY 2024-25 (ends
    /// 31-Mar-2025) has a cut-off of 30-Nov-2025.
    /// </summary>
    public static DateOnly NovemberThirtyFollowing(DateOnly originalSupplyDate)
    {
        var (_, fyEnd) = TdsService.FinancialYearOf(originalSupplyDate); // 31-Mar-YYYY
        return new DateOnly(fyEnd.Year, 11, 30);
    }

    /// <summary>The result of building a §34 note: the directional Output-tax legs to post (on the reducing side for a
    /// credit note, the increasing side for a debit note) + the persisted <see cref="GstCreditDebitNoteLink"/> record +
    /// the raw computed split.</summary>
    /// <param name="TaxLines">The Output-tax entry lines, placed on the CDN-appropriate side; the caller books the party
    /// + sales/purchase legs and appends these.</param>
    /// <param name="Link">The §34 link record (already added to the company).</param>
    /// <param name="Computed">The raw <see cref="GstService.InvoiceTax"/> (per-head totals, breakdown) for the note.</param>
    public sealed record CdnPosting(
        IReadOnlyList<EntryLine> TaxLines, GstCreditDebitNoteLink Link, GstService.InvoiceTax Computed)
    {
        /// <summary>Σ GST (CGST+SGST+IGST) on the note (always the positive magnitude; the sign is the note's direction).</summary>
        public Money TotalTax => Computed.TotalTax;
    }

    /// <summary>
    /// Builds a §34 credit or debit note (RQ-24; ER-12; DP-27). Reuses <see cref="GstService.ComputeInvoiceTax"/> for the
    /// per-head split, then places the Output-tax legs on the <b>reducing</b> (Debit) side for a
    /// <see cref="CdnType.Credit"/> note and the <b>increasing</b> (Credit) side for a <see cref="CdnType.Debit"/> note.
    /// It creates + registers the <see cref="GstCreditDebitNoteLink"/> (whose constructor enforces ER-12 — a note must
    /// reference the original invoice voucher or a consolidated-party original-invoice number) and enforces the §34(2)
    /// 30-Nov guard on a liability-reducing credit note (blocked unless <paramref name="overrideTimeLimit"/>).
    /// </summary>
    /// <param name="type">Credit (reduces output) or Debit (increases output).</param>
    /// <param name="lines">The taxable value(s) at their integrated rate(s) — the amount the note adjusts.</param>
    /// <param name="interState">True ⇒ IGST; false ⇒ CGST+SGST (place-of-supply routing, same as the original supply).</param>
    /// <param name="cdnVoucherId">The credit/debit-note voucher id (the caller mints it before posting).</param>
    /// <param name="originalInvoiceVoucherId">The linked original-invoice voucher, or <c>null</c> for a consolidated ref.</param>
    /// <param name="originalInvoiceNumber">The original invoice number (required when the voucher link is null; ER-12).</param>
    /// <param name="originalInvoiceDate">The original supply date — drives the §34(2) FY basis.</param>
    /// <param name="cdnDate">The note's own date (checked against the §34(2) cut-off for a credit note).</param>
    /// <param name="reasonCode">The §34 reason (e.g. "01 sales return"); required.</param>
    /// <param name="is9BTarget">True ⇒ a registered-party CDN (GSTR-1 Table 9B); false ⇒ an unregistered CDN.</param>
    /// <param name="overrideTimeLimit">Explicitly permit a credit note past the §34(2) 30-Nov cut-off (fail-fast house style: default blocks).</param>
    public CdnPosting BuildCreditDebitNote(
        CdnType type,
        IReadOnlyList<GstService.TaxableLine> lines,
        bool interState,
        Guid cdnVoucherId,
        Guid? originalInvoiceVoucherId,
        string? originalInvoiceNumber,
        DateOnly? originalInvoiceDate,
        DateOnly cdnDate,
        string reasonCode,
        bool is9BTarget = true,
        bool overrideTimeLimit = false)
    {
        ArgumentNullException.ThrowIfNull(lines);
        if (lines.Count == 0)
            throw new ArgumentException("A §34 note must carry at least one taxable line.", nameof(lines));

        // §34(2): a CREDIT note that reduces the supplier's output liability must be declared no later than 30-Nov of the
        // FY following the original supply. A later credit note is blocked (unless explicitly overridden). Debit notes
        // are uncapped (no issuance cut-off; a debit note's ITC is keyed to its own §16(4) FY — an S6/S7 concern).
        if (type == CdnType.Credit && !overrideTimeLimit)
        {
            // The cut-off is anchored to the original supply date; without it §34(2) cannot be verified, so a
            // liability-reducing credit note with a NULL original-invoice date is rejected (finding #5) rather than
            // silently bypassing the guard (which would wave a very-late note through). Pass overrideTimeLimit to force.
            if (originalInvoiceDate is not { } origDate)
                throw new ArgumentException(
                    "A liability-reducing §34 credit note requires the original supply date to verify the §34(2) "
                    + "30-November declaration cut-off — supply the original-invoice date, or pass overrideTimeLimit to "
                    + "bypass the check.", nameof(originalInvoiceDate));
            var deadline = NovemberThirtyFollowing(origDate);
            if (cdnDate > deadline)
                throw new InvalidOperationException(
                    $"Credit note dated {cdnDate:yyyy-MM-dd} is past the §34(2) declaration cut-off of "
                    + $"{deadline:yyyy-MM-dd} (30-November following the original supply's FY) — a liability-reducing "
                    + "credit note declared after the cut-off is not permitted (pass overrideTimeLimit to force).");
        }

        // The per-head split is computed EXACTLY like an ordinary Output invoice (reuse ComputeInvoiceTax → CGST+SGST ==
        // IGST to the paisa). It posts the tax on the CREDIT side (correct for a sale / an increasing debit note); for a
        // credit note we place the SAME legs on the DEBIT (reducing) side so the note nets the Output ledger DOWN.
        var computed = _gst.ComputeInvoiceTax(lines, interState, GstTaxDirection.Output);
        var taxSide = type == CdnType.Credit ? DrCr.Debit : DrCr.Credit;
        var taxLines = computed.TaxLines
            .Select(l => new EntryLine(l.LedgerId, l.Amount, taxSide, gst: l.Gst))
            .ToList();

        // The link record (its constructor enforces ER-12: a note is never free-floating). Registered on the company so
        // the persistence/Io round-trip carries it and the reports (Table 9B, output-tax netting) can find it by voucher.
        var link = new GstCreditDebitNoteLink(
            Guid.NewGuid(), cdnVoucherId, type, originalInvoiceVoucherId, originalInvoiceNumber,
            originalInvoiceDate, reasonCode, is9BTarget);
        _company.AddCreditDebitNoteLink(link);

        return new CdnPosting(taxLines, link, computed);
    }
}
