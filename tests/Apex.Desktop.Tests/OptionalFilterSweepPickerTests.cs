using System;
using System.Collections.Generic;
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
/// Census <b>5.7</b>, T2-174 (wave 13, track id, IV-388..IV-390): the voucher-entry and GST action screens that
/// skipped only a <i>cancelled</i> voucher now also skip an <i>Optional</i> one. Vendor (TallyPrime),
/// help.tallysolutions.com/create-optional-voucher/, opened by content 2026-11-24: Optional Vouchers record tentative
/// or future transactions <i>"without affecting your books of accounts"</i>, and when the transaction is due you
/// <i>"make the entry regular to include its accounting impact in the books of accounts"</i> (Ctrl+L, Regular).
///
/// <para>Each fixture holds live vouchers beside ONE Optional voucher. Every screen is built over a book reloaded
/// through a fresh <see cref="CompanyStorage"/>, first while the voucher is Optional and then after it is regularised
/// on that reloaded book and saved again, so neither half can pass vacuously.</para>
/// </summary>
public sealed class OptionalFilterSweepPickerTests : IDisposable
{
    private const string SellerGstin = "27AAPFU0939F1ZV";
    private const string PartyGstin = "27AAACC1206D1ZM";
    private static readonly DateOnly FyStart = new(2024, 4, 1);

    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public OptionalFilterSweepPickerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexOptionalSweepPick_" + Guid.NewGuid().ToString("N"));
        _storage = new CompanyStorage(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    // ------------------------------------------------------------------------------------------ scaffolding

    private (Company Book, CompanyStorage Store) Reload(string name)
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        var fresh = new CompanyStorage(_tempDir);
        return (fresh.Load(fresh.ListCompanies().Single(e => e.Name == name)), fresh);
    }

    /// <summary>Ctrl+L (Regular) on the RELOADED book — the engine verb the voucher screen's Ctrl+L calls — saved
    /// through that same fresh handle.</summary>
    private void RegulariseOnDisk(string name, Guid voucherId)
    {
        var (book, store) = Reload(name);
        Assert.True(book.FindVoucher(voucherId)!.Optional, "the fixture's voucher must still be Optional on disk");
        new LedgerService(book).SetOptional(voucherId, false);
        store.Save(book);
    }

    private static string Unique(string stem) => stem + " " + Guid.NewGuid().ToString("N")[..6];

    private static DomainLedger Add(Company c, string name, string groupName, bool openingIsDebit)
    {
        var l = new DomainLedger(Guid.NewGuid(), name, c.FindGroupByName(groupName)!.Id, Money.Zero, openingIsDebit);
        c.AddLedger(l);
        return l;
    }

    private static Company GstBook(string name, bool eInvoicing = false, bool eWay = false)
    {
        var c = CompanyFactory.CreateSeeded(name, FyStart);
        c.Address = "Unit 4, Fort Industrial Estate\nBallard Pier";
        c.Pin = "400001";
        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = "27", Gstin = SellerGstin, RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart, Periodicity = GstReturnPeriodicity.Monthly,
            EInvoicingEnabled = eInvoicing, EInvoiceApplicableFrom = eInvoicing ? FyStart : null,
            EWayBillEnabled = eWay, EWayApplicableFrom = eWay ? FyStart : null,
        });
        return c;
    }

    private static DomainLedger RegisteredParty(Company c, string name, string group, bool debit)
    {
        var p = Add(c, name, group, debit);
        p.PartyGst = new PartyGstDetails { RegistrationType = GstRegistrationType.Regular, Gstin = PartyGstin, StateCode = "27" };
        p.Mailing = new PartyMailingDetails
        {
            MailingName = name + " Co", Address = "12 Residency Road\nShivajinagar", Country = "India", Pincode = "400012",
        };
        return p;
    }

    /// <summary>Posts an intra-State 18% invoice of <paramref name="value"/> and returns it.</summary>
    private static Voucher Invoice(Company c, VoucherBaseType type, DomainLedger party, DomainLedger nominal,
        DateOnly on, decimal value, int number, bool accountingInvoice = false,
        IReadOnlyList<VoucherInventoryLine>? stock = null)
    {
        var outward = type == VoucherBaseType.Sales;
        var tax = new GstService(c).ComputeInvoiceTax(
            new[] { new GstService.TaxableLine(Money.FromRupees(value), 1800) }, false,
            outward ? GstTaxDirection.Output : GstTaxDirection.Input);
        var gross = new Money(value + tax.TaxLines.Sum(l => l.Amount.Amount));
        var lines = new List<EntryLine>
        {
            new(party.Id, gross, outward ? DrCr.Debit : DrCr.Credit),
            new(nominal.Id, Money.FromRupees(value), outward ? DrCr.Credit : DrCr.Debit),
        };
        lines.AddRange(tax.TaxLines);
        return new LedgerService(c).Post(new Voucher(Guid.NewGuid(),
            c.VoucherTypes.First(t => t.BaseType == type).Id, on, lines, number: number, partyId: party.Id,
            narration: $"DOC-{number}", isAccountingInvoice: accountingInvoice, inventoryLines: stock));
    }

    private static Guid[] Ids<T>(IEnumerable<T> rows, Func<T, Guid?> id) =>
        rows.Select(id).Where(g => g is not null).Select(g => g!.Value).OrderBy(g => g).ToArray();

    private static Guid[] Sorted(params Guid[] ids) => ids.OrderBy(g => g).ToArray();

    private static VoucherEntryViewModel Entry(Company book, CompanyStorage store, VoucherBaseType type) =>
        new(book, book.VoucherTypes.First(t => t.BaseType == type), store, onSaved: () => { }, onCancelled: () => { });

    /// <summary>A GST book with two posted Sales invoices; the second is Optional.</summary>
    private (string Name, Guid Live, Guid Optional) SalesBook()
    {
        var name = Unique("Optional Sales Co");
        var c = GstBook(name);
        var customer = RegisteredParty(c, "Acme Ltd", "Sundry Debtors", true);
        var sales = Add(c, "Sales", "Sales Accounts", false);
        var live = Invoice(c, VoucherBaseType.Sales, customer, sales, new DateOnly(2024, 6, 10), 10_000.37m, 1);
        var opt = Invoice(c, VoucherBaseType.Sales, customer, sales, new DateOnly(2024, 6, 12), 4_321.59m, 2);
        new LedgerService(c).SetOptional(opt.Id, true);
        _storage.Save(c);
        return (name, live.Id, opt.Id);
    }

    // ================================================================== VoucherEntryViewModel — IV-388

    /// <summary>T2-174 at <c>VoucherEntryViewModel.BuildSection34Pickers</c>: a §34 credit note is not offered an
    /// Optional sale as its original until the sale is regularised.</summary>
    [Fact]
    public void The_section_34_original_invoice_picker_does_not_offer_an_optional_sale_until_regularised()
    {
        var (name, live, opt) = SalesBook();

        var (book, store) = Reload(name);
        var note = Entry(book, store, VoucherBaseType.CreditNote);
        Assert.True(note.CanBeSection34Note);
        Assert.Equal(new[] { live }, Ids(note.CdnOriginalInvoices, o => o.Invoice?.Id));

        RegulariseOnDisk(name, opt);

        var (book2, store2) = Reload(name);
        Assert.Equal(Sorted(live, opt),
            Ids(Entry(book2, store2, VoucherBaseType.CreditNote).CdnOriginalInvoices, o => o.Invoice?.Id));
    }

    /// <summary>T2-174 at <c>VoucherEntryViewModel.BuildAdvancePickers</c>: the Journal's advance-adjustment invoice
    /// list does not offer an Optional sale until it is regularised.</summary>
    [Fact]
    public void The_advance_adjustment_invoice_picker_does_not_offer_an_optional_sale_until_regularised()
    {
        var (name, live, opt) = SalesBook();

        var (book, store) = Reload(name);
        var journal = Entry(book, store, VoucherBaseType.Journal);
        Assert.Equal(VoucherEntryViewModel.AdvanceAction.Adjust, journal.AdvanceActionForType);
        Assert.Equal(new[] { live }, Ids(journal.AdvanceInvoices, o => o.Invoice?.Id));

        RegulariseOnDisk(name, opt);

        var (book2, store2) = Reload(name);
        Assert.Equal(Sorted(live, opt),
            Ids(Entry(book2, store2, VoucherBaseType.Journal).AdvanceInvoices, o => o.Invoice?.Id));
    }

    // ================================================================== Generate e-Invoice / e-Way Bill — IV-389

    private static GenerateEInvoiceViewModel EInvoiceScreen(Company book, CompanyStorage store) =>
        new(book, store, onChanged: null, folder: "C:/nowhere", today: new DateOnly(2024, 6, 15), writeBytes: (_, _) => { });

    private static GenerateEWayBillViewModel EWayScreen(Company book, CompanyStorage store) =>
        new(book, store, onChanged: null, folder: "C:/nowhere", today: new DateOnly(2024, 6, 15),
            now: () => new DateTimeOffset(2024, 6, 15, 10, 0, 0, TimeSpan.Zero), writeBytes: (_, _) => { });

    /// <summary>A covered service-invoice book: Consultancy (SAC 998313) at 18% to a registered in-state buyer.</summary>
    private static (Company C, DomainLedger Buyer, DomainLedger Consultancy) EInvoiceBook(string name)
    {
        var c = GstBook(name, eInvoicing: true);
        var consultancy = Add(c, "Consultancy Income", "Sales Accounts", false);
        consultancy.SalesPurchaseGst = new StockItemGstDetails
        {
            HsnSac = "998313", SupplyType = GstSupplyType.Services, Taxability = GstTaxability.Taxable, RateBasisPoints = 1800,
        };
        return (c, RegisteredParty(c, "Buyer", "Sundry Debtors", true), consultancy);
    }

    /// <summary>T2-174 at <c>GenerateEInvoiceViewModel.Rebuild</c>: an Optional covered sale is not listed for an IRN
    /// until it is regularised.</summary>
    [Fact]
    public void Generate_e_invoice_does_not_list_an_optional_sale_until_regularised()
    {
        var name = Unique("Optional EInv Co");
        var (c, buyer, consultancy) = EInvoiceBook(name);
        var live = Invoice(c, VoucherBaseType.Sales, buyer, consultancy, new DateOnly(2024, 6, 10), 10_000.37m, 1, accountingInvoice: true);
        var opt = Invoice(c, VoucherBaseType.Sales, buyer, consultancy, new DateOnly(2024, 6, 12), 4_321.59m, 2, accountingInvoice: true);
        Assert.Equal(EInvoiceCoverage.Covered, new EInvoiceService(c).CoverageOf(opt));   // premise: it WOULD be listed
        new LedgerService(c).SetOptional(opt.Id, true);
        _storage.Save(c);

        var (book, store) = Reload(name);
        Assert.Equal(new[] { live.Id }, Ids(EInvoiceScreen(book, store).Rows, r => (Guid?)r.VoucherId));

        RegulariseOnDisk(name, opt.Id);

        var (book2, store2) = Reload(name);
        Assert.Equal(Sorted(live.Id, opt.Id), Ids(EInvoiceScreen(book2, store2).Rows, r => (Guid?)r.VoucherId));
    }

    /// <summary>The documented exception (OURS, IV-389): an Optional voucher that already holds a GENERATED IRN stays
    /// on the screen, because this screen is the only route to cancel that IRN.</summary>
    [Fact]
    public void Generate_e_invoice_keeps_an_optional_sale_that_holds_a_generated_irn()
    {
        var name = Unique("Optional EInv IRN Co");
        var (c, buyer, consultancy) = EInvoiceBook(name);
        var opt = Invoice(c, VoucherBaseType.Sales, buyer, consultancy, new DateOnly(2024, 6, 12), 4_321.59m, 1, accountingInvoice: true);
        var svc = new EInvoiceService(c);
        var record = svc.PrepareRecord(opt);
        svc.RecordIrpResponse(record, new string('a', 64), "112410000000001", new DateOnly(2024, 6, 12), "QR", new byte[] { 1 });
        new LedgerService(c).SetOptional(opt.Id, true);
        _storage.Save(c);

        var (book, store) = Reload(name);
        var row = Assert.Single(EInvoiceScreen(book, store).Rows);
        Assert.Equal(opt.Id, row.VoucherId);
        Assert.Equal(nameof(EInvoiceStatus.Generated), row.Status);
    }

    private static (Company C, DomainLedger Buyer, DomainLedger Sales, StockItem Item) EWayBook(string name)
    {
        var c = GstBook(name, eWay: true);
        var inv = new InventoryService(c);
        var item = inv.CreateStockItem("Widget", inv.CreateStockGroup("Goods").Id,
            inv.CreateSimpleUnit("Nos", "Numbers", unitQuantityCode: "NOS").Id);
        item.Gst = new StockItemGstDetails { HsnSac = "847130", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800 };
        inv.AddOpeningBalance(item.Id, c.MainLocation!.Id, 1000m, Money.FromRupees(20m));
        return (c, RegisteredParty(c, "Buyer", "Sundry Debtors", true), Add(c, "Sales", "Sales Accounts", false), item);
    }

    private static Voucher GoodsSale(Company c, DomainLedger buyer, DomainLedger sales, StockItem item, DateOnly on,
        decimal value, int number) =>
        Invoice(c, VoucherBaseType.Sales, buyer, sales, on, value, number, stock: new[]
        {
            new VoucherInventoryLine(item.Id, c.MainLocation!.Id, 1m, Money.FromRupees(value)),
        });

    /// <summary>T2-174 at <c>GenerateEWayBillViewModel.Rebuild</c>: an Optional covered goods sale is not listed for an
    /// e-Way Bill until it is regularised.</summary>
    [Fact]
    public void Generate_e_way_bill_does_not_list_an_optional_sale_until_regularised()
    {
        var name = Unique("Optional EWay Co");
        var (c, buyer, sales, item) = EWayBook(name);
        var live = GoodsSale(c, buyer, sales, item, new DateOnly(2024, 6, 10), 54_321.37m, 1);
        var opt = GoodsSale(c, buyer, sales, item, new DateOnly(2024, 6, 12), 61_234.59m, 2);
        Assert.Equal(EWayCoverage.Required, new EWayBillService(c).CoverageOf(opt));   // premise: it WOULD be listed
        new LedgerService(c).SetOptional(opt.Id, true);
        _storage.Save(c);

        var (book, store) = Reload(name);
        Assert.Equal(new[] { live.Id }, Ids(EWayScreen(book, store).Rows, r => (Guid?)r.VoucherId));

        RegulariseOnDisk(name, opt.Id);

        var (book2, store2) = Reload(name);
        Assert.Equal(Sorted(live.Id, opt.Id), Ids(EWayScreen(book2, store2).Rows, r => (Guid?)r.VoucherId));
    }

    /// <summary>The documented exception (OURS, IV-389): an Optional sale holding a GENERATED e-Way Bill stays
    /// listed, so the bill can still be cancelled from this screen.</summary>
    [Fact]
    public void Generate_e_way_bill_keeps_an_optional_sale_that_holds_a_generated_bill()
    {
        var name = Unique("Optional EWay EWB Co");
        var (c, buyer, sales, item) = EWayBook(name);
        var opt = GoodsSale(c, buyer, sales, item, new DateOnly(2024, 6, 12), 61_234.59m, 1);
        var svc = new EWayBillService(c);
        var record = svc.PrepareRecord(opt, new DateOnly(2024, 6, 12));
        svc.SetPartB(record, null, EWayTransportMode.Road, "MH12AB1234", 250);
        svc.RecordPortalResponse(record, "181234567890",
            new DateTimeOffset(2024, 6, 12, 10, 0, 0, TimeSpan.Zero), new DateTimeOffset(2024, 6, 14, 23, 59, 0, TimeSpan.Zero));
        new LedgerService(c).SetOptional(opt.Id, true);
        _storage.Save(c);

        var (book, store) = Reload(name);
        var row = Assert.Single(EWayScreen(book, store).Rows);
        Assert.Equal(opt.Id, row.VoucherId);
        Assert.Equal(nameof(EWayStatus.Generated), row.Status);
    }

    // ================================================================== GST action screens — IV-390

    /// <summary>
    /// T2-174 at <c>RunSetOffViewModel.DischargedCashFor</c>: an Optional 3B cash discharge has paid nothing, so the
    /// head it discharged is shown as still due until the discharge is regularised.
    /// </summary>
    [Fact]
    public void Run_set_off_does_not_net_an_optional_cash_discharge_until_regularised()
    {
        var name = Unique("Optional SetOff Co");
        var c = GstBook(name);
        var customer = RegisteredParty(c, "Acme Ltd", "Sundry Debtors", true);
        var sales = Add(c, "Sales", "Sales Accounts", false);
        var bank = Add(c, "HDFC Bank", "Bank Accounts", true);
        Invoice(c, VoucherBaseType.Sales, customer, sales, new DateOnly(2024, 4, 5), 1_234.56m, 1);   // 111.11 + 111.11

        var deposit = new GstDepositService(c);
        var monthEnd = new DateOnly(2024, 4, 30);
        foreach (var head in new[] { GstTaxHead.Central, GstTaxHead.State })
        {
            deposit.PostPmt06(head, GstMinorHead.Tax, Money.FromRupees(200m), bank, monthEnd,
                cpin: "24CPIN00000001000", cin: "CIN" + head);
            deposit.PostCashDischarge(head, Money.FromRupees(111.11m), monthEnd);
        }
        var cgstDischarge = c.Vouchers.Single(v => v.Lines.Any(l => l.Side == DrCr.Debit
            && l.Gst is { Adjustment: GstAdjustmentKind.CashPayment, TaxHead: GstTaxHead.Central }));
        new LedgerService(c).SetOptional(cgstDischarge.Id, true);
        _storage.Save(c);

        var (book, store) = Reload(name);
        var page = new RunSetOffViewModel(book, store);
        Assert.Equal("2024-04", page.SelectedPeriod!.Period);
        Assert.Equal("111.11", page.CashCells.Single(x => x.Head == "CGST").Required);
        Assert.Equal("0.00", page.CashCells.Single(x => x.Head == "SGST/UTGST").Required);

        RegulariseOnDisk(name, cgstDischarge.Id);

        var (book2, store2) = Reload(name);
        var page2 = new RunSetOffViewModel(book2, store2);
        Assert.Equal("0.00", page2.CashCells.Single(x => x.Head == "CGST").Required);
        Assert.Equal("0.00", page2.CashCells.Single(x => x.Head == "SGST/UTGST").Required);
    }

    private static Voucher SupplierPurchase(Company c, decimal value, int number, DateOnly on)
    {
        var supplier = c.FindLedgerByName("Local Supplier") ?? RegisteredParty(c, "Local Supplier", "Sundry Creditors", false);
        var purchases = c.FindLedgerByName("Purchases") ?? Add(c, "Purchases", "Purchase Accounts", true);
        return Invoice(c, VoucherBaseType.Purchase, supplier, purchases, on, value, number);
    }

    /// <summary>T2-174 at <c>PostItcReversalViewModel.BuildSourceVouchers</c>: a Rule 37 reversal is not offered an
    /// Optional purchase as its anchor until the purchase is regularised.</summary>
    [Fact]
    public void Itc_reversal_does_not_offer_an_optional_purchase_as_an_anchor_until_regularised()
    {
        var name = Unique("Optional ITC Rev Co");
        var c = GstBook(name);
        var live = SupplierPurchase(c, 5_000.37m, 1, new DateOnly(2024, 4, 3));
        var opt = SupplierPurchase(c, 2_345.67m, 2, new DateOnly(2024, 4, 4));
        new LedgerService(c).SetOptional(opt.Id, true);
        _storage.Save(c);

        var (book, store) = Reload(name);
        var page = new PostItcReversalViewModel(book, store);
        page.Kind = ItcReversalPostKind.Rule37;
        Assert.Equal(new[] { live.Id }, Ids(page.SourceVouchers, r => (Guid?)r.VoucherId));

        RegulariseOnDisk(name, opt.Id);

        var (book2, store2) = Reload(name);
        var page2 = new PostItcReversalViewModel(book2, store2);
        page2.Kind = ItcReversalPostKind.Rule37;
        Assert.Equal(Sorted(live.Id, opt.Id), Ids(page2.SourceVouchers, r => (Guid?)r.VoucherId));
    }

    /// <summary>
    /// T2-174 at <c>PostItcReversalViewModel.PrimaryHasAnchorablePurchase</c>: on a two-registration book whose ONLY
    /// purchase is Optional, the refusal on the second registration does not send the operator to the primary for a
    /// purchase that is not on the books, until that purchase is regularised.
    /// </summary>
    [Fact]
    public void Itc_reversal_refusal_does_not_point_at_an_optional_purchase_until_regularised()
    {
        var name = Unique("Optional ITC Refusal Co");
        var c = GstBook(name);
        var branch = new GstRegistration(
            Guid.NewGuid(), "Gujarat Branch", "24", "24AAACC1206D1Z" + Gstin.ComputeCheckDigit("24AAACC1206D1Z0"),
            GstRegistrationType.Regular, FyStart);
        c.Gst!.AddRegistration(branch);
        c.Gst!.EnsureValid();
        var opt = SupplierPurchase(c, 2_345.67m, 1, new DateOnly(2024, 4, 4));
        new LedgerService(c).SetOptional(opt.Id, true);
        _storage.Save(c);

        string RefusalOnTheBranch()
        {
            var (book, store) = Reload(name);
            var page = new PostItcReversalViewModel(book, store);
            page.Kind = ItcReversalPostKind.Rule37;
            page.SelectedRegistration = page.Registrations.Single(r => r.Id == branch.Id);
            Assert.False(page.Post());
            return page.Message!;
        }

        var optional = RefusalOnTheBranch();
        Assert.Contains("records no such purchase under", optional, StringComparison.Ordinal);
        Assert.DoesNotContain("The purchases that carry input tax are recorded under", optional, StringComparison.Ordinal);

        RegulariseOnDisk(name, opt.Id);

        var regular = RefusalOnTheBranch();
        Assert.Contains("The purchases that carry input tax are recorded under", regular, StringComparison.Ordinal);
    }

    /// <summary>T2-174 at <c>RecordIsdCreditViewModel.Rebuild</c>: an Optional ISD credit receipt is not listed among
    /// the recorded credits until it is regularised.</summary>
    [Fact]
    public void Record_isd_credit_does_not_list_an_optional_receipt_until_regularised()
    {
        var name = Unique("Optional ISD Co");
        var c = GstBook(name);
        var ho = Add(c, "Head Office A/c", "Sundry Creditors", false);
        var isd = new GstIsdCreditService(c);
        var live = isd.Post("ISD/2024/01", new DateOnly(2024, 4, 10),
            new GstIsdCreditService.IsdCreditAmount(12_345, 12_345, 0, 0), ho.Id, new DateOnly(2024, 4, 10));
        var opt = isd.Post("ISD/2024/02", new DateOnly(2024, 4, 20),
            new GstIsdCreditService.IsdCreditAmount(6_789, 6_789, 0, 0), ho.Id, new DateOnly(2024, 4, 20));
        new LedgerService(c).SetOptional(opt.Id, true);
        _storage.Save(c);

        var (book, store) = Reload(name);
        var page = new RecordIsdCreditViewModel(book, store);
        Assert.Equal(new[] { live.Id }, Ids(page.Recorded, r => (Guid?)r.VoucherId));

        RegulariseOnDisk(name, opt.Id);

        var (book2, store2) = Reload(name);
        Assert.Equal(Sorted(live.Id, opt.Id), Ids(new RecordIsdCreditViewModel(book2, store2).Recorded, r => (Guid?)r.VoucherId));
    }
}
