using System.Xml.Linq;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;

namespace Apex.Ledger.Io.Tests;

/// <summary>
/// <b>Census 10.1 — the party Credit Limit on the wire</b>, through both canonical serialisers.
///
/// <para><b>🔴 WHY THIS FILE EXISTS.</b> The credit limit is a <b>save-blocking</b> rule. If the canonical
/// export/import path dropped it — the recurring "Io bypass" defect class this repository has filed repeatedly —
/// then a company exported and re-imported would <b>accept invoices the original refused</b>, silently, with no
/// error anywhere. Nothing in <c>CreditLimitRulesTests</c>, <c>CreditLimitBlockTests</c> or
/// <c>CreditLimitSchemaTests</c> would notice: they never cross this boundary.</para>
///
/// <para><b>The distinction under test is three-valued, not two:</b> <c>null</c> ("no limit"), <c>0</c> ("a limit
/// of zero", which blocks EVERY credit transaction) and an ordinary amount. Collapsing the first two on the wire
/// would either unfreeze a deliberately blocked party or freeze every unlimited one on import, so the XML attribute
/// is written only when the limit is present and each of the three cases is asserted separately below.</para>
/// </summary>
public sealed class CanonicalCreditLimitRoundTripTests
{
    private static readonly DateOnly FyStart = new(2025, 4, 1);

    private static Domain.Ledger AddDebtor(Company c, string name)
    {
        var l = new Domain.Ledger(Guid.NewGuid(), name, c.FindGroupByName("Sundry Debtors")!.Id,
                           Money.Zero, openingIsDebit: true);
        c.AddLedger(l);
        return l;
    }

    /// <summary>Three parties covering all three states, plus both switches on exactly one of them so a mapper
    /// that hard-coded either flag cannot pass.</summary>
    private static Company BuildCompanyWithLimits()
    {
        var c = CompanyFactory.CreateSeeded("Limit Traders", FyStart);
        AddDebtor(c, "No Limit Ltd");                                  // null — no limit

        var zero = AddDebtor(c, "Zero Limit Ltd");
        zero.CreditLimit = Money.Zero;                                 // 0 — blocked entirely

        var normal = AddDebtor(c, "Normal Limit Ltd");
        normal.CreditLimit = Money.FromRupees(73_500.25m);             // an odd amount, not a round default
        normal.CheckCreditDaysOnEntry = true;
        normal.OverrideCreditLimitWithPostDated = true;
        return c;
    }

    private static Company Fresh() => CompanyFactory.CreateSeeded("Fresh Limit Co", FyStart);

    private static void AssertLimitsSurvived(Company target)
    {
        var none = target.FindLedgerByName("No Limit Ltd")!;
        var zero = target.FindLedgerByName("Zero Limit Ltd")!;
        var normal = target.FindLedgerByName("Normal Limit Ltd")!;

        Assert.Null(none.CreditLimit);
        Assert.False(none.CheckCreditDaysOnEntry);
        Assert.False(none.OverrideCreditLimitWithPostDated);

        Assert.NotNull(zero.CreditLimit);
        Assert.Equal(Money.Zero, zero.CreditLimit!.Value);
        Assert.False(zero.OverrideCreditLimitWithPostDated);

        Assert.Equal(Money.FromRupees(73_500.25m), normal.CreditLimit!.Value);
        Assert.True(normal.CheckCreditDaysOnEntry);
        Assert.True(normal.OverrideCreditLimitWithPostDated);
    }

    [Fact]
    public void The_credit_limit_block_round_trips_through_canonical_JSON()
    {
        var (model, errors) = CanonicalJson.Parse(CanonicalJson.Export(BuildCompanyWithLimits()));
        Assert.Empty(errors);
        Assert.NotNull(model);

        // On the wire: three distinct states, and the "no limit" one really is absent rather than zero.
        var dtos = model!.Payload.Ledgers.Where(l => l.Name.EndsWith("Limit Ltd", StringComparison.Ordinal)).ToList();
        Assert.Equal(3, dtos.Count);
        Assert.Null(dtos.Single(d => d.Name == "No Limit Ltd").CreditLimitPaisa);
        Assert.Equal(0L, dtos.Single(d => d.Name == "Zero Limit Ltd").CreditLimitPaisa);
        Assert.Equal(7_350_025L, dtos.Single(d => d.Name == "Normal Limit Ltd").CreditLimitPaisa);

        var target = Fresh();
        var result = new CompanyImportService(target).Apply(model, DuplicatePolicy.Skip);
        Assert.True(result.Applied, string.Join(" | ", result.Errors));
        AssertLimitsSurvived(target);
    }

    [Fact]
    public void The_credit_limit_block_round_trips_through_canonical_XML()
    {
        var (model, errors) = CanonicalXml.Parse(CanonicalXml.Export(BuildCompanyWithLimits()));
        Assert.Empty(errors);
        Assert.NotNull(model);

        var target = Fresh();
        var result = new CompanyImportService(target).Apply(model!, DuplicatePolicy.Skip);
        Assert.True(result.Applied, string.Join(" | ", result.Errors));
        AssertLimitsSurvived(target);
    }

    /// <summary>
    /// 🔴 The wire format keeps "no limit" and "a limit of zero" apart by <b>presence</b>, not by value: the XML
    /// attribute is absent for the first and reads "0" for the second. A writer that emitted <c>0</c> for both
    /// would freeze every unlimited party on import; one that omitted both would unfreeze a blocked party.
    /// </summary>
    [Fact]
    public void The_XML_omits_the_attribute_for_no_limit_and_writes_zero_for_a_limit_of_zero()
    {
        var xml = CanonicalXml.Export(BuildCompanyWithLimits());
        var doc = XDocument.Parse(System.Text.Encoding.UTF8.GetString(xml));

        var elements = doc.Descendants()
            .Where(e => (string?)e.Attribute("name") is { } n && n.EndsWith("Limit Ltd", StringComparison.Ordinal))
            .ToList();
        Assert.Equal(3, elements.Count);

        var none = elements.Single(e => (string?)e.Attribute("name") == "No Limit Ltd");
        var zero = elements.Single(e => (string?)e.Attribute("name") == "Zero Limit Ltd");

        Assert.Null(none.Attribute("creditLimitPaisa"));
        Assert.Equal("0", (string?)zero.Attribute("creditLimitPaisa"));
    }

    /// <summary>
    /// ER-13: a document written before v54 carries neither attribute, and must import to exactly the ledger it
    /// always did — <b>no limit</b>. Simulated by stripping the attributes from a current export, which is what an
    /// older document looks like.
    /// </summary>
    [Fact]
    public void A_document_without_the_credit_attributes_imports_with_no_limit()
    {
        var doc = XDocument.Parse(System.Text.Encoding.UTF8.GetString(CanonicalXml.Export(BuildCompanyWithLimits())));
        foreach (var name in new[]
                 { "creditLimitPaisa", "checkCreditDaysOnEntry", "overrideCreditLimitWithPostDated" })
            foreach (var a in doc.Descendants().Select(e => e.Attribute(name)).Where(a => a is not null).Select(a => a!).ToList())
                a.Remove();

        var (model, errors) = CanonicalXml.Parse(System.Text.Encoding.UTF8.GetBytes(doc.ToString()));
        Assert.Empty(errors);

        var target = Fresh();
        Assert.True(new CompanyImportService(target).Apply(model!, DuplicatePolicy.Skip).Applied);

        foreach (var n in new[] { "No Limit Ltd", "Zero Limit Ltd", "Normal Limit Ltd" })
        {
            var l = target.FindLedgerByName(n)!;
            Assert.Null(l.CreditLimit);
            Assert.False(l.CheckCreditDaysOnEntry);
            Assert.False(l.OverrideCreditLimitWithPostDated);
        }
    }
}
