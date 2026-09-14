using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.Tests;

/// <summary>
/// 🔴 <b>CENSUS ROW 2.6 — DOES A VOUCHER CLASS ACTUALLY POST?</b>
///
/// <para><b>Why this file exists, and why the existing 2.6 tests were not enough.</b> Schema v62 shipped the class
/// aggregate, its two child tables, the Voucher Type master screen that keys them and
/// <see cref="VoucherClassPosting"/>, which turns a class into legs — with <b>no production caller at all</b>.
/// <c>VoucherClassPosting.Compute</c> had zero references in <c>src/</c>; its only callers were test files, which
/// is exactly how three earlier features on this project shipped green and completely dead. The v62 tests prove
/// the class SAVES and that the ENGINE computes; none of them proves an operator can raise an invoice under a
/// class, and none of them looks at a posted voucher's legs.</para>
///
/// <para><b>So every assertion below is on <c>Voucher.Lines</c> AFTER <c>Accept()</c>.</b> A voucher class writes
/// ledger entries with no prompt: a mis-mapped ledger, a mis-signed additional entry or a round-off measured
/// against the wrong base posts wrong figures on every invoice that uses the class and says nothing. Asserting
/// that the class saves would catch none of those.</para>
///
/// <para><b>R7 — ATTESTED.</b>
/// <c>help.tallysolutions.com/tally-prime/accounting/voucher-types-tally/</c> — the class automates entries and,
/// on a Sales type, <i>"you can also specify the ledgers to be allocated automatically for inventory items under
/// Default Accounting Allocations for all items in Invoice"</i>.
/// <c>help.tallysolutions.com/tally-prime/accounting/round-off-invoice-and-ledger-values/</c> — <i>"Normal
/// Rounding"</i>, <i>"Upward Rounding"</i>, <i>"Downward Rounding"</i> and the <i>"Rounding limit"</i>, with the
/// difference posted to the round-off ledger (the page's own worked example rounds <b>the invoice</b>, 125.60 down
/// to 125, with (-)0.60 on the round-off ledger).</para>
/// </summary>
public sealed class VoucherClassPostedLegsTests : IDisposable
{
    private const string GstinMaharashtra = "27AAPFU0939F1ZV";
    private static readonly DateOnly GstFyStart = new(2024, 4, 1);

    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public VoucherClassPostedLegsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexVClassPost_" + Guid.NewGuid().ToString("N"));
        _storage = new CompanyStorage(_tempDir);
    }

    // ───────────────────────────────────────────────────────────────────── scaffolding

    private sealed class Kit
    {
        public required MainWindowViewModel Vm { get; init; }
        public required string CompanyName { get; init; }
        public required Guid ItemId { get; init; }
        public required Guid GodownId { get; init; }
        public required Guid DozNosUnitId { get; init; }
        public required Guid SalesLedgerId { get; init; }
        public required Guid ExportSalesId { get; init; }
        public required Guid ScrapSalesId { get; init; }
        public required Guid FreightId { get; init; }
        public required Guid RoundOffId { get; init; }
        public required Guid CustomerId { get; init; }
        public required Guid SupplierId { get; init; }
        public required Guid PurchasesLedgerId { get; init; }
        public required Guid ImportPurchasesId { get; init; }
        public required VoucherType SalesType { get; init; }
        public required VoucherType PurchaseType { get; init; }
    }

    /// <summary>
    /// A seeded company with one Widget (Nos, 1 000 on hand), two Sales-Accounts ledgers to split a pre-map
    /// across, a freight and a round-off ledger for the Additional Accounting Entries, and one customer.
    /// <para><paramref name="withGst"/> turns on GST (home Maharashtra 27, the customer in-state and registered,
    /// the Widget taxable at 18%) — the only shape in which the invoice total and the pre-tax item value are
    /// DIFFERENT numbers, which is what the round-off's base has to be measured against.</para>
    /// </summary>
    private Kit NewKit(string companyName, bool withGst = false)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = companyName;
        vm.CreateCompany();
        var c = vm.Company!;

        if (withGst)
        {
            // Back-date the FY so the default entry date (books-begin) lands inside the GST applicability window.
            c.FinancialYearStart = GstFyStart;
            c.BooksBeginFrom = GstFyStart;
            new GstService(c).EnableGst(new GstConfig
            {
                HomeStateCode = "27",
                Gstin = GstinMaharashtra,
                RegistrationType = GstRegistrationType.Regular,
                ApplicableFrom = GstFyStart,
                Periodicity = GstReturnPeriodicity.Monthly,
            });
        }

        var masters = new InventoryService(c);
        var grp = masters.CreateStockGroup("Goods");
        var nos = masters.CreateSimpleUnit("Nos", "Numbers", unitQuantityCode: "NOS");
        var doz = masters.CreateSimpleUnit("Doz", "Dozens", unitQuantityCode: "DOZ");
        // 1 × FIRST (the larger unit) = factor × TAIL (the base): 1 Doz = 12 Nos.
        var dozNos = masters.CreateCompoundUnit("Doz-Nos", "Dozen of 12 Numbers", doz.Id, nos.Id, 12);
        var item = masters.CreateStockItem("Widget", grp.Id, nos.Id);
        if (withGst)
            item.Gst = new StockItemGstDetails
            {
                HsnSac = "847130", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800,
            };
        masters.AddOpeningBalance(item.Id, c.MainLocation!.Id, 1_000m, Money.FromRupees(10m));

        var sales = AddLedger(c, "Domestic Sales", "Sales Accounts");
        var export = AddLedger(c, "Export Sales", "Sales Accounts");
        var scrap = AddLedger(c, "Scrap Sales", "Sales Accounts");
        var freight = AddLedger(c, "Freight Outward", "Indirect Expenses");
        var roundOff = AddLedger(c, "Round Off", "Indirect Expenses");
        var purchases = AddLedger(c, "Domestic Purchases", "Purchase Accounts");
        var importPurchases = AddLedger(c, "Import Purchases", "Purchase Accounts");
        var supplier = AddLedger(c, "Acme Supplies", "Sundry Creditors");
        var customer = AddLedger(c, "Beta Buyers", "Sundry Debtors");
        if (withGst)
            customer.PartyGst = new PartyGstDetails
            {
                RegistrationType = GstRegistrationType.Regular, Gstin = GstinMaharashtra, StateCode = "27",
            };

        var salesType = c.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Sales && t.IsActive);
        var purchaseType = c.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Purchase && t.IsActive);
        _storage.Save(c);

        return new Kit
        {
            Vm = vm,
            CompanyName = companyName,
            ItemId = item.Id,
            GodownId = c.MainLocation!.Id,
            DozNosUnitId = dozNos.Id,
            SalesLedgerId = sales.Id,
            ExportSalesId = export.Id,
            ScrapSalesId = scrap.Id,
            FreightId = freight.Id,
            RoundOffId = roundOff.Id,
            CustomerId = customer.Id,
            SupplierId = supplier.Id,
            PurchasesLedgerId = purchases.Id,
            ImportPurchasesId = importPurchases.Id,
            SalesType = salesType,
            PurchaseType = purchaseType,
        };
    }

    private static DomainLedger AddLedger(Company c, string name, string groupName)
    {
        var group = c.FindGroupByName(groupName) ?? throw new InvalidOperationException($"No group '{groupName}'.");
        var ledger = new DomainLedger(Guid.NewGuid(), name, group.Id, Money.Zero, openingIsDebit: false);
        c.AddLedger(ledger);
        return ledger;
    }

    /// <summary>Defines a class through the SAME service the master screen uses. <paramref name="typeId"/> defaults
    /// to the Sales type; the alteration tests pass the PURCHASE type, because a Sales item invoice is refused for
    /// alteration outright for an unrelated, pre-existing reason (the price-level list rate is not persisted), so
    /// the class guards are only reachable on a purchase.</summary>
    private static VoucherClass DefineClass(Kit k, string name, Guid? typeId = null)
    {
        var svc = new VoucherTypeService(k.Vm.Company!);
        return svc.AddClass(typeId ?? k.SalesType.Id, name, useClassForInterGodownTransfers: false);
    }

    private static void Allocate(Kit k, VoucherClass cls, Guid ledgerId, int percentBasisPoints, Guid? typeId = null) =>
        new VoucherTypeService(k.Vm.Company!)
            .AddClassAllocation(typeId ?? k.SalesType.Id, cls.Id, ledgerId, percentBasisPoints);

    private static void AddEntry(
        Kit k, VoucherClass cls, Guid ledgerId,
        VoucherClassCalculationType calc, Money basis,
        VoucherClassRoundingMethod rounding, Money limit, Guid? typeId = null) =>
        new VoucherTypeService(k.Vm.Company!).AddClassAdditionalEntry(
            typeId ?? k.SalesType.Id, cls.Id, ledgerId, calc, basis, rounding, limit, removeIfZero: true);

    /// <summary>A Direct-Expenses ledger that IS an additional cost of purchase — i.e. one carrying a non-null
    /// <see cref="MethodOfAppropriation"/>. The kit's plain "Freight Outward" deliberately has none (RQ-19).</summary>
    private static DomainLedger AddApportioningLedger(Company c, string name, MethodOfAppropriation method)
    {
        var group = c.FindGroupByName("Direct Expenses")
                    ?? throw new InvalidOperationException("No 'Direct Expenses' group.");
        var ledger = new DomainLedger(Guid.NewGuid(), name, group.Id, Money.Zero, openingIsDebit: true,
            methodOfAppropriation: method);
        c.AddLedger(ledger);
        return ledger;
    }

    /// <summary>The Purchase counterpart of <see cref="OpenInvoice"/> — same shape, supplier party, Purchases
    /// value ledger.</summary>
    private static VoucherEntryViewModel OpenPurchase(Kit k, decimal qty, string rate, string? className)
    {
        k.Vm.OpenVoucher(VoucherBaseType.Purchase);
        var entry = k.Vm.VoucherEntry!;
        k.Vm.ToggleItemInvoice();
        Assert.True(entry.IsItemInvoice);
        Assert.True(entry.IsPurchaseInvoice);

        if (className is not null)
        {
            var option = entry.VoucherClassOptions.SingleOrDefault(o => o.Class?.Name == className);
            Assert.NotNull(option);
            entry.SelectedVoucherClass = option;
        }

        entry.SelectedParty = entry.Parties.Single(p => p.Ledger?.Id == k.SupplierId);

        var line = entry.InventoryLines[0];
        line.SelectedItem = entry.StockItems.Single(i => i.Id == k.ItemId);
        line.SelectedGodown = entry.Godowns.Single(g => g.Id == k.GodownId);
        line.QuantityText = qty.ToString(CultureInfo.InvariantCulture);
        line.RateText = rate;
        return entry;
    }

    private static Voucher PostedPurchase(Kit k) =>
        k.Vm.Company!.Vouchers.Single(v => v.TypeId == k.PurchaseType.Id);

    /// <summary>Opens a Sales item invoice for one item line, selecting <paramref name="className"/> when given.</summary>
    private static VoucherEntryViewModel OpenInvoice(
        Kit k, decimal qty, string rate, string? className)
    {
        k.Vm.OpenVoucher(VoucherBaseType.Sales);
        var entry = k.Vm.VoucherEntry!;
        k.Vm.ToggleItemInvoice();
        Assert.True(entry.IsItemInvoice);

        if (className is not null)
        {
            var option = entry.VoucherClassOptions.SingleOrDefault(o => o.Class?.Name == className);
            Assert.NotNull(option);
            entry.SelectedVoucherClass = option;
        }

        entry.SelectedParty = entry.Parties.Single(p => p.Ledger?.Id == k.CustomerId);

        var line = entry.InventoryLines[0];
        line.SelectedItem = entry.StockItems.Single(i => i.Id == k.ItemId);
        line.SelectedGodown = entry.Godowns.Single(g => g.Id == k.GodownId);
        line.QuantityText = qty.ToString(CultureInfo.InvariantCulture);
        line.RateText = rate;
        return entry;
    }

    private static Voucher PostedSale(Kit k) =>
        k.Vm.Company!.Vouchers.Single(v => v.TypeId == k.SalesType.Id);

    private static decimal Leg(Voucher v, Guid ledgerId, DrCr side) =>
        v.Lines.Where(l => l.LedgerId == ledgerId && l.Side == side).Sum(l => l.Amount.Amount);

    // ───────────────────────────────────────────────────────────────────── (1) the pre-map posts

    /// <summary>
    /// 🔴 <b>THE LEDGER PRE-MAP SPLITS THE VALUE LEG ON A REAL POSTED VOUCHER.</b> On today's main this is red at
    /// the very first line — <c>VoucherClassOptions</c> does not exist, because nothing in <c>src/</c> ever
    /// consumed a class. With the wiring it proves the operator's own 60/40 reaches <c>Voucher.Lines</c>.
    ///
    /// <para>🔴 <b>THE FIGURES ARE A THREE-WAY 33.33 / 33.33 / 33.34 SPLIT OF ₹1 000.01, AND THAT IS THE WHOLE
    /// POINT.</b> A first draft of this test used 60/40, where an independently-rounded last share and the
    /// remainder happen to be the same number — and a mutant that rounded the last share instead of taking the
    /// remainder passed it. Here they differ: rounded independently the shares are
    /// 333.30 + 333.30 + 333.40 = ₹1 000.00, a paisa short of the customer's ₹1 000.01, and the voucher does not
    /// balance at all. Taking the remainder gives 333.30 + 333.30 + <b>333.41</b>.</para>
    /// </summary>
    [Fact]
    public void A_class_ledger_pre_map_splits_the_value_leg_across_the_operators_ledgers()
    {
        var k = NewKit("Class Pre Map Co");
        var cls = DefineClass(k, "Retail");
        Allocate(k, cls, k.SalesLedgerId, 3_333);   // 33.33%
        Allocate(k, cls, k.ExportSalesId, 3_333);   // 33.33%
        Allocate(k, cls, k.ScrapSalesId, 3_334);    // 33.34% — and it takes the remainder, not its own rounding

        var entry = OpenInvoice(k, qty: 1m, rate: "1000.01", className: "Retail");

        // The vendor hides the Sales-ledger field once a class supplies those ledgers.
        Assert.True(entry.ClassSuppliesValueLedgers);
        Assert.False(entry.ShowStockLedgerPicker);
        Assert.True(entry.ShowVoucherClassSelector);

        Assert.True(entry.Accept(), entry.Message);

        var v = PostedSale(k);
        Assert.Equal(333.30m, Leg(v, k.SalesLedgerId, DrCr.Credit));
        Assert.Equal(333.30m, Leg(v, k.ExportSalesId, DrCr.Credit));
        Assert.Equal(333.41m, Leg(v, k.ScrapSalesId, DrCr.Credit));
        Assert.Equal(1_000.01m, Leg(v, k.CustomerId, DrCr.Debit));

        // …and the voucher balances to the paisa, which is the whole reason the last share is a remainder.
        Assert.Equal(v.TotalDebit.Amount, v.TotalCredit.Amount);
        Assert.Equal(1_000.01m, v.TotalDebit.Amount);
    }

    // ───────────────────────────────────────────────────────────────────── (2) the additional-ledger rule posts

    /// <summary>
    /// 🔴 <b>AN ADDITIONAL-LEDGER RULE POSTS ITS OWN LEG AND THE PARTY MOVES WITH IT.</b> Freight at ₹12.50 per
    /// base unit over 4 units is ₹50.00 — on a SALE it is a credit (it is charged to the customer), and the
    /// customer's debit must rise by exactly that or the voucher is out of balance.
    /// </summary>
    [Fact]
    public void An_additional_accounting_entry_posts_its_leg_and_raises_the_party_total()
    {
        var k = NewKit("Class Freight Co");
        var cls = DefineClass(k, "Retail");
        Allocate(k, cls, k.SalesLedgerId, 10_000);
        AddEntry(k, cls, k.FreightId,
            VoucherClassCalculationType.BasedOnQuantity, Money.FromRupees(12.50m),
            VoucherClassRoundingMethod.NotApplicable, Money.Zero);

        var entry = OpenInvoice(k, qty: 4m, rate: "100.00", className: "Retail");
        Assert.True(entry.Accept(), entry.Message);

        var v = PostedSale(k);
        Assert.Equal(400.00m, Leg(v, k.SalesLedgerId, DrCr.Credit));
        Assert.Equal(50.00m, Leg(v, k.FreightId, DrCr.Credit));
        Assert.Equal(450.00m, Leg(v, k.CustomerId, DrCr.Debit));
        Assert.Equal(v.TotalDebit.Amount, v.TotalCredit.Amount);
    }

    /// <summary>
    /// 🔴 <b>A PER-UNIT VALUE BASIS IS PER BASE UNIT, SO A LINE KEYED IN DOZENS MUST BE CONVERTED FIRST.</b> The
    /// vendor's Value Basis on a Based-on-Quantity entry is a rate per unit; this product lets an operator key a
    /// line in an alternate unit (2 Doz of a Nos-based item). Charging ₹12.50 against the TYPED 2 would post ₹25
    /// of freight on 24 pieces — a twelfth of the right figure, silently, on every such invoice. The base quantity
    /// is 24, so the freight is ₹300.00.
    /// </summary>
    [Fact]
    public void A_per_unit_value_basis_is_charged_on_the_base_quantity_not_the_typed_one()
    {
        var k = NewKit("Class Alt Unit Co");
        var cls = DefineClass(k, "Retail");
        Allocate(k, cls, k.SalesLedgerId, 10_000);
        AddEntry(k, cls, k.FreightId,
            VoucherClassCalculationType.BasedOnQuantity, Money.FromRupees(12.50m),
            VoucherClassRoundingMethod.NotApplicable, Money.Zero);

        var entry = OpenInvoice(k, qty: 2m, rate: "600.00", className: "Retail");
        var line = entry.InventoryLines[0];
        line.SelectedUnit = line.UnitOptions.Single(u => u.Id == k.DozNosUnitId);

        Assert.True(entry.Accept(), entry.Message);

        var v = PostedSale(k);
        Assert.Equal(1_200.00m, Leg(v, k.SalesLedgerId, DrCr.Credit));  // 2 × ₹600, valued as typed
        Assert.Equal(300.00m, Leg(v, k.FreightId, DrCr.Credit));        // 24 base units × ₹12.50
        Assert.Equal(1_500.00m, Leg(v, k.CustomerId, DrCr.Debit));
        Assert.Equal(v.TotalDebit.Amount, v.TotalCredit.Amount);
    }

    // ───────────────────────────────────────────────────────────────────── (3) rounding — the highest-risk part

    /// <summary>
    /// 🔴 <b>THE ROUND-OFF MAKES THE INVOICE ROUND, AND A DOWNWARD ROUNDING IS A NEGATIVE LEG THAT FLIPS SIDE.</b>
    /// This is the vendor's own worked example transplanted onto a posted voucher: an invoice of ₹1 000.60,
    /// downward rounding at a limit of 1, becomes ₹1 000.00 with (-)0.60 on the round-off ledger. The natural side
    /// of a sale's value leg is CREDIT, so a negative round-off posts as a DEBIT of 0.60 — a leg that was dropped
    /// for being negative, or kept positive, would move the invoice by 0.60 or by 1.20 respectively.
    /// </summary>
    [Fact]
    public void A_downward_round_off_posts_a_negative_leg_and_the_invoice_total_comes_out_round()
    {
        var k = NewKit("Class Round Down Co");
        var cls = DefineClass(k, "Retail");
        Allocate(k, cls, k.SalesLedgerId, 10_000);
        AddEntry(k, cls, k.RoundOffId,
            VoucherClassCalculationType.AsTotalAmountRounding, Money.Zero,
            VoucherClassRoundingMethod.Downward, Money.FromRupees(1m));

        var entry = OpenInvoice(k, qty: 1m, rate: "1000.60", className: "Retail");
        Assert.True(entry.Accept(), entry.Message);

        var v = PostedSale(k);
        Assert.Equal(1_000.60m, Leg(v, k.SalesLedgerId, DrCr.Credit));
        Assert.Equal(0.60m, Leg(v, k.RoundOffId, DrCr.Debit));     // (-)0.60 against a credit natural side
        Assert.Equal(0m, Leg(v, k.RoundOffId, DrCr.Credit));
        Assert.Equal(1_000.00m, Leg(v, k.CustomerId, DrCr.Debit)); // the figure the customer is asked to pay
        Assert.Equal(v.TotalDebit.Amount, v.TotalCredit.Amount);
    }

    /// <summary>Upward rounding is the mirror: ₹1 000.30 at a limit of 1 becomes ₹1 001.00 and the round-off ledger
    /// takes a 0.70 CREDIT, matching the vendor page's second worked example.</summary>
    [Fact]
    public void An_upward_round_off_raises_the_invoice_to_the_next_whole_rupee()
    {
        var k = NewKit("Class Round Up Co");
        var cls = DefineClass(k, "Retail");
        Allocate(k, cls, k.SalesLedgerId, 10_000);
        AddEntry(k, cls, k.RoundOffId,
            VoucherClassCalculationType.AsTotalAmountRounding, Money.Zero,
            VoucherClassRoundingMethod.Upward, Money.FromRupees(1m));

        var entry = OpenInvoice(k, qty: 1m, rate: "1000.30", className: "Retail");
        Assert.True(entry.Accept(), entry.Message);

        var v = PostedSale(k);
        Assert.Equal(0.70m, Leg(v, k.RoundOffId, DrCr.Credit));
        Assert.Equal(1_001.00m, Leg(v, k.CustomerId, DrCr.Debit));
        Assert.Equal(v.TotalDebit.Amount, v.TotalCredit.Amount);
    }

    /// <summary>
    /// 🔴 <b>THE ROUND-OFF IS MEASURED AGAINST THE WHOLE INVOICE, FREIGHT INCLUDED.</b> This is the ordering rule
    /// that makes the round-off leg mean anything: 1 × ₹1 000.00 plus freight of ₹0.55 is ₹1 000.55, and normal
    /// rounding at a limit of 1 must take the invoice to ₹1 001.00 — a round-off computed on the ITEM VALUE alone
    /// would see an already-round ₹1 000.00, post nothing, and leave the customer billed ₹1 000.55.
    /// </summary>
    [Fact]
    public void The_round_off_is_computed_after_every_other_additional_entry_not_on_the_item_value()
    {
        var k = NewKit("Class Round After Freight Co");
        var cls = DefineClass(k, "Retail");
        Allocate(k, cls, k.SalesLedgerId, 10_000);
        AddEntry(k, cls, k.FreightId,
            VoucherClassCalculationType.BasedOnQuantity, Money.FromRupees(0.55m),
            VoucherClassRoundingMethod.NotApplicable, Money.Zero);
        AddEntry(k, cls, k.RoundOffId,
            VoucherClassCalculationType.AsTotalAmountRounding, Money.Zero,
            VoucherClassRoundingMethod.Normal, Money.FromRupees(1m));

        var entry = OpenInvoice(k, qty: 1m, rate: "1000.00", className: "Retail");
        Assert.True(entry.Accept(), entry.Message);

        var v = PostedSale(k);
        Assert.Equal(0.55m, Leg(v, k.FreightId, DrCr.Credit));
        Assert.Equal(0.45m, Leg(v, k.RoundOffId, DrCr.Credit));
        Assert.Equal(1_001.00m, Leg(v, k.CustomerId, DrCr.Debit));
        Assert.Equal(v.TotalDebit.Amount, v.TotalCredit.Amount);
    }

    // ───────────────────────────────────────────────────────────────────── (3b) rounding on a GST invoice

    /// <summary>
    /// 🔴 <b>THE ROUND-OFF ROUNDS THE INVOICE, TAX INCLUDED — AND NOTHING ELSE IN THIS FILE PROVES IT.</b>
    ///
    /// <para>Every other rounding test here runs on a GST-free company, where the item value IS the invoice total,
    /// so a round-off computed on the pre-tax subtotal and one computed on the invoice are the same number. This
    /// test separates them, and it was written because the separation was MEASURED: deleting the invoice-total term
    /// from <c>VoucherClassPosting.Compute</c> left all nine of the other tests green.</para>
    ///
    /// <para>1 × ₹1 002.00 at 18% intra-state is CGST ₹90.18 + SGST ₹90.18, an invoice of ₹1 182.36. Downward
    /// rounding at a limit of 1 must take it to ₹1 182.00 with (-)0.36 on the round-off ledger. Round the PRE-TAX
    /// ₹1 002.00 instead and it is already a whole rupee: no round-off leg is posted at all and the customer is
    /// billed ₹1 182.36 by a class whose entire purpose was to stop that.</para>
    /// </summary>
    [Fact]
    public void On_a_gst_invoice_the_round_off_is_measured_against_the_tax_inclusive_total()
    {
        var k = NewKit("Class Gst Round Co", withGst: true);
        var cls = DefineClass(k, "Retail");
        Allocate(k, cls, k.SalesLedgerId, 10_000);
        AddEntry(k, cls, k.RoundOffId,
            VoucherClassCalculationType.AsTotalAmountRounding, Money.Zero,
            VoucherClassRoundingMethod.Downward, Money.FromRupees(1m));

        var entry = OpenInvoice(k, qty: 1m, rate: "1002.00", className: "Retail");
        Assert.True(entry.IsGstInvoice);
        Assert.True(entry.Accept(), entry.Message);

        var v = PostedSale(k);
        Assert.Equal(1_002.00m, Leg(v, k.SalesLedgerId, DrCr.Credit));

        // CGST 90.18 + SGST 90.18 — the tax the invoice actually carries, posted by the GST engine.
        var tax = v.Lines.Where(l => l.Side == DrCr.Credit && l.Gst is not null).Sum(l => l.Amount.Amount);
        Assert.Equal(180.36m, tax);

        // …and the class's round-off closes the gap to a whole rupee.
        Assert.Equal(0.36m, Leg(v, k.RoundOffId, DrCr.Debit));
        Assert.Equal(1_182.00m, Leg(v, k.CustomerId, DrCr.Debit));
        Assert.Equal(v.TotalDebit.Amount, v.TotalCredit.Amount);
    }

    // ───────────────────────────────────────────────────────────────────── (3c) the band matches the post

    /// <summary>
    /// 🔴 <b>THE FIGURE ON SCREEN IS THE FIGURE THAT POSTS.</b> Found by inspection, not by a failing test: the
    /// live totals band summed items + additional cost + tax + TCS and knew nothing of the class, so an invoice
    /// under a round-off class showed the operator ₹1 000.60 and then posted ₹1 000.00. Worse, the Bill-wise panel
    /// foots against that same band — so a bill-wise party under a class would have been REFUSED at Accept for a
    /// mismatch nothing on screen accounted for.
    /// </summary>
    [Fact]
    public void The_live_party_total_already_carries_the_classes_round_off()
    {
        var k = NewKit("Class Band Co");
        var cls = DefineClass(k, "Retail");
        Allocate(k, cls, k.SalesLedgerId, 10_000);
        AddEntry(k, cls, k.RoundOffId,
            VoucherClassCalculationType.AsTotalAmountRounding, Money.Zero,
            VoucherClassRoundingMethod.Downward, Money.FromRupees(1m));

        var entry = OpenInvoice(k, qty: 1m, rate: "1000.60", className: "Retail");

        Assert.Equal("1,000.60", entry.ItemsTotalText);   // the items are what they are…
        Assert.Equal("1,000.00", entry.PartyTotalText);   // …and the party owes the ROUNDED figure

        Assert.True(entry.Accept(), entry.Message);
        Assert.Equal(1_000.00m, Leg(PostedSale(k), k.CustomerId, DrCr.Debit));
    }

    // ───────────────────────────────────────────────────────────────────── (4) the class is not imposed

    /// <summary>
    /// An invoice raised on the SAME type with the class declined posts exactly what it always did — one value leg
    /// on the Sales ledger the operator picked, no freight, no round-off. A class that applied itself because it
    /// merely EXISTS would silently change every historical workflow on that voucher type.
    /// </summary>
    [Fact]
    public void Declining_the_class_posts_the_ordinary_two_leg_invoice()
    {
        var k = NewKit("Class Declined Co");
        var cls = DefineClass(k, "Retail");
        Allocate(k, cls, k.ExportSalesId, 10_000);
        AddEntry(k, cls, k.FreightId,
            VoucherClassCalculationType.BasedOnQuantity, Money.FromRupees(99m),
            VoucherClassRoundingMethod.NotApplicable, Money.Zero);

        var entry = OpenInvoice(k, qty: 2m, rate: "250.00", className: null);

        // The picker is offered, and its resting choice is the decline.
        Assert.True(entry.ShowVoucherClassSelector);
        Assert.Null(entry.ActiveVoucherClass);
        Assert.True(entry.ShowStockLedgerPicker);
        entry.SelectedStockLedger = entry.StockLedgers.Single(l => l.Id == k.SalesLedgerId);

        Assert.True(entry.Accept(), entry.Message);

        var v = PostedSale(k);
        Assert.Equal(2, v.Lines.Count);
        Assert.Equal(500m, Leg(v, k.SalesLedgerId, DrCr.Credit));
        Assert.Equal(500m, Leg(v, k.CustomerId, DrCr.Debit));
        Assert.Equal(0m, Leg(v, k.FreightId, DrCr.Credit));
        Assert.Equal(0m, Leg(v, k.ExportSalesId, DrCr.Credit));
    }

    /// <summary>A type that defines NO class never shows the picker at all, so the invoice header is unchanged for
    /// every company that has never heard of a voucher class.</summary>
    [Fact]
    public void A_type_with_no_class_offers_no_picker_and_keeps_its_value_ledger_field()
    {
        var k = NewKit("No Class Co");
        var entry = OpenInvoice(k, qty: 1m, rate: "100.00", className: null);

        Assert.False(entry.ShowVoucherClassSelector);
        Assert.True(entry.ShowStockLedgerPicker);
        Assert.Equal(string.Empty, entry.VoucherClassSummary);
    }

    /// <summary>
    /// A class that only pre-maps ledgers and adds none is still a class — and an empty shell that does NEITHER is
    /// not offered at all, because it would post nothing while looking like it does something.
    /// </summary>
    [Fact]
    public void An_empty_class_is_not_offered_at_entry()
    {
        var k = NewKit("Empty Class Co");
        DefineClass(k, "Shell");

        var entry = OpenInvoice(k, qty: 1m, rate: "100.00", className: null);
        Assert.False(entry.ShowVoucherClassSelector);
        Assert.DoesNotContain(entry.VoucherClassOptions, o => o.Class?.Name == "Shell");
    }

    // ───────────────────────────────────────────────────────────────────── (5) persistence + the mode gate

    /// <summary>The split value leg survives a save/reload — the class's legs are ordinary posted lines, not a
    /// display-time derivation that would vanish from the reopened book.</summary>
    [Fact]
    public void The_classes_legs_survive_a_reload()
    {
        var k = NewKit("Class Reload Co");
        var cls = DefineClass(k, "Retail");
        Allocate(k, cls, k.SalesLedgerId, 7_500);
        Allocate(k, cls, k.ExportSalesId, 2_500);

        var entry = OpenInvoice(k, qty: 1m, rate: "400.00", className: "Retail");
        Assert.True(entry.Accept(), entry.Message);

        var reloaded = _storage.Load(_storage.ListCompanies().Single(e => e.Name == k.CompanyName));
        var type = reloaded.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Sales && t.IsActive);
        var v = reloaded.Vouchers.Single(x => x.TypeId == type.Id);

        Assert.Equal(300.00m, Leg(v, k.SalesLedgerId, DrCr.Credit));
        Assert.Equal(100.00m, Leg(v, k.ExportSalesId, DrCr.Credit));
        Assert.Equal(v.TotalDebit.Amount, v.TotalCredit.Amount);
    }

    /// <summary>
    /// 🔴 <b>LEAVING ITEM-INVOICE MODE MUST PUT THE VALUE-LEDGER FIELD BACK.</b> The class hides that field; a
    /// Ctrl+H out of the mode leaves a plain Dr/Cr grid that no class governs, and a field still hidden there
    /// would be unreachable with no way to name the value leg.
    /// </summary>
    [Fact]
    public void Leaving_item_invoice_mode_stands_the_class_down_and_restores_the_value_ledger_field()
    {
        var k = NewKit("Class Mode Flip Co");
        var cls = DefineClass(k, "Retail");
        Allocate(k, cls, k.SalesLedgerId, 10_000);

        var entry = OpenInvoice(k, qty: 1m, rate: "100.00", className: "Retail");
        Assert.False(entry.ShowStockLedgerPicker);

        k.Vm.ToggleItemInvoice();   // back out of item-invoice mode
        Assert.False(entry.IsItemInvoice);
        Assert.Null(entry.ActiveVoucherClass);
        Assert.False(entry.ClassSuppliesValueLedgers);
        Assert.False(entry.ShowVoucherClassSelector);
    }

    // ───────────────────────────────────────────────────────────────────── (6) alteration is refused, not silent

    /// <summary>
    /// 🔴 <b>A CLASS-POSTED INVOICE CANNOT BE ALTERED ON THIS SCREEN, AND IT IS REFUSED BY NAME.</b> This screen
    /// re-derives exactly ONE value leg, so re-accepting a class invoice would collapse the split — the precise
    /// class of silent loss <c>TryCarryDerivedLegChildren</c> exists to stop. The refusal must also SAY "voucher
    /// class": the sentence used to read "this shape can only have arrived from an import", which was true when it
    /// was written and is now false, and which sends the operator nowhere.
    ///
    /// <para><b>Why this is a PURCHASE.</b> A Sales item invoice is refused for alteration outright, for an
    /// unrelated pre-existing reason (the price-level list rate behind the posted effective rate is not
    /// persisted), so the class refusal is unreachable there and a Sales version of this test would pass on a
    /// message that has nothing to do with classes. A purchase item invoice opens today, which is what makes this
    /// guard real.</para>
    /// </summary>
    [Fact]
    public void Altering_a_class_posted_invoice_is_refused_by_name_rather_than_collapsing_the_split()
    {
        var k = NewKit("Class Alter Refusal Co");
        var cls = DefineClass(k, "Import Mix", k.PurchaseType.Id);
        Allocate(k, cls, k.PurchasesLedgerId, 6_000, k.PurchaseType.Id);
        Allocate(k, cls, k.ImportPurchasesId, 4_000, k.PurchaseType.Id);

        var entry = OpenPurchase(k, qty: 1m, rate: "1000.00", className: "Import Mix");
        Assert.True(entry.Accept(), entry.Message);

        var posted = PostedPurchase(k);
        Assert.Equal(600m, Leg(posted, k.PurchasesLedgerId, DrCr.Debit));
        Assert.Equal(400m, Leg(posted, k.ImportPurchasesId, DrCr.Debit));

        var open = VoucherEntryViewModel.ForAlter(
            k.Vm.Company!, posted.Id, _storage, onSaved: () => { }, onCancelled: () => { });

        Assert.True(open.IsRefused, "A class-posted invoice opened for alteration — it would collapse the split.");
        Assert.Contains("voucher class", open.Refusal!, StringComparison.OrdinalIgnoreCase);

        // …and the posted voucher is untouched by the attempt.
        var after = PostedPurchase(k);
        Assert.Equal(600m, Leg(after, k.PurchasesLedgerId, DrCr.Debit));
        Assert.Equal(400m, Leg(after, k.ImportPurchasesId, DrCr.Debit));
    }

    /// <summary>The other direction of the same rule: a class cannot be APPLIED to a posted invoice being altered,
    /// and that refusal names itself too. Without it the class would split a value leg whose cost-centre, forex and
    /// bill-wise children this screen can only carry onto ONE leg.</summary>
    [Fact]
    public void A_class_cannot_be_applied_while_altering_a_posted_invoice()
    {
        var k = NewKit("Class Alter Apply Co");
        var cls = DefineClass(k, "Import Mix", k.PurchaseType.Id);
        Allocate(k, cls, k.PurchasesLedgerId, 10_000, k.PurchaseType.Id);

        // Post an ORDINARY purchase first (no class), so it is alterable.
        var entry = OpenPurchase(k, qty: 1m, rate: "500.00", className: null);
        entry.SelectedStockLedger = entry.StockLedgers.Single(l => l.Id == k.PurchasesLedgerId);
        Assert.True(entry.Accept(), entry.Message);
        var posted = PostedPurchase(k);

        var open = VoucherEntryViewModel.ForAlter(
            k.Vm.Company!, posted.Id, _storage, onSaved: () => { }, onCancelled: () => { });
        Assert.False(open.IsRefused, open.Refusal);

        var altering = open.Entry!;
        altering.SelectedVoucherClass =
            altering.VoucherClassOptions.Single(o => o.Class?.Id == cls.Id);

        // 🔴 ACCEPT-ALTERATION IS THE VERB, AND USING THE WRONG ONE HID THIS GUARD COMPLETELY. The first draft of
        // this test called Accept(), which refuses EVERY altering screen at its first line ("accepting it as a new
        // entry would post a second voucher") before item-invoice code runs at all. It therefore went green on a
        // refusal that had nothing to do with voucher classes, and would have stayed green with the class guard
        // deleted. AcceptAlteration routes _alteringPostedAsItemInvoice through AcceptItemInvoiceAlteration into
        // BuildItemInvoice, which is where the guard lives and the only path that reaches it.
        Assert.False(altering.AcceptAlteration());
        Assert.Contains("voucher class cannot be applied", altering.Message!, StringComparison.OrdinalIgnoreCase);

        // The posted voucher is unchanged — still the single ordinary value leg.
        Assert.Equal(500m, Leg(PostedPurchase(k), k.PurchasesLedgerId, DrCr.Debit));
    }

    /// <summary>
    /// 🔴 <b>A CLASS'S ADDITIONAL LEDGER LOADS ONTO STOCK VALUATION — THE ONE REUSE CLAIM NOTHING TESTED.</b>
    ///
    /// <para><c>VoucherClassPosting</c>'s remarks assert that appropriation "is not decided here": the class computes
    /// an additional ledger's AMOUNT and stops, and whether that amount then loads onto the item lines' stock rate is
    /// the ledger master's <see cref="Ledger.MethodOfAppropriation"/>, "read by the existing
    /// <c>AdditionalCostApportionment</c> exactly as it is for an operator-keyed line". That is a claim about a
    /// WRONG-MONEY surface — freight that loads changes closing stock and therefore the Balance Sheet — and NO test
    /// anywhere exercised it. It is true only because <see cref="AdditionalCostApportionment.TrackedCostLegs"/>
    /// sweeps the POSTED <c>Voucher.Lines</c> by ledger rather than reading any screen-side collection, so a
    /// class-posted leg is indistinguishable from a hand-keyed one by the time valuation runs. Untested, that is a
    /// coincidence of two engines; tested, it is the documented contract.</para>
    ///
    /// <para><b>THE FIGURES.</b> 100 Nos @ ₹10.00 = ₹1 000.00 of goods, and the class adds ₹2.00/unit of freight on
    /// a By-Value ledger = ₹200.00. The landed unit rate must therefore be ₹12.00, not the ₹10.00 the operator
    /// keyed — and the freight must be a genuine Dr leg on the posted voucher for the sweep to see it at all.</para>
    ///
    /// <para><b>The two-method question the row owes an answer to.</b> The LEDGER's method is what decides, and the
    /// Account Group's "method to allocate when used in purchase invoice" reaches this path only through the ledger
    /// that inherits it — there is deliberately no class-level apportionment field, so a class can never contradict
    /// either. The assertion below pins that: the class names the ledger and the amount, the LEDGER names the method.</para>
    /// </summary>
    [Fact]
    public void A_class_additional_ledger_loads_onto_stock_valuation_through_the_existing_apportionment()
    {
        var k = NewKit("Class Apportionment Co");
        var company = k.Vm.Company!;

        // A By-Value additional-cost ledger — the method lives on the LEDGER, never on the class.
        var freightByValue = AddApportioningLedger(company, "Freight Inward (by Value)", MethodOfAppropriation.ByValue);

        var cls = DefineClass(k, "Landed Import", k.PurchaseType.Id);
        Allocate(k, cls, k.PurchasesLedgerId, 10_000, k.PurchaseType.Id);
        AddEntry(k, cls, freightByValue.Id, VoucherClassCalculationType.BasedOnQuantity,
            Money.FromRupees(2m), VoucherClassRoundingMethod.NotApplicable, Money.Zero, k.PurchaseType.Id);

        var entry = OpenPurchase(k, qty: 100m, rate: "10.00", className: "Landed Import");
        entry.TrackAdditionalCosts = true;
        Assert.True(entry.Accept(), entry.Message);

        var posted = PostedPurchase(k);

        // 1 — the class's freight really posted as a Dr leg (without this the sweep has nothing to find).
        Assert.Equal(200m, Leg(posted, freightByValue.Id, DrCr.Debit));
        Assert.Equal(1_000m, Leg(posted, k.PurchasesLedgerId, DrCr.Debit));
        Assert.Equal(1_200m, Leg(posted, k.SupplierId, DrCr.Credit));

        // 2 — …and the EXISTING apportionment engine, which never heard of voucher classes, picks that leg up
        //     purely from the ledger's method and loads it onto the item.
        var tracked = AdditionalCostApportionment.TrackedCostLegs(company, posted);
        var freightLeg = Assert.Single(tracked, t => t.Ledger.Id == freightByValue.Id);
        Assert.Equal(MethodOfAppropriation.ByValue, freightLeg.Method);
        Assert.Equal(200m, freightLeg.Amount.Amount);

        var landed = AdditionalCostApportionment.ForPurchase(company, posted);
        var line = Assert.Single(landed);
        Assert.True(line.HasLoad, "The class's freight did not load onto the item at all.");
        Assert.Equal(12m, line.LandedUnitRate);
    }

    /// <summary>
    /// The inverse, and the fidelity trap it protects (RQ-19): the SAME class, the SAME ₹200 of freight, on a
    /// Direct-Expenses ledger carrying NO method. It must still post as a real ledger leg — the supplier is owed the
    /// money either way — and must NOT touch the item's valuation. If a class-posted leg were swept into the pool by
    /// virtue of coming from a class rather than by its ledger's method, this is the test that goes red.
    /// </summary>
    [Fact]
    public void A_class_additional_ledger_with_no_method_posts_but_does_not_load_the_stock()
    {
        var k = NewKit("Class No Method Co");
        var company = k.Vm.Company!;

        var plainFreight = AddLedger(company, "Freight Inward (plain)", "Direct Expenses");
        Assert.Null(plainFreight.MethodOfAppropriation);

        var cls = DefineClass(k, "Plain Freight", k.PurchaseType.Id);
        Allocate(k, cls, k.PurchasesLedgerId, 10_000, k.PurchaseType.Id);
        AddEntry(k, cls, plainFreight.Id, VoucherClassCalculationType.BasedOnQuantity,
            Money.FromRupees(2m), VoucherClassRoundingMethod.NotApplicable, Money.Zero, k.PurchaseType.Id);

        var entry = OpenPurchase(k, qty: 100m, rate: "10.00", className: "Plain Freight");
        entry.TrackAdditionalCosts = true;
        Assert.True(entry.Accept(), entry.Message);

        var posted = PostedPurchase(k);

        // The money still moves — the freight is owed and the party total carries it.
        Assert.Equal(200m, Leg(posted, plainFreight.Id, DrCr.Debit));
        Assert.Equal(1_200m, Leg(posted, k.SupplierId, DrCr.Credit));

        // …but the stock is valued at what was paid for the GOODS, exactly as RQ-19 requires.
        Assert.Empty(AdditionalCostApportionment.TrackedCostLegs(company, posted));
        var landed = AdditionalCostApportionment.ForPurchase(company, posted);
        Assert.All(landed, l => Assert.False(l.HasLoad, "A method-less freight ledger loaded the stock."));
    }

    /// <summary>
    /// 🔴 <b>THE GST ANCHOR IS A TAX-RATE DECISION, NOT A COSMETIC ONE — AND ONLY ITS EASY ARM WAS TESTED.</b>
    ///
    /// <para>The shipped rate hierarchy is the vendor's <c>Ledger → Accounting Group → Stock Item → Stock Group →
    /// Company</c> walk, which every company created on schema v51 or later carries. The LEDGER rung therefore
    /// OUTRANKS the stock item: whichever ledger is handed to <c>ResolveRate</c> as the anchor decides the rate even
    /// when the item declares one of its own. <c>GstAnchorLedger</c> changes that anchor, so it moves tax.</para>
    ///
    /// <para>The rule it implements: ONE pre-mapped ledger is still an unambiguous anchor and keeps the old
    /// behaviour; SEVERAL deliberately have NO anchor, because the invoice value is being split across ledgers that
    /// may disagree and no one of them can be the right answer. Dropping the anchor lets the item speak. Picking the
    /// first row (or the largest) would be silent, arbitrary, and OURS rather than the vendor's.</para>
    ///
    /// <para><b>THE FIGURES SEPARATE THE ARMS.</b> The value ledger says 5%, the Widget says 18%. Under a
    /// single-ledger class the invoice must bear the LEDGER's 5% (₹50.00 on ₹1 000); under a two-ledger class the
    /// anchor is gone and the ITEM's 18% must apply (₹180.00). Identical masters, identical keystrokes, and the only
    /// difference is how many ledgers the class names.</para>
    /// </summary>
    [Fact]
    public void A_multi_ledger_class_drops_the_gst_anchor_so_the_item_rate_applies()
    {
        // ── Arm 1: ONE pre-mapped ledger — the anchor survives and the LEDGER's 5% wins.
        var single = NewKit("Class Anchor One Co", withGst: true);
        SetLedgerGst(single, single.SalesLedgerId, rateBasisPoints: 500);
        var clsOne = DefineClass(single, "Single");
        Allocate(single, clsOne, single.SalesLedgerId, 10_000);

        var e1 = OpenInvoice(single, qty: 1m, rate: "1000.00", className: "Single");
        Assert.True(e1.Accept(), e1.Message);
        var v1 = PostedSale(single);

        var tax1 = v1.Lines.Where(l => l.Side == DrCr.Credit && l.Gst is not null).Sum(l => l.Amount.Amount);
        Assert.Equal(50.00m, tax1);                                       // 5% — the anchor ledger's own rate
        Assert.Equal(1_050.00m, Leg(v1, single.CustomerId, DrCr.Debit));

        // ── Arm 2: the SAME masters, but the class names TWO ledgers. No ledger can be the anchor, so the rate
        //    falls through to the Widget's own 18%.
        var multi = NewKit("Class Anchor Many Co", withGst: true);
        SetLedgerGst(multi, multi.SalesLedgerId, rateBasisPoints: 500);
        SetLedgerGst(multi, multi.ExportSalesId, rateBasisPoints: 500);
        var clsMany = DefineClass(multi, "Split");
        Allocate(multi, clsMany, multi.SalesLedgerId, 6_000);
        Allocate(multi, clsMany, multi.ExportSalesId, 4_000);

        var e2 = OpenInvoice(multi, qty: 1m, rate: "1000.00", className: "Split");
        Assert.True(e2.Accept(), e2.Message);
        var v2 = PostedSale(multi);

        // The split itself posted…
        Assert.Equal(600m, Leg(v2, multi.SalesLedgerId, DrCr.Credit));
        Assert.Equal(400m, Leg(v2, multi.ExportSalesId, DrCr.Credit));

        // …and the tax is the ITEM's 18%, not either ledger's 5%.
        var tax2 = v2.Lines.Where(l => l.Side == DrCr.Credit && l.Gst is not null).Sum(l => l.Amount.Amount);
        Assert.Equal(180.00m, tax2);
        Assert.Equal(1_180.00m, Leg(v2, multi.CustomerId, DrCr.Debit));
        Assert.Equal(v2.TotalDebit.Amount, v2.TotalCredit.Amount);
    }

    /// <summary>Puts a GST block on a Sales/Purchase ledger — the TOP rung of the shipped rate hierarchy.</summary>
    private static void SetLedgerGst(Kit k, Guid ledgerId, int rateBasisPoints) =>
        k.Vm.Company!.FindLedger(ledgerId)!.SalesPurchaseGst = new StockItemGstDetails
        {
            HsnSac = "9973", Taxability = GstTaxability.Taxable, RateBasisPoints = rateBasisPoints,
        };

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); } catch { /* temp */ }
    }
}
