using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Desktop.Views;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Path = System.IO.Path;
using Xunit;

namespace Apex.Desktop.Tests;

/// <summary>
/// 🔴 <b>CENSUS 3.13 — THE COMPANY RUNG OF THE GST RATE HIERARCHY.</b>
///
/// <para><b>The defect, measured.</b> <c>GstConfig.DefaultGst</c> has existed since schema v51,
/// <c>GstConfig.EnsureValid</c> has validated it since, and <c>GstService.Hierarchy</c> has READ it since
/// T0-4 slice S2a — the company is the fifth and last level of both published resolution orders. But
/// <c>grep -rn "DefaultGst" src/Apex.Desktop/</c> returned <b>zero</b>: the canonical importer was the only
/// writer in the entire product. The consequence was not a missing feature but a <b>divergence between two
/// kinds of book</b> — an imported book resolved rates from a rung a hand-keyed book could never populate, and
/// a single-rate business that set its rate where the vendor's documentation tells it to found the field
/// unreachable. This file is the reachability and the money half of closing that.</para>
///
/// <para>🔴 <b>WHY THE VISUAL TREE.</b> A test asserting <c>page.CompanyGst.Enabled = true</c> passes against a
/// build with no XAML field at all, leaving the operator a rung they cannot type into — the same defect class
/// this repository has filed three times, most sharply at row 13.6, where deleting an export radio left eight
/// view-model tests green while the format became unreachable by any user. So the fields are looked up BY NAME
/// in the realised tree and asserted EFFECTIVELY visible, and the money tests drive the real screen and then
/// read the domain back.</para>
///
/// <para>🔴 <b>THE SAFETY PROPERTY THIS FILE EXISTS TO LOCK.</b> This slice ADDS a rung; it does not re-order
/// one. A company default that outranked a ledger rate would be wrong money on every voucher in the book, so
/// <see cref="The_company_default_never_outranks_a_rate_declared_on_a_ledger"/> and its siblings pin the
/// precedence against the resolver's own published order strings. If an edit to the screen ever forces one of
/// those tests to change, the edit is wrong.</para>
/// </summary>
public sealed class CompanyGstRungTests
{
    private static readonly DateOnly FyStart = new(2024, 4, 1);
    private const string GstinMaharashtra = "27AAPFU0939F1ZV";

    // ================================================================= harness

    private static (MainWindow Window, MainWindowViewModel Vm, string TempDir) Open(string name)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ApexCoGstRung_" + Guid.NewGuid().ToString("N"));
        var vm = new MainWindowViewModel(new CompanyStorage(tempDir));
        var window = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
        window.Show();

        vm.NewCompanyName = name;
        vm.CreateCompany();
        Pump(window);
        return (window, vm, tempDir);
    }

    private static void Pump(MainWindow w)
    {
        w.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        w.UpdateLayout();
    }

    private static void Close(MainWindow window, string dir)
    {
        window.Close();
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static IEnumerable<Visual> Descendants(Visual v)
    {
        foreach (var c in v.GetVisualChildren())
        {
            yield return c;
            foreach (var g in Descendants(c)) yield return g;
        }
    }

    private static T? Named<T>(MainWindow w, string name) where T : Visual =>
        Descendants(w).OfType<T>().FirstOrDefault(x => x.Name == name);

    /// <summary>
    /// 🔴 <b>EFFECTIVE visibility, never the local flag.</b> Avalonia's <c>IsVisible</c> is the control's OWN
    /// property, so a field inside a collapsed container reports <c>true</c> while the operator cannot see it —
    /// which is exactly the failure this file exists to catch, so the helper must not re-introduce it.
    /// </summary>
    private static bool Shown(Visual? v) => v is not null && v.IsEffectivelyVisible;

    /// <summary>
    /// Opens F11 <b>through the keystroke an operator actually presses</b>, not through the view-model method.
    /// A route proved only by calling <c>ShowGstConfig()</c> is a route no keyboard reaches.
    /// </summary>
    private static GstConfigViewModel OpenF11(MainWindow w, MainWindowViewModel vm)
    {
        w.KeyPressQwerty(PhysicalKey.F11, RawInputModifiers.None);
        Pump(w);
        Assert.True(vm.GstConfig is not null,
            "F11 did not open the company-features page — the company GST rung has no door.");
        return vm.GstConfig!;
    }

    /// <summary>Fills the GST identity the screen requires before it will apply anything.</summary>
    private static void EnableGstOnScreen(MainWindow w, GstConfigViewModel page)
    {
        page.GstEnabled = true;
        page.Gstin = GstinMaharashtra;
        page.HomeState = page.HomeStates.First(s => s.Code == "27");
        Pump(w);
    }

    // ================================================================= reachability

    /// <summary>
    /// 🔴 <b>THE RUNG HAS A DOOR, AND THE KEYBOARD OPENS IT.</b> F11 → tick <i>Set/Alter GST Details</i> → all
    /// four of the vendor's fields realise and are effectively visible. <b>Red on today's main</b>, where
    /// <c>DefaultGst</c> has zero occurrences anywhere in <c>src/Apex.Desktop</c>: none of these five controls
    /// exists, so every <c>Named&lt;T&gt;</c> below returns <c>null</c> and every assertion fails.
    ///
    /// <para>The four fields are asserted HIDDEN before the tick as well. A block that renders its rate box
    /// whether or not the operator asked for a company default would invite a rate onto the last rung of the
    /// walk by accident — and the last rung is the one that answers when nothing else did.</para>
    /// </summary>
    [AvaloniaFact]
    public void The_company_gst_rung_is_reachable_from_the_keyboard_and_realises_the_vendors_four_fields()
    {
        var (w, vm, dir) = Open("Company Rung Reachability Co");
        try
        {
            var page = OpenF11(w, vm);
            EnableGstOnScreen(w, page);

            var toggle = Named<CheckBox>(w, "CompanyGstSetDetails");
            Assert.True(Shown(toggle),
                "The company GST rung has no 'Set/Alter GST Details' control on the F11 page — the last rung of " +
                "the rate walk is unreachable from the keyboard.");

            // Off by default, and the fields are genuinely collapsed rather than merely flagged invisible.
            Assert.False(page.CompanyGst.Enabled);
            Assert.False(Shown(Named<TextBox>(w, "CompanyGstHsn")));
            Assert.False(Shown(Named<TextBox>(w, "CompanyGstRate")));
            Assert.False(Shown(Named<ComboBox>(w, "CompanyGstTaxability")));
            Assert.False(Shown(Named<ComboBox>(w, "CompanyGstSupplyType")));

            page.CompanyGst.Enabled = true;
            Pump(w);

            Assert.True(Shown(Named<TextBox>(w, "CompanyGstHsn")), "HSN/SAC is not reachable.");
            Assert.True(Shown(Named<TextBox>(w, "CompanyGstRate")), "GST Rate % is not reachable.");
            Assert.True(Shown(Named<ComboBox>(w, "CompanyGstTaxability")), "Taxability is not reachable.");
            Assert.True(Shown(Named<ComboBox>(w, "CompanyGstSupplyType")), "Type of Supply is not reachable.");
        }
        finally { Close(w, dir); }
    }

    /// <summary>
    /// The block is inside the GST section, so it disappears with it. A company that has not enabled GST has no
    /// business being offered a GST rate default — and leaving the block standing would let an operator set a
    /// rung on a book whose rate walk never runs.
    /// </summary>
    [AvaloniaFact]
    public void The_company_gst_rung_is_not_offered_on_a_company_with_gst_switched_off()
    {
        var (w, vm, dir) = Open("No Gst Co");
        try
        {
            var page = OpenF11(w, vm);
            Assert.False(page.GstEnabled, "Fixture guard: a fresh company has GST off.");
            Pump(w);

            Assert.False(Shown(Named<CheckBox>(w, "CompanyGstSetDetails")),
                "The company GST rung is offered on a book that has not enabled GST.");
        }
        finally { Close(w, dir); }
    }

    // ================================================================= the writer

    /// <summary>
    /// 🔴 <b>THE SCREEN IS THE WRITER, AND WHAT IT WRITES SURVIVES A RE-ENTRY.</b> Apply moves
    /// <c>GstConfig.DefaultGst</c> on the live company, and re-opening F11 shows the saved values back.
    ///
    /// <para>The re-entry half is not padding: it is the specific omission that would leave an operator able to
    /// set a company default, re-open F11, see the block apparently OFF, and then <b>clear the rung for real</b>
    /// on their next Apply — a silent wrong-money regression on every line that rung was answering. The same
    /// omission is recorded in <c>LoadFromCompany</c>'s own comments for the tracking flags and the VAT block,
    /// so it is a defect this page has already shipped once.</para>
    /// </summary>
    [AvaloniaFact]
    public void Applying_the_screen_writes_the_company_rung_and_re_entry_shows_it_back()
    {
        var (w, vm, dir) = Open("Company Rung Writer Co");
        try
        {
            var page = OpenF11(w, vm);
            EnableGstOnScreen(w, page);

            page.CompanyGst.Enabled = true;
            page.CompanyGst.HsnSac = "998311";
            page.CompanyGst.RatePercentText = "18";
            page.CompanyGst.SelectedSupplyType =
                page.CompanyGst.SupplyTypeChoices.First(s => s.Display == "Services");

            Assert.True(page.Apply(), $"Apply refused the company rung: {page.Message}");
            Pump(w);

            var saved = vm.Company!.Gst!.DefaultGst;
            Assert.NotNull(saved);
            Assert.Equal("998311", saved!.HsnSac);
            Assert.Equal(1800, saved.RateBasisPoints);
            Assert.Equal(GstSupplyType.Services, saved.SupplyType);
            Assert.Equal(GstTaxability.Taxable, saved.Taxability);

            // Re-entry: ShowGstConfig builds a FRESH page, so this exercises LoadFromCompany for real.
            var reopened = OpenF11(w, vm);
            Assert.NotSame(page, reopened);
            Assert.True(reopened.CompanyGst.Enabled,
                "Re-opening F11 shows the company GST rung as OFF although the book carries one — the next " +
                "Apply would silently clear it.");
            Assert.Equal("998311", reopened.CompanyGst.HsnSac);
            Assert.Equal("18", reopened.CompanyGst.RatePercentText);
            Assert.Equal(GstSupplyType.Services, reopened.CompanyGst.SelectedSupplyType!.Value);
        }
        finally { Close(w, dir); }
    }

    /// <summary>
    /// Switching the block OFF and applying CLEARS the rung to <c>null</c> — an absent rung the walk skips, never
    /// an empty block it would stop at with nothing to say. <c>null</c> is also what every pre-v51 book reads as,
    /// so a company that never touches this block stays byte-identical (ER-13).
    /// </summary>
    [AvaloniaFact]
    public void Switching_the_block_off_clears_the_company_rung_to_absent()
    {
        var (w, vm, dir) = Open("Company Rung Clear Co");
        try
        {
            var page = OpenF11(w, vm);
            EnableGstOnScreen(w, page);
            page.CompanyGst.Enabled = true;
            page.CompanyGst.RatePercentText = "12";
            Assert.True(page.Apply(), page.Message);
            Assert.NotNull(vm.Company!.Gst!.DefaultGst);

            var reopened = OpenF11(w, vm);
            reopened.CompanyGst.Enabled = false;
            Assert.True(reopened.Apply(), reopened.Message);

            Assert.Null(vm.Company!.Gst!.DefaultGst);
        }
        finally { Close(w, dir); }
    }

    /// <summary>
    /// A malformed value is refused by the DOMAIN's own validator (<c>MasterGstDetails.EnsureValid</c>, the same
    /// rule the canonical importer applies), surfaced on the screen, and <b>nothing else on the page is written
    /// either</b>. The last assertion is the one that matters: <c>TryBuild</c> runs with the other
    /// pre-validations, BEFORE the first write to the shared <c>GstConfig</c>, so a bad rate cannot leave a
    /// half-applied GSTIN or Home State behind — and Home State decides intra- versus inter-state supply.
    /// </summary>
    [AvaloniaFact]
    public void A_malformed_company_rung_is_refused_and_leaves_the_rest_of_the_screen_unwritten()
    {
        var (w, vm, dir) = Open("Company Rung Bad Hsn Co");
        try
        {
            var page = OpenF11(w, vm);
            EnableGstOnScreen(w, page);
            page.CompanyGst.Enabled = true;
            page.CompanyGst.HsnSac = "12345";          // 5 digits — the vendor's rule is 4, 6 or 8

            Assert.False(page.Apply(), "A 5-digit HSN was accepted onto the company rung.");
            Assert.Contains("4, 6 or 8 digits", page.Message);

            Assert.Null(vm.Company!.Gst?.DefaultGst);
            Assert.False(vm.Company!.GstEnabled,
                "The refused Apply still enabled GST — a bad company rung wrote half the screen.");
        }
        finally { Close(w, dir); }
    }

    /// <summary>
    /// A negative rate is refused too, and the message names the field rather than leaking a parser exception.
    /// </summary>
    [AvaloniaFact]
    public void A_malformed_company_rate_is_refused_with_a_readable_message()
    {
        var (w, vm, dir) = Open("Company Rung Bad Rate Co");
        try
        {
            var page = OpenF11(w, vm);
            EnableGstOnScreen(w, page);
            page.CompanyGst.Enabled = true;
            page.CompanyGst.RatePercentText = "eighteen";

            Assert.False(page.Apply());
            Assert.Contains("GST rate", page.Message);
            Assert.Null(vm.Company!.Gst?.DefaultGst);
        }
        finally { Close(w, dir); }
    }

    /// <summary>
    /// 🔴 <b>CULTURE.</b> The gate runs on ubuntu and macos, where an ambient comma-decimal culture makes "12.5"
    /// unparseable and renders 12.5 as "12,5". The editor parses and formats with
    /// <see cref="System.Globalization.CultureInfo.InvariantCulture"/>; this pins it end to end through the
    /// screen, since a company rate that silently became 125% offshore is wrong money on every unanswered line.
    /// </summary>
    [AvaloniaFact]
    public void A_fractional_company_rate_round_trips_regardless_of_the_ambient_culture()
    {
        var (w, vm, dir) = Open("Company Rung Culture Co");
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture =
                new System.Globalization.CultureInfo("de-DE");   // comma decimal separator

            var page = OpenF11(w, vm);
            EnableGstOnScreen(w, page);
            page.CompanyGst.Enabled = true;
            page.CompanyGst.RatePercentText = "12.5";
            Assert.True(page.Apply(), page.Message);

            Assert.Equal(1250, vm.Company!.Gst!.DefaultGst!.RateBasisPoints);

            var reopened = OpenF11(w, vm);
            Assert.Equal("12.5", reopened.CompanyGst.RatePercentText);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
            Close(w, dir);
        }
    }

    // ================================================================= the money: precedence

    /// <summary>
    /// 🔴🔴 <b>THE SAFETY PROPERTY. A COMPANY DEFAULT NEVER OUTRANKS A RATE DECLARED ON A LEDGER.</b>
    ///
    /// <para>The company is the FIFTH and LAST level of both of the resolver's published order strings
    /// (<c>Ledger → Accounting Group → Stock Item → Stock Group → Company</c> and
    /// <c>Stock Item → Stock Group → Ledger → Accounting Group → Company</c>), and this slice adds a screen for
    /// that rung without touching either. Asserted under BOTH orders, because a regression that only showed on
    /// the non-default order would ship silently to every migrated book.</para>
    ///
    /// <para>This is the test the whole slice is gated on: a company rung that won here would put a single wrong
    /// rate on every voucher in the book, on paper that has already been filed.</para>
    /// </summary>
    [Theory]
    [InlineData(GstDetailSource.LedgerFirst)]
    [InlineData(GstDetailSource.StockItemFirst)]
    public void The_company_default_never_outranks_a_rate_declared_on_a_ledger(GstDetailSource source)
    {
        var company = GstCompanyWithDefault(source, companyRateBp: 2800);
        var (item, ledger) = ItemAndSalesLedger(company);

        // The SALES LEDGER declares 5%; the company default says 28%.
        ledger.SalesPurchaseGst = new StockItemGstDetails
        {
            Taxability = GstTaxability.Taxable,
            RateBasisPoints = 500,
        };

        var resolved = new GstService(company).ResolveRate(item, ledger, voucherDate: null);

        Assert.True(resolved.IsTaxable);
        Assert.Equal(500, resolved.RateBasisPoints);
    }

    /// <summary>
    /// Same property one rung up the other side: a rate on the STOCK ITEM also beats the company default, under
    /// both orders. Together with the ledger case this pins the company rung below every DETAILED rung.
    /// </summary>
    [Theory]
    [InlineData(GstDetailSource.LedgerFirst)]
    [InlineData(GstDetailSource.StockItemFirst)]
    public void The_company_default_never_outranks_a_rate_declared_on_a_stock_item(GstDetailSource source)
    {
        var company = GstCompanyWithDefault(source, companyRateBp: 2800);
        var (item, ledger) = ItemAndSalesLedger(company);

        item.Gst = new StockItemGstDetails
        {
            Taxability = GstTaxability.Taxable,
            RateBasisPoints = 1200,
        };

        var resolved = new GstService(company).ResolveRate(item, ledger, voucherDate: null);

        Assert.True(resolved.IsTaxable);
        Assert.Equal(1200, resolved.RateBasisPoints);
    }

    /// <summary>
    /// 🔴 <b>AND THE RUNG IS NOT INERT.</b> The three tests above would all pass against a build that ignored the
    /// company default entirely, so this one proves the other half: when NO other rung declares anything, the
    /// company default is what the line resolves to — under both orders.
    ///
    /// <para>Without a company default the same fixture resolves to the ER-5 <i>unresolved</i> sentinel (a hard
    /// block, not a silent zero), which is asserted here as the control. That contrast is the actual user-visible
    /// win of this slice: a single-rate business can now set its rate once and post.</para>
    /// </summary>
    [Theory]
    [InlineData(GstDetailSource.LedgerFirst)]
    [InlineData(GstDetailSource.StockItemFirst)]
    public void The_company_default_answers_when_no_other_rung_declares_anything(GstDetailSource source)
    {
        var withDefault = GstCompanyWithDefault(source, companyRateBp: 1800);
        var (item, ledger) = ItemAndSalesLedger(withDefault);

        var resolved = new GstService(withDefault).ResolveRate(item, ledger, voucherDate: null);
        Assert.False(GstService.IsUnresolved(resolved),
            "A company that set its rate on the company rung still cannot post a line.");
        Assert.True(resolved.IsTaxable);
        Assert.Equal(1800, resolved.RateBasisPoints);

        // The control: strip the rung and the same fixture must fail fast again, never resolve to a silent zero.
        var without = GstCompanyWithDefault(source, companyRateBp: null);
        var (bareItem, bareLedger) = ItemAndSalesLedger(without);
        Assert.True(GstService.IsUnresolved(new GstService(without).ResolveRate(bareItem, bareLedger, null)),
            "Removing the company rung stopped the ER-5 unresolved sentinel from firing.");
    }

    // ================================================================= fixtures

    private static Company GstCompanyWithDefault(GstDetailSource source, int? companyRateBp)
    {
        var c = CompanyFactory.CreateSeeded("Company Rung Fixture Co", FyStart);
        var config = new GstConfig
        {
            HomeStateCode = "27",
            Gstin = GstinMaharashtra,
            RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart,
            Periodicity = GstReturnPeriodicity.Monthly,
            SourceOfGstRate = source,
            SourceOfHsnSacDetails = source,
        };
        if (companyRateBp is { } bp)
        {
            config.DefaultGst = new MasterGstDetails
            {
                Taxability = GstTaxability.Taxable,
                RateBasisPoints = bp,
            };
        }
        new GstService(c).EnableGst(config);
        return c;
    }

    /// <summary>
    /// A stock item and a sales ledger that declare NO GST block of their own, under a stock group and an
    /// accounting group that declare none either — so all four rungs above the company are silent and the walk
    /// falls all the way through to whatever the test populates. Built the same way as
    /// <c>GstWinningBlockTests.BuildProbe</c>, so the two files cannot disagree about what an empty rung is.
    /// </summary>
    private static (StockItem Item, Apex.Ledger.Domain.Ledger Ledger) ItemAndSalesLedger(Company c)
    {
        var inventory = new InventoryService(c);
        var groups = new GroupService(c);

        var stockGroup = inventory.CreateStockGroup("Rung Probe SG");
        var nos = inventory.CreateSimpleUnit("Nos", "Numbers");
        var item = inventory.CreateStockItem("Rung Probe Widget", stockGroup.Id, nos.Id);

        var accountingGroup = groups.CreateGroup("Rung Probe Sales Group", c.FindGroupByName("Sales Accounts")!.Id);
        var ledger = new Apex.Ledger.Domain.Ledger(
            Guid.NewGuid(), "Rung Probe Sales", accountingGroup.Id, Money.Zero, openingIsDebit: false);
        c.AddLedger(ledger);

        return (item, ledger);
    }
}
