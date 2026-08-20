using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Xunit;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.Tests;

/// <summary>
/// <b>T0-11 slice S1 — the classification seam, and the ER-13 proof that introducing it moved NOTHING.</b>
///
/// <para>S1 adds <see cref="GstReportSupport.ClassifyPrintedDocument"/> and re-expresses the shipped
/// Sales / Bill-of-Supply outcome through it. It claims <b>no new behaviour whatsoever</b>, so its acceptance
/// criterion is BYTE-IDENTITY: every document this app could print before the seam existed must come out
/// byte-for-byte identical after it. A slice that claims nothing new can only be proved by showing that nothing
/// moved, and this repository has no byte goldens for the print pipeline at all — PDF assertions everywhere else
/// are substring probes over a Latin-1 decode, which cannot see a moved column, a dropped row or a changed
/// declaration. So the golden is built here, first.</para>
///
/// <para><b>🔴 HOW THE GOLDEN HASHES BELOW WERE OBTAINED, AND WHY THAT IS NOT "A GOLDEN EDITED TO MATCH THE
/// CODE".</b> They are SHA-256 of the PDF bytes produced by the code as it stood <b>BEFORE</b> a single production
/// line of S1 was written — captured at commit <c>23c4d69</c>, with the seam not yet in existence. That is the whole
/// point of a byte-identity golden and it is the opposite of the failure mode this project has logged: the expected
/// value is not derived from the new code, it is derived from the OLD code, and the new code has to reproduce it.
/// The two disciplines that make it binding:</para>
/// <list type="number">
/// <item><b>Capture strictly precedes the change.</b> Every literal below was frozen while HEAD was unmodified.</item>
/// <item><b>The harness was proved SENSITIVE before it was trusted.</b> A byte-identity test that cannot fail is
/// worth nothing; this one was deliberately broken (one character changed in
/// <c>GstReportSupport.BillOfSupplyTitle</c>) and the composition and exempt rows went red on the spot, with the
/// taxable rows staying green — i.e. it discriminates, it does not merely pass.</item>
/// </list>
///
/// <para><b>🔴 SLICE S2 MOVED EXACTLY ONE ROW, <c>purchase/item</c>, AND THIS IS THE DISCIPLINE THAT MAKES REPLACING
/// A GOLDEN HASH LEGITIMATE RATHER THAN THE FAILURE MODE THIS PROJECT HAS LOGGED.</b> A hash is not an expectation
/// anybody can derive, and pasting one from a failing run is, on its own, exactly "a golden edited to match the
/// code". What makes this one binding is that <b>the bytes it summarises are pinned independently, BY CONTENT, from
/// the requirement</b>: <c>PurchaseRecordPrintTests</c> asserts the title, the item rows and their values, the
/// party-block orientation, the suppressed place of supply / declaration / signature, the number caption and the
/// supplier-tax caption — each cited to RQ-11a, which slice S0 wrote before a line of this code existed. The hash
/// then adds the one thing those assertions cannot: that <b>nothing ELSE</b> on that page moved. And the nine other
/// rows, untouched, are the ER-13 proof that S2's behaviour change reached one document class and no other.</para>
///
/// <para>The old value (<c>5a065e5a…</c>, the plain Dr/Cr voucher) WAS the defect. It was captured at 23c4d69 and
/// deliberately carried through S1, so that a slice claiming to be a pure refactor could not have fixed it
/// quietly.</para>
///
/// <para>Money is odd to the paisa throughout: a round figure passes under a rounding defect and asserts nothing.</para>
/// </summary>
public sealed class PrintedDocumentClassificationTests
{
    private const string GstinMaharashtra = "27AAPFU0939F1ZV";
    private const string GstinGujarat = "24AAACC1206D1ZM";
    private static readonly DateOnly FyStart = new(2025, 4, 1);
    private static readonly DateOnly DocDate = new(2025, 4, 10);

    // ================================================================ the fixture

    private sealed class Fx
    {
        public required Company Company { get; init; }
        public required Guid GodownId { get; init; }
        public required Guid TaxableItemId { get; init; }
        public required Guid ExemptItemId { get; init; }
        public required Guid SalesLedgerId { get; init; }
        public required Guid PurchaseLedgerId { get; init; }
        public required Guid ExemptServiceLedgerId { get; init; }
        public required Guid ZeroRatedServiceLedgerId { get; init; }
        /// <summary>The Gujarat (24) buyer — INTER-state routing (IGST).</summary>
        public required Guid PartyId { get; init; }
        /// <summary>The Maharashtra (27) buyer — INTRA-state routing (CGST+SGST), and the only routing
        /// CGST §10(2)(c) permits a composition dealer.</summary>
        public required Guid LocalPartyId { get; init; }
        public required Guid SupplierId { get; init; }
        public Guid SalesTypeId => Company.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id;
        public Guid PurchaseTypeId => Company.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase).Id;
    }

    private static DomainLedger Add(Company c, string name, string groupName, bool openingIsDebit)
    {
        var l = new DomainLedger(Guid.NewGuid(), name, c.FindGroupByName(groupName)!.Id, Money.Zero, openingIsDebit);
        c.AddLedger(l);
        return l;
    }

    /// <summary>A fixed-name company so the printed seller block — and therefore the bytes — is stable across
    /// runs and across registrations. Nothing GUID-derived reaches a printed document.</summary>
    private static Fx Build(GstRegistrationType registration)
    {
        var c = CompanyFactory.CreateSeeded("Seam Fixture Co", FyStart);
        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = "27",
            Gstin = GstinMaharashtra,
            RegistrationType = registration,
            CompositionSubType = registration == GstRegistrationType.Composition ? CompositionSubType.Trader : null,
            ApplicableFrom = FyStart,
            Periodicity = GstReturnPeriodicity.Monthly,
        });

        var inv = new InventoryService(c);
        var grp = inv.CreateStockGroup("Goods");
        var nos = inv.CreateSimpleUnit("Nos", "Numbers", unitQuantityCode: "NOS");

        var taxable = inv.CreateStockItem("Widget", grp.Id, nos.Id);
        taxable.Gst = new StockItemGstDetails
        { HsnSac = "847130", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800 };
        var exempt = inv.CreateStockItem("Fresh Milk", grp.Id, nos.Id);
        exempt.Gst = new StockItemGstDetails { HsnSac = "040110", Taxability = GstTaxability.Exempt };

        var sales = Add(c, "Sales", "Sales Accounts", false);
        var purchases = Add(c, "Purchases", "Purchase Accounts", true);

        var exemptService = Add(c, "Exempt Service Income", "Sales Accounts", false);
        exemptService.SalesPurchaseGst = new StockItemGstDetails
        { HsnSac = "999999", Taxability = GstTaxability.Exempt, SupplyType = GstSupplyType.Services };
        // A TAXABLE service at 0% (LUT/export): a genuine Rule-46 tax invoice that posts no tax leg, so it needs no
        // seeded Output tax ledger and therefore exists under BOTH registrations.
        var zeroRatedService = Add(c, "Export Consultancy (LUT)", "Sales Accounts", false);
        zeroRatedService.SalesPurchaseGst = new StockItemGstDetails
        {
            HsnSac = "998311", Taxability = GstTaxability.Taxable, RateBasisPoints = 0,
            SupplyType = GstSupplyType.Services,
        };

        var party = Add(c, "Gujarat Buyer", "Sundry Debtors", true);
        party.PartyGst = new PartyGstDetails
        { RegistrationType = GstRegistrationType.Regular, Gstin = GstinGujarat, StateCode = "24" };
        var localParty = Add(c, "Mumbai Buyer", "Sundry Debtors", true);
        localParty.PartyGst = new PartyGstDetails
        { RegistrationType = GstRegistrationType.Consumer, StateCode = "27" };
        var supplier = Add(c, "Gujarat Supplier", "Sundry Creditors", false);
        supplier.PartyGst = new PartyGstDetails
        { RegistrationType = GstRegistrationType.Regular, Gstin = GstinGujarat, StateCode = "24" };

        return new Fx
        {
            Company = c,
            GodownId = c.MainLocation!.Id,
            TaxableItemId = taxable.Id,
            ExemptItemId = exempt.Id,
            SalesLedgerId = sales.Id,
            PurchaseLedgerId = purchases.Id,
            ExemptServiceLedgerId = exemptService.Id,
            ZeroRatedServiceLedgerId = zeroRatedService.Id,
            PartyId = party.Id,
            LocalPartyId = localParty.Id,
            SupplierId = supplier.Id,
        };
    }

    private static DomainLedger OutputHead(Company c, GstTaxHead head) =>
        c.Ledgers.Single(l => l.GstClassification is
        { Direction: GstTaxDirection.Output, IsReverseCharge: false } g && g.TaxHead == head);

    /// <summary>An item sale with NO posted tax leg — the exempt / composition shapes.</summary>
    private static Voucher ItemSale(Fx f, Guid partyId, Guid itemId, decimal value, int number,
        bool cancelled = false) =>
        new(Guid.NewGuid(), f.SalesTypeId, DocDate, new List<EntryLine>
        {
            new(partyId, new Money(value), DrCr.Debit),
            new(f.SalesLedgerId, new Money(value), DrCr.Credit),
        }, number: number, partyId: partyId, cancelled: cancelled, inventoryLines: new[]
        {
            new VoucherInventoryLine(itemId, f.GodownId, 1m, new Money(value)),
        });

    /// <summary>An INTER-state item sale carrying a posted, tagged IGST leg — the ordinary Rule-46 tax invoice.</summary>
    private static Voucher TaxedInterStateSale(Fx f, decimal value, decimal igst, int number, bool cancelled = false)
    {
        var outIgst = OutputHead(f.Company, GstTaxHead.Integrated);
        return new Voucher(Guid.NewGuid(), f.SalesTypeId, DocDate, new List<EntryLine>
        {
            new(f.PartyId, new Money(value + igst), DrCr.Debit),
            new(f.SalesLedgerId, new Money(value), DrCr.Credit),
            new(outIgst.Id, new Money(igst), DrCr.Credit,
                gst: new GstLineTax(GstTaxHead.Integrated, 1800, new Money(value))),
        }, number: number, partyId: f.PartyId, cancelled: cancelled, inventoryLines: new[]
        {
            new VoucherInventoryLine(f.TaxableItemId, f.GodownId, 1m, new Money(value)),
        });
    }

    /// <summary>An INTRA-state item sale carrying posted, tagged CGST + SGST legs.</summary>
    private static Voucher TaxedIntraStateSale(Fx f, decimal value, decimal cgst, decimal sgst, int number)
    {
        var outCgst = OutputHead(f.Company, GstTaxHead.Central);
        var outSgst = OutputHead(f.Company, GstTaxHead.State);
        return new Voucher(Guid.NewGuid(), f.SalesTypeId, DocDate, new List<EntryLine>
        {
            new(f.LocalPartyId, new Money(value + cgst + sgst), DrCr.Debit),
            new(f.SalesLedgerId, new Money(value), DrCr.Credit),
            new(outCgst.Id, new Money(cgst), DrCr.Credit,
                gst: new GstLineTax(GstTaxHead.Central, 1800, new Money(value))),
            new(outSgst.Id, new Money(sgst), DrCr.Credit,
                gst: new GstLineTax(GstTaxHead.State, 1800, new Money(value))),
        }, number: number, partyId: f.LocalPartyId, inventoryLines: new[]
        {
            new VoucherInventoryLine(f.TaxableItemId, f.GodownId, 1m, new Money(value)),
        });
    }

    /// <summary>A LEDGER-ONLY Sales voucher. <paramref name="accountingInvoice"/> is the v49 persisted flag
    /// <c>IsServiceAccountingInvoice</c> gates on, so the same two legs are a service tax invoice with it and a plain
    /// As-Voucher sale without it.</summary>
    private static Voucher LedgerOnlySale(Fx f, Guid incomeLedgerId, decimal value, bool accountingInvoice,
        int number) =>
        new(Guid.NewGuid(), f.SalesTypeId, DocDate, new List<EntryLine>
        {
            new(f.PartyId, new Money(value), DrCr.Debit),
            new(incomeLedgerId, new Money(value), DrCr.Credit),
        }, number: number, partyId: f.PartyId, isAccountingInvoice: accountingInvoice);

    /// <summary>An item PURCHASE — the shape T0-11 exists to fix. It prints the plain Dr/Cr voucher today.</summary>
    private static Voucher PurchaseItem(Fx f, decimal value, int number) =>
        new(Guid.NewGuid(), f.PurchaseTypeId, DocDate, new List<EntryLine>
        {
            new(f.PurchaseLedgerId, new Money(value), DrCr.Debit),
            new(f.SupplierId, new Money(value), DrCr.Credit),
        }, number: number, partyId: f.SupplierId, inventoryLines: new[]
        {
            new VoucherInventoryLine(f.TaxableItemId, f.GodownId, 1m, new Money(value)),
        });

    /// <summary>The §10 contradiction — a composition dealer's sale that nonetheless posted forward tax. Neither
    /// statutory document may be issued for it, so it prints the plain voucher.</summary>
    private static Voucher SectionTenContradiction(Fx f, decimal value, decimal cgst, decimal sgst, int number)
    {
        var v = TaxedIntraStateSale(f, value, cgst, sgst, number);
        f.Company.Gst!.RegistrationType = GstRegistrationType.Composition;
        f.Company.Gst!.CompositionSubType = CompositionSubType.Trader;
        return v;
    }

    // ================================================================ the matrix

    /// <summary>One printable shape, named, with the company it lives in. Each entry builds its OWN company so a
    /// registration switch in one shape cannot leak into another's bytes.</summary>
    private readonly record struct Shape(string Name, Company Company, Voucher Voucher);

    private static IReadOnlyList<Shape> EveryShippedShape()
    {
        var shapes = new List<Shape>();

        var reg = Build(GstRegistrationType.Regular);
        shapes.Add(new("sales/item/taxable-interstate",
            reg.Company, TaxedInterStateSale(reg, 94_271.63m, 16_968.89m, 1)));

        var reg2 = Build(GstRegistrationType.Regular);
        shapes.Add(new("sales/item/taxable-intrastate",
            reg2.Company, TaxedIntraStateSale(reg2, 2_17_483.91m, 19_573.55m, 19_573.56m, 2)));

        var reg3 = Build(GstRegistrationType.Regular);
        shapes.Add(new("sales/item/wholly-exempt",
            reg3.Company, ItemSale(reg3, reg3.PartyId, reg3.ExemptItemId, 1_63_059.37m, 3)));

        var comp = Build(GstRegistrationType.Composition);
        shapes.Add(new("sales/item/composition",
            comp.Company, ItemSale(comp, comp.LocalPartyId, comp.TaxableItemId, 1_63_059.37m, 4)));

        var reg4 = Build(GstRegistrationType.Regular);
        shapes.Add(new("sales/service/zero-rated",
            reg4.Company, LedgerOnlySale(reg4, reg4.ZeroRatedServiceLedgerId, 94_271.63m, true, 5)));

        var reg5 = Build(GstRegistrationType.Regular);
        shapes.Add(new("sales/service/exempt",
            reg5.Company, LedgerOnlySale(reg5, reg5.ExemptServiceLedgerId, 2_17_483.91m, true, 6)));

        var reg6 = Build(GstRegistrationType.Regular);
        shapes.Add(new("sales/item/taxable-cancelled",
            reg6.Company, TaxedInterStateSale(reg6, 94_271.63m, 16_968.89m, 7, cancelled: true)));

        var reg7 = Build(GstRegistrationType.Regular);
        shapes.Add(new("sales/ledger-only/plain",
            reg7.Company, LedgerOnlySale(reg7, reg7.SalesLedgerId, 41_209.09m, false, 8)));

        var reg8 = Build(GstRegistrationType.Regular);
        shapes.Add(new("purchase/item", reg8.Company, PurchaseItem(reg8, 2_17_483.91m, 9)));

        var reg9 = Build(GstRegistrationType.Regular);
        shapes.Add(new("sales/item/section-10-contradiction",
            reg9.Company, SectionTenContradiction(reg9, 2_17_483.91m, 19_573.55m, 19_573.56m, 10)));

        return shapes;
    }

    private static string Sha256Of(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    // ================================================================ the ER-13 byte-identity gate

    /// <summary>
    /// <b>T7 — every document this app could print before the seam existed still renders the SAME BYTES.</b>
    ///
    /// <para>It drives the whole printable matrix through the real user path —
    /// <c>VoucherDetailViewModel.BuildPrintPreview()</c>, which is what P / Ctrl+P calls — so all three things S1
    /// touches are inside the assertion: the routing decision (invoice vs plain voucher), the projection
    /// (<c>ProjectInvoice</c> / <c>ProjectServiceInvoice</c> / <c>ProjectVoucher</c>) and the rendered PDF. A
    /// substring probe cannot see a moved column or a dropped declaration; a hash of the whole file can.</para>
    ///
    /// <para>The renderers take no clock and no RNG (<c>InvoicePdf</c> / <c>VoucherPdf</c> are documented
    /// deterministic, and <c>PrintPreviewViewModel</c> supplies a page footer with no date), so the hash is a
    /// function of the fixture alone — which is also asserted, below, by rendering each shape twice.</para>
    ///
    /// <para>🔴 If a hash below ever needs changing, that is not a maintenance chore: it means a printed document
    /// moved. Change it only with the slice, the requirement and the derivation that authorised the move written
    /// beside it.</para>
    /// </summary>
    [Fact]
    public void Every_shipped_printed_document_is_byte_identical_after_the_classification_seam()
    {
        var expected = new Dictionary<string, string>
        {
            ["sales/item/taxable-interstate"] = "4be51f36bd1c0e6688eade8e6a3f2c6e3413016242ac4b47c3dcd5e5073f0d57",   // TAX INVOICE, IGST breakup
            ["sales/item/taxable-intrastate"] = "b3702a3ccee48e56606adf4abc720cc8def1e53a6da0f82ca2b7b511849a436d",   // TAX INVOICE, CGST+SGST breakup
            ["sales/item/wholly-exempt"] = "72a50acf66c0db81e59af4269f842d1a1d4fa418f85ddf2e7e9f38eb8a94d0de",   // BILL OF SUPPLY, no declaration
            ["sales/item/composition"] = "8de8b2dc059e01f3d75f48a22e0b31ab88413dd51b8a03492cb6366b62058d56",   // BILL OF SUPPLY + Rule 5(1)(f)
            ["sales/service/zero-rated"] = "16483b75fa2b52a0ab34157950103646184d56398b689ffcb351fcc0e2146e04",   // TAX INVOICE, service pass
            ["sales/service/exempt"] = "efb41f873629006cbb1d2cd26912e957088d854282fd4dade2c7c72a3175cc2b",   // BILL OF SUPPLY, service pass
            ["sales/item/taxable-cancelled"] = "8140742878f565f2530d7d00fee6b9aff20a823511ae386a049dd9a2da4fb851",   // TAX INVOICE + CANCELLED over-print
            ["sales/ledger-only/plain"] = "5d17527e76d0be49b935d70819ff631dc3f3e595bc6befa83ca0702d946a9968",   // plain Dr/Cr voucher
            // 🔴 THE ONE ROW SLICE S2 MOVED, AND THE ONLY ONE. Its old value was the plain Dr/Cr voucher captured at
            // 23c4d69 — the defect itself. See the note under the summary for why replacing a hash is legitimate here
            // and only here, and what fixes the new bytes.
            ["purchase/item"] = "77fd5b99eb49e94a8d760c277b8016beae45e56ccb000fe4978784e7bfb04170",   // PURCHASE RECORD (S2)
            ["sales/item/section-10-contradiction"] = "911d5d4c5b5c31a02fe6dd78dedc87a13c2f0d337c27780d0b9929b883018897",   // plain voucher; no statutory title
        };

        var shapes = EveryShippedShape();
        Assert.Equal(expected.Count, shapes.Count);   // no shape may be silently dropped from the gate

        foreach (var s in shapes)
        {
            var bytes = new VoucherDetailViewModel(s.Company, s.Voucher).BuildPrintPreview().PdfBytes;
            Assert.True(bytes.Length > 0, $"{s.Name} rendered no bytes at all");
            Assert.True(expected[s.Name] == Sha256Of(bytes),
                $"{s.Name} no longer renders the bytes it rendered before the classification seam existed " +
                $"(expected sha256 {expected[s.Name]}, got {Sha256Of(bytes)}). A refactor may not move a printed " +
                "document; if this move is intended, it is not a refactor.");
        }
    }

    /// <summary>
    /// The byte-identity gate above is only meaningful if the bytes are a function of the fixture and nothing else.
    /// Rendering every shape twice in the same process and requiring the two hashes to match pins that: a clock, a
    /// GUID or a hash-ordering leak into the PDF would show up here as a flake rather than as a mysterious golden
    /// churn later.
    /// </summary>
    [Fact]
    public void The_printed_bytes_are_a_function_of_the_fixture_alone()
    {
        foreach (var s in EveryShippedShape())
        {
            var first = new VoucherDetailViewModel(s.Company, s.Voucher).BuildPrintPreview().PdfBytes;
            var second = new VoucherDetailViewModel(s.Company, s.Voucher).BuildPrintPreview().PdfBytes;
            Assert.Equal(Sha256Of(first), Sha256Of(second));
        }
    }

    // ================================================================ the classification itself, asserted as ANSWERS

    /// <summary>What the statute says each shipped shape's document is. Written from CGST Act §31(1)/(2) and
    /// §31(3)(c) + Rule 49, NOT read off <c>ClassifyPrintedDocument</c> — an expectation copied from the code under
    /// test is this project's documented failure mode.</summary>
    private readonly record struct Expected(
        DocumentRole Role, string Title, string ScreenLabel, bool RendersItemDetail, TaxParticulars StatesTax);

    private static readonly IReadOnlyDictionary<string, Expected> StatutoryExpectation =
        new Dictionary<string, Expected>
        {
            // §31(1): "a registered person supplying goods shall … issue a tax invoice". We supply; the supply is
            // taxable; Rule 46(l)/(m) put the rate and the amount of tax charged on it. Both routings, same document.
            ["sales/item/taxable-interstate"] =
                new(DocumentRole.Issued, "TAX INVOICE", "Tax Invoice", true, TaxParticulars.AsChargedByUs),
            ["sales/item/taxable-intrastate"] =
                new(DocumentRole.Issued, "TAX INVOICE", "Tax Invoice", true, TaxParticulars.AsChargedByUs),
            // §31(3)(c): "a registered person supplying exempted goods … shall issue, instead of a tax invoice, a bill
            // of supply". §2(47) folds nil-rated and non-taxable in. Rule 49 prescribes NO rate and NO tax amount, so
            // the document states no tax as charged by us.
            ["sales/item/wholly-exempt"] =
                new(DocumentRole.Issued, "BILL OF SUPPLY", "Bill of Supply", true, TaxParticulars.None),
            // §31(3)(c)'s other limb — "paying tax under the provisions of section 10" — plus §10(4), which forbids
            // him to "collect any tax from the recipient on supplies made by him".
            ["sales/item/composition"] =
                new(DocumentRole.Issued, "BILL OF SUPPLY", "Bill of Supply", true, TaxParticulars.None),
            // A TAXABLE service at 0% (LUT/export) is not an exempt supply — it attracts tax at a nil RATE under a
            // taxable classification — so §31(2)'s tax invoice stands even though no tax leg is posted.
            ["sales/service/zero-rated"] =
                new(DocumentRole.Issued, "TAX INVOICE", "Tax Invoice", true, TaxParticulars.AsChargedByUs),
            // …whereas a declared-exempt service takes §31(3)(c) exactly as exempt goods do.
            ["sales/service/exempt"] =
                new(DocumentRole.Issued, "BILL OF SUPPLY", "Bill of Supply", true, TaxParticulars.None),
            // Cancelling a document does not change what it was ISSUED as; the CANCELLED over-print rides alongside
            // the statutory title rather than replacing it (Phase 10.11 S3).
            ["sales/item/taxable-cancelled"] =
                new(DocumentRole.Issued, "TAX INVOICE", "Tax Invoice", true, TaxParticulars.AsChargedByUs),
            // An As-Voucher sale is not entered in an invoice mode at all: RQ-11 gives the invoice format to the item
            // invoice and the accounting invoice, and this is neither. No statutory document is projected for it, so
            // it names none — and it prints the plain Dr/Cr voucher, which states every posted leg exactly, which is
            // why it may state tax.
            ["sales/ledger-only/plain"] =
                new(DocumentRole.NoStatutoryDocument, "", "", false, TaxParticulars.AsChargedByUs),
            // 🔴 THE T0-11 DEFECT, NOW FIXED — MOVED BY SLICE S2, AND HERE IS THE DERIVATION S1 DEMANDED.
            // Every field below comes from RQ-11a (docs/phase5-reports-io-requirements.md), which slice S0 wrote
            // BEFORE any of this code existed, and from the statute RQ-11a cites — not from what the new classifier
            // returns. Field by field:
            //   Role     — RECORDED. §31(1)/(2) attach the duty to "a registered person SUPPLYING". On an inward
            //              supply we supply nothing, so we issue nothing and merely record what the supplier issued.
            //              (Unchanged in substance from S1: the Role was already "not Issued". What moved is that a
            //              document we do not issue is now a NAMED class instead of "no document at all".)
            //   Title    — "PURCHASE RECORD". RQ-11a: NOT "Tax Invoice", NOT "Bill of Supply" (Rule 49 puts that one
            //              on the supplier too). 🔴 The STRING is OURS, ruling 9 — the corpus names no title for a
            //              purchase print and evidences no law-driven title derivation.
            //   Renders  — TRUE. RQ-11a: the record "SHALL carry the same item detail RQ-11(c) requires". This is the
            //              user-visible defect: the goods bought never appeared on the page at all.
            //   StatesTax— AsChargedByTheSupplier. The record substantiates the input tax credit we claim, so it must
            //              state the tax; the tax is HIS charge, so it is captioned as his and never as ours.
            ["purchase/item"] =
                new(DocumentRole.Recorded, "PURCHASE RECORD", "Purchase Record", true,
                    TaxParticulars.AsChargedByTheSupplier),
            // The §10 contradiction: §31(3)(c) makes his document a bill of supply unconditionally while §10(4) says
            // the tax that IS in the GL may not be on it. Neither document may be issued, so none is named.
            ["sales/item/section-10-contradiction"] =
                new(DocumentRole.NoStatutoryDocument, "", "", false, TaxParticulars.AsChargedByUs),
        };

    /// <summary>
    /// <b>T12 — the classification is asserted as an ANSWER, per shape, against the statute.</b>
    ///
    /// <para>Written this way deliberately. The obvious alternative — assert that the classifier agrees with
    /// <c>IsTaxInvoice</c> / <c>IsBillOfSupply</c> — would be a tautology dressed as a test: it restates the
    /// implementation, and it stays green under any change that moves both sides together. This repository has
    /// already shipped one matrix test whose comparisons could not fail (W0-9 finding #1), so the rule here is that
    /// the expectation is a statutory answer written down in <see cref="StatutoryExpectation"/> and the classifier
    /// has to reproduce it.</para>
    ///
    /// <para>Every consumer is then required to match the SAME record, so the drill badge, the projected DTO and the
    /// renderer choice cannot be right individually and wrong together.</para>
    /// </summary>
    [Fact]
    public void The_classification_states_the_statutory_answer_for_every_shipped_shape()
    {
        foreach (var s in EveryShippedShape())
        {
            var expected = StatutoryExpectation[s.Name];
            var doc = GstReportSupport.ClassifyPrintedDocument(s.Company, s.Voucher);

            Assert.True(expected.Role == doc.Role, $"Role wrong for {s.Name}");
            Assert.True(expected.Title == doc.Title, $"Title wrong for {s.Name}");
            Assert.True(expected.ScreenLabel == doc.ScreenLabel, $"ScreenLabel wrong for {s.Name}");
            Assert.True(expected.RendersItemDetail == doc.RendersItemDetail,
                $"RendersItemDetail wrong for {s.Name}");
            Assert.True(expected.StatesTax == doc.StatesTax, $"StatesTax wrong for {s.Name}");

            // …and the three consumers read that record rather than re-deriving it.
            var detail = new VoucherDetailViewModel(s.Company, s.Voucher);
            Assert.True(expected.ScreenLabel == detail.DocumentLabel, $"DocumentLabel wrong for {s.Name}");
            Assert.True(
                (expected.RendersItemDetail
                    ? PrintPreviewViewModel.PrintKind.Invoice
                    : PrintPreviewViewModel.PrintKind.Voucher) == detail.BuildPrintPreview().Kind,
                $"PrintKind wrong for {s.Name}");
            if (expected.RendersItemDetail)
            {
                var data = VoucherPrintProjector.ProjectInvoice(s.Company, s.Voucher);
                Assert.True(expected.Title == data.DocumentTitle, $"DocumentTitle wrong for {s.Name}");
                Assert.True((expected.StatesTax == TaxParticulars.None) == data.IsBillOfSupply,
                    $"IsBillOfSupply wrong for {s.Name}");
            }
        }
    }

    /// <summary>
    /// <b>T12's anti-vacuity half.</b> A matrix that produces one answer everywhere proves nothing about the other,
    /// so each axis is required to have taken both of its values somewhere — and the two values S1 must NOT be able
    /// to produce are required to be absent.
    ///
    /// <para><b>🔴 S1's contract was "these two values are UNREACHABLE"; slice S2 is the slice that says so and
    /// flips it.</b> S1 asserted <see cref="DocumentRole.Recorded"/> and <see cref="PartyOrientation.WeAreRecipient"/>
    /// appeared nowhere, precisely so that a refactor which may move no bytes could not create a document class by
    /// accident. S2 creates exactly one — the recipient-side record of RQ-11a — so the assertion is INVERTED here
    /// rather than deleted: both values must now appear, and the matrix must still produce every OTHER answer it
    /// produced before. Deleting it instead would have removed the only thing watching that axis.</para>
    /// </summary>
    [Fact]
    public void The_classification_matrix_is_non_vacuous_and_reaches_no_new_document_class()
    {
        var roles = new HashSet<DocumentRole>();
        var titles = new HashSet<string>();
        var heads = new HashSet<PartyOrientation>();
        var rendersItemDetail = new HashSet<bool>();
        var statesTax = new HashSet<TaxParticulars>();

        foreach (var s in EveryShippedShape())
        {
            var doc = GstReportSupport.ClassifyPrintedDocument(s.Company, s.Voucher);
            roles.Add(doc.Role);
            titles.Add(doc.Title);
            heads.Add(doc.Heads);
            rendersItemDetail.Add(doc.RendersItemDetail);
            statesTax.Add(doc.StatesTax);
        }

        // Non-vacuity: every axis the seam claims to separate really did take both values.
        Assert.Contains(DocumentRole.Issued, roles);
        Assert.Contains(DocumentRole.NoStatutoryDocument, roles);
        Assert.Contains(DocumentRole.Recorded, roles);
        Assert.Contains(GstReportSupport.TaxInvoiceTitle, titles);
        Assert.Contains(GstReportSupport.BillOfSupplyTitle, titles);
        Assert.Contains(GstReportSupport.PurchaseRecordTitle, titles);
        Assert.Contains(string.Empty, titles);
        Assert.Equal(2, rendersItemDetail.Count);

        // All THREE tax answers are reached, which is the whole reason S2 had to widen the axis: "states no tax"
        // (a bill of supply), "states OUR tax" (a tax invoice) and "states the SUPPLIER's tax" (a record) are three
        // different claims, and the two-valued boolean S1 shipped could not tell the last two apart.
        Assert.Equal(3, statesTax.Count);

        // S2's contract, the inversion of S1's: the recipient orientation is now REACHED, and the supplier one is
        // still reached beside it — a matrix that produced only the new answer would have broken every outward
        // document while passing this test.
        Assert.Contains(PartyOrientation.WeAreRecipient, heads);
        Assert.Contains(PartyOrientation.WeAreSupplier, heads);
    }

    /// <summary>
    /// <b>The permanent statutory claim about a PURCHASE — the half of the census row that is right, isolated so it
    /// survives the slice that fixes the half that is wrong.</b>
    ///
    /// <para>CGST Act §31(1) attaches the duty to "a registered person <b>supplying</b> goods"; §31(2) says the same
    /// of services. On an inward supply we supply nothing, so we are entitled to issue no document of our own — the
    /// supplier issues his tax invoice and we record it. The census's implied fix (flip the Sales gate in
    /// <c>IsTaxInvoice</c>) would have titled the supplier's document OUR tax invoice, and — because
    /// <c>IsBillOfSupply</c>'s exempt limb gates on that predicate and feeds the NIC e-Way <c>docType</c> — would
    /// also have titled a wholly-exempt purchase "BILL OF SUPPLY", a document Rule 49 says is "issued by the
    /// supplier", while changing what we file with the portal.</para>
    ///
    /// <para>So this asserts the OUTCOME, not the mechanism: whatever a purchase prints, it is never a document we
    /// claim to have issued, and it never bears either outward title. Slice S2 will make a purchase render with item
    /// detail under the record title; this test must stay green through that, and go red the moment anyone reaches
    /// for the naive fix.</para>
    /// </summary>
    [Fact]
    public void A_purchase_is_never_a_document_we_are_entitled_to_issue()
    {
        var f = Build(GstRegistrationType.Regular);
        var v = PurchaseItem(f, 2_17_483.91m, 1);

        var doc = GstReportSupport.ClassifyPrintedDocument(f.Company, v);
        Assert.NotEqual(DocumentRole.Issued, doc.Role);
        Assert.NotEqual(GstReportSupport.TaxInvoiceTitle, doc.Title);
        Assert.NotEqual(GstReportSupport.BillOfSupplyTitle, doc.Title);
        Assert.False(doc.StatesOurDeclarationAndSignature);

        // …and the entitlement predicates themselves are unmoved, which is what keeps the e-Way docType unmoved.
        Assert.False(GstReportSupport.IsTaxInvoice(f.Company, v));
        Assert.False(GstReportSupport.IsBillOfSupply(f.Company, v));
    }
}
