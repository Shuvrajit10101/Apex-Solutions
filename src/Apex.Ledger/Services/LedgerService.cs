using Apex.Ledger.Domain;

namespace Apex.Ledger.Services;

/// <summary>
/// The posting service (design §8.2). Validates §6 invariants then appends a voucher to
/// the company's posted set; rejects an unbalanced/malformed voucher (never persists it);
/// supports Cancel (Alt+X, keep number) and Delete (Alt+D, may gap numbering); and
/// assigns automatic numbers per voucher type.
/// </summary>
public sealed class LedgerService
{
    private readonly Company _company;

    public LedgerService(Company company)
        => _company = company ?? throw new ArgumentNullException(nameof(company));

    /// <summary>
    /// Validates invariants (§6), assigns a number for Automatic-numbered types when the
    /// voucher's number is unset, then appends it. Throws on any violation — a bad voucher
    /// is never persisted.
    /// <para><b>Item-invoice mode (slice 3.3b).</b> When the voucher carries
    /// <see cref="Voucher.InventoryLines"/> (a Purchase/Sales run in item-invoice mode), posting is
    /// <b>atomic across accounts and stock</b>: (a) the item lines' direction is stamped from the voucher nature
    /// (Purchase ⇒ inward, Sales ⇒ outward), (b) the balanced Dr/Cr legs are validated with the pairing
    /// invariant (§10), and (c) the resulting stock movement is verified against the no-negative-stock guard
    /// (DP-7). If the stock effect is invalid (e.g. a Sales item-invoice would drive an on-hand negative), the
    /// ENTIRE post fails — the voucher is removed and nothing (no accounting leg, no stock movement) persists.</para>
    /// </summary>
    public Voucher Post(Voucher voucher)
    {
        // Item-invoice mode: stamp the voucher-nature-implied direction on every item line BEFORE validating,
        // so the pairing check and the on-hand engine both read the canonical direction.
        StampInventoryLineDirections(voucher);

        VoucherValidator.EnsureValid(voucher, _company);

        var type = _company.FindVoucherType(voucher.TypeId)!;
        if (type.Numbering == NumberingMethod.Automatic && voucher.Number <= 0)
            voucher.Number = NextNumber(voucher.TypeId);

        _company.AddVoucherInternal(voucher);

        // Atomic accounts↔stock: with the voucher provisionally appended (so its item-invoice movements are now
        // visible to the inventory engine), verify no key ever goes negative; roll the whole voucher back on
        // violation so neither the accounting leg nor the stock movement is persisted.
        if (voucher.HasInventoryLines)
        {
            try
            {
                new InventoryPostingService(_company).EnsureNoNegativeStock();
            }
            catch
            {
                _company.RemoveVoucherInternal(voucher);
                throw;
            }
        }

        return voucher;
    }

    /// <summary>
    /// Stamps each item-invoice line's <see cref="VoucherInventoryLine.Direction"/> from the voucher type's
    /// nature (Purchase ⇒ Inward, Sales ⇒ Outward). Only Purchase/Sales types are valid carriers; other types
    /// are left untouched here and rejected by the validator. Rebuilds the voucher's lines in place via the
    /// domain's own <see cref="VoucherInventoryLine.WithDirection"/> so the stored line is self-consistent.
    /// </summary>
    private void StampInventoryLineDirections(Voucher voucher)
    {
        if (!voucher.HasInventoryLines) return;
        var type = _company.FindVoucherType(voucher.TypeId);
        if (type is null) return; // referential integrity is reported by the validator

        StockDirection? dir = type.BaseType switch
        {
            VoucherBaseType.Purchase => StockDirection.Inward,
            VoucherBaseType.Sales => StockDirection.Outward,
            _ => null,
        };
        if (dir is not { } direction) return; // wrong carrier type — validator throws

        voucher.SetInventoryLineDirections(direction);
    }

    /// <summary>Alt+X — mark cancelled; keeps the number in sequence, zero effect on balances.
    /// For an item-invoice voucher, cancelling reverses its stock effect; if that would retro-drive a later
    /// movement's on-hand negative (e.g. cancelling the purchase that a later delivery drew from), the cancel is
    /// blocked and rolled back (the same DP-7 guard the pure-stock side uses).</summary>
    public void Cancel(Guid voucherId)
    {
        var v = _company.FindVoucher(voucherId)
            ?? throw new InvalidOperationException($"Voucher {voucherId} not found.");
        var was = v.Cancelled;
        v.Cancelled = true;
        if (v.HasInventoryLines)
        {
            try { new InventoryPostingService(_company).EnsureNoNegativeStock(); }
            catch { v.Cancelled = was; throw; }
        }
    }

    /// <summary>Alt+D — remove entirely; may leave a gap in numbering. For an item-invoice voucher, deleting
    /// reverses its stock effect; if that would retro-drive a later movement's on-hand negative, the delete is
    /// blocked and the voucher restored (the same DP-7 guard the pure-stock side uses).</summary>
    public void Delete(Guid voucherId)
    {
        var v = _company.FindVoucher(voucherId)
            ?? throw new InvalidOperationException($"Voucher {voucherId} not found.");
        _company.RemoveVoucherInternal(v);
        if (v.HasInventoryLines)
        {
            try { new InventoryPostingService(_company).EnsureNoNegativeStock(); }
            catch { _company.AddVoucherInternal(v); throw; }
        }
    }

    /// <summary>
    /// Converts a <b>Memorandum</b> voucher (a non-affecting suspense entry, catalog §7) into a real
    /// voucher of <paramref name="targetTypeId"/> so it now affects the books. The memo voucher is
    /// removed and a fresh voucher — same date, party, narration, and entry lines, but the chosen type —
    /// is posted through the normal validating path (so it must balance). The new voucher keeps a fresh
    /// id and takes an automatic number for its target type; its <c>Optional</c>/<c>PostDated</c> flags
    /// are cleared (a regularised entry is a real one). Returns the newly posted voucher.
    /// </summary>
    /// <exception cref="InvalidOperationException">The voucher is unknown, is not a Memorandum, or the
    /// target voucher type is unknown.</exception>
    public Voucher ConvertToRegular(Guid memorandumVoucherId, Guid targetTypeId)
    {
        var memo = _company.FindVoucher(memorandumVoucherId)
            ?? throw new InvalidOperationException($"Voucher {memorandumVoucherId} not found.");

        var sourceType = _company.FindVoucherType(memo.TypeId)
            ?? throw new InvalidOperationException($"Voucher {memorandumVoucherId} has unknown type {memo.TypeId}.");
        if (sourceType.BaseType != VoucherBaseType.Memorandum)
            throw new InvalidOperationException(
                $"Voucher {memorandumVoucherId} is a '{sourceType.Name}', not a Memorandum; only memoranda are converted.");

        if (_company.FindVoucherType(targetTypeId) is null)
            throw new InvalidOperationException($"Target voucher type {targetTypeId} not found.");

        var regular = new Voucher(
            Guid.NewGuid(),
            targetTypeId,
            memo.Date,
            memo.Lines,          // same balanced lines
            number: 0,           // take a fresh automatic number for the target type
            narration: memo.Narration,
            partyId: memo.PartyId,
            cancelled: false,
            optional: false,     // a regularised entry affects the real books
            postDated: false,
            applicableUpto: null);

        // Post first (validates); only remove the memo once the real voucher is accepted.
        Post(regular);
        _company.RemoveVoucherInternal(memo);
        return regular;
    }

    /// <summary>Next automatic number for a voucher type = max existing + 1 (per type, per company).</summary>
    public int NextNumber(Guid voucherTypeId)
    {
        var max = 0;
        foreach (var v in _company.Vouchers)
            if (v.TypeId == voucherTypeId && v.Number > max)
                max = v.Number;
        return max + 1;
    }
}
