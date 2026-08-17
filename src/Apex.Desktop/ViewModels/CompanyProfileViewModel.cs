using System;
using System.Collections.ObjectModel;
using System.Linq;
using Apex.Desktop.Services;
using Apex.Ledger.Domain;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Apex.Desktop.ViewModels;

/// <summary>
/// One entry in the company's postal State picker. Normally backed by an <see cref="IndianState"/>;
/// <see cref="State"/> is <c>null</c> only for the transient "value stored on this book that is not in the
/// list" entry described on <see cref="CompanyProfileViewModel"/>.
/// </summary>
public sealed class PostalStateOption
{
    /// <summary>The recognised State/UT, or <c>null</c> for a stored value the list does not contain.</summary>
    public IndianState? State { get; init; }

    /// <summary>The text stored on the company for this option — what an untouched accept writes back.</summary>
    public string StoredValue { get; init; } = string.Empty;

    /// <summary>
    /// What the picker shows. An unrecognised stored value is shown verbatim, never "corrected" — but it is
    /// also <b>marked</b>.
    ///
    /// <para><b>Why the marker is not cosmetic.</b> The stored value this entry exists for is very often a
    /// canonical-imported string that differs from a real State/UT only in whitespace or case — "West Bengal "
    /// with a trailing space is the fixture. Rendered bare, the picker then offers two rows that look
    /// character-for-character identical, and choosing the wrong one silently rewrites the one field whose
    /// entire justification is surviving byte-identically. <see cref="StoredValue"/> — what an accept actually
    /// writes — is untouched by the marker.</para>
    /// </summary>
    public string Display => State is null ? StoredValue + "   (stored on this book)" : State.Name;

    /// <summary>True for the transient entry carrying a stored value the list does not recognise.</summary>
    public bool IsUnrecognised => State is null;
}

/// <summary>
/// The company profile form, shared by <b>Company Creation</b> and <b>Company Alteration</b>.
///
/// <para><b>What it captures, and why that matters.</b> The eleven profile fields that already exist on the
/// domain, in the schema and in the printer — Mailing Name, Address, State, Country, PIN, the two book dates
/// and the four base-currency fields. Until this screen existed, creation captured exactly one field (the
/// name) and <see cref="Company.Address"/>, <see cref="Company.State"/> and <see cref="Company.Pin"/> had NO
/// assignment site anywhere in the desktop layer, so the supplier address CGST Rule 46(a) requires on every
/// tax invoice could not be typed at all. Rule 46(a)'s supplier <i>name</i> maps to <b>Mailing Name</b>, which
/// is why that field is editable in both modes.</para>
///
/// <para><b>One view model, two modes.</b> Creation and alteration differ in exactly two ways, so duplicating
/// eleven bound fields across two view models is how the two screens would start disagreeing with each other.
/// The differences are stated on <see cref="IsAltering"/>.</para>
///
/// <para><b>The State picker tolerates a value it does not recognise — deliberately, and it is load-bearing.</b>
/// Canonical import assigns <c>Company.State</c> verbatim with no list check, so a book on disk can legitimately
/// hold a trailing-space value, an abbreviation, or a name from a source that predates the list. When the
/// loaded value is non-blank and unrecognised, the picker gains one transient entry carrying that text verbatim,
/// preselected; accepting without touching the control writes the identical string back. <b>Opening alteration
/// and pressing accept must never blank or "correct" a stored State</b> — that would be silent data loss on a
/// field the canonical round-trip asserts.</para>
///
/// <para>MVVM boundary: domain + persistence only, no Avalonia types, so it is headlessly unit-testable.</para>
/// </summary>
public sealed partial class CompanyProfileViewModel : ViewModelBase
{
    private readonly CompanyStorage _storage;
    private readonly Action _onChanged;

    /// <summary>
    /// The company being altered, or <c>null</c> in creation mode (there is no aggregate until Accept runs).
    /// </summary>
    private readonly Company? _company;

    /// <summary>
    /// True on <b>Company Alteration</b>, false on <b>Company Creation</b>. The two modes differ in exactly two
    /// ways, both recorded where they are enforced:
    /// <list type="number">
    /// <item><see cref="Name"/> is editable on creation and <b>read-only</b> on alteration — see
    /// <see cref="IsNameEditable"/> for the reason, which is a storage fact and a serious one.</item>
    /// <item>Alteration mutates and re-saves an existing aggregate; creation builds a seeded one.</item>
    /// </list>
    /// </summary>
    public bool IsAltering => _company is not null;

    /// <summary>The screen caption, matching the two verbs the reference product uses.</summary>
    public string Caption => IsAltering ? "Company Alteration" : "Company Creation";

    /// <summary>
    /// <b>Renaming is not offered here, and the reason is a book-eater.</b> The company's <c>.db</c> file path
    /// is derived from its NAME (<c>CompanyStorage.PathForName</c>), and the company-select list takes each
    /// display name back from the FILENAME. So saving a renamed company would write a brand-new <c>.db</c> at
    /// the new name and leave the old file untouched — two entries in Company Select carrying the same company
    /// id, with every later save landing on only one of them, and nothing anywhere reporting an error. Rename
    /// is a storage operation (move the file, refuse a collision, handle the open handle) and belongs in its own
    /// slice. <b>This costs nothing statutory:</b> the supplier name Rule 46(a) requires is the Mailing Name,
    /// which is editable in both modes.
    /// </summary>
    public bool IsNameEditable => !IsAltering;

    // ---- the bound fields (corpus screen order: Name, then Primary Mailing Details, then the book dates,
    //      then Base Currency Information) --------------------------------------------------------------

    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _mailingName = string.Empty;
    [ObservableProperty] private string _address = string.Empty;
    [ObservableProperty] private PostalStateOption? _selectedState;
    [ObservableProperty] private string _country = "India";
    [ObservableProperty] private string _pin = string.Empty;
    [ObservableProperty] private string _financialYearStartText = string.Empty;
    [ObservableProperty] private string _booksBeginFromText = string.Empty;
    [ObservableProperty] private string _baseCurrencySymbol = "₹";
    [ObservableProperty] private string _baseCurrencyName = "INR";
    [ObservableProperty] private string _decimalPlacesText = "2";
    [ObservableProperty] private string _decimalUnitName = "Paisa";
    [ObservableProperty] private string? _message;

    /// <summary>
    /// True when <see cref="Message"/> is a refusal, false when it is a confirmation. <b>The screen used to
    /// render both in the alert colour</b>, so "Company details saved." printed in red — a successful save
    /// reported as a failure. The view binds two text blocks to this rather than the view model choosing a
    /// brush, which would put an Avalonia concern in a headlessly-testable class.
    /// </summary>
    [ObservableProperty] private bool _messageIsError;

    /// <summary>The message line when it is a REFUSAL, else <c>null</c>. The view binds one text block to this
    /// and one to <see cref="ConfirmationMessage"/>; splitting them here rather than converting a brush in the
    /// view keeps the severity decision testable and keeps Avalonia out of this class.</summary>
    public string? ErrorMessage => MessageIsError ? Message : null;

    /// <summary>The message line when it is a CONFIRMATION, else <c>null</c>.</summary>
    public string? ConfirmationMessage => MessageIsError ? null : Message;

    partial void OnMessageChanged(string? value) => RaiseMessageParts();
    partial void OnMessageIsErrorChanged(bool value) => RaiseMessageParts();

    private void RaiseMessageParts()
    {
        OnPropertyChanged(nameof(ErrorMessage));
        OnPropertyChanged(nameof(ConfirmationMessage));
    }

    /// <summary>
    /// The postal State options: every recognised State/UT, plus — when the stored value is not one of them —
    /// one transient entry carrying that value verbatim. An ordinary picker: there is no type-to-filter
    /// anywhere in this application and none is invented here.
    /// </summary>
    public ObservableCollection<PostalStateOption> StateOptions { get; } = new();

    /// <summary>
    /// The postal-State / GST-registration-State advisory, or empty when there is nothing to say. Recomputed
    /// as the operator moves the picker, so it appears the moment a divergence is created and disappears the
    /// moment it is resolved. Shown on alteration only: a company being created has no GST registration yet.
    /// </summary>
    public string StateAdvisory =>
        IsAltering
            ? CompanyStateConsistency.Advisory(
                SelectedStoredStateText(),
                CompanyStateConsistency.RegisteredStateCodeOf(_company))
            : string.Empty;

    /// <summary>
    /// Shown while altering a book that already carries vouchers: the two book dates key every period report,
    /// so moving them moves what every report covers. <b>An advisory, not a lock</b> — no corpus source says any
    /// company field becomes read-only after creation, so locking them would be inventing a restriction, while
    /// letting them move silently on a posted book is a wrong-figures hazard. The middle course is stated here
    /// rather than chosen quietly.
    /// </summary>
    public string BookDatesAdvisory =>
        IsAltering && _company!.Vouchers.Count > 0
            ? "This book already has vouchers. Changing these dates changes which period every report covers."
            : string.Empty;

    /// <summary>Creation mode: an empty form for a company that does not exist yet.</summary>
    public CompanyProfileViewModel(CompanyStorage storage, Action onChanged)
    {
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _onChanged = onChanged ?? throw new ArgumentNullException(nameof(onChanged));
        _company = null;

        BuildStateOptions(storedState: null);
        // The two dates are left blank on creation: blank means "use the seeded default", which is what the
        // 153 existing fixture bootstraps rely on and what keeps a never-typed creation byte-identical.
    }

    /// <summary>Alteration mode: the form pre-filled from an existing company.</summary>
    public CompanyProfileViewModel(Company company, CompanyStorage storage, Action onChanged)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _onChanged = onChanged ?? throw new ArgumentNullException(nameof(onChanged));

        Name = company.Name;
        MailingName = company.MailingName ?? string.Empty;
        Address = company.Address ?? string.Empty;
        Country = company.Country;
        Pin = company.Pin ?? string.Empty;
        FinancialYearStartText = ApexDate.Format(company.FinancialYearStart);
        BooksBeginFromText = ApexDate.Format(company.BooksBeginFrom);
        BaseCurrencySymbol = company.BaseCurrencySymbol;
        BaseCurrencyName = company.BaseCurrencyName;
        DecimalPlacesText = company.DecimalPlaces.ToString(System.Globalization.CultureInfo.InvariantCulture);
        DecimalUnitName = company.DecimalUnitName;

        BuildStateOptions(company.State);
    }

    /// <summary>
    /// Fills the picker with every recognised State/UT and, when <paramref name="storedState"/> is non-blank
    /// but unrecognised, appends the transient verbatim entry and preselects it (see the type's own summary).
    /// </summary>
    private void BuildStateOptions(string? storedState)
    {
        StateOptions.Clear();
        foreach (var s in IndianState.All)
            StateOptions.Add(new PostalStateOption { State = s, StoredValue = s.Name });

        var stored = storedState ?? string.Empty;
        if (stored.Trim().Length == 0) { SelectedState = null; return; }

        var known = IndianState.FromName(stored);
        if (known is not null)
        {
            SelectedState = StateOptions.FirstOrDefault(o => o.State?.Code == known.Code);
            return;
        }

        var verbatim = new PostalStateOption { State = null, StoredValue = stored };
        StateOptions.Add(verbatim);
        SelectedState = verbatim;
    }

    /// <summary>What an accept would write to <see cref="Company.State"/> — <c>null</c> when nothing is picked.</summary>
    private string? SelectedStoredStateText() =>
        SelectedState is null ? null : SelectedState.StoredValue;

    partial void OnSelectedStateChanged(PostalStateOption? value) => OnPropertyChanged(nameof(StateAdvisory));

    /// <summary>
    /// Accept (Ctrl+A, or the Accept confirmation's "Y"). On alteration: pre-validates, assigns the profile
    /// fields onto the shared aggregate, persists, and <b>restores every previous value on any failure</b>.
    /// On creation this is a no-op — creation is driven by the shell, which needs the seeded factory.
    ///
    /// <para><b>The rollback is not optional.</b> Every master screen in this application mutates the shared
    /// <see cref="Company"/> aggregate and only then persists it, so a save that fails for an operational
    /// reason (a second instance holding the write lock, a read-only file) would otherwise leave the in-memory
    /// company holding values the book on disk does not have — the same divergence class the statutory screen's
    /// rollback exists to prevent, on the postal block instead of the GST one.</para>
    /// </summary>
    public bool Accept()
    {
        ClearMessage();
        if (_company is null) return false;

        if (!Validate(out var pin, out var fyStart, out var books, out var decimalPlaces)) return false;

        var previous = Capture(_company);
        Apply(_company, pin, fyStart, books, decimalPlaces);

        try
        {
            _storage.Save(_company);
        }
        catch (Exception ex)
        {
            Restore(_company, previous);
            if (!SaveFailure.IsReportable(ex)) throw;
            Refuse(ex.Message);
            return false;
        }

        // The form must end up showing what the BOOK now holds, not what was typed. Apply coalesces four
        // fields — a blank Mailing Name becomes the company name, a blank Country / currency symbol / name /
        // decimal unit keeps its previous value — so without this the operator is left looking at an empty
        // control while the book carries a value, and the next accept would re-read the stale blank.
        SyncFromCompany(_company);

        OnPropertyChanged(nameof(StateAdvisory));
        OnPropertyChanged(nameof(BookDatesAdvisory));
        Confirm(SavedMessage);
        _onChanged();
        return true;
    }

    /// <summary>The confirmation wording, in one place so a test can name it and the screen cannot drift.</summary>
    public const string SavedMessage = "Company details saved.";

    /// <summary>The screen's own PIN refusal — deliberately worded differently from the engine's
    /// "Company PIN code '…' is not a valid 6-digit Indian PIN code.", so a test can tell which one fired.</summary>
    public const string BadPinMessage =
        "PIN code must be 6 digits (a leading zero is not a valid Indian PIN).";

    /// <summary>The screen's decimal-places refusal.</summary>
    public const string BadDecimalPlacesMessage =
        "Number of decimal places must be a whole number from 0 to 4.";

    /// <summary>Clears the message line and its severity together, so neither can outlive the other.</summary>
    internal void ClearMessage()
    {
        Message = null;
        MessageIsError = false;
    }

    /// <summary>Shows <paramref name="text"/> as a REFUSAL. Internal because the shell's creation path reports
    /// the domain's own refusals onto this same form.</summary>
    internal void Refuse(string text)
    {
        Message = text;
        MessageIsError = true;
    }

    private void Confirm(string text)
    {
        Message = text;
        MessageIsError = false;
    }

    /// <summary>
    /// Re-reads every bound control from the aggregate. Used after a successful accept so the form and the
    /// book agree — see <see cref="Accept"/>. The State picker is rebuilt rather than re-pointed, because the
    /// stored value may now be a recognised one where it was not (or the reverse).
    /// </summary>
    private void SyncFromCompany(Company company)
    {
        MailingName = company.MailingName;
        Address = company.Address ?? string.Empty;
        Country = company.Country;
        Pin = company.Pin ?? string.Empty;
        FinancialYearStartText = ApexDate.Format(company.FinancialYearStart);
        BooksBeginFromText = ApexDate.Format(company.BooksBeginFrom);
        BaseCurrencySymbol = company.BaseCurrencySymbol;
        BaseCurrencyName = company.BaseCurrencyName;
        DecimalPlacesText = company.DecimalPlaces.ToString(System.Globalization.CultureInfo.InvariantCulture);
        DecimalUnitName = company.DecimalUnitName;
        BuildStateOptions(company.State);
    }

    /// <summary>
    /// The typed values, validated and parsed, for the creation path — which cannot reuse <see cref="Accept"/>
    /// because the aggregate does not exist yet. Returns false (with <see cref="Message"/> set) on a bad value.
    /// </summary>
    internal bool TryReadForCreate(out CompanyProfileValues values)
    {
        values = default;
        ClearMessage();
        if (!Validate(out var pin, out var fyStart, out var books, out var decimalPlaces)) return false;

        values = new CompanyProfileValues(
            MailingName: Trimmed(MailingName),
            Address: Trimmed(Address),
            State: SelectedStoredStateText(),
            Country: string.IsNullOrWhiteSpace(Country) ? null : Country.Trim(),
            Pin: pin,
            FinancialYearStart: fyStart,
            BooksBeginFrom: books,
            BaseCurrencySymbol: Trimmed(BaseCurrencySymbol),
            BaseCurrencyName: Trimmed(BaseCurrencyName),
            DecimalPlaces: decimalPlaces,
            DecimalUnitName: Trimmed(DecimalUnitName));
        return true;
    }

    /// <summary>
    /// Every validation this screen performs, in one place so the create and alter paths cannot diverge on
    /// what they refuse.
    ///
    /// <para><b>Two rules, and the second one is why this method exists at all.</b> The PIN rule is the
    /// engine's own (<c>IndianPinCode</c>), pre-checked here so a mistyped PIN is a friendly message rather
    /// than an exception surfaced as a save failure after the aggregate has already been mutated. The
    /// books-begin rule (<c>BooksBeginFrom</c> ≥ <c>FinancialYearStart</c>) is enforced by
    /// <see cref="Company"/>'s CONSTRUCTOR — and, since this slice, also by <see cref="Company.EnsureValid"/>,
    /// so the store refuses it too. This screen is the first UI that assigns those dates, so it refuses the
    /// state with a message rather than producing it and letting the floor throw.</para>
    ///
    /// <para><b>On CREATION the comparison must supply the factory's own default</b>, because there is no
    /// aggregate to read a stored year start from — see the comment on the comparison itself. Getting that
    /// wrong was a hard crash, not a missed message.</para>
    /// </summary>
    private bool Validate(out string? pin, out DateOnly? fyStart, out DateOnly? books, out int? decimalPlaces)
    {
        pin = null;
        fyStart = null;
        books = null;
        decimalPlaces = null;

        var typedPin = (Pin ?? string.Empty).Trim();
        if (!IndianPinCode.IsValidOrBlank(typedPin))
        {
            Refuse(BadPinMessage);
            return false;
        }
        pin = typedPin.Length == 0 ? null : typedPin;

        if (!TryReadDate(FinancialYearStartText, "Financial year begins from", out fyStart)) return false;
        if (!TryReadDate(BooksBeginFromText, "Books beginning from", out books)) return false;

        // 🔴 THE DEFAULT IS PART OF THE COMPARISON, and leaving it out was a crash. On ALTERATION the pair to
        // check against is the stored one. On CREATION there is no aggregate yet — so an untyped year start is
        // NOT "no constraint", it is `CompanyFactory.DefaultFinancialYearStart`, the value the factory is about
        // to substitute. Reading `fyStart ?? _company?.FinancialYearStart` alone left BOTH terms null on
        // creation, the comparison short-circuited away, and a books date earlier than 1-Apr of this year — the
        // exact input the field's own "blank = 1-Apr of this year" placeholder invites — reached
        // `new Company(...)` and threw out of the constructor, unhandled, to the UI dispatcher.
        // The default is read FROM the factory so the guard and the factory cannot drift.
        var effectiveStart = fyStart ?? _company?.FinancialYearStart
                             ?? Apex.Ledger.Services.CompanyFactory.DefaultFinancialYearStart;
        // Same reasoning on the books side: the factory defaults it to the year start, so an untyped books date
        // can never violate the rule.
        var effectiveBooks = books ?? _company?.BooksBeginFrom ?? effectiveStart;
        if (effectiveBooks < effectiveStart)
        {
            // The effective year start is NAMED. On creation with only a books date typed it is a value the
            // operator never saw, so "cannot be earlier than the date the financial year begins from" alone
            // would refuse against an invisible number.
            Refuse("Books beginning from cannot be earlier than the date the financial year begins from "
                 + $"({ApexDate.Format(effectiveStart)}).");
            return false;
        }

        var typedPlaces = (DecimalPlacesText ?? string.Empty).Trim();
        if (typedPlaces.Length > 0)
        {
            if (!int.TryParse(typedPlaces, System.Globalization.NumberStyles.None,
                              System.Globalization.CultureInfo.InvariantCulture, out var places)
                || places < 0 || places > 4)
            {
                Refuse(BadDecimalPlacesMessage);
                return false;
            }
            decimalPlaces = places;
        }

        return true;
    }

    private bool TryReadDate(string? text, string label, out DateOnly? value)
    {
        value = null;
        var t = (text ?? string.Empty).Trim();
        if (t.Length == 0) return true;

        // The app-wide day-first parser and its one rejection message, so a company date reads and refuses
        // exactly like every other date field.
        if (!ApexDate.TryParse(t, out var parsed))
        {
            Refuse($"{label} — {ApexDate.ErrorFor(t)}");
            return false;
        }
        value = parsed;
        return true;
    }

    private static string? Trimmed(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    /// <summary>The profile fields this screen overwrites, captured so a failed save can put them all back.</summary>
    private readonly record struct Snapshot(
        string MailingName, string? Address, string? State, string Country, string? Pin,
        DateOnly FinancialYearStart, DateOnly BooksBeginFrom,
        string BaseCurrencySymbol, string BaseCurrencyName, int DecimalPlaces, string DecimalUnitName);

    private static Snapshot Capture(Company company) => new(
        company.MailingName, company.Address, company.State, company.Country, company.Pin,
        company.FinancialYearStart, company.BooksBeginFrom,
        company.BaseCurrencySymbol, company.BaseCurrencyName, company.DecimalPlaces, company.DecimalUnitName);

    private static void Restore(Company company, Snapshot s)
    {
        company.MailingName = s.MailingName;
        company.Address = s.Address;
        company.State = s.State;
        company.Country = s.Country;
        company.Pin = s.Pin;
        company.FinancialYearStart = s.FinancialYearStart;
        company.BooksBeginFrom = s.BooksBeginFrom;
        company.BaseCurrencySymbol = s.BaseCurrencySymbol;
        company.BaseCurrencyName = s.BaseCurrencyName;
        company.DecimalPlaces = s.DecimalPlaces;
        company.DecimalUnitName = s.DecimalUnitName;
    }

    /// <summary>
    /// Writes the typed values onto the aggregate. <b>Blank never overwrites a non-blank default</b> for
    /// Country, the currency symbol/name and the decimal unit: those three are non-null on every company ever
    /// constructed, so treating an empty control as "clear it" would write <c>""</c> into a NOT NULL column and
    /// change the printed output of every book. The Name is untouched — alteration cannot rename.
    /// </summary>
    private void Apply(Company company, string? pin, DateOnly? fyStart, DateOnly? books, int? decimalPlaces)
    {
        company.MailingName = string.IsNullOrWhiteSpace(MailingName) ? company.Name : MailingName.Trim();
        company.Address = Trimmed(Address);
        company.State = SelectedStoredStateText();
        company.Country = string.IsNullOrWhiteSpace(Country) ? company.Country : Country.Trim();
        company.Pin = pin;
        if (fyStart is { } fy) company.FinancialYearStart = fy;
        if (books is { } bb) company.BooksBeginFrom = bb;
        company.BaseCurrencySymbol = string.IsNullOrWhiteSpace(BaseCurrencySymbol) ? company.BaseCurrencySymbol : BaseCurrencySymbol.Trim();
        company.BaseCurrencyName = string.IsNullOrWhiteSpace(BaseCurrencyName) ? company.BaseCurrencyName : BaseCurrencyName.Trim();
        if (decimalPlaces is { } dp) company.DecimalPlaces = dp;
        company.DecimalUnitName = string.IsNullOrWhiteSpace(DecimalUnitName) ? company.DecimalUnitName : DecimalUnitName.Trim();
    }
}

/// <summary>
/// The validated profile values the creation path assigns onto a freshly seeded company. A plain carrier —
/// the creation path cannot reuse <c>CompanyProfileViewModel.Accept</c> because the aggregate it would mutate
/// does not exist until the seeded factory has run.
/// </summary>
internal readonly record struct CompanyProfileValues(
    string? MailingName,
    string? Address,
    string? State,
    string? Country,
    string? Pin,
    DateOnly? FinancialYearStart,
    DateOnly? BooksBeginFrom,
    string? BaseCurrencySymbol,
    string? BaseCurrencyName,
    int? DecimalPlaces,
    string? DecimalUnitName);
