using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Desktop.Views;
using Xunit;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.Tests;

/// <summary>
/// Census 6.28 (wave 12, track hf) — the independent verifier's reproduction of the review fix 3b67e76, driven end
/// to end through the REAL key handlers of a realised <see cref="MainWindow"/>: the voucher is posted through the
/// voucher screen, the master is reached Gateway → Create → Nature of Payment by arrows, opened with Ctrl+Enter and
/// saved with Ctrl+A, and the posted voucher is re-opened from the Day Book with Ctrl+Enter and accepted with
/// Ctrl+A. Assertions are on the RENDERED message TextBlocks and on a book reloaded through a fresh
/// <see cref="CompanyStorage"/>.
///
/// <para>What is pinned is the TRUE consequence of having no dated rate history (IV-343): after a rate or threshold
/// alteration, a voucher posted under the old figures keeps them on disk and a narration-only alteration of it is
/// refused, with a refusal that names the right cause; the master's own save says so when it happens.</para>
/// </summary>
public sealed class NatureOfPaymentAlterVerifierTests
{
    private const string ValidTan = "MUMA12345B";
    private const string DeducteePan = "AAPFU0939F";

    private sealed record Book(MainWindow Window, MainWindowViewModel Vm, string Dir, string Name);

    private static Book NewTdsBook(string name)
    {
        var dir = Path.Combine(Path.GetTempPath(), "ApexW12hfV_" + Guid.NewGuid().ToString("N"));
        var storage = new CompanyStorage(dir);
        var vm = new MainWindowViewModel(storage);
        vm.NewCompanyName = name;
        vm.CreateCompany();
        var c = vm.Company!;
        new TdsTcsService(c).EnableTds(new TdsConfig { Tan = ValidTan });
        var fees = AddLedger(c, "Professional Fees", "Indirect Expenses", true);
        fees.TdsApplicable = true;
        fees.TdsNatureOfPaymentId = c.FindNatureOfPaymentByCode("194J(b)")!.Id;
        var vendor = AddLedger(c, "Acme Consultants", "Sundry Creditors", false);
        vendor.DeducteeType = DeducteeType.Firm;
        vendor.PartyPan = DeducteePan;
        storage.Save(c);
        vm.ShowGateway();

        var window = new MainWindow { DataContext = vm };
        window.Show();
        Pump(window);
        return new Book(window, vm, dir, name);
    }

    private static DomainLedger AddLedger(Company c, string name, string groupName, bool openingIsDebit)
    {
        var l = new DomainLedger(Guid.NewGuid(), name, c.FindGroupByName(groupName)!.Id, Money.Zero, openingIsDebit);
        c.AddLedger(l);
        return l;
    }

    private static void Pump(MainWindow window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }

    private static void Close(Book b)
    {
        b.Window.Close();
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(b.Dir)) Directory.Delete(b.Dir, recursive: true);
        }
        catch (IOException) { /* best effort */ }
        catch (UnauthorizedAccessException) { /* best effort */ }
    }

    private static void Key(MainWindow window, PhysicalKey key, RawInputModifiers mods = RawInputModifiers.None)
    {
        window.KeyPressQwerty(key, mods);
        Pump(window);
    }

    private static Company Reload(Book b)
    {
        var fresh = new CompanyStorage(b.Dir);
        return fresh.Load(fresh.ListCompanies().Single(e => string.Equals(e.Name, b.Name, StringComparison.Ordinal)));
    }

    private static string? ActiveLabel(MainWindowViewModel vm) => vm.Columns[vm.ActiveColumnIndex].Selected?.Label;

    private static void ArrowToAndEnter(MainWindow window, MainWindowViewModel vm, string label)
    {
        var bound = vm.Columns[vm.ActiveColumnIndex].Items.Count + 2;
        for (var i = 0; i < bound; i++)
        {
            if (ActiveLabel(vm) == label) { Key(window, PhysicalKey.Enter); return; }
            Key(window, PhysicalKey.ArrowDown);
        }
        Assert.Fail($"'{label}' was not reachable by arrow navigation from the active column.");
    }

    /// <summary>Gateway → Create → Nature of Payment by arrows, then the section's row by arrows, then
    /// Ctrl+Enter: the alteration form over the stored master.</summary>
    private static NatureOfPaymentMasterViewModel OpenAlteration(Book b, string section)
    {
        b.Vm.ShowGateway();
        Pump(b.Window);
        ArrowToAndEnter(b.Window, b.Vm, "Create");
        ArrowToAndEnter(b.Window, b.Vm, "Nature of Payment");
        Assert.Equal(Screen.NatureOfPaymentMaster, b.Vm.CurrentScreen);
        var bound = (b.Vm.NatureOfPaymentMaster?.Natures.Count ?? 0) + 2;
        for (var i = 0; i < bound && b.Vm.NatureOfPaymentMaster?.HighlightedRow?.SectionCode != section; i++)
            Key(b.Window, PhysicalKey.ArrowDown);
        Assert.Equal(section, b.Vm.NatureOfPaymentMaster?.HighlightedRow?.SectionCode);
        Key(b.Window, PhysicalKey.Enter, RawInputModifiers.Control);
        var alter = b.Vm.NatureOfPaymentMaster!;
        Assert.True(alter.IsAltering);
        return alter;
    }

    /// <summary>Posts Dr Fees / Cr Vendor through the voucher screen; returns the posted voucher.</summary>
    private static Voucher PostFeesJournal(MainWindowViewModel vm, decimal gross)
    {
        var c = vm.Company!;
        var before = c.Vouchers.Select(v => v.Id).ToHashSet();
        vm.OpenVoucher(VoucherBaseType.Journal);
        var e = vm.VoucherEntry!;
        var text = gross.ToString(CultureInfo.InvariantCulture);
        e.Lines[0].SelectedLedger = c.FindLedgerByName("Professional Fees")!;
        e.Lines[0].Side = DrCr.Debit; e.Lines[0].AmountText = text;
        e.Lines[1].SelectedLedger = c.FindLedgerByName("Acme Consultants")!;
        e.Lines[1].Side = DrCr.Credit; e.Lines[1].AmountText = text;
        e.Recalculate();
        Assert.True(e.ShowTdsPanel);
        Assert.True(e.Accept(), e.Message);
        return c.Vouchers.Single(v => !before.Contains(v.Id));
    }

    /// <summary>Day Book → the voucher's row → Ctrl+Enter (the real alteration arm) → a narration-only edit →
    /// Ctrl+A. Returns the alteration screen, which is still up when the save is refused.</summary>
    private static VoucherEntryViewModel AlterNarrationFromDayBook(Book b, Guid voucherId)
    {
        b.Vm.OpenReport(ReportKind.DayBook);
        Pump(b.Window);
        b.Vm.Reports!.SelectedRow = b.Vm.Reports!.Rows.Single(r => r.DrillVoucherId == voucherId);
        Pump(b.Window);
        Key(b.Window, PhysicalKey.Enter, RawInputModifiers.Control);
        Assert.Equal(Screen.VoucherEntry, b.Vm.CurrentScreen);
        var entry = b.Vm.VoucherEntry!;
        Assert.True(entry.IsAltering);
        entry.Narration = "narration only";
        Pump(b.Window);
        Key(b.Window, PhysicalKey.A, RawInputModifiers.Control);
        return entry;
    }

    private static IEnumerable<Avalonia.Visual> Descendants(Avalonia.Visual v)
    {
        foreach (var c in v.GetVisualChildren())
        {
            yield return c;
            foreach (var g in Descendants(c)) yield return g;
        }
    }

    private static bool Rendered(MainWindow window, string fragment) =>
        Descendants(window).OfType<TextBlock>()
            .Any(t => t.IsEffectivelyVisible && t.Text is { } s && s.Contains(fragment, StringComparison.Ordinal));

    private static TdsLineTax PostedTds(Company c, Guid voucherId) =>
        c.FindVoucher(voucherId)!.Lines.Single(l => l.HasTds).Tds!;

    /// <summary>
    /// §194J(b) 10% → 7.5% BY KEYS over a posted ₹1,23,456.78 journal (₹12,346 withheld). The save's rendered
    /// message warns that the posted voucher keeps the old figures and that altering it is now refused; the voucher,
    /// re-opened from the Day Book by Ctrl+Enter, refuses a narration-only Ctrl+A naming both rates; and the
    /// reloaded book still holds 10% / ₹12,346 and the ORIGINAL narration.
    /// <para>Mutations (verifier, 3b67e76): the <c>Message +=</c> warning append disabled → the warning assertion
    /// reddens.</para>
    /// </summary>
    [AvaloniaFact]
    public void Rate_alteration_by_keys_warns_and_the_posted_voucher_refuses_a_narration_edit_naming_both_rates()
    {
        var b = NewTdsBook("NOP Verify Rate Co");
        try
        {
            var posted = PostFeesJournal(b.Vm, 123_456.78m);
            Assert.Equal(12_346m, PostedTds(Reload(b), posted.Id).TdsAmount.Amount);
            var narrationBefore = Reload(b).FindVoucher(posted.Id)!.Narration;

            var alter = OpenAlteration(b, "194J(b)");
            Assert.Equal("10", alter.RateWithPanText);
            alter.RateWithPanText = "7.5";
            Key(b.Window, PhysicalKey.A, RawInputModifiers.Control);
            Assert.Contains("altered", alter.Message);
            Assert.Contains("1 voucher already posted under this section", alter.Message);
            Assert.True(Rendered(b.Window, "1 voucher already posted under this section"),
                "the posted-voucher warning is not on screen");
            Assert.Equal(750, Reload(b).FindNatureOfPaymentByCode("194J(b)")!.RateWithPanBp);

            var entry = AlterNarrationFromDayBook(b, posted.Id);
            Assert.Equal(Screen.VoucherEntry, b.Vm.CurrentScreen); // refused: the alteration screen is still up
            Assert.Contains("10%", entry.Message);
            Assert.Contains("7.5%", entry.Message);
            Assert.Contains("refused", entry.Message);
            Assert.True(Rendered(b.Window, "now resolves to 7.5%"), "the rate refusal is not on screen");

            var reloaded = Reload(b);
            var tds = PostedTds(reloaded, posted.Id);
            Assert.Equal(1000, tds.RateBasisPoints);
            Assert.Equal(12_346m, tds.TdsAmount.Amount);
            Assert.Equal(narrationBefore, reloaded.FindVoucher(posted.Id)!.Narration);
        }
        finally { Close(b); }
    }

    /// <summary>
    /// §194J(b) cumulative threshold ₹50,000 → ₹1,00,000 BY KEYS over a posted ₹60,000.30 journal (₹6,000
    /// withheld). The save warns; the Day Book Ctrl+Enter → narration-only Ctrl+A is refused and the rendered refusal
    /// names the THRESHOLD ALTERATION as a cause; the reloaded book still holds ₹6,000.
    /// <para>Mutations (verifier, 3b67e76): the threshold clause removed from the ApplyReCarve message → the
    /// "threshold has been altered" assertion reddens; <c>termsMoved</c> narrowed to the two rates only → the
    /// warning assertion reddens.</para>
    /// </summary>
    [AvaloniaFact]
    public void Threshold_alteration_by_keys_warns_and_the_refusal_names_the_threshold_alteration()
    {
        var b = NewTdsBook("NOP Verify Threshold Co");
        try
        {
            var posted = PostFeesJournal(b.Vm, 60_000.30m);
            Assert.Equal(6_000m, PostedTds(Reload(b), posted.Id).TdsAmount.Amount);

            var alter = OpenAlteration(b, "194J(b)");
            Assert.Equal("50000", alter.CumulativeThresholdText);
            alter.CumulativeThresholdText = "100000";
            Key(b.Window, PhysicalKey.A, RawInputModifiers.Control);
            Assert.Contains("1 voucher already posted under this section", alter.Message);
            Assert.True(Rendered(b.Window, "1 voucher already posted under this section"),
                "the posted-voucher warning is not on screen");
            Assert.Equal(100_000m, Reload(b).FindNatureOfPaymentByCode("194J(b)")!.CumulativeThreshold!.Value.Amount);

            var entry = AlterNarrationFromDayBook(b, posted.Id);
            Assert.Equal(Screen.VoucherEntry, b.Vm.CurrentScreen);
            Assert.Contains("threshold has been altered on its Nature of Payment master", entry.Message);
            Assert.True(Rendered(b.Window, "threshold has been altered"), "the threshold refusal is not on screen");

            Assert.Equal(6_000m, PostedTds(Reload(b), posted.Id).TdsAmount.Amount);
        }
        finally { Close(b); }
    }

    /// <summary>
    /// The lock is NOT blanket: a voucher whose deduction the new figures do not move stays alterable. §194J(b)'s
    /// threshold raised ₹50,000 → ₹1,00,000 BY KEYS over a posted ₹1,23,456.78 journal, which is still above the new
    /// threshold, so it re-computes to the same ₹12,346; the Day Book Ctrl+Enter → narration-only Ctrl+A is ACCEPTED,
    /// and the reloaded book carries the new narration and the unchanged deduction. This pins the "wherever the new
    /// rate or threshold would change its deduction" qualifier in the save's warning, and the verifier's correction
    /// of IV-343, which had said EVERY voucher posted under the old figures refuses alteration.
    /// </summary>
    [AvaloniaFact]
    public void Threshold_alteration_leaves_a_voucher_whose_deduction_it_does_not_move_alterable()
    {
        var b = NewTdsBook("NOP Verify Unmoved Co");
        try
        {
            var posted = PostFeesJournal(b.Vm, 123_456.78m);
            Assert.Equal(12_346m, PostedTds(Reload(b), posted.Id).TdsAmount.Amount);

            var alter = OpenAlteration(b, "194J(b)");
            alter.CumulativeThresholdText = "100000";
            Key(b.Window, PhysicalKey.A, RawInputModifiers.Control);
            Assert.Contains("1 voucher already posted under this section", alter.Message);
            Assert.Contains("wherever the new rate or threshold would change its deduction", alter.Message);

            AlterNarrationFromDayBook(b, posted.Id);
            Assert.NotEqual(Screen.VoucherEntry, b.Vm.CurrentScreen); // accepted: the alteration column popped

            var reloaded = Reload(b);
            Assert.Equal("narration only", reloaded.FindVoucher(posted.Id)!.Narration);
            var tds = PostedTds(reloaded, posted.Id);
            Assert.Equal(1000, tds.RateBasisPoints);
            Assert.Equal(12_346m, tds.TdsAmount.Amount);
        }
        finally { Close(b); }
    }

    /// <summary>
    /// Review F3: a CANCELLED voucher still carries its TDS line, so it still blocks deleting the custom nature it
    /// was withheld under (IV-342). Posted through the voucher screen, cancelled from the Day Book by Alt+X then Y,
    /// then Alt+D then Y on the nature: refused by name, and the reloaded book holds both the nature and the
    /// cancelled voucher's line pointing at it.
    /// <para>Mutation (verifier): <c>NatureOfPaymentRules.VouchersCarrying</c> filtering <c>!v.Cancelled</c> → the
    /// nature is deleted and the assertions redden.</para>
    /// </summary>
    [AvaloniaFact]
    public void A_cancelled_voucher_still_blocks_deleting_the_nature_it_withheld_under()
    {
        var b = NewTdsBook("NOP Verify Cancelled Co");
        try
        {
            // A custom nature through the screen's own Create path.
            b.Vm.ShowGateway();
            Pump(b.Window);
            ArrowToAndEnter(b.Window, b.Vm, "Create");
            ArrowToAndEnter(b.Window, b.Vm, "Nature of Payment");
            var m = b.Vm.NatureOfPaymentMaster!;
            m.SectionCode = "194KK"; m.Name = "Custom 194KK"; m.FvuSectionCode = "94KK"; m.RateWithPanText = "5";
            Key(b.Window, PhysicalKey.A, RawInputModifiers.Control);
            var c = b.Vm.Company!;
            var nature = c.FindNatureOfPaymentByCode("194KK");
            Assert.True(nature is not null, m.Message);
            var fees = c.FindLedgerByName("Professional Fees")!;
            fees.TdsNatureOfPaymentId = nature!.Id;
            var posted = PostFeesJournal(b.Vm, 123_456.78m);
            fees.TdsNatureOfPaymentId = null; // only the posted (soon cancelled) line names it now
            new CompanyStorage(b.Dir).Save(c);

            b.Vm.OpenReport(ReportKind.DayBook);
            Pump(b.Window);
            b.Vm.Reports!.SelectedRow = b.Vm.Reports!.Rows.Single(r => r.DrillVoucherId == posted.Id);
            Pump(b.Window);
            Key(b.Window, PhysicalKey.X, RawInputModifiers.Alt);
            Key(b.Window, PhysicalKey.Y);
            Assert.True(Reload(b).FindVoucher(posted.Id)!.Cancelled, b.Vm.Notice);

            b.Vm.ShowGateway();
            Pump(b.Window);
            ArrowToAndEnter(b.Window, b.Vm, "Create");
            ArrowToAndEnter(b.Window, b.Vm, "Nature of Payment");
            var bound = (b.Vm.NatureOfPaymentMaster?.Natures.Count ?? 0) + 2;
            for (var i = 0; i < bound && b.Vm.NatureOfPaymentMaster?.HighlightedRow?.SectionCode != "194KK"; i++)
                Key(b.Window, PhysicalKey.ArrowDown);
            Assert.False(b.Vm.NatureOfPaymentMaster!.IsAltering);
            Key(b.Window, PhysicalKey.D, RawInputModifiers.Alt);
            Key(b.Window, PhysicalKey.Y);
            Assert.Contains("194KK", b.Vm.Notice);
            Assert.Contains("1 posted voucher", b.Vm.Notice);

            var reloaded = Reload(b);
            var kept = reloaded.FindNatureOfPaymentByCode("194KK");
            Assert.NotNull(kept);
            Assert.True(reloaded.FindVoucher(posted.Id)!.Cancelled);
            Assert.Equal(kept!.Id, PostedTds(reloaded, posted.Id).NatureId);
        }
        finally { Close(b); }
    }
}
