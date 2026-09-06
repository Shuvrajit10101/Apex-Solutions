using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Microsoft.Data.Sqlite;
using Xunit;
using Domain = Apex.Ledger.Domain;

namespace Apex.Persistence.Sqlite.Tests;

/// <summary>
/// Schema v53 → v54 (W-F2; census 10.1) — <b>Credit Limits</b> on a party ledger
/// (<c>credit_limit_paisa</c>, <c>check_credit_days_on_entry</c>, <c>override_credit_limit_post_dated</c>).
///
/// <para>The "genuine v53 database" is manufactured with <see cref="SchemaDowngrade.V54ToV53"/> rather than
/// hand-written DDL, so the migration is exercised against real rows — the idiom every schema test here uses.</para>
///
/// <para>🔴 <b>The load-bearing test in this file is
/// <see cref="Credit_limit_round_trips_including_a_limit_of_ZERO"/>.</b> A limit of zero is a real, blocking value
/// ("this party may take nothing on credit"), so it must survive a round trip as <b>0</b> and not collapse into
/// "no limit". That is the entire reason <c>credit_limit_paisa</c> is the one v54 column declared NULLable with no
/// DEFAULT; had it been <c>NOT NULL DEFAULT 0</c>, this test could not tell the two apart and every existing party
/// in every existing book would have been frozen by the upgrade.</para>
/// </summary>
public sealed class CreditLimitSchemaTests
{
    private static readonly DateOnly FyStart = new(2025, 4, 1);

    // ================================================================= migration parity

    /// <summary>Fails on today's main, where <see cref="Schema.CurrentVersion"/> is 53 and none of these columns
    /// exists in either database.</summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Migration_v53_to_v54_matches_CreateV1()
    {
        var migratedPath = TempDbFile.NewPath("apex-climit-v53-migrated");
        var freshPath = TempDbFile.NewPath("apex-climit-v54-fresh");
        try
        {
            var fresh = CompanyFactory.CreateSeeded("Fresh Limit Co", FyStart);
            using (var store = new SqliteCompanyStore(freshPath)) store.Save(fresh);

            var legacy = CompanyFactory.CreateSeeded("Legacy Limit Co", FyStart);
            AddDebtor(legacy, "Acme Ltd");
            using (var store = new SqliteCompanyStore(migratedPath)) store.Save(legacy);
            using (var conn = Open(migratedPath))
            {
                SchemaDowngrade.V54ToV53(conn);
                SqliteConnection.ClearPool(conn);
            }
            Assert.Equal(53L, ReadScalar(migratedPath, "SELECT version FROM schema_version LIMIT 1;"));

            var before = ColumnNames(migratedPath, "ledgers");
            foreach (var col in Schema.V54CreditLimitColumns) Assert.DoesNotContain(col, before);

            // Reopen through the production store — the v53 → v54 migration runs.
            using (new SqliteCompanyStore(migratedPath)) { }
            Assert.Equal((long)Schema.CurrentVersion,
                ReadScalar(migratedPath, "SELECT version FROM schema_version LIMIT 1;"));

            // Only the columns this migration adds are compared: the downgrade's CREATE … AS SELECT rebuild erases
            // the PRE-EXISTING columns' declared type/notnull. Whole-schema parity across the full v1→current chain
            // is separately guaranteed by SchemaMigrationEquivalenceTests.
            foreach (var col in Schema.V54CreditLimitColumns)
                Assert.Equal(ColumnContract(freshPath, "ledgers", col),
                             ColumnContract(migratedPath, "ledgers", col));
        }
        finally
        {
            TempDbFile.Delete(migratedPath);
            TempDbFile.Delete(freshPath);
        }
    }

    /// <summary>
    /// 🔴 The migration back-fills nothing: every migrated ledger reads <b>no limit</b> (NULL, not zero) and both
    /// flags off — which is exactly what a v53 ledger was (ER-13). A <c>DEFAULT 0</c> on the limit column would
    /// have declared, on upgrade, that every existing party may buy nothing.
    /// </summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void A_migrated_v53_book_reads_no_credit_limit_and_both_flags_off()
    {
        var path = TempDbFile.NewPath("apex-climit-backfill");
        try
        {
            var legacy = CompanyFactory.CreateSeeded("Backfill Limit Co", FyStart);
            AddDebtor(legacy, "Acme Ltd");
            AddDebtor(legacy, "Beta Traders");
            using (var store = new SqliteCompanyStore(path)) store.Save(legacy);
            using (var conn = Open(path)) { SchemaDowngrade.V54ToV53(conn); SqliteConnection.ClearPool(conn); }

            using var reopened = new SqliteCompanyStore(path);
            var loaded = reopened.Load(legacy.Id)!;

            Assert.NotEmpty(loaded.Ledgers);
            Assert.All(loaded.Ledgers, l =>
            {
                Assert.Null(l.CreditLimit);
                Assert.False(l.CheckCreditDaysOnEntry);
                Assert.False(l.OverrideCreditLimitWithPostDated);
            });

            // And the stored cell really is NULL — not 0 dressed up as "unset".
            Assert.Equal(0L, ReadScalar(path, "SELECT COUNT(*) FROM ledgers WHERE credit_limit_paisa IS NOT NULL;"));
        }
        finally { TempDbFile.Delete(path); }
    }

    // ================================================================= round-trip

    /// <summary>
    /// 🔴 <b>The case a <c>NOT NULL DEFAULT 0</c> declaration would have destroyed.</b> Three ledgers: one with no
    /// limit, one with a limit of ZERO, one with an ordinary limit. All three must come back distinguishable, and
    /// the zero must come back as a stored 0 rather than a NULL.
    /// </summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Credit_limit_round_trips_including_a_limit_of_ZERO()
    {
        var path = TempDbFile.NewPath("apex-climit-roundtrip");
        try
        {
            var c = CompanyFactory.CreateSeeded("Limit Co", FyStart);
            var none = AddDebtor(c, "No Limit Ltd");
            var zero = AddDebtor(c, "Zero Limit Ltd");
            var normal = AddDebtor(c, "Normal Limit Ltd");

            zero.CreditLimit = Money.Zero;
            normal.CreditLimit = Money.FromRupees(50000m);
            normal.CheckCreditDaysOnEntry = true;
            normal.OverrideCreditLimitWithPostDated = true;

            using (var store = new SqliteCompanyStore(path)) store.Save(c);

            // The raw cells: NULL for "no limit", 0 for "a limit of zero", 5,000,000 paisa for ₹50,000.
            Assert.Equal(0L, ReadScalar(path,
                $"SELECT COUNT(*) FROM ledgers WHERE id = '{none.Id:D}' AND credit_limit_paisa IS NOT NULL;"));
            Assert.Equal(0L, ReadScalar(path,
                $"SELECT credit_limit_paisa FROM ledgers WHERE id = '{zero.Id:D}';"));
            Assert.Equal(5_000_000L, ReadScalar(path,
                $"SELECT credit_limit_paisa FROM ledgers WHERE id = '{normal.Id:D}';"));

            using var reopened = new SqliteCompanyStore(path);
            var loaded = reopened.Load(c.Id)!;

            Assert.Null(loaded.FindLedger(none.Id)!.CreditLimit);

            var z = loaded.FindLedger(zero.Id)!;
            Assert.NotNull(z.CreditLimit);
            Assert.Equal(Money.Zero, z.CreditLimit!.Value);
            Assert.False(z.CheckCreditDaysOnEntry);
            Assert.False(z.OverrideCreditLimitWithPostDated);

            var n = loaded.FindLedger(normal.Id)!;
            Assert.Equal(Money.FromRupees(50000m), n.CreditLimit!.Value);
            Assert.True(n.CheckCreditDaysOnEntry);
            Assert.True(n.OverrideCreditLimitWithPostDated);
        }
        finally { TempDbFile.Delete(path); }
    }

    /// <summary>A second Save must not duplicate the ledger or lose its limit — the store's delete-all +
    /// re-insert snapshot walks <c>ledgers</c> like every other table.</summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void SecondSave_keeps_one_row_and_keeps_the_limit()
    {
        var path = TempDbFile.NewPath("apex-climit-secondsave");
        try
        {
            var c = CompanyFactory.CreateSeeded("Twice Limit Co", FyStart);
            var debtor = AddDebtor(c, "Acme Ltd");
            debtor.CreditLimit = Money.FromRupees(1234.56m);

            using var store = new SqliteCompanyStore(path);
            store.Save(c);
            store.Save(c);

            Assert.Equal(1L, ReadScalar(path, $"SELECT COUNT(*) FROM ledgers WHERE id = '{debtor.Id:D}';"));
            Assert.Equal(Money.FromRupees(1234.56m), store.Load(c.Id)!.FindLedger(debtor.Id)!.CreditLimit!.Value);
        }
        finally { TempDbFile.Delete(path); }
    }

    // ================================================================= 🔴 the unopenable-company guard

    /// <summary>
    /// 🔴 <b>THE TEST THIS ROW MOST NEEDS, end to end against a real SQLite file.</b> A book is saved with no
    /// limit, the party's limit is then lowered far below what the book already holds — exactly what an operator
    /// does through the Ledger master — the company is saved again, and it is <b>reopened through the production
    /// store</b>, whose <c>Load</c> re-posts every stored voucher through the same validator that carries the
    /// block.
    ///
    /// <para>If the credit-limit check were not gated to ENTRY paths, this Load would throw and the company would
    /// be permanently unopenable, with no route back in: the screen that could raise the limit again lives inside
    /// the company that will not open. That is strictly worse than the over-limit invoice it was meant to prevent.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Lowering_a_limit_below_an_existing_book_still_lets_the_book_LOAD()
    {
        var path = TempDbFile.NewPath("apex-climit-unopenable");
        try
        {
            var c = CompanyFactory.CreateSeeded("Unopenable Co", FyStart);
            var party = AddDebtor(c, "Acme Ltd");
            var sales = new Domain.Ledger(Guid.NewGuid(), "Sales", c.FindGroupByName("Sales Accounts")!.Id,
                Money.Zero, openingIsDebit: false);
            c.AddLedger(sales);
            var salesType = c.FindVoucherTypeByName("Sales")!;
            var svc = new LedgerService(c);

            foreach (var day in new[] { 10, 11, 12 })
                svc.Post(new Voucher(Guid.NewGuid(), salesType.Id, new DateOnly(2025, 4, day), new[]
                {
                    new EntryLine(party.Id, Money.FromRupees(4000m), DrCr.Debit),
                    new EntryLine(sales.Id, Money.FromRupees(4000m), DrCr.Credit),
                }, partyId: party.Id));

            // ₹12,000 is on the book. The operator now sets a limit of ₹100.
            party.CreditLimit = Money.FromRupees(100m);
            using (var store = new SqliteCompanyStore(path)) store.Save(c);

            // The company MUST still open, with every voucher and the lowered limit intact.
            using var reopened = new SqliteCompanyStore(path);
            var loaded = reopened.Load(c.Id)!;

            Assert.Equal(3, loaded.Vouchers.Count);
            Assert.Equal(Money.FromRupees(100m), loaded.FindLedger(party.Id)!.CreditLimit!.Value);

            // …and a NEW entry into the reopened book is still refused, so the guard is scoped, not switched off.
            var reSales = loaded.FindLedgerByName("Sales")!;
            Assert.Throws<InvalidVoucherException>(() => new LedgerService(loaded).Post(
                new Voucher(Guid.NewGuid(), loaded.FindVoucherTypeByName("Sales")!.Id, new DateOnly(2025, 4, 20),
                    new[]
                    {
                        new EntryLine(party.Id, Money.FromRupees(1m), DrCr.Debit),
                        new EntryLine(reSales.Id, Money.FromRupees(1m), DrCr.Credit),
                    }, partyId: party.Id)));
        }
        finally { TempDbFile.Delete(path); }
    }

    // ================================================================= downgrade

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Downgrade_v54_to_v53_drops_the_three_columns_and_keeps_every_ledger_row()
    {
        var path = TempDbFile.NewPath("apex-climit-downgrade");
        try
        {
            var c = CompanyFactory.CreateSeeded("Downgrade Limit Co", FyStart);
            var debtor = AddDebtor(c, "Acme Ltd");
            debtor.CreditLimit = Money.FromRupees(9000m);
            var ledgerCountBefore = c.Ledgers.Count;

            using (var store = new SqliteCompanyStore(path)) store.Save(c);
            Assert.Equal((long)ledgerCountBefore, ReadScalar(path, "SELECT COUNT(*) FROM ledgers;"));

            using (var conn = Open(path)) { SchemaDowngrade.V54ToV53(conn); SqliteConnection.ClearPool(conn); }

            Assert.Equal(53L, ReadScalar(path, "SELECT version FROM schema_version LIMIT 1;"));
            Assert.Equal((long)ledgerCountBefore, ReadScalar(path, "SELECT COUNT(*) FROM ledgers;"));

            var cols = ColumnNames(path, "ledgers");
            foreach (var col in Schema.V54CreditLimitColumns) Assert.DoesNotContain(col, cols);

            // The downgrade is deliberately NOT information-preserving: the limit is gone. Re-migrating up must
            // therefore bring the ledger back with NO limit rather than resurrect a figure from nowhere.
            using var reopened = new SqliteCompanyStore(path);
            Assert.Null(reopened.Load(c.Id)!.FindLedger(debtor.Id)!.CreditLimit);
        }
        finally { TempDbFile.Delete(path); }
    }

    // ---- helpers (the shape every schema test file here open-codes) ----

    private static Domain.Ledger AddDebtor(Company c, string name)
    {
        var l = new Domain.Ledger(Guid.NewGuid(), name, c.FindGroupByName("Sundry Debtors")!.Id,
            Money.Zero, openingIsDebit: true);
        c.AddLedger(l);
        return l;
    }

    private static SqliteConnection Open(string path)
    {
        var conn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString());
        conn.Open();
        return conn;
    }

    private static long ReadScalar(string path, string sql)
    {
        using var conn = Open(path);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var v = cmd.ExecuteScalar();
        SqliteConnection.ClearPool(conn);
        return Convert.ToInt64(v);
    }

    private static List<string> ColumnNames(string path, string table)
    {
        using var conn = Open(path);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({table});";
        var names = new List<string>();
        using (var r = cmd.ExecuteReader())
            while (r.Read()) names.Add(r.GetString(1));
        SqliteConnection.ClearPool(conn);
        return names;
    }

    /// <summary>(type, notnull, default, pk) for one column — the same contract tuple the other schema tests
    /// compare, so a migration that adds a column with a different declaration fails here.</summary>
    private static string ColumnContract(string path, string table, string column)
    {
        using var conn = Open(path);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({table});";
        var contract = "(absent)";
        using (var r = cmd.ExecuteReader())
            while (r.Read())
                if (string.Equals(r.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                    contract = $"{r.GetString(2)}|{r.GetInt32(3)}|{(r.IsDBNull(4) ? "-" : r.GetString(4))}|{r.GetInt32(5)}";
        SqliteConnection.ClearPool(conn);
        return contract;
    }
}
