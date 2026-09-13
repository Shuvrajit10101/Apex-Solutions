using Apex.Ledger.Domain;

namespace Apex.Ledger.Services;

/// <summary>
/// One leg a <see cref="VoucherClass"/> posts automatically. <see cref="Amount"/> is <b>signed relative to the
/// class's natural side</b>: positive means "the side the item value posts to", negative the opposite side. A
/// leg is never dropped for being negative — a downward-rounding round-off leg IS negative, and turning it
/// positive would move the invoice total by twice the rounding.
/// </summary>
public readonly record struct VoucherClassLeg(Guid LedgerId, Money Amount)
{
    /// <summary>This leg as an <see cref="EntryLine"/> on <paramref name="naturalSide"/>, flipping to the opposite
    /// side when the amount is negative — <see cref="EntryLine"/> requires a positive magnitude, so the sign has
    /// to live in the side. Never call this on a zero leg; <see cref="VoucherClassPosting"/> does not emit one.</summary>
    public EntryLine ToEntryLine(DrCr naturalSide) =>
        Amount.Amount >= 0m
            ? new EntryLine(LedgerId, Amount, naturalSide)
            : new EntryLine(LedgerId, -Amount, naturalSide == DrCr.Debit ? DrCr.Credit : DrCr.Debit);
}

/// <summary>The legs a class produced for one voucher, kept apart so a caller (and a test) can see which table
/// each came from.</summary>
public sealed record VoucherClassPostingResult(
    IReadOnlyList<VoucherClassLeg> AllocationLegs,
    IReadOnlyList<VoucherClassLeg> AdditionalLegs)
{
    /// <summary>Every leg, allocations first then additional entries, in the order they post.</summary>
    public IReadOnlyList<VoucherClassLeg> AllLegs =>
        AllocationLegs.Concat(AdditionalLegs).ToList();

    /// <summary>The signed sum of every leg — the voucher total this class produces.</summary>
    public Money Total =>
        new(AllocationLegs.Sum(l => l.Amount.Amount) + AdditionalLegs.Sum(l => l.Amount.Amount));
}

/// <summary>
/// Computes the ledger legs a <see cref="VoucherClass"/> posts automatically on a voucher (census row 2.6; schema
/// v62) — the vendor's <b>Default Accounting Allocations</b> split and its <b>Additional Accounting Entries</b>,
/// including the invoice round-off.
/// </summary>
/// <remarks>
/// <para>🔴 <b>THIS IS A WRONG-MONEY SURFACE AND IT PROMPTS NOBODY.</b> Every figure below posts onto every
/// voucher that uses the class without the operator seeing a question. The two failure modes that matter are
/// therefore both silent, and both are closed here rather than left to a caller:</para>
///
/// <para>🔴 <b>1 — THE ALLOCATION SPLIT MUST BE EXACT, NOT MERELY CLOSE.</b> Three ledgers at 33.33% / 33.33% /
/// 33.34% of ₹100.00 round to 33.33 + 33.33 + 33.34 = ₹100.00 only by luck of the figures; at ₹1 000.01 the
/// independently-rounded shares sum to ₹1 000.00 and the invoice is a paisa short — an UNBALANCED VOUCHER, posted
/// silently. <see cref="Allocate"/> therefore computes every share but the LAST by rounding, and gives the last
/// allocation the REMAINDER, so the shares sum to the item value to the paisa by construction and not by
/// arithmetic coincidence. The residue lands on the last row in the operator's own declared order, which is
/// stable across platforms because <see cref="VoucherClassLedgerAllocation.Order"/> is stored.</para>
///
/// <para>🔴 <b>2 — THE ROUND-OFF LEG IS COMPUTED ON THE RUNNING TOTAL, NOT ON THE ITEM VALUE.</b> The vendor
/// defines it as <i>"the difference between the original and rounded amounts"</i> of the invoice, which balances
/// it. If it were taken on the item value alone, any additional entry posted after it (a per-unit freight, say)
/// would move the total off the rounded figure again and the round-off would be wrong by that freight's fraction.
/// So every non-rounding entry is computed FIRST, in declared order, and the rounding entry is applied to
/// allocations + those entries. Two rounding entries on one class would each claim to balance the same total, so
/// <see cref="VoucherClassService"/> rejects that at the master-save boundary.</para>
///
/// <para><b>R7 — ATTESTED.</b> The tables and their fields are cited on
/// <see cref="VoucherClassLedgerAllocation"/> and <see cref="VoucherClassAdditionalEntry"/>; the rounding
/// vocabulary and the round-off ledger's balancing role on <see cref="VoucherClassRounding"/> and
/// <c>help.tallysolutions.com/round-off-invoice-and-ledger-values/</c>.</para>
///
/// <para><b>Appropriation is not decided here.</b> This computes an additional ledger's AMOUNT and stops. Whether
/// that amount then loads onto the item lines' stock rate is the ledger master's
/// <see cref="Ledger.MethodOfAppropriation"/>, read by the existing <c>AdditionalCostApportionment</c> exactly as
/// it is for an operator-keyed line. See <see cref="VoucherClassAdditionalEntry"/> for why there is deliberately
/// no class-level apportionment field and why the ledger wins.</para>
/// </remarks>
public static class VoucherClassPosting
{
    /// <summary>
    /// The legs <paramref name="voucherClass"/> posts for a voucher whose item lines total
    /// <paramref name="itemValue"/> and carry <paramref name="totalBaseQuantity"/> base units.
    ///
    /// <para>A class with no allocations and no additional entries returns two empty lists — that is the shipped
    /// Stock Journal transfer class (census 9.9), which posts no ledger entry at all, and it must keep posting
    /// none.</para>
    /// </summary>
    public static VoucherClassPostingResult Compute(
        VoucherClass voucherClass, Money itemValue, decimal totalBaseQuantity)
    {
        ArgumentNullException.ThrowIfNull(voucherClass);

        // 🔴 AN INCOMPLETE CLASS NEVER POSTS. The master-save gate refuses one too, but this is the check that
        // makes that guarantee unconditional: a class reaching here with 90% allocated — from an import, from a
        // hand-edited book, from a screen that forgot to validate — would otherwise post an invoice 10% short and
        // say nothing. Refusing is loud; posting is not.
        VoucherClassService.EnsureAllocationsAreComplete(voucherClass);

        var allocations = Allocate(voucherClass, itemValue);

        // Pass 1 — every entry that is NOT a total-amount rounding, in the operator's declared order. These move
        // the total, so the rounding entry cannot be computed until they are all in.
        var additional = new List<VoucherClassLeg>();
        var ordered = voucherClass.AdditionalEntries.OrderBy(e => e.Order).ThenBy(e => e.Id).ToList();

        foreach (var entry in ordered)
        {
            if (entry.CalculationType == VoucherClassCalculationType.AsTotalAmountRounding) continue;

            var raw = entry.CalculationType switch
            {
                VoucherClassCalculationType.BasedOnQuantity => entry.ValueBasis.Amount * totalBaseQuantity,
                // NotApplicable derives nothing: the class names the ledger, the operator keys the figure.
                _ => 0m,
            };

            if (entry.CalculationType == VoucherClassCalculationType.NotApplicable) continue;

            var amount = VoucherClassRounding.Apply(raw, entry.RoundingMethod, entry.RoundingLimit);
            if (amount == Money.Zero && entry.RemoveIfZero) continue;
            if (amount == Money.Zero) continue; // an EntryLine cannot carry a zero magnitude

            additional.Add(new VoucherClassLeg(entry.LedgerId, amount));
        }

        // Pass 2 — the rounding entry, on the running total. See the class remarks, point 2.
        var rounding = ordered.FirstOrDefault(
            e => e.CalculationType == VoucherClassCalculationType.AsTotalAmountRounding);

        if (rounding is not null)
        {
            var running = allocations.Sum(l => l.Amount.Amount) + additional.Sum(l => l.Amount.Amount);
            var rounded = VoucherClassRounding.Apply(running, rounding.RoundingMethod, rounding.RoundingLimit);
            var difference = new Money(rounded.Amount - running);

            // A nil difference is the common case on an already-round invoice. The vendor's Remove if Zero decides
            // whether it shows; either way an EntryLine cannot carry a zero magnitude, so nothing is emitted.
            if (difference != Money.Zero)
                additional.Add(new VoucherClassLeg(rounding.LedgerId, difference));
        }

        return new VoucherClassPostingResult(allocations, additional);
    }

    /// <summary>
    /// Splits <paramref name="itemValue"/> across the class's Default Accounting Allocations. Every share but the
    /// last is <c>value × percent</c> rounded to the paisa; the LAST share is the remainder, so the shares sum to
    /// <paramref name="itemValue"/> exactly. See the class remarks, point 1, for why this is not optional.
    /// </summary>
    public static IReadOnlyList<VoucherClassLeg> Allocate(VoucherClass voucherClass, Money itemValue)
    {
        ArgumentNullException.ThrowIfNull(voucherClass);

        var rows = voucherClass.LedgerAllocations.OrderBy(a => a.Order).ThenBy(a => a.Id).ToList();
        if (rows.Count == 0) return Array.Empty<VoucherClassLeg>();

        var legs = new List<VoucherClassLeg>(rows.Count);
        var assigned = 0m;

        for (var i = 0; i < rows.Count; i++)
        {
            Money share;
            if (i == rows.Count - 1)
            {
                // The remainder — never an independently rounded figure. This is what makes the split exact.
                share = new Money(itemValue.Amount - assigned);
            }
            else
            {
                var raw = itemValue.Amount
                          * rows[i].PercentBasisPoints
                          / VoucherClassLedgerAllocation.FullAllocationBasisPoints;
                share = new Money(Math.Round(raw, 2, MidpointRounding.AwayFromZero));
                assigned += share.Amount;
            }

            if (share != Money.Zero) legs.Add(new VoucherClassLeg(rows[i].LedgerId, share));
        }

        return legs;
    }
}
