namespace Apex.Persistence.Sqlite;

/// <summary>
/// The relational schema for a single-company Apex ledger database (design §2 / accounting-core §4).
/// One <c>.db</c> file per company. Money is stored as <b>INTEGER paisa</b> (amount × 100) so the
/// double-entry math stays exact to the paisa (NFR-3) — never REAL/float. Dates are stored as ISO-8601
/// <c>yyyy-MM-dd</c> TEXT (a <see cref="System.DateOnly"/>). GUID surrogate keys are stored as TEXT
/// ("D" format). A <c>schema_version</c> table carries the versioned-migration marker; v1 is the
/// Phase-1 accounting core, <b>v2</b> adds bill-wise accounting (catalog §5): two bill-by-bill
/// columns on <c>ledgers</c> and a <c>bill_allocations</c> child table hung off <c>entry_lines</c>.
/// <b>v3</b> adds cost categories and cost centres (catalog §6): <c>cost_categories</c> and
/// <c>cost_centres</c> master tables, a <c>cost_allocations</c> child table hung off
/// <c>entry_lines</c>, and a nullable <c>cost_applicable</c> column on <c>ledgers</c> (NULL = auto).
/// <b>v4</b> adds budgets (catalog §7): a <c>budgets</c> master table and a <c>budget_lines</c> child
/// table (each line targets a group OR a ledger, with a type and an amount in paisa).
/// <b>v5</b> adds banking (catalog §8): a <c>bank_allocations</c> child table hung off
/// <c>entry_lines</c> (one row per bank line — transaction type, instrument no./date, and the nullable
/// cleared <c>bank_date</c>), plus two cheque-printing columns on <c>ledgers</c>.
/// <b>v6</b> adds scenarios (catalog §7): a <c>scenarios</c> master table and a
/// <c>scenario_voucher_types</c> child table (one row per included/excluded voucher type), plus a nullable
/// <c>applicable_upto</c> column on <c>vouchers</c> for Reversing Journals.
/// </summary>
public static class Schema
{
    /// <summary>The current schema version this adapter reads and writes (v6 = scenarios).</summary>
    public const int CurrentVersion = 6;

    /// <summary>
    /// The full create DDL for a brand-new database at <see cref="CurrentVersion"/>. Executed inside a
    /// transaction when a database is first created (or opened empty); a fresh DB is stamped straight to
    /// the current version, so this DDL already includes every column/table added by later migrations.
    /// Foreign keys wire the aggregate together; <c>PRAGMA foreign_keys</c> is enabled per connection.
    /// </summary>
    public const string CreateV1 = """
        CREATE TABLE schema_version (
            version     INTEGER NOT NULL
        );

        CREATE TABLE companies (
            id                       TEXT    NOT NULL PRIMARY KEY,
            name                     TEXT    NOT NULL,
            mailing_name             TEXT    NOT NULL,
            address                  TEXT        NULL,
            country                  TEXT    NOT NULL,
            state                    TEXT        NULL,
            pin                      TEXT        NULL,
            financial_year_start     TEXT    NOT NULL,
            books_begin_from         TEXT    NOT NULL,
            base_currency_symbol     TEXT    NOT NULL,
            base_currency_name       TEXT    NOT NULL,
            decimal_places           INTEGER NOT NULL,
            decimal_unit_name        TEXT    NOT NULL,
            primary_cost_category    TEXT    NOT NULL,
            main_location            TEXT    NOT NULL,
            -- The reserved Profit & Loss head (NOT one of the 28 groups); persisted as its own group row
            -- and pointed at here so the P&L A/c ledger's group resolves on reload.
            profit_and_loss_head_id  TEXT        NULL REFERENCES groups(id)
        );

        CREATE TABLE groups (
            id            TEXT    NOT NULL PRIMARY KEY,
            company_id    TEXT    NOT NULL REFERENCES companies(id),
            name          TEXT    NOT NULL,
            nature        INTEGER NOT NULL,   -- GroupNature enum ordinal
            parent_id     TEXT        NULL REFERENCES groups(id),
            alias         TEXT        NULL,
            is_predefined INTEGER NOT NULL,   -- 0/1
            -- Is this the reserved P&L head (kept out of Company.Groups on reload)?
            is_pl_head    INTEGER NOT NULL DEFAULT 0
        );

        CREATE TABLE ledgers (
            id                   TEXT    NOT NULL PRIMARY KEY,
            company_id           TEXT    NOT NULL REFERENCES companies(id),
            name                 TEXT    NOT NULL,
            group_id             TEXT    NOT NULL REFERENCES groups(id),
            opening_balance_paisa INTEGER NOT NULL,  -- magnitude ≥ 0, in paisa
            opening_is_debit     INTEGER NOT NULL,   -- 0/1
            alias                TEXT        NULL,
            is_predefined        INTEGER NOT NULL,
            -- v2 bill-wise (catalog §5):
            maintain_bill_by_bill    INTEGER NOT NULL DEFAULT 0,   -- 0/1
            default_credit_period    INTEGER     NULL,             -- days, NULL = none
            -- v3 cost centres (catalog §6): NULL = auto (by nature), 0/1 = explicit override
            cost_applicable          INTEGER     NULL,
            -- v5 banking (catalog §8): cheque-printing configuration (physical layout deferred)
            enable_cheque_printing   INTEGER NOT NULL DEFAULT 0,   -- 0/1
            cheque_bank_name         TEXT        NULL
        );

        CREATE TABLE voucher_types (
            id               TEXT    NOT NULL PRIMARY KEY,
            company_id       TEXT    NOT NULL REFERENCES companies(id),
            name             TEXT    NOT NULL,
            base_type        INTEGER NOT NULL,   -- VoucherBaseType enum ordinal
            default_shortcut TEXT        NULL,
            numbering        INTEGER NOT NULL,   -- NumberingMethod enum ordinal
            abbreviation     TEXT        NULL,
            is_active        INTEGER NOT NULL,
            is_predefined    INTEGER NOT NULL
        );

        CREATE TABLE vouchers (
            id          TEXT    NOT NULL PRIMARY KEY,
            company_id  TEXT    NOT NULL REFERENCES companies(id),
            type_id     TEXT    NOT NULL REFERENCES voucher_types(id),
            number      INTEGER NOT NULL,
            date        TEXT    NOT NULL,
            narration   TEXT        NULL,
            party_id    TEXT        NULL REFERENCES ledgers(id),
            cancelled   INTEGER NOT NULL,
            optional    INTEGER NOT NULL,
            post_dated  INTEGER NOT NULL,
            -- v6 (catalog §7): Reversing-Journal "Applicable upto" date; NULL for every other voucher
            applicable_upto TEXT    NULL
        );

        CREATE TABLE entry_lines (
            id           INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
            voucher_id   TEXT    NOT NULL REFERENCES vouchers(id),
            line_order   INTEGER NOT NULL,   -- preserves the entry order within the voucher
            ledger_id    TEXT    NOT NULL REFERENCES ledgers(id),
            amount_paisa INTEGER NOT NULL,   -- magnitude > 0, in paisa
            side         INTEGER NOT NULL    -- DrCr enum ordinal (Debit=0, Credit=1)
        );

        CREATE TABLE bill_allocations (
            id             INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
            entry_line_id  INTEGER NOT NULL REFERENCES entry_lines(id),
            alloc_order    INTEGER NOT NULL,   -- preserves order within the line
            ref_type       INTEGER NOT NULL,   -- BillRefType enum ordinal
            name           TEXT    NOT NULL,   -- bill reference id ('' for On-Account)
            amount_paisa   INTEGER NOT NULL,   -- magnitude > 0, in paisa
            due_date       TEXT        NULL,   -- ISO yyyy-MM-dd, or NULL when derived
            credit_days    INTEGER     NULL    -- credit period days, or NULL
        );

        CREATE TABLE cost_categories (
            id                      TEXT    NOT NULL PRIMARY KEY,
            company_id              TEXT    NOT NULL REFERENCES companies(id),
            name                    TEXT    NOT NULL,
            allocate_revenue        INTEGER NOT NULL,   -- 0/1
            allocate_non_revenue    INTEGER NOT NULL,   -- 0/1
            is_predefined           INTEGER NOT NULL    -- 0/1 (the seeded Primary Cost Category)
        );

        CREATE TABLE cost_centres (
            id            TEXT    NOT NULL PRIMARY KEY,
            company_id    TEXT    NOT NULL REFERENCES companies(id),
            name          TEXT    NOT NULL,
            category_id   TEXT    NOT NULL REFERENCES cost_categories(id),
            parent_id     TEXT        NULL REFERENCES cost_centres(id),   -- NULL = primary centre
            alias         TEXT        NULL
        );

        CREATE TABLE cost_allocations (
            id             INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
            entry_line_id  INTEGER NOT NULL REFERENCES entry_lines(id),
            alloc_order    INTEGER NOT NULL,   -- preserves order within the line
            category_id    TEXT    NOT NULL REFERENCES cost_categories(id),
            centre_id      TEXT    NOT NULL REFERENCES cost_centres(id),
            amount_paisa   INTEGER NOT NULL    -- magnitude > 0, in paisa
        );

        CREATE TABLE budgets (
            id            TEXT    NOT NULL PRIMARY KEY,
            company_id    TEXT    NOT NULL REFERENCES companies(id),
            name          TEXT    NOT NULL,
            under_id      TEXT        NULL REFERENCES groups(id),   -- optional Primary group; NULL = top-level
            period_from   TEXT    NOT NULL,   -- ISO yyyy-MM-dd
            period_to     TEXT    NOT NULL    -- ISO yyyy-MM-dd, ≥ period_from
        );

        CREATE TABLE budget_lines (
            id            INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
            budget_id     TEXT    NOT NULL REFERENCES budgets(id),
            line_order    INTEGER NOT NULL,   -- preserves order within the budget
            group_id      TEXT        NULL REFERENCES groups(id),   -- exactly one of group_id / ledger_id set
            ledger_id     TEXT        NULL REFERENCES ledgers(id),
            budget_type   INTEGER NOT NULL,   -- BudgetType enum ordinal (0 = OnClosingBalance, 1 = OnNettTransactions)
            amount_paisa  INTEGER NOT NULL    -- magnitude ≥ 0, in paisa
        );

        CREATE TABLE bank_allocations (
            id                INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
            entry_line_id     INTEGER NOT NULL REFERENCES entry_lines(id),   -- one row per bank line
            transaction_type  INTEGER NOT NULL,   -- BankTransactionType enum ordinal
            instrument_number TEXT    NOT NULL,   -- cheque/DD/UTR reference ('' if none)
            instrument_date   TEXT        NULL,   -- ISO yyyy-MM-dd, or NULL
            bank_date         TEXT        NULL    -- cleared date (BRS), or NULL when unreconciled
        );

        CREATE TABLE scenarios (
            id               TEXT    NOT NULL PRIMARY KEY,
            company_id       TEXT    NOT NULL REFERENCES companies(id),
            name             TEXT    NOT NULL,
            include_actuals  INTEGER NOT NULL   -- 0/1
        );

        CREATE TABLE scenario_voucher_types (
            id               INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
            scenario_id      TEXT    NOT NULL REFERENCES scenarios(id),
            voucher_type_id  TEXT    NOT NULL REFERENCES voucher_types(id),
            is_included      INTEGER NOT NULL   -- 1 = included, 0 = excluded
        );

        CREATE INDEX ix_groups_company        ON groups(company_id);
        CREATE INDEX ix_ledgers_company        ON ledgers(company_id);
        CREATE INDEX ix_voucher_types_company  ON voucher_types(company_id);
        CREATE INDEX ix_vouchers_company       ON vouchers(company_id);
        CREATE INDEX ix_entry_lines_voucher    ON entry_lines(voucher_id);
        CREATE INDEX ix_bill_allocations_line  ON bill_allocations(entry_line_id);
        CREATE INDEX ix_cost_categories_company ON cost_categories(company_id);
        CREATE INDEX ix_cost_centres_company    ON cost_centres(company_id);
        CREATE INDEX ix_cost_allocations_line   ON cost_allocations(entry_line_id);
        CREATE INDEX ix_budgets_company         ON budgets(company_id);
        CREATE INDEX ix_budget_lines_budget     ON budget_lines(budget_id);
        CREATE INDEX ix_bank_allocations_line   ON bank_allocations(entry_line_id);
        CREATE INDEX ix_scenarios_company        ON scenarios(company_id);
        CREATE INDEX ix_scenario_vtypes_scenario ON scenario_voucher_types(scenario_id);
        """;

    /// <summary>
    /// The v1→v2 migration: adds the two bill-wise columns to <c>ledgers</c> and creates the
    /// <c>bill_allocations</c> table + index. Idempotent-friendly (ADD COLUMN with defaults, plus a
    /// fresh table), run inside a transaction that also bumps <c>schema_version</c> to 2. Existing v1
    /// databases keep all their data — the new columns default to "not bill-wise".
    /// </summary>
    public const string MigrateV1ToV2 = """
        ALTER TABLE ledgers ADD COLUMN maintain_bill_by_bill INTEGER NOT NULL DEFAULT 0;
        ALTER TABLE ledgers ADD COLUMN default_credit_period INTEGER NULL;

        CREATE TABLE bill_allocations (
            id             INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
            entry_line_id  INTEGER NOT NULL REFERENCES entry_lines(id),
            alloc_order    INTEGER NOT NULL,
            ref_type       INTEGER NOT NULL,
            name           TEXT    NOT NULL,
            amount_paisa   INTEGER NOT NULL,
            due_date       TEXT        NULL,
            credit_days    INTEGER     NULL
        );

        CREATE INDEX ix_bill_allocations_line ON bill_allocations(entry_line_id);
        """;

    /// <summary>
    /// The v2→v3 migration (catalog §6): adds the nullable <c>cost_applicable</c> column to
    /// <c>ledgers</c> and creates the <c>cost_categories</c>, <c>cost_centres</c> and
    /// <c>cost_allocations</c> tables + indexes. Run inside a transaction that also bumps
    /// <c>schema_version</c> to 3. Existing v2 databases keep all their data — the new column defaults
    /// to NULL ("auto by nature") and the new tables start empty.
    /// </summary>
    public const string MigrateV2ToV3 = """
        ALTER TABLE ledgers ADD COLUMN cost_applicable INTEGER NULL;

        CREATE TABLE cost_categories (
            id                      TEXT    NOT NULL PRIMARY KEY,
            company_id              TEXT    NOT NULL REFERENCES companies(id),
            name                    TEXT    NOT NULL,
            allocate_revenue        INTEGER NOT NULL,
            allocate_non_revenue    INTEGER NOT NULL,
            is_predefined           INTEGER NOT NULL
        );

        CREATE TABLE cost_centres (
            id            TEXT    NOT NULL PRIMARY KEY,
            company_id    TEXT    NOT NULL REFERENCES companies(id),
            name          TEXT    NOT NULL,
            category_id   TEXT    NOT NULL REFERENCES cost_categories(id),
            parent_id     TEXT        NULL REFERENCES cost_centres(id),
            alias         TEXT        NULL
        );

        CREATE TABLE cost_allocations (
            id             INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
            entry_line_id  INTEGER NOT NULL REFERENCES entry_lines(id),
            alloc_order    INTEGER NOT NULL,
            category_id    TEXT    NOT NULL REFERENCES cost_categories(id),
            centre_id      TEXT    NOT NULL REFERENCES cost_centres(id),
            amount_paisa   INTEGER NOT NULL
        );

        CREATE INDEX ix_cost_categories_company ON cost_categories(company_id);
        CREATE INDEX ix_cost_centres_company    ON cost_centres(company_id);
        CREATE INDEX ix_cost_allocations_line   ON cost_allocations(entry_line_id);
        """;

    /// <summary>
    /// The v3→v4 migration (catalog §7): creates the <c>budgets</c> master table and the
    /// <c>budget_lines</c> child table + indexes. Run inside a transaction that also bumps
    /// <c>schema_version</c> to 4. Existing v3 databases keep all their data — the new tables start empty.
    /// </summary>
    public const string MigrateV3ToV4 = """
        CREATE TABLE budgets (
            id            TEXT    NOT NULL PRIMARY KEY,
            company_id    TEXT    NOT NULL REFERENCES companies(id),
            name          TEXT    NOT NULL,
            under_id      TEXT        NULL REFERENCES groups(id),
            period_from   TEXT    NOT NULL,
            period_to     TEXT    NOT NULL
        );

        CREATE TABLE budget_lines (
            id            INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
            budget_id     TEXT    NOT NULL REFERENCES budgets(id),
            line_order    INTEGER NOT NULL,
            group_id      TEXT        NULL REFERENCES groups(id),
            ledger_id     TEXT        NULL REFERENCES ledgers(id),
            budget_type   INTEGER NOT NULL,
            amount_paisa  INTEGER NOT NULL
        );

        CREATE INDEX ix_budgets_company     ON budgets(company_id);
        CREATE INDEX ix_budget_lines_budget ON budget_lines(budget_id);
        """;

    /// <summary>
    /// The v4→v5 migration (catalog §8): adds the two cheque-printing columns to <c>ledgers</c> and
    /// creates the <c>bank_allocations</c> table + index. Run inside a transaction that also bumps
    /// <c>schema_version</c> to 5. Existing v4 databases keep all their data — the new columns default to
    /// "no cheque printing" and the new table starts empty.
    /// </summary>
    public const string MigrateV4ToV5 = """
        ALTER TABLE ledgers ADD COLUMN enable_cheque_printing INTEGER NOT NULL DEFAULT 0;
        ALTER TABLE ledgers ADD COLUMN cheque_bank_name TEXT NULL;

        CREATE TABLE bank_allocations (
            id                INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
            entry_line_id     INTEGER NOT NULL REFERENCES entry_lines(id),
            transaction_type  INTEGER NOT NULL,
            instrument_number TEXT    NOT NULL,
            instrument_date   TEXT        NULL,
            bank_date         TEXT        NULL
        );

        CREATE INDEX ix_bank_allocations_line ON bank_allocations(entry_line_id);
        """;

    /// <summary>
    /// The v5→v6 migration (catalog §7): adds the nullable <c>applicable_upto</c> column to
    /// <c>vouchers</c> and creates the <c>scenarios</c> master table and the
    /// <c>scenario_voucher_types</c> child table + indexes. Run inside a transaction that also bumps
    /// <c>schema_version</c> to 6. Existing v5 databases keep all their data — the new column defaults to
    /// NULL (no Reversing-Journal "Applicable upto") and the new tables start empty.
    /// </summary>
    public const string MigrateV5ToV6 = """
        ALTER TABLE vouchers ADD COLUMN applicable_upto TEXT NULL;

        CREATE TABLE scenarios (
            id               TEXT    NOT NULL PRIMARY KEY,
            company_id       TEXT    NOT NULL REFERENCES companies(id),
            name             TEXT    NOT NULL,
            include_actuals  INTEGER NOT NULL
        );

        CREATE TABLE scenario_voucher_types (
            id               INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
            scenario_id      TEXT    NOT NULL REFERENCES scenarios(id),
            voucher_type_id  TEXT    NOT NULL REFERENCES voucher_types(id),
            is_included      INTEGER NOT NULL
        );

        CREATE INDEX ix_scenarios_company        ON scenarios(company_id);
        CREATE INDEX ix_scenario_vtypes_scenario ON scenario_voucher_types(scenario_id);
        """;
}
