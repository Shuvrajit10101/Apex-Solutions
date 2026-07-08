using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// The <b>Bright re-verification gate</b> (the hard Phase-3 sign-off; phase3-inventory-requirements §6,
/// BR-1..BR-6). It proves that the accounts↔inventory engine can <b>derive</b> Bright's closing stock
/// (₹15,000) from real inventory movements — opening stock booked to a Stock-in-Hand ledger, purchases and
/// the credit sale posted as <b>item-invoices</b>, and the cash-sale stock-out as a stock-only Delivery Note
/// — rather than the hand-posted closing-stock Journal, and that the Balance Sheet and Trading/P&amp;L
/// reconcile <b>to the paisa</b> under <see cref="ClosingStockMode.InventoryDerived"/>.
///
/// <para><b>Design (Average Cost — DP-1 default).</b> Opening 250 @ ₹100 = ₹25,000; purchase 400 @ ₹100 =
/// ₹40,000 (the running average stays ₹100 throughout, since every inward is ₹100); credit sale out 400,
/// cash sale out 100 ⇒ closing 150 units × ₹100 = ₹15,000 DERIVED. The item-invoice accounting legs are the
/// SAME Dr/Cr Bright already posts (so the existing AsPostedLedger tests in
/// <see cref="FixtureLoadTests"/> stay green unchanged); the added stock is additive/derived-only.</para>
///
/// All figures are asserted paisa-exact (NFR-3). This test uses <see cref="ClosingStockMode.InventoryDerived"/>
/// throughout; the fixture's manual "Closing Stock" Journal (#11) is IGNORED in this mode (every Stock-in-Hand
/// ledger is suppressed and replaced by the single derived Σ item-closing-value line).
/// </summary>
public class BrightReVerificationTests
{
    private const string ItemName = "General Merchandise";

    /// <summary>
    /// Loads Bright for the <b>inventory-derived</b> gate: the hand-posted closing-stock Journal (#11) is
    /// excluded (BR-3 — closing stock is DERIVED, so retaining the manual entry would double-count it), and the
    /// inventory block (opening stock booked to Stock-in-Hand, item-invoice purchases/sales, the cash-sale
    /// Delivery Note) is loaded. The remaining accounting legs are byte-for-byte those Bright already posts.
    /// </summary>
    private static (FixtureLoader.LoadedFixture Fixture, Guid ItemId) LoadBright()
    {
        var f = FixtureLoader.Load("bright.json", skipManualClosingStock: true);
        var itemId = FixtureLoader.StockItemId(f.Company, ItemName);
        Assert.NotEqual(Guid.Empty, itemId); // the inventory block loaded
        return (f, itemId);
    }

    // ---------------------------------------------------------------- BR-1: opening stock reconciles

    [Fact]
    [Trait("Category", "Fixture")]
    public void BR1_opening_stock_reconciles_from_inventory_to_the_opening_stock_in_hand_ledger()
    {
        var (f, itemId) = LoadBright();

        // Inventory opening value Σ(qty × rate) = 250 × ₹100 = ₹25,000.
        var openingValue = new InventoryService(f.Company).OpeningValueOf(itemId);
        Assert.Equal(Money.FromRupees(25000m), openingValue);

        // …equal to the "Opening Stock" Stock-in-Hand ledger's opening debit (BR-1).
        var openingStockLedger = f.Company.FindLedgerByName("Opening Stock")!;
        Assert.True(openingStockLedger.OpeningIsDebit);
        Assert.Equal(Money.FromRupees(25000m), openingStockLedger.OpeningBalance);
        Assert.True(ClassificationRules.IsStockInHandLedger(openingStockLedger, f.Company));
    }

    // ---------------------------------------------------------------- BR-2: on-hand quantity reconciles

    [Fact]
    [Trait("Category", "Fixture")]
    public void BR2_on_hand_quantity_reconciles_opening_plus_purchases_minus_sales_equals_closing()
    {
        var (f, itemId) = LoadBright();
        var onHand = new InventoryLedger(f.Company);
        var main = f.Company.MainLocation!.Id;

        // Opening 250 + purchase 400 − credit-sale 400 − cash-sale 100 = closing 150 (opening + purchases − sales).
        Assert.Equal(150m, onHand.OnHand(itemId, main, f.AsOf));
        Assert.Equal(150m, new StockValuationService(f.Company).ClosingValue(itemId, f.AsOf).Quantity);
    }

    // ---------------------------------------------------------------- BR-3: closing stock DERIVED = ₹15,000

    [Fact]
    [Trait("Category", "Fixture")]
    public void BR3_closing_stock_is_derived_to_15000_by_average_cost_without_a_manual_journal()
    {
        var (f, itemId) = LoadBright();
        var valuation = new StockValuationService(f.Company);

        // The item uses Average Cost (DP-1 default), the running average is ₹100 throughout.
        Assert.Equal(StockValuationMethod.AverageCost, f.Company.FindStockItem(itemId)!.ValuationMethod);

        // Per-item and company-aggregate derived closing value = ₹15,000, to the paisa.
        Assert.Equal(Money.FromRupees(15000m), valuation.ClosingValue(itemId, f.AsOf).Value);
        Assert.Equal(Money.FromRupees(15000m), valuation.TotalClosingStockValue(f.AsOf));

        // Cross-check against the fixture's declared derived target.
        var exp = f.Expected.GetProperty("inventoryDerived");
        Assert.Equal(exp.GetProperty("closingStockValue").GetDecimal(), valuation.TotalClosingStockValue(f.AsOf).Amount);
    }

    // ---------------------------------------------------------------- BR-4: Balance Sheet reconciles

    [Fact]
    [Trait("Category", "Fixture")]
    public void BR4_balance_sheet_stock_in_hand_equals_derived_closing_and_balances_to_the_paisa()
    {
        var (f, _) = LoadBright();
        var bs = BalanceSheet.Build(f.Company, f.AsOf, ClosingStockMode.InventoryDerived);

        // The single derived Stock-in-Hand asset line == the derived closing value ₹15,000.
        var stockInHand = bs.Assets.Single(a => a.Name == "Stock-in-Hand");
        Assert.Equal(Money.FromRupees(15000m), stockInHand.Amount);

        // Under InventoryDerived the manual "Closing Stock" / "Opening Stock" ledgers do NOT appear as assets.
        Assert.DoesNotContain(bs.Assets, a => a.Name == "Closing Stock");
        Assert.DoesNotContain(bs.Assets, a => a.Name == "Opening Stock");

        // Totals: ₹1,84,000 both sides, balanced to the paisa; net profit −₹1,000 folded into capital.
        Assert.Equal(Money.FromRupees(184000m), bs.TotalAssets);
        Assert.Equal(Money.FromRupees(184000m), bs.TotalLiabilities);
        Assert.True(bs.Balanced);
        Assert.Equal(Money.FromRupees(-1000m), bs.NetProfitInCapital);

        // The other asset lines are exactly Bright's expected figures (net-of-dep Machinery etc.).
        Assert.Equal(Money.FromRupees(54000m), bs.Assets.Single(a => a.Name == "Machinery").Amount);
        Assert.Equal(Money.FromRupees(27000m), bs.Assets.Single(a => a.Name == "Cash").Amount);
        Assert.Equal(Money.FromRupees(53000m), bs.Assets.Single(a => a.Name == "HDFC Bank").Amount);
        Assert.Equal(Money.FromRupees(35000m), bs.Assets.Single(a => a.Name == "Ram & Co (Debtor)").Amount);

        var exp = f.Expected.GetProperty("inventoryDerived");
        Assert.Equal(exp.GetProperty("balanceSheetTotal").GetDecimal(), bs.TotalAssets.Amount);
    }

    // ---------------------------------------------------------------- BR-5: Trading/COGS identity

    [Fact]
    [Trait("Category", "Fixture")]
    public void BR5_trading_cogs_identity_holds_and_gross_and_net_profit_tie_to_bright()
    {
        var (f, _) = LoadBright();
        var pl = ProfitAndLoss.Build(f.Company, f.AsOf, ClosingStockMode.InventoryDerived);

        // Periodic-inventory pieces (derived closing stock; opening from the Stock-in-Hand ledger).
        var opening = pl.OpeningStock.Amount;
        var closing = pl.ClosingStock.Amount;
        Assert.Equal(25000m, opening);   // opening Stock-in-Hand
        Assert.Equal(15000m, closing);   // DERIVED, not the manual journal

        // Purchases = ₹40,000 (Purchase Accounts).
        var purchases = LedgerBalances.SignedClosing(f.Company, f.Company.FindLedgerByName("Purchases")!, f.AsOf);
        Assert.Equal(40000m, purchases);

        // COGS = Opening + Purchases − Closing = 25,000 + 40,000 − 15,000 = ₹50,000.
        var cogs = opening + purchases - closing;
        Assert.Equal(50000m, cogs);

        // Gross profit = Sales + Direct Income − (Opening + Purchases + Direct Expenses − Closing).
        // Sales 73,000; Direct Expenses = Wages 6,000 + Carriage 2,000 = 8,000.
        // GP = 73,000 − (25,000 + 40,000 + 8,000 − 15,000) = 73,000 − 58,000 = ₹15,000.
        Assert.Equal(Money.FromRupees(15000m), pl.GrossProfit);

        // Net profit = GP − Indirect Expenses (Salaries 7,000 + Rent 3,000 + Depreciation 6,000 = 16,000)
        //            = 15,000 − 16,000 = −₹1,000 — Bright's expected figure, now DERIVED.
        Assert.Equal(Money.FromRupees(-1000m), pl.NetProfit);

        // Tie to the fixture's declared derived + original P&L targets to the paisa.
        var exp = f.Expected.GetProperty("inventoryDerived");
        Assert.Equal(exp.GetProperty("cogs").GetDecimal(), cogs);
        Assert.Equal(exp.GetProperty("grossProfit").GetDecimal(), pl.GrossProfit.Amount);
        Assert.Equal(exp.GetProperty("netProfit").GetDecimal(), pl.NetProfit.Amount);

        var expTpl = f.Expected.GetProperty("tradingAndProfitAndLoss");
        Assert.Equal(expTpl.GetProperty("grossProfit").GetDecimal(), pl.GrossProfit.Amount);
        Assert.Equal(expTpl.GetProperty("netProfit").GetDecimal(), pl.NetProfit.Amount);
    }

    // ---------------------------------------------------------------- BR-3/BR-4/BR-5: manual journal is redundant

    [Fact]
    [Trait("Category", "Fixture")]
    public void The_derived_statements_equal_the_hand_posted_statements_so_the_manual_journal_is_redundant()
    {
        // The whole point of the gate: the DERIVED statement set (inventory-derived, manual journal SKIPPED)
        // equals the hand-posted statement set (AsPostedLedger, manual journal KEPT) to the paisa — so the
        // ₹15,000 falls out of inventory and the manual closing-stock Journal is now redundant.
        var derivedCompany = FixtureLoader.Load("bright.json", skipManualClosingStock: true);
        var asPostedCompany = FixtureLoader.Load("bright.json"); // keeps the manual closing-stock Journal (#11)

        var derived = ProfitAndLoss.Build(derivedCompany.Company, derivedCompany.AsOf, ClosingStockMode.InventoryDerived);
        var asPosted = ProfitAndLoss.Build(asPostedCompany.Company, asPostedCompany.AsOf, ClosingStockMode.AsPostedLedger);

        Assert.Equal(Money.FromRupees(15000m), derived.ClosingStock);
        Assert.Equal(asPosted.ClosingStock, derived.ClosingStock);   // both ₹15,000
        Assert.Equal(asPosted.OpeningStock, derived.OpeningStock);   // both ₹25,000
        Assert.Equal(asPosted.GrossProfit, derived.GrossProfit);     // both ₹15,000
        Assert.Equal(asPosted.NetProfit, derived.NetProfit);         // both −₹1,000

        var bsDerived = BalanceSheet.Build(derivedCompany.Company, derivedCompany.AsOf, ClosingStockMode.InventoryDerived);
        var bsAsPosted = BalanceSheet.Build(asPostedCompany.Company, asPostedCompany.AsOf, ClosingStockMode.AsPostedLedger);
        Assert.Equal(Money.FromRupees(184000m), bsDerived.TotalAssets);
        Assert.Equal(bsAsPosted.TotalAssets, bsDerived.TotalAssets);         // both ₹1,84,000
        Assert.Equal(bsAsPosted.TotalLiabilities, bsDerived.TotalLiabilities);
        Assert.True(bsDerived.Balanced);
        Assert.True(bsAsPosted.Balanced);
    }

    // ---------------------------------------------------------------- BR-6: Robert (accounts-only) stays green

    [Fact]
    [Trait("Category", "Fixture")]
    public void BR6_robert_accounts_only_is_unaffected_by_inventory_and_stays_green()
    {
        // Robert has NO inventory block; loading it must not create any stock masters, and its accounts-only
        // statements are unchanged (net profit ₹5,000, balances ₹1,05,000). Inventory must not perturb the
        // accounts-only path (BR-6).
        var f = FixtureLoader.Load("robert.json");
        Assert.Empty(f.Company.StockItems);
        Assert.Empty(f.Company.StockOpeningBalances);

        var pl = ProfitAndLoss.Build(f.Company, f.AsOf);
        Assert.Equal(Money.FromRupees(5000m), pl.NetProfit);

        var bs = BalanceSheet.Build(f.Company, f.AsOf);
        Assert.Equal(Money.FromRupees(105000m), bs.TotalAssets);
        Assert.Equal(Money.FromRupees(105000m), bs.TotalLiabilities);
        Assert.True(bs.Balanced);
    }
}
