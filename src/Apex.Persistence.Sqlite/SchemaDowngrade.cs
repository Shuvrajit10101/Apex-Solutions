using Microsoft.Data.Sqlite;

namespace Apex.Persistence.Sqlite;

/// <summary>
/// The <b>downgrade</b> counterparts to the <c>MigrateVNToVN+1</c> scripts — the house discipline that every
/// schema bump ships with a way back down. A downgrade is not a production path (the store only ever migrates
/// forward); it exists so a round-trip test can manufacture a <i>genuine</i> older database, <b>carrying real
/// rows</b>, and then drive the real migration over it. Without that, a migration is only ever exercised against
/// an empty schema, which is exactly where the interesting failures are not.
/// </summary>
public static class SchemaDowngrade
{
    /// <summary>
    /// Reverses <see cref="Schema.MigrateV44ToV45"/>: removes the four party Mailing Details columns from
    /// <c>ledgers</c> and stamps <c>schema_version</c> back to 44. Any captured mailing details are discarded —
    /// that is what a downgrade means. Nothing else is touched, because v45 added no tables, indexes or constraints.
    ///
    /// <para><b>Why this is code and not a <c>DROP COLUMN</c> script.</b> SQLite implements
    /// <c>ALTER TABLE … DROP COLUMN</c> by editing the table's stored <c>CREATE TABLE</c> text, and that editing
    /// fails outright on a heavily-commented DDL like ours — it leaves a dangling trailing comma ahead of the
    /// v45 comment block and SQLite rejects the result with <c>"error in table ledgers after drop column:
    /// incomplete input"</c>. The alternative the repo used previously was to hand-write the whole prior-version
    /// <c>CREATE TABLE</c> in the downgrade; for <c>ledgers</c> that would mean duplicating sixty-odd columns that
    /// then silently rot the next time a column is added. So this rebuilds the table from
    /// <c>PRAGMA table_info</c> instead: whatever columns exist minus the v45 four. It cannot drift.</para>
    /// </summary>
    public static void V45ToV44(SqliteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var keep = ColumnNames(connection, "ledgers")
            .Where(c => !Schema.V45MailingColumns.Contains(c, StringComparer.OrdinalIgnoreCase))
            .ToList();

        if (keep.Count > 0 && keep.Count < ColumnNames(connection, "ledgers").Count)
        {
            var columnList = string.Join(", ", keep.Select(c => $"\"{c}\""));

            // Foreign keys off for the swap: other tables reference ledgers(id), and the intermediate DROP would
            // otherwise trip enforcement. The rebuilt table carries the same rows and the same id values.
            Exec(connection, "PRAGMA foreign_keys=OFF;");
            Exec(connection, $"""
                CREATE TABLE ledgers_v44 AS SELECT {columnList} FROM ledgers;
                DROP TABLE ledgers;
                ALTER TABLE ledgers_v44 RENAME TO ledgers;
                """);
            Exec(connection, "PRAGMA foreign_keys=ON;");
        }

        Exec(connection, "UPDATE schema_version SET version = 44;");
    }

    private static List<string> ColumnNames(SqliteConnection connection, string table)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info(\"{table}\");";
        var names = new List<string>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) names.Add(r.GetString(1));
        return names;
    }

    private static void Exec(SqliteConnection connection, string sql)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
