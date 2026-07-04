using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Persistence;
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
public sealed class SqliteCompanyStore : ICompanyRepository, IMasterRepository, IVoucherRepository, IDisposable
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
                   primary_cost_category, main_location, profit_and_loss_head_id
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
            };
            plHeadId = r.IsDBNull(15) ? null : Guid.Parse(r.GetString(15));
        }

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
        InsertVouchers(tx, company);
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
                   interest_round_method, interest_round_decimals, currency_id
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
                currencyId: r.IsDBNull(21) ? (Guid?)null : Guid.Parse(r.GetString(21))));
        }
        return list;
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
            SELECT id, name, base_type, default_shortcut, numbering, abbreviation, is_active, is_predefined
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
                isPredefined: r.GetInt64(7) != 0));
        }
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
            result.Add(new Voucher(
                h.Id, h.TypeId, h.Date, lines,
                number: h.Number,
                narration: h.Narration,
                partyId: h.PartyId,
                cancelled: h.Cancelled,
                optional: h.Optional,
                postDated: h.PostDated,
                applicableUpto: h.ApplicableUpto));
        }
        return result;
    }

    private List<EntryLine> ReadEntryLines(Guid voucherId)
    {
        // Read the raw line rows first (capturing the id), then load each line's bill allocations.
        var raw = new List<(long Id, Guid LedgerId, long AmountPaisa, DrCr Side, ForexInfo? Forex)>();
        using (var cmd = _connection.CreateCommand())
        {
            cmd.CommandText = """
                SELECT id, ledger_id, amount_paisa, side,
                       forex_currency_id, forex_amount_micro, forex_rate_micro
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
                raw.Add((
                    r.GetInt64(0),
                    Guid.Parse(r.GetString(1)),
                    r.GetInt64(2),
                    (DrCr)(int)r.GetInt64(3),
                    forex));
            }
        }

        var lines = new List<EntryLine>(raw.Count);
        foreach (var (id, ledgerId, amountPaisa, side, forex) in raw)
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
                forex));
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
        ExecTx(tx, "DELETE FROM entry_lines WHERE voucher_id IN (SELECT id FROM vouchers WHERE company_id = $cid);", ("$cid", cid));
        ExecTx(tx, "DELETE FROM vouchers WHERE company_id = $cid;", ("$cid", cid));
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
        ExecTx(tx, "DELETE FROM groups WHERE company_id = $cid;", ("$cid", cid));
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
                 primary_cost_category, main_location, profit_and_loss_head_id)
            VALUES
                ($id, $name, $mail, $addr, $country, $state, $pin,
                 $fy, $books, $sym, $curname, $dp, $unit, $pcc, $loc, NULL);
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
        cmd.ExecuteNonQuery();
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
                     interest_round_method, interest_round_decimals, currency_id)
                VALUES ($id, $cid, $name, $gid, $ob, $od, $alias, $pre, $bbb, $dcp, $cca, $ecp, $cbn,
                        $ien, $irate, $iper, $ion, $iapp, $icf, $istyle, $irm, $ird, $curid);
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
                    (id, company_id, name, base_type, default_shortcut, numbering, abbreviation, is_active, is_predefined)
                VALUES ($id, $cid, $name, $base, $sc, $num, $abbr, $active, $pre);
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
                         forex_currency_id, forex_amount_micro, forex_rate_micro)
                    VALUES ($vid, $ord, $lid, $amt, $side, $fxcur, $fxamt, $fxrate)
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
                lineId = Convert.ToInt64(cmd.ExecuteScalar());
            }

            InsertBillAllocations(tx, lineId, line);
            InsertCostAllocations(tx, lineId, line);
            InsertBankAllocation(tx, lineId, line);
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
