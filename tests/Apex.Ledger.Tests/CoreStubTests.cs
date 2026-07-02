using Apex.Ledger;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// Phase-0 smoke tests. One trivial passing test keeps CI green on the (near-)empty
/// suite; it also exercises the minimal core stub (<see cref="Money"/> / <see cref="DrCr"/>)
/// so the intent of the framework-agnostic ledger core is signalled. The real posting
/// engine and its assertions arrive in Phase 1.
/// </summary>
public class CoreStubTests
{
    [Fact]
    public void Money_addition_is_exact()
    {
        var a = Money.FromRupees(100000.00m);
        var b = Money.FromRupees(30000.00m);

        Assert.Equal(Money.FromRupees(130000.00m), a + b);
        Assert.Equal("130000.00", (a + b).ToString());
    }

    [Fact]
    public void DrCr_has_debit_and_credit_sides()
    {
        Assert.Equal(0, (int)DrCr.Debit);
        Assert.Equal(1, (int)DrCr.Credit);
    }
}
