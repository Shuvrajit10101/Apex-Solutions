using System;
using System.Collections.Generic;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;

namespace Apex.Desktop.ViewModels;

/// <summary>
/// 🔴 <b>Phase 10.11 S5c — the INVERSE of the two engine appenders that write onto a plain-grid voucher's lines:</b>
/// the TDS withholding carve-out (<see cref="TdsService.BuildCarveOut"/>) and the reverse-charge self-accounting
/// pair (<see cref="RcmService.BuildReverseCharge"/>). Given a POSTED voucher it hands back the lines the operator
/// actually KEYED, plus a <b>pin</b> describing the derived shape, or a named refusal when the posted shape cannot
/// be inverted.
///
/// <para>🔴 <b>THE RULE THIS CLASS AND ITS CALLERS IMPLEMENT — stated once, in one place.</b>
/// <i>The POSTED VOUCHER decides WHETHER a leg is derived; the AMENDED CONTENT decides HOW MUCH; and any
/// disagreement between them is REFUSED BY NAME.</i> Concretely:
/// <list type="number">
///   <item><b>No detection is ever run for a family the posted voucher does not already carry.</b> A voucher posted
///     with no withholding cannot ACQUIRE one because a party master gained a <c>DeducteeType</c> after posting —
///     the detector is not consulted at all. This is the S5b behaviour, unchanged, and it is why lifting the
///     refusal does not regress the SIMPLE rows.</item>
///   <item><b>A voucher posted WITH a derived leg re-derives it, and can never silently lose it.</b> The carve is
///     re-computed by the same engine from the restored gross; if today's masters no longer produce the same
///     SHAPE (section, deductee, rate, or the reverse-charge head/scheme set) the alteration is refused with a
///     sentence naming what moved. Silence is the one outcome that is not available.</item>
/// </list></para>
///
/// <para>🔴 <b>RE-CARVE FROM THE RESTORED GROSS — never re-apply the stored carve to a new base.</b> On a
/// TDS-carved purchase the operator keys a GROSS; the posted lines carry the DERIVED net party credit plus a
/// separate TDS-Payable leg. <see cref="Invert"/> therefore adds the withheld amount back onto the party leg and
/// DROPS the payable leg, so the grid the operator re-opens holds the figure they typed. Re-applying the stored
/// carve to a new base instead would drift the party credit by exactly the carve — a silent wrong figure, and the
/// worst outcome available in this phase (design §3.2, the inversion trap).</para>
///
/// <para>🔴 <b>And the mirror obligation on the GST side: RECOMPUTE the stamp, never copy it</b> (design finding
/// L3-07). GSTR-1 and GSTR-3B read the STAMPED <see cref="GstLineTax.TaxableValue"/>, not the posted amounts, so a
/// replacement carrying a stale stamp makes a filed return declare a figure the book does not hold. Nothing
/// upstream catches it — <c>LedgerService.Replace</c> is symmetric with <c>Post</c> — so it is a CALLER obligation,
/// discharged by dropping every engine-written reverse-charge line here and rebuilding it through
/// <see cref="RcmService.BuildReverseCharge"/> at accept.</para>
/// </summary>
public static class VoucherAlterationDerivedLegs
{
    /// <summary>
    /// The TDS carve-out shape read off the POSTED voucher. Only <see cref="RestoredGross"/> is free to move under
    /// an alteration; the other three are the drift guard.
    /// </summary>
    /// <param name="NatureId">The posted Nature of Payment (section) — pinned, so a re-carve cannot silently
    /// re-section the withholding when the expense ledger's default has moved since posting.</param>
    /// <param name="DeducteeLedgerId">The posted deductee — pinned, so the carve cannot migrate to a different
    /// party leg.</param>
    /// <param name="RateBasisPoints">The posted rate — pinned, so a moved rate master, or a deductee PAN added or
    /// removed since posting (which flips the §206AA no-PAN rate), is refused rather than silently applied.</param>
    /// <param name="SectionCode">The posted section code, for the refusal sentences.</param>
    /// <param name="PostedTdsAmount">
    /// What was actually withheld (0 below threshold). 🔴 <b>READ, not merely written</b>: this field was
    /// captured and never compared, which left the applies/not-applies transition entirely unguarded — a posted
    /// withholding was silently DELETED whenever the cumulative-FY projection moved for any reason other than the
    /// voucher's own gross. <c>VoucherEntryViewModel.ApplyReCarve</c> is where it is compared, and the comment
    /// there records the three measured routes.
    /// </param>
    /// <param name="RestoredGross">The party's gross obligation as KEYED — the net credit plus the withheld
    /// amount. This is the figure the re-opened grid shows and the base the re-carve works from.</param>
    public sealed record TdsPin(
        Guid NatureId, Guid DeducteeLedgerId, int RateBasisPoints, string SectionCode,
        Money PostedTdsAmount, Money RestoredGross);

    /// <summary>
    /// The reverse-charge shape read off the POSTED voucher, as a sorted multiset of per-line signatures
    /// (ledger · side · head · rate · ITC scheme). <b>Amounts are deliberately absent</b>: they are what an
    /// alteration is allowed to move. Everything else is drift.
    /// </summary>
    public sealed record RcmPin(IReadOnlyList<string> Signature)
    {
        /// <summary>True when a freshly recomputed reverse-charge line set has the same shape as the posted one.</summary>
        public bool Matches(IEnumerable<EntryLine> recomputed) =>
            Signature.SequenceEqual(SignatureOf(recomputed));
    }

    /// <summary>The keyed lines plus the pins — what <c>ForAlter</c> rehydrates from and <c>AcceptAlteration</c>
    /// re-derives against.</summary>
    public sealed record Inversion(IReadOnlyList<EntryLine> KeyedLines, TdsPin? Tds, RcmPin? Rcm)
    {
        /// <summary>True when the posted voucher carries no engine-derived leg at all — the SIMPLE shape S5b
        /// already served, where <see cref="KeyedLines"/> is the posted line list unchanged.</summary>
        public bool IsPlain => Tds is null && Rcm is null;
    }

    /// <summary>
    /// Inverts <paramref name="voucher"/>'s engine-derived legs. Returns <c>null</c> and sets
    /// <paramref name="inversion"/> when the posted shape can be re-keyed; otherwise returns the named refusal
    /// and leaves <paramref name="inversion"/> null.
    /// </summary>
    public static string? Invert(Company company, Voucher voucher, out Inversion? inversion)
    {
        ArgumentNullException.ThrowIfNull(company);
        ArgumentNullException.ThrowIfNull(voucher);
        inversion = null;

        var lines = voucher.Lines.ToList();

        if (InvertReverseCharge(company, lines, out var rcmPin) is { } rcmRefusal) return rcmRefusal;
        if (InvertWithholding(company, lines, out var tdsPin) is { } tdsRefusal) return tdsRefusal;

        // Whatever GST stamp is LEFT after the reverse-charge pair has been lifted was not written by an appender
        // this class can invert — a hand-stamped line from a canonical-XML import, or an engine family whose own
        // refusal arm did not fire. Refusing it keeps the "never echo a stamp" obligation absolute: there is no
        // path from here that carries an un-recomputed GstLineTax into a replacement.
        if (lines.FirstOrDefault(l => l.HasGst) is { } stamped)
        {
            var head = stamped.Gst!.TaxHead;
            return $"This voucher carries an engine-stamped {head} GST figure on a line that is not part of a "
                 + "reverse-charge pair — the taxable value GSTR-1 and GSTR-3B read. It cannot be re-derived from "
                 + "the entry grid, and carrying it forward unchanged would let a return declare a figure the book "
                 + "no longer holds.";
        }

        if (lines.FirstOrDefault(l => l.HasTcs) is { } collected)
        {
            var section = collected.Tcs!.CollectionCode;
            return $"This voucher carries a TCS collection ({section}): the collected amount and its assessable "
                 + "value are stamped on the line and read by Form 27EQ. TCS is collected on the INVOICE screens, "
                 + "never on the Dr/Cr grid, so this grid has nothing to re-derive it from.";
        }

        inversion = new Inversion(lines, tdsPin, rcmPin);
        return null;
    }

    // ------------------------------------------------------------------ the TDS carve-out

    /// <summary>
    /// Removes the TDS-Payable leg and restores the party's GROSS credit, mutating <paramref name="lines"/> in
    /// place. Two posted shapes exist and they fail differently, so both are matched explicitly rather than by a
    /// tag scan:
    /// <list type="bullet">
    ///   <item><b>Withheld</b> — <c>Cr Party = NET</c> (untagged) beside <c>Cr TDS Payable = TDS</c> carrying the
    ///     <see cref="TdsLineTax"/>. The payable leg goes; the party leg is restored to <c>net + TDS</c>.</item>
    ///   <item><b>Below threshold</b> — no payable leg at all; the party is credited the FULL gross and the detail
    ///     (with <c>TdsAmount</c> 0) rides that line so the FY cumulative projection still counts the assessment.
    ///     Only the tag comes off; the amount was never carved.</item>
    /// </list>
    /// </summary>
    private static string? InvertWithholding(Company company, List<EntryLine> lines, out TdsPin? pin)
    {
        pin = null;

        var tagged = lines.Where(l => l.HasTds).ToList();
        if (tagged.Count == 0) return null;
        if (tagged.Count > 1)
            return $"This voucher carries {tagged.Count} withholding assessments on its lines. The entry screen "
                 + "computes exactly one carve-out per voucher, so it cannot re-key this shape — it can only have "
                 + "arrived from an import.";

        var carveLine = tagged[0];
        var detail = carveLine.Tds!;

        if (company.FindLedger(detail.DeducteeLedgerId) is null)
            return "The deductee party this voucher withheld tax from is no longer in this company, so the "
                 + "withholding cannot be re-computed and the gross that was keyed cannot be restored.";

        // The section is the pin the re-carve is measured against, so a voucher whose section is not in this
        // company is refused AT THE DOOR rather than opened and refused at accept. That shape is reachable: the
        // canonical import builds a TdsLineTax straight from XML, and a company that never enabled TDS has no
        // Nature of Payment at all.
        if (company.FindNatureOfPayment(detail.NatureId) is null)
            return $"This voucher withheld TDS under section {detail.SectionCode}, which is not one of this "
                 + "company's Natures of Payment — the deduction cannot be re-computed, so the gross the operator "
                 + "keyed cannot be restored and the screen cannot re-open it.";

        // Below-threshold shape: the detail rides the party's own (full-gross) credit.
        if (carveLine.LedgerId == detail.DeducteeLedgerId)
        {
            if (detail.TdsAmount.Amount != 0m)
                return "The withholding detail on this voucher sits on the deductee's own line while recording a "
                     + "deducted amount, which is not a shape this screen posts — a deducted amount always sits on "
                     + "a separate TDS Payable leg. This voucher can only have arrived from an import.";

            var index = lines.IndexOf(carveLine);
            lines[index] = Untagged(carveLine);
            pin = new TdsPin(detail.NatureId, detail.DeducteeLedgerId, detail.RateBasisPoints, detail.SectionCode,
                Money.Zero, carveLine.Amount);
            return null;
        }

        // Withheld shape: carveLine is the TDS Payable leg.
        if (detail.TdsAmount.Amount == 0m)
            return "This voucher records a zero withholding on a leg that is not the deductee's, which is not a "
                 + "shape this screen posts. It can only have arrived from an import.";
        if (carveLine.Amount != detail.TdsAmount)
            return $"The TDS Payable leg on this voucher is {carveLine.Amount} while the withholding detail it "
                 + $"carries records {detail.TdsAmount}. The two disagree, so the gross the operator keyed cannot "
                 + "be recovered by adding the withholding back.";

        var partyLines = lines
            .Where(l => l.Side == DrCr.Credit && l.LedgerId == detail.DeducteeLedgerId && !l.HasTds)
            .ToList();
        if (partyLines.Count != 1)
            return partyLines.Count == 0
                ? "This voucher withholds tax from a party that has no credit line left on it, so the gross that "
                + "was keyed cannot be recovered."
                : $"This voucher withholds tax from a party credited on {partyLines.Count} separate lines, so "
                + "there is no single leg to add the withholding back onto.";

        var party = partyLines[0];
        var gross = party.Amount + detail.TdsAmount;

        // 🔴 THE CHILDREN COME BACK TO THE GROSS TOO, and that is not decoration: the entry grid demands that a
        // bill-wise split sum to the line amount and that a cost split total it per category, so restoring the leg
        // to the gross while leaving a split keyed at the net produces a grid the screen refuses to accept
        // ("Bill-wise allocations for 'X' must sum to the line amount"). This is the exact inverse of
        // TdsService.CarveBills / CarveCosts, which take the withheld amount OUT of the single reference.
        var restoredBills = RestoreAllocation(
            party.BillAllocations, detail.TdsAmount,
            b => new BillAllocation(b.RefType, b.Name, b.Amount + detail.TdsAmount, b.DueDate, b.CreditPeriodDays));
        var restoredCosts = RestoreAllocation(
            party.CostAllocations, detail.TdsAmount,
            a => new CostAllocation(a.CategoryId, a.CentreId, a.Amount + detail.TdsAmount));

        // More than one reference on a withheld leg cannot have come from this screen (the carve refuses to post
        // it), so it is an import shape, and there is no way to decide which reference the withholding came out of.
        if (restoredBills is null && party.BillAllocations.Count > 1)
            return $"This voucher's withheld credit to the deductee is split across {party.BillAllocations.Count} "
                 + "bill references. The gross the operator keyed can only be restored by adding the withholding "
                 + "back onto one of them, and there is no way to tell which — so this shape cannot be re-keyed on "
                 + "the entry grid.";
        if (restoredCosts is null && party.CostAllocations.Count > 1)
            return $"This voucher's withheld credit to the deductee is split across {party.CostAllocations.Count} "
                 + "cost-centre allocations. The gross the operator keyed can only be restored by adding the "
                 + "withholding back onto one of them, and there is no way to tell which — so this shape cannot be "
                 + "re-keyed on the entry grid.";

        lines.Remove(carveLine);
        lines[lines.IndexOf(party)] = new EntryLine(
            party.LedgerId, gross, party.Side,
            restoredBills,
            restoredCosts,
            party.BankAllocation,
            party.Forex);

        pin = new TdsPin(detail.NatureId, detail.DeducteeLedgerId, detail.RateBasisPoints, detail.SectionCode,
            detail.TdsAmount, gross);
        return null;
    }

    /// <summary>
    /// Adds <paramref name="tds"/> back onto the ONE allocation the carve took it out of. <c>null</c> when there is
    /// nothing to restore (no allocation at all) <b>and</b> when there is more than one — the caller distinguishes
    /// those two cases by the source count, because only the second is a refusal.
    /// </summary>
    private static IReadOnlyList<T>? RestoreAllocation<T>(
        IReadOnlyList<T> allocations, Money tds, Func<T, T> restore)
    {
        if (allocations.Count != 1) return null;
        return tds.Amount == 0m ? allocations : new[] { restore(allocations[0]) };
    }

    private static EntryLine Untagged(EntryLine line) =>
        new(line.LedgerId, line.Amount, line.Side,
            line.BillAllocations.Count > 0 ? line.BillAllocations : null,
            line.CostAllocations.Count > 0 ? line.CostAllocations : null,
            line.BankAllocation,
            line.Forex);

    // ------------------------------------------------------------------ the reverse-charge pair

    /// <summary>
    /// Removes every engine-written reverse-charge line, mutating <paramref name="lines"/> in place. Under a
    /// REGULAR registration the pair is <c>Cr RCM Output {head}</c> + <c>Dr Input {head}</c> and BOTH legs carry
    /// <c>GstLineTax.IsReverseCharge</c>, so the tag is a complete and exact selector.
    ///
    /// <para>🔴 <b>A COMPOSITION-SHAPED pair is refused by name rather than inverted, and that is a measured
    /// limit, not an oversight.</b> Under composition <c>BuildReverseCharge</c> routes the balancing debit to the
    /// non-creditable "RCM Tax" expense ledger and deliberately leaves it UNTAGGED (there must be no ITC-tagged
    /// line, because composition blocks all credit). An untagged ordinary expense debit is indistinguishable from
    /// one the operator keyed, so lifting it would mean guessing — and guessing wrong either drops a real expense
    /// leg or keeps an engine one.</para>
    ///
    /// <para>🔴 <b>The shape is read off the BOOK, never off today's registration type — and it used to be the
    /// other way round, which failed in BOTH directions.</b> <c>company.Gst.RegistrationType</c> is a master an
    /// operator can change in production (the GST configuration screen), and the two directions were measured:
    /// a voucher posted under REGULAR, whose balancing debit IS tagged and IS exactly invertible, became
    /// PERMANENTLY unalterable the moment the company opted into composition — refused by a sentence describing a
    /// shape it demonstrably did not have; and a voucher posted under COMPOSITION, after a conversion to Regular,
    /// OPENED, put the engine's untagged RCM-tax debit on the grid as if the operator had keyed it, and left the
    /// alteration screen out of balance by exactly that leg (Dr 11,800.59 against Cr 10,000.50) before refusing
    /// with a raw engine exception telling the operator to enable GST on a company where GST was already enabled.
    /// The posted shape is self-describing and needs no master at all: under Regular each pair is a tagged CREDIT
    /// plus a tagged DEBIT, and under composition it is a tagged CREDIT with an UNTAGGED debit — so "no tagged
    /// reverse-charge debit" IS the composition shape.</para>
    /// </summary>
    private static string? InvertReverseCharge(Company company, List<EntryLine> lines, out RcmPin? pin)
    {
        pin = null;

        var rcmLines = lines.Where(l => l.Gst is { IsReverseCharge: true }).ToList();
        if (rcmLines.Count == 0) return null;

        if (rcmLines.All(l => l.Side == DrCr.Credit))
            return "This voucher self-accounts reverse charge with the balancing debit booked to the non-creditable "
                 + "RCM tax expense, carrying no tax tag at all — the shape a COMPOSITION registration posts, "
                 + "because composition blocks input credit. That leg is indistinguishable on the grid from an "
                 + "expense the operator keyed, so the engine's own lines cannot be told apart from the keyed ones.";

        // The supply KIND is keyed at entry and is persisted NOWHERE, so an import-of-services voucher cannot be
        // re-opened under the routing it was posted with: the screen rehydrates the selector to Domestic and shows
        // "Domestic inward supply (§9(3) / §9(4))" on a §5(3) voucher. The ITC scheme IS on the book, which is what
        // makes that refusable at the DOOR rather than opened under a re-labelled routing and refused at accept —
        // and a wrong routing on the screen is a wrong figure even when nothing is posted.
        if (rcmLines.FirstOrDefault(l => l.Gst!.RcmScheme == RcmItcScheme.ImportOfServices) is not null)
            return "This voucher self-accounts reverse charge on an IMPORT OF SERVICES (§5(3), GSTR-3B table "
                 + "4A(2)). The supply kind is keyed on the entry screen and is not stored on the voucher, so "
                 + "re-opening it would show the supply routed as a domestic inward supply — a different table of "
                 + "the return from the one this voucher was posted under.";

        pin = new RcmPin(SignatureOf(rcmLines));
        foreach (var l in rcmLines) lines.Remove(l);
        return null;
    }

    /// <summary>
    /// The order-independent shape signature of a reverse-charge line set: ledger, side, head, rate and ITC scheme,
    /// sorted. <b>The taxable value and the amount are excluded on purpose</b> — they are precisely what an
    /// alteration moves, and pinning them would refuse every real amendment while pinning nothing that matters.
    /// </summary>
    internal static IReadOnlyList<string> SignatureOf(IEnumerable<EntryLine> lines) =>
        lines
            .Where(l => l.Gst is { IsReverseCharge: true })
            .Select(l =>
            {
                var g = l.Gst!;
                var scheme = g.RcmScheme is { } s ? ((int)s).ToString() : "-";
                return $"{l.LedgerId:N}|{(int)l.Side}|{(int)g.TaxHead}|{g.RateBasisPoints}|{scheme}";
            })
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();
}
