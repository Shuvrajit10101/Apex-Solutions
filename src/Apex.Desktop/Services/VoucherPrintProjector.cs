using System;
using System.Collections.Generic;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Io;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;

namespace Apex.Desktop.Services;

/// <summary>
/// Projects a posted <see cref="Voucher"/> (with its <see cref="Company"/> context) into the framework-agnostic
/// print DTOs the <c>Apex.Ledger.Io</c> renderers consume (RQ-10 / RQ-11): a <see cref="VoucherPrintData"/> for a
/// plain accounting voucher, or an <see cref="InvoicePrintData"/> for a Sales voucher run in item-invoice (or
/// accounting-invoice) mode — which renders as a GST <b>tax invoice</b> or, per <see cref="IsBillOfSupply"/>, as the
/// <b>bill of supply</b> CGST Act §31(3)(c) requires of a composition or wholly-exempt supply (W0-1 / census T0-7).
/// The mapping is pure and Avalonia-free — it only resolves GUID→name masters and formats dates and quantities to
/// display strings. It never touches disk, dialogs, OS-print or the clock (ER-12): the whole IO path stays in
/// <c>Apex.Ledger.Io</c>. No brand text is ever introduced.
///
/// <para><b>🔴 W0-10 — THE ONE MONEY RULE THIS CLASS KEEPS: every printed figure is read off the voucher's POSTED
/// legs, on BOTH passes.</b> No rate, cess, routing or total is ever resolved from a live master or recomputed at
/// print time, so the printed Grand Total is the debt the general ledger recorded — <b>on every non-TCS sale</b>, and
/// by construction. <b>That qualification is exact and load-bearing</b> (W0-10 review, findings #3/#10; plan.md
/// carry-forward (a) states it in the same words): §206C TCS is collected on top of the GST-inclusive total and rides
/// the party debit (<c>VoucherEntryViewModel.AcceptItemInvoice</c>), while <see cref="InvoicePrintData"/> has no TCS
/// member at all — measured on the odd-paisa fixture, a posted party leg of ₹56,368.14 against a printed Grand Total
/// of ₹55,810.14, short by the collected ₹558. That is NOT something this class caused or can reach (TCS is not GST
/// tax, so the posted-legs switch cannot see it) and it is listed as out of scope below; <b>the unqualified claim that
/// used to stand here is exactly the claim that would justify deleting a TCS row as redundant</b>, and it is also what
/// the next planned slice (the item-path footing guard) would have acted on by refusing to print every TCS invoice.
/// The item pass used to re-derive its head totals, its per-rate breakup rows and its intra/inter routing from a live
/// <see cref="GstService.ComputeInvoiceTax"/> while the service pass read the posted legs, so ONE projector held TWO
/// sources of truth for money and every ordinary master edit silently rewrote already-issued documents (measured, all
/// through the shipped UI: re-rating an item 18% → 28% reprinted ₹60,539.81 against a posted ₹55,810.14; declaring a
/// cess after the sale conjured ₹5,675.61 that is in no ledger; a wholly-exempt line under posted tax printed
/// ₹47,296.73 against ₹55,810.14, short by the whole ₹8,513.41). <b>Do not reintroduce a live resolve on either pass.</b>
/// Pinned by <c>ItemInvoicePostedTaxTests</c> and <c>InvoicePrintFootingTests</c>.</para>
///
/// <para><b>KNOWN, DELIBERATELY OUT OF SCOPE HERE (recorded so the next reader does not re-discover them):</b></para>
/// <list type="bullet">
/// <item><b>§206C(1H)/§206C(1) TCS is on the party leg but NOT on the document</b> (W0-10 carry-forward (a); review
/// findings #3/#10). <c>AcceptItemInvoice</c> builds the Sales party debit as <c>Σ item value + GST + cess + TCS</c>
/// and <see cref="InvoicePrintData"/> carries no TCS field, so a TCS-bearing invoice prints short by exactly the
/// collected TCS — measured ₹55,810.14 printed against ₹56,368.14 posted. Closing it needs a DTO field plus an
/// <c>InvoicePdf</c>/preview row, i.e. a slice of its own, and the <b>item-path footing guard must be sequenced after
/// it</b> or it would demote every TCS invoice to a plain voucher. Pinned meanwhile by
/// <c>ItemInvoicePostedTaxTests.A_tcs_bearing_invoice_still_prints_as_a_tax_invoice_and_pins_the_known_shortfall</c>,
/// which fails BY DESIGN the day the row lands.</item>
/// <item><b>A supply TAXABLE AT 0% states no rate row</b> (W0-10 review findings #2/#4/#9). A zero-rate group posts no
/// tax leg at all (<c>GstService.AddHead</c> early-returns on a zero amount), so neither pass can see it: the item
/// pass stopped emitting the <c>"0% | value | 0.00 | 0.00"</c> row the pre-W0-10 live resolve produced, which is how
/// it CONVERGED on the service pass, where <c>ZeroRatedServiceInvoice_printsAsTaxInvoice</c> has always asserted
/// <c>Empty(TaxRows)</c>. Restoring the row on one pass alone would re-open the two-answers-for-one-question defect
/// this slice closed, and the only source for "this line was rated 0%" is the LIVE master this class refuses to read
/// for a printed particular. Whether CGST Rule 46(m) requires the row at all is a statutory question for BOTH passes
/// whose answer needs a rate snapshotted onto the posted line (a schema change) — a plan.md carry-forward, not a
/// print-path patch. Held together meanwhile by
/// <c>ItemInvoicePostedTaxTests.A_taxable_at_zero_percent_supply_prints_no_rate_row_on_the_item_and_service_passes_alike</c>.</item>
/// <item><b>F7 — printing after GST is switched off throws.</b> <see cref="ProjectInvoice"/> opens with
/// <c>GstService.IsInterState</c>, which raises "GST is not enabled (no home state) — cannot route a supply" when the
/// company has no home State. Reachable on HEAD too (any item invoice posted while GST was on, reprinted after it was
/// switched off); the service-invoice slice merely widens the set of vouchers that reach it. Fixing it means deciding
/// what a de-GSTed company should print for an already-issued tax invoice — a separate change, not a print-path
/// patch.</item>
/// <item><b>W0-1b — the POS retail-bill path was the OTHER HALF of the bill-of-supply defect. FIXED in the W0-1
/// follow-up.</b> W0-1 routed the voucher-screen document only, so the same composition dealer billing the same supply
/// through POS Billing still got a customer-facing receipt titled from <c>PosConfig.DefaultTitle</c> ("Retail
/// Invoice") that stated "Taxable / CGST / SGST" head lines — <c>PosReceiptPdf</c> drew them UNCONDITIONALLY, outside
/// any TaxRows guard — with no §31(3)(c) routing and none of the Rule 5(1)(f) wording. <c>PosReceiptData</c> now
/// carries <c>IsBillOfSupply</c> + <c>TopDeclaration</c>, <c>PosReceiptPdf</c> and the on-screen receipt mirror gate
/// the title, the head lines and the per-rate breakup on them, and <c>PosBillingViewModel.BuildReceipt</c> routes
/// them from <see cref="IsBillOfSupply"/> — the SAME predicate the invoice path uses, never a third copy of the
/// rule.</item>
/// <item><b>W0-8 — the e-Way Bill Part-A <c>docType</c> was a FOURTH document-kind emitter. NOW ROUTED.</b>
/// <c>EWayBillService</c> derived the NIC code from <c>VoucherBaseType</c> alone, so a movement this projector titles
/// BILL OF SUPPLY still emitted <c>"INV"</c> into the portal request. The NIC master-codes list was read from an
/// official source (<c>https://docs.ewaybillgst.gov.in/apidocs/master-codes-list.html</c>) and carries <c>BIL</c> =
/// Bill of Supply, so <c>EWayBillService.PartACodesFor</c> now takes its outward-sales limb from
/// <c>GstReportSupport.IsBillOfSupply</c> — the shared engine predicate.
/// <para><b>W0-9 — the residual gap that note recorded is now CLOSED, and this class is where it lived.</b> The engine
/// predicate used to be the §10 (composition) limb only, while an <c>IsBillOfSupply</c> of the SAME NAME here added the
/// §31(3)(c) <i>exempt</i> limb — so a Regular dealer's wholly-exempt movement printed BILL OF SUPPLY while filing
/// <c>docType "INV"</c>. The whole rule now lives in <c>GstReportSupport.IsBillOfSupply</c>, one layer down where the
/// printer AND the e-Way engine both reach it, and <see cref="IsBillOfSupply"/> here is a pure forward. <b>The document
/// kind is decided in exactly one place; do not re-add a condition to any wrapper.</b>
/// <para><b>One documented divergence, and it is a FILING rule, not a second document rule (W0-9 review; R12 user
/// ruling, 2026-08-14).</b> The e-Way Part-A reads <c>GstReportSupport.IsBillOfSupplyForFiling</c>, which is
/// <c>IsCompositionBillOfSupply || IsBillOfSupply</c>. It differs on exactly one shape — a §10 dealer's movement
/// carrying posted forward tax — because <c>IsBillOfSupply</c>'s first gate exists to stop a PRINTED Grand Total
/// falling short of the posted party leg, and the NIC <c>docType</c> carries no money at all. That shape has no
/// printed statutory title to contradict: this projector refuses it outright (<see cref="ProjectInvoice"/>) and it
/// prints as the plain Dr/Cr voucher. Nothing in THIS class reads the filing predicate, and nothing should.</para></item>
/// <item><b>F6 — <c>SchemaDowngrade.V49ToV48</c> loses the vouchers PK / NOT-NULLs / index</b> when it rebuilds the
/// table. This is the SAME pre-existing idiom as <c>V48ToV47</c> and <c>V47ToV46</c>, and <c>SchemaDowngrade</c> is
/// referenced nowhere in <c>src/</c> (test-only, to prove forward-migration parity), so nothing shipped reads a
/// downgraded database. Changing the idiom means changing all three together.</item>
/// </list>
/// </summary>
public static class VoucherPrintProjector
{
    /// <summary>
    /// True iff <paramref name="voucher"/> should print as a GST invoice document (tax invoice or bill of supply)
    /// rather than a plain Dr/Cr voucher (RQ-10).
    /// <para><b>W0-9 — a PURE FORWARD to <see cref="GstReportSupport.IsTaxInvoice"/>, where the rule now lives.</b>
    /// It moved down because <see cref="GstReportSupport.IsBillOfSupply"/>'s exempt limb gates on it and that limb now
    /// serves the e-Way engine as well as the printer. This wrapper carries <b>no logic of its own and must never
    /// acquire any</b> — a second body here is exactly how the two <c>IsBillOfSupply</c> predicates came to disagree.
    /// Pinned by <c>OneBillOfSupplyRuleDelegationTests</c>.</para>
    /// </summary>
    public static bool IsTaxInvoice(Company company, Voucher voucher) =>
        GstReportSupport.IsTaxInvoice(company, voucher);

    /// <summary>
    /// The message <see cref="ProjectInvoice"/> refuses the §10 contradiction with. Stated once so the exception text
    /// and the tests that pin it cannot drift, and kept ASCII-safe (it can surface in the UI).
    /// </summary>
    internal const string CompositionContradictionRefusal =
        "No statutory document can be issued for this voucher: it is a section 10 (composition) outward supply that " +
        "nonetheless recorded forward GST. CGST Act section 31(3)(c) requires a bill of supply instead of a tax " +
        "invoice, while section 10(4) bars the dealer from collecting any tax from the recipient - so a tax invoice " +
        "is the document he is denied, and a bill of supply would state a total short of the posted party leg. It " +
        "prints as the plain voucher, which states every posted leg exactly.";

    /// <summary>
    /// True iff a <b>ledger-only Sales</b> voucher is a SERVICE (Accounting Invoice) sale — the one ledger-only shape
    /// that may print as a tax invoice.
    /// <para><b>W0-9 — a PURE FORWARD to <see cref="GstReportSupport.IsServiceAccountingInvoice"/>, where the rule and
    /// its six structural conjuncts now live.</b> They moved down with <see cref="GstReportSupport.IsTaxInvoice"/>,
    /// because <see cref="GstReportSupport.IsBillOfSupply"/>'s exempt limb branches on this answer and that limb now
    /// serves the e-Way engine as well as the printer. This wrapper carries <b>no logic of its own and must never
    /// acquire any</b>. Pinned by <c>OneBillOfSupplyRuleDelegationTests</c>.</para>
    /// </summary>
    public static bool IsServiceAccountingInvoice(Company company, Voucher voucher) =>
        GstReportSupport.IsServiceAccountingInvoice(company, voucher);

    // ---------------------------------------------------------------- RQ-10: plain voucher

    /// <summary>
    /// Projects a voucher into a <see cref="VoucherPrintData"/> for <c>VoucherPdf</c>: company/title header,
    /// No/Date/Party line, the Dr/Cr posting lines (ledger names resolved) and the narration. Dates are
    /// formatted here so the renderer stays clock-free.
    /// </summary>
    public static VoucherPrintData ProjectVoucher(Company company, Voucher voucher)
    {
        ArgumentNullException.ThrowIfNull(company);
        ArgumentNullException.ThrowIfNull(voucher);

        var type = company.FindVoucherType(voucher.TypeId);
        var party = voucher.PartyId is Guid pid ? company.FindLedger(pid)?.Name : null;

        var lines = new List<VoucherPrintLine>(voucher.Lines.Count);
        foreach (var l in voucher.Lines)
            lines.Add(new VoucherPrintLine
            {
                LedgerName = ReportPrintProjector.Ascii(company.FindLedger(l.LedgerId)?.Name ?? "(unknown)"),
                IsDebit = l.Side == DrCr.Debit,
                Amount = l.Amount,
            });

        return new VoucherPrintData
        {
            CompanyName = ReportPrintProjector.Ascii(CompanyDisplayName(company)),
            VoucherTypeName = ReportPrintProjector.Ascii(type?.Name ?? string.Empty),
            VoucherNumber = company.FormatVoucherNumber(voucher),
            DateText = voucher.Date.ToString("dd-MM-yyyy", System.Globalization.CultureInfo.InvariantCulture),
            PartyName = ReportPrintProjector.Ascii(party ?? string.Empty),
            // Counterparty captured field (numbering §8): the other party's number, labelled per base type. Blank
            // when none was captured ⇒ nothing prints ⇒ byte-identical (ER-13).
            ReferenceNo = ReportPrintProjector.Ascii(voucher.ReferenceNo ?? string.Empty),
            ReferenceCaption = ReferenceCaption(type),
            ReferenceDateText = voucher.ReferenceDate is { } rd
                ? rd.ToString("dd-MM-yyyy", System.Globalization.CultureInfo.InvariantCulture)
                : string.Empty,
            Lines = lines,
            Narration = ReportPrintProjector.Ascii(voucher.Narration ?? string.Empty),
        };
    }

    // ---------------------------------------------------------------- W0-1: which document is this, in law?

    /// <summary>
    /// True iff this voucher must be issued as a <b>bill of supply</b> rather than a tax invoice — CGST Act
    /// §31(3)(c), <b>both</b> limbs (§10 composition, and wholly exempt / nil-rated / non-GST).
    ///
    /// <para><b>🔴 W0-9 — a PURE FORWARD to <see cref="GstReportSupport.IsBillOfSupply"/>, and that is the entire
    /// point of the slice.</b> This method used to hold the §31(3)(c) EXEMPT limb itself, because it lives in
    /// <c>Apex.Desktop</c> and <c>Apex.Ledger</c> cannot reference it — so the engine kept a narrower predicate of the
    /// same name and the two disagreed. The printed title read this one; the e-Way Bill Part-A <c>docType</c> read the
    /// engine's. A REGULAR dealer's wholly-exempt goods movement therefore printed BILL OF SUPPLY while the EWB-01
    /// filed <c>"INV"</c>. The rule now lives one layer down, where both consumers reach it.</para>
    ///
    /// <para><b>This wrapper carries no logic of its own and must never acquire any.</b> It survives only so the
    /// Desktop call sites read naturally; the moment a condition is added here the two predicates are two rules again.
    /// Pinned by <c>OneBillOfSupplyRuleDelegationTests</c>, which drives both predicates over the whole document
    /// matrix and fails on the first disagreement.</para>
    /// </summary>
    public static bool IsBillOfSupply(Company company, Voucher voucher) =>
        GstReportSupport.IsBillOfSupply(company, voucher);

    /// <summary>The Rule 5(1)(f) wording a <b>composition</b> taxable person must carry "at the top of the bill of
    /// supply issued by him", or blank. Gated on the §10 limb alone: a regular dealer's exempt bill of supply must NOT
    /// bear it — he is not a composition taxable person.
    /// <para>Public since the W0-1 follow-up so the POS receipt path reads the SAME rule rather than spelling a third
    /// copy of it (<c>PosBillingViewModel.BuildReceipt</c>). Callers must gate it on
    /// <see cref="IsBillOfSupply"/> first, exactly as <see cref="ProjectInvoice"/> does — the wording belongs on a
    /// bill of supply, not on any document a §10 dealer happens to produce.</para></summary>
    public static string TopDeclarationFor(Company company, Voucher voucher) =>
        GstReportSupport.IsCompositionBillOfSupply(company, voucher)
            ? GstReportSupport.BillOfSupplyDeclaration
            : string.Empty;

    // ---------------------------------------------------------------- RQ-11: tax invoice

    /// <summary>
    /// Projects a Sales item-invoice voucher into an <see cref="InvoicePrintData"/> GST tax invoice for
    /// <c>InvoicePdf</c>: the seller (company) and buyer (party) name/address/GSTIN/State blocks, the item rows
    /// (Sr resolved by row order, Description/HSN from the stock item, Qty/Rate formatted), the per-rate GST
    /// breakup and the money totals.
    ///
    /// <para><b>W0-10 — the rows come from the stock lines, the MONEY comes from the POSTED legs.</b> The per-rate
    /// breakup, the per-head totals, the ring-fenced cess, the round-off and the intra/inter routing are all read off
    /// this voucher's own <see cref="GstLineTax"/> legs (<see cref="GstReportSupport.ReadPostedRateGroups"/>,
    /// <see cref="GstReportSupport.PostedCessTotal"/>, <see cref="GstReportSupport.PostedForwardRouting"/>,
    /// <see cref="PostedRoundOff"/>) — <b>never recomputed, never re-resolved from a live master</b>, exactly as
    /// <see cref="ProjectServiceInvoice"/> has always done. So the printed Grand Total equals the posted party leg, and
    /// editing a rate, a cess or the party's State after posting cannot move a figure on an issued document. Intra vs
    /// inter falls back to the party's recorded State only when the voucher posted no forward tax leg at all and
    /// therefore states nothing about routing (F1).</para>
    ///
    /// <para><b>W0-1 follow-up — it REFUSES the §10 contradiction structurally, instead of relying on the caller.</b>
    /// <see cref="IsTaxInvoice"/> returns <c>false</c> for a composition dealer's outward supply that nonetheless
    /// carries posted forward tax, but this method used to return a TAX INVOICE DTO for it anyway: its own
    /// <see cref="IsBillOfSupply"/> bails on the posted-tax gate BEFORE the §10 limb, so <c>billOfSupply</c> came out
    /// false and every branch below took the tax-invoice road. Measured: Grand Total ₹47,296.73 against a posted party
    /// debit of ₹55,810.14 — understated by the whole ₹8,513.41, because the head totals come from a live
    /// <c>ComputeInvoiceTax</c> that short-circuits to zero for composition. Exactly one <c>src/</c> call site
    /// (<c>VoucherDetailViewModel.BuildPrintPreview</c>) checked <see cref="IsTaxInvoice"/> first, and this method is
    /// public. A projector that cannot produce a lawful document must not produce one, so it throws — the same
    /// direction <c>GstReportSupport.ServiceInvoiceFoots</c> (F2) already takes when a document cannot reconcile to its own
    /// party leg, and the same one the (pre-existing, F7) GST-disabled throw takes. The real call site is unaffected;
    /// a future caller that forgets the predicate now fails loudly instead of issuing an understated demand.</para>
    ///
    /// <para><b>🔴 W0-10 — THAT REFUSAL IS NOW MOTIVATED BY THE STATUTE ALONE, AND MUST NOT BE READ AS REDUNDANT.</b>
    /// Its original justification was arithmetic: the head totals came from a live <c>ComputeInvoiceTax</c> that
    /// short-circuits to zero for composition, so the document understated the posted debt by the whole tax. Reading
    /// the POSTED legs removes that symptom — a §10 voucher carrying posted forward tax would now print a TAX INVOICE
    /// whose Grand Total DOES equal the party leg. It is still refused, and for the reason that never depended on the
    /// arithmetic: CGST Act §31(3)(c) requires a bill of supply "instead of a tax invoice" from a §10 dealer and
    /// §10(4) bars him from collecting any tax, so the tax invoice is the document he is DENIED however well it foots.
    /// A figure that now reconciles is not a licence to issue the wrong document.</para>
    /// </summary>
    /// <exception cref="InvalidOperationException">The voucher is a §10 (composition) outward supply carrying posted
    /// forward GST or Compensation Cess — neither statutory document describes it (CGST Act §31(3)(c) vs §10(4)), so
    /// it has no invoice projection at all and must print as the plain Dr/Cr voucher.</exception>
    public static InvoicePrintData ProjectInvoice(Company company, Voucher voucher)
    {
        ArgumentNullException.ThrowIfNull(company);
        ArgumentNullException.ThrowIfNull(voucher);

        // The structural refusal, from the ONE shared definition (GstReportSupport), tested BEFORE anything else so
        // no partial projection or live master read can happen first.
        if (GstReportSupport.IsCompositionSupplyCarryingForwardTax(company, voucher))
            throw new InvalidOperationException(CompositionContradictionRefusal);

        var gst = new GstService(company);
        var partyLedger = voucher.PartyId is Guid pid ? company.FindLedger(pid) : null;
        var partyState = partyLedger?.PartyGst?.StateCode;
        // The routing the party's LIVE master implies. Used only where the voucher posted no forward tax leg at all
        // and there is therefore nothing posted for the document to contradict (F1) — on BOTH passes, since W0-10.
        bool livePartyInterState = gst.IsInterState(partyState);

        // A SERVICE (Accounting Invoice) sale has no stock lines, so the item pass below would project an EMPTY
        // invoice. It takes its own projection; both passes now read the same POSTED legs for every figure.
        if (IsServiceAccountingInvoice(company, voucher))
            return ProjectServiceInvoice(company, voucher, partyLedger, livePartyInterState);

        var items = new List<InvoiceItemRow>(voucher.InventoryLines.Count);
        // Σ of EVERY item line's value — rated AND exempt/nil/non-GST/unresolved. This is the invoice's goods
        // (taxable-value) total that the Grand Total must foot to; the per-rate breakup below only carries the
        // GST tax, which was charged on rated lines alone (exempt/nil lines contribute their value at 0 tax).
        decimal totalGoodsValue = 0m;

        foreach (var il in voucher.InventoryLines)
        {
            var item = company.FindStockItem(il.StockItemId);
            // WI-10 Gap 2: label the quantity with the unit the LINE is actually stated in, not the item's base
            // unit — the printed quantity IS the line quantity, and the printed Rate is per that same unit, so
            // "2 Doz @ ₹10.00 = ₹20.00" reads correctly and foots. Falling back to the item's base unit keeps a
            // line that carries no unit byte-identical to before (ER-13). Printing "2 Nos @ ₹10 = ₹20" would be
            // internally consistent arithmetic on a QUANTITY THAT IS NOT WHAT MOVED (24 Nos did) — a document
            // the buyer, the auditor and the e-way bill would all read differently.
            var unit = il.UnitId is { } lineUnitId
                ? company.FindUnit(lineUnitId)?.Symbol
                : item is not null ? company.FindUnit(item.BaseUnitId)?.Symbol : null;
            var qtyText = IndianFormat.Quantity(il.Quantity);
            if (!string.IsNullOrEmpty(unit)) qtyText += " " + unit;

            items.Add(new InvoiceItemRow
            {
                Description = ReportPrintProjector.Ascii(item?.Name ?? "(item)"),
                // Resolution order is the ONE rule (drift lock D7); a Rule-46 document leaves an undeclared
                // HSN column blank rather than printing a placeholder.
                HsnSac = ReportPrintProjector.Ascii(
                    Apex.Ledger.Reports.GstReportSupport.HsnSacOf(item) ?? string.Empty),
                QuantityText = ReportPrintProjector.Ascii(qtyText),
                RateText = IndianFormat.Amount(il.Rate),
                TaxableValue = il.Value,
            });
            totalGoodsValue += il.Value.Amount;
        }

        // ---------------------------------------------------------------- W0-10: the money is the POSTED money
        //
        // 🔴 EVERY FIGURE BELOW IS READ OFF THE VOUCHER'S OWN POSTED LEGS. Nothing here resolves a rate, a cess or a
        // routing from a live master, and nothing re-runs `GstService.ComputeInvoiceTax` — the same rule
        // `ProjectServiceInvoice` has always kept, so ONE projector no longer has TWO sources of truth for money.
        //
        // <b>What it cures.</b> The item pass used to take its head totals and its breakup rows from a LIVE
        // `ComputeInvoiceTax` over `gst.ResolveRate(item, valueLedger, voucher.Date)`, and its intra/inter routing from
        // the party's LIVE recorded State. Masters are editable long after a document is issued, so every ordinary
        // master edit silently rewrote the money on every already-issued invoice that touched it. Measured, each
        // through the shipped UI with no import: re-rating the item's HSN 18% → 28% reprinted a ₹60,539.81 demand
        // against a posted party debit of ₹55,810.14; declaring a 12% cess on an item that had posted none conjured
        // ₹5,675.61 of cess that is in no ledger; reclassifying an exempt line taxable taxed it retrospectively; and
        // editing the customer's State moved a posted CGST+SGST supply into an IGST column under a Place of Supply the
        // posted tax contradicts. The opposite direction was pinned to the paisa in
        // `BillOfSupplyRoutingTests.An_exempt_supply_that_posted_forward_tax_stays_a_tax_invoice`: a wholly exempt line
        // feeds the live computation nothing, so a voucher carrying ₹8,513.41 of POSTED forward tax printed a Grand
        // Total of ₹47,296.73 against a posted party debit of ₹55,810.14 — short by the whole tax.
        //
        // <b>Why POSTED is the right source and not a toss-up.</b> A tax invoice is evidence of a liability: CGST Rule
        // 46(m) requires "the amount of tax charged", i.e. the tax this supply actually bore, and CGST Act §34 changes
        // an issued figure by a credit/debit NOTE — a new document — never by reprinting the old one at today's rate.
        // The posted legs ARE that history; a live recomputation is a re-derivation of an issued document from mutable
        // data. Two earlier figures on this very path were moved onto the posted legs for exactly this reason (F4, the
        // ring-fenced cess; FIX-F10, the round-off); this is the last live one.
        //
        // <b>An ordinary reprint is byte-identical (ER-13), with ONE named exception</b>: the accept path builds the
        // posted legs from the SAME `ComputeInvoiceTax`, which stamps each group's own rate and taxable subtotal onto
        // its `GstLineTax`, so an untouched invoice reads back exactly what the live pass used to compute.
        //
        // 🔴 THE EXCEPTION, stated because the unqualified sentence here was FALSE and a reader would have relied on it
        // (W0-10 review, findings #2/#4/#9): a rate group whose tax is ZERO posts NO leg — `GstService.AddHead`
        // early-returns on `amount == 0m` — so a supply TAXABLE AT 0% leaves nothing to read back, and the "0% | value
        // | 0.00 | 0.00" breakup row the pre-W0-10 live resolve emitted is gone. No money moves (`TotalTaxable` sums
        // every line's value either way, all heads are zero, and the Grand Total still equals the posted party leg);
        // what is lost is the per-rate ROW. That is how the item pass CONVERGED on the service pass, which has never
        // emitted it (`ServiceAccountingInvoicePrintTests.ZeroRatedServiceInvoice_printsAsTaxInvoice` asserts
        // `Empty(TaxRows)` on a 0% LUT/export invoice, and `GstReportSupport.RateBreakupReconciles` is built on the
        // same premise). Deliberately NOT "fixed" on this pass alone — see the class doc's out-of-scope list.
        //
        // A wholly EXEMPT line never had a row on either pass and still has none: exempt is not zero-rated, and the
        // pre-W0-10 code skipped it explicitly (`if (!res.IsTaxable …) continue`).
        var money = ReadPostedMoney(voucher, livePartyInterState);
        // FIX-F10: the printed round-off is the one the voucher POSTED — never one invented at print time. Zero for
        // every voucher this app posts (no path posts a Round-Off leg), so the Grand Total equals the party leg; a
        // crafted/imported document that DOES carry one still states its own debt. Deliberately NOT part of
        // <see cref="ReadPostedMoney"/> — the service pass must keep printing none (see its own note).
        var roundOff = PostedRoundOff(company, voucher);

        // FIX-3, now reaching the item pass too. It could not fire here before: the reconciliation was fed the LIVE
        // routing, so `liveIsInterState == postedInterState` held by construction and it always returned the master
        // verbatim. Fed the POSTED routing it does its job — the printed State can no longer contradict the printed
        // tax. Where no forward tax was posted there is nothing to reconcile against, so the party's own State is
        // printed verbatim (F1) — which is what a bill of supply already did, byte-identically.
        var buyerState = money.PostedRouting is { } posted
            ? ConsistentBuyerStateCode(company, partyLedger, posted)
            : partyLedger?.PartyGst?.StateCode;
        // W0-1 (T0-7): which document is this, in law? A bill of supply carries NO tax breakup (CGST Rule 49 prescribes
        // no rate and no tax-amount particular), so the per-rate rows and the per-head totals are dropped.
        // <para>W0-10 — the suppressions are now BELT AND BRACES rather than the only protection, and they stay for
        // that reason. `taxRows` used to be built with the STATIC `GstService.ComputeLineTax`, which is NOT
        // composition-gated, so a composition dealer's document printed CGST/SGST that was never charged, never posted
        // and not in its own Grand Total (measured: a CGST 4,256.71 + SGST 4,256.70 row under a Grand Total of
        // 47,296.73) — the rows had to be dropped or the page contradicted itself. Reading the POSTED legs makes that
        // shape unreachable from this side: `IsBillOfSupply` returns false whenever the voucher carries forward tax or
        // cess (`GstReportSupport.CarriesForwardTax`), so a bill of supply's posted groups are empty and its posted
        // cess is zero, and every branch below can only ever zero a figure that is already zero. Do not delete them —
        // they are what makes that reasoning a local, checkable property of this method.</para>
        bool billOfSupply = IsBillOfSupply(company, voucher);
        return new InvoicePrintData
        {
            DocumentTitle = billOfSupply ? GstReportSupport.BillOfSupplyTitle : GstReportSupport.TaxInvoiceTitle,
            IsBillOfSupply = billOfSupply,
            TopDeclaration = billOfSupply ? TopDeclarationFor(company, voucher) : string.Empty,
            Seller = SellerBlock(company),
            Buyer = BuyerBlock(company, partyLedger, buyerState),
            InvoiceNumber = company.FormatVoucherNumber(voucher),
            InvoiceDateText = voucher.Date.ToString("dd-MM-yyyy", System.Globalization.CultureInfo.InvariantCulture),
            // Counterparty captured field (numbering §8): on a Sales tax invoice this is the buyer's "Reference No.".
            // Blank when none was captured ⇒ nothing prints ⇒ byte-identical (ER-13).
            ReferenceNo = ReportPrintProjector.Ascii(voucher.ReferenceNo ?? string.Empty),
            ReferenceCaption = ReferenceCaption(company.FindVoucherType(voucher.TypeId)),
            ReferenceDateText = voucher.ReferenceDate is { } rd
                ? rd.ToString("dd-MM-yyyy", System.Globalization.CultureInfo.InvariantCulture)
                : string.Empty,
            PlaceOfSupply = PlaceOfSupply(company, buyerState, money.InterState),
            IsInterState = money.InterState,
            Items = items,
            TaxRows = billOfSupply ? Array.Empty<InvoiceTaxRow>() : money.TaxRows,
            // The taxable/goods total = sum of ALL line values (rated + exempt/nil), so exempt lines are never
            // silently dropped from the Grand Total (GrandTotal = TotalTaxable + TotalTax + TotalCess + RoundOff).
            // On a bill of supply this IS the Grand Total — Rule 49(g)'s "value of supply".
            TotalTaxable = new Money(totalGoodsValue),
            TotalCgst = billOfSupply ? Money.Zero : money.TotalCgst,
            TotalSgst = billOfSupply ? Money.Zero : money.TotalSgst,
            TotalIgst = billOfSupply ? Money.Zero : money.TotalIgst,
            // FIX-1: the ring-fenced Compensation Cess is part of what the customer OWES. Omitting it made the printed
            // Grand Total understate the posted party leg by the whole cess (measured 1,180 printed vs 1,300 posted).
            // Zero on every cess-free invoice ⇒ byte-identical (ER-13). F4/W0-10: the POSTED cess legs, and ONLY them —
            // the live fallback for a voucher that posted none is gone, so a cess declared on the master after the sale
            // can no longer appear on the reprint. W0-1: a bill of supply bears no cess either — and `IsBillOfSupply`
            // refuses that classification whenever cess WAS posted, so this can only ever zero an already-zero figure.
            TotalCess = billOfSupply ? Money.Zero : money.TotalCess,
            RoundOff = roundOff,
            Narration = ReportPrintProjector.Ascii(voucher.Narration ?? string.Empty),
        };
    }

    // ---------------------------------------------------------------- service (Accounting Invoice) tax invoice

    /// <summary>
    /// Projects a Sales <b>accounting (service) invoice</b> into an <see cref="InvoicePrintData"/> GST tax invoice —
    /// the service mirror of the item pass above. The seller/buyer/place-of-supply blocks are the SAME master reads;
    /// what differs is where the lines and the tax come from:
    /// <list type="bullet">
    /// <item><b>Lines</b> — one printed row per service-income leg (<see cref="Gstr1.ServiceLegs"/>), described by its
    /// ledger, carrying the SAC that <c>Gstr1</c>'s Table-12 row and the e-invoice <c>HsnCd</c> already use
    /// (<see cref="Gstr1.ServiceSacOf"/>) so the document, the return and the payload cannot disagree, and valued at
    /// the posted leg amount. A service has neither a quantity nor a per-unit rate, so those cells print blank.</item>
    /// <item><b>Tax</b> — read verbatim off the posted <see cref="GstLineTax"/> legs
    /// (<see cref="GstReportSupport.ReadPostedRateGroups"/>), <b>never recomputed</b>. Re-rating the service ledger's master after
    /// posting therefore cannot move a printed figure: the invoice the customer holds always states the tax the GL
    /// actually carries.</item>
    /// <item><b>Intra vs inter</b> — decided by which HEAD was posted <b>whenever a forward tax leg exists</b>, not
    /// re-derived from the party's (editable) State, for the same reason; the printed buyer State and Place of Supply
    /// are then forced to AGREE with that posted routing (<see cref="ConsistentBuyerStateCode"/>), so editing the
    /// party's State after posting can no longer produce a document that states CGST+SGST under an inter-state Place
    /// of Supply. <b>When the voucher posts NO forward tax leg at all</b> — a zero-rated (LUT/export) or wholly-exempt
    /// supply — the buyer block, the Place of Supply and the routing all come from the party's recorded State instead
    /// (F1, below).</item>
    /// </list>
    ///
    /// <para><b>F1 (blocker, fixed) — "no tax posted" is NOT "intra-state".</b> Routing on
    /// <see cref="GstReportSupport.PostedForwardRouting"/> as a plain boolean made a no-tax invoice read "intra", and the FIX-3
    /// reconciliation then rewrote the document to match that phantom: measured on a ₹1,00,000 LUT export to a Gujarat
    /// party, the invoice printed the SELLER's own State as the BUYER's State <i>and</i> as the Place of Supply,
    /// BLANKED the buyer's real GSTIN (its 24… prefix "contradicted" the fabricated 27) and declared itself
    /// intra-state, so <c>InvoicePdf</c> rendered CGST+SGST 0.00 rows instead of IGST. Four false statements on an
    /// issued document — and precisely the two shapes FIX-0 exists to admit, so FIX-3 and FIX-0 collided head-on.
    /// The posted tax can only overrule the master where the posted tax SAYS something: with no forward tax leg there
    /// is nothing for the document to contradict, so the party's recorded State is authoritative and is printed in
    /// full (State, real GSTIN, Place of Supply) with the routing taken from it. Wherever forward tax legs DO exist
    /// the posted-tax-wins behaviour is untouched — that is what FIX-3 is for.</para>
    ///
    /// <para><b>Known behaviour (F8):</b> the printed Description and SAC are read LIVE from the service ledger's
    /// master (<see cref="Gstr1.ServiceSacOf"/>), not snapshotted onto the voucher — so renaming a ledger or changing
    /// its SAC changes what a HISTORICAL invoice reprints. This is deliberate and shared with GSTR-1 Table 12 and the
    /// e-invoice <c>HsnCd</c>, which read the same live master: snapshotting here alone would make the reprinted
    /// document disagree with the filed return. The MONEY is never live — tax and values are posted-only (above) — so
    /// no figure can move; only the descriptive text can. Snapshotting all three together is a separate change.</para>
    /// <para>No round-off is printed: the accounting-invoice accept path computes its tax with
    /// <c>applyInvoiceRoundOff: false</c> and posts no round-off leg, so <c>GrandTotal</c> = Σ service legs + Σ posted
    /// tax foots to the posted party leg exactly.</para>
    /// </summary>
    /// <param name="livePartyInterState">The routing derived from the party's RECORDED State vs the company home State
    /// (<c>GstService.IsInterState</c>). Used only when the voucher posted no forward tax leg at all (F1).</param>
    private static InvoicePrintData ProjectServiceInvoice(
        Company company, Voucher voucher, Apex.Ledger.Domain.Ledger? partyLedger, bool livePartyInterState)
    {
        var items = new List<InvoiceItemRow>();
        // Σ of EVERY service leg — taxed AND exempt/nil — so an exempt line is never silently dropped from the
        // Grand Total (the same rule the item pass keeps with `totalGoodsValue`).
        decimal totalServiceValue = 0m;
        foreach (var (ledger, value) in Gstr1.ServiceLegs(company, voucher))
        {
            items.Add(new InvoiceItemRow
            {
                Description = ReportPrintProjector.Ascii(ledger.Name),
                HsnSac = ReportPrintProjector.Ascii(Gstr1.ServiceSacOf(ledger) ?? string.Empty),
                QuantityText = string.Empty, // a service carries no quantity …
                RateText = string.Empty,     // … and no per-unit rate
                TaxableValue = new Money(value),
            });
            totalServiceValue += value;
        }

        // W0-10: the SAME read the item pass makes, from the same method — the rows, the four heads and the routing.
        var money = ReadPostedMoney(voucher, livePartyInterState);

        // F1: reconcile the printed State to the POSTED tax only where posted tax exists. With none, the party's own
        // recorded State is printed verbatim — the real State, the real GSTIN (ConsistentBuyerGstin keeps a GSTIN
        // whose prefix matches the State it is printed under) and the matching Place of Supply.
        var buyerState = money.PostedRouting is { } posted
            ? ConsistentBuyerStateCode(company, partyLedger, posted)
            : partyLedger?.PartyGst?.StateCode;
        // W0-1 (T0-7): the same §31(3)(c) routing as the item pass. On this path a bill of supply is necessarily
        // tax-free already (`IsBillOfSupply` refuses the classification when forward tax or cess was posted, and every
        // figure here is read from the POSTED legs), so the suppressions below can only ever drop empty rows and zero
        // figures — a taxed service invoice is byte-identical (ER-13).
        bool billOfSupply = IsBillOfSupply(company, voucher);
        return new InvoicePrintData
        {
            DocumentTitle = billOfSupply ? GstReportSupport.BillOfSupplyTitle : GstReportSupport.TaxInvoiceTitle,
            IsBillOfSupply = billOfSupply,
            TopDeclaration = billOfSupply ? TopDeclarationFor(company, voucher) : string.Empty,
            Seller = SellerBlock(company),
            Buyer = BuyerBlock(company, partyLedger, buyerState),
            InvoiceNumber = company.FormatVoucherNumber(voucher),
            InvoiceDateText = voucher.Date.ToString("dd-MM-yyyy", System.Globalization.CultureInfo.InvariantCulture),
            ReferenceNo = ReportPrintProjector.Ascii(voucher.ReferenceNo ?? string.Empty),
            ReferenceCaption = ReferenceCaption(company.FindVoucherType(voucher.TypeId)),
            ReferenceDateText = voucher.ReferenceDate is { } rd
                ? rd.ToString("dd-MM-yyyy", System.Globalization.CultureInfo.InvariantCulture)
                : string.Empty,
            PlaceOfSupply = PlaceOfSupply(company, buyerState, money.InterState),
            IsInterState = money.InterState,
            Items = items,
            TaxRows = billOfSupply ? Array.Empty<InvoiceTaxRow>() : money.TaxRows,
            TotalTaxable = new Money(totalServiceValue),
            TotalCgst = billOfSupply ? Money.Zero : money.TotalCgst,
            TotalSgst = billOfSupply ? Money.Zero : money.TotalSgst,
            TotalIgst = billOfSupply ? Money.Zero : money.TotalIgst,
            // FIX-1: the ring-fenced Compensation Cess, read off the POSTED cess legs like every other figure here —
            // part of what the customer owes, so it must reach the Grand Total (measured before this: printed 11,800
            // vs a posted party leg of 13,000). Zero on every cess-free invoice ⇒ byte-identical (ER-13).
            TotalCess = billOfSupply ? Money.Zero : money.TotalCess,
            RoundOff = Money.Zero,
            Narration = ReportPrintProjector.Ascii(voucher.Narration ?? string.Empty),
        };
    }

    // ---------------------------------------------------------------- W0-10: the ONE money read, shared by both passes

    /// <summary>Everything an invoice document says about GST, read off one voucher's POSTED legs: the per-rate
    /// breakup rows, the three head totals, the ring-fenced Compensation Cess, the intra/inter routing to print, and
    /// the raw three-valued <paramref name="PostedRouting"/> the buyer-block reconciliation needs.</summary>
    /// <param name="PostedRouting"><c>true</c> = the legs say inter-state, <c>false</c> = intra, <c>null</c> = the
    /// voucher posted no forward tax leg at all and therefore states NOTHING about routing (F1). Distinct from
    /// <paramref name="InterState"/>, which has already resolved that <c>null</c> against the party's live State — the
    /// buyer block must NOT reconcile against a routing the voucher never stated.</param>
    private readonly record struct PostedInvoiceMoney(
        IReadOnlyList<InvoiceTaxRow> TaxRows,
        Money TotalCgst, Money TotalSgst, Money TotalIgst, Money TotalCess,
        bool InterState, bool? PostedRouting);

    /// <summary>
    /// <b>W0-10 — THE single source of truth for money in this class.</b> Both <see cref="ProjectInvoice"/>'s item pass
    /// and <see cref="ProjectServiceInvoice"/> call this and nothing else; neither builds a tax figure of its own.
    ///
    /// <para><b>Why it is one method and not two identical blocks.</b> The two passes ran on two sources — the item one
    /// on a live <see cref="GstService.ComputeInvoiceTax"/>, this one's reads on the posted legs — and the printed
    /// Grand Total was therefore not the debt the general ledger recorded wherever a master had moved since posting.
    /// Converging them left ~18 identical lines in two places, and "one rule, many copies" is precisely the defect
    /// class that produced two disagreeing <c>IsBillOfSupply</c> predicates (W0-9), a fourth document-kind emitter in
    /// the POS receipt (W0-1b) and a fifth in the e-Way Part-A <c>docType</c> (W0-8). <b>Do not inline it back into
    /// either caller, and do not add a parameter that makes one pass read differently from the other.</b></para>
    ///
    /// <para>Every figure is a pure read of <see cref="EntryLine.Gst"/> metadata and posted leg amounts — no master is
    /// consulted, so no post-issue master edit can move a printed figure, and no live resolve can throw. The head
    /// exclusions come from <see cref="GstReportSupport.ReadPostedRateGroups"/> and
    /// <see cref="GstReportSupport.PostedCessTotal"/>, which the filed GSTR-1 rate rows already share — so the
    /// document, the return and the e-invoice payload cannot disagree about what this supply bore.</para>
    ///
    /// <para><b>The round-off is deliberately NOT here.</b> The item pass prints the POSTED round-off leg (FIX-F10);
    /// the service pass prints none, because <c>GstReportSupport.ServiceInvoiceFoots</c> is a security guard and
    /// admitting a round-off into it would hand crafted data a free plug for any shortfall. That divergence is a real
    /// rule, not an oversight, so it stays visible at each call site.</para>
    /// </summary>
    /// <param name="livePartyInterState">The routing the party's live master implies, used ONLY when the voucher posted
    /// no forward tax leg at all and there is therefore nothing posted for the document to contradict (F1).</param>
    private static PostedInvoiceMoney ReadPostedMoney(Voucher voucher, bool livePartyInterState)
    {
        var groups = GstReportSupport.ReadPostedRateGroups(voucher);
        var postedRouting = GstReportSupport.PostedForwardRouting(voucher);

        var taxRows = new List<InvoiceTaxRow>(groups.Count);
        decimal cgst = 0m, sgst = 0m, igst = 0m;
        foreach (var g in groups)
        {
            taxRows.Add(new InvoiceTaxRow
            {
                RateLabel = RateLabel(g.Rate),
                TaxableValue = new Money(g.Taxable),
                Cgst = new Money(g.Cgst),
                Sgst = new Money(g.Sgst),
                Igst = new Money(g.Igst),
            });
            cgst += g.Cgst; sgst += g.Sgst; igst += g.Igst;
        }

        return new PostedInvoiceMoney(
            taxRows, new Money(cgst), new Money(sgst), new Money(igst),
            GstReportSupport.PostedCessTotal(voucher),
            postedRouting ?? livePartyInterState, postedRouting);
    }

    /// <summary>
    /// The voucher's POSTED invoice round-off (FIX-F10), signed the way <c>InvoicePrintData.GrandTotal</c> adds it:
    /// positive when the round-off RAISED the party total to the rupee, negative when it shaved it. On a sale the party
    /// is a debit, so a Round-Off CREDIT raised the total; on a purchase the party is a credit, so the sides invert —
    /// the same convention <c>GstService.RoundOffSide</c> posts with.
    ///
    /// <para>Zero for every voucher this app posts: nothing in <c>src/</c> ever posts a Round-Off leg (the accept paths
    /// call <c>ComputeInvoiceTax</c> without <c>applyInvoiceRoundOff</c>, so <c>InvoiceTax.RoundOffLine</c> is always
    /// null and is read by nobody). Reading the posted leg rather than hardcoding zero is what makes the rule "print
    /// the round-off that was POSTED" rather than "print no round-off", so a crafted or imported document that genuinely
    /// settles at the rupee still prints a Grand Total equal to its party leg.</para>
    ///
    /// <para><b>Deliberately NOT used by the service path.</b> <c>GstReportSupport.ServiceInvoiceFoots</c> is a security guard,
    /// and admitting a Round-Off leg into it would hand crafted data a free plug for any shortfall. A service voucher
    /// carrying one simply fails to foot and prints as the plain voucher it is — the conservative direction.</para>
    /// </summary>
    private static Money PostedRoundOff(Company company, Voucher voucher)
    {
        if (company.FindLedgerByName(GstService.RoundOffLedgerName)?.Id is not Guid roundOffId) return Money.Zero;
        var partyIsCredit = company.FindVoucherType(voucher.TypeId)?.BaseType == VoucherBaseType.Purchase;

        var total = 0m;
        foreach (var line in voucher.Lines)
        {
            if (line.LedgerId != roundOffId) continue;
            var raisedThePartyTotal = partyIsCredit ? line.Side == DrCr.Debit : line.Side == DrCr.Credit;
            total += raisedThePartyTotal ? line.Amount.Amount : -line.Amount.Amount;
        }
        return new Money(total);
    }

    /// <summary>
    /// The buyer's State code to PRINT, forced to agree with the tax the voucher actually posted (FIX-3).
    ///
    /// <para>The tax on an issued document is history; the party's State is a LIVE, editable master. Read
    /// independently they can contradict each other, and the printed document then contradicts ITSELF: edit a party
    /// from Maharashtra to Gujarat after posting an intra-state sale and the invoice reprints CGST+SGST (correctly —
    /// the posted tax is never recomputed) under an INTER-state Place of Supply and a Gujarat buyer. No such document
    /// is valid: CGST+SGST asserts the place of supply IS the supplier's State.</para>
    ///
    /// <para>So: when the live State agrees with the posted routing — the ordinary case, and every invoice whose
    /// party was never edited — it is printed unchanged (byte-identical, ER-13). When it contradicts:</para>
    /// <list type="bullet">
    /// <item><b>posted INTRA</b> ⇒ the buyer was in the company's home State (that is what CGST+SGST means), so the
    /// home State is printed. Fully recoverable.</item>
    /// <item><b>posted INTER</b> ⇒ the buyer was in SOME other State, but which one is not recoverable from the GL
    /// (IGST does not record it), so nothing is printed rather than a home-State value that would contradict the
    /// IGST. Blank is a Rule-46 omission; a self-contradicting document is a Rule-46 <i>falsehood</i>.</item>
    /// </list>
    /// <para>The tax itself is never recomputed here — only the descriptive State is reconciled to it.</para>
    /// </summary>
    private static string? ConsistentBuyerStateCode(
        Company company, Apex.Ledger.Domain.Ledger? party, bool postedInterState)
    {
        var live = party?.PartyGst?.StateCode;
        var home = company.Gst?.HomeStateCode;
        var liveIsInterState =
            !string.IsNullOrWhiteSpace(live) && !string.IsNullOrWhiteSpace(home) &&
            !string.Equals(live.Trim(), home.Trim(), StringComparison.OrdinalIgnoreCase);

        if (liveIsInterState == postedInterState) return live;   // consistent — print the master verbatim
        return postedInterState ? null : home;                   // intra ⇒ the home State; inter ⇒ unrecoverable
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>The counterparty-reference label per base type (numbering §8): "Supplier Invoice No." on a Purchase
    /// (the other party's number is the supplier's invoice number), "Reference No." on every other type.</summary>
    private static string ReferenceCaption(VoucherType? type) =>
        type?.BaseType == VoucherBaseType.Purchase ? "Supplier Invoice No." : "Reference No.";

    /// <summary>The rate label for the breakup group (e.g. 1800 bp -> "18%"); trims a trailing ".00".</summary>
    private static string RateLabel(int bp)
    {
        decimal pct = bp / 100m;
        var s = pct.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        return s + "%";
    }

    private static string CompanyDisplayName(Company company) =>
        string.IsNullOrWhiteSpace(company.MailingName) ? company.Name : company.MailingName;

    private static InvoicePartyBlock SellerBlock(Company company) => new()
    {
        Name = ReportPrintProjector.Ascii(CompanyDisplayName(company)),
        AddressLines = SplitAddress(company.Address),
        Gstin = ReportPrintProjector.Ascii(company.Gst?.Gstin ?? string.Empty),
        StateText = StateText(company.Gst?.HomeStateCode),
    };

    /// <summary>
    /// The printed invoice's recipient block. The name is the party's <b>Mailing Name</b> when one was captured
    /// (Tally's "Mailing Name (auto, editable)" convention), else the ledger's own Name; the address lines come
    /// from the WI-4 Mailing Details block through the same <see cref="SplitAddress"/> the seller uses.
    /// <para>Before v45 this hardcoded <c>Array.Empty&lt;string&gt;()</c> with a comment explaining that a party
    /// ledger had no address field — so every invoice this app printed carried a blank recipient address. The
    /// field now exists, and <c>InvoicePdf</c> already renders whatever lines it is given.</para>
    /// </summary>
    /// <param name="stateCode">The State code to print — <see cref="ConsistentBuyerStateCode"/>'s verdict, which is
    /// the party's own live code except where it would contradict the posted tax (FIX-3).</param>
    private static InvoicePartyBlock BuyerBlock(
        Company company, Apex.Ledger.Domain.Ledger? party, string? stateCode) => new()
    {
        Name = ReportPrintProjector.Ascii(
            string.IsNullOrWhiteSpace(party?.Mailing?.MailingName)
                ? party?.Name ?? string.Empty
                : party!.Mailing!.MailingName!),
        AddressLines = SplitAddress(BuyerAddressText(party)),
        Gstin = ReportPrintProjector.Ascii(ConsistentBuyerGstin(party, stateCode)),
        StateText = StateText(stateCode),
    };

    /// <summary>
    /// The buyer GSTIN to print (FIX-3). A GSTIN's first two characters ARE its State code, so re-stating a party's
    /// GSTIN on a historical invoice whose printed State had to be reconciled to the posted tax would put the
    /// contradiction straight back on the document — "GSTIN: 24…" under "State: Maharashtra (27)". The check only
    /// runs when the State actually had to be overridden (<paramref name="stateCode"/> differs from the party's own),
    /// so an untouched invoice prints its GSTIN verbatim (ER-13); and when the party's GSTIN still matches the
    /// reconciled State — only the State field was edited — the GSTIN is the historically correct one and is kept.
    /// </summary>
    private static string ConsistentBuyerGstin(Apex.Ledger.Domain.Ledger? party, string? stateCode)
    {
        var gstin = party?.PartyGst?.Gstin;
        if (string.IsNullOrWhiteSpace(gstin)) return string.Empty;

        var live = party?.PartyGst?.StateCode;
        // No override happened ⇒ nothing to reconcile (the overwhelmingly common path).
        if (string.Equals(live?.Trim(), stateCode?.Trim(), StringComparison.OrdinalIgnoreCase)) return gstin;

        // Overridden: keep the GSTIN only if its own State prefix agrees with the State we are printing.
        if (string.IsNullOrWhiteSpace(stateCode) || gstin.Trim().Length < 2) return string.Empty;
        return gstin.Trim().StartsWith(stateCode.Trim(), StringComparison.OrdinalIgnoreCase) ? gstin : string.Empty;
    }

    /// <summary>
    /// The buyer's printable address text: the Mailing Details address, with the PIN code appended as its own
    /// final line when one was captured (the CA's "along with PIN code" — a recipient block without it is not a
    /// complete postal address). Blank when the party has no mailing block, which reproduces the pre-v45 output.
    /// </summary>
    private static string? BuyerAddressText(Apex.Ledger.Domain.Ledger? party)
    {
        var mailing = party?.Mailing;
        if (mailing is null) return null;

        var lines = new List<string>(mailing.AddressLines);
        if (!string.IsNullOrWhiteSpace(mailing.Country)) lines.Add(mailing.Country.Trim());
        if (!string.IsNullOrWhiteSpace(mailing.Pincode)) lines.Add("PIN: " + mailing.Pincode.Trim());
        return lines.Count == 0 ? null : string.Join("\n", lines);
    }

    /// <summary>Place of supply = the buyer's State — the reconciled one (<see cref="ConsistentBuyerStateCode"/>), so
    /// it can never name a State the posted tax contradicts (FIX-3). Falls back to the company home State for a B2C
    /// recipient with no recorded State (DP-8) — but only on an INTRA-state supply, where the home State is what
    /// CGST+SGST already asserts; on an inter-state supply a home-State fallback would contradict the posted IGST, so
    /// the field is left blank instead.</summary>
    private static string PlaceOfSupply(Company company, string? buyerStateCode, bool postedInterState)
    {
        var code = buyerStateCode;
        if (string.IsNullOrWhiteSpace(code) && !postedInterState) code = company.Gst?.HomeStateCode;
        return StateText(code);
    }

    /// <summary>"West Bengal (19)" for a recognised code; blank when unset/unrecognised.</summary>
    private static string StateText(string? code)
    {
        var st = IndianState.FromCode(code);
        return st is null ? string.Empty : ReportPrintProjector.Ascii($"{st.Name} ({st.Code})");
    }

    /// <summary>Splits a free-text address into printable lines (newline- or comma-separated); empty when blank.</summary>
    private static IReadOnlyList<string> SplitAddress(string? address)
    {
        if (string.IsNullOrWhiteSpace(address)) return Array.Empty<string>();
        var parts = address
            .Replace("\r\n", "\n").Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length > 0
            ? parts.Select(ReportPrintProjector.Ascii).ToArray()
            : Array.Empty<string>();
    }
}
