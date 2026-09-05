using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.Tests;

/// <summary>
/// W2-06 slice (b) — <b>reachability</b> of the GST offline-return JSON writers (census rows 6.10, 6.13).
/// <para>
/// Before this slice <c>GstReturnJson</c> had <b>zero production callers</b>: the five writers it exposed
/// (CMP-08, GSTR-4, 9, 9A, 9C) could not be reached from any screen, any menu or any keystroke, and the two
/// added by slice (a) (GSTR-1, GSTR-3B) had nowhere to be invoked from either. A capability a user cannot
/// reach does not close a census row, so this file drives the whole route through the real shell: the menu
/// entry exists → the screen opens → the figures project → the JSON is built → the file is written.
/// </para>
/// <para>
/// Every expected figure is derived by hand from the shared fixture: an intra-state purchase of ₹5,000 @18%
/// (ITC CGST ₹450.00 + SGST ₹450.00) and an intra-state B2B sale of ₹1,000 @18% (output CGST ₹90.00 +
/// SGST ₹90.00), both in April 2024. So GSTR-1 total CGST = ₹90.00 (9000 paisa) and GSTR-3B table 6.1 net
/// CGST = 90.00 − 450.00 = <b>−₹360.00</b> (−36000 paisa), a carried-forward credit shown verbatim (DP-9).
/// </para>
/// </summary>
public sealed class GstOfflineReturnsUiViewModelTests : IDisposable
{
    private const string GstinMaharashtra = "27AAPFU0939F1ZV";
    private const string GstinSupplier = "27AAACC1206D1ZM";

    private static readonly DateOnly FyStart = new(2024, 4, 1);
    private static readonly DateOnly PurchaseDate = new(2024, 4, 3);
    private static readonly DateOnly SaleDate = new(2024, 4, 5);
    private static readonly DateOnly RcmDate = new(2024, 4, 8);

    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public GstOfflineReturnsUiViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexGstOfflineReturnsTests_" + Guid.NewGuid().ToString("N"));
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

    private static void EnableGst(Company c, GstRegistrationType type)
        => new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = "27",
            Gstin = GstinMaharashtra,
            RegistrationType = type,
            CompositionSubType = type == GstRegistrationType.Composition ? CompositionSubType.Trader : null,
            ApplicableFrom = FyStart,
            Periodicity = type == GstRegistrationType.Composition
                ? GstReturnPeriodicity.Quarterly
                : GstReturnPeriodicity.Monthly,
        });

    private static DomainLedger Add(Company c, string name, string groupName, bool debit)
    {
        var group = c.FindGroupByName(groupName) ?? throw new InvalidOperationException($"No group '{groupName}'.");
        var ledger = new DomainLedger(Guid.NewGuid(), name, group.Id, Money.Zero, openingIsDebit: debit);
        c.AddLedger(ledger);
        return ledger;
    }

    /// <summary>A Regular GST company with the ₹5,000 purchase (ITC 450+450) and the ₹1,000 B2B sale (90+90).</summary>
    private MainWindowViewModel NewRegularGstCompany(string name)
    {
        var vm = NewSeededCompany(name);
        var c = vm.Company!;
        EnableGst(c, GstRegistrationType.Regular);

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

        var pTax = gst.ComputeInvoiceTax(
            new[] { new GstService.TaxableLine(Money.FromRupees(5000m), 1800) }, false, GstTaxDirection.Input);
        var pLines = new List<EntryLine>
        {
            new(purchases.Id, Money.FromRupees(5000m), DrCr.Debit),
            new(supplier.Id, Money.FromRupees(5900m), DrCr.Credit),
        };
        pLines.AddRange(pTax.TaxLines);
        ledgers.Post(new Voucher(Guid.NewGuid(), purchaseType, PurchaseDate, pLines, partyId: supplier.Id));

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

    /// <summary>A Composition dealer with one ₹1,00,001 outward sale (the CMP-08 / GSTR-4 / GSTR-9A fixture).</summary>
    private MainWindowViewModel NewCompositionCompany(string name)
    {
        var vm = NewSeededCompany(name);
        var c = vm.Company!;
        EnableGst(c, GstRegistrationType.Composition);

        var sales = Add(c, "Sales", "Sales Accounts", false);
        sales.SalesPurchaseGst = new StockItemGstDetails { Taxability = GstTaxability.Taxable, RateBasisPoints = 1800 };
        var party = Add(c, "Walk-in", "Sundry Debtors", true);
        var salesType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id;
        new LedgerService(c).Post(new Voucher(Guid.NewGuid(), salesType, SaleDate, new[]
        {
            new EntryLine(party.Id, Money.FromRupees(100001m), DrCr.Debit),
            new EntryLine(sales.Id, Money.FromRupees(100001m), DrCr.Credit),
        }, partyId: party.Id));

        _storage.Save(c);
        vm.ShowGateway();
        return vm;
    }

    /// <summary>
    /// A Regular GST company carrying BOTH reverse-charge ITC kinds, so that GSTR-3B row 4(A)(2) and row 4(A)(3)
    /// hold DIFFERENT figures and a row that silently sums the two is visible:
    /// <list type="bullet">
    ///   <item>an <b>import of services</b> of ₹20,000 @18% ⇒ IGST ₹3,600.00 — GSTR-3B table 4(A)(2);</item>
    ///   <item>a <b>domestic inter-state</b> legal fee of ₹10,000 @18% ⇒ IGST ₹1,800.00 — GSTR-3B table 4(A)(3).</item>
    /// </list>
    /// Both are posted in April 2024 so the April period picks them up.
    /// </summary>
    private MainWindowViewModel NewRegularGstCompanyWithBothRcmKinds(string name)
    {
        var vm = NewSeededCompany(name);
        var c = vm.Company!;
        var gst = new GstService(c);
        EnableGst(c, GstRegistrationType.Regular);
        gst.SeedAdvancedGst(); // seeds the notified RCM categories BuildReverseCharge resolves against

        var rcm = new RcmService(c);

        var legal = Add(c, "Legal Fees", "Indirect Expenses", true);
        legal.SalesPurchaseGst = new StockItemGstDetails
        {
            Taxability = GstTaxability.Taxable,
            RateBasisPoints = 1800,
            SupplyType = GstSupplyType.Services,
            ReverseChargeApplicable = true,
            RcmCategoryId = c.Gst!.RcmCategories.First(x => x.SupplyNature == "Legal").Id,
        };
        var advocate = Add(c, "Advocate (Gujarat)", "Sundry Creditors", false);
        advocate.PartyGst = new PartyGstDetails
        { RegistrationType = GstRegistrationType.Regular, Gstin = "24AAACC1206D1ZM", StateCode = "24" };

        var consulting = Add(c, "Foreign Consulting", "Indirect Expenses", true);
        consulting.SalesPurchaseGst = new StockItemGstDetails
        {
            Taxability = GstTaxability.Taxable,
            RateBasisPoints = 1800,
            SupplyType = GstSupplyType.Services,
            ReverseChargeApplicable = true,
            RcmCategoryId = c.Gst!.RcmCategories.First(x => x.SupplyNature == "Legal").Id,
        };
        var overseas = Add(c, "Overseas Consultant", "Sundry Creditors", false);
        overseas.PartyGst = new PartyGstDetails { RegistrationType = GstRegistrationType.Unregistered };

        PostRcm(c, legal, advocate, Money.FromRupees(10000m),
            rcm.BuildReverseCharge(Money.FromRupees(10000m), null, legal, advocate.PartyGst, RcmDate,
                RcmService.SupplyKind.Domestic));
        PostRcm(c, consulting, overseas, Money.FromRupees(20000m),
            rcm.BuildReverseCharge(Money.FromRupees(20000m), null, consulting, overseas.PartyGst, RcmDate,
                RcmService.SupplyKind.ImportOfServices));

        _storage.Save(c);
        vm.ShowGateway();
        return vm;
    }

    /// <summary>Posts the RCM inward Purchase: Dr Expense / Cr Party (supplier charges zero tax) + the balanced pair.</summary>
    private static void PostRcm(
        Company c, DomainLedger expense, DomainLedger party, Money value, RcmService.RcmPosting posting)
    {
        Assert.True(posting.Applies, "the RCM fixture must actually raise a reverse-charge pair");
        var lines = new List<EntryLine>
        {
            new(expense.Id, value, DrCr.Debit),
            new(party.Id, value, DrCr.Credit),
        };
        lines.AddRange(posting.Lines);
        var type = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase).Id;
        new LedgerService(c).Post(new Voucher(Guid.NewGuid(), type, RcmDate, lines));
    }

    private static string Figure(GstOfflineReturnsViewModel page, string label) =>
        page.Figures.Single(f => f.Label == label).Value;

    // ================================================================ navigation / gating

    [Fact]
    public void Regular_dealer_surfaces_the_offline_return_files_page_under_gst_returns_advanced()
    {
        var vm = NewRegularGstCompany("Offline Nav Co");

        vm.ShowGstAdvancedReturnsMenu();
        var labels = vm.Columns[^1].Items.Where(i => i.IsSelectable).Select(i => i.Label).ToList();
        Assert.Contains("Offline Return Files (JSON)", labels);
    }

    [Fact]
    public void Composition_dealer_surfaces_gstr9a_and_the_offline_return_files_page()
    {
        var vm = NewCompositionCompany("Offline Comp Nav Co");

        vm.ShowCompositionReturnsMenu();
        var labels = vm.Columns[^1].Items.Where(i => i.IsSelectable).Select(i => i.Label).ToList();
        Assert.Contains("GSTR-9A", labels);
        Assert.Contains("Offline Return Files (JSON)", labels);
    }

    /// <summary>
    /// 🔴 <b>The reachability standard for this slice.</b> Calling <c>OpenGstOfflineReturns()</c> from a test proves
    /// only that a method exists — this project has already shipped <c>CompanyStorage.Rename()</c> and
    /// <c>CostReports.BuildLedgerBreakup</c> fully written, fully tested and callable by nobody. So this test drives
    /// the row the way the keyboard does: highlight it in the cascade and press Enter (<c>DrillIn</c>), through the
    /// real <c>OpenPageOf</c> dispatch, from the Gateway root down.
    /// </summary>
    [Fact]
    public void The_offline_return_files_menu_row_opens_the_page_when_a_user_drills_into_it()
    {
        var vm = NewRegularGstCompany("Offline Drill Co");

        vm.ShowGstAdvancedReturnsMenu();
        SelectByLabel(vm.Columns[^1], "Offline Return Files (JSON)");
        vm.DrillIn();

        Assert.Equal(Screen.GstOfflineReturns, vm.CurrentScreen);
        Assert.NotNull(vm.GstOfflineReturns);
        Assert.NotEmpty(vm.GstOfflineReturns!.Figures);
        Assert.True(vm.GstOfflineReturns.BuildJson().Length > 0);
    }

    /// <summary>
    /// Census row 6.13 (GSTR-9A) the same way: the composition menu row must actually land on the GSTR-9A form.
    /// This is a menu row that dispatches into the shared offline-returns page, NOT a GSTR-9A report page of its own
    /// — the row moves ABSENT → PARTIAL, and is recorded as PARTIAL.
    /// </summary>
    [Fact]
    public void The_gstr9a_menu_row_opens_the_page_on_gstr9a_when_a_user_drills_into_it()
    {
        var vm = NewCompositionCompany("Offline 9A Drill Co");

        vm.ShowCompositionReturnsMenu();
        SelectByLabel(vm.Columns[^1], "GSTR-9A");
        vm.DrillIn();

        Assert.Equal(Screen.GstOfflineReturns, vm.CurrentScreen);
        Assert.Equal("GSTR-9A", vm.GstOfflineReturns!.SelectedReturn!.Label);
    }

    private static void SelectByLabel(GatewayColumn column, string label)
    {
        for (var i = 0; i < column.Items.Count; i++)
            if (column.Items[i].IsSelectable && column.Items[i].Label == label)
            {
                column.SetSelected(i);
                return;
            }
        throw new Xunit.Sdk.XunitException($"No selectable row labelled '{label}' in column '{column.Title}'.");
    }

    [Fact]
    public void A_company_without_gst_can_never_open_the_offline_returns_page()
    {
        var vm = NewSeededCompany("No Gst Co");
        _storage.Save(vm.Company!);
        vm.ShowGateway();

        vm.OpenGstOfflineReturns();
        Assert.Null(vm.GstOfflineReturns);
        Assert.Equal(Screen.Gateway, vm.CurrentScreen);
    }

    // ================================================================ the applicable-return sets

    [Fact]
    public void Regular_dealer_is_offered_exactly_the_regular_returns()
    {
        var vm = NewRegularGstCompany("Offline Regular Co");
        vm.OpenGstOfflineReturns();

        Assert.Equal(Screen.GstOfflineReturns, vm.CurrentScreen);
        Assert.Equal(
            new[] { "GSTR-1", "GSTR-3B", "GSTR-9", "GSTR-9C" },
            vm.GstOfflineReturns!.Returns.Select(r => r.Label).ToArray());
    }

    [Fact]
    public void Composition_dealer_is_offered_exactly_the_composition_returns()
    {
        var vm = NewCompositionCompany("Offline Comp Co");
        vm.OpenGstOfflineReturns();

        Assert.Equal(
            new[] { "CMP-08", "GSTR-4", "GSTR-9A" },
            vm.GstOfflineReturns!.Returns.Select(r => r.Label).ToArray());
    }

    [Fact]
    public void The_gstr9a_menu_entry_opens_the_page_preselected_on_gstr9a()
    {
        var vm = NewCompositionCompany("Offline 9A Co");
        vm.OpenGstOfflineReturns(GstOfflineReturnKind.Gstr9a);

        var page = vm.GstOfflineReturns!;
        Assert.Equal("GSTR-9A", page.SelectedReturn!.Label);
        // Composition tax paid = the CMP-08 tax on ₹1,00,001 at 1% (0.5% CGST + 0.5% SGST) = ₹1,000.01.
        Assert.Equal("1,000.01", Figure(page, "Composition tax paid"));
    }

    // ================================================================ the figures + the JSON

    [Fact]
    public void Gstr1_for_april_projects_the_output_tax_and_builds_the_portal_json()
    {
        var vm = NewRegularGstCompany("Offline Gstr1 Co");
        vm.OpenGstOfflineReturns(GstOfflineReturnKind.Gstr1);
        var page = vm.GstOfflineReturns!;

        page.SelectedPeriod = page.Periods.Single(p => p.Label == "April 2024");

        Assert.Equal("90.00", Figure(page, "Total CGST"));
        Assert.Equal("90.00", Figure(page, "Total SGST"));
        Assert.Equal("0.00", Figure(page, "Total IGST"));
        Assert.Equal("1", Figure(page, "B2B invoices"));

        using var doc = JsonDocument.Parse(page.BuildJson());
        Assert.Equal("042024", doc.RootElement.GetProperty("fp").GetString());
        Assert.Equal(9000L, doc.RootElement.GetProperty("total_cgst_paisa").GetInt64());
        Assert.Equal(1, doc.RootElement.GetProperty("b2b").GetArrayLength());
    }

    [Fact]
    public void Gstr3b_for_april_shows_the_negative_net_head_verbatim()
    {
        var vm = NewRegularGstCompany("Offline Gstr3b Co");
        vm.OpenGstOfflineReturns(GstOfflineReturnKind.Gstr3b);
        var page = vm.GstOfflineReturns!;

        page.SelectedPeriod = page.Periods.Single(p => p.Label == "April 2024");

        Assert.Equal("1,000.00", Figure(page, "3.1(a) Taxable outward value"));
        Assert.Equal("450.00", Figure(page, "4(A)(5) ITC CGST"));
        // 90.00 − 450.00 = −360.00, a carried-forward credit shown as it stands (DP-9), never floored to zero.
        Assert.Equal("-360.00", Figure(page, "6.1 Net CGST"));

        using var doc = JsonDocument.Parse(page.BuildJson());
        Assert.Equal(-36000L, doc.RootElement.GetProperty("tbl6_1_net_camt_paisa").GetInt64());
    }

    /// <summary>
    /// 🔴 <b>Wrong-money regression lock.</b> GSTR-3B row <b>4(A)(3)</b> is, verbatim per CBIC Circular
    /// No. 170/02/2022-GST (Table 2, Table 4 "(A) ITC Available"), <i>"3. Inward Supplies liable to Reverse Charge
    /// <b>(other than 1 &amp; 2 above)</b>"</i> — rows 1 and 2 being import of goods and import of services. That
    /// circular's own worked example books ITC on import of services (IGST ₹50,000) to 4(A)(2) and ITC on inward
    /// supplies under RCM (CGST ₹25,000 + SGST ₹25,000) to 4(A)(3); the import figure appears in 4(A)(3) nowhere.
    /// <para>
    /// The page originally bound the 4(A)(3) row to <c>Gstr3b.TotalRcmItc</c>, which that record documents (and
    /// computes) as Σ 4(A)(2) <b>+</b> 4(A)(3) — so the screen showed a label and a figure that disagreed, and the
    /// import-of-services IGST was counted twice on one filed return.
    /// </para>
    /// Hand-derived from the fixture: 4(A)(2) = 18% of ₹20,000 = <b>₹3,600.00</b>; 4(A)(3) = 18% of ₹10,000 =
    /// <b>₹1,800.00</b>. The defect showed ₹5,400.00 on the 4(A)(3) row.
    /// </summary>
    [Fact]
    public void Gstr3b_row_4A3_excludes_the_import_of_services_itc_that_row_4A2_already_carries()
    {
        var vm = NewRegularGstCompanyWithBothRcmKinds("Offline Rcm Split Co");
        vm.OpenGstOfflineReturns(GstOfflineReturnKind.Gstr3b);
        var page = vm.GstOfflineReturns!;
        page.SelectedPeriod = page.Periods.Single(p => p.Label == "April 2024");

        Assert.Equal("3,600.00", Figure(page, "4(A)(2) ITC on import of services"));
        Assert.Equal("1,800.00", Figure(page, "4(A)(3) ITC on other reverse-charge inward"));
    }

    [Fact]
    public void Export_writes_the_named_json_file_through_the_seam()
    {
        var vm = NewRegularGstCompany("Offline Export Co");
        vm.OpenGstOfflineReturns(GstOfflineReturnKind.Gstr1);
        var page = vm.GstOfflineReturns!;
        page.SelectedPeriod = page.Periods.Single(p => p.Label == "April 2024");
        page.ExportFolder = "C:/exports";

        string? writtenPath = null;
        byte[]? writtenBytes = null;
        Assert.True(page.ExportJson((p, b) => { writtenPath = p; writtenBytes = b; }));

        Assert.NotNull(writtenPath);
        Assert.Contains("GSTR-1", writtenPath!);
        Assert.Contains("042024", writtenPath!);
        Assert.EndsWith(".json", writtenPath!);
        Assert.Equal(page.BuildJson(), writtenBytes);
        Assert.Contains("Exported", page.ExportStatus);
    }

    /// <summary>
    /// The export destination must be a real, findable folder before the user touches anything. With
    /// <c>ExportFolder</c> empty the path handed to <c>File.WriteAllBytes</c> is a bare file name, so the return
    /// file lands in the process working directory — beside the executable — with no picker and no way for the user
    /// to know where it went. Every other shipped export page on this app (Form 16, Form 16A, Form 24Q, Form 26Q,
    /// the ESI contribution report) seeds the same default; this page must match them.
    ///
    /// <para><b>The assertion is deliberately NOT against <c>MyDocuments</c>.</b> On Linux that lookup returns the
    /// EMPTY STRING when XDG user dirs are unconfigured, so an equality check against it compares <c>""</c> to
    /// <c>""</c> and passes VACUOUSLY on the one platform where the defect is real — which is how it shipped. The
    /// page is pinned to <see cref="DefaultExportFolder"/>, whose own suite proves the ladder is never blank, and
    /// the non-blank and not-the-working-directory assertions below bite on every platform.</para>
    /// </summary>
    [Fact]
    public void The_export_folder_defaults_to_a_findable_folder_so_the_file_never_lands_in_the_working_directory()
    {
        var vm = NewRegularGstCompany("Offline Folder Co");
        vm.OpenGstOfflineReturns();
        var page = vm.GstOfflineReturns!;

        Assert.Equal(DefaultExportFolder.Resolve(), page.ExportFolder);
        Assert.False(string.IsNullOrWhiteSpace(page.ExportFolder));
        Assert.True(Path.IsPathRooted(page.ExportFolder), page.ExportFolder);
        Assert.NotEqual(
            Directory.GetCurrentDirectory().TrimEnd(Path.DirectorySeparatorChar),
            page.ExportFolder.TrimEnd(Path.DirectorySeparatorChar));

        string? writtenPath = null;
        Assert.True(page.ExportJson((p, _) => writtenPath = p));
        Assert.Equal(Path.Combine(page.ExportFolder, page.ExportFileName), writtenPath);
    }

    /// <summary>
    /// <c>ExportFileName</c> and <c>FinancialPeriodCode</c> are computed from the selected form + period and are
    /// bound in the view (the file-name placeholder). Without a change notification the placeholder keeps showing the
    /// file name of whatever was selected when the page opened, while the button writes a different file.
    /// </summary>
    [Fact]
    public void Changing_the_form_or_the_period_renotifies_the_derived_file_name()
    {
        var vm = NewRegularGstCompany("Offline Notify Co");
        vm.OpenGstOfflineReturns(GstOfflineReturnKind.Gstr1);
        var page = vm.GstOfflineReturns!;
        page.SelectedPeriod = page.Periods.Single(p => p.Label == "April 2024");
        Assert.Equal($"GSTR-1_{GstinMaharashtra}_042024.json", page.ExportFileName);

        var raised = new List<string>();
        page.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? string.Empty);

        page.SelectedPeriod = page.Periods.Single(p => p.Label == "May 2024");
        Assert.Contains(nameof(page.ExportFileName), raised);
        Assert.Contains(nameof(page.FinancialPeriodCode), raised);
        Assert.Equal($"GSTR-1_{GstinMaharashtra}_052024.json", page.ExportFileName);

        raised.Clear();
        page.SelectedReturn = page.Returns.Single(r => r.Label == "GSTR-9");
        Assert.Contains(nameof(page.ExportFileName), raised);
        Assert.Contains(nameof(page.FinancialPeriodCode), raised);
        // GSTR-9 collapses to the full year, whose last month is March 2025.
        Assert.Equal($"GSTR-9_{GstinMaharashtra}_032025.json", page.ExportFileName);
    }

    [Fact]
    public void Annual_returns_offer_only_the_whole_financial_year()
    {
        var vm = NewRegularGstCompany("Offline Annual Co");
        vm.OpenGstOfflineReturns(GstOfflineReturnKind.Gstr9);
        var page = vm.GstOfflineReturns!;

        Assert.Single(page.Periods);
        Assert.Equal("Full year", page.Periods[0].Label);
        // Table 4 total tax = the sale's 90 + 90.
        Assert.Equal("180.00", Figure(page, "Table 4 total tax"));
    }

    [Fact]
    public void Cmp08_offers_the_four_quarters_of_the_financial_year()
    {
        var vm = NewCompositionCompany("Offline Cmp08 Co");
        vm.OpenGstOfflineReturns(GstOfflineReturnKind.Cmp08);
        var page = vm.GstOfflineReturns!;

        Assert.Equal(4, page.Periods.Count);
        Assert.Equal("Q1 (Apr-Jun 2024)", page.Periods[0].Label);
        // The ₹1,00,001 sale falls in Q1: CGST 0.5% = ₹500.01 (half-up), SGST 0.5% = ₹500.00.
        Assert.Equal("500.01", Figure(page, "Outward turnover CGST"));
        Assert.Equal("500.00", Figure(page, "Outward turnover SGST"));
    }

    /// <summary>The reachability lock for the whole slice: BEFORE W2-06 every one of these writers had zero
    /// production callers. Walking each applicable form and demanding a non-empty, schemaStatus-flagged file is
    /// what stops a later refactor from quietly orphaning one of them again.</summary>
    [Fact]
    public void Every_return_form_offered_to_a_regular_dealer_actually_produces_a_file()
    {
        var vm = NewRegularGstCompany("Offline Wiring Co");
        vm.OpenGstOfflineReturns();
        var page = vm.GstOfflineReturns!;

        foreach (var option in page.Returns.ToList())
        {
            page.SelectedReturn = option;
            var bytes = page.BuildJson();
            Assert.True(bytes.Length > 0, $"{option.Label} produced no file.");

            using var doc = JsonDocument.Parse(bytes);
            Assert.True(doc.RootElement.TryGetProperty("schemaStatus", out _), $"{option.Label} lost its schema flag.");
            Assert.Equal(GstinMaharashtra, doc.RootElement.GetProperty("gstin").GetString());
            Assert.NotEmpty(page.Figures);
        }
    }

    [Fact]
    public void Every_return_form_offered_to_a_composition_dealer_actually_produces_a_file()
    {
        var vm = NewCompositionCompany("Offline Comp Wiring Co");
        vm.OpenGstOfflineReturns();
        var page = vm.GstOfflineReturns!;

        foreach (var option in page.Returns.ToList())
        {
            page.SelectedReturn = option;
            var bytes = page.BuildJson();
            Assert.True(bytes.Length > 0, $"{option.Label} produced no file.");

            using var doc = JsonDocument.Parse(bytes);
            Assert.True(doc.RootElement.TryGetProperty("schemaStatus", out _), $"{option.Label} lost its schema flag.");
            Assert.NotEmpty(page.Figures);
        }
    }

    [Fact]
    public void The_page_never_names_the_reference_product()
    {
        var vm = NewRegularGstCompany("Offline Brand Co");
        vm.OpenGstOfflineReturns();
        var page = vm.GstOfflineReturns!;

        Assert.DoesNotContain("Tally", page.Title, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Tally", page.Subtitle, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Tally", page.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); } catch { }
    }
}
