namespace Apex.Desktop.ViewModels;

/// <summary>
/// What a Ctrl+Enter alteration request DID — the three outcomes
/// <see cref="MainWindowViewModel.RequestAlterHighlightedVoucher"/> can have, kept apart because the window's key
/// arm must treat two of them alike and the third differently.
///
/// <para>🔴 <b>Why this is not a <c>bool</c>.</b> A bool conflates <see cref="NoVoucherHere"/> with
/// <see cref="Refused"/>, and the window arm has to tell them apart:
/// <list type="bullet">
///   <item><see cref="NoVoucherHere"/> must FALL THROUGH to the RQ-7 drill below the arm. Ctrl+Enter on a
///     Trial Balance ledger row, or on any header/total row, drilled before this arm existed (the drill arm tests
///     <c>e.Key == Key.Enter</c> with no modifier test at all). Consuming those would take a working behaviour
///     away.</item>
///   <item><see cref="Refused"/> must be TERMINAL. The refusal has just been written to
///     <see cref="MainWindowViewModel.Notice"/>, and <c>OnCurrentScreenChanged</c> clears that bar on any change
///     of screen — so falling through to a drill would open the voucher-detail column and WIPE the sentence
///     explaining why the alteration could not happen, on its way past. That is the "reported through a channel
///     the page could not render" defect S3's review found, arriving by a different route.</item>
/// </list></para>
/// </summary>
public enum VoucherAlterationRequest
{
    /// <summary>Nothing on this surface resolves to a posted voucher — no selection, a header, a total, an
    /// empty-state note, or a row whose drill id is not a voucher. A quiet no-op; the keystroke is NOT ours.</summary>
    NoVoucherHere,

    /// <summary>A posted voucher was found and <see cref="VoucherEntryViewModel.ForAlter"/> refused its shape by
    /// name. The sentence is on the notice bar and the keystroke IS ours.</summary>
    Refused,

    /// <summary>The alteration screen is open over the surface the operator drilled from.</summary>
    Opened,
}
