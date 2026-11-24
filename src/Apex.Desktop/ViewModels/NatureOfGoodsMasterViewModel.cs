using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Apex.Desktop.ViewModels;

/// <summary>A Nature-of-Goods (§206C TCS category) row for the master's existing-list grid.</summary>
/// <remarks>Census 6.36 clause 1 (wave 13, track ic): it carries the master's id and a keyboard highlight, so the
/// arrows can walk it, Ctrl+Enter can open it for alteration and Alt+D can delete it through the shared
/// <see cref="IMasterListScreen"/> arm — the shape <see cref="NatureOfPaymentListRow"/> took in PR #214.</remarks>
public sealed partial class NatureOfGoodsListRow : ObservableObject, IMasterListRow
{
    /// <inheritdoc/>
    public Guid MasterId { get; init; }

    /// <inheritdoc/>
    public string MasterName => $"{CollectionCode} - {Name}";

    /// <inheritdoc/>
    [ObservableProperty] private bool _isHighlighted;

    public string CollectionCode { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string RateWithPan { get; init; } = string.Empty;
    public string RateWithoutPan { get; init; } = string.Empty;
    public string Threshold { get; init; } = string.Empty;

    /// <summary>"Predefined" / "Custom", plus " · legacy" for the year-gated §206C(1H) sale-of-goods entry.</summary>
    public string Kind { get; init; } = string.Empty;
}

/// <summary>
/// The <b>Nature of Goods</b> master ("Masters → Create → Statutory Masters → Nature of Goods (§206C)"; Phase 7
/// slice 1; mirrors <see cref="NatureOfPaymentMasterViewModel"/>). Lists the seeded predefined §206C set (scrap /
/// timber / tendu / liquor / minerals / 206C(1F) / 206C(1H)-legacy — FY 2025-26) and lets the user create a new
/// <b>custom</b> nature (a 27EQ collection code, a name, the with-PAN and no-PAN §206CC rates in %, and an optional
/// value threshold). A created nature is added to <see cref="TcsConfig.NaturesOfGoods"/> and the company persisted.
///
/// <para>Only reachable when TCS is enabled (the Create-menu item is gated on <see cref="Company.TcsEnabled"/>).
/// The legacy year-gated §206C(1H) nature is listed but flagged (non-operative for dates ≥ 01-Apr-2025). The
/// ~~predefined masters are immutable (add-only domain), so this slice lists them and creates customs — it does not
/// edit a seeded nature.~~ 🔴 <b>STRUCK, census 6.36 clause 1 (wave 13, track ic).</b> That sentence was the TCS half
/// of T1-21: a wrong seeded TCS rate had no remedy in-app. The screen now also <b>ALTERS</b> any nature, predefined
/// included (Ctrl+Enter on the highlighted row, then Ctrl+A — <see cref="ForAlter"/> / <see cref="Alter"/>), and
/// <b>DELETES</b> a custom one (Alt+D, then Y — <see cref="DeleteMaster"/>). The referential rules live in
/// <c>NatureOfGoodsRules</c> (NEW file); the vendor grounds and what is ours are written out there.
/// MVVM boundary: domain + persistence only, no Avalonia types.</para>
/// </summary>
public sealed partial class NatureOfGoodsMasterViewModel : ViewModelBase, IMasterListExportSource, IMasterListScreen
{
    private readonly Company _company;
    private readonly CompanyStorage _storage;
    private readonly Action _onChanged;

    // ------------------------------------------------- census 6.36 clause 1 (wave 13, track ic): ALTER and DELETE

    /// <summary>The id of the nature being ALTERED, or <see cref="Guid.Empty"/> in Create mode.</summary>
    private Guid _editingId = Guid.Empty;

    /// <inheritdoc/>
    public string MasterKindLabel => "nature of goods";

    /// <inheritdoc/>
    public bool IsAltering => _editingId != Guid.Empty;

    /// <summary>True while altering a PREDEFINED nature: its collection code is then read-only (ours — see
    /// <c>NatureOfGoodsRules.EnsureAlterable</c>), so the form does not invite an edit it refuses.</summary>
    public bool IsCodeLocked { get; private set; }

    /// <summary>The screen heading, naming the verb that is running.</summary>
    public string Caption => IsAltering ? "Nature of Goods Alteration" : "Nature of Goods (§206C TCS)";

    /// <summary>The commit button's label, matching <see cref="Caption"/>.</summary>
    public string CommitLabel => IsAltering ? "Save (Ctrl+A)" : "Create (Ctrl+A)";

    /// <summary>Opens this master in <b>Alter</b> mode over an existing nature — the same form, pre-filled. Returns
    /// <c>null</c> if the id does not resolve.</summary>
    public static NatureOfGoodsMasterViewModel? ForAlter(
        Company company, CompanyStorage storage, Guid natureId, Action onChanged)
    {
        ArgumentNullException.ThrowIfNull(company);
        if (company.FindNatureOfGoods(natureId) is not { } nature) return null;

        var vm = new NatureOfGoodsMasterViewModel(company, storage, onChanged);
        vm._editingId = natureId;
        vm.IsCodeLocked = nature.IsPredefined;
        vm.CollectionCode = nature.CollectionCode;
        vm.Name = nature.Name;
        vm.RateWithPanText = FormatRate(nature.RateWithPanBp);
        vm.RateWithoutPanText = FormatRate(nature.RateWithoutPanBp);
        vm.ThresholdText = nature.Threshold is { } t
            ? t.Amount.ToString("0.##", CultureInfo.InvariantCulture)
            : string.Empty;
        vm.OnPropertyChanged(nameof(IsAltering));
        vm.OnPropertyChanged(nameof(IsCodeLocked));
        vm.OnPropertyChanged(nameof(Caption));
        vm.OnPropertyChanged(nameof(CommitLabel));
        return vm;
    }

    /// <summary>Ctrl+A on this screen: <see cref="Alter"/> when altering, else <see cref="Create"/>. The keyboard arm
    /// and the mouse button both call this.</summary>
    public bool Accept() => IsAltering ? Alter() : Create();

    /// <summary>
    /// Ctrl+A <b>alter</b>: re-validates every field exactly as <see cref="Create"/> does (the code-uniqueness check
    /// EXCLUDING this nature's own row), asks <c>NatureOfGoodsRules.EnsureAlterable</c>, then replaces the master in
    /// place and persists. On a failed save the original instance is put back first.
    /// </summary>
    public bool Alter()
    {
        Message = null;

        if (_company.Tcs is not { Enabled: true } tcs)
        {
            Message = "Enable TCS (F11 → Enable TCS) before altering a Nature of Goods.";
            return false;
        }
        if (_company.FindNatureOfGoods(_editingId) is not { } existing)
        {
            Message = "The Nature of Goods being altered no longer exists.";
            return false;
        }
        if (!TryReadForm(_editingId, out var code, out var name, out var withPanBp, out var withoutPanBp,
                out var threshold))
            return false;

        // The Form-27EQ code follows the collection code exactly when it did before (Create sets them equal); an
        // imported nature whose two codes differ keeps its own FVU code unless the collection code moves.
        var fvu = string.Equals(existing.FvuCode, existing.CollectionCode, StringComparison.Ordinal)
                  || !string.Equals(code, existing.CollectionCode, StringComparison.Ordinal)
            ? code
            : existing.FvuCode;

        NatureOfGoods replacement;
        try
        {
            replacement = existing.WithTerms(code, name, withPanBp, withoutPanBp, fvu, threshold);
            Apex.Ledger.Services.NatureOfGoodsRules.EnsureAlterable(_company, existing, replacement);
            tcs.ReplaceNatureOfGoods(replacement);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            Message = ex.Message;
            return false;
        }

        try
        {
            _storage.Save(_company);
        }
        catch (Exception ex)
        {
            tcs.ReplaceNatureOfGoods(existing);
            if (!SaveFailure.IsReportable(ex)) throw;
            Message = ex.Message;
            return false;
        }

        RefreshList();
        Message = $"Nature of Goods '{replacement.CollectionCode}' altered "
                + $"({replacement.RateWithPanBp / 100m:0.##}% with PAN, "
                + $"{replacement.RateWithoutPanBp / 100m:0.##}% without).";
        // There is no dated rate history: a voucher posted under the OLD figures keeps the rate and amount stored on
        // its TCS line. Say how many live vouchers the new figures would have assessed differently (T2-181's measure).
        var moved = Apex.Ledger.Services.NatureOfGoodsRules.VouchersMovedBy(_company, existing, replacement).Count;
        if (moved > 0)
            Message += $" {(moved == 1 ? "1 voucher" : $"{moved} vouchers")} already posted under this nature "
                     + $"{(moved == 1 ? "keeps" : "keep")} the TCS {(moved == 1 ? "it was" : "they were")} collected "
                     + "at; the new rate or threshold applies to sales posted from now on.";
        _onChanged();
        return true;
    }

    /// <inheritdoc/>
    public IMasterListRow? HighlightedMasterRow => HighlightedRow;

    /// <inheritdoc/>
    public void ReloadExisting() => RefreshList();

    /// <inheritdoc/>
    /// <remarks>Engine-only — the shell saves and reloads after this returns. The guard
    /// (<c>NatureOfGoodsRules.EnsureDeletable</c>) runs here, at the moment of the irreversible act, and its message is
    /// what the shell shows when it refuses.</remarks>
    public void DeleteMaster(Guid id)
    {
        if (_company.Tcs is not { } tcs || _company.FindNatureOfGoods(id) is not { } nature)
            throw new InvalidOperationException($"Nature of Goods {id} not found.");
        Apex.Ledger.Services.NatureOfGoodsRules.EnsureDeletable(_company, nature);
        tcs.RemoveNatureOfGoods(id);
    }

    private PayrollMasterHighlight<NatureOfGoodsListRow>? _highlight;

    private PayrollMasterHighlight<NatureOfGoodsListRow> Highlight =>
        _highlight ??= new PayrollMasterHighlight<NatureOfGoodsListRow>(
            Natures, () => OnPropertyChanged(nameof(HighlightedRow)));

    /// <summary>The arrow-highlighted existing nature, or null.</summary>
    public NatureOfGoodsListRow? HighlightedRow => Highlight.Row;

    /// <inheritdoc/>
    public void MoveHighlight(int direction) => Highlight.Move(direction);

    private static string FormatRate(int basisPoints) =>
        (basisPoints / 100m).ToString("0.##", CultureInfo.InvariantCulture);

    /// <inheritdoc/>
    public MasterListSnapshot ToMasterListSnapshot() => new(
        "Nature of Goods",
        new[]
        {
            MasterListColumn.Text("Code"), MasterListColumn.Text("Name"),
            MasterListColumn.Text("Rate (PAN)"), MasterListColumn.Text("Rate (no-PAN)"),
            MasterListColumn.Text("Threshold"), MasterListColumn.Text("Kind"),
        },
        Natures.Select(r => (IReadOnlyList<string>)new[]
        {
            r.CollectionCode, r.Name, r.RateWithPan, r.RateWithoutPan, r.Threshold, r.Kind,
        }).ToList());

    /// <summary>The existing natures (seeded predefined + any created customs), refreshed after each create.</summary>
    public ObservableCollection<NatureOfGoodsListRow> Natures { get; } = new();

    // ---- Create form ----
    [ObservableProperty] private string _collectionCode = string.Empty;
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _rateWithPanText = string.Empty;
    [ObservableProperty] private string _rateWithoutPanText = "5";
    [ObservableProperty] private string _thresholdText = string.Empty;
    [ObservableProperty] private string? _message;

    public NatureOfGoodsMasterViewModel(Company company, CompanyStorage storage, Action onChanged)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _onChanged = onChanged ?? throw new ArgumentNullException(nameof(onChanged));
        RefreshList();
    }

    /// <summary>
    /// Ctrl+A: validates the collection code + name are non-empty, the code is unique, the rates parse to a
    /// percentage ≥ 0, and any threshold parses to money ≥ 0; then adds a custom <see cref="NatureOfGoods"/>
    /// (base-includes-GST per §206C, non-legacy) and persists. Refreshes the list + clears the form.
    /// </summary>
    public bool Create()
    {
        Message = null;

        if (_company.Tcs is not { Enabled: true })
        {
            Message = "Enable TCS (F11 → Enable TCS) before adding a Nature of Goods.";
            return false;
        }

        if (!TryReadForm(Guid.Empty, out var code, out var name, out var withPanBp, out var withoutPanBp,
                out var threshold))
            return false;

        try
        {
            var nature = new NatureOfGoods(
                Guid.NewGuid(), code, name, withPanBp, withoutPanBp, code,
                threshold: threshold, baseIncludesGst: true,
                effectiveFrom: _company.FinancialYearStart, isPredefined: false);
            _company.Tcs.AddNatureOfGoods(nature);
            _storage.Save(_company);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            Message = ex.Message;
            return false;
        }

        RefreshList();
        Message = $"Nature of Goods '{code}' created ({withPanBp / 100m:0.##}% with PAN).";
        CollectionCode = string.Empty;
        Name = string.Empty;
        RateWithPanText = string.Empty;
        RateWithoutPanText = "5";
        ThresholdText = string.Empty;
        _onChanged();
        return true;
    }

    /// <summary>
    /// Reads and validates the form — the ONE validation <see cref="Create"/> and <see cref="Alter"/> share, so an
    /// altered master can never hold a value a created one could not. <paramref name="excludeId"/> is the nature being
    /// altered (its own code is not a duplicate of itself), or <see cref="Guid.Empty"/> on Create.
    /// </summary>
    private bool TryReadForm(
        Guid excludeId, out string code, out string name, out int withPanBp, out int withoutPanBp,
        out Money? threshold)
    {
        code = (CollectionCode ?? string.Empty).Trim();
        name = (Name ?? string.Empty).Trim();
        withPanBp = 0; withoutPanBp = 0; threshold = null;
        var codeValue = code;

        if (string.IsNullOrWhiteSpace(code)) { Message = "A collection code is required (e.g. 6CE for scrap)."; return false; }
        if (string.IsNullOrWhiteSpace(name)) { Message = "A name is required (e.g. Scrap)."; return false; }
        if (_company.NaturesOfGoods.Any(n => n.Id != excludeId
                                             && string.Equals(n.CollectionCode, codeValue, StringComparison.OrdinalIgnoreCase)))
        {
            Message = $"A Nature of Goods '{code}' already exists.";
            return false;
        }
        if (!TryParseRateBp(RateWithPanText, out withPanBp))
        {
            Message = "Rate (with PAN) must be a percentage ≥ 0 (e.g. 1 for 1%).";
            return false;
        }
        if (!TryParseRateBp(RateWithoutPanText, out withoutPanBp))
        {
            Message = "Rate (without PAN, §206CC) must be a percentage ≥ 0 (e.g. 5).";
            return false;
        }
        if (!TryParseThreshold(ThresholdText, out threshold))
        {
            Message = "Threshold must be a rupee amount ≥ 0, or blank.";
            return false;
        }
        return true;
    }

    private static bool TryParseRateBp(string? text, out int basisPoints)
    {
        basisPoints = 0;
        var t = (text ?? string.Empty).Trim();
        if (t.Length == 0) return true;
        if (!decimal.TryParse(t, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var pct) || pct < 0m)
            return false;
        basisPoints = (int)Math.Round(pct * 100m, MidpointRounding.AwayFromZero);
        return true;
    }

    private static bool TryParseThreshold(string? text, out Money? money)
    {
        money = null;
        var t = (text ?? string.Empty).Trim();
        if (t.Length == 0) return true;
        if (!decimal.TryParse(t, NumberStyles.AllowDecimalPoint | NumberStyles.AllowThousands,
                CultureInfo.InvariantCulture, out var rupees) || rupees < 0m)
            return false;
        money = Money.FromRupees(rupees);
        return true;
    }

    private void RefreshList()
    {
        // Census 6.36: the highlight is put back on the SAME master by id after the rebuild — by index, an alter that
        // renames a collection code (and so re-sorts the list) would move it onto a neighbour, which the next Alt+D
        // would then delete.
        var previous = _highlight?.IdBeforeRebuild();
        Natures.Clear();
        foreach (var n in _company.NaturesOfGoods
                     .OrderBy(n => n.CollectionCode, StringComparer.OrdinalIgnoreCase))
        {
            var kind = n.IsPredefined ? "Predefined" : "Custom";
            if (n.IsLegacy) kind += " · legacy";
            Natures.Add(new NatureOfGoodsListRow
            {
                MasterId = n.Id,
                CollectionCode = n.CollectionCode,
                Name = n.Name,
                RateWithPan = $"{n.RateWithPanBp / 100m:0.##}%",
                RateWithoutPan = $"{n.RateWithoutPanBp / 100m:0.##}%",
                // Whole rupees through the ONE grouping rule (drift lock D2) — an interpolated ":#,##0" specifier
                // binds to CurrentCulture, so a lakh-scale §206C threshold grouped the Western way on an en-US host.
                Threshold = n.Threshold is { } t ? $"₹{IndianFormat.RupeesAlways(t)}" : "—",
                Kind = kind,
            });
        }
        _highlight?.RestoreTo(previous);
    }
}
