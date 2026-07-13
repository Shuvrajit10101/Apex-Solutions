using System.Collections.Generic;

namespace Apex.Desktop.ViewModels;

/// <summary>
/// A selectable §192 <b>salary deductor category</b> = the Form-24Q salary section code (Phase 8 slice 7; RQ-12):
/// <b>92B</b> a private / non-government employer (the default), <b>92A</b> a government employer and <b>92C</b> a
/// union-government (Central-Government) employer. It selects the section-code stamped on every Annexure-I deductee
/// row and the Form-16 certificate; the deductor identity itself (TAN / responsible person) reuses the Phase-7
/// deductor config. Shared by the F11 §192 config block and the Form 24Q / Form 16 report screens.
/// </summary>
public sealed class SalarySectionCodeOption
{
    /// <summary>The bare section code the engine's <c>Form24Q.Build</c> / <c>Form16.Build</c> takes ("92B" …).</summary>
    public string Code { get; init; } = "92B";

    /// <summary>The picker display ("92B — Private / non-government employer").</summary>
    public string Display { get; init; } = string.Empty;

    public override string ToString() => Display;

    /// <summary>The three salary deductor categories, 92B (private) first as the working default.</summary>
    public static IReadOnlyList<SalarySectionCodeOption> All { get; } = new[]
    {
        new SalarySectionCodeOption { Code = "92B", Display = "92B — Private / non-government employer" },
        new SalarySectionCodeOption { Code = "92A", Display = "92A — Government employer" },
        new SalarySectionCodeOption { Code = "92C", Display = "92C — Union (Central) Government" },
    };
}
