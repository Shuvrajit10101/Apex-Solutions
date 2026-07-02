using System.IO;
using System.Text.Json;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// Tracks the two deterministic study fixtures (Robert, Bright) as committed DATA and
/// documents the intent to drive them through the ledger engine (R8). Each test loads and
/// sanity-checks its fixture (masters + vouchers + expected totals), but the tests are
/// <b>skipped</b> for now: the posting/valuation engine arrives in Phase 1. When the engine
/// lands, drop the <c>Skip</c> and assert Trial Balance / P&amp;L / Balance Sheet to the
/// paisa (NFR-3).
/// </summary>
public class FixtureLoadTests
{
    private const string PhaseSkip = "ledger engine arrives in Phase 1";

    private static readonly string FixturesDir =
        Path.Combine(AppContext.BaseDirectory, "Fixtures");

    [Fact(Skip = PhaseSkip)]
    [Trait("Category", "Fixture")]
    public void Robert_fixture_loads_and_trial_balance_is_balanced()
        => AssertFixtureLoadsAndBalances("robert.json");

    [Fact(Skip = PhaseSkip)]
    [Trait("Category", "Fixture")]
    public void Bright_fixture_loads_and_trial_balance_is_balanced()
        => AssertFixtureLoadsAndBalances("bright.json");

    /// <summary>
    /// Parses a fixture file and verifies its self-consistency: a fixture name, at least one
    /// master ledger, at least one voucher, and a Trial Balance whose declared debit and
    /// credit totals are equal. This runs only once the Skip is removed in Phase 1; today it
    /// merely documents the expected shape of the data.
    /// </summary>
    private static void AssertFixtureLoadsAndBalances(string fileName)
    {
        var path = Path.Combine(FixturesDir, fileName);
        Assert.True(File.Exists(path), $"Fixture not found: {path}");

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;

        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("fixture").GetString()));
        Assert.True(root.GetProperty("masters").GetProperty("ledgers").GetArrayLength() > 0);
        Assert.True(root.GetProperty("vouchers").GetArrayLength() > 0);

        var tb = root.GetProperty("expected").GetProperty("trialBalance");
        var totalDebit = tb.GetProperty("totalDebit").GetDecimal();
        var totalCredit = tb.GetProperty("totalCredit").GetDecimal();
        Assert.Equal(totalDebit, totalCredit);
        Assert.True(tb.GetProperty("balanced").GetBoolean());
    }
}
