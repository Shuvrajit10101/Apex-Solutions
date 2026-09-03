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
        /// <summary>A stock item carrying NO GST block at all, so rate resolution falls through to the value ledger
        /// (and, when that has none either, to the unresolved sentinel). Needed by the reverse-charge and
        /// "silence is not an exemption" locks below.</summary>
        public required Guid PlainItemId { get; init; }
        public required Guid MainGodownId { get; init; }
        public required Guid CustomerId { get; init; }      // in-state (27), B2B
        public required Guid SalesLedgerId { get; init; }
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
        // No GST sub-form was ever opened on this one — item.Gst stays null, so ResolveBase falls to the ledger.
        var plain = inv.CreateStockItem("Unclassified Part", grp.Id, nos.Id);

        inv.AddOpeningBalance(taxable.Id, main, 500m, Money.FromRupees(311.17m));
        inv.AddOpeningBalance(exempt.Id, main, 500m, Money.FromRupees(29.43m));
        inv.AddOpeningBalance(plain.Id, main, 500m, Money.FromRupees(103.61m));

        var salesLedger = AddLedger(c, "Sales");
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
            PlainItemId = plain.Id,
            MainGodownId = main,
            CustomerId = customer.Id,
            SalesLedgerId = salesLedger.Id,
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

    // ---------------------------------------------------------------- service (Accounting Invoice) scaffolding

    /// <summary>A GST-enabled Regular company with the two ledger-only SERVICE shapes the §31(3)(c) exempt limb has to
    /// tell apart: a wholly EXEMPT SAC ledger (a bill of supply) and a ZERO-RATED 0%/LUT one (still a tax invoice —
    /// zero-rated is taxable). Kept separate from <see cref="NewKit"/> so the item fixtures' sales-ledger resolution
    /// is untouched.</summary>
    private sealed class ServiceKit
    {
        public required MainWindowViewModel Vm { get; init; }
        public required Guid ExemptServiceId { get; init; }
        public required Guid ZeroRatedServiceId { get; init; }
        public required Guid CustomerId { get; init; }
        public Company Company => Vm.Company!;
    }

    private ServiceKit NewServiceKit(string companyName)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = companyName;
        vm.CreateCompany();

        var c = vm.Company!;
        c.FinancialYearStart = FyStart;
        c.BooksBeginFrom = FyStart;

        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = "27", Gstin = GstinMaharashtra, RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart, Periodicity = GstReturnPeriodicity.Monthly,
        });

        var exemptService = AddLedger(c, "Exempt Service", "Sales Accounts");
        exemptService.SalesPurchaseGst = new StockItemGstDetails
        {
            HsnSac = "999999", Taxability = GstTaxability.Exempt, SupplyType = GstSupplyType.Services,
        };
        var zeroRated = AddLedger(c, "Export Service (LUT)", "Sales Accounts");
        zeroRated.SalesPurchaseGst = new StockItemGstDetails
        {
            HsnSac = "998313", Taxability = GstTaxability.Taxable, RateBasisPoints = 0,
            SupplyType = GstSupplyType.Services,
        };

        var customer = AddLedger(c, "Local Customer", "Sundry Debtors");
        customer.PartyGst = new PartyGstDetails
        {
            RegistrationType = GstRegistrationType.Regular, Gstin = GstinMaharashtra, StateCode = "27",
        };

        _storage.Save(c);
        return new ServiceKit
        {
            Vm = vm,
            ExemptServiceId = exemptService.Id,
            ZeroRatedServiceId = zeroRated.Id,
            CustomerId = customer.Id,
        };
    }

    /// <summary>Posts a ledger-only SERVICE sale through the real Accounting Invoice entry mode.</summary>
    private static Voucher PostServiceSale(ServiceKit k, Guid serviceLedgerId, string amount)
    {
        k.Vm.OpenVoucher(VoucherBaseType.Sales);
        var entry = k.Vm.VoucherEntry!;
        entry.ChangeMode();   // As Voucher   -> Item Invoice
        entry.ChangeMode();   // Item Invoice -> Accounting Invoice
        Assert.True(entry.IsAccountingInvoice);
        entry.SelectedParty = entry.Parties.Single(p => p.Ledger?.Id == k.CustomerId);
        while (entry.AccountingInvoiceLines.Count == 0) entry.AddAccountingInvoiceLine();
        var line = entry.AccountingInvoiceLines[0];
        line.SelectedLedger = entry.AccountingInvoiceLedgers.Single(l => l.Id == serviceLedgerId);
        line.AmountText = amount;
        Assert.True(entry.Accept());

        var c = k.Vm.Company!;
        var type = c.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Sales && t.IsActive);
        return c.Vouchers.Last(v => v.TypeId == type.Id);
    }

    private static DomainLedger AddLedger(Company c, string name, string groupName)
    {
        var group = c.FindGroupByName(groupName) ?? throw new InvalidOperationException($"No group '{groupName}'.");
        var ledger = new DomainLedger(Guid.NewGuid(), name, group.Id, Money.Zero, openingIsDebit: false);
        c.AddLedger(ledger);
        return ledger;
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
    ///
    /// <para><b>FIX-W1i — this test used to assert a figure that CANNOT MOVE.</b> Its only money assertion was
    /// <c>TotalTaxable == 47,296.73 + 793.00</c>, and <c>TotalTaxable</c> is Σ of EVERY line value, rated and exempt
    /// alike (VoucherPrintProjector.cs:488) — so it is identical whether the exempt line is taxed or not. Measured:
    /// mutating the projector to accumulate a non-taxable line into <c>taxableByRate</c> at 1800bp — i.e. charging
    /// 18% GST on exempt milk, raising the customer's demand above the posted party leg — left ALL SEVEN tests in
    /// this file green. The exempt line was also 12.5 × 63.44 = ₹793.00 exactly, a whole rupee, in a file whose own
    /// header says round numbers assert nothing. It now bills 4.25 × ₹63.48 = ₹269.79 and asserts the COMPONENTS:
    /// the single rate row's own taxable value (which excludes the exempt line), the head splits, and the Grand
    /// Total against the debt actually posted to the party.</para>
    /// </summary>
    [Fact]
    public void A_mixed_taxable_and_exempt_supply_is_not_demoted_to_a_bill_of_supply()
    {
        var k = NewKit("Reg Mixed Co", GstRegistrationType.Regular);
        var v = PostSaleInvoice(k, e =>
        {
            FillItemLine(e, k.TaxableItemId, k.MainGodownId, Qty, RateText, index: 0);
            FillItemLine(e, k.ExemptItemId, k.MainGodownId, 4.25m, "63.48", index: 1);   // = 269.79, odd paisa
        });

        var data = VoucherPrintProjector.ProjectInvoice(k.Company, v);
        Assert.False(data.IsBillOfSupply);
        Assert.Equal("TAX INVOICE", data.DocumentTitle);
        // The exempt line's value is in the goods total but must NOT be in the rate row and must bear no tax.
        Assert.Equal(new Money(SupplyValue.Amount + 269.79m), data.TotalTaxable);

        var engine = GstService.ComputeLineTax(SupplyValue, 1800, interState: false);
        var row = Assert.Single(data.TaxRows);
        Assert.Equal("18%", row.RateLabel);
        Assert.Equal(SupplyValue, row.TaxableValue);      // 47,296.73 — NOT the combined 47,566.52
        Assert.Equal(engine.Cgst, row.Cgst);
        Assert.Equal(engine.Sgst, row.Sgst);
        Assert.Equal(engine.Cgst, data.TotalCgst);
        Assert.Equal(engine.Sgst, data.TotalSgst);

        // The printed demand equals the debt the GL recorded — the invariant that catches a redistributed exempt line.
        var partyLeg = v.Lines.Single(l => l.LedgerId == k.CustomerId && l.Side == DrCr.Debit);
        Assert.Equal(partyLeg.Amount, data.GrandTotal);
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

    // ================================================================ 8 — FIX-W1b: outward RCM is a TAX INVOICE

    /// <summary>
    /// <b>FIX-W1b (HIGH, regression introduced by W0-1).</b> An outward <b>reverse-charge</b> supply bears no forward
    /// tax, and the exempt limb classified purely by declared taxability — so W0-1 titled it BILL OF SUPPLY. It is a
    /// TAXABLE supply: CGST §2(98) defines reverse charge as "the liability to pay tax by the recipient of supply of
    /// goods or services or both <b>instead of the supplier</b>", so the tax is due and merely moves. §31(3)(c)
    /// reserves the bill of supply for an exempted supply or a §10 dealer, and he is neither. On HEAD this printed
    /// TAX INVOICE — the right document kind — so W0-1 regressed it.
    ///
    /// <para>The shape is the app's own and the only reachable one (Gstr1.cs:246, "zero forward tax; sales ledger
    /// flagged ReverseChargeApplicable"): <c>ComputeItemInvoiceGst</c> does not consult
    /// <c>ReverseChargeApplicable</c> (VoucherEntryViewModel.cs:3657 skips on <c>!res.IsTaxable</c>), so a Taxable
    /// ledger would post forward CGST/SGST the supplier must not charge, and an unclassified one is refused at Accept.
    /// </para>
    ///
    /// <para><b>Both ends are asserted in one test</b> so the paper and the return can never diverge again: the app's
    /// own GSTR-1 files this very voucher in Table 4B as a taxable reverse-charge outward supply, and deliberately
    /// keeps it OUT of the exempt bucket (Gstr1.cs:249). A document titled BILL OF SUPPLY would assert the exact
    /// opposite of the return filed for it.</para>
    /// </summary>
    [Fact]
    public void An_outward_reverse_charge_supply_stays_a_tax_invoice_and_matches_its_own_GSTR1_table_4B()
    {
        var k = NewKit("Rcm Outward Co", GstRegistrationType.Regular);
        var sales = k.Company.FindLedger(k.SalesLedgerId)!;
        sales.SalesPurchaseGst = new StockItemGstDetails
        {
            HsnSac = "996511", Taxability = GstTaxability.NilRated, ReverseChargeApplicable = true,
        };

        var v = PostSaleInvoice(k, e => FillItemLine(e, k.PlainItemId, k.MainGodownId, Qty, RateText));
        Assert.All(v.Lines, l => Assert.Null(l.Gst));                      // no forward tax, as an RCM supply must be
        Assert.True(GstReportSupport.IsOutwardReverseChargeSupply(k.Company, v));

        var data = VoucherPrintProjector.ProjectInvoice(k.Company, v);
        Assert.False(data.IsBillOfSupply);
        Assert.Equal("TAX INVOICE", data.DocumentTitle);
        Assert.Equal(string.Empty, data.TopDeclaration);
        Assert.Equal("Tax Invoice", new VoucherDetailViewModel(k.Company, v).DocumentLabel);

        var text = AsLatin1(PrintDrilled(k.Vm, v.Id).PdfBytes);
        Assert.Contains("TAX INVOICE", text);
        Assert.DoesNotContain("BILL OF SUPPLY", text);

        // The return files the SAME ₹47,296.73 as a TAXABLE reverse-charge outward supply.
        var g1 = Gstr1.Build(k.Company, FyStart, FyStart.AddYears(1).AddDays(-1));
        Assert.Equal(SupplyValue, g1.Rcm4BOutwardValue);
    }

    // ================================================================ 9 — FIX-W1a: §31(3)(c) binds a REGISTERED person

    /// <summary>
    /// <b>FIX-W1a.</b> CGST §31(3)(c) binds "<b>a registered person</b>". The §10 limb has always carried an
    /// <c>Enabled: true</c> gate (GstReportSupport.cs:139, whose doc comment records the gate as load-bearing and
    /// names this exact scenario); W0-1's exempt limb shipped with no GST-status check at all. So a company that
    /// enabled GST, classified an item Exempt, posted the sale and then switched GST OFF — the F11 disable branch
    /// clears <c>Enabled</c> but RETAINS the config and every master — badged the drilled voucher "Bill of Supply"
    /// and titled the document BILL OF SUPPLY, naming a GST statutory document for a business that is not registered
    /// under GST and whose whole GST menu is hidden.
    /// </summary>
    [Fact]
    public void Switching_GST_off_stops_the_exempt_supply_being_called_a_bill_of_supply()
    {
        var k = NewKit("DeGst Exempt Co", GstRegistrationType.Regular);
        var v = PostSaleInvoice(k, e => FillItemLine(e, k.ExemptItemId, k.MainGodownId, Qty, RateText));

        // Sanity: while GST is ON it IS a bill of supply (the behaviour W0-1 correctly added).
        Assert.True(VoucherPrintProjector.IsBillOfSupply(k.Company, v));

        // The F11 disable branch: Enabled goes false, the config and the masters survive (GstConfigViewModel.cs:1298).
        k.Company.Gst!.Enabled = false;

        Assert.False(VoucherPrintProjector.IsBillOfSupply(k.Company, v));
        var detail = new VoucherDetailViewModel(k.Company, v);
        Assert.False(detail.IsBillOfSupply);
        Assert.NotEqual("Bill of Supply", detail.DocumentLabel);
        Assert.Equal(string.Empty, detail.BillOfSupplyDeclaration);
    }

    // ================================================================ 10 — FIX-W1c: the §10 contradiction prints neither

    /// <summary>
    /// <b>FIX-W1c.</b> The posted-forward-tax gate sat ABOVE the §10 limb, so posted data could override a composition
    /// dealer's statutory document kind: a §10 voucher carrying forward <c>GstLineTax</c> legs fell straight through
    /// to <c>DocumentTitle = "TAX INVOICE"</c> with the full Rule-46 breakup — the precise document this slice exists
    /// to stop, and weaker than HEAD, whose badge at least said "Bill of Supply".
    ///
    /// <para>Picking either paper launders a contradiction (§31(3)(c) makes it a bill of supply unconditionally;
    /// §10(4) says he may collect no tax, so a bill of supply's Grand Total would fall short of the posted party leg).
    /// The voucher is therefore not projected as an invoice at all and prints as the plain Dr/Cr voucher, which states
    /// the posted legs exactly — the same structural refusal <c>ServiceInvoiceFoots</c> (F2) already applies.</para>
    ///
    /// <para><b>Reachable through the shipped UI, no import needed</b> — which also makes this the fix for the MONEY
    /// half of the same scenario. Post a taxed sale as a Regular dealer, then change Registration Type to Composition
    /// in the F11 GST config (idempotent, and it checks no existing voucher). Before this fix, reprinting that sale
    /// produced an invoice titled TAX INVOICE whose Grand Total was ₹47,296.73 against a posted party debit of
    /// ₹55,810.14 — because <c>ComputeInvoiceTax</c> short-circuits to zero for composition (GstService.cs:619) while
    /// the item path reads its head totals from that live computation. Routing the voucher to the plain Dr/Cr print
    /// removes the understated document entirely.</para>
    /// </summary>
    [Fact]
    public void A_composition_voucher_carrying_posted_forward_tax_prints_neither_document()
    {
        var k = NewKit("Comp Contradiction Co", GstRegistrationType.Regular);
        var c = k.Company;
        var v = PostSaleInvoice(k, e => FillItemLine(e, k.TaxableItemId, k.MainGodownId, Qty, RateText));
        var partyLeg = v.Lines.Single(l => l.LedgerId == k.CustomerId && l.Side == DrCr.Debit).Amount;
        Assert.Equal(Money.FromRupees(55_810.14m), partyLeg);        // 47,296.73 + 4,256.71 + 4,256.70

        // The operator switches the registration type on the EXISTING books.
        c.Gst!.RegistrationType = GstRegistrationType.Composition;
        c.Gst!.CompositionSubType = CompositionSubType.Trader;

        Assert.True(GstReportSupport.IsCompositionBillOfSupply(c, v)); // he IS now a §10 dealer
        Assert.True(GstReportSupport.HasForwardTaxLines(v));       // …and the GL nonetheless recorded forward tax

        // Neither statutory document may be issued for it.
        Assert.False(VoucherPrintProjector.IsBillOfSupply(c, v));
        Assert.False(VoucherPrintProjector.IsTaxInvoice(c, v));

        var detail = new VoucherDetailViewModel(c, v);
        Assert.Equal(string.Empty, detail.DocumentLabel);
        Assert.Equal(string.Empty, detail.BillOfSupplyDeclaration);

        // It prints the plain Dr/Cr voucher, which states the posted debt in full — nothing is understated.
        var preview = detail.BuildPrintPreview();
        Assert.Equal(PrintPreviewViewModel.PrintKind.Voucher, preview.Kind);
        var text = AsLatin1(preview.PdfBytes);
        Assert.DoesNotContain("TAX INVOICE", text);
        Assert.DoesNotContain("BILL OF SUPPLY", text);
        Assert.Contains("55,810.14", text);
    }

    // ================================================================ 11 — FIX-W1j: the forward-tax gate itself

    /// <summary>
    /// <b>FIX-W1j — the gate that was the slice's only protection against an understated Grand Total, and had no
    /// test.</b> Measured: deleting the forward-tax gate (then written inline as
    /// <c>if (HasForwardTaxLines || HasPostedForwardCess) return false;</c>, now
    /// <c>if (CarriesForwardTax(company, voucher)) return false;</c> after W0-1's follow-up widened it to the ledger
    /// side and W0-9 moved the whole rule into <c>GstReportSupport</c>) from <c>IsBillOfSupply</c> left 31 tests green,
    /// because every fixture in the slice either posted no tax (so the gate never fires) or failed both limbs anyway
    /// (so its result is irrelevant).
    ///
    /// <para>This exercises it on the limb where nothing else can mask it: a REGULAR dealer whose lines are all
    /// exempt — limb 2 says "bill of supply" — but whose voucher carries POSTED forward tax legs. With the gate the
    /// document keeps its TAX INVOICE title; without it the document is retitled BILL OF SUPPLY and its heads and
    /// breakup are stripped.</para>
    ///
    /// <para><b>🔴 W0-10 — THE ₹8,513.41 SHORTFALL THIS TEST USED TO PIN IS NOW CLOSED, AND THE ASSERTION IS INVERTED
    /// ON PURPOSE.</b> Until W0-10 the lines below read <c>Assert.Equal(SupplyValue, data.GrandTotal)</c> and
    /// <c>Assert.Equal(8_513.41m, postedPartyLeg.Amount - data.GrandTotal.Amount)</c> — they asserted the DEFECT to the
    /// paisa rather than leaving it silent, with a note saying that the day it was fixed this test would fail and say
    /// so. That day is this slice, and this is it saying so: <b>the change below is deliberate, not a regression.</b>
    /// The cause was that <c>ProjectInvoice</c>'s ITEM pass took its head totals from a LIVE <c>ComputeInvoiceTax</c>
    /// over the lines it could classify, while the SERVICE pass read the POSTED legs — one projector, two sources of
    /// truth for money. A wholly exempt item feeds the live computation nothing, so this voucher's ₹8,513.41 of POSTED
    /// forward tax vanished and the document demanded ₹47,296.73 against a posted party debit of ₹55,810.14. The item
    /// pass now reads <c>GstReportSupport.ReadPostedRateGroups</c> / <c>PostedForwardRouting</c> /
    /// <c>PostedCessTotal</c> — the same reads the service pass has always made — so the figure is the tax the general
    /// ledger actually carries. Measured on this exact fixture: the Grand Total moved 47,296.73 ⇒ 55,810.14 and the
    /// shortfall is 0.00.</para>
    ///
    /// <para><b>What it locks now is the positive invariant: the printed Grand Total IS the posted party leg.</b> That
    /// is strictly stronger than the old pin — the old one would still have passed if the projector had drifted to any
    /// other wrong figure, so long as the gap stayed 8,513.41. Read with the sibling
    /// <c>ItemInvoicePostedTaxTests</c>, which drives the same rule from the four master edits that reach it through
    /// the shipped UI. The in-app-reachable §10 instance of the same arithmetic (Regular → Composition, then reprint)
    /// was closed earlier and separately, by FIX-W1c above.</para>
    /// </summary>
    [Fact]
    public void An_exempt_supply_that_posted_forward_tax_stays_a_tax_invoice()
    {
        var k = NewKit("Exempt WithTax Co", GstRegistrationType.Regular);
        var c = k.Company;
        var gst = new GstService(c);
        var salesType = c.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Sales && t.IsActive);
        var cgst = gst.FindTaxLedger(GstTaxHead.Central, GstTaxDirection.Output)!;
        var sgst = gst.FindTaxLedger(GstTaxHead.State, GstTaxDirection.Output)!;
        var partyDebit = Money.FromRupees(55_810.14m);   // 47,296.73 + 4,256.71 + 4,256.70

        var v = new Voucher(Guid.NewGuid(), salesType.Id, FyStart.AddDays(12), new[]
        {
            new EntryLine(k.CustomerId, partyDebit, DrCr.Debit),
            new EntryLine(k.SalesLedgerId, SupplyValue, DrCr.Credit),
            new EntryLine(cgst.Id, Money.FromRupees(4_256.71m), DrCr.Credit,
                gst: new GstLineTax(GstTaxHead.Central, 900, SupplyValue)),
            new EntryLine(sgst.Id, Money.FromRupees(4_256.70m), DrCr.Credit,
                gst: new GstLineTax(GstTaxHead.State, 900, SupplyValue)),
        }, partyId: k.CustomerId, inventoryLines: new[]
        {
            new VoucherInventoryLine(k.ExemptItemId, k.MainGodownId, Qty, Money.FromRupees(786.64m)),
        });
        new LedgerService(c).Post(v);

        // --- THE LOCK: the gate holds, so the document is not retitled and its breakup is not stripped. ---
        var data = VoucherPrintProjector.ProjectInvoice(c, v);
        Assert.False(data.IsBillOfSupply);
        Assert.Equal("TAX INVOICE", data.DocumentTitle);

        // --- W0-10: THE CURE, where the ₹8,513.41 shortfall used to be pinned. See the doc comment. ---
        // W0-1 follow-up review, finding #10: an earlier pass replaced one tautology with another. `LedgerService
        // .Post` stores the CALLER's own Voucher instance (`_company.AddVoucherInternal(voucher)` — there is no clone
        // anywhere in Post), so re-reading `v.Lines` returns the very object this fixture built four lines earlier:
        // `Assert.Equal(partyDebit, postedPartyLeg)` compared `partyDebit` with `partyDebit` and could not fail on any
        // input or any change to the ledger engine. It is gone. The load-bearing reconciliation is the invariant
        // below, which measures a PRODUCTION output (`data.GrandTotal`) against the debt the GL recorded — and the leg
        // is read back through a real save → load round trip, which makes the read genuinely independent of this
        // fixture's own object graph.
        _storage.Save(c);
        var reloaded = _storage.Load(_storage.ListCompanies().Single(e => e.Name == "Exempt WithTax Co"));
        var postedPartyLeg = reloaded.Vouchers
            .Single(x => x.Id == v.Id).Lines
            .Single(l => l.LedgerId == k.CustomerId && l.Side == DrCr.Debit).Amount;
        Assert.Equal(Money.FromRupees(55_810.14m), postedPartyLeg);

        // The document states the debt: Grand Total == the posted party leg, and the shortfall is gone, not smaller.
        Assert.Equal(postedPartyLeg, data.GrandTotal);
        Assert.Equal(0m, postedPartyLeg.Amount - data.GrandTotal.Amount);              // was 8,513.41 before W0-10
        // …because the printed tax is now the tax the LEGS carry, not a live recomputation over the (exempt) items.
        Assert.Equal(Money.FromRupees(4_256.71m), data.TotalCgst);
        Assert.Equal(Money.FromRupees(4_256.70m), data.TotalSgst);
        Assert.Equal(SupplyValue, data.TotalTaxable);                                  // the goods value, unchanged
        var pinRow = Assert.Single(data.TaxRows);
        Assert.Equal("18%", pinRow.RateLabel);
        Assert.Equal(SupplyValue, pinRow.TaxableValue);
    }

    // ================================================================ 12 — FIX-W1k: silence is not an exemption

    /// <summary>
    /// <b>FIX-W1k — the <c>|| GstService.IsUnresolved(res)</c> half of the exempt-limb guard had no test.</b>
    /// Measured: deleting it left 51 tests green, because every fixture declared an explicit taxability. The sentinel
    /// <c>ResolveBase</c> returns for a line with no GST master anywhere (GstService.cs:415) has
    /// <c>IsTaxable = false</c>, so without this clause an ordinary UNCLASSIFIED sale reads as wholly exempt: it
    /// would be retitled BILL OF SUPPLY, have its breakup stripped and lock out the F12 title override.
    ///
    /// <para>Reached the way a real book reaches it — the item's GST classification is cleared after the sale was
    /// posted, so the reprint resolves against nothing. (Accept refuses to POST an unresolved taxable line outright,
    /// VoucherEntryViewModel.cs:4692, so this is the reachable route.)</para>
    /// </summary>
    [Fact]
    public void An_unclassified_line_is_not_read_as_exempt_and_keeps_its_tax_invoice()
    {
        var k = NewKit("Unresolved Co", GstRegistrationType.Regular);
        var v = PostSaleInvoice(k, e => FillItemLine(e, k.ExemptItemId, k.MainGodownId, Qty, RateText));
        Assert.True(VoucherPrintProjector.IsBillOfSupply(k.Company, v));   // while it is declared Exempt

        // The classification is cleared on the master; the value ledger carries none either ⇒ unresolved sentinel.
        k.Company.FindStockItem(k.ExemptItemId)!.Gst = null;
        Assert.Null(k.Company.FindLedger(k.SalesLedgerId)!.SalesPurchaseGst);

        var data = VoucherPrintProjector.ProjectInvoice(k.Company, v);
        Assert.False(data.IsBillOfSupply);
        Assert.Equal("TAX INVOICE", data.DocumentTitle);
        Assert.Equal("Tax Invoice", new VoucherDetailViewModel(k.Company, v).DocumentLabel);
    }

    // ================================================================ 13 — FIX-W1f: the document-number caption

    /// <summary>
    /// <b>FIX-W1f.</b> W0-1 changed the on-screen preview mirror to caption the serial "Bill of Supply No." but left
    /// <c>InvoicePdf.DrawFirstHeader</c> hardcoding "Invoice No: ". The operator approved a page reading "Bill of
    /// Supply No." and handed the customer a PDF calling the same serial an invoice number, directly under a title
    /// band reading BILL OF SUPPLY — the self-description error the slice deliberately fixed one line away in
    /// <c>BuildClosing</c>. Neither new test inspected the number row, so it shipped green.
    ///
    /// <para>Also pins the rest of the mirror, which had exactly ONE assertion in the whole slice (its Title):
    /// measured, reverting every row change in <c>BuildInvoicePreviewReport</c> — putting "Taxable Value", "CGST",
    /// "SGST" and "Grand Total" back on the operator's screen for a bill of supply — left 15 tests green.</para>
    /// </summary>
    [Fact]
    public void A_bill_of_supply_names_its_own_serial_on_screen_and_on_paper_and_shows_no_tax_head_on_either()
    {
        var k = NewKit("Caption BoS Co", GstRegistrationType.Composition);
        var v = PostSaleInvoice(k, e => FillItemLine(e, k.TaxableItemId, k.MainGodownId, Qty, RateText));
        var preview = PrintDrilled(k.Vm, v.Id);

        // --- the bytes the customer receives ---
        var text = AsLatin1(preview.PdfBytes);
        Assert.Contains("Bill of Supply No", text);
        Assert.DoesNotContain("Invoice No", text);

        // --- the mirror the operator approves: same document, same suppressions ---
        var cells = preview.Pages[0].Lines.SelectMany(r => r.Cells).ToList();
        Assert.Contains(cells, cell => cell.StartsWith("Bill of Supply No", StringComparison.Ordinal));
        Assert.DoesNotContain(cells, cell => cell.StartsWith("Invoice No", StringComparison.Ordinal));
        Assert.DoesNotContain("CGST", cells);
        Assert.DoesNotContain("SGST", cells);
        Assert.DoesNotContain("IGST", cells);
        Assert.DoesNotContain("Taxable Value", cells);
        Assert.DoesNotContain("Grand Total", cells);
        Assert.Contains("Value of Supply", cells);      // Rule 49(g)
        Assert.Contains("Total", cells);
        // Rule 5(1)(f) — present for this composition dealer, and it reaches the mirror's first rows.
        Assert.Contains(GstReportSupport.BillOfSupplyDeclaration, cells);
    }

    /// <summary>The mirror's Rule 5(1)(f) row is gated on the §10 limb alone: a REGULAR dealer's exempt bill of
    /// supply must not claim composition status on screen any more than on paper.</summary>
    [Fact]
    public void A_regular_dealers_exempt_bill_of_supply_carries_no_composition_row_in_the_preview()
    {
        var k = NewKit("Caption Exempt Co", GstRegistrationType.Regular);
        var v = PostSaleInvoice(k, e => FillItemLine(e, k.ExemptItemId, k.MainGodownId, Qty, RateText));
        var preview = PrintDrilled(k.Vm, v.Id);

        var cells = preview.Pages[0].Lines.SelectMany(r => r.Cells).ToList();
        Assert.Contains(cells, cell => cell.StartsWith("Bill of Supply No", StringComparison.Ordinal));
        Assert.Contains("Value of Supply", cells);
        Assert.DoesNotContain(cells, cell => cell.Contains("Composition taxable person", StringComparison.Ordinal));
    }

    // ================================================================ 14 — FIX-W1e: badge and declaration agree

    /// <summary>
    /// <b>FIX-W1e.</b> W0-1 split one predicate into two: the badge read
    /// <c>VoucherPrintProjector.IsBillOfSupply</c> while the declaration still read the §10-only
    /// <c>GstReportSupport.IsBillOfSupply</c>, and MainWindow.axaml (1984-1991) renders them one under the other in
    /// the same Border, binding the declaration's visibility to the STRING rather than to the document kind. Before
    /// the slice both read one property and could not contradict each other.
    ///
    /// <para>Reachable through the shipped UI with no import: post a taxed sale as a Regular dealer, then change
    /// Registration Type to Composition in the F11 GST config (idempotent, and it checks no existing voucher). The
    /// pane then read badge "Tax Invoice" with "Composition taxable person, not eligible to collect tax on supplies"
    /// printed beneath it — on a document that demonstrably DID collect ₹8,513.41 of tax — while the PDF carried no
    /// declaration at all.</para>
    /// </summary>
    [Fact]
    public void The_badge_and_the_composition_declaration_can_never_contradict_each_other()
    {
        var k = NewKit("Switched Reg Co", GstRegistrationType.Regular);
        var v = PostSaleInvoice(k, e => FillItemLine(e, k.TaxableItemId, k.MainGodownId, Qty, RateText));
        Assert.True(GstReportSupport.HasForwardTaxLines(v));

        // The operator re-opens F11 GST and switches the registration type (GstConfigViewModel.Apply:1342).
        k.Company.Gst!.RegistrationType = GstRegistrationType.Composition;
        k.Company.Gst!.CompositionSubType = CompositionSubType.Trader;

        var detail = new VoucherDetailViewModel(k.Company, v);
        // The invariant, stated directly: the §10 wording may appear ONLY under a "Bill of Supply" badge.
        if (detail.BillOfSupplyDeclaration.Length > 0)
            Assert.Equal("Bill of Supply", detail.DocumentLabel);
        Assert.NotEqual("Tax Invoice", detail.DocumentLabel);
        Assert.Equal(string.Empty, detail.BillOfSupplyDeclaration);
    }

    // ================================================================ 15 — FIX-W1l: the SERVICE limb had no test

    /// <summary>
    /// <b>FIX-W1l.</b> Every one of W0-1's seven Desktop tests posted an ITEM invoice, so
    /// <c>IsWhollyExemptServiceSupply</c> and the whole bill-of-supply block in <c>ProjectServiceInvoice</c> had ZERO
    /// coverage — while the slice silently INVERTED the shipped behaviour of a wholly-exempt Accounting Invoice from
    /// TAX INVOICE to BILL OF SUPPLY. The pre-existing test written to pin that behaviour
    /// (<c>ServiceAccountingInvoicePrintTests.WhollyExemptServiceInvoice_…</c>) stayed green through the inversion
    /// because it asserts only <c>IsTaxInvoice</c> — a DIFFERENT predicate, still true — and never touches
    /// <c>DocumentTitle</c>.
    ///
    /// <para>The new behaviour is the legally correct one: §31(3)(c) names "a registered person supplying exempted
    /// goods <b>or services</b> or both". This test makes that a deliberate, asserted choice instead of an
    /// accident.</para>
    ///
    /// <para><b>SETTLED AND USER-RATIFIED (R12, 2026-08-10).</b> W0-1 flipped this on its own authority; the user has
    /// now explicitly ratified it, so it is a decision of record and no longer an open question for a later slice to
    /// revisit. Ground: CGST Act §31(3)(c) ("a registered person supplying exempted goods <b>or services</b> or both
    /// … shall issue, instead of a tax invoice, a bill of supply") read with §2(47) ("attracts nil rate of tax or …
    /// wholly exempt from tax under section 11 … and includes non-taxable supply") —
    /// <c>https://cbic-gst.gov.in/pdf/CGST-Act-Updated-30092020.pdf</c>. The zero-rated twin below is the boundary
    /// this ratification does NOT move: a 0%/LUT supply is taxable, not exempt, and stays a Rule-46 tax invoice.</para>
    /// </summary>
    [Fact]
    public void A_wholly_exempt_service_invoice_is_a_bill_of_supply()
    {
        var k = NewServiceKit("Svc Exempt BoS Co");
        var v = PostServiceSale(k, k.ExemptServiceId, "7512.37");     // odd paisa

        var data = VoucherPrintProjector.ProjectInvoice(k.Company, v);
        Assert.True(data.IsBillOfSupply);
        Assert.Equal("BILL OF SUPPLY", data.DocumentTitle);
        Assert.Equal(string.Empty, data.TopDeclaration);              // a Regular dealer, not a §10 one
        Assert.Empty(data.TaxRows);
        Assert.Equal(Money.FromRupees(7_512.37m), data.GrandTotal);
        var partyLeg = v.Lines.Single(l => l.LedgerId == v.PartyId!.Value);
        Assert.Equal(partyLeg.Amount, data.GrandTotal);
        Assert.Equal("Bill of Supply", new VoucherDetailViewModel(k.Company, v).DocumentLabel);

        var text = AsLatin1(PrintDrilled(k.Vm, v.Id).PdfBytes);
        Assert.Contains("BILL OF SUPPLY", text);
        Assert.DoesNotContain("TAX INVOICE", text);
        Assert.Contains("Bill of Supply No", text);
    }

    /// <summary>The zero-rated twin, which must NOT move: a 0% (LUT/export) service ledger declares
    /// <c>IsTaxable = true</c>, so the supply is taxable — not exempt — and stays a Rule-46 tax invoice even though it
    /// posts no tax leg either. This is the assertion that stops the service limb over-reaching onto zero-rated
    /// supplies, which have their own statutory treatment.</summary>
    [Fact]
    public void A_zero_rated_service_invoice_stays_a_tax_invoice()
    {
        var k = NewServiceKit("Svc ZeroRated Co");
        var v = PostServiceSale(k, k.ZeroRatedServiceId, "7512.37");

        var data = VoucherPrintProjector.ProjectInvoice(k.Company, v);
        Assert.False(data.IsBillOfSupply);
        Assert.Equal("TAX INVOICE", data.DocumentTitle);
        Assert.Equal("Tax Invoice", new VoucherDetailViewModel(k.Company, v).DocumentLabel);
        Assert.Equal(Money.FromRupees(7_512.37m), data.GrandTotal);
    }

    // ================================================================ 16 — F4: the zero-rated GAP, pinned not fixed

    /// <summary>
    /// <b>PINS A KNOWN GAP — this test asserts today's behaviour, and today's behaviour is a CONTRADICTION.</b>
    /// The routing predicate has no zero-rated concept: <c>GstTaxability</c> has four members and every non-Taxable
    /// resolution takes the §31(3)(c) exempt limb. A zero-rated supply (export, or supply to an SEZ unit/developer)
    /// is NOT an exempt supply and requires a Rule-46 tax invoice — CGST Rule 46's third proviso says an export
    /// invoice "shall carry an endorsement SUPPLY MEANT FOR EXPORT…", i.e. it is an invoice. The licensed corpus
    /// teaches exactly the colliding ledger ("Create Sales Export Exempt", 703679456-TALLY-PRIME-WITH-GST-Notes-PDF).
    ///
    /// <para>It is not fixed here because a zero-rated recipient is barely expressible: <c>IndianState.All</c> carries
    /// neither "96" nor "99" and there is no party SEZ flag (EInvoiceService.cs says so). But the collision is live
    /// wherever <c>EInvoiceService.ResolveSupplyCategory</c> answers <c>Export</c>: the same voucher's e-invoice
    /// payload declares a tax invoice while the printed paper says BILL OF SUPPLY. This test fails the day either
    /// side is changed, so the export/SEZ slice cannot land without resolving it.</para>
    ///
    /// <para><b>🔴 W0-8 — THIS TEST'S ORIGINAL PREMISE WAS FALSIFIED, and the test is restated rather than deleted.</b>
    /// It used to be named <c>…_an_exempt_supply_to_an_overseas_place_of_supply_…</c> and drove the collision from
    /// state code <b>"97"</b>, on the stated belief that 97 is overseas. It is not. The official state-code master at
    /// <c>https://einvoice1.gst.gov.in/Others/MasterCodes</c> (State Codes table, read from the page DOM) reads
    /// <b>96 = OTHER COUNTRIES, 97 = Other Territory, 99 = OTHER COUNTRIES</b>: "Other Territory" is a DOMESTIC GST
    /// territory. The shipped predicate <c>is "96" or "97"</c> was therefore wrong on both halves — it made every 97
    /// supply an export and missed 99 entirely — and is now <c>GstReportSupport.IsOverseasStateCode</c> (96/99).
    /// So the gap this test pins is unchanged in SUBSTANCE, but it is driven from <b>99</b>, a code that really is
    /// overseas; and the test now additionally asserts the corrected half — that 97 no longer collides at all.</para>
    /// </summary>
    [Fact]
    public void PINNED_GAP_an_exempt_supply_to_a_genuinely_overseas_place_of_supply_prints_a_bill_of_supply_but_e_invoices_as_an_export()
    {
        var k = NewKit("Export Collision Co", GstRegistrationType.Regular);
        k.Company.FindLedger(k.CustomerId)!.PartyGst = new PartyGstDetails
        {
            RegistrationType = GstRegistrationType.Regular, Gstin = GstinMaharashtra, StateCode = "99",
        };
        var v = PostSaleInvoice(k, e => FillItemLine(e, k.ExemptItemId, k.MainGodownId, Qty, RateText));

        // The paper says: not a tax invoice.
        Assert.True(VoucherPrintProjector.IsBillOfSupply(k.Company, v));
        // The e-invoice payload says: an export — which is a TAX INVOICE (Rule 46, third proviso). THE PIN.
        Assert.Equal(EInvoiceSupplyCategory.Export, new EInvoiceService(k.Company).ResolveSupplyCategory(v));
    }

    /// <summary>
    /// <b>W0-8, the corrected half — state code 97 is DOMESTIC and no longer collides.</b> The very shape the test
    /// above used to be built on: an exempt supply to an "Other Territory" party. Officially 97 is a domestic GST
    /// territory (see the citation above), so this is an ordinary domestic exempt supply — a bill of supply on paper
    /// AND an ordinary domestic supply to the e-invoice resolver, with no contradiction to pin. Kept as a test in its
    /// own right so the falsified premise cannot quietly come back.
    /// </summary>
    [Fact]
    public void An_exempt_supply_to_state_97_other_territory_is_domestic_not_an_export()
    {
        var k = NewKit("Other Territory Co", GstRegistrationType.Regular);
        k.Company.FindLedger(k.CustomerId)!.PartyGst = new PartyGstDetails
        {
            RegistrationType = GstRegistrationType.Regular, Gstin = GstinMaharashtra, StateCode = "97",
        };
        var v = PostSaleInvoice(k, e => FillItemLine(e, k.ExemptItemId, k.MainGodownId, Qty, RateText));

        Assert.True(VoucherPrintProjector.IsBillOfSupply(k.Company, v));
        // W0-8 (review finding #17) — the POSITIVE outcome, not `NotEqual(Export, …)`. That form also passes for null,
        // and null means "excluded from e-invoicing entirely": a reordering that let the B2C fall-through win would
        // silently drop this registered B2B recipient out of coverage with no IRN ever minted, and stay green.
        Assert.Equal(EInvoiceSupplyCategory.Regular, new EInvoiceService(k.Company).ResolveSupplyCategory(v));
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch { /* best-effort cleanup */ }
    }
}
