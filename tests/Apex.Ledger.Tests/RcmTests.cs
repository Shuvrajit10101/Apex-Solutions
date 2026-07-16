using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// Phase 9 slice 2 — RCM (reverse charge) core (RQ-3/RQ-7/RQ-8/RQ-11; ER-3). Proves: the balanced dual leg (RCM Output
/// liability + Input ITC), the intra CGST/SGST vs inter/import IGST split, the cash-only structural invariant (own RCM
/// Output ledger, discharged by cash), §9(4) promoter-only default-OFF, import-of-services (4A(2)) vs import-of-goods
/// (fail-fast, excluded), self-invoice ≤30-day + registered-supplier suppression, GSTR-3B 3.1(d)/4A buckets with no
/// double-count, cess-on-RCM ring-fence, the shared time-of-supply helper, and ER-13 byte-identity when RCM is off.
/// All pure, deterministic, paisa-exact.
/// </summary>
public sealed class RcmTests
{
    private const string GstinMaharashtra = "27AAPFU0939F1ZV";
    private const string GstinGujarat = "24AAACC1206D1ZM";
    private static readonly DateOnly FyStart = new(2025, 4, 1);
    private static readonly DateOnly D1 = new(2025, 4, 10);
    private static readonly DateOnly PreCutover = new(2025, 9, 20);
    private static readonly DateOnly PostCutover = new(2025, 9, 25);

    private static Company NewRcmCompany()
    {
        var c = CompanyFactory.CreateSeeded("RCM Traders", FyStart);
        var gst = new GstService(c);
        gst.EnableGst(new GstConfig
        {
            HomeStateCode = "27",
            Gstin = GstinMaharashtra,
            RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart,
            Periodicity = GstReturnPeriodicity.Monthly,
        });
        gst.SeedAdvancedGst(); // seeds the dated rate history + cess windows + the notified RCM categories
        return c;
    }

    private static RcmCategory Cat(Company c, string nature) =>
        c.Gst!.RcmCategories.First(x => x.SupplyNature == nature);

    private static Domain.Ledger AddLedger(Company c, string name, string groupName, bool openingIsDebit)
    {
        var l = new Domain.Ledger(Guid.NewGuid(), name, c.FindGroupByName(groupName)!.Id, Money.Zero, openingIsDebit);
        c.AddLedger(l);
        return l;
    }

    /// <summary>An expense/purchase ledger flagged reverse-charge for a service, linked to a seeded category.</summary>
    private static Domain.Ledger RcmExpenseLedger(Company c, string name, string nature, int rateBp, GstSupplyType type = GstSupplyType.Services, string? hsn = null)
    {
        var l = AddLedger(c, name, "Indirect Expenses", true);
        l.SalesPurchaseGst = new StockItemGstDetails
        {
            HsnSac = hsn,
            Taxability = GstTaxability.Taxable,
            RateBasisPoints = rateBp,
            SupplyType = type,
            ReverseChargeApplicable = true,
            RcmCategoryId = Cat(c, nature).Id,
        };
        return l;
    }

    private static Domain.Ledger Party(Company c, string name, string? gstin, string? state, bool unregistered = false, bool promoter = false, bool bodyCorporate = false)
    {
        var l = AddLedger(c, name, "Sundry Creditors", false);
        l.PartyGst = new PartyGstDetails
        {
            RegistrationType = unregistered ? GstRegistrationType.Unregistered : GstRegistrationType.Regular,
            Gstin = gstin,
            StateCode = state,
            IsPromoter = promoter,
            IsBodyCorporate = bodyCorporate,
        };
        return l;
    }

    /// <summary>Assembles + posts the RCM inward Purchase (Dr Expense / Cr Party + the RCM dual pair) and returns it.</summary>
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

    // ---------------------------------------------------------------- 1. Dual-leg balances to the paisa (inter, OtherRcm)

    [Fact]
    public void Rcm_inward_service_interstate_posts_balanced_output_liability_and_itc_pair()
    {
        var c = NewRcmCompany();
        var rcm = new RcmService(c);
        var expense = RcmExpenseLedger(c, "Legal Fees", "Legal", 1800);
        var party = Party(c, "Advocate (Gujarat)", GstinGujarat, "24");

        var posting = rcm.BuildReverseCharge(Money.FromRupees(10000m), null, expense, party.PartyGst, D1, RcmService.SupplyKind.Domestic);
        Assert.True(posting.Applies);

        var v = PostRcmInward(c, expense, party, Money.FromRupees(10000m), posting, D1);
        Assert.True(VoucherValidator.IsBalanced(v)); // Σ Dr 11,800 == Σ Cr 11,800

        var gst = new GstService(c);
        // The output liability lands in the dedicated RCM Output IGST ledger — NEVER the normal Output IGST.
        Assert.Equal(1800m, LedgerBalances.SignedClosing(c, gst.FindRcmOutputLedger(GstTaxHead.Integrated)!, D1) * -1);
        Assert.Equal(0m, LedgerBalances.SignedClosing(c, gst.FindTaxLedger(GstTaxHead.Integrated, GstTaxDirection.Output)!, D1));
        // The ITC lands in the ordinary Input IGST ledger, tagged reverse-charge + OtherRcm.
        Assert.Equal(1800m, LedgerBalances.SignedClosing(c, gst.FindTaxLedger(GstTaxHead.Integrated, GstTaxDirection.Input)!, D1));
        var itc = v.Lines.Single(l => l.Gst is { IsReverseCharge: true, RcmScheme: RcmItcScheme.OtherRcm });
        Assert.Equal(DrCr.Debit, itc.Side);
        Assert.Equal(GstTaxHead.Integrated, itc.Gst!.TaxHead);
    }

    // ---------------------------------------------------------------- 2. POS split (intra ⇒ CGST + SGST)

    [Fact]
    public void Rcm_inward_intrastate_splits_cgst_sgst_on_both_legs()
    {
        var c = NewRcmCompany();
        var rcm = new RcmService(c);
        var expense = RcmExpenseLedger(c, "Legal Fees", "Legal", 1800);
        var party = Party(c, "Advocate (Maharashtra)", GstinMaharashtra, "27"); // same state ⇒ intra

        var posting = rcm.BuildReverseCharge(Money.FromRupees(10000m), null, expense, party.PartyGst, D1, RcmService.SupplyKind.Domestic);
        var v = PostRcmInward(c, expense, party, Money.FromRupees(10000m), posting, D1);
        Assert.True(VoucherValidator.IsBalanced(v));

        var gst = new GstService(c);
        Assert.Equal(900m, LedgerBalances.SignedClosing(c, gst.FindRcmOutputLedger(GstTaxHead.Central)!, D1) * -1);
        Assert.Equal(900m, LedgerBalances.SignedClosing(c, gst.FindRcmOutputLedger(GstTaxHead.State)!, D1) * -1);
        Assert.Equal(900m, LedgerBalances.SignedClosing(c, gst.FindTaxLedger(GstTaxHead.Central, GstTaxDirection.Input)!, D1));
        Assert.Equal(900m, LedgerBalances.SignedClosing(c, gst.FindTaxLedger(GstTaxHead.State, GstTaxDirection.Input)!, D1));
        // No IGST leg on an intra supply.
        Assert.Null(gst.FindRcmOutputLedger(GstTaxHead.Integrated));
    }

    // ---------------------------------------------------------------- 3. Cash-only (structural): discharge is a cash payment

    [Fact]
    public void Rcm_output_liability_is_discharged_by_a_cash_payment_never_the_credit_ledger()
    {
        var c = NewRcmCompany();
        var rcm = new RcmService(c);
        var expense = RcmExpenseLedger(c, "Legal Fees", "Legal", 1800);
        var party = Party(c, "Advocate (Gujarat)", GstinGujarat, "24");
        var posting = rcm.BuildReverseCharge(Money.FromRupees(10000m), null, expense, party.PartyGst, D1, RcmService.SupplyKind.Domestic);
        PostRcmInward(c, expense, party, Money.FromRupees(10000m), posting, D1);

        var gst = new GstService(c);
        var rcmIgst = gst.FindRcmOutputLedger(GstTaxHead.Integrated)!;
        var bank = AddLedger(c, "Bank", "Bank Accounts", true);
        var ledgers = new LedgerService(c);

        // Discharge in cash: Dr RCM Output IGST / Cr Bank — structurally distinct from the credit/ITC ledger.
        var pay = ledgers.Post(new Voucher(Guid.NewGuid(), c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Payment).Id, new DateOnly(2025, 5, 5),
            new List<EntryLine> { new(rcmIgst.Id, Money.FromRupees(1800m), DrCr.Debit), new(bank.Id, Money.FromRupees(1800m), DrCr.Credit) }));
        Assert.True(VoucherValidator.IsBalanced(pay));
        // After the cash discharge the RCM Output liability nets to zero.
        Assert.Equal(0m, LedgerBalances.SignedClosing(c, rcmIgst, new DateOnly(2025, 5, 31)));
    }

    // ---------------------------------------------------------------- 4. §9(4) promoter-only + cement dated rate

    [Fact]
    public void Section_9_4_fires_only_for_the_promoter_and_resolves_the_dated_cement_rate()
    {
        var c = NewRcmCompany();
        var rcm = new RcmService(c);
        var cement = RcmExpenseLedger(c, "Cement Purchase", "Cement", 2800, GstSupplyType.Goods, hsn: "2523");
        var unregistered = Party(c, "Local Cement Dealer", gstin: null, state: "27", unregistered: true);

        // No promoter profile ⇒ blanket §9(4) is OFF by default (no RCM leg).
        var off = rcm.BuildReverseCharge(Money.FromRupees(100000m), null, cement, unregistered.PartyGst, PostCutover, RcmService.SupplyKind.Domestic);
        Assert.False(off.Applies);
        Assert.Empty(off.Lines);

        // Promoter recipient ⇒ §9(4) fires; the rate resolves through the S1 dated HSN-2523 history.
        var pre = rcm.BuildReverseCharge(Money.FromRupees(100000m), null, cement, unregistered.PartyGst, PreCutover, RcmService.SupplyKind.Domestic, recipientIsPromoter: true);
        Assert.True(pre.Applies);
        Assert.Equal(2800, pre.Resolution.RateBasisPoints); // 28% before 22-Sep-2025

        var post = rcm.BuildReverseCharge(Money.FromRupees(100000m), null, cement, unregistered.PartyGst, PostCutover, RcmService.SupplyKind.Domestic, recipientIsPromoter: true);
        Assert.True(post.Applies);
        Assert.Equal(1800, post.Resolution.RateBasisPoints); // 18% from 22-Sep-2025
    }

    // ---------------------------------------------------------------- 5. Import of services (4A(2)) vs goods (excluded)

    [Fact]
    public void Import_of_services_is_igst_rcm_import_of_goods_is_excluded()
    {
        var c = NewRcmCompany();
        var rcm = new RcmService(c);
        var expense = RcmExpenseLedger(c, "Foreign Consulting", "Legal", 1800);
        var foreign = Party(c, "Overseas Consultant", gstin: null, state: null, unregistered: true);

        var svc = rcm.BuildReverseCharge(Money.FromRupees(10000m), null, expense, foreign.PartyGst, D1, RcmService.SupplyKind.ImportOfServices);
        Assert.True(svc.Applies);
        Assert.Equal(RcmItcScheme.ImportOfServices, svc.Resolution.Scheme);
        Assert.True(svc.Resolution.InterState); // import of services is always IGST
        Assert.Contains(svc.Lines, l => l.Gst is { IsReverseCharge: true, RcmScheme: RcmItcScheme.ImportOfServices });

        // Import of goods is NEVER reverse charge (customs IGST, 4A(1)) — a hard fail-fast.
        Assert.Throws<InvalidOperationException>(() =>
            rcm.BuildReverseCharge(Money.FromRupees(10000m), null, expense, foreign.PartyGst, D1, RcmService.SupplyKind.ImportOfGoods));
    }

    // ---------------------------------------------------------------- 6. Self-invoice (Rule 47A) + payment voucher (Rule 52)

    [Fact]
    public void Self_invoice_generated_for_unregistered_supplier_within_30_days_only()
    {
        var c = NewRcmCompany();
        var rcm = new RcmService(c);
        var sourceVoucherId = Guid.NewGuid();

        // Unregistered supplier ⇒ a self-invoice is generated (own series), constrained ≤ receipt + 30 days.
        var doc = rcm.GenerateSelfInvoice(sourceVoucherId, D1, D1.AddDays(30), supplierIsRegistered: false);
        Assert.NotNull(doc);
        Assert.Equal(RcmDocumentKind.SelfInvoice, doc!.Kind);
        Assert.Equal(1, doc.SeriesNumber);

        // A registered §9(3) supplier issues its own invoice ⇒ NO self-invoice.
        Assert.Null(rcm.GenerateSelfInvoice(Guid.NewGuid(), D1, D1.AddDays(5), supplierIsRegistered: true));

        // A self-invoice dated > 30 days after receipt is rejected (Rule 47A).
        Assert.Throws<InvalidOperationException>(() =>
            rcm.GenerateSelfInvoice(Guid.NewGuid(), D1, D1.AddDays(31), supplierIsRegistered: false));

        // The payment voucher (Rule 52) is a separate document with its own series.
        var pv = rcm.GeneratePaymentVoucher(sourceVoucherId, new DateOnly(2025, 5, 1));
        Assert.Equal(RcmDocumentKind.PaymentVoucher, pv.Kind);
        Assert.Equal(1, pv.SeriesNumber);
    }

    // ---------------------------------------------------------------- 7. GSTR-3B RCM buckets (3.1(d) / 4A) + no double-count

    [Fact]
    public void Gstr3b_buckets_rcm_output_and_itc_without_double_counting_normal_itc()
    {
        var c = NewRcmCompany();
        var rcm = new RcmService(c);
        var expense = RcmExpenseLedger(c, "Legal Fees", "Legal", 1800);
        var party = Party(c, "Advocate (Gujarat)", GstinGujarat, "24");
        var posting = rcm.BuildReverseCharge(Money.FromRupees(10000m), null, expense, party.PartyGst, D1, RcmService.SupplyKind.Domestic);
        PostRcmInward(c, expense, party, Money.FromRupees(10000m), posting, D1);

        var b = Gstr3b.Build(c, FyStart, new DateOnly(2026, 3, 31));
        // 3.1(d) RCM outward liability = 1,800 IGST.
        Assert.Equal(1800m, b.RcmOutwardIgst.Amount);
        Assert.Equal(1800m, b.TotalRcmOutward.Amount);
        // 4A(3) "other RCM" ITC = 1,800 IGST; 4A(2) import = 0.
        Assert.Equal(1800m, b.RcmItcOtherIgst.Amount);
        Assert.Equal(0m, b.RcmItcImportIgst.Amount);
        // The normal 4A(5) "all other ITC" buckets EXCLUDE the RCM line (no double-count).
        Assert.Equal(0m, b.ItcIgst.Amount);
        // A purchase carries no OUTWARD forward tax — 3.1(a) unaffected.
        Assert.Equal(0m, b.TotalOutwardTax.Amount);
    }

    // ---------------------------------------------------------------- 8. Cess-on-RCM (interaction, ring-fenced)

    [Fact]
    public void Rcm_line_bearing_cess_posts_a_ring_fenced_cess_pair()
    {
        var c = NewRcmCompany();
        var rcm = new RcmService(c);
        // A cess-bearing imported good under a promoter scenario — the §9(4) "Capital-goods" category (18% RCM) on an
        // item whose HSN 8703 (car) also bears a 22% ad-valorem cess in window (a).
        var goodExpense = AddLedger(c, "Imported Capital Good (promoter)", "Indirect Expenses", true);
        goodExpense.SalesPurchaseGst = new StockItemGstDetails
        {
            HsnSac = "8703", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800, SupplyType = GstSupplyType.Goods,
            ReverseChargeApplicable = true, RcmCategoryId = Cat(c, "Capital-goods").Id, // §9(4) promoter category (goods)
        };
        var unregistered = Party(c, "Unregistered Dealer", gstin: null, state: "27", unregistered: true);

        var posting = rcm.BuildReverseCharge(Money.FromRupees(100000m), null, goodExpense, unregistered.PartyGst,
            PreCutover, RcmService.SupplyKind.Domestic, recipientIsPromoter: true, quantity: 1m);
        Assert.True(posting.Applies);
        var v = PostRcmInward(c, goodExpense, unregistered, Money.FromRupees(100000m), posting, PreCutover);
        Assert.True(VoucherValidator.IsBalanced(v));

        var gst = new GstService(c);
        // The cess liability + ITC are their own pair, ring-fenced out of the CGST/SGST/IGST heads (ER-2).
        var cessOut = gst.FindRcmOutputLedger(GstTaxHead.Cess)!;
        Assert.Equal(22000m, LedgerBalances.SignedClosing(c, cessOut, PreCutover) * -1); // 22% of 100,000
        Assert.Equal(22000m, LedgerBalances.SignedClosing(c, gst.FindTaxLedger(GstTaxHead.Cess, GstTaxDirection.Input)!, PreCutover));

        var b = Gstr3b.Build(c, FyStart, new DateOnly(2026, 3, 31));
        Assert.Equal(22000m, b.RcmOutwardCess.Amount);
        Assert.Equal(22000m, b.RcmItcOtherCess.Amount);
        // Cess never mingles with the RCM CGST/SGST/IGST buckets.
        Assert.Equal(18000m, b.RcmOutwardCgst.Amount + b.RcmOutwardSgst.Amount); // 18% CGST+SGST (intra)
    }

    // ---------------------------------------------------------------- 17. Shared time-of-supply helper

    [Fact]
    public void Time_of_supply_follows_section_12_3_goods_and_13_3_services()
    {
        // Services §13(3): ToS = earliest of { payment, the day immediately following 60 days from the supplier's
        // invoice = invoice + 61 }. The RAW invoice date is NOT a limb under RCM (that is forward-charge §13(2)).
        var inv = new DateOnly(2025, 4, 10);
        // A payment BEFORE the invoice crystallises first (payment 2025-04-05 < invoice+61 2025-06-10).
        Assert.Equal(new DateOnly(2025, 4, 5),
            RcmService.TimeOfSupply(GstSupplyType.Services, inv, receiptDate: null, paymentDate: new DateOnly(2025, 4, 5)));
        // No payment ⇒ the invoice+61 fallback crystallises (2025-04-10 + 61 = 2025-06-10) — the invoice date itself
        // (2025-04-10) is NOT selected, proving it is not a §13(3) limb.
        Assert.Equal(new DateOnly(2025, 6, 10), RcmService.TimeOfSupply(GstSupplyType.Services, inv, null, null));
        // A payment LATER than the invoice but WITHIN 60 days: the payment wins, the earlier invoice date is NOT chosen
        // (invoice 2025-04-01, payment 2025-05-01 < invoice+61 2025-06-01 ⇒ ToS = 2025-05-01, never 2025-04-01).
        Assert.Equal(new DateOnly(2025, 5, 1), RcmService.TimeOfSupply(
            GstSupplyType.Services, invoiceDate: new DateOnly(2025, 4, 1), receiptDate: null, paymentDate: new DateOnly(2025, 5, 1)));
        // With no invoice at all, the invoice+61 fallback does not apply — payment wins.
        Assert.Equal(new DateOnly(2025, 4, 5),
            RcmService.TimeOfSupply(GstSupplyType.Services, invoiceDate: null, receiptDate: null, paymentDate: new DateOnly(2025, 4, 5)));

        // Goods §12(3): ToS = earliest of { receipt of goods, payment, the day immediately following 30 days from the
        // supplier's invoice = invoice + 31 }. The fallback is anchored to the INVOICE, never the receipt date.
        var rcpt = new DateOnly(2025, 4, 1);
        // With no payment and no invoice, the receipt of goods is itself a limb and crystallises.
        Assert.Equal(rcpt, RcmService.TimeOfSupply(GstSupplyType.Goods, invoiceDate: null, receiptDate: rcpt, paymentDate: null));
        // The invoice+31 fallback wins over a much-later receipt (invoice 2025-04-01 + 31 = 2025-05-02 < receipt
        // 2025-12-01) — and it is invoice+31, NOT receipt+30 (which would have given 2025-12-31).
        Assert.Equal(new DateOnly(2025, 5, 2), RcmService.TimeOfSupply(
            GstSupplyType.Goods, invoiceDate: new DateOnly(2025, 4, 1), receiptDate: new DateOnly(2025, 12, 1), paymentDate: null));
    }

    // ---------------------------------------------------------------- ER-13: byte-identical when RCM is off

    [Fact]
    public void A_forward_charge_purchase_is_unaffected_when_rcm_is_off()
    {
        var c = NewRcmCompany();
        var rcm = new RcmService(c);
        // A plain expense ledger with NO reverse-charge flag ⇒ Resolve returns not-applicable, no lines.
        var plain = AddLedger(c, "Ordinary Rent", "Indirect Expenses", true);
        plain.SalesPurchaseGst = new StockItemGstDetails { Taxability = GstTaxability.Taxable, RateBasisPoints = 1800, SupplyType = GstSupplyType.Services };
        var party = Party(c, "Landlord", GstinGujarat, "24");

        var res = rcm.Resolve(plain.SalesPurchaseGst, party.PartyGst, null, plain, D1, RcmService.SupplyKind.Domestic);
        Assert.False(res.Applies);
        // No RCM Output ledgers are ever created for an off company.
        var gst = new GstService(c);
        Assert.Null(gst.FindRcmOutputLedger(GstTaxHead.Integrated));
        Assert.Null(gst.FindRcmOutputLedger(GstTaxHead.Central));
    }

    // ---------------------------------------------------------------- #4: per-unit cess on an RCM line + zero quantity fails fast

    [Fact]
    public void Rcm_line_with_per_unit_cess_but_zero_quantity_fails_fast()
    {
        var c = NewRcmCompany();
        var rcm = new RcmService(c);
        // A goods §9(4)-promoter RCM line whose cess is per-unit (Specific ₹400/tonne). BuildReverseCharge's default
        // quantity is 0, so a Specific cess computes round(0 × 400) = ₹0 that the `cess.Amount != 0` guard then SKIPS —
        // a silent under-collection. The engine must FAIL-FAST instead (mirrors the S1 RSP-no-RSP guard).
        var coal = AddLedger(c, "Coal (promoter, per-tonne cess)", "Indirect Expenses", true);
        coal.SalesPurchaseGst = new StockItemGstDetails
        {
            HsnSac = "2701", Taxability = GstTaxability.Taxable, RateBasisPoints = 500, SupplyType = GstSupplyType.Goods,
            ReverseChargeApplicable = true, RcmCategoryId = Cat(c, "Capital-goods").Id,
            CessApplicable = true, CessValuationMode = CessValuationMode.Specific, CessPerUnit = Money.FromRupees(400m),
        };
        var unregistered = Party(c, "Unregistered Coal Dealer", gstin: null, state: "27", unregistered: true);

        // quantity defaults to 0 ⇒ a clear domain error (never a silent ₹0 cess).
        var ex = Assert.Throws<InvalidOperationException>(() =>
            rcm.BuildReverseCharge(Money.FromRupees(100000m), null, coal, unregistered.PartyGst,
                PreCutover, RcmService.SupplyKind.Domestic, recipientIsPromoter: true)); // no quantity
        Assert.Contains("per-unit", ex.Message, StringComparison.OrdinalIgnoreCase);

        // Supplying a positive quantity values the per-unit cess (₹400 × 50 = ₹20,000) — no throw.
        var ok = rcm.BuildReverseCharge(Money.FromRupees(100000m), null, coal, unregistered.PartyGst,
            PreCutover, RcmService.SupplyKind.Domestic, recipientIsPromoter: true, quantity: 50m);
        Assert.True(ok.Applies);
        var gst = new GstService(c);
        Assert.Equal(20000m, ok.Lines.Where(l => l.Gst!.TaxHead == GstTaxHead.Cess && l.Side == DrCr.Credit).Sum(l => l.Amount.Amount));
    }

    // ---------------------------------------------------------------- #5: outward RCM supply is NOT double-counted in exempt

    [Fact]
    public void Outward_rcm_supply_is_not_swept_into_the_exempt_bucket()
    {
        var c = NewRcmCompany();
        var (sales, debtor, itemId, main) = SeedOutwardRcm(c);
        // An OUTWARD reverse-charge item sale of ₹10,000 (the sales ledger is flagged ReverseChargeApplicable; the
        // recipient pays the tax, so the invoice bears ZERO forward tax). It belongs ONLY in Table 4B / 3.1(d)-value.
        PostOutwardRcmSale(c, sales, debtor, itemId, main, qty: 100m, rate: Money.FromRupees(100m),
            VoucherBaseType.Sales, D1);

        var g1 = Gstr1.Build(c, FyStart, new DateOnly(2026, 3, 31));
        var g3 = Gstr3b.Build(c, FyStart, new DateOnly(2026, 3, 31));

        // The value is represented ONCE — in Table 4B — and is NOT also swept into the exempt/nil/non-GST bucket.
        Assert.Equal(10000m, g1.Rcm4BOutwardValue.Amount);
        Assert.Equal(0m, g1.ExemptNilNonGstValue.Amount);
        Assert.Equal(0m, g3.ExemptNilNonGstOutward.Amount);
        // Nor does it leak into the HSN summary (the outward-RCM sweep is skipped wholesale).
        Assert.DoesNotContain(g1.HsnSummary, h => h.HsnSac == "7204");
    }

    // ---------------------------------------------------------------- #6: a Credit Note against an outward RCM supply REDUCES 4B

    [Fact]
    public void Credit_note_against_an_outward_rcm_supply_reduces_the_table_4b_value()
    {
        var c = NewRcmCompany();
        var (sales, debtor, _, _) = SeedOutwardRcm(c);
        // An outward RCM sale of ₹10,000 (accounts-only; the sales-ledger line carries the supply value into 4B) …
        PostOutwardRcmAccounts(c, sales, debtor, Money.FromRupees(10000m), VoucherBaseType.Sales, D1);
        // … then a ₹2,000 Credit Note (sales return) against it. 4B must NET DOWN to ₹8,000, not inflate to ₹12,000.
        PostOutwardRcmAccounts(c, sales, debtor, Money.FromRupees(2000m), VoucherBaseType.CreditNote, new DateOnly(2025, 4, 20));

        var g1 = Gstr1.Build(c, FyStart, new DateOnly(2026, 3, 31));
        Assert.Equal(8000m, g1.Rcm4BOutwardValue.Amount);
    }

    // ---- outward-RCM test helpers ----

    /// <summary>Seeds outward-RCM infrastructure: a Sales ledger flagged reverse-charge, a taxable stock item with
    /// opening stock, and a registered debtor. The item is taxable, but an outward RCM sale posts ZERO tax (the
    /// recipient pays it) — so the supply's value belongs in Table 4B, never the exempt bucket.</summary>
    private static (Domain.Ledger Sales, Domain.Ledger Debtor, Guid ItemId, Guid Location) SeedOutwardRcm(Company c)
    {
        var inv = new InventoryService(c);
        var grp = inv.CreateStockGroup("Goods");
        var nos = inv.CreateSimpleUnit("Nos", "Numbers", unitQuantityCode: "NOS");
        var main = c.MainLocation!.Id;
        var item = inv.CreateStockItem("Scrap Metal", grp.Id, nos.Id);
        item.Gst = new StockItemGstDetails { HsnSac = "7204", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800 };
        inv.AddOpeningBalance(item.Id, main, 1000m, Money.FromRupees(100m));

        var sales = AddLedger(c, "RCM Sales", "Sales Accounts", false);
        sales.SalesPurchaseGst = new StockItemGstDetails
        {
            HsnSac = "7204", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800, SupplyType = GstSupplyType.Goods,
            ReverseChargeApplicable = true, // outward: the recipient pays RCM (GSTR-1 Table 4B)
        };
        var debtor = AddLedger(c, "Metal Buyer", "Sundry Debtors", true);
        debtor.PartyGst = new PartyGstDetails { RegistrationType = GstRegistrationType.Regular, Gstin = GstinGujarat, StateCode = "24" };
        return (sales, debtor, item.Id, main);
    }

    /// <summary>Posts an outward-RCM item sale / return (ZERO tax) with an inventory line.</summary>
    private static Voucher PostOutwardRcmSale(
        Company c, Domain.Ledger sales, Domain.Ledger debtor, Guid itemId, Guid main,
        decimal qty, Money rate, VoucherBaseType baseType, DateOnly date)
    {
        var value = new Money(qty * rate.Amount);
        var lines = baseType == VoucherBaseType.CreditNote
            ? new List<EntryLine> { new(sales.Id, value, DrCr.Debit), new(debtor.Id, value, DrCr.Credit) }
            : new List<EntryLine> { new(debtor.Id, value, DrCr.Debit), new(sales.Id, value, DrCr.Credit) };
        var typeId = c.VoucherTypes.First(t => t.BaseType == baseType).Id;
        return new LedgerService(c).Post(new Voucher(Guid.NewGuid(), typeId, date, lines, partyId: debtor.Id,
            inventoryLines: new[] { new VoucherInventoryLine(itemId, main, qty, rate) }));
    }

    /// <summary>Posts an accounts-only outward-RCM sale / credit note (ZERO tax; the sales-ledger line carries the value).</summary>
    private static Voucher PostOutwardRcmAccounts(
        Company c, Domain.Ledger sales, Domain.Ledger debtor, Money value, VoucherBaseType baseType, DateOnly date)
    {
        var lines = baseType == VoucherBaseType.CreditNote
            ? new List<EntryLine> { new(sales.Id, value, DrCr.Debit), new(debtor.Id, value, DrCr.Credit) }
            : new List<EntryLine> { new(debtor.Id, value, DrCr.Debit), new(sales.Id, value, DrCr.Credit) };
        var typeId = c.VoucherTypes.First(t => t.BaseType == baseType).Id;
        return new LedgerService(c).Post(new Voucher(Guid.NewGuid(), typeId, date, lines, partyId: debtor.Id));
    }
}
