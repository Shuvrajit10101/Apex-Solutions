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
    /// <see cref="MasterGstDetails.EnsureValid"/> too, and every value it accepts is accepted. Asserted by running
    /// BOTH validators over the same inputs and comparing whether each threw — so a future relaxation of either one
    /// alone fails here.
    /// </summary>
    [Theory]
    [InlineData("7318", 1237, GstTaxability.Taxable)]
    [InlineData("998313", 0, GstTaxability.NilRated)]
    [InlineData(null, null, GstTaxability.Taxable)]
    [InlineData("731", 1237, GstTaxability.Taxable)]           // bad HSN length
    [InlineData("8517121A", 1237, GstTaxability.Taxable)]      // non-numeric HSN
    [InlineData("7318", -1, GstTaxability.Taxable)]            // negative rate
    [InlineData("7318", 1237, GstTaxability.Exempt)]           // positive rate on a non-taxable block
    public void The_master_block_and_the_item_block_agree_on_every_shared_rule(
        string? hsn, int? rateBp, GstTaxability taxability)
    {
        var masterThrew = Threw(() => new MasterGstDetails
        {
            HsnSac = hsn, RateBasisPoints = rateBp, Taxability = taxability,
        }.EnsureValid());

        var itemThrew = Threw(() => new StockItemGstDetails
        {
            HsnSac = hsn, RateBasisPoints = rateBp, Taxability = taxability,
        }.EnsureValid());

        Assert.Equal(itemThrew, masterThrew);
    }

    private static bool Threw(Action a)
    {
        try { a(); return false; }
        catch (ArgumentException) { return true; }
    }
}
