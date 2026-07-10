using System;
using System.Collections.Generic;
using System.Linq;
using Apex.Ledger.Domain;

namespace Apex.Desktop.ViewModels;

/// <summary>
/// A TDS/TCS <b>deductor / collector</b> legal-status picker option (label + the enum value) for the F11
/// deductor-details form (Phase 7 slice 1). Mirrors <see cref="GstRegistrationTypeOption"/>.
/// </summary>
public sealed class DeductorTypeOption
{
    public DeductorType Value { get; init; }
    public string Display { get; init; } = string.Empty;
}

/// <summary>
/// A <b>deductee</b> (party subject to TDS) legal-status picker option on the Ledger master (Phase 7 slice 1).
/// <see cref="Value"/> is <c>null</c> for the "(not set)" entry, so an existing party carries no deductee type.
/// </summary>
public sealed class DeducteeTypeChoice
{
    public DeducteeType? Value { get; init; }
    public string Display { get; init; } = string.Empty;
    public bool IsNone => Value is null;
}

/// <summary>
/// A <b>collectee</b> (buyer from whom TCS is collected) legal-status picker option on the Ledger master
/// (Phase 7 slice 1). <see cref="Value"/> is <c>null</c> for the "(not set)" entry.
/// </summary>
public sealed class CollecteeTypeChoice
{
    public CollecteeType? Value { get; init; }
    public string Display { get; init; } = string.Empty;
    public bool IsNone => Value is null;
}

/// <summary>
/// A <b>Nature of Payment</b> (TDS section) picker option: a defined nature, or the "(none)" entry that leaves
/// the ledger's default nature unset (Phase 7 slice 1). <see cref="NatureId"/> is <c>null</c> for "(none)".
/// </summary>
public sealed class NatureOfPaymentChoice
{
    public Guid? NatureId { get; init; }
    public string Display { get; init; } = string.Empty;
    public bool IsNone => NatureId is null;
}

/// <summary>
/// A <b>Nature of Goods</b> (§206C TCS category) picker option: a defined nature, or the "(none)" entry
/// (Phase 7 slice 1). <see cref="NatureId"/> is <c>null</c> for "(none)".
/// </summary>
public sealed class NatureOfGoodsChoice
{
    public Guid? NatureId { get; init; }
    public string Display { get; init; } = string.Empty;
    public bool IsNone => NatureId is null;
}

/// <summary>
/// Shared, UI-toolkit-free label/option helpers for the TDS/TCS masters (Phase 7 slice 1). Keeps the
/// legal-status humanisation and picker-list construction in one place so the F11 config, the Ledger master
/// and the Stock-Item master render the identical option text. Framework-agnostic (headlessly testable).
/// </summary>
public static class TdsTcsDisplay
{
    /// <summary>A human label for a deductor/collector legal status (drives the 26Q/27EQ deductor block).</summary>
    public static string Humanize(DeductorType t) => t switch
    {
        DeductorType.Company => "Company",
        DeductorType.Individual => "Individual",
        DeductorType.HinduUndividedFamily => "HUF (Hindu Undivided Family)",
        DeductorType.Firm => "Firm / LLP",
        DeductorType.AssociationOfPersons => "Association of Persons (AOP)",
        DeductorType.BodyOfIndividuals => "Body of Individuals (BOI)",
        DeductorType.LocalAuthority => "Local Authority",
        DeductorType.Government => "Government",
        DeductorType.ArtificialJuridicalPerson => "Artificial Juridical Person",
        _ => t.ToString(),
    };

    /// <summary>A human label for a deductee legal status (selects the §194C 1%/2% rate branch at compute).</summary>
    public static string Humanize(DeducteeType t) => Humanize((DeductorType)(int)t);

    /// <summary>A human label for a collectee legal status.</summary>
    public static string Humanize(CollecteeType t) => Humanize((DeductorType)(int)t);

    /// <summary>The deductor/collector-type options (all legal persons), for the F11 deductor form.</summary>
    public static IReadOnlyList<DeductorTypeOption> DeductorTypeOptions() =>
        Enum.GetValues<DeductorType>()
            .Select(t => new DeductorTypeOption { Value = t, Display = Humanize(t) })
            .ToList();

    /// <summary>The deductee-type options for a party ledger: "(not set)" first, then every legal person.</summary>
    public static IReadOnlyList<DeducteeTypeChoice> DeducteeTypeChoices()
    {
        var list = new List<DeducteeTypeChoice> { new() { Value = null, Display = "◦ (not set)" } };
        list.AddRange(Enum.GetValues<DeducteeType>()
            .Select(t => new DeducteeTypeChoice { Value = t, Display = Humanize(t) }));
        return list;
    }

    /// <summary>The collectee-type options for a party ledger: "(not set)" first, then every legal person.</summary>
    public static IReadOnlyList<CollecteeTypeChoice> CollecteeTypeChoices()
    {
        var list = new List<CollecteeTypeChoice> { new() { Value = null, Display = "◦ (not set)" } };
        list.AddRange(Enum.GetValues<CollecteeType>()
            .Select(t => new CollecteeTypeChoice { Value = t, Display = Humanize(t) }));
        return list;
    }

    /// <summary>The Nature-of-Payment picker options for the company: "(none)" first, then every defined nature.</summary>
    public static IReadOnlyList<NatureOfPaymentChoice> NatureOfPaymentChoices(Company company)
    {
        var list = new List<NatureOfPaymentChoice> { new() { NatureId = null, Display = "◦ (none)" } };
        list.AddRange(company.NaturesOfPayment
            .OrderBy(n => n.SectionCode, StringComparer.OrdinalIgnoreCase)
            .Select(n => new NatureOfPaymentChoice
            {
                NatureId = n.Id,
                Display = $"{n.SectionCode} — {n.Name}",
            }));
        return list;
    }

    /// <summary>The Nature-of-Goods picker options for the company: "(none)" first, then every defined nature.</summary>
    public static IReadOnlyList<NatureOfGoodsChoice> NatureOfGoodsChoices(Company company)
    {
        var list = new List<NatureOfGoodsChoice> { new() { NatureId = null, Display = "◦ (none)" } };
        list.AddRange(company.NaturesOfGoods
            .OrderBy(n => n.CollectionCode, StringComparer.OrdinalIgnoreCase)
            .Select(n => new NatureOfGoodsChoice
            {
                NatureId = n.Id,
                Display = $"{n.CollectionCode} — {n.Name}",
            }));
        return list;
    }
}
