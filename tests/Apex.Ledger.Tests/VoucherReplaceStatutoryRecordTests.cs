using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// Design §3.3 — the records stored <b>BESIDE</b> the voucher, and the CARRY + WARN the design assigned them.
///
/// <para><b>The finding these tests close.</b> The slice's headline claim was that the §3.3 families all
/// survive a Replace because the <see cref="Voucher.Id"/> is preserved. That is TRUE — and it is the wrong
/// claim: they survive and then <b>lie</b>, because each freezes a fact about a voucher that just moved, and
/// §3.3's right-hand column says CARRY <b>+ WARN</b>, not CARRY. Measured before these tests existed, every one
/// of the cases below raised ZERO warnings:</para>
/// <list type="bullet">
/// <item>A <b>Generated</b> e-Way Bill kept its portal-issued EWB number against a consignment value ten times
/// the amended invoice — and the EWB-01 request the app files became internally contradictory, its header
/// stating the frozen ₹70,800 over an item list summing to ₹7,080 (the header is the frozen field; the items are
/// re-read live off the amended lines). The movement also dropped below the Rule-138 threshold, so a Generated
/// EWB stood against a movement that needs none. §3.3 calls this <i>"the highest silent-divergence risk in the
/// phase"</i>.</item>
/// <item>A GSTR-1 <b>Table 11A</b> line went on declaring a ₹10,000 advance with ₹1,800 tax against a book now
/// recording ₹1,180 — the ledger moved by ₹10,620 and the return line did not move at all.</item>
/// <item>An IRN-tagged invoice divided by ten left <c>EInvoiceReconciliation</c> reporting
/// <c>Mismatched = 0</c> — a clean bill of health — because its only content check compares the document
/// NUMBER.</item>
/// <item><c>GstCreditDebitNoteLink.OriginalInvoiceDate</c> — the frozen basis for the §34(2) 30-Nov cut-off —
/// and <c>RcmDocument.DocDate</c> both kept pointing at the pre-amendment date.</item>
/// </list>
///
/// <para><b>These are warnings, not refusals, deliberately.</b> §6.6 puts the <c>EInvoiceStatus.Generated</c>
/// REFUSAL in S5b and states warn-and-proceed for an active e-Way bill; S5a does not invent a refusal the design
/// assigned to a later slice.</para>
/// </summary>
public class VoucherReplaceStatutoryRecordTests
{
    private const string HomeGstin = "27AAPFU0939F1ZV";
    private static readonly DateOnly FyStart = new(2025, 4, 1);
    private static readonly DateOnly SaleDate = new(2025, 4, 10);

    private sealed class Gb
    {
        public required Company Company { get; init; }
        public required LedgerService Service { get; init; }
        public required EWayBillService EWay { get; init; }
        public required Guid SalesTypeId { get; init; }
        public required Guid ReceiptTypeId { get; init; }
        public required Domain.Ledger SalesLedger { get; init; }
        public required Domain.Ledger OutputCgst { get; init; }
        public required Domain.Ledger OutputSgst { get; init; }
        public required Domain.Ledger Party { get; init; }
        public required Domain.Ledger Cash { get; init; }
        public required Guid WidgetId { get; init; }
        public required Guid GodownId { get; init; }
    }

    private static Gb Build()
    {
        var c = CompanyFactory.CreateSeeded("Statutory Co", FyStart, FyStart);
        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = "27", Gstin = HomeGstin, RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart, Periodicity = GstReturnPeriodicity.Monthly,
            EWayBillEnabled = true, EWayApplicableFrom = FyStart, EWayIntraStateApplicable = true,
        });

        var inv = new InventoryService(c);
        var grp = inv.CreateStockGroup("Goods");
        var nos = inv.CreateSimpleUnit("Nos", "Numbers", unitQuantityCode: "NOS");
        var widget = inv.CreateStockItem("Widget", grp.Id, nos.Id);
        widget.Gst = new StockItemGstDetails
        {
            HsnSac = "847130", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800,
        };

        Domain.Ledger Add(string name, string groupName, bool debit)
        {
            var l = new Domain.Ledger(
                Guid.NewGuid(), name, c.FindGroupByName(groupName)!.Id, Money.Zero, openingIsDebit: debit);
            c.AddLedger(l);
            return l;
        }

        var party = Add("A Local Buyer", "Sundry Debtors", true);
        party.PartyGst = new PartyGstDetails
        {
            RegistrationType = GstRegistrationType.Regular, StateCode = "27", Gstin = "27AACCA1234A1Z5",
        };

        return new Gb
        {
            Company = c,
            Service = new LedgerService(c),
            EWay = new EWayBillService(c),
            SalesTypeId = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id,
            ReceiptTypeId = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Receipt).Id,
            SalesLedger = Add("Sales", "Sales Accounts", false),
            OutputCgst = Add("Output CGST", "Current Liabilities", false),
            OutputSgst = Add("Output SGST", "Current Liabilities", false),
            Party = party,
            Cash = c.FindLedgerByName("Cash")!,
            WidgetId = widget.Id,
            GodownId = c.MainLocation!.Id,
        };
    }

    /// <summary>An intra-state item invoice for <paramref name="taxable"/> @18% (CGST 9 + SGST 9).</summary>
    private static Voucher Sale(Gb g, Guid id, decimal taxable, DateOnly? date = null)
    {
        var half = Math.Round(taxable * 0.09m, 2, MidpointRounding.AwayFromZero);
        return new Voucher(
            id, g.SalesTypeId, date ?? SaleDate,
            new[]
            {
                new EntryLine(g.Party.Id, new Money(taxable + (half * 2m)), DrCr.Debit),
                new EntryLine(g.SalesLedger.Id, new Money(taxable), DrCr.Credit),
                new EntryLine(
                    g.OutputCgst.Id, new Money(half), DrCr.Credit,
                    gst: new GstLineTax(GstTaxHead.Central, 900, new Money(taxable))),
                new EntryLine(
                    g.OutputSgst.Id, new Money(half), DrCr.Credit,
                    gst: new GstLineTax(GstTaxHead.State, 900, new Money(taxable))),
            },
            partyId: g.Party.Id,
            inventoryLines: new[]
            {
                new VoucherInventoryLine(g.WidgetId, g.GodownId, 1m, new Money(taxable)),
            });
    }

    // =================================================================================================
    // e-Way Bill — §3.3's CARRY + WARN row, and the one it calls the highest silent-divergence risk.
    // =================================================================================================

    [Fact]
    public void Amending_a_voucher_under_a_generated_eWay_bill_warns_that_the_consignment_value_diverged()
    {
        var g = Build();
        var id = Guid.NewGuid();
        g.Service.Post(Sale(g, id, 60000m));                       // consignment 70,800 ⇒ Required

        var record = g.EWay.PrepareRecord(g.Company.FindVoucher(id)!, SaleDate);
        g.EWay.SetPartB(record, "27AAPFU0939F1ZV", EWayTransportMode.Road, "MH12AB1234", 120);
        g.EWay.RecordPortalResponse(
            record, "271234567890", new DateTimeOffset(2025, 4, 10, 9, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2025, 4, 12, 9, 0, 0, TimeSpan.Zero));
        Assert.Equal(EWayStatus.Generated, record.Status);
        Assert.Equal(7080000L, record.ConsignmentValuePaisa);

        g.Service.Replace(id, Sale(g, id, 6000m), out var warnings);

        var warning = Assert.Single(warnings, w => w.Code == VoucherAlterationWarningCode.StatutoryRecordDiverged);
        Assert.Contains("e-Way Bill", warning.Message, StringComparison.Ordinal);
        Assert.Contains("70800.00", warning.Message, StringComparison.Ordinal);
        Assert.Contains("7080.00", warning.Message, StringComparison.Ordinal);
        Assert.Contains("271234567890", warning.Message, StringComparison.Ordinal);

        // The record itself is CARRIED, exactly as §3.3 says — the warning is the whole remedy S5a ships.
        Assert.Equal(7080000L, g.Company.FindEWayBillRecordForVoucher(id)!.ConsignmentValuePaisa);
    }

    [Fact]
    public void An_alteration_that_leaves_the_consignment_value_alone_raises_no_eWay_warning()
    {
        var g = Build();
        var id = Guid.NewGuid();
        g.Service.Post(Sale(g, id, 60000m));
        var record = g.EWay.PrepareRecord(g.Company.FindVoucher(id)!, SaleDate);
        g.EWay.SetPartB(record, "27AAPFU0939F1ZV", EWayTransportMode.Road, "MH12AB1234", 120);

        // Narration only — every figure identical.
        var sameFigures = Sale(g, id, 60000m);
        sameFigures.Narration = "corrected wording";
        g.Service.Replace(id, sameFigures, out var warnings);

        Assert.DoesNotContain(warnings, w => w.Code == VoucherAlterationWarningCode.StatutoryRecordDiverged);
    }

    // =================================================================================================
    // GSTR-1 Table 11A — the frozen advance the return declares.
    // =================================================================================================

    [Fact]
    public void Amending_a_receipt_under_a_GST_advance_warns_that_Table_11A_will_not_follow()
    {
        var g = Build();
        var id = Guid.NewGuid();

        Voucher Receipt(Money amount) => new(
            id, g.ReceiptTypeId, SaleDate,
            new[]
            {
                new EntryLine(g.Cash.Id, amount, DrCr.Debit),
                new EntryLine(g.Party.Id, amount, DrCr.Credit),
            },
            partyId: g.Party.Id);

        g.Service.Post(Receipt(Money.FromRupees(11800m)));
        g.Company.AddAdvanceReceipt(new GstAdvanceReceipt(
            Guid.NewGuid(), id, isService: true, advanceAmount: Money.FromRupees(10000m), rateBasisPoints: 1800,
            interState: false, placeOfSupplyStateCode: "27", advanceTax: Money.FromRupees(1800m)));

        var before = Gstr1.Build(g.Company, FyStart, new DateOnly(2026, 3, 31));

        g.Service.Replace(id, Receipt(Money.FromRupees(1180m)), out var warnings);

        var warning = Assert.Single(warnings, w => w.Code == VoucherAlterationWarningCode.StatutoryRecordDiverged);
        Assert.Contains("advance receipt", warning.Message, StringComparison.Ordinal);
        Assert.Contains("Table 11A", warning.Message, StringComparison.Ordinal);

        // The measured harm the warning is about: the return line does NOT move with the ledger.
        var after = Gstr1.Build(g.Company, FyStart, new DateOnly(2026, 3, 31));
        Assert.Equal(Money.FromRupees(10000m), new Money(before.Table11A.Sum(r => r.AdvanceReceived.Amount)));
        Assert.Equal(Money.FromRupees(10000m), new Money(after.Table11A.Sum(r => r.AdvanceReceived.Amount)));
        Assert.Equal(before.AdvanceTaxReceived, after.AdvanceTaxReceived);
    }

    /// <summary>
    /// The SECOND half of the Table 11A finding, verified by construction rather than assumed:
    /// <c>Gstr1.BuildAdvanceTables</c> gates each advance on its RECEIPT VOUCHER'S DATE, so a Replace that moves
    /// the receipt across a return-period boundary silently drops a whole 11A row from the period it was filed
    /// in. The shipped <c>DateChanged</c> warning says the date moved and says nothing about the return line it
    /// moved; the statutory warning is what names it.
    /// </summary>
    [Fact]
    public void Moving_an_advance_receipt_across_a_period_boundary_drops_its_Table_11A_row()
    {
        var g = Build();
        var id = Guid.NewGuid();

        Voucher Receipt(DateOnly on) => new(
            id, g.ReceiptTypeId, on,
            new[]
            {
                new EntryLine(g.Cash.Id, Money.FromRupees(11800m), DrCr.Debit),
                new EntryLine(g.Party.Id, Money.FromRupees(11800m), DrCr.Credit),
            },
            partyId: g.Party.Id);

        g.Service.Post(Receipt(SaleDate));
        g.Company.AddAdvanceReceipt(new GstAdvanceReceipt(
            Guid.NewGuid(), id, isService: true, advanceAmount: Money.FromRupees(10000m), rateBasisPoints: 1800,
            interState: false, placeOfSupplyStateCode: "27", advanceTax: Money.FromRupees(1800m)));

        var aprilFrom = new DateOnly(2025, 4, 1);
        var aprilTo = new DateOnly(2025, 4, 30);
        Assert.Single(Gstr1.Build(g.Company, aprilFrom, aprilTo).Table11A);

        g.Service.Replace(id, Receipt(new DateOnly(2025, 5, 20)), out var warnings);

        Assert.Empty(Gstr1.Build(g.Company, aprilFrom, aprilTo).Table11A);
        var warning = Assert.Single(warnings, w => w.Code == VoucherAlterationWarningCode.StatutoryRecordDiverged);
        Assert.Contains("add or drop the row from a return period", warning.Message, StringComparison.Ordinal);
    }

    // =================================================================================================
    // e-invoice — the IRN the IRP signed over a document this alteration changed.
    // =================================================================================================

    [Fact]
    public void Amending_an_IRN_tagged_invoice_warns_even_though_the_reconciliation_reads_clean()
    {
        var g = Build();
        var id = Guid.NewGuid();
        g.Service.Post(Sale(g, id, 60000m));

        g.Company.AddEInvoiceRecord(EInvoiceRecord.Rehydrate(
            Guid.NewGuid(), id, EInvoiceService.DocumentNumberOf(g.Company, g.Company.FindVoucher(id)!),
            EInvoiceStatus.Generated, irn: new string('a', 64), ackNo: "112233445566",
            ackDate: SaleDate, signedQr: "qr", signedJson: null, cancelledOn: null, cancelReasonCode: null));

        g.Service.Replace(id, Sale(g, id, 6000m), out var warnings);

        var warning = Assert.Single(
            warnings,
            w => w.Code == VoucherAlterationWarningCode.StatutoryRecordDiverged
                 && w.Message.Contains("IRN", StringComparison.Ordinal));
        Assert.Contains("compares only the document NUMBER", warning.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The measured reason the warning has to exist: the app's ONLY detector for an amended e-invoiced document
    /// compares the document NUMBER, so after an amount-only amendment it still reports a clean bill of health.
    /// Pinned as a KNOWN LIMIT of the counter, so nobody points at it to argue this case is covered.
    /// </summary>
    [Fact]
    public void The_eInvoice_reconciliation_is_blind_to_an_amount_only_amendment_by_construction()
    {
        var g = Build();
        var id = Guid.NewGuid();
        g.Service.Post(Sale(g, id, 60000m));
        g.Company.AddEInvoiceRecord(EInvoiceRecord.Rehydrate(
            Guid.NewGuid(), id, EInvoiceService.DocumentNumberOf(g.Company, g.Company.FindVoucher(id)!),
            EInvoiceStatus.Generated, irn: new string('a', 64), ackNo: "112233445566",
            ackDate: SaleDate, signedQr: "qr", signedJson: null, cancelledOn: null, cancelReasonCode: null));

        var to = new DateOnly(2026, 3, 31);
        Assert.Equal(0, Gstr1.EInvoiceReconciliation(g.Company, FyStart, to).Mismatched);

        g.Service.Replace(id, Sale(g, id, 6000m), out _);

        // Still zero — the document number did not move, and that is all this counter looks at.
        Assert.Equal(0, Gstr1.EInvoiceReconciliation(g.Company, FyStart, to).Mismatched);
    }

    // =================================================================================================
    // §34(2) — the frozen original-invoice date, and the RCM document date.
    // =================================================================================================

    [Fact]
    public void Moving_an_invoice_that_a_section_34_note_points_at_warns_about_the_frozen_cut_off_date()
    {
        var g = Build();
        var invoiceId = Guid.NewGuid();
        g.Service.Post(Sale(g, invoiceId, 60000m));
        var invoice = g.Company.FindVoucher(invoiceId)!;

        g.Company.AddCreditDebitNoteLink(new GstCreditDebitNoteLink(
            Guid.NewGuid(), Guid.NewGuid(), CdnType.Credit, invoiceId,
            g.Company.FormatVoucherNumber(invoice), invoice.Date, reasonCode: "01"));

        g.Service.Replace(invoiceId, Sale(g, invoiceId, 60000m, SaleDate.AddMonths(1)), out var warnings);

        var warning = Assert.Single(
            warnings,
            w => w.Code == VoucherAlterationWarningCode.StatutoryRecordDiverged
                 && w.Message.Contains("30-Nov", StringComparison.Ordinal));
        Assert.Contains("credit/debit note", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Moving_a_voucher_that_an_RCM_document_was_raised_from_warns_about_the_document_date()
    {
        var g = Build();
        var id = Guid.NewGuid();
        g.Service.Post(Sale(g, id, 60000m));

        g.Company.AddRcmDocument(new RcmDocument(
            Guid.NewGuid(), RcmDocumentKind.SelfInvoice, id, seriesNumber: 1, docDate: SaleDate));

        g.Service.Replace(id, Sale(g, id, 60000m, SaleDate.AddDays(9)), out var warnings);

        Assert.Contains(
            warnings,
            w => w.Code == VoucherAlterationWarningCode.StatutoryRecordDiverged
                 && w.Message.Contains("RCM", StringComparison.Ordinal));
    }

    // =================================================================================================
    // 🔴 The paired SILENCE case for each family: the record IS attached, and what it froze did NOT move.
    //
    // The "no record attached" test at the bottom of this file cannot reach these guards at all — each one only
    // runs once its record is present — so without the silence cases below, their warnings could each be made to
    // fire on EVERY alteration with every test project still at exact baseline. A warning that always fires is the
    // failure this family's own doc names as fatal to it.
    // =================================================================================================

    /// <summary>Advance receipt attached, total and date both unmoved ⇒ silent.</summary>
    [Fact]
    public void An_advance_receipt_whose_voucher_did_not_move_raises_no_Table_11A_warning()
    {
        var g = Build();
        var id = Guid.NewGuid();

        Voucher Receipt(Money amount, string? narration = null) => new(
            id, g.ReceiptTypeId, SaleDate,
            new[]
            {
                new EntryLine(g.Cash.Id, amount, DrCr.Debit),
                new EntryLine(g.Party.Id, amount, DrCr.Credit),
            },
            narration: narration,
            partyId: g.Party.Id);

        g.Service.Post(Receipt(Money.FromRupees(11800m)));
        g.Company.AddAdvanceReceipt(new GstAdvanceReceipt(
            Guid.NewGuid(), id, isService: true, Money.FromRupees(10000m), 1800, interState: false, "27",
            Money.FromRupees(1800m)));

        g.Service.Replace(id, Receipt(Money.FromRupees(11800m), "corrected wording"), out var warnings);

        Assert.DoesNotContain(warnings, w => w.Code == VoucherAlterationWarningCode.StatutoryRecordDiverged);
    }

    /// <summary>A Generated IRN whose document did not move ⇒ silent.</summary>
    [Fact]
    public void An_IRN_tagged_invoice_that_did_not_move_raises_no_eInvoice_warning()
    {
        var g = Build();
        var id = Guid.NewGuid();
        g.Service.Post(Sale(g, id, 60000m));

        g.Company.AddEInvoiceRecord(EInvoiceRecord.Rehydrate(
            Guid.NewGuid(), id, EInvoiceService.DocumentNumberOf(g.Company, g.Company.FindVoucher(id)!),
            EInvoiceStatus.Generated, irn: new string('a', 64), ackNo: "112233445566",
            ackDate: SaleDate, signedQr: "qr", signedJson: null, cancelledOn: null, cancelReasonCode: null));

        var sameFigures = Sale(g, id, 60000m);
        sameFigures.Narration = "corrected wording";
        g.Service.Replace(id, sameFigures, out var warnings);

        Assert.DoesNotContain(warnings, w => w.Code == VoucherAlterationWarningCode.StatutoryRecordDiverged);
    }

    /// <summary>A §34 link whose frozen number AND date both still match ⇒ silent. Both stale arms are held false
    /// here (the number is preserved by Replace, and the date is unchanged), which is what makes this the paired
    /// case for that guard rather than for one half of it.</summary>
    [Fact]
    public void A_section_34_link_whose_invoice_did_not_move_raises_no_cut_off_warning()
    {
        var g = Build();
        var invoiceId = Guid.NewGuid();
        g.Service.Post(Sale(g, invoiceId, 60000m));
        var invoice = g.Company.FindVoucher(invoiceId)!;

        g.Company.AddCreditDebitNoteLink(new GstCreditDebitNoteLink(
            Guid.NewGuid(), Guid.NewGuid(), CdnType.Credit, invoiceId,
            g.Company.FormatVoucherNumber(invoice), invoice.Date, reasonCode: "01"));

        var sameDate = Sale(g, invoiceId, 6000m);   // the AMOUNT may move; the cut-off basis is number + date
        g.Service.Replace(invoiceId, sameDate, out var warnings);

        Assert.DoesNotContain(
            warnings,
            w => w.Code == VoucherAlterationWarningCode.StatutoryRecordDiverged
                 && w.Message.Contains("30-Nov", StringComparison.Ordinal));
    }

    /// <summary>An RCM document whose source voucher kept its date ⇒ silent.</summary>
    [Fact]
    public void An_RCM_document_whose_source_voucher_kept_its_date_raises_no_warning()
    {
        var g = Build();
        var id = Guid.NewGuid();
        g.Service.Post(Sale(g, id, 60000m));

        g.Company.AddRcmDocument(new RcmDocument(
            Guid.NewGuid(), RcmDocumentKind.SelfInvoice, id, seriesNumber: 1, docDate: SaleDate));

        g.Service.Replace(id, Sale(g, id, 6000m), out var warnings);   // amount moves, date does not

        Assert.DoesNotContain(
            warnings,
            w => w.Code == VoucherAlterationWarningCode.StatutoryRecordDiverged
                 && w.Message.Contains("RCM", StringComparison.Ordinal));
    }

    /// <summary>A book with NO statutory records attached raises no statutory warnings at all — the whole family
    /// must stay silent for the ordinary case, or the warnings become noise nobody reads.</summary>
    [Fact]
    public void A_voucher_with_no_attached_records_raises_no_statutory_warning()
    {
        var g = Build();
        var id = Guid.NewGuid();
        g.Service.Post(Sale(g, id, 60000m));

        g.Service.Replace(id, Sale(g, id, 6000m, SaleDate.AddDays(5)), out var warnings);

        Assert.DoesNotContain(warnings, w => w.Code == VoucherAlterationWarningCode.StatutoryRecordDiverged);
    }
}
