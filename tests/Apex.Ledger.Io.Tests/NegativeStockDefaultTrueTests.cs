using System.Text;
using System.Xml.Linq;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;

namespace Apex.Ledger.Io.Tests;

/// <summary>
/// The IO half of schema v50's <b>negative-stock warning toggle</b> (plan.md NS-4): JSON + XML lossless in both
/// states, and — the reason this file exists as its own fixture rather than three lines appended to the round-trip
/// suite — the <b>DEFAULT-TRUE ASYMMETRY</b> on every read path that can see a pre-v50 document.
///
/// <para>⚠️ <b>Why this is not boilerplate.</b> Every other company flag in the canonical model defaults FALSE, so
/// "the property is absent from the document" and "the flag is off" produce the same result and a forgotten default
/// is invisible. <see cref="Company.WarnOnNegativeStock"/> defaults <b>TRUE</b>, so they produce OPPOSITE results:
/// a pre-v50 export carries no <c>warnOnNegativeStock</c> key/attribute at all, and any read path that falls
/// through to <c>default(bool)</c> imports that book with negative-stock warnings silently switched OFF. Two
/// distinct mechanisms defend it and each is pinned separately below, because they fail independently:</para>
/// <list type="number">
/// <item><b>JSON</b> — <c>CanonicalCompanyDto.WarnOnNegativeStock</c> carries <c>= true</c>. System.Text.Json
/// leaves an absent property at whatever the initialiser set, so deleting that initialiser is the failure.</item>
/// <item><b>XML</b> — <c>CanonicalXml</c> reads the attribute through the <c>whenAbsent: true</c> overload.
/// "Simplifying" it back to the plain <c>Bool(e, name)</c> overload is the failure.</item>
/// </list>
/// <para>Both are asserted against documents with the field <b>physically removed</b>, which is what a genuine
/// pre-v50 export is — not against a hand-built DTO, which would exercise the initialiser and nothing else.</para>
/// </summary>
public class NegativeStockDefaultTrueTests
{
    private static readonly DateOnly FyStart = new(2025, 4, 1);

    private static Company Seeded(bool warn)
    {
        var c = CompanyFactory.CreateSeeded("Neg Stock Io Co", FyStart);
        c.WarnOnNegativeStock = warn;
        return c;
    }

    // ================================================================ lossless, both states

    [Fact]
    public void Json_roundTrips_the_flag_in_both_states()
    {
        foreach (var warn in new[] { true, false })
        {
            var (model, errors) = CanonicalJson.Parse(CanonicalJson.Export(Seeded(warn)));
            Assert.Empty(errors);
            Assert.Equal(warn, model!.Company.WarnOnNegativeStock);

            var target = CompanyFactory.CreateSeeded("Json Target", FyStart);
            Assert.True(new CompanyImportService(target).Apply(model).Applied);
            Assert.Equal(warn, target.WarnOnNegativeStock);
        }
    }

    [Fact]
    public void Xml_roundTrips_the_flag_in_both_states()
    {
        foreach (var warn in new[] { true, false })
        {
            var (model, errors) = CanonicalXml.Parse(CanonicalXml.Export(Seeded(warn)));
            Assert.Empty(errors);
            Assert.Equal(warn, model!.Company.WarnOnNegativeStock);

            var target = CompanyFactory.CreateSeeded("Xml Target", FyStart);
            Assert.True(new CompanyImportService(target).Apply(model).Applied);
            Assert.Equal(warn, target.WarnOnNegativeStock);
        }
    }

    /// <summary>
    /// The flag is written UNCONDITIONALLY, in both states. An "omit when it equals the default" optimisation would
    /// be a bug here specifically because the reader's default is TRUE: an omitted attribute would then be
    /// indistinguishable from "warnings on", so a company that deliberately turned them OFF would silently get them
    /// back on the next import.
    /// </summary>
    [Fact]
    public void The_flag_is_always_written_even_when_it_equals_the_default()
    {
        foreach (var warn in new[] { true, false })
        {
            var json = Encoding.UTF8.GetString(CanonicalJson.Export(Seeded(warn)));
            Assert.Contains($"\"warnOnNegativeStock\": {(warn ? "true" : "false")}", json, StringComparison.Ordinal);

            var xml = XDocument.Parse(Encoding.UTF8.GetString(CanonicalXml.Export(Seeded(warn))));
            var attr = xml.Descendants("company").First().Attribute("warnOnNegativeStock");
            Assert.NotNull(attr);
            Assert.Equal(warn ? "true" : "false", attr!.Value);
        }
    }

    // ================================================================ ⚠️ the default-true asymmetry

    /// <summary>
    /// READ PATH 1 — JSON. A genuine pre-v50 document simply has no <c>warnOnNegativeStock</c> key. Manufactured by
    /// deleting the key from a real export (not by hand-writing a document), so the assertion is about the parser,
    /// not about the fixture. It must import with warnings ON.
    /// </summary>
    [Fact]
    public void A_preV50_json_document_without_the_key_parses_as_warnings_ON()
    {
        var json = Encoding.UTF8.GetString(CanonicalJson.Export(Seeded(warn: true)));
        Assert.Contains("\"warnOnNegativeStock\"", json, StringComparison.Ordinal);   // the key really is there…
        var stripped = StripJsonKey(json, "warnOnNegativeStock");
        Assert.DoesNotContain("warnOnNegativeStock", stripped, StringComparison.Ordinal);   // …and really is gone

        var (model, errors) = CanonicalJson.Parse(Encoding.UTF8.GetBytes(stripped));
        Assert.Empty(errors);
        Assert.True(model!.Company.WarnOnNegativeStock);

        // …and it survives the import too, not just the parse.
        var target = CompanyFactory.CreateSeeded("Legacy Json Target", FyStart);
        target.WarnOnNegativeStock = false;              // start from OFF so TRUE can only come from the default
        Assert.True(new CompanyImportService(target).Apply(model).Applied);
        Assert.True(target.WarnOnNegativeStock);
    }

    /// <summary>
    /// READ PATH 2 — canonical XML. Same construction: the attribute is removed from a real export. This is the one
    /// that regresses if <c>CanonicalXml</c>'s <c>Bool(e, name, whenAbsent: true)</c> call is "simplified" back to
    /// the plain overload — a change that looks like tidying and silently disables warnings on every imported book.
    /// </summary>
    [Fact]
    public void A_preV50_xml_document_without_the_attribute_parses_as_warnings_ON()
    {
        var doc = XDocument.Parse(Encoding.UTF8.GetString(CanonicalXml.Export(Seeded(warn: true))));
        var companyEl = doc.Descendants("company").First();
        Assert.NotNull(companyEl.Attribute("warnOnNegativeStock"));
        companyEl.Attribute("warnOnNegativeStock")!.Remove();

        var (model, errors) = CanonicalXml.Parse(Encoding.UTF8.GetBytes(doc.ToString()));
        Assert.Empty(errors);
        Assert.True(model!.Company.WarnOnNegativeStock);

        var target = CompanyFactory.CreateSeeded("Legacy Xml Target", FyStart);
        target.WarnOnNegativeStock = false;              // start from OFF so TRUE can only come from the default
        Assert.True(new CompanyImportService(target).Apply(model).Applied);
        Assert.True(target.WarnOnNegativeStock);
    }

    /// <summary>
    /// The other half of the contract, and the reason the default cannot simply be "always true": an EXPLICIT
    /// <c>false</c> must survive both formats. A "default" that overrode a present value would be worse than the bug
    /// it was meant to fix — the operator's own setting would be unsettable.
    /// </summary>
    [Fact]
    public void An_explicit_false_is_never_overridden_by_the_default()
    {
        var (jsonModel, jsonErrors) = CanonicalJson.Parse(CanonicalJson.Export(Seeded(warn: false)));
        Assert.Empty(jsonErrors);
        Assert.False(jsonModel!.Company.WarnOnNegativeStock);

        var (xmlModel, xmlErrors) = CanonicalXml.Parse(CanonicalXml.Export(Seeded(warn: false)));
        Assert.Empty(xmlErrors);
        Assert.False(xmlModel!.Company.WarnOnNegativeStock);

        // Into a target whose flag is ON: the import must turn it OFF.
        var target = CompanyFactory.CreateSeeded("Explicit False Target", FyStart);
        Assert.True(target.WarnOnNegativeStock);
        Assert.True(new CompanyImportService(target).Apply(jsonModel).Applied);
        Assert.False(target.WarnOnNegativeStock);
    }

    /// <summary>
    /// READ PATH 4's Io counterpart: a failed import must RESTORE the flag, not leave the partially-applied value or
    /// silently reset it to the default. The <c>CompanyHeaderSnapshot</c> is what carries it — an omission there is
    /// invisible in every happy-path test.
    /// </summary>
    [Fact]
    public void A_rolled_back_import_restores_the_flag_it_found()
    {
        var (model, errors) = CanonicalJson.Parse(CanonicalJson.Export(Seeded(warn: true)));
        Assert.Empty(errors);

        // Corrupt a voucher so the batch is rejected AFTER the company header has been applied.
        var corrupted = model! with
        {
            Payload = model.Payload with
            {
                InventoryVouchers = model.Payload.InventoryVouchers
                    .Append(new InventoryVoucherDto { Id = Guid.NewGuid(), TypeId = Guid.NewGuid(), Date = "2025-04-05" })
                    .ToList(),
            },
        };

        var target = CompanyFactory.CreateSeeded("Rollback Target", FyStart);
        target.WarnOnNegativeStock = false;                       // the pre-existing setting the rollback must restore

        Assert.False(new CompanyImportService(target).Apply(corrupted).Applied);
        Assert.False(target.WarnOnNegativeStock);
    }

    // ---- helpers ----

    /// <summary>Removes a <c>"key": value,</c> line from indented canonical JSON — the crude, obvious way to
    /// manufacture a document that predates the property.</summary>
    private static string StripJsonKey(string json, string key)
    {
        var kept = json
            .Split('\n')
            .Where(line => !line.TrimStart().StartsWith($"\"{key}\":", StringComparison.Ordinal))
            .ToList();
        return string.Join("\n", kept);
    }
}
