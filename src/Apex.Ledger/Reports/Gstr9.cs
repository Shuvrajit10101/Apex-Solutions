using System.Globalization;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;

namespace Apex.Ledger.Reports;

/// <summary>
/// <b>Form GSTR-9</b> — the regular taxpayer's <b>annual</b> return (Phase 9 slice 8a; RQ-17; DP-18). A pure, read-only
/// projection over an FY window <c>[fyFrom, fyTo]</c>, built <b>by construction</b> as the <b>Σ of the year's already-
/// rounded monthly (or quarterly, QRMP) GSTR-3B / GSTR-1</b> — never a whole-FY re-aggregation that would round
/// differently and silently break the foot to the monthly returns (the top risk; mirrors the <c>Gstr4</c> Σ-of-quarters
/// template). Every figure therefore reconciles to Σ(the year's <see cref="Gstr3b"/>) and Σ(the year's <see cref="Gstr1"/>)
/// to the paisa.
/// <list type="bullet">
///   <item><b>Part II Table 4</b> — outward + inward-RCM supplies on which tax IS payable (Σ 3B 3.1 + 3.1(d), net of §34).</item>
///   <item><b>Part II Table 5</b> — outward supplies on which tax is NOT payable (Σ 3B exempt/nil/non-GST). 5N turnover
///     is the anchor <see cref="Gstr9c"/> reconciles to the audited books.</item>
///   <item><b>Part III Table 6</b> — ITC availed (Σ 3B 4(A) forward + RCM ITC); Table 7 — ITC reversed, sub-bucketed by
///     rule (7A Rule 37/37A, 7C Rule 42, 7D Rule 43, 7E §17(5), 7H other) off the posted reversal tag; Table 8 — ITC
///     recon (8A from imported GSTR-2B, 8D = 8A − 8B, <b>reported, never forced to zero</b>).</item>
///   <item><b>Part IV Table 9</b> — tax paid: through ITC (Σ Rule-88A set-off Table 6.1) vs in cash (Σ PMT-06 challans).</item>
///   <item><b>Part VI Table 17</b> — HSN summary of outward supplies (Σ the year's <see cref="Gstr1.HsnSummary"/>).</item>
/// </list>
/// A Composition dealer (files 9A, <see cref="Gstr9a"/>) and a GST-off company yield a <b>not-applicable</b> (all-zero)
/// return, so ER-13 is automatic. Cess is ring-fenced in its own columns (ER-2). Deterministic, paisa-exact; posts and
/// persists nothing.
/// </summary>
public sealed record Gstr9(
    DateOnly From,
    DateOnly To,
    bool Applicable,
    string? Gstin,
    string LegalName)
{
    // ---- Part II Table 4 — outward + inward-RCM on which tax IS payable (Σ 3B, net of §34 CDN). Cess ring-fenced. ----
    /// <summary>Table 4 — forward outward CGST (Σ 3B 3.1(a), net of §34 credit/debit notes).</summary>
    public Money Table4Cgst { get; init; }
    /// <summary>Table 4 — forward outward SGST/UTGST.</summary>
    public Money Table4Sgst { get; init; }
    /// <summary>Table 4 — forward outward IGST.</summary>
    public Money Table4Igst { get; init; }
    /// <summary>Table 4 — forward outward Compensation Cess (ring-fenced, ER-2).</summary>
    public Money Table4Cess { get; init; }
    /// <summary>Table 4G — inward reverse-charge CGST (Σ 3B 3.1(d)).</summary>
    public Money Table4RcmCgst { get; init; }
    /// <summary>Table 4G — inward reverse-charge SGST/UTGST.</summary>
    public Money Table4RcmSgst { get; init; }
    /// <summary>Table 4G — inward reverse-charge IGST.</summary>
    public Money Table4RcmIgst { get; init; }
    /// <summary>Table 4G — inward reverse-charge Compensation Cess (ring-fenced, ER-2).</summary>
    public Money Table4RcmCess { get; init; }
    /// <summary>Table 4N — the taxable value of outward supplies on which tax is payable (Σ 3B taxable outward value).</summary>
    public Money Table4TaxableValue { get; init; }

    // ---- Part II Table 5 — outward on which tax is NOT payable (exempt / nil / non-GST). ----
    /// <summary>Table 5 — exempt / nil-rated / non-GST outward value (Σ 3B exempt bucket).</summary>
    public Money Table5ExemptNilNonGst { get; init; }

    // ---- Part III Table 6 — ITC availed during the FY (Σ 3B 4(A) forward + RCM ITC). Cess ring-fenced. ----
    /// <summary>Table 6 — ITC availed, CGST (forward 4(A)(5) + RCM 4(A)(3)).</summary>
    public Money Table6Cgst { get; init; }
    /// <summary>Table 6 — ITC availed, SGST/UTGST.</summary>
    public Money Table6Sgst { get; init; }
    /// <summary>Table 6 — ITC availed, IGST (forward + import 4(A)(2) + other RCM 4(A)(3)).</summary>
    public Money Table6Igst { get; init; }
    /// <summary>Table 6 — ITC availed, Compensation Cess (ring-fenced, ER-2).</summary>
    public Money Table6Cess { get; init; }
    /// <summary>Table 6H — ITC reclaimed (Σ 3B 4(D)(1) reclaim of an earlier reversal), GST heads.</summary>
    public Money Table6HReclaimed { get; init; }

    // ---- Part III Table 7 — ITC reversed + ineligible, sub-bucketed by rule (GST heads; cess ring-fenced separately). ----
    /// <summary>Table 7A — Rule 37 / 37A reclaimable reversals (GST heads, excl cess).</summary>
    public Money Table7Rule37 { get; init; }
    /// <summary>Table 7C — Rule 42 (inputs / input-services common credit) reversal.</summary>
    public Money Table7Rule42 { get; init; }
    /// <summary>Table 7D — Rule 43 (capital-goods common credit) reversal.</summary>
    public Money Table7Rule43 { get; init; }
    /// <summary>Table 7E — §17(5) blocked-credit reversal.</summary>
    public Money Table7Section17_5 { get; init; }
    /// <summary>Table 7H — other reversals (ineligible / §34 credit note).</summary>
    public Money Table7Other { get; init; }
    /// <summary>Table 7 — Compensation Cess reversed across all rules (ring-fenced, ER-2).</summary>
    public Money Table7Cess { get; init; }

    // ---- Part III Table 8 — ITC reconciliation (8A from GSTR-2B; 8D = 8A − 8B, reported). ----
    /// <summary>Table 8A — ITC as per the year's imported GSTR-2B (Σ the ITC-available lines' GST tax; cess separate).</summary>
    public Money Table8A { get; init; }
    /// <summary>Table 8A — Compensation Cess as per GSTR-2B (ring-fenced, ER-2).</summary>
    public Money Table8ACess { get; init; }

    // ---- Part IV Table 9 — tax paid (through ITC vs in cash). ----
    /// <summary>Table 9 — tax paid through ITC (Σ Rule-88A set-off Table 6.1 credit utilisation, S7 gst_setoff_lines).</summary>
    public Money Table9PaidThroughItc { get; init; }
    /// <summary>Table 9 — tax paid in cash (Σ PMT-06 deposits, S7 gst_challans).</summary>
    public Money Table9PaidInCash { get; init; }

    // ---- Part VI Table 17 — HSN summary of OUTWARD supplies (Σ the year's GSTR-1 HSN). ----
    /// <summary>Table 17 — the year's outward HSN summary (Σ each month's <see cref="Gstr1.HsnSummary"/>, merged by HSN).</summary>
    public IReadOnlyList<Gstr1HsnRow> Table17Hsn { get; init; } = [];

    // ---- Computed foot-checks / derived figures ----

    /// <summary>Table 4 total tax payable (forward outward + inward-RCM, GST heads; cess ring-fenced, ER-2).</summary>
    public Money Table4TotalTax => new(
        Table4Cgst.Amount + Table4Sgst.Amount + Table4Igst.Amount +
        Table4RcmCgst.Amount + Table4RcmSgst.Amount + Table4RcmIgst.Amount);

    /// <summary>Table 5N — total outward turnover (taxable + exempt/nil/non-GST); the anchor GSTR-9C reconciles to.</summary>
    public Money Table5NTurnover => new(Table4TaxableValue.Amount + Table5ExemptNilNonGst.Amount);

    /// <summary>Table 6 — total ITC availed across the GST heads (excl cess, ER-2).</summary>
    public Money Table6ItcAvailed => new(Table6Cgst.Amount + Table6Sgst.Amount + Table6Igst.Amount);

    /// <summary>Table 7 — total ITC reversed across the rule sub-buckets (GST heads, excl cess, ER-2).</summary>
    public Money Table7ItcReversed => new(
        Table7Rule37.Amount + Table7Rule42.Amount + Table7Rule43.Amount +
        Table7Section17_5.Amount + Table7Other.Amount);

    /// <summary>Table 8B — ITC availed as per GSTR-9 Table 6 (the return side of the 8A reconciliation).</summary>
    public Money Table8B => Table6ItcAvailed;

    /// <summary>Table 8D — the ITC-reconciliation difference (8A − 8B). <b>Reported, never forced to zero.</b></summary>
    public Money Table8D => new(Table8A.Amount - Table8B.Amount);

    /// <summary>Table 9 — total tax payable declared (= Table 4 total tax).</summary>
    public Money Table9Payable => Table4TotalTax;

    /// <summary>Net ITC per GSTR-9 (Table 6 availed − Table 7 reversed), consumed by GSTR-9C Table 12E.</summary>
    public Money NetItc => new(Table6ItcAvailed.Amount - Table7ItcReversed.Amount);

    /// <summary>Table 17 — Σ taxable value across the outward HSN rows.</summary>
    public Money Table17TaxableValue => new(Table17Hsn.Sum(h => h.TaxableValue.Amount));

    /// <summary>Table 17 — Σ tax across the outward HSN rows.</summary>
    public Money Table17TotalTax => new(Table17Hsn.Sum(h => h.TotalTax.Amount));

    /// <summary>
    /// Builds GSTR-9 for a regular company over the FY <c>[fyFrom, fyTo]</c>; a Composition dealer (files 9A) and a
    /// GST-off company yield a not-applicable (all-zero) return.
    /// </summary>
    public static Gstr9 Build(Company company, DateOnly fyFrom, DateOnly fyTo)
    {
        ArgumentNullException.ThrowIfNull(company);

        // A Composition dealer files 9A (not 9); a GST-off company has nothing to report ⇒ not-applicable (ER-13 auto).
        if (!company.GstEnabled || company.Gst?.RegistrationType == GstRegistrationType.Composition)
            return NotApplicable(company, fyFrom, fyTo);

        decimal outC = 0m, outS = 0m, outI = 0m;
        decimal rcmC = 0m, rcmS = 0m, rcmI = 0m, rcmCess = 0m;
        decimal taxableVal = 0m, exempt = 0m;
        decimal itcC = 0m, itcS = 0m, itcI = 0m, itcCess = 0m, reclaimed = 0m;
        var hsn = new Dictionary<string, HsnAcc>();

        // Σ-of-already-rounded-periods (fact 1): each month's (or quarter's) GSTR-3B / GSTR-1 is rounded once, then
        // added — so the annual figures CANNOT diverge from the monthly returns (the foot holds by construction).
        foreach (var (from, to) in PeriodWindows(company, fyFrom))
        {
            var b = Gstr3b.Build(company, from, to);
            outC += b.OutwardCgst.Amount; outS += b.OutwardSgst.Amount; outI += b.OutwardIgst.Amount;
            rcmC += b.RcmOutwardCgst.Amount; rcmS += b.RcmOutwardSgst.Amount;
            rcmI += b.RcmOutwardIgst.Amount; rcmCess += b.RcmOutwardCess.Amount;
            taxableVal += b.TaxableOutwardValue.Amount; exempt += b.ExemptNilNonGstOutward.Amount;
            // Table 6 ITC availed = forward 4(A)(5) + RCM 4(A)(2)/4(A)(3), per head (cess ring-fenced, ER-2).
            itcC += b.ItcCgst.Amount + b.RcmItcOtherCgst.Amount;
            itcS += b.ItcSgst.Amount + b.RcmItcOtherSgst.Amount;
            itcI += b.ItcIgst.Amount + b.RcmItcImportIgst.Amount + b.RcmItcOtherIgst.Amount;
            itcCess += b.RcmItcOtherCess.Amount;
            reclaimed += b.TotalItcReclaimed.Amount;

            foreach (var h in Gstr1.Build(company, from, to).HsnSummary)
            {
                if (!hsn.TryGetValue(h.HsnSac, out var acc))
                    hsn[h.HsnSac] = acc = new HsnAcc
                    {
                        HsnSac = h.HsnSac, Description = h.Description, Uqc = h.Uqc, BaseUqc = h.BaseCode,
                        DeclaredUnitId = h.DeclaredUnitId, BaseUnitId = h.BaseUnitId, MixedBases = h.MixedBases,
                    };

                // Table 17 sums the PERIOD rows, and a period row states its quantity in its OWN Uqc — which now
                // varies period to period (April billed in DOZ, May in NOS). Adding those as-is declared "7 DOZ"
                // = 84 Nos for a year in which 29 Nos moved: money right, quantity 2.9x overstated, on a MANDATORY
                // filed field. This is exactly the rule Gstr1 applies WITHIN a period — degrade to the base unit
                // whenever two rows are not commensurable — so the annual figure cannot contradict the monthlies
                // it is built from.
                //
                // Commensurability is decided on the rows' UNIT IDENTITY, never on the Uqc LABEL. "OTH" is the
                // department's CATCH-ALL for a unit absent from its master list, so an April row of Crates and a
                // May row of Pallets both carry "OTH": comparing labels finds them equal and files "8" for a year
                // in which 81 Nos moved. (The superseded comparison also treated null == null as agreement — two
                // unknown labels are not known to be the same unit.)
                //
                // As in Gstr1, this runs on the SEEDING iteration and therefore compares a row against ITSELF.
                // Load-bearing, not an oversight: AreCommensurable is deliberately NOT reflexive on a wholly
                // unknown pair, so a single period row carrying neither a DeclaredUnitId nor a Uqc flags itself and
                // degrades to the base declaration — where the degrade is the identity, so the figure is unmoved.
                if (!UqcResolver.AreCommensurable(acc.DeclaredUnitId, acc.Uqc, h.DeclaredUnitId, h.Uqc))
                    acc.MixedUnits = true;
                // Re-tested across periods AND carried forward; see Gstr1HsnRow.MixedBases.
                if (acc.BaseUnitId != h.BaseUnitId || h.MixedBases) acc.MixedBases = true;
                acc.BaseQuantity += h.BaseQuantity;
                acc.Quantity += h.Quantity;
                acc.Taxable += h.TaxableValue.Amount;
                acc.Cgst += h.Cgst.Amount; acc.Sgst += h.Sgst.Amount; acc.Igst += h.Igst.Amount;
            }
        }

        var rev = ReadTable7(company, fyFrom, fyTo);
        var (a8Gst, a8Cess) = ReadTable8A(company, fyFrom, fyTo);
        var (paidItc, paidCash) = ReadTable9(company, fyFrom, fyTo);

        var hsnRows = hsn.Values
            .OrderBy(h => h.HsnSac, StringComparer.Ordinal)
            .Select(h => new Gstr1HsnRow(h.HsnSac, h.Description,
                h.MixedUnits ? h.BaseUqc : h.Uqc,
                h.MixedUnits ? h.BaseQuantity : h.Quantity,
                new Money(h.Taxable), new Money(h.Cgst), new Money(h.Sgst), new Money(h.Igst),
                h.BaseQuantity, h.BaseUqc,
                h.MixedUnits ? h.BaseUnitId : h.DeclaredUnitId, h.BaseUnitId, h.MixedBases))
            .ToList();

        return new Gstr9(fyFrom, fyTo, true, company.Gst?.Gstin, company.Name)
        {
            Table4Cgst = new Money(outC), Table4Sgst = new Money(outS), Table4Igst = new Money(outI), Table4Cess = Money.Zero,
            Table4RcmCgst = new Money(rcmC), Table4RcmSgst = new Money(rcmS),
            Table4RcmIgst = new Money(rcmI), Table4RcmCess = new Money(rcmCess),
            Table4TaxableValue = new Money(taxableVal),
            Table5ExemptNilNonGst = new Money(exempt),
            Table6Cgst = new Money(itcC), Table6Sgst = new Money(itcS), Table6Igst = new Money(itcI), Table6Cess = new Money(itcCess),
            Table6HReclaimed = new Money(reclaimed),
            Table7Rule37 = new Money(rev.Rule37), Table7Rule42 = new Money(rev.Rule42), Table7Rule43 = new Money(rev.Rule43),
            Table7Section17_5 = new Money(rev.Section17_5), Table7Other = new Money(rev.Other), Table7Cess = new Money(rev.Cess),
            Table8A = new Money(a8Gst), Table8ACess = new Money(a8Cess),
            Table9PaidThroughItc = new Money(paidItc), Table9PaidInCash = new Money(paidCash),
            Table17Hsn = hsnRows,
        };
    }

    private static Gstr9 NotApplicable(Company company, DateOnly fyFrom, DateOnly fyTo) =>
        new(fyFrom, fyTo, false, company.Gst?.Gstin, company.Name);

    /// <summary>The FY's period sub-windows — 12 calendar months (or 4 quarters for a QRMP <c>Quarterly</c> filer) — over
    /// which GSTR-3B / GSTR-1 are built and their already-rounded figures summed (fact 1). The windows tile the FY exactly.</summary>
    private static IReadOnlyList<(DateOnly From, DateOnly To)> PeriodWindows(Company company, DateOnly fyFrom)
    {
        var quarterly = company.Gst?.Periodicity == GstReturnPeriodicity.Quarterly;
        var count = quarterly ? 4 : 12;
        var step = quarterly ? 3 : 1;
        var windows = new List<(DateOnly, DateOnly)>(count);
        for (var i = 0; i < count; i++)
            windows.Add((fyFrom.AddMonths(step * i), fyFrom.AddMonths(step * (i + 1)).AddDays(-1)));
        return windows;
    }

    /// <summary>Table 7 — Σ the posted ITC-reversal lines over the FY, sub-bucketed by rule via the
    /// <see cref="GstAdjustmentKind"/> tag (7A Rule 37/37A, 7C Rule 42, 7D Rule 43, 7E §17(5), 7H ineligible/§34 CN),
    /// GST heads with cess ring-fenced (ER-2). A reclaim (4(D)(1)) and a set-off / cash-payment (Table 6.1) are NOT
    /// Table-7 reversals ⇒ excluded — so the sub-buckets sum to Σ the year's <see cref="Gstr3b.TotalItcReversed"/>.</summary>
    private static (decimal Rule37, decimal Rule42, decimal Rule43, decimal Section17_5, decimal Other, decimal Cess) ReadTable7(
        Company company, DateOnly from, DateOnly to)
    {
        decimal r37 = 0m, r42 = 0m, r43 = 0m, s175 = 0m, other = 0m, cess = 0m;
        foreach (var v in company.Vouchers)
        {
            if (v.Date < from) continue;
            var type = company.FindVoucherType(v.TypeId);
            if (type is null || !LedgerBalances.CountsAsOf(v, to, type.BaseType)) continue;
            foreach (var line in v.Lines)
            {
                if (line.Gst is not { Adjustment: { } adj } g) continue;
                var amt = line.Amount.Amount;
                if (g.TaxHead == GstTaxHead.Cess)
                {
                    if (IsTable7Reversal(adj)) cess += amt; // ring-fenced (ER-2)
                    continue;
                }
                switch (adj)
                {
                    case GstAdjustmentKind.ReversalRule37 or GstAdjustmentKind.ReversalRule37A: r37 += amt; break;
                    case GstAdjustmentKind.ReversalRule42: r42 += amt; break;
                    case GstAdjustmentKind.ReversalRule43: r43 += amt; break;
                    case GstAdjustmentKind.ReversalSection17_5: s175 += amt; break;
                    case GstAdjustmentKind.ReversalIneligible or GstAdjustmentKind.ReversalCreditNote: other += amt; break;
                    // Reclaim (4(D)(1)) / SetOff / CashPayment (Table 6.1) are not Table-7 reversals.
                }
            }
        }
        return (r37, r42, r43, s175, other, cess);
    }

    private static bool IsTable7Reversal(GstAdjustmentKind adj) => adj is
        GstAdjustmentKind.ReversalRule37 or GstAdjustmentKind.ReversalRule37A or GstAdjustmentKind.ReversalRule42
        or GstAdjustmentKind.ReversalRule43 or GstAdjustmentKind.ReversalSection17_5
        or GstAdjustmentKind.ReversalIneligible or GstAdjustmentKind.ReversalCreditNote;

    /// <summary>Table 8A — Σ the ITC-available lines of the year's imported GSTR-2B snapshots (GST heads; cess ring-fenced,
    /// ER-2), taking the LATEST snapshot per return period (a re-import supersedes, never double-counts; ER-6). A 2A
    /// snapshot and a not-available line are excluded (the §16(2)(aa) gate lives in the portal 2B).</summary>
    private static (decimal Gst, decimal Cess) ReadTable8A(Company company, DateOnly from, DateOnly to)
    {
        // A re-import for the same return period creates a FRESH 2B snapshot (the old one untouched, ER-6) — so Table 8A
        // must take the LATEST snapshot per period (by ImportedAt), never SUM both, else a routine revision double-counts
        // the year's ITC. Dedup the FY-window 2B snapshots to one-per-period (latest ImportedAt wins; SourceFileHash
        // breaks an ImportedAt tie deterministically).
        var latestPerPeriod = company.Gstr2bSnapshots
            .Where(s => s.StatementType == GstStatementType.Gstr2b && PeriodInWindow(s.ReturnPeriod, from, to))
            .GroupBy(s => s.ReturnPeriod, StringComparer.Ordinal)
            .Select(g => g
                .OrderByDescending(s => s.ImportedAt)
                .ThenByDescending(s => s.SourceFileHash, StringComparer.Ordinal)
                .First());

        decimal gst = 0m, cess = 0m;
        foreach (var snap in latestPerPeriod)
            foreach (var line in snap.Lines)
            {
                if (!line.ItcAvailable) continue;
                gst += (line.IgstPaisa + line.CgstPaisa + line.SgstPaisa) / 100m;
                cess += line.CessPaisa / 100m;
            }
        return (gst, cess);
    }

    /// <summary>Table 9 — tax paid through ITC (Σ the non-cash Rule-88A set-off Table 6.1 lines whose period falls in the
    /// FY) vs in cash (Σ the PMT-06 challan deposits made in the FY). Reads the S7 audit rows; recomputes nothing.</summary>
    private static (decimal PaidItc, decimal PaidCash) ReadTable9(Company company, DateOnly from, DateOnly to)
    {
        decimal paidItc = 0m;
        foreach (var l in company.GstSetoffLines)
            if (!l.IsCash && PeriodInWindow(l.Period, from, to))
                paidItc += l.AmountPaisa / 100m;

        // Tax-paid-IN-CASH counts ONLY the Tax minor-head PMT-06 challans — an interest / late-fee / penalty deposit is
        // NOT tax paid (DP-34: those belong in their own Table-9 rows / are flag-only), so summing every minor head would
        // overstate the year's tax-in-cash. Cess stays ring-fenced by the same (Tax) filter (ER-2).
        decimal paidCash = 0m;
        foreach (var ch in company.GstChallans)
            if (ch.MinorHead == GstMinorHead.Tax && ch.DepositDate >= from && ch.DepositDate <= to)
                paidCash += ch.Amount.Amount;

        return (paidItc, paidCash);
    }

    /// <summary>True iff a "yyyy-MM" return period falls within the FY window <c>[from, to]</c> (invariant-culture parse).</summary>
    private static bool PeriodInWindow(string period, DateOnly from, DateOnly to)
    {
        if (period.Length < 7
            || !int.TryParse(period.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var year)
            || !int.TryParse(period.AsSpan(5, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var month)
            || month is < 1 or > 12)
            return false;
        var first = new DateOnly(year, month, 1);
        return first >= new DateOnly(from.Year, from.Month, 1) && first <= to;
    }

    private sealed class HsnAcc
    {
        public string HsnSac = "";
        public string Description = "";
        public string? Uqc;
        /// <summary>The item's base-unit UQC — the label <see cref="BaseQuantity"/> carries.</summary>
        public string? BaseUqc;
        /// <summary>Σ of every period row's base-unit quantity; the commensurable fallback when
        /// <see cref="MixedUnits"/> makes <see cref="Quantity"/> unsummable. Accumulated unconditionally.</summary>
        public decimal BaseQuantity;
        /// <summary>The identity of the unit <see cref="Quantity"/> counts — what commensurability is decided on.</summary>
        public Guid? DeclaredUnitId;
        /// <summary>The identity of the unit <see cref="BaseQuantity"/> counts.</summary>
        public Guid? BaseUnitId;
        /// <summary>Set when two period rows of this HSN were not commensurable.</summary>
        public bool MixedUnits;
        /// <summary>Set when the folded period rows span unrelated BASE units; see <see cref="Gstr1HsnRow.MixedBases"/>.</summary>
        public bool MixedBases;
        public decimal Quantity;
        public decimal Taxable;
        public decimal Cgst;
        public decimal Sgst;
        public decimal Igst;
    }
}
