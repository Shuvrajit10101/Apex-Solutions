using Apex.Ledger.Domain;
using Apex.Ledger.Reports;

namespace Apex.Ledger.Services;

/// <summary>
/// Records <b>ITC received from an Input Service Distributor</b> — the recipient half of ISD, and the half the
/// vendor's product actually has.
///
/// <para><b>WHAT THE VENDOR DOES, QUOTED BY CONTENT.</b> At
/// <c>help.tallysolutions.com/tally-prime/india-gst/isd-credit/</c>: <i>"You can account for Input Service
/// Distributor Credit (ISD) Credit in TallyPrime when your branch receives Input Tax Credit distributed by the
/// head office"</i>. The procedure is a <b>Journal</b> voucher (<i>"Press Alt+G (Go To) &gt; Create Voucher &gt;
/// press F7 (Journal)"</i>) whose Stat Adjustment carries <i>Type of duty/tax</i> = <i>"GST"</i>, <i>Nature of
/// Adjustment</i> = <i>"Increase in Input Tax Credit"</i> and <i>Additional Nature of Adjustment</i> =
/// <i>"ISD Invoice/Debit Note"</i>, with the ISD invoice / debit-note number and date, and <i>Type of Supply</i>
/// = <i>"Services"</i>. The outcome the page states: <i>"The voucher will be included in GSTR-3B and appear
/// under 4A: Input Tax Credit Available (either in part or in full) &gt; Inward supplies from ISD."</i> The page
/// also bounds the mechanism: <i>"Credit related to goods or capital goods cannot be distributed through
/// ISD"</i> — ISD is input <b>services</b> only.
/// </para>
///
/// <para>🔴 <b>WHAT THE VENDOR DOES NOT DO, MEASURED ACROSS THREE OF ITS OWN PAGES — AND WHY THIS SERVICE
/// STOPS WHERE IT DOES.</b> The ISD page above covers only credit <b>received</b>; it never describes acting AS
/// an ISD, and it never mentions GSTR-6. The vendor's GST-returns FAQ
/// (<c>help.tallysolutions.com/india-gst-faq-tally/</c>) enumerates GSTR-1, GSTR-2/2A/2B, GSTR-3B, GSTR-9/9C and
/// CMP-08 — <b>GSTR-6 is absent</b> — and states <i>"Using TallyPrime Release 5.0, you can upload GSTR-1,
/// GSTR-3B, and CMP-08."</i> The release-wise feature list
/// (<c>help.tallysolutions.com/tallyprime-features-release-wise/</c>) contains no occurrence of "GSTR-6",
/// "ISD" or "Input Service Distributor" at all. So <b>credit DISTRIBUTION and the ISD return are beyond the
/// clone target</b>, and this service deliberately does not post either. Rule 39 distribution arithmetic already
/// exists in this repo as <see cref="IsdDistribution"/> and is, by that measurement, <b>ours rather than
/// cloned</b>; nothing here feeds it.</para>
///
/// <para><b>THE LEDGER SHAPE, AND THE ONE PLACE IT IS A REASONED MAPPING RATHER THAN A QUOTE.</b> The vendor
/// names the debit side generically — <i>"select the reversal of ITC ledger created under Current Assets or
/// Expenses"</i> — and the credit side as <i>"the purchase, expense, or branch account ledger, as
/// applicable"</i>. In this book the four <c>Input {head}</c> Duties-&amp;-Taxes ledgers <b>are</b> the
/// electronic credit ledger's pools (see <see cref="ElectronicLedgersView"/>), so the ITC-bearing debit is
/// posted <b>per head</b> against those ledgers. That is required anyway: Table 4(A)(4) is a head-wise figure,
/// and a single lump debit to one asset ledger could not produce it. The credit leg is the counterpart ledger
/// the operator picks, exactly as the vendor describes. <b>Labelled here as our mapping of the vendor's generic
/// wording onto this book's chart of accounts, not as a vendor quote.</b></para>
///
/// <para><b>Scope.</b> ITC lands in the electronic credit ledger of exactly one GSTIN, so the stat-adjustment
/// voucher is stamped with the registration it belongs to, the same rule <see cref="GstReversalService"/>
/// applies. The stamp is written only when it differs from <see cref="GstRegistration.PrimaryId"/>, so a
/// single-registration book stores the same <c>null</c> it always stored (ER-13).</para>
/// </summary>
public sealed class GstIsdCreditService
{
    private readonly Company _company;

    /// <summary>Creates the service over <paramref name="company"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="company"/> is null.</exception>
    public GstIsdCreditService(Company company)
    {
        ArgumentNullException.ThrowIfNull(company);
        _company = company;
    }

    /// <summary>
    /// The per-head ITC distributed to this branch by the ISD, in integer paisa. Cess is carried as its own head
    /// (ER-2) and is never folded into the three tax heads.
    /// </summary>
    public readonly record struct IsdCreditAmount(long CgstPaisa, long SgstPaisa, long IgstPaisa, long CessPaisa)
    {
        /// <summary>Σ across the four heads, in paisa.</summary>
        public long TotalPaisa => CgstPaisa + SgstPaisa + IgstPaisa + CessPaisa;

        /// <summary>True iff every head is zero — nothing was distributed.</summary>
        public bool IsZero => TotalPaisa == 0;
    }

    /// <summary>
    /// Posts one ISD credit receipt as a balanced stat-adjustment <b>Journal</b>: a <c>Dr Input {head}</c> leg per
    /// non-zero head, each tagged <see cref="GstAdjustmentKind.IsdCreditReceived"/>, against a single
    /// <c>Cr <paramref name="counterpartLedgerId"/></c> for the total. Returns the posted voucher.
    ///
    /// <para>🔴 <b>THE DUPLICATE REFUSAL IS OURS AND IS LABELLED AS SUCH.</b> No retrieved vendor page states
    /// whether re-entering the same ISD invoice number is refused, silently ignored, or posted twice. Posting it
    /// twice would double a filed Table 4(A)(4) figure and double the credit in the books, so an explicit
    /// refusal naming the clash is taken in preference to either silent arm. It is keyed on (document number,
    /// registration) because the same ISD invoice number may legitimately reach two different branches of one
    /// company, each with its own credit.</para>
    /// </summary>
    /// <param name="documentNumber">The ISD invoice / ISD debit-note number (required, non-blank).</param>
    /// <param name="documentDate">The ISD invoice / debit-note date.</param>
    /// <param name="amount">The per-head credit distributed to this branch.</param>
    /// <param name="counterpartLedgerId">The purchase / expense / branch-account ledger to credit.</param>
    /// <param name="date">The voucher date.</param>
    /// <param name="registrationId">The registration receiving the credit; null ⇒ the primary.</param>
    /// <exception cref="ArgumentException">The document number is blank, a head is negative, every head is zero,
    /// or the counterpart ledger does not exist.</exception>
    /// <exception cref="InvalidOperationException">GST is not enabled, or the same document number has already
    /// been recorded for this registration.</exception>
    public Voucher Post(
        string documentNumber,
        DateOnly documentDate,
        IsdCreditAmount amount,
        Guid counterpartLedgerId,
        DateOnly date,
        Guid? registrationId = null)
    {
        if (string.IsNullOrWhiteSpace(documentNumber))
            throw new ArgumentException(
                "An ISD invoice / debit-note number is required — it is the document the credit is received against.",
                nameof(documentNumber));

        if (amount.CgstPaisa < 0 || amount.SgstPaisa < 0 || amount.IgstPaisa < 0 || amount.CessPaisa < 0)
            throw new ArgumentException("An ISD credit amount must be ≥ 0 paisa.", nameof(amount));

        if (amount.IsZero)
            throw new ArgumentException(
                "An ISD credit receipt must carry credit in at least one head — a zero receipt records nothing.",
                nameof(amount));

        if (!_company.GstEnabled)
            throw new InvalidOperationException(
                "GST is not enabled for this company, so there is no input-credit pool to receive ISD credit into.");

        var counterpart = _company.FindLedger(counterpartLedgerId)
            ?? throw new ArgumentException(
                "The purchase / expense / branch-account ledger to credit was not found.", nameof(counterpartLedgerId));

        var scope = registrationId is { } req
            ? (req == Guid.Empty ? GstRegistration.PrimaryId : req)
            : GstRegistration.PrimaryId;

        var trimmed = documentNumber.Trim();
        if (FindExisting(trimmed, scope) is { } clash)
            throw new InvalidOperationException(
                $"ISD invoice / debit note '{trimmed}' has already been recorded for this registration " +
                $"(voucher dated {clash.Date:dd-MMM-yyyy}). Recording it again would claim the same credit twice " +
                "in Table 4(A)(4) and in the books.");

        var gst = new GstService(_company);
        var type = new GstSetOffService(_company).EnsureStatAdjustmentType();

        var lines = new List<EntryLine>();

        void Leg(GstTaxHead head, long paisa)
        {
            if (paisa <= 0) return;
            if (head == GstTaxHead.Cess) gst.EnsureCessLedgers();
            var input = gst.FindTaxLedger(head, GstTaxDirection.Input)
                ?? throw new InvalidOperationException(
                    $"Input {head} ledger not found — enable GST first (EnableGst auto-creates it).");
            lines.Add(new EntryLine(input.Id, new Money(paisa / 100m), DrCr.Debit,
                gst: new GstLineTax(head, 0, Money.Zero, adjustment: GstAdjustmentKind.IsdCreditReceived)));
        }

        Leg(GstTaxHead.Central, amount.CgstPaisa);
        Leg(GstTaxHead.State, amount.SgstPaisa);
        Leg(GstTaxHead.Integrated, amount.IgstPaisa);
        Leg(GstTaxHead.Cess, amount.CessPaisa);

        lines.Add(new EntryLine(counterpart.Id, new Money(amount.TotalPaisa / 100m), DrCr.Credit));

        var voucher = new Voucher(
            Guid.NewGuid(), type.Id, date, lines,
            narration: $"{NarrationPrefix}{trimmed} dated {documentDate:dd-MMM-yyyy}");

        if (scope != GstRegistration.PrimaryId) voucher.GstRegistrationId = scope;

        return new LedgerService(_company).Post(voucher);
    }

    /// <summary>
    /// The narration prefix every ISD receipt carries. It is how <see cref="FindExisting"/> recovers the document
    /// number without a new column — and therefore <b>without a migration</b>.
    /// <para>🔴 <b>Stated plainly because it is a constraint, not a design:</b> the document number is carried in
    /// the narration rather than in a typed field. A typed <c>isd_receipts</c> table would be the better home and
    /// would also let the receipt be listed and amended; that is DDL, and taking a schema version was not this
    /// track's to take. The duplicate guard below is therefore only as strong as the narration, and an operator
    /// who edits the narration of a posted receipt can defeat it.</para>
    /// </summary>
    public const string NarrationPrefix = "ISD credit received — ";

    /// <summary>The already-posted ISD receipt for <paramref name="documentNumber"/> under
    /// <paramref name="scope"/>, or null. Case-insensitive on the document number: the same invoice re-keyed in a
    /// different case is the same invoice.</summary>
    private Voucher? FindExisting(string documentNumber, Guid scope)
    {
        var needle = NarrationPrefix + documentNumber + " dated ";
        foreach (var v in _company.Vouchers)
        {
            if (v.Narration is not { } n) continue;
            if (!n.StartsWith(needle, StringComparison.OrdinalIgnoreCase)) continue;
            if (GstReportSupport.RegistrationOf(v) != scope) continue;
            // A CANCELLED receipt claims no credit and files no figure, so it must not block a re-entry of the
            // same ISD invoice — otherwise cancelling a mis-keyed receipt would permanently lock its number out.
            if (v.Cancelled) continue;
            var type = _company.FindVoucherType(v.TypeId);
            if (type is null || !type.IsGstStatAdjustmentType) continue;
            return v;
        }
        return null;
    }
}
