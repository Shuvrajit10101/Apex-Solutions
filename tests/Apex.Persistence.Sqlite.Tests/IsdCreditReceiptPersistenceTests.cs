using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Persistence.Sqlite.Tests;

/// <summary>
/// <b>An ISD credit receipt survives a save/load, and reports the same Table 4(A)(4) off the RELOADED book</b>
/// (census row 6.24).
///
/// <para>🔴 <b>THE POINT OF THIS FILE IS THE NO-DDL CLAIM, TESTED RATHER THAN ASSERTED.</b> The feature adds a
/// new <see cref="GstAdjustmentKind"/> member, <c>IsdCreditReceived</c>, and <b>no schema version was taken</b>
/// — v67 is owned by another branch this wave. That is only safe because <c>gst_adjustment_kind</c> is a bare
/// <c>INTEGER NULL</c> with no CHECK constraint, so a new ordinal stores and reads back like any other. If that
/// were ever untrue the round-trip below would fail rather than a comment being wrong.</para>
///
/// <para>🔴 <b>DELIBERATELY NOT A <c>Schema.CurrentVersion == 66</c> TRIPWIRE.</b> The census records that
/// exact pattern biting: <c>IsdRegistrationPersistenceTests</c> hard-asserted <c>== 64</c>, v65 landed from
/// another branch, and the test then failed on rebase through no fault of its own. A sibling owns v67 this
/// wave, so pinning the number here would schedule the same breakage. What this file pins instead is the
/// PROPERTY that matters — the tag and its figures round-trip — which stays true at any schema version.</para>
/// </summary>
public sealed class IsdCreditReceiptPersistenceTests
{
    private static readonly DateOnly FyStart = new(2024, 4, 1);
    private static readonly DateOnly DocDate = new(2024, 4, 10);
    private static readonly DateOnly PostDate = new(2024, 4, 20);
    private static readonly DateOnly From = new(2024, 4, 1);
    private static readonly DateOnly To = new(2024, 4, 30);

    private const string Maharashtra = "27";
    private const string Karnataka = "29";

    private static string GstinFor(string stateCode, string pan)
    {
        var body = stateCode + pan + "1Z";
        return body + Gstin.ComputeCheckDigit(body + "0");
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void An_isd_credit_receipt_round_trips_and_still_files_table_4a4()
    {
        var dbPath = TempDbFile.NewPath("apex-isd-roundtrip");
        try
        {
            var c = CompanyFactory.CreateSeeded("ISD Persistence Co", FyStart);
            new GstService(c).EnableGst(new GstConfig
            {
                HomeStateCode = Maharashtra,
                Gstin = GstinFor(Maharashtra, "AAPFU0939F"),
                RegistrationType = GstRegistrationType.Regular,
                ApplicableFrom = FyStart,
                Periodicity = GstReturnPeriodicity.Monthly,
            });

            var branch = new GstRegistration(
                Guid.NewGuid(), "Karnataka Registration", Karnataka, GstinFor(Karnataka, "AABCC1206D"),
                GstRegistrationType.Regular, FyStart);
            c.Gst!.AddRegistration(branch);
            c.Gst!.EnsureValid();

            var headOffice = new Apex.Ledger.Domain.Ledger(
                Guid.NewGuid(), "Head Office Account", c.FindGroupByName("Indirect Expenses")!.Id, Money.Zero, true);
            c.AddLedger(headOffice);

            // ₹1,500 CGST + ₹1,500 SGST + ₹2,000 IGST + ₹500 cess, received into the KARNATAKA branch.
            var posted = new GstIsdCreditService(c).Post(
                "ISD/2024-25/042", DocDate,
                new GstIsdCreditService.IsdCreditAmount(150_000L, 150_000L, 200_000L, 50_000L),
                headOffice.Id, PostDate, branch.Id);

            using (var store = new SqliteCompanyStore(dbPath)) store.Save(c);
            Company r;
            using (var store = new SqliteCompanyStore(dbPath)) r = store.Load(c.Id)!;

            // ---- the tag survived on the reloaded voucher ----
            var reloaded = r.FindVoucher(posted.Id);
            Assert.NotNull(reloaded);

            var tagged = reloaded!.Lines
                .Where(l => l.Gst is { Adjustment: GstAdjustmentKind.IsdCreditReceived })
                .ToList();
            Assert.Equal(4, tagged.Count);
            Assert.All(tagged, l => Assert.Equal(DrCr.Debit, l.Side));

            // ---- the registration stamp survived (it is what routes the credit to ONE return) ----
            Assert.Equal(branch.Id, reloaded.GstRegistrationId);

            // ---- the document number survived, which is what the duplicate guard reads ----
            Assert.StartsWith(GstIsdCreditService.NarrationPrefix, reloaded.Narration!, StringComparison.Ordinal);
            Assert.Contains("ISD/2024-25/042", reloaded.Narration!, StringComparison.Ordinal);

            // ---- and the RELOADED book files the same figures ----
            var branch3b = Gstr3b.Build(r, From, To, branch.Id);
            Assert.Equal(1_500m, branch3b.Itc4A4IsdCgst.Amount);
            Assert.Equal(1_500m, branch3b.Itc4A4IsdSgst.Amount);
            Assert.Equal(2_000m, branch3b.Itc4A4IsdIgst.Amount);
            Assert.Equal(500m, branch3b.Itc4A4IsdCess.Amount);
            Assert.Equal(5_000m, branch3b.TotalItc4A4Isd.Amount);

            // ---- and it reaches no sister registration off the reloaded book either ----
            Assert.Equal(0m, Gstr3b.Build(r, From, To, GstRegistration.PrimaryId).TotalItc4A4Isd.Amount);

            // ---- the duplicate guard still holds against the RELOADED book ----
            var ex = Assert.Throws<InvalidOperationException>(() => new GstIsdCreditService(r).Post(
                "ISD/2024-25/042", DocDate,
                new GstIsdCreditService.IsdCreditAmount(150_000L, 150_000L, 200_000L, 50_000L),
                headOffice.Id, PostDate, branch.Id));
            Assert.Contains("ISD/2024-25/042", ex.Message, StringComparison.Ordinal);
        }
        finally { TempDbFile.Delete(dbPath); }
    }
}
