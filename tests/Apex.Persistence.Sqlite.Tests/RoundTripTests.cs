using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Apex.Ledger.Tests;   // FixtureLoader (linked from the core test project)
using Apex.Persistence.Sqlite;
using Xunit;

namespace Apex.Persistence.Sqlite.Tests;

/// <summary>
/// The persistence round-trip contract (task §3; accounting-core §9). A company is seeded via
/// <see cref="CompanyFactory"/>, the Robert fixture is loaded + posted through the engine, the
/// whole aggregate is SAVED to a temp <c>.db</c> file, then RELOADED into a fresh
/// <see cref="SqliteCompanyStore"/> and the engine reports are recomputed on the rehydrated
/// company. Everything must still reconcile to the paisa: Trial Balance 137000 = 137000, Balance
/// Sheet 105000 = 105000, and the master + voucher counts survive the round-trip.
/// </summary>
public sealed class RoundTripTests
{
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Robert_survives_save_reload_and_reports_still_reconcile()
    {
        // A unique temp .db path; deleted in the finally block.
        var dbPath = Path.Combine(
            Path.GetTempPath(), $"apex-roundtrip-{Guid.NewGuid():N}.db");

        try
        {
            // ---- Arrange: seed + load + post the Robert fixture through the real engine. ----
            var loaded = FixtureLoader.Load("robert.json");
            var original = loaded.Company;
            var asOf = loaded.AsOf;

            // Sanity: the seeded masters are present before we persist anything.
            Assert.Equal(28, original.Groups.Count);
            Assert.Equal(24, original.VoucherTypes.Count);
            var originalLedgerCount = original.Ledgers.Count;
            var originalVoucherCount = original.Vouchers.Count;
            var originalLineCount = original.Vouchers.Sum(v => v.Lines.Count);
            Assert.Equal(13, originalVoucherCount);

            // Baseline reports on the in-memory company (the numbers we must reproduce post-reload).
            var tbBefore = TrialBalance.Build(original, asOf);
            var bsBefore = BalanceSheet.Build(original, asOf);
            Assert.Equal(137000m, tbBefore.TotalDebit.Amount);
            Assert.Equal(137000m, tbBefore.TotalCredit.Amount);
            Assert.Equal(105000m, bsBefore.TotalLiabilities.Amount);
            Assert.Equal(105000m, bsBefore.TotalAssets.Amount);

            // ---- Act: SAVE to SQLite, then RELOAD from a fresh store instance. ----
            using (var writeStore = new SqliteCompanyStore(dbPath))
            {
                writeStore.Save(original);
            }

            Assert.True(File.Exists(dbPath), "The .db file was not created on save.");

            Company reloaded;
            using (var readStore = new SqliteCompanyStore(dbPath))
            {
                reloaded = readStore.Load(original.Id)
                    ?? throw new Xunit.Sdk.XunitException("Load returned null for a saved company.");
            }

            // ---- Assert: identity + master counts survive. ----
            Assert.Equal(original.Id, reloaded.Id);
            Assert.Equal(original.Name, reloaded.Name);
            Assert.Equal(28, reloaded.Groups.Count);
            Assert.Equal(24, reloaded.VoucherTypes.Count);
            Assert.Equal(originalLedgerCount, reloaded.Ledgers.Count);
            Assert.NotNull(reloaded.ProfitAndLossHead);

            // Voucher + entry-line counts survive.
            Assert.Equal(originalVoucherCount, reloaded.Vouchers.Count);
            Assert.Equal(originalLineCount, reloaded.Vouchers.Sum(v => v.Lines.Count));

            // The reserved P&L A/c ledger's group still resolves (it points at the P&L head).
            var plLedger = reloaded.FindLedgerByName("Profit & Loss A/c")!;
            Assert.NotNull(reloaded.FindGroup(plLedger.GroupId));

            // ---- Assert: engine reports on the RELOADED company reconcile to the paisa. ----
            var tbAfter = TrialBalance.Build(reloaded, asOf);
            Assert.Equal(137000m, tbAfter.TotalDebit.Amount);
            Assert.Equal(137000m, tbAfter.TotalCredit.Amount);
            Assert.True(tbAfter.Balanced);

            var bsAfter = BalanceSheet.Build(reloaded, asOf);
            Assert.Equal(105000m, bsAfter.TotalLiabilities.Amount);
            Assert.Equal(105000m, bsAfter.TotalAssets.Amount);
            Assert.True(bsAfter.Balanced);
            Assert.Equal(5000m, bsAfter.NetProfitInCapital.Amount);

            // P&L reconciles too (net profit 5000).
            var plAfter = ProfitAndLoss.Build(reloaded, asOf);
            Assert.Equal(37000m, plAfter.TotalIncome.Amount);
            Assert.Equal(32000m, plAfter.TotalExpenses.Amount);
            Assert.Equal(5000m, plAfter.NetProfit.Amount);

            // Per-ledger closings match the fixture's expected block after the round-trip.
            foreach (var prop in loaded.Expected.GetProperty("ledgerClosing").EnumerateObject())
            {
                var expectedAmount = prop.Value.GetProperty("amount").GetDecimal();
                var ledger = reloaded.FindLedgerByName(prop.Name)!;
                var bal = LedgerBalances.Closing(reloaded, ledger, asOf);
                Assert.Equal(expectedAmount, bal.Amount.Amount);
            }
        }
        finally
        {
            if (File.Exists(dbPath))
                File.Delete(dbPath);
        }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Seeded_empty_company_round_trips_master_counts()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"apex-seed-{Guid.NewGuid():N}.db");
        try
        {
            var company = CompanyFactory.CreateSeeded(
                "Round-Trip Co", new DateOnly(2020, 4, 1), new DateOnly(2020, 4, 1));

            using (var writeStore = new SqliteCompanyStore(dbPath))
                writeStore.Save(company);

            using var readStore = new SqliteCompanyStore(dbPath);
            var reloaded = readStore.Load(company.Id)!;

            Assert.Equal(28, reloaded.Groups.Count);
            Assert.Equal(2, reloaded.Ledgers.Count);
            Assert.Equal(24, reloaded.VoucherTypes.Count);
            Assert.Empty(reloaded.Vouchers);
            Assert.NotNull(reloaded.ProfitAndLossHead);

            // Master repository accessors read the same rows.
            Assert.Equal(28, readStore.GetGroups(company.Id).Count);
            Assert.Equal(2, readStore.GetLedgers(company.Id).Count);
            Assert.Equal(24, readStore.GetVoucherTypes(company.Id).Count);
        }
        finally
        {
            if (File.Exists(dbPath))
                File.Delete(dbPath);
        }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Load_returns_null_for_unknown_company()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"apex-empty-{Guid.NewGuid():N}.db");
        try
        {
            using var store = new SqliteCompanyStore(dbPath);
            Assert.Null(store.Load(Guid.NewGuid()));
        }
        finally
        {
            if (File.Exists(dbPath))
                File.Delete(dbPath);
        }
    }
}
