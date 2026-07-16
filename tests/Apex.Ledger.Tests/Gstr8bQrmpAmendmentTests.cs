using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// Phase 9 slice 8b — <b>QRMP / IFF cadence</b> (RQ-17; DP-19) + <b>GSTR-1 amendment tables</b> 9A/9C/10 and the
/// <b>GSTR-3B correction advisory</b> (RQ-29; DP-33). All pure, read-only projections over the <b>existing</b>
/// <see cref="GstConfig.Periodicity"/> election and the §34 CDN link — nothing posts, nothing persists, no schema.
/// The adversarial focus:
/// <list type="bullet">
///   <item>A Quarterly filer's cadence sums 4 quarters (still footing to the quarterly 3B/1); the M1/M2 PMT-06 is
///     computed on <b>both</b> fixed-sum bases (35%-prior-quarter vs 100%-last-month) plus self-assessment; a Monthly
///     filer is not-applicable (ER-13).</item>
///   <item>IFF is the B2B subset of the M1/M2 window (excludes B2C); the quarterly GSTR-1 does not double-count the
///     IFF-furnished B2B (M1/M2 via IFF, only M3 residual in the quarterly return); the ₹50 lakh cap flags.</item>
///   <item>An amended CDN (prior-period original) lands in Table 9C via the §34 link; an ordinary B2B re-stated in a
///     later period surfaces in the advisory Table 9A with the original ref + differential, deduped one-per-document;
///     the 3B-correction advisory surfaces the prior-period correction flowing through the current 3B.</item>
///   <item>ER-13: an off / monthly / ordinary company's S8b views are empty/not-applicable and the existing Phase-4
///     GSTR-1/3B are byte-identical.</item>
/// </list>
/// </summary>
public sealed class Gstr8bQrmpAmendmentTests
{
    private static readonly DateOnly FyStart = new(2025, 4, 1);
    private static readonly DateOnly FyEnd = new(2026, 3, 31);

    private const string GstinMaharashtra = "27AAPFU0939F1ZV";
    private const string GstinGujarat = "24AAACC1206D1ZM";

    private static Domain.Ledger Add(Company c, string name, string groupName, bool openingIsDebit)
    {
        var l = new Domain.Ledger(Guid.NewGuid(), name, c.FindGroupByName(groupName)!.Id, Money.Zero, openingIsDebit);
        c.AddLedger(l);
        return l;
    }

    private static Company NewFiler(GstReturnPeriodicity periodicity, string name = "QRMP Co")
    {
        var c = CompanyFactory.CreateSeeded(name, FyStart);
        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = "27", Gstin = GstinMaharashtra, RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart, Periodicity = periodicity,
        });
        return c;
    }

    /// <summary>Posts an intra-state B2B sale to a registered debtor (Dr Debtor / Cr Sales / Cr Output tax).</summary>
    private static Voucher PostB2BSale(
        Company c, Domain.Ledger sales, Domain.Ledger debtor, decimal taxable, int rateBp, DateOnly date, int number = 0)
    {
        var gst = new GstService(c);
        var tax = gst.ComputeInvoiceTax(new[] { new GstService.TaxableLine(Money.FromRupees(taxable), rateBp) }, false, GstTaxDirection.Output);
        var lines = new List<EntryLine>
        {
            new(debtor.Id, new Money(taxable + tax.TotalTax.Amount), DrCr.Debit),
            new(sales.Id, Money.FromRupees(taxable), DrCr.Credit),
        };
        lines.AddRange(tax.TaxLines);
        var type = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id;
        return new LedgerService(c).Post(new Voucher(Guid.NewGuid(), type, date, lines, number: number, partyId: debtor.Id));
    }

    /// <summary>Posts an intra-state B2C sale to an unregistered consumer.</summary>
    private static void PostB2CSale(Company c, Domain.Ledger sales, Domain.Ledger consumer, decimal taxable, int rateBp, DateOnly date)
    {
        var gst = new GstService(c);
        var tax = gst.ComputeInvoiceTax(new[] { new GstService.TaxableLine(Money.FromRupees(taxable), rateBp) }, false, GstTaxDirection.Output);
        var lines = new List<EntryLine>
        {
            new(consumer.Id, new Money(taxable + tax.TotalTax.Amount), DrCr.Debit),
            new(sales.Id, Money.FromRupees(taxable), DrCr.Credit),
        };
        lines.AddRange(tax.TaxLines);
        var type = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id;
        new LedgerService(c).Post(new Voucher(Guid.NewGuid(), type, date, lines, partyId: consumer.Id));
    }

    private static Domain.Ledger RegisteredDebtor(Company c, string name, string gstin, string state)
    {
        var l = Add(c, name, "Sundry Debtors", true);
        l.PartyGst = new PartyGstDetails { RegistrationType = GstRegistrationType.Regular, Gstin = gstin, StateCode = state };
        return l;
    }

    private static Domain.Ledger Consumer(Company c)
    {
        var l = Add(c, "Walk-in", "Sundry Debtors", true);
        l.PartyGst = new PartyGstDetails { RegistrationType = GstRegistrationType.Consumer, StateCode = "27" };
        return l;
    }

    // ---- inward-RCM helpers (mirror RcmTests' S2/S7 posting path) ----

    private static RcmCategory Cat(Company c, string nature) =>
        c.Gst!.RcmCategories.First(x => x.SupplyNature == nature);

    /// <summary>An expense ledger flagged reverse-charge for a service, linked to a seeded notified RCM category.</summary>
    private static Domain.Ledger RcmExpenseLedger(Company c, string name, string nature, int rateBp)
    {
        var l = Add(c, name, "Indirect Expenses", true);
        l.SalesPurchaseGst = new StockItemGstDetails
        {
            Taxability = GstTaxability.Taxable, RateBasisPoints = rateBp, SupplyType = GstSupplyType.Services,
            ReverseChargeApplicable = true, RcmCategoryId = Cat(c, nature).Id,
        };
        return l;
    }

    /// <summary>A creditor party (the RCM supplier).</summary>
    private static Domain.Ledger RcmSupplier(Company c, string name, string? gstin, string? state)
    {
        var l = Add(c, name, "Sundry Creditors", false);
        l.PartyGst = new PartyGstDetails { RegistrationType = GstRegistrationType.Regular, Gstin = gstin, StateCode = state };
        return l;
    }

    /// <summary>Assembles + posts the RCM inward Purchase (Dr Expense / Cr Party + the balanced RCM dual pair).</summary>
    private static Voucher PostRcmInward(
        Company c, Domain.Ledger expense, Domain.Ledger party, Money value, RcmService.RcmPosting posting, DateOnly date)
    {
        var lines = new List<EntryLine>
        {
            new(expense.Id, value, DrCr.Debit),   // ordinary purchase leg (supplier value)
            new(party.Id, value, DrCr.Credit),    // supplier charges ZERO tax
        };
        lines.AddRange(posting.Lines);            // the balanced RCM pair, additive
        var type = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase).Id;
        return new LedgerService(c).Post(new Voucher(Guid.NewGuid(), type, date, lines));
    }

    // ================================================================ TEST 11: quarterly cadence sums 4 quarters

    [Fact]
    public void Qrmp_cadence_is_four_quarters_and_gstr9_foots_to_the_quarterly_returns()
    {
        var c = NewFiler(GstReturnPeriodicity.Quarterly, "Cadence Co");
        var sales = Add(c, "Sales", "Sales Accounts", false);
        var debtor = RegisteredDebtor(c, "Debtor", GstinGujarat, "24");
        // Sales in Q1 (April) and Q3 (October) — two distinct quarters.
        PostB2BSale(c, sales, debtor, 1000m, 1800, new(2025, 4, 5));
        PostB2BSale(c, sales, debtor, 2000m, 1800, new(2025, 10, 7));

        var qrmp = GstQrmp.Build(c, FyStart, FyEnd);
        Assert.True(qrmp.Applicable);
        Assert.Equal(4, qrmp.Quarters.Count);

        // The four quarters tile the FY exactly (Apr-Jun, Jul-Sep, Oct-Dec, Jan-Mar).
        Assert.Equal(new DateOnly(2025, 4, 1), qrmp.Quarters[0].From);
        Assert.Equal(new DateOnly(2025, 6, 30), qrmp.Quarters[0].To);
        Assert.Equal(new DateOnly(2025, 7, 1), qrmp.Quarters[1].From);
        Assert.Equal(new DateOnly(2025, 10, 1), qrmp.Quarters[2].From);
        Assert.Equal(new DateOnly(2026, 1, 1), qrmp.Quarters[3].From);
        Assert.Equal(new DateOnly(2026, 3, 31), qrmp.Quarters[3].To);

        // GSTR-9 for a Quarterly filer sums the 4 QUARTERLY returns (not 12 months) and foots to Σ the quarterly 3B/1.
        var g9 = Gstr9.Build(c, FyStart, FyEnd);
        decimal sumOutward = 0m;
        foreach (var q in qrmp.Quarters)
            sumOutward += Gstr3b.Build(c, q.From, q.To).TotalOutwardTax.Amount;
        Assert.Equal(new Money(sumOutward), g9.Table4TotalTax);
        Assert.Equal(Money.FromRupees(540m), g9.Table4TotalTax); // (1000+2000) @18% = 540
    }

    // ================================================================ TEST 12: IFF = B2B subset of M1/M2 (no double-count)

    [Fact]
    public void Iff_is_the_b2b_subset_of_m1_m2_and_the_quarterly_gstr1_does_not_double_count()
    {
        var c = NewFiler(GstReturnPeriodicity.Quarterly, "IFF Co");
        var sales = Add(c, "Sales", "Sales Accounts", false);
        var debtor = RegisteredDebtor(c, "Debtor", GstinGujarat, "24");
        var consumer = Consumer(c);

        // Q2 (Jul-Sep): B2B in July (M1), B2B in Aug (M2), B2B in Sep (M3), plus a B2C in July.
        PostB2BSale(c, sales, debtor, 1000m, 1800, new(2025, 7, 5));
        PostB2CSale(c, sales, consumer, 500m, 1800, new(2025, 7, 6));
        PostB2BSale(c, sales, debtor, 2000m, 1800, new(2025, 8, 10));
        PostB2BSale(c, sales, debtor, 3000m, 1800, new(2025, 9, 20));

        var qrmp = GstQrmp.Build(c, FyStart, FyEnd);
        var q2 = qrmp.Quarters[1];

        // IFF M1 (July) == the July B2B subset (the July B2C is excluded).
        var julyB2B = Gstr1.Build(c, new(2025, 7, 1), new(2025, 7, 31)).B2B;
        Assert.Equal(julyB2B.Count, q2.Month1Iff.B2B.Count);
        Assert.Equal(1, q2.Month1Iff.InvoiceCount);                         // only the one B2B (not the B2C)
        Assert.Equal(Money.FromRupees(1000m), q2.Month1Iff.TaxableValue);
        Assert.False(q2.Month1Iff.ExceedsCap);

        // IFF M2 (Aug) == the Aug B2B subset.
        Assert.Equal(1, q2.Month2Iff.InvoiceCount);
        Assert.Equal(Money.FromRupees(2000m), q2.Month2Iff.TaxableValue);

        // The quarterly GSTR-1 residual is the M3 (Sep) B2B only — the M1/M2 B2B is furnished via IFF (no double-count).
        var residual = Assert.Single(q2.QuarterlyResidualB2B);
        Assert.Equal(Money.FromRupees(3000m), residual.TaxableValue);

        // No double-count / no loss: Σ(IFF M1 + IFF M2 + residual) B2B count == the whole quarter's B2B count.
        var quarterB2BCount = Gstr1.Build(c, q2.From, q2.To).B2B.Count;
        Assert.Equal(quarterB2BCount, q2.Month1Iff.InvoiceCount + q2.Month2Iff.InvoiceCount + q2.QuarterlyResidualB2B.Count);
        Assert.Equal(3, quarterB2BCount);
    }

    [Fact]
    public void Iff_flags_the_fifty_lakh_monthly_cap()
    {
        var c = NewFiler(GstReturnPeriodicity.Quarterly, "IFF Cap Co");
        var sales = Add(c, "Sales", "Sales Accounts", false);
        var debtor = RegisteredDebtor(c, "Debtor", GstinGujarat, "24");
        // A single ₹60 lakh B2B invoice in July (M1 of Q2) exceeds the ₹50 lakh IFF cap.
        PostB2BSale(c, sales, debtor, 6_000_000m, 1800, new(2025, 7, 5));

        var q2 = GstQrmp.Build(c, FyStart, FyEnd).Quarters[1];
        Assert.True(q2.Month1Iff.ExceedsCap);
        Assert.Equal(Money.FromRupees(6_000_000m), q2.Month1Iff.TaxableValue);
        Assert.False(q2.Month2Iff.ExceedsCap); // Aug has nothing
    }

    // ================================================================ TEST 13: M1/M2 PMT-06 both bases + reconcile challans

    [Fact]
    public void Pmt06_suggestion_computes_both_fixed_sum_bases_and_reconciles_to_the_challans()
    {
        var c = NewFiler(GstReturnPeriodicity.Quarterly, "PMT06 Co");
        var bank = Add(c, "Bank", "Bank Accounts", true);
        var deposit = new GstDepositService(c);

        // Preceding quarter for Q2 (Jul-Sep) is Q1 (Apr-Jun). Cash Tax challans: April ₹1000, May ₹500, June ₹300.
        deposit.PostPmt06(GstTaxHead.Central, GstMinorHead.Tax, Money.FromRupees(1000m), bank, new(2025, 4, 30), "CPIN-A", "CIN-A");
        deposit.PostPmt06(GstTaxHead.Central, GstMinorHead.Tax, Money.FromRupees(500m), bank, new(2025, 5, 31), "CPIN-M", "CIN-M");
        deposit.PostPmt06(GstTaxHead.Central, GstMinorHead.Tax, Money.FromRupees(300m), bank, new(2025, 6, 30), "CPIN-J", "CIN-J");
        // A June INTEREST deposit (not tax) must be excluded from the cash bases (DP-34).
        deposit.PostPmt06(GstTaxHead.Central, GstMinorHead.Interest, Money.FromRupees(70m), bank, new(2025, 6, 30), "CPIN-INT", "CIN-INT");
        // The Q2-M1 (July) deposit actually made — reconciles to AlreadyDeposited.
        deposit.PostPmt06(GstTaxHead.Central, GstMinorHead.Tax, Money.FromRupees(630m), bank, new(2025, 7, 25), "CPIN-JUL", "CIN-JUL");

        var q2 = GstQrmp.Build(c, FyStart, FyEnd).Quarters[1];
        var m1 = q2.Month1Pmt06;

        // 35% of the preceding QUARTER's cash tax (₹1800) = ₹630 (the interest ₹70 is excluded).
        Assert.Equal(Money.FromRupees(630m), m1.FixedSum35PercentPriorQuarter);
        // 100% of the preceding quarter's LAST MONTH (June) cash tax = ₹300.
        Assert.Equal(Money.FromRupees(300m), m1.FixedSum100PercentLastMonth);
        // The M1 (July) deposit actually made reconciles to the S7 challans.
        Assert.Equal(Money.FromRupees(630m), m1.AlreadyDeposited);
        Assert.Equal(new DateOnly(2025, 7, 1), m1.MonthFrom);
        Assert.Equal(new DateOnly(2025, 7, 31), m1.MonthTo);
    }

    [Fact]
    public void Pmt06_self_assessment_is_the_months_net_cash_liability()
    {
        var c = NewFiler(GstReturnPeriodicity.Quarterly, "PMT06 Self Co");
        var sales = Add(c, "Sales", "Sales Accounts", false);
        var purchases = Add(c, "Purchases", "Purchase Accounts", true);
        var debtor = RegisteredDebtor(c, "Debtor", GstinGujarat, "24");
        var creditor = Add(c, "Creditor", "Sundry Creditors", false);

        // July (Q2 M1): output tax ₹360 on ₹2000 @18%; ITC ₹180 on ₹1000 @18% ⇒ self-assessment net ₹180.
        PostB2BSale(c, sales, debtor, 2000m, 1800, new(2025, 7, 5));
        var gst = new GstService(c);
        var tax = gst.ComputeInvoiceTax(new[] { new GstService.TaxableLine(Money.FromRupees(1000m), 1800) }, false, GstTaxDirection.Input);
        var plines = new List<EntryLine> { new(purchases.Id, Money.FromRupees(1000m), DrCr.Debit), new(creditor.Id, new Money(1000m + tax.TotalTax.Amount), DrCr.Credit) };
        plines.AddRange(tax.TaxLines);
        new LedgerService(c).Post(new Voucher(Guid.NewGuid(), c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase).Id, new(2025, 7, 6), plines, partyId: creditor.Id));

        var m1 = GstQrmp.Build(c, FyStart, FyEnd).Quarters[1].Month1Pmt06;
        Assert.Equal(Money.FromRupees(180m), m1.SelfAssessment); // 360 output − 180 ITC
    }

    // FIX 1 (A10 MEDIUM): the reverse-charge output liability is CASH-ONLY (§49(4)/§2(82); ER-3) — the self-assessment
    // must NEVER let ITC (not even the matching RCM ITC) discharge it, it is added on top as a cash-only floor.
    [Fact]
    public void Pmt06_self_assessment_adds_rcm_liability_as_a_cash_only_floor_never_offset_by_itc()
    {
        var c = NewFiler(GstReturnPeriodicity.Quarterly, "PMT06 RCM Co");
        new GstService(c).SeedAdvancedGst();                       // seeds the notified RCM categories (incl. "Legal")
        var rcm = new RcmService(c);
        var expense = RcmExpenseLedger(c, "Legal Fees", "Legal", 1800);
        var advocate = RcmSupplier(c, "Advocate (Gujarat)", GstinGujarat, "24");

        // July (Q2 M1): an inward reverse-charge legal service ₹10,000 @18% ⇒ RCM output ₹1800 + a matching RCM ITC
        // ₹1800, with zero forward output/ITC. The ₹1800 RCM liability is cash-only — the RCM ITC cannot net it to ₹0.
        var posting = rcm.BuildReverseCharge(
            Money.FromRupees(10000m), null, expense, advocate.PartyGst, new(2025, 7, 5), RcmService.SupplyKind.Domestic);
        Assert.True(posting.Applies);
        PostRcmInward(c, expense, advocate, Money.FromRupees(10000m), posting, new(2025, 7, 5));

        // Sanity: the July 3B has RCM output ₹1800 + RCM ITC ₹1800, and zero forward output.
        var july3b = Gstr3b.Build(c, new(2025, 7, 1), new(2025, 7, 31));
        Assert.Equal(Money.FromRupees(1800m), july3b.TotalRcmOutward);
        Assert.Equal(Money.FromRupees(1800m), july3b.TotalRcmItc);
        Assert.Equal(Money.Zero, july3b.TotalOutwardTax);

        var m1 = GstQrmp.Build(c, FyStart, FyEnd).Quarters[1].Month1Pmt06;
        // The suggested cash deposit is ₹1800 (the RCM liability floored on top) — NEVER ₹0 (RCM ITC does not offset it).
        Assert.Equal(Money.FromRupees(1800m), m1.SelfAssessment);
    }

    // FIX 2 (A10 LOW): all three PMT-06 methods must treat Compensation Cess consistently. The self-assessment excludes
    // cess (GSTR-3B exposes no forward output-cess field — the accepted S1/3B gap), so the fixed-sum + already-deposited
    // cash bases must exclude it too (else one method silently counts cess while another drops it).
    [Fact]
    public void Pmt06_cash_bases_exclude_cess_consistently_with_the_cess_free_self_assessment()
    {
        var c = NewFiler(GstReturnPeriodicity.Quarterly, "PMT06 Cess Co");
        var bank = Add(c, "Bank", "Bank Accounts", true);
        var deposit = new GstDepositService(c);

        // Preceding quarter (Q1) cash Tax deposits made in June: CGST ₹1000 + a CESS-head Tax challan ₹500.
        deposit.PostPmt06(GstTaxHead.Central, GstMinorHead.Tax, Money.FromRupees(1000m), bank, new(2025, 6, 30), "CPIN-C", "CIN-C");
        deposit.PostPmt06(GstTaxHead.Cess, GstMinorHead.Tax, Money.FromRupees(500m), bank, new(2025, 6, 30), "CPIN-CESS", "CIN-CESS");
        // July (Q2 M1) cash deposits: CGST ₹700 + a CESS-head Tax challan ₹200.
        deposit.PostPmt06(GstTaxHead.Central, GstMinorHead.Tax, Money.FromRupees(700m), bank, new(2025, 7, 25), "CPIN-J", "CIN-J");
        deposit.PostPmt06(GstTaxHead.Cess, GstMinorHead.Tax, Money.FromRupees(200m), bank, new(2025, 7, 25), "CPIN-JCESS", "CIN-JCESS");

        var m1 = GstQrmp.Build(c, FyStart, FyEnd).Quarters[1].Month1Pmt06;
        // 35% of the preceding quarter's cash EXCLUDING cess = 35% × ₹1000 = ₹350 (NOT 35% × ₹1500 = ₹525).
        Assert.Equal(Money.FromRupees(350m), m1.FixedSum35PercentPriorQuarter);
        // 100% of June (last month) cash EXCLUDING cess = ₹1000 (NOT ₹1500).
        Assert.Equal(Money.FromRupees(1000m), m1.FixedSum100PercentLastMonth);
        // AlreadyDeposited excludes the July cess challan = ₹700 (NOT ₹900).
        Assert.Equal(Money.FromRupees(700m), m1.AlreadyDeposited);
    }

    // ================================================================ TEST 14: monthly / off / composition ⇒ not-applicable

    [Fact]
    public void Qrmp_is_not_applicable_for_a_monthly_off_or_composition_filer()
    {
        Assert.False(GstQrmp.Build(NewFiler(GstReturnPeriodicity.Monthly, "Monthly Co"), FyStart, FyEnd).Applicable);
        Assert.Empty(GstQrmp.Build(NewFiler(GstReturnPeriodicity.Monthly, "Monthly Co2"), FyStart, FyEnd).Quarters);

        var off = CompanyFactory.CreateSeeded("Off Co", FyStart);
        Assert.False(GstQrmp.Build(off, FyStart, FyEnd).Applicable);

        var comp = CompanyFactory.CreateSeeded("Comp Co", FyStart);
        new GstService(comp).EnableGst(new GstConfig
        {
            HomeStateCode = "27", Gstin = GstinMaharashtra, RegistrationType = GstRegistrationType.Composition,
            CompositionSubType = CompositionSubType.Trader, ApplicableFrom = FyStart, Periodicity = GstReturnPeriodicity.Quarterly,
        });
        Assert.False(GstQrmp.Build(comp, FyStart, FyEnd).Applicable);
    }

    // ================================================================ TEST 15: 9C CDNRA via the §34 link

    /// <summary>Posts an original outward sale and a §34 credit/debit note against it (reusing the S2b CDN engine).</summary>
    private static Voucher PostCdn(
        Company c, CdnType type, Domain.Ledger sales, Domain.Ledger debtor, decimal value, int rateBp,
        DateOnly cdnDate, Guid origVoucherId, int origNumber, DateOnly origDate)
    {
        var svc = new CreditDebitNoteService(c);
        var cdnVoucherId = Guid.NewGuid();
        var posting = svc.BuildCreditDebitNote(
            type, new[] { new GstService.TaxableLine(Money.FromRupees(value), rateBp) }, interState: false, cdnVoucherId,
            origVoucherId, origNumber.ToString(System.Globalization.CultureInfo.InvariantCulture), origDate, cdnDate,
            reasonCode: type == CdnType.Credit ? "01 sales return" : "04 upward revision");
        var tax = new Money(posting.Computed.TotalTax.Amount);
        var total = new Money(value + tax.Amount);
        var baseType = type == CdnType.Credit ? VoucherBaseType.CreditNote : VoucherBaseType.DebitNote;
        List<EntryLine> lines = type == CdnType.Credit
            ? new() { new(sales.Id, Money.FromRupees(value), DrCr.Debit), new(debtor.Id, total, DrCr.Credit) }
            : new() { new(debtor.Id, total, DrCr.Debit), new(sales.Id, Money.FromRupees(value), DrCr.Credit) };
        lines.AddRange(posting.TaxLines);
        var typeId = c.VoucherTypes.First(t => t.BaseType == baseType).Id;
        return new LedgerService(c).Post(new Voucher(cdnVoucherId, typeId, cdnDate, lines, partyId: debtor.Id));
    }

    [Fact]
    public void Table9c_surfaces_an_amended_cdn_against_a_prior_period_invoice_via_the_section34_link()
    {
        var c = NewFiler(GstReturnPeriodicity.Monthly, "CDNRA Co");
        var sales = Add(c, "Sales", "Sales Accounts", false);
        var debtor = RegisteredDebtor(c, "Debtor", GstinGujarat, "24");

        // Original B2B sale in April (₹5000 @18%).
        var orig = PostB2BSale(c, sales, debtor, 5000m, 1800, new(2025, 4, 10), number: 700);
        // A §34 CREDIT note in July against the April invoice (₹1000 @18% ⇒ reduces output ₹180).
        PostCdn(c, CdnType.Credit, sales, debtor, 1000m, 1800, new(2025, 7, 12), orig.Id, 700, new(2025, 4, 10));

        // The amendment view for July surfaces the CDN in Table 9C (CDNRA) — prior-period original, signed revised tax.
        var amend = Gstr1Amendments.Build(c, new(2025, 7, 1), new(2025, 7, 31));
        Assert.True(amend.Applicable);
        var row = Assert.Single(amend.Table9C);
        Assert.Equal("CDNRA", row.SectionCode);
        Assert.Equal("9C", row.FormTable);
        Assert.Equal(CdnType.Credit, row.NoteType);
        Assert.Equal(new DateOnly(2025, 4, 10), row.OriginalInvoiceDate);
        Assert.Equal("700", row.OriginalInvoiceNumber);
        // A credit note is signed negative (it reduces output) — ₹180 total (CGST 90 + SGST 90).
        Assert.Equal(Money.FromRupees(-180m), row.RevisedTax);
        Assert.Equal(Money.FromRupees(-1000m), row.RevisedTaxableValue);

        // A same-period (July) CDN against a July invoice is NOT an amendment (it is an ordinary Table 9B note).
        var sameMonth = Gstr1Amendments.Build(c, new(2025, 4, 1), new(2025, 4, 30));
        Assert.Empty(sameMonth.Table9C);
    }

    // ================================================================ TEST 16: 9A B2BA advisory delta + dedup

    [Fact]
    public void Table9a_surfaces_an_ordinary_b2b_restated_in_a_later_period_and_dedups_one_per_document()
    {
        var c = NewFiler(GstReturnPeriodicity.Monthly, "B2BA Co");
        var sales = Add(c, "Sales", "Sales Accounts", false);
        var debtor = RegisteredDebtor(c, "Debtor", GstinGujarat, "24");

        // Original B2B invoice #500 in April (₹1000 @18%).
        PostB2BSale(c, sales, debtor, 1000m, 1800, new(2025, 4, 5), number: 500);
        // Amendment: the SAME invoice #500 re-stated in July (₹1200 @18%).
        PostB2BSale(c, sales, debtor, 1200m, 1800, new(2025, 7, 5), number: 500);
        // A SECOND re-statement of #500 in July (₹1300) — one-amendment-per-document: it REPLACES, never stacks.
        PostB2BSale(c, sales, debtor, 1300m, 1800, new(2025, 7, 20), number: 500);

        var amend = Gstr1Amendments.Build(c, new(2025, 7, 1), new(2025, 7, 31));
        var row = Assert.Single(amend.Table9A);                              // deduped to one row
        Assert.Equal("B2BA", row.SectionCode);
        Assert.Equal("9A", row.FormTable);
        Assert.True(row.Advisory);
        Assert.Equal(500, row.OriginalDocNumber);
        Assert.Equal(new DateOnly(2025, 4, 5), row.OriginalDocDate);
        Assert.Equal(Money.FromRupees(1000m), row.OriginalTaxableValue);
        Assert.Equal(Money.FromRupees(1300m), row.RevisedTaxableValue);      // the LATEST re-statement is the revised value
        Assert.Equal(Money.FromRupees(300m), row.DifferentialTaxableValue);  // 1300 − 1000
        Assert.Equal(Money.FromRupees(234m), row.RevisedTax);                // 1300 @18%
    }

    [Fact]
    public void FormTable_maps_amendment_section_codes_to_form_tables_exactly()
    {
        Assert.Equal("9A", Gstr1Amendments.FormTableOf("B2BA"));
        Assert.Equal("9A", Gstr1Amendments.FormTableOf("B2CLA"));
        Assert.Equal("9A", Gstr1Amendments.FormTableOf("EXPA"));
        Assert.Equal("9C", Gstr1Amendments.FormTableOf("CDNRA"));
        Assert.Equal("9C", Gstr1Amendments.FormTableOf("CDNURA"));
        Assert.Equal("10", Gstr1Amendments.FormTableOf("B2CSA"));
    }

    // ================================================================ TEST 17: GSTR-3B correction flows to the next period

    [Fact]
    public void Gstr3b_correction_flows_to_the_subsequent_period_and_the_advisory_flags_it()
    {
        var c = NewFiler(GstReturnPeriodicity.Monthly, "3B Correction Co");
        var sales = Add(c, "Sales", "Sales Accounts", false);
        var debtor = RegisteredDebtor(c, "Debtor", GstinGujarat, "24");

        // Original April B2B sale (₹5000 @18% ⇒ output ₹900).
        var orig = PostB2BSale(c, sales, debtor, 5000m, 1800, new(2025, 4, 10), number: 800);
        var aprilOutward = Gstr3b.Build(c, new(2025, 4, 1), new(2025, 4, 30)).TotalOutwardTax;

        // A §34 credit note in July against the April invoice (₹1000 @18% ⇒ reduces output ₹180).
        PostCdn(c, CdnType.Credit, sales, debtor, 1000m, 1800, new(2025, 7, 12), orig.Id, 800, new(2025, 4, 10));

        // The correction flows into the SUBSEQUENT (July) GSTR-3B — a filed 3B is never directly amended.
        var aprilAfter = Gstr3b.Build(c, new(2025, 4, 1), new(2025, 4, 30)).TotalOutwardTax;
        Assert.Equal(aprilOutward, aprilAfter);                              // April 3B unchanged (₹900)
        Assert.Equal(Money.FromRupees(900m), aprilAfter);
        var julyOutward = Gstr3b.Build(c, new(2025, 7, 1), new(2025, 7, 31)).TotalOutwardTax;
        Assert.Equal(Money.FromRupees(-180m), julyOutward);                  // the CDN nets July outward down

        // The July 3B-correction advisory flags the prior-period (April) correction being declared in July.
        var adv = Gstr3bCorrectionAdvisory.Build(c, new(2025, 7, 1), new(2025, 7, 31));
        Assert.True(adv.Applicable);
        Assert.True(adv.RequiresCorrection);
        Assert.Equal(1, adv.CorrectionCount);
        Assert.Equal(Money.FromRupees(-180m), adv.PriorPeriodCorrectionTax);
        Assert.Contains("subsequent period", adv.Mechanism, StringComparison.OrdinalIgnoreCase);

        // A period with no prior-period correction needs none.
        var aprilAdv = Gstr3bCorrectionAdvisory.Build(c, new(2025, 4, 1), new(2025, 4, 30));
        Assert.False(aprilAdv.RequiresCorrection);
    }

    // ================================================================ ER-13: off company empty + Phase-4 byte-identical

    [Fact]
    public void Er13_an_off_company_yields_empty_s8b_views_and_unchanged_phase4_returns()
    {
        var off = CompanyFactory.CreateSeeded("Off Co", FyStart);
        var g1Before = Gstr1.Build(off, FyStart, FyEnd);
        var g3Before = Gstr3b.Build(off, FyStart, FyEnd);

        Assert.False(GstQrmp.Build(off, FyStart, FyEnd).Applicable);
        Assert.False(Gstr1Amendments.Build(off, FyStart, FyEnd).Applicable);
        Assert.Empty(Gstr1Amendments.Build(off, FyStart, FyEnd).Table9A);
        Assert.Empty(Gstr1Amendments.Build(off, FyStart, FyEnd).Table9C);
        Assert.False(Gstr3bCorrectionAdvisory.Build(off, FyStart, FyEnd).Applicable);

        var g1After = Gstr1.Build(off, FyStart, FyEnd);
        var g3After = Gstr3b.Build(off, FyStart, FyEnd);
        Assert.Equal(g1Before.TotalTax, g1After.TotalTax);
        Assert.Equal(g3Before.TotalOutwardTax, g3After.TotalOutwardTax);
        Assert.Equal(g3Before.TotalItc, g3After.TotalItc);
    }

    // ================================================================ determinism

    [Fact]
    public void S8b_projections_are_deterministic_across_runs()
    {
        var c = NewFiler(GstReturnPeriodicity.Quarterly, "Determinism Co");
        var sales = Add(c, "Sales", "Sales Accounts", false);
        var debtor = RegisteredDebtor(c, "Debtor", GstinGujarat, "24");
        PostB2BSale(c, sales, debtor, 1000m, 1800, new(2025, 7, 5), number: 900);
        PostB2BSale(c, sales, debtor, 1200m, 1800, new(2025, 10, 5), number: 900);

        var a = GstQrmp.Build(c, FyStart, FyEnd);
        var b = GstQrmp.Build(c, FyStart, FyEnd);
        Assert.Equal(a.Quarters.Count, b.Quarters.Count);
        Assert.Equal(a.Quarters[1].Month1Iff.TaxableValue, b.Quarters[1].Month1Iff.TaxableValue);

        var am1 = Gstr1Amendments.Build(c, new(2025, 10, 1), new(2025, 10, 31));
        var am2 = Gstr1Amendments.Build(c, new(2025, 10, 1), new(2025, 10, 31));
        Assert.Equal(am1.Table9A.Count, am2.Table9A.Count);
    }
}
