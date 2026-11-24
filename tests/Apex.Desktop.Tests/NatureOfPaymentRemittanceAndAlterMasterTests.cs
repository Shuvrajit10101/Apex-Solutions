using System;
using System.Collections.Generic;
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
/// 🔴 <b>CENSUS 6.28 CLAUSES (i) AND (iii), 6.36's ROUTE, AND T2-181 (WAVE 13, TRACK ic)</b>, each driven through a
/// realised <see cref="MainWindow"/> and its real key handler, and asserted on a book reloaded through a FRESH
/// <see cref="CompanyStorage"/> or on the shell's own state.
///
/// <list type="bullet">
///   <item><b>(i) Nature of Remittance</b> — help.tallysolutions.com/tds-faq-tally/: <i>"manually enter the applicable
///     Nature of Remittance in the Nature of Payment master."</i></item>
///   <item><b>(iii) Go To &gt; Alter Master</b> — help.tallysolutions.com/tally-prime/india-tds/form-27q-tally/:
///     <i>"Press Alt+G (Go To) &gt; Create Master/Alter Master &gt; TDS Nature of Payments."</i>; and
///     help.tallysolutions.com/tcs-faq-tally/: <i>"Press Alt+G (Go To) &gt; Alter Master &gt; TCS Nature of Goods and
///     press Enter."</i> (both read by content 2026-11-24).</item>
///   <item><b>T2-181</b> — the alteration warning counts only the live vouchers the new figures move, and the
///     rollback and the highlight-restore are pinned.</item>
/// </list>
/// Menu walks and list walks are bounded (T2-169).
/// </summary>
public sealed class NatureOfPaymentRemittanceAndAlterMasterTests
{
    private const string ValidTan = "MUMA12345B";

    private sealed record Book(MainWindow Window, MainWindowViewModel Vm, string Dir, string Name);

    private static Book NewBook(string name, bool tds = true, bool tcs = true)
    {
        var dir = Path.Combine(Path.GetTempPath(), "ApexW13icN_" + Guid.NewGuid().ToString("N"));
        var storage = new CompanyStorage(dir);
        var vm = new MainWindowViewModel(storage);
        vm.NewCompanyName = name;
        vm.CreateCompany();
        var c = vm.Company!;
        if (tds) new TdsTcsService(c).EnableTds(new TdsConfig { Tan = ValidTan });
        if (tcs) new TdsTcsService(c).EnableTcs(new TcsConfig { Tan = ValidTan });
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

    private static NatureOfPaymentMasterViewModel OpenNop(Book b)
    {
        b.Vm.ShowGateway();
        Pump(b.Window);
        ArrowToAndEnter(b.Window, b.Vm, "Create");
        ArrowToAndEnter(b.Window, b.Vm, "Nature of Payment");
        Assert.Equal(Screen.NatureOfPaymentMaster, b.Vm.CurrentScreen);
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

    private static void CreateCustomNop(Book b, string section)
    {
        var m = OpenNop(b);
        m.SectionCode = section;
        m.Name = "Custom " + section;
        m.FvuSectionCode = "9" + section;
        m.RateWithPanText = "1";
        Key(b.Window, PhysicalKey.A, RawInputModifiers.Control);
        Assert.True(b.Vm.Company!.FindNatureOfPaymentByCode(section) is not null, m.Message);
    }

    /// <summary>Alt+G, type into the realised search box, arrow to <paramref name="label"/> (bounded), Enter.</summary>
    private static void GoTo(Book b, string search, string label)
    {
        Key(b.Window, PhysicalKey.G, RawInputModifiers.Alt);
        Assert.True(b.Vm.IsGoToOpen);
        var box = Descendants(b.Window).OfType<TextBox>().First(t => t.Name == "GoToSearchBox");
        box.Text = search;
        Pump(b.Window);
        var bound = b.Vm.GoTo!.Results.Count + 2;
        for (var i = 0; i < bound; i++)
        {
            if (b.Vm.GoTo?.Selected?.Label == label) { Key(b.Window, PhysicalKey.Enter); return; }
            Key(b.Window, PhysicalKey.ArrowDown);
        }
        Assert.Fail($"Go To had no reachable result '{label}' for '{search}'.");
    }

    private static IEnumerable<Avalonia.Visual> Descendants(Avalonia.Visual v)
    {
        foreach (var c in v.GetVisualChildren())
        {
            yield return c;
            foreach (var g in Descendants(c)) yield return g;
        }
    }

    // ============================================================================ (i) Nature of Remittance

    /// <summary>
    /// 🔴 <b>CAPTURED ON CREATE AND ON ALTER, BY KEYS, AND READ BACK FROM A FRESH STORE.</b> A custom nature created with
    /// remittance "27" stores "27"; the predefined 194J(b), opened by Ctrl+Enter, shows a blank field, is given "16" and
    /// stores "16"; a non-numeric value is refused by name and nothing is stored. The realised form carries the field.
    /// <para>Mutations: dropping <c>natureOfRemittance: remittance</c> in <c>Create</c> → the custom reloads null (red);
    /// passing the 7-argument <c>WithTerms</c> in <c>Alter</c> → 194J(b) reloads null (red).</para>
    /// </summary>
    [AvaloniaFact]
    public void Nature_of_remittance_is_captured_on_create_and_alter_and_read_back_from_a_fresh_store()
    {
        var b = NewBook("NOR Capture Co", tcs: false);
        try
        {
            var m = OpenNop(b);
            Assert.Contains(Descendants(b.Window).OfType<TextBlock>(), t => t.IsEffectivelyVisible && t.Text == "Nature of Remittance");
            m.SectionCode = "195A";
            m.Name = "Payment to non-resident";
            m.FvuSectionCode = "95A";
            m.RateWithPanText = "20";
            m.NatureOfRemittanceText = "27";
            Key(b.Window, PhysicalKey.A, RawInputModifiers.Control);
            Assert.Equal("27", Reload(b).FindNatureOfPaymentByCode("195A")!.NatureOfRemittance);

            OpenNop(b);
            ArrowToSection(b, "194J(b)");
            Key(b.Window, PhysicalKey.Enter, RawInputModifiers.Control);
            var alter = b.Vm.NatureOfPaymentMaster!;
            Assert.True(alter.IsAltering);
            Assert.Equal(string.Empty, alter.NatureOfRemittanceText);

            alter.NatureOfRemittanceText = "16A";
            Key(b.Window, PhysicalKey.A, RawInputModifiers.Control);
            Assert.Contains("Nature of Remittance", alter.Message);
            Assert.Null(Reload(b).FindNatureOfPaymentByCode("194J(b)")!.NatureOfRemittance);

            alter.NatureOfRemittanceText = " 16 ";
            Key(b.Window, PhysicalKey.A, RawInputModifiers.Control);
            Assert.Contains("altered", alter.Message);
            var reloaded = Reload(b);
            Assert.Equal("16", reloaded.FindNatureOfPaymentByCode("194J(b)")!.NatureOfRemittance);
            Assert.Equal(1000, reloaded.FindNatureOfPaymentByCode("194J(b)")!.RateWithPanBp);

            // And it is pre-filled when the master is opened for alteration again.
            OpenNop(b);
            ArrowToSection(b, "195A");
            Key(b.Window, PhysicalKey.Enter, RawInputModifiers.Control);
            Assert.Equal("27", b.Vm.NatureOfPaymentMaster!.NatureOfRemittanceText);
        }
        finally { Close(b); }
    }

    // ============================================================================ (iii) Go To > Alter Master

    /// <summary>
    /// 🔴 <b>Alt+G &gt; Alter Master &gt; TDS Nature of Payment &gt; 194J(b), BY KEYS, ALTERS THE STORED MASTER.</b> The Go
    /// To result sits under "Masters → Alter Master"; Enter opens the company's own natures as a picker; Enter on a row
    /// opens that nature's ALTERATION screen; Ctrl+A saves; a fresh store reads the new rate back on the same id.
    /// <para>Mutations: deleting the <c>"Alter Master"</c> root row → Go To has no result (red); deleting the
    /// <c>AlterNatureOfPaymentPicker</c> arm in <c>OpenPageOf</c> → Enter on the row opens nothing and the rate stays
    /// 1000 (red).</para>
    /// </summary>
    [AvaloniaFact]
    public void Go_To_Alter_Master_reaches_and_alters_a_nature_of_payment()
    {
        var b = NewBook("GoTo Alter NOP Co");
        try
        {
            var id = b.Vm.Company!.FindNatureOfPaymentByCode("194J(b)")!.Id;
            GoTo(b, "TDS Nature of Payment", MainWindowViewModel.AlterNatureOfPaymentLabel);
            Assert.False(b.Vm.IsGoToOpen);
            Assert.Equal(GatewayMenu.AlterNatureOfPaymentPicker, b.Vm.CurrentGatewayMenu);
            Assert.Contains("Alter Master", b.Vm.ScreenTitle);

            ArrowToAndEnter(b.Window, b.Vm, "194J(b) - Fees for professional services / royalty / non-compete");
            Assert.Equal(Screen.NatureOfPaymentMaster, b.Vm.CurrentScreen);
            var alter = b.Vm.NatureOfPaymentMaster!;
            Assert.True(alter.IsAltering);
            Assert.Equal("194J(b)", alter.SectionCode);
            alter.RateWithPanText = "8";
            Key(b.Window, PhysicalKey.A, RawInputModifiers.Control);
            Assert.Contains("altered", alter.Message);

            var stored = Reload(b).FindNatureOfPaymentByCode("194J(b)")!;
            Assert.Equal(id, stored.Id);
            Assert.Equal(800, stored.RateWithPanBp);
        }
        finally { Close(b); }
    }

    /// <summary>
    /// The same route for the TCS master: Alt+G &gt; Alter Master &gt; TCS Nature of Goods &gt; 6CE alters scrap's rate.
    /// <para>Mutation: deleting the <c>AlterNatureOfGoodsPicker</c> arm in <c>OpenPageOf</c> → the rate stays 100 (red).</para>
    /// </summary>
    [AvaloniaFact]
    public void Go_To_Alter_Master_reaches_and_alters_a_nature_of_goods()
    {
        var b = NewBook("GoTo Alter NOG Co");
        try
        {
            var result = b.Vm.BuildGoToIndex().Single(d => d.Label == MainWindowViewModel.AlterNatureOfGoodsLabel);
            Assert.Equal(new[] { "Alter Master" }, result.Path);
            Assert.True(result.OpensSubmenu);

            GoTo(b, "TCS Nature of Goods", MainWindowViewModel.AlterNatureOfGoodsLabel);
            Assert.Equal(GatewayMenu.AlterNatureOfGoodsPicker, b.Vm.CurrentGatewayMenu);
            ArrowToAndEnter(b.Window, b.Vm, "6CE - Scrap");
            var alter = b.Vm.NatureOfGoodsMaster!;
            Assert.True(alter.IsAltering);
            Assert.Equal("Nature of Goods Alteration", alter.Caption);
            alter.RateWithPanText = "2";
            Key(b.Window, PhysicalKey.A, RawInputModifiers.Control);
            Assert.Equal(200, Reload(b).FindNatureOfGoodsByCode("6CE")!.RateWithPanBp);
        }
        finally { Close(b); }
    }

    /// <summary>ER-13: a company with neither TDS nor TCS shows no "Alter Master" row and Go To offers none; with TDS
    /// only, the Alter Master column lists the TDS master and not the TCS one.</summary>
    [AvaloniaFact]
    public void Alter_Master_is_gated_on_the_statutory_features()
    {
        var plain = NewBook("No Withholding Co", tds: false, tcs: false);
        try
        {
            Assert.DoesNotContain(plain.Vm.Columns[0].Items, i => i.Label == "Alter Master");
            Assert.DoesNotContain(plain.Vm.BuildGoToIndex(), d => d.Label == "Alter Master");
        }
        finally { Close(plain); }

        var tdsOnly = NewBook("TDS Only Co", tds: true, tcs: false);
        try
        {
            ArrowToAndEnter(tdsOnly.Window, tdsOnly.Vm, "Alter Master");
            var labels = tdsOnly.Vm.Columns[tdsOnly.Vm.ActiveColumnIndex].Items.Select(i => i.Label).ToList();
            Assert.Contains(MainWindowViewModel.AlterNatureOfPaymentLabel, labels);
            Assert.DoesNotContain(MainWindowViewModel.AlterNatureOfGoodsLabel, labels);
        }
        finally { Close(tdsOnly); }
    }

    // ============================================================================ T2-181

    /// <summary>
    /// 🔴 <b>T2-181: THE WARNING COUNTS ONLY THE LIVE VOUCHERS THE NEW FIGURES MOVE.</b> §194J(b) (₹50,000 FY aggregate):
    /// vendor A ₹1,00,000 (withholds), vendor B ₹10,000 (below threshold), and a CANCELLED ₹1,00,000 for A. A rate
    /// alteration (10% → 7.5%) moves A and B's rate — the message says <b>2 vouchers</b> (the old count said 3). A
    /// threshold alteration to ₹60,000 moves neither — the message carries no posted-voucher sentence at all (the old
    /// code warned "3 vouchers").
    /// <para>Mutation: restoring <c>VouchersCarrying(_company, replacement.Id).Count</c> in <c>Alter</c> → "3 vouchers"
    /// (red).</para>
    /// </summary>
    [AvaloniaFact]
    public void The_alteration_warning_counts_only_live_vouchers_the_new_figures_move()
    {
        var b = NewBook("T2-181 Count Co", tcs: false);
        try
        {
            var c = b.Vm.Company!;
            var j = c.FindNatureOfPaymentByCode("194J(b)")!;
            var fees = AddLedger(c, "Fees", "Indirect Expenses", true);
            var a = AddLedger(c, "Vendor A", "Sundry Creditors", false);
            a.PartyPan = "AAPFU0939F"; a.DeducteeType = DeducteeType.Firm;
            var v = AddLedger(c, "Vendor B", "Sundry Creditors", false);
            v.PartyPan = "AAQCS1234K"; v.DeducteeType = DeducteeType.Firm;
            Voucher Post(DomainLedger party, decimal gross, int day)
            {
                var g = Money.FromRupees(gross);
                var on = c.FinancialYearStart.AddDays(day);
                var carve = new TdsService(c).BuildCarveOut(g, g, j, party, on);
                var lines = new List<EntryLine> { new(fees.Id, g, DrCr.Debit), carve.PartyLine };
                if (carve.TdsPayableLine is { } p) lines.Add(p);
                return new LedgerService(c).Post(new Voucher(Guid.NewGuid(),
                    c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Journal).Id, on, lines));
            }
            Post(a, 1_00_000m, 40);
            Post(v, 10_000m, 41);
            var cancelled = Post(a, 1_00_000m, 42);
            new LedgerService(c).Cancel(cancelled.Id);
            new CompanyStorage(b.Dir).Save(c);

            OpenNop(b);
            ArrowToSection(b, "194J(b)");
            Key(b.Window, PhysicalKey.Enter, RawInputModifiers.Control);
            var alter = b.Vm.NatureOfPaymentMaster!;
            alter.CumulativeThresholdText = "60000";
            Key(b.Window, PhysicalKey.A, RawInputModifiers.Control);
            Assert.Contains("altered", alter.Message);
            Assert.DoesNotContain("already posted", alter.Message);
            Assert.Equal(60_000m, Reload(b).FindNatureOfPaymentByCode("194J(b)")!.CumulativeThreshold!.Value.Amount);

            alter.RateWithPanText = "7.5";
            Key(b.Window, PhysicalKey.A, RawInputModifiers.Control);
            Assert.Contains("2 vouchers already posted", alter.Message);
            Assert.DoesNotContain("3 vouchers", alter.Message);
        }
        finally { Close(b); }
    }

    /// <summary>
    /// 🔴 <b>T2-181: ROLLBACK.</b> A save the FILE refuses (read-only .db) leaves the shared aggregate holding the
    /// ORIGINAL master: once the file is writable again an unrelated save (a new custom nature) persists, and the
    /// reloaded 194J(b) still carries 10% — had the failed alteration stayed in memory, that save would have written 5%.
    /// <para>Mutation: deleting <c>tds.ReplaceNatureOfPayment(existing)</c> in <c>Alter</c>'s catch → the reloaded rate is
    /// 500 (red).</para>
    /// </summary>
    [AvaloniaFact]
    public void A_refused_save_rolls_the_nature_of_payment_alteration_back_out_of_the_shared_book()
    {
        var b = NewBook("T2-181 Rollback Co", tcs: false);
        try
        {
            OpenNop(b);
            ArrowToSection(b, "194J(b)");
            Key(b.Window, PhysicalKey.Enter, RawInputModifiers.Control);
            var alter = b.Vm.NatureOfPaymentMaster!;
            alter.RateWithPanText = "5";

            var dbPath = new CompanyStorage(b.Dir).PathForName(b.Name);
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            File.SetAttributes(dbPath, File.GetAttributes(dbPath) | FileAttributes.ReadOnly);
            try
            {
                Key(b.Window, PhysicalKey.A, RawInputModifiers.Control);
                Assert.DoesNotContain("altered", alter.Message ?? string.Empty);
                Assert.False(string.IsNullOrWhiteSpace(alter.Message));
            }
            finally
            {
                File.SetAttributes(dbPath, File.GetAttributes(dbPath) & ~FileAttributes.ReadOnly);
            }

            CreateCustomNop(b, "194NN");
            var reloaded = Reload(b);
            Assert.NotNull(reloaded.FindNatureOfPaymentByCode("194NN"));
            Assert.Equal(1000, reloaded.FindNatureOfPaymentByCode("194J(b)")!.RateWithPanBp);
        }
        finally { Close(b); }
    }

    /// <summary>
    /// 🔴 <b>T2-181: HIGHLIGHT RESTORE BY ID.</b> The highlight sits on custom 194ZZ (its neighbour above is custom
    /// 194ZY); a Create of "100A" sorts to the top and shifts every index by one. Alt+D then Y must delete 194ZZ, the
    /// master still highlighted, and NOT 194ZY.
    /// <para>Mutation: deleting <c>_highlight?.RestoreTo(previous)</c> in <c>NatureOfPaymentMasterViewModel.RefreshList</c>
    /// → the highlight lands on 194ZY and 194ZY is deleted (red).</para>
    /// </summary>
    [AvaloniaFact]
    public void After_a_create_re_sorts_the_list_Alt_D_deletes_the_nature_of_payment_still_highlighted()
    {
        var b = NewBook("T2-181 Highlight Co", tcs: false);
        try
        {
            CreateCustomNop(b, "194ZY");
            CreateCustomNop(b, "194ZZ");
            var m = OpenNop(b);
            ArrowToSection(b, "194ZZ");
            m.SectionCode = "100A";
            m.Name = "Sorts first";
            m.FvuSectionCode = "00A";
            m.RateWithPanText = "1";
            Key(b.Window, PhysicalKey.A, RawInputModifiers.Control);
            Assert.Contains("created", m.Message);
            Assert.Equal("194ZZ", m.HighlightedRow?.SectionCode);

            Key(b.Window, PhysicalKey.D, RawInputModifiers.Alt);
            Key(b.Window, PhysicalKey.Y);
            var reloaded = Reload(b);
            Assert.Null(reloaded.FindNatureOfPaymentByCode("194ZZ"));
            Assert.NotNull(reloaded.FindNatureOfPaymentByCode("194ZY"));
            Assert.NotNull(reloaded.FindNatureOfPaymentByCode("100A"));
        }
        finally { Close(b); }
    }
}
