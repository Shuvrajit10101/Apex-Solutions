using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Io;
using Apex.Ledger.Persistence;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Microsoft.Data.Sqlite;

namespace Apex.Persistence.Sqlite;

/// <summary>
/// SQLite adapter for the Phase-1 accounting core: <b>one <c>.db</c> file per company</b>. Opens or
/// creates the database at a given path, applies the versioned schema (v1), and implements the three
/// persistence ports (<see cref="ICompanyRepository"/>, <see cref="IMasterRepository"/>,
/// <see cref="IVoucherRepository"/>) against relational tables (accounting-core §2). Money is stored
/// as INTEGER paisa so reports reconcile to the paisa (NFR-3). The domain library stays free of any
/// SQLite dependency — this adapter is the only place <c>Microsoft.Data.Sqlite</c> is referenced.
/// </summary>
/// <remarks>
/// <para><b>Save</b> persists a whole <see cref="Company"/> aggregate (company + masters + posted
/// vouchers) transactionally, replacing any prior state for that company id (idempotent upsert via
/// delete-then-insert within one transaction).</para>
/// <para><b>Load</b> rehydrates the aggregate: it rebuilds the masters directly, then re-posts every
/// stored voucher through <see cref="LedgerService"/> (the real posting path), which preserves the
/// stored voucher number. A voucher that fails validation on reload signals a corrupt store and is
/// surfaced as an exception rather than silently dropped.</para>
/// </remarks>
public sealed class SqliteCompanyStore : ICompanyRepository, IMasterRepository, IVoucherRepository, ISavedReportViewRepository, ISmtpProfileRepository, IDisposable
{
    private readonly SqliteConnection _connection;

    /// <summary>The database file path this store is bound to (empty for in-memory).</summary>
    public string DatabasePath { get; }

    /// <summary>
    /// Opens (or creates) the company database at <paramref name="databasePath"/> and ensures the
    /// schema is present and at the expected version. A single long-lived connection is held for the
    /// lifetime of the store; dispose it to release the file handle.
    /// </summary>
    public SqliteCompanyStore(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
            throw new ArgumentException("A database path is required.", nameof(databasePath));

        DatabasePath = databasePath;

        var dir = Path.GetDirectoryName(Path.GetFullPath(databasePath));
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var connStr = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
        }.ToString();

        _connection = new SqliteConnection(connStr);
        _connection.Open();
        Exec("PRAGMA foreign_keys = ON;");
        EnsureSchema();
    }

    // ------------------------------------------------------------------ schema / migrations

    private void EnsureSchema()
    {
        var hasVersionTable = ScalarLong(
            "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='schema_version';") > 0;

        if (!hasVersionTable)
        {
            using var tx = _connection.BeginTransaction();
            using (var cmd = _connection.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = Schema.CreateV1;
                cmd.ExecuteNonQuery();
            }
            using (var ins = _connection.CreateCommand())
            {
                ins.Transaction = tx;
                ins.CommandText = "INSERT INTO schema_version(version) VALUES ($v);";
                ins.Parameters.AddWithValue("$v", Schema.CurrentVersion);
                ins.ExecuteNonQuery();
            }
            tx.Commit();
            return;
        }

        var version = ScalarLong("SELECT version FROM schema_version LIMIT 1;");

        // v1 → v2: apply the bill-wise migration, then bump the marker. Existing v1 data survives.
        if (version == 1)
        {
            using var tx = _connection.BeginTransaction();
            using (var mig = _connection.CreateCommand())
            {
                mig.Transaction = tx;
                mig.CommandText = Schema.MigrateV1ToV2;
                mig.ExecuteNonQuery();
            }
            using (var bump = _connection.CreateCommand())
            {
                bump.Transaction = tx;
                bump.CommandText = "UPDATE schema_version SET version = $v;";
                bump.Parameters.AddWithValue("$v", 2);
                bump.ExecuteNonQuery();
            }
            tx.Commit();
            version = 2;
        }

        // v2 → v3: apply the cost-centre migration, then bump the marker. Existing v2 data survives.
        if (version == 2)
        {
            using var tx = _connection.BeginTransaction();
            using (var mig = _connection.CreateCommand())
            {
                mig.Transaction = tx;
                mig.CommandText = Schema.MigrateV2ToV3;
                mig.ExecuteNonQuery();
            }
            using (var bump = _connection.CreateCommand())
            {
                bump.Transaction = tx;
                bump.CommandText = "UPDATE schema_version SET version = $v;";
                bump.Parameters.AddWithValue("$v", 3);
                bump.ExecuteNonQuery();
            }
            tx.Commit();
            version = 3;
        }

        // v3 → v4: apply the budgets migration, then bump the marker. Existing v3 data survives.
        if (version == 3)
        {
            using var tx = _connection.BeginTransaction();
            using (var mig = _connection.CreateCommand())
            {
                mig.Transaction = tx;
                mig.CommandText = Schema.MigrateV3ToV4;
                mig.ExecuteNonQuery();
            }
            using (var bump = _connection.CreateCommand())
            {
                bump.Transaction = tx;
                bump.CommandText = "UPDATE schema_version SET version = $v;";
                bump.Parameters.AddWithValue("$v", 4);
                bump.ExecuteNonQuery();
            }
            tx.Commit();
            version = 4;
        }

        // v4 → v5: apply the banking migration, then bump the marker. Existing v4 data survives.
        if (version == 4)
        {
            using var tx = _connection.BeginTransaction();
            using (var mig = _connection.CreateCommand())
            {
                mig.Transaction = tx;
                mig.CommandText = Schema.MigrateV4ToV5;
                mig.ExecuteNonQuery();
            }
            using (var bump = _connection.CreateCommand())
            {
                bump.Transaction = tx;
                bump.CommandText = "UPDATE schema_version SET version = $v;";
                bump.Parameters.AddWithValue("$v", 5);
                bump.ExecuteNonQuery();
            }
            tx.Commit();
            version = 5;
        }

        // v5 → v6: apply the scenarios migration, then bump the marker. Existing v5 data survives.
        if (version == 5)
        {
            using var tx = _connection.BeginTransaction();
            using (var mig = _connection.CreateCommand())
            {
                mig.Transaction = tx;
                mig.CommandText = Schema.MigrateV5ToV6;
                mig.ExecuteNonQuery();
            }
            using (var bump = _connection.CreateCommand())
            {
                bump.Transaction = tx;
                bump.CommandText = "UPDATE schema_version SET version = $v;";
                bump.Parameters.AddWithValue("$v", 6);
                bump.ExecuteNonQuery();
            }
            tx.Commit();
            version = 6;
        }

        // v6 → v7: apply the interest-calculation migration, then bump the marker. Existing v6 data survives.
        if (version == 6)
        {
            using var tx = _connection.BeginTransaction();
            using (var mig = _connection.CreateCommand())
            {
                mig.Transaction = tx;
                mig.CommandText = Schema.MigrateV6ToV7;
                mig.ExecuteNonQuery();
            }
            using (var bump = _connection.CreateCommand())
            {
                bump.Transaction = tx;
                bump.CommandText = "UPDATE schema_version SET version = $v;";
                bump.Parameters.AddWithValue("$v", 7);
                bump.ExecuteNonQuery();
            }
            tx.Commit();
            version = 7;
        }

        // v7 → v8: apply the multi-currency migration, then bump the marker. Existing v7 data survives.
        if (version == 7)
        {
            using var tx = _connection.BeginTransaction();
            using (var mig = _connection.CreateCommand())
            {
                mig.Transaction = tx;
                mig.CommandText = Schema.MigrateV7ToV8;
                mig.ExecuteNonQuery();
            }
            using (var bump = _connection.CreateCommand())
            {
                bump.Transaction = tx;
                bump.CommandText = "UPDATE schema_version SET version = $v;";
                bump.Parameters.AddWithValue("$v", 8);
                bump.ExecuteNonQuery();
            }
            tx.Commit();
            version = 8;
        }

        // v8 → v9: apply the inventory-masters migration, then bump the marker. Existing v8 data survives.
        if (version == 8)
        {
            using var tx = _connection.BeginTransaction();
            using (var mig = _connection.CreateCommand())
            {
                mig.Transaction = tx;
                mig.CommandText = Schema.MigrateV8ToV9;
                mig.ExecuteNonQuery();
            }
            using (var bump = _connection.CreateCommand())
            {
                bump.Transaction = tx;
                bump.CommandText = "UPDATE schema_version SET version = $v;";
                bump.Parameters.AddWithValue("$v", 9);
                bump.ExecuteNonQuery();
            }
            tx.Commit();
            version = 9;
        }

        // v9 → v10: apply the inventory & order voucher migration, then bump the marker. Existing v9 data survives.
        if (version == 9)
        {
            using var tx = _connection.BeginTransaction();
            using (var mig = _connection.CreateCommand())
            {
                mig.Transaction = tx;
                mig.CommandText = Schema.MigrateV9ToV10;
                mig.ExecuteNonQuery();
            }
            using (var bump = _connection.CreateCommand())
            {
                bump.Transaction = tx;
                bump.CommandText = "UPDATE schema_version SET version = $v;";
                bump.Parameters.AddWithValue("$v", 10);
                bump.ExecuteNonQuery();
            }
            tx.Commit();
            version = 10;
        }

        // v10 → v11: apply the per-item standard-cost migration, then bump the marker. Existing v10 data survives.
        if (version == 10)
        {
            using var tx = _connection.BeginTransaction();
            using (var mig = _connection.CreateCommand())
            {
                mig.Transaction = tx;
                mig.CommandText = Schema.MigrateV10ToV11;
                mig.ExecuteNonQuery();
            }
            using (var bump = _connection.CreateCommand())
            {
                bump.Transaction = tx;
                bump.CommandText = "UPDATE schema_version SET version = $v;";
                bump.Parameters.AddWithValue("$v", 11);
                bump.ExecuteNonQuery();
            }
            tx.Commit();
            version = 11;
        }

        // v11 → v12: apply the item-invoice stock-line migration, then bump the marker. Existing v11 data survives.
        if (version == 11)
        {
            using var tx = _connection.BeginTransaction();
            using (var mig = _connection.CreateCommand())
            {
                mig.Transaction = tx;
                mig.CommandText = Schema.MigrateV11ToV12;
                mig.ExecuteNonQuery();
            }
            using (var bump = _connection.CreateCommand())
            {
                bump.Transaction = tx;
                bump.CommandText = "UPDATE schema_version SET version = $v;";
                bump.Parameters.AddWithValue("$v", 12);
                bump.ExecuteNonQuery();
            }
            tx.Commit();
            version = 12;
        }

        // v12 → v13: apply the core-GST migration, then bump the marker. Existing v12 data survives (GST off).
        if (version == 12)
        {
            using var tx = _connection.BeginTransaction();
            using (var mig = _connection.CreateCommand())
            {
                mig.Transaction = tx;
                mig.CommandText = Schema.MigrateV12ToV13;
                mig.ExecuteNonQuery();
            }
            using (var bump = _connection.CreateCommand())
            {
                bump.Transaction = tx;
                bump.CommandText = "UPDATE schema_version SET version = $v;";
                bump.Parameters.AddWithValue("$v", 13);
                bump.ExecuteNonQuery();
            }
            tx.Commit();
            version = 13;
        }

        // v13 → v14: apply the saved-views migration, then bump the marker. Existing v13 data survives untouched.
        if (version == 13)
        {
            using var tx = _connection.BeginTransaction();
            using (var mig = _connection.CreateCommand())
            {
                mig.Transaction = tx;
                mig.CommandText = Schema.MigrateV13ToV14;
                mig.ExecuteNonQuery();
            }
            using (var bump = _connection.CreateCommand())
            {
                bump.Transaction = tx;
                bump.CommandText = "UPDATE schema_version SET version = $v;";
                bump.Parameters.AddWithValue("$v", 14);
                bump.ExecuteNonQuery();
            }
            tx.Commit();
            version = 14;
        }

        // v14 → v15: apply the SMTP-profile migration, then bump the marker. Existing v14 data survives untouched.
        if (version == 14)
        {
            using var tx = _connection.BeginTransaction();
            using (var mig = _connection.CreateCommand())
            {
                mig.Transaction = tx;
                mig.CommandText = Schema.MigrateV14ToV15;
                mig.ExecuteNonQuery();
            }
            using (var bump = _connection.CreateCommand())
            {
                bump.Transaction = tx;
                bump.CommandText = "UPDATE schema_version SET version = $v;";
                bump.Parameters.AddWithValue("$v", 15);
                bump.ExecuteNonQuery();
            }
            tx.Commit();
            version = 15;
        }

        // v15 → v16: apply the batch-masters migration, then bump the marker. Existing v15 data survives untouched
        // (pure CREATE + additive nullable batch_id columns; batch_label stays intact — Phase 6 slice 1).
        if (version == 15)
        {
            using var tx = _connection.BeginTransaction();
            using (var mig = _connection.CreateCommand())
            {
                mig.Transaction = tx;
                mig.CommandText = Schema.MigrateV15ToV16;
                mig.ExecuteNonQuery();
            }
            using (var bump = _connection.CreateCommand())
            {
                bump.Transaction = tx;
                bump.CommandText = "UPDATE schema_version SET version = $v;";
                bump.Parameters.AddWithValue("$v", 16);
                bump.ExecuteNonQuery();
            }
            tx.Commit();
            version = 16;
        }

        // v16 → v17: apply the Bill-of-Materials masters migration, then bump the marker. Existing v16 data survives
        // untouched (two pure CREATE TABLE + four CREATE INDEX; no ALTER, no row rewrites — Phase 6 slice 2).
        if (version == 16)
        {
            using var tx = _connection.BeginTransaction();
            using (var mig = _connection.CreateCommand())
            {
                mig.Transaction = tx;
                mig.CommandText = Schema.MigrateV16ToV17;
                mig.ExecuteNonQuery();
            }
            using (var bump = _connection.CreateCommand())
            {
                bump.Transaction = tx;
                bump.CommandText = "UPDATE schema_version SET version = $v;";
                bump.Parameters.AddWithValue("$v", 17);
                bump.ExecuteNonQuery();
            }
            tx.Commit();
            version = 17;
        }

        // v17 → v18: apply the two Manufacturing-Journal master-flag columns, then bump the marker. Existing v17
        // data survives untouched (two additive ALTER … ADD COLUMN DEFAULT 0; no row rewrites — Phase 6 slice 2).
        if (version == 17)
        {
            using var tx = _connection.BeginTransaction();
            using (var mig = _connection.CreateCommand())
            {
                mig.Transaction = tx;
                mig.CommandText = Schema.MigrateV17ToV18;
                mig.ExecuteNonQuery();
            }
            using (var bump = _connection.CreateCommand())
            {
                bump.Transaction = tx;
                bump.CommandText = "UPDATE schema_version SET version = $v;";
                bump.Parameters.AddWithValue("$v", 18);
                bump.ExecuteNonQuery();
            }
            tx.Commit();
            version = 18;
        }

        // v18 → v19: apply the Additional-Cost-of-Purchase schema (track_additional_costs flag +
        // method_of_appropriation column + additional_cost_lines table), then bump the marker. Existing v18 data
        // survives untouched (two additive ALTER + one CREATE TABLE/INDEX; no row rewrites — Phase 6 slice 3).
        if (version == 18)
        {
            using var tx = _connection.BeginTransaction();
            using (var mig = _connection.CreateCommand())
            {
                mig.Transaction = tx;
                mig.CommandText = Schema.MigrateV18ToV19;
                mig.ExecuteNonQuery();
            }
            using (var bump = _connection.CreateCommand())
            {
                bump.Transaction = tx;
                bump.CommandText = "UPDATE schema_version SET version = $v;";
                bump.Parameters.AddWithValue("$v", 19);
                bump.ExecuteNonQuery();
            }
            tx.Commit();
            version = 19;
        }

        // v19 → v20: apply the Actual/Billed-quantity + zero-valued schema (four nullable qty columns on the two
        // stock-line tables + a company F11 flag + a voucher-type flag), then bump the marker. Existing v19 data
        // survives untouched (four additive ALTER ADD COLUMN NULL + two ALTER ADD COLUMN DEFAULT 0; no row rewrites —
        // Phase 6 slice 4).
        if (version == 19)
        {
            using var tx = _connection.BeginTransaction();
            using (var mig = _connection.CreateCommand())
            {
                mig.Transaction = tx;
                mig.CommandText = Schema.MigrateV19ToV20;
                mig.ExecuteNonQuery();
            }
            using (var bump = _connection.CreateCommand())
            {
                bump.Transaction = tx;
                bump.CommandText = "UPDATE schema_version SET version = $v;";
                bump.Parameters.AddWithValue("$v", 20);
                bump.ExecuteNonQuery();
            }
            tx.Commit();
            version = 20;
        }

        if (version != Schema.CurrentVersion)
            throw new InvalidOperationException(
                $"Database schema version {version} is not supported by this adapter (expected {Schema.CurrentVersion}). " +
                "A migration is required; none is registered for this version.");
    }

    // ------------------------------------------------------------------ ICompanyRepository

    /// <inheritdoc />
    public Company? Load(Guid companyId)
    {
        using var read = _connection.CreateCommand();
        read.CommandText = """
            SELECT id, name, mailing_name, address, country, state, pin,
                   financial_year_start, books_begin_from, base_currency_symbol,
                   base_currency_name, decimal_places, decimal_unit_name,
                   primary_cost_category, main_location, profit_and_loss_head_id,
                   gst_enabled, gstin, gst_home_state, gst_reg_type, gst_applicable_from, gst_periodicity,
                   use_separate_actual_billed_qty
            FROM companies WHERE id = $id;
            """;
        read.Parameters.AddWithValue("$id", companyId.ToString("D"));

        Company company;
        Guid? plHeadId;
        using (var r = read.ExecuteReader())
        {
            if (!r.Read())
                return null;

            var fyStart = ParseDate(r.GetString(7));
            var books = ParseDate(r.GetString(8));
            company = new Company(companyId, r.GetString(1), fyStart, books)
            {
                MailingName = r.GetString(2),
                Address = r.IsDBNull(3) ? null : r.GetString(3),
                Country = r.GetString(4),
                State = r.IsDBNull(5) ? null : r.GetString(5),
                Pin = r.IsDBNull(6) ? null : r.GetString(6),
                BaseCurrencySymbol = r.GetString(9),
                BaseCurrencyName = r.GetString(10),
                DecimalPlaces = (int)r.GetInt64(11),
                DecimalUnitName = r.GetString(12),
                PrimaryCostCategoryName = r.GetString(13),
                MainLocationName = r.GetString(14),
                // v20 (RQ-22): F11 "Use separate Actual & Billed Quantity" — a plain persisted toggle, read verbatim.
                UseSeparateActualBilledQuantity = r.GetInt64(22) != 0,
            };
            plHeadId = r.IsDBNull(15) ? null : Guid.Parse(r.GetString(15));

            // v13 core GST config. A company is GST-enabled iff gst_enabled = 1; when off (default for every
            // pre-v13 company) Gst stays null and no GST path activates (ER-10).
            if (r.GetInt64(16) != 0)
            {
                company.Gst = new GstConfig
                {
                    Enabled = true,
                    Gstin = r.IsDBNull(17) ? null : r.GetString(17),
                    HomeStateCode = r.IsDBNull(18) ? null : r.GetString(18),
                    RegistrationType = r.IsDBNull(19) ? GstRegistrationType.Regular : (GstRegistrationType)(int)r.GetInt64(19),
                    ApplicableFrom = r.IsDBNull(20) ? (DateOnly?)null : ParseDate(r.GetString(20)),
                    Periodicity = r.IsDBNull(21) ? GstReturnPeriodicity.Monthly : (GstReturnPeriodicity)(int)r.GetInt64(21),
                };
            }
        }

        // v13 GST rate slabs (only present when GST was enabled). Loaded into the config's slab set.
        if (company.Gst is not null)
            foreach (var slab in ReadGstRateSlabs(companyId))
                company.Gst.AddRateSlab(slab);

        // Groups: the reserved P&L head (is_pl_head = 1) is registered via SetProfitAndLossHead and
        // kept OUT of Company.Groups; the 28 (and any custom) groups go into Company.Groups. Load
        // reads ALL rows (including the head) and routes the head aside by its id.
        foreach (var g in ReadGroupRows(companyId, includePlHead: true))
        {
            if (plHeadId is not null && g.Id == plHeadId.Value)
                company.SetProfitAndLossHead(g);
            else
                company.AddGroup(g);
        }

        // Currencies + rates before ledgers/vouchers: ledgers.currency_id and entry-line forex reference them.
        foreach (var cur in ReadCurrencies(companyId))
            company.AddCurrency(cur);
        foreach (var rate in ReadExchangeRates(companyId))
            company.AddExchangeRate(rate);

        foreach (var l in ReadLedgers(companyId))
            company.AddLedger(l);

        foreach (var t in ReadVoucherTypes(companyId))
            company.AddVoucherType(t);

        // Cost categories then centres (centres reference categories; both must exist before vouchers
        // are re-posted, since the validator checks a line's cost allocations against them).
        foreach (var cat in ReadCostCategories(companyId))
            company.AddCostCategory(cat);
        foreach (var centre in ReadCostCentres(companyId))
            company.AddCostCentre(centre);

        // Inventory masters (catalog §9). Order matters: units first (a compound unit + a stock item
        // reference simple units), then stock groups + categories + godowns, then stock items (reference
        // group/category/unit), then opening balances (reference items + godowns).
        foreach (var u in ReadUnits(companyId))
            company.AddUnit(u);
        foreach (var g in ReadStockGroups(companyId))
            company.AddStockGroup(g);
        foreach (var cat in ReadStockCategories(companyId))
            company.AddStockCategory(cat);
        foreach (var g in ReadGodowns(companyId))
            company.AddGodown(g);
        foreach (var item in ReadStockItems(companyId))
            company.AddStockItem(item);
        // Batch masters after items + godowns (they reference both), before opening balances.
        foreach (var bm in ReadBatchMasters(companyId))
            company.AddBatchMaster(bm);
        // Bill-of-Materials masters after items + godowns (a BOM header + line reference both). The finished
        // good's Set-Components flag is persisted verbatim on the item (loaded above), so it is NOT re-derived here.
        foreach (var bom in ReadBillsOfMaterials(companyId))
            company.AddBillOfMaterials(bom);
        foreach (var ob in ReadStockOpeningBalances(companyId))
            company.AddStockOpeningBalance(ob);

        // Inventory & order vouchers (catalog §10): rehydrated directly (the store is trusted; posting guards
        // ran when they were first accepted). They reference stock items/godowns/units + a party ledger.
        foreach (var iv in ReadInventoryVouchers(companyId))
            company.AddInventoryVoucher(iv);

        // Vouchers: re-post through the engine (real posting path). The stored number is preserved
        // because Post only assigns a number when the voucher's number is unset (≤ 0).
        var service = new LedgerService(company);
        foreach (var v in ReadVouchers(companyId))
            service.Post(v);

        // Budgets (catalog §7): masters that reference groups/ledgers already loaded above.
        foreach (var b in ReadBudgets(companyId))
            company.AddBudget(b);

        // Scenarios (catalog §7): masters whose include/exclude lists reference voucher types loaded above.
        foreach (var s in ReadScenarios(companyId))
            company.AddScenario(s);

        return company;
    }

    /// <summary>
    /// Reads the ids and names of all companies stored in this database file. In the one-db-per-company
    /// model this is normally a single row, but the method returns all rows so a UI can list them without
    /// knowing the company id in advance.
    /// </summary>
    public IReadOnlyList<(Guid Id, string Name)> ListCompanies()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT id, name FROM companies ORDER BY rowid;";
        using var r = cmd.ExecuteReader();
        var list = new List<(Guid, string)>();
        while (r.Read())
            list.Add((Guid.Parse(r.GetString(0)), r.GetString(1)));
        return list;
    }

    /// <inheritdoc />
    public void Save(Company company)
    {
        if (company is null) throw new ArgumentNullException(nameof(company));

        using var tx = _connection.BeginTransaction();

        DeleteCompanyRows(tx, company.Id);

        // Company row is written after its groups so the profit_and_loss_head_id FK resolves; but the
        // groups reference companies(id), so insert the company row first WITHOUT the head fk, then
        // patch the head id once the head group row exists.
        InsertCompany(tx, company);
        InsertGroups(tx, company);
        SetProfitAndLossHead(tx, company);
        // Currencies + rates before ledgers (ledgers.currency_id FK currencies) and before vouchers
        // (entry-line forex FK currencies).
        InsertCurrencies(tx, company);
        InsertExchangeRates(tx, company);
        InsertLedgers(tx, company);
        InsertVoucherTypes(tx, company);
        // Cost categories before centres (centres FK categories); both before vouchers (cost allocations
        // FK both).
        InsertCostCategories(tx, company);
        InsertCostCentres(tx, company);
        // Inventory masters (catalog §9). Units first (a compound unit references simple units; a stock item
        // references a unit); then groups + categories + godowns; then stock items; then opening balances.
        InsertUnits(tx, company);
        InsertStockGroups(tx, company);
        InsertStockCategories(tx, company);
        InsertGodowns(tx, company);
        InsertStockItems(tx, company);
        // Batch masters after stock items (FK stock_items) + godowns; before opening balances (a batch_id on an
        // opening layer would FK batch_masters — even though we do not populate batch_id today, keep the order safe).
        InsertBatchMasters(tx, company);
        // Bill-of-Materials masters after stock items + godowns (a BOM header + its lines FK both).
        InsertBillsOfMaterials(tx, company);
        InsertStockOpeningBalances(tx, company);
        InsertVouchers(tx, company);
        // Inventory & order vouchers (catalog §10): reference voucher_types, stock_items, godowns, units, and
        // (optionally) a party ledger — all inserted above.
        InsertInventoryVouchers(tx, company);
        // Budgets last: their lines FK groups and ledgers, both already inserted.
        InsertBudgets(tx, company);
        // Scenarios: their voucher-type rows FK voucher_types, already inserted.
        InsertScenarios(tx, company);

        tx.Commit();
    }

    // ------------------------------------------------------------------ IMasterRepository

    /// <inheritdoc />
    public IReadOnlyList<Group> GetGroups(Guid companyId) => ReadGroups(companyId).ToList();

    /// <inheritdoc />
    public IReadOnlyList<Apex.Ledger.Domain.Ledger> GetLedgers(Guid companyId) => ReadLedgers(companyId).ToList();

    /// <inheritdoc />
    public IReadOnlyList<VoucherType> GetVoucherTypes(Guid companyId) => ReadVoucherTypes(companyId).ToList();

    // ------------------------------------------------------------------ IVoucherRepository

    /// <inheritdoc />
    public IReadOnlyList<Voucher> GetAll(Guid companyId) => ReadVouchers(companyId).ToList();

    /// <inheritdoc />
    public void Add(Guid companyId, Voucher voucher)
    {
        if (voucher is null) throw new ArgumentNullException(nameof(voucher));
        using var tx = _connection.BeginTransaction();
        InsertVoucher(tx, companyId, voucher);
        tx.Commit();
    }

    /// <inheritdoc />
    public void Remove(Guid companyId, Guid voucherId)
    {
        using var tx = _connection.BeginTransaction();
        using (var delAllocs = _connection.CreateCommand())
        {
            delAllocs.Transaction = tx;
            delAllocs.CommandText = """
                DELETE FROM bill_allocations WHERE entry_line_id IN (
                    SELECT id FROM entry_lines WHERE voucher_id = $vid);
                """;
            delAllocs.Parameters.AddWithValue("$vid", voucherId.ToString("D"));
            delAllocs.ExecuteNonQuery();
        }
        using (var delCostAllocs = _connection.CreateCommand())
        {
            delCostAllocs.Transaction = tx;
            delCostAllocs.CommandText = """
                DELETE FROM cost_allocations WHERE entry_line_id IN (
                    SELECT id FROM entry_lines WHERE voucher_id = $vid);
                """;
            delCostAllocs.Parameters.AddWithValue("$vid", voucherId.ToString("D"));
            delCostAllocs.ExecuteNonQuery();
        }
        using (var delBankAllocs = _connection.CreateCommand())
        {
            delBankAllocs.Transaction = tx;
            delBankAllocs.CommandText = """
                DELETE FROM bank_allocations WHERE entry_line_id IN (
                    SELECT id FROM entry_lines WHERE voucher_id = $vid);
                """;
            delBankAllocs.Parameters.AddWithValue("$vid", voucherId.ToString("D"));
            delBankAllocs.ExecuteNonQuery();
        }
        using (var delLines = _connection.CreateCommand())
        {
            delLines.Transaction = tx;
            delLines.CommandText = "DELETE FROM entry_lines WHERE voucher_id = $vid;";
            delLines.Parameters.AddWithValue("$vid", voucherId.ToString("D"));
            delLines.ExecuteNonQuery();
        }
        using (var delV = _connection.CreateCommand())
        {
            delV.Transaction = tx;
            delV.CommandText = "DELETE FROM vouchers WHERE id = $vid AND company_id = $cid;";
            delV.Parameters.AddWithValue("$vid", voucherId.ToString("D"));
            delV.Parameters.AddWithValue("$cid", companyId.ToString("D"));
            delV.ExecuteNonQuery();
        }
        tx.Commit();
    }

    // ------------------------------------------------------------------ readers

    /// <summary>
    /// Reads the normal group masters — the 28 predefined groups plus any custom ones — mirroring
    /// <see cref="Company.Groups"/>. The reserved Profit &amp; Loss head (<c>is_pl_head = 1</c>) is
    /// deliberately excluded; it is not one of the 28 and is surfaced only via
    /// <see cref="Company.ProfitAndLossHead"/> on a full <see cref="Load"/>.
    /// </summary>
    private IEnumerable<Group> ReadGroups(Guid companyId) => ReadGroupRows(companyId, includePlHead: false);

    private IEnumerable<Group> ReadGroupRows(Guid companyId, bool includePlHead)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = includePlHead
            ? """
              SELECT id, name, nature, parent_id, alias, is_predefined
              FROM groups WHERE company_id = $cid ORDER BY rowid;
              """
            : """
              SELECT id, name, nature, parent_id, alias, is_predefined
              FROM groups WHERE company_id = $cid AND is_pl_head = 0 ORDER BY rowid;
              """;
        cmd.Parameters.AddWithValue("$cid", companyId.ToString("D"));
        using var r = cmd.ExecuteReader();
        var list = new List<Group>();
        while (r.Read())
        {
            var parent = r.IsDBNull(3) ? (Guid?)null : Guid.Parse(r.GetString(3));
            list.Add(new Group(
                Guid.Parse(r.GetString(0)),
                r.GetString(1),
                (GroupNature)(int)r.GetInt64(2),
                parentId: parent,
                alias: r.IsDBNull(4) ? null : r.GetString(4),
                isPredefined: r.GetInt64(5) != 0));
        }
        return list;
    }

    private IEnumerable<Apex.Ledger.Domain.Ledger> ReadLedgers(Guid companyId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, name, group_id, opening_balance_paisa, opening_is_debit, alias, is_predefined,
                   maintain_bill_by_bill, default_credit_period, cost_applicable,
                   enable_cheque_printing, cheque_bank_name,
                   interest_enabled, interest_rate_millis, interest_per, interest_on_balance,
                   interest_applicability, interest_calc_from, interest_style,
                   interest_round_method, interest_round_decimals, currency_id,
                   party_gst_reg_type, party_gstin, party_gst_state,
                   sp_gst_hsn, sp_gst_taxability, sp_gst_rate_bp, sp_gst_supply_type,
                   gst_tax_head, gst_tax_direction, method_of_appropriation
            FROM ledgers WHERE company_id = $cid ORDER BY rowid;
            """;
        cmd.Parameters.AddWithValue("$cid", companyId.ToString("D"));
        using var r = cmd.ExecuteReader();
        var list = new List<Apex.Ledger.Domain.Ledger>();
        while (r.Read())
        {
            list.Add(new Apex.Ledger.Domain.Ledger(
                Guid.Parse(r.GetString(0)),
                r.GetString(1),
                Guid.Parse(r.GetString(2)),
                Paisa.ToMoney(r.GetInt64(3)),
                openingIsDebit: r.GetInt64(4) != 0,
                alias: r.IsDBNull(5) ? null : r.GetString(5),
                isPredefined: r.GetInt64(6) != 0,
                maintainBillByBill: r.GetInt64(7) != 0,
                defaultCreditPeriodDays: r.IsDBNull(8) ? (int?)null : (int)r.GetInt64(8),
                costCentresApplicable: r.IsDBNull(9) ? (bool?)null : r.GetInt64(9) != 0,
                enableChequePrinting: r.GetInt64(10) != 0,
                chequePrintingBankName: r.IsDBNull(11) ? null : r.GetString(11),
                interest: ReadInterest(r),
                currencyId: r.IsDBNull(21) ? (Guid?)null : Guid.Parse(r.GetString(21)),
                partyGst: ReadPartyGst(r),
                salesPurchaseGst: ReadSalesPurchaseGst(r),
                gstClassification: ReadLedgerGstClassification(r),
                // v19 (RQ-16..RQ-20): method of appropriation (NULL = plain P&L ledger, not additional-cost).
                methodOfAppropriation: r.IsDBNull(31) ? (MethodOfAppropriation?)null : (MethodOfAppropriation)(int)r.GetInt64(31)));
        }
        return list;
    }

    /// <summary>Reads the party GST block (columns 22–24), or <c>null</c> when the ledger has no party GST.</summary>
    private static PartyGstDetails? ReadPartyGst(SqliteDataReader r)
    {
        // Present iff any of reg-type / gstin / state is set.
        if (r.IsDBNull(22) && r.IsDBNull(23) && r.IsDBNull(24)) return null;
        return new PartyGstDetails
        {
            RegistrationType = r.IsDBNull(22) ? GstRegistrationType.Unregistered : (GstRegistrationType)(int)r.GetInt64(22),
            Gstin = r.IsDBNull(23) ? null : r.GetString(23),
            StateCode = r.IsDBNull(24) ? null : r.GetString(24),
        };
    }

    /// <summary>Reads the sales/purchase GST block (columns 25–28), or <c>null</c> when taxability is NULL.</summary>
    private static StockItemGstDetails? ReadSalesPurchaseGst(SqliteDataReader r)
    {
        if (r.IsDBNull(26)) return null; // sp_gst_taxability NULL = no block
        return new StockItemGstDetails
        {
            HsnSac = r.IsDBNull(25) ? null : r.GetString(25),
            Taxability = (GstTaxability)(int)r.GetInt64(26),
            RateBasisPoints = r.IsDBNull(27) ? (int?)null : (int)r.GetInt64(27),
            SupplyType = r.IsDBNull(28) ? GstSupplyType.Goods : (GstSupplyType)(int)r.GetInt64(28),
        };
    }

    /// <summary>Reads the tax-ledger classification (columns 29–30), or <c>null</c> for an ordinary ledger.</summary>
    private static LedgerGstClassification? ReadLedgerGstClassification(SqliteDataReader r)
    {
        if (r.IsDBNull(29) || r.IsDBNull(30)) return null;
        return new LedgerGstClassification((GstTaxHead)(int)r.GetInt64(29), (GstTaxDirection)(int)r.GetInt64(30));
    }

    /// <summary>
    /// Reads the interest block from the current ledger row (columns 12–20), or <c>null</c> when the ledger
    /// carries no block (<c>interest_enabled IS NULL</c>). Rate is stored as millis (rate% × 1000).
    /// </summary>
    private static InterestParameters? ReadInterest(SqliteDataReader r)
    {
        if (r.IsDBNull(12)) return null; // no interest block at all

        return new InterestParameters(
            enabled: r.GetInt64(12) != 0,
            ratePercent: r.GetInt64(13) / 1000m,
            per: (InterestPer)(int)r.GetInt64(14),
            onBalance: (InterestOnBalance)(int)r.GetInt64(15),
            applicability: (InterestApplicability)(int)r.GetInt64(16),
            calculateFrom: r.IsDBNull(17) ? (DateOnly?)null : ParseDate(r.GetString(17)),
            style: (InterestStyle)(int)r.GetInt64(18),
            roundingMethod: (InterestRoundingMethod)(int)r.GetInt64(19),
            roundingDecimals: (int)r.GetInt64(20));
    }

    private IEnumerable<VoucherType> ReadVoucherTypes(Guid companyId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, name, base_type, default_shortcut, numbering, abbreviation, is_active, is_predefined,
                   affects_accounts, affects_stock, use_as_manufacturing_journal, track_additional_costs,
                   allow_zero_valued
            FROM voucher_types WHERE company_id = $cid ORDER BY rowid;
            """;
        cmd.Parameters.AddWithValue("$cid", companyId.ToString("D"));
        using var r = cmd.ExecuteReader();
        var list = new List<VoucherType>();
        while (r.Read())
        {
            list.Add(new VoucherType(
                Guid.Parse(r.GetString(0)),
                r.GetString(1),
                (VoucherBaseType)(int)r.GetInt64(2),
                numbering: (NumberingMethod)(int)r.GetInt64(4),
                defaultShortcut: r.IsDBNull(3) ? null : r.GetString(3),
                abbreviation: r.IsDBNull(5) ? null : r.GetString(5),
                isActive: r.GetInt64(6) != 0,
                isPredefined: r.GetInt64(7) != 0,
                affectsAccounts: r.GetInt64(8) != 0,
                affectsStock: r.GetInt64(9) != 0,
                // v18 (RQ-11): "Use as Manufacturing Journal" flag, read verbatim.
                useAsManufacturingJournal: r.GetInt64(10) != 0,
                // v19 (RQ-16..RQ-20): "Track Additional Costs for Purchases" flag, read verbatim.
                trackAdditionalCosts: r.GetInt64(11) != 0,
                // v20 (RQ-21): "Allow zero-valued transactions" flag, read verbatim.
                allowZeroValuedTransactions: r.GetInt64(12) != 0));
        }
        return list;
    }

    private IEnumerable<GstRateSlab> ReadGstRateSlabs(Guid companyId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, rate_bp, label, is_predefined
            FROM gst_rate_slabs WHERE company_id = $cid ORDER BY rowid;
            """;
        cmd.Parameters.AddWithValue("$cid", companyId.ToString("D"));
        using var r = cmd.ExecuteReader();
        var list = new List<GstRateSlab>();
        while (r.Read())
            list.Add(new GstRateSlab(
                Guid.Parse(r.GetString(0)),
                (int)r.GetInt64(1),
                r.GetString(2),
                isPredefined: r.GetInt64(3) != 0));
        return list;
    }

    private IEnumerable<Currency> ReadCurrencies(Guid companyId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, symbol, formal_name, decimal_places, is_base
            FROM currencies WHERE company_id = $cid ORDER BY rowid;
            """;
        cmd.Parameters.AddWithValue("$cid", companyId.ToString("D"));
        using var r = cmd.ExecuteReader();
        var list = new List<Currency>();
        while (r.Read())
        {
            list.Add(new Currency(
                Guid.Parse(r.GetString(0)),
                r.GetString(1),
                r.GetString(2),
                decimalPlaces: (int)r.GetInt64(3),
                isBaseCurrency: r.GetInt64(4) != 0));
        }
        return list;
    }

    private IEnumerable<ExchangeRate> ReadExchangeRates(Guid companyId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, currency_id, rate_date, standard_rate_micro, selling_rate_micro, buying_rate_micro
            FROM exchange_rates WHERE company_id = $cid ORDER BY rowid;
            """;
        cmd.Parameters.AddWithValue("$cid", companyId.ToString("D"));
        using var r = cmd.ExecuteReader();
        var list = new List<ExchangeRate>();
        while (r.Read())
        {
            list.Add(new ExchangeRate(
                Guid.Parse(r.GetString(0)),
                Guid.Parse(r.GetString(1)),
                ParseDate(r.GetString(2)),
                standardRate: MicroToDecimal(r.GetInt64(3)),
                sellingRate: r.IsDBNull(4) ? (decimal?)null : MicroToDecimal(r.GetInt64(4)),
                buyingRate: r.IsDBNull(5) ? (decimal?)null : MicroToDecimal(r.GetInt64(5))));
        }
        return list;
    }

    private IEnumerable<CostCategory> ReadCostCategories(Guid companyId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, name, allocate_revenue, allocate_non_revenue, is_predefined
            FROM cost_categories WHERE company_id = $cid ORDER BY rowid;
            """;
        cmd.Parameters.AddWithValue("$cid", companyId.ToString("D"));
        using var r = cmd.ExecuteReader();
        var list = new List<CostCategory>();
        while (r.Read())
        {
            list.Add(new CostCategory(
                Guid.Parse(r.GetString(0)),
                r.GetString(1),
                allocateRevenueItems: r.GetInt64(2) != 0,
                allocateNonRevenueItems: r.GetInt64(3) != 0,
                isPredefined: r.GetInt64(4) != 0));
        }
        return list;
    }

    private IEnumerable<CostCentre> ReadCostCentres(Guid companyId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, name, category_id, parent_id, alias
            FROM cost_centres WHERE company_id = $cid ORDER BY rowid;
            """;
        cmd.Parameters.AddWithValue("$cid", companyId.ToString("D"));
        using var r = cmd.ExecuteReader();
        var list = new List<CostCentre>();
        while (r.Read())
        {
            list.Add(new CostCentre(
                Guid.Parse(r.GetString(0)),
                r.GetString(1),
                Guid.Parse(r.GetString(2)),
                parentId: r.IsDBNull(3) ? (Guid?)null : Guid.Parse(r.GetString(3)),
                alias: r.IsDBNull(4) ? null : r.GetString(4)));
        }
        return list;
    }

    private IEnumerable<Unit> ReadUnits(Guid companyId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, symbol, formal_name, is_compound, uqc, decimal_places,
                   first_unit_id, tail_unit_id, conversion_numerator, conversion_denominator
            FROM units WHERE company_id = $cid ORDER BY rowid;
            """;
        cmd.Parameters.AddWithValue("$cid", companyId.ToString("D"));
        using var r = cmd.ExecuteReader();
        var list = new List<Unit>();
        while (r.Read())
        {
            list.Add(Unit.FromStorage(
                Guid.Parse(r.GetString(0)),
                r.GetString(1),
                r.GetString(2),
                isCompound: r.GetInt64(3) != 0,
                unitQuantityCode: r.IsDBNull(4) ? null : r.GetString(4),
                decimalPlaces: (int)r.GetInt64(5),
                firstUnitId: r.IsDBNull(6) ? (Guid?)null : Guid.Parse(r.GetString(6)),
                tailUnitId: r.IsDBNull(7) ? (Guid?)null : Guid.Parse(r.GetString(7)),
                conversionNumerator: r.IsDBNull(8) ? (int?)null : (int)r.GetInt64(8),
                conversionDenominator: r.IsDBNull(9) ? (int?)null : (int)r.GetInt64(9)));
        }
        return list;
    }

    private IEnumerable<StockGroup> ReadStockGroups(Guid companyId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, name, parent_id, alias, add_quantities
            FROM stock_groups WHERE company_id = $cid ORDER BY rowid;
            """;
        cmd.Parameters.AddWithValue("$cid", companyId.ToString("D"));
        using var r = cmd.ExecuteReader();
        var list = new List<StockGroup>();
        while (r.Read())
        {
            list.Add(new StockGroup(
                Guid.Parse(r.GetString(0)),
                r.GetString(1),
                parentId: r.IsDBNull(2) ? (Guid?)null : Guid.Parse(r.GetString(2)),
                alias: r.IsDBNull(3) ? null : r.GetString(3),
                addQuantities: r.GetInt64(4) != 0));
        }
        return list;
    }

    private IEnumerable<StockCategory> ReadStockCategories(Guid companyId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, name, parent_id, alias
            FROM stock_categories WHERE company_id = $cid ORDER BY rowid;
            """;
        cmd.Parameters.AddWithValue("$cid", companyId.ToString("D"));
        using var r = cmd.ExecuteReader();
        var list = new List<StockCategory>();
        while (r.Read())
        {
            list.Add(new StockCategory(
                Guid.Parse(r.GetString(0)),
                r.GetString(1),
                parentId: r.IsDBNull(2) ? (Guid?)null : Guid.Parse(r.GetString(2)),
                alias: r.IsDBNull(3) ? null : r.GetString(3)));
        }
        return list;
    }

    private IEnumerable<Godown> ReadGodowns(Guid companyId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, name, parent_id, alias, third_party, is_main_location
            FROM godowns WHERE company_id = $cid ORDER BY rowid;
            """;
        cmd.Parameters.AddWithValue("$cid", companyId.ToString("D"));
        using var r = cmd.ExecuteReader();
        var list = new List<Godown>();
        while (r.Read())
        {
            list.Add(new Godown(
                Guid.Parse(r.GetString(0)),
                r.GetString(1),
                parentId: r.IsDBNull(2) ? (Guid?)null : Guid.Parse(r.GetString(2)),
                alias: r.IsDBNull(3) ? null : r.GetString(3),
                thirdParty: r.GetInt64(4) != 0,
                isMainLocation: r.GetInt64(5) != 0));
        }
        return list;
    }

    private IEnumerable<StockItem> ReadStockItems(Guid companyId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, name, stock_group_id, category_id, base_unit_id, alias, valuation_method,
                   hsn_sac_code, is_taxable, reorder_level_micro, min_order_qty_micro, standard_cost_paisa,
                   gst_hsn_sac, gst_taxability, gst_rate_bp, gst_supply_type,
                   maintain_in_batches, track_manufacturing_date, use_expiry_dates, set_components
            FROM stock_items WHERE company_id = $cid ORDER BY rowid;
            """;
        cmd.Parameters.AddWithValue("$cid", companyId.ToString("D"));
        using var r = cmd.ExecuteReader();
        var list = new List<StockItem>();
        while (r.Read())
        {
            var item = new StockItem(
                Guid.Parse(r.GetString(0)),
                r.GetString(1),
                stockGroupId: Guid.Parse(r.GetString(2)),
                baseUnitId: Guid.Parse(r.GetString(4)),
                categoryId: r.IsDBNull(3) ? (Guid?)null : Guid.Parse(r.GetString(3)),
                alias: r.IsDBNull(5) ? null : r.GetString(5),
                valuationMethod: (StockValuationMethod)(int)r.GetInt64(6),
                hsnSacCode: r.IsDBNull(7) ? null : r.GetString(7),
                isTaxable: r.GetInt64(8) != 0,
                reorderLevel: r.IsDBNull(9) ? (decimal?)null : QtyMicroToDecimal(r.GetInt64(9)),
                minimumOrderQuantity: r.IsDBNull(10) ? (decimal?)null : QtyMicroToDecimal(r.GetInt64(10)),
                standardCost: r.IsDBNull(11) ? (Money?)null : Paisa.ToMoney(r.GetInt64(11)),
                gst: ReadStockItemGst(r));
            // v16 batch switches (columns 16–18).
            item.MaintainInBatches = r.GetInt64(16) != 0;
            item.TrackManufacturingDate = r.GetInt64(17) != 0;
            item.UseExpiryDates = r.GetInt64(18) != 0;
            // v18 Set-Components (BOM) flag (column 19; RQ-10), read verbatim.
            item.SetComponents = r.GetInt64(19) != 0;
            list.Add(item);
        }
        return list;
    }

    private IEnumerable<BatchMaster> ReadBatchMasters(Guid companyId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, stock_item_id, batch_no, mfg_date, expiry_date, expiry_period,
                   godown_id, inward_qty_micro, inward_rate_paisa
            FROM batch_masters WHERE company_id = $cid ORDER BY rowid;
            """;
        cmd.Parameters.AddWithValue("$cid", companyId.ToString("D"));
        using var r = cmd.ExecuteReader();
        var list = new List<BatchMaster>();
        while (r.Read())
        {
            list.Add(new BatchMaster(
                Guid.Parse(r.GetString(0)),
                stockItemId: Guid.Parse(r.GetString(1)),
                batchNumber: r.GetString(2),
                manufacturingDate: r.IsDBNull(3) ? (DateOnly?)null : ParseDate(r.GetString(3)),
                expiryDate: r.IsDBNull(4) ? (DateOnly?)null : ParseDate(r.GetString(4)),
                expiryPeriod: r.IsDBNull(5) ? null : ExpiryPeriod.Parse(r.GetString(5)),
                godownId: r.IsDBNull(6) ? (Guid?)null : Guid.Parse(r.GetString(6)),
                inwardQuantity: r.IsDBNull(7) ? (decimal?)null : QtyMicroToDecimal(r.GetInt64(7)),
                inwardRate: r.IsDBNull(8) ? (Money?)null : Paisa.ToMoney(r.GetInt64(8))));
        }
        return list;
    }

    /// <summary>
    /// Reads the Bill-of-Materials masters (Phase 6 slice 2; RQ-9, DP-3) from the v17 <c>bill_of_materials</c> +
    /// <c>bom_lines</c> tables, reconstructing each header with its ordered lines. Micros (× 1,000,000) →
    /// exact decimal quantity / unit-of-manufacture; paisa → carve-out rate; millis (× 1,000) → carve-out percent
    /// — all lossless (ER-3). Lines are read in stored <c>line_order</c>.
    /// </summary>
    private IEnumerable<BillOfMaterials> ReadBillsOfMaterials(Guid companyId)
    {
        // Read the lines per BOM once, keyed by bom id (ordered by line_order), then assemble the headers.
        var linesByBom = new Dictionary<Guid, List<BomLine>>();
        using (var lineCmd = _connection.CreateCommand())
        {
            lineCmd.CommandText = """
                SELECT l.bom_id, l.line_type, l.component_stock_item_id, l.godown_id, l.qty_micro,
                       l.rate_paisa, l.percent_millis
                FROM bom_lines l
                JOIN bill_of_materials b ON b.id = l.bom_id
                WHERE b.company_id = $cid
                ORDER BY l.bom_id, l.line_order;
                """;
            lineCmd.Parameters.AddWithValue("$cid", companyId.ToString("D"));
            using var lr = lineCmd.ExecuteReader();
            while (lr.Read())
            {
                var bomId = Guid.Parse(lr.GetString(0));
                var line = new BomLine(
                    (BomLineType)(int)lr.GetInt64(1),
                    componentStockItemId: Guid.Parse(lr.GetString(2)),
                    quantityPerBlock: QtyMicroToDecimal(lr.GetInt64(4)),
                    godownId: lr.IsDBNull(3) ? (Guid?)null : Guid.Parse(lr.GetString(3)),
                    rate: lr.IsDBNull(5) ? (Money?)null : Paisa.ToMoney(lr.GetInt64(5)),
                    percentOfFinishedGoodCost: lr.IsDBNull(6) ? (decimal?)null : PercentMillisToDecimal(lr.GetInt64(6)));
                if (!linesByBom.TryGetValue(bomId, out var list)) linesByBom[bomId] = list = new List<BomLine>();
                list.Add(line);
            }
        }

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, stock_item_id, name, unit_of_manufacture_micro
            FROM bill_of_materials WHERE company_id = $cid ORDER BY rowid;
            """;
        cmd.Parameters.AddWithValue("$cid", companyId.ToString("D"));
        using var r = cmd.ExecuteReader();
        var boms = new List<BillOfMaterials>();
        while (r.Read())
        {
            var id = Guid.Parse(r.GetString(0));
            boms.Add(new BillOfMaterials(
                id,
                stockItemId: Guid.Parse(r.GetString(1)),
                name: r.GetString(2),
                unitOfManufacture: QtyMicroToDecimal(r.GetInt64(3)),
                lines: linesByBom.TryGetValue(id, out var lines) ? lines : new List<BomLine>()));
        }
        return boms;
    }

    /// <summary>Reads the item GST block (columns 12–15), or <c>null</c> when gst_taxability is NULL.</summary>
    private static StockItemGstDetails? ReadStockItemGst(SqliteDataReader r)
    {
        if (r.IsDBNull(13)) return null; // gst_taxability NULL = no GST block
        return new StockItemGstDetails
        {
            HsnSac = r.IsDBNull(12) ? null : r.GetString(12),
            Taxability = (GstTaxability)(int)r.GetInt64(13),
            RateBasisPoints = r.IsDBNull(14) ? (int?)null : (int)r.GetInt64(14),
            SupplyType = r.IsDBNull(15) ? GstSupplyType.Goods : (GstSupplyType)(int)r.GetInt64(15),
        };
    }

    private IEnumerable<StockOpeningBalance> ReadStockOpeningBalances(Guid companyId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, stock_item_id, godown_id, batch_label, quantity_micro, rate_paisa, mfg_date, expiry_date
            FROM stock_opening_balances WHERE company_id = $cid ORDER BY rowid;
            """;
        cmd.Parameters.AddWithValue("$cid", companyId.ToString("D"));
        using var r = cmd.ExecuteReader();
        var list = new List<StockOpeningBalance>();
        while (r.Read())
        {
            list.Add(new StockOpeningBalance(
                Guid.Parse(r.GetString(0)),
                stockItemId: Guid.Parse(r.GetString(1)),
                godownId: Guid.Parse(r.GetString(2)),
                quantity: QtyMicroToDecimal(r.GetInt64(4)),
                rate: Paisa.ToMoney(r.GetInt64(5)),
                batchLabel: r.IsDBNull(3) ? null : r.GetString(3),
                manufacturingDate: r.IsDBNull(6) ? (DateOnly?)null : ParseDate(r.GetString(6)),
                expiryDate: r.IsDBNull(7) ? (DateOnly?)null : ParseDate(r.GetString(7))));
        }
        return list;
    }

    private IEnumerable<InventoryVoucher> ReadInventoryVouchers(Guid companyId)
    {
        // Header rows first, then the four kinds of child line per voucher.
        var headers = new List<(Guid Id, Guid TypeId, int Number, DateOnly Date, string? Narration,
            Guid? PartyId, bool Cancelled, bool PostDated)>();
        using (var cmd = _connection.CreateCommand())
        {
            cmd.CommandText = """
                SELECT id, type_id, number, date, narration, party_id, cancelled, post_dated
                FROM inventory_vouchers WHERE company_id = $cid ORDER BY rowid;
                """;
            cmd.Parameters.AddWithValue("$cid", companyId.ToString("D"));
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                headers.Add((
                    Guid.Parse(r.GetString(0)),
                    Guid.Parse(r.GetString(1)),
                    (int)r.GetInt64(2),
                    ParseDate(r.GetString(3)),
                    r.IsDBNull(4) ? null : r.GetString(4),
                    r.IsDBNull(5) ? (Guid?)null : Guid.Parse(r.GetString(5)),
                    r.GetInt64(6) != 0,
                    r.GetInt64(7) != 0));
            }
        }

        var result = new List<InventoryVoucher>();
        foreach (var h in headers)
        {
            var (source, destination) = ReadInventoryAllocations(h.Id);
            result.Add(InventoryVoucher.FromStorage(
                h.Id, h.TypeId, h.Date,
                allocations: source,
                destinationAllocations: destination,
                orderLines: ReadOrderLines(h.Id),
                physicalLines: ReadPhysicalStockLines(h.Id),
                number: h.Number,
                narration: h.Narration,
                partyId: h.PartyId,
                cancelled: h.Cancelled,
                postDated: h.PostDated,
                additionalCostLines: ReadAdditionalCostLines(h.Id)));
        }
        return result;
    }

    private List<AdditionalCostLine> ReadAdditionalCostLines(Guid voucherId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT ledger_id, amount_paisa
            FROM additional_cost_lines WHERE inventory_voucher_id = $vid ORDER BY line_order, id;
            """;
        cmd.Parameters.AddWithValue("$vid", voucherId.ToString("D"));
        using var r = cmd.ExecuteReader();
        var list = new List<AdditionalCostLine>();
        while (r.Read())
            list.Add(new AdditionalCostLine(Guid.Parse(r.GetString(0)), Paisa.ToMoney(r.GetInt64(1))));
        return list;
    }

    private (List<InventoryAllocation> Source, List<InventoryAllocation> Destination) ReadInventoryAllocations(Guid voucherId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT role, stock_item_id, godown_id, unit_id, quantity_micro, direction, rate_paisa, batch_label
            FROM inventory_allocations WHERE inventory_voucher_id = $vid ORDER BY line_order, id;
            """;
        cmd.Parameters.AddWithValue("$vid", voucherId.ToString("D"));
        using var r = cmd.ExecuteReader();
        var source = new List<InventoryAllocation>();
        var destination = new List<InventoryAllocation>();
        while (r.Read())
        {
            var alloc = new InventoryAllocation(
                Guid.Parse(r.GetString(1)),
                Guid.Parse(r.GetString(2)),
                QtyMicroToDecimal(r.GetInt64(4)),
                (StockDirection)(int)r.GetInt64(5),
                rate: r.IsDBNull(6) ? null : Paisa.ToMoney(r.GetInt64(6)),
                batchLabel: r.IsDBNull(7) ? null : r.GetString(7),
                unitId: r.IsDBNull(3) ? (Guid?)null : Guid.Parse(r.GetString(3)));
            if (r.GetInt64(0) == 1) destination.Add(alloc);
            else source.Add(alloc);
        }
        return (source, destination);
    }

    private List<OrderLine> ReadOrderLines(Guid voucherId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT stock_item_id, godown_id, quantity_micro, rate_paisa
            FROM order_lines WHERE inventory_voucher_id = $vid ORDER BY line_order, id;
            """;
        cmd.Parameters.AddWithValue("$vid", voucherId.ToString("D"));
        using var r = cmd.ExecuteReader();
        var list = new List<OrderLine>();
        while (r.Read())
        {
            list.Add(new OrderLine(
                Guid.Parse(r.GetString(0)),
                Guid.Parse(r.GetString(1)),
                QtyMicroToDecimal(r.GetInt64(2)),
                r.IsDBNull(3) ? null : Paisa.ToMoney(r.GetInt64(3))));
        }
        return list;
    }

    private List<PhysicalStockLine> ReadPhysicalStockLines(Guid voucherId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT stock_item_id, godown_id, counted_qty_micro, batch_label
            FROM physical_stock_lines WHERE inventory_voucher_id = $vid ORDER BY line_order, id;
            """;
        cmd.Parameters.AddWithValue("$vid", voucherId.ToString("D"));
        using var r = cmd.ExecuteReader();
        var list = new List<PhysicalStockLine>();
        while (r.Read())
        {
            list.Add(new PhysicalStockLine(
                Guid.Parse(r.GetString(0)),
                Guid.Parse(r.GetString(1)),
                QtyMicroToDecimal(r.GetInt64(2)),
                r.IsDBNull(3) ? null : r.GetString(3)));
        }
        return list;
    }

    private IEnumerable<Budget> ReadBudgets(Guid companyId)
    {
        // Header rows first, then lines per budget (ordered), to build each aggregate.
        var headers = new List<(Guid Id, string Name, Guid? UnderId, DateOnly From, DateOnly To)>();
        using (var cmd = _connection.CreateCommand())
        {
            cmd.CommandText = """
                SELECT id, name, under_id, period_from, period_to
                FROM budgets WHERE company_id = $cid ORDER BY rowid;
                """;
            cmd.Parameters.AddWithValue("$cid", companyId.ToString("D"));
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                headers.Add((
                    Guid.Parse(r.GetString(0)),
                    r.GetString(1),
                    r.IsDBNull(2) ? (Guid?)null : Guid.Parse(r.GetString(2)),
                    ParseDate(r.GetString(3)),
                    ParseDate(r.GetString(4))));
            }
        }

        var result = new List<Budget>();
        foreach (var h in headers)
            result.Add(new Budget(h.Id, h.Name, h.From, h.To, underId: h.UnderId, lines: ReadBudgetLines(h.Id)));
        return result;
    }

    private List<BudgetLine> ReadBudgetLines(Guid budgetId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT group_id, ledger_id, budget_type, amount_paisa
            FROM budget_lines WHERE budget_id = $bid ORDER BY line_order, id;
            """;
        cmd.Parameters.AddWithValue("$bid", budgetId.ToString("D"));
        using var r = cmd.ExecuteReader();
        var list = new List<BudgetLine>();
        while (r.Read())
        {
            var type = (BudgetType)(int)r.GetInt64(2);
            var amount = Paisa.ToMoney(r.GetInt64(3));
            list.Add(r.IsDBNull(0)
                ? BudgetLine.ForLedger(Guid.Parse(r.GetString(1)), type, amount)
                : BudgetLine.ForGroup(Guid.Parse(r.GetString(0)), type, amount));
        }
        return list;
    }

    private IEnumerable<Scenario> ReadScenarios(Guid companyId)
    {
        // Header rows first, then include/exclude voucher-type rows per scenario.
        var headers = new List<(Guid Id, string Name, bool IncludeActuals)>();
        using (var cmd = _connection.CreateCommand())
        {
            cmd.CommandText = """
                SELECT id, name, include_actuals
                FROM scenarios WHERE company_id = $cid ORDER BY rowid;
                """;
            cmd.Parameters.AddWithValue("$cid", companyId.ToString("D"));
            using var r = cmd.ExecuteReader();
            while (r.Read())
                headers.Add((Guid.Parse(r.GetString(0)), r.GetString(1), r.GetInt64(2) != 0));
        }

        var result = new List<Scenario>();
        foreach (var h in headers)
        {
            var (included, excluded) = ReadScenarioVoucherTypes(h.Id);
            result.Add(new Scenario(h.Id, h.Name, h.IncludeActuals, included, excluded));
        }
        return result;
    }

    private (List<Guid> Included, List<Guid> Excluded) ReadScenarioVoucherTypes(Guid scenarioId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT voucher_type_id, is_included
            FROM scenario_voucher_types WHERE scenario_id = $sid ORDER BY id;
            """;
        cmd.Parameters.AddWithValue("$sid", scenarioId.ToString("D"));
        using var r = cmd.ExecuteReader();
        var included = new List<Guid>();
        var excluded = new List<Guid>();
        while (r.Read())
        {
            var id = Guid.Parse(r.GetString(0));
            if (r.GetInt64(1) != 0) included.Add(id);
            else excluded.Add(id);
        }
        return (included, excluded);
    }

    private IEnumerable<Voucher> ReadVouchers(Guid companyId)
    {
        // Header rows first, then lines per voucher (ordered), to build each aggregate.
        var headers = new List<(Guid Id, Guid TypeId, int Number, DateOnly Date, string? Narration,
            Guid? PartyId, bool Cancelled, bool Optional, bool PostDated, DateOnly? ApplicableUpto)>();

        using (var cmd = _connection.CreateCommand())
        {
            cmd.CommandText = """
                SELECT id, type_id, number, date, narration, party_id, cancelled, optional, post_dated,
                       applicable_upto
                FROM vouchers WHERE company_id = $cid ORDER BY rowid;
                """;
            cmd.Parameters.AddWithValue("$cid", companyId.ToString("D"));
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                headers.Add((
                    Guid.Parse(r.GetString(0)),
                    Guid.Parse(r.GetString(1)),
                    (int)r.GetInt64(2),
                    ParseDate(r.GetString(3)),
                    r.IsDBNull(4) ? null : r.GetString(4),
                    r.IsDBNull(5) ? (Guid?)null : Guid.Parse(r.GetString(5)),
                    r.GetInt64(6) != 0,
                    r.GetInt64(7) != 0,
                    r.GetInt64(8) != 0,
                    r.IsDBNull(9) ? (DateOnly?)null : ParseDate(r.GetString(9))));
            }
        }

        var result = new List<Voucher>();
        foreach (var h in headers)
        {
            var lines = ReadEntryLines(h.Id);
            var inventoryLines = ReadVoucherInventoryLines(h.Id);
            result.Add(new Voucher(
                h.Id, h.TypeId, h.Date, lines,
                number: h.Number,
                narration: h.Narration,
                partyId: h.PartyId,
                cancelled: h.Cancelled,
                optional: h.Optional,
                postDated: h.PostDated,
                applicableUpto: h.ApplicableUpto,
                inventoryLines: inventoryLines.Count > 0 ? inventoryLines : null));
        }
        return result;
    }

    private List<VoucherInventoryLine> ReadVoucherInventoryLines(Guid voucherId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT stock_item_id, godown_id, quantity_micro, direction, rate_paisa, batch_label,
                   billed_qty_micro
            FROM voucher_inventory_lines WHERE voucher_id = $vid ORDER BY line_order, id;
            """;
        cmd.Parameters.AddWithValue("$vid", voucherId.ToString("D"));
        using var r = cmd.ExecuteReader();
        var list = new List<VoucherInventoryLine>();
        while (r.Read())
        {
            // v20 (RQ-22/RQ-23): quantity_micro is the Actual (stock) quantity; Billed = billed_qty_micro when
            // present, else defaults to Actual (feature off / no split — byte-identical, ER-13).
            var actual = QtyMicroToDecimal(r.GetInt64(2));
            var billed = r.IsDBNull(6) ? actual : QtyMicroToDecimal(r.GetInt64(6));
            list.Add(new VoucherInventoryLine(
                Guid.Parse(r.GetString(0)),
                Guid.Parse(r.GetString(1)),
                actual,
                Paisa.ToMoney(r.GetInt64(4)),
                (StockDirection)(int)r.GetInt64(3),
                batchLabel: r.IsDBNull(5) ? null : r.GetString(5),
                billedQuantity: billed));
        }
        return list;
    }

    private List<EntryLine> ReadEntryLines(Guid voucherId)
    {
        // Read the raw line rows first (capturing the id), then load each line's bill allocations.
        var raw = new List<(long Id, Guid LedgerId, long AmountPaisa, DrCr Side, ForexInfo? Forex, GstLineTax? Gst)>();
        using (var cmd = _connection.CreateCommand())
        {
            cmd.CommandText = """
                SELECT id, ledger_id, amount_paisa, side,
                       forex_currency_id, forex_amount_micro, forex_rate_micro,
                       gst_tax_head, gst_rate_bp, gst_taxable_value_paisa
                FROM entry_lines WHERE voucher_id = $vid ORDER BY line_order, id;
                """;
            cmd.Parameters.AddWithValue("$vid", voucherId.ToString("D"));
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                ForexInfo? forex = null;
                if (!r.IsDBNull(4))
                {
                    forex = new ForexInfo(
                        Guid.Parse(r.GetString(4)),
                        Money.FromRupees(MicroToDecimal(r.GetInt64(5))),
                        MicroToDecimal(r.GetInt64(6)));
                }

                // v13 GST tax-line detail: present iff gst_tax_head is set.
                GstLineTax? gst = null;
                if (!r.IsDBNull(7))
                {
                    gst = new GstLineTax(
                        (GstTaxHead)(int)r.GetInt64(7),
                        (int)r.GetInt64(8),
                        Paisa.ToMoney(r.GetInt64(9)));
                }

                raw.Add((
                    r.GetInt64(0),
                    Guid.Parse(r.GetString(1)),
                    r.GetInt64(2),
                    (DrCr)(int)r.GetInt64(3),
                    forex,
                    gst));
            }
        }

        var lines = new List<EntryLine>(raw.Count);
        foreach (var (id, ledgerId, amountPaisa, side, forex, gst) in raw)
        {
            var allocs = ReadBillAllocations(id);
            var costAllocs = ReadCostAllocations(id);
            var bankAlloc = ReadBankAllocation(id);
            lines.Add(new EntryLine(
                ledgerId,
                Paisa.ToMoney(amountPaisa),
                side,
                allocs.Count > 0 ? allocs : null,
                costAllocs.Count > 0 ? costAllocs : null,
                bankAlloc,
                forex,
                gst));
        }
        return lines;
    }

    private List<BillAllocation> ReadBillAllocations(long entryLineId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT ref_type, name, amount_paisa, due_date, credit_days
            FROM bill_allocations WHERE entry_line_id = $lid ORDER BY alloc_order, id;
            """;
        cmd.Parameters.AddWithValue("$lid", entryLineId);
        using var r = cmd.ExecuteReader();
        var list = new List<BillAllocation>();
        while (r.Read())
        {
            list.Add(new BillAllocation(
                (BillRefType)(int)r.GetInt64(0),
                r.GetString(1),
                Paisa.ToMoney(r.GetInt64(2)),
                dueDate: r.IsDBNull(3) ? (DateOnly?)null : ParseDate(r.GetString(3)),
                creditPeriodDays: r.IsDBNull(4) ? (int?)null : (int)r.GetInt64(4)));
        }
        return list;
    }

    private List<CostAllocation> ReadCostAllocations(long entryLineId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT category_id, centre_id, amount_paisa
            FROM cost_allocations WHERE entry_line_id = $lid ORDER BY alloc_order, id;
            """;
        cmd.Parameters.AddWithValue("$lid", entryLineId);
        using var r = cmd.ExecuteReader();
        var list = new List<CostAllocation>();
        while (r.Read())
        {
            list.Add(new CostAllocation(
                Guid.Parse(r.GetString(0)),
                Guid.Parse(r.GetString(1)),
                Paisa.ToMoney(r.GetInt64(2))));
        }
        return list;
    }

    /// <summary>Reads the (at most one) bank allocation for an entry line, or <c>null</c> if none.</summary>
    private BankAllocation? ReadBankAllocation(long entryLineId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT transaction_type, instrument_number, instrument_date, bank_date
            FROM bank_allocations WHERE entry_line_id = $lid ORDER BY id LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$lid", entryLineId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return new BankAllocation(
            (BankTransactionType)(int)r.GetInt64(0),
            instrumentNumber: r.GetString(1),
            instrumentDate: r.IsDBNull(2) ? (DateOnly?)null : ParseDate(r.GetString(2)),
            bankDate: r.IsDBNull(3) ? (DateOnly?)null : ParseDate(r.GetString(3)));
    }

    // ------------------------------------------------------------------ writers

    private void DeleteCompanyRows(SqliteTransaction tx, Guid companyId)
    {
        // Child-first so foreign keys are satisfied. Break the company→head fk before deleting groups.
        var cid = companyId.ToString("D");
        ExecTx(tx, "UPDATE companies SET profit_and_loss_head_id = NULL WHERE id = $cid;", ("$cid", cid));
        ExecTx(tx, """
            DELETE FROM bill_allocations WHERE entry_line_id IN (
                SELECT el.id FROM entry_lines el
                JOIN vouchers v ON v.id = el.voucher_id
                WHERE v.company_id = $cid);
            """, ("$cid", cid));
        ExecTx(tx, """
            DELETE FROM cost_allocations WHERE entry_line_id IN (
                SELECT el.id FROM entry_lines el
                JOIN vouchers v ON v.id = el.voucher_id
                WHERE v.company_id = $cid);
            """, ("$cid", cid));
        ExecTx(tx, """
            DELETE FROM bank_allocations WHERE entry_line_id IN (
                SELECT el.id FROM entry_lines el
                JOIN vouchers v ON v.id = el.voucher_id
                WHERE v.company_id = $cid);
            """, ("$cid", cid));
        // Item-invoice stock lines FK the accounting voucher + stock masters → delete before vouchers and the
        // stock masters below.
        ExecTx(tx, """
            DELETE FROM voucher_inventory_lines WHERE voucher_id IN (
                SELECT id FROM vouchers WHERE company_id = $cid);
            """, ("$cid", cid));
        ExecTx(tx, "DELETE FROM entry_lines WHERE voucher_id IN (SELECT id FROM vouchers WHERE company_id = $cid);", ("$cid", cid));
        ExecTx(tx, "DELETE FROM vouchers WHERE company_id = $cid;", ("$cid", cid));
        // Inventory & order vouchers: child lines FK the header; the header FKs voucher_types + a party ledger
        // + stock masters → delete these before voucher_types / ledgers / stock masters below.
        ExecTx(tx, """
            DELETE FROM inventory_allocations WHERE inventory_voucher_id IN (
                SELECT id FROM inventory_vouchers WHERE company_id = $cid);
            """, ("$cid", cid));
        ExecTx(tx, """
            DELETE FROM order_lines WHERE inventory_voucher_id IN (
                SELECT id FROM inventory_vouchers WHERE company_id = $cid);
            """, ("$cid", cid));
        ExecTx(tx, """
            DELETE FROM physical_stock_lines WHERE inventory_voucher_id IN (
                SELECT id FROM inventory_vouchers WHERE company_id = $cid);
            """, ("$cid", cid));
        // v19 additional-cost lines FK the inventory-voucher header AND a ledger → delete before the header and
        // before ledgers below (FK order).
        ExecTx(tx, """
            DELETE FROM additional_cost_lines WHERE inventory_voucher_id IN (
                SELECT id FROM inventory_vouchers WHERE company_id = $cid);
            """, ("$cid", cid));
        ExecTx(tx, "DELETE FROM inventory_vouchers WHERE company_id = $cid;", ("$cid", cid));
        // Budgets (and their lines) FK groups + ledgers → delete them before those masters.
        ExecTx(tx, """
            DELETE FROM budget_lines WHERE budget_id IN (SELECT id FROM budgets WHERE company_id = $cid);
            """, ("$cid", cid));
        ExecTx(tx, "DELETE FROM budgets WHERE company_id = $cid;", ("$cid", cid));
        // Scenarios (and their voucher-type rows) FK voucher_types → delete them before those masters.
        ExecTx(tx, """
            DELETE FROM scenario_voucher_types WHERE scenario_id IN (
                SELECT id FROM scenarios WHERE company_id = $cid);
            """, ("$cid", cid));
        ExecTx(tx, "DELETE FROM scenarios WHERE company_id = $cid;", ("$cid", cid));
        ExecTx(tx, "DELETE FROM ledgers WHERE company_id = $cid;", ("$cid", cid));
        // Exchange rates FK currencies; ledgers + entry-line forex FK currencies → after those are gone.
        ExecTx(tx, "DELETE FROM exchange_rates WHERE company_id = $cid;", ("$cid", cid));
        ExecTx(tx, "DELETE FROM currencies WHERE company_id = $cid;", ("$cid", cid));
        ExecTx(tx, "DELETE FROM voucher_types WHERE company_id = $cid;", ("$cid", cid));
        // Cost centres reference cost categories → delete centres first.
        ExecTx(tx, "DELETE FROM cost_centres WHERE company_id = $cid;", ("$cid", cid));
        ExecTx(tx, "DELETE FROM cost_categories WHERE company_id = $cid;", ("$cid", cid));
        // Inventory: openings FK items+godowns; items FK groups/categories/units → delete child-first.
        ExecTx(tx, "DELETE FROM stock_opening_balances WHERE company_id = $cid;", ("$cid", cid));
        // Bill-of-Materials lines FK the header; the header FKs stock_items + godowns → delete lines, then headers,
        // before items + godowns below.
        ExecTx(tx, """
            DELETE FROM bom_lines WHERE bom_id IN (SELECT id FROM bill_of_materials WHERE company_id = $cid);
            """, ("$cid", cid));
        ExecTx(tx, "DELETE FROM bill_of_materials WHERE company_id = $cid;", ("$cid", cid));
        // Batch masters FK stock_items + godowns; the batch_id-bearing line tables (openings/allocations/physical/
        // item-invoice) were all deleted above → safe to drop batch masters before items + godowns.
        ExecTx(tx, "DELETE FROM batch_masters WHERE company_id = $cid;", ("$cid", cid));
        ExecTx(tx, "DELETE FROM stock_items WHERE company_id = $cid;", ("$cid", cid));
        ExecTx(tx, "DELETE FROM stock_categories WHERE company_id = $cid;", ("$cid", cid));
        ExecTx(tx, "DELETE FROM stock_groups WHERE company_id = $cid;", ("$cid", cid));
        ExecTx(tx, "DELETE FROM godowns WHERE company_id = $cid;", ("$cid", cid));
        ExecTx(tx, "DELETE FROM units WHERE company_id = $cid;", ("$cid", cid));
        ExecTx(tx, "DELETE FROM groups WHERE company_id = $cid;", ("$cid", cid));
        // v13 GST rate slabs FK companies → delete before the company row.
        ExecTx(tx, "DELETE FROM gst_rate_slabs WHERE company_id = $cid;", ("$cid", cid));
        ExecTx(tx, "DELETE FROM companies WHERE id = $cid;", ("$cid", cid));
    }

    private void InsertCompany(SqliteTransaction tx, Company c)
    {
        using var cmd = _connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO companies
                (id, name, mailing_name, address, country, state, pin,
                 financial_year_start, books_begin_from, base_currency_symbol,
                 base_currency_name, decimal_places, decimal_unit_name,
                 primary_cost_category, main_location, profit_and_loss_head_id,
                 gst_enabled, gstin, gst_home_state, gst_reg_type, gst_applicable_from, gst_periodicity,
                 use_separate_actual_billed_qty)
            VALUES
                ($id, $name, $mail, $addr, $country, $state, $pin,
                 $fy, $books, $sym, $curname, $dp, $unit, $pcc, $loc, NULL,
                 $gsten, $gstin, $gsthome, $gstreg, $gstfrom, $gstper,
                 $abqty);
            """;
        cmd.Parameters.AddWithValue("$id", c.Id.ToString("D"));
        cmd.Parameters.AddWithValue("$name", c.Name);
        cmd.Parameters.AddWithValue("$mail", c.MailingName);
        cmd.Parameters.AddWithValue("$addr", (object?)c.Address ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$country", c.Country);
        cmd.Parameters.AddWithValue("$state", (object?)c.State ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$pin", (object?)c.Pin ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$fy", FormatDate(c.FinancialYearStart));
        cmd.Parameters.AddWithValue("$books", FormatDate(c.BooksBeginFrom));
        cmd.Parameters.AddWithValue("$sym", c.BaseCurrencySymbol);
        cmd.Parameters.AddWithValue("$curname", c.BaseCurrencyName);
        cmd.Parameters.AddWithValue("$dp", c.DecimalPlaces);
        cmd.Parameters.AddWithValue("$unit", c.DecimalUnitName);
        cmd.Parameters.AddWithValue("$pcc", c.PrimaryCostCategoryName);
        cmd.Parameters.AddWithValue("$loc", c.MainLocationName);

        // v13 core GST config: all NULL / 0 for a non-GST company (default), so existing companies are unchanged.
        var gst = c.Gst;
        cmd.Parameters.AddWithValue("$gsten", gst is { Enabled: true } ? 1 : 0);
        cmd.Parameters.AddWithValue("$gstin", (object?)gst?.Gstin ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$gsthome", (object?)gst?.HomeStateCode ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$gstreg", gst is null ? (object)DBNull.Value : (int)gst.RegistrationType);
        cmd.Parameters.AddWithValue("$gstfrom", gst?.ApplicableFrom is { } af ? FormatDate(af) : (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$gstper", gst is null ? (object)DBNull.Value : (int)gst.Periodicity);
        // v20 (RQ-22): the F11 Actual/Billed toggle, written verbatim (default 0 for an existing company — ER-13).
        cmd.Parameters.AddWithValue("$abqty", c.UseSeparateActualBilledQuantity ? 1 : 0);
        cmd.ExecuteNonQuery();

        // v13 GST rate slabs (the seeded config-driven slabs), if any.
        if (gst is not null)
            foreach (var slab in gst.RateSlabs)
            {
                using var s = _connection.CreateCommand();
                s.Transaction = tx;
                s.CommandText = """
                    INSERT INTO gst_rate_slabs (id, company_id, rate_bp, label, is_predefined)
                    VALUES ($id, $cid, $bp, $label, $pre);
                    """;
                s.Parameters.AddWithValue("$id", slab.Id.ToString("D"));
                s.Parameters.AddWithValue("$cid", c.Id.ToString("D"));
                s.Parameters.AddWithValue("$bp", slab.RateBasisPoints);
                s.Parameters.AddWithValue("$label", slab.Label);
                s.Parameters.AddWithValue("$pre", slab.IsPredefined ? 1 : 0);
                s.ExecuteNonQuery();
            }
    }

    private void InsertGroups(SqliteTransaction tx, Company c)
    {
        foreach (var g in c.Groups)
            InsertGroup(tx, c.Id, g, isPlHead: false);

        if (c.ProfitAndLossHead is not null)
            InsertGroup(tx, c.Id, c.ProfitAndLossHead, isPlHead: true);
    }

    private void InsertGroup(SqliteTransaction tx, Guid companyId, Group g, bool isPlHead)
    {
        using var cmd = _connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO groups (id, company_id, name, nature, parent_id, alias, is_predefined, is_pl_head)
            VALUES ($id, $cid, $name, $nature, $parent, $alias, $pre, $plhead);
            """;
        cmd.Parameters.AddWithValue("$id", g.Id.ToString("D"));
        cmd.Parameters.AddWithValue("$cid", companyId.ToString("D"));
        cmd.Parameters.AddWithValue("$name", g.Name);
        cmd.Parameters.AddWithValue("$nature", (int)g.Nature);
        cmd.Parameters.AddWithValue("$parent", (object?)g.ParentId?.ToString("D") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$alias", (object?)g.Alias ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$pre", g.IsPredefined ? 1 : 0);
        cmd.Parameters.AddWithValue("$plhead", isPlHead ? 1 : 0);
        cmd.ExecuteNonQuery();
    }

    private void SetProfitAndLossHead(SqliteTransaction tx, Company c)
    {
        if (c.ProfitAndLossHead is null) return;
        ExecTx(tx,
            "UPDATE companies SET profit_and_loss_head_id = $head WHERE id = $cid;",
            ("$head", c.ProfitAndLossHead.Id.ToString("D")),
            ("$cid", c.Id.ToString("D")));
    }

    private void InsertLedgers(SqliteTransaction tx, Company c)
    {
        foreach (var l in c.Ledgers)
        {
            using var cmd = _connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO ledgers
                    (id, company_id, name, group_id, opening_balance_paisa, opening_is_debit, alias, is_predefined,
                     maintain_bill_by_bill, default_credit_period, cost_applicable,
                     enable_cheque_printing, cheque_bank_name,
                     interest_enabled, interest_rate_millis, interest_per, interest_on_balance,
                     interest_applicability, interest_calc_from, interest_style,
                     interest_round_method, interest_round_decimals, currency_id,
                     party_gst_reg_type, party_gstin, party_gst_state,
                     sp_gst_hsn, sp_gst_taxability, sp_gst_rate_bp, sp_gst_supply_type,
                     gst_tax_head, gst_tax_direction, method_of_appropriation)
                VALUES ($id, $cid, $name, $gid, $ob, $od, $alias, $pre, $bbb, $dcp, $cca, $ecp, $cbn,
                        $ien, $irate, $iper, $ion, $iapp, $icf, $istyle, $irm, $ird, $curid,
                        $pgreg, $pgstin, $pgstate, $sphsn, $sptax, $sprate, $spsup, $gthead, $gtdir, $moa);
                """;
            cmd.Parameters.AddWithValue("$id", l.Id.ToString("D"));
            cmd.Parameters.AddWithValue("$cid", c.Id.ToString("D"));
            cmd.Parameters.AddWithValue("$name", l.Name);
            cmd.Parameters.AddWithValue("$gid", l.GroupId.ToString("D"));
            cmd.Parameters.AddWithValue("$ob", Paisa.FromMoney(l.OpeningBalance));
            cmd.Parameters.AddWithValue("$od", l.OpeningIsDebit ? 1 : 0);
            cmd.Parameters.AddWithValue("$alias", (object?)l.Alias ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$pre", l.IsPredefined ? 1 : 0);
            cmd.Parameters.AddWithValue("$bbb", l.MaintainBillByBill ? 1 : 0);
            cmd.Parameters.AddWithValue("$dcp", (object?)l.DefaultCreditPeriodDays ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$cca", l.CostCentresApplicable is { } cca ? (cca ? 1 : 0) : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$ecp", l.EnableChequePrinting ? 1 : 0);
            cmd.Parameters.AddWithValue("$cbn", (object?)l.ChequePrintingBankName ?? DBNull.Value);

            // v7 interest block: all NULL when the ledger has no block, so existing ledgers stay off.
            var ip = l.Interest;
            cmd.Parameters.AddWithValue("$ien", ip is null ? (object)DBNull.Value : (ip.Enabled ? 1 : 0));
            cmd.Parameters.AddWithValue("$irate", ip is null ? (object)DBNull.Value : RateToMillis(ip.RatePercent));
            cmd.Parameters.AddWithValue("$iper", ip is null ? (object)DBNull.Value : (int)ip.Per);
            cmd.Parameters.AddWithValue("$ion", ip is null ? (object)DBNull.Value : (int)ip.OnBalance);
            cmd.Parameters.AddWithValue("$iapp", ip is null ? (object)DBNull.Value : (int)ip.Applicability);
            cmd.Parameters.AddWithValue("$icf",
                ip?.CalculateFrom is { } cf ? FormatDate(cf) : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$istyle", ip is null ? (object)DBNull.Value : (int)ip.Style);
            cmd.Parameters.AddWithValue("$irm", ip is null ? (object)DBNull.Value : (int)ip.RoundingMethod);
            cmd.Parameters.AddWithValue("$ird", ip is null ? (object)DBNull.Value : ip.RoundingDecimals);

            // v8 multi-currency: the ledger's currency (NULL = base).
            cmd.Parameters.AddWithValue("$curid", (object?)l.CurrencyId?.ToString("D") ?? DBNull.Value);

            // v13 party GST details (NULL when the ledger has no party GST block).
            var pg = l.PartyGst;
            cmd.Parameters.AddWithValue("$pgreg", pg is null ? (object)DBNull.Value : (int)pg.RegistrationType);
            cmd.Parameters.AddWithValue("$pgstin", (object?)pg?.Gstin ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$pgstate", (object?)pg?.StateCode ?? DBNull.Value);

            // v13 sales/purchase-ledger GST block (NULL taxability = no block).
            var sp = l.SalesPurchaseGst;
            cmd.Parameters.AddWithValue("$sphsn", (object?)sp?.HsnSac ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$sptax", sp is null ? (object)DBNull.Value : (int)sp.Taxability);
            cmd.Parameters.AddWithValue("$sprate", sp?.RateBasisPoints is { } sprbp ? sprbp : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$spsup", sp is null ? (object)DBNull.Value : (int)sp.SupplyType);

            // v13 tax-ledger classification (NULL for an ordinary ledger).
            var gc = l.GstClassification;
            cmd.Parameters.AddWithValue("$gthead", gc is null ? (object)DBNull.Value : (int)gc.TaxHead);
            cmd.Parameters.AddWithValue("$gtdir", gc is null ? (object)DBNull.Value : (int)gc.Direction);

            // v19 additional-cost ledger method (NULL for a plain P&L ledger).
            cmd.Parameters.AddWithValue("$moa", l.MethodOfAppropriation is { } moa ? (int)moa : (object)DBNull.Value);
            cmd.ExecuteNonQuery();
        }
    }

    private void InsertCurrencies(SqliteTransaction tx, Company c)
    {
        foreach (var cur in c.Currencies)
        {
            using var cmd = _connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO currencies (id, company_id, symbol, formal_name, decimal_places, is_base)
                VALUES ($id, $cid, $sym, $fn, $dp, $base);
                """;
            cmd.Parameters.AddWithValue("$id", cur.Id.ToString("D"));
            cmd.Parameters.AddWithValue("$cid", c.Id.ToString("D"));
            cmd.Parameters.AddWithValue("$sym", cur.Symbol);
            cmd.Parameters.AddWithValue("$fn", cur.FormalName);
            cmd.Parameters.AddWithValue("$dp", cur.DecimalPlaces);
            cmd.Parameters.AddWithValue("$base", cur.IsBaseCurrency ? 1 : 0);
            cmd.ExecuteNonQuery();
        }
    }

    private void InsertExchangeRates(SqliteTransaction tx, Company c)
    {
        foreach (var rate in c.ExchangeRates)
        {
            using var cmd = _connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO exchange_rates
                    (id, company_id, currency_id, rate_date, standard_rate_micro, selling_rate_micro, buying_rate_micro)
                VALUES ($id, $cid, $cur, $date, $std, $sell, $buy);
                """;
            cmd.Parameters.AddWithValue("$id", rate.Id.ToString("D"));
            cmd.Parameters.AddWithValue("$cid", c.Id.ToString("D"));
            cmd.Parameters.AddWithValue("$cur", rate.CurrencyId.ToString("D"));
            cmd.Parameters.AddWithValue("$date", FormatDate(rate.Date));
            cmd.Parameters.AddWithValue("$std", MicroFromDecimal(rate.StandardRate));
            cmd.Parameters.AddWithValue("$sell", rate.SellingRate is { } s ? MicroFromDecimal(s) : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$buy", rate.BuyingRate is { } b ? MicroFromDecimal(b) : (object)DBNull.Value);
            cmd.ExecuteNonQuery();
        }
    }

    private void InsertVoucherTypes(SqliteTransaction tx, Company c)
    {
        foreach (var t in c.VoucherTypes)
        {
            using var cmd = _connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO voucher_types
                    (id, company_id, name, base_type, default_shortcut, numbering, abbreviation, is_active, is_predefined,
                     affects_accounts, affects_stock, use_as_manufacturing_journal, track_additional_costs, allow_zero_valued)
                VALUES ($id, $cid, $name, $base, $sc, $num, $abbr, $active, $pre, $aa, $as, $mfg, $tac, $azv);
                """;
            cmd.Parameters.AddWithValue("$id", t.Id.ToString("D"));
            cmd.Parameters.AddWithValue("$cid", c.Id.ToString("D"));
            cmd.Parameters.AddWithValue("$name", t.Name);
            cmd.Parameters.AddWithValue("$base", (int)t.BaseType);
            cmd.Parameters.AddWithValue("$sc", (object?)t.DefaultShortcut ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$num", (int)t.Numbering);
            cmd.Parameters.AddWithValue("$abbr", (object?)t.Abbreviation ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$active", t.IsActive ? 1 : 0);
            cmd.Parameters.AddWithValue("$pre", t.IsPredefined ? 1 : 0);
            cmd.Parameters.AddWithValue("$aa", t.AffectsAccounts ? 1 : 0);
            cmd.Parameters.AddWithValue("$as", t.AffectsStock ? 1 : 0);
            cmd.Parameters.AddWithValue("$mfg", t.UseAsManufacturingJournal ? 1 : 0); // v18 (RQ-11)
            cmd.Parameters.AddWithValue("$tac", t.TrackAdditionalCosts ? 1 : 0);      // v19 (RQ-16..RQ-20)
            cmd.Parameters.AddWithValue("$azv", t.AllowZeroValuedTransactions ? 1 : 0); // v20 (RQ-21)
            cmd.ExecuteNonQuery();
        }
    }

    private void InsertCostCategories(SqliteTransaction tx, Company c)
    {
        foreach (var cat in c.CostCategories)
        {
            using var cmd = _connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO cost_categories
                    (id, company_id, name, allocate_revenue, allocate_non_revenue, is_predefined)
                VALUES ($id, $cid, $name, $rev, $nonrev, $pre);
                """;
            cmd.Parameters.AddWithValue("$id", cat.Id.ToString("D"));
            cmd.Parameters.AddWithValue("$cid", c.Id.ToString("D"));
            cmd.Parameters.AddWithValue("$name", cat.Name);
            cmd.Parameters.AddWithValue("$rev", cat.AllocateRevenueItems ? 1 : 0);
            cmd.Parameters.AddWithValue("$nonrev", cat.AllocateNonRevenueItems ? 1 : 0);
            cmd.Parameters.AddWithValue("$pre", cat.IsPredefined ? 1 : 0);
            cmd.ExecuteNonQuery();
        }
    }

    private void InsertCostCentres(SqliteTransaction tx, Company c)
    {
        foreach (var centre in c.CostCentres)
        {
            using var cmd = _connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO cost_centres (id, company_id, name, category_id, parent_id, alias)
                VALUES ($id, $cid, $name, $cat, $parent, $alias);
                """;
            cmd.Parameters.AddWithValue("$id", centre.Id.ToString("D"));
            cmd.Parameters.AddWithValue("$cid", c.Id.ToString("D"));
            cmd.Parameters.AddWithValue("$name", centre.Name);
            cmd.Parameters.AddWithValue("$cat", centre.CategoryId.ToString("D"));
            cmd.Parameters.AddWithValue("$parent", (object?)centre.ParentId?.ToString("D") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$alias", (object?)centre.Alias ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }
    }

    private void InsertUnits(SqliteTransaction tx, Company c)
    {
        foreach (var u in c.Units)
        {
            using var cmd = _connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO units
                    (id, company_id, symbol, formal_name, is_compound, uqc, decimal_places,
                     first_unit_id, tail_unit_id, conversion_numerator, conversion_denominator)
                VALUES ($id, $cid, $sym, $fn, $comp, $uqc, $dp, $first, $tail, $num, $den);
                """;
            cmd.Parameters.AddWithValue("$id", u.Id.ToString("D"));
            cmd.Parameters.AddWithValue("$cid", c.Id.ToString("D"));
            cmd.Parameters.AddWithValue("$sym", u.Symbol);
            cmd.Parameters.AddWithValue("$fn", u.FormalName);
            cmd.Parameters.AddWithValue("$comp", u.IsCompound ? 1 : 0);
            cmd.Parameters.AddWithValue("$uqc", (object?)u.UnitQuantityCode ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$dp", u.DecimalPlaces);
            cmd.Parameters.AddWithValue("$first", (object?)u.FirstUnitId?.ToString("D") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$tail", (object?)u.TailUnitId?.ToString("D") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$num", (object?)u.ConversionNumerator ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$den", (object?)u.ConversionDenominator ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }
    }

    private void InsertStockGroups(SqliteTransaction tx, Company c)
    {
        // Self-FK (parent_id → id) is enforced, so a parent must be inserted before its children — even when
        // it appears later in list order (e.g. re-parented under a later-created sibling). A cycle throws a
        // clean domain exception rather than a raw FK error (DEFECT 1).
        var ordered = HierarchyOrdering.ParentsBeforeChildren(
            c.StockGroups, g => g.Id, g => g.ParentId, "Stock group");
        foreach (var g in ordered)
        {
            using var cmd = _connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO stock_groups (id, company_id, name, parent_id, alias, add_quantities)
                VALUES ($id, $cid, $name, $parent, $alias, $addq);
                """;
            cmd.Parameters.AddWithValue("$id", g.Id.ToString("D"));
            cmd.Parameters.AddWithValue("$cid", c.Id.ToString("D"));
            cmd.Parameters.AddWithValue("$name", g.Name);
            cmd.Parameters.AddWithValue("$parent", (object?)g.ParentId?.ToString("D") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$alias", (object?)g.Alias ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$addq", g.AddQuantities ? 1 : 0);
            cmd.ExecuteNonQuery();
        }
    }

    private void InsertStockCategories(SqliteTransaction tx, Company c)
    {
        var ordered = HierarchyOrdering.ParentsBeforeChildren(
            c.StockCategories, cat => cat.Id, cat => cat.ParentId, "Stock category");
        foreach (var cat in ordered)
        {
            using var cmd = _connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO stock_categories (id, company_id, name, parent_id, alias)
                VALUES ($id, $cid, $name, $parent, $alias);
                """;
            cmd.Parameters.AddWithValue("$id", cat.Id.ToString("D"));
            cmd.Parameters.AddWithValue("$cid", c.Id.ToString("D"));
            cmd.Parameters.AddWithValue("$name", cat.Name);
            cmd.Parameters.AddWithValue("$parent", (object?)cat.ParentId?.ToString("D") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$alias", (object?)cat.Alias ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }
    }

    private void InsertGodowns(SqliteTransaction tx, Company c)
    {
        var ordered = HierarchyOrdering.ParentsBeforeChildren(
            c.Godowns, g => g.Id, g => g.ParentId, "Godown");
        foreach (var g in ordered)
        {
            using var cmd = _connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO godowns (id, company_id, name, parent_id, alias, third_party, is_main_location)
                VALUES ($id, $cid, $name, $parent, $alias, $tp, $main);
                """;
            cmd.Parameters.AddWithValue("$id", g.Id.ToString("D"));
            cmd.Parameters.AddWithValue("$cid", c.Id.ToString("D"));
            cmd.Parameters.AddWithValue("$name", g.Name);
            cmd.Parameters.AddWithValue("$parent", (object?)g.ParentId?.ToString("D") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$alias", (object?)g.Alias ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$tp", g.ThirdParty ? 1 : 0);
            cmd.Parameters.AddWithValue("$main", g.IsMainLocation ? 1 : 0);
            cmd.ExecuteNonQuery();
        }
    }

    private void InsertStockItems(SqliteTransaction tx, Company c)
    {
        foreach (var item in c.StockItems)
        {
            using var cmd = _connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO stock_items
                    (id, company_id, name, stock_group_id, category_id, base_unit_id, alias, valuation_method,
                     hsn_sac_code, is_taxable, reorder_level_micro, min_order_qty_micro, standard_cost_paisa,
                     gst_hsn_sac, gst_taxability, gst_rate_bp, gst_supply_type,
                     maintain_in_batches, track_manufacturing_date, use_expiry_dates, set_components)
                VALUES ($id, $cid, $name, $grp, $cat, $unit, $alias, $vm, $hsn, $tax, $rol, $moq, $std,
                        $ghsn, $gtax, $grate, $gsup, $mib, $tmd, $ued, $setc);
                """;
            cmd.Parameters.AddWithValue("$id", item.Id.ToString("D"));
            cmd.Parameters.AddWithValue("$cid", c.Id.ToString("D"));
            cmd.Parameters.AddWithValue("$name", item.Name);
            cmd.Parameters.AddWithValue("$grp", item.StockGroupId.ToString("D"));
            cmd.Parameters.AddWithValue("$cat", (object?)item.CategoryId?.ToString("D") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$unit", item.BaseUnitId.ToString("D"));
            cmd.Parameters.AddWithValue("$alias", (object?)item.Alias ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$vm", (int)item.ValuationMethod);
            cmd.Parameters.AddWithValue("$hsn", (object?)item.HsnSacCode ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$tax", item.IsTaxable ? 1 : 0);
            cmd.Parameters.AddWithValue("$rol", item.ReorderLevel is { } rol ? QtyMicroFromDecimal(rol) : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$moq", item.MinimumOrderQuantity is { } moq ? QtyMicroFromDecimal(moq) : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$std", item.StandardCost is { } std ? Paisa.FromMoney(std) : (object)DBNull.Value);

            // v13 item GST block (all NULL when the item has no GST block).
            var g = item.Gst;
            cmd.Parameters.AddWithValue("$ghsn", (object?)g?.HsnSac ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$gtax", g is null ? (object)DBNull.Value : (int)g.Taxability);
            cmd.Parameters.AddWithValue("$grate", g?.RateBasisPoints is { } grbp ? grbp : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$gsup", g is null ? (object)DBNull.Value : (int)g.SupplyType);

            // v16 batch switches (0/1; default off).
            cmd.Parameters.AddWithValue("$mib", item.MaintainInBatches ? 1 : 0);
            cmd.Parameters.AddWithValue("$tmd", item.TrackManufacturingDate ? 1 : 0);
            cmd.Parameters.AddWithValue("$ued", item.UseExpiryDates ? 1 : 0);
            cmd.Parameters.AddWithValue("$setc", item.SetComponents ? 1 : 0); // v18 Set-Components (RQ-10)
            cmd.ExecuteNonQuery();
        }
    }

    private void InsertBatchMasters(SqliteTransaction tx, Company c)
    {
        foreach (var b in c.BatchMasters)
        {
            using var cmd = _connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO batch_masters
                    (id, company_id, stock_item_id, batch_no, mfg_date, expiry_date, expiry_period,
                     godown_id, inward_qty_micro, inward_rate_paisa)
                VALUES ($id, $cid, $item, $no, $mfg, $exp, $period, $godown, $qty, $rate);
                """;
            cmd.Parameters.AddWithValue("$id", b.Id.ToString("D"));
            cmd.Parameters.AddWithValue("$cid", c.Id.ToString("D"));
            cmd.Parameters.AddWithValue("$item", b.StockItemId.ToString("D"));
            cmd.Parameters.AddWithValue("$no", b.BatchNumber);
            cmd.Parameters.AddWithValue("$mfg", (object?)(b.ManufacturingDate is { } m ? FormatDate(m) : null) ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$exp", (object?)(b.ExpiryDate is { } e ? FormatDate(e) : null) ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$period", (object?)b.ExpiryPeriod?.RawText ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$godown", (object?)b.GodownId?.ToString("D") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$qty", b.InwardQuantity is { } q ? QtyMicroFromDecimal(q) : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$rate", b.InwardRate is { } r ? Paisa.FromMoney(r) : (object)DBNull.Value);
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// Persists the Bill-of-Materials masters (Phase 6 slice 2; RQ-9, DP-3) into the v17 <c>bill_of_materials</c>
    /// header + <c>bom_lines</c> child tables. The block size (unit of manufacture) and each line's per-block
    /// quantity are stored as INTEGER micros (× 1,000,000); a carve-out per-unit rate as INTEGER paisa; a
    /// carve-out percent as INTEGER millis (× 1,000) — all exact, no float (ER-3). <see cref="BomLine.LineType"/>
    /// is stored as its enum ordinal (Component=0, ByProduct=1, CoProduct=2, Scrap=3). Line order is preserved.
    /// </summary>
    private void InsertBillsOfMaterials(SqliteTransaction tx, Company c)
    {
        foreach (var bom in c.BillsOfMaterials)
        {
            using (var head = _connection.CreateCommand())
            {
                head.Transaction = tx;
                head.CommandText = """
                    INSERT INTO bill_of_materials (id, company_id, stock_item_id, name, unit_of_manufacture_micro)
                    VALUES ($id, $cid, $item, $name, $uom);
                    """;
                head.Parameters.AddWithValue("$id", bom.Id.ToString("D"));
                head.Parameters.AddWithValue("$cid", c.Id.ToString("D"));
                head.Parameters.AddWithValue("$item", bom.StockItemId.ToString("D"));
                head.Parameters.AddWithValue("$name", bom.Name);
                head.Parameters.AddWithValue("$uom", QtyMicroFromDecimal(bom.UnitOfManufacture));
                head.ExecuteNonQuery();
            }

            var order = 0;
            foreach (var line in bom.Lines)
            {
                using var cmd = _connection.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = """
                    INSERT INTO bom_lines
                        (bom_id, line_order, line_type, component_stock_item_id, godown_id, qty_micro,
                         rate_paisa, percent_millis)
                    VALUES ($bom, $ord, $type, $comp, $godown, $qty, $rate, $pct);
                    """;
                cmd.Parameters.AddWithValue("$bom", bom.Id.ToString("D"));
                cmd.Parameters.AddWithValue("$ord", order++);
                cmd.Parameters.AddWithValue("$type", (int)line.LineType);
                cmd.Parameters.AddWithValue("$comp", line.ComponentStockItemId.ToString("D"));
                cmd.Parameters.AddWithValue("$godown", (object?)line.GodownId?.ToString("D") ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$qty", QtyMicroFromDecimal(line.QuantityPerBlock));
                cmd.Parameters.AddWithValue("$rate", line.Rate is { } rt ? Paisa.FromMoney(rt) : (object)DBNull.Value);
                cmd.Parameters.AddWithValue("$pct",
                    line.PercentOfFinishedGoodCost is { } p ? PercentMillisFromDecimal(p) : (object)DBNull.Value);
                cmd.ExecuteNonQuery();
            }
        }
    }

    private void InsertStockOpeningBalances(SqliteTransaction tx, Company c)
    {
        foreach (var b in c.StockOpeningBalances)
        {
            using var cmd = _connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO stock_opening_balances
                    (id, company_id, stock_item_id, godown_id, batch_label, quantity_micro, rate_paisa,
                     mfg_date, expiry_date)
                VALUES ($id, $cid, $item, $godown, $batch, $qty, $rate, $mfg, $exp);
                """;
            cmd.Parameters.AddWithValue("$id", b.Id.ToString("D"));
            cmd.Parameters.AddWithValue("$cid", c.Id.ToString("D"));
            cmd.Parameters.AddWithValue("$item", b.StockItemId.ToString("D"));
            cmd.Parameters.AddWithValue("$godown", b.GodownId.ToString("D"));
            cmd.Parameters.AddWithValue("$batch", (object?)b.BatchLabel ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$qty", QtyMicroFromDecimal(b.Quantity));
            cmd.Parameters.AddWithValue("$rate", Paisa.FromMoney(b.Rate));
            cmd.Parameters.AddWithValue("$mfg", (object?)(b.ManufacturingDate is { } m ? FormatDate(m) : null) ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$exp", (object?)(b.ExpiryDate is { } e ? FormatDate(e) : null) ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }
    }

    private void InsertInventoryVouchers(SqliteTransaction tx, Company c)
    {
        foreach (var v in c.InventoryVouchers)
        {
            using (var cmd = _connection.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = """
                    INSERT INTO inventory_vouchers
                        (id, company_id, type_id, number, date, narration, party_id, cancelled, post_dated)
                    VALUES ($id, $cid, $tid, $num, $date, $narr, $party, $cancel, $pd);
                    """;
                cmd.Parameters.AddWithValue("$id", v.Id.ToString("D"));
                cmd.Parameters.AddWithValue("$cid", c.Id.ToString("D"));
                cmd.Parameters.AddWithValue("$tid", v.TypeId.ToString("D"));
                cmd.Parameters.AddWithValue("$num", v.Number);
                cmd.Parameters.AddWithValue("$date", FormatDate(v.Date));
                cmd.Parameters.AddWithValue("$narr", (object?)v.Narration ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$party", (object?)v.PartyId?.ToString("D") ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$cancel", v.Cancelled ? 1 : 0);
                cmd.Parameters.AddWithValue("$pd", v.PostDated ? 1 : 0);
                cmd.ExecuteNonQuery();
            }

            var order = 0;
            foreach (var a in v.Allocations)
                InsertInventoryAllocation(tx, v.Id, a, role: 0, order++);
            foreach (var a in v.DestinationAllocations)
                InsertInventoryAllocation(tx, v.Id, a, role: 1, order++);

            order = 0;
            foreach (var o in v.OrderLines)
            {
                using var cmd = _connection.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = """
                    INSERT INTO order_lines
                        (inventory_voucher_id, line_order, stock_item_id, godown_id, quantity_micro, rate_paisa)
                    VALUES ($vid, $ord, $item, $godown, $qty, $rate);
                    """;
                cmd.Parameters.AddWithValue("$vid", v.Id.ToString("D"));
                cmd.Parameters.AddWithValue("$ord", order++);
                cmd.Parameters.AddWithValue("$item", o.StockItemId.ToString("D"));
                cmd.Parameters.AddWithValue("$godown", o.GodownId.ToString("D"));
                cmd.Parameters.AddWithValue("$qty", QtyMicroFromDecimal(o.Quantity));
                cmd.Parameters.AddWithValue("$rate", o.Rate is { } r ? Paisa.FromMoney(r) : (object)DBNull.Value);
                cmd.ExecuteNonQuery();
            }

            order = 0;
            foreach (var pl in v.PhysicalLines)
            {
                using var cmd = _connection.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = """
                    INSERT INTO physical_stock_lines
                        (inventory_voucher_id, line_order, stock_item_id, godown_id, counted_qty_micro, batch_label)
                    VALUES ($vid, $ord, $item, $godown, $qty, $batch);
                    """;
                cmd.Parameters.AddWithValue("$vid", v.Id.ToString("D"));
                cmd.Parameters.AddWithValue("$ord", order++);
                cmd.Parameters.AddWithValue("$item", pl.StockItemId.ToString("D"));
                cmd.Parameters.AddWithValue("$godown", pl.GodownId.ToString("D"));
                cmd.Parameters.AddWithValue("$qty", QtyMicroFromDecimal(pl.CountedQuantity));
                cmd.Parameters.AddWithValue("$batch", (object?)pl.BatchLabel ?? DBNull.Value);
                cmd.ExecuteNonQuery();
            }

            // v19 (RQ-20): additional-cost lines on a Stock-Journal transfer (empty for every other voucher).
            order = 0;
            foreach (var acl in v.AdditionalCostLines)
            {
                using var cmd = _connection.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = """
                    INSERT INTO additional_cost_lines
                        (inventory_voucher_id, line_order, ledger_id, amount_paisa)
                    VALUES ($vid, $ord, $ledger, $amt);
                    """;
                cmd.Parameters.AddWithValue("$vid", v.Id.ToString("D"));
                cmd.Parameters.AddWithValue("$ord", order++);
                cmd.Parameters.AddWithValue("$ledger", acl.LedgerId.ToString("D"));
                cmd.Parameters.AddWithValue("$amt", Paisa.FromMoney(acl.Amount));
                cmd.ExecuteNonQuery();
            }
        }
    }

    private void InsertInventoryAllocation(SqliteTransaction tx, Guid voucherId, InventoryAllocation a, int role, int order)
    {
        using var cmd = _connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO inventory_allocations
                (inventory_voucher_id, line_order, role, stock_item_id, godown_id, unit_id,
                 quantity_micro, direction, rate_paisa, batch_label, actual_qty_micro, billed_qty_micro)
            VALUES ($vid, $ord, $role, $item, $godown, $unit, $qty, $dir, $rate, $batch, NULL, NULL);
            """;
        cmd.Parameters.AddWithValue("$vid", voucherId.ToString("D"));
        cmd.Parameters.AddWithValue("$ord", order);
        cmd.Parameters.AddWithValue("$role", role);
        cmd.Parameters.AddWithValue("$item", a.StockItemId.ToString("D"));
        cmd.Parameters.AddWithValue("$godown", a.GodownId.ToString("D"));
        cmd.Parameters.AddWithValue("$unit", (object?)a.UnitId?.ToString("D") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$qty", QtyMicroFromDecimal(a.Quantity));
        cmd.Parameters.AddWithValue("$dir", (int)a.Direction);
        cmd.Parameters.AddWithValue("$rate", a.Rate is { } r ? Paisa.FromMoney(r) : (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$batch", (object?)a.BatchLabel ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    private void InsertBudgets(SqliteTransaction tx, Company c)
    {
        foreach (var budget in c.Budgets)
        {
            using (var cmd = _connection.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = """
                    INSERT INTO budgets (id, company_id, name, under_id, period_from, period_to)
                    VALUES ($id, $cid, $name, $under, $from, $to);
                    """;
                cmd.Parameters.AddWithValue("$id", budget.Id.ToString("D"));
                cmd.Parameters.AddWithValue("$cid", c.Id.ToString("D"));
                cmd.Parameters.AddWithValue("$name", budget.Name);
                cmd.Parameters.AddWithValue("$under", (object?)budget.UnderId?.ToString("D") ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$from", FormatDate(budget.PeriodFrom));
                cmd.Parameters.AddWithValue("$to", FormatDate(budget.PeriodTo));
                cmd.ExecuteNonQuery();
            }

            var order = 0;
            foreach (var line in budget.Lines)
            {
                using var cmd = _connection.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = """
                    INSERT INTO budget_lines
                        (budget_id, line_order, group_id, ledger_id, budget_type, amount_paisa)
                    VALUES ($bid, $ord, $gid, $lid, $type, $amt);
                    """;
                cmd.Parameters.AddWithValue("$bid", budget.Id.ToString("D"));
                cmd.Parameters.AddWithValue("$ord", order++);
                cmd.Parameters.AddWithValue("$gid", (object?)line.GroupId?.ToString("D") ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$lid", (object?)line.LedgerId?.ToString("D") ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$type", (int)line.Type);
                cmd.Parameters.AddWithValue("$amt", Paisa.FromMoney(line.Amount));
                cmd.ExecuteNonQuery();
            }
        }
    }

    private void InsertScenarios(SqliteTransaction tx, Company c)
    {
        foreach (var scenario in c.Scenarios)
        {
            using (var cmd = _connection.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = """
                    INSERT INTO scenarios (id, company_id, name, include_actuals)
                    VALUES ($id, $cid, $name, $ia);
                    """;
                cmd.Parameters.AddWithValue("$id", scenario.Id.ToString("D"));
                cmd.Parameters.AddWithValue("$cid", c.Id.ToString("D"));
                cmd.Parameters.AddWithValue("$name", scenario.Name);
                cmd.Parameters.AddWithValue("$ia", scenario.IncludeActuals ? 1 : 0);
                cmd.ExecuteNonQuery();
            }

            foreach (var typeId in scenario.IncludedTypeIds)
                InsertScenarioVoucherType(tx, scenario.Id, typeId, isIncluded: true);
            foreach (var typeId in scenario.ExcludedTypeIds)
                InsertScenarioVoucherType(tx, scenario.Id, typeId, isIncluded: false);
        }
    }

    private void InsertScenarioVoucherType(SqliteTransaction tx, Guid scenarioId, Guid voucherTypeId, bool isIncluded)
    {
        using var cmd = _connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO scenario_voucher_types (scenario_id, voucher_type_id, is_included)
            VALUES ($sid, $vtid, $inc);
            """;
        cmd.Parameters.AddWithValue("$sid", scenarioId.ToString("D"));
        cmd.Parameters.AddWithValue("$vtid", voucherTypeId.ToString("D"));
        cmd.Parameters.AddWithValue("$inc", isIncluded ? 1 : 0);
        cmd.ExecuteNonQuery();
    }

    private void InsertVouchers(SqliteTransaction tx, Company c)
    {
        foreach (var v in c.Vouchers)
            InsertVoucher(tx, c.Id, v);
    }

    private void InsertVoucher(SqliteTransaction tx, Guid companyId, Voucher v)
    {
        using (var cmd = _connection.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO vouchers
                    (id, company_id, type_id, number, date, narration, party_id, cancelled, optional, post_dated,
                     applicable_upto)
                VALUES ($id, $cid, $tid, $num, $date, $narr, $party, $cancel, $opt, $pd, $au);
                """;
            cmd.Parameters.AddWithValue("$id", v.Id.ToString("D"));
            cmd.Parameters.AddWithValue("$cid", companyId.ToString("D"));
            cmd.Parameters.AddWithValue("$tid", v.TypeId.ToString("D"));
            cmd.Parameters.AddWithValue("$num", v.Number);
            cmd.Parameters.AddWithValue("$date", FormatDate(v.Date));
            cmd.Parameters.AddWithValue("$narr", (object?)v.Narration ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$party", (object?)v.PartyId?.ToString("D") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$cancel", v.Cancelled ? 1 : 0);
            cmd.Parameters.AddWithValue("$opt", v.Optional ? 1 : 0);
            cmd.Parameters.AddWithValue("$pd", v.PostDated ? 1 : 0);
            cmd.Parameters.AddWithValue("$au", (object?)(v.ApplicableUpto is { } au ? FormatDate(au) : null) ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }

        var order = 0;
        foreach (var line in v.Lines)
        {
            long lineId;
            using (var cmd = _connection.CreateCommand())
            {
                cmd.Transaction = tx;
                // RETURNING id gives us the AUTOINCREMENT key to hang bill allocations off (SQLite ≥ 3.35).
                cmd.CommandText = """
                    INSERT INTO entry_lines
                        (voucher_id, line_order, ledger_id, amount_paisa, side,
                         forex_currency_id, forex_amount_micro, forex_rate_micro,
                         gst_tax_head, gst_rate_bp, gst_taxable_value_paisa)
                    VALUES ($vid, $ord, $lid, $amt, $side, $fxcur, $fxamt, $fxrate, $gthead, $grate, $gtax)
                    RETURNING id;
                    """;
                cmd.Parameters.AddWithValue("$vid", v.Id.ToString("D"));
                cmd.Parameters.AddWithValue("$ord", order++);
                cmd.Parameters.AddWithValue("$lid", line.LedgerId.ToString("D"));
                cmd.Parameters.AddWithValue("$amt", Paisa.FromMoney(line.Amount));
                cmd.Parameters.AddWithValue("$side", (int)line.Side);

                // v8 multi-currency: forex detail, all NULL for a base-currency line.
                var fx = line.Forex;
                cmd.Parameters.AddWithValue("$fxcur", (object?)fx?.CurrencyId.ToString("D") ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$fxamt", fx is null ? (object)DBNull.Value : MicroFromDecimal(fx.ForexAmount.Amount));
                cmd.Parameters.AddWithValue("$fxrate", fx is null ? (object)DBNull.Value : MicroFromDecimal(fx.Rate));

                // v13 GST tax-line detail: all NULL for a non-tax line.
                var gt = line.Gst;
                cmd.Parameters.AddWithValue("$gthead", gt is null ? (object)DBNull.Value : (int)gt.TaxHead);
                cmd.Parameters.AddWithValue("$grate", gt is null ? (object)DBNull.Value : gt.RateBasisPoints);
                cmd.Parameters.AddWithValue("$gtax", gt is null ? (object)DBNull.Value : Paisa.FromMoney(gt.TaxableValue));
                lineId = Convert.ToInt64(cmd.ExecuteScalar());
            }

            InsertBillAllocations(tx, lineId, line);
            InsertCostAllocations(tx, lineId, line);
            InsertBankAllocation(tx, lineId, line);
        }

        InsertVoucherInventoryLines(tx, v);
    }

    private void InsertVoucherInventoryLines(SqliteTransaction tx, Voucher v)
    {
        var order = 0;
        foreach (var line in v.InventoryLines)
        {
            using var cmd = _connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO voucher_inventory_lines
                    (voucher_id, line_order, stock_item_id, godown_id, quantity_micro, direction, rate_paisa, batch_label,
                     actual_qty_micro, billed_qty_micro)
                VALUES ($vid, $ord, $item, $godown, $qty, $dir, $rate, $batch, $aqty, $bqty);
                """;
            cmd.Parameters.AddWithValue("$vid", v.Id.ToString("D"));
            cmd.Parameters.AddWithValue("$ord", order++);
            cmd.Parameters.AddWithValue("$item", line.StockItemId.ToString("D"));
            cmd.Parameters.AddWithValue("$godown", line.GodownId.ToString("D"));
            cmd.Parameters.AddWithValue("$qty", QtyMicroFromDecimal(line.Quantity));
            cmd.Parameters.AddWithValue("$dir", (int)line.Direction);
            cmd.Parameters.AddWithValue("$rate", Paisa.FromMoney(line.Rate));
            cmd.Parameters.AddWithValue("$batch", (object?)line.BatchLabel ?? DBNull.Value);
            // v20 (RQ-22/RQ-23; DP-7): persist the Actual/Billed split only when it is active (Billed ≠ Actual);
            // otherwise both columns stay NULL so quantity_micro alone drives Billed ≡ Actual (byte-identical, ER-13).
            var split = line.BilledQuantity != line.Quantity;
            cmd.Parameters.AddWithValue("$aqty", split ? QtyMicroFromDecimal(line.Quantity) : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$bqty", split ? QtyMicroFromDecimal(line.BilledQuantity) : (object)DBNull.Value);
            cmd.ExecuteNonQuery();
        }
    }

    private void InsertBillAllocations(SqliteTransaction tx, long entryLineId, EntryLine line)
    {
        var allocOrder = 0;
        foreach (var a in line.BillAllocations)
        {
            using var cmd = _connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO bill_allocations
                    (entry_line_id, alloc_order, ref_type, name, amount_paisa, due_date, credit_days)
                VALUES ($lid, $ord, $rt, $name, $amt, $due, $cd);
                """;
            cmd.Parameters.AddWithValue("$lid", entryLineId);
            cmd.Parameters.AddWithValue("$ord", allocOrder++);
            cmd.Parameters.AddWithValue("$rt", (int)a.RefType);
            cmd.Parameters.AddWithValue("$name", a.Name);
            cmd.Parameters.AddWithValue("$amt", Paisa.FromMoney(a.Amount));
            cmd.Parameters.AddWithValue("$due", (object?)(a.DueDate is { } d ? FormatDate(d) : null) ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$cd", (object?)a.CreditPeriodDays ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }
    }

    private void InsertCostAllocations(SqliteTransaction tx, long entryLineId, EntryLine line)
    {
        var allocOrder = 0;
        foreach (var a in line.CostAllocations)
        {
            using var cmd = _connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO cost_allocations
                    (entry_line_id, alloc_order, category_id, centre_id, amount_paisa)
                VALUES ($lid, $ord, $cat, $centre, $amt);
                """;
            cmd.Parameters.AddWithValue("$lid", entryLineId);
            cmd.Parameters.AddWithValue("$ord", allocOrder++);
            cmd.Parameters.AddWithValue("$cat", a.CategoryId.ToString("D"));
            cmd.Parameters.AddWithValue("$centre", a.CentreId.ToString("D"));
            cmd.Parameters.AddWithValue("$amt", Paisa.FromMoney(a.Amount));
            cmd.ExecuteNonQuery();
        }
    }

    private void InsertBankAllocation(SqliteTransaction tx, long entryLineId, EntryLine line)
    {
        var a = line.BankAllocation;
        if (a is null) return;

        using var cmd = _connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO bank_allocations
                (entry_line_id, transaction_type, instrument_number, instrument_date, bank_date)
            VALUES ($lid, $tt, $inum, $idate, $bdate);
            """;
        cmd.Parameters.AddWithValue("$lid", entryLineId);
        cmd.Parameters.AddWithValue("$tt", (int)a.TransactionType);
        cmd.Parameters.AddWithValue("$inum", a.InstrumentNumber);
        cmd.Parameters.AddWithValue("$idate", (object?)(a.InstrumentDate is { } id ? FormatDate(id) : null) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$bdate", (object?)(a.BankDate is { } bd ? FormatDate(bd) : null) ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    // ------------------------------------------------------------------ ISavedReportViewRepository

    /// <inheritdoc />
    public void Save(Guid companyId, string name, SavedReportView view)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A saved-view name is required.", nameof(name));
        ArgumentNullException.ThrowIfNull(view);

        // Config-only JSON: the report is always recomputed on apply, so no figure is ever persisted (ER-9).
        var json = view.ToJson();

        using var tx = _connection.BeginTransaction();
        // Upsert by (company, name), case-insensitive: overwrite the existing row of that name, else insert.
        // Delete-then-insert keeps a stable single row and a fresh id only when creating.
        using (var find = _connection.CreateCommand())
        {
            find.Transaction = tx;
            find.CommandText =
                "SELECT id FROM saved_views WHERE company_id = $cid AND name = $name COLLATE NOCASE LIMIT 1;";
            find.Parameters.AddWithValue("$cid", companyId.ToString("D"));
            find.Parameters.AddWithValue("$name", name);
            var existingId = find.ExecuteScalar() as string;

            if (existingId is not null)
            {
                using var upd = _connection.CreateCommand();
                upd.Transaction = tx;
                upd.CommandText = "UPDATE saved_views SET name = $name, config_json = $json WHERE id = $id;";
                upd.Parameters.AddWithValue("$name", name);
                upd.Parameters.AddWithValue("$json", json);
                upd.Parameters.AddWithValue("$id", existingId);
                upd.ExecuteNonQuery();
            }
            else
            {
                using var ins = _connection.CreateCommand();
                ins.Transaction = tx;
                ins.CommandText =
                    "INSERT INTO saved_views (id, company_id, name, config_json) VALUES ($id, $cid, $name, $json);";
                ins.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("D"));
                ins.Parameters.AddWithValue("$cid", companyId.ToString("D"));
                ins.Parameters.AddWithValue("$name", name);
                ins.Parameters.AddWithValue("$json", json);
                ins.ExecuteNonQuery();
            }
        }
        tx.Commit();
    }

    /// <inheritdoc />
    public IReadOnlyList<SavedReportViewEntry> List(Guid companyId)
    {
        var entries = new List<SavedReportViewEntry>();
        using var cmd = _connection.CreateCommand();
        cmd.CommandText =
            "SELECT name, config_json FROM saved_views WHERE company_id = $cid ORDER BY name COLLATE NOCASE;";
        cmd.Parameters.AddWithValue("$cid", companyId.ToString("D"));
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            entries.Add(new SavedReportViewEntry(reader.GetString(0), SavedReportView.FromJson(reader.GetString(1))));
        return entries;
    }

    /// <inheritdoc />
    public SavedReportView? Get(Guid companyId, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        using var cmd = _connection.CreateCommand();
        cmd.CommandText =
            "SELECT config_json FROM saved_views WHERE company_id = $cid AND name = $name COLLATE NOCASE LIMIT 1;";
        cmd.Parameters.AddWithValue("$cid", companyId.ToString("D"));
        cmd.Parameters.AddWithValue("$name", name);
        return cmd.ExecuteScalar() is string json ? SavedReportView.FromJson(json) : null;
    }

    /// <inheritdoc />
    public void Delete(Guid companyId, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "DELETE FROM saved_views WHERE company_id = $cid AND name = $name COLLATE NOCASE;";
        cmd.Parameters.AddWithValue("$cid", companyId.ToString("D"));
        cmd.Parameters.AddWithValue("$name", name);
        cmd.ExecuteNonQuery();
    }

    // ------------------------------------------------------------------ ISmtpProfileRepository

    // The port's Save/Get/Delete are implemented EXPLICITLY, forwarding to these descriptively-named public
    // methods so the store's own API reads clearly (the plain Save/Get/Delete names collide with the other
    // repositories on this class, e.g. Save(Company) and Get(company, viewName)).

    /// <inheritdoc cref="ISmtpProfileRepository.Save" />
    public void SaveSmtpProfile(Guid companyId, SmtpProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        // One row per company (company_id is the PRIMARY KEY): upsert by delete-then-insert in one transaction.
        // No password/secret is ever written — the row carries only host/port/TLS/from (R13).
        using var tx = _connection.BeginTransaction();
        using (var del = _connection.CreateCommand())
        {
            del.Transaction = tx;
            del.CommandText = "DELETE FROM smtp_profile WHERE company_id = $cid;";
            del.Parameters.AddWithValue("$cid", companyId.ToString("D"));
            del.ExecuteNonQuery();
        }
        using (var ins = _connection.CreateCommand())
        {
            ins.Transaction = tx;
            ins.CommandText = """
                INSERT INTO smtp_profile (company_id, host, port, use_tls, from_address, from_name)
                VALUES ($cid, $host, $port, $tls, $from, $fromName);
                """;
            ins.Parameters.AddWithValue("$cid", companyId.ToString("D"));
            ins.Parameters.AddWithValue("$host", profile.Host);
            ins.Parameters.AddWithValue("$port", profile.Port);
            ins.Parameters.AddWithValue("$tls", profile.UseTls ? 1 : 0);
            ins.Parameters.AddWithValue("$from", profile.FromAddress);
            ins.Parameters.AddWithValue("$fromName", (object?)profile.FromName ?? DBNull.Value);
            ins.ExecuteNonQuery();
        }
        tx.Commit();
    }

    /// <inheritdoc cref="ISmtpProfileRepository.Get" />
    public SmtpProfile? GetSmtpProfile(Guid companyId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT host, port, use_tls, from_address, from_name
            FROM smtp_profile WHERE company_id = $cid LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$cid", companyId.ToString("D"));
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return new SmtpProfile
        {
            Host = r.GetString(0),
            Port = (int)r.GetInt64(1),
            UseTls = r.GetInt64(2) != 0,
            FromAddress = r.GetString(3),
            FromName = r.IsDBNull(4) ? null : r.GetString(4),
        };
    }

    /// <inheritdoc cref="ISmtpProfileRepository.Delete" />
    public void DeleteSmtpProfile(Guid companyId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "DELETE FROM smtp_profile WHERE company_id = $cid;";
        cmd.Parameters.AddWithValue("$cid", companyId.ToString("D"));
        cmd.ExecuteNonQuery();
    }

    void ISmtpProfileRepository.Save(Guid companyId, SmtpProfile profile) => SaveSmtpProfile(companyId, profile);
    SmtpProfile? ISmtpProfileRepository.Get(Guid companyId) => GetSmtpProfile(companyId);
    void ISmtpProfileRepository.Delete(Guid companyId) => DeleteSmtpProfile(companyId);

    // ------------------------------------------------------------------ low-level helpers

    private void Exec(string sql)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private void ExecTx(SqliteTransaction tx, string sql, params (string Name, object Value)[] ps)
    {
        using var cmd = _connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        foreach (var (name, value) in ps)
            cmd.Parameters.AddWithValue(name, value);
        cmd.ExecuteNonQuery();
    }

    private long ScalarLong(string sql)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        var result = cmd.ExecuteScalar();
        return result is null or DBNull ? 0 : Convert.ToInt64(result);
    }

    /// <summary>
    /// Interest rate percent → millis (rate% × 1000), stored as INTEGER so the rate round-trips exactly
    /// (no binary float). Throws if the rate carries more than 3 decimal places (would lose precision).
    /// </summary>
    private static long RateToMillis(decimal ratePercent)
    {
        var scaled = ratePercent * 1000m;
        var truncated = decimal.Truncate(scaled);
        if (scaled != truncated)
            throw new InvalidOperationException(
                $"Interest rate {ratePercent}% is not exact to 3 decimal places; cannot persist without loss.");
        return (long)truncated;
    }

    /// <summary>
    /// A forex amount / exchange rate → micros (value × 1,000,000), stored as INTEGER so it round-trips
    /// exactly (no binary float). Throws if the value carries more than 6 decimal places (would lose precision).
    /// </summary>
    private static long MicroFromDecimal(decimal value)
    {
        var scaled = value * Schema.ForexScale;
        var truncated = decimal.Truncate(scaled);
        if (scaled != truncated)
            throw new InvalidOperationException(
                $"Value {value} is not exact to 6 decimal places; cannot persist as forex micros without loss.");
        return (long)truncated;
    }

    /// <summary>Micros (value × 1,000,000) → an exact decimal.</summary>
    private static decimal MicroToDecimal(long micros) => micros / (decimal)Schema.ForexScale;

    /// <summary>
    /// A stock quantity → micros (qty × 1,000,000), stored as INTEGER so a 0–4-decimal quantity round-trips
    /// exactly (no binary float). Throws if the quantity carries more than 6 decimal places (would lose precision).
    /// </summary>
    private static long QtyMicroFromDecimal(decimal qty)
    {
        var scaled = qty * Schema.QuantityScale;
        var truncated = decimal.Truncate(scaled);
        if (scaled != truncated)
            throw new InvalidOperationException(
                $"Quantity {qty} is not exact to 6 decimal places; cannot persist as quantity micros without loss.");
        return (long)truncated;
    }

    /// <summary>Quantity micros (qty × 1,000,000) → an exact decimal.</summary>
    private static decimal QtyMicroToDecimal(long micros) => micros / (decimal)Schema.QuantityScale;

    /// <summary>The scale a BOM carve-out percent is stored at (× 1,000 = "millis"), as INTEGER, so a 3-dp
    /// percentage (e.g. 5.125%) round-trips exactly (no float — ER-3).</summary>
    private const long PercentMillisScale = 1_000L;

    /// <summary>A carve-out percent → millis (percent × 1,000), stored as INTEGER. Throws if the percent carries
    /// more than 3 decimal places (would lose precision).</summary>
    private static long PercentMillisFromDecimal(decimal percent)
    {
        var scaled = percent * PercentMillisScale;
        var truncated = decimal.Truncate(scaled);
        if (scaled != truncated)
            throw new InvalidOperationException(
                $"BOM carve-out percent {percent} is not exact to 3 decimal places; cannot persist as percent millis without loss.");
        return (long)truncated;
    }

    /// <summary>Percent millis (percent × 1,000) → an exact decimal percent.</summary>
    private static decimal PercentMillisToDecimal(long millis) => millis / (decimal)PercentMillisScale;

    private static string FormatDate(DateOnly d) => d.ToString("yyyy-MM-dd");

    private static DateOnly ParseDate(string s) =>
        DateOnly.ParseExact(s, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Closes the underlying connection and releases the database file handle.</summary>
    public void Dispose()
    {
        _connection.Dispose();
        // Ensure the native SQLite handle is released promptly so a temp .db file can be deleted.
        SqliteConnection.ClearPool(_connection);
    }
}
