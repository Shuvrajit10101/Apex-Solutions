using System.Text;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Domain = Apex.Ledger.Domain;

namespace Apex.Ledger.Io.Tests;

/// <summary>
/// Voucher-numbering S5 (numbering-design-v2 §8.3) — the Io fold-in gate for the <b>counterparty captured field</b>.
/// A voucher that carries a reference (Reference No / Supplier Invoice No + its date) exports and re-imports exact in
/// JSON <i>and</i> XML, byte-stably, into a fresh (differently-Guid'd) company; and a voucher with NO reference stays
/// byte-identical to the pre-feature golden — no new keys/attributes at all (ER-13).
///
/// <para><b>ER-13 is the sharp edge.</b> The canonical JSON writer is configured <c>DefaultIgnoreCondition =
/// Never</c>, so a naively-added <c>string?</c> would emit <c>"referenceNo": null</c> on <i>every</i> voucher of
/// <i>every</i> existing company; and an unconditional XML <c>Attr(...)</c> would emit <c>referenceNo=""</c> on every
/// voucher. Both are pinned below.</para>
/// </summary>
public sealed class CanonicalReferenceRoundTripTests
{
    private static readonly DateOnly FyStart = new(2025, 4, 1);
    private const string RefNarration = "Sale carrying a counterparty reference";

    /// <summary>A seeded company with one Sales voucher carrying a counterparty reference
    /// ("ABC/123" dated 5-Apr-2025), distinguished by <see cref="RefNarration"/>.</summary>
    private static Company BuildCompanyWithReference()
    {
        var c = CompanyFactory.CreateSeeded("Referenced Traders", FyStart);
        var sales = new Domain.Ledger(Guid.NewGuid(), "Sales", c.FindGroupByName("Sales Accounts")!.Id,
            Money.Zero, openingIsDebit: false);
        c.AddLedger(sales);
        var debtor = new Domain.Ledger(Guid.NewGuid(), "Acme Ltd", c.FindGroupByName("Sundry Debtors")!.Id,
            Money.Zero, openingIsDebit: true);
        c.AddLedger(debtor);

        var salesType = c.FindVoucherTypeByName("Sales")!;
        new LedgerService(c).Post(new Voucher(Guid.NewGuid(), salesType.Id, new DateOnly(2025, 4, 5),
            new[]
            {
                new EntryLine(debtor.Id, Money.FromRupees(1000m), DrCr.Debit),
                new EntryLine(sales.Id, Money.FromRupees(1000m), DrCr.Credit),
            }, partyId: debtor.Id, narration: RefNarration,
            referenceNo: "ABC/123", referenceDate: new DateOnly(2025, 4, 5)));
        return c;
    }

    private static Company Fresh() => CompanyFactory.CreateSeeded("Fresh Ref Co", FyStart);

    // ================================================================= lossless round-trip

    [Fact]
    public void ReferenceNo_roundTrips_io()
    {
        var c = BuildCompanyWithReference();

        // JSON: byte-stable, and imports into a fresh company with the reference preserved.
        var json = CanonicalJson.Export(c);
        var (jsonModel, jsonErrors) = CanonicalJson.Parse(json);
        Assert.Empty(jsonErrors);
        Assert.NotNull(jsonModel);
        Assert.Equal(json, CanonicalJson.Export(jsonModel!)); // byte-stable
        AssertReferenceSurvived(ImportInto(jsonModel!));

        // XML: byte-stable, and imports into a fresh company with the reference preserved.
        var xml = CanonicalXml.Export(c);
        var (xmlModel, xmlErrors) = CanonicalXml.Parse(xml);
        Assert.Empty(xmlErrors);
        Assert.NotNull(xmlModel);
        Assert.Equal(xml, CanonicalXml.Export(xmlModel!)); // byte-stable
        AssertReferenceSurvived(ImportInto(xmlModel!));
    }

    private static Company ImportInto(CanonicalModel model)
    {
        var target = Fresh();
        Assert.True(new CompanyImportService(target).Apply(model, DuplicatePolicy.Skip).Applied);
        return target;
    }

    private static void AssertReferenceSurvived(Company target)
    {
        var v = target.Vouchers.Single(x => x.Narration == RefNarration);
        Assert.Equal("ABC/123", v.ReferenceNo);
        Assert.Equal(new DateOnly(2025, 4, 5), v.ReferenceDate);
    }

    // ================================================================= ER-13

    [Fact]
    public void EmptyReference_isEr13ByteIdentical()
    {
        // A company whose vouchers carry NO counterparty reference must serialise EXACTLY as it did before v48 — no
        // referenceNo / referenceDate keys (JSON) or attributes (XML) anywhere.
        var c = CompanyFactory.CreateSeeded("Plain Ref Co", FyStart);
        var sales = new Domain.Ledger(Guid.NewGuid(), "Sales", c.FindGroupByName("Sales Accounts")!.Id,
            Money.Zero, openingIsDebit: false);
        c.AddLedger(sales);
        var debtor = new Domain.Ledger(Guid.NewGuid(), "Acme Ltd", c.FindGroupByName("Sundry Debtors")!.Id,
            Money.Zero, openingIsDebit: true);
        c.AddLedger(debtor);
        new LedgerService(c).Post(new Voucher(Guid.NewGuid(), c.FindVoucherTypeByName("Sales")!.Id,
            new DateOnly(2025, 4, 5), new[]
            {
                new EntryLine(debtor.Id, Money.FromRupees(1000m), DrCr.Debit),
                new EntryLine(sales.Id, Money.FromRupees(1000m), DrCr.Credit),
            }, partyId: debtor.Id, narration: "Plain sale, no reference"));

        var json = Encoding.UTF8.GetString(CanonicalJson.Export(c));
        var xml = Encoding.UTF8.GetString(CanonicalXml.Export(c));

        Assert.DoesNotContain("\"referenceNo\":", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"referenceDate\":", json, StringComparison.Ordinal);
        Assert.DoesNotContain("referenceNo", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("referenceDate", xml, StringComparison.Ordinal);

        // Belt-and-braces: a company that DOES carry a reference emits them, so the assertions above are detecting a
        // real absence rather than passing because the writer never emits them at all.
        var configured = BuildCompanyWithReference();
        var jsonWith = Encoding.UTF8.GetString(CanonicalJson.Export(configured));
        var xmlWith = Encoding.UTF8.GetString(CanonicalXml.Export(configured));
        Assert.Contains("\"referenceNo\":", jsonWith, StringComparison.Ordinal);
        Assert.Contains("referenceNo=", xmlWith, StringComparison.Ordinal);
    }
}
