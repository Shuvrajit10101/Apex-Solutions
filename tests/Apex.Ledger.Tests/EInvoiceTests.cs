using System.Reflection;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// Phase-9 slice-4a <b>e-Invoice engine</b> gate (RQ-5/RQ-18; ER-5, ER-15). Proves applicability (covered-doc scope),
/// the IRN-only-from-IRP lifecycle (never computed locally), the 24-h full-document cancel + no-reuse rule, the GSTR-1
/// IRN annotation + reconciliation view, and that a B2C supply is EXCLUDED and never mints an e-invoice record. All
/// figures worked by hand: a ₹50,000 @18% intra B2B sale ⇒ CGST ₹4,500 + SGST ₹4,500, invoice value ₹59,000.
/// </summary>
public sealed class EInvoiceTests
{
    private const string GstinMaharashtra = "27AAPFU0939F1ZV";
    private const string GstinGujarat = "24AAACC1206D1ZM";
    private static readonly DateOnly FyStart = new(2025, 4, 1);
    private static readonly DateOnly SaleDate = new(2025, 4, 10);

    private sealed class Fixture
    {
        public required Company Company { get; init; }
        public required Voucher B2BSale { get; init; }
        public required Voucher ExemptB2BSale { get; init; }
        public required Voucher B2CSale { get; init; }
        public required Voucher ExportSale { get; init; }
        public required Voucher RcmSale { get; init; }
        public required Voucher Purchase { get; init; }
        public required EInvoiceService Service { get; init; }
    }

    private static Fixture Build(bool eInvoicing = true)
    {
        var c = CompanyFactory.CreateSeeded("e-Invoice Co", FyStart);
        var gst = new GstService(c);
        gst.EnableGst(new GstConfig
        {
            HomeStateCode = "27", Gstin = GstinMaharashtra, RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart, Periodicity = GstReturnPeriodicity.Monthly,
            EInvoicingEnabled = eInvoicing, EInvoiceApplicableFrom = eInvoicing ? FyStart : null,
        });

        var ledgers = new LedgerService(c);
        var inv = new InventoryService(c);
        var grp = inv.CreateStockGroup("Goods");
        var nos = inv.CreateSimpleUnit("Nos", "Numbers", unitQuantityCode: "NOS");
        var main = c.MainLocation!.Id;

        var widget = inv.CreateStockItem("Widget", grp.Id, nos.Id);
        widget.Gst = new StockItemGstDetails { HsnSac = "847130", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800 };
        inv.AddOpeningBalance(widget.Id, main, 100m, Money.FromRupees(40000m)); // stock on hand for the sale

        var sales = Add(c, "Sales", "Sales Accounts", false);
        var rcmSales = Add(c, "RCM Sales", "Sales Accounts", false);
        rcmSales.SalesPurchaseGst = new StockItemGstDetails
        {
            Taxability = GstTaxability.Taxable, RateBasisPoints = 1800, SupplyType = GstSupplyType.Services,
            ReverseChargeApplicable = true,
        };
        var purchases = Add(c, "Purchases", "Purchase Accounts", true);

        var b2b = Add(c, "Local Debtor", "Sundry Debtors", true);
        b2b.PartyGst = new PartyGstDetails { RegistrationType = GstRegistrationType.Regular, Gstin = GstinMaharashtra, StateCode = "27" };
        var consumer = Add(c, "Walk-in Consumer", "Sundry Debtors", true);
        consumer.PartyGst = new PartyGstDetails { RegistrationType = GstRegistrationType.Consumer, StateCode = "27" };
        var exporter = Add(c, "Overseas Buyer", "Sundry Debtors", true);
        exporter.PartyGst = new PartyGstDetails { RegistrationType = GstRegistrationType.Unregistered, StateCode = "96" };
        var supplier = Add(c, "Local Supplier", "Sundry Creditors", false);
        supplier.PartyGst = new PartyGstDetails { RegistrationType = GstRegistrationType.Regular, Gstin = GstinMaharashtra, StateCode = "27" };

        var salesType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id;
        var purchaseType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase).Id;

        // ---- B2B intra sale: 1 Widget @ ₹50,000 = ₹50,000 @ 18% intra ⇒ CGST 4500 + SGST 4500, value ₹59,000. ----
        var s1Tax = gst.ComputeInvoiceTax(new[] { new GstService.TaxableLine(Money.FromRupees(50000m), 1800) },
            interState: false, GstTaxDirection.Output);
        var s1Lines = new List<EntryLine>
        {
            new(b2b.Id, Money.FromRupees(59000m), DrCr.Debit),
            new(sales.Id, Money.FromRupees(50000m), DrCr.Credit),
        };
        s1Lines.AddRange(s1Tax.TaxLines);
        var b2bSale = ledgers.Post(new Voucher(Guid.NewGuid(), salesType, SaleDate, s1Lines, partyId: b2b.Id,
            inventoryLines: new[] { new VoucherInventoryLine(widget.Id, main, 1m, Money.FromRupees(50000m)) }));

        // ---- Exempt-only B2B sale (registered GSTIN-bearing recipient, but the supply carries NO forward tax —
        //      all lines exempt/nil ⇒ a Bill of Supply, which is never e-invoiced). As-voucher, no tax lines. ----
        var exemptB2BSale = ledgers.Post(new Voucher(Guid.NewGuid(), salesType, SaleDate, new List<EntryLine>
        {
            new(b2b.Id, Money.FromRupees(4000m), DrCr.Debit),
            new(sales.Id, Money.FromRupees(4000m), DrCr.Credit),
        }, partyId: b2b.Id));

        // ---- B2C intra sale (as-voucher, no tax needed for coverage). ----
        var b2cSale = ledgers.Post(new Voucher(Guid.NewGuid(), salesType, SaleDate, new List<EntryLine>
        {
            new(consumer.Id, Money.FromRupees(1000m), DrCr.Debit),
            new(sales.Id, Money.FromRupees(1000m), DrCr.Credit),
        }, partyId: consumer.Id));

        // ---- Export sale (overseas place of supply). ----
        var exportSale = ledgers.Post(new Voucher(Guid.NewGuid(), salesType, SaleDate, new List<EntryLine>
        {
            new(exporter.Id, Money.FromRupees(2000m), DrCr.Debit),
            new(sales.Id, Money.FromRupees(2000m), DrCr.Credit),
        }, partyId: exporter.Id));

        // ---- Outward RCM supply (recipient pays; sales ledger flagged ReverseChargeApplicable). ----
        var rcmSale = ledgers.Post(new Voucher(Guid.NewGuid(), salesType, SaleDate, new List<EntryLine>
        {
            new(b2b.Id, Money.FromRupees(3000m), DrCr.Debit),
            new(rcmSales.Id, Money.FromRupees(3000m), DrCr.Credit),
        }, partyId: b2b.Id));

        // ---- A purchase (inward — never e-invoiced). ----
        var purchase = ledgers.Post(new Voucher(Guid.NewGuid(), purchaseType, SaleDate, new List<EntryLine>
        {
            new(purchases.Id, Money.FromRupees(500m), DrCr.Debit),
            new(supplier.Id, Money.FromRupees(500m), DrCr.Credit),
        }, partyId: supplier.Id));

        return new Fixture
        {
            Company = c, B2BSale = b2bSale, ExemptB2BSale = exemptB2BSale, B2CSale = b2cSale, ExportSale = exportSale,
            RcmSale = rcmSale, Purchase = purchase, Service = new EInvoiceService(c),
        };
    }

    private static Domain.Ledger Add(Company c, string name, string groupName, bool openingIsDebit)
    {
        var l = new Domain.Ledger(Guid.NewGuid(), name, c.FindGroupByName(groupName)!.Id, Money.Zero, openingIsDebit);
        c.AddLedger(l);
        return l;
    }

    // ================================================================ §6.6 IRN stored-from-IRP, never computed

    [Fact]
    public void Irn_and_qr_are_stored_from_the_irp_response_and_never_computed_locally()
    {
        var f = Build();
        var record = f.Service.PrepareRecord(f.B2BSale);
        Assert.Equal(EInvoiceStatus.Pending, record.Status);
        Assert.Null(record.Irn);

        var irn = new string('A', 64);
        var qr = "IRP-SIGNED-QR-BLOB";
        var signed = new byte[] { 1, 2, 3, 4 };
        f.Service.RecordIrpResponse(record, irn, "112010036284", SaleDate, qr, signed);

        Assert.Equal(EInvoiceStatus.Generated, record.Status);
        Assert.Equal(irn, record.Irn);
        Assert.Equal(qr, record.SignedQr);
        Assert.Equal(signed, record.SignedJson);

        // ER-5 structural: no method on the engine / record computes a 64-char IRN or signs a QR.
        foreach (var t in new[] { typeof(EInvoiceService), typeof(EInvoiceRecord) })
            foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                var name = m.Name.ToLowerInvariant();
                Assert.DoesNotContain("computeirn", name);
                Assert.DoesNotContain("signqr", name);
                Assert.DoesNotContain("sha256", name);
                Assert.False(name.Contains("hash") && (name.Contains("irn") || name.Contains("qr")),
                    $"{t.Name}.{m.Name} looks like it derives an IRN/QR — ER-5 forbids local IRN/QR computation.");
            }
    }

    // ================================================================ §6.7 24-h cancel + no reuse + no amend

    [Fact]
    public void Cancel_succeeds_within_24h_and_throws_after_and_doc_no_is_not_reusable()
    {
        var f = Build();
        var record = f.Service.PrepareRecord(f.B2BSale);
        f.Service.RecordIrpResponse(record, new string('B', 64), "ACK1", SaleDate, "QR", new byte[] { 9 });

        // A cancel at AckDate + 1 day succeeds; at + 2 days throws.
        var other = Build();
        var r2 = other.Service.PrepareRecord(other.B2BSale);
        other.Service.RecordIrpResponse(r2, new string('C', 64), "ACK2", SaleDate, "QR", new byte[] { 9 });
        Assert.Throws<InvalidOperationException>(() => other.Service.Cancel(r2, SaleDate.AddDays(2), "1"));

        f.Service.Cancel(record, SaleDate.AddDays(1), "1");
        Assert.Equal(EInvoiceStatus.Cancelled, record.Status);

        // The cancelled doc-no is NOT reusable — PrepareRecord refuses a second record for the same voucher/doc-no.
        Assert.Throws<InvalidOperationException>(() => f.Service.PrepareRecord(f.B2BSale));

        // There is no amend-on-IRP path (structural).
        Assert.DoesNotContain(typeof(EInvoiceService).GetMethods(),
            m => m.Name.Contains("Amend", StringComparison.OrdinalIgnoreCase) || m.Name.Contains("Modify", StringComparison.OrdinalIgnoreCase));
    }

    // ================================================================ §6.8 covered-doc scope

    [Fact]
    public void Covered_doc_scope_includes_b2b_export_rcm_and_excludes_b2c_composition_and_inward()
    {
        var f = Build();
        Assert.Equal(EInvoiceCoverage.Covered, f.Service.CoverageOf(f.B2BSale));
        Assert.Equal(EInvoiceCoverage.Covered, f.Service.CoverageOf(f.ExportSale));
        Assert.Equal(EInvoiceCoverage.Covered, f.Service.CoverageOf(f.RcmSale));
        Assert.Equal(EInvoiceSupplyCategory.RcmSupplierLiable, f.Service.ResolveSupplyCategory(f.RcmSale));
        Assert.Equal(EInvoiceSupplyCategory.Export, f.Service.ResolveSupplyCategory(f.ExportSale));
        Assert.Equal(EInvoiceSupplyCategory.Regular, f.Service.ResolveSupplyCategory(f.B2BSale));

        // B2C excluded; inward purchase (import/ISD class) not applicable.
        Assert.Equal(EInvoiceCoverage.Excluded, f.Service.CoverageOf(f.B2CSale));
        Assert.Equal(EInvoiceCoverage.NotApplicable, f.Service.CoverageOf(f.Purchase));

        // e-invoicing off ⇒ not applicable everywhere.
        Assert.Equal(EInvoiceCoverage.NotApplicable, Build(eInvoicing: false).Service.CoverageOf(f.B2BSale));
    }

    [Fact]
    public void Composition_dealer_never_e_invoices_and_an_exempt_class_is_exempt()
    {
        // A composition company: e-invoicing short-circuits to Not-Applicable (a Bill of Supply is never e-invoiced).
        var comp = CompanyFactory.CreateSeeded("Composition Co", FyStart);
        new GstService(comp).EnableGst(new GstConfig
        {
            HomeStateCode = "27", Gstin = GstinMaharashtra, RegistrationType = GstRegistrationType.Composition,
            CompositionSubType = CompositionSubType.Trader, ApplicableFrom = FyStart, Periodicity = GstReturnPeriodicity.Quarterly,
            EInvoicingEnabled = true, EInvoiceApplicableFrom = FyStart,
        });
        var cSales = Add(comp, "Sales", "Sales Accounts", false);
        var cParty = Add(comp, "Debtor", "Sundry Debtors", true);
        cParty.PartyGst = new PartyGstDetails { RegistrationType = GstRegistrationType.Regular, Gstin = GstinGujarat, StateCode = "24" };
        var v = new LedgerService(comp).Post(new Voucher(Guid.NewGuid(),
            comp.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id, SaleDate, new List<EntryLine>
            {
                new(cParty.Id, Money.FromRupees(1000m), DrCr.Debit), new(cSales.Id, Money.FromRupees(1000m), DrCr.Credit),
            }, partyId: cParty.Id));
        Assert.Equal(EInvoiceCoverage.NotApplicable, new EInvoiceService(comp).CoverageOf(v));

        // An exempt supplier class (e.g. GTA) ⇒ every document is Exempt regardless of turnover.
        var f = Build();
        f.Company.Gst!.ExemptionClasses = EInvoiceExemptionClass.Gta;
        Assert.Equal(EInvoiceCoverage.Exempt, f.Service.CoverageOf(f.B2BSale));
    }

    [Fact]
    public void Thirty_day_age_guard_fires_only_when_the_reporting_age_limit_applies()
    {
        var f = Build();
        // Below ₹10 cr (ReportingAgeLimitApplies false): never fires, even 60 days later.
        Assert.False(f.Service.IsReportingAgeExceeded(f.B2BSale, SaleDate.AddDays(60)));

        // At/above ₹10 cr (the INDEPENDENT flag on): fires past 30 days, not before.
        f.Company.Gst!.ReportingAgeLimitApplies = true;
        Assert.False(f.Service.IsReportingAgeExceeded(f.B2BSale, SaleDate.AddDays(30)));
        Assert.True(f.Service.IsReportingAgeExceeded(f.B2BSale, SaleDate.AddDays(31)));
    }

    // ================================================================ §6.9 GSTR-1 auto-populate + reconciliation

    [Fact]
    public void Gstr1_b2b_row_carries_the_irn_of_a_generated_record_and_reconciles()
    {
        var f = Build();
        var record = f.Service.PrepareRecord(f.B2BSale);
        var irn = new string('D', 64);
        f.Service.RecordIrpResponse(record, irn, "ACK", SaleDate, "QR", new byte[] { 7 });

        var from = new DateOnly(2025, 4, 1);
        var to = new DateOnly(2025, 4, 30);
        var g = Gstr1.Build(f.Company, from, to);
        var b2bRow = g.B2B.Single(r => r.PartyGstin == GstinMaharashtra);
        Assert.Equal(irn, b2bRow.Irn);

        var recon = Gstr1.EInvoiceReconciliation(f.Company, from, to);
        Assert.True(recon.Covered >= 1);
        Assert.Equal(1, recon.Tagged);
        Assert.Equal(0, recon.Mismatched);

        // An e-invoicing-off company's GSTR-1 B2B rows carry no IRN (byte-identical, ER-13).
        var off = Build(eInvoicing: false);
        var offRow = Gstr1.Build(off.Company, from, to).B2B.Single(r => r.PartyGstin == GstinMaharashtra);
        Assert.Null(offRow.Irn);
    }

    // ================================================================ §6.10 (B2C part) ER-15: B2C never enters IRP flow

    [Fact]
    public void A_b2c_supply_is_excluded_and_never_mints_an_einvoice_record()
    {
        var f = Build();
        Assert.Equal(EInvoiceCoverage.Excluded, f.Service.CoverageOf(f.B2CSale));
        Assert.Throws<InvalidOperationException>(() => f.Service.PrepareRecord(f.B2CSale));
        Assert.Null(f.Company.FindEInvoiceRecordForVoucher(f.B2CSale.Id));
        Assert.Empty(f.Company.EInvoiceRecords);
    }

    // ================================================================ A10 #1 — an exempt-only B2B sale is a Bill of Supply

    [Fact]
    public void An_exempt_only_b2b_sale_is_a_bill_of_supply_and_is_excluded_not_covered()
    {
        var f = Build();
        // The recipient is a registered GSTIN-bearer (category resolves to Regular B2B), but the supply carries NO
        // forward tax (all lines exempt/nil) ⇒ it is itself a Bill of Supply, never e-invoiced. Pre-fix CoverageOf
        // returned Covered (category non-null) and PrepareRecord would mint a zero-value INV-01; post-fix it is Excluded.
        Assert.Equal(EInvoiceSupplyCategory.Regular, f.Service.ResolveSupplyCategory(f.ExemptB2BSale));
        Assert.Equal(EInvoiceCoverage.Excluded, f.Service.CoverageOf(f.ExemptB2BSale));
        Assert.Throws<InvalidOperationException>(() => f.Service.PrepareRecord(f.ExemptB2BSale));
        Assert.Null(f.Company.FindEInvoiceRecordForVoucher(f.ExemptB2BSale.Id));

        // The exclusion must NOT sweep up a legitimately zero-rated EXPORT (covered, can be zero-tax) or an outward RCM
        // supply (covered, recipient pays) — both carry no forward tax yet remain covered.
        Assert.Equal(EInvoiceCoverage.Covered, f.Service.CoverageOf(f.ExportSale));
        Assert.Equal(EInvoiceCoverage.Covered, f.Service.CoverageOf(f.RcmSale));
    }

    // ================================================================ A10 #2 — RecordIrpResponse status guard

    [Fact]
    public void RecordIrpResponse_refuses_a_cancelled_or_already_generated_record_but_allows_pending_or_failed()
    {
        var f = Build();
        var record = f.Service.PrepareRecord(f.B2BSale);
        // Pending → the IRP response records normally.
        f.Service.RecordIrpResponse(record, new string('A', 64), "ACK", SaleDate, "QR", new byte[] { 1 });
        Assert.Equal(EInvoiceStatus.Generated, record.Status);

        // Already-Generated → a second response is refused (a generated IRN cannot be silently overwritten); untouched.
        Assert.Throws<InvalidOperationException>(() =>
            f.Service.RecordIrpResponse(record, new string('Z', 64), "ACK2", SaleDate, "QR2", new byte[] { 2 }));
        Assert.Equal(new string('A', 64), record.Irn);

        // Cancelled → refused (a cancelled IRN cannot be resurrected to Generated); state stays Cancelled.
        f.Service.Cancel(record, SaleDate.AddDays(1), "1");
        Assert.Equal(EInvoiceStatus.Cancelled, record.Status);
        Assert.Throws<InvalidOperationException>(() =>
            f.Service.RecordIrpResponse(record, new string('Y', 64), "ACK3", SaleDate, "QR3", new byte[] { 3 }));
        Assert.Equal(EInvoiceStatus.Cancelled, record.Status);

        // A Failed record (an earlier IRP rejection) can still be recorded on retry.
        var f2 = Build();
        var r2 = f2.Service.PrepareRecord(f2.B2BSale);
        f2.Service.RecordFailure(r2, "2150", "Duplicate IRN");
        Assert.Equal(EInvoiceStatus.Failed, r2.Status);
        f2.Service.RecordIrpResponse(r2, new string('B', 64), "ACK", SaleDate, "QR", new byte[] { 4 });
        Assert.Equal(EInvoiceStatus.Generated, r2.Status);
    }

    // ================================================================ A10 #3 — NicApiCredentials redacts its secrets

    [Fact]
    public void NicApiCredentials_ToString_redacts_the_secret_fields_but_stays_a_usable_diagnostic()
    {
        var creds = new NicApiCredentials("cid", "topsecret", "user", "pw");
        var text = creds.ToString();

        // The two secrets never appear in the synthesized ToString (ER-16 — a stray log must not leak them).
        Assert.DoesNotContain("topsecret", text);
        Assert.DoesNotContain("pw", text);
        Assert.Contains("<redacted>", text);

        // Still a usable diagnostic: the two non-secret members remain visible.
        Assert.Contains("cid", text);
        Assert.Contains("user", text);

        // Redaction is display-only — the record's value semantics (Equals) still compare the real secrets.
        Assert.Equal(new NicApiCredentials("cid", "topsecret", "user", "pw"), creds);
        Assert.NotEqual(new NicApiCredentials("cid", "different", "user", "pw"), creds);
    }
}
