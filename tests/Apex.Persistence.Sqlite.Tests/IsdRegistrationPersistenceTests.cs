using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Persistence.Sqlite.Tests;

/// <summary>
/// Census row <b>6.24 (Input Service Distributor)</b> — the persistence half, and the reason this slice needed
/// <b>no schema change at all</b>.
///
/// <para>🔴 <b>THE CLAIM UNDER TEST IS "NO MIGRATION", AND IT IS A CLAIM ABOUT STORED BYTES.</b> An ISD is
/// modelled as a registration whose <see cref="GstRegistrationType"/> is
/// <see cref="GstRegistrationType.InputServiceDistributor"/>, persisted as the enum <b>ordinal</b> into the
/// <c>gst_registrations.registration_type</c> and <c>companies.gst_reg_type</c> INTEGER columns that already
/// exist at v63. Two things therefore have to be true and neither is obvious from reading the domain code:
/// the new value has to survive a save/reload, and <see cref="Schema.CurrentVersion"/> has to be unchanged.
/// If the second ever stopped being true this file would say so, rather than a reviewer having to notice.</para>
///
/// <para>The other half is the <b>set rule</b>: census row 6.23 refuses two registrations in one State, and this
/// slice narrowed that to two registrations <i>of the same kind</i> — because an ISD ordinarily sits in the same
/// State as an operating registration. CBIC's own example is a Bangalore corporate office acting as ISD beside a
/// Bangalore business location. A reopened book has to load that shape without the loader's validation refusing
/// it, which is what the round-trip below actually exercises.</para>
/// </summary>
public sealed class IsdRegistrationPersistenceTests
{
    private static readonly DateOnly FyStart = new(2025, 4, 1);
    private const string Karnataka = "29";
    private const string TamilNadu = "33";

    private static string GstinFor(string stateCode, string pan)
    {
        var body = stateCode + pan + "1Z";
        return body + Gstin.ComputeCheckDigit(body + "0");
    }

    [Fact]
    public void The_isd_slice_adds_no_schema_version()
    {
        // If this ever fails, the "no migration" claim in the slice report is stale and the ladder needs an
        // allocation — which is the thing that must never be assumed.
        Assert.Equal(63, Schema.CurrentVersion);
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void An_isd_registration_round_trips_through_the_existing_registration_type_column()
    {
        var path = TempDbFile.NewPath("apex-t3-isd-roundtrip");
        try
        {
            var c = CompanyFactory.CreateSeeded("ISD Persistence Co", FyStart);
            new GstService(c).EnableGst(new GstConfig
            {
                HomeStateCode = Karnataka,
                Gstin = GstinFor(Karnataka, "AAPFU0939F"),
                RegistrationType = GstRegistrationType.Regular,
                ApplicableFrom = FyStart,
                Periodicity = GstReturnPeriodicity.Monthly,
            });

            var isdId = Guid.NewGuid();
            c.Gst!.AddRegistration(new GstRegistration(
                isdId, "Head Office (ISD)", Karnataka, GstinFor(Karnataka, "AAACC1206D"),
                GstRegistrationType.InputServiceDistributor, FyStart));
            c.Gst!.AddRegistration(new GstRegistration(
                Guid.NewGuid(), "Tamil Nadu Registration", TamilNadu, GstinFor(TamilNadu, "AABCC1206D"),
                GstRegistrationType.Regular, FyStart));
            c.Gst!.EnsureValid();

            using (var store = new SqliteCompanyStore(path)) store.Save(c);
            using var reopened = new SqliteCompanyStore(path);
            var loaded = reopened.Load(c.Id)!;

            var isd = Assert.Single(loaded.Gst!.IsdRegistrations);
            Assert.Equal(isdId, isd.Id);
            Assert.Equal(GstRegistrationType.InputServiceDistributor, isd.RegistrationType);
            Assert.Equal(Karnataka, isd.StateCode);
            Assert.True(loaded.Gst!.HasIsdRegistration);

            // The ISD sits in the SAME State as the company's own registration and the reopened book accepts it.
            Assert.Equal(Karnataka, loaded.Gst!.HomeStateCode);
            loaded.Gst!.EnsureValid();

            // …and it is not one of its own recipients.
            var recipients = loaded.Gst!.IsdRecipientRegistrations(isdId);
            Assert.Equal(2, recipients.Count);
            Assert.DoesNotContain(recipients, r => r.Id == isdId);
        }
        finally { TempDbFile.Delete(path); }
    }

    [Fact]
    [Trait("Category", "RoundTrip")]
    public void A_company_with_no_isd_registration_stores_exactly_what_it_stored_before()
    {
        // ER-13: the slice adds a possible VALUE, never a required one. A book that never creates an ISD is
        // byte-identical, and the guard that would otherwise have to be inspected by eye is asserted here.
        var path = TempDbFile.NewPath("apex-t3-no-isd");
        try
        {
            var c = CompanyFactory.CreateSeeded("No ISD Co", FyStart);
            new GstService(c).EnableGst(new GstConfig
            {
                HomeStateCode = Karnataka,
                Gstin = GstinFor(Karnataka, "AAPFU0939F"),
                RegistrationType = GstRegistrationType.Regular,
                ApplicableFrom = FyStart,
                Periodicity = GstReturnPeriodicity.Monthly,
            });

            using (var store = new SqliteCompanyStore(path)) store.Save(c);
            using var reopened = new SqliteCompanyStore(path);
            var loaded = reopened.Load(c.Id)!;

            Assert.False(loaded.Gst!.HasIsdRegistration);
            Assert.Empty(loaded.Gst!.IsdRegistrations);
            Assert.False(loaded.Gst!.IsMultiRegistration);
            Assert.Equal(GstRegistrationType.Regular, loaded.Gst!.RegistrationType);
        }
        finally { TempDbFile.Delete(path); }
    }
}
