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
/// Coverage for the Phase-9 <b>UI-1 advanced-GST report screens</b> surfaced in the cascade (RQ-17): the ten
/// read-only projections nested under Reports → Statutory Reports → <b>Annual Returns</b> (GSTR-9, GSTR-9C) and
/// <b>GST Returns (Advanced)</b> (Electronic Ledgers, ITC Set-Off, ITC Reversal, GSTR-2B Reconciliation, ITC Gate,
/// QRMP / IFF, GST Amendments, e-Invoice / e-Way Status). Each test opens the screen through the real shell and
/// asserts the screen + its projected content; the gated-off cases assert a Composition dealer never reaches any of
/// them (ER-13 — the groups are surfaced only for a Regular GST company). Drives the real shell view models over a
/// throwaway <c>.db</c> — no UI toolkit.
/// </summary>
public sealed class GstAdvancedReportsUiViewModelTests : IDisposable
{
    private const string GstinMaharashtra = "27AAPFU0939F1ZV"; // state code 27 (home)
    private const string GstinSupplier = "27AAACC1206D1ZM";    // an in-state supplier

    private static readonly DateOnly FyStart = new(2024, 4, 1);
    private static readonly DateOnly PurchaseDate = new(2024, 4, 3);
    private static readonly DateOnly SaleDate = new(2024, 4, 5);

    // The cess-on-RCM fixture lives on FY 2025-26: the 22% ad-valorem car-cess window (HSN 8703) opens at that FY's
    // start and closes at the GST-2.0 cutover, so the RCM inward must be dated inside it.
    private static readonly DateOnly RcmFyStart = new(2025, 4, 1);
    private static readonly DateOnly RcmDate = new(2025, 9, 20);

    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public GstAdvancedReportsUiViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexGstAdvUiTests_" + Guid.NewGuid().ToString("N"));
        _storage = new CompanyStorage(_tempDir);
    }

    // ---------------------------------------------------------------- scaffolding

    private MainWindowViewModel NewSeededCompany(string name)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = name;
        vm.CreateCompany();
        Assert.Equal(Screen.Gateway, vm.CurrentScreen);
        var c = vm.Company!;
        c.FinancialYearStart = FyStart;
        c.BooksBeginFrom = FyStart;
        return vm;
    }

    private static void EnableGst(
        Company c, GstRegistrationType type, GstReturnPeriodicity periodicity = GstReturnPeriodicity.Monthly)
        => new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = "27",
            Gstin = GstinMaharashtra,
            RegistrationType = type,
            CompositionSubType = type == GstRegistrationType.Composition ? CompositionSubType.Trader : null,
            ApplicableFrom = FyStart,
            Periodicity = periodicity,
        });

    private static DomainLedger Add(Company c, string name, string groupName, bool debit)
    {
        var group = c.FindGroupByName(groupName) ?? throw new InvalidOperationException($"No group '{groupName}'.");
        var ledger = new DomainLedger(Guid.NewGuid(), name, group.Id, Money.Zero, openingIsDebit: debit);
        c.AddLedger(ledger);
        return ledger;
    }

    /// <summary>A Regular GST company with one intra purchase (₹5,000 @ 18% ⇒ ITC 450+450) and one intra B2B sale
    /// (₹1,000 @ 18% ⇒ output 90+90) posted — so every advanced projection has real, non-zero figures.</summary>
    private MainWindowViewModel NewRegularGstCompany(
        string name, GstReturnPeriodicity periodicity = GstReturnPeriodicity.Monthly)
    {
        var vm = NewSeededCompany(name);
        var c = vm.Company!;
        EnableGst(c, GstRegistrationType.Regular, periodicity);

        var gst = new GstService(c);
        var ledgers = new LedgerService(c);

        var sales = Add(c, "Sales", "Sales Accounts", false);
        var purchases = Add(c, "Purchases", "Purchase Accounts", true);
        var debtor = Add(c, "Local Debtor", "Sundry Debtors", true);
        debtor.PartyGst = new PartyGstDetails
        { RegistrationType = GstRegistrationType.Regular, Gstin = GstinMaharashtra, StateCode = "27" };
        var supplier = Add(c, "Local Supplier", "Sundry Creditors", false);
        supplier.PartyGst = new PartyGstDetails
        { RegistrationType = GstRegistrationType.Regular, Gstin = GstinSupplier, StateCode = "27" };

        var salesType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id;
        var purchaseType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase).Id;

        // PURCHASE ₹5,000 @ 18% intra ⇒ Input CGST 450 + SGST 450 (the ITC pool the set-off/ledgers project).
        var pTax = gst.ComputeInvoiceTax(
            new[] { new GstService.TaxableLine(Money.FromRupees(5000m), 1800) }, false, GstTaxDirection.Input);
        var pLines = new List<EntryLine>
        {
            new(purchases.Id, Money.FromRupees(5000m), DrCr.Debit),
            new(supplier.Id, Money.FromRupees(5900m), DrCr.Credit),
        };
        pLines.AddRange(pTax.TaxLines);
        ledgers.Post(new Voucher(Guid.NewGuid(), purchaseType, PurchaseDate, pLines, partyId: supplier.Id,
            narration: "SUP-INV-001"));

        // INTRA B2B SALE ₹1,000 @ 18% ⇒ Output CGST 90 + SGST 90.
        var sTax = gst.ComputeInvoiceTax(
            new[] { new GstService.TaxableLine(Money.FromRupees(1000m), 1800) }, false, GstTaxDirection.Output);
        var sLines = new List<EntryLine>
        {
            new(debtor.Id, Money.FromRupees(1180m), DrCr.Debit),
            new(sales.Id, Money.FromRupees(1000m), DrCr.Credit),
        };
        sLines.AddRange(sTax.TaxLines);
        ledgers.Post(new Voucher(Guid.NewGuid(), salesType, SaleDate, sLines, number: 1, partyId: debtor.Id));

        _storage.Save(c);
        vm.ShowGateway();
        return vm;
    }

    /// <summary>Formats a <see cref="Money"/> exactly as the report view models do (Indian format, always 2dp) — so a
    /// test can assert two screens agree on a figure without restating its literal.</summary>
    private static string A(Money m) => IndianFormat.AmountAlways(m);

    /// <summary>
    /// A Regular GST company on FY 2025-26 with one <b>cess-bearing RCM inward supply</b> posted: a ₹1,00,000 §9(4)
    /// promoter capital good (HSN 8703) at 18% RCM intra (₹9,000 CGST + ₹9,000 SGST) that also attracts the 22%
    /// ad-valorem car cess (₹22,000) in the pre-cutover window — the interaction that exposes the RCM cess liability.
    /// </summary>
    private MainWindowViewModel NewRcmCessCompany(string name)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = name;
        vm.CreateCompany();
        var c = vm.Company!;
        c.FinancialYearStart = RcmFyStart;
        c.BooksBeginFrom = RcmFyStart;

        var gst = new GstService(c);
        gst.EnableGst(new GstConfig
        {
            HomeStateCode = "27",
            Gstin = GstinMaharashtra,
            RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = RcmFyStart,
            Periodicity = GstReturnPeriodicity.Monthly,
        });
        gst.SeedAdvancedGst();   // the dated rate history + cess windows + the notified RCM categories

        var good = Add(c, "Imported Capital Good (promoter)", "Indirect Expenses", true);
        good.SalesPurchaseGst = new StockItemGstDetails
        {
            HsnSac = "8703", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800,
            SupplyType = GstSupplyType.Goods, ReverseChargeApplicable = true,
            RcmCategoryId = c.Gst!.RcmCategories.First(x => x.SupplyNature == "Capital-goods").Id,
        };
        var unregistered = Add(c, "Unregistered Dealer", "Sundry Creditors", false);
        unregistered.PartyGst = new PartyGstDetails
        { RegistrationType = GstRegistrationType.Unregistered, StateCode = "27", IsPromoter = false };

        var value = Money.FromRupees(100000m);
        var posting = new RcmService(c).BuildReverseCharge(
            value, null, good, unregistered.PartyGst, RcmDate, RcmService.SupplyKind.Domestic,
            recipientIsPromoter: true, quantity: 1m);
        Assert.True(posting.Applies);

        var lines = new List<EntryLine>
        {
            new(good.Id, value, DrCr.Debit),
            new(unregistered.Id, value, DrCr.Credit),   // the supplier charges ZERO tax
        };
        lines.AddRange(posting.Lines);                  // the balanced RCM pair, additive
        var purchaseType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase).Id;
        new LedgerService(c).Post(new Voucher(Guid.NewGuid(), purchaseType, RcmDate, lines));

        _storage.Save(c);
        vm.ShowGateway();
        return vm;
    }

    /// <summary>Imports a one-line GSTR-2B snapshot for Apr-2024 that exactly matches the seeded purchase (same
    /// supplier GSTIN + doc-no + value/tax) — so the reconciler pairs it into the Matched bucket.</summary>
    private static void ImportMatching2b(Company c)
    {
        var line = new Gstr2bLine(
            Guid.NewGuid(), GstinSupplier, "Local Supplier", Gstr2bDocType.B2b, "SUP-INV-001",
            Gstr2bReconciler.NormaliseDocNo("SUP-INV-001"), PurchaseDate, "27",
            taxableValuePaisa: 500_000, igstPaisa: 0, cgstPaisa: 45_000, sgstPaisa: 45_000, cessPaisa: 0,
            itcAvailable: true, itcUnavailableReason: null, reverseCharge: false);

        c.AddGstr2bSnapshot(new Gstr2bSnapshot(
            Guid.NewGuid(), GstStatementType.Gstr2b, "2024-04", GstinMaharashtra, new DateOnly(2024, 5, 14),
            sourceFileHash: "hash-apr-2024", importedAt: new DateTimeOffset(2024, 5, 14, 0, 0, 0, TimeSpan.Zero),
            summaryIgstPaisa: 0, summaryCgstPaisa: 45_000, summarySgstPaisa: 45_000, summaryCessPaisa: 0,
            lines: new[] { line }));
    }

    // ================================================================ Nav: the two advanced groups

    /// <summary>
    /// ER-13 reachability: a <b>plain</b> Regular GST company — GST on, but no TDS / TCS / Payroll-statutory and not a
    /// Composition dealer — must see the <b>Statutory Reports</b> group on the ROOT Gateway column, because that group
    /// is the only door to the ten advanced-GST screens. Regression-locks the root gate (which omitted
    /// <c>IsRegularGstDealer</c> and so made every UI-1 screen unreachable through the real cascade).
    /// </summary>
    [Fact]
    public void Plain_regular_gst_company_reaches_the_advanced_groups_from_the_root_gateway()
    {
        var vm = NewRegularGstCompany("Root Gate Co");
        var c = vm.Company!;
        // A plain GST company: nothing else is switched on — only the GST F11 feature.
        Assert.False(c.TdsEnabled);
        Assert.False(c.TcsEnabled);
        Assert.False(c.PayrollStatutoryEnabled);

        // The ROOT column must offer the door.
        vm.ShowGateway();
        var root = vm.Columns[0].Items.Where(i => i.IsSelectable).Select(i => i.Label).ToList();
        Assert.Contains("Statutory Reports", root);

        // …and drilling through it must reach both advanced groups.
        vm.ShowStatutoryReportsMenu();
        Assert.Equal(GatewayMenu.StatutoryReports, vm.CurrentGatewayMenu);
        var statutory = vm.Columns[^1].Items.Where(i => i.IsSelectable).Select(i => i.Label).ToList();
        Assert.Contains("Annual Returns", statutory);
        Assert.Contains("GST Returns (Advanced)", statutory);
    }

    /// <summary>
    /// The <b>Statutory Reports</b> column itself must carry the two advanced-GST sub-groups for a Regular company.
    /// Asserted on the column (not merely on the menu the opener forces open) so that deleting the group-adding block
    /// turns this test RED — the blind spot that let the root-gate defect ship.
    /// </summary>
    [Fact]
    public void Statutory_reports_column_surfaces_the_two_advanced_gst_groups()
    {
        var vm = NewRegularGstCompany("Statutory Column Co");

        vm.ShowStatutoryReportsMenu();
        var labels = vm.Columns[^1].Items.Select(i => i.Label).ToList();
        Assert.Contains("Annual Returns", labels);
        Assert.Contains("GST Returns (Advanced)", labels);
    }

    [Fact]
    public void Regular_gst_company_surfaces_the_annual_and_advanced_return_groups()
    {
        var vm = NewRegularGstCompany("Adv Nav Co");

        vm.ShowAnnualReturnsMenu();
        Assert.Equal(GatewayMenu.AnnualReturns, vm.CurrentGatewayMenu);
        var annual = vm.Columns[^1].Items.Where(i => i.IsSelectable).Select(i => i.Label).ToList();
        Assert.Contains("GSTR-9", annual);
        Assert.Contains("GSTR-9C", annual);

        vm.ShowGstAdvancedReturnsMenu();
        Assert.Equal(GatewayMenu.GstAdvancedReturns, vm.CurrentGatewayMenu);
        var advanced = vm.Columns[^1].Items.Where(i => i.IsSelectable).Select(i => i.Label).ToList();
        Assert.Equal(
            new[]
            {
                "Electronic Ledgers", "ITC Set-Off", "ITC Reversal", "GSTR-2B Reconciliation",
                "ITC Gate", "QRMP / IFF", "GST Amendments", "e-Invoice / e-Way Status",
            },
            advanced);
    }

    [Fact]
    public void Composition_dealer_never_surfaces_the_advanced_gst_groups_or_reports()
    {
        var vm = NewSeededCompany("Adv Gated Co");
        EnableGst(vm.Company!, GstRegistrationType.Composition);
        _storage.Save(vm.Company!);
        vm.ShowGateway();

        // Neither group opens for a Composition dealer (ER-13).
        vm.ShowAnnualReturnsMenu();
        Assert.NotEqual(GatewayMenu.AnnualReturns, vm.CurrentGatewayMenu);
        vm.ShowGstAdvancedReturnsMenu();
        Assert.NotEqual(GatewayMenu.GstAdvancedReturns, vm.CurrentGatewayMenu);

        // And every opener is a no-op — the page stays null and the screen never changes.
        vm.OpenGstr9Report(); Assert.Null(vm.Gstr9Report);
        vm.OpenGstr9cReport(); Assert.Null(vm.Gstr9cReport);
        vm.OpenElectronicLedgersReport(); Assert.Null(vm.ElectronicLedgersReport);
        vm.OpenItcSetOffReport(); Assert.Null(vm.ItcSetOffReport);
        vm.OpenItcReversalReport(); Assert.Null(vm.ItcReversalReport);
        vm.OpenGstr2bReconReport(); Assert.Null(vm.Gstr2bReconReport);
        vm.OpenItcGateReport(); Assert.Null(vm.ItcGateReport);
        vm.OpenQrmpReport(); Assert.Null(vm.QrmpReport);
        vm.OpenGstAmendmentsReport(); Assert.Null(vm.GstAmendmentsReport);
        vm.OpenEInvoiceEWayStatusReport(); Assert.Null(vm.EInvoiceEWayStatusReport);
        Assert.Equal(Screen.Gateway, vm.CurrentScreen);
    }

    /// <summary>
    /// The other half of the ER-13 gate: an <b>ordinary company that never enabled GST</b> sees neither advanced group
    /// (nor the Statutory Reports door they hang from), and every one of the ten openers is a no-op.
    /// </summary>
    [Fact]
    public void Gst_off_company_never_surfaces_the_advanced_gst_groups_or_reports()
    {
        var vm = NewSeededCompany("No Gst Co");   // GST never enabled; no TDS / TCS / Payroll either
        _storage.Save(vm.Company!);
        vm.ShowGateway();

        // The root offers no Statutory Reports door at all (nothing statutory is switched on).
        var root = vm.Columns[0].Items.Where(i => i.IsSelectable).Select(i => i.Label).ToList();
        Assert.DoesNotContain("Statutory Reports", root);

        // Neither group opens.
        vm.ShowAnnualReturnsMenu();
        Assert.NotEqual(GatewayMenu.AnnualReturns, vm.CurrentGatewayMenu);
        vm.ShowGstAdvancedReturnsMenu();
        Assert.NotEqual(GatewayMenu.GstAdvancedReturns, vm.CurrentGatewayMenu);

        // And every opener is a no-op — the page stays null and the screen never changes.
        vm.OpenGstr9Report(); Assert.Null(vm.Gstr9Report);
        vm.OpenGstr9cReport(); Assert.Null(vm.Gstr9cReport);
        vm.OpenElectronicLedgersReport(); Assert.Null(vm.ElectronicLedgersReport);
        vm.OpenItcSetOffReport(); Assert.Null(vm.ItcSetOffReport);
        vm.OpenItcReversalReport(); Assert.Null(vm.ItcReversalReport);
        vm.OpenGstr2bReconReport(); Assert.Null(vm.Gstr2bReconReport);
        vm.OpenItcGateReport(); Assert.Null(vm.ItcGateReport);
        vm.OpenQrmpReport(); Assert.Null(vm.QrmpReport);
        vm.OpenGstAmendmentsReport(); Assert.Null(vm.GstAmendmentsReport);
        vm.OpenEInvoiceEWayStatusReport(); Assert.Null(vm.EInvoiceEWayStatusReport);
        Assert.Equal(Screen.Gateway, vm.CurrentScreen);
    }

    // ================================================================ Screen 1: GSTR-9

    [Fact]
    public void Gstr9_opens_and_projects_the_annual_return_tables()
    {
        var vm = NewRegularGstCompany("Gstr9 Co");
        vm.OpenGstr9Report();

        Assert.Equal(Screen.Gstr9Report, vm.CurrentScreen);
        var page = vm.Gstr9Report!;
        Assert.True(page.Return.Applicable);
        Assert.Equal(3, page.FinancialYears.Count);

        // Table 4 = the seeded intra sale's output tax (90 + 90); Table 6 = the purchase's ITC (450 + 450).
        Assert.Equal(90m, page.Return.Table4Cgst.Amount);
        Assert.Equal(90m, page.Return.Table4Sgst.Amount);
        Assert.Equal(180m, page.Return.Table4TotalTax.Amount);
        Assert.Equal(900m, page.Return.Table6ItcAvailed.Amount);
        Assert.Equal(1000m, page.Return.Table5NTurnover.Amount);

        // The flattened text mirrors the engine (Indian-format, always 2dp).
        Assert.Equal("90.00", page.Table4CgstText);
        Assert.Equal("900.00", page.Table6ItcAvailedText);
        Assert.Contains("Tax payable", page.StatusText);
        Assert.Contains(GstinMaharashtra, page.GstinText);
    }

    // ================================================================ Screen 2: GSTR-9C

    [Fact]
    public void Gstr9c_opens_and_shows_the_unreconciled_difference_lines()
    {
        var vm = NewRegularGstCompany("Gstr9c Co");
        vm.OpenGstr9cReport();

        Assert.Equal(Screen.Gstr9cReport, vm.CurrentScreen);
        var page = vm.Gstr9cReport!;
        Assert.True(page.Statement.Applicable);

        // The books and the return agree on turnover (₹1,000 P&L revenue vs GSTR-9 5N) ⇒ 5R reconciles to zero,
        // and the tax/ITC anchors are the FY gross accrual legs (nothing discharged yet).
        Assert.Equal(1000m, page.Statement.Table5ABooksTurnover.Amount);
        Assert.Equal(1000m, page.Statement.Table5QReturnTurnover.Amount);
        Assert.Equal(0m, page.Statement.Table5RUnreconciledTurnover.Amount);
        Assert.Equal(180m, page.Statement.Table9TaxPerBooks.Amount);
        Assert.Equal(900m, page.Statement.Table12ABooksItc.Amount);
        Assert.Equal("0.00", page.UnreconciledTurnoverText);
        Assert.Contains("Unreconciled", page.StatusText);
    }

    // ================================================================ Screen 3: Electronic Ledgers

    [Fact]
    public void Electronic_ledgers_opens_and_projects_the_three_pools()
    {
        var vm = NewRegularGstCompany("ELedgers Co");
        vm.OpenElectronicLedgersReport();

        Assert.Equal(Screen.ElectronicLedgersReport, vm.CurrentScreen);
        var page = vm.ElectronicLedgersReport!;
        Assert.Equal(4, page.CreditRows.Count);      // CGST / SGST / IGST / Cess
        Assert.Equal(4, page.LiabilityRows.Count);

        // Credit ledger = the purchase's ITC (450 each); liability = the sale's output tax (90 each).
        Assert.Equal(450m, page.View.CreditCgst.Amount);
        Assert.Equal(900m, page.View.TotalCredit.Amount);
        Assert.Equal(90m, page.View.LiabilityCgst.Amount);
        Assert.Equal("450.00", page.CreditRows[0].Closing);
        Assert.Equal("CGST", page.CreditRows[0].Head);
        Assert.Equal("900.00", page.TotalCreditText);
        Assert.Contains("Credit available", page.StatusText);

        // No PMT-06 challan deposited ⇒ no cash cells, and the flag drives the grid's empty-state note (so the
        // Cash-ledger section never renders a bare header over nothing).
        Assert.Empty(page.CashRows);
        Assert.False(page.HasCashCells);
    }

    // ================================================================ Screen 4: ITC Set-Off (view)

    [Fact]
    public void Itc_setoff_opens_and_projects_the_rule88a_allocation_without_posting()
    {
        var vm = NewRegularGstCompany("SetOff Co");
        var voucherCountBefore = vm.Company!.Vouchers.Count;

        vm.OpenItcSetOffReport();
        Assert.Equal(Screen.ItcSetOffReport, vm.CurrentScreen);
        var page = vm.ItcSetOffReport!;

        // Liability 90+90 fully covered by the 450+450 own-head credit ⇒ two lines, zero residual cash.
        Assert.Equal(2, page.Lines.Count);
        Assert.Contains(page.Lines, l => l.CreditHead == "CGST" && l.LiabilityHead == "CGST" && l.Amount == "90.00");
        Assert.Contains(page.Lines, l => l.CreditHead == "SGST/UTGST" && l.LiabilityHead == "SGST/UTGST" && l.Amount == "90.00");
        Assert.Equal(0, page.Allocation.TotalCash);
        Assert.Equal(18_000, page.Allocation.TotalCreditUtilised);   // ₹180 in paisa
        Assert.Equal("360.00", page.ClosingCgstText);                // 450 − 90 carried forward
        Assert.Equal("0.00", page.TotalCashText);

        // Display-only: opening the projection posts NOTHING (no set-off journal appears).
        Assert.Equal(voucherCountBefore, vm.Company!.Vouchers.Count);
        Assert.Empty(vm.Company!.GstSetoffLines);
    }

    /// <summary>
    /// The set-off's credit side must be the <b>real Input-ledger pool</b> — net of Rule-42/43/37/37A reversals — not
    /// the GROSS Table 4(A) availment. The reversals post on a Journal-base stat-adjustment that
    /// <c>Gstr3b.ReadSide(Input)</c> deliberately excludes, so sourcing the credit from <c>g3b.Itc*</c> counted
    /// already-reversed ITC as still-available and told the taxpayer "cash payable 0.00" when cash was owed — while the
    /// Electronic Ledgers screen (which reads the Input-ledger closing balance) disagreed. Locks the two screens to
    /// ONE credit pool, by construction.
    /// </summary>
    [Fact]
    public void Itc_setoff_credit_pool_is_net_of_reversals_and_agrees_with_the_electronic_ledgers()
    {
        var vm = NewRegularGstCompany("SetOff Reversal Co");
        var c = vm.Company!;

        // Reverse ₹400 of each of the ₹450 CGST/SGST ITC pools under Rule 42 (common-credit apportionment) — leaving
        // ₹50 per head against a ₹90-per-head output liability, so ₹40 per head MUST fall to cash.
        var reversal = new GstReversalService(c).PostReversal(
            ItcReversalRule.Rule42, "2024-04",
            new GstReversalService.ReversalAmount(CgstPaisa: 40_000, SgstPaisa: 40_000, IgstPaisa: 0, CessPaisa: 0),
            new DateOnly(2024, 4, 30));
        Assert.NotNull(reversal);
        _storage.Save(c);

        // The Electronic Ledgers screen: the reversal reduced the Input pools to ₹50 per head.
        vm.OpenElectronicLedgersReport();
        var ledgers = vm.ElectronicLedgersReport!;
        Assert.Equal(50m, ledgers.View.CreditCgst.Amount);
        Assert.Equal(50m, ledgers.View.CreditSgst.Amount);
        Assert.Equal(400m, ledgers.View.CreditReversedCgst.Amount);

        vm.OpenItcSetOffReport();
        var page = vm.ItcSetOffReport!;

        // (1) The two screens agree on the credit pool — the set-off draws on the SAME Input-ledger pool.
        Assert.Equal("50.00", page.CreditCgstText);
        Assert.Equal("50.00", page.CreditSgstText);
        Assert.Equal(A(ledgers.View.CreditCgst), page.CreditCgstText);
        Assert.Equal(A(ledgers.View.CreditSgst), page.CreditSgstText);
        Assert.Equal(A(ledgers.View.CreditIgst), page.CreditIgstText);
        Assert.Equal(A(ledgers.View.CreditCess), page.CreditCessText);

        // (2) The ₹90-per-head liability now outruns the ₹50-per-head credit ⇒ ₹40 per head payable in CASH.
        Assert.Equal(4_000, page.Allocation.CashCgst);        // ₹40 in paisa
        Assert.Equal(4_000, page.Allocation.CashSgst);
        Assert.Equal(8_000, page.Allocation.TotalCash);       // ₹80 in paisa
        Assert.True(page.Allocation.TotalCash > 0);
        Assert.Equal("80.00", page.TotalCashText);
        Assert.Equal("100.00", page.TotalCreditUtilisedText); // only the ₹50+₹50 that survived the reversal
        Assert.Equal("0.00", page.ClosingCgstText);           // the pool is fully consumed — nothing carried forward

        // Still strictly display-only.
        Assert.Empty(c.GstSetoffLines);
    }

    /// <summary>
    /// An RCM inward supply that attracts <b>cess</b> must show its cess in the cash-only RCM liability.
    /// <c>Gstr3b.TotalRcmOutward</c> excludes <c>RcmOutwardCess</c>, so the screen credited the matching RCM cess ITC
    /// while the cash-only cess liability it mirrors stayed invisible. Cess stays ring-fenced (ER-2): the RCM cash
    /// liability never enters the credit-utilisation steps.
    /// </summary>
    [Fact]
    public void Itc_setoff_rcm_cash_liability_includes_the_ring_fenced_cess()
    {
        var vm = NewRcmCessCompany("SetOff Rcm Cess Co");
        var page3b = Gstr3b.Build(vm.Company!, RcmFyStart, RcmFyStart.AddYears(1).AddDays(-1));
        Assert.Equal(22_000m, page3b.RcmOutwardCess.Amount);        // 22% ad-valorem cess on the ₹1,00,000 good
        Assert.Equal(18_000m, page3b.TotalRcmOutward.Amount);       // 18% CGST+SGST — and NOT the cess

        vm.OpenItcSetOffReport();
        var page = vm.ItcSetOffReport!;

        // The RCM cash liability carries the cess: ₹18,000 (CGST+SGST) + ₹22,000 (cess) = ₹40,000.
        Assert.Equal("40,000.00", page.LiabRcmCashText);
        Assert.Equal(40_00_000, page.Allocation.CashRcm);           // ₹40,000 in paisa — cash-only (ER-3)
        Assert.Contains("40,000.00", page.TotalCashText);

        // Ring-fence intact: the cess ITC is still credit (carried forward), never netted against the RCM cash.
        Assert.Equal("22,000.00", page.CreditCessText);
        Assert.Equal("22,000.00", page.ClosingCessText);
    }

    // ================================================================ Screen 5: ITC Reversal (view)

    [Fact]
    public void Itc_reversal_opens_and_shows_a_clean_empty_state_without_a_2b()
    {
        var vm = NewRegularGstCompany("Reversal Co");
        vm.OpenItcReversalReport();

        Assert.Equal(Screen.ItcReversalReport, vm.CurrentScreen);
        var page = vm.ItcReversalReport!;
        Assert.False(page.HasSnapshot);
        Assert.Empty(page.Candidates);
        Assert.Equal("0.00", page.BalanceTotalText);   // nothing reversed yet ⇒ a zero ECRS balance
        Assert.Contains("No GSTR-2B imported", page.Message);
    }

    [Fact]
    public void Itc_reversal_surfaces_the_candidates_from_an_imported_2b()
    {
        var vm = NewRegularGstCompany("Reversal 2B Co");
        ImportMatching2b(vm.Company!);

        vm.OpenItcReversalReport();
        var page = vm.ItcReversalReport!;
        Assert.True(page.HasSnapshot);
        Assert.Null(page.Message);
        // The 2B matches the purchase, so its ITC is claimable — no §16(2)(aa) candidate is raised.
        Assert.Empty(page.Candidates);
        Assert.Contains("ECRS", page.StatusText);
    }

    // ================================================================ Screen 6: GSTR-2B Reconciliation

    [Fact]
    public void Gstr2b_recon_opens_with_a_clean_empty_state_when_no_2b_is_imported()
    {
        var vm = NewRegularGstCompany("Recon Empty Co");
        vm.OpenGstr2bReconReport();

        Assert.Equal(Screen.Gstr2bReconReport, vm.CurrentScreen);
        var page = vm.Gstr2bReconReport!;
        Assert.False(page.HasSnapshot);
        Assert.Empty(page.Snapshots);
        Assert.Empty(page.Rows);
        Assert.Equal(-1, page.HighlightedIndex);
        Assert.Contains("No GSTR-2B imported", page.Message);
    }

    [Fact]
    public void Gstr2b_recon_matches_the_imported_2b_to_the_booked_purchase_and_moves_the_highlight()
    {
        var vm = NewRegularGstCompany("Recon Co");
        ImportMatching2b(vm.Company!);
        vm.OpenGstr2bReconReport();

        Assert.Equal(Screen.Gstr2bReconReport, vm.CurrentScreen);
        var page = vm.Gstr2bReconReport!;
        Assert.True(page.HasSnapshot);
        Assert.Single(page.Snapshots);

        // The 2B line pairs cleanly with the booked purchase (same GSTIN + doc-no + value) ⇒ one Matched row.
        Assert.Equal(1, page.MatchedCount);
        Assert.Equal(0, page.PartialCount);
        Assert.Equal(0, page.InPortalOnlyCount);
        Assert.Equal(0, page.InBooksOnlyCount);
        Assert.Single(page.Rows);
        Assert.Equal("Matched", page.Rows[0].Bucket);
        Assert.Equal(GstinSupplier, page.Rows[0].Supplier);
        Assert.Equal("5,000.00", page.Rows[0].TaxableValue);
        Assert.True(page.Rows[0].IsClean);

        // The row list is highlightable and the shell routes the arrows to it.
        Assert.Equal(0, page.HighlightedIndex);
        Assert.True(page.Rows[0].IsHighlighted);
        Assert.True(vm.IsGstr2bReconScreen);
        vm.MoveDown();
        Assert.Equal(0, page.HighlightedIndex);   // single row ⇒ wraps back onto itself
    }

    // ================================================================ Screen 7: ITC Gate

    [Fact]
    public void Itc_gate_opens_and_compares_books_2b_and_3b()
    {
        var vm = NewRegularGstCompany("Gate Co");
        ImportMatching2b(vm.Company!);
        vm.OpenItcGateReport();

        Assert.Equal(Screen.ItcGateReport, vm.CurrentScreen);
        var page = vm.ItcGateReport!;
        Assert.True(page.HasSnapshot);
        Assert.Equal(7, page.Rows.Count);   // books / claimable / not-in-portal / blocked / ineligible / 2B / 3B

        // The purchase's ITC is eligible AND reflected in 2B ⇒ fully claimable, nothing blocked or stranded.
        Assert.Equal(900m, page.View!.BooksEligibleTotal.Amount);
        Assert.Equal(900m, page.View.ClaimableTotal.Amount);
        Assert.Equal(0m, page.View.NotInPortalTotal.Amount);
        Assert.Equal(0m, page.View.BlockedTotal.Amount);
        Assert.Equal(900m, page.View.Portal2b.Total.Amount);
        Assert.Empty(page.Candidates);
        Assert.Contains("Books eligible", page.StatusText);
    }

    [Fact]
    public void Itc_gate_shows_a_clean_empty_state_without_a_2b()
    {
        var vm = NewRegularGstCompany("Gate Empty Co");
        vm.OpenItcGateReport();

        var page = vm.ItcGateReport!;
        Assert.False(page.HasSnapshot);
        Assert.Null(page.View);
        Assert.Empty(page.Rows);
        Assert.Contains("No GSTR-2B imported", page.Message);
    }

    // ================================================================ Screen 8: QRMP / IFF

    [Fact]
    public void Qrmp_projects_four_quarters_of_iff_and_pmt06_for_a_quarterly_filer()
    {
        var vm = NewRegularGstCompany("Qrmp Co", GstReturnPeriodicity.Quarterly);
        vm.OpenQrmpReport();

        Assert.Equal(Screen.QrmpReport, vm.CurrentScreen);
        var page = vm.QrmpReport!;
        Assert.True(page.Applicable);
        Assert.Equal(4, page.Projection.Quarters.Count);
        Assert.Equal(8, page.IffRows.Count);      // M1 + M2 across four quarters
        Assert.Equal(8, page.Pmt06Rows.Count);

        // Q1-M1 (Apr-2024) carries the seeded B2B sale; it is well within the ₹50 lakh IFF cap.
        var q1m1 = page.IffRows[0];
        Assert.Equal("Apr 2024", q1m1.Month);
        Assert.Equal("1", q1m1.Invoices);
        Assert.Equal("1,000.00", q1m1.TaxableValue);
        Assert.False(q1m1.ExceedsCap);
        Assert.True(q1m1.WithinCap);
        Assert.Equal("Within cap", q1m1.Cap);
    }

    [Fact]
    public void Qrmp_is_not_applicable_for_a_monthly_filer()
    {
        var vm = NewRegularGstCompany("Qrmp Monthly Co");   // seeded Monthly
        vm.OpenQrmpReport();

        Assert.Equal(Screen.QrmpReport, vm.CurrentScreen);
        var page = vm.QrmpReport!;
        Assert.False(page.Applicable);
        Assert.Empty(page.IffRows);
        Assert.Empty(page.Pmt06Rows);
        Assert.Contains("Not applicable", page.StatusText);
    }

    // ================================================================ Screen 9: GST Amendments

    [Fact]
    public void Gst_amendments_opens_and_shows_the_3b_correction_advisory()
    {
        var vm = NewRegularGstCompany("Amend Co");
        vm.OpenGstAmendmentsReport();

        Assert.Equal(Screen.GstAmendmentsReport, vm.CurrentScreen);
        var page = vm.GstAmendmentsReport!;
        Assert.True(page.Applicable);

        // Nothing was re-stated from a prior period, so both amendment tables are empty and no 3B correction is due.
        Assert.Empty(page.Table9A);
        Assert.Empty(page.Table9C);
        Assert.False(page.RequiresCorrection);
        Assert.Equal("0", page.CorrectionCountText);
        // The advisory mechanism note is always surfaced (GSTR-1A / subsequent period / Jul-2025 hard-lock).
        Assert.Contains("GSTR-1A", page.MechanismText);
        Assert.Contains("Table 9A", page.StatusText);
    }

    // ================================================================ Screen 10: e-Invoice / e-Way status

    [Fact]
    public void Einvoice_eway_status_opens_and_lists_the_artefacts()
    {
        var vm = NewRegularGstCompany("EInv Co");
        var c = vm.Company!;

        // Seed one Generated e-invoice + one Pending e-Way Bill (the portal-issued values are copied, never derived).
        var sale = c.Vouchers.First(v => v.Number == 1);
        c.AddEInvoiceRecord(EInvoiceRecord.Rehydrate(
            Guid.NewGuid(), sale.Id, "INV-1", EInvoiceStatus.Generated,
            irn: new string('a', 64), ackNo: "112400001", ackDate: SaleDate, signedQr: "signed-qr-blob",
            signedJson: null, cancelledOn: null, cancelReasonCode: null));
        c.AddEWayBillRecord(new EWayBillRecord(
            Guid.NewGuid(), sale.Id, "INV-1", "Outward", "Supply", "INV",
            consignmentValuePaisa: 118_000, shipFromStateCode: "27", shipToStateCode: "27"));

        vm.OpenEInvoiceEWayStatusReport();
        Assert.Equal(Screen.EInvoiceEWayStatusReport, vm.CurrentScreen);
        var page = vm.EInvoiceEWayStatusReport!;

        Assert.True(page.HasEInvoices);
        Assert.Single(page.EInvoices);
        Assert.Equal("INV-1", page.EInvoices[0].DocNo);
        Assert.Equal("Generated", page.EInvoices[0].Status);
        Assert.Equal("Signed", page.EInvoices[0].Qr);
        Assert.Equal("112400001", page.EInvoices[0].AckNo);
        Assert.Contains("…", page.EInvoices[0].Irn);        // the 64-char IRN is shortened for the grid
        Assert.Contains("1 e-invoice(s)", page.EInvoiceStatusText);

        Assert.True(page.HasEWayBills);
        Assert.Single(page.EWayBills);
        Assert.Equal("Pending", page.EWayBills[0].Status);
        Assert.Equal("—", page.EWayBills[0].EwbNumber);     // no portal number until Generated
        Assert.Equal("—", page.EWayBills[0].ValidUpto);
    }

    [Fact]
    public void Einvoice_eway_status_is_empty_when_neither_is_used()
    {
        var vm = NewRegularGstCompany("EInv Empty Co");
        vm.OpenEInvoiceEWayStatusReport();

        var page = vm.EInvoiceEWayStatusReport!;
        Assert.False(page.HasEInvoices);
        Assert.False(page.HasEWayBills);
        Assert.Empty(page.EInvoices);
        Assert.Empty(page.EWayBills);
        Assert.Contains("No e-invoices raised", page.EInvoiceStatusText);
        Assert.Contains("No e-Way Bills raised", page.EWayStatusText);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch { /* best-effort cleanup */ }
    }
}
