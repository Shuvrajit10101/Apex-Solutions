using System;
using System.Globalization;
using System.Linq;
using Apex.Ledger.Domain;
using Apex.Desktop.Services;

namespace Apex.Desktop.ViewModels;

/// <summary>Shared helpers for the Phase-9 advanced-GST snapshot-driven report screens (2B reconciliation, ITC-gate,
/// ITC reversal): turning a <c>yyyy-MM</c> GSTR-2B return period into its calendar-month <c>[from, to]</c> window and
/// picking the company's latest imported GSTR-2B snapshot.</summary>
internal static class GstAdvancedSnapshots
{
    /// <summary>The calendar-month window for a <c>yyyy-MM</c> return period; falls back to <paramref name="fallback"/>'s
    /// month when the string is malformed.</summary>
    public static (DateOnly From, DateOnly To) Window(string returnPeriod, DateOnly fallback)
    {
        if (returnPeriod.Length >= 7
            && int.TryParse(returnPeriod.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var year)
            && int.TryParse(returnPeriod.AsSpan(5, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var month)
            && month is >= 1 and <= 12)
        {
            var from = new DateOnly(year, month, 1);
            return (from, from.AddMonths(1).AddDays(-1));
        }
        var f = new DateOnly(fallback.Year, fallback.Month, 1);
        return (f, f.AddMonths(1).AddDays(-1));
    }

    /// <summary>The company's imported GSTR-2B snapshots (the static ITC gatekeeper only), latest-import first.</summary>
    public static System.Collections.Generic.IEnumerable<Gstr2bSnapshot> Gstr2b(Company company) =>
        company.Gstr2bSnapshots
            .Where(s => s.StatementType == GstStatementType.Gstr2b)
            .OrderByDescending(s => s.ImportedAt)
            .ThenByDescending(s => s.ReturnPeriod, StringComparer.Ordinal);
}

/// <summary>A selectable financial year on a Phase-9 advanced-GST report screen (its 01-Apr start year + the "2024-25"
/// label). Shared by the GSTR-9/9C, Electronic-Ledgers, ITC set-off/reversal, QRMP and amendment report view models —
/// each maps the chosen year to the FY window <c>[fyFrom, fyTo]</c> the pure engine is built over.</summary>
public sealed class GstAdvFyOption
{
    public int StartYear { get; init; }
    public string Label => $"{StartYear}-{(StartYear + 1) % 100:00}";
    public override string ToString() => Label;
}

/// <summary>A selectable imported GSTR-2B snapshot on the 2B-reconciliation / ITC-gate report screens (its return
/// period + a "GSTR-2B · 2024-07 · imported 12-Aug" label). The reconciler + ITC-gate are built over the chosen
/// snapshot's return-period window.</summary>
public sealed class Gstr2bSnapshotOption
{
    public required Gstr2bSnapshot Snapshot { get; init; }

    public string Label =>
        $"{(Snapshot.StatementType == GstStatementType.Gstr2b ? "GSTR-2B" : "GSTR-2A")}  ·  {Snapshot.ReturnPeriod}" +
        $"  ·  imported {ApexDate.Format(Snapshot.ImportedAt)}";

    public override string ToString() => Label;
}
