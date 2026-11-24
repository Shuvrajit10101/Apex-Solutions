using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using Xunit;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.Tests;

/// <summary>
/// Census <b>5.7</b>, T2-174 (wave 13, track id, IV-391..IV-394): the statutory reports that skipped only a
/// <i>cancelled</i> voucher now also skip an <i>Optional</i> one. Vendor (TallyPrime),
/// help.tallysolutions.com/create-optional-voucher/, opened by content 2026-11-24: Optional Vouchers record tentative
/// or future transactions <i>"without affecting your books of accounts"</i>, and when the transaction is due you
/// <i>"make the entry regular to include its accounting impact in the books of accounts"</i> (Ctrl+L, Regular).
///
/// <para>Each fixture holds a live voucher beside ONE Optional voucher of the same shape (the TDS/TCS books hold one
/// Optional voucher per report walk). Every figure is read off a book reloaded through a fresh
/// <see cref="CompanyStorage"/>, first while Optional and then after the Optional voucher is regularised on that
/// reloaded book and saved again, so neither half can pass vacuously.</para>
/// </summary>
public sealed class OptionalFilterSweepStatutoryReportTests : IDisposable
{
    private const string ValidTan = "MUMA12345B";
    private const string VendorPan = "AAPFU0939F";
    private const string BuyerPan = "AAQCS1234K";
    private const string GstinMaharashtra = "27AAPFU0939F1ZV";
    private static readonly DateOnly Fy2025 = new(2025, 4, 1);
    private static readonly DateOnly AsOf = new(2025, 6, 30);

    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public OptionalFilterSweepStatutoryReportTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexOptionalSweepStat_" + Guid.NewGuid().ToString("N"));
        _storage = new CompanyStorage(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>Loads the named book through a brand-new <see cref="CompanyStorage"/> handle.</summary>
    private (Company Book, CompanyStorage Store) Reload(string name)
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        var fresh = new CompanyStorage(_tempDir);
        return (fresh.Load(fresh.ListCompanies().Single(e => e.Name == name)), fresh);
    }

    /// <summary>Ctrl+L (Regular) on the RELOADED book — the engine verb the voucher screen's Ctrl+L calls — then saved
    /// through that same fresh handle, so the next reload reads the regularised flag back off disk.</summary>
    private void RegulariseOnDisk(string name, params Guid[] voucherIds)
    {
        var (book, store) = Reload(name);
        var svc = new LedgerService(book);
        foreach (var id in voucherIds)
        {
            Assert.True(book.FindVoucher(id)!.Optional, "the fixture's voucher must still be Optional on disk");
            svc.SetOptional(id, false);
        }
        store.Save(book);
    }

    private static string Unique(string stem) => stem + " " + Guid.NewGuid().ToString("N")[..6];

    private static DomainLedger AddLedger(Company c, string name, string groupName, bool openingIsDebit)
    {
        var l = new DomainLedger(Guid.NewGuid(), name, c.FindGroupByName(groupName)!.Id, Money.Zero, openingIsDebit);
        c.AddLedger(l);
        return l;
    }

    // =========================================================================================== TDS

    private static Company NewTdsCompany(string name)
    {
        var c = CompanyFactory.CreateSeeded(name, Fy2025);
        new TdsTcsService(c).EnableTds(new TdsConfig
        {
            Tan = ValidTan, DeductorType = DeductorType.Company,
            ResponsiblePersonName = "A. Sharma", ResponsiblePersonPan = VendorPan,
            ResponsiblePersonDesignation = "Director", ResponsiblePersonAddress = "12 MG Road",
        });
        return c;
    }

    private static DomainLedger Deductee(Company c, string name, string section, DeducteeType type, string? pan)
    {
        var v = AddLedger(c, name, "Sundry Creditors", false);
        v.TdsApplicable = true;
        v.TdsNatureOfPaymentId = c.FindNatureOfPaymentByCode(section)!.Id;
        v.DeducteeType = type;
        v.PartyPan = pan;
        return v;
    }

    /// <summary>Books an expense journal carrying the engine's own TDS carve-out for <paramref name="vendor"/>'s
    /// default section, and returns the posted voucher's id.</summary>
    private static Guid BookDeduction(Company c, DateOnly on, DomainLedger vendor, decimal gross)
    {
        var fees = AddLedger(c, $"Expense {Guid.NewGuid():N}", "Indirect Expenses", true);
        var nop = c.FindNatureOfPayment(vendor.TdsNatureOfPaymentId!.Value)!;
        var g = Money.FromRupees(gross);
        var carve = new TdsService(c).BuildCarveOut(g, g, nop, vendor, on);
        var lines = new List<EntryLine> { new(fees.Id, g, DrCr.Debit), carve.PartyLine };
        if (carve.TdsPayableLine is not null) lines.Add(carve.TdsPayableLine);
        var journal = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Journal).Id;
        return new LedgerService(c).Post(new Voucher(Guid.NewGuid(), journal, on, lines)).Id;
    }

    /// <summary>
    /// T2-174 at <c>TdsCoverage.CollectUnits</c> (TDS Outstanding / Nature-wise deducted), and at both
    /// <c>TdsExceptionReports</c> below-threshold walks (TDS Not Deducted, and the Nature-wise Summary's
    /// below-threshold count). An Optional §194J(b) deduction of ₹1,23,456.78 and an Optional below-threshold §194C
    /// payment of ₹23,456.78 move none of the three until they are regularised.
    /// </summary>
    [Fact]
    public void Optional_tds_vouchers_are_not_deducted_not_outstanding_and_not_below_threshold_until_regularised()
    {
        var name = Unique("Optional TDS Co");
        var c = NewTdsCompany(name);
        var consultant = Deductee(c, "Consultant", "194J(b)", DeducteeType.Firm, VendorPan);
        var contractor = Deductee(c, "Contractor", "194C", DeducteeType.Individual, VendorPan);

        BookDeduction(c, new DateOnly(2025, 5, 10), consultant, 1_00_000m);       // live: TDS 10,000
        BookDeduction(c, new DateOnly(2025, 5, 11), contractor, 12_345.67m);      // live: below threshold, TDS 0
        var optDeduction = BookDeduction(c, new DateOnly(2025, 5, 20), consultant, 1_23_456.78m);
        var optBelow = BookDeduction(c, new DateOnly(2025, 5, 21), contractor, 23_456.78m);
        var svc = new LedgerService(c);
        svc.SetOptional(optDeduction, true);
        svc.SetOptional(optBelow, true);
        _storage.Save(c);

        var (opt, _) = Reload(name);
        var outstanding = Assert.Single(Report.BuildTdsOutstanding(opt, AsOf).Rows);
        Assert.Equal("194J(b)", outstanding.Section);
        Assert.Equal(10_000.00m, outstanding.Deducted.Amount);
        Assert.Equal(10_000.00m, outstanding.Outstanding.Amount);

        var notDeducted = Assert.Single(Report.BuildTdsNotDeducted(opt, AsOf).Rows);
        Assert.Equal(12_345.67m, notDeducted.Assessable.Amount);

        var summary = Report.BuildTdsNatureSummary(opt, AsOf).Rows;
        Assert.Equal(10_000.00m, summary.Single(r => r.Section == "194J(b)").Deducted.Amount);
        var c194 = summary.Single(r => r.Section == "194C");
        Assert.Equal(1, c194.BelowThresholdCount);
        Assert.Equal(12_345.67m, c194.Assessable.Amount);

        RegulariseOnDisk(name, optDeduction, optBelow);

        var (live, _) = Reload(name);
        var outstandingLive = Assert.Single(Report.BuildTdsOutstanding(live, AsOf).Rows);
        Assert.Equal(22_346.00m, outstandingLive.Deducted.Amount);
        Assert.Equal(22_346.00m, outstandingLive.Outstanding.Amount);

        var notDeductedLive = Report.BuildTdsNotDeducted(live, AsOf).Rows;
        Assert.Equal(2, notDeductedLive.Count);
        Assert.Equal(35_802.45m, notDeductedLive.Sum(r => r.Assessable.Amount));

        var summaryLive = Report.BuildTdsNatureSummary(live, AsOf).Rows;
        Assert.Equal(22_346.00m, summaryLive.Single(r => r.Section == "194J(b)").Deducted.Amount);
        var c194Live = summaryLive.Single(r => r.Section == "194C");
        Assert.Equal(2, c194Live.BelowThresholdCount);
        Assert.Equal(35_802.45m, c194Live.Assessable.Amount);
    }

    /// <summary>
    /// T2-174 at <c>LedgersWithoutPan.Build</c>: an Optional no-PAN deduction adds nothing to the party's
    /// "tax at the no-PAN rate" until it is regularised.
    /// </summary>
    [Fact]
    public void An_optional_no_pan_deduction_adds_no_no_pan_tax_until_regularised()
    {
        var name = Unique("Optional No-PAN Co");
        var c = NewTdsCompany(name);
        var noPan = Deductee(c, "No-PAN Vendor", "194J(b)", DeducteeType.Firm, pan: null);

        BookDeduction(c, new DateOnly(2025, 5, 10), noPan, 1_00_000m);                 // 20% => 20,000
        var opt = BookDeduction(c, new DateOnly(2025, 5, 20), noPan, 54_321.37m);       // 20% => 10,864 (TDS is rounded to the rupee)
        new LedgerService(c).SetOptional(opt, true);
        _storage.Save(c);

        var (optional, _) = Reload(name);
        var row = Assert.Single(Report.BuildLedgersWithoutPan(optional, AsOf).Rows);
        Assert.Equal("No-PAN Vendor", row.Party);
        Assert.Equal(20_000.00m, row.TaxAtNoPanRate.Amount);

        RegulariseOnDisk(name, opt);

        var (live, _) = Reload(name);
        Assert.Equal(30_864.00m, Assert.Single(Report.BuildLedgersWithoutPan(live, AsOf).Rows).TaxAtNoPanRate.Amount);
    }

    // =========================================================================================== TCS

    private static Company NewTcsCompany(string name)
    {
        var c = CompanyFactory.CreateSeeded(name, Fy2025);
        new TdsTcsService(c).EnableTcs(new TcsConfig
        {
            Tan = ValidTan, CollectorType = DeductorType.Company,
            ResponsiblePersonName = "A. Sharma", ResponsiblePersonPan = VendorPan,
            ResponsiblePersonDesignation = "Director", ResponsiblePersonAddress = "12 MG Road",
        });
        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = "27", Gstin = GstinMaharashtra, RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = Fy2025, Periodicity = GstReturnPeriodicity.Monthly,
        });
        return c;
    }

    /// <summary>Posts a sale of <paramref name="value"/> (+18% GST) of an item whose TCS nature is
    /// <paramref name="code"/>, collecting whatever the engine says, and returns the voucher's id.</summary>
    private static Guid BookTcsSale(Company c, StockItem item, DomainLedger sales, DomainLedger buyer, string code,
        DateOnly on, decimal value, decimal qty)
    {
        var gst = new GstService(c);
        var v = Money.FromRupees(value);
        var tax = gst.ComputeInvoiceTax(new[] { new GstService.TaxableLine(v, 1800) }, false, GstTaxDirection.Output);
        var nature = c.FindNatureOfGoodsByCode(code)!;
        var col = new TcsService(c).BuildCollection(v, tax.TotalTax, nature, buyer, on);
        var buyerDebit = col.Applies
            ? new EntryLine(buyer.Id, v + tax.TotalTax + col.TcsAmount, DrCr.Debit)
            : new EntryLine(buyer.Id, v + tax.TotalTax, DrCr.Debit, tcs: col.Detail);
        var lines = new List<EntryLine> { buyerDebit, new(sales.Id, v, DrCr.Credit) };
        lines.AddRange(tax.TaxLines);
        if (col.TcsPayableLine is not null) lines.Add(col.TcsPayableLine);
        return new LedgerService(c).Post(new Voucher(Guid.NewGuid(),
            c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id, on, lines,
            inventoryLines: new[] { new VoucherInventoryLine(item.Id, c.MainLocation!.Id, qty, Money.FromRupees(value / qty)) })).Id;
    }

    /// <summary>
    /// T2-174 at <c>TcsCoverage.CollectUnits</c> (TCS Outstanding / Nature-wise collected) and at both
    /// <c>TcsExceptionReports</c> below-threshold walks (TCS Not Collected, and the Nature-wise Summary's
    /// below-threshold count).
    /// </summary>
    [Fact]
    public void Optional_tcs_sales_are_not_collected_not_outstanding_and_not_below_threshold_until_regularised()
    {
        var name = Unique("Optional TCS Co");
        var c = NewTcsCompany(name);
        var inv = new InventoryService(c);
        var group = inv.CreateStockGroup("Waste");
        var kg = inv.CreateSimpleUnit("Kg", "Kilogram");
        var scrap = inv.CreateStockItem("Scrap Metal", group.Id, kg.Id);
        scrap.Gst = new StockItemGstDetails { Taxability = GstTaxability.Taxable, RateBasisPoints = 1800 };
        inv.AddOpeningBalance(scrap.Id, c.MainLocation!.Id, 10_000m, Money.FromRupees(10m));

        var sales = AddLedger(c, "Scrap Sales", "Sales Accounts", false);
        var buyer = AddLedger(c, "Scrap Buyer", "Sundry Debtors", true);
        buyer.PartyGst = new PartyGstDetails
        { RegistrationType = GstRegistrationType.Regular, Gstin = GstinMaharashtra, StateCode = "27" };
        buyer.TcsApplicable = true; buyer.CollecteeType = CollecteeType.Individual; buyer.PartyPan = BuyerPan;

        BookTcsSale(c, scrap, sales, buyer, "6CE", new DateOnly(2025, 5, 10), 1_00_000m, 1000m);   // 1% of 1,18,000
        BookTcsSale(c, scrap, sales, buyer, "6CL", new DateOnly(2025, 5, 11), 2_00_000m, 100m);    // below 10,00,000
        var optCollected = BookTcsSale(c, scrap, sales, buyer, "6CE", new DateOnly(2025, 5, 20), 54_321.37m, 1m);
        var optBelow = BookTcsSale(c, scrap, sales, buyer, "6CL", new DateOnly(2025, 5, 21), 1_23_456.78m, 1m);
        var svc = new LedgerService(c);
        svc.SetOptional(optCollected, true);
        svc.SetOptional(optBelow, true);
        _storage.Save(c);

        var (opt, _) = Reload(name);
        var outstanding = Assert.Single(Report.BuildTcsOutstanding(opt, AsOf).Rows);
        Assert.Equal("6CE", outstanding.Code);
        Assert.Equal(1_180.00m, outstanding.Collected.Amount);

        var notCollected = Assert.Single(Report.BuildTcsNotCollected(opt, AsOf).Rows);
        Assert.Equal("6CL", notCollected.Code);
        Assert.Equal(2_36_000.00m, notCollected.Assessable.Amount);

        var summary = Report.BuildTcsNatureSummary(opt, AsOf).Rows;
        Assert.Equal(1_180.00m, summary.Single(r => r.Code == "6CE").Collected.Amount);
        var cl = summary.Single(r => r.Code == "6CL");
        Assert.Equal(1, cl.BelowThresholdCount);
        Assert.Equal(2_36_000.00m, cl.Assessable.Amount);

        RegulariseOnDisk(name, optCollected, optBelow);

        var (live, _) = Reload(name);
        var outstandingLive = Assert.Single(Report.BuildTcsOutstanding(live, AsOf).Rows);
        Assert.Equal(1_821.00m, outstandingLive.Collected.Amount);

        var notCollectedLive = Report.BuildTcsNotCollected(live, AsOf).Rows;
        Assert.Equal(2, notCollectedLive.Count);
        Assert.Equal(3_81_679.00m, notCollectedLive.Sum(r => r.Assessable.Amount));

        var summaryLive = Report.BuildTcsNatureSummary(live, AsOf).Rows;
        Assert.Equal(1_821.00m, summaryLive.Single(r => r.Code == "6CE").Collected.Amount);
        var clLive = summaryLive.Single(r => r.Code == "6CL");
        Assert.Equal(2, clLive.BelowThresholdCount);
        Assert.Equal(3_81_679.00m, clLive.Assessable.Amount);
    }

    // =========================================================================================== Form 24Q

    /// <summary>
    /// T2-174 at the three <c>Form24Q</c> walks: Annexure I's deductee rows (<c>CollectDeductions</c>), and Annexure
    /// II's gross salary (<c>SumPostedTaxableGross</c>) and tax deducted (<c>SumPostedSalaryTds</c>). The April payroll
    /// voucher is Optional; it is neither a Q1 deductee row nor part of the year's salary until it is regularised.
    /// </summary>
    [Fact]
    public void An_optional_payroll_month_is_absent_from_form_24q_until_regularised()
    {
        var name = Unique("Optional 24Q Co");
        var c = CompanyFactory.CreateSeeded(name, Fy2025, Fy2025);
        var pay = new PayrollService(c);
        pay.EnablePayroll();
        pay.EnableSalaryTds();
        var ph = new PayHeadService(c);
        var basic = ph.CreatePayHead("Basic", PayHeadType.Earnings, PayHeadCalculationType.FlatRate,
            underGroupId: c.FindGroupByName("Indirect Expenses")!.Id);
        var tds = ph.CreatePayHead("TDS on Salary", PayHeadType.EmployeesStatutoryDeductions,
            PayHeadCalculationType.AsUserDefinedValue, underGroupId: c.FindGroupByName("Current Liabilities")!.Id,
            incomeTaxComponent: IncomeTaxComponent.TaxDeductedAtSource);
        var e = pay.CreateEmployee("Anita Rao", pay.CreateEmployeeGroup("Staff").Id);
        var emp = c.FindEmployee(e.Id)!;
        emp.ApplicableTaxRegime = TaxRegime.New;
        emp.Pan = "ABCDE1234F";
        new SalaryStructureService(c).DefineForEmployee(e.Id, Fy2025, new[]
        {
            new SalaryStructureLine(basic.Id, 0, new Money(125_000m)),
            new SalaryStructureLine(tds.Id, 1),
        });
        for (var m = 0; m < 12; m++)
        {
            var from = Fy2025.AddMonths(m);
            new PayrollVoucherService(c).Post(from, from.AddMonths(1).AddDays(-1), new[] { e.Id });
        }

        var april = c.Vouchers.Single(v => v.Date.Year == 2025 && v.Date.Month == 4 && v.Lines.Any(l => l.Payroll is not null));
        new LedgerService(c).SetOptional(april.Id, true);
        _storage.Save(c);

        var (opt, _) = Reload(name);
        var q1 = Form24Q.Build(opt, 2025, 1);
        Assert.Equal(2, q1.Deductees.Count);
        Assert.Equal(16_250.00m, q1.TotalTdsDeducted.Amount);
        var annexOpt = Assert.Single(Form24Q.Build(opt, 2025, 4).AnnexureII);
        Assert.Equal(13_75_000.00m, annexOpt.GrossSalary.Amount);
        Assert.Equal(89_375.00m, annexOpt.TaxDeducted.Amount);

        RegulariseOnDisk(name, april.Id);

        var (live, _) = Reload(name);
        var q1Live = Form24Q.Build(live, 2025, 1);
        Assert.Equal(3, q1Live.Deductees.Count);
        Assert.Equal(24_375.00m, q1Live.TotalTdsDeducted.Amount);
        var annexLive = Assert.Single(Form24Q.Build(live, 2025, 4).AnnexureII);
        Assert.Equal(15_00_000.00m, annexLive.GrossSalary.Amount);
        Assert.Equal(97_500.00m, annexLive.TaxDeducted.Amount);
    }
}
