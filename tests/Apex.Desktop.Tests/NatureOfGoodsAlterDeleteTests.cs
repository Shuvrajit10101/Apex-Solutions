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
/// 🔴 <b>CENSUS 6.36 CLAUSE 1 (WAVE 13, TRACK ic) — ALTER AND DELETE ON THE NATURE OF GOODS (§206C TCS) MASTER.</b>
/// <c>NatureOfGoodsMasterViewModel</c> was create-only with immutable predefined rows, so a wrong seeded TCS rate had
/// no in-app remedy. Every test drives a realised <see cref="MainWindow"/> through its real key handler (arrows,
/// Ctrl+Enter, Ctrl+A, Alt+D, Y) and asserts on a book reloaded through a FRESH <see cref="CompanyStorage"/>, on the
/// rendered window, or on the shell's own notice — never on a field the test itself set.
///
/// <para><b>FIDELITY (R7, ruling 14), read by content 2026-11-24.</b> <c>help.tallysolutions.com/tcs-faq-tally/</c>:
/// <i>"Press Alt+G (Go To) &gt; Alter Master &gt; TCS Nature of Goods and press Enter."</i> and the rate is entered on
/// the existing master (so predefined rates are operator-editable); and <i>"Updating Section or Payment code in
/// existing TCS Nature of Goods will affect the payment entries"</i> (so a posted nature's code is refused). No vendor
/// page speaks to DELETING a nature: every deletion rule is ours (docs/invented-vs-cloned.md IV-382).</para>
///
/// <para>Menu walks are bounded (T2-169): every loop stops at the column's item count plus two and fails naming the
/// label it was looking for.</para>
/// </summary>
public sealed class NatureOfGoodsAlterDeleteTests
{
    private const string ValidTan = "MUMA12345B";
    private const string BuyerPan = "AAQCS1234K";

    private sealed record Book(MainWindow Window, MainWindowViewModel Vm, string Dir, string Name);

    private static Book NewTcsBook(string name)
    {
        var dir = Path.Combine(Path.GetTempPath(), "ApexW13ic_" + Guid.NewGuid().ToString("N"));
        var storage = new CompanyStorage(dir);
        var vm = new MainWindowViewModel(storage);
        vm.NewCompanyName = name;
        vm.CreateCompany();
        var c = vm.Company!;
        new TdsTcsService(c).EnableTcs(new TcsConfig { Tan = ValidTan });
        var buyer = AddLedger(c, "Scrap Buyer", "Sundry Debtors", true);
        buyer.PartyPan = BuyerPan;
        AddLedger(c, "Scrap Sales", "Sales Accounts", false);
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

    /// <summary>The real keyboard route: Gateway → Create → Nature of Goods.</summary>
    private static NatureOfGoodsMasterViewModel OpenMaster(Book b)
    {
        b.Vm.ShowGateway();
        Pump(b.Window);
        ArrowToAndEnter(b.Window, b.Vm, "Create");
        ArrowToAndEnter(b.Window, b.Vm, "Nature of Goods");
        Assert.Equal(Screen.NatureOfGoodsMaster, b.Vm.CurrentScreen);
        Assert.Same(b.Vm.NatureOfGoodsMaster, b.Vm.MasterListScreen);
        return b.Vm.NatureOfGoodsMaster!;
    }

    private static void ArrowToCode(Book b, string code)
    {
        var bound = (b.Vm.NatureOfGoodsMaster?.Natures.Count ?? 0) + 2;
        for (var i = 0; i < bound; i++)
        {
            if (b.Vm.NatureOfGoodsMaster?.HighlightedRow?.CollectionCode == code) return;
            Key(b.Window, PhysicalKey.ArrowDown);
        }
        Assert.Fail($"Code '{code}' was not reachable by arrow navigation on the Nature of Goods list.");
    }

    /// <summary>Creates a custom nature through the screen's own Create path (Ctrl+A).</summary>
    private static void CreateCustom(Book b, string code, string rate)
    {
        var m = OpenMaster(b);
        m.CollectionCode = code;
        m.Name = "Custom " + code;
        m.RateWithPanText = rate;
        Key(b.Window, PhysicalKey.A, RawInputModifiers.Control);
        Assert.True(b.Vm.Company!.FindNatureOfGoodsByCode(code) is not null, m.Message);
    }

    /// <summary>Posts a Journal Dr Buyer / Cr Sales / Cr TCS Payable under <paramref name="code"/> through the engine
    /// and saves it, so a TCS line carries the nature.</summary>
    private static Voucher PostTcs(Book b, string code, decimal value)
    {
        var c = b.Vm.Company!;
        var nature = c.FindNatureOfGoodsByCode(code)!;
        var buyer = c.FindLedgerByName("Scrap Buyer")!;
        var sales = c.FindLedgerByName("Scrap Sales")!;
        var on = c.FinancialYearStart.AddDays(30);
        var col = new TcsService(c).BuildCollection(Money.FromRupees(value), Money.Zero, nature, buyer, on);
        var lines = new List<EntryLine>
        {
            new(buyer.Id, Money.FromRupees(value) + col.TcsAmount, DrCr.Debit),
            new(sales.Id, Money.FromRupees(value), DrCr.Credit),
        };
        if (col.TcsPayableLine is { } p) lines.Add(p);
        var v = new LedgerService(c).Post(new Voucher(Guid.NewGuid(),
            c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Journal).Id, on, lines));
        new CompanyStorage(b.Dir).Save(c);
        return v;
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
    /// 🔴 <b>THE HEADLINE: a predefined nature's rate and threshold are altered by keys, survive a fresh store with the
    /// same id, and are what the engine collects with afterwards.</b> §206C(1F) 6CL, 1% above ₹10,00,000 → 2% above
    /// ₹5,00,000: on a ₹6,00,000.50 sale the reloaded book collects ₹12,000 (it collected nothing before).
    /// <para>Mutation: <c>TcsConfig.ReplaceNatureOfGoods</c> body made a no-op → the reloaded rate stays 100 (red).</para>
    /// </summary>
    [AvaloniaFact]
    public void Altered_predefined_rate_and_threshold_persist_and_drive_the_next_TCS_collection()
    {
        var b = NewTcsBook("NOG Alter Rate Co");
        try
        {
            var id = b.Vm.Company!.FindNatureOfGoodsByCode("6CL")!.Id;
            OpenMaster(b);
            ArrowToCode(b, "6CL");
            Key(b.Window, PhysicalKey.Enter, RawInputModifiers.Control);

            var alter = b.Vm.NatureOfGoodsMaster!;
            Assert.True(alter.IsAltering);
            Assert.Equal("Nature of Goods Alteration", alter.Caption);
            Assert.Equal("1", alter.RateWithPanText);          // pre-filled from the stored master
            Assert.Equal("1000000", alter.ThresholdText);
            alter.RateWithPanText = "2";
            alter.ThresholdText = "500000";
            Key(b.Window, PhysicalKey.A, RawInputModifiers.Control);
            Assert.Contains("altered", alter.Message);

            var reloaded = Reload(b);
            var stored = reloaded.NaturesOfGoods.Single(n => n.CollectionCode == "6CL");
            Assert.Equal(id, stored.Id);
            Assert.Equal(200, stored.RateWithPanBp);
            Assert.Equal(500_000m, stored.Threshold!.Value.Amount);
            Assert.True(stored.IsPredefined);
            Assert.Single(reloaded.NaturesOfGoods, n => n.CollectionCode == "6CL");

            var buyer = reloaded.FindLedgerByName("Scrap Buyer")!;
            var col = new TcsService(reloaded).Compute(Money.FromRupees(6_00_000.50m), Money.Zero, stored, buyer,
                reloaded.FinancialYearStart.AddDays(10));
            Assert.True(col.Applies);
            Assert.Equal(12_000m, col.TcsAmount.Amount);
        }
        finally { Close(b); }
    }

    /// <summary>A predefined nature's collection code is LOCKED (ours): the realised TextBox is read-only, and a forced
    /// change is refused by name with nothing stored.
    /// <para>Mutation: deleting the <c>existing.IsPredefined</c> branch of <c>NatureOfGoodsRules.EnsureAlterable</c> →
    /// the forced code is stored (red).</para></summary>
    [AvaloniaFact]
    public void Predefined_collection_code_is_locked_and_a_forced_change_is_refused_by_name()
    {
        var b = NewTcsBook("NOG Code Lock Co");
        try
        {
            OpenMaster(b);
            ArrowToCode(b, "6CE");
            Key(b.Window, PhysicalKey.Enter, RawInputModifiers.Control);
            var alter = b.Vm.NatureOfGoodsMaster!;
            Assert.True(alter.IsCodeLocked);
            var locked = Descendants(b.Window).OfType<TextBox>()
                .Where(t => t.IsEffectivelyVisible && t.Text == "6CE").ToList();
            Assert.Single(locked);
            Assert.True(locked[0].IsReadOnly);

            alter.CollectionCode = "6CX";
            Key(b.Window, PhysicalKey.A, RawInputModifiers.Control);
            Assert.Contains("predefined statutory nature", alter.Message);
            Assert.Contains("'6CE'", alter.Message);
            Assert.NotNull(Reload(b).FindNatureOfGoodsByCode("6CE"));
            Assert.Null(Reload(b).FindNatureOfGoodsByCode("6CX"));
        }
        finally { Close(b); }
    }

    /// <summary>A CUSTOM nature a posted TCS line carries refuses a code change naming the voucher; its rate is still
    /// alterable and the posted line keeps its own rate and amount.
    /// <para>Mutation: deleting the posted-voucher branch of <c>EnsureAlterable</c> → the new code is stored (red).</para></summary>
    [AvaloniaFact]
    public void Custom_nature_with_posted_TCS_refuses_a_code_change_but_accepts_a_rate_change()
    {
        var b = NewTcsBook("NOG Custom Posted Co");
        try
        {
            CreateCustom(b, "6ZZ", "1");
            PostTcs(b, "6ZZ", 10_000m);   // 1% = ₹100

            OpenMaster(b);
            ArrowToCode(b, "6ZZ");
            Key(b.Window, PhysicalKey.Enter, RawInputModifiers.Control);
            var alter = b.Vm.NatureOfGoodsMaster!;
            Assert.False(alter.IsCodeLocked);
            alter.CollectionCode = "6YY";
            Key(b.Window, PhysicalKey.A, RawInputModifiers.Control);
            Assert.Contains("Cannot change the collection code", alter.Message);
            Assert.Contains("1 posted voucher", alter.Message);
            Assert.Contains("Journal", alter.Message);
            Assert.NotNull(Reload(b).FindNatureOfGoodsByCode("6ZZ"));
            Assert.Null(Reload(b).FindNatureOfGoodsByCode("6YY"));

            alter.CollectionCode = "6ZZ";
            alter.RateWithPanText = "3";
            Key(b.Window, PhysicalKey.A, RawInputModifiers.Control);
            Assert.Contains("1 voucher already posted", alter.Message);
            var reloaded = Reload(b);
            Assert.Equal(300, reloaded.FindNatureOfGoodsByCode("6ZZ")!.RateWithPanBp);
            var posted = reloaded.Vouchers.SelectMany(v => v.Lines).Single(l => l.Tcs is { TcsAmount.Amount: > 0m }).Tcs!;
            Assert.Equal(100, posted.RateBasisPoints);
            Assert.Equal(100m, posted.TcsAmount.Amount);
        }
        finally { Close(b); }
    }

    /// <summary>
    /// 🔴 <b>ROLLBACK.</b> A save the FILE refuses (read-only .db) leaves the shared aggregate holding the ORIGINAL
    /// master: after the file is writable again, an unrelated save (a new custom nature) persists, and the reloaded
    /// 6CE still carries 1% — had the failed alteration stayed in memory, that second save would have written 4%.
    /// <para>Mutation: deleting <c>tcs.ReplaceNatureOfGoods(existing)</c> in <c>Alter</c>'s catch → the reloaded rate is
    /// 400 (red).</para>
    /// </summary>
    [AvaloniaFact]
    public void A_refused_save_rolls_the_alteration_back_out_of_the_shared_book()
    {
        var b = NewTcsBook("NOG Rollback Co");
        try
        {
            OpenMaster(b);
            ArrowToCode(b, "6CE");
            Key(b.Window, PhysicalKey.Enter, RawInputModifiers.Control);
            var alter = b.Vm.NatureOfGoodsMaster!;
            alter.RateWithPanText = "4";

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

            CreateCustom(b, "6NN", "1");   // an unrelated save of the whole book
            var reloaded = Reload(b);
            Assert.NotNull(reloaded.FindNatureOfGoodsByCode("6NN"));
            Assert.Equal(100, reloaded.FindNatureOfGoodsByCode("6CE")!.RateWithPanBp);
        }
        finally { Close(b); }
    }

    // ======================================================================================= DELETE

    /// <summary>Alt+D then Y on a PREDEFINED nature is refused by name and nothing leaves the book.
    /// <para>Mutation: deleting the <c>nature.IsPredefined</c> branch of <c>EnsureDeletable</c> → 6CE is gone from the
    /// reloaded book (red).</para></summary>
    [AvaloniaFact]
    public void Delete_of_a_predefined_nature_is_refused_by_name()
    {
        var b = NewTcsBook("NOG Delete Predefined Co");
        try
        {
            OpenMaster(b);
            ArrowToCode(b, "6CE");
            Key(b.Window, PhysicalKey.D, RawInputModifiers.Alt);
            Key(b.Window, PhysicalKey.Y);
            Assert.Contains("'6CE'", b.Vm.Notice);
            Assert.Contains("predefined statutory nature", b.Vm.Notice);
            Assert.NotNull(Reload(b).FindNatureOfGoodsByCode("6CE"));
        }
        finally { Close(b); }
    }

    /// <summary>A custom nature a LEDGER names, and one a STOCK ITEM names, are each refused by name; an UNUSED custom
    /// nature is deleted, absent from a fresh store and from the screen's list.
    /// <para>Mutations: deleting the ledger branch, or the stock-item branch, of <c>EnsureDeletable</c> → that nature
    /// is deleted while still named (red); deleting <c>Screen.NatureOfGoodsMaster =&gt; RequestDeleteMasterListRow()</c>
    /// from the shell's delete switch → Alt+D is a dead key and 6UU survives (red).</para></summary>
    [AvaloniaFact]
    public void Delete_refuses_natures_a_ledger_or_stock_item_names_and_deletes_an_unused_one()
    {
        var b = NewTcsBook("NOG Delete Named Co");
        try
        {
            CreateCustom(b, "6LL", "1");
            CreateCustom(b, "6MM", "1");
            CreateCustom(b, "6UU", "1");
            var c = b.Vm.Company!;
            AddLedger(c, "Timber Sales", "Sales Accounts", false).TcsNatureOfGoodsId = c.FindNatureOfGoodsByCode("6LL")!.Id;
            var inv = new InventoryService(c);
            inv.CreateStockItem("Teak Logs", inv.CreateStockGroup("Wood").Id, inv.CreateSimpleUnit("Cft", "Cubic feet").Id)
                .TcsNatureOfGoodsId = c.FindNatureOfGoodsByCode("6MM")!.Id;
            new CompanyStorage(b.Dir).Save(c);

            OpenMaster(b);
            ArrowToCode(b, "6LL");
            Key(b.Window, PhysicalKey.D, RawInputModifiers.Alt);
            Key(b.Window, PhysicalKey.Y);
            Assert.Contains("'Timber Sales'", b.Vm.Notice);
            Assert.NotNull(Reload(b).FindNatureOfGoodsByCode("6LL"));

            ArrowToCode(b, "6MM");
            Key(b.Window, PhysicalKey.D, RawInputModifiers.Alt);
            Key(b.Window, PhysicalKey.Y);
            Assert.Contains("'Teak Logs'", b.Vm.Notice);
            Assert.NotNull(Reload(b).FindNatureOfGoodsByCode("6MM"));

            ArrowToCode(b, "6UU");
            Key(b.Window, PhysicalKey.D, RawInputModifiers.Alt);
            Key(b.Window, PhysicalKey.Y);
            Assert.Contains("deleted", b.Vm.Notice);
            var reloaded = Reload(b);
            Assert.Null(reloaded.FindNatureOfGoodsByCode("6UU"));
            Assert.NotNull(reloaded.FindNatureOfGoodsByCode("6LL"));
            Assert.NotNull(reloaded.FindNatureOfGoodsByCode("6MM"));
            Assert.DoesNotContain(b.Vm.NatureOfGoodsMaster!.Natures, r => r.CollectionCode == "6UU");
        }
        finally { Close(b); }
    }

    /// <summary>A custom nature a POSTED (and since cancelled) TCS line carries is refused by name, naming the voucher.
    /// <para>Mutation: deleting the posted-voucher branch of <c>EnsureDeletable</c> → the nature is deleted and the
    /// posted line's NatureId resolves to nothing (red).</para></summary>
    [AvaloniaFact]
    public void Delete_refuses_a_nature_a_posted_and_cancelled_TCS_line_carries()
    {
        var b = NewTcsBook("NOG Delete Posted Co");
        try
        {
            CreateCustom(b, "6PP", "1");
            var v = PostTcs(b, "6PP", 20_000m);
            new LedgerService(b.Vm.Company!).Cancel(v.Id);
            new CompanyStorage(b.Dir).Save(b.Vm.Company!);

            OpenMaster(b);
            ArrowToCode(b, "6PP");
            Key(b.Window, PhysicalKey.D, RawInputModifiers.Alt);
            Key(b.Window, PhysicalKey.Y);
            Assert.Contains("'6PP'", b.Vm.Notice);
            Assert.Contains("1 posted voucher", b.Vm.Notice);

            var reloaded = Reload(b);
            var nature = reloaded.FindNatureOfGoodsByCode("6PP");
            Assert.NotNull(nature);
            Assert.Contains(reloaded.Vouchers.SelectMany(x => x.Lines), l => l.Tcs is { } t && t.NatureId == nature!.Id);
        }
        finally { Close(b); }
    }

    /// <summary>
    /// 🔴 <b>HIGHLIGHT RESTORE BY ID.</b> The highlight sits on custom 6ZZ (its neighbour above is custom 6ZY); a Create
    /// of "1AA" sorts to the top and shifts every index by one. Alt+D then Y must delete 6ZZ — the master the operator
    /// highlighted — and NOT 6ZY.
    /// <para>Mutation: deleting <c>_highlight?.RestoreTo(previous)</c> in <c>NatureOfGoodsMasterViewModel.RefreshList</c>
    /// → the highlight index is kept, lands on 6ZY, and 6ZY is deleted (red).</para>
    /// </summary>
    [AvaloniaFact]
    public void After_a_create_re_sorts_the_list_Alt_D_deletes_the_master_still_highlighted()
    {
        var b = NewTcsBook("NOG Highlight Co");
        try
        {
            CreateCustom(b, "6ZY", "1");
            CreateCustom(b, "6ZZ", "1");
            var m = OpenMaster(b);
            ArrowToCode(b, "6ZZ");
            m.CollectionCode = "1AA";
            m.Name = "Sorts first";
            m.RateWithPanText = "1";
            Key(b.Window, PhysicalKey.A, RawInputModifiers.Control);
            Assert.Contains("created", m.Message);
            Assert.Equal("6ZZ", m.HighlightedRow?.CollectionCode);

            Key(b.Window, PhysicalKey.D, RawInputModifiers.Alt);
            Key(b.Window, PhysicalKey.Y);
            var reloaded = Reload(b);
            Assert.Null(reloaded.FindNatureOfGoodsByCode("6ZZ"));
            Assert.NotNull(reloaded.FindNatureOfGoodsByCode("6ZY"));
            Assert.NotNull(reloaded.FindNatureOfGoodsByCode("1AA"));
        }
        finally { Close(b); }
    }

    /// <summary>Alt+D is inert on the alteration screen (the shared arm's <c>IsAltering</c> gate).</summary>
    [AvaloniaFact]
    public void Alt_D_is_inert_on_the_alteration_screen()
    {
        var b = NewTcsBook("NOG Alter Gate Co");
        try
        {
            CreateCustom(b, "6GG", "1");
            OpenMaster(b);
            ArrowToCode(b, "6GG");
            Key(b.Window, PhysicalKey.Enter, RawInputModifiers.Control);
            Assert.True(b.Vm.NatureOfGoodsMaster!.IsAltering);
            Assert.False(b.Vm.IsDeleteTargetPage);
            Key(b.Window, PhysicalKey.D, RawInputModifiers.Alt);
            Key(b.Window, PhysicalKey.Y);
            Assert.NotNull(Reload(b).FindNatureOfGoodsByCode("6GG"));
        }
        finally { Close(b); }
    }
}
