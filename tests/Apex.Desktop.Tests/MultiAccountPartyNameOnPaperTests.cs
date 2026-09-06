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

    private static (Company C, Guid PartyId) BooksWithABrandedParty()
    {
        var c = CompanyFactory.CreateSeeded(OurCompany, FyStart);
        var party = new Domain.Ledger(
            Guid.NewGuid(), PartyName, c.FindGroupByName("Sundry Debtors")!.Id,
            Money.FromRupees(25_000m), openingIsDebit: true);
        c.AddLedger(party);
        return (c, party.Id);
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
}
