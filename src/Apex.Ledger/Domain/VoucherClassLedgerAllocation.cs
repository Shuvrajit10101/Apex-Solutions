namespace Apex.Ledger.Domain;

/// <summary>
/// One row of the vendor's <b>"Default Accounting Allocations for all items in Invoice"</b> table on a
/// <see cref="VoucherClass"/> (census 2.6; schema v62) — a <b>ledger pre-map</b>: the class names the ledger a
/// voucher of that class posts the item value to, and the <b>percentage</b> of that value it receives, so the
/// operator names neither.
/// </summary>
/// <remarks>
/// <para><b>R7 — ATTESTED.</b> <c>help.tallysolutions.com/tally-prime/accounting/voucher-types-tally/</c> names
/// the table — the operator may <i>"specify the ledgers to be allocated automatically for inventory items under
/// <b>Default Accounting Allocations for all items in Invoice</b>"</i> — and the vendor's accounting guidance
/// gives the two fields a row carries: <i>"Set <b>Enable Default Accounting Entries</b> to Yes … Enable the
/// option <b>Set/Alter Default Accounting Entries</b>. Select the <b>Ledger</b>, required and define the
/// <b>percentage</b> of allocation."</i> Ledger + percentage is therefore the whole attested shape of a row, and
/// nothing else is added to it.</para>
///
/// <para>🔴 <b>THE PERCENTAGES ARE NOT FORCED TO 100.</b> This type carries one row; whether the rows of a class
/// sum to 100% is a property of the SET, and <see cref="Apex.Ledger.Services.VoucherClassService"/> enforces it
/// at the master-save boundary — because a class whose allocations sum to 90% posts an invoice that does not
/// balance, on every voucher, with no prompt. The constructor's job is only to reject a row that is nonsense on
/// its own.</para>
///
/// <para>🔴 <b>PERCENT IS BASIS POINTS, AS AN INTEGER — never a <c>decimal</c> percentage and never a
/// <c>double</c>.</b> The same rule <see cref="Money"/> exists for: a third of an invoice is 33.33%, and two
/// machines that store that as binary floating point disagree in the last place, which is enough to move a posted
/// paisa. One hundred percent is <c>10_000</c> basis points. This matches the <c>vat_cst_rate_form_c_bp</c>
/// precedent already in the schema.</para>
/// </remarks>
public sealed class VoucherClassLedgerAllocation
{
    /// <summary>One hundred percent, in basis points — the total the allocations of a class must reach.</summary>
    public const int FullAllocationBasisPoints = 10_000;

    /// <summary>Stable surrogate key.</summary>
    public Guid Id { get; }

    /// <summary>The vendor's <b>Ledger</b> — the account the allocated share of the item value posts to.</summary>
    public Guid LedgerId { get; set; }

    /// <summary>The vendor's <b>percentage</b> of allocation, in BASIS POINTS (10 000 = 100%). Must be strictly
    /// positive: a 0% allocation names a ledger that receives nothing, which is a row with no effect and is far
    /// more likely to be a mis-keyed figure than an intention.</summary>
    public int PercentBasisPoints { get; set; }

    /// <summary>Creation order, so the allocation table lists and posts deterministically on every platform.</summary>
    public int Order { get; set; }

    public VoucherClassLedgerAllocation(Guid id, Guid ledgerId, int percentBasisPoints, int order = 0)
    {
        if (ledgerId == Guid.Empty)
            throw new ArgumentException("A default accounting allocation must name a ledger.", nameof(ledgerId));
        if (percentBasisPoints <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(percentBasisPoints),
                percentBasisPoints,
                "A default accounting allocation must allocate a positive percentage.");
        if (percentBasisPoints > FullAllocationBasisPoints)
            throw new ArgumentOutOfRangeException(
                nameof(percentBasisPoints),
                percentBasisPoints,
                "A default accounting allocation cannot allocate more than 100% of the item value.");

        Id = id;
        LedgerId = ledgerId;
        PercentBasisPoints = percentBasisPoints;
        Order = order;
    }
}
