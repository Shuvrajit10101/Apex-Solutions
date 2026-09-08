using System;
using System.Collections.Generic;
using System.Globalization;
using Apex.Ledger.Domain;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Apex.Desktop.ViewModels;

/// <summary>One option in the <i>Taxability</i> picker. Deliberately NOT a generic
/// <c>MasterGstChoice&lt;T&gt;</c>: Avalonia's compiled bindings need a nameable <c>x:DataType</c> for the
/// ComboBox item template, and a generic there is not expressible.</summary>
public sealed class MasterGstTaxabilityChoice
{
    public GstTaxability Value { get; }
    public string Display { get; }
    public MasterGstTaxabilityChoice(GstTaxability value, string display) { Value = value; Display = display; }
}

/// <summary>One option in the <i>Type of Supply</i> picker (Goods / Services). Concrete for the same reason as
/// <see cref="MasterGstTaxabilityChoice"/>.</summary>
public sealed class MasterGstSupplyTypeChoice
{
    public GstSupplyType Value { get; }
    public string Display { get; }
    public MasterGstSupplyTypeChoice(GstSupplyType value, string display) { Value = value; Display = display; }
}

/// <summary>
/// Census 3.13 — the <b>capture</b> half of the GST hierarchy's two middle rungs: the vendor's <i>Set/Alter GST
/// Details</i> sub-screen as it appears on the <b>Stock Group</b> and the <b>accounting Group</b> masters. One
/// editor, shared by both screens, so the two cannot drift into offering different fields or different validation.
///
/// <para>🔴 <b>WHY THIS CLASS EXISTS AT ALL — READ THE DEFECT IT CLOSES BEFORE CHANGING IT.</b> The STORAGE for
/// both rungs shipped at schema v51 and the RESOLVER that reads them shipped with T0-4 slices S1/S2a/S2b:
/// <c>GstService.WalkFor</c> and <c>MasterAncestry.NearestGroupGst</c> / <c>NearestStockGroupGst</c> walk to the
/// nearest ancestor carrying a block and honour whatever they find. What did not exist was any way for an operator
/// to PUT a block there — the canonical importer was the only writer in the entire product. So the consequence was
/// not that the feature was missing; it was that <b>an imported book resolved rates from rungs a hand-keyed book
/// could never populate</b>. This editor is what makes the two kinds of book behave the same.</para>
///
/// <para><b>Validation is the domain's, not the screen's.</b> <see cref="TryBuild"/> ends by calling
/// <see cref="MasterGstDetails.EnsureValid"/> — the same 4/6/8-digit HSN rule, the same non-negative rate rule and
/// the same "a non-taxable master carries no positive rate" rule the canonical import enforces. A screen-local
/// re-implementation is exactly how this product could previously save a database its own importer rejected.</para>
///
/// <para><b>Culture.</b> The rate is parsed and rendered with <see cref="CultureInfo.InvariantCulture"/>. The gate
/// runs on ubuntu and macos, where the ambient culture can make "18.5" unparseable and render 18.5 as "18,5".</para>
/// </summary>
public sealed partial class MasterGstBlockEditor : ObservableObject
{
    /// <summary>Vendor's <i>"Set/Alter GST Details"</i>: when off, the master carries NO GST block at all
    /// (<c>null</c>), which is what every master that has never used the hierarchy is.</summary>
    [ObservableProperty] private bool _enabled;

    [ObservableProperty] private string _hsnSac = string.Empty;
    [ObservableProperty] private string _ratePercentText = string.Empty;
    [ObservableProperty] private MasterGstTaxabilityChoice? _selectedTaxability;
    [ObservableProperty] private MasterGstSupplyTypeChoice? _selectedSupplyType;

    public IReadOnlyList<MasterGstTaxabilityChoice> TaxabilityChoices { get; } = new[]
    {
        new MasterGstTaxabilityChoice(GstTaxability.Taxable, "Taxable"),
        new MasterGstTaxabilityChoice(GstTaxability.Exempt, "Exempt"),
        new MasterGstTaxabilityChoice(GstTaxability.NilRated, "Nil Rated"),
        new MasterGstTaxabilityChoice(GstTaxability.NonGst, "Non-GST"),
    };

    public IReadOnlyList<MasterGstSupplyTypeChoice> SupplyTypeChoices { get; } = new[]
    {
        new MasterGstSupplyTypeChoice(GstSupplyType.Goods, "Goods"),
        new MasterGstSupplyTypeChoice(GstSupplyType.Services, "Services"),
    };

    public MasterGstBlockEditor()
    {
        SelectedTaxability = TaxabilityChoices[0];
        SelectedSupplyType = SupplyTypeChoices[0];
    }

    /// <summary>Loads an existing block into the form; <c>null</c> switches the block off and clears every field,
    /// so re-opening a master that has no block never shows a previous master's values.</summary>
    public void LoadFrom(MasterGstDetails? block)
    {
        if (block is null)
        {
            Enabled = false;
            HsnSac = string.Empty;
            RatePercentText = string.Empty;
            SelectedTaxability = TaxabilityChoices[0];
            SelectedSupplyType = SupplyTypeChoices[0];
            return;
        }

        Enabled = true;
        HsnSac = block.HsnSac ?? string.Empty;
        RatePercentText = block.RateBasisPoints is { } bp
            ? (bp / 100m).ToString("0.####", CultureInfo.InvariantCulture)
            : string.Empty;
        foreach (var c in TaxabilityChoices) if (c.Value == block.Taxability) SelectedTaxability = c;
        foreach (var c in SupplyTypeChoices) if (c.Value == block.SupplyType) SelectedSupplyType = c;
    }

    /// <summary>
    /// Builds the block this form describes. Returns <c>true</c> with <paramref name="block"/> set to <c>null</c>
    /// when <see cref="Enabled"/> is off (the master carries no GST details — NOT an empty block, which would be a
    /// rung the resolver stops at with nothing to say). Returns <c>false</c> with <paramref name="error"/> set when
    /// a value is malformed, so the caller refuses the whole save rather than writing half a master.
    /// </summary>
    public bool TryBuild(out MasterGstDetails? block, out string? error)
    {
        block = null;
        error = null;

        if (!Enabled) return true;

        var hsn = (HsnSac ?? string.Empty).Trim();
        var rateText = (RatePercentText ?? string.Empty).Trim();

        int? rateBp = null;
        if (rateText.Length > 0)
        {
            if (!decimal.TryParse(rateText, NumberStyles.Number, CultureInfo.InvariantCulture, out var pct)
                || pct < 0m)
            {
                error = "GST rate must be a percentage, for example 18 or 12.5 — or left blank.";
                return false;
            }
            rateBp = (int)Math.Round(pct * 100m, MidpointRounding.AwayFromZero);
        }

        var candidate = new MasterGstDetails
        {
            HsnSac = hsn.Length == 0 ? null : hsn,
            Taxability = SelectedTaxability?.Value ?? GstTaxability.Taxable,
            RateBasisPoints = rateBp,
            SupplyType = SelectedSupplyType?.Value ?? GstSupplyType.Goods,
        };

        try
        {
            candidate.EnsureValid();
        }
        catch (ArgumentException ex)
        {
            error = ex.Message;
            return false;
        }

        block = candidate;
        return true;
    }
}
