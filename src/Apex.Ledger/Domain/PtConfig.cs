namespace Apex.Ledger.Domain;

/// <summary>
/// The <b>company-level Professional-Tax configuration</b> (Phase 8 slice 6; catalog §14; F11 Payroll Statutory) —
/// the establishment's PT registration facts plus the editable per-state <see cref="SlabTables"/>. Present
/// (non-<c>null</c> on <see cref="Company.PtConfig"/>) once the establishment is enrolled for PT; a company that
/// never enrols carries no config and serialises byte-identically to a pre-v35 company (ER-13). Pure data —
/// framework-, DB-, clock- and RNG-free. The dedicated <c>ProfessionalTax</c> engine resolves the applicable slab
/// table from <see cref="StateCode"/> + the employee's gender and reads the band amount for the month.
/// </summary>
public sealed class PtConfig
{
    private readonly List<PtSlab> _slabTables = new();

    /// <summary>The <b>active PT state</b> (2-digit GST state code, e.g. "27" Maharashtra); <c>null</c> = "None"
    /// (no PT levied — Delhi/Haryana/UP/… and most UTs). Only the slab table(s) for this state drive the deduction;
    /// the other seeded tables are retained so the state can be switched without re-seeding.</summary>
    public string? StateCode { get; set; }

    /// <summary>The PT <b>enrolment / registration number</b> (the PTEC/PTRC number printed on the challan);
    /// optional (may be captured later).</summary>
    public string? RegistrationNumber { get; set; }

    /// <summary>The wage basis the slab is selected against; default <see cref="PtWageBasis.GrossEarnings"/> (DP-3).</summary>
    public PtWageBasis WageBasis { get; set; } = PtWageBasis.GrossEarnings;

    /// <summary>The per-state slab tables (seeded Maharashtra men/women, Karnataka, West Bengal), editable per
    /// company. Order-preserved.</summary>
    public IReadOnlyList<PtSlab> SlabTables => _slabTables;

    public PtConfig() { }

    public PtConfig(string? stateCode, string? registrationNumber = null, PtWageBasis wageBasis = PtWageBasis.GrossEarnings)
    {
        StateCode = string.IsNullOrWhiteSpace(stateCode) ? null : stateCode.Trim();
        RegistrationNumber = string.IsNullOrWhiteSpace(registrationNumber) ? null : registrationNumber.Trim();
        WageBasis = wageBasis;
    }

    /// <summary>Adds a slab table (editable per company; also used by the seed + the store/import rehydration).</summary>
    public void AddSlabTable(PtSlab slab) => _slabTables.Add(slab ?? throw new ArgumentNullException(nameof(slab)));

    /// <summary>Removes a slab table (used by an edit / the transactional import roll-back).</summary>
    public bool RemoveSlabTable(PtSlab slab) => _slabTables.Remove(slab);

    /// <summary>
    /// The slab table that applies to an employee of <paramref name="gender"/> under the active
    /// <see cref="StateCode"/>, or <c>null</c> when PT is not levied (no active state, or no table for it — ⇒ PT ₹0):
    /// among the active state's tables, a gender-agnostic (<see cref="PtGenderScope.Any"/>) table wins if present
    /// (Karnataka/West Bengal), else the table matching the employee's gender (Maharashtra men/women); an unknown
    /// gender in a gender-scoped state falls back to the first (male) table so a member is never silently exempted.
    /// </summary>
    public PtSlab? ResolveSlab(string? gender)
    {
        if (StateCode is null) return null;
        List<PtSlab>? forState = null;
        foreach (var s in _slabTables)
            if (string.Equals(s.StateCode, StateCode, StringComparison.Ordinal))
                (forState ??= new List<PtSlab>()).Add(s);
        if (forState is null || forState.Count == 0) return null;

        foreach (var s in forState)
            if (s.GenderScope == PtGenderScope.Any) return s;

        var scope = GenderScopeOf(gender);
        foreach (var s in forState)
            if (s.GenderScope == scope) return s;

        // Unknown/unset gender in a gender-scoped state → the base (lowest-scope, i.e. Male) table.
        PtSlab best = forState[0];
        foreach (var s in forState)
            if ((int)s.GenderScope < (int)best.GenderScope) best = s;
        return best;
    }

    /// <summary>Maps an <see cref="Employee.Gender"/> string to a <see cref="PtGenderScope"/>: "female"/"f" ⇒
    /// Female, "male"/"m" ⇒ Male, anything else (unset/unknown) ⇒ <see cref="PtGenderScope.Any"/>. Culture-invariant.</summary>
    public static PtGenderScope GenderScopeOf(string? gender)
    {
        if (string.IsNullOrWhiteSpace(gender)) return PtGenderScope.Any;
        var g = gender.Trim();
        if (g.Equals("female", StringComparison.OrdinalIgnoreCase) || g.Equals("f", StringComparison.OrdinalIgnoreCase))
            return PtGenderScope.Female;
        if (g.Equals("male", StringComparison.OrdinalIgnoreCase) || g.Equals("m", StringComparison.OrdinalIgnoreCase))
            return PtGenderScope.Male;
        return PtGenderScope.Any;
    }
}
