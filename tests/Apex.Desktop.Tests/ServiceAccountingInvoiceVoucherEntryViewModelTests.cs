using System;
using System.Collections.Generic;
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
/// End-to-end coverage for the <b>Accounting Invoice (service) mode for Sales</b> — a service billed as a
/// service-income LEDGER line (Particulars) with auto SAC-based GST and NO stock. Drives the real shell + entry VM
/// over a throwaway <c>.db</c> (no UI toolkit) and asserts the <b>real posted GST amounts + place of supply</b>, never
/// "a field exists": intra CGST/SGST split, inter IGST, exempt (value, zero tax), fail-fast on a taxable ledger with no
/// SAC, the BLOCKER-1 CanAccept survival through the REAL Recalculate(), the GSTR-1 HSN/SAC summary (service SAC feeds
/// it; a plain taxable sale is NOT mis-filed exempt; an item invoice is not double-counted), and that the item-invoice
/// path + a hand-keyed as-voucher sale's print/label are UNCHANGED (purely additive). Amounts worked by hand to paisa.
/// </summary>
public sealed class ServiceAccountingInvoiceVoucherEntryViewModelTests : IDisposable
{
    private const string GstinMaharashtra = "27AAPFU0939F1ZV";
    private const string GstinGujarat = "24AAACC1206D1ZM";
    private static readonly DateOnly FyStart = new(2024, 4, 1);

    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public ServiceAccountingInvoiceVoucherEntryViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexServiceInvoiceTests_" + Guid.NewGuid().ToString("N"));
        _storage = new CompanyStorage(_tempDir);
    }

    // ---------------------------------------------------------------- scaffolding

    private sealed class Kit
    {
        public required MainWindowViewModel Vm { get; init; }
        public required string CompanyName { get; init; }
        public required Guid ConsultancyId { get; init; }   // Income, taxable @ 18% (SAC 998311)
        public required Guid FreightIncomeId { get; init; } // Income, taxable @ 5%  (SAC 996511)
        public required Guid ExemptServiceId { get; init; } // Income, exempt
        public required Guid NoSacServiceId { get; init; }  // Income, taxable, NO rate / NO SAC ⇒ unresolved
        public required Guid PlainSalesId { get; init; }    // Income, taxable @ 18% — used for the plain as-voucher sale
        public required Guid LocalCustomerId { get; init; } // in-state (27), registered (B2B)
        public required Guid InterCustomerId { get; init; } // Gujarat (24), inter-state
        public required Guid ConsumerId { get; init; }      // no GSTIN, in-state (B2C)
    }

    /// <summary>A GST-enabled (home Maharashtra 27) company with service-income ledgers and three parties.</summary>
    private Kit NewServiceKit(string companyName)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = companyName;
        vm.CreateCompany();
        Assert.Equal(Screen.Gateway, vm.CurrentScreen);

        var c = vm.Company!;
        c.FinancialYearStart = FyStart;
        c.BooksBeginFrom = FyStart;

        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = "27",
            Gstin = GstinMaharashtra,
            RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart,
            Periodicity = GstReturnPeriodicity.Monthly,
        });

        var consultancy = AddLedger(c, "Consultancy Income", "Sales Accounts");
        consultancy.SalesPurchaseGst = new StockItemGstDetails
        {
            HsnSac = "998311", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800,
            SupplyType = GstSupplyType.Services,
        };
        var freight = AddLedger(c, "Freight Income", "Direct Incomes");
        freight.SalesPurchaseGst = new StockItemGstDetails
        {
            HsnSac = "996511", Taxability = GstTaxability.Taxable, RateBasisPoints = 500,
            SupplyType = GstSupplyType.Services,
        };
        var exempt = AddLedger(c, "Exempt Service", "Sales Accounts");
        exempt.SalesPurchaseGst = new StockItemGstDetails
        {
            HsnSac = "999999", Taxability = GstTaxability.Exempt, SupplyType = GstSupplyType.Services,
        };
        var noSac = AddLedger(c, "Untaxed Service", "Sales Accounts");
        noSac.SalesPurchaseGst = new StockItemGstDetails
        {
            // Taxable but NO rate and NO SAC anywhere ⇒ ResolveRate is unresolved ⇒ fail fast (never a silent ₹0).
            HsnSac = null, Taxability = GstTaxability.Taxable, RateBasisPoints = null,
            SupplyType = GstSupplyType.Services,
        };
        var plainSales = AddLedger(c, "Sales @18%", "Sales Accounts");
        plainSales.SalesPurchaseGst = new StockItemGstDetails
        {
            HsnSac = "998314", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800,
            SupplyType = GstSupplyType.Services,
        };

        var localCustomer = AddLedger(c, "Local Customer", "Sundry Debtors");
        localCustomer.PartyGst = new PartyGstDetails { RegistrationType = GstRegistrationType.Regular, Gstin = GstinMaharashtra, StateCode = "27" };
        var interCustomer = AddLedger(c, "Gujarat Customer", "Sundry Debtors");
        interCustomer.PartyGst = new PartyGstDetails { RegistrationType = GstRegistrationType.Regular, Gstin = GstinGujarat, StateCode = "24" };
        var consumer = AddLedger(c, "Walk-in Consumer", "Sundry Debtors");
        consumer.PartyGst = new PartyGstDetails { RegistrationType = GstRegistrationType.Consumer, StateCode = "27" };

        _storage.Save(c);

        return new Kit
        {
            Vm = vm,
            CompanyName = companyName,
            ConsultancyId = consultancy.Id,
            FreightIncomeId = freight.Id,
            ExemptServiceId = exempt.Id,
            NoSacServiceId = noSac.Id,
            PlainSalesId = plainSales.Id,
            LocalCustomerId = localCustomer.Id,
            InterCustomerId = interCustomer.Id,
            ConsumerId = consumer.Id,
        };
    }

    private static DomainLedger AddLedger(Company c, string name, string groupName)
    {
        var group = c.FindGroupByName(groupName) ?? throw new InvalidOperationException($"No group '{groupName}'.");
        var ledger = new DomainLedger(Guid.NewGuid(), name, group.Id, Money.Zero, openingIsDebit: false);
        c.AddLedger(ledger);
        return ledger;
    }

    private Company Reload(string companyName)
    {
        var entry = _storage.ListCompanies().Single(e => e.Name == companyName);
        return _storage.Load(entry);
    }

    private static DateOnly AsOf(Company c) => c.FinancialYearStart.AddYears(1).AddDays(-1);

    private static void SelectParty(VoucherEntryViewModel entry, Guid partyId) =>
        entry.SelectedParty = entry.Parties.Single(p => p.Ledger?.Id == partyId);

    /// <summary>Fills Particulars line <paramref name="index"/> with (ledger, amount), adding rows as needed.</summary>
    private static void FillLine(VoucherEntryViewModel entry, Guid ledgerId, string amount, int index = 0)
    {
        while (entry.AccountingInvoiceLines.Count <= index) entry.AddAccountingInvoiceLine();
        var line = entry.AccountingInvoiceLines[index];
        line.SelectedLedger = entry.AccountingInvoiceLedgers.Single(l => l.Id == ledgerId);
        line.AmountText = amount;
    }

    private static decimal Signed(Company c, Guid ledgerId, DateOnly asOf) =>
        LedgerBalances.SignedClosing(c, c.FindLedger(ledgerId)!, asOf);

    private static Guid TaxLedgerId(Company c, GstTaxHead head, GstTaxDirection direction) =>
        new GstService(c).FindTaxLedger(head, direction)!.Id;

    private static Voucher PostedSale(Company c) =>
        c.Vouchers.Single(v => c.FindVoucherType(v.TypeId)!.BaseType == VoucherBaseType.Sales);

    private static VoucherEntryViewModel OpenAccountingSale(Kit k)
    {
        k.Vm.OpenVoucher(VoucherBaseType.Sales);
        var entry = k.Vm.VoucherEntry!;
        entry.ChangeMode(); // As Voucher -> Item Invoice
        entry.ChangeMode(); // Item Invoice -> Accounting Invoice
        Assert.True(entry.IsAccountingInvoice);
        Assert.True(entry.IsAccountingGstInvoice);
        return entry;
    }

    // ================================================================ (1) intra-state 18% service sale

    [Fact]
    public void ServiceSale_intraState_splitsCgstSgst()
    {
        var k = NewServiceKit("Svc Intra Co");
        var entry = OpenAccountingSale(k);
        SelectParty(entry, k.LocalCustomerId);

        // Consultancy @18% ₹5,000 intra ⇒ CGST 450 + SGST 450; customer = 5,900.
        FillLine(entry, k.ConsultancyId, "5000");
        Assert.Equal("450.00", entry.GstCgstText);
        Assert.Equal("450.00", entry.GstSgstText);
        Assert.Equal("0.00", entry.GstIgstText);
        Assert.Equal("5,900.00", entry.PartyTotalText);
        Assert.True(entry.CanAccept);
        Assert.True(entry.Accept());

        var c = k.Vm.Company!;
        var asOf = AsOf(c);
        var posted = PostedSale(c);
        Assert.True(VoucherValidator.IsBalanced(posted));
        Assert.False(posted.HasInventoryLines);                                   // no stock ever entered
        Assert.Equal(-5000m, Signed(c, k.ConsultancyId, asOf));                    // Cr income (taxable only)
        Assert.Equal(-450m, Signed(c, TaxLedgerId(c, GstTaxHead.Central, GstTaxDirection.Output), asOf));
        Assert.Equal(-450m, Signed(c, TaxLedgerId(c, GstTaxHead.State, GstTaxDirection.Output), asOf));
        Assert.Equal(0m, Signed(c, TaxLedgerId(c, GstTaxHead.Integrated, GstTaxDirection.Output), asOf)); // no IGST
        Assert.Equal(5900m, Signed(c, k.LocalCustomerId, asOf));                   // Dr Customer (taxable + tax)

        // Persisted end-to-end.
        var reloaded = Reload(k.CompanyName);
        Assert.Equal(5900m, Signed(reloaded, k.LocalCustomerId, AsOf(reloaded)));
        Assert.False(PostedSale(reloaded).HasInventoryLines);
    }

    // ================================================================ (2) inter-state 18% service sale ⇒ IGST

    [Fact]
    public void ServiceSale_interState_producesIgst()
    {
        var k = NewServiceKit("Svc Inter Co");
        var entry = OpenAccountingSale(k);
        SelectParty(entry, k.InterCustomerId);                 // Gujarat (24) ⇒ inter-state

        FillLine(entry, k.ConsultancyId, "5000");
        Assert.Equal("0.00", entry.GstCgstText);
        Assert.Equal("0.00", entry.GstSgstText);
        Assert.Equal("900.00", entry.GstIgstText);
        Assert.Equal("5,900.00", entry.PartyTotalText);
        Assert.True(entry.Accept());

        var c = k.Vm.Company!;
        var asOf = AsOf(c);
        Assert.Equal(-900m, Signed(c, TaxLedgerId(c, GstTaxHead.Integrated, GstTaxDirection.Output), asOf));
        Assert.Equal(0m, Signed(c, TaxLedgerId(c, GstTaxHead.Central, GstTaxDirection.Output), asOf));
        Assert.Equal(0m, Signed(c, TaxLedgerId(c, GstTaxHead.State, GstTaxDirection.Output), asOf));
        Assert.Equal(5900m, Signed(c, k.InterCustomerId, asOf));
    }

    // ================================================================ (3) exempt service ⇒ value, zero tax

    [Fact]
    public void ServiceSale_exemptLedger_valueNoTax()
    {
        var k = NewServiceKit("Svc Exempt Co");
        var entry = OpenAccountingSale(k);
        SelectParty(entry, k.LocalCustomerId);

        FillLine(entry, k.ExemptServiceId, "5000");
        Assert.Equal("0.00", entry.GstCgstText);
        Assert.Equal("0.00", entry.GstSgstText);
        Assert.Equal("0.00", entry.GstIgstText);
        Assert.Equal("5,000.00", entry.PartyTotalText);        // party owes only the value
        Assert.True(entry.CanAccept);
        Assert.True(entry.Accept());

        var c = k.Vm.Company!;
        var asOf = AsOf(c);
        var posted = PostedSale(c);
        Assert.Equal(2, posted.Lines.Count);                    // just Dr party / Cr income — no tax legs
        Assert.Equal(-5000m, Signed(c, k.ExemptServiceId, asOf));
        Assert.Equal(5000m, Signed(c, k.LocalCustomerId, asOf));
    }

    // ================================================================ (4) taxable ledger, NO SAC ⇒ fail fast

    [Fact]
    public void ServiceSale_ratedLedgerNoSac_failsFast()
    {
        var k = NewServiceKit("Svc NoSac Co");
        var entry = OpenAccountingSale(k);
        SelectParty(entry, k.LocalCustomerId);

        FillLine(entry, k.NoSacServiceId, "5000");
        Assert.False(entry.CanAccept);                          // the gate refuses (unresolved taxable ledger)
        Assert.False(entry.Accept());                           // and Accept refuses — never a silent ₹0
        Assert.NotNull(entry.Message);
        Assert.Contains("no GST rate", entry.Message!);
        Assert.Empty(k.Vm.Company!.Vouchers);                   // nothing posted
    }

    // ================================================================ (5) BLOCKER-1: CanAccept survives Recalculate()

    [Fact]
    public void AccountingMode_CanAccept_survivesRecalculate()
    {
        var k = NewServiceKit("Svc Recalc Co");
        var entry = OpenAccountingSale(k);
        SelectParty(entry, k.LocalCustomerId);
        FillLine(entry, k.ConsultancyId, "5000");
        Assert.True(entry.CanAccept);

        // Drive the REAL Recalculate() (the property-change entry point), not RecalculateAccountingInvoice directly:
        // without the :676 accounting route it falls through to the plain-grid branch and stomps CanAccept to false.
        entry.Recalculate();
        Assert.True(entry.CanAccept);
        Assert.True(entry.Accept());
    }

    // ================================================================ (6) multi-line, per-rate service legs

    [Fact]
    public void ServiceSale_multiRate_postsPerRateLegsAndTwoIncomeLegs()
    {
        var k = NewServiceKit("Svc MultiRate Co");
        var entry = OpenAccountingSale(k);
        SelectParty(entry, k.LocalCustomerId);

        // Consultancy @18% ₹10,000 (CGST 900 + SGST 900) + Freight @5% ₹2,000 (CGST 50 + SGST 50); party = 13,900.
        FillLine(entry, k.ConsultancyId, "10000", index: 0);
        FillLine(entry, k.FreightIncomeId, "2000", index: 1);
        Assert.Equal("950.00", entry.GstCgstText);
        Assert.Equal("950.00", entry.GstSgstText);
        Assert.Equal("13,900.00", entry.PartyTotalText);
        Assert.True(entry.Accept());

        var c = k.Vm.Company!;
        var asOf = AsOf(c);
        Assert.Equal(-10000m, Signed(c, k.ConsultancyId, asOf));  // two separate income legs
        Assert.Equal(-2000m, Signed(c, k.FreightIncomeId, asOf));
        Assert.Equal(-950m, Signed(c, TaxLedgerId(c, GstTaxHead.Central, GstTaxDirection.Output), asOf));
        Assert.Equal(13900m, Signed(c, k.LocalCustomerId, asOf));
    }

    // ================================================================ (7) GSTR-1: service SAC in the HSN/SAC summary

    [Fact]
    public void Gstr1_serviceSale_inHsnSacSummary_withSac()
    {
        var k = NewServiceKit("Svc Gstr1 Co");
        var entry = OpenAccountingSale(k);
        SelectParty(entry, k.LocalCustomerId);
        FillLine(entry, k.ConsultancyId, "5000");
        Assert.True(entry.Accept());

        var c = k.Vm.Company!;
        var g1 = Report.BuildGstr1(c, FyStart, AsOf(c));

        var sacRow = g1.HsnSummary.Single(h => h.HsnSac == "998311");
        Assert.Equal(Money.FromRupees(5000m), sacRow.TaxableValue);   // taxable == posted value
        Assert.Equal(Money.FromRupees(450m), sacRow.Cgst);            // read from the posted tax legs
        Assert.Equal(Money.FromRupees(450m), sacRow.Sgst);
        Assert.Equal(Money.Zero, sacRow.Igst);

        // The B2B row (already reads posted tax legs) is unchanged for a service invoice.
        var b2b = g1.B2B.Single(b => b.PartyGstin == GstinMaharashtra);
        Assert.Equal(Money.FromRupees(5000m), b2b.TaxableValue);
        Assert.Equal(Money.FromRupees(450m), b2b.Cgst);
    }

    // ================================================================ (8) GSTR-1 inter-state ⇒ SAC row IGST

    [Fact]
    public void Gstr1_serviceSale_interState_sacRowIgst()
    {
        var k = NewServiceKit("Svc Gstr1 Inter Co");
        var entry = OpenAccountingSale(k);
        SelectParty(entry, k.InterCustomerId);
        FillLine(entry, k.ConsultancyId, "5000");
        Assert.True(entry.Accept());

        var c = k.Vm.Company!;
        var g1 = Report.BuildGstr1(c, FyStart, AsOf(c));
        var sacRow = g1.HsnSummary.Single(h => h.HsnSac == "998311");
        Assert.Equal(Money.FromRupees(900m), sacRow.Igst);
        Assert.Equal(Money.Zero, sacRow.Cgst);
        Assert.Equal(Money.Zero, sacRow.Sgst);
    }

    // ================================================================ (9) must-fix 4: a plain taxable sale is NOT exempt

    [Fact]
    public void Gstr1_taxablePlainSale_notInExemptColumn()
    {
        var k = NewServiceKit("Svc PlainTaxable Co");
        var c = k.Vm.Company!;
        var service = new LedgerService(c);
        var salesType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales && t.IsActive);

        // A hand-keyed As-Voucher TAXABLE sale: the 18% "Sales @18%" ledger, but the plain grid posts NO tax legs.
        // It reaches the GSTR-1 no-tax (exempt) branch; it must NOT be attributed to the Table-12 exempt column.
        var v = new Voucher(Guid.NewGuid(), salesType.Id, FyStart.AddDays(10), new[]
        {
            new EntryLine(k.LocalCustomerId, Money.FromRupees(5000m), DrCr.Debit),
            new EntryLine(k.PlainSalesId, Money.FromRupees(5000m), DrCr.Credit),
        }, partyId: k.LocalCustomerId);
        service.Post(v);
        _storage.Save(c);

        var g1 = Report.BuildGstr1(c, FyStart, AsOf(c));
        Assert.Equal(Money.Zero, g1.ExemptNilNonGstValue);                     // NOT filed as exempt
        Assert.DoesNotContain(g1.HsnSummary, h => h.HsnSac == "998314");        // and not in the SAC summary either
    }

    // ================================================================ (10) must-fix HIGH: item invoice not double-counted

    [Fact]
    public void ExistingItemInvoice_notDoubleCounted_byServiceLegAttribution()
    {
        // A GST item invoice (stock lines) whose SALES value ledger ALSO carries a SalesPurchaseGst block — the exact
        // shape that would be double-counted if service-leg attribution ran regardless of InventoryLines.Count.
        var k = NewItemInvoiceKit("Svc ItemGuard Co", out var widgetId, out var mainGodownId, out var salesLedgerId, out var customerId);
        k.OpenVoucher(VoucherBaseType.Sales);
        var entry = k.VoucherEntry!;
        entry.ToggleItemInvoice();
        SelectParty(entry, customerId);
        FillItemLine(entry, widgetId, mainGodownId, 10m, "100.00");
        Assert.True(entry.Accept());

        var c = k.Company!;
        var g1 = Report.BuildGstr1(c, FyStart, AsOf(c));

        // Exactly ONE HSN row (the stock item's HSN), carrying the item value once — the sales ledger's SAC is not
        // added as a second row and the taxable value is not double-counted.
        var itemRow = g1.HsnSummary.Single(h => h.HsnSac == "847130");
        Assert.Equal(Money.FromRupees(1000m), itemRow.TaxableValue);
        Assert.DoesNotContain(g1.HsnSummary, h => h.HsnSac == "998311"); // the sales ledger's SAC never appears
        Assert.Single(g1.HsnSummary);
    }

    // ================================================================ (11) must-fix 6: hand-keyed sale print/label unchanged

    [Fact]
    public void PlainAsVoucherSale_printAndLabel_unchanged()
    {
        var k = NewServiceKit("Svc Print Co");
        var c = k.Vm.Company!;
        var service = new LedgerService(c);
        var salesType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales && t.IsActive);
        var cgstLedger = TaxLedgerId(c, GstTaxHead.Central, GstTaxDirection.Output);
        var sgstLedger = TaxLedgerId(c, GstTaxHead.State, GstTaxDirection.Output);

        // A hand-keyed As-Voucher GST sale carrying its OWN posted tax legs.
        var v = new Voucher(Guid.NewGuid(), salesType.Id, FyStart.AddDays(10), new[]
        {
            new EntryLine(k.LocalCustomerId, Money.FromRupees(5900m), DrCr.Debit),
            new EntryLine(k.PlainSalesId, Money.FromRupees(5000m), DrCr.Credit),
            new EntryLine(cgstLedger, Money.FromRupees(450m), DrCr.Credit),
            new EntryLine(sgstLedger, Money.FromRupees(450m), DrCr.Credit),
        }, partyId: k.LocalCustomerId);
        service.Post(v);

        // Conservative print: a ledger-only sale still prints as a PLAIN voucher (not relabelled "Tax Invoice"), and
        // the projected lines carry the POSTED tax legs verbatim — no recompute.
        Assert.False(VoucherPrintProjector.IsTaxInvoice(c, v));
        var data = VoucherPrintProjector.ProjectVoucher(c, v);
        Assert.Equal("Sales", data.VoucherTypeName);                         // label unchanged
        Assert.Contains(data.Lines, l => !l.IsDebit && l.Amount.Amount == 450m); // posted CGST leg printed
        Assert.Equal(2, data.Lines.Count(l => !l.IsDebit && l.Amount.Amount == 450m)); // both CGST + SGST
        Assert.Contains(data.Lines, l => l.IsDebit && l.Amount.Amount == 5900m);  // party leg
    }

    // ================================================================ (12) item-invoice path unchanged (additive refactor)

    [Fact]
    public void ItemInvoice_stillPostsIdentically_afterModeModel()
    {
        var k = NewItemInvoiceKit("Svc ItemParity Co", out var widgetId, out var mainGodownId, out var salesLedgerId, out var customerId);
        k.OpenVoucher(VoucherBaseType.Sales);
        var entry = k.VoucherEntry!;

        entry.ToggleItemInvoice();                    // Ctrl+I still a 2-way As-Voucher <-> Item flip
        Assert.True(entry.IsItemInvoice);
        Assert.False(entry.IsAccountingInvoice);
        SelectParty(entry, customerId);
        FillItemLine(entry, widgetId, mainGodownId, 10m, "100.00");
        Assert.Equal("90.00", entry.GstCgstText);
        Assert.Equal("1,180.00", entry.PartyTotalText);
        Assert.True(entry.Accept());

        var c = k.Company!;
        var asOf = AsOf(c);
        var posted = PostedSale(c);
        Assert.True(posted.HasInventoryLines);                 // the item path still moves stock
        Assert.Equal(1000m, posted.InventoryLinesValue.Amount);
        Assert.Equal(-1000m, Signed(c, salesLedgerId, asOf));
        Assert.Equal(1180m, Signed(c, customerId, asOf));
    }

    // ================================================================ (13) FIX-1: a SINGLE-rate invoice's exempt leg

    /// <summary>
    /// <b>FIX-1 (money).</b> On a SINGLE-rate accounting invoice the old <c>singleRate</c> collapse forced EVERY
    /// service leg into the one posted rate group — including a NON-taxable (exempt) ledger leg that
    /// <c>ComputeAccountingInvoiceGst</c> had correctly kept out of the tax base. Measured before the fix:
    /// <c>998311 taxable=10000 cgst=600 sgst=600 | 999999 taxable=5000 cgst=300 sgst=300 | exemptBucket=0</c> —
    /// three misstatements at once (taxable understated by 5,000 on the taxed SAC, tax attributed to an EXEMPT
    /// supply, and the exempt turnover missing from Table 8/12 entirely).
    /// <para><b>Bite:</b> restoring <c>var rate = singleRate ?? …</c> for a non-taxable leg reproduces 600/600 +
    /// 300/300 + exempt 0 and every assertion below fails.</para>
    /// </summary>
    [Fact]
    public void Gstr1_singleRateServiceSale_exemptLegIsNotBucketedIntoThePostedRateGroup()
    {
        var k = NewServiceKit("Svc SingleRateExempt Co");
        var entry = OpenAccountingSale(k);
        SelectParty(entry, k.LocalCustomerId);

        // Consultancy @18% ₹10,000 (the ONLY posted rate group: CGST 900 + SGST 900) + Exempt ₹5,000 (no tax).
        FillLine(entry, k.ConsultancyId, "10000", index: 0);
        FillLine(entry, k.ExemptServiceId, "5000", index: 1);
        Assert.Equal("900.00", entry.GstCgstText);              // the exempt leg is outside the tax base
        Assert.Equal("900.00", entry.GstSgstText);
        Assert.Equal("16,800.00", entry.PartyTotalText);        // 10,000 + 5,000 + 1,800
        Assert.True(entry.Accept());

        var c = k.Vm.Company!;
        var g1 = Report.BuildGstr1(c, FyStart, AsOf(c));

        var taxed = g1.HsnSummary.Single(h => h.HsnSac == "998311");
        Assert.Equal(Money.FromRupees(10000m), taxed.TaxableValue);   // NOT 15,000, NOT 10,000-with-600
        Assert.Equal(Money.FromRupees(900m), taxed.Cgst);
        Assert.Equal(Money.FromRupees(900m), taxed.Sgst);

        var exemptRow = g1.HsnSummary.Single(h => h.HsnSac == "999999");
        Assert.Equal(Money.FromRupees(5000m), exemptRow.TaxableValue);
        Assert.Equal(Money.Zero, exemptRow.Cgst);                     // tax on an exempt supply is a filing error
        Assert.Equal(Money.Zero, exemptRow.Sgst);
        Assert.Equal(Money.Zero, exemptRow.Igst);

        // …and the exempt turnover is actually declared.
        Assert.Equal(Money.FromRupees(5000m), g1.ExemptNilNonGstValue);
    }

    // ================================================================ (14) FIX-2: a MULTI-rate invoice's exempt leg

    /// <summary>
    /// <b>FIX-2 (money).</b> On a MULTI-rate accounting invoice <c>LedgerIntegratedRate</c> returns 0 for a
    /// non-taxable ledger, no rate-0 group exists, and the old <c>continue</c> DISCARDED the leg outright: the
    /// ₹5,000 exempt supply was absent from Table 12 AND from the exempt bucket — an outward supply that simply
    /// vanished from the return. Same root cause as FIX-1: a non-taxable service leg must never be bucketed into a
    /// posted rate group; it belongs in the exempt bucket + a zero-tax SAC row.
    /// <para><b>Bite:</b> deleting the non-taxable route in <c>AccumulateServiceHsn</c> drops the 999999 row and
    /// zeroes <c>ExemptNilNonGstValue</c>.</para>
    /// </summary>
    [Fact]
    public void Gstr1_multiRateServiceSale_exemptLegReachesTheExemptBucketNotOblivion()
    {
        var k = NewServiceKit("Svc MultiRateExempt Co");
        var entry = OpenAccountingSale(k);
        SelectParty(entry, k.LocalCustomerId);

        // 18% ₹10,000 (900+900) + 5% ₹2,000 (50+50) + exempt ₹5,000 (nothing).
        FillLine(entry, k.ConsultancyId, "10000", index: 0);
        FillLine(entry, k.FreightIncomeId, "2000", index: 1);
        FillLine(entry, k.ExemptServiceId, "5000", index: 2);
        Assert.Equal("950.00", entry.GstCgstText);
        Assert.Equal("950.00", entry.GstSgstText);
        Assert.Equal("18,900.00", entry.PartyTotalText);        // 17,000 + 1,900
        Assert.True(entry.Accept());

        var c = k.Vm.Company!;
        var g1 = Report.BuildGstr1(c, FyStart, AsOf(c));

        var high = g1.HsnSummary.Single(h => h.HsnSac == "998311");
        Assert.Equal(Money.FromRupees(10000m), high.TaxableValue);
        Assert.Equal(Money.FromRupees(900m), high.Cgst);
        Assert.Equal(Money.FromRupees(900m), high.Sgst);

        var low = g1.HsnSummary.Single(h => h.HsnSac == "996511");
        Assert.Equal(Money.FromRupees(2000m), low.TaxableValue);
        Assert.Equal(Money.FromRupees(50m), low.Cgst);
        Assert.Equal(Money.FromRupees(50m), low.Sgst);

        var exemptRow = g1.HsnSummary.Single(h => h.HsnSac == "999999");   // it EXISTS (before: absent)
        Assert.Equal(Money.FromRupees(5000m), exemptRow.TaxableValue);
        Assert.Equal(Money.Zero, exemptRow.Cgst);
        Assert.Equal(Money.FromRupees(5000m), g1.ExemptNilNonGstValue);    // and is declared (before: 0)
    }

    // ================================================================ (15) BITE: LedgerIntegratedRate must resolve

    /// <summary>
    /// The multi-rate service bucketing — the most complex new code — had no assertion that survived
    /// <c>LedgerIntegratedRate → 0</c>. With both taxable legs collapsed to rate 0 neither posted group matches
    /// any leg, so the 18% and 5% SAC rows carry ZERO tax (or vanish). This is the missing bite.
    /// </summary>
    [Fact]
    public void Gstr1_multiRateServiceSale_attributesEachSacItsOwnRatesTax()
    {
        var k = NewServiceKit("Svc MultiRateBite Co");
        var entry = OpenAccountingSale(k);
        SelectParty(entry, k.LocalCustomerId);

        FillLine(entry, k.ConsultancyId, "10000", index: 0);   // 18% ⇒ 900 + 900
        FillLine(entry, k.FreightIncomeId, "2000", index: 1);  // 5%  ⇒  50 +  50
        Assert.True(entry.Accept());

        var c = k.Vm.Company!;
        var g1 = Report.BuildGstr1(c, FyStart, AsOf(c));

        var high = g1.HsnSummary.Single(h => h.HsnSac == "998311");
        var low = g1.HsnSummary.Single(h => h.HsnSac == "996511");
        Assert.Equal(Money.FromRupees(900m), high.Cgst);       // never a blended value-share of 950
        Assert.Equal(Money.FromRupees(900m), high.Sgst);
        Assert.Equal(Money.FromRupees(50m), low.Cgst);
        Assert.Equal(Money.FromRupees(50m), low.Sgst);
        // Σ over the SAC rows == the invoice's posted tax, exactly.
        Assert.Equal(950m, g1.HsnSummary.Sum(h => h.Cgst.Amount));
        Assert.Equal(950m, g1.HsnSummary.Sum(h => h.Sgst.Amount));
    }

    // ================================================================ (16) BITE: the EXEMPT half of the double-count guard

    /// <summary>
    /// The anti-double-count guard has two halves; only the taxable half (<c>AccumulateServiceHsn</c>, test 10) was
    /// covered. Dropping the <c>InventoryLines.Count == 0</c> gate on the EXEMPT branch left the suite green. Here an
    /// EXEMPT item invoice whose sales value ledger ALSO carries a non-taxable <c>SalesPurchaseGst</c> block would be
    /// counted twice — 1,000 of exempt turnover filed as 2,000, plus a phantom second Table-12 row.
    /// </summary>
    [Fact]
    public void Gstr1_exemptItemInvoice_isNotCountedTwiceByTheServiceLegPass()
    {
        var k = NewExemptItemInvoiceKit("Svc ExemptItemGuard Co", out var widgetId, out var godownId, out var customerId);
        k.OpenVoucher(VoucherBaseType.Sales);
        var entry = k.VoucherEntry!;
        entry.ToggleItemInvoice();
        SelectParty(entry, customerId);
        FillItemLine(entry, widgetId, godownId, 10m, "100.00");
        Assert.True(entry.Accept());

        var c = k.Company!;
        var g1 = Report.BuildGstr1(c, FyStart, AsOf(c));

        Assert.Equal(Money.FromRupees(1000m), g1.ExemptNilNonGstValue);   // ONCE (before the gate: 2,000)
        Assert.Single(g1.HsnSummary);                                     // the item HSN only
        var itemRow = g1.HsnSummary.Single(h => h.HsnSac == "847130");
        Assert.Equal(Money.FromRupees(1000m), itemRow.TaxableValue);
        Assert.DoesNotContain(g1.HsnSummary, h => h.HsnSac == "999998");   // the sales ledger's SAC never appears
    }

    // ================================================================ (17) FIX-3: the PURCHASE side stays DEFERRED

    /// <summary>
    /// <b>FIX-3 (money + scope).</b> The purchase-side accounting invoice SHIPPED although the settled scope declared
    /// it DEFERRED, and it silently dropped §194J TDS (<c>TdsPossible</c>/<c>DetectTdsShape</c> read <c>Lines</c>,
    /// which is empty in accounting mode) and mis-evaluated RCM: a professional-fee purchase posted
    /// <c>Consultant Co Cr 118000 | Professional Fees Dr 100000 | Input CGST 9000 | Input SGST 9000</c> with NO TDS
    /// carve-out. Accounting mode is now gated to SALES ONLY; the purchase code path is dormant but UNREACHABLE.
    /// <para>Note the direct <c>Mode</c> write: the gate lives in <see cref="VoucherEntryViewModel.IsAccountingInvoice"/>,
    /// not merely in the cycle, so even forcing the enum cannot enter the mode.</para>
    /// </summary>
    [Fact]
    public void PurchaseVoucher_accountingInvoiceMode_isUnreachable()
    {
        var k = NewServiceKit("Svc PurchaseGate Co");
        k.Vm.OpenVoucher(VoucherBaseType.Purchase);
        var entry = k.Vm.VoucherEntry!;
        Assert.True(entry.CanBeItemInvoice);            // Purchase IS invoiceable (Ctrl+I still works)…
        Assert.False(entry.CanBeAccountingInvoice);     // …but the accounting mode is deferred here.

        entry.ToggleAccountingInvoice();                // the checkbox affordance
        Assert.False(entry.IsAccountingInvoice);
        Assert.True(entry.IsAsVoucherMode);

        entry.ChangeMode();                             // Ctrl+H: As Voucher -> Item Invoice
        Assert.True(entry.IsItemInvoice);
        entry.ChangeMode();                             // …and straight back, never into Accounting
        Assert.True(entry.IsAsVoucherMode);
        Assert.False(entry.IsAccountingInvoice);

        // Even forcing the raw enum cannot arm the purchase path: the derived gate refuses.
        entry.Mode = VoucherEntryMode.AccountingInvoice;
        Assert.False(entry.IsAccountingInvoice);
        Assert.False(entry.IsAccountingGstInvoice);
        Assert.False(entry.ShowParticularsGrid);
        Assert.True(entry.IsAsVoucherMode);             // it renders (and posts) as the plain Dr/Cr voucher
    }

    // ================================================================ (18) FIX-4: Alt+C on the Particulars ledger field

    /// <summary>
    /// <b>FIX-4 (high).</b> Alt+C create-on-the-fly was DEAD on the Particulars ledger field, two independent breaks:
    /// (a) <c>ApplyCreatedMaster</c> had no <see cref="AccountingInvoiceLineViewModel"/> case, so a created ledger was
    /// never written back; (b) <c>AccountingInvoiceLedgers</c> was a ctor-built <c>.ToList()</c> snapshot that
    /// <c>RefreshMasterPickers()</c> never rebuilt. Measured: <c>AccountingInvoiceLedgers contains new ledger = False</c>
    /// while StockLedgers and Parties both refreshed True. On a company with no income ledger the mode was unusable.
    /// Driven through the REAL create-on-the-fly round trip (the same entry point the Alt+C key uses).
    /// </summary>
    [Fact]
    public void AltC_onAParticularsLine_createsTheLedgerAndSelectsItInThatLine()
    {
        var k = NewServiceKit("Svc AltC Co");
        var entry = OpenAccountingSale(k);
        SelectParty(entry, k.LocalCustomerId);
        var line = entry.AccountingInvoiceLines[0];
        var before = entry.AccountingInvoiceLedgers.Count;

        Assert.True(k.Vm.CreateMasterOnTheFly(MasterCreateKind.Ledger, MasterCreateFields.Ledger, line));
        Assert.Equal(Screen.LedgerMaster, k.Vm.CurrentScreen);
        k.Vm.LedgerMaster!.Name = "Training Income";
        k.Vm.LedgerMaster!.SelectedGroup = k.Vm.Company!.FindGroupByName("Sales Accounts");
        Assert.True(k.Vm.LedgerMaster!.Create());   // the same round-trip Ctrl+A drives on the create screen

        // (b) the picker rebuilt…
        var created = k.Vm.Company!.FindLedgerByName("Training Income")!;
        Assert.Equal(before + 1, entry.AccountingInvoiceLedgers.Count);
        Assert.Contains(entry.AccountingInvoiceLedgers, l => l.Id == created.Id);
        // …and (a) the value landed back in THAT line (before: the field returned blank).
        Assert.Same(created, line.SelectedLedger);
        Assert.Same(entry, k.Vm.VoucherEntry);          // the in-progress voucher survived
    }

    /// <summary>The picker-refresh half of FIX-4 in isolation: <c>RefreshMasterPickers()</c> must rebuild the
    /// Particulars ledger list (Parties and StockLedgers already did), and must not lose an already-picked ledger.</summary>
    [Fact]
    public void RefreshMasterPickers_rebuildsTheParticularsLedgerPicker()
    {
        var k = NewServiceKit("Svc Refresh Co");
        var entry = OpenAccountingSale(k);
        FillLine(entry, k.ConsultancyId, "1000");
        var picked = entry.AccountingInvoiceLines[0].SelectedLedger;

        var c = k.Vm.Company!;
        var late = AddLedger(c, "Late Income", "Sales Accounts");
        late.SalesPurchaseGst = new StockItemGstDetails
        {
            HsnSac = "998319", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800,
            SupplyType = GstSupplyType.Services,
        };
        Assert.DoesNotContain(entry.AccountingInvoiceLedgers, l => l.Id == late.Id);  // snapshot is stale

        entry.RefreshMasterPickers();

        Assert.Contains(entry.AccountingInvoiceLedgers, l => l.Id == late.Id);
        Assert.Same(picked, entry.AccountingInvoiceLines[0].SelectedLedger);          // selection survives
    }

    // ================================================================ (19) FIX-5: a Particulars line can be removed

    /// <summary>
    /// <b>FIX-5.</b> <c>RemoveAccountingInvoiceLine</c> had ZERO call sites — a Particulars line could not be deleted;
    /// the grid's 40px remove column was declared but empty. This asserts the behaviour the now-wired button drives
    /// (the rendered affordance itself is locked in <c>ServiceAccountingInvoiceKeyboardTests</c>).
    /// </summary>
    [Fact]
    public void RemoveAccountingInvoiceLine_dropsTheLineAndRecomputes()
    {
        var k = NewServiceKit("Svc Remove Co");
        var entry = OpenAccountingSale(k);
        SelectParty(entry, k.LocalCustomerId);
        FillLine(entry, k.ConsultancyId, "10000", index: 0);
        FillLine(entry, k.FreightIncomeId, "2000", index: 1);
        Assert.Equal("950.00", entry.GstCgstText);

        entry.RemoveAccountingInvoiceLine(entry.AccountingInvoiceLines[1]);   // drop the 5% line

        Assert.DoesNotContain(entry.AccountingInvoiceLines, l => l.SelectedLedger?.Id == k.FreightIncomeId);
        Assert.Equal("900.00", entry.GstCgstText);                            // recomputed, not stale
        Assert.Equal("11,800.00", entry.PartyTotalText);
        Assert.True(entry.CanAccept);
    }

    // ================================================================ (20) FIX-6: the e-invoice SAC == the GSTR-1 SAC

    /// <summary>
    /// <b>FIX-6.</b> The slice taught GSTR-1 about service SACs but not the e-invoice payload, so they disagreed:
    /// the ledger-only synthetic-item path emitted <c>HsnCd = ""</c> while the same voucher filed SAC <c>998311</c> in
    /// Table 12 — a BLANK mandatory <c>HsnCd</c> is an IRP rejection. The e-invoice HsnCd must now equal the GSTR-1 SAC.
    /// </summary>
    [Fact]
    public void EInvoice_serviceSale_carriesTheSameSacAsGstr1()
    {
        var k = NewServiceKit("Svc EInv Co");
        var entry = OpenAccountingSale(k);
        SelectParty(entry, k.LocalCustomerId);
        FillLine(entry, k.ConsultancyId, "5000");
        Assert.True(entry.Accept());

        var c = k.Vm.Company!;
        var posted = PostedSale(c);
        var json = System.Text.Encoding.UTF8.GetString(Apex.Ledger.Io.EInvoiceJson.BuildInv01(c, posted));
        using var doc = System.Text.Json.JsonDocument.Parse(json);

        var items = doc.RootElement.GetProperty("ItemList");
        Assert.Equal(1, items.GetArrayLength());
        var hsnCd = items[0].GetProperty("HsnCd").GetString();
        Assert.Equal("998311", hsnCd);                                // before: "" (IRP rejection)

        // …and it is the SAME code GSTR-1 files, which is the whole point.
        var g1 = Report.BuildGstr1(c, FyStart, AsOf(c));
        Assert.Equal(g1.HsnSummary.Single().HsnSac, hsnCd);
        // Footing preserved: Σ item assessable == the declared assessable value.
        Assert.Equal(
            doc.RootElement.GetProperty("ValDtls").GetProperty("ass_val_paisa").GetInt64(),
            items.EnumerateArray().Sum(i => i.GetProperty("ass_amt_paisa").GetInt64()));
    }

    /// <summary>A multi-rate service invoice e-invoices ONE item per service leg, each with its own SAC — and the
    /// per-leg assessable values still foot to the declared total.</summary>
    [Fact]
    public void EInvoice_multiRateServiceSale_emitsOneItemPerSacAndStillFoots()
    {
        var k = NewServiceKit("Svc EInvMulti Co");
        var entry = OpenAccountingSale(k);
        SelectParty(entry, k.LocalCustomerId);
        FillLine(entry, k.ConsultancyId, "10000", index: 0);
        FillLine(entry, k.FreightIncomeId, "2000", index: 1);
        Assert.True(entry.Accept());

        var c = k.Vm.Company!;
        var json = System.Text.Encoding.UTF8.GetString(Apex.Ledger.Io.EInvoiceJson.BuildInv01(c, PostedSale(c)));
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var items = doc.RootElement.GetProperty("ItemList");

        Assert.Equal(2, items.GetArrayLength());
        var sacs = items.EnumerateArray().Select(i => i.GetProperty("HsnCd").GetString()).OrderBy(s => s, StringComparer.Ordinal).ToList();
        Assert.Equal(new[] { "996511", "998311" }, sacs);
        Assert.DoesNotContain(items.EnumerateArray(), i => string.IsNullOrEmpty(i.GetProperty("HsnCd").GetString()));
        Assert.Equal(
            doc.RootElement.GetProperty("ValDtls").GetProperty("ass_val_paisa").GetInt64(),
            items.EnumerateArray().Sum(i => i.GetProperty("ass_amt_paisa").GetInt64()));
    }

    /// <summary>ER-13 lock for FIX-6: a hand-keyed As-Voucher sale with NO SAC-bearing service leg still emits the
    /// old single synthetic item per rate group — the e-invoice change is additive to the service path only.</summary>
    [Fact]
    public void EInvoice_plainAsVoucherSale_keepsTheSyntheticItemPath()
    {
        var k = NewServiceKit("Svc EInvPlain Co");
        var c = k.Vm.Company!;
        var service = new LedgerService(c);
        var salesType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales && t.IsActive);

        // "Misc Income" carries NO SalesPurchaseGst block, so it is not a service leg at all. The tax legs are built
        // through the SAME GstService the entry screens use, so they carry real GstLineTax (an e-invoice payload is
        // read off the posted tax lines — hand-made EntryLines carry none and would yield no rate group at all).
        var misc = AddLedger(c, "Misc Income", "Sales Accounts");
        var tax = new GstService(c).ComputeInvoiceTax(
            new[] { new GstService.TaxableLine(Money.FromRupees(5000m), 1800) },
            interState: false, GstTaxDirection.Output);
        var lines = new List<EntryLine>
        {
            new(k.LocalCustomerId, Money.FromRupees(5900m), DrCr.Debit),
            new(misc.Id, Money.FromRupees(5000m), DrCr.Credit),
        };
        lines.AddRange(tax.TaxLines);
        var v = new Voucher(Guid.NewGuid(), salesType.Id, FyStart.AddDays(10), lines, partyId: k.LocalCustomerId);
        service.Post(v);

        var json = System.Text.Encoding.UTF8.GetString(Apex.Ledger.Io.EInvoiceJson.BuildInv01(c, v));
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var items = doc.RootElement.GetProperty("ItemList");
        Assert.Equal(1, items.GetArrayLength());
        Assert.Equal("", items[0].GetProperty("HsnCd").GetString());   // unchanged: no SAC to resolve
        Assert.Equal(500000L, items[0].GetProperty("ass_amt_paisa").GetInt64());
    }

    // ================================================================ (21) FIX-7 + the uncovered display gates

    /// <summary>
    /// <b>FIX-7.</b> After Ctrl+H away from Accounting the stale accounting <c>DerivedSummary</c> / GST band persisted
    /// (the fields are SHARED with the item path). Leaving the mode must clear them.
    /// </summary>
    [Fact]
    public void LeavingAccountingMode_clearsTheStaleServiceBandAndSummary()
    {
        var k = NewServiceKit("Svc StaleBand Co");
        var entry = OpenAccountingSale(k);
        SelectParty(entry, k.LocalCustomerId);
        FillLine(entry, k.ConsultancyId, "5000");
        Assert.Equal("450.00", entry.GstCgstText);
        Assert.Contains("Services", entry.DerivedSummary);

        entry.ChangeMode();                       // Accounting -> As Voucher

        Assert.True(entry.IsAsVoucherMode);
        Assert.Equal("0.00", entry.GstCgstText);
        Assert.Equal("0.00", entry.GstSgstText);
        Assert.Equal("0.00", entry.PartyTotalText);
        Assert.DoesNotContain("Services", entry.DerivedSummary);
    }

    /// <summary>FIX-7 (caption): the shared total caption read "Items Total" on a service invoice. It is now
    /// mode-aware.</summary>
    [Fact]
    public void LineTotalCaption_isModeAware()
    {
        var k = NewServiceKit("Svc Caption Co");
        k.Vm.OpenVoucher(VoucherBaseType.Sales);
        var entry = k.Vm.VoucherEntry!;

        entry.ChangeMode();                       // Item Invoice
        Assert.Contains("Items Total", entry.LineTotalCaption);
        entry.ChangeMode();                       // Accounting Invoice
        Assert.Contains("Services Total", entry.LineTotalCaption);
        Assert.DoesNotContain("Items Total", entry.LineTotalCaption);
    }

    /// <summary>BITE for must-fix 5: reverting the GST band's gate to <c>IsGstInvoice</c> hides the accounting
    /// invoice's authoritative tax breakdown — previously uncovered.</summary>
    [Fact]
    public void ShowGstTotals_isOn_forAGstAwareAccountingInvoice()
    {
        var k = NewServiceKit("Svc GstTotals Co");
        var entry = OpenAccountingSale(k);
        Assert.False(entry.IsGstInvoice);          // NOT an item invoice…
        Assert.True(entry.IsAccountingGstInvoice); // …but GST-aware…
        Assert.True(entry.ShowGstTotals);          // …so the band must show (bite: `=> IsGstInvoice` fails here)
    }

    /// <summary>BITE: <c>ShowParticularsGrid =&gt; false</c> was uncovered — the whole service grid would silently
    /// disappear while the mode still gated Accept.</summary>
    [Fact]
    public void ShowParticularsGrid_isOn_inAccountingModeOnly()
    {
        var k = NewServiceKit("Svc Particulars Co");
        k.Vm.OpenVoucher(VoucherBaseType.Sales);
        var entry = k.Vm.VoucherEntry!;
        Assert.False(entry.ShowParticularsGrid);   // As Voucher
        entry.ChangeMode();
        Assert.False(entry.ShowParticularsGrid);   // Item Invoice
        entry.ChangeMode();
        Assert.True(entry.ShowParticularsGrid);    // Accounting Invoice
    }

    // ---------------------------------------------------------------- exempt item-invoice kit (double-count guard)

    /// <summary>An EXEMPT item invoice whose sales VALUE ledger also carries a non-taxable <c>SalesPurchaseGst</c>
    /// block — the exact shape the exempt half of the double-count guard protects.</summary>
    private MainWindowViewModel NewExemptItemInvoiceKit(string companyName, out Guid widgetId, out Guid godownId, out Guid customerId)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = companyName;
        vm.CreateCompany();
        var c = vm.Company!;
        c.FinancialYearStart = FyStart;
        c.BooksBeginFrom = FyStart;

        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = "27", Gstin = GstinMaharashtra, RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart, Periodicity = GstReturnPeriodicity.Monthly,
        });

        var inv = new InventoryService(c);
        var grp = inv.CreateStockGroup("Goods");
        var nos = inv.CreateSimpleUnit("Nos", "Numbers", unitQuantityCode: "NOS");
        var main = c.MainLocation!.Id;
        var widget = inv.CreateStockItem("Widget", grp.Id, nos.Id);
        widget.Gst = new StockItemGstDetails { HsnSac = "847130", Taxability = GstTaxability.Exempt };
        inv.AddOpeningBalance(widget.Id, main, 200m, Money.FromRupees(100m));

        var sales = AddLedger(c, "Exempt Sales", "Sales Accounts");
        sales.SalesPurchaseGst = new StockItemGstDetails
        {
            HsnSac = "999998", Taxability = GstTaxability.Exempt, SupplyType = GstSupplyType.Services,
        };

        var customer = AddLedger(c, "Local Customer", "Sundry Debtors");
        customer.PartyGst = new PartyGstDetails { RegistrationType = GstRegistrationType.Regular, Gstin = GstinMaharashtra, StateCode = "27" };
        _storage.Save(c);

        widgetId = widget.Id;
        godownId = main;
        customerId = customer.Id;
        return vm;
    }

    // ================================================================ FIX-4: goods may not be billed on this screen

    /// <summary>
    /// <b>FIX-4 (medium) — a GOODS ledger was offered on the Accounting-Invoice Particulars picker, and billing one
    /// produced a Rule-46-INVALID tax invoice.</b> <c>IsAccountingLineLedger</c> only asked for an Income primary
    /// nature, and no <c>SupplyType</c> check existed anywhere on the path. Measured through the real screen before
    /// this guard: <c>offered=True</c>, Accept succeeded, the document was labelled "Tax Invoice", and its row printed
    /// <c>hsn/sac=847130 qty="" rate=""</c> — Rule 46(f) requires the quantity AND unit for a supply of goods, and an
    /// accounting invoice has neither by construction. Goods belong on an item invoice.
    ///
    /// <para>The guard refuses an explicit <see cref="GstSupplyType.Goods"/> declaration only: a ledger with no
    /// <c>SalesPurchaseGst</c> block declares no supply type (every ledger in a GST-off company, and every ledger just
    /// created with Alt+C) and stays offered, so the mode does not become unusable.</para>
    ///
    /// <para><b>Bite:</b> drop the <c>SupplyType</c> conjunct from <c>IsAccountingLineLedger</c> and the goods ledger
    /// is offered again.</para>
    /// </summary>
    [Fact]
    public void ParticularsPicker_doesNotOfferAGoodsSupplyLedger()
    {
        var k = NewServiceKit("Svc Goods Picker Co");
        var c = k.Vm.Company!;

        // A goods sales ledger, exactly as a trading company configures one: Income nature, HSN, SupplyType = Goods.
        var goodsSales = AddLedger(c, "Widget Sales", "Sales Accounts");
        goodsSales.SalesPurchaseGst = new StockItemGstDetails
        {
            HsnSac = "847130", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800,
            SupplyType = GstSupplyType.Goods,
        };
        // …and a service ledger created with NO GST block at all (the Alt+C shape), which must stay offered.
        var undeclared = AddLedger(c, "Training Income", "Sales Accounts");
        _storage.Save(c);

        var entry = OpenAccountingSale(k);
        entry.RefreshMasterPickers();

        Assert.DoesNotContain(entry.AccountingInvoiceLedgers, l => l.Id == goodsSales.Id);   // goods: refused
        Assert.Contains(entry.AccountingInvoiceLedgers, l => l.Id == k.ConsultancyId);       // services: offered
        Assert.Contains(entry.AccountingInvoiceLedgers, l => l.Id == undeclared.Id);         // no block: still offered
    }

    // ---------------------------------------------------------------- item-invoice kit (for the guard/parity tests)

    private MainWindowViewModel NewItemInvoiceKit(string companyName, out Guid widgetId, out Guid mainGodownId, out Guid salesLedgerId, out Guid customerId)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = companyName;
        vm.CreateCompany();
        var c = vm.Company!;
        c.FinancialYearStart = FyStart;
        c.BooksBeginFrom = FyStart;

        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = "27", Gstin = GstinMaharashtra, RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart, Periodicity = GstReturnPeriodicity.Monthly,
        });

        var inv = new InventoryService(c);
        var grp = inv.CreateStockGroup("Goods");
        var nos = inv.CreateSimpleUnit("Nos", "Numbers", unitQuantityCode: "NOS");
        var main = c.MainLocation!.Id;
        var widget = inv.CreateStockItem("Widget", grp.Id, nos.Id);
        widget.Gst = new StockItemGstDetails { HsnSac = "847130", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800 };
        inv.AddOpeningBalance(widget.Id, main, 200m, Money.FromRupees(100m));

        // The sales value ledger carries a SalesPurchaseGst (SAC) block — the double-count trap: it is a service-shaped
        // ledger leg on an item invoice, which must NOT be attributed as a service leg (InventoryLines.Count > 0).
        var sales = AddLedger(c, "Sales", "Sales Accounts");
        sales.SalesPurchaseGst = new StockItemGstDetails
        {
            HsnSac = "998311", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800, SupplyType = GstSupplyType.Services,
        };

        var customer = AddLedger(c, "Local Customer", "Sundry Debtors");
        customer.PartyGst = new PartyGstDetails { RegistrationType = GstRegistrationType.Regular, Gstin = GstinMaharashtra, StateCode = "27" };
        _storage.Save(c);

        widgetId = widget.Id;
        mainGodownId = main;
        salesLedgerId = sales.Id;
        customerId = customer.Id;
        return vm;
    }

    private static void FillItemLine(VoucherEntryViewModel entry, Guid itemId, Guid godownId, decimal qty, string rate, int index = 0)
    {
        while (entry.InventoryLines.Count <= index) entry.AddInventoryLine();
        var line = entry.InventoryLines[index];
        line.SelectedItem = entry.StockItems.Single(i => i.Id == itemId);
        line.SelectedGodown = entry.Godowns.Single(g => g.Id == godownId);
        line.QuantityText = qty.ToString(System.Globalization.CultureInfo.InvariantCulture);
        line.RateText = rate;
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch { /* best-effort temp cleanup */ }
    }
}
