using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// 🔴 <b>GSTR-9 AND GSTR-9C NAMED THE WRONG GSTIN ON THEIR OWN FACE.</b> Both took a
/// <c>registrationId</c>, scoped every FIGURE by it correctly — and then stamped the return with
/// <c>company.Gst?.Gstin</c>, the <b>primary's</b> GSTIN, whatever registration the return was built for. A
/// branch's annual return therefore carried the head office's GSTIN above the branch's numbers.
///
/// <para><b>Why this is a filing defect and not a caption nit.</b> The GSTIN is the identity of the return.
/// CGST §44 requires <i>"every registered person"</i> to furnish an annual return, and §25(4) makes each
/// registration a <b>distinct person</b>; the figures and the GSTIN must therefore name the same registered
/// person. A document that pairs one registration's turnover with another's GSTIN is not a mislabelled report,
/// it is a return filed against the wrong taxpayer.</para>
///
/// <para><b>The four sites</b> were <c>Gstr9.cs:223</c> and <c>:241</c>, and <c>Gstr9c.cs:131</c> and
/// <c>:143</c> — the applicable and the not-applicable constructor of each. They are fixed by resolving the
/// scope through <c>GstConfig.FindRegistration</c>, which already existed and is what every other
/// registration-aware path uses; nothing new was invented.</para>
///
/// <para>ER-13: on a single-registration book <c>FindRegistration(null)</c> returns the primary, whose
/// <c>Gstin</c> IS <c>company.Gst.Gstin</c>, so that book is byte-identical.</para>
/// </summary>
public class Gstr9RegistrationIdentityTests
{
    private static readonly DateOnly FyStart = new(2024, 4, 1);
    private static readonly DateOnly FyEnd = new(2025, 3, 31);

    private const string Karnataka = "29";
    private const string TamilNadu = "33";

    private static string GstinFor(string stateCode, string pan)
    {
        var body = stateCode + pan + "1Z";
        return body + Gstin.ComputeCheckDigit(body + "0");
    }

    private static readonly string GstinPrimary = GstinFor(Karnataka, "AAPFU0939F");
    private static readonly string GstinBranch = GstinFor(TamilNadu, "AABCC1206D");

    private static (Company Company, GstRegistration Branch) Build()
    {
        var c = CompanyFactory.CreateSeeded("Annual Identity Co", FyStart);
        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = Karnataka,
            Gstin = GstinPrimary,
            RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart,
            Periodicity = GstReturnPeriodicity.Monthly,
        });

        var branch = new GstRegistration(
            Guid.NewGuid(), "Tamil Nadu Registration", TamilNadu, GstinBranch,
            GstRegistrationType.Regular, FyStart);
        c.Gst!.AddRegistration(branch);
        c.Gst!.EnsureValid();

        return (c, branch);
    }

    /// <summary>🔴 A branch's GSTR-9 carries the BRANCH's GSTIN, not the head office's.</summary>
    [Fact]
    public void Gstr9_names_the_registration_it_was_built_for()
    {
        var (c, branch) = Build();

        Assert.Equal(GstinBranch, Gstr9.Build(c, FyStart, FyEnd, branch.Id).Gstin);
        Assert.Equal(GstinPrimary, Gstr9.Build(c, FyStart, FyEnd, GstRegistration.PrimaryId).Gstin);
    }

    /// <summary>🔴 The same for GSTR-9C, which reconciles that very return to the books.</summary>
    [Fact]
    public void Gstr9c_names_the_registration_it_was_built_for()
    {
        var (c, branch) = Build();

        Assert.Equal(GstinBranch, Gstr9c.Build(c, FyStart, FyEnd, branch.Id).Gstin);
        Assert.Equal(GstinPrimary, Gstr9c.Build(c, FyStart, FyEnd, GstRegistration.PrimaryId).Gstin);
    }

    /// <summary>
    /// The NOT-APPLICABLE arm must name the right registration too. A Composition dealer files 9A rather than
    /// 9, so this is the arm a multi-registration composition book reaches — and an empty return that names the
    /// wrong GSTIN is still a document about the wrong taxpayer.
    /// </summary>
    [Fact]
    public void The_not_applicable_arm_also_names_the_registration_it_was_built_for()
    {
        var c = CompanyFactory.CreateSeeded("Annual NA Co", FyStart);
        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = Karnataka,
            Gstin = GstinPrimary,
            RegistrationType = GstRegistrationType.Composition,
            ApplicableFrom = FyStart,
            Periodicity = GstReturnPeriodicity.Quarterly,
            CompositionSubType = CompositionSubType.Trader,
        });
        var branch = new GstRegistration(
            Guid.NewGuid(), "Tamil Nadu Registration", TamilNadu, GstinBranch,
            GstRegistrationType.Composition, FyStart);
        c.Gst!.AddRegistration(branch);
        c.Gst!.EnsureValid();

        var g9 = Gstr9.Build(c, FyStart, FyEnd, branch.Id);
        Assert.False(g9.Applicable);               // a composition dealer files 9A
        Assert.Equal(GstinBranch, g9.Gstin);       // …but it is still THAT branch's non-return

        var g9c = Gstr9c.Build(c, FyStart, FyEnd, branch.Id);
        Assert.False(g9c.Applicable);
        Assert.Equal(GstinBranch, g9c.Gstin);
    }

    /// <summary>ER-13: a single-registration book reads exactly what it always read.</summary>
    [Fact]
    public void A_single_registration_book_still_reads_the_company_gstin()
    {
        var c = CompanyFactory.CreateSeeded("Annual Solo Co", FyStart);
        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = Karnataka,
            Gstin = GstinPrimary,
            RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart,
            Periodicity = GstReturnPeriodicity.Monthly,
        });

        Assert.Equal(GstinPrimary, Gstr9.Build(c, FyStart, FyEnd).Gstin);
        Assert.Equal(GstinPrimary, Gstr9c.Build(c, FyStart, FyEnd).Gstin);
    }
}
