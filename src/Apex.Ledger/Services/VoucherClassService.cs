using Apex.Ledger.Domain;

namespace Apex.Ledger.Services;

/// <summary>
/// The master-save boundary for a <see cref="VoucherClass"/>'s Default Accounting Allocations and Additional
/// Accounting Entries (census row 2.6; schema v62).
/// </summary>
/// <remarks>
/// <para>🔴 <b>WHY VALIDATION IS AT THE MASTER, NOT AT VOUCHER ENTRY.</b> A voucher class posts its legs on every
/// voucher that uses it, with no prompt to the operator. A class whose allocations sum to 90% does not produce
/// one wrong voucher that somebody notices — it produces an unbalanced voucher every time anyone touches it,
/// forever, and the first symptom is a Balance Sheet that does not tie months later. The only place the mistake
/// is cheap is the screen where the class is saved, so every rule below refuses there.</para>
///
/// <para>Each rule is a defect this row could otherwise ship, not a tidiness check.</para>
/// </remarks>
public static class VoucherClassService
{
    /// <summary>
    /// The FULL check: <see cref="ValidateStructure"/> plus the <b>completeness</b> rule that the allocations
    /// total 100%. Throws <see cref="InvalidOperationException"/> on the first rule broken.
    /// <paramref name="ledgerExists"/> resolves a ledger id against the company; pass <c>null</c> to skip the
    /// existence check (the Io import pre-flight has its own).
    ///
    /// <para>Use this where the class must be READY TO POST — at the master-save gate and from
    /// <see cref="VoucherClassPosting.Compute"/>. Do NOT use it while the operator is still keying the
    /// allocation table; see <see cref="ValidateStructure"/> for why.</para>
    /// </summary>
    public static void Validate(VoucherClass voucherClass, Func<Guid, bool>? ledgerExists = null)
    {
        ValidateStructure(voucherClass, ledgerExists);
        EnsureAllocationsAreComplete(voucherClass);
    }

    /// <summary>
    /// Everything that must be true of a class <b>even while it is half-keyed</b>: no ledger allocated twice,
    /// every named ledger real, the rounding method/limit pairing sound, and at most one total-amount-rounding
    /// entry.
    ///
    /// <para>🔴 <b>WHY THE 100% RULE IS NOT IN HERE, AND WHY THAT SPLIT IS NOT A WEAKENING.</b> An operator
    /// building a three-way 50/30/20 pre-map passes through 50% and then 80% before reaching 100%. Enforcing
    /// completeness on every append would refuse the FIRST row of every class that is not a single 100% line,
    /// making the table impossible to build — so a single combined rule would not be "stricter", it would make
    /// the feature unusable and invite someone to delete the rule outright. The completeness rule is instead
    /// enforced at the two places that actually matter, both of which are unavoidable:
    /// <see cref="Validate"/> at the master-save gate, and <see cref="VoucherClassPosting.Compute"/>, which
    /// REFUSES to post from an incomplete class. There is no path by which an incomplete class writes a
    /// voucher.</para>
    /// </summary>
    public static void ValidateStructure(VoucherClass voucherClass, Func<Guid, bool>? ledgerExists = null)
    {
        ArgumentNullException.ThrowIfNull(voucherClass);

        ValidateAllocations(voucherClass, ledgerExists);
        ValidateAdditionalEntries(voucherClass, ledgerExists);
    }

    /// <summary>
    /// 🔴 <b>THE UNBALANCED-VOUCHER RULE.</b> A class that pre-maps ledgers must allocate exactly 100% of the item
    /// value across them. Anything else posts an invoice that does not balance, silently, on every voucher entered
    /// under the class. Compared in BASIS POINTS, so 33.33% + 33.33% + 33.34% is exact and no floating-point
    /// comparison is involved.
    ///
    /// <para>A class with NO allocations is complete — it pre-maps nothing, which is every class written before
    /// v62 and is exactly what the Stock Journal transfer class does.</para>
    /// </summary>
    public static void EnsureAllocationsAreComplete(VoucherClass voucherClass)
    {
        ArgumentNullException.ThrowIfNull(voucherClass);

        var rows = voucherClass.LedgerAllocations;
        if (rows.Count == 0) return;

        var total = rows.Sum(a => (long)a.PercentBasisPoints);
        if (total == VoucherClassLedgerAllocation.FullAllocationBasisPoints) return;

        var percent = total / 100m;
        throw new InvalidOperationException(
            $"Voucher class '{voucherClass.Name}' allocates {percent:0.##}% of the item value across its default " +
            "accounting allocations. They must total exactly 100%, or every voucher entered under this " +
            "class posts out of balance.");
    }

    private static void ValidateAllocations(VoucherClass cls, Func<Guid, bool>? ledgerExists)
    {
        var rows = cls.LedgerAllocations;
        if (rows.Count == 0) return; // A class that pre-maps nothing is legal — that is every pre-v62 class.

        var seen = new HashSet<Guid>();
        foreach (var a in rows)
        {
            // Two rows on one ledger are two shares of the same account. The total would still be 100%, so the
            // rule above cannot see it, and the posted result depends on which row the split's REMAINDER lands
            // on — an order-dependent figure the operator never chose.
            if (!seen.Add(a.LedgerId))
                throw new InvalidOperationException(
                    $"Voucher class '{cls.Name}' allocates to the same ledger more than once. Each ledger may " +
                    "take at most one default accounting allocation.");

            if (ledgerExists is not null && !ledgerExists(a.LedgerId))
                throw new InvalidOperationException(
                    $"Voucher class '{cls.Name}' allocates to a ledger that does not exist.");
        }
    }

    private static void ValidateAdditionalEntries(VoucherClass cls, Func<Guid, bool>? ledgerExists)
    {
        var roundingEntries = 0;

        foreach (var e in cls.AdditionalEntries)
        {
            if (ledgerExists is not null && !ledgerExists(e.LedgerId))
                throw new InvalidOperationException(
                    $"Voucher class '{cls.Name}' has an additional accounting entry on a ledger that does not exist.");

            // The vendor's own pairing: a rounding method needs a limit, and "Not Applicable" means leave the
            // Rounding Limit blank. A limit with no method is a figure the operator believes is in force and
            // which nothing reads.
            if (e.RoundingMethod == VoucherClassRoundingMethod.NotApplicable)
            {
                if (e.RoundingLimit != Money.Zero)
                    throw new InvalidOperationException(
                        $"Voucher class '{cls.Name}' has an additional accounting entry with a rounding limit " +
                        "but no rounding method.");
            }
            else
            {
                if (e.RoundingLimit <= Money.Zero)
                    throw new InvalidOperationException(
                        $"Voucher class '{cls.Name}' has an additional accounting entry whose rounding method " +
                        "needs a positive rounding limit.");

                // A sub-paisa limit snaps amounts to a multiple the INTEGER-paisa store cannot hold, so the
                // voucher balances in memory and is refused (or truncated) on the way to disk. Same rule the
                // pay-head master enforces for the same reason.
                if (!e.RoundingLimit.IsPaisaExact)
                    throw new InvalidOperationException(
                        $"Voucher class '{cls.Name}' has an additional accounting entry whose rounding limit " +
                        $"{e.RoundingLimit} must be a whole number of paisa.");
            }

            if (e.CalculationType == VoucherClassCalculationType.BasedOnQuantity)
            {
                if (e.ValueBasis <= Money.Zero)
                    throw new InvalidOperationException(
                        $"Voucher class '{cls.Name}' has a Based on Quantity entry with no value basis. The " +
                        "value basis is the rate per unit and must be positive.");

                if (!e.ValueBasis.IsPaisaExact)
                    throw new InvalidOperationException(
                        $"Voucher class '{cls.Name}' has a Based on Quantity entry whose value basis " +
                        $"{e.ValueBasis} must be a whole number of paisa.");
            }

            if (e.CalculationType == VoucherClassCalculationType.AsTotalAmountRounding)
            {
                roundingEntries++;

                // 🔴 A rounding entry with NO rounding method rounds nothing and posts nothing, while looking on
                // the screen exactly like a configured round-off. The operator then believes their invoices are
                // rounded and they are not.
                if (e.RoundingMethod == VoucherClassRoundingMethod.NotApplicable)
                    throw new InvalidOperationException(
                        $"Voucher class '{cls.Name}' has a total-amount-rounding entry with no rounding method, " +
                        "so it would round nothing.");
            }
        }

        // 🔴 Each rounding entry claims to be the difference that makes the SAME total come out round. Two of them
        // cannot both be right: whichever posts second sees a total the first already balanced, so it computes a
        // nil difference and vanishes — or, if the limits differ, unbalances what the first fixed.
        if (roundingEntries > 1)
            throw new InvalidOperationException(
                $"Voucher class '{cls.Name}' has {roundingEntries} total-amount-rounding entries. A class may " +
                "have at most one, because each one balances the whole voucher total.");
    }
}
