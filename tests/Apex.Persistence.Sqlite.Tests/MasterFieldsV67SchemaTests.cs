using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Microsoft.Data.Sqlite;
using Xunit;
using Domain = Apex.Ledger.Domain;

namespace Apex.Persistence.Sqlite.Tests;

/// <summary>
/// Schema v66 → v67 (census rows <b>2.4</b> Voucher Type master, <b>2.7</b> Cost Category master, <b>2.9</b>
/// Budget master, <b>2.11</b> Currency master, <b>3.7</b> Godown master — cluster C2): twenty-two columns across
/// six tables in ONE migration.
///
/// <para>Older databases are manufactured by walking the ladder DOWN from current with
/// <see cref="SchemaDowngrade"/> rather than from hand-written DDL, so the migration is exercised against real
/// rows — the idiom every schema test in this project uses.</para>
///
/// <para>🔴 <b>WHAT THIS FILE CHECKS BEYOND "THE COLUMNS EXIST", BECAUSE COLUMN-EXISTENCE PROVES NOTHING.</b>
/// <list type="number">
///   <item><b>The migrated contract equals the fresh one</b>, column for column, on ALL SIX tables.</item>
///   <item><b>Every one of the six keeps its PRIMARY KEY across the downgrade.</b> Five are FK PARENTS, and a
///   <c>CREATE … AS SELECT</c> rebuild of a parent silently loses the PK — the <c>foreign key mismatch</c>
///   failure <c>V56ToV55</c> records. This is why <c>V67ToV66</c> uses <c>RebuildPreservingShape</c> six
///   times.</item>
///   <item><b>The migration moves NOTHING.</b> v67 has no <c>UPDATE</c> at all, and a pre-v67 book climbs the
///   rung with every godown still storing materials, every flag off and every text field unset.</item>
///   <item>🔴 <b>Every value actually ROUND-TRIPS through the production store, read back off the RELOADED
///   book.</b> A column nothing reads or writes is dead schema, and that is exactly what a migration-parity
///   test cannot see.</item>
///   <item>🔴 <b>Census row 2.10 (Scenario period) is asserted ABSENT.</b> See
///   <see cref="The_scenario_master_gains_no_period_because_the_vendor_screen_has_none"/> — this is the one
///   clause in the brief that the vendor documentation answered AGAINST the census, and the absence is pinned
///   so a later wave cannot quietly add an unsourced field.</item>
/// </list></para>
/// </summary>
public sealed class MasterFieldsV67SchemaTests
{
    private static readonly DateOnly FyStart = new(2024, 4, 1);

    private static readonly string[] V67Tables =
        ["voucher_types", "cost_categories", "budget_lines", "currencies", "godowns", "companies"];

    // ================================================================= migration parity

    /// <summary>
    /// All six v67 tables end up byte-identical in contract (name / type / NOT NULL / DEFAULT / PK on every
    /// column) to what <c>CreateV1</c> gives a fresh book. A DEFAULT drifting between an <c>ALTER</c> and
    /// <see cref="Schema.CreateV1"/> is caught here with a readable message — including the one DEFAULT in this
    /// migration that is NOT zero, <c>godowns.allow_storage_of_materials DEFAULT 1</c>.
    /// </summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void Migration_v66_to_v67_matches_CreateV1_on_all_six_tables()
    {
        var migratedPath = TempDbFile.NewPath("apex-v67-migrated");
        var freshPath = TempDbFile.NewPath("apex-v67-fresh");
        try
        {
            using (var store = new SqliteCompanyStore(freshPath))
                store.Save(CompanyFactory.CreateSeeded("Fresh Master Co", FyStart));
            using (var store = new SqliteCompanyStore(migratedPath))
                store.Save(CompanyFactory.CreateSeeded("Legacy Master Co", FyStart));

            // ONE rung only — this test is about the v66 → v67 step in isolation.
            using (var conn = Open(migratedPath)) { SchemaDowngrade.V67ToV66(conn); SqliteConnection.ClearPool(conn); }
            Assert.Equal(66L, ReadScalar(migratedPath, "SELECT version FROM schema_version LIMIT 1;"));

            // The downgraded book genuinely lacks all twenty-two — otherwise the "migration" below proves nothing.
            AssertAbsent(migratedPath, "voucher_types", Schema.V67VoucherTypeColumns);
            AssertAbsent(migratedPath, "cost_categories", Schema.V67CostCategoryColumns);
            AssertAbsent(migratedPath, "budget_lines", Schema.V67BudgetLineColumns);
            AssertAbsent(migratedPath, "currencies", Schema.V67CurrencyColumns);
            AssertAbsent(migratedPath, "godowns", Schema.V67GodownColumns);
            AssertAbsent(migratedPath, "companies", Schema.V67CompanyColumns);

            // Reopen through the production store — the v66 → v67 migration runs.
            using (new SqliteCompanyStore(migratedPath)) { }
            Assert.Equal((long)Schema.CurrentVersion,
                ReadScalar(migratedPath, "SELECT version FROM schema_version LIMIT 1;"));

            foreach (var table in V67Tables)
                Assert.Equal(TableInfo(freshPath, table), TableInfo(migratedPath, table));

            // 🔴 The one non-zero DEFAULT in the whole migration, pinned by VALUE rather than only by equality,
            // because "the two copies agree" would still pass if BOTH had drifted to 0 — and 0 would mean every
            // upgraded godown silently stopped being allowed to store materials.
            Assert.Equal("1", DefaultOf(migratedPath, "godowns", "allow_storage_of_materials"));
            Assert.Equal("1", DefaultOf(freshPath, "godowns", "allow_storage_of_materials"));
        }
        finally
        {
            TempDbFile.Delete(migratedPath);
            TempDbFile.Delete(freshPath);
        }
    }

    /// <summary>
    /// 🔴 <b>FIVE OF THE SIX v67 TABLES ARE FK PARENTS</b>, so the downgrade must rebuild each with
    /// <c>RebuildPreservingShape</c> and never with a <c>CREATE … AS SELECT</c>, which silently loses the
    /// PRIMARY KEY. This asserts every PK survives the round trip, that no FK anywhere in the file is left
    /// dangling, that each table's own index survived the rebuild, and that the file climbs back up clean.
    /// </summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void The_v67_downgrade_preserves_every_primary_key_index_and_foreign_key()
    {
        var path = TempDbFile.NewPath("apex-v67-fk");
        try
        {
            using (var store = new SqliteCompanyStore(path))
                store.Save(CompanyFactory.CreateSeeded("FK Master Co", FyStart));

            var indexesBefore = V67Tables.ToDictionary(t => t, t => IndexNames(path, t).OrderBy(n => n).ToList());

            using (var conn = Open(path)) { SchemaDowngrade.V67ToV66(conn); SqliteConnection.ClearPool(conn); }

            foreach (var table in V67Tables)
                Assert.Equal(new List<string> { "id" }, PrimaryKeyColumns(path, table));

            // 🔴 DROP TABLE drops every index with it, and RebuildPreservingShape replays them from
            // sqlite_master. Losing one is a silent performance regression a PK assertion cannot see.
            foreach (var table in V67Tables)
                Assert.Equal(indexesBefore[table], IndexNames(path, table).OrderBy(n => n).ToList());

            Assert.Equal(0, ForeignKeyViolationCount(path));
            Assert.Equal("ok", IntegrityCheck(path));

            using (new SqliteCompanyStore(path)) { }
            Assert.Equal((long)Schema.CurrentVersion,
                ReadScalar(path, "SELECT version FROM schema_version LIMIT 1;"));
            Assert.Equal(0, ForeignKeyViolationCount(path));
        }
        finally { TempDbFile.Delete(path); }
    }

    /// <summary>
    /// 🔴 <b>ER-13 — THE MIGRATION MOVES NOTHING, AND THAT IS ASSERTED ON THE ROWS RATHER THAN ONLY ON THE
    /// ABSENCE OF AN <c>UPDATE</c> IN THE SQL.</b> A pre-v67 book climbs the rung with every voucher type's
    /// eleven new fields off/unset, every cost category without an alias, every currency on the pre-v67
    /// formatting, <b>every godown still allowed to store materials</b>, and both F12 switches off — which is
    /// exactly what the Godown Creation screen showed before.
    /// </summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void A_pre_v67_book_climbs_the_rung_with_every_master_on_the_vendor_default()
    {
        var path = TempDbFile.NewPath("apex-v67-inert");
        try
        {
            var company = CompanyFactory.CreateSeeded("Inert Master Co", FyStart);
            using (var store = new SqliteCompanyStore(path)) store.Save(company);

            var typesBefore = ReadScalar(path, "SELECT COUNT(*) FROM voucher_types;");
            var catsBefore = ReadScalar(path, "SELECT COUNT(*) FROM cost_categories;");
            var godownsBefore = ReadScalar(path, "SELECT COUNT(*) FROM godowns;");
            Assert.True(typesBefore > 0 && catsBefore > 0 && godownsBefore > 0,
                "the seeded book is missing one of the three masters, so this test proves nothing");

            using (var conn = Open(path)) { SchemaDowngrade.V67ToV66(conn); SqliteConnection.ClearPool(conn); }
            using (new SqliteCompanyStore(path)) { }   // climbs v66 → v67

            Assert.Equal(typesBefore, ReadScalar(path, "SELECT COUNT(*) FROM voucher_types;"));
            Assert.Equal(catsBefore, ReadScalar(path, "SELECT COUNT(*) FROM cost_categories;"));
            Assert.Equal(godownsBefore, ReadScalar(path, "SELECT COUNT(*) FROM godowns;"));

            Assert.Equal(0L, ReadScalar(path,
                "SELECT COUNT(*) FROM voucher_types WHERE use_effective_dates <> 0 OR optional_by_default <> 0 "
                + "OR default_accounting_allocations <> 0 OR whatsapp_after_saving <> 0 "
                + "OR generate_payment_link_after_saving <> 0 OR default_print_template IS NOT NULL "
                + "OR default_print_title IS NOT NULL OR default_bank_ledger_id IS NOT NULL "
                + "OR default_jurisdiction IS NOT NULL OR print_declaration IS NOT NULL "
                + "OR print_terms_and_conditions IS NOT NULL;"));
            Assert.Equal(0L, ReadScalar(path, "SELECT COUNT(*) FROM cost_categories WHERE alias IS NOT NULL;"));
            Assert.Equal(0L, ReadScalar(path, "SELECT COUNT(*) FROM budget_lines WHERE cost_centre_id IS NOT NULL;"));
            Assert.Equal(0L, ReadScalar(path,
                "SELECT COUNT(*) FROM currencies WHERE show_amounts_in_millions <> 0 OR suffix_symbol_to_amount <> 0 "
                + "OR space_between_amount_and_symbol <> 0 OR decimal_word IS NOT NULL "
                + "OR decimal_places_for_words IS NOT NULL;"));
            // 🔴 THE ONE THAT MUST BE 1, NOT 0 — the vendor's "By default, each godown can store materials".
            Assert.Equal(0L, ReadScalar(path, "SELECT COUNT(*) FROM godowns WHERE allow_storage_of_materials <> 1;"));
            Assert.Equal(0L, ReadScalar(path, "SELECT COUNT(*) FROM godowns WHERE address IS NOT NULL;"));
            Assert.Equal(0L, ReadScalar(path,
                "SELECT COUNT(*) FROM companies WHERE config_allow_storage_of_materials <> 0 "
                + "OR config_use_address_for_locations <> 0;"));

            using var reopened = new SqliteCompanyStore(path);
            var back = reopened.Load(company.Id)!;
            Assert.All(back.VoucherTypes, t => Assert.False(t.UseEffectiveDates));
            Assert.All(back.CostCategories, c => Assert.Null(c.Alias));
            Assert.All(back.Godowns, g => Assert.True(g.AllowStorageOfMaterials));
            Assert.False(back.ConfigAllowStorageOfMaterials);
            Assert.False(back.ConfigUseAddressForLocations);
        }
        finally { TempDbFile.Delete(path); }
    }

    // ================================================================= 🔴 the round trips

    /// <summary>
    /// Census <b>2.4</b>: all eleven attested Voucher Type fields round-trip through the production store, read
    /// back off the RELOADED book. Captions are vendor-verbatim from
    /// <c>help.tallysolutions.com/voucher-types-tally/</c>; before v67 every one was a zero-hit search over
    /// <c>src/</c>.
    /// </summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void All_eleven_voucher_type_fields_round_trip()
    {
        var path = TempDbFile.NewPath("apex-v67-vt");
        try
        {
            var company = CompanyFactory.CreateSeeded("VT Co", FyStart);
            var bank = company.Ledgers.First();
            var type = new VoucherType(Guid.NewGuid(), "Export Invoice", VoucherBaseType.Sales)
            {
                UseEffectiveDates = true,
                OptionalByDefault = true,
                DefaultAccountingAllocations = true,
                WhatsAppAfterSaving = true,
                GeneratePaymentLinkAfterSaving = true,
                DefaultPrintTemplate = "Simple Invoice",
                DefaultPrintTitle = "TAX INVOICE / EXPORT",
                DefaultBankLedgerId = bank.Id,
                DefaultJurisdiction = "Bengaluru",
                PrintDeclaration = "We declare that this invoice shows the actual price of the goods described.",
                PrintTermsAndConditions = "Payment within 30 days. Interest at 18% p.a. thereafter.",
            };
            company.AddVoucherType(type);
            using (var store = new SqliteCompanyStore(path)) store.Save(company);

            using var reopened = new SqliteCompanyStore(path);
            var back = reopened.Load(company.Id)!.VoucherTypes.Single(t => t.Id == type.Id);

            Assert.True(back.UseEffectiveDates);
            Assert.True(back.OptionalByDefault);
            Assert.True(back.DefaultAccountingAllocations);
            Assert.True(back.WhatsAppAfterSaving);
            Assert.True(back.GeneratePaymentLinkAfterSaving);
            Assert.Equal("Simple Invoice", back.DefaultPrintTemplate);
            Assert.Equal("TAX INVOICE / EXPORT", back.DefaultPrintTitle);
            Assert.Equal(bank.Id, back.DefaultBankLedgerId);
            Assert.Equal("Bengaluru", back.DefaultJurisdiction);
            Assert.StartsWith("We declare that", back.PrintDeclaration);
            Assert.StartsWith("Payment within 30 days", back.PrintTermsAndConditions);

            // 🔴 And the SIBLING types are untouched: a writer that bound the same parameter for every row, or a
            // reader off by one column, would otherwise light every seeded type up with these values.
            Assert.All(reopened.Load(company.Id)!.VoucherTypes.Where(t => t.Id != type.Id),
                t =>
                {
                    Assert.False(t.UseEffectiveDates);
                    Assert.Null(t.DefaultPrintTitle);
                    Assert.Null(t.DefaultBankLedgerId);
                });
        }
        finally { TempDbFile.Delete(path); }
    }

    /// <summary>
    /// Census <b>2.7</b>: the Cost CATEGORY alias round-trips. <i>"Name &amp; alias: As in other masters, you
    /// can specify multiple aliases"</i> — <c>help.tallysolutions.com/cost-centre-or-profit-centre-tally/</c>,
    /// in the Cost Category section. This is the field the row was DEMOTED for on 2026-10-04.
    /// </summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void The_cost_category_alias_round_trips()
    {
        var path = TempDbFile.NewPath("apex-v67-cc");
        try
        {
            var company = CompanyFactory.CreateSeeded("CostCat Co", FyStart);
            var category = new CostCategory(Guid.NewGuid(), "Departments") { Alias = "DEPT" };
            company.AddCostCategory(category);
            using (var store = new SqliteCompanyStore(path)) store.Save(company);

            using var reopened = new SqliteCompanyStore(path);
            var loaded = reopened.Load(company.Id)!;
            Assert.Equal("DEPT", loaded.CostCategories.Single(c => c.Id == category.Id).Alias);
            // The seeded Primary Cost Category keeps a NULL alias — the column is per-row, not a constant.
            Assert.All(loaded.CostCategories.Where(c => c.Id != category.Id), c => Assert.Null(c.Alias));
        }
        finally { TempDbFile.Delete(path); }
    }

    /// <summary>
    /// Census <b>2.9</b>: a budget line that targets a COST CENTRE round-trips, and comes back as a cost-centre
    /// target rather than as a group or ledger one. <c>help.tallysolutions.com/budgets-tally/</c>: <i>"Set Cost
    /// Centres to Yes"</i>, <i>"In the Cost Centre Budget screen, select the required Cost Centre, Expenses,
    /// Income and Closing Balance."</i>
    /// </summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void A_cost_centre_budget_line_round_trips_and_stays_a_cost_centre_target()
    {
        var path = TempDbFile.NewPath("apex-v67-budget");
        try
        {
            var company = CompanyFactory.CreateSeeded("Budget Co", FyStart);
            var centre = new CostCentre(Guid.NewGuid(), "Marketing", company.CostCategories.First().Id);
            company.AddCostCentre(centre);

            var budget = new Budget(Guid.NewGuid(), "FY Departmental", FyStart, FyStart.AddYears(1).AddDays(-1));
            budget.AddLine(BudgetLine.ForCostCentre(centre.Id, BudgetType.OnNettTransactions, new Money(45_000m)));
            budget.AddLine(BudgetLine.ForLedger(company.Ledgers.First().Id, BudgetType.OnClosingBalance, new Money(10_000m)));
            company.AddBudget(budget);

            using (var store = new SqliteCompanyStore(path)) store.Save(company);

            using var reopened = new SqliteCompanyStore(path);
            var back = reopened.Load(company.Id)!.Budgets.Single(b => b.Id == budget.Id);

            var ccLine = back.Lines.Single(l => l.IsCostCentreTarget);
            Assert.Equal(centre.Id, ccLine.CostCentreId);
            Assert.Null(ccLine.GroupId);
            Assert.Null(ccLine.LedgerId);
            Assert.Equal(BudgetType.OnNettTransactions, ccLine.Type);
            Assert.Equal(45_000m, ccLine.Amount.Amount);

            // …and the ledger line beside it did NOT become a cost-centre line.
            var ledgerLine = back.Lines.Single(l => l.IsLedgerTarget);
            Assert.Null(ledgerLine.CostCentreId);
        }
        finally { TempDbFile.Delete(path); }
    }

    /// <summary>
    /// Census <b>2.9</b> / the delete guard: a cost centre a budget line targets cannot be deleted. Without the
    /// guard this is a permitted delete that makes the open company UNSAVABLE on the very next Save, because
    /// <c>budget_lines</c> is written from <c>Company.Budgets</c> and does not leave with the centre.
    /// </summary>
    [Fact]
    public void A_cost_centre_a_budget_line_targets_cannot_be_deleted()
    {
        var company = CompanyFactory.CreateSeeded("Guard Co", FyStart);
        var centre = new CostCentre(Guid.NewGuid(), "Marketing", company.CostCategories.First().Id);
        company.AddCostCentre(centre);

        // Deletable before the budget names it — otherwise the refusal below could be for any other reason.
        MasterDeletionRules.EnsureCostCentreDeletable(company, centre);

        var budget = new Budget(Guid.NewGuid(), "FY Departmental", FyStart, FyStart.AddYears(1).AddDays(-1));
        budget.AddLine(BudgetLine.ForCostCentre(centre.Id, BudgetType.OnNettTransactions, new Money(45_000m)));
        company.AddBudget(budget);

        var ex = Assert.Throws<InvalidOperationException>(
            () => { MasterDeletionRules.EnsureCostCentreDeletable(company, centre); });
        Assert.Contains("budget line", ex.Message);
    }

    /// <summary>
    /// Census <b>2.4</b> / the delete guard: a ledger named as a voucher type's <i>"Default bank"</i> cannot be
    /// deleted. Same mechanism as the cost-centre guard above — a voucher type is written from
    /// <c>Company.VoucherTypes</c> and does not leave with the ledger.
    /// </summary>
    [Fact]
    public void A_ledger_named_as_a_voucher_types_default_bank_cannot_be_deleted()
    {
        var company = CompanyFactory.CreateSeeded("Guard Bank Co", FyStart);
        var group = company.Groups.First(g => !MasterAlterationRules.WellKnownLedgerNames.Contains(g.Name));
        var bank = new Domain.Ledger(Guid.NewGuid(), "HDFC Current A/c", group.Id, Money.Zero, openingIsDebit: true);
        company.AddLedger(bank);

        MasterDeletionRules.EnsureLedgerDeletable(company, bank);

        company.AddVoucherType(new VoucherType(Guid.NewGuid(), "Bank Receipt", VoucherBaseType.Receipt)
        {
            DefaultBankLedgerId = bank.Id,
        });

        var ex = Assert.Throws<InvalidOperationException>(
            () => { MasterDeletionRules.EnsureLedgerDeletable(company, bank); });
        Assert.Contains("default bank", ex.Message);
    }

    /// <summary>
    /// Census <b>2.11</b>: all five new currency formatting options round-trip. Captions verbatim from
    /// <c>help.tallysolutions.com/create-alter-or-delete-currencies/</c>. 🔴 The row's cell recorded FOUR
    /// options and a 404'd page; there are SIX and the sixth is the pre-existing <c>DecimalPlaces</c>, asserted
    /// here alongside them so the count is visible in the test rather than only in prose.
    /// </summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void All_six_currency_formatting_options_round_trip()
    {
        var path = TempDbFile.NewPath("apex-v67-cur");
        try
        {
            var company = CompanyFactory.CreateSeeded("Currency Co", FyStart);
            var usd = new Currency(Guid.NewGuid(), "$", "USD", decimalPlaces: 2)
            {
                ShowAmountsInMillions = true,
                SuffixSymbolToAmount = true,
                SpaceBetweenAmountAndSymbol = true,
                DecimalWord = "Cent",
                DecimalPlacesForWords = 2,
            };
            company.AddCurrency(usd);
            using (var store = new SqliteCompanyStore(path)) store.Save(company);

            using var reopened = new SqliteCompanyStore(path);
            var loaded = reopened.Load(company.Id)!;
            var back = loaded.Currencies.Single(c => c.Id == usd.Id);

            Assert.Equal(2, back.DecimalPlaces);              // (1) "Number of decimal places"
            Assert.True(back.ShowAmountsInMillions);          // (2) "Show amounts in millions"
            Assert.True(back.SuffixSymbolToAmount);           // (3) "Suffix symbol to amount"
            Assert.True(back.SpaceBetweenAmountAndSymbol);    // (4) "Add space between amount and symbol"
            Assert.Equal("Cent", back.DecimalWord);           // (5) "Word representing amount after decimal"
            Assert.Equal(2, back.DecimalPlacesForWords);      // (6) "No. of decimal places for amounts in words"

            // 🔴 NULL IS NOT ZERO, and the base currency proves it survives as NULL rather than collapsing to 0.
            // null means "fall back to DecimalPlaces" — the pre-v67 behaviour — and 0 is the operator's
            // deliberate "no minor unit in words". A DEFAULT 0 column would have silently converted every
            // existing book to the second, and nothing else in this suite would have noticed.
            Assert.All(loaded.Currencies.Where(c => c.Id != usd.Id),
                c => Assert.Null(c.DecimalPlacesForWords));
        }
        finally { TempDbFile.Delete(path); }
    }

    /// <summary>
    /// Census <b>3.7</b>: the godown pair and their two F12 switches round-trip. 🔴 Four values, not two —
    /// <c>help.tallysolutions.com/inventory-storage-using-godowns-locations-tally/</c> shows neither field until
    /// its F12 switch is set (<i>"setting this option to Yes to displays the Allow storage of materials option
    /// on the Godown Creation screen"</i>, under <i>"Press F12 (Configure) to change the options."</i>), so a
    /// build that stored the fields without the switches would itself be a divergence.
    /// </summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void The_godown_pair_and_both_f12_switches_round_trip()
    {
        var path = TempDbFile.NewPath("apex-v67-godown");
        try
        {
            var company = CompanyFactory.CreateSeeded("Godown Co", FyStart);
            company.ConfigAllowStorageOfMaterials = true;
            company.ConfigUseAddressForLocations = true;

            var transit = new Godown(Guid.NewGuid(), "Transit Point")
            {
                AllowStorageOfMaterials = false,
                Address = "Gate 4, Peenya Industrial Area, Bengaluru 560058",
            };
            company.AddGodown(transit);
            using (var store = new SqliteCompanyStore(path)) store.Save(company);

            using var reopened = new SqliteCompanyStore(path);
            var loaded = reopened.Load(company.Id)!;

            Assert.True(loaded.ConfigAllowStorageOfMaterials);
            Assert.True(loaded.ConfigUseAddressForLocations);

            var back = loaded.Godowns.Single(g => g.Id == transit.Id);
            Assert.False(back.AllowStorageOfMaterials);
            Assert.Equal("Gate 4, Peenya Industrial Area, Bengaluru 560058", back.Address);

            // 🔴 The seeded Main Location is untouched and STILL STORES MATERIALS. A writer that bound the same
            // flag for every row would turn the whole book's inventory off, and an `Assert.False` on one godown
            // alone would not see it.
            Assert.All(loaded.Godowns.Where(g => g.Id != transit.Id),
                g => { Assert.True(g.AllowStorageOfMaterials); Assert.Null(g.Address); });
        }
        finally { TempDbFile.Delete(path); }
    }

    // ================================================================= the declined clause

    /// <summary>
    /// 🔴 <b>CENSUS ROW 2.10 IS DECLINED, NOT DEFERRED, AND THIS TEST PINS THE ABSENCE.</b> Row 2.10 names
    /// <i>"no period"</i> as a Scenario-master gap and this migration was briefed to add a scenario period
    /// from/to. It does not, <b>because the vendor's TallyPrime Scenario Creation screen has no period
    /// field</b>: <c>help.tallysolutions.com/tally-prime/financial-controlling/scenarios-tally/</c>, opened by
    /// content 2026-10-16, lists the screen's fields as <i>Name</i>, <i>Include actuals</i> and the voucher
    /// types to <i>Include</i> / <i>Exclude</i>. The period is chosen on the REPORT the scenario is applied to.
    ///
    /// <para>Adding <c>scenarios.period_from</c> / <c>period_to</c> would have been an INVENTION shipped under a
    /// fidelity row, and a round-trip test over it would have pinned the wrong answer — the failure mode this
    /// project has now hit three times. The absence is asserted so a later wave adding the columns has to come
    /// here and justify it against the page.</para>
    /// </summary>
    [Fact]
    public void The_scenario_master_gains_no_period_because_the_vendor_screen_has_none()
    {
        var path = TempDbFile.NewPath("apex-v67-scenario");
        try
        {
            using (var store = new SqliteCompanyStore(path))
                store.Save(CompanyFactory.CreateSeeded("Scenario Co", FyStart));

            var columns = ColumnNames(path, "scenarios");
            Assert.Equal(new List<string> { "id", "company_id", "name", "include_actuals" }, columns);
            Assert.DoesNotContain("period_from", columns);
            Assert.DoesNotContain("period_to", columns);
        }
        finally { TempDbFile.Delete(path); }
    }

    // ================================================================= the published constants

    /// <summary>
    /// The published column-name lists describe exactly what the migration adds, each <c>ALTER</c>'s declaration
    /// is byte-identical to its <see cref="Schema.CreateV1"/> counterpart, and there is NO <c>UPDATE</c> — the
    /// claim ER-13 rests on for this version.
    /// </summary>
    [Fact]
    public void The_published_v67_constants_describe_what_the_migration_adds()
    {
        Assert.Equal(11, Schema.V67VoucherTypeColumns.Count);
        Assert.Equal(new[] { "alias" }, Schema.V67CostCategoryColumns);
        Assert.Equal(new[] { "cost_centre_id" }, Schema.V67BudgetLineColumns);
        Assert.Equal(5, Schema.V67CurrencyColumns.Count);
        Assert.Equal(new[] { "allow_storage_of_materials", "address" }, Schema.V67GodownColumns);
        Assert.Equal(
            new[] { "config_allow_storage_of_materials", "config_use_address_for_locations" },
            Schema.V67CompanyColumns);

        foreach (var c in Schema.V67VoucherTypeColumns)
            Assert.Contains($"ALTER TABLE voucher_types ADD COLUMN {c} ", Schema.MigrateV66ToV67);
        foreach (var c in Schema.V67CurrencyColumns)
            Assert.Contains($"ALTER TABLE currencies ADD COLUMN {c} ", Schema.MigrateV66ToV67);
        Assert.Contains("ALTER TABLE cost_categories ADD COLUMN alias ", Schema.MigrateV66ToV67);
        Assert.Contains("ALTER TABLE budget_lines ADD COLUMN cost_centre_id ", Schema.MigrateV66ToV67);
        Assert.Contains("ALTER TABLE godowns ADD COLUMN allow_storage_of_materials INTEGER NOT NULL DEFAULT 1;",
            Schema.MigrateV66ToV67);
        foreach (var c in Schema.V67CompanyColumns)
            Assert.Contains($"ALTER TABLE companies ADD COLUMN {c} ", Schema.MigrateV66ToV67);

        // 🔴 AND THERE IS NO UPDATE. v67 back-fills nothing.
        Assert.DoesNotContain("UPDATE ", Schema.MigrateV66ToV67);

        // 🔴 Nothing for census 2.10 — the vendor's Scenario Creation screen has no period. See
        // The_scenario_master_gains_no_period_because_the_vendor_screen_has_none.
        Assert.DoesNotContain("ALTER TABLE scenarios", Schema.MigrateV66ToV67);

        Assert.Equal(67, Schema.CurrentVersion);
    }

    // ================================================================= helpers

    private static void AssertAbsent(string path, string table, IReadOnlyList<string> columns)
    {
        var present = ColumnNames(path, table);
        foreach (var c in columns) Assert.DoesNotContain(c, present);
    }

    private static string? DefaultOf(string path, string table, string column)
    {
        using var conn = Open(path);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info(\"{table}\");";
        string? value = null;
        using (var r = cmd.ExecuteReader())
            while (r.Read())
                if (string.Equals(r.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                    value = r.IsDBNull(4) ? null : r.GetString(4);
        SqliteConnection.ClearPool(conn);
        return value;
    }

    private static SqliteConnection Open(string path)
    {
        var conn = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadWrite }.ToString());
        conn.Open();
        return conn;
    }

    private static long ReadScalar(string path, string sql)
    {
        using var conn = Open(path);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var value = Convert.ToInt64(cmd.ExecuteScalar());
        SqliteConnection.ClearPool(conn);
        return value;
    }

    private static List<string> ColumnNames(string path, string table)
    {
        using var conn = Open(path);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info(\"{table}\");";
        var names = new List<string>();
        using (var r = cmd.ExecuteReader()) while (r.Read()) names.Add(r.GetString(1));
        SqliteConnection.ClearPool(conn);
        return names;
    }

    /// <summary>name | type | notnull | default | pk for every column — the equivalence contract.</summary>
    private static List<string> TableInfo(string path, string table)
    {
        using var conn = Open(path);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info(\"{table}\");";
        var rows = new List<string>();
        using (var r = cmd.ExecuteReader())
            while (r.Read())
                rows.Add(string.Join('|',
                    r.GetString(1), r.GetString(2), r.GetInt32(3),
                    r.IsDBNull(4) ? "<null>" : r.GetString(4), r.GetInt32(5)));
        SqliteConnection.ClearPool(conn);
        return rows;
    }

    private static List<string> PrimaryKeyColumns(string path, string table)
    {
        using var conn = Open(path);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info(\"{table}\");";
        var pk = new List<string>();
        using (var r = cmd.ExecuteReader())
            while (r.Read()) if (r.GetInt32(5) > 0) pk.Add(r.GetString(1));
        SqliteConnection.ClearPool(conn);
        return pk;
    }

    private static List<string> IndexNames(string path, string table)
    {
        using var conn = Open(path);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type = 'index' AND tbl_name = $t AND sql IS NOT NULL;";
        cmd.Parameters.AddWithValue("$t", table);
        var names = new List<string>();
        using (var r = cmd.ExecuteReader()) while (r.Read()) names.Add(r.GetString(0));
        SqliteConnection.ClearPool(conn);
        return names;
    }

    private static int ForeignKeyViolationCount(string path)
    {
        using var conn = Open(path);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA foreign_key_check;";
        var n = 0;
        using (var r = cmd.ExecuteReader()) while (r.Read()) n++;
        SqliteConnection.ClearPool(conn);
        return n;
    }

    private static string IntegrityCheck(string path)
    {
        using var conn = Open(path);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA integrity_check;";
        var result = Convert.ToString(cmd.ExecuteScalar()) ?? string.Empty;
        SqliteConnection.ClearPool(conn);
        return result;
    }
}
