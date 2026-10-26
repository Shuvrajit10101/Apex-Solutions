using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Xunit;

namespace Apex.Desktop.Tests;

/// <summary>
/// Census 5.11 (v68), written by the wave-7 A12 review: the withholding carve REBUILDS the deductee's line on every
/// accept (<c>TdsService.BuildCarveOut</c>), on BOTH arms, the withheld one and the below-threshold one. Before
/// this file nothing pinned that a narration keyed under the deductee's row rides the rebuilt leg. That matters
/// more than an ordinary carry would, because on an alteration the deductee's narration sits in an EDITABLE field,
/// so <c>VoucherAlterationDerivedLegs.DroppedLineNarrationRefusal</c> excuses it. If the carve dropped it, the loss
/// would be SILENT on a fresh entry and on an alteration alike.
///
/// <para><b>Mutations:</b> <c>narration: keyedPartyLine?.Narration</c> to <c>null</c> on the withheld arm of
/// <c>TdsService.BuildCarveOut</c> reddens the withheld rows; the same on the below-threshold arm reddens the
/// below-threshold rows.</para>
/// </summary>
public sealed class LineNarrationTdsCarveTests
{
    private static Company Reloaded(AlterationBook book) =>
        book.Storage.Load(book.Storage.ListCompanies().Single(e => e.Name == book.Company.Name));

    [Theory]
    [InlineData(VoucherBaseType.Journal, "120000.30", 3)]   // withheld: Dr expense / Cr party net / Cr TDS Payable
    [InlineData(VoucherBaseType.Payment, "120000.30", 3)]
    [InlineData(VoucherBaseType.Journal, "30000.30", 2)]    // below threshold: the party leg is still rebuilt
    [InlineData(VoucherBaseType.Payment, "30000.30", 2)]
    public void A_narration_keyed_on_the_deductee_row_survives_the_carve_and_a_no_edit_alteration(
        VoucherBaseType baseType, string gross, int postedLines)
    {
        using var book = AlterationBook.New($"ln-tds-{baseType}-{gross}");
        var (expense, party) = book.EnableTds("194J(b)");
        var type = book.Type(baseType);
        type.ProvideNarrationForEachLedger = true;

        var posted = book.Post(type, book.On(),
            new[] { (expense, DrCr.Debit, gross), (party, DrCr.Credit, gross) },
            configure: e =>
            {
                e.Lines[0].LineNarration = "Statutory audit FY 2024-25";
                e.Lines[1].LineNarration = "Audit fees, May instalment";
            });
        Assert.Equal(postedLines, posted.Lines.Count);

        var first = Reloaded(book).Vouchers.Single(v => v.Id == posted.Id);
        Assert.Equal("Audit fees, May instalment", first.Lines.Single(l => l.LedgerId == party.Id).Narration);
        Assert.Equal("Statutory audit FY 2024-25", first.Lines.Single(l => l.LedgerId == expense.Id).Narration);

        var open = book.ForAlter(posted.Id);
        Assert.False(open.IsRefused, open.Refusal);
        Assert.Equal("Audit fees, May instalment",
            open.Entry!.Lines.Single(l => l.SelectedLedger?.Id == party.Id).LineNarration);
        Assert.True(open.Entry.AcceptAlteration(), open.Entry.Message);

        var second = Reloaded(book).Vouchers.Single(v => v.Id == posted.Id);
        Assert.Equal("Audit fees, May instalment", second.Lines.Single(l => l.LedgerId == party.Id).Narration);
        Assert.Equal("Statutory audit FY 2024-25", second.Lines.Single(l => l.LedgerId == expense.Id).Narration);
    }
}
