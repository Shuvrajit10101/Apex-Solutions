using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Xunit;
using Domain = Apex.Ledger.Domain;

namespace Apex.Ledger.Tests;

/// <summary>
/// Census 6.36 clause 1 (wave 13, track ic) — the engine half of Nature of Goods ALTER and DELETE
/// (<see cref="TcsConfig.ReplaceNatureOfGoods"/>, <see cref="TcsConfig.RemoveNatureOfGoods"/>,
/// <see cref="NatureOfGoods.WithTerms"/>, <see cref="NatureOfGoodsRules"/>), and T2-181's
/// <see cref="NatureOfPaymentRules.VouchersMovedBy"/>. The window-level proofs through the real keys and a reloaded
/// book are <c>Apex.Desktop.Tests.NatureOfGoodsAlterDeleteTests</c> and <c>NatureOfPaymentMovedCountTests</c>.
/// </summary>
public class NatureOfGoodsAlterDeleteRulesTests
{
    private static readonly DateOnly Fy = new(2025, 4, 1);
    private const string Pan = "AAPFU0939F";

    private static Company NewTcsCompany()
    {
        var c = CompanyFactory.CreateSeeded("NOG Rules Co", Fy);
        new TdsTcsService(c).EnableTcs(new TcsConfig { Tan = "MUMA12345B" });
        return c;
    }

    private static NatureOfGoods AddCustom(Company c, string code)
    {
        var n = new NatureOfGoods(Guid.NewGuid(), code, "Custom " + code, 100, 500, code, isPredefined: false);
        c.Tcs!.AddNatureOfGoods(n);
        return n;
    }

    private static Domain.Ledger AddLedger(Company c, string name, string group, bool debit)
    {
        var l = new Domain.Ledger(Guid.NewGuid(), name, c.FindGroupByName(group)!.Id, Money.Zero, debit);
        c.AddLedger(l);
        return l;
    }

    /// <summary>Posts a Journal Dr Buyer / Cr Sales / Cr TCS Payable carrying a TCS line under <paramref name="nature"/>.</summary>
    private static Voucher PostTcs(Company c, NatureOfGoods nature, decimal value, DateOnly on)
    {
        var buyer = c.FindLedgerByName("Buyer") ?? AddLedger(c, "Buyer", "Sundry Debtors", true);
        buyer.PartyPan = Pan;
        var sales = c.FindLedgerByName("Scrap Sales") ?? AddLedger(c, "Scrap Sales", "Sales Accounts", false);
        var col = new TcsService(c).BuildCollection(Money.FromRupees(value), Money.Zero, nature, buyer, on);
        var lines = new List<EntryLine>
        {
            new(buyer.Id, Money.FromRupees(value) + col.TcsAmount, DrCr.Debit),
            new(sales.Id, Money.FromRupees(value), DrCr.Credit),
            col.TcsPayableLine!,
        };
        return new LedgerService(c).Post(new Voucher(Guid.NewGuid(),
            c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Journal).Id, on, lines));
    }

    /// <summary>Replace keeps id, list position, legacy gate and base flag, and publishes the new terms.
    /// <para>Mutation: <c>ReplaceNatureOfGoods</c> appending instead of assigning by index → the count grows (red).</para></summary>
    [Fact]
    public void Replace_keeps_id_position_and_the_legacy_gate_and_publishes_the_new_terms()
    {
        var c = NewTcsCompany();
        var before = c.NaturesOfGoods.ToList();
        var legacy = c.FindNatureOfGoodsByCode("6CR")!;
        var index = before.IndexOf(legacy);

        var altered = legacy.WithTerms(legacy.CollectionCode, "Renamed", 20, 100, legacy.FvuCode, Money.FromRupees(60_00_000m));
        c.Tcs!.ReplaceNatureOfGoods(altered);

        Assert.Equal(before.Count, c.NaturesOfGoods.Count);
        Assert.Same(altered, c.NaturesOfGoods[index]);
        Assert.Equal(legacy.Id, altered.Id);
        Assert.True(altered.IsLegacy);
        Assert.Equal(legacy.LegacyCutoff, altered.LegacyCutoff);
        Assert.Equal(legacy.BaseIncludesGst, altered.BaseIncludesGst);
        Assert.True(altered.IsPredefined);
        Assert.Equal(20, c.FindNatureOfGoods(legacy.Id)!.RateWithPanBp);
    }

    [Fact]
    public void Replace_refuses_a_duplicate_code_an_unknown_id_and_a_predefined_flip()
    {
        var c = NewTcsCompany();
        var custom = AddCustom(c, "6ZZ");
        Assert.Throws<InvalidOperationException>(() =>
            c.Tcs!.ReplaceNatureOfGoods(custom.WithTerms("6CE", custom.Name, 100, 500, "6CE", null)));
        Assert.Throws<InvalidOperationException>(() => c.Tcs!.ReplaceNatureOfGoods(
            new NatureOfGoods(Guid.NewGuid(), "6QQ", "Ghost", 100, 500, "6QQ")));
        var scrap = c.FindNatureOfGoodsByCode("6CE")!;
        Assert.Throws<InvalidOperationException>(() => c.Tcs!.ReplaceNatureOfGoods(
            new NatureOfGoods(scrap.Id, "6CE", "Scrap", 100, 500, "6CE", isPredefined: false)));
        Assert.False(c.Tcs!.RemoveNatureOfGoods(Guid.NewGuid()));
    }

    /// <summary>The four deletion refusals, each by name, and an unused custom nature passes.
    /// <para>Mutations: deleting each branch of <c>NatureOfGoodsRules.EnsureDeletable</c> in turn reddens the
    /// matching <c>Assert.Throws</c>.</para></summary>
    [Fact]
    public void EnsureDeletable_refuses_predefined_posted_ledger_and_stock_item_by_name()
    {
        var c = NewTcsCompany();
        var predefined = c.FindNatureOfGoodsByCode("6CE")!;
        var ex = Assert.Throws<InvalidOperationException>(() => NatureOfGoodsRules.EnsureDeletable(c, predefined));
        Assert.Contains("'6CE'", ex.Message);
        Assert.Contains("predefined", ex.Message);

        var posted = AddCustom(c, "6PP");
        var v = PostTcs(c, posted, 10_000m, new DateOnly(2025, 6, 1));
        new LedgerService(c).Cancel(v.Id);   // a CANCELLED voucher still carries the line
        ex = Assert.Throws<InvalidOperationException>(() => NatureOfGoodsRules.EnsureDeletable(c, posted));
        Assert.Contains("1 posted voucher collects", ex.Message);
        Assert.Contains("Journal", ex.Message);

        var named = AddCustom(c, "6LL");
        AddLedger(c, "Timber Sales", "Sales Accounts", false).TcsNatureOfGoodsId = named.Id;
        ex = Assert.Throws<InvalidOperationException>(() => NatureOfGoodsRules.EnsureDeletable(c, named));
        Assert.Contains("'Timber Sales'", ex.Message);

        var itemNamed = AddCustom(c, "6II");
        var inv = new InventoryService(c);
        var item = inv.CreateStockItem("Teak Logs", inv.CreateStockGroup("Wood").Id, inv.CreateSimpleUnit("Cft", "Cubic feet").Id);
        item.TcsNatureOfGoodsId = itemNamed.Id;
        ex = Assert.Throws<InvalidOperationException>(() => NatureOfGoodsRules.EnsureDeletable(c, itemNamed));
        Assert.Contains("'Teak Logs'", ex.Message);
        Assert.Contains("stock item", ex.Message);

        var unused = AddCustom(c, "6UU");
        NatureOfGoodsRules.EnsureDeletable(c, unused);   // no throw
        Assert.True(c.Tcs!.RemoveNatureOfGoods(unused.Id));
        Assert.Null(c.FindNatureOfGoodsByCode("6UU"));
    }

    /// <summary>Code change: refused on a predefined nature and on a custom one a posted line carries; allowed on an
    /// unused custom; rate-only changes always allowed.
    /// <para>Mutations: deleting the <c>IsPredefined</c> branch, or the posted-voucher branch, of
    /// <c>EnsureAlterable</c> reddens the matching throw.</para></summary>
    [Fact]
    public void EnsureAlterable_locks_codes_on_predefined_and_posted_natures_only()
    {
        var c = NewTcsCompany();
        var scrap = c.FindNatureOfGoodsByCode("6CE")!;
        NatureOfGoodsRules.EnsureAlterable(c, scrap, scrap.WithTerms("6CE", "Scrap", 200, 500, "6CE", null));
        Assert.Throws<InvalidOperationException>(() =>
            NatureOfGoodsRules.EnsureAlterable(c, scrap, scrap.WithTerms("6CX", "Scrap", 100, 500, "6CX", null)));

        var posted = AddCustom(c, "6PQ");
        PostTcs(c, posted, 10_000m, new DateOnly(2025, 6, 1));
        var ex = Assert.Throws<InvalidOperationException>(() =>
            NatureOfGoodsRules.EnsureAlterable(c, posted, posted.WithTerms("6PR", posted.Name, 100, 500, "6PR", null)));
        Assert.Contains("re-file", ex.Message);

        var unused = AddCustom(c, "6UV");
        NatureOfGoodsRules.EnsureAlterable(c, unused, unused.WithTerms("6UW", unused.Name, 100, 500, "6UW", null));
    }

    /// <summary>
    /// 🔴 <b>T2-181 — the count is the vouchers the NEW FIGURES MOVE, excluding cancelled.</b> §194J(b), FY aggregate
    /// ₹50,000: vendor A ₹1,00,000 (withholds ₹10,000), vendor B ₹10,000 (below threshold, ₹0), and a CANCELLED
    /// ₹1,00,000 for A. The old measure (<see cref="NatureOfPaymentRules.VouchersCarrying"/>) says 3 for every
    /// alteration.
    /// <list type="bullet">
    ///   <item>rate 10% → 7.5%: A moves (₹7,500) and B's rate moves (1000 → 750 bp) — <b>2</b>, not 3;</item>
    ///   <item>threshold ₹50,000 → ₹1,50,000: only A moves (now below) — <b>1</b>;</item>
    ///   <item>threshold ₹50,000 → ₹60,000: nothing moves — <b>0</b>.</item>
    /// </list>
    /// <para>Mutations: dropping <c>.Where(v =&gt; !v.Cancelled)</c> → 3 / 2 (red); replacing the comparison with
    /// <c>true</c> → 3 / 3 / 3 (red).</para>
    /// </summary>
    [Fact]
    public void VouchersMovedBy_counts_only_live_vouchers_whose_withholding_the_new_terms_change()
    {
        var c = CompanyFactory.CreateSeeded("Moved Count Co", Fy);
        new TdsTcsService(c).EnableTds(new TdsConfig { Tan = "MUMA12345B" });
        var j = c.FindNatureOfPaymentByCode("194J(b)")!;
        var fees = AddLedger(c, "Fees", "Indirect Expenses", true);
        var a = AddLedger(c, "Vendor A", "Sundry Creditors", false);
        a.PartyPan = Pan; a.DeducteeType = DeducteeType.Firm;
        var b = AddLedger(c, "Vendor B", "Sundry Creditors", false);
        b.PartyPan = "AAQCS1234K"; b.DeducteeType = DeducteeType.Firm;

        Voucher Post(Domain.Ledger party, decimal gross, DateOnly on)
        {
            var g = Money.FromRupees(gross);
            var carve = new TdsService(c).BuildCarveOut(g, g, j, party, on);
            var lines = new List<EntryLine> { new(fees.Id, g, DrCr.Debit), carve.PartyLine };
            if (carve.TdsPayableLine is { } p) lines.Add(p);
            return new LedgerService(c).Post(new Voucher(Guid.NewGuid(),
                c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Journal).Id, on, lines));
        }

        Post(a, 1_00_000m, new DateOnly(2025, 5, 10));
        Post(b, 10_000m, new DateOnly(2025, 5, 11));
        var cancelled = Post(a, 1_00_000m, new DateOnly(2025, 5, 12));
        new LedgerService(c).Cancel(cancelled.Id);
        Assert.Equal(3, NatureOfPaymentRules.VouchersCarrying(c, j.Id).Count);

        var rate = j.WithTerms(j.SectionCode, j.Name, 750, 1500, j.FvuSectionCode, null, j.CumulativeThreshold);
        Assert.Equal(2, NatureOfPaymentRules.VouchersMovedBy(c, j, rate).Count);

        var high = j.WithTerms(j.SectionCode, j.Name, 1000, 2000, j.FvuSectionCode, null, Money.FromRupees(1_50_000m));
        var movedHigh = NatureOfPaymentRules.VouchersMovedBy(c, j, high);
        Assert.Single(movedHigh);
        Assert.Equal(new DateOnly(2025, 5, 10), movedHigh[0].Date);

        var low = j.WithTerms(j.SectionCode, j.Name, 1000, 2000, j.FvuSectionCode, null, Money.FromRupees(60_000m));
        Assert.Empty(NatureOfPaymentRules.VouchersMovedBy(c, j, low));
    }

    /// <summary>The TCS mirror: a rate change moves the live scrap sale, not the cancelled one; a name-only change
    /// moves nothing.</summary>
    [Fact]
    public void Nature_of_goods_VouchersMovedBy_excludes_cancelled_and_unmoved()
    {
        var c = NewTcsCompany();
        var scrap = c.FindNatureOfGoodsByCode("6CE")!;
        PostTcs(c, scrap, 1_00_000m, new DateOnly(2025, 6, 1));
        var cancelled = PostTcs(c, scrap, 50_000m, new DateOnly(2025, 6, 2));
        new LedgerService(c).Cancel(cancelled.Id);

        Assert.Single(NatureOfGoodsRules.VouchersMovedBy(c, scrap, scrap.WithTerms("6CE", "Scrap", 200, 500, "6CE", null)));
        Assert.Empty(NatureOfGoodsRules.VouchersMovedBy(c, scrap, scrap.WithTerms("6CE", "Scrap metal", 100, 500, "6CE", null)));
    }
}
