using Apex.Ledger.Domain;
using Apex.Ledger.Reports;

namespace Apex.Ledger.Services;

/// <summary>
/// The Settle-Bill (Ctrl+B) helper the UI drives from the Outstandings report (plan.md §5, C-3).
/// It turns a multi-select of open bills into the Agst-Ref <see cref="BillAllocation"/> list a
/// settlement line needs, and can build a balanced settlement voucher (party knock-off vs a
/// cash/bank contra) that posts through the normal <see cref="LedgerService"/> path. Framework-
/// and DB-free: it only produces domain objects.
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
    /// &gt; 0 and not exceed that bill's current pending; the reference must be a currently-open bill.
    /// Returns allocations totalling the sum of the requested amounts (the caller uses that total as
    /// the party line amount).
    /// </summary>
    public IReadOnlyList<BillAllocation> BuildSettlementAllocations(
        Domain.Ledger party, DateOnly asOf, IEnumerable<Knock> knocks)
    {
        if (party is null) throw new ArgumentNullException(nameof(party));
        if (!party.MaintainBillByBill)
            throw new InvalidOperationException($"Ledger '{party.Name}' does not maintain balances bill-by-bill.");

        var open = Outstandings.OpenBillsFor(_company, party, asOf)
            .ToDictionary(b => b.Reference, StringComparer.OrdinalIgnoreCase);

        var allocations = new List<BillAllocation>();
        foreach (var k in knocks)
        {
            if (k.Amount.Amount <= 0m)
                throw new InvalidOperationException("A settlement amount must be > 0.");
            if (!open.TryGetValue(k.Reference, out var bill))
                throw new InvalidOperationException(
                    $"'{k.Reference}' is not an open bill for '{party.Name}' as of {asOf:yyyy-MM-dd}.");
            if (k.Amount > bill.Pending)
                throw new InvalidOperationException(
                    $"Cannot settle {k.Amount} against bill '{k.Reference}' (pending {bill.Pending}).");

            allocations.Add(new BillAllocation(BillRefType.AgstRef, k.Reference, k.Amount));
        }
        return allocations;
    }

    /// <summary>
    /// Builds AND posts a settlement voucher: the party line (knock-off side) plus a single contra
    /// line to <paramref name="settleThrough"/> (cash/bank). The party side is the opposite of the
    /// bill's natural side — settling a receivable credits the party and debits cash; settling a
    /// payable debits the party and credits cash. Posts through <see cref="LedgerService"/> so every
    /// §6 invariant (including the bill-split rule) is enforced.
    /// </summary>
    public Voucher SettleAndPost(
        Domain.Ledger party,
        Domain.Ledger settleThrough,
        Guid voucherTypeId,
        DateOnly date,
        IEnumerable<Knock> knocks,
        string? narration = null)
    {
        var knockList = knocks.ToList();
        var allocations = BuildSettlementAllocations(party, date, knockList);

        var total = 0m;
        foreach (var a in allocations) total += a.Amount.Amount;
        var totalMoney = new Money(total);

        var kind = Outstandings.KindOf(_company, party);
        // Settling a receivable: Cr party / Dr cash. Settling a payable: Dr party / Cr cash.
        var partySide = kind == OutstandingKind.Receivable ? DrCr.Credit : DrCr.Debit;
        var contraSide = partySide == DrCr.Credit ? DrCr.Debit : DrCr.Credit;

        var partyLine = new EntryLine(party.Id, totalMoney, partySide, allocations);
        var contraLine = new EntryLine(settleThrough.Id, totalMoney, contraSide);

        var voucher = new Voucher(
            Guid.NewGuid(), voucherTypeId, date, new[] { partyLine, contraLine }, narration: narration);

        return new LedgerService(_company).Post(voucher);
    }
}
