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
///
/// <b>v12</b> adds Item-Invoice-mode stock lines (catalog §10; plan.md §5 Phase 3, slice 3.3b): one child
/// table <c>voucher_inventory_lines</c> hung off the accounting <c>vouchers</c> table (a Purchase/Sales
/// voucher run in item-invoice mode carries item lines that move stock in the same voucher). Added by
/// <see cref="MigrateV11ToV12"/> (one pure CREATE TABLE/INDEX — no ALTER, no row rewrites — so an existing v11
/// database keeps all its data and simply gains an empty table). Run inside a transaction that bumps
/// <c>schema_version</c> to 12; a fresh DB is stamped straight to v12 via <see cref="CreateV1"/>.
/// </summary>
public static class Schema
{
    /// <summary>The current schema version this adapter reads and writes (v18 = the two Manufacturing-Journal
    /// master flags: <c>voucher_types.use_as_manufacturing_journal</c> (RQ-11) and <c>stock_items.set_components</c>
    /// (RQ-10) — both 0/1 defaulting to 0, so an existing DB is byte-identical (ER-13). v17 = Bill of Materials
    /// masters: <c>bill_of_materials</c> header + <c>bom_lines</c> child — multiple BOMs per finished good, with
    /// Component/By-Product/Co-Product/Scrap line types and a per-block qty/rate/percent carve-out — Phase 6 slice 2).</summary>
    public const int CurrentVersion = 23;

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
            profit_and_loss_head_id  TEXT        NULL REFERENCES groups(id),
            -- v13 core GST (catalog §12): company GST config; gst_enabled = 0 for every existing company.
            gst_enabled          INTEGER NOT NULL DEFAULT 0,   -- 0/1
            gstin                TEXT        NULL,             -- 15-char GSTIN, NULL when unset
            gst_home_state       TEXT        NULL,             -- 2-digit GST state code
            gst_reg_type         INTEGER     NULL,             -- GstRegistrationType enum ordinal
            gst_applicable_from  TEXT        NULL,             -- ISO yyyy-MM-dd, or NULL
            gst_periodicity      INTEGER     NULL,             -- GstReturnPeriodicity enum ordinal
            -- v20 (Phase 6 slice 4; RQ-22; DP-7): F11 "Use separate Actual & Billed Quantity columns" — a pure
            -- persisted toggle (cannot be inferred). 0/1, default 0 so an existing company is byte-identical (ER-13).
            use_separate_actual_billed_qty INTEGER NOT NULL DEFAULT 0,  -- 0/1
            -- v21 (Phase 6 slice 5; RQ-26): F11 "Enable multiple Price Levels" — a pure persisted toggle (cannot be
            -- inferred). 0/1, default 0 so an existing company is byte-identical (ER-13).
            enable_multiple_price_levels INTEGER NOT NULL DEFAULT 0     -- 0/1
        );

        CREATE TABLE gst_rate_slabs (
            id            TEXT    NOT NULL PRIMARY KEY,
            company_id    TEXT    NOT NULL REFERENCES companies(id),
            rate_bp       INTEGER NOT NULL,   -- integrated GST rate in basis points (1800 = 18%)
            label         TEXT    NOT NULL,
            is_predefined INTEGER NOT NULL DEFAULT 0
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
            currency_id              TEXT        NULL REFERENCES currencies(id),
            -- v13 core GST (catalog §12): party GST details (Sundry Debtor/Creditor), all NULL for a
            -- non-party ledger; sales/purchase-ledger GST; and the tax-ledger classification for the
            -- auto-created Output/Input CGST/SGST/IGST ledgers.
            party_gst_reg_type   INTEGER     NULL,             -- GstRegistrationType enum ordinal (party)
            party_gstin          TEXT        NULL,             -- party GSTIN, NULL when B2C/unset
            party_gst_state      TEXT        NULL,             -- party 2-digit GST state code
            sp_gst_hsn           TEXT        NULL,             -- sales/purchase ledger HSN/SAC
            sp_gst_taxability    INTEGER     NULL,             -- GstTaxability enum ordinal (NULL = no S/P GST block)
            sp_gst_rate_bp       INTEGER     NULL,             -- sales/purchase ledger rate in basis points
            sp_gst_supply_type   INTEGER     NULL,             -- GstSupplyType enum ordinal
            gst_tax_head         INTEGER     NULL,             -- GstTaxHead enum ordinal (tax ledger only)
            gst_tax_direction    INTEGER     NULL,             -- GstTaxDirection enum ordinal (tax ledger only)
            -- v19 (Phase 6 slice 3; RQ-16..RQ-20): "Method of Appropriation in Purchase invoice" — a NON-NULL value
            -- marks this Direct-Expenses ledger as an additional-cost ledger (0 = ByQuantity, 1 = ByValue). NULL
            -- (the default) = a plain P&L ledger that never touches a stock rate (RQ-19).
            method_of_appropriation INTEGER  NULL,             -- MethodOfAppropriation enum ordinal, or NULL
            -- v21 (Phase 6 slice 5; RQ-30): a party ledger's default Price Level. Nullable FK; NULL (the default for
            -- every existing ledger) = no default level. Only meaningful while enable_multiple_price_levels is on.
            default_price_level_id  TEXT     NULL REFERENCES price_levels(id)
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
            affects_stock    INTEGER NOT NULL DEFAULT 0,   -- 0/1
            -- v18 (RQ-11): "Use as Manufacturing Journal" — a user-created Stock-Journal type flagged as a
            -- Manufacturing Journal. 0/1, default 0 so every existing Stock Journal is byte-identical (ER-13).
            use_as_manufacturing_journal INTEGER NOT NULL DEFAULT 0,   -- 0/1
            -- v19 (Phase 6 slice 3; RQ-16..RQ-20): "Track Additional Costs for Purchases" — a Purchase voucher-type
            -- flag enabling the additional-cost apportionment path. 0/1, default 0 so an existing type is unchanged (ER-13).
            track_additional_costs INTEGER NOT NULL DEFAULT 0,        -- 0/1
            -- v20 (Phase 6 slice 4; RQ-21): "Allow zero-valued transactions" — a Sales/Purchase voucher-type flag
            -- permitting ₹0 free-goods item lines. 0/1, default 0 so an existing type is byte-identical (ER-13).
            allow_zero_valued INTEGER NOT NULL DEFAULT 0,             -- 0/1
            -- v23 (Phase 6 slice 7; RQ-38): "Use for POS invoicing" — a Sales voucher-type flag marking a POS
            -- (retail-till) invoice. 0/1, default 0 so an existing type is byte-identical (ER-13). The retail-till
            -- config lives in pos_voucher_type_config; the tender-ledger class map in pos_tender_ledger_defaults.
            use_for_pos INTEGER NOT NULL DEFAULT 0                    -- 0/1
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
            forex_rate_micro   INTEGER NULL,   -- rate (base per foreign) × 1,000,000 (exact, no float)
            -- v13 core GST (catalog §12): tax-line detail, all NULL for a non-tax line
            gst_tax_head          INTEGER NULL,   -- GstTaxHead enum ordinal (Central/State/Integrated)
            gst_rate_bp           INTEGER NULL,   -- applied head rate in basis points (900 = 9% CGST half)
            gst_taxable_value_paisa INTEGER NULL  -- the taxable value the tax was computed on, in paisa
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
            hsn_sac_code        TEXT        NULL,             -- Phase-3 HSN/SAC placeholder (now backed by the GST block)
            is_taxable          INTEGER NOT NULL DEFAULT 0,   -- Phase-3 taxability placeholder 0/1
            reorder_level_micro INTEGER     NULL,             -- simple reorder level, qty × 1,000,000
            min_order_qty_micro INTEGER     NULL,             -- minimum order qty, qty × 1,000,000
            standard_cost_paisa INTEGER     NULL,             -- Standard-Cost valuation rate in paisa (NULL = unset)
            -- v13 core GST (catalog §12): the active item GST block; gst_taxability NULL = no GST block
            gst_hsn_sac         TEXT        NULL,             -- HSN/SAC (4/6/8 digits)
            gst_taxability      INTEGER     NULL,             -- GstTaxability enum ordinal (NULL = no GST block)
            gst_rate_bp         INTEGER     NULL,             -- integrated GST rate in basis points
            gst_supply_type     INTEGER     NULL,             -- GstSupplyType enum ordinal (Goods/Services)
            -- v16 (RQ-2): the three independent batch switches. 0/1, default 0 so an existing item is unchanged
            -- (ER-13). Use-Expiry may be on without Track-Mfg; all three are model flags read/written verbatim.
            maintain_in_batches      INTEGER NOT NULL DEFAULT 0,   -- "Maintain in Batches" 0/1
            track_manufacturing_date INTEGER NOT NULL DEFAULT 0,   -- "Track date of Manufacturing" 0/1
            use_expiry_dates         INTEGER NOT NULL DEFAULT 0,   -- "Use Expiry dates" 0/1
            -- v18 (RQ-10): "Set Components (BOM)" — the item is a manufactured finished good with ≥1 BOM. 0/1,
            -- default 0 so an existing item is unchanged (ER-13); a plain model flag read/written verbatim.
            set_components           INTEGER NOT NULL DEFAULT 0    -- 0/1
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
            expiry_date        TEXT        NULL,             -- forward-compat (Phase 6), no behaviour yet
            -- v16 (RQ-1): optional first-class batch this opening layer belongs to; NULL for non-batch items.
            batch_id           TEXT        NULL REFERENCES batch_masters(id)
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
            quantity_micro    INTEGER NOT NULL,               -- qty × 1,000,000 (> 0) — the Actual (stock) quantity
            direction         INTEGER NOT NULL,               -- StockDirection ordinal (Inward=0, Outward=1)
            rate_paisa        INTEGER     NULL,               -- optional per-unit rate in paisa
            batch_label       TEXT        NULL,
            -- v16 (RQ-1/RQ-3): optional first-class batch this movement allocates to; NULL for non-batch lines.
            batch_id          TEXT        NULL REFERENCES batch_masters(id),
            -- v20 (Phase 6 slice 4; RQ-22; DP-7): the Actual/Billed split, symmetric with voucher_inventory_lines.
            -- NULL ⇒ "not split": actual defaults to quantity_micro, billed defaults to actual. In practice these stay
            -- NULL on the pure-stock table (A/B is Sales/Purchase-only, on voucher_inventory_lines); kept for symmetry.
            actual_qty_micro  INTEGER     NULL,               -- Actual qty × 1,000,000, or NULL = quantity_micro
            billed_qty_micro  INTEGER     NULL                -- Billed qty × 1,000,000, or NULL = actual
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
            batch_label       TEXT        NULL,
            -- v16 (RQ-1): optional first-class batch this physical count applies to; NULL for non-batch lines.
            batch_id          TEXT        NULL REFERENCES batch_masters(id)
        );

        -- v12 (catalog §10; slice 3.3b): Item-Invoice stock lines on a Purchase/Sales accounting voucher.
        CREATE TABLE voucher_inventory_lines (
            id                INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
            voucher_id        TEXT    NOT NULL REFERENCES vouchers(id),   -- the accounting voucher it hangs off
            line_order        INTEGER NOT NULL,               -- preserves order within the voucher
            stock_item_id     TEXT    NOT NULL REFERENCES stock_items(id),
            godown_id         TEXT    NOT NULL REFERENCES godowns(id),
            quantity_micro    INTEGER NOT NULL,               -- qty × 1,000,000 (> 0) — the Actual (stock) quantity
            direction         INTEGER NOT NULL,               -- StockDirection ordinal (Inward=0, Outward=1)
            rate_paisa        INTEGER NOT NULL,               -- per-unit rate in paisa (0 for a zero-valued free line)
            batch_label       TEXT        NULL,
            -- v16 (RQ-1/RQ-3): optional first-class batch this item-invoice line allocates to; NULL for non-batch.
            batch_id          TEXT        NULL REFERENCES batch_masters(id),
            -- v20 (Phase 6 slice 4; RQ-22/RQ-23; DP-7): the Actual/Billed split. quantity_micro REMAINS the Actual
            -- (stock) quantity — every existing reader is unchanged. actual_qty_micro mirrors it (written only when
            -- the split is active, else NULL) and billed_qty_micro carries Billed only when it differs from Actual
            -- (else NULL). NULL ⇒ Billed ≡ Actual, so a feature-off line round-trips byte-identically (ER-13).
            actual_qty_micro  INTEGER     NULL,               -- Actual qty × 1,000,000, or NULL = quantity_micro
            billed_qty_micro  INTEGER     NULL                -- Billed qty × 1,000,000, or NULL = actual
        );

        -- v19 (Phase 6 slice 3; RQ-20): additional-cost lines on a Stock-Journal TRANSFER inventory voucher. Each
        -- row apportions an additional-cost ledger amount across the voucher's destination allocations (raising
        -- their landed inward rate by the ledger's method_of_appropriation). Backs ONLY the transfer case — a
        -- Purchase item-invoice needs no row (its additional cost is an ordinary entry_lines Dr to the ledger).
        CREATE TABLE additional_cost_lines (
            id                   INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
            inventory_voucher_id TEXT    NOT NULL REFERENCES inventory_vouchers(id),
            line_order           INTEGER NOT NULL,
            ledger_id            TEXT    NOT NULL REFERENCES ledgers(id),
            amount_paisa         INTEGER NOT NULL
        );

        -- v16 (RQ-1/RQ-4/RQ-6; DP-8): the BATCH master — one first-class row per (stock item, batch number),
        -- REPLACING today's bare batch_label text. batch_no is unique WITHIN an item (the ux index below), NOT
        -- globally (Cluster 1 subtlety d), so two different items may reuse the same batch number. mfg_date and
        -- expiry_date are optional (RQ-2): expiry_date is the resolved absolute date; expiry_period holds the raw
        -- "12 Months"-style text when the user enters a period (RQ-4) that the engine resolves to expiry_date
        -- (mfg + period). The optional per-batch inward cost layer (inward_qty_micro / inward_rate_paisa, RQ-6,
        -- DP-8) records the batch's own inward quantity and rate so per-batch cost may differ from the item's
        -- overall average; godown_id is the layer's location (batch on-hand is per item/godown/batch, RQ-5).
        -- Money = INTEGER paisa; qty = INTEGER micros (× 1,000,000). All cost-layer columns are NULL until an
        -- inward populates them; a batch may exist as a pure label with no cost layer yet.
        CREATE TABLE batch_masters (
            id                TEXT    NOT NULL PRIMARY KEY,
            company_id        TEXT    NOT NULL REFERENCES companies(id),
            stock_item_id     TEXT    NOT NULL REFERENCES stock_items(id),
            batch_no          TEXT    NOT NULL,               -- unique within the item (see ux index), not global
            mfg_date          TEXT        NULL,               -- ISO yyyy-MM-dd, or NULL (RQ-2 Track date of Mfg)
            expiry_date       TEXT        NULL,               -- resolved absolute ISO yyyy-MM-dd, or NULL (RQ-4)
            expiry_period     TEXT        NULL,               -- raw period text ("12 Months") when entered as a period (RQ-4)
            godown_id         TEXT        NULL REFERENCES godowns(id),   -- inward layer location (RQ-5), NULL if unknown
            inward_qty_micro  INTEGER     NULL,               -- per-batch inward qty × 1,000,000 (RQ-6/DP-8), NULL = no layer
            inward_rate_paisa INTEGER     NULL                -- per-batch inward rate in paisa (RQ-6/DP-8), NULL = no layer
        );

        -- v17 (RQ-9; ER-1): the Bill of Materials HEADER — one first-class row per named BOM on a finished-good
        -- stock item. A single item may own MULTIPLE BOMs (RQ-9: "multiple per item"), each distinguished by name;
        -- unit_of_manufacture_micro is the BLOCK size (e.g. 1 or 10 units) the component quantities are expressed
        -- per, stored as INTEGER micros (× 1,000,000) so a fractional block round-trips exactly (no float, ER-3).
        CREATE TABLE bill_of_materials (
            id                          TEXT    NOT NULL PRIMARY KEY,
            company_id                  TEXT    NOT NULL REFERENCES companies(id),
            stock_item_id               TEXT    NOT NULL REFERENCES stock_items(id),   -- the finished good this BOM makes
            name                        TEXT    NOT NULL,               -- BOM name, unique within the item (see ux index)
            unit_of_manufacture_micro   INTEGER NOT NULL                -- block size × 1,000,000 (> 0), qty basis for the lines
        );

        -- v17 (RQ-9; DP-3): the BOM component/output LINES. line_type distinguishes an input Component from a
        -- carved-out By-Product / Co-Product / Scrap output (enum ordinal: Component=0, ByProduct=1, CoProduct=2,
        -- Scrap=3). qty_micro is the PER-BLOCK quantity (× 1,000,000) the Manufacturing Journal auto-scales by the
        -- produced quantity. godown_id is the line's consumption/output location (RQ-9), NULL = resolve at posting.
        -- The carve-out VALUE basis (DP-3) for a By-Product/Co-Product/Scrap line is user-entered as either an
        -- absolute per-unit rate_paisa (INTEGER paisa) OR a percent of finished-good cost (percent_millis =
        -- percent × 1,000, exact to 3 dp); both NULL = default to standard cost (DP-3 default). rate_paisa also
        -- carries an additional-cost line's per-unit/blanket value when line_type = Component (additional cost).
        CREATE TABLE bom_lines (
            id                    INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
            bom_id                TEXT    NOT NULL REFERENCES bill_of_materials(id),
            line_order            INTEGER NOT NULL,               -- preserves order within the BOM
            line_type             INTEGER NOT NULL,               -- BomLineType ordinal (Component=0, ByProduct=1, CoProduct=2, Scrap=3)
            component_stock_item_id TEXT  NOT NULL REFERENCES stock_items(id),
            godown_id             TEXT        NULL REFERENCES godowns(id),   -- consumption/output location, NULL = resolve at posting
            qty_micro             INTEGER NOT NULL,               -- per-block quantity × 1,000,000 (> 0)
            rate_paisa            INTEGER     NULL,               -- carve-out / additional-cost per-unit value in paisa (DP-3), NULL = default
            percent_millis        INTEGER     NULL                -- carve-out % of FG cost × 1,000 (DP-3), NULL = not a %-basis line
        );

        -- v21 (Phase 6 slice 5; RQ-26): the named Price Levels — a bare per-company master (Wholesale/Retail…),
        -- unique name within a company (see ux index). A level is nothing but an id + name; the party default
        -- (ledgers.default_price_level_id) and the dated price lists reference it.
        CREATE TABLE price_levels (
            id          TEXT NOT NULL PRIMARY KEY,
            company_id  TEXT NOT NULL REFERENCES companies(id),
            name        TEXT NOT NULL
        );

        -- v21 (Phase 6 slice 5; RQ-27): the Price List HEADER — one dated version per (level, item). A revision
        -- APPENDS a new row with a later applicable_from; older rows are retained (append-only history). The
        -- resolver picks the latest applicable_from <= voucher date (RQ-29). applicable_from is ISO yyyy-MM-dd TEXT
        -- (culture-invariant, ER-10).
        CREATE TABLE price_lists (
            id              TEXT NOT NULL PRIMARY KEY,
            company_id      TEXT NOT NULL REFERENCES companies(id),
            price_level_id  TEXT NOT NULL REFERENCES price_levels(id),
            stock_item_id   TEXT NOT NULL REFERENCES stock_items(id),
            applicable_from TEXT NOT NULL              -- ISO yyyy-MM-dd
        );

        -- v21 (Phase 6 slice 5; RQ-27/RQ-28): the Price List SLAB lines. Quantities are INTEGER micros
        -- (× 1,000,000, QuantityScale); to_qty_micro NULL = open-ended top slab. rate_paisa = INTEGER paisa (Money
        -- boundary, ER-2). discount_percent_millis = percent × 1,000 (INTEGER millis, exact to 3 dp — mirrors
        -- bom_lines.percent_millis / interest_rate_millis). Slabs are contiguous/ascending/non-overlapping
        -- (service-validated); From is inclusive (≥), To is exclusive (<) — the boundary lands in the higher slab.
        CREATE TABLE price_list_lines (
            id                      INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
            price_list_id           TEXT    NOT NULL REFERENCES price_lists(id),
            line_order              INTEGER NOT NULL,        -- preserves slab order within the list
            from_qty_micro          INTEGER NOT NULL,        -- inclusive lower bound  (From ≥)
            to_qty_micro            INTEGER     NULL,        -- exclusive upper bound (To <); NULL = open-ended
            rate_paisa              INTEGER NOT NULL,        -- per-unit rate in paisa
            discount_percent_millis INTEGER NOT NULL DEFAULT 0 -- discount% × 1,000 (0 = none)
        );

        -- v22 (Phase 6 slice 6; RQ-32..RQ-35): Reorder Level definitions — per Stock Item / Group / Category
        -- (scope 0/1/2). At most one row per (scope, target_id) (see ux index). reorder_advanced / minqty_advanced
        -- are the independent Alt+S / Alt+V Simple(0)/Advanced(1) flags; the *_qty_micro fixed figures are qty ×
        -- 1,000,000 (NULL = unset). A single shared period (period_unit = ExpiryPeriodUnit ordinal 0..3 + count > 0)
        -- and criteria (0=Higher 1=Lower) govern BOTH Advanced figures (DD-1); all three are NULL when neither
        -- figure is Advanced. Quantity-only (no money) — all values are INTEGER micros (ER-3).
        CREATE TABLE reorder_definitions (
            id                   TEXT    NOT NULL PRIMARY KEY,
            company_id           TEXT    NOT NULL REFERENCES companies(id),
            scope                INTEGER NOT NULL,               -- 0=Item 1=Group 2=Category
            target_id            TEXT    NOT NULL,               -- StockItem / StockGroup / StockCategory id
            reorder_advanced     INTEGER NOT NULL DEFAULT 0,     -- Alt+S: 0=Simple 1=Advanced
            reorder_qty_micro    INTEGER     NULL,               -- fixed reorder level × 1,000,000 (NULL=unset)
            minqty_advanced      INTEGER NOT NULL DEFAULT 0,     -- Alt+V: 0=Simple 1=Advanced
            min_order_qty_micro  INTEGER     NULL,               -- fixed min order qty × 1,000,000 (NULL=unset)
            period_unit          INTEGER     NULL,               -- ExpiryPeriodUnit ordinal 0=Days..3=Years
            period_count         INTEGER     NULL,               -- rolling-window length (>0) when Advanced
            criteria             INTEGER     NULL                -- 0=Higher 1=Lower (when Advanced)
        );

        -- v23 (Phase 6 slice 7; RQ-38): the retail-till config for a POS-flagged Sales voucher type — ONE row per
        -- POS type (voucher_type_id is the PRIMARY KEY). Keeps the RQ-38 config strings off the lean voucher_types
        -- row: the pre-selected default godown / default party (NULL party = walk-in "(cash)"), whether to open the
        -- receipt preview after Accept, the receipt title, two thank-you messages, and the declaration line.
        CREATE TABLE pos_voucher_type_config (
            voucher_type_id   TEXT    NOT NULL PRIMARY KEY REFERENCES voucher_types(id),
            default_godown_id TEXT        NULL REFERENCES godowns(id),
            default_party_id  TEXT        NULL REFERENCES ledgers(id),   -- NULL = walk-in "(cash)"
            print_after_save  INTEGER NOT NULL DEFAULT 0,                -- 0/1
            default_title     TEXT        NULL,
            message_1         TEXT        NULL,
            message_2         TEXT        NULL,
            declaration       TEXT        NULL
        );

        -- v23 (Phase 6 slice 7; RQ-38/DP-4): the POS Voucher Class tender-ledger pre-map — up to 4 rows per POS
        -- type (one per tender kind), pre-filled at entry and overridable. Keyed by (voucher_type_id, tender_type).
        CREATE TABLE pos_tender_ledger_defaults (
            voucher_type_id  TEXT    NOT NULL REFERENCES voucher_types(id),
            tender_type      INTEGER NOT NULL,               -- PosTenderType ordinal (0 Gift,1 Card,2 Cheque,3 Cash)
            ledger_id        TEXT    NOT NULL REFERENCES ledgers(id),
            PRIMARY KEY (voucher_type_id, tender_type)
        );

        -- v23 (Phase 6 slice 7; RQ-39/RQ-40; DP-6): the per-POS-voucher tender rows — the metadata entry_lines
        -- cannot carry (tender kind, cash tendered/change, card/bank/cheque). Keyed by voucher_id. amount_paisa is
        -- the POSTED payable share (Cash = residual, NOT tendered); tendered/change are Cash-only (change is
        -- informational, never a ledger line). Money = INTEGER paisa (no float). No persisted POS session object.
        CREATE TABLE pos_tender_allocations (
            id             INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
            voucher_id     TEXT    NOT NULL REFERENCES vouchers(id),
            tender_order   INTEGER NOT NULL,                 -- stable order (Gift,Card,Cheque,Cash)
            tender_type    INTEGER NOT NULL,                 -- PosTenderType ordinal
            ledger_id      TEXT    NOT NULL REFERENCES ledgers(id),
            amount_paisa   INTEGER NOT NULL,                 -- posted payable share (Cash = residual, not tendered)
            tendered_paisa INTEGER     NULL,                 -- Cash only: Cash Tendered
            change_paisa   INTEGER     NULL,                 -- Cash only: informational change = tendered − payable
            card_no        TEXT        NULL,                 -- Card only
            bank_name      TEXT        NULL,                 -- Cheque/DD only
            cheque_no      TEXT        NULL                  -- Cheque/DD only
        );

        -- v14 (RQ-8 Save View): a named, config-only report view per company. `config_json` holds ONLY the
        -- report configuration tuple (kind/period/depth/sort/filter/comparative/F12) — never a computed figure;
        -- the report is always recomputed when the view is applied, so a saved view can never go stale (ER-9).
        CREATE TABLE saved_views (
            id           TEXT    NOT NULL PRIMARY KEY,
            company_id   TEXT    NOT NULL,                    -- the owning company id (per-company isolation key)
            name         TEXT    NOT NULL,                    -- unique within a company (case-insensitive)
            config_json  TEXT    NOT NULL                     -- the SavedReportView config tuple as JSON
        );

        -- v15 (RQ-27 SMTP profile; R13): a capture-only SMTP server profile, ONE row per company (company_id is
        -- the PRIMARY KEY, so a save upserts the single row). Records ONLY the connection identity — host, port,
        -- TLS flag and the sender identity. There is deliberately NO password/secret/credential column: a
        -- credential (if ever needed) lives in the OS secret store / environment, never in this DB. Nothing yet
        -- consumes this row to open a socket — live SMTP send is DEFERRED.
        CREATE TABLE smtp_profile (
            company_id   TEXT    NOT NULL PRIMARY KEY,        -- the owning company id (one profile per company)
            host         TEXT    NOT NULL,                    -- SMTP server host
            port         INTEGER NOT NULL,                    -- submission port (587/465/25…)
            use_tls      INTEGER NOT NULL,                    -- 0/1
            from_address TEXT    NOT NULL,                    -- envelope/from address
            from_name    TEXT        NULL                     -- optional display name
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
        CREATE INDEX ix_voucher_inv_lines_voucher ON voucher_inventory_lines(voucher_id);
        CREATE INDEX ix_additional_cost_lines_voucher ON additional_cost_lines(inventory_voucher_id);
        -- v16: batch masters — one lookup index by company + item, and the per-item-unique batch number key
        -- (batch numbers are unique WITHIN an item, not globally — RQ-1 / Cluster 1 subtlety d).
        CREATE INDEX ix_batch_masters_company        ON batch_masters(company_id);
        CREATE INDEX ix_batch_masters_item           ON batch_masters(stock_item_id);
        CREATE UNIQUE INDEX ux_batch_masters_item_no ON batch_masters(stock_item_id, batch_no);
        -- v17: Bill of Materials — lookup by company + finished-good item, child lines by BOM, and the per-item
        -- unique BOM-name key (a BOM name is unique WITHIN its finished good, RQ-9 "multiple per item").
        CREATE INDEX ix_bom_company            ON bill_of_materials(company_id);
        CREATE INDEX ix_bom_item               ON bill_of_materials(stock_item_id);
        CREATE UNIQUE INDEX ux_bom_item_name   ON bill_of_materials(stock_item_id, name COLLATE NOCASE);
        CREATE INDEX ix_bom_lines_bom          ON bom_lines(bom_id);
        -- v21: price levels — lookup by company + the per-company case-insensitive unique name; price lists —
        -- lookup by company + the (level, item) resolution key; slab lines by their list.
        CREATE INDEX ix_price_levels_company     ON price_levels(company_id);
        CREATE UNIQUE INDEX ux_price_levels_name ON price_levels(company_id, name COLLATE NOCASE);
        CREATE INDEX ix_price_lists_company      ON price_lists(company_id);
        CREATE INDEX ix_price_lists_level_item   ON price_lists(price_level_id, stock_item_id);
        CREATE INDEX ix_price_list_lines_list    ON price_list_lines(price_list_id);
        -- v22: reorder definitions — lookup by company + the per-(scope, target) unique key (at most one
        -- definition per item/group/category, RQ-32).
        CREATE INDEX ix_reorder_definitions_company      ON reorder_definitions(company_id);
        CREATE UNIQUE INDEX ux_reorder_definitions_scope ON reorder_definitions(scope, target_id);
        -- v23: POS tender allocations — lookup by owning voucher; the config + class-map tables are keyed by their
        -- PRIMARY KEY so they need no extra index.
        CREATE INDEX ix_pos_tender_allocations_voucher ON pos_tender_allocations(voucher_id);
        -- v14: one saved view per (company, name); the unique index enforces the case-insensitive upsert key.
        CREATE UNIQUE INDEX ux_saved_views_company_name ON saved_views(company_id, name COLLATE NOCASE);
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

    /// <summary>
    /// The v11→v12 migration (catalog §10 Item-Invoice mode; slice 3.3b): creates the
    /// <c>voucher_inventory_lines</c> child table (item lines on a Purchase/Sales accounting voucher) + its
    /// index. Pure CREATE TABLE/INDEX — no ALTER, no row rewrites — so an existing v11 database keeps all its
    /// data and simply gains an empty table (there are no item-invoice lines until a company is next saved).
    /// Run inside a transaction that also bumps <c>schema_version</c> to 12. A fresh DB is stamped straight to
    /// v12 via <see cref="CreateV1"/>.
    /// </summary>
    public const string MigrateV11ToV12 = """
        CREATE TABLE voucher_inventory_lines (
            id                INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
            voucher_id        TEXT    NOT NULL REFERENCES vouchers(id),
            line_order        INTEGER NOT NULL,
            stock_item_id     TEXT    NOT NULL REFERENCES stock_items(id),
            godown_id         TEXT    NOT NULL REFERENCES godowns(id),
            quantity_micro    INTEGER NOT NULL,
            direction         INTEGER NOT NULL,
            rate_paisa        INTEGER NOT NULL,
            batch_label       TEXT        NULL
        );

        CREATE INDEX ix_voucher_inv_lines_voucher ON voucher_inventory_lines(voucher_id);
        """;

    /// <summary>
    /// v12 → v13: <b>core GST</b> (catalog §12; phase4 ER-1). Adds the company GST config columns, the
    /// <c>gst_rate_slabs</c> table, party/sales-purchase/tax-ledger GST columns on <c>ledgers</c>, the item
    /// GST block columns on <c>stock_items</c>, and the tax-line detail columns on <c>entry_lines</c> — all as
    /// <c>ALTER TABLE … ADD COLUMN</c> with GST-off / NULL defaults plus one new <c>CREATE TABLE</c>. It never
    /// rewrites an existing row: an existing v12 company reloads GST-off (byte-for-byte unchanged, ER-10). Run
    /// inside a transaction that also bumps <c>schema_version</c> to 13. A fresh DB is stamped straight to v13
    /// via <see cref="CreateV1"/>.
    /// </summary>
    public const string MigrateV12ToV13 = """
        ALTER TABLE companies ADD COLUMN gst_enabled         INTEGER NOT NULL DEFAULT 0;
        ALTER TABLE companies ADD COLUMN gstin               TEXT        NULL;
        ALTER TABLE companies ADD COLUMN gst_home_state      TEXT        NULL;
        ALTER TABLE companies ADD COLUMN gst_reg_type        INTEGER     NULL;
        ALTER TABLE companies ADD COLUMN gst_applicable_from TEXT        NULL;
        ALTER TABLE companies ADD COLUMN gst_periodicity     INTEGER     NULL;

        CREATE TABLE gst_rate_slabs (
            id            TEXT    NOT NULL PRIMARY KEY,
            company_id    TEXT    NOT NULL REFERENCES companies(id),
            rate_bp       INTEGER NOT NULL,
            label         TEXT    NOT NULL,
            is_predefined INTEGER NOT NULL DEFAULT 0
        );

        CREATE INDEX ix_gst_rate_slabs_company ON gst_rate_slabs(company_id);

        ALTER TABLE ledgers ADD COLUMN party_gst_reg_type INTEGER NULL;
        ALTER TABLE ledgers ADD COLUMN party_gstin        TEXT    NULL;
        ALTER TABLE ledgers ADD COLUMN party_gst_state    TEXT    NULL;
        ALTER TABLE ledgers ADD COLUMN sp_gst_hsn         TEXT    NULL;
        ALTER TABLE ledgers ADD COLUMN sp_gst_taxability  INTEGER NULL;
        ALTER TABLE ledgers ADD COLUMN sp_gst_rate_bp     INTEGER NULL;
        ALTER TABLE ledgers ADD COLUMN sp_gst_supply_type INTEGER NULL;
        ALTER TABLE ledgers ADD COLUMN gst_tax_head       INTEGER NULL;
        ALTER TABLE ledgers ADD COLUMN gst_tax_direction  INTEGER NULL;

        ALTER TABLE stock_items ADD COLUMN gst_hsn_sac     TEXT    NULL;
        ALTER TABLE stock_items ADD COLUMN gst_taxability  INTEGER NULL;
        ALTER TABLE stock_items ADD COLUMN gst_rate_bp     INTEGER NULL;
        ALTER TABLE stock_items ADD COLUMN gst_supply_type INTEGER NULL;

        ALTER TABLE entry_lines ADD COLUMN gst_tax_head            INTEGER NULL;
        ALTER TABLE entry_lines ADD COLUMN gst_rate_bp             INTEGER NULL;
        ALTER TABLE entry_lines ADD COLUMN gst_taxable_value_paisa INTEGER NULL;
        """;

    /// <summary>
    /// v13 → v14: <b>saved report views</b> (RQ-8 Save View; ER-9, DP-7). Creates the <c>saved_views</c> table
    /// and its unique <c>(company_id, name)</c> index — pure CREATE TABLE/INDEX, no ALTER, no row rewrites — so
    /// an existing v13 database keeps every table and row untouched and simply gains an empty <c>saved_views</c>
    /// table. The <c>company_id</c> is a plain isolation key (no companies FK), so a view can be stored without
    /// coupling to a persisted company row. Each row holds ONLY the report configuration tuple as JSON (kind/period/depth/sort/filter/
    /// comparative/F12), never a computed figure: a saved view is always recomputed against the live company on
    /// apply, so it can never go stale (ER-9). Run inside a transaction that also bumps <c>schema_version</c> to
    /// 14. A fresh DB is stamped straight to v14 via <see cref="CreateV1"/>.
    /// </summary>
    public const string MigrateV13ToV14 = """
        CREATE TABLE saved_views (
            id           TEXT    NOT NULL PRIMARY KEY,
            company_id   TEXT    NOT NULL,
            name         TEXT    NOT NULL,
            config_json  TEXT    NOT NULL
        );

        CREATE UNIQUE INDEX ux_saved_views_company_name ON saved_views(company_id, name COLLATE NOCASE);
        """;

    /// <summary>
    /// v14 → v15: <b>capture-only SMTP profile</b> (RQ-27; R13). Creates the <c>smtp_profile</c> table — a single
    /// per-company row holding host/port/TLS/from-address/from-name only. Pure CREATE TABLE, no ALTER, no row
    /// rewrites — so an existing v14 database keeps every table and row untouched and simply gains an empty
    /// <c>smtp_profile</c> table. The <c>company_id</c> is a plain isolation key + PRIMARY KEY (one profile per
    /// company; no companies FK, so a profile can be stored without a persisted company row). There is
    /// deliberately <b>no password/secret column</b>: a credential — if ever needed — lives in the OS secret
    /// store / environment, never in this DB. Run inside a transaction that also bumps <c>schema_version</c> to
    /// 15. A fresh DB is stamped straight to v15 via <see cref="CreateV1"/>.
    /// </summary>
    public const string MigrateV14ToV15 = """
        CREATE TABLE smtp_profile (
            company_id   TEXT    NOT NULL PRIMARY KEY,
            host         TEXT    NOT NULL,
            port         INTEGER NOT NULL,
            use_tls      INTEGER NOT NULL,
            from_address TEXT    NOT NULL,
            from_name    TEXT        NULL
        );
        """;

    /// <summary>
    /// v15 → v16: <b>batch masters + per-batch inward cost layer</b> (Phase 6 slice 1; RQ-1..RQ-8, DP-8; ER-1).
    /// Creates the <c>batch_masters</c> table (a first-class batch per <c>(stock item, batch number)</c>, batch
    /// numbers unique WITHIN an item — not globally — with optional mfg/expiry dates, an optional resolved-from
    /// expiry-period, and an optional per-batch inward cost layer of qty-micros + rate-paisa) and its indexes,
    /// then adds an <b>additive nullable</b> <c>batch_id</c> reference to the four stock-line tables that carry a
    /// <c>batch_label</c> today (<c>stock_opening_balances</c>, <c>inventory_allocations</c>,
    /// <c>physical_stock_lines</c>, <c>voucher_inventory_lines</c>) plus the three <c>stock_items</c> batch
    /// switches (<c>maintain_in_batches</c> / <c>track_manufacturing_date</c> / <c>use_expiry_dates</c>, RQ-2),
    /// each <c>NOT NULL DEFAULT 0</c> so an existing item is byte-identical (ER-13). It is purely additive: one new
    /// <c>CREATE TABLE</c> + three indexes + four nullable + three defaulted <c>ALTER TABLE … ADD COLUMN</c> — it
    /// never rewrites an existing row and leaves every <c>batch_label</c> column intact (backward-compat, DP-10),
    /// so an existing v15
    /// database keeps all its data and simply gains one empty table and four NULL columns (non-batch data behaves
    /// byte-identically, ER-13). Run inside a transaction that also bumps <c>schema_version</c> to 16. A fresh DB
    /// is stamped straight to v16 via <see cref="CreateV1"/>.
    /// </summary>
    public const string MigrateV15ToV16 = """
        CREATE TABLE batch_masters (
            id                TEXT    NOT NULL PRIMARY KEY,
            company_id        TEXT    NOT NULL REFERENCES companies(id),
            stock_item_id     TEXT    NOT NULL REFERENCES stock_items(id),
            batch_no          TEXT    NOT NULL,
            mfg_date          TEXT        NULL,
            expiry_date       TEXT        NULL,
            expiry_period     TEXT        NULL,
            godown_id         TEXT        NULL REFERENCES godowns(id),
            inward_qty_micro  INTEGER     NULL,
            inward_rate_paisa INTEGER     NULL
        );

        CREATE INDEX ix_batch_masters_company        ON batch_masters(company_id);
        CREATE INDEX ix_batch_masters_item           ON batch_masters(stock_item_id);
        CREATE UNIQUE INDEX ux_batch_masters_item_no ON batch_masters(stock_item_id, batch_no);

        ALTER TABLE stock_opening_balances ADD COLUMN batch_id TEXT NULL REFERENCES batch_masters(id);
        ALTER TABLE inventory_allocations  ADD COLUMN batch_id TEXT NULL REFERENCES batch_masters(id);
        ALTER TABLE physical_stock_lines   ADD COLUMN batch_id TEXT NULL REFERENCES batch_masters(id);
        ALTER TABLE voucher_inventory_lines ADD COLUMN batch_id TEXT NULL REFERENCES batch_masters(id);

        ALTER TABLE stock_items ADD COLUMN maintain_in_batches      INTEGER NOT NULL DEFAULT 0;
        ALTER TABLE stock_items ADD COLUMN track_manufacturing_date INTEGER NOT NULL DEFAULT 0;
        ALTER TABLE stock_items ADD COLUMN use_expiry_dates         INTEGER NOT NULL DEFAULT 0;
        """;

    /// <summary>
    /// v16 → v17: <b>Bill of Materials masters</b> (Phase 6 slice 2; RQ-9; ER-1). Creates the
    /// <c>bill_of_materials</c> header table (one first-class row per named BOM on a finished-good stock item,
    /// with a per-block <c>unit_of_manufacture_micro</c>; a single item may own MULTIPLE BOMs, keyed unique by
    /// name within the item) and the <c>bom_lines</c> child table (each line is a Component / By-Product /
    /// Co-Product / Scrap with a per-block <c>qty_micro</c> and an optional carve-out / additional-cost basis of
    /// per-unit <c>rate_paisa</c> OR <c>percent_millis</c> = percent × 1,000, per DP-3), plus their indexes. It is
    /// purely additive: two new <c>CREATE TABLE</c> + four <c>CREATE INDEX</c> — no ALTER, no row rewrites — so an
    /// existing v16 database keeps every table and row untouched and simply gains two empty BOM tables
    /// (non-BOM/non-manufacturing data behaves byte-identically, ER-13). Money = INTEGER paisa; quantity =
    /// INTEGER micros (× 1,000,000); percent = INTEGER millis (× 1,000) — no float (ER-3). Run inside a
    /// transaction that also bumps <c>schema_version</c> to 17. A fresh DB is stamped straight to v17 via
    /// <see cref="CreateV1"/>.
    /// </summary>
    public const string MigrateV16ToV17 = """
        CREATE TABLE bill_of_materials (
            id                          TEXT    NOT NULL PRIMARY KEY,
            company_id                  TEXT    NOT NULL REFERENCES companies(id),
            stock_item_id               TEXT    NOT NULL REFERENCES stock_items(id),
            name                        TEXT    NOT NULL,
            unit_of_manufacture_micro   INTEGER NOT NULL
        );

        CREATE TABLE bom_lines (
            id                    INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
            bom_id                TEXT    NOT NULL REFERENCES bill_of_materials(id),
            line_order            INTEGER NOT NULL,
            line_type             INTEGER NOT NULL,
            component_stock_item_id TEXT  NOT NULL REFERENCES stock_items(id),
            godown_id             TEXT        NULL REFERENCES godowns(id),
            qty_micro             INTEGER NOT NULL,
            rate_paisa            INTEGER     NULL,
            percent_millis        INTEGER     NULL
        );

        CREATE INDEX ix_bom_company            ON bill_of_materials(company_id);
        CREATE INDEX ix_bom_item               ON bill_of_materials(stock_item_id);
        CREATE UNIQUE INDEX ux_bom_item_name   ON bill_of_materials(stock_item_id, name COLLATE NOCASE);
        CREATE INDEX ix_bom_lines_bom          ON bom_lines(bom_id);
        """;

    /// <summary>
    /// v17 → v18: the two <b>Manufacturing-Journal master flags</b> (Phase 6 slice 2; RQ-10/RQ-11; ER-1). Adds
    /// <c>voucher_types.use_as_manufacturing_journal</c> (RQ-11: a user-created Stock-Journal type flagged as a
    /// Manufacturing Journal — it cannot be re-derived from data, so it must be stored to round-trip losslessly)
    /// and <c>stock_items.set_components</c> (RQ-10: the finished good carries a BOM). Two
    /// <c>ALTER TABLE … ADD COLUMN … DEFAULT 0</c> — no row rewrites — so an existing v17 database keeps every
    /// row untouched and both flags default off, i.e. non-manufacturing data behaves byte-identically (ER-13). Run
    /// inside a transaction that also bumps <c>schema_version</c> to 18. A fresh DB is stamped straight to v18 via
    /// <see cref="CreateV1"/>.
    /// </summary>
    public const string MigrateV17ToV18 = """
        ALTER TABLE voucher_types ADD COLUMN use_as_manufacturing_journal INTEGER NOT NULL DEFAULT 0;
        ALTER TABLE stock_items   ADD COLUMN set_components                INTEGER NOT NULL DEFAULT 0;
        """;

    /// <summary>
    /// v18 → v19: <b>Additional Cost of Purchase</b> (Phase 6 slice 3; RQ-16..RQ-20; ER-1). Adds
    /// <c>voucher_types.track_additional_costs</c> (a Purchase voucher-type flag) and
    /// <c>ledgers.method_of_appropriation</c> (a non-null value marks an additional-cost Direct-Expenses ledger —
    /// 0 = ByQuantity, 1 = ByValue), and creates the <c>additional_cost_lines</c> child table + index that backs the
    /// Stock-Journal-transfer variant (RQ-20). Two additive <c>ALTER TABLE … ADD COLUMN</c> (one <c>DEFAULT 0</c>,
    /// one <c>NULL</c>) plus one pure <c>CREATE TABLE</c>/<c>INDEX</c> — no row rewrites — so an existing v18 database
    /// keeps every row untouched: the flag defaults off and the method defaults NULL, i.e. a plain freight ledger
    /// stays purely P&amp;L (RQ-19) and non-additional-cost data behaves byte-identically (ER-13). The Purchase case
    /// needs NO new row storage (its additional cost is an ordinary <c>entry_lines</c> Dr to the Direct-Expenses
    /// ledger, from which the engine derives the apportionment). Run inside a transaction that also bumps
    /// <c>schema_version</c> to 19. A fresh DB is stamped straight to v19 via <see cref="CreateV1"/>.
    /// </summary>
    public const string MigrateV18ToV19 = """
        ALTER TABLE voucher_types ADD COLUMN track_additional_costs  INTEGER NOT NULL DEFAULT 0;
        ALTER TABLE ledgers       ADD COLUMN method_of_appropriation INTEGER     NULL;

        CREATE TABLE additional_cost_lines (
            id                   INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
            inventory_voucher_id TEXT    NOT NULL REFERENCES inventory_vouchers(id),
            line_order           INTEGER NOT NULL,
            ledger_id            TEXT    NOT NULL REFERENCES ledgers(id),
            amount_paisa         INTEGER NOT NULL
        );

        CREATE INDEX ix_additional_cost_lines_voucher ON additional_cost_lines(inventory_voucher_id);
        """;

    /// <summary>
    /// v19 → v20: <b>Zero-valued transactions &amp; separate Actual-vs-Billed quantity</b> (Phase 6 slice 4;
    /// RQ-21..RQ-25; ER-1, ER-13; DP-7). Purely additive — four nullable Actual/Billed qty columns (two on each
    /// stock-line table, <c>voucher_inventory_lines</c> + <c>inventory_allocations</c>) plus one
    /// <c>DEFAULT 0</c> flag column on <c>companies</c> (F11 "Use separate Actual &amp; Billed Quantity") and one
    /// on <c>voucher_types</c> ("Allow zero-valued transactions"). No <c>ALTER</c> that rewrites rows, no data
    /// loss: existing v19 rows keep every value, the two flags default OFF and the four qty columns default NULL,
    /// so <c>quantity_micro</c> remains the Actual (stock) quantity and Billed ≡ Actual — non-A/B, non-zero-valued
    /// data behaves byte-identically (ER-13). Run inside a transaction that also bumps <c>schema_version</c> to 20.
    /// A fresh DB is stamped straight to v20 via <see cref="CreateV1"/>.
    /// </summary>
    public const string MigrateV19ToV20 = """
        ALTER TABLE voucher_inventory_lines ADD COLUMN actual_qty_micro INTEGER NULL;
        ALTER TABLE voucher_inventory_lines ADD COLUMN billed_qty_micro INTEGER NULL;
        ALTER TABLE inventory_allocations   ADD COLUMN actual_qty_micro INTEGER NULL;
        ALTER TABLE inventory_allocations   ADD COLUMN billed_qty_micro INTEGER NULL;

        ALTER TABLE companies     ADD COLUMN use_separate_actual_billed_qty INTEGER NOT NULL DEFAULT 0;
        ALTER TABLE voucher_types ADD COLUMN allow_zero_valued              INTEGER NOT NULL DEFAULT 0;
        """;

    /// <summary>
    /// v20 → v21: <b>Price Levels &amp; Price Lists</b> (Phase 6 slice 5; RQ-26..RQ-31; ER-1, ER-13). Purely
    /// additive — creates the three new tables <c>price_levels</c> (a bare named per-company master),
    /// <c>price_lists</c> (one dated version header per (level, item), append-only) and <c>price_list_lines</c>
    /// (the quantity slabs: micros/paisa/millis, From≥ / To&lt;), plus two additive <c>ALTER … ADD COLUMN</c>:
    /// <c>companies.enable_multiple_price_levels</c> (F11 toggle, <c>DEFAULT 0</c>) and
    /// <c>ledgers.default_price_level_id</c> (a party's default level, nullable FK). No <c>ALTER</c> that rewrites
    /// rows, no data loss: existing v20 rows keep every value, the flag defaults OFF and the FK defaults NULL, so a
    /// non-price-level company is byte-identical (ER-13). Run inside a transaction that also bumps
    /// <c>schema_version</c> to 21. A fresh DB is stamped straight to v21 via <see cref="CreateV1"/>.
    /// </summary>
    public const string MigrateV20ToV21 = """
        CREATE TABLE price_levels (
            id          TEXT NOT NULL PRIMARY KEY,
            company_id  TEXT NOT NULL REFERENCES companies(id),
            name        TEXT NOT NULL
        );

        CREATE TABLE price_lists (
            id              TEXT NOT NULL PRIMARY KEY,
            company_id      TEXT NOT NULL REFERENCES companies(id),
            price_level_id  TEXT NOT NULL REFERENCES price_levels(id),
            stock_item_id   TEXT NOT NULL REFERENCES stock_items(id),
            applicable_from TEXT NOT NULL
        );

        CREATE TABLE price_list_lines (
            id                      INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
            price_list_id           TEXT    NOT NULL REFERENCES price_lists(id),
            line_order              INTEGER NOT NULL,
            from_qty_micro          INTEGER NOT NULL,
            to_qty_micro            INTEGER     NULL,
            rate_paisa              INTEGER NOT NULL,
            discount_percent_millis INTEGER NOT NULL DEFAULT 0
        );

        ALTER TABLE companies ADD COLUMN enable_multiple_price_levels INTEGER NOT NULL DEFAULT 0;
        ALTER TABLE ledgers   ADD COLUMN default_price_level_id       TEXT    NULL REFERENCES price_levels(id);

        CREATE INDEX ix_price_levels_company     ON price_levels(company_id);
        CREATE UNIQUE INDEX ux_price_levels_name ON price_levels(company_id, name COLLATE NOCASE);
        CREATE INDEX ix_price_lists_company      ON price_lists(company_id);
        CREATE INDEX ix_price_lists_level_item   ON price_lists(price_level_id, stock_item_id);
        CREATE INDEX ix_price_list_lines_list    ON price_list_lines(price_list_id);
        """;

    /// <summary>
    /// v21 → v22: <b>Reorder Levels</b> (Phase 6 slice 6; RQ-32..RQ-35; ER-1, ER-13). Purely additive — creates
    /// the single new table <c>reorder_definitions</c> (a per-company master keyed uniquely by (scope, target_id):
    /// a reorder level defined per Stock Item / Group / Category, with the two independent Simple/Advanced flags,
    /// the fixed qty-micros figures, and a shared Advanced period/criteria) plus its two indexes. No
    /// <c>ALTER</c>, no row rewrite: an existing v21 database gains one empty table and every other table/row is
    /// byte-identical (ER-13). Run inside a transaction that also bumps <c>schema_version</c> to 22. A fresh DB is
    /// stamped straight to v22 via <see cref="CreateV1"/>.
    /// </summary>
    public const string MigrateV21ToV22 = """
        CREATE TABLE reorder_definitions (
            id                   TEXT    NOT NULL PRIMARY KEY,
            company_id           TEXT    NOT NULL REFERENCES companies(id),
            scope                INTEGER NOT NULL,
            target_id            TEXT    NOT NULL,
            reorder_advanced     INTEGER NOT NULL DEFAULT 0,
            reorder_qty_micro    INTEGER     NULL,
            minqty_advanced      INTEGER NOT NULL DEFAULT 0,
            min_order_qty_micro  INTEGER     NULL,
            period_unit          INTEGER     NULL,
            period_count         INTEGER     NULL,
            criteria             INTEGER     NULL
        );

        CREATE INDEX ix_reorder_definitions_company      ON reorder_definitions(company_id);
        CREATE UNIQUE INDEX ux_reorder_definitions_scope ON reorder_definitions(scope, target_id);
        """;

    /// <summary>
    /// v22 → v23: <b>POS (single/multi-tender)</b> (Phase 6 slice 7; RQ-38..RQ-44; ER-1, ER-13; DP-6). Purely
    /// additive — one <c>ALTER TABLE voucher_types ADD COLUMN use_for_pos … DEFAULT 0</c> flag plus three new
    /// tables: <c>pos_voucher_type_config</c> (one retail-till config row per POS-flagged Sales type),
    /// <c>pos_tender_ledger_defaults</c> (the DP-4 tender-ledger class map, up to 4 rows per type) and
    /// <c>pos_tender_allocations</c> (the per-POS-voucher tender rows the balanced entry lines cannot carry), plus
    /// the allocations lookup index. No <c>ALTER</c> that rewrites rows, no data backfill: an existing v22 database
    /// keeps every row untouched, the flag defaults off, and the three tables start empty — so a non-POS company is
    /// byte-identical (ER-13). POS <b>is</b> a Sales voucher (DP-6: no persisted session object); the tender split
    /// is metadata paired with the balanced tender debit lines. Money = INTEGER paisa (no float). Run inside a
    /// transaction that also bumps <c>schema_version</c> to 23. A fresh DB is stamped straight to v23 via
    /// <see cref="CreateV1"/>.
    /// </summary>
    public const string MigrateV22ToV23 = """
        ALTER TABLE voucher_types ADD COLUMN use_for_pos INTEGER NOT NULL DEFAULT 0;

        CREATE TABLE pos_voucher_type_config (
            voucher_type_id   TEXT    NOT NULL PRIMARY KEY REFERENCES voucher_types(id),
            default_godown_id TEXT        NULL REFERENCES godowns(id),
            default_party_id  TEXT        NULL REFERENCES ledgers(id),
            print_after_save  INTEGER NOT NULL DEFAULT 0,
            default_title     TEXT        NULL,
            message_1         TEXT        NULL,
            message_2         TEXT        NULL,
            declaration       TEXT        NULL
        );

        CREATE TABLE pos_tender_ledger_defaults (
            voucher_type_id  TEXT    NOT NULL REFERENCES voucher_types(id),
            tender_type      INTEGER NOT NULL,
            ledger_id        TEXT    NOT NULL REFERENCES ledgers(id),
            PRIMARY KEY (voucher_type_id, tender_type)
        );

        CREATE TABLE pos_tender_allocations (
            id             INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
            voucher_id     TEXT    NOT NULL REFERENCES vouchers(id),
            tender_order   INTEGER NOT NULL,
            tender_type    INTEGER NOT NULL,
            ledger_id      TEXT    NOT NULL REFERENCES ledgers(id),
            amount_paisa   INTEGER NOT NULL,
            tendered_paisa INTEGER     NULL,
            change_paisa   INTEGER     NULL,
            card_no        TEXT        NULL,
            bank_name      TEXT        NULL,
            cheque_no      TEXT        NULL
        );

        CREATE INDEX ix_pos_tender_allocations_voucher ON pos_tender_allocations(voucher_id);
        """;
}
