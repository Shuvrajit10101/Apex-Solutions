using System;
using System.IO;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Xunit;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.Tests;

/// <summary>
/// Phase 9 UI-3 — the <b>reverse-charge (RCM) inward-supply voucher-entry UI</b> (<see cref="VoucherEntryViewModel"/>;
/// RQ-3/RQ-7/RQ-8/RQ-11; ER-3). Proves the plain-grid Purchase/Journal screen resolves applicability through the SAME
/// <see cref="RcmService"/> the posting uses (ER-4, no re-implementation), that Accept appends the engine's balanced
/// <b>dual leg</b> — Cr "RCM Output {head}" (the cash-only §49(4) liability) + Dr "Input {head}" (the matching credit) —
/// paisa-exact, and that the Rule-47A self-invoice / Rule-52 payment voucher are generated off the posted voucher.
/// <para>
/// Also locks the ER-13 gates: a GST-off company, a non-RCM expense ledger, and a supply for which no notified category
/// fires all post <b>verbatim</b> with no RCM ledger conjured into existence.
/// </para>
/// </summary>
public sealed class RcmVoucherEntryViewModelTests : IDisposable
{
    private const string GstinMaharashtra = "27AAPFU0939F1ZV";
    private const string GstinGujarat = "24AAACC1206D1ZM";
    private static readonly DateOnly FyStart = new(2025, 4, 1);
    private static readonly DateOnly D1 = new(2025, 4, 10);

    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public RcmVoucherEntryViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexRcmVoucherTests_" + Guid.NewGuid().ToString("N"));
        _storage = new CompanyStorage(_tempDir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch { /* best-effort cleanup */ }
    }

    private MainWindowViewModel NewCompany(string name)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = name;
        vm.CreateCompany();
        var c = vm.Company!;
        c.FinancialYearStart = FyStart;
        c.BooksBeginFrom = FyStart;
        return vm;
    }

    private static DomainLedger AddLedger(Company c, string name, string groupName, bool openingIsDebit)
    {
        var l = new DomainLedger(Guid.NewGuid(), name, c.FindGroupByName(groupName)!.Id, Money.Zero, openingIsDebit);
        c.AddLedger(l);
        return l;
    }

    /// <summary>A Regular-GST (Maharashtra) company with the notified RCM categories + dated rate history seeded.</summary>
    private MainWindowViewModel GstCompany(string name)
    {
        var vm = NewCompany(name);
        var gst = new GstService(vm.Company!);
        gst.EnableGst(new GstConfig
        {
            HomeStateCode = "27",
            Gstin = GstinMaharashtra,
            RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart,
            Periodicity = GstReturnPeriodicity.Monthly,
        });
        gst.SeedAdvancedGst();
        return vm;
    }

    /// <summary>An expense ledger flagged reverse-charge, linked to a seeded notified category.</summary>
    private static DomainLedger RcmExpense(
        Company c, string name, string nature, int rateBp,
        GstSupplyType type = GstSupplyType.Services, string? hsn = null)
    {
        var l = AddLedger(c, name, "Indirect Expenses", true);
        l.SalesPurchaseGst = new StockItemGstDetails
        {
            HsnSac = hsn,
            Taxability = GstTaxability.Taxable,
            RateBasisPoints = rateBp,
            SupplyType = type,
            ReverseChargeApplicable = true,
            RcmCategoryId = c.Gst!.RcmCategories.First(x => x.SupplyNature == nature).Id,
        };
        return l;
    }

    private static DomainLedger Supplier(
        Company c, string name, string? gstin, string? state, bool unregistered = false)
    {
        var l = AddLedger(c, name, "Sundry Creditors", false);
        l.PartyGst = new PartyGstDetails
        {
            RegistrationType = unregistered ? GstRegistrationType.Unregistered : GstRegistrationType.Regular,
            Gstin = gstin,
            StateCode = state,
        };
        return l;
    }

    /// <summary>Opens a plain-grid Purchase and types the ordinary RCM inward legs: Dr Expense / Cr Supplier (the
    /// supplier charges NO tax on a reverse-charge supply, so the debit IS the assessable value).</summary>
    private static VoucherEntryViewModel OpenInward(
        MainWindowViewModel vm, DomainLedger expense, DomainLedger supplier, decimal value,
        VoucherBaseType type = VoucherBaseType.Purchase)
    {
        vm.OpenVoucher(type);
        var e = vm.VoucherEntry!;
        e.Date = D1;
        e.Lines[0].SelectedLedger = expense;
        e.Lines[0].Side = DrCr.Debit;
        e.Lines[0].AmountText = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        e.Lines[1].SelectedLedger = supplier;
        e.Lines[1].Side = DrCr.Credit;
        e.Lines[1].AmountText = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        e.Recalculate();
        return e;
    }

    private static Voucher LastVoucher(Company c) => c.Vouchers.OrderBy(v => v.Number).Last();

    private static Money AmountOn(Voucher v, Guid ledgerId, DrCr side) =>
        v.Lines.Where(l => l.LedgerId == ledgerId && l.Side == side)
            .Aggregate(Money.Zero, (a, l) => a + l.Amount);

    // ================================================================ ER-13 — the panel never appears when RCM is off

    /// <summary>ER-13: a GST-off company's plain purchase shows no RCM panel and posts verbatim (2 lines).</summary>
    [Fact]
    public void Panel_hidden_and_posting_verbatim_on_a_gst_off_company()
    {
        var vm = NewCompany("No-GST Co"); // GST never enabled
        var c = vm.Company!;
        var freight = AddLedger(c, "Freight", "Indirect Expenses", true);
        var carrier = AddLedger(c, "Carrier", "Sundry Creditors", false);

        var e = OpenInward(vm, freight, carrier, 10000m);

        Assert.False(e.ShowRcmPanel);
        Assert.True(e.Accept());
        Assert.Equal(2, LastVoucher(c).Lines.Count); // byte-identical: no RCM pair
    }

    /// <summary>ER-13: on a GST company, an expense ledger that is NOT reverse-charge-flagged shows no panel and posts
    /// verbatim — the flag is the master gate (mirroring TDS's Is-TDS-Applicable).</summary>
    [Fact]
    public void Panel_hidden_when_the_expense_ledger_is_not_reverse_charge_flagged()
    {
        var vm = GstCompany("Plain-GST Co");
        var c = vm.Company!;
        var stationery = AddLedger(c, "Stationery", "Indirect Expenses", true); // no SalesPurchaseGst at all
        var supplier = Supplier(c, "Paper Mart", GstinMaharashtra, "27");

        var e = OpenInward(vm, stationery, supplier, 5000m);

        Assert.False(e.ShowRcmPanel);
        Assert.True(e.Accept());
        Assert.Equal(2, LastVoucher(c).Lines.Count);
    }

    // ================================================================ happy path — the balanced dual leg, paisa-exact

    /// <summary>
    /// The screen previews the engine's resolution (applies / rate / place of supply / tax) and Accept appends the
    /// balanced dual leg: Cr <b>RCM Output IGST</b> ₹1,800 (the cash-only liability) + Dr <b>Input IGST</b> ₹1,800 (the
    /// matching credit), on top of Dr Legal Fees ₹10,000 / Cr Advocate ₹10,000 — paisa-exact and balanced.
    /// </summary>
    [Fact]
    public void Interstate_domestic_rcm_previews_and_posts_the_balanced_dual_leg()
    {
        var vm = GstCompany("RCM Legal Co");
        var c = vm.Company!;
        var fees = RcmExpense(c, "Legal Fees", "Legal", 1800);
        var advocate = Supplier(c, "Advocate (Gujarat)", GstinGujarat, "24");

        var e = OpenInward(vm, fees, advocate, 10000m);

        // ---- the live preview mirrors the engine
        Assert.True(e.ShowRcmPanel);
        Assert.Equal("Yes — reverse charge applies", e.RcmAppliesText);
        Assert.Equal("18%", e.RcmRateText);
        Assert.Equal("Inter-State (IGST)", e.RcmPosText);
        Assert.Equal("1,800.00", e.RcmTaxText);
        Assert.Contains("CASH", e.RcmSummary); // §49(4) — the RCM liability is never credit-offset
        Assert.Contains("4A(3)", e.RcmSummary); // OtherRcm bucket

        Assert.True(e.Accept());

        // ---- the posted dual leg, paisa-exact
        var v = LastVoucher(c);
        var gst = new GstService(c);
        var rcmOutIgst = gst.FindRcmOutputLedger(GstTaxHead.Integrated)!;
        var inputIgst = gst.FindTaxLedger(GstTaxHead.Integrated, GstTaxDirection.Input)!;

        Assert.True(VoucherValidator.IsBalanced(v));                       // Σ Dr 11,800 == Σ Cr 11,800
        Assert.Equal(4, v.Lines.Count);                                    // 2 ordinary + the RCM pair
        Assert.Equal(1800.00m, AmountOn(v, rcmOutIgst.Id, DrCr.Credit).Amount);  // liability leg
        Assert.Equal(1800.00m, AmountOn(v, inputIgst.Id, DrCr.Debit).Amount);    // ITC leg — the SAME amount
        Assert.Equal(10000.00m, AmountOn(v, fees.Id, DrCr.Debit).Amount);
        Assert.Equal(10000.00m, AmountOn(v, advocate.Id, DrCr.Credit).Amount);   // the supplier charges NO tax

        // The liability is structurally cash-only: it lands in its OWN ledger, never the ordinary Output IGST.
        var ordinaryOutIgst = gst.FindTaxLedger(GstTaxHead.Integrated, GstTaxDirection.Output)!;
        Assert.NotEqual(ordinaryOutIgst.Id, rcmOutIgst.Id);
        Assert.Equal(0m, AmountOn(v, ordinaryOutIgst.Id, DrCr.Credit).Amount);
    }

    /// <summary>An intra-state RCM supply splits the SAME total into CGST+SGST (₹900 + ₹900 on ₹10,000 @ 18%), each
    /// landing in its own RCM Output ledger with a matching Input leg.</summary>
    [Fact]
    public void Intrastate_domestic_rcm_posts_the_cgst_sgst_pair()
    {
        var vm = GstCompany("RCM Intra Co");
        var c = vm.Company!;
        var fees = RcmExpense(c, "Legal Fees", "Legal", 1800);
        var advocate = Supplier(c, "Advocate (Mumbai)", GstinMaharashtra, "27"); // same state as home

        var e = OpenInward(vm, fees, advocate, 10000m);

        Assert.True(e.ShowRcmPanel);
        Assert.Equal("Intra-State (CGST+SGST)", e.RcmPosText);
        Assert.Equal("1,800.00", e.RcmTaxText);
        Assert.True(e.Accept());

        var v = LastVoucher(c);
        var gst = new GstService(c);
        Assert.True(VoucherValidator.IsBalanced(v));
        Assert.Equal(900.00m, AmountOn(v, gst.FindRcmOutputLedger(GstTaxHead.Central)!.Id, DrCr.Credit).Amount);
        Assert.Equal(900.00m, AmountOn(v, gst.FindRcmOutputLedger(GstTaxHead.State)!.Id, DrCr.Credit).Amount);
        Assert.Equal(900.00m, AmountOn(v, gst.FindTaxLedger(GstTaxHead.Central, GstTaxDirection.Input)!.Id, DrCr.Debit).Amount);
        Assert.Equal(900.00m, AmountOn(v, gst.FindTaxLedger(GstTaxHead.State, GstTaxDirection.Input)!.Id, DrCr.Debit).Amount);
    }

    // ================================================================ the guard — a supply that does NOT attract RCM

    /// <summary>
    /// The engine guard surfaces cleanly: §9(4) fires <b>only</b> for a promoter recipient (Notn 7/2019), so the blanket
    /// §9(4) is OFF by default — the panel says "forward charge" and Accept posts <b>no</b> RCM legs. Ticking the
    /// promoter checkbox makes the very same grid fire the dual leg, proving the checkbox is genuinely wired through to
    /// <see cref="RcmService.Resolve"/> (not decorative).
    /// </summary>
    [Fact]
    public void Section_9_4_promoter_checkbox_gates_the_dual_leg()
    {
        var vm = GstCompany("Promoter Co");
        var c = vm.Company!;
        var cement = RcmExpense(c, "Cement Purchase", "Cement", 2800, GstSupplyType.Goods, hsn: "2523");
        var dealer = Supplier(c, "Local Cement Dealer", gstin: null, state: "27", unregistered: true);

        var e = OpenInward(vm, cement, dealer, 100000m);

        // Default (not a promoter) ⇒ §9(4) does NOT fire.
        Assert.True(e.ShowRcmPanel);                       // the shape holds — the panel explains WHY it does not apply
        Assert.Equal("No — forward charge", e.RcmAppliesText);
        Assert.Equal("0.00", e.RcmTaxText);

        // Tick "we are a promoter" ⇒ the same grid now fires §9(4).
        e.RcmRecipientIsPromoter = true;
        Assert.Equal("Yes — reverse charge applies", e.RcmAppliesText);
        Assert.Equal("28%", e.RcmRateText);                // 28% before the 22-Sep-2025 cement cut-over

        // Untick ⇒ back to forward charge, and the voucher posts with NO RCM legs at all.
        e.RcmRecipientIsPromoter = false;
        Assert.Equal("No — forward charge", e.RcmAppliesText);
        Assert.True(e.Accept());

        var v = LastVoucher(c);
        Assert.Equal(2, v.Lines.Count); // verbatim — no dual leg
        Assert.Null(new GstService(c).FindRcmOutputLedger(GstTaxHead.Central)); // no RCM ledger conjured
    }

    /// <summary>
    /// <b>The preview must not mutate the company.</b> <see cref="RcmService.BuildReverseCharge"/> lazily CREATES the
    /// "RCM Output {head}" ledgers, so previewing through it would conjure them on a company that never posts an RCM
    /// voucher — an ER-13 break. Merely showing the panel (even with RCM applying) must leave the ledger set untouched;
    /// only Accept may create them.
    /// </summary>
    [Fact]
    public void Live_preview_creates_no_rcm_ledgers_until_the_voucher_is_accepted()
    {
        var vm = GstCompany("Preview Purity Co");
        var c = vm.Company!;
        var fees = RcmExpense(c, "Legal Fees", "Legal", 1800);
        var advocate = Supplier(c, "Advocate (Gujarat)", GstinGujarat, "24");
        var ledgersBefore = c.Ledgers.Count;

        var e = OpenInward(vm, fees, advocate, 10000m);

        // The panel is live and previewing a FIRING reverse charge…
        Assert.True(e.ShowRcmPanel);
        Assert.Equal("1,800.00", e.RcmTaxText);
        // …yet nothing was created: the preview goes through the pure Resolve + ComputeLineTax, never the builder.
        Assert.Null(new GstService(c).FindRcmOutputLedger(GstTaxHead.Integrated));
        Assert.Equal(ledgersBefore, c.Ledgers.Count);

        // Only Accept may create the RCM ledger.
        Assert.True(e.Accept());
        Assert.NotNull(new GstService(c).FindRcmOutputLedger(GstTaxHead.Integrated));
    }

    // ================================================================ import of services (§5(3) — always IGST, 4A(2))

    /// <summary>Import of services is reverse charge by law: always IGST regardless of the supplier's state, bucketed to
    /// GSTR-3B 4A(2), and the category is named from the statute (the engine matches no notified category for it).</summary>
    [Fact]
    public void Import_of_services_routes_igst_and_the_4A2_scheme()
    {
        var vm = GstCompany("Import Services Co");
        var c = vm.Company!;
        // A plain reverse-charge-flagged services ledger with NO category link — import of services needs none.
        var consulting = AddLedger(c, "Overseas Consulting", "Indirect Expenses", true);
        consulting.SalesPurchaseGst = new StockItemGstDetails
        {
            Taxability = GstTaxability.Taxable,
            RateBasisPoints = 1800,
            SupplyType = GstSupplyType.Services,
            ReverseChargeApplicable = true,
        };
        var foreignVendor = Supplier(c, "Foreign Vendor", gstin: null, state: null, unregistered: true);

        var e = OpenInward(vm, consulting, foreignVendor, 50000m);

        // Route it as an import of services.
        e.SelectedRcmSupplyKind = e.RcmSupplyKinds.First(k => k.Kind == RcmService.SupplyKind.ImportOfServices);

        Assert.Equal("Yes — reverse charge applies", e.RcmAppliesText);
        Assert.Equal("Inter-State (IGST)", e.RcmPosText);   // §5(3) forces IGST
        Assert.Equal("9,000.00", e.RcmTaxText);
        Assert.Contains("4A(2)", e.RcmSummary);             // the import-of-services ITC bucket
        Assert.Contains("§5(3)", e.RcmCategoryText);

        Assert.True(e.Accept());

        var v = LastVoucher(c);
        var gst = new GstService(c);
        Assert.True(VoucherValidator.IsBalanced(v));
        Assert.Equal(9000.00m, AmountOn(v, gst.FindRcmOutputLedger(GstTaxHead.Integrated)!.Id, DrCr.Credit).Amount);
        Assert.Equal(9000.00m, AmountOn(v, gst.FindTaxLedger(GstTaxHead.Integrated, GstTaxDirection.Input)!.Id, DrCr.Debit).Amount);

        // The ITC leg carries the ImportOfServices scheme so GSTR-3B buckets it to 4A(2), not 4A(3).
        var itcLine = v.Lines.Single(l =>
            l.LedgerId == gst.FindTaxLedger(GstTaxHead.Integrated, GstTaxDirection.Input)!.Id && l.Side == DrCr.Debit);
        Assert.Equal(RcmItcScheme.ImportOfServices, itcLine.Gst!.RcmScheme);
    }

    // ================================================================ Rule 47A self-invoice / Rule 52 payment voucher

    /// <summary>Rule 47A: an <b>unregistered</b> supplier's RCM inward supply gets a self-invoice with a consecutive
    /// series number, linked to the posted voucher; a Rule-52 payment voucher gets its own series.</summary>
    [Fact]
    public void Self_invoice_and_payment_voucher_are_generated_for_an_unregistered_supplier()
    {
        var vm = GstCompany("Self-Invoice Co");
        var c = vm.Company!;
        var fees = RcmExpense(c, "Legal Fees", "Legal", 1800);
        var advocate = Supplier(c, "Unregistered Advocate", gstin: null, state: "24", unregistered: true);

        var e = OpenInward(vm, fees, advocate, 10000m);
        e.GenerateRcmSelfInvoice = true;
        e.GenerateRcmPaymentVoucher = true;

        Assert.True(e.Accept());

        var v = LastVoucher(c);
        var selfInvoice = c.RcmDocuments.Single(d => d.Kind == RcmDocumentKind.SelfInvoice);
        var paymentVoucher = c.RcmDocuments.Single(d => d.Kind == RcmDocumentKind.PaymentVoucher);

        Assert.Equal(1, selfInvoice.SeriesNumber);
        Assert.Equal(v.Id, selfInvoice.SourceVoucherId);   // linked to the posted RCM voucher
        Assert.Equal(advocate.Id, selfInvoice.SupplierLedgerId);
        Assert.Equal(D1, selfInvoice.DocDate);
        Assert.Equal(1, paymentVoucher.SeriesNumber);      // its own independent series
        Assert.Equal(v.Id, paymentVoucher.SourceVoucherId);
        Assert.Contains("Self-invoice No. 1", e.Message);
        Assert.Contains("Payment voucher No. 1", e.Message);
    }

    /// <summary>Rule 47A: a <b>registered</b> §9(3) supplier issues its own tax invoice, so the engine declines to raise
    /// a self-invoice. The screen must SAY so rather than silently doing nothing.</summary>
    [Fact]
    public void Self_invoice_is_declined_for_a_registered_supplier_with_an_explanation()
    {
        var vm = GstCompany("Registered Supplier Co");
        var c = vm.Company!;
        var fees = RcmExpense(c, "Legal Fees", "Legal", 1800);
        var advocate = Supplier(c, "Registered Advocate", GstinGujarat, "24"); // Regular + GSTIN ⇒ not B2C

        var e = OpenInward(vm, fees, advocate, 10000m);
        e.GenerateRcmSelfInvoice = true;

        Assert.True(e.Accept());

        Assert.DoesNotContain(c.RcmDocuments, d => d.Kind == RcmDocumentKind.SelfInvoice);
        Assert.Contains("Self-invoice not raised", e.Message);
        Assert.Contains("issues its own tax invoice", e.Message);
    }

    /// <summary>No document is generated when the operator did not ask for one — and never on a voucher that carries no
    /// reverse charge at all.</summary>
    [Fact]
    public void No_rcm_documents_are_generated_unless_requested()
    {
        var vm = GstCompany("No-Docs Co");
        var c = vm.Company!;
        var fees = RcmExpense(c, "Legal Fees", "Legal", 1800);
        var advocate = Supplier(c, "Advocate (Gujarat)", GstinGujarat, "24");

        var e = OpenInward(vm, fees, advocate, 10000m);
        Assert.True(e.Accept());

        Assert.Empty(c.RcmDocuments);
    }

    /// <summary>The RCM panel is reachable from a plain-grid <b>Journal</b> too (an RCM inward supply is commonly booked
    /// through a Journal), and posts the same dual leg.</summary>
    [Fact]
    public void Rcm_panel_is_available_on_a_journal_voucher()
    {
        var vm = GstCompany("RCM Journal Co");
        var c = vm.Company!;
        var fees = RcmExpense(c, "Legal Fees", "Legal", 1800);
        var advocate = Supplier(c, "Advocate (Gujarat)", GstinGujarat, "24");

        var e = OpenInward(vm, fees, advocate, 10000m, VoucherBaseType.Journal);

        Assert.True(e.ShowRcmPanel);
        Assert.True(e.Accept());

        var v = LastVoucher(c);
        Assert.Equal(4, v.Lines.Count);
        Assert.True(VoucherValidator.IsBalanced(v));
    }

    // ================================================================ every RCM leg is self-accounted (UI-3 fix 2)

    /// <summary>
    /// <b>All</b> reverse-charge legs on the grid are self-accounted — not just the first. One supplier invoice commonly
    /// carries two notified heads (here ₹10,000 legal @18% + ₹20,000 GTA @5%); taking only the first silently
    /// under-collects the cash-only §49(4) liability with no warning and no refusal, while Accept reports success.
    /// </summary>
    [Fact]
    public void Every_rcm_expense_leg_is_self_accounted_not_only_the_first()
    {
        var vm = GstCompany("Multi-Leg RCM Co");
        var c = vm.Company!;
        var legal = RcmExpense(c, "Legal Fees", "Legal", 1800);
        var freight = RcmExpense(c, "Freight Inward (GTA)", "GTA", 500);
        var supplier = Supplier(c, "Consolidated Supplier (Gujarat)", GstinGujarat, "24");

        vm.OpenVoucher(VoucherBaseType.Purchase);
        var e = vm.VoucherEntry!;
        e.Date = D1;
        e.Lines[0].SelectedLedger = legal;
        e.Lines[0].Side = DrCr.Debit;
        e.Lines[0].AmountText = "10000";
        e.AddLine(DrCr.Debit);
        e.Lines[2].SelectedLedger = freight;
        e.Lines[2].Side = DrCr.Debit;
        e.Lines[2].AmountText = "20000";
        e.Lines[1].SelectedLedger = supplier;
        e.Lines[1].Side = DrCr.Credit;
        e.Lines[1].AmountText = "30000";
        e.Recalculate();

        // The preview shows the AGGREGATE liability: 18% of 10,000 + 5% of 20,000 = 1,800 + 1,000 = 2,800.
        Assert.True(e.ShowRcmPanel);
        Assert.Equal("Yes — reverse charge applies", e.RcmAppliesText);
        Assert.Equal("2,800.00", e.RcmTaxText);

        Assert.True(e.Accept());

        var v = LastVoucher(c);
        var gst = new GstService(c);
        var rcmOut = gst.FindRcmOutputLedger(GstTaxHead.Integrated)!.Id;
        var inputIgst = gst.FindTaxLedger(GstTaxHead.Integrated, GstTaxDirection.Input)!.Id;

        // BOTH dual legs post, paisa-exact — the previewed figure is the posted figure.
        Assert.Equal(2800.00m, AmountOn(v, rcmOut, DrCr.Credit).Amount);
        Assert.Equal(2800.00m, AmountOn(v, inputIgst, DrCr.Debit).Amount);
        Assert.True(VoucherValidator.IsBalanced(v));

        // Each notified head keeps its own tagged line (2 ordinary legs + 2 pairs).
        Assert.Equal(3 + 4, v.Lines.Count);
        var rcmCredits = v.Lines.Where(l => l.LedgerId == rcmOut && l.Side == DrCr.Credit).ToList();
        Assert.Equal(2, rcmCredits.Count);
        Assert.Contains(rcmCredits, l => l.Amount.Amount == 1800.00m);
        Assert.Contains(rcmCredits, l => l.Amount.Amount == 1000.00m);
    }

    // ================================================================ RCM is never silently self-accounted (UI-3 fix 3)

    /// <summary>
    /// <b>An ordinary accrual Journal must never self-account reverse charge.</b> The supplier leg was detected as "any
    /// complete credit line", so a plain Dr Expense / Cr Outstanding-Expenses accrual — which has no supplier on it at
    /// all — silently posted a ₹1,800 cash-only §49(4) liability against an accrual head. That is a false posting on an
    /// ORDINARY voucher: no supplier, no self-accounting.
    /// </summary>
    [Fact]
    public void A_plain_accrual_journal_with_no_supplier_never_self_accounts_reverse_charge()
    {
        var vm = GstCompany("Accrual Journal Co");
        var c = vm.Company!;
        var fees = RcmExpense(c, "Legal Fees", "Legal", 1800);
        // NOT a party: an accrual head under Current Liabilities — no PartyGst, not a Sundry-Creditors ledger.
        var accrual = AddLedger(c, "Outstanding Expenses", "Current Liabilities", false);

        var e = OpenInward(vm, fees, accrual, 10000m, VoucherBaseType.Journal);

        Assert.False(e.ShowRcmPanel);   // no supplier ⇒ no RCM shape at all
        Assert.True(e.Accept());

        var v = LastVoucher(c);
        Assert.Equal(2, v.Lines.Count); // byte-identical (ER-13) — no §49(4) liability conjured
        Assert.Null(new GstService(c).FindRcmOutputLedger(GstTaxHead.Integrated));
    }

    /// <summary>
    /// The operator can <b>decline</b> reverse charge (the affordance TDS has always had) — an inward supply the screen
    /// reads as notified may be forward charge, or not a supply at all. Declining posts nothing and conjures no RCM
    /// ledger; the panel stays on screen so the decline is reversible.
    /// </summary>
    [Fact]
    public void Declining_reverse_charge_posts_no_self_accounting_pair()
    {
        var vm = GstCompany("Decline RCM Co");
        var c = vm.Company!;
        var fees = RcmExpense(c, "Legal Fees", "Legal", 1800);
        var advocate = Supplier(c, "Advocate (Gujarat)", GstinGujarat, "24");

        var e = OpenInward(vm, fees, advocate, 10000m);
        Assert.True(e.ShowRcmPanel);
        Assert.Equal("1,800.00", e.RcmTaxText);   // firing by default — RCM is mandatory when a category matches

        // The sentinel leads the list (mirroring the TDS "Not Applicable" pattern) but is never the default.
        Assert.Same(e.RcmSupplyKinds[0], e.RcmSupplyKinds.Single(k => k.Kind is null));
        Assert.NotSame(e.RcmSupplyKinds[0], e.SelectedRcmSupplyKind);

        e.SelectedRcmSupplyKind = e.RcmSupplyKinds.Single(k => k.Kind is null);

        Assert.True(e.ShowRcmPanel);              // the panel stays — the decline must be reversible
        Assert.Equal("0.00", e.RcmTaxText);
        Assert.Equal("No — declined by the operator", e.RcmAppliesText);

        Assert.True(e.Accept());
        Assert.Equal(2, LastVoucher(c).Lines.Count);
        Assert.Null(new GstService(c).FindRcmOutputLedger(GstTaxHead.Integrated));
    }

    // ================================================================ the preview never understates the cash (UI-3 fix 5)

    /// <summary>
    /// The RCM preview must include the <b>Compensation Cess</b> that actually posts. The panel previewed through
    /// <c>ComputeLineTax</c> (CGST/SGST/IGST only) while the builder also resolves and posts a cess pair — so the
    /// previewed cash liability understated what posts. A preview that lies about the posting is worse than no preview.
    /// </summary>
    [Fact]
    public void The_rcm_preview_includes_the_compensation_cess_that_posts()
    {
        var vm = GstCompany("RCM Cess Co");
        var c = vm.Company!;
        var sponsorship = RcmExpense(c, "Sponsorship", "Sponsorship", 1800);
        sponsorship.SalesPurchaseGst!.CessApplicable = true;
        sponsorship.SalesPurchaseGst.CessValuationMode = CessValuationMode.AdValorem;
        sponsorship.SalesPurchaseGst.CessRateBasisPoints = 1200;   // 12% ad-valorem cess
        var sponsor = Supplier(c, "Sponsor Co (Gujarat)", GstinGujarat, "24");

        var e = OpenInward(vm, sponsorship, sponsor, 10000m);

        Assert.True(e.ShowRcmPanel);
        Assert.True(e.ShowRcmCess);
        Assert.Equal("1,200.00", e.RcmCessText);
        // The headline is the TOTAL cash liability: 1,800 IGST + 1,200 cess.
        Assert.Equal("3,000.00", e.RcmTaxText);
        Assert.Contains("Cess", e.RcmSummary);

        Assert.True(e.Accept());

        var v = LastVoucher(c);
        var gst = new GstService(c);
        var posted = AmountOn(v, gst.FindRcmOutputLedger(GstTaxHead.Integrated)!.Id, DrCr.Credit)
                     + AmountOn(v, gst.FindRcmOutputLedger(GstTaxHead.Cess)!.Id, DrCr.Credit);

        Assert.Equal(3000.00m, posted.Amount);   // preview == posted, paisa-exact
        Assert.True(VoucherValidator.IsBalanced(v));
    }

    /// <summary>A non-cess RCM supply is untouched by the cess resolution: no cess line, no cess ledger (ER-13).</summary>
    [Fact]
    public void A_non_cess_rcm_supply_shows_no_cess_line()
    {
        var vm = GstCompany("No Cess RCM Co");
        var c = vm.Company!;
        var fees = RcmExpense(c, "Legal Fees", "Legal", 1800);
        var advocate = Supplier(c, "Advocate (Gujarat)", GstinGujarat, "24");

        var e = OpenInward(vm, fees, advocate, 10000m);

        Assert.False(e.ShowRcmCess);
        Assert.Equal("1,800.00", e.RcmTaxText);
        Assert.True(e.Accept());
        Assert.Null(new GstService(c).FindRcmOutputLedger(GstTaxHead.Cess));
    }
}
