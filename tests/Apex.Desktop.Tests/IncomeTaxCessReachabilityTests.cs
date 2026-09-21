using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Desktop.Views;
using Xunit;

namespace Apex.Desktop.Tests;

/// <summary>
/// 🔴 <b>REACHABILITY FOR THE HEALTH &amp; EDUCATION CESS RATE</b> — limb (b) of the user's ruling: the rate must be
/// <b>per-company editable, reachable from the keyboard like any other company setting</b>.
///
/// <para><b>What these exist to stop.</b> This project has repeatedly shipped services with <b>zero production
/// callers</b> and graded them as progress. A dated, per-company cess rate that only a test can write would be
/// exactly that failure, and on the one path in this product that takes money off somebody's salary. So these tests
/// drive the screen, not the domain: they reach the field through the <b>F11 Features</b> route the operator uses,
/// commit it with the button that is actually on the page, reload from SQLite, and assert the rendered control is
/// really in the visual tree.</para>
///
/// <para><b>Why the field lives inside the existing §192 block rather than on a new screen.</b> It is committed by
/// the same "Apply Salary TDS" accept, so it inherits that block's keyboard route and Tab order instead of inventing
/// a second one — the settled keyboard-first contract for this product.</para>
/// </summary>
public sealed class IncomeTaxCessReachabilityTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public IncomeTaxCessReachabilityTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexCessReach_" + Guid.NewGuid().ToString("N"));
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
    }

    // ================================================================= limb (b): editable, and it persists

    /// <summary>
    /// 🔴 <b>THE HEADLINE.</b> An operator opens F11, types a cess rate and an effective date, accepts — and the
    /// book charges that rate from that date, still charging it after a reload. Fails on today's main, where there
    /// is no field, no storage and no way to change the rate at all.
    /// </summary>
    [Fact]
    public void An_operator_can_set_a_dated_cess_rate_from_F11_and_it_persists()
    {
        var vm = NewCompany("Cess F11 Co");
        vm.ShowGstConfig();
        var cfg = Assert.IsType<GstConfigViewModel>(vm.GstConfig);

        cfg.SalaryTdsEnabled = true;
        cfg.SalaryTdsCessPercentText = "5";
        cfg.SalaryTdsCessEffectiveFromText = "01-10-2025";
        Assert.True(cfg.ApplySalaryTds());

        var reloaded = Reload("Cess F11 Co");
        var stored = Assert.Single(reloaded.IncomeTaxCessRates);
        Assert.Equal(new DateOnly(2025, 10, 1), stored.EffectiveFrom);
        Assert.Equal(500, stored.RateBasisPoints);

        // 🔴 And the BEHAVIOUR, not just the row: the dated window really confines the rate.
        Assert.Equal(0.04m, SalaryTaxRates.ForCompanyPeriod(reloaded, new DateOnly(2025, 9, 30)).CessRate);
        Assert.Equal(0.05m, SalaryTaxRates.ForCompanyPeriod(reloaded, new DateOnly(2025, 10, 31)).CessRate);

        // 🔴 The page built over the RELOADED company reads the rate back. This is the assertion that catches a
        // screen which writes but does not LOAD — the omission that lets an operator set a value, return, see
        // nothing, and believe it was never saved.
        var reopened = new GstConfigViewModel(reloaded, _storage, () => { });
        var row = Assert.Single(reopened.SalaryTdsCessRates);
        Assert.Equal("01-10-2025", row.EffectiveFromText);
        Assert.Equal("5%", row.RateText);
        Assert.True(reopened.HasSalaryTdsCessRates);
    }

    /// <summary>Blank effective-from means "from the start of this financial year" — the ordinary case needs no
    /// typing, and it must not silently become today's date or 01-01.</summary>
    [Fact]
    public void A_blank_effective_from_means_the_start_of_the_current_financial_year()
    {
        var vm = NewCompany("Cess Default Date Co");
        var fyStart = vm.Company!.FinancialYearStart;
        vm.ShowGstConfig();
        var cfg = vm.GstConfig!;

        cfg.SalaryTdsEnabled = true;
        cfg.SalaryTdsCessPercentText = "3";
        cfg.SalaryTdsCessEffectiveFromText = string.Empty;
        Assert.True(cfg.ApplySalaryTds());

        var stored = Assert.Single(Reload("Cess Default Date Co").IncomeTaxCessRates);
        Assert.Equal(fyStart, stored.EffectiveFrom);
        Assert.Equal(300, stored.RateBasisPoints);
    }

    // ================================================================= limb (c): nothing changes by default

    /// <summary>
    /// 🔴 <b>LIMB (c), THROUGH THE SCREEN.</b> An operator who opens F11, switches §192 on and accepts <b>without
    /// touching the cess fields</b> must leave the rate exactly as it was. A blank rate box is "leave it alone", and
    /// is never read as 0% — which would zero a statutory deduction on a live payslip.
    /// </summary>
    [Fact]
    public void Accepting_the_page_without_touching_the_cess_fields_writes_no_rate_and_changes_nothing()
    {
        var vm = NewCompany("Cess Untouched Co");
        vm.ShowGstConfig();
        var cfg = vm.GstConfig!;

        cfg.SalaryTdsEnabled = true;
        Assert.True(cfg.ApplySalaryTds());

        var reloaded = Reload("Cess Untouched Co");
        Assert.Empty(reloaded.IncomeTaxCessRates);

        var rates = SalaryTaxRates.ForCompanyPeriod(reloaded, new DateOnly(2026, 3, 31));
        Assert.Equal(0.04m, rates.CessRate);           // the statutory rate, not 0%
        Assert.False(rates.CessRateIsCompanyOverride);
        Assert.Equal(new Money(97_500m), SalaryIncomeTax.ComputeAnnual(14_25_000m, TaxRegime.New, rates).AnnualTax);
    }

    /// <summary>The status line tells the operator which rate is in force and on whose authority BEFORE they change
    /// anything — a bare percent box with no read-back is how a statutory rate gets overwritten by accident.</summary>
    [Fact]
    public void The_screen_states_which_rate_is_in_force_and_whether_the_company_set_it()
    {
        var vm = NewCompany("Cess Status Co");
        vm.ShowGstConfig();
        var cfg = vm.GstConfig!;

        Assert.Contains("4%", cfg.SalaryTdsCessStatusText, StringComparison.Ordinal);
        Assert.Contains("statutory", cfg.SalaryTdsCessStatusText, StringComparison.OrdinalIgnoreCase);
        Assert.False(cfg.HasSalaryTdsCessRates);

        cfg.SalaryTdsEnabled = true;
        cfg.SalaryTdsCessPercentText = "5";
        Assert.True(cfg.ApplySalaryTds());

        Assert.Contains("5%", cfg.SalaryTdsCessStatusText, StringComparison.Ordinal);
        Assert.Contains("set by this company", cfg.SalaryTdsCessStatusText, StringComparison.Ordinal);
        Assert.True(cfg.HasSalaryTdsCessRates);
    }

    // ================================================================= bad input is refused, not swallowed

    [Theory]
    [InlineData("four per cent", "01-04-2025")]
    [InlineData("-1", "01-04-2025")]
    [InlineData("101", "01-04-2025")]
    [InlineData("4", "2025-04-01")]
    [InlineData("4", "31-02-2025")]
    public void A_bad_cess_entry_is_reported_and_nothing_is_saved(string percent, string from)
    {
        var name = "Bad Cess Co " + Guid.NewGuid().ToString("N")[..8];
        var vm = NewCompany(name);
        vm.ShowGstConfig();
        var cfg = vm.GstConfig!;

        cfg.SalaryTdsEnabled = true;
        cfg.SalaryTdsCessPercentText = percent;
        cfg.SalaryTdsCessEffectiveFromText = from;

        Assert.False(cfg.ApplySalaryTds());
        Assert.NotNull(cfg.SalaryTdsMessage);
        // 🔴 Nothing was half-applied to the shared aggregate, and nothing reached the store.
        Assert.Empty(vm.Company!.IncomeTaxCessRates);
        Assert.Empty(Reload(name).IncomeTaxCessRates);
    }

    // ================================================================= the realised visual tree

    /// <summary>
    /// 🔴 <b>THE FIELDS ARE REALLY ON THE PAGE.</b> A view-model property nobody bound to is the same defect as a
    /// service nobody calls. This walks the rendered F11 screen and asserts the two cess text boxes and the status
    /// line exist, are effectively visible, are non-degenerate in size, and are FOCUSABLE — the last being what
    /// makes them reachable by Tab, which is the whole of "reachable from the keyboard".
    /// </summary>
    [AvaloniaFact]
    public void The_cess_rate_and_effective_from_boxes_are_rendered_visible_and_focusable()
    {
        var vm = NewCompany("Cess Visual Co");
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 720 };
        window.Show();
        Pump(window);

        vm.ShowGstConfig();
        Pump(window);

        // The §192 block only renders once Payroll Statutory + the salary-TDS switch are on.
        var cfg = vm.GstConfig!;
        cfg.PayrollStatutoryEnabled = true;
        cfg.SalaryTdsEnabled = true;
        Pump(window);

        var boxes = Descendants(window)
            .OfType<TextBox>()
            .Where(t => t.IsEffectivelyVisible
                        && t.Watermark is not null
                        && (t.Watermark.Contains("leave blank to keep current", StringComparison.Ordinal)
                            || t.Watermark.Contains("blank = FY start", StringComparison.Ordinal)))
            .ToList();

        Assert.True(boxes.Count == 2,
            $"Expected the cess rate box and its effective-from box on the rendered F11 page; found {boxes.Count}. "
            + "A cess rate nobody can type is the zero-production-caller failure this suite exists to stop.");

        foreach (var b in boxes)
        {
            Assert.True(b.Bounds.Width > 0 && b.Bounds.Height > 0,
                "A cess box rendered with a degenerate size, so nobody can click or read it.");
            Assert.True(b.Focusable, "A cess box is not focusable, so Tab cannot reach it from the keyboard.");
            Assert.False(b.IsReadOnly, "A cess box is read-only, so the rate is not editable after all.");
        }

        // And the read-back sentence is on screen, wrapped, so the operator sees the rate in force before typing.
        var status = Descendants(window)
            .OfType<TextBlock>()
            .FirstOrDefault(t => t.IsEffectivelyVisible
                                 && t.Text is not null
                                 && t.Text.Contains("Currently charging", StringComparison.Ordinal));
        Assert.True(status is not null,
            "The cess read-back line did not reach the visual tree, so an operator meets a bare percent box with "
            + "nothing telling them what the book charges today.");
        Assert.Equal(Avalonia.Media.TextWrapping.Wrap, status!.TextWrapping);
        Assert.True(status.Bounds.Width > 0 && status.Bounds.Height > 0);
    }

    // ================================================================= helpers

    private MainWindowViewModel NewCompany(string name)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = name;
        vm.CreateCompany();
        Assert.Equal(Screen.Gateway, vm.CurrentScreen);
        return vm;
    }

    private Company Reload(string companyName)
    {
        var entry = _storage.ListCompanies().Single(e => e.Name == companyName);
        return _storage.Load(entry);
    }

    private static void Pump(MainWindow w)
    {
        w.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        w.UpdateLayout();
    }

    private static IEnumerable<Visual> Descendants(Visual v)
    {
        foreach (var c in v.GetVisualChildren())
        {
            yield return c;
            foreach (var g in Descendants(c)) yield return g;
        }
    }
}
