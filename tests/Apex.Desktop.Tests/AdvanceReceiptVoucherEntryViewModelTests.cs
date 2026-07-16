using System;
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
/// Phase 9 UI-3 — the <b>GST-on-advances voucher-entry UI</b> (<see cref="VoucherEntryViewModel"/>; RQ-25; Rule 50/51).
/// Proves the Receipt screen books a Rule-50 advance through the SAME <see cref="AdvanceReceiptService"/> the posting
/// uses (ER-4) — appending the self-balancing <c>Cr Output {head}</c> / <c>Dr Output Tax on Advances</c> pair paisa-exact
/// — that a goods advance is de-taxed (Notn 66/2017), and that a Journal <b>adjusts</b> the advance against the invoice
/// (→ 11B) while a Payment <b>refunds</b> it (Rule 51).
/// <para>
/// Also locks the engine guards surfacing cleanly (a double adjustment, a partial adjustment), the ER-13 gates, and the
/// <b>rollback</b>: the advance engine mutates the company, so a voucher the engine then REFUSES must leave no phantom
/// advance behind.
/// </para>
/// </summary>
public sealed class AdvanceReceiptVoucherEntryViewModelTests : IDisposable
{
    private const string GstinMaharashtra = "27AAPFU0939F1ZV";
    private static readonly DateOnly FyStart = new(2024, 4, 1);
    private static readonly DateOnly D1 = new(2024, 6, 10);

    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public AdvanceReceiptVoucherEntryViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexAdvVoucherTests_" + Guid.NewGuid().ToString("N"));
        _storage = new CompanyStorage(_tempDir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch { /* best-effort cleanup */ }
    }

    private static DomainLedger AddLedger(Company c, string name, string groupName, bool openingIsDebit)
    {
        var l = new DomainLedger(Guid.NewGuid(), name, c.FindGroupByName(groupName)!.Id, Money.Zero, openingIsDebit);
        c.AddLedger(l);
        return l;
    }

    private MainWindowViewModel NewCompany(string name, bool enableGst = true)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = name;
        vm.CreateCompany();
        var c = vm.Company!;
        c.FinancialYearStart = FyStart;
        c.BooksBeginFrom = FyStart;
        if (enableGst)
            new GstService(c).EnableGst(new GstConfig
            {
                HomeStateCode = "27",
                Gstin = GstinMaharashtra,
                RegistrationType = GstRegistrationType.Regular,
                ApplicableFrom = FyStart,
                Periodicity = GstReturnPeriodicity.Monthly,
            });
        return vm;
    }

    /// <summary>Opens a Receipt and types the ordinary Rule-50 legs: Dr Bank (gross) / Cr Advance from customer (gross).</summary>
    private static VoucherEntryViewModel OpenReceipt(
        MainWindowViewModel vm, DomainLedger bank, DomainLedger advanceLedger, decimal gross)
    {
        vm.OpenVoucher(VoucherBaseType.Receipt);
        var e = vm.VoucherEntry!;
        e.Date = D1;
        e.Lines[0].SelectedLedger = bank;
        e.Lines[0].Side = DrCr.Debit;
        e.Lines[0].AmountText = gross.ToString(System.Globalization.CultureInfo.InvariantCulture);
        e.Lines[1].SelectedLedger = advanceLedger;
        e.Lines[1].Side = DrCr.Credit;
        e.Lines[1].AmountText = gross.ToString(System.Globalization.CultureInfo.InvariantCulture);
        e.Recalculate();
        return e;
    }

    private static VoucherEntryViewModel OpenPlain(
        MainWindowViewModel vm, VoucherBaseType type, DomainLedger dr, DomainLedger cr, decimal amount)
    {
        vm.OpenVoucher(type);
        var e = vm.VoucherEntry!;
        e.Date = D1;
        e.Lines[0].SelectedLedger = dr;
        e.Lines[0].Side = DrCr.Debit;
        e.Lines[0].AmountText = amount.ToString(System.Globalization.CultureInfo.InvariantCulture);
        e.Lines[1].SelectedLedger = cr;
        e.Lines[1].Side = DrCr.Credit;
        e.Lines[1].AmountText = amount.ToString(System.Globalization.CultureInfo.InvariantCulture);
        e.Recalculate();
        return e;
    }

    private static Voucher LastVoucher(Company c) => c.Vouchers.OrderBy(v => v.Number).Last();

    private static Money AmountOn(Voucher v, Guid ledgerId, DrCr side) =>
        v.Lines.Where(l => l.LedgerId == ledgerId && l.Side == side)
            .Aggregate(Money.Zero, (a, l) => a + l.Amount);

    /// <summary>Books a ₹10,000 net inter-state service advance through the UI and returns the resulting record.</summary>
    private (MainWindowViewModel Vm, GstAdvanceReceipt Advance, DomainLedger Bank, DomainLedger AdvLedger) BookAdvance(
        string name, decimal net = 10000m, bool interState = true)
    {
        var vm = NewCompany(name);
        var c = vm.Company!;
        var bank = c.FindLedgerByName("Cash") ?? AddLedger(c, "Bank", "Bank Accounts", true);
        var advLedger = AddLedger(c, "Advance from customer", "Current Liabilities", false);

        var tax = net * 0.18m;
        var e = OpenReceipt(vm, bank, advLedger, net + tax);
        e.IsAdvanceReceipt = true;
        e.AdvanceIsService = true;
        e.AdvanceAmountText = net.ToString(System.Globalization.CultureInfo.InvariantCulture);
        e.AdvanceInterState = interState;
        Assert.True(e.Accept());

        return (vm, c.AdvanceReceipts.Single(), bank, advLedger);
    }

    // ================================================================ ER-13 — the advance is opt-in

    /// <summary>ER-13: a GST-off company never offers the advance opt-in, and an ordinary receipt posts verbatim.</summary>
    [Fact]
    public void Advance_optin_is_absent_on_a_gst_off_company()
    {
        var vm = NewCompany("No-GST Receipt Co", enableGst: false);
        var c = vm.Company!;
        var bank = c.FindLedgerByName("Cash")!;
        var customer = AddLedger(c, "Acme Ltd", "Sundry Debtors", true);

        var e = OpenReceipt(vm, bank, customer, 11800m);

        Assert.False(e.CanBeAdvanceReceipt);
        Assert.False(e.ShowAdvanceReceiptDetails);
        Assert.True(e.Accept());
        Assert.Equal(2, LastVoucher(c).Lines.Count);
        Assert.Empty(c.AdvanceReceipts);
    }

    /// <summary>ER-13: on a GST company the opt-in is offered but OFF, so an ordinary receipt still posts verbatim —
    /// no advance record, and crucially no "Output Tax on Advances" suspense ledger conjured into the ledger set.</summary>
    [Fact]
    public void Ordinary_receipt_on_a_gst_company_posts_unchanged()
    {
        var vm = NewCompany("Plain Receipt Co");
        var c = vm.Company!;
        var bank = c.FindLedgerByName("Cash")!;
        var customer = AddLedger(c, "Acme Ltd", "Sundry Debtors", true);
        var ledgersBefore = c.Ledgers.Count;

        var e = OpenReceipt(vm, bank, customer, 11800m);

        Assert.True(e.CanBeAdvanceReceipt);
        Assert.False(e.IsAdvanceReceipt);
        Assert.True(e.Accept());

        Assert.Equal(2, LastVoucher(c).Lines.Count);
        Assert.Empty(c.AdvanceReceipts);
        Assert.Equal(ledgersBefore, c.Ledgers.Count);
        Assert.Null(c.FindLedgerByName(GstService.AdvanceTaxSuspenseLedgerName));
    }

    // ================================================================ (a) booking the Rule-50 advance

    /// <summary>
    /// A ₹10,000 inter-state <b>service</b> advance: the panel previews ₹1,800 tax / ₹11,800 gross, and Accept appends
    /// the self-balancing pair — Cr Output IGST ₹1,800 + Dr Output Tax on Advances ₹1,800 — on top of the operator's own
    /// Dr Bank / Cr Advance legs, leaving the voucher balanced and the record ready for GSTR-1 11A.
    /// </summary>
    [Fact]
    public void Service_advance_previews_and_posts_the_self_balancing_tax_pair()
    {
        var vm = NewCompany("Advance Service Co");
        var c = vm.Company!;
        var bank = c.FindLedgerByName("Cash")!;
        var advLedger = AddLedger(c, "Advance from customer", "Current Liabilities", false);

        var e = OpenReceipt(vm, bank, advLedger, 11800m);
        e.IsAdvanceReceipt = true;
        e.AdvanceAmountText = "10000";
        e.AdvanceInterState = true;
        e.AdvancePlaceOfSupplyStateCode = "24";

        // ---- the live preview
        Assert.True(e.ShowAdvanceReceiptDetails);
        Assert.Equal("1,800.00", e.AdvanceTaxText);
        Assert.Equal("11,800.00", e.AdvanceGrossText);
        Assert.Contains("11A", e.AdvanceSummary);

        Assert.True(e.Accept());

        // ---- the posted pair, paisa-exact
        var v = LastVoucher(c);
        var gst = new GstService(c);
        var outputIgst = gst.FindTaxLedger(GstTaxHead.Integrated, GstTaxDirection.Output)!;
        var suspense = c.FindLedgerByName(GstService.AdvanceTaxSuspenseLedgerName)!;

        Assert.True(VoucherValidator.IsBalanced(v));
        Assert.Equal(4, v.Lines.Count);
        Assert.Equal(1800.00m, AmountOn(v, outputIgst.Id, DrCr.Credit).Amount);  // the liability, due now
        Assert.Equal(1800.00m, AmountOn(v, suspense.Id, DrCr.Debit).Amount);     // the suspense, released on adjustment
        Assert.Equal(11800.00m, AmountOn(v, bank.Id, DrCr.Debit).Amount);        // the operator's legs, untouched
        Assert.Equal(11800.00m, AmountOn(v, advLedger.Id, DrCr.Credit).Amount);

        // ---- the 11A/11B source record
        var record = Assert.Single(c.AdvanceReceipts);
        Assert.Equal(v.Id, record.ReceiptVoucherId);
        Assert.True(record.IsService);
        Assert.Equal(10000.00m, record.AdvanceAmount.Amount);
        Assert.Equal(1800.00m, record.AdvanceTax.Amount);
        Assert.Equal(1800, record.RateBasisPoints);
        Assert.True(record.InterState);
        Assert.Equal("24", record.PlaceOfSupplyStateCode);
        Assert.Null(record.AdjustedAgainstInvoiceVoucherId);
    }

    /// <summary>An intra-state service advance splits the same total into CGST+SGST (₹900 + ₹900).</summary>
    [Fact]
    public void Intrastate_service_advance_splits_cgst_sgst()
    {
        var vm = NewCompany("Advance Intra Co");
        var c = vm.Company!;
        var bank = c.FindLedgerByName("Cash")!;
        var advLedger = AddLedger(c, "Advance from customer", "Current Liabilities", false);

        var e = OpenReceipt(vm, bank, advLedger, 11800m);
        e.IsAdvanceReceipt = true;
        e.AdvanceAmountText = "10000";
        e.AdvanceInterState = false;

        Assert.Equal("1,800.00", e.AdvanceTaxText);
        Assert.True(e.Accept());

        var v = LastVoucher(c);
        var gst = new GstService(c);
        Assert.True(VoucherValidator.IsBalanced(v));
        Assert.Equal(900.00m, AmountOn(v, gst.FindTaxLedger(GstTaxHead.Central, GstTaxDirection.Output)!.Id, DrCr.Credit).Amount);
        Assert.Equal(900.00m, AmountOn(v, gst.FindTaxLedger(GstTaxHead.State, GstTaxDirection.Output)!.Id, DrCr.Credit).Amount);
        Assert.Equal(1800.00m, AmountOn(v, c.FindLedgerByName(GstService.AdvanceTaxSuspenseLedgerName)!.Id, DrCr.Debit).Amount);
    }

    /// <summary>A <b>goods</b> advance is de-taxed (Notn 66/2017): the panel says so, no tax pair posts, and the record
    /// carries zero tax (so it raises no 11A row).</summary>
    [Fact]
    public void Goods_advance_is_de_taxed()
    {
        var vm = NewCompany("Advance Goods Co");
        var c = vm.Company!;
        var bank = c.FindLedgerByName("Cash")!;
        var advLedger = AddLedger(c, "Advance from customer", "Current Liabilities", false);

        var e = OpenReceipt(vm, bank, advLedger, 10000m);
        e.IsAdvanceReceipt = true;
        e.AdvanceIsService = false; // goods
        e.AdvanceAmountText = "10000";

        Assert.Equal("0.00", e.AdvanceTaxText);
        Assert.Contains("de-taxed", e.AdvanceSummary);
        Assert.True(e.Accept());

        var v = LastVoucher(c);
        Assert.Equal(2, v.Lines.Count); // no tax pair
        var record = Assert.Single(c.AdvanceReceipts);
        Assert.False(record.IsService);
        Assert.Equal(0m, record.AdvanceTax.Amount);
        // A de-taxed advance must not conjure the suspense ledger.
        Assert.Null(c.FindLedgerByName(GstService.AdvanceTaxSuspenseLedgerName));
    }

    /// <summary>An advance opted-in but left blank is refused before the engine is touched.</summary>
    [Fact]
    public void Advance_with_no_amount_is_refused()
    {
        var vm = NewCompany("Blank Advance Co");
        var c = vm.Company!;
        var bank = c.FindLedgerByName("Cash")!;
        var advLedger = AddLedger(c, "Advance from customer", "Current Liabilities", false);

        var e = OpenReceipt(vm, bank, advLedger, 11800m);
        e.IsAdvanceReceipt = true; // …but no amount typed

        Assert.False(e.Accept());
        Assert.Contains("net (ex-tax) advance", e.Message);
        Assert.Empty(c.AdvanceReceipts);
    }

    // ================================================================ (b) adjusting the advance against the invoice

    /// <summary>
    /// A Journal adjusts the advance against the tax invoice (→ GSTR-1 11B): the suspense-releasing reversal — Dr Output
    /// IGST ₹1,800 / Cr Output Tax on Advances ₹1,800 — is appended to the operator's own application legs, and the
    /// record is stamped with the invoice link so the invoice's own output tax is not double-counted.
    /// </summary>
    [Fact]
    public void Journal_adjusts_the_advance_against_the_invoice_and_releases_the_suspense()
    {
        var (vm, advance, _, advLedger) = BookAdvance("Adjust Co");
        var c = vm.Company!;
        var customer = AddLedger(c, "Acme Ltd", "Sundry Debtors", true);
        var sales = c.FindLedgerByName("Sales") ?? AddLedger(c, "Sales", "Sales Accounts", false);

        // The later tax invoice that fully consumes the advance (taxable 10,000 == the advance's net).
        var salesType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id;
        var invoice = new LedgerService(c).Post(new Voucher(
            Guid.NewGuid(), salesType, D1,
            new[]
            {
                new EntryLine(customer.Id, Money.FromRupees(11800m), DrCr.Debit),
                new EntryLine(sales.Id, Money.FromRupees(10000m), DrCr.Credit,
                    gst: new GstLineTax(GstTaxHead.Integrated, 1800, Money.FromRupees(10000m))),
                new EntryLine(new GstService(c).FindTaxLedger(GstTaxHead.Integrated, GstTaxDirection.Output)!.Id,
                    Money.FromRupees(1800m), DrCr.Credit,
                    gst: new GstLineTax(GstTaxHead.Integrated, 1800, Money.FromRupees(10000m))),
            },
            partyId: customer.Id));

        // The application journal: Dr Advance from customer / Cr Acme.
        var e = OpenPlain(vm, VoucherBaseType.Journal, advLedger, customer, 11800m);

        Assert.True(e.ShowAdvanceActionPanel);
        Assert.True(e.ShowAdvanceInvoicePicker);
        Assert.Equal(VoucherEntryViewModel.AdvanceAction.Adjust, e.AdvanceActionForType);

        e.SelectedOutstandingAdvance = e.OutstandingAdvances.Single(o => o.Receipt?.Id == advance.Id);
        e.SelectedAdvanceInvoice = e.AdvanceInvoices.Single(o => o.Invoice?.Id == invoice.Id);
        Assert.Contains("11B", e.AdvanceActionSummary);

        Assert.True(e.Accept());

        var v = LastVoucher(c);
        var gst = new GstService(c);
        Assert.True(VoucherValidator.IsBalanced(v));
        // The reversal releases the suspense and reverses the advance's output recognition.
        Assert.Equal(1800.00m, AmountOn(v, gst.FindTaxLedger(GstTaxHead.Integrated, GstTaxDirection.Output)!.Id, DrCr.Debit).Amount);
        Assert.Equal(1800.00m, AmountOn(v, c.FindLedgerByName(GstService.AdvanceTaxSuspenseLedgerName)!.Id, DrCr.Credit).Amount);

        var updated = c.AdvanceReceipts.Single();
        Assert.Equal(invoice.Id, updated.AdjustedAgainstInvoiceVoucherId);
    }

    /// <summary>Adjusting with no invoice picked is refused (the 11B anchor is mandatory).</summary>
    [Fact]
    public void Adjustment_with_no_invoice_selected_is_refused()
    {
        var (vm, advance, _, advLedger) = BookAdvance("Adjust No-Invoice Co");
        var c = vm.Company!;
        var customer = AddLedger(c, "Acme Ltd", "Sundry Debtors", true);

        var e = OpenPlain(vm, VoucherBaseType.Journal, advLedger, customer, 11800m);
        e.SelectedOutstandingAdvance = e.OutstandingAdvances.Single(o => o.Receipt?.Id == advance.Id);
        // …but no invoice picked.

        Assert.False(e.Accept());
        Assert.Contains("Select the tax invoice", e.Message);
        Assert.Null(c.AdvanceReceipts.Single().AdjustedAgainstInvoiceVoucherId); // record untouched
    }

    /// <summary>
    /// The S2b <b>partial-adjustment</b> guard surfaces cleanly: an invoice whose adjustable taxable value is LESS than
    /// the advance's net would over-reverse the whole advance tax, so the engine refuses — and the screen relays that
    /// refusal instead of crashing. Critically, the refused voucher leaves the advance record <b>untouched</b>.
    /// </summary>
    [Fact]
    public void Partial_adjustment_is_refused_and_the_record_is_left_untouched()
    {
        var (vm, advance, _, advLedger) = BookAdvance("Partial Adjust Co");
        var c = vm.Company!;
        var customer = AddLedger(c, "Acme Ltd", "Sundry Debtors", true);
        var sales = c.FindLedgerByName("Sales") ?? AddLedger(c, "Sales", "Sales Accounts", false);

        // A SMALL invoice (taxable 4,000 < the advance's net 10,000) — a partial adjustment.
        var salesType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id;
        var small = new LedgerService(c).Post(new Voucher(
            Guid.NewGuid(), salesType, D1,
            new[]
            {
                new EntryLine(customer.Id, Money.FromRupees(4000m), DrCr.Debit),
                new EntryLine(sales.Id, Money.FromRupees(4000m), DrCr.Credit,
                    gst: new GstLineTax(GstTaxHead.Integrated, 1800, Money.FromRupees(4000m))),
            },
            partyId: customer.Id));

        var e = OpenPlain(vm, VoucherBaseType.Journal, advLedger, customer, 11800m);
        e.SelectedOutstandingAdvance = e.OutstandingAdvances.Single(o => o.Receipt?.Id == advance.Id);
        e.SelectedAdvanceInvoice = e.AdvanceInvoices.Single(o => o.Invoice?.Id == small.Id);

        Assert.False(e.Accept());
        Assert.Contains("Cannot adjust the advance", e.Message);
        Assert.Contains("Partial advance adjustment is not supported", e.Message);

        // The refusal must leave the advance exactly as it was — still outstanding, still adjustable later.
        var untouched = c.AdvanceReceipts.Single();
        Assert.Null(untouched.AdjustedAgainstInvoiceVoucherId);
        Assert.Null(untouched.RefundVoucherId);
    }

    /// <summary>
    /// An already-adjusted advance can never be adjusted twice. The picker itself is the first line of defence — it only
    /// ever lists OUTSTANDING advances — and the engine guard is the second, surfaced cleanly through a screen opened
    /// <i>before</i> the adjustment happened (a genuinely reachable stale-picker state).
    /// </summary>
    [Fact]
    public void An_advance_adjusted_twice_is_refused()
    {
        var (vm, advance, _, advLedger) = BookAdvance("Double Adjust Co");
        var c = vm.Company!;
        var customer = AddLedger(c, "Acme Ltd", "Sundry Debtors", true);
        var sales = c.FindLedgerByName("Sales") ?? AddLedger(c, "Sales", "Sales Accounts", false);
        var outIgst = new GstService(c).FindTaxLedger(GstTaxHead.Integrated, GstTaxDirection.Output)!;

        var salesType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id;
        var invoice = new LedgerService(c).Post(new Voucher(
            Guid.NewGuid(), salesType, D1,
            new[]
            {
                new EntryLine(customer.Id, Money.FromRupees(11800m), DrCr.Debit),
                new EntryLine(sales.Id, Money.FromRupees(10000m), DrCr.Credit,
                    gst: new GstLineTax(GstTaxHead.Integrated, 1800, Money.FromRupees(10000m))),
                new EntryLine(outIgst.Id, Money.FromRupees(1800m), DrCr.Credit,
                    gst: new GstLineTax(GstTaxHead.Integrated, 1800, Money.FromRupees(10000m))),
            },
            partyId: customer.Id));

        // A STALE screen, opened while the advance was still outstanding (so its picker still lists it).
        var stale = OpenPlain(vm, VoucherBaseType.Journal, advLedger, customer, 11800m);
        stale.SelectedOutstandingAdvance = stale.OutstandingAdvances.Single(o => o.Receipt?.Id == advance.Id);
        stale.SelectedAdvanceInvoice = stale.AdvanceInvoices.Single(o => o.Invoice?.Id == invoice.Id);

        // Meanwhile another screen adjusts it.
        var first = OpenPlain(vm, VoucherBaseType.Journal, advLedger, customer, 11800m);
        first.SelectedOutstandingAdvance = first.OutstandingAdvances.Single(o => o.Receipt?.Id == advance.Id);
        first.SelectedAdvanceInvoice = first.AdvanceInvoices.Single(o => o.Invoice?.Id == invoice.Id);
        Assert.True(first.Accept());

        // The stale screen's second adjustment is refused by the engine, relayed cleanly.
        // (Regression: the picker's snapshot still LOOKS outstanding — the engine's guards read the object handed to
        // them — so passing the snapshot straight through would wave this second adjustment past every guard, then
        // leave two records sharing one id, which the store rejects with a UNIQUE-constraint crash on save. The entry
        // VM re-resolves the advance by id against the live company so the guards see current state.)
        Assert.False(stale.Accept());
        Assert.Contains("already been adjusted", stale.Message);
        Assert.Single(c.AdvanceReceipts);                                   // exactly one record — no duplicate id
        Assert.Equal(invoice.Id, c.AdvanceReceipts.Single().AdjustedAgainstInvoiceVoucherId);
        _storage.Save(c);                                                    // the aggregate is still persistable

        // A freshly-opened screen no longer offers it at all (the picker never lists an adjusted advance).
        var fresh = OpenPlain(vm, VoucherBaseType.Journal, advLedger, customer, 11800m);
        Assert.DoesNotContain(fresh.OutstandingAdvances, o => o.Receipt?.Id == advance.Id);
        Assert.False(fresh.ShowAdvanceActionPanel); // nothing outstanding ⇒ no panel (ER-13)
    }

    // ================================================================ (c) refunding the advance (Rule 51)

    /// <summary>A Payment refunds the advance (Rule 51): the suspense is released, the advance's output recognition is
    /// reversed, and the record is stamped with the refund voucher.</summary>
    [Fact]
    public void Payment_refunds_the_advance_and_releases_the_suspense()
    {
        var (vm, advance, bank, advLedger) = BookAdvance("Refund Co");
        var c = vm.Company!;

        // The refund payment: Dr Advance from customer / Cr Bank.
        var e = OpenPlain(vm, VoucherBaseType.Payment, advLedger, bank, 11800m);

        Assert.True(e.ShowAdvanceActionPanel);
        Assert.False(e.ShowAdvanceInvoicePicker); // a refund needs no invoice
        Assert.Equal(VoucherEntryViewModel.AdvanceAction.Refund, e.AdvanceActionForType);

        e.SelectedOutstandingAdvance = e.OutstandingAdvances.Single(o => o.Receipt?.Id == advance.Id);
        Assert.Contains("Rule 51", e.AdvanceActionSummary);

        Assert.True(e.Accept());

        var v = LastVoucher(c);
        var gst = new GstService(c);
        Assert.True(VoucherValidator.IsBalanced(v));
        Assert.Equal(1800.00m, AmountOn(v, gst.FindTaxLedger(GstTaxHead.Integrated, GstTaxDirection.Output)!.Id, DrCr.Debit).Amount);
        Assert.Equal(1800.00m, AmountOn(v, c.FindLedgerByName(GstService.AdvanceTaxSuspenseLedgerName)!.Id, DrCr.Credit).Amount);

        var updated = c.AdvanceReceipts.Single();
        Assert.Equal(v.Id, updated.RefundVoucherId);   // linked to THIS payment voucher
        Assert.Null(updated.AdjustedAgainstInvoiceVoucherId);
    }

    /// <summary>
    /// <b>The rollback lock.</b> The advance engine is not pure — it registers/replaces records on the company. A voucher
    /// the engine then REFUSES must therefore leave no trace: here an unbalanced grid is rejected by the posting engine,
    /// and the advance record the service had already registered must be rolled back (otherwise a phantom advance would
    /// linger and the next Accept would register a second one).
    /// </summary>
    [Fact]
    public void A_rejected_advance_receipt_leaves_no_phantom_record()
    {
        var vm = NewCompany("Rollback Co");
        var c = vm.Company!;
        var bank = c.FindLedgerByName("Cash")!;
        var advLedger = AddLedger(c, "Advance from customer", "Current Liabilities", false);

        var e = OpenReceipt(vm, bank, advLedger, 11800m);
        e.IsAdvanceReceipt = true;
        e.AdvanceAmountText = "10000";
        e.AdvanceInterState = true;

        // Force the engine to refuse: unbalance the grid behind the VM's own CanAccept gate.
        e.Lines[1].AmountText = "9999";

        Assert.False(e.Accept());
        Assert.Empty(c.AdvanceReceipts);   // the registered record was rolled back
        Assert.Empty(c.Vouchers);          // and nothing posted

        // Re-balancing and accepting must now produce exactly ONE record (not two).
        e.Lines[1].AmountText = "11800";
        e.Recalculate();
        Assert.True(e.Accept());
        Assert.Single(c.AdvanceReceipts);
    }

    /// <summary>
    /// <b>The rollback lock, generalised (Phase 9 UI-3 fix 1).</b> The rollback above only ever ran from Accept's two
    /// <i>engine-refusal</i> catch blocks, so every OTHER refusal exit leaked the mutation the advance engine had
    /// already performed. The narrowest path there is the deadliest: a <b>goods</b> advance is de-taxed (Notn 66/2017),
    /// so the engine registers the record and returns <b>no</b> tax lines — a half-typed receipt is then one line short
    /// and Accept refuses at its own "needs at least two lines" gate, never reaching a catch block.
    /// <para>
    /// The phantom that left behind pointed at a <c>ReceiptVoucherId</c> that was never posted, and
    /// <c>gst_advance_receipts.receipt_voucher_id</c> is <c>NOT NULL REFERENCES vouchers(id)</c> — so the operator
    /// doing exactly what the refusal message asks (add the missing leg, Accept again) hit a FOREIGN KEY violation that
    /// escaped <see cref="VoucherEntryViewModel.Accept"/> uncaught, lost the legitimate voucher, and bricked every
    /// subsequent save for the rest of the session.
    /// </para>
    /// </summary>
    [Fact]
    public void A_goods_advance_refused_before_posting_leaves_no_phantom_and_the_company_stays_saveable()
    {
        var vm = NewCompany("Goods Advance Rollback Co");
        var c = vm.Company!;
        var bank = c.FindLedgerByName("Cash")!;
        var advLedger = AddLedger(c, "Advance from customer", "Current Liabilities", false);

        // A half-typed receipt: only the Dr Bank leg. The credit leg is still blank (an ordinary thing to be mid-entry).
        vm.OpenVoucher(VoucherBaseType.Receipt);
        var e = vm.VoucherEntry!;
        e.Date = D1;
        e.Lines[0].SelectedLedger = bank;
        e.Lines[0].Side = DrCr.Debit;
        e.Lines[0].AmountText = "100000";
        e.IsAdvanceReceipt = true;
        e.AdvanceIsService = false;            // GOODS ⇒ de-taxed ⇒ the engine hands back ZERO tax lines…
        e.AdvanceAmountText = "100000";
        e.Recalculate();

        // …so the voucher is one line short and Accept refuses at its own gate — before the posting engine is touched.
        Assert.False(e.Accept());
        Assert.Contains("at least two lines", e.Message);

        // The refusal must leave NOTHING behind. BuildAdvanceReceipt had already registered the record on the company.
        Assert.Empty(c.AdvanceReceipts);
        Assert.Empty(c.Vouchers);

        // The operator now does exactly what the message asks: adds the credit leg and accepts again.
        e.Lines[1].SelectedLedger = advLedger;
        e.Lines[1].Side = DrCr.Credit;
        e.Lines[1].AmountText = "100000";
        e.Recalculate();

        Assert.True(e.Accept());
        Assert.Single(c.AdvanceReceipts);                                       // ONE record, not two
        Assert.Equal(LastVoucher(c).Id, c.AdvanceReceipts.Single().ReceiptVoucherId); // …pointing at a REAL voucher

        // And the aggregate is still persistable: a dangling receipt_voucher_id FK bricks every save from here on.
        _storage.Save(c);
    }

    /// <summary>
    /// The same leak on the <b>TDS</b> exit (Accept's "Cannot compute TDS" refusal): a Payment that refunds an
    /// outstanding SERVICE advance runs the advance engine FIRST (stamping the record refunded against this voucher),
    /// then asks <see cref="TdsService.BuildCarveOut"/> for the withholding — which refuses when the assessable value
    /// dwarfs the party's gross obligation (TDS ≥ what is payable). That refusal exit rolled nothing back, permanently
    /// marking a REAL advance refunded against a voucher that never existed — so its suspense could never be released,
    /// by this screen or any other.
    /// </summary>
    [Fact]
    public void A_refused_tds_carve_out_does_not_strand_the_advance_it_was_about_to_refund()
    {
        var (vm, advance, bank, advLedger) = BookAdvance("Stranded Refund Co");
        var c = vm.Company!;
        var postedSoFar = c.Vouchers.Count;

        // A TDS-enabled company with a 194J(b) expense ledger + a deductee vendor.
        new TdsTcsService(c).EnableTds(new TdsConfig { Tan = "MUMA12345B" });
        var fees = AddLedger(c, "Professional Fees", "Indirect Expenses", true);
        fees.TdsApplicable = true;
        fees.TdsNatureOfPaymentId = c.FindNatureOfPaymentByCode("194J(b)")!.Id;
        var vendor = AddLedger(c, "Acme Consultants", "Sundry Creditors", false);
        vendor.DeducteeType = DeducteeType.Firm;
        vendor.PartyPan = "AAPFU0939F";

        // A Payment whose TDS carve-out cannot be computed: ₹10,00,000 assessable but only ₹100 payable to the vendor,
        // so the 10% withholding exceeds the obligation and the engine refuses.
        var e = OpenPlain(vm, VoucherBaseType.Payment, fees, vendor, 100m);
        e.Lines[0].AmountText = "1000000";
        e.Recalculate();

        // …while this same voucher is also refunding the outstanding advance (Rule 51).
        e.SelectedOutstandingAdvance = e.OutstandingAdvances.Single(o => o.Receipt?.Id == advance.Id);
        Assert.Equal(VoucherEntryViewModel.AdvanceAction.Refund, e.AdvanceActionForType);

        Assert.False(e.Accept());
        Assert.Contains("Cannot compute TDS", e.Message);

        // The advance must be exactly as it was — still outstanding, still refundable by a later, valid voucher.
        var after = c.AdvanceReceipts.Single();
        Assert.Null(after.RefundVoucherId);
        Assert.Null(after.AdjustedAgainstInvoiceVoucherId);
        Assert.Equal(postedSoFar, c.Vouchers.Count);   // nothing posted

        // Proof it is genuinely still live: a fresh screen still offers it, and refunding it works.
        var fresh = OpenPlain(vm, VoucherBaseType.Payment, advLedger, bank, 11800m);
        Assert.Contains(fresh.OutstandingAdvances, o => o.Receipt?.Id == advance.Id);
        fresh.SelectedOutstandingAdvance = fresh.OutstandingAdvances.Single(o => o.Receipt?.Id == advance.Id);
        Assert.True(fresh.Accept());
        Assert.Equal(LastVoucher(c).Id, c.AdvanceReceipts.Single().RefundVoucherId);
    }
}
