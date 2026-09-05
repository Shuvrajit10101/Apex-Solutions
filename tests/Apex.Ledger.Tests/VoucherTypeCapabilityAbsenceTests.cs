using System;
using System.Linq;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// <b>W2-03 — the capability locks for the Voucher Type master (census 2.4, 5.10, 5.11).</b>
///
/// <para><b>Why these are written by reflection.</b> Every assertion here names a member that did NOT exist when
/// the slice started, so a direct reference would have failed to COMPILE and the red would have proved only that
/// the compiler works. Reflected, the whole file compiles against the pre-slice tree and each test fails on the
/// thing it is actually about — a missing enum member, a missing flag, a missing service. That red is the
/// evidence the census rows were graded on, and these tests stay in the tree so the capability cannot silently
/// leave again.</para>
///
/// <para><b>R7 — sources.</b> The five methods and the two flags are ATTESTED at help.tallysolutions.com
/// (fetched 2026-09-05): the voucher-types page gives <i>"Automatic, Automatic (Manual Override), Manual, or
/// Multi-user Auto"</i>, <i>"Provide narration for each ledger in voucher"</i> and <i>"Enable Print voucher after
/// saving to automatically open the Voucher Printing screen"</i>; the voucher-numbering-methods page adds
/// <i>"you can also disable the voucher numbering by selecting the None option"</i>. The service SHAPE
/// (create/alter/delete/activate) is OURS.</para>
/// </summary>
public class VoucherTypeCapabilityAbsenceTests
{
    private static readonly DateOnly FyStart = new(2024, 4, 1);

    /// <summary>ATTESTED: five methods. The tree shipped three, so two of the five had no domain member at all —
    /// they did not merely lack a picker.</summary>
    [Fact]
    public void All_five_attested_numbering_methods_have_a_domain_member()
    {
        var names = Enum.GetNames<NumberingMethod>();
        Assert.Equal(
            new[] { "Automatic", "AutomaticManualOverride", "Manual", "MultiUserAuto", "None" },
            names.OrderBy(n => n, StringComparer.Ordinal).ToArray());
    }

    /// <summary>The three original ordinals are PERSISTED in <c>voucher_types.numbering</c>, so the two new
    /// methods must be APPENDED. A renumber would silently re-interpret every stored row.</summary>
    [Fact]
    public void The_three_original_numbering_ordinals_are_unchanged_because_they_are_persisted()
    {
        Assert.Equal(0, (int)NumberingMethod.Automatic);
        Assert.Equal(1, (int)NumberingMethod.Manual);
        Assert.Equal(2, (int)NumberingMethod.None);
    }

    /// <summary>ATTESTED flags, and ER-13: both default OFF, so every book that has never opened the master is
    /// byte-identical.</summary>
    [Theory]
    [InlineData("PrintAfterSaving")]
    [InlineData("ProvideNarrationForEachLedger")]
    public void The_attested_voucher_type_user_flags_exist_and_default_to_off(string property)
    {
        var p = typeof(VoucherType).GetProperty(property);
        Assert.NotNull(p);
        Assert.Equal(typeof(bool), p!.PropertyType);
        Assert.NotNull(p.SetMethod);

        var c = CompanyFactory.CreateSeeded("Flag Co", FyStart, FyStart);
        Assert.NotEmpty(c.VoucherTypes);
        Assert.All(c.VoucherTypes, t => Assert.False((bool)p.GetValue(t)!));
    }

    /// <summary>census 2.4 — the four verbs the master screen drives. Named here so the ENGINE half of the row
    /// cannot regress behind a still-present screen.</summary>
    [Theory]
    [InlineData("Create")]
    [InlineData("Alter")]
    [InlineData("Delete")]
    [InlineData("SetActive")]
    public void The_voucher_type_service_carries_all_four_master_verbs(string verb)
    {
        var t = Type.GetType("Apex.Ledger.Services.VoucherTypeService, Apex.Ledger");
        Assert.NotNull(t);
        Assert.Contains(t!.GetMethods(), m => m.Name == verb);
    }

    /// <summary>An auto-numbering method must be asked as a QUESTION, not by comparing to one enum member — the
    /// two post paths compared against <c>Automatic</c> alone, so selecting either new method would have left
    /// every voucher unnumbered.</summary>
    [Fact]
    public void VoucherType_answers_whether_its_method_numbers_automatically()
    {
        var p = typeof(VoucherType).GetProperty("AssignsNumberAutomatically");
        Assert.NotNull(p);
        Assert.Equal(typeof(bool), p!.PropertyType);
    }
}
