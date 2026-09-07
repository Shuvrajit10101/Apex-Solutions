using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Io;
using Apex.Ledger.Services;
using Microsoft.Data.Sqlite;
using Xunit;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Persistence.Sqlite.Tests;

/// <summary>
/// Schema v56 → v57 (census rows 8.4 / 8.5 / 8.6) — <b>BANKING DOCUMENTS</b>: the three tables
/// (<c>cheque_books</c>, <c>cheque_status_overrides</c>, <c>cheque_layouts</c>) and the six <c>ledgers</c>
/// columns (<c>bank_account_number</c>, <c>bank_branch</c>, <c>bank_ifsc</c>, <c>cheque_adjust_top_tmm</c>,
/// <c>cheque_adjust_left_tmm</c>, <c>print_company_name_on_cheque</c>).
///
/// <para>🔴 <b>THE LOAD-BEARING TEST IN THIS FILE IS
/// <see cref="A_persisted_layout_makes_the_cheque_leaf_actually_renderable"/>.</b> Everything else here is
/// schema hygiene. That one states the reason this version exists at all: before v57, <c>Ledger.ChequeLayout</c>
/// had <b>zero writers anywhere in <c>src/</c></b> — there was no table to load one from — so
/// <c>ChequePdf.Validate</c> refused every render on every loaded company and roughly 625 lines of shipped,
/// tested, deterministic cheque-rendering code could be reached by nobody. The test SAVES a layout, RELOADS
/// through SQLite, and only then asks the renderer, because that reload is the exact step that used to lose it.</para>
///
/// <para>🔴 <b>AND THE UNITS TEST IS NOT DECORATION.</b>
/// <see cref="Every_geometry_column_is_an_INTEGER_of_tenths_of_a_millimetre"/> asserts the declared column type,
/// because a <c>REAL</c> millimetre would let one stored layout render two different byte streams on two
/// machines and break every PDF determinism assertion in this repository — the same reason <c>Paisa</c> exists.</para>
///
/// <para>The "genuine v56 database" is manufactured with <see cref="SchemaDowngrade.V57ToV56"/> rather than
/// hand-written DDL, so the migration runs against real rows — the idiom every schema test file here uses.</para>
/// </summary>
public sealed class BankingDocumentsSchemaTests
{
    private static readonly DateOnly FyStart = new(2025, 4, 1);

    // ================================================================= the version itself

    /// <summary>🔴 Fails on today's <c>origin/main</c>, where <see cref="Schema.CurrentVersion"/> is 56.</summary>
    [Fact]
    public void Schema_current_version_is_at_least_57()
    {
        Assert.True(Schema.CurrentVersion >= 57,
            $"Banking documents landed at v57; Schema.CurrentVersion reads {Schema.CurrentVersion}.");
    }

    // ================================================================= migration parity

    /// <summary>
    /// 🔴 The migration-equivalence contract for v57: a genuine v56 book climbed to v57 must end with exactly the
    /// tables, columns and indexes a fresh v57 book has. Fails on today's main, where none of these objects
    /// exists on either side.
    /// </summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Migration_v56_to_v57_matches_CreateV1()
    {
        var freshPath = TempDbFile.NewPath("apex-bankdocs-fresh");
        var migratedPath = TempDbFile.NewPath("apex-bankdocs-migrated");
        try
        {
            using (var store = new SqliteCompanyStore(freshPath))
                store.Save(CompanyFactory.CreateSeeded("Fresh Banking Co", FyStart));

            using (var store = new SqliteCompanyStore(migratedPath))
                store.Save(CompanyFactory.CreateSeeded("Legacy Banking Co", FyStart));
            using (var conn = Open(migratedPath))
            {
                SchemaDowngrade.V57ToV56(conn);
                SqliteConnection.ClearPool(conn);
            }
            Assert.Equal(56L, ReadScalar(migratedPath, "SELECT version FROM schema_version LIMIT 1;"));

            // Precondition: the v56 book really has none of it, so the assertions below cannot pass vacuously.
            var beforeTables = TableNames(migratedPath);
            foreach (var t in Schema.V57ChequeTables) Assert.DoesNotContain(t, beforeTables);
            var beforeCols = ColumnNames(migratedPath, "ledgers");
            foreach (var c in Schema.V57BankingLedgerColumns) Assert.DoesNotContain(c, beforeCols);

            // Reopen through the production store — the v56 → v57 migration runs.
            using (new SqliteCompanyStore(migratedPath)) { }
            Assert.Equal((long)Schema.CurrentVersion,
                ReadScalar(migratedPath, "SELECT version FROM schema_version LIMIT 1;"));

            foreach (var table in Schema.V57ChequeTables)
            {
                Assert.Contains(table, TableNames(migratedPath));
                Assert.Equal(TableContract(freshPath, table), TableContract(migratedPath, table));
            }
            foreach (var column in Schema.V57BankingLedgerColumns)
                Assert.Equal(
                    ColumnContract(freshPath, "ledgers", column),
                    ColumnContract(migratedPath, "ledgers", column));

            Assert.Equal(BankingIndexes(freshPath), BankingIndexes(migratedPath));
        }
        finally
        {
            TempDbFile.Delete(freshPath);
            TempDbFile.Delete(migratedPath);
        }
    }

    /// <summary>
    /// 🔴 <b>UNITS.</b> Every geometry column of <c>cheque_layouts</c>, and both calibration nudges on
    /// <c>ledgers</c>, must be declared <c>INTEGER</c>. A <c>REAL</c> millimetre is the defect this asserts
    /// against: it renders two different byte streams for one stored layout on two machines, which breaks the
    /// determinism every PDF test in this repository relies on. Same rule <c>Paisa</c> exists for.
    /// </summary>
    [Fact]
    public void Every_geometry_column_is_an_INTEGER_of_tenths_of_a_millimetre()
    {
        var path = TempDbFile.NewPath("apex-bankdocs-units");
        try
        {
            using (var store = new SqliteCompanyStore(path))
                store.Save(CompanyFactory.CreateSeeded("Units Co", FyStart));

            var declared = ColumnTypes(path, "cheque_layouts");
            var geometry = declared.Keys.Where(k => k.EndsWith("_tmm", StringComparison.Ordinal)).ToList();
            // The control: if the suffix convention ever changes, this test must fail loudly rather than
            // silently checking nothing. Twenty measures are declared on the layout.
            Assert.Equal(20, geometry.Count);
            foreach (var column in geometry)
                Assert.Equal("INTEGER", declared[column]);

            var onLedgers = ColumnTypes(path, "ledgers");
            Assert.Equal("INTEGER", onLedgers["cheque_adjust_top_tmm"]);
            Assert.Equal("INTEGER", onLedgers["cheque_adjust_left_tmm"]);

            // And the cheque NUMBERS are TEXT, not INTEGER: a leaf printed 000123 is not the string 123, and an
            // integer column would silently drop the leading zeros the operator is looking at on the paper.
            Assert.Equal("TEXT", ColumnTypes(path, "cheque_books")["from_number"]);
            Assert.Equal("TEXT", ColumnTypes(path, "cheque_books")["to_number"]);
            Assert.Equal("TEXT", ColumnTypes(path, "cheque_status_overrides")["cheque_number"]);
        }
        finally { TempDbFile.Delete(path); }
    }

    // ================================================================= the reason the version exists

    /// <summary>
    /// 🔴 <b>THE TEST THIS WHOLE VERSION EXISTS FOR.</b> Capture cheque dimensions, save, RELOAD FROM SQLITE, and
    /// the renderer accepts the reloaded layout and produces a real PDF on the leaf's own page size.
    ///
    /// <para>On today's main this fails at the reload: there is no <c>cheque_layouts</c> table, so
    /// <c>Ledger.ChequeLayout</c> comes back <c>null</c>, <c>ChequePdf.Validate</c> answers "Cheque dimensions
    /// are not set for this bank", and the cheque leaf cannot be printed by anybody. That is the dead feature
    /// this closes, stated as an assertion rather than as a comment.</para>
    /// </summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void A_persisted_layout_makes_the_cheque_leaf_actually_renderable()
    {
        var path = TempDbFile.NewPath("apex-bankdocs-layout");
        try
        {
            var company = CompanyFactory.CreateSeeded("Cheque Leaf Co", FyStart);
            var bank = AddBank(company, "HDFC Bank");
            bank.BankAccountNumber = "50100123456789";
            bank.BankBranch = "MG Road";
            bank.BankIfsc = "HDFC0000123";
            bank.ChequeAdjustTopTmm = 15;
            bank.ChequeAdjustLeftTmm = 25;
            bank.PrintCompanyNameOnCheque = true;
            bank.ChequeLayout = PrintableLayout();

            using (var store = new SqliteCompanyStore(path)) store.Save(company);

            Company reloaded;
            using (var store = new SqliteCompanyStore(path)) reloaded = store.Load(company.Id)!;
            var reloadedBank = reloaded.FindLedger(bank.Id)!;

            // The six ledgers columns came back verbatim.
            Assert.Equal("50100123456789", reloadedBank.BankAccountNumber);
            Assert.Equal("MG Road", reloadedBank.BankBranch);
            Assert.Equal("HDFC0000123", reloadedBank.BankIfsc);
            Assert.Equal(15, reloadedBank.ChequeAdjustTopTmm);
            Assert.Equal(25, reloadedBank.ChequeAdjustLeftTmm);
            Assert.True(reloadedBank.PrintCompanyNameOnCheque);

            // 🔴 And the LAYOUT came back — the assignment that had no writer before v57.
            var layout = reloadedBank.ChequeLayout;
            Assert.NotNull(layout);
            Assert.Equal(2030, layout!.LeafWidthTmm);
            Assert.Equal(920, layout.LeafHeightTmm);
            Assert.Equal(1350, layout.PayeeWidthTmm);
            Assert.Equal("For Apex Solutions", layout.Salutation1);
            Assert.True(layout.PrintCurrencySymbol);

            // 🔴 The consequence, which is the whole point: the renderer no longer refuses.
            var data = new ChequePrintData
            {
                PayeeName = "Acme Supplies",
                Amount = Money.FromRupees(41250m),
                ChequeDate = new DateOnly(2026, 5, 20),
                InstrumentNumber = "100300",
            };
            Assert.Null(ChequePdf.Validate(data, layout));
            var pdf = ChequePdf.Render(data, layout);
            Assert.NotEmpty(pdf);

            // Deterministic, byte for byte — which is exactly what an INTEGER tenths-mm layout buys and what a
            // double-millimetre one would have cost.
            Assert.Equal(pdf, ChequePdf.Render(data, layout));
        }
        finally { TempDbFile.Delete(path); }
    }

    /// <summary>
    /// A ledger that never captured dimensions persists exactly as it did at v56 (ER-13): NULL bank identity,
    /// zero nudges, and <b>no <c>cheque_layouts</c> row at all</b> — not a row of zeros, which would turn "never
    /// configured" into "configured to place everything at the corner of the leaf".
    /// </summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void An_untouched_bank_ledger_writes_no_layout_row_and_reads_back_unconfigured()
    {
        var path = TempDbFile.NewPath("apex-bankdocs-untouched");
        try
        {
            var company = CompanyFactory.CreateSeeded("Untouched Co", FyStart);
            var bank = AddBank(company, "SBI Current");
            using (var store = new SqliteCompanyStore(path)) store.Save(company);

            Assert.Equal(0L, ReadScalar(path, "SELECT COUNT(*) FROM cheque_layouts;"));
            Assert.Equal(0L, ReadScalar(path, "SELECT COUNT(*) FROM cheque_books;"));

            Company reloaded;
            using (var store = new SqliteCompanyStore(path)) reloaded = store.Load(company.Id)!;
            var reloadedBank = reloaded.FindLedger(bank.Id)!;

            Assert.Null(reloadedBank.ChequeLayout);
            Assert.Null(reloadedBank.BankAccountNumber);
            Assert.Null(reloadedBank.BankBranch);
            Assert.Null(reloadedBank.BankIfsc);
            Assert.Equal(0, reloadedBank.ChequeAdjustTopTmm);
            Assert.Equal(0, reloadedBank.ChequeAdjustLeftTmm);
            Assert.False(reloadedBank.PrintCompanyNameOnCheque);
        }
        finally { TempDbFile.Delete(path); }
    }

    /// <summary>
    /// Cheque books and their operator-set leaf statuses survive a save/reload — the storage census row 8.5
    /// needed, because Available / Blank / Cancelled are facts about paper you hold and no projection can supply
    /// them.
    ///
    /// <para>Also asserts the "absence IS Available" rule: setting a leaf back to Available REMOVES its row,
    /// rather than storing a table full of defaults that would then have to be kept in step with a range it does
    /// not own.</para>
    /// </summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Cheque_books_and_leaf_statuses_round_trip_through_sqlite()
    {
        var path = TempDbFile.NewPath("apex-bankdocs-books");
        try
        {
            var company = CompanyFactory.CreateSeeded("Cheque Book Co", FyStart);
            var bank = AddBank(company, "ICICI Current");
            var book = new ChequeBook(Guid.NewGuid(), bank.Id, "Book 1", "000101", "000110");
            company.AddChequeBook(book);
            company.SetChequeStatus(book.Id, "000103", ChequeStatus.Cancelled);
            company.SetChequeStatus(book.Id, "000104", ChequeStatus.Blank);
            company.SetChequeStatus(book.Id, "000105", ChequeStatus.Available);   // stores nothing

            using (var store = new SqliteCompanyStore(path)) store.Save(company);
            Assert.Equal(2L, ReadScalar(path, "SELECT COUNT(*) FROM cheque_status_overrides;"));

            Company reloaded;
            using (var store = new SqliteCompanyStore(path)) reloaded = store.Load(company.Id)!;

            var reloadedBook = Assert.Single(reloaded.ChequeBooks);
            Assert.Equal(book.Id, reloadedBook.Id);
            Assert.Equal("Book 1", reloadedBook.Name);
            // 🔴 The leading zeros survived. An INTEGER column would have handed back "101", which is not what is
            // printed on the paper the operator is holding.
            Assert.Equal("000101", reloadedBook.FromNumber);
            Assert.Equal("000110", reloadedBook.ToNumber);
            Assert.Equal(10, reloadedBook.Count);

            Assert.Equal(ChequeStatus.Cancelled, reloaded.FindChequeStatus(reloadedBook.Id, "000103")!.Status);
            Assert.Equal(ChequeStatus.Blank, reloaded.FindChequeStatus(reloadedBook.Id, "000104")!.Status);
            Assert.Null(reloaded.FindChequeStatus(reloadedBook.Id, "000105"));
        }
        finally { TempDbFile.Delete(path); }
    }

    /// <summary>
    /// The downgrade drops exactly the v57 objects and stamps the marker back to 56, and re-migrating restores
    /// the shape. The residual is DATA, and it is stated rather than hidden: a v56 database has nowhere to keep a
    /// cheque book, a leaf status or a layout, so those are discarded — the same honest loss <c>V52ToV51</c>
    /// records for the edit log.
    ///
    /// <para>🔴 It also asserts the thing that made <c>V56ToV55</c> use <c>RebuildPreservingShape</c>:
    /// <c>ledgers</c> keeps its PRIMARY KEY across the rebuild. Losing it leaves every child table's foreign key
    /// dangling and the next child insert fails with <c>foreign key mismatch</c>.</para>
    /// </summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void The_downgrade_drops_the_v57_objects_and_keeps_the_ledgers_primary_key()
    {
        var path = TempDbFile.NewPath("apex-bankdocs-downgrade");
        try
        {
            var company = CompanyFactory.CreateSeeded("Downgrade Co", FyStart);
            var bank = AddBank(company, "Axis Current");
            bank.BankAccountNumber = "918020012345678";
            bank.ChequeLayout = PrintableLayout();
            company.AddChequeBook(new ChequeBook(Guid.NewGuid(), bank.Id, "Book 1", "000101", "000110"));
            using (var store = new SqliteCompanyStore(path)) store.Save(company);

            var beforeContract = TableContract(path, "ledgers");

            using (var conn = Open(path))
            {
                SchemaDowngrade.V57ToV56(conn);
                SqliteConnection.ClearPool(conn);
            }

            Assert.Equal(56L, ReadScalar(path, "SELECT version FROM schema_version LIMIT 1;"));
            var tables = TableNames(path);
            foreach (var t in Schema.V57ChequeTables) Assert.DoesNotContain(t, tables);
            var cols = ColumnNames(path, "ledgers");
            foreach (var c in Schema.V57BankingLedgerColumns) Assert.DoesNotContain(c, cols);

            // 🔴 The primary key survived the rebuild — this is the assertion that separates
            // RebuildPreservingShape from DropColumns, and a regression here is silent until a child insert fails.
            Assert.Equal(1L, ReadScalar(path,
                "SELECT COUNT(*) FROM pragma_table_info('ledgers') WHERE name = 'id' AND pk = 1;"));

            // …and re-migrating restores exactly the shape it started with.
            using (new SqliteCompanyStore(path)) { }
            Assert.Equal((long)Schema.CurrentVersion, ReadScalar(path, "SELECT version FROM schema_version LIMIT 1;"));
            Assert.Equal(beforeContract, TableContract(path, "ledgers"));
            Assert.Equal(0L, ReadScalar(path, "SELECT COUNT(*) FROM cheque_books;"));   // the documented data loss
        }
        finally { TempDbFile.Delete(path); }
    }

    /// <summary>
    /// Deleting a bank ledger that still holds a cheque book is REFUSED, with the count in the message.
    ///
    /// <para>🔴 Not a nicety. <c>SqliteCompanyStore</c> runs <c>PRAGMA foreign_keys = ON</c> and Save is
    /// delete-all + full re-insert, so a permitted delete here would leave a <c>cheque_books</c> row pointing at
    /// a ledger that is gone and <b>every later save on the open company would throw</b> — the book on screen
    /// could never be written again by any screen. The layout is deliberately NOT counted: it is a block on the
    /// ledger object and leaves with it.</para>
    /// </summary>
    [Fact]
    public void A_bank_ledger_that_still_holds_a_cheque_book_refuses_to_be_deleted()
    {
        var company = CompanyFactory.CreateSeeded("Guard Co", FyStart);
        var bank = AddBank(company, "Kotak Current");
        bank.ChequeLayout = PrintableLayout();

        // A layout alone does not block: it is part of the ledger's own graph.
        MasterDeletionRules.EnsureLedgerDeletable(company, bank);

        var book = new ChequeBook(Guid.NewGuid(), bank.Id, "Book 1", "000101", "000110");
        company.AddChequeBook(book);

        var ex = Assert.Throws<InvalidOperationException>(
            () => { MasterDeletionRules.EnsureLedgerDeletable(company, bank); });
        Assert.Contains("cheque book", ex.Message, StringComparison.OrdinalIgnoreCase);

        // Removing the book takes its leaf statuses with it and clears the refusal.
        company.SetChequeStatus(book.Id, "000103", ChequeStatus.Cancelled);
        company.RemoveChequeBook(book);
        Assert.Empty(company.ChequeStatusOverrides);
        MasterDeletionRules.EnsureLedgerDeletable(company, bank);
    }

    // ================================================================= helpers

    private static DomainLedger AddBank(Company company, string name)
    {
        var bank = new DomainLedger(
            Guid.NewGuid(), name, company.FindGroupByName("Bank Accounts")!.Id,
            Money.FromRupees(500000m), openingIsDebit: true)
        { EnableChequePrinting = true };
        company.AddLedger(bank);
        return bank;
    }

    /// <summary>A layout that actually places its five elements, so the renderer's "this would place nothing"
    /// refusal is not what the test is measuring.</summary>
    private static ChequeLayout PrintableLayout() => new()
    {
        LeafWidthTmm = 2030,
        LeafHeightTmm = 920,
        DateTopTmm = 120,
        DateLeftTmm = 1400,
        DateCharPitchTmm = 50,
        PayeeTopTmm = 300,
        PayeeLeftTmm = 250,
        WordsLine1TopTmm = 400,
        WordsLine1LeftTmm = 250,
        WordsLine2TopTmm = 470,
        WordsLine2LeftTmm = 250,
        WordsWidthTmm = 1400,
        FiguresTopTmm = 400,
        FiguresLeftTmm = 1600,
        FiguresWidthTmm = 350,
        SignTopTmm = 700,
        SignLeftTmm = 1400,
        SignWidthTmm = 500,
        SignHeightTmm = 150,
        Salutation1 = "For Apex Solutions",
        PrintCurrencySymbol = true,
    };

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
        return Convert.ToInt64(v, CultureInfo.InvariantCulture);
    }

    private static List<string> TableNames(string path)
    {
        using var conn = Open(path);
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name;";
        var names = new List<string>();
        using (var r = cmd.ExecuteReader()) while (r.Read()) names.Add(r.GetString(0));
        SqliteConnection.ClearPool(conn);
        return names;
    }

    private static List<string> ColumnNames(string path, string table)
    {
        using var conn = Open(path);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info(\"{table}\");";
        var names = new List<string>();
        using (var r = cmd.ExecuteReader()) while (r.Read()) names.Add(r.GetString(1));
        SqliteConnection.ClearPool(conn);
        return names;
    }

    /// <summary>Column name → declared type, for the units assertion.</summary>
    private static Dictionary<string, string> ColumnTypes(string path, string table)
    {
        using var conn = Open(path);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info(\"{table}\");";
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        using (var r = cmd.ExecuteReader()) while (r.Read()) map[r.GetString(1)] = r.GetString(2);
        SqliteConnection.ClearPool(conn);
        return map;
    }

    /// <summary>The whole table's per-column contract (name/type/notnull/default/pk), sorted — the same tuple
    /// <c>SchemaMigrationEquivalenceTests</c> compares.</summary>
    private static string TableContract(string path, string table)
    {
        using var conn = Open(path);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info(\"{table}\");";
        var rows = new List<string>();
        using (var r = cmd.ExecuteReader())
            while (r.Read())
                rows.Add(string.Join('|',
                    r.GetString(1), r.GetString(2), r.GetInt64(3),
                    r.IsDBNull(4) ? "<null>" : r.GetString(4), r.GetInt64(5)));
        SqliteConnection.ClearPool(conn);
        rows.Sort(StringComparer.Ordinal);
        return string.Join('\n', rows);
    }

    private static string ColumnContract(string path, string table, string column)
    {
        using var conn = Open(path);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info(\"{table}\");";
        var contract = "(absent)";
        using (var r = cmd.ExecuteReader())
            while (r.Read())
                if (string.Equals(r.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                    contract = string.Join('|',
                        r.GetString(2), r.GetInt64(3),
                        r.IsDBNull(4) ? "<null>" : r.GetString(4), r.GetInt64(5));
        SqliteConnection.ClearPool(conn);
        return contract;
    }

    /// <summary>The three v57 indexes, name → whitespace-normalised SQL. Named explicitly rather than derived,
    /// because two of the three are UNIQUE indexes with a <c>ux_</c> prefix and one is keyed on a column pair —
    /// a derived name would have expected an index that does not exist and passed anyway.</summary>
    private static SortedDictionary<string, string> BankingIndexes(string path)
    {
        using var conn = Open(path);
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT name, sql FROM sqlite_master WHERE type = 'index' AND sql IS NOT NULL "
            + "AND name IN ('ix_cheque_books_ledger', 'ux_cheque_status_book_number', 'ux_cheque_layouts_ledger');";
        var map = new SortedDictionary<string, string>(StringComparer.Ordinal);
        using (var r = cmd.ExecuteReader())
            while (r.Read())
                map[r.GetString(0)] = string.Join(' ',
                    r.GetString(1).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        SqliteConnection.ClearPool(conn);
        // The control: all three must actually be there, or an empty map would compare equal to an empty map.
        Assert.Equal(3, map.Count);
        return map;
    }
}
