using System;
using System.IO;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.Tests;

/// <summary>
/// 🔴 <b>ONE RATE RULE, ON EVERY SURFACE — the T0-18 / T0-19 / T0-20 family, asserted together.</b>
///
/// <para>The three gap-register rows are one defect wearing three hats: <b>the rate a line resolves must be the
/// rate for THAT DATE, by THAT ORDER, on EVERY surface.</b> Each of the three broke a different clause of that
/// one sentence, and each was invisible to the others' tests because every one of them shipped green:</para>
/// <list type="bullet">
///   <item><b>T0-18</b> — <c>RcmService</c>'s import-of-services limb resolved its reverse-charge rate with a
///     hand-written two-rung <c>item ?? ledger</c> pick, <b>no supply date</b> and a hard-coded <c>1800</c>
///     floor, while the domestic limb fifteen lines below it in the same method called
///     <c>_gst.ResolveRate(item, spLedger, supplyDate)</c>. Reverse charge is the recipient's own liability, so a
///     wrong figure here is paid by us AND claimed by us as ITC.</item>
///   <item><b>T0-19</b> — both POS rate resolutions used the DATE-BLIND two-argument overload (which forwarded
///     <c>voucherDate: null</c>), so a dated <c>GstRateHistory</c> override never fired at the counter, while all
///     four <c>VoucherEntryViewModel</c> sites passed <c>Date</c>.</item>
///   <item><b>T0-20</b> — the dated override keyed on an ITEM-FIRST HSN pick, so on a <c>LedgerFirst</c> book the
///     base rate came from the LEDGER while the override that replaced it was matched on the ITEM's HSN.</item>
/// </list>
///
/// <para><b>Why one class and not three pins.</b> Three isolated tests would each have passed against a fix that
/// repaired only its own call site; what the family needs asserted is the INVARIANT — that the counter, the item
/// invoice, the accounting invoice, the reverse-charge engine and <c>GstService</c> itself all answer the same
/// question the same way for the same masters on the same day. <see cref="Every_surface_resolves_the_same_rate_for_the_same_item_on_the_same_day"/>
/// is that assertion; the rest name the individual shapes so a regression reports which clause broke.</para>
///
/// <para><b>Every figure below is derived by hand and asserted to the paisa.</b> The dated windows are the
/// SEEDED ones already in the shipped tree (<c>SeedGstRates.BuildDefaultRateHistory</c>, which carries its own R7
/// provenance) — this file asserts that our engine consults its own dated table consistently and makes no
/// statutory claim of its own. Where an HSN is attached to a fixture whose trade it does not describe (the
/// reverse-charge ledger below), that is a FIXTURE pick of an existing dated window, said so in place, and not a
/// classification claim.</para>
/// </summary>
public sealed class RateResolutionOneRuleTests : IDisposable
{
    private const string GstinMaharashtra = "27AAPFU0939F1ZV";

    private static readonly DateOnly FyStart = new(2025, 4, 1);

    /// <summary>20-Sep-2025 — inside the LEGACY window of every seeded HSN row (…21-Sep-2025 inclusive).</summary>
    private static readonly DateOnly BeforeCutover = new(2025, 9, 20);

    /// <summary>25-Sep-2025 — inside the GST 2.0 window of every seeded HSN row (22-Sep-2025 onward).</summary>
    private static readonly DateOnly AfterCutover = new(2025, 9, 25);

    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public RateResolutionOneRuleTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexOneRateRule_" + Guid.NewGuid().ToString("N"));
        _storage = new CompanyStorage(_tempDir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch (IOException) { /* a locked temp file must never fail a test */ }
    }

    // ================================================================ fixture

    private sealed class Kit
    {
        public required MainWindowViewModel Vm { get; init; }
        public required Company Company { get; init; }
        public required VoucherType PosType { get; init; }
        public required Guid CarId { get; init; }
        public required Guid MainGodownId { get; init; }
        public required Guid SalesLedgerId { get; init; }
        public required Guid CustomerId { get; init; }
    }

    private static DomainLedger AddLedger(Company c, string name, string groupName, bool openingIsDebit = false)
    {
        var group = c.FindGroupByName(groupName) ?? throw new InvalidOperationException($"No group '{groupName}'.");
        var ledger = new DomainLedger(Guid.NewGuid(), name, group.Id, Money.Zero, openingIsDebit);
        c.AddLedger(ledger);
        return ledger;
    }

    /// <summary>
    /// A GST-enabled (home MH 27) advanced-GST company carrying the seeded dated rate history, a Car (HSN 8703 —
    /// seeded 28% legacy → 40% from 22-Sep-2025) with opening stock, a POS-flagged Sales type with its tender
    /// ledger, and an in-state B2B customer. The Car's own scalar rate is 4000 bp so that on
    /// <see cref="BeforeCutover"/> the DATED answer (2800) and the UNDATED one (4000) differ — which is what makes
    /// the counter-vs-invoice assertion discriminating rather than merely green.
    /// </summary>
    private Kit NewKit(string companyName)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = companyName;
        vm.CreateCompany();

        var c = vm.Company!;
        c.FinancialYearStart = FyStart;
        c.BooksBeginFrom = FyStart;

        var gst = new GstService(c);
        gst.EnableGst(new GstConfig
        {
            HomeStateCode = "27",
            Gstin = GstinMaharashtra,
            RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart,
            Periodicity = GstReturnPeriodicity.Monthly,
        });
        gst.SeedAdvancedGst();

        var inv = new InventoryService(c);
        var grp = inv.CreateStockGroup("Vehicles");
        var nos = inv.CreateSimpleUnit("Nos", "Numbers", unitQuantityCode: "NOS");
        var main = c.MainLocation!.Id;

        var car = inv.CreateStockItem("Car", grp.Id, nos.Id);
        car.Gst = new StockItemGstDetails { HsnSac = "8703", Taxability = GstTaxability.Taxable, RateBasisPoints = 4000 };
        inv.AddOpeningBalance(car.Id, main, 10m, Money.FromRupees(500000m));

        var sales = AddLedger(c, "Sales", "Sales Accounts");
        AddLedger(c, "Cash", "Cash-in-Hand", openingIsDebit: true);

        var customer = AddLedger(c, "Local Customer", "Sundry Debtors", openingIsDebit: true);
        customer.PartyGst = new PartyGstDetails
        {
            RegistrationType = GstRegistrationType.Regular, Gstin = GstinMaharashtra, StateCode = "27",
        };

        var posType = new VoucherType(Guid.NewGuid(), "Sales (POS)", VoucherBaseType.Sales, useForPos: true,
            posConfig: new PosConfig { DefaultTitle = "Retail Invoice" });
        c.AddVoucherType(posType);

        _storage.Save(c);

        return new Kit
        {
            Vm = vm, Company = c, PosType = posType, CarId = car.Id, MainGodownId = main,
            SalesLedgerId = sales.Id, CustomerId = customer.Id,
        };
    }

    // ---------------------------------------------------------------- surface drivers

    /// <summary>Sells one Car over the counter on <paramref name="date"/> and returns the screen's own CGST/SGST
    /// cells — the figures the operator reads and the ones <c>BuildPosBill</c> posts (both come from the single
    /// <c>ComputeGst</c>).</summary>
    private (string Cgst, string Sgst) CounterTax(Kit k, DateOnly date, string rate)
    {
        var pos = new PosBillingViewModel(k.Company, k.PosType, _storage, () => { }, () => { });
        pos.Date = date;                                   // the bill date FIRST, exactly as the operator keys it
        pos.SelectedSalesLedger = pos.SalesLedgers.Single(l => l.Id == k.SalesLedgerId);

        var line = pos.Items[0];
        line.SelectedItem = k.Company.StockItems.Single(i => i.Id == k.CarId);
        line.SelectedGodown = pos.Godowns.Single(g => g.Id == k.MainGodownId);
        line.QuantityText = "1";
        line.RateText = rate;
        pos.Recalculate();

        return (pos.GstCgstText, pos.GstSgstText);
    }

    /// <summary>Sells one Car on a Sales ITEM INVOICE on <paramref name="date"/> and returns the screen's own
    /// CGST/SGST cells.</summary>
    private static (string Cgst, string Sgst) InvoiceTax(Kit k, DateOnly date, string rate)
    {
        k.Vm.OpenVoucher(VoucherBaseType.Sales);
        var entry = k.Vm.VoucherEntry!;
        k.Vm.ToggleItemInvoice();

        entry.Date = date;
        entry.SelectedParty = entry.Parties.Single(p => p.Ledger?.Id == k.CustomerId);
        while (entry.InventoryLines.Count <= 0) entry.AddInventoryLine();
        var line = entry.InventoryLines[0];
        line.SelectedItem = entry.StockItems.Single(i => i.Id == k.CarId);
        line.SelectedGodown = entry.Godowns.Single(g => g.Id == k.MainGodownId);
        line.QuantityText = "1";
        line.RateText = rate;

        return (entry.GstCgstText, entry.GstSgstText);
    }

    // ================================================================ THE INVARIANT

    /// <summary>
    /// 🔴 <b>THE FAMILY INVARIANT, ON EVERY SURFACE AT ONCE.</b> One Car, one book, one price — sold on the two
    /// days that straddle the seeded HSN-8703 window boundary. <c>GstService</c>, the POS counter and the Sales
    /// item invoice must all answer with the SAME rate on the SAME day.
    ///
    /// <para><b>DERIVED BY HAND.</b> Taxable ₹10,00,000.00.
    /// On 20-Sep-2025 the seeded row "Car 28% (legacy)" (2017-07-01 … 2025-09-21, inclusive) is in force:
    /// 10,00,000.00 × 2800 / 10000 = ₹2,80,000.00, split intra-State into CGST ₹1,40,000.00 + SGST ₹1,40,000.00.
    /// On 25-Sep-2025 the row "Car 40% (GST 2.0)" (from 22-Sep-2025, open) is in force:
    /// 10,00,000.00 × 4000 / 10000 = ₹4,00,000.00 ⇒ CGST ₹2,00,000.00 + SGST ₹2,00,000.00.</para>
    ///
    /// <para><b>What made this RED (T0-19).</b> The counter's <c>ResolveRate</c> dropped the date, so it read the
    /// Car's own undated 4000 bp scalar on BOTH days and billed ₹2,00,000.00 + ₹2,00,000.00 on 20-Sep — ₹1,20,000
    /// of tax collected over the counter that the very same sale on an invoice, the same day, did not charge. The
    /// 25-Sep leg is deliberately kept: it is the leg where the two AGREE, and without it a "fix" that simply
    /// broke the invoice instead would pass.</para>
    /// </summary>
    [Fact]
    public void Every_surface_resolves_the_same_rate_for_the_same_item_on_the_same_day()
    {
        var k = NewKit("One Rate Rule Co");
        var gst = new GstService(k.Company);
        var car = k.Company.StockItems.Single(i => i.Id == k.CarId);
        var salesLedger = k.Company.FindLedger(k.SalesLedgerId)!;

        // ---- 20-Sep-2025: the legacy window.
        Assert.Equal(2800, gst.ResolveRate(car, salesLedger, BeforeCutover).RateBasisPoints);

        var counterBefore = CounterTax(k, BeforeCutover, "1000000.00");
        Assert.Equal("1,40,000.00", counterBefore.Cgst);
        Assert.Equal("1,40,000.00", counterBefore.Sgst);

        var invoiceBefore = InvoiceTax(k, BeforeCutover, "1000000.00");
        Assert.Equal("1,40,000.00", invoiceBefore.Cgst);
        Assert.Equal("1,40,000.00", invoiceBefore.Sgst);

        Assert.Equal(invoiceBefore, counterBefore);   // the invariant itself, stated as one equality

        // ---- 25-Sep-2025: the GST 2.0 window.
        Assert.Equal(4000, gst.ResolveRate(car, salesLedger, AfterCutover).RateBasisPoints);

        var counterAfter = CounterTax(k, AfterCutover, "1000000.00");
        Assert.Equal("2,00,000.00", counterAfter.Cgst);
        Assert.Equal("2,00,000.00", counterAfter.Sgst);

        var invoiceAfter = InvoiceTax(k, AfterCutover, "1000000.00");
        Assert.Equal("2,00,000.00", invoiceAfter.Cgst);
        Assert.Equal("2,00,000.00", invoiceAfter.Sgst);

        Assert.Equal(invoiceAfter, counterAfter);

        // ...and the two days really do differ, so neither pair above is vacuous.
        Assert.NotEqual(counterBefore, counterAfter);
    }

    /// <summary>
    /// <b>T0-19, POSTED not previewed.</b> The counter's preview and its posting share one <c>ComputeGst</c>, so
    /// the dated rate must reach the LEDGER BALANCES too. One Car at ₹10,00,000.00 on 20-Sep-2025, accepted
    /// through the real POS screen, must credit Output CGST and Output SGST ₹1,40,000.00 each (28%), never
    /// ₹2,00,000.00 (the undated 40%).
    /// </summary>
    [Fact]
    public void The_counter_POSTS_the_dated_rate_not_the_items_undated_scalar()
    {
        var k = NewKit("Counter Posts Dated Co");

        var pos = new PosBillingViewModel(k.Company, k.PosType, _storage, () => { }, () => { });
        pos.Date = BeforeCutover;
        pos.SelectedSalesLedger = pos.SalesLedgers.Single(l => l.Id == k.SalesLedgerId);
        var line = pos.Items[0];
        line.SelectedItem = k.Company.StockItems.Single(i => i.Id == k.CarId);
        line.SelectedGodown = pos.Godowns.Single(g => g.Id == k.MainGodownId);
        line.QuantityText = "1";
        line.RateText = "1000000.00";
        pos.Recalculate();

        Assert.True(pos.Accept(), pos.Message);

        var c = k.Company;
        var gst = new GstService(c);
        var asOf = new DateOnly(2026, 3, 31);
        Assert.Equal(-140000m, LedgerBalances.SignedClosing(
            c, gst.FindTaxLedger(GstTaxHead.Central, GstTaxDirection.Output)!, asOf));
        Assert.Equal(-140000m, LedgerBalances.SignedClosing(
            c, gst.FindTaxLedger(GstTaxHead.State, GstTaxDirection.Output)!, asOf));
    }

    // ================================================================ T0-20 — the override key follows the walk

    /// <summary>
    /// A book whose Sales ledger and stock item declare DIFFERENT HSNs, both of which carry a seeded dated window:
    /// the ledger says cement (2523 — 28% legacy → <b>18%</b> from 22-Sep-2025), the item says car (8703 — 28%
    /// legacy → <b>40%</b>). The two disagree on and only on <see cref="AfterCutover"/>, which is what makes the
    /// key visible.
    /// </summary>
    private (GstService Gst, StockItem Item, DomainLedger Ledger, Company Company) TwoHsnBook(GstDetailSource order)
    {
        var c = CompanyFactory.CreateSeeded("Two HSN Co", FyStart);
        var gst = new GstService(c);
        gst.EnableGst(new GstConfig
        {
            HomeStateCode = "27",
            Gstin = GstinMaharashtra,
            RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart,
            Periodicity = GstReturnPeriodicity.Monthly,
        });
        gst.SeedAdvancedGst();
        c.Gst!.SourceOfGstRate = order;

        var inv = new InventoryService(c);
        var grp = inv.CreateStockGroup("Goods");
        var nos = inv.CreateSimpleUnit("Nos", "Numbers", unitQuantityCode: "NOS");
        var item = inv.CreateStockItem("Car", grp.Id, nos.Id);
        item.Gst = new StockItemGstDetails { HsnSac = "8703", Taxability = GstTaxability.Taxable, RateBasisPoints = 4000 };

        var ledger = AddLedger(c, "Sales — Cement", "Sales Accounts");
        ledger.SalesPurchaseGst = new StockItemGstDetails
        {
            HsnSac = "2523", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800,
        };

        return (gst, item, ledger, c);
    }

    /// <summary>
    /// 🔴 <b>T0-20 — the dated override must be keyed by the SAME master that supplied the rate.</b>
    ///
    /// <para>On a <c>LedgerFirst</c> book the walk stops at the Sales ledger, so the base rate is the ledger's
    /// 1800 bp and the classification in play is the LEDGER's HSN 2523. On 25-Sep-2025 the seeded row "Cement 18%
    /// (GST 2.0)" is in force for 2523 ⇒ <b>1800 bp</b>. The override was instead looked up on
    /// <c>item?.Gst?.HsnSac ?? ledger…</c>, which found 8703 and returned "Car 40% (GST 2.0)" ⇒ <b>4000 bp</b> —
    /// a rate belonging to a classification this line never resolved through. That is not a refinement of the
    /// walk, it is a second and inconsistent resolution.</para>
    ///
    /// <para>The <c>StockItemFirst</c> half is asserted in the same test on purpose: it is the leg that proves the
    /// fix FOLLOWS the order rather than merely flipping the hard-coded pick from item-first to ledger-first. On a
    /// migrated (item-first) book the item answers, so 8703 → 4000 bp is the correct answer there.</para>
    /// </summary>
    [Fact]
    public void The_dated_override_is_keyed_by_the_master_that_supplied_the_rate()
    {
        var (ledgerFirst, lfItem, lfLedger, _) = TwoHsnBook(GstDetailSource.LedgerFirst);

        // Base (undated) resolution already stops at the ledger — without this the dated claim would be unanchored.
        Assert.Equal(1800, ledgerFirst.ResolveRate(lfItem, lfLedger, voucherDate: null).RateBasisPoints);
        // ...and the dated override must stay on the ledger's own classification.
        Assert.Equal(1800, ledgerFirst.ResolveRate(lfItem, lfLedger, AfterCutover).RateBasisPoints);
        // The legacy window: 2523 and 8703 were both 28% before the cut-over, so this leg pins the WINDOW, not the key.
        Assert.Equal(2800, ledgerFirst.ResolveRate(lfItem, lfLedger, BeforeCutover).RateBasisPoints);

        var (itemFirst, ifItem, ifLedger, _) = TwoHsnBook(GstDetailSource.StockItemFirst);

        Assert.Equal(4000, itemFirst.ResolveRate(ifItem, ifLedger, voucherDate: null).RateBasisPoints);
        Assert.Equal(4000, itemFirst.ResolveRate(ifItem, ifLedger, AfterCutover).RateBasisPoints);
        Assert.Equal(2800, itemFirst.ResolveRate(ifItem, ifLedger, BeforeCutover).RateBasisPoints);
    }

    /// <summary>
    /// <b>T0-20, the rungs the two-rung pick could never see.</b> A <c>LedgerFirst</c> book whose Sales ledger
    /// declares NO GST block at all, but whose accounting GROUP does — HSN 2523 at 1800 bp. The walk resolves the
    /// rate at the Accounting-Group rung, so the override must be keyed on 2523 (⇒ 1800 bp on 25-Sep-2025), not on
    /// the stock item's 8703 (⇒ 4000 bp). The old pick consulted only the two DETAILED rungs, so a rate resolved
    /// at a group or company rung could only ever be overridden by some other master's classification.
    /// </summary>
    [Fact]
    public void A_rate_resolved_at_a_group_rung_is_overridden_on_that_rungs_own_HSN()
    {
        var (gst, item, ledger, c) = TwoHsnBook(GstDetailSource.LedgerFirst);

        // Strip the ledger's own block and hang the same classification on a dedicated parent group instead.
        ledger.SalesPurchaseGst = null;
        var salesAccounts = c.FindGroupByName("Sales Accounts")!;
        var cementSales = new Group(Guid.NewGuid(), "Sales — Cement (group)", salesAccounts.Nature, salesAccounts.Id)
        {
            Gst = new MasterGstDetails { HsnSac = "2523", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800 },
        };
        c.AddGroup(cementSales);
        ledger.GroupId = cementSales.Id;

        Assert.Equal(1800, gst.ResolveRate(item, ledger, voucherDate: null).RateBasisPoints);
        Assert.Equal(1800, gst.ResolveRate(item, ledger, AfterCutover).RateBasisPoints);
    }

    /// <summary>
    /// 🔴 <b>T0-20's own tail: keying the override off the walk must not resurrect an unpostable book.</b>
    ///
    /// <para><see cref="GstService"/>'s hierarchy is lazy as a CORRECTNESS property — a corrupt (cyclic) group
    /// chain hanging BELOW the rung that answered is never built, so one bad parent id cannot make every line on
    /// an otherwise-fine item unpostable (<c>GstHierarchyAncestryTests.A_cycle_below_an_answering_item_rung_is_never_reached</c>).
    /// The classification walk asks ONE question further than the rate walk — "does any rung declare an HSN?" — and
    /// on a book carrying dated rows it therefore reaches rungs the rate walk deliberately skipped. Left unguarded
    /// that would have re-opened exactly the shape that property exists to prevent.</para>
    ///
    /// <para>The fixture is the same one that pins the property, plus the two things that make the new walk run at
    /// all: seeded dated rows and a voucher date. The item answers for itself at 1800 bp and declares no HSN, so
    /// the classification walk falls past it into the cycle. It must still resolve 1800 and post.</para>
    /// </summary>
    [Fact]
    public void A_cycle_below_the_answering_rung_stays_unreachable_on_a_book_with_dated_rows()
    {
        var c = CompanyFactory.CreateSeeded("Cyclic Below Co", FyStart);
        var gst = new GstService(c);
        gst.EnableGst(new GstConfig
        {
            HomeStateCode = "27", Gstin = GstinMaharashtra, RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart, Periodicity = GstReturnPeriodicity.Monthly,
        });
        gst.SeedAdvancedGst();                                  // the dated rows the classification walk needs

        var inv = new InventoryService(c);
        var a = inv.CreateStockGroup("SG A");
        var b = inv.CreateStockGroup("SG B", a.Id);
        a.ParentId = b.Id;                                      // A -> B -> A
        var nos = inv.CreateSimpleUnit("Nos", "Numbers");
        var item = inv.CreateStockItem("Self-Rated Widget", b.Id, nos.Id);
        // Rate but NO HSN: the rate walk stops here, the classification walk does not.
        item.Gst = new StockItemGstDetails { Taxability = GstTaxability.Taxable, RateBasisPoints = 1800 };

        Assert.Equal(1800, gst.ResolveRate(item, salesPurchaseLedger: null, AfterCutover).RateBasisPoints);
    }

    /// <summary>
    /// The mirror, so the guard above is a narrowing and not a blanket swallow: a cycle the RATE walk itself
    /// reaches still fails fast, with a date and dated rows in play.
    /// </summary>
    [Fact]
    public void A_cycle_the_rate_walk_reaches_still_fails_fast_with_a_date()
    {
        var c = CompanyFactory.CreateSeeded("Cyclic Above Co", FyStart);
        var gst = new GstService(c);
        gst.EnableGst(new GstConfig
        {
            HomeStateCode = "27", Gstin = GstinMaharashtra, RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart, Periodicity = GstReturnPeriodicity.Monthly,
        });
        gst.SeedAdvancedGst();

        var inv = new InventoryService(c);
        var a = inv.CreateStockGroup("SG A");
        var b = inv.CreateStockGroup("SG B", a.Id);
        a.ParentId = b.Id;
        var nos = inv.CreateSimpleUnit("Nos", "Numbers");
        var item = inv.CreateStockItem("Unrated Widget", b.Id, nos.Id);   // declares nothing ⇒ the walk reaches SG

        var ex = Assert.Throws<InvalidOperationException>(
            () => gst.ResolveRate(item, salesPurchaseLedger: null, AfterCutover));
        Assert.Contains("cycle", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ================================================================ T0-18 — reverse charge, import of services

    /// <summary>An advanced-GST company with an expense ledger for an imported service, plus the Input tax ledgers
    /// <c>BuildReverseCharge</c> needs for the ITC leg.</summary>
    private static (RcmService Rcm, GstService Gst, Company Company) RcmBook()
    {
        var c = CompanyFactory.CreateSeeded("Import RCM Co", FyStart);
        var gst = new GstService(c);
        gst.EnableGst(new GstConfig
        {
            HomeStateCode = "27",
            Gstin = GstinMaharashtra,
            RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart,
            Periodicity = GstReturnPeriodicity.Monthly,
        });
        gst.SeedAdvancedGst();
        return (new RcmService(c), gst, c);
    }

    private static PartyGstDetails ForeignSupplier() => new()
    {
        RegistrationType = GstRegistrationType.Unregistered, Gstin = null, StateCode = null,
    };

    /// <summary>The IGST amount on the reverse-charge OUTPUT (liability) leg of a posting.</summary>
    private static Money RcmOutputIgst(RcmService.RcmPosting p) =>
        p.OutputLines
            .Where(l => l.Gst?.TaxHead == GstTaxHead.Integrated)
            .Aggregate(Money.Zero, (a, l) => a + l.Amount);

    /// <summary>
    /// 🔴 <b>T0-18 (a) — the import-of-services rate must be resolved through the HIERARCHY, not a two-rung pick.</b>
    ///
    /// <para>The expense ledger declares no GST block; its parent accounting group does, at <b>500 bp</b>. The
    /// five-rung walk answers 500 at the Accounting-Group rung — which is exactly the rung the old hand-written
    /// <c>supplyGst?.RateBasisPoints ?? spLedger?.SalesPurchaseGst?.RateBasisPoints ?? 1800</c> could not see, so
    /// it fell all the way through to the hard-coded 18% floor.</para>
    ///
    /// <para><b>DERIVED BY HAND.</b> Taxable ₹1,00,000.00 at 500 bp ⇒ IGST ₹5,000.00 (import of services is always
    /// IGST, §5(3)). The defect posted ₹18,000.00 — ₹13,000.00 of reverse-charge liability paid in cash by us and
    /// claimed back by us as ITC, on a figure sourced from nowhere.</para>
    /// </summary>
    [Fact]
    public void Import_of_services_resolves_its_rate_through_the_hierarchy()
    {
        var (rcm, _, c) = RcmBook();

        var indirect = c.FindGroupByName("Indirect Expenses")!;
        var importedServices = new Group(Guid.NewGuid(), "Imported Services", indirect.Nature, indirect.Id)
        {
            Gst = new MasterGstDetails
            {
                Taxability = GstTaxability.Taxable, RateBasisPoints = 500, SupplyType = GstSupplyType.Services,
            },
        };
        c.AddGroup(importedServices);

        var expense = new DomainLedger(Guid.NewGuid(), "Overseas Design Fees", importedServices.Id, Money.Zero, true);
        c.AddLedger(expense);

        var posting = rcm.BuildReverseCharge(
            Money.FromRupees(100000m), item: null, expense, ForeignSupplier(),
            AfterCutover, RcmService.SupplyKind.ImportOfServices);

        Assert.True(posting.Applies);
        Assert.Equal(500, posting.Resolution.RateBasisPoints);
        Assert.True(posting.Resolution.InterState);
        Assert.Equal(Money.FromRupees(5000m), RcmOutputIgst(posting));
    }

    /// <summary>
    /// 🔴 <b>T0-18 (b) — the import-of-services rate must be resolved AS OF THE SUPPLY DATE.</b>
    ///
    /// <para>The expense ledger carries HSN 2523 and a stale 2800 bp scalar. <b>The HSN is a FIXTURE pick of an
    /// existing seeded dated window</b> (its 28% → 18% pair straddles 22-Sep-2025) chosen so this file invents no
    /// dated row of its own; it is not a claim about how any imported service is classified.</para>
    ///
    /// <para><b>DERIVED BY HAND.</b> Taxable ₹1,00,000.00. On 20-Sep-2025 the legacy window gives 2800 bp ⇒ IGST
    /// ₹28,000.00; on 25-Sep-2025 the GST 2.0 window gives 1800 bp ⇒ IGST ₹18,000.00. The date-blind limb read the
    /// ledger's own 2800 scalar and posted ₹28,000.00 on BOTH days.</para>
    ///
    /// <para>Note what the 25-Sep leg does NOT prove on its own: 1800 bp is also the value of the deleted
    /// hard-coded floor, so the 20-Sep leg — where the correct answer is 2800 and the floor would have been 1800 —
    /// is the one that separates "resolved" from "guessed". Both are asserted.</para>
    /// </summary>
    [Fact]
    public void Import_of_services_resolves_its_rate_as_of_the_supply_date()
    {
        var (rcm, _, c) = RcmBook();

        var expense = new DomainLedger(
            Guid.NewGuid(), "Imported Testing Services", c.FindGroupByName("Indirect Expenses")!.Id, Money.Zero, true);
        expense.SalesPurchaseGst = new StockItemGstDetails
        {
            HsnSac = "2523", Taxability = GstTaxability.Taxable, RateBasisPoints = 2800,
            SupplyType = GstSupplyType.Services,
        };
        c.AddLedger(expense);

        var before = rcm.BuildReverseCharge(
            Money.FromRupees(100000m), item: null, expense, ForeignSupplier(),
            BeforeCutover, RcmService.SupplyKind.ImportOfServices);
        Assert.Equal(2800, before.Resolution.RateBasisPoints);
        Assert.Equal(Money.FromRupees(28000m), RcmOutputIgst(before));

        var after = rcm.BuildReverseCharge(
            Money.FromRupees(100000m), item: null, expense, ForeignSupplier(),
            AfterCutover, RcmService.SupplyKind.ImportOfServices);
        Assert.Equal(1800, after.Resolution.RateBasisPoints);
        Assert.Equal(Money.FromRupees(18000m), RcmOutputIgst(after));
    }

    /// <summary>
    /// 🔴 <b>T0-18 (c) — with no rate declared anywhere, the engine REFUSES rather than guessing 18%.</b>
    ///
    /// <para>R7 forbids a rate constant with no citation, and this project has already had to strip such constants
    /// out of shipped code. The <c>?? 1800</c> floor was exactly that: a statutory figure asserted from nowhere,
    /// applied to the recipient's own cash liability. It is deleted, not re-sourced. A taxable line with a rate at
    /// no rung at all is the ordinary ER-5 unresolved case and fails fast, as every forward-charge posting path
    /// already does — never a silent figure.</para>
    /// </summary>
    [Fact]
    public void Import_of_services_with_no_rate_anywhere_fails_fast_instead_of_defaulting_to_18pct()
    {
        var (rcm, _, c) = RcmBook();

        // No block on the ledger, none on its group, and no company default ⇒ the ER-5 unresolved sentinel.
        var expense = new DomainLedger(
            Guid.NewGuid(), "Overseas Consulting", c.FindGroupByName("Indirect Expenses")!.Id, Money.Zero, true);
        c.AddLedger(expense);

        var ex = Assert.Throws<InvalidOperationException>(() => rcm.BuildReverseCharge(
            Money.FromRupees(100000m), item: null, expense, ForeignSupplier(),
            AfterCutover, RcmService.SupplyKind.ImportOfServices));

        Assert.Contains("rate", ex.Message, StringComparison.OrdinalIgnoreCase);
        // The refusal must not have conjured a liability leg on the way out.
        Assert.DoesNotContain(c.Ledgers, l => l.Name.Contains("RCM Output", StringComparison.Ordinal)
                                              && LedgerBalances.SignedClosing(c, l, AfterCutover) != 0m);
    }

    /// <summary>
    /// <b>T0-18 (c), on the SCREEN — the refusal must be reachable, named and non-crashing.</b>
    ///
    /// <para>The engine's fail-fast is only half a fix: <c>UpdateRcmPanel</c> re-resolves on every keystroke and
    /// would otherwise do arithmetic on the <c>-1</c> unresolved sentinel and quote the operator a NEGATIVE
    /// reverse-charge liability, and Accept must refuse with a message rather than throw. This drives the real
    /// Purchase screen with a reverse-charge-flagged expense ledger that declares a taxability but <b>no rate</b>,
    /// and no rate at any rung above it — the exact shape the deleted <c>?? 1800</c> floor used to swallow.</para>
    ///
    /// <para>Without this the new guard would be dead code: nothing else in the suite reaches it.</para>
    /// </summary>
    [Fact]
    public void The_screen_names_an_unresolved_reverse_charge_rate_and_refuses_the_voucher()
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = "Rateless RCM Co";
        vm.CreateCompany();
        var c = vm.Company!;
        c.FinancialYearStart = FyStart;
        c.BooksBeginFrom = FyStart;

        var gst = new GstService(c);
        gst.EnableGst(new GstConfig
        {
            HomeStateCode = "27",
            Gstin = GstinMaharashtra,
            RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart,
            Periodicity = GstReturnPeriodicity.Monthly,
        });
        gst.SeedAdvancedGst();

        // Reverse-charge flagged (so the screen detects the shape) but declaring NO rate — and no rung above it
        // declares one either, so the hierarchy resolves the ER-5 unresolved sentinel.
        var expense = AddLedger(c, "Overseas Consulting", "Indirect Expenses", openingIsDebit: true);
        expense.SalesPurchaseGst = new StockItemGstDetails
        {
            Taxability = GstTaxability.Taxable,
            SupplyType = GstSupplyType.Services,
            ReverseChargeApplicable = true,
        };
        var supplier = AddLedger(c, "Overseas Consultant", "Sundry Creditors");
        supplier.PartyGst = new PartyGstDetails
        {
            RegistrationType = GstRegistrationType.Unregistered, Gstin = null, StateCode = null,
        };
        _storage.Save(c);

        vm.OpenVoucher(VoucherBaseType.Purchase);
        var e = vm.VoucherEntry!;
        e.Date = AfterCutover;
        e.Lines[0].SelectedLedger = expense;
        e.Lines[0].Side = DrCr.Debit;
        e.Lines[0].AmountText = "100000.00";
        e.Lines[1].SelectedLedger = supplier;
        e.Lines[1].Side = DrCr.Credit;
        e.Lines[1].AmountText = "100000.00";
        e.Recalculate();
        e.SelectedRcmSupplyKind = e.RcmSupplyKinds.First(k => k.Kind == RcmService.SupplyKind.ImportOfServices);

        // The panel names the problem instead of quoting a figure (the -1 sentinel would have read "-0.01%").
        Assert.Equal("Yes — reverse charge applies", e.RcmAppliesText);
        Assert.Equal("—", e.RcmRateText);
        Assert.Equal("0.00", e.RcmTaxText);
        Assert.Contains("Overseas Consulting", e.RcmSummary, StringComparison.Ordinal);
        Assert.Contains("no GST rate is declared", e.RcmSummary, StringComparison.Ordinal);

        // ...and Accept refuses with a message rather than throwing or posting an assumed 18%.
        Assert.False(e.Accept());
        Assert.NotNull(e.Message);
        Assert.Contains("rate", e.Message!, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(c.Vouchers);
    }

    /// <summary>
    /// <b>T0-18, the cross-check that makes it one rule and not two.</b> For the same masters and the same date,
    /// the rate <c>RcmService</c> self-assesses on an import of services is the rate <c>GstService.ResolveRate</c>
    /// gives — the same equality the domestic limb has always satisfied. Asserted on both sides of the window so a
    /// fix that hard-wired one date would fail.
    /// </summary>
    [Fact]
    public void The_reverse_charge_rate_equals_the_ordinary_resolved_rate_for_the_same_masters()
    {
        var (rcm, gst, c) = RcmBook();

        var expense = new DomainLedger(
            Guid.NewGuid(), "Imported Testing Services", c.FindGroupByName("Indirect Expenses")!.Id, Money.Zero, true);
        expense.SalesPurchaseGst = new StockItemGstDetails
        {
            HsnSac = "2523", Taxability = GstTaxability.Taxable, RateBasisPoints = 2800,
            SupplyType = GstSupplyType.Services,
        };
        c.AddLedger(expense);

        foreach (var date in new[] { BeforeCutover, AfterCutover })
        {
            var resolved = gst.ResolveRate(item: null, expense, date);
            var rcmRate = rcm.Resolve(
                expense.SalesPurchaseGst, ForeignSupplier(), item: null, expense, date,
                RcmService.SupplyKind.ImportOfServices);

            Assert.True(resolved.IsTaxable);
            Assert.Equal(resolved.RateBasisPoints, rcmRate.RateBasisPoints);
        }
    }
}
