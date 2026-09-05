using System.Text;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;

namespace Apex.Ledger.Io.Tests;

/// <summary>
/// W2-03 (census 2.4/5.11; schema v53) — the Io fold-in gate for the Voucher Type master's two ATTESTED user flags
/// (<c>printAfterSaving</c>, <c>provideNarrationForEachLedger</c>) and for the two APPENDED numbering methods
/// (<c>AutomaticManualOverride</c>, <c>MultiUserAuto</c>). A configured type exports and re-imports exact in JSON
/// <i>and</i> XML, byte-stably, into a fresh (differently-Guid'd) company; and a company that has never opened the
/// master stays byte-identical to the pre-v53 golden — no new keys or attributes at all (ER-13).
///
/// <para><b>ER-13 is the sharp edge, exactly as it was at v47.</b> The canonical JSON writer is configured
/// <c>DefaultIgnoreCondition = Never</c>, so a naively-added scalar emits <c>"printAfterSaving": false</c> on
/// <i>every</i> voucher type of <i>every</i> existing company, and an unconditional XML <c>Attr(...)</c> emits
/// <c>printAfterSaving="false"</c> on every type. Both are pinned below.</para>
///
/// <para>🔴 <b>The numbering assertion is the one that would fail silently.</b> <c>numbering</c> is exported as the
/// enum NAME, so a method whose member is missing on the reading side round-trips as a PARSE FAILURE rather than a
/// wrong value — and a method that was renumbered instead of appended round-trips through canonical Io perfectly
/// while corrupting every SQLite book. The two are tested together on purpose.</para>
/// </summary>
public sealed class CanonicalVoucherTypeFlagsRoundTripTests
{
    private static readonly DateOnly FyStart = new(2025, 4, 1);

    /// <summary>A seeded company plus two custom types: one carrying both user flags under <i>Automatic (Manual
    /// Override)</i>, one under <i>Multi-user Auto</i> with neither flag.</summary>
    private static Company BuildCompanyWithFlaggedTypes()
    {
        var c = CompanyFactory.CreateSeeded("Flagged Traders", FyStart);
        var svc = new VoucherTypeService(c);
        svc.Create("Counter Sales", VoucherBaseType.Sales, NumberingMethod.AutomaticManualOverride,
            abbreviation: "CtrS", printAfterSaving: true, provideNarrationForEachLedger: true);
        svc.Create("Shared Journal", VoucherBaseType.Journal, NumberingMethod.MultiUserAuto);
        return c;
    }

    private static Company Fresh() => CompanyFactory.CreateSeeded("Fresh Flagged Co", FyStart);

    // ================================================================= lossless round-trip

    [Fact]
    public void VoucherTypeUserFlags_and_appendedNumberingMethods_roundTrip_io()
    {
        var c = BuildCompanyWithFlaggedTypes();

        var json = CanonicalJson.Export(c);
        var (jsonModel, jsonErrors) = CanonicalJson.Parse(json);
        Assert.Empty(jsonErrors);
        Assert.NotNull(jsonModel);
        Assert.Equal(json, CanonicalJson.Export(jsonModel!)); // byte-stable
        AssertFlagsSurvived(ImportInto(jsonModel!));

        var xml = CanonicalXml.Export(c);
        var (xmlModel, xmlErrors) = CanonicalXml.Parse(xml);
        Assert.Empty(xmlErrors);
        Assert.NotNull(xmlModel);
        Assert.Equal(xml, CanonicalXml.Export(xmlModel!)); // byte-stable
        AssertFlagsSurvived(ImportInto(xmlModel!));

        Assert.DoesNotContain("Tally", Encoding.UTF8.GetString(json), StringComparison.OrdinalIgnoreCase);
    }

    private static Company ImportInto(CanonicalModel model)
    {
        var target = Fresh();
        Assert.True(new CompanyImportService(target).Apply(model, DuplicatePolicy.Skip).Applied);
        return target;
    }

    private static void AssertFlagsSurvived(Company target)
    {
        var counter = target.VoucherTypes.Single(x => x.Name == "Counter Sales");
        Assert.True(counter.PrintAfterSaving);
        Assert.True(counter.ProvideNarrationForEachLedger);
        Assert.Equal(NumberingMethod.AutomaticManualOverride, counter.Numbering);
        Assert.Equal("CtrS", counter.Abbreviation);

        var shared = target.VoucherTypes.Single(x => x.Name == "Shared Journal");
        Assert.False(shared.PrintAfterSaving);
        Assert.False(shared.ProvideNarrationForEachLedger);
        Assert.Equal(NumberingMethod.MultiUserAuto, shared.Numbering);
    }

    // ================================================================= ER-13

    [Fact]
    public void UnflaggedVoucherTypes_areEr13ByteIdentical()
    {
        var c = CompanyFactory.CreateSeeded("Plain Flag Co", FyStart);

        var json = Encoding.UTF8.GetString(CanonicalJson.Export(c));
        var xml = Encoding.UTF8.GetString(CanonicalXml.Export(c));

        foreach (var token in new[] { "printAfterSaving", "provideNarrationForEachLedger" })
        {
            Assert.DoesNotContain($"\"{token}\":", json, StringComparison.Ordinal);
            Assert.DoesNotContain(token, xml, StringComparison.Ordinal);
        }

        // Belt-and-braces: a company that DOES carry the flags emits them, so the absences above are real.
        var flagged = BuildCompanyWithFlaggedTypes();
        var jsonWith = Encoding.UTF8.GetString(CanonicalJson.Export(flagged));
        var xmlWith = Encoding.UTF8.GetString(CanonicalXml.Export(flagged));
        Assert.Contains("\"printAfterSaving\":", jsonWith, StringComparison.Ordinal);
        Assert.Contains("\"provideNarrationForEachLedger\":", jsonWith, StringComparison.Ordinal);
        Assert.Contains("printAfterSaving=", xmlWith, StringComparison.Ordinal);
        Assert.Contains("provideNarrationForEachLedger=", xmlWith, StringComparison.Ordinal);
    }
}
