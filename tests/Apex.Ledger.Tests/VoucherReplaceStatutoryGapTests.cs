using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// <b>Phase 10.11 S5c — the §3.3 records that survive a <c>Replace</c> and then LIE, with no warning at all.</b>
///
/// <para>S5a shipped <c>StatutoryRecordDiverged</c> for five families: the e-invoice, the e-Way bill, the GST
/// advance receipt, the §34 credit/debit-note link and the RCM document. The brief for this slice asked whether it
/// fires <b>for every record that can go stale this way, not just that one</b>. Audited row by row against §3.3's
/// twelve-record table, it did not: <b>five more families raised nothing</b> — the TDS challan link, the TCS
/// challan link, the GST challan (PMT-06), the DRC-03, the ITC reversal, the GSTR-2B reconciliation result and the
/// Rule-88A set-off. Each freezes a figure computed off a voucher, each survives the swap because the
/// <see cref="Voucher.Id"/> is preserved, and each then declares the pre-alteration figure with no detector
/// anywhere in the app.</para>
///
/// <para>🔴 <b>Why the engine and not only the screen.</b> <c>VoucherAlterationEligibility</c> refuses several of
/// these families at the entry screen, which is where an operator meets them. But <c>Replace</c> is the ENGINE
/// contract — the canonical importer and every future caller reach it directly — and §3.3 assigns the records
/// CARRY <b>+ WARN</b>, not "CARRY, and hope the only caller checks". A guard that lives in one caller is already
/// half missing.</para>
///
/// <para>Every test below asserts the SENTENCE, and the paired "no move ⇒ no warning" case, so the family cannot
/// become noise nobody reads.</para>
/// </summary>
public class VoucherReplaceStatutoryGapTests
{
    private static readonly DateOnly FyStart = new(2025, 4, 1);
    private static readonly DateOnly D1 = new(2025, 4, 10);

    private sealed record Book(Company Company, LedgerService Service, Guid TypeId, Domain.Ledger Dr, Domain.Ledger Cr);

    private static Book Build()
    {
        var c = CompanyFactory.CreateSeeded("Gap Co", FyStart, FyStart);

        Domain.Ledger Add(string name, string groupName, bool debit)
        {
            var l = new Domain.Ledger(
                Guid.NewGuid(), name, c.FindGroupByName(groupName)!.Id, Money.Zero, openingIsDebit: debit);
            c.AddLedger(l);
            return l;
        }

        return new Book(
            c,
            new LedgerService(c),
            c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Journal).Id,
            Add("An Expense", "Indirect Expenses", true),
            Add("A Liability", "Current Liabilities", false));
    }

    private static Voucher Entry(Book b, Guid id, decimal amount, DateOnly? date = null) =>
        new(id, b.TypeId, date ?? D1,
            new[]
            {
                new EntryLine(b.Dr.Id, new Money(amount), DrCr.Debit),
                new EntryLine(b.Cr.Id, new Money(amount), DrCr.Credit),
            });

    private static string SingleDivergence(IReadOnlyList<VoucherAlterationWarning> warnings, string mustMention)
    {
        var match = Assert.Single(
            warnings,
            w => w.Code == VoucherAlterationWarningCode.StatutoryRecordDiverged
                 && w.Message.Contains(mustMention, StringComparison.Ordinal));
        Assert.False(string.IsNullOrWhiteSpace(match.Message));
        return match.Message;
    }

    // ================================================================ TDS / TCS challans

    /// <summary>
    /// §3.3 records the asymmetry verbatim: <c>ChallanReconciliation.ChallanHasLiveVoucher</c> drops a challan
    /// whose booking voucher is cancelled or deleted, so <b>cancel and delete SELF-HEAL; amend does NOT</b>. The
    /// challan's frozen <c>Amount</c> simply stops matching and the reconciliation reports the wrong Remaining.
    /// </summary>
    [Fact]
    public void Amending_a_voucher_linked_to_a_TDS_challan_warns_that_the_deposit_figure_diverged()
    {
        var b = Build();
        var id = Guid.NewGuid();
        b.Service.Post(Entry(b, id, 12000.30m));

        var challan = new TdsChallan(
            Guid.NewGuid(), "0001234", "0510308", D1.AddDays(20), new Money(12000.30m), "194J(b)", "200");
        b.Company.AddTdsChallan(challan);
        b.Company.LinkChallanToVoucher(challan.Id, id);

        b.Service.Replace(id, Entry(b, id, 9000.30m), out var warnings);

        var message = SingleDivergence(warnings, "TDS challan");
        Assert.Contains("12000.30", message, StringComparison.Ordinal);
        Assert.Contains("0001234", message, StringComparison.Ordinal);
        Assert.Contains("194J(b)", message, StringComparison.Ordinal);
        Assert.Contains("cancelled or deleted", message, StringComparison.Ordinal);

        // CARRY, exactly as §3.3 says — the record is untouched and the warning is the whole remedy.
        Assert.Equal(new Money(12000.30m), b.Company.TdsChallans.Single().Amount);
    }

    [Fact]
    public void An_alteration_that_leaves_the_total_alone_raises_no_TDS_challan_warning()
    {
        var b = Build();
        var id = Guid.NewGuid();
        b.Service.Post(Entry(b, id, 12000.30m));

        var challan = new TdsChallan(
            Guid.NewGuid(), "0001234", "0510308", D1.AddDays(20), new Money(12000.30m), "194J(b)", "200");
        b.Company.AddTdsChallan(challan);
        b.Company.LinkChallanToVoucher(challan.Id, id);

        var narrationOnly = Entry(b, id, 12000.30m);
        narrationOnly.Narration = "corrected wording";
        b.Service.Replace(id, narrationOnly, out var warnings);

        Assert.DoesNotContain(warnings, w => w.Code == VoucherAlterationWarningCode.StatutoryRecordDiverged);
    }

    [Fact]
    public void Amending_a_voucher_linked_to_a_TCS_challan_warns_by_name()
    {
        var b = Build();
        var id = Guid.NewGuid();
        b.Service.Post(Entry(b, id, 7500.55m));

        var challan = new TcsChallan(
            Guid.NewGuid(), "0009876", "0510308", D1.AddDays(20), new Money(7500.55m), "206C(1H)", "200");
        b.Company.AddTcsChallan(challan);
        b.Company.LinkTcsChallanToVoucher(challan.Id, id);

        b.Service.Replace(id, Entry(b, id, 500.55m), out var warnings);

        var message = SingleDivergence(warnings, "TCS challan");
        Assert.Contains("7500.55", message, StringComparison.Ordinal);
        Assert.Contains("206C(1H)", message, StringComparison.Ordinal);
    }

    // ================================================================ GST challan / DRC-03

    [Fact]
    public void Amending_a_voucher_carrying_a_GST_challan_warns_that_the_portal_deposit_diverged()
    {
        var b = Build();
        var id = Guid.NewGuid();
        b.Service.Post(Entry(b, id, 25000.75m));

        b.Company.AddGstChallan(new GstChallan(
            Guid.NewGuid(), "25040700012345", cin: null, brn: null, D1, GstTaxHead.Integrated,
            GstMinorHead.Tax, new Money(25000.75m), id));

        b.Service.Replace(id, Entry(b, id, 2500.75m), out var warnings);

        var message = SingleDivergence(warnings, "GST challan");
        Assert.Contains("25000.75", message, StringComparison.Ordinal);
        Assert.Contains("25040700012345", message, StringComparison.Ordinal);
    }

    [Fact]
    public void Amending_a_voucher_carrying_a_DRC_03_warns_that_the_filed_figures_diverged()
    {
        var b = Build();
        var id = Guid.NewGuid();
        b.Service.Post(Entry(b, id, 11800.40m));

        b.Company.AddGstDrc03(new GstDrc03(
            Guid.NewGuid(), "DRC03/2025/1", "Voluntary", "2025-04",
            cgstPaisa: 500000, sgstPaisa: 500000, igstPaisa: 0, cessPaisa: 0, interestPaisa: 18040,
            drc03aDemandRef: null, voucherId: id, createdAt: DateTimeOffset.UnixEpoch));

        b.Service.Replace(id, Entry(b, id, 1180.40m), out var warnings);

        var message = SingleDivergence(warnings, "DRC-03");
        Assert.Contains("10000.00", message, StringComparison.Ordinal); // the frozen tax
        Assert.Contains("180.40", message, StringComparison.Ordinal);   // the frozen interest
        Assert.Contains("2025-04", message, StringComparison.Ordinal);
    }

    // ================================================================ ITC reversal / GSTR-2B / set-off

    [Fact]
    public void Amending_the_source_voucher_of_an_ITC_reversal_warns_that_Table_4B_diverged()
    {
        var b = Build();
        var id = Guid.NewGuid();
        b.Service.Post(Entry(b, id, 60000.60m));

        b.Company.AddItcReversal(new ItcReversal(
            Guid.NewGuid(), ItcReversalRule.Rule42, "2025-04",
            cgstPaisa: 90000, sgstPaisa: 90000, igstPaisa: 0, cessPaisa: 0,
            d1BasisPaisa: null, d2BasisPaisa: null,
            sourceVoucherId: id, sourceLineId: null, reversalVoucherId: Guid.NewGuid(),
            reclaimOfId: null, drc03Id: null, table4bBucket: Table4bBucket.Table4B1,
            createdAt: DateTimeOffset.UnixEpoch));

        b.Service.Replace(id, Entry(b, id, 6000.60m), out var warnings);

        var message = SingleDivergence(warnings, "ITC reversal");
        Assert.Contains("1800.00", message, StringComparison.Ordinal);
        Assert.Contains("Table 4(B)", message, StringComparison.Ordinal);
    }

    [Fact]
    public void Amending_a_GSTR2B_matched_voucher_warns_that_the_frozen_variance_diverged()
    {
        var b = Build();
        var id = Guid.NewGuid();
        b.Service.Post(Entry(b, id, 45000.45m));

        b.Company.AddGstr2bReconResult(new Gstr2bReconResult(
            Guid.NewGuid(), Guid.NewGuid(), ReconBucket.PartialMismatch, id,
            taxableVariancePaisa: 12345, taxVariancePaisa: 2222));

        b.Service.Replace(id, Entry(b, id, 4500.45m), out var warnings);

        var message = SingleDivergence(warnings, "GSTR-2B");
        Assert.Contains("123.45", message, StringComparison.Ordinal);
        Assert.Contains("22.22", message, StringComparison.Ordinal);
    }

    [Fact]
    public void Amending_the_set_off_journal_warns_that_the_period_must_be_re_run()
    {
        var b = Build();
        var id = Guid.NewGuid();
        b.Service.Post(Entry(b, id, 33000.33m));

        // Two heads on one voucher — the warning is per VOUCHER, not per head, or it becomes noise.
        b.Company.AddGstSetoffLine(new GstSetoffLine(
            Guid.NewGuid(), id, "2025-04", GstTaxHead.Integrated, GstTaxHead.Central, isCash: false, 1000000));
        b.Company.AddGstSetoffLine(new GstSetoffLine(
            Guid.NewGuid(), id, "2025-04", GstTaxHead.Integrated, GstTaxHead.State, isCash: false, 2300033));

        b.Service.Replace(id, Entry(b, id, 3300.33m), out var warnings);

        var message = SingleDivergence(warnings, "Rule-88A set-off");
        Assert.Contains("2025-04", message, StringComparison.Ordinal);
        Assert.Contains("re-run the period", message, StringComparison.OrdinalIgnoreCase);
    }

    // ================================================================ the ELEVEN carries, verified

    /// <summary>
    /// 🔴 <b>S5a's measured claim, re-verified rather than re-asserted: eleven of the twelve §3.3 records are
    /// carried BY CONSTRUCTION.</b> Each is a separate object on <see cref="Company"/> keyed by the voucher's
    /// <see cref="Voucher.Id"/>; <c>Replace</c> preserves the Guid and never touches those collections, so every one
    /// of them still resolves afterwards. The twelfth, <c>BankAllocation.BankDate</c>, is a LINE child rather than a
    /// separate object, needed code, and got it in S5a (<c>CarryBankDatesForward</c>).
    ///
    /// <para>This test attaches ALL eleven to one voucher, replaces it, and asserts each one still points at it.
    /// It is deliberately a survival test and not a correctness test — <b>surviving is exactly what lets them
    /// lie</b>, which is what the divergence warnings above are for.</para>
    /// </summary>
    [Fact]
    public void All_eleven_records_stored_beside_a_voucher_survive_a_Replace_by_construction()
    {
        var b = Build();
        var id = Guid.NewGuid();
        b.Service.Post(Entry(b, id, 50000.50m));

        var challanId = Guid.NewGuid();
        b.Company.AddTdsChallan(new TdsChallan(
            challanId, "0001234", "0510308", D1, new Money(50000.50m), "194J(b)", "200"));
        b.Company.LinkChallanToVoucher(challanId, id);

        var tcsChallanId = Guid.NewGuid();
        b.Company.AddTcsChallan(new TcsChallan(
            tcsChallanId, "0009876", "0510308", D1, new Money(50000.50m), "206C(1H)", "200"));
        b.Company.LinkTcsChallanToVoucher(tcsChallanId, id);

        b.Company.AddEInvoiceRecord(new EInvoiceRecord(Guid.NewGuid(), id, "JV/1"));
        b.Company.AddEWayBillRecord(new EWayBillRecord(
            Guid.NewGuid(), id, "JV/1", "Outward", "Supply", "INV", 5000050L, "27", "24"));
        b.Company.AddCreditDebitNoteLink(new GstCreditDebitNoteLink(
            Guid.NewGuid(), Guid.NewGuid(), CdnType.Credit, id, "JV/1", D1, "01"));
        b.Company.AddAdvanceReceipt(new GstAdvanceReceipt(
            Guid.NewGuid(), id, isService: true, new Money(50000.50m), 1800, interState: false, "27",
            new Money(9000.09m)));
        b.Company.AddRcmDocument(new RcmDocument(Guid.NewGuid(), RcmDocumentKind.SelfInvoice, id, 1, D1));
        b.Company.AddGstSetoffLine(new GstSetoffLine(
            Guid.NewGuid(), id, "2025-04", GstTaxHead.Integrated, GstTaxHead.Central, isCash: false, 100000));
        b.Company.AddItcReversal(new ItcReversal(
            Guid.NewGuid(), ItcReversalRule.Rule42, "2025-04", 100000, 100000, 0, 0, null, null, id, null,
            Guid.NewGuid(), null, null, Table4bBucket.Table4B1, DateTimeOffset.UnixEpoch));
        b.Company.AddGstr2bReconResult(new Gstr2bReconResult(
            Guid.NewGuid(), Guid.NewGuid(), ReconBucket.PartialMismatch, id, 111, 222));
        b.Company.AddGstChallan(new GstChallan(
            Guid.NewGuid(), "25040700012345", null, null, D1, GstTaxHead.Integrated, GstMinorHead.Tax,
            new Money(50000.50m), id));
        b.Company.AddGstDrc03(new GstDrc03(
            Guid.NewGuid(), "DRC03/1", "Voluntary", "2025-04", 1000, 1000, 0, 0, 0, null, id,
            DateTimeOffset.UnixEpoch));

        var replacement = Entry(b, id, 12345.67m, D1.AddDays(4));
        b.Service.Replace(id, replacement, out _);

        // The Guid is every outside link's only handle, and it is preserved (clause 2).
        Assert.Same(replacement, b.Company.FindVoucher(id));

        Assert.Contains(b.Company.ChallanVoucherLinks, l => l.VoucherId == id);
        Assert.Contains(b.Company.TcsChallanVoucherLinks, l => l.VoucherId == id);
        Assert.Contains(b.Company.EInvoiceRecords, r => r.SourceVoucherId == id);
        Assert.Contains(b.Company.EWayBillRecords, r => r.SourceVoucherId == id);
        Assert.Contains(b.Company.CreditDebitNoteLinks, l => l.OriginalInvoiceVoucherId == id);
        Assert.Contains(b.Company.AdvanceReceipts, a => a.ReceiptVoucherId == id);
        Assert.Contains(b.Company.RcmDocuments, d => d.SourceVoucherId == id);
        Assert.Contains(b.Company.GstSetoffLines, l => l.VoucherId == id);
        Assert.Contains(b.Company.ItcReversals, r => r.SourceVoucherId == id);
        Assert.Contains(b.Company.Gstr2bReconResults, r => r.MatchedVoucherId == id);
        Assert.Contains(b.Company.GstChallans, c => c.VoucherId == id);
        Assert.Contains(b.Company.GstDrc03s, d => d.VoucherId == id);
    }

    // ================================================================ the silence case

    /// <summary>
    /// The whole family stays silent for an ordinary alteration on a book carrying none of these records — if a
    /// warning fires when nothing is attached, the operator stops reading them and the five that matter are lost.
    /// </summary>
    [Fact]
    public void A_plain_alteration_on_a_book_with_no_attached_records_raises_no_new_warning()
    {
        var b = Build();
        var id = Guid.NewGuid();
        b.Service.Post(Entry(b, id, 1000.10m));

        b.Service.Replace(id, Entry(b, id, 2000.20m, D1.AddDays(3)), out var warnings);

        Assert.DoesNotContain(warnings, w => w.Code == VoucherAlterationWarningCode.StatutoryRecordDiverged);
    }
}
