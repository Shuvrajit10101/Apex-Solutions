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
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Desktop.Tests;

/// <summary>
/// <b>Census row 6.24 — Record ISD Credit, driven by the keyboard alone and asserted on the REALISED window.</b>
///
/// <para>🔴 <b>WHY THIS FILE PRESSES KEYS INSTEAD OF CALLING THE OPENER.</b> This project has filed three dead
/// features — a correct engine with no door. A test that called <c>vm.OpenRecordIsdCredit()</c> and asserted the
/// property became non-null would stay green on the day the menu row, the dispatch case or the DataTemplate went
/// missing, and those three are exactly what make a capability reachable. So every test below starts on the
/// Gateway and walks Reports → Statutory Reports → GST Actions → Record ISD Credit with ArrowDown and Enter
/// through <see cref="MainWindow"/>'s own key handler, then posts with <b>Ctrl+A</b> and reads the result off the
/// BOOK rather than off the view model it just set.</para>
///
/// <para><b>Grounding (R7, ruling 14 tier 1), read BY CONTENT at
/// <c>help.tallysolutions.com/tally-prime/india-gst/isd-credit/</c>:</b> <i>"You can account for Input Service
/// Distributor Credit (ISD) Credit in TallyPrime when your branch receives Input Tax Credit distributed by the
/// head office"</i>; the Stat Adjustment is <i>"Increase in Input Tax Credit"</i> &gt; <i>"ISD Invoice/Debit
/// Note"</i>; and <i>"The voucher will be included in GSTR-3B and appear under 4A: Input Tax Credit Available
/// (either in part or in full) &gt; Inward supplies from ISD."</i></para>
/// </summary>
public sealed class RecordIsdCreditReachabilityTests
{
    private static readonly DateOnly FyStart = new(2024, 4, 1);
    private const string Karnataka = "29";

    private static string GstinFor(string stateCode, string pan)
    {
        var body = stateCode + pan + "1Z";
        return body + Gstin.ComputeCheckDigit(body + "0");
    }

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

    private static List<TextBlock> VisibleTextBlocks(Window w) =>
        Descendants(w).OfType<TextBlock>()
            .Where(t => t.IsEffectivelyVisible && t.Bounds.Width > 0 && t.Bounds.Height > 0)
            .ToList();

    private static bool ScreenShows(Window w, string fragment) =>
        VisibleTextBlocks(w).Any(t => (t.Text ?? string.Empty).Contains(fragment, StringComparison.Ordinal));

    private static List<T> VisibleControls<T>(Window w) where T : Control =>
        Descendants(w).OfType<T>()
            .Where(c => c.IsEffectivelyVisible && c.Bounds.Width > 0 && c.Bounds.Height > 0)
            .ToList();

    private static void KeyboardInto(MainWindow w, MainWindowViewModel vm, string label)
    {
        var offered = vm.Menu.Where(i => i.IsSelectable).Select(i => i.Label).ToList();
        Assert.True(offered.Contains(label),
            $"The keyboard cascade never offered '{label}'. This column offers: {string.Join(" | ", offered)}. " +
            "A screen with no menu row is unreachable and does not move a census row.");

        for (var i = 0; i <= vm.Menu.Count; i++)
        {
            if (vm.SelectedIndex >= 0 && vm.SelectedIndex < vm.Menu.Count
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
        var dir = Path.Combine(Path.GetTempPath(), "ApexBG_" + tag + "_" + Guid.NewGuid().ToString("N"));
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

    /// <summary>A saved Regular-GST Karnataka company with a "Head Office Account" ledger to take the credit
    /// against, and optionally a second registration so the picker is live.</summary>
    private static void SeedCompany(MainWindowViewModel vm, string name, bool multiRegistration)
    {
        vm.NewCompanyName = name;
        vm.CreateCompany();
        var c = vm.Company!;
        c.FinancialYearStart = FyStart;
        c.BooksBeginFrom = FyStart;

        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = Karnataka,
            Gstin = GstinFor(Karnataka, "AAPFU0939F"),
            RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart,
            Periodicity = GstReturnPeriodicity.Monthly,
        });

        if (multiRegistration)
        {
            c.Gst!.AddRegistration(new GstRegistration(
                Guid.NewGuid(), "Tamil Nadu Registration", "33", GstinFor("33", "AABCC1206D"),
                GstRegistrationType.Regular, FyStart));
            c.Gst!.EnsureValid();
        }

        c.AddLedger(new Apex.Ledger.Domain.Ledger(
            Guid.NewGuid(), "Head Office Account", c.FindGroupByName("Indirect Expenses")!.Id, Money.Zero, true));

        vm.ShowGateway();
    }

    private static void OpenScreen(MainWindow w, MainWindowViewModel vm)
    {
        KeyboardInto(w, vm, "Statutory Reports");
        KeyboardInto(w, vm, "GST Actions");
        KeyboardInto(w, vm, "Record ISD Credit");
    }

    // ================================================================ the row

    /// <summary>
    /// 🔴 THE REACHABILITY TEST. Gateway → Reports → Statutory Reports → GST Actions → Record ISD Credit, by
    /// ArrowDown and Enter alone, ending on a REALISED page carrying the vendor's own Stat-Adjustment wording,
    /// the form's fields and the declared divergence.
    /// </summary>
    [AvaloniaFact]
    public void Record_isd_credit_is_reachable_from_the_gateway_by_keyboard_alone()
    {
        var (w, vm, dir) = NewWindow("IsdReach");
        try
        {
            SeedCompany(vm, "ISD Credit Reach Co", multiRegistration: false);
            Pump(w);

            OpenScreen(w, vm);

            Assert.Equal(Screen.RecordIsdCredit, vm.CurrentScreen);
            Assert.NotNull(vm.RecordIsdCredit);

            // The page is REALISED — these captions live only in the DataTemplate, so a missing template fails here.
            Assert.True(ScreenShows(w, "Record ISD Credit"), "The title is not on screen.");
            Assert.True(ScreenShows(w, "ISD Invoice / Debit Note"),
                "The ISD document field is not on screen — the vendor's own required field.");
            Assert.True(ScreenShows(w, "Credit To (purchase / expense / branch)"),
                "The counterpart-ledger picker is not on screen.");

            // 🔴 The vendor's own Stat Adjustment wording is shown, so an operator coming from that page can see
            // the two selections match rather than having to trust that they do.
            Assert.True(ScreenShows(w, "Increase in Input Tax Credit"),
                "The Nature of Adjustment is not shown.");
            Assert.True(ScreenShows(w, "ISD Invoice/Debit Note"),
                "The Additional Nature of Adjustment is not shown.");

            // 🔴 The DECLARED DIVERGENCE is rendered, not buried in a code comment.
            Assert.True(ScreenShows(w, "Credit related to goods or capital goods cannot be distributed through ISD"),
                "The vendor's services-only bound is not on screen.");
            Assert.True(ScreenShows(w, "Both are ours, not the vendor's."),
                "The screen does not own its two divergences on its face.");

            // The form's four head boxes plus the three text fields are realised and editable.
            Assert.True(VisibleControls<TextBox>(w).Count >= 7,
                "The ISD document, dates and four head boxes are not all realised.");
            Assert.True(VisibleControls<ComboBox>(w).Count >= 1,
                "The counterpart-ledger picker is not realised.");
        }
        finally { Cleanup(w, dir); }
    }

    /// <summary>
    /// 🔴 <b>THE END-TO-END MONEY TEST — Ctrl+A on the realised screen posts a real voucher, and GSTR-3B Table
    /// 4(A)(4) reads it off the BOOK.</b> ₹1,500 CGST + ₹1,500 SGST + ₹2,000 IGST = ₹5,000 across the three GST
    /// heads. Nothing is asserted on an internal the test just set.
    /// </summary>
    [AvaloniaFact]
    public void Ctrl_a_records_the_receipt_and_it_files_in_gstr3b_table_4a4()
    {
        var (w, vm, dir) = NewWindow("IsdPost");
        try
        {
            SeedCompany(vm, "ISD Credit Post Co", multiRegistration: false);
            Pump(w);
            OpenScreen(w, vm);

            var page = vm.RecordIsdCredit!;
            var company = vm.Company!;
            var before = company.Vouchers.Count;

            page.DocumentNumberText = "ISD/2024-25/001";
            page.DocumentDateText = "10-Apr-2024";
            page.VoucherDateText = "20-Apr-2024";
            page.CgstText = "1500";
            page.SgstText = "1500";
            page.IgstText = "2000";
            Pump(w);

            // 🔴 Ctrl+A through the window's own key handler — not page.Record().
            w.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
            Pump(w);

            Assert.True(page.LastActionSucceeded, $"Ctrl+A did not record the receipt. Message: {page.Message}");
            Assert.Equal(before + 1, company.Vouchers.Count);

            // The observable artefact: the posted voucher in the book, tagged and balanced.
            var posted = company.Vouchers.Single(v =>
                v.Lines.Any(l => l.Gst is { Adjustment: GstAdjustmentKind.IsdCreditReceived }));
            Assert.Equal(
                posted.Lines.Where(l => l.Side == DrCr.Debit).Sum(l => l.Amount.Amount),
                posted.Lines.Where(l => l.Side == DrCr.Credit).Sum(l => l.Amount.Amount));

            // …and the filed figure, built from the book.
            var b3 = Gstr3b.Build(company, new DateOnly(2024, 4, 1), new DateOnly(2024, 4, 30));
            Assert.Equal(1_500m, b3.Itc4A4IsdCgst.Amount);
            Assert.Equal(1_500m, b3.Itc4A4IsdSgst.Amount);
            Assert.Equal(2_000m, b3.Itc4A4IsdIgst.Amount);
            Assert.Equal(5_000m, b3.TotalItc4A4Isd.Amount);

            // The receipt is now listed on the screen, and the document number survived the round trip through
            // the narration the service writes.
            Assert.Single(page.Recorded);
            Assert.Equal("ISD/2024-25/001", page.Recorded[0].Document);
            Pump(w);
            Assert.True(ScreenShows(w, "ISD/2024-25/001"),
                "The recorded receipt is not rendered in the list.");
        }
        finally { Cleanup(w, dir); }
    }

    /// <summary>
    /// 🔴 The screen can leave the screen. <c>IsPrintablePage</c> and <c>IsExportablePage</c> are the PRODUCTION
    /// gates; row 6.19's history is three passes of a screen believed to be an output dead end, so both are
    /// asserted true and the snapshot is asserted to carry real columns and real rows.
    /// </summary>
    [AvaloniaFact]
    public void The_screen_can_be_printed_and_exported_with_real_rows()
    {
        var (w, vm, dir) = NewWindow("IsdExport");
        try
        {
            SeedCompany(vm, "ISD Credit Export Co", multiRegistration: false);
            Pump(w);
            OpenScreen(w, vm);

            var page = vm.RecordIsdCredit!;
            page.DocumentNumberText = "ISD/2024-25/002";
            page.DocumentDateText = "10-Apr-2024";
            page.VoucherDateText = "20-Apr-2024";
            page.CgstText = "1500";
            page.SgstText = "1500";
            Pump(w);
            w.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
            Pump(w);
            Assert.True(page.LastActionSucceeded, $"Setup post failed: {page.Message}");

            Assert.True(vm.IsPrintablePage, "The ISD screen is not printable.");
            Assert.True(vm.IsExportablePage, "The ISD screen is not exportable.");

            var snapshot = page.ToMasterListSnapshot();
            Assert.False(string.IsNullOrWhiteSpace(snapshot.Title));
            Assert.NotEmpty(snapshot.Columns);
            Assert.NotEmpty(snapshot.Rows);
            Assert.All(snapshot.Rows, r => Assert.Equal(snapshot.Columns.Count, r.Count));
            Assert.Contains(snapshot.Rows, r => r.Contains("ISD/2024-25/002"));
        }
        finally { Cleanup(w, dir); }
    }

    /// <summary>
    /// 🔴 The duplicate refusal reaches the OPERATOR, not just the engine. A guard that throws into a swallowed
    /// catch is the defect class this project filed as T1-56 (a return filed with no feedback).
    /// </summary>
    [AvaloniaFact]
    public void A_duplicate_document_number_is_refused_on_screen_and_posts_nothing()
    {
        var (w, vm, dir) = NewWindow("IsdDup");
        try
        {
            SeedCompany(vm, "ISD Credit Dup Co", multiRegistration: false);
            Pump(w);
            OpenScreen(w, vm);

            var page = vm.RecordIsdCredit!;
            var company = vm.Company!;

            void Fill()
            {
                page.DocumentNumberText = "ISD/2024-25/003";
                page.DocumentDateText = "10-Apr-2024";
                page.VoucherDateText = "20-Apr-2024";
                page.CgstText = "1000";
            }

            Fill();
            Pump(w);
            w.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
            Pump(w);
            Assert.True(page.LastActionSucceeded);
            var after = company.Vouchers.Count;

            // The same document again.
            Fill();
            Pump(w);
            w.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
            Pump(w);

            Assert.False(page.LastActionSucceeded, "The duplicate was accepted.");
            Assert.Contains("ISD/2024-25/003", page.Message!, StringComparison.Ordinal);
            Assert.Equal(after, company.Vouchers.Count);

            // The refusal is VISIBLE, not merely held in a property.
            Assert.True(ScreenShows(w, "already been recorded"),
                "The duplicate refusal is not rendered on the screen.");

            // And only one receipt's credit is filed.
            var b3 = Gstr3b.Build(company, new DateOnly(2024, 4, 1), new DateOnly(2024, 4, 30));
            Assert.Equal(1_000m, b3.TotalItc4A4Isd.Amount);
        }
        finally { Cleanup(w, dir); }
    }

    /// <summary>
    /// On a multi-registration book the registration picker is live and the credit is received into the
    /// SELECTED registration — ITC lands in exactly one GSTIN's credit ledger.
    /// </summary>
    [AvaloniaFact]
    public void The_credit_is_received_into_the_selected_registration_only()
    {
        var (w, vm, dir) = NewWindow("IsdScope");
        try
        {
            SeedCompany(vm, "ISD Credit Scope Co", multiRegistration: true);
            Pump(w);
            OpenScreen(w, vm);

            var page = vm.RecordIsdCredit!;
            var company = vm.Company!;

            Assert.True(page.ShowsRegistrationPicker, "The registration picker is hidden on a multi-GSTIN book.");

            var branch = page.Registrations.Single(r => r.Name == "Tamil Nadu Registration");
            page.SelectedRegistration = branch;
            page.DocumentNumberText = "ISD/2024-25/004";
            page.DocumentDateText = "10-Apr-2024";
            page.VoucherDateText = "20-Apr-2024";
            page.IgstText = "2000";
            Pump(w);

            w.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
            Pump(w);
            Assert.True(page.LastActionSucceeded, $"Post failed: {page.Message}");

            var from = new DateOnly(2024, 4, 1);
            var to = new DateOnly(2024, 4, 30);

            Assert.Equal(2_000m, Gstr3b.Build(company, from, to, branch.Id).Itc4A4IsdIgst.Amount);
            Assert.Equal(0m, Gstr3b.Build(company, from, to, GstRegistration.PrimaryId).Itc4A4IsdIgst.Amount);
        }
        finally { Cleanup(w, dir); }
    }

    /// <summary>
    /// 🔴 The counterpart picker must not offer a Duties &amp; Taxes ledger. The four <c>Input {head}</c>
    /// ledgers are the very pools this receipt debits, so crediting one would let an operator post a
    /// self-cancelling voucher that still files a real Table 4(A)(4) figure — credit claimed on a return with no
    /// matching movement in the books.
    /// </summary>
    [AvaloniaFact]
    public void The_counterpart_picker_never_offers_a_duties_and_taxes_ledger()
    {
        var (w, vm, dir) = NewWindow("IsdLedgers");
        try
        {
            SeedCompany(vm, "ISD Credit Ledger Co", multiRegistration: false);
            Pump(w);
            OpenScreen(w, vm);

            var page = vm.RecordIsdCredit!;
            var company = vm.Company!;

            Assert.NotEmpty(page.CounterpartOptions);

            foreach (var offered in page.CounterpartOptions)
            {
                var group = company.FindGroup(offered.GroupId);
                while (group is not null)
                {
                    Assert.False(
                        string.Equals(group.Name, "Duties & Taxes", StringComparison.OrdinalIgnoreCase),
                        $"'{offered.Name}' is under Duties & Taxes and must not be offered as the ISD counterpart.");
                    group = group.ParentId is { } pid ? company.FindGroup(pid) : null;
                }
            }

            // The Input tax ledgers exist on this book — so the assertion above is a real filter, not a vacuous
            // pass over a book that happens to have none.
            var gst = new GstService(company);
            Assert.NotNull(gst.FindTaxLedger(GstTaxHead.Central, GstTaxDirection.Input));
            Assert.DoesNotContain(page.CounterpartOptions,
                l => l.Id == gst.FindTaxLedger(GstTaxHead.Central, GstTaxDirection.Input)!.Id);
        }
        finally { Cleanup(w, dir); }
    }
}
