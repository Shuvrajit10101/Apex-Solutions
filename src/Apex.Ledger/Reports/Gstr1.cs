using Apex.Ledger.Domain;
using Apex.Ledger.Services;

namespace Apex.Ledger.Reports;

/// <summary>
/// One <b>B2B</b> row of GSTR-1 (phase4-gst-requirements RQ-21): a single registered-party outward invoice —
/// the party GSTIN, the invoice number + date, the place-of-supply state, and the taxable value with its
/// CGST/SGST (intra) or IGST (inter) — read from the posted tax lines. Phase-4 aggregates the invoice's tax
/// per head (no Large/Small split; that is Phase 9).
/// </summary>
public sealed record Gstr1B2BRow(
    string PartyName,
    string? PartyGstin,
    string InvoiceNumber,
    DateOnly InvoiceDate,
    string? PlaceOfSupplyStateCode,
    Money TaxableValue,
    Money Cgst,
    Money Sgst,
    Money Igst)
{
    /// <summary>
    /// The Invoice Reference Number of a <b>Generated</b> e-invoice for this document (Phase 9 slice 4a; RQ-18), or
    /// <c>null</c> when the document carries no IRN. An <b>additive</b> annotation: a company that does not e-invoice
    /// leaves it null on every row (byte-identical, ER-13); it is not written to the tabular export in S4a.
    /// </summary>
    public string? Irn { get; init; }

    /// <summary>The <b>raw underlying voucher sequence number</b> of this document (the bare int, before affix rendering).
    /// A display-invisible ordering key: <see cref="InvoiceNumber"/> is the rendered display string (prefix/suffix and
    /// all), so numeric ordering of the amendment tables (<see cref="Gstr1Amendments"/>) must sort on this raw int — an
    /// ordinal sort of the rendered string would place "10" before "2". Defaults to 0 (never emitted anywhere).</summary>
    public int RawNumber { get; init; }
}

/// <summary>
/// One consolidated <b>B2C</b> row of GSTR-1 (RQ-21; DP-8): outward supplies to unregistered/consumer parties
/// grouped <b>rate-wise</b> (the Phase-4 simple consolidation — no B2C-Large/Small interstate split). Carries
/// the integrated rate, taxable value and CGST/SGST/IGST accumulated across all B2C supplies at that rate.
/// </summary>
public sealed record Gstr1B2CRow(int RateBasisPoints, Money TaxableValue, Money Cgst, Money Sgst, Money Igst);

/// <summary>One rate-wise summary row of GSTR-1: total taxable value and total tax at an integrated rate.</summary>
public sealed record Gstr1RateRow(int RateBasisPoints, Money TaxableValue, Money TotalTax);

/// <summary>
/// One HSN-summary row of GSTR-1 (RQ-21; law L-7): a supply grouped by HSN/SAC — the code, a description, the
/// unit-quantity code (UQC), total quantity, taxable value, and CGST/SGST/IGST. Built from the outward
/// item-invoice stock lines, with each invoice's posted tax apportioned to its lines by value share.
/// </summary>
/// <param name="Quantity">
/// The total quantity, stated in <paramref name="Uqc"/>'s unit. <b>Commensurable ONLY within one HSN and only
/// against a row carrying the SAME <paramref name="Uqc"/>.</b> Two rows of one HSN from different periods may
/// legitimately carry different UQCs (April billed in DOZ, May in NOS), so summing this field across them adds
/// quantities in different units — "2 DOZ + 5 NOS = 7" is nonsense under either label. An aggregator that spans
/// rows must compare <paramref name="Uqc"/> and, on any mismatch, fall back to <paramref name="BaseQuantity"/>
/// under <paramref name="BaseCode"/>, which every row states in the item's own base unit and is therefore always
/// addable. <c>Gstr9.Build</c> is the worked example.
/// </param>
/// <param name="BaseQuantity">
/// The SAME physical quantity re-expressed in the item's base unit (24, where <paramref name="Quantity"/> is
/// 2 DOZ) — the commensurable measure an aggregator degrades to. Always populated, never collapsed: a row that is
/// ITSELF already degraded still carries the raw base accumulator, so a further aggregation can degrade again.
/// </param>
/// <param name="BaseCode">The item's base-unit UQC — the label <paramref name="BaseQuantity"/> is stated in.</param>
/// <param name="DeclaredUnitId">
/// The identity of the simple unit <paramref name="Quantity"/> counts. <b>An aggregator spanning rows must decide
/// commensurability with <see cref="UqcResolver.AreCommensurable"/>, never by comparing <paramref name="Uqc"/>
/// alone</b> — <c>"OTH"</c> is the department's catch-all for a unit absent from the master list, so two rows
/// stated in DIFFERENT unmapped units carry the SAME label and a label comparison sums 5 Crates and 3 Pallets into
/// "8 OTH". On a degrade this equals <paramref name="BaseUnitId"/>, so an already-degraded row is self-consistent
/// and degrading it again is the identity.
/// </param>
/// <param name="BaseUnitId">
/// The identity of the item's base unit — what <paramref name="BaseQuantity"/> counts and <paramref name="BaseCode"/>
/// names. Lets a further aggregation see whether even the degrade target is commensurable.
/// </param>
/// <param name="MixedBases">
/// <b>KNOWN LIMITATION, RECORDED — and, as of this slice, NOT YET SURFACED TO ANY READER.</b> True when this row
/// folded lines (or period rows) whose
/// items are measured in UNRELATED base units — 5 Nos of eggs and 3 Kgs of rice under one HSN. The degrade target
/// is then itself incommensurable and <paramref name="Quantity"/>/<paramref name="BaseQuantity"/> cannot be a
/// meaningful single figure under any label. This is pre-existing and is NOT resolved here: there is no correct
/// number to emit while the table is keyed on HSN alone, and inventing one (0, or the first-seen label) would
/// merely hide it. Real Table 12 rows appear to be keyed on (HSN, UQC, rate), which would remove the need to
/// degrade at all — recorded as a HYPOTHESIS, not a particular: under R7 that row-keying must be verified against
/// an official primary source before the shape of a FILED output is changed.
///
/// <para><b>THE FLAG HAS NO PRODUCTION CONSUMER TODAY — state that plainly rather than implying a warning reaches
/// anyone.</b> (Tests do read it, to pin the limitation; nothing a user can see does.)
/// <c>ReportsViewModel</c> and <c>Gstr9ReportViewModel</c> both project the HSN row WITHOUT it, and
/// <c>GstReturnJson</c> has no field for it, so a reader of the GSTR-1 Table-12 / GSTR-9 Table-17 grid sees the
/// folded number with NO indication it is unsound: 5 Nos of eggs and 3 Kgs of rice under one HSN display as
/// "8 NOS", and that number is FILED. The flag is recorded here for a future consumer — the report grids carry
/// eight fixed columns, all occupied on this row, so surfacing it is a grid change and not a one-liner, and it is
/// deliberately out of scope for this slice. <b>Anyone adding that consumer:</b> the value is already computed and
/// carried correctly across both the line fold and the cross-period fold; only the presentation is missing.</para>
/// </param>
public sealed record Gstr1HsnRow(
    string HsnSac,
    string Description,
    string? Uqc,
    decimal Quantity,
    Money TaxableValue,
    Money Cgst,
    Money Sgst,
    Money Igst,
    Money Cess,
    decimal BaseQuantity,
    string? BaseCode,
    Guid? DeclaredUnitId,
    Guid? BaseUnitId,
    bool MixedBases)
{
    /// <summary>Σ tax on this HSN row (CGST + SGST + IGST). <b>Cess is deliberately NOT included</b> — the return
    /// states Compensation-Cess in its own column beside the tax amount, and the cess ring-fence (ER-2) is the
    /// same distinction carried onto the filed row.</summary>
    public Money TotalTax => new(Cgst.Amount + Sgst.Amount + Igst.Amount);

    /// <summary>
    /// <b>Table 12 "Total Value"</b> — the total value of the supplies under this HSN, i.e. the taxable value plus
    /// every tax levied on it. This is the SECOND of the two statutory cells Table 12 filed blank: the return states
    /// Total Value and Taxable Value as separate columns, and the GST portal's own HSN tile reports
    /// "Total Value, Total Taxable Value and Total Tax Liability" against the summary
    /// (https://tutorial.gst.gov.in/userguide/returns/Creation_of_Outward_Supplies_Return_in_GSTR-1.htm).
    ///
    /// <para><b>🔴 CESS IS INCLUDED HERE, AND THAT IS NOT A CONTRADICTION OF <see cref="TotalTax"/>.</b> The two
    /// members answer different questions. <c>TotalTax</c> is the return's <i>tax amount</i> cell, which ring-fences
    /// Compensation-Cess into its own column (ER-2) and so must exclude it. <c>TotalValue</c> is what the consignment
    /// is worth in total — a value, not a tax classification — and cess is unquestionably part of what the recipient
    /// is billed. Excluding cess here would file a Total Value that is short by exactly the cess on every cess-bearing
    /// HSN, which is the same class of silent understatement this slice exists to close.</para>
    ///
    /// <para>Derived rather than accumulated on purpose: every component is already folded correctly across the line
    /// fold and the cross-period fold, so a stored copy could only ever drift out of agreement with them.</para>
    /// </summary>
    public Money TotalValue => new(TaxableValue.Amount + TotalTax.Amount + Cess.Amount);
}

/// <summary>
/// One <b>Table 9B</b> row of GSTR-1 (Phase 9 slice 2b; RQ-24; §34): a credit/debit note against an original invoice —
/// the note type (C/D), the original-invoice reference (number + date), the note's own date, place of supply, and the
/// <b>signed</b> taxable value + CGST/SGST/IGST (a credit note is negative, a debit note positive), plus the §34 reason.
/// Read off the posted note tax lines (never recomputed), joined to the <see cref="GstCreditDebitNoteLink"/> record.
/// </summary>
public sealed record Gstr1Table9BRow(
    CdnType NoteType,
    Guid? OriginalInvoiceVoucherId,
    string? OriginalInvoiceNumber,
    DateOnly? OriginalInvoiceDate,
    DateOnly NoteDate,
    string? PlaceOfSupplyStateCode,
    Money TaxableValue,
    Money Cgst,
    Money Sgst,
    Money Igst,
    string ReasonCode,
    bool Is9BTarget)
{
    /// <summary>Σ signed tax on this note (CGST + SGST + IGST).</summary>
    public Money TotalTax => new(Cgst.Amount + Sgst.Amount + Igst.Amount);
}

/// <summary>One <b>Table 11A</b> row of GSTR-1 (Phase 9 slice 2b; RQ-25): advance <b>received</b> in the period, grouped
/// by integrated rate and place-of-supply nature, with its tax (services only — goods advances are de-taxed).</summary>
public sealed record Gstr1AdvanceRow(
    int RateBasisPoints, bool InterState, Money AdvanceReceived, Money Cgst, Money Sgst, Money Igst)
{
    /// <summary>Σ advance tax on this row.</summary>
    public Money TotalTax => new(Cgst.Amount + Sgst.Amount + Igst.Amount);
}

/// <summary>One <b>Table 11B</b> row of GSTR-1 (Phase 9 slice 2b; RQ-25): advance <b>adjusted</b> against an invoice in
/// the period, grouped by integrated rate and place-of-supply nature (reverses the 11A the advance was reported in).</summary>
public sealed record Gstr1AdvanceAdjustedRow(
    int RateBasisPoints, bool InterState, Money AdvanceAdjusted, Money Cgst, Money Sgst, Money Igst)
{
    /// <summary>Σ adjusted advance tax on this row.</summary>
    public Money TotalTax => new(Cgst.Amount + Sgst.Amount + Igst.Amount);
}

/// <summary>
/// <b>GSTR-1</b> (outward supplies) — a pure, read-only projection over the posted <b>outward</b> (Sales /
/// Credit-Note) GST vouchers in <c>[from, to]</c> (phase4-gst-requirements RQ-21; catalog §12; ER-7). It reads
/// the tax off each tax <see cref="EntryLine"/>'s <see cref="GstLineTax"/> — never recomputing — so the
/// section totals reconcile to the Output tax-ledger postings for the period. Sections: <see cref="B2B"/> (one
/// row per registered-party invoice, party has a GSTIN), <see cref="B2C"/> (unregistered/consumer supplies
/// consolidated rate-wise, DP-8), a <see cref="RateSummary"/> and an <see cref="HsnSummary"/>.
/// Cancelled and post-dated-after-<c>to</c> vouchers are excluded; exempt/nil/non-GST outward value is shown
/// in <see cref="ExemptNilNonGstValue"/>. A non-GST company yields empty sections. No UI, no DB.
/// </summary>
public sealed record Gstr1(
    DateOnly From,
    DateOnly To,
    IReadOnlyList<Gstr1B2BRow> B2B,
    IReadOnlyList<Gstr1B2CRow> B2C,
    IReadOnlyList<Gstr1RateRow> RateSummary,
    IReadOnlyList<Gstr1HsnRow> HsnSummary,
    Money ExemptNilNonGstValue,
    Money TotalCgst,
    Money TotalSgst,
    Money TotalIgst)
{
    /// <summary>
    /// Table 4B (Phase 9 slice 2; RQ-7) — the value of <b>outward</b> supplies on which the <b>recipient</b> pays tax
    /// under reverse charge (the invoice carries zero tax but must be flagged). Light in S2a: a single value bucket
    /// (Σ the outward supply value of vouchers whose sales/expense ledger is flagged
    /// <see cref="StockItemGstDetails.ReverseChargeApplicable"/>). Default <c>Money.Zero</c> so a company with no outward
    /// RCM supply is byte-identical (ER-13).
    /// </summary>
    public Money Rcm4BOutwardValue { get; init; }

    /// <summary>
    /// Table 9B (Phase 9 slice 2b; RQ-24; §34) — the credit/debit notes issued against original invoices in the period,
    /// each signed by note type (credit negative, debit positive). Default empty so a company with no §34 note is
    /// byte-identical (ER-13). The note tax is <b>already folded (signed) into <see cref="TotalCgst"/>/…</b>.
    /// </summary>
    public IReadOnlyList<Gstr1Table9BRow> Table9B { get; init; } = [];

    /// <summary>Table 11A (Phase 9 slice 2b; RQ-25) — advances received (services only) in the period. Default empty (ER-13).</summary>
    public IReadOnlyList<Gstr1AdvanceRow> Table11A { get; init; } = [];

    /// <summary>Table 11B (Phase 9 slice 2b; RQ-25) — advances adjusted against invoices in the period. Default empty (ER-13).</summary>
    public IReadOnlyList<Gstr1AdvanceAdjustedRow> Table11B { get; init; } = [];

    /// <summary>Σ advance tax received (Table 11A) across all rows.</summary>
    public Money AdvanceTaxReceived =>
        new(Table11A.Sum(r => r.Cgst.Amount + r.Sgst.Amount + r.Igst.Amount));

    /// <summary>Σ advance tax adjusted (Table 11B) across all rows.</summary>
    public Money AdvanceTaxAdjusted =>
        new(Table11B.Sum(r => r.Cgst.Amount + r.Sgst.Amount + r.Igst.Amount));

    /// <summary>Σ all output tax on the return (CGST + SGST + IGST). Includes the §34 CDN net (signed).</summary>
    public Money TotalTax => new(TotalCgst.Amount + TotalSgst.Amount + TotalIgst.Amount);

    /// <summary>Builds GSTR-1 over <c>[from, to]</c> for the GST registration named by
    /// <paramref name="registrationId"/> (census 6.23; <c>null</c> ⇒ the company's only registration — and a
    /// multi-registration company REFUSES an unscoped build, see <c>GstReportSupport</c>).</summary>
    public static Gstr1 Build(Company company, DateOnly from, DateOnly to, Guid? registrationId = null)
    {
        // Phase 9 slice 3 (RQ-16): a Composition dealer files CMP-08 / GSTR-4, NOT GSTR-1 — early-return an empty return
        // so it never emits an outward-supplies return (the Desktop routes it to the CMP-08/GSTR-4 screens). The inward
        // RCM the dealer owes is reported in CMP-08 Table 3(ii), so nothing is lost. A Regular company never enters this
        // branch ⇒ byte-identical (ER-13).
        if (company.Gst?.RegistrationType == GstRegistrationType.Composition)
            return new Gstr1(from, to, [], [], [], [], Money.Zero, Money.Zero, Money.Zero, Money.Zero);

        var b2b = new List<Gstr1B2BRow>();
        // Table 9B rows for the §34 notes the user did NOT formalise with a link (see the unlinked-9B block in the
        // sweep). Folded into Table9B by BuildTable9B so both kinds sort through one comparator; their tax is NOT
        // re-folded into the totals, because the sweep below has already signed it in.
        var unlinked9B = new List<Gstr1Table9BRow>();
        var b2cAcc = new Dictionary<int, HeadAmounts>();       // by integrated rate
        var rateAcc = new Dictionary<int, (decimal Taxable, decimal Tax)>();
        var hsnAcc = new Dictionary<string, HsnAcc>();
        var exempt = 0m;
        var totalCgst = 0m; var totalSgst = 0m; var totalIgst = 0m;

        foreach (var (voucher, voucherType) in GstReportSupport.PostedDirectionalVouchers(company, from, to, GstTaxDirection.Output, registrationId))
        {
            // Phase 9 slice 2b: a formalised §34 credit/debit note is a first-class outward document projected by Table 9B
            // (signed by note type) and folded — signed — into the output totals below. Exclude it from the ordinary
            // invoice sweep so its tax is not double-counted (mirrors the RCM / outward-4B exclusions, risk #4). A company
            // with no §34 note never enters this branch (byte-identical, ER-13).
            //
            // 🔴 THIS EXCLUSION IS LOAD-BEARING AND MUST STAY FIRST, now more than before: the sign below would
            // otherwise net a LINKED note into the aggregates while BuildTable9B ALSO folds it into the totals,
            // double-reducing the return. Pinned by
            // Gstr1UnlinkedReturnNoteSignTests.A_linked_section_34_note_is_still_projected_once_through_Table_9B.
            if (GstReportSupport.CdnLinkFor(company, voucher) is not null) continue;

            // 🔴 T2-59 — THE OUTWARD SIGN OF THIS DOCUMENT, and the one fact the whole sweep used to ignore.
            // Every amount below is read as a POSITIVE MAGNITUDE off EntryLine.Amount; the direction lives in
            // EntryLine.Side, which none of these readers looks at. A Sales voucher raises outward supply (+1); a
            // Credit Note REVERSES one (-1), and the engine has already posted it that way —
            // ComputeItemInvoiceGst passes `reverseSides: IsReturnNote`, so the note genuinely DEBITS Output CGST.
            // Without the sign the report re-added a sales return as though it were a sale: measured on the
            // fixture in the owning test, a ₹10,000 + ₹5,000 return made Table 12 file ₹60,000 taxable / ₹5,400
            // per head / 3,000 Nos and an exempt bucket of ₹25,000, against an Output CGST ledger of ₹3,600 —
            // breaking this type's own documented invariant that "the section totals reconcile to the Output
            // tax-ledger postings for the period".
            //
            // 🔴 IT READS THE EXISTING ONE HOME, VoucherEffects.IsReturnNote, AND DOES NOT INVENT A SECOND
            // PREDICATE. ~~There is no shared sign helper in this codebase (a prior report claimed a
            // `GstReportSupport.SignOf`; grep returns zero hits in src/ and tests/ — it never existed)~~ 🔴 T2-119
            // (vi): THAT SENTENCE IS NOW FALSE AND IS STRUCK IN PLACE RATHER THAN DELETED, because a comment that
            // refutes itself while BEING one of the grep's hits is exactly the stale assertion this project keeps
            // catching. GstReportSupport.SignOf DOES exist (landed wave 44) with many live call sites, and
            // TaxAnalysis.ReadSide now routes BOTH arms through it. IsReturnNote is kept HERE deliberately, not by
            // inertia: the sign-of-a-note defect has already been fixed one-call-site-at-a-time four times, and this
            // sweep's behaviour was gated and mutation-pinned on IsReturnNote. The two agree on this path — the
            // CdnLinkFor exclusion immediately above stands FIRST, so SignOf's linked-note branch is unreachable
            // here and it reduces to -1 for an unlinked credit note, the same answer. Swapping the predicate would
            // change no figure and would move a pinned behaviour onto an equivalence rather than onto its own test,
            // so it is left alone and said out loud instead. IsReturnNote
            // is the declared home for "this carrier reverses an earlier document" and is derived from the BASE
            // TYPE alone — wholly independent of the opt-in §34 toggle, which is exactly why it is the right
            // source: the toggle is an annotation about REPORTING, not about which way the money moved.
            var sign = VoucherEffects.IsReturnNote(voucherType.BaseType) ? -1m : 1m;

            // Per-invoice posted tax by head (read off the tax lines — never recomputed).
            var invoice = ReadInvoiceHeads(voucher);
            var hasTax = invoice.Cgst != 0m || invoice.Sgst != 0m || invoice.Igst != 0m;
            totalCgst += sign * invoice.Cgst; totalSgst += sign * invoice.Sgst; totalIgst += sign * invoice.Igst;

            // An outward reverse-charge supply (zero forward tax; sales ledger flagged ReverseChargeApplicable) belongs
            // ONLY in Table 4B (Rcm4BOutwardValue) — never the exempt/nil/non-GST bucket or the HSN sweep, else it is
            // double-represented (its value would appear in both 4B and exempt). Skip it here (Phase 9 slice 2; RQ-7).
            if (!hasTax && GstReportSupport.IsOutwardReverseChargeSupply(company, voucher)) continue;

            // HSN summary + exempt bucket — every other outward supply (taxable AND exempt/nil) contributes here,
            // SIGNED: a sales return nets its HSN row's value, tax, cess AND filed quantity down, and nets the
            // exempt bucket down when the returned supply was exempt (T2-59's own half of the defect).
            AccumulateHsn(company, voucher, invoice, hsnAcc, ref exempt, sign);

            // A no-tax supply (exempt/nil/non-GST) belongs only in the HSN summary + exempt bucket, not in the
            // B2B/B2C tax rows or the taxable rate-wise summary.
            if (!hasTax) continue;

            // 🔴 THE LINE THIS SWEEP DRAWS, and it is drawn once here rather than per table: an AGGREGATE cell
            // nets a reversal (the output totals, Table 12, the exempt bucket above, and the rate-wise summary
            // below — all signed); a DOCUMENT-level INVOICE table does not, because a reversal is not an invoice.
            // Vendor-attested, opened and checked by content — help.tallysolutions.com/gstr-1-report-in-tallyprime/
            // ("GSTR-1 Report in TallyPrime", product TallyPrime) publishes the section names "Credit or Debit
            // Notes (Registered) – 9B" and "Credit or Debit Notes (Unregistered) – 9B" verbatim, so a note's home
            // is a 9B section and not 4A/7. (It does NOT publish any "never in 4A/7" prohibition and does not
            // publish a reconciling total row; an earlier revision of this comment asserted the former, and that
            // over-claim is struck.) Before this, the note was emitted as a second POSITIVE B2B invoice row
            // carrying the returned value (measured: 2 rows for 1 invoice).
            //
            // 🔴 AND IT IS NOT SIGNED INTO B2B/B2C INSTEAD. A negative invoice in a document-level INVOICE table
            // is a different wrong document, not a fix. It goes to its own document table — Table 9B — see the
            // unlinked-9B block below, which is what keeps the filed header equal to the sum of the filed detail
            // sections.
            var isInvoiceDocument = sign > 0m;

            var party = voucher.PartyId is Guid pid ? company.FindLedger(pid) : null;
            // W0-15: the place of supply an ISSUED document states — the s.10(1)(ca) ladder RECONCILED to the tax the
            // voucher posted, the same value the printed invoice carries. Reading the raw ladder here let the return
            // name the SUPPLIER's own State on an IGST-bearing voucher whose party State had since been cleared or
            // "corrected" (NIC validation 24 makes that self-refuting) while the reprint of the same voucher printed
            // nothing at all. An undrifted voucher resolves identically ⇒ byte-identical (ER-13).
            var pos = GstReportSupport.IssuedPlaceOfSupply(company, voucher);
            var taxable = GstReportSupport.InvoiceTaxableValue(voucher);

            // Per-(integrated rate) breakdown of THIS invoice's posted tax, so a multi-rate invoice contributes
            // one entry per rate to the rate-wise summary / B2C consolidation (never a blended 0% row).
            var rateGroups = ReadInvoiceRateGroups(voucher);

            // B2B when the party carries a GSTIN (registered); else B2C (DP-8). Both are DOCUMENT-level tables, so
            // only a real invoice reaches them (see isInvoiceDocument above).
            // 🔴 Note the shape: a reversal falls past BOTH invoice branches but is deliberately NOT `continue`d,
            // because the rate-wise summary below is an AGGREGATE and must still net this note down — otherwise it
            // would disagree with the Table 12 and the output totals that already have.
            // 🔴 BOTH branches are gated on isInvoiceDocument, and that is not belt-and-braces. Gating only the
            // B2B arm sends a reversal down the `else` into the B2C consolidation, where it is accumulated
            // POSITIVELY — trading one wrong filed table for another. I wrote exactly that bug while tidying this
            // block and my own tests stayed green, because the fixture's party is registered and nothing asserted
            // B2C was empty. The B2C emptiness assertion added in the owning test is what closes it.
            var isB2B = party?.PartyGst is { } pg && !pg.IsB2C;

            // 🔴 THE UNLINKED §34 NOTE GETS ITS OWN TABLE 9B ROW, WITH A NULL ORIGINAL-INVOICE REFERENCE — and
            // without this the filed return CONTRADICTS ITSELF. Measured on the owning fixture: the header filed
            // total_cgst_paisa 360000 (4500 − 900, correctly net) while b2b carried ONE row of camt_paisa 450000,
            // b2cs was empty and cdnr was empty — ₹900 per head of declared liability with no document behind it,
            // so an upload rebuilt from the detail sections could not reproduce the declared total. The tax is NOT
            // re-added here: the sweep already signed it into totalCgst/Sgst/Igst at the top, and BuildTable9B
            // folds only the LINKED notes it reads off company.CreditDebitNoteLinks. This row is therefore a
            // DISCLOSURE of money already in the totals, which is exactly what makes header == Σ sections hold.
            //
            // 🔴 CdnType.Credit IS NOT A GUESS AND IS NOT A DEAD GUARD. This sweep is the OUTPUT direction, and
            // GstReportSupport.DirectionOf maps only Sales and CreditNote to Output (DebitNote maps to Input), so
            // sign < 0 here means CreditNote and nothing else. A branch for Debit would be unreachable code.
            //
            // 🔴 WHY THE HEADER NETS *AND* THE DOCUMENT IS STILL DECLARED — STATUTE, NOT A HOUSE CHOICE, and this
            // is what settles the "is the header 360000 or 450000?" question rather than leaving it to taste.
            // CGST Act §34(2) (opened by content at taxinformation.cbic.gov.in, "Section 34. Credit and debit
            // notes."): the issuer "shall declare the details of such credit note in the return … and the tax
            // liability shall be adjusted in such manner as may be prescribed". BOTH limbs bind at once, so a
            // conforming return must carry the note AS A DECLARED DOCUMENT *and* show the liability ADJUSTED.
            // Grossing the header back to 450000 would defeat the second limb; dropping the 9B row (what main
            // does) defeats the first. 360000 with the row below is the only reading that satisfies both.
            //
            // 🔴 WHAT IS VENDOR-ATTESTED AND WHAT IS OURS. Attested (page opened by content, above): a §34 note
            // belongs in a "Credit or Debit Notes (Registered)/(Unregistered) – 9B" section. OURS, a documented
            // divergence: the vendor page is SILENT on a note whose original-invoice details are absent — it
            // states no treatment for that case at all — so emitting it as a 9B row with a NULL original-invoice
            // reference, and leaving ReasonCode EMPTY rather than inventing a §34 reason the user never declared,
            // is this project's own choice. The alternative (omit the document and keep its money in the header)
            // is the defect this replaces. Is9BTarget follows the party's registration, which is the same
            // Registered/Unregistered split the two published section names draw.
            if (!isInvoiceDocument)
                unlinked9B.Add(new Gstr1Table9BRow(
                    CdnType.Credit, null, null, null, voucher.Date, pos,
                    new Money(sign * taxable.Amount), new Money(sign * invoice.Cgst),
                    new Money(sign * invoice.Sgst), new Money(sign * invoice.Igst), string.Empty, isB2B));

            if (isInvoiceDocument && isB2B)
            {
                // One B2B invoice row carrying the whole-invoice taxable value and both heads' total tax. Phase 9
                // slice 4a: additively annotate it with the IRN of a Generated e-invoice for the voucher (null when the
                // company does not e-invoice ⇒ byte-identical, ER-13). A stale record whose voucher changed simply no
                // longer matches (surfaced in EInvoiceReconciliation, not auto-cleared).
                var irn = company.FindEInvoiceRecordForVoucher(voucher.Id) is { Status: EInvoiceStatus.Generated } eiv
                    ? eiv.Irn
                    : null;
                b2b.Add(new Gstr1B2BRow(
                    party!.Name, party.PartyGst!.Gstin, EInvoiceService.DocumentNumberOf(company, voucher), voucher.Date, pos,
                    taxable, new Money(invoice.Cgst), new Money(invoice.Sgst), new Money(invoice.Igst))
                    { Irn = irn, RawNumber = voucher.Number });
            }
            else if (isInvoiceDocument)
            {
                foreach (var (rate, g) in rateGroups)
                {
                    var h = b2cAcc.TryGetValue(rate, out var cur) ? cur : new HeadAmounts();
                    h.Taxable += g.Taxable; h.Cgst += g.Cgst; h.Sgst += g.Sgst; h.Igst += g.Igst;
                    b2cAcc[rate] = h;
                }
            }

            // Rate-wise summary (taxable outward, by integrated rate) — one contribution per rate group, SIGNED so
            // a sales return nets its rate down. This is an aggregate, so it follows the aggregate rule: leaving it
            // unsigned while Table 12 and the output totals net would make the return internally inconsistent.
            foreach (var (rate, g) in rateGroups)
            {
                var (t, x) = rateAcc.TryGetValue(rate, out var cur) ? cur : (0m, 0m);
                rateAcc[rate] = (t + sign * g.Taxable, x + sign * (g.Cgst + g.Sgst + g.Igst));
            }
        }

        var b2cRows = b2cAcc
            .OrderBy(kv => kv.Key)
            .Select(kv => new Gstr1B2CRow(kv.Key, new Money(kv.Value.Taxable),
                new Money(kv.Value.Cgst), new Money(kv.Value.Sgst), new Money(kv.Value.Igst)))
            .ToList();

        var rateRows = rateAcc
            .OrderBy(kv => kv.Key)
            .Select(kv => new Gstr1RateRow(kv.Key, new Money(kv.Value.Taxable), new Money(kv.Value.Tax)))
            .ToList();

        var hsnRows = hsnAcc.Values
            .OrderBy(h => h.HsnSac, StringComparer.Ordinal)
            .Select(h => new Gstr1HsnRow(
                h.HsnSac, h.Description,
                h.MixedUnits ? h.BaseUqc : h.Uqc,
                h.MixedUnits ? h.BaseQuantity : h.Quantity,
                new Money(h.Taxable), new Money(h.Cgst), new Money(h.Sgst), new Money(h.Igst), new Money(h.Cess),
                // The RAW accumulators, deliberately NOT collapsed through MixedUnits: a downstream aggregator
                // must be able to degrade an ALREADY-degraded row further, which it can only do if the base
                // measure survives the projection intact.
                h.BaseQuantity, h.BaseUqc,
                // A degraded row declares the BASE unit, so its declared identity IS the base identity — which
                // keeps the row self-consistent and makes a second degrade the identity operation.
                h.MixedUnits ? h.BaseUnitId : h.DeclaredUnitId, h.BaseUnitId, h.MixedBases))
            .ToList();

        // Phase 9 slice 2b: Table 9B (§34 CDN) — signed by note type — is folded into the output totals; 11A/11B are
        // projected off the advance records. All skipped byte-identically when the collections are empty (ER-13).
        var table9B = BuildTable9B(
            company, from, to, registrationId, unlinked9B, ref totalCgst, ref totalSgst, ref totalIgst);
        var (table11A, table11B) = BuildAdvanceTables(company, from, to, registrationId);

        return new Gstr1(from, to, b2b, b2cRows, rateRows, hsnRows,
            new Money(exempt), new Money(totalCgst), new Money(totalSgst), new Money(totalIgst))
        {
            Rcm4BOutwardValue = ComputeRcm4BOutwardValue(company, from, to, registrationId),
            Table9B = table9B,
            Table11A = table11A,
            Table11B = table11B,
        };
    }

    /// <summary>
    /// The e-invoice reconciliation view (Phase 9 slice 4a; RQ-18): counts of the covered outward B2B/export/SEZ/RCM/CDN
    /// documents in <c>[from, to]</c> versus how many carry a <b>Generated</b> IRN versus <b>Pending/Failed/Cancelled</b>,
    /// plus <b>mismatched</b> — a Generated record whose voucher's current document number no longer matches (an edited
    /// voucher; surfaced, not auto-cleared). Advisory; recomputed each call (no persistence). The "GSTR-1 reconciles to
    /// IRN-tagged docs" gate: <see cref="Covered"/> == <see cref="Tagged"/> when every covered document has been IRN-tagged.
    ///
    /// <para>🔴 <b><see cref="Mismatched"/> IS A DOCUMENT-NUMBER COMPARISON, NOT AN AMENDMENT DETECTOR — do not read it
    /// as one.</b> The only content check below is <c>record.DocumentNumberUpper</c> versus
    /// <c>EInvoiceService.DocumentNumberOf</c>. The IRP signed a whole document, and the record's <c>SignedJson</c> is
    /// never compared to anything, so an <b>amount-only</b> amendment leaves this counter reading a clean
    /// <c>Mismatched = 0</c> while the GSTR-1 B2B row files the NEW taxable value under the OLD IRN. Measured: dividing
    /// an IRN-tagged invoice by ten moved the B2B taxable value 60,000 to 6,000 with <c>Tagged = 1, Mismatched = 0</c>
    /// throughout. That is worse than having no detector, because it is the thing a reviewer points at to say the case
    /// is covered. The alteration path warns instead — <c>VoucherAlterationWarningCode.StatutoryRecordDiverged</c>,
    /// raised by <c>LedgerService.Replace</c> — and the <c>EInvoiceStatus.Generated</c> REFUSAL is phase-10-11 §6.6's
    /// S5b work. Widening this limb to compare a value the IRN was signed over is the real fix and is NOT done here.</para>
    /// </summary>
    public sealed record EInvoiceReconciliationView(
        int Covered, int Tagged, int Pending, int Failed, int Cancelled, int Mismatched);

    /// <summary>Builds the e-invoice reconciliation view over the covered outward documents in <c>[from, to]</c>.</summary>
    public static EInvoiceReconciliationView EInvoiceReconciliation(
        Company company, DateOnly from, DateOnly to, Guid? registrationId = null)
    {
        var svc = new EInvoiceService(company);
        int covered = 0, tagged = 0, pending = 0, failed = 0, cancelled = 0, mismatched = 0;

        foreach (var (voucher, _) in GstReportSupport.PostedDirectionalVouchers(company, from, to, GstTaxDirection.Output, registrationId))
        {
            if (svc.CoverageOf(voucher) != EInvoiceCoverage.Covered) continue;
            covered++;
            var record = company.FindEInvoiceRecordForVoucher(voucher.Id);
            switch (record?.Status)
            {
                case EInvoiceStatus.Generated:
                    tagged++;
                    if (!string.Equals(record.DocumentNumberUpper, EInvoiceService.DocumentNumberOf(company, voucher), StringComparison.Ordinal))
                        mismatched++;
                    break;
                case EInvoiceStatus.Pending: pending++; break;
                case EInvoiceStatus.Failed: failed++; break;
                case EInvoiceStatus.Cancelled: cancelled++; break;
            }
        }

        return new EInvoiceReconciliationView(covered, tagged, pending, failed, cancelled, mismatched);
    }

    /// <summary>
    /// Builds GSTR-1 Table 9B (Phase 9 slice 2b; RQ-24; §34): one row per §34 credit/debit note posted in the window,
    /// read off the note's posted Output-tax lines (never recomputed), joined to its <see cref="GstCreditDebitNoteLink"/>
    /// for the original-invoice reference + reason. Each row is <b>signed by note type</b> — a credit note is negative
    /// (it reduces output), a debit note positive — and that signed tax is folded into the return's output totals so
    /// GSTR-1 nets correctly. A company with no §34 note yields an empty table and leaves the totals untouched (ER-13).
    ///
    /// <para>🔴 <paramref name="unlinked"/> carries the rows the sweep in <see cref="Build"/> already built for the
    /// notes the user did NOT formalise with a <see cref="GstCreditDebitNoteLink"/>. They are merged here so BOTH
    /// kinds leave through ONE comparator — otherwise the filed section's order would depend on which kind a book
    /// happened to hold. <b>Their tax is deliberately NOT folded into the totals here</b>: the sweep signed it in
    /// already, and folding it twice would halve the filed liability. Only the LINKED rows this method reads off
    /// <c>company.CreditDebitNoteLinks</c> are folded. An empty <paramref name="unlinked"/> leaves the result
    /// byte-identical (ER-13).</para>
    ///
    /// <para>🔴 <b>T2-119 (ii) — THE LINKED ROWS ARE SCOPED TO <paramref name="registrationId"/>, AND BEFORE THIS
    /// THEY WERE NOT.</b> Every other leg of <see cref="Build"/> reaches its vouchers through
    /// <c>GstReportSupport.PostedDirectionalVouchers</c>, which applies the registration filter; this method walks
    /// <c>company.CreditDebitNoteLinks</c> directly and so has to apply it itself — exactly as
    /// <c>Gstr3b.ReadCdn</c> already did for the same collection. <b>Unscoped, the scoping was inconsistent INSIDE
    /// ONE TABLE:</b> the <paramref name="unlinked"/> rows arrive from the scoped sweep and were right, while the
    /// linked rows were folded wholesale — so a second registration's §34 note appeared in this registration's
    /// Table 9B <i>and</i> its tax moved this registration's header through the <c>ref</c> totals. Measured on
    /// <c>GstOutwardRegistrationScopeAndSignTests</c>: the Karnataka registration filed <c>TotalIgst</c>
    /// <b>−₹1,800.00</b> against a true ₹0.00, on a credit note issued under the Tamil Nadu GSTIN — a reduction
    /// Karnataka never made, while Tamil Nadu declared the same note in its own return. One note, filed twice,
    /// against two GSTINs. Same shape as <c>T1-64</c>/<c>T1-72</c>, where <c>Gstr3b.ReadReversals</c> was the one
    /// unscoped leg of its own <c>Build</c>.</para>
    ///
    /// <para><b>Vendor-attested (R7, opened by content).</b> TallyPrime's GSTR-1 report: "<i>Press F3 (Company/Tax
    /// Registration) and select the registration for which you want to view the report</i>"
    /// (<c>help.tallysolutions.com/gstr-1-report-in-tallyprime/</c>); and for the filed artefact, "<i>If you have
    /// multiple registrations, select the required GST Registration</i>"
    /// (<c>help.tallysolutions.com/upload-gstr-1/</c>). <b>The statute binds the other half:</b> CGST Act §34(2)
    /// (<c>taxinformation.cbic.gov.in</c>, opened by content) makes the note's declaration the duty of the person
    /// "<i>who issues</i>" it — so it is owed by its OWN registration's return, which is why the filter must scope
    /// it out of this one without dropping it from that one.</para>
    /// </summary>
    /// <param name="registrationId">The registration the return is being filed for (census 6.23). <c>null</c> ⇒ a
    /// single-registration company, where <c>RegistrationOf</c> normalises every voucher to
    /// <c>GstRegistration.PrimaryId</c> and the filter is a no-op — every pre-v61 book is byte-identical (ER-13).</param>
    private static IReadOnlyList<Gstr1Table9BRow> BuildTable9B(
        Company company, DateOnly from, DateOnly to, Guid? registrationId, List<Gstr1Table9BRow> unlinked,
        ref decimal totalCgst, ref decimal totalSgst, ref decimal totalIgst)
    {
        if (company.CreditDebitNoteLinks.Count == 0 && unlinked.Count == 0) return [];

        // Normalise the requested scope exactly as PostedDirectionalVouchers does, so naming the primary explicitly
        // and leaving it null cannot disagree about which rows belong here.
        var scope = registrationId is { } req ? (Guid?)(req == Guid.Empty ? GstRegistration.PrimaryId : req) : null;

        var rows = new List<Gstr1Table9BRow>(unlinked);
        foreach (var link in company.CreditDebitNoteLinks)
        {
            var v = company.FindVoucher(link.CdnVoucherId);
            if (v is null || v.Date < from) continue;
            var type = company.FindVoucherType(v.TypeId);
            if (type is null || !LedgerBalances.CountsAsOf(v, to, type.BaseType)) continue;
            // 🔴 T2-119 (ii): the §34 notes are walked off the LINK collection, not the voucher funnel, so the
            // registration filter is applied here too — a credit note issued under the Tamil Nadu GSTIN must not
            // appear in, or net down the header of, the Karnataka return.
            if (scope is { } s && GstReportSupport.RegistrationOf(v) != s) continue;

            var heads = ReadInvoiceHeads(v);              // positive magnitudes
            var taxable = GstReportSupport.InvoiceTaxableValue(v).Amount;
            var sign = link.CdnType == CdnType.Credit ? -1m : 1m;

            totalCgst += sign * heads.Cgst;
            totalSgst += sign * heads.Sgst;
            totalIgst += sign * heads.Igst;

            rows.Add(new Gstr1Table9BRow(
                link.CdnType, link.OriginalInvoiceVoucherId, link.OriginalInvoiceNumber, link.OriginalInvoiceDate,
                // W0-15: the same reconciled place of supply the invoice states (see the Table 4/7 note above).
                v.Date, GstReportSupport.IssuedPlaceOfSupply(company, v),
                new Money(sign * taxable), new Money(sign * heads.Cgst), new Money(sign * heads.Sgst),
                new Money(sign * heads.Igst), link.ReasonCode, link.Is9BTarget));
        }
        // Deterministic order: by note date, then original-invoice reference, then id-stable link order.
        return rows
            .OrderBy(r => r.NoteDate)
            .ThenBy(r => r.OriginalInvoiceNumber, StringComparer.Ordinal)
            .ThenBy(r => r.NoteType)
            .ToList();
    }

    /// <summary>
    /// Builds GSTR-1 Table 11A (advances received) + 11B (advances adjusted) off the <see cref="GstAdvanceReceipt"/>
    /// records (Phase 9 slice 2b; RQ-25) — the source of truth for the advance tax. 11A groups the service advances whose
    /// <b>receipt</b> voucher falls in the window (goods advances are de-taxed → excluded); 11B groups those whose
    /// <b>adjustment</b> (invoice) <b>or Rule-51 refund</b> voucher falls in the window (a refund reverses the advance
    /// exactly like an adjustment). Each group's CGST/SGST/IGST split is reproduced from the record's net advance + rate +
    /// POS via the same total-then-split rule (paisa-exact). Empty when unused (ER-13).
    ///
    /// <para>🔴 <b>A11 REVIEW (census 6.23) — THE ADVANCE TABLES ARE SCOPED TOO, AND THEY WERE THE SECOND
    /// UNSCOPED LEG OF THIS BUILD.</b> Like <see cref="BuildTable9B"/> this method walks a record collection
    /// (<c>company.AdvanceReceipts</c>) instead of <c>GstReportSupport.PostedDirectionalVouchers</c>, so it has to
    /// apply the registration filter itself. Unscoped, a Tamil Nadu advance appeared in the Karnataka return:
    /// measured on <c>Gstr1AdvanceTableRegistrationScopeTests</c>, Karnataka's Table 11A carried the branch's
    /// ₹1,00,000 advance and ₹18,000 of IGST advance tax against a true empty table — and from this wave that
    /// figure reaches the EMITTED <c>at</c>/<c>atadj</c> payload, which previously refused to build at all on a
    /// multi-registration book.</para>
    ///
    /// <para><b>The record is attributed through its RECEIPT voucher</b> — the registration that collected the
    /// advance is the one that owes the tax on it (CGST Act §13(2); the receipt voucher of Rule 50 is issued by
    /// that registration), so both 11A and 11B follow the receipt rather than being split between registrations.
    /// Under an explicit scope a record whose receipt voucher cannot be found is unattributable and is skipped.
    /// <c>null</c> ⇒ no filter, so every single-registration book is byte-identical (ER-13).</para>
    ///
    /// <para><b>Vendor-attested (R7, opened by content):</b> "<i>Press F3 (Company/Tax Registration) and select the
    /// registration for which you want to view the report</i>" — <c>help.tallysolutions.com/gstr-1-report-in-tallyprime/</c>.</para>
    /// </summary>
    private static (IReadOnlyList<Gstr1AdvanceRow> Table11A, IReadOnlyList<Gstr1AdvanceAdjustedRow> Table11B)
        BuildAdvanceTables(Company company, DateOnly from, DateOnly to, Guid? registrationId)
    {
        if (company.AdvanceReceipts.Count == 0) return ([], []);

        // Normalised exactly as PostedDirectionalVouchers and BuildTable9B do it.
        var scope = registrationId is { } req ? (Guid?)(req == Guid.Empty ? GstRegistration.PrimaryId : req) : null;

        var received = new Dictionary<(int Rate, bool Inter), (decimal Adv, decimal Cgst, decimal Sgst, decimal Igst)>();
        var adjusted = new Dictionary<(int Rate, bool Inter), (decimal Adv, decimal Cgst, decimal Sgst, decimal Igst)>();

        bool InWindow(Guid? voucherId)
        {
            if (voucherId is not { } vid || company.FindVoucher(vid) is not { } v) return false;
            if (v.Date < from) return false;
            var type = company.FindVoucherType(v.TypeId);
            return type is not null && LedgerBalances.CountsAsOf(v, to, type.BaseType);
        }

        void Accumulate(
            Dictionary<(int, bool), (decimal, decimal, decimal, decimal)> acc, GstAdvanceReceipt a)
        {
            var split = GstService.ComputeLineTax(a.AdvanceAmount, a.RateBasisPoints, a.InterState);
            var key = (a.RateBasisPoints, a.InterState);
            var (adv, cg, sg, ig) = acc.TryGetValue(key, out var cur) ? cur : (0m, 0m, 0m, 0m);
            acc[key] = (adv + a.AdvanceAmount.Amount, cg + split.Cgst.Amount, sg + split.Sgst.Amount, ig + split.Igst.Amount);
        }

        foreach (var a in company.AdvanceReceipts)
        {
            if (!a.IsService || a.AdvanceTax.Amount == 0m) continue; // goods advances are de-taxed — no 11A/11B
            // 🔴 A11 review (census 6.23): the advance belongs to the registration whose receipt voucher collected
            // it — another registration's advance must not appear in, or add tax to, this return's 11A/11B.
            if (scope is { } s
                && (company.FindVoucher(a.ReceiptVoucherId) is not { } rv || GstReportSupport.RegistrationOf(rv) != s))
                continue;
            if (InWindow(a.ReceiptVoucherId)) Accumulate(received, a);
            if (InWindow(a.AdjustedAgainstInvoiceVoucherId)) Accumulate(adjusted, a);
            // A Rule-51 REFUND reverses the advance: net it back out in the refund period exactly like an adjustment
            // (11B), so a refunded advance's 11A − 11B collapses to 0 and reconciles with the zero ledger balance
            // (finding #1). Without this the 11A liability stays reported forever though the books say 0.
            if (InWindow(a.RefundVoucherId)) Accumulate(adjusted, a);
        }

        var t11a = received
            .OrderBy(kv => kv.Key.Rate).ThenBy(kv => kv.Key.Inter)
            .Select(kv => new Gstr1AdvanceRow(kv.Key.Rate, kv.Key.Inter,
                new Money(kv.Value.Adv), new Money(kv.Value.Cgst), new Money(kv.Value.Sgst), new Money(kv.Value.Igst)))
            .ToList();

        var t11b = adjusted
            .OrderBy(kv => kv.Key.Rate).ThenBy(kv => kv.Key.Inter)
            .Select(kv => new Gstr1AdvanceAdjustedRow(kv.Key.Rate, kv.Key.Inter,
                new Money(kv.Value.Adv), new Money(kv.Value.Cgst), new Money(kv.Value.Sgst), new Money(kv.Value.Igst)))
            .ToList();

        return (t11a, t11b);
    }

    /// <summary>
    /// The Table-4B outward-RCM-supply value (Phase 9 slice 2; RQ-7) — Σ the outward supply value of vouchers whose
    /// sales/expense ledger carries an <b>outward</b> reverse-charge flag (the recipient pays the tax, so the invoice bears
    /// none). Reads posted amounts only. A company with no such supply yields <c>Money.Zero</c> (byte-identical, ER-13).
    /// </summary>
    private static Money ComputeRcm4BOutwardValue(
        Company company, DateOnly from, DateOnly to, Guid? registrationId = null)
    {
        if (!company.GstEnabled) return Money.Zero;
        var total = 0m;
        foreach (var (voucher, type) in GstReportSupport.PostedDirectionalVouchers(company, from, to, GstTaxDirection.Output, registrationId))
        {
            // A Credit Note against an outward RCM supply REDUCES the 4B value (it nets the original supply down),
            // mirroring how an outward return nets down a rate row; a Sales voucher adds. Signing by base type (rather
            // than treating every posted line as additive) keeps 4B from being inflated by a credit note (Phase 9 S2; RQ-7).
            // Routed through the one helper, for the reason in GstReportSupport.SignOf: this expression is right here
            // and was copied onto inward sweeps where it silently did nothing.
            var sign = (decimal)GstReportSupport.SignOf(company, voucher, type.BaseType);
            foreach (var line in voucher.Lines)
                if (company.FindLedger(line.LedgerId)?.SalesPurchaseGst is { ReverseChargeApplicable: true })
                    total += sign * line.Amount.Amount;
        }
        return new Money(total);
    }

    /// <summary>Reads a voucher's posted forward tax by head off its <see cref="GstLineTax"/> tax lines. Reverse-charge
    /// lines are excluded (finding #7): they are their own 3.1(d)/4A buckets, so folding an identical head set here keeps
    /// Table 9B aligned with GSTR-3B <c>ReadCdn</c> (which already skips them) and consistent with
    /// <see cref="GstReportSupport.InvoiceTaxableValue"/> (which excludes them too). An ordinary outward invoice carries no
    /// reverse-charge forward-tax line, so this is a defensive alignment — byte-identical for the reachable paths.</summary>
    private static (decimal Cgst, decimal Sgst, decimal Igst) ReadInvoiceHeads(Voucher voucher)
    {
        var cgst = 0m; var sgst = 0m; var igst = 0m;
        foreach (var line in voucher.Lines)
        {
            if (line.Gst is not { } g || g.IsReverseCharge) continue;
            switch (g.TaxHead)
            {
                case GstTaxHead.Central: cgst += line.Amount.Amount; break;
                case GstTaxHead.State: sgst += line.Amount.Amount; break;
                case GstTaxHead.Integrated: igst += line.Amount.Amount; break;
            }
        }
        return (cgst, sgst, igst);
    }

    /// <summary>
    /// The per-(integrated rate) breakdown of an outward invoice's posted tax, read off its tax lines (never
    /// recomputed). One entry per distinct integrated rate — the group's per-head tax (CGST/SGST/IGST) and its
    /// taxable value (the max <see cref="GstLineTax.TaxableValue"/> across the group's lines, which dedups the
    /// equal-valued CGST and SGST legs of an intra group). A multi-rate invoice yields multiple entries so the
    /// rate-wise summary / B2C consolidation attribute each rate correctly; an all-exempt sale yields none.
    /// Ordered by rate for determinism.
    /// </summary>
    private static IReadOnlyList<(int Rate, HeadAmounts Amounts)> ReadInvoiceRateGroups(Voucher voucher)
    {
        var byRate = new Dictionary<int, HeadAmounts>();
        foreach (var line in voucher.Lines)
        {
            if (line.Gst is not { } g) continue;
            // Phase 9 slice 1: a Compensation-Cess line is ring-fenced (own column/total, ER-2), NOT a CGST/SGST/IGST
            // rate group. Its (doubled) cess-rate key would otherwise inject a phantom rate row into the rate-wise /
            // B2C consolidation and duplicate the group's taxable value. Skip it here; reports read cess separately.
            if (g.TaxHead == GstTaxHead.Cess) continue;
            var rate = GstReportSupport.IntegratedRateOf(g, line.Amount);
            if (!byRate.TryGetValue(rate, out var acc)) byRate[rate] = acc = new HeadAmounts();
            switch (g.TaxHead)
            {
                case GstTaxHead.Central: acc.Cgst += line.Amount.Amount; break;
                case GstTaxHead.State: acc.Sgst += line.Amount.Amount; break;
                case GstTaxHead.Integrated: acc.Igst += line.Amount.Amount; break;
            }
            // The group taxable is the same on every line of the group; take the max so the intra CGST+SGST legs
            // (equal taxable) are not double-counted.
            if (g.TaxableValue.Amount > acc.Taxable) acc.Taxable = g.TaxableValue.Amount;
        }
        return byRate.OrderBy(kv => kv.Key).Select(kv => (kv.Key, kv.Value)).ToList();
    }

    /// <summary>
    /// Attributes an outward invoice's posted tax to its item-invoice stock lines, grouping by HSN/SAC. Tax is
    /// attributed <b>per rate group</b>: each stock line's tax is a value-share of ITS OWN integrated-rate
    /// group's posted tax (not the blended invoice total), so a multi-rate invoice shows each HSN row its true
    /// rate's tax (e.g. Widget @18% ⇒ 180, Gadget @5% ⇒ 25 — not a value-share blend of 205). Within a rate
    /// group the value-share is paisa-exact (the group's last line absorbs the rounding remainder), so Σ line tax
    /// == the group's posted tax and Σ over all groups == the invoice tax. An exempt/nil supply (zero invoice
    /// tax) adds only its value to the exempt bucket and its HSN row. Reads only posted amounts — it never
    /// recomputes tax from a rate; the per-line RATE is read from the item's GST master purely to bucket the line
    /// into the matching posted rate group.
    ///
    /// <para>🔴 <b>T1-59 — a NON-TAXABLE stock line is NEVER a member of a posted rate group</b>, and that holds on a
    /// MIXED invoice, not only on a wholly-exempt one. The whole-invoice exempt branch below fires only when the
    /// invoice posted no tax at all; an invoice carrying one taxable and one exempt line falls through it, and the
    /// <c>singleRate</c> collapse then forced the exempt line into the one posted group and apportioned real tax
    /// across it by value. Measured: Widget ₹50,000 @ 18% (HSN 847130) + EXEMPT Fresh Milk ₹20,000 (HSN 040110),
    /// posted CGST ₹4,500 / SGST ₹4,500, filed <c>040110 taxable=20,000 cgst=1,285.71 sgst=1,285.71</c> and
    /// <c>847130 cgst=3,214.29 sgst=3,214.29</c> with an EMPTY exempt bucket — three misstatements on one filed
    /// return: an exempt supply declared as taxed, the taxed HSN understated by ₹1,285.71 per head, and ₹20,000 of
    /// exempt turnover vanished. Identical in kind to the two defects <see cref="AccumulateServiceHsn"/> already
    /// fixed for ledger legs; the discriminator the GOODS side lacked is
    /// <see cref="GstReportSupport.IsNonTaxableStockLine"/>. Every non-taxable line is now split out BEFORE the rate
    /// machinery, into the exempt bucket + a zero-tax HSN row.</para>
    /// </summary>
    /// <param name="sign">🔴 T2-59 — the outward sign of the carrying document: <c>+1</c> for a Sales invoice,
    /// <c>-1</c> for a Credit Note (a sales return), from <see cref="VoucherEffects.IsReturnNote"/>. Applied to the
    /// value, each tax head, the cess AND the filed quantity, so a return nets its Table-12 row down instead of
    /// adding to it. Every amount here is a positive magnitude off <c>EntryLine.Amount</c> / the inventory line —
    /// the posted SIDE is never read — which is precisely why the sign has to be carried in.</param>
    private static void AccumulateHsn(
        Company company, Voucher voucher, (decimal Cgst, decimal Sgst, decimal Igst) invoice,
        Dictionary<string, HsnAcc> hsnAcc, ref decimal exempt, decimal sign)
    {
        var invoiceTax = invoice.Cgst == 0m && invoice.Sgst == 0m && invoice.Igst == 0m;
        if (invoiceTax)
        {
            // An all-exempt/nil outward supply: record its value against exempt + its HSN row (zero tax).
            foreach (var il in voucher.InventoryLines)
            {
                exempt += sign * il.Value.Amount;
                // An exempt/nil supply bears no cess either — ResolveCess short-circuits on a non-taxable block
                // ("cess never over-collects on an exempt supply"), so a zero here is the engine's own answer.
                AddHsnRow(company, il, il.Value.Amount, 0m, 0m, 0m, 0m, hsnAcc, sign);
            }

            // Accounting (service) invoice with no stock lines: attribute the service-income LEDGER legs to the exempt
            // bucket + their SAC row — but ONLY a genuinely exempt/nil service ledger (SalesPurchaseGst IsTaxable:false).
            // A plain As-Voucher TAXABLE sale (an 18% ledger, but the plain grid posts NO tax legs) ALSO reaches this
            // no-tax branch; attributing it here would file its value in the Table-12 EXEMPT column — a positive
            // misstatement. The IsTaxable:false gate is the discriminator the flag-less structural signal otherwise
            // lacks. Gated on InventoryLines.Count==0 so an existing exempt item invoice is never double-counted.
            if (voucher.InventoryLines.Count == 0)
                // T1-79 sweep: Side read and DELIBERATELY not applied here. The exempt-turnover sign on a mixed-side
                // ledger document is a GSTR-1 question of its own (reported, out of this slice's scope); narrowing it
                // here without the Table-12 fixtures to pin it would be an unmeasured change to a filed return.
                foreach (var (ledger, value, _) in ServiceLegs(company, voucher))
                    if (IsNonTaxableServiceLedger(ledger))
                    {
                        exempt += sign * value;
                        AddServiceHsnRow(ledger, value, 0m, 0m, 0m, hsnAcc, sign);
                    }
            return;
        }

        if (voucher.InventoryLines.Count == 0)
        {
            // Accounting (service) invoice: attribute the posted tax to the service-income LEDGER legs, grouped by
            // SAC — the service mirror of the item-line attribution below. ONLY when there are no stock lines, so an
            // existing item invoice's HSN summary is never double-counted (its stock lines carry the tax below). A
            // plain As-Voucher sale with posted tax legs (unusual) still finds no SAC-bearing service leg here and is
            // simply not attributed to HSN, exactly as before.
            AccumulateServiceHsn(company, voucher, hsnAcc, ref exempt, sign);
            return;
        }

        // The posted per-(integrated rate) tax groups for this invoice (from its tax lines).
        var rateGroups = ReadInvoiceRateGroups(voucher);

        // Per-VOUCHER, so it is resolved once rather than per line (see BucketingValueLedger). 🔴 T1-59: this is now
        // resolved UNCONDITIONALLY, not only on the multi-rate path — the per-line taxability discriminator below
        // needs it on every invoice, and it is one ancestry climb per voucher.
        var valueLedger = GstReportSupport.BucketingValueLedger(company, voucher);

        // 🔴 T1-59 — SPLIT FIRST: a NON-TAXABLE stock line is never a member of a posted rate group. It posted no tax
        // and contributed no taxable value to the tax the screen computed (ComputeItemInvoiceGst skips it), so it must
        // not receive a share of that tax back here. This is the identical split AccumulateServiceHsn already makes
        // for ledger legs, on the GOODS side where it was missing — see GstReportSupport.IsNonTaxableStockLine for
        // the measured money. Every non-taxable line goes to the EXEMPT bucket + a zero-tax HSN row, mirroring the
        // whole-invoice exempt branch above, and the posted tax is apportioned across the TAXABLE lines only.
        var taxableLines = new List<VoucherInventoryLine>();
        foreach (var il in voucher.InventoryLines)
        {
            if (GstReportSupport.IsNonTaxableStockLine(company, voucher, valueLedger, il))
            {
                exempt += sign * il.Value.Amount;
                AddHsnRow(company, il, il.Value.Amount, 0m, 0m, 0m, 0m, hsnAcc, sign);
                continue;
            }
            taxableLines.Add(il);
        }

        // Bucket the TAXABLE stock lines by their integrated rate so each line's tax comes from its OWN rate group.
        // The single-rate collapse is now scoped to those lines: when the invoice posted exactly one rate group, every
        // taxable line belongs to it regardless of where its own rate resolved (a rate-history override, a
        // company-level default). A non-taxable line can never reach this line.
        var singleRate = rateGroups.Count == 1 ? rateGroups[0].Rate : (int?)null;
        var linesByRate = new Dictionary<int, List<VoucherInventoryLine>>();
        foreach (var il in taxableLines)
        {
            var rate = singleRate ?? LineIntegratedRate(company, voucher, valueLedger, il);
            if (!linesByRate.TryGetValue(rate, out var list)) linesByRate[rate] = list = new List<VoucherInventoryLine>();
            list.Add(il);
        }

        // 🔴 Table-12 CESS. The posted Cess legs are read SEPARATELY from the rate groups and attributed here —
        // ReadInvoiceRateGroups still skips every Cess line (the `GstTaxHead.Cess` continue in that method — the line
        // number drifts, so it is named rather than cited) and that skip stays load-bearing: a cess leg's
        // rate key is derived from the CESS amount, so admitting it there would invent a phantom rate row and
        // double-count the group's taxable value. Reading cess on its own keeps both facts true at once.
        var postedCess = ReadPostedCessByRate(voucher);
        // Resolved ONCE per voucher and only when this voucher actually posted cess, so a book with no cess at all
        // does exactly the work it did before.
        // valueLedger is now resolved unconditionally above (T1-59), so there is never a second ancestry climb here.
        var cessLedger = postedCess.Count == 0 ? null : valueLedger;
        var cessResolver = postedCess.Count == 0 ? null : new Services.GstService(company);

        foreach (var (rate, group) in rateGroups)
        {
            if (!linesByRate.TryGetValue(rate, out var groupLines) || groupLines.Count == 0)
                continue; // no matched stock line for this rate group (defensive; e.g. as-voucher-only tax)

            var groupValue = groupLines.Sum(l => l.Value.Amount);
            if (groupValue == 0m) continue;

            var cessPerLine = AttributeGroupCess(
                company, voucher, groupLines, groupValue,
                postedCess.TryGetValue(rate, out var posted) ? posted : 0m, cessResolver, cessLedger);

            var runCgst = 0m; var runSgst = 0m; var runIgst = 0m;
            for (var i = 0; i < groupLines.Count; i++)
            {
                var il = groupLines[i];
                var value = il.Value.Amount;
                decimal cgst, sgst, igst;
                if (i == groupLines.Count - 1)
                {
                    // The group's last line absorbs the remainder so Σ line tax == the group's posted tax exactly.
                    cgst = group.Cgst - runCgst;
                    sgst = group.Sgst - runSgst;
                    igst = group.Igst - runIgst;
                }
                else
                {
                    cgst = Apportion(group.Cgst, value, groupValue);
                    sgst = Apportion(group.Sgst, value, groupValue);
                    igst = Apportion(group.Igst, value, groupValue);
                    runCgst += cgst; runSgst += sgst; runIgst += igst;
                }
                AddHsnRow(company, il, value, cgst, sgst, igst, cessPerLine[i], hsnAcc, sign);
            }
        }
    }

    /// <summary>
    /// The <b>posted</b> Compensation-Cess of a voucher, keyed by the integrated GST rate of the group it was posted
    /// against — never recomputed from a rate.
    ///
    /// <para><b>Why adjacency, and why it is not a guess.</b> <c>GstService.ComputeInvoiceTax</c> walks the rate
    /// groups in order and, for each, emits that group's CGST/SGST or IGST head(s) and then
    /// <c>AddHead(GstTaxHead.Cess, cessRoundedByRate[bp], …)</c> — so a Cess leg always FOLLOWS the heads of the
    /// group it belongs to. The leg cannot be keyed by its own rate (that is the <c>ReadInvoiceRateGroups</c> cess-skip
    /// problem — named, not cited by line, because the line number has already gone stale twice), and it cannot
    /// be keyed by its <c>TaxableValue</c> either, because the engine stamps it with the WHOLE group's taxable — not
    /// the cess-bearing subset's. Position is the only surviving link, and it is the same link
    /// <c>EWayBillJson.ReadRateGroups</c> already ships on. A leg seen before any head (defensive; no such ordering
    /// exists today) is held and attached to the first head that follows.</para>
    /// </summary>
    private static Dictionary<int, decimal> ReadPostedCessByRate(Voucher voucher)
    {
        var byRate = new Dictionary<int, decimal>();
        int? lastRate = null;
        var pending = 0m;

        foreach (var line in voucher.Lines)
        {
            if (line.Gst is not { } g) continue;

            if (g.TaxHead == GstTaxHead.Cess)
            {
                if (lastRate is int key) Add(key, line.Amount.Amount);
                else pending += line.Amount.Amount;
                continue;
            }

            if (g.TaxHead is not (GstTaxHead.Central or GstTaxHead.State or GstTaxHead.Integrated)) continue;
            lastRate = GstReportSupport.IntegratedRateOf(g, line.Amount);
            if (pending == 0m) continue;
            Add(lastRate.Value, pending);
            pending = 0m;
        }

        return byRate;

        void Add(int rate, decimal amount) =>
            byRate[rate] = byRate.TryGetValue(rate, out var cur) ? cur + amount : amount;
    }

    /// <summary>
    /// Splits ONE rate group's <b>posted</b> cess (<paramref name="groupCess"/>) across that group's stock lines, so
    /// each HSN row carries the cess actually levied on its own goods.
    ///
    /// <para><b>🔴 THE HARD PART, AND WHY A VALUE-SHARE WOULD HAVE BEEN WRONG.</b> Cess is levied at its own rate on
    /// its OWN notified goods, so a rate group routinely mixes cess-bearing and cess-free lines — an aerated drink
    /// and an ordinary 28% item on one invoice. Apportioning the group's cess by VALUE would smear it across every
    /// line in the group and file cess against an HSN that bears none, which is a positive misstatement on a filed
    /// cell, not a rounding difference.</para>
    ///
    /// <para><b>The weight is the engine's own per-line cess basis.</b> Each line is weighted by
    /// <c>CessCharge.CessBeforeRounding</c> — the exact unrounded figure <c>ComputeInvoiceTax</c> summed to produce
    /// the posted leg in the first place. A cess-free line resolves to <c>null</c> and weighs ZERO, so it receives
    /// nothing; ad-valorem, specific and RSP-factor lines are all weighted on the same footing because that one
    /// method values all three. The master is used ONLY to weight and to select — the posted total is authoritative
    /// and is never recomputed — and the group's last cess-bearing line absorbs the rounding remainder, so
    /// Σ line cess == the posted leg to the paisa (the same discipline the CGST/SGST/IGST split above uses).</para>
    ///
    /// <para><b>Fallback.</b> If the group posted cess but NO line resolves any (a cess master edited after the
    /// voucher was posted), the cess is spread by value rather than dropped — a filed figure that is slightly
    /// mis-attributed is recoverable, a silently vanished one is not, and Σ still reconciles to the posting.</para>
    /// </summary>
    private static decimal[] AttributeGroupCess(
        Company company, Voucher voucher, List<VoucherInventoryLine> groupLines, decimal groupValue,
        decimal groupCess, Services.GstService? resolver, Domain.Ledger? cessLedger)
    {
        var result = new decimal[groupLines.Count];
        if (groupCess == 0m || resolver is null) return result;

        var weights = new decimal[groupLines.Count];
        var weightSum = 0m;
        for (var i = 0; i < groupLines.Count; i++)
        {
            var il = groupLines[i];
            var charge = resolver.ResolveCess(
                company.FindStockItem(il.StockItemId), cessLedger, voucher.Date, il.Quantity);
            var w = charge?.CessBeforeRounding(il.Value) ?? 0m;
            if (w <= 0m) continue;
            weights[i] = w;
            weightSum += w;
        }

        // No line claims the cess: spread by value rather than lose it (see the fallback note above).
        var byValue = weightSum <= 0m;
        if (byValue)
        {
            for (var i = 0; i < groupLines.Count; i++) weights[i] = groupLines[i].Value.Amount;
            weightSum = groupValue;
            if (weightSum <= 0m) return result;
        }

        var last = Array.FindLastIndex(weights, w => w > 0m);
        var run = 0m;
        for (var i = 0; i < groupLines.Count; i++)
        {
            if (weights[i] <= 0m) continue;
            if (i == last) { result[i] = groupCess - run; break; }
            result[i] = Apportion(groupCess, weights[i], weightSum);
            run += result[i];
        }
        return result;
    }

    /// <summary>
    /// The integrated GST rate (basis points) of an item-invoice stock line, used only to bucket a multi-rate
    /// invoice's stock lines into the matching posted rate group — never to compute tax.
    /// <para><b>T0-17: this used to read the stock item's GST block directly</b>, hard-wired to one rung of a
    /// five-rung hierarchy, and so disagreed with the rate the posting engine actually assigned on any book where
    /// the walk landed elsewhere — the sales ledger under the shipped <c>LedgerFirst</c> default, a group or company
    /// rung, or an HSN-dated rate-history window. It now delegates to the ONE rule,
    /// <see cref="GstReportSupport.BucketingRateOf"/>, which resolves the line exactly as the posting did.</para>
    /// </summary>
    private static int LineIntegratedRate(
        Company company, Voucher voucher, Domain.Ledger? valueLedger, VoucherInventoryLine il) =>
        GstReportSupport.BucketingRateOf(company, voucher, company.FindStockItem(il.StockItemId), valueLedger);

    /// <summary>Delegates to <see cref="ProRata.Rupees"/> — the ONE apportionment rule (drift lock D1). This was a
    /// pure de-duplication: this copy carried no zero-denominator guard of its own, but it never needed one, because
    /// BOTH call sites (the stock/HSN loop and the service-SAC loop) already <c>continue</c> on
    /// <c>groupValue == 0m</c> before reaching here. Those caller-side guards are LOAD-BEARING — they skip the group
    /// entirely, which is the observable behaviour; do not delete them believing the shared rule's <c>== 0</c>
    /// replaces them, or the group's whole posted tax would land on its last leg via the remainder branch.</summary>
    private static decimal Apportion(decimal total, decimal value, decimal totalValue) =>
        ProRata.Rupees(total, value, totalValue);

    /// <param name="sign">🔴 T2-59 — <c>+1</c> for a supply, <c>-1</c> for a reversal (see
    /// <see cref="AccumulateHsn"/>). Applied ONLY at the accumulation below, deliberately: the UQC declaration and
    /// the commensurability checks above are about unit IDENTITY, which a reversal does not change, and declaring a
    /// NEGATIVE quantity into <c>UqcResolver.Declare</c> would push a sign through a resolver that has no business
    /// seeing one.</param>
    private static void AddHsnRow(
        Company company, VoucherInventoryLine il, decimal value, decimal cgst, decimal sgst, decimal igst,
        decimal cess, Dictionary<string, HsnAcc> hsnAcc, decimal sign)
    {
        var item = company.FindStockItem(il.StockItemId);
        // Resolution order is the ONE rule (GstReportSupport.HsnSacOf, drift lock D7); the "(none)" bucket label
        // is this report's own rendering of absence — see that method's note on why the sentinels differ.
        var hsn = GstReportSupport.HsnSacOf(item) ?? "(none)";
        // WI-10 Gap 2 follow-on: declare the quantity in the unit the LINE is stated in when that unit maps to a
        // valid UQC ("2 DOZ"), exactly as the printed invoice already does — NOT the line quantity beside the
        // item's BASE UQC, which would file "2 NOS" for a Table-12 row in which 24 Nos were actually supplied.
        var decl = UqcResolver.Declare(company, il, il.Quantity);

        if (!hsnAcc.TryGetValue(hsn, out var acc))
        {
            acc = new HsnAcc
            {
                HsnSac = hsn, Description = item?.Name ?? "(unknown)",
                Uqc = decl.Code, BaseUqc = decl.BaseCode,
                DeclaredUnitId = decl.DeclaredUnitId, BaseUnitId = decl.BaseUnitId,
            };
            hsnAcc[hsn] = acc;
        }

        // One row per HSN means one UQC per row, so lines of the same HSN declared in DIFFERENT units cannot be
        // summed as-is — "2 DOZ + 5 NOS = 7" is nonsense under either label. On a mismatch the whole row degrades
        // to the base-unit declaration, in which every line IS commensurable.
        //
        // Commensurability is decided on the lines' UNIT IDENTITY, not on the UQC label. Comparing labels is
        // unsound the moment one label is a CATCH-ALL: "OTH" marks a unit absent from the department's master
        // list, so 5 Crate-Nos and 3 Pallet-Nos both declare "OTH", the labels match, and the row files "8" for a
        // supply of 81 Nos — money right, quantity 10x understated, on a mandatory filed field. (This table has no
        // unit-price field and therefore no footing constraint, which is why it can and must degrade where the NIC
        // payload — which foots per line and aggregates nothing — deliberately does not.)
        //
        // NOTE — this runs on the SEEDING iteration too (acc was just built from this same line), so it compares a
        // line against ITSELF. That is intentional and LOAD-BEARING: AreCommensurable is deliberately NOT reflexive
        // on a wholly unknown pair, so a line with a null DeclaredUnitId AND a null/blank Uqc sets MixedUnits on a
        // single-line row and degrades it to the base declaration. That is the RIGHT outcome — a quantity we cannot
        // name is safer stated in the base unit — and on that path the degrade is the IDENTITY anyway
        // (Quantity == BaseQuantity, Uqc == BaseUqc), so nothing moves. Do not "optimise" the seed iteration away
        // and do not make the predicate reflexive to make this look tidier.
        if (!UqcResolver.AreCommensurable(acc.DeclaredUnitId, acc.Uqc, decl.DeclaredUnitId, decl.Code))
            acc.MixedUnits = true;
        // The degrade TARGET is only commensurable when the items share a base unit; see Gstr1HsnRow.MixedBases.
        if (acc.BaseUnitId != decl.BaseUnitId) acc.MixedBases = true;
        acc.Quantity += sign * decl.Quantity;
        acc.BaseQuantity += sign * decl.BaseQuantity;
        acc.Taxable += sign * value;
        acc.Cgst += sign * cgst; acc.Sgst += sign * sgst; acc.Igst += sign * igst; acc.Cess += sign * cess;
    }

    /// <summary>
    /// The service-income LEDGER legs of an accounting (service) invoice — a non-party, non-tax entry line whose
    /// ledger carries a <c>SalesPurchaseGst</c> (SAC) block; its taxable value is the leg amount. Round-Off and every
    /// other ledger without a SAC block (or a GST tax ledger) are excluded. Used ONLY when the voucher has no stock
    /// lines, so an item invoice never routes through here.
    /// <para><b>Public because the e-invoice payload MUST share this exact definition.</b> The NIC INV-01 emitter used
    /// to send <c>HsnCd = ""</c> on the ledger-only path while this method filed SAC 998311 in Table 12 for the very
    /// same voucher — a blank mandatory HsnCd is an IRP rejection, and two parallel implementations would drift again.
    /// One definition, two readers.</para>
    ///
    /// <para>🔴 <b>T1-79 — <c>Value</c> IS A MAGNITUDE, AND THE POSTED <c>Side</c> IS NOW YIELDED BESIDE IT SO NO
    /// READER CAN BE SIGN-BLIND BY ACCIDENT.</b> This method used to yield <c>line.Amount.Amount</c> alone and drop
    /// <see cref="EntryLine.Side"/> on the floor. Every consumer then added the magnitude as a positive contribution,
    /// so a leg posted on the side OPPOSING the document's natural one — a deduction — was counted as an addition.
    /// Measured off the emitted INV-01 bytes on Consultancy ₹7,500 Cr @ 18% (CGST 675 + SGST 675) with an EXEMPT SAC
    /// leg of ₹1,000 posted <b>Dr</b>: the correct document is <c>AssVal</c> ₹6,500 / <c>TotInvVal</c> ₹7,850 (the
    /// posted party debit); the payload declared <c>AssVal</c> ₹8,500 / <c>TotInvVal</c> ₹9,850 — <b>overstated by
    /// ₹2,000, twice the leg</b>, because the correct contribution is −1,000 and +1,000 was emitted. Σ <c>AssAmt</c>
    /// still equalled <c>AssVal</c>, so the IRP would have ACCEPTED the larger figure.</para>
    ///
    /// <para>🔴 <b>THE SIGN IS EXPOSED, NOT APPLIED, AND THAT IS DELIBERATE — SIGNING IT HERE WOULD HAVE REMOVED THE
    /// ONLY GUARD THAT CATCHES THE SHAPE.</b> <see cref="GstReportSupport.ServiceProjectionFoots"/> sums these
    /// magnitudes and compares them against the posted party leg, and double-entry balance makes that comparison a
    /// COMPLETE detector: a deduction leg of D forces the party leg down by D while the magnitude sum goes UP by D, so
    /// the projection exceeds the party leg by exactly 2D for every D &gt; 0 (measured: 9,850 against 7,850, 2 × 1,000).
    /// Had this method returned a signed value, the projection would have footed, the guard would have fallen silent,
    /// and the document would have been admitted — and no correct INV-01 exists for it anyway, because NIC types
    /// <c>"AssAmt": { "type": "number", "minimum": 0, … }</c> and so admits no negative line. The consumers' verdicts
    /// are recorded one by one at <see cref="GstReportSupport.ServiceProjectionFoots"/>.</para>
    /// </summary>
    public static IEnumerable<(Domain.Ledger Ledger, decimal Value, DrCr Side)> ServiceLegs(
        Company company, Voucher voucher)
    {
        foreach (var line in voucher.Lines)
        {
            if (voucher.PartyId is Guid pid && line.LedgerId == pid) continue; // the party leg is not a service leg
            var led = company.FindLedger(line.LedgerId);
            if (led?.SalesPurchaseGst is null) continue;    // not a service-income / SAC-bearing ledger
            if (led.GstClassification is not null) continue; // a GST (Duties &amp; Taxes) tax ledger
            yield return (led, line.Amount.Amount, line.Side);
        }
    }

    /// <summary>
    /// The side a service leg of <paramref name="voucher"/> posts on when it <b>ADDS</b> to the document's value. A leg
    /// on the other side is a DEDUCTION (<see cref="IsDeductionServiceLeg"/>). <c>null</c> for a base type that carries
    /// no such orientation, where "natural side" has no meaning — and on that reading NO leg is ever called a deduction,
    /// which is the conservative direction.
    ///
    /// <para>🔴 <b>THIS IS NOT <see cref="GstReportSupport.DirectionOf"/>, AND THE DIFFERENCE IS THE WHOLE POINT. The
    /// first cut of this method delegated to it and was WRONG ON THE NOTES — caught by probing a credit note rather
    /// than by reasoning about one.</b> <c>DirectionOf</c> groups <b>Sales with CreditNote</b> (both are outward
    /// supplies for tax purposes) and <b>Purchase with DebitNote</b>. The side a VALUE leg posts on groups them the
    /// OTHER way, because a note REVERSES its invoice:</para>
    /// <list type="bullet">
    /// <item><b>Sales</b> — Dr party, Cr income ⇒ value leg <b>Credit</b>.</item>
    /// <item><b>Debit Note</b> (purchase return we issue) — Dr party, Cr expense ⇒ value leg <b>Credit</b>.</item>
    /// <item><b>Purchase</b> — Dr expense, Cr party ⇒ value leg <b>Debit</b>.</item>
    /// <item><b>Credit Note</b> (sales return we issue) — Cr party, Dr income ⇒ value leg <b>Debit</b>.</item>
    /// </list>
    /// <para>So the rule is "the opposite of the side the PARTY leg takes", and <c>CreditNote</c> sits with
    /// <c>Purchase</c> here while <c>DirectionOf</c> puts it with <c>Sales</c>. MEASURED: delegating to
    /// <c>DirectionOf</c> made every leg of an ordinary ledger-only service credit note (Dr Consultancy 7,500,
    /// Cr party 8,850, Dr CGST/SGST 675 each — <c>CoverageOf == Covered</c>, <c>DocDtls.Typ "CRN"</c>, and
    /// <see cref="GstReportSupport.ServiceProjectionFoots"/> <b>True</b>, i.e. a perfectly sound document) look like a
    /// deduction, and the INV-01 was REFUSED. That is a sign error of exactly the class this slice exists to end, and
    /// it is written down here so the next reader does not "simplify" this back into <c>DirectionOf</c>.</para>
    /// </summary>
    public static DrCr? NaturalServiceLegSide(Company company, Voucher voucher)
    {
        ArgumentNullException.ThrowIfNull(company);
        ArgumentNullException.ThrowIfNull(voucher);
        return company.FindVoucherType(voucher.TypeId)?.BaseType switch
        {
            VoucherBaseType.Sales or VoucherBaseType.DebitNote => DrCr.Credit,
            VoucherBaseType.Purchase or VoucherBaseType.CreditNote => DrCr.Debit,
            _ => null,
        };
    }

    /// <summary>
    /// 🔴 <b>T1-79 — whether a service leg posted on the side OPPOSING its document's natural one</b>, i.e. a leg that
    /// REDUCES the invoice rather than adding to it. The single predicate every reader of
    /// <see cref="ServiceLegs"/> asks, so the sign question is answered in one place rather than re-derived (this
    /// project has had the sign-of-a-reducing-entry wrong in four separate reports, each fixed at one call site).
    /// <para>Conservative by construction: when the document's natural side cannot be established
    /// (<see cref="NaturalServiceLegSide"/> is <c>null</c>) no leg is called a deduction, so this can only ever
    /// identify a leg whose orientation is known to oppose a known natural side.</para>
    /// </summary>
    public static bool IsDeductionServiceLeg(Company company, Voucher voucher, DrCr side) =>
        NaturalServiceLegSide(company, voucher) is DrCr natural && side != natural;

    /// <summary>
    /// Attributes an accounting (service) invoice's posted tax to its service-income ledger legs, grouped by SAC —
    /// the service mirror of <see cref="AccumulateHsn"/>'s item-line pass. Each leg's tax is a value-share of ITS OWN
    /// integrated-rate group's posted tax (never the blended total); within a group the value-share is paisa-exact and
    /// the group's last leg absorbs the rounding remainder, so Σ leg tax == the group's posted tax. Reads only posted
    /// amounts — the per-leg RATE is read from the ledger's SAC purely to bucket the leg into the matching posted group.
    ///
    /// <para><b>A NON-TAXABLE service leg is NEVER a member of a posted rate group.</b> It contributed no taxable value
    /// to the tax the screen computed (<c>ComputeAccountingInvoiceGst</c> skips it), so it must not receive a share of
    /// that tax back here. Both money defects on this path were the same root cause:</para>
    /// <list type="bullet">
    /// <item><b>Single-rate invoice</b> — the <c>singleRate</c> collapse forced EVERY leg into the one posted group.
    /// Consultancy 18% 10,000 + Exempt 5,000 filed <c>998311 taxable=10,000 cgst=600 sgst=600</c> and
    /// <c>999999 taxable=5,000 cgst=300 sgst=300</c> with an EMPTY exempt bucket: the taxed SAC understated its own
    /// tax, an EXEMPT supply was declared as taxed, and the exempt turnover disappeared — three misstatements at once.</item>
    /// <item><b>Multi-rate invoice</b> — <see cref="LedgerIntegratedRate"/> returns 0 for a non-taxable ledger, no
    /// rate-0 group exists, so the <c>continue</c> below DISCARDED the leg: the 5,000 exempt supply was absent from
    /// Table 12 AND from the exempt bucket.</item>
    /// </list>
    /// <para>Every non-taxable leg is therefore routed to the EXEMPT bucket + a zero-tax SAC row (the same treatment
    /// <see cref="AccumulateHsn"/> now gives a non-taxable STOCK line — 🔴 note that when this was written that claim
    /// was FALSE: <c>AccumulateHsn</c>'s exempt handling was all-or-nothing, firing only on a wholly-untaxed invoice,
    /// so the goods path still collapsed an exempt line into the posted group. That is T1-59, fixed with the shared
    /// per-line discriminator <see cref="GstReportSupport.IsNonTaxableStockLine"/>), and the posted tax is apportioned across the TAXABLE
    /// legs only. A wholly-exempt invoice never reaches here (it has no posted tax); a wholly-taxable one behaves
    /// exactly as before.</para>
    /// </summary>
    /// <param name="sign">🔴 T2-59 — <c>+1</c> for a supply, <c>-1</c> for a reversal; see
    /// <see cref="AccumulateHsn"/>. Threaded through to <see cref="AddServiceHsnRow"/> and the exempt bucket so a
    /// service credit note nets its SAC row and its exempt turnover down.</param>
    private static void AccumulateServiceHsn(
        Company company, Voucher voucher, Dictionary<string, HsnAcc> hsnAcc, ref decimal exempt, decimal sign)
    {
        var rateGroups = ReadInvoiceRateGroups(voucher);

        // Split first: exempt/nil/non-GST legs out of the rate machinery entirely, taxable legs into it.
        //
        // T1-79 sweep: the posted Side is now VISIBLE here and is deliberately NOT applied — see the note at the
        // ServiceLegs loop in the Table-12 pass above. Table 12 and the exempt bucket are a filed return with their
        // own fixtures; the magnitude reading is REPORTED as an exposure rather than changed unmeasured.
        var taxableLegs = new List<(Domain.Ledger Ledger, decimal Value)>();
        foreach (var (ledger, value, _) in ServiceLegs(company, voucher))
        {
            if (IsNonTaxableServiceLedger(ledger))
            {
                exempt += sign * value;
                AddServiceHsnRow(ledger, value, 0m, 0m, 0m, hsnAcc, sign);
                continue;
            }
            taxableLegs.Add((ledger, value));
        }

        // The single-rate collapse is now scoped to the TAXABLE legs: when the invoice posted exactly one rate group,
        // every taxable leg belongs to it regardless of where its own rate resolved (a rate-history override, a
        // company-level default). A non-taxable leg can never reach this line.
        var singleRate = rateGroups.Count == 1 ? rateGroups[0].Rate : (int?)null;

        var legsByRate = new Dictionary<int, List<(Domain.Ledger Ledger, decimal Value)>>();
        foreach (var leg in taxableLegs)
        {
            var rate = singleRate ?? LedgerIntegratedRate(company, voucher, leg.Ledger);
            if (!legsByRate.TryGetValue(rate, out var list)) legsByRate[rate] = list = new List<(Domain.Ledger, decimal)>();
            list.Add(leg);
        }

        foreach (var (rate, group) in rateGroups)
        {
            if (!legsByRate.TryGetValue(rate, out var groupLegs) || groupLegs.Count == 0)
                continue; // no matched service leg for this posted rate group (defensive)

            var groupValue = groupLegs.Sum(l => l.Value);
            if (groupValue == 0m) continue;

            var runCgst = 0m; var runSgst = 0m; var runIgst = 0m;
            for (var i = 0; i < groupLegs.Count; i++)
            {
                var (ledger, value) = groupLegs[i];
                decimal cgst, sgst, igst;
                if (i == groupLegs.Count - 1)
                {
                    // The group's last leg absorbs the remainder so Σ leg tax == the group's posted tax exactly.
                    cgst = group.Cgst - runCgst;
                    sgst = group.Sgst - runSgst;
                    igst = group.Igst - runIgst;
                }
                else
                {
                    cgst = Apportion(group.Cgst, value, groupValue);
                    sgst = Apportion(group.Sgst, value, groupValue);
                    igst = Apportion(group.Igst, value, groupValue);
                    runCgst += cgst; runSgst += sgst; runIgst += igst;
                }
                AddServiceHsnRow(ledger, value, cgst, sgst, igst, hsnAcc, sign);
            }
        }
    }

    /// <summary>
    /// The integrated GST rate (basis points) of a service-income ledger leg, used only to bucket a multi-rate
    /// service invoice's legs into the matching posted rate group, never to compute tax.
    /// <para><b>T0-17: this used to read the ledger's <c>SalesPurchaseGst</c> (SAC) block directly.</b> A service leg
    /// IS its own value ledger, so the walk ORDER cannot separate the two here — but an HSN/SAC-dated
    /// rate-history window can, and did: with both legs read at their declared rate, a posted group that matched no
    /// leg was skipped by the caller's defensive <c>continue</c> and ITS TAX WAS DROPPED FROM TABLE 12 ALTOGETHER.
    /// It now delegates to the ONE rule, <see cref="GstReportSupport.BucketingRateOf"/>.</para>
    /// </summary>
    private static int LedgerIntegratedRate(Company company, Voucher voucher, Domain.Ledger ledger) =>
        GstReportSupport.BucketingRateOf(company, voucher, item: null, ledger);

    /// <summary>Whether a service-income ledger's SAC block declares an <b>exempt / nil-rated / non-GST</b> supply.
    /// The single discriminator for "this leg contributed nothing to the tax base, so it may never be bucketed into a
    /// posted rate group" — see <see cref="AccumulateServiceHsn"/>. Kept as one predicate so the exempt-branch and the
    /// taxed-branch treatments can never drift apart.</summary>
    public static bool IsNonTaxableServiceLedger(Domain.Ledger ledger) =>
        ledger.SalesPurchaseGst is { IsTaxable: false };

    /// <summary>The SAC a service-income ledger declares, or <c>null</c> when it declares none. The single resolver
    /// GSTR-1's Table-12 rows and the e-invoice <c>HsnCd</c> both read, so the two can never disagree (FIX-6).</summary>
    public static string? ServiceSacOf(Domain.Ledger ledger) =>
        string.IsNullOrWhiteSpace(ledger.SalesPurchaseGst?.HsnSac) ? null : ledger.SalesPurchaseGst!.HsnSac;

    /// <summary>
    /// Adds/accumulates a service-income ledger's SAC row: SAC = <c>ledger.SalesPurchaseGst.HsnSac</c>, description =
    /// the ledger name, taxable = the leg value, tax = the attributed heads. A service carries no unit, so the row
    /// declares a blank UQC and zero quantity (Table-12 rows for services have no quantity).
    /// </summary>
    /// <param name="sign">🔴 T2-59 — <c>+1</c> for a supply, <c>-1</c> for a reversal (a service credit note). The
    /// SAC path needs the identical treatment to the goods path in <see cref="AddHsnRow"/>: a returned service that
    /// ADDED to its SAC row files the same overstatement, and fixing only the goods half is exactly how the
    /// sign-of-a-note defect came back four times on this project.</param>
    private static void AddServiceHsnRow(
        Domain.Ledger ledger, decimal value, decimal cgst, decimal sgst, decimal igst,
        Dictionary<string, HsnAcc> hsnAcc, decimal sign)
    {
        var sac = ledger.SalesPurchaseGst?.HsnSac ?? "(none)";
        if (!hsnAcc.TryGetValue(sac, out var acc))
        {
            acc = new HsnAcc { HsnSac = sac, Description = ledger.Name };
            hsnAcc[sac] = acc;
        }
        acc.Taxable += sign * value;
        acc.Cgst += sign * cgst; acc.Sgst += sign * sgst; acc.Igst += sign * igst;
    }

    private sealed class HeadAmounts
    {
        public decimal Taxable;
        public decimal Cgst;
        public decimal Sgst;
        public decimal Igst;
    }

    private sealed class HsnAcc
    {
        public string HsnSac = "";
        public string Description = "";
        public string? Uqc;
        /// <summary>The item's base-unit UQC — the label <see cref="BaseQuantity"/> carries.</summary>
        public string? BaseUqc;
        /// <summary>Σ of every line's quantity re-expressed in the item's base unit; the commensurable
        /// fallback when <see cref="MixedUnits"/> makes <see cref="Quantity"/> unsummable.</summary>
        public decimal BaseQuantity;
        /// <summary>The identity of the unit <see cref="Quantity"/> counts — what commensurability is decided on.</summary>
        public Guid? DeclaredUnitId;
        /// <summary>The identity of the unit <see cref="BaseQuantity"/> counts.</summary>
        public Guid? BaseUnitId;
        /// <summary>Set when two lines of this HSN were not commensurable (different units, whatever their labels).</summary>
        public bool MixedUnits;
        /// <summary>Set when two lines of this HSN are measured in unrelated BASE units, so even the degrade
        /// target is incommensurable. Carried onto <see cref="Gstr1HsnRow.MixedBases"/>, which no view model or
        /// payload writer reads today — see that member for why, and do not read this as a warning that reaches
        /// a filer.</summary>
        public bool MixedBases;
        public decimal Quantity;
        public decimal Taxable;
        public decimal Cgst;
        public decimal Sgst;
        public decimal Igst;
        /// <summary>Σ Compensation-Cess attributed to this HSN — see <see cref="AccumulateHsn"/>'s cess block.</summary>
        public decimal Cess;
    }
}
