using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Io;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Xunit;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.Tests;

/// <summary>
/// <b>T0-11 review C1 (L1-01) — a PURCHASE RECORD carrying an ADDITIONAL COST OF PURCHASE must state the debt the
/// general ledger recorded.</b>
///
/// <para><b>The defect, and it is a regression this chain introduced.</b> Before commit <c>96db1c0</c> a Purchase
/// item invoice printed through <c>ProjectVoucher</c>, whose loop over <c>voucher.Lines</c> showed the Freight
/// Inward debit. Slice S2 routed the same voucher to the invoice-shaped <c>ProjectInvoice</c>, whose money vocabulary
/// is closed — <c>GrandTotal == TotalTaxable + TotalTax + TotalCess + RoundOff</c>, with <c>TotalTaxable</c> read off
/// <c>voucher.InventoryLines</c> ALONE. The additional-cost legs are on neither side of that sum, so the record
/// printed a Grand Total short of the posted supplier credit by the whole cost, with the cost ledger nowhere on the
/// page. Measured through the shipped UI: 11,800.00 printed against 13,034.56 posted.</para>
///
/// <para><b>🔴 WHERE EVERY EXPECTED VALUE BELOW COMES FROM.</b> The fixture's own arithmetic, stated once as
/// constants and derived by hand — never read back from the projector, the renderer or the entry screen. RQ-11a's
/// ER-4 ("every figure SHALL tie to the posted voucher to the paisa") is the rule the literals encode. Money is odd
/// to the paisa throughout: a round figure passes under a rounding defect and asserts nothing.</para>
///
/// <para>Everything here is driven through the SHIPPED UI path — <c>CreateCompany</c> → <c>EnableGst</c> →
/// <c>OpenVoucher(Purchase)</c> → <c>ToggleItemInvoice</c> → <c>TrackAdditionalCosts</c> → item line → additional
/// cost → <c>Accept</c> — and printed with <c>VoucherDetailViewModel.BuildPrintPreview()</c>, the same call
/// <c>P</c> / <c>Ctrl+P</c> makes. A hand-built voucher would prove nothing about reachability.</para>
/// </summary>
public sealed class PurchaseRecordAdditionalCostPrintTests : IDisposable
{
    private const string OurGstin = "27AAPFU0939F1ZV";          // Maharashtra (27) — the company
    private const string SupplierGstin = "27AAACC1206D1Z9";     // Maharashtra (27) — an INTRA-state supplier
    private static readonly DateOnly FyStart = new(2025, 4, 1);

    // ---------------------------------------------------------------- the fixture arithmetic, stated once
    //
    // One stock line: 10 Nos @ Rs.1,000.00. GST is charged on the GOODS only (the accept path computes it from the
    // item lines), intra-state at 18% => 9% CGST + 9% SGST. The additional cost rides the supplier's bill on top.
    private const decimal Qty = 10m;
    private const decimal Rate = 1_000.00m;
    private const decimal Goods = Qty * Rate;              // 10,000.00
    private const decimal Freight = 1_234.56m;             // the additional cost of purchase
    private const decimal Cgst = 900.00m;                  // 10,000.00 x 9%
    private const decimal Sgst = 900.00m;                  // 10,000.00 x 9%
    /// <summary>What we owe the supplier — the posted CREDIT leg on his account.
    /// 10,000.00 + 1,234.56 + 900.00 + 900.00.</summary>
    private const decimal SupplierLeg = Goods + Freight + Cgst + Sgst;   // 13,034.56
    /// <summary>What the record printed before the fix: goods + tax, with no term for the cost.</summary>
    private const decimal ShortGrandTotal = Goods + Cgst + Sgst;         // 11,800.00

    private const string FreightLedgerName = "Freight Inward";

    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public PurchaseRecordAdditionalCostPrintTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexRecordAddlCost_" + Guid.NewGuid().ToString("N"));
        _storage = new CompanyStorage(_tempDir);
    }

    // ---------------------------------------------------------------- scaffolding

    private sealed class Kit
    {
        public required MainWindowViewModel Vm { get; init; }
        public required Guid WidgetId { get; init; }
        public required Guid GodownId { get; init; }
        public required Guid SupplierId { get; init; }
        public required Guid PurchasesId { get; init; }
        public required Guid FreightId { get; init; }
        public Company Company => Vm.Company!;
    }

    private static DomainLedger Add(Company c, string name, string groupName, bool openingIsDebit,
        MethodOfAppropriation? method = null)
    {
        var group = c.FindGroupByName(groupName) ?? throw new InvalidOperationException($"No group '{groupName}'.");
        var l = new DomainLedger(Guid.NewGuid(), name, group.Id, Money.Zero, openingIsDebit,
            methodOfAppropriation: method);
        c.AddLedger(l);
        return l;
    }

    private Kit NewKit(string companyName)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = companyName;
        vm.CreateCompany();
        Assert.Equal(Screen.Gateway, vm.CurrentScreen);

        var c = vm.Company!;
        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = "27",
            Gstin = OurGstin,
            RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart,
            Periodicity = GstReturnPeriodicity.Monthly,
        });

        var inv = new InventoryService(c);
        var grp = inv.CreateStockGroup("Goods");
        var nos = inv.CreateSimpleUnit("Nos", "Numbers", unitQuantityCode: "NOS");
        var widget = inv.CreateStockItem("Widget", grp.Id, nos.Id);
        widget.Gst = new StockItemGstDetails
        { HsnSac = "847130", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800 };

        var purchases = Add(c, "Purchases", "Purchase Accounts", openingIsDebit: true);
        // RQ-16/RQ-19: an additional-cost ledger is a Direct-Expenses ledger carrying a Method of Appropriation.
        var freight = Add(c, FreightLedgerName, "Direct Expenses", openingIsDebit: true,
            method: MethodOfAppropriation.ByValue);

        var supplier = Add(c, "Local Supplier", "Sundry Creditors", openingIsDebit: false);
        supplier.PartyGst = new PartyGstDetails
        { RegistrationType = GstRegistrationType.Regular, Gstin = SupplierGstin, StateCode = "27" };
        supplier.Mailing = new PartyMailingDetails { Address = "9 Fort Street\nMumbai", Pincode = "400001" };

        _storage.Save(c);

        return new Kit
        {
            Vm = vm,
            WidgetId = widget.Id,
            GodownId = c.MainLocation!.Id,
            SupplierId = supplier.Id,
            PurchasesId = purchases.Id,
            FreightId = freight.Id,
        };
    }

    /// <summary>Posts the fixture's purchase through the real entry screen and returns the POSTED voucher.</summary>
    private static Voucher PostTrackedPurchase(Kit k, decimal freight = Freight)
    {
        k.Vm.OpenVoucher(VoucherBaseType.Purchase);
        var entry = k.Vm.VoucherEntry!;
        k.Vm.ToggleItemInvoice();
        entry.TrackAdditionalCosts = true;
        Assert.True(entry.ShowAdditionalCosts);
        entry.SelectedParty = entry.Parties.Single(p => p.Ledger?.Id == k.SupplierId);

        var line = entry.InventoryLines[0];
        line.SelectedItem = entry.StockItems.Single(i => i.Id == k.WidgetId);
        line.SelectedGodown = entry.Godowns.Single(g => g.Id == k.GodownId);
        line.QuantityText = Qty.ToString(CultureInfo.InvariantCulture);
        line.RateText = Rate.ToString("0.00", CultureInfo.InvariantCulture);

        entry.AdditionalCosts[0].SelectedLedger = entry.AdditionalCostLedgers.Single(l => l.Id == k.FreightId);
        entry.AdditionalCosts[0].AmountText = freight.ToString("0.00", CultureInfo.InvariantCulture);

        Assert.True(entry.Accept(), entry.Message);

        var c = k.Company;
        var type = c.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Purchase && t.IsActive);
        return c.Vouchers.Last(v => v.TypeId == type.Id);
    }

    /// <summary>The rendered PDF as text. Latin-1, the same decode every other print test here uses — the content
    /// streams are ASCII-safe by construction (<c>ReportPrintProjector.Ascii</c>).</summary>
    private static string PdfText(byte[] bytes) => Encoding.Latin1.GetString(bytes);

    private static decimal PostedLeg(Voucher v, Guid ledgerId, DrCr side) =>
        v.Lines.Where(l => l.LedgerId == ledgerId && l.Side == side).Sum(l => l.Amount.Amount);

    // ================================================================ the blocker

    /// <summary>
    /// <b>ER-4, on the one document class whose whole purpose is to verify what we owe a supplier.</b> The posted
    /// supplier credit is 13,034.56; the record must state 13,034.56, and must NAME the 1,234.56 it is stating.
    /// </summary>
    [Fact]
    public void A_purchase_record_carrying_an_additional_cost_foots_to_the_posted_supplier_credit()
    {
        var k = NewKit("Addl Cost Record Co");
        var posted = PostTrackedPurchase(k);

        // ---- the POSTED legs, against the hand-derived literals (nothing here is read off the print path) ----
        Assert.Equal(Goods, PostedLeg(posted, k.PurchasesId, DrCr.Debit));
        Assert.Equal(Freight, PostedLeg(posted, k.FreightId, DrCr.Debit));
        Assert.Equal(SupplierLeg, PostedLeg(posted, k.SupplierId, DrCr.Credit));
        Assert.Equal(13_034.56m, PostedLeg(posted, k.SupplierId, DrCr.Credit));

        // ---- the document, at the real user path (P / Ctrl+P) ----
        var preview = new VoucherDetailViewModel(k.Company, posted).BuildPrintPreview();
        Assert.Equal(PrintPreviewViewModel.PrintKind.Invoice, preview.Kind);

        var data = VoucherPrintProjector.ProjectInvoice(k.Company, posted);
        Assert.True(data.IsRecipientRecord);
        Assert.Equal(Goods, data.TotalTaxable.Amount);
        Assert.Equal(Cgst, data.TotalCgst.Amount);
        Assert.Equal(Sgst, data.TotalSgst.Amount);

        // 🔴 THE DEFECT: 11,800.00 was printed against 13,034.56 posted.
        Assert.NotEqual(ShortGrandTotal, data.GrandTotal.Amount);
        Assert.Equal(SupplierLeg, data.GrandTotal.Amount);
        Assert.Equal(13_034.56m, data.GrandTotal.Amount);

        // ---- and the cost is NAMED on the page, not silently folded into a bigger number ----
        var text = PdfText(preview.PdfBytes);
        Assert.Contains(FreightLedgerName, text);
        Assert.Contains("1,234.56", text);
        Assert.Contains("13,034.56", text);
        Assert.Contains("Rupees Thirteen Thousand Thirty Four and Fifty Six Paise Only", text);
        Assert.DoesNotContain("Eleven Thousand Eight Hundred", text);
    }

    /// <summary>
    /// FIX-W1e: the approval pane and the bytes must state the same money. The mirror carried no term for the cost
    /// either, so the operator approved a screen whose visible rows summed to 11,800.00 under a Grand Total the same
    /// pane printed as 11,800.00 — internally consistent, and 1,234.56 short of the book.
    /// </summary>
    [Fact]
    public void The_approval_pane_states_the_additional_cost_the_bytes_state()
    {
        var k = NewKit("Addl Cost Mirror Co");
        var posted = PostTrackedPurchase(k);

        var preview = new VoucherDetailViewModel(k.Company, posted).BuildPrintPreview();
        var lines = preview.Pages.SelectMany(p => p.Lines).SelectMany(l => l.Cells).ToList();

        Assert.Contains(FreightLedgerName, lines);
        Assert.Contains(IndianFormat.AmountAlways(Freight), lines);
        Assert.Contains(IndianFormat.AmountAlways(SupplierLeg), lines);
        Assert.DoesNotContain(IndianFormat.AmountAlways(ShortGrandTotal), lines);
    }

    /// <summary>
    /// The guard the class needed, stated as a property rather than as a case: whatever the voucher posts, the
    /// printed Grand Total equals the posted party leg to the paisa. Swept over several odd-to-the-paisa costs so
    /// the assertion cannot pass on one lucky figure.
    /// </summary>
    [Theory]
    [InlineData("0.01")]
    [InlineData("7.77")]
    [InlineData("1234.56")]
    [InlineData("99999.99")]
    public void Whatever_the_additional_cost_the_record_foots_to_the_posted_party_leg(string cost)
    {
        var amount = decimal.Parse(cost, CultureInfo.InvariantCulture);
        var k = NewKit("Addl Cost Sweep " + cost);
        var posted = PostTrackedPurchase(k, amount);

        var expected = Goods + amount + Cgst + Sgst;
        Assert.Equal(expected, PostedLeg(posted, k.SupplierId, DrCr.Credit));
        Assert.Equal(expected, VoucherPrintProjector.ProjectInvoice(k.Company, posted).GrandTotal.Amount);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch (IOException) { /* a locked temp file must not fail the run */ }
    }
}
