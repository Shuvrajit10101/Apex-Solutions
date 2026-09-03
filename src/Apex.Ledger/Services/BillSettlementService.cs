using Apex.Ledger.Domain;
using Apex.Ledger.Reports;

namespace Apex.Ledger.Services;

/// <summary>
/// Turns a selection of open bills into the Agst-Ref <see cref="BillAllocation"/> list a settlement line
/// needs — validating each reference against a genuinely open bill and capping each knock at that bill's
/// pending amount. Framework- and DB-free: it only produces domain objects, and it <b>posts nothing</b>.
///
/// <para><b>This class used to post (Phase 10.11 S2 / VL-4 / register row IV-5).</b> It carried a
/// <c>SettleAndPost</c> that built AND posted a whole settlement voucher, driven from a <b>Ctrl+B</b> binding
/// on the Outstandings report: for every spacebar-selected bill it wrote a real Receipt or Payment, always
/// for the bill's <i>full</i> pending amount, always through a ledger literally named "Cash", dated at the
/// report's as-of, with no preview, no confirmation and no undo. In TallyPrime <b>Ctrl+B is "Basis of Values"
/// — a report option that re-bases how figures are DISPLAYED and writes nothing to the books</b>
/// [TallyHelp keyboard-shortcuts, Reports section], and TallyPrime's Bills Outstanding has no settlement
/// action at all: a bill is settled by keying a Receipt/Payment and choosing <i>Against Reference</i> from the
/// List of Pending Bills [CORPUS-SG p.92 §5.5]. <c>SettleAndPost</c> is therefore deleted, and the operator
/// now confirms date, cash/bank ledger and per-bill amounts on a pre-loaded Single-Entry voucher before
/// accepting it.</para>
///
/// <para><b><see cref="BuildSettlementAllocations"/> deliberately survived that deletion.</b> It is the only
/// code in the repository that checks an Agst-Ref against a real open bill and caps the knock at its pending
/// amount — the in-voucher Bill-wise panel does not (register defect D5), so removing it along with the
/// posting would have made settlement <i>less</i> safe than the defect it replaced. The shell now calls it
/// twice: once to build the pre-load, and again at Accept over whatever the operator left in the panel.</para>
/// </summary>
public sealed class BillSettlementService
{
    private readonly Company _company;

    public BillSettlementService(Company company)
        => _company = company ?? throw new ArgumentNullException(nameof(company));

    /// <summary>One bill to knock off: the open bill's reference and how much of it to settle now.</summary>
    public readonly record struct Knock(string Reference, Money Amount);

    /// <summary>
    /// Builds the Agst-Ref allocations for settling the given bills of a party. Each amount must be
    /// &gt; 0 and the reference must name a currently-open bill; and the knocks naming ONE bill must not
    /// exceed that bill's current pending <b>in total</b>. Returns allocations totalling the sum of the
    /// requested amounts (the caller uses that total as the party line amount).
    ///
    /// <para><b>The cap is AGGREGATE, not per-knock, and that distinction is load-bearing.</b> Comparing each
    /// knock against the untouched <c>bill.Pending</c> is sufficient only while every knock names a different
    /// bill — true of the original caller, which built knocks from distinct selected report rows. Phase 10.11 S2
    /// points this method at the operator-editable Agst-Ref rows of a pre-loaded settlement voucher, where the
    /// bill name is a free TextBox (register defect D5) sitting next to an "+ Add bill" button, so two rows can
    /// name the SAME bill. Two rows each under the pending can then sum to well over it, and nothing downstream
    /// notices: <c>VoucherValidator.EnsureBillAllocationsValid</c> only checks that allocations sum to the LINE
    /// amount. The bill's accumulated pending goes negative, <see cref="Outstandings.OpenBillsFor"/> drops a
    /// non-positive pending, and the over-knocked bill VANISHES from the report while the party's ledger balance
    /// nets out — breaking the "Σ open bills == ledger closing balance" invariant with no message anywhere.</para>
    ///
    /// <para>Splitting one bill across several rows stays legal: only the running TOTAL per reference is capped,
    /// so two remittances against one invoice are expressible exactly as before.</para>
    /// </summary>
    public IReadOnlyList<BillAllocation> BuildSettlementAllocations(
        Domain.Ledger party, DateOnly asOf, IEnumerable<Knock> knocks)
    {
        if (party is null) throw new ArgumentNullException(nameof(party));
        if (!party.MaintainBillByBill)
            throw new InvalidOperationException($"Ledger '{party.Name}' does not maintain balances bill-by-bill.");

        var open = Outstandings.OpenBillsFor(_company, party, asOf)
            .ToDictionary(b => b.Reference, StringComparer.OrdinalIgnoreCase);

        // Running total already claimed per reference. SAME comparer as `open` above — the lookup treats
        // "inv-9" and "INV-9" as one bill, so the cap must too or a case flip would slip the whole guard.
        var claimed = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

        var allocations = new List<BillAllocation>();
        foreach (var k in knocks)
        {
            if (k.Amount.Amount <= 0m)
                throw new InvalidOperationException("A settlement amount must be > 0.");
            if (!open.TryGetValue(k.Reference, out var bill))
                throw new InvalidOperationException(
                    $"'{k.Reference}' is not an open bill for '{party.Name}' as of {asOf:yyyy-MM-dd}.");

            claimed.TryGetValue(k.Reference, out var already);
            var running = already + k.Amount.Amount;
            if (running > bill.Pending.Amount)
                throw new InvalidOperationException(
                    $"Cannot settle {new Money(running)} against bill '{k.Reference}' (pending {bill.Pending}).");
            claimed[k.Reference] = running;

            allocations.Add(new BillAllocation(BillRefType.AgstRef, k.Reference, k.Amount));
        }
        return allocations;
    }
}
