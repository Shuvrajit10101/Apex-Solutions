using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Io;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.Tests;

/// <summary>
/// <b>Census T0-9, the projector half — WHICH e-invoice records may reach a printed document.</b>
///
/// <para>The renderer's side (that a QR is drawn at all, and that it is the IRP's string verbatim) is pinned by
/// <c>EInvoiceQrPrintTests</c> in <c>Apex.Ledger.Io.Tests</c>. This file pins the gate in front of it, which is the
/// part with teeth: an <see cref="EInvoiceRecord"/> exists from the moment a request is staged and it SURVIVES
/// cancellation, so "a record exists" and "this document has a live IRN" are different questions.</para>
///
/// <para><b>The cancelled case is the one that matters and it is not obvious.</b> A cancelled IRN's signed QR still
/// verifies against the IRP's public key — the signature was genuine when it was made, and cancelling at the portal
/// does not and cannot un-sign it. So a document printed with a cancelled IRN's QR scans as a VALID e-invoice.
/// The artefact outlives the authority it stood for, and the failure is silent at every step: the operator sees a QR,
/// the recipient scans a QR, the signature checks out, and the IRN behind it was withdrawn.</para>
///
/// <para>Fixture figures are the project's odd-valued supply — 60.125 Nos @ ₹786.64 = ₹47,296.73 — so no assertion
/// can pass on a round number by accident.</para>
/// </summary>
public sealed class EInvoiceArtefactPrintTests : IDisposable
{
    private const string GstinMaharashtra = "27AAPFU0939F1ZV";
    private static readonly DateOnly FyStart = new(2024, 4, 1);
    private const string Irn = "a5c12bbe4c1f0b1b1cfa4e0c2b4a63c9d8e7f60a1b2c3d4e5f60718293a4b5c6";
    private const string AckNo = "112010036777771";

    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public EInvoiceArtefactPrintTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexEInvPrint_" + Guid.NewGuid().ToString("N"));
        _storage = new CompanyStorage(_tempDir);
    }

    /// <summary>A JWS-shaped signed QR carrying a nonce, so no comparison below can pass by comparing a fixed
    /// constant with itself.</summary>
    private static string SignedQr(string nonce)
    {
        string B64(string s) => Convert.ToBase64String(Encoding.ASCII.GetBytes(s))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return B64("{\"alg\":\"RS256\",\"typ\":\"JWT\"}") + "."
             + B64("{\"data\":\"{\\\"SellerGstin\\\":\\\"" + GstinMaharashtra + "\\\",\\\"Nonce\\\":\\\"" + nonce
                   + "\\\",\\\"TotInvVal\\\":55810.14}\"}") + "."
             + new string('A', 342);
    }

    // ================================================================ the artefacts reach the document

    [Fact]
    public void A_generated_irn_reaches_both_the_printed_bytes_and_the_screen_the_operator_approves()
    {
        var k = NewKit("EInv Generated Co");
        var v = PostSale(k);
        var qr = SignedQr("NONCE-GEN");
        Generate(k.Company, v, qr);

        var data = VoucherPrintProjector.ProjectInvoice(k.Company, v);
        Assert.True(data.StatesEInvoice);
        Assert.Equal(qr, data.EInvoiceSignedQr);
        Assert.Equal(Irn, data.EInvoiceIrn);
        Assert.Equal(AckNo, data.EInvoiceAckNo);
        Assert.Equal("01-04-2024", data.EInvoiceAckDateText);

        // The bytes the customer holds…
        var preview = PrintDrilled(k.Vm, v.Id);
        var text = Encoding.Latin1.GetString(preview.PdfBytes);
        Assert.Contains("/Subtype /Image", text, StringComparison.Ordinal);
        Assert.Contains(Irn, text, StringComparison.Ordinal);
        // …and the mirror the operator approves. If these disagreed the operator would approve one document and
        // issue another — the divergence W0-1 and W0-15 each had to close on other fields.
        var cells = preview.Pages[0].Lines.SelectMany(r => r.Cells).ToList();
        Assert.Contains(cells, c => c.Contains(Irn, StringComparison.Ordinal));
        Assert.Contains(cells, c => c.Contains(AckNo, StringComparison.Ordinal));
        Assert.Contains(cells, c => c.Contains("Signed QR code", StringComparison.Ordinal));
    }

    /// <summary>ER-5, end to end: the symbol in the file is the QR of the stored string, character for character.
    /// Built here from the independently-verified encoder, so any transformation on the way moves one side only.</summary>
    [Fact]
    public void The_symbol_in_the_file_is_the_stored_irp_string_and_not_a_re_derivation()
    {
        var k = NewKit("EInv Verbatim Co");
        var v = PostSale(k);
        var qr = SignedQr("NONCE-VERBATIM");
        Generate(k.Company, v, qr);

        var pdf = InvoicePdf.Render(
            VoucherPrintProjector.ProjectInvoice(k.Company, v), new PrintConfig(), new PageConfig());
        var expected = PdfBitmap.FromQr(QrCode.Encode(
            k.Company.FindEInvoiceRecordForVoucher(v.Id)!.SignedQr!, QrErrorCorrection.Low)).ToBytes();
        Assert.Equal(expected, ExtractImage(pdf));
    }

    /// <summary>Both projector passes carry it — the ITEM invoice above and the ledger-only SERVICE (accounting)
    /// invoice here. A field wired into one pass only is how this projector has drifted before.</summary>
    [Fact]
    public void A_service_accounting_invoice_carries_the_artefacts_too()
    {
        var k = NewServiceKit("EInv Service Co");
        var v = PostServiceSale(k);
        var qr = SignedQr("NONCE-SERVICE");
        Generate(k.Company, v, qr);

        var data = VoucherPrintProjector.ProjectInvoice(k.Company, v);
        Assert.Empty(v.InventoryLines);                    // it really is the service pass
        Assert.Equal(qr, data.EInvoiceSignedQr);
        Assert.Equal(Irn, data.EInvoiceIrn);
        var text = Encoding.Latin1.GetString(
            InvoicePdf.Render(data, new PrintConfig(), new PageConfig()));
        Assert.Contains("/Subtype /Image", text, StringComparison.Ordinal);
    }

    // ================================================================ the gate

    /// <summary>
    /// 🔴 <b>A CANCELLED IRN must not be printed.</b> Its signed QR still verifies — cancelling at the portal cannot
    /// un-sign it — so a document carrying it would scan as a valid e-invoice for a document that no longer has one.
    /// </summary>
    [Fact]
    public void A_cancelled_irn_prints_nothing_even_though_its_signature_is_still_valid()
    {
        var k = NewKit("EInv Cancelled Co");
        var v = PostSale(k);
        var qr = SignedQr("NONCE-CANCELLED");
        var record = Generate(k.Company, v, qr);

        // Control: while it is live, it prints. Without this the assertion below could pass on a broken fixture.
        Assert.True(VoucherPrintProjector.ProjectInvoice(k.Company, v).StatesEInvoice);

        new EInvoiceService(k.Company).Cancel(record, new DateOnly(2024, 4, 1), "1");
        Assert.Equal(EInvoiceStatus.Cancelled, record.Status);
        Assert.Equal(Irn, record.Irn);              // the artefacts are STILL on the record…
        Assert.Equal(qr, record.SignedQr);

        var data = VoucherPrintProjector.ProjectInvoice(k.Company, v);   // …and none of them reaches the document
        Assert.False(data.StatesEInvoice);
        Assert.Equal(string.Empty, data.EInvoiceSignedQr);
        Assert.Equal(string.Empty, data.EInvoiceIrn);
        Assert.Equal(string.Empty, data.EInvoiceAckNo);
        Assert.Equal(string.Empty, data.EInvoiceAckDateText);

        var text = Encoding.Latin1.GetString(InvoicePdf.Render(data, new PrintConfig(), new PageConfig()));
        Assert.DoesNotContain("/Subtype /Image", text, StringComparison.Ordinal);
        Assert.DoesNotContain(Irn, text, StringComparison.Ordinal);
    }

    /// <summary>A staged-but-unanswered request has no IRN at all (<c>Irn</c> is null by construction until
    /// <c>RecordIrpResponse</c> runs), so the document is simply not yet an e-invoice.</summary>
    [Fact]
    public void A_pending_request_prints_nothing()
    {
        var k = NewKit("EInv Pending Co");
        var v = PostSale(k);
        var record = new EInvoiceService(k.Company).PrepareRecord(v);
        Assert.Equal(EInvoiceStatus.Pending, record.Status);
        Assert.Null(record.Irn);

        Assert.False(VoucherPrintProjector.ProjectInvoice(k.Company, v).StatesEInvoice);
    }

    /// <summary>An IRP rejection is not an e-invoice either — and it must not print a stale artefact from a previous
    /// attempt, because <c>RecordIrpResponse</c> is the only thing that can set one.</summary>
    [Fact]
    public void A_failed_submission_prints_nothing()
    {
        var k = NewKit("EInv Failed Co");
        var v = PostSale(k);
        var svc = new EInvoiceService(k.Company);
        var record = svc.PrepareRecord(v);
        svc.RecordFailure(record, "2150", "Duplicate IRN");
        Assert.Equal(EInvoiceStatus.Failed, record.Status);

        Assert.False(VoucherPrintProjector.ProjectInvoice(k.Company, v).StatesEInvoice);
    }

    /// <summary>ER-13 through the projector: a voucher with no e-invoice record at all projects blank fields and
    /// renders the same bytes as one whose company has no e-invoicing enabled.</summary>
    [Fact]
    public void A_voucher_with_no_record_renders_exactly_as_it_did_before_this_feature()
    {
        var k = NewKit("EInv None Co");
        var v = PostSale(k);
        Assert.Null(k.Company.FindEInvoiceRecordForVoucher(v.Id));

        var data = VoucherPrintProjector.ProjectInvoice(k.Company, v);
        Assert.False(data.StatesEInvoice);
        var withEInvoicingOn = InvoicePdf.Render(data, new PrintConfig(), new PageConfig());

        // The same book with e-invoicing switched OFF entirely must produce the identical file.
        k.Company.Gst!.EInvoicingEnabled = false;
        k.Company.Gst!.EInvoiceApplicableFrom = null;
        var withEInvoicingOff = InvoicePdf.Render(
            VoucherPrintProjector.ProjectInvoice(k.Company, v), new PrintConfig(), new PageConfig());

        Assert.Equal(withEInvoicingOn, withEInvoicingOff);
        Assert.DoesNotContain("/XObject", Encoding.Latin1.GetString(withEInvoicingOn), StringComparison.Ordinal);
    }

    // ================================================================ scaffolding

    private static byte[] ExtractImage(byte[] pdf)
    {
        var text = Encoding.Latin1.GetString(pdf);
        var m = Regex.Match(text, @"/Subtype /Image /Width \d+ /Height \d+ /ColorSpace /DeviceGray /BitsPerComponent 1 /Interpolate false /Length (\d+) >>\nstream\n");
        Assert.True(m.Success, "the rendered PDF carries no 1-bit image XObject");
        int len = int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        return pdf.Skip(m.Index + m.Length).Take(len).ToArray();
    }

    private static EInvoiceRecord Generate(Company c, Voucher v, string signedQr)
    {
        var svc = new EInvoiceService(c);
        Assert.Equal(EInvoiceCoverage.Covered, svc.CoverageOf(v));
        var record = svc.PrepareRecord(v);
        svc.RecordIrpResponse(record, Irn, AckNo, new DateOnly(2024, 4, 1), signedQr, Array.Empty<byte>());
        Assert.Equal(EInvoiceStatus.Generated, record.Status);
        return record;
    }

    private sealed class Kit
    {
        public required MainWindowViewModel Vm { get; init; }
        public required Guid TaxableItemId { get; init; }
        public required Guid MainGodownId { get; init; }
        public required Guid CustomerId { get; init; }
        public Company Company => Vm.Company!;
    }

    private sealed class ServiceKit
    {
        public required MainWindowViewModel Vm { get; init; }
        public required Guid ConsultancyId { get; init; }
        public required Guid CustomerId { get; init; }
        public Company Company => Vm.Company!;
    }

    private MainWindowViewModel NewEInvoiceCompany(string name)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = name;
        vm.CreateCompany();
        var c = vm.Company!;
        c.MailingName = "Acme Traders Pvt Ltd";
        c.Address = "12 Industrial Estate\nPune, Maharashtra 411001";
        c.FinancialYearStart = FyStart;
        c.BooksBeginFrom = FyStart;
        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = "27", Gstin = GstinMaharashtra, RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart, Periodicity = GstReturnPeriodicity.Monthly,
        });
        c.Gst!.EInvoicingEnabled = true;
        c.Gst!.EInvoiceApplicableFrom = FyStart;
        return vm;
    }

    private Kit NewKit(string name)
    {
        var vm = NewEInvoiceCompany(name);
        var c = vm.Company!;
        var inv = new InventoryService(c);
        var grp = inv.CreateStockGroup("Goods");
        var nos = inv.CreateSimpleUnit("Nos", "Numbers", unitQuantityCode: "NOS");
        var main = c.MainLocation!.Id;
        var taxable = inv.CreateStockItem("Widget", grp.Id, nos.Id);
        taxable.Gst = new StockItemGstDetails
        { HsnSac = "847130", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800 };
        inv.AddOpeningBalance(taxable.Id, main, 500m, Money.FromRupees(311.17m));

        AddLedger(c, "Sales", "Sales Accounts");
        var customer = AddLedger(c, "Local Customer", "Sundry Debtors");
        customer.PartyGst = new PartyGstDetails
        { RegistrationType = GstRegistrationType.Regular, Gstin = GstinMaharashtra, StateCode = "27" };
        _storage.Save(c);

        return new Kit { Vm = vm, TaxableItemId = taxable.Id, MainGodownId = main, CustomerId = customer.Id };
    }

    private ServiceKit NewServiceKit(string name)
    {
        var vm = NewEInvoiceCompany(name);
        var c = vm.Company!;
        var consultancy = AddLedger(c, "Consultancy Income", "Sales Accounts");
        consultancy.SalesPurchaseGst = new StockItemGstDetails
        {
            HsnSac = "998311", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800,
            SupplyType = GstSupplyType.Services,
        };
        var customer = AddLedger(c, "Local Customer", "Sundry Debtors");
        customer.PartyGst = new PartyGstDetails
        { RegistrationType = GstRegistrationType.Regular, Gstin = GstinMaharashtra, StateCode = "27" };
        _storage.Save(c);
        return new ServiceKit { Vm = vm, ConsultancyId = consultancy.Id, CustomerId = customer.Id };
    }

    private static DomainLedger AddLedger(Company c, string name, string groupName)
    {
        var group = c.FindGroupByName(groupName) ?? throw new InvalidOperationException($"No group '{groupName}'.");
        var ledger = new DomainLedger(Guid.NewGuid(), name, group.Id, Money.Zero, openingIsDebit: false);
        c.AddLedger(ledger);
        return ledger;
    }

    private static Voucher PostSale(Kit k)
    {
        k.Vm.OpenVoucher(VoucherBaseType.Sales);
        var entry = k.Vm.VoucherEntry!;
        entry.ToggleItemInvoice();
        entry.SelectedParty = entry.Parties.Single(p => p.Ledger?.Id == k.CustomerId);
        while (entry.InventoryLines.Count == 0) entry.AddInventoryLine();
        var line = entry.InventoryLines[0];
        line.SelectedItem = entry.StockItems.Single(i => i.Id == k.TaxableItemId);
        line.SelectedGodown = entry.Godowns.Single(g => g.Id == k.MainGodownId);
        line.QuantityText = "60.125";
        line.RateText = "786.64";
        Assert.True(entry.Accept(), entry.Message);
        return LastSale(k.Company);
    }

    private static Voucher PostServiceSale(ServiceKit k)
    {
        k.Vm.OpenVoucher(VoucherBaseType.Sales);
        var entry = k.Vm.VoucherEntry!;
        entry.ChangeMode();   // As Voucher   -> Item Invoice
        entry.ChangeMode();   // Item Invoice -> Accounting Invoice
        Assert.True(entry.IsAccountingInvoice);
        entry.SelectedParty = entry.Parties.Single(p => p.Ledger?.Id == k.CustomerId);
        while (entry.AccountingInvoiceLines.Count == 0) entry.AddAccountingInvoiceLine();
        var line = entry.AccountingInvoiceLines[0];
        line.SelectedLedger = entry.AccountingInvoiceLedgers.Single(l => l.Id == k.ConsultancyId);
        line.AmountText = "47296.73";
        Assert.True(entry.Accept(), entry.Message);
        return LastSale(k.Company);
    }

    private static Voucher LastSale(Company c)
    {
        var type = c.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Sales && t.IsActive);
        return c.Vouchers.Last(v => v.TypeId == type.Id);
    }

    private PrintPreviewViewModel PrintDrilled(MainWindowViewModel vm, Guid voucherId)
    {
        vm.OpenVoucherDetail(voucherId);
        vm.OpenPrintPreview();
        Assert.Equal(Screen.PrintPreview, vm.CurrentScreen);
        return vm.PrintPreview!;
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch { /* best-effort temp cleanup */ }
    }
}
