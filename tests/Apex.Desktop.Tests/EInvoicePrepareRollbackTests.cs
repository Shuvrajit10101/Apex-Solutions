using System;
using System.Collections.Generic;
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
/// 🔴 <b>F1 — A REFUSED e-INVOICE MUST NOT BURN THE VOUCHER.</b>
///
/// <para><c>GenerateEInvoiceViewModel.PrepareAndWriteJson</c> calls <c>EInvoiceService.PrepareRecord</c> <b>before</b>
/// <c>EInvoiceJson.BuildInv01</c>. <c>PrepareRecord</c> is a MUTATOR: it attaches a <b>Pending</b>
/// <c>EInvoiceRecord</c> to the company and <b>consumes the document number</b>. When the INV-01 pre-flight then
/// refuses the document, the failure path returned the message and left that record in place — and every exit out of
/// that state is closed:</para>
/// <list type="number">
/// <item><b>retry</b> — <c>PrepareRecord</c> refuses: "An e-invoice record already exists for this voucher";</item>
/// <item><b>re-number</b> — refused: a document number already held by ANY record is not reusable;</item>
/// <item><b>cancel the orphan</b> — refused: "Only a Generated e-invoice can be cancelled", and it is Pending;</item>
/// <item><b>remove it</b> — <c>Company.RemoveEInvoiceRecord</c> exists and NO view model ever called it.</item>
/// </list>
/// <para>So one press of Generate against a service ledger with no SAC left the voucher <b>permanently
/// unregistrable</b>, showing a false "Pending" for a document that was never built — and <b>the remedy the refusal
/// itself prints</b>, "Enter the HSN/SAC on the ledger and generate it again", <b>could not be followed</b>. An exempt
/// or taxable service ledger whose SAC was never filled is the ORDINARY case that pre-flight was written for, so this
/// was the normal path, not a corner.</para>
///
/// <para><b>Both halves of the harm are asserted, because they are separate.</b> The in-session trap needs no save at
/// all (the refusal path never saved, so the orphan lived only in memory — and still blocked every retry); and any
/// LATER successful action on the screen calls <c>CompanyStorage.Save</c>, which persists the orphan into the book.
/// The second test drives exactly that and reloads from disk.</para>
/// </summary>
public sealed class EInvoicePrepareRollbackTests : IDisposable
{
    private const string SellerGstin = "27AAPFU0939F1ZV";
    private const string BuyerGstin = "27AACCM9910C1ZK";   // distinct from the seller's: not a self-invoice
    private const string GoodSac = "998311";

    private static readonly DateOnly FyStart = new(2026, 4, 1);
    private static readonly DateOnly SaleDate = new(2026, 9, 10);

    // Hand-computed: Consultancy ₹10,000 Cr @ 18% intra ⇒ CGST 900 + SGST 900; the buyer is billed ₹11,800.
    private const decimal Taxed = 10_000m;
    private const decimal HeadTax = 900m;
    private const decimal PartyDebit = 11_800m;

    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public EInvoicePrepareRollbackTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexEInvRollbackTests_" + Guid.NewGuid().ToString("N"));
        _storage = new CompanyStorage(_tempDir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch (IOException) { /* a throwaway temp dir — a locked file must not fail the test */ }
    }

    /// <summary>
    /// 🔴 <b>THE DEFECT, driven through the screen exactly as an operator meets it.</b> Press Generate on a covered
    /// service invoice whose ledger has no SAC; read the refusal; fill the SAC it names; press Generate again. The
    /// second press must now produce the INV-01. Before the fix it was refused with "An e-invoice record already
    /// exists for this voucher" and the voucher could never be registered.
    /// </summary>
    [Fact]
    public void Pressing_generate_again_after_filling_the_SAC_the_refusal_named_now_writes_the_INV01()
    {
        var (company, sale, consultancy) = ServiceInvoiceWithNoSac();
        var written = new Dictionary<string, byte[]>();
        var page = NewEInvoiceVm(company, written);
        page.HighlightedIndex = IndexOf(page, sale);

        // --- press 1: refused, and the refusal tells the operator what to do.
        Assert.False(page.PrepareAndWriteJson());
        Assert.False(page.LastActionSucceeded);
        Assert.Contains("Consultancy Income", page.Message!);
        Assert.Contains("generate it again", page.Message!);
        Assert.Empty(written);

        // 🔴 THE ASSERTION THAT REDDENS ON TODAY'S main: the refusal left NO record and did NOT consume the doc-no.
        Assert.Empty(company.EInvoiceRecords);
        Assert.False(company.HasEInvoiceDocumentNumber(EInvoiceService.DocumentNumberOf(company, sale)));

        // --- the operator does exactly what the message says.
        consultancy.SalesPurchaseGst!.HsnSac = GoodSac;

        // --- press 2: the document is registrable, and the INV-01 is really written.
        Assert.True(page.PrepareAndWriteJson(), page.Message);
        Assert.True(page.LastActionSucceeded);

        var record = Assert.Single(company.EInvoiceRecords);
        Assert.Equal(EInvoiceStatus.Pending, record.Status);
        Assert.Equal(EInvoiceService.DocumentNumberOf(company, sale), record.DocumentNumberUpper);

        // MUTATION-VERIFIED ON THE EMITTED BYTES, not on a view-model flag: the file holds the real payload, and its
        // total is the voucher's own posted party debit.
        var file = Assert.Single(written);
        using var payload = System.Text.Json.JsonDocument.Parse(file.Value);
        var root = payload.RootElement;
        Assert.Equal(GoodSac, root.GetProperty("ItemList").EnumerateArray().Single().GetProperty("HsnCd").GetString());
        Assert.Equal(Taxed, root.GetProperty("ValDtls").GetProperty("AssVal").GetDecimal());
        Assert.Equal(PartyDebit, root.GetProperty("ValDtls").GetProperty("TotInvVal").GetDecimal());
        Assert.Equal(PartyDebit, PostedPartyDebit(sale));          // the books, computed independently
    }

    /// <summary>
    /// 🔴 <b>THE SECOND HALF OF THE HARM: the orphan used to be PERSISTED.</b> The refusal path itself never saved,
    /// but the screen saves on every later success — so one refused press followed by any ordinary action wrote the
    /// false "Pending" record into the book, where it outlived the session. This drives that exact sequence and
    /// reloads the company from disk.
    /// </summary>
    [Fact]
    public void A_refused_generation_leaves_nothing_behind_in_the_saved_book()
    {
        var (company, sale, consultancy) = ServiceInvoiceWithNoSac();
        var written = new Dictionary<string, byte[]>();
        var page = NewEInvoiceVm(company, written);
        page.HighlightedIndex = IndexOf(page, sale);

        Assert.False(page.PrepareAndWriteJson());

        // The later success is what calls Save — the orphan rode along on it.
        consultancy.SalesPurchaseGst!.HsnSac = GoodSac;
        Assert.True(page.PrepareAndWriteJson(), page.Message);

        var entry = _storage.ListCompanies().Single(e => e.Name == company.Name);
        var reloaded = _storage.Load(entry);

        // 🔴 EXACTLY ONE record on disk — the real one. Before the fix there were two, and the first was an orphan
        // Pending record against a document that was never built, holding a document number for ever.
        var persisted = Assert.Single(reloaded.EInvoiceRecords);
        Assert.Equal(EInvoiceStatus.Pending, persisted.Status);
        Assert.Equal(sale.Id, persisted.SourceVoucherId);
        Assert.Equal(EInvoiceService.DocumentNumberOf(company, sale), persisted.DocumentNumberUpper);
    }

    /// <summary>The orphan also used to make the grid LIE: the voucher read "Pending" for a document the app had
    /// refused to build. Asserted on the rebuilt row, which is what the operator actually sees.</summary>
    [Fact]
    public void A_refused_generation_does_not_leave_the_row_showing_Pending()
    {
        var (company, sale, _) = ServiceInvoiceWithNoSac();
        var page = NewEInvoiceVm(company, new Dictionary<string, byte[]>());
        page.HighlightedIndex = IndexOf(page, sale);

        Assert.False(page.PrepareAndWriteJson());
        page.Rebuild();

        var row = page.Rows.Single(r => r.VoucherId == sale.Id);
        Assert.False(row.HasRecord);
        Assert.Equal("—", row.Status);
        Assert.True(row.IsCovered);                                 // still covered — still registrable
    }

    // ================================================================ helpers

    private GenerateEInvoiceViewModel NewEInvoiceVm(Company c, IDictionary<string, byte[]> written) =>
        new(c, _storage, onChanged: null, folder: Path.Combine(_tempDir, "out"),
            today: SaleDate, writeBytes: (p, b) => written[p] = b);

    private static int IndexOf(GenerateEInvoiceViewModel page, Voucher v)
    {
        var idx = page.Rows.ToList().FindIndex(r => r.VoucherId == v.Id);
        Assert.True(idx >= 0, "the covered service sale should be listed");
        return idx;
    }

    /// <summary>The voucher's own posted party DEBIT — the book's statement of what the buyer was billed.</summary>
    private static decimal PostedPartyDebit(Voucher v) =>
        v.Lines.Where(l => l.LedgerId == v.PartyId && l.Side == DrCr.Debit).Sum(l => l.Amount.Amount);

    /// <summary>
    /// A COVERED, ordinary ledger-only service accounting invoice — Consultancy ₹10,000 Cr @ 18% to a registered
    /// in-state buyer — whose service ledger declares a GST block with <b>no SAC</b>. That is the shape the INV-01
    /// pre-flight refuses, and it is ordinary master data an operator is expected to correct, not a contrived input.
    /// </summary>
    private (Company Company, Voucher Sale, DomainLedger Consultancy) ServiceInvoiceWithNoSac()
    {
        var c = CompanyFactory.CreateSeeded("EInv Rollback Co", FyStart);
        c.Address = "Unit 4, Fort Industrial Estate\nBallard Pier";
        c.Pin = "400001";
        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = "27", Gstin = SellerGstin, RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart, Periodicity = GstReturnPeriodicity.Monthly,
            EInvoicingEnabled = true, EInvoiceApplicableFrom = FyStart,
        });

        var consultancy = Add(c, "Consultancy Income", "Sales Accounts", openingIsDebit: false);
        consultancy.SalesPurchaseGst = new StockItemGstDetails
        {
            HsnSac = null, SupplyType = GstSupplyType.Services,
            Taxability = GstTaxability.Taxable, RateBasisPoints = 1800,
        };

        var buyer = Add(c, "Buyer", "Sundry Debtors", openingIsDebit: true);
        buyer.PartyGst = new PartyGstDetails
        { RegistrationType = GstRegistrationType.Regular, Gstin = BuyerGstin, StateCode = "27" };
        buyer.Mailing = new PartyMailingDetails
        {
            MailingName = "Buyer Trading Co", Address = "12 Residency Road\nShivajinagar",
            Country = "India", Pincode = "400012",
        };

        var tax = new GstService(c).ComputeInvoiceTax(
            new[] { new GstService.TaxableLine(new Money(Taxed), 1800) },
            interState: false, GstTaxDirection.Output);
        Assert.Equal(2 * HeadTax, tax.TaxLines.Sum(l => l.Amount.Amount));        // the fixture's own premise

        var lines = new List<EntryLine>
        {
            new(buyer.Id, new Money(PartyDebit), DrCr.Debit),
            new(consultancy.Id, new Money(Taxed), DrCr.Credit),
        };
        lines.AddRange(tax.TaxLines);

        var sale = new LedgerService(c).Post(new Voucher(
            Guid.NewGuid(), c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id, SaleDate, lines,
            number: 1, partyId: buyer.Id, isAccountingInvoice: true));

        // The IRP path is genuinely reachable: this is a refusal of something the app would otherwise have emitted.
        Assert.Equal(EInvoiceCoverage.Covered, new EInvoiceService(c).CoverageOf(sale));
        Assert.Null(Gstr1.ServiceSacOf(consultancy));
        _storage.Save(c);
        return (c, sale, consultancy);
    }

    private static DomainLedger Add(Company c, string name, string groupName, bool openingIsDebit)
    {
        var l = new DomainLedger(Guid.NewGuid(), name, c.FindGroupByName(groupName)!.Id, Money.Zero, openingIsDebit);
        c.AddLedger(l);
        return l;
    }
}
