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
    /// </summary>
    public Voucher Post(Voucher voucher)
    {
        VoucherValidator.EnsureValid(voucher, _company);

        var type = _company.FindVoucherType(voucher.TypeId)!;
        if (type.Numbering == NumberingMethod.Automatic && voucher.Number <= 0)
            voucher.Number = NextNumber(voucher.TypeId);

        _company.AddVoucherInternal(voucher);
        return voucher;
    }

    /// <summary>Alt+X — mark cancelled; keeps the number in sequence, zero effect on balances.</summary>
    public void Cancel(Guid voucherId)
    {
        var v = _company.FindVoucher(voucherId)
            ?? throw new InvalidOperationException($"Voucher {voucherId} not found.");
        v.Cancelled = true;
    }

    /// <summary>Alt+D — remove entirely; may leave a gap in numbering.</summary>
    public void Delete(Guid voucherId)
    {
        var v = _company.FindVoucher(voucherId)
            ?? throw new InvalidOperationException($"Voucher {voucherId} not found.");
        _company.RemoveVoucherInternal(v);
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
