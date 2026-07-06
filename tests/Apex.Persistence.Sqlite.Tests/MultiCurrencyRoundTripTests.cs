using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Apex.Persistence.Sqlite;
using Microsoft.Data.Sqlite;
using Xunit;
using Domain = Apex.Ledger.Domain;

namespace Apex.Persistence.Sqlite.Tests;

/// <summary>
/// Multi-currency persistence contract (task §6/§7; catalog §2/§20): a company with a base ₹/INR currency
/// (seeded), a foreign USD currency, dated Rates of Exchange, a foreign-currency ledger, and a
/// forex-tagged entry line SAVES and RELOADS with schema_version = 8 — preserving every currency, rate,
/// the ledger's currency, and the line's forex amount/rate exactly — and reproducing the period-end
/// forex revaluation. A ledger with no currency reloads as base; a legacy v7 database still opens
/// (auto-migrated, chained, to the current version) with its data intact and then accepts forex data.
/// </summary>
public sealed class MultiCurrencyRoundTripTests
{
    // A US$1,000 export receivable booked at ₹80 on a foreign-currency debtor, plus a USD rate at ₹83.
    private static (Company Company, Guid UsdId, DateOnly AsOf) SeedWithForex()
    {
        var c = CompanyFactory.CreateSeeded("Forex Persist Co", new DateOnly(2024, 1, 1));

        var usd = new Currency(Guid.NewGuid(), "$", "USD", decimalPlaces: 2);
        c.AddCurrency(usd);
        c.AddExchangeRate(new ExchangeRate(Guid.NewGuid(), usd.Id, new DateOnly(2024, 1, 1), 80m,
            sellingRate: 80.5m, buyingRate: 79.5m));
        c.AddExchangeRate(new ExchangeRate(Guid.NewGuid(), usd.Id, new DateOnly(2024, 3, 31), 83m));

        var sales = new Domain.Ledger(Guid.NewGuid(), "Export Sales",
            c.FindGroupByName("Sales Accounts")!.Id, Money.Zero, openingIsDebit: false);
        var debtor = new Domain.Ledger(Guid.NewGuid(), "US Customer",
            c.FindGroupByName("Sundry Debtors")!.Id, Money.Zero, openingIsDebit: true, currencyId: usd.Id);
        c.AddLedger(sales);
        c.AddLedger(debtor);

        var svc = new LedgerService(c);
        var salesVt = c.FindVoucherTypeByName("Sales")!;
        svc.Post(new Voucher(Guid.NewGuid(), salesVt.Id, new DateOnly(2024, 1, 10), new[]
        {
            new EntryLine(debtor.Id, Money.FromRupees(80000m), DrCr.Debit,
                forex: new ForexInfo(usd.Id, Money.FromRupees(1000m), 80m)),
            new EntryLine(sales.Id, Money.FromRupees(80000m), DrCr.Credit),
        }));

        return (c, usd.Id, new DateOnly(2024, 3, 31));
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Currencies_rates_and_forex_lines_survive_save_reload_with_schema_version_8()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"apex-forex-{Guid.NewGuid():N}.db");
        try
        {
            var (original, usdId, asOf) = SeedWithForex();
            var before = ForexGainLoss.Revalue(original, asOf);

            using (var write = new SqliteCompanyStore(dbPath))
            {
                write.Save(original);
                Assert.True(Schema.CurrentVersion >= 8);
                // Re-save (delete-then-insert upsert) must not trip a currency/rate foreign key: the
                // delete order tears down entry-line forex + ledgers before currencies + exchange_rates.
                write.Save(original);
            }

            Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));

            Company reloaded;
            using (var read = new SqliteCompanyStore(dbPath))
                reloaded = read.Load(original.Id)!;

            // Base currency ₹/INR survived and is still flagged base.
            var baseCur = reloaded.BaseCurrency;
            Assert.NotNull(baseCur);
            Assert.Equal("INR", baseCur!.FormalName);
            Assert.Equal("₹", baseCur.Symbol);

            // The USD currency survived.
            var usd = reloaded.FindCurrencyByName("USD")!;
            Assert.Equal(usdId, usd.Id);
            Assert.False(usd.IsBaseCurrency);
            Assert.Equal(2, usd.DecimalPlaces);

            // Both dated rates survived, with directional rates and the fallback intact.
            var jan = reloaded.RateInForce(usdId, new DateOnly(2024, 1, 1))!;
            Assert.Equal(80m, jan.StandardRate);
            Assert.Equal(80.5m, jan.SellingRate);
            Assert.Equal(79.5m, jan.BuyingRate);
            Assert.Equal(83m, reloaded.RateInForce(usdId, new DateOnly(2024, 3, 31))!.StandardRate);

            // The debtor reloaded as a foreign-currency ledger.
            var debtor = reloaded.FindLedgerByName("US Customer")!;
            Assert.Equal(usdId, debtor.CurrencyId);
            Assert.True(debtor.IsForeignCurrency);

            // A base-currency ledger reloaded with no currency.
            var sales = reloaded.FindLedgerByName("Export Sales")!;
            Assert.Null(sales.CurrencyId);
            Assert.False(sales.IsForeignCurrency);

            // The forex line survived (currency + forex amount + rate), and its base = forex × rate.
            var forexLine = reloaded.Vouchers
                .SelectMany(v => v.Lines)
                .Single(l => l.HasForex);
            Assert.Equal(usdId, forexLine.Forex!.CurrencyId);
            Assert.Equal(Money.FromRupees(1000m), forexLine.Forex.ForexAmount);
            Assert.Equal(80m, forexLine.Forex.Rate);
            Assert.Equal(forexLine.Amount, forexLine.Forex.BaseValue);

            // The forex revaluation reproduces on the reloaded company.
            var after = ForexGainLoss.Revalue(reloaded, asOf);
            Assert.Equal(before.Lines.Count, after.Lines.Count);
            Assert.Equal(before.NetGainLoss, after.NetGainLoss);
            Assert.Equal(before.Lines[0].GainLoss, after.Lines[0].GainLoss);
            Assert.Equal(before.Lines[0].RevaluedBase, after.Lines[0].RevaluedBase);
        }
        finally
        {
            TempDbFile.Delete(dbPath);
        }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void A_forex_line_at_a_non_round_rate_saves_reloads_and_revalues_without_throwing()
    {
        // Regression: a NON-ROUND rate makes forex × rate carry a sub-paisa tail (US$100 × 83.33335 =
        // ₹8 333.335). The base MUST be paisa-rounded (₹8 333.34) so Paisa.FromMoney does not throw on Save.
        // This test posts, SAVES to a temp .db, RELOADS, and runs a (non-round) revaluation — all crash-free.
        var dbPath = Path.Combine(Path.GetTempPath(), $"apex-forex-nonround-{Guid.NewGuid():N}.db");
        try
        {
            var c = CompanyFactory.CreateSeeded("Forex NonRound Co", new DateOnly(2024, 1, 1));
            var usd = new Currency(Guid.NewGuid(), "$", "USD", decimalPlaces: 2);
            c.AddCurrency(usd);
            c.AddExchangeRate(new ExchangeRate(Guid.NewGuid(), usd.Id, new DateOnly(2024, 1, 1), 83.33335m));

            var sales = new Domain.Ledger(Guid.NewGuid(), "Export Sales",
                c.FindGroupByName("Sales Accounts")!.Id, Money.Zero, openingIsDebit: false);
            var debtor = new Domain.Ledger(Guid.NewGuid(), "US Customer",
                c.FindGroupByName("Sundry Debtors")!.Id, Money.Zero, openingIsDebit: true, currencyId: usd.Id);
            var forexGl = new Domain.Ledger(Guid.NewGuid(), ForexGainLoss.ForexGainLossLedgerName,
                c.FindGroupByName("Indirect Expenses")!.Id, Money.Zero, openingIsDebit: true);
            c.AddLedger(sales);
            c.AddLedger(debtor);
            c.AddLedger(forexGl);

            var txnRate = 83.33335m;
            var baseBooked = new ForexInfo(usd.Id, Money.FromRupees(100m), txnRate).BaseValue; // paisa-exact 8333.34
            Assert.Equal(Money.FromRupees(8333.34m), baseBooked);

            var svc = new LedgerService(c);
            var salesVt = c.FindVoucherTypeByName("Sales")!;
            svc.Post(new Voucher(Guid.NewGuid(), salesVt.Id, new DateOnly(2024, 1, 10), new[]
            {
                new EntryLine(debtor.Id, baseBooked, DrCr.Debit, forex: new ForexInfo(usd.Id, Money.FromRupees(100m), txnRate)),
                new EntryLine(sales.Id, baseBooked, DrCr.Credit),
            }));

            // (b) SAVE to a temp .db and RELOAD — the pre-fix sub-paisa base would throw here in Paisa.FromMoney.
            using (var write = new SqliteCompanyStore(dbPath))
                write.Save(c);

            Company reloaded;
            using (var read = new SqliteCompanyStore(dbPath))
                reloaded = read.Load(c.Id)!;

            var forexLine = reloaded.Vouchers.SelectMany(v => v.Lines).Single(l => l.HasForex);
            Assert.Equal(baseBooked, forexLine.Amount);                       // paisa-exact base survived
            Assert.Equal(baseBooked, forexLine.Forex!.BaseValue);             // base == paisa-rounded forex × rate
            Assert.Equal(txnRate, forexLine.Forex.Rate);                      // rate preserved verbatim
            Assert.Equal(Money.FromRupees(100m), forexLine.Forex.ForexAmount);

            // (c) A NON-ROUND revaluation on the reloaded company runs and its adjusting journal SAVES again.
            var asOf = new DateOnly(2024, 2, 15);
            var reval = ForexGainLoss.Revalue(reloaded, asOf, new Dictionary<Guid, decimal> { [usd.Id] = 84.44445m });
            var revLine = Assert.Single(reval.Lines);
            Assert.Equal(Money.FromRupees(8444.45m), revLine.RevaluedBase); // 100 × 84.44445 → paisa-exact
            Assert.Equal(111.11m, revLine.GainLoss);                        // 8444.45 − 8333.34

            var journalVt = reloaded.FindVoucherTypeByName("Journal")!;
            var reloadedForexGl = reloaded.FindLedgerByName(ForexGainLoss.ForexGainLossLedgerName)!;
            var adj = ForexGainLoss.BuildAdjustingJournal(reloaded, reval, journalVt.Id, reloadedForexGl.Id)!;
            new LedgerService(reloaded).Post(adj);
            using (var write = new SqliteCompanyStore(dbPath))
                write.Save(reloaded); // the adjusting journal's paisa-exact legs persist without throwing
        }
        finally
        {
            TempDbFile.Delete(dbPath);
        }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Legacy_v7_database_opens_and_auto_migrates_to_v8()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"apex-v7legacy-{Guid.NewGuid():N}.db");
        try
        {
            var companyId = Guid.NewGuid();
            var connStr = new SqliteConnectionStringBuilder
            {
                DataSource = dbPath, Mode = SqliteOpenMode.ReadWriteCreate,
            }.ToString();

            using (var conn = new SqliteConnection(connStr))
            {
                conn.Open();
                Exec(conn, LegacyV7Ddl);
                Exec(conn, "INSERT INTO schema_version(version) VALUES (7);");
                Exec(conn,
                    """
                    INSERT INTO companies
                      (id, name, mailing_name, address, country, state, pin,
                       financial_year_start, books_begin_from, base_currency_symbol,
                       base_currency_name, decimal_places, decimal_unit_name,
                       primary_cost_category, main_location, profit_and_loss_head_id)
                    VALUES
                      ($id, 'Legacy V7 Co', 'Legacy V7 Co', NULL, 'India', NULL, NULL,
                       '2024-04-01', '2024-04-01', '₹', 'INR', 2, 'Paisa',
                       'Primary Cost Category', 'Main Location', NULL);
                    """,
                    ("$id", companyId.ToString("D")));
                SqliteConnection.ClearPool(conn);
            }

            Assert.Equal(7L, ReadSchemaVersion(dbPath));

            using (var store = new SqliteCompanyStore(dbPath))
            {
                var loaded = store.Load(companyId);
                Assert.NotNull(loaded);
                Assert.Equal("Legacy V7 Co", loaded!.Name);
                // A migrated v7 DB has no currency rows until re-saved.
                Assert.Empty(loaded.Currencies);
            }

            Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));
            Assert.True(TableExists(dbPath, "currencies"), "currencies table was not created.");
            Assert.True(TableExists(dbPath, "exchange_rates"), "exchange_rates table was not created.");
            Assert.True(ColumnExists(dbPath, "ledgers", "currency_id"), "ledgers.currency_id was not added.");
            Assert.True(ColumnExists(dbPath, "entry_lines", "forex_amount_micro"), "entry_lines.forex_amount_micro was not added.");
        }
        finally
        {
            TempDbFile.Delete(dbPath);
        }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Migrated_v7_database_then_accepts_and_round_trips_forex_data()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"apex-v7then8-{Guid.NewGuid():N}.db");
        try
        {
            var connStr = new SqliteConnectionStringBuilder
            {
                DataSource = dbPath, Mode = SqliteOpenMode.ReadWriteCreate,
            }.ToString();
            using (var conn = new SqliteConnection(connStr))
            {
                conn.Open();
                Exec(conn, LegacyV7Ddl);
                Exec(conn, "INSERT INTO schema_version(version) VALUES (7);");
                SqliteConnection.ClearPool(conn);
            }

            var (original, usdId, asOf) = SeedWithForex();
            var before = ForexGainLoss.Revalue(original, asOf);

            using (var store = new SqliteCompanyStore(dbPath)) // opens v7 → migrates (chained) to current
            {
                Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));
                store.Save(original);
            }

            using (var store = new SqliteCompanyStore(dbPath))
            {
                var reloaded = store.Load(original.Id)!;
                Assert.Equal(usdId, reloaded.FindCurrencyByName("USD")!.Id);
                var after = ForexGainLoss.Revalue(reloaded, asOf);
                Assert.Equal(before.NetGainLoss, after.NetGainLoss);
                Assert.Equal(before.Lines.Count, after.Lines.Count);
            }
        }
        finally
        {
            TempDbFile.Delete(dbPath);
        }
    }

    // ---- helpers ----

    private static long ReadSchemaVersion(string dbPath)
    {
        using var conn = Open(dbPath);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT version FROM schema_version LIMIT 1;";
        var v = Convert.ToInt64(cmd.ExecuteScalar());
        SqliteConnection.ClearPool(conn);
        return v;
    }

    private static bool ColumnExists(string dbPath, string table, string column)
    {
        using var conn = Open(dbPath);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name=$c;";
        cmd.Parameters.AddWithValue("$c", column);
        var exists = Convert.ToInt64(cmd.ExecuteScalar()) > 0;
        SqliteConnection.ClearPool(conn);
        return exists;
    }

    private static bool TableExists(string dbPath, string table)
    {
        using var conn = Open(dbPath);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$t;";
        cmd.Parameters.AddWithValue("$t", table);
        var exists = Convert.ToInt64(cmd.ExecuteScalar()) > 0;
        SqliteConnection.ClearPool(conn);
        return exists;
    }

    private static SqliteConnection Open(string dbPath)
    {
        var conn = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = dbPath, Mode = SqliteOpenMode.ReadWrite,
        }.ToString());
        conn.Open();
        return conn;
    }

    private static void Exec(SqliteConnection conn, string sql, params (string Name, object Value)[] ps)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v);
        cmd.ExecuteNonQuery();
    }

    /// <summary>The pre-multi-currency v7 DDL (the tables the migration/load touches), kept in the test so
    /// it does not drift when the production <see cref="Schema"/> advances past v7. It includes the v7
    /// interest columns on <c>ledgers</c>, but NOT the v8 currencies/exchange_rates tables, the
    /// <c>currency_id</c> ledger column, or the forex columns on <c>entry_lines</c>.</summary>
    private const string LegacyV7Ddl = """
        CREATE TABLE schema_version (version INTEGER NOT NULL);
        CREATE TABLE companies (
            id TEXT NOT NULL PRIMARY KEY, name TEXT NOT NULL, mailing_name TEXT NOT NULL,
            address TEXT NULL, country TEXT NOT NULL, state TEXT NULL, pin TEXT NULL,
            financial_year_start TEXT NOT NULL, books_begin_from TEXT NOT NULL,
            base_currency_symbol TEXT NOT NULL, base_currency_name TEXT NOT NULL,
            decimal_places INTEGER NOT NULL, decimal_unit_name TEXT NOT NULL,
            primary_cost_category TEXT NOT NULL, main_location TEXT NOT NULL,
            profit_and_loss_head_id TEXT NULL REFERENCES groups(id));
        CREATE TABLE groups (
            id TEXT NOT NULL PRIMARY KEY, company_id TEXT NOT NULL REFERENCES companies(id),
            name TEXT NOT NULL, nature INTEGER NOT NULL, parent_id TEXT NULL REFERENCES groups(id),
            alias TEXT NULL, is_predefined INTEGER NOT NULL, is_pl_head INTEGER NOT NULL DEFAULT 0);
        CREATE TABLE ledgers (
            id TEXT NOT NULL PRIMARY KEY, company_id TEXT NOT NULL REFERENCES companies(id),
            name TEXT NOT NULL, group_id TEXT NOT NULL REFERENCES groups(id),
            opening_balance_paisa INTEGER NOT NULL, opening_is_debit INTEGER NOT NULL,
            alias TEXT NULL, is_predefined INTEGER NOT NULL,
            maintain_bill_by_bill INTEGER NOT NULL DEFAULT 0, default_credit_period INTEGER NULL,
            cost_applicable INTEGER NULL,
            enable_cheque_printing INTEGER NOT NULL DEFAULT 0, cheque_bank_name TEXT NULL,
            interest_enabled INTEGER NULL, interest_rate_millis INTEGER NULL, interest_per INTEGER NULL,
            interest_on_balance INTEGER NULL, interest_applicability INTEGER NULL,
            interest_calc_from TEXT NULL, interest_style INTEGER NULL,
            interest_round_method INTEGER NULL, interest_round_decimals INTEGER NULL);
        CREATE TABLE voucher_types (
            id TEXT NOT NULL PRIMARY KEY, company_id TEXT NOT NULL REFERENCES companies(id),
            name TEXT NOT NULL, base_type INTEGER NOT NULL, default_shortcut TEXT NULL,
            numbering INTEGER NOT NULL, abbreviation TEXT NULL, is_active INTEGER NOT NULL,
            is_predefined INTEGER NOT NULL);
        CREATE TABLE vouchers (
            id TEXT NOT NULL PRIMARY KEY, company_id TEXT NOT NULL REFERENCES companies(id),
            type_id TEXT NOT NULL REFERENCES voucher_types(id), number INTEGER NOT NULL,
            date TEXT NOT NULL, narration TEXT NULL, party_id TEXT NULL REFERENCES ledgers(id),
            cancelled INTEGER NOT NULL, optional INTEGER NOT NULL, post_dated INTEGER NOT NULL,
            applicable_upto TEXT NULL);
        CREATE TABLE entry_lines (
            id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, voucher_id TEXT NOT NULL REFERENCES vouchers(id),
            line_order INTEGER NOT NULL, ledger_id TEXT NOT NULL REFERENCES ledgers(id),
            amount_paisa INTEGER NOT NULL, side INTEGER NOT NULL);
        CREATE TABLE bill_allocations (
            id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, entry_line_id INTEGER NOT NULL REFERENCES entry_lines(id),
            alloc_order INTEGER NOT NULL, ref_type INTEGER NOT NULL, name TEXT NOT NULL,
            amount_paisa INTEGER NOT NULL, due_date TEXT NULL, credit_days INTEGER NULL);
        CREATE TABLE cost_categories (
            id TEXT NOT NULL PRIMARY KEY, company_id TEXT NOT NULL REFERENCES companies(id),
            name TEXT NOT NULL, allocate_revenue INTEGER NOT NULL, allocate_non_revenue INTEGER NOT NULL,
            is_predefined INTEGER NOT NULL);
        CREATE TABLE cost_centres (
            id TEXT NOT NULL PRIMARY KEY, company_id TEXT NOT NULL REFERENCES companies(id),
            name TEXT NOT NULL, category_id TEXT NOT NULL REFERENCES cost_categories(id),
            parent_id TEXT NULL REFERENCES cost_centres(id), alias TEXT NULL);
        CREATE TABLE cost_allocations (
            id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, entry_line_id INTEGER NOT NULL REFERENCES entry_lines(id),
            alloc_order INTEGER NOT NULL, category_id TEXT NOT NULL REFERENCES cost_categories(id),
            centre_id TEXT NOT NULL REFERENCES cost_centres(id), amount_paisa INTEGER NOT NULL);
        CREATE TABLE budgets (
            id TEXT NOT NULL PRIMARY KEY, company_id TEXT NOT NULL REFERENCES companies(id),
            name TEXT NOT NULL, under_id TEXT NULL REFERENCES groups(id),
            period_from TEXT NOT NULL, period_to TEXT NOT NULL);
        CREATE TABLE budget_lines (
            id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, budget_id TEXT NOT NULL REFERENCES budgets(id),
            line_order INTEGER NOT NULL, group_id TEXT NULL REFERENCES groups(id),
            ledger_id TEXT NULL REFERENCES ledgers(id), budget_type INTEGER NOT NULL, amount_paisa INTEGER NOT NULL);
        CREATE TABLE bank_allocations (
            id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, entry_line_id INTEGER NOT NULL REFERENCES entry_lines(id),
            transaction_type INTEGER NOT NULL, instrument_number TEXT NOT NULL,
            instrument_date TEXT NULL, bank_date TEXT NULL);
        CREATE TABLE scenarios (
            id TEXT NOT NULL PRIMARY KEY, company_id TEXT NOT NULL REFERENCES companies(id),
            name TEXT NOT NULL, include_actuals INTEGER NOT NULL);
        CREATE TABLE scenario_voucher_types (
            id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, scenario_id TEXT NOT NULL REFERENCES scenarios(id),
            voucher_type_id TEXT NOT NULL REFERENCES voucher_types(id), is_included INTEGER NOT NULL);
        """;
}
