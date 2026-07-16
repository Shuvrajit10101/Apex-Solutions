using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// Phase 9 slice 2b — GST on advances (RQ-25; §12/§13, Rule 50/51; DP-32). Proves: a Rule-50 service advance is taxed on
/// receipt (the self-balancing Cr Output {head} + Dr "Output Tax on Advances" suspense pair → GSTR-1 11A), a goods advance
/// is de-taxed (Notn 66/2017 — no tax pair, no 11A), the advance→invoice adjustment reverses the suspense (→ 11B) so the
/// invoice's own output tax is not double-counted, a Rule-51 refund returns the advance + tax, and the earliest-event
/// time-of-supply helper is shared verbatim with RCM. All pure, deterministic, paisa-exact. ER-13 when off.
/// </summary>
public sealed class AdvanceReceiptTests
{
    private const string GstinMaharashtra = "27AAPFU0939F1ZV";
    private static readonly DateOnly FyStart = new(2025, 4, 1);
    private static readonly DateOnly ReceiptDate = new(2025, 4, 10);
    private static readonly DateOnly InvoiceDate = new(2025, 5, 15);
    private static readonly DateOnly ToEnd = new(2026, 3, 31);

    private static Company NewGstCompany()
    {
        var c = CompanyFactory.CreateSeeded("Advance Traders", FyStart);
        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = "27", Gstin = GstinMaharashtra, RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart, Periodicity = GstReturnPeriodicity.Monthly,
        });
        return c;
    }

    private static Domain.Ledger AddLedger(Company c, string name, string groupName, bool openingIsDebit)
    {
        var l = new Domain.Ledger(Guid.NewGuid(), name, c.FindGroupByName(groupName)!.Id, Money.Zero, openingIsDebit);
        c.AddLedger(l);
        return l;
    }

    /// <summary>Posts a Rule-50 advance Receipt voucher (Dr Bank / Cr Advance + the tax pair) and returns (voucher, record).</summary>
    private static (Voucher Voucher, GstAdvanceReceipt Record) PostAdvanceReceipt(
        Company c, bool isService, Money net, int rateBp, bool interState, DateOnly date)
    {
        var svc = new AdvanceReceiptService(c);
        var bank = c.FindLedgerByName("Bank") ?? AddLedger(c, "Bank", "Bank Accounts", true);
        var advance = c.FindLedgerByName("Advance from customer") ?? AddLedger(c, "Advance from customer", "Current Liabilities", false);
        var receiptId = Guid.NewGuid();
        var posting = svc.BuildAdvanceReceipt(receiptId, isService, net, rateBp, interState);
        var gross = new Money(net.Amount + posting.AdvanceTax.Amount);
        var lines = new List<EntryLine> { new(bank.Id, gross, DrCr.Debit), new(advance.Id, gross, DrCr.Credit) };
        lines.AddRange(posting.TaxLines);
        var typeId = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Receipt).Id;
        var v = new LedgerService(c).Post(new Voucher(receiptId, typeId, date, lines));
        return (v, posting.Receipt);
    }

    // ---------------------------------------------------------------- 13. Service advance taxes on receipt → 11A

    [Fact]
    public void Service_advance_is_taxed_on_receipt_and_feeds_table_11a()
    {
        var c = NewGstCompany();
        var gst = new GstService(c);
        var (v, record) = PostAdvanceReceipt(c, isService: true, Money.FromRupees(10000m), 1800, interState: true, ReceiptDate);
        Assert.True(VoucherValidator.IsBalanced(v)); // Σ Dr 13,600 == Σ Cr 13,600

        // The tax pair: Cr Output IGST 1,800 (liability) + Dr suspense 1,800.
        var outputIgst = gst.FindTaxLedger(GstTaxHead.Integrated, GstTaxDirection.Output)!;
        var suspense = c.FindLedgerByName(GstService.AdvanceTaxSuspenseLedgerName)!;
        Assert.Equal(1800m, LedgerBalances.SignedClosing(c, outputIgst, ReceiptDate) * -1);
        Assert.Equal(1800m, LedgerBalances.SignedClosing(c, suspense, ReceiptDate));
        Assert.Equal(Money.FromRupees(1800m), record.AdvanceTax);

        // GSTR-1 Table 11A carries the advance tax; the Receipt voucher never bleeds into the invoice B2B/B2C rows.
        var g1 = Gstr1.Build(c, FyStart, ToEnd);
        var row = Assert.Single(g1.Table11A);
        Assert.Equal(1800, row.RateBasisPoints);
        Assert.True(row.InterState);
        Assert.Equal(10000m, row.AdvanceReceived.Amount);
        Assert.Equal(1800m, row.Igst.Amount);
        Assert.Equal(1800m, g1.AdvanceTaxReceived.Amount);
        Assert.Empty(g1.B2B); // the advance is NOT an invoice row
        Assert.Equal(0m, g1.TotalIgst.Amount); // no invoice yet ⇒ no outward invoice tax
    }

    // ---------------------------------------------------------------- 14. Goods advance de-taxed (Notn 66/2017)

    [Fact]
    public void Goods_advance_is_de_taxed_and_has_no_table_11a_row()
    {
        var c = NewGstCompany();
        var (v, record) = PostAdvanceReceipt(c, isService: false, Money.FromRupees(10000m), 1800, interState: true, ReceiptDate);
        Assert.True(VoucherValidator.IsBalanced(v));
        Assert.Equal(Money.Zero, record.AdvanceTax);
        Assert.False(record.IsService);
        // No suspense ledger was ever created (no taxable advance) — ER-13 ledger set unchanged for a de-taxed goods advance.
        Assert.Null(c.FindLedgerByName(GstService.AdvanceTaxSuspenseLedgerName));

        var g1 = Gstr1.Build(c, FyStart, ToEnd);
        Assert.Empty(g1.Table11A);
        Assert.Equal(0m, g1.AdvanceTaxReceived.Amount);
    }

    // ---------------------------------------------------------------- 15. Advance → invoice adjustment → 11B, no double-count

    [Fact]
    public void Advance_adjusted_against_invoice_reverses_the_suspense_and_feeds_table_11b()
    {
        var c = NewGstCompany();
        var gst = new GstService(c);
        var svc = new AdvanceReceiptService(c);
        var (_, record) = PostAdvanceReceipt(c, isService: true, Money.FromRupees(10000m), 1800, interState: true, ReceiptDate);

        // Raise the tax invoice for the full ₹10,000 @18% (its own Cr Output IGST 1,800).
        var sales = AddLedger(c, "Sales", "Sales Accounts", false);
        var debtor = AddLedger(c, "Buyer", "Sundry Debtors", true);
        var invTax = gst.ComputeInvoiceTax(new[] { new GstService.TaxableLine(Money.FromRupees(10000m), 1800) }, true, GstTaxDirection.Output);
        var invId = Guid.NewGuid();
        var invLines = new List<EntryLine> { new(debtor.Id, Money.FromRupees(11800m), DrCr.Debit), new(sales.Id, Money.FromRupees(10000m), DrCr.Credit) };
        invLines.AddRange(invTax.TaxLines);
        new LedgerService(c).Post(new Voucher(invId, c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id, InvoiceDate, invLines, partyId: debtor.Id));

        // Adjust: reverse the suspense (Dr Output IGST / Cr suspense) so the invoice's own output tax is not double-counted.
        var reversal = svc.AdjustAgainstInvoice(record, invId);
        new LedgerService(c).Post(new Voucher(Guid.NewGuid(), c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Journal).Id, InvoiceDate, reversal));

        var outputIgst = gst.FindTaxLedger(GstTaxHead.Integrated, GstTaxDirection.Output)!;
        var suspense = c.FindLedgerByName(GstService.AdvanceTaxSuspenseLedgerName)!;
        // Output IGST = receipt Cr 1,800 + invoice Cr 1,800 − adjustment Dr 1,800 = 1,800 (NOT double-counted).
        Assert.Equal(1800m, LedgerBalances.SignedClosing(c, outputIgst, ToEnd) * -1);
        Assert.Equal(0m, LedgerBalances.SignedClosing(c, suspense, ToEnd)); // suspense fully released

        var adjusted = c.AdvanceReceipts.Single();
        Assert.Equal(invId, adjusted.AdjustedAgainstInvoiceVoucherId);

        var g1 = Gstr1.Build(c, FyStart, ToEnd);
        Assert.Single(g1.Table11A);                       // received earlier in the FY
        var adj = Assert.Single(g1.Table11B);             // adjusted this FY
        Assert.Equal(10000m, adj.AdvanceAdjusted.Amount);
        Assert.Equal(1800m, adj.Igst.Amount);
        Assert.Equal(1800m, g1.AdvanceTaxAdjusted.Amount);
    }

    // ---------------------------------------------------------------- 16. Refund voucher (Rule 51)

    [Fact]
    public void Refund_returns_the_advance_and_tax_and_sets_the_refund_link()
    {
        var c = NewGstCompany();
        var gst = new GstService(c);
        var svc = new AdvanceReceiptService(c);
        var (_, record) = PostAdvanceReceipt(c, isService: true, Money.FromRupees(10000m), 1800, interState: true, ReceiptDate);

        var bank = c.FindLedgerByName("Bank")!;
        var advance = c.FindLedgerByName("Advance from customer")!;
        var refundId = Guid.NewGuid();
        // Rule-51 refund: Dr Advance / Cr Bank (return the gross ₹11,800) + the reversal pair (Dr Output IGST / Cr suspense).
        var refundLines = new List<EntryLine> { new(advance.Id, Money.FromRupees(11800m), DrCr.Debit), new(bank.Id, Money.FromRupees(11800m), DrCr.Credit) };
        refundLines.AddRange(svc.Refund(record, refundId));
        var v = new LedgerService(c).Post(new Voucher(refundId, c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Payment).Id, new DateOnly(2025, 4, 20), refundLines));
        Assert.True(VoucherValidator.IsBalanced(v));

        var outputIgst = gst.FindTaxLedger(GstTaxHead.Integrated, GstTaxDirection.Output)!;
        var suspense = c.FindLedgerByName(GstService.AdvanceTaxSuspenseLedgerName)!;
        Assert.Equal(0m, LedgerBalances.SignedClosing(c, outputIgst, ToEnd)); // liability reversed
        Assert.Equal(0m, LedgerBalances.SignedClosing(c, suspense, ToEnd));   // suspense released
        Assert.Equal(refundId, c.AdvanceReceipts.Single().RefundVoucherId);
    }

    // ---------------------------------------------------------------- 16b. Refund nets out of Table 11A (finding #1)

    [Fact]
    public void Refunded_advance_nets_out_of_table_11a_and_reconciles_with_the_zero_ledger_balance()
    {
        var c = NewGstCompany();
        var gst = new GstService(c);
        var svc = new AdvanceReceiptService(c);
        var (_, record) = PostAdvanceReceipt(c, isService: true, Money.FromRupees(10000m), 1800, interState: true, ReceiptDate);

        // Rule-51 refund the whole advance (no supply happened): Dr Advance / Cr Bank + the reversal pair.
        var bank = c.FindLedgerByName("Bank")!;
        var advance = c.FindLedgerByName("Advance from customer")!;
        var refundId = Guid.NewGuid();
        var refundLines = new List<EntryLine> { new(advance.Id, Money.FromRupees(11800m), DrCr.Debit), new(bank.Id, Money.FromRupees(11800m), DrCr.Credit) };
        refundLines.AddRange(svc.Refund(record, refundId));
        new LedgerService(c).Post(new Voucher(refundId, c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Payment).Id, new DateOnly(2025, 4, 20), refundLines));

        // The ledgers say 0 (both Output IGST and the suspense fully reversed).
        var outputIgst = gst.FindTaxLedger(GstTaxHead.Integrated, GstTaxDirection.Output)!;
        var suspense = c.FindLedgerByName(GstService.AdvanceTaxSuspenseLedgerName)!;
        Assert.Equal(0m, LedgerBalances.SignedClosing(c, outputIgst, ToEnd));
        Assert.Equal(0m, LedgerBalances.SignedClosing(c, suspense, ToEnd));

        // GSTR-1: the refund reverses the 11A in its period (11B), so advance tax received − adjusted nets to 0 — no
        // phantom advance liability lingering in 11A (finding #1).
        var g1 = Gstr1.Build(c, FyStart, ToEnd);
        Assert.Single(g1.Table11A);                        // received this FY
        var refundRow = Assert.Single(g1.Table11B);        // refunded this FY (reverses the 11A)
        Assert.Equal(10000m, refundRow.AdvanceAdjusted.Amount);
        Assert.Equal(1800m, refundRow.Igst.Amount);
        Assert.Equal(0m, g1.AdvanceTaxReceived.Amount - g1.AdvanceTaxAdjusted.Amount); // nets to 0, matches the books
        Assert.Equal(refundId, c.AdvanceReceipts.Single().RefundVoucherId);
    }

    // ---------------------------------------------------------------- 16c. Partial adjustment fail-fast (finding #4)

    [Fact]
    public void Adjust_against_a_partial_invoice_is_rejected_but_a_full_adjustment_still_works()
    {
        var c = NewGstCompany();
        var gst = new GstService(c);
        var svc = new AdvanceReceiptService(c);
        var (_, record) = PostAdvanceReceipt(c, isService: true, Money.FromRupees(10000m), 1800, interState: true, ReceiptDate);

        var sales = AddLedger(c, "Sales", "Sales Accounts", false);
        var debtor = AddLedger(c, "Buyer", "Sundry Debtors", true);
        var salesType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id;

        Voucher PostInvoice(Money value)
        {
            var tax = gst.ComputeInvoiceTax(new[] { new GstService.TaxableLine(value, 1800) }, true, GstTaxDirection.Output);
            var total = new Money(value.Amount + tax.TotalTax.Amount);
            var lines = new List<EntryLine> { new(debtor.Id, total, DrCr.Debit), new(sales.Id, value, DrCr.Credit) };
            lines.AddRange(tax.TaxLines);
            return new LedgerService(c).Post(new Voucher(Guid.NewGuid(), salesType, InvoiceDate, lines, partyId: debtor.Id));
        }

        // A PARTIAL invoice (₹6,000 taxable < the ₹10,000 advance) would leave a residual advance — reject it rather
        // than over-reverse the FULL advance tax (finding #4).
        var partial = PostInvoice(Money.FromRupees(6000m));
        var ex = Assert.Throws<InvalidOperationException>(() => svc.AdjustAgainstInvoice(record, partial.Id));
        Assert.Contains("Partial advance adjustment", ex.Message);
        Assert.Null(c.AdvanceReceipts.Single().AdjustedAgainstInvoiceVoucherId); // nothing recorded on the rejected adjust

        // A FULL invoice (₹10,000 == the advance) adjusts cleanly.
        var full = PostInvoice(Money.FromRupees(10000m));
        var reversal = svc.AdjustAgainstInvoice(record, full.Id);
        new LedgerService(c).Post(new Voucher(Guid.NewGuid(), c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Journal).Id, InvoiceDate, reversal));
        Assert.Equal(full.Id, c.AdvanceReceipts.Single().AdjustedAgainstInvoiceVoucherId);
    }

    // ---------------------------------------------------------------- 17. Shared time-of-supply helper

    [Fact]
    public void Time_of_supply_is_shared_verbatim_with_reverse_charge()
    {
        var inv = new DateOnly(2025, 4, 10);
        var rcpt = new DateOnly(2025, 4, 1);
        var pay = new DateOnly(2025, 4, 5);

        foreach (var kind in new[] { GstSupplyType.Goods, GstSupplyType.Services })
        {
            Assert.Equal(RcmService.TimeOfSupply(kind, inv, rcpt, pay), AdvanceReceiptService.TimeOfSupply(kind, inv, rcpt, pay));
            Assert.Equal(RcmService.TimeOfSupply(kind, inv, null, null), AdvanceReceiptService.TimeOfSupply(kind, inv, null, null));
            Assert.Equal(RcmService.TimeOfSupply(kind, inv, rcpt, null), AdvanceReceiptService.TimeOfSupply(kind, inv, rcpt, null));
        }
    }
}
