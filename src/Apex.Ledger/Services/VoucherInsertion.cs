using System;
using System.Collections.Generic;
using System.Linq;
using Apex.Ledger.Domain;

namespace Apex.Ledger.Services;

/// <summary>
/// One voucher whose number a pending insertion would REWRITE: <paramref name="VoucherId"/> moves from
/// <paramref name="From"/> to <paramref name="To"/>.
/// </summary>
/// <param name="VoucherId">The already-posted voucher being renumbered.</param>
/// <param name="From">The number it carries today.</param>
/// <param name="To">The number it would carry after the insertion.</param>
public readonly record struct VoucherRenumber(Guid VoucherId, int From, int To);

/// <summary>
/// What an <b>Insert Voucher</b> (Alt+I) would do to a voucher type's number series — computed BEFORE anything is
/// posted, so the shell can refuse, warn, or proceed with the consequences named.
/// </summary>
/// <param name="Refusal">A named sentence when the insertion must not proceed, else <c>null</c>.</param>
/// <param name="InsertedNumber">The number the new voucher will carry. 0 under
/// <see cref="NumberingMethod.None"/> (that method assigns no numbers) and under
/// <see cref="NumberingMethod.Manual"/> (the operator types it).</param>
/// <param name="Renumbers">Every already-posted voucher whose number this insertion rewrites, in ascending
/// order. EMPTY under every method except <see cref="NumberingMethod.Automatic"/> and
/// <see cref="NumberingMethod.MultiUserAuto"/>, and empty even under those two when the insertion lands after
/// the last voucher of the type (a plain append).</param>
/// <param name="Method">The numbering method this plan was computed under — carried so the shell can explain
/// itself to the operator without re-deriving it.</param>
public sealed record VoucherInsertionPlan(
    string? Refusal,
    int InsertedNumber,
    IReadOnlyList<VoucherRenumber> Renumbers,
    NumberingMethod Method)
{
    /// <summary>True when this plan may be applied at all.</summary>
    public bool IsAllowed => Refusal is null;

    /// <summary>True when applying this plan rewrites at least one already-posted voucher's number.</summary>
    public bool RewritesExistingNumbers => Renumbers.Count > 0;
}

/// <summary>
/// <b>Insert Voucher (Alt+I) — the number-series half</b> (census row 5.5).
///
/// <para><b>What the vendor attests</b> (R7 / ruling 14 tier 1,
/// <c>help.tallysolutions.com/tally-prime/accounting-financial-reports/day-book-tally/</c>, fetched 2026-09-08).
/// The verb and the chord, verbatim: <i>"If you want to add a transaction in the exact chronological order of the
/// voucher type for the proper sequencing of the voucher number, then you can insert it directly in the Day
/// Book"</i>; <i>"Select the entry above which you want to insert the transaction, press <b>Alt+I</b> (Insert
/// Vch)"</i>. And the consequence that makes this more than a second name for Add Voucher: <i>"If you insert a
/// voucher between two vouchers with the method of voucher numbering set to automatic, then all the existing
/// vouchers of that particular type recorded on all subsequent days are renumbered."</i> The worked example is
/// quoted verbatim on <see cref="Plan"/>.</para>
///
/// <para>🔴 <b>WHICH METHODS RENUMBER, AND WHY THE OTHER THREE DO NOT.</b> This is NOT a judgement call — the
/// vendor scopes it (<c>help.tallysolutions.com/tally-prime/company-features/use-voucher-numbering-methods/</c>,
/// re-fetched and re-verified 2026-09-08). The insertion/deletion choice is offered under <b>Automatic</b> —
/// <i>"you can select the <b>Numbering behaviour on the insertion/deletion</b> to <b>Renumber Vouchers</b> or
/// <b>Retain Original Voucher No.</b>"</i> — and the page offers the same choice again in its <b>Multi-user
/// Auto</b> section. It is withheld from the other two: <i>"Note: This option will not be available if you have
/// selected 'Manual' or 'None' as the method of voucher numbering."</i></para>
///
/// <para>🔴 <b>ONE CITATION WAS WEAKENED ON 2026-09-08 RATHER THAN LEFT STANDING.</b> An earlier draft of this
/// paragraph quoted the scoping as the verbatim string <i>"applicable, only if you have selected Automatic or
/// Multi-user Auto numbering method"</i>. Re-fetching the page did NOT reproduce that sentence. The SUBSTANCE
/// survived the re-check — both methods are offered the setting, Manual and None are refused it, and the two
/// quotes retained above are the ones that came back word for word — so no behaviour changes here; only the
/// claim to be quoting is withdrawn. An unverifiable verbatim quote in shipped code is a defect class this
/// project has already had to strip out once, and it is not re-introduced by leaving a comfortable sentence in
/// place.</para>
///
/// <para>So, per method:
/// <list type="bullet">
/// <item><b>Automatic</b> and <b>Multi-user Auto</b> — the inserted voucher TAKES the number of the first voucher
/// of its type at or after the insertion point, and that voucher and every later one of the same type shift up by
/// one. <b>No gap and no duplicate</b>: the map <c>n → n+1</c> over a contiguous tail is injective and leaves the
/// vacated number to the newcomer.</item>
/// <item><b>Automatic (Manual Override)</b> — outside the vendor's stated scope, so nothing is renumbered. The
/// inserted voucher is APPENDED at <c>max + 1</c> (or carries a number the operator typed). It therefore sits
/// chronologically before vouchers that carry LOWER numbers — a series out of date order. That is the documented
/// consequence of the method, not a defect of this code, and it is why the shell says so on the notice bar.</item>
/// <item><b>Manual</b> — outside the scope; the operator types the number and this planner assigns none.
/// 🔴 <b>THIS IS THE ONE METHOD UNDER WHICH AN INSERT CAN PRODUCE A DUPLICATE</b>, because nothing here or in the
/// vendor's flow stops the operator typing a number another voucher already holds. The duplicate is created by the
/// typed value, not by the insert — so the guard for it is not this planner's and is deliberately not duplicated
/// here: it is the voucher type's <see cref="VoucherType.PreventDuplicate"/> flag (the vendor's <i>"Prevent
/// creating duplicate Voucher Nos"</i>), which <c>VoucherValidator</c> already enforces on every post, insert or
/// not. An insert under Manual is therefore exactly as safe, and exactly as unsafe, as an ordinary Add under
/// Manual — which is the correct relationship, because the number came from the same keystrokes either way.</item>
/// <item><b>None</b> — the type assigns no numbers at all (<c>Number = 0</c>), so an insert is a purely
/// chronological placement and there is nothing to renumber, duplicate or leave a gap in.</item>
/// </list></para>
///
/// <para>🔴 <b>THE STATUTORY REFUSAL — the reason this row needed an engine and not a menu item.</b> Renumbering
/// rewrites the <b>document number</b> of vouchers that have ALREADY been posted, and some of those documents have
/// been filed: an e-invoice carries an IRN issued against a specific invoice number, an e-Way bill carries the
/// same. Changing the number afterwards makes the books disagree with a filed return, which is a wrong-document
/// defect on a statutory filing rather than a display nit. This planner therefore REFUSES outright when any
/// voucher the shift would touch is a filed statutory document, reusing
/// <see cref="MasterDeletionRules.IsFiledStatutoryDocument"/> — the very predicate that already guards deletion,
/// so insert and delete cannot disagree about which documents are frozen.</para>
///
/// <para>🔴 <b>CORRECTION, 2026-09-08.</b> An earlier draft of these remarks asserted that <i>"Prevent creating
/// duplicate Voucher Nos"</i> <b>is not shipped by this build</b>. That was FALSE and is corrected above:
/// <see cref="VoucherType.PreventDuplicate"/> exists, persists, and is enforced by <c>VoucherValidator</c> and
/// <c>InventoryPostingService</c>. The claim is recorded rather than quietly deleted because an inaccurate
/// "declared gap" is worse than no note at all — it invites a later agent to build a guard that is already
/// there.</para>
///
/// <para><b>DECLARED GAP, labelled as ours and NOT claimed as built.</b> The vendor's per-voucher-type
/// <i>"Numbering behaviour on the insertion/deletion"</i> switch — <i>"Renumber Vouchers"</i> versus <i>"Retain
/// Original Voucher No."</i> — is NOT shipped, because it is a persisted per-type setting and this slice carries
/// no schema budget (a <c>voucher_types</c> column is a migration, and the next migration owns v59). This build
/// implements the vendor's stated DEFAULT only — <i>"Renumber Vouchers … is set as default when you migrate your
/// data to TallyPrime 3.0 or later from any of the earlier versions, or you create a new company or a voucher
/// type in the latest release"</i> — so no operator choice is silently misread; the alternative simply cannot be
/// selected yet. Adding it is a schema change and a census follow-up, not a hidden default here.</para>
/// </summary>
public static class VoucherInsertion
{
    /// <summary>
    /// The sentence the shell shows when a filed statutory document sits in the shift range. Public so the tests
    /// assert the operator-visible text rather than a bare null-check, and so the shell cannot drift from it.
    /// </summary>
    public const string FiledDocumentRefusal =
        "Insert would renumber a voucher that has already been filed (a generated IRN or e-Way Bill), "
        + "changing a document number that has been reported. Insert it after that document, or use Add "
        + "Voucher (Alt+A), which numbers the new entry without touching any existing one.";

    /// <summary>
    /// Computes what inserting a voucher of <paramref name="type"/> immediately ABOVE <paramref name="anchor"/>
    /// would do to <paramref name="type"/>'s number series. Mutates nothing.
    ///
    /// <para><b>The vendor's worked example, verbatim, is this method's specification:</b> <i>"if you insert a
    /// sales voucher between sales voucher numbers 2 and 3, then the inserted voucher will attain voucher number 3
    /// and the existing voucher number 3 will be listed as voucher number 4 for the proper sequencing of the
    /// voucher numbers"</i>, and <i>"all the existing sales vouchers recorded on all subsequent days will be
    /// renumbered"</i>.</para>
    ///
    /// <para>🔴 <b>WHERE THE VENDOR'S EXAMPLE IS SILENT, AND THE LABELLED DERIVATION THAT COVERS IT.</b> The
    /// example inserts a SALES voucher above a SALES voucher — the anchor and the newcomer share a type, so "the
    /// number the newcomer takes" is unambiguous. TallyPrime's Insert lets the operator choose ANY voucher type,
    /// and the vendor does not say what number a Payment inserted above a Sales row takes. Guessing a
    /// cross-series rule would be inventing, so this planner derives it from the one sentence that IS attested —
    /// renumbering is <i>"of that particular type"</i> — and locates the insertion point WITHIN the newcomer's own
    /// series: the newcomer takes the number of the first voucher OF ITS OWN TYPE at or after the anchor in Day
    /// Book order, and that voucher and its successors shift. When the anchor and the newcomer share a type this
    /// reduces EXACTLY to the vendor's example (the first such voucher IS the anchor). When no voucher of the
    /// newcomer's type sits at or after the anchor, there is no successor to shift and the insert is an ordinary
    /// append at <c>max + 1</c> — the same number Add Voucher would have given it.</para>
    ///
    /// <para><b>Day Book order</b> is <c>(Date, then the book's own insertion order)</c> — the order the Day Book
    /// itself renders and therefore the order the operator's highlight means. Ties on date are broken by position
    /// in <see cref="Company.Vouchers"/>, which is stable and is how the report already reads.</para>
    /// </summary>
    /// <param name="company">The open company. Not mutated.</param>
    /// <param name="type">The voucher type being inserted.</param>
    /// <param name="anchor">The highlighted voucher — the newcomer goes immediately above it.</param>
    public static VoucherInsertionPlan Plan(Company company, VoucherType type, Voucher anchor)
    {
        ArgumentNullException.ThrowIfNull(company);
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(anchor);

        var none = Array.Empty<VoucherRenumber>();

        // ── The three methods the vendor puts OUTSIDE the insertion/deletion behaviour ────────────────────
        // Nothing is renumbered under these, so there is nothing to refuse either: no already-posted document
        // changes number, which is the only thing the statutory freeze protects.
        switch (type.Numbering)
        {
            // No numbers exist under this method — the insert is a pure chronological placement.
            case NumberingMethod.None:
                return new VoucherInsertionPlan(null, 0, none, type.Numbering);

            // The operator types the number; this planner assigns none. A duplicate is possible and is the
            // typed value's doing — see the class remarks.
            case NumberingMethod.Manual:
                return new VoucherInsertionPlan(null, 0, none, type.Numbering);

            // Attested as outside the setting's scope, so the newcomer is appended and nothing shifts.
            case NumberingMethod.AutomaticManualOverride:
                return new VoucherInsertionPlan(null, NextNumber(company, type.Id), none, type.Numbering);
        }

        // ── Automatic and Multi-user Auto — the two methods that renumber ─────────────────────────────────
        var order = DayBookOrder(company);
        var anchorRank = order.IndexOf(anchor.Id);
        if (anchorRank < 0)
            // The anchor is not in this book. Nothing sensible to insert above; treat as an append rather than
            // renumbering a series against a row that is not there.
            return new VoucherInsertionPlan(null, NextNumber(company, type.Id), none, type.Numbering);

        // The newcomer's OWN series, from the insertion point onward, in Day Book order.
        var successors = company.Vouchers
            .Where(v => v.TypeId == type.Id && v.Number > 0 && order.IndexOf(v.Id) >= anchorRank)
            .OrderBy(v => order.IndexOf(v.Id))
            .ToList();

        if (successors.Count == 0)
            // Nothing of this type at or after the anchor — an ordinary append, no shift.
            return new VoucherInsertionPlan(null, NextNumber(company, type.Id), none, type.Numbering);

        // 🔴 THE STATUTORY FREEZE. Checked over the vouchers that would actually MOVE, before any plan is
        // handed back, and reusing deletion's own predicate so the two verbs cannot disagree.
        foreach (var v in successors)
            if (MasterDeletionRules.IsFiledStatutoryDocument(company, v.Id))
                return new VoucherInsertionPlan(FiledDocumentRefusal, 0, none, type.Numbering);

        // The newcomer takes the first successor's number; every successor shifts up by one.
        // Ordered ascending by the number being vacated, which is also the order Apply must NOT rely on —
        // see Apply's remarks on why it snapshots first.
        var insertedNumber = successors[0].Number;
        var renumbers = successors
            .Select(v => new VoucherRenumber(v.Id, v.Number, v.Number + 1))
            .OrderBy(r => r.From)
            .ToList();

        return new VoucherInsertionPlan(null, insertedNumber, renumbers, type.Numbering);
    }

    /// <summary>
    /// Applies <paramref name="plan"/>'s renumbering to <paramref name="company"/>. A no-op for a plan that
    /// rewrites nothing; throws for a refused plan, so a caller that ignores <see cref="VoucherInsertionPlan.IsAllowed"/>
    /// fails loudly instead of quietly rewriting a filed document's number.
    ///
    /// <para>🔴 <b>WHY THE NEW NUMBERS ARE READ FROM THE PLAN AND NEVER RE-DERIVED.</b> The shift <c>n → n+1</c>
    /// walks a contiguous run, so applying it in place while re-reading each voucher's CURRENT number would let an
    /// earlier write feed a later read and cascade (3→4 then reading 4 and writing 5 on the same voucher). Every
    /// target and every value comes from the immutable plan computed before anything moved, so each voucher is
    /// written exactly once and the order of application cannot matter.</para>
    /// </summary>
    public static void Apply(Company company, VoucherInsertionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(company);
        ArgumentNullException.ThrowIfNull(plan);
        if (!plan.IsAllowed)
            throw new InvalidOperationException(
                "Refused insertion plan applied: " + plan.Refusal);

        foreach (var r in plan.Renumbers)
            if (company.FindVoucher(r.VoucherId) is { } v)
                v.Number = r.To;
    }

    /// <summary>Day Book order — <c>(Date, then position in the book)</c> — as a list of voucher ids, so a rank
    /// is an index lookup. Built once per plan rather than sorted per comparison.</summary>
    private static List<Guid> DayBookOrder(Company company) =>
        company.Vouchers
            .Select((v, i) => (v, i))
            .OrderBy(t => t.v.Date)
            .ThenBy(t => t.i)
            .Select(t => t.v.Id)
            .ToList();

    /// <summary>Max existing number for the type + 1 — the same rule <c>LedgerService.NextNumber</c> applies,
    /// restated here so the planner does not need a service instance to answer a pure question about the book.</summary>
    private static int NextNumber(Company company, Guid typeId)
    {
        var max = 0;
        foreach (var v in company.Vouchers)
            if (v.TypeId == typeId && v.Number > max)
                max = v.Number;
        return max + 1;
    }
}
