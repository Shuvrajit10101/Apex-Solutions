using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Io;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Apex.Desktop.Tests;

/// <summary>
/// v70 (census 2.9, wave 12 hc continuation) — the List of Budgets' hierarchy reaches the EMITTED export bytes: the
/// CSV carries an <b>Under</b> column naming the parent budget, rows in tree order (a child directly beneath its
/// parent), read off a book reloaded through a FRESH <see cref="CompanyStorage"/>.
///
/// <para><b>Mutation (run 2026-11-23):</b> the <c>r.Under</c> cell in <c>BudgetMasterViewModel.ToMasterListSnapshot</c>
/// replaced by <c>"Primary"</c> reddens
/// <see cref="The_exported_List_of_Budgets_names_each_parent_and_lists_a_child_beneath_it_off_a_reloaded_book"/>.</para>
/// </summary>
public sealed class BudgetListExportHierarchyTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "ApexW12HCExport_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
        }
        catch (IOException) { /* best effort */ }
        catch (UnauthorizedAccessException) { /* best effort */ }
    }

    [Fact]
    public void The_exported_List_of_Budgets_names_each_parent_and_lists_a_child_beneath_it_off_a_reloaded_book()
    {
        const string co = "W12 Budget Export Co";
        var shell = new MainWindowViewModel(new CompanyStorage(_dir));
        shell.NewCompanyName = co;
        shell.CreateCompany();
        var c = shell.Company!;
        var from = c.FinancialYearStart;
        var to = from.AddYears(1).AddDays(-1);
        var ledger = c.Ledgers.First().Id;

        // "Alpha" sorts before its parent "Zed", so a flat name sort would list the child first.
        var zed = new Budget(Guid.NewGuid(), "Zed Department Budget", from, to);
        zed.AddLine(BudgetLine.ForLedger(ledger, BudgetType.OnClosingBalance, new Money(10_000.37m)));
        var alpha = new Budget(Guid.NewGuid(), "Alpha Sales Budget", from, to, underId: zed.Id);
        alpha.AddLine(BudgetLine.ForLedger(ledger, BudgetType.OnNettTransactions, new Money(2_500.05m)));
        c.AddBudget(zed);
        c.AddBudget(alpha);
        new CompanyStorage(_dir).Save(c);

        SqliteConnection.ClearAllPools();
        var fresh = new CompanyStorage(_dir);
        var reloaded = fresh.Load(fresh.ListCompanies().Single(e => e.Name == co));
        var screen = new BudgetMasterViewModel(reloaded, fresh, () => { });

        var export = MasterListTabularProjector.ProjectSource(screen);
        byte[] bytes = Array.Empty<byte>();
        var vm = new ExportViewModel("Budgets", () => export, projectPrint: null, folder: "C:\\Out",
            now: new DateTime(2026, 11, 23, 12, 0, 0), writeBytes: (_, b) => bytes = b)
        {
            Format = ExportFormat.Csv,
        };
        Assert.True(vm.Apply());

        var text = Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF');
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.TrimEnd('\r')).ToList();
        var header = lines[0].Split(',');
        Assert.Equal(new[] { "Name", "Under", "Period", "Lines" }, header);
        var budgetRows = lines.Skip(1).Where(l => l.Contains("Budget", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, budgetRows.Count);
        Assert.StartsWith("Zed Department Budget,Primary,", budgetRows[0], StringComparison.Ordinal);
        Assert.StartsWith("Alpha Sales Budget,Zed Department Budget,", budgetRows[1], StringComparison.Ordinal);
        Assert.DoesNotContain("tally", text, StringComparison.OrdinalIgnoreCase);
    }
}
