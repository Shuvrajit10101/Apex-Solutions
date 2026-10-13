using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Desktop.Views;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.Tests;

/// <summary>
/// 🔴 <b>VOUCHER-LEVEL GST REGISTRATION ATTRIBUTION — the vendor's F3 (Company/Tax Registration) at voucher
/// entry</b> (census row <b>6.23</b>; gap register <b>T2-121(i)</b>; schema <b>v66</b>, user ruling 30(4)).
///
/// <para><b>R7 — ATTESTED, opened BY CONTENT 2026-10-13.</b>
/// <c>help.tallysolutions.com/tally-prime/gst-regular-sales/gst-sales-tally/</c>, verbatim: <i>"For TallyPrime
/// 3.0 &amp; later, if you have multiple registrations, press F3 (Company/Tax Registration) and select the
/// registration under which you want to create the voucher."</i></para>
///
/// <para>🔴 <b>WHAT THIS FILE EXISTS TO PROVE, AND WHY A "THE PROPERTY IS SET" TEST WOULD NOT.</b> Before v66
/// <see cref="Voucher.GstRegistrationId"/> was written in exactly TWO places in all of <c>src/</c> — the ITC
/// reversal stamp and the database read-back — and <b>neither was a voucher-entry path</b>. The consequence was
/// not cosmetic: no voucher could ever carry a non-primary registration, so <b>every non-primary registration's
/// GSTR-1, GSTR-3B and ITC gate rendered STRUCTURALLY ZERO</b>, and an empty GSTR-3B is indistinguishable from a
/// valid NIL return an operator could file. So these tests do not assert a property; they
/// <list type="number">
///   <item>press <b>F3</b> through <see cref="MainWindow"/>'s own key handler, so the keystroke is proved to
///   reach the picker rather than being assumed to (this project has shipped an inert Alt+P and a verb button
///   that ran the wrong method);</item>
///   <item>walk the picker with ArrowDown and Enter;</item>
///   <item>read the attribution back <b>off the posted voucher in the book</b>, and then off the <b>saved
///   database</b>;</item>
///   <item>and assert the <b>branch registration's own GSTR-1 stops being zero while the primary's stays
///   zero</b> — the observable artefact the defect was actually about.</item>
/// </list></para>
///
/// <para>Headless-safe: visual-tree and realised-text inspection only, culture-invariant, every path built with
/// <see cref="Path.Combine"/>, so it behaves identically on the ubuntu and macos CI legs.</para>
/// </summary>
public sealed class VoucherGstRegistrationAttributionTests
{
    private static readonly DateOnly FyStart = new(2024, 4, 1);
    private static readonly DateOnly D1 = new(2024, 4, 10);

    private const string Maharashtra = "27";
    private const string Karnataka = "29";

    private static string GstinFor(string stateCode, string pan)
    {
        var body = stateCode + pan + "1Z";
        return body + Gstin.ComputeCheckDigit(body + "0");
    }

    private static readonly string GstinPrimary = GstinFor(Maharashtra, "AAPFU0939F");
    private static readonly string GstinBranch = GstinFor(Karnataka, "AAACC1206D");

    // ---------------------------------------------------------------- scaffolding

    private static void Pump(Window w)
    {
        Dispatcher.UIThread.RunJobs();
        w.Measure(new Size(1280, 800));
        w.Arrange(new Rect(0, 0, 1280, 800));
        Dispatcher.UIThread.RunJobs();
    }

    private static IEnumerable<Visual> Descendants(Visual v)
    {
        foreach (var c in v.GetVisualChildren())
        {
            yield return c;
            foreach (var g in Descendants(c)) yield return g;
        }
    }

    private static bool ScreenShows(Window w, string fragment) =>
        Descendants(w)
            .OfType<TextBlock>()
            .Where(t => t.IsEffectivelyVisible && t.Bounds.Width > 0 && t.Bounds.Height > 0)
            .Any(t => (t.Text ?? string.Empty).Contains(fragment, StringComparison.Ordinal));

    /// <summary>A GST-enabled Maharashtra company; <paramref name="withBranch"/> adds a Karnataka registration.</summary>
    private static (MainWindow Window, MainWindowViewModel Vm, string Dir, Guid BranchId) NewWindow(
        string tag, bool withBranch)
    {
        var dir = Path.Combine(Path.GetTempPath(), "ApexRegAttr_" + tag + "_" + Guid.NewGuid().ToString("N"));
        var vm = new MainWindowViewModel(new CompanyStorage(dir));
        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 800 };
        window.Show();

        vm.NewCompanyName = tag + " Co";
        vm.CreateCompany();
        var c = vm.Company!;
        c.FinancialYearStart = FyStart;
        c.BooksBeginFrom = FyStart;

        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = Maharashtra,
            Gstin = GstinPrimary,
            RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart,
            Periodicity = GstReturnPeriodicity.Monthly,
        });

        var branchId = Guid.Empty;
        if (withBranch)
        {
            branchId = Guid.NewGuid();
            c.Gst!.AddRegistration(new GstRegistration(
                branchId, "Karnataka Registration", Karnataka, GstinBranch,
                GstRegistrationType.Regular, FyStart));
            c.Gst!.EnsureValid();
        }

        vm.ShowGateway();
        Pump(window);
        return (window, vm, dir, branchId);
    }

    private static void Cleanup(MainWindow w, string dir)
    {
        w.Close();
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static DomainLedger AddLedger(Company c, string name, string groupName, bool openingIsDebit)
    {
        var l = new DomainLedger(Guid.NewGuid(), name, c.FindGroupByName(groupName)!.Id, Money.Zero, openingIsDebit);
        c.AddLedger(l);
        return l;
    }

    /// <summary>Opens a Sales voucher and keys a taxable ₹10,000 intra-state supply on the plain Dr/Cr grid.</summary>
    private static VoucherEntryViewModel OpenKeyedSales(MainWindow w, MainWindowViewModel vm)
    {
        var c = vm.Company!;
        var customer = c.FindLedgerByName("Acme Ltd") ?? AddLedger(c, "Acme Ltd", "Sundry Debtors", true);
        customer.PartyGst = new PartyGstDetails
        {
            RegistrationType = GstRegistrationType.Regular,
            Gstin = GstinPrimary,
            StateCode = Maharashtra,
        };
        var sales = c.FindLedgerByName("Sales") ?? AddLedger(c, "Sales", "Sales Accounts", false);
        // 🔴 AN EXEMPT SERVICE-INCOME LEDGER, DELIBERATELY, AND THE REASON IS THE ASSERTION DOWNSTREAM. A plain
        // Dr/Cr Sales voucher whose income ledger declares NO GST block at all contributes to no GSTR-1 bucket
        // whatsoever — Gstr1.AccumulateHsn's own comment says so and explains why (attributing an 18% ledger's
        // plain-grid value to the exempt column would be "a positive misstatement"). Declaring the ledger
        // genuinely exempt/nil is what puts a real, filed figure on the return, so the registration-scoping
        // assertion lands on an emitted artefact rather than on a bucket that is empty for an unrelated reason.
        sales.SalesPurchaseGst = new StockItemGstDetails
        {
            Taxability = GstTaxability.Exempt,
            SupplyType = GstSupplyType.Services,
            HsnSac = "9983",
        };

        vm.OpenVoucher(VoucherBaseType.Sales);
        var e = vm.VoucherEntry!;
        e.Date = D1;
        e.Lines[0].SelectedLedger = customer;
        e.Lines[0].Side = DrCr.Debit;
        e.Lines[0].AmountText = "10000";
        e.Lines[1].SelectedLedger = sales;
        e.Lines[1].Side = DrCr.Credit;
        e.Lines[1].AmountText = "10000";
        e.Recalculate();
        Pump(w);
        return e;
    }

    /// <summary>ArrowDown until the active cascade column highlights <paramref name="fragment"/>, then Enter.</summary>
    private static void KeyboardTake(MainWindow w, MainWindowViewModel vm, string fragment)
    {
        var column = vm.Columns[vm.ActiveColumnIndex];
        var offered = column.Items.Where(i => i.IsSelectable).Select(i => i.Label).ToList();
        Assert.True(
            offered.Any(l => l.Contains(fragment, StringComparison.Ordinal)),
            $"The picker never offered '{fragment}'. It offers: {string.Join(" | ", offered)}. "
            + "A column with no such row cannot be driven by an operator.");

        for (var i = 0; i <= column.Items.Count; i++)
        {
            if (column.Selected is { } sel && sel.Label.Contains(fragment, StringComparison.Ordinal))
            {
                w.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
                Pump(w);
                return;
            }
            w.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
            Pump(w);
        }

        Assert.Fail($"ArrowDown never landed the highlight on '{fragment}' within one lap of the column.");
    }

    // ================================================================= the headline

    /// <summary>
    /// 🔴 <b>THE ROW-6.23 / T2-121(i) TEST.</b> F3 on a live voucher, ArrowDown onto the Karnataka
    /// registration, Enter, Ctrl+A — and the voucher read back off the book carries that registration, which is
    /// a thing no voucher-entry path in this product could do before v66.
    /// </summary>
    [AvaloniaFact]
    public void F3_at_voucher_entry_records_the_voucher_under_the_chosen_registration()
    {
        var (w, vm, dir, branchId) = NewWindow("F3Pick", withBranch: true);
        try
        {
            var e = OpenKeyedSales(w, vm);

            // Before F3: the default is the company's own first registration, exactly as every pre-v66 book's
            // every voucher attributes (NULL means the primary).
            Assert.Equal(GstRegistration.PrimaryId, e.SelectedGstRegistrationId);
            Assert.True(e.CanSelectGstRegistration);

            // 🔴 THE KEYSTROKE, THROUGH THE WINDOW'S OWN HANDLER. Not vm.OpenVoucherRegistrationPicker() — a
            // method call would stay green on the day the F3 button-bar row went missing, and the row is what
            // Fire() looks up. The button bar is checked explicitly below for the same reason.
            Assert.Contains(vm.ButtonBar, b => b.Key == "F3" && b.Caption == "Tax Regn" && b.Enabled);
            w.KeyPressQwerty(PhysicalKey.F3, RawInputModifiers.None);
            Pump(w);

            Assert.Equal(Screen.GstRegistrationPicker, vm.CurrentScreen);
            // BOTH registrations are offered — the primary is not a hidden special case.
            var rows = vm.Columns[vm.ActiveColumnIndex].Items
                .Where(i => i.IsSelectable).Select(i => i.Label).ToList();
            Assert.Equal(2, rows.Count);
            Assert.Contains(rows, l => l.Contains("Karnataka Registration", StringComparison.Ordinal));
            // …and the picker names the GSTIN, so the operator can tell two same-named registrations apart.
            Assert.True(ScreenShows(w, GstinBranch),
                "the picker does not render the branch GSTIN — the operator cannot see which taxpayer "
                + "identity the document is about to be issued under");

            KeyboardTake(w, vm, "Karnataka Registration");

            // The picker popped and the SAME half-keyed voucher is live underneath, lines intact.
            Assert.Equal(Screen.VoucherEntry, vm.CurrentScreen);
            Assert.Same(e, vm.VoucherEntry);
            Assert.Equal("10000", e.Lines[0].AmountText);
            Assert.Equal(branchId, e.SelectedGstRegistrationId);

            // Post it.
            Assert.True(e.Accept());

            // 🔴 THE OBSERVABLE ARTEFACT: the POSTED voucher, read back off the book — not the view model.
            var posted = Assert.Single(vm.Company!.Vouchers);
            Assert.Equal(branchId, posted.GstRegistrationId);
            Assert.Equal(branchId, GstReportSupport.RegistrationOf(posted));
        }
        finally { Cleanup(w, dir); }
    }

    /// <summary>
    /// 🔴 <b>THE DEFECT, IN THE ARTEFACT THAT WAS WRONG.</b> The finding behind T2-121(i) was not "a property is
    /// unset" — it was that <b>a non-primary registration's return rendered STRUCTURALLY ZERO</b>, and an empty
    /// GSTR-3B looks exactly like a valid NIL return. This test posts the same ₹10,000 supply under the branch
    /// and asserts the two registrations' GSTR-1s have SWAPPED relative to pre-v66 behaviour: the branch's is no
    /// longer zero, and the primary's now is.
    /// </summary>
    [AvaloniaFact]
    public void A_voucher_recorded_under_the_branch_lands_in_the_BRANCH_return_and_not_the_primary_s()
    {
        var (w, vm, dir, branchId) = NewWindow("BranchReturn", withBranch: true);
        try
        {
            var e = OpenKeyedSales(w, vm);
            Assert.Null(e.SelectGstRegistration(branchId));
            Assert.True(e.Accept());

            var c = vm.Company!;
            var from = FyStart;
            var to = new DateOnly(2025, 3, 31);

            var branchReturn = Gstr1.Build(c, from, to, branchId);
            var primaryReturn = Gstr1.Build(c, from, to, GstRegistration.PrimaryId);

            // The declared outward value on the return, however the supply buckets (rate rows for a taxable
            // supply, the exempt/nil bucket for one carrying no tax line). Summed this way so the assertion is
            // about WHICH RETURN DECLARES THE SUPPLY — the thing the defect was about — rather than about which
            // table it lands in, which is a different row's business.
            static decimal Declared(Gstr1 r) =>
                r.ExemptNilNonGstValue.Amount + r.RateSummary.Sum(x => x.TaxableValue.Amount);

            // 🔴 The branch's return now carries the supply. Before v66 this figure was ZERO for every
            // non-primary registration of every company, because no entry path could stamp one — and a zero
            // GSTR-1/3B is indistinguishable from a valid NIL return an operator could file.
            Assert.True(Declared(branchReturn) > 0m,
                "the branch registration's GSTR-1 is still structurally zero — the attribution never reached "
                + "the posted voucher, which is the whole of T2-121(i)");
            Assert.Equal(10000m, Declared(branchReturn));
            // …and it is NOT also counted into the primary's. A supply appearing in BOTH returns would be the
            // same ₹10,000 declared twice, under two different GSTINs.
            Assert.Equal(0m, Declared(primaryReturn));
        }
        finally { Cleanup(w, dir); }
    }

    /// <summary>
    /// <b>ER-13 — a single-registration book sees NOTHING NEW.</b> The vendor scopes its own instruction with
    /// <i>"if you have multiple registrations"</i>, so on a one-GSTIN company F3 stays <i>Change Company</i>,
    /// no badge is painted on the voucher, and the posted voucher's attribution stays <c>null</c> — which is
    /// what every voucher in every book written before v61 already holds.
    /// </summary>
    [AvaloniaFact]
    public void A_single_registration_book_keeps_F3_as_Change_Company_and_posts_an_unstamped_voucher()
    {
        var (w, vm, dir, _) = NewWindow("SingleReg", withBranch: false);
        try
        {
            var e = OpenKeyedSales(w, vm);

            Assert.False(e.CanSelectGstRegistration);
            Assert.False(vm.IsVoucherRegistrationSelectable);
            Assert.Contains(vm.ButtonBar, b => b.Key == "F3" && b.Caption == "Company");
            Assert.DoesNotContain(vm.ButtonBar, b => b.Key == "F3" && b.Caption == "Tax Regn");
            // Exactly ONE F3 row — a second would shadow the first in Fire()'s first-match lookup.
            Assert.Single(vm.ButtonBar, b => b.Key == "F3");

            Assert.True(e.Accept());

            // 🔴 NULL, not Guid.Empty. NULL is the contract vouchers.gst_registration_id has carried since v61
            // ("the company's own first registration") and is why no book was ever back-filled; writing the
            // all-zero Guid would also break the column's REFERENCES gst_registrations(id) clause.
            var posted = Assert.Single(vm.Company!.Vouchers);
            Assert.Null(posted.GstRegistrationId);
            Assert.Equal(GstRegistration.PrimaryId, GstReportSupport.RegistrationOf(posted));
        }
        finally { Cleanup(w, dir); }
    }

    /// <summary>
    /// <b>The attribution survives the save and the reopen</b> — so it is on disk, not only on the aggregate.
    /// This is the half that the storage column exists for, and the half a view-model test cannot see.
    /// </summary>
    [AvaloniaFact]
    public void The_attribution_round_trips_through_the_saved_database()
    {
        var (w, vm, dir, branchId) = NewWindow("RoundTrip", withBranch: true);
        try
        {
            var e = OpenKeyedSales(w, vm);
            Assert.Null(e.SelectGstRegistration(branchId));
            Assert.True(e.Accept());   // Accept saves

            var storage = new CompanyStorage(dir);
            var entry = storage.ListCompanies().Single(x => x.Name == vm.Company!.Name);
            var reopened = storage.Load(entry);

            var posted = Assert.Single(reopened.Vouchers);
            Assert.Equal(branchId, posted.GstRegistrationId);
            // And the registration itself survived with its GSTIN, so the reopened book can still file for it.
            Assert.Equal(GstinBranch, reopened.Gst!.FindRegistration(branchId)!.Gstin);
        }
        finally { Cleanup(w, dir); }
    }

    /// <summary>
    /// 🔴 <b>CENSUS ROW 6.24's STATED PRECONDITION, MEASURED RATHER THAN CLAIMED.</b> That row's cell gives the
    /// reason ISD was sequenced where it was: <i>"ISD is credit <b>distribution</b> across registrations; it was
    /// sequenced behind 6.23 and 6.23 landed <c>PARTIAL</c> with <b>no voucher-side registration
    /// attribution</b>, so there is no distribution flow for an ISD return to project."</i> This test drives the
    /// operator route end to end and shows that precondition is now met: F3 → the ISD registration → an input
    /// service → Ctrl+A, and <c>Gstr6.Build</c> reports the credit.
    ///
    /// <para>🔴 <b>AND HERE IS WHAT THIS DOES <i>NOT</i> SHOW, STATED PLAINLY SO THE CLAIM IS NOT READ WIDER
    /// THAN IT IS.</b> The GSTR-6 engine was <b>already</b> exercised with non-zero data —
    /// <c>Gstr6IsdReturnTests</c> hand-sets <c>GstRegistrationId = isd.Id</c> on a hand-built voucher and pins
    /// ₹4,500 / ₹4,500 / ₹9,000 received plus the whole Rule-39 distribution. So this change does <b>not</b>
    /// make a never-tested report live; it supplies the <b>operator route to the data the engine was already
    /// tested against</b>. That distinction is the difference between a true statement and an over-claim, and
    /// row 6.24 is <b>not</b> proposed for promotion on the strength of it: the ISD slice's own blocking
    /// findings (<c>T1-72</c> and the Rule 39(1)(c)/(k)/(l) and Table 6A/6B/6C/9/10 gaps that
    /// <c>T2-121(ix)</c> calls "its own slice") are untouched by this wave.</para>
    /// </summary>
    [AvaloniaFact]
    public void A_voucher_recorded_under_an_ISD_registration_reaches_the_GSTR_6_the_engine_already_computes()
    {
        var (w, vm, dir, _) = NewWindow("IsdRoute", withBranch: true);
        try
        {
            var c = vm.Company!;
            // A head-office ISD beside the operating registration — CBIC's own ABC Ltd shape.
            var isdId = Guid.NewGuid();
            c.Gst!.AddRegistration(new GstRegistration(
                isdId, "Head Office (ISD)", Maharashtra, GstinFor(Maharashtra, "AAACS1206D"),
                GstRegistrationType.InputServiceDistributor, FyStart));
            c.Gst!.EnsureValid();

            var supplier = AddLedger(c, "Service Supplier", "Sundry Creditors", false);
            supplier.PartyGst = new PartyGstDetails
            {
                RegistrationType = GstRegistrationType.Regular,
                Gstin = GstinFor(Maharashtra, "AAACT1206D"),
                StateCode = Maharashtra,
            };
            var services = AddLedger(c, "Software Licence", "Indirect Expenses", true);
            services.SalesPurchaseGst = new StockItemGstDetails
            {
                Taxability = GstTaxability.Taxable,
                RateBasisPoints = 1800,
                SupplyType = GstSupplyType.Services,
                HsnSac = "9983",
            };

            // 🔴 THE ACCOUNTING (SERVICE) INVOICE MODE, AND THE ROUTE MATTERS — a first draft of this test keyed
            // the four legs by hand on the plain Dr/Cr grid and GSTR-6 reported 0.00. That was the test being
            // wrong, not the code: Gstr6 reads tax off each EntryLine's `Gst` STAMP, and the plain grid posts no
            // stamp (Gstr1.AccumulateHsn says so in terms — "the plain grid posts NO tax legs"). An ISD receives
            // input SERVICES, so the accounting-invoice mode is its real entry route, and it is available on
            // Purchase (CanBeAccountingInvoice = Sales or Purchase; the purchase arm was gated off once and was
            // re-opened when TDS/RCM were wired to the Particulars lines).
            vm.OpenVoucher(VoucherBaseType.Purchase);
            var e = vm.VoucherEntry!;
            e.Mode = VoucherEntryMode.AccountingInvoice;
            e.Date = D1;
            e.SelectedParty = e.Parties.Single(p => p.Ledger?.Id == supplier.Id);
            var leg = e.AccountingInvoiceLines[0];
            leg.SelectedLedger = e.AccountingInvoiceLedgers.Single(l => l.Id == services.Id);
            leg.AmountText = "50000";
            e.RecalculateAccountingInvoice();
            Pump(w);

            // 🔴 F3 → the ISD. The picker offers it, because an ISD is exactly the registration that RECEIVES
            // input-service invoices — that is its whole statutory function (§20 / Rule 39).
            Assert.Contains(e.AvailableGstRegistrations, r => r.Id == isdId);
            Assert.Null(e.SelectGstRegistration(isdId));
            Assert.True(e.Accept(), e.Message);

            var posted = Assert.Single(c.Vouchers);
            Assert.Equal(isdId, posted.GstRegistrationId);

            // 🔴 AND THE RETURN REPORTS IT. Before this, no entry path could stamp a non-primary registration,
            // an ISD can only ever BE one, and Gstr6.Build sweeps PostedDirectionalVouchers scoped to it — so
            // for an operator the sweep was necessarily empty.
            var r6 = Gstr6.Build(c, FyStart, new DateOnly(2025, 3, 31), isdId);
            Assert.Equal(4_500m, r6.ReceivedCgst.Amount);
            Assert.Equal(4_500m, r6.ReceivedSgst.Amount);
            Assert.Equal(9_000m, r6.TotalReceived.Amount);
        }
        finally { Cleanup(w, dir); }
    }

    /// <summary>
    /// 🔴 <b>AN ALTERATION CANNOT MOVE A VOUCHER BETWEEN REGISTRATIONS, AND IT SAYS SO BY NAME.</b> This refusal
    /// is OURS, not the vendor's: its page describes F3 for <i>creating</i> a voucher and says nothing about a
    /// posted one. Moving one is not a field edit — it moves a document out of one filed return and into
    /// another under a different GSTIN. So the posted attribution is CARRIED onto the replacement, exactly as
    /// <c>LedgerService.Replace</c> §12.8 carries the provisional-state vector, and the keystroke explains
    /// itself instead of appearing to work. Labelled in <c>docs/invented-vs-cloned.md</c>.
    /// </summary>
    [AvaloniaFact]
    public void Altering_a_posted_voucher_refuses_the_registration_change_by_name_and_carries_the_original()
    {
        var (w, vm, dir, branchId) = NewWindow("AlterLock", withBranch: true);
        try
        {
            // Post one under the BRANCH.
            var e = OpenKeyedSales(w, vm);
            Assert.Null(e.SelectGstRegistration(branchId));
            Assert.True(e.Accept());
            var postedId = Assert.Single(vm.Company!.Vouchers).Id;

            // 🔴 RE-OPENED THROUGH THE SHELL'S OWN ALTERATION DOOR — the Day Book row plus
            // RequestAlterHighlightedVoucher — not by newing an entry view model. The refusal has to hold on the
            // surface an operator can actually reach.
            vm.OpenReport(ReportKind.DayBook);
            Pump(w);
            vm.Reports!.SelectedRow = vm.Reports.Rows.First(r => r.DrillVoucherId == postedId);
            Assert.Equal(VoucherAlterationRequest.Opened, vm.RequestAlterHighlightedVoucher());
            Pump(w);

            var alter = vm.VoucherEntry!;
            Assert.True(alter.IsAltering);
            Assert.Equal(Screen.VoucherEntry, vm.CurrentScreen);

            // Seeded from the posted voucher, so the screen SAYS which registration's return it belongs to…
            Assert.Equal(branchId, alter.SelectedGstRegistrationId);
            Assert.Contains(GstinBranch, alter.GstRegistrationCaption, StringComparison.Ordinal);

            // …and refuses to move it, with the named sentence rather than silently.
            var refusal = alter.SelectGstRegistration(GstRegistration.PrimaryId);
            Assert.Equal(VoucherEntryViewModel.RegistrationLockedOnAlterationRefusal, refusal);
            Assert.Equal(branchId, alter.SelectedGstRegistrationId);

            // 🔴 AND THE F3 KEYSTROKE ITSELF REFUSES rather than opening a picker whose every row would be
            // refused one keystroke later. The notice bar carries the reason — a dead key would not.
            w.KeyPressQwerty(PhysicalKey.F3, RawInputModifiers.None);
            Pump(w);
            Assert.NotEqual(Screen.GstRegistrationPicker, vm.CurrentScreen);
            Assert.Equal(Screen.VoucherEntry, vm.CurrentScreen);
            Assert.Equal(VoucherEntryViewModel.RegistrationLockedOnAlterationRefusal, vm.Notice);

            // And the ALTERATION, once accepted, carries the branch attribution rather than dropping it to the
            // primary.
            //
            // 🔴 WHAT THIS PINS, STATED EXACTLY, BECAUSE IT IS NOT WHAT IT LOOKS LIKE. It pins the SEED in
            // RehydrateFrom, not the `carryFrom:` argument at the alteration's StampGstRegistration call.
            // MEASURED: dropping `carryFrom: existing` leaves this file green at 5/5 (the seed already holds
            // the posted value and SelectGstRegistration refuses every reachable attempt to move it), while
            // removing the seed line reddens exactly this assertion with
            // `Expected: 7e5ce78c-… / Actual: 00000000-…`. Said out loud so nobody reads this test as covering
            // the `carryFrom` argument — that argument is labelled defence in depth at its own call site.
            alter.Narration = "altered";
            // AcceptAlteration, not Accept — Accept refuses outright on an altering screen ("accepting it as a
            // new entry would post a SECOND voucher"), which is the correct refusal and not this test's subject.
            Assert.True(alter.AcceptAlteration(), alter.Message);
            Assert.Equal(branchId, vm.Company!.FindVoucher(postedId)!.GstRegistrationId);
        }
        finally { Cleanup(w, dir); }
    }
}
