using System.Text;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;

namespace Apex.Ledger.Io.Tests;

/// <summary>
/// Phase-9 slice-2b <b>§34-CDN + GST-on-advances Io fold-in</b> gate (RQ-24/RQ-25; RQ-23; ER-13): a company carrying a
/// service-built §34 credit note (link + directional Output tax) and a Rule-50 service advance (suspense pair + record)
/// <b>exports and re-imports exact in JSON AND XML</b>, both byte-stable and into a fresh (differently-Guid'd) company
/// through the engine-routed <see cref="CompanyImportService"/> (all-or-nothing; the balance is enforced by the engine).
/// A batch carrying a late (post §34(2) 30-Nov) credit note rejects wholesale. A plain GST company (no CDN/advance)
/// serialises with empty CDN/advance sections — byte-identical to a v38 company (ER-13). De-brand clean (no "Tally").
/// </summary>
public sealed class CanonicalCdnAdvanceRoundTripTests
{
    private const string GstinMaharashtra = "27AAPFU0939F1ZV";
    private const string GstinGujarat = "24AAACC1206D1ZM";
    private static readonly DateOnly FyStart = new(2025, 4, 1);
    private static readonly DateOnly SaleDate = new(2025, 4, 10);
    private static readonly DateOnly ToEnd = new(2026, 3, 31);

    private static Company Gst(string name, DateOnly fyStart)
    {
        var c = CompanyFactory.CreateSeeded(name, fyStart);
        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = "27", Gstin = GstinMaharashtra, RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = fyStart, Periodicity = GstReturnPeriodicity.Monthly,
        });
        return c;
    }

    private static Domain.Ledger Add(Company c, string name, string group, bool dr)
    {
        var l = new Domain.Ledger(Guid.NewGuid(), name, c.FindGroupByName(group)!.Id, Money.Zero, dr);
        c.AddLedger(l);
        return l;
    }

    private static Company BuildCdnAdvanceCompany()
    {
        var c = Gst("CDN+Advance Co", FyStart);
        var gst = new GstService(c);
        var sales = Add(c, "Sales", "Sales Accounts", false);
        var debtor = Add(c, "Buyer", "Sundry Debtors", true);
        debtor.PartyGst = new PartyGstDetails { RegistrationType = GstRegistrationType.Regular, Gstin = GstinGujarat, StateCode = "24" };
        var ledgers = new LedgerService(c);

        // Original inter-state sale ₹10,000 @18%.
        var saleTax = gst.ComputeInvoiceTax(new[] { new GstService.TaxableLine(Money.FromRupees(10000m), 1800) }, true, GstTaxDirection.Output);
        var saleLines = new List<EntryLine> { new(debtor.Id, Money.FromRupees(11800m), DrCr.Debit), new(sales.Id, Money.FromRupees(10000m), DrCr.Credit) };
        saleLines.AddRange(saleTax.TaxLines);
        var sale = ledgers.Post(new Voucher(Guid.NewGuid(), c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id, SaleDate, saleLines, partyId: debtor.Id));

        // §34 credit note ₹4,000 against it.
        var cdn = new CreditDebitNoteService(c);
        var cnId = Guid.NewGuid();
        var cnPosting = cdn.BuildCreditDebitNote(CdnType.Credit, new[] { new GstService.TaxableLine(Money.FromRupees(4000m), 1800) },
            interState: true, cnId, sale.Id, "INV-1", SaleDate, new DateOnly(2025, 4, 25), "01 sales return");
        var cnLines = new List<EntryLine> { new(sales.Id, Money.FromRupees(4000m), DrCr.Debit), new(debtor.Id, Money.FromRupees(4720m), DrCr.Credit) };
        cnLines.AddRange(cnPosting.TaxLines);
        ledgers.Post(new Voucher(cnId, c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.CreditNote).Id, new DateOnly(2025, 4, 25), cnLines, partyId: debtor.Id));

        // Rule-50 service advance ₹5,000 @18%.
        var adv = new AdvanceReceiptService(c);
        var bank = Add(c, "Bank", "Bank Accounts", true);
        var advLed = Add(c, "Advance from customer", "Current Liabilities", false);
        var rcptId = Guid.NewGuid();
        var advPosting = adv.BuildAdvanceReceipt(rcptId, isService: true, Money.FromRupees(5000m), 1800, interState: true);
        var advLines = new List<EntryLine> { new(bank.Id, Money.FromRupees(5900m), DrCr.Debit), new(advLed.Id, Money.FromRupees(5900m), DrCr.Credit) };
        advLines.AddRange(advPosting.TaxLines);
        ledgers.Post(new Voucher(rcptId, c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Receipt).Id, new DateOnly(2025, 4, 12), advLines));

        return c;
    }

    private static Company Fresh() => CompanyFactory.CreateSeeded("Fresh CDN Co", FyStart);

    [Fact]
    public void Json_round_trips_byte_stable()
    {
        var c = BuildCdnAdvanceCompany();
        var first = CanonicalJson.Export(c);
        var (model, errors) = CanonicalJson.Parse(first);
        Assert.Empty(errors);
        Assert.Equal(first, CanonicalJson.Export(model!));
        Assert.DoesNotContain("Tally", Encoding.UTF8.GetString(first), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Xml_round_trips_byte_stable()
    {
        var c = BuildCdnAdvanceCompany();
        var first = CanonicalXml.Export(c);
        var (model, errors) = CanonicalXml.Parse(first);
        Assert.Empty(errors);
        Assert.Equal(first, CanonicalXml.Export(model!));
    }

    [Fact]
    public void Export_import_into_fresh_reconciles_and_reproduces_the_reports()
    {
        var source = BuildCdnAdvanceCompany();
        var expected1 = Gstr1.Build(source, FyStart, ToEnd);

        foreach (var bytes in new[] { CanonicalJson.Export(source), CanonicalXml.Export(source) })
        {
            var (model, errors) = bytes[0] == (byte)'{' ? CanonicalJson.Parse(bytes) : CanonicalXml.Parse(bytes);
            Assert.Empty(errors);
            var fresh = Fresh();
            var result = new CompanyImportService(fresh).Apply(model!);
            Assert.True(result.Applied, string.Join("; ", result.Errors));

            var link = Assert.Single(fresh.CreditDebitNoteLinks);
            Assert.Equal(CdnType.Credit, link.CdnType);
            var record = Assert.Single(fresh.AdvanceReceipts);
            Assert.Equal(Money.FromRupees(900m), record.AdvanceTax);

            // The reports reproduce paisa-exact after the round-trip.
            var g1 = Gstr1.Build(fresh, FyStart, ToEnd);
            Assert.Equal(expected1.TotalIgst.Amount, g1.TotalIgst.Amount);   // 1,800 (sale) − 720 (credit) = 1,080
            Assert.Equal(1080m, g1.TotalIgst.Amount);
            Assert.Single(g1.Table9B);
            Assert.Single(g1.Table11A);
            Assert.Equal(900m, g1.AdvanceTaxReceived.Amount);
        }
    }

    [Fact]
    public void Import_round_trips_an_override_late_credit_note_losslessly()
    {
        // A credit note dated PAST the §34(2) 30-Nov cut-off, forced through at ENTRY time via overrideTimeLimit. The
        // §34(2) cut-off is an entry-time decision; IMPORT restores already-decided data losslessly (finding #2) — it
        // must NOT re-reject the note (which would break backup/restore for a legitimately-overridden late CDN).
        var fy = new DateOnly(2024, 4, 1);
        var c = Gst("Late CDN Co", fy);
        var sales = Add(c, "Sales", "Sales Accounts", false);
        var debtor = Add(c, "Buyer", "Sundry Debtors", true);
        debtor.PartyGst = new PartyGstDetails { RegistrationType = GstRegistrationType.Regular, Gstin = GstinGujarat, StateCode = "24" };
        var cdn = new CreditDebitNoteService(c);
        var origDate = new DateOnly(2024, 6, 1);                 // FY 2024-25 ⇒ cut-off 30-Nov-2025
        var lateDate = new DateOnly(2025, 12, 15);              // past the cut-off
        var cnId = Guid.NewGuid();
        var posting = cdn.BuildCreditDebitNote(CdnType.Credit, new[] { new GstService.TaxableLine(Money.FromRupees(1000m), 1800) },
            true, cnId, null, "INV-LATE", origDate, lateDate, "01 sales return", overrideTimeLimit: true);
        var lines = new List<EntryLine> { new(sales.Id, Money.FromRupees(1000m), DrCr.Debit), new(debtor.Id, Money.FromRupees(1180m), DrCr.Credit) };
        lines.AddRange(posting.TaxLines);
        new LedgerService(c).Post(new Voucher(cnId, c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.CreditNote).Id, lateDate, lines, partyId: debtor.Id));

        var window = (from: new DateOnly(2025, 4, 1), to: new DateOnly(2026, 3, 31));
        var expected = Gstr1.Build(c, window.from, window.to);

        var (model, errors) = CanonicalJson.Parse(CanonicalJson.Export(c));
        Assert.Empty(errors);
        var fresh = CompanyFactory.CreateSeeded("Fresh Late Co", fy);
        var result = new CompanyImportService(fresh).Apply(model!);
        Assert.True(result.Applied, string.Join("; ", result.Errors)); // lossless — NOT re-rejected on the late date

        var link = Assert.Single(fresh.CreditDebitNoteLinks);
        Assert.Equal(CdnType.Credit, link.CdnType);

        // The late credit note still nets the output down after the round-trip (paisa-exact).
        var g1 = Gstr1.Build(fresh, window.from, window.to);
        Assert.Equal(expected.TotalIgst.Amount, g1.TotalIgst.Amount);   // −180 (the ₹1,000 @18% credit)
        Assert.Equal(-180m, g1.TotalIgst.Amount);
        Assert.Single(g1.Table9B);
    }

    [Fact]
    public void Import_reports_a_clean_error_for_a_dangling_cdn_original_invoice_link()
    {
        var source = BuildCdnAdvanceCompany();
        var (model, errors) = CanonicalJson.Parse(CanonicalJson.Export(source));
        Assert.Empty(errors);

        // Corrupt the CDN link so its original-invoice reference resolves to NOTHING: an OriginalInvoiceVoucherId that is
        // neither imported nor present AND no consolidated number. Keep CdnVoucherId valid so it is not pruned as an orphan.
        var bad = model!.Payload.CreditDebitNoteLinks[0] with
        {
            OriginalInvoiceVoucherId = Guid.NewGuid(),
            OriginalInvoiceNumber = null,
        };
        var corrupted = model with { Payload = model.Payload with { CreditDebitNoteLinks = new[] { bad } } };

        var result = new CompanyImportService(Fresh()).Apply(corrupted);
        Assert.False(result.Applied);
        // A clean per-record pre-flight error (finding #3), NOT the opaque mid-Apply rollback the domain ctor would throw.
        Assert.Contains(result.Errors, e => e.Contains("neither imported nor present", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Errors, e => e.Contains("aborted and rolled back", StringComparison.Ordinal));
    }

    [Fact]
    public void Plain_gst_company_serialises_with_empty_cdn_and_advance_sections()
    {
        var c = Gst("Plain GST Co", FyStart);
        var (model, errors) = CanonicalJson.Parse(CanonicalJson.Export(c));
        Assert.Empty(errors);
        Assert.Empty(model!.Payload.CreditDebitNoteLinks);
        Assert.Empty(model.Payload.AdvanceReceipts);
        var g1 = Gstr1.Build(c, FyStart, ToEnd);
        Assert.Empty(g1.Table9B);
        Assert.Empty(g1.Table11A);
        Assert.Empty(g1.Table11B);
    }
}
