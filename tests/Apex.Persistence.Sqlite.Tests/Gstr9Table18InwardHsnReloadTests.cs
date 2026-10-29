using System.Text.Json;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Io;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Apex.Persistence.Sqlite;
using Xunit;
using Domain = Apex.Ledger.Domain;

namespace Apex.Persistence.Sqlite.Tests;

/// <summary>
/// 🔴 <b>Census 6.12 — GSTR-9 Table 18, the HSN-wise summary of INWARD supplies, was ABSENT.</b> The annual return
/// carried Table 17 (outward HSN) only; there was no inward HSN anywhere in <c>src/</c> (literal grep of
/// "InwardHsn" / "Table18" on <c>origin/main</c> e0b9116 returns nothing).
///
/// <para><b>Grounding.</b> The GST portal's GSTR-9 manual (tutorial.gst.gov.in, Manual &gt; GSTR-9, Table 18 "HSN
/// wise summary of Inward Supplies") lists the cells — HSN, UQC, Total Quantity, Total Taxable Value, Rate, Central /
/// Integrated / State-UT tax and Cess. The vendor's GST Annual Computation page
/// (help.tallysolutions.com/tally-prime/gst-reports/gst-annual-computation-tally/) says the HSN/SAC Summary of Inward
/// Supplies can be viewed "for the whole year".</para>
///
/// <para><b>Figures worked BY HAND</b> (intra-state 27→27). One MULTI-RATE purchase: Widget HSN 847130, 1,000 Nos ×
/// ₹33.33 = ₹33,330.00 @ 18% ⇒ CGST 2,999.70 + SGST 2,999.70; Bolt HSN 731815, 777 Nos × ₹11.11 = ₹8,632.47 @ 5%
/// ⇒ 2.5% = 215.81175 ⇒ CGST 215.81 + SGST 215.81. One UNLINKED Debit Note (purchase return): Widget 100 × ₹33.33 =
/// ₹3,333.00 @ 18% ⇒ CGST 299.97 + SGST 299.97 on the CREDIT side of Input. Table 18 therefore reads:</para>
/// <list type="bullet">
/// <item>731815 — 777 Nos, taxable ₹8,632.47, tax ₹431.62.</item>
/// <item>847130 — 900 Nos, taxable ₹29,997.00, tax 5,999.40 − 599.94 = ₹5,399.46.</item>
/// </list>
/// <para>Every figure is read off a report built from a <b>RELOADED</b> book, and the filed artefact is asserted as
/// the emitted JSON bytes.</para>
/// </summary>
public sealed class Gstr9Table18InwardHsnReloadTests
{
    private static readonly DateOnly FyFrom = new(2025, 4, 1);
    private static readonly DateOnly FyTo = new(2026, 3, 31);
    private const string GstinMaharashtra = "27AAPFU0939F1ZV";
    private const string GstinSupplier = "27AAACR5055K1Z5";

    [Fact]
    public void Table_18_lists_each_inward_hsn_with_its_own_rate_groups_tax_off_the_reloaded_book()
    {
        var r = Gstr9.Build(Reload(GoodsBook()), FyFrom, FyTo);

        Assert.Equal(new[] { "731815", "847130" }, r.Table18InwardHsn.Select(h => h.HsnSac).ToArray());

        var bolt = r.Table18InwardHsn.Single(h => h.HsnSac == "731815");
        Assert.Equal(777m, bolt.Quantity);
        Assert.Equal(8_632.47m, bolt.TaxableValue.Amount);
        Assert.Equal(215.81m, bolt.Cgst.Amount);
        Assert.Equal(215.81m, bolt.Sgst.Amount);
        Assert.Equal(0.00m, bolt.Igst.Amount);

        var widget = r.Table18InwardHsn.Single(h => h.HsnSac == "847130");
        Assert.Equal(900m, widget.Quantity);                    // 1,000 bought − 100 returned
        Assert.Equal(29_997.00m, widget.TaxableValue.Amount);   // a purchase return nets the row down
        Assert.Equal(2_699.73m, widget.Cgst.Amount);
        Assert.Equal(2_699.73m, widget.Sgst.Amount);

        Assert.Equal(38_629.47m, r.Table18TaxableValue.Amount);
        Assert.Equal(5_831.08m, r.Table18TotalTax.Amount);
    }

    /// <summary>The invariant against the BOOKS: Table 18's tax equals the Input tax ledgers' net FY movement — the
    /// purchases' debits less the purchase return's credits. Two projections cannot satisfy this by agreeing on the
    /// same wrong answer.</summary>
    [Fact]
    public void Table_18_tax_equals_the_input_tax_ledgers_net_movement()
    {
        var c = Reload(GoodsBook());
        var gst = new GstService(c);
        var books = NetDebit(c, gst.FindTaxLedger(GstTaxHead.Central, GstTaxDirection.Input)!.Id)
                  + NetDebit(c, gst.FindTaxLedger(GstTaxHead.State, GstTaxDirection.Input)!.Id);

        Assert.Equal(5_831.08m, books);
        Assert.Equal(books, Gstr9.Build(c, FyFrom, FyTo).Table18TotalTax.Amount);
    }

    /// <summary>🔴 Reverse charge: the RCM purchase posts the Input (ITC) pair AND the RCM Output LIABILITY pair, each
    /// carrying GST metadata. Only the former is the tax on the inward supply — ₹10,000 @ 18% ⇒ ₹1,800, not the
    /// ₹3,600 a reader of every GST leg would file.</summary>
    [Fact]
    public void A_reverse_charge_inward_service_files_its_tax_once()
    {
        var r = Gstr9.Build(Reload(RcmBook()), FyFrom, FyTo);

        var row = Assert.Single(r.Table18InwardHsn);
        Assert.Equal("998212", row.HsnSac);
        Assert.Equal(10_000.00m, row.TaxableValue.Amount);
        Assert.Equal(900.00m, row.Cgst.Amount);
        Assert.Equal(900.00m, row.Sgst.Amount);
        Assert.Equal(1_800.00m, row.TotalTax.Amount);
    }

    /// <summary>The FILED artefact: the offline GSTR-9 JSON emitted off the reloaded book carries a
    /// <c>tbl18_hsn</c> array with the paisa figures above.</summary>
    [Fact]
    public void The_emitted_gstr9_json_carries_table_18_in_paisa()
    {
        var bytes = GstReturnJson.Gstr9(Reload(GoodsBook()), FyFrom, FyTo);

        using var doc = JsonDocument.Parse(bytes);
        var rows = doc.RootElement.GetProperty("tbl18_hsn").EnumerateArray().ToList();
        Assert.Equal(2, rows.Count);

        var widget = rows.Single(e => e.GetProperty("hsn_sac").GetString() == "847130");
        Assert.Equal(2_999_700L, widget.GetProperty("taxable_value_paisa").GetInt64());
        Assert.Equal(269_973L, widget.GetProperty("cgst_paisa").GetInt64());
        Assert.Equal(269_973L, widget.GetProperty("sgst_paisa").GetInt64());

        var bolt = rows.Single(e => e.GetProperty("hsn_sac").GetString() == "731815");
        Assert.Equal(863_247L, bolt.GetProperty("taxable_value_paisa").GetInt64());
        Assert.Equal(21_581L, bolt.GetProperty("cgst_paisa").GetInt64());
    }

    /// <summary>ER-13: Table 17 (outward) is untouched — a purchase-only book files an empty Table 17.</summary>
    [Fact]
    public void Table_17_is_not_polluted_by_inward_supplies()
    {
        var r = Gstr9.Build(Reload(GoodsBook()), FyFrom, FyTo);
        Assert.Empty(r.Table17Hsn);
    }

    /// <summary>🔴 W8 review F1 — an OUTWARD §34 debit note raised against a CONSOLIDATED original reference (the
    /// voucher-entry panel's "Consolidated…" option, which stores <c>OriginalInvoiceVoucherId = null</c>). It is a
    /// DebitNote base type, so it reaches the inward sweep, but it is an upward revision of OUR sale and posts
    /// Output tax. Before the fix, Table 18 filed <c>998311 taxable −2,000.00, tax −360.00</c>. The book has a real
    /// inward purchase too, so the assertion is "exactly the purchase row", not merely "empty".</summary>
    [Fact]
    public void A_consolidated_outward_debit_note_files_no_inward_row_on_the_reloaded_book()
    {
        var c = Reload(OutwardDebitNoteBook(consolidated: true));
        var r = Gstr9.Build(c, FyFrom, FyTo);

        var row = Assert.Single(r.Table18InwardHsn);
        Assert.Equal("998212", row.HsnSac);                    // the genuine inward service only
        Assert.Equal(4_444.44m, row.TaxableValue.Amount);
        Assert.Equal(400.00m, row.Cgst.Amount);
        Assert.Equal(400.00m, row.Sgst.Amount);
        Assert.DoesNotContain(r.Table18InwardHsn, h => h.HsnSac == "998311");
        Assert.Equal(4_444.44m, r.Table18TaxableValue.Amount);
        Assert.Equal(800.00m, r.Table18TotalTax.Amount);

        // The filed bytes: no 998311 row, and the one row is the purchase in paisa.
        using var doc = JsonDocument.Parse(GstReturnJson.Gstr9(c, FyFrom, FyTo));
        var rows = doc.RootElement.GetProperty("tbl18_hsn").EnumerateArray().ToList();
        var only = Assert.Single(rows);
        Assert.Equal("998212", only.GetProperty("hsn_sac").GetString());
        Assert.Equal(444_444L, only.GetProperty("taxable_value_paisa").GetInt64());
    }

    /// <summary>W8 review F1, the wider shape: an UNLINKED Debit Note to a customer (the §34 toggle is opt-in) that
    /// carries Output tax legs is not an inward supply either.</summary>
    [Fact]
    public void An_unlinked_debit_note_to_a_customer_files_no_inward_row_on_the_reloaded_book()
    {
        var r = Gstr9.Build(Reload(OutwardDebitNoteBook(consolidated: false)), FyFrom, FyTo);

        var row = Assert.Single(r.Table18InwardHsn);
        Assert.Equal("998212", row.HsnSac);
        Assert.Equal(800.00m, r.Table18TotalTax.Amount);
    }

    // ================================================================ helpers

    /// <summary>A service sale ₹10,000 @ 18% on SAC 998311, an inward service purchase ₹4,444.44 @ 18% on SAC
    /// 998212 (tax 18% × 4,444.44 = 799.9992 ⇒ 800.00, split total-then-halve to 400.00 + 400.00), and an outward Debit Note ₹2,000 @ 18%
    /// to the customer — either a §34 note against a CONSOLIDATED reference, or unlinked.</summary>
    private static Company OutwardDebitNoteBook(bool consolidated)
    {
        var c = NewRegular("Outward DN Co");
        var gst = new GstService(c);
        var ledgers = new LedgerService(c);
        Guid TypeOf(VoucherBaseType b) => c.VoucherTypes.First(t => t.BaseType == b).Id;

        var income = Add(c, "Consulting Income", "Sales Accounts", false);
        income.SalesPurchaseGst = new StockItemGstDetails
        {
            HsnSac = "998311", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800, SupplyType = GstSupplyType.Services,
        };
        var client = Add(c, "Client", "Sundry Debtors", true);
        client.PartyGst = new PartyGstDetails { RegistrationType = GstRegistrationType.Regular, Gstin = GstinSupplier, StateCode = "27" };

        var fees = Add(c, "Professional Fees", "Indirect Expenses", true);
        fees.SalesPurchaseGst = new StockItemGstDetails
        {
            HsnSac = "998212", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800, SupplyType = GstSupplyType.Services,
        };
        var vendor = Add(c, "Consultant", "Sundry Creditors", false);
        vendor.PartyGst = new PartyGstDetails { RegistrationType = GstRegistrationType.Regular, Gstin = GstinSupplier, StateCode = "27" };

        // ---- the sale.
        var st = gst.ComputeInvoiceTax(new[] { new GstService.TaxableLine(Money.FromRupees(10_000m), 1800) },
            interState: false, GstTaxDirection.Output);
        var sl = new List<EntryLine>
        {
            new(client.Id, Money.FromRupees(10_000m + st.TotalTax.Amount), DrCr.Debit),
            new(income.Id, Money.FromRupees(10_000m), DrCr.Credit),
        };
        sl.AddRange(st.TaxLines);
        ledgers.Post(new Voucher(Guid.NewGuid(), TypeOf(VoucherBaseType.Sales), new(2025, 5, 10), sl, partyId: client.Id));

        // ---- the genuine inward service.
        var pt = gst.ComputeInvoiceTax(new[] { new GstService.TaxableLine(Money.FromRupees(4_444.44m), 1800) },
            interState: false, GstTaxDirection.Input);
        Assert.Equal(800.00m, pt.TotalTax.Amount);   // the fixture's premise: 18% of 4,444.44 = 799.9992, total-then-split
        var pl = new List<EntryLine>
        {
            new(fees.Id, Money.FromRupees(4_444.44m), DrCr.Debit),
            new(vendor.Id, Money.FromRupees(4_444.44m + pt.TotalTax.Amount), DrCr.Credit),
        };
        pl.AddRange(pt.TaxLines);
        ledgers.Post(new Voucher(Guid.NewGuid(), TypeOf(VoucherBaseType.Purchase), new(2025, 5, 12), pl, partyId: vendor.Id));

        // ---- the OUTWARD debit note: Output legs on the Credit side (an increase of our sale's tax).
        var dnId = Guid.NewGuid();
        IReadOnlyList<EntryLine> dnTax;
        decimal dnTotal;
        if (consolidated)
        {
            var posting = new CreditDebitNoteService(c).BuildCreditDebitNote(CdnType.Debit,
                new[] { new GstService.TaxableLine(Money.FromRupees(2_000m), 1800) }, interState: false, dnId,
                originalInvoiceVoucherId: null, originalInvoiceNumber: "CONSOLIDATED-MAY",
                originalInvoiceDate: new DateOnly(2025, 5, 10), cdnDate: new DateOnly(2025, 6, 10),
                reasonCode: "04 Correction in invoice");
            Assert.Null(posting.Link.OriginalInvoiceVoucherId);   // the consolidated shape, held at build time
            dnTax = posting.TaxLines; dnTotal = posting.Computed.TotalTax.Amount;
        }
        else
        {
            var t = gst.ComputeInvoiceTax(new[] { new GstService.TaxableLine(Money.FromRupees(2_000m), 1800) },
                interState: false, GstTaxDirection.Output);
            dnTax = t.TaxLines; dnTotal = t.TotalTax.Amount;
        }
        Assert.Equal(360m, dnTotal);
        var dl = new List<EntryLine>
        {
            new(client.Id, Money.FromRupees(2_000m + dnTotal), DrCr.Debit),
            new(income.Id, Money.FromRupees(2_000m), DrCr.Credit),
        };
        dl.AddRange(dnTax);
        ledgers.Post(new Voucher(dnId, TypeOf(VoucherBaseType.DebitNote), new(2025, 6, 10), dl, partyId: client.Id));
        return c;
    }

    private static decimal NetDebit(Company c, Guid ledgerId) =>
        c.Vouchers.Where(v => v.Date >= FyFrom && v.Date <= FyTo)
            .SelectMany(v => v.Lines).Where(l => l.LedgerId == ledgerId)
            .Sum(l => l.Side == DrCr.Debit ? l.Amount.Amount : -l.Amount.Amount);

    private static Company Reload(Company original)
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"apex-g9-t18-{Guid.NewGuid():N}.db");
        try
        {
            using (var write = new SqliteCompanyStore(dbPath))
                write.Save(original);
            using var read = new SqliteCompanyStore(dbPath);
            return read.Load(original.Id)!;
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { File.Delete(dbPath); } catch (IOException) { }
        }
    }

    private static Company NewRegular(string name)
    {
        var c = CompanyFactory.CreateSeeded(name, FyFrom);
        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = "27",
            Gstin = GstinMaharashtra,
            RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyFrom,
            Periodicity = GstReturnPeriodicity.Monthly,
        });
        return c;
    }

    private static Company GoodsBook()
    {
        var c = NewRegular("Inward HSN Co");
        var gst = new GstService(c);
        var inv = new InventoryService(c);
        var ledgers = new LedgerService(c);
        var grp = inv.CreateStockGroup("Goods");
        var nos = inv.CreateSimpleUnit("Nos", "Numbers", unitQuantityCode: "NOS");
        var main = c.MainLocation!.Id;

        var widget = inv.CreateStockItem("Widget", grp.Id, nos.Id);
        widget.Gst = new StockItemGstDetails { HsnSac = "847130", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800 };
        var bolt = inv.CreateStockItem("Bolt", grp.Id, nos.Id);
        bolt.Gst = new StockItemGstDetails { HsnSac = "731815", Taxability = GstTaxability.Taxable, RateBasisPoints = 500 };

        var purchases = Add(c, "Purchases", "Purchase Accounts", true);
        var supplier = Add(c, "Supplier", "Sundry Creditors", false);
        supplier.PartyGst = new PartyGstDetails { RegistrationType = GstRegistrationType.Regular, Gstin = GstinSupplier, StateCode = "27" };
        Guid TypeOf(VoucherBaseType b) => c.VoucherTypes.First(t => t.BaseType == b).Id;

        // ---- one MULTI-RATE purchase.
        var tax = gst.ComputeInvoiceTax(new[]
        {
            new GstService.TaxableLine(Money.FromRupees(33_330.00m), 1800),
            new GstService.TaxableLine(Money.FromRupees(8_632.47m), 500),
        }, interState: false, GstTaxDirection.Input);
        Assert.Equal(5_999.40m + 431.62m, tax.TotalTax.Amount);   // the fixture's premise, held at build time
        const decimal value = 33_330.00m + 8_632.47m;
        var lines = new List<EntryLine>
        {
            new(purchases.Id, Money.FromRupees(value), DrCr.Debit),
            new(supplier.Id, Money.FromRupees(value + tax.TotalTax.Amount), DrCr.Credit),
        };
        lines.AddRange(tax.TaxLines);
        ledgers.Post(new Voucher(Guid.NewGuid(), TypeOf(VoucherBaseType.Purchase), new(2025, 5, 3), lines,
            partyId: supplier.Id, inventoryLines: new[]
            {
                new VoucherInventoryLine(widget.Id, main, 1_000m, Money.FromRupees(33.33m)),
                new VoucherInventoryLine(bolt.Id, main, 777m, Money.FromRupees(11.11m)),
            }));

        // ---- one UNLINKED Debit Note (purchase return): every tax leg on the CREDIT side of Input.
        var dnTax = gst.ComputeInvoiceTax(new[] { new GstService.TaxableLine(Money.FromRupees(3_333.00m), 1800) },
            interState: false, GstTaxDirection.Input, reverseSides: true);
        Assert.Equal(599.94m, dnTax.TotalTax.Amount);
        var dnLines = new List<EntryLine>
        {
            new(supplier.Id, Money.FromRupees(3_333.00m + dnTax.TotalTax.Amount), DrCr.Debit),
            new(purchases.Id, Money.FromRupees(3_333.00m), DrCr.Credit),
        };
        dnLines.AddRange(dnTax.TaxLines);
        ledgers.Post(new Voucher(Guid.NewGuid(), TypeOf(VoucherBaseType.DebitNote), new(2025, 6, 9), dnLines,
            partyId: supplier.Id, inventoryLines: new[]
            {
                new VoucherInventoryLine(widget.Id, main, 100m, Money.FromRupees(33.33m)),
            }));

        return c;
    }

    private static Company RcmBook()
    {
        var c = NewRegular("Inward RCM Co");
        new GstService(c).SeedAdvancedGst();   // the notified RCM categories
        var legal = Add(c, "Legal Fees", "Indirect Expenses", true);
        legal.SalesPurchaseGst = new StockItemGstDetails
        {
            HsnSac = "998212", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800,
            SupplyType = GstSupplyType.Services, ReverseChargeApplicable = true,
            RcmCategoryId = c.Gst!.RcmCategories.First(x => x.SupplyNature == "Legal").Id,
        };
        var advocate = Add(c, "Advocate", "Sundry Creditors", false);
        advocate.PartyGst = new PartyGstDetails { RegistrationType = GstRegistrationType.Regular, Gstin = GstinSupplier, StateCode = "27" };

        var date = new DateOnly(2025, 7, 14);
        var posting = new RcmService(c).BuildReverseCharge(
            Money.FromRupees(10_000m), null, legal, advocate.PartyGst, date, RcmService.SupplyKind.Domestic);
        Assert.True(posting.Applies);
        var lines = new List<EntryLine>
        {
            new(legal.Id, Money.FromRupees(10_000m), DrCr.Debit),
            new(advocate.Id, Money.FromRupees(10_000m), DrCr.Credit),
        };
        lines.AddRange(posting.Lines);
        new LedgerService(c).Post(new Voucher(Guid.NewGuid(),
            c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase).Id, date, lines, partyId: advocate.Id));
        return c;
    }

    private static Domain.Ledger Add(Company c, string name, string groupName, bool openingIsDebit)
    {
        var l = new Domain.Ledger(Guid.NewGuid(), name, c.FindGroupByName(groupName)!.Id, Money.Zero, openingIsDebit);
        c.AddLedger(l);
        return l;
    }
}
