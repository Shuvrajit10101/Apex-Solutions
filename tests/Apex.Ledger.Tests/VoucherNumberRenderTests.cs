using System.Globalization;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// Numbering slice S2 (numbering-design-v2 §2, §3, §7) — the engine-side render-everywhere policy and the
/// duplicate guard. Proves: (a) with an EMPTY numbering config every rendered number is byte-identical to the
/// bare int across the whole Robert/Bright posted surface (ER-13); (b) under an affix the ONE
/// <see cref="EInvoiceService.DocumentNumberOf(Company, Voucher)"/> policy makes the GSTR-1 B2B number equal the
/// portal doc-no (review corr-F1); (c) the second (inventory) engine renders the affix in its registers
/// (review corr-F2); (d) Prevent-Duplicate rejects a genuine collision yet accepts a legitimate new number
/// (review schema-F3); (e) two covered types sharing an int under DISTINCT prefixes both mint an e-invoice
/// (review schema-F3).
/// </summary>
public sealed class VoucherNumberRenderTests
{
    private const string GstinMaharashtra = "27AAPFU0939F1ZV";
    private static readonly DateOnly FyStart = new(2025, 4, 1);
    private static readonly DateOnly SaleDate = new(2025, 4, 10);
    private static readonly DateOnly AsOf = new(2026, 3, 31);

    // ================================================================ (a) empty config == today, whole surface

    [Theory]
    [InlineData("robert.json")]
    [InlineData("bright.json")]
    public void Robert_and_Bright_unchanged(string fixture)
    {
        var f = FixtureLoader.Load(fixture);
        var c = f.Company;

        // Every posted accounting voucher renders EXACTLY its bare int (empty numbering config ⇒ ER-13). The
        // byte-identity half: repointing every render site to Company.FormatVoucherNumber changed nothing today.
        Assert.NotEmpty(c.Vouchers);
        foreach (var v in c.Vouchers)
        {
            Assert.True(v.Number > 0);
            Assert.Equal(v.Number.ToString(CultureInfo.InvariantCulture), c.FormatVoucherNumber(v));
        }

        // The second (inventory) engine's numbers render bare too (Bright carries Delivery/Receipt notes).
        foreach (var iv in c.InventoryVouchers)
            Assert.Equal(iv.Number.ToString(CultureInfo.InvariantCulture), c.FormatVoucherNumber(iv));

        // Read the repointed Day Book SITE for the empty-config fixtures: every rendered row number equals the bare int
        // of its voucher. (An empty-config read can only prove byte-identity — it cannot detect a missed repoint, which
        // is why WholeSurface_affix_everyLedgerSiteRendersAffix drives an AFFIX to make the sites bite.)
        foreach (var row in DayBook.Build(c, new DateOnly(1900, 1, 1), new DateOnly(2200, 1, 1)))
            if (c.FindVoucher(row.VoucherId) is { } v)
                Assert.Equal(v.Number.ToString(CultureInfo.InvariantCulture), row.FormattedNumber);
    }

    // ================================================================ (b) GSTR-1 B2B number == DocumentNumberOf

    [Fact]
    public void Gstr1Number_equalsDocumentNumber_underAffix()
    {
        var (c, gst, ledgers, sales, debtor, widgetId, main) = BuildGstCo();
        var affixSales = AffixSalesType("25-26/", width: 3);
        c.AddVoucherType(affixSales);

        var sale = PostB2BSale(c, gst, ledgers, affixSales.Id, sales, debtor, widgetId, main);
        Assert.Equal(1, sale.Number);

        var g = Gstr1.Build(c, FyStart, AsOf);
        var row = Assert.Single(g.B2B);
        // The ONE policy: BOTH the return's B2B document number AND the portal doc-no are the literal rendered string.
        // Pin each INDEPENDENTLY to the literal (STR-4: comparing the two production calls to each other is tautological —
        // they are the same call — and would stay green even if both regressed together).
        Assert.Equal("25-26/001", EInvoiceService.DocumentNumberOf(c, sale));
        Assert.Equal("25-26/001", row.InvoiceNumber);
    }

    // ================================================================ (c) inventory register renders the affix

    [Fact]
    public void InventoryRegister_rendersAffix()
    {
        var c = CompanyFactory.CreateSeeded("SJ Co", new DateOnly(2024, 4, 1));
        var masters = new InventoryService(c);
        var grp = masters.CreateStockGroup("Goods");
        var nos = masters.CreateSimpleUnit("Nos", "Numbers");
        var item = masters.CreateStockItem("Widget", grp.Id, nos.Id);
        var wh2 = masters.CreateGodown("Warehouse 2");
        masters.AddOpeningBalance(item.Id, c.MainLocation!.Id, 10m, Money.FromRupees(100m));

        // A Stock-Journal voucher type carrying a "SJ/" prefix (rules set in-memory, per the slice).
        var sjType = new VoucherType(Guid.NewGuid(), "SJ Prefixed", VoucherBaseType.StockJournal,
            prefixes: new[] { new VoucherNumberAffix(Guid.NewGuid(), new DateOnly(2024, 4, 1), "SJ/") });
        c.AddVoucherType(sjType);

        var posting = new InventoryPostingService(c);
        var posted = posting.Post(InventoryVoucher.StockJournal(Guid.NewGuid(), sjType.Id, new DateOnly(2024, 4, 10),
            source: new[] { new InventoryAllocation(item.Id, c.MainLocation!.Id, 6m, StockDirection.Outward) },
            destination: new[] { new InventoryAllocation(item.Id, wh2.Id, 6m, StockDirection.Inward) }));
        Assert.Equal(1, posted.Number);

        // The inventory REGISTER (not merely the accept toast) reads the affixed number "SJ/1".
        var movement = StockItemMovement.Build(c, item.Id, new DateOnly(2024, 4, 30));
        Assert.Contains(movement.Rows, r => r.FormattedNumber == "SJ/1");
        // And every rendered movement row of the stock journal carries the prefix (never the bare int).
        Assert.All(movement.Rows.Where(r => r.Number == 1), r => Assert.Equal("SJ/1", r.FormattedNumber));
    }

    // ================================================================ (d) duplicate guard: reject vs allow

    [Fact]
    public void PreventDuplicate_allowsLegitimateNumber()
    {
        var (c, _, ledgers, _, _, _, _) = BuildGstCo();
        var manual = new VoucherType(Guid.NewGuid(), "Manual Jrnl", VoucherBaseType.Journal,
            numbering: NumberingMethod.Manual, preventDuplicate: true);
        c.AddVoucherType(manual);
        var cashA = Add(c, "Cash A", "Cash-in-Hand", true);
        var cashB = Add(c, "Cash B", "Cash-in-Hand", true);

        // #5 posts; #6 is a legitimately DIFFERENT number ⇒ accepted (no false-reject: restart is deferred).
        ledgers.Post(ManualJournal(manual.Id, 5, cashA, cashB));
        var second = ledgers.Post(ManualJournal(manual.Id, 6, cashA, cashB));
        Assert.Equal(6, second.Number);
        Assert.Equal(2, c.Vouchers.Count(v => v.TypeId == manual.Id));
    }

    // ================================================================ (e) shared int, distinct prefix, both e-invoiced

    [Fact]
    public void TwoCoveredTypes_shareInt_distinctPrefix_bothEInvoice()
    {
        var (c, gst, ledgers, sales, debtor, widgetId, main) = BuildGstCo();
        var typeA = AffixSalesType("A/", width: 0);
        var typeB = AffixSalesType("B/", width: 0);
        c.AddVoucherType(typeA);
        c.AddVoucherType(typeB);

        // Both types' first voucher shares the bare int 1, but renders "A/1" vs "B/1".
        var saleA = PostB2BSale(c, gst, ledgers, typeA.Id, sales, debtor, widgetId, main);
        var saleB = PostB2BSale(c, gst, ledgers, typeB.Id, sales, debtor, widgetId, main);
        Assert.Equal(1, saleA.Number);
        Assert.Equal(1, saleB.Number);
        Assert.Equal("A/1", EInvoiceService.DocumentNumberOf(c, saleA));
        Assert.Equal("B/1", EInvoiceService.DocumentNumberOf(c, saleB));

        // The reuse guard keys on the RENDERED string, so distinct prefixes never false-block the second document.
        var svc = new EInvoiceService(c);
        var recA = svc.PrepareRecord(saleA);
        var recB = svc.PrepareRecord(saleB);
        Assert.Equal("A/1", recA.DocumentNumberUpper);
        Assert.Equal("B/1", recB.DocumentNumberUpper);
    }

    // ============================ STR-1 whole-surface (the biting half): under an AFFIX every Ledger render SITE differs
    // from the bare int, so reverting any one repoint (DayBook / Gstr1 / LedgerBook / StockItemMovement) to the bare int
    // turns THIS red — the check an empty-config test structurally cannot make.

    [Fact]
    public void WholeSurface_affix_everyLedgerSiteRendersAffix()
    {
        var (c, gst, ledgers, sales, debtor, widgetId, main) = BuildGstCo();
        var affixSales = AffixSalesType("25-26/", width: 3);
        c.AddVoucherType(affixSales);
        var sale = PostB2BSale(c, gst, ledgers, affixSales.Id, sales, debtor, widgetId, main);
        Assert.Equal(1, sale.Number);
        // The affix ≠ the bare int, which is what makes a reverted repoint detectable here.
        Assert.NotEqual("25-26/001", sale.Number.ToString(CultureInfo.InvariantCulture));

        // Day Book SITE (DayBook.cs).
        Assert.Equal("25-26/001", Assert.Single(DayBook.Build(c, FyStart, AsOf), r => r.VoucherId == sale.Id).FormattedNumber);
        // Ledger-Vouchers SITE (LedgerBook.cs) — read through the debtor account.
        var lb = LedgerBook.Build(c, debtor.Id, FyStart, AsOf);
        Assert.Contains(lb.Rows, r => r.FormattedNumber == "25-26/001");
        // GSTR-1 B2B SITE (Gstr1.cs).
        Assert.Equal("25-26/001", Assert.Single(Gstr1.Build(c, FyStart, AsOf).B2B).InvoiceNumber);
        // Inventory register SITE (StockItemMovement.cs) — the sale moved 1 widget outward.
        Assert.Contains(StockItemMovement.Build(c, widgetId, AsOf).Rows, r => r.FormattedNumber == "25-26/001");
    }

    // ============================ FIX-3: Table 9A secondary order must be NUMERIC (raw int), not ordinal string.

    [Fact]
    public void Gstr1Amendment_table9A_ordersByRawNumber_notOrdinalString()
    {
        var c = CompanyFactory.CreateSeeded("Amend Order Co", FyStart);
        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = "27", Gstin = GstinMaharashtra, RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart, Periodicity = GstReturnPeriodicity.Monthly,
        });
        var sales = Add(c, "Sales", "Sales Accounts", false);
        var debtor = Add(c, "Reg Debtor", "Sundry Debtors", true);
        debtor.PartyGst = new PartyGstDetails { RegistrationType = GstRegistrationType.Regular, Gstin = GstinGujarat, StateCode = "24" };

        // Two amended B2B invoices to ONE GSTIN, numbered 2 and 10 (empty config ⇒ they render "2" / "10"). Each is an
        // amendment: an April (prior filed) original re-stated in July (the current period).
        PostManualB2BSale(c, sales, debtor, 1000m, new(2025, 4, 5), number: 2);
        PostManualB2BSale(c, sales, debtor, 1200m, new(2025, 7, 5), number: 2);
        PostManualB2BSale(c, sales, debtor, 5000m, new(2025, 4, 6), number: 10);
        PostManualB2BSale(c, sales, debtor, 5200m, new(2025, 7, 6), number: 10);

        var amend = Gstr1Amendments.Build(c, new(2025, 7, 1), new(2025, 7, 31));
        // Same GSTIN ⇒ the party key ties, so the SECONDARY sort decides. It must be NUMERIC (2 before 10) — not ordinal
        // string, which would place "10" before "2" and break the empty-config byte-identity of the amendment order.
        Assert.Equal(new[] { "2", "10" }, amend.Table9A.Select(r => r.OriginalDocNumber).ToArray());
    }

    // ============================ FIX-1 / STR-3 (Ledger side): the B2C-QR reference is the AS-TYPED rendered doc no.

    [Fact]
    public void B2cQrReference_isAsTyped_lowercasePreserved()
    {
        var c = CompanyFactory.CreateSeeded("B2C Affix Co", FyStart);
        var gst = new GstService(c);
        gst.EnableGst(new GstConfig
        {
            HomeStateCode = "27", Gstin = GstinMaharashtra, RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart, Periodicity = GstReturnPeriodicity.Monthly,
            B2cDynamicQrEnabled = true, B2cQrUpiId = "apex@upi", B2cQrPayeeName = "Apex Traders",
        });
        var inv = new InventoryService(c);
        var grp = inv.CreateStockGroup("Goods");
        var nos = inv.CreateSimpleUnit("Nos", "Numbers", unitQuantityCode: "NOS");
        var main = c.MainLocation!.Id;
        var widget = inv.CreateStockItem("Widget", grp.Id, nos.Id);
        widget.Gst = new StockItemGstDetails { HsnSac = "847130", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800 };
        inv.AddOpeningBalance(widget.Id, main, 1000m, Money.FromRupees(40000m));
        var sales = Add(c, "Sales", "Sales Accounts", false);
        var consumer = Add(c, "Walk-in Consumer", "Sundry Debtors", true);
        consumer.PartyGst = new PartyGstDetails { RegistrationType = GstRegistrationType.Consumer, StateCode = "27" };

        var lowerType = AffixSalesType("inv/", width: 3);
        c.AddVoucherType(lowerType);
        var sale = PostB2BSale(c, gst, new LedgerService(c), lowerType.Id, sales, consumer, widget.Id, main);

        // A consumer party ⇒ a B2C supply; its self-generated UPI QR reference carries the AS-TYPED rendered doc no,
        // lowercase preserved — the same string print/Day Book render.
        var payload = new B2cQrService(c).BuildFor(sale, Money.FromRupees(6_000_000_000m));
        Assert.NotNull(payload);
        Assert.Equal("inv/001", payload!.Reference);
        Assert.Equal(c.FormatVoucherNumber(sale), payload.Reference);
    }

    // ============================ STR-2: Prevent-Duplicate on the SECOND (inventory) posting engine.

    [Fact]
    public void InventoryPreventDuplicate_rejectsCollision_allowsWhenFlagOff()
    {
        // ON: a Prevent-Duplicate Manual Stock-Journal type rejects a second voucher rendering the same number.
        var on = BuildSjCompany(preventDuplicate: true, out var onItem, out var onWh2, out var onType);
        var posting = new InventoryPostingService(on);
        posting.Post(StockJournalDup(onType.Id, onItem, on.MainLocation!.Id, onWh2, new DateOnly(2024, 4, 10), number: 7));
        var ex = Assert.Throws<InvalidOperationException>(() =>
            posting.Post(StockJournalDup(onType.Id, onItem, on.MainLocation!.Id, onWh2, new DateOnly(2024, 4, 11), number: 7)));
        Assert.Contains("already exists", ex.Message);
        Assert.Single(on.InventoryVouchers);

        // OFF: the identical collision is ACCEPTED (both post) — proving the guard, not some other rule, did the rejecting.
        var off = BuildSjCompany(preventDuplicate: false, out var offItem, out var offWh2, out var offType);
        var posting2 = new InventoryPostingService(off);
        posting2.Post(StockJournalDup(offType.Id, offItem, off.MainLocation!.Id, offWh2, new DateOnly(2024, 4, 10), number: 7));
        posting2.Post(StockJournalDup(offType.Id, offItem, off.MainLocation!.Id, offWh2, new DateOnly(2024, 4, 11), number: 7));
        Assert.Equal(2, off.InventoryVouchers.Count);
    }

    // ---------------------------------------------------------------- helpers

    private const string GstinGujarat = "24AAACC1206D1ZM";

    private static void PostManualB2BSale(Company c, Domain.Ledger sales, Domain.Ledger debtor, decimal taxable,
        DateOnly date, int number)
    {
        var gst = new GstService(c);
        var tax = gst.ComputeInvoiceTax(new[] { new GstService.TaxableLine(Money.FromRupees(taxable), 1800) }, interState: true, GstTaxDirection.Output);
        var lines = new List<EntryLine>
        {
            new(debtor.Id, new Money(taxable + tax.TotalTax.Amount), DrCr.Debit),
            new(sales.Id, Money.FromRupees(taxable), DrCr.Credit),
        };
        lines.AddRange(tax.TaxLines);
        var type = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id;
        new LedgerService(c).Post(new Voucher(Guid.NewGuid(), type, date, lines, number: number, partyId: debtor.Id));
    }

    private static Company BuildSjCompany(bool preventDuplicate, out Guid itemId, out Guid wh2Id, out VoucherType sjType)
    {
        var c = CompanyFactory.CreateSeeded("SJ Dup Co", new DateOnly(2024, 4, 1));
        var masters = new InventoryService(c);
        var grp = masters.CreateStockGroup("Goods");
        var nos = masters.CreateSimpleUnit("Nos", "Numbers");
        var item = masters.CreateStockItem("Widget", grp.Id, nos.Id);
        var wh2 = masters.CreateGodown("Warehouse 2");
        masters.AddOpeningBalance(item.Id, c.MainLocation!.Id, 100m, Money.FromRupees(1000m));
        sjType = new VoucherType(Guid.NewGuid(), "Manual SJ", VoucherBaseType.StockJournal,
            numbering: NumberingMethod.Manual, preventDuplicate: preventDuplicate);
        c.AddVoucherType(sjType);
        itemId = item.Id; wh2Id = wh2.Id;
        return c;
    }

    private static InventoryVoucher StockJournalDup(Guid typeId, Guid itemId, Guid from, Guid to, DateOnly date, int number) =>
        InventoryVoucher.StockJournal(Guid.NewGuid(), typeId, date,
            source: new[] { new InventoryAllocation(itemId, from, 1m, StockDirection.Outward) },
            destination: new[] { new InventoryAllocation(itemId, to, 1m, StockDirection.Inward) },
            number: number);

    private static (Company c, GstService gst, LedgerService ledgers, Domain.Ledger sales, Domain.Ledger debtor,
        Guid widgetId, Guid main) BuildGstCo()
    {
        var c = CompanyFactory.CreateSeeded("Numbering GST Co", FyStart);
        var gst = new GstService(c);
        gst.EnableGst(new GstConfig
        {
            HomeStateCode = "27", Gstin = GstinMaharashtra, RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart, Periodicity = GstReturnPeriodicity.Monthly,
            EInvoicingEnabled = true, EInvoiceApplicableFrom = FyStart,
        });

        var inv = new InventoryService(c);
        var grp = inv.CreateStockGroup("Goods");
        var nos = inv.CreateSimpleUnit("Nos", "Numbers", unitQuantityCode: "NOS");
        var main = c.MainLocation!.Id;
        var widget = inv.CreateStockItem("Widget", grp.Id, nos.Id);
        widget.Gst = new StockItemGstDetails { HsnSac = "847130", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800 };
        inv.AddOpeningBalance(widget.Id, main, 1000m, Money.FromRupees(40000m));

        var sales = Add(c, "Sales", "Sales Accounts", false);
        var debtor = Add(c, "Local Debtor", "Sundry Debtors", true);
        debtor.PartyGst = new PartyGstDetails { RegistrationType = GstRegistrationType.Regular, Gstin = GstinMaharashtra, StateCode = "27" };

        return (c, gst, new LedgerService(c), sales, debtor, widget.Id, main);
    }

    // A Sales voucher type carrying a date-effective prefix from FyStart (rules set in-memory, per slice S2).
    private static VoucherType AffixSalesType(string prefix, int width) =>
        new(Guid.NewGuid(), $"Sales {prefix}", VoucherBaseType.Sales,
            numberWidth: width, prefillWithZero: width > 0,
            prefixes: new[] { new VoucherNumberAffix(Guid.NewGuid(), FyStart, prefix) });

    // ₹50,000 @18% intra B2B item-invoice sale ⇒ CGST 4500 + SGST 4500, value ₹59,000.
    private static Voucher PostB2BSale(Company c, GstService gst, LedgerService ledgers, Guid salesTypeId,
        Domain.Ledger sales, Domain.Ledger debtor, Guid widgetId, Guid main)
    {
        var tax = gst.ComputeInvoiceTax(new[] { new GstService.TaxableLine(Money.FromRupees(50000m), 1800) },
            interState: false, GstTaxDirection.Output);
        var lines = new List<EntryLine>
        {
            new(debtor.Id, Money.FromRupees(59000m), DrCr.Debit),
            new(sales.Id, Money.FromRupees(50000m), DrCr.Credit),
        };
        lines.AddRange(tax.TaxLines);
        return ledgers.Post(new Voucher(Guid.NewGuid(), salesTypeId, SaleDate, lines, partyId: debtor.Id,
            inventoryLines: new[] { new VoucherInventoryLine(widgetId, main, 1m, Money.FromRupees(50000m)) }));
    }

    private static Voucher ManualJournal(Guid typeId, int number, Domain.Ledger dr, Domain.Ledger cr) =>
        new(Guid.NewGuid(), typeId, SaleDate, new List<EntryLine>
        {
            new(dr.Id, Money.FromRupees(100m), DrCr.Debit),
            new(cr.Id, Money.FromRupees(100m), DrCr.Credit),
        }, number: number);

    private static Domain.Ledger Add(Company c, string name, string groupName, bool openingIsDebit)
    {
        var l = new Domain.Ledger(Guid.NewGuid(), name, c.FindGroupByName(groupName)!.Id, Money.Zero, openingIsDebit);
        c.AddLedger(l);
        return l;
    }
}
