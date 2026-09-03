using System.Text;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;

namespace Apex.Ledger.Io.Tests;

/// <summary>
/// Voucher-numbering S3 (numbering-design-v2 §6.6) — the Io fold-in gate for the per-type numbering config. A
/// configured type (date-keyed Prefix/Suffix rows + Width + Prefill + Prevent-duplicate) exports and re-imports
/// exact in JSON <i>and</i> XML, byte-stably, into a fresh (differently-Guid'd) company; and a never-configured
/// company stays byte-identical to the pre-feature golden — no new keys/attributes/elements at all (ER-13).
///
/// <para><b>ER-13 is the sharp edge.</b> The canonical JSON writer is configured
/// <c>DefaultIgnoreCondition = Never</c>, so naively-added scalars would emit <c>"preventDuplicate": false</c> (and
/// two empty arrays) on <i>every</i> voucher type of <i>every</i> existing company; and unconditional XML
/// <c>Attr(...)</c> would emit <c>preventDuplicate="false"</c> on every type. Both are pinned below.</para>
/// </summary>
public sealed class CanonicalNumberingRoundTripTests
{
    private static readonly DateOnly FyStart = new(2025, 4, 1);

    /// <summary>A seeded company plus one fully-configured custom Sales type: 2 date-keyed prefix rows, 1 suffix
    /// row, width 4, prefill on, prevent-duplicate on.</summary>
    private static Company BuildCompanyWithConfiguredType()
    {
        var c = CompanyFactory.CreateSeeded("Numbered Traders", FyStart);
        c.AddVoucherType(new VoucherType(Guid.NewGuid(), "Configured Sales", VoucherBaseType.Sales,
            numbering: NumberingMethod.Automatic,
            preventDuplicate: true, numberWidth: 4, prefillWithZero: true,
            prefixes: new[]
            {
                new VoucherNumberAffix(Guid.NewGuid(), new DateOnly(2025, 4, 1), "25-26/"),
                new VoucherNumberAffix(Guid.NewGuid(), new DateOnly(2026, 4, 1), "26-27/"),
            },
            suffixes: new[]
            {
                new VoucherNumberAffix(Guid.NewGuid(), new DateOnly(2025, 4, 1), "/A"),
            }));
        return c;
    }

    private static Company Fresh() => CompanyFactory.CreateSeeded("Fresh Numbered Co", FyStart);

    // ================================================================= lossless round-trip

    [Fact]
    public void NumberingRules_roundTrip_io()
    {
        var c = BuildCompanyWithConfiguredType();

        // JSON: byte-stable, and imports into a fresh company with every rule preserved.
        var json = CanonicalJson.Export(c);
        var (jsonModel, jsonErrors) = CanonicalJson.Parse(json);
        Assert.Empty(jsonErrors);
        Assert.NotNull(jsonModel);
        Assert.Equal(json, CanonicalJson.Export(jsonModel!)); // byte-stable
        AssertConfigSurvived(ImportInto(jsonModel!));

        // XML: byte-stable, and imports into a fresh company with every rule preserved.
        var xml = CanonicalXml.Export(c);
        var (xmlModel, xmlErrors) = CanonicalXml.Parse(xml);
        Assert.Empty(xmlErrors);
        Assert.NotNull(xmlModel);
        Assert.Equal(xml, CanonicalXml.Export(xmlModel!)); // byte-stable
        AssertConfigSurvived(ImportInto(xmlModel!));

        Assert.DoesNotContain("Tally", Encoding.UTF8.GetString(json), StringComparison.OrdinalIgnoreCase);
    }

    private static Company ImportInto(CanonicalModel model)
    {
        var target = Fresh();
        Assert.True(new CompanyImportService(target).Apply(model, DuplicatePolicy.Skip).Applied);
        return target;
    }

    /// <summary>The imported "Configured Sales" type carries all three scalars and both date-keyed affix lists,
    /// re-minted into the target — dates + particulars exact, prefixes ordered by ApplicableFrom.</summary>
    private static void AssertConfigSurvived(Company target)
    {
        var t = target.VoucherTypes.Single(x => x.Name == "Configured Sales");
        Assert.True(t.PreventDuplicate);
        Assert.Equal(4, t.NumberWidth);
        Assert.True(t.PrefillWithZero);

        Assert.Equal(2, t.Prefixes.Count);
        Assert.Equal(new DateOnly(2025, 4, 1), t.Prefixes[0].ApplicableFrom);
        Assert.Equal("25-26/", t.Prefixes[0].Particulars);
        Assert.Equal(new DateOnly(2026, 4, 1), t.Prefixes[1].ApplicableFrom);
        Assert.Equal("26-27/", t.Prefixes[1].Particulars);

        var suffix = Assert.Single(t.Suffixes);
        Assert.Equal(new DateOnly(2025, 4, 1), suffix.ApplicableFrom);
        Assert.Equal("/A", suffix.Particulars);
    }

    // ================================================================= ER-13

    [Fact]
    public void EmptyNumbering_isEr13ByteIdentical()
    {
        // A company whose voucher types carry NO numbering config must serialise EXACTLY as it did before v47 — no
        // preventDuplicate / numberWidth / prefillWithZero keys or attributes, no prefixes/suffixes lists.
        var c = CompanyFactory.CreateSeeded("Plain Co", FyStart);

        var json = Encoding.UTF8.GetString(CanonicalJson.Export(c));
        var xml = Encoding.UTF8.GetString(CanonicalXml.Export(c));

        foreach (var token in new[] { "preventDuplicate", "numberWidth", "prefillWithZero", "prefixes", "suffixes" })
        {
            Assert.DoesNotContain($"\"{token}\":", json, StringComparison.Ordinal);
            Assert.DoesNotContain(token, xml, StringComparison.Ordinal);
        }

        // Belt-and-braces: a company that DOES carry numbering config emits them, so the assertions above are
        // detecting a real absence rather than passing because the writer never emits them at all.
        var configured = BuildCompanyWithConfiguredType();
        var jsonWith = Encoding.UTF8.GetString(CanonicalJson.Export(configured));
        var xmlWith = Encoding.UTF8.GetString(CanonicalXml.Export(configured));
        Assert.Contains("\"preventDuplicate\":", jsonWith, StringComparison.Ordinal);
        Assert.Contains("\"prefixes\":", jsonWith, StringComparison.Ordinal);
        Assert.Contains("preventDuplicate=", xmlWith, StringComparison.Ordinal);
        Assert.Contains("<prefixes>", xmlWith, StringComparison.Ordinal);
    }
}
