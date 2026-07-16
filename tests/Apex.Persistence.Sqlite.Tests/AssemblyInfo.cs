using Xunit;

// Test-infra only: run every test class in this assembly serially.
//
// SqliteCompanyStore opens a *pooled* Microsoft.Data.Sqlite connection (the connection string does not
// set Pooling=False), and virtually every test here (49 files) clears the pool in its teardown — via
// TempDbFile.Delete → the process-global SqliteConnection.ClearAllPools(), or SqliteConnection.ClearPool
// on a helper connection. xUnit runs distinct test classes (collections) in parallel by default, so one
// class's teardown could fire ClearAllPools() while another class was mid-Save/Load/EnsureSchema —
// disposing the pooled native sqlite3 handle out from under a running prepare/step and flaking it with an
// intermittent ObjectDisposedException on SQLitePCL.sqlite3 (observed in EnsureSchema → sqlite3_prepare_v2).
//
// Disabling parallelization serializes the classes so a pool clear can never run concurrently with another
// DB-touching test. This changes no product behaviour — it only sequences the test run. This mirrors the
// identical guard already in Apex.Desktop.Tests/AssemblyInfo.cs.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
