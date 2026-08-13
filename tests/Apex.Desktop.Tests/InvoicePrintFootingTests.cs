using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Io;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.Tests;

/// <summary>
/// The second adversarial-review pass on the printed tax invoice (F9–F11). All three are <b>print-side</b> defects:
/// the posted voucher is right and stays untouched; what was wrong is the document the customer receives.
///
/// <list type="bullet">
/// <item><b>F10 (HIGH, money, PRE-EXISTING)</b> — the printed ITEM invoice invented a nearest-rupee round-off that no
/// voucher ever posts, so the printed Grand Total differed from the recorded debt by up to ±0.50 on every invoice
/// whose taxable+tax is not a whole rupee.</item>
/// <item><b>F9 (MEDIUM)</b> — a service invoice posted while GST was OFF was retro-relabelled a Rule-46 tax invoice the
/// moment GST was enabled and its income ledger classified: a TAXABLE SAC supply declared at NIL GST.</item>
/// <item><b>F11 (LOW-MED)</b> — the footing guard constrained the TOTALS but not the printed rate-breakup ROW, so a
/// crafted <c>GstLineTax.TaxableValue</c> printed a rate row contradicting the totals band above it.</item>
/// </list>
///
/// <para>Every test drives a REAL posted voucher (through the real entry screen wherever the shape is reachable that
/// way) through the REAL projector, and asserts the printed money against the POSTED money.</para>
/// </summary>
public sealed class InvoicePrintFootingTests : IDisposable
{
    private const string GstinMaharashtra = "27AAPFU0939F1ZV";
    private static readonly DateOnly FyStart = new(2024, 4, 1);

    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public InvoicePrintFootingTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexInvoiceFootingTests_" + Guid.NewGuid().ToString("N"));
        _storage = new CompanyStorage(_tempDir);
    }

    // ================================================================ F10: the printed total IS the posted debt

    /// <summary>
    /// <b>F10 (HIGH, money, PRE-EXISTING — identical at HEAD f017a72).</b> The printed item tax invoice did not state
    /// the debt the voucher recorded. <c>ProjectInvoice</c> ran the engine with <c>applyInvoiceRoundOff: true</c> while
    /// the ACCEPT path uses the 3-arg overload (<c>false</c>) — so PRINT invented a nearest-rupee round-off that NO
    /// voucher ever posts (<c>GstService.InvoiceTax.RoundOffLine</c> is produced and consumed nowhere in <c>src/</c>).
    ///
    /// <para>Measured on exactly this fixture — 7 Widgets @ ₹133.33 @18% intra: taxable 933.31, CGST 84.00, SGST 84.00,
    /// <b>posted party leg 1101.31</b> against a printed <c>Round Off −0.31 / Grand Total 1101.00</c>. Up to ±0.50 on
    /// every invoice whose taxable+tax is not a whole rupee: the printed demand differs from the recorded debt, and
    /// paying the printed figure leaves a permanent paisa residue in the debtor ledger.</para>
    ///
    /// <para><b>Bite:</b> restore <c>applyInvoiceRoundOff: true</c> on the <c>gst.ComputeInvoiceTax</c> call in
    /// <c>ProjectInvoice</c> and this prints 1101.00 against a posted 1101.31.</para>
    ///
    /// <para><b>W0-10 — that call is gone, so the bite is no longer expressible and this test's meaning changed.</b>
    /// The item pass reads its heads, rows, cess and routing off the POSTED legs; a print-time round-off cannot be
    /// invented because there is no print-time computation left to invent one. What this now locks is the undrifted
    /// baseline of the invariant — an ordinary odd-paisa invoice, no master edited, printed Grand Total == posted party
    /// leg — with the drifted cases driven by the sibling <c>ItemInvoicePostedTaxTests</c>.</para>
    /// </summary>
    [Fact]
    public void ItemInvoice_oddPaisa_printedGrandTotal_equalsPostedPartyLeg()
    {
        var vm = NewItemInvoiceKit("Foot ItemOddPaisa Co", out var widgetId, out var godownId, out var customerId);
        var c = PostItemInvoice(vm, widgetId, godownId, customerId, qty: "7", rate: "133.33");
        var v = PostedSale(c);

        // What the books actually recorded — the debt the document must state.
        Assert.Equal(84m, PostedHead(v, GstTaxHead.Central));
        Assert.Equal(84m, PostedHead(v, GstTaxHead.State));
        Assert.Equal(1101.31m, PartyLegAmount(c, v));
        Assert.Null(PostedRoundOffLine(c, v));            // no round-off leg was posted — none exists to print

        var data = VoucherPrintProjector.ProjectInvoice(c, v);
        Assert.Equal(933.31m, data.TotalTaxable.Amount);
        Assert.Equal(84m, data.TotalCgst.Amount);
        Assert.Equal(84m, data.TotalSgst.Amount);
        Assert.Equal(0m, data.RoundOff.Amount);           // …so the document invents none
        Assert.Equal(1101.31m, data.GrandTotal.Amount);
        Assert.Equal(PartyLegAmount(c, v), data.GrandTotal.Amount);

        // …and the real renderer still produces a real invoice PDF through the real routing.
        Assert.NotEmpty(new VoucherDetailViewModel(c, v).BuildPrintPreview().PdfBytes);
    }

    /// <summary>
    /// The SERVICE half of F10. The accounting-invoice path already prints no round-off (it computes its tax with
    /// <c>applyInvoiceRoundOff: false</c>, matching accept), so this shape FOOTS today — it is pinned here so the item
    /// fix can never be "harmonised" the wrong way, by teaching the service path to round instead.
    /// <para><b>Bite:</b> give <c>ProjectServiceInvoice</c> a nearest-rupee <c>RoundOff</c> and this prints 1101.00
    /// against a posted 1101.31 (and the footing invariant then demotes the invoice to a plain voucher).</para>
    /// </summary>
    [Fact]
    public void ServiceInvoice_oddPaisa_printedGrandTotal_equalsPostedPartyLeg()
    {
        var k = NewServiceKit("Foot SvcOddPaisa Co");
        var entry = OpenAccountingSale(k);
        SelectParty(entry, k.LocalCustomerId);
        FillLine(entry, k.ConsultancyId, "933.31");       // 18% ⇒ CGST 84.00 + SGST 84.00 ⇒ customer owes 1101.31
        Assert.True(entry.Accept());

        var c = k.Vm.Company!;
        var v = PostedSale(c);
        Assert.Equal(84m, PostedHead(v, GstTaxHead.Central));
        Assert.Equal(84m, PostedHead(v, GstTaxHead.State));
        Assert.Equal(1101.31m, PartyLegAmount(c, v));

        Assert.True(VoucherPrintProjector.IsTaxInvoice(c, v));
        var data = VoucherPrintProjector.ProjectInvoice(c, v);
        Assert.Equal(933.31m, data.TotalTaxable.Amount);
        Assert.Equal(0m, data.RoundOff.Amount);
        Assert.Equal(1101.31m, data.GrandTotal.Amount);
        Assert.Equal(PartyLegAmount(c, v), data.GrandTotal.Amount);
    }

    /// <summary>
    /// The no-regression half of F10: a WHOLE-RUPEE item invoice — the common case, and the only case the six
    /// pre-existing foot-to-the-party-leg assertions ever exercised — prints exactly as it did before. Its invented
    /// round-off was always 0.00, so removing the invention cannot move a single figure here.
    /// </summary>
    [Fact]
    public void ItemInvoice_wholeRupee_printsExactlyAsBefore()
    {
        var vm = NewItemInvoiceKit("Foot ItemWholeRupee Co", out var widgetId, out var godownId, out var customerId);
        var c = PostItemInvoice(vm, widgetId, godownId, customerId, qty: "10", rate: "100.00");
        var v = PostedSale(c);
        Assert.Equal(1180m, PartyLegAmount(c, v));

        var data = VoucherPrintProjector.ProjectInvoice(c, v);
        var row = Assert.Single(data.Items);
        Assert.Equal("Widget", row.Description);
        Assert.Equal("10 Nos", row.QuantityText);
        Assert.Equal("100.00", row.RateText);
        Assert.Equal(1000m, data.TotalTaxable.Amount);
        Assert.Equal(90m, data.TotalCgst.Amount);
        Assert.Equal(90m, data.TotalSgst.Amount);
        Assert.Equal(0m, data.RoundOff.Amount);
        Assert.Equal(1180m, data.GrandTotal.Amount);
        Assert.Equal(PartyLegAmount(c, v), data.GrandTotal.Amount);
    }

    /// <summary>
    /// The other side of F10: "print no round-off" is not the rule — "print the round-off that was POSTED" is. A
    /// voucher whose GL genuinely carries a Round-Off leg (only reachable today through a crafted/imported document,
    /// since no path in <c>src/</c> posts one — but the document must still state its own debt) prints that leg, so the
    /// Grand Total again equals the party leg.
    /// <para><b>Bite:</b> hardcode <c>RoundOff = Money.Zero</c> in <c>ProjectInvoice</c> (instead of reading
    /// <c>PostedRoundOff</c>) and this prints 1101.31 against a posted party leg of 1101.00.</para>
    /// </summary>
    [Fact]
    public void ItemInvoice_withAPostedRoundOffLeg_printsThatRoundOff_andStillFoots()
    {
        var vm = NewItemInvoiceKit("Foot ItemPostedRoundOff Co", out var widgetId, out var godownId, out var customerId);
        var c = vm.Company!;
        var gst = new GstService(c);
        var salesType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales && t.IsActive);
        var salesLedger = c.FindLedgerByName("Sales")!;
        var roundOff = c.FindLedgerByName(GstService.RoundOffLedgerName)!;

        // The SAME 7 × ₹133.33 @18% invoice, but settled at the rupee: party 1101.00, with the 0.31 residual parked on
        // the Round-Off ledger (Dr on a sale ⇒ the party total was shaved).
        var v = new Voucher(Guid.NewGuid(), salesType.Id, FyStart.AddDays(9), new[]
        {
            new EntryLine(customerId, Money.FromRupees(1101m), DrCr.Debit),
            new EntryLine(salesLedger.Id, Money.FromRupees(933.31m), DrCr.Credit),
            new EntryLine(gst.FindTaxLedger(GstTaxHead.Central, GstTaxDirection.Output)!.Id, Money.FromRupees(84m),
                DrCr.Credit, gst: new GstLineTax(GstTaxHead.Central, 900, Money.FromRupees(933.31m))),
            new EntryLine(gst.FindTaxLedger(GstTaxHead.State, GstTaxDirection.Output)!.Id, Money.FromRupees(84m),
                DrCr.Credit, gst: new GstLineTax(GstTaxHead.State, 900, Money.FromRupees(933.31m))),
            new EntryLine(roundOff.Id, Money.FromRupees(0.31m), DrCr.Debit),
        }, partyId: customerId, inventoryLines: new[]
        {
            new VoucherInventoryLine(widgetId, godownId, 7m, Money.FromRupees(133.33m)),
        });
        new LedgerService(c).Post(v);

        Assert.Equal(1101m, PartyLegAmount(c, v));
        Assert.NotNull(PostedRoundOffLine(c, v));

        var data = VoucherPrintProjector.ProjectInvoice(c, v);
        Assert.Equal(933.31m, data.TotalTaxable.Amount);
        Assert.Equal(-0.31m, data.RoundOff.Amount);       // the POSTED residual, signed the way the party leg moved
        Assert.Equal(1101m, data.GrandTotal.Amount);
        Assert.Equal(PartyLegAmount(c, v), data.GrandTotal.Amount);
    }

    // ================================================================ F9: a taxable supply billed at NIL GST

    /// <summary>
    /// <b>F9 (MEDIUM, reachable through the UI — no import, no tampering).</b> Post a service invoice while GST is OFF
    /// (the accounting-invoice screen is available on any Sales voucher, GST or not): the voucher carries the persisted
    /// <c>IsAccountingInvoice</c> flag, two legs and a party leg of 5,000, and prints as the plain voucher it is.
    ///
    /// <para>Then register for GST and classify the income ledger — both ordinary, encouraged actions. The SAME
    /// already-issued voucher now printed <c>label="Tax Invoice" sac=998311 taxable=5000 tax=0 rows=0 grand=5000</c>:
    /// an issued document retro-relabelled a Rule-46 tax invoice declaring a TAXABLE SAC supply billed at NIL GST,
    /// with an EMPTY rate breakup. The footing guard cannot catch it — a document that charges no tax foots
    /// trivially.</para>
    ///
    /// <para>A service leg whose ledger is taxable at a NON-ZERO rate, on a voucher carrying no forward tax leg at all,
    /// is internally inconsistent; it is not projected as a tax invoice.</para>
    ///
    /// <para><b>Bite:</b> drop the taxable-but-untaxed conjunct from <c>IsServiceAccountingInvoice</c> and this prints
    /// a "Tax Invoice" with a 998311 SAC row and no tax.</para>
    /// </summary>
    [Fact]
    public void ServiceInvoice_postedWithGstOff_thenLedgerClassifiedTaxable_printsAsAPlainVoucher()
    {
        // --- a GST-less company; the accounting-invoice screen still works ---
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = "Foot GstOffThenOn Co";
        vm.CreateCompany();
        var c = vm.Company!;
        c.FinancialYearStart = FyStart;
        c.BooksBeginFrom = FyStart;
        var consultancy = AddLedger(c, "Consultancy Income", "Sales Accounts");
        var customer = AddLedger(c, "Local Customer", "Sundry Debtors");
        _storage.Save(c);
        Assert.False(c.GstEnabled);

        vm.OpenVoucher(VoucherBaseType.Sales);
        var entry = vm.VoucherEntry!;
        entry.ChangeMode();  // As Voucher -> Item Invoice
        entry.ChangeMode();  // Item Invoice -> Accounting Invoice
        Assert.True(entry.IsAccountingInvoice);
        Assert.False(entry.IsAccountingGstInvoice);       // GST is off — no tax will be computed or posted
        SelectParty(entry, customer.Id);
        FillLine(entry, consultancy.Id, "5000");
        Assert.True(entry.Accept());

        var v = PostedSale(c);
        Assert.True(v.IsAccountingInvoice);
        Assert.Equal(2, v.Lines.Count);                   // party + income only
        Assert.Equal(5000m, PartyLegAmount(c, v));
        Assert.All(v.Lines, l => Assert.Null(l.Gst));

        // --- the company registers for GST and classifies the income ledger: both ordinary, encouraged actions ---
        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = "27", Gstin = GstinMaharashtra, RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart, Periodicity = GstReturnPeriodicity.Monthly,
        });
        consultancy.SalesPurchaseGst = new StockItemGstDetails
        {
            HsnSac = "998311", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800,
            SupplyType = GstSupplyType.Services,
        };
        customer.PartyGst = new PartyGstDetails
        { RegistrationType = GstRegistrationType.Regular, Gstin = GstinMaharashtra, StateCode = "27" };
        _storage.Save(c);

        // THE GUARANTEE: the already-issued document is NOT retro-relabelled a tax invoice.
        Assert.False(VoucherPrintProjector.IsServiceAccountingInvoice(c, v));
        Assert.False(VoucherPrintProjector.IsTaxInvoice(c, v));
        var detail = new VoucherDetailViewModel(c, v);
        Assert.Equal(string.Empty, detail.DocumentLabel);
        Assert.Equal(PrintPreviewViewModel.PrintKind.Voucher, detail.BuildPrintPreview().Kind);

        // …and it prints as the plain Dr/Cr voucher it was posted as, both legs verbatim.
        var plain = VoucherPrintProjector.ProjectVoucher(c, v);
        Assert.Equal(2, plain.Lines.Count);
        Assert.Single(plain.Lines, l => l.IsDebit && l.Amount.Amount == 5000m);
        Assert.Single(plain.Lines, l => !l.IsDebit && l.Amount.Amount == 5000m);
    }

    /// <summary>
    /// The F1 behaviour F9 must NOT regress: a genuinely ZERO-RATED (0%, LUT/export) and a genuinely EXEMPT service
    /// invoice each post no forward tax leg either — and each IS a Rule-46 tax invoice. The discriminator is what the
    /// ledger DECLARES (<c>Gstr1.IsNonTaxableServiceLedger</c> / a zero rate), not "no tax was posted".
    /// <para><b>Bite:</b> widen the F9 conjunct to "no forward tax leg ⇒ not a tax invoice" and both of these are
    /// demoted to plain vouchers.</para>
    /// </summary>
    [Fact]
    public void GenuineZeroRatedAndExemptServiceInvoices_stillPrintAsTaxInvoices()
    {
        foreach (var (name, pick, amount, sac) in new (string, Func<Kit, Guid>, string, string)[]
        {
            ("zero",   k => k.ZeroRatedId,     "5000", "998313"),
            ("exempt", k => k.ExemptServiceId, "7500", "999999"),
        })
        {
            var k = NewServiceKit("Foot NoTaxOk " + name + " Co");
            var entry = OpenAccountingSale(k);
            SelectParty(entry, k.LocalCustomerId);
            FillLine(entry, pick(k), amount);
            Assert.True(entry.Accept());

            var c = k.Vm.Company!;
            var v = PostedSale(c);
            Assert.All(v.Lines, l => Assert.Null(l.Gst));            // no forward tax leg …
            Assert.True(VoucherPrintProjector.IsTaxInvoice(c, v));   // … and still a tax invoice

            var data = VoucherPrintProjector.ProjectInvoice(c, v);
            Assert.Equal(sac, Assert.Single(data.Items).HsnSac);
            Assert.Empty(data.TaxRows);
            Assert.Equal(decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture),
                data.GrandTotal.Amount);
            Assert.Equal(PartyLegAmount(c, v), data.GrandTotal.Amount);
        }
    }

    // ================================================================ F11: the rate ROW must reconcile too

    /// <summary>
    /// <b>F11 (LOW-MED, crafted import).</b> <c>ReadPostedRateGroups</c> took the rate label and the taxable value
    /// VERBATIM off the <c>GstLineTax</c> metadata, and the F2 footing guard constrains only the TOTALS — so a voucher
    /// whose tax legs are stamped <c>GstLineTax(head, 250, TaxableValue = 10,00,000)</c> FOOTS (5,000 + 900 = 5,900)
    /// yet printed <c>rows = 5% / taxable = 10,00,000 / cgst = 450</c> under a totals band saying ₹5,000: a rate row
    /// that contradicts the document it sits on.
    /// <para>The breakup is now validated against the projection — the rate rows' taxable values cannot exceed the
    /// invoice's own taxable total.</para>
    /// <para><b>Bite:</b> drop the breakup-reconciliation conjunct from <c>IsServiceAccountingInvoice</c> and this
    /// prints the contradictory 5% / 10,00,000 row again.</para>
    /// </summary>
    [Fact]
    public void CraftedRateGroupTaxable_exceedingTheInvoiceTotal_printsAsAPlainVoucher()
    {
        var k = NewServiceKit("Foot CraftedBreakup Co");
        var c = k.Vm.Company!;
        var gst = new GstService(c);
        var salesType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales && t.IsActive);

        var v = new Voucher(Guid.NewGuid(), salesType.Id, FyStart.AddDays(11), new[]
        {
            new EntryLine(k.LocalCustomerId, Money.FromRupees(5900m), DrCr.Debit),
            new EntryLine(k.ConsultancyId, Money.FromRupees(5000m), DrCr.Credit),
            // The crafted metadata: a 2.5% head (⇒ a "5%" row) declaring a ₹10,00,000 taxable base on a ₹5,000 invoice.
            new EntryLine(gst.FindTaxLedger(GstTaxHead.Central, GstTaxDirection.Output)!.Id, Money.FromRupees(450m),
                DrCr.Credit, gst: new GstLineTax(GstTaxHead.Central, 250, Money.FromRupees(1000000m))),
            new EntryLine(gst.FindTaxLedger(GstTaxHead.State, GstTaxDirection.Output)!.Id, Money.FromRupees(450m),
                DrCr.Credit, gst: new GstLineTax(GstTaxHead.State, 250, Money.FromRupees(1000000m))),
        }, partyId: k.LocalCustomerId, isAccountingInvoice: true, narration: "crafted breakup");
        new LedgerService(c).Post(v);

        // It FOOTS — 5,000 + 900 == the posted 5,900 — which is exactly why the F2 totals guard cannot catch it.
        Assert.Equal(5900m, PartyLegAmount(c, v));

        Assert.False(VoucherPrintProjector.IsServiceAccountingInvoice(c, v));
        Assert.False(VoucherPrintProjector.IsTaxInvoice(c, v));
        Assert.Equal(PrintPreviewViewModel.PrintKind.Voucher,
            new VoucherDetailViewModel(c, v).BuildPrintPreview().Kind);
        Assert.Equal(4, VoucherPrintProjector.ProjectVoucher(c, v).Lines.Count);
    }

    /// <summary>
    /// The other side of F11: the breakup check must not shut the feature off. A genuine MULTI-RATE service invoice
    /// (whose rate rows sum to exactly the invoice taxable) and a genuine PARTLY-EXEMPT one (whose rate rows sum to
    /// LESS than it, because the exempt leg carries value with no rate row) both still print as tax invoices.
    /// <para><b>Bite:</b> tighten the reconciliation to equality (<c>==</c> instead of <c>&lt;=</c>) and the
    /// partly-exempt invoice is demoted to a plain voucher.</para>
    /// </summary>
    [Fact]
    public void GenuineMultiRateAndPartlyExemptServiceInvoices_stillPrintAsTaxInvoices()
    {
        // --- multi-rate: 10,000 @18% + 4,000 @5% ⇒ rows 10,000 + 4,000 == taxable 14,000 ---
        var k1 = NewServiceKit("Foot MultiRate Co");
        var e1 = OpenAccountingSale(k1);
        SelectParty(e1, k1.LocalCustomerId);
        FillLine(e1, k1.ConsultancyId, "10000", index: 0);
        FillLine(e1, k1.FreightIncomeId, "4000", index: 1);
        Assert.True(e1.Accept());
        var c1 = k1.Vm.Company!;
        var v1 = PostedSale(c1);
        Assert.True(VoucherPrintProjector.IsTaxInvoice(c1, v1));
        var d1 = VoucherPrintProjector.ProjectInvoice(c1, v1);
        Assert.Equal(2, d1.TaxRows.Count);
        Assert.Equal(14000m, d1.TaxRows.Sum(r => r.TaxableValue.Amount));
        Assert.Equal(16000m, d1.GrandTotal.Amount);
        Assert.Equal(PartyLegAmount(c1, v1), d1.GrandTotal.Amount);

        // --- partly exempt: 10,000 @18% + 5,000 exempt ⇒ rows 10,000 < taxable 15,000 ---
        var k2 = NewServiceKit("Foot PartExempt Co");
        var e2 = OpenAccountingSale(k2);
        SelectParty(e2, k2.LocalCustomerId);
        FillLine(e2, k2.ConsultancyId, "10000", index: 0);
        FillLine(e2, k2.ExemptServiceId, "5000", index: 1);
        Assert.True(e2.Accept());
        var c2 = k2.Vm.Company!;
        var v2 = PostedSale(c2);
        Assert.True(VoucherPrintProjector.IsTaxInvoice(c2, v2));
        var d2 = VoucherPrintProjector.ProjectInvoice(c2, v2);
        Assert.Equal(10000m, Assert.Single(d2.TaxRows).TaxableValue.Amount);
        Assert.Equal(15000m, d2.TotalTaxable.Amount);
        Assert.Equal(16800m, d2.GrandTotal.Amount);
        Assert.Equal(PartyLegAmount(c2, v2), d2.GrandTotal.Amount);
    }

    // ================================================================ the F2 footing invariant keeps its own bite

    /// <summary>
    /// The F2 footing invariant needs a fixture only IT can reject, or the F9/F11 conjuncts added above would silently
    /// take over its job and its mutation would stop biting. Here a crafted voucher bills the party ₹6,000 while the
    /// projection can only see ₹5,900 (a ₹100 charge on an income ledger carrying no SAC block, so it is not a service
    /// leg): the F9 conjunct passes (forward tax IS posted), the F11 conjunct passes (the rate row states the true
    /// 5,000 base) — only the totals reconciliation catches the ₹100 the document would understate the debt by.
    /// <para><b>Bite:</b> drop the <c>ServiceInvoiceFoots</c> conjunct from <c>IsServiceAccountingInvoice</c> and this
    /// prints a 5,900 tax invoice against a posted 6,000 debt.</para>
    /// </summary>
    [Fact]
    public void CraftedUnderStatedTotal_isCaughtByTheFootingInvariantAlone()
    {
        var k = NewServiceKit("Foot Understated Co");
        var c = k.Vm.Company!;
        var gst = new GstService(c);
        var salesType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales && t.IsActive);
        var packing = AddLedger(c, "Packing Charges Recovered", "Direct Incomes"); // no SalesPurchaseGst ⇒ invisible

        var v = new Voucher(Guid.NewGuid(), salesType.Id, FyStart.AddDays(13), new[]
        {
            new EntryLine(k.LocalCustomerId, Money.FromRupees(6000m), DrCr.Debit),
            new EntryLine(k.ConsultancyId, Money.FromRupees(5000m), DrCr.Credit),
            new EntryLine(packing.Id, Money.FromRupees(100m), DrCr.Credit),
            new EntryLine(gst.FindTaxLedger(GstTaxHead.Central, GstTaxDirection.Output)!.Id, Money.FromRupees(450m),
                DrCr.Credit, gst: new GstLineTax(GstTaxHead.Central, 900, Money.FromRupees(5000m))),
            new EntryLine(gst.FindTaxLedger(GstTaxHead.State, GstTaxDirection.Output)!.Id, Money.FromRupees(450m),
                DrCr.Credit, gst: new GstLineTax(GstTaxHead.State, 900, Money.FromRupees(5000m))),
        }, partyId: k.LocalCustomerId, isAccountingInvoice: true, narration: "understated");
        new LedgerService(c).Post(v);

        Assert.Equal(6000m, PartyLegAmount(c, v));
        Assert.NotNull(PostedForwardTaxHead(v));                       // F9 has nothing to object to …
        Assert.Equal(5000m, PostedRateGroupTaxable(v));                // … and F11 nothing either
        Assert.False(VoucherPrintProjector.IsServiceAccountingInvoice(c, v));
        Assert.Equal(PrintPreviewViewModel.PrintKind.Voucher,
            new VoucherDetailViewModel(c, v).BuildPrintPreview().Kind);
    }

    // ---------------------------------------------------------------- scaffolding

    private sealed class Kit
    {
        public required MainWindowViewModel Vm { get; init; }
        public required Guid ConsultancyId { get; init; }     // Income, taxable @18% (SAC 998311)
        public required Guid FreightIncomeId { get; init; }   // Income, taxable @5%  (SAC 996511)
        public required Guid ExemptServiceId { get; init; }   // Income, EXEMPT (SAC 999999)
        public required Guid ZeroRatedId { get; init; }       // Income, taxable @0% (LUT/export)
        public required Guid LocalCustomerId { get; init; }   // in-state (27), registered
    }

    private Kit NewServiceKit(string companyName)
    {
        var vm = NewGstCompany(companyName);
        var c = vm.Company!;

        var consultancy = AddLedger(c, "Consultancy Income", "Sales Accounts");
        consultancy.SalesPurchaseGst = Sac("998311", 1800);
        var freight = AddLedger(c, "Freight Income", "Direct Incomes");
        freight.SalesPurchaseGst = Sac("996511", 500);
        var exempt = AddLedger(c, "Exempt Service", "Sales Accounts");
        exempt.SalesPurchaseGst = new StockItemGstDetails
        { HsnSac = "999999", Taxability = GstTaxability.Exempt, SupplyType = GstSupplyType.Services };
        var zeroRated = AddLedger(c, "Export Service (LUT)", "Sales Accounts");
        zeroRated.SalesPurchaseGst = Sac("998313", 0);

        var customer = AddLedger(c, "Local Customer", "Sundry Debtors");
        customer.PartyGst = new PartyGstDetails
        { RegistrationType = GstRegistrationType.Regular, Gstin = GstinMaharashtra, StateCode = "27" };
        _storage.Save(c);

        return new Kit
        {
            Vm = vm,
            ConsultancyId = consultancy.Id,
            FreightIncomeId = freight.Id,
            ExemptServiceId = exempt.Id,
            ZeroRatedId = zeroRated.Id,
            LocalCustomerId = customer.Id,
        };
    }

    private static StockItemGstDetails Sac(string sac, int bp) => new()
    {
        HsnSac = sac, Taxability = GstTaxability.Taxable, RateBasisPoints = bp,
        SupplyType = GstSupplyType.Services,
    };

    /// <summary>A GST-enabled company with a Widget (HSN 847130 @18%), a Sales ledger and a local customer.</summary>
    private MainWindowViewModel NewItemInvoiceKit(
        string companyName, out Guid widgetId, out Guid godownId, out Guid customerId)
    {
        var vm = NewGstCompany(companyName);
        var c = vm.Company!;

        var inv = new InventoryService(c);
        var grp = inv.CreateStockGroup("Goods");
        var nos = inv.CreateSimpleUnit("Nos", "Numbers", unitQuantityCode: "NOS");
        var main = c.MainLocation!.Id;
        var widget = inv.CreateStockItem("Widget", grp.Id, nos.Id);
        widget.Gst = new StockItemGstDetails
        { HsnSac = "847130", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800 };
        inv.AddOpeningBalance(widget.Id, main, 200m, Money.FromRupees(100m));

        var salesLedger = AddLedger(c, "Sales", "Sales Accounts");
        salesLedger.SalesPurchaseGst = new StockItemGstDetails
        {
            HsnSac = "847130", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800,
            SupplyType = GstSupplyType.Goods,
        };
        var customer = AddLedger(c, "Local Customer", "Sundry Debtors");
        customer.PartyGst = new PartyGstDetails
        { RegistrationType = GstRegistrationType.Regular, Gstin = GstinMaharashtra, StateCode = "27" };
        _storage.Save(c);

        widgetId = widget.Id;
        godownId = main;
        customerId = customer.Id;
        return vm;
    }

    private MainWindowViewModel NewGstCompany(string companyName)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = companyName;
        vm.CreateCompany();
        var c = vm.Company!;
        c.FinancialYearStart = FyStart;
        c.BooksBeginFrom = FyStart;
        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = "27", Gstin = GstinMaharashtra, RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart, Periodicity = GstReturnPeriodicity.Monthly,
        });
        return vm;
    }

    /// <summary>Posts an item invoice through the REAL item-invoice screen; returns the company.</summary>
    private static Company PostItemInvoice(
        MainWindowViewModel vm, Guid widgetId, Guid godownId, Guid customerId, string qty, string rate)
    {
        vm.OpenVoucher(VoucherBaseType.Sales);
        var entry = vm.VoucherEntry!;
        entry.ToggleItemInvoice();
        SelectParty(entry, customerId);
        while (entry.InventoryLines.Count == 0) entry.AddInventoryLine();
        var line = entry.InventoryLines[0];
        line.SelectedItem = entry.StockItems.Single(i => i.Id == widgetId);
        line.SelectedGodown = entry.Godowns.Single(g => g.Id == godownId);
        line.QuantityText = qty;
        line.RateText = rate;
        Assert.True(entry.Accept());
        return vm.Company!;
    }

    private static DomainLedger AddLedger(Company c, string name, string groupName)
    {
        var group = c.FindGroupByName(groupName) ?? throw new InvalidOperationException($"No group '{groupName}'.");
        var ledger = new DomainLedger(Guid.NewGuid(), name, group.Id, Money.Zero, openingIsDebit: false);
        c.AddLedger(ledger);
        return ledger;
    }

    private static VoucherEntryViewModel OpenAccountingSale(Kit k)
    {
        k.Vm.OpenVoucher(VoucherBaseType.Sales);
        var entry = k.Vm.VoucherEntry!;
        entry.ChangeMode(); // As Voucher -> Item Invoice
        entry.ChangeMode(); // Item Invoice -> Accounting Invoice
        Assert.True(entry.IsAccountingInvoice);
        Assert.True(entry.IsAccountingGstInvoice);
        return entry;
    }

    private static void SelectParty(VoucherEntryViewModel entry, Guid partyId) =>
        entry.SelectedParty = entry.Parties.Single(p => p.Ledger?.Id == partyId);

    private static void FillLine(VoucherEntryViewModel entry, Guid ledgerId, string amount, int index = 0)
    {
        while (entry.AccountingInvoiceLines.Count <= index) entry.AddAccountingInvoiceLine();
        var line = entry.AccountingInvoiceLines[index];
        line.SelectedLedger = entry.AccountingInvoiceLedgers.Single(l => l.Id == ledgerId);
        line.AmountText = amount;
    }

    private static Voucher PostedSale(Company c) =>
        c.Vouchers.Single(v => c.FindVoucherType(v.TypeId)!.BaseType == VoucherBaseType.Sales);

    private static decimal PostedHead(Voucher v, GstTaxHead head)
    {
        var total = 0m;
        foreach (var l in v.Lines)
            if (l.Gst is { } g && !g.IsReverseCharge && g.TaxHead == head)
                total += l.Amount.Amount;
        return total;
    }

    /// <summary>The first posted FORWARD GST head on the voucher, or <c>null</c> when it posted none (the F9 signal).</summary>
    private static GstTaxHead? PostedForwardTaxHead(Voucher v)
    {
        foreach (var l in v.Lines)
            if (l.Gst is { IsReverseCharge: false } g &&
                g.TaxHead is GstTaxHead.Central or GstTaxHead.State or GstTaxHead.Integrated)
                return g.TaxHead;
        return null;
    }

    /// <summary>Σ, per integrated rate group, of the taxable value the posted tax legs DECLARE (the F11 signal).</summary>
    private static decimal PostedRateGroupTaxable(Voucher v)
    {
        var byRate = new Dictionary<int, decimal>();
        foreach (var l in v.Lines)
        {
            if (l.Gst is not { IsReverseCharge: false } g || g.TaxHead == GstTaxHead.Cess) continue;
            var rate = GstReportSupport.IntegratedRateOf(g, l.Amount);
            var cur = byRate.TryGetValue(rate, out var m) ? m : 0m;
            if (g.TaxableValue.Amount > cur) byRate[rate] = g.TaxableValue.Amount;
        }
        return byRate.Values.Sum();
    }

    /// <summary>The voucher's posted Round-Off entry line, or <c>null</c> when it posted none.</summary>
    private static EntryLine? PostedRoundOffLine(Company c, Voucher v)
    {
        var id = c.FindLedgerByName(GstService.RoundOffLedgerName)?.Id;
        return id is Guid roId ? v.Lines.FirstOrDefault(l => l.LedgerId == roId) : null;
    }

    private static decimal PartyLegAmount(Company c, Voucher v) =>
        v.Lines.Single(l => l.LedgerId == v.PartyId!.Value).Amount.Amount;

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch { /* best-effort temp cleanup */ }
    }
}
