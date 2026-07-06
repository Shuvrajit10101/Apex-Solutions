using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Io;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;

namespace Apex.Desktop.Tests;

/// <summary>
/// UI-side coverage for Phase-5 slice-13: the E / Alt+E Export action wired onto the MASTER-LIST screens
/// (Chart of Accounts, the ledger-creation list, the stock-item-creation list) plus the shortcut/nav
/// completeness checks (RQ-14/16, RQ-28..30).
///
/// <para>Each master list projects through <see cref="MasterListTabularProjector"/> into a
/// <see cref="TabularExport"/> — real column captions, amounts as <b>Number</b> cells a spreadsheet can sum —
/// and reuses the existing <see cref="ExportViewModel"/> + CSV / XLSX / PDF writers. These tests pin the thin
/// Avalonia layer: the shell opens an export column over a master screen, the projected CSV / XLSX carries the
/// expected captions + rows with amounts as real numbers, nothing carries "tally" (RQ-13), the P/E/M/O header
/// hints map to LIVE keys in their context, and every report family resolves under the Reports section of the
/// cascading nav (never a flat dump).</para>
/// </summary>
public sealed class MasterListExportViewModelTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public MasterListExportViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexMasterExportTests_" + Guid.NewGuid().ToString("N"));
        _storage = new CompanyStorage(_tempDir);
    }

    // ---------------------------------------------------------------- helpers

    private MainWindowViewModel NewSeededCompany(string name)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = name;
        vm.CreateCompany();
        Assert.Equal(Screen.Gateway, vm.CurrentScreen);
        return vm;
    }

    private sealed class Captured
    {
        public string? Path;
        public byte[] Bytes = Array.Empty<byte>();
    }

    /// <summary>Wraps the shell's open export panel with a captured-bytes seam by rebuilding an equivalent VM
    /// over the SAME live master projector, so we assert the projection without touching disk.</summary>
    private static ExportViewModel Capture(string title, Func<TabularExport> project, out Captured cap,
        ExportFormat format = ExportFormat.Csv)
    {
        var captured = new Captured();
        var vm = new ExportViewModel(title, project, projectPrint: null, folder: "C:\\Out",
            now: new DateTime(2026, 7, 6, 12, 0, 0),
            writeBytes: (path, bytes) => { captured.Path = path; captured.Bytes = bytes; })
        {
            Format = format,
        };
        cap = captured;
        return vm;
    }

    private static readonly byte[] Utf8Bom = { 0xEF, 0xBB, 0xBF };

    /// <summary>Parses RFC-4180 CSV bytes (UTF-8, optional BOM) back into records of fields.</summary>
    private static List<List<string>> ParseCsv(byte[] bytes)
    {
        int start = (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) ? 3 : 0;
        string text = Encoding.UTF8.GetString(bytes, start, bytes.Length - start);

        var records = new List<List<string>>();
        var record = new List<string>();
        var field = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else inQuotes = false;
                }
                else field.Append(c);
            }
            else if (c == '"') inQuotes = true;
            else if (c == ',') { record.Add(field.ToString()); field.Clear(); }
            else if (c == '\r') { /* swallow */ }
            else if (c == '\n')
            {
                record.Add(field.ToString()); field.Clear();
                records.Add(record); record = new List<string>();
            }
            else field.Append(c);
        }
        if (field.Length > 0 || record.Count > 0)
        {
            record.Add(field.ToString());
            records.Add(record);
        }
        return records;
    }

    // ================================================================ (1) Chart of Accounts export

    [Fact]
    public void ChartOfAccounts_export_opens_over_the_master_and_has_real_captions_and_a_numeric_opening()
    {
        var shell = new MainWindowViewModel(_storage);
        shell.LoadRobertDemo();
        shell.ShowChartOfAccounts();
        Assert.Equal(Screen.ChartOfAccounts, shell.CurrentScreen);

        // E / Alt+E opens the export column over the master list.
        Assert.True(shell.IsExportablePage);
        shell.OpenExport();
        Assert.NotNull(shell.ExportPanel);
        Assert.Equal(Screen.Export, shell.CurrentScreen);
        Assert.Equal("Chart of Accounts", shell.ExportPanel!.DocumentTitle);

        // The projection carries the master's real captions and a numeric Opening column.
        var export = MasterListTabularProjector.ProjectChartOfAccounts(shell.ChartOfAccounts!);
        Assert.Equal(new[] { "Name", "Type", "Nature", "Opening", "Dr/Cr" },
            export.Columns.Select(c => c.Header).ToArray());
        Assert.Equal(CellType.Number, export.Columns[3].Type);           // Opening is a Number column

        // A ledger row (Robert's Cash) carries its opening as an exact Number cell (105000.00), side "Dr".
        var vm = Capture("Chart of Accounts", () => export, out var cap, ExportFormat.Csv);
        Assert.True(vm.Apply());
        var records = ParseCsv(cap.Bytes);
        Assert.Equal("Name", records[0][0]);
        Assert.Equal("Opening", records[0][3]);

        // Every populated Opening cell is a plain invariant number (a spreadsheet can sum it) — no Indian grouping.
        var openings = records.Skip(1).Where(r => r.Count > 3 && r[3].Length > 0).Select(r => r[3]).ToList();
        Assert.NotEmpty(openings);
        foreach (var o in openings)
        {
            Assert.DoesNotContain(",", o);
            Assert.True(decimal.TryParse(o, System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture, out _), $"'{o}' is not a plain number");
        }
        Assert.DoesNotContain("tally", Encoding.UTF8.GetString(cap.Bytes), StringComparison.OrdinalIgnoreCase);
    }

    // ================================================================ (2) Ledgers list export

    [Fact]
    public void Ledgers_list_export_csv_carries_captions_and_openings_as_numbers()
    {
        var shell = new MainWindowViewModel(_storage);
        shell.LoadRobertDemo();
        shell.ShowLedgerMaster();
        Assert.Equal(Screen.LedgerMaster, shell.CurrentScreen);
        Assert.True(shell.IsExportablePage);

        var lm = shell.LedgerMaster!;
        Assert.NotEmpty(lm.Existing);                                    // Robert seeds several ledgers

        var export = MasterListTabularProjector.ProjectLedgers(lm);
        Assert.Equal(new[] { "Name", "Under", "Opening", "Dr/Cr", "Currency", "Interest" },
            export.Columns.Select(c => c.Header).ToArray());
        Assert.Equal(CellType.Number, export.Columns[2].Type);

        var vm = Capture("Ledgers", () => export, out var cap, ExportFormat.Csv);
        Assert.True(vm.Apply());
        var records = ParseCsv(cap.Bytes);
        Assert.Equal(new[] { "Name", "Under", "Opening", "Dr/Cr", "Currency", "Interest" },
            records[0].ToArray());

        // At least one ledger has a non-empty opening balance, exported as a bare number with a Dr/Cr side.
        var withOpening = records.Skip(1).Where(r => r.Count > 3 && r[2].Length > 0).ToList();
        Assert.NotEmpty(withOpening);
        foreach (var r in withOpening)
        {
            Assert.DoesNotContain(",", r[2]);
            Assert.True(decimal.TryParse(r[2], System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture, out _));
            Assert.True(r[3] is "Dr" or "Cr");                          // the side survives as its own column
        }
    }

    // ================================================================ (3) Stock-items list export

    [Fact]
    public void StockItems_list_export_xlsx_has_a_numeric_opening_value_cell()
    {
        var shell = NewSeededCompany("Stock Export Co");

        // Seed a group + unit + a stock item with an opening value of 100 × 25.50 = 2,550.00.
        shell.ShowStockGroupMaster();
        shell.StockGroupMaster!.Name = "Finished Goods";
        Assert.True(shell.StockGroupMaster.Create());

        shell.ShowUnitMaster();
        shell.UnitMaster!.IsCompound = false;
        shell.UnitMaster.Symbol = "Nos";
        shell.UnitMaster.FormalName = "Numbers";
        shell.UnitMaster.DecimalPlacesText = "0";
        Assert.True(shell.UnitMaster.Create());

        shell.ShowStockItemMaster();
        Assert.Equal(Screen.StockItemMaster, shell.CurrentScreen);
        var m = shell.StockItemMaster!;
        m.Name = "Widget-X";
        m.SelectedGroup = m.Groups.Single(g => g.Name == "Finished Goods");
        m.SelectedUnit = m.Units.Single(u => u.Symbol == "Nos");
        m.OpeningQuantityText = "100";
        m.OpeningRateText = "25.50";
        Assert.True(m.Create());
        Assert.Contains(m.Existing, r => r.Name == "Widget-X");

        Assert.True(shell.IsExportablePage);
        var export = MasterListTabularProjector.ProjectStockItems(m);
        Assert.Equal(new[] { "Name", "Under", "Unit", "Valuation", "Opening Value" },
            export.Columns.Select(c => c.Header).ToArray());
        Assert.Equal(CellType.Number, export.Columns[4].Type);

        var vm = Capture("Stock Items", () => export, out var cap, ExportFormat.Xlsx);
        Assert.True(vm.Apply());
        Assert.EndsWith(".xlsx", cap.Path);

        using var zip = new ZipArchive(new MemoryStream(cap.Bytes), ZipArchiveMode.Read);
        var sheet = zip.GetEntry("xl/worksheets/sheet1.xml")!;
        string sheetXml;
        using (var r = new StreamReader(sheet.Open())) sheetXml = r.ReadToEnd();

        // The opening value 2550.00 is a numeric cell (a real number, not an inline string).
        Assert.Matches(@"<c[^>]*><v>2550(\.0+)?</v></c>", sheetXml);

        foreach (var entry in zip.Entries)
        {
            using var r = new StreamReader(entry.Open());
            Assert.DoesNotContain("tally", r.ReadToEnd(), StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Master_export_default_filename_is_the_master_title_and_pdf_is_available()
    {
        var shell = new MainWindowViewModel(_storage);
        shell.LoadRobertDemo();
        shell.ShowLedgerMaster();
        shell.OpenExport();
        var panel = shell.ExportPanel!;
        Assert.Equal("Ledgers", panel.FileName);                        // stem = the master title
        Assert.Equal("Ledgers.csv", panel.ResolvedFileName);

        // A master list has no bespoke print projector, so PDF is served by the generic tabular fallback.
        panel.Format = ExportFormat.Pdf;
        Assert.Equal("Ledgers.pdf", panel.ResolvedFileName);
        Assert.True(panel.Apply());
        Assert.Contains("Exported", panel.Status);
    }

    [Fact]
    public void OpenExport_is_a_noop_on_a_non_exportable_screen_and_does_not_stack()
    {
        var shell = NewSeededCompany("Noop Co");
        Assert.Equal(Screen.Gateway, shell.CurrentScreen);
        Assert.False(shell.IsExportablePage);
        shell.OpenExport();                                             // Gateway is not exportable
        Assert.Null(shell.ExportPanel);

        shell.ShowChartOfAccounts();
        shell.OpenExport();
        var first = shell.ExportPanel;
        Assert.NotNull(first);
        shell.OpenExport();                                             // re-press: must not stack a second panel
        Assert.Same(first, shell.ExportPanel);
    }

    // ================================================================ (Fix 1) generic master-list export

    [Fact]
    public void Parties_list_export_csv_carries_only_party_ledgers_with_captions_and_numeric_openings()
    {
        var shell = new MainWindowViewModel(_storage);
        shell.LoadRobertDemo();
        shell.ShowLedgerMaster();
        var lm = shell.LedgerMaster!;

        // The Parties projection keeps ONLY ledgers under Sundry Debtors / Sundry Creditors.
        var export = MasterListTabularProjector.ProjectParties(lm);
        Assert.Equal(new[] { "Name", "Under", "Opening", "Dr/Cr", "Currency", "Interest" },
            export.Columns.Select(c => c.Header).ToArray());
        Assert.Equal(CellType.Number, export.Columns[2].Type);

        var vm = Capture("Parties", () => export, out var cap, ExportFormat.Csv);
        Assert.True(vm.Apply());
        var records = ParseCsv(cap.Bytes);
        Assert.Equal(new[] { "Name", "Under", "Opening", "Dr/Cr", "Currency", "Interest" }, records[0].ToArray());

        var body = records.Skip(1).Where(r => r.Count > 1 && r[0].Length > 0).ToList();
        Assert.NotEmpty(body);                                            // Robert seeds party ledgers
        foreach (var r in body)
            Assert.Contains("Sundry", r[1], StringComparison.OrdinalIgnoreCase);   // only party groups survive

        // No non-party ledger (e.g. Cash) leaked in.
        Assert.DoesNotContain(body, r => r[0].Equals("Cash", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CostCentres_master_exports_generically_via_the_source_snapshot()
    {
        var shell = NewSeededCompany("CC Export Co");

        // Seed a cost category + a centre so the list is non-empty.
        shell.ShowCostCategoryMaster();
        shell.CostCategoryMaster!.Name = "Departments";
        Assert.True(shell.CostCategoryMaster.Create());

        shell.ShowCostCentreMaster();
        var m = shell.CostCentreMaster!;
        m.Name = "Head Office";
        m.SelectedCategory = m.Categories.Single(c => c.Name == "Departments");
        Assert.True(m.Create());
        Assert.Contains(m.Existing, r => r.Name == "Head Office");

        // The generic path lights up: E / Alt+E is live and the export column opens over the cost-centre master.
        Assert.Equal(Screen.CostCentreMaster, shell.CurrentScreen);
        Assert.True(shell.IsExportablePage);
        shell.OpenExport();
        Assert.NotNull(shell.ExportPanel);
        Assert.Equal("Cost Centres", shell.ExportPanel!.DocumentTitle);

        // The projected CSV carries the master's captions + the seeded row.
        var export = MasterListTabularProjector.ProjectSource(m);
        Assert.Equal(new[] { "Name", "Category", "Under" }, export.Columns.Select(c => c.Header).ToArray());

        var vm = Capture("Cost Centres", () => export, out var cap, ExportFormat.Csv);
        Assert.True(vm.Apply());
        var records = ParseCsv(cap.Bytes);
        Assert.Equal(new[] { "Name", "Category", "Under" }, records[0].ToArray());
        Assert.Contains(records.Skip(1), r => r.Count > 0 && r[0] == "Head Office");
        Assert.DoesNotContain("tally", Encoding.UTF8.GetString(cap.Bytes), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Groups_master_is_generically_exportable_when_it_is_the_top_page_column()
    {
        // The Ledger master doubles as the Group/Ledger creation list; every OTHER master (cost centres, godowns,
        // units, currencies, scenarios, budgets, stock groups/categories) is now exportable through the generic
        // source path — pin one more (a Stock Group) to prove the uniform wiring beyond cost centres.
        var shell = NewSeededCompany("Group Export Co");
        shell.ShowStockGroupMaster();
        shell.StockGroupMaster!.Name = "Raw Materials";
        Assert.True(shell.StockGroupMaster.Create());

        Assert.True(shell.IsExportablePage);
        var export = MasterListTabularProjector.ProjectSource(shell.StockGroupMaster!);
        Assert.Equal(new[] { "Name", "Under", "Quantities" }, export.Columns.Select(c => c.Header).ToArray());

        var vm = Capture("Stock Groups", () => export, out var cap, ExportFormat.Csv);
        Assert.True(vm.Apply());
        var records = ParseCsv(cap.Bytes);
        Assert.Contains(records.Skip(1), r => r.Count > 0 && r[0] == "Raw Materials");
    }

    // ================================================================ (Fix 2) button-bar 'O' hint = Import

    [Fact]
    public void Button_bar_O_letter_is_not_claimed_by_Outstandings_because_bare_O_triggers_Import()
    {
        var shell = new MainWindowViewModel(_storage);
        shell.LoadRobertDemo();

        // No button-bar entry advertises the single letter "O" — the bare-O key is bound to Import (RQ-28), so
        // nothing else may claim it. The Outstandings quick-button carries a non-colliding mnemonic instead.
        Assert.DoesNotContain(shell.ButtonBar, b => b.Key == "O");
        Assert.Contains(shell.ButtonBar, b => b.Caption == "Outstandings");

        // And bare-O really does open Import on the Gateway (the action the "O" hint promises).
        Assert.Equal(Screen.Gateway, shell.CurrentScreen);
        shell.OpenImport();
        Assert.NotNull(shell.ImportDataPanel);
    }

    // ================================================================ (Fix 3) Account Books family (RQ-30 / §16)

    [Fact]
    public void Account_Books_family_resolves_under_reports_with_cash_bank_and_ledger()
    {
        var shell = NewSeededCompany("Books Co");

        // "Account Books" sits under the Reports section of the root cascade.
        Assert.Equal("Reports", SectionOf(shell, "Account Books"));

        // Drilling it exposes the three core books.
        shell.ShowAccountBooksMenu();
        var books = shell.Columns[^1];
        var labels = books.Items.Where(i => i.IsSelectable).Select(i => i.Label).ToArray();
        Assert.Equal(new[] { "Cash Book", "Bank Book", "Ledger" }, labels);
    }

    [Fact]
    public void Account_Books_pickers_open_a_ledger_book_reusing_the_existing_drill()
    {
        var shell = new MainWindowViewModel(_storage);
        shell.LoadRobertDemo();

        // Cash Book lists cash ledgers; picking one opens that ledger's book (the RQ-7 LedgerVouchers drill).
        shell.ShowCashBookMenu();
        var cash = shell.Columns[^1];
        var cashLedger = cash.Items.FirstOrDefault(i => i.IsSelectable);
        Assert.NotNull(cashLedger);                                       // Robert seeds a Cash ledger
        shell.OpenAccountBook(cashLedger!.Label);
        Assert.NotNull(shell.LedgerVouchers);
        Assert.Equal(Screen.LedgerVouchers, shell.CurrentScreen);
        shell.Back();
        shell.Back();

        // Bank Book lists bank ledgers.
        shell.ShowBankBookMenu();
        var bank = shell.Columns[^1];
        Assert.Contains(bank.Items, i => i.IsSelectable);                 // Robert seeds a Bank ledger

        // Ledger lists every ledger.
        shell.ShowLedgerBooksMenu();
        var all = shell.Columns[^1];
        Assert.True(all.Items.Count(i => i.IsSelectable) > bank.Items.Count(i => i.IsSelectable));
    }

    // ================================================================ (RQ-28) P/E/M/O header bar is LIVE

    [Fact]
    public void PEMO_header_hints_map_to_live_keys_in_their_context()
    {
        var shell = new MainWindowViewModel(_storage);
        shell.LoadRobertDemo();

        // O: Import is live on the Gateway (a company is open there).
        Assert.Equal(Screen.Gateway, shell.CurrentScreen);
        shell.OpenImport();
        Assert.NotNull(shell.ImportDataPanel);
        Assert.Equal(Screen.ImportData, shell.CurrentScreen);
        shell.Back();

        // On a report: P: Print, E: Export, M: E-Mail are all live.
        shell.OpenReport(ReportKind.TrialBalance);
        Assert.True(shell.IsPrintablePage);                            // P: Print
        Assert.True(shell.IsExportablePage);                          // E: Export

        shell.OpenPrintPreview();
        Assert.NotNull(shell.PrintPreview);
        shell.Back();

        shell.OpenEmailCompose();                                     // M: E-Mail
        Assert.NotNull(shell.EmailCompose);
        shell.Back();

        shell.OpenExport();                                           // E: Export
        Assert.NotNull(shell.ExportPanel);
    }

    // ================================================================ (RQ-30) every report family under Reports

    [Fact]
    public void Every_report_family_resolves_under_the_reports_section_of_the_nav()
    {
        var shell = NewSeededCompany("Nav Co");

        // The report families expected under the single "Reports" section header (never a flat dump).
        string[] families =
        {
            "Balance Sheet", "Profit & Loss A/c", "Trial Balance",
            "Account Books",                                         // Cash Book / Bank Book / Ledger (§16)
            "Statements",                                            // Cash Flow / Funds Flow / Ratio
            "Statements of Accounts",
            "Inventory Reports",
            "GST Reports",
            "Exception Reports",
        };

        foreach (var family in families)
            Assert.Equal("Reports", SectionOf(shell, family));
    }

    /// <summary>The section-header label the menu item <paramref name="label"/> sits under (walking the ordered
    /// cascade column). Returns null when the item is not present.</summary>
    private static string? SectionOf(MainWindowViewModel vm, string label)
    {
        string? current = null;
        foreach (var item in vm.Menu)
        {
            if (item.IsHeader) current = item.Label;
            else if (item.Label == label) return current;
        }
        return null;
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch { /* best effort */ }
    }
}
