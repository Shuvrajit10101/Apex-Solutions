using System.Text;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Domain = Apex.Ledger.Domain;

namespace Apex.Ledger.Io.Tests;

/// <summary>
/// The Io fold-in gate for the <b>accounting-invoice flag</b> (schema v49) — the persisted fact that a Sales voucher
/// was posted from the Accounting Invoice (service-invoice) entry mode, which is what makes the print path issue it as
/// a Rule-46 tax invoice. A flagged voucher exports and re-imports exact in JSON <i>and</i> XML, byte-stably, into a
/// fresh (differently-Guid'd) company — otherwise an exported/re-imported service invoice would silently downgrade to
/// a plain voucher; and a voucher WITHOUT the flag stays byte-identical to the pre-feature golden (ER-13).
///
/// <para><b>ER-13 is the sharp edge.</b> The canonical JSON writer is configured <c>DefaultIgnoreCondition =
/// Never</c>, so a naively-added <c>bool</c> would emit <c>"isAccountingInvoice": false</c> on <i>every</i> voucher of
/// <i>every</i> existing company; an unconditional XML <c>Attr(...)</c> would emit
/// <c>isAccountingInvoice="false"</c> on every voucher. Both are pinned below.</para>
/// </summary>
public sealed class CanonicalAccountingInvoiceFlagRoundTripTests
{
    private static readonly DateOnly FyStart = new(2025, 4, 1);
    private const string ServiceNarration = "Consultancy billed as a service invoice";
    private const string PlainNarration = "Ordinary hand-keyed sale";

    /// <summary>A seeded company with one flagged (accounting-invoice) Sales voucher and one ordinary one.</summary>
    private static Company BuildCompanyWithFlaggedInvoice()
    {
        var c = CompanyFactory.CreateSeeded("Service Traders", FyStart);
        var sales = new Domain.Ledger(Guid.NewGuid(), "Consultancy Income", c.FindGroupByName("Sales Accounts")!.Id,
            Money.Zero, openingIsDebit: false);
        c.AddLedger(sales);
        var debtor = new Domain.Ledger(Guid.NewGuid(), "Acme Ltd", c.FindGroupByName("Sundry Debtors")!.Id,
            Money.Zero, openingIsDebit: true);
        c.AddLedger(debtor);

        var salesType = c.FindVoucherTypeByName("Sales")!;
        var svc = new LedgerService(c);

        svc.Post(new Voucher(Guid.NewGuid(), salesType.Id, new DateOnly(2025, 4, 5),
            new[]
            {
                new EntryLine(debtor.Id, Money.FromRupees(1000m), DrCr.Debit),
                new EntryLine(sales.Id, Money.FromRupees(1000m), DrCr.Credit),
            }, partyId: debtor.Id, narration: ServiceNarration, isAccountingInvoice: true));

        svc.Post(new Voucher(Guid.NewGuid(), salesType.Id, new DateOnly(2025, 4, 6),
            new[]
            {
                new EntryLine(debtor.Id, Money.FromRupees(500m), DrCr.Debit),
                new EntryLine(sales.Id, Money.FromRupees(500m), DrCr.Credit),
            }, partyId: debtor.Id, narration: PlainNarration));

        return c;
    }

    private static Company Fresh() => CompanyFactory.CreateSeeded("Fresh Service Co", FyStart);

    // ================================================================= lossless round-trip

    [Fact]
    public void AccountingInvoiceFlag_roundTrips_io()
    {
        var c = BuildCompanyWithFlaggedInvoice();

        var json = CanonicalJson.Export(c);
        var (jsonModel, jsonErrors) = CanonicalJson.Parse(json);
        Assert.Empty(jsonErrors);
        Assert.NotNull(jsonModel);
        Assert.Equal(json, CanonicalJson.Export(jsonModel!)); // byte-stable
        AssertFlagSurvived(ImportInto(jsonModel!));

        var xml = CanonicalXml.Export(c);
        var (xmlModel, xmlErrors) = CanonicalXml.Parse(xml);
        Assert.Empty(xmlErrors);
        Assert.NotNull(xmlModel);
        Assert.Equal(xml, CanonicalXml.Export(xmlModel!)); // byte-stable
        AssertFlagSurvived(ImportInto(xmlModel!));
    }

    private static Company ImportInto(CanonicalModel model)
    {
        var target = Fresh();
        Assert.True(new CompanyImportService(target).Apply(model, DuplicatePolicy.Skip).Applied);
        return target;
    }

    private static void AssertFlagSurvived(Company target)
    {
        Assert.True(target.Vouchers.Single(x => x.Narration == ServiceNarration).IsAccountingInvoice);
        // …and it is not smeared over every voucher: the ordinary sale stays false.
        Assert.False(target.Vouchers.Single(x => x.Narration == PlainNarration).IsAccountingInvoice);
    }

    // ================================================================= ER-13

    [Fact]
    public void UnflaggedVouchers_areEr13ByteIdentical()
    {
        // A company with no accounting invoice must serialise EXACTLY as it did before v49 — no isAccountingInvoice
        // key (JSON) or attribute (XML) anywhere.
        var c = CompanyFactory.CreateSeeded("Plain Service Co", FyStart);
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
            }, partyId: debtor.Id, narration: PlainNarration));

        var json = Encoding.UTF8.GetString(CanonicalJson.Export(c));
        var xml = Encoding.UTF8.GetString(CanonicalXml.Export(c));

        Assert.DoesNotContain("isAccountingInvoice", json, StringComparison.Ordinal);
        Assert.DoesNotContain("isAccountingInvoice", xml, StringComparison.Ordinal);

        // Belt-and-braces: a company that DOES carry the flag emits it, so the assertions above detect a real absence
        // rather than passing because the writer never emits the field at all.
        var configured = BuildCompanyWithFlaggedInvoice();
        Assert.Contains("\"isAccountingInvoice\"",
            Encoding.UTF8.GetString(CanonicalJson.Export(configured)), StringComparison.Ordinal);
        Assert.Contains("isAccountingInvoice=\"true\"",
            Encoding.UTF8.GetString(CanonicalXml.Export(configured)), StringComparison.Ordinal);
    }
}
