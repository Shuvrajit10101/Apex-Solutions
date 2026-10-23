using System;
using System.Linq;
using System.Text;
using Apex.Desktop.Services;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Io;
using Apex.Ledger.Services;
using Xunit;
using Domain = Apex.Ledger.Domain;

namespace Apex.Desktop.Tests;

/// <summary>
/// 🔴 <b>A MASTER NAME THE PDF FONT CAN RENDER MUST REACH THE PRINTED PAGE AS ITSELF — ASSERTED ON THE EMITTED
/// PDF BYTES. Census 12.1 clause (d), the "ASCII fold".</b>
///
/// <para><b>The defect, measured on <c>main</c> at 9ddeec5 rather than predicted.</b>
/// <c>ReportPrintProjector.Ascii</c> ended with <c>if (c &lt;= 126) sb.Append(c); else sb.Append('-');</c>, so
/// <b>every</b> character above U+007E that its eight-case editorial switch did not name was replaced by a
/// hyphen. Its own summary gave the reason — *"so the ASCII-only PDF writer never emits a '?'"* — and
/// <b>that premise was false</b>: <see cref="PdfWriter"/> encodes WinAnsi (CP1252), not ASCII. Both fonts are
/// declared <c>/Encoding /WinAnsiEncoding</c>, <c>PdfWriter.ToWinAnsi</c> maps the whole Latin-1 range and the
/// CP1252 0x80–0x9F block, and the content stream is serialised with <c>Encoding.Latin1</c> precisely so those
/// bytes survive. So the fold was destroying roughly 120 characters the writer renders correctly, for a reason
/// that had stopped being true.</para>
///
/// <para><b>Why that is a fidelity defect and not a cosmetic one.</b> The vendor documents regional-language
/// masters as a product capability: help.tallysolutions.com, *"Use TallyPrime in your preferred language"* —
/// "you can enter data in regional languages", "You can now create ledgers and stock items in your preferred
/// language", and "you can now view and print all your transactions and reports in your preferred language".
/// A party ledger named <c>Müller &amp; Söhne</c> printed as <c>M-ller &amp; S-hne</c> on its own statement of
/// account, on its tax invoices, and on its vouchers.</para>
///
/// <para>🔴 <b>A CORRECTION KEPT IN PLACE RATHER THAN TIDIED AWAY, because it is the kind of mistake this file
/// exists to catch.</b> An earlier draft of this note claimed the same name "printed correctly on an invoice,
/// because <see cref="InvoicePdf"/> never calls this fold". <see cref="InvoicePdf"/> genuinely contains zero
/// <c>Ascii</c> call sites — and the conclusion was still wrong, because the fold is applied UPSTREAM by the
/// projector that builds the print model (<c>VoucherPrintProjector.CounterpartyBlock</c> and
/// <c>VoucherPrintProjector.SellerBlock</c>). Grepping the renderer answered a different question than the one
/// asked. The invoice path therefore gets its own byte-level assertion below rather than an inference.</para>
///
/// <para><b>Reach.</b> <c>ReportPrintProjector.Ascii</c> is the chokepoint for the report, statement and voucher
/// print paths — <see cref="ReportPrintProjector"/> itself (titles, subtitles, column captions and every banded
/// and generic cell), <see cref="MultiAccountPrintProjector"/> (11 call sites: ledger-account, confirmation and
/// reminder sheets) and <see cref="VoucherPrintProjector"/> (party name, ledger names, narration, voucher type,
/// stock-item descriptions).</para>
///
/// <para>🔴 <b>IT IS NOT "ONE FOLD, EVERY PRINTED DOCUMENT", WHICH IS WHAT THIS FILE FIRST CLAIMED, AND THE WAY
/// THAT CLAIM WAS WRONG IS THE SAME WAY THE INVOICE CLAIM ABOVE WAS WRONG — IT WAS REASONED FROM THE THREE
/// PROJECTORS IT HAD ALREADY OPENED.</b> <c>ChequePrintProjector</c> sits in the same folder and has <b>zero</b>
/// <c>Ascii</c> call sites: <c>ChequePdf</c> inks <c>ChequePrintData.PayeeName</c> verbatim, deliberately, so the
/// ER-11 brand scrub cannot rewrite a payee's legal name on a negotiable instrument. The two TDS/TCS certificate
/// renderers bypass it too — <c>Form16APdf</c> inks <c>cert.Deductee.Name</c> and <c>Form27DPdf</c> inks
/// <c>cert.Collectee.Name</c> straight through <c>CertificatePdfSupport.KeyVal</c> — and so does the POS receipt,
/// whose <c>StoreName</c> and <c>Party</c> <c>PosBillingViewModel</c> fills straight from the book. On those four
/// unrenderable character reaches <see cref="PdfWriter"/> and is drawn as its <c>'?'</c> fallback: the wave-47
/// reviewer rendered a cheque to a Devanagari payee and read <c>?????</c> off the emitted bytes. That is
/// <b>pre-existing and unchanged by this fix</b> — it is recorded so the next reader does not inherit the
/// universal.</para>
///
/// <para>🔴 <b>WHAT IS DELIBERATELY NOT CLOSED, AND IT IS THE EXPENSIVE HALF.</b> A character WinAnsi cannot
/// represent still folds, because the writer carries the standard-14 faces with <b>no font embedding</b> — there
/// is no font program in the document and therefore no glyph for ₹ U+20B9 or for any Indic script. That residual
/// is census 12.8 and it is pinned, not quietly widened, by
/// <see cref="A_script_the_pdf_font_cannot_render_still_folds_and_never_reaches_the_page_as_a_question_mark"/>
/// and <see cref="The_rupee_sign_still_folds_to_Rs_because_WinAnsi_has_no_glyph_for_it"/>. Closing it needs an
/// embedded Unicode font, which needs a font program committed to the repository — a scope and licensing call,
/// and <b>not</b> a system-font lookup, which would break the writer's documented cross-platform byte-identity
/// (ER-13). The seam for it is <see cref="PdfWriter.CanRender"/>: widen that one predicate when a font is
/// embedded and every print path above widens with it.</para>
/// </summary>
public sealed class PrintedNonAsciiNameFidelityTests
{
    private static readonly DateOnly FyStart = new(2025, 4, 1);

    /// <summary>A real-shaped party name whose non-ASCII characters are all WinAnsi-representable:
    /// ü = U+00FC → byte 0xFC, ö = U+00F6 → byte 0xF6. No parenthesis and no backslash, so the PDF
    /// literal-string escaping cannot confuse the byte search (the trap that once manufactured a clipped-caption
    /// defect that did not exist).</summary>
    /// <para>Written with escapes rather than literals on purpose: every assertion in this file is about which
    /// BYTE reached the page, so the test data must not itself depend on how this source file was decoded.</para>
    private const string WinAnsiParty = "Müller & Söhne Handels";

    /// <summary>What <c>main</c> actually printed for <see cref="WinAnsiParty"/>.</summary>
    private const string MangledOnMain = "M-ller & S-hne Handels";

    private static (Company C, Guid PartyId) Books(string partyName)
    {
        var c = CompanyFactory.CreateSeeded("Apex Retail", FyStart);
        var party = new Domain.Ledger(
            Guid.NewGuid(), partyName, c.FindGroupByName("Sundry Debtors")!.Id,
            Money.FromRupees(25_000m), openingIsDebit: true);
        c.AddLedger(party);
        return (c, party.Id);
    }

    /// <summary>Renders the party's ledger-account statement through the production projector and renderer and
    /// returns the emitted PDF bytes read back 1:1 as Latin1 — which is exactly the encoding the writer used, so
    /// each char of this string IS one emitted byte.</summary>
    private static string PrintedStatement(string partyName)
    {
        var (c, partyId) = Books(partyName);
        var docs = MultiAccountPrintProjector.Project(
            c, new[] { partyId }, MultiAccountDocumentKind.LedgerAccount, FyStart, new DateOnly(2026, 3, 31));
        return Encoding.Latin1.GetString(ReportPdf.Render(docs, new PageConfig()));
    }

    /// <summary>
    /// 🔴 <b>THE LOCK.</b> The party's own statement of account spells the party's name the way the book holds
    /// it. Asserted on the emitted bytes, and the mangled spelling is asserted ABSENT as well — a test that only
    /// looked for the right name would also pass if the renderer drew both.
    /// </summary>
    [Fact]
    public void A_WinAnsi_representable_party_name_reaches_the_printed_page_as_itself()
    {
        string pdf = PrintedStatement(WinAnsiParty);

        Assert.Contains(WinAnsiParty, pdf, StringComparison.Ordinal);
        Assert.DoesNotContain(MangledOnMain, pdf, StringComparison.Ordinal);
    }

    /// <summary>
    /// The same lock stated at the level of single bytes, so it cannot be satisfied by anything other than the
    /// correct WinAnsi code units actually being in the file: 0xFC for ü and 0xF6 for ö, inside the heading.
    /// </summary>
    [Fact]
    public void The_emitted_bytes_carry_the_WinAnsi_code_units_for_the_accented_characters()
    {
        string pdf = PrintedStatement(WinAnsiParty);

        Assert.Contains("Müller", pdf, StringComparison.Ordinal);   // 'M' 0xFC 'l' 'l' 'e' 'r'
        Assert.Contains("Söhne", pdf, StringComparison.Ordinal);    // 'S' 0xF6 'h' 'n' 'e'
    }

    /// <summary>
    /// 🔴 <b>THE SECOND DOCUMENT, AND THE REASON IT IS HERE RATHER THAN INFERRED.</b> A tax invoice is the
    /// document the counterparty actually keeps, and the fold reaches it through
    /// <c>VoucherPrintProjector.SellerBlock</c> / <c>CounterpartyBlock</c> — not through
    /// <see cref="InvoicePdf"/>, which contains no <c>Ascii</c> call site at all. This renders a posted sale
    /// through the production invoice projector and renderer and reads the party's name off the emitted bytes,
    /// so the claim rests on the artefact rather than on which file a grep happened to cover.
    /// </summary>
    [Fact]
    public void A_tax_invoice_also_names_the_party_as_the_book_holds_it()
    {
        var c = CompanyFactory.CreateSeeded("Apex Retail", FyStart);
        var party = new Domain.Ledger(
            Guid.NewGuid(), WinAnsiParty, c.FindGroupByName("Sundry Debtors")!.Id,
            Money.Zero, openingIsDebit: true);
        c.AddLedger(party);
        var sales = new Domain.Ledger(
            Guid.NewGuid(), "Consultancy Income", c.FindGroupByName("Sales Accounts")!.Id,
            Money.Zero, openingIsDebit: false);
        // The income leg needs a SAC block or the projector sees no stateable service line, prices the
        // document at 0.00 against a 4,720.00 party leg and refuses it. Exempt, not Taxable: a ledger that
        // DECLARES taxability while the voucher posts no tax leg is refused too, and this company has GST off.
        sales.SalesPurchaseGst = new StockItemGstDetails
        {
            HsnSac = "998311",
            Taxability = GstTaxability.Exempt,
            SupplyType = GstSupplyType.Services,
        };
        c.AddLedger(sales);

        // An ACCOUNTING invoice on a GST-less company: the simplest voucher the invoice projector will accept.
        // Without the flag it refuses the document outright (its Grand Total would read 0.00 against a 4,720.00
        // party leg) — a guard of its own, and the reason this test constructs a real invoice rather than a
        // bare sale.
        var salesType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales && t.IsActive);
        var v = new Voucher(Guid.NewGuid(), salesType.Id, FyStart.AddDays(5), new[]
        {
            new EntryLine(party.Id, Money.FromRupees(4_720m), DrCr.Debit),
            new EntryLine(sales.Id, Money.FromRupees(4_720m), DrCr.Credit),
        }, partyId: party.Id, isAccountingInvoice: true);
        new LedgerService(c).Post(v);

        string pdf = Encoding.Latin1.GetString(
            InvoicePdf.Render(VoucherPrintProjector.ProjectInvoice(c, v), new PrintConfig(), new PageConfig()));

        Assert.Contains(WinAnsiParty, pdf, StringComparison.Ordinal);
        Assert.DoesNotContain(MangledOnMain, pdf, StringComparison.Ordinal);
    }

    /// <summary>
    /// 🔴 <b>THE RESIDUAL, PINNED SO IT CANNOT BE QUIETLY WIDENED OR QUIETLY WORSENED.</b> A Devanagari name has
    /// no glyph in a standard-14 face, so it still folds — and <b>on the paths that go through this fold</b> it
    /// must fold to the deterministic hyphen, never reach <see cref="PdfWriter"/> and come out as the writer's
    /// <c>'?'</c> last-resort byte. The ASCII tail of the same name still prints, which is what proves the fold
    /// is per-character and not a blanking.
    ///
    /// <para>🔴 <b>The method name says "the page" and that is narrower than it sounds: it means the statement
    /// page this test renders.</b> It is NOT a claim about every printed document — a cheque and the two TDS/TCS
    /// certificates do not route through this fold at all (see the class note) and DO ink <c>'?'</c> there.</para>
    /// </summary>
    [Fact]
    public void A_script_the_pdf_font_cannot_render_still_folds_and_never_reaches_the_page_as_a_question_mark()
    {
        const string Indic = "कुमार Traders";   // "कुमार Traders"
        string pdf = PrintedStatement(Indic);

        Assert.Contains("Traders", pdf, StringComparison.Ordinal);
        Assert.Contains("----- Traders", pdf, StringComparison.Ordinal);
        Assert.DoesNotContain("?????", pdf, StringComparison.Ordinal);
    }

    /// <summary>
    /// WinAnsi has no rupee sign, so ₹ still folds to "Rs." — the behaviour <c>ReportColumnBands</c> documents
    /// and the one the Unicode CSV/XLSX twin deliberately does NOT share. Pinned here so widening the fold did
    /// not take this with it.
    /// </summary>
    [Fact]
    public void The_rupee_sign_still_folds_to_Rs_because_WinAnsi_has_no_glyph_for_it()
    {
        Assert.Equal("Rs. 1,200.00", ReportPrintProjector.Ascii("₹ 1,200.00"));
        Assert.False(PdfWriter.CanRender('₹'));
    }

    /// <summary>
    /// 🔴 <b>THE AGREEMENT THAT KEEPS THE TWO HALVES FROM DRIFTING, which is the actual cause of the original
    /// defect.</b> The fold decided for itself what the writer could render and was wrong for ~120 characters.
    /// Now it asks, and this sweeps the whole BMP to assert the question and the answer cannot disagree: for
    /// every character, <see cref="PdfWriter.CanRender"/> is true exactly when the byte the writer actually
    /// emits is the character itself rather than its '?' fallback. A future font embedding that widens
    /// <c>CanRender</c> without widening the encoder — which would put '?' on a printed document — or the
    /// reverse, which would resume destroying renderable text, reddens here.
    ///
    /// <para><b>Three characters are excluded with a reason, and are pinned separately by
    /// <see cref="Tab_CR_and_LF_are_structural_escapes_and_are_outside_CanRender_s_question"/> rather than
    /// merely skipped.</b> <c>PdfWriter.EscapeString</c> maps tab, CR and LF to the PDF escapes
    /// <c>\t \r \n</c>, so they "survive" the encoder while being no glyph at all. <c>CanRender</c> answers
    /// "can this be DRAWN", so it is false for them, and the fold never consults it there — its ASCII arm
    /// keeps them unchanged. Excluding them states that boundary; it does not loosen the sweep, which still
    /// covers all 65,533 other code units including every one the fold now delegates.</para>
    /// </summary>
    [Fact]
    public void CanRender_agrees_with_what_the_encoder_actually_emits_across_the_whole_char_range()
    {
        var disagreements = new System.Collections.Generic.List<string>();
        int compared = 0;

        for (int i = 0; i <= 0xFFFF; i++)
        {
            char c = (char)i;
            if (char.IsSurrogate(c)) continue;
            if (c is '\t' or '\r' or '\n') continue;   // structural escapes, not glyphs — pinned separately

            compared++;
            bool claimed = PdfWriter.CanRender(c);

            // Render the character ALONE through the production writer and read the one drawn operand back off
            // the emitted bytes. '?' in, '?' out is the one legitimate '?', so it is excluded from the compare.
            var w = new PdfWriter();
            w.BeginPage(200, 200);
            w.Text(10, 10, c.ToString(), 9);
            string emitted = Encoding.Latin1.GetString(w.Build());
            bool survived = c == '?' || !emitted.Contains("(?) Tj", StringComparison.Ordinal);

            if (claimed != survived)
                disagreements.Add($"U+{i:X4} CanRender={claimed} survived={survived}");
        }

        Assert.True(disagreements.Count == 0,
            "PdfWriter.CanRender and the bytes the writer actually emits disagree for "
            + $"{disagreements.Count} character(s): "
            + string.Join(", ", disagreements.Take(12))
            + ". The print fold asks CanRender what it may keep, so a disagreement here is exactly the defect "
            + "census 12.1(d) recorded: text destroyed on the way to a writer that could have drawn it, or '?' "
            + "reaching a printed document.");

        // A sweep that silently compared nothing would also report zero disagreements.
        Assert.Equal(0x10000 - 2048 - 3, compared);
    }

    /// <summary>
    /// The boundary the sweep above excludes, pinned rather than assumed: tab, CR and LF are mapped by
    /// <c>PdfWriter.EscapeString</c> to the PDF escapes <c>\t \r \n</c>, so they are structure and not glyphs.
    /// <see cref="PdfWriter.CanRender"/> therefore says false, and the fold's ASCII arm — deliberately left
    /// byte-for-byte as it was — keeps them anyway without ever consulting it. Both halves asserted, so neither
    /// can move silently.
    /// </summary>
    [Fact]
    public void Tab_CR_and_LF_are_structural_escapes_and_are_outside_CanRender_s_question()
    {
        Assert.False(PdfWriter.CanRender('\t'));
        Assert.False(PdfWriter.CanRender('\r'));
        Assert.False(PdfWriter.CanRender('\n'));

        Assert.Equal("a\tb\r\nc", ReportPrintProjector.Ascii("a\tb\r\nc"));
    }

    /// <summary>
    /// Characters the fold must keep, one assertion per class, at the unit level — the complement of the sweep
    /// above and the readable statement of what changed: the Latin-1 letters European party and bank names
    /// actually use, and the CP1252 typographic block.
    /// </summary>
    [Theory]
    [InlineData("Müller")]                        // German        Müller
    [InlineData("Société Générale")] // French      Société Générale
    [InlineData("Peña Hermanos")]                 // Spanish       Peña Hermanos
    [InlineData("Åkerström AB")]             // Swedish       Åkerström AB
    [InlineData("Lefèvre & Cie")]                 // French        Lefèvre & Cie
    [InlineData("Ürün Ticaret")]             // Turkish       Ürün Ticaret
    public void A_latin1_master_name_is_kept_character_for_character_by_the_fold(string name)
    {
        Assert.Equal(name, ReportPrintProjector.Ascii(name));
    }

    /// <summary>
    /// The eight editorial folds the projector always performed are deliberate and are NOT collateral of this
    /// change: the dashes, quotes and bullet are WinAnsi-representable, so "keep what the font can render" would
    /// have passed them through and silently altered every existing printed document's bytes. They are named
    /// cases in the switch for that reason and they stay.
    /// </summary>
    [Fact]
    public void The_deliberate_editorial_folds_are_unchanged()
    {
        Assert.Equal("a-b", ReportPrintProjector.Ascii("a—b"));   // em dash
        Assert.Equal("a-b", ReportPrintProjector.Ascii("a–b"));   // en dash
        Assert.Equal("'a'", ReportPrintProjector.Ascii("‘a’"));
        Assert.Equal("\"a\"", ReportPrintProjector.Ascii("“a”"));
        Assert.Equal("*", ReportPrintProjector.Ascii("•"));
    }
}
