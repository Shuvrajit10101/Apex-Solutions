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
        InsertLedgers(tx, company);
        InsertVoucherTypes(tx, company);
        // Cost categories before centres (centres FK categories); both before vouchers (cost allocations
        // FK both).
        InsertCostCategories(tx, company);
        InsertCostCentres(tx, company);
        InsertVouchers(tx, company);

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
                   maintain_bill_by_bill, default_credit_period, cost_applicable
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
                costCentresApplicable: r.IsDBNull(9) ? (bool?)null : r.GetInt64(9) != 0));
        }
        return list;
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

    private IEnumerable<Voucher> ReadVouchers(Guid companyId)
    {
        // Header rows first, then lines per voucher (ordered), to build each aggregate.
        var headers = new List<(Guid Id, Guid TypeId, int Number, DateOnly Date, string? Narration,
            Guid? PartyId, bool Cancelled, bool Optional, bool PostDated)>();

        using (var cmd = _connection.CreateCommand())
        {
            cmd.CommandText = """
                SELECT id, type_id, number, date, narration, party_id, cancelled, optional, post_dated
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
                    r.GetInt64(8) != 0));
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
                postDated: h.PostDated));
        }
        return result;
    }

    private List<EntryLine> ReadEntryLines(Guid voucherId)
    {
        // Read the raw line rows first (capturing the id), then load each line's bill allocations.
        var raw = new List<(long Id, Guid LedgerId, long AmountPaisa, DrCr Side)>();
        using (var cmd = _connection.CreateCommand())
        {
            cmd.CommandText = """
                SELECT id, ledger_id, amount_paisa, side
                FROM entry_lines WHERE voucher_id = $vid ORDER BY line_order, id;
                """;
            cmd.Parameters.AddWithValue("$vid", voucherId.ToString("D"));
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                raw.Add((
                    r.GetInt64(0),
                    Guid.Parse(r.GetString(1)),
                    r.GetInt64(2),
                    (DrCr)(int)r.GetInt64(3)));
            }
        }

        var lines = new List<EntryLine>(raw.Count);
        foreach (var (id, ledgerId, amountPaisa, side) in raw)
        {
            var allocs = ReadBillAllocations(id);
            var costAllocs = ReadCostAllocations(id);
            lines.Add(new EntryLine(
                ledgerId,
                Paisa.ToMoney(amountPaisa),
                side,
                allocs.Count > 0 ? allocs : null,
                costAllocs.Count > 0 ? costAllocs : null));
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
        ExecTx(tx, "DELETE FROM entry_lines WHERE voucher_id IN (SELECT id FROM vouchers WHERE company_id = $cid);", ("$cid", cid));
        ExecTx(tx, "DELETE FROM vouchers WHERE company_id = $cid;", ("$cid", cid));
        ExecTx(tx, "DELETE FROM ledgers WHERE company_id = $cid;", ("$cid", cid));
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
                     maintain_bill_by_bill, default_credit_period, cost_applicable)
                VALUES ($id, $cid, $name, $gid, $ob, $od, $alias, $pre, $bbb, $dcp, $cca);
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
                    (id, company_id, type_id, number, date, narration, party_id, cancelled, optional, post_dated)
                VALUES ($id, $cid, $tid, $num, $date, $narr, $party, $cancel, $opt, $pd);
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
                    INSERT INTO entry_lines (voucher_id, line_order, ledger_id, amount_paisa, side)
                    VALUES ($vid, $ord, $lid, $amt, $side)
                    RETURNING id;
                    """;
                cmd.Parameters.AddWithValue("$vid", v.Id.ToString("D"));
                cmd.Parameters.AddWithValue("$ord", order++);
                cmd.Parameters.AddWithValue("$lid", line.LedgerId.ToString("D"));
                cmd.Parameters.AddWithValue("$amt", Paisa.FromMoney(line.Amount));
                cmd.Parameters.AddWithValue("$side", (int)line.Side);
                lineId = Convert.ToInt64(cmd.ExecuteScalar());
            }

            InsertBillAllocations(tx, lineId, line);
            InsertCostAllocations(tx, lineId, line);
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
