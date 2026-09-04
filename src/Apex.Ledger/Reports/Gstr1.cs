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
    decimal BaseQuantity,
    string? BaseCode,
    Guid? DeclaredUnitId,
    Guid? BaseUnitId,
    bool MixedBases)
{
    /// <summary>Σ tax on this HSN row (CGST + SGST + IGST).</summary>
    public Money TotalTax => new(Cgst.Amount + Sgst.Amount + Igst.Amount);
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

    /// <summary>Builds GSTR-1 for the whole company over <c>[from, to]</c>.</summary>
    public static Gstr1 Build(Company company, DateOnly from, DateOnly to)
    {
        // Phase 9 slice 3 (RQ-16): a Composition dealer files CMP-08 / GSTR-4, NOT GSTR-1 — early-return an empty return
        // so it never emits an outward-supplies return (the Desktop routes it to the CMP-08/GSTR-4 screens). The inward
        // RCM the dealer owes is reported in CMP-08 Table 3(ii), so nothing is lost. A Regular company never enters this
        // branch ⇒ byte-identical (ER-13).
        if (company.Gst?.RegistrationType == GstRegistrationType.Composition)
            return new Gstr1(from, to, [], [], [], [], Money.Zero, Money.Zero, Money.Zero, Money.Zero);

        var b2b = new List<Gstr1B2BRow>();
        var b2cAcc = new Dictionary<int, HeadAmounts>();       // by integrated rate
        var rateAcc = new Dictionary<int, (decimal Taxable, decimal Tax)>();
        var hsnAcc = new Dictionary<string, HsnAcc>();
        var exempt = 0m;
        var totalCgst = 0m; var totalSgst = 0m; var totalIgst = 0m;

        foreach (var (voucher, _) in GstReportSupport.PostedDirectionalVouchers(company, from, to, GstTaxDirection.Output))
        {
            // Phase 9 slice 2b: a formalised §34 credit/debit note is a first-class outward document projected by Table 9B
            // (signed by note type) and folded — signed — into the output totals below. Exclude it from the ordinary
            // invoice sweep so its tax is not double-counted (mirrors the RCM / outward-4B exclusions, risk #4). A company
            // with no §34 note never enters this branch (byte-identical, ER-13).
            if (GstReportSupport.CdnLinkFor(company, voucher) is not null) continue;

            // Per-invoice posted tax by head (read off the tax lines — never recomputed).
            var invoice = ReadInvoiceHeads(voucher);
            var hasTax = invoice.Cgst != 0m || invoice.Sgst != 0m || invoice.Igst != 0m;
            totalCgst += invoice.Cgst; totalSgst += invoice.Sgst; totalIgst += invoice.Igst;

            // An outward reverse-charge supply (zero forward tax; sales ledger flagged ReverseChargeApplicable) belongs
            // ONLY in Table 4B (Rcm4BOutwardValue) — never the exempt/nil/non-GST bucket or the HSN sweep, else it is
            // double-represented (its value would appear in both 4B and exempt). Skip it here (Phase 9 slice 2; RQ-7).
            if (!hasTax && GstReportSupport.IsOutwardReverseChargeSupply(company, voucher)) continue;

            // HSN summary + exempt bucket — every other outward supply (taxable AND exempt/nil) contributes here.
            AccumulateHsn(company, voucher, invoice, hsnAcc, ref exempt);

            // A no-tax supply (exempt/nil/non-GST) belongs only in the HSN summary + exempt bucket, not in the
            // B2B/B2C tax rows or the taxable rate-wise summary.
            if (!hasTax) continue;

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

            // B2B when the party carries a GSTIN (registered); else B2C (DP-8).
            var isB2B = party?.PartyGst is { } pg && !pg.IsB2C;
            if (isB2B)
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
            else
            {
                foreach (var (rate, g) in rateGroups)
                {
                    var h = b2cAcc.TryGetValue(rate, out var cur) ? cur : new HeadAmounts();
                    h.Taxable += g.Taxable; h.Cgst += g.Cgst; h.Sgst += g.Sgst; h.Igst += g.Igst;
                    b2cAcc[rate] = h;
                }
            }

            // Rate-wise summary (taxable outward, by integrated rate) — one contribution per rate group.
            foreach (var (rate, g) in rateGroups)
            {
                var (t, x) = rateAcc.TryGetValue(rate, out var cur) ? cur : (0m, 0m);
                rateAcc[rate] = (t + g.Taxable, x + g.Cgst + g.Sgst + g.Igst);
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
                new Money(h.Taxable), new Money(h.Cgst), new Money(h.Sgst), new Money(h.Igst),
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
        var table9B = BuildTable9B(company, from, to, ref totalCgst, ref totalSgst, ref totalIgst);
        var (table11A, table11B) = BuildAdvanceTables(company, from, to);

        return new Gstr1(from, to, b2b, b2cRows, rateRows, hsnRows,
            new Money(exempt), new Money(totalCgst), new Money(totalSgst), new Money(totalIgst))
        {
            Rcm4BOutwardValue = ComputeRcm4BOutwardValue(company, from, to),
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
    public static EInvoiceReconciliationView EInvoiceReconciliation(Company company, DateOnly from, DateOnly to)
    {
        var svc = new EInvoiceService(company);
        int covered = 0, tagged = 0, pending = 0, failed = 0, cancelled = 0, mismatched = 0;

        foreach (var (voucher, _) in GstReportSupport.PostedDirectionalVouchers(company, from, to, GstTaxDirection.Output))
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
    /// </summary>
    private static IReadOnlyList<Gstr1Table9BRow> BuildTable9B(
        Company company, DateOnly from, DateOnly to, ref decimal totalCgst, ref decimal totalSgst, ref decimal totalIgst)
    {
        if (company.CreditDebitNoteLinks.Count == 0) return [];

        var rows = new List<Gstr1Table9BRow>();
        foreach (var link in company.CreditDebitNoteLinks)
        {
            var v = company.FindVoucher(link.CdnVoucherId);
            if (v is null || v.Date < from) continue;
            var type = company.FindVoucherType(v.TypeId);
            if (type is null || !LedgerBalances.CountsAsOf(v, to, type.BaseType)) continue;

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
    /// </summary>
    private static (IReadOnlyList<Gstr1AdvanceRow> Table11A, IReadOnlyList<Gstr1AdvanceAdjustedRow> Table11B)
        BuildAdvanceTables(Company company, DateOnly from, DateOnly to)
    {
        if (company.AdvanceReceipts.Count == 0) return ([], []);

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
    private static Money ComputeRcm4BOutwardValue(Company company, DateOnly from, DateOnly to)
    {
        if (!company.GstEnabled) return Money.Zero;
        var total = 0m;
        foreach (var (voucher, type) in GstReportSupport.PostedDirectionalVouchers(company, from, to, GstTaxDirection.Output))
        {
            // A Credit Note against an outward RCM supply REDUCES the 4B value (it nets the original supply down),
            // mirroring how an outward return nets down a rate row; a Sales voucher adds. Signing by base type (rather
            // than treating every posted line as additive) keeps 4B from being inflated by a credit note (Phase 9 S2; RQ-7).
            var sign = type.BaseType == VoucherBaseType.CreditNote ? -1m : 1m;
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
    /// </summary>
    private static void AccumulateHsn(
        Company company, Voucher voucher, (decimal Cgst, decimal Sgst, decimal Igst) invoice,
        Dictionary<string, HsnAcc> hsnAcc, ref decimal exempt)
    {
        var invoiceTax = invoice.Cgst == 0m && invoice.Sgst == 0m && invoice.Igst == 0m;
        if (invoiceTax)
        {
            // An all-exempt/nil outward supply: record its value against exempt + its HSN row (zero tax).
            foreach (var il in voucher.InventoryLines)
            {
                exempt += il.Value.Amount;
                AddHsnRow(company, il, il.Value.Amount, 0m, 0m, 0m, hsnAcc);
            }

            // Accounting (service) invoice with no stock lines: attribute the service-income LEDGER legs to the exempt
            // bucket + their SAC row — but ONLY a genuinely exempt/nil service ledger (SalesPurchaseGst IsTaxable:false).
            // A plain As-Voucher TAXABLE sale (an 18% ledger, but the plain grid posts NO tax legs) ALSO reaches this
            // no-tax branch; attributing it here would file its value in the Table-12 EXEMPT column — a positive
            // misstatement. The IsTaxable:false gate is the discriminator the flag-less structural signal otherwise
            // lacks. Gated on InventoryLines.Count==0 so an existing exempt item invoice is never double-counted.
            if (voucher.InventoryLines.Count == 0)
                foreach (var (ledger, value) in ServiceLegs(company, voucher))
                    if (IsNonTaxableServiceLedger(ledger))
                    {
                        exempt += value;
                        AddServiceHsnRow(ledger, value, 0m, 0m, 0m, hsnAcc);
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
            AccumulateServiceHsn(company, voucher, hsnAcc, ref exempt);
            return;
        }

        // The posted per-(integrated rate) tax groups for this invoice (from its tax lines).
        var rateGroups = ReadInvoiceRateGroups(voucher);

        // Bucket the stock lines by their integrated rate so each line's tax comes from its OWN rate group.
        // When the invoice is single-rate, every taxable line falls in that one group regardless of where the
        // rate resolved; for a multi-rate invoice a line's rate is read from the item's GST master.
        var singleRate = rateGroups.Count == 1 ? rateGroups[0].Rate : (int?)null;
        // Per-VOUCHER, so it is resolved once rather than per line (see BucketingValueLedger).
        var valueLedger = singleRate is null ? GstReportSupport.BucketingValueLedger(company, voucher) : null;
        var linesByRate = new Dictionary<int, List<VoucherInventoryLine>>();
        foreach (var il in voucher.InventoryLines)
        {
            var rate = singleRate ?? LineIntegratedRate(company, voucher, valueLedger, il);
            if (!linesByRate.TryGetValue(rate, out var list)) linesByRate[rate] = list = new List<VoucherInventoryLine>();
            list.Add(il);
        }

        foreach (var (rate, group) in rateGroups)
        {
            if (!linesByRate.TryGetValue(rate, out var groupLines) || groupLines.Count == 0)
                continue; // no matched stock line for this rate group (defensive; e.g. as-voucher-only tax)

            var groupValue = groupLines.Sum(l => l.Value.Amount);
            if (groupValue == 0m) continue;

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
                AddHsnRow(company, il, value, cgst, sgst, igst, hsnAcc);
            }
        }
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

    private static void AddHsnRow(
        Company company, VoucherInventoryLine il, decimal value, decimal cgst, decimal sgst, decimal igst,
        Dictionary<string, HsnAcc> hsnAcc)
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
        acc.Quantity += decl.Quantity;
        acc.BaseQuantity += decl.BaseQuantity;
        acc.Taxable += value;
        acc.Cgst += cgst; acc.Sgst += sgst; acc.Igst += igst;
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
    /// </summary>
    public static IEnumerable<(Domain.Ledger Ledger, decimal Value)> ServiceLegs(Company company, Voucher voucher)
    {
        foreach (var line in voucher.Lines)
        {
            if (voucher.PartyId is Guid pid && line.LedgerId == pid) continue; // the party leg is not a service leg
            var led = company.FindLedger(line.LedgerId);
            if (led?.SalesPurchaseGst is null) continue;    // not a service-income / SAC-bearing ledger
            if (led.GstClassification is not null) continue; // a GST (Duties &amp; Taxes) tax ledger
            yield return (led, line.Amount.Amount);
        }
    }

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
    /// <para>Every non-taxable leg is therefore routed to the EXEMPT bucket + a zero-tax SAC row (mirroring the
    /// exempt-branch treatment in <see cref="AccumulateHsn"/>), and the posted tax is apportioned across the TAXABLE
    /// legs only. A wholly-exempt invoice never reaches here (it has no posted tax); a wholly-taxable one behaves
    /// exactly as before.</para>
    /// </summary>
    private static void AccumulateServiceHsn(
        Company company, Voucher voucher, Dictionary<string, HsnAcc> hsnAcc, ref decimal exempt)
    {
        var rateGroups = ReadInvoiceRateGroups(voucher);

        // Split first: exempt/nil/non-GST legs out of the rate machinery entirely, taxable legs into it.
        var taxableLegs = new List<(Domain.Ledger Ledger, decimal Value)>();
        foreach (var leg in ServiceLegs(company, voucher))
        {
            if (IsNonTaxableServiceLedger(leg.Ledger))
            {
                exempt += leg.Value;
                AddServiceHsnRow(leg.Ledger, leg.Value, 0m, 0m, 0m, hsnAcc);
                continue;
            }
            taxableLegs.Add(leg);
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
                AddServiceHsnRow(ledger, value, cgst, sgst, igst, hsnAcc);
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
    private static void AddServiceHsnRow(
        Domain.Ledger ledger, decimal value, decimal cgst, decimal sgst, decimal igst,
        Dictionary<string, HsnAcc> hsnAcc)
    {
        var sac = ledger.SalesPurchaseGst?.HsnSac ?? "(none)";
        if (!hsnAcc.TryGetValue(sac, out var acc))
        {
            acc = new HsnAcc { HsnSac = sac, Description = ledger.Name };
            hsnAcc[sac] = acc;
        }
        acc.Taxable += value;
        acc.Cgst += cgst; acc.Sgst += sgst; acc.Igst += igst;
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
    }
}
