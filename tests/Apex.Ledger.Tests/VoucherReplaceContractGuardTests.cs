using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Apex.Ledger.Tests.Support;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// The S5a engine-contract guards that the first cut of <see cref="VoucherReplaceEngineTests"/> left unpinned —
/// every one of them found by an adversarial review of the committed slice, and every one of them a mutant that
/// survived the whole four-project gate.
///
/// <para>Each test here names the defect it fixes, because a test whose reason is not written down is the first
/// thing a future maintainer "simplifies".</para>
/// </summary>
public class VoucherReplaceContractGuardTests
{
    // -------------------------------------------------------------------------------------------------
    // ALIASING — the guard that makes all four identity refusals real (review finding L1-02).
    // -------------------------------------------------------------------------------------------------

    /// <summary>
    /// Every identity guard compares <c>replacement.X</c> to <c>existing.X</c>. Hand Replace the LIVE posted
    /// voucher and each of those compares a value to itself, so a renumber, a cancel and an Optional flip all
    /// drove straight through — measured: #10 became #99, cancelled, Optional, ZERO warnings, and
    /// <c>NextNumber</c> jumped from 12 to 100.
    /// </summary>
    [Fact]
    public void Replace_refuses_the_live_posted_voucher_as_its_own_replacement()
    {
        var book = LifecycleBook.Build(LifecycleBook.WrongTotal);
        var live = book.Company.Vouchers.Single(v => v.Id == book.TenthId);
        var before = DerivedStateSnapshot.Snapshot(book.Company, LifecycleBook.AsOf);

        live.Number = 99;
        live.Cancelled = true;
        live.Optional = true;

        var ex = Assert.Throws<InvalidOperationException>(() => book.Service.Replace(book.TenthId, live));
        Assert.Contains("NEW voucher instance", ex.Message, StringComparison.Ordinal);

        // Put the deliberate sabotage back and prove the book is exactly where it started — the refusal has to be
        // a refusal, not a partial application.
        live.Number = 10;
        live.Cancelled = false;
        live.Optional = false;
        Assert.Equal(before, DerivedStateSnapshot.Snapshot(book.Company, LifecycleBook.AsOf));
        Assert.Equal(12, book.Service.NextNumber(book.SalesType.Id));
    }

    /// <summary>A no-op self-replace is refused too: the aliasing guard is unconditional, so it cannot be
    /// defeated by passing a voucher that happens not to have been tampered with yet.</summary>
    [Fact]
    public void Even_an_untampered_self_replace_is_refused()
    {
        var book = LifecycleBook.Build(LifecycleBook.WrongTotal);
        var live = book.Company.Vouchers.Single(v => v.Id == book.TenthId);

        Assert.Throws<InvalidOperationException>(() => book.Service.Replace(book.TenthId, live));
        Assert.Equal(11, book.Company.Vouchers.Count);
    }

    // -------------------------------------------------------------------------------------------------
    // Clause 3 — the ACCEPT side (review finding L2-06). Tightening the guard to `Number != 0` passed the
    // whole gate while refusing every rehydrated alteration.
    // -------------------------------------------------------------------------------------------------

    /// <summary>
    /// A replacement carrying the voucher's OWN number is the shape an S5b <c>ForAlter</c> rehydration produces
    /// — rehydrating a posted voucher reads its <c>Number</c> back. It must be ACCEPTED.
    /// </summary>
    [Fact]
    public void Replace_accepts_a_replacement_that_carries_the_vouchers_own_number()
    {
        var book = LifecycleBook.Build(LifecycleBook.WrongTotal);
        var original = book.Company.Vouchers.Single(v => v.Id == book.TenthId);

        var rehydrated = LifecycleBook.SalesVoucher(
            book, book.TenthId, original.Date, LifecycleBook.RightTotal, LifecycleBook.TenthNarration);
        rehydrated.Number = original.Number;          // exactly what reading the posted voucher back produces

        var accepted = book.Service.Replace(book.TenthId, rehydrated, out var warnings);

        Assert.Equal(10, accepted.Number);
        Assert.Equal(LifecycleBook.RightTotal, accepted.TotalDebit);
        Assert.Empty(warnings);
    }

    // -------------------------------------------------------------------------------------------------
    // A rejected replacement is left as the caller built it (review finding L1-04).
    // -------------------------------------------------------------------------------------------------

    /// <summary>
    /// Replace stamps the original's <c>Number</c> onto the replacement BEFORE validating. When validation then
    /// refuses, the caller used to get its draft back carrying #10 — and re-posting that corrected draft as a NEW
    /// voucher took the stamped number instead of a fresh one, so the book ended with TWO live Sales #10.
    /// </summary>
    [Fact]
    public void A_rejected_replacement_does_not_keep_the_originals_number()
    {
        var book = LifecycleBook.Build(LifecycleBook.WrongTotal);
        var original = book.Company.Vouchers.Single(v => v.Id == book.TenthId);

        var unbalanced = new Voucher(
            book.TenthId, book.SalesType.Id, original.Date,
            new[]
            {
                new EntryLine(book.Customer.Id, LifecycleBook.RightTotal, DrCr.Debit),
                new EntryLine(book.SalesLedger.Id, LifecycleBook.RightTotal - Money.FromRupees(1m), DrCr.Credit),
            });

        Assert.Throws<UnbalancedVoucherException>(() => book.Service.Replace(book.TenthId, unbalanced));
        Assert.Equal(0, unbalanced.Number);
    }

    /// <summary>The harm the stamp caused, pinned end to end: correct the rejected draft, post it as a NEW
    /// voucher, and it must take a FRESH number — not the original's.</summary>
    [Fact]
    public void Re_posting_a_corrected_draft_after_a_rejection_takes_a_fresh_number()
    {
        var book = LifecycleBook.Build(LifecycleBook.WrongTotal);
        var original = book.Company.Vouchers.Single(v => v.Id == book.TenthId);

        var unbalanced = new Voucher(
            book.TenthId, book.SalesType.Id, original.Date,
            new[]
            {
                new EntryLine(book.Customer.Id, LifecycleBook.RightTotal, DrCr.Debit),
                new EntryLine(book.SalesLedger.Id, LifecycleBook.RightTotal - Money.FromRupees(1m), DrCr.Credit),
            });
        Assert.Throws<UnbalancedVoucherException>(() => book.Service.Replace(book.TenthId, unbalanced));

        var corrected = LifecycleBook.SalesVoucher(
            book, Guid.NewGuid(), original.Date, LifecycleBook.RightTotal, "posted as a new voucher instead");
        corrected.Number = unbalanced.Number;         // the caller carries its draft's number forward

        var posted = book.Service.Post(corrected);

        Assert.Equal(12, posted.Number);
        Assert.Equal(1, book.Company.Vouchers.Count(v => v.TypeId == book.SalesType.Id && v.Number == 10));
    }

    /// <summary>
    /// The same promise for the item-invoice half: Replace rewrites every item line's Direction before it
    /// validates, and a rejected replacement used to be handed back with its lines flipped Inward → Outward.
    /// </summary>
    [Fact]
    public void A_rejected_item_invoice_replacement_keeps_the_directions_the_caller_built()
    {
        var kit = ItemInvoiceBook.Build();

        // Item line says 12.125 units, the accounting legs still carry the 3.75-unit value: the §10 pairing
        // invariant refuses it — AFTER the direction stamp has already run.
        var mismatched = new Voucher(
            kit.SaleVoucherId, kit.SalesTypeId, ItemInvoiceBook.SaleDate,
            new[]
            {
                new EntryLine(kit.Debtor.Id, Money.FromRupees(5297.06m), DrCr.Debit),
                new EntryLine(kit.SalesLedger.Id, Money.FromRupees(5297.06m), DrCr.Credit),
            },
            inventoryLines: new[]
            {
                new VoucherInventoryLine(kit.ItemId, kit.GodownId, 12.125m, Money.FromRupees(1412.55m)),
            });

        Assert.Equal(StockDirection.Inward, mismatched.InventoryLines[0].Direction);   // the ctor default

        Assert.Throws<InvalidVoucherException>(() => kit.Service.Replace(kit.SaleVoucherId, mismatched));

        Assert.Equal(StockDirection.Inward, mismatched.InventoryLines[0].Direction);
    }

    // -------------------------------------------------------------------------------------------------
    // IsAccountingInvoice — the domain declares it immutable; Replace flipped it (review finding L3-05).
    // -------------------------------------------------------------------------------------------------

    /// <summary>
    /// <c>Voucher.IsAccountingInvoice</c> is get-only with a written reason: <i>"the printed document type of an
    /// issued invoice must not be flippable after the fact"</i>. Clause 5's premise — construct a NEW voucher —
    /// is precisely the door that leaves open, and it drives
    /// <c>GstReportSupport.IsServiceAccountingInvoice</c>, the gate deciding whether a ledger-only Sales voucher
    /// projects as a Rule-46 TAX INVOICE or a plain Dr/Cr voucher.
    /// </summary>
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void Replace_refuses_to_flip_the_accounting_invoice_flag_in_either_direction(bool posted, bool asked)
    {
        var book = LifecycleBook.Build(LifecycleBook.WrongTotal);
        var original = book.Company.Vouchers.Single(v => v.Id == book.TenthId);

        // Re-post the tenth carrying the flag under test, so both directions are reachable.
        book.Service.Delete(book.TenthId);
        var seeded = new Voucher(
            book.TenthId, book.SalesType.Id, original.Date,
            new[]
            {
                new EntryLine(book.Customer.Id, LifecycleBook.WrongTotal, DrCr.Debit),
                new EntryLine(book.SalesLedger.Id, LifecycleBook.WrongTotal, DrCr.Credit),
            },
            number: 10, partyId: book.Customer.Id, isAccountingInvoice: posted);
        book.Service.Post(seeded);

        var flipped = new Voucher(
            book.TenthId, book.SalesType.Id, original.Date,
            new[]
            {
                new EntryLine(book.Customer.Id, LifecycleBook.RightTotal, DrCr.Debit),
                new EntryLine(book.SalesLedger.Id, LifecycleBook.RightTotal, DrCr.Credit),
            },
            partyId: book.Customer.Id, isAccountingInvoice: asked);

        var ex = Assert.Throws<InvalidOperationException>(() => book.Service.Replace(book.TenthId, flipped));
        Assert.Contains("Accounting Invoice", ex.Message, StringComparison.Ordinal);
        Assert.Equal(posted, book.Company.FindVoucher(book.TenthId)!.IsAccountingInvoice);
    }

    /// <summary>And an alteration that KEEPS the flag is accepted — the refusal is about the change, not about
    /// accounting invoices, so it must not become a blanket "service invoices cannot be altered".</summary>
    [Fact]
    public void An_accounting_invoice_can_still_be_altered_when_the_flag_is_carried()
    {
        var book = LifecycleBook.Build(LifecycleBook.WrongTotal);
        var original = book.Company.Vouchers.Single(v => v.Id == book.TenthId);
        book.Service.Delete(book.TenthId);
        book.Service.Post(new Voucher(
            book.TenthId, book.SalesType.Id, original.Date,
            new[]
            {
                new EntryLine(book.Customer.Id, LifecycleBook.WrongTotal, DrCr.Debit),
                new EntryLine(book.SalesLedger.Id, LifecycleBook.WrongTotal, DrCr.Credit),
            },
            number: 10, partyId: book.Customer.Id, isAccountingInvoice: true));

        var accepted = book.Service.Replace(book.TenthId, new Voucher(
            book.TenthId, book.SalesType.Id, original.Date,
            new[]
            {
                new EntryLine(book.Customer.Id, LifecycleBook.RightTotal, DrCr.Debit),
                new EntryLine(book.SalesLedger.Id, LifecycleBook.RightTotal, DrCr.Credit),
            },
            partyId: book.Customer.Id, isAccountingInvoice: true));

        Assert.True(accepted.IsAccountingInvoice);
        Assert.Equal(LifecycleBook.RightTotal, accepted.TotalDebit);
    }

    // -------------------------------------------------------------------------------------------------
    // The three author deviations, judged and RECORDED so they are not re-litigated (review finding L1-11).
    // -------------------------------------------------------------------------------------------------

    /// <summary>
    /// The TypeId refusal is RIGHT — the collision it prevents is real and permanent, not speculative — but what
    /// it blocks is the legitimate "keyed under the wrong type" correction, whose only remaining route is
    /// Delete + re-Post, the exact harm S5a exists to remove. The message must therefore name the remedy.
    /// </summary>
    [Fact]
    public void The_type_change_refusal_names_the_remedy()
    {
        var book = LifecycleBook.Build(LifecycleBook.WrongTotal);
        var original = book.Company.Vouchers.Single(v => v.Id == book.TenthId);
        var journal = book.Company.FindVoucherTypeByName("Journal")!;

        var retyped = new Voucher(
            book.TenthId, journal.Id, original.Date,
            new[]
            {
                new EntryLine(book.Customer.Id, LifecycleBook.RightTotal, DrCr.Debit),
                new EntryLine(book.SalesLedger.Id, LifecycleBook.RightTotal, DrCr.Credit),
            });

        var ex = Assert.Throws<InvalidOperationException>(() => book.Service.Replace(book.TenthId, retyped));
        Assert.Contains("re-enter it under the correct type", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The Cancelled refusal is RIGHT and it is NOT over-broad: a cancelled voucher CAN still be altered, so long
    /// as the caller carries the flag. Pinned so nobody later "fixes" the guard into a blanket refusal.
    /// </summary>
    [Fact]
    public void A_cancelled_voucher_can_still_be_altered_when_the_replacement_carries_the_flag()
    {
        var book = LifecycleBook.Build(LifecycleBook.WrongTotal);
        book.Service.Cancel(book.TenthId);
        var original = book.Company.Vouchers.Single(v => v.Id == book.TenthId);

        var amended = new Voucher(
            book.TenthId, book.SalesType.Id, original.Date,
            new[]
            {
                new EntryLine(book.Customer.Id, LifecycleBook.RightTotal, DrCr.Debit),
                new EntryLine(book.SalesLedger.Id, LifecycleBook.RightTotal, DrCr.Credit),
            },
            narration: "amended while cancelled",
            partyId: book.Customer.Id,
            cancelled: true);

        var accepted = book.Service.Replace(book.TenthId, amended, out var warnings);

        Assert.True(accepted.Cancelled);
        Assert.Equal("amended while cancelled", accepted.Narration);
        Assert.Empty(warnings);
    }

    // -------------------------------------------------------------------------------------------------
    // The rendered document number (review finding L1-06). Clause 3 preserves the INT, not the string the
    // outside world uses.
    // -------------------------------------------------------------------------------------------------

    /// <summary>
    /// <c>VoucherNumberFormatter</c> picks the affix by voucher DATE, so moving a voucher across a
    /// date-effective prefix boundary rewrites the PRINTED document number while the int stays #10. That used to
    /// be reported only as "the date changed".
    /// </summary>
    [Fact]
    public void A_date_change_that_rewrites_the_rendered_number_says_so()
    {
        var book = LifecycleBook.Build(LifecycleBook.WrongTotal);
        book.SalesType.SetAffixes(
            new[]
            {
                new VoucherNumberAffix(Guid.NewGuid(), LifecycleBook.BooksBegin, "SL/"),
                new VoucherNumberAffix(Guid.NewGuid(), LifecycleBook.BooksBegin.AddDays(7), "SL2/"),
            },
            null);

        var original = book.Company.Vouchers.Single(v => v.Id == book.TenthId);
        Assert.Equal("SL2/10", book.Company.FormatVoucherNumber(original));

        var moved = LifecycleBook.SalesVoucher(
            book, book.TenthId, LifecycleBook.BooksBegin.AddDays(2), LifecycleBook.RightTotal,
            LifecycleBook.TenthNarration);

        var accepted = book.Service.Replace(book.TenthId, moved, out var warnings);

        Assert.Equal("SL/10", book.Company.FormatVoucherNumber(accepted));
        Assert.Equal(10, accepted.Number);

        var renumbered = Assert.Single(warnings, w => w.Code == VoucherAlterationWarningCode.RenderedNumberChanged);
        Assert.Contains("'SL2/10' to 'SL/10'", renumbered.Message, StringComparison.Ordinal);
        Assert.Contains(warnings, w => w.Code == VoucherAlterationWarningCode.DateChanged);
    }

    /// <summary>A date change that does NOT move the affix must not raise the number warning — the warning is
    /// about the rendered number, not a second copy of the date warning.</summary>
    [Fact]
    public void A_date_change_inside_one_affix_window_raises_no_rendered_number_warning()
    {
        var book = LifecycleBook.Build(LifecycleBook.WrongTotal);
        var original = book.Company.Vouchers.Single(v => v.Id == book.TenthId);

        book.Service.Replace(
            book.TenthId,
            LifecycleBook.SalesVoucher(
                book, book.TenthId, original.Date.AddDays(1), LifecycleBook.RightTotal, LifecycleBook.TenthNarration),
            out var warnings);

        Assert.Equal(VoucherAlterationWarningCode.DateChanged, Assert.Single(warnings).Code);
    }

    // -------------------------------------------------------------------------------------------------
    // The null guard (review finding L2-09) — deleting it passed Ledger, Io and Sqlite.
    // -------------------------------------------------------------------------------------------------

    [Fact]
    public void Replace_names_a_null_replacement_instead_of_dereferencing_it()
    {
        var book = LifecycleBook.Build(LifecycleBook.WrongTotal);
        Assert.Throws<ArgumentNullException>(() => book.Service.Replace(book.TenthId, null!));
    }

    // -------------------------------------------------------------------------------------------------
    // The pure-stock aggregate refusal SAYS SO (review finding L3-08). The test that claimed this asserted
    // only the exception TYPE, so "and says so" was unpinned and the message was the generic not-found.
    // -------------------------------------------------------------------------------------------------

    [Fact]
    public void Replace_on_an_InventoryVoucher_id_names_the_aggregate_and_the_right_service()
    {
        var kit = ItemInvoiceBook.Build();
        var inventory = new InventoryPostingService(kit.Company);
        var physicalType = kit.Company.VoucherTypes.First(t => t.BaseType == VoucherBaseType.PhysicalStock);

        var count = InventoryVoucher.PhysicalStock(
            Guid.NewGuid(), physicalType.Id, ItemInvoiceBook.SaleDate.AddDays(6),
            new[] { new PhysicalStockLine(kit.ItemId, kit.GodownId, 90m, null) });
        inventory.Post(count);

        var ex = Assert.Throws<InvalidOperationException>(() => kit.Service.Replace(
            count.Id, ItemInvoiceBook.SaleInvoice(kit, count.Id, ItemInvoiceBook.SaleDate, 1m)));

        Assert.Contains("pure-stock inventory voucher", ex.Message, StringComparison.Ordinal);
        Assert.Contains("InventoryPostingService", ex.Message, StringComparison.Ordinal);
        Assert.Single(kit.Company.InventoryVouchers);
    }

    /// <summary>A genuinely unknown Guid still gets the plain not-found message — the named one must not become
    /// a catch-all that hides a mistyped id.</summary>
    [Fact]
    public void An_unknown_guid_still_gets_the_plain_not_found_message()
    {
        var kit = ItemInvoiceBook.Build();
        var ex = Assert.Throws<InvalidOperationException>(() => kit.Service.Replace(
            Guid.NewGuid(), ItemInvoiceBook.SaleInvoice(kit, Guid.NewGuid(), ItemInvoiceBook.SaleDate, 1m)));

        Assert.Contains("not found", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("pure-stock", ex.Message, StringComparison.Ordinal);
    }

    // -------------------------------------------------------------------------------------------------
    // Guid uniqueness — the invariant clause 2's whole rationale rests on (review findings L1-10, L3-09).
    // -------------------------------------------------------------------------------------------------

    /// <summary>
    /// Clause 2 says the Guid is "the outside world's only handle on this voucher". That was an assertion, not an
    /// invariant: a second voucher carrying an already-used Guid posted without complaint, and
    /// <c>Company.FindVoucher</c> (a <c>FirstOrDefault</c>) could then only ever see the first of the two.
    /// </summary>
    [Fact]
    public void A_second_voucher_carrying_an_already_used_Guid_is_refused()
    {
        var book = LifecycleBook.Build(LifecycleBook.WrongTotal);

        var collider = LifecycleBook.SalesVoucher(
            book, book.TenthId, LifecycleBook.BooksBegin.AddDays(20), Money.FromRupees(555.55m), "a collider");

        var ex = Assert.Throws<InvalidVoucherException>(() => book.Service.Post(collider));
        Assert.Contains("already posted", ex.Message, StringComparison.Ordinal);
        Assert.Equal(1, book.Company.Vouchers.Count(v => v.Id == book.TenthId));
    }

    /// <summary>
    /// The CROSS-aggregate half — the invariant "Replace cannot reach an InventoryVoucher" actually rests on.
    /// Post a pure-stock voucher carrying an ACCOUNTING voucher's Guid and one Guid names two different things in
    /// one company, after which <c>Replace(thatGuid, …)</c> silently alters the accounting one.
    /// </summary>
    [Fact]
    public void A_pure_stock_voucher_carrying_an_accounting_vouchers_Guid_is_refused()
    {
        var kit = ItemInvoiceBook.Build();
        var inventory = new InventoryPostingService(kit.Company);
        var physicalType = kit.Company.VoucherTypes.First(t => t.BaseType == VoucherBaseType.PhysicalStock);

        var collider = InventoryVoucher.PhysicalStock(
            kit.SaleVoucherId, physicalType.Id, ItemInvoiceBook.SaleDate.AddDays(6),
            new[] { new PhysicalStockLine(kit.ItemId, kit.GodownId, 90m, null) });

        var ex = Assert.Throws<InvalidVoucherException>(() => inventory.Post(collider));
        Assert.Contains("already posted in this company's accounting book", ex.Message, StringComparison.Ordinal);
        Assert.Empty(kit.Company.InventoryVouchers);
    }

    /// <summary>The mirror direction: an ACCOUNTING voucher may not take an id the pure-stock book already
    /// holds, or the same one Guid names two things from the other side.</summary>
    [Fact]
    public void An_accounting_voucher_carrying_a_pure_stock_vouchers_Guid_is_refused()
    {
        var kit = ItemInvoiceBook.Build();
        var inventory = new InventoryPostingService(kit.Company);
        var physicalType = kit.Company.VoucherTypes.First(t => t.BaseType == VoucherBaseType.PhysicalStock);

        var count = InventoryVoucher.PhysicalStock(
            Guid.NewGuid(), physicalType.Id, ItemInvoiceBook.SaleDate.AddDays(6),
            new[] { new PhysicalStockLine(kit.ItemId, kit.GodownId, 90m, null) });
        inventory.Post(count);

        var ex = Assert.Throws<InvalidVoucherException>(() => kit.Service.Post(
            ItemInvoiceBook.SaleInvoice(kit, count.Id, ItemInvoiceBook.SaleDate.AddDays(7), 1m)));

        Assert.Contains("inventory book", ex.Message, StringComparison.Ordinal);
        Assert.Contains("id space", ex.Message, StringComparison.Ordinal);
    }
}
