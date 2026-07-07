using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Desktop.Views;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;

namespace Apex.Desktop.Tests;

// THROWAWAY render + valuation-drift probe (A10). Deleted after inspection.
public sealed class ZZRenderProbe : IDisposable
{
    private readonly string _tempDir;
    private readonly CompanyStorage _storage;
    private const string Out = @"C:\Users\dkpho\AppData\Local\Temp\claude\C--Users-dkpho-OneDrive-Desktop-Apex-Solutions-end---claude-worktrees-pensive-hellman-5627d3\37187d25-c28a-417a-86ab-83eba8e1c1ba\scratchpad";

    public ZZRenderProbe()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexRenderProbe_" + Guid.NewGuid().ToString("N"));
        _storage = new CompanyStorage(_tempDir);
    }

    private MainWindowViewModel New(string name)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = name;
        vm.CreateCompany();
        return vm;
    }
    private void EnableBom(MainWindowViewModel vm, bool types = true)
    {
        vm.ShowGstConfig();
        vm.GstConfig!.SetComponentsBom = true;
        if (types) vm.GstConfig!.DefineBomComponentType = true;
        vm.Back();
    }
    private StockGroup Grp(MainWindowViewModel vm, string n)
    { vm.ShowStockGroupMaster(); var m = vm.StockGroupMaster!; m.Name = n; m.Create(); vm.Back(); return vm.Company!.FindStockGroupByName(n)!; }
    private Unit Un(MainWindowViewModel vm, string s)
    { vm.ShowUnitMaster(); var m = vm.UnitMaster!; m.IsCompound = false; m.Symbol = s; m.FormalName = s; m.DecimalPlacesText = "0"; m.Create(); vm.Back(); return vm.Company!.FindUnitByName(s)!; }
    private StockItem It(MainWindowViewModel vm, string n, StockGroup g, Unit u, decimal? oq = null, decimal orate = 0m, bool batches = false)
    {
        vm.ShowStockItemMaster(); var m = vm.StockItemMaster!;
        m.Name = n; m.SelectedGroup = m.Groups.Single(x => x.Id == g.Id); m.SelectedUnit = m.Units.Single(x => x.Id == u.Id);
        if (batches && m.ShowBatchSwitches) m.MaintainInBatches = true;
        if (oq is { } q) { m.OpeningGodown = m.Godowns.First(x => x.IsMainLocation); m.OpeningQuantityText = q.ToString(System.Globalization.CultureInfo.InvariantCulture); m.OpeningRateText = orate.ToString(System.Globalization.CultureInfo.InvariantCulture); if (batches) m.OpeningBatchLabel = "L1"; }
        m.Create(); vm.Back(); return vm.Company!.FindStockItemByName(n)!;
    }

    private void Cap(Window w, string file)
    {
        Dispatcher.UIThread.RunJobs();
        var frame = w.CaptureRenderedFrame();
        Directory.CreateDirectory(Out);
        frame!.Save(Path.Combine(Out, file));
    }

    [AvaloniaFact]
    public void Render_bom_master()
    {
        var vm = New("Render BOM Co");
        EnableBom(vm);
        var raw = Grp(vm, "Raw Materials"); var fgG = Grp(vm, "Finished Goods"); var u = Un(vm, "Nos");
        var c1 = It(vm, "Resin", raw, u, 100m, 10m);
        var c2 = It(vm, "Pigment", raw, u, 100m, 4m);
        var scrap = It(vm, "Trimmings", raw, u);
        var fg = It(vm, "Panel", fgG, u);
        // pre-create one BOM so the list is populated
        var svc = new BomService(vm.Company!);
        svc.CreateBom(fg.Id, "Standard", 1m, new[]{
            new BomLine(BomLineType.Component, c1.Id, 2m),
            new BomLine(BomLineType.Component, c2.Id, 1m),
            new BomLine(BomLineType.Scrap, scrap.Id, 1m, rate: Money.FromRupees(2m)),
        });
        _storage.Save(vm.Company!);

        vm.ShowBomMaster();
        var m = vm.BomMaster!;
        m.Name = "Deluxe";
        m.SelectedFinishedGood = m.FinishedGoods.Single(i => i.Id == fg.Id);
        m.UnitOfManufactureText = "10";
        var l1 = m.Lines[0]; l1.SelectedItem = l1.ItemOptions.Single(i => i.Id == c1.Id); l1.QuantityText = "20";
        var l2 = m.Lines.Last(l => l.IsBlank); l2.SelectedItem = l2.ItemOptions.Single(i => i.Id == c2.Id); l2.QuantityText = "10";
        var l3 = m.Lines.Last(l => l.IsBlank); l3.SelectedItem = l3.ItemOptions.Single(i => i.Id == scrap.Id);
        l3.SelectedType = l3.TypeOptions.Single(t => t.Type == BomLineType.Scrap); l3.QuantityText = "10"; l3.CarveOutRateText = "2.00";

        var win = new MainWindow { DataContext = vm };
        win.Show();
        win.Width = 1400; win.Height = 900;
        Cap(win, "bom_master.png");
    }

    [AvaloniaFact]
    public void Render_manufacturing_journal()
    {
        var vm = New("Render Mfg Co");
        EnableBom(vm);
        var raw = Grp(vm, "Raw Materials"); var fgG = Grp(vm, "Finished Goods"); var u = Un(vm, "Nos");
        var c1 = It(vm, "Resin", raw, u, 100m, 10m);
        var c2 = It(vm, "Pigment", raw, u, 100m, 4m);
        var scrap = It(vm, "Trimmings", raw, u);
        var fg = It(vm, "Panel", fgG, u);
        var svc = new BomService(vm.Company!);
        var bom = svc.CreateBom(fg.Id, "Standard", 1m, new[]{
            new BomLine(BomLineType.Component, c1.Id, 2m),
            new BomLine(BomLineType.Component, c2.Id, 1m),
            new BomLine(BomLineType.Scrap, scrap.Id, 1m, rate: Money.FromRupees(2m)),
        });
        _storage.Save(vm.Company!);

        vm.OpenManufacturingJournal();
        var e = vm.ManufacturingJournalEntry!;
        e.SelectedFinishedGood = e.FinishedGoods.Single(i => i.Id == fg.Id);
        e.SelectedBom = e.Boms.Single(b => b.Bom.Id == bom.Id);
        e.QuantityText = "10";
        e.ConsumptionGodown = vm.Company!.MainLocation!;
        e.ProductionGodown = vm.Company!.MainLocation!;
        var cost = e.AdditionalCosts[0]; cost.Name = "Labour"; cost.AmountText = "50.00";

        var win = new MainWindow { DataContext = vm };
        win.Show();
        win.Width = 1400; win.Height = 900;
        Cap(win, "mfg_journal.png");
    }

    // ---- Valuation-display drift probes (no render) ----

    // PERCENT-based carve-out: engine carveOutTotal = round(preCarve*pct/100); UI row value = round(round(value/qty,2)*qty).
    [AvaloniaFact]
    public void Drift_percent_carveout_display_vs_engine()
    {
        var vm = New("Drift Pct Co");
        EnableBom(vm);
        var raw = Grp(vm, "Raw Materials"); var fgG = Grp(vm, "Finished Goods"); var u = Un(vm, "Nos");
        var c1 = It(vm, "Resin", raw, u, 100m, 10m);
        var scrap = It(vm, "Chips", raw, u);
        var fg = It(vm, "Panel", fgG, u);
        var svc = new BomService(vm.Company!);
        // component 1×10=10 per block. carve-out scrap qty 3, percent 33.33% of preCarve => value=round(10*0.3333)=3.33; unit=round(3.33/3)=1.11; recomputed row=round(1.11*3)=3.33 (may match). Try qty 7 pct 33.
        var bom = svc.CreateBom(fg.Id, "Std", 1m, new[]{
            new BomLine(BomLineType.Component, c1.Id, 1m),
            new BomLine(BomLineType.Scrap, scrap.Id, 7m, percentOfFinishedGoodCost: 33m),
        });
        _storage.Save(vm.Company!);

        vm.OpenManufacturingJournal();
        var e = vm.ManufacturingJournalEntry!;
        e.SelectedFinishedGood = e.FinishedGoods.Single(i => i.Id == fg.Id);
        e.SelectedBom = e.Boms.Single(b => b.Bom.Id == bom.Id);
        e.QuantityText = "100";   // preCarve = 100 units *? component 1/block*100 = 100 resin *10 = 1000; carve 33% = 330; scrap qty 700; unit=round(330/700,2)=0.47; recomputed=round(0.47*700)=329.00 != 330
        e.ConsumptionGodown = vm.Company!.MainLocation!;
        e.ProductionGodown = vm.Company!.MainLocation!;

        var mfg = new ManufacturingJournalService(vm.Company!);
        var type = vm.Company!.VoucherTypes.First(t => t.IsManufacturingJournal);
        var preview = mfg.PreviewManufacture(type.Id, bom.Id, 100m, vm.Company!.BooksBeginFrom, vm.Company!.MainLocation!.Id, vm.Company!.MainLocation!.Id);

        var sumDisplayedCarve = e.CarveOuts.Sum(r => decimal.Parse(r.Value.Replace("₹", "").Replace(",", ""), System.Globalization.CultureInfo.InvariantCulture));
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"[DRIFT-PCT] engine CarveOutTotal={preview.CarveOutTotal.Amount} displayedCarveText={e.CarveOutText} sumOfDisplayedCarveRows={sumDisplayedCarve} FGvalueText={e.FinishedGoodValueText} engineFG={preview.FinishedGoodValue.Amount}");
        foreach (var r in e.CarveOuts)
            sb.AppendLine($"[DRIFT-PCT-ROW] {r.Item} kind={r.Kind} qty={r.Quantity} value={r.Value}");
        File.WriteAllText(Path.Combine(Out, "drift_pct.txt"), sb.ToString());
    }

    public void Dispose()
    {
        try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); } catch (IOException) { }
    }
}
