namespace Apex.Persistence.Sqlite;

/// <summary>
/// The relational schema for a single-company Apex ledger database (design §2 / accounting-core §4).
/// One <c>.db</c> file per company. Money is stored as <b>INTEGER paisa</b> (amount × 100) so the
/// double-entry math stays exact to the paisa (NFR-3) — never REAL/float. Dates are stored as ISO-8601
/// <c>yyyy-MM-dd</c> TEXT (a <see cref="System.DateOnly"/>). GUID surrogate keys are stored as TEXT
/// ("D" format). A <c>schema_version</c> table carries the versioned-migration marker; v1 is the
/// Phase-1 accounting core, <b>v2</b> adds bill-wise accounting (catalog §5): two bill-by-bill
/// columns on <c>ledgers</c> and a <c>bill_allocations</c> child table hung off <c>entry_lines</c>.
/// </summary>
public static class Schema
{
    /// <summary>The current schema version this adapter reads and writes (v2 = bill-wise).</summary>
    public const int CurrentVersion = 2;

    /// <summary>
    /// The v1 DDL. Executed inside a transaction when a database is first created (or opened empty).
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
            default_credit_period    INTEGER     NULL              -- days, NULL = none
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
            post_dated  INTEGER NOT NULL
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

        CREATE INDEX ix_groups_company        ON groups(company_id);
        CREATE INDEX ix_ledgers_company        ON ledgers(company_id);
        CREATE INDEX ix_voucher_types_company  ON voucher_types(company_id);
        CREATE INDEX ix_vouchers_company       ON vouchers(company_id);
        CREATE INDEX ix_entry_lines_voucher    ON entry_lines(voucher_id);
        CREATE INDEX ix_bill_allocations_line  ON bill_allocations(entry_line_id);
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
}
