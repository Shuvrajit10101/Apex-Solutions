using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Apex.Persistence.Sqlite.Tests;

/// <summary>
/// WI-4 schema + persistence contract (v44 → v45). The bump is additive: four nullable TEXT columns on
/// <c>ledgers</c> holding the party Mailing Details block. Covers: a fresh DB stamps to v45 with those columns; a
/// real v44 database <b>carrying data</b> migrates forward with every existing row intact and the new columns NULL
/// (ER-13); a mailing block survives a full save → reload; and — the design invariant — there is <b>no</b>
/// <c>mailing_state</c> column, because the party's State is the single <c>party_gst_state</c> value that drives
/// GST place of supply.
///
/// <para>The v44 database is manufactured from a freshly-saved current one via
/// <see cref="SchemaDowngrade.V45ToV44"/>, so the migration is exercised against genuine rows rather than an empty
/// schema — the same technique the inventory round-trip tests use.</para>
/// </summary>
public sealed class PartyMailingSchemaTests
{
    private static readonly DateOnly FyStart = new(2025, 4, 1);

    private static readonly string[] MailingColumns =
        { "mailing_name", "mailing_address", "mailing_country", "mailing_pincode" };

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Fresh_database_stamps_to_v45_with_the_mailing_columns()
    {
        var dbPath = TempDbFile.NewPath("apex-wi4-fresh");
        try
        {
            using (new SqliteCompanyStore(dbPath)) { }

            Assert.Equal(45, Schema.CurrentVersion);
            Assert.Equal(45L, ReadSchemaVersion(dbPath));

            var columns = ColumnNames(dbPath, "ledgers");
            foreach (var col in MailingColumns)
                Assert.Contains(col, columns);
        }
        finally { TempDbFile.Delete(dbPath); }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void There_is_no_mailing_state_column_the_party_State_is_stored_once()
    {
        // The structural half of the single-State ruling, at the storage layer. A second stored State could
        // contradict party_gst_state and silently produce the wrong tax head (CGST+SGST vs IGST).
        var dbPath = TempDbFile.NewPath("apex-wi4-nostate");
        try
        {
            using (new SqliteCompanyStore(dbPath)) { }

            var columns = ColumnNames(dbPath, "ledgers");
            Assert.DoesNotContain("mailing_state", columns);
            Assert.Contains("party_gst_state", columns);   // the one and only party State
        }
        finally { TempDbFile.Delete(dbPath); }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void A_party_mailing_block_survives_a_full_save_and_reload()
    {
        var dbPath = TempDbFile.NewPath("apex-wi4-roundtrip");
        try
        {
            var company = CompanyFactory.CreateSeeded("Mailing Co", FyStart);
            var party = new Apex.Ledger.Domain.Ledger(
                Guid.NewGuid(), "Naresh Traders", company.FindGroupByName("Sundry Debtors")!.Id,
                Money.Zero, openingIsDebit: true);
            party.Mailing = new PartyMailingDetails
            {
                MailingName = "Naresh Traders Pvt Ltd",
                Address = "12 Park Street\nKolkata",
                Country = "India",
                Pincode = "700019",
            };
            party.MailingStateCode = "19";
            company.AddLedger(party);

            // A second party with NO mailing block, to prove the null case reloads as null rather than as an
            // empty-but-present block.
            var plain = new Apex.Ledger.Domain.Ledger(
                Guid.NewGuid(), "Plain Party", company.FindGroupByName("Sundry Debtors")!.Id,
                Money.Zero, openingIsDebit: true);
            company.AddLedger(plain);

            using (var store = new SqliteCompanyStore(dbPath)) store.Save(company);

            using var reopened = new SqliteCompanyStore(dbPath);
            var loaded = reopened.Load(company.Id)!;

            var reloaded = loaded.FindLedgerByName("Naresh Traders")!;
            Assert.NotNull(reloaded.Mailing);
            Assert.Equal("Naresh Traders Pvt Ltd", reloaded.Mailing!.MailingName);
            Assert.Equal("12 Park Street\nKolkata", reloaded.Mailing!.Address);
            Assert.Equal("India", reloaded.Mailing!.Country);
            Assert.Equal("700019", reloaded.Mailing!.Pincode);
            // The single State round-tripped on the GST block and is readable through the mailing accessor.
            Assert.Equal("19", reloaded.MailingStateCode);
            Assert.Equal("19", reloaded.PartyGst!.StateCode);

            Assert.Null(loaded.FindLedgerByName("Plain Party")!.Mailing);
        }
        finally { TempDbFile.Delete(dbPath); }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void A_real_v44_database_with_data_migrates_to_v45_leaving_every_row_intact()
    {
        var dbPath = TempDbFile.NewPath("apex-wi4-v44legacy");
        try
        {
            // 1) Build and save a genuine current-version company with real ledgers.
            var company = CompanyFactory.CreateSeeded("Legacy Co", FyStart);
            var party = new Apex.Ledger.Domain.Ledger(
                Guid.NewGuid(), "Old Party", company.FindGroupByName("Sundry Debtors")!.Id,
                Money.Zero, openingIsDebit: true);
            company.AddLedger(party);
            using (var store = new SqliteCompanyStore(dbPath)) store.Save(company);

            var ledgerCountBefore = CountRows(dbPath, "ledgers");
            Assert.True(ledgerCountBefore > 0);

            // 2) Downgrade it to a genuine v44 shape (drop the v45 columns, stamp version 44).
            using (var conn = Open(dbPath))
            {
                SchemaDowngrade.V45ToV44(conn);
                SqliteConnection.ClearPool(conn);
            }
            Assert.Equal(44L, ReadSchemaVersion(dbPath));
            foreach (var col in MailingColumns)
                Assert.DoesNotContain(col, ColumnNames(dbPath, "ledgers"));

            // 3) Reopen through the production store — the v44 → v45 migration runs.
            using (new SqliteCompanyStore(dbPath)) { }

            Assert.Equal(45L, ReadSchemaVersion(dbPath));
            foreach (var col in MailingColumns)
                Assert.Contains(col, ColumnNames(dbPath, "ledgers"));

            // Every pre-existing row survived, and the new columns read NULL (ER-13 — no backfill, no rewrite).
            Assert.Equal(ledgerCountBefore, CountRows(dbPath, "ledgers"));
            Assert.Equal(ledgerCountBefore,
                CountRows(dbPath, "ledgers", "mailing_name IS NULL AND mailing_address IS NULL " +
                                            "AND mailing_country IS NULL AND mailing_pincode IS NULL"));

            // And the migrated database still loads, with no mailing blocks materialised out of thin air.
            using var reopened = new SqliteCompanyStore(dbPath);
            var loaded = reopened.Load(company.Id)!;
            Assert.All(loaded.Ledgers, l => Assert.Null(l.Mailing));
            Assert.NotNull(loaded.FindLedgerByName("Old Party"));
        }
        finally { TempDbFile.Delete(dbPath); }
    }

    // ---- helpers ----

    private static long ReadSchemaVersion(string dbPath) =>
        ReadScalar(dbPath, "SELECT version FROM schema_version LIMIT 1;");

    private static long CountRows(string dbPath, string table, string? where = null) =>
        ReadScalar(dbPath, $"SELECT COUNT(*) FROM {table}{(where is null ? "" : " WHERE " + where)};");

    private static long ReadScalar(string dbPath, string sql)
    {
        using var conn = Open(dbPath);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var v = Convert.ToInt64(cmd.ExecuteScalar());
        SqliteConnection.ClearPool(conn);
        return v;
    }

    private static IReadOnlyList<string> ColumnNames(string dbPath, string table)
    {
        using var conn = Open(dbPath);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({table});";
        var names = new List<string>();
        using (var r = cmd.ExecuteReader())
            while (r.Read()) names.Add(r.GetString(1));
        SqliteConnection.ClearPool(conn);
        return names;
    }

    private static SqliteConnection Open(string dbPath)
    {
        var conn = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadWrite }.ToString());
        conn.Open();
        return conn;
    }

    private static void Exec(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
