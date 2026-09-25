using System;

namespace Apex.Desktop.ViewModels;

/// <summary>
/// The two-sided result of <see cref="InventoryVoucherEntryViewModel.ForAlter"/>: either a rehydrated entry
/// screen, or a NAMED refusal. The pure-stock sibling of <see cref="VoucherAlterationOpen"/>, and it is a
/// separate type only because it carries a different view model — the DISCIPLINE is the shared one.
///
/// <para>🔴 <b>Why it is not a nullable view model.</b> A <c>null</c> return conflates "refused" with "nothing
/// here", and a caller that dropped the distinction would make Ctrl+Enter indistinguishable from a dead key.
/// That is the exact defect census row 9.2 records against the Job Work registers — <i>"a silent no-op with no
/// named refusal is the worst of the three failure modes, because the operator believes the correction
/// landed."</i> Making the refusal a value the caller must handle is what stops this door reintroducing it.</para>
/// </summary>
public sealed class InventoryVoucherAlterationOpen
{
    private InventoryVoucherAlterationOpen(InventoryVoucherEntryViewModel? entry, string? refusal)
    {
        Entry = entry;
        Refusal = refusal;
    }

    /// <summary>The rehydrated entry screen, or <c>null</c> when the alteration was refused.</summary>
    public InventoryVoucherEntryViewModel? Entry { get; }

    /// <summary>The named, family-specific refusal, or <c>null</c> when the screen opened.</summary>
    public string? Refusal { get; }

    /// <summary>True when the alteration was refused; <see cref="Refusal"/> is then non-empty.</summary>
    public bool IsRefused => Entry is null;

    internal static InventoryVoucherAlterationOpen Opened(InventoryVoucherEntryViewModel entry) =>
        new(entry ?? throw new ArgumentNullException(nameof(entry)), refusal: null);

    internal static InventoryVoucherAlterationOpen Refused(string refusal) =>
        string.IsNullOrWhiteSpace(refusal)
            // A blank refusal IS the silent no-op this type exists to make impossible, so it is a bug in the
            // predicate rather than a state a caller should have to handle.
            ? throw new ArgumentException("A refusal must name the family and the reason.", nameof(refusal))
            : new InventoryVoucherAlterationOpen(entry: null, refusal);
}
