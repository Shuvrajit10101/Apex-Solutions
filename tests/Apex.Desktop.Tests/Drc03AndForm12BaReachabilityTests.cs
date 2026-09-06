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
using Xunit;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.Tests;

/// <summary>
/// <b>Census rows 6.20 (DRC-03) and 6.42 (Form 12BA) — the LAST mile, driven by the keyboard alone.</b>
///
/// <para><b>🔴 WHY THIS FILE ASSERTS ON KEYS AND ON THE REALISED VISUAL TREE, AND NOT ON A VIEW-MODEL FLAG.</b>
/// Row 6.20 is the project's canonical <i>"complete verb nobody can reach"</i>: <see cref="GstDepositService.PostDrc03"/>
/// has been correct, guarded and unit-tested for phases, and until this slice <b>no user could invoke it</b> — the
/// census graded the row ABSENT with the engine sitting right there. A test that called <c>vm.OpenDrc03Payment()</c>
/// and asserted <c>vm.Drc03Payment is not null</c> would have been just as green on the day the row was unreachable,
/// because the opener is not the thing that was missing: the <b>menu row</b>, the <b>dispatch case</b> and the
/// <b>rendered form</b> were. So every reachability test below starts on the Gateway and presses real keys through
/// <see cref="MainWindow"/>'s own handler — ArrowDown to the row, Enter to open it — then walks the realised visual
/// tree for controls a person could actually see and use. Delete the menu row, the dispatch case, or the
/// <c>DataTemplate</c>, and these redden; that is the whole point.</para>
///
/// <para>Headless-safe: visual-tree, text and enabled-state inspection only. No Skia, no rendered frame. Every
/// figure is culture-invariant and every path is built with <see cref="Path.Combine"/>, so the file behaves the
/// same on the ubuntu and macos CI legs (an assertion that compared two empty strings on Linux has escaped this
/// gate before).</para>
/// </summary>
public sealed class Drc03AndForm12BaReachabilityTests
{
    private const string GstinMaharashtra = "27AAPFU0939F1ZV";
    private static readonly DateOnly FyStart = new(2024, 4, 1);

    // ---------------------------------------------------------------- scaffolding

    private static void Pump(Window w)
    {
        Dispatcher.UIThread.RunJobs();
        w.Measure(new Size(1280, 800));
        w.Arrange(new Rect(0, 0, 1280, 800));
        Dispatcher.UIThread.RunJobs();
    }

    private static IEnumerable<Visual> Descendants(Visual v)
    {
        foreach (var c in v.GetVisualChildren())
        {
            yield return c;
            foreach (var g in Descendants(c)) yield return g;
        }
    }

    /// <summary>Every realised, visible, non-degenerate <see cref="TextBlock"/> string on screen.</summary>
    private static List<TextBlock> VisibleTextBlocks(Window w) =>
        Descendants(w)
            .OfType<TextBlock>()
            .Where(t => t.IsEffectivelyVisible && t.Bounds.Width > 0 && t.Bounds.Height > 0)
            .ToList();

    private static bool ScreenShows(Window w, string fragment) =>
        VisibleTextBlocks(w).Any(t => (t.Text ?? string.Empty).Contains(fragment, StringComparison.Ordinal));

    /// <summary>
    /// Walks the Miller cascade <b>by keyboard only</b>: ArrowDown until the highlighted item is
    /// <paramref name="label"/>, then Enter. Fails loudly — naming what the column actually offered — rather than
    /// looping forever, because "the row is not in the menu" is precisely the defect being guarded.
    /// </summary>
    private static void KeyboardInto(MainWindow w, MainWindowViewModel vm, string label)
    {
        var offered = vm.Menu.Where(i => i.IsSelectable).Select(i => i.Label).ToList();
        Assert.True(offered.Contains(label),
            $"The keyboard cascade never offered '{label}'. This column offers: {string.Join(" | ", offered)}. " +
            "A screen with no menu row is unreachable and does not move a census row.");

        // At most one full lap of the column; the highlight wraps, so a lap is enough to reach any row.
        for (var i = 0; i <= vm.Menu.Count; i++)
        {
            if (vm.SelectedIndex >= 0
                && vm.SelectedIndex < vm.Menu.Count
                && vm.Menu[vm.SelectedIndex].Label == label)
            {
                w.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
                Pump(w);
                return;
            }
            w.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
            Pump(w);
        }

        Assert.Fail($"ArrowDown never landed the highlight on '{label}' within one lap of the column.");
    }

    private static (MainWindow Window, MainWindowViewModel Vm, string Dir) NewWindow(string tag)
    {
        var dir = Path.Combine(Path.GetTempPath(), "ApexD3_" + tag + "_" + Guid.NewGuid().ToString("N"));
        var vm = new MainWindowViewModel(new CompanyStorage(dir));
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 800 };
        window.Show();
        return (window, vm, dir);
    }

    private static void Cleanup(MainWindow w, string dir)
    {
        w.Close();
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>A saved Regular-GST company sitting on the Gateway, with a Bank ledger a DRC-03 can be funded from.</summary>
    private static void SeedRegularGst(MainWindowViewModel vm, string name)
    {
        vm.NewCompanyName = name;
        vm.CreateCompany();
        var c = vm.Company!;
        c.FinancialYearStart = FyStart;
        c.BooksBeginFrom = FyStart;

        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = "27",
            Gstin = GstinMaharashtra,
            RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart,
            Periodicity = GstReturnPeriodicity.Monthly,
        });

        var bankGroup = c.FindGroupByName("Bank Accounts")!;
        c.AddLedger(new DomainLedger(Guid.NewGuid(), "HDFC Current", bankGroup.Id,
            Money.FromRupees(500_000m), openingIsDebit: true));

        vm.ShowGateway();
    }

    // ================================================================ ROW 6.20 — DRC-03

    /// <summary>
    /// 🔴 THE ROW-6.20 TEST. Gateway → Statutory Reports → GST Actions → DRC-03 Voluntary Payment, by ArrowDown and
    /// Enter alone, ending on a form the operator can actually see. Asserts the realised tree, not the opener.
    /// </summary>
    [AvaloniaFact]
    public void Drc03_voluntary_payment_is_reachable_from_the_gateway_by_keyboard_alone()
    {
        var (w, vm, dir) = NewWindow("Drc03Reach");
        try
        {
            SeedRegularGst(vm, "DRC03 Reach Co");
            Pump(w);

            KeyboardInto(w, vm, "Statutory Reports");
            KeyboardInto(w, vm, "GST Actions");
            KeyboardInto(w, vm, "DRC-03 Voluntary Payment");

            Assert.Equal(Screen.Drc03Payment, vm.CurrentScreen);
            Assert.NotNull(vm.Drc03Payment);

            // The form is REALISED — the operator sees the instrument named and the portal's own cause picker.
            Assert.True(ScreenShows(w, "DRC-03"),
                "The DRC-03 screen opened but nothing on it is visible — the DataTemplate did not realise.");
            Assert.True(ScreenShows(w, "Rule 142(2)"),
                "The DRC-03 form does not name the rule it files under anywhere the operator can read it.");

            // The cause picker is a realised, enabled control bound to the portal's twelve verbatim causes.
            var causeBox = Descendants(w)
                .OfType<ComboBox>()
                .FirstOrDefault(cb => cb.IsEffectivelyVisible
                                      && cb.ItemsSource is IEnumerable<Drc03CauseOption>);
            Assert.True(causeBox is not null,
                "No realised, visible cause picker is bound to the portal's Cause-of-Payment list. The cause is " +
                "written verbatim into GstDrc03.Cause and read back on every reconciliation — a screen that cannot " +
                "pick one cannot file a DRC-03.");
            Assert.True(causeBox!.IsEffectivelyEnabled, "The cause picker realised but is disabled.");

            var causes = ((IEnumerable<Drc03CauseOption>)causeBox.ItemsSource!).ToList();
            Assert.Equal(12, causes.Count);
            Assert.Equal("Annual return", causes[0].Text);
            Assert.Equal("Others", causes[10].Text);
            Assert.Equal("Order", causes[11].Text);

            // The declared divergence is on the FACE of the screen, not in a code comment.
            Assert.True(ScreenShows(w, "Penalty, Fee and Others"),
                "The screen silently drops three portal fields this book cannot hold (Penalty / Fee / Others, the " +
                "Section Number and the Communication Reference Number) without telling the operator. A form that " +
                "quietly omits two-fifths of the statutory grid reads as complete and is not.");

            // Brand rule: the shipped product never says "Tally".
            Assert.DoesNotContain(VisibleTextBlocks(w),
                t => (t.Text ?? string.Empty).Contains("Tally", StringComparison.OrdinalIgnoreCase));
        }
        finally { Cleanup(w, dir); }
    }

    /// <summary>
    /// 🔴 THE ROW-6.20 ENGINE TEST. The screen is not a mock: real <b>Ctrl+A</b> on the real window files a real
    /// DRC-03 through <see cref="GstDepositService.PostDrc03"/>. Asserted on the ENGINE's own effect — the stored
    /// record and the posted voucher — never on the screen's success message, which a stub could also print.
    /// </summary>
    [AvaloniaFact]
    public void Ctrl_A_on_the_drc03_screen_files_the_payment_through_the_engine()
    {
        var (w, vm, dir) = NewWindow("Drc03Post");
        try
        {
            SeedRegularGst(vm, "DRC03 Post Co");
            Pump(w);

            KeyboardInto(w, vm, "Statutory Reports");
            KeyboardInto(w, vm, "GST Actions");
            KeyboardInto(w, vm, "DRC-03 Voluntary Payment");

            var page = vm.Drc03Payment!;
            var company = vm.Company!;
            Assert.Empty(company.GstDrc03s);          // opening the screen posts NOTHING

            page.SelectedCause = page.Causes.Single(c => c.Ordinal == 10);   // "…(Voluntary)"
            page.Period = "2024-25";
            page.Method = GstDepositService.PaymentMethod.Bank;
            page.SelectedBank = page.BankOptions.Single(b => b.Name == "HDFC Current");
            page.CgstText = "1000";
            page.SgstText = "1000";
            page.ReasonsText = "Short payment found on internal review";
            page.DemandRefText = "DRC-03A/2024/0007";

            var before = company.Vouchers.Count;
            w.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
            Pump(w);

            Assert.True(page.LastActionSucceeded, $"Ctrl+A did not file the DRC-03: {page.Message}");

            var rec = Assert.Single(company.GstDrc03s);
            Assert.Equal("Before issuance of SCN/Statement (Voluntary)", rec.Cause);
            Assert.Equal("2024-25", rec.Period);
            Assert.Equal(100_000L, rec.CgstPaisa);
            Assert.Equal(100_000L, rec.SgstPaisa);
            Assert.Equal(200_000L, rec.TotalTaxPaisa);
            Assert.Equal("DRC-03A/2024/0007", rec.Drc03aDemandRef);

            // The voucher really posted, and the portal's "Reasons if any" survived into it (there is no column
            // for it on GstDrc03, so the narration is the only place it can be retrieved from without a schema change).
            Assert.Equal(before + 1, company.Vouchers.Count);
            Assert.NotNull(rec.VoucherId);
            var voucher = company.Vouchers.Single(v => v.Id == rec.VoucherId!.Value);
            Assert.Contains("Short payment found on internal review", voucher.Narration ?? string.Empty);
            Assert.Equal(Money.Zero, voucher.Lines.Aggregate(Money.Zero,
                (a, l) => l.Side == DrCr.Debit ? a + l.Amount : a - l.Amount));   // the voucher balances

            // The filed history is re-projected onto the screen the operator is still looking at.
            Assert.Single(page.Filed);
            Assert.Equal("2024-25", page.Filed[0].Period);
        }
        finally { Cleanup(w, dir); }
    }

    /// <summary>
    /// 🔴 THE ROW-6.20 GUARD TEST. Section 49(4) / rule 86(2) let the electronic CREDIT ledger settle tax only; the
    /// portal states it as <i>"Interest and penalty amount shall be paid out of cash ledger only."</i> The engine has
    /// always thrown on it. A screen that accepts a number and then throws is worse than one that says why — so the
    /// realised interest box must be <b>disabled</b>, with the portal's sentence visible beside it.
    /// </summary>
    [AvaloniaFact]
    public void Credit_funded_drc03_disables_the_interest_box_and_shows_the_portal_cash_only_rule()
    {
        var (w, vm, dir) = NewWindow("Drc03Credit");
        try
        {
            SeedRegularGst(vm, "DRC03 Credit Co");
            Pump(w);

            KeyboardInto(w, vm, "Statutory Reports");
            KeyboardInto(w, vm, "GST Actions");
            KeyboardInto(w, vm, "DRC-03 Voluntary Payment");

            var page = vm.Drc03Payment!;

            // Identify the interest box by its ACTUAL BINDING, not by position or name: push a sentinel through
            // InterestText and take the realised TextBox that shows it. A box bound to something else cannot be
            // mistaken for this one, which is what makes the disabled-state assertion below mean anything.
            page.Method = GstDepositService.PaymentMethod.Cash;
            page.InterestText = "4242.42";
            Pump(w);

            var box = Descendants(w)
                .OfType<TextBox>()
                .FirstOrDefault(tb => tb.IsEffectivelyVisible && tb.Text == "4242.42");
            Assert.True(box is not null,
                "No realised, visible TextBox is bound to the screen's InterestText. Section 50 interest cannot be " +
                "entered at all, so the cash-only rule below guards nothing.");
            Assert.True(box!.IsEffectivelyEnabled,
                "The interest box is disabled even under a cash-funded DRC-03, where section 50 interest is payable.");

            // Under credit it must be shut, and the reason must be on screen in the portal's own words.
            page.Method = GstDepositService.PaymentMethod.Credit;
            Pump(w);

            // The SAME control, still attached to the tree — so this is a state change, not a swapped-in stub.
            Assert.True(Descendants(w).Any(v => ReferenceEquals(v, box)),
                "The interest box left the visual tree when the payment method changed; the assertion below would " +
                "be inspecting a detached control and would pass vacuously.");
            Assert.False(box.IsEffectivelyEnabled,
                "A credit-funded DRC-03 still lets the operator type a section 50 interest figure into a live box. " +
                "Credit settles TAX ONLY (s.49(4) / rule 86(2)); the engine throws on it. Typing a number that will " +
                "be refused — or worse, silently dropped — is the failure this guard exists to prevent.");

            Assert.True(ScreenShows(w, Drc03PaymentViewModel.CashOnlyRule),
                "The interest box is disabled but the screen never says why. The portal's own sentence — " +
                $"\"{Drc03PaymentViewModel.CashOnlyRule}\" — must be visible beside the box it explains.");
        }
        finally { Cleanup(w, dir); }
    }

    /// <summary>
    /// 🔴 <b>THE ENUMERATION LOCK — all twelve causes, verbatim, in the portal's own order.</b> The reachability test
    /// above spot-checks causes 1, 11 and 12 and the count; that leaves the eight in the middle free to be reworded
    /// or re-ordered with the whole suite staying green. <b>Measured, not assumed:</b> rewording cause 5 to
    /// "Mismatch between GSTR-2B and GSTR-3B" (dropping the two "FORM " prefixes) left every other test in this file
    /// passing.
    ///
    /// <para>That is not cosmetic. The picked string is written verbatim into <see cref="GstDrc03.Cause"/>, a free
    /// text column, and is what every later reconciliation matches on — so a silent re-wording orphans the records
    /// already filed under the old spelling. And this project's standing rule for a statutory taxonomy is
    /// <i>clone, never invent</i>: the list is GSTN's, from
    /// <c>tutorial.gst.gov.in/userguide/demandsandrecovery/Manual_GST_FORM_DRC-03.htm</c>, and this test is what
    /// makes "we did not invent it" checkable instead of merely asserted in a comment.</para>
    ///
    /// <para>Plain <c>[Fact]</c> on purpose — it inspects a static table, needs no window, and therefore cannot be
    /// made to pass vacuously by a headless-render quirk on a CI runner with no display.</para>
    /// </summary>
    [Fact]
    public void The_twelve_drc03_causes_are_the_portals_own_words_in_the_portals_own_order()
    {
        var expected = new[]
        {
            "Annual return",
            "Audit",
            "Investigation/Enforcement",
            "Intimation of tax ascertained through FORM GST DRC-01A",
            "Mismatch between FORM GSTR-2B and FORM GSTR-3B",
            "Mismatch between FORM GSTR-1 and FORM GSTR-3B",
            "Reconciliation statement",
            "After issuance of SCN/Statement but before issuance of the order",
            "Scrutiny",
            "Before issuance of SCN/Statement (Voluntary)",
            "Others",
            "Order",
        };

        Assert.Equal(expected, Drc03CauseOption.All.Select(c => c.Text).ToArray());

        // The ordinal is the portal's own numbering and is what the conditional fields key off (the Communication
        // Reference Number is portal-conditional on 4 / 8 / 12, and "Please specify" on 11). A gap or a renumber
        // would silently re-point those conditions at the wrong cause.
        Assert.Equal(Enumerable.Range(1, 12).ToArray(), Drc03CauseOption.All.Select(c => c.Ordinal).ToArray());
    }

    // ================================================================ ROW 6.42 — Form 12BA

    /// <summary>A saved salary-TDS company with one high-earning employee and twelve posted payroll runs.</summary>
    private static void SeedSalaryTds(MainWindowViewModel vm, string name, decimal monthlyBasic)
    {
        vm.NewCompanyName = name;
        vm.CreateCompany();
        var c = vm.Company!;
        c.FinancialYearStart = FyStart;
        c.BooksBeginFrom = FyStart;
        c.PayrollEnabled = true;
        c.PayrollStatutoryEnabled = true;
        c.SalaryTdsEnabled = true;

        var ph = new PayHeadService(c);
        var basic = ph.CreatePayHead("Basic", PayHeadType.Earnings, PayHeadCalculationType.FlatRate,
            underGroupId: c.FindGroupByName("Indirect Expenses")!.Id);
        var tds = ph.CreatePayHead("TDS on Salary", PayHeadType.EmployeesStatutoryDeductions,
            PayHeadCalculationType.AsUserDefinedValue, underGroupId: c.FindGroupByName("Current Liabilities")!.Id,
            incomeTaxComponent: IncomeTaxComponent.TaxDeductedAtSource);

        var payroll = new PayrollService(c);
        var groupId = payroll.CreateEmployeeGroup("Staff").Id;
        var e = payroll.CreateEmployee("Anita Rao", groupId);
        var emp = c.FindEmployee(e.Id)!;
        emp.ApplicableTaxRegime = TaxRegime.New;
        emp.Pan = "ABCDE1234F";

        new SalaryStructureService(c).DefineForEmployee(e.Id, c.FinancialYearStart, new[]
        {
            new SalaryStructureLine(basic.Id, 0, new Money(monthlyBasic)),
            new SalaryStructureLine(tds.Id, 1),
        });

        var svc = new PayrollVoucherService(c);
        var d = c.FinancialYearStart;
        for (var i = 0; i < 12; i++)
        {
            svc.Post(d, new DateOnly(d.Year, d.Month, DateTime.DaysInMonth(d.Year, d.Month)), new[] { e.Id });
            d = d.AddMonths(1);
        }

        vm.ShowGateway();
    }

    /// <summary>
    /// 🔴 THE ROW-6.42 TEST. Gateway → Statutory Reports → Payroll → Form 12BA, by keyboard alone — and the page
    /// that opens must render the <b>prescribed column shape</b> and the <b>honest empty state</b>. The empty state
    /// is the fidelity content of this row: this book maintains no section 17(2) perquisite records at all, so the
    /// only truthful rendering is a stated absence. A column of zeros would certify that no perquisite was provided,
    /// which the book cannot know — so this test also proves the screen does NOT print one.
    /// </summary>
    [AvaloniaFact]
    public void Form_12ba_is_reachable_from_the_gateway_by_keyboard_alone_and_states_its_empty_shape()
    {
        var (w, vm, dir) = NewWindow("F12BAReach");
        try
        {
            SeedSalaryTds(vm, "Form12BA Reach Co", 1_25_000m);
            Pump(w);

            KeyboardInto(w, vm, "Statutory Reports");
            KeyboardInto(w, vm, "Payroll");
            KeyboardInto(w, vm, "Form 12BA");

            Assert.Equal(Screen.Form12Ba, vm.CurrentScreen);
            var page = vm.Form12Ba;
            Assert.True(page is not null, "The Form 12BA menu row opened no page.");

            // FY 2024-25 is still under the 1961 Act, so the FY-gated title reads "Form 12BA", not "Form 123".
            Assert.True(ScreenShows(w, "Form 12BA"),
                "The Form 12BA page realised without its own form number on screen.");

            // The prescribed column shape is rendered — all four captions, visible.
            foreach (var caption in Form12BaViewModel.PrescribedColumns)
                Assert.True(ScreenShows(w, caption),
                    $"The prescribed perquisite-table column '{caption}' is not rendered anywhere on the page. " +
                    "The shape of the statement is what makes the empty state readable as an absence rather than " +
                    "as a missing screen.");

            // The honest empty state is on the face of the page.
            Assert.True(page!.IsEmpty);
            Assert.True(ScreenShows(w, "Perquisite capture under section 17(2) is not maintained in this book"),
                "The page renders the prescribed table but never tells the operator WHY every row is empty. A blank " +
                "statutory table that does not explain itself reads as a nil return — and a nil return is a claim " +
                "this book has no basis to make.");

            Assert.DoesNotContain(VisibleTextBlocks(w),
                t => (t.Text ?? string.Empty).Contains("Tally", StringComparison.OrdinalIgnoreCase));
        }
        finally { Cleanup(w, dir); }
    }

    /// <summary>
    /// The one genuinely COMPUTED thing on the 12BA screen: the rule 26A(2)(b) applicability test. Gross salary is a
    /// figure this book really does hold, so the verdict is real — and it is strictly greater-than, so a salary of
    /// exactly the threshold does not cross it.
    /// </summary>
    [AvaloniaTheory]
    [InlineData(1_25_000, true)]    // ₹15,00,000 a year — far over the threshold
    [InlineData(10_000, false)]     // ₹1,20,000 a year — under it
    // 🔴 THE BOUNDARY, AND IT IS THE ONLY CASE THAT LOCKS THE COMPARISON OPERATOR. Basic is the sole earning in
    // this fixture, so 12 × ₹12,500 is gross salary of EXACTLY ₹1,50,000. Rule 26A(2)(b) requires the statement
    // where salary "exceeds" the threshold, so exactly-at-it is NOT due. Without this row the two rows above pass
    // identically whether the code says `>` or `>=` — measured: mutating ThresholdRupees comparison to `>=` left
    // the whole suite green until this case existed.
    [InlineData(12_500, false)]     // ₹1,50,000 a year — EXACTLY the threshold; "exceeds" is strict
    public void Form_12ba_reports_the_rule_26A_threshold_verdict_from_real_gross_salary(
        int monthlyBasic, bool expectedDue)
    {
        var (w, vm, dir) = NewWindow("F12BAThresh" + monthlyBasic);
        try
        {
            SeedSalaryTds(vm, $"Form12BA Threshold Co {monthlyBasic}", monthlyBasic);
            Pump(w);

            vm.OpenForm12Ba();
            Pump(w);
            var page = vm.Form12Ba!;

            Assert.NotEmpty(page.Employees);
            Assert.Equal(expectedDue, page.IsFormDue);
            Assert.Contains(expectedDue ? "is required for this employee"
                                        : "is not required for this employee",
                            page.ThresholdText);

            // The verdict is on screen, not just in the view model.
            Assert.True(ScreenShows(w, "rule 26A(2)(b)"),
                "The threshold verdict is computed but never rendered.");
        }
        finally { Cleanup(w, dir); }
    }
}
