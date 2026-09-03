using Apex.Ledger.Domain;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// The narrow <see cref="MasterGstDetails"/> block the Stock Group, the accounting Group and the company default
/// carry (schema v51; plan.md Phase 10.10 WF-1 / register IV-1).
///
/// <para>Two things are pinned here. First, the <b>shape</b>: this block deliberately carries FOUR fields and not
/// the item block's cess/RSP/RCM/§17(5) surface, because those are read item-first and mean nothing at these three
/// levels — a later "why not just reuse <see cref="StockItemGstDetails"/>" would publish a dozen inert fields on
/// three masters. Second, the <b>validation parity</b>: an HSN or a rate that <see cref="StockItemGstDetails"/>
/// rejects must be rejected here too, or the exact same bad value becomes reachable simply by typing it on a Stock
/// Group instead of on a Stock Item.</para>
/// </summary>
public class MasterGstDetailsTests
{
    // ---------------------------------------------------------------- defaults

    [Fact]
    public void A_new_block_is_taxable_goods_with_no_hsn_and_no_rate()
    {
        var g = new MasterGstDetails();

        Assert.Null(g.HsnSac);
        Assert.Null(g.RateBasisPoints);
        Assert.Equal(GstTaxability.Taxable, g.Taxability);
        Assert.Equal(GstSupplyType.Goods, g.SupplyType);
        Assert.True(g.IsTaxable);
        g.EnsureValid();   // an empty block is valid — it simply answers nothing
    }

    [Theory]
    [InlineData(GstTaxability.Exempt)]
    [InlineData(GstTaxability.NilRated)]
    [InlineData(GstTaxability.NonGst)]
    public void Only_a_Taxable_block_is_taxable(GstTaxability taxability)
    {
        Assert.False(new MasterGstDetails { Taxability = taxability }.IsTaxable);
    }

    // ---------------------------------------------------------------- HSN/SAC shape

    [Theory]
    [InlineData("7318")]        // 4
    [InlineData("998313")]      // 6
    [InlineData("85171213")]    // 8
    public void A_4_6_or_8_digit_hsn_is_accepted(string hsn)
    {
        new MasterGstDetails { HsnSac = hsn, RateBasisPoints = 1237 }.EnsureValid();
    }

    [Theory]
    [InlineData("731")]         // 3 — too short
    [InlineData("73185")]       // 5 — not a permitted length
    [InlineData("1234567")]     // 7 — not a permitted length
    [InlineData("851712134")]   // 9 — too long
    [InlineData("8517121A")]    // 8 chars but not numeric
    public void A_malformed_hsn_is_rejected_by_length_or_by_digits(string hsn)
    {
        var ex = Assert.Throws<ArgumentException>(() => new MasterGstDetails { HsnSac = hsn }.EnsureValid());
        Assert.Contains("must be 4, 6 or 8 digits", ex.Message, StringComparison.Ordinal);
        Assert.Contains(hsn, ex.Message, StringComparison.Ordinal);   // the offending value is named
    }

    // ---------------------------------------------------------------- rate

    [Fact]
    public void A_negative_rate_is_rejected()
    {
        var ex = Assert.Throws<ArgumentException>(
            () => new MasterGstDetails { RateBasisPoints = -1 }.EnsureValid());
        Assert.Contains("must be ≥ 0", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The rule that actually protects a figure: an Exempt/Nil/Non-GST master must not carry a positive rate, or the
    /// hierarchy could hand a resolver a rate for a supply that bears no tax. Zero is allowed — that is how a
    /// nil-rated master is legitimately expressed.
    /// </summary>
    [Theory]
    [InlineData(GstTaxability.Exempt)]
    [InlineData(GstTaxability.NilRated)]
    [InlineData(GstTaxability.NonGst)]
    public void A_nonTaxable_master_may_carry_zero_but_not_a_positive_rate(GstTaxability taxability)
    {
        new MasterGstDetails { Taxability = taxability, RateBasisPoints = 0 }.EnsureValid();

        var ex = Assert.Throws<ArgumentException>(
            () => new MasterGstDetails { Taxability = taxability, RateBasisPoints = 1237 }.EnsureValid());
        Assert.Contains(taxability.ToString(), ex.Message, StringComparison.Ordinal);
        Assert.Contains("1237", ex.Message, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- validation parity with the item block

    /// <summary>
    /// The parity that stops the hierarchy from becoming a way around the item block's guards: every value
    /// <see cref="StockItemGstDetails.EnsureValid"/> rejects on these four shared fields is rejected by
    /// <see cref="MasterGstDetails.EnsureValid"/> too, and every value it accepts is accepted.
    ///
    /// <para>🔴 <b><paramref name="expectThrow"/> exists because the owed review (lens 2 finding 5) measured this
    /// test INERT.</b> It used to assert only <c>Assert.Equal(itemThrew, masterThrew)</c> — agreement — so its one
    /// failure mode was DISAGREEMENT and it never said what the agreed answer should be. Relaxing the HSN rule in
    /// BOTH validators identically (the exact change a "simplification" would make) left this test <b>green</b>,
    /// including the two rows that exist specifically for that rule. Worse, it is the only test in the repository
    /// that touches <see cref="StockItemGstDetails.EnsureValid"/>'s HSN branch at all, so that branch had no live
    /// guard anywhere. Pinning the expected verdict per row fixes both: each row now fails on its own if either
    /// validator changes, and still fails if the two disagree.</para>
    /// </summary>
    [Theory]
    [InlineData("7318", 1237, GstTaxability.Taxable, false)]
    [InlineData("998313", 0, GstTaxability.NilRated, false)]
    [InlineData(null, null, GstTaxability.Taxable, false)]
    [InlineData("85171213", null, GstTaxability.Taxable, false)]   // 8-digit HSN, no rate
    [InlineData("731", 1237, GstTaxability.Taxable, true)]         // bad HSN length
    [InlineData("73185", null, GstTaxability.Taxable, true)]       // 5 digits — not a permitted length
    [InlineData("8517121A", 1237, GstTaxability.Taxable, true)]    // non-numeric HSN
    [InlineData("", null, GstTaxability.Taxable, true)]            // empty string is not "unset"
    [InlineData("7318", -1, GstTaxability.Taxable, true)]          // negative rate
    [InlineData("7318", 1237, GstTaxability.Exempt, true)]         // positive rate on a non-taxable block
    public void The_master_block_and_the_item_block_agree_on_every_shared_rule(
        string? hsn, int? rateBp, GstTaxability taxability, bool expectThrow)
    {
        var masterThrew = Threw(() => new MasterGstDetails
        {
            HsnSac = hsn, RateBasisPoints = rateBp, Taxability = taxability,
        }.EnsureValid());

        var itemThrew = Threw(() => new StockItemGstDetails
        {
            HsnSac = hsn, RateBasisPoints = rateBp, Taxability = taxability,
        }.EnsureValid());

        // The RULE, on each block independently — this is what makes the row bite when both are relaxed together.
        Assert.Equal(expectThrow, masterThrew);
        Assert.Equal(expectThrow, itemThrew);
        // …and the AGREEMENT, which is what makes it bite when only one is relaxed.
        Assert.Equal(itemThrew, masterThrew);
    }

    // ---------------------------------------------------------------- the company-level call site

    /// <summary>
    /// <see cref="GstConfig.EnsureValid"/> validates <see cref="GstConfig.DefaultGst"/> — the company block is the
    /// LAST level of both resolution orders, so a malformed one would surface as a bad rate on any line no other
    /// level answered.
    ///
    /// <para>🔴 <b>This test exists because that call site had ZERO coverage in the project that owns it</b>
    /// (owed-review lens 2 finding 10): commenting out <c>DefaultGst?.EnsureValid()</c> together with both
    /// <c>ImportPlan</c> call sites left all of <c>Apex.Ledger.Tests</c> green — the only reds were three Io theory
    /// cases, in a different project. <see cref="MasterGstDetailsTests"/> never constructed a
    /// <see cref="GstConfig"/> at all.</para>
    /// </summary>
    [Fact]
    public void A_company_default_block_is_validated_by_GstConfig_EnsureValid()
    {
        var config = new GstConfig
        {
            Enabled = true,
            Gstin = "27AAPFU0939F1ZV",
            HomeStateCode = "27",
            DefaultGst = new MasterGstDetails { HsnSac = "7318", RateBasisPoints = 1741 },
        };
        config.EnsureValid();   // a well-formed default block passes

        config.DefaultGst = new MasterGstDetails { HsnSac = "1234567" };   // 7 digits
        var ex = Assert.Throws<ArgumentException>(config.EnsureValid);
        Assert.Contains("must be 4, 6 or 8 digits", ex.Message, StringComparison.Ordinal);

        config.DefaultGst = new MasterGstDetails { Taxability = GstTaxability.Exempt, RateBasisPoints = 1741 };
        Assert.Contains("must not carry a positive GST rate",
            Assert.Throws<ArgumentException>(config.EnsureValid).Message, StringComparison.Ordinal);

        config.DefaultGst = new MasterGstDetails { RateBasisPoints = -1 };
        Assert.Contains("must be ≥ 0",
            Assert.Throws<ArgumentException>(config.EnsureValid).Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// 🔴 <b>The limit of all of the above, asserted rather than described</b> (owed-review lens 2 finding 4). None
    /// of the three <see cref="MasterGstDetails.EnsureValid"/> call sites is on a domain WRITE path: the aggregate
    /// accepts a malformed block on a Stock Group, on an accounting Group and on the company default without
    /// complaint, and only the canonical import refuses it. This test pins that fact so it stays a KNOWN limit
    /// rather than a surprise — <b>if a future slice validates on assignment, this test goes red and should be
    /// deleted with a note, not weakened.</b> It is the same shape as the <c>Company.EnsureValid</c> limit recorded
    /// for W0-2a, and it is why the deferred master-GST screens must validate on save.
    /// </summary>
    [Fact]
    public void KNOWN_LIMIT_the_domain_accepts_a_malformed_block_because_only_the_import_validates()
    {
        var malformed = new MasterGstDetails { HsnSac = "1234567", RateBasisPoints = -9 };

        // Assignment on either master is unguarded…
        var stockGroup = new StockGroup(Guid.NewGuid(), "Unvalidated SG") { Gst = malformed };
        var group = new Group(Guid.NewGuid(), "Unvalidated Grp", GroupNature.Income) { Gst = malformed };
        Assert.Equal("1234567", stockGroup.Gst!.HsnSac);
        Assert.Equal(-9, group.Gst!.RateBasisPoints);

        // …and so is the company default, until GstConfig.EnsureValid is explicitly called.
        var config = new GstConfig { Enabled = true, DefaultGst = malformed };
        Assert.Same(malformed, config.DefaultGst);

        // The block itself knows it is bad — nothing asks it.
        Assert.Throws<ArgumentException>(malformed.EnsureValid);
    }

    private static bool Threw(Action a)
    {
        try { a(); return false; }
        catch (ArgumentException) { return true; }
    }
}
