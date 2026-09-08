using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Ledger.Tests;

/// <summary>
/// W-N1 — <b>State VAT and Central Sales Tax for the goods GST never absorbed</b> (census 15.1 State VAT ·
/// 15.2 the Tax Rate on the masters · 15.5 VAT Computation · 15.6 CST declaration forms).
///
/// <para>🔴 <b>THE FIRST BLOCK IS THE MOST IMPORTANT AND IS ABOUT REFUSAL, NOT ARITHMETIC.</b> VAT and CST were
/// subsumed by GST for ordinary goods on 01-Jul-2017. They survive on exactly two classes: alcoholic liquor for
/// human consumption, which the Constitution puts outside GST permanently (Art. 366(12A); CGST Act s.9(1)
/// verbatim — "except on the supply of alcoholic liquor for human consumption"), and the five petroleum
/// products CGST Act s.9(2) keeps out until the GST Council notifies otherwise. A VAT rate accepted on ordinary
/// goods is not a cosmetic defect: it invites an operator to compute, and then to file, a tax abolished for
/// their trade. Every test in the first block exists to prove the product says no.</para>
///
/// <para>All figures are paisa-exact and every rate is in basis points (integers — never a REAL; see
/// <c>Paisa.cs</c>).</para>
/// </summary>
public sealed class StateVatCstTests
{
    private static readonly DateOnly FyStart = new(2025, 4, 1);
    private static readonly DateOnly Jun10 = new(2025, 6, 10);
    private static readonly DateOnly WindowFrom = new(2025, 4, 1);
    private static readonly DateOnly WindowTo = new(2026, 3, 31);

    // ================================================================= the gate (census 15.1 / 15.2)

    /// <summary>
    /// 🔴 A VAT rate is REFUSED on ordinary GST goods, and the refusal names what to change.
    /// Fails on today's main, where neither <c>NonGstGoodsClass</c> nor <c>VatService</c> exists.
    /// </summary>
    [Fact]
    public void A_VAT_rate_is_refused_on_ordinary_GST_goods()
    {
        var (c, item, _, _, _) = Book();

        Assert.Equal(NonGstGoodsClass.None, item.NonGstGoodsClass);

        var ex = Assert.Throws<InvalidOperationException>(
            () => new VatService(c).SetItemVatRate(item, 1450));

        // The operator is told WHY and what to do, not merely that they cannot.
        Assert.Contains("class of goods", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(item.VatTaxRateBasisPoints);
    }

    /// <summary>Each of the six classes the statute puts outside GST accepts a rate; ordinary goods and tobacco
    /// do not.</summary>
    [Theory]
    [InlineData(NonGstGoodsClass.AlcoholicLiquorForHumanConsumption, true)]
    [InlineData(NonGstGoodsClass.PetroleumCrude, true)]
    [InlineData(NonGstGoodsClass.HighSpeedDiesel, true)]
    [InlineData(NonGstGoodsClass.MotorSpirit, true)]
    [InlineData(NonGstGoodsClass.NaturalGas, true)]
    [InlineData(NonGstGoodsClass.AviationTurbineFuel, true)]
    [InlineData(NonGstGoodsClass.None, false)]
    [InlineData(NonGstGoodsClass.Tobacco, false)]
    public void Only_the_six_classes_outside_GST_accept_a_VAT_rate(NonGstGoodsClass goodsClass, bool allowed)
    {
        var (c, item, _, _, _) = Book();
        var vat = new VatService(c);
        vat.SetItemGoodsClass(item, goodsClass);

        if (allowed)
        {
            vat.SetItemVatRate(item, 1450);
            Assert.Equal(1450, item.VatTaxRateBasisPoints);
        }
        else
        {
            Assert.Throws<InvalidOperationException>(() => vat.SetItemVatRate(item, 1450));
            Assert.Null(item.VatTaxRateBasisPoints);
        }
    }

    /// <summary>
    /// 🔴 <b>Tobacco is refused for a DIFFERENT reason, and the message says so.</b> It is inside GST — only
    /// central excise runs alongside — so a product that lumped it in with ordinary goods would be telling the
    /// operator something false about their own trade.
    /// </summary>
    [Fact]
    public void Tobacco_is_refused_because_it_is_inside_GST_and_the_reason_says_so()
    {
        Assert.False(NonGstGoods.IsOutsideGst(NonGstGoodsClass.Tobacco));
        Assert.True(NonGstGoods.AttractsCentralExcise(NonGstGoodsClass.Tobacco));

        var reason = NonGstGoods.VatRefusalReason(NonGstGoodsClass.Tobacco);
        Assert.NotNull(reason);
        Assert.Contains("inside GST", reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("excise", reason, StringComparison.OrdinalIgnoreCase);

        // …and it is NOT the ordinary-goods sentence, which would send the operator to change a class that is
        // already correct.
        Assert.NotEqual(NonGstGoods.VatRefusalReason(NonGstGoodsClass.None), reason);
    }

    /// <summary>
    /// Moving an item back inside GST CLEARS its VAT rate. Leaving a stale rate behind would leave the report
    /// a figure it then has to decide whether to believe.
    /// </summary>
    [Fact]
    public void Reclassifying_an_item_back_inside_GST_clears_its_VAT_rate()
    {
        var (c, item, _, _, _) = Book();
        var vat = new VatService(c);
        vat.SetItemGoodsClass(item, NonGstGoodsClass.MotorSpirit);
        vat.SetItemVatRate(item, 2000);
        Assert.Equal(2000, item.VatTaxRateBasisPoints);

        vat.SetItemGoodsClass(item, NonGstGoodsClass.None);

        Assert.Null(item.VatTaxRateBasisPoints);
        Assert.False(item.IsOutsideGst);
    }

    /// <summary>Clearing a rate is always permitted, even on an item the rule would refuse to SET one on —
    /// taking a wrong figure off must never be blocked by the rule that stopped it going on.</summary>
    [Fact]
    public void Clearing_a_rate_is_permitted_even_on_ordinary_goods()
    {
        var (c, item, _, _, _) = Book();
        new VatService(c).SetItemVatRate(item, null);   // must not throw
        Assert.Null(item.VatTaxRateBasisPoints);
    }

    // ================================================================= VAT Computation (census 15.5)

    /// <summary>
    /// 🔴 <b>AN ORDINARY GST ITEM ON THE SAME INVOICE CONTRIBUTES NOTHING.</b> This is the gate expressed as a
    /// figure rather than as an exception, and it is the assertion that would catch a report which read the
    /// ledger rate, or the item rate, without asking what the goods are.
    /// </summary>
    [Fact]
    public void VAT_Computation_counts_only_the_goods_outside_GST()
    {
        var (c, diesel, widget, customer, salesLedger) = Book();
        var vat = new VatService(c);
        vat.EnableVat(tin: "29123456789");
        vat.SetItemGoodsClass(diesel, NonGstGoodsClass.HighSpeedDiesel);
        vat.SetItemVatRate(diesel, 1450);            // 14.5%
        // `widget` stays ordinary GST goods and carries no VAT rate.

        // One invoice: ₹1,00,000 of diesel + ₹50,000 of ordinary widgets.
        PostItemSale(c, customer, salesLedger, Jun10, new[]
        {
            (diesel, 100m, 1000m),   // 100 × ₹1,000 = ₹1,00,000
            (widget, 50m, 1000m),    //  50 × ₹1,000 =   ₹50,000
        });

        var report = VatComputation.Build(c, WindowFrom, WindowTo);

        var localTaxable = report.Sales.Single(l => l.Bucket == VatComputationBucket.LocalTaxable);
        // Only the diesel value is assessable — the widgets are invisible to VAT.
        Assert.Equal(100_000m, localTaxable.AssessableValue.Amount);
        Assert.Equal(14_500m, localTaxable.Tax.Amount);          // 1,00,000 × 14.5%
        Assert.Equal(14_500m, report.OutputTax.Amount);
        Assert.Equal(Money.Zero.Amount, report.InputTax.Amount);
        Assert.True(report.IsPayable);
        Assert.Equal(14_500m, report.PayableOrRefundable.Amount);
    }

    /// <summary>
    /// 🔴 <b>CST Payable is NULL — not zero — when the operator has not supplied the Form-C rate.</b> Zero would
    /// read as "you owe nothing"; null lets the screen say the rate was never set. The concessional rate could
    /// not be retrieved from an official source for this slice, so nothing is seeded, and this test is what
    /// stops a future <c>?? 200</c> from quietly asserting one.
    /// </summary>
    [Fact]
    public void CST_Payable_is_null_when_no_Form_C_rate_was_supplied()
    {
        var (c, diesel, _, customer, salesLedger) = Book();
        var vat = new VatService(c);
        vat.EnableVat(tin: "29123456789");            // no cstRateAgainstFormCBasisPoints
        vat.SetItemGoodsClass(diesel, NonGstGoodsClass.HighSpeedDiesel);
        vat.SetItemVatRate(diesel, 1450);

        var v = PostItemSale(c, customer, salesLedger, Jun10, new[] { (diesel, 100m, 1000m) });
        vat.SetCstDeclarationForm(v, CstDeclarationForm.FormC);   // makes it inter-State

        var report = VatComputation.Build(c, WindowFrom, WindowTo);

        Assert.Null(report.CstPayable);
        Assert.Equal(1, report.InterstateFromDeclarationForm);
        Assert.False(c.Vat!.HasCstRateAgainstFormC);
    }

    /// <summary>With a rate the operator supplied, CST Payable is computed from the inter-State sales value —
    /// and the value used is the assessable value, not the tax-inclusive total.</summary>
    [Fact]
    public void CST_Payable_uses_the_operator_supplied_rate_over_the_interstate_sales_value()
    {
        var (c, diesel, _, customer, salesLedger) = Book();
        var vat = new VatService(c);
        vat.EnableVat(tin: "29123456789", cstRateAgainstFormCBasisPoints: 200);   // the operator keyed 2%
        vat.SetItemGoodsClass(diesel, NonGstGoodsClass.HighSpeedDiesel);
        vat.SetItemVatRate(diesel, 1450);

        var v = PostItemSale(c, customer, salesLedger, Jun10, new[] { (diesel, 100m, 1000m) });
        vat.SetCstDeclarationForm(v, CstDeclarationForm.FormC);

        var report = VatComputation.Build(c, WindowFrom, WindowTo);

        Assert.NotNull(report.CstPayable);
        Assert.Equal(2_000m, report.CstPayable!.Value.Amount);    // 1,00,000 × 2%
    }

    /// <summary>A declaration form moves the transaction from the local bucket to the inter-State one.</summary>
    [Fact]
    public void A_declaration_form_classifies_the_transaction_as_interstate()
    {
        var (c, diesel, _, customer, salesLedger) = Book();
        var vat = new VatService(c);
        vat.EnableVat(tin: "29123456789");
        vat.SetItemGoodsClass(diesel, NonGstGoodsClass.HighSpeedDiesel);
        vat.SetItemVatRate(diesel, 1450);

        var v = PostItemSale(c, customer, salesLedger, Jun10, new[] { (diesel, 10m, 1000m) });

        var before = VatComputation.Build(c, WindowFrom, WindowTo);
        Assert.Equal(10_000m,
            before.Sales.Single(l => l.Bucket == VatComputationBucket.LocalTaxable).AssessableValue.Amount);
        Assert.Equal(0, before.InterstateFromDeclarationForm);

        vat.SetCstDeclarationForm(v, CstDeclarationForm.FormC);

        var after = VatComputation.Build(c, WindowFrom, WindowTo);
        Assert.Equal(Money.Zero.Amount,
            after.Sales.Single(l => l.Bucket == VatComputationBucket.LocalTaxable).AssessableValue.Amount);
        Assert.Equal(10_000m,
            after.Sales.Single(l => l.Bucket == VatComputationBucket.InterstateTaxable).AssessableValue.Amount);
        Assert.Equal(1, after.InterstateFromDeclarationForm);
    }

    /// <summary>The report names the vendor sections it does NOT produce, so a missing section can never be
    /// read as "nothing to show".</summary>
    [Fact]
    public void The_computation_names_the_sections_it_does_not_produce()
    {
        Assert.Contains("VAT Adjustments", VatComputation.OmittedSections);
        Assert.Contains("VAT Payable or Refundable after Adjustments", VatComputation.OmittedSections);
        Assert.Contains("not posted", VatComputation.Disclosure, StringComparison.OrdinalIgnoreCase);
    }

    // ================================================================= Declaration Forms (census 15.6)

    /// <summary>
    /// 🔴 <b>THE REACHABILITY PROPERTY.</b> A sales voucher with no declaration form still appears on Forms
    /// Receivable, under <see cref="CstFormStatus.NotRecorded"/> — because an operator can only record a form
    /// against a transaction they can reach. An earlier draft listed only vouchers that already carried a form
    /// type, which left census row 15.6 with no way in at all.
    /// </summary>
    [Fact]
    public void A_sale_with_no_form_is_listed_as_a_candidate_so_a_form_can_be_recorded_against_it()
    {
        var (c, diesel, _, customer, salesLedger) = Book();
        new VatService(c).EnableVat(tin: "29123456789");
        PostItemSale(c, customer, salesLedger, Jun10, new[] { (diesel, 10m, 1000m) });

        var report = CstDeclarationFormsReport.Build(c, CstFormsSide.Receivable, WindowFrom, WindowTo);

        var row = Assert.Single(report.Rows);
        Assert.Equal(CstFormStatus.NotRecorded, row.Status);
        Assert.Null(row.FormType);
        Assert.Single(report.NotRecorded);
        Assert.Empty(report.Pending);
        Assert.Empty(report.Completed);
        // A candidate is NOT an exposure: the pending total must not include it.
        Assert.Equal(Money.Zero.Amount, report.PendingValue.Amount);
    }

    /// <summary>
    /// The three-way status walk: candidate → pending (form type, no number) → completed (number recorded).
    ///
    /// <para>🔴 <b>The FORM NUMBER alone decides pending-ness.</b> Series was optional in practice and a date
    /// without a number records nothing, so this test fills the series and the date FIRST and asserts the row
    /// is still pending — a implementation that treated any filled field as "received" would drop a real
    /// exposure off the report.</para>
    /// </summary>
    [Fact]
    public void The_form_number_alone_moves_a_row_out_of_pending()
    {
        var (c, diesel, _, customer, salesLedger) = Book();
        var vat = new VatService(c);
        vat.EnableVat(tin: "29123456789");
        var v = PostItemSale(c, customer, salesLedger, Jun10, new[] { (diesel, 10m, 1000m) });

        // Series and date filled, number still blank — STILL PENDING.
        vat.SetCstDeclarationForm(
            v, CstDeclarationForm.FormC, seriesNumber: "A", formNumber: null,
            formDate: new DateOnly(2025, 7, 1));

        var pending = CstDeclarationFormsReport.Build(c, CstFormsSide.Receivable, WindowFrom, WindowTo);
        Assert.Single(pending.Pending);
        Assert.Empty(pending.Completed);
        Assert.Equal(10_000m, pending.PendingValue.Amount);

        // The number arrives — now completed, and the exposure clears.
        vat.SetCstDeclarationForm(
            v, CstDeclarationForm.FormC, "A", "C-000914", new DateOnly(2025, 7, 1));

        var done = CstDeclarationFormsReport.Build(c, CstFormsSide.Receivable, WindowFrom, WindowTo);
        Assert.Empty(done.Pending);
        Assert.Single(done.Completed);
        Assert.Equal(Money.Zero.Amount, done.PendingValue.Amount);
    }

    /// <summary>Clearing the form type clears the WHOLE block — no orphan series or number is left behind for
    /// the reports to have to interpret.</summary>
    [Fact]
    public void Clearing_the_form_type_clears_the_whole_block()
    {
        var (c, diesel, _, customer, salesLedger) = Book();
        var vat = new VatService(c);
        vat.EnableVat(tin: "29123456789");
        var v = PostItemSale(c, customer, salesLedger, Jun10, new[] { (diesel, 10m, 1000m) });
        vat.SetCstDeclarationForm(v, CstDeclarationForm.FormC, "A", "C-1", new DateOnly(2025, 7, 1));

        vat.SetCstDeclarationForm(v, null);

        Assert.Null(v.CstFormType);
        Assert.Null(v.CstFormSeriesNumber);
        Assert.Null(v.CstFormNumber);
        Assert.Null(v.CstFormDate);
        Assert.False(v.CstFormReceived);
    }

    /// <summary>
    /// A sale lands on Forms RECEIVABLE and a purchase on Forms ISSUABLE — never both. Folding the two sides
    /// would double-count every transaction on a statutory working paper.
    /// </summary>
    [Fact]
    public void A_sale_is_receivable_and_a_purchase_is_issuable_and_never_both()
    {
        var (c, diesel, _, customer, salesLedger) = Book();
        var vat = new VatService(c);
        vat.EnableVat(tin: "29123456789");
        PostItemSale(c, customer, salesLedger, Jun10, new[] { (diesel, 10m, 1000m) });

        var receivable = CstDeclarationFormsReport.Build(c, CstFormsSide.Receivable, WindowFrom, WindowTo);
        var issuable = CstDeclarationFormsReport.Build(c, CstFormsSide.Issuable, WindowFrom, WindowTo);

        Assert.Single(receivable.Rows);
        Assert.Empty(issuable.Rows);
        Assert.Equal("Forms Receivable", receivable.Title);
        Assert.Equal("Forms Issuable", issuable.Title);
    }

    /// <summary>
    /// The gross amount is the PARTY LINE's own amount, taken from the books — so the figure on the report and
    /// the figure in the ledger are the same number and can be reconciled.
    /// </summary>
    [Fact]
    public void The_gross_amount_is_the_party_line_from_the_books()
    {
        var (c, diesel, _, customer, salesLedger) = Book();
        new VatService(c).EnableVat(tin: "29123456789");
        PostItemSale(c, customer, salesLedger, Jun10, new[] { (diesel, 37m, 1234.56m) });

        var report = CstDeclarationFormsReport.Build(c, CstFormsSide.Receivable, WindowFrom, WindowTo);
        var row = Assert.Single(report.Rows);

        Assert.Equal(45_678.72m, row.GrossAmount.Amount);   // 37 × 1,234.56, paisa-exact
        Assert.Equal(customer.Id, row.PartyId);
    }

    /// <summary>A cancelled sale owes nobody a declaration and must not appear.</summary>
    [Fact]
    public void A_cancelled_sale_is_not_on_the_declaration_forms_report()
    {
        var (c, diesel, _, customer, salesLedger) = Book();
        new VatService(c).EnableVat(tin: "29123456789");
        var v = PostItemSale(c, customer, salesLedger, Jun10, new[] { (diesel, 10m, 1000m) });

        new LedgerService(c).Cancel(v.Id);

        var report = CstDeclarationFormsReport.Build(c, CstFormsSide.Receivable, WindowFrom, WindowTo);
        Assert.Empty(report.Rows);
    }

    // ================================================================= helpers

    /// <summary>A VAT-shaped book: one diesel item, one ordinary item, a customer and a sales ledger.</summary>
    private static (Company Company, StockItem Diesel, StockItem Widget, DomainLedger Customer, DomainLedger Sales)
        Book()
    {
        var c = CompanyFactory.CreateSeeded("Vat Fixture Co", FyStart);
        var inventory = new InventoryService(c);
        var unit = inventory.CreateSimpleUnit("Nos", "Numbers");
        var group = c.FindStockGroupByName("Primary") ?? inventory.CreateStockGroup("Primary");

        var diesel = inventory.CreateStockItem("High Speed Diesel", group.Id, unit.Id);
        var widget = inventory.CreateStockItem("Ordinary Widget", group.Id, unit.Id);

        var customer = new DomainLedger(
            Guid.NewGuid(), "Highway Fuels", c.FindGroupByName("Sundry Debtors")!.Id,
            Money.Zero, openingIsDebit: true);
        c.AddLedger(customer);

        var sales = new DomainLedger(
            Guid.NewGuid(), "Fuel Sales", c.FindGroupByName("Sales Accounts")!.Id,
            Money.Zero, openingIsDebit: false);
        c.AddLedger(sales);

        return (c, diesel, widget, customer, sales);
    }

    /// <summary>Posts an item-invoice Sales voucher with the given (item, qty, rate) lines.</summary>
    private static Voucher PostItemSale(
        Company c, DomainLedger customer, DomainLedger salesLedger, DateOnly date,
        IReadOnlyList<(StockItem Item, decimal Qty, decimal Rate)> lines)
    {
        var godown = c.Godowns.First();
        var total = Money.Zero;
        var inventoryLines = new List<VoucherInventoryLine>();
        foreach (var (item, qty, rate) in lines)
        {
            var line = new VoucherInventoryLine(
                item.Id, godown.Id, qty, Money.FromRupees(rate), StockDirection.Outward);
            inventoryLines.Add(line);
            total += line.Value;
        }

        var salesType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales);
        var voucher = new Voucher(
            Guid.NewGuid(), salesType.Id, date,
            new[]
            {
                new EntryLine(customer.Id, total, DrCr.Debit),
                new EntryLine(salesLedger.Id, total, DrCr.Credit),
            },
            partyId: customer.Id,
            inventoryLines: inventoryLines);

        return new LedgerService(c).Post(voucher);
    }
}
