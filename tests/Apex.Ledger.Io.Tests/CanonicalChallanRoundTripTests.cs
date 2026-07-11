using System.Text;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Domain = Apex.Ledger.Domain;

namespace Apex.Ledger.Io.Tests;

/// <summary>
/// Phase-7 slice-3 <b>Io fold-in</b> gate (PR-4 losslessness): a company that has deducted 194J TDS and then
/// deposited it via a Stat Payment (a Payment type flagged <see cref="VoucherType.IsStatPayment"/>) with an
/// ITNS-281 challan + its challan↔voucher link <b>exports and re-imports paisa + count exact in JSON AND XML</b>,
/// both byte-stable, and into a fresh (differently-Guid'd) company through the engine-routed CompanyImportService —
/// the challan link re-mapping to the target's re-minted stat-payment voucher (the Phase-6 carry-forward defect
/// class: a new child silently dropped on export/import), built in here rather than deferred.
/// </summary>
public class CanonicalChallanRoundTripTests
{
    private const string ValidTan = "MUMA12345B";
    private const string DeducteePan = "AAPFU0939F";
    private static readonly DateOnly Fy = new(2025, 4, 1);
    private static readonly DateOnly Deduct = new(2025, 5, 10);
    private static readonly DateOnly Deposit = new(2025, 6, 5);

    private static Company BuildDepositedCompany()
    {
        var c = CompanyFactory.CreateSeeded("Deposit Traders", Fy, Fy);
        new TdsTcsService(c).EnableTds(new TdsConfig { Tan = ValidTan });

        var fees = new Domain.Ledger(Guid.NewGuid(), "Professional Fees", c.FindGroupByName("Indirect Expenses")!.Id, Money.Zero, true);
        c.AddLedger(fees);
        var vendor = new Domain.Ledger(Guid.NewGuid(), "Consultant", c.FindGroupByName("Sundry Creditors")!.Id, Money.Zero, false)
        { TdsApplicable = true, TdsNatureOfPaymentId = c.FindNatureOfPaymentByCode("194J(b)")!.Id, DeducteeType = DeducteeType.Firm, PartyPan = DeducteePan };
        c.AddLedger(vendor);
        var bank = new Domain.Ledger(Guid.NewGuid(), "HDFC Bank", c.FindGroupByName("Bank Accounts")!.Id, Money.Zero, true);
        c.AddLedger(bank);

        var nop = c.FindNatureOfPaymentByCode("194J(b)")!;
        var carve = new TdsService(c).BuildCarveOut(Money.FromRupees(1_00_000m), Money.FromRupees(1_00_000m), nop, vendor, Deduct);
        new LedgerService(c).Post(new Voucher(Guid.NewGuid(),
            c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Journal).Id, Deduct,
            new[] { new Domain.EntryLine(fees.Id, Money.FromRupees(1_00_000m), DrCr.Debit), carve.PartyLine, carve.TdsPayableLine! }));

        var dep = new TdsDepositService(c);
        var statType = dep.EnsureStatPaymentType();
        var posted = new LedgerService(c).Post(dep.BuildStatPayment(Money.FromRupees(10_000m), bank, Deposit, statType));
        dep.RecordChallan("00123", "0510308", Deposit, Money.FromRupees(10_000m), "194J(b)", "200", posted);
        return c;
    }

    private static Company FreshTarget() => CompanyFactory.CreateSeeded("Fresh Deposit Co", Fy, Fy);

    [Fact]
    public void Json_round_trips_byte_stable_with_challan_and_stat_payment()
    {
        var c = BuildDepositedCompany();
        var first = CanonicalJson.Export(c);
        var (model, errors) = CanonicalJson.Parse(first);
        Assert.Empty(errors);
        Assert.Equal(first, CanonicalJson.Export(model!)); // byte-for-byte
        AssertProjection(model!);
        AssertNoTally(first);
    }

    [Fact]
    public void Xml_round_trips_byte_stable_with_challan_and_stat_payment()
    {
        var c = BuildDepositedCompany();
        var first = CanonicalXml.Export(c);
        var (model, errors) = CanonicalXml.Parse(first);
        Assert.Empty(errors);
        Assert.Equal(first, CanonicalXml.Export(model!));
        AssertProjection(model!);
        AssertNoTally(first);
    }

    [Fact]
    public void Json_and_xml_carry_identical_challan_payload()
    {
        var c = BuildDepositedCompany();
        var (jm, je) = CanonicalJson.Parse(CanonicalJson.Export(c));
        var (xm, xe) = CanonicalXml.Parse(CanonicalXml.Export(c));
        Assert.Empty(je);
        Assert.Empty(xe);
        Assert.Equal(CanonicalJson.Export(jm!), CanonicalJson.Export(xm!));
    }

    [Fact]
    public void Deposit_company_export_import_into_fresh_company_reconciles_json_and_xml()
    {
        var source = BuildDepositedCompany();

        foreach (var bytes in new[] { CanonicalJson.Export(source), CanonicalXml.Export(source) })
        {
            var (model, errors) = bytes[0] == (byte)'{' ? CanonicalJson.Parse(bytes) : CanonicalXml.Parse(bytes);
            Assert.Empty(errors);

            var fresh = FreshTarget();
            var result = new CompanyImportService(fresh).Apply(model!);
            Assert.True(result.Applied, string.Join("; ", result.Errors));

            // The stat-payment type + challan + link reconcile paisa/count-exact, the link re-mapped to the target's
            // re-minted stat-payment voucher (Dr TDS Payable / Cr Bank).
            var statType = Assert.Single(fresh.VoucherTypes, t => t.IsStatPaymentType);
            Assert.Equal(VoucherBaseType.Payment, statType.BaseType);

            var ch = Assert.Single(fresh.TdsChallans);
            Assert.Equal("00123", ch.ChallanNo);
            Assert.Equal("0510308", ch.BsrCode);
            Assert.Equal(Money.FromRupees(10_000m), ch.Amount);
            Assert.Equal("194J(b)", ch.Section);
            Assert.Equal("200", ch.MinorHead);

            var linkedVoucherId = Assert.Single(fresh.VouchersLinkedToChallan(ch.Id));
            var linked = fresh.FindVoucher(linkedVoucherId)!;
            Assert.Equal(statType.Id, linked.TypeId);              // the link resolves to the re-minted stat voucher
            Assert.Equal(Money.FromRupees(10_000m), linked.TotalDebit);
        }
    }

    private static void AssertProjection(CanonicalModel model)
    {
        var stat = Assert.Single(model.Payload.VoucherTypes, t => t.IsStatPayment);
        Assert.Equal("Payment", stat.BaseType);

        var ch = Assert.Single(model.Payload.TdsChallans);
        Assert.Equal("00123", ch.ChallanNo);
        Assert.Equal(1_000_000L, ch.AmountPaisa); // ₹10,000 = 1,000,000 paisa
        Assert.Equal("194J(b)", ch.Section);

        var link = Assert.Single(model.Payload.ChallanVoucherLinks);
        Assert.Equal(ch.Id, link.ChallanId);
    }

    private static void AssertNoTally(byte[] bytes) =>
        Assert.DoesNotContain("tally", Encoding.UTF8.GetString(bytes), StringComparison.OrdinalIgnoreCase);
}
