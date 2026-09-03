using Apex.Ledger.Domain;
using Apex.Ledger.Services;

namespace Apex.Ledger.Reports;

/// <summary>
/// One <b>ordinary-invoice amendment</b> row of GSTR-1 (Phase 9 slice 8b; RQ-29; DP-33) — Table <b>9A</b>
/// (B2BA amended B2B / B2CLA amended B2C-Large / EXPA amended exports) or Table <b>10</b> (B2CSA amended B2C-Others).
/// An amendment references the <b>original</b> document (its number + date + period) and re-states the <b>revised</b>
/// values; only the <see cref="DifferentialTax"/> (revised − original) is actually settled. Because no persisted
/// amend-link exists (adding one is a schema change, deferred), the ordinary-invoice amendment is an <b>advisory,
/// best-effort</b> projection (<see cref="Advisory"/> = true): it is detected when a document number + party seen in an
/// already-filed period reappears — restated — in the current period.
/// </summary>
public sealed record Gstr1AmendmentRow(
    string SectionCode,
    string? OriginalPartyGstin,
    string OriginalDocNumber,
    DateOnly OriginalDocDate,
    Money OriginalTaxableValue,
    Money OriginalTax,
    Money RevisedTaxableValue,
    Money RevisedCgst,
    Money RevisedSgst,
    Money RevisedIgst,
    bool Advisory)
{
    /// <summary>The form-table number this section code maps to ("9A" or "10").</summary>
    public string FormTable => Gstr1Amendments.FormTableOf(SectionCode);

    /// <summary>Σ the revised tax across heads (CGST + SGST + IGST).</summary>
    public Money RevisedTax => new(RevisedCgst.Amount + RevisedSgst.Amount + RevisedIgst.Amount);

    /// <summary>The differential taxable value settled by the amendment (revised − original).</summary>
    public Money DifferentialTaxableValue => new(RevisedTaxableValue.Amount - OriginalTaxableValue.Amount);

    /// <summary>The differential tax settled by the amendment (revised − original); only this delta is actually paid.</summary>
    public Money DifferentialTax => new(RevisedTax.Amount - OriginalTax.Amount);
}

/// <summary>
/// One <b>amended credit/debit note</b> row of GSTR-1 (Phase 9 slice 8b; RQ-29; DP-33) — Table <b>9C</b>
/// (CDNRA amended registered CDN / CDNURA amended unregistered CDN). Modelled cleanly off the existing §34
/// <see cref="GstCreditDebitNoteLink"/> (no schema): a credit/debit note whose <b>original invoice reference points at a
/// prior filed period</b> is the no-schema proxy for a cross-period CDN amendment. The row carries the original-invoice
/// reference and the note's <b>signed</b> revised tax (a credit note negative, a debit note positive).
/// </summary>
public sealed record Gstr1CdnAmendmentRow(
    string SectionCode,
    CdnType NoteType,
    Guid? OriginalInvoiceVoucherId,
    string? OriginalInvoiceNumber,
    DateOnly OriginalInvoiceDate,
    DateOnly NoteDate,
    Money RevisedTaxableValue,
    Money RevisedCgst,
    Money RevisedSgst,
    Money RevisedIgst)
{
    /// <summary>The form-table number this section code maps to ("9C").</summary>
    public string FormTable => Gstr1Amendments.FormTableOf(SectionCode);

    /// <summary>Σ the signed revised tax across heads (CGST + SGST + IGST).</summary>
    public Money RevisedTax => new(RevisedCgst.Amount + RevisedSgst.Amount + RevisedIgst.Amount);
}

/// <summary>
/// <b>GSTR-1 amendment tables</b> (Phase 9 slice 8b; RQ-29; DP-33; §2.16) — the projections that surface amendments of
/// earlier-period outward documents declared in the current period <c>[from, to]</c>. Section codes map to form tables
/// exactly (A14-CONFIRMED): <b>9A</b> = B2BA + B2CLA + EXPA; <b>9C</b> = CDNRA + CDNURA; <b>10</b> = B2CSA
/// (the original credit/debit notes are the existing <see cref="Gstr1.Table9B"/> — Table 9B — not an amendment). No
/// schema is added:
/// <list type="bullet">
///   <item><b>Table 9C (amended CDN)</b> reuses the §34 <see cref="GstCreditDebitNoteLink"/> — a CDN whose original
///     invoice is in a prior filed period. Fully no-schema; the linkage is real.</item>
///   <item><b>Table 9A (amended B2B)</b> is an <b>advisory</b> prior-period-delta projection (no persisted amend-link;
///     an exact link would be schema, deferred). One-amendment-per-document is enforced by dedup on the original doc ref
///     (a second re-statement in the period <b>replaces</b>, never stacks).</item>
///   <item><b>Table 10 (B2CSA)</b> — consumer supplies carry no document number to key an amendment on, so it is a
///     documented carry-forward (empty) rather than a silent auto-detection that could mis-state.</item>
/// </list>
/// The current window's start acts as the already-filed-through boundary: a document dated before <c>from</c> is prior /
/// filed. A Composition / GST-off company yields a not-applicable (empty) projection (ER-13). Deterministic; posts nothing.
/// </summary>
public sealed record Gstr1Amendments(
    DateOnly From,
    DateOnly To,
    bool Applicable,
    IReadOnlyList<Gstr1AmendmentRow> Table9A,
    IReadOnlyList<Gstr1CdnAmendmentRow> Table9C)
{
    /// <summary>Maps a GSTR-1 amendment <b>section code</b> to its <b>form-table number</b> (A14-CONFIRMED). B2BA/B2CLA/
    /// EXPA → 9A; CDNRA/CDNURA → 9C; B2CSA → 10. (Note: form-table numbers deliberately differ from the API section codes.)</summary>
    public static string FormTableOf(string sectionCode) => sectionCode switch
    {
        "B2BA" or "B2CLA" or "EXPA" => "9A",
        "CDNRA" or "CDNURA" => "9C",
        "B2CSA" => "10",
        _ => throw new ArgumentOutOfRangeException(nameof(sectionCode), sectionCode, "Unknown GSTR-1 amendment section code."),
    };

    /// <summary>Builds the GSTR-1 amendment tables for the current period <c>[from, to]</c>; a Composition / GST-off
    /// company yields a not-applicable (empty) projection (ER-13).</summary>
    public static Gstr1Amendments Build(Company company, DateOnly from, DateOnly to)
    {
        ArgumentNullException.ThrowIfNull(company);

        // A Composition dealer files no GSTR-1 (CMP-08 / GSTR-4 instead) and a GST-off company has no outward return ⇒
        // a not-applicable (empty) amendment projection (ER-13 automatic).
        if (!company.GstEnabled || company.Gst?.RegistrationType == GstRegistrationType.Composition)
            return new Gstr1Amendments(from, to, false, [], []);

        var table9C = BuildTable9C(company, from, to);
        var table9A = BuildTable9A(company, from, to);
        return new Gstr1Amendments(from, to, true, table9A, table9C);
    }

    /// <summary>Table 9C (CDNRA / CDNURA) — the §34 credit/debit notes posted in <c>[from, to]</c> whose original invoice
    /// falls in a <b>prior</b> filed period (dated before <c>from</c>): the no-schema proxy for a cross-period CDN
    /// amendment (§5.1). Each row carries the original-invoice reference and the note's signed revised tax (a credit note
    /// negative, a debit note positive). Deterministic order (note date, then original ref, then note type).</summary>
    private static IReadOnlyList<Gstr1CdnAmendmentRow> BuildTable9C(Company company, DateOnly from, DateOnly to)
    {
        if (company.CreditDebitNoteLinks.Count == 0) return [];

        var rows = new List<Gstr1CdnAmendmentRow>();
        foreach (var link in company.CreditDebitNoteLinks)
        {
            // The original invoice must be in a PRIOR filed period; a same-period note is an ordinary Table 9B CDN, not
            // an amendment. A note with no original date cannot be placed in the prior/current split ⇒ skipped.
            if (link.OriginalInvoiceDate is not { } origDate || origDate >= from) continue;

            var v = company.FindVoucher(link.CdnVoucherId);
            if (v is null || v.Date < from) continue;
            var type = company.FindVoucherType(v.TypeId);
            if (type is null || !LedgerBalances.CountsAsOf(v, to, type.BaseType)) continue;

            var heads = ReadForwardHeads(v);
            var taxable = GstReportSupport.InvoiceTaxableValue(v).Amount;
            var sign = link.CdnType == CdnType.Credit ? -1m : 1m;

            rows.Add(new Gstr1CdnAmendmentRow(
                link.Is9BTarget ? "CDNRA" : "CDNURA", link.CdnType,
                link.OriginalInvoiceVoucherId, link.OriginalInvoiceNumber, origDate, v.Date,
                new Money(sign * taxable), new Money(sign * heads.Cgst), new Money(sign * heads.Sgst), new Money(sign * heads.Igst)));
        }
        return rows
            .OrderBy(r => r.NoteDate)
            .ThenBy(r => r.OriginalInvoiceNumber, StringComparer.Ordinal)
            .ThenBy(r => r.NoteType)
            .ToList();
    }

    /// <summary>Table 9A (B2BA) — the <b>advisory</b> prior-period-delta projection for ordinary B2B invoices. An
    /// amendment is detected when a (party GSTIN, invoice number) seen in an already-filed period (dated before
    /// <c>from</c>) reappears — re-stated — in the current period <c>[from, to]</c>. Because no persisted amend-link
    /// exists, this is best-effort (<see cref="Gstr1AmendmentRow.Advisory"/> = true). One-amendment-per-document: the
    /// LATEST current-period re-statement is the revised value (a second re-statement replaces, never stacks); the
    /// referenced original is the LAST filed version. Deterministic order (original party GSTIN, then doc number).</summary>
    private static IReadOnlyList<Gstr1AmendmentRow> BuildTable9A(Company company, DateOnly from, DateOnly to)
    {
        // The already-filed B2B (dated before the current window) and the current-window B2B, keyed by (GSTIN, number).
        var prior = Latest(Gstr1.Build(company, DateOnly.MinValue, from.AddDays(-1)).B2B);
        var current = Latest(Gstr1.Build(company, from, to).B2B);

        var rows = new List<(int Seq, Gstr1AmendmentRow Row)>();
        foreach (var (key, revised) in current)
        {
            if (!prior.TryGetValue(key, out var original)) continue; // brand-new invoice this period — not an amendment
            rows.Add((original.RawNumber, new Gstr1AmendmentRow(
                "B2BA", original.PartyGstin, original.InvoiceNumber, original.InvoiceDate,
                original.TaxableValue, new Money(original.Cgst.Amount + original.Sgst.Amount + original.Igst.Amount),
                revised.TaxableValue, revised.Cgst, revised.Sgst, revised.Igst, Advisory: true)));
        }
        // Secondary order is the RAW int voucher sequence (numeric), NOT an ordinal sort of the rendered doc string —
        // otherwise invoices 2 and 10 would sort as ("10","2"). This restores the pre-retype empty-config byte-identity
        // and is a sensible order (by underlying sequence) for affixed types too. OriginalDocNumber stays display-only.
        return rows
            .OrderBy(x => x.Row.OriginalPartyGstin, StringComparer.Ordinal)
            .ThenBy(x => x.Seq)
            .Select(x => x.Row)
            .ToList();
    }

    /// <summary>Collapses a B2B list to one row per (party GSTIN, invoice number) — the LATEST by (date, then taxable /
    /// tax) — so one-amendment-per-document holds (a second re-statement replaces, never stacks). Deterministic.</summary>
    private static Dictionary<(string Gstin, string Number), Gstr1B2BRow> Latest(IReadOnlyList<Gstr1B2BRow> rows)
    {
        var map = new Dictionary<(string, string), Gstr1B2BRow>();
        foreach (var r in rows.OrderBy(r => r.InvoiceDate)
                     .ThenBy(r => r.TaxableValue.Amount)
                     .ThenBy(r => r.Cgst.Amount + r.Sgst.Amount + r.Igst.Amount))
        {
            if (r.PartyGstin is not { } gstin) continue; // a B2B row always carries a registered-party GSTIN (defensive)
            map[(gstin, r.InvoiceNumber)] = r; // ordered ascending ⇒ the last write per key is the latest version
        }
        return map;
    }

    /// <summary>Reads a voucher's posted forward tax by head (reverse-charge lines excluded), mirroring the GSTR-1 /
    /// GSTR-3B convention — never recomputed.</summary>
    private static (decimal Cgst, decimal Sgst, decimal Igst) ReadForwardHeads(Voucher voucher)
    {
        decimal cgst = 0m, sgst = 0m, igst = 0m;
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
}

/// <summary>
/// The <b>GSTR-3B correction advisory</b> view (Phase 9 slice 8b; RQ-29; DP-33; §2.16). There is <b>no direct amendment
/// of a filed GSTR-3B</b>: a correction flows either (1) <b>before</b> filing 3B through <b>GSTR-1A</b> (optional,
/// same tax period, once per period, cannot change GSTIN — live Aug-2024), or (2) <b>after</b> filing through the
/// <b>subsequent period's</b> return. From <b>Jul-2025 the 3B outward tables (3.1/3.2) are auto-populated from
/// GSTR-1/IFF/GSTR-1A and hard-locked</b> (non-editable). This view is therefore <b>advisory</b> — it surfaces the net
/// tax of the amendments of prior periods that are being declared in the current period (a correction of an earlier
/// period flowing through the current 3B), plus the mechanism text; it posts nothing and asserts no editable 3B. A
/// Composition / GST-off company yields a not-applicable projection (ER-13). Deterministic.
/// </summary>
public sealed record Gstr3bCorrectionAdvisory(
    DateOnly From,
    DateOnly To,
    bool Applicable,
    Money PriorPeriodCorrectionTax,
    Money PriorPeriodCorrectionTaxable,
    int CorrectionCount,
    string Mechanism)
{
    /// <summary>The advisory-mechanism note surfaced to the user (current-law GSTR-1A / subsequent-period / hard-lock).</summary>
    public const string MechanismNote =
        "A filed GSTR-3B is not directly amendable: correct before filing via GSTR-1A (same period, once), else in a "
        + "subsequent period's return. From Jul-2025 the 3B outward tables (3.1/3.2) are auto-populated and hard-locked.";

    /// <summary>True when there is a prior-period correction to declare in the current period (advisory).</summary>
    public bool RequiresCorrection => PriorPeriodCorrectionTax.Amount != 0m || PriorPeriodCorrectionTaxable.Amount != 0m;

    /// <summary>Builds the 3B-correction advisory for the current period <c>[from, to]</c>; a Composition / GST-off
    /// company yields a not-applicable projection (ER-13).</summary>
    public static Gstr3bCorrectionAdvisory Build(Company company, DateOnly from, DateOnly to)
    {
        ArgumentNullException.ThrowIfNull(company);

        if (!company.GstEnabled || company.Gst?.RegistrationType == GstRegistrationType.Composition)
            return new Gstr3bCorrectionAdvisory(from, to, false, Money.Zero, Money.Zero, 0, MechanismNote);

        // There is no direct amendment of a filed 3B: a correction of a prior period is declared in the CURRENT period's
        // return and flows through the current 3B. The correction is exactly the net tax of the GSTR-1 amendments of
        // prior periods declared now — the ordinary-invoice DIFFERENTIAL (9A) + the signed CDN adjustment (9C).
        var amend = Gstr1Amendments.Build(company, from, to);

        var tax = amend.Table9A.Sum(r => r.DifferentialTax.Amount) + amend.Table9C.Sum(r => r.RevisedTax.Amount);
        var taxable = amend.Table9A.Sum(r => r.DifferentialTaxableValue.Amount) + amend.Table9C.Sum(r => r.RevisedTaxableValue.Amount);
        var count = amend.Table9A.Count + amend.Table9C.Count;

        return new Gstr3bCorrectionAdvisory(from, to, true, new Money(tax), new Money(taxable), count, MechanismNote);
    }
}
