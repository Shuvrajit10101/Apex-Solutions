using Xunit;

// Test-infra only: run every test class serially.
//
// These view-model tests each create/save/reload a throwaway SQLite .db and, in Dispose, call the
// process-global Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(). xUnit runs distinct test
// classes (collections) in parallel by default, so one class's Dispose could fire ClearAllPools()
// while another class was mid-Save/Load — tearing the pooled native sqlite3 handle out from under a
// running test and flaking it with an intermittent ObjectDisposedException (or a save/reload assert).
//
// Disabling parallelization serializes the classes so ClearAllPools() can never run concurrently with
// another DB-touching test. This changes no product behaviour — it only sequences the test run.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
