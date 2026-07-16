using System.Reflection;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// Phase-9 slice-4b <b>B2C dynamic (UPI) QR</b> gate (RQ-28; Notn 14/2020-CT; ER-15). Proves the self-generated payment
/// QR is built only for a &gt; ₹500 cr, B2C-QR-enabled company on a B2C outward sale; that a B2B sale, a below-threshold
/// turnover and a disabled company all yield no payload; that the payload is deterministic and de-branded; and —
/// structurally — that the B2C path carries NO IRN and never references the e-Invoice / IRP connector world (ER-15).
/// Figures worked by hand: a ₹50,000 @18% intra B2C sale ⇒ CGST ₹4,500 + SGST ₹4,500, grand total ₹59,000.
/// </summary>
public sealed class B2cQrTests
{
    private const string GstinMaharashtra = "27AAPFU0939F1ZV";
    private static readonly DateOnly FyStart = new(2025, 4, 1);
    private static readonly DateOnly SaleDate = new(2025, 4, 10);
    private static readonly Money Above500Cr = Money.FromRupees(6_000_000_000m); // ₹600 cr
    private static readonly Money Below500Cr = Money.FromRupees(1_000_000_000m); // ₹100 cr

    private sealed class Fixture
    {
        public required Company Company { get; init; }
        public required Voucher B2CSale { get; init; }
        public required Voucher B2BSale { get; init; }
        public required B2cQrService Service { get; init; }
    }

    private static Fixture Build(bool b2cQr = true)
    {
        var c = CompanyFactory.CreateSeeded("B2C QR Co", FyStart);
        var gst = new GstService(c);
        gst.EnableGst(new GstConfig
        {
            HomeStateCode = "27", Gstin = GstinMaharashtra, RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart, Periodicity = GstReturnPeriodicity.Monthly,
            B2cDynamicQrEnabled = b2cQr,
            B2cQrUpiId = b2cQr ? "apexsolutions@upi" : null,
            B2cQrPayeeName = b2cQr ? "Apex Traders" : null,
        });

        var inv = new InventoryService(c);
        var grp = inv.CreateStockGroup("Goods");
        var nos = inv.CreateSimpleUnit("Nos", "Numbers", unitQuantityCode: "NOS");
        var main = c.MainLocation!.Id;
        var widget = inv.CreateStockItem("Widget", grp.Id, nos.Id);
        widget.Gst = new StockItemGstDetails { HsnSac = "847130", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800 };
        inv.AddOpeningBalance(widget.Id, main, 100m, Money.FromRupees(40000m));

        var sales = Add(c, "Sales", "Sales Accounts", false);

        var consumer = Add(c, "Walk-in Consumer", "Sundry Debtors", true);
        consumer.PartyGst = new PartyGstDetails { RegistrationType = GstRegistrationType.Consumer, StateCode = "27" };
        var b2b = Add(c, "Local Debtor", "Sundry Debtors", true);
        b2b.PartyGst = new PartyGstDetails { RegistrationType = GstRegistrationType.Regular, Gstin = GstinMaharashtra, StateCode = "27" };

        var ledgers = new LedgerService(c);
        var salesType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id;

        // A B2C intra sale: 1 Widget @ ₹50,000 = ₹50,000 @ 18% intra ⇒ CGST 4,500 + SGST 4,500, grand total ₹59,000.
        var b2cSale = ledgers.Post(new Voucher(Guid.NewGuid(), salesType, SaleDate, TaxedSaleLines(gst, consumer.Id, sales.Id),
            partyId: consumer.Id, inventoryLines: new[] { new VoucherInventoryLine(widget.Id, main, 1m, Money.FromRupees(50000m)) }));

        // The same sale to a registered B2B recipient — never a B2C QR.
        var b2bSale = ledgers.Post(new Voucher(Guid.NewGuid(), salesType, SaleDate, TaxedSaleLines(gst, b2b.Id, sales.Id),
            partyId: b2b.Id, inventoryLines: new[] { new VoucherInventoryLine(widget.Id, main, 1m, Money.FromRupees(50000m)) }));

        return new Fixture { Company = c, B2CSale = b2cSale, B2BSale = b2bSale, Service = new B2cQrService(c) };
    }

    private static List<EntryLine> TaxedSaleLines(GstService gst, Guid partyId, Guid salesId)
    {
        var tax = gst.ComputeInvoiceTax(new[] { new GstService.TaxableLine(Money.FromRupees(50000m), 1800) },
            interState: false, GstTaxDirection.Output);
        var lines = new List<EntryLine>
        {
            new(partyId, Money.FromRupees(59000m), DrCr.Debit),
            new(salesId, Money.FromRupees(50000m), DrCr.Credit),
        };
        lines.AddRange(tax.TaxLines);
        return lines;
    }

    private static Domain.Ledger Add(Company c, string name, string groupName, bool openingIsDebit)
    {
        var l = new Domain.Ledger(Guid.NewGuid(), name, c.FindGroupByName(groupName)!.Id, Money.Zero, openingIsDebit);
        c.AddLedger(l);
        return l;
    }

    private static Guid ConsumerId(Company c) => c.FindLedgerByName("Walk-in Consumer")!.Id;

    /// <summary>Posts a taxable intra 18% Widget sale of <paramref name="saleValue"/> to <paramref name="partyId"/> with a
    /// hand-set party debit (the receivable) and optional extra credit legs (a round-off / freight / charge), so a test can
    /// prove the QR amount equals the party debit even when the receivable ≠ supply + Σtax.</summary>
    private static Voucher PostSale(Company c, Guid partyId, decimal partyDebit, decimal saleValue,
        IEnumerable<EntryLine>? extraCreditLines = null)
    {
        var gst = new GstService(c);
        var sales = c.FindLedgerByName("Sales")!;
        var widget = c.FindStockItemByName("Widget")!;
        var main = c.MainLocation!.Id;
        var salesType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id;
        var tax = gst.ComputeInvoiceTax(new[] { new GstService.TaxableLine(Money.FromRupees(saleValue), 1800) },
            interState: false, GstTaxDirection.Output);
        var lines = new List<EntryLine>
        {
            new(partyId, Money.FromRupees(partyDebit), DrCr.Debit),
            new(sales.Id, Money.FromRupees(saleValue), DrCr.Credit),
        };
        lines.AddRange(tax.TaxLines);
        if (extraCreditLines is not null) lines.AddRange(extraCreditLines);
        return new LedgerService(c).Post(new Voucher(Guid.NewGuid(), salesType, SaleDate, lines,
            partyId: partyId, inventoryLines: new[] { new VoucherInventoryLine(widget.Id, main, 1m, Money.FromRupees(saleValue)) }));
    }

    // ================================================================ generated for an enabled >₹500 cr B2C sale

    [Fact]
    public void B2c_qr_is_generated_for_an_enabled_over_500cr_company_on_a_b2c_sale()
    {
        var f = Build();
        var payload = f.Service.BuildFor(f.B2CSale, Above500Cr);

        Assert.NotNull(payload);
        Assert.Equal(Money.FromRupees(59000m), payload!.Amount);          // taxable ₹50,000 + CGST ₹4,500 + SGST ₹4,500
        Assert.Equal("apexsolutions@upi", payload.PayeeVpa);
        Assert.Equal("Apex Traders", payload.PayeeName);

        // A well-formed, deterministic UPI deep link: literal VPA, escaped payee/ref, amount to the paisa, INR.
        var expected = $"upi://pay?pa=apexsolutions@upi&pn=Apex%20Traders&am=59000.00&cu=INR&tn={payload.Reference}";
        Assert.Equal(expected, payload.UpiUri);
        Assert.StartsWith("upi://pay?pa=apexsolutions@upi", payload.UpiUri);

        // ER-15: the self-generated B2C QR path mints NO e-invoice record and never touches the IRP.
        Assert.Empty(f.Company.EInvoiceRecords);
    }

    // ================================================================ no QR for B2B / below-threshold / disabled

    [Fact]
    public void No_b2c_qr_for_a_b2b_sale()
    {
        var f = Build();
        Assert.Null(f.Service.BuildFor(f.B2BSale, Above500Cr));
    }

    [Fact]
    public void No_b2c_qr_below_or_at_the_500cr_threshold()
    {
        var f = Build();
        Assert.Null(f.Service.BuildFor(f.B2CSale, Below500Cr));
        // Gated strictly ABOVE ₹500 cr: exactly at the threshold yields no QR.
        Assert.Null(f.Service.BuildFor(f.B2CSale, f.Company.Gst!.B2cQrAatoThreshold));
    }

    [Fact]
    public void No_b2c_qr_when_the_feature_is_disabled()
    {
        var f = Build(b2cQr: false);
        Assert.Null(f.Service.BuildFor(f.B2CSale, Above500Cr));
    }

    // ============================================ finding #1: am= is the party debit (round-off / charges included)

    [Fact]
    public void B2c_qr_amount_equals_the_party_debit_including_invoice_round_off()
    {
        // ₹1,005 @ 18% intra ⇒ CGST 90.45 + SGST 90.45 (tax 180.90); pre-round total ₹1,185.90; invoice round-off +0.10 ⇒
        // payable ₹1,186.00. The round-off leg posts to the Round Off ledger with NO GstLineTax, so the old
        // "supply + Σtax" reconstruction produced ₹1,185.90 — NOT what the buyer owes (finding #1).
        var f = Build();
        var roundOff = f.Company.FindLedgerByName(GstService.RoundOffLedgerName)!;
        var sale = PostSale(f.Company, ConsumerId(f.Company), partyDebit: 1186.00m, saleValue: 1005m,
            extraCreditLines: new[] { new EntryLine(roundOff.Id, Money.FromRupees(0.10m), DrCr.Credit) });

        var payload = f.Service.BuildFor(sale, Above500Cr);

        Assert.NotNull(payload);
        var partyDebit = sale.Lines.Single(l => l.LedgerId == sale.PartyId && l.Side == DrCr.Debit).Amount;
        Assert.Equal(partyDebit, payload!.Amount);                 // == the authoritative receivable
        Assert.Equal(Money.FromRupees(1186.00m), payload.Amount);  // NOT the pre-round ₹1,185.90
        Assert.Contains("&am=1186.00&", payload.UpiUri);
    }

    [Fact]
    public void B2c_qr_amount_includes_a_freight_other_charge_leg()
    {
        // A ₹50,000 @ 18% sale (tax ₹9,000) plus a ₹500 freight charged to the customer ⇒ party debit ₹59,500. The freight
        // leg carries no GstLineTax and is not an inventory line, so the old reconstruction dropped it (returned ₹59,000).
        var f = Build();
        var freight = Add(f.Company, "Freight Charges Collected", "Indirect Incomes", false);
        var sale = PostSale(f.Company, ConsumerId(f.Company), partyDebit: 59500m, saleValue: 50000m,
            extraCreditLines: new[] { new EntryLine(freight.Id, Money.FromRupees(500m), DrCr.Credit) });

        var payload = f.Service.BuildFor(sale, Above500Cr);

        Assert.NotNull(payload);
        var partyDebit = sale.Lines.Single(l => l.LedgerId == sale.PartyId && l.Side == DrCr.Debit).Amount;
        Assert.Equal(partyDebit, payload!.Amount);                 // == the receivable, freight included
        Assert.Equal(Money.FromRupees(59500m), payload.Amount);    // NOT the ₹59,000 supply+tax reconstruction
        Assert.Contains("&am=59500.00&", payload.UpiUri);
    }

    // ============================================ finding #2: a GSTIN-bearing party is B2B even if typed Unregistered

    [Fact]
    public void No_b2c_qr_for_a_party_that_carries_a_gstin_even_if_typed_unregistered()
    {
        // A party with a non-empty GSTIN is a registered (B2B) recipient — its document carries the IRP QR, never a B2C
        // dynamic QR. IsB2C is OR-based and RegistrationType defaults to Unregistered, so such a party leaked through the
        // old gate (finding #2); the GSTIN short-circuit now honours the "never a registered GSTIN-bearing recipient" contract.
        var f = Build();
        var gstinUnreg = Add(f.Company, "GSTIN-bearing Unregistered", "Sundry Debtors", true);
        gstinUnreg.PartyGst = new PartyGstDetails
        {
            RegistrationType = GstRegistrationType.Unregistered, Gstin = GstinMaharashtra, StateCode = "27",
        };
        Assert.True(gstinUnreg.PartyGst.IsB2C); // the OR-based flag still reports B2C — that is exactly the leak
        var sale = PostSale(f.Company, gstinUnreg.Id, partyDebit: 59000m, saleValue: 50000m);

        Assert.Null(f.Service.BuildFor(sale, Above500Cr));
    }

    // ============================================ finding #3: a malformed payee VPA is rejected

    [Fact]
    public void Enable_gst_rejects_a_malformed_b2c_qr_vpa()
    {
        var c = CompanyFactory.CreateSeeded("Bad VPA Co", FyStart);
        var ex = Assert.Throws<ArgumentException>(() => new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = "27", Gstin = GstinMaharashtra, RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart, Periodicity = GstReturnPeriodicity.Monthly,
            B2cDynamicQrEnabled = true, B2cQrUpiId = "bad vpa", B2cQrPayeeName = "Apex Traders",
        }));
        Assert.Contains("VPA", ex.Message);
    }

    [Fact]
    public void No_b2c_qr_when_the_payee_vpa_is_malformed()
    {
        // Defence in depth: even a directly-mutated config carrying a malformed VPA yields no QR (never a corrupt deep link).
        var f = Build();
        f.Company.Gst!.B2cQrUpiId = "x@y&z"; // '&' would inject into upi://pay?pa=…&…
        Assert.Null(f.Service.BuildFor(f.B2CSale, Above500Cr));
    }

    // ================================================================ deterministic payload

    [Fact]
    public void The_b2c_qr_payload_is_deterministic()
    {
        var f = Build();
        var a = f.Service.BuildFor(f.B2CSale, Above500Cr);
        var b = f.Service.BuildFor(f.B2CSale, Above500Cr);
        Assert.NotNull(a);
        Assert.Equal(a!.UpiUri, b!.UpiUri);
        Assert.Equal(a, b);
    }

    // ================================================================ ER-15 structural + ER-11 de-branded

    [Fact]
    public void B2c_qr_never_touches_the_irn_or_connector_types_ER15()
    {
        // (a) The payload has NO IRN field — a B2C dynamic QR is never IRP-signed (ER-15, by construction).
        Assert.DoesNotContain(typeof(B2cQrPayload).GetProperties(),
            p => p.Name.Contains("Irn", StringComparison.OrdinalIgnoreCase));

        // (b) Assembly-direction guarantee: B2cQrService lives in Apex.Ledger, which does NOT reference Apex.Ledger.Io,
        //     so it structurally cannot even name IGstPortalConnector / Inv01Request / OfflineJsonConnector.
        Assert.DoesNotContain(typeof(B2cQrService).Assembly.GetReferencedAssemblies(),
            a => a.Name == "Apex.Ledger.Io");

        // (c) No member signature (params / returns / fields) of the B2C types references an e-Invoice / IRP type.
        var forbidden = new HashSet<Type>
        {
            typeof(EInvoiceService), typeof(EInvoiceRecord), typeof(EInvoiceStatus),
            typeof(EInvoiceCoverage), typeof(EInvoiceSupplyCategory), typeof(EInvoiceExemptionClass),
        };
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        foreach (var t in new[] { typeof(B2cQrService), typeof(B2cQrPayload) })
        {
            foreach (var m in t.GetMethods(all))
            {
                Assert.DoesNotContain(m.ReturnType, forbidden);
                foreach (var p in m.GetParameters()) Assert.DoesNotContain(p.ParameterType, forbidden);
            }
            foreach (var fld in t.GetFields(all)) Assert.DoesNotContain(fld.FieldType, forbidden);
        }

        // (d) De-branded (ER-11): no "Tally" leaks into the generated payload.
        var f = Build();
        var payload = f.Service.BuildFor(f.B2CSale, Above500Cr)!;
        Assert.DoesNotContain("Tally", payload.UpiUri, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Tally", payload.PayeeName, StringComparison.OrdinalIgnoreCase);
    }
}
