using Apex.Ledger;
using Apex.Desktop.Services;
using Xunit;

namespace Apex.Desktop.Tests;

/// <summary>
/// Unit tests for the side-qualified money formatters (<see cref="IndianFormat.Signed"/> /
/// <see cref="IndianFormat.SignedAlways"/>) used by the ledger-book opening / running / closing lines.
/// The regression under guard (UI-defect C9): a zero amount must NOT render a bare " Dr"/" Cr" — the
/// Tally-faithful blank-at-zero of <see cref="IndianFormat.Amount(decimal)"/> is kept, but without the
/// dangling side label.
/// </summary>
public sealed class IndianFormatSignedTests
{
    [Fact]
    public void Signed_zero_renders_empty_with_no_dangling_side()
    {
        Assert.Equal(string.Empty, IndianFormat.Signed(0m, DrCr.Debit));
        Assert.Equal(string.Empty, IndianFormat.Signed(0m, DrCr.Credit));
    }

    [Fact]
    public void Signed_nonzero_appends_the_side()
    {
        Assert.Equal("1,05,000.00 Dr", IndianFormat.Signed(105000m, DrCr.Debit));
        Assert.Equal("2,500.50 Cr", IndianFormat.Signed(2500.50m, DrCr.Credit));
    }

    [Fact]
    public void SignedAlways_zero_still_renders_with_a_side()
    {
        // The period-opening / closing lines keep a meaningful "0.00 Dr" — always rendered, never blanked.
        Assert.Equal("0.00 Dr", IndianFormat.SignedAlways(0m, DrCr.Debit));
        Assert.Equal("0.00 Cr", IndianFormat.SignedAlways(0m, DrCr.Credit));
    }

    [Fact]
    public void SignedAlways_nonzero_matches_Signed()
    {
        Assert.Equal("1,05,000.00 Dr", IndianFormat.SignedAlways(105000m, DrCr.Debit));
    }
}
