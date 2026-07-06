using System.Collections.Generic;

namespace Apex.Desktop.ViewModels;

/// <summary>
/// One column of a master-list export snapshot: the on-screen caption plus whether the column holds an
/// amount (so the projector emits <see cref="Apex.Ledger.Io.CellType.Number"/> cells a spreadsheet can sum,
/// parsing the grid's Indian-grouped string back to an exact decimal). <see cref="IsNumeric"/> is
/// <c>false</c> for name / group / kind columns.
/// </summary>
public readonly record struct MasterListColumn(string Caption, bool IsNumeric)
{
    /// <summary>A text column (name / group / nature / kind).</summary>
    public static MasterListColumn Text(string caption) => new(caption, false);

    /// <summary>A numeric column (an opening balance / value / amount) — a spreadsheet stores a real number.</summary>
    public static MasterListColumn Number(string caption) => new(caption, true);
}

/// <summary>
/// A framework-agnostic snapshot of a master-list grid: its worksheet title, the ordered column captions
/// (with their numeric flag) and the rows, each already a list of the grid's display strings aligned to the
/// columns. This is the pure hand-off the generic <see cref="Apex.Desktop.Services.MasterListTabularProjector"/>
/// turns into a <see cref="Apex.Ledger.Io.TabularExport"/> — no Avalonia, no clock, no engine re-computation.
/// </summary>
public sealed record MasterListSnapshot(
    string Title,
    IReadOnlyList<MasterListColumn> Columns,
    IReadOnlyList<IReadOnlyList<string>> Rows);

/// <summary>
/// Implemented by any master-list page view-model (Chart of Accounts, Ledgers, Stock Items, Groups, Cost
/// Centres / Categories, Godowns, Units, Currencies, Scenarios, Budgets, …) so the single E / Alt+E Export
/// action works uniformly on EVERY master-list screen (RQ-14/16, slice 13 audit). Each VM returns a
/// <see cref="MasterListSnapshot"/> built straight from its already-displayed <c>Existing</c> rows — the
/// export mirrors exactly what the grid shows (RQ-15). The generic projector recovers amounts from the
/// snapshot's numeric columns, so a new master screen becomes exportable the moment it implements this.
/// </summary>
public interface IMasterListExportSource
{
    /// <summary>Snapshots the currently-displayed master-list grid (captions + numeric flags + string rows).</summary>
    MasterListSnapshot ToMasterListSnapshot();
}
