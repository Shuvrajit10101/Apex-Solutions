using Apex.Ledger;
using Apex.Desktop.Services;

namespace Apex.Desktop.ViewModels;

/// <summary>
/// A single presentation row for the Tally-style report grids. Carries a particulars label,
/// an optional secondary label (group), and up to two amount columns (debit/credit or a single
/// amount). <see cref="IsTotal"/> and <see cref="IsHeader"/> drive bold/underlined styling.
/// </summary>
public sealed class ReportRow
{
    public string Particulars { get; init; } = string.Empty;
    public string Secondary { get; init; } = string.Empty;
    public string Debit { get; init; } = string.Empty;
    public string Credit { get; init; } = string.Empty;
    public string Amount { get; init; } = string.Empty;
    public bool IsTotal { get; init; }
    public bool IsHeader { get; init; }

    public static ReportRow Line(string particulars, Money amount, string secondary = "")
        => new() { Particulars = particulars, Secondary = secondary, Amount = IndianFormat.Amount(amount) };

    public static ReportRow DrCrLine(string particulars, Money debit, Money credit, string secondary = "")
        => new()
        {
            Particulars = particulars,
            Secondary = secondary,
            Debit = IndianFormat.Amount(debit),
            Credit = IndianFormat.Amount(credit),
        };

    public static ReportRow Total(string particulars, Money amount)
        => new() { Particulars = particulars, Amount = IndianFormat.AmountAlways(amount), IsTotal = true };

    public static ReportRow DrCrTotal(string particulars, Money debit, Money credit)
        => new()
        {
            Particulars = particulars,
            Debit = IndianFormat.AmountAlways(debit),
            Credit = IndianFormat.AmountAlways(credit),
            IsTotal = true,
        };
}
