using Apex.Ledger.Domain;

namespace Apex.Ledger.Services;

/// <summary>
/// The <b>Provident-Fund contribution engine</b> (Phase 8 slice 4; RQ-9; EPFO ContributionRate) — a <b>pure,
/// deterministic</b>, framework-/DB-/clock-/RNG-free calculator for the statutory EPF/EPS/EDLI split and the
/// establishment administration charge. It exists as dedicated logic (rather than the generic
/// As-Computed-Value slabs) because the statutory figures cannot be expressed as ordinary slabs:
/// <list type="bullet">
///   <item><b>EPS is capped</b> at the ₹15,000 wage ceiling and at ₹1,250 — a percentage slab cannot express a
///     hard rupee cap;</item>
///   <item><b>employer EPF is the residual</b> employee-share − EPS (subtracting two <i>already-rounded</i>
///     figures), never a re-computed 3.67% — the anti-3.67% invariant <c>EPS + EmployerEpf == EmployeeEpf</c>;</item>
///   <item><b>the admin charge has a floor</b> (₹500 / ₹75) applied <b>once per establishment challan</b> over the
///     aggregate EPF wages, not per member.</item>
/// </list>
/// Every per-member figure is rounded <b>half-up to whole rupees</b> (EPFO rounds each member's each account to a
/// rupee); the establishment aggregate is rounded once. All figures are exact <see cref="Money"/>.
/// </summary>
public static class PfContribution
{
    /// <summary>The statutory monthly wage ceiling (₹15,000, current — not hiked).</summary>
    public const decimal WageCeiling = 15000m;

    /// <summary>The employee/employer EPF rate (12%) in basis points (100 bp = 1%).</summary>
    public const int DefaultEpfRateBasisPoints = 1200;

    /// <summary>The reduced EPF rate (10%) in basis points for a special establishment.</summary>
    public const int ReducedEpfRateBasisPoints = 1000;

    /// <summary>The EPS (pension, A/c 10) rate — 8.33% in basis points.</summary>
    public const int PensionRateBasisPoints = 833;

    /// <summary>The EPS monthly cap (₹1,250 = 8.33% × ₹15,000).</summary>
    public const decimal PensionCap = 1250m;

    /// <summary>The EDLI (A/c 21) rate — 0.5% in basis points.</summary>
    public const int EdliRateBasisPoints = 50;

    /// <summary>The EDLI monthly cap (₹75 = 0.5% × ₹15,000).</summary>
    public const decimal EdliCap = 75m;

    /// <summary>The EPF administration (A/c 2) rate — 0.5% in basis points.</summary>
    public const int AdminRateBasisPoints = 50;

    /// <summary>The EPF-admin monthly minimum when the establishment has ≥1 contributory member (₹500).</summary>
    public const decimal AdminMinimum = 500m;

    /// <summary>The EPF-admin monthly minimum when the establishment has no contributory member (₹75).</summary>
    public const decimal AdminMinimumNoMembers = 75m;

    /// <summary>The EDLI-admin (A/c 22) charge — NIL (0%) since it was waived.</summary>
    public static readonly Money EdliAdminCharge = Money.Zero;

    /// <summary>
    /// Computes one member's PF contribution from their <paramref name="pfWages"/> (Basic + DA, HRA excluded), in
    /// the statutory order:
    /// <list type="number">
    ///   <item><c>EE_EPF = round(rate% × EPF_wages)</c> — EPF wages = the full <paramref name="pfWages"/> when the
    ///     member opts to contribute on higher wages, else capped at ₹15,000;</item>
    ///   <item><c>EPS = round(8.33% × min(pfWages, 15000))</c>, capped at ₹1,250;</item>
    ///   <item><c>EmployerEpf = EE_EPF − EPS</c> — the residual of the two already-rounded figures (no re-round);</item>
    ///   <item><c>EDLI = round(0.5% × min(pfWages, 15000))</c>, capped at ₹75.</item>
    /// </list>
    /// <paramref name="epfRateBasisPoints"/> is 1200 (12%) by default or 1000 (10%) for a special establishment.
    /// </summary>
    public static PfMemberContribution ComputeMember(
        decimal pfWages, bool contributeOnHigherWages, int epfRateBasisPoints = DefaultEpfRateBasisPoints)
    {
        if (pfWages < 0m)
            throw new ArgumentOutOfRangeException(nameof(pfWages), "PF wages cannot be negative.");
        if (epfRateBasisPoints is not (DefaultEpfRateBasisPoints or ReducedEpfRateBasisPoints))
            throw new ArgumentException(
                $"EPF rate must be {DefaultEpfRateBasisPoints} (12%) or {ReducedEpfRateBasisPoints} (10%) basis points.",
                nameof(epfRateBasisPoints));

        var cappedWages = Math.Min(pfWages, WageCeiling);
        var epfWages = contributeOnHigherWages ? pfWages : cappedWages;   // EE basis (uncapped only on opt-in)
        var epsWages = cappedWages;                                        // EPS/EDLI always on the capped wages
        var edliWages = cappedWages;

        var employeeEpf = RoundRupee(epfRateBasisPoints / 10000m * epfWages);
        var pension = Math.Min(RoundRupee(PensionRateBasisPoints / 10000m * epsWages), PensionCap);
        var employerEpf = employeeEpf - pension;                           // residual; NOT 3.67%; NOT re-rounded
        var edli = Math.Min(RoundRupee(EdliRateBasisPoints / 10000m * edliWages), EdliCap);

        return new PfMemberContribution(
            EpfWages: new Money(epfWages),
            EpsWages: new Money(epsWages),
            EdliWages: new Money(edliWages),
            EmployeeEpf: new Money(employeeEpf),
            EmployerPension: new Money(pension),
            EmployerEpf: new Money(employerEpf),
            Edli: new Money(edli));
    }

    /// <summary>
    /// Computes the establishment <b>EPF administration charge</b> (A/c 2), applied <b>once per challan</b> over all
    /// contributory members' EPF wages: <c>max(round(0.5% × Σ EPF_wages), 500)</c>, or the ₹75 minimum when the
    /// establishment has <b>no</b> contributory member that period. The floor is deliberately applied to the
    /// aggregate — a per-member floor would over-charge a multi-member establishment.
    /// </summary>
    public static Money ComputeAdminCharge(IReadOnlyCollection<Money> contributoryEpfWages)
    {
        ArgumentNullException.ThrowIfNull(contributoryEpfWages);
        if (contributoryEpfWages.Count == 0)
            return new Money(AdminMinimumNoMembers);

        decimal sum = 0m;
        foreach (var w in contributoryEpfWages) sum += w.Amount;
        var raw = RoundRupee(AdminRateBasisPoints / 10000m * sum);
        return new Money(Math.Max(raw, AdminMinimum));
    }

    /// <summary>
    /// Whether a member's EPF wages are computed <b>uncapped</b> (i.e. contributes on wages above the ₹15,000
    /// ceiling): true when the member opts in per-employee (<paramref name="employeeOptIn"/>), OR when the
    /// establishment's default is to <b>not</b> cap (<paramref name="capWagesAtCeiling"/> false). The per-employee
    /// opt-in overrides the company default; a null <see cref="PfConfig"/> defaults to capping. EPS and EDLI stay
    /// capped at ₹15,000 regardless — only the EPF (A/c 1) wage basis is affected.
    /// </summary>
    public static bool ContributesOnHigherWages(bool employeeOptIn, bool capWagesAtCeiling)
        => employeeOptIn || !capWagesAtCeiling;

    /// <summary>Rounds a rupee amount half-up (away from zero) to a whole rupee — the EPFO per-account rounding.</summary>
    public static decimal RoundRupee(decimal value) => Math.Round(value, 0, MidpointRounding.AwayFromZero);
}

/// <summary>
/// One member's computed PF breakdown (Phase 8 slice 4). Every amount is a whole-rupee <see cref="Money"/> except
/// the wage bases, which carry the (possibly-capped) wage figures the ECR reports. The invariant
/// <see cref="EmployerPension"/> + <see cref="EmployerEpf"/> == <see cref="EmployeeEpf"/> holds by construction.
/// </summary>
public readonly record struct PfMemberContribution(
    Money EpfWages,
    Money EpsWages,
    Money EdliWages,
    Money EmployeeEpf,
    Money EmployerPension,
    Money EmployerEpf,
    Money Edli);
