using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Desktop.Views;
using Xunit;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.Tests;

/// <summary>
/// <b>Binding-reachability locks</b> for the Phase-9 UI-3 <b>voucher-entry</b> surfaces (RCM / §34 CDN / GST advances).
///
/// <para>A view-model property is worthless if no control surfaces it, and a guard is worthless if the operator cannot
/// reach the input that satisfies it. This exact class of defect shipped three times in UI-2 (a permanently-dead
/// "Record failure" button, an unbound statutory Close, a hard-coded cancellation reason) — a view-model-only test can
/// never see it, because the VM is perfectly happy talking to nobody.</para>
///
/// <para>These drive the <b>real MainWindow XAML</b> headlessly: they realise the voucher-entry DataTemplate, then
/// assert each new affordance genuinely exists AND round-trips to the view model.</para>
/// </summary>
public sealed class GstVoucherEntryUiBindingTests
{
    private const string GstinMaharashtra = "27AAPFU0939F1ZV";
    private const string GstinGujarat = "24AAACC1206D1ZM";
    private static readonly DateOnly FyStart = new(2025, 4, 1);
    private static readonly DateOnly D1 = new(2025, 4, 10);

    /// <summary>Boots the real window on a Regular-GST company with the notified RCM categories seeded, then lays it out
    /// so the voucher-entry DataTemplate is actually realised.</summary>
    private static (MainWindow Window, MainWindowViewModel Vm, string TempDir) NewWindow(string name)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ApexVchBind_" + Guid.NewGuid().ToString("N"));
        var storage = new CompanyStorage(tempDir);
        var vm = new MainWindowViewModel(storage);
        vm.NewCompanyName = name;
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
        storage.Save(c);
        vm.ShowGateway();

        var window = new MainWindow { DataContext = vm };
        window.Show();
        Pump(window);
        return (window, vm, tempDir);
    }

    /// <summary>Flushes bindings and forces a layout pass so a freshly-shown page's DataTemplate is realised.</summary>
    private static void Pump(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.Measure(new Size(1280, 800));
        window.Arrange(new Rect(0, 0, 1280, 800));
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    /// Every string a realised control is actually displaying to the operator.
    /// <para>
    /// Two traps this deliberately avoids. (1) <see cref="Run"/> inlines: several figures are bound through Runs, which a
    /// naive <c>TextBlock.Text</c> probe silently misses. (2) <b>Collapsed</b> controls: Avalonia keeps
    /// <c>IsVisible="false"</c> controls in the visual tree, so an unfiltered walk happily "finds" the contents of a
    /// hidden panel — which would make every ER-13 negative assertion here vacuously pass. Only effectively-visible
    /// controls count as reaching the operator.
    /// </para>
    /// </summary>
    private static IEnumerable<string> AllShownText(Window window)
    {
        foreach (var x in window.GetVisualDescendants().Where(v => v.IsEffectivelyVisible))
        {
            switch (x)
            {
                case TextBox tb when tb.Text is { } t:
                    yield return t;
                    break;
                case TextBlock tbl:
                    if (tbl.Text is { } bt) yield return bt;
                    if (tbl.Inlines is { } inlines)
                        foreach (var run in inlines.OfType<Run>())
                            if (run.Text is { } rt) yield return rt;
                    break;
            }
        }
    }

    /// <summary>True iff SOME realised control actually displays <paramref name="sentinel"/> — i.e. a control is really
    /// bound to the view-model property the sentinel came from.</summary>
    private static bool SomeControlShows(Window window, string sentinel) =>
        AllShownText(window).Any(t => t.Contains(sentinel, StringComparison.Ordinal));

    /// <summary>The <b>visible</b> <see cref="CheckBox"/> whose caption is <paramref name="content"/>, or null if the
    /// operator has no such affordance (a collapsed box is no affordance at all).</summary>
    private static CheckBox? FindCheckBox(Window window, string content) =>
        window.GetVisualDescendants().OfType<CheckBox>()
            .FirstOrDefault(cb => cb.IsEffectivelyVisible && cb.Content as string == content);

    /// <summary>The <b>visible</b> <see cref="ComboBox"/> bound to <paramref name="itemsSource"/> (reference identity —
    /// the only honest proof that THIS collection is the one the operator picks from).</summary>
    private static ComboBox? FindComboBoxFor(Window window, object itemsSource) =>
        window.GetVisualDescendants().OfType<ComboBox>()
            .FirstOrDefault(cb => cb.IsEffectivelyVisible && ReferenceEquals(cb.ItemsSource, itemsSource));

    private static void Cleanup(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
        catch { /* best-effort */ }
    }

    private static DomainLedger AddLedger(Company c, string name, string groupName, bool openingIsDebit)
    {
        var l = new DomainLedger(Guid.NewGuid(), name, c.FindGroupByName(groupName)!.Id, Money.Zero, openingIsDebit);
        c.AddLedger(l);
        return l;
    }

    /// <summary>An RCM-flagged expense ledger + an inter-state supplier, wired onto a live Purchase grid.</summary>
    private static VoucherEntryViewModel OpenRcmPurchase(MainWindow window, MainWindowViewModel vm)
    {
        var c = vm.Company!;
        var fees = AddLedger(c, "Legal Fees", "Indirect Expenses", true);
        fees.SalesPurchaseGst = new StockItemGstDetails
        {
            Taxability = GstTaxability.Taxable,
            RateBasisPoints = 1800,
            SupplyType = GstSupplyType.Services,
            ReverseChargeApplicable = true,
            RcmCategoryId = c.Gst!.RcmCategories.First(x => x.SupplyNature == "Legal").Id,
        };
        var advocate = AddLedger(c, "Advocate (Gujarat)", "Sundry Creditors", false);
        advocate.PartyGst = new PartyGstDetails
        {
            RegistrationType = GstRegistrationType.Regular,
            Gstin = GstinGujarat,
            StateCode = "24",
        };

        vm.OpenVoucher(VoucherBaseType.Purchase);
        var e = vm.VoucherEntry!;
        e.Date = D1;
        e.Lines[0].SelectedLedger = fees;
        e.Lines[0].Side = DrCr.Debit;
        e.Lines[0].AmountText = "10000";
        e.Lines[1].SelectedLedger = advocate;
        e.Lines[1].Side = DrCr.Credit;
        e.Lines[1].AmountText = "10000";
        e.Recalculate();
        Pump(window);
        return e;
    }

    // ================================================================ RCM — the panel is real and reachable

    /// <summary>
    /// The RCM panel's resolution preview must actually reach the operator: the applicability verdict, the place-of-supply
    /// routing, the rate, the self-accounted tax and the cash-only summary are all surfaced by realised controls.
    /// </summary>
    [AvaloniaFact]
    public void Rcm_resolution_preview_is_surfaced_by_real_controls()
    {
        var (window, vm, dir) = NewWindow("RCM Bind Co");
        try
        {
            var e = OpenRcmPurchase(window, vm);
            Assert.True(e.ShowRcmPanel);

            Assert.True(SomeControlShows(window, "Yes — reverse charge applies"),
                "no control binds RcmAppliesText — the operator cannot see whether reverse charge fires");
            Assert.True(SomeControlShows(window, "Inter-State (IGST)"),
                "no control binds RcmPosText — the operator cannot see the place-of-supply routing");
            Assert.True(SomeControlShows(window, "18%"),
                "no control binds RcmRateText — the operator cannot see the resolved RCM rate");
            Assert.True(SomeControlShows(window, "1,800.00"),
                "no control binds RcmTaxText — the operator cannot see the self-accounted RCM tax");
            // The cash-only §49(4) rule is the single most misunderstood fact about RCM — it MUST be on screen.
            Assert.True(SomeControlShows(window, "payable in CASH"),
                "no control binds RcmSummary — the cash-only §49(4) liability is never explained to the operator");
        }
        finally { window.Close(); Cleanup(dir); }
    }

    /// <summary>
    /// The supply-kind picker must be bound to the real option list and round-trip to the view model — otherwise an
    /// import of services could never be routed to IGST / 4A(2).
    /// </summary>
    [AvaloniaFact]
    public void Rcm_supply_kind_picker_is_bound_and_round_trips()
    {
        var (window, vm, dir) = NewWindow("RCM Kind Bind Co");
        try
        {
            var e = OpenRcmPurchase(window, vm);

            var combo = FindComboBoxFor(window, e.RcmSupplyKinds);
            Assert.True(combo is not null,
                "no ComboBox binds RcmSupplyKinds — the supply kind can never be routed to import-of-services");

            // Import of goods must NEVER be offered: it is not reverse charge and the engine hard-fails on it.
            Assert.DoesNotContain(e.RcmSupplyKinds, k => k.Kind == RcmService.SupplyKind.ImportOfGoods);

            // Picking through the CONTROL (not the VM) must reach the engine and re-resolve.
            combo!.SelectedItem = e.RcmSupplyKinds.First(k => k.Kind == RcmService.SupplyKind.ImportOfServices);
            Pump(window);

            Assert.Equal(RcmService.SupplyKind.ImportOfServices, e.SelectedRcmSupplyKind!.Kind);
            Assert.True(SomeControlShows(window, "§5(3)"),
                "selecting import-of-services through the control did not re-resolve the panel");
        }
        finally { window.Close(); Cleanup(dir); }
    }

    /// <summary>
    /// The §9(4) promoter / body-corporate qualifiers and the Rule-47A / Rule-52 document toggles must be real, bound
    /// checkboxes. Without the promoter box the §9(4) supply could never be entered; without the document boxes the
    /// self-invoice / payment voucher could never be raised.
    /// </summary>
    [AvaloniaFact]
    public void Rcm_qualifier_and_document_checkboxes_are_bound_and_round_trip()
    {
        var (window, vm, dir) = NewWindow("RCM Checkbox Bind Co");
        try
        {
            var e = OpenRcmPurchase(window, vm);

            var promoter = FindCheckBox(window, "We are a promoter (§9(4))");
            var bodyCorporate = FindCheckBox(window, "We are a body corporate");
            var selfInvoice = FindCheckBox(window, "Generate self-invoice (Rule 47A)");
            var paymentVoucher = FindCheckBox(window, "Generate payment voucher (Rule 52)");

            Assert.True(promoter is not null, "no checkbox binds RcmRecipientIsPromoter — a §9(4) supply is unreachable");
            Assert.True(bodyCorporate is not null, "no checkbox binds RcmRecipientIsBodyCorporate");
            Assert.True(selfInvoice is not null, "no checkbox binds GenerateRcmSelfInvoice — Rule 47A is unreachable");
            Assert.True(paymentVoucher is not null, "no checkbox binds GenerateRcmPaymentVoucher — Rule 52 is unreachable");

            // The engine defaults must be what the operator sees on arrival (promoter OFF ⇒ blanket §9(4) stays off).
            Assert.False(promoter!.IsChecked);
            Assert.True(bodyCorporate!.IsChecked);

            // Toggling through the CONTROL must reach the view model (two-way, not a decorative box).
            promoter.IsChecked = true;
            selfInvoice!.IsChecked = true;
            paymentVoucher!.IsChecked = true;
            Pump(window);

            Assert.True(e.RcmRecipientIsPromoter);
            Assert.True(e.GenerateRcmSelfInvoice);
            Assert.True(e.GenerateRcmPaymentVoucher);
        }
        finally { window.Close(); Cleanup(dir); }
    }

    // ================================================================ §34 CDN — the picker is real and reachable

    /// <summary>Posts an original Sales invoice, then opens a Credit Note grid with the ordinary return legs typed.</summary>
    private static (VoucherEntryViewModel Entry, Voucher Original) OpenCreditNote(MainWindow window, MainWindowViewModel vm)
    {
        var c = vm.Company!;
        var customer = AddLedger(c, "Acme Ltd", "Sundry Debtors", true);
        var sales = c.FindLedgerByName("Sales") ?? AddLedger(c, "Sales", "Sales Accounts", false);

        var salesType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id;
        var original = new LedgerService(c).Post(new Voucher(
            Guid.NewGuid(), salesType, D1,
            new[]
            {
                new EntryLine(customer.Id, Money.FromRupees(11800m), DrCr.Debit),
                new EntryLine(sales.Id, Money.FromRupees(11800m), DrCr.Credit),
            },
            partyId: customer.Id));

        vm.OpenVoucher(VoucherBaseType.CreditNote);
        var e = vm.VoucherEntry!;
        e.Date = new DateOnly(2025, 5, 5);
        e.Lines[0].SelectedLedger = sales;
        e.Lines[0].Side = DrCr.Debit;
        e.Lines[0].AmountText = "1180";
        e.Lines[1].SelectedLedger = customer;
        e.Lines[1].Side = DrCr.Credit;
        e.Lines[1].AmountText = "1180";
        e.Recalculate();
        Pump(window);
        return (e, original);
    }

    /// <summary>
    /// The §34 opt-in and the original-invoice picker must be real, bound controls — without them the link GSTR-1
    /// Table 9B reads could never be captured, which is the entire point of this feature.
    /// </summary>
    [AvaloniaFact]
    public void Section34_optin_and_original_invoice_picker_are_bound_and_round_trip()
    {
        var (window, vm, dir) = NewWindow("CDN Bind Co");
        try
        {
            var (e, original) = OpenCreditNote(window, vm);

            var optIn = FindCheckBox(window, "This is a §34 GST credit/debit note (link the original invoice)");
            Assert.True(optIn is not null, "no checkbox binds IsSection34Note — §34 details are unreachable");

            // Before opting in, the detail fields must not be realised (ER-13).
            Assert.Null(FindComboBoxFor(window, e.CdnOriginalInvoices));

            // Opting in THROUGH the control must reveal the picker.
            optIn!.IsChecked = true;
            Pump(window);
            Assert.True(e.IsSection34Note);

            var picker = FindComboBoxFor(window, e.CdnOriginalInvoices);
            Assert.True(picker is not null,
                "no ComboBox binds CdnOriginalInvoices — the original invoice can never be linked");
            var reason = FindComboBoxFor(window, e.CdnReasonCodes);
            Assert.True(reason is not null, "no ComboBox binds CdnReasonCodes — the §34 reason is unreachable");

            // Picking the original THROUGH the control must reach the view model and refresh the §34(2) advisory.
            picker!.SelectedItem = e.CdnOriginalInvoices.Single(o => o.Invoice?.Id == original.Id);
            reason!.SelectedItem = "01 Sales return";
            Pump(window);

            Assert.Equal(original.Id, e.SelectedCdnOriginalInvoice!.Invoice!.Id);
            Assert.Equal("01 Sales return", e.SelectedCdnReasonCode);
            // The original supply is dated 10-Apr-2025 ⇒ FY 2025-26 ⇒ the §34(2) cut-off is 30-Nov-2026.
            Assert.True(SomeControlShows(window, "30-Nov-2026"),
                "no control binds CdnSummary — the §34(2) cut-off never reaches the operator");
        }
        finally { window.Close(); Cleanup(dir); }
    }

    /// <summary>
    /// The consolidated-reference fields must appear (and bind) when that option is chosen — otherwise an unregistered /
    /// consolidated note could satisfy ER-12 in the view model but never in the UI.
    /// </summary>
    [AvaloniaFact]
    public void Section34_consolidated_reference_fields_are_bound()
    {
        var (window, vm, dir) = NewWindow("CDN Consolidated Bind Co");
        try
        {
            var (e, _) = OpenCreditNote(window, vm);
            e.IsSection34Note = true;
            e.SelectedCdnOriginalInvoice = e.CdnOriginalInvoices.Single(o => o.IsConsolidated);
            Pump(window);

            e.CdnOriginalInvoiceNumber = "INV-1042";
            e.CdnOriginalInvoiceDateText = "10-Jun-2024";
            Pump(window);

            Assert.True(SomeControlShows(window, "INV-1042"),
                "no control binds CdnOriginalInvoiceNumber — a consolidated §34 note can never satisfy ER-12");
            Assert.True(SomeControlShows(window, "10-Jun-2024"),
                "no control binds CdnOriginalInvoiceDateText — the §34(2) FY basis can never be supplied");
        }
        finally { window.Close(); Cleanup(dir); }
    }

    /// <summary>
    /// <b>The dead-guard lock.</b> Accept refuses a liability-reducing credit note whose original supply date is unknown,
    /// and the refusal tells the operator to tick Override. That advice is worthless unless the Override box is actually
    /// on screen in that exact state — the UI-2 defect class (a guard whose only escape is unbound) reproduced here.
    /// </summary>
    [AvaloniaFact]
    public void Section34_override_is_on_screen_in_the_very_state_that_demands_it()
    {
        var (window, vm, dir) = NewWindow("CDN Override Bind Co");
        try
        {
            var (e, _) = OpenCreditNote(window, vm);
            e.IsSection34Note = true;
            e.SelectedCdnOriginalInvoice = e.CdnOriginalInvoices.Single(o => o.IsConsolidated);
            e.CdnOriginalInvoiceNumber = "INV-9001"; // a reference, but deliberately NO date
            e.SelectedCdnReasonCode = "07 Others";
            Pump(window);

            // The engine refuses, advising the override…
            Assert.False(e.Accept());
            Assert.Contains("Override", e.Message);
            Pump(window);

            // …so the override MUST be reachable right now (it is not gated on CdnPastTimeLimit, which is false here).
            var overrideBox = FindCheckBox(window, "Override §34(2) time limit");
            Assert.True(overrideBox is not null,
                "the §34(2) refusal advises ticking Override, but no visible checkbox binds CdnOverrideTimeLimit");

            overrideBox!.IsChecked = true;
            Pump(window);
            Assert.True(e.CdnOverrideTimeLimit);
            Assert.True(e.Accept());
            Assert.Single(vm.Company!.CreditDebitNoteLinks);
        }
        finally { window.Close(); Cleanup(dir); }
    }

    // ================================================================ GST advances — the panels are real and reachable

    /// <summary>Opens a Receipt grid with the ordinary Rule-50 legs typed (Dr Cash / Cr Advance from customer).</summary>
    private static VoucherEntryViewModel OpenAdvanceReceipt(MainWindow window, MainWindowViewModel vm)
    {
        var c = vm.Company!;
        var cash = c.FindLedgerByName("Cash")!;
        var advLedger = AddLedger(c, "Advance from customer", "Current Liabilities", false);

        vm.OpenVoucher(VoucherBaseType.Receipt);
        var e = vm.VoucherEntry!;
        e.Date = D1;
        e.Lines[0].SelectedLedger = cash;
        e.Lines[0].Side = DrCr.Debit;
        e.Lines[0].AmountText = "11800";
        e.Lines[1].SelectedLedger = advLedger;
        e.Lines[1].Side = DrCr.Credit;
        e.Lines[1].AmountText = "11800";
        e.Recalculate();
        Pump(window);
        return e;
    }

    /// <summary>
    /// The advance opt-in, the net-advance / rate inputs and the computed figures must all be real, bound controls —
    /// otherwise a Rule-50 advance could never be entered at all.
    /// </summary>
    [AvaloniaFact]
    public void Advance_receipt_fields_are_bound_and_round_trip()
    {
        var (window, vm, dir) = NewWindow("Advance Bind Co");
        try
        {
            var e = OpenAdvanceReceipt(window, vm);

            var optIn = FindCheckBox(window, "This receipt is an advance against a future supply (GST — Rule 50)");
            Assert.True(optIn is not null, "no checkbox binds IsAdvanceReceipt — a GST advance is unreachable");

            // Opting in THROUGH the control must reveal the fields.
            optIn!.IsChecked = true;
            Pump(window);
            Assert.True(e.IsAdvanceReceipt);

            var service = FindCheckBox(window, "Service advance (a goods advance is de-taxed)");
            var interState = FindCheckBox(window, "Inter-State (IGST)");
            Assert.True(service is not null, "no checkbox binds AdvanceIsService — a goods advance cannot be de-taxed");
            Assert.True(interState is not null, "no checkbox binds AdvanceInterState — the POS routing is unreachable");

            e.AdvanceAmountText = "10000";
            interState!.IsChecked = true;
            Pump(window);

            Assert.True(e.AdvanceInterState);
            Assert.True(SomeControlShows(window, "10000"),
                "no control binds AdvanceAmountText — the net advance can never be supplied");
            Assert.True(SomeControlShows(window, "1,800.00"),
                "no control binds AdvanceTaxText — the computed advance tax never reaches the operator");
            Assert.True(SomeControlShows(window, "11,800.00"),
                "no control binds AdvanceGrossText — the gross the party remits never reaches the operator");
            Assert.True(SomeControlShows(window, "11A"),
                "no control binds AdvanceSummary — the 11A consequence is never explained");
        }
        finally { window.Close(); Cleanup(dir); }
    }

    /// <summary>
    /// The adjust/refund panel must surface the outstanding-advance picker (and, on a Journal, the invoice picker) —
    /// without them a booked advance could never be released, leaving the suspense stranded forever.
    /// </summary>
    [AvaloniaFact]
    public void Advance_adjust_pickers_are_bound_on_a_journal()
    {
        var (window, vm, dir) = NewWindow("Advance Adjust Bind Co");
        try
        {
            // Book an advance first (through the real screen), so an outstanding advance exists to act on.
            var receipt = OpenAdvanceReceipt(window, vm);
            receipt.IsAdvanceReceipt = true;
            receipt.AdvanceAmountText = "10000";
            receipt.AdvanceInterState = true;
            Assert.True(receipt.Accept());

            var c = vm.Company!;
            var advLedger = c.FindLedgerByName("Advance from customer")!;
            var customer = AddLedger(c, "Acme Ltd", "Sundry Debtors", true);

            vm.OpenVoucher(VoucherBaseType.Journal);
            var e = vm.VoucherEntry!;
            e.Date = D1;
            e.Lines[0].SelectedLedger = advLedger;
            e.Lines[0].Side = DrCr.Debit;
            e.Lines[0].AmountText = "11800";
            e.Lines[1].SelectedLedger = customer;
            e.Lines[1].Side = DrCr.Credit;
            e.Lines[1].AmountText = "11800";
            e.Recalculate();
            Pump(window);

            Assert.True(e.ShowAdvanceActionPanel);
            var advancePicker = FindComboBoxFor(window, e.OutstandingAdvances);
            Assert.True(advancePicker is not null,
                "no ComboBox binds OutstandingAdvances — a booked advance can never be adjusted or refunded");
            var invoicePicker = FindComboBoxFor(window, e.AdvanceInvoices);
            Assert.True(invoicePicker is not null,
                "no ComboBox binds AdvanceInvoices — the 11B invoice anchor is unreachable on a Journal");

            // Picking THROUGH the control must reach the view model and refresh the advisory.
            advancePicker!.SelectedItem = e.OutstandingAdvances.First(o => !o.IsNone);
            Pump(window);
            Assert.NotNull(e.SelectedOutstandingAdvance!.Receipt);
            Assert.True(SomeControlShows(window, "11B"),
                "no control binds AdvanceActionSummary — the adjustment's 11B consequence is never explained");
        }
        finally { window.Close(); Cleanup(dir); }
    }

    /// <summary>ER-13: with no advance ever booked, the adjust/refund panel must not be realised on an ordinary
    /// Journal — the existing screen is untouched.</summary>
    [AvaloniaFact]
    public void Advance_action_panel_is_absent_when_no_advance_is_outstanding()
    {
        var (window, vm, dir) = NewWindow("No-Advance Bind Co");
        try
        {
            var c = vm.Company!;
            var expense = AddLedger(c, "Rent", "Indirect Expenses", true);
            var customer = AddLedger(c, "Acme Ltd", "Sundry Debtors", true);

            vm.OpenVoucher(VoucherBaseType.Journal);
            var e = vm.VoucherEntry!;
            e.Date = D1;
            e.Lines[0].SelectedLedger = expense;
            e.Lines[0].Side = DrCr.Debit;
            e.Lines[0].AmountText = "5000";
            e.Lines[1].SelectedLedger = customer;
            e.Lines[1].Side = DrCr.Credit;
            e.Lines[1].AmountText = "5000";
            e.Recalculate();
            Pump(window);

            Assert.False(e.ShowAdvanceActionPanel);
            Assert.Null(FindComboBoxFor(window, e.OutstandingAdvances));
            Assert.False(SomeControlShows(window, "GST advance"),
                "the advance panel leaked onto an ordinary journal (ER-13)");
        }
        finally { window.Close(); Cleanup(dir); }
    }

    /// <summary>ER-13: on a GST company with no reverse-charge-flagged ledger, the RCM panel must not be realised at all
    /// — the existing plain voucher screen is untouched.</summary>
    [AvaloniaFact]
    public void Rcm_panel_is_not_shown_on_an_ordinary_purchase()
    {
        var (window, vm, dir) = NewWindow("Ordinary Purchase Bind Co");
        try
        {
            var c = vm.Company!;
            var stationery = AddLedger(c, "Stationery", "Indirect Expenses", true); // no GST block ⇒ no RCM flag
            var supplier = AddLedger(c, "Paper Mart", "Sundry Creditors", false);

            vm.OpenVoucher(VoucherBaseType.Purchase);
            var e = vm.VoucherEntry!;
            e.Date = D1;
            e.Lines[0].SelectedLedger = stationery;
            e.Lines[0].Side = DrCr.Debit;
            e.Lines[0].AmountText = "5000";
            e.Lines[1].SelectedLedger = supplier;
            e.Lines[1].Side = DrCr.Credit;
            e.Lines[1].AmountText = "5000";
            e.Recalculate();
            Pump(window);

            Assert.False(e.ShowRcmPanel);
            Assert.False(SomeControlShows(window, "Reverse charge — supply kind"),
                "the RCM panel leaked onto an ordinary purchase (ER-13)");
        }
        finally { window.Close(); Cleanup(dir); }
    }

    // ================================================================ the panels must never starve the lines grid

    /// <summary>The realised height of the Dr/Cr lines viewport. Pinned by REFERENCE to the voucher's own
    /// <see cref="VoucherEntryViewModel.Lines"/>, so it can never match the nav list's ScrollViewer by accident.</summary>
    private static double LinesViewportHeight(Window window, VoucherEntryViewModel e) =>
        window.GetVisualDescendants().OfType<ScrollViewer>()
            .Where(sv => sv.IsEffectivelyVisible
                         && sv.Content is ItemsControl ic && ReferenceEquals(ic.ItemsSource, e.Lines))
            .Select(sv => sv.Bounds.Height)
            .DefaultIfEmpty(-1)
            .Max();

    /// <summary>
    /// <b>The layout floor (Phase 9 UI-3 fix 4).</b> The lines ScrollViewer sits in the voucher template's only '*' row
    /// while the GST panels stack in the trailing Auto row below it — so panels showing at once ATE the grid. With TDS +
    /// RCM both on a Purchase the viewport collapsed to 87px (barely two rows) against 385px on a plain voucher, and
    /// entry became unusable exactly when the screen had the most to say.
    /// <para>
    /// A view-model test can never catch this: every property is correct and every control reports
    /// <c>IsEffectivelyVisible</c> — the grid is simply squeezed to nothing. Only measuring the realised viewport does.
    /// </para>
    /// </summary>
    [AvaloniaFact]
    public void The_lines_grid_keeps_a_usable_height_when_two_gst_panels_are_open()
    {
        var (window, vm, dir) = NewWindow("Two Panel Layout Co");
        try
        {
            var c = vm.Company!;
            new TdsTcsService(c).EnableTds(new TdsConfig { Tan = "MUMA12345B" });

            var e = OpenRcmPurchase(window, vm);

            // Make the SAME voucher carry a TDS withholding too, so both panels are on screen at once.
            var fees = c.FindLedgerByName("Legal Fees")!;
            fees.TdsApplicable = true;
            fees.TdsNatureOfPaymentId = c.FindNatureOfPaymentByCode("194J(b)")!.Id;
            var advocate = c.FindLedgerByName("Advocate (Gujarat)")!;
            advocate.DeducteeType = DeducteeType.Firm;
            advocate.PartyPan = "AAPFU0939F";
            e.Recalculate();
            Pump(window);

            Assert.True(e.ShowTdsPanel);
            Assert.True(e.ShowRcmPanel);

            // Both panels are genuinely realised (not merely flagged on the view model).
            Assert.True(SomeControlShows(window, "TDS — Nature of Payment"));
            Assert.True(SomeControlShows(window, "Reverse charge — supply kind"));

            // …and the grid still has room to work in: ~3 rows, never a sliver.
            var height = LinesViewportHeight(window, e);
            Assert.True(height >= 140,
                $"the lines grid collapsed to {height:0.#}px with two GST panels open — voucher entry is unusable");
        }
        finally { window.Close(); Cleanup(dir); }
    }
}
