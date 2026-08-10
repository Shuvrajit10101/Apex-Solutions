using System;
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
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.Tests;

/// <summary>
/// <b>W0-1 (census T0-7) — Bill of Supply routing.</b> Until this slice landed, EVERY document this app printed for a
/// Sales item-invoice was titled "TAX INVOICE" and carried a GST breakup, whatever the supply actually was. Two
/// statutory limbs were therefore issued wrong:
/// <list type="number">
/// <item>a <b>composition dealer</b> may not issue a tax invoice at all — CGST Act §31(3)(c) requires a
/// <b>bill of supply</b> "instead of a tax invoice" from "a registered person … paying tax under the provisions of
/// section 10", and §10(4) forbids him to "collect any tax from the recipient on supplies made by him"; and</item>
/// <item>a <b>regular dealer's wholly exempt / nil-rated / non-GST</b> outward supply takes the same limb — §31(3)(c)
/// also names "a registered person supplying exempted goods or services or both", and §2(47) defines an exempt supply
/// as one which "attracts nil rate of tax or which may be wholly exempt from tax under section 11 … and includes
/// non-taxable supply".</item>
/// </list>
///
/// <para><b>A bill of supply carries NO tax breakup.</b> CGST Rule 49 prescribes exactly eight particulars —
/// (a) supplier name/address/GSTIN, (b) serial number, (c) date, (d) recipient name/address/GSTIN if registered,
/// (e) HSN, (f) description, (g) <i>value of supply</i> taking discount/abatement into account, and (h) signature.
/// It has no counterpart to Rule 46's clause (l) "rate of tax", clause (m) "amount of tax charged" or clause (n)
/// "place of supply … in the case of a supply in the course of inter-State trade". Showing a tax breakup on a bill of
/// supply would also state that a composition dealer collected tax, which §32(2) forbids.</para>
///
/// <para><b>The §10 declaration.</b> CGST Rule 5(1)(f) (composition rules): the composition taxable person "shall
/// mention the words <i>composition taxable person, not eligible to collect tax on supplies</i> at the <b>top</b> of
/// the bill of supply issued by him". It binds a composition dealer only — a regular dealer's exempt bill of supply
/// must NOT bear it (he is not a composition taxable person).</para>
///
/// <para>Sources (verbatim, official): CGST Act §31(3)(c), §32, §2(47), §10(4) —
/// <c>https://cbic-gst.gov.in/pdf/CGST-Act-Updated-30092020.pdf</c>; CGST Rules 46, 46A, 49 and composition Rule
/// 5(1)(f) — <c>https://cbic-gst.gov.in/pdf/01062021-CGST-Rules-2017-Part-A-Rules.pdf</c>.</para>
///
/// <para>Fixtures are deliberately odd-valued (60.125 Nos @ ₹786.64 = ₹47,296.73) — round numbers assert nothing.</para>
/// </summary>
public sealed class BillOfSupplyRoutingTests : IDisposable
{
    private const string GstinMaharashtra = "27AAPFU0939F1ZV";
    private const string GstinGujarat = "24AAACC1206D1ZM";
    private static readonly DateOnly FyStart = new(2024, 4, 1);

    // The one odd-valued supply every fixture bills: 60.125 Nos @ ₹786.64 = ₹47,296.73 exactly.
    private const decimal Qty = 60.125m;
    private const string RateText = "786.64";
    private static readonly Money SupplyValue = Money.FromRupees(47_296.73m);

    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public BillOfSupplyRoutingTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexBillOfSupplyTests_" + Guid.NewGuid().ToString("N"));
        _storage = new CompanyStorage(_tempDir);
    }

    private static string AsLatin1(byte[] bytes) => Encoding.Latin1.GetString(bytes);

    // ---------------------------------------------------------------- scaffolding

    private sealed class Kit
    {
        public required MainWindowViewModel Vm { get; init; }
        public required Guid TaxableItemId { get; init; }   // 18%
        public required Guid ExemptItemId { get; init; }    // exempt (no GST)
        public required Guid MainGodownId { get; init; }
        public required Guid CustomerId { get; init; }      // in-state (27), B2B
        public Company Company => Vm.Company!;
    }

    private Kit NewKit(string companyName, GstRegistrationType registration)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = companyName;
        vm.CreateCompany();

        var c = vm.Company!;
        c.MailingName = "Acme Traders Pvt Ltd";
        c.Address = "12 Industrial Estate\nPune, Maharashtra 411001";
        c.FinancialYearStart = FyStart;
        c.BooksBeginFrom = FyStart;

        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = "27",
            Gstin = GstinMaharashtra,
            RegistrationType = registration,
            CompositionSubType = registration == GstRegistrationType.Composition ? CompositionSubType.Trader : null,
            ApplicableFrom = FyStart,
            Periodicity = GstReturnPeriodicity.Quarterly,
        });

        var inv = new InventoryService(c);
        var grp = inv.CreateStockGroup("Goods");
        var nos = inv.CreateSimpleUnit("Nos", "Numbers", unitQuantityCode: "NOS");
        var main = c.MainLocation!.Id;

        var taxable = inv.CreateStockItem("Widget", grp.Id, nos.Id);
        taxable.Gst = new StockItemGstDetails { HsnSac = "847130", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800 };
        var exempt = inv.CreateStockItem("Fresh Milk", grp.Id, nos.Id);
        exempt.Gst = new StockItemGstDetails { HsnSac = "040110", Taxability = GstTaxability.Exempt };

        inv.AddOpeningBalance(taxable.Id, main, 500m, Money.FromRupees(311.17m));
        inv.AddOpeningBalance(exempt.Id, main, 500m, Money.FromRupees(29.43m));

        AddLedger(c, "Sales");
        var customer = AddLedger(c, "Local Customer");
        customer.PartyGst = new PartyGstDetails
        {
            RegistrationType = GstRegistrationType.Regular, Gstin = GstinMaharashtra, StateCode = "27",
        };

        _storage.Save(c);

        return new Kit
        {
            Vm = vm,
            TaxableItemId = taxable.Id,
            ExemptItemId = exempt.Id,
            MainGodownId = main,
            CustomerId = customer.Id,
        };
    }

    private static DomainLedger AddLedger(Company c, string name)
    {
        var groupName = name == "Sales" ? "Sales Accounts" : "Sundry Debtors";
        var group = c.FindGroupByName(groupName) ?? throw new InvalidOperationException($"No group '{groupName}'.");
        var ledger = new DomainLedger(Guid.NewGuid(), name, group.Id, Money.Zero, openingIsDebit: false);
        c.AddLedger(ledger);
        return ledger;
    }

    private static void FillItemLine(VoucherEntryViewModel entry, Guid itemId, Guid godownId, decimal qty, string rate, int index = 0)
    {
        while (entry.InventoryLines.Count <= index) entry.AddInventoryLine();
        var line = entry.InventoryLines[index];
        line.SelectedItem = entry.StockItems.Single(i => i.Id == itemId);
        line.SelectedGodown = entry.Godowns.Single(g => g.Id == godownId);
        line.QuantityText = qty.ToString(System.Globalization.CultureInfo.InvariantCulture);
        line.RateText = rate;
    }

    /// <summary>Posts a Sales item-invoice through the real entry VM and returns the posted voucher.</summary>
    private static Voucher PostSaleInvoice(Kit k, Action<VoucherEntryViewModel> fill)
    {
        k.Vm.OpenVoucher(VoucherBaseType.Sales);
        var entry = k.Vm.VoucherEntry!;
        k.Vm.ToggleItemInvoice();
        entry.SelectedParty = entry.Parties.Single(p => p.Ledger?.Id == k.CustomerId);
        fill(entry);
        Assert.True(entry.Accept());

        var c = k.Vm.Company!;
        var type = c.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Sales && t.IsActive);
        return c.Vouchers.Last(v => v.TypeId == type.Id);
    }

    private PrintPreviewViewModel PrintDrilled(MainWindowViewModel vm, Guid voucherId)
    {
        vm.OpenVoucherDetail(voucherId);
        Assert.Equal(Screen.VoucherDetail, vm.CurrentScreen);
        vm.OpenPrintPreview();
        Assert.Equal(Screen.PrintPreview, vm.CurrentScreen);
        return vm.PrintPreview!;
    }

    // ================================================================ 1 — §10: a composition sale is a BILL OF SUPPLY

    /// <summary>
    /// <b>The active harm.</b> A composition dealer's item-invoice printed "TAX INVOICE" — a document CGST §31(3)(c)
    /// forbids him to issue. It must be a BILL OF SUPPLY bearing the Rule 5(1)(f) declaration.
    /// </summary>
    [Fact]
    public void A_composition_dealer_sale_prints_a_bill_of_supply_never_a_tax_invoice()
    {
        var k = NewKit("Comp BoS Co", GstRegistrationType.Composition);
        var v = PostSaleInvoice(k, e => FillItemLine(e, k.TaxableItemId, k.MainGodownId, Qty, RateText));

        // The projection itself carries the statutory title + the §10 declaration.
        var data = VoucherPrintProjector.ProjectInvoice(k.Company, v);
        Assert.True(data.IsBillOfSupply);
        Assert.Equal("BILL OF SUPPLY", data.DocumentTitle);
        Assert.Equal(GstReportSupport.BillOfSupplyDeclaration, data.TopDeclaration);

        // …and it reaches the rendered document, which is what the customer holds.
        var text = AsLatin1(PrintDrilled(k.Vm, v.Id).PdfBytes);
        Assert.StartsWith("%PDF-", text);
        Assert.Contains("BILL OF SUPPLY", text);
        Assert.DoesNotContain("TAX INVOICE", text);
        Assert.Contains("Composition taxable person, not eligible to collect tax on supplies", text);
        Assert.Contains("47,296.73", text);   // Rule 49(g): the value of supply
        Assert.DoesNotContain("tally", text, StringComparison.OrdinalIgnoreCase);
    }

    // ================================================================ 2 — Rule 49: a BoS carries NO tax breakup

    /// <summary>
    /// <b>The money defect hiding under the title defect.</b> The per-rate breakup rows were built with the STATIC
    /// <c>GstService.ComputeLineTax</c>, which is not composition-gated — so a composition dealer's document printed a
    /// "GST Breakup" table showing CGST/SGST that was never charged, never posted and not in its own Grand Total.
    /// Rule 49 prescribes no rate or tax particular at all, and §32(2) forbids a registered person collecting tax
    /// otherwise than as the Act allows.
    /// </summary>
    [Fact]
    public void A_bill_of_supply_carries_no_tax_breakup_and_its_grand_total_is_the_value_of_supply()
    {
        var k = NewKit("Comp NoTax Co", GstRegistrationType.Composition);
        var v = PostSaleInvoice(k, e => FillItemLine(e, k.TaxableItemId, k.MainGodownId, Qty, RateText));

        var data = VoucherPrintProjector.ProjectInvoice(k.Company, v);
        Assert.Empty(data.TaxRows);
        Assert.Equal(Money.Zero, data.TotalCgst);
        Assert.Equal(Money.Zero, data.TotalSgst);
        Assert.Equal(Money.Zero, data.TotalIgst);
        Assert.Equal(Money.Zero, data.TotalCess);
        Assert.Equal(SupplyValue, data.TotalTaxable);
        Assert.Equal(SupplyValue, data.GrandTotal);   // Rule 49(g): value of supply — nothing added

        // THE FOOTING INVARIANT this project keeps getting bitten by: the printed demand must equal the debt the GL
        // actually recorded. Before the fix the document showed a breakup totalling 8,513.41 of tax that was in no
        // ledger and in no total — so the page contradicted both the books and itself.
        var partyLeg = v.Lines.Single(l => l.LedgerId == k.CustomerId && l.Side == DrCr.Debit);
        Assert.Equal(partyLeg.Amount, data.GrandTotal);

        var text = AsLatin1(PrintDrilled(k.Vm, v.Id).PdfBytes);
        Assert.DoesNotContain("GST Breakup", text);
        Assert.DoesNotContain("CGST", text);
        Assert.DoesNotContain("SGST", text);
        Assert.DoesNotContain("IGST", text);
        // The 18% tax the ungated static would have invented on this value (8,513.41 split 4,256.70/4,256.71).
        Assert.DoesNotContain("4,256.7", text);
    }

    // ================================================================ 3 — §31(3)(c) exempt limb, REGULAR dealer

    /// <summary>
    /// A regular (non-composition) dealer supplying only exempt goods takes the SAME limb of §31(3)(c) — but he is not
    /// a composition taxable person, so Rule 5(1)(f)'s declaration must NOT appear on his document.
    /// </summary>
    [Fact]
    public void A_regular_dealer_wholly_exempt_supply_prints_a_bill_of_supply_without_the_section_10_declaration()
    {
        var k = NewKit("Reg Exempt Co", GstRegistrationType.Regular);
        var v = PostSaleInvoice(k, e => FillItemLine(e, k.ExemptItemId, k.MainGodownId, Qty, RateText));

        var data = VoucherPrintProjector.ProjectInvoice(k.Company, v);
        Assert.True(data.IsBillOfSupply);
        Assert.Equal("BILL OF SUPPLY", data.DocumentTitle);
        Assert.Equal(string.Empty, data.TopDeclaration);   // Rule 5(1)(f) binds a composition dealer only
        Assert.Empty(data.TaxRows);
        Assert.Equal(SupplyValue, data.GrandTotal);

        var text = AsLatin1(PrintDrilled(k.Vm, v.Id).PdfBytes);
        Assert.Contains("BILL OF SUPPLY", text);
        Assert.DoesNotContain("TAX INVOICE", text);
        Assert.DoesNotContain("Composition taxable person", text);
        Assert.DoesNotContain("GST Breakup", text);
    }

    // ================================================================ 4 — the ordinary taxable supply is UNCHANGED

    /// <summary>
    /// ER-13 at the statutory boundary: a regular dealer's ordinary taxable supply is still a TAX INVOICE and still
    /// carries its full Rule-46 breakup. This is the assertion that stops the fix over-reaching.
    /// </summary>
    [Fact]
    public void A_regular_taxable_supply_still_prints_a_tax_invoice_with_its_breakup_intact()
    {
        var k = NewKit("Reg Taxable Co", GstRegistrationType.Regular);
        var v = PostSaleInvoice(k, e => FillItemLine(e, k.TaxableItemId, k.MainGodownId, Qty, RateText));

        var engine = GstService.ComputeLineTax(SupplyValue, 1800, interState: false);
        var data = VoucherPrintProjector.ProjectInvoice(k.Company, v);
        Assert.False(data.IsBillOfSupply);
        Assert.Equal("TAX INVOICE", data.DocumentTitle);
        Assert.Equal(string.Empty, data.TopDeclaration);
        var row = Assert.Single(data.TaxRows);
        Assert.Equal("18%", row.RateLabel);
        Assert.Equal(engine.Cgst, row.Cgst);
        Assert.Equal(engine.Sgst, row.Sgst);
        Assert.Equal(engine.Cgst, data.TotalCgst);
        Assert.Equal(engine.Sgst, data.TotalSgst);
        Assert.Equal(new Money(SupplyValue.Amount + engine.Cgst.Amount + engine.Sgst.Amount), data.GrandTotal);

        var text = AsLatin1(PrintDrilled(k.Vm, v.Id).PdfBytes);
        Assert.Contains("TAX INVOICE", text);
        Assert.DoesNotContain("BILL OF SUPPLY", text);
        Assert.Contains("GST Breakup", text);
        Assert.Contains("CGST", text);
        Assert.Contains("47,296.73", text);
        Assert.Contains(engine.Cgst.Amount.ToString("#,##0.00", System.Globalization.CultureInfo.InvariantCulture), text);
    }

    // ================================================================ 5 — a MIXED supply stays a tax invoice

    /// <summary>
    /// Rule 46A permits a single "invoice-cum-bill of supply" for a mixed taxable + exempt supply, and only to an
    /// <b>unregistered</b> person ("where a registered person is supplying taxable as well as exempted goods or
    /// services or both to an unregistered person, a single invoice-cum-bill of supply <b>may</b> be issued"). It is
    /// permissive and out of this slice's scope, so a mixed supply keeps the TAX INVOICE it genuinely is for its taxed
    /// lines — the exempt line rides along at zero tax. Demoting it to a bill of supply would hide real posted tax.
    /// </summary>
    [Fact]
    public void A_mixed_taxable_and_exempt_supply_is_not_demoted_to_a_bill_of_supply()
    {
        var k = NewKit("Reg Mixed Co", GstRegistrationType.Regular);
        var v = PostSaleInvoice(k, e =>
        {
            FillItemLine(e, k.TaxableItemId, k.MainGodownId, Qty, RateText, index: 0);
            FillItemLine(e, k.ExemptItemId, k.MainGodownId, 12.5m, "63.44", index: 1);
        });

        var data = VoucherPrintProjector.ProjectInvoice(k.Company, v);
        Assert.False(data.IsBillOfSupply);
        Assert.Equal("TAX INVOICE", data.DocumentTitle);
        Assert.NotEmpty(data.TaxRows);
        // The exempt line's value (12.5 × 63.44 = 793.00) is in the taxable/goods total but bears no tax.
        Assert.Equal(new Money(SupplyValue.Amount + 793.00m), data.TotalTaxable);
    }

    // ================================================================ 6 — the on-screen badge follows the document

    /// <summary>The drilled voucher-detail badge and the printed document must state the same thing; before this slice
    /// the badge knew about the §10 limb only, so a regular dealer's exempt supply showed "Tax Invoice".</summary>
    [Fact]
    public void The_voucher_detail_badge_follows_the_statutory_document_kind()
    {
        var k = NewKit("Badge Exempt Co", GstRegistrationType.Regular);
        var v = PostSaleInvoice(k, e => FillItemLine(e, k.ExemptItemId, k.MainGodownId, Qty, RateText));

        var detail = new VoucherDetailViewModel(k.Company, v);
        Assert.True(detail.IsBillOfSupply);
        Assert.Equal("Bill of Supply", detail.DocumentLabel);
        Assert.Equal(string.Empty, detail.BillOfSupplyDeclaration);   // regular dealer — no Rule 5(1)(f) wording
    }

    // ================================================================ 7 — F12 may not re-title a bill of supply

    /// <summary>
    /// The F12 title override exists so an operator can print e.g. "PROFORMA INVOICE". It must not be able to re-title
    /// a bill of supply "TAX INVOICE" — that would reissue, through a print knob, exactly the illegal document
    /// §31(3)(c) forbids. The document kind is a consequence of the supply, not a print preference.
    /// </summary>
    [Fact]
    public void The_F12_title_override_cannot_turn_a_bill_of_supply_into_a_tax_invoice()
    {
        var k = NewKit("Override BoS Co", GstRegistrationType.Composition);
        var v = PostSaleInvoice(k, e => FillItemLine(e, k.TaxableItemId, k.MainGodownId, Qty, RateText));

        var preview = PrintDrilled(k.Vm, v.Id);
        preview.TitleOverride = "TAX INVOICE";

        var text = AsLatin1(preview.PdfBytes);
        Assert.Contains("BILL OF SUPPLY", text);
        Assert.DoesNotContain("TAX INVOICE", text);
        Assert.Equal("BILL OF SUPPLY", preview.Pages[0].Title);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch { /* best-effort cleanup */ }
    }
}
