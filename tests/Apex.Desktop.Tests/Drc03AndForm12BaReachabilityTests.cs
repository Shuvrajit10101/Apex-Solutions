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

    /// <summary>
    /// 🔴 <b>THE "OTHERS / PLEASE SPECIFY" SUB-FLOW, WHICH HAD ZERO COVERAGE AND SILENTLY DISCARDS THE OPERATOR'S
    /// STATED CAUSE WHEN IT IS ONE CHARACTER WRONG.</b> <c>NeedsOthersSpecify =&gt; SelectedCause.Ordinal == 11</c>
    /// is the hinge of the whole sub-flow: it shows the free-text box, it gates the "specify the cause" refusal, and
    /// it decides whether <c>ComposeCause</c> appends the typed text to the stored cause. Point that ordinal at any
    /// other cause and the failure is <b>silent and lossy in the worst direction</b> — the operator selects
    /// "Others", is never asked what it was, and a DRC-03 is filed on the portal's generic word with their actual
    /// stated cause thrown away. Nothing throws, nothing turns red, and the record is wrong forever, because
    /// <see cref="GstDrc03.Cause"/> is what every later reconciliation matches on.
    ///
    /// <para>Driven end-to-end through the real engine, so it covers the flag, the refusal, the composition and the
    /// stored record in one pass. Every neighbouring cause is checked too: 10 and 12 sit either side of 11 in the
    /// portal's own numbering, which is precisely where an off-by-one lands.</para>
    /// </summary>
    [AvaloniaFact]
    public void The_others_cause_asks_for_the_specify_text_and_files_it_with_the_record()
    {
        var (w, vm, dir) = NewWindow("Drc03Others");
        try
        {
            SeedRegularGst(vm, "DRC-03 Others Co");
            Pump(w);

            vm.OpenDrc03Payment();
            Pump(w);
            var page = vm.Drc03Payment!;

            // 1. Only the portal's cause 11 asks for free text — checked across ALL twelve, not just the default.
            foreach (var cause in Drc03CauseOption.All)
            {
                page.SelectedCause = cause;
                Assert.True(page.NeedsOthersSpecify == (cause.Ordinal == 11),
                    $"Cause {cause.Ordinal} (\"{cause.Text}\") reports NeedsOthersSpecify = " +
                    $"{page.NeedsOthersSpecify}. Only the portal's cause 11 (\"Others\") has a Please-specify " +
                    "field; anything else either hides the box under Others (discarding the stated cause) or " +
                    "demands free text under a cause the portal never asks it for.");
            }

            // 2. Under "Others", a blank specify box is REFUSED — and nothing is filed.
            page.SelectedCause = Drc03CauseOption.All.Single(c => c.Ordinal == 11);
            Assert.True(page.NeedsOthersSpecify);
            page.OthersSpecifyText = "   ";
            page.Period = "2024-25";
            page.IgstText = "5000";
            page.Method = GstDepositService.PaymentMethod.Bank;

            Assert.False(page.Post());
            Assert.False(page.LastActionSucceeded);
            Assert.Contains("Others", page.Message!);
            Assert.Empty(vm.Company!.GstDrc03s);

            // 3. 🔴 The stated cause is CARRIED INTO THE RECORD, not dropped. This is the assertion a wrong ordinal
            //    fails: with the box hidden, ComposeCause returns the bare portal string and the reason is lost.
            page.OthersSpecifyText = "ITC reversed on a supplier who never filed GSTR-1";
            Assert.True(page.Post(), "The DRC-03 was refused: " + page.Message);
            Assert.True(page.LastActionSucceeded);

            var filed = Assert.Single(vm.Company!.GstDrc03s);
            Assert.StartsWith("Others", filed.Cause, StringComparison.Ordinal);
            Assert.Contains("ITC reversed on a supplier who never filed GSTR-1", filed.Cause, StringComparison.Ordinal);

            // 4. Leaving cause 11 clears the free text, so it can never be appended to a different cause.
            page.SelectedCause = Drc03CauseOption.All.Single(c => c.Ordinal == 12);
            Assert.False(page.NeedsOthersSpecify);
            Assert.Equal(string.Empty, page.OthersSpecifyText);

            page.Period = "2024-25";
            page.IgstText = "2500";
            Assert.True(page.Post(), "The DRC-03 was refused: " + page.Message);
            var second = vm.Company!.GstDrc03s.Single(d => d.Cause != filed.Cause);
            Assert.Equal("Order", second.Cause);
        }
        finally { Cleanup(w, dir); }
    }

    /// <summary>
    /// 🔴 <b>A CASH BALANCE THAT COULD NOT BE READ MUST NOT BE SHOWN AS ₹0.00 ON A SCREEN THAT IS ABOUT TO TAKE A
    /// PAYMENT.</b> The read-out used to swallow both exception types into <c>"0.00"</c>. That is not a harmless
    /// default: a cell genuinely holding cash would read empty, the operator would conclude the electronic cash
    /// ledger was exhausted and fund the DRC-03 from the bank — paying twice for one liability — and nothing on the
    /// screen would ever have said the app did not know.
    ///
    /// <para>The engine's own projection is a pure loop over challans and vouchers and cannot be made to throw from
    /// a <c>Company</c>, so the branch is exercised through the constructor's cash-reader seam — the same shape as
    /// the export page's <c>writeBytes</c> seam. Untestable error handling is exactly how a wrong figure survives a
    /// green suite.</para>
    /// </summary>
    [AvaloniaFact]
    public void A_cash_cell_that_cannot_be_read_says_so_instead_of_showing_zero()
    {
        var (w, vm, dir) = NewWindow("Drc03CashRead");
        try
        {
            SeedRegularGst(vm, "DRC-03 Cash Read Co");
            Pump(w);

            var page = new Drc03PaymentViewModel(
                vm.Company!, new CompanyStorage(dir),
                availableCash: (major, _) => major == GstTaxHead.Central
                    ? throw new InvalidOperationException("the electronic cash ledger is unreadable")
                    : Money.Zero);

            // 🔴 The unreadable cell is NOT a number, and above all is not "0.00".
            Assert.Equal(Drc03PaymentViewModel.CashUnreadable, page.AvailableCgstText);
            Assert.NotEqual("0.00", page.AvailableCgstText);

            // The cells that DID read are still figures — a single bad cell does not blank the whole read-out.
            Assert.Equal("0.00", page.AvailableSgstText);

            // …and the failure is surfaced, naming the cell and saying what a blank does not mean.
            Assert.True(page.CashReadFailed);
            Assert.Contains("could not be read", page.CashReadErrorText);
            Assert.Contains("the electronic cash ledger is unreadable", page.CashReadErrorText);
            Assert.Contains("not read a blank cell as a nil balance", page.CashReadErrorText);

            // A book that reads cleanly raises nothing at all — the warning is state, not decoration.
            var healthy = new Drc03PaymentViewModel(vm.Company!, new CompanyStorage(dir));
            Assert.False(healthy.CashReadFailed);
            Assert.Equal(string.Empty, healthy.CashReadErrorText);
            Assert.Equal("0.00", healthy.AvailableCgstText);
        }
        finally { Cleanup(w, dir); }
    }

    // ================================================================ ROW 6.42 — Form 12BA

    /// <summary>
    /// A saved salary-TDS company with one employee and twelve posted payroll runs.
    /// Seeds a §192 company with one employee paid <paramref name="monthlyBasic"/> for twelve months, so gross
    /// salary is exactly <c>12 × monthlyBasic</c> — Basic is the only earning in the fixture.
    ///
    /// <para><paramref name="lastMonthDelta"/> shifts the <b>final month's</b> Basic by a signed rupee amount using
    /// a dated structure revision (<see cref="SalaryStructureService.InForceOn"/>, ER-4), making annual gross
    /// <c>12 × monthlyBasic + lastMonthDelta</c>. That is the only way to land the fixture ONE RUPEE either side of
    /// a threshold that is not divisible by twelve, and landing exactly there is the whole point: see
    /// <see cref="Form_12ba_reports_the_rule_26A_threshold_verdict_from_real_gross_salary"/>.</para>
    /// </summary>
    private static void SeedSalaryTds(MainWindowViewModel vm, string name, decimal monthlyBasic,
                                      decimal lastMonthDelta = 0m)
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
            // The final month may carry a signed rupee shift, applied as a dated structure revision so the payroll
            // engine resolves it exactly as it would a real mid-year revision — no test-only path into the figures.
            if (i == 11 && lastMonthDelta != 0m)
                new SalaryStructureService(c).DefineForEmployee(e.Id, d, new[]
                {
                    new SalaryStructureLine(basic.Id, 0, new Money(monthlyBasic + lastMonthDelta)),
                    new SalaryStructureLine(tds.Id, 1),
                });

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
    ///
    /// <para>🔴 <b>THE THREE-ROW BOUNDARY BLOCK IS THE POINT OF THIS TEST, AND IT IS PINNED TWICE OVER.</b> The
    /// strict-inequality claim is asserted in four places in the shipped source — the type remarks, the constant's
    /// own doc, the inline comment at the comparison, and the wording of <c>ThresholdText</c> — and four assertions
    /// of a claim are still zero tests of it. The far-over and far-under rows below pass identically whether the
    /// code says <c>&gt;</c> or <c>&gt;=</c>; only <b>exactly at</b> separates them.</para>
    ///
    /// <para>Each boundary row also asserts the <b>gross salary the fixture actually produced</b>, not merely the
    /// verdict. Without that, a fixture that drifted off ₹1,50,000 by a rupee would leave a boundary test that no
    /// longer tests the boundary and never says so — a dead guard that reads exactly like a live one, which is a
    /// defect class this project has shipped before.</para>
    /// </summary>
    [AvaloniaTheory]
    [InlineData(1_25_000, 0, 15_00_000, true)]    // ₹15,00,000 a year — far over the threshold
    [InlineData(10_000, 0, 1_20_000, false)]      // ₹1,20,000 a year — far under it
    // 🔴 THE BOUNDARY. Basic is the sole earning, so gross is 12 × basic (+ the final-month delta). Rule 26A(2)(b)
    // requires the statement where salary "exceeds" ₹1,50,000, so exactly-at-it is NOT due, one rupee under is NOT
    // due, and one rupee over IS. Flip the operator to `>=` and the exactly-at row reddens on IsFormDue.
    [InlineData(12_500, -1, 1_49_999, false)]     // ₹1,49,999 — one rupee below
    [InlineData(12_500, 0, 1_50_000, false)]      // ₹1,50,000 — EXACTLY the threshold; "exceeds" is strict
    [InlineData(12_500, 1, 1_50_001, true)]       // ₹1,50,001 — one rupee above
    public void Form_12ba_reports_the_rule_26A_threshold_verdict_from_real_gross_salary(
        int monthlyBasic, int lastMonthDelta, int expectedAnnualGross, bool expectedDue)
    {
        var (w, vm, dir) = NewWindow($"F12BAThresh{monthlyBasic}_{lastMonthDelta}");
        try
        {
            SeedSalaryTds(vm, $"Form12BA Threshold Co {monthlyBasic} {lastMonthDelta}", monthlyBasic,
                          lastMonthDelta);
            Pump(w);

            vm.OpenForm12Ba();
            Pump(w);
            var page = vm.Form12Ba!;

            Assert.NotEmpty(page.Employees);

            // 🔴 FIRST: prove the fixture is where it claims to be. A boundary case that has drifted off the
            // boundary still passes its verdict assertion and guards nothing.
            Assert.True(page.Certificate?.PartB is not null,
                "The fixture produced no Form 24Q Annexure-II row, so there is no measured gross salary to test " +
                "the threshold against.");
            Assert.Equal(expectedAnnualGross, page.Certificate!.PartB!.GrossSalary.Amount);

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

    /// <summary>
    /// 🔴 <b>AN UNMEASURED SALARY MUST NOT BE PRINTED AS A COMPUTED VERDICT.</b> <c>Project</c> used to read
    /// <c>cert.PartB?.GrossSalary ?? Money.Zero</c>. Through the picker that fallback cannot fire — the employee
    /// list IS the Annexure-II projection — but the day it did, an <b>unmeasured</b> salary would enter the rule
    /// 26A(2)(b) test as ₹0.00, fail it, and the screen would state "this statement is not required for this
    /// employee" as though something had decided it. A nil figure and an unmeasured figure are not interchangeable
    /// on a statutory form; that is the same principle the empty perquisite table on this very screen exists to
    /// honour.
    ///
    /// <para>Reached the only way it can be: by selecting an employee who <b>exists</b> but has no §192 activity in
    /// the year, so <c>Form16.Build</c> resolves the employee and finds no Annexure-II row.</para>
    /// </summary>
    [AvaloniaFact]
    public void Form_12ba_states_an_unmeasured_gross_salary_instead_of_giving_a_verdict_on_zero()
    {
        var (w, vm, dir) = NewWindow("F12BAUnmeasured");
        try
        {
            SeedSalaryTds(vm, "Form12BA Unmeasured Co", 1_25_000m);
            Pump(w);

            // A second employee with no salary structure and no payroll voucher: a real employee of this company,
            // with no §192 activity at all, so Form 24Q Annexure-II carries no row for them.
            var c = vm.Company!;
            var payroll = new PayrollService(c);
            var groupId = c.EmployeeGroups.First().Id;
            var quiet = payroll.CreateEmployee("Silent Partner", groupId);

            vm.OpenForm12Ba();
            Pump(w);
            var page = vm.Form12Ba!;

            // The picker itself never offers them — that is the invariant that makes the fallback unreachable.
            Assert.DoesNotContain(page.Employees, x => x.EmployeeId == quiet.Id);

            page.SelectedEmployee = new Form12BaEmployeeOptionVm
            {
                EmployeeId = quiet.Id, Name = "Silent Partner", Pan = "PANNOTAVBL", GrossSalary = "0.00",
            };
            Pump(w);

            Assert.Null(page.Certificate!.PartB);

            // 🔴 The figure is stated as unmeasured, NOT as zero.
            Assert.Equal("—", page.GrossSalaryText);
            Assert.DoesNotContain("0.00", page.GrossSalaryText);

            // 🔴 And no verdict is given in either direction.
            Assert.False(page.IsFormDue);
            Assert.DoesNotContain("is not required for this employee", page.ThresholdText);
            Assert.DoesNotContain("is required for this employee", page.ThresholdText);
            Assert.Contains("could not be measured", page.ThresholdText);
            Assert.Contains("UNKNOWN", page.ThresholdText);

            Assert.True(ScreenShows(w, "could not be measured"),
                "The screen still shows a threshold verdict for a salary it never measured.");
        }
        finally { Cleanup(w, dir); }
    }

    /// <summary>
    /// After <see cref="Form12BaViewModel.Rebuild"/> replaces the employee list, the selection must point INTO the
    /// new list. Setting <c>HighlightedIndex</c> to the value it already holds raises no change notification, so the
    /// index alone cannot re-point anything — the unconditional <c>SelectedEmployee</c> assignment is what does it.
    /// This is the shape the deleted per-row <c>IsHighlighted</c> flag failed at silently: nothing bound it, so
    /// nothing could notice it pointing at a discarded object.
    ///
    /// <para>🔴 <b>Exercised on a BARE view model, with no window and no ListBox — and that is the whole design of
    /// this test, not an economy.</b> Measured: with the page realised in the window, deleting the
    /// <c>SelectedEmployee</c> assignment leaves every assertion here still passing, because the ListBox's
    /// <c>SelectedIndex</c> two-way binding drives the index to -1 when the collection is cleared and back to 0
    /// afterwards, so the change notification fires anyway and the view rescues the view model. A test that only
    /// ever runs bound to its view cannot see a view-model defect the view happens to paper over — and
    /// <see cref="Form12BaViewModel"/>'s own remarks promise it is headlessly testable, which is exactly the promise
    /// this asserts.</para>
    /// </summary>
    [AvaloniaFact]
    public void Rebuilding_the_form_12ba_employee_list_repoints_the_selection_into_the_new_list()
    {
        var (w, vm, dir) = NewWindow("F12BARebuild");
        try
        {
            SeedSalaryTds(vm, "Form12BA Rebuild Co", 1_25_000m);
            Pump(w);

            // Bare view model: nothing binds to it, so nothing can compensate for it.
            var page = new Form12BaViewModel(vm.Company!);

            Assert.NotEmpty(page.Employees);
            Assert.Equal(0, page.HighlightedIndex);          // the index that will NOT change across the rebuild
            var before = page.SelectedEmployee;
            Assert.NotNull(before);

            page.Rebuild();

            Assert.Equal(0, page.HighlightedIndex);
            Assert.NotNull(page.SelectedEmployee);
            Assert.NotSame(before, page.SelectedEmployee);   // the old row really was discarded…
            Assert.Contains(page.SelectedEmployee!, page.Employees);          // …and the new one is in the new list
            Assert.Same(page.Employees[page.HighlightedIndex], page.SelectedEmployee);
            Assert.NotNull(page.Certificate);                // …and the projection re-ran off the new selection
        }
        finally { Cleanup(w, dir); }
    }
}
