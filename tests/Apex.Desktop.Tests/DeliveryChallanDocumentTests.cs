using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Io;
using Apex.Ledger.Services;
using Domain = Apex.Ledger.Domain;

namespace Apex.Desktop.Tests;

/// <summary>
/// 🔴 <b>CENSUS 12.7's THIRD DOCUMENT — THE DELIVERY CHALLAN — ASSERTED ON THE EMITTED PDF BYTES.</b>
///
/// <para>Every assertion in this file reads the Latin-1 bytes <see cref="ReportPdf"/> actually emits, never a
/// <see cref="PrintReport"/> field this test just watched the projector set. That is deliberate and it is this
/// project's own scar: a sibling branch was withheld two waves ago because its figures did not survive to the
/// PDF — at fourteen columns every figure drew as <c>37,000....</c> with the In/Out suffix clipped, so an
/// outflow was typographically identical to an inflow. A model is not a document.</para>
///
/// <para><b>GROUNDING (R7, ruling 14 — the vendor's own published documentation, opened BY CONTENT
/// 2026-10-23).</b> <c>help.tallysolutions.com/tally-prime/sales-process/faq-sales/</c>: <i>"A delivery note is
/// an inventory document/challan sent with the goods to be delivered to the customer."</i>
/// <c>help.tallysolutions.com/tally-prime/sales-process/sales-order-tally/</c>: the recording steps name the
/// <i>Party A/c name</i>, a <i>Tracking No.</i>, an <i>Order No.</i>, a <i>Quantity</i> and a <i>Rate</i>, and
/// state <i>"A Delivery Note with Tracking No. marked as Not Applicable is an inventory document affecting only
/// the inventory values"</i> / <i>"This will not affect the accounts."</i> 🔴 <b>The LAYOUT is OURS</b> — four
/// TallyPrime pages were opened and none states one (IV-159). The tests below assert that OUR layout carries
/// the vendor-named FIELDS; they do not claim the shape is cloned.</para>
/// </summary>
public sealed class DeliveryChallanDocumentTests
{
    private static readonly DateOnly Fy = new(2025, 4, 1);
    private static readonly DateOnly Despatch = new(2025, 6, 12);

    private static string AsLatin1(byte[] bytes) => Encoding.Latin1.GetString(bytes);

    private sealed record Kit(Company C, Guid GodownId, Guid NosId, Guid GroupId);

    private static Kit Seed(string companyName = "Acme Retail")
    {
        var c = CompanyFactory.CreateSeeded(companyName, Fy);
        var masters = new InventoryService(c);
        var grp = masters.CreateStockGroup("Goods");
        var nos = masters.CreateSimpleUnit("Nos", "Numbers");
        return new Kit(c, c.MainLocation!.Id, nos.Id, grp.Id);
    }

    private static Domain.Ledger Party(Kit k, string name)
    {
        var l = new Domain.Ledger(
            Guid.NewGuid(), name, k.C.FindGroupByName("Sundry Debtors")!.Id,
            Money.Zero, openingIsDebit: true);
        k.C.AddLedger(l);
        return l;
    }

    private static Guid Item(Kit k, string name, Guid? unitId = null) =>
        new InventoryService(k.C).CreateStockItem(name, k.GroupId, unitId ?? k.NosId).Id;

    /// <summary>Posts a pure-stock voucher of <paramref name="baseType"/> straight onto the aggregate.</summary>
    private static InventoryVoucher Post(
        Kit k, VoucherBaseType baseType, Guid? partyId, params InventoryAllocation[] lines)
    {
        var type = k.C.VoucherTypes.First(t => t.BaseType == baseType);
        var v = new InventoryVoucher(Guid.NewGuid(), type.Id, Despatch, lines, number: 7, partyId: partyId);
        k.C.AddInventoryVoucher(v);
        return v;
    }

    private static InventoryAllocation Outward(
        Guid itemId, Guid godownId, decimal qty, decimal rate,
        string? tracking = null, Guid? unitId = null, string? batch = null) =>
        new(itemId, godownId, qty, StockDirection.Outward, Money.FromRupees(rate), batch, unitId, tracking);

    private static string RenderChallan(Kit k, InventoryVoucher v)
    {
        var projection = DeliveryChallanPrintProjector.Project(k.C, v);
        Assert.Null(projection.Refusal);
        Assert.NotNull(projection.Document);
        return AsLatin1(ReportPdf.Render(projection.Document!, new PageConfig()));
    }

    // ============================================================ (1) the document exists and carries its fields

    /// <summary>
    /// 🔴 <b>THE CLAUSE THE ROW WAS PARTIAL OVER.</b> A posted Delivery Note produces a challan, and the
    /// vendor-named fields — the consignee, the goods, the Tracking No., the Quantity and the Rate — all reach
    /// the emitted page, together with the Amount our layout derives from Quantity × Rate.
    ///
    /// <para><b>The consignee name is deliberately LONG.</b> Before <c>PrintRow.FullWidth</c> existed it was
    /// carried in cell 0 of a six-column grid and <c>ReportPdf</c> clipped it to that column's ~190pt, so the
    /// page named <i>"To: Bharat Heavy Electricals &amp; Comp..."</i> — a consignee this document does not
    /// actually name. <b>Mutation: setting <c>FullWidth = false</c> on the addressee row (or deleting the
    /// full-width arm in <c>ReportPdf.DrawRowCells</c>) fails this test at the consignee assertion.</b></para>
    /// </summary>
    [Fact]
    public void A_delivery_note_prints_a_challan_naming_its_consignee_its_goods_and_the_vendor_named_fields()
    {
        var k = Seed();
        var party = Party(k, "Bharat Heavy Electricals & Components Private Limited");
        var widget = Item(k, "Galvanised Widget");
        var v = Post(k, VoucherBaseType.DeliveryNote, party.Id,
            Outward(widget, k.GodownId, 14m, 250m, tracking: "TRK-4417"));

        var document = DeliveryChallanPrintProjector.Project(k.C, v).Document!;
        var goods = document.Rows.Single(r => r.Cells.Count > 2 && r.Cells[2] == "TRK-4417");
        Assert.Equal("Galvanised Widget", goods.Cells[0]);
        Assert.Equal("14", goods.Cells[3]);                            // vendor field: Quantity
        Assert.Equal("250.00", goods.Cells[4]);                        // vendor field: Rate
        Assert.Equal("3,500.00", goods.Cells[5]);                      // ours: 14 x 250

        string s = RenderChallan(k, v);

        Assert.Contains("Delivery Challan", s, StringComparison.Ordinal);
        // The consignee, IN FULL — not a prefix of it.
        Assert.Contains("To: Bharat Heavy Electricals & Components Private Limited", s, StringComparison.Ordinal);
        Assert.Contains("Galvanised Widget", s, StringComparison.Ordinal);
        Assert.Contains("TRK-4417", s, StringComparison.Ordinal);      // vendor field: Tracking No.
        Assert.Contains("Tracking No.", s, StringComparison.Ordinal);  // ... and its caption
        Assert.Contains("250.00", s, StringComparison.Ordinal);
        Assert.Contains("3,500.00", s, StringComparison.Ordinal);
        // It is NOT a tax invoice and must never head itself as one (T0-11).
        Assert.DoesNotContain("TAX INVOICE", s, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 🔴 <b>A MONEY FIGURE MUST SURVIVE WHOLE, NOT AS A SMALLER TRUE-LOOKING NUMBER.</b> Seven figures across
    /// six columns: <c>99,99,999.00</c> drawn as <c>99,99,9...</c> reads as a real, smaller amount and this
    /// document goes to the party who receives the goods. Asserted on the bytes, with the ellipsis excluded by
    /// name so a clipped draw cannot pass by containing the prefix.
    /// </summary>
    [Fact]
    public void Every_figure_on_the_challan_is_drawn_whole_and_never_ellipsised()
    {
        var k = Seed();
        var party = Party(k, "Northern Distribution Partners LLP");
        var widget = Item(k, "Compressor Mk II");
        var v = Post(k, VoucherBaseType.DeliveryNote, party.Id,
            Outward(widget, k.GodownId, 9m, 1_111_111m, tracking: "TRK-9001"));

        string s = RenderChallan(k, v);

        Assert.Contains("11,11,111.00", s, StringComparison.Ordinal);    // the rate, whole
        Assert.Contains("99,99,999.00", s, StringComparison.Ordinal);    // 9 x 11,11,111 — the amount, whole
        Assert.DoesNotContain("11,11,1...", s, StringComparison.Ordinal);
        Assert.DoesNotContain("99,99,9...", s, StringComparison.Ordinal);
    }

    // ============================================================ (2) it carries NOBODY ELSE

    /// <summary>
    /// 🔴 <b>A CHALLAN IS HANDED TO A THIRD PARTY, SO IT MUST CARRY THAT PARTY AND NOBODY ELSE.</b> This
    /// product has already shipped a Day Book that SHARED ITSELF to a third party in place of one invoice. Two
    /// Delivery Notes are posted to two different consignees with two different goods; the challan rendered for
    /// one must not contain one character of the other's name, goods, tracking number or figures.
    ///
    /// <para><b>Mutation:</b> making <c>DeliveryChallanPrintProjector.Project</c> iterate
    /// <c>company.InventoryVouchers</c> instead of the one voucher's own allocations reddens every
    /// <c>DoesNotContain</c> here.</para>
    /// </summary>
    [Fact]
    public void The_challan_contains_the_party_it_names_and_nobody_elses_data()
    {
        var k = Seed();
        var ours = Party(k, "Coastal Marine Supplies");
        var theirs = Party(k, "Himalayan Textiles Cooperative");
        var ourItem = Item(k, "Anchor Chain 12mm");
        var theirItem = Item(k, "Merino Yarn Cone");

        var mine = Post(k, VoucherBaseType.DeliveryNote, ours.Id,
            Outward(ourItem, k.GodownId, 4m, 1_250m, tracking: "TRK-OURS"));
        Post(k, VoucherBaseType.DeliveryNote, theirs.Id,
            Outward(theirItem, k.GodownId, 88m, 777m, tracking: "TRK-THEIRS"));

        string s = RenderChallan(k, mine);

        Assert.Contains("Coastal Marine Supplies", s, StringComparison.Ordinal);
        Assert.Contains("Anchor Chain 12mm", s, StringComparison.Ordinal);
        Assert.DoesNotContain("Himalayan Textiles Cooperative", s, StringComparison.Ordinal);
        Assert.DoesNotContain("Merino Yarn Cone", s, StringComparison.Ordinal);
        Assert.DoesNotContain("TRK-THEIRS", s, StringComparison.Ordinal);
        Assert.DoesNotContain("777.00", s, StringComparison.Ordinal);
    }

    // ============================================================ (3) the non-ASCII fold, on the bytes

    /// <summary>
    /// 🔴 <b>THE NON-ASCII FOLD, PROVED ON THE PAGE AND NOT ROUTED AROUND.</b> Characters above U+007E used to
    /// be replaced by a hyphen wholesale, so a consignee named <i>Müller &amp; Söhne</i> was printed
    /// <i>M-ller &amp; S-hne</i> on the document handed to them. The fold now ASKS
    /// <see cref="PdfWriter.CanRender"/>, and this challan goes through the same shared
    /// <c>ReportPrintProjector.Ascii</c> as every other printed document rather than a local copy of the rule.
    ///
    /// <para><b>Both halves are asserted, and the second is the honest one.</b> A WinAnsi-representable name
    /// reaches paper AS ITSELF. The RUPEE SIGN does not have a glyph in a standard-14 face and this document
    /// embeds no font (census 12.8), so it reaches paper as the deterministic <c>"Rs."</c> the shared fold
    /// defines — <b>not</b> dropped, and <b>not</b> the writer's <c>'?'</c> fallback. Claiming the glyph itself
    /// survives would be false, so this test pins what actually happens.</para>
    ///
    /// <para><b>Mutation:</b> changing the fold's non-ASCII arm back to an unconditional <c>'-'</c> reddens the
    /// umlaut assertions; deleting the <c>'₹'</c> case reddens the <c>"Rs."</c> assertion.</para>
    /// </summary>
    [Fact]
    public void A_non_ascii_consignee_name_and_the_rupee_sign_reach_the_page_through_the_shared_fold()
    {
        var k = Seed();
        var party = Party(k, "Müller & Söhne Fördertechnik");
        var item = Item(k, "Präzision ₹ Spindel");
        var v = Post(k, VoucherBaseType.DeliveryNote, party.Id,
            Outward(item, k.GodownId, 3m, 500m, tracking: "TRK-DE-1"));

        string s = RenderChallan(k, v);

        // WinAnsi-representable: drawn as itself, byte for byte.
        Assert.Contains("To: Müller & Söhne Fördertechnik", s, StringComparison.Ordinal);
        Assert.DoesNotContain("M-ller", s, StringComparison.Ordinal);
        Assert.DoesNotContain("S-hne", s, StringComparison.Ordinal);
        Assert.DoesNotContain("F-rdertechnik", s, StringComparison.Ordinal);
        // The rupee sign has no standard-14 glyph: it folds to "Rs.", never to '-' and never to '?'.
        Assert.Contains("Präzision Rs. Spindel", s, StringComparison.Ordinal);
        Assert.DoesNotContain("Pr-zision", s, StringComparison.Ordinal);
        Assert.DoesNotContain("Präzision ? Spindel", s, StringComparison.Ordinal);
        Assert.DoesNotContain("Präzision - Spindel", s, StringComparison.Ordinal);
        // And the seam the fold consults is the writer's own, not a local guess.
        Assert.True(PdfWriter.CanRender('ü'));
        Assert.True(PdfWriter.CanRender('ö'));
        Assert.False(PdfWriter.CanRender('₹'));
    }

    // ============================================================ (4) who gets a document, and who gets a reason

    public static TheoryData<VoucherBaseType> EveryPureStockKind => new()
    {
        VoucherBaseType.StockJournal,
        VoucherBaseType.PhysicalStock,
        VoucherBaseType.SalesOrder,
        VoucherBaseType.PurchaseOrder,
        VoucherBaseType.DeliveryNote,
        VoucherBaseType.ReceiptNote,
        VoucherBaseType.RejectionOut,
        VoucherBaseType.RejectionIn,
    };

    /// <summary>
    /// 🔴 <b>EXACTLY ONE OF THE TWO HALVES IS NON-NULL, FOR EVERY KIND.</b> "Neither" is the state in which the
    /// shell opens no preview AND says nothing — which is indistinguishable from a dead keystroke — and "both"
    /// is a document shipped alongside the reason it should not exist. Only a Delivery Note gets a document:
    /// no TallyPrime page we opened gives any other pure-stock kind a printed one, and the Tally.ERP 9 pages
    /// that describe a job-work material-out challan are not ground truth here.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryPureStockKind))]
    public void Every_pure_stock_kind_yields_either_a_document_or_a_reason_and_only_a_delivery_note_a_document(
        VoucherBaseType baseType)
    {
        var k = Seed();
        var party = Party(k, "Riverside Trading Co");
        var item = Item(k, "Bolt M8");
        var lines = new[] { Outward(item, k.GodownId, 5m, 20m) };
        var v = Post(k, baseType, party.Id, lines);

        var p = DeliveryChallanPrintProjector.Project(k.C, v);

        Assert.True(
            (p.Document is null) != (p.Refusal is null),
            $"{baseType}: exactly one of Document/Refusal must be non-null, got "
            + $"Document={(p.Document is null ? "null" : "set")} Refusal={p.Refusal ?? "null"}");
        Assert.Equal(baseType == VoucherBaseType.DeliveryNote, p.Document is not null);
        if (p.Document is null)
            Assert.Equal(DeliveryChallanPrintProjector.NotAChallan, p.Refusal);
    }

    /// <summary>
    /// A Delivery Note may be posted with no party — our entry screen offers "(none)". A challan is handed to a
    /// consignee, so one that names nobody is refused rather than produced headed <c>"To: "</c> with nothing
    /// after it. Same direction <c>MultiAccountPrintProjector.MayProduce</c> takes for this row's other two
    /// documents.
    /// </summary>
    [Fact]
    public void A_delivery_note_naming_no_party_is_refused_rather_than_addressed_to_nobody()
    {
        var k = Seed();
        var item = Item(k, "Bolt M8");
        var v = Post(k, VoucherBaseType.DeliveryNote, partyId: null,
            Outward(item, k.GodownId, 5m, 20m));

        var p = DeliveryChallanPrintProjector.Project(k.C, v);

        Assert.Null(p.Document);
        Assert.Equal(DeliveryChallanPrintProjector.NoParty, p.Refusal);
    }

    // ============================================================ (5) the figures are the drilled pane's figures

    /// <summary>
    /// 🔴 <b>THE CHALLAN AND THE PANE THE OPERATOR PRINTED IT FROM MUST SHOW THE SAME FIGURES — LOCKED, NOT
    /// MERELY WRITTEN ALIKE.</b> The line is stated in a COMPOUND unit (1 Dozen = 12 Nos), which is the shape
    /// that separates a correct implementation from the two wrong ones: converting only the quantity turns
    /// "2 Doz @ Rs.10" into Rs.240, converting only the rate into Rs.1.67, and converting both (or neither)
    /// into the Rs.20 the books mean. The challan's own Quantity / Rate / Amount cells are compared against
    /// <see cref="InventoryVoucherDetailViewModel"/>'s, cell for cell.
    ///
    /// <para><b>Mutation:</b> deleting the unit conversion in
    /// <c>DeliveryChallanPrintProjector.InBaseUnit</c> reddens this at the Amount comparison (Rs.240 vs
    /// Rs.20).</para>
    /// </summary>
    [Fact]
    public void The_challans_figures_are_the_drilled_panes_figures_even_in_a_compound_unit()
    {
        var k = Seed();
        var masters = new InventoryService(k.C);
        // 1 Dozen = 12 Nos: the FIRST unit is the larger one, the TAIL the smaller, and they must differ.
        var dz = masters.CreateSimpleUnit("Dz", "Dozen");
        var dozen = masters.CreateCompoundUnit("Doz", "Dozen of 12 Nos", dz.Id, k.NosId, 12);
        var party = Party(k, "Riverside Trading Co");
        var item = Item(k, "Candle");
        // 2 Dozen at Rs.120 per Dozen = 24 Nos at Rs.10 per Nos = Rs.240 either way round.
        var v = Post(k, VoucherBaseType.DeliveryNote, party.Id,
            Outward(item, k.GodownId, 2m, 120m, tracking: "TRK-D", unitId: dozen.Id));

        var document = DeliveryChallanPrintProjector.Project(k.C, v).Document!;
        var pane = new InventoryVoucherDetailViewModel(k.C, v);
        var paneRow = pane.Rows.Single();

        // The one goods row of the challan: the row whose Tracking No. cell is the line's.
        var goods = document.Rows.Single(r => r.Cells.Count > 2 && r.Cells[2] == "TRK-D");

        Assert.Equal(paneRow.Col3, goods.Cells[3]);   // Quantity
        Assert.Equal(paneRow.Col4, goods.Cells[4]);   // Rate
        Assert.Equal(paneRow.Col5, goods.Cells[5]);   // Amount
        // ... and the arithmetic itself, so a test that merely agreed with a wrong pane could not pass.
        Assert.Equal("24", goods.Cells[3]);
        Assert.Equal("10.00", goods.Cells[4]);
        Assert.Equal("240.00", goods.Cells[5]);

        // And the figures reach the page.
        string s = AsLatin1(ReportPdf.Render(document, new PageConfig()));
        Assert.Contains("240.00", s, StringComparison.Ordinal);
    }

    /// <summary>
    /// The quantity column is deliberately NOT totalled — lines may be stated in different stock units and a
    /// sum of Nos and Kg is a number that means nothing. The VALUE column is totalled, paisa-exact across
    /// lines. Asserted on the emitted page so the total is proved to have been drawn, not merely computed.
    /// </summary>
    [Fact]
    public void The_challan_totals_the_value_across_lines_and_never_sums_mixed_quantities()
    {
        var k = Seed();
        var masters = new InventoryService(k.C);
        var kg = masters.CreateSimpleUnit("Kg", "Kilogram", decimalPlaces: 3);
        var party = Party(k, "Riverside Trading Co");
        var bolt = Item(k, "Bolt M8");
        var sand = Item(k, "Foundry Sand", kg.Id);

        var v = Post(k, VoucherBaseType.DeliveryNote, party.Id,
            Outward(bolt, k.GodownId, 10m, 15m, tracking: "T1"),
            Outward(sand, k.GodownId, 25m, 8m, tracking: "T2", unitId: kg.Id));

        var document = DeliveryChallanPrintProjector.Project(k.C, v).Document!;
        var total = document.Rows.Single(r => r.IsTotal);

        Assert.Equal("Total", total.Cells[0]);
        Assert.Equal(string.Empty, total.Cells[3]);          // no mixed-unit quantity total
        Assert.Equal("350.00", total.Cells[5]);              // 150.00 + 200.00

        string s = AsLatin1(ReportPdf.Render(document, new PageConfig()));
        Assert.Contains("350.00", s, StringComparison.Ordinal);
    }

    // ============================================================ (6) the row's other two documents

    /// <summary>
    /// 🔴 <b>THE SAME CLIPPING DEFECT WAS ALREADY SHIPPING ON THIS ROW'S OTHER TWO DOCUMENTS, AND IT IS FIXED
    /// HERE RATHER THAN LEFT BECAUSE IT IS OLD.</b> The reminder letter and the confirmation of accounts are
    /// letters rendered through the tabular <see cref="ReportPdf"/>, and their addressee and their sentences
    /// sat in cell 0 of a four-column grid whose first column is <i>Reference</i> — so the line naming the
    /// counterparty, and the sentence asking them to pay, were drawn with an ellipsis on the document posted to
    /// them. Both now use <c>PrintRow.FullWidth</c>.
    ///
    /// <para><b>Mutation:</b> reverting either letter's addressee row to a plain <c>PrintRow.Header</c> reddens
    /// the matching assertion below.</para>
    /// </summary>
    [Theory]
    [InlineData(MultiAccountDocumentKind.ReminderLetter)]
    [InlineData(MultiAccountDocumentKind.ConfirmationOfAccounts)]
    public void The_reminder_letter_and_the_confirmation_name_their_addressee_in_full_too(
        MultiAccountDocumentKind kind)
    {
        const string Name = "Bharat Heavy Electricals & Components Private Limited";
        var k = Seed();
        var party = Party(k, Name);

        var docs = MultiAccountPrintProjector.Project(
            k.C, new[] { party.Id }, kind, Fy, new DateOnly(2026, 3, 31));
        string s = AsLatin1(ReportPdf.Render(docs, new PageConfig()));

        Assert.Contains("To: " + Name, s, StringComparison.Ordinal);
        // The letter's own sentence reaches the page whole as well.
        Assert.Contains(
            kind == MultiAccountDocumentKind.ReminderLetter
                ? "Our records show the following amounts still outstanding as at 31-03-2026."
                : "Our books show the following balance on your account as at 31-03-2026.",
            s, StringComparison.Ordinal);
    }
}
