using Apex.Ledger.Domain;
using Apex.Ledger.Reports;

namespace Apex.Ledger.Services;

/// <summary>
/// The §6 posting invariants, factored out so they are directly unit-testable and
/// shared by <see cref="LedgerService.Post"/>. All money math is in <see cref="Money"/>
/// (decimal) — never <c>double</c>.
/// </summary>
public static class VoucherValidator
{
    /// <summary>Σ debit magnitudes and Σ credit magnitudes over the voucher's lines.</summary>
    public static (Money Debit, Money Credit) Totals(Voucher v) => (v.TotalDebit, v.TotalCredit);

    /// <summary>True iff Σ Dr == Σ Cr (in decimal).</summary>
    public static bool IsBalanced(Voucher v) => v.TotalDebit == v.TotalCredit;

    /// <summary>
    /// Enforces every §6 invariant relevant to posting; throws on the first violation
    /// (never persists a bad voucher). Checks, in order: ≥ 2 lines, positive line amounts,
    /// known-ledger references, date within books, and the balanced-voucher invariant.
    /// </summary>
    public static void EnsureValid(Voucher v, Company c)
        => EnsureValid(v, c, CostAllocationStrictness.Strict);

    /// <summary>
    /// <see cref="EnsureValid(Voucher, Company)"/> with an explicit cost-allocation invariant (see
    /// <see cref="CostAllocationStrictness"/>). Every other check is identical. Only the two rehydration
    /// paths — <c>SqliteCompanyStore.Load</c> and company import — pass anything but
    /// <see cref="CostAllocationStrictness.Strict"/>.
    /// <para><b>The parameter now gates a SECOND entry-only rule</b> — the §10(4) "a composition dealer may not
    /// collect tax" guard at the foot of this method — for the same reason and on the same two paths: a rule that
    /// refuses to POST must not refuse to LOAD, or a book already containing the shape becomes unopenable. A
    /// dedicated name for "entry vs rehydration" would read better than <see cref="CostAllocationStrictness"/>; that
    /// rename touches every caller and is deliberately left to its own slice rather than folded in here.</para>
    /// </summary>
    /// <param name="replacing">
    /// The voucher <c>LedgerService.Replace</c> is swapping OUT, when this validation is on the Alter path;
    /// <c>null</c> on every Post path. It exists for exactly one rule — the Prevent-Duplicate scan below — and it
    /// changes nothing else.
    /// </param>
    public static void EnsureValid(
        Voucher v, Company c, CostAllocationStrictness costAllocationStrictness, Voucher? replacing = null)
    {
        ArgumentNullException.ThrowIfNull(v);
        ArgumentNullException.ThrowIfNull(c);

        // §6.5 referential integrity: the voucher type must be known.
        var voucherType = c.FindVoucherType(v.TypeId);
        if (voucherType is null)
            throw new InvalidVoucherException($"Unknown voucher type {v.TypeId}.");

        // numbering-design-v2 §7 — Prevent Duplicate. When the type opts in, a voucher whose FULLY-RENDERED number
        // collides (ordinal, case-sensitive) with an existing non-deleted voucher of the same type is rejected. The
        // check lives here so the Io import path (which posts every voucher through LedgerService.Post ⇒ EnsureValid)
        // inherits it and cannot bypass it. An Automatic voucher reaches EnsureValid with Number == 0 (assigned AFTER
        // validation) ⇒ renders "" ⇒ never collides; the guard bites on a Manual/pre-set number. A colliding number
        // renders the same string only when two vouchers share an int and the same date-selected affix — a genuine
        // duplicate (restart is deferred, so there is no legitimate repeat), so there is no false-reject. The
        // counterparty reference field is the OTHER party's number and is never run through this guard.
        //
        // 🔴 TWO EXEMPTIONS, both load-bearing for the ALTER path (phase-10-11 S5a), and both PINNED BY THEIR OWN
        // TEST because each is invisible to every other test in the repository:
        //
        //   (a) `other.Id == v.Id` — the voucher BEING re-validated is still on the book while Replace validates
        //       (clause 1 validates before it swaps), so without this skip a voucher would collide with ITSELF and
        //       clause 3 — "the number is preserved" — could never be satisfied under Prevent Duplicates. Deleting
        //       this line used to leave ALL FOUR test projects green (4,699 tests); the coupling is now pinned by
        //       VoucherReplacePreventDuplicateTests.
        //   (b) the pre-existing-collision exemption — a DIFFERENT voucher that ALREADY rendered the same string as
        //       the voucher being replaced. A book can legitimately hold two same-numbered vouchers (posted with
        //       Prevent Duplicates off, or under Manual numbering); flipping the setting on must not make BOTH of
        //       them permanently unalterable, since Replace refuses a renumber and Delete+re-Post is the exact harm
        //       S5a exists to remove. The alteration did not create that collision and is not the place to refuse
        //       it. A collision the alteration DOES create — a date change that moves the rendered number onto a
        //       live one — is still refused.
        if (voucherType.PreventDuplicate)
        {
            var rendered = VoucherNumberFormatter.Render(voucherType, v.Number, v.Date);
            var priorRendered = replacing is null
                ? null
                : VoucherNumberFormatter.Render(voucherType, replacing.Number, replacing.Date);

            if (rendered.Length > 0)
                foreach (var other in c.Vouchers)
                {
                    if (other.Id == v.Id) continue;                 // (a) re-validating an already-posted voucher
                    if (other.TypeId != v.TypeId) continue;
                    if (!string.Equals(
                            VoucherNumberFormatter.Render(voucherType, other.Number, other.Date), rendered,
                            StringComparison.Ordinal))
                        continue;

                    // (b) the collision predates this alteration — refusing it would trap the voucher forever.
                    if (replacing is not null && other.Id != replacing.Id
                        && string.Equals(priorRendered, rendered, StringComparison.Ordinal))
                        continue;

                    throw new InvalidVoucherException(
                        $"Voucher number '{rendered}' already exists for '{voucherType.Name}' " +
                        "(Prevent Duplicates is on).");
                }
        }

        // §11 zero-valued transactions (Phase 6 slice 4 RQ-21): "Allow zero-valued transactions" is a Sales/Purchase
        // feature only. A Journal / Stock-Journal (or any other base) type must never carry it — reject at post time
        // so the illegal configuration can never smuggle a ₹0 accounting entry onto a non-invoice voucher.
        if (voucherType.AllowZeroValuedTransactions &&
            voucherType.BaseType is not (VoucherBaseType.Purchase or VoucherBaseType.Sales))
            throw new InvalidVoucherException(
                $"'Allow zero-valued transactions' is only valid on a Purchase or Sales voucher type; " +
                $"'{voucherType.Name}' is a {voucherType.BaseType}.");

        // §6.2 at least two lines.
        if (v.Lines.Count < 2)
            throw new InvalidVoucherException("A voucher must have at least two entry lines.");

        // §6.3 positive line amounts + §6.5 known ledgers + §5 bill-wise integrity.
        foreach (var line in v.Lines)
        {
            if (line.Amount.Amount <= 0m)
                throw new InvalidVoucherException("Every entry line amount must be > 0.");
            var ledger = c.FindLedger(line.LedgerId)
                ?? throw new InvalidVoucherException($"Entry line references unknown ledger {line.LedgerId}.");

            if (line.HasBillAllocations)
                EnsureBillAllocationsValid(line, ledger);

            if (line.HasCostAllocations)
                EnsureCostAllocationsValid(line, ledger, c, costAllocationStrictness);

            if (line.HasBankAllocation)
                EnsureBankAllocationValid(line, ledger, c);

            if (line.HasForex)
                EnsureForexValid(line, ledger, c);
        }

        // §6.9 date within books.
        if (v.Date < c.BooksBeginFrom)
            throw new InvalidVoucherException(
                $"Voucher date {v.Date:yyyy-MM-dd} is before BooksBeginFrom {c.BooksBeginFrom:yyyy-MM-dd}.");

        // §6.1 the golden invariant: Σ Dr == Σ Cr.
        if (!IsBalanced(v))
            throw new UnbalancedVoucherException(v.TotalDebit, v.TotalCredit);

        // §10 item-invoice mode (slice 3.3b): the accounts↔inventory pairing invariant.
        if (v.HasInventoryLines)
            EnsureItemInvoiceValid(v, c);

        // §11 POS mode (slice 7 RQ-39..RQ-42): the tender-split invariants — entered only when the voucher carries
        // POS tenders, so an ordinary sale is byte-identical (ER-13). The Cr Sales + Cr Output-GST credit side and
        // the item-invoice pairing above are untouched; POS only changes the DEBIT side to a tender split.
        if (v.HasPosTenders)
            EnsurePosTendersValid(v, c);

        // W0-1 follow-up (R12 user decision) — CGST Act §10(4): a composition dealer "shall not collect any tax from
        // the recipient on supplies made by him". An outward supply of his that CARRIES posted forward GST is
        // therefore not a document-classification problem to be explained at print time; it is an entry that should
        // never have been accepted. Refused here so it cannot enter the books at all.
        //
        // ⚠️ ENTRY PATHS ONLY, and that is load-bearing. A guard that refuses to POST is not a guard that refuses to
        // LOAD: SqliteCompanyStore.Load re-posts every stored voucher through this engine, so applying this rule
        // unconditionally would make a book that ALREADY contains the shape unopenable — strictly worse than the
        // print-path refusal it supplements. The two rehydration paths (SqliteCompanyStore.Load and company import)
        // are exactly the two that pass CostAllocationStrictness.Legacy, and that enum's own contract already says so
        // ("Rehydration only … never used when a user enters or alters a voucher"), so it is the discriminator here
        // too. An already-posted anomalous voucher keeps loading, reading and printing — as the plain Dr/Cr voucher,
        // which states every posted leg exactly (VoucherPrintProjector.IsTaxInvoice).
        if (costAllocationStrictness == CostAllocationStrictness.Strict &&
            GstReportSupport.IsCompositionSupplyCarryingForwardTax(c, v))
            throw new InvalidVoucherException(
                "A composition dealer may not collect GST. This outward supply posts forward Output CGST/SGST/IGST " +
                "or Compensation Cess, which CGST Act section 10(4) forbids (\"shall not collect any tax from the " +
                "recipient on supplies made by him\") and section 31(3)(c) answers by requiring a bill of supply " +
                "instead of a tax invoice. Remove the tax legs, or change the Registration Type under F11 GST.");
    }

    /// <summary>
    /// The POS tender-split invariants (catalog §11; Phase 6 slice 7 RQ-39..RQ-42; TOP RISK #6). Entered only when
    /// <see cref="Voucher.HasPosTenders"/>. Enforces, in order:
    /// <list type="bullet">
    ///   <item><b>Base + flag</b>: POS tenders are valid only on a <b>Sales</b> voucher type flagged
    ///     <see cref="VoucherType.UseForPos"/>.</item>
    ///   <item><b>Reconciliation</b>: Σ tender.Amount == the voucher's total debit (the bill total) — the tenders
    ///     ARE the debit side, so every debit line is a tender share and they foot to the bill (RQ-40).</item>
    ///   <item><b>Grouping</b>: each tender ledger sits under its required group (Gift → Sundry Debtors,
    ///     Card/Cheque → Bank, Cash → Cash-in-Hand) — load-bearing (RQ-41).</item>
    ///   <item><b>Cash change</b>: every Cash tender carries Tendered ≥ Amount and Change == Tendered − Amount
    ///     (≥ 0, informational — never posted); a non-cash tender carries no cash-only fields (RQ-39).</item>
    /// </list>
    /// Throws <see cref="InvalidVoucherException"/> on the first violation, so an unbalanced or misgrouped tender
    /// split can never persist. <see cref="EnsureItemInvoiceValid"/> and the balance invariant continue to pass.
    /// </summary>
    public static void EnsurePosTendersValid(Voucher v, Company c)
    {
        var type = c.FindVoucherType(v.TypeId)!; // referential integrity already checked
        if (type.BaseType != VoucherBaseType.Sales || !type.UseForPos)
            throw new InvalidVoucherException(
                $"POS tenders are only valid on a POS-flagged Sales voucher type; '{type.Name}' is a " +
                $"{type.BaseType}{(type.BaseType == VoucherBaseType.Sales ? " without 'Use for POS invoicing'" : "")}.");

        // Reconciliation: Σ tender == total debit (the bill total). Because the tenders replace the single customer
        // debit, this simultaneously proves the debit side is entirely tender shares that foot to the bill (RQ-40).
        Services.PosTenderService.EnsureBalanced(v.TotalDebit, v.PosTenders);

        // Grouping (load-bearing, RQ-41).
        Services.PosTenderService.EnsureGrouping(c, v.PosTenders);

        // Cash change consistency (RQ-39): a Cash tender's tendered ≥ amount and change == tendered − amount; a
        // non-cash tender must not carry cash-only fields.
        foreach (var t in v.PosTenders)
        {
            if (t.Type == PosTenderType.Cash)
            {
                if (t.Tendered is not { } tendered)
                    throw new InvalidVoucherException("A POS Cash tender must record the Cash Tendered amount.");
                if (tendered < t.Amount)
                    throw new InvalidVoucherException(
                        $"POS Cash tendered {tendered} is less than the cash payable {t.Amount}.");
                var expectedChange = tendered - t.Amount;
                if (t.Change is not { } change || change != expectedChange)
                    throw new InvalidVoucherException(
                        $"POS Cash change must equal tendered − payable ({expectedChange}); got " +
                        $"{(t.Change is { } ch ? ch.ToString() : "null")}.");
            }
            else if (t.Tendered is not null || t.Change is not null)
            {
                throw new InvalidVoucherException(
                    $"A POS {t.Type} tender must not carry Cash Tendered / Change (those are Cash-only).");
            }
        }
    }

    /// <summary>
    /// The item-invoice pairing invariant (catalog §10; phase3-inventory-requirements RQ-16/RQ-17; slice 3.3b).
    /// Item lines are permitted only on a Purchase or Sales voucher whose type moves stock, every line must
    /// reference a known stock item and godown, and — critically — the item lines' <b>total value</b>
    /// (Σ qty × rate) must reconcile with the voucher's <b>stock accounting amount</b> so the inward/outward is
    /// always backed by an accounting posting (no unbacked stock, no phantom profit). The exact rule:
    /// <list type="bullet">
    ///   <item><b>Purchase</b>: Σ item-line value == Σ of the <b>debit</b>-line amounts posted to ledgers under
    ///     <b>Purchase Accounts</b> or <b>Stock-in-Hand</b> (the stock-in leg).</item>
    ///   <item><b>Sales</b>: Σ item-line value == Σ of the <b>credit</b>-line amounts posted to ledgers under
    ///     <b>Sales Accounts</b> (the sales leg).</item>
    /// </list>
    /// A mismatch, item lines on a non-Purchase/Sales (or non-stock-affecting) type, or an unknown item/godown
    /// reference all throw a clean <see cref="InvalidVoucherException"/>.
    /// </summary>
    public static void EnsureItemInvoiceValid(Voucher v, Company c)
    {
        var type = c.FindVoucherType(v.TypeId)!; // referential integrity already checked above
        var isPurchase = type.BaseType == VoucherBaseType.Purchase;
        var isSales = type.BaseType == VoucherBaseType.Sales;
        if (!isPurchase && !isSales)
            throw new InvalidVoucherException(
                $"Item-invoice stock lines are only valid on a Purchase or Sales voucher; '{type.Name}' is neither.");

        // The implied direction: Purchase ⇒ inward, Sales ⇒ outward. Every item line must already carry it
        // (the posting service stamps it), so the on-hand engine reads the direction directly.
        var expectedDir = isPurchase ? StockDirection.Inward : StockDirection.Outward;
        foreach (var line in v.InventoryLines)
        {
            var item = c.FindStockItem(line.StockItemId);
            if (item is null)
                throw new InvalidVoucherException($"Item-invoice line references unknown stock item {line.StockItemId}.");
            if (c.FindGodown(line.GodownId) is null)
                throw new InvalidVoucherException($"Item-invoice line references unknown godown {line.GodownId}.");
            // WI-10 Gap 2: a line unit must exist AND reduce to the item's own base unit, because the stock
            // engine normalises the quantity through Unit.QuantityInBaseMeasure before it accumulates on hand.
            // Without this gate "1 Kg" of a Nos-measured item would silently scale on-hand by an unrelated
            // factor — and the value leg would still foot, so nothing else would catch it. (Mirrors the same
            // guard InventoryPostingService applies to pure-stock allocations.)
            if (line.UnitId is { } lineUnitId)
            {
                var unit = c.FindUnit(lineUnitId);
                if (unit is null)
                    throw new InvalidVoucherException($"Item-invoice line references unknown unit {lineUnitId}.");
                if (unit.BaseMeasureUnitId != item.BaseUnitId)
                {
                    var itemUnit = c.FindUnit(item.BaseUnitId)?.Symbol ?? item.BaseUnitId.ToString();
                    throw new InvalidVoucherException(
                        $"Item-invoice line for '{item.Name}' states its quantity in '{unit.Symbol}', which does " +
                        $"not reduce to the item's base unit '{itemUnit}'.");
                }
            }
            if (line.Direction != expectedDir)
                throw new InvalidVoucherException(
                    $"Item-invoice line direction {line.Direction} does not match the '{type.Name}' nature " +
                    $"(expected {expectedDir}).");
            // Zero-value guard (Phase 6 slice 4 RQ-21, ER-7 surgical relaxation). A zero-rate / zero-value line
            // normally injects unbacked stock (phantom on-hand / phantom profit) that slips through the pairing
            // check, so it stays rejected — UNLESS this Sales/Purchase type has "Allow zero-valued transactions"
            // on, in which case a ₹0 free-goods line is a legitimate entry (it moves stock but posts ₹0, and the
            // pairing invariant still balances ₹0 against ₹0). The relaxation is scoped to zero-valued-enabled
            // types only; a normal invoice still rejects a fat-finger ₹0 line, and a positive-value line is never
            // affected.
            if (!type.AllowZeroValuedTransactions && (line.Rate.Amount <= 0m || line.Value.Amount <= 0m))
                throw new InvalidVoucherException(
                    "Item-invoice line rate must be greater than zero (a zero-rate line would move stock with no " +
                    "accounting backing).");
        }

        // Σ of the accounting stock leg: Purchase = debit lines to Purchase Accounts / Stock-in-Hand ledgers;
        // Sales = credit lines to Sales Accounts ledgers. NOTE (Phase 7 slice 2 — TDS carve-out): a withholding
        // purchase books Dr Purchases GROSS / Cr Party NET / Cr TDS Payable — the stock leg (Purchases) is still
        // the GROSS debit, so it equals the item-lines value; the reduced party leg and the TDS Payable (Duties &
        // Taxes) credit are BOTH outside this stock-leg sum (TDS Payable via IsDutiesAndTaxesLedger, exactly like
        // GST), so the pairing foots unchanged and the balance invariant (Σ Dr == Σ Cr) guards net + withheld == gross.
        var wantSide = isPurchase ? DrCr.Debit : DrCr.Credit;
        var accountingStockAmount = 0m;
        foreach (var line in v.Lines)
        {
            if (line.Side != wantSide) continue;
            var ledger = c.FindLedger(line.LedgerId);
            if (ledger is null) continue; // already validated above
            if (IsStockLegLedger(ledger, c, isPurchase))
                accountingStockAmount += line.Amount.Amount;
        }

        var itemLinesValue = v.InventoryLinesValue.Amount;
        if (accountingStockAmount != itemLinesValue)
        {
            var leg = isPurchase ? "Purchases / Stock-in-Hand (debit)" : "Sales (credit)";
            throw new InvalidVoucherException(
                $"Item-invoice pairing: the item lines total ₹{itemLinesValue:0.00} (Σ qty × rate) does not equal " +
                $"the voucher's {leg} accounting amount ₹{accountingStockAmount:0.00}. The stock leg must be backed " +
                "by an equal accounting posting so no unbacked stock is created.");
        }
    }

    /// <summary>
    /// Whether a ledger is the accounting "stock leg" for an item-invoice: for a Purchase, a ledger under
    /// <b>Purchase Accounts</b> (primary ancestor) or under <b>Stock-in-Hand</b>; for a Sales, a ledger under
    /// <b>Sales Accounts</b> (primary ancestor).
    /// </summary>
    private static bool IsStockLegLedger(Domain.Ledger ledger, Company c, bool isPurchase)
    {
        var group = c.FindGroup(ledger.GroupId);
        if (group is null) return false;
        if (isPurchase)
        {
            if (ClassificationRules.IsStockInHandLedger(ledger, c)) return true;
            return string.Equals(ClassificationRules.PrimaryAncestorOf(group, c).Name, "Purchase Accounts",
                StringComparison.OrdinalIgnoreCase);
        }
        return string.Equals(ClassificationRules.PrimaryAncestorOf(group, c).Name, "Sales Accounts",
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// §5 bill-wise integrity for one line: allocations are only permitted on a bill-by-bill ledger,
    /// and their magnitudes must <b>sum exactly to the line amount</b> ("split"). Throws otherwise.
    /// </summary>
    public static void EnsureBillAllocationsValid(Domain.EntryLine line, Domain.Ledger ledger)
    {
        if (!ledger.MaintainBillByBill)
            throw new InvalidVoucherException(
                $"Ledger '{ledger.Name}' does not maintain balances bill-by-bill; it cannot carry bill allocations.");

        if (line.BillAllocationTotal != line.Amount)
            throw new InvalidVoucherException(
                $"Bill allocations on the line for '{ledger.Name}' sum to {line.BillAllocationTotal} " +
                $"but the line amount is {line.Amount}; they must be equal (split).");
    }

    /// <summary>
    /// §6 cost-centre integrity for one line: cost allocations are only permitted on a ledger with cost
    /// centres applicable, every allocation must reference a known category and a known centre that
    /// belongs to that category, and — <b>within each cost category independently</b> — their magnitudes
    /// must sum exactly to the line amount. Throws otherwise.
    /// <para><b>Parallel sets, not a partition (spec §4.2 rule C-27; gap G-2).</b> Cost categories are
    /// independent allocation <i>axes</i>. The corpus's worked example allocates one ₹5,000 travelling
    /// expense in full to Branch → Kolkata <i>and</i> in full to Department → Marketing <i>and</i> in full
    /// to Executive → Sales Executive 1 (TALLY PRIME STUDY GUIDE pp.101–102). Summing across categories and
    /// comparing that to the line — which this validator used to do — rejects the reference product's own
    /// example and makes multi-category cost accounting impossible. Within one axis the split behaviour is
    /// unchanged: ₹5,000 may still be split ₹3,000 Kolkata + ₹2,000 Delhi.</para>
    /// </summary>
    public static void EnsureCostAllocationsValid(Domain.EntryLine line, Domain.Ledger ledger, Company c)
        => EnsureCostAllocationsValid(line, ledger, c, CostAllocationStrictness.Strict);

    /// <summary>
    /// <see cref="EnsureCostAllocationsValid(Domain.EntryLine, Domain.Ledger, Company)"/> with an explicit
    /// invariant (see <see cref="CostAllocationStrictness"/>).
    /// </summary>
    public static void EnsureCostAllocationsValid(
        Domain.EntryLine line, Domain.Ledger ledger, Company c, CostAllocationStrictness strictness)
    {
        if (!ClassificationRules.CostCentresApplicableFor(ledger, c))
            throw new InvalidVoucherException(
                $"Ledger '{ledger.Name}' does not have cost centres applicable; it cannot carry cost allocations.");

        foreach (var a in line.CostAllocations)
        {
            var category = c.FindCostCategory(a.CategoryId)
                ?? throw new InvalidVoucherException(
                    $"Cost allocation on the line for '{ledger.Name}' references unknown cost category {a.CategoryId}.");
            var centre = c.FindCostCentre(a.CentreId)
                ?? throw new InvalidVoucherException(
                    $"Cost allocation on the line for '{ledger.Name}' references unknown cost centre {a.CentreId}.");
            if (centre.CategoryId != category.Id)
                throw new InvalidVoucherException(
                    $"Cost centre '{centre.Name}' does not belong to category '{category.Name}'.");
        }

        // The per-category (per-axis) invariant: each category the line uses must itself total the line.
        // Reported on the FIRST short axis in first-appearance order, so the message is deterministic.
        Guid? shortCategoryId = null;
        var shortAllocated = Money.Zero;
        foreach (var categoryId in line.CostAllocationCategoryIds)
        {
            var allocated = line.CostAllocationTotalFor(categoryId);
            if (allocated == line.Amount) continue;
            shortCategoryId = categoryId;
            shortAllocated = allocated;
            break;
        }

        if (shortCategoryId is null) return;   // every axis foots — valid under both rules

        // Rehydration tolerance: books written under the superseded partition rule "split" one amount
        // ACROSS axes, so no single axis foots but the cross-axis sum does. Those vouchers were legitimately
        // accepted once and must keep loading — SqliteCompanyStore.Load re-posts every stored voucher
        // through this engine, so rejecting them would make the whole company unopenable. Never granted on
        // an entry path; CostAllocationDiagnostics lists what a human still needs to re-allocate.
        if (strictness == CostAllocationStrictness.Legacy && line.CostAllocationTotal == line.Amount)
            return;

        // C-27: the corpus's own failure text names the ledger AND the category.
        var shortCategoryName = c.FindCostCategory(shortCategoryId.Value)?.Name ?? shortCategoryId.Value.ToString();
        throw new InvalidVoucherException(
            $"Cost allocations on the line for '{ledger.Name}' total {shortAllocated} under cost category " +
            $"'{shortCategoryName}' but the line amount is {line.Amount}; each cost category must be " +
            "allocated in full (categories are parallel axes, not a split of the line).");
    }

    /// <summary>
    /// §8 banking integrity for one line: a bank allocation is only permitted on a bank ledger
    /// (a ledger under Bank Accounts / Bank OD A/c). The allocation carries no amount of its own — it
    /// annotates the whole line — so there is no split-sum check; it is enough that the ledger is a bank.
    /// Throws otherwise.
    /// </summary>
    public static void EnsureBankAllocationValid(Domain.EntryLine line, Domain.Ledger ledger, Company c)
    {
        if (!ClassificationRules.IsBankLedger(ledger, c))
            throw new InvalidVoucherException(
                $"Ledger '{ledger.Name}' is not a bank account; it cannot carry a bank allocation.");
    }

    /// <summary>One paisa, the coarsest base-currency unit — the tolerance a rounded forex base may differ by.</summary>
    private const decimal OnePaisa = 0.01m;

    /// <summary>
    /// Multi-currency integrity for one line (catalog §2/§20): the forex detail must reference a known
    /// currency, and the line's base <see cref="Domain.EntryLine.Amount"/> must equal the
    /// <b>paisa-rounded</b> <c>ForexAmount × Rate</c> (<see cref="ForexInfo.BaseValue"/>), so the base ledger
    /// math is unchanged. Because a non-round rate makes the raw product carry a sub-paisa tail, the base is
    /// the product snapped to the paisa; a base off by <b>more than a paisa</b> (or an unknown currency) is
    /// rejected. Throws otherwise.
    /// </summary>
    public static void EnsureForexValid(Domain.EntryLine line, Domain.Ledger ledger, Company c)
    {
        var forex = line.Forex!;
        if (c.FindCurrency(forex.CurrencyId) is null)
            throw new InvalidVoucherException(
                $"Forex on the line for '{ledger.Name}' references unknown currency {forex.CurrencyId}.");

        // BaseValue is the paisa-rounded forex × rate; the line's base must match it to within one paisa,
        // so a base that carries the unrounded sub-paisa tail (or the rounded value) both pass, but a base
        // that is genuinely wrong (off by more than a paisa) is rejected.
        var expected = forex.BaseValue; // paisa-exact
        if (Math.Abs((line.Amount - expected).Amount) > OnePaisa)
            throw new InvalidVoucherException(
                $"Forex on the line for '{ledger.Name}': {forex.ForexAmount} × {forex.Rate} ≈ {expected} " +
                $"(paisa-rounded) does not equal the base line amount {line.Amount}; the base amount must be " +
                $"forex × rate rounded to the paisa.");
    }
}
