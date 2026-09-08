namespace Apex.Ledger.Domain;

/// <summary>
/// The <b>National Pension Scheme</b> pay-head facts (census row 7.18) — a pure, framework-/DB-/clock-/RNG-free
/// classifier for the two NPS <i>Statutory Pay Type</i> tags a <see cref="PayHead"/> can carry
/// (<see cref="IncomeTaxComponent.NationalPensionSchemeTierI"/> and
/// <see cref="IncomeTaxComponent.NationalPensionSchemeTierII"/>), the accounting side each is allowed on, and the
/// captions the vendor shows.
///
/// <para>🔴 <b>THERE IS DELIBERATELY NO ARITHMETIC IN THIS FILE, AND THAT IS THE VENDOR'S SHAPE, NOT A SHORTCUT.</b>
/// Unlike PF, ESI and Professional Tax — each of which needs a dedicated engine because its statutory split cannot
/// be expressed as ordinary pay-head slabs — an NPS pay head in the vendor product is an <b>ordinary</b> head whose
/// amount comes from a <b>user-entered</b> Percentage slab on Basic Pay. The vendor's employee page: <i>"Calculation
/// type … By default, this is set to <b>As Computed Value</b>, but you can also select <b>Flat Rate</b> or <b>As
/// User Defined Value</b>"</i>, computing <i>"On Specified Formula"</i> over <i>"Basic Pay"</i> with <i>"Slab Type:
/// Percentage"</i>. So this book seeds <b>no NPS rate, no ceiling and no slab</b>: there is no statutory figure to
/// get wrong here, and none is invented. The head evaluates on the generic
/// <see cref="PayHeadCalculationType"/> path in <c>PayrollComputationService</c>, unchanged.</para>
///
/// <para><b>SOURCES — vendor documentation (R7 / ruling 14 grounding order, step 1), retrieved 2026-09-08.</b>
/// <list type="bullet">
///   <item><b>Employee NPS deduction pay head</b> —
///     <c>https://help.tallysolutions.com/docs/te9rel61/Payroll/Employees_NPS_Deduction_Pay_Head.htm</c>:
///     <i>"The National Pension Scheme (NPS), a defined contribution based pension system, is structured into two
///     tiers"</i>; Tier-I <i>"does not allow premature withdrawal and is eligible for deduction under section
///     80CCD (1) of the Income Tax Act, 1961 to the extent of 10% of Basic Pay and Dearness Allowance (DA)"</i>;
///     Tier-II <i>"allows withdrawals at the option of the employee. However, contribution to this account does not
///     attract any deduction under the Income Tax Act."</i> Pay Head Type <i>"Employees' Statutory Deductions"</i>,
///     Under <i>"Current Liabilities"</i>.</item>
///   <item><b>Employer NPS contribution pay head</b> —
///     <c>https://help.tallysolutions.com/docs/te9rel61/Payroll/Employer_NPS_Contribution_Pay_Head.htm</c>:
///     <i>"Any contribution by the employer towards NPS will fall under the Tier I account of the scheme. Such
///     employer's contribution towards NPS will be allowed to the employee under Section 80CCD (2) of the Income Tax
///     Act, 1961, to the extent of 10% of Basic Pay and Dearness Allowance (DA)."</i> Pay Head Type <i>"Employer's
///     Statutory Contributions"</i>, Statutory Pay Type <i>"National Pension Scheme (Tier - I)"</i>, Under
///     <i>"Indirect Expenses"</i>.</item>
///   <item><b>Statutory Pay Type option names + the TallyPrime master route</b> —
///     <c>https://help.tallysolutions.com/tally-prime/payroll-masters/professional-tax-deduction-pay-head-tally/</c>
///     and <c>.../payroll-employers-eps-contribution-pay-head/</c>: the two options are <i>"National Pension Scheme
///     (Tier – I)"</i> and <i>"National Pension Scheme (Tier II)"</i>, reached by <i>"Press <b>Alt+G</b> (Go To) &gt;
///     <b>Create Master</b> &gt; <b>Pay Heads</b>"</i>.</item>
/// </list></para>
///
/// <para>⚠️ <b>WHAT IS NOT SHIPPED, STATED RATHER THAN GLOSSED.</b> The <b>10% of Basic + DA</b> the vendor quotes for
/// §80CCD(1) and §80CCD(2) is <b>not encoded anywhere</b> — not as a default, not as a cap. It is a statutory
/// deduction <i>limit</i> in the hands of the employee's income-tax computation, not a payroll rate, and the section
/// text on <c>incometaxindia.gov.in</c> could not be retrieved to confirm the current-year figure or its
/// new-regime §115BAC variant (that host returns HTTP 403 to non-browser requests, and this book has been bitten
/// before by its section slugs silently rolling forward). Deducting a percentage nobody sourced would be exactly the
/// defect class that produced the Karnataka professional-tax over-charge, so the percentage stays where the vendor
/// puts it: <b>typed by the operator on the pay head</b>. §80CCD relief continues to reach the §192 estimate only
/// through the employee's declared <c>TaxDeclaration</c> figures, so the posted pay head and the declaration cannot
/// double-count.</para>
/// </summary>
public static class NationalPensionScheme
{
    /// <summary>The vendor's Statutory-Pay-Type caption for the Tier-I account.</summary>
    public const string TierIName = "National Pension Scheme (Tier - I)";

    /// <summary>The vendor's Statutory-Pay-Type caption for the Tier-II account.</summary>
    public const string TierIIName = "National Pension Scheme (Tier II)";

    /// <summary>The roll-up caption the Payroll Statutory Summary shows for the NPS type.</summary>
    public const string SummaryCaption = "National Pension Scheme";

    /// <summary>Whether <paramref name="component"/> is one of the two NPS statutory pay types.</summary>
    public static bool IsNpsComponent(IncomeTaxComponent component) =>
        component is IncomeTaxComponent.NationalPensionSchemeTierI
                  or IncomeTaxComponent.NationalPensionSchemeTierII;

    /// <summary>The vendor caption for an NPS <paramref name="component"/>, or <c>null</c> when it is not NPS.</summary>
    public static string? CaptionFor(IncomeTaxComponent component) => component switch
    {
        IncomeTaxComponent.NationalPensionSchemeTierI => TierIName,
        IncomeTaxComponent.NationalPensionSchemeTierII => TierIIName,
        _ => null,
    };

    /// <summary>
    /// Whether an NPS <paramref name="component"/> may be carried by a pay head of <paramref name="type"/>.
    /// <list type="bullet">
    ///   <item><b>Tier-I</b> — <b>both</b> sides: <see cref="PayHeadType.EmployeesStatutoryDeductions"/> (the
    ///     employee's own NPS deduction) and <see cref="PayHeadType.EmployersStatutoryContributions"/> (the
    ///     employer's contribution, which the vendor books under Indirect Expenses). This is why NPS cannot use
    ///     <c>PayrollComputationService.RequiredStatutoryRole</c>, which returns a <i>single</i> required posting
    ///     role: NPS Tier-I is the one statutory tag in this book that is legitimately two-sided.</item>
    ///   <item><b>Tier-II</b> — <b>employee deduction only</b>. The vendor states that <i>"Any contribution by the
    ///     employer towards NPS will fall under the Tier I account of the scheme"</i>, so an employer Tier-II head
    ///     has no counterpart in the scheme and is rejected.</item>
    ///   <item>Any non-NPS <paramref name="component"/> is not this rule's business and returns <c>true</c>.</item>
    /// </list>
    /// Rejecting the wrong side matters for the same reason the PF/ESI/PT guard does: a mis-typed statutory head
    /// posts a phantom, self-balancing pair on the wrong side of the payroll voucher and silently mis-states both
    /// net pay and employer cost.
    /// </summary>
    public static bool IsPayHeadTypeAllowed(IncomeTaxComponent component, PayHeadType type) => component switch
    {
        IncomeTaxComponent.NationalPensionSchemeTierI =>
            type is PayHeadType.EmployeesStatutoryDeductions or PayHeadType.EmployersStatutoryContributions,
        IncomeTaxComponent.NationalPensionSchemeTierII =>
            type is PayHeadType.EmployeesStatutoryDeductions,
        _ => true,
    };

    /// <summary>The pay-head types an NPS <paramref name="component"/> is allowed on, named for an error message
    /// (culture-invariant, stable order). Empty for a non-NPS component.</summary>
    public static IReadOnlyList<PayHeadType> AllowedPayHeadTypes(IncomeTaxComponent component) => component switch
    {
        IncomeTaxComponent.NationalPensionSchemeTierI =>
            new[] { PayHeadType.EmployeesStatutoryDeductions, PayHeadType.EmployersStatutoryContributions },
        IncomeTaxComponent.NationalPensionSchemeTierII =>
            new[] { PayHeadType.EmployeesStatutoryDeductions },
        _ => Array.Empty<PayHeadType>(),
    };

    /// <summary>The rejection message for an NPS head on a disallowed pay-head type — one wording, used by both
    /// <c>PayHeadService</c> (master creation) and the Io import pre-flight, so a hand-edited export is refused in
    /// the same words the master screen refuses it in.</summary>
    public static string WrongSideMessage(string payHeadName, IncomeTaxComponent component, PayHeadType type)
    {
        var allowed = string.Join(" or ", AllowedPayHeadTypes(component));
        return $"Pay head '{payHeadName}' carries the statutory pay type '{CaptionFor(component)}', which is only "
             + $"valid on a pay head of type {allowed}, but its pay-head type is '{type}'.";
    }
}
