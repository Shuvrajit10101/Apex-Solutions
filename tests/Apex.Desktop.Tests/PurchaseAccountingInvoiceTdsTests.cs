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
/// <b>G-7</b> — Purchase <b>Accounting Invoice</b> mode, and the §194J carve-out that gated it off.
///
/// <para><b>Why this file is mandatory.</b> The mode was written, then deliberately disabled, because shipping it
/// silently BROKE MONEY: <c>TdsPossible</c> and <c>DetectTdsShape</c> read the plain <c>Lines</c> collection, which
/// is EMPTY in accounting mode, so a professional-fee purchase posted <c>Cr Consultant / Dr Professional Fees /
/// Dr Input CGST / Dr Input SGST</c> with <b>no §194J TDS carve-out at all</b> — and RCM mis-evaluated identically.
/// The rationale comment at <c>VoucherEntryViewModel.cs:70-79</c> is explicit that the fix is to wire TDS/RCM to the
/// Particulars lines BEFORE flipping the predicate. These tests are that proof.</para>
///
/// <para>Corpus: SG p.80 names exactly the two use cases the mode exists for — service purchases and fixed-asset
/// purchases. Rates: §194J(b) professional services = 10% with PAN, cumulative threshold ₹50,000
/// (<c>SeedTdsTcsRates</c>). TDS rounds to the nearest whole rupee, round-half-up.</para>
///
/// <para><b>Odd-paisa fixtures throughout</b> — round figures would not distinguish a correct carve-out from a
/// dozen wrong ones.</para>
/// </summary>
public sealed class PurchaseAccountingInvoiceTdsTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public PurchaseAccountingInvoiceTdsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexPurchAcctInv_" + Guid.NewGuid().ToString("N"));
        _storage = new CompanyStorage(_tempDir);
    }

    private sealed class Kit
    {
        public required MainWindowViewModel Vm { get; init; }
        public required Guid FeesId { get; init; }
        public required Guid VendorId { get; init; }
        public required Guid NonTdsExpenseId { get; init; }
        public required Guid PlainVendorId { get; init; }
    }

    /// <summary>
    /// A TDS-enabled company with a §194J(b) "Professional Fees" expense ledger, a deductee vendor (Firm, with PAN),
    /// a NON-TDS expense ledger and a NON-deductee vendor — the two controls that prove the carve-out is targeted.
    /// </summary>
    private Kit NewKit(string companyName)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = companyName;
        vm.CreateCompany();
        var c = vm.Company!;

        new TdsTcsService(c).EnableTds(new TdsConfig { Tan = "MUMA12345B" });

        var fees = AddLedger(c, "Professional Fees", "Indirect Expenses");
        fees.TdsApplicable = true;
        fees.TdsNatureOfPaymentId = c.FindNatureOfPaymentByCode("194J(b)")!.Id;

        var stationery = AddLedger(c, "Printing & Stationery", "Indirect Expenses");

        var vendor = AddLedger(c, "Acme Consultants", "Sundry Creditors");
        vendor.DeducteeType = DeducteeType.Firm;
        vendor.PartyPan = "AAPFU0939F";

        var plainVendor = AddLedger(c, "Zeta Stationers", "Sundry Creditors");

        _storage.Save(c);
        return new Kit
        {
            Vm = vm,
            FeesId = fees.Id,
            VendorId = vendor.Id,
            NonTdsExpenseId = stationery.Id,
            PlainVendorId = plainVendor.Id,
        };
    }

    private static DomainLedger AddLedger(Company c, string name, string groupName)
    {
        var group = c.FindGroupByName(groupName) ?? throw new InvalidOperationException($"No group '{groupName}'.");
        var ledger = new DomainLedger(Guid.NewGuid(), name, group.Id, Money.Zero, openingIsDebit: false);
        c.AddLedger(ledger);
        return ledger;
    }

    /// <summary>Opens a Purchase in accounting-invoice mode with one Particulars line.</summary>
    private static VoucherEntryViewModel OpenAccountingPurchase(Kit k, Guid expenseId, Guid partyId, string amount)
    {
        k.Vm.OpenVoucher(VoucherBaseType.Purchase);
        var e = k.Vm.VoucherEntry!;
        e.Mode = VoucherEntryMode.AccountingInvoice;
        e.SelectedParty = e.Parties.Single(p => p.Ledger?.Id == partyId);
        var line = e.AccountingInvoiceLines[0];
        line.SelectedLedger = e.AccountingInvoiceLedgers.Single(l => l.Id == expenseId);
        line.AmountText = amount;
        e.RecalculateAccountingInvoice();
        return e;
    }

    // ================================================================ (1) the mode is available at all

    /// <summary>SG p.80 — Purchase must offer Accounting Invoice mode (service and fixed-asset purchases).</summary>
    [Fact]
    public void Purchase_offers_accounting_invoice_mode()
    {
        var k = NewKit("Purchase Acct Mode Co");
        k.Vm.OpenVoucher(VoucherBaseType.Purchase);
        var e = k.Vm.VoucherEntry!;

        Assert.True(e.CanBeAccountingInvoice);

        // Ctrl+H cycles As Voucher → Item Invoice → Accounting Invoice → As Voucher, all three arms now present.
        Assert.True(e.IsAsVoucherMode);
        e.ChangeMode();
        Assert.True(e.IsItemInvoice);
        e.ChangeMode();
        Assert.True(e.IsAccountingInvoice);
        e.ChangeMode();
        Assert.True(e.IsAsVoucherMode);
    }

    // ================================================================ (2) THE MANDATORY TEST

    /// <summary>
    /// <b>§194J must still fire.</b> A professional-fee purchase of ₹1,23,456.70 to a deductee firm withholds 10% —
    /// ₹12,345.67 raw, rounded to ₹12,346 (nearest rupee, half-up) — leaving ₹1,11,110.70 payable, and posts the
    /// TDS Payable credit leg. Without the Particulars rewire this voucher posted the FULL ₹1,23,456.70 to the
    /// vendor and no TDS leg at all.
    /// </summary>
    [Fact]
    public void Section_194J_still_fires_on_a_professional_fee_purchase_accounting_invoice()
    {
        var k = NewKit("194J Acct Invoice Co");
        var e = OpenAccountingPurchase(k, k.FeesId, k.VendorId, "123456.70");

        // The advisory panel sees the shape — it reads the Particulars lines now, not the empty plain grid.
        Assert.True(e.ShowTdsPanel);
        Assert.Contains("194J(b)", e.TdsSectionText);

        Assert.True(e.Accept());

        var c = k.Vm.Company!;
        var type = c.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Purchase && t.IsActive);
        var posted = c.Vouchers.Single(v => v.TypeId == type.Id);

        // Dr Professional Fees GROSS 1,23,456.70
        var expenseLeg = posted.Lines.Single(l => l.LedgerId == k.FeesId);
        Assert.Equal(DrCr.Debit, expenseLeg.Side);
        Assert.Equal(123456.70m, expenseLeg.Amount.Amount);

        // Cr Vendor NET 1,11,110.70  (1,23,456.70 − 12,346)
        var partyLeg = posted.Lines.Single(l => l.LedgerId == k.VendorId);
        Assert.Equal(DrCr.Credit, partyLeg.Side);
        Assert.Equal(111110.70m, partyLeg.Amount.Amount);

        // …carrying the withholding detail, assessed on the GST-exclusive base.
        Assert.NotNull(partyLeg.Tds);
        Assert.Equal("194J(b)", partyLeg.Tds!.SectionCode);
        Assert.Equal(1000, partyLeg.Tds.RateBasisPoints);
        Assert.Equal(123456.70m, partyLeg.Tds.AssessableValue.Amount);
        Assert.Equal(12346m, partyLeg.Tds.TdsAmount.Amount);

        // Cr TDS Payable 12,346 — the leg whose absence was the money defect.
        var tdsLeg = posted.Lines.Single(l =>
            l.LedgerId != k.VendorId && l.LedgerId != k.FeesId && l.Side == DrCr.Credit);
        Assert.Equal(12346m, tdsLeg.Amount.Amount);

        // And the voucher balances: 1,23,456.70 Dr == 1,11,110.70 + 12,346 Cr.
        Assert.Equal(123456.70m, posted.TotalDebit.Amount);
        Assert.Equal(123456.70m, posted.TotalCredit.Amount);
    }

    // ================================================================ (3) control: non-TDS expense

    /// <summary>
    /// A NON-TDS expense to the same deductee vendor must withhold nothing — applicability is driven by the
    /// EXPENSE ledger's <c>TdsApplicable</c> flag, never by the party. Two legs, no TDS leg, no detail.
    /// </summary>
    [Fact]
    public void A_non_tds_expense_to_a_deductee_vendor_withholds_nothing()
    {
        var k = NewKit("Non TDS Expense Co");
        var e = OpenAccountingPurchase(k, k.NonTdsExpenseId, k.VendorId, "98765.43");

        Assert.False(e.ShowTdsPanel);
        Assert.True(e.Accept());

        var c = k.Vm.Company!;
        var type = c.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Purchase && t.IsActive);
        var posted = c.Vouchers.Single(v => v.TypeId == type.Id);

        Assert.Equal(2, posted.Lines.Count);
        Assert.Equal(98765.43m, posted.Lines.Single(l => l.LedgerId == k.VendorId).Amount.Amount);
        Assert.All(posted.Lines, l => Assert.Null(l.Tds));
    }

    // ================================================================ (4) control: non-deductee party

    /// <summary>
    /// A §194J expense billed by a party carrying NO <c>DeducteeType</c> is not a deductee at all — nothing is
    /// withheld, and the vendor is credited in full.
    /// </summary>
    [Fact]
    public void A_professional_fee_from_a_non_deductee_party_withholds_nothing()
    {
        var k = NewKit("Non Deductee Co");
        var e = OpenAccountingPurchase(k, k.FeesId, k.PlainVendorId, "77777.77");

        Assert.False(e.ShowTdsPanel);
        Assert.True(e.Accept());

        var c = k.Vm.Company!;
        var type = c.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Purchase && t.IsActive);
        var posted = c.Vouchers.Single(v => v.TypeId == type.Id);
        Assert.Equal(77777.77m, posted.Lines.Single(l => l.LedgerId == k.PlainVendorId).Amount.Amount);
        Assert.All(posted.Lines, l => Assert.Null(l.Tds));
    }

    // ================================================================ (5) the operator can decline

    /// <summary>
    /// The "Not Applicable" sentinel must work here exactly as on the plain grid — the screen cannot know every
    /// reason a §194J-looking fee is really outside the section, so declining must post the full gross.
    /// </summary>
    [Fact]
    public void Declining_tds_posts_the_full_gross_to_the_vendor()
    {
        var k = NewKit("Decline TDS Co");
        var e = OpenAccountingPurchase(k, k.FeesId, k.VendorId, "123456.70");
        Assert.True(e.ShowTdsPanel);

        e.SelectedTdsNature = VoucherEntryViewModel.TdsNotApplicable;
        Assert.True(e.Accept());

        var c = k.Vm.Company!;
        var type = c.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Purchase && t.IsActive);
        var posted = c.Vouchers.Single(v => v.TypeId == type.Id);
        Assert.Equal(2, posted.Lines.Count);
        Assert.Equal(123456.70m, posted.Lines.Single(l => l.LedgerId == k.VendorId).Amount.Amount);
    }

    // ================================================================ (6) below threshold

    /// <summary>
    /// §194J(b)'s cumulative threshold is ₹50,000. A ₹4,321.09 fee is below it, so nothing is withheld — but the
    /// (TDS 0) detail must still ride the party leg so the FY cumulative and the "TDS Not Deducted" projection
    /// still count the assessment.
    /// </summary>
    [Fact]
    public void A_below_threshold_fee_withholds_nothing_but_still_records_the_assessment()
    {
        var k = NewKit("Below Threshold Co");
        var e = OpenAccountingPurchase(k, k.FeesId, k.VendorId, "4321.09");
        Assert.True(e.Accept());

        var c = k.Vm.Company!;
        var type = c.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Purchase && t.IsActive);
        var posted = c.Vouchers.Single(v => v.TypeId == type.Id);

        var partyLeg = posted.Lines.Single(l => l.LedgerId == k.VendorId);
        Assert.Equal(4321.09m, partyLeg.Amount.Amount);       // full gross — nothing withheld
        Assert.NotNull(partyLeg.Tds);
        Assert.Equal(0m, partyLeg.Tds!.TdsAmount.Amount);     // …but the assessment is recorded
        Assert.Equal(2, posted.Lines.Count);                  // no TDS Payable leg
    }

    // ================================================================ (7) Sales accounting invoice untouched

    /// <summary>
    /// ER-13 control. A SALES accounting invoice must be completely unaffected by the purchase-side rewire — no
    /// TDS panel, no carve-out, the party debited in full. (Withholding is a purchaser's obligation.)
    /// </summary>
    [Fact]
    public void A_sales_accounting_invoice_is_unaffected_by_the_purchase_side_rewire()
    {
        var k = NewKit("Sales Unaffected Co");
        var c0 = k.Vm.Company!;
        var income = AddLedger(c0, "Consultancy Income", "Sales Accounts");
        var customer = AddLedger(c0, "Beta Buyers", "Sundry Debtors");

        k.Vm.OpenVoucher(VoucherBaseType.Sales);
        var e = k.Vm.VoucherEntry!;
        e.Mode = VoucherEntryMode.AccountingInvoice;
        e.SelectedParty = e.Parties.Single(p => p.Ledger?.Id == customer.Id);
        var line = e.AccountingInvoiceLines[0];
        line.SelectedLedger = e.AccountingInvoiceLedgers.Single(l => l.Id == income.Id);
        line.AmountText = "56789.01";
        e.RecalculateAccountingInvoice();

        Assert.False(e.ShowTdsPanel);
        Assert.True(e.Accept());

        var type = k.Vm.Company!.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Sales && t.IsActive);
        var posted = k.Vm.Company!.Vouchers.Single(v => v.TypeId == type.Id);
        var partyLeg = posted.Lines.Single(l => l.LedgerId == customer.Id);
        Assert.Equal(DrCr.Debit, partyLeg.Side);
        Assert.Equal(56789.01m, partyLeg.Amount.Amount);
        Assert.Null(partyLeg.Tds);
    }

    // ================================================================ (8) the GST-EXCLUSIVE assessable base

    /// <summary>
    /// <b>CBDT Circular 23/2017 — TDS is assessed on the value EXCLUDING GST.</b> Every other fixture in this file
    /// runs on a GST-disabled company, where the taxable total and the party total are numerically identical, so the
    /// rule can never be observed: mutating <c>AssessableExGst</c>'s accounting branch from
    /// <c>AccountingInvoiceTaxable()</c> to <c>AccountingInvoicePartyAmount()</c> — assessing on the GST-INCLUSIVE
    /// invoice — left all seven green. This test is the one that kills that mutation.
    /// <para>Fixture: 18% intra-state professional fee of ₹87,654.32 (CGST ₹7,888.89 + SGST ₹7,888.89, gross
    /// ₹1,03,432.10). §194J(b) @10% on the EX-GST ₹87,654.32 = ₹8,765.432 → ₹8,765 (nearest rupee, half-up), leaving
    /// ₹94,667.10 payable. On the GST-INCLUSIVE base it would be ₹10,343 — ₹1,578 over-deducted from the consultant on
    /// this single invoice, and Form 26Q mis-stated.</para>
    /// </summary>
    [Fact]
    public void Section_194J_assesses_the_gst_exclusive_base_on_a_gst_bearing_purchase_accounting_invoice()
    {
        var k = NewKit("194J Ex-GST Co");
        var c = k.Vm.Company!;

        // Turn the same company into a Regular-GST Maharashtra company and make the fee ledger 18% forward charge.
        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = "27",
            Gstin = "27AAPFU0939F1ZV",
            RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = c.FinancialYearStart,
            Periodicity = GstReturnPeriodicity.Monthly,
        });
        c.FindLedger(k.FeesId)!.SalesPurchaseGst = new StockItemGstDetails
        {
            Taxability = GstTaxability.Taxable,
            RateBasisPoints = 1800,
            SupplyType = GstSupplyType.Services,
        };
        c.FindLedger(k.VendorId)!.PartyGst = new PartyGstDetails
        {
            RegistrationType = GstRegistrationType.Regular,
            Gstin = "27AAACC1206D1ZM",
            StateCode = "27", // same as home ⇒ intra-state CGST+SGST
        };

        var e = OpenAccountingPurchase(k, k.FeesId, k.VendorId, "87654.32");

        // What the screen shows is what posts (ER-4): tax on the ex-GST value, net payable after the ex-GST TDS.
        Assert.Equal("7,888.89", e.GstCgstText);
        Assert.Equal("7,888.89", e.GstSgstText);
        Assert.Equal("8,765.00", e.TdsAmountText);
        Assert.Equal("94,667.10", e.TdsNetPayableText);

        Assert.True(e.Accept());

        var type = c.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Purchase && t.IsActive);
        var posted = c.Vouchers.Single(v => v.TypeId == type.Id);

        var partyLeg = posted.Lines.Single(l => l.LedgerId == k.VendorId);
        Assert.Equal(DrCr.Credit, partyLeg.Side);
        Assert.Equal(94667.10m, partyLeg.Amount.Amount);           // 1,03,432.10 − 8,765

        Assert.NotNull(partyLeg.Tds);
        Assert.Equal("194J(b)", partyLeg.Tds!.SectionCode);
        Assert.Equal(1000, partyLeg.Tds.RateBasisPoints);

        // THE assertion the suite was missing: assessed on the GST-EXCLUSIVE base, not the 1,03,432.10 gross.
        Assert.Equal(87654.32m, partyLeg.Tds.AssessableValue.Amount);
        Assert.NotEqual(103432.10m, partyLeg.Tds.AssessableValue.Amount);
        Assert.Equal(8765m, partyLeg.Tds.TdsAmount.Amount);        // not 10,343

        // …and the whole voucher still foots: Dr 87,654.32 + 7,888.89 + 7,888.89 == Cr 94,667.10 + 8,765.
        Assert.Equal(103432.10m, posted.TotalDebit.Amount);
        Assert.Equal(103432.10m, posted.TotalCredit.Amount);
    }

    // ================================================================ (9) bill-wise on a TDS deductee (rev2-1 MEDIUM)

    /// <summary>
    /// <b>The screen must validate what Accept validates.</b> On a supplier that is BOTH bill-by-bill and a §194J
    /// deductee the invoice panel used to demand the GROSS while Accept demanded the TDS-carved NET, so a split that
    /// read "fully allocated" on screen was refused at post time against a figure never displayed.
    /// <para>Fee ₹87,654.32, §194J(b) @10% ⇒ ₹8,765 withheld ⇒ NET ₹78,889.32. The panel must open on ₹78,889.32,
    /// the auto-seeded row must carry ₹78,889.32, a split of the SHOWN net must post, and a split of the GROSS must be
    /// refused ON SCREEN (not only at Accept).</para>
    /// </summary>
    [Fact]
    public void The_bill_wise_panel_on_a_deductee_supplier_targets_the_tds_carved_net()
    {
        var k = NewKit("BillWise Deductee Co");
        var c = k.Vm.Company!;
        c.FindLedger(k.VendorId)!.MaintainBillByBill = true;

        var e = OpenAccountingPurchase(k, k.FeesId, k.VendorId, "87654.32");

        // The panel targets the NET the party will actually be owed — the figure Accept reconciles against.
        Assert.True(e.ShowInvoiceBillWise);
        Assert.Equal(78889.32m, e.InvoicePartyTotal);
        Assert.Equal(78889.32m, e.InvoiceBillAllocations.Single().ParsedAmount);
        Assert.Contains("78,889.32", e.InvoiceBillSummary);
        Assert.DoesNotContain("87,654.32", e.InvoiceBillSummary);

        // A split of the GROSS is refused ON SCREEN, before Accept — it was previously reported as fully allocated.
        e.InvoiceBillAllocations[0].Name = "BILL-A";
        e.InvoiceBillAllocations[0].AmountText = "40000.00";
        var second = e.AddInvoiceBillAllocation(BillRefType.NewRef);
        second.Name = "BILL-B";
        second.AmountText = "47654.32";   // 40,000.00 + 47,654.32 = the GROSS 87,654.32
        Assert.False(e.InvoiceBillSplitOk);
        Assert.False(e.CanAccept);

        // Retyped against the NET the screen showed all along ⇒ it posts, and the bills are the NET.
        second.AmountText = "38889.32";   // 40,000.00 + 38,889.32 = the NET 78,889.32
        Assert.True(e.InvoiceBillSplitOk);
        Assert.True(e.Accept());

        var type = c.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Purchase && t.IsActive);
        var posted = c.Vouchers.Single(v => v.TypeId == type.Id);
        var partyLeg = posted.Lines.Single(l => l.LedgerId == k.VendorId);
        Assert.Equal(78889.32m, partyLeg.Amount.Amount);

        var bills = Outstandings.OpenBillsFor(c, c.FindLedger(k.VendorId)!, posted.Date);
        Assert.Equal(2, bills.Count);
        Assert.Equal(40000.00m, bills.Single(b => b.Reference == "BILL-A").Pending.Amount);
        Assert.Equal(38889.32m, bills.Single(b => b.Reference == "BILL-B").Pending.Amount);
    }

    /// <summary>
    /// The control for the test above: with the TDS declined the party is owed the full gross again, so the panel must
    /// swing back to ₹87,654.32 — the target tracks the previewed withholding, it is not a one-way carve.
    /// </summary>
    [Fact]
    public void Declining_tds_swings_the_bill_wise_target_back_to_the_gross()
    {
        var k = NewKit("BillWise Decline Co");
        k.Vm.Company!.FindLedger(k.VendorId)!.MaintainBillByBill = true;

        var e = OpenAccountingPurchase(k, k.FeesId, k.VendorId, "87654.32");
        Assert.Equal(78889.32m, e.InvoicePartyTotal);

        e.SelectedTdsNature = VoucherEntryViewModel.TdsNotApplicable;
        Assert.Equal(87654.32m, e.InvoicePartyTotal);
        Assert.Equal(87654.32m, e.InvoiceBillAllocations.Single().ParsedAmount);

        e.InvoiceBillAllocations[0].Name = "FULL-BILL";
        Assert.True(e.InvoiceBillSplitOk);
        Assert.True(e.Accept());

        var c = k.Vm.Company!;
        var type = c.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Purchase && t.IsActive);
        var posted = c.Vouchers.Single(v => v.TypeId == type.Id);
        Assert.Equal(87654.32m, posted.Lines.Single(l => l.LedgerId == k.VendorId).Amount.Amount);
        Assert.Equal(87654.32m,
            Outstandings.OpenBillsFor(c, c.FindLedger(k.VendorId)!, posted.Date).Single().Pending.Amount);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch (IOException) { }
    }
}
