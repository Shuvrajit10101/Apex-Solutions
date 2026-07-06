using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Ledger.Domain;
using Apex.Ledger.Io;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;

namespace Apex.Desktop.Tests;

/// <summary>
/// UI-side coverage for the "Export Data" (RQ-19/DP-4 canonical backup) and "Import" (RQ-20..24 engine-routed
/// import) panels — the thin Avalonia layer over <c>Apex.Ledger.Io</c>'s canonical writers/parsers and the
/// engine-routed <see cref="CompanyImportService"/>.
///
/// <para>The writers/parser/service are trusted (covered by <c>Apex.Ledger.Io.Tests</c> and the engine's PR-4 gate);
/// these tests pin the thin layer: a company exported by the Export-Data VM and imported by the Import VM into a fresh
/// company reconciles to the paisa (round-trip at the VM layer, JSON and XML); a corrupted / unbalanced / dangling
/// file surfaces the per-record errors and mutates nothing; the duplicate-policy chooser is honoured; the CSV path
/// applies; and no output carries "tally". A read/write seam supplies/captures bytes without touching disk.</para>
/// </summary>
public sealed class ImportExportDataViewModelTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public ImportExportDataViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexImportTests_" + Guid.NewGuid().ToString("N"));
        _storage = new CompanyStorage(_tempDir);
    }

    // A populated source company (Robert demo: seeded + custom ledgers with openings + posted vouchers).
    private static Company SourceCompany() => DemoData.BuildRobert("Robert Source");

    private static Company FreshCompany(string name = "Fresh Co") => CompanyFactory.CreateSeeded(name);

    private static readonly DateOnly AsOf = new(2100, 3, 31); // well past any books date

    private static byte[] ExportJson(Company c)
    {
        byte[] captured = Array.Empty<byte>();
        var vm = new ExportDataViewModel(c, "C:\\Out", new DateTime(2026, 7, 6, 12, 0, 0),
            writeBytes: (_, bytes) => captured = bytes)
        { Format = CompanyExportFormat.Json };
        Assert.True(vm.Apply());
        return captured;
    }

    private static byte[] ExportXml(Company c)
    {
        byte[] captured = Array.Empty<byte>();
        var vm = new ExportDataViewModel(c, "C:\\Out", new DateTime(2026, 7, 6, 12, 0, 0),
            writeBytes: (_, bytes) => captured = bytes)
        { Format = CompanyExportFormat.Xml };
        Assert.True(vm.Apply());
        return captured;
    }

    private ImportDataViewModel ImportVm(Company target, byte[] bytes, ImportDataFormat format,
        DuplicatePolicy policy = DuplicatePolicy.Skip)
        => new(target, _storage, onImported: null, readBytes: _ => bytes)
        {
            FilePath = "backup." + format,
            Format = format,
            Policy = policy,
        };

    // ---------------------------------------------------------------- round-trip: JSON + XML reconcile

    [Fact]
    public void Json_round_trip_at_the_vm_layer_reproduces_the_company_to_the_paisa()
    {
        var source = SourceCompany();
        var bytes = ExportJson(source);

        var fresh = FreshCompany();
        var import = ImportVm(fresh, bytes, ImportDataFormat.Json);
        Assert.True(import.Apply());
        Assert.Empty(import.Errors);
        Assert.Contains("posted", import.Status);

        AssertReconciles(source, fresh);
    }

    [Fact]
    public void Xml_round_trip_at_the_vm_layer_reproduces_the_company_to_the_paisa()
    {
        var source = SourceCompany();
        var bytes = ExportXml(source);

        var fresh = FreshCompany();
        var import = ImportVm(fresh, bytes, ImportDataFormat.Xml);
        Assert.True(import.Apply());
        Assert.Empty(import.Errors);

        AssertReconciles(source, fresh);
    }

    [Fact]
    public void Successful_import_persists_the_company_to_storage()
    {
        var source = SourceCompany();
        var bytes = ExportJson(source);
        var fresh = FreshCompany("Persisted Co");

        Assert.True(ImportVm(fresh, bytes, ImportDataFormat.Json).Apply());

        // The import restores the source's whole header (including its name), and the panel persists the mutated
        // company via CompanyStorage under that name — so it is discoverable and reloads with the imported data.
        Assert.Equal(source.Name, fresh.Name);
        var reloaded = _storage.Load(_storage.ListCompanies().Single(e => e.Name == source.Name));
        AssertReconciles(source, reloaded);
    }

    // ---------------------------------------------------------------- rejects mutate nothing

    [Fact]
    public void A_corrupted_file_surfaces_errors_and_does_not_mutate_the_company()
    {
        var fresh = FreshCompany();
        var before = SnapshotCounts(fresh);

        var import = ImportVm(fresh, Encoding.UTF8.GetBytes("{ not valid json"), ImportDataFormat.Json);
        Assert.False(import.Apply());
        Assert.NotEmpty(import.Errors);
        Assert.Contains("unchanged", import.Status);

        Assert.Equal(before, SnapshotCounts(fresh));      // no masters/vouchers leaked in
    }

    [Fact]
    public void An_unbalanced_voucher_is_rejected_and_the_company_is_unchanged()
    {
        var source = SourceCompany();
        var bytes = ExportJson(source);
        // Corrupt one debit line's paisa by +1 so the voucher no longer balances.
        var corrupted = BumpFirstAmountPaisa(bytes);

        var fresh = FreshCompany();
        var before = SnapshotCounts(fresh);

        var import = ImportVm(fresh, corrupted, ImportDataFormat.Json);
        Assert.False(import.Apply());
        Assert.Contains(import.Errors, msg => msg.Contains("unbalanced", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(before, SnapshotCounts(fresh));
    }

    // ---------------------------------------------------------------- duplicate policy chooser is honoured

    [Fact]
    public void RejectBatch_policy_rejects_when_a_master_already_exists_and_applies_nothing()
    {
        var source = SourceCompany();
        var bytes = ExportJson(source);

        var fresh = FreshCompany();
        Assert.True(ImportVm(fresh, bytes, ImportDataFormat.Json).Apply()); // first import creates the masters
        var afterFirst = SnapshotCounts(fresh);

        // Re-importing the same data under Reject-batch must reject (Cash / seed masters already exist) and change nothing.
        var reject = ImportVm(fresh, bytes, ImportDataFormat.Json, DuplicatePolicy.RejectBatch);
        Assert.False(reject.Apply());
        Assert.Contains(reject.Errors, msg => msg.Contains("reject-batch", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(afterFirst, SnapshotCounts(fresh));
    }

    [Fact]
    public void Skip_policy_reuses_existing_masters_on_a_re_import()
    {
        var source = SourceCompany();
        var bytes = ExportJson(source);

        var fresh = FreshCompany();
        Assert.True(ImportVm(fresh, bytes, ImportDataFormat.Json).Apply());
        var ledgersAfterFirst = fresh.Ledgers.Count;

        // Re-import under Skip: masters are reused (not duplicated); the status reflects reuse.
        var reimport = ImportVm(fresh, bytes, ImportDataFormat.Json, DuplicatePolicy.Skip);
        Assert.True(reimport.Apply());
        Assert.Contains("reused", reimport.Status);
        Assert.Equal(ledgersAfterFirst, fresh.Ledgers.Count); // no ledger duplicated by name
    }

    // ---------------------------------------------------------------- CSV best-effort path applies

    [Fact]
    public void Csv_import_applies_flat_masters_and_a_balanced_voucher()
    {
        var fresh = FreshCompany();
        var csv =
            "#ledgers\n" +
            "Name,Under,OpeningBalance,OpeningSide\n" +
            "Acme Traders,Sundry Debtors,1000.00,Debit\n" +
            "#vouchers\n" +
            "Date,Type,VoucherRef,Ledger,Amount,DrCr,Narration\n" +
            "2026-04-02,Receipt,V1,Cash,500.00,Debit,Cash sale\n" +
            "2026-04-02,Receipt,V1,Acme Traders,500.00,Credit,\n";

        var import = ImportVm(fresh, Encoding.UTF8.GetBytes(csv), ImportDataFormat.Csv);
        Assert.True(import.Apply());
        Assert.Empty(import.Errors);
        Assert.NotNull(fresh.FindLedgerByName("Acme Traders"));
        Assert.Single(fresh.Vouchers);
    }

    [Fact]
    public void Csv_import_with_a_dangling_voucher_type_is_rejected_and_mutates_nothing()
    {
        var fresh = FreshCompany();
        var before = SnapshotCounts(fresh);
        var csv =
            "#vouchers\n" +
            "Date,Type,VoucherRef,Ledger,Amount,DrCr,Narration\n" +
            "2026-04-02,Nonexistent Type,V1,Cash,500.00,Debit,\n" +
            "2026-04-02,Nonexistent Type,V1,Sales,500.00,Credit,\n";

        var import = ImportVm(fresh, Encoding.UTF8.GetBytes(csv), ImportDataFormat.Csv);
        Assert.False(import.Apply());
        Assert.NotEmpty(import.Errors);
        Assert.Equal(before, SnapshotCounts(fresh));
    }

    // ---------------------------------------------------------------- Export-Data panel + shell wiring

    [Fact]
    public void ExportData_writes_deterministic_bytes_with_no_tally_and_honours_format_and_timestamp()
    {
        var source = SourceCompany();

        string? jsonPath = null; byte[] jsonBytes = Array.Empty<byte>();
        var json = new ExportDataViewModel(source, "C:\\Out", new DateTime(2026, 7, 6, 12, 0, 0),
            writeBytes: (p, b) => { jsonPath = p; jsonBytes = b; })
        { Format = CompanyExportFormat.Json, FileName = "backup", AppendTimestamp = true };
        Assert.Equal("backup_20260706-1200.json", json.ResolvedFileName);
        Assert.True(json.Apply());
        Assert.EndsWith("backup_20260706-1200.json", jsonPath);
        Assert.DoesNotContain("tally", Encoding.UTF8.GetString(jsonBytes), StringComparison.OrdinalIgnoreCase);

        string? xmlPath = null;
        var xml = new ExportDataViewModel(source, "C:\\Out", new DateTime(2026, 7, 6, 12, 0, 0),
            writeBytes: (p, _) => xmlPath = p)
        { Format = CompanyExportFormat.Xml };
        Assert.True(xml.Apply());
        Assert.EndsWith(".xml", xmlPath);
    }

    [Fact]
    public void ExportData_writes_a_real_file_to_disk()
    {
        var source = SourceCompany();
        var vm = new ExportDataViewModel(source, _tempDir, new DateTime(2026, 7, 6), writeBytes: null)
        { Format = CompanyExportFormat.Json, FileName = "backup" };

        Assert.True(vm.Apply());
        var path = Path.Combine(_tempDir, "backup.json");
        Assert.True(File.Exists(path));
        Assert.Contains("Exported", vm.Status);
    }

    [Fact]
    public void OpenImport_and_OpenExportData_are_noops_without_a_company_and_do_not_stack()
    {
        var empty = new MainWindowViewModel(_storage);
        empty.OpenImport();
        empty.OpenExportData();
        Assert.Null(empty.ImportDataPanel);
        Assert.Null(empty.ExportDataPanel);

        var shell = new MainWindowViewModel(_storage);
        shell.LoadRobertDemo();

        shell.OpenImport();
        var firstImport = shell.ImportDataPanel;
        Assert.NotNull(firstImport);
        Assert.Equal(Screen.ImportData, shell.CurrentScreen);
        shell.OpenImport();                                // re-press must not stack a second panel
        Assert.Same(firstImport, shell.ImportDataPanel);

        shell.Back();                                      // close the import column
        shell.OpenExportData();
        var firstExport = shell.ExportDataPanel;
        Assert.NotNull(firstExport);
        Assert.Equal(Screen.ExportData, shell.CurrentScreen);
        shell.OpenExportData();
        Assert.Same(firstExport, shell.ExportDataPanel);
    }

    // ---------------------------------------------------------------- reconciliation helpers

    /// <summary>Reconciles a re-imported <paramref name="fresh"/> company against its <paramref name="source"/>:
    /// the Trial Balance (per-ledger Dr/Cr by name + both totals) and the ledger/voucher counts match to the paisa.</summary>
    private static void AssertReconciles(Company source, Company fresh)
    {
        var src = TrialBalance.Build(source, AsOf);
        var dst = TrialBalance.Build(fresh, AsOf);

        Assert.Equal(src.TotalDebit, dst.TotalDebit);
        Assert.Equal(src.TotalCredit, dst.TotalCredit);
        Assert.True(dst.Balanced);

        var srcRows = src.Rows.ToDictionary(r => r.LedgerName, r => (r.Debit, r.Credit));
        var dstRows = dst.Rows.ToDictionary(r => r.LedgerName, r => (r.Debit, r.Credit));
        Assert.Equal(srcRows.Count, dstRows.Count);
        foreach (var (name, dr) in srcRows)
        {
            Assert.True(dstRows.ContainsKey(name), $"Missing ledger '{name}' after import.");
            Assert.Equal(dr, dstRows[name]);
        }

        Assert.Equal(source.Vouchers.Count, fresh.Vouchers.Count);
    }

    private static (int Groups, int Ledgers, int Vouchers) SnapshotCounts(Company c)
        => (c.Groups.Count, c.Ledgers.Count, c.Vouchers.Count);

    /// <summary>Adds 1 to the first <c>"amountPaisa": N</c> occurrence in a JSON export, unbalancing that voucher.</summary>
    private static byte[] BumpFirstAmountPaisa(byte[] json)
    {
        var text = Encoding.UTF8.GetString(json);
        const string key = "\"amountPaisa\": ";
        var i = text.IndexOf(key, StringComparison.Ordinal);
        Assert.True(i >= 0, "export did not contain an amountPaisa field");
        var start = i + key.Length;
        var end = start;
        while (end < text.Length && (char.IsDigit(text[end]) || text[end] == '-')) end++;
        var value = long.Parse(text[start..end]);
        var bumped = text[..start] + (value + 1) + text[end..];
        return Encoding.UTF8.GetBytes(bumped);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch { /* best effort */ }
    }
}
