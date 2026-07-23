using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// Numbering slice S1 (numbering-design-v2 §2.1, §9 S1) — the pure <see cref="VoucherNumberFormatter.Render"/>
/// projection: a date-selected Prefix/Suffix, a non-truncating left-pad by <see cref="VoucherType.NumberWidth"/>
/// (space or '0' per <see cref="VoucherType.PrefillWithZero"/>), and the two empty-output guards
/// (Numbering None / number ≤ 0). Nothing here reaches a posted voucher — the formatter is not yet wired to any
/// render site — so with an empty config <c>Render</c> is byte-identical to today's bare <c>int.ToString()</c>
/// (the ER-13 render guard). Every test carries a named source mutation proven to turn it RED.
/// </summary>
public class VoucherNumberFormatterTests
{
    private static readonly DateOnly Apr1_25 = new(2025, 4, 1);
    private static readonly DateOnly Apr1_26 = new(2026, 4, 1);
    private static readonly DateOnly May5_25 = new(2025, 5, 5);
    private static readonly DateOnly Mar10_26 = new(2026, 3, 10);
    private static readonly DateOnly AnyDate = new(2025, 6, 15);

    // A minimal Automatic Sales type with the given numbering config; identity/other flags are irrelevant to Render.
    private static VoucherType MakeType(
        NumberingMethod numbering = NumberingMethod.Automatic,
        int width = 0,
        bool prefill = false,
        IEnumerable<VoucherNumberAffix>? prefixes = null,
        IEnumerable<VoucherNumberAffix>? suffixes = null)
        => new(
            Guid.NewGuid(),
            "Test Type",
            VoucherBaseType.Sales,
            numbering: numbering,
            numberWidth: width,
            prefillWithZero: prefill,
            prefixes: prefixes,
            suffixes: suffixes);

    // {Prefix 1-Apr-25 "25-26/"}, Width 3, Prefill on, Render(_,7,05-May-25) == "25-26/007".
    // Mutation: PadLeft -> PadRight in the formatter -> "25-26/700" != "25-26/007" -> RED.
    [Fact]
    public void Render_padsAndAffixesByDate()
    {
        var type = MakeType(
            width: 3,
            prefill: true,
            prefixes: new[] { new VoucherNumberAffix(Guid.NewGuid(), Apr1_25, "25-26/") });

        Assert.Equal("25-26/007", VoucherNumberFormatter.Render(type, 7, May5_25));
    }

    // Two prefix rows (1-Apr-25 "25-26/", 1-Apr-26 "26-27/"): the row selected is the greatest ApplicableFrom
    // that is on/before the voucher date. A 10-Mar-26 voucher renders "25-26/..." (the FY26-27 row is not yet in
    // force); a voucher dated exactly 1-Apr-26 renders "26-27/..." (on/before is inclusive at the boundary).
    // Mutation: selector "<= date" -> "< date" -> the boundary voucher drops to "25-26/1" != "26-27/1" -> RED.
    [Fact]
    public void Render_picksLatestAffixOnOrBeforeDate()
    {
        var type = MakeType(prefixes: new[]
        {
            new VoucherNumberAffix(Guid.NewGuid(), Apr1_25, "25-26/"),
            new VoucherNumberAffix(Guid.NewGuid(), Apr1_26, "26-27/"),
        });

        Assert.Equal("25-26/1", VoucherNumberFormatter.Render(type, 1, Mar10_26)); // future row not yet applicable
        Assert.Equal("26-27/1", VoucherNumberFormatter.Render(type, 1, Apr1_26));  // boundary: on/before is inclusive
    }

    // Empty rules + Width 0 (all ctor defaults) -> Render is exactly number.ToString() -> the ER-13 render guard.
    // Mutation: change the VoucherType ctor default numberWidth 0 -> 3 -> this default-config voucher renders
    // " 42" (Width 3, space pad) != "42" -> RED. (Width 1 cannot bite: PadLeft(1) is a no-op on any integer.)
    [Fact]
    public void Render_emptyRulesWidth0_isBareInt()
    {
        var type = new VoucherType(Guid.NewGuid(), "Test Type", VoucherBaseType.Sales); // rely on all defaults

        Assert.Equal("42", VoucherNumberFormatter.Render(type, 42, AnyDate));
    }

    // Width 3 with a 4-digit number: the pad NEVER truncates -> "1000".
    // Mutation: truncate the core to the width (e.g. .Substring(0, width)) -> "100" != "1000" -> RED.
    [Fact]
    public void Render_widthNeverTruncates()
    {
        var type = MakeType(width: 3);

        Assert.Equal("1000", VoucherNumberFormatter.Render(type, 1000, AnyDate));
    }

    // Two prefix rows share an ApplicableFrom; the tie-break is the greatest Id, applied stably regardless of the
    // rows' storage order. The greater-Id row ("HI/") must win in BOTH input orderings.
    // Mutation: drop ".ThenBy(Id)" -> selection becomes storage-order dependent -> the greater-Id-first ordering
    // returns "LO/1" != "HI/1" -> RED.
    [Fact]
    public void Render_tieBreakIsDeterministic()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var greater = a.CompareTo(b) > 0 ? a : b;
        var lesser = a.CompareTo(b) > 0 ? b : a;
        var hi = new VoucherNumberAffix(greater, Apr1_25, "HI/");
        var lo = new VoucherNumberAffix(lesser, Apr1_25, "LO/");

        var greaterFirst = MakeType(prefixes: new[] { hi, lo });
        var greaterLast = MakeType(prefixes: new[] { lo, hi });

        Assert.Equal("HI/1", VoucherNumberFormatter.Render(greaterFirst, 1, May5_25));
        Assert.Equal("HI/1", VoucherNumberFormatter.Render(greaterLast, 1, May5_25));
    }

    // Numbering None -> "" (matches today's empty output), even with affixes/width configured.
    // Mutation: drop the "Numbering == None" guard -> renders "PP/007" != "" -> RED.
    [Fact]
    public void Render_noneMethod_isEmpty()
    {
        var type = MakeType(
            numbering: NumberingMethod.None,
            width: 3,
            prefill: true,
            prefixes: new[] { new VoucherNumberAffix(Guid.NewGuid(), Apr1_25, "PP/") });

        Assert.Equal("", VoucherNumberFormatter.Render(type, 7, May5_25));
    }

    // number <= 0 -> "" (matches today's empty output for an unnumbered voucher).
    // Mutation: drop the "number <= 0" guard -> renders "PP/0" != "" -> RED.
    [Fact]
    public void Render_numberZero_isEmpty()
    {
        var type = MakeType(prefixes: new[] { new VoucherNumberAffix(Guid.NewGuid(), Apr1_25, "PP/") });

        Assert.Equal("", VoucherNumberFormatter.Render(type, 0, May5_25));
    }

    // Suffix twin of Render_padsAndAffixesByDate: a SUFFIX row {1-Apr-25 "/A"}, Width 3, Prefill on,
    // Render(_,7,05-May-25) == "007/A" — the suffix trails the padded core (the prefix side is empty). The
    // existing suite exercises the prefix affix but never a suffix, so a lost suffix would ship undetected.
    // Mutation: drop the suffix in the formatter (return "prefix + core") -> "007" != "007/A" -> RED.
    [Fact]
    public void Render_appliesSuffixByDate()
    {
        var type = MakeType(
            width: 3,
            prefill: true,
            suffixes: new[] { new VoucherNumberAffix(Guid.NewGuid(), Apr1_25, "/A") });

        Assert.Equal("007/A", VoucherNumberFormatter.Render(type, 7, May5_25));
    }

    // Suffix twin of Render_picksLatestAffixOnOrBeforeDate: two suffix rows (1-Apr-25 "/A", 1-Apr-26 "/B").
    // A 10-Mar-26 voucher renders ".../A" (the FY26-27 suffix is not yet in force); a voucher dated exactly
    // 1-Apr-26 renders ".../B" (on/before is inclusive at the boundary). The boundary case is the one that
    // bites the selector mutation, since "<=" and "<" differ only on a date equal to an ApplicableFrom.
    // Mutation: selector "<= date" -> "< date" in SelectAffix -> the boundary voucher drops to "1/A" != "1/B" -> RED.
    [Fact]
    public void Render_picksLatestSuffixOnOrBeforeDate()
    {
        var type = MakeType(suffixes: new[]
        {
            new VoucherNumberAffix(Guid.NewGuid(), Apr1_25, "/A"),
            new VoucherNumberAffix(Guid.NewGuid(), Apr1_26, "/B"),
        });

        Assert.Equal("1/A", VoucherNumberFormatter.Render(type, 1, Mar10_26)); // future suffix not yet applicable
        Assert.Equal("1/B", VoucherNumberFormatter.Render(type, 1, Apr1_26));  // boundary: on/before is inclusive
    }

    // Prefix AND suffix together (both {1-Apr-25}): Width 3, Prefill on, Render(_,7,05-May-25) == "25-26/007/X" —
    // i.e. prefix ++ padded-core ++ suffix, in that exact order (numbering-design-v2 §2.1). No single-affix test can
    // pin the assembly order; this one guards against a future concatenation-order regression.
    // Mutation: swap the concatenation order to "suffix + core + prefix" -> "/X00725-26/" != "25-26/007/X" -> RED.
    [Fact]
    public void Render_prefixAndSuffixTogether()
    {
        var type = MakeType(
            width: 3,
            prefill: true,
            prefixes: new[] { new VoucherNumberAffix(Guid.NewGuid(), Apr1_25, "25-26/") },
            suffixes: new[] { new VoucherNumberAffix(Guid.NewGuid(), Apr1_25, "/X") });

        Assert.Equal("25-26/007/X", VoucherNumberFormatter.Render(type, 7, May5_25));
    }

    // Prefill OFF with Width 3: the pad character is a SPACE, so Render(_,7,anyDate) == "  7" (two leading spaces),
    // never "007". Every existing width test pads with '0' (Prefill on), so the space branch was untested.
    // Mutation: change the pad char ' ' -> '0' in the formatter -> "007" != "  7" -> RED.
    [Fact]
    public void Render_spacePadsWhenPrefillOff()
    {
        var type = MakeType(width: 3, prefill: false);

        Assert.Equal("  7", VoucherNumberFormatter.Render(type, 7, AnyDate));
    }
}
