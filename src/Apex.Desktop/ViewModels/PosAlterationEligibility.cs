using System;
using System.Linq;
using Apex.Ledger.Domain;

namespace Apex.Desktop.ViewModels;

/// <summary>
/// 🔴 <b>Phase 10.11 S5e — the refusal predicate for <see cref="PosBillingViewModel.ForAlter"/>.</b> The POS
/// sibling of <see cref="VoucherAlterationEligibility"/>, asking the same one question about one posted voucher —
/// <i>can this screen rebuild it from what was posted?</i> — and answering with <c>null</c> or a named,
/// family-specific sentence, never with a bare false.
///
/// <para>🔴 <b>Why a POS bill needs its OWN door rather than a lifted refusal on the accounting entry screen.</b>
/// A POS bill's single customer debit is replaced by a SPLIT of tender debits, and the per-tender metadata that
/// the balanced <c>EntryLine</c> cannot carry — kind, cash tendered, change, card number, drawee bank, cheque
/// number — lives in <see cref="Voucher.PosTenders"/>. Every one of those fields IS persisted, so the bill is
/// fully recoverable; what it is not is <i>expressible</i> on either grid of the accounting entry screen. The
/// plain Dr/Cr grid would rebuild the split as ordinary debits, and the item grid derives exactly one party debit,
/// which is precisely the leg a POS bill does not have. The POS billing screen is the only screen that keys a
/// tender split, so it is the only screen that can invert one. <see cref="VoucherAlterationEligibility"/>'s POS
/// refusal therefore STANDS and is correct — it answers a narrower question about a different screen.</para>
///
/// <para><b>The book-level refusals are NOT re-implemented here.</b> An advance record, a §34 link, a challan
/// link, a live IRN and the provisional-state vector's shape are facts about the BOOK, not about which grid
/// re-keys the voucher, so they come from <see cref="VoucherAlterationEligibility.BookLevelRefusalFor"/> — the
/// same three arms the accounting door ends in.</para>
///
/// <para>🔴 <b>Note precisely what that buys, because this comment used to overstate it</b> (review finding
/// L3-booklevel-refusal-has-one-consumer-not-two). The ARMS are single-sourced: both doors run
/// <c>OffLineSideEffectRefusal</c>, <c>StatutoryDocumentRefusal</c> and <c>ProvisionalShapeRefusal</c>, so neither
/// can hold a weaker copy of any one of them. The COMPOSITION is not: the accounting door chains those three
/// itself, interleaved with its screen-level arms, so a fourth book-level refusal added as a NEW private method
/// wired only into that chain would never be asked here. A fourth arm belongs INSIDE one of the three.</para>
///
/// <para>🔴 The single call below is pinned by three tests — a live IRN, a §34 link and an 'Applicable Upto',
/// each driven through <c>PosBillingViewModel.ForAlter</c>. Delete the call and all three redden; before they
/// existed it returned <c>null</c> on every POS shape the suite built, so deleting it reddened nothing.</para>
/// </summary>
public static class PosAlterationEligibility
{
    /// <summary>
    /// The refusal for the voucher <paramref name="voucherId"/> names, or <c>null</c> when the POS billing screen
    /// can rebuild it.
    /// </summary>
    public static string? RefusalFor(Company company, Guid voucherId)
    {
        ArgumentNullException.ThrowIfNull(company);

        if (company.FindVoucher(voucherId) is not { } voucher)
            return "That bill is no longer in this company's books — it may have been deleted since the report was "
                 + "drawn. Re-open the report and try again.";

        if (company.FindVoucherType(voucher.TypeId) is not { } type)
            return "This bill's voucher type is missing from the company, so the POS screen cannot be re-opened "
                 + "for it. Restore the voucher type first.";

        return RefusalFor(company, voucher, type);
    }

    /// <summary>The predicate proper, over an already-resolved voucher and its type.</summary>
    public static string? RefusalFor(Company company, Voucher voucher, VoucherType type)
    {
        ArgumentNullException.ThrowIfNull(company);
        ArgumentNullException.ThrowIfNull(voucher);
        ArgumentNullException.ThrowIfNull(type);

        // The door itself. Anything that is not a POS bill belongs to the accounting entry screen, and saying so
        // is better than opening a tender panel over a voucher that has no tenders.
        if (!type.IsPosSales)
            return $"'{type.Name}' is not a POS voucher type, so the POS billing screen cannot re-open this "
                 + "voucher. Alter it on the accounting voucher screen.";

        // Both of these are written by EVERY bill this screen posts (Accept builds the tender records and the
        // outward item lines before it constructs the voucher), so a POS-typed voucher missing either can only
        // have arrived from an import — and the screen would have to invent the missing half.
        if (!voucher.HasPosTenders)
            return "This voucher is on a POS type but carries no tender records at all, so the payment panel has "
                 + "nothing to re-key: the POS screen would have to invent a tender split the bill never had.";

        if (!voucher.HasInventoryLines)
            return "This POS bill carries no item lines, so the billing grid has nothing to re-key — every bill "
                 + "this screen posts moves stock, so it can only have arrived from an import.";

        // 🔴 TWO TENDERS OF ONE KIND CANNOT BE RE-KEYED, and the payment panel cannot even say so. It has FOUR
        // FIXED ROWS, one per kind (PosBillingViewModel.Tenders, addressed as Gift/Card/Cheque/Cash), and
        // RehydrateTenders writes each posted tender into the row its TYPE selects — so two tenders of one kind
        // land in ONE row and the second silently overwrites the first's amount, its ledger and its
        // card/bank/cheque reference. Recalculate then re-cuts the cash residual over the survivor and Σ tenders
        // foots to the bill again, so the screen reports Balanced and nothing downstream reports anything.
        //
        // MEASURED, on the shape the tests build: a Rs 1,180.00 bill settled Card 600.00 + Card 400.00 +
        // Cash 180.00 re-opens as Card 400.00, the residual is re-cut 180.00 -> 780.00, and re-accepting moves
        // Rs 600.00 out of the bank and into the cash drawer on a bill nobody touched. Two CASH tenders on two
        // drawers move the whole of the first drawer's share to the second, which is why this arm is keyed on the
        // tender KIND and not on the Card panel.
        //
        // 🔴 REFUSED RATHER THAN REPRESENTED, and the choice is not a preference. Preserving N tenders of one kind
        // is not a rehydration fix: the four rows are the SCREEN's shape — the fresh-entry path can only ever
        // produce one tender per kind, the AXAML binds one panel per kind, and TryBuildTenders builds one tender
        // per row — so representing the shape is a new payment-panel design and an R6 plan row, not a defect fix.
        // Nor can the shape be declared illegal: pos_tender_allocations keys on an AUTOINCREMENT surrogate with NO
        // unique index on (voucher_id, tender_type), and PosTenderService imposes only Σ tenders == total debit
        // and the per-tender ledger grouping, so an existing book can hold one and it survives a full save/reload.
        // What is left is to refuse the bill BY NAME at the DOOR — which also puts the refusal on the accept path,
        // because AcceptAlterationCore re-runs this predicate before it replaces anything.
        if (voucher.PosTenders.GroupBy(t => t.Type).FirstOrDefault(g => g.Count() > 1) is { } duplicated)
            return $"This bill is settled with {duplicated.Count()} {TenderKindName(duplicated.Key)} tenders, and "
                 + "the payment panel has exactly one row per tender kind. Re-opening it here would keep only the "
                 + "last of them and re-cut the cash residual over what is left, moving money between the ledgers "
                 + "those tenders debit. A bill split twice across one kind can only have arrived from an import: "
                 + "correct it there, or cancel this bill and re-key it.";

        return VoucherAlterationEligibility.BookLevelRefusalFor(company, voucher, type)
            // The POS screen runs the ordinary outward GST engine and nothing else, so exactly the same
            // engine-stamp question applies to it as to the item grid: a withholding, a TCS collection, a
            // reverse-charge pair or a statutory adjustment has no re-derivation here.
            ?? VoucherAlterationEligibility.ItemGridDerivedLegRefusal(voucher);
    }

    /// <summary>The tender kind as the payment panel labels it, so the refusal names the row the operator sees.</summary>
    private static string TenderKindName(PosTenderType type) => type switch
    {
        PosTenderType.GiftVoucher => "Gift Voucher",
        _ => type.ToString(),
    };
}

/// <summary>
/// The outcome of <see cref="PosBillingViewModel.ForAlter"/> — <b>either</b> a rehydrated POS screen <b>or</b> a
/// named refusal, never neither and never both. The POS sibling of <see cref="VoucherAlterationOpen"/>, and it is
/// a type for the same reason: a caller handed a bare <c>null</c> has nothing to show the operator, and a failed
/// alteration that looks like a dead key is the exact failure ORCHESTRATOR RULING 1 forbids.
/// </summary>
public sealed class PosAlterationOpen
{
    private PosAlterationOpen(PosBillingViewModel? entry, string? refusal)
    {
        Entry = entry;
        Refusal = refusal;
    }

    /// <summary>The rehydrated POS billing screen, or <c>null</c> when the alteration was refused.</summary>
    public PosBillingViewModel? Entry { get; }

    /// <summary>The named, family-specific refusal, or <c>null</c> when the screen opened.</summary>
    public string? Refusal { get; }

    /// <summary>True when the alteration was refused; <see cref="Refusal"/> is then non-empty.</summary>
    public bool IsRefused => Entry is null;

    internal static PosAlterationOpen Opened(PosBillingViewModel entry) =>
        new(entry ?? throw new ArgumentNullException(nameof(entry)), refusal: null);

    internal static PosAlterationOpen Refused(string refusal) =>
        string.IsNullOrWhiteSpace(refusal)
            ? throw new ArgumentException("A refusal must name the family and the reason.", nameof(refusal))
            : new PosAlterationOpen(entry: null, refusal);
}
