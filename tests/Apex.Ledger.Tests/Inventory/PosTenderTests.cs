using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests.Inventory;

/// <summary>
/// POS (single/multi-tender) engine tests (catalog §11; Phase 6 slice 7 RQ-38..RQ-44; TOP RISK #6; PR-9; DP-6).
/// A POS bill is a Sales item-invoice whose single customer debit is replaced by a split of tender debits; the
/// credit side (Cr Sales + Cr Output CGST/SGST) and the stock movement are byte-identical to a normal sale, so
/// GST reuses the Phase-4 engine unchanged. Proves: cash residual + change math; the reconciliation invariant
/// (Σ tenders == bill total); the load-bearing tender-ledger GROUPING (Gift → Sundry Debtors, Card/Cheque → Bank,
/// Cash → Cash-in-Hand); over-tender rejection; the PR-9 worked example (multi- AND single-tender both foot to
/// ₹12,065.50 with identical Sales+GST credits, change ₹34.50 both ways, cash posts the residual NOT the tendered);
/// and that a non-POS sale is unaffected. All pure, deterministic, paisa-exact.
/// </summary>
public class PosTenderTests
{
    private static readonly DateOnly FyStart = new(2024, 4, 1);
    private static readonly DateOnly D1 = new(2024, 4, 5);
    private static readonly DateOnly D2 = new(2024, 4, 10);
    private const string GstinMaharashtra = "27AAPFU0939F1ZV";

    private sealed class Kit
    {
        public required Company Company { get; init; }
        public required LedgerService Ledgers { get; init; }
        public required GstService Gst { get; init; }
        public required Guid ItemId { get; init; }
        public required Guid MainId { get; init; }
        public required Domain.Ledger Sales { get; init; }
        public required Domain.Ledger Gift { get; init; }
        public required Domain.Ledger Card { get; init; }
        public required Domain.Ledger Cheque { get; init; }
        public required Domain.Ledger Cash { get; init; }
        public required Guid PosTypeId { get; init; }
    }

    private static Domain.Ledger AddLedger(Company c, string name, string groupName, bool openingIsDebit)
    {
        var l = new Domain.Ledger(Guid.NewGuid(), name, c.FindGroupByName(groupName)!.Id, Money.Zero, openingIsDebit);
        c.AddLedger(l);
        return l;
    }

    // A GST company with an 18%-taxable item, a POS-flagged Sales type, tender ledgers under the required groups,
    // and enough stock on hand to sell.
    private static Kit NewKit()
    {
        var c = CompanyFactory.CreateSeeded("POS Retail Co", FyStart);
        var gst = new GstService(c);
        gst.EnableGst(new GstConfig
        {
            HomeStateCode = "27",
            Gstin = GstinMaharashtra,
            RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart,
            Periodicity = GstReturnPeriodicity.Monthly,
        });

        var masters = new InventoryService(c);
        var grp = masters.CreateStockGroup("Goods");
        var nos = masters.CreateSimpleUnit("Nos", "Numbers");
        var item = masters.CreateStockItem("Widget", grp.Id, nos.Id, valuationMethod: StockValuationMethod.Fifo);
        item.Gst = new StockItemGstDetails { Taxability = GstTaxability.Taxable, RateBasisPoints = 1800 };
        var main = c.MainLocation!.Id;

        var sales = AddLedger(c, "Sales (POS)", "Sales Accounts", openingIsDebit: false);
        var gift = AddLedger(c, "Gift Voucher", "Sundry Debtors", openingIsDebit: true);
        var card = AddLedger(c, "ICICI Credit Card", "Bank Accounts", openingIsDebit: true);
        var cheque = AddLedger(c, "SBI Cheque", "Bank Accounts", openingIsDebit: true);
        var cash = AddLedger(c, "Cash", "Cash-in-Hand", openingIsDebit: true);

        // A user-created POS-flagged Sales voucher type (RQ-38).
        var posType = new VoucherType(Guid.NewGuid(), "Sales (POS)", VoucherBaseType.Sales, useForPos: true,
            posConfig: new PosConfig { PrintAfterSave = true });
        c.AddVoucherType(posType);

        var ledgers = new LedgerService(c);

        // Stock the shelf: buy 10 @ ₹2000 so we can sell one @ ₹10,225 taxable (on-hand ≥ 1).
        var purchases = AddLedger(c, "Purchases", "Purchase Accounts", openingIsDebit: true);
        var creditor = AddLedger(c, "Creditor", "Sundry Creditors", openingIsDebit: false);
        ledgers.Post(new Voucher(Guid.NewGuid(), c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase).Id, D1,
            new[] { new EntryLine(purchases.Id, Money.FromRupees(20000m), DrCr.Debit), new EntryLine(creditor.Id, Money.FromRupees(20000m), DrCr.Credit) },
            inventoryLines: new[] { new VoucherInventoryLine(item.Id, main, 10m, Money.FromRupees(2000m)) }));

        return new Kit
        {
            Company = c, Ledgers = ledgers, Gst = gst, ItemId = item.Id, MainId = main,
            Sales = sales, Gift = gift, Card = card, Cheque = cheque, Cash = cash, PosTypeId = posType.Id,
        };
    }

    // Assembles the PR-9 POS bill (taxable ₹10,225 @ 18% intra ⇒ CGST 920.25 + SGST 920.25, bill total 12,065.50)
    // and posts it with the given tender split. The item line sells 1 @ ₹10,225.
    private Voucher PostPos(Kit k, IReadOnlyList<PosTender> tenders)
    {
        var tax = k.Gst.ComputeInvoiceTax(
            new[] { new GstService.TaxableLine(Money.FromRupees(10225m), 1800) }, interState: false, GstTaxDirection.Output);
        var lines = new List<EntryLine>();
        lines.AddRange(PosTenderService.BuildTenderDebitLines(tenders)); // the tender debits (replace the customer Dr)
        lines.Add(new EntryLine(k.Sales.Id, Money.FromRupees(10225m), DrCr.Credit)); // Cr Sales = taxable
        lines.AddRange(tax.TaxLines);                                                // Cr Output CGST/SGST
        return k.Ledgers.Post(new Voucher(Guid.NewGuid(), k.PosTypeId, D2, lines,
            inventoryLines: new[] { new VoucherInventoryLine(k.ItemId, k.MainId, 1m, Money.FromRupees(10225m)) },
            posTenders: tenders));
    }

    private static IReadOnlyList<PosTender> MultiTender(Kit k)
    {
        var residual = PosTenderService.CashResidual(
            Money.FromRupees(12065.50m), Money.FromRupees(500m), Money.FromRupees(5000m), Money.FromRupees(5000m));
        var change = PosTenderService.Change(Money.FromRupees(1600m), residual);
        return new[]
        {
            new PosTender(PosTenderType.GiftVoucher, k.Gift.Id, Money.FromRupees(500m)),
            new PosTender(PosTenderType.Card, k.Card.Id, Money.FromRupees(5000m), CardNo: "4111"),
            new PosTender(PosTenderType.Cheque, k.Cheque.Id, Money.FromRupees(5000m), BankName: "SBI", ChequeNo: "235681"),
            new PosTender(PosTenderType.Cash, k.Cash.Id, residual, Tendered: Money.FromRupees(1600m), Change: change),
        };
    }

    // ---------------------------------------------------------------- residual / change math (RQ-39/RQ-40)

    [Fact]
    public void Cash_residual_is_bill_total_minus_non_cash_tenders()
    {
        Assert.Equal(Money.FromRupees(1565.50m), PosTenderService.CashResidual(
            Money.FromRupees(12065.50m), Money.FromRupees(500m), Money.FromRupees(5000m), Money.FromRupees(5000m)));
    }

    [Fact]
    public void Change_is_cash_tendered_minus_cash_payable()
    {
        Assert.Equal(Money.FromRupees(34.50m), PosTenderService.Change(Money.FromRupees(1600m), Money.FromRupees(1565.50m)));
    }

    [Fact]
    public void Non_cash_over_tender_is_rejected()
    {
        // Gift+Card+Cheque = 12,600 > bill total 12,065.50 ⇒ negative residual ⇒ reject.
        var ex = Assert.Throws<InvalidVoucherException>(() => PosTenderService.CashResidual(
            Money.FromRupees(12065.50m), Money.FromRupees(2600m), Money.FromRupees(5000m), Money.FromRupees(5000m)));
        Assert.Contains("over-pay", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Cash_tendered_short_of_payable_is_rejected()
    {
        Assert.Throws<InvalidVoucherException>(() =>
            PosTenderService.Change(Money.FromRupees(1000m), Money.FromRupees(1565.50m)));
    }

    // ---------------------------------------------------------------- reconciliation (RQ-40)

    [Fact]
    public void Ensure_balanced_accepts_a_tender_split_that_foots_to_the_bill()
    {
        var k = NewKit();
        PosTenderService.EnsureBalanced(Money.FromRupees(12065.50m), MultiTender(k)); // no throw
    }

    [Fact]
    public void Ensure_balanced_rejects_a_tender_split_that_does_not_foot()
    {
        var k = NewKit();
        var tenders = new[] { new PosTender(PosTenderType.Cash, k.Cash.Id, Money.FromRupees(12000m),
            Tendered: Money.FromRupees(12000m), Change: Money.Zero) };
        var ex = Assert.Throws<InvalidVoucherException>(() =>
            PosTenderService.EnsureBalanced(Money.FromRupees(12065.50m), tenders));
        Assert.Contains("reconcile", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ---------------------------------------------------------------- grouping negatives (RQ-41, LOAD-BEARING)

    [Fact]
    public void Grouping_accepts_correctly_grouped_tenders()
    {
        var k = NewKit();
        PosTenderService.EnsureGrouping(k.Company, MultiTender(k)); // no throw
    }

    [Fact]
    public void Gift_ledger_not_under_sundry_debtors_is_rejected()
    {
        var k = NewKit();
        var bad = new[] { new PosTender(PosTenderType.GiftVoucher, k.Sales.Id, Money.FromRupees(500m)) };
        var ex = Assert.Throws<InvalidVoucherException>(() => PosTenderService.EnsureGrouping(k.Company, bad));
        Assert.Contains("Sundry Debtors", ex.Message);
    }

    [Fact]
    public void Card_or_cheque_ledger_not_a_bank_is_rejected()
    {
        var k = NewKit();
        var badCard = new[] { new PosTender(PosTenderType.Card, k.Cash.Id, Money.FromRupees(500m)) };
        Assert.Throws<InvalidVoucherException>(() => PosTenderService.EnsureGrouping(k.Company, badCard));
        var badCheque = new[] { new PosTender(PosTenderType.Cheque, k.Gift.Id, Money.FromRupees(500m)) };
        Assert.Throws<InvalidVoucherException>(() => PosTenderService.EnsureGrouping(k.Company, badCheque));
    }

    [Fact]
    public void Cash_ledger_not_cash_in_hand_is_rejected()
    {
        var k = NewKit();
        var bad = new[] { new PosTender(PosTenderType.Cash, k.Card.Id, Money.FromRupees(500m),
            Tendered: Money.FromRupees(500m), Change: Money.Zero) };
        var ex = Assert.Throws<InvalidVoucherException>(() => PosTenderService.EnsureGrouping(k.Company, bad));
        Assert.Contains("Cash-in-Hand", ex.Message);
    }

    // ---------------------------------------------------------------- PR-9 hard gate: multi-tender

    [Fact]
    public void Pr9_multi_tender_posts_each_tender_debit_and_identical_sales_and_gst_credits()
    {
        var k = NewKit();
        var v = PostPos(k, MultiTender(k));

        Assert.True(VoucherValidator.IsBalanced(v));
        Assert.Equal(Money.FromRupees(12065.50m), v.TotalDebit);
        Assert.Equal(Money.FromRupees(12065.50m), v.TotalCredit);

        // Each tender debits its correctly-grouped ledger for its posted share.
        Assert.Equal(500m, LedgerBalances.SignedClosing(k.Company, k.Gift, D2));
        Assert.Equal(5000m, LedgerBalances.SignedClosing(k.Company, k.Card, D2));
        Assert.Equal(5000m, LedgerBalances.SignedClosing(k.Company, k.Cheque, D2));
        // Cash posts the RESIDUAL (1,565.50), NOT the tendered 1,600 — the change (34.50) never hits the books.
        Assert.Equal(1565.50m, LedgerBalances.SignedClosing(k.Company, k.Cash, D2));

        // Sales + GST credits are byte-identical to a normal sale (taxable 10,225; CGST/SGST 920.25 each).
        Assert.Equal(-10225m, LedgerBalances.SignedClosing(k.Company, k.Sales, D2));
        Assert.Equal(-920.25m, LedgerBalances.SignedClosing(k.Company, k.Gst.FindTaxLedger(GstTaxHead.Central, GstTaxDirection.Output)!, D2));
        Assert.Equal(-920.25m, LedgerBalances.SignedClosing(k.Company, k.Gst.FindTaxLedger(GstTaxHead.State, GstTaxDirection.Output)!, D2));

        // Pairing invariant held (ER-8): Σ item value == Σ Sales credit == 10,225; the tender debits don't perturb it.
        Assert.Equal(Money.FromRupees(10225m), v.InventoryLinesValue);
        // The informational change is carried on the cash tender but never posted.
        var cashTender = v.PosTenders.Single(t => t.Type == PosTenderType.Cash);
        Assert.Equal(Money.FromRupees(34.50m), cashTender.Change);
        Assert.Equal(Money.FromRupees(1600m), cashTender.Tendered);
        // Stock moved out by 1 (10 bought − 1 sold = 9 on hand).
        Assert.Equal(9m, new InventoryLedger(k.Company).OnHand(k.ItemId, k.MainId, D2));
    }

    // ---------------------------------------------------------------- PR-9 hard gate: single-tender parity

    [Fact]
    public void Pr9_single_tender_posts_identical_sales_and_gst_credits_with_change_34_50()
    {
        var k = NewKit();
        // Single-mode: one Cash tender for the full bill; tendered 12,100 → change 34.50.
        var change = PosTenderService.Change(Money.FromRupees(12100m), Money.FromRupees(12065.50m));
        Assert.Equal(Money.FromRupees(34.50m), change);
        var single = new[] { new PosTender(PosTenderType.Cash, k.Cash.Id, Money.FromRupees(12065.50m),
            Tendered: Money.FromRupees(12100m), Change: change) };
        var v = PostPos(k, single);

        Assert.True(VoucherValidator.IsBalanced(v));
        // Cash posts the full bill (12,065.50), NOT the tendered 12,100.
        Assert.Equal(12065.50m, LedgerBalances.SignedClosing(k.Company, k.Cash, D2));
        // Identical Sales + GST credits as the multi-tender case (only the debit split differs).
        Assert.Equal(-10225m, LedgerBalances.SignedClosing(k.Company, k.Sales, D2));
        Assert.Equal(-920.25m, LedgerBalances.SignedClosing(k.Company, k.Gst.FindTaxLedger(GstTaxHead.Central, GstTaxDirection.Output)!, D2));
        Assert.Equal(-920.25m, LedgerBalances.SignedClosing(k.Company, k.Gst.FindTaxLedger(GstTaxHead.State, GstTaxDirection.Output)!, D2));
    }

    // ---------------------------------------------------------------- validator gate (posting path)

    [Fact]
    public void Posting_a_misgrouped_tender_split_is_rejected_and_persists_nothing()
    {
        var k = NewKit();
        // Gift on the Sales ledger (wrong group) — the posting must throw and persist nothing.
        var bad = new[]
        {
            new PosTender(PosTenderType.GiftVoucher, k.Sales.Id, Money.FromRupees(500m)),
            new PosTender(PosTenderType.Card, k.Card.Id, Money.FromRupees(5000m)),
            new PosTender(PosTenderType.Cheque, k.Cheque.Id, Money.FromRupees(5000m)),
            new PosTender(PosTenderType.Cash, k.Cash.Id, Money.FromRupees(1565.50m),
                Tendered: Money.FromRupees(1600m), Change: Money.FromRupees(34.50m)),
        };
        Assert.Throws<InvalidVoucherException>(() => PostPos(k, bad));
        // Cash never moved (nothing persisted from the failed voucher).
        Assert.Equal(0m, LedgerBalances.SignedClosing(k.Company, k.Cash, D2));
    }

    [Fact]
    public void Pos_tenders_on_a_non_pos_sales_type_are_rejected()
    {
        var k = NewKit();
        var plainSales = k.Company.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales && !t.UseForPos).Id;
        var tenders = MultiTender(k);
        var tax = k.Gst.ComputeInvoiceTax(new[] { new GstService.TaxableLine(Money.FromRupees(10225m), 1800) },
            interState: false, GstTaxDirection.Output);
        var lines = new List<EntryLine>();
        lines.AddRange(PosTenderService.BuildTenderDebitLines(tenders));
        lines.Add(new EntryLine(k.Sales.Id, Money.FromRupees(10225m), DrCr.Credit));
        lines.AddRange(tax.TaxLines);
        var ex = Assert.Throws<InvalidVoucherException>(() => k.Ledgers.Post(new Voucher(
            Guid.NewGuid(), plainSales, D2, lines,
            inventoryLines: new[] { new VoucherInventoryLine(k.ItemId, k.MainId, 1m, Money.FromRupees(10225m)) },
            posTenders: tenders)));
        Assert.Contains("POS", ex.Message);
    }

    // ---------------------------------------------------------------- POS Register report (RQ-44; DP-6)

    [Fact]
    public void Pos_register_decomposes_the_bill_into_tender_columns_and_foots_totals()
    {
        var k = NewKit();
        PostPos(k, MultiTender(k));   // the PR-9 multi-tender bill on D2

        var reg = PosRegister.Build(k.Company, FyStart, new DateOnly(2024, 4, 30));
        var row = Assert.Single(reg.Rows);

        // The bill is decomposed into per-tender POSTED shares (Cash = residual 1,565.50, not the tendered 1,600).
        Assert.Equal(Money.FromRupees(500m), row.Gift);
        Assert.Equal(Money.FromRupees(5000m), row.Card);
        Assert.Equal(Money.FromRupees(5000m), row.Cheque);
        Assert.Equal(Money.FromRupees(1565.50m), row.Cash);
        Assert.Equal(Money.FromRupees(12065.50m), row.BillTotal);   // = Σ tender = the voucher debit total

        // Grand totals foot the single row.
        Assert.Equal(Money.FromRupees(500m), reg.TotalGift);
        Assert.Equal(Money.FromRupees(5000m), reg.TotalCard);
        Assert.Equal(Money.FromRupees(5000m), reg.TotalCheque);
        Assert.Equal(Money.FromRupees(1565.50m), reg.TotalCash);
        Assert.Equal(Money.FromRupees(12065.50m), reg.TotalBill);
    }

    [Fact]
    public void Pos_register_day_filter_excludes_out_of_range_bills_and_non_pos_sales()
    {
        var k = NewKit();
        PostPos(k, MultiTender(k));   // POS bill dated D2 (2024-04-10)

        // A normal (non-POS) sale on the same day must NOT appear in the POS register.
        var debtor = AddLedger(k.Company, "Debtor", "Sundry Debtors", openingIsDebit: true);
        k.Ledgers.Post(new Voucher(Guid.NewGuid(),
            k.Company.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales && !t.UseForPos).Id, D2,
            new[]
            {
                new EntryLine(debtor.Id, Money.FromRupees(2000m), DrCr.Debit),
                new EntryLine(k.Sales.Id, Money.FromRupees(2000m), DrCr.Credit),
            },
            inventoryLines: new[] { new VoucherInventoryLine(k.ItemId, k.MainId, 1m, Money.FromRupees(2000m)) }));

        // Day filter that ends BEFORE the POS bill's date yields no rows.
        var before = PosRegister.Build(k.Company, FyStart, new DateOnly(2024, 4, 9));
        Assert.Empty(before.Rows);

        // A filter covering D2 yields exactly the one POS bill (the normal sale is excluded).
        var covering = PosRegister.Build(k.Company, D2, D2);
        var row = Assert.Single(covering.Rows);
        Assert.Equal(Money.FromRupees(12065.50m), row.BillTotal);
    }

    // ---------------------------------------------------------------- ER-13: a non-POS sale is unaffected

    [Fact]
    public void A_normal_sale_carries_no_pos_tenders()
    {
        var k = NewKit();
        var debtor = AddLedger(k.Company, "Debtor", "Sundry Debtors", openingIsDebit: true);
        var v = k.Ledgers.Post(new Voucher(Guid.NewGuid(), k.Company.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales && !t.UseForPos).Id, D2,
            new[]
            {
                new EntryLine(debtor.Id, Money.FromRupees(2000m), DrCr.Debit),
                new EntryLine(k.Sales.Id, Money.FromRupees(2000m), DrCr.Credit),
            },
            inventoryLines: new[] { new VoucherInventoryLine(k.ItemId, k.MainId, 1m, Money.FromRupees(2000m)) }));
        Assert.False(v.HasPosTenders);
        Assert.Empty(v.PosTenders);
    }
}
