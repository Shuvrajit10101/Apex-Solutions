using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Apex.Persistence.Sqlite.Tests;

/// <summary>
/// Schema v54 → v55 (Ruling 16) — the <b>Karnataka Professional-Tax February back-fill</b>. The first version in
/// this schema that adds no DDL whatsoever: it exists only to correct <b>wrong money already persisted in existing
/// books</b>.
///
/// <para>📌 <b>It is v55, not the v54 ruling 16 named.</b> PR #62 (Credit Limits) had already taken and landed v54
/// on <c>origin/main</c> before the ruling was made, and two migrations cannot share one version number, so the
/// back-fill was renumbered on merge. Nothing about its behaviour, gating or citation changed.</para>
///
/// <para><b>The defect.</b> The seeded Karnataka PT top band carried a ₹300 February over-charge that no Karnataka
/// instrument grants. The state's SCHEDULE [See Section 3(2)] Sl. No. 1
/// (https://ptax.karnataka.gov.in/documents/pt%20amendment%20bill.pdf) reads in full: <i>"Salary or wage earners
/// whose salary or wage or both, as the case may be, for a month is Rs. 25,000-00 and above — Rs. 200-00 per
/// month"</i>. Statutory annual liability is 12 × ₹200 = ₹2,400; the application deducted ₹2,500. The seeding code
/// was corrected separately, but PT slab tables are seeded ONCE at enrolment
/// (<see cref="PayrollService.EnableProfessionalTax"/>) and thereafter persisted and user-editable, so that fix
/// reaches only companies enrolled after it. This migration reaches the rest.
///
/// <para>🔴 <b>What these tests are really guarding is the RESTRAINT, not the UPDATE.</b> <c>pt_slab_bands</c> has no
/// provenance column, so nothing distinguishes a seeded row from an operator-edited one except the shipped seed's
/// exact shape. Four of the tests below therefore assert that an EDITED Karnataka table is left completely alone —
/// an unconditional UPDATE would pass the "it fixes the bug" test and still be a wrong-money defect in the opposite
/// direction. A fifth asserts Maharashtra's identical <c>2:30000</c> override survives, because Maharashtra's
/// Schedule I grants it in terms and dropping the <c>state_code</c> predicate would silently UNDER-deduct there.</para>
///
/// <para>Every "genuine pre-fix v53 book" here is manufactured the honest way — a real store save, the defective
/// override written back into the row exactly as the old seed wrote it, then the FULL downgrade chain
/// <see cref="SchemaDowngrade.V55ToV54"/> → <see cref="SchemaDowngrade.V54ToV53"/> — so the migration runs against
/// real rows through the production store, and the book has to climb 53 → 54 → 55 to get back.</para>
///
/// <para>🔴 <b>The ladder order is itself a claim under test.</b>
/// <see cref="A_v53_book_climbs_the_whole_ladder_and_gets_BOTH_v54_and_v55"/> pins that a v53 book arrives carrying
/// the v54 credit-limit columns AND the v55 Karnataka correction, in that order, and comes back down to a genuine
/// v53 shape. A renumber that wired the back-fill in as a REPLACEMENT for the v54 rung rather than a rung above it
/// would pass every other test in this file and silently drop Credit Limits from every migrated book.</para>
/// </summary>
public sealed class KarnatakaPtBackfillSchemaTests
{
    private static readonly DateOnly FyStart = new(2025, 4, 1);

    private const string Karnataka = "29";
    private const string Maharashtra = "27";

    /// <summary>The Karnataka top band a pre-fix book holds: ₹200 flat with a ₹300 February over-charge.</summary>
    private const string BadFebruary = "2:30000";

    // ================================================================= the correction

    /// <summary>
    /// The core claim: a book enrolled for Karnataka BEFORE the seed fix opens on this build with the February
    /// over-charge gone — asserted both structurally (the band carries no override at all) and in money
    /// (₹200 in February, ₹2,400 over the financial year, not ₹2,500).
    /// </summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void A_pre_fix_v53_book_is_corrected_to_a_flat_200_and_a_2400_year()
    {
        var path = TempDbFile.NewPath("apex-kapt-correct");
        try
        {
            var companyId = MakePreFixV53Book(path, "KA Pre-Fix Co");

            // Precondition: the file really is a v53 book really carrying the defect.
            Assert.Equal(53L, ReadScalar(path, "SELECT version FROM schema_version LIMIT 1;"));
            Assert.Equal(1L, ReadScalar(path,
                $"SELECT COUNT(*) FROM pt_slab_bands WHERE state_code = '{Karnataka}' AND month_overrides = '{BadFebruary}';"));

            // Reopen through the production store — the v53 → v54 → v55 migration chain runs.
            using var reopened = new SqliteCompanyStore(path);
            Assert.Equal((long)Schema.CurrentVersion, ReadScalar(path, "SELECT version FROM schema_version LIMIT 1;"));
            Assert.Equal(55, Schema.CurrentVersion);

            var ka = KarnatakaSlab(reopened.Load(companyId)!);
            var top = ka.Bands[^1];

            // Structural: no override survives on the top band at all.
            Assert.Empty(top.MonthOverrides);
            Assert.Equal(200m, top.MonthlyAmount.Amount);
            Assert.Null(top.ToWage);

            // Money: February is ₹200 like every other month, and the year totals the statutory ₹2,400.
            Assert.Equal(200m, top.AmountForMonth(2).Amount);
            Assert.Equal(2400m, FinancialYearPt(ka, ptWages: 30000m));
        }
        finally { TempDbFile.Delete(path); }
    }

    /// <summary>The band the defect never touched — the ₹0 nil band — is not collateral damage.</summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void The_nil_band_is_untouched_by_the_correction()
    {
        var path = TempDbFile.NewPath("apex-kapt-nilband");
        try
        {
            var companyId = MakePreFixV53Book(path, "KA Nil Band Co");
            using var reopened = new SqliteCompanyStore(path);

            var ka = KarnatakaSlab(reopened.Load(companyId)!);
            Assert.Equal(2, ka.Bands.Count);
            Assert.Equal(0m, ka.Bands[0].FromWage.Amount);
            Assert.Equal(24999m, ka.Bands[0].ToWage!.Value.Amount);
            Assert.Equal(0m, ka.Bands[0].MonthlyAmount.Amount);
            Assert.Empty(ka.Bands[0].MonthOverrides);

            // A below-threshold Karnataka employee still pays nothing, in February as in every month.
            Assert.Equal(0m, ProfessionalTax.ComputeMonthly(ka, 24999m, 2, new Money(0m)).Amount);
        }
        finally { TempDbFile.Delete(path); }
    }

    // ================================================================= the restraint

    /// <summary>
    /// 🔴 The load-bearing test. A Karnataka table an operator has EDITED must survive the migration byte for byte —
    /// these rows are user-editable and carry no provenance marker, so the fingerprint is the only thing standing
    /// between "fix the bug" and "silently overwrite a deliberate figure". Each case below breaks one clause of the
    /// fingerprint; every one of them must leave the operator's ₹300 February exactly where they put it.
    /// </summary>
    [Theory]
    [Trait("Category", "RoundTrip")]
    // The operator moved the monthly figure off ₹200.
    [InlineData("monthly-amount", "UPDATE pt_slab_bands SET monthly_amount_paisa = 25000 WHERE state_code = '29' AND band_order = 1;")]
    // The operator moved the ₹25,000 threshold.
    [InlineData("threshold", "UPDATE pt_slab_bands SET from_wage_paisa = 2000000 WHERE state_code = '29' AND band_order = 1;")]
    // The operator edited the NIL band's ceiling (the fingerprint covers the whole table, not just the top band).
    [InlineData("nil-band-ceiling", "UPDATE pt_slab_bands SET to_wage_paisa = 1999900 WHERE state_code = '29' AND band_order = 0;")]
    // The operator closed the open-ended top band.
    [InlineData("closed-top-band", "UPDATE pt_slab_bands SET to_wage_paisa = 99999900 WHERE state_code = '29' AND band_order = 1;")]
    public void An_operator_edited_karnataka_table_survives_the_migration_untouched(string _, string operatorEdit)
    {
        var path = TempDbFile.NewPath("apex-kapt-edited");
        try
        {
            MakePreFixV53Book(path, "KA Edited Co", operatorEdit);

            using (new SqliteCompanyStore(path)) { }   // migration runs
            Assert.Equal((long)Schema.CurrentVersion, ReadScalar(path, "SELECT version FROM schema_version LIMIT 1;"));

            Assert.Equal(BadFebruary, ReadText(path,
                $"SELECT month_overrides FROM pt_slab_bands WHERE state_code = '{Karnataka}' AND band_order = 1;"));
        }
        finally { TempDbFile.Delete(path); }
    }

    /// <summary>
    /// The operator typed their OWN February figure (₹250, not the shipped ₹300). That is a deliberate value, not
    /// the application's mistake, and the migration must not touch it — nor may it "helpfully" clear any February
    /// override it finds on Karnataka.
    /// </summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void An_operator_chosen_february_figure_survives_the_migration()
    {
        var path = TempDbFile.NewPath("apex-kapt-ownfeb");
        try
        {
            MakePreFixV53Book(path, "KA Own Feb Co",
                $"UPDATE pt_slab_bands SET month_overrides = '2:25000' WHERE state_code = '{Karnataka}' AND band_order = 1;");

            using (new SqliteCompanyStore(path)) { }

            Assert.Equal("2:25000", ReadText(path,
                $"SELECT month_overrides FROM pt_slab_bands WHERE state_code = '{Karnataka}' AND band_order = 1;"));
        }
        finally { TempDbFile.Delete(path); }
    }

    /// <summary>An operator who added a third Karnataka band has a table this migration cannot recognise, so it
    /// leaves the whole thing alone (the <c>COUNT(*) = 2</c> clause).</summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void An_extra_operator_added_band_stops_the_migration_touching_the_table()
    {
        var path = TempDbFile.NewPath("apex-kapt-thirdband");
        try
        {
            var companyId = MakePreFixV53Book(path, "KA Third Band Co");
            var slabId = ReadText(path,
                $"SELECT slab_id FROM pt_slab_bands WHERE state_code = '{Karnataka}' LIMIT 1;");

            using (var conn = Open(path))
            {
                Exec(conn, $"""
                    INSERT INTO pt_slab_bands
                        (id, company_id, slab_id, state_code, gender_scope, band_order,
                         from_wage_paisa, to_wage_paisa, monthly_amount_paisa, month_overrides)
                    VALUES ('{Guid.NewGuid():D}', '{companyId:D}', '{slabId}', '{Karnataka}', 0, 2,
                            5000000, NULL, 30000, '');
                    """);
                SqliteConnection.ClearPool(conn);
            }

            using (new SqliteCompanyStore(path)) { }

            Assert.Equal(BadFebruary, ReadText(path,
                $"SELECT month_overrides FROM pt_slab_bands WHERE state_code = '{Karnataka}' AND band_order = 1;"));
        }
        finally { TempDbFile.Delete(path); }
    }

    /// <summary>
    /// 🔴 Maharashtra's ₹300 February is STATUTORY — its Schedule I says "two hundred per month except for the month
    /// of February ; three hundred for the month of February" — and it is stored as the identical
    /// <c>2:30000</c> string. Dropping the <c>state_code</c> predicate from the migration would silently
    /// under-deduct every Maharashtra employee, so both Maharashtra tables are pinned here.
    /// </summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Maharashtras_statutory_february_override_survives_the_migration()
    {
        var path = TempDbFile.NewPath("apex-kapt-mh");
        try
        {
            var companyId = MakePreFixV53Book(path, "MH Guard Co");

            var mhBefore = ReadScalar(path,
                $"SELECT COUNT(*) FROM pt_slab_bands WHERE state_code = '{Maharashtra}' AND month_overrides = '{BadFebruary}';");
            Assert.Equal(2L, mhBefore);   // MH men + MH women, one top band each

            using var reopened = new SqliteCompanyStore(path);
            Assert.Equal(mhBefore, ReadScalar(path,
                $"SELECT COUNT(*) FROM pt_slab_bands WHERE state_code = '{Maharashtra}' AND month_overrides = '{BadFebruary}';"));

            var company = reopened.Load(companyId)!;
            foreach (var mh in company.PtConfig!.SlabTables.Where(s => s.StateCode == Maharashtra))
            {
                var top = mh.Bands[^1];
                Assert.Equal(200m, top.MonthlyAmount.Amount);
                Assert.Equal(300m, top.AmountForMonth(2).Amount);
            }

            // And the Maharashtra year is still the ₹2,500 its own schedule prescribes.
            var mhMale = company.PtConfig!.SlabTables
                .Single(s => s.StateCode == Maharashtra && s.GenderScope == PtGenderScope.Male);
            Assert.Equal(2500m, FinancialYearPt(mhMale, ptWages: 30000m));
        }
        finally { TempDbFile.Delete(path); }
    }

    /// <summary>
    /// 🔴 The test that actually falsifies the <c>state_code = '29'</c> predicate. Maharashtra alone cannot do it:
    /// its tables are gender-scoped (Male/Female), so <c>gender_scope = 0</c> already excludes them and deleting the
    /// state clause leaves the Maharashtra assertions green — which is exactly what a mutation run showed. So this
    /// test stands up a gender-agnostic slab table for ANOTHER state carrying the Karnataka shape byte for byte
    /// (a realistic operator move: copy the Karnataka table when adding your own state), and pins that the migration
    /// leaves it alone. Karnataka's own table in the same book is corrected in the same pass, so the test cannot
    /// pass by the migration simply doing nothing.
    /// </summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void An_identically_shaped_table_for_another_state_is_not_touched()
    {
        var path = TempDbFile.NewPath("apex-kapt-otherstate");
        try
        {
            var companyId = MakePreFixV53Book(path, "KA Other State Co");

            // Tamil Nadu (33), gender scope 0, the Karnataka fingerprint copied exactly — February override included.
            var foreignSlab = Guid.NewGuid();
            using (var conn = Open(path))
            {
                Exec(conn, $"""
                    INSERT INTO pt_slab_bands
                        (id, company_id, slab_id, state_code, gender_scope, band_order,
                         from_wage_paisa, to_wage_paisa, monthly_amount_paisa, month_overrides)
                    VALUES ('{Guid.NewGuid():D}', '{companyId:D}', '{foreignSlab:D}', '33', 0, 0,
                            0, 2499900, 0, ''),
                           ('{Guid.NewGuid():D}', '{companyId:D}', '{foreignSlab:D}', '33', 0, 1,
                            2500000, NULL, 20000, '{BadFebruary}');
                    """);
                SqliteConnection.ClearPool(conn);
            }

            using (new SqliteCompanyStore(path)) { }

            // The foreign state keeps its override …
            Assert.Equal(BadFebruary, ReadText(path,
                $"SELECT month_overrides FROM pt_slab_bands WHERE slab_id = '{foreignSlab:D}' AND band_order = 1;"));
            // … while Karnataka, in the very same book and the very same pass, is corrected.
            Assert.Equal(string.Empty, ReadText(path,
                $"SELECT month_overrides FROM pt_slab_bands WHERE state_code = '{Karnataka}' AND band_order = 1;"));
        }
        finally { TempDbFile.Delete(path); }
    }

    /// <summary>West Bengal never had a February override and must not acquire or lose anything here.</summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void West_bengal_bands_are_untouched()
    {
        var path = TempDbFile.NewPath("apex-kapt-wb");
        try
        {
            MakePreFixV53Book(path, "WB Guard Co");
            const string wbBands =
                "SELECT band_order || ':' || month_overrides FROM pt_slab_bands WHERE state_code = '19' ORDER BY band_order;";
            var before = ReadRows(path, wbBands);
            Assert.NotEmpty(before);
            Assert.All(before, row => Assert.EndsWith(":", row, StringComparison.Ordinal));  // no override anywhere

            using (new SqliteCompanyStore(path)) { }

            Assert.Equal(before, ReadRows(path, wbBands));
        }
        finally { TempDbFile.Delete(path); }
    }

    /// <summary>
    /// 🔴 One database file can hold MORE THAN ONE company — <c>SqliteCompanyStore.Save</c> deletes and re-inserts
    /// only <c>DeleteCompanyRows(tx, company.Id)</c>, scoped by id. So the fingerprint must be evaluated per SLAB
    /// TABLE, not per file: a book where company A left the seed alone and company B edited theirs must come out of
    /// the migration with A corrected and B untouched. A migration that decided "this file looks edited, skip it" —
    /// or "this file looks seeded, rewrite it" — passes every single-company test above and fails here.
    /// </summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Two_companies_in_one_file_are_judged_separately()
    {
        var path = TempDbFile.NewPath("apex-kapt-twoco");
        try
        {
            var untouched = CompanyFactory.CreateSeeded("Untouched Co", FyStart);
            new PayrollService(untouched).EnableProfessionalTax(stateCode: Karnataka);
            var edited = CompanyFactory.CreateSeeded("Edited Co", FyStart);
            new PayrollService(edited).EnableProfessionalTax(stateCode: Karnataka);

            using (var store = new SqliteCompanyStore(path))
            {
                store.Save(untouched);
                store.Save(edited);
            }

            using (var conn = Open(path))
            {
                // Both books carry the pre-fix defect …
                Exec(conn, $"""
                    UPDATE pt_slab_bands SET month_overrides = '{BadFebruary}'
                    WHERE state_code = '{Karnataka}' AND band_order = 1;
                    """);
                // … but only "Edited Co" has an operator-moved figure.
                Exec(conn, $"""
                    UPDATE pt_slab_bands SET monthly_amount_paisa = 25000
                    WHERE state_code = '{Karnataka}' AND band_order = 1 AND company_id = '{edited.Id:D}';
                    """);
                SchemaDowngrade.V55ToV54(conn);
                SchemaDowngrade.V54ToV53(conn);
                SqliteConnection.ClearPool(conn);
            }
            Assert.Equal(2L, ReadScalar(path,
                $"SELECT COUNT(*) FROM pt_slab_bands WHERE state_code = '{Karnataka}' AND band_order = 1;"));

            using (new SqliteCompanyStore(path)) { }

            Assert.Equal(string.Empty, ReadText(path,
                $"SELECT month_overrides FROM pt_slab_bands WHERE band_order = 1 AND state_code = '{Karnataka}' AND company_id = '{untouched.Id:D}';"));
            Assert.Equal(BadFebruary, ReadText(path,
                $"SELECT month_overrides FROM pt_slab_bands WHERE band_order = 1 AND state_code = '{Karnataka}' AND company_id = '{edited.Id:D}';"));
        }
        finally { TempDbFile.Delete(path); }
    }

    // ================================================================= idempotence + shape

    /// <summary>
    /// Re-running the migration over an already-corrected book moves nothing (the <c>'2:30000'</c> predicate no
    /// longer matches), so an operator who downgrades and re-upgrades does not get the over-charge back.
    /// </summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Re_running_the_migration_over_a_corrected_book_is_a_no_op()
    {
        var path = TempDbFile.NewPath("apex-kapt-idempotent");
        try
        {
            MakePreFixV53Book(path, "KA Idempotent Co");
            using (new SqliteCompanyStore(path)) { }                       // first pass: corrects
            var afterFirst = AllPtBands(path);

            using (var conn = Open(path))
            {
                SchemaDowngrade.V55ToV54(conn);
                SchemaDowngrade.V54ToV53(conn);
                SqliteConnection.ClearPool(conn);
            }
            Assert.Equal(53L, ReadScalar(path, "SELECT version FROM schema_version LIMIT 1;"));

            using (new SqliteCompanyStore(path)) { }                       // second pass: must move nothing
            Assert.Equal((long)Schema.CurrentVersion, ReadScalar(path, "SELECT version FROM schema_version LIMIT 1;"));

            Assert.Equal(afterFirst, AllPtBands(path));
        }
        finally { TempDbFile.Delete(path); }
    }

    /// <summary>
    /// v55 adds NO DDL, so a v55 database and the same file after <see cref="SchemaDowngrade.V55ToV54"/> must have
    /// byte-identical schema — every table, index and column declaration. Asserted over the whole
    /// <c>sqlite_master</c> rather than one table, because "adds nothing" is the entire structural claim of this
    /// version and a single-table check could not falsify it. Note the file stops at <b>v54</b>, not v53: the
    /// credit-limit columns v54 added are still there, which is what makes this a one-rung step.
    /// </summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Downgrade_v55_to_v54_restores_the_v54_shape_and_keeps_the_correction()
    {
        var path = TempDbFile.NewPath("apex-kapt-downgrade");
        try
        {
            var companyId = MakePreFixV53Book(path, "KA Downgrade Co");
            using (new SqliteCompanyStore(path)) { }
            Assert.Equal((long)Schema.CurrentVersion, ReadScalar(path, "SELECT version FROM schema_version LIMIT 1;"));

            var schemaAtV55 = SchemaFingerprint(path);
            var bandsAtV55 = AllPtBands(path);
            var rowCountAtV55 = ReadScalar(path, "SELECT COUNT(*) FROM pt_slab_bands;");

            using (var conn = Open(path)) { SchemaDowngrade.V55ToV54(conn); SqliteConnection.ClearPool(conn); }

            Assert.Equal(54L, ReadScalar(path, "SELECT version FROM schema_version LIMIT 1;"));
            Assert.Equal(schemaAtV55, SchemaFingerprint(path));                       // no DDL was added, so none is removed
            Assert.Equal(rowCountAtV55, ReadScalar(path, "SELECT COUNT(*) FROM pt_slab_bands;"));

            // v55 sits ABOVE v54, so stepping down one rung must leave the v54 credit-limit columns in place.
            var ledgerColumns = ColumnNames(path, "ledgers");
            foreach (var col in Schema.V54CreditLimitColumns) Assert.Contains(col, ledgerColumns);

            // 🔴 The correction deliberately SURVIVES the downgrade. Restoring a ₹300 February the state never
            // levied, for the sake of symmetry, would re-open the defect in every book that round-trips.
            Assert.Equal(bandsAtV55, AllPtBands(path));
            Assert.Equal(0L, ReadScalar(path,
                $"SELECT COUNT(*) FROM pt_slab_bands WHERE state_code = '{Karnataka}' AND month_overrides = '{BadFebruary}';"));

            // And the downgraded file is still readable by the migration chain, which finds nothing to do.
            using var reopened = new SqliteCompanyStore(path);
            Assert.Empty(KarnatakaSlab(reopened.Load(companyId)!).Bands[^1].MonthOverrides);
        }
        finally { TempDbFile.Delete(path); }
    }

    // ================================================================= the ladder

    /// <summary>
    /// 🔴 <b>The renumber's own test.</b> User ruling 16 named this back-fill v54; PR #62 had already landed Credit
    /// Limits on v54, so the back-fill became v55 and had to be wired in as a rung ABOVE v54 rather than in place of
    /// it. This test is what separates those two outcomes.
    ///
    /// <para>A genuine v53 book — carrying the bad Karnataka row and NOT carrying the credit-limit columns — is
    /// driven up the whole ladder through the production store, and BOTH effects are asserted at the top:
    /// the three v54 <c>ledgers</c> columns exist, and the v55 Karnataka figure is corrected. A renumber that
    /// replaced the v54 rung would leave the columns missing and the store would still report v55; a renumber that
    /// never ran the new rung would leave the ₹300 in place. Then it comes back DOWN both rungs and the v53 shape is
    /// re-asserted, so the chain is proved in both directions.</para>
    /// </summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void A_v53_book_climbs_the_whole_ladder_and_gets_BOTH_v54_and_v55()
    {
        var path = TempDbFile.NewPath("apex-kapt-ladder");
        try
        {
            var companyId = MakePreFixV53Book(path, "KA Ladder Co");

            // Precondition: a REAL v53 book — no credit-limit columns, and the defect present.
            Assert.Equal(53L, ReadScalar(path, "SELECT version FROM schema_version LIMIT 1;"));
            var atV53 = ColumnNames(path, "ledgers");
            foreach (var col in Schema.V54CreditLimitColumns) Assert.DoesNotContain(col, atV53);
            Assert.Equal(1L, ReadScalar(path,
                $"SELECT COUNT(*) FROM pt_slab_bands WHERE state_code = '{Karnataka}' AND month_overrides = '{BadFebruary}';"));

            // 53 → 54 → 55, through the production store's own ladder.
            using (var reopened = new SqliteCompanyStore(path))
            {
                Assert.Equal(55L, ReadScalar(path, "SELECT version FROM schema_version LIMIT 1;"));

                // v54's effect: the three credit-limit columns exist, and read as "no limit / both flags off".
                var atV55 = ColumnNames(path, "ledgers");
                foreach (var col in Schema.V54CreditLimitColumns) Assert.Contains(col, atV55);
                Assert.Equal(0L, ReadScalar(path, "SELECT COUNT(*) FROM ledgers WHERE credit_limit_paisa IS NOT NULL;"));
                Assert.Equal(0L, ReadScalar(path,
                    "SELECT COUNT(*) FROM ledgers WHERE check_credit_days_on_entry <> 0 OR override_credit_limit_post_dated <> 0;"));

                // v55's effect: the Karnataka February over-charge is gone, in structure and in money.
                var ka = KarnatakaSlab(reopened.Load(companyId)!);
                Assert.Empty(ka.Bands[^1].MonthOverrides);
                Assert.Equal(200m, ka.Bands[^1].AmountForMonth(2).Amount);
                Assert.Equal(2400m, FinancialYearPt(ka, ptWages: 30000m));
            }

            // … and back DOWN both rungs, one at a time, to a genuine v53 shape again.
            using (var conn = Open(path))
            {
                SchemaDowngrade.V55ToV54(conn);
                Assert.Equal(54L, ScalarOn(conn, "SELECT version FROM schema_version LIMIT 1;"));
                SchemaDowngrade.V54ToV53(conn);
                Assert.Equal(53L, ScalarOn(conn, "SELECT version FROM schema_version LIMIT 1;"));
                SqliteConnection.ClearPool(conn);
            }
            var backAtV53 = ColumnNames(path, "ledgers");
            foreach (var col in Schema.V54CreditLimitColumns) Assert.DoesNotContain(col, backAtV53);
        }
        finally { TempDbFile.Delete(path); }
    }

    /// <summary>
    /// The fingerprint the migration gates on is named in <see cref="Schema"/> so the SQL, the downgrade and these
    /// tests cannot drift apart. This pins that the three copies still agree: the constants this file asserts with,
    /// the constants <c>Schema</c> publishes, and the literals actually inside the shipped migration SQL. Without
    /// it, a future edit to the SQL's <c>'29'</c> or <c>'2:30000'</c> would leave every test above green and
    /// testing the wrong shape.
    /// </summary>
    [Fact]
    public void The_fingerprint_constants_agree_with_the_shipped_migration_sql()
    {
        Assert.Equal(Schema.V55KarnatakaStateCode, Karnataka);
        Assert.Equal(Schema.V55KarnatakaBadFebruaryOverride, BadFebruary);
        Assert.Contains($"state_code = '{Schema.V55KarnatakaStateCode}'", Schema.MigrateV54ToV55, StringComparison.Ordinal);
        Assert.Contains($"month_overrides = '{Schema.V55KarnatakaBadFebruaryOverride}'", Schema.MigrateV54ToV55, StringComparison.Ordinal);
    }

    /// <summary>
    /// A company with no PT enrolment at all has no <c>pt_slab_bands</c> rows (ER-13), and the migration must be a
    /// clean no-op on it rather than throwing on an empty table.
    /// </summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void A_book_with_no_pt_enrolment_migrates_cleanly()
    {
        var path = TempDbFile.NewPath("apex-kapt-nopt");
        try
        {
            var company = CompanyFactory.CreateSeeded("No PT Co", FyStart);
            using (var store = new SqliteCompanyStore(path)) store.Save(company);
            using (var conn = Open(path))
            {
                SchemaDowngrade.V55ToV54(conn);
                SchemaDowngrade.V54ToV53(conn);
                SqliteConnection.ClearPool(conn);
            }

            Assert.Equal(0L, ReadScalar(path, "SELECT COUNT(*) FROM pt_slab_bands;"));

            using var reopened = new SqliteCompanyStore(path);
            Assert.Equal((long)Schema.CurrentVersion, ReadScalar(path, "SELECT version FROM schema_version LIMIT 1;"));
            Assert.Null(reopened.Load(company.Id)!.PtConfig);
        }
        finally { TempDbFile.Delete(path); }
    }

    // ================================================================= helpers

    /// <summary>
    /// A genuine pre-fix v53 book: a real company enrolled for Karnataka, saved through the production store, with
    /// the defective ₹300 February written back onto the Karnataka top band exactly as the old seed wrote it, and
    /// the file stamped back to v53. <paramref name="operatorEdit"/> (optional) is applied afterwards to stand in
    /// for a deliberate operator change.
    /// </summary>
    private static Guid MakePreFixV53Book(string path, string name, string? operatorEdit = null)
    {
        var company = CompanyFactory.CreateSeeded(name, FyStart);
        new PayrollService(company).EnableProfessionalTax(stateCode: Karnataka);
        using (var store = new SqliteCompanyStore(path)) store.Save(company);

        using (var conn = Open(path))
        {
            Exec(conn, $"""
                UPDATE pt_slab_bands SET month_overrides = '{BadFebruary}'
                WHERE state_code = '{Karnataka}' AND band_order = 1;
                """);
            if (operatorEdit is not null) Exec(conn, operatorEdit);
            // The FULL chain down. v55 first (it is the top rung), then v54 — skipping either would stamp the
            // marker past a rung the forward climb then re-runs against a file that was never un-done.
            SchemaDowngrade.V55ToV54(conn);
            SchemaDowngrade.V54ToV53(conn);
            SqliteConnection.ClearPool(conn);
        }
        return company.Id;
    }

    private static PtSlab KarnatakaSlab(Company company) =>
        company.PtConfig!.SlabTables.Single(s => s.StateCode == Karnataka);

    /// <summary>Total PT charged over the Apr–Mar financial year for <paramref name="ptWages"/>, cap applied month
    /// by month exactly as payroll runs it — the figure the defect actually moved.</summary>
    private static decimal FinancialYearPt(PtSlab slab, decimal ptWages)
    {
        var cumulative = new Money(0m);
        foreach (var month in new[] { 4, 5, 6, 7, 8, 9, 10, 11, 12, 1, 2, 3 })
            cumulative = new Money(cumulative.Amount + ProfessionalTax.ComputeMonthly(slab, ptWages, month, cumulative).Amount);
        return cumulative.Amount;
    }

    /// <summary>Every PT band row as one ordered, culture-free string — the comparison an idempotence or
    /// downgrade claim needs (any moved figure changes it). Rows are ordered and joined in C# rather than by
    /// <c>group_concat</c>, whose ordering SQLite does not contract.</summary>
    private static string AllPtBands(string path) => string.Join('|', ReadRows(path, """
        SELECT state_code || ':' || gender_scope || ':' || band_order || ':' || from_wage_paisa || ':' ||
               COALESCE(CAST(to_wage_paisa AS TEXT), 'null') || ':' || monthly_amount_paisa || ':' ||
               month_overrides
        FROM pt_slab_bands
        ORDER BY state_code, gender_scope, band_order;
        """));

    /// <summary>Every CREATE statement SQLite holds, ordered — the whole-schema shape a "this version adds no DDL"
    /// claim has to be tested against.</summary>
    private static string SchemaFingerprint(string path) => string.Join(";\n", ReadRows(path,
        "SELECT sql FROM sqlite_master WHERE sql IS NOT NULL ORDER BY type, name;"));

    /// <summary>The column names of <paramref name="table"/>, from <c>PRAGMA table_info</c> — how the ladder test
    /// tells a v53 <c>ledgers</c> (no credit-limit columns) from a v54/v55 one.</summary>
    private static List<string> ColumnNames(string path, string table) =>
        ReadRows(path, $"SELECT name FROM pragma_table_info('{table}');");

    /// <summary>The first column of every row, in the query's own ORDER BY.</summary>
    private static List<string> ReadRows(string path, string sql)
    {
        using var conn = Open(path);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var rows = new List<string>();
        using (var r = cmd.ExecuteReader())
            while (r.Read()) rows.Add(r.IsDBNull(0) ? string.Empty : r.GetString(0));
        SqliteConnection.ClearPool(conn);
        return rows;
    }

    private static SqliteConnection Open(string path)
    {
        var conn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString());
        conn.Open();
        return conn;
    }

    private static void Exec(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    /// <summary><see cref="ReadScalar"/> against an ALREADY-OPEN connection — needed between two downgrade steps,
    /// where opening a second connection to the same file would read across the first one's transaction state.
    /// </summary>
    private static long ScalarOn(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt64(cmd.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static long ReadScalar(string path, string sql)
    {
        using var conn = Open(path);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var v = cmd.ExecuteScalar();
        SqliteConnection.ClearPool(conn);
        return Convert.ToInt64(v, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string ReadText(string path, string sql)
    {
        using var conn = Open(path);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var v = cmd.ExecuteScalar();
        SqliteConnection.ClearPool(conn);
        return v is null or DBNull ? string.Empty : Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture)!;
    }
}
