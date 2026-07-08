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
/// Interest-calculation persistence contract (task §7): a company whose ledgers carry interest-parameter
/// blocks (an Always/simple loan and a PostDue/bill-wise customer, covering Rate / Per / On / Applicability /
/// Calculate-From / Style / Rounding) SAVES and RELOADS with schema_version = 7, preserving every parameter
/// and reproducing the Interest Calculation report; a ledger with NO interest block reloads with none; and a
/// legacy v6 database still opens (auto-migrated to v7) with its data intact and then accepts interest data.
/// </summary>
public sealed class InterestRoundTripTests
{
    private static (Company Company, DateOnly From, DateOnly To) SeedWithInterest()
    {
        var c = CompanyFactory.CreateSeeded("Interest Persist Co", new DateOnly(2024, 1, 1));

        var cash = c.FindLedgerByName("Cash")!;
        cash.OpeningBalance = Money.FromRupees(1000000m);
        cash.OpeningIsDebit = true;

        // A loan (credit balance) with Always/simple interest and Normal rounding.
        var loan = new Domain.Ledger(Guid.NewGuid(), "Bank Loan",
            c.FindGroupByName("Loans (Liability)")!.Id, Money.Zero, openingIsDebit: false)
        {
            Interest = new InterestParameters(
                enabled: true, ratePercent: 18.5m, per: InterestPer.ThreeSixtyFiveDayYear,
                onBalance: InterestOnBalance.CreditOnly, applicability: InterestApplicability.Always,
                calculateFrom: new DateOnly(2024, 1, 1), style: InterestStyle.Simple,
                roundingMethod: InterestRoundingMethod.Normal, roundingDecimals: 0),
        };

        // A customer (debit balance, bill-wise) with PostDue interest, no rounding.
        var customer = new Domain.Ledger(Guid.NewGuid(), "Acme Ltd",
            c.FindGroupByName("Sundry Debtors")!.Id, Money.Zero, openingIsDebit: true)
        {
            MaintainBillByBill = true,
            Interest = new InterestParameters(
                enabled: true, ratePercent: 12m, per: InterestPer.ThirtyDayMonth,
                onBalance: InterestOnBalance.DebitOnly, applicability: InterestApplicability.PostDue),
        };

        // A plain ledger with NO interest block.
        var rent = new Domain.Ledger(Guid.NewGuid(), "Rent",
            c.FindGroupByName("Indirect Expenses")!.Id, Money.Zero, openingIsDebit: true);

        c.AddLedger(loan);
        c.AddLedger(customer);
        c.AddLedger(rent);

        var journal = c.FindVoucherTypeByName("Journal")!;
        var svc = new LedgerService(c);

        // Loan drawn 1,00,000 on Jan 1.
        svc.Post(new Voucher(Guid.NewGuid(), journal.Id, new DateOnly(2024, 1, 1), new[]
        {
            new EntryLine(cash.Id, Money.FromRupees(100000m), DrCr.Debit),
            new EntryLine(loan.Id, Money.FromRupees(100000m), DrCr.Credit),
        }));

        // Customer invoiced 50,000 on Jan 1, due Jan 31 (New-Ref INV1).
        svc.Post(new Voucher(Guid.NewGuid(), journal.Id, new DateOnly(2024, 1, 1), new[]
        {
            new EntryLine(customer.Id, Money.FromRupees(50000m), DrCr.Debit,
                billAllocations: new[]
                {
                    new BillAllocation(BillRefType.NewRef, "INV1", Money.FromRupees(50000m),
                        dueDate: new DateOnly(2024, 1, 31)),
                }),
            new EntryLine(cash.Id, Money.FromRupees(50000m), DrCr.Credit),
        }));

        return (c, new DateOnly(2024, 1, 1), new DateOnly(2024, 3, 1));
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Interest_parameters_survive_save_reload_with_schema_version_7()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"apex-interest-{Guid.NewGuid():N}.db");
        try
        {
            var (original, from, to) = SeedWithInterest();
            var before = InterestCalculation.Build(original, from, to);

            using (var write = new SqliteCompanyStore(dbPath))
            {
                write.Save(original);
                Assert.True(Schema.CurrentVersion >= 7);
            }

            Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));

            Company reloaded;
            using (var read = new SqliteCompanyStore(dbPath))
                reloaded = read.Load(original.Id)!;

            // The loan's interest block survived every parameter.
            var loan = reloaded.FindLedgerByName("Bank Loan")!;
            Assert.NotNull(loan.Interest);
            Assert.True(loan.Interest!.Enabled);
            Assert.Equal(18.5m, loan.Interest.RatePercent);
            Assert.Equal(InterestPer.ThreeSixtyFiveDayYear, loan.Interest.Per);
            Assert.Equal(InterestOnBalance.CreditOnly, loan.Interest.OnBalance);
            Assert.Equal(InterestApplicability.Always, loan.Interest.Applicability);
            Assert.Equal(new DateOnly(2024, 1, 1), loan.Interest.CalculateFrom);
            Assert.Equal(InterestStyle.Simple, loan.Interest.Style);
            Assert.Equal(InterestRoundingMethod.Normal, loan.Interest.RoundingMethod);
            Assert.Equal(0, loan.Interest.RoundingDecimals);

            // The customer's PostDue block survived.
            var customer = reloaded.FindLedgerByName("Acme Ltd")!;
            Assert.NotNull(customer.Interest);
            Assert.Equal(InterestApplicability.PostDue, customer.Interest!.Applicability);
            Assert.Equal(InterestPer.ThirtyDayMonth, customer.Interest.Per);
            Assert.Equal(InterestOnBalance.DebitOnly, customer.Interest.OnBalance);

            // The plain ledger reloaded with no interest block.
            var rent = reloaded.FindLedgerByName("Rent")!;
            Assert.Null(rent.Interest);
            Assert.False(rent.InterestEnabled);

            // The Interest report reproduces line-for-line on the reloaded company.
            var after = InterestCalculation.Build(reloaded, from, to);
            Assert.Equal(before.Lines.Count, after.Lines.Count);
            Assert.Equal(before.TotalInterest, after.TotalInterest);
            for (var i = 0; i < before.Lines.Count; i++)
            {
                Assert.Equal(before.Lines[i].Principal, after.Lines[i].Principal);
                Assert.Equal(before.Lines[i].Days, after.Lines[i].Days);
                Assert.Equal(before.Lines[i].Basis, after.Lines[i].Basis);
                Assert.Equal(before.Lines[i].Interest, after.Lines[i].Interest);
                Assert.Equal(before.Lines[i].BillReference, after.Lines[i].BillReference);
            }
        }
        finally
        {
            TempDbFile.Delete(dbPath);
        }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Legacy_v6_database_opens_and_auto_migrates_to_v7()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"apex-v6legacy-{Guid.NewGuid():N}.db");
        try
        {
            var companyId = Guid.NewGuid();
            var connStr = new SqliteConnectionStringBuilder
            {
                DataSource = dbPath,
                Mode = SqliteOpenMode.ReadWriteCreate,
            }.ToString();

            using (var conn = new SqliteConnection(connStr))
            {
                conn.Open();
                Exec(conn, LegacyV6Ddl);
                Exec(conn, "INSERT INTO schema_version(version) VALUES (6);");
                Exec(conn,
                    """
                    INSERT INTO companies
                      (id, name, mailing_name, address, country, state, pin,
                       financial_year_start, books_begin_from, base_currency_symbol,
                       base_currency_name, decimal_places, decimal_unit_name,
                       primary_cost_category, main_location, profit_and_loss_head_id)
                    VALUES
                      ($id, 'Legacy V6 Co', 'Legacy V6 Co', NULL, 'India', NULL, NULL,
                       '2024-04-01', '2024-04-01', '₹', 'INR', 2, 'Paisa',
                       'Primary Cost Category', 'Main Location', NULL);
                    """,
                    ("$id", companyId.ToString("D")));
                SqliteConnection.ClearPool(conn);
            }

            Assert.Equal(6L, ReadSchemaVersion(dbPath));

            using (var store = new SqliteCompanyStore(dbPath))
            {
                var loaded = store.Load(companyId);
                Assert.NotNull(loaded);
                Assert.Equal("Legacy V6 Co", loaded!.Name);
            }

            Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));
            Assert.True(ColumnExists(dbPath, "ledgers", "interest_enabled"), "interest_enabled column was not added.");
            Assert.True(ColumnExists(dbPath, "ledgers", "interest_rate_millis"), "interest_rate_millis column was not added.");
            Assert.True(ColumnExists(dbPath, "ledgers", "interest_calc_from"), "interest_calc_from column was not added.");
        }
        finally
        {
            TempDbFile.Delete(dbPath);
        }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Migrated_v6_database_then_accepts_and_round_trips_interest_data()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"apex-v6then7-{Guid.NewGuid():N}.db");
        try
        {
            var connStr = new SqliteConnectionStringBuilder
            {
                DataSource = dbPath, Mode = SqliteOpenMode.ReadWriteCreate,
            }.ToString();
            using (var conn = new SqliteConnection(connStr))
            {
                conn.Open();
                Exec(conn, LegacyV6Ddl);
                Exec(conn, "INSERT INTO schema_version(version) VALUES (6);");
                SqliteConnection.ClearPool(conn);
            }

            var (original, from, to) = SeedWithInterest();
            var before = InterestCalculation.Build(original, from, to);

            using (var store = new SqliteCompanyStore(dbPath)) // opens v6 → migrates (chained) to current
            {
                Assert.Equal((long)Schema.CurrentVersion, ReadSchemaVersion(dbPath));
                store.Save(original);
            }

            using (var store = new SqliteCompanyStore(dbPath))
            {
                var reloaded = store.Load(original.Id)!;
                var loan = reloaded.FindLedgerByName("Bank Loan")!;
                Assert.Equal(18.5m, loan.Interest!.RatePercent);
                var after = InterestCalculation.Build(reloaded, from, to);
                Assert.Equal(before.TotalInterest, after.TotalInterest);
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

    private static SqliteConnection Open(string dbPath)
    {
        var conn = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWrite,
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

    /// <summary>The pre-interest v6 DDL (the tables the migration/load touches), kept in the test so it
    /// does not drift when the production <see cref="Schema"/> advances past v6. It includes scenarios and
    /// the <c>applicable_upto</c> voucher column, but NOT the v7 interest columns on <c>ledgers</c>.</summary>
    private const string LegacyV6Ddl = """
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
            enable_cheque_printing INTEGER NOT NULL DEFAULT 0, cheque_bank_name TEXT NULL);
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
