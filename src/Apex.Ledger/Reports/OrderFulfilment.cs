using Apex.Ledger.Domain;

namespace Apex.Ledger.Reports;

/// <summary>
/// Retires purchase and sales orders against the movements that fulfil them (Phase 10.10 / WF-8).
///
/// <para><b>The defect this exists to remove.</b> Before WF-8 an order in this product was <b>never retired</b>:
/// <see cref="InventoryRegisters.BuildOrders"/> hard-coded <c>FulfilledQuantity: 0m,
/// OutstandingQuantity: line.Quantity</c> and <see cref="InventoryVoucher"/> carried no fulfilment or closure
/// state at all. A sales order delivered in full still reported its whole quantity outstanding for ever, so the
/// error was the entire delivered-order history and grew without bound. WF-7 then nets Sales Orders Due into
/// Nett Available, at which point that stale figure stops being a wrong display column and becomes the shortfall
/// arithmetic behind a real supplier purchase order.</para>
///
/// <para><b>What is ported, and what could not be.</b> The movement-scanning predicate set is the one
/// <c>JobWorkReports.FulfilledQuantity</c> already proves: skip a cancelled movement, pin the fulfilling voucher
/// to its <b>base type</b> (not merely to a line direction), match the stock item, match the direction, and sum
/// <c>QuantityInBase</c>. What could <b>not</b> be ported is JobWork's <i>attribution</i>: JobWork names the
/// order it fulfils in <see cref="InventoryVoucher.OrderLinks"/>, and that link is <b>unreachable for purchase
/// and sales orders</b> — no factory on <see cref="InventoryVoucher"/> other than
/// <see cref="InventoryVoucher.MaterialMovement"/> accepts <c>orderLinks</c> (every other one passes
/// <c>null</c>), and the backing table's foreign key (<c>material_order_links.job_work_order_id REFERENCES
/// job_work_orders(id)</c>, with <c>PRAGMA foreign_keys = ON</c>) rejects any target that is not a Job Work
/// order. Making the link storable is a schema change, and Phase 10.10 binds v51/v52/v53 to WF-1/WF-2/WF-3 and
/// reserves only v54 for this — a version that cannot be built before the three ahead of it exist. Attribution
/// is therefore <b>derived</b>, which is the route <c>plan.md</c> WF-8 states as the plan ("schema-clean if the
/// match is derived").</para>
///
/// <para><b>🔴 THE ATTRIBUTION RULE IS A STATED ENGINEERING CHOICE, NOT A SOURCED TALLY BEHAVIOUR (R7).</b>
/// TallyPrime retires an order through an explicit order/tracking number captured on the note; this product has
/// no such field on any master or line, and no UI to capture one. In its absence the rule is:
/// <list type="number">
///   <item>the cohort is the pair <b>(PartyId, StockItemId)</b> — a movement can only ever retire an order for
///     the <b>same stock item</b> raised on the <b>same counterparty</b>;</item>
///   <item>inside that cohort a movement retires the <b>earliest still-open order line</b> — first-in,
///     first-out — and never an order dated after the movement;</item>
///   <item><b>"no party" is a cohort of its own, not a wildcard</b> — see the next paragraph.</item>
/// </list>
/// It is recorded here rather than in a doc because the rule decides a figure. Owed to the post-merge
/// documentation slice: its own row in <c>docs/invented-vs-cloned.md</c>, and a user go/no-go on whether a
/// derived match may retire an order at all.</para>
///
/// <para><b>🔴 WHAT HAPPENS WHEN A NOTE NAMES NO PARTY — the rule, and why it is this one.</b> The party field
/// is optional on both an order and a note ("(none)" in the picker), so <c>null</c> is a real, reachable value
/// on either side. It is treated as <b>a counterparty of its own</b>, matched by strict equality: a blank note
/// retires only blank orders, and a note naming a party never retires a blank order.
/// <list type="bullet">
///   <item><b>It is not a silent fallback.</b> The rejected alternative — treat <c>null</c> as a wildcard
///     matching anything — sounds conservative and is not: because every note in this product used to be blank,
///     the wildcard always fired and the cohort was <b>item-wide in every real book</b>, so a delivery to one
///     customer routinely retired a different customer's order. That is a mis-stated figure, invented by the
///     engine, and it is what this revision deletes.</item>
///   <item><b>A book that names nobody still works.</b> If the operator leaves the picker blank on the orders
///     <i>and</i> the notes — the ordinary small-book case — both sit in the <c>(null, item)</c> cohort and FIFO
///     retires them normally. Blankness costs nothing as long as it is consistent.</item>
///   <item><b>The residual error of THIS RULE is bounded and is never worse than what shipped before.</b> The one
///     case that under-matches is the MIXED book: an order raised on a named party, fulfilled by a note left
///     blank. That order stays open — which is precisely the pre-WF-8 figure for that order, so the
///     <b>blank-party rule</b> is a strict improvement on every book and a regression on none. Unlike before, the
///     operator now has a control that fixes it: name the party on the note.
///     <para>🔴 <b>That sentence is scoped to the blank-party rule and is NOT a claim about this class.</b> Two
///     other rules documented below ARE a regression on a book each one names — the <b>cancelled-order cohort
///     exclusion</b> and the <b>duplicate-document per-order spill</b> — and each states its own direction where
///     it is stated. An earlier revision left this sentence unqualified and a reader took it for a class-wide
///     safety guarantee.</para></item>
/// </list>
/// The direction of that residual is stated rather than left to be discovered: an unmatched <b>purchase</b>
/// order overstates Purc Orders Pending and therefore Nett Available (WF-7), understating shortfall; an
/// unmatched <b>sales</b> order overstates Sales Orders Due, overstating shortfall. Guessing the party would
/// replace a bounded, explainable gap with an unbounded, invisible misattribution.</para>
///
/// <para><b>🔴 HOW A NOTE CAME TO CARRY A PARTY AT ALL — do not undo this.</b> Until Phase 10.10 the cohort
/// above was unreachable: <c>InventoryVoucherEntryViewModel.BuildMovementNote</c> — the only
/// <c>new InventoryVoucher(</c> in the Desktop project — omitted <c>partyId</c>, and the picker was bound
/// <c>IsVisible="{Binding IsOrder}"</c>, which is false for a note. An order sat in cohort
/// <c>(customer, item)</c>, its delivery note in <c>(null, item)</c>, the lookup missed, and the register
/// reported the full ordered quantity outstanding — <b>byte-identical to the pre-WF-8 defect this class exists
/// to delete</b>. The fix was made at the root (R12, 2026-08-07): the note now passes the party it captures and
/// the picker is shown for movement notes (<c>ShowsParty</c>). It is a <b>fidelity fix in its own right</b> —
/// TallyPrime's Receipt Note and Delivery Note both take "Party's A/c Name" [CORPUS-BOOK pp.71, 77] and its
/// Rejection In/Out both take a "Ledger Account" naming the customer/supplier [CORPUS-BOOK pp.51, 53] — and it
/// posts no accounting entry, matching the corpus's own note that a delivery/receipt note "will not reflect in
/// Ledger/party balance ... will only affect your stock" [CORPUS-BOOK pp.70, 76]. A party is also round-tripped
/// by <c>CanonicalMapper</c>/<c>CanonicalXml</c> and by <c>inventory_vouchers.party_id</c>, which already existed
/// on the header for every inventory voucher, so <b>no schema change was needed</b>.</para>
///
/// <para><b>🔴 STOCK LEAVES AND ENTERS THE BUILDING THROUGH MORE THAN ONE DOOR — the door census, and why the
/// fulfilling document is an ALLOW-LIST rather than a single base type.</b> This class shipped its first
/// revision pinned to exactly one fulfilling <see cref="VoucherBaseType"/> per arm, which saw the Delivery Note
/// and the Receipt Note and nothing else. A shop that <b>invoices its goods directly</b> — the ordinary retail
/// and trading path — moves its stock through <see cref="Voucher.InventoryLines"/> via
/// <c>Services.ItemInvoiceStock</c> and <b>never posts an <see cref="InventoryVoucher"/> at all</b>, so its
/// orders were never retired: byte-identical to the pre-WF-8 defect above, for the majority of real books. The
/// full census of stock-moving surfaces, taken from <see cref="VoucherEffects.AffectsStock"/> plus the
/// item-invoice bridge, is:
/// <list type="number">
///   <item><b>Receipt Note</b> — inward, pure stock. <b>FULFILS a purchase order.</b></item>
///   <item><b>Delivery Note</b> — outward, pure stock. <b>FULFILS a sales order.</b></item>
///   <item><b>Purchase item-invoice</b> — inward, via <see cref="Voucher.InventoryLines"/>. <b>FULFILS a
///     purchase order.</b></item>
///   <item><b>Sales item-invoice, INCLUDING POS</b> — outward, via <see cref="Voucher.InventoryLines"/>. A POS
///     bill is <b>not a separate door</b>: <see cref="VoucherType.IsPosSales"/> is
///     <c>BaseType == Sales &amp;&amp; UseForPos</c>, and the tender split changes only the voucher's DEBIT
///     side. <b>FULFILS a sales order.</b></item>
///   <item><b>Rejection In</b> — inward (a customer returns to us). Reverses a delivery. <b>Neither retires nor
///     un-retires</b> — see the rejection paragraph below.</item>
///   <item><b>Rejection Out</b> — outward (we return to a supplier). Reverses a receipt. <b>Neither retires nor
///     un-retires.</b></item>
///   <item><b>Stock Journal source</b> — outward. An internal transfer or a Manufacturing Journal's consumption
///     (a Manufacturing Journal is a Stock-Journal type flagged <see cref="VoucherType.IsManufacturingJournal"/>,
///     not a base type of its own). Nothing left the building. <b>Never retires.</b></item>
///   <item><b>Stock Journal destination</b> — inward. Same. <b>Never retires.</b></item>
///   <item><b>Material Out</b> — outward, Job Work. Its orders are Job-Work orders, handled by
///     <c>JobWorkReports</c>. <b>Never retires a purchase or sales order.</b></item>
///   <item><b>Material In</b> — carries an outward source and/or an inward destination, Job Work. Same.</item>
///   <item><b>Physical Stock</b> — not a linear movement at all: it <i>sets</i> on-hand to a counted quantity
///     and is surfaced separately by <c>InventoryLedger.PhysicalStockAdjustments</c>. <b>Never retires.</b></item>
///   <item><b>Credit Note / Debit Note</b> — <b>cannot carry stock lines in this product</b>:
///     <c>VoucherValidator.EnsureItemInvoiceValid</c> and <c>ItemInvoiceStock.Counts</c> both admit only
///     Purchase/Sales base types, so a stock-bearing sales return has no door here. That is a divergence from
///     TallyPrime, which threads a Credit Note into the order chain
///     [CORPUS-TALLY-BOOK(719244897) p.18]; it belongs to the register, not to this class.</item>
/// </list>
/// Four of those twelve fulfil an order. <see cref="IsFulfillingDoor"/> is written as a <b>closed allow-list</b>
/// precisely so a thirteenth surface added later defaults to CLOSED and has to be admitted deliberately, rather
/// than leaking in because it happens to point the right way.</para>
///
/// <para><b>🔴 R7 — the invoice door is CORPUS-SOURCED, unlike the attribution rule above.</b> TallyPrime's own
/// order cycle shows the <b>Purchase Voucher (F9)</b> carrying "<c>Tracking No : 1  Order No : 1</c>" against
/// Purchase Order 1 [CORPUS-TALLY-BOOK(719244897) p.15] and the <b>Sales Voucher (F8)</b> doing the same against
/// Sales Order 1 [p.18]. An invoice IS a fulfilment document there, on both arms — so the symmetric answer ("a
/// purchase invoice retires a purchase order, exactly as a sales invoice retires a sales order") is the sourced
/// one, and an asymmetry would have been the invention.</para>
///
/// <para><b>🔴 DOUBLE-COUNTING — what stops it, and the one thing that does NOT.</b> Opening extra doors adds
/// CANDIDATE movements; it can never inflate a single order line, because the FIFO walk caps every allocation at
/// that line's own remaining quantity (<c>take = Math.Min(open.Remaining, remaining)</c>). Once a line reaches
/// zero, no later movement from any door can take against it again — Fulfilled stops at Ordered and Outstanding
/// stops at zero. That is a per-line structural guarantee and it is what makes a multi-door design safe at all.
/// <b>Which part of it actually carries the weight was measured, not assumed:</b> when the second document
/// exactly equals the order line, three things stop it independently (the cursor has already passed the
/// exhausted line, the <c>open.Remaining &lt;= 0m</c> skip, and <c>Math.Min(0, q) == 0</c>) — so replacing the
/// cap with <c>take = remaining</c> leaves that case still passing. The cap is load-bearing only when the second
/// document EXCEEDS the line's remainder — a partial note then a full invoice, which is the corpus's own shape
/// (Receipt Note 10, Rejection Out 2, Purchase Voucher 8) [CORPUS-TALLY-BOOK(719244897) pp.14-15]. Both shapes
/// are locked, and the test says which one bites.
/// <list type="bullet">
///   <item><b>What is NOT claimed:</b> the engine does not de-duplicate the STOCK. A book that raises both a
///     Delivery Note and an item invoice for the same goods really has moved them twice — on-hand drops twice,
///     and <c>InventoryPostingService.DetectNegativeStock</c> will say so. TallyPrime avoids that with the
///     <b>Tracking Number</b>: an invoice raised against a note takes its quantities from the tracked note
///     rather than moving stock again ("On the Basis of tracking PO/OO1 &amp; RN/OO1, it takes the items with the
///     Quantity, Rate &amp; Amount automatically" [CORPUS-GST-NOTES(703679456) p.24]; the sales twin at p.31).
///     <b>This product has no tracking-number field on any master, line or voucher</b> — the same absence that
///     forced the derived attribution above.</item>
///   <item><b>Why fulfilment MIRRORS the stock engine instead of guessing.</b> A duplicate document and a
///     genuine second shipment are indistinguishable without that link; a heuristic ("same party + item +
///     quantity ⇒ one shipment") would silently swallow real second shipments. De-duplicating fulfilment while
///     the stock engine kept both movements would also make the item-level error <c>D</c> instead of
///     <c>D − A</c> — strictly larger, in the same direction, for more work.</item>
///   <item><b>🔴 THE ERROR DIRECTION IS PER ARM, AND ONE ARM IS UNSAFE.</b> An earlier revision of this bullet
///     derived the direction for the OUTWARD arm only and then stated it unqualified — "comes out <c>(D − A)</c>
///     LOW — never high … can never UNDER-state it". <b>That is false on the purchase arm, and it was refuted by
///     this paragraph's own worked example</b>, which is the corpus PURCHASE cycle cited two bullets up (Receipt
///     Note 10, Rejection Out 2, Purchase Voucher 8). Both arms, separately, for a duplicated quantity <c>D</c>
///     of which <c>A ≤ D</c> is absorbed by open order lines:
///     <list type="bullet">
///       <item><b>OUTWARD duplicate</b> (Delivery Note + sales invoice): closing stock is <c>D</c> LOW — the
///         goods went OUT twice — and Sales Orders Due is <c>A</c> LOW, so
///         <c>NettAvailable = closing + pendingPO − soDue</c> is <c>(D − A)</c> <b>LOW</b> ⇒ shortfall
///         <b>OVER-stated</b> ⇒ re-order more than needed. <b>Safe.</b></item>
///       <item><b>INWARD duplicate</b> (Receipt Note + purchase invoice): closing stock is <c>D</c> <b>HIGH</b>
///         — the goods came IN twice — and Purc Orders Pending is <c>A</c> LOW, so Nett Available is
///         <c>(D − A)</c> <b>HIGH</b> ⇒ shortfall <b>UNDER-stated</b> ⇒ the buyer under-orders and a customer is
///         left unserved. <b>UNSAFE</b> — the same direction as the un-netted Rejection In below, and it is
///         handed to the user as an open question beside it.</item>
///     </list>
///     Measured rather than reasoned:
///     <c>A_duplicate_purchase_document_pushes_nett_available_high_and_under_states_shortfall</c> asserts
///     <see cref="ReorderStatusRow.NettAvailable"/> and <see cref="ReorderStatusRow.Shortfall"/> on the corpus's
///     own PO → Receipt Note → Purchase Voucher book, so the unsafe arm is pinned by a test and not by
///     prose.</item>
///   <item><b>🔴 AND THE PER-ORDER COLUMN HAS NO COMPENSATING ERROR AT ALL.</b> Everything above is about the
///     item AGGREGATE that WF-7 nets. The Order Register's per-ORDER Fulfilled/Outstanding columns are not
///     covered by it: the surplus of a duplicate document spills by FIFO onto the next open order in the cohort
///     and can retire, <b>in full</b>, a genuinely open order that <b>nothing has shipped against</b> — a row
///     reading "Outstanding 0" where the truth is the whole ordered quantity, on either arm. Before this class
///     existed that row read the ordered quantity (accidentally right, because nothing was ever retired), so
///     this specific row is a <b>REGRESSION</b>, stated as one rather than blessed. Pinned with its components
///     by <c>A_second_document_for_the_same_goods_spills_by_fifo_exactly_as_an_over_delivery_does</c> (sales)
///     and <c>A_duplicate_purchase_invoice_retires_a_second_open_order_nothing_was_received_against</c>
///     (purchase). It is not fixable without the Tracking Number: bounding a movement to the order lines an
///     earlier movement already touched is the "same party + item + quantity ⇒ one shipment" guess under
///     another name, and it would silently swallow a genuine second shipment. <b>Owed to the user as a
///     go/no-go, with the blank-party rule.</b></item>
/// </list></para>
///
/// <para><b>🔴 A CANCELLED ORDER IS EXCLUDED FROM THE COHORT — why it is kept, and the book where it costs.</b>
/// <see cref="CountsAsOf"/> drops a cancelled order, so its FIFO capacity disappears with it.
/// <list type="bullet">
///   <item><b>Why the exclusion is right on the ordinary book.</b> The ORDINARY reason to cancel an order is
///     that it will never be fulfilled — the customer changed their mind, the supplier could not supply. On
///     that book, and it is by far the common one, exclusion gives the correct answer: a later movement retires
///     the live order it was actually for. Keeping the cancelled line in the cohort as an absorber would make
///     it soak that movement and leave a genuinely delivered order reading outstanding for ever.</item>
///   <item><b>Where it costs — DELIVER-THEN-CANCEL.</b> The mirror book ships goods against an order that is
///     cancelled only afterwards. <see cref="InventoryVoucher"/> carries <c>Cancelled</c> as a <b>boolean with
///     no cancellation date</b>, so the engine cannot tell the two books apart. The movement finds the cancelled
///     line gone, walks on, and retires the next live order of the same (party, item) — an order nothing was
///     ever received or delivered against, which then prints Outstanding 0.</item>
///   <item><b>Direction, stated rather than left to be discovered.</b> SALES arm: Sales Orders Due comes out
///     LOW ⇒ Nett Available HIGH ⇒ shortfall <b>UNDER-stated</b> ⇒ a customer left unserved (<b>UNSAFE</b>).
///     PURCHASE arm: Purc Orders Pending LOW ⇒ Nett Available LOW ⇒ shortfall OVER-stated (safe). On that book
///     it is a <b>REGRESSION</b> versus the pre-WF-8 raw sum, which also skipped cancelled orders and therefore
///     happened to be right there — which is why the blank-party bullet's improvement claim is scoped to the
///     blank-party rule and does not reach here. Both arms are pinned, with their components, by
///     <c>A_movement_booked_against_a_cancelled_order_retires_the_live_order_behind_it_a_stated_residual</c>.
///     <b>Owed to the user as a go/no-go</b>: the only alternative available without a cancellation date (keep
///     cancelled lines as non-reported absorbers) does not remove the error — it swaps which of the two books
///     is wrong and inverts the direction on both arms.</item>
/// </list></para>
///
/// <para><b>🔴 REJECTIONS — the decision, its reason, and its cost.</b> Rejection In and Rejection Out are
/// recognised as stock doors and admitted to NEITHER arm: a rejection retires nothing, and it does not re-open
/// what a note or an invoice retired. <b>This is a known divergence, not an oversight</b> — TallyPrime threads
/// them into the chain, its Rejection Out and Rejection In both carrying "<c>Tracking No : 1  Order No : 1</c>"
/// [CORPUS-TALLY-BOOK(719244897) pp.14, 18]. Reproducing it needs two things this slice does not have: a
/// <b>negative</b> fulfilment arm — which the FIFO machine has no representation for, and whose absence both
/// clamp comments in this file and in <c>InventoryRegisters.BuildOrders</c> currently rely on — and an
/// attribution rule for WHICH movement a rejection reverses, which without a tracking number is a second guess
/// stacked on the first. Direction of the residual, stated rather than left to be discovered: an un-netted
/// Rejection <b>Out</b> leaves a purchase order retired ⇒ Purc Orders Pending low ⇒ Nett Available low ⇒
/// shortfall OVER-stated (safe); an un-netted Rejection <b>In</b> leaves a sales order retired ⇒ Sales Orders Due
/// low ⇒ Nett Available high ⇒ shortfall UNDER-stated (<b>unsafe</b>). That second arm is the one open question
/// this class hands to the user, and it is why the rejection stance is written down rather than implied by a
/// missing branch.</para>
///
/// <para><b>Stated limitations, each a deliberate choice rather than an oversight.</b>
/// <list type="bullet">
///   <item>Rejections are not netted back — see the rejection paragraph above for the reason and the cost.</item>
///   <item>Godown is <b>not</b> matched. An order names an expected godown, but goods routinely ship from
///     another; requiring equality would leave those orders un-retired and re-open the unbounded error.</item>
///   <item><b><see cref="VoucherType.AffectsStock"/> gates the PURE-STOCK doors and is IGNORED on the
///     item-invoice doors</b> — a per-door rule, not a global one, and it is stated here because the obvious
///     global reading of it is false. A Receipt/Delivery Note whose type carries the flag OFF moves no stock
///     (<c>InventoryMovements.CountsPureStock</c> requires it, mirroring <see cref="InventoryLedger"/>) and so
///     retires nothing; a <b>Purchase/Sales type with the flag OFF still moves stock and still retires an
///     order</b>, because <c>ItemInvoiceStock.Counts</c> keys item-invoice mode on the PRESENCE of item lines,
///     in its own words "not the type's <c>AffectsStock</c> flag". This is reachable, not theoretical: the flag
///     is settable, round-trips through <c>CanonicalMapper</c>/<c>CanonicalXml</c>, and is replayed verbatim by
///     <c>ImportPlan</c> (<c>affectsStock: vt.AffectsStock</c>), so an imported book can carry such a type.
///     <para>The asymmetry is <b>not</b> this class's to resolve, and copying it into a predicate here would be
///     the "persisted flag the compute ignores" defect in its usual form — two engines disagreeing about what
///     arrived. Reading movements through <see cref="InventoryMovements"/> means fulfilment gives the SAME answer
///     as the on-hand and valuation engines on BOTH arms, which is the invariant that matters and is what
///     <c>An_item_invoice_retires_an_order_even_when_its_type_has_affects_stock_off</c> and
///     <c>A_delivery_note_whose_type_has_affects_stock_off_retires_nothing_and_moves_no_stock</c> pin — each
///     asserting the order figure and the closing-stock figure together.</para></item>
/// </list></para>
///
/// <para><b>WHAT PER-LINE ATTRIBUTION MOVES, AND WHAT IT CANNOT — stated precisely, because the loose version of
/// this claim is false.</b> It is tempting to write "only the per-order rows move; the item-level total
/// <see cref="OutstandingByItem"/> hands WF-7 is unaffected". <b>That is true only of the same-date line
/// tie-break, and false in general.</b> The three cases, separately:
/// <list type="bullet">
///   <item><b>The same-date tie-break is total-neutral.</b> The FIFO walk caps every allocation at the line's own
///     remainder, so <c>done ≤ Quantity</c> on every line, the zero-floor in <see cref="OutstandingByItem"/>
///     never fires, and the total is exactly <c>Σ ordered − Σ absorbed</c>. Lines are sorted by DATE, so the
///     number/line/id tie-breaks only ever decide between lines of the SAME date — and a movement that can reach
///     one of those can reach all of them, so absorption is bounded by the movement quantity and the cohort's
///     total remainder, neither of which a tie-break touches. Only WHICH order shows the remainder moves.
///     Checked by construction in
///     <c>Swapping_two_same_day_order_lines_moves_the_per_line_figures_but_never_the_item_total</c>, which runs
///     one book twice with two same-item lines swapped: the components swap, the total does not.</item>
///   <item><b>The COHORT moves the total.</b> A movement credited to the wrong party retires a different order
///     — for the same item, so the item total really does change. That is the whole reason the party is half the
///     key, and it is why the wildcard-null rule above was deleted.</item>
///   <item><b>The DATE ordering moves the total.</b> A movement barred from a later line drops its surplus for
///     want of a line to carry it, so the total is higher than an unbounded attribution would give.</item>
/// </list>
/// Anyone tempted to restate this as a flat safety guarantee should restate it as the first bullet only.</para>
/// </summary>
public static class OrderFulfilment
{
    /// <summary>The cohort key: one counterparty's open orders for one stock item. <c>null</c> party is a key
    /// value in its own right, not a wildcard — see the class doc.</summary>
    private readonly record struct CohortKey(Guid? PartyId, Guid StockItemId);

    /// <summary>One still-open order line inside a cohort, carrying the quantity not yet attributed to a
    /// movement. It holds no party of its own: the party is half the cohort KEY, so every line in a cohort
    /// necessarily shares it.</summary>
    private sealed class OpenLine
    {
        public required Guid VoucherId { get; init; }
        public required int LineIndex { get; init; }
        public required DateOnly Date { get; init; }
        public required int Number { get; init; }
        public required decimal Remaining { get; set; }
    }

    /// <summary>Every still-open order line for one (party, stock item) pair, oldest first, plus the FIFO
    /// cursor.</summary>
    private sealed class Cohort
    {
        public List<OpenLine> Lines { get; } = new();

        /// <summary>Index of the first line not yet known to be exhausted. Movements are walked in date order
        /// and <see cref="OpenLine.Remaining"/> never increases, so a line that has reached zero can never take
        /// again and skipping it for ever is figure-neutral. Only <b>exhausted</b> lines are passed: a line left
        /// open by the date bound keeps its place, or a later movement that could legitimately retire it would
        /// find it gone.</summary>
        public int Cursor;
    }

    /// <summary>One fulfilling movement line, already normalised to the stock item's base unit.</summary>
    private readonly record struct MovementLine(
        DateOnly Date, int Number, Guid VoucherId, Guid? PartyId, Guid StockItemId, decimal Quantity);

    /// <summary>
    /// The quantity fulfilled per order line as of <paramref name="asOf"/>, keyed by
    /// (order voucher id, order-line index). Every counting order line of either base type gets an entry, so a
    /// caller can distinguish "no fulfilment" from "not an order line". Fulfilled never exceeds the ordered
    /// quantity: a surplus movement has no line to be carried on, which is what clamps outstanding at zero.
    /// </summary>
    public static IReadOnlyDictionary<(Guid VoucherId, int LineIndex), decimal> Build(Company company, DateOnly asOf)
    {
        var fulfilled = new Dictionary<(Guid, int), decimal>();
        // ONE flattened movement enumeration for the whole book, shared by both arms. This is deliberately the
        // SAME enumeration the on-hand engine (InventoryLedger) and the valuation engine (StockValuationService)
        // read: it already spans every door — pure-stock allocations AND item-invoice Voucher.InventoryLines —
        // already normalises every quantity to the item's base unit, already applies the cancelled / optional /
        // post-dated / as-of conventions, already resolves the party from the right header on each path
        // (InventoryVoucher.PartyId for a note, Voucher.PartyId for an invoice), and is already ordered by
        // (date, number, id). Enumerating the doors here instead would be a second copy of all six of those
        // rules, and the FIRST of them is the one this step exists to fix: a door that exists for stock but not
        // for fulfilment. It cannot drift now, because there is only one list.
        var movements = InventoryMovements.Between(company, from: null, to: asOf);
        Accumulate(company, asOf, VoucherBaseType.PurchaseOrder, StockDirection.Inward, movements, fulfilled);
        Accumulate(company, asOf, VoucherBaseType.SalesOrder, StockDirection.Outward, movements, fulfilled);
        return fulfilled;
    }

    /// <summary>
    /// Σ still-<b>outstanding</b> ordered quantity per stock item for one order base type as of
    /// <paramref name="asOf"/> — the figure TallyPrime's "Purc Orders Pending" and "Sales Orders Due" columns
    /// mean, and the one <see cref="ReorderStatus"/>'s Nett Available nets (WF-7/S2; netting the raw ordered
    /// quantity there was DD-5). Items with nothing outstanding are absent from the dictionary; a caller reads a
    /// missing key as zero.
    /// </summary>
    public static IReadOnlyDictionary<Guid, decimal> OutstandingByItem(
        Company company, VoucherBaseType orderBaseType, DateOnly asOf)
    {
        var fulfilled = Build(company, asOf);
        var totals = new Dictionary<Guid, decimal>();
        foreach (var v in company.InventoryVouchers)
        {
            if (!CountsAsOf(company, v, asOf, orderBaseType)) continue;
            for (var i = 0; i < v.OrderLines.Count; i++)
            {
                var line = v.OrderLines[i];
                var done = fulfilled.TryGetValue((v.Id, i), out var f) ? f : 0m;
                var outstanding = line.Quantity - done;
                // 🔴 Stated plainly so no reviewer reads `<= 0m` as a TESTED clamp — the same disclosure
                // InventoryRegisters.BuildOrders carries on its twin floor. Under the CURRENT derivation the
                // negative arm is UNREACHABLE: Accumulate caps every allocation at the line's own Remaining, so
                // `done <= line.Quantity` always and `outstanding` cannot be negative. What this guard actually
                // does today is the `== 0` half: drop a fully-retired line from the total. It is written `<= 0m`
                // and kept because the guard belongs to the SUM, not to one attribution rule — an OrderLinks
                // based sum (the mechanism this slice could not reach, and the one JobWorkReports uses) has no
                // per-line cap and CAN exceed the ordered quantity, and an unclamped negative there would net
                // against a genuinely open line and under-state real demand.
                if (outstanding <= 0m) continue;
                totals[line.StockItemId] = totals.TryGetValue(line.StockItemId, out var t)
                    ? t + outstanding
                    : outstanding;
            }
        }
        return totals;
    }

    /// <summary>Σ still-outstanding ordered quantity for ONE stock item — the single-item convenience over
    /// <see cref="OutstandingByItem"/>. Prefer the dictionary when scanning many items: this rebuilds the whole
    /// fulfilment map per call.</summary>
    public static decimal OutstandingForItem(
        Company company, Guid stockItemId, VoucherBaseType orderBaseType, DateOnly asOf)
        => OutstandingByItem(company, orderBaseType, asOf).TryGetValue(stockItemId, out var q) ? q : 0m;

    // ------------------------------------------------------------------ internal

    /// <summary>
    /// Whether a movement that came out of <see cref="InventoryMovements.Between"/> is a document that
    /// <b>fulfils</b> an order of <paramref name="orderBaseType"/>. This is the door allow-list, and it is
    /// written as an explicit closed match on purpose (full census in the class doc): every other stock-moving
    /// surface — Rejection In/Out, Stock-Journal source and destination, Material In/Out, and any origin added
    /// later — falls through to <c>false</c>. A direction-only or "any outward movement" rule would let an
    /// inter-godown transfer or a supplier return retire a sales order, and a rejection retire the order it was
    /// meant to re-open.
    /// <para><b>POS needs no arm of its own.</b> A POS bill is a Sales voucher with
    /// <see cref="VoucherType.UseForPos"/> on, so it arrives as <see cref="InventoryMovements.Source.ItemInvoiceSales"/>
    /// exactly like any other item invoice; the tender split touches only the accounting debit side.</para>
    /// </summary>
    private static bool IsFulfillingDoor(InventoryMovements.Source origin, VoucherBaseType orderBaseType) =>
        orderBaseType switch
        {
            VoucherBaseType.PurchaseOrder =>
                origin is InventoryMovements.Source.ReceiptNote or InventoryMovements.Source.ItemInvoicePurchase,
            VoucherBaseType.SalesOrder =>
                origin is InventoryMovements.Source.DeliveryNote or InventoryMovements.Source.ItemInvoiceSales,
            _ => false,
        };

    /// <summary>
    /// The counting predicate for an order voucher — deliberately the SAME one
    /// <see cref="InventoryRegisters.BuildOrders"/> uses, so the two cannot disagree about <b>WHICH ORDERS
    /// EXIST</b> (ER-4): cancelled never counts, and a post-dated order is provisional only until its date
    /// arrives. It applies to the ORDER side only; which documents fulfil one is
    /// <see cref="IsFulfillingDoor"/>'s job.
    /// <para>Parity now runs to the QUANTITY as well. WF-7/S2 deleted <c>ReorderStatus.PendingOrderQty</c>'s raw
    /// ordered-quantity sum and pointed <see cref="ReorderStatus"/> at <see cref="OutstandingByItem"/>, so the
    /// Order Register's Outstanding column and Reorder Status's "Purc Orders Pending" / "Sales Orders Due"
    /// columns are the same derivation over the same book. The temporary divergence WF-8 shipped with — and the
    /// tripwire test that held it visible — are closed; the agreement is pinned by
    /// <c>Order_register_and_reorder_status_agree_on_a_partly_delivered_order</c>.</para>
    /// </summary>
    private static bool CountsAsOf(Company company, InventoryVoucher v, DateOnly asOf, VoucherBaseType baseType)
    {
        if (v.Cancelled) return false;
        if (v.Date > asOf) return false;
        // Redundant with the date bound above and kept for literal parity with the sibling predicates, which
        // state the post-dating rule explicitly rather than leaving it implied by the bound.
        if (v.PostDated && v.Date > asOf) return false;
        return company.FindVoucherType(v.TypeId)?.BaseType == baseType;
    }

    private static void Accumulate(
        Company company,
        DateOnly asOf,
        VoucherBaseType orderBaseType,
        StockDirection fulfillingDirection,
        IReadOnlyList<InventoryMovements.Movement> allMovements,
        Dictionary<(Guid, int), decimal> fulfilled)
    {
        // 1. Every counting order line, bucketed by (PARTY, STOCK ITEM) — a movement can only ever retire an
        //    order raised on the same counterparty for the same item, and a null party is a bucket of its own
        //    rather than a wildcard (class doc: the wildcard made the cohort item-wide in every real book, so a
        //    delivery to one customer retired another's order). A CANCELLED order is void and is excluded here
        //    (CountsAsOf), so it cannot absorb FIFO capacity belonging to the live order behind it — which would
        //    otherwise leave a genuinely delivered order outstanding for ever on the ORDINARY cancellation book.
        //    🔴 THAT EXCLUSION HAS A REAL COST, and stating only its benefit here is how the cost stayed
        //    invisible: on the DELIVER-THEN-CANCEL book the movement finds the cancelled line gone and retires
        //    the next LIVE order instead — an order nothing was ever received or delivered against. `Cancelled`
        //    is a boolean with no cancellation date, so the two books are indistinguishable here. The class doc's
        //    cancelled-order paragraph states the direction on each arm (sales = UNSAFE) and the user go/no-go.
        var cohorts = new Dictionary<CohortKey, Cohort>();
        foreach (var v in company.InventoryVouchers)
        {
            if (!CountsAsOf(company, v, asOf, orderBaseType)) continue;
            for (var i = 0; i < v.OrderLines.Count; i++)
            {
                var line = v.OrderLines[i];
                fulfilled[(v.Id, i)] = 0m;
                var key = new CohortKey(v.PartyId, line.StockItemId);
                if (!cohorts.TryGetValue(key, out var cohort))
                    cohorts[key] = cohort = new Cohort();
                // OrderLine.Quantity is already in the item's base unit (an order line carries no UnitId), so
                // it needs no conversion — unlike the movement allocations below.
                cohort.Lines.Add(new OpenLine
                {
                    VoucherId = v.Id, LineIndex = i, Date = v.Date, Number = v.Number,
                    Remaining = line.Quantity,
                });
            }
        }
        if (cohorts.Count == 0) return;
        foreach (var cohort in cohorts.Values) cohort.Lines.Sort(CompareOpenLines);

        // 2. Every fulfilling movement, filtered to the DOORS that discharge this kind of order — a Receipt Note
        //    or a Purchase item-invoice inward, a Delivery Note or a Sales/POS item-invoice outward. See
        //    IsFulfillingDoor for the closed allow-list and the class doc for the census of the twelve
        //    stock-moving surfaces it selects four of.
        var movements = new List<MovementLine>();
        foreach (var m in allMovements)
        {
            if (!IsFulfillingDoor(m.Origin, orderBaseType)) continue;
            // DEFENSIVE ONLY, and recorded as such so its untestedness is not read as a coverage hole. 🔴 Every
            // symbol named below RESOLVES TO A GREP, which is the whole point of restating them: the previous
            // revision cited "Voucher.StampInventoryDirection", a member that exists NOWHERE in this repository
            // (the only hit was that comment asserting it), so the one piece of evidence offered for skipping
            // coverage on this line could not be checked — and an unresolvable citation is what R7 exists to
            // prevent. A wrong-direction line cannot be posted on EITHER door:
            //   * PURE STOCK — InventoryPostingService.RequireDirection (InventoryPostingService.cs:290, called
            //     at :213/:219) forces Inward on a Receipt Note and Outward on a Delivery Note, and throws
            //     otherwise.
            //   * ITEM INVOICE — Voucher.SetInventoryLineDirections (Voucher.cs:181) rewrites every item line to
            //     the voucher-nature-implied direction (Purchase => Inward, Sales => Outward). It is invoked by
            //     LedgerService.StampInventoryLineDirections (LedgerService.cs:67) from LedgerService.Post
            //     (LedgerService.cs:43) BEFORE validation, and VoucherValidator.EnsureItemInvoiceValid then
            //     re-checks the stamped direction against its own expectedDir (VoucherValidator.cs:204 and :230)
            //     and throws on a mismatch. Two independent guards, both greppable.
            // The DOOR filter above is the guard that actually bites (a Rejection In is inward; a Stock-Journal
            // source line is outward).
            if (m.Direction != fulfillingDirection) continue;
            // m.Quantity is ALREADY in the stock item's base unit — InventoryMovements normalises both a
            // pure-stock allocation and an item-invoice line through the item's own Unit, so "4 Box-12" is 48
            // Nos on either door and this walk needs no conversion of its own.
            movements.Add(new MovementLine(m.Date, m.Number, m.VoucherId, m.PartyId, m.StockItemId, m.Quantity));
        }
        if (movements.Count == 0) return;
        // allMovements already arrives ordered by (date, number, id) and the filter preserves it, so this is a
        // no-op re-assertion rather than work — kept because the FIFO walk below is only correct on that order
        // and a reader must be able to see the guarantee here, not infer it from another file.
        movements.Sort(CompareMovements);

        // 3. FIFO: each movement, oldest first, retires the oldest still-open order line in ITS OWN cohort — the
        //    same counterparty and the same item — that was already placed when the movement happened, spilling
        //    into the next. A movement whose (party, item) pair has no cohort at all retires nothing, which is
        //    where a blank note meets a party-named order. A surplus that finds no open line is DROPPED — there
        //    is no line to carry it, which is precisely the over-delivery clamp: Fulfilled stops at Ordered and
        //    Outstanding is 0, never negative.
        foreach (var mv in movements)
        {
            if (!cohorts.TryGetValue(new CohortKey(mv.PartyId, mv.StockItemId), out var cohort)) continue;
            var lines = cohort.Lines;
            // Retire the cursor past lines that can never take again. See Cohort.Cursor: only EXHAUSTED lines
            // are passed, which is what makes this figure-neutral rather than an optimisation that loses a line.
            while (cohort.Cursor < lines.Count && lines[cohort.Cursor].Remaining <= 0m) cohort.Cursor++;

            var remaining = mv.Quantity;
            for (var i = cohort.Cursor; i < lines.Count; i++)
            {
                if (remaining <= 0m) break;
                var open = lines[i];
                // The cohort is sorted by date, so the first order placed after this movement means every later
                // one is too: goods shipped in April cannot retire an order placed in May.
                if (open.Date > mv.Date) break;
                if (open.Remaining <= 0m) continue;
                var take = Math.Min(open.Remaining, remaining);
                open.Remaining -= take;
                remaining -= take;
                fulfilled[(open.VoucherId, open.LineIndex)] += take;
            }
        }
    }

    /// <summary>Order lines oldest-first — date, then the type's own sequence number, then the line's position
    /// on its voucher, then the voucher id so the order is TOTAL and the report is deterministic.
    /// <para>The tie-breaks are load-bearing, not decoration: <see cref="List{T}.Sort(Comparison{T})"/> is
    /// <b>unstable</b>, so any pair that compares EQUAL is retired in whichever order the sort happened to
    /// produce — the same book yielding two different Order Registers. Totality is the whole defence, so each
    /// clause is pinned by a fixture that was measured to bite it, not merely to cover it:
    /// <list type="bullet">
    ///   <item><b>date</b> — <c>Fulfilment_is_attributed_to_the_earliest_open_order_first</c>.</item>
    ///   <item><b>number</b> — <c>Two_orders_raised_on_the_same_day_are_retired_in_voucher_number_order</c>,
    ///     which posts its two orders with numbers in the reverse of their insertion order.</item>
    ///   <item><b>line index</b> — <c>Two_lines_of_one_order_for_the_same_item_are_retired_in_line_order</c>.
    ///     It uses <b>40</b> lines on one voucher for a measured reason: below 17 elements
    ///     <c>List.Sort</c> falls back to an insertion sort that preserves insertion order, so a two-line fixture
    ///     leaves this clause's removal GREEN and pins nothing.</item>
    ///   <item><b>voucher id</b> — <c>Two_orders_alike_in_date_and_number_are_retired_in_a_deterministic_total_order</c>.
    ///     Reachable only across two voucher TYPES of one base type, since numbers are unique per type; it runs
    ///     twelve independently-seeded books because one pair catches the clause's removal only about half the
    ///     time (measured 4 red in 5), the coin-flip being exactly the non-determinism the clause removes.</item>
    /// </list></para>
    /// </summary>
    private static int CompareOpenLines(OpenLine a, OpenLine b)
    {
        var byDate = a.Date.CompareTo(b.Date);
        if (byDate != 0) return byDate;
        var byNumber = a.Number.CompareTo(b.Number);
        if (byNumber != 0) return byNumber;
        var byLine = a.LineIndex.CompareTo(b.LineIndex);
        return byLine != 0 ? byLine : a.VoucherId.CompareTo(b.VoucherId);
    }

    /// <summary>Movements oldest-first, on the same total ordering as the order lines.</summary>
    private static int CompareMovements(MovementLine a, MovementLine b)
    {
        var byDate = a.Date.CompareTo(b.Date);
        if (byDate != 0) return byDate;
        var byNumber = a.Number.CompareTo(b.Number);
        return byNumber != 0 ? byNumber : a.VoucherId.CompareTo(b.VoucherId);
    }
}
