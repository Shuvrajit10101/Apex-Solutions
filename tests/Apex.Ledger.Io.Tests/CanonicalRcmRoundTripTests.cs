using System.Text;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;

namespace Apex.Ledger.Io.Tests;

/// <summary>
/// Phase-9 slice-2 <b>RCM Io fold-in</b> gate (RQ-3/RQ-7/RQ-8; RQ-23; ER-13): an RCM company — notified categories +
/// a posted reverse-charge dual leg + a self-invoice document + (S2b-empty) CDN link + advance receipt —
/// <b>exports and re-imports exact in JSON AND XML</b>, both byte-stable and into a fresh (differently-Guid'd) company
/// through the engine-routed <see cref="CompanyImportService"/> (all-or-nothing; the dual-leg balance is enforced by the
/// engine, the cash-only structural guard by the service). A batch mis-routing the RCM output liability to a normal
/// Output ledger rejects wholesale. A plain GST company (no RCM) serialises with empty <c>rcmCategories</c> and inert
/// reverse-charge flags — byte-identical shape to a v38 company (ER-13). De-brand clean (no "Tally").
/// </summary>
public sealed class CanonicalRcmRoundTripTests
{
    private const string GstinMaharashtra = "27AAPFU0939F1ZV";
    private const string GstinGujarat = "24AAACC1206D1ZM";
    private static readonly DateOnly FyStart = new(2025, 4, 1);
    private static readonly DateOnly D1 = new(2025, 4, 10);

    private static Company BuildRcmCompany()
    {
        var c = CompanyFactory.CreateSeeded("RCM Traders", FyStart);
        var gst = new GstService(c);
        gst.EnableGst(new GstConfig
        {
            HomeStateCode = "27", Gstin = GstinMaharashtra, RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart, Periodicity = GstReturnPeriodicity.Monthly,
        });
        gst.SeedAdvancedGst();
        var rcm = new RcmService(c);

        var legal = c.Gst!.RcmCategories.First(x => x.SupplyNature == "Legal");
        var expense = new Domain.Ledger(Guid.NewGuid(), "Legal Fees", c.FindGroupByName("Indirect Expenses")!.Id, Money.Zero, true)
        {
            SalesPurchaseGst = new StockItemGstDetails
            {
                Taxability = GstTaxability.Taxable, RateBasisPoints = 1800, SupplyType = GstSupplyType.Services,
                ReverseChargeApplicable = true, RcmCategoryId = legal.Id,
            },
        };
        c.AddLedger(expense);
        var party = new Domain.Ledger(Guid.NewGuid(), "Advocate", c.FindGroupByName("Sundry Creditors")!.Id, Money.Zero, false)
        {
            PartyGst = new PartyGstDetails { RegistrationType = GstRegistrationType.Regular, Gstin = GstinGujarat, StateCode = "24", IsBodyCorporate = true },
        };
        c.AddLedger(party);

        var posting = rcm.BuildReverseCharge(Money.FromRupees(10000m), null, expense, party.PartyGst, D1, RcmService.SupplyKind.Domestic);
        var lines = new List<EntryLine> { new(expense.Id, Money.FromRupees(10000m), DrCr.Debit), new(party.Id, Money.FromRupees(10000m), DrCr.Credit) };
        lines.AddRange(posting.Lines);
        var v = new LedgerService(c).Post(new Voucher(Guid.NewGuid(), c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase).Id, D1, lines));

        rcm.GenerateSelfInvoice(v.Id, D1, D1.AddDays(3), supplierIsRegistered: false, supplierLedgerId: party.Id);
        c.AddCreditDebitNoteLink(new GstCreditDebitNoteLink(Guid.NewGuid(), v.Id, CdnType.Credit, v.Id, "INV-1", D1, "01 sales return"));
        c.AddAdvanceReceipt(new GstAdvanceReceipt(Guid.NewGuid(), v.Id, isService: true, Money.FromRupees(5000m), 1800, interState: true, "24", Money.FromRupees(900m)));
        return c;
    }

    private static Company Fresh() => CompanyFactory.CreateSeeded("Fresh RCM Co", FyStart);

    [Fact]
    public void Json_round_trips_byte_stable()
    {
        var c = BuildRcmCompany();
        var first = CanonicalJson.Export(c);
        var (model, errors) = CanonicalJson.Parse(first);
        Assert.Empty(errors);
        Assert.Equal(first, CanonicalJson.Export(model!));
        Assert.DoesNotContain("Tally", Encoding.UTF8.GetString(first), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Xml_round_trips_byte_stable()
    {
        var c = BuildRcmCompany();
        var first = CanonicalXml.Export(c);
        var (model, errors) = CanonicalXml.Parse(first);
        Assert.Empty(errors);
        Assert.Equal(first, CanonicalXml.Export(model!));
    }

    [Fact]
    public void Json_and_xml_carry_identical_payload()
    {
        var c = BuildRcmCompany();
        var (jm, je) = CanonicalJson.Parse(CanonicalJson.Export(c));
        var (xm, xe) = CanonicalXml.Parse(CanonicalXml.Export(c));
        Assert.Empty(je);
        Assert.Empty(xe);
        Assert.Equal(CanonicalJson.Export(jm!), CanonicalJson.Export(xm!));
    }

    [Fact]
    public void Export_import_into_fresh_company_reconciles_json_and_xml()
    {
        var source = BuildRcmCompany();
        var expectedCategories = source.Gst!.RcmCategories.Count;

        foreach (var bytes in new[] { CanonicalJson.Export(source), CanonicalXml.Export(source) })
        {
            var (model, errors) = bytes[0] == (byte)'{' ? CanonicalJson.Parse(bytes) : CanonicalXml.Parse(bytes);
            Assert.Empty(errors);

            var fresh = Fresh();
            var result = new CompanyImportService(fresh).Apply(model!);
            Assert.True(result.Applied, string.Join("; ", result.Errors));

            // Categories + the S/P-ledger RCM link survived.
            Assert.Equal(expectedCategories, fresh.Gst!.RcmCategories.Count);
            var legal = fresh.Gst.RcmCategories.First(x => x.SupplyNature == "Legal");
            Assert.Equal(legal.Id, fresh.FindLedgerByName("Legal Fees")!.SalesPurchaseGst!.RcmCategoryId);

            // The posted RCM dual leg survived: an output liability on an RCM Output ledger + an OtherRcm ITC.
            var v = fresh.Vouchers.Single(x => fresh.FindVoucherType(x.TypeId)!.BaseType == VoucherBaseType.Purchase);
            var outLine = v.Lines.Single(l => l.Gst is { IsReverseCharge: true, RcmScheme: null } && l.Side == DrCr.Credit);
            Assert.True(fresh.FindLedger(outLine.LedgerId)!.GstClassification is { IsReverseCharge: true });
            Assert.Contains(v.Lines, l => l.Gst is { IsReverseCharge: true, RcmScheme: RcmItcScheme.OtherRcm } && l.Side == DrCr.Debit);

            // The self-invoice document + CDN link + advance receipt survived, re-linked to the imported voucher.
            var doc = Assert.Single(fresh.RcmDocuments);
            Assert.Equal(RcmDocumentKind.SelfInvoice, doc.Kind);
            Assert.Equal(v.Id, doc.SourceVoucherId);
            var link = Assert.Single(fresh.CreditDebitNoteLinks);
            Assert.Equal(CdnType.Credit, link.CdnType);
            var adv = Assert.Single(fresh.AdvanceReceipts);
            Assert.Equal(Money.FromRupees(900m), adv.AdvanceTax);
        }
    }

    [Fact]
    public void Import_rejects_an_rcm_output_liability_mis_routed_to_a_normal_output_ledger()
    {
        // Hand-build a company whose RCM output-liability line posts to the NORMAL Output IGST ledger (not a dedicated
        // RCM Output ledger) — the cash-only structural invariant the CompanyImportService guard enforces.
        var c = CompanyFactory.CreateSeeded("Mis-routed RCM Co", FyStart);
        var gst = new GstService(c);
        gst.EnableGst(new GstConfig
        {
            HomeStateCode = "27", Gstin = GstinMaharashtra, RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart, Periodicity = GstReturnPeriodicity.Monthly,
        });
        var expense = new Domain.Ledger(Guid.NewGuid(), "Legal Fees", c.FindGroupByName("Indirect Expenses")!.Id, Money.Zero, true);
        c.AddLedger(expense);
        var party = new Domain.Ledger(Guid.NewGuid(), "Advocate", c.FindGroupByName("Sundry Creditors")!.Id, Money.Zero, false);
        c.AddLedger(party);
        var normalOutputIgst = gst.FindTaxLedger(GstTaxHead.Integrated, GstTaxDirection.Output)!;
        var inputIgst = gst.FindTaxLedger(GstTaxHead.Integrated, GstTaxDirection.Input)!;

        var lines = new List<EntryLine>
        {
            new(expense.Id, Money.FromRupees(10000m), DrCr.Debit),
            new(party.Id, Money.FromRupees(10000m), DrCr.Credit),
            // RCM output liability tag but posted to the NORMAL Output IGST ledger (the mis-route).
            new(normalOutputIgst.Id, Money.FromRupees(1800m), DrCr.Credit, gst: new GstLineTax(GstTaxHead.Integrated, 1800, Money.FromRupees(10000m), isReverseCharge: true)),
            new(inputIgst.Id, Money.FromRupees(1800m), DrCr.Debit, gst: new GstLineTax(GstTaxHead.Integrated, 1800, Money.FromRupees(10000m), isReverseCharge: true, rcmScheme: RcmItcScheme.OtherRcm)),
        };
        new LedgerService(c).Post(new Voucher(Guid.NewGuid(), c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase).Id, D1, lines));

        var (model, errors) = CanonicalJson.Parse(CanonicalJson.Export(c));
        Assert.Empty(errors);

        var result = new CompanyImportService(Fresh()).Apply(model!);
        Assert.False(result.Applied);
        Assert.Contains(result.Errors, e => e.Contains("RCM output", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Import_rejects_a_mis_routed_rcm_output_liability_even_when_tagged_with_a_non_null_scheme()
    {
        // Hardening (#7): the cash-only structural guard identifies the RCM output leg STRUCTURALLY (a reverse-charge
        // Credit line whose target ledger is not an RCM Output classification) — it is NOT keyed on RcmScheme being null.
        // A corrupt/hand-edited batch that tags the mis-routed liability leg with a NON-NULL RcmScheme (here OtherRcm)
        // must STILL be rejected — otherwise the §49(4) cash-only invariant is bypassable.
        var c = CompanyFactory.CreateSeeded("Corrupt-scheme RCM Co", FyStart);
        var gst = new GstService(c);
        gst.EnableGst(new GstConfig
        {
            HomeStateCode = "27", Gstin = GstinMaharashtra, RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart, Periodicity = GstReturnPeriodicity.Monthly,
        });
        var expense = new Domain.Ledger(Guid.NewGuid(), "Legal Fees", c.FindGroupByName("Indirect Expenses")!.Id, Money.Zero, true);
        c.AddLedger(expense);
        var party = new Domain.Ledger(Guid.NewGuid(), "Advocate", c.FindGroupByName("Sundry Creditors")!.Id, Money.Zero, false);
        c.AddLedger(party);
        var normalOutputIgst = gst.FindTaxLedger(GstTaxHead.Integrated, GstTaxDirection.Output)!;
        var inputIgst = gst.FindTaxLedger(GstTaxHead.Integrated, GstTaxDirection.Input)!;

        var lines = new List<EntryLine>
        {
            new(expense.Id, Money.FromRupees(10000m), DrCr.Debit),
            new(party.Id, Money.FromRupees(10000m), DrCr.Credit),
            // RCM output liability CREDIT tagged with a NON-NULL scheme (the corruption) but posted to the NORMAL Output
            // IGST ledger (the mis-route). The old `RcmScheme: null` predicate would have let this slip through.
            new(normalOutputIgst.Id, Money.FromRupees(1800m), DrCr.Credit, gst: new GstLineTax(GstTaxHead.Integrated, 1800, Money.FromRupees(10000m), isReverseCharge: true, rcmScheme: RcmItcScheme.OtherRcm)),
            new(inputIgst.Id, Money.FromRupees(1800m), DrCr.Debit, gst: new GstLineTax(GstTaxHead.Integrated, 1800, Money.FromRupees(10000m), isReverseCharge: true, rcmScheme: RcmItcScheme.OtherRcm)),
        };
        new LedgerService(c).Post(new Voucher(Guid.NewGuid(), c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase).Id, D1, lines));

        var (model, errors) = CanonicalJson.Parse(CanonicalJson.Export(c));
        Assert.Empty(errors);

        var result = new CompanyImportService(Fresh()).Apply(model!);
        Assert.False(result.Applied);
        Assert.Contains(result.Errors, e => e.Contains("RCM output", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Plain_gst_company_serialises_with_empty_rcm_fields()
    {
        // ER-13: an RCM-off company exports rcmCategories empty, classification/line reverse-charge flags inert.
        var c = CompanyFactory.CreateSeeded("Plain GST Co", FyStart);
        var gst = new GstService(c);
        gst.EnableGst(new GstConfig
        {
            HomeStateCode = "27", Gstin = GstinMaharashtra, RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart, Periodicity = GstReturnPeriodicity.Monthly,
        });

        var (model, errors) = CanonicalJson.Parse(CanonicalJson.Export(c));
        Assert.Empty(errors);
        Assert.Empty(model!.Company.Gst!.RcmCategories);
        Assert.Empty(model.Payload.RcmDocuments);
        Assert.Empty(model.Payload.CreditDebitNoteLinks);
        Assert.Empty(model.Payload.AdvanceReceipts);
        // The auto-created tax ledgers carry no reverse-charge discriminator.
        Assert.All(model.Payload.Ledgers.Where(l => l.GstClassification is not null),
            l => Assert.False(l.GstClassification!.IsReverseCharge));
    }

    [Fact]
    public void A_non_gst_company_serialises_de_branded()
    {
        var c = CompanyFactory.CreateSeeded("Robert Transport", FyStart);
        var (model, errors) = CanonicalJson.Parse(CanonicalJson.Export(c));
        Assert.Empty(errors);
        Assert.Null(model!.Company.Gst);
        Assert.Empty(model.Payload.RcmDocuments);
        Assert.DoesNotContain("Tally", Encoding.UTF8.GetString(CanonicalJson.Export(c)), StringComparison.OrdinalIgnoreCase);
    }
}
