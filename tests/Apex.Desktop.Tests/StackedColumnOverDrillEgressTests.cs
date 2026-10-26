using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Ledger.Io;
using Apex.Desktop.Views;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Domain = Apex.Ledger.Domain;
using Xunit;

namespace Apex.Desktop.Tests;

/// <summary>
/// 🔴 <b>T2-159, THE THIRD DOOR (wave 7 adversarial review): a column STACKED OVER a drilled voucher pane must not
/// export or share the register two columns down.</b> Found by the reviewer with this fixture on the track's head
/// (a8acfa7): a drilled Delivery Note, then P (the challan's own preview), then Ctrl+E / Ctrl+M / Alt+M &gt; E-Mail /
/// W wrote or attached the WHOLE Day Book, the other consignee included; Ctrl+E then M did the same; and so did a
/// drilled accounting voucher's preview. Asserted on the artefact that leaves the application (CSV bytes, the
/// .eml with every base64 MIME part decoded, the WhatsApp PDF).
///
/// <para><b>Mutation (run):</b> deleting <c>&amp;&amp; !IsDrillPageAboveReport()</c> from
/// <c>MainWindowViewModel.IsReportContext</c> reddens every <c>Probe</c> case at the leaked consignee's name; the
/// positive control <c>A_report_preview_still_e_mails_the_report</c> stays green, so the clause does not switch
/// the report off where the report IS the page beneath.</para>
/// </summary>
public sealed class StackedColumnOverDrillEgressTests
{
    private readonly ITestOutputHelper _out;
    public StackedColumnOverDrillEgressTests(ITestOutputHelper o) => _out = o;

    private const string Ours = "Probe Ours Traders";
    private const string Theirs = "Probe Theirs Rivals";

    private static void Pump(MainWindow w)
    {
        Dispatcher.UIThread.RunJobs(); w.UpdateLayout(); Dispatcher.UIThread.RunJobs(); w.UpdateLayout();
    }

    private static (MainWindow, MainWindowViewModel, string, Guid ours, Guid theirs, Guid item1, Guid item2, Guid godown) Setup(string name)
    {
        var dir = Path.Combine(Path.GetTempPath(), "ApexProbe_" + Guid.NewGuid().ToString("N"));
        var vm = new MainWindowViewModel(new CompanyStorage(dir));
        var w = new MainWindow { DataContext = vm, Width = 1440, Height = 900 };
        w.Show();
        vm.NewCompanyName = name;
        vm.CreateCompany();
        var c = vm.Company!;
        var inv = new InventoryService(c);
        var g = inv.CreateStockGroup("Goods");
        var nos = inv.CreateSimpleUnit("Nos", "Numbers");
        var i1 = inv.CreateStockItem("Probe Bolt", g.Id, nos.Id);
        var i2 = inv.CreateStockItem("Probe Secret Widget", g.Id, nos.Id);
        inv.AddOpeningBalance(i1.Id, c.MainLocation!.Id, 1000m, Money.FromRupees(100m));
        inv.AddOpeningBalance(i2.Id, c.MainLocation!.Id, 1000m, Money.FromRupees(100m));
        var debtors = c.FindGroupByName("Sundry Debtors")!.Id;
        var o = new Domain.Ledger(Guid.NewGuid(), Ours, debtors, Money.Zero, openingIsDebit: true);
        var t = new Domain.Ledger(Guid.NewGuid(), Theirs, debtors, Money.Zero, openingIsDebit: true);
        c.AddLedger(o); c.AddLedger(t);
        return (w, vm, dir, o.Id, t.Id, i1.Id, i2.Id, c.MainLocation!.Id);
    }

    private static InventoryVoucher PostDn(MainWindowViewModel vm, Guid party, Guid item, Guid godown, decimal qty)
    {
        var before = vm.Company!.InventoryVouchers.Select(v => v.Id).ToHashSet();
        vm.OpenInventoryVoucher(VoucherBaseType.DeliveryNote);
        var e = vm.InventoryVoucherEntry!;
        e.SelectedParty = e.Parties.Single(p => p.Ledger?.Id == party);
        var l = e.Lines[0];
        l.SelectedItem = e.StockItems.Single(i => i.Id == item);
        l.SelectedGodown = e.Godowns.Single(gd => gd.Id == godown);
        l.QuantityText = qty.ToString(CultureInfo.InvariantCulture);
        l.RateText = "250";
        Assert.True(e.Accept(), e.Message);
        return vm.Company!.InventoryVouchers.Single(v => !before.Contains(v.Id));
    }

    [AvaloniaTheory]
    [InlineData("P-then-CtrlE")]
    [InlineData("P-then-CtrlM")]
    [InlineData("P-then-W")]
    [InlineData("P-then-AltM")]
    [InlineData("CtrlE-then-M")]
    [InlineData("acct-P-then-CtrlM")]
    [InlineData("acct-P-then-CtrlE")]
    public void Probe(string route)
    {
        var (w, vm, dir, ours, theirs, i1, i2, gd) = Setup("Probe Co " + route);
        try
        {
            var mine = PostDn(vm, ours, i1, gd, 4m);
            PostDn(vm, theirs, i2, gd, 88m);
            Guid receiptId = Guid.Empty;
            if (route.StartsWith("acct-", StringComparison.Ordinal))
            {
                foreach (var party in new[] { ours, theirs })
                {
                    vm.OpenVoucher(VoucherBaseType.Receipt);
                    var en = vm.VoucherEntry!;
                    en.Lines[0].SelectedLedger = vm.Company!.FindLedgerByName("Cash")!;
                    en.Lines[0].Side = DrCr.Debit;
                    en.Lines[0].AmountText = "100";
                    en.Lines[1].SelectedLedger = vm.Company!.FindLedger(party)!;
                    en.Lines[1].Side = DrCr.Credit;
                    en.Lines[1].AmountText = "100";
                    Assert.True(en.Accept(), en.Message);
                    if (party == ours) receiptId = vm.Company!.Vouchers.Single().Id;
                }
                route = route.Substring(5);
            }
            vm.OpenReport(ReportKind.DayBook);
            Pump(w);
            vm.Reports!.SelectedRow = receiptId != Guid.Empty
                ? vm.Reports!.Rows.Single(r => r.DrillVoucherId == receiptId)
                : vm.Reports!.Rows.Single(r => r.DrillInventoryVoucherId == mine.Id);
            Pump(w);
            w.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Pump(w);
            Assert.Equal(receiptId != Guid.Empty ? Screen.VoucherDetail : Screen.InventoryVoucherDetail, vm.CurrentScreen);

            if (route.StartsWith("P-", StringComparison.Ordinal))
            {
                w.KeyPressQwerty(PhysicalKey.P, RawInputModifiers.None);
                Pump(w);
                Assert.Equal(Screen.PrintPreview, vm.CurrentScreen);
            }
            else
            {
                w.KeyPressQwerty(PhysicalKey.E, RawInputModifiers.Control);
                Pump(w);
                Assert.Equal(Screen.Export, vm.CurrentScreen);
            }
            w.Focus();
            Pump(w);
            _out.WriteLine($"after first step: screen={vm.CurrentScreen} IsReportContext={vm.IsReportContext} focus={w.FocusManager?.GetFocusedElement()?.GetType().Name}");

            switch (route)
            {
                case "P-then-CtrlE": w.KeyPressQwerty(PhysicalKey.E, RawInputModifiers.Control); break;
                case "P-then-CtrlM": w.KeyPressQwerty(PhysicalKey.M, RawInputModifiers.Control); break;
                case "P-then-W": w.KeyPressQwerty(PhysicalKey.W, RawInputModifiers.None); break;
                case "P-then-AltM":
                    w.KeyPressQwerty(PhysicalKey.M, RawInputModifiers.Alt); Pump(w);
                    w.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None); break;
                case "CtrlE-then-M": w.KeyPressQwerty(PhysicalKey.M, RawInputModifiers.None); break;
            }
            Pump(w);
            _out.WriteLine($"after second step: screen={vm.CurrentScreen} export={vm.ExportPanel?.DocumentTitle} email={vm.EmailCompose?.DocumentTitle} wa={vm.WhatsAppShare?.DocumentTitle}");

            string artefact = string.Empty;
            Directory.CreateDirectory(dir);
            if (vm.CurrentScreen == Screen.Export && vm.ExportPanel is { } ep)
            {
                ep.Folder = dir; ep.Format = ExportFormat.Csv; ep.AppendTimestamp = false;
                var path = ep.BuildConfig().FullPath;
                w.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control); Pump(w);
                artefact = File.Exists(path) ? Encoding.Latin1.GetString(File.ReadAllBytes(path)) : "";
            }
            else if (vm.CurrentScreen == Screen.EmailCompose && vm.EmailCompose is { } em)
            {
                em.To = "x@example.com";
                var path = Path.Combine(dir, "p.eml");
                vm.SaveEmail(path);
                var raw = File.ReadAllText(path, Encoding.Latin1);
                var sb = new StringBuilder(raw);
                foreach (var part in raw.Split("\r\n\r\n"))
                {
                    var cand = string.Concat(part.Split('\r', '\n').TakeWhile(x => !x.StartsWith("--", StringComparison.Ordinal)));
                    if (cand.Length < 8) continue;
                    try { sb.Append(Encoding.Latin1.GetString(Convert.FromBase64String(cand))); } catch (FormatException) { }
                }
                artefact = sb.ToString();
            }
            else if (vm.CurrentScreen == Screen.WhatsAppShare)
            {
                var path = Path.Combine(dir, "p.pdf");
                vm.SaveWhatsAppDocument(path);
                artefact = Encoding.Latin1.GetString(File.ReadAllBytes(path));
            }
            _out.WriteLine($"artefact contains Theirs={artefact.Contains(Theirs)} DayBook={artefact.Contains("Day Book")} len={artefact.Length}");
            Assert.DoesNotContain(Theirs, artefact, StringComparison.Ordinal);
            Assert.DoesNotContain("Day Book", artefact, StringComparison.Ordinal);
            Assert.True(vm.ExportPanel is null || vm.ExportPanel.DocumentTitle != "Day Book", route + ": exported the Day Book");
            Assert.True(vm.EmailCompose is null || vm.EmailCompose.DocumentTitle != "Day Book", route + ": e-mailed the Day Book");
            Assert.True(vm.WhatsAppShare is null || vm.WhatsAppShare.DocumentTitle != "Day Book", route + ": shared the Day Book");
        }
        finally
        {
            w.Close();
            try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); Directory.Delete(dir, true); } catch (IOException) { }
        }
    }

    /// <summary>Positive control: a Print Preview stacked directly over the REPORT keeps the report as context, so
    /// Ctrl+M there still composes the report (the Printer/E-Mail-over-preview route census 12.5 relies on).</summary>
    [AvaloniaFact]
    public void A_report_preview_still_e_mails_the_report()
    {
        var (w, vm, dir, ours, theirs, i1, i2, gd) = Setup("Probe Co control");
        try
        {
            PostDn(vm, ours, i1, gd, 4m);
            vm.OpenReport(ReportKind.DayBook);
            Pump(w);
            w.KeyPressQwerty(PhysicalKey.P, RawInputModifiers.None);
            Pump(w);
            Assert.Equal(Screen.PrintPreview, vm.CurrentScreen);
            Assert.True(vm.IsReportContext);
            w.Focus();
            Pump(w);
            w.KeyPressQwerty(PhysicalKey.M, RawInputModifiers.Control);
            Pump(w);
            Assert.Equal(Screen.EmailCompose, vm.CurrentScreen);
            Assert.Equal("Day Book", vm.EmailCompose!.DocumentTitle);
        }
        finally
        {
            w.Close();
            try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); Directory.Delete(dir, true); } catch (IOException) { }
        }
    }
}
