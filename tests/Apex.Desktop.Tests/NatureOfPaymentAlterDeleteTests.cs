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
/// 🔴 <b>CENSUS 6.28 (WAVE 12, TRACK hf) — ALTER AND DELETE ON THE NATURE OF PAYMENT (TDS SECTION) MASTER.</b>
/// The master was create-only and its predefined rows immutable by design (T1-21), so a wrong seeded rate had no
/// remedy in-app. Every test here drives a realised <see cref="MainWindow"/> through its real key handler
/// (arrows, Ctrl+Enter, Ctrl+A, Alt+D, Y) and asserts on a book reloaded through a FRESH
/// <see cref="CompanyStorage"/>, or on the rendered window — never on a view-model field the test itself set.
///
/// <para><b>FIDELITY (R7, ruling 14), read by content 2026-11-22.</b> <c>help.tallysolutions.com/tds-faq-tally/</c>
/// tells the user to <i>"Manually change the No PAN rate to 5%"</i> on the predefined "Purchase of Any Goods"
/// nature and to set the §194Q threshold <i>"in the Nature of Payment master"</i> — so predefined figures are
/// operator-editable; and on changing Section / Payment code it warns the change <i>"will affect the Payment
/// Entries and return filing process"</i> and recommends a new Nature of Payment instead. No vendor page speaks to
/// DELETING a nature: every deletion rule is ours (docs/invented-vs-cloned.md §2AG).</para>
///
/// <para>Menu walks are bounded (T2-169): every loop stops at the active column's item count plus two, or a fixed
/// small bound on the existing-list, and fails naming the label it was looking for.</para>
/// </summary>
public sealed class NatureOfPaymentAlterDeleteTests
{
    private const string ValidTan = "MUMA12345B";
    private const string DeducteePan = "AAPFU0939F";
    private const decimal Gross = 123_456.78m; // odd paisa, far above §194J(b)'s ₹50,000 aggregate

    // ======================================================================================= harness

    private sealed record Book(MainWindow Window, MainWindowViewModel Vm, string Dir, string Name);

    /// <summary>A TDS-enabled company with a "Professional Fees" expense ledger defaulting to §194J(b) and a
    /// deductee vendor with a PAN — the shape <c>TdsVoucherEntryViewModelTests</c> posts against.</summary>
    private static Book NewTdsBook(string name)
    {
        var dir = Path.Combine(Path.GetTempPath(), "ApexW12hf_" + Guid.NewGuid().ToString("N"));
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

    /// <summary>The company as STORED: a brand-new <see cref="CompanyStorage"/> over the same directory, so nothing
    /// the screen holds in memory can answer for the disk.</summary>
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

    /// <summary>The real keyboard route: Gateway → Create → Statutory Masters → Nature of Payment.</summary>
    private static NatureOfPaymentMasterViewModel OpenMaster(Book b)
    {
        b.Vm.ShowGateway();
        Pump(b.Window);
        ArrowToAndEnter(b.Window, b.Vm, "Create");
        ArrowToAndEnter(b.Window, b.Vm, "Nature of Payment");
        Assert.Equal(Screen.NatureOfPaymentMaster, b.Vm.CurrentScreen);
        Assert.Same(b.Vm.NatureOfPaymentMaster, b.Vm.MasterListScreen);
        return b.Vm.NatureOfPaymentMaster!;
    }

    private static void ArrowToSection(Book b, string section)
    {
        var bound = (b.Vm.NatureOfPaymentMaster?.Natures.Count ?? 0) + 2;
        for (var i = 0; i < bound; i++)
        {
            if (b.Vm.NatureOfPaymentMaster?.HighlightedRow?.SectionCode == section) return;
            Key(b.Window, PhysicalKey.ArrowDown);
        }
        Assert.Fail($"Section '{section}' was not reachable by arrow navigation on the Nature of Payment list.");
    }

    /// <summary>Creates a custom nature through the screen's own Create path (Ctrl+A).</summary>
    private static void CreateCustom(Book b, string section, string fvu, string rate)
    {
        var m = OpenMaster(b);
        m.SectionCode = section;
        m.Name = "Custom " + section;
        m.FvuSectionCode = fvu;
        m.RateWithPanText = rate;
        Key(b.Window, PhysicalKey.A, RawInputModifiers.Control);
        Assert.True(b.Vm.Company!.FindNatureOfPaymentByCode(section) is not null, m.Message);
    }

    /// <summary>Posts Dr Fees GROSS / Cr Vendor GROSS as a Journal through the voucher screen; TDS is carved by the
    /// engine from the fees ledger's default nature. Returns the TDS amount the screen showed before Accept.</summary>
    private static string PostFeesJournal(MainWindowViewModel vm)
    {
        var c = vm.Company!;
        var fees = c.FindLedgerByName("Professional Fees")!;
        var vendor = c.FindLedgerByName("Acme Consultants")!;
        vm.OpenVoucher(VoucherBaseType.Journal);
        var e = vm.VoucherEntry!;
        var gross = Gross.ToString(CultureInfo.InvariantCulture);
        e.Lines[0].SelectedLedger = fees; e.Lines[0].Side = DrCr.Debit; e.Lines[0].AmountText = gross;
        e.Lines[1].SelectedLedger = vendor; e.Lines[1].Side = DrCr.Credit; e.Lines[1].AmountText = gross;
        e.Recalculate();
        Assert.True(e.ShowTdsPanel);
        var shown = e.TdsAmountText;
        Assert.True(e.Accept());
        return shown;
    }

    private static IEnumerable<Avalonia.Visual> Descendants(Avalonia.Visual v)
    {
        foreach (var c in v.GetVisualChildren())
        {
            yield return c;
            foreach (var g in Descendants(c)) yield return g;
        }
    }

    // ======================================================================================= ALTER

    /// <summary>
    /// 🔴 <b>THE ROW'S HEADLINE: a predefined section's rate is altered by keys, survives a fresh store, and is the
    /// rate a TDS voucher posted AFTERWARDS computes with.</b> §194J(b) 10% → 7.5% with PAN and 20% → 15% without.
    /// On ₹1,23,456.78 the deduction is ₹9,259 (7.5%, rounded to the rupee), not the seeded ₹12,346.
    /// <para>Mutation: <c>TdsConfig.ReplaceNatureOfPayment</c> body made a no-op → the reloaded rate stays 1000 and
    /// the posted TDS is 12,346 (red).</para>
    /// </summary>
    [AvaloniaFact]
    public void Altered_predefined_rate_is_read_back_from_a_fresh_store_and_drives_the_next_TDS_deduction()
    {
        var b = NewTdsBook("NOP Alter Rate Co");
        try
        {
            var id = b.Vm.Company!.FindNatureOfPaymentByCode("194J(b)")!.Id;
            OpenMaster(b);
            ArrowToSection(b, "194J(b)");
            Key(b.Window, PhysicalKey.Enter, RawInputModifiers.Control);

            var alter = b.Vm.NatureOfPaymentMaster!;
            Assert.True(alter.IsAltering);
            Assert.Equal("Nature of Payment Alteration", alter.Caption);
            Assert.Equal("10", alter.RateWithPanText);   // pre-filled from the stored master
            alter.RateWithPanText = "7.5";
            alter.RateWithoutPanText = "15";
            Key(b.Window, PhysicalKey.A, RawInputModifiers.Control);
            Assert.Contains("altered", alter.Message);

            // The STORED master: same id (ledgers and posted lines still resolve), new rates, still predefined.
            var stored = Reload(b).NaturesOfPayment.Single(n => n.SectionCode == "194J(b)");
            Assert.Equal(id, stored.Id);
            Assert.Equal(750, stored.RateWithPanBp);
            Assert.Equal(1500, stored.RateWithoutPanBp);
            Assert.True(stored.IsPredefined);
            Assert.Equal(1, Reload(b).NaturesOfPayment.Count(n => n.SectionCode == "194J(b)"));

            // A SECOND shell over a FRESH store opens the saved book and posts through the voucher screen.
            var vm2 = new MainWindowViewModel(new CompanyStorage(b.Dir));
            vm2.ShowCompanySelect();
            var bound = vm2.Menu.Count + 2;
            var opened = false;
            for (var i = 0; i < bound && !opened; i++)
            {
                if (vm2.Menu[vm2.SelectedIndex].Label == b.Name) { vm2.Menu[vm2.SelectedIndex].Activate(); opened = true; }
                else vm2.MoveDown();
            }
            Assert.True(opened, $"'{b.Name}' was not reachable on Company Select.");
            Assert.Equal("9,259.00", PostFeesJournal(vm2));

            // And the posted deduction, read back off a THIRD fresh store.
            var tds = Reload(b).Vouchers.SelectMany(v => v.Lines).Single(l => l.Tds is { TdsAmount.Amount: > 0m }).Tds!;
            Assert.Equal(id, tds.NatureId);
            Assert.Equal(750, tds.RateBasisPoints);
            Assert.Equal(9_259m, tds.TdsAmount.Amount);
            Assert.Equal(Gross, tds.AssessableValue.Amount);
        }
        finally { Close(b); }
    }

    /// <summary>
    /// The threshold of a predefined section is alterable and persists (the vendor's §194Q advice is exactly
    /// this), and the altered threshold is the one the engine applies: raised to ₹2,00,000, the same ₹1,23,456.78
    /// journal is BELOW threshold and posts no deduction.
    /// <para>Mutation: <c>Alter</c> passing the existing <c>CumulativeThreshold</c> instead of the parsed one →
    /// the stored threshold stays ₹50,000 and TDS is deducted (red).</para>
    /// </summary>
    [AvaloniaFact]
    public void Altered_threshold_persists_and_the_engine_applies_it()
    {
        var b = NewTdsBook("NOP Alter Threshold Co");
        try
        {
            OpenMaster(b);
            ArrowToSection(b, "194J(b)");
            Key(b.Window, PhysicalKey.Enter, RawInputModifiers.Control);
            var alter = b.Vm.NatureOfPaymentMaster!;
            Assert.Equal("50000", alter.CumulativeThresholdText);
            alter.CumulativeThresholdText = "200000";
            Key(b.Window, PhysicalKey.A, RawInputModifiers.Control);

            Assert.Equal(200_000m, Reload(b).FindNatureOfPaymentByCode("194J(b)")!.CumulativeThreshold!.Value.Amount);
            Assert.Equal("0.00", PostFeesJournal(b.Vm));
            Assert.DoesNotContain(Reload(b).Vouchers.SelectMany(v => v.Lines), l => l.Tds is { TdsAmount.Amount: > 0m });
        }
        finally { Close(b); }
    }

    /// <summary>
    /// A predefined section's section code and Form-26Q code are LOCKED on alteration (ours): the realised
    /// TextBoxes are read-only, and a code change forced past the form is refused by name with nothing stored.
    /// <para>Mutation: deleting the <c>existing.IsPredefined</c> branch in
    /// <c>NatureOfPaymentRules.EnsureAlterable</c> → the forced code is stored (red).</para>
    /// </summary>
    [AvaloniaFact]
    public void Predefined_section_and_payment_codes_are_locked_and_a_forced_change_is_refused_by_name()
    {
        var b = NewTdsBook("NOP Code Lock Co");
        try
        {
            OpenMaster(b);
            ArrowToSection(b, "194J(b)");
            Key(b.Window, PhysicalKey.Enter, RawInputModifiers.Control);
            var alter = b.Vm.NatureOfPaymentMaster!;
            Assert.True(alter.IsCodeLocked);
            var locked = Descendants(b.Window).OfType<TextBox>()
                .Where(t => t.IsEffectivelyVisible && (t.Text == "194J(b)" || t.Text == "94J-B")).ToList();
            Assert.Equal(2, locked.Count);
            Assert.All(locked, t => Assert.True(t.IsReadOnly));

            alter.SectionCode = "194J(z)";
            Key(b.Window, PhysicalKey.A, RawInputModifiers.Control);
            Assert.Contains("predefined statutory section", alter.Message);
            Assert.Contains("194J(b)", alter.Message);
            Assert.NotNull(Reload(b).FindNatureOfPaymentByCode("194J(b)"));
            Assert.Null(Reload(b).FindNatureOfPaymentByCode("194J(z)"));
        }
        finally { Close(b); }
    }

    /// <summary>
    /// A CUSTOM nature that posted TDS lines carry cannot have its section code changed (vendor FAQ: the change
    /// would affect payment entries and return filing); the refusal names the posted voucher. Its RATE can still be
    /// altered, and the posted voucher keeps the amount it was posted with.
    /// <para>Mutation: deleting the posted-voucher branch of <c>EnsureAlterable</c> → the new code is stored
    /// (red).</para>
    /// </summary>
    [AvaloniaFact]
    public void Custom_nature_with_posted_TDS_refuses_a_code_change_naming_the_voucher_but_accepts_a_rate_change()
    {
        var b = NewTdsBook("NOP Custom Posted Co");
        try
        {
            CreateCustom(b, "194ZZ", "94ZZ", "4");
            var c = b.Vm.Company!;
            c.FindLedgerByName("Professional Fees")!.TdsNatureOfPaymentId = c.FindNatureOfPaymentByCode("194ZZ")!.Id;
            Assert.Equal("4,938.00", PostFeesJournal(b.Vm)); // 4% of 1,23,456.78 = 4,938.27 → ₹4,938

            OpenMaster(b);
            ArrowToSection(b, "194ZZ");
            Key(b.Window, PhysicalKey.Enter, RawInputModifiers.Control);
            var alter = b.Vm.NatureOfPaymentMaster!;
            Assert.False(alter.IsCodeLocked);
            alter.SectionCode = "194YY";
            Key(b.Window, PhysicalKey.A, RawInputModifiers.Control);
            Assert.Contains("Cannot change the section code", alter.Message);
            Assert.Contains("Journal", alter.Message);
            Assert.Contains("1 posted voucher", alter.Message);
            Assert.NotNull(Reload(b).FindNatureOfPaymentByCode("194ZZ"));
            Assert.Null(Reload(b).FindNatureOfPaymentByCode("194YY"));

            alter.SectionCode = "194ZZ";
            alter.RateWithPanText = "3";
            Key(b.Window, PhysicalKey.A, RawInputModifiers.Control);
            var reloaded = Reload(b);
            Assert.Equal(300, reloaded.FindNatureOfPaymentByCode("194ZZ")!.RateWithPanBp);
            var posted = reloaded.Vouchers.SelectMany(v => v.Lines).Single(l => l.Tds is { TdsAmount.Amount: > 0m }).Tds!;
            Assert.Equal(400, posted.RateBasisPoints);
            Assert.Equal(4_938m, posted.TdsAmount.Amount);
        }
        finally { Close(b); }
    }

    // ======================================================================================= DELETE

    /// <summary>
    /// Alt+D then Y on a PREDEFINED section is refused by name and nothing leaves the book.
    /// <para>Mutation: deleting the <c>nature.IsPredefined</c> branch of <c>EnsureDeletable</c> → §194C is gone
    /// from the reloaded book (red).</para>
    /// </summary>
    [AvaloniaFact]
    public void Delete_of_a_predefined_section_is_refused_by_name()
    {
        var b = NewTdsBook("NOP Delete Predefined Co");
        try
        {
            OpenMaster(b);
            ArrowToSection(b, "194C");
            Key(b.Window, PhysicalKey.D, RawInputModifiers.Alt);
            Key(b.Window, PhysicalKey.Y);
            Assert.Contains("194C", b.Vm.Notice);
            Assert.Contains("predefined statutory section", b.Vm.Notice);
            Assert.NotNull(Reload(b).FindNatureOfPaymentByCode("194C"));
        }
        finally { Close(b); }
    }

    /// <summary>
    /// A custom nature a LEDGER names as its default is refused by name (the ledger is named); an UNUSED custom
    /// nature is deleted and is absent from a fresh store, and the screen's list no longer shows it.
    /// <para>Mutation: deleting the ledger branch of <c>EnsureDeletable</c> → "194LL" is deleted while
    /// "Rent Payable" still names it (red).</para>
    /// </summary>
    [AvaloniaFact]
    public void Delete_refuses_a_nature_a_ledger_names_and_deletes_an_unused_custom_nature()
    {
        var b = NewTdsBook("NOP Delete Ledger Co");
        try
        {
            CreateCustom(b, "194LL", "94LL", "2");
            CreateCustom(b, "194UU", "94UU", "1");
            var c = b.Vm.Company!;
            var rent = AddLedger(c, "Rent Payable", "Indirect Expenses", true);
            rent.TdsNatureOfPaymentId = c.FindNatureOfPaymentByCode("194LL")!.Id;
            new CompanyStorage(b.Dir).Save(c);

            OpenMaster(b);
            ArrowToSection(b, "194LL");
            Key(b.Window, PhysicalKey.D, RawInputModifiers.Alt);
            Key(b.Window, PhysicalKey.Y);
            Assert.Contains("194LL", b.Vm.Notice);
            Assert.Contains("'Rent Payable'", b.Vm.Notice);
            Assert.NotNull(Reload(b).FindNatureOfPaymentByCode("194LL"));

            ArrowToSection(b, "194UU");
            Key(b.Window, PhysicalKey.D, RawInputModifiers.Alt);
            Key(b.Window, PhysicalKey.Y);
            Assert.Contains("deleted", b.Vm.Notice);
            Assert.Null(Reload(b).FindNatureOfPaymentByCode("194UU"));
            Assert.NotNull(Reload(b).FindNatureOfPaymentByCode("194LL"));
            Assert.DoesNotContain(b.Vm.NatureOfPaymentMaster!.Natures, r => r.SectionCode == "194UU");
        }
        finally { Close(b); }
    }

    /// <summary>
    /// A custom nature a POSTED TDS line carries is refused by name even after every ledger stops naming it — the
    /// refusal names the voucher, and the nature survives a fresh store so Form 26Q can still resolve it.
    /// <para>Mutation: deleting the posted-voucher branch of <c>EnsureDeletable</c> → the nature is deleted and
    /// the posted line's <c>NatureId</c> resolves to nothing (red).</para>
    /// </summary>
    [AvaloniaFact]
    public void Delete_refuses_a_nature_a_posted_TDS_line_carries_naming_the_voucher()
    {
        var b = NewTdsBook("NOP Delete Posted Co");
        try
        {
            CreateCustom(b, "194PP", "94PP", "5");
            var c = b.Vm.Company!;
            var fees = c.FindLedgerByName("Professional Fees")!;
            fees.TdsNatureOfPaymentId = c.FindNatureOfPaymentByCode("194PP")!.Id;
            PostFeesJournal(b.Vm);
            fees.TdsNatureOfPaymentId = null; // no ledger names it any more: only the posted line does
            new CompanyStorage(b.Dir).Save(c);

            OpenMaster(b);
            ArrowToSection(b, "194PP");
            Key(b.Window, PhysicalKey.D, RawInputModifiers.Alt);
            Key(b.Window, PhysicalKey.Y);
            Assert.Contains("194PP", b.Vm.Notice);
            Assert.Contains("1 posted voucher", b.Vm.Notice);
            Assert.Contains("Journal", b.Vm.Notice);

            var reloaded = Reload(b);
            var nature = reloaded.FindNatureOfPaymentByCode("194PP");
            Assert.NotNull(nature);
            Assert.Contains(reloaded.Vouchers.SelectMany(v => v.Lines), l => l.Tds is { } t && t.NatureId == nature!.Id);
        }
        finally { Close(b); }
    }

    /// <summary>Alt+D is refused while the screen is ALTERING (the shared arm's <c>IsAltering</c> gate): deleting
    /// the master the open form is editing is never what was meant.</summary>
    [AvaloniaFact]
    public void Alt_D_is_inert_on_the_alteration_screen()
    {
        var b = NewTdsBook("NOP Alter Gate Co");
        try
        {
            CreateCustom(b, "194GG", "94GG", "1");
            OpenMaster(b);
            ArrowToSection(b, "194GG");
            Key(b.Window, PhysicalKey.Enter, RawInputModifiers.Control);
            Assert.True(b.Vm.NatureOfPaymentMaster!.IsAltering);
            Assert.False(b.Vm.IsDeleteTargetPage);
            Key(b.Window, PhysicalKey.D, RawInputModifiers.Alt);
            Key(b.Window, PhysicalKey.Y);
            Assert.NotNull(Reload(b).FindNatureOfPaymentByCode("194GG"));
        }
        finally { Close(b); }
    }
}
