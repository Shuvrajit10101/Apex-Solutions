using System;
using System.Collections.Generic;
using System.Globalization;
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
/// Coverage for the Phase-9 <b>UI-2 advanced-GST ACTION screens</b> surfaced in the cascade (RQ-17): the six
/// interactive screens nested under Reports → Statutory Reports → <b>GST Actions</b> — IMS (Accept / Reject /
/// Pending), Run Set-Off &amp; Pay, Post ITC Reversal, Import GSTR-2B, Generate e-Invoice and Generate e-Way Bill.
/// Unlike the UI-1 projections these <b>mutate</b>, so every screen is tested on four axes:
/// <list type="number">
/// <item><b>Opening posts NOTHING</b> — the engine effect appears only after an explicit action.</item>
/// <item>The <b>explicit action DOES</b> post (asserted on the engine's own effect, not the screen's text).</item>
/// <item>The <b>engine guard surfaces cleanly</b> as a message — never as a crash, and never a partial write.</item>
/// <item>The <b>ER-13 gate</b> holds: a Composition / GST-off company never reaches any of them.</item>
/// </list>
/// Drives the real shell view models over a throwaway <c>.db</c> — no UI toolkit.
/// </summary>
public sealed class GstActionsUiViewModelTests : IDisposable
{
    private const string GstinMaharashtra = "27AAPFU0939F1ZV"; // state code 27 (home)
    private const string GstinSupplier = "27AAACC1206D1ZM";    // an in-state supplier

    private static readonly DateOnly FyStart = new(2024, 4, 1);
    private static readonly DateOnly PurchaseDate = new(2024, 4, 3);
    private static readonly DateOnly SaleDate = new(2024, 4, 5);

    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public GstActionsUiViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexGstActionsUiTests_" + Guid.NewGuid().ToString("N"));
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
    /// (₹1,000 @ 18% ⇒ output 90+90) posted — the same fixture the UI-1 tests use, so every action screen has real,
    /// non-zero figures to act on.</summary>
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

        // PURCHASE ₹5,000 @ 18% intra ⇒ Input CGST 450 + SGST 450 (the ITC pool the set-off/reversal screens drive).
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

    /// <summary>Builds one GSTR-2B line; <paramref name="reverseCharge"/> raises the §3.3 flag that makes the line
    /// bypass IMS entirely (the engine throws if asked to action it).</summary>
    private static Gstr2bLine Line(string docNo, bool reverseCharge = false) => new(
        Guid.NewGuid(), GstinSupplier, "Local Supplier", Gstr2bDocType.B2b, docNo,
        Gstr2bReconciler.NormaliseDocNo(docNo), PurchaseDate, "27",
        taxableValuePaisa: 500_000, igstPaisa: 0, cgstPaisa: 45_000, sgstPaisa: 45_000, cessPaisa: 0,
        itcAvailable: true, itcUnavailableReason: null, reverseCharge: reverseCharge);

    /// <summary>Imports an Apr-2024 GSTR-2B snapshot carrying the given lines.</summary>
    private static void Import2b(Company c, params Gstr2bLine[] lines) =>
        c.AddGstr2bSnapshot(new Gstr2bSnapshot(
            Guid.NewGuid(), GstStatementType.Gstr2b, "2024-04", GstinMaharashtra, new DateOnly(2024, 5, 14),
            sourceFileHash: "hash-apr-2024", importedAt: new DateTimeOffset(2024, 5, 14, 0, 0, 0, TimeSpan.Zero),
            summaryIgstPaisa: 0, summaryCgstPaisa: 45_000, summarySgstPaisa: 45_000, summaryCessPaisa: 0,
            lines: lines));

    // ================================================================ Nav: the GST Actions group

    /// <summary>
    /// ER-13 root reachability: a <b>plain</b> Regular GST company must find the <b>GST Actions</b> group on the
    /// <b>Statutory Reports column itself</b> — asserted on the column the ROOT cascade actually builds, not on a
    /// force-opened submenu, so deleting the group-adding block turns this RED. (A test that only calls
    /// <c>ShowGstActionsMenu()</c> would still pass with the group unreachable — the blind spot that let the UI-1
    /// root-gate defect ship.)
    /// </summary>
    [Fact]
    public void Statutory_reports_column_surfaces_the_gst_actions_group_from_the_root()
    {
        var vm = NewRegularGstCompany("Actions Root Co");
        var c = vm.Company!;
        Assert.False(c.TdsEnabled);
        Assert.False(c.TcsEnabled);
        Assert.False(c.PayrollStatutoryEnabled);

        // The ROOT column offers the only door.
        vm.ShowGateway();
        var root = vm.Columns[0].Items.Where(i => i.IsSelectable).Select(i => i.Label).ToList();
        Assert.Contains("Statutory Reports", root);

        // …and that column must itself carry the GST Actions group.
        vm.ShowStatutoryReportsMenu();
        var statutory = vm.Columns[^1].Items.Where(i => i.IsSelectable).Select(i => i.Label).ToList();
        Assert.Contains("GST Actions", statutory);
    }

    [Fact]
    public void Gst_actions_group_lists_the_six_action_screens()
    {
        var vm = NewRegularGstCompany("Actions Nav Co");

        vm.ShowGstActionsMenu();
        Assert.Equal(GatewayMenu.GstActions, vm.CurrentGatewayMenu);
        var items = vm.Columns[^1].Items.Where(i => i.IsSelectable).Select(i => i.Label).ToList();
        Assert.Equal(
            new[]
            {
                "IMS (Accept / Reject / Pending)", "Run Set-Off & Pay", "Post ITC Reversal",
                "Import GSTR-2B", "Generate e-Invoice", "Generate e-Way Bill",
            },
            items);
    }

    /// <summary>ER-13: a Composition dealer never sees the GST Actions group, and every action opener is a no-op.</summary>
    [Fact]
    public void Composition_dealer_never_surfaces_the_gst_actions_group_or_screens()
    {
        var vm = NewSeededCompany("Actions Gated Co");
        EnableGst(vm.Company!, GstRegistrationType.Composition);
        _storage.Save(vm.Company!);
        vm.ShowGateway();

        vm.ShowStatutoryReportsMenu();
        Assert.DoesNotContain("GST Actions", vm.Columns[^1].Items.Select(i => i.Label));

        vm.ShowGstActionsMenu();
        Assert.NotEqual(GatewayMenu.GstActions, vm.CurrentGatewayMenu);

        vm.OpenImsActions(); Assert.Null(vm.ImsActions);
        Assert.Equal(Screen.Gateway, vm.CurrentScreen);
    }

    /// <summary>The other half of the ER-13 gate: a company that never enabled GST reaches no action screen.</summary>
    [Fact]
    public void Gst_off_company_never_surfaces_the_gst_actions_group_or_screens()
    {
        var vm = NewSeededCompany("Actions No Gst Co");
        _storage.Save(vm.Company!);
        vm.ShowGateway();

        var root = vm.Columns[0].Items.Where(i => i.IsSelectable).Select(i => i.Label).ToList();
        Assert.DoesNotContain("Statutory Reports", root);

        vm.ShowGstActionsMenu();
        Assert.NotEqual(GatewayMenu.GstActions, vm.CurrentGatewayMenu);

        vm.OpenImsActions(); Assert.Null(vm.ImsActions);
        Assert.Equal(Screen.Gateway, vm.CurrentScreen);
    }

    // ================================================================ Screen 1: IMS — Accept / Reject / Pending

    /// <summary>(a) Opening the IMS screen records NOTHING — the lines merely project their derived deemed-accept.</summary>
    [Fact]
    public void Ims_opening_the_screen_records_no_action()
    {
        var vm = NewRegularGstCompany("Ims Open Co");
        var c = vm.Company!;
        Import2b(c, Line("SUP-INV-001"));

        vm.OpenImsActions();
        Assert.Equal(Screen.ImsActions, vm.CurrentScreen);
        var page = vm.ImsActions!;

        Assert.True(page.HasSnapshot);
        Assert.Single(page.Rows);
        // Nothing is written merely by looking: the IMS action list is still empty…
        Assert.Empty(c.ImsActions);
        // …yet the line already reads as accepted, because no-action is DEEMED accepted on filing.
        Assert.Equal("Accepted (deemed)", page.Rows[0].Status);
        Assert.False(page.Rows[0].IsExplicit);
        Assert.True(page.Rows[0].IsActionable);
        Assert.Equal(1, page.AcceptedCount);

        // The shell routes the arrows to the row list.
        Assert.True(vm.IsImsActionsScreen);
        Assert.Equal(0, page.HighlightedIndex);
        vm.MoveDown();
        Assert.Equal(0, page.HighlightedIndex);   // single row ⇒ wraps onto itself
    }

    /// <summary>(b) The explicit Reject DOES record an IMS action against the highlighted line.</summary>
    [Fact]
    public void Ims_explicit_reject_records_the_action_against_the_line()
    {
        var vm = NewRegularGstCompany("Ims Reject Co");
        var c = vm.Company!;
        Import2b(c, Line("SUP-INV-001"));
        vm.OpenImsActions();
        var page = vm.ImsActions!;
        var lineId = page.Rows[0].LineId;

        Assert.True(page.Reject());

        // The ENGINE effect: exactly one action, Rejected, against that line.
        var action = Assert.Single(c.ImsActions);
        Assert.Equal(lineId, action.LineId);
        Assert.Equal(ImsStatus.Rejected, action.Status);
        Assert.True(page.LastActionSucceeded);
        Assert.Equal("Rejected", page.Rows[0].Status);
        Assert.True(page.Rows[0].IsExplicit);
        Assert.Equal(1, page.RejectedCount);
        Assert.Equal(0, page.AcceptedCount);
    }

    /// <summary>Pending, then Clear — the decision reverts to the derived deemed-accept (no row is left behind).</summary>
    [Fact]
    public void Ims_pending_then_clear_reverts_the_line_to_deemed_accept()
    {
        var vm = NewRegularGstCompany("Ims Clear Co");
        var c = vm.Company!;
        Import2b(c, Line("SUP-INV-001"));
        vm.OpenImsActions();
        var page = vm.ImsActions!;

        Assert.True(page.Pending());
        Assert.Equal(ImsStatus.Pending, Assert.Single(c.ImsActions).Status);
        Assert.Equal("Pending", page.Rows[0].Status);

        Assert.True(page.Clear());
        Assert.Empty(c.ImsActions);
        Assert.Equal("Accepted (deemed)", page.Rows[0].Status);
        Assert.False(page.Rows[0].IsExplicit);
    }

    /// <summary>An Accept carrying a declared <b>partial</b> ITC reversal + remarks (the Oct-2025 credit-note rule)
    /// records both against the action.</summary>
    [Fact]
    public void Ims_accept_records_a_declared_partial_reversal_with_remarks()
    {
        var vm = NewRegularGstCompany("Ims Partial Co");
        var c = vm.Company!;
        Import2b(c, Line("SUP-CN-009"));
        vm.OpenImsActions();
        var page = vm.ImsActions!;

        page.DeclaredReversalText = "250.50";
        page.Remarks = "Partial reversal — goods short-received.";
        Assert.True(page.Accept());

        var action = Assert.Single(c.ImsActions);
        Assert.Equal(ImsStatus.Accepted, action.Status);
        Assert.Equal(25_050, action.DeclaredReversalPaisa);          // ₹250.50 in exact paisa
        Assert.Equal("Partial reversal — goods short-received.", action.Remarks);
        Assert.False(action.NoReversalDeclared);
        Assert.Contains("250.50", page.Message);
    }

    /// <summary>(c) Engine guard — a declared <b>partial</b> reversal <b>without remarks</b> is rejected by the engine's
    /// Oct-2025 invariant; the screen surfaces the message and writes NOTHING (validate-before-mutate).</summary>
    [Fact]
    public void Ims_partial_reversal_without_remarks_is_rejected_and_writes_nothing()
    {
        var vm = NewRegularGstCompany("Ims Partial Guard Co");
        var c = vm.Company!;
        Import2b(c, Line("SUP-CN-010"));
        vm.OpenImsActions();
        var page = vm.ImsActions!;

        page.DeclaredReversalText = "100.00";
        page.Remarks = string.Empty;                 // the engine requires remarks for a partial

        Assert.False(page.Accept());
        Assert.False(page.LastActionSucceeded);
        Assert.Contains("remarks", page.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(c.ImsActions);                  // nothing written — not even the Accept without the reversal
    }

    /// <summary>(c) Engine guard — an <b>RCM</b> line is NOT IMS-actionable: §3.3 supplies bypass the IMS dashboard and
    /// <c>ImsService.SetAction</c> throws for them. The screen must render the row as not-actionable and refuse the
    /// action with a message, never crash and never write.</summary>
    [Fact]
    public void Ims_rcm_line_is_not_actionable_and_the_guard_surfaces_cleanly()
    {
        var vm = NewRegularGstCompany("Ims Rcm Co");
        var c = vm.Company!;
        Import2b(c, Line("SUP-RCM-001", reverseCharge: true));
        vm.OpenImsActions();
        var page = vm.ImsActions!;

        var row = Assert.Single(page.Rows);
        Assert.False(row.IsActionable);
        Assert.Equal("Not actionable", row.Status);
        Assert.Contains("bypasses the IMS dashboard", row.Note);
        Assert.Equal(1, page.NotActionableCount);
        Assert.Contains("bypass IMS", page.StatusText);

        // Every action refuses cleanly — no exception escapes, and nothing is recorded.
        Assert.False(page.Accept());
        Assert.Contains("reverse-charge", page.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(page.Reject());
        Assert.False(page.Pending());
        Assert.False(page.Clear());
        Assert.Empty(c.ImsActions);

        // The engine really would have thrown — this is the guard the screen is standing in front of.
        Assert.Throws<InvalidOperationException>(() =>
            ImsService.SetAction(c, row.LineId, ImsStatus.Accepted));
    }

    /// <summary>An RCM line and a normal line in the same 2B: the RCM row is inert while the normal row still acts.</summary>
    [Fact]
    public void Ims_acts_on_the_normal_line_while_the_rcm_line_stays_inert()
    {
        var vm = NewRegularGstCompany("Ims Mixed Co");
        var c = vm.Company!;
        Import2b(c, Line("SUP-RCM-001", reverseCharge: true), Line("SUP-INV-002"));
        vm.OpenImsActions();
        var page = vm.ImsActions!;

        Assert.Equal(2, page.Rows.Count);
        Assert.Equal(1, page.NotActionableCount);

        // Highlight the RCM row (index 0) — refused.
        page.HighlightedIndex = 0;
        Assert.False(page.Reject());
        Assert.Empty(c.ImsActions);

        // Move to the normal row — accepted, and only that line gets the action.
        vm.MoveDown();
        Assert.Equal(1, page.HighlightedIndex);
        Assert.True(page.Reject());
        var action = Assert.Single(c.ImsActions);
        Assert.Equal(page.Rows[1].LineId, action.LineId);
    }

    /// <summary>A blank 2B: the screen shows a clean empty state and every action is a safe no-op.</summary>
    [Fact]
    public void Ims_shows_a_clean_empty_state_without_a_2b()
    {
        var vm = NewRegularGstCompany("Ims Empty Co");
        vm.OpenImsActions();
        var page = vm.ImsActions!;

        Assert.False(page.HasSnapshot);
        Assert.Empty(page.Rows);
        Assert.Empty(page.Snapshots);
        Assert.Equal(-1, page.HighlightedIndex);
        Assert.Contains("No GSTR-2B imported", page.Message);

        Assert.False(page.Accept());
        Assert.Contains("Highlight a 2B line", page.Message);
        Assert.Empty(vm.Company!.ImsActions);
    }

    // ================================================================ Screen 2: Run Set-Off (Rule 88A) & Pay

    /// <summary>(a) Opening the Run Set-Off screen PREVIEWS only — no set-off Journal and no set-off lines appear.</summary>
    [Fact]
    public void Setoff_opening_the_screen_previews_but_posts_nothing()
    {
        var vm = NewRegularGstCompany("SetOff Open Co");
        var c = vm.Company!;
        var vouchersBefore = c.Vouchers.Count;

        vm.OpenRunSetOff();
        Assert.Equal(Screen.RunSetOff, vm.CurrentScreen);
        var page = vm.RunSetOff!;

        // The preview: liability 90+90 fully covered by the 450+450 own-head credit ⇒ two lines, zero residual cash.
        Assert.Equal("2024-04", page.SelectedPeriod!.Period);
        Assert.Equal(2, page.Lines.Count);
        Assert.Equal(0, page.Allocation.TotalCash);
        Assert.Equal(18_000, page.Allocation.TotalCreditUtilised);   // ₹180 in paisa
        Assert.True(page.HasCreditLines);
        Assert.False(page.IsPosted);
        Assert.Contains("Preview", page.StatusText);

        // …and NOTHING was written by merely opening it.
        Assert.Equal(vouchersBefore, c.Vouchers.Count);
        Assert.Empty(c.GstSetoffLines);
    }

    /// <summary>(b) The explicit Run DOES post the Rule-88A set-off Journal + its Table-6.1 lines.</summary>
    [Fact]
    public void Setoff_explicit_run_posts_the_setoff_journal()
    {
        var vm = NewRegularGstCompany("SetOff Run Co");
        var c = vm.Company!;
        var vouchersBefore = c.Vouchers.Count;

        vm.OpenRunSetOff();
        var page = vm.RunSetOff!;
        Assert.True(page.PostSetOff());

        // The ENGINE effect: a set-off Journal exists for the period, with its per-head utilisation lines.
        Assert.Equal(vouchersBefore + 1, c.Vouchers.Count);
        Assert.NotEmpty(c.GstSetoffLines);
        Assert.All(c.GstSetoffLines, l => Assert.Equal("2024-04", l.Period));
        Assert.Equal(18_000, c.GstSetoffLines.Sum(l => l.AmountPaisa));   // ₹180 utilised

        Assert.True(page.LastActionSucceeded);
        Assert.True(page.IsPosted);
        Assert.Contains("Set-off run", page.Message);
        Assert.Contains("Nothing is payable in cash", page.Message);

        // Re-previewing after the posting still describes THE PERIOD, not the aftermath of its own posting: the
        // credit this period had available was ₹450 per head, and the preview must keep saying so. (Reading the raw
        // Input pool here would show ₹360 — the posting's own consumption — and then claim the ₹90 liability had
        // become payable in cash, double-counting the very set-off just posted.)
        Assert.Equal("450.00", page.CreditCgstText);
        Assert.Equal(0, page.Allocation.TotalCash);
    }

    /// <summary>
    /// The set-off is <b>idempotent per period</b>: running it a second time must produce the <b>same</b> allocation
    /// and replace the posting in place — never stack a second Journal, and never shrink to a smaller allocation
    /// because the first run had already drawn the pool down.
    /// </summary>
    [Fact]
    public void Setoff_running_twice_is_idempotent_and_replaces_the_posting()
    {
        var vm = NewRegularGstCompany("SetOff Idempotent Co");
        var c = vm.Company!;

        vm.OpenRunSetOff();
        var page = vm.RunSetOff!;
        Assert.True(page.PostSetOff());
        var afterFirst = c.Vouchers.Count;
        var linesAfterFirst = c.GstSetoffLines.Count;
        var utilisedAfterFirst = c.GstSetoffLines.Sum(l => l.AmountPaisa);

        // The second run sees the SAME demand and posts the SAME allocation, replacing the first Journal in place.
        Assert.True(page.PostSetOff());
        Assert.Equal(afterFirst, c.Vouchers.Count);
        Assert.Equal(linesAfterFirst, c.GstSetoffLines.Count);
        Assert.Equal(utilisedAfterFirst, c.GstSetoffLines.Sum(l => l.AmountPaisa));
        Assert.Equal(18_000, c.GstSetoffLines.Sum(l => l.AmountPaisa));
        Assert.All(c.GstSetoffLines, l => Assert.Equal("2024-04", l.Period));
    }

    /// <summary>
    /// The cash residual cannot be paid <b>twice</b>. The engine's cash discharge carries no period key of its own, so
    /// nothing in it stops a second identical payment — the screen nets the period's already-discharged cash out of
    /// what it still asks for, and refuses once the period is settled.
    /// </summary>
    [Fact]
    public void Setoff_residual_cash_cannot_be_discharged_twice()
    {
        var vm = NewRegularGstCompany("SetOff Double Pay Co");
        var c = vm.Company!;
        new GstReversalService(c).PostReversal(
            ItcReversalRule.Rule42, "2024-04",
            new GstReversalService.ReversalAmount(CgstPaisa: 40_000, SgstPaisa: 40_000, IgstPaisa: 0, CessPaisa: 0),
            new DateOnly(2024, 4, 30));
        Add(c, "HDFC Bank", "Bank Accounts", true);
        _storage.Save(c);

        vm.OpenRunSetOff();
        var page = vm.RunSetOff!;

        // Fund BOTH heads with DOUBLE the residual (₹80 each against a ₹40 need) — so a second payment would be
        // affordable, and only the period-netting can stop it.
        foreach (var head in new[] { GstTaxHead.Central, GstTaxHead.State })
        {
            page.ChallanHead = head;
            page.ChallanAmountText = "80.00";
            page.Cpin = "24CPIN00000001000";
            page.Cin = "CIN" + head;
            Assert.True(page.DepositChallan());
        }

        Assert.True(page.PostSetOff());
        Assert.True(page.PayResidualCash());
        var vouchersAfterPaying = c.Vouchers.Count;

        // The period now shows nothing further due, and a second attempt is refused — despite the cash being there.
        Assert.All(page.CashCells, x => Assert.Equal("0.00", x.Required));
        Assert.False(page.PayResidualCash());
        Assert.Contains("already been discharged", page.Message);
        Assert.Equal(vouchersAfterPaying, c.Vouchers.Count);

        // The engine's cash ledger confirms exactly ONE discharge happened: ₹80 deposited − ₹40 drawn = ₹40 left.
        var deposit = new GstDepositService(c);
        Assert.Equal(40m, deposit.AvailableCash(GstTaxHead.Central, GstMinorHead.Tax).Amount);
        Assert.Equal(40m, deposit.AvailableCash(GstTaxHead.State, GstMinorHead.Tax).Amount);
    }

    /// <summary>(c) Engine guard — the residual cash cannot be discharged out of an <b>underfunded</b> electronic cash
    /// ledger. The screen refuses with a message naming the shortfall and discharges NOTHING (all-or-nothing).</summary>
    [Fact]
    public void Setoff_paying_residual_cash_without_a_challan_is_refused_and_pays_nothing()
    {
        var vm = NewRegularGstCompany("SetOff Underfunded Co");
        var c = vm.Company!;

        // Reverse ₹400 of each ₹450 ITC pool ⇒ ₹50 credit vs ₹90 liability per head ⇒ ₹40 per head falls to CASH.
        new GstReversalService(c).PostReversal(
            ItcReversalRule.Rule42, "2024-04",
            new GstReversalService.ReversalAmount(CgstPaisa: 40_000, SgstPaisa: 40_000, IgstPaisa: 0, CessPaisa: 0),
            new DateOnly(2024, 4, 30));
        _storage.Save(c);

        vm.OpenRunSetOff();
        var page = vm.RunSetOff!;
        Assert.Equal(8_000, page.Allocation.TotalCash);       // ₹80 payable in cash
        Assert.All(page.CashCells.Where(x => x.Required != "0.00"), x => Assert.False(x.IsFunded));

        var vouchersBefore = c.Vouchers.Count;
        Assert.False(page.PayResidualCash());
        Assert.False(page.LastActionSucceeded);
        Assert.Contains("underfunded", page.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PMT-06", page.Message);
        Assert.Equal(vouchersBefore, c.Vouchers.Count);       // nothing discharged — not even the funded head
    }

    /// <summary>(b) The full happy path: deposit a PMT-06 challan, then discharge the residual cash out of it.</summary>
    [Fact]
    public void Setoff_deposit_challan_then_pay_residual_cash_discharges_the_liability()
    {
        var vm = NewRegularGstCompany("SetOff Pay Co");
        var c = vm.Company!;
        new GstReversalService(c).PostReversal(
            ItcReversalRule.Rule42, "2024-04",
            new GstReversalService.ReversalAmount(CgstPaisa: 40_000, SgstPaisa: 40_000, IgstPaisa: 0, CessPaisa: 0),
            new DateOnly(2024, 4, 30));
        Add(c, "HDFC Bank", "Bank Accounts", true);
        _storage.Save(c);

        vm.OpenRunSetOff();
        var page = vm.RunSetOff!;
        Assert.NotEmpty(page.BankOptions);

        // Fund BOTH cash cells (₹40 each) with a PMT-06 challan.
        foreach (var head in new[] { GstTaxHead.Central, GstTaxHead.State })
        {
            page.ChallanHead = head;
            page.ChallanAmountText = "40.00";
            page.Cpin = "24CPIN00000001000";
            page.Cin = "CIN" + head;
            Assert.True(page.DepositChallan());
        }
        Assert.All(page.CashCells.Where(x => x.Required != "0.00"), x => Assert.True(x.IsFunded));

        // Run the set-off, then discharge the ₹80 residual out of the cash ledger.
        Assert.True(page.PostSetOff());
        Assert.True(page.PayResidualCash());
        Assert.True(page.LastActionSucceeded);
        Assert.Contains("discharged", page.Message);

        // The ENGINE effect: both cash cells are drained back to zero by the discharge.
        var deposit = new GstDepositService(c);
        Assert.Equal(0m, deposit.AvailableCash(GstTaxHead.Central, GstMinorHead.Tax).Amount);
        Assert.Equal(0m, deposit.AvailableCash(GstTaxHead.State, GstMinorHead.Tax).Amount);
    }

    /// <summary>(c) Engine guard — a PMT-06 <b>without a CIN</b> is refused: the electronic cash ledger is credited
    /// only once the bank confirms the payment, so a CPIN alone (an unpaid intent) must not fund it.</summary>
    [Fact]
    public void Setoff_challan_without_a_cin_is_refused_and_funds_nothing()
    {
        var vm = NewRegularGstCompany("SetOff No Cin Co");
        var c = vm.Company!;
        Add(c, "HDFC Bank", "Bank Accounts", true);
        _storage.Save(c);

        vm.OpenRunSetOff();
        var page = vm.RunSetOff!;
        page.ChallanHead = GstTaxHead.Central;
        page.ChallanAmountText = "500.00";
        page.Cpin = "24CPIN00000001000";
        page.Cin = string.Empty;                 // never confirmed by the bank

        Assert.False(page.DepositChallan());
        Assert.False(page.LastActionSucceeded);
        Assert.NotNull(page.Message);
        Assert.Equal(0m, new GstDepositService(c).AvailableCash(GstTaxHead.Central, GstMinorHead.Tax).Amount);
    }

    /// <summary>A non-numeric challan amount is rejected by the screen before it ever reaches the engine.</summary>
    [Fact]
    public void Setoff_challan_with_a_bad_amount_is_rejected()
    {
        var vm = NewRegularGstCompany("SetOff Bad Amount Co");
        Add(vm.Company!, "HDFC Bank", "Bank Accounts", true);
        _storage.Save(vm.Company!);

        vm.OpenRunSetOff();
        var page = vm.RunSetOff!;
        page.ChallanAmountText = "not-a-number";
        page.Cin = "CIN1";

        Assert.False(page.DepositChallan());
        Assert.Contains("not a valid rupee amount", page.Message);
    }

    /// <summary>ER-13: a Composition / GST-off company never reaches the Run Set-Off screen.</summary>
    [Fact]
    public void Setoff_screen_is_gated_off_for_composition_and_gst_off_companies()
    {
        var comp = NewSeededCompany("SetOff Gated Comp Co");
        EnableGst(comp.Company!, GstRegistrationType.Composition);
        _storage.Save(comp.Company!);
        comp.ShowGateway();
        comp.OpenRunSetOff();
        Assert.Null(comp.RunSetOff);
        Assert.Equal(Screen.Gateway, comp.CurrentScreen);

        var off = NewSeededCompany("SetOff Gated Off Co");
        _storage.Save(off.Company!);
        off.ShowGateway();
        off.OpenRunSetOff();
        Assert.Null(off.RunSetOff);
        Assert.Equal(Screen.Gateway, off.CurrentScreen);
    }

    // ================================================================ Screen 3: Post ITC Reversal

    /// <summary>(a) Opening the Post-ITC-Reversal screen posts NOTHING — it only projects the (zero) ECRS balance.</summary>
    [Fact]
    public void Reversal_opening_the_screen_posts_nothing()
    {
        var vm = NewRegularGstCompany("Reversal Open Co");
        var c = vm.Company!;
        var vouchersBefore = c.Vouchers.Count;

        vm.OpenPostItcReversal();
        Assert.Equal(Screen.PostItcReversal, vm.CurrentScreen);
        var page = vm.PostItcReversal!;

        Assert.Equal("0.00", page.BalanceTotalText);
        Assert.Empty(page.Posted);
        Assert.Empty(c.ItcReversals);
        Assert.Equal(vouchersBefore, c.Vouchers.Count);
        Assert.Contains("ECRS balance", page.StatusText);
    }

    /// <summary>(b) The explicit Post DOES write a Rule-42 reversal row (with its D1/D2 apportionment).</summary>
    [Fact]
    public void Reversal_explicit_rule42_post_writes_the_reversal_row()
    {
        var vm = NewRegularGstCompany("Reversal Rule42 Co");
        var c = vm.Company!;

        vm.OpenPostItcReversal();
        var page = vm.PostItcReversal!;
        page.Kind = ItcReversalPostKind.Rule42;
        page.Period = "2024-04";
        page.CgstText = "450.00";                 // the whole CGST ITC pool as common credit (C2)
        page.SgstText = "450.00";
        page.ExemptTurnoverText = "1000.00";      // E
        page.TotalTurnoverText = "5000.00";       // F ⇒ D1 = C2 × E/F = 20%, D2 = 5%

        Assert.True(page.Post());
        Assert.True(page.LastActionSucceeded);

        // The ENGINE effect: one Rule-42 row for the period, with the apportioned amount (D1 90 + D2 22.50 per head).
        var row = Assert.Single(c.ItcReversals);
        Assert.Equal(ItcReversalRule.Rule42, row.Rule);
        Assert.Equal("2024-04", row.Period);
        Assert.Equal(11_250, row.CgstPaisa);      // ₹90.00 (D1) + ₹22.50 (D2) = ₹112.50
        Assert.Equal(11_250, row.SgstPaisa);
        Assert.Equal(Table4bBucket.Table4B1, row.Table4bBucket);
        Assert.Single(page.Posted);
        Assert.Equal("Rule 42", page.Posted[0].Rule);
        Assert.False(page.Posted[0].IsReclaimable);   // an apportionment is permanent
        Assert.Contains("Rule 42 reversal posted", page.Message);
    }

    /// <summary>(c) Engine guard — Rule 42 needs a positive total turnover; a zero F is refused and nothing is written.</summary>
    [Fact]
    public void Reversal_rule42_with_zero_total_turnover_is_refused()
    {
        var vm = NewRegularGstCompany("Reversal Bad Basis Co");
        var c = vm.Company!;

        vm.OpenPostItcReversal();
        var page = vm.PostItcReversal!;
        page.Kind = ItcReversalPostKind.Rule42;
        page.Period = "2024-04";
        page.CgstText = "450.00";
        page.ExemptTurnoverText = "1000.00";
        page.TotalTurnoverText = "0";              // F = 0 ⇒ the apportionment is undefined

        Assert.False(page.Post());
        Assert.False(page.LastActionSucceeded);
        Assert.NotNull(page.Message);
        Assert.Empty(c.ItcReversals);
    }

    /// <summary>A malformed period is rejected by the screen before the engine is ever called.</summary>
    [Fact]
    public void Reversal_malformed_period_is_rejected()
    {
        var vm = NewRegularGstCompany("Reversal Bad Period Co");
        vm.OpenPostItcReversal();
        var page = vm.PostItcReversal!;
        page.Period = "April-2024";
        page.CgstText = "100.00";

        Assert.False(page.Post());
        Assert.Contains("not a valid return period", page.Message);
        Assert.Empty(vm.Company!.ItcReversals);
    }

    /// <summary>(b) A Rule-37 reversal is posted and shows as <b>reclaimable</b>; it raises the ECRS balance.</summary>
    [Fact]
    public void Reversal_rule37_post_is_reclaimable_and_raises_the_ecrs_balance()
    {
        var vm = NewRegularGstCompany("Reversal Rule37 Co");
        var c = vm.Company!;

        vm.OpenPostItcReversal();
        var page = vm.PostItcReversal!;
        page.Kind = ItcReversalPostKind.Rule37;
        page.Period = "2024-10";
        page.CgstText = "100.00";
        page.SgstText = "100.00";
        // The reversal is keyed to the purchase it reverses, so the supplier's purchase is NAMED — the screen no
        // longer guesses it from whatever party voucher happens to come first (see FIX 1).
        page.SelectedSourceIndex = 0;

        Assert.True(page.Post());
        var row = Assert.Single(c.ItcReversals);
        Assert.Equal(ItcReversalRule.Rule37, row.Rule);
        Assert.Equal(Table4bBucket.Table4B2, row.Table4bBucket);   // 4(B)(2) — the reclaimable bucket

        // The ECRS balance now tracks the reclaimable ₹200, and the row offers the reclaim.
        Assert.Equal("200.00", page.BalanceTotalText);
        Assert.Equal("100.00", page.BalanceCgstText);
        Assert.True(page.Posted[0].IsReclaimable);
    }

    /// <summary>(b) The explicit Reclaim writes the re-credit row against the Rule-37 reversal.</summary>
    [Fact]
    public void Reversal_reclaim_of_a_rule37_row_posts_the_recredit()
    {
        var vm = NewRegularGstCompany("Reversal Reclaim Co");
        var c = vm.Company!;

        vm.OpenPostItcReversal();
        var page = vm.PostItcReversal!;
        page.Kind = ItcReversalPostKind.Rule37;
        page.Period = "2024-10";
        page.CgstText = "100.00";
        page.SgstText = "100.00";
        page.SelectedSourceIndex = 0;                 // name the supplier's purchase (FIX 1)
        Assert.True(page.Post());
        var reversalId = c.ItcReversals.Single().Id;

        // Reclaim the whole ₹200 in a later period (the supplier was paid).
        page.HighlightedIndex = page.Posted.ToList().FindIndex(r => r.IsReclaimable);
        page.Period = "2024-11";
        page.CgstText = "100.00";
        page.SgstText = "100.00";
        Assert.True(page.Reclaim());

        // The ENGINE effect: a reclaim row pointing at the reversal, in the 4(D)(1) bucket, and the ECRS goes to zero.
        var reclaim = Assert.Single(c.ItcReversals, r => r.ReclaimOfId is not null);
        Assert.Equal(reversalId, reclaim.ReclaimOfId);
        Assert.Equal("2024-11", reclaim.Period);
        Assert.Equal(Table4bBucket.Table4D1, reclaim.Table4bBucket);
        Assert.Equal("0.00", page.BalanceTotalText);
        Assert.Contains("Reclaimed", page.Message);

        // …and the row is no longer reclaimable, so it cannot be reclaimed twice.
        Assert.All(page.Posted, r => Assert.False(r.IsReclaimable));
    }

    /// <summary>
    /// (c) <b>The ECRS cap</b> — a reclaim may never exceed the tracked outstanding reversal balance. The engine
    /// refuses it per head; the screen surfaces that refusal cleanly and writes nothing.
    /// </summary>
    [Fact]
    public void Reversal_reclaim_over_the_ecrs_cap_is_rejected_and_writes_nothing()
    {
        var vm = NewRegularGstCompany("Reversal Cap Co");
        var c = vm.Company!;

        vm.OpenPostItcReversal();
        var page = vm.PostItcReversal!;
        page.Kind = ItcReversalPostKind.Rule37;
        page.Period = "2024-10";
        page.CgstText = "100.00";
        page.SelectedSourceIndex = 0;                 // name the supplier's purchase (FIX 1)
        Assert.True(page.Post());
        Assert.Equal("100.00", page.BalanceTotalText);

        var rowsBefore = c.ItcReversals.Count;

        // Try to reclaim ₹500 against a ₹100 tracked balance — over the cap.
        page.HighlightedIndex = page.Posted.ToList().FindIndex(r => r.IsReclaimable);
        page.Period = "2024-11";
        page.CgstText = "500.00";
        page.SgstText = string.Empty;

        Assert.False(page.Reclaim());
        Assert.False(page.LastActionSucceeded);
        Assert.Contains("ECRS", page.Message);
        Assert.Equal(rowsBefore, c.ItcReversals.Count);          // nothing written
        Assert.DoesNotContain(c.ItcReversals, r => r.ReclaimOfId is not null);
        Assert.Equal("100.00", page.BalanceTotalText);           // the balance is untouched
    }

    /// <summary>(c) Engine guard — a Rule-42 apportionment is <b>permanent</b>: it is not reclaimable, and the screen
    /// refuses the reclaim without ever calling the engine.</summary>
    [Fact]
    public void Reversal_a_rule42_apportionment_cannot_be_reclaimed()
    {
        var vm = NewRegularGstCompany("Reversal Perm Co");
        var c = vm.Company!;

        vm.OpenPostItcReversal();
        var page = vm.PostItcReversal!;
        page.Kind = ItcReversalPostKind.Rule42;
        page.Period = "2024-04";
        page.CgstText = "450.00";
        page.ExemptTurnoverText = "1000.00";
        page.TotalTurnoverText = "5000.00";
        Assert.True(page.Post());

        page.HighlightedIndex = 0;
        Assert.False(page.Posted[0].IsReclaimable);
        Assert.False(page.Reclaim());
        Assert.Contains("Only an un-reclaimed Rule 37 / 37A reversal", page.Message);
        Assert.DoesNotContain(c.ItcReversals, r => r.ReclaimOfId is not null);
    }

    /// <summary>ER-13: a Composition / GST-off company never reaches the Post-ITC-Reversal screen.</summary>
    [Fact]
    public void Reversal_screen_is_gated_off_for_composition_and_gst_off_companies()
    {
        var comp = NewSeededCompany("Reversal Gated Comp Co");
        EnableGst(comp.Company!, GstRegistrationType.Composition);
        _storage.Save(comp.Company!);
        comp.ShowGateway();
        comp.OpenPostItcReversal();
        Assert.Null(comp.PostItcReversal);
        Assert.Equal(Screen.Gateway, comp.CurrentScreen);

        var off = NewSeededCompany("Reversal Gated Off Co");
        _storage.Save(off.Company!);
        off.ShowGateway();
        off.OpenPostItcReversal();
        Assert.Null(off.PostItcReversal);
        Assert.Equal(Screen.Gateway, off.CurrentScreen);
    }

    // ================================================================ Screen 4: Import GSTR-2B

    /// <summary>A minimal well-formed portal GSTR-2B JSON addressed to <paramref name="gstin"/>.</summary>
    private static byte[] Portal2bJson(string gstin) => System.Text.Encoding.UTF8.GetBytes($$"""
    {
      "gstin": "{{gstin}}",
      "rtnprd": "042024",
      "gendt": "14-05-2024",
      "itcsumm": { "igst": 0, "cgst": 450.00, "sgst": 450.00, "cess": 0 },
      "docdata": {
        "b2b": [
          {
            "ctin": "{{GstinSupplier}}",
            "trdnm": "Local Supplier",
            "inv": [
              {
                "inum": "SUP-INV-001",
                "dt": "03-04-2024",
                "val": 5900.00,
                "pos": "27",
                "rev": "N",
                "itms": [
                  { "txval": 5000.00, "iamt": 0, "camt": 450.00, "samt": 450.00, "csamt": 0 }
                ]
              }
            ]
          }
        ]
      }
    }
    """);

    /// <summary>Builds the Import screen with an injected read seam + a fixed clock (no disk, deterministic).</summary>
    private ImportGstr2bViewModel NewImportVm(Company c, byte[] bytes) =>
        new(c, _storage, onChanged: null,
            readBytes: _ => bytes,
            now: () => new DateTimeOffset(2024, 5, 14, 0, 0, 0, TimeSpan.Zero));

    /// <summary>(a) Opening the Import screen imports NOTHING.</summary>
    [Fact]
    public void Import2b_opening_the_screen_imports_nothing()
    {
        var vm = NewRegularGstCompany("Import Open Co");
        var c = vm.Company!;

        vm.OpenImportGstr2b();
        Assert.Equal(Screen.ImportGstr2b, vm.CurrentScreen);
        var page = vm.ImportGstr2b!;

        Assert.Empty(page.Imported);
        Assert.Empty(c.Gstr2bSnapshots);
        Assert.Contains("No statement imported yet", page.StatusText);
        Assert.Contains(GstinMaharashtra, page.Subtitle);
    }

    /// <summary>(b) The explicit Import DOES materialise the parsed snapshot into the company.</summary>
    [Fact]
    public void Import2b_explicit_import_materialises_the_snapshot()
    {
        var vm = NewRegularGstCompany("Import Ok Co");
        var c = vm.Company!;
        var page = NewImportVm(c, Portal2bJson(GstinMaharashtra));
        page.FilePath = @"C:\portal\gstr2b-042024.json";

        Assert.True(page.Import());
        Assert.True(page.LastImportSucceeded);
        Assert.Null(page.ErrorCode);

        // The ENGINE effect: one 2B snapshot for 2024-04 carrying the supplier's line.
        var snap = Assert.Single(c.Gstr2bSnapshots);
        Assert.Equal(GstStatementType.Gstr2b, snap.StatementType);
        Assert.Equal("2024-04", snap.ReturnPeriod);
        Assert.Equal(45_000, snap.SummaryCgstPaisa);
        var line = Assert.Single(snap.Lines);
        Assert.Equal(GstinSupplier, line.SupplierGstin);
        Assert.Equal("SUP-INV-001", line.DocNumber);
        Assert.Equal(500_000, line.TaxableValuePaisa);
        Assert.False(line.ReverseCharge);

        Assert.Single(page.Imported);
        Assert.Equal("GSTR-2B", page.Imported[0].Statement);
        Assert.Contains("Imported GSTR-2B 2024-04", page.Message);

        // …and the snapshot is immediately visible to the screens that read it (the 2B recon pairs it to the books).
        vm.OpenGstr2bReconReport();
        Assert.True(vm.Gstr2bReconReport!.HasSnapshot);
        Assert.Equal(1, vm.Gstr2bReconReport.MatchedCount);
    }

    /// <summary>
    /// (c) Engine guard — a statement addressed to a <b>different GSTIN</b> is rejected <b>all-or-nothing</b>: the
    /// parser returns <c>GSTIN_MISMATCH</c> rather than a partial statement, and nothing is imported.
    /// </summary>
    [Fact]
    public void Import2b_a_wrong_gstin_file_is_rejected_all_or_nothing()
    {
        var vm = NewRegularGstCompany("Import Wrong Gstin Co");
        var c = vm.Company!;
        var page = NewImportVm(c, Portal2bJson("29AAACC1206D1ZM"));   // someone else's 2B (state 29)
        page.FilePath = @"C:\portal\someone-else.json";

        Assert.False(page.Import());
        Assert.False(page.LastImportSucceeded);
        Assert.Equal("GSTIN_MISMATCH", page.ErrorCode);
        Assert.Contains("addressed to a different GSTIN", page.Message);
        Assert.Contains("Nothing was imported", page.Message);

        Assert.Empty(c.Gstr2bSnapshots);      // not one line leaked in
        Assert.Empty(page.Imported);
    }

    /// <summary>(c) Engine guard — a malformed JSON file is rejected all-or-nothing, never crashing.</summary>
    [Fact]
    public void Import2b_a_malformed_file_is_rejected_all_or_nothing()
    {
        var vm = NewRegularGstCompany("Import Malformed Co");
        var c = vm.Company!;
        var page = NewImportVm(c, System.Text.Encoding.UTF8.GetBytes("{ this is not json ]["));
        page.FilePath = @"C:\portal\broken.json";

        Assert.False(page.Import());
        Assert.Equal("MALFORMED_JSON", page.ErrorCode);
        Assert.Contains("not valid JSON", page.Message);
        Assert.Empty(c.Gstr2bSnapshots);
    }

    /// <summary>An unreadable file surfaces cleanly as a message, not an exception.</summary>
    [Fact]
    public void Import2b_an_unreadable_file_is_reported_not_thrown()
    {
        var vm = NewRegularGstCompany("Import Unreadable Co");
        var c = vm.Company!;
        var page = new ImportGstr2bViewModel(c, _storage, onChanged: null,
            readBytes: _ => throw new FileNotFoundException("No such file."),
            now: () => DateTimeOffset.UnixEpoch);
        page.FilePath = @"C:\portal\missing.json";

        Assert.False(page.Import());
        Assert.Contains("Could not read the file", page.Message);
        Assert.Empty(c.Gstr2bSnapshots);
    }

    /// <summary>A blank path is refused before anything is read.</summary>
    [Fact]
    public void Import2b_a_blank_path_is_refused()
    {
        var vm = NewRegularGstCompany("Import Blank Co");
        vm.OpenImportGstr2b();
        var page = vm.ImportGstr2b!;

        Assert.False(page.Import());
        Assert.Contains("Enter the path", page.Message);
        Assert.Empty(vm.Company!.Gstr2bSnapshots);
    }

    /// <summary>ER-13: a Composition / GST-off company never reaches the Import screen.</summary>
    [Fact]
    public void Import2b_screen_is_gated_off_for_composition_and_gst_off_companies()
    {
        var comp = NewSeededCompany("Import Gated Comp Co");
        EnableGst(comp.Company!, GstRegistrationType.Composition);
        _storage.Save(comp.Company!);
        comp.ShowGateway();
        comp.OpenImportGstr2b();
        Assert.Null(comp.ImportGstr2b);
        Assert.Equal(Screen.Gateway, comp.CurrentScreen);

        var off = NewSeededCompany("Import Gated Off Co");
        _storage.Save(off.Company!);
        off.ShowGateway();
        off.OpenImportGstr2b();
        Assert.Null(off.ImportGstr2b);
        Assert.Equal(Screen.Gateway, off.CurrentScreen);
    }

    // ================================================================ Screen 5: Generate e-Invoice

    /// <summary>Switches on e-invoicing from the FY start with the applicability override, so the seeded B2B sale is
    /// Covered regardless of turnover (the AATO threshold is not what these UI tests are about).</summary>
    private static void EnableEInvoicing(Company c)
    {
        c.Gst!.EInvoicingEnabled = true;
        c.Gst.EInvoiceApplicableFrom = FyStart;
        c.Gst.EInvoiceApplicabilityOverride = true;
    }

    /// <summary>Switches on e-Way billing from the FY start (the ₹50,000 threshold stays the engine's own).</summary>
    private static void EnableEWay(Company c)
    {
        c.Gst!.EWayBillEnabled = true;
        c.Gst.EWayApplicableFrom = FyStart;
    }

    /// <summary>Builds the e-Invoice screen with an injected folder + "today" + write seam (deterministic, diskless).</summary>
    private GenerateEInvoiceViewModel NewEInvoiceVm(
        Company c, System.Collections.Generic.IDictionary<string, byte[]> written, DateOnly? today = null) =>
        new(c, _storage, onChanged: null, folder: @"C:\out",
            today: today ?? SaleDate, writeBytes: (p, b) => written[p] = b);

    /// <summary>(a) Opening the e-Invoice screen prepares NOTHING — no record is raised merely by looking.</summary>
    [Fact]
    public void Einvoice_opening_the_screen_prepares_nothing()
    {
        var vm = NewRegularGstCompany("EInv Open Co");
        var c = vm.Company!;
        EnableEInvoicing(c);

        vm.OpenGenerateEInvoice();
        Assert.Equal(Screen.GenerateEInvoice, vm.CurrentScreen);
        var page = vm.GenerateEInvoice!;

        Assert.NotEmpty(page.Rows);
        Assert.Empty(c.EInvoiceRecords);                       // nothing prepared
        Assert.All(page.Rows, r => Assert.False(r.HasRecord));
        Assert.All(page.Rows, r => Assert.Equal("—", r.Status));
        Assert.True(vm.IsGenerateEInvoiceScreen);
    }

    /// <summary>(b) The explicit Prepare DOES raise the record and write the offline INV-01 JSON.</summary>
    [Fact]
    public void Einvoice_explicit_prepare_raises_the_record_and_writes_the_inv01_json()
    {
        var vm = NewRegularGstCompany("EInv Prepare Co");
        var c = vm.Company!;
        EnableEInvoicing(c);
        var written = new System.Collections.Generic.Dictionary<string, byte[]>();
        var page = NewEInvoiceVm(c, written);

        // The B2B sale is the covered voucher.
        var idx = page.Rows.ToList().FindIndex(r => r.IsCovered);
        Assert.True(idx >= 0, "the seeded B2B sale should be Covered");
        page.HighlightedIndex = idx;

        Assert.True(page.PrepareAndWriteJson());
        Assert.True(page.LastActionSucceeded);

        // The ENGINE effect: a Pending record against the sale, and the INV-01 bytes on the (fake) disk.
        var record = Assert.Single(c.EInvoiceRecords);
        Assert.Equal(EInvoiceStatus.Pending, record.Status);
        Assert.Null(record.Irn);                               // no IRN until the portal issues one
        var file = Assert.Single(written);
        Assert.StartsWith(@"C:\out\INV01_", file.Key);
        Assert.NotEmpty(file.Value);
        Assert.Equal(file.Key, page.LastJsonPath);
        Assert.Contains("INV-01 written", page.Message);
    }

    /// <summary>(b) The IRP's IRN / Ack / QR are recorded back against the prepared record (copied, never derived).</summary>
    [Fact]
    public void Einvoice_recording_the_irp_response_generates_the_einvoice()
    {
        var vm = NewRegularGstCompany("EInv Irn Co");
        var c = vm.Company!;
        EnableEInvoicing(c);
        var written = new System.Collections.Generic.Dictionary<string, byte[]>();
        var page = NewEInvoiceVm(c, written);
        page.HighlightedIndex = page.Rows.ToList().FindIndex(r => r.IsCovered);
        Assert.True(page.PrepareAndWriteJson());

        var irn = new string('a', 64);
        page.Irn = irn;
        page.AckNo = "112400001";
        page.AckDateText = SaleDate.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);
        page.SignedQr = "signed-qr-blob";
        Assert.True(page.RecordIrpResponse());

        // The ENGINE effect: the record is Generated and carries exactly what the portal returned.
        var record = Assert.Single(c.EInvoiceRecords);
        Assert.Equal(EInvoiceStatus.Generated, record.Status);
        Assert.Equal(irn, record.Irn);
        Assert.Equal("112400001", record.AckNo);
        Assert.Equal("signed-qr-blob", record.SignedQr);
        Assert.Contains("IRN recorded", page.Message);

        // (c) Engine guard — a Generated record is immutable: a second IRN cannot overwrite it.
        page.Irn = new string('b', 64);
        Assert.False(page.RecordIrpResponse());
        Assert.False(page.LastActionSucceeded);
        Assert.Equal(irn, c.EInvoiceRecords.Single().Irn);     // the original IRN is untouched
    }

    /// <summary>Posts a ₹1,000 <b>B2C</b> sale (an unregistered walk-in customer). A domestic B2C supply is
    /// <b>Excluded</b> from e-invoicing — it takes the self-generated B2C-QR path, never the IRP — yet it is still
    /// LISTED, which is exactly what the coverage guard needs as a subject.</summary>
    private static Voucher AddB2cSale(Company c)
    {
        var sales = c.FindLedgerByName("Sales")!;
        var walkIn = Add(c, "Walk-in Customer", "Sundry Debtors", true);
        walkIn.PartyGst = new PartyGstDetails
        { RegistrationType = GstRegistrationType.Unregistered, StateCode = "27" };

        var tax = new GstService(c).ComputeInvoiceTax(
            new[] { new GstService.TaxableLine(Money.FromRupees(1000m), 1800) }, false, GstTaxDirection.Output);
        var lines = new List<EntryLine>
        {
            new(walkIn.Id, Money.FromRupees(1180m), DrCr.Debit),
            new(sales.Id, Money.FromRupees(1000m), DrCr.Credit),
        };
        lines.AddRange(tax.TaxLines);
        return new LedgerService(c).Post(new Voucher(
            Guid.NewGuid(), c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id,
            SaleDate, lines, number: 2, partyId: walkIn.Id));
    }

    /// <summary>
    /// (c) Engine guard — a voucher that is <b>not covered</b> cannot be prepared, and no record is raised.
    /// <para>(FIX 8) This used to assert NOTHING: every listed row in the old fixture was Covered, so the
    /// <c>if (idx &lt; 0) return;</c> bail fired before a single assertion and the guard on the MUTATING Prepare path
    /// was entirely unverified. The fixture now carries a real uncovered listed row (a B2C sale) and the bail is
    /// gone — break the guard and this goes red.</para>
    /// </summary>
    [Fact]
    public void Einvoice_an_uncovered_voucher_cannot_be_prepared()
    {
        var vm = NewRegularGstCompany("EInv Uncovered Co");
        var c = vm.Company!;
        EnableEInvoicing(c);
        var b2c = AddB2cSale(c);
        var written = new System.Collections.Generic.Dictionary<string, byte[]>();
        var page = NewEInvoiceVm(c, written);

        var idx = page.Rows.ToList().FindIndex(r => r.VoucherId == b2c.Id);
        Assert.True(idx >= 0, "the B2C sale should be listed");
        Assert.False(page.Rows[idx].IsCovered);          // ...and genuinely uncovered
        page.HighlightedIndex = idx;

        Assert.False(page.PrepareAndWriteJson());
        Assert.False(page.LastActionSucceeded);
        Assert.NotNull(page.Message);
        Assert.Empty(c.EInvoiceRecords);
        Assert.Empty(written);
    }

    /// <summary>(c) Engine guard — a document number is <b>never reusable</b>: a second prepare against the same
    /// voucher is refused and no duplicate record appears.</summary>
    [Fact]
    public void Einvoice_a_document_number_cannot_be_prepared_twice()
    {
        var vm = NewRegularGstCompany("EInv Dup Co");
        var c = vm.Company!;
        EnableEInvoicing(c);
        var written = new System.Collections.Generic.Dictionary<string, byte[]>();
        var page = NewEInvoiceVm(c, written);
        page.HighlightedIndex = page.Rows.ToList().FindIndex(r => r.IsCovered);
        Assert.True(page.PrepareAndWriteJson());

        Assert.False(page.PrepareAndWriteJson());
        Assert.False(page.LastActionSucceeded);
        Assert.NotNull(page.Message);
        Assert.Single(c.EInvoiceRecords);                      // still exactly one
    }

    /// <summary>(FIX 3) An IRP rejection is recorded against the prepared record — and without an error code the
    /// screen refuses clearly. (The affordance itself is locked by <see cref="GstActionsUiBindingTests"/>: this guard
    /// was unreachable because no XAML control bound <c>ErrorCode</c>.)</summary>
    [Fact]
    public void Einvoice_recording_a_failure_needs_the_error_code_and_records_it()
    {
        var vm = NewRegularGstCompany("EInv Failure Co");
        var c = vm.Company!;
        EnableEInvoicing(c);
        var written = new System.Collections.Generic.Dictionary<string, byte[]>();
        var page = NewEInvoiceVm(c, written);
        page.HighlightedIndex = page.Rows.ToList().FindIndex(r => r.IsCovered);
        Assert.True(page.PrepareAndWriteJson());

        // No error code ⇒ a clear refusal, and the record is left Pending.
        page.ErrorCode = string.Empty;
        Assert.False(page.RecordFailure());
        Assert.Contains("error code", page.Message!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(EInvoiceStatus.Pending, c.EInvoiceRecords.Single().Status);

        // With it, the rejection is recorded and the document stays re-submittable.
        page.ErrorCode = "2172";
        page.ErrorMessage = "Duplicate IRN for the document";
        Assert.True(page.RecordFailure());
        Assert.True(page.LastActionSucceeded);
        var record = Assert.Single(c.EInvoiceRecords);
        Assert.Equal(EInvoiceStatus.Failed, record.Status);
    }

    /// <summary>Recording an IRN before anything is prepared is refused (there is no record to record against).</summary>
    [Fact]
    public void Einvoice_recording_an_irn_without_a_prepared_record_is_refused()
    {
        var vm = NewRegularGstCompany("EInv No Record Co");
        EnableEInvoicing(vm.Company!);
        vm.OpenGenerateEInvoice();
        var page = vm.GenerateEInvoice!;
        page.Irn = new string('a', 64);

        Assert.False(page.RecordIrpResponse());
        Assert.Contains("prepare the INV-01 first", page.Message);
        Assert.Empty(vm.Company!.EInvoiceRecords);
    }

    /// <summary>ER-13: a Composition / GST-off company never reaches the e-Invoice screen.</summary>
    [Fact]
    public void Einvoice_screen_is_gated_off_for_composition_and_gst_off_companies()
    {
        var comp = NewSeededCompany("EInv Gated Comp Co");
        EnableGst(comp.Company!, GstRegistrationType.Composition);
        _storage.Save(comp.Company!);
        comp.ShowGateway();
        comp.OpenGenerateEInvoice();
        Assert.Null(comp.GenerateEInvoice);

        var off = NewSeededCompany("EInv Gated Off Co");
        _storage.Save(off.Company!);
        off.ShowGateway();
        off.OpenGenerateEInvoice();
        Assert.Null(off.GenerateEWayBill);
        Assert.Null(off.GenerateEInvoice);
        Assert.Equal(Screen.Gateway, off.CurrentScreen);
    }

    // ================================================================ Screen 6: Generate e-Way Bill

    /// <summary>
    /// A Regular GST company with one <b>₹1,00,000 inter-state sale of real stock</b> — an e-Way Bill is only ever
    /// raised for a <b>goods movement</b>, so the voucher must carry <b>inventory lines</b> (an accounts-only sale is
    /// NotApplicable however large it is). The ₹1,18,000 consignment is comfortably over the strict ₹50,000 threshold.
    /// </summary>
    /// <param name="saleDate">The outward movement's date. Defaults to <see cref="SaleDate"/>; a date on/after
    /// 01-Aug-2026 is what unlocks the statutory <b>closure</b> mechanism (and makes the Ship-To GSTIN mandatory).</param>
    private MainWindowViewModel NewEWayCompany(string name, DateOnly? saleDate = null)
    {
        var vm = NewSeededCompany(name);
        var c = vm.Company!;
        EnableGst(c, GstRegistrationType.Regular);
        EnableEWay(c);
        var sold = saleDate ?? SaleDate;

        var gst = new GstService(c);
        var ledgers = new LedgerService(c);
        var inv = new InventoryService(c);
        var main = c.MainLocation!.Id;

        var widget = inv.CreateStockItem("Widget", inv.CreateStockGroup("Goods").Id,
            inv.CreateSimpleUnit("Nos", "Nos").Id);
        widget.Gst = new StockItemGstDetails
        { HsnSac = "8471", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800 };

        var sales = Add(c, "Sales", "Sales Accounts", false);
        var purchases = Add(c, "Purchases", "Purchase Accounts", true);
        var creditor = Add(c, "Creditor", "Sundry Creditors", false);
        var debtor = Add(c, "Outstate Debtor", "Sundry Debtors", true);
        debtor.PartyGst = new PartyGstDetails
        { RegistrationType = GstRegistrationType.Regular, Gstin = "29AAACC1206D1ZM", StateCode = "29" };

        // Stock on hand: 100 widgets @ ₹500.
        ledgers.Post(new Voucher(Guid.NewGuid(),
            c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase).Id, PurchaseDate,
            new[]
            {
                new EntryLine(purchases.Id, Money.FromRupees(50000m), DrCr.Debit),
                new EntryLine(creditor.Id, Money.FromRupees(50000m), DrCr.Credit),
            },
            inventoryLines: new[] { new VoucherInventoryLine(widget.Id, main, 100m, Money.FromRupees(500m)) }));

        // INTER-STATE sale: 100 @ ₹1,000 = ₹1,00,000 @ 18% IGST ⇒ party ₹1,18,000 (the consignment value).
        var tax = gst.ComputeInvoiceTax(
            new[] { new GstService.TaxableLine(Money.FromRupees(100000m), 1800) }, true, GstTaxDirection.Output);
        var lines = new List<EntryLine>
        {
            new(debtor.Id, Money.FromRupees(118000m), DrCr.Debit),
            new(sales.Id, Money.FromRupees(100000m), DrCr.Credit),
        };
        lines.AddRange(tax.TaxLines);
        ledgers.Post(new Voucher(Guid.NewGuid(),
            c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id, sold, lines,
            number: 1, partyId: debtor.Id,
            inventoryLines: new[] { new VoucherInventoryLine(widget.Id, main, 100m, Money.FromRupees(1000m)) }));

        _storage.Save(c);
        vm.ShowGateway();
        return vm;
    }

    private GenerateEWayBillViewModel NewEWayVm(
        Company c, System.Collections.Generic.IDictionary<string, byte[]> written, DateTimeOffset? now = null,
        DateOnly? today = null) =>
        new(c, _storage, onChanged: null, folder: @"C:\out", today: today ?? SaleDate,
            now: () => now ?? new DateTimeOffset(2024, 4, 5, 9, 0, 0, TimeSpan.Zero),
            writeBytes: (p, b) => written[p] = b);

    /// <summary>(a) Opening the e-Way screen prepares NOTHING.</summary>
    [Fact]
    public void Eway_opening_the_screen_prepares_nothing()
    {
        var vm = NewEWayCompany("EWay Open Co");
        var c = vm.Company!;

        vm.OpenGenerateEWayBill();
        Assert.Equal(Screen.GenerateEWayBill, vm.CurrentScreen);
        var page = vm.GenerateEWayBill!;

        // Both goods movements are listed: the inward stock purchase and the outward inter-state sale.
        Assert.Equal(2, page.Rows.Count);

        // The ₹50,000 purchase sits EXACTLY on the threshold — and the threshold is STRICT (> ₹50,000), so it needs
        // no bill. Only more than ₹50,000 does.
        var atThreshold = page.Rows.Single(r => r.ConsignmentValue == "50,000.00");
        Assert.False(atThreshold.IsRequired);
        Assert.Equal("Not required", atThreshold.Coverage);
        Assert.Contains("threshold is strict", atThreshold.Note);

        // The ₹1,18,000 sale is over it ⇒ a bill is required.
        var row = page.Rows.Single(r => r.IsRequired);
        Assert.Equal("Required", row.Coverage);
        Assert.Equal("1,18,000.00", row.ConsignmentValue);
        Assert.False(row.HasRecord);

        Assert.Empty(c.EWayBillRecords);             // nothing prepared by opening
        Assert.True(vm.IsGenerateEWayBillScreen);
    }

    /// <summary>(b) The explicit Prepare DOES raise the record (with its Part-B) and write the offline EWB-01 JSON.</summary>
    [Fact]
    public void Eway_explicit_prepare_raises_the_record_and_writes_the_ewb01_json()
    {
        var vm = NewEWayCompany("EWay Prepare Co");
        var c = vm.Company!;
        var written = new System.Collections.Generic.Dictionary<string, byte[]>();
        var page = NewEWayVm(c, written);
        page.HighlightedIndex = page.Rows.ToList().FindIndex(r => r.IsRequired);
        page.Mode = EWayTransportMode.Road;
        page.VehicleNumber = "MH12AB1234";
        page.DistanceKmText = "250";

        Assert.True(page.PrepareAndWriteJson());
        Assert.True(page.LastActionSucceeded);

        // The ENGINE effect: a Pending record carrying the Part-B, and the EWB-01 bytes written.
        var record = Assert.Single(c.EWayBillRecords);
        Assert.Equal(EWayStatus.Pending, record.Status);
        Assert.Equal("MH12AB1234", record.VehicleNumber);
        Assert.Equal(250, record.DistanceKm);
        Assert.Null(record.EwbNumber);               // no number until the portal issues one
        var file = Assert.Single(written);
        Assert.StartsWith(@"C:\out\EWB01_", file.Key);
        Assert.NotEmpty(file.Value);
        Assert.Contains("EWB-01 written", page.Message);
    }

    /// <summary>(b) The portal's EWB number + validity are recorded back; the bill becomes Generated.</summary>
    [Fact]
    public void Eway_recording_the_portal_response_generates_the_bill_with_its_validity()
    {
        var vm = NewEWayCompany("EWay Portal Co");
        var c = vm.Company!;
        var written = new System.Collections.Generic.Dictionary<string, byte[]>();
        var page = NewEWayVm(c, written);
        page.HighlightedIndex = page.Rows.ToList().FindIndex(r => r.IsRequired);
        page.VehicleNumber = "MH12AB1234";
        page.DistanceKmText = "250";
        Assert.True(page.PrepareAndWriteJson());

        page.EwbNumber = "241234567890";
        Assert.True(page.RecordPortalResponse());

        var record = Assert.Single(c.EWayBillRecords);
        Assert.Equal(EWayStatus.Generated, record.Status);
        Assert.Equal("241234567890", record.EwbNumber);
        Assert.NotNull(record.ValidUpto);
        Assert.NotNull(record.GeneratedAt);
        Assert.Contains("valid until", page.Message);

        // The re-projected row carries the portal's number + the validity it was issued with.
        var billed = page.Rows.Single(r => r.HasRecord);
        Assert.Equal("241234567890", billed.EwbNumber);
        Assert.NotEqual("—", billed.ValidUpto);
        Assert.Equal(nameof(EWayStatus.Generated), billed.Status);
    }

    /// <summary>
    /// (c) Engine guard — the ₹50,000 threshold is <b>strict</b>: a consignment at or under it is NotRequired, and
    /// preparing a bill for it is refused with nothing written.
    /// <para>(FIX 5) This used to assert NOTHING: the old fixture's vouchers carried no inventory lines, so
    /// <c>page.Rows</c> was EMPTY — the <c>Assert.All</c> was vacuously true over zero rows and the
    /// <c>if (page.Rows.Count == 0) return;</c> bail killed the body before it reached the guard. It now uses the
    /// inventory-bearing fixture and targets the ₹50,000 movement that sits EXACTLY on the strict threshold.</para>
    /// </summary>
    [Fact]
    public void Eway_a_below_threshold_movement_cannot_be_prepared()
    {
        var vm = NewEWayCompany("EWay Small Co");
        var c = vm.Company!;
        var written = new System.Collections.Generic.Dictionary<string, byte[]>();
        var page = NewEWayVm(c, written);

        // The ₹50,000 purchase sits EXACTLY on the threshold — and the threshold is STRICT (> ₹50,000), so it needs
        // no bill. Target it explicitly rather than "whatever row 0 happens to be".
        var idx = page.Rows.ToList().FindIndex(r => r.ConsignmentValue == "50,000.00");
        Assert.True(idx >= 0, "the fixture should carry the at-threshold ₹50,000 movement");
        Assert.False(page.Rows[idx].IsRequired);
        page.HighlightedIndex = idx;
        page.DistanceKmText = "10";

        Assert.False(page.PrepareAndWriteJson());
        Assert.False(page.LastActionSucceeded);
        Assert.NotNull(page.Message);
        Assert.Empty(c.EWayBillRecords);
        Assert.Empty(written);
    }

    /// <summary>
    /// (FIX 4) The statutory <b>closure</b> mechanism is reachable and works. <c>CloseAction</c> was implemented and
    /// command-generated but bound to NO button and covered by ZERO tests — a statutory affordance the shipped UI
    /// could not reach. This drives the happy path end-to-end.
    /// </summary>
    [Fact]
    public void Eway_close_marks_an_on_or_after_2026_08_01_bill_closed()
    {
        var moved = new DateOnly(2026, 8, 10);            // on/after the date the closure mechanism exists
        var vm = NewEWayCompany("EWay Close Co", moved);
        var c = vm.Company!;
        var written = new System.Collections.Generic.Dictionary<string, byte[]>();
        var page = NewEWayVm(c, written, now: new DateTimeOffset(2026, 8, 10, 9, 0, 0, TimeSpan.Zero), today: moved);

        page.HighlightedIndex = page.Rows.ToList().FindIndex(r => r.IsRequired);
        page.VehicleNumber = "MH12AB1234";
        page.DistanceKmText = "250";
        page.ShipToGstin = "29AAACC1206D1ZM";            // mandatory from 01-Aug-2026
        Assert.True(page.PrepareAndWriteJson(), page.Message);
        page.EwbNumber = "261234567890";
        Assert.True(page.RecordPortalResponse(), page.Message);

        Assert.True(page.Close(), page.Message);
        Assert.True(page.LastActionSucceeded);

        // The ENGINE effect: the closure is really recorded. It is ADVISORY — no state transition — so the bill stays
        // Generated and carries the closure request + its date.
        var record = Assert.Single(c.EWayBillRecords);
        Assert.True(record.ClosureRequested);
        Assert.Equal(moved, record.ClosedOn);
        Assert.Equal(EWayStatus.Generated, record.Status);
        Assert.Contains("Closure requested", page.Message!);
    }

    /// <summary>(FIX 4) The closure guard surfaces cleanly: before 01-Aug-2026 the mechanism does not exist, so the
    /// request is refused and the bill is left Generated.</summary>
    [Fact]
    public void Eway_closing_before_the_mechanism_exists_is_refused()
    {
        var vm = NewEWayCompany("EWay Close Early Co");   // the 2024 movement — no closure mechanism yet
        var c = vm.Company!;
        var written = new System.Collections.Generic.Dictionary<string, byte[]>();
        var page = NewEWayVm(c, written);
        page.HighlightedIndex = page.Rows.ToList().FindIndex(r => r.IsRequired);
        page.VehicleNumber = "MH12AB1234";
        page.DistanceKmText = "250";
        Assert.True(page.PrepareAndWriteJson());
        page.EwbNumber = "241234567890";
        Assert.True(page.RecordPortalResponse());

        Assert.False(page.Close());
        Assert.False(page.LastActionSucceeded);
        Assert.Contains("01-Aug-2026", page.Message!);
        Assert.False(c.EWayBillRecords.Single().ClosureRequested);   // untouched
    }

    /// <summary>(c) Engine guard — an <b>extension</b> outside the ±8-hour window around expiry is refused, and the
    /// bill's validity is left untouched.</summary>
    [Fact]
    public void Eway_extending_outside_the_window_is_refused()
    {
        var vm = NewEWayCompany("EWay Extend Co");
        var c = vm.Company!;
        var written = new System.Collections.Generic.Dictionary<string, byte[]>();
        var page = NewEWayVm(c, written);
        page.HighlightedIndex = page.Rows.ToList().FindIndex(r => r.IsRequired);
        page.VehicleNumber = "MH12AB1234";
        page.DistanceKmText = "250";
        Assert.True(page.PrepareAndWriteJson());
        page.EwbNumber = "241234567890";
        Assert.True(page.RecordPortalResponse());

        var validBefore = c.EWayBillRecords.Single().ValidUpto;

        // "Now" is right after generation — nowhere near the expiry window.
        page.RemainingDistanceKmText = "100";
        Assert.False(page.Extend());
        Assert.False(page.LastActionSucceeded);
        Assert.NotNull(page.Message);
        Assert.Equal(validBefore, c.EWayBillRecords.Single().ValidUpto);   // untouched
    }

    /// <summary>A bad distance is rejected by the screen before the engine is called.</summary>
    [Fact]
    public void Eway_a_bad_distance_is_rejected()
    {
        var vm = NewEWayCompany("EWay Bad Km Co");
        var c = vm.Company!;
        var written = new System.Collections.Generic.Dictionary<string, byte[]>();
        var page = NewEWayVm(c, written);
        page.HighlightedIndex = page.Rows.ToList().FindIndex(r => r.IsRequired);
        page.DistanceKmText = "far";

        Assert.False(page.PrepareAndWriteJson());
        Assert.Contains("not a valid distance", page.Message);
        Assert.Empty(c.EWayBillRecords);
    }

    /// <summary>ER-13: a Composition / GST-off company never reaches the e-Way Bill screen.</summary>
    [Fact]
    public void Eway_screen_is_gated_off_for_composition_and_gst_off_companies()
    {
        var comp = NewSeededCompany("EWay Gated Comp Co");
        EnableGst(comp.Company!, GstRegistrationType.Composition);
        _storage.Save(comp.Company!);
        comp.ShowGateway();
        comp.OpenGenerateEWayBill();
        Assert.Null(comp.GenerateEWayBill);
        Assert.Equal(Screen.Gateway, comp.CurrentScreen);

        var off = NewSeededCompany("EWay Gated Off Co");
        _storage.Save(off.Company!);
        off.ShowGateway();
        off.OpenGenerateEWayBill();
        Assert.Null(off.GenerateEWayBill);
        Assert.Equal(Screen.Gateway, off.CurrentScreen);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch { /* best-effort cleanup */ }
    }

    // ================================================================ FIX 1: the Rule 43 / 37 / 37A source anchor
    //
    // Rule 43 apportions ONE SPECIFIC capital good's credit over 60 months and Rule 37/37A reverses ONE SPECIFIC
    // supplier purchase's ITC — and the engine keys both on (rule, period, sourceVoucherId). The screen used to GUESS
    // that anchor (`_company.Vouchers.FirstOrDefault()`), which meant (a) the reversal row's SourceVoucherId was audit
    // garbage and (b) every capital good after the first collided on the SAME key, so PostReversal returned the first
    // one's existing row and the tranche was silently dropped while the screen reported success. These lock the fix:
    // the anchor is the user's explicit pick, and no pick means no posting.

    /// <summary>Posts an extra ₹<paramref name="rupees"/> capital-goods purchase from the seeded supplier and returns
    /// it — the real subject of a Rule-43 apportionment (the base fixture's own first purchase is raw material, which
    /// is exactly the wrong thing to anchor a capital-goods schedule to).</summary>
    private static Voucher AddCapitalGoodPurchase(Company c, string docNo, decimal rupees)
    {
        var asset = c.FindLedgerByName("Capital Goods") ?? Add(c, "Capital Goods", "Fixed Assets", true);
        var supplier = c.FindLedgerByName("Local Supplier")!;
        var tax = new GstService(c).ComputeInvoiceTax(
            new[] { new GstService.TaxableLine(Money.FromRupees(rupees), 1800) }, false, GstTaxDirection.Input);

        var lines = new List<EntryLine>
        {
            new(asset.Id, Money.FromRupees(rupees), DrCr.Debit),
            new(supplier.Id, Money.FromRupees(rupees * 1.18m), DrCr.Credit),
        };
        lines.AddRange(tax.TaxLines);
        return new LedgerService(c).Post(new Voucher(
            Guid.NewGuid(), c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase).Id,
            PurchaseDate, lines, partyId: supplier.Id, narration: docNo));
    }

    /// <summary>The picker index of <paramref name="voucherId"/> — asserting on the way through that the screen offers
    /// it at all (a picker that cannot reach the asset is as broken as a guessed anchor).</summary>
    private static int SourceIndexOf(PostItcReversalViewModel page, Guid voucherId)
    {
        var i = page.SourceVouchers.ToList().FindIndex(r => r.VoucherId == voucherId);
        Assert.True(i >= 0, "the source-voucher picker should offer this purchase");
        return i;
    }

    /// <summary>(FIX 1a) A Rule-43 tranche anchors to the <b>selected</b> capital good — never to whatever voucher
    /// happens to sit first in the company (a raw-material purchase, in this fixture).</summary>
    [Fact]
    public void Reversal_rule43_anchors_to_the_selected_capital_good_never_an_arbitrary_voucher()
    {
        var vm = NewRegularGstCompany("Rev Rule43 Anchor Co");
        var c = vm.Company!;
        var rawMaterial = c.Vouchers.First();                            // NOT a capital good
        var capitalGood = AddCapitalGoodPurchase(c, "CG-A", 60000m);     // the asset being apportioned

        vm.OpenPostItcReversal();
        var page = vm.PostItcReversal!;
        page.Kind = ItcReversalPostKind.Rule43;
        page.Period = "2024-04";
        page.SelectedSourceIndex = SourceIndexOf(page, capitalGood.Id);
        page.CgstText = "60000.00";                                      // Tc = 60,000
        page.ExemptTurnoverText = "50.00";
        page.TotalTurnoverText = "100.00";

        Assert.True(page.Post());
        Assert.True(page.LastActionSucceeded);

        var row = Assert.Single(c.ItcReversals);
        Assert.Equal(capitalGood.Id, row.SourceVoucherId);               // the SELECTED asset...
        Assert.NotEqual(rawMaterial.Id, row.SourceVoucherId);            // ...never the company's first voucher
    }

    /// <summary>(FIX 1b) Two capital goods apportioned in the SAME period each post their OWN tranche. This is the
    /// silent under-reversal: on the guessed anchor both collided on one key, so B's ₹1,000 tranche vanished while the
    /// screen reported it posted.</summary>
    [Fact]
    public void Reversal_rule43_two_capital_goods_in_one_period_each_post_their_own_tranche()
    {
        var vm = NewRegularGstCompany("Rev Rule43 Two Assets Co");
        var c = vm.Company!;
        var assetA = AddCapitalGoodPurchase(c, "CG-A", 60000m);          // Tc =   60,000 ⇒ Te = (60000/60) × 50% = ₹500
        var assetB = AddCapitalGoodPurchase(c, "CG-B", 120000m);         // Tc = 1,20,000 ⇒ Te = ₹1,000 (exactly double)

        vm.OpenPostItcReversal();
        var page = vm.PostItcReversal!;
        page.Kind = ItcReversalPostKind.Rule43;
        page.Period = "2024-04";
        page.ExemptTurnoverText = "50.00";
        page.TotalTurnoverText = "100.00";

        page.SelectedSourceIndex = SourceIndexOf(page, assetA.Id);
        page.CgstText = "60000.00";
        Assert.True(page.Post());

        page.SelectedSourceIndex = SourceIndexOf(page, assetB.Id);
        page.CgstText = "120000.00";
        Assert.True(page.Post());

        // BOTH tranches exist, each against its OWN asset and carrying its OWN amount.
        Assert.Equal(2, c.ItcReversals.Count);
        var a = c.ItcReversals.Single(r => r.SourceVoucherId == assetA.Id);
        var b = c.ItcReversals.Single(r => r.SourceVoucherId == assetB.Id);
        Assert.Equal(50_000, a.CgstPaisa);                               // ₹500
        Assert.Equal(100_000, b.CgstPaisa);                              // ₹1,000 — the tranche that used to disappear
    }

    /// <summary>(FIX 1c) With no capital good selected the screen REFUSES rather than guessing an anchor.</summary>
    [Fact]
    public void Reversal_rule43_without_a_selected_capital_good_is_refused_and_posts_nothing()
    {
        var vm = NewRegularGstCompany("Rev Rule43 No Pick Co");
        var c = vm.Company!;
        AddCapitalGoodPurchase(c, "CG-A", 60000m);

        vm.OpenPostItcReversal();
        var page = vm.PostItcReversal!;
        page.Kind = ItcReversalPostKind.Rule43;
        page.Period = "2024-04";
        page.SelectedSourceIndex = -1;                                   // nothing picked
        page.CgstText = "60000.00";
        page.ExemptTurnoverText = "50.00";
        page.TotalTurnoverText = "100.00";

        Assert.False(page.Post());
        Assert.False(page.LastActionSucceeded);
        Assert.Contains("capital-goods", page.Message!, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(c.ItcReversals);
    }

    /// <summary>(FIX 1d) Rule 37/37A anchors to the <b>selected</b> supplier purchase, refuses without one, and never
    /// offers a SALES invoice — whose output tax the engine's forward-ITC default would happily mistake for ITC.</summary>
    [Fact]
    public void Reversal_rule37_anchors_to_the_selected_supplier_purchase_and_refuses_without_one()
    {
        var vm = NewRegularGstCompany("Rev Rule37 Anchor Co");
        var c = vm.Company!;
        var secondPurchase = AddCapitalGoodPurchase(c, "SUP-INV-002", 20000m);

        vm.OpenPostItcReversal();
        var page = vm.PostItcReversal!;
        page.Kind = ItcReversalPostKind.Rule37;
        page.Period = "2024-10";
        page.CgstText = "100.00";

        // The picker is PURCHASES only — the seeded B2B sale is never a Rule-37 subject.
        var saleId = c.Vouchers
            .Single(v => c.VoucherTypes.Single(t => t.Id == v.TypeId).BaseType == VoucherBaseType.Sales).Id;
        Assert.DoesNotContain(page.SourceVouchers, r => r.VoucherId == saleId);

        // (c) Nothing selected ⇒ refused outright rather than anchored to a guess.
        page.SelectedSourceIndex = -1;
        Assert.False(page.Post());
        Assert.False(page.LastActionSucceeded);
        Assert.Empty(c.ItcReversals);

        // (a) The user's pick is what it anchors to — including the SECOND supplier purchase, which the old
        // "first party voucher" guess could never reach.
        page.SelectedSourceIndex = SourceIndexOf(page, secondPurchase.Id);
        Assert.True(page.Post());
        Assert.Equal(secondPurchase.Id, Assert.Single(c.ItcReversals).SourceVoucherId);
    }

    /// <summary>(FIX 1e) Re-posting the SAME (rule, period, asset) is the engine's honest idempotency — the screen must
    /// say "already posted" rather than report a second tranche it never wrote.</summary>
    [Fact]
    public void Reversal_reposting_the_same_tranche_reports_already_posted_not_a_fresh_posting()
    {
        var vm = NewRegularGstCompany("Rev Rule43 Idempotent Co");
        var c = vm.Company!;
        var asset = AddCapitalGoodPurchase(c, "CG-A", 60000m);

        vm.OpenPostItcReversal();
        var page = vm.PostItcReversal!;
        page.Kind = ItcReversalPostKind.Rule43;
        page.Period = "2024-04";
        page.SelectedSourceIndex = SourceIndexOf(page, asset.Id);
        page.CgstText = "60000.00";
        page.ExemptTurnoverText = "50.00";
        page.TotalTurnoverText = "100.00";

        Assert.True(page.Post());
        Assert.Contains("posted", page.Message!, StringComparison.OrdinalIgnoreCase);

        // The same key again: PostReversal returns the EXISTING row and writes nothing.
        Assert.False(page.Post());
        Assert.False(page.LastActionSucceeded);
        Assert.Contains("already", page.Message!, StringComparison.OrdinalIgnoreCase);
        Assert.Single(c.ItcReversals);
    }

    // ================================================================ FIX 2 / FIX 6: the residual-cash demand

    /// <summary>
    /// The base Regular fixture (credit pool 450/450, forward liability 90/90) <b>plus a ₹1,00,000 domestic §9(4)
    /// promoter RCM inward at 18%</b> ⇒ a ₹9,000 + ₹9,000 = <b>₹18,000 cash-only RCM liability</b> (ER-3) that the
    /// credit ledger may never touch. The forward heads are comfortably credit-covered, so the RCM cash is the only
    /// thing this period still owes — which is exactly the figure the screen used to hide.
    /// </summary>
    private MainWindowViewModel NewRcmSetOffCompany(string name)
    {
        var vm = NewRegularGstCompany(name);
        var c = vm.Company!;
        var gst = new GstService(c);
        gst.SeedAdvancedGst();                 // the notified RCM categories (the §9(4) promoter rows)

        var good = Add(c, "Promoter Capital Good", "Indirect Expenses", true);
        good.SalesPurchaseGst = new StockItemGstDetails
        {
            HsnSac = "8471", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800,
            SupplyType = GstSupplyType.Goods, ReverseChargeApplicable = true,
            RcmCategoryId = c.Gst!.RcmCategories.First(x => x.SupplyNature == "Capital-goods").Id,
        };
        var unregistered = Add(c, "Unregistered Dealer", "Sundry Creditors", false);
        unregistered.PartyGst = new PartyGstDetails
        { RegistrationType = GstRegistrationType.Unregistered, StateCode = "27", IsPromoter = false };

        var value = Money.FromRupees(100000m);
        var posting = new RcmService(c).BuildReverseCharge(
            value, null, good, unregistered.PartyGst, PurchaseDate, RcmService.SupplyKind.Domestic,
            recipientIsPromoter: true, quantity: 1m);
        Assert.True(posting.Applies);

        var lines = new List<EntryLine>
        {
            new(good.Id, value, DrCr.Debit),
            new(unregistered.Id, value, DrCr.Credit),   // the supplier charges ZERO tax
        };
        lines.AddRange(posting.Lines);                  // the balanced RCM pair, additive
        new LedgerService(c).Post(new Voucher(
            Guid.NewGuid(), c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase).Id,
            PurchaseDate, lines));

        _storage.Save(c);
        vm.ShowGateway();
        return vm;
    }

    private static decimal Required(RunSetOffViewModel page, string head) =>
        decimal.Parse(page.CashCells.Single(x => x.Head == head).Required,
            NumberStyles.Number, CultureInfo.InvariantCulture);

    /// <summary>Funds a cash cell with a real PMT-06 challan (CIN present, so the cash ledger is actually credited).</summary>
    private static void Deposit(RunSetOffViewModel page, GstTaxHead head, string rupees)
    {
        page.ChallanHead = head;
        page.ChallanAmountText = rupees;
        page.Cpin = "24040100001234";
        page.Cin = "SBIN24040100001234";
        Assert.True(page.DepositChallan(), page.Message);
    }

    /// <summary>
    /// (FIX 2) The <b>cash-only RCM liability must be dischargeable</b>. It was excluded from the screen's cash demand
    /// entirely: all four cells read 0.00, the discharge had nothing to pay, and — because the forward heads were
    /// credit-covered — the screen told the taxpayer the period was <b>"already discharged in full"</b> while ₹18,000
    /// of RCM cash was outstanding. A false statement on a money screen.
    /// </summary>
    [Fact]
    public void Setoff_cash_only_rcm_liability_is_exposed_and_dischargeable()
    {
        var vm = NewRcmSetOffCompany("SetOff Rcm Cash Co");
        var c = vm.Company!;
        vm.OpenRunSetOff();
        var page = vm.RunSetOff!;

        // The RCM cash is real and is ring-fenced OUT of the credit steps (ER-3).
        Assert.Equal("18,000.00", page.LiabRcmCashText);
        Assert.Equal(18_000_00, page.Allocation.CashRcm);

        // (a) It must be VISIBLE as payable, per the head it actually arose in — today every cell reads 0.00.
        Assert.Equal(9000m, Required(page, "CGST"));
        Assert.Equal(9000m, Required(page, "SGST/UTGST"));
        Assert.Equal(0m, Required(page, "IGST"));

        // (b) It must NEVER claim the period is settled while that cash is outstanding. Unfunded, the honest answer is
        // "deposit a challan" — not "already discharged in full".
        Assert.False(page.PayResidualCash());
        Assert.DoesNotContain("already been discharged in full", page.Message!);
        Assert.Contains("underfunded", page.Message!, StringComparison.OrdinalIgnoreCase);

        // (c) Funded, the discharge actually happens — the mechanism used to be unreachable.
        Deposit(page, GstTaxHead.Central, "9000");
        Deposit(page, GstTaxHead.State, "9000");
        Assert.True(page.PayResidualCash(), page.Message);
        Assert.True(page.LastActionSucceeded);

        // The ENGINE effect: ₹18,000 really drawn out of the electronic cash ledger.
        var drawn = new GstDepositService(c);
        Assert.Equal(Money.Zero, drawn.AvailableCash(GstTaxHead.Central, GstMinorHead.Tax));
        Assert.Equal(Money.Zero, drawn.AvailableCash(GstTaxHead.State, GstMinorHead.Tax));

        // ...and it nets out, so a second run cannot pay it twice.
        Assert.Equal(0m, Required(page, "CGST"));
        Assert.False(page.PayResidualCash());
    }

    /// <summary>
    /// (FIX 6) A <b>DRC-03</b> cash payment must NOT reduce the 3B cash demanded. Both posters draw identically-tagged
    /// <c>CashPayment</c> legs off the electronic cash ledger, so netting on "any cash draw dated inside the month"
    /// silently counted a voluntary DRC-03 as if the period's 3B cash had been discharged.
    /// </summary>
    [Fact]
    public void Setoff_a_drc03_cash_payment_does_not_reduce_the_3b_cash_demanded()
    {
        var vm = NewRcmSetOffCompany("SetOff Drc03 Co");
        var c = vm.Company!;
        vm.OpenRunSetOff();
        var page = vm.RunSetOff!;
        Assert.Equal(9000m, Required(page, "CGST"));

        // Fund the cell and spend ₹9,000 of it on a DRC-03 — a voluntary payment of a DIFFERENT liability that happens
        // to fall inside the same month.
        Deposit(page, GstTaxHead.Central, "9000");
        new GstDepositService(c).PostDrc03(
            cause: "Voluntary — under-reported outward supply", period: "2024-04", date: new DateOnly(2024, 4, 20),
            cgstPaisa: 9000_00, sgstPaisa: 0, igstPaisa: 0, cessPaisa: 0, interestPaisa: 0,
            method: GstDepositService.PaymentMethod.Cash);
        _storage.Save(c);

        page.Rebuild();

        // The 3B cash this period demands is UNCHANGED — the DRC-03 discharged something else entirely.
        Assert.Equal(9000m, Required(page, "CGST"));
    }

    /// <summary>(FIX 1f) The promised first clause, finally wired: a highlighted candidate that names its own source
    /// voucher pre-selects that voucher in the picker.</summary>
    [Fact]
    public void Reversal_a_highlighted_candidate_preselects_its_own_source_voucher()
    {
        var vm = NewRegularGstCompany("Rev Candidate Anchor Co");
        var c = vm.Company!;
        Import2b(c);                                    // an EMPTY 2B ⇒ the booked purchase is §16(2)(aa) not-in-portal
        var purchase = c.Vouchers.First();

        vm.OpenPostItcReversal();
        var page = vm.PostItcReversal!;
        page.Kind = ItcReversalPostKind.Rule37;         // a rule that needs a source voucher

        var i = page.RawCandidates.ToList().FindIndex(x => x.VoucherId == purchase.Id);
        Assert.True(i >= 0, "the empty 2B should raise a §16(2)(aa) candidate keyed to the booked purchase");
        page.CandidateIndex = i;

        Assert.Equal(purchase.Id, page.SelectedSource?.VoucherId);
    }
}
