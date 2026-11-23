using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Xunit;
using Domain = Apex.Ledger.Domain;

namespace Apex.Ledger.Tests;

/// <summary>
/// Census 6.28 (wave 12, track hf) — the engine half of Nature of Payment ALTER and DELETE:
/// <see cref="TdsConfig.ReplaceNatureOfPayment"/>, <see cref="TdsConfig.RemoveNatureOfPayment"/>,
/// <see cref="NatureOfPayment.WithTerms"/> and <see cref="NatureOfPaymentRules"/>. The window-level proof, through
/// the real keys and a reloaded book, is <c>Apex.Desktop.Tests.NatureOfPaymentAlterDeleteTests</c>.
/// </summary>
public class NatureOfPaymentAlterDeleteRulesTests
{
    private static Company NewTdsCompany()
    {
        var c = CompanyFactory.CreateSeeded("NOP Rules Co", new DateOnly(2025, 4, 1));
        new TdsTcsService(c).EnableTds(new TdsConfig { Tan = "MUMA12345B" });
        return c;
    }

    private static NatureOfPayment AddCustom(Company c, string section)
    {
        var n = new NatureOfPayment(Guid.NewGuid(), section, "Custom " + section, 100, 2000, "9" + section);
        c.Tds!.AddNatureOfPayment(n);
        return n;
    }

    /// <summary>Replace keeps the list POSITION (the store's rowid order) and the id; the new terms are what
    /// <see cref="Company.FindNatureOfPayment"/> returns.
    /// <para>Mutation: <c>ReplaceNatureOfPayment</c> appending instead of assigning by index → count grows and the
    /// position moves (red).</para></summary>
    [Fact]
    public void Replace_keeps_id_and_position_and_publishes_the_new_terms()
    {
        var c = NewTdsCompany();
        var before = c.NaturesOfPayment.ToList();
        var target = c.FindNatureOfPaymentByCode("194J(b)")!;
        var index = before.IndexOf(target);

        var altered = target.WithTerms(target.SectionCode, target.Name, 750, 1500, target.FvuSectionCode,
            target.SingleTransactionThreshold, Money.FromRupees(2_00_000m));
        c.Tds!.ReplaceNatureOfPayment(altered);

        Assert.Equal(before.Count, c.NaturesOfPayment.Count);
        Assert.Same(altered, c.NaturesOfPayment[index]);
        Assert.Equal(target.Id, c.FindNatureOfPayment(target.Id)!.Id);
        Assert.Equal(750, c.FindNatureOfPayment(target.Id)!.RateWithPanBp);
        Assert.True(c.FindNatureOfPayment(target.Id)!.IsPredefined);
        Assert.Equal(target.EffectiveFrom, altered.EffectiveFrom);
    }

    /// <summary>Replace refuses a duplicate section code and an unknown id; remove reports a miss.</summary>
    [Fact]
    public void Replace_refuses_a_duplicate_section_code_and_an_unknown_id()
    {
        var c = NewTdsCompany();
        var custom = AddCustom(c, "194ZZ");
        var dup = custom.WithTerms("194C", custom.Name, 100, 2000, custom.FvuSectionCode, null, null);
        var ex = Assert.Throws<InvalidOperationException>(() => c.Tds!.ReplaceNatureOfPayment(dup));
        Assert.Contains("194C", ex.Message);

        var stranger = new NatureOfPayment(Guid.NewGuid(), "194QQ", "Stranger", 100, 2000, "94QQ");
        Assert.Throws<InvalidOperationException>(() => c.Tds!.ReplaceNatureOfPayment(stranger));
        Assert.False(c.Tds!.RemoveNatureOfPayment(stranger.Id));
        Assert.True(c.Tds!.RemoveNatureOfPayment(custom.Id));
        Assert.Null(c.FindNatureOfPayment(custom.Id));
    }

    /// <summary>The deletion guard: predefined refused; a ledger-named custom refused naming the ledger; an unused
    /// custom passes.
    /// <para>Mutation: removing the ledger branch of <c>EnsureDeletable</c> → no throw for "194LL" (red).</para></summary>
    [Fact]
    public void EnsureDeletable_refuses_predefined_and_ledger_named_natures()
    {
        var c = NewTdsCompany();
        var predefined = c.FindNatureOfPaymentByCode("194C")!;
        Assert.Contains("predefined statutory section",
            Assert.Throws<InvalidOperationException>(() => NatureOfPaymentRules.EnsureDeletable(c, predefined)).Message);

        var named = AddCustom(c, "194LL");
        var rent = new Domain.Ledger(Guid.NewGuid(), "Rent Payable", c.FindGroupByName("Indirect Expenses")!.Id,
            Money.Zero, true) { TdsNatureOfPaymentId = named.Id };
        c.AddLedger(rent);
        var refusal = Assert.Throws<InvalidOperationException>(() => NatureOfPaymentRules.EnsureDeletable(c, named));
        Assert.Contains("'Rent Payable'", refusal.Message);
        Assert.Contains("194LL", refusal.Message);

        var unused = AddCustom(c, "194UU");
        NatureOfPaymentRules.EnsureDeletable(c, unused); // does not throw
    }

    /// <summary>The alteration guard: a predefined section's codes are locked; its rates are not.
    /// <para>Mutation: removing the <c>existing.IsPredefined</c> branch of <c>EnsureAlterable</c> → no throw on
    /// the code change (red).</para></summary>
    [Fact]
    public void EnsureAlterable_locks_predefined_codes_but_not_rates()
    {
        var c = NewTdsCompany();
        var p = c.FindNatureOfPaymentByCode("194J(b)")!;
        NatureOfPaymentRules.EnsureAlterable(c, p,
            p.WithTerms(p.SectionCode, "Renamed", 500, 1000, p.FvuSectionCode, null, null)); // allowed
        var code = Assert.Throws<InvalidOperationException>(() => NatureOfPaymentRules.EnsureAlterable(c, p,
            p.WithTerms(p.SectionCode, p.Name, p.RateWithPanBp, p.RateWithoutPanBp, "94J-Z",
                p.SingleTransactionThreshold, p.CumulativeThreshold)));
        Assert.Contains("Form-26Q code", code.Message);

        var custom = AddCustom(c, "194ZZ"); // no posted lines: its codes may change
        NatureOfPaymentRules.EnsureAlterable(c, custom,
            custom.WithTerms("194YY", custom.Name, 100, 2000, "94YY", null, null));
    }
}
