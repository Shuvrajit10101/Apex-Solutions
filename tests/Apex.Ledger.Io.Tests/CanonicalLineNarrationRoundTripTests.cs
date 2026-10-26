using System.Text;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;

namespace Apex.Ledger.Io.Tests;

/// <summary>
/// Census 5.11 (schema v68) — the per-ledger narration the voucher type's <i>Provide narration for each ledger in
/// voucher</i> flag lets the operator key (help.tallysolutions.com/voucher-types-tally/, Set up transaction
/// options: <i>"Enable Provide narration for each ledger in voucher to add narration for individual ledgers"</i>)
/// round-trips through canonical JSON <b>and</b> XML into a FRESH company, on the right line, byte-stably; and a
/// book with no line narration exports byte-identically to before (no new key, no new attribute — ER-13).
///
/// <para><b>Mutations:</b> dropping <c>Narration = l.Narration</c> from <c>CanonicalMapper.MapEntryLine</c>, or
/// <c>narration: l.Narration</c> from <c>ImportPlan</c>, reddens
/// <see cref="Line_narration_roundTrips_through_json_and_xml_onto_the_right_line"/>; dropping the XML reader's
/// <c>Narration = Str(e, "narration")</c> reddens its XML half only.</para>
/// </summary>
public sealed class CanonicalLineNarrationRoundTripTests
{
    private static readonly DateOnly FyStart = new(2025, 4, 1);

    private static Company BuildNarratedBook(bool narrate)
    {
        var c = CompanyFactory.CreateSeeded("Narrated Traders", FyStart);
        var cash = c.FindLedgerByName("Cash")!;
        var rent = new Ledger.Domain.Ledger(Guid.NewGuid(), "Rent", c.FindGroupByName("Indirect Expenses")!.Id,
            Money.Zero, openingIsDebit: true);
        c.AddLedger(rent);
        var payment = c.FindVoucherTypeByName("Payment")!;
        new LedgerService(c).Post(new Voucher(Guid.NewGuid(), payment.Id, new DateOnly(2025, 5, 2), new[]
        {
            new EntryLine(rent.Id, Money.FromRupees(12000m), DrCr.Debit,
                narration: narrate ? "Rent for May, shop 4 & godown" : null),
            new EntryLine(cash.Id, Money.FromRupees(12000m), DrCr.Credit,
                narration: narrate ? "Paid from counter cash" : null),
        }, narration: "Monthly rent"));
        return c;
    }

    [Fact]
    public void Line_narration_roundTrips_through_json_and_xml_onto_the_right_line()
    {
        var c = BuildNarratedBook(narrate: true);

        var json = CanonicalJson.Export(c);
        var (jsonModel, jsonErrors) = CanonicalJson.Parse(json);
        Assert.Empty(jsonErrors);
        Assert.Equal(json, CanonicalJson.Export(jsonModel!));   // byte-stable
        AssertNarrationSurvived(ImportInto(jsonModel!));

        var xml = CanonicalXml.Export(c);
        var (xmlModel, xmlErrors) = CanonicalXml.Parse(xml);
        Assert.Empty(xmlErrors);
        Assert.Equal(xml, CanonicalXml.Export(xmlModel!));      // byte-stable
        AssertNarrationSurvived(ImportInto(xmlModel!));
    }

    [Fact]
    public void A_book_without_line_narration_emits_no_narration_key_on_any_line()
    {
        var plain = BuildNarratedBook(narrate: false);
        var json = Encoding.UTF8.GetString(CanonicalJson.Export(plain));
        var xml = Encoding.UTF8.GetString(CanonicalXml.Export(plain));

        var narrated = BuildNarratedBook(narrate: true);
        var jsonWith = Encoding.UTF8.GetString(CanonicalJson.Export(narrated));
        var xmlWith = Encoding.UTF8.GetString(CanonicalXml.Export(narrated));

        // No <line> element of the plain book carries the attribute (ER-13) ...
        foreach (var lineEl in xml.Split("<line ").Skip(1))
            Assert.DoesNotContain("narration=", lineEl[..lineEl.IndexOf('>')], StringComparison.Ordinal);
        // ... while both lines of the narrated book do, so the absence above is real.
        Assert.Equal(2, xmlWith.Split("<line ").Skip(1)
            .Count(lineEl => lineEl[..lineEl.IndexOf('>')].Contains("narration=", StringComparison.Ordinal)));
        Assert.Contains("narration=\"Rent for May, shop 4 &amp; godown\"", xmlWith, StringComparison.Ordinal);

        // JSON: the narrated book carries exactly two more "narration" keys than the plain one (one per line);
        // the voucher-level narration key is present in both, so it is not what is being counted.
        static int Keys(string s) => s.Split("\"narration\"").Length - 1;
        Assert.Equal(Keys(json) + 2, Keys(jsonWith));
        Assert.Contains("Paid from counter cash", jsonWith, StringComparison.Ordinal);
        Assert.DoesNotContain("Paid from counter cash", json, StringComparison.Ordinal);
    }

    private static Company ImportInto(CanonicalModel model)
    {
        var target = CompanyFactory.CreateSeeded("Fresh Narration Co", FyStart);
        Assert.True(new CompanyImportService(target).Apply(model, DuplicatePolicy.Skip).Applied);
        return target;
    }

    private static void AssertNarrationSurvived(Company target)
    {
        var v = Assert.Single(target.Vouchers);
        var rent = target.FindLedgerByName("Rent")!;
        var cash = target.FindLedgerByName("Cash")!;
        Assert.Equal("Rent for May, shop 4 & godown", v.Lines.Single(l => l.LedgerId == rent.Id).Narration);
        Assert.Equal("Paid from counter cash", v.Lines.Single(l => l.LedgerId == cash.Id).Narration);
        Assert.Equal("Monthly rent", v.Narration);
    }
}
