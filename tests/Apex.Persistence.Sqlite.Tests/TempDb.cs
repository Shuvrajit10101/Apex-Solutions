using Microsoft.Data.Sqlite;

namespace Apex.Persistence.Sqlite.Tests;

/// <summary>
/// Shared temp-database teardown for the round-trip tests. Microsoft.Data.Sqlite pools connections, so a
/// store's underlying file handle can outlive its <c>Dispose</c> until the pool is cleared — a plain
/// <c>File.Delete</c> then throws an <see cref="IOException"/> ("used by another process") on Windows.
/// <see cref="Delete"/> first clears every pooled connection and then deletes tolerantly (short retry),
/// so a test's <c>finally</c> teardown is robust regardless of pooling timing.
/// </summary>
internal static class TempDbFile
{
    /// <summary>A fresh, unique temp .db path with the given prefix (never collides across tests).</summary>
    public static string NewPath(string prefix) =>
        Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}.db");

    /// <summary>
    /// Deletes a temp database file robustly: clears all pooled connections (releasing the file handle held
    /// open by pooling), then deletes with a few short retries, swallowing a lingering lock rather than
    /// failing the test's teardown. A missing file is a no-op.
    /// </summary>
    public static void Delete(string dbPath)
    {
        if (string.IsNullOrEmpty(dbPath)) return;

        // Release any handle the connection pool is still holding on this file.
        SqliteConnection.ClearAllPools();

        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                if (File.Exists(dbPath)) File.Delete(dbPath);
                return;
            }
            catch (IOException)
            {
                // The OS may not have released the handle yet; clear pools again and retry briefly.
                SqliteConnection.ClearAllPools();
                Thread.Sleep(25);
            }
            catch (UnauthorizedAccessException)
            {
                SqliteConnection.ClearAllPools();
                Thread.Sleep(25);
            }
        }
        // Last-chance best effort; a leftover temp file must never fail the suite.
        try { if (File.Exists(dbPath)) File.Delete(dbPath); } catch { /* leave the temp file behind */ }
    }
}
