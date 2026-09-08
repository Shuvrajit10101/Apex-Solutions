using Apex.Ledger.Domain;

namespace Apex.Ledger.Reports;

/// <summary>
/// The four buckets each half of <see cref="VatComputation"/> is split into: local vs inter-State, and taxable
/// vs exempt. These are the vendor's own subsections —
/// <c>help.tallysolutions.com/tally-prime/reports/vat-particulars-computation-tally/</c> describes "<i>Local
/// Sales / Interstate Sales (divided into Taxable and Exempt)</i>" and the same for purchases.
/// </summary>
public enum VatComputationBucket
{
    /// <summary>Local (intra-State) transactions bearing a VAT rate.</summary>
    LocalTaxable = 0,

    /// <summary>Local transactions in VAT goods carrying no rate — the vendor's "Exempt".</summary>
    LocalExempt = 1,

    /// <summary>Inter-State transactions bearing a rate. These are what CST reaches.</summary>
    InterstateTaxable = 2,

    /// <summary>Inter-State transactions carrying no rate.</summary>
    InterstateExempt = 3,
}

/// <summary>One bucket's totals on one half of the computation.</summary>
/// <param name="Bucket">Which of the vendor's four subsections this is.</param>
/// <param name="AssessableValue">Vendor: "<i>the assessable value of sales and purchases</i>" — the goods
/// value the rate applies to, exclusive of tax.</param>
/// <param name="Tax">The tax that value carries. 🔴 <b>COMPUTED BY THIS REPORT, NOT POSTED</b> — see
/// <see cref="VatComputation"/>'s remarks before using it as a ledger figure.</param>
/// <param name="TransactionCount">How many vouchers contributed, so a surprising total can be chased.</param>
public sealed record VatComputationLine(
    VatComputationBucket Bucket,
    Money AssessableValue,
    Money Tax,
    int TransactionCount);

/// <summary>
/// The <b>VAT Computation</b> report — the vendor's <i>Particulars of Computation Details</i>
/// (<c>help.tallysolutions.com/tally-prime/reports/vat-particulars-computation-tally/</c>), for the goods GST
/// never absorbed. Census row 15.5, computation half.
/// </summary>
/// <remarks>
/// <para>🔴 <b>READ THIS FIRST: THE TAX ON THIS REPORT IS COMPUTED HERE AND IS NOT IN THE BOOKS.</b> This build
/// posts no VAT leg on any voucher — <c>VatService</c> creates no tax ledger and writes no entry line. Every
/// <see cref="VatComputationLine.Tax"/> figure below is <c>assessable value × rate</c>, worked out at report
/// time from the masters, exactly as the vendor describes its own report ("<i>displays the transaction values
/// considered in the returns … along with the amount of liability</i>"). It follows that <b>no figure on this
/// report appears in the Trial Balance or the Balance Sheet</b>, and an operator who owes VAT records the
/// payment as an ordinary voucher. That limit is stated here, on the report, rather than left for someone to
/// infer from a balance that will not tie.</para>
///
/// <para>🔴 <b>WHAT IS IN SCOPE IS DECIDED BY THE GOODS, ITEM BY ITEM.</b> A line contributes only when its
/// stock item is one State VAT can lawfully reach — <see cref="NonGstGoods.IsOutsideGst"/>: alcoholic liquor
/// for human consumption (Constitution Art. 366(12A); CGST Act s.9(1)) and the five petroleum products (CGST
/// Act s.9(2)). An ordinary GST item on the same invoice contributes NOTHING to this report, which is why a
/// mixed-catalogue dealer can enable VAT without every sale suddenly acquiring a VAT figure.</para>
///
/// <para><b>The rate that applies, in one sentence and one order.</b> For a voucher with item lines: the
/// ITEM's <see cref="StockItem.VatTaxRateBasisPoints"/>, and nothing else. For a voucher with no item lines
/// (the vendor's accounts-only case): the sales/purchase LEDGER's
/// <see cref="Ledger.VatTaxRateBasisPoints"/> on a ledger flagged <see cref="Ledger.VatApplicable"/>.
/// 🔴 <b>The ledger rate never overrides an item rate</b>, and that asymmetry is deliberate: the item path is
/// gated by the class of goods and the ledger path cannot be (a ledger has no goods), so letting the unguarded
/// path win would defeat the gate.</para>
///
/// <para><b>Local vs inter-State.</b> A transaction is inter-State when EITHER the operator has recorded a CST
/// declaration form on it (<see cref="Voucher.CstFormType"/>) OR the party master routes inter-State. Both
/// facts are operator-supplied and neither is guessed; where neither is available the transaction is local,
/// which is the common case and the conservative one (it attracts VAT rather than the concessional CST rate).
/// <see cref="VatComputation.InterstateFromDeclarationForm"/> reports how many rows were classified by a form,
/// so the split can be audited rather than trusted.</para>
///
/// <para>🔴 <b>The State comparison is NOT re-implemented here.</b> It is
/// <see cref="GstReportSupport.RoutingOf(Company, string?)"/>, this rule's single home — see
/// <see cref="IsDifferentState"/> for why a private copy is a defect the build refuses.</para>
///
/// <para>🔴 <b>CST PAYABLE IS REPORTED ONLY WHEN A RATE WAS SUPPLIED, AND IS OTHERWISE NULL.</b> The
/// concessional Form-C rate could not be retrieved from an official source for this slice (indiacode.nic.in
/// refused every copy of the CST Act 1956), so nothing is seeded and nothing is assumed. When the operator has
/// not filled the vendor's "CST Rate Against Form C" field, <see cref="CstPayable"/> is <c>null</c> and the
/// screen says the rate is not set — it does NOT quietly show zero, which would read as "you owe nothing".</para>
///
/// <para>🔴 <b>THREE OF THE VENDOR'S SECTIONS ARE DELIBERATELY ABSENT, AND SAYING SO IS THE POINT.</b> The
/// vendor's report also carries "<i>VAT Adjustments</i>", "<i>VAT Payable or Refundable after Adjustments</i>"
/// and the "<i>Purchase tax liability</i>" pair. This build has no VAT adjustment mechanism at all — no
/// adjustment voucher, no journal class, nothing that could produce a figure — so those sections would be
/// permanently zero. A row that can only ever read zero is worse than an absent one: it tells the operator
/// their adjustments came to nothing, when in truth they were never asked for. <see cref="OmittedSections"/>
/// names them on the report itself.</para>
///
/// <para>🔴 <b>THE ~30 STATE RETURN FORMS ARE NOT BUILT AND CANNOT HONESTLY BE.</b> Census row 15.5 pairs the
/// computation with the state return formats (Form VAT 100 and its ~30 siblings). Those are repealed layouts:
/// each was prescribed by a State's own VAT rules, the States withdrew them on GST, and no live official
/// source publishes the layouts. Reconstructing them from memory or from a tax blog would put fabricated
/// statutory forms into a shipped product. The computation — whose shape the vendor page above does publish —
/// is what ships.</para>
///
/// <para><b>What never counts.</b> Cancelled and Optional (Ctrl+L) vouchers and not-yet-due post-dated ones,
/// via the same <see cref="LedgerBalances.CountsAsOf(Voucher, DateOnly, VoucherBaseType?)"/> rule every report
/// here uses.</para>
/// </remarks>
public sealed record VatComputation(
    DateOnly From,
    DateOnly To,
    IReadOnlyList<VatComputationLine> Sales,
    IReadOnlyList<VatComputationLine> Purchases,
    Money OutputTax,
    Money InputTax,
    Money? CstPayable,
    int InterstateFromDeclarationForm)
{
    /// <summary>The vendor's section caption for the sales half.</summary>
    public const string SalesCaption = "Sales";

    /// <summary>The vendor's section caption for the purchases half.</summary>
    public const string PurchasesCaption = "Purchases";

    /// <summary>The vendor's section caption for the net position.</summary>
    public const string PayableCaption = "VAT Payable or Refundable";

    /// <summary>The vendor's section caption for the CST line.</summary>
    public const string CstPayableCaption = "CST Payable";

    /// <summary>
    /// The vendor sections this build does not produce, named on the report itself so a reader is never left
    /// to wonder whether a missing section means "nothing to show". See the type remarks.
    /// </summary>
    public static IReadOnlyList<string> OmittedSections { get; } = new[]
    {
        "VAT Adjustments",
        "VAT Payable or Refundable after Adjustments",
        "Purchase tax liability",
    };

    /// <summary>
    /// The single sentence the screen prints under the computation, so the "computed, not posted" limit and the
    /// omitted sections travel with the report wherever it is shown or printed.
    /// </summary>
    public const string Disclosure =
        "VAT shown here is computed from the transaction values and is not posted to any ledger. "
        + "Adjustments and the state return forms are not in this build.";

    /// <summary>
    /// <b>VAT Payable or Refundable</b> — output tax less input tax. Positive is payable, negative refundable,
    /// which is why the vendor's own caption names both directions.
    /// </summary>
    public Money PayableOrRefundable => OutputTax - InputTax;

    /// <summary>True iff the net position is a liability rather than a refund.</summary>
    public bool IsPayable => PayableOrRefundable.Amount >= 0m;

    /// <summary>Builds the computation over <c>[from, to]</c>.</summary>
    public static VatComputation Build(Company company, DateOnly from, DateOnly to)
    {
        ArgumentNullException.ThrowIfNull(company);

        var ledgersById = company.Ledgers.ToDictionary(l => l.Id);

        var sales = NewBuckets();
        var purchases = NewBuckets();
        var formClassified = 0;

        foreach (var v in company.Vouchers)
        {
            if (v.Date < from || v.Date > to) continue;

            var type = company.FindVoucherType(v.TypeId);
            if (type is null) continue;

            var isSale = type.BaseType == VoucherBaseType.Sales;
            var isPurchase = type.BaseType == VoucherBaseType.Purchase;
            if (!isSale && !isPurchase) continue;
            if (!LedgerBalances.CountsAsOf(v, to, type.BaseType)) continue;

            Domain.Ledger? party = v.PartyId is { } pid && ledgersById.TryGetValue(pid, out var p) ? p : null;
            var byForm = v.CstFormType is not null;
            var interstate = byForm || IsDifferentState(company, party);

            var (value, tax, counted) = Measure(company, v, ledgersById);
            if (!counted) continue;
            if (byForm) formClassified++;

            var bucket = (interstate, tax.Amount != 0m) switch
            {
                (true, true) => VatComputationBucket.InterstateTaxable,
                (true, false) => VatComputationBucket.InterstateExempt,
                (false, true) => VatComputationBucket.LocalTaxable,
                (false, false) => VatComputationBucket.LocalExempt,
            };

            var target = isSale ? sales : purchases;
            var current = target[(int)bucket];
            target[(int)bucket] = current with
            {
                AssessableValue = current.AssessableValue + value,
                Tax = current.Tax + tax,
                TransactionCount = current.TransactionCount + 1,
            };
        }

        var outputTax = Total(sales);
        var inputTax = Total(purchases);

        // 🔴 CST Payable is NULL, never zero, when the operator has not supplied the Form-C rate. Zero would
        // read as "nothing is owed"; null lets the screen say the rate has not been set. See the type remarks.
        Money? cstPayable = null;
        if (company.Vat is { HasCstRateAgainstFormC: true } vat)
        {
            var interstateSalesValue =
                sales[(int)VatComputationBucket.InterstateTaxable].AssessableValue
                + sales[(int)VatComputationBucket.InterstateExempt].AssessableValue;
            cstPayable = ApplyRate(interstateSalesValue, vat.CstRateAgainstFormCBasisPoints!.Value);
        }

        return new VatComputation(from, to, sales, purchases, outputTax, inputTax, cstPayable, formClassified);
    }

    // ----------------------------------------------------------------- internals

    private static VatComputationLine[] NewBuckets() => new[]
    {
        new VatComputationLine(VatComputationBucket.LocalTaxable, Money.Zero, Money.Zero, 0),
        new VatComputationLine(VatComputationBucket.LocalExempt, Money.Zero, Money.Zero, 0),
        new VatComputationLine(VatComputationBucket.InterstateTaxable, Money.Zero, Money.Zero, 0),
        new VatComputationLine(VatComputationBucket.InterstateExempt, Money.Zero, Money.Zero, 0),
    };

    private static Money Total(IReadOnlyList<VatComputationLine> buckets)
    {
        var sum = Money.Zero;
        foreach (var b in buckets) sum += b.Tax;
        return sum;
    }

    /// <summary>
    /// The assessable value and computed tax one voucher contributes, and whether it contributes at all.
    ///
    /// <para>The item path is tried first and, when the voucher HAS item lines, it is the only path — even if
    /// every one of those items is inside GST and the voucher therefore contributes nothing. Falling through
    /// to the ledger rate in that case would let a VAT-flagged sales ledger put a VAT figure on an invoice for
    /// ordinary goods, which is precisely what the class-of-goods gate exists to prevent.</para>
    /// </summary>
    private static (Money Value, Money Tax, bool Counted) Measure(
        Company company, Voucher voucher, IReadOnlyDictionary<Guid, Domain.Ledger> ledgersById)
    {
        if (voucher.HasInventoryLines)
        {
            var value = Money.Zero;
            var tax = Money.Zero;
            var any = false;
            foreach (var line in voucher.InventoryLines)
            {
                var item = company.FindStockItem(line.StockItemId);
                if (item is null || !item.IsOutsideGst) continue;
                any = true;
                value += line.Value;
                if (item.VatTaxRateBasisPoints is { } bp && bp > 0)
                    tax += ApplyRate(line.Value, bp);
            }
            return (value, tax, any);
        }

        // Accounts-only: the vendor's separately documented case, where there is no stock item to ask.
        var ledgerValue = Money.Zero;
        var ledgerTax = Money.Zero;
        var counted = false;
        foreach (var line in voucher.Lines)
        {
            if (!ledgersById.TryGetValue(line.LedgerId, out var ledger)) continue;
            if (!ledger.VatApplicable) continue;
            counted = true;
            ledgerValue += line.Amount;
            if (ledger.VatTaxRateBasisPoints is { } bp && bp > 0)
                ledgerTax += ApplyRate(line.Amount, bp);
        }
        return (ledgerValue, ledgerTax, counted);
    }

    /// <summary>
    /// Applies a basis-point rate to a value and rounds to the paisa.
    ///
    /// <para>Rounding happens ONCE, on the line, and the buckets then add rounded figures — the same order
    /// every tax projection in this repository uses. Summing unrounded products and rounding the total instead
    /// would produce a figure that does not equal the sum of the lines an operator can see.</para>
    /// </summary>
    private static Money ApplyRate(Money value, int basisPoints) =>
        new Money(value.Amount * basisPoints / 10_000m).RoundToPaisa();

    /// <summary>
    /// Whether the transaction crosses a State border, per the party master.
    ///
    /// <para>🔴 <b>DELEGATED TO <see cref="GstReportSupport.RoutingOf(Company, string?)"/>, WHICH IS THIS
    /// RULE'S ONE HOME — and that is enforced, not merely intended.</b> An earlier draft of this method
    /// compared the two State codes itself, and <c>OneRuleDriftLockTests.IntraInterRoutingHasOneHome</c> failed
    /// the build for it. It was right to: this project has already deleted TWO divergent copies of intra/inter
    /// routing, and the second of them silently changed a statutory e-Way answer for a blank-State party. A
    /// third copy here would have made a VAT/CST answer disagree with the GST answer for the same party.
    /// (That lock is a TEXT scan, so this note deliberately describes the deleted code rather than quoting
    /// it — quoting it trips the lock, which is the lock working, not a false positive.)</para>
    ///
    /// <para>The shared rule's three answers map onto this one cleanly: <c>null</c> ("cannot route — no
    /// supplier location recorded") and <c>false</c> ("intra-State") both mean <b>local</b> here. Treating an
    /// unroutable transaction as inter-State would move value onto the concessional CST rate on no evidence at
    /// all, which is the conservative direction to get wrong.</para>
    /// </summary>
    private static bool IsDifferentState(Company company, Domain.Ledger? party) =>
        GstReportSupport.RoutingOf(company, party?.PartyGst?.StateCode) == true;
}
