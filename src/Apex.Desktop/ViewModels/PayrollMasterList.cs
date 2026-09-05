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
public interface IPayrollMasterListRow
{
    /// <summary>The stable identity of the master this row displays.</summary>
    Guid MasterId { get; }

    /// <summary>The operator-facing name of the master, used verbatim in the delete confirmation.</summary>
    string MasterName { get; }

    /// <summary>True while this row carries the keyboard highlight (Up/Down move it).</summary>
    bool IsHighlighted { get; set; }
}

/// <summary>
/// A payroll master screen whose existing-list can be walked with the arrows, opened for <b>alteration</b> with
/// Ctrl+Enter and <b>deleted</b> with Alt+D — the capability census row 7.16 records as absent across all eight
/// payroll master kinds.
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
public interface IPayrollMasterList
{
    /// <summary>The master kind in the operator's words, lower case, e.g. <c>"employee category"</c>. Used in the
    /// delete confirmation, so it reads as a sentence: <i>Delete employee category 'On-Roll'?</i></summary>
    string MasterKindLabel { get; }

    /// <summary>The highlighted existing-master row, or <c>null</c> when nothing is highlighted.</summary>
    IPayrollMasterListRow? HighlightedMasterRow { get; }

    /// <summary>True iff this screen is ALTERING an existing master rather than creating one. Alt+D is refused
    /// while it is true — deleting the master you are part-way through editing is never what was meant.</summary>
    bool IsAltering { get; }

    /// <summary>Moves the existing-list highlight by <paramref name="direction"/> (−1 up, +1 down), wrapping.</summary>
    void MoveHighlight(int direction);

    /// <summary>Re-renders the existing-list from the company, after a create, an alter or a delete.</summary>
    void ReloadExisting();

    /// <summary>
    /// Deletes the named master through its own engine service, which owns the referential guards (predefined
    /// masters, masters in use). Throws <see cref="InvalidOperationException"/> with the guard's own message when
    /// the deletion is refused — the shell turns that into a notice rather than a crash.
    /// </summary>
    void DeleteMaster(Guid id);
}

/// <summary>
/// The keyboard-highlight machinery every payroll master's existing-list needs, written once.
///
/// <para>It is a plain helper rather than a base class because the eight master view models already derive from
/// <see cref="ViewModelBase"/> and carry source-generated observable properties; giving them a second inheritance
/// story to satisfy a shared highlight would be the tail wagging the dog.</para>
/// </summary>
/// <typeparam name="TRow">The master's own list-row type.</typeparam>
public sealed class PayrollMasterHighlight<TRow> where TRow : class, IPayrollMasterListRow
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
