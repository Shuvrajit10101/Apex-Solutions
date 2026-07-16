using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// Phase 9 slice 2b — §34 Credit/Debit Notes (RQ-24; ER-12; DP-27). Proves: the directional output-tax adjustment (a
/// credit note posts Output tax on the reducing side and nets GSTR-1/3B DOWN; a debit note on the increasing side and
/// nets UP), the original-invoice link + the ER-12 "never free-floating" rejection, the §34(2) 30-Nov declaration guard
/// (credit notes capped, debit notes uncapped), the GSTR-1 Table 9B mapping (signed, read never recomputed), a
/// consolidated CDN against a party reference, and paisa-exact total-then-split reuse of <c>ComputeInvoiceTax</c>. All
/// pure, deterministic, paisa-exact. ER-13: a company that never issues a §34 note projects byte-identically to Phase-4.
/// </summary>
public sealed class CreditDebitNoteTests
{
    private const string GstinMaharashtra = "27AAPFU0939F1ZV";
    private const string GstinGujarat = "24AAACC1206D1ZM";
    private static readonly DateOnly FyStart = new(2025, 4, 1);
    private static readonly DateOnly SaleDate = new(2025, 4, 10);
    private static readonly DateOnly ToEnd = new(2026, 3, 31);

    private static Company NewGstCompany()
    {
        var c = CompanyFactory.CreateSeeded("CDN Traders", FyStart);
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

    private static Domain.Ledger SalesLedger(Company c) => AddLedger(c, "Sales", "Sales Accounts", false);

    private static Domain.Ledger Debtor(Company c, string state)
    {
        var l = AddLedger(c, "Buyer", "Sundry Debtors", true);
        l.PartyGst = new PartyGstDetails { RegistrationType = GstRegistrationType.Regular, Gstin = GstinGujarat, StateCode = state };
        return l;
    }

    /// <summary>Posts an original outward sale (Dr Debtor / Cr Sales / Cr Output tax) and returns its voucher.</summary>
    private static Voucher PostSale(Company c, Domain.Ledger sales, Domain.Ledger debtor, Money value, int rateBp, bool interState, DateOnly date)
    {
        var gst = new GstService(c);
        var tax = gst.ComputeInvoiceTax(new[] { new GstService.TaxableLine(value, rateBp) }, interState, GstTaxDirection.Output);
        var total = new Money(value.Amount + tax.TotalTax.Amount);
        var lines = new List<EntryLine> { new(debtor.Id, total, DrCr.Debit), new(sales.Id, value, DrCr.Credit) };
        lines.AddRange(tax.TaxLines);
        var type = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id;
        return new LedgerService(c).Post(new Voucher(Guid.NewGuid(), type, date, lines, partyId: debtor.Id));
    }

    /// <summary>Builds + posts a §34 note (credit ⇒ Dr Sales / Dr Output tax / Cr Party; debit ⇒ Dr Party / Cr Sales / Cr Output tax).</summary>
    private static Voucher PostCdn(
        Company c, CdnType type, Domain.Ledger sales, Domain.Ledger debtor, Money value, int rateBp, bool interState,
        DateOnly cdnDate, Guid? origVoucherId, string? origNumber, DateOnly? origDate, bool overrideTimeLimit = false)
    {
        var svc = new CreditDebitNoteService(c);
        var cdnVoucherId = Guid.NewGuid();
        var posting = svc.BuildCreditDebitNote(
            type, new[] { new GstService.TaxableLine(value, rateBp) }, interState, cdnVoucherId,
            origVoucherId, origNumber, origDate, cdnDate, reasonCode: type == CdnType.Credit ? "01 sales return" : "04 upward revision",
            overrideTimeLimit: overrideTimeLimit);

        var tax = new Money(posting.Computed.TotalTax.Amount);
        var total = new Money(value.Amount + tax.Amount);
        var baseType = type == CdnType.Credit ? VoucherBaseType.CreditNote : VoucherBaseType.DebitNote;
        List<EntryLine> lines = type == CdnType.Credit
            ? new() { new(sales.Id, value, DrCr.Debit), new(debtor.Id, total, DrCr.Credit) }   // credit note reduces
            : new() { new(debtor.Id, total, DrCr.Debit), new(sales.Id, value, DrCr.Credit) };  // debit note increases
        lines.AddRange(posting.TaxLines);
        var typeId = c.VoucherTypes.First(t => t.BaseType == baseType).Id;
        return new LedgerService(c).Post(new Voucher(cdnVoucherId, typeId, cdnDate, lines, partyId: debtor.Id));
    }

    // ---------------------------------------------------------------- 9. Credit note reduces output; debit note increases

    [Fact]
    public void Credit_note_reduces_output_tax_and_debit_note_increases_it()
    {
        var c = NewGstCompany();
        var sales = SalesLedger(c);
        var debtor = Debtor(c, "24"); // inter-state ⇒ IGST
        var gst = new GstService(c);
        var outputIgst = gst.FindTaxLedger(GstTaxHead.Integrated, GstTaxDirection.Output)!;

        var sale = PostSale(c, sales, debtor, Money.FromRupees(10000m), 1800, interState: true, SaleDate);
        Assert.Equal(1800m, LedgerBalances.SignedClosing(c, outputIgst, SaleDate) * -1); // Cr 1,800 liability

        // Credit note (full return) → Output IGST posted on the DEBIT (reducing) side → net liability 0.
        var cn = PostCdn(c, CdnType.Credit, sales, debtor, Money.FromRupees(10000m), 1800, true, new DateOnly(2025, 4, 20),
            origVoucherId: sale.Id, origNumber: null, origDate: SaleDate);
        Assert.True(VoucherValidator.IsBalanced(cn));
        Assert.Equal(0m, LedgerBalances.SignedClosing(c, outputIgst, new DateOnly(2025, 4, 30)));

        // GSTR-1 / GSTR-3B net DOWN by the credit note.
        var g1 = Gstr1.Build(c, FyStart, ToEnd);
        var g3 = Gstr3b.Build(c, FyStart, ToEnd);
        Assert.Equal(0m, g1.TotalIgst.Amount);
        Assert.Equal(0m, g3.OutwardIgst.Amount);

        // A debit note (upward revision ₹2,000) → Output IGST on the CREDIT (increasing) side → nets UP.
        var dn = PostCdn(c, CdnType.Debit, sales, debtor, Money.FromRupees(2000m), 1800, true, new DateOnly(2025, 5, 5),
            origVoucherId: sale.Id, origNumber: null, origDate: SaleDate);
        Assert.True(VoucherValidator.IsBalanced(dn));

        var g1b = Gstr1.Build(c, FyStart, ToEnd);
        var g3b = Gstr3b.Build(c, FyStart, ToEnd);
        Assert.Equal(360m, g1b.TotalIgst.Amount);   // 1800 (sale) − 1800 (credit) + 360 (debit)
        Assert.Equal(360m, g3b.OutwardIgst.Amount);
    }

    // ---------------------------------------------------------------- ER-12: a CDN must reference the original

    [Fact]
    public void A_note_with_no_original_invoice_reference_is_rejected()
    {
        var c = NewGstCompany();
        var svc = new CreditDebitNoteService(c);
        // Neither an original-invoice voucher nor a consolidated-party number ⇒ ER-12 rejection (a note is never free-floating).
        Assert.Throws<ArgumentException>(() =>
            svc.BuildCreditDebitNote(CdnType.Credit, new[] { new GstService.TaxableLine(Money.FromRupees(1000m), 1800) },
                interState: true, Guid.NewGuid(), originalInvoiceVoucherId: null, originalInvoiceNumber: null,
                originalInvoiceDate: SaleDate, cdnDate: new DateOnly(2025, 4, 20), reasonCode: "01 sales return"));
        // No link was registered on the failed build.
        Assert.Empty(c.CreditDebitNoteLinks);
    }

    // ---------------------------------------------------------------- Consolidated CDN (party reference, no voucher link)

    [Fact]
    public void A_consolidated_note_against_a_party_reference_is_allowed()
    {
        var c = NewGstCompany();
        var sales = SalesLedger(c);
        var debtor = Debtor(c, "24");
        // No original-invoice voucher link, but a consolidated-party original-invoice number ⇒ allowed (ER-12 satisfied).
        var cn = PostCdn(c, CdnType.Credit, sales, debtor, Money.FromRupees(3000m), 1800, true, new DateOnly(2025, 5, 1),
            origVoucherId: null, origNumber: "CONSOLIDATED-APR-2025", origDate: SaleDate);
        Assert.True(VoucherValidator.IsBalanced(cn));
        var link = Assert.Single(c.CreditDebitNoteLinks);
        Assert.Null(link.OriginalInvoiceVoucherId);
        Assert.Equal("CONSOLIDATED-APR-2025", link.OriginalInvoiceNumber);
    }

    // ---------------------------------------------------------------- 10. §34(2) 30-Nov guard

    [Fact]
    public void Section_34_2_blocks_a_late_credit_note_but_never_a_debit_note()
    {
        var c = NewGstCompany();
        var svc = new CreditDebitNoteService(c);
        var origDate = new DateOnly(2024, 6, 1); // FY 2024-25 ⇒ cut-off 30-Nov-2025.
        Assert.Equal(new DateOnly(2025, 11, 30), CreditDebitNoteService.NovemberThirtyFollowing(origDate));

        GstService.TaxableLine[] Line() => new[] { new GstService.TaxableLine(Money.FromRupees(1000m), 1800) };

        // A liability-reducing credit note AFTER the cut-off is blocked.
        var ex = Assert.Throws<InvalidOperationException>(() =>
            svc.BuildCreditDebitNote(CdnType.Credit, Line(), true, Guid.NewGuid(), null, "INV-1", origDate,
                cdnDate: new DateOnly(2025, 12, 1), reasonCode: "01 sales return"));
        Assert.Contains("§34(2)", ex.Message);
        Assert.Empty(c.CreditDebitNoteLinks); // nothing registered on the blocked build

        // On the cut-off date it posts.
        var onDeadline = svc.BuildCreditDebitNote(CdnType.Credit, Line(), true, Guid.NewGuid(), null, "INV-1", origDate,
            cdnDate: new DateOnly(2025, 11, 30), reasonCode: "01 sales return");
        Assert.Equal(CdnType.Credit, onDeadline.Link.CdnType);

        // A DEBIT note after the same cut-off is accepted (no §34 issuance cap on debit notes).
        var lateDebit = svc.BuildCreditDebitNote(CdnType.Debit, Line(), true, Guid.NewGuid(), null, "INV-1", origDate,
            cdnDate: new DateOnly(2026, 2, 1), reasonCode: "04 upward revision");
        Assert.Equal(CdnType.Debit, lateDebit.Link.CdnType);

        // An explicit override forces a late credit note through.
        var overridden = svc.BuildCreditDebitNote(CdnType.Credit, Line(), true, Guid.NewGuid(), null, "INV-1", origDate,
            cdnDate: new DateOnly(2025, 12, 1), reasonCode: "01 sales return", overrideTimeLimit: true);
        Assert.Equal(CdnType.Credit, overridden.Link.CdnType);
    }

    // ---------------------------------------------------------------- 11. Table 9B mapping (signed)

    [Fact]
    public void Table_9b_maps_the_note_with_the_original_reference_signed()
    {
        var c = NewGstCompany();
        var sales = SalesLedger(c);
        var debtor = Debtor(c, "24");
        var sale = PostSale(c, sales, debtor, Money.FromRupees(10000m), 1800, true, SaleDate);
        PostCdn(c, CdnType.Credit, sales, debtor, Money.FromRupees(4000m), 1800, true, new DateOnly(2025, 4, 25),
            origVoucherId: sale.Id, origNumber: "INV-9", origDate: SaleDate);

        var g1 = Gstr1.Build(c, FyStart, ToEnd);
        var row = Assert.Single(g1.Table9B);
        Assert.Equal(CdnType.Credit, row.NoteType);
        Assert.Equal(sale.Id, row.OriginalInvoiceVoucherId);
        Assert.Equal(SaleDate, row.OriginalInvoiceDate);
        Assert.Equal("01 sales return", row.ReasonCode);
        Assert.Equal("24", row.PlaceOfSupplyStateCode);
        // Signed negative for a credit note: ₹4,000 taxable @18% ⇒ −₹720 IGST (read off the posted line, never recomputed).
        Assert.Equal(-4000m, row.TaxableValue.Amount);
        Assert.Equal(-720m, row.Igst.Amount);
        Assert.Equal(-720m, row.TotalTax.Amount);
    }

    // ---------------------------------------------------------------- 12. Paisa-exact total-then-split (intra)

    [Fact]
    public void Credit_note_intrastate_split_is_paisa_exact()
    {
        var c = NewGstCompany();
        var sales = SalesLedger(c);
        var debtor = Debtor(c, "27"); // same state ⇒ intra ⇒ CGST + SGST
        var svc = new CreditDebitNoteService(c);
        var value = Money.FromRupees(9999m);
        var posting = svc.BuildCreditDebitNote(CdnType.Credit, new[] { new GstService.TaxableLine(value, 1800) },
            interState: false, Guid.NewGuid(), null, "INV-2", SaleDate, new DateOnly(2025, 4, 20), "01 sales return");

        // total-then-split: CGST + SGST == IGST-equivalent == round(9999 × 18%) = 1,799.82.
        var expectedTotal = GstService.TaxAmount(value, 1800);
        Assert.Equal(1799.82m, expectedTotal.Amount);
        Assert.Equal(expectedTotal.Amount, posting.Computed.TotalCgst.Amount + posting.Computed.TotalSgst.Amount);
        Assert.Equal(posting.Computed.TotalCgst.Amount, posting.Computed.TotalSgst.Amount); // even total ⇒ equal halves
        // The tax legs are on the DEBIT (reducing) side for a credit note.
        Assert.All(posting.TaxLines, l => Assert.Equal(DrCr.Debit, l.Side));
    }

    // ---------------------------------------------------------------- §34(2): a null original date cannot be waved through (finding #5)

    [Fact]
    public void A_credit_note_without_an_original_invoice_date_is_rejected_unless_overridden()
    {
        var c = NewGstCompany();
        var svc = new CreditDebitNoteService(c);
        GstService.TaxableLine[] Line() => new[] { new GstService.TaxableLine(Money.FromRupees(1000m), 1800) };

        // §34(2) cannot be verified without the original supply date, so a liability-reducing credit note carrying a
        // NULL original-invoice date is rejected — never silently waved through (finding #5). ER-12 is satisfied by the
        // consolidated number, so the ONLY reason to fail is the missing date.
        Assert.Throws<ArgumentException>(() =>
            svc.BuildCreditDebitNote(CdnType.Credit, Line(), interState: true, Guid.NewGuid(), null, "INV-NODATE",
                originalInvoiceDate: null, cdnDate: new DateOnly(2026, 2, 1), reasonCode: "01 sales return"));
        Assert.Empty(c.CreditDebitNoteLinks); // nothing registered on the rejected build

        // An explicit override intentionally bypasses §34(2), so a null-date credit note is then permitted.
        var overridden = svc.BuildCreditDebitNote(CdnType.Credit, Line(), interState: true, Guid.NewGuid(), null,
            "INV-NODATE", originalInvoiceDate: null, cdnDate: new DateOnly(2026, 2, 1), reasonCode: "01 sales return",
            overrideTimeLimit: true);
        Assert.Equal(CdnType.Credit, overridden.Link.CdnType);

        // A DEBIT note is uncapped by §34(2) and never needs the original-supply date.
        var debit = svc.BuildCreditDebitNote(CdnType.Debit, Line(), interState: true, Guid.NewGuid(), null, "INV-NODATE",
            originalInvoiceDate: null, cdnDate: new DateOnly(2026, 2, 1), reasonCode: "04 upward revision");
        Assert.Equal(CdnType.Debit, debit.Link.CdnType);
    }

    // ---------------------------------------------------------------- exempt-outward reconciliation for a CDN-linked voucher (finding #6)

    [Fact]
    public void A_cdn_linked_exempt_outward_voucher_reconciles_between_gstr1_and_gstr3b()
    {
        var c = NewGstCompany();
        var inv = new InventoryService(c);
        var grp = inv.CreateStockGroup("Goods");
        var nos = inv.CreateSimpleUnit("Nos", "Numbers", unitQuantityCode: "NOS");
        var main = c.MainLocation!.Id;
        var book = inv.CreateStockItem("Book", grp.Id, nos.Id);
        book.Gst = new StockItemGstDetails { HsnSac = "490199", Taxability = GstTaxability.Exempt };
        inv.AddOpeningBalance(book.Id, main, 50m, Money.FromRupees(150m));

        var sales = SalesLedger(c);
        var debtor = Debtor(c, "27");
        var ledgers = new LedgerService(c);
        var salesType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id;

        // A plain EXEMPT sale (₹1,000, no tax) — belongs in the exempt bucket of BOTH returns.
        var plainLines = new List<EntryLine> { new(debtor.Id, Money.FromRupees(1000m), DrCr.Debit), new(sales.Id, Money.FromRupees(1000m), DrCr.Credit) };
        ledgers.Post(new Voucher(Guid.NewGuid(), salesType, SaleDate, plainLines, partyId: debtor.Id,
            inventoryLines: new[] { new VoucherInventoryLine(book.Id, main, 5m, Money.FromRupees(200m)) }));

        // A §34-linked EXEMPT outward voucher (₹300, no tax, carrying inventory value). A real §34 note posts on a
        // Credit-Note voucher (which cannot carry item lines), so an exempt CDN with inventory value is a latent case —
        // this exercises the report-projection invariant directly by CDN-tagging an exempt outward voucher. GSTR-1's main
        // sweep skips CDN-linked vouchers; GSTR-3B's exempt bucket must skip them identically (finding #6), else divergence.
        var linkedId = Guid.NewGuid();
        var linkedLines = new List<EntryLine> { new(debtor.Id, Money.FromRupees(300m), DrCr.Debit), new(sales.Id, Money.FromRupees(300m), DrCr.Credit) };
        ledgers.Post(new Voucher(linkedId, salesType, new DateOnly(2025, 4, 20), linkedLines, partyId: debtor.Id,
            inventoryLines: new[] { new VoucherInventoryLine(book.Id, main, 1m, Money.FromRupees(300m)) }));
        c.AddCreditDebitNoteLink(new GstCreditDebitNoteLink(Guid.NewGuid(), linkedId, CdnType.Credit, null, "INV-EX", SaleDate, "01 sales return"));

        var g1 = Gstr1.Build(c, FyStart, ToEnd);
        var g3 = Gstr3b.Build(c, FyStart, ToEnd);
        // Only the ₹1,000 plain sale is exempt outward — the CDN-linked voucher is excluded by BOTH returns, so they agree.
        Assert.Equal(1000m, g1.ExemptNilNonGstValue.Amount);
        Assert.Equal(g1.ExemptNilNonGstValue.Amount, g3.ExemptNilNonGstOutward.Amount);
    }

    // ---------------------------------------------------------------- ER-13: no §34 note ⇒ Phase-4 projection unchanged

    [Fact]
    public void A_company_with_no_note_projects_identically_to_phase4()
    {
        var c = NewGstCompany();
        var sales = SalesLedger(c);
        var debtor = Debtor(c, "24");
        PostSale(c, sales, debtor, Money.FromRupees(10000m), 1800, true, SaleDate);

        var g1 = Gstr1.Build(c, FyStart, ToEnd);
        var g3 = Gstr3b.Build(c, FyStart, ToEnd);
        Assert.Empty(g1.Table9B);
        Assert.Empty(g1.Table11A);
        Assert.Empty(g1.Table11B);
        Assert.Equal(1800m, g1.TotalIgst.Amount);   // unchanged by the empty CDN section
        Assert.Equal(1800m, g3.OutwardIgst.Amount);
    }
}
