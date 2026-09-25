using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Xunit;

using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.Tests;

/// <summary>
/// <b>The OUTPUT DEAD-END cluster — census rows 6.19, 6.20, 7.13 and 7.14.</b>
///
/// <para>🔴 <b>What was wrong, and why it was one defect rather than four.</b> Ten screens across these four rows
/// derived from <see cref="ViewModelBase"/> <b>alone</b>. None of them is a <c>Screen.Report</c>, so
/// <c>IsReportContext</c> is false on all ten, and the only other arm that can make a page exportable or
/// printable is <c>MainWindowViewModel.TopMasterExportSource()</c> — which tests the top cascade column for
/// <see cref="IMasterListExportSource"/>. Not implementing it meant E / Alt+E and P / Ctrl+P did nothing at all
/// on every one of them: not a refusal an operator could learn from, just keys with no effect.</para>
///
/// <para>🔴 <b>The arm was already general; the ADOPTION was the missing half.</b> 6.19's own evidence says so —
/// five sibling rows went COMPLETE on this same interface while 6.19 did not, and "a general fix nobody adopts
/// looks exactly like a fix from the outside". So the fix here adds no machinery and no per-screen list: each
/// view model implements one method. These tests exist to prove the shared arm reaches <b>every</b> member,
/// because a cluster fix demonstrated on one member with the rest assumed is precisely the failure this project
/// keeps catching at review.</para>
///
/// <para><b>Each case opens the screen through the real shell opener</b> — the same method the menu dispatch
/// calls — and then asserts the production gate (<c>IsExportablePage</c> / <c>IsPrintablePage</c>), not a
/// view-model flag. Calling <c>ToMasterListSnapshot()</c> directly would prove nothing about whether a user can
/// reach the export; the gate is what was false.</para>
/// </summary>
public sealed class OutputDeadEndClusterTests : IDisposable
{
    private const string GstinMaharashtra = "27AAPFU0939F1ZV";
    private const string GstinSupplier = "27AAACC1206D1ZM";
    private static readonly DateOnly FyStart = new(2024, 4, 1);

    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public OutputDeadEndClusterTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexOutputDeadEnd_" + Guid.NewGuid().ToString("N"));
        _storage = new CompanyStorage(_tempDir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch (IOException) { /* a held handle must not fail the assertion under test */ }
    }

    // ---------------------------------------------------------------- scaffolding

    private static DomainLedger Add(Company c, string name, string groupName, bool debit)
    {
        var group = c.FindGroupByName(groupName) ?? throw new InvalidOperationException($"No group '{groupName}'.");
        var ledger = new DomainLedger(Guid.NewGuid(), name, group.Id, Money.Zero, openingIsDebit: debit);
        c.AddLedger(ledger);
        return ledger;
    }

    /// <summary>A Regular GST company with one intra purchase and one intra B2B sale posted, so every advanced
    /// GST projection has real non-zero figures rather than an empty grid that would hide a broken snapshot.</summary>
    private MainWindowViewModel NewRegularGstCompany(string name)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = name;
        vm.CreateCompany();
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
        ledgers.Post(new Voucher(Guid.NewGuid(), purchaseType, new DateOnly(2024, 4, 3), pLines,
            partyId: supplier.Id, narration: "SUP-INV-001"));

        var sTax = gst.ComputeInvoiceTax(
            new[] { new GstService.TaxableLine(Money.FromRupees(1000m), 1800) }, false, GstTaxDirection.Output);
        var sLines = new List<EntryLine>
        {
            new(debtor.Id, Money.FromRupees(1180m), DrCr.Debit),
            new(sales.Id, Money.FromRupees(1000m), DrCr.Credit),
        };
        sLines.AddRange(sTax.TaxLines);
        ledgers.Post(new Voucher(Guid.NewGuid(), salesType, new DateOnly(2024, 4, 5), sLines, number: 1,
            partyId: debtor.Id));

        _storage.Save(c);
        vm.ShowGateway();
        return vm;
    }

    private MainWindowViewModel NewPayrollCompany(string name)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = name;
        vm.CreateCompany();
        vm.ShowGstConfig();
        var page = vm.GstConfig!;
        page.PayrollEnabled = true;
        page.PayrollStatutoryEnabled = true;
        vm.Back();
        return vm;
    }

    /// <summary>
    /// The assertion every member of the cluster must satisfy, stated once: after the real opener has run, the
    /// screen is the active one, BOTH production gates are true, and the page describes a grid with columns and
    /// at least one row. The row check matters — a snapshot with captions and no rows would pass a gate test and
    /// still export an empty file, which is the same dead end with extra steps.
    /// </summary>
    private static void AssertHasAnExit(MainWindowViewModel vm, Screen expected, string expectedTitle)
    {
        Assert.Equal(expected, vm.CurrentScreen);

        // 🔴 The production gates — the two properties that were FALSE on all ten screens before this slice.
        Assert.True(vm.IsExportablePage, $"{expected}: E / Alt+E is still dead on this page.");
        Assert.True(vm.IsPrintablePage, $"{expected}: P / Ctrl+P is still dead on this page.");

        // The top cascade column really is the page (this is what TopMasterExportSource() reads).
        var source = Assert.IsAssignableFrom<IMasterListExportSource>(vm.Columns[^1].Page);

        var snap = source.ToMasterListSnapshot();
        Assert.Equal(expectedTitle, snap.Title);
        Assert.NotEmpty(snap.Columns);
        Assert.NotEmpty(snap.Rows);
        Assert.All(snap.Columns, col => Assert.False(string.IsNullOrWhiteSpace(col.Caption)));

        // 🔴 Every row is aligned to the column count. A ragged row is how a projector silently drops a cell or
        // shifts a money figure into the wrong column — an export that is worse than no export.
        Assert.All(snap.Rows, r => Assert.Equal(snap.Columns.Count, r.Count));
    }

    // ============================================================ 6.19 — six advanced-GST read-only screens

    [Fact]
    public void Row_6_19_electronic_ledgers_can_leave_the_screen()
    {
        var vm = NewRegularGstCompany("Elec Ledgers Out Co");
        vm.OpenElectronicLedgersReport();
        AssertHasAnExit(vm, Screen.ElectronicLedgersReport, "Electronic Ledgers");
    }

    [Fact]
    public void Row_6_19_itc_set_off_can_leave_the_screen()
    {
        var vm = NewRegularGstCompany("Itc SetOff Out Co");
        vm.OpenItcSetOffReport();
        AssertHasAnExit(vm, Screen.ItcSetOffReport, "ITC Set-Off (Rule 88A) — projection");
    }

    [Fact]
    public void Row_6_19_itc_reversal_can_leave_the_screen()
    {
        var vm = NewRegularGstCompany("Itc Reversal Out Co");
        vm.OpenItcReversalReport();
        AssertHasAnExit(vm, Screen.ItcReversalReport, "ITC Reversal — outstanding balance & candidates");
    }

    [Fact]
    public void Row_6_19_itc_gate_can_leave_the_screen()
    {
        var vm = NewRegularGstCompany("Itc Gate Out Co");
        vm.OpenItcGateReport();
        AssertHasAnExit(vm, Screen.ItcGateReport, "ITC Gate — §16(2)(aa) / §17(5)");
    }

    [Fact]
    public void Row_6_19_qrmp_can_leave_the_screen()
    {
        var vm = NewRegularGstCompany("Qrmp Out Co");
        vm.OpenQrmpReport();
        AssertHasAnExit(vm, Screen.QrmpReport, "QRMP / IFF Cadence");
    }

    [Fact]
    public void Row_6_19_gst_amendments_can_leave_the_screen()
    {
        var vm = NewRegularGstCompany("Gst Amend Out Co");
        vm.OpenGstAmendmentsReport();
        AssertHasAnExit(vm, Screen.GstAmendmentsReport, "GSTR-1 / 3B Amendments");
    }

    [Fact]
    public void Row_6_19_einvoice_eway_status_can_leave_the_screen()
    {
        var vm = NewRegularGstCompany("EInv Status Out Co");
        vm.OpenEInvoiceEWayStatusReport();
        AssertHasAnExit(vm, Screen.EInvoiceEWayStatusReport, "e-Invoice / e-Way Status");
    }

    /// <summary>
    /// 🔴 <b>The e-Invoice export carries the FULL IRN, not the grid's elided one.</b> The grid shortens a
    /// 64-character IRN to "first 12… last 6" to fit its column. On screen that is fine. In a CSV handed to
    /// someone else it is a dead identifier that still LOOKS like a value — it cannot be looked up on the portal
    /// and no reader could tell it was truncated. This is the one deliberate departure from "the export mirrors
    /// the grid", so it is asserted rather than left to the doc comment.
    /// </summary>
    [Fact]
    public void Row_6_19_einvoice_export_carries_the_unelided_irn()
    {
        var vm = NewRegularGstCompany("EInv Irn Co");
        var c = vm.Company!;
        var irn = new string('a', 64);
        var sale = c.Vouchers.First(v => v.Date == new DateOnly(2024, 4, 5));

        // Rehydrate is the only public way to build a GENERATED record: the normal constructor deliberately has
        // no IRN parameter (ER-5 — an IRN comes FROM the IRP and is never computed here).
        c.AddEInvoiceRecord(EInvoiceRecord.Rehydrate(
            Guid.NewGuid(), sale.Id, "INV-1", EInvoiceStatus.Generated,
            irn: irn, ackNo: "112233445566", ackDate: new DateOnly(2024, 4, 5),
            signedQr: "signed-qr", signedJson: null, cancelledOn: null, cancelReasonCode: null));

        vm.OpenEInvoiceEWayStatusReport();
        var source = Assert.IsAssignableFrom<IMasterListExportSource>(vm.Columns[^1].Page);
        var snap = source.ToMasterListSnapshot();

        var row = Assert.Single(snap.Rows, r => r[1] == "INV-1");
        Assert.Equal(irn, row[3]);            // the whole 64 characters …
        Assert.DoesNotContain("…", row[3]);   // … and demonstrably not the elided form.

        // The grid itself still elides — the fix is in what is EXPORTED, not a regression of the display.
        var page = Assert.IsType<EInvoiceEWayStatusReportViewModel>(vm.Columns[^1].Page);
        Assert.Contains("…", page.EInvoices.Single(r => r.DocNo == "INV-1").Irn);
    }

    // ============================================================ 6.20 — DRC-03

    [Fact]
    public void Row_6_20_drc03_panel_can_leave_the_screen()
    {
        var vm = NewRegularGstCompany("Drc03 Out Co");
        vm.OpenDrc03Payment();
        AssertHasAnExit(vm, Screen.Drc03Payment, "DRC-03 — Voluntary / Self-Ascertained Payment");
    }

    /// <summary>
    /// 🔴 <b>What 6.20 still does NOT get, asserted so the row cannot be over-graded.</b> 6.20's gap has two
    /// halves — no output path, and no filable DRC-03 artefact. This slice closes the first only. The export is
    /// a register of filed payments and says so on its face; it is not a portal-shaped DRC-03. A future reader
    /// who sees "DRC-03 exports now" must be able to find the half that is still open.
    /// </summary>
    [Fact]
    public void Row_6_20_drc03_export_is_a_register_not_a_filable_artefact()
    {
        var vm = NewRegularGstCompany("Drc03 Scope Co");
        vm.OpenDrc03Payment();
        var snap = Assert.IsAssignableFrom<IMasterListExportSource>(vm.Columns[^1].Page).ToMasterListSnapshot();

        Assert.Contains(snap.Rows, r => r[0].Contains("NOT a filable DRC-03 artefact", StringComparison.Ordinal));
    }

    // ============================================================ 7.13 / 7.14 — the two payroll registers

    private MainWindowViewModel GratuityCompany(string name)
    {
        var vm = NewPayrollCompany(name);
        vm.ShowGstConfig();
        var page = vm.GstConfig!;
        page.GratuityEnabled = true;
        Assert.True(page.ApplyGratuity());
        vm.Back();
        vm.ShowGateway();
        return vm;
    }

    private MainWindowViewModel BonusCompany(string name)
    {
        var vm = NewPayrollCompany(name);
        vm.ShowGstConfig();
        var page = vm.GstConfig!;
        page.BonusEnabled = true;
        page.BonusRatePercentText = "8.33";
        page.BonusCalculationCeilingText = "7000";
        page.BonusMinimumWageText = "0";
        page.BonusProrate = true;
        Assert.True(page.ApplyBonus());
        vm.Back();
        vm.ShowGateway();
        return vm;
    }

    [Fact]
    public void Row_7_13_gratuity_provision_register_can_leave_the_screen()
    {
        var vm = GratuityCompany("Gratuity Out Co");
        vm.OpenGratuityProvisionRegister();
        AssertHasAnExit(vm, Screen.GratuityProvisionRegister, "Gratuity Provision Register");
    }

    /// <summary>
    /// The gratuity export carries the <b>delta</b>, which is the one figure the register is read for and is NOT
    /// the column total of any row — it is the accrued liability net of the prior posted provision. An export of
    /// the per-employee accruals alone would invite a reader to add the column up and post the wrong number.
    /// </summary>
    [Fact]
    public void Row_7_13_gratuity_export_carries_the_delta_not_only_the_employee_rows()
    {
        var vm = GratuityCompany("Gratuity Delta Co");
        vm.OpenGratuityProvisionRegister();
        var snap = Assert.IsAssignableFrom<IMasterListExportSource>(vm.Columns[^1].Page).ToMasterListSnapshot();

        Assert.Contains(snap.Rows, r => r[0] == "Total Liability");
        Assert.Contains(snap.Rows, r => r[0] == "Less: Prior Posted Provision");
        Assert.Contains(snap.Rows, r => r[0] == "Provision to Post (delta)");
        Assert.Contains(snap.Rows, r => r[0].StartsWith("Provision as-on ", StringComparison.Ordinal));
    }

    [Fact]
    public void Row_7_14_bonus_register_can_leave_the_screen()
    {
        var vm = BonusCompany("Bonus Out Co");
        vm.OpenBonusRegister();
        AssertHasAnExit(vm, Screen.BonusRegister, "Statutory Bonus Register");
    }

    /// <summary>
    /// The bonus export carries BOTH wage bases. A bonus register is the document produced to show the §12
    /// calculation ceiling was applied, and that is unprovable from the bonus figure alone — it needs the actual
    /// Basic + DA beside the capped base it was computed on.
    /// </summary>
    [Fact]
    public void Row_7_14_bonus_export_carries_both_the_actual_and_the_capped_wage_base()
    {
        var vm = BonusCompany("Bonus Base Co");
        vm.OpenBonusRegister();
        var snap = Assert.IsAssignableFrom<IMasterListExportSource>(vm.Columns[^1].Page).ToMasterListSnapshot();

        var captions = snap.Columns.Select(col => col.Caption).ToArray();
        Assert.Contains("Actual Basic + DA", captions);
        Assert.Contains("Capped Base", captions);
        Assert.Contains(snap.Rows, r => r[0] == "Total Bonus Payable");
    }

    // ============================================================ the cluster, as a class

    /// <summary>
    /// 🔴 <b>The cluster guard.</b> The per-screen tests above prove the fix reaches each member today. This one
    /// states the CLASS: every view model the four rows name must implement <see cref="IMasterListExportSource"/>,
    /// because that single interface is what <c>TopMasterExportSource()</c> — and therefore both
    /// <c>IsExportablePage</c> and <c>IsPrintablePage</c> — is gated on. A new screen added to any of these rows
    /// without it is the same dead end returning, and this test names the offender rather than leaving it to be
    /// re-measured by a future census pass.
    /// </summary>
    [Fact]
    public void Every_screen_in_the_output_dead_end_cluster_implements_the_export_source()
    {
        var cluster = new (string Row, Type Vm)[]
        {
            ("6.19", typeof(ElectronicLedgersReportViewModel)),
            ("6.19", typeof(ItcSetOffReportViewModel)),
            ("6.19", typeof(ItcReversalReportViewModel)),
            ("6.19", typeof(ItcGateReportViewModel)),
            ("6.19", typeof(QrmpReportViewModel)),
            ("6.19", typeof(GstAmendmentsReportViewModel)),
            ("6.19", typeof(EInvoiceEWayStatusReportViewModel)),
            ("6.20", typeof(Drc03PaymentViewModel)),
            ("7.13", typeof(GratuityProvisionRegisterViewModel)),
            ("7.14", typeof(BonusRegisterViewModel)),
        };

        var dead = cluster
            .Where(x => !typeof(IMasterListExportSource).IsAssignableFrom(x.Vm))
            .Select(x => $"census {x.Row}: {x.Vm.Name}")
            .ToArray();

        Assert.True(dead.Length == 0,
            "These cluster screens are still output dead ends (no E / Alt+E, no P / Ctrl+P): "
            + string.Join(", ", dead));
    }
}
