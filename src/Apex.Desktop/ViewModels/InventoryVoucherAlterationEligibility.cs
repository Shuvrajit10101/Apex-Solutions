using System;
using System.Linq;
using Apex.Ledger.Domain;

namespace Apex.Desktop.ViewModels;

/// <summary>
/// 🔴 <b>The BOOK-level refusals for altering a posted pure-stock voucher</b> — the pure-stock sibling of
/// <see cref="VoucherAlterationEligibility"/>, asked by every alteration door before any screen is built.
///
/// <para><b>The split is the same one the accounting side draws, deliberately.</b> What lives here is what is
/// true about the BOOK regardless of which grid re-keys the voucher: does it exist, is it in this aggregate at
/// all, is its type still present. What each entry screen can or cannot REBUILD is a screen-level question and
/// lives in that screen's own <c>RehydrateFrom</c>, because the answer differs between the three pure-stock
/// entry screens (<see cref="InventoryVoucherEntryViewModel"/>, <c>MaterialMovementEntryViewModel</c> and
/// <c>JobWorkOrderEntryViewModel</c>) and a single enumeration here would have to know all three.</para>
///
/// <para>🔴 <b>A cancelled voucher is NOT refused here, and that is a match with the accounting door rather than
/// an oversight.</b> <c>VoucherAlterationEligibility</c> has no cancelled arm either; the flag is carried
/// forward unchanged and <c>InventoryPostingService.Replace</c> refuses any CHANGE to it by name, because
/// un-cancelling is Alt+X's verb and belongs in its own edit-log entry. Refusing here instead would have been a
/// second, divergent rule for the same fact.</para>
/// </summary>
public static class InventoryVoucherAlterationEligibility
{
    /// <summary>
    /// The refusal for altering <paramref name="voucherId"/> as a pure-stock voucher, or <c>null</c> when the
    /// book has no objection and the screen may try to rehydrate it.
    /// </summary>
    public static string? RefusalFor(Company company, Guid voucherId)
    {
        ArgumentNullException.ThrowIfNull(company);

        if (company.FindInventoryVoucher(voucherId) is not { } voucher)
        {
            // 🔴 The MIRROR of VoucherAlterationEligibility's inventory arm. The two aggregates share one id
            // space (InventoryPostingService.Post enforces it), so a caller can land in the wrong door with a
            // perfectly valid Guid — and a bare "not found" is indistinguishable from a mistyped one. Written for
            // the OPERATOR: the family, the reason in their vocabulary, and the screen to go to.
            if (company.FindVoucher(voucherId) is not null)
                return "This is an accounting voucher — it records double entry, not goods moving — so the "
                     + "inventory voucher screen cannot re-open it. Alter it from the Day Book or its register.";

            return "That voucher is no longer in this company's books — it may have been deleted since the "
                 + "report was drawn. Re-open the report and try again.";
        }

        if (company.FindVoucherType(voucher.TypeId) is null)
            return "This voucher's type is missing from the company, so the entry screen cannot be re-opened "
                 + "for it. Restore the voucher type first.";

        return null;
    }

    /// <summary>
    /// True when <paramref name="voucherId"/> names a voucher in the pure-stock aggregate — the test the shell
    /// uses to decide WHICH alteration door a highlighted row belongs to. A pure lookup, no refusal text.
    /// </summary>
    public static bool IsInventoryVoucher(Company company, Guid voucherId)
    {
        ArgumentNullException.ThrowIfNull(company);
        return company.InventoryVouchers.Any(v => v.Id == voucherId);
    }
}
