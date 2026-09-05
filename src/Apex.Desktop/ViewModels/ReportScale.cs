using System;
using Apex.Ledger;

namespace Apex.Desktop.ViewModels;

/// <summary>
/// The <b>Scale Factor</b> a report's figures are DISPLAYED in (W2-13a, census row 14.5 — the Ctrl+B
/// "Basis of Values" option).
///
/// <para><b>Fidelity (RULING 14 — help.tallysolutions.com).</b> The vendor reaches this through
/// <i>"Press Ctrl+B (Basis of Value) &gt; Scale Factor and select the required option"</i>, and names
/// Hundreds, Thousands, Lakhs, Millions and Crores between its Stock Summary, Cash Flow, Funds Flow and
/// Batch Summary pages, with <i>Default</i> as the unscaled state. Those six are the whole enum.</para>
///
/// <para><b>🔴 DOCUMENTED DIVERGENCE, LABELLED AS OURS (ruling 9): the vendor also offers a "#New Number"
/// free-entry factor.</b> It is not built. A user-typed divisor is a second, unbounded input on a financial
/// statement and it is carved out deliberately rather than guessed at; row 14.5 is not closed on this
/// slice and the carve-out is recorded, not hidden.</para>
/// </summary>
public enum ReportScale
{
    /// <summary>Rupees, unscaled — the pre-slice behaviour and the default on every report.</summary>
    Default,
    Hundreds,
    Thousands,
    Lakhs,
    Millions,
    Crores,
}

/// <summary>The divisor and the label for each <see cref="ReportScale"/>, and the display divide itself.</summary>
public static class ReportScales
{
    /// <summary>The vendor-named factors, in the order the Ctrl+B panel offers them.</summary>
    public static readonly ReportScale[] All =
    {
        ReportScale.Default, ReportScale.Hundreds, ReportScale.Thousands,
        ReportScale.Lakhs, ReportScale.Millions, ReportScale.Crores,
    };

    /// <summary>The divisor a figure is divided by for display. <see cref="ReportScale.Default"/> is 1.</summary>
    public static decimal Divisor(ReportScale scale) => scale switch
    {
        ReportScale.Hundreds => 100m,
        ReportScale.Thousands => 1_000m,
        ReportScale.Lakhs => 100_000m,
        ReportScale.Millions => 1_000_000m,
        ReportScale.Crores => 10_000_000m,
        _ => 1m,
    };

    /// <summary>The name shown in the panel and in the report's own header clause.</summary>
    public static string Label(ReportScale scale) => scale switch
    {
        ReportScale.Default => "Default (Rupees)",
        _ => scale.ToString(),
    };

    /// <summary>
    /// The DISPLAY value of <paramref name="money"/> at <paramref name="scale"/>. Exact decimal division —
    /// no rounding happens here, so the two-decimal grid formatter is the only place a figure is rounded and
    /// the scale can never introduce a second rounding step of its own.
    ///
    /// <para><b>This is display only.</b> Nothing in the engine sees it: the projections are built from the
    /// unscaled books, percentages and sort magnitudes are computed over the unscaled set, and the divide is
    /// applied on the way into a <see cref="ReportRow"/> cell. That is what keeps a percentage share identical
    /// at every scale.</para>
    /// </summary>
    public static Money Apply(ReportScale scale, Money money)
        => scale == ReportScale.Default ? money : new Money(money.Amount / Divisor(scale));
}

/// <summary>One Scale-Factor option on the Ctrl+B panel (the enum member + its display label).</summary>
public sealed class ReportScaleOption
{
    public ReportScale Scale { get; }
    public string Display { get; }

    public ReportScaleOption(ReportScale scale)
    {
        Scale = scale;
        Display = ReportScales.Label(scale);
    }
}
