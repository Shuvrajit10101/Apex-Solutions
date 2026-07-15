using System.Text;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;

namespace Apex.Ledger.Services;

/// <summary>
/// Reconciles an imported <see cref="Gstr2bSnapshot"/> against the booked purchase register (Phase 9 slice 6; RQ-13;
/// DP-13/DP-15). A <b>pure, deterministic, side-effect-free</b> engine — it takes a <see cref="Company"/> + a snapshot
/// and returns a <see cref="Gstr2bReconciliationReport"/>. It has <b>no posting surface</b> (no <c>LedgerService</c>, no
/// journal, emits no <c>EntryLine</c>): the ADVISORY-only guarantee is the <b>structural absence</b> of any posting path
/// (ER-14, mirroring how <c>EInvoiceRecord</c> has no IRN-minting method). The reconciler never mutates the company.
/// <para>
/// <b>Match key</b> (§2.3): normalised supplier GSTIN + normalised supplier doc-no (from the purchase's bill-wise ref,
/// the books have no first-class supplier-invoice field) + a date window + a value/tax tolerance. Matching runs in
/// <b>two passes</b> so a clean doc-no key is preferred (finding #1): pass 1 pairs ONLY where BOTH sides carry a doc-no
/// and it is equal (Matched or, if the value differs beyond tolerance, PartialMismatch); pass 2 then does the doc-LESS
/// fallback (GSTIN + date-window + value/tax) over the still-unconsumed vouchers, ALWAYS demoted to PartialMismatch so
/// the user verifies it. Without the two passes a doc-less value/date-twin could greedily steal the pairing from the
/// exact-doc-no voucher — downgrading a clean Matched AND emitting the real match as InBooksOnly (a spurious S7
/// ITC-reversal candidate). <b>RCM-tagged inward is EXCLUDED symmetrically</b> — from the books register AND from the
/// portal lines (a reverse-charge 2B line is handled by the S2a self-invoice path, not surfaced as "supplier filed, you
/// didn't book"; finding #2) — and composition takes no ITC (empty register, §2.7). Each pass is greedy + stable
/// (deterministic, source-derived ordering ⇒ one 2B line matches at most one voucher and vice-versa).
/// </para>
/// </summary>
public static class Gstr2bReconciler
{
    /// <summary>
    /// Builds the advisory reconciliation report for one snapshot over <c>[from, to]</c> with the given tolerance.
    /// Deterministic and pure (ER-14) — posts nothing, mutates nothing.
    /// </summary>
    public static Gstr2bReconciliationReport Reconcile(
        Company company, Gstr2bSnapshot snapshot, DateOnly from, DateOnly to, ReconTolerance tolerance)
    {
        ArgumentNullException.ThrowIfNull(company);
        ArgumentNullException.ThrowIfNull(snapshot);

        var books = BuildBooksRegister(company, from, to);          // deterministically ordered; RCM + composition excluded
        var consumed = new bool[books.Count];

        var matched = new List<ReconMatch>();
        var partial = new List<ReconMatch>();
        var portalOnly = new List<Gstr2bLine>();

        // Consider the 2B lines in a fixed, SOURCE-DERIVED order so the greedy pairing is reproducible across re-imports
        // (finding #4). The old tiebreak was the per-line random Guid (Id), so a doc-no/date/value collision paired
        // non-deterministically; tiebreak instead on the doc number / type / value carried by the portal file itself.
        var portalLines = snapshot.Lines
            .Where(l => !l.ReverseCharge)   // finding #2: RCM inward is excluded portal-side too (mirror the books register)
            .OrderBy(l => l.SupplierGstin, StringComparer.Ordinal)
            .ThenBy(l => l.DocNumberNorm ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(l => l.DocDate)
            .ThenBy(l => l.DocNumber, StringComparer.Ordinal)
            .ThenBy(l => (int)l.DocType)
            .ThenBy(l => l.TaxableValuePaisa)
            .ThenBy(l => l.TotalTaxPaisa)
            .ToList();
        var lineMatched = new bool[portalLines.Count];

        // Records a pairing into the right bucket (signed portal − books variances, cess folded into the tax variance).
        void Record(Gstr2bLine line, BooksEntry b, ReconBucket bucket)
        {
            var m = new ReconMatch(line, b.VoucherId, line.TaxableValuePaisa - b.TaxableValuePaisa,
                line.TotalTaxPaisa - b.TotalTaxPaisa);
            if (bucket == ReconBucket.Matched) matched.Add(m); else partial.Add(m);
        }

        // Pass 1 — the CLEAN doc-no key: both sides carry a doc-no AND it is equal (finding #1). This wins globally over
        // the doc-less fallback so an exact-doc-no voucher is never stolen by a value/date twin.
        for (var pi = 0; pi < portalLines.Count; pi++)
        {
            var line = portalLines[pi];
            var lineGstin = NormaliseGstin(line.SupplierGstin);
            for (var i = 0; i < books.Count; i++)
            {
                if (consumed[i]) continue;
                var b = books[i];
                if (!string.Equals(b.GstinNorm, lineGstin, StringComparison.Ordinal)) continue;

                var (isMatch, bucket) = ClassifyByDocNo(line, b, tolerance);
                if (!isMatch) continue;

                consumed[i] = true;
                lineMatched[pi] = true;
                Record(line, b, bucket);
                break;
            }
        }

        // Pass 2 — the doc-LESS fallback over the still-unconsumed vouchers: at least one side has no recoverable doc-no,
        // so key on GSTIN + date-window + value/tax, ALWAYS demoted to PartialMismatch (§2.3). A line that matches nothing
        // here is genuinely InPortalOnly.
        for (var pi = 0; pi < portalLines.Count; pi++)
        {
            if (lineMatched[pi]) continue;
            var line = portalLines[pi];
            var lineGstin = NormaliseGstin(line.SupplierGstin);
            var found = false;
            for (var i = 0; i < books.Count; i++)
            {
                if (consumed[i]) continue;
                var b = books[i];
                if (!string.Equals(b.GstinNorm, lineGstin, StringComparison.Ordinal)) continue;
                if (!ClassifyFallback(line, b, tolerance)) continue;

                consumed[i] = true;
                lineMatched[pi] = true;
                Record(line, b, ReconBucket.PartialMismatch);
                found = true;
                break;
            }
            if (!found) portalOnly.Add(line);
        }

        var inBooksOnly = new List<ReconBooksEntry>();
        for (var i = 0; i < books.Count; i++)
            if (!consumed[i]) inBooksOnly.Add(books[i].ToReportEntry());

        return new Gstr2bReconciliationReport(snapshot, matched, partial, portalOnly, inBooksOnly);
    }

    /// <summary>Pass 1 (finding #1): a <b>clean doc-no key</b> — BOTH sides carry a supplier doc-no, the (normalised)
    /// doc-nos are equal, and the date is within the window. The value/tax tolerance then splits Matched vs
    /// PartialMismatch. Preferring this key stops a doc-less value/date twin from stealing the pairing from the
    /// exact-doc-no voucher.</summary>
    private static (bool IsMatch, ReconBucket Bucket) ClassifyByDocNo(Gstr2bLine line, BooksEntry b, ReconTolerance tol)
    {
        if (line.DocNumberNorm is not { } ln || b.DocNoNorm is not { } bn) return (false, default);
        if (!string.Equals(ln, bn, StringComparison.Ordinal)) return (false, default);
        if (!DateWithin(line.DocDate, b.Date, tol.DateWindowDays)) return (false, default);
        return (true, WithinValue(line, b, tol) ? ReconBucket.Matched : ReconBucket.PartialMismatch);
    }

    /// <summary>Pass 2 (§2.3): the <b>doc-less fallback</b> — at least one side has no recoverable supplier doc-no, so key
    /// on GSTIN (already equal) + date-window + value/tax within tolerance, ALWAYS demoted to PartialMismatch so the user
    /// verifies the ref-less match. Two entries that BOTH carry a doc-no are deliberately excluded here — only pass 1 may
    /// pair them (a mismatched doc-no is never a fuzzy match).</summary>
    private static bool ClassifyFallback(Gstr2bLine line, BooksEntry b, ReconTolerance tol)
    {
        if (line.DocNumberNorm is not null && b.DocNoNorm is not null) return false;
        return DateWithin(line.DocDate, b.Date, tol.DateWindowDays) && WithinValue(line, b, tol);
    }

    /// <summary>Whether the taxable value AND the total tax (cess folded in) are both within the paisa tolerance.</summary>
    private static bool WithinValue(Gstr2bLine line, BooksEntry b, ReconTolerance tol) =>
        Math.Abs(line.TaxableValuePaisa - b.TaxableValuePaisa) <= tol.ValueTolerancePaisa &&
        Math.Abs(line.TotalTaxPaisa - b.TotalTaxPaisa) <= tol.ValueTolerancePaisa;

    private static bool DateWithin(DateOnly portalDate, DateOnly booksDate, int windowDays) =>
        Math.Abs(portalDate.DayNumber - booksDate.DayNumber) <= windowDays;

    // ---- books-side purchase register ----

    /// <summary>The books-side inward register over <c>[from, to]</c>: posted Purchase/Debit-Note vouchers carrying at
    /// least one <b>forward</b> (non-RCM) GST line, keyed on the supplier GSTIN (B2C purchases with no GSTIN can never
    /// match a 2B line, so they are excluded). A composition dealer has no ITC ⇒ an empty register. Deterministically
    /// ordered so the greedy pass is reproducible.</summary>
    private static List<BooksEntry> BuildBooksRegister(Company company, DateOnly from, DateOnly to)
    {
        var register = new List<BooksEntry>();
        // Composition dealers take no ITC — there is no inward register to reconcile against (§2.7).
        if (company.Gst?.RegistrationType == GstRegistrationType.Composition) return register;

        foreach (var (voucher, _) in GstReportSupport.PostedGstVouchers(company, from, to, GstTaxDirection.Input))
        {
            // Exclude a purely reverse-charge purchase: RCM inward bypasses 2B/IMS (§2.7). A voucher with no forward
            // (non-RCM) GST line never enters the reconcilable register (risk #6).
            if (!GstReportSupport.HasForwardTaxLines(voucher)) continue;

            var gstin = SupplierGstinOf(company, voucher);
            if (string.IsNullOrWhiteSpace(gstin)) continue; // B2C / unregistered purchase — cannot appear in 2B

            var docNoRaw = SupplierDocNumberOf(company, voucher);
            var taxable = ToPaisa(GstReportSupport.InvoiceTaxableValue(voucher));
            var tax = ToPaisa(GstReportSupport.PostedForwardTaxTotal(voucher)) + ToPaisa(GstReportSupport.PostedCessTotal(voucher));

            register.Add(new BooksEntry(
                voucher.Id, gstin!, NormaliseGstin(gstin!), docNoRaw, NormaliseDocNo(docNoRaw), voucher.Date, taxable, tax));
        }

        // Deterministic order (GSTIN, doc-no, date, voucher id) so a consumed voucher can never re-match and two
        // identical candidates resolve to the lower voucher id (risk #2).
        register.Sort((a, b) =>
        {
            var c = string.CompareOrdinal(a.GstinNorm, b.GstinNorm);
            if (c != 0) return c;
            c = string.CompareOrdinal(a.DocNoNorm ?? string.Empty, b.DocNoNorm ?? string.Empty);
            if (c != 0) return c;
            c = a.Date.CompareTo(b.Date);
            return c != 0 ? c : a.VoucherId.CompareTo(b.VoucherId);
        });
        return register;
    }

    /// <summary>The supplier GSTIN for a purchase — the party (creditor) ledger's recorded GSTIN, or <c>null</c> for a
    /// B2C/unregistered supplier.</summary>
    private static string? SupplierGstinOf(Company company, Voucher voucher) =>
        voucher.PartyId is Guid pid ? company.FindLedger(pid)?.PartyGst?.Gstin : null;

    /// <summary>The supplier's document number for a purchase, read from the party/creditor line's <b>bill-wise ref</b>
    /// (<see cref="BillAllocation.Name"/> on a New/Agst allocation — the canonical home for the supplier's invoice
    /// reference; the <c>Voucher</c> header has no first-class supplier-invoice field, §2.3). Falls back to the voucher
    /// <see cref="Voucher.Narration"/> when no bill-wise ref exists; if that is also absent the doc-no is <c>null</c> and
    /// the reconciler demotes any match to PartialMismatch. <b>Choice (§2.3):</b> the voucher NUMBER is deliberately NOT
    /// used as a last resort — it is the recipient's own series, not the supplier's ref, so it would never match a 2B
    /// doc-no and would only pollute the key.</summary>
    private static string? SupplierDocNumberOf(Company company, Voucher voucher)
    {
        if (voucher.PartyId is Guid pid)
        {
            var partyLine = voucher.Lines.FirstOrDefault(l => l.LedgerId == pid);
            var billRef = partyLine?.BillAllocations
                .Where(a => a.RefType is BillRefType.NewRef or BillRefType.AgstRef)
                .Select(a => a.Name)
                .FirstOrDefault(n => !string.IsNullOrWhiteSpace(n));
            if (!string.IsNullOrWhiteSpace(billRef)) return billRef;
        }
        return string.IsNullOrWhiteSpace(voucher.Narration) ? null : voucher.Narration;
    }

    // ---- normalisation ----

    /// <summary>Normalises a GSTIN for matching: upper-cased + trimmed (ordinal).</summary>
    public static string NormaliseGstin(string gstin) => gstin.Trim().ToUpperInvariant();

    /// <summary>Normalises a supplier doc-no for matching (§2.3): upper-case, strip every non-alphanumeric, then trim
    /// leading zeros (the portal doc-no is case-insensitive + formatting-noisy). <c>null</c>/blank → <c>null</c>; an
    /// all-zero/all-symbol ref collapses to <c>null</c> (no usable key).</summary>
    public static string? NormaliseDocNo(string? docNo)
    {
        if (string.IsNullOrWhiteSpace(docNo)) return null;
        var sb = new StringBuilder(docNo.Length);
        foreach (var ch in docNo)
            if (char.IsLetterOrDigit(ch)) sb.Append(char.ToUpperInvariant(ch));
        var alnum = sb.ToString();
        if (alnum.Length == 0) return null;
        var trimmed = alnum.TrimStart('0');
        return trimmed.Length == 0 ? null : trimmed;
    }

    private static long ToPaisa(Money money) => (long)Math.Round(money.Amount * 100m, MidpointRounding.AwayFromZero);

    /// <summary>A books-register entry (internal; the report surfaces <see cref="ReconBooksEntry"/>).</summary>
    private readonly record struct BooksEntry(
        Guid VoucherId, string Gstin, string GstinNorm, string? DocNoRaw, string? DocNoNorm, DateOnly Date,
        long TaxableValuePaisa, long TotalTaxPaisa)
    {
        public ReconBooksEntry ToReportEntry() =>
            new(VoucherId, Gstin, DocNoRaw, Date, TaxableValuePaisa, TotalTaxPaisa);
    }
}
