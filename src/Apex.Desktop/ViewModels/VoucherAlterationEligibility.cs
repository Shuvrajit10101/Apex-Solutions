using System;
using System.Linq;
using Apex.Ledger.Domain;

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
            if (company.InventoryVouchers.Any(iv => iv.Id == voucherId))
                return "This is a pure-stock inventory voucher. It lives in the inventory aggregate, which "
                     + "LedgerService.Replace does not reach, so the accounting entry screen cannot re-open it — "
                     + "the refusal is architectural, not a judgement about its shape. Alter it on the inventory "
                     + "entry screen once that verb exists.";

            return "That voucher is no longer in this company's books — it may have been deleted since the report "
                 + "was drawn. Re-open the report and try again.";
        }

        if (company.FindVoucherType(voucher.TypeId) is not { } type)
            return "This voucher's type is missing from the company, so the entry screen cannot be re-opened for "
                 + "it. Restore the voucher type first.";

        return RefusalFor(company, voucher, type);
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
            ?? StampedTaxRefusal(voucher)
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
        if (type.IsPosSales)
            return "This is a POS bill. Its single customer debit is a split of payment tenders keyed on the POS "
                 + "billing screen, and the plain Dr/Cr grid has no way to express them, so re-accepting it here "
                 + "would rebuild the tender split as ordinary debits.";

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
            return $"A {type.Name} is a pure-stock inventory voucher. It lives in the inventory aggregate, which "
                 + "LedgerService.Replace does not reach, so the accounting entry screen cannot re-open it.";

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

        // Row 13 — the adjustment Journal. Same untagged reversal pair as the refund, and worse in one respect:
        // AdjustAgainstInvoice throws "this advance has already been adjusted" on a second call, so a rehydration
        // that DID restore the panel would refuse with a message about the wrong thing.
        if (company.AdvanceReceipts.Any(a => a.AdjustedAgainstInvoiceVoucherId == id))
            return "This journal releases a GST advance against an invoice. The suspense-releasing legs it "
                 + "carries were built by the advance engine and are untagged, so the grid cannot tell them from "
                 + "keyed lines; re-accepting would drop the release and leave the advance still outstanding.";

        // Row 4 — the Rule-51 refund Payment. AdvanceReceiptService.BuildAdvanceReversalPair's own doc comment
        // states the reversal legs carry NO GstLineTax, and Refund REPLACES the record on the company.
        if (company.AdvanceReceipts.Any(a => a.RefundVoucherId == id))
            return "This payment refunds a GST advance (Rule 51). Its reversal legs carry no tax tag at all, so "
                 + "they are indistinguishable from keyed lines on the grid; re-accepting would drop them and the "
                 + "advance's output tax would silently return to the books while the record still reads refunded.";

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
        if (company.ChallanVoucherLinks.Any(l => l.VoucherId == id)
            || company.TcsChallanVoucherLinks.Any(l => l.VoucherId == id))
            return "This voucher is linked to a TDS/TCS challan, which freezes the challan number, BSR code, "
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
        // Rows 17 and 22 — the item invoice. Two proven non-inverses beyond tax: a batch-split line posts ONE item
        // line PER BATCH (so one keyed row becomes N posted rows), and the posted rate is the EffectiveRate
        // (rate x (1 - discount/100)) while VoucherInventoryLine has no discount field at all — the list rate and
        // the Price-Level discount are unrecoverable from what was posted.
        if (voucher.HasInventoryLines)
            return "This voucher was entered as an ITEM INVOICE. Its accounting legs are derived from the stock "
                 + "lines rather than keyed, a batch-split row posts one line per batch, and the posted rate is "
                 + "already net of the price-level discount — so the list rate and the discount cannot be read "
                 + "back. Altering an item invoice arrives in a later slice.";

        // Row 18 — UNDETERMINED, and it must NOT ship as SIMPLE. The party leg is a DERIVED total, and the
        // zero-rated / LUT / wholly-exempt branch posts no tax leg at all, so this shape passes every tag filter
        // while still carrying a derived leg. It was never measured; refused by name until it is.
        // Row 23 — the purchase arm, DEFER: DetectAccountingTdsShape / DetectAccountingRcmShape are wired to the
        // Particulars lines, so it is never tax-free by construction the way the sales arm can be.
        if (voucher.IsAccountingInvoice)
            return type.BaseType == VoucherBaseType.Purchase
                ? "This voucher was entered as an ACCOUNTING (service) INVOICE. Its party leg and tax legs are "
                + "derived from the Particulars rows rather than keyed, and the withholding / reverse-charge "
                + "detection is wired to those rows. Altering a service invoice arrives in a later slice."
                : "This voucher was entered as an ACCOUNTING (service) INVOICE. Its party leg is a DERIVED total "
                + "rather than a keyed line, and on a zero-rated (LUT/export) or wholly exempt supply it carries "
                + "no tax leg at all — so it cannot be told apart from a plain voucher by its lines. Whether it "
                + "round-trips has not been measured, so it is refused rather than guessed at.";

        return null;
    }

    // ------------------------------------------------------------------ the stamped-tax families (DEFER)

    /// <summary>
    /// The tag scan — necessary but, on its own, <b>provably insufficient</b> (which is why it runs last and not
    /// first). A stamped figure on a line is a figure the engine DERIVED, and design finding L3-07 binds this
    /// caller to RE-DERIVE rather than echo it. S5b does not re-derive anything, so it refuses instead: refusing is
    /// the only form of "never echo" available to a slice that has no re-derivation.
    /// </summary>
    private static string? StampedTaxRefusal(Voucher voucher)
    {
        if (voucher.Lines.Any(l => l.HasGst))
            return "This voucher carries engine-stamped GST on its lines — the head, rate and taxable value GSTR-1 "
                 + "and GSTR-3B read. Those figures must be RE-DERIVED from the amended content, never carried "
                 + "forward, or a return would declare a figure the book no longer holds. The re-stamp arrives in "
                 + "a later slice.";

        if (voucher.Lines.Any(l => l.HasTds))
            return "This voucher carries a TDS withholding carve-out: the party leg holds the DERIVED net, not the "
                 + "gross that was keyed, and a separate TDS-payable leg sits beside it. Re-opening it means "
                 + "inverting the carve to recover the gross and re-carving from the restored gross; that arrives "
                 + "in a later slice.";

        if (voucher.Lines.Any(l => l.HasTcs))
            return "This voucher carries a TCS collection: the collected amount and its assessable value are "
                 + "stamped on the line and read by Form 27EQ. They must be re-derived from the amended content, "
                 + "which arrives in a later slice.";

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
    private static string? ProvisionalShapeRefusal(Voucher voucher, VoucherType type) =>
        voucher.ApplicableUpto is not null && type.BaseType != VoucherBaseType.ReversingJournal
            ? $"This {type.Name} carries an 'Applicable Upto' date "
            + $"({voucher.ApplicableUpto:dd-MMM-yyyy}), which only a Reversing Journal's entry screen can state. "
            + "Re-accepting it here would drop the date and change when the entry lapses, so it is refused."
            : null;
}
