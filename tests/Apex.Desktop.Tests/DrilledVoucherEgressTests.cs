using System;
using System.Collections.Generic;
using System.Globalization;
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
using Domain = Apex.Ledger.Domain;
using Xunit;

namespace Apex.Desktop.Tests;

/// <summary>
/// 🔴 <b>T2-159 (HIGH, confidentiality) — EXPORT AND SHARE ON A DRILLED PURE-STOCK VOUCHER ACT ON THAT VOUCHER,
/// NEVER ON THE REGISTER BEHIND IT.</b> Driven through the REAL <see cref="MainWindow"/> key handler, on a
/// two-consignee book, and asserted on the ARTEFACT that leaves the application: the bytes of the file the
/// Export panel writes, and the decoded MIME parts of the <c>.eml</c> the E-Mail panel saves.
///
/// <para><b>WHAT WAS LIVE ON MAIN.</b> <c>MainWindowViewModel.IsReportContext</c> excluded
/// <see cref="Screen.LedgerVouchers"/> and <see cref="Screen.VoucherDetail"/> by id but not
/// <see cref="Screen.InventoryVoucherDetail"/>, while the Day Book stays bound beneath that pane. So Ctrl+E on a
/// drilled Delivery Note opened Export for the <b>whole Day Book</b> and wrote both consignees' goods, and
/// Alt+M &gt; E-Mail attached the same register — to a third party. Every test below fails on that code.</para>
///
/// <para><b>GROUNDING (R7, ruling 14).</b>
/// <c>help.tallysolutions.com/tally-prime/keyboard-shortcuts/keyboard-shortcuts-tally-prime/</c> (opened by
/// content 2026-10-25): <b>Ctrl+E</b> <i>"To export the current voucher or report"</i>, <b>Ctrl+M</b> <i>"To
/// e-mail the current voucher or report"</i>, <b>Alt+E</b> <i>"To open the export menu ..."</i>, <b>Alt+M</b>
/// <i>"To open the Share menu ..."</i>. "The current voucher" is the one on screen — not the register it was
/// drilled from.</para>
/// </summary>
public sealed class DrilledVoucherEgressTests
{
    private const string Ours = "Coastal Marine Supplies";
    private const string Theirs = "Himalayan Textiles Cooperative";
    private const string OurItem = "Anchor Chain";
    private const string TheirItem = "Merino Yarn";

    private static (MainWindow Window, MainWindowViewModel Vm, string TempDir) NewWindow()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ApexDrilledEgress_" + Guid.NewGuid().ToString("N"));
        var storage = new CompanyStorage(tempDir);
        var vm = new MainWindowViewModel(storage);
        var window = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
        window.Show();
        return (window, vm, tempDir);
    }

    private static void Pump(MainWindow window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    private static void Close(Window window, string dir)
    {
        window.Close();
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
        catch (IOException) { /* best effort */ }
    }

    private sealed record Kit(Guid WidgetId, Guid GadgetId, Guid GodownId, Guid OursId, Guid TheirsId);

    private static Kit Seed(MainWindowViewModel vm, string name)
    {
        vm.NewCompanyName = name;
        vm.CreateCompany();
        var c = vm.Company!;

        var masters = new InventoryService(c);
        var group = masters.CreateStockGroup("Goods");
        var nos = masters.CreateSimpleUnit("Nos", "Numbers");
        var widget = masters.CreateStockItem(OurItem, group.Id, nos.Id);
        var gadget = masters.CreateStockItem(TheirItem, group.Id, nos.Id);
        masters.AddOpeningBalance(widget.Id, c.MainLocation!.Id, 1000m, Money.FromRupees(100m));
        masters.AddOpeningBalance(gadget.Id, c.MainLocation!.Id, 1000m, Money.FromRupees(100m));

        var debtors = c.FindGroupByName("Sundry Debtors")!.Id;
        var ours = new Domain.Ledger(Guid.NewGuid(), Ours, debtors, Money.Zero, openingIsDebit: true);
        var theirs = new Domain.Ledger(Guid.NewGuid(), Theirs, debtors, Money.Zero, openingIsDebit: true);
        c.AddLedger(ours);
        c.AddLedger(theirs);

        return new Kit(widget.Id, gadget.Id, c.MainLocation!.Id, ours.Id, theirs.Id);
    }

    private static InventoryVoucher PostThroughTheScreen(
        MainWindowViewModel vm, Kit k, VoucherBaseType baseType, Guid itemId, Guid? partyId, decimal qty)
    {
        var before = vm.Company!.InventoryVouchers.Select(v => v.Id).ToHashSet();

        vm.OpenInventoryVoucher(baseType);
        var entry = vm.InventoryVoucherEntry!;
        entry.SelectedParty = partyId is { } pid
            ? entry.Parties.Single(p => p.Ledger?.Id == pid)
            : entry.Parties.Single(p => p.IsNone);

        var line = entry.Lines[0];
        line.SelectedItem = entry.StockItems.Single(i => i.Id == itemId);
        line.SelectedGodown = entry.Godowns.Single(g => g.Id == k.GodownId);
        line.QuantityText = qty.ToString(CultureInfo.InvariantCulture);
        if (!entry.IsPhysicalStock) line.RateText = "250";
        if (entry.IsStockJournal)
        {
            var dest = entry.DestinationLines[0];
            dest.SelectedItem = entry.StockItems.Single(i => i.Id == k.WidgetId);
            dest.SelectedGodown = entry.Godowns.Single(g => g.Id == k.GodownId);
            dest.QuantityText = qty.ToString(CultureInfo.InvariantCulture);
            dest.RateText = "250";
        }

        Assert.True(entry.Accept(), $"{baseType}: {entry.Message}");
        return vm.Company!.InventoryVouchers.Single(v => !before.Contains(v.Id));
    }

    /// <summary>Two Delivery Notes to two consignees; returns OURS (the one the tests drill).</summary>
    private static InventoryVoucher TwoConsigneeBook(MainWindowViewModel vm, Kit k)
    {
        var mine = PostThroughTheScreen(vm, k, VoucherBaseType.DeliveryNote, k.WidgetId, k.OursId, 4m);
        PostThroughTheScreen(vm, k, VoucherBaseType.DeliveryNote, k.GadgetId, k.TheirsId, 88m);
        return mine;
    }

    private static void DrillFromTheDayBook(MainWindow window, MainWindowViewModel vm, Guid stockVoucherId)
    {
        vm.OpenReport(ReportKind.DayBook);
        Pump(window);
        // The register beneath really does carry the OTHER consignee — otherwise "not in the file" proves nothing.
        Assert.Contains(vm.Reports!.Rows, r => typeof(ReportRow).GetProperties()
            .Where(p => p.PropertyType == typeof(string) && p.GetIndexParameters().Length == 0)
            .Any(p => ((string?)p.GetValue(r) ?? string.Empty).Contains(Theirs, StringComparison.Ordinal)));
        var row = vm.Reports!.Rows.SingleOrDefault(r => r.DrillInventoryVoucherId == stockVoucherId);
        Assert.NotNull(row);
        vm.Reports!.SelectedRow = row;
        Pump(window);
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Pump(window);
        Assert.Equal(Screen.InventoryVoucherDetail, vm.CurrentScreen);
        Assert.Equal(stockVoucherId, vm.InventoryVoucherDetail!.VoucherId);
    }

    /// <summary>Applies the open Export panel with Ctrl+A into <paramref name="dir"/> and returns the bytes written.</summary>
    private static string ApplyExportAndRead(MainWindow window, MainWindowViewModel vm, string dir, ExportFormat format)
    {
        Directory.CreateDirectory(dir);
        var panel = vm.ExportPanel!;
        panel.Folder = dir;
        panel.Format = format;
        panel.AppendTimestamp = false;
        var path = panel.BuildConfig().FullPath;
        if (File.Exists(path)) File.Delete(path);

        window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
        Pump(window);

        Assert.True(File.Exists(path), "Ctrl+A on the Export panel wrote no file: " + panel.Status);
        return Encoding.Latin1.GetString(File.ReadAllBytes(path));
    }

    /// <summary>The raw <c>.eml</c> text PLUS every base64 MIME part decoded (Latin-1), so a name hidden inside
    /// the attached PDF is as visible to the assertions as one in a header.</summary>
    private static string ReadEmlDecoded(string path)
    {
        var raw = Encoding.Latin1.GetString(File.ReadAllBytes(path));
        var sb = new StringBuilder(raw);
        foreach (var part in raw.Split("\r\n\r\n"))
        {
            var candidate = string.Concat(part.Split('\r', '\n').TakeWhile(l => !l.StartsWith("--", StringComparison.Ordinal)));
            if (candidate.Length < 8) continue;
            try { sb.Append('\n').Append(Encoding.Latin1.GetString(Convert.FromBase64String(candidate))); }
            catch (FormatException) { /* a header block, not a base64 body */ }
        }
        return sb.ToString();
    }

    private static void AssertOursOnly(string s, bool expectChallan)
    {
        // The confidentiality half FIRST, so a regression fails naming the leaked consignee.
        Assert.DoesNotContain(Theirs, s, StringComparison.Ordinal);
        Assert.DoesNotContain(TheirItem, s, StringComparison.Ordinal);
        Assert.DoesNotContain("Day Book", s, StringComparison.Ordinal);
        Assert.Contains(Ours, s, StringComparison.Ordinal);
        Assert.Contains(OurItem, s, StringComparison.Ordinal);
        if (expectChallan) Assert.Contains(DeliveryChallanPrintProjector.Title, s, StringComparison.Ordinal);
    }

    // ============================================================================================ Export

    /// <summary>
    /// 🔴 <b>Ctrl+E on a drilled Delivery Note writes THAT voucher — CSV and PDF both — and not one character
    /// of the other consignee.</b>
    ///
    /// <para><b>Mutations (run):</b> (1) <c>MainWindowViewModel.cs</c> restored to origin/main — the panel is the
    /// Day Book and the CSV contains <c>Himalayan Textiles Cooperative</c> (DoesNotContain fails at the leaked name);
    /// (2) only <c>BuildExportPanel</c>'s pane arm deleted — no Export panel opens: fails.</para>
    /// </summary>
    [AvaloniaFact]
    public void Ctrl_E_on_a_drilled_delivery_note_writes_that_voucher_and_no_other_consignee()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var k = Seed(vm, "Egress Export Co");
            var mine = TwoConsigneeBook(vm, k);
            DrillFromTheDayBook(window, vm, mine.Id);

            window.KeyPressQwerty(PhysicalKey.E, RawInputModifiers.Control);
            Pump(window);

            Assert.Equal(Screen.Export, vm.CurrentScreen);
            Assert.NotNull(vm.ExportPanel);

            var csv = ApplyExportAndRead(window, vm, Path.Combine(dir, "out"), ExportFormat.Csv);
            AssertOursOnly(csv, expectChallan: false);

            var pdf = ApplyExportAndRead(window, vm, Path.Combine(dir, "out"), ExportFormat.Pdf);
            AssertOursOnly(pdf, expectChallan: true);   // Export > PDF is the same document Print renders
            Assert.StartsWith("Delivery Note No. ", vm.ExportPanel!.DocumentTitle, StringComparison.Ordinal);
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// <b>Alt+E &gt; Current</b> — the vendor's export MENU route — reaches the same panel over the same voucher.
    /// <para><b>Mutations (run):</b> the same two as above; on (1) the menu's Current row writes the Day Book,
    /// leaked consignee included.</para>
    /// </summary>
    [AvaloniaFact]
    public void Alt_E_then_Current_on_a_drilled_delivery_note_exports_that_voucher()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var k = Seed(vm, "Egress Export Menu Co");
            var mine = TwoConsigneeBook(vm, k);
            DrillFromTheDayBook(window, vm, mine.Id);

            window.KeyPressQwerty(PhysicalKey.E, RawInputModifiers.Alt);
            Pump(window);
            Assert.Equal(Screen.ExportMenu, vm.CurrentScreen);
            Assert.Equal(ReportExportMenu.CurrentVerb, vm.Columns[^1].Selected?.Label);

            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Pump(window);

            Assert.Equal(Screen.Export, vm.CurrentScreen);
            var csv = ApplyExportAndRead(window, vm, Path.Combine(dir, "out"), ExportFormat.Csv);
            AssertOursOnly(csv, expectChallan: false);
        }
        finally { Close(window, dir); }
    }

    // ============================================================================================ Share

    /// <summary>
    /// 🔴 <b>Alt+M &gt; E-Mail on a drilled Delivery Note attaches ITS challan, and the saved <c>.eml</c> — headers
    /// and every decoded MIME part — carries no trace of the other consignee.</b>
    ///
    /// <para><b>Mutation (run):</b> <c>MainWindowViewModel.cs</c> restored to origin/main — the draft attaches
    /// <c>Day Book.pdf</c> and the decoded attachment contains <c>Himalayan Textiles Cooperative</c>.</para>
    /// </summary>
    [AvaloniaFact]
    public void Alt_M_then_E_Mail_on_a_drilled_delivery_note_attaches_its_challan_and_no_other_consignee()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var k = Seed(vm, "Egress Mail Co");
            var mine = TwoConsigneeBook(vm, k);
            DrillFromTheDayBook(window, vm, mine.Id);

            window.KeyPressQwerty(PhysicalKey.M, RawInputModifiers.Alt);
            Pump(window);
            Assert.Equal(Screen.ShareMenu, vm.CurrentScreen);
            Assert.Equal(ReportShareMenu.EmailVerb, vm.Columns[^1].Selected?.Label);

            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Pump(window);

            Assert.Equal(Screen.EmailCompose, vm.CurrentScreen);
            var compose = vm.EmailCompose!;
            Assert.True(compose.HasAttachment);
            compose.To = "consignee@example.com";

            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "draft.eml");
            Assert.True(vm.SaveEmail(path), compose.Status);

            var eml = ReadEmlDecoded(path);
            AssertOursOnly(eml, expectChallan: true);
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// The direct chords — Ctrl+M (e-mail) and W (WhatsApp) — obey the same rule as the menu: the drilled
    /// voucher's own challan, never the register. Asserted on the saved artefact of each channel.
    /// <para><b>Mutations (run):</b> origin/main's <c>MainWindowViewModel.cs</c> fails at the Ctrl+M draft;
    /// keeping the e-mail arm but deleting only <c>OpenWhatsAppShare</c>'s pane arm (with the old
    /// <c>IsReportContext</c>) fails at the W document, which is then the Day Book.</para>
    /// </summary>
    [AvaloniaFact]
    public void Ctrl_M_and_W_on_a_drilled_delivery_note_share_its_challan_and_no_other_consignee()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var k = Seed(vm, "Egress Direct Co");
            var mine = TwoConsigneeBook(vm, k);
            DrillFromTheDayBook(window, vm, mine.Id);
            Directory.CreateDirectory(dir);

            window.KeyPressQwerty(PhysicalKey.M, RawInputModifiers.Control);
            Pump(window);
            Assert.Equal(Screen.EmailCompose, vm.CurrentScreen);
            vm.EmailCompose!.To = "consignee@example.com";
            var emlPath = Path.Combine(dir, "direct.eml");
            Assert.True(vm.SaveEmail(emlPath), vm.EmailCompose.Status);
            AssertOursOnly(ReadEmlDecoded(emlPath), expectChallan: true);

            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Pump(window);
            Assert.Equal(Screen.InventoryVoucherDetail, vm.CurrentScreen);

            window.KeyPressQwerty(PhysicalKey.W, RawInputModifiers.None);
            Pump(window);
            Assert.Equal(Screen.WhatsAppShare, vm.CurrentScreen);
            var docPath = Path.Combine(dir, "share.pdf");
            Assert.True(vm.SaveWhatsAppDocument(docPath));
            AssertOursOnly(Encoding.Latin1.GetString(File.ReadAllBytes(docPath)), expectChallan: true);
        }
        finally { Close(window, dir); }
    }

    /// <summary>
    /// 🔴 <b>A pure-stock voucher with NO printed document is REFUSED BY NAME on the share channels — it does not
    /// fall back to the register.</b> A Stock Journal prints nothing (no TallyPrime page we opened gives it a
    /// document), so Alt+M &gt; E-Mail opens no compose panel and the notice says why, realised on screen. Its
    /// Ctrl+E still exports the journal's OWN lines — data, not a printed document — and nothing of the Delivery
    /// Note posted to the other consignee.
    /// <para><b>Mutations (run):</b> origin/main's <c>MainWindowViewModel.cs</c> opens a compose panel carrying
    /// <c>Day Book.pdf</c>; replacing the refusal in <c>OpenEmailCompose</c>'s pane arm with an empty attachment
    /// opens a compose panel; deleting <c>BuildExportPanel</c>'s pane arm opens no Export: each fails.</para>
    /// </summary>
    [AvaloniaFact]
    public void A_drilled_stock_journal_refuses_share_by_name_and_exports_only_its_own_lines()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var k = Seed(vm, "Egress Journal Co");
            var journal = PostThroughTheScreen(vm, k, VoucherBaseType.StockJournal, k.WidgetId, null, 3m);
            PostThroughTheScreen(vm, k, VoucherBaseType.DeliveryNote, k.GadgetId, k.TheirsId, 88m);
            DrillFromTheDayBook(window, vm, journal.Id);

            window.KeyPressQwerty(PhysicalKey.M, RawInputModifiers.Alt);
            Pump(window);
            Assert.Equal(Screen.ShareMenu, vm.CurrentScreen);
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Pump(window);

            Assert.Null(vm.EmailCompose);
            Assert.Equal(Screen.InventoryVoucherDetail, vm.CurrentScreen);
            Assert.Contains(DeliveryChallanPrintProjector.NotAChallan, vm.Notice ?? string.Empty, StringComparison.Ordinal);
            Assert.Contains(RenderedText(window),
                t => t.Contains(DeliveryChallanPrintProjector.NotAChallan, StringComparison.Ordinal));

            window.KeyPressQwerty(PhysicalKey.E, RawInputModifiers.Control);
            Pump(window);
            Assert.Equal(Screen.Export, vm.CurrentScreen);
            var csv = ApplyExportAndRead(window, vm, Path.Combine(dir, "out"), ExportFormat.Csv);
            Assert.Contains(OurItem, csv, StringComparison.Ordinal);
            Assert.DoesNotContain(Theirs, csv, StringComparison.Ordinal);
            Assert.DoesNotContain(TheirItem, csv, StringComparison.Ordinal);
            Assert.DoesNotContain("Day Book", csv, StringComparison.Ordinal);
        }
        finally { Close(window, dir); }
    }

    // ============================================================================================ the same shape

    /// <summary>
    /// The report-PARAMETER shortcuts are inert on the drilled pane, as they are on the other two drill columns:
    /// F12 does not open the hidden Day Book's configuration over the voucher, and F2 does not re-date it.
    /// <para><b>Mutation (run):</b> removing <c>Screen.InventoryVoucherDetail</c> from <c>IsReportContext</c>'s
    /// exclusion — F12 opens <see cref="Screen.ReportConfig"/> over the voucher: fails.</para>
    /// </summary>
    [AvaloniaFact]
    public void Report_parameter_keys_do_not_reach_the_register_behind_a_drilled_stock_voucher()
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var k = Seed(vm, "Egress Params Co");
            var mine = TwoConsigneeBook(vm, k);
            DrillFromTheDayBook(window, vm, mine.Id);

            Assert.False(vm.IsReportContext);

            window.KeyPressQwerty(PhysicalKey.F12, RawInputModifiers.None);
            Pump(window);
            Assert.NotEqual(Screen.ReportConfig, vm.CurrentScreen);
            Assert.Null(vm.ReportConfig);

            window.KeyPressQwerty(PhysicalKey.F2, RawInputModifiers.None);
            Pump(window);
            Assert.Null(vm.ReportConfig);
        }
        finally { Close(window, dir); }
    }


    /// <summary>
    /// 🔴 <b>THE SAME SHAPE FROM A SECOND DOOR: THE ALTERATION SCREENS DRILLED FROM THE DAY BOOK.</b> Ctrl+Enter
    /// on a Day Book row opens the voucher's alteration screen through the same <c>OpenDrillColumn</c> helper,
    /// so the register stays bound beneath it — and <c>IsReportContext</c> was TRUE there too. Measured before
    /// the fix (probe, this branch): on the stock-voucher alteration, focus on no text field, Ctrl+E opened
    /// Export titled <b>"Day Book"</b>. Asserted here on the stock-voucher alteration, the accounting-voucher
    /// alteration, the Alt+2 duplicate and the POS-bill alteration: none of Ctrl+E, Alt+E, Alt+M, Ctrl+M or W produces anything built
    /// from the register.
    ///
    /// <para><b>Mutation (run):</b> removing <c>VoucherEntry</c> / <c>InventoryVoucherEntry</c> /
    /// <c>PosBilling</c> from <c>IsReportContext</c>'s exclusion — Ctrl+E opens the Day Book export: fails.
    /// Removing <c>PosBilling</c> ALONE fails exactly the <c>pos-alter</c> case (M14, wave 7 resume), so the POS
    /// door is held by its own row and not only by the predicate.</para>
    /// </summary>
    [AvaloniaTheory]
    [InlineData("inventory-alter")]
    [InlineData("accounting-alter")]
    [InlineData("accounting-duplicate")]
    [InlineData("pos-alter")]
    public void Egress_keys_on_an_alteration_drilled_from_the_day_book_never_build_the_register(string door)
    {
        var (window, vm, dir) = NewWindow();
        try
        {
            var k = Seed(vm, "Egress Alter Co " + door);
            var mine = TwoConsigneeBook(vm, k);

            // One accounting voucher, so the accounting doors have a row to open.
            vm.OpenVoucher(VoucherBaseType.Receipt);
            var entry = vm.VoucherEntry!;
            var cash = vm.Company!.FindLedgerByName("Cash")!;
            var ours = vm.Company!.FindLedger(k.OursId)!;
            entry.Lines[0].SelectedLedger = cash;
            entry.Lines[0].Side = DrCr.Debit;
            entry.Lines[0].AmountText = "100";
            entry.Lines[1].SelectedLedger = ours;
            entry.Lines[1].Side = DrCr.Credit;
            entry.Lines[1].AmountText = "100";
            Assert.True(entry.Accept(), entry.Message);
            var receiptId = vm.Company!.Vouchers.Single().Id;

            // One POS bill, posted through the real POS screen, so the POS-alteration door has a row to open.
            Guid? posBillId = null;
            if (door == "pos-alter")
            {
                var c = vm.Company!;
                var sales = new Domain.Ledger(Guid.NewGuid(), "Retail Sales",
                    c.FindGroupByName("Sales Accounts")!.Id, Money.Zero, openingIsDebit: false);
                var till = new Domain.Ledger(Guid.NewGuid(), "Till Cash",
                    c.FindGroupByName("Cash-in-Hand")!.Id, Money.Zero, openingIsDebit: false);
                c.AddLedger(sales);
                c.AddLedger(till);
                var before = c.Vouchers.Select(v => v.Id).ToHashSet();
                vm.OpenPosBilling();
                Pump(window);
                var pos = vm.PosBilling!;
                pos.Date = entry.Date;
                pos.SelectedSalesLedger = pos.SalesLedgers.Single(l => l.Id == sales.Id);
                pos.CashRow.SelectedLedger = c.Ledgers.Single(l => l.Id == till.Id);
                var line = pos.Items[0];
                line.SelectedItem = pos.StockItems.Single(i => i.Id == k.WidgetId);
                line.SelectedGodown = pos.Godowns.Single(g => g.Id == k.GodownId);
                line.QuantityText = "2";
                line.RateText = "50";
                window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
                Pump(window);
                var bill = Assert.Single(c.Vouchers, v => !before.Contains(v.Id));
                Assert.True(bill.HasPosTenders);
                posBillId = bill.Id;
            }

            vm.OpenReport(ReportKind.DayBook);
            Pump(window);
            vm.Reports!.SelectedRow = door switch
            {
                "inventory-alter" => vm.Reports!.Rows.Single(r => r.DrillInventoryVoucherId == mine.Id),
                "pos-alter" => vm.Reports!.Rows.Single(r => r.DrillVoucherId == posBillId),
                _ => vm.Reports!.Rows.Single(r => r.DrillVoucherId == receiptId),
            };
            Pump(window);

            if (door == "accounting-duplicate")
                window.KeyPressQwerty(PhysicalKey.Digit2, RawInputModifiers.Alt);
            else
                window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.Control);
            Pump(window);

            var entryScreen = door switch
            {
                "inventory-alter" => Screen.InventoryVoucherEntry,
                "pos-alter" => Screen.PosBilling,
                _ => Screen.VoucherEntry,
            };
            Assert.Equal(entryScreen, vm.CurrentScreen);
            Assert.NotNull(vm.Reports);           // the register IS still bound beneath — the precondition

            // Take focus off any text field, as the probe found it, so the keys reach the shell's arms.
            window.Focus();
            Pump(window);

            foreach (var (key, mods) in new[]
            {
                (PhysicalKey.E, RawInputModifiers.Control),
                (PhysicalKey.E, RawInputModifiers.Alt),
                (PhysicalKey.M, RawInputModifiers.Alt),
                (PhysicalKey.M, RawInputModifiers.Control),
                (PhysicalKey.W, RawInputModifiers.None),
            })
            {
                window.KeyPressQwerty(key, mods);
                Pump(window);
                Assert.True(vm.ExportPanel is null || vm.ExportPanel.DocumentTitle != "Day Book",
                    $"{door} {mods}+{key}: exported the Day Book");
                Assert.True(vm.EmailCompose is null || vm.EmailCompose.DocumentTitle != "Day Book",
                    $"{door} {mods}+{key}: e-mailed the Day Book");
                Assert.True(vm.WhatsAppShare is null || vm.WhatsAppShare.DocumentTitle != "Day Book",
                    $"{door} {mods}+{key}: shared the Day Book");
                Assert.NotEqual(Screen.ExportMenu, vm.CurrentScreen);
                Assert.NotEqual(Screen.ShareMenu, vm.CurrentScreen);
            }
            Assert.False(vm.IsReportContext);
        }
        finally { Close(window, dir); }
    }

    private static IEnumerable<Visual> Descendants(Visual v)
    {
        foreach (var c in v.GetVisualChildren())
        {
            yield return c;
            foreach (var g in Descendants(c)) yield return g;
        }
    }

    private static List<string> RenderedText(MainWindow window) =>
        Descendants(window).OfType<TextBlock>()
            .Select(t => t.Text ?? string.Empty)
            .Where(s => s.Length > 0)
            .ToList();
}
