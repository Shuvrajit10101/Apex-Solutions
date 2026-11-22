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

/// <summary>A Nature-of-Payment (TDS section) row for the master's existing-list grid.</summary>
/// <remarks>Census 6.28 (wave 12, track hf): it carries the master's id and a keyboard highlight, so the arrows
/// can walk it, Ctrl+Enter can open it for alteration and Alt+D can delete it through the shared
/// <see cref="IMasterListScreen"/> arm.</remarks>
public sealed partial class NatureOfPaymentListRow : ObservableObject, IMasterListRow
{
    /// <inheritdoc/>
    public Guid MasterId { get; init; }

    /// <inheritdoc/>
    public string MasterName => $"{SectionCode} - {Name}";

    /// <inheritdoc/>
    [ObservableProperty] private bool _isHighlighted;

    public string SectionCode { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string RateWithPan { get; init; } = string.Empty;
    public string RateWithoutPan { get; init; } = string.Empty;
    public string Threshold { get; init; } = string.Empty;
    public string FvuCode { get; init; } = string.Empty;

    /// <summary>"Predefined" for a seeded nature, else "Custom".</summary>
    public string Kind { get; init; } = string.Empty;
}

/// <summary>
/// The <b>Nature of Payment</b> master ("Masters → Create → Statutory Masters → Nature of Payment"; Phase 7
/// slice 1; mirrors <see cref="CurrencyMasterViewModel"/>/<see cref="LedgerMasterViewModel"/>). Lists the seeded
/// predefined TDS section set (194A/194C/194H/194I(a·b)/194J(a·b)/194Q — FY 2025-26) and lets the user create a
/// new <b>custom</b> nature (a section code, a name, the with-PAN and no-PAN §206AA rates in %, the Form-26Q/FVU
/// section code, and optional single-transaction / cumulative-FY thresholds). A created nature is added to the
/// company's <see cref="TdsConfig.NaturesOfPayment"/> and the whole company is persisted.
///
/// <para>Only reachable when TDS is enabled (the Create-menu item is gated on <see cref="Company.TdsEnabled"/>).
/// ~~the predefined masters are immutable (add-only domain), so this slice lists them and creates customs — it does
/// not edit a seeded nature.~~ 🔴 <b>STRUCK, census 6.28 (wave 12, track hf).</b> That sentence was T1-21: a user
/// facing a wrong seeded rate had no remedy in-app. The screen now also <b>ALTERS</b> any nature, predefined
/// included (Ctrl+Enter on the highlighted row, then Ctrl+A — <see cref="ForAlter"/> / <see cref="Alter"/>), and
/// <b>DELETES</b> a custom one (Alt+D, then Y — <see cref="DeleteMaster"/>). The referential rules live in
/// <c>NatureOfPaymentRules</c>; the vendor grounds and what is ours are written out there.
/// MVVM boundary: domain + persistence only, no Avalonia types (headlessly testable).</para>
/// </summary>
public sealed partial class NatureOfPaymentMasterViewModel : ViewModelBase, IMasterListExportSource, IMasterListScreen
{
    private readonly Company _company;
    private readonly CompanyStorage _storage;
    private readonly Action _onChanged;

    // ------------------------------------------------- census 6.28 (wave 12, track hf): ALTER and DELETE

    /// <summary>The id of the nature being ALTERED, or <see cref="Guid.Empty"/> in Create mode.</summary>
    private Guid _editingId = Guid.Empty;

    /// <inheritdoc/>
    public string MasterKindLabel => "nature of payment";

    /// <inheritdoc/>
    public bool IsAltering => _editingId != Guid.Empty;

    /// <summary>True while altering a PREDEFINED section: its section code and Form-26Q code are then read-only
    /// (ours — see <c>NatureOfPaymentRules.EnsureAlterable</c>), so the form does not invite an edit it refuses.</summary>
    public bool IsCodeLocked { get; private set; }

    /// <summary>The screen heading, naming the verb that is running (the form is the same in both modes).</summary>
    public string Caption => IsAltering ? "Nature of Payment Alteration" : "Nature of Payment (TDS)";

    /// <summary>The commit button's label, matching <see cref="Caption"/>.</summary>
    public string CommitLabel => IsAltering ? "Save (Ctrl+A)" : "Create (Ctrl+A)";

    /// <summary>
    /// Opens this master in <b>Alter</b> mode over an existing nature — the same form, pre-filled. Returns
    /// <c>null</c> if the id does not resolve (or TDS is off).
    /// </summary>
    public static NatureOfPaymentMasterViewModel? ForAlter(
        Company company, CompanyStorage storage, Guid natureId, Action onChanged)
    {
        ArgumentNullException.ThrowIfNull(company);
        if (company.FindNatureOfPayment(natureId) is not { } nature) return null;

        var vm = new NatureOfPaymentMasterViewModel(company, storage, onChanged);
        vm._editingId = natureId;
        vm.IsCodeLocked = nature.IsPredefined;
        vm.SectionCode = nature.SectionCode;
        vm.Name = nature.Name;
        vm.RateWithPanText = FormatRate(nature.RateWithPanBp);
        vm.RateWithoutPanText = FormatRate(nature.RateWithoutPanBp);
        vm.FvuSectionCode = nature.FvuSectionCode;
        vm.SingleThresholdText = FormatThreshold(nature.SingleTransactionThreshold);
        // A per-month section's stored cumulative figure is INERT (see DescribeThreshold, and the same refusal in
        // Create): an existing book can still carry the superseded annualised ₹6,00,000 there. Pre-filling it would
        // make every alteration of that row refuse itself until the operator blanked a figure the engine never
        // applies, so it is loaded blank — and saving then drops the inert value, which changes no computation.
        vm.CumulativeThresholdText = nature.ThresholdWindowIsPerMonth
            ? string.Empty
            : FormatThreshold(nature.CumulativeThreshold);
        vm.OnPropertyChanged(nameof(IsAltering));
        vm.OnPropertyChanged(nameof(IsCodeLocked));
        vm.OnPropertyChanged(nameof(Caption));
        vm.OnPropertyChanged(nameof(CommitLabel));
        return vm;
    }

    /// <summary>Ctrl+A on this screen: <see cref="Alter"/> when altering, else <see cref="Create"/>. The keyboard
    /// arm and the mouse button both call this, so the two can never disagree about the verb.</summary>
    public bool Accept() => IsAltering ? Alter() : Create();

    /// <summary>
    /// Ctrl+A <b>alter</b>: re-validates every field exactly as <see cref="Create"/> does (the section-code
    /// uniqueness check EXCLUDING this nature's own row), asks <c>NatureOfPaymentRules.EnsureAlterable</c>, then
    /// replaces the master in place and persists. On a failed save the original instance is put back first, so
    /// the shared aggregate never carries figures the store refused.
    /// </summary>
    public bool Alter()
    {
        Message = null;

        if (_company.Tds is not { Enabled: true } tds)
        {
            Message = "Enable TDS (F11 → Enable TDS) before altering a Nature of Payment.";
            return false;
        }
        if (_company.FindNatureOfPayment(_editingId) is not { } existing)
        {
            Message = "The Nature of Payment being altered no longer exists.";
            return false;
        }
        if (!TryReadForm(_editingId, out var section, out var name, out var fvu, out var withPanBp,
                out var withoutPanBp, out var single, out var cumulative))
            return false;

        NatureOfPayment replacement;
        try
        {
            replacement = existing.WithTerms(section, name, withPanBp, withoutPanBp, fvu, single, cumulative);
            Apex.Ledger.Services.NatureOfPaymentRules.EnsureAlterable(_company, existing, replacement);
            tds.ReplaceNatureOfPayment(replacement);
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
            tds.ReplaceNatureOfPayment(existing);
            if (!SaveFailure.IsReportable(ex)) throw;
            Message = ex.Message;
            return false;
        }

        RefreshList();
        Message = $"Nature of Payment '{replacement.SectionCode}' altered "
                + $"({replacement.RateWithPanBp / 100m:0.##}% with PAN, "
                + $"{replacement.RateWithoutPanBp / 100m:0.##}% without).";
        _onChanged();
        return true;
    }

    /// <inheritdoc/>
    public IMasterListRow? HighlightedMasterRow => HighlightedRow;

    /// <inheritdoc/>
    public void ReloadExisting() => RefreshList();

    /// <inheritdoc/>
    /// <remarks>Engine-only — the shell saves and reloads after this returns. The guard
    /// (<c>NatureOfPaymentRules.EnsureDeletable</c>) runs here, at the moment of the irreversible act, and its
    /// message is what the shell shows when it refuses.</remarks>
    public void DeleteMaster(Guid id)
    {
        if (_company.Tds is not { } tds || _company.FindNatureOfPayment(id) is not { } nature)
            throw new InvalidOperationException($"Nature of Payment {id} not found.");
        Apex.Ledger.Services.NatureOfPaymentRules.EnsureDeletable(_company, nature);
        tds.RemoveNatureOfPayment(id);
    }

    private PayrollMasterHighlight<NatureOfPaymentListRow>? _highlight;

    private PayrollMasterHighlight<NatureOfPaymentListRow> Highlight =>
        _highlight ??= new PayrollMasterHighlight<NatureOfPaymentListRow>(
            Natures, () => OnPropertyChanged(nameof(HighlightedRow)));

    /// <summary>The arrow-highlighted existing nature, or null.</summary>
    public NatureOfPaymentListRow? HighlightedRow => Highlight.Row;

    /// <inheritdoc/>
    public void MoveHighlight(int direction) => Highlight.Move(direction);

    private static string FormatRate(int basisPoints) =>
        (basisPoints / 100m).ToString("0.##", CultureInfo.InvariantCulture);

    private static string FormatThreshold(Money? money) =>
        money is { } m ? m.Amount.ToString("0.##", CultureInfo.InvariantCulture) : string.Empty;

    /// <inheritdoc/>
    public MasterListSnapshot ToMasterListSnapshot() => new(
        "Nature of Payment",
        new[]
        {
            MasterListColumn.Text("Section"), MasterListColumn.Text("Name"),
            MasterListColumn.Text("Rate (PAN)"), MasterListColumn.Text("Rate (no-PAN)"),
            MasterListColumn.Text("Threshold"), MasterListColumn.Text("FVU"), MasterListColumn.Text("Kind"),
        },
        Natures.Select(r => (IReadOnlyList<string>)new[]
        {
            r.SectionCode, r.Name, r.RateWithPan, r.RateWithoutPan, r.Threshold, r.FvuCode, r.Kind,
        }).ToList());

    /// <summary>The existing natures (seeded predefined + any created customs), refreshed after each create.</summary>
    public ObservableCollection<NatureOfPaymentListRow> Natures { get; } = new();

    // ---- Create form ----
    [ObservableProperty] private string _sectionCode = string.Empty;
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _rateWithPanText = string.Empty;
    [ObservableProperty] private string _rateWithoutPanText = "20";
    [ObservableProperty] private string _fvuSectionCode = string.Empty;
    [ObservableProperty] private string _singleThresholdText = string.Empty;
    [ObservableProperty] private string _cumulativeThresholdText = string.Empty;
    [ObservableProperty] private string? _message;

    public NatureOfPaymentMasterViewModel(Company company, CompanyStorage storage, Action onChanged)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _onChanged = onChanged ?? throw new ArgumentNullException(nameof(onChanged));
        RefreshList();
    }

    /// <summary>
    /// Ctrl+A: validates the section code + name + FVU code are non-empty, the section code is unique, the
    /// rates parse to a percentage ≥ 0, and any thresholds parse to money ≥ 0; then adds a custom
    /// <see cref="NatureOfPayment"/> to the company's TDS config and persists. Refreshes the list + clears the form.
    /// </summary>
    public bool Create()
    {
        Message = null;

        if (_company.Tds is not { Enabled: true })
        {
            Message = "Enable TDS (F11 → Enable TDS) before adding a Nature of Payment.";
            return false;
        }

        if (!TryReadForm(Guid.Empty, out var section, out var name, out var fvu, out var withPanBp,
                out var withoutPanBp, out var single, out var cumulative))
            return false;

        try
        {
            var nature = new NatureOfPayment(
                Guid.NewGuid(), section, name, withPanBp, withoutPanBp, fvu,
                singleTransactionThreshold: single, cumulativeThreshold: cumulative,
                effectiveFrom: _company.FinancialYearStart, isPredefined: false);
            _company.Tds.AddNatureOfPayment(nature);
            _storage.Save(_company);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            Message = ex.Message;
            return false;
        }

        RefreshList();
        Message = $"Nature of Payment '{section}' created ({withPanBp / 100m:0.##}% with PAN).";
        SectionCode = string.Empty;
        Name = string.Empty;
        RateWithPanText = string.Empty;
        RateWithoutPanText = "20";
        FvuSectionCode = string.Empty;
        SingleThresholdText = string.Empty;
        CumulativeThresholdText = string.Empty;
        _onChanged();
        return true;
    }

    /// <summary>
    /// Reads and validates the form — the ONE validation <see cref="Create"/> and <see cref="Alter"/> share, so an
    /// altered master can never hold a value a created one could not. <paramref name="excludeId"/> is the nature
    /// being altered (its own section code is not a duplicate of itself), or <see cref="Guid.Empty"/> on Create.
    /// Sets <see cref="Message"/> and returns false on the first invalid field.
    /// </summary>
    private bool TryReadForm(
        Guid excludeId, out string section, out string name, out string fvu, out int withPanBp,
        out int withoutPanBp, out Money? single, out Money? cumulative)
    {
        section = (SectionCode ?? string.Empty).Trim();
        name = (Name ?? string.Empty).Trim();
        fvu = (FvuSectionCode ?? string.Empty).Trim();
        withPanBp = 0; withoutPanBp = 0; single = null; cumulative = null;
        var sectionCode = section;

        if (string.IsNullOrWhiteSpace(section)) { Message = "A section code is required (e.g. 194J(b))."; return false; }
        if (string.IsNullOrWhiteSpace(name)) { Message = "A name is required (e.g. Fees for professional services)."; return false; }
        if (string.IsNullOrWhiteSpace(fvu)) { Message = "A Form-26Q / FVU section code is required (e.g. 94J-B)."; return false; }
        if (_company.NaturesOfPayment.Any(n => n.Id != excludeId
                                               && string.Equals(n.SectionCode, sectionCode, StringComparison.OrdinalIgnoreCase)))
        {
            Message = $"A Nature of Payment '{section}' already exists.";
            return false;
        }
        if (!TryParseRateBp(RateWithPanText, out withPanBp))
        {
            Message = "Rate (with PAN) must be a percentage ≥ 0 (e.g. 10 for 10%).";
            return false;
        }
        if (!TryParseRateBp(RateWithoutPanText, out withoutPanBp))
        {
            Message = "Rate (without PAN, §206AA) must be a percentage ≥ 0 (e.g. 20).";
            return false;
        }
        if (!TryParseThreshold(SingleThresholdText, out single))
        {
            Message = "Single-transaction threshold must be a rupee amount ≥ 0, or blank.";
            return false;
        }
        if (!TryParseThreshold(CumulativeThresholdText, out cumulative))
        {
            Message = "Cumulative-FY threshold must be a rupee amount ≥ 0, or blank.";
            return false;
        }
        // 🔴 A PER-MONTH SECTION HAS NO FINANCIAL-YEAR AGGREGATE TO STORE, so a figure typed into this field would
        // be persisted and never applied — and, since the list column now states the window instead, it would not
        // even show up to contradict itself. Silently ignoring what the operator typed is the one outcome not
        // available here, so it is refused by name. (This is also what the seeded §194-I rows do: they carry no
        // stored threshold at all, and an existing book's superseded ₹6,00,000 in this field is inert.)
        if (cumulative is not null
            && new NatureOfPayment(Guid.Empty, section, name, withPanBp, withoutPanBp, fvu).ThresholdWindowIsPerMonth)
        {
            Message = $"§{section} tests its threshold PER MONTH — \"rent credited or paid for a month or part of "
                    + "a month … does not exceed fifty thousand rupees\" — and carries no financial-year aggregate "
                    + "limb at all. A Cumulative-FY threshold on this section would be stored and never applied, "
                    + "so leave it blank.";
            return false;
        }
        return true;
    }

    /// <summary>
    /// 🔴 <b>Ctrl+U — "Add missing predefined sections". THE ONLY ROUTE BY WHICH AN EXISTING BOOK CAN EVER SEE A
    /// NEWLY SEEDED TDS SECTION, and it exists because the obvious alternative is wrong.</b>
    ///
    /// <para><b>The problem.</b> <c>TdsTcsService.EnableTds</c> seeds the predefined set only when the config has
    /// <b>none</b> — "Preserve any natures already seeded on a prior enable (or supplied by import)". So a company
    /// that enabled TDS before §194T/§194R/§194S were added to <c>SeedTdsTcsRates</c> holds the old eight and will
    /// hold them forever: re-running F11 → Enable TDS changes nothing, because the count is not zero. Without this
    /// action the three new sections would be reachable only in a <b>brand-new company</b> — which is precisely
    /// the "a capability no user can reach is not complete" trap this project has filed three times.</para>
    ///
    /// <para>🔴 <b>WHY THE FIX IS NOT A TOP-UP INSIDE <c>EnableTds</c>, which is where it first looks like it
    /// belongs.</b> <c>ImportPlan</c> builds a <see cref="TdsConfig"/> from the imported natures and <b>then</b>
    /// calls <c>EnableTds</c>. A top-up there would inject the predefined set into every imported company —
    /// including one that legitimately carries a curated or trimmed list — so an export → import → export cycle
    /// would come back with rows the original never had, and the canonical round-trip would no longer be a
    /// round trip. Making it an explicit, user-initiated action on this screen keeps import byte-faithful.</para>
    ///
    /// <para><b>Semantics.</b> Idempotent and additive-only, matched on <see cref="NatureOfPayment.SectionCode"/>
    /// case-insensitively: a section already present — predefined, or hand-created by the operator under the same
    /// code — is left <b>exactly</b> as it is and is never overwritten, so a user's edited (since census 6.28,
    /// ALTERED) figures survive. ~~The domain is add-only (there is no remove), so~~ 🔴 Since census 6.28 a nature
    /// CAN be removed, but only a <b>custom</b> one — a predefined section is refused by
    /// <c>NatureOfPaymentRules.EnsureDeletable</c> — so a missing PREDEFINED code can still only ever mean "seeded
    /// after this book was created", never "deliberately deleted". Persists only when something was actually
    /// added.</para>
    /// </summary>
    public int AddMissingPredefined()
    {
        Message = null;

        if (_company.Tds is not { Enabled: true } tds)
        {
            Message = "Enable TDS (F11 → Enable TDS) before adding the predefined Nature-of-Payment sections.";
            return 0;
        }

        var present = new HashSet<string>(
            _company.NaturesOfPayment.Select(n => n.SectionCode), StringComparer.OrdinalIgnoreCase);

        var added = new List<string>();
        foreach (var seeded in Apex.Ledger.Seed.SeedTdsTcsRates.BuildTdsDefaults())
        {
            if (!present.Add(seeded.SectionCode)) continue;
            tds.AddNatureOfPayment(seeded);
            added.Add(seeded.SectionCode);
        }

        if (added.Count == 0)
        {
            Message = "All predefined Nature-of-Payment sections are already present — nothing to add.";
            return 0;
        }

        _storage.Save(_company);
        RefreshList();
        // Ordinal sort so the message reads the same on every host culture (the gate runs on ubuntu and macos,
        // and a culture-sensitive compare orders section codes differently under e.g. tr-TR).
        added.Sort(StringComparer.Ordinal);
        Message = $"Added {added.Count} predefined section(s): {string.Join(", ", added)}.";
        _onChanged();
        return added.Count;
    }

    /// <summary>Parses a percentage (e.g. "10" or "0.1") to basis points (1000 / 10); false if not a number ≥ 0.</summary>
    private static bool TryParseRateBp(string? text, out int basisPoints)
    {
        basisPoints = 0;
        var t = (text ?? string.Empty).Trim();
        if (t.Length == 0) return true; // blank ⇒ 0%
        if (!decimal.TryParse(t, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var pct) || pct < 0m)
            return false;
        basisPoints = (int)Math.Round(pct * 100m, MidpointRounding.AwayFromZero);
        return true;
    }

    /// <summary>Parses an optional rupee threshold to <see cref="Money"/>; blank ⇒ null; false if invalid.</summary>
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
        // Census 6.28: the highlight is put back on the SAME master by id after the rebuild — by index, an alter
        // that renames a section code (and so re-sorts the list) would move it onto a neighbour, which the next
        // Alt+D would then delete. The first call runs from the constructor, before the field initialisers of the
        // highlight helper matter, and finds nothing to restore.
        var previous = _highlight?.IdBeforeRebuild();
        Natures.Clear();
        foreach (var n in _company.NaturesOfPayment
                     .OrderBy(n => n.SectionCode, StringComparer.OrdinalIgnoreCase))
        {
            Natures.Add(new NatureOfPaymentListRow
            {
                MasterId = n.Id,
                SectionCode = n.SectionCode,
                Name = n.Name,
                RateWithPan = $"{n.RateWithPanBp / 100m:0.##}%",
                RateWithoutPan = $"{n.RateWithoutPanBp / 100m:0.##}%",
                Threshold = DescribeThreshold(n),
                // The code that will actually be FILED, not the one stored — they differ on a legacy §194-I row
                // whose book persists "4IA" where the notified Form 26Q spells it "4-IA". Showing the stored value
                // here would tell the operator a code the return does not carry. See
                // NatureOfPayment.NotifiedFvuSectionCode.
                FvuCode = n.NotifiedFvuSectionCode,
                Kind = n.IsPredefined ? "Predefined" : "Custom",
            });
        }
        _highlight?.RestoreTo(previous);
    }

    /// <summary>
    /// The threshold column for one nature. 🔴 <b>It states the window as well as the figure, because they are
    /// not the same test.</b> A per-month section (§194-I: "rent credited or paid for a month or part of a month
    /// … does not exceed fifty thousand rupees", and no annual limb at all) prints "₹50,000/month" and does NOT
    /// print its stored <see cref="NatureOfPayment.CumulativeThreshold"/>: on such a nature that field is inert —
    /// an existing book still carries the superseded annualised ₹6,00,000 there, and printing it would tell the
    /// operator a figure the engine does not apply.
    /// </summary>
    private static string DescribeThreshold(NatureOfPayment n)
    {
        var single = n.SingleTransactionThreshold;
        var monthly = n.MonthlyThreshold;
        var cumulative = monthly is null ? n.CumulativeThreshold : null;
        if (single is null && monthly is null && cumulative is null) return "—";
        var parts = new List<string>();
        // Whole rupees through the ONE grouping rule (drift lock D2). An interpolated ":#,##0" specifier binds to
        // CurrentCulture, so on the en-US host this app targets a §194C ₹1,00,000 threshold printed "₹100,000"
        // while the tax invoice for the same company printed "₹1,00,000" — the same-assembly contradiction D2
        // exists to eliminate — and on a de-DE host "₹100.000", which reads as a decimal.
        if (single is { } s) parts.Add($"₹{IndianFormat.RupeesAlways(s)} single");
        if (monthly is { } m) parts.Add($"₹{IndianFormat.RupeesAlways(m)}/month");
        // 🔴 THE BOUNDARY IS PART OF THE FIGURE, and printing the number alone tells the operator the wrong rule
        // on §192A and §194EE. Those two are liable AT the threshold ("is less than X"), every other section only
        // ABOVE it ("does not exceed X") — so a bare "₹50,000/FY" on §192A reads as the exclusive rule the engine
        // deliberately does not apply there. Suffixed rather than reworded so the exclusive sections, which are the
        // overwhelming majority and whose wording is already pinned by tests, print exactly as they always have.
        if (cumulative is { } c)
            parts.Add($"₹{IndianFormat.RupeesAlways(c)}/FY" + (n.AggregateThresholdIsInclusive ? " (incl.)" : ""));
        // §194-O(2)'s exemption is available only to an individual/HUF participant who furnished a PAN; everyone
        // else is liable from the first rupee, so the figure alone would overstate who it protects.
        if (n.AggregateThresholdAppliesOnlyToIndividualHufWithPan && cumulative is not null)
            parts.Add("Ind/HUF with PAN only");
        return string.Join(" · ", parts);
    }
}
