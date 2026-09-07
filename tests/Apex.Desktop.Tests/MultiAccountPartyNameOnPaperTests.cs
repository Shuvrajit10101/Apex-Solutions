using System;
using System.Text;
using Apex.Desktop.Services;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Io;
using Apex.Ledger.Services;
using Domain = Apex.Ledger.Domain;

namespace Apex.Desktop.Tests;

/// <summary>
/// 🔴 <b>RULING 18 at the one place in the product where a document HEADING is a master name.</b>
///
/// <para><c>MultiAccountPrintProjector</c> titles each ledger-account sheet
/// <c>"Ledger Account - " + ledger.Name</c>, and <c>ReportPdf</c>'s ER-11 guard used to strip the vendor token
/// out of the whole heading. The sheet is a STATEMENT OF ACCOUNT posted to that party, so the guard renamed the
/// addressee on their own statement. <c>ReportPdf</c>'s own source note called this out as the case that made
/// the scrub "reachable" — it is in fact the case that made the scrub wrong, and Ruling 18 settles it.</para>
///
/// <para>The seam is <see cref="PrintReport.TitleCarriesMasterName"/>: the projector knows which half of the
/// heading it authored and says so, because a bare string cannot. Both halves are asserted here — the party's
/// name reaches paper intact, and the subtitle (OUR company name and the period) still goes through the guard.
/// The shell route into this projector is separately proved live by
/// <c>MultiAccountPrintReachabilityTests</c>.</para>
/// </summary>
public sealed class MultiAccountPartyNameOnPaperTests
{
    private static readonly DateOnly FyStart = new(2025, 4, 1);
    private const string PartyName = "Tally Traders Pvt Ltd";
    private const string OurCompany = "Tally Solutions Retail";   // OUR OWN name, deliberately branded

    private static string AsLatin1(byte[] bytes) => Encoding.Latin1.GetString(bytes);

    private static int CountBrand(string text)
    {
        int n = 0;
        for (int i = text.IndexOf("tally", StringComparison.OrdinalIgnoreCase); i >= 0;
             i = text.IndexOf("tally", i + 5, StringComparison.OrdinalIgnoreCase))
        {
            n++;
        }
        return n;
    }

    private static (Company C, Guid PartyId) BooksWithABrandedParty() => Books(PartyName);

    private static (Company C, Guid PartyId) Books(string partyName)
    {
        var c = CompanyFactory.CreateSeeded(OurCompany, FyStart);
        var party = new Domain.Ledger(
            Guid.NewGuid(), partyName, c.FindGroupByName("Sundry Debtors")!.Id,
            Money.FromRupees(25_000m), openingIsDebit: true);
        c.AddLedger(party);
        return (c, party.Id);
    }

    private static string RenderOne(Company c, Guid partyId, MultiAccountDocumentKind kind)
    {
        var docs = MultiAccountPrintProjector.Project(
            c, new[] { partyId }, kind, FyStart, new DateOnly(2026, 3, 31));
        return AsLatin1(ReportPdf.Render(docs, new PageConfig()));
    }

    /// <summary>
    /// The projector states the provenance rather than leaving the renderer to guess: the heading is flagged as
    /// carrying a master name, and it carries the party's name in full.
    /// </summary>
    [Fact]
    public void The_projector_flags_a_ledger_account_heading_as_carrying_a_master_name()
    {
        var (c, partyId) = BooksWithABrandedParty();

        var docs = MultiAccountPrintProjector.Project(
            c, new[] { partyId }, MultiAccountDocumentKind.LedgerAccount, FyStart, new DateOnly(2026, 3, 31));

        var sheet = Assert.Single(docs);
        Assert.True(sheet.TitleCarriesMasterName,
            "the heading concatenates our label with a master name; without the flag the renderer scrubs the "
          + "party's own name off their statement.");
        Assert.Contains(PartyName, sheet.Title, StringComparison.Ordinal);
    }

    /// <summary>
    /// End of the pipe: the party's name survives onto the printed page, and OUR company name in the subtitle is
    /// still de-branded in the SAME document — which is the whole of Ruling 18 in one assertion pair.
    /// </summary>
    [Fact]
    public void The_printed_statement_names_the_party_in_full_and_still_debrands_our_own_company_name()
    {
        var (c, partyId) = BooksWithABrandedParty();

        var docs = MultiAccountPrintProjector.Project(
            c, new[] { partyId }, MultiAccountDocumentKind.LedgerAccount, FyStart, new DateOnly(2026, 3, 31));
        string s = AsLatin1(ReportPdf.Render(docs, new PageConfig()));

        Assert.Contains("Ledger Account - " + PartyName, s, StringComparison.Ordinal);
        // Our company name lost the token but kept the rest — de-branding is not blanking.
        Assert.Contains("Solutions Retail", s, StringComparison.Ordinal);
        // And the party's heading is the ONLY surviving occurrence in the whole document.
        Assert.Equal(1, CountBrand(s));
    }

    /// <summary>
    /// 🔴 <b>Pins the VISIBLE-heading vs <c>/Title</c>-metadata divergence that ruling 18 opened, in both
    /// directions — WITHOUT settling it.</b> <c>ReportPdf.HeadingText</c> honours the master-name flag, so the
    /// party's name is intact on the page; <c>ReportPdf.SafeTitle</c> is deliberately still unconditional, so the
    /// document-properties <c>/Title</c> has the token stripped. One PDF therefore says two different things
    /// about the same party.
    ///
    /// <para><b>Whether that is right is an open R12 question for the user</b> — nothing a counterparty reads on
    /// the page is altered, only the file's metadata, and metadata is arguably ours. This test does not answer
    /// it. It exists because the divergence was documented in a source comment and pinned by NOTHING, so either
    /// side could be flipped silently by a later track and the whole gate would stay green. Whichever way the
    /// user settles it, this test must be updated deliberately, which is the point.</para>
    /// </summary>
    [Fact]
    public void The_visible_heading_keeps_the_party_name_while_the_pdf_title_metadata_still_strips_it()
    {
        var (c, partyId) = BooksWithABrandedParty();

        string s = RenderOne(c, partyId, MultiAccountDocumentKind.LedgerAccount);

        // On the page: intact.
        Assert.Contains("Ledger Account - " + PartyName, s, StringComparison.Ordinal);
        // In /Title: stripped, and the divergence is exactly this.
        Assert.Contains("/Title (Ledger Account - Traders Pvt Ltd", s, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ ruling 18, the OTHER direction
    //
    // 🔴 The two tests below are the ones that were MISSING, and their absence let a real defect ship green:
    // this file rendered ONLY MultiAccountDocumentKind.LedgerAccount, and the Reminder Letter and the
    // Confirmation of Accounts — the two documents that put "For <our company>" in a PrintRow BODY cell — were
    // never rendered at all. When ReportPdf stopped scrubbing body cells (correctly: they are book data), OUR
    // OWN company name started printing un-stripped on the signature line of two letters posted to
    // counterparties, while the SUBTITLE of the same page, which the renderer still scrubs, showed it stripped.
    // One document disagreeing with itself, and the whole four-project gate stayed green through it.
    //
    // The PARTY NAME IS DELIBERATELY CLEAN in both. That is the point: it makes the branded company name the
    // ONLY possible source of the token, so `CountBrand == 0` names the defect precisely instead of being
    // satisfied by a party name that happens to be there.

    [Theory]
    [InlineData(MultiAccountDocumentKind.ReminderLetter)]
    [InlineData(MultiAccountDocumentKind.ConfirmationOfAccounts)]
    public void Our_own_company_name_is_debranded_on_the_signature_line_of_a_letter_sent_to_a_counterparty(
        MultiAccountDocumentKind kind)
    {
        // A CLEAN party — so the ONLY thing that could put the token on this page is OUR OWN name.
        var (c, partyId) = Books("Bright Traders Pvt Ltd");

        string s = RenderOne(c, partyId, kind);

        // The signatory line is actually on the page (so the assertion below cannot pass by the line being absent).
        Assert.Contains("For Solutions Retail", s, StringComparison.Ordinal);
        // De-branded, not blanked: the rest of our name survives.
        Assert.Contains("Bright Traders Pvt Ltd", s, StringComparison.Ordinal);
        // And nothing anywhere on this letter carries the token.
        Assert.Equal(0, CountBrand(s));
    }

    /// <summary>
    /// Both directions of ruling 18 on ONE page, for the two letters: the counterparty's branded legal name
    /// survives in full where the letter addresses them, and our own branded name is still stripped on the
    /// signature line of that same page.
    /// </summary>
    [Theory]
    [InlineData(MultiAccountDocumentKind.ReminderLetter)]
    [InlineData(MultiAccountDocumentKind.ConfirmationOfAccounts)]
    public void A_letter_names_the_counterparty_in_full_while_still_debranding_our_own_name(
        MultiAccountDocumentKind kind)
    {
        var (c, partyId) = BooksWithABrandedParty();

        string s = RenderOne(c, partyId, kind);

        // Direction (a): the addressee's own legal name reaches paper intact.
        Assert.Contains("To: " + PartyName, s, StringComparison.Ordinal);
        // Direction (b): ours is stripped on the same page.
        Assert.Contains("For Solutions Retail", s, StringComparison.Ordinal);
        // Exactly one occurrence — the addressee's. Over-scrubbing (0) and our leak (2) both fail here.
        Assert.Equal(1, CountBrand(s));
    }
}
