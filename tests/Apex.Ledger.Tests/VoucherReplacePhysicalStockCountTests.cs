using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Apex.Ledger.Tests.Support;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// Design §7.4 — <b>Physical Stock, "the single nastiest family in the phase"</b>, and the reason it is nasty:
/// <c>InventoryLedger</c> treats a count as a CHECKPOINT that RESETS the running balance
/// (<c>InventoryLedger.cs line 193-207</c>), so every on-hand figure downstream of a count is blind to a
/// quantity change upstream of it.
///
/// <para><b>The measured hole.</b> S5a's correctness statement rests on <c>DerivedStateSnapshot</c>, which was
/// documented as <i>"a canonical, ordered, paisa-exact text dump of a company's ENTIRE derived surface"</i>. In
/// a book with a count downstream, a quantity-only alteration — 10 units out becomes 20 units out for the
/// IDENTICAL money — left that dump <b>BYTE-IDENTICAL</b>. The absorbed variance silently moved from a
/// 2.5-unit shortage to a 7.5-unit excess and no figure in the whole instrument recorded it, because the stock
/// section reads only on-hand and closing valuation AT the as-of date, both of which the count had reset. The
/// fixtures of <c>VoucherReplaceInventoryFamilyTests</c> contained no Physical Stock count at all.</para>
///
/// <para>The first test is the instrument's own precondition — without it the second is vacuous.</para>
/// </summary>
public class VoucherReplacePhysicalStockCountTests
{
    private static readonly Money TenAtAThousand = Money.FromRupees(1000m);
    private static readonly Money TwentyAtFiveHundred = Money.FromRupees(500m);

    /// <summary>
    /// PRECONDITION. Two books that differ ONLY in the quantity of one sale — same money on every line, a
    /// Physical Stock count downstream absorbing the difference, and therefore the same on-hand at the as-of
    /// date — must nonetheless snapshot DIFFERENTLY. This is the assertion that used to be false.
    /// </summary>
    [Fact]
    public void A_quantity_only_difference_absorbed_by_a_downstream_count_is_visible_in_the_snapshot()
    {
        var a = ItemInvoiceBook.Build(saleQuantity: 10m, physicalCount: 88m, saleRate: TenAtAThousand);
        var b = ItemInvoiceBook.Build(saleQuantity: 20m, physicalCount: 88m, saleRate: TwentyAtFiveHundred);

        // The money is identical on both books…
        Assert.Equal(
            Money.FromRupees(10000m),
            a.Company.FindVoucher(a.SaleVoucherId)!.TotalDebit);
        Assert.Equal(
            Money.FromRupees(10000m),
            b.Company.FindVoucher(b.SaleVoucherId)!.TotalDebit);

        // …and so is the on-hand, because the count reset it.
        var onHandA = new InventoryLedger(a.Company).OnHand(a.ItemId, ItemInvoiceBook.AsOf);
        var onHandB = new InventoryLedger(b.Company).OnHand(b.ItemId, ItemInvoiceBook.AsOf);
        Assert.Equal(88m, onHandA);
        Assert.Equal(onHandA, onHandB);

        Assert.NotEqual(
            DerivedStateSnapshot.Snapshot(a.Company, ItemInvoiceBook.AsOf),
            DerivedStateSnapshot.Snapshot(b.Company, ItemInvoiceBook.AsOf));
    }

    /// <summary>
    /// §7.2 T-1 for the count-bearing case: a book corrected in place must equal a book keyed right the first
    /// time, on an instrument that can actually SEE the correction.
    /// </summary>
    [Fact]
    public void An_altered_quantity_under_a_downstream_count_equals_a_directly_posted_book()
    {
        var a = ItemInvoiceBook.Build(saleQuantity: 10m, physicalCount: 88m, saleRate: TenAtAThousand);
        var b = ItemInvoiceBook.Build(
            saleQuantity: 20m, fixedSaleId: a.SaleVoucherId, physicalCount: 88m, saleRate: TwentyAtFiveHundred);

        a.Service.Replace(
            a.SaleVoucherId,
            ItemInvoiceBook.SaleInvoice(a, a.SaleVoucherId, ItemInvoiceBook.SaleDate, 20m, TwentyAtFiveHundred));

        Assert.Equal(
            DerivedStateSnapshot.Snapshot(b.Company, ItemInvoiceBook.AsOf),
            DerivedStateSnapshot.Snapshot(a.Company, ItemInvoiceBook.AsOf));
    }

    /// <summary>
    /// The variance the count ABSORBS is what actually changed, and it is now on the record: 100 in, 10 + 2.5
    /// out leaves 87.5 against a count of 88 (a 0.5 excess); doubling the sale quantity leaves 77.5 against the
    /// same count (a 10.5 excess). Same money, same closing on-hand, a ten-unit difference in what the count had
    /// to absorb.
    /// </summary>
    [Fact]
    public void The_absorbed_variance_is_what_a_quantity_only_alteration_actually_moves()
    {
        var book = ItemInvoiceBook.Build(saleQuantity: 10m, physicalCount: 88m, saleRate: TenAtAThousand);
        var ledger = new InventoryLedger(book.Company);
        var dayBeforeCount = ItemInvoiceBook.Books.AddDays(10);

        Assert.Equal(87.5m, ledger.OnHand(book.ItemId, dayBeforeCount));

        book.Service.Replace(
            book.SaleVoucherId,
            ItemInvoiceBook.SaleInvoice(
                book, book.SaleVoucherId, ItemInvoiceBook.SaleDate, 20m, TwentyAtFiveHundred));

        Assert.Equal(77.5m, ledger.OnHand(book.ItemId, dayBeforeCount));
        Assert.Equal(88m, ledger.OnHand(book.ItemId, ItemInvoiceBook.AsOf));   // the count still rules after it
    }

    /// <summary>
    /// The §7.2 instrument must not be silently broken on the fixtures it is asserted over: a section that
    /// THROWS renders "!!" rather than aborting, which is deliberate but makes a dead section invisible.
    /// </summary>
    [Fact]
    public void The_snapshot_swallows_no_exceptions_on_the_lifecycle_fixtures()
    {
        DerivedStateSnapshot.Snapshot(
            LifecycleBook.Build(LifecycleBook.WrongTotal).Company, LifecycleBook.AsOf);
        Assert.Equal(0, DerivedStateSnapshot.SwallowedThrowCount);

        DerivedStateSnapshot.Snapshot(
            ItemInvoiceBook.Build(physicalCount: 88m).Company, ItemInvoiceBook.AsOf);
        Assert.Equal(0, DerivedStateSnapshot.SwallowedThrowCount);
    }

    /// <summary>
    /// And the instrument refuses to hand back a dump that lost its identity section — the 36% of the dump that
    /// carries the whole discrimination of the red proof. Asserted structurally, because suppressing that
    /// section made a corrected-in-place book and a Delete-then-rePost book compare byte-identical while 1,767
    /// tests stayed green.
    /// </summary>
    [Fact]
    public void The_snapshot_always_carries_the_voucher_identity_section()
    {
        var book = LifecycleBook.Build(LifecycleBook.WrongTotal);
        var dump = DerivedStateSnapshot.Snapshot(book.Company, LifecycleBook.AsOf);

        var identityLines = dump.Split('\n').Count(l => l.StartsWith("12.VoucherIdentity", StringComparison.Ordinal));
        Assert.True(identityLines > 0, "the identity section did not render");
        Assert.Contains("12.VoucherIdentity[9].Index = 9", dump, StringComparison.Ordinal);

        // The rule itself, exercised directly so it is a live guard rather than an unreachable one. A section
        // that THROWS still renders its HEADING, so the rule counts rendered ROWS: a heading-only check would
        // pass over a section that produced no data at all.
        DerivedStateSnapshot.EnsureIdentitySectionRendered(dump, book.Company.Vouchers.Count);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            DerivedStateSnapshot.EnsureIdentitySectionRendered(
                "01.TrialBalance = x\n12.VoucherIdentity !! InvalidOperationException: boom\n", 11));
        Assert.Contains("no voucher identity ROWS", ex.Message, StringComparison.Ordinal);

        // An empty book has nothing to assert about, and must not be refused.
        DerivedStateSnapshot.EnsureIdentitySectionRendered("", 0);
    }
}
