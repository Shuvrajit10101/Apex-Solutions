using Apex.Ledger.Domain;
using Apex.Ledger.Services;

namespace Apex.Ledger.Reports;

/// <summary>
/// <b>Form GSTR-9C</b> — the self-certified <b>reconciliation statement</b> (Phase 9 slice 8a; RQ-17; DP-18) that
/// reconciles the annual return (<see cref="Gstr9"/>) turnover, tax and ITC to the <b>audited annual financial
/// statements</b> (the books). Self-certified since FY2020-21 (no CA/CMA attestation); applicability AATO &gt; ₹5 cr —
/// treated here as <b>advisory</b> (the app has no turnover-history gate), so it is produced for any regular GST company.
/// <para>
/// The two sides are computed <b>independently</b> — the books (the FY-window <see cref="ProfitAndLoss.TotalIncome"/> +
/// the FY gross-accrual tax-ledger legs) versus the return (<see cref="Gstr9"/>) — and the <b>unreconciled difference is
/// COMPUTED AND SHOWN, never forced to zero</b> (that transparent delta is the whole deliverable; the accrual books and
/// the posted GST lines legitimately differ on Schedule-III supplies, non-GST income, timing). The "per books" anchors
/// are FY-WINDOW GROSS ACCRUAL FLOWS — the P&amp;L revenue and the Output-credit / Input-debit legs over <c>[From, To]</c>
/// — NOT from-inception net closings (which prior-FY residuals inflate and the Rule-88A set-off / PMT-06 discharge drain).
/// </para>
/// <list type="bullet">
///   <item><b>Part A Table 5</b> — gross-turnover recon: 5A books turnover → 5Q return turnover → <b>5R unreconciled</b>.</item>
///   <item><b>Part III Tables 9–11</b> — tax recon: tax per return vs tax per books → <b>11 unreconciled tax</b>.</item>
///   <item><b>Part B Table 12</b> — net-ITC recon: 12A ITC per books → 12E ITC per GSTR-9 → <b>12F unreconciled</b>.</item>
/// </list>
/// A Composition dealer and a GST-off company (e.g. the accounts-only Robert fixture) yield a not-applicable statement.
/// Deterministic, paisa-exact; posts and persists nothing.
/// </summary>
public sealed record Gstr9c(
    DateOnly From,
    DateOnly To,
    bool Applicable,
    string? Gstin,
    string LegalName)
{
    // ---- Part A Table 5 — reconciliation of gross turnover. ----
    /// <summary>5A — turnover as per the audited financial statements (the FY-window P&amp;L total income anchor).</summary>
    public Money Table5ABooksTurnover { get; init; }
    /// <summary>5Q — turnover as declared in the annual return (GSTR-9 Table 5N total turnover).</summary>
    public Money Table5QReturnTurnover { get; init; }

    // ---- Part III Tables 9–11 — reconciliation of rate-wise tax liability. ----
    /// <summary>Tax payable as per GSTR-9 (Table 9 payable = Table 4 total tax).</summary>
    public Money Table9TaxPerReturn { get; init; }
    /// <summary>Tax payable as per the books (Σ the Output tax-ledger CREDIT legs over the FY — output tax charged).</summary>
    public Money Table9TaxPerBooks { get; init; }

    // ---- Part B Table 12 — reconciliation of net ITC. ----
    /// <summary>12A — ITC availed as per the audited books (Σ the Input tax-ledger DEBIT legs over the FY — ITC availed).</summary>
    public Money Table12ABooksItc { get; init; }
    /// <summary>12E — net ITC as per GSTR-9 (Table 6 availed − Table 7 reversed).</summary>
    public Money Table12EReturnItc { get; init; }

    // ---- The unreconciled-difference lines — COMPUTED AND SHOWN, never forced to zero. ----

    /// <summary>5R — the unreconciled gross-turnover difference (5A books − 5Q return). Reported, never forced to zero.</summary>
    public Money Table5RUnreconciledTurnover => new(Table5ABooksTurnover.Amount - Table5QReturnTurnover.Amount);

    /// <summary>Table 11 — the unreconciled tax difference (return − books). Reported, never forced to zero.</summary>
    public Money Table11UnreconciledTax => new(Table9TaxPerReturn.Amount - Table9TaxPerBooks.Amount);

    /// <summary>12F — the unreconciled net-ITC difference (12A books − 12E return). Reported, never forced to zero.</summary>
    public Money Table12FUnreconciledItc => new(Table12ABooksItc.Amount - Table12EReturnItc.Amount);

    /// <summary>
    /// Builds GSTR-9C for a regular company over the FY <c>[fyFrom, fyTo]</c>, reconciling GSTR-9 to the books; a
    /// Composition dealer and a GST-off company yield a not-applicable statement.
    /// </summary>
    public static Gstr9c Build(Company company, DateOnly fyFrom, DateOnly fyTo)
    {
        ArgumentNullException.ThrowIfNull(company);

        // 9C reconciles a regular taxpayer's GSTR-9 to the audited books; a Composition dealer and a GST-off company
        // (e.g. accounts-only Robert) yield a not-applicable statement (ER-13 auto).
        if (!company.GstEnabled || company.Gst?.RegistrationType == GstRegistrationType.Composition)
            return NotApplicable(company, fyFrom, fyTo);

        // The two sides — computed INDEPENDENTLY (fact 2): the return (GSTR-9) and the books (P&L income + the Input /
        // Output tax-ledger closings). The unreconciled differences are the record's computed properties (never forced).
        var g9 = Gstr9.Build(company, fyFrom, fyTo);

        // 5A "books turnover" is the FY-WINDOW revenue, NOT the cumulative books-begin→To income: the return side (5Q)
        // is strictly FY-scoped, so a prior-FY sale must not inflate the books side (else 5R is spuriously non-zero for a
        // perfectly reconciled multi-year company). Use the windowed P&L overload over [fyFrom, fyTo].
        var books = ProfitAndLoss.Build(company, fyTo, new ReportOptions { Period = new PeriodRange(fyFrom, fyTo) });
        var gst = new GstService(company);

        // The tax/ITC "per books" anchors must be the FY GROSS ACCRUAL flows, NOT the from-inception NET closing
        // balances: a Rule-88A set-off (Dr Output / Cr Input) and a PMT-06 cash discharge (Dr Output / Cr Cash) draw the
        // net closings toward zero, so a normally-discharged company would spuriously show Table 11 = the full liability
        // and Table 12F = −(the full ITC). We instead sum only the ACCRUAL-direction legs over [fyFrom, fyTo]: the Output
        // ledgers' CREDIT legs (output tax CHARGED) and the Input ledgers' DEBIT legs (ITC AVAILED). A net movement would
        // still be wrong (it nets out the discharge legs) — so the leg-sum is directional. This leaves the unreconciled
        // lines surfacing only GENUINE book-vs-return differences, never the routine discharge.

        // FY-windowed directional leg-sum over a tax ledger: Σ the line amounts on it whose DrCr side matches `side`,
        // counted over [fyFrom, fyTo] (opening balance + prior-FY vouchers excluded). Isolates the accrual legs from the
        // opposite-side set-off / payment discharge legs.
        decimal LegSum(Domain.Ledger ledger, DrCr side)
        {
            var sum = 0m;
            foreach (var v in company.Vouchers)
            {
                if (v.Date < fyFrom) continue;
                var type = company.FindVoucherType(v.TypeId);
                if (type is null || !LedgerBalances.CountsAsOf(v, fyTo, type.BaseType)) continue;
                foreach (var line in v.Lines)
                    if (line.LedgerId == ledger.Id && line.Side == side)
                        sum += line.Amount.Amount;
            }
            return sum;
        }

        // ITC per books = Σ the Input {head} DEBIT legs over the FY (ITC availed); the set-off's Cr Input utilisation leg
        // is on the opposite side, so it is excluded and never nets the anchor down.
        decimal InputAccrual(GstTaxHead head)
        {
            var l = gst.FindTaxLedger(head, GstTaxDirection.Input);
            return l is null ? 0m : LegSum(l, DrCr.Debit);
        }

        // Tax per books = Σ the Output {head} CREDIT legs over the FY (output tax charged); the set-off's Dr Output and
        // the cash discharge's Dr Output legs are on the opposite side, so they are excluded.
        decimal OutputAccrual(GstTaxHead head)
        {
            var l = gst.FindTaxLedger(head, GstTaxDirection.Output);
            return l is null ? 0m : LegSum(l, DrCr.Credit);
        }

        var booksItc = InputAccrual(GstTaxHead.Central) + InputAccrual(GstTaxHead.State) + InputAccrual(GstTaxHead.Integrated);
        var booksTax = OutputAccrual(GstTaxHead.Central) + OutputAccrual(GstTaxHead.State) + OutputAccrual(GstTaxHead.Integrated);

        return new Gstr9c(fyFrom, fyTo, true, company.Gst?.Gstin, company.Name)
        {
            Table5ABooksTurnover = books.TotalIncome,      // 5A — audited P&L revenue (the books anchor)
            Table5QReturnTurnover = g9.Table5NTurnover,    // 5Q — GSTR-9 total turnover
            Table9TaxPerReturn = g9.Table4TotalTax,        // tax per GSTR-9 (Table 9 payable)
            Table9TaxPerBooks = new Money(booksTax),       // tax per books (Output-ledger postings)
            Table12ABooksItc = new Money(booksItc),        // 12A — ITC per books (Input-ledger closings)
            Table12EReturnItc = g9.NetItc,                 // 12E — net ITC per GSTR-9 (6 − 7)
        };
    }

    private static Gstr9c NotApplicable(Company company, DateOnly fyFrom, DateOnly fyTo) =>
        new(fyFrom, fyTo, false, company.Gst?.Gstin, company.Name);
}
