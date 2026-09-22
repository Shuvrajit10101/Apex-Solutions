using System;
using System.IO;
using System.Linq;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Xunit;

namespace Apex.Desktop.Tests;

/// <summary>
/// 🔴 <b>USER RULING 26 — THE ON-OPEN WARNING, ASSERTED AT THE PLACE THE OPERATOR ACTUALLY ARRIVES.</b>
///
/// <para><b>Why this file exists when <c>MarketValuationTests</c> already covers the warning.</b> Those tests
/// exercise <see cref="ValuationRemediationNotice"/>, a pure function: they prove the <i>sentence</i> is right.
/// They cannot prove the sentence is ever <b>shown</b>. A book whose Balance Sheet moved on upgrade and whose
/// warning is computed into a string that nothing displays is exactly as silent as no warning at all — and this
/// project has repeatedly shipped services with zero production callers and graded them as progress. These tests
/// drive the <b>real user route</b> end to end: Company Info → Select Company → the company's own menu entry →
/// <c>OpenExisting</c> → <c>OpenCompany</c>, and assert on the property the Gateway banner is bound to.</para>
///
/// <para>They also close the round trip through SQLite: the marker is stamped, saved, reloaded from disk by a
/// <b>fresh</b> view model, and only then does the banner appear. That is the path a real upgrade takes.</para>
///
/// <para><b>Red on today's main</b>, where <c>MainWindowViewModel.ValuationRemediationWarning</c> does not
/// exist at all.</para>
/// </summary>
public sealed class ValuationRemediationBannerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public ValuationRemediationBannerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexValuationBannerTests_" + Guid.NewGuid().ToString("N"));
        _storage = new CompanyStorage(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>
    /// Creates a book holding one stock item and saves it. When <paramref name="remediated"/> is true the item
    /// carries the marker schema v65's migration stamps — i.e. this book is one whose closing stock value moved.
    /// </summary>
    private void SeedCompany(string name, bool remediated)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = name;
        vm.CreateCompany();

        var company = vm.Company!;
        var masters = new InventoryService(company);
        var grp = masters.CreateStockGroup("Goods");
        var nos = masters.CreateSimpleUnit("Nos", "Numbers");
        var item = masters.CreateStockItem(
            "Widget", grp.Id, nos.Id, valuationMethod: StockValuationMethod.LastPurchaseCost);

        // Exactly what Schema.MigrateV64ToV65 writes: moved ONTO LastPurchaseCost, stamped with where it came from.
        if (remediated) item.ValuationRemediatedFrom = StockValuationMethod.LastSaleCost;

        _storage.Save(company);
    }

    /// <summary>
    /// Opens a saved book through the route a keyboard operator actually walks — Company Info → Select Company,
    /// then the company's own entry in the menu — rather than by reaching into a private method.
    /// </summary>
    private MainWindowViewModel OpenThroughTheMenu(string name)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.ShowCompanySelect();

        var entry = vm.Menu.FirstOrDefault(m => m.Label == name);
        Assert.NotNull(entry);
        Assert.Equal("Open", entry!.Hint);
        entry.Activate();

        Assert.NotNull(vm.Company);
        return vm;
    }

    /// <summary>
    /// 🔴 <b>AN AFFECTED BOOK WARNS ITS OPERATOR, ON THE SCREEN THEY LAND ON.</b> The banner text must say what
    /// changed, what the item now uses, and what it means for the reported figures — an operator told only "the
    /// valuation method changed" cannot tell whether to trust last month's Balance Sheet.
    /// </summary>
    [Fact]
    public void An_affected_book_raises_the_banner_when_it_is_opened()
    {
        SeedCompany("Remediated Co", remediated: true);

        var vm = OpenThroughTheMenu("Remediated Co");

        Assert.NotEqual(string.Empty, vm.ValuationRemediationWarning);
        Assert.Contains("Widget", vm.ValuationRemediationWarning);
        Assert.Contains("Last Purchase Cost", vm.ValuationRemediationWarning);
        Assert.Contains("Balance Sheet", vm.ValuationRemediationWarning);

        // The standing brand rule applies to this string like every other user-visible one.
        Assert.DoesNotContain("Tally", vm.ValuationRemediationWarning, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 🔴 <b>AN UNAFFECTED BOOK SHOWS NOTHING — and this is the half that makes the warning worth having.</b>
    /// The banner's visibility is bound to this string being non-empty, so an empty string is literally no
    /// banner. A notice every operator sees on every open is one nobody reads, which would leave the genuinely
    /// affected operator no better off than silence.
    /// </summary>
    [Fact]
    public void An_unaffected_book_shows_no_banner_at_all()
    {
        SeedCompany("Untouched Co", remediated: false);

        var vm = OpenThroughTheMenu("Untouched Co");

        Assert.Equal(string.Empty, vm.ValuationRemediationWarning);
    }

    /// <summary>
    /// Dismissal clears the banner for the session, and the warning <b>returns on the next open</b> because
    /// dismissing does not clear the per-item marker. That is deliberate: the marker is the only record that a
    /// book's reported figures were restated, and this build will not discard evidence of a movement in profit
    /// to silence a notice. (Whether acknowledgement should persist is recorded as a user decision owed.)
    /// </summary>
    [Fact]
    public void Dismissing_the_banner_lasts_for_the_session_only()
    {
        SeedCompany("Dismissive Co", remediated: true);

        var vm = OpenThroughTheMenu("Dismissive Co");
        Assert.NotEqual(string.Empty, vm.ValuationRemediationWarning);

        vm.DismissValuationRemediationWarning();
        Assert.Equal(string.Empty, vm.ValuationRemediationWarning);

        // A fresh session over the same stored book warns again.
        var reopened = OpenThroughTheMenu("Dismissive Co");
        Assert.NotEqual(string.Empty, reopened.ValuationRemediationWarning);
    }

    /// <summary>
    /// 🔴 <b>THE MARKER SURVIVES THE ROUND TRIP THROUGH SQLITE.</b> The banner is only ever correct if
    /// <c>valuation_remediated_from</c> is actually written and read back; if the column were dropped on save,
    /// every affected book would fall silent on its second open and the warning would quietly disappear for
    /// exactly the operators who need it.
    /// </summary>
    [Fact]
    public void The_remediation_marker_survives_being_saved_and_reloaded()
    {
        SeedCompany("Persisted Co", remediated: true);

        var vm = OpenThroughTheMenu("Persisted Co");
        var item = vm.Company!.StockItems.Single(i => i.Name == "Widget");

        Assert.Equal(StockValuationMethod.LastSaleCost, item.ValuationRemediatedFrom);
        // And the costing method it was moved ONTO came back too — not the retired ordinal.
        Assert.Equal(StockValuationMethod.LastPurchaseCost, item.ValuationMethod);
    }
}
