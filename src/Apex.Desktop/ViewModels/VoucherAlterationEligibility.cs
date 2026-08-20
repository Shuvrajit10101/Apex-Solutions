using System;
using System.Linq;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;

namespace Apex.Desktop.ViewModels;

/// <summary>
/// 🔴 <b>Phase 10.11 S5b — the refusal predicate for <see cref="VoucherEntryViewModel.ForAlter"/>.</b> Answers one
/// question about one posted voucher: <i>can this screen rebuild it from its posted lines?</i> Returns <c>null</c>
/// when it can (design §6.6a calls that shape <b>SIMPLE</b>), and a <b>named, family-specific message</b> when it
/// cannot.
///
/// <para><b>Why a named refusal and not a silent no-op.</b> ORCHESTRATOR RULING 1: <i>"a silent no-op is the failure
/// mode being avoided"</i>. A test asserting "nothing happened" passes for a silent no-op too, so every non-SIMPLE
/// family gets its own sentence and its own test. Nothing here returns <c>false</c>, <c>null</c> or an empty
/// string as a refusal.</para>
///
/// <para>🔴 <b>THE ORDER OF THE CHECKS IS LOAD-BEARING — type flags FIRST, base kind SECOND</b> (§6.6a.2). The
/// product already knows this: <c>MainWindowViewModel.PickAddVoucherType</c> is written in exactly this order,
/// with its own comment saying why — <i>"Types whose identity means a DIFFERENT SCREEN, not just a different
/// series — checked before the base switch, because each of these shares its base kind with an ordinary type."</i>
/// A predicate that switched on <see cref="VoucherBaseType"/> alone would hand a POS bill, a GST challan and a
/// manufacturing journal to the plain Dr/Cr grid.</para>
///
/// <para>🔴 <b>AND THE PREDICATE IS A UNION, NOT A TAG SCAN</b> (§6.6a.6 answer 1). A filter that looked only for
/// <c>EntryLine.Gst</c> / <c>.Tds</c> / <c>.Tcs</c> is <b>provably insufficient</b>: five families carry no tagged
/// line at all and still fail to round-trip, because <c>Accept</c> had an <b>off-line side effect</b> — a record
/// added to or replaced on <see cref="Company"/> that no test of <see cref="EntryLine"/> contents can see. They are
/// enumerated in <see cref="OffLineSideEffectRefusal"/>. This is why the checks below reach into
/// <c>Company.AdvanceReceipts</c>, <c>Company.CreditDebitNoteLinks</c> and the challan links rather than only into
/// the voucher.</para>
///
/// <para><b>What is NOT decided here.</b> Master drift and the forex rate's precision cannot be answered from the
/// posted voucher alone — they depend on the gate a rehydrated <see cref="VoucherLineViewModel"/> actually opens
/// with today. Those two refusals live inside <see cref="VoucherEntryViewModel.ForAlter"/>, where the real gate can
/// be read off the real line rather than re-implemented (and therefore cannot drift out of step with
/// <c>SyncBillWise</c> / <c>SyncCostApplicable</c> / <c>SyncBankLine</c> / <c>SyncForexLine</c>).</para>
/// </summary>
public static class VoucherAlterationEligibility
{
    /// <summary>
    /// The refusal for altering <paramref name="voucherId"/> on the plain voucher-entry screen, or <c>null</c> when
    /// the voucher is one of the eleven SIMPLE shapes §6.6a.3 enumerates.
    /// </summary>
    public static string? RefusalFor(Company company, Guid voucherId)
    {
        ArgumentNullException.ThrowIfNull(company);

        // ---------------------------------------------------------------- (0) does it exist, and in WHICH aggregate
        if (company.FindVoucher(voucherId) is not { } voucher)
        {
            // §6.6a.4 — all twelve inventory base kinds post an InventoryVoucher into a DIFFERENT list on Company.
            // LedgerService.Replace cannot see them and says so by name; a caller that lands here deserves the same
            // answer rather than a bare "not found", which is indistinguishable from a mistyped Guid.
            // 🔴 Written for the OPERATOR, not for the reader of this file (finding L3-07). The mechanism — a
            // different aggregate that LedgerService.Replace cannot reach, so the refusal is architectural rather
            // than a judgement about the voucher's shape — belongs in this comment; an accountant deciding what to
            // do next needs the family, the reason in their own vocabulary, and the screen to go to.
            if (company.InventoryVouchers.Any(iv => iv.Id == voucherId))
                return "This is an inventory voucher — it records goods moving, not double entry — so the "
                     + "accounting entry screen cannot re-open it. Alter it on the inventory voucher screen.";

            return "That voucher is no longer in this company's books — it may have been deleted since the report "
                 + "was drawn. Re-open the report and try again.";
        }

        if (company.FindVoucherType(voucher.TypeId) is not { } type)
            return "This voucher's type is missing from the company, so the entry screen cannot be re-opened for "
                 + "it. Restore the voucher type first.";

        return RefusalFor(company, voucher, type);
    }

    /// <summary>
    /// 🔴 <b>The refusals that are about the BOOK, not about which grid re-keys the voucher</b> — the off-line
    /// side effects (an advance record, a §34 link, a challan link), the live IRN, and the provisional-state
    /// vector's shape. Every alteration door must ask these, whatever screen it opens.
    ///
    /// <para>🔴 <b>WHAT IS SINGLE-SOURCED HERE IS THE ARMS, NOT THE COMPOSITION — and the doc comment that used to
    /// claim otherwise was false</b> (review finding L3-booklevel-refusal-has-one-consumer-not-two). This method has
    /// exactly ONE caller in the tree, <see cref="PosAlterationEligibility"/>;
    /// <see cref="RefusalFor(Company, Voucher, VoucherType)"/> chains the same three private methods ITSELF rather
    /// than calling this, because its book-level arms are INTERLEAVED with screen-level ones (§6.6a.2 puts the type
    /// flags and the base kind first, and the entry-mode and derived-leg arms sit between them) — substituting this
    /// call there would reorder which sentence the operator reads. Both doors therefore reach the same three
    /// methods and demonstrably return the same answers; what is duplicated is only the ENUMERATION of which three
    /// count as book-level.</para>
    ///
    /// <para>🔴 <b>So the property to keep true when a fourth book-level refusal is added</b> is that it goes
    /// INSIDE one of the three methods below, where both doors already reach it. Adding a fourth private method and
    /// wiring it only into <c>RefusalFor</c>'s chain is the one divergence this split cannot prevent, and the POS
    /// door would never ask it.</para>
    /// </summary>
    public static string? BookLevelRefusalFor(Company company, Voucher voucher, VoucherType type)
    {
        ArgumentNullException.ThrowIfNull(company);
        ArgumentNullException.ThrowIfNull(voucher);
        ArgumentNullException.ThrowIfNull(type);

        return OffLineSideEffectRefusal(company, voucher)
            ?? StatutoryDocumentRefusal(company, voucher)
            ?? ProvisionalShapeRefusal(voucher, type);
    }

    /// <summary>The predicate proper, over an already-resolved voucher and its type.</summary>
    public static string? RefusalFor(Company company, Voucher voucher, VoucherType type)
    {
        ArgumentNullException.ThrowIfNull(company);
        ArgumentNullException.ThrowIfNull(voucher);
        ArgumentNullException.ThrowIfNull(type);

        return SpecialisedTypeRefusal(type)               // §6.6a.2 — type flags FIRST
            ?? BaseKindRefusal(type, voucher)             // then the base kind
            ?? OffLineSideEffectRefusal(company, voucher) // then the five untagged families
            ?? StatutoryDocumentRefusal(company, voucher)
            ?? EntryModeRefusal(voucher, type)
            ?? DerivedLegRefusal(company, voucher)
            ?? ProvisionalShapeRefusal(voucher, type);
    }

    // ------------------------------------------------------------------ §6.6a.2 — the type flags, checked first

    /// <summary>
    /// The specialised voucher TYPES that share a base kind with an ordinary one (§6.6a.1 layer L5). Every one of
    /// these is posted by a service and was never keyed on a Dr/Cr grid, so there is nothing for the screen to
    /// rebuild from.
    /// </summary>
    private static string? SpecialisedTypeRefusal(VoucherType type)
    {
        // A Manufacturing Journal is a StockJournal-based INVENTORY voucher, so it would also be caught by the
        // architectural refusal above — but §6.6a.4 corrects §6.6 on exactly this point: it is the FOURTH
        // InventoryVoucher entry screen, not a fifth family beside them, and it is named here so the refusal an
        // operator reads names the screen they came from.
        if (type.IsManufacturingJournal)
            return "A Manufacturing Journal is keyed as a finished good plus its components on its own screen, and "
                 + "it posts a pure-stock inventory voucher (it books no double-entry at all). The accounting entry "
                 + "screen cannot re-open it.";

        // Row 19 — POS. Unlike the inventory screens this one IS reachable by Replace (PosBillingViewModel posts
        // through LedgerService.Post into Company.Vouchers), so the refusal must be EXPLICIT rather than
        // architectural: without it a POS bill would open in the plain Dr/Cr grid and its tender split would be
        // rebuilt as ordinary debits.
        //
        // 🔴 S5e — THE REFUSAL STANDS AND ITS REASON IS UNCHANGED; only its LAST SENTENCE moved, because there is
        // now a door that does open. A POS bill IS alterable — on the POS billing screen, which is the only screen
        // that keys a tender split — through <see cref="PosAlterationEligibility"/> and
        // <c>PosBillingViewModel.ForAlter</c>. What this predicate answers is narrower and still No: can the
        // ACCOUNTING ENTRY SCREEN rebuild it? Neither of its grids has a tender panel (the item grid derives a
        // single party debit, which is precisely the leg a POS bill does not have), so re-accepting here would
        // rebuild the tender split as one ordinary customer debit. The shell routes a POS voucher to the POS
        // screen before it ever reaches this predicate; an operator who lands here anyway is told where to go.
        if (type.IsPosSales)
            return "This is a POS bill. Its single customer debit is a split of payment tenders, and neither grid "
                 + "on the accounting entry screen can express them, so re-accepting it here would rebuild the "
                 + "tender split as one ordinary customer debit. Alter it on the POS billing screen.";

        // Row 5 — the GST / TDS / TCS challan. Posted by GstDepositService / TdsDepositService / TcsDepositService
        // with lines no entry screen ever keyed, and §3.3 records that ChallanReconciliation self-heals on cancel
        // and delete but NOT on amend.
        if (type.IsStatPaymentType)
            return "This is a statutory deposit (challan) payment, posted by the deposit screen rather than keyed "
                 + "on a Dr/Cr grid. Its challan record freezes the challan number, BSR code, deposit date and "
                 + "amount, and the challan reconciliation does not re-derive them when a voucher is amended.";

        // Row 6 — the Rule-52 RCM payment voucher: a Payment base kind wearing a flag, with an RcmDocument series
        // hung off it (§3.3).
        if (type.IsRcmPaymentVoucherType)
            return "This is a reverse-charge (Rule 52) payment voucher, raised by the reverse-charge engine "
                 + "alongside its own document series. It was never keyed on a Dr/Cr grid, so the entry screen "
                 + "cannot rebuild it.";

        // Row 14 — Rule-88A set-off / ITC reversal, posted by GstSetOffService / GstReversalService.
        if (type.IsGstStatAdjustmentType)
            return "This is a GST statutory adjustment (set-off / ITC reversal), computed for a return period and "
                 + "posted by the GST engine. Re-run the period rather than amending the voucher.";

        return null;
    }

    // ------------------------------------------------------------------ the base kind

    private static string? BaseKindRefusal(VoucherType type, Voucher voucher)
    {
        // §6.6a.4 — the twelve inventory base kinds. Reached here only for the (impossible today) shape of an
        // accounting Voucher carrying an inventory base kind; the ordinary route is the aggregate check above.
        if (VoucherEffects.IsInventoryBaseType(type.BaseType))
            return $"A {type.Name} is an inventory voucher — it records goods moving, not double entry — so the "
                 + "accounting entry screen cannot re-open it. Alter it on the inventory voucher screen.";

        // Row 30 — Payroll. Every line carries EntryLine.Payroll written by PayrollComputationService, and the
        // EntryLine constructor enforces payroll.Amount == amount, so a Dr/Cr grid cannot express one leg of it.
        // It is keyed as a period plus an employee set, never as a grid.
        if (type.BaseType == VoucherBaseType.Payroll || voucher.Lines.Any(l => l.HasPayroll))
            return "A payroll voucher is computed from a payroll period and an employee set on the payroll voucher "
                 + "screen — every line carries the employee and pay head that produced it. The plain Dr/Cr grid "
                 + "cannot express that, so re-accepting it here would strip the payslip detail off every line.";

        // Attendance is the 24th enum member and posts AttendanceEntry rows, never a Voucher. If it is ever seeded
        // and ever posts one, this is the sentence that appears rather than a silent open (§6.6a.8).
        if (type.BaseType == VoucherBaseType.Attendance)
            return "An attendance / production entry is recorded on the attendance screen, not as a Dr/Cr voucher, "
                 + "so the accounting entry screen cannot re-open it.";

        return null;
    }

    // ------------------------------------------------------------------ §6.6a.6 — the five UNTAGGED families

    /// <summary>
    /// 🔴 <b>The subtle half of the predicate.</b> Each of these carries <b>no</b> <c>Gst</c> / <c>Tds</c> /
    /// <c>Tcs</c> line, so a tag scan passes it — yet none round-trips, because <c>Accept</c> added or replaced a
    /// record ON THE COMPANY that re-accepting would double, orphan or silently release.
    /// </summary>
    private static string? OffLineSideEffectRefusal(Company company, Voucher voucher)
    {
        var id = voucher.Id;

        // Rows 8 and 9 — the advance RECEIPT itself. AdvanceReceiptService.BuildAdvanceReceipt registers a
        // GstAdvanceReceipt against this voucher id, and gst_advance_receipts.receipt_voucher_id is
        // NOT NULL REFERENCES vouchers(id). The two arms are separated because they fail DIFFERENTLY.
        if (company.AdvanceReceipts.FirstOrDefault(a => a.ReceiptVoucherId == id) is { } booked)
            return booked.IsService
                // Row 8 — DEFER. Its Output legs ARE tagged, so the tag filter would have caught it too; the
                // suspense debit and the record registration would not have been.
                ? "This receipt books a GST advance on a SERVICE supply (Rule 50): its output-tax legs and "
                + "suspense release are derived by the advance engine, not keyed. Altering an advance receipt "
                + "arrives with the tax re-stamp in a later slice."
                // Row 9 — REFUSE, and the second hole in the tag filter. A GOODS advance is de-taxed
                // (Notn 66/2017), so the engine appends NO tax lines: posted lines genuinely equal keyed lines
                // and it passes every tag test, while the record still points at this voucher.
                : "This receipt books a GST advance on a GOODS supply. It is de-taxed (Notification 66/2017), so "
                + "it carries no tax line at all and looks like an ordinary receipt — but an advance record "
                + "points at it, and re-accepting from the grid alone would leave that record naming a voucher "
                + "that no longer claims it.";

        // Row 4 — the Rule-51 refund Payment. Refund REPLACES the record on the company, and on a SERVICE advance
        // BuildAdvanceReversalPair's own doc comment states the reversal legs carry NO GstLineTax.
        //
        // 🔴 CORRECTION (finding L3-04), because the row's stated evidence is wrong for half the family:
        // BuildAdvanceReversalPair's FIRST statement is `if (!advance.IsService || advance.AdvanceTax.Amount == 0m)
        // return Array.Empty<EntryLine>()`. A GOODS advance is de-taxed (Notn 66/2017), so its refund voucher
        // carries NO engine line at all — the off-line effect is the record replacement alone. The verdict survives
        // anyway, and only because this arm keys on RefundVoucherId, which IS set for a goods advance: the record
        // points straight at the refund voucher, so no shape proxy is needed here.
        if (company.AdvanceReceipts.Any(a => a.RefundVoucherId == id))
            return "This payment refunds a GST advance (Rule 51). The advance record names it, and on a service "
                 + "advance its reversal legs carry no tax tag at all, so they are indistinguishable from keyed "
                 + "lines on the grid; re-accepting would drop them and the advance's output tax would silently "
                 + "return to the books while the record still reads refunded.";

        // Row 13 — the adjustment Journal, selected BY SHAPE, because nothing on the record names it.
        //
        // 🔴 THIS ARM REPLACES A WRONG ONE (finding L1-01, a measured BLOCKER). The predicate shipped as
        // `AdvanceReceipts.Any(a => a.AdjustedAgainstInvoiceVoucherId == id)` on the stated belief that the field
        // names the adjusting journal. It does not: AdvanceReceiptService.AdjustAgainstInvoice stores
        // `adjustedAgainstInvoiceVoucherId: invoiceVoucherId`, and its only caller passes SelectedAdvanceInvoice —
        // a picker built from Company.Vouchers filtered to BaseType == Sales. Gstr1 corroborates it, reporting the
        // 11B row in the window of that id. So the old arm refused the SALES INVOICE (with a sentence beginning
        // "This journal…") and left the adjusting Journal OPEN — where removing its engine-built release pair
        // declared the advance's output tax a second time and stranded the suspense, with the record still reading
        // adjusted and the advance gone from every picker.
        //
        // The shape that DOES select it: the release pair is Dr Output {head} / Cr "Output Tax on Advances", so a
        // line to the advance-tax suspense ledger is the discriminator. It also covers a hand-keyed voucher on that
        // engine-owned ledger, which is the conservative direction.
        //
        // 🔴 AND ITS LIMIT, STATED (finding L3-04): a GOODS advance's adjusting Journal carries NO engine line, so
        // this arm cannot see it — and it does not need to. With no appended line and no record mutation on the
        // accept path (AcceptAlteration never calls AdjustAgainstInvoice), the voucher's posted lines ARE its keyed
        // lines and the record is untouched by Replace. §6.6a row 13a records the measurement.
        if (company.FindLedgerByName(GstService.AdvanceTaxSuspenseLedgerName) is { } suspense
            && voucher.Lines.Any(l => l.LedgerId == suspense.Id))
            return "This voucher moves the GST advance-tax suspense ('Output Tax on Advances') — the balance the "
                 + "advance engine debits when a service advance is received and credits when the advance is "
                 + "released against an invoice. Those release legs carry no tax tag, so the grid cannot tell them "
                 + "from keyed lines; re-accepting would drop the release and the advance's output tax would stand "
                 + "twice while the record still reads settled.";

        // Row 16b — the SALES INVOICE an advance was adjusted against (finding L3-05). The old arm's PREDICATE was
        // right about what it matched and wrong about what it said; this is the correct sentence for it, and the
        // refusal is a real one: AdjustAgainstInvoice re-read this invoice's posted taxable value
        // (GstReportSupport.InvoiceTaxableValue) to prove the advance was fully consumed, and GSTR-1 11B's figures
        // were frozen against it. A plain-grid Sales invoice is otherwise SIMPLE (row 16a), so without this arm the
        // taxable value behind a released advance could be amended with nothing reporting it.
        if (company.AdvanceReceipts.Any(a => a.AdjustedAgainstInvoiceVoucherId == id))
            return "A GST advance was released against this invoice. The advance engine tested the advance against "
                 + "this invoice's taxable value and froze the GSTR-1 11B figures on it, and it does not re-derive "
                 + "them when a voucher is amended — so altering the invoice would leave the release standing "
                 + "against a figure the book no longer holds.";

        // Rows 25 and 27 — a §34 credit/debit note. RegisterSection34Link mints a fresh Guid.NewGuid() link on
        // EVERY Accept, so a re-accept adds a SECOND GstCreditDebitNoteLink for one note. §3.3 classes the link
        // CARRY — and CARRY means do not rebuild it, which the entry path does unconditionally.
        if (company.CreditDebitNoteLinks.Any(l => l.CdnVoucherId == id))
            return "This note carries a §34 credit/debit-note link to its original invoice. The entry screen mints "
                 + "a fresh link on every accept, so re-accepting would leave the note carrying two links and "
                 + "GSTR-1 would report it twice.";

        // Defensive, and NOT redundant with the IsStatPaymentType refusal above: the link is keyed on
        // (ChallanId, VoucherId) and nothing structurally confines it to a stat-payment TYPE. §3.3 records that
        // ChallanReconciliation self-heals on cancel and delete but not on amend.
        //
        // 🔴 ALL FOUR CHALLAN COLLECTIONS, not two (finding L1-05). GstChallan.VoucherId (non-null) and
        // GstDrc03.VoucherId (nullable) are equally bare voucher ids with exactly the same reachability argument:
        // GstDepositService creates both against the stat-payment type today, but ImportPlan builds both straight
        // from canonical XML with an arbitrary voucher id. Keeping half of a defence whose own stated rationale
        // covers all four is the shape that ships a hole.
        if (company.ChallanVoucherLinks.Any(l => l.VoucherId == id)
            || company.TcsChallanVoucherLinks.Any(l => l.VoucherId == id)
            || company.GstChallans.Any(c => c.VoucherId == id)
            || company.GstDrc03s.Any(d => d.VoucherId == id))
            return "This voucher is linked to a GST/TDS/TCS challan, which freezes the challan number, BSR code, "
                 + "deposit date and amount. The challan reconciliation does not re-derive those when a voucher "
                 + "is amended, so altering it would leave the reconciliation reporting a figure the book no "
                 + "longer holds.";

        return null;
    }

    // ------------------------------------------------------------------ ORCHESTRATOR RULING 2

    /// <summary>
    /// A <c>Generated</c> e-invoice is refused (design §3.3, RULING 2): the IRN was signed by the IRP over the
    /// document as it stood, an IRN cannot be re-derived, and the app's only content check compares the document
    /// NUMBER — which an amount-only amendment leaves untouched, so nothing would report the divergence. A
    /// <c>Pending</c> record was never sent to the portal and is warn-and-proceed (raised by <c>Replace</c>), as is
    /// an active e-Way bill.
    /// </summary>
    private static string? StatutoryDocumentRefusal(Company company, Voucher voucher) =>
        company.EInvoiceRecords.Any(
            r => r.SourceVoucherId == voucher.Id && r.Status == EInvoiceStatus.Generated)
            ? "This voucher carries a live IRN issued by the e-invoice portal against the document as it stands. "
            + "An IRN cannot be re-derived, and the e-invoice reconciliation compares only the document number, so "
            + "an amendment here would go unreported. Cancel the IRN at the portal first, then raise a fresh "
            + "document."
            : null;

    // ------------------------------------------------------------------ the two invoice entry modes

    private static string? EntryModeRefusal(Voucher voucher, VoucherType type)
    {
        // Rows 17 and 22 — the item invoice. 🔴 S5e NARROWED THIS ARM FROM THE WHOLE FAMILY TO SALES, and BOTH of
        // the reasons the blanket sentence gave were re-measured rather than inherited:
        //
        //   • THE BATCH SPLIT IS AN AMBIGUITY, NOT AN IRRECOVERABILITY. TryAppendSplitBatchLines does post one item
        //     line per batch, so N posted rows fit two keyed states (one split row, or N separate rows) — but the
        //     guard directly above it REFUSES any split whose per-batch values do not foot to the line value, and
        //     forces Billed == Actual on it. Every split that actually posts is therefore VALUE-IDENTICAL to the
        //     N-separate-rows keying, so rehydrating FLAT is a true inverse of the posted shape. What is lost is
        //     the operator's knowledge that the sub-screen was used, and no figure depends on it.
        //
        //   • THE DISCOUNT ONLY BITES SALES. ShowPriceLevelSelector is
        //     `IsItemInvoice && CanBeItemInvoice && !IsPurchaseInvoice && _company.EnableMultiplePriceLevels`, and
        //     it is the SOLE writer of InventoryVoucherLineViewModel.ShowDiscount (AddInventoryLine seeds it,
        //     SyncPriceLevelOnLines maintains it, and nothing else in src/ assigns it — PosBillingViewModel builds
        //     its item rows without touching it, so it defaults false there). ParsedDiscountPercent returns 0
        //     whenever ShowDiscount is false, so EffectiveRate == ParsedRate EXACTLY on every Purchase item
        //     invoice and on every POS bill, unconditionally and with no history dependence: the posted rate IS
        //     the keyed rate.
        //
        // 🔴 AND THE SALES ARM IS NOT NARROWED FURTHER TO "the price-level flag is on", although only those
        // invoices can actually carry an unrecoverable list rate. Company.EnableMultiplePriceLevels is a LIVE
        // flag: reading it here to decide what a voucher POSTED months ago can carry is the master-drift trap this
        // whole predicate exists to avoid — switch it off after posting a discounted invoice and the same voucher
        // would suddenly look recoverable, with the discount column hidden and the list rate silently lost. The
        // posted line carries no marker to tell the two apart, so the whole Sales arm is refused by name.
        if (voucher.HasInventoryLines && type.BaseType == VoucherBaseType.Sales)
            return "This voucher was entered as a SALES ITEM INVOICE. Only the effective rate is posted — the "
                 + "list rate and the price-level discount that produced it are not recorded anywhere on the "
                 + "line, so re-opening it could not show the operator the rate they keyed, and re-accepting "
                 + "would discount the effective rate a second time. Altering a sales item invoice needs a "
                 + "schema column for the list rate and the discount; a purchase item invoice, whose posted "
                 + "rate IS the keyed rate, opens today.";

        // Row 18 — 🔴 NO LONGER "UNDETERMINED", AND THE REASON CHANGED (finding L1-04). The round trip WAS measured
        // after this arm shipped: with the arm lifted and SeedAlterationMode pointed at the plain grid, a wholly
        // exempt Sales accounting invoice posted, re-opened, re-accepted and exported BYTE-IDENTICALLY, in memory
        // and on disk, with IsAccountingInvoice and PartyId intact and GSTR-1's exempt value unmoved — because
        // AcceptAlteration already carries `isAccountingInvoice: existing.IsAccountingInvoice` and
        // `partyId: existing.PartyId`, and the two posted lines are ordinary plain-grid lines.
        //
        // So the refusal stands but its SENTENCE was wrong twice over. It said "it cannot be told apart from a
        // plain voucher by its lines" — true, and irrelevant, because this arm reads the PERSISTED
        // IsAccountingInvoice flag, not the lines — and it said the round trip "has not been measured", which is no
        // longer a fact about this repository. What is genuinely not recoverable is the party leg's DERIVED STATUS:
        // on the plain grid it re-opens as an ordinary editable row, AcceptAlteration re-derives nothing, and an
        // operator can therefore move the party total away from the sum of the service rows and still balance.
        // That is an EDIT hazard, not a round-trip one, and it is what the sentence now says.
        //
        // Row 23 — the purchase arm, DEFER: DetectAccountingTdsShape / DetectAccountingRcmShape are wired to the
        // Particulars lines, so it is never tax-free by construction the way the sales arm can be.
        if (voucher.IsAccountingInvoice)
            return type.BaseType == VoucherBaseType.Purchase
                ? "This voucher was entered as an ACCOUNTING (service) INVOICE. Its party leg and tax legs are "
                + "derived from the Particulars rows rather than keyed, and the withholding / reverse-charge "
                + "detection is wired to those rows. Altering a service invoice arrives in a later slice."
                : "This voucher was entered as an ACCOUNTING (service) INVOICE. Its party leg is a DERIVED total of "
                + "the service rows, not a keyed line — the plain Dr/Cr grid would re-open it as an ordinary "
                + "editable row that nothing re-derives, so the party total could be moved off the sum of those "
                + "rows and still balance. Altering a service invoice arrives with its own inverse in a later "
                + "slice.";

        return null;
    }

    // ------------------------------------------------------------------ the engine-DERIVED legs (S5c)

    /// <summary>
    /// 🔴 <b>S5c LIFTED S5b's TEMPORARY BLANKET REFUSAL HERE.</b> S5b refused any voucher carrying
    /// <c>EntryLine.Gst</c> / <c>.Tds</c> / <c>.Tcs</c> outright, with a stated reason: design finding L3-07 binds
    /// this caller to RE-DERIVE a stamped figure rather than echo it (GSTR-1 and GSTR-3B read the STAMPED taxable
    /// value, not the posted amounts), and a slice with no re-derivation has no honest option but to refuse.
    ///
    /// <para>S5c has the re-derivation, so the question is no longer <i>"is anything stamped?"</i> but <i>"can the
    /// engine's own lines be told apart from the keyed ones and rebuilt?"</i> — which is exactly what
    /// <see cref="VoucherAlterationDerivedLegs.Invert"/> answers, and it is the SAME call the rehydration and the
    /// accept path make. One implementation decides for all three, so a shape that opens is by construction a shape
    /// on which <b>Invert raised no refusal</b>.</para>
    ///
    /// <para>🔴 <b>That is deliberately weaker than "a shape that opens inverts", which is what this paragraph
    /// used to claim, and the difference was measured.</b> A reverse-charge voucher posted under COMPOSITION and
    /// then converted to Regular passed <c>Invert</c> without a refusal and still handed back a grid nobody keyed:
    /// the engine's untagged RCM-tax debit appeared as an ordinary row and the screen opened out of balance by
    /// exactly that leg. The refusal for that shape now reads the POSTED lines instead of today's registration type,
    /// which closes that particular gap — but the invariant stays stated as what the code actually guarantees,
    /// because "opens ⇒ correct" is the claim that stopped anyone looking.</para>
    ///
    /// <para><b>What is still refused, and why each is a real limit rather than a deferral:</b> a TCS collection
    /// (collected only on the invoice screens, so the Dr/Cr grid has nothing to re-derive it from); a
    /// reverse-charge pair under a COMPOSITION registration (its balancing debit is deliberately untagged and
    /// cannot be told from a keyed expense); a stamped GST figure that is not part of a reverse-charge pair (no
    /// appender on this screen writes one, so it can only have arrived from an import); and every import-shaped
    /// withholding the entry screen cannot produce.</para>
    /// </summary>
    private static string? DerivedLegRefusal(Company company, Voucher voucher) =>
        voucher.HasInventoryLines
            ? ItemGridDerivedLegRefusal(voucher)
            : VoucherAlterationDerivedLegs.Invert(company, voucher, out _);

    /// <summary>
    /// 🔴 <b>The item invoice's own derived-leg question, and it has a DIFFERENT answer from the plain grid's.</b>
    /// <see cref="VoucherAlterationDerivedLegs.Invert"/> refuses any line carrying a <c>GstLineTax</c> that is not
    /// half of a reverse-charge pair, and that refusal is right FOR THE Dr/Cr GRID: no appender on that screen
    /// writes one, so it can only have arrived from an import and there is nothing to re-derive it from. On the
    /// ITEM grid the opposite holds — <c>ComputeItemInvoiceGst</c> is the very engine that stamped it, and
    /// <c>AcceptItemInvoiceAlteration</c> re-runs it from the amended item lines, so the L3-07 obligation
    /// (re-derive a stamped figure, never echo it) is DISCHARGED rather than dodged. Running Invert here would
    /// refuse every GST-bearing purchase invoice for a reason that does not apply to the screen re-keying it.
    ///
    /// <para>What IS refused is a stamp the item screen has no engine for. A withholding never reaches this path
    /// (<c>TdsPossible</c> is false in item-invoice mode by construction) and a TCS collection is a SALES-side
    /// figure — so on a Purchase item invoice either tag can only have arrived from an import or from a screen
    /// that no longer exists, and there is nothing here that could rebuild it.</para>
    /// </summary>
    public static string? ItemGridDerivedLegRefusal(Voucher voucher)
    {
        if (voucher.Lines.FirstOrDefault(l => l.HasTds) is { } withheld)
            return $"This item invoice carries a {withheld.Tds!.SectionCode} TDS withholding on one of its legs. "
                 + "The item-invoice screen computes no withholding at all — the panel is not wired to the stock "
                 + "grid — so it has nothing to re-derive the carve-out from, and re-accepting would credit the "
                 + "party the full gross.";

        if (voucher.Lines.FirstOrDefault(l => l.HasTcs) is { } collected)
            return $"This item invoice carries a TCS collection ({collected.Tcs!.CollectionCode}). The collected "
                 + "amount and its assessable value are stamped on the line and read by Form 27EQ, and neither "
                 + "the purchase item grid nor the POS screen runs the collection engine — so re-accepting would "
                 + "drop it.";

        if (voucher.Lines.FirstOrDefault(l => l.Gst is { IsReverseCharge: true }) is not null)
            return "This item invoice carries a reverse-charge self-accounting pair. The reverse-charge engine is "
                 + "wired to the plain Dr/Cr grid and to the service invoice, not to the stock grid, so the item "
                 + "screen cannot re-stamp it and re-accepting would drop the self-accounted tax.";

        if (voucher.Lines.FirstOrDefault(l => l.Gst is { Adjustment: not null }) is { } adjusted)
            return $"This item invoice carries a GST statutory adjustment ({adjusted.Gst!.Adjustment}) on one of "
                 + "its legs. It was computed for a return period by the GST engine, not keyed on the item grid, "
                 + "so re-accepting would drop it.";

        return null;
    }

    // ------------------------------------------------------------------ the provisional vector's shape

    /// <summary>
    /// 🔴 <b>The provisional-state vector must be CARRIABLE, not merely carried</b> (§12.8 consequence 2). The entry
    /// screen can only express <c>ApplicableUpto</c> on a Reversing Journal — <c>PostAndSave</c> parses
    /// <c>ApplicableUptoText</c> under <c>IsReversing</c> and hands <c>null</c> otherwise — so a voucher of any
    /// other kind that carries one cannot be rebuilt with it. <c>Replace</c> would refuse that as a provisional-state
    /// change, which is the correct outcome but arrives as an engine message about a field the operator never saw.
    /// Refusing it up front, by name, is the same answer with a sentence that makes sense.
    /// </summary>
    private static string? ProvisionalShapeRefusal(Voucher voucher, VoucherType type)
    {
        var isReversing = type.BaseType == VoucherBaseType.ReversingJournal;

        if (voucher.ApplicableUpto is not null && !isReversing)
            return $"This {type.Name} carries an 'Applicable Upto' date "
                 + $"({voucher.ApplicableUpto:dd-MMM-yyyy}), which only a Reversing Journal's entry screen can "
                 + "state. Re-accepting it here would drop the date and change when the entry lapses, so it is "
                 + "refused.";

        // 🔴 THE MIRROR DIRECTION, and it is reachable (findings L1-03 and L3-02). §6.6a row 29 asserted that
        // "every Reversing Journal carries a non-null ApplicableUpto" on the strength of the ENTRY SCREEN's
        // mandatory-field rule — but that rule is not an invariant of the model: VoucherValidator has no
        // ReversingJournal clause, LedgerService.Post takes one straight through, and CanonicalXml.Parse accepts a
        // reversing journal with the attribute stripped, with ZERO parse errors. Such a voucher opened, seeded
        // ApplicableUptoText from the CONSTRUCTOR's financial-year-end default, and could then NEVER be accepted:
        // Replace refused a provisional-state change to a field the operator never saw. That is exactly what the
        // arm above exists to prevent, in the other direction, so it is refused at the door with a sentence that
        // makes sense instead.
        if (voucher.ApplicableUpto is null && isReversing)
            return "This Reversing Journal carries no 'Applicable Upto' date, which the entry screen requires and "
                 + "cannot state as blank — a book written by an import or by a service can hold one, this screen "
                 + "cannot re-key it. Re-accepting it would silently stamp the financial year end on it.";

        return null;
    }
}
