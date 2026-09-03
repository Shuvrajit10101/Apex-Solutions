using System;

namespace Apex.Desktop.ViewModels;

/// <summary>
/// The outcome of <see cref="VoucherEntryViewModel.ForAlter"/> — <b>either</b> a rehydrated entry screen
/// <b>or</b> a named refusal, never neither and never both.
///
/// <para>🔴 <b>Why this is a type and not a nullable view model.</b> ORCHESTRATOR RULING 1's standard is that
/// <i>"a silent no-op is the failure mode being avoided"</i>, and a caller handed a bare <c>null</c> has nothing
/// to show the operator — the outcome would be indistinguishable from a dead key, which is the precise defect the
/// Alt+X arm's notice bar was added to close. Making the refusal a required half of the result means a caller
/// cannot drop it by accident, and it is what lets the §6.6a.7 coverage test assert something stronger than
/// "nothing happened": that EVERY seeded base kind yields either a screen or a non-empty, family-specific
/// sentence.</para>
/// </summary>
public sealed class VoucherAlterationOpen
{
    private VoucherAlterationOpen(VoucherEntryViewModel? entry, string? refusal)
    {
        Entry = entry;
        Refusal = refusal;
    }

    /// <summary>The rehydrated entry screen, or <c>null</c> when the alteration was refused.</summary>
    public VoucherEntryViewModel? Entry { get; }

    /// <summary>The named, family-specific refusal, or <c>null</c> when the screen opened.</summary>
    public string? Refusal { get; }

    /// <summary>True when the alteration was refused; <see cref="Refusal"/> is then non-empty.</summary>
    public bool IsRefused => Entry is null;

    internal static VoucherAlterationOpen Opened(VoucherEntryViewModel entry) =>
        new(entry ?? throw new ArgumentNullException(nameof(entry)), refusal: null);

    internal static VoucherAlterationOpen Refused(string refusal) =>
        string.IsNullOrWhiteSpace(refusal)
            // A blank refusal IS the silent no-op this type exists to make impossible, so it is a bug in the
            // predicate rather than a state a caller should have to handle.
            ? throw new ArgumentException("A refusal must name the family and the reason.", nameof(refusal))
            : new VoucherAlterationOpen(entry: null, refusal);
}
