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

/// <summary>
/// 🔴 <b>W-BJ2 — the contract a financial-year-scoped advanced-GST page must satisfy to be RE-HOMED onto the
/// shared report surface.</b> Every one of these pages replaced the vendor's report period control with a
/// financial-year <see cref="GstAdvFyOption"/> ComboBox of its own, which is the gesture a dedicated page
/// <c>Screen</c> switches off: the shell's report context goes null, and with it F2 as-of, Alt+F2 period, the
/// F12 configuration panel, Ctrl+H saved views and the report's own Enter-drill, all at once. The vendor's
/// keyboard-shortcuts page gives <c>F2</c> as <i>"To change the date of voucher entry or date/period for
/// reports"</i> — opened by content on 2026-10-20 at
/// <c>help.tallysolutions.com/tally-prime/keyboard-shortcuts/keyboard-shortcuts-tally-prime/</c> — so the
/// period control is the reference product's gesture on a REPORT, generally, and not a convenience.
///
/// <para>🔴 <b>WHY THE RE-HOME READS THE PAGE'S OWN SNAPSHOT INSTEAD OF RE-DERIVING ITS ROWS.</b>
/// <see cref="IMasterListExportSource.ToMasterListSnapshot"/> is already the authoritative projection of the
/// page's grid — it is what E / Alt+E exports today. Building the re-homed report FROM it means the report is
/// cell-for-cell the superseded page by construction, which is provable by a test, and no arithmetic is
/// transcribed twice. Transcribing instead is how a re-home loses a figure between the engine and the grid.</para>
///
/// <para><see cref="SelectedYear"/> is what the shared surface DRIVES: the report period's financial year is
/// pushed into it, so Alt+F2 genuinely re-scopes the projection rather than decorating a fixed one. A page
/// offers a bounded list of years, so the surface must handle a period outside it by SAYING SO — see the
/// builder, which marks the report empty and names the years on offer rather than showing another year's
/// figures under the chosen year's heading.</para>
/// </summary>
public interface IFyScopedGstReportPage : IMasterListExportSource
{
    /// <summary>The financial years this page can project (its own ComboBox's items).</summary>
    System.Collections.ObjectModel.ObservableCollection<GstAdvFyOption> FinancialYears { get; }

    /// <summary>The projected year; setting it re-projects the page.</summary>
    GstAdvFyOption? SelectedYear { get; set; }
}

/// <summary>A selectable imported GSTR-2B snapshot on the 2B-reconciliation / ITC-gate report screens (its return
/// period + a "GSTR-2B · 2024-07 · 27AAPFU0939F1ZV · imported 12-Aug" label). The reconciler + ITC-gate are built over
/// the chosen snapshot's return-period window.
/// <para>🔴 <b>The recipient GSTIN is part of the label, and that is not decoration.</b> A GSTR-2B is made available
/// to ONE registered person (CGST Rules, rule 60(7)), so on a book holding more than one registration the operator
/// has to be able to tell WHICH registration a listed statement belongs to before choosing it — the period and the
/// import date alone cannot distinguish two registrations' statements for the same month, which is exactly the
/// position an operator was left in. Single-registration books simply gain their own GSTIN in the label.</para></summary>
public sealed class Gstr2bSnapshotOption
{
    public required Gstr2bSnapshot Snapshot { get; init; }

    public string Label =>
        $"{(Snapshot.StatementType == GstStatementType.Gstr2b ? "GSTR-2B" : "GSTR-2A")}  ·  {Snapshot.ReturnPeriod}" +
        $"  ·  {Snapshot.RecipientGstin}  ·  imported {ApexDate.Format(Snapshot.ImportedAt)}";

    public override string ToString() => Label;
}
