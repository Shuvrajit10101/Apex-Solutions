using System;
using System.IO;
using System.Linq;
using System.Text;
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
using Apex.Ledger.Io;
using Apex.Ledger.Services;
using Xunit;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.Tests;

/// <summary>
/// 🔴 <b>CENSUS 5.11, WAVE 8 — per-ledger narration on the POS BILL (IV-236) and on its receipt (IV-237).</b>
/// help.tallysolutions.com/voucher-types-tally/, Set up transaction options, opened by content 2026-10-26:
/// <i>"Enable Provide narration for each ledger in voucher to add narration for individual ledgers"</i>. The bill's
/// KEYED ledgers are the Sales ledger and each tender's ledger; each now takes a narration through the real keys,
/// posts it, reloads it into its field on alteration, carries it through canonical export and import, and prints it
/// on the retail receipt. Odd-paisa figures throughout (3 x 1,234.57 = 3,703.71).
/// </summary>
public sealed class LineNarrationPosBillTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ApexLnPosW8_" + Guid.NewGuid().ToString("N"));

    private static void Pump(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.Measure(new Size(1280, 900));
        window.Arrange(new Rect(0, 0, 1280, 900));
        Dispatcher.UIThread.RunJobs();
    }

    private static void Key(Window w, PhysicalKey key, RawInputModifiers mods = RawInputModifiers.None)
    {
        w.KeyPressQwerty(key, mods);
        Pump(w);
    }

    private static TextBox? Focused(Window w) =>
        TopLevel.GetTopLevel(w)?.FocusManager?.GetFocusedElement() as TextBox;

    private static TextBox[] Boxes(Window w, string name) =>
        w.GetVisualDescendants().OfType<TextBox>().Where(t => t.Name == name && t.IsEffectivelyVisible).ToArray();

    private sealed record Kit(MainWindowViewModel Vm, VoucherType PosType, Guid WidgetId, DomainLedger Sales,
        DomainLedger Cash);

    private Kit NewBook(string name, bool flag, bool printAfterSaving = false)
    {
        var vm = new MainWindowViewModel(new CompanyStorage(_dir));
        vm.NewCompanyName = name;
        vm.CreateCompany();
        var c = vm.Company!;
        var masters = new InventoryService(c);
        var widget = masters.CreateStockItem("Till Widget", masters.CreateStockGroup("Goods").Id,
            masters.CreateSimpleUnit("Nos", "Numbers").Id);
        masters.AddOpeningBalance(widget.Id, c.MainLocation!.Id, 40m, Money.FromRupees(900m));
        var sales = new DomainLedger(Guid.NewGuid(), "Retail Sales", c.FindGroupByName("Sales Accounts")!.Id,
            Money.Zero, openingIsDebit: false);
        var cash = new DomainLedger(Guid.NewGuid(), "Till Cash", c.FindGroupByName("Cash-in-Hand")!.Id,
            Money.Zero, openingIsDebit: true);
        c.AddLedger(sales);
        c.AddLedger(cash);
        var posType = new VoucherType(Guid.NewGuid(), "Counter Sales", VoucherBaseType.Sales, useForPos: true,
            posConfig: new PosConfig { DefaultTitle = "Retail Invoice" })
        {
            ProvideNarrationForEachLedger = flag,
            PrintAfterSaving = printAfterSaving,
        };
        c.AddVoucherType(posType);
        return new Kit(vm, posType, widget.Id, sales, cash);
    }

    private Company Reload(string name)
    {
        var fresh = new CompanyStorage(_dir);
        return fresh.Load(fresh.ListCompanies().Single(e => e.Name == name));
    }

    private static PosBillingViewModel OpenBill(Window w, Kit k)
    {
        k.Vm.OpenPosBilling();
        Pump(w);
        var pos = k.Vm.PosBilling!;
        Assert.Same(k.PosType, pos.Type);
        pos.SelectedSalesLedger = pos.SalesLedgers.Single(l => l.Id == k.Sales.Id);
        pos.CashRow.SelectedLedger = pos.CashRow.LedgerChoices.Single(l => l.Id == k.Cash.Id);
        var line = pos.Items[0];
        line.SelectedItem = pos.StockItems.Single(i => i.Id == k.WidgetId);
        line.SelectedGodown = pos.Godowns.Single(g => g.Id == k.Vm.Company!.MainLocation!.Id);
        line.QuantityText = "3";
        line.RateText = "1234.57";
        Pump(w);
        return pos;
    }

    /// <summary>THE CONTROL: flag off, no narration field is shown and the bill posts none.
    /// <b>Mutation (P1):</b> making <c>PosBillingViewModel.ShowLineNarration</c> always true reddens this.</summary>
    [AvaloniaFact]
    public void Without_the_flag_the_pos_bill_offers_no_ledger_narration()
    {
        var k = NewBook("LNP8 Control Co", flag: false);
        var w = new MainWindow { DataContext = k.Vm };
        try
        {
            w.Show();
            OpenBill(w, k);
            Assert.Empty(Boxes(w, "PosSalesNarrationBox"));
            Assert.Empty(Boxes(w, "PosTenderNarrationBox"));
            Key(w, PhysicalKey.A, RawInputModifiers.Control);
            Assert.All(Reload("LNP8 Control Co").Vouchers.Single().Lines, l => Assert.Null(l.Narration));
        }
        finally { w.Close(); }
    }

    /// <summary>
    /// 🔴 <b>THROUGH THE REAL KEYS.</b> The REAL Tab off the Godown picker lands in the Sales ledger's narration; the
    /// REAL Tab off the Change box lands in the cash tender's narration; both are typed as text input; the REAL
    /// Ctrl+A posts; each narration is read off the RELOADED book on its own leg.
    /// <b>Mutations:</b> (P2) dropping <c>narration: NormaliseLineNarration(SalesLineNarration)</c> from the Sales leg
    /// reddens the Sales assertion; (P3) posting the bare tender line (ignoring <c>tenderNarration</c>) reddens the
    /// cash assertion.
    /// </summary>
    [AvaloniaFact]
    public void With_the_flag_on_the_sales_and_cash_legs_take_narrations_from_the_keyboard()
    {
        var k = NewBook("LNP8 Keys Co", flag: true);
        var w = new MainWindow { DataContext = k.Vm };
        try
        {
            w.Show();
            var pos = OpenBill(w, k);

            var godown = w.GetVisualDescendants().OfType<ComboBox>()
                .Single(c => ReferenceEquals(c.ItemsSource, pos.Godowns) && c.DataContext == pos);
            godown.Focus();
            Pump(w);
            Key(w, PhysicalKey.Tab);
            Assert.Equal("PosSalesNarrationBox", Focused(w)?.Name);
            w.KeyTextInput("Festival counter stall 9");
            Pump(w);

            var change = w.GetVisualDescendants().OfType<TextBox>()
                .Single(t => ReferenceEquals(t.DataContext, pos.CashRow) && t.IsReadOnly && t.IsEffectivelyVisible);
            change.Focus();
            Pump(w);
            Key(w, PhysicalKey.Tab);
            var focused = Focused(w);
            Assert.Equal("PosTenderNarrationBox", focused?.Name);
            Assert.Same(pos.CashRow, focused!.DataContext);
            w.KeyTextInput("Float topped up at noon");
            Pump(w);

            Key(w, PhysicalKey.A, RawInputModifiers.Control);
            var v = Reload("LNP8 Keys Co").Vouchers.Single();
            Assert.Equal("Festival counter stall 9", v.Lines.Single(l => l.LedgerId == k.Sales.Id).Narration);
            var cashLeg = v.Lines.Single(l => l.LedgerId == k.Cash.Id);
            Assert.Equal("Float topped up at noon", cashLeg.Narration);
            Assert.Equal(3703.71m, cashLeg.Amount.Amount);
        }
        finally { w.Close(); }
    }

    /// <summary>
    /// 🔴 <b>RELOAD ON ALTERATION.</b> A posted bill re-opens with each narration in its field; an edit to the Sales
    /// narration and a deliberate CLEAR of the cash narration both reach the reloaded book.
    /// <b>Mutations:</b> (P4) deleting <c>LoadLineNarrations(voucher, valueLegs[0])</c> reddens the cash-field
    /// assertion; (P5) deleting the <c>SalesLineNarration = …</c> rehydration line reddens the Sales-field
    /// assertion; (P6) passing <c>Array.Empty&lt;string&gt;()</c> as the guard's editable list reddens the accept
    /// (the clear is then refused).
    /// </summary>
    [AvaloniaFact]
    public void A_pos_bill_alteration_reloads_its_narrations_and_honours_an_edit_and_a_clear()
    {
        var k = NewBook("LNP8 Alter Co", flag: true);
        var w = new MainWindow { DataContext = k.Vm };
        try
        {
            w.Show();
            var pos = OpenBill(w, k);
            pos.SalesLineNarration = "Festival counter stall 9";
            pos.CashRow.LineNarration = "Float topped up at noon";
            Assert.True(pos.Accept(), pos.Message);
        }
        finally { w.Close(); }

        var c = k.Vm.Company!;
        var posted = c.Vouchers.Single();
        var open = PosBillingViewModel.ForAlter(c, posted.Id, new CompanyStorage(_dir), () => { }, () => { });
        Assert.False(open.IsRefused, open.Refusal);
        var alter = open.Entry!;
        Assert.True(alter.ShowLineNarration);
        Assert.Equal("Festival counter stall 9", alter.SalesLineNarration);
        Assert.Equal("Float topped up at noon", alter.CashRow.LineNarration);

        alter.SalesLineNarration = "Festival counter stall 12";
        alter.CashRow.LineNarration = "  ";
        Assert.True(alter.AcceptAlteration(), alter.Message);

        var v = Reload("LNP8 Alter Co").Vouchers.Single();
        Assert.Equal("Festival counter stall 12", v.Lines.Single(l => l.LedgerId == k.Sales.Id).Narration);
        Assert.Null(v.Lines.Single(l => l.LedgerId == k.Cash.Id).Narration);
    }

    /// <summary>
    /// 🔴 <b>EXPORT → IMPORT, READ OFF THE RELOADED IMPORT.</b> The POS bill's two narrations survive canonical JSON
    /// and XML into a separate company that is saved and reloaded through a fresh handle.
    /// </summary>
    [AvaloniaFact]
    public void Pos_bill_narrations_survive_canonical_export_and_import()
    {
        var k = NewBook("LNP8 Export Co", flag: true);
        var w = new MainWindow { DataContext = k.Vm };
        try
        {
            w.Show();
            var pos = OpenBill(w, k);
            pos.SalesLineNarration = "Festival counter stall 9";
            pos.CashRow.LineNarration = "Float topped up at noon";
            Assert.True(pos.Accept(), pos.Message);
        }
        finally { w.Close(); }

        var source = Reload("LNP8 Export Co");
        foreach (var (format, bytes) in new[]
                 {
                     ("json", CanonicalJson.Export(source)),
                     ("xml", CanonicalXml.Export(source)),
                 })
        {
            var (model, errors) = format == "json" ? CanonicalJson.Parse(bytes) : CanonicalXml.Parse(bytes);
            Assert.Empty(errors);
            var targetDir = Path.Combine(_dir, "import-" + format);
            var targetStorage = new CompanyStorage(targetDir);
            var tvm = new MainWindowViewModel(targetStorage);
            tvm.NewCompanyName = "LNP8 Import " + format;
            tvm.CreateCompany();
            var result = new CompanyImportService(tvm.Company!).Apply(model!, DuplicatePolicy.Skip);
            Assert.True(result.Applied);
            targetStorage.Save(tvm.Company!);

            var fresh = new CompanyStorage(targetDir);
            var imported = fresh.Load(fresh.ListCompanies().Single(x => x.Name == tvm.Company!.Name));
            var v = Assert.Single(imported.Vouchers);
            Assert.Equal("Festival counter stall 9",
                v.Lines.Single(l => l.LedgerId == imported.FindLedgerByName("Retail Sales")!.Id).Narration);
            Assert.Equal("Float topped up at noon",
                v.Lines.Single(l => l.LedgerId == imported.FindLedgerByName("Till Cash")!.Id).Narration);
        }
    }

    private static string PdfText(PrintPreviewViewModel p) => Encoding.Latin1.GetString(p.PdfBytes);

    /// <summary>
    /// 🔴 <b>PRINTED.</b> With the type's <i>Print voucher after saving</i> on, the saved bill's retail receipt
    /// opens, and its emitted PDF bytes carry a "Ledger Narrations" block naming each ledger; the on-screen mirror
    /// shows the same. <b>Mutations:</b> (P7) deleting the block in <c>PosReceiptPdf.Render</c> reddens the byte
    /// assertions; (P8) dropping <c>LedgerNarrations = …</c> from <c>BuildReceipt</c> reddens bytes and mirror;
    /// (P9) dropping the mirror block in <c>PrintPreviewViewModel</c> reddens the mirror assertion.
    /// </summary>
    [AvaloniaFact]
    public void The_pos_receipt_prints_the_ledger_narrations()
    {
        var k = NewBook("LNP8 Print Co", flag: true, printAfterSaving: true);
        var w = new MainWindow { DataContext = k.Vm };
        try
        {
            w.Show();
            var pos = OpenBill(w, k);
            pos.SalesLineNarration = "Festival counter stall 9";
            pos.CashRow.LineNarration = "Float topped up at noon";
            Key(w, PhysicalKey.A, RawInputModifiers.Control);

            var posted = Reload("LNP8 Print Co").Vouchers.Single();
            var preview = k.Vm.PrintPreview;
            Assert.NotNull(preview);
            Assert.Equal(posted.Id, preview!.SourceVoucherId);
            var pdf = PdfText(preview);
            Assert.Contains("(Ledger Narrations) Tj", pdf, StringComparison.Ordinal);
            Assert.Contains("(Retail Sales: Festival counter stall 9) Tj", pdf, StringComparison.Ordinal);
            Assert.Contains("(Till Cash: Float topped up at noon) Tj", pdf, StringComparison.Ordinal);
            var mirror = string.Join("\n", preview.Pages.SelectMany(p => p.Lines.Select(l => string.Join(" ", l.Cells))));
            Assert.Contains("Retail Sales: Festival counter stall 9", mirror, StringComparison.Ordinal);
        }
        finally { w.Close(); }
    }

    /// <summary>THE CONTROL for the receipt: no narration keyed, no "Ledger Narrations" block (ER-13).</summary>
    [AvaloniaFact]
    public void A_pos_receipt_without_narrations_prints_no_block()
    {
        var k = NewBook("LNP8 Print Ctl Co", flag: true, printAfterSaving: true);
        var w = new MainWindow { DataContext = k.Vm };
        try
        {
            w.Show();
            OpenBill(w, k);
            Key(w, PhysicalKey.A, RawInputModifiers.Control);
            Assert.NotNull(k.Vm.PrintPreview);
            Assert.DoesNotContain("Ledger Narrations", PdfText(k.Vm.PrintPreview!), StringComparison.Ordinal);
        }
        finally { w.Close(); }
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
