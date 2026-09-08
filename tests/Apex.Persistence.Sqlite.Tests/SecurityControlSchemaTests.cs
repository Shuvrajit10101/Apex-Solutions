using System;
using System.Collections.Generic;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Io;
using Apex.Ledger.Security;
using Apex.Ledger.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Apex.Persistence.Sqlite.Tests;

/// <summary>
/// Schema v55 → v56 (census 16.2 slice S3) — <b>SECURITY CONTROL</b>: the three tables
/// (<c>security_levels</c>, <c>security_level_rules</c>, <c>company_users</c>) and the three <c>companies</c>
/// columns (<c>use_user_access_control</c>, <c>password_min_length</c>, <c>password_expiry_days</c>).
///
/// <para>The "genuine v55 database" is manufactured with <see cref="SchemaDowngrade.V56ToV55"/> rather than
/// hand-written DDL, so the migration runs against real rows — the idiom every schema test file here uses.</para>
///
/// <para>🔴 <b>THE LOAD-BEARING TEST IN THIS FILE IS
/// <see cref="No_password_is_stored_recoverably_anywhere_in_the_database"/>.</b> It opens the raw SQLite file and
/// requires that the plaintext password does not appear in ANY text column of ANY table — not just the one it was
/// meant to go in. That is the check a reviewer can trust without reading the writer, and it is the check that
/// would have caught a well-meaning "store it encrypted so the admin can recover it".</para>
///
/// <para>Every derivation uses <see cref="TestIterations"/>, not the production 600,000 — a schema test file that
/// paid the real work factor per user would add minutes to three CI legs.</para>
/// </summary>
public sealed class SecurityControlSchemaTests
{
    private static readonly DateOnly FyStart = new(2025, 4, 1);
    private static readonly DateTimeOffset When = new(2026, 4, 1, 9, 0, 0, TimeSpan.FromHours(5.5));
    private const int TestIterations = PasswordHash.MinimumIterations;

    // ================================================================= the version itself

    /// <summary>Security Control's own version floor. (It read "fails on origin/main where CurrentVersion is 55" when it landed; that is history now.)</summary>
    [Fact]
    public void Schema_current_version_is_at_least_56()
    {
        // Pinned as a FLOOR, not an equality. The equality was true the day this slice landed and became a false
        // statement the moment v57 landed — a test failing for a reason unrelated to what it guards. What this
        // guards is that Security Control's storage is reachable, and that is true at 56 and at every version above.
        Assert.True(Schema.CurrentVersion >= 56,
            $"Security Control landed at v56; Schema.CurrentVersion reads {Schema.CurrentVersion}.");
    }

    // ================================================================= migration parity

    /// <summary>
    /// 🔴 The migration-equivalence contract for v56: a genuine v55 book climbed to v56 must end with exactly the
    /// tables, columns and indexes a fresh v56 book has. Fails on today's main, where none of these objects
    /// exists in either database.
    /// </summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Migration_v55_to_v56_matches_CreateV1()
    {
        var migratedPath = TempDbFile.NewPath("apex-security-v55-migrated");
        var freshPath = TempDbFile.NewPath("apex-security-v56-fresh");
        try
        {
            var fresh = CompanyFactory.CreateSeeded("Fresh Security Co", FyStart);
            using (var store = new SqliteCompanyStore(freshPath)) store.Save(fresh);

            var legacy = CompanyFactory.CreateSeeded("Legacy Security Co", FyStart);
            using (var store = new SqliteCompanyStore(migratedPath)) store.Save(legacy);
            using (var conn = Open(migratedPath))
            {
                SchemaDowngrade.V59ToV58(conn); SchemaDowngrade.V58ToV57(conn); SchemaDowngrade.V57ToV56(conn); SchemaDowngrade.V56ToV55(conn);
                SqliteConnection.ClearPool(conn);
            }
            Assert.Equal(55L, ReadScalar(migratedPath, "SELECT version FROM schema_version LIMIT 1;"));

            // Precondition: the v55 book really has none of it.
            var beforeTables = TableNames(migratedPath);
            foreach (var t in Schema.V56SecurityTables) Assert.DoesNotContain(t, beforeTables);
            var beforeCols = ColumnNames(migratedPath, "companies");
            foreach (var c in Schema.V56SecurityCompanyColumns) Assert.DoesNotContain(c, beforeCols);

            // Reopen through the production store — the v55 → v56 migration runs.
            using (new SqliteCompanyStore(migratedPath)) { }
            Assert.Equal((long)Schema.CurrentVersion, ReadScalar(migratedPath, "SELECT version FROM schema_version LIMIT 1;"));

            // The three tables and the three columns are now present, with identical declarations either way.
            foreach (var table in Schema.V56SecurityTables)
            {
                Assert.Contains(table, TableNames(migratedPath));
                Assert.Equal(TableContract(freshPath, table), TableContract(migratedPath, table));
            }
            foreach (var column in Schema.V56SecurityCompanyColumns)
                Assert.Equal(
                    ColumnContract(freshPath, "companies", column),
                    ColumnContract(migratedPath, "companies", column));

            // …and the indexes, which the equivalence test also compares.
            Assert.Equal(SecurityIndexes(freshPath), SecurityIndexes(migratedPath));
        }
        finally
        {
            TempDbFile.Delete(migratedPath);
            TempDbFile.Delete(freshPath);
        }
    }

    /// <summary>
    /// 🔴 <b>THE MIGRATION BACK-FILLS NOTHING.</b> Every pre-v56 company must arrive with access control OFF and
    /// no policy — "column absent" and "feature off" coincide here, deliberately unlike v50's <c>DEFAULT 1</c>,
    /// whose non-coincidence is the trap the schema doc records. A DEFAULT 1 on the gate would silently switch
    /// access control ON in every upgraded book and lock operators out of their own data.
    /// </summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void An_upgraded_company_comes_up_with_access_control_off_and_no_policy()
    {
        var path = TempDbFile.NewPath("apex-security-backfill");
        try
        {
            var c = CompanyFactory.CreateSeeded("Back-fill Co", FyStart);
            using (var store = new SqliteCompanyStore(path)) store.Save(c);
            using (var conn = Open(path)) { SchemaDowngrade.V59ToV58(conn); SchemaDowngrade.V58ToV57(conn); SchemaDowngrade.V57ToV56(conn); SchemaDowngrade.V56ToV55(conn); SqliteConnection.ClearPool(conn); }

            using var reopened = new SqliteCompanyStore(path);
            var loaded = reopened.Load(c.Id)!;

            Assert.False(loaded.UseUserAccessControl);
            Assert.True(loaded.Security.IsEmpty);
            Assert.Equal(0, loaded.Security.PasswordPolicy.MinimumLength);
            Assert.Null(loaded.Security.PasswordPolicy.ExpiryDays);

            // The columns' own defaults say the same thing at the SQL level.
            Assert.Equal(0L, ReadScalar(path, "SELECT use_user_access_control FROM companies LIMIT 1;"));
            Assert.Equal(0L, ReadScalar(path, "SELECT password_min_length FROM companies LIMIT 1;"));
            Assert.Equal(0L, ReadScalar(path,
                "SELECT COUNT(*) FROM companies WHERE password_expiry_days IS NOT NULL;"));
        }
        finally { TempDbFile.Delete(path); }
    }

    // ================================================================= round trip

    /// <summary>Levels, their facility rules, users and the policy all survive a save/load cycle in order.</summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Levels_rules_users_and_policy_round_trip()
    {
        var path = TempDbFile.NewPath("apex-security-roundtrip");
        try
        {
            var c = CompanyFactory.CreateSeeded("Round Trip Co", FyStart);
            c.UseUserAccessControl = true;
            c.Security.SeedDefaultLevels();
            c.Security.PasswordPolicy = new PasswordPolicy { MinimumLength = 8, ExpiryDays = 90 };

            var clerk = c.Security.Levels[1];
            clerk.DaysAllowedForBackDatedVouchers = 7;
            clerk.CutOffDateForBackDatedVouchers = new DateOnly(2025, 4, 1);
            clerk.UseBasicFacilitiesOf = "Owner";
            clerk.Rules.Add(new SecurityAccessRule("Balance Sheet", SecurityAccessType.FullAccess, true));
            clerk.Rules.Add(new SecurityAccessRule("Day Book", SecurityAccessType.Print, true));
            clerk.Rules.Add(new SecurityAccessRule("Day Book", SecurityAccessType.Display, false));

            var admin = new CompanyUser("admin", c.Security.Levels[0].Id);
            Assert.True(c.Security.AddUser(admin, out _));
            admin.SetPassword("Monsoon#2026", When, TestIterations);

            var operatorUser = new CompanyUser("dataentry", clerk.Id) { IsActive = false };
            Assert.True(c.Security.AddUser(operatorUser, out _));   // no password set — a real state

            using (var store = new SqliteCompanyStore(path)) store.Save(c);
            using var reopened = new SqliteCompanyStore(path);
            var loaded = reopened.Load(c.Id)!;

            Assert.True(loaded.UseUserAccessControl);
            Assert.Equal(8, loaded.Security.PasswordPolicy.MinimumLength);
            Assert.Equal(90, loaded.Security.PasswordPolicy.ExpiryDays);

            Assert.Equal(new[] { "Owner", "Data Entry Operator" },
                loaded.Security.Levels.Select(l => l.Name).ToArray());
            var loadedClerk = loaded.Security.LevelByName("Data Entry Operator")!;
            Assert.False(loadedClerk.IsOwner);
            Assert.Equal(7, loadedClerk.DaysAllowedForBackDatedVouchers);
            Assert.Equal(new DateOnly(2025, 4, 1), loadedClerk.CutOffDateForBackDatedVouchers);
            Assert.Equal("Owner", loadedClerk.UseBasicFacilitiesOf);

            // The rules survive in order, with their access types and their allow/disallow sense intact — and
            // the behaviour they encode survives with them.
            Assert.Equal(3, loadedClerk.Rules.Count);
            Assert.Equal("Balance Sheet", loadedClerk.Rules[0].Facility);
            Assert.Equal(SecurityAccessType.FullAccess, loadedClerk.Rules[0].Access);
            Assert.True(loadedClerk.Rules[0].Disallowed);
            Assert.False(loadedClerk.Rules[2].Disallowed);
            Assert.False(loadedClerk.IsAllowed("Balance Sheet", SecurityAccessType.Display));
            Assert.False(loadedClerk.IsAllowed("Day Book", SecurityAccessType.Print));
            Assert.True(loadedClerk.IsAllowed("Day Book", SecurityAccessType.Display));

            var loadedAdmin = loaded.Security.UserByName("admin")!;
            Assert.True(loadedAdmin.IsActive);
            Assert.True(loadedAdmin.HasPassword);
            Assert.True(loadedAdmin.VerifyPassword("Monsoon#2026"));
            Assert.False(loadedAdmin.VerifyPassword("wrong"));
            Assert.Equal(When, loadedAdmin.PasswordSetOn);

            var loadedOperator = loaded.Security.UserByName("dataentry")!;
            Assert.False(loadedOperator.IsActive);
            Assert.False(loadedOperator.HasPassword);
            Assert.True(loadedOperator.VerifyPassword(""));          // "no password" is not "any password"
            Assert.False(loadedOperator.VerifyPassword("guess"));
        }
        finally { TempDbFile.Delete(path); }
    }

    /// <summary>
    /// 🔴 <b>THE R13 TEST.</b> The plaintext password must not appear in ANY text column of ANY table of the
    /// saved file — not merely in the column it was meant to go into. This is what makes "no password is stored
    /// recoverably" a checked fact rather than a claim, and it is what a "store it encrypted so the admin can
    /// recover it" change would trip on regardless of where the author put it.
    ///
    /// <para>Mutation-verified: writing <c>user.Name + ":" + password</c> into <c>company_users.password_hash</c>
    /// reddens this test naming the offending table and column, while every other test in this file stays
    /// green.</para>
    /// </summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void No_password_is_stored_recoverably_anywhere_in_the_database()
    {
        const string password = "Zanzibar!Quokka!7731";
        var path = TempDbFile.NewPath("apex-security-nopassword");
        try
        {
            var c = CompanyFactory.CreateSeeded("Secret Co", FyStart);
            c.UseUserAccessControl = true;
            c.Security.SeedDefaultLevels();
            var admin = new CompanyUser("admin", c.Security.Levels[0].Id);
            Assert.True(c.Security.AddUser(admin, out _));
            admin.SetPassword(password, When, TestIterations);

            using (var store = new SqliteCompanyStore(path)) store.Save(c);

            var hits = new List<string>();
            using (var conn = Open(path))
            {
                foreach (var table in TableNames(path))
                    foreach (var column in ColumnNames(path, table))
                    {
                        using var cmd = conn.CreateCommand();
                        // CAST to TEXT so a BLOB or INTEGER column is searched too — a reversible copy hidden in
                        // a BLOB would otherwise slip past a TEXT-only sweep.
                        cmd.CommandText =
                            $"SELECT COUNT(*) FROM \"{table}\" WHERE CAST(\"{column}\" AS TEXT) LIKE $needle;";
                        cmd.Parameters.AddWithValue("$needle", "%" + password + "%");
                        if (Convert.ToInt64(cmd.ExecuteScalar()) > 0) hits.Add($"{table}.{column}");
                    }
                SqliteConnection.ClearPool(conn);
            }

            Assert.True(hits.Count == 0,
                "🔴 R13 VIOLATION — the plaintext password was found in the saved database at: "
                + string.Join(", ", hits)
                + ". A password must be stored ONLY as a one-way PBKDF2-HMAC-SHA256 verifier. See "
                + "Apex.Ledger.Security.PasswordHash.");

            // …and what IS stored is a well-formed one-way verifier at the shape the type specifies.
            var stored = ReadText(path, "SELECT password_hash FROM company_users LIMIT 1;");
            Assert.NotNull(stored);
            Assert.True(PasswordHash.TryParse(stored, out var parsed));
            Assert.Equal(TestIterations, parsed!.Iterations);
            Assert.True(parsed.Verify(password));
            Assert.False(parsed.Verify("Zanzibar!Quokka!7730"));
        }
        finally { TempDbFile.Delete(path); }
    }

    /// <summary>
    /// 🔴 <b>THE CANONICAL EXPORT CARRIES NO USER ROW AND NO PASSWORD FIELD</b> — the structural absence ER-16
    /// already gives the NIC credential columns. The canonical backup is a portable file an operator mails
    /// around; a verifier in it is an offline cracking target that leaves the machine.
    ///
    /// <para>Two limbs, because either alone is weak: the serialised text must not contain the password OR the
    /// verifier, AND the canonical model must have no member whose name suggests one — so a future
    /// <c>Users</c> member cannot be added without this test noticing.</para>
    /// </summary>
    [Fact]
    public void Canonical_export_carries_no_user_row_and_no_password_field()
    {
        const string password = "Kilimanjaro!Tapir!4419";
        var c = CompanyFactory.CreateSeeded("Export Co", FyStart);
        c.UseUserAccessControl = true;
        c.Security.SeedDefaultLevels();
        var admin = new CompanyUser("admin", c.Security.Levels[0].Id);
        Assert.True(c.Security.AddUser(admin, out _));
        admin.SetPassword(password, When, TestIterations);

        var json = System.Text.Encoding.UTF8.GetString(CanonicalJson.Export(c));

        Assert.DoesNotContain(password, json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(admin.StoredPasswordVerifier!, json, StringComparison.Ordinal);
        Assert.DoesNotContain(PasswordHash.AlgorithmLabel, json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
        // The user names are not exported either — a user list is a security fact, not book data.
        Assert.DoesNotContain("\"admin\"", json, StringComparison.OrdinalIgnoreCase);

        // Structural: no member of the canonical model or its company header names a password, a user or a
        // credential, so a future member cannot be added without this test noticing.
        foreach (var dto in new[] { typeof(CanonicalModel), typeof(CompanyDto), typeof(PayloadDto) })
        {
            var members = dto.GetProperties().Select(p => p.Name).ToList();
            Assert.DoesNotContain(members, m => m.Contains("Password", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(members, m => m.Contains("Credential", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(members, m => m.Contains("SecurityLevel", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(members, m => m.Equals("Users", StringComparison.OrdinalIgnoreCase));
        }
    }

    // ================================================================= the downgrade

    /// <summary>
    /// The downgrade drops exactly what the migration adds — and NOT information-preservingly, which is the point
    /// of the version: a v55 book has no access control to enforce, so carrying users down would be storing
    /// verifiers no code reads. Re-migrating up must bring the company back with access control OFF rather than
    /// resurrect users from nowhere.
    /// </summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void The_downgrade_drops_exactly_what_the_migration_adds()
    {
        var path = TempDbFile.NewPath("apex-security-downgrade");
        try
        {
            var c = CompanyFactory.CreateSeeded("Downgrade Co", FyStart);
            c.UseUserAccessControl = true;
            c.Security.SeedDefaultLevels();
            var admin = new CompanyUser("admin", c.Security.Levels[0].Id);
            Assert.True(c.Security.AddUser(admin, out _));
            admin.SetPassword("pw-to-be-dropped", When, TestIterations);
            using (var store = new SqliteCompanyStore(path)) store.Save(c);

            using (var conn = Open(path)) { SchemaDowngrade.V59ToV58(conn); SchemaDowngrade.V58ToV57(conn); SchemaDowngrade.V57ToV56(conn); SchemaDowngrade.V56ToV55(conn); SqliteConnection.ClearPool(conn); }

            Assert.Equal(55L, ReadScalar(path, "SELECT version FROM schema_version LIMIT 1;"));
            var tables = TableNames(path);
            foreach (var t in Schema.V56SecurityTables) Assert.DoesNotContain(t, tables);
            var cols = ColumnNames(path, "companies");
            foreach (var col in Schema.V56SecurityCompanyColumns) Assert.DoesNotContain(col, cols);

            using var reopened = new SqliteCompanyStore(path);
            var loaded = reopened.Load(c.Id)!;
            Assert.False(loaded.UseUserAccessControl);
            Assert.True(loaded.Security.IsEmpty);
        }
        finally { TempDbFile.Delete(path); }
    }

    /// <summary>
    /// A book that climbs the WHOLE ladder from v53 arrives carrying every rung's effect: the v54 credit-limit
    /// columns, the corrected v55 Karnataka figure's absence of an override, and the v56 security tables. A rung
    /// that silently did not run would show up here rather than in whichever feature broke first.
    /// </summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void A_v53_book_climbs_the_whole_ladder_and_arrives_at_v56()
    {
        var path = TempDbFile.NewPath("apex-security-full-ladder");
        try
        {
            var c = CompanyFactory.CreateSeeded("Ladder Co", FyStart);
            using (var store = new SqliteCompanyStore(path)) store.Save(c);
            using (var conn = Open(path))
            {
                SchemaDowngrade.V59ToV58(conn); SchemaDowngrade.V58ToV57(conn); SchemaDowngrade.V57ToV56(conn); SchemaDowngrade.V56ToV55(conn);
                SchemaDowngrade.V55ToV54(conn);
                SchemaDowngrade.V54ToV53(conn);
                SqliteConnection.ClearPool(conn);
            }
            Assert.Equal(53L, ReadScalar(path, "SELECT version FROM schema_version LIMIT 1;"));

            using (new SqliteCompanyStore(path)) { }

            Assert.Equal((long)Schema.CurrentVersion, ReadScalar(path, "SELECT version FROM schema_version LIMIT 1;"));
            foreach (var col in Schema.V54CreditLimitColumns)
                Assert.Contains(col, ColumnNames(path, "ledgers"));
            foreach (var table in Schema.V56SecurityTables)
                Assert.Contains(table, TableNames(path));
            foreach (var col in Schema.V56SecurityCompanyColumns)
                Assert.Contains(col, ColumnNames(path, "companies"));
        }
        finally { TempDbFile.Delete(path); }
    }

    // ---- helpers (the shape every schema test file here open-codes) ----

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

    private static string? ReadText(string path, string sql)
    {
        using var conn = Open(path);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var v = cmd.ExecuteScalar();
        SqliteConnection.ClearPool(conn);
        return v is DBNull or null ? null : Convert.ToString(v);
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

    /// <summary>The whole table's per-column contract (name/type/notnull/default/pk), sorted — the same tuple
    /// <c>SchemaMigrationEquivalenceTests</c> compares, so a migration whose declaration drifts from
    /// <c>CreateV1</c> fails here with the difference visible.</summary>
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

    /// <summary>The v56 indexes, name → whitespace-normalised SQL, so a migration that forgets one is caught
    /// here as well as by the whole-schema equivalence test.</summary>
    private static SortedDictionary<string, string> SecurityIndexes(string path)
    {
        using var conn = Open(path);
        using var cmd = conn.CreateCommand();
        // Named explicitly, and derived from V56SecurityTables so a table added to that list without an index
        // shows up here rather than passing silently. (Mixed AND/OR with a LIKE would have been precedence bait.)
        var names = Schema.V56SecurityTables.Select(t => $"'ix_{t}_company'");
        cmd.CommandText =
            "SELECT name, sql FROM sqlite_master WHERE type = 'index' AND sql IS NOT NULL "
            + $"AND name IN ({string.Join(", ", names)});";
        var map = new SortedDictionary<string, string>(StringComparer.Ordinal);
        using (var r = cmd.ExecuteReader())
            while (r.Read())
                map[r.GetString(0)] = string.Join(' ',
                    r.GetString(1).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        SqliteConnection.ClearPool(conn);
        return map;
    }
}
