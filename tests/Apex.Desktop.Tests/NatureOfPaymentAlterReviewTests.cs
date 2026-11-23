using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
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
/// Census 6.28 (wave 12, track hf) — the adversarial reviewer's INDEPENDENT reproduction, on a fixture the builder
/// did not use: §194C (not §194J(b)), a deductee with NO PAN (so the §206AA no-PAN rate is the figure altered), a
/// Payment-shaped journal, a Name change on a predefined section, and Alt+D answered N before Y. Driven through the
/// realised <see cref="MainWindow"/>'s key handler; every assertion reads a FRESH <see cref="CompanyStorage"/>.
/// Menu walks are bounded (T2-169).
/// </summary>
public sealed class NatureOfPaymentAlterReviewTests
{
    private const decimal Gross = 45_678.91m; // above §194C's ₹30,000 single-transaction threshold, odd paisa

    private sealed record Book(MainWindow Window, MainWindowViewModel Vm, string Dir, string Name);

    private static Book NewBook(string name)
    {
        var dir = Path.Combine(Path.GetTempPath(), "ApexW12hfRev_" + Guid.NewGuid().ToString("N"));
        var storage = new CompanyStorage(dir);
        var vm = new MainWindowViewModel(storage);
        vm.NewCompanyName = name;
        vm.CreateCompany();
        var c = vm.Company!;
        new TdsTcsService(c).EnableTds(new TdsConfig { Tan = "MUMA12345B" });
        var work = new DomainLedger(Guid.NewGuid(), "Civil Works", c.FindGroupByName("Indirect Expenses")!.Id, Money.Zero, true);
        c.AddLedger(work);
        work.TdsApplicable = true;
        work.TdsNatureOfPaymentId = c.FindNatureOfPaymentByCode("194C")!.Id;
        var contractor = new DomainLedger(Guid.NewGuid(), "Ravi Contractor", c.FindGroupByName("Sundry Creditors")!.Id, Money.Zero, false);
        c.AddLedger(contractor);
        contractor.DeducteeType = DeducteeType.Individual;
        contractor.PartyPan = null; // no PAN: §206AA rate applies
        storage.Save(c);
        vm.ShowGateway();
        var window = new MainWindow { DataContext = vm };
        window.Show();
        Pump(window);
        return new Book(window, vm, dir, name);
    }

    private static void Pump(MainWindow w)
    {
        Dispatcher.UIThread.RunJobs();
        w.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }

    private static void Key(Book b, PhysicalKey key, RawInputModifiers mods = RawInputModifiers.None)
    {
        b.Window.KeyPressQwerty(key, mods);
        Pump(b.Window);
    }

    private static void Close(Book b)
    {
        b.Window.Close();
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(b.Dir)) Directory.Delete(b.Dir, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static Company Reload(Book b)
    {
        var fresh = new CompanyStorage(b.Dir);
        return fresh.Load(fresh.ListCompanies().Single(e => e.Name == b.Name));
    }

    private static void ArrowToAndEnter(Book b, string label)
    {
        var bound = b.Vm.Columns[b.Vm.ActiveColumnIndex].Items.Count + 2;
        for (var i = 0; i < bound; i++)
        {
            if (b.Vm.Columns[b.Vm.ActiveColumnIndex].Selected?.Label == label) { Key(b, PhysicalKey.Enter); return; }
            Key(b, PhysicalKey.ArrowDown);
        }
        Assert.Fail($"'{label}' was not reachable by arrow navigation.");
    }

    private static void OpenMaster(Book b)
    {
        b.Vm.ShowGateway();
        Pump(b.Window);
        ArrowToAndEnter(b, "Create");
        ArrowToAndEnter(b, "Nature of Payment");
        Assert.Equal(Screen.NatureOfPaymentMaster, b.Vm.CurrentScreen);
    }

    private static void ArrowToSection(Book b, string section)
    {
        var bound = (b.Vm.NatureOfPaymentMaster?.Natures.Count ?? 0) + 2;
        for (var i = 0; i < bound; i++)
        {
            if (b.Vm.NatureOfPaymentMaster?.HighlightedRow?.SectionCode == section) return;
            Key(b, PhysicalKey.ArrowDown);
        }
        Assert.Fail($"Section '{section}' was not reachable on the Nature of Payment list.");
    }

    /// <summary>
    /// §194C's no-PAN rate 20% → 5% and its name changed, by keys; the reloaded master keeps its id, is still
    /// predefined, carries both new terms; and a contractor with no PAN is then withheld at 5%:
    /// 5% of ₹45,678.91 = ₹2,283.95 → ₹2,284 (not the seeded 20% = ₹9,136).
    /// </summary>
    [AvaloniaFact]
    public void No_PAN_rate_and_name_of_a_predefined_section_are_altered_by_keys_and_drive_the_next_deduction()
    {
        var b = NewBook("Rev NOP 194C Co");
        try
        {
            var id = b.Vm.Company!.FindNatureOfPaymentByCode("194C")!.Id;
            OpenMaster(b);
            ArrowToSection(b, "194C");
            Key(b, PhysicalKey.Enter, RawInputModifiers.Control);
            var alter = b.Vm.NatureOfPaymentMaster!;
            Assert.True(alter.IsAltering);
            Assert.Equal("20", alter.RateWithoutPanText);
            alter.RateWithoutPanText = "5";
            alter.Name = "Works contract (reviewed)";
            Key(b, PhysicalKey.A, RawInputModifiers.Control);
            Assert.Contains("altered", alter.Message);
            // The alteration screen's own list shows the new rate (RefreshList after the save).
            Assert.Contains(alter.Natures, r => r.SectionCode == "194C" && r.Name == "Works contract (reviewed)");

            var stored = Reload(b).FindNatureOfPaymentByCode("194C")!;
            Assert.Equal(id, stored.Id);
            Assert.True(stored.IsPredefined);
            Assert.Equal(500, stored.RateWithoutPanBp);
            Assert.Equal("Works contract (reviewed)", stored.Name);
            Assert.Equal(1, Reload(b).NaturesOfPayment.Count(n => n.SectionCode == "194C"));

            // Post through the voucher screen on a SECOND shell over a fresh store.
            var vm2 = new MainWindowViewModel(new CompanyStorage(b.Dir));
            vm2.ShowCompanySelect();
            var opened = false;
            for (var i = 0; i < vm2.Menu.Count + 2 && !opened; i++)
            {
                if (vm2.Menu[vm2.SelectedIndex].Label == b.Name) { vm2.Menu[vm2.SelectedIndex].Activate(); opened = true; }
                else vm2.MoveDown();
            }
            Assert.True(opened, $"'{b.Name}' was not reachable on Company Select.");
            var c2 = vm2.Company!;
            vm2.OpenVoucher(VoucherBaseType.Journal);
            var e = vm2.VoucherEntry!;
            var gross = Gross.ToString(CultureInfo.InvariantCulture);
            e.Lines[0].SelectedLedger = c2.FindLedgerByName("Civil Works"); e.Lines[0].Side = DrCr.Debit; e.Lines[0].AmountText = gross;
            e.Lines[1].SelectedLedger = c2.FindLedgerByName("Ravi Contractor"); e.Lines[1].Side = DrCr.Credit; e.Lines[1].AmountText = gross;
            e.Recalculate();
            Assert.True(e.ShowTdsPanel);
            Assert.True(e.Accept(), e.Message);

            var tds = Reload(b).Vouchers.SelectMany(v => v.Lines).Single(l => l.Tds is { TdsAmount.Amount: > 0m }).Tds!;
            Assert.Equal(id, tds.NatureId);
            Assert.False(tds.PanApplied);
            Assert.Equal(500, tds.RateBasisPoints);
            Assert.Equal(2_284m, tds.TdsAmount.Amount);
        }
        finally { Close(b); }
    }

    /// <summary>Alt+D answered N leaves an unused custom nature in the book; Alt+D answered Y removes it.</summary>
    [AvaloniaFact]
    public void Alt_D_answered_N_keeps_the_nature_and_Y_deletes_it()
    {
        var b = NewBook("Rev NOP Delete Co");
        try
        {
            OpenMaster(b);
            var m = b.Vm.NatureOfPaymentMaster!;
            m.SectionCode = "194RV";
            m.Name = "Reviewer custom";
            m.FvuSectionCode = "94RV";
            m.RateWithPanText = "1.5";
            Key(b, PhysicalKey.A, RawInputModifiers.Control);
            Assert.NotNull(Reload(b).FindNatureOfPaymentByCode("194RV"));

            ArrowToSection(b, "194RV");
            Key(b, PhysicalKey.D, RawInputModifiers.Alt);
            Key(b, PhysicalKey.N);
            Assert.NotNull(Reload(b).FindNatureOfPaymentByCode("194RV"));

            ArrowToSection(b, "194RV");
            Key(b, PhysicalKey.D, RawInputModifiers.Alt);
            Key(b, PhysicalKey.Y);
            Assert.Null(Reload(b).FindNatureOfPaymentByCode("194RV"));
            Assert.NotNull(Reload(b).FindNatureOfPaymentByCode("194C"));
        }
        finally { Close(b); }
    }
}
