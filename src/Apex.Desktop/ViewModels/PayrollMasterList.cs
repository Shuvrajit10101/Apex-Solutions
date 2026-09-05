using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Apex.Desktop.ViewModels;

/// <summary>
/// One row of a payroll master's existing-list, resolvable back to the master it displays.
///
/// <para><b>Why the id matters</b> (census row 7.16 / <c>T2-…</c>): the payroll master lists all shipped ID-LESS.
/// The operator could SEE every employee, pay head and attendance type, but no row could be resolved back to the
/// master it displayed — so there was nothing for a drill key to open and nothing for a delete key to name. That
/// is the same gap that left <c>StockItemMasterViewModel.ForAlter</c> with zero production callers, repeated eight
/// times. An id on the row is what makes payroll master alteration and deletion REACHABLE.</para>
/// </summary>
public interface IPayrollMasterListRow : IMasterListRow
{
}

/// <summary>
/// One row of ANY master's existing-list that the arrows can walk, Ctrl+Enter can open and Alt+D can delete.
///
/// <para>Extracted from <see cref="IPayrollMasterListRow"/> at W2-03, when the <b>Voucher Type</b> master became
/// the first non-payroll screen to need the same three verbs over the same kind of list. Nothing about the
/// contract is payroll-specific — it never was — so the payroll interface now derives from this one and every
/// existing implementation and call site is unaffected.</para>
/// </summary>
public interface IMasterListRow
{
    /// <summary>The stable identity of the master this row displays.</summary>
    Guid MasterId { get; }

    /// <summary>The operator-facing name of the master, used verbatim in the delete confirmation.</summary>
    string MasterName { get; }

    /// <summary>True while this row carries the keyboard highlight (Up/Down move it).</summary>
    bool IsHighlighted { get; set; }
}

/// <summary>
/// A master screen whose existing-list can be walked with the arrows, opened for <b>alteration</b> with
/// Ctrl+Enter and <b>deleted</b> with Alt+D — the shell-facing half of the contract, with no reference to any
/// particular master family.
///
/// <para>Extracted from <see cref="IPayrollMasterList"/> at W2-03 for the Voucher Type master (census 2.4), which
/// needs exactly these five members and is not a payroll screen. The extraction is deliberately shape-preserving:
/// <see cref="IPayrollMasterList"/> now derives from this and adds nothing, so the four payroll kinds that
/// implement it, the shell arms that resolve it and
/// <c>PayrollMasterHalfWiredKindsTests</c>'s lock on the remainder all behave exactly as before. The alternative
/// — a second, parallel set of arrow / Ctrl+Enter / Alt+D arms for the new screen — is the very shape
/// <see cref="IPayrollMasterList"/>'s own remarks give as how one kind silently ends up gated differently from
/// the rest.</para>
/// </summary>
public interface IMasterListScreen
{
    /// <summary>The master kind in the operator's words, lower case, e.g. <c>"voucher type"</c>. Used in the
    /// delete confirmation, so it reads as a sentence: <i>Delete voucher type 'Export Sales'?</i></summary>
    string MasterKindLabel { get; }

    /// <summary>The highlighted existing-master row, or <c>null</c> when nothing is highlighted.</summary>
    IMasterListRow? HighlightedMasterRow { get; }

    /// <summary>True iff this screen is ALTERING an existing master rather than creating one. Alt+D is refused
    /// while it is true — deleting the master you are part-way through editing is never what was meant.</summary>
    bool IsAltering { get; }

    /// <summary>Moves the existing-list highlight by <paramref name="direction"/> (−1 up, +1 down), wrapping.</summary>
    void MoveHighlight(int direction);

    /// <summary>Re-renders the existing-list from the company, after a create, an alter or a delete.</summary>
    void ReloadExisting();

    /// <summary>
    /// Deletes the named master through its own engine service, which owns the referential guards. Throws
    /// <see cref="InvalidOperationException"/> with the guard's own message when the deletion is refused — the
    /// shell turns that into a notice rather than a crash.
    /// </summary>
    void DeleteMaster(Guid id);
}

/// <summary>
/// A payroll master screen whose existing-list can be walked with the arrows, opened for <b>alteration</b> with
/// Ctrl+Enter and <b>deleted</b> with Alt+D — the capability census row 7.16 records as absent across all eight
/// payroll master kinds.
///
/// <para>🔴 <b>SHIPPED COVERAGE: FOUR of those eight implement this interface today</b> — employee category,
/// employee group, payroll unit and attendance/production type. The employee, pay head, salary structure and tax
/// declaration masters do NOT, and the row is therefore NOT closed. Do not read the "all eight" above as a claim
/// about what is built: it describes the DEFECT, not the fix. <c>MainWindowViewModel.PayrollMasterScreen</c>
/// carries the exact remainder and <c>PayrollMasterHalfWiredKindsTests</c> holds it to it.</para>
///
/// <para><b>Why an interface rather than eight copies of the same eight members.</b> The shell needs exactly one
/// arrow arm, one Ctrl+Enter arm, one Alt+D arm and one refresh arm for the whole family. Eight parallel arms is
/// how one of them silently ends up gated differently from the other seven — which is precisely the shape of the
/// defect this row records (the payroll service <i>advertised</i> create/alter/delete in its own doc comment and
/// nothing reached the last two). With one arm each, a screen either implements this interface and gets all three
/// verbs, or it does not appear on any of them.</para>
///
/// <para><b>Alteration is deliberately NOT on this interface.</b> Opening a master for alteration is a static
/// factory per type (<c>ForAlter</c>), and it has to build a whole screen with its own pickers — so the shell
/// resolves it by screen in one place. Everything that can be shared is here; nothing is faked to look shared.</para>
/// </summary>
public interface IPayrollMasterList : IMasterListScreen
{
}

/// <summary>
/// The keyboard-highlight machinery every payroll master's existing-list needs, written once.
///
/// <para>It is a plain helper rather than a base class because the eight master view models already derive from
/// <see cref="ViewModelBase"/> and carry source-generated observable properties; giving them a second inheritance
/// story to satisfy a shared highlight would be the tail wagging the dog.</para>
/// </summary>
/// <typeparam name="TRow">The master's own list-row type. Constrained to <see cref="IMasterListRow"/> rather than
/// to the payroll interface since W2-03, so the Voucher Type master reuses this machinery verbatim instead of
/// growing a second, subtly-different highlight (by-index restore being the trap that one would fall into).
/// </typeparam>
public sealed class PayrollMasterHighlight<TRow> where TRow : class, IMasterListRow
{
    private readonly ObservableCollection<TRow> _rows;
    private readonly Action _notify;

    /// <param name="rows">The screen's existing-list collection.</param>
    /// <param name="notify">Raises <c>PropertyChanged</c> for the screen's highlighted-row property.</param>
    public PayrollMasterHighlight(ObservableCollection<TRow> rows, Action notify)
    {
        _rows = rows ?? throw new ArgumentNullException(nameof(rows));
        _notify = notify ?? throw new ArgumentNullException(nameof(notify));
    }

    /// <summary>The index of the highlighted row, or −1 when nothing is highlighted.</summary>
    public int Index { get; private set; } = -1;

    /// <summary>The highlighted row, or <c>null</c>.</summary>
    public TRow? Row => Index >= 0 && Index < _rows.Count ? _rows[Index] : null;

    /// <summary>
    /// Moves the highlight by <paramref name="direction"/>, wrapping. The FIRST press on an untouched screen lands
    /// on row 0 rather than jumping to the end, so Down reads as "enter the list". A no-op on an empty list, so
    /// the arrows stay harmless on a company with no payroll masters of this kind yet.
    /// </summary>
    public void Move(int direction)
    {
        if (_rows.Count == 0) { Set(-1); return; }
        if (Index < 0) { Set(direction >= 0 ? 0 : _rows.Count - 1); return; }
        Set((Index + direction + _rows.Count) % _rows.Count);
    }

    /// <summary>The id the highlight was on before a rebuild, so it can be put back on the SAME master.</summary>
    public Guid? IdBeforeRebuild() => Row?.MasterId;

    /// <summary>
    /// Puts the highlight back on the master it was on before the list was rebuilt, by ID rather than by index.
    /// By index, an alter that renames a master (and so re-sorts the list) would silently move the highlight onto
    /// a NEIGHBOURING master — which the next Ctrl+Enter would open and the next Alt+D would delete.
    /// </summary>
    public void RestoreTo(Guid? previous)
    {
        var index = -1;
        if (previous is { } id)
        {
            for (var i = 0; i < _rows.Count; i++)
                if (_rows[i].MasterId == id) { index = i; break; }
        }
        Set(index);
    }

    private void Set(int index)
    {
        for (var i = 0; i < _rows.Count; i++) _rows[i].IsHighlighted = i == index;
        Index = index >= 0 && index < _rows.Count ? index : -1;
        _notify();
    }
}
