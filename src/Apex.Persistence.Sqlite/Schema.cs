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
/// <b>v7</b> adds interest calculation (catalog §7): a nullable interest-parameter block on <c>ledgers</c>
/// (<c>interest_enabled</c> and the Rate / Per / On / Applicability / Calculate-From / Style / Rounding
/// columns) — all NULL/0 for a ledger with no interest, so existing ledgers stay off.
/// <b>v8</b> adds multi-currency (catalog §2/§20; plan.md §10 C-1): a <c>currencies</c> master table (the
/// base ₹/INR plus any foreign currencies), an <c>exchange_rates</c> table (dated base-per-foreign quotes,
/// rates stored as INTEGER micros = rate × 1,000,000), a nullable <c>currency_id</c> column on
/// <c>ledgers</c> (NULL = base), and three nullable forex columns on <c>entry_lines</c>
/// (<c>forex_currency_id</c>, <c>forex_amount_micro</c> = forex × 1,000,000, <c>forex_rate_micro</c> =
/// rate × 1,000,000) — all NULL for a base-currency line, so the ledger engine is unchanged for base.
/// <b>v9</b> adds inventory masters (catalog §9; plan.md §5 Phase 3): six new master tables —
/// <c>stock_groups</c>, <c>stock_categories</c>, <c>units</c>, <c>godowns</c>, <c>stock_items</c> and
/// <c>stock_opening_balances</c> — created by <see cref="MigrateV8ToV9"/> (pure CREATE TABLE/INDEX, no
/// ALTER, so existing v8 data is untouched). Inventory money is INTEGER paisa (rate, value); quantity is an
/// INTEGER scaled by <see cref="QuantityScale"/> (× 1,000,000 = "micros") so 0–4 decimal quantities
/// round-trip exactly with no float. All inventory tables start empty on a migrated database.
/// <b>v10</b> adds inventory &amp; order vouchers (catalog §10; plan.md §5 Phase 3): two effect-flag columns on
/// <c>voucher_types</c> (<c>affects_accounts</c>, <c>affects_stock</c>) and four child tables —
/// <c>inventory_vouchers</c> (a stock/order voucher header), <c>inventory_allocations</c> (its stock-movement
/// lines, with a <c>role</c> distinguishing a Stock-Journal source from its destination), <c>order_lines</c>
/// (PO/SO ordered items) and <c>physical_stock_lines</c> (Physical-Stock counted quantities). Added by
/// <see cref="MigrateV9ToV10"/> (two ALTER + four CREATE TABLE/INDEX; existing v9 data untouched). Quantities
/// are INTEGER micros, rates INTEGER paisa.
/// <b>v11</b> adds the per-item standard-cost rate (catalog §9; plan.md §5 Phase 3, slice 3.3a): a single
/// nullable <c>standard_cost_paisa</c> column on <c>stock_items</c> (the rate the Standard-Cost valuation
/// method values closing stock at), added by <see cref="MigrateV10ToV11"/> (one <c>ALTER TABLE … ADD
/// COLUMN</c>; existing v10 rows keep NULL = no standard rate). Run inside a transaction that bumps
/// <c>schema_version</c> to 11; a fresh DB is stamped straight to v11 via <see cref="CreateV1"/>.
/// </summary>
public static class Schema
{
    /// <summary>The current schema version this adapter reads and writes (v11 = per-item standard cost).</summary>
    public const int CurrentVersion = 11;

    /// <summary>The scale forex amounts and rates are stored at (× 1,000,000 = "micros"), as INTEGER.</summary>
    public const long ForexScale = 1_000_000L;

    /// <summary>The scale stock quantities are stored at (× 1,000,000 = "micros"), as INTEGER — covers a
    /// unit's 0–4 decimals exactly with headroom, so quantities round-trip losslessly (no binary float).</summary>
    public const long QuantityScale = 1_000_000L;

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
            cheque_bank_name         TEXT        NULL,
            -- v7 interest calculation (catalog §7): nullable interest-parameter block. When
            -- interest_enabled IS NULL the ledger has no interest block at all (default off).
            interest_enabled         INTEGER     NULL,             -- NULL = no block, 0/1 = Activate flag
            interest_rate_millis     INTEGER     NULL,             -- rate% × 1000 (exact to 3 dp)
            interest_per             INTEGER     NULL,             -- InterestPer enum ordinal
            interest_on_balance      INTEGER     NULL,             -- InterestOnBalance enum ordinal
            interest_applicability   INTEGER     NULL,             -- InterestApplicability enum ordinal
            interest_calc_from       TEXT        NULL,             -- ISO yyyy-MM-dd, or NULL
            interest_style           INTEGER     NULL,             -- InterestStyle enum ordinal
            interest_round_method    INTEGER     NULL,             -- InterestRoundingMethod enum ordinal
            interest_round_decimals  INTEGER     NULL,             -- decimal places for rounding
            -- v8 multi-currency (catalog §2/§20): the ledger's currency, NULL = base ₹/INR
            currency_id              TEXT        NULL REFERENCES currencies(id)
        );

        CREATE TABLE currencies (
            id               TEXT    NOT NULL PRIMARY KEY,
            company_id       TEXT    NOT NULL REFERENCES companies(id),
            symbol           TEXT    NOT NULL,   -- display symbol (₹, $, €)
            formal_name      TEXT    NOT NULL,   -- ISO-style code (INR, USD, EUR)
            decimal_places   INTEGER NOT NULL,   -- minor-unit places
            is_base          INTEGER NOT NULL    -- 1 for the single base currency (₹/INR)
        );

        CREATE TABLE exchange_rates (
            id                 TEXT    NOT NULL PRIMARY KEY,
            company_id         TEXT    NOT NULL REFERENCES companies(id),
            currency_id        TEXT    NOT NULL REFERENCES currencies(id),
            rate_date          TEXT    NOT NULL,   -- ISO yyyy-MM-dd (effective date)
            standard_rate_micro INTEGER NOT NULL,  -- base per 1 foreign × 1,000,000 (> 0)
            selling_rate_micro  INTEGER    NULL,   -- NULL = fall back to standard
            buying_rate_micro   INTEGER    NULL    -- NULL = fall back to standard
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
            is_predefined    INTEGER NOT NULL,
            -- v10 effect flags (catalog §10): whether posting this type touches accounts / moves stock
            affects_accounts INTEGER NOT NULL DEFAULT 0,   -- 0/1
            affects_stock    INTEGER NOT NULL DEFAULT 0    -- 0/1
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
            amount_paisa INTEGER NOT NULL,   -- magnitude > 0, in paisa (base value = forex × rate)
            side         INTEGER NOT NULL,   -- DrCr enum ordinal (Debit=0, Credit=1)
            -- v8 multi-currency (catalog §2/§20): forex detail, all NULL for a base-currency line
            forex_currency_id  TEXT    NULL REFERENCES currencies(id),
            forex_amount_micro INTEGER NULL,   -- forex magnitude × 1,000,000 (exact, no float)
            forex_rate_micro   INTEGER NULL    -- rate (base per foreign) × 1,000,000 (exact, no float)
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

        CREATE TABLE stock_groups (
            id             TEXT    NOT NULL PRIMARY KEY,
            company_id     TEXT    NOT NULL REFERENCES companies(id),
            name           TEXT    NOT NULL,
            parent_id      TEXT        NULL REFERENCES stock_groups(id),   -- NULL = under implicit Primary
            alias          TEXT        NULL,
            add_quantities INTEGER NOT NULL DEFAULT 1                      -- "Should quantities be added?" 0/1
        );

        CREATE TABLE stock_categories (
            id             TEXT    NOT NULL PRIMARY KEY,
            company_id     TEXT    NOT NULL REFERENCES companies(id),
            name           TEXT    NOT NULL,
            parent_id      TEXT        NULL REFERENCES stock_categories(id),
            alias          TEXT        NULL
        );

        CREATE TABLE units (
            id                     TEXT    NOT NULL PRIMARY KEY,
            company_id             TEXT    NOT NULL REFERENCES companies(id),
            symbol                 TEXT    NOT NULL,
            formal_name            TEXT    NOT NULL,
            is_compound            INTEGER NOT NULL,          -- 0 = simple, 1 = compound
            uqc                    TEXT        NULL,          -- GST Unit Quantity Code (simple units)
            decimal_places         INTEGER NOT NULL DEFAULT 0,-- 0–4 for simple units, 0 for compound
            first_unit_id          TEXT        NULL REFERENCES units(id),   -- compound: base/first unit
            tail_unit_id           TEXT        NULL REFERENCES units(id),   -- compound: tail unit
            conversion_numerator   INTEGER     NULL,          -- compound: tail-per-first factor numerator (> 0)
            conversion_denominator INTEGER     NULL           -- compound: factor denominator (> 0, default 1)
        );

        CREATE TABLE godowns (
            id               TEXT    NOT NULL PRIMARY KEY,
            company_id       TEXT    NOT NULL REFERENCES companies(id),
            name             TEXT    NOT NULL,
            parent_id        TEXT        NULL REFERENCES godowns(id),
            alias            TEXT        NULL,
            third_party      INTEGER NOT NULL DEFAULT 0,   -- "our stock with a third party" (job-work) 0/1
            is_main_location INTEGER NOT NULL DEFAULT 0    -- the single seeded "Main Location" 0/1
        );

        CREATE TABLE stock_items (
            id                  TEXT    NOT NULL PRIMARY KEY,
            company_id          TEXT    NOT NULL REFERENCES companies(id),
            name                TEXT    NOT NULL,
            stock_group_id      TEXT    NOT NULL REFERENCES stock_groups(id),
            category_id         TEXT        NULL REFERENCES stock_categories(id),
            base_unit_id        TEXT    NOT NULL REFERENCES units(id),
            alias               TEXT        NULL,
            valuation_method    INTEGER NOT NULL DEFAULT 0,   -- StockValuationMethod ordinal (0 = AverageCost)
            hsn_sac_code        TEXT        NULL,             -- GST placeholder, inert until the GST slice
            is_taxable          INTEGER NOT NULL DEFAULT 0,   -- GST placeholder 0/1
            reorder_level_micro INTEGER     NULL,             -- simple reorder level, qty × 1,000,000
            min_order_qty_micro INTEGER     NULL,             -- minimum order qty, qty × 1,000,000
            standard_cost_paisa INTEGER     NULL              -- Standard-Cost valuation rate in paisa (NULL = unset)
        );

        CREATE TABLE stock_opening_balances (
            id                 TEXT    NOT NULL PRIMARY KEY,
            company_id         TEXT    NOT NULL REFERENCES companies(id),
            stock_item_id      TEXT    NOT NULL REFERENCES stock_items(id),
            godown_id          TEXT    NOT NULL REFERENCES godowns(id),
            batch_label        TEXT        NULL,             -- batch/lot label (DP-10), NULL for non-batch
            quantity_micro     INTEGER NOT NULL,             -- opening qty × 1,000,000
            rate_paisa         INTEGER NOT NULL,             -- per-unit rate in paisa
            -- value (qty × rate) is NOT stored: it is derived on read from quantity × rate to the paisa,
            -- so a single source of truth avoids drift (the old write-only value_paisa column was removed).
            mfg_date           TEXT        NULL,             -- forward-compat (Phase 6), no behaviour yet
            expiry_date        TEXT        NULL              -- forward-compat (Phase 6), no behaviour yet
        );

        CREATE TABLE inventory_vouchers (
            id          TEXT    NOT NULL PRIMARY KEY,
            company_id  TEXT    NOT NULL REFERENCES companies(id),
            type_id     TEXT    NOT NULL REFERENCES voucher_types(id),
            number      INTEGER NOT NULL,
            date        TEXT    NOT NULL,                     -- ISO yyyy-MM-dd
            narration   TEXT        NULL,
            party_id    TEXT        NULL REFERENCES ledgers(id),
            cancelled   INTEGER NOT NULL DEFAULT 0,           -- 0/1
            post_dated  INTEGER NOT NULL DEFAULT 0            -- 0/1
        );

        CREATE TABLE inventory_allocations (
            id                INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
            inventory_voucher_id TEXT NOT NULL REFERENCES inventory_vouchers(id),
            line_order        INTEGER NOT NULL,               -- preserves order within the voucher
            role              INTEGER NOT NULL,               -- 0 = movement/source, 1 = Stock-Journal destination
            stock_item_id     TEXT    NOT NULL REFERENCES stock_items(id),
            godown_id         TEXT    NOT NULL REFERENCES godowns(id),
            unit_id           TEXT        NULL REFERENCES units(id),   -- line's unit; NULL = item base unit
            quantity_micro    INTEGER NOT NULL,               -- qty × 1,000,000 (> 0)
            direction         INTEGER NOT NULL,               -- StockDirection ordinal (Inward=0, Outward=1)
            rate_paisa        INTEGER     NULL,               -- optional per-unit rate in paisa
            batch_label       TEXT        NULL
        );

        CREATE TABLE order_lines (
            id                INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
            inventory_voucher_id TEXT NOT NULL REFERENCES inventory_vouchers(id),
            line_order        INTEGER NOT NULL,
            stock_item_id     TEXT    NOT NULL REFERENCES stock_items(id),
            godown_id         TEXT    NOT NULL REFERENCES godowns(id),
            quantity_micro    INTEGER NOT NULL,               -- qty × 1,000,000 (> 0)
            rate_paisa        INTEGER     NULL                -- optional per-unit rate in paisa
        );

        CREATE TABLE physical_stock_lines (
            id                INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
            inventory_voucher_id TEXT NOT NULL REFERENCES inventory_vouchers(id),
            line_order        INTEGER NOT NULL,
            stock_item_id     TEXT    NOT NULL REFERENCES stock_items(id),
            godown_id         TEXT    NOT NULL REFERENCES godowns(id),
            counted_qty_micro INTEGER NOT NULL,               -- counted qty × 1,000,000 (≥ 0)
            batch_label       TEXT        NULL
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
        CREATE INDEX ix_currencies_company       ON currencies(company_id);
        CREATE INDEX ix_exchange_rates_company   ON exchange_rates(company_id);
        CREATE INDEX ix_exchange_rates_currency  ON exchange_rates(currency_id);
        CREATE INDEX ix_stock_groups_company     ON stock_groups(company_id);
        CREATE INDEX ix_stock_categories_company ON stock_categories(company_id);
        CREATE INDEX ix_units_company            ON units(company_id);
        CREATE INDEX ix_godowns_company          ON godowns(company_id);
        CREATE INDEX ix_stock_items_company      ON stock_items(company_id);
        CREATE INDEX ix_stock_openings_company   ON stock_opening_balances(company_id);
        CREATE INDEX ix_stock_openings_item      ON stock_opening_balances(stock_item_id);
        CREATE INDEX ix_inv_vouchers_company     ON inventory_vouchers(company_id);
        CREATE INDEX ix_inv_allocations_voucher  ON inventory_allocations(inventory_voucher_id);
        CREATE INDEX ix_order_lines_voucher      ON order_lines(inventory_voucher_id);
        CREATE INDEX ix_physical_lines_voucher   ON physical_stock_lines(inventory_voucher_id);
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

    /// <summary>
    /// The v6→v7 migration (catalog §7): adds the nullable interest-parameter columns to <c>ledgers</c>.
    /// Run inside a transaction that also bumps <c>schema_version</c> to 7. Existing v6 databases keep all
    /// their data — every new column defaults to NULL, i.e. "no interest block" (interest stays off).
    /// </summary>
    public const string MigrateV6ToV7 = """
        ALTER TABLE ledgers ADD COLUMN interest_enabled        INTEGER NULL;
        ALTER TABLE ledgers ADD COLUMN interest_rate_millis    INTEGER NULL;
        ALTER TABLE ledgers ADD COLUMN interest_per            INTEGER NULL;
        ALTER TABLE ledgers ADD COLUMN interest_on_balance     INTEGER NULL;
        ALTER TABLE ledgers ADD COLUMN interest_applicability  INTEGER NULL;
        ALTER TABLE ledgers ADD COLUMN interest_calc_from      TEXT    NULL;
        ALTER TABLE ledgers ADD COLUMN interest_style          INTEGER NULL;
        ALTER TABLE ledgers ADD COLUMN interest_round_method   INTEGER NULL;
        ALTER TABLE ledgers ADD COLUMN interest_round_decimals INTEGER NULL;
        """;

    /// <summary>
    /// The v7→v8 migration (catalog §2/§20 Multi-currency): creates the <c>currencies</c> and
    /// <c>exchange_rates</c> master tables, adds the nullable <c>currency_id</c> column to <c>ledgers</c>,
    /// and adds the three nullable forex columns to <c>entry_lines</c>. Run inside a transaction that also
    /// bumps <c>schema_version</c> to 8. Existing v7 databases keep all their data — the new columns default
    /// to NULL ("base currency" / "no forex") and the new tables start empty. A migrated database has NO base
    /// currency row until the company is next saved (Save seeds it from the base-currency fields).
    /// </summary>
    public const string MigrateV7ToV8 = """
        CREATE TABLE currencies (
            id               TEXT    NOT NULL PRIMARY KEY,
            company_id       TEXT    NOT NULL REFERENCES companies(id),
            symbol           TEXT    NOT NULL,
            formal_name      TEXT    NOT NULL,
            decimal_places   INTEGER NOT NULL,
            is_base          INTEGER NOT NULL
        );

        CREATE TABLE exchange_rates (
            id                 TEXT    NOT NULL PRIMARY KEY,
            company_id         TEXT    NOT NULL REFERENCES companies(id),
            currency_id        TEXT    NOT NULL REFERENCES currencies(id),
            rate_date          TEXT    NOT NULL,
            standard_rate_micro INTEGER NOT NULL,
            selling_rate_micro  INTEGER    NULL,
            buying_rate_micro   INTEGER    NULL
        );

        ALTER TABLE ledgers ADD COLUMN currency_id TEXT NULL REFERENCES currencies(id);

        ALTER TABLE entry_lines ADD COLUMN forex_currency_id  TEXT    NULL REFERENCES currencies(id);
        ALTER TABLE entry_lines ADD COLUMN forex_amount_micro INTEGER NULL;
        ALTER TABLE entry_lines ADD COLUMN forex_rate_micro   INTEGER NULL;

        CREATE INDEX ix_currencies_company      ON currencies(company_id);
        CREATE INDEX ix_exchange_rates_company  ON exchange_rates(company_id);
        CREATE INDEX ix_exchange_rates_currency ON exchange_rates(currency_id);
        """;

    /// <summary>
    /// The v8→v9 migration (catalog §9 Inventory): creates the six inventory master tables
    /// (<c>stock_groups</c>, <c>stock_categories</c>, <c>units</c>, <c>godowns</c>, <c>stock_items</c>,
    /// <c>stock_opening_balances</c>) + indexes. Pure CREATE TABLE/INDEX — no ALTER, no row rewrites — so an
    /// existing v8 database keeps all its data and the new tables simply start empty. Run inside a transaction
    /// that also bumps <c>schema_version</c> to 9. A migrated database has NO godown rows (not even Main
    /// Location) until the company is next saved; a fresh DB is stamped straight to v9 via <see cref="CreateV1"/>.
    /// </summary>
    public const string MigrateV8ToV9 = """
        CREATE TABLE stock_groups (
            id             TEXT    NOT NULL PRIMARY KEY,
            company_id     TEXT    NOT NULL REFERENCES companies(id),
            name           TEXT    NOT NULL,
            parent_id      TEXT        NULL REFERENCES stock_groups(id),
            alias          TEXT        NULL,
            add_quantities INTEGER NOT NULL DEFAULT 1
        );

        CREATE TABLE stock_categories (
            id             TEXT    NOT NULL PRIMARY KEY,
            company_id     TEXT    NOT NULL REFERENCES companies(id),
            name           TEXT    NOT NULL,
            parent_id      TEXT        NULL REFERENCES stock_categories(id),
            alias          TEXT        NULL
        );

        CREATE TABLE units (
            id                     TEXT    NOT NULL PRIMARY KEY,
            company_id             TEXT    NOT NULL REFERENCES companies(id),
            symbol                 TEXT    NOT NULL,
            formal_name            TEXT    NOT NULL,
            is_compound            INTEGER NOT NULL,
            uqc                    TEXT        NULL,
            decimal_places         INTEGER NOT NULL DEFAULT 0,
            first_unit_id          TEXT        NULL REFERENCES units(id),
            tail_unit_id           TEXT        NULL REFERENCES units(id),
            conversion_numerator   INTEGER     NULL,
            conversion_denominator INTEGER     NULL
        );

        CREATE TABLE godowns (
            id               TEXT    NOT NULL PRIMARY KEY,
            company_id       TEXT    NOT NULL REFERENCES companies(id),
            name             TEXT    NOT NULL,
            parent_id        TEXT        NULL REFERENCES godowns(id),
            alias            TEXT        NULL,
            third_party      INTEGER NOT NULL DEFAULT 0,
            is_main_location INTEGER NOT NULL DEFAULT 0
        );

        CREATE TABLE stock_items (
            id                  TEXT    NOT NULL PRIMARY KEY,
            company_id          TEXT    NOT NULL REFERENCES companies(id),
            name                TEXT    NOT NULL,
            stock_group_id      TEXT    NOT NULL REFERENCES stock_groups(id),
            category_id         TEXT        NULL REFERENCES stock_categories(id),
            base_unit_id        TEXT    NOT NULL REFERENCES units(id),
            alias               TEXT        NULL,
            valuation_method    INTEGER NOT NULL DEFAULT 0,
            hsn_sac_code        TEXT        NULL,
            is_taxable          INTEGER NOT NULL DEFAULT 0,
            reorder_level_micro INTEGER     NULL,
            min_order_qty_micro INTEGER     NULL
        );

        CREATE TABLE stock_opening_balances (
            id                 TEXT    NOT NULL PRIMARY KEY,
            company_id         TEXT    NOT NULL REFERENCES companies(id),
            stock_item_id      TEXT    NOT NULL REFERENCES stock_items(id),
            godown_id          TEXT    NOT NULL REFERENCES godowns(id),
            batch_label        TEXT        NULL,
            quantity_micro     INTEGER NOT NULL,
            rate_paisa         INTEGER NOT NULL,
            -- value (qty × rate) is derived on read, not stored (no drift-prone second source of truth).
            mfg_date           TEXT        NULL,
            expiry_date        TEXT        NULL
        );

        CREATE INDEX ix_stock_groups_company     ON stock_groups(company_id);
        CREATE INDEX ix_stock_categories_company ON stock_categories(company_id);
        CREATE INDEX ix_units_company            ON units(company_id);
        CREATE INDEX ix_godowns_company          ON godowns(company_id);
        CREATE INDEX ix_stock_items_company      ON stock_items(company_id);
        CREATE INDEX ix_stock_openings_company   ON stock_opening_balances(company_id);
        CREATE INDEX ix_stock_openings_item      ON stock_opening_balances(stock_item_id);
        """;

    /// <summary>
    /// The v9→v10 migration (catalog §10 Inventory &amp; order vouchers): adds the two effect-flag columns to
    /// <c>voucher_types</c> and creates the four inventory-voucher tables (<c>inventory_vouchers</c>,
    /// <c>inventory_allocations</c>, <c>order_lines</c>, <c>physical_stock_lines</c>) + indexes. Two
    /// <c>ALTER TABLE … ADD COLUMN</c> with defaults + pure CREATE TABLE/INDEX — no row rewrites — so an
    /// existing v9 database keeps all its data (the flag columns default to 0 and the new tables start empty).
    /// Run inside a transaction that also bumps <c>schema_version</c> to 10. A fresh DB is stamped straight to
    /// v10 via <see cref="CreateV1"/>; a migrated database has NO inventory-voucher rows until the company is
    /// next saved (which re-writes the correct effect flags on every voucher type).
    /// </summary>
    public const string MigrateV9ToV10 = """
        ALTER TABLE voucher_types ADD COLUMN affects_accounts INTEGER NOT NULL DEFAULT 0;
        ALTER TABLE voucher_types ADD COLUMN affects_stock    INTEGER NOT NULL DEFAULT 0;

        CREATE TABLE inventory_vouchers (
            id          TEXT    NOT NULL PRIMARY KEY,
            company_id  TEXT    NOT NULL REFERENCES companies(id),
            type_id     TEXT    NOT NULL REFERENCES voucher_types(id),
            number      INTEGER NOT NULL,
            date        TEXT    NOT NULL,
            narration   TEXT        NULL,
            party_id    TEXT        NULL REFERENCES ledgers(id),
            cancelled   INTEGER NOT NULL DEFAULT 0,
            post_dated  INTEGER NOT NULL DEFAULT 0
        );

        CREATE TABLE inventory_allocations (
            id                INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
            inventory_voucher_id TEXT NOT NULL REFERENCES inventory_vouchers(id),
            line_order        INTEGER NOT NULL,
            role              INTEGER NOT NULL,
            stock_item_id     TEXT    NOT NULL REFERENCES stock_items(id),
            godown_id         TEXT    NOT NULL REFERENCES godowns(id),
            unit_id           TEXT        NULL REFERENCES units(id),
            quantity_micro    INTEGER NOT NULL,
            direction         INTEGER NOT NULL,
            rate_paisa        INTEGER     NULL,
            batch_label       TEXT        NULL
        );

        CREATE TABLE order_lines (
            id                INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
            inventory_voucher_id TEXT NOT NULL REFERENCES inventory_vouchers(id),
            line_order        INTEGER NOT NULL,
            stock_item_id     TEXT    NOT NULL REFERENCES stock_items(id),
            godown_id         TEXT    NOT NULL REFERENCES godowns(id),
            quantity_micro    INTEGER NOT NULL,
            rate_paisa        INTEGER     NULL
        );

        CREATE TABLE physical_stock_lines (
            id                INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
            inventory_voucher_id TEXT NOT NULL REFERENCES inventory_vouchers(id),
            line_order        INTEGER NOT NULL,
            stock_item_id     TEXT    NOT NULL REFERENCES stock_items(id),
            godown_id         TEXT    NOT NULL REFERENCES godowns(id),
            counted_qty_micro INTEGER NOT NULL,
            batch_label       TEXT        NULL
        );

        CREATE INDEX ix_inv_vouchers_company     ON inventory_vouchers(company_id);
        CREATE INDEX ix_inv_allocations_voucher  ON inventory_allocations(inventory_voucher_id);
        CREATE INDEX ix_order_lines_voucher      ON order_lines(inventory_voucher_id);
        CREATE INDEX ix_physical_lines_voucher   ON physical_stock_lines(inventory_voucher_id);
        """;

    /// <summary>
    /// The v10→v11 migration (catalog §9 per-item standard cost; slice 3.3a): adds a single nullable
    /// <c>standard_cost_paisa</c> column to <c>stock_items</c> (the Standard-Cost valuation rate in paisa).
    /// One <c>ALTER TABLE … ADD COLUMN</c> — no row rewrites — so an existing v10 database keeps all its data
    /// (the column defaults to NULL = no standard rate, i.e. Standard-Cost items fall back to last purchase
    /// cost until a rate is set). Run inside a transaction that also bumps <c>schema_version</c> to 11. A
    /// fresh DB is stamped straight to v11 via <see cref="CreateV1"/>.
    /// </summary>
    public const string MigrateV10ToV11 = """
        ALTER TABLE stock_items ADD COLUMN standard_cost_paisa INTEGER NULL;
        """;
}
