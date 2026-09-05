using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Io;
using Apex.Ledger.Services;

namespace Apex.Desktop.Tests;

/// <summary>
/// THE COMPANY PROFILE SCREEN — creation and alteration of the eleven profile fields.
///
/// <para><b>What this file is for.</b> Until this screen shipped, company creation captured exactly one field
/// (the name), so the supplier address CGST Rule 46(a) requires on every tax invoice could not be typed
/// anywhere in the product. These tests drive the REAL shell view model over a throwaway storage folder — no
/// UI toolkit — and cover what the screen is answerable for: the postal block round-tripping through the
/// database on BOTH paths, the two validation rules and their refusals, the postal-State / GST-State
/// inheritance and its divergence warning, the rollback, and the promise that a company where none of it is
/// touched is left exactly as it was.
///
/// <para><b>Every test below names the mutation that must redden it</b>, because a test that cannot fail is
/// not coverage. Those mutations were run — and on <b>2026-08-17</b> they were run AGAIN, one at a time,
/// after an adversarial review measured NINE guards that could be deleted simultaneously with the whole
/// 3,828-test suite green. Three named mutations did not redden their own test; the tests were rewritten
/// until they did, and the surface with no pin at all (Apply's eight non-postal writes, the whole
/// decimal-places branch, date parsing, the book-dates advisory, the confirmation message) was given one.
/// Where a claim could not be made true it was deleted rather than softened.</para>
/// </summary>
public sealed class CompanyProfileScreenTests : IDisposable
{
    // 🔴 THE EXPECTED MESSAGES ARE LITERALS HERE, NOT REFERENCES TO THE VIEW MODEL'S OWN CONSTANTS.
    // Asserting `Assert.Equal(CompanyProfileViewModel.SavedMessage, form.Message)` compares the production
    // constant with itself: editing the constant moves BOTH sides and the test stays green. Measured — with
    // the constant form, mutating the confirmation text and mutating the decimal-places refusal were both
    // GREEN across the whole filtered set. A literal is the only thing that pins wording.
    private const string SavedConfirmation = "Company details saved.";
    private const string ScreenPinRefusal =
        "PIN code must be 6 digits (a leading zero is not a valid Indian PIN).";

    private const string GstinMaharashtra = "27AAPFU0939F1ZV";
    private const string GstinKerala = "32AAPFU0939F1ZX";

    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public CompanyProfileScreenTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexCompanyProfileTests_" + Guid.NewGuid().ToString("N"));
        _storage = new CompanyStorage(_tempDir);
    }

    private MainWindowViewModel NewShell() => new(_storage);

    /// <summary>Drives the REAL creation screen: open it, fill the form, accept.</summary>
    private MainWindowViewModel CreateThroughScreen(string name, Action<CompanyProfileViewModel>? fill = null)
    {
        var vm = NewShell();
        vm.ShowCompanySelect();
        // The creation screen is reached from the Company-Select menu row, not by poking the enum.
        ActivateMenuItem(vm, "Create Company");
        Assert.Equal(Screen.CreateCompany, vm.CurrentScreen);

        vm.NewCompanyName = name;
        fill?.Invoke(vm.CreateCompanyProfile);
        vm.ActivateSelected();     // the Ctrl+A path
        return vm;
    }

    private static void ActivateMenuItem(MainWindowViewModel vm, string label)
    {
        var index = -1;
        for (var i = 0; i < vm.Menu.Count; i++)
            if (vm.Menu[i].IsSelectable && vm.Menu[i].Label == label) { index = i; break; }
        Assert.True(index >= 0, $"No menu row labelled '{label}'.");
        vm.Menu[index].Activate();
    }

    /// <summary>Picks a State in the profile picker by its recognised name.</summary>
    private static void PickState(CompanyProfileViewModel form, string stateName) =>
        form.SelectedState = form.StateOptions.Single(o => o.State?.Name == stateName);

    /// <summary>Reloads the company from its own <c>.db</c> — the only honest test of "was it persisted".</summary>
    private Company Reload(string companyName)
    {
        var entry = _storage.ListCompanies().Single(e => e.Name == companyName);
        return _storage.Load(entry);
    }

    private static void EnableGst(Company company, string gstin, string homeStateCode) =>
        new GstService(company).EnableGst(new GstConfig
        {
            Gstin = gstin,
            HomeStateCode = homeStateCode,
            RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = company.FinancialYearStart,
            Periodicity = GstReturnPeriodicity.Monthly,
        });

    /// <summary>
    /// The advisory as PRODUCTION composes it: the stored postal State against the registration code the
    /// shared helper extracts. There used to be an <c>Advisory(Company?)</c> convenience overload with zero
    /// <c>src/</c> call sites, and six of the seven advisory assertions were going through it — exercising a
    /// door no screen uses. It was deleted; this is the composition both screens actually perform.
    /// </summary>
    private static string StoredAdvisory(Company company) =>
        CompanyStateConsistency.Advisory(
            company.State, CompanyStateConsistency.RegisteredStateCodeOf(company));

    // =========================================================================================
    // The inheritance: the GST home State DEFAULTS FROM the postal State — as a DISPLAY default
    // =========================================================================================

    /// <summary>
    /// The rule, from the capture side: type a postal State on creation, open the statutory screen, and the
    /// Home State picker is already on it before the operator touches anything.
    /// <para><i>Mutation that reddens it:</i> delete the seeding statement in <c>LoadFromCompany</c>.</para>
    /// </summary>
    [Fact]
    public void A_new_company_with_a_postal_State_seeds_the_GST_Home_State_when_the_statutory_screen_opens()
    {
        var vm = CreateThroughScreen("Seed Kerala Co", f => PickState(f, "Kerala"));

        vm.ShowGstConfig();
        Assert.NotNull(vm.GstConfig);
        Assert.Equal("32", vm.GstConfig!.HomeState?.Code);
    }

    /// <summary>
    /// The seed must never overwrite a REGISTRATION. A stored Home State of 27 against a postal State of Kerala
    /// keeps 27 — because the Home State decides intra- versus inter-state supply, i.e. CGST+SGST versus IGST,
    /// on every invoice the company issues.
    /// <para><i>Mutation that reddens it:</i> change <c>??=</c> to <c>=</c> in the seed.</para>
    /// </summary>
    [Fact]
    public void A_stored_GST_Home_State_is_never_overwritten_by_the_postal_State()
    {
        var vm = CreateThroughScreen("Stored Home State Co", f => PickState(f, "Kerala"));
        var company = vm.Company!;
        EnableGst(company, GstinMaharashtra, "27");
        _storage.Save(company);

        vm.ShowGstConfig();
        Assert.Equal("27", vm.GstConfig!.HomeState?.Code);
    }

    /// <summary>
    /// A GSTIN the operator types wins over the postal default, because its leading two digits ARE the
    /// registration State. Postal Kerala + a Maharashtra GSTIN must land on 27, not fall back to 32.
    /// <para><i>Mutation that reddens it:</i> move the seed so it runs after <c>OnGstinChanged</c>'s
    /// assignment, letting the postal default clobber the GSTIN-derived code.</para>
    /// </summary>
    [Fact]
    public void A_typed_GSTIN_still_wins_over_the_postal_State_seed()
    {
        var vm = CreateThroughScreen("Gstin Wins Co", f => PickState(f, "Kerala"));

        vm.ShowGstConfig();
        Assert.Equal("32", vm.GstConfig!.HomeState?.Code);   // the postal seed, before the GSTIN is typed

        vm.GstConfig!.Gstin = GstinMaharashtra;
        Assert.Equal("27", vm.GstConfig!.HomeState?.Code);
    }

    /// <summary>
    /// 🔴 THE REGRESSION GUARD FOR THE TRAP THE OBVIOUS IMPLEMENTATION FALLS INTO. Reading "the GST home State
    /// defaults from the postal State at creation" literally means building a <c>GstConfig</c> during creation
    /// and stamping <c>HomeStateCode</c> on it. <b>That value cannot survive a reload.</b> The store writes
    /// <c>gst_home_state</c> whenever a config object exists, regardless of <c>Enabled</c>, but rebuilds the
    /// config only when <c>gst_enabled = 1</c> — so the stamp is discarded by the very next load and then
    /// overwritten with NULL by the following save, with nothing reporting an error.
    /// <para>Creating with a postal State must therefore leave the company GST-OFF and its config null, on disk
    /// and in memory, while the postal State itself persists untouched.</para>
    /// <para><i>Mutation that reddens it:</i> stamp <c>company.Gst = new GstConfig { HomeStateCode = … }</c>
    /// during creation.</para>
    /// </summary>
    [Fact]
    public void A_GST_home_State_is_never_written_onto_a_GST_off_company()
    {
        var vm = CreateThroughScreen("No Ghost Gst Co", f => PickState(f, "Kerala"));
        Assert.Null(vm.Company!.Gst);

        var reloaded = Reload("No Ghost Gst Co");
        Assert.Null(reloaded.Gst);
        Assert.False(reloaded.GstEnabled);
        Assert.Equal("Kerala", reloaded.State);

        // Save it a second time and reload again: a stamped value would surface (or be nulled) here.
        _storage.Save(reloaded);
        var twice = Reload("No Ghost Gst Co");
        Assert.Null(twice.Gst);
        Assert.Equal("Kerala", twice.State);
    }

    // =========================================================================================
    // The divergence guard — a warning, never a refusal
    // =========================================================================================

    private const string DivergenceWarning =
        "Postal State 'Kerala' differs from the GST registration State 'Maharashtra (27)'. "
        + "Printed invoices and tax calculation use the GST State.";

    /// <summary>
    /// Divergence is legal, so it is announced and then allowed: the advisory names BOTH values, the accept
    /// succeeds, and both stored values survive it unchanged.
    /// <para><i>Mutation that reddens it:</i> make the guard a refusal (return before saving).</para>
    /// </summary>
    [Fact]
    public void A_postal_State_that_disagrees_with_the_GST_State_raises_a_warning_and_still_saves()
    {
        var vm = CreateThroughScreen("Divergent Co");
        var company = vm.Company!;
        EnableGst(company, GstinMaharashtra, "27");
        _storage.Save(company);

        vm.ShowAlterCompany();
        var form = vm.AlterCompany!;
        PickState(form, "Kerala");

        Assert.Equal(DivergenceWarning, form.StateAdvisory);
        Assert.True(form.Accept());

        var reloaded = Reload("Divergent Co");
        Assert.Equal("Kerala", reloaded.State);              // the postal one, as typed
        Assert.Equal("27", reloaded.Gst!.HomeStateCode);     // the statutory one, untouched
    }

    /// <summary>
    /// The three deliberate silences. Matching States say nothing; a company with GST off says nothing (there
    /// is no second State); a blank postal State says nothing (nothing was claimed).
    /// <para><i>Mutation that reddens it:</i> drop the "GST is enabled" term from
    /// <c>RegisteredStateCodeOf</c> — the GST-off case then warns on every book on disk that does not use
    /// GST.</para>
    /// </summary>
    [Fact]
    public void Matching_States_and_GST_off_and_a_blank_postal_State_all_raise_no_warning()
    {
        var gstOn = CompanyFactory.CreateSeeded("Silences Co", new DateOnly(2025, 4, 1));
        EnableGst(gstOn, GstinMaharashtra, "27");

        gstOn.State = "Maharashtra";
        Assert.Equal(string.Empty, StoredAdvisory(gstOn));   // matching

        gstOn.State = null;
        Assert.Equal(string.Empty, StoredAdvisory(gstOn));   // nothing claimed

        gstOn.State = "   ";
        Assert.Equal(string.Empty, StoredAdvisory(gstOn));   // whitespace is still nothing

        var gstOff = CompanyFactory.CreateSeeded("Silences Off Co", new DateOnly(2025, 4, 1));
        gstOff.State = "Kerala";
        Assert.Null(gstOff.Gst);
        Assert.Equal(string.Empty, StoredAdvisory(gstOff));  // no registration to disagree with

        // …and a config that exists but is switched OFF is not a registration either.
        EnableGst(gstOff, GstinMaharashtra, "27");
        gstOff.Gst!.Enabled = false;
        Assert.Equal(string.Empty, StoredAdvisory(gstOff));
    }

    /// <summary>
    /// A stored postal State the list does not recognise gets its OWN message, not the divergence one.
    /// Comparing an unresolvable name against a code and announcing "they differ" would report a divergence
    /// that may not exist — and this case is reachable today, because canonical import assigns
    /// <c>Company.State</c> verbatim with no list check.
    /// <para>🔴 <b>And the message quotes the value AS STORED.</b> The lookup is untrimmed on purpose, so the
    /// ER-13 fixture "West Bengal " really is unrecognised — but the message used to quote the TRIMMED text
    /// back, producing "Postal State 'West Bengal' is not a recognised State/UT" about a book whose State
    /// reads West Bengal. Since the difference is whitespace, and whitespace is invisible inside quotation
    /// marks, the message has to say so in words.</para>
    /// <para><i>Mutations that redden it:</i> fold the unrecognised case into the divergence branch; or report
    /// <c>postalText</c> instead of the raw value; or drop the whitespace clause.</para>
    /// </summary>
    [Fact]
    public void An_unrecognised_postal_State_gets_its_own_message_not_the_divergence_one()
    {
        var company = CompanyFactory.CreateSeeded("Unrecognised Co", new DateOnly(2025, 4, 1));
        // The registration is set directly rather than through the engine: the advisory reads only the
        // enabled flag and the home State code, and routing through a GSTIN validator here would make the
        // test depend on a checksum that has nothing to do with what it is asserting.
        company.Gst = new GstConfig { Enabled = true, HomeStateCode = "19" };
        company.State = "WB";

        Assert.Equal(
            "Postal State 'WB' is not a recognised State/UT, so it cannot be checked against the GST "
            + "registration State 'West Bengal (19)'.",
            StoredAdvisory(company));

        // The ER-13 fixture: a value that differs from a real State/UT only by trailing whitespace.
        company.State = "West Bengal ";
        Assert.Equal(
            "Postal State 'West Bengal ' is not a recognised State/UT — it has leading or trailing spaces, "
            + "so it cannot be checked against the GST registration State 'West Bengal (19)'.",
            StoredAdvisory(company));
    }

    /// <summary>
    /// The warning is SYMMETRIC: the same divergent company produces the same advisory from the profile screen
    /// and from the statutory screen. Either screen can create the divergence, so warning on only one of them
    /// would announce it when it arrives from the advisory side and stay silent when it arrives from the
    /// statutory side — which is the side that decides the tax head.
    /// <para>Both are asserted against the EXPECTED LITERAL, not against each other: "the two screens agree"
    /// is satisfied by both being wrong, and would stay green if the rule were relaxed in both places.</para>
    /// <para><i>Mutation that reddens it:</i> delete the statutory screen's advisory property, or change either
    /// screen to compute its own wording.</para>
    /// </summary>
    [Fact]
    public void The_warning_is_symmetric_across_both_screens()
    {
        var vm = CreateThroughScreen("Symmetry Co", f => PickState(f, "Kerala"));
        var company = vm.Company!;
        EnableGst(company, GstinMaharashtra, "27");
        _storage.Save(company);

        vm.ShowAlterCompany();
        Assert.Equal(DivergenceWarning, vm.AlterCompany!.StateAdvisory);

        vm.ShowGstConfig();
        Assert.Equal(DivergenceWarning, vm.GstConfig!.PostalStateAdvisory);
    }

    /// <summary>
    /// 🔴 …AND THE STATUTORY HALF IS ACTUALLY ON THE SCREEN. The view-model property above was computed,
    /// tested and <b>bound nowhere</b>: <c>PostalStateAdvisory</c> had four occurrences in the whole tree,
    /// three in its own class and one in this file, and ZERO in any <c>.axaml</c>. So the test above passed
    /// while the symmetry it names did not exist for a user — moving the GST Home State away from the postal
    /// State on the statutory screen produced exactly the silence the design calls unacceptable.
    /// <para>A rendered-surface claim needs a rendered-surface assertion. The profile screen's own
    /// <c>StateAdvisory</c> is checked here too, so the pair cannot regress one at a time.</para>
    /// <para><i>Mutation that reddens it:</i> delete either binding from <c>MainWindow.axaml</c>.</para>
    /// </summary>
    [Fact]
    public void Both_State_advisories_are_bound_in_the_window_not_only_computed()
    {
        var xaml = File.ReadAllText(WindowXamlPath());

        Assert.Contains("{Binding PostalStateAdvisory}", xaml, StringComparison.Ordinal);
        Assert.Contains("{Binding StateAdvisory}", xaml, StringComparison.Ordinal);
    }

    /// <summary>
    /// 🔴 THE FIELD LABELS ARE THE CORPUS'S OWN WORDS. Six of the twelve shipped labels originally matched
    /// NEITHER primary source — "Year begins from", "Books begin from", "Symbol", "Decimal places" and, worst,
    /// "Decimal unit" for the corpus's *"Word representing amount after decimal"* on a field whose value is
    /// "Paisa". A Tally operator looking for that field would not have recognised it. `docs/full-clone-census.md`
    /// row 9 claimed the field set and screen order as SOURCED while saying nothing about labels, which is why
    /// this is asserted and not merely recorded.
    /// <para>Sources: Book PDF pp.13-14 and Study Guide PDF pp.59-60. Two labels are a CHOICE between the two
    /// primaries rather than a match — "Name" (SG p.58, against the Book's "Company Name") and "Country"
    /// (Book p.13, against the SG's "Statutory Compliance for") — and are logged in grounding §9 item 22.</para>
    /// <para><i>Mutation that reddens it:</i> shorten any of these labels back.</para>
    /// </summary>
    [Theory]
    [InlineData("Mailing Name")]
    [InlineData("Address")]
    [InlineData("State")]
    [InlineData("Country")]
    [InlineData("Pin Code")]
    [InlineData("Financial year begins from")]
    [InlineData("Books beginning from")]
    [InlineData("Base Currency symbol")]
    [InlineData("Formal Name")]
    [InlineData("Number of decimal places")]
    [InlineData("Word representing amount after decimal")]
    public void The_profile_form_uses_the_corpus_field_label(string label)
    {
        var xaml = File.ReadAllText(WindowXamlPath());

        // Both forms carry it: Company Creation and Company Alteration are one field set, and a label that
        // drifted on only one of them would be the two screens disagreeing — the thing the shared view model
        // exists to prevent.
        var occurrences = System.Text.RegularExpressions.Regex.Matches(
            xaml, "Text=\"" + System.Text.RegularExpressions.Regex.Escape(label) + "\"").Count;
        Assert.True(occurrences >= 2,
            $"'{label}' appears as a label {occurrences} time(s); Company Creation and Company Alteration must "
            + "both carry the corpus wording (Book pp.13-14 / Study Guide pp.59-60).");
    }

    /// <summary>
    /// The message line is bound by SEVERITY, not by one property in the alert colour. A single block bound to
    /// `Message` with `Foreground=AlertRed` printed "Company details saved." in red — a successful save
    /// reported as a failure — and the view-model half of the fix is worthless if the view does not use it.
    /// <para><i>Mutation that reddens it:</i> bind either block back to `Message`.</para>
    /// </summary>
    [Fact]
    public void The_message_line_is_bound_by_severity_in_the_window()
    {
        var xaml = File.ReadAllText(WindowXamlPath());

        Assert.Contains("{Binding ErrorMessage}", xaml, StringComparison.Ordinal);
        Assert.Contains("{Binding ConfirmationMessage}", xaml, StringComparison.Ordinal);
        Assert.Contains("{Binding CreateCompanyProfile.ErrorMessage}", xaml, StringComparison.Ordinal);
        Assert.Contains("{Binding CreateCompanyProfile.ConfirmationMessage}", xaml, StringComparison.Ordinal);
    }

    private static string WindowXamlPath([CallerFilePath] string thisFile = "")
        => Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(thisFile)!, "..", "..",
            "src", "Apex.Desktop", "Views", "MainWindow.axaml"));

    // =========================================================================================
    // PIN validation — the screen's friendly refusal, and the engine floor behind it
    // =========================================================================================

    /// <summary>
    /// A bad PIN is refused on the screen with the SCREEN's own message, no exception, and nothing written.
    /// <para>🔴 <b>The assertion is the friendly literal, and that is the whole test.</b> It used to be
    /// <c>Assert.Contains("PIN", …, OrdinalIgnoreCase)</c>, which cannot separate the screen's wording from
    /// the engine's — so deleting the view model's pre-check left this test GREEN: <c>Accept</c> wraps the
    /// save, <c>SaveFailure.IsReportable</c> lists <c>ArgumentException</c>, and the engine's message
    /// contains "PIN" too. The mutation the doc named did not redden the test the doc named it on.</para>
    /// <para><i>Mutation that reddens it:</i> delete the view model's PIN pre-check — the message then becomes
    /// the engine's "Company PIN code '70003' is not a valid 6-digit Indian PIN code.", which both assertions
    /// below reject.</para>
    /// </summary>
    [Fact]
    public void A_bad_PIN_typed_into_the_screen_is_refused_with_a_message_and_nothing_is_saved()
    {
        var vm = CreateThroughScreen("Bad Pin Co", f => f.Pin = "700039");
        Assert.Equal("700039", vm.Company!.Pin);

        vm.ShowAlterCompany();
        var form = vm.AlterCompany!;
        form.Pin = "70003";                       // five digits

        Assert.False(form.Accept());              // refused, and it did not throw
        Assert.Equal(ScreenPinRefusal, form.Message);
        Assert.DoesNotContain("Company PIN code", form.Message!, StringComparison.Ordinal);
        Assert.True(form.MessageIsError);

        Assert.Equal("700039", vm.Company!.Pin);  // the aggregate was not mutated…
        Assert.Equal("700039", Reload("Bad Pin Co").Pin);   // …and neither was the book
    }

    /// <summary>
    /// The floor behind the friendly message: <c>CompanyStorage.Save</c> calls <c>Company.EnsureValid()</c>, so
    /// a bad PIN set straight onto the aggregate — bypassing every screen — is refused by the store itself. The
    /// message names the COMPANY, deliberately distinct from the party-side wording.
    /// <para><i>Mutation that reddens it:</i> delete <c>company.EnsureValid();</c> from
    /// <c>CompanyStorage.Save</c>. That is the mutation the slice records, and this is the test it reddens.</para>
    /// </summary>
    [Theory]
    // The company name is a LITERAL per case. It used to be derived from Math.Abs(pin.GetHashCode()), which is
    // randomised per process on .NET Core — so the fixture name differed between runs — and Math.Abs on
    // int.MinValue throws outright.
    [InlineData("070039", "Engine Floor Leading Zero Co")]   // a naive "six digits" check accepts it; India Post does not
    [InlineData("70003", "Engine Floor Five Digit Co")]
    [InlineData("7000399", "Engine Floor Seven Digit Co")]
    [InlineData("abcdef", "Engine Floor Alpha Co")]          // the value the shared rule's doc names as its reason to exist
    // Six characters, a valid leading ASCII digit, and ONE Devanagari digit in the middle — so the only rule
    // that can reject it is char.IsAsciiDigit. Nothing else in the repository walks a non-ASCII digit, which
    // left the ASCII half of the rule unprovable.
    [InlineData("7000३3", "Engine Floor Devanagari Co")]
    public void The_engine_guard_still_refuses_a_bad_PIN_when_the_screen_check_is_bypassed(string pin, string name)
    {
        var vm = CreateThroughScreen(name);
        var company = vm.Company!;
        company.Pin = pin;

        var ex = Assert.Throws<ArgumentException>(() => _storage.Save(company));
        Assert.Contains("Company PIN code", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The values the rule ACCEPTS, so the guard cannot be satisfied by refusing everything: the corpus's own
    /// worked PIN, the same value with surrounding whitespace, and unset.
    /// <para>🔴 <b>The whitespace case is stored VERBATIM, and the old doc said otherwise.</b> It claimed "the
    /// rule trims"; measured, <c>IndianPinCode.IsValidOrBlank</c> trims FOR VALIDATION ONLY and nothing trims
    /// for storage, so " 700039 " persists with its spaces and would print as <c>PIN:  700039 </c> in the Rule
    /// 46(a) block. This test never reloaded, despite being named <c>…_PIN_saves</c>, so it could not have
    /// seen that. It reloads now and asserts the truth. (Typing the same value INTO the screen trims it —
    /// <c>Validate</c> does that — which is why the padded value can only arrive by direct assignment or by
    /// canonical import.)</para>
    /// <para><i>Mutation that reddens it:</i> make <c>IndianPinCode.IsValidOrBlank</c> reject blanks, or drop
    /// its <c>Trim()</c>.</para>
    /// </summary>
    [Theory]
    [InlineData("700039", "Good Pin Plain Co")]
    [InlineData(" 700039 ", "Good Pin Padded Co")]
    [InlineData("", "Good Pin Empty Co")]
    [InlineData(null, "Good Pin Unset Co")]
    public void A_valid_or_unset_PIN_saves(string? pin, string name)
    {
        var vm = CreateThroughScreen(name);
        var company = vm.Company!;
        company.Pin = pin;

        _storage.Save(company);       // must not throw
        Assert.Equal(pin, company.Pin);
        Assert.Equal(pin, Reload(name).Pin);   // …and it is stored exactly as given, spaces and all
    }

    // =========================================================================================
    // The book dates — the second invariant, on BOTH paths
    // =========================================================================================

    /// <summary>
    /// The SECOND invariant this screen made reachable, and it is not the PIN one.
    /// <c>BooksBeginFrom >= FinancialYearStart</c> was enforced by <c>Company</c>'s CONSTRUCTOR and by nothing
    /// else — both dates are plain settable properties — so assigning them as properties could persist a
    /// company the domain's own constructor would refuse. The screen refuses the state rather than producing
    /// it, and <c>EnsureValid</c> is the floor behind that (see the store test below).
    /// <para><i>Mutation that reddens it:</i> delete the date comparison from the screen's validation.</para>
    /// </summary>
    [Fact]
    public void Books_beginning_before_the_financial_year_start_is_refused_on_the_screen()
    {
        var vm = CreateThroughScreen("Book Dates Co");
        vm.ShowAlterCompany();
        var form = vm.AlterCompany!;

        form.FinancialYearStartText = "01-Apr-2025";
        form.BooksBeginFromText = "01-Jan-2025";

        Assert.False(form.Accept());
        Assert.False(string.IsNullOrWhiteSpace(form.Message));
        Assert.Contains("Books beginning from", form.Message!, StringComparison.Ordinal);

        // And the creation path refuses the same pair, so the two cannot diverge on what they accept.
        var creating = NewShell();
        creating.NewCompanyName = "Book Dates Create Co";
        creating.CreateCompanyProfile.FinancialYearStartText = "01-Apr-2025";
        creating.CreateCompanyProfile.BooksBeginFromText = "01-Jan-2025";
        creating.CreateCompany();
        Assert.Null(creating.Company);
        Assert.DoesNotContain(_storage.ListCompanies(), e => e.Name == "Book Dates Create Co");
    }

    /// <summary>
    /// 🔴 THE CRASH. Type ONLY a books date, earlier than 1-Apr of the current calendar year, and press Ctrl+A.
    ///
    /// <para>This is the INVITED input — the field's own placeholder reads "blank = 1-Apr of this year" — and
    /// it used to throw <c>ArgumentException</c> out of <c>Company</c>'s constructor, through
    /// <c>CompanyFactory.CreateSeeded</c>, past <c>CreateCompany</c>, past <c>ActivateSelected</c>, to the
    /// Avalonia dispatcher. Unhandled. No message on the form. The screen's guard read
    /// <c>fyStart ?? _company?.FinancialYearStart</c>, and on the CREATION path both terms are null, so the
    /// comparison short-circuited away entirely — the guard never learned what the factory was about to
    /// substitute.</para>
    ///
    /// <para>The date used is deliberately derived from <c>CompanyFactory.DefaultFinancialYearStart</c> rather
    /// than hard-coded, so this test still means the same thing next January.</para>
    ///
    /// <para><i>Mutations that redden it:</i> drop the <c>?? CompanyFactory.DefaultFinancialYearStart</c> term
    /// from the guard (the refusal disappears and the test throws); or re-derive that default inside the view
    /// model instead of reading it from the factory, and change one of them.</para>
    /// </summary>
    [Fact]
    public void Creating_with_only_a_books_date_before_the_default_year_start_is_refused_not_crashed()
    {
        var beforeDefault = CompanyFactory.DefaultFinancialYearStart.AddDays(-1);

        var vm = NewShell();
        vm.ShowCompanySelect();
        ActivateMenuItem(vm, "Create Company");
        vm.NewCompanyName = "Books Only Co";
        vm.CreateCompanyProfile.BooksBeginFromText = ApexDate.Format(beforeDefault);

        vm.ActivateSelected();      // the real Ctrl+A path — must not throw

        Assert.Null(vm.Company);
        Assert.Equal(Screen.CreateCompany, vm.CurrentScreen);
        Assert.Contains("Books beginning from", vm.CreateCompanyProfile.Message!, StringComparison.Ordinal);
        // The refusal names the date it is refusing against — otherwise it refuses against an invisible number.
        Assert.Contains(ApexDate.Format(CompanyFactory.DefaultFinancialYearStart),
                        vm.CreateCompanyProfile.Message!, StringComparison.Ordinal);
        Assert.True(vm.CreateCompanyProfile.MessageIsError);
        Assert.Empty(_storage.ListCompanies());
    }

    /// <summary>
    /// …and the SAME books date is accepted when the operator also types a year start that permits it, so the
    /// guard above cannot be satisfied by refusing every books date.
    /// </summary>
    [Fact]
    public void The_same_books_date_is_accepted_when_a_matching_year_start_is_typed()
    {
        var vm = CreateThroughScreen("Old Books Co", f =>
        {
            f.FinancialYearStartText = "01-Apr-2018";
            f.BooksBeginFromText = "15-Aug-2018";
        });

        Assert.NotNull(vm.Company);
        Assert.Equal(new DateOnly(2018, 8, 15), Reload("Old Books Co").BooksBeginFrom);
    }

    /// <summary>
    /// THE FLOOR BEHIND THE SCREEN, and it closes a real data-loss hole: <c>CompanyStorage.Save</c> used to
    /// write a company that <c>Load</c> could never reopen. Both dates are plain settable properties, so
    /// assigning them straight onto the aggregate produced a book that saved without complaint and then threw
    /// <c>ArgumentException</c> out of <c>Company</c>'s constructor on the way back in — permanently
    /// unopenable, with no UI recovery.
    /// <para><i>Mutation that reddens it:</i> delete the date clause from <c>Company.EnsureValid</c>.</para>
    /// </summary>
    [Fact]
    public void The_store_refuses_a_company_whose_books_start_before_its_financial_year()
    {
        var vm = CreateThroughScreen("Unopenable Co");
        var company = vm.Company!;

        company.BooksBeginFrom = company.FinancialYearStart.AddDays(-1);

        var ex = Assert.Throws<ArgumentException>(() => _storage.Save(company));
        Assert.Contains("earlier than the financial-year start", ex.Message, StringComparison.Ordinal);

        // …and the book on disk is still the one that was saved before the bad assignment: still openable.
        Assert.Equal(company.FinancialYearStart, Reload("Unopenable Co").BooksBeginFrom);
    }

    /// <summary>
    /// A date the parser cannot read is REFUSED, not silently ignored. Nothing typed a malformed date into
    /// either company screen before this, so replacing the rejection with <c>return true;</c> — which
    /// discards the typed value and saves the old one — was green across the entire suite.
    /// <para><i>Mutation that reddens it:</i> make <c>TryReadDate</c> return true on a parse failure.</para>
    /// </summary>
    [Fact]
    public void A_date_the_parser_cannot_read_is_refused_on_both_company_screens()
    {
        var vm = CreateThroughScreen("Bad Date Co", f => f.FinancialYearStartText = "01-Apr-2021");
        Assert.Equal(new DateOnly(2021, 4, 1), vm.Company!.FinancialYearStart);

        vm.ShowAlterCompany();
        var form = vm.AlterCompany!;
        form.FinancialYearStartText = "31-Feb-2021";      // a day that does not exist

        Assert.False(form.Accept());
        Assert.Contains("Financial year begins from", form.Message!, StringComparison.Ordinal);
        Assert.Contains("31-Feb-2021", form.Message!, StringComparison.Ordinal);
        Assert.Equal(new DateOnly(2021, 4, 1), Reload("Bad Date Co").FinancialYearStart);

        // The creation path refuses it too, and creates nothing.
        var creating = NewShell();
        creating.NewCompanyName = "Bad Date Create Co";
        creating.CreateCompanyProfile.BooksBeginFromText = "not a date";
        creating.CreateCompany();
        Assert.Null(creating.Company);
        Assert.Contains("Books beginning from", creating.CreateCompanyProfile.Message!, StringComparison.Ordinal);
        Assert.DoesNotContain(_storage.ListCompanies(), e => e.Name == "Bad Date Create Co");
    }

    // =========================================================================================
    // Decimal places — a branch that was completely dead to the suite
    // =========================================================================================

    /// <summary>
    /// The decimal-places field is CAPTURED, its range is enforced, and the refusal is the screen's own
    /// wording. All three were unpinned: never capturing it, removing the 0–4 range check, and replacing the
    /// message were each green across the full suite, because no fixture ever typed a non-default value.
    /// <para><i>Mutations that redden it:</i> stop assigning <c>DecimalPlaces</c> in <c>Apply</c> / in
    /// <c>CreateCompany</c>; drop the <c>places &lt; 0 || places &gt; 4</c> test; change the message.</para>
    /// </summary>
    [Theory]
    [InlineData("5")]      // above the range
    [InlineData("-1")]     // below it (and NumberStyles.None rejects the sign outright)
    [InlineData("two")]    // not a number
    [InlineData("2.5")]    // not a whole number
    public void A_decimal_places_value_outside_zero_to_four_is_refused_with_the_screens_own_message(string typed)
    {
        var vm = CreateThroughScreen("Decimals Co");
        vm.ShowAlterCompany();
        var form = vm.AlterCompany!;
        form.DecimalPlacesText = typed;

        Assert.False(form.Accept());
        Assert.Equal("Number of decimal places must be a whole number from 0 to 4.", form.Message);
        Assert.Equal(2, Reload("Decimals Co").DecimalPlaces);
    }

    /// <summary>The accepted end of the same rule, on both paths, with a NON-default value.</summary>
    [Fact]
    public void A_decimal_places_value_inside_the_range_is_captured_on_both_paths()
    {
        var vm = CreateThroughScreen("Three Dp Co", f => f.DecimalPlacesText = "3");
        Assert.Equal(3, Reload("Three Dp Co").DecimalPlaces);

        vm.ShowAlterCompany();
        var form = vm.AlterCompany!;
        form.DecimalPlacesText = "0";
        Assert.True(form.Accept());
        Assert.Equal(0, Reload("Three Dp Co").DecimalPlaces);
    }

    // =========================================================================================
    // Persistence and the round trip
    // =========================================================================================

    /// <summary>
    /// The whole postal block typed into the CREATION screen survives a save, a reload from the <c>.db</c>,
    /// AND a canonical export/import into a different company.
    /// <para>Every fixture value is deliberately NOT the seeded default — Country is "Bharat", not "India" —
    /// because a fixture that matches the default cannot distinguish "applied" from "untouched".</para>
    /// <para><i>Mutation that reddens it:</i> drop any field from <c>CreateCompany</c>'s profile
    /// application.</para>
    /// </summary>
    [Fact]
    public void A_postal_block_typed_into_the_screen_survives_save_reload_and_a_canonical_round_trip()
    {
        var vm = CreateThroughScreen("Round Trip Traders", f =>
        {
            f.MailingName = "Round Trip Traders Pvt Ltd";
            f.Address = "13A, Picnic Garden Road\n3rd Lane\nKolkata";
            PickState(f, "West Bengal");
            f.Country = "Bharat";
            f.Pin = "700039";
        });

        var reloaded = Reload("Round Trip Traders");
        Assert.Equal("Round Trip Traders Pvt Ltd", reloaded.MailingName);
        Assert.Equal("13A, Picnic Garden Road\n3rd Lane\nKolkata", reloaded.Address);
        Assert.Equal("West Bengal", reloaded.State);
        Assert.Equal("Bharat", reloaded.Country);
        Assert.Equal("700039", reloaded.Pin);

        var (model, errors) = CanonicalJson.Parse(CanonicalJson.Export(reloaded));
        Assert.Empty(errors);
        Assert.NotNull(model);

        var target = CompanyFactory.CreateSeeded("Round Trip Target", new DateOnly(2025, 4, 1));
        Assert.True(new CompanyImportService(target).Apply(model!, DuplicatePolicy.Skip).Applied);

        Assert.Equal("13A, Picnic Garden Road\n3rd Lane\nKolkata", target.Address);
        Assert.Equal("West Bengal", target.State);
        Assert.Equal("Bharat", target.Country);
        Assert.Equal("700039", target.Pin);
    }

    /// <summary>
    /// 🔴 THE ALTERATION LEG, which did not exist. The round-trip test above drives CREATION, and
    /// <c>Apply</c> — the alteration capture — is never reached by it (<c>Accept</c> returns false when the
    /// company is null). Measured: deleting Address, Country, both date writes and all four currency writes
    /// from <c>Apply</c>, keeping only State and Pin, left the FULL Desktop suite green. Only MailingName and
    /// State had any pin at all; eight of Apply's eleven writes were dead.
    /// <para>So: open Alter on a saved company, change ALL ELEVEN, accept, and reload from the <c>.db</c>.
    /// Every value differs from both the seeded default and the value it replaces.</para>
    /// <para><i>Mutation that reddens it:</i> drop ANY field from <c>Apply</c>.</para>
    /// </summary>
    [Fact]
    public void Every_one_of_the_eleven_fields_altered_on_the_screen_survives_a_save_and_a_reload()
    {
        var vm = CreateThroughScreen("Alter Round Trip Co", f =>
        {
            f.MailingName = "Before Traders";
            f.Address = "1 Before Road";
            PickState(f, "Goa");
            f.Country = "India";
            f.Pin = "403001";
            f.FinancialYearStartText = "01-Apr-2019";
            f.BooksBeginFromText = "01-Jul-2019";
            f.BaseCurrencySymbol = "$";
            f.BaseCurrencyName = "USD";
            f.DecimalPlacesText = "2";
            f.DecimalUnitName = "Cent";
        });

        vm.ShowAlterCompany();
        var form = vm.AlterCompany!;
        form.MailingName = "After Traders Pvt Ltd";
        form.Address = "13A, Picnic Garden Road\n3rd Lane\nKolkata";
        PickState(form, "Kerala");
        form.Country = "Bharat";
        form.Pin = "682001";
        form.FinancialYearStartText = "01-Apr-2021";
        form.BooksBeginFromText = "15-Aug-2021";
        form.BaseCurrencySymbol = "€";
        form.BaseCurrencyName = "EUR";
        form.DecimalPlacesText = "3";
        form.DecimalUnitName = "Centime";

        Assert.True(form.Accept());

        var reloaded = Reload("Alter Round Trip Co");
        Assert.Equal("After Traders Pvt Ltd", reloaded.MailingName);
        Assert.Equal("13A, Picnic Garden Road\n3rd Lane\nKolkata", reloaded.Address);
        Assert.Equal("Kerala", reloaded.State);
        Assert.Equal("Bharat", reloaded.Country);
        Assert.Equal("682001", reloaded.Pin);
        Assert.Equal(new DateOnly(2021, 4, 1), reloaded.FinancialYearStart);
        Assert.Equal(new DateOnly(2021, 8, 15), reloaded.BooksBeginFrom);
        Assert.Equal("€", reloaded.BaseCurrencySymbol);
        Assert.Equal("EUR", reloaded.BaseCurrencyName);
        Assert.Equal(3, reloaded.DecimalPlaces);
        Assert.Equal("Centime", reloaded.DecimalUnitName);
    }

    /// <summary>
    /// The blank-never-overwrites-a-default rule, which had no coverage either: clearing Country, the currency
    /// symbol/name or the decimal unit must leave the stored value alone rather than writing <c>""</c> into a
    /// NOT NULL column, and clearing Mailing Name falls back to the company NAME.
    /// <para>🔴 <b>And the form is left showing what the BOOK now holds.</b> <c>Apply</c> coalesces those four
    /// values, and without a sync the operator was left looking at an empty control while the book carried a
    /// value — the form and the book disagreeing after a save the screen called successful.</para>
    /// <para><i>Mutations that redden it:</i> remove a blank-guard from <c>Apply</c>; remove the
    /// <c>SyncFromCompany</c> call from <c>Accept</c>.</para>
    /// </summary>
    [Fact]
    public void Clearing_a_defaulted_field_keeps_the_stored_value_and_the_form_is_resynced_to_the_book()
    {
        var vm = CreateThroughScreen("Blank Guard Co", f =>
        {
            f.MailingName = "Blank Guard Traders";
            f.Country = "Bharat";
            f.BaseCurrencySymbol = "$";
            f.BaseCurrencyName = "USD";
            f.DecimalUnitName = "Cent";
        });

        vm.ShowAlterCompany();
        var form = vm.AlterCompany!;
        form.MailingName = "   ";
        form.Country = "";
        form.BaseCurrencySymbol = "  ";
        form.BaseCurrencyName = "";
        form.DecimalUnitName = "   ";

        Assert.True(form.Accept());

        var reloaded = Reload("Blank Guard Co");
        Assert.Equal("Blank Guard Co", reloaded.MailingName);   // fell back to the company name
        Assert.Equal("Bharat", reloaded.Country);               // kept
        Assert.Equal("$", reloaded.BaseCurrencySymbol);
        Assert.Equal("USD", reloaded.BaseCurrencyName);
        Assert.Equal("Cent", reloaded.DecimalUnitName);

        // The controls now show the book, not the blanks that were typed over it.
        Assert.Equal("Blank Guard Co", form.MailingName);
        Assert.Equal("Bharat", form.Country);
        Assert.Equal("$", form.BaseCurrencySymbol);
        Assert.Equal("USD", form.BaseCurrencyName);
        Assert.Equal("Cent", form.DecimalUnitName);
    }

    /// <summary>
    /// 🔴 THE ER-13 GUARD ON THE ALTER SCREEN, and the only thing standing between it and silent data loss on a
    /// canonical-imported book. Canonical import assigns <c>Company.State</c> verbatim with no list check, so a
    /// book on disk can hold "West Bengal " with a trailing space. Opening alteration and accepting without
    /// touching the control must write that string back BYTE-IDENTICALLY — not blanked, not "corrected".
    /// <para>The row is also MARKED in the picker. Without a marker the list shows two entries that render
    /// character-for-character identically ("West Bengal" and "West Bengal "), and picking the wrong one
    /// silently rewrites the one field whose entire justification is byte-identical survival. What is WRITTEN
    /// is <c>StoredValue</c>, which the marker does not touch.</para>
    /// <para><i>Mutations that redden it:</i> make the picker fall back to null (or to the first item) when
    /// the stored value is not in the list; or drop the marker from <c>Display</c>; or put the marker into
    /// <c>StoredValue</c>.</para>
    /// </summary>
    [Fact]
    public void Opening_Alter_on_a_company_whose_stored_State_is_not_in_the_list_and_accepting_changes_nothing()
    {
        const string Stored = "West Bengal ";   // trailing space — a value canonical import can produce
        var vm = CreateThroughScreen("Verbatim State Co");
        var company = vm.Company!;
        company.State = Stored;
        _storage.Save(company);
        Assert.Null(IndianState.FromName(Stored));   // the list really does not recognise it

        vm.ShowAlterCompany();
        var form = vm.AlterCompany!;
        Assert.True(form.SelectedState!.IsUnrecognised);
        Assert.Equal(Stored, form.SelectedState!.StoredValue);       // what an accept writes: verbatim
        Assert.StartsWith(Stored, form.SelectedState!.Display, StringComparison.Ordinal);
        // …and the row is VISIBLY marked. Asserting only "its Display differs from the canonical row's" is
        // vacuous here: the two already differ by the trailing space, which is exactly the character a reader
        // cannot see. The marker is what makes the two rows distinguishable ON SCREEN, so it is asserted as
        // text the stored value does not contain.
        Assert.NotEqual(Stored, form.SelectedState!.Display);
        Assert.Contains("(stored on this book)", form.SelectedState!.Display, StringComparison.Ordinal);
        Assert.DoesNotContain("(stored on this book)",
            form.StateOptions.Single(o => o.State?.Name == "West Bengal").Display, StringComparison.Ordinal);

        Assert.True(form.Accept());
        Assert.Equal(Stored, Reload("Verbatim State Co").State);
    }

    // =========================================================================================
    // ER-13 — a company where none of this is touched must be exactly what it always was
    // =========================================================================================

    /// <summary>
    /// A creation where only the name was typed must produce the company the product produced before this
    /// screen existed: a blank address, the "India" country default, no PIN, and the seeded 1-Apr financial
    /// year. This is what keeps every book already on disk, and ~150 existing fixtures, exactly where they were
    /// — and it is what stops every historical invoice gaining a supplier block containing the single word
    /// "India".
    /// <para><i>Mutation that reddens it:</i> make the screen write its empty controls through instead of
    /// leaving the defaults alone — e.g. assign <c>Country</c> unconditionally.</para>
    /// </summary>
    [Fact]
    public void A_creation_that_types_nothing_but_the_name_is_unchanged_from_the_seeded_default()
    {
        var vm = CreateThroughScreen("Untouched Co");
        var company = vm.Company!;

        Assert.Equal("Untouched Co", company.Name);
        Assert.Equal("Untouched Co", company.MailingName);
        Assert.Null(company.Address);
        Assert.Equal("India", company.Country);
        Assert.Null(company.State);
        Assert.Null(company.Pin);
        Assert.Equal("₹", company.BaseCurrencySymbol);
        Assert.Equal("INR", company.BaseCurrencyName);
        Assert.Equal(2, company.DecimalPlaces);
        Assert.Equal("Paisa", company.DecimalUnitName);

        // The seeded default still governs the dates — the screen passes nothing when nothing was typed.
        var seeded = CompanyFactory.CreateSeeded("Reference Co");
        Assert.Equal(seeded.FinancialYearStart, company.FinancialYearStart);
        Assert.Equal(seeded.BooksBeginFrom, company.BooksBeginFrom);

        var reloaded = Reload("Untouched Co");
        Assert.Null(reloaded.Address);
        Assert.Equal("India", reloaded.Country);
        Assert.Null(reloaded.State);
        Assert.Null(reloaded.Pin);
    }

    /// <summary>
    /// The prior-FY book the screen exists to unblock: creation now accepts the two dates, so a company can be
    /// opened for a year that is not the current one — the wrong-figures defect where a company created in
    /// January was stamped a financial year three months in the future.
    /// <para><i>Mutation that reddens it:</i> stop passing the typed dates into the seeded factory.</para>
    /// </summary>
    [Fact]
    public void A_company_can_be_created_for_a_prior_financial_year()
    {
        var vm = CreateThroughScreen("Prior Year Co", f =>
        {
            f.FinancialYearStartText = "01-Apr-2019";
            f.BooksBeginFromText = "01-Jul-2019";
        });

        Assert.Equal(new DateOnly(2019, 4, 1), vm.Company!.FinancialYearStart);
        Assert.Equal(new DateOnly(2019, 7, 1), vm.Company!.BooksBeginFrom);

        var reloaded = Reload("Prior Year Co");
        Assert.Equal(new DateOnly(2019, 4, 1), reloaded.FinancialYearStart);
        Assert.Equal(new DateOnly(2019, 7, 1), reloaded.BooksBeginFrom);
    }

    // =========================================================================================
    // One file is one book
    // =========================================================================================

    /// <summary>
    /// 🔴 TWO NAMES THAT SANITISE TO ONE FILENAME MUST NOT FORK THE BOOK. The <c>.db</c> path replaces every
    /// character a filename cannot hold with <c>_</c>, so "Acme/Traders" and "Acme_Traders" share one path —
    /// and creation used to write the second company as a SECOND ROW inside the first company's file, with no
    /// exception and no message. <c>CompanyStorage.Load</c> returns the first row, so everything typed into
    /// the second company became unreachable forever.
    /// <para>The alteration screen already refuses to RENAME for exactly this reason. Refusing a rename while
    /// leaving the identical hole open on create is not a coherent position.</para>
    /// <para><b>Why the colliding pair uses '/' and not ':'.</b> This test used to pair "Acme:Traders" with
    /// "Acme_Traders", and a colon is a Windows-ism: <c>Path.GetInvalidFileNameChars()</c> returns 41
    /// characters on Windows but exactly two on Unix, <c>'\0'</c> and <c>'/'</c>. On Linux and macOS a colon
    /// is a perfectly legal filename byte, so that pair mapped to two DIFFERENT files, the guard correctly did
    /// not fire, and the test failed while the product was behaving properly. <c>'/'</c> is the only printable
    /// character invalid on every platform, so it is the only pair that collides everywhere.</para>
    /// <para><i>Mutation that reddens it:</i> delete the <c>_storage.Exists(name)</c> guard from
    /// <c>CreateCompany</c>.</para>
    /// </summary>
    [Fact]
    public void A_second_company_whose_name_sanitises_onto_an_existing_file_is_refused()
    {
        CreateThroughScreen("Acme_Traders");
        Assert.Single(_storage.ListCompanies());

        var second = NewShell();
        second.ShowCompanySelect();
        ActivateMenuItem(second, "Create Company");
        second.NewCompanyName = "Acme/Traders";      // the slash sanitises to '_' on every platform
        second.ActivateSelected();

        Assert.Null(second.Company);
        Assert.Equal(Screen.CreateCompany, second.CurrentScreen);
        Assert.Contains("already exists", second.Message!, StringComparison.OrdinalIgnoreCase);
        Assert.Single(_storage.ListCompanies());     // still exactly one book
    }

    /// <summary>
    /// …and a file that ALREADY holds two companies — written by a build that predates the guard above — is
    /// refused on open rather than silently narrowed to <c>companies[0]</c>. Returning the first row and
    /// carrying on is what made the fork undetectable: every later save landed on the first company while the
    /// operator believed they were editing the second.
    /// <para><i>Mutation that reddens it:</i> delete the <c>companies.Count > 1</c> check from
    /// <c>CompanyStorage.Load</c>.</para>
    /// </summary>
    [Fact]
    public void A_company_file_holding_two_companies_is_refused_rather_than_silently_opening_the_first()
    {
        var path = _storage.PathForName("Forked Co");
        using (var store = new Apex.Persistence.Sqlite.SqliteCompanyStore(path))
        {
            store.Save(CompanyFactory.CreateSeeded("Forked Co", new DateOnly(2025, 4, 1)));
            // '/' rather than ':' — the pair must read as a genuine collision on every platform. A colon is
            // only invalid in a filename on Windows, so "Forked:Co" told a Windows-only story. (This test
            // writes both rows through one open store rather than through PathForName, so it PASSED on POSIX
            // either way — the name was narrative, and the narrative was wrong.)
            store.Save(CompanyFactory.CreateSeeded("Forked/Co", new DateOnly(2025, 4, 1)));
        }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        var entry = _storage.ListCompanies().Single(e => e.Name == "Forked Co");
        var ex = Assert.Throws<InvalidOperationException>(() => _storage.Load(entry));
        Assert.Contains("One file is one book", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A domain or operational refusal raised while CREATING is reported on the form, not thrown at the
    /// dispatcher. Nothing between <c>CreateCompany</c> and Avalonia catches, so an escaped exception is a
    /// crash with no message. The failure is induced the way the field produces it — the company's <c>.db</c>
    /// path cannot be opened for writing.
    /// <para><i>Mutation that reddens it:</i> remove the try/catch from <c>CreateCompany</c>.</para>
    /// </summary>
    [Fact]
    public void An_operational_failure_during_creation_is_reported_on_the_form_not_thrown()
    {
        // A DIRECTORY standing where the .db must be written. File.Exists is false for it, so the name guard
        // lets this through and the store construction is what fails.
        Directory.CreateDirectory(_storage.PathForName("Blocked Co"));

        var vm = NewShell();
        vm.ShowCompanySelect();
        ActivateMenuItem(vm, "Create Company");
        vm.NewCompanyName = "Blocked Co";

        vm.ActivateSelected();      // must not throw

        Assert.Null(vm.Company);
        Assert.Equal(Screen.CreateCompany, vm.CurrentScreen);
        Assert.False(string.IsNullOrWhiteSpace(vm.CreateCompanyProfile.Message));
        Assert.True(vm.CreateCompanyProfile.MessageIsError);
    }

    // =========================================================================================
    // The screen itself: navigation, the read-only name, and the accept confirmation
    // =========================================================================================

    /// <summary>
    /// Alteration is reachable through the real cascade — an "Alter Company" row under MASTERS — and it opens
    /// the profile page pre-filled from the open company.
    /// <para>🔴 <b>Under Masters, and that placement is a correction.</b> The row first shipped as a new
    /// "Company" section placed AHEAD of Masters, which moved the Gateway's default keyboard highlight off
    /// Masters → Create for every entry into the screen. <c>docs/invented-vs-cloned.md</c> IV-29 already
    /// catalogues this menu as invented, names "the menu GREW A SECTION PER PHASE" as the cause, and
    /// prescribes adding "Alter" to MASTERS. So the row sits there, and the Gateway opens on Create as it
    /// always did.</para>
    /// <para><i>Mutation that reddens it:</i> remove the Gateway row, move it back above Masters, or break the
    /// label dispatch that opens it.</para>
    /// </summary>
    [Fact]
    public void Alter_Company_is_reachable_from_the_Gateway_and_opens_pre_filled()
    {
        var vm = CreateThroughScreen("Reachable Co", f =>
        {
            f.MailingName = "Reachable Traders";
            f.Address = "1 Mill Road";
            PickState(f, "Karnataka");
            f.Pin = "560001";
        });
        Assert.Equal(Screen.Gateway, vm.CurrentScreen);

        var root = vm.Columns[0];
        var row = root.Items.Single(i => i.Label == "Alter Company");
        Assert.True(row.IsSubItem);
        Assert.Equal(string.Empty, row.Hint);   // no accelerator is invented for it

        // It is under MASTERS, and the Gateway's opening highlight is still Masters → Create.
        Assert.True(root.Items[0].IsHeader);
        Assert.Equal("Masters", root.Items[0].Label);
        Assert.Equal("Create", root.Items[root.SelectedIndex].Label);

        // Drill in through the real KEYBOARD path — one arrow down from Create — rather than calling the
        // opener or poking an index.
        vm.MoveDown();
        Assert.Equal("Alter Company", root.Items[root.SelectedIndex].Label);
        vm.ActivateSelected();

        Assert.Equal(Screen.AlterCompany, vm.CurrentScreen);
        Assert.NotNull(vm.AlterCompany);
        Assert.True(vm.Columns.Count >= 2, "the Miller cascade must keep the menu column to its left");

        var form = vm.AlterCompany!;
        Assert.True(form.IsAltering);
        Assert.Equal("Company Alteration", form.Caption);
        Assert.Equal("Reachable Traders", form.MailingName);
        Assert.Equal("1 Mill Road", form.Address);
        Assert.Equal("Karnataka", form.SelectedState!.Display);
        Assert.Equal("560001", form.Pin);
    }

    /// <summary>
    /// 🔴 <b>THE BOOK IS NEVER FORKED BY A RENAME — the file MOVES, it is not copied.</b> The company's
    /// <c>.db</c> is NAMED after the company and the company-select list takes each display name back from the
    /// FILENAME, so an accept that assigned <c>company.Name</c> and saved would write a SECOND file and leave the
    /// book forked in two — same company id in both, later saves landing on only one, nothing reporting an error.
    ///
    /// <para><b>†† 2026-09-05 — THIS TEST WAS RE-POINTED, AND ONLY ITS FIRST ASSERTION MOVED.</b> It shipped as
    /// <c>The_company_name_is_display_only_on_alteration_and_accepting_never_forks_the_book</c>, opening
    /// <c>Assert.False(form.IsNameEditable)</c>. That expectation was <b>stale, not wrong when written</b>: the
    /// name was read-only on alteration only because the rename had been carved out into its own slice, which is
    /// census row <b>1.4</b> and has now shipped. <b>The authority for the new expectation is the vendor, not the
    /// new code:</b> RULING 14 / R7 — <i>help.tallysolutions.com/…/set-up-company-tally/</i> renames a company by
    /// <i>"Alt+K (Company) &gt; Alter"</i> and editing the Name on the Company Alteration screen. There is no
    /// separate Rename screen to build; this IS the reference route.</para>
    ///
    /// <para><b>The anti-fork half is deliberately KEPT, unweakened, and it is the half that ever protected
    /// anyone.</b> It no longer says "the name did not change" — it says <b>exactly one book exists afterwards
    /// and it is the renamed one</b>, which is the same invariant stated against a feature that now exists. A
    /// rename that copied instead of moving still reddens it.</para>
    ///
    /// <para><i>Mutations that redden it:</i> assign <c>company.Name = Name</c> in the screen's <c>Apply</c> and
    /// save (forks the book — two entries); or drop the <c>Delete(entry)</c> from
    /// <c>CompanyStorage.Rename</c> (leaves the old file standing — two entries).</para>
    /// </summary>
    [Fact]
    public void Renaming_on_alteration_moves_the_book_and_never_forks_it()
    {
        var vm = CreateThroughScreen("Original Name Co");
        vm.ShowAlterCompany();
        var form = vm.AlterCompany!;

        Assert.True(form.IsNameEditable);
        form.Name = "Renamed Co";
        Assert.True(form.Accept());

        // The aggregate the shell is holding carries the new name...
        Assert.Equal("Renamed Co", vm.Company!.Name);
        // ...and the picker offers ONE book, under that name. Two entries here is the fork.
        var files = _storage.ListCompanies().Select(e => e.Name).ToList();
        Assert.Equal(new[] { "Renamed Co" }, files);
    }

    /// <summary>
    /// The company screens accept exactly the way every other master does — Ctrl+A saves outright, Enter asks
    /// "Accept Company? (Y/N)" first. Creation had NO coverage of its keyboard behaviour at all before this
    /// (only its side effect, a company object, was exercised 150-odd times), so the behaviour change ships
    /// with the coverage rather than unobserved.
    /// <para><i>Mutation that reddens it:</i> drop either company screen from the accept-screen list, or give
    /// the prompt a different noun.</para>
    /// </summary>
    [Fact]
    public void Both_company_screens_carry_the_Accept_Company_confirmation()
    {
        var vm = NewShell();
        vm.ShowCompanySelect();
        ActivateMenuItem(vm, "Create Company");

        Assert.True(vm.IsMasterAcceptScreen);
        Assert.True(vm.RequestMasterAccept());
        Assert.Equal("Accept Company? (Y/N)", vm.AcceptPromptText);

        // "Y" runs the SAME path Ctrl+A runs, so the prompt can never drift from the shortcut.
        vm.NewCompanyName = "Prompted Co";
        Assert.True(vm.ConfirmMasterAccept());
        Assert.False(vm.IsAcceptPromptOpen);
        Assert.Equal(Screen.Gateway, vm.CurrentScreen);
        Assert.Equal("Prompted Co", vm.Company!.Name);

        vm.ShowAlterCompany();
        Assert.True(vm.IsMasterAcceptScreen);
        Assert.True(vm.RequestMasterAccept());
        Assert.Equal("Accept Company? (Y/N)", vm.AcceptPromptText);

        vm.AlterCompany!.MailingName = "Prompted Traders";
        Assert.True(vm.ConfirmMasterAccept());
        Assert.False(vm.IsAcceptPromptOpen);
        Assert.Equal("Prompted Traders", Reload("Prompted Co").MailingName);
    }

    /// <summary>
    /// A successful accept says so, and says so as a CONFIRMATION rather than an alert. The message text was
    /// unpinned — replacing it with anything at all was green — and the view rendered it in the alert colour,
    /// so a successful save printed red. The view now binds two blocks, one per severity, and this is what
    /// stops either half regressing.
    /// <para><i>Mutations that redden it:</i> change the confirmation wording; set <c>MessageIsError</c> true
    /// on the success path (or drop the flag and go back to one colour).</para>
    /// </summary>
    [Fact]
    public void A_successful_accept_reports_a_confirmation_not_an_alert()
    {
        var vm = CreateThroughScreen("Confirmation Co");
        vm.ShowAlterCompany();
        var form = vm.AlterCompany!;
        form.MailingName = "Confirmation Traders";

        Assert.True(form.Accept());
        Assert.Equal(SavedConfirmation, form.Message);
        Assert.False(form.MessageIsError);
        Assert.Equal(SavedConfirmation, form.ConfirmationMessage);
        Assert.Null(form.ErrorMessage);

        // …and a refusal is the other way round.
        form.Pin = "70003";
        Assert.False(form.Accept());
        Assert.True(form.MessageIsError);
        Assert.Equal(ScreenPinRefusal, form.ErrorMessage);
        Assert.Null(form.ConfirmationMessage);
    }

    /// <summary>
    /// The book-dates advisory: moving <c>FinancialYearStart</c> / <c>BooksBeginFrom</c> on a book that
    /// already carries vouchers changes which period every report covers, so the screen says so. It is an
    /// ADVISORY, not a lock — no corpus source makes any company field read-only after creation.
    /// <para>It shipped with zero tests: mutating both its condition and its text was green across the whole
    /// suite. A new user-visible wrong-figures advisory with no coverage at all.</para>
    /// <para><i>Mutations that redden it:</i> change the <c>Vouchers.Count > 0</c> condition; change the
    /// wording; show it on creation.</para>
    /// </summary>
    [Fact]
    public void The_book_dates_advisory_appears_only_on_an_altered_book_that_already_has_vouchers()
    {
        var vm = CreateThroughScreen("Advisory Dates Co");

        // Creation: there is no book yet, so there is nothing to warn about.
        Assert.Equal(string.Empty, vm.CreateCompanyProfile.BookDatesAdvisory);

        // Alteration of an empty book: still nothing.
        vm.ShowAlterCompany();
        Assert.Equal(string.Empty, vm.AlterCompany!.BookDatesAdvisory);

        // Post one voucher and re-open the screen.
        var company = vm.Company!;
        PostOneReceipt(company);
        _storage.Save(company);

        vm.ShowGateway();
        vm.ShowAlterCompany();
        Assert.Equal(
            "This book already has vouchers. Changing these dates changes which period every report covers.",
            vm.AlterCompany!.BookDatesAdvisory);
    }

    /// <summary>One posted voucher, so the book stops being empty. The amounts are irrelevant here.</summary>
    private static void PostOneReceipt(Company company)
    {
        var cash = company.FindLedgerByName("Cash")!;
        var capital = new Apex.Ledger.Domain.Ledger(
            Guid.NewGuid(), "Advisory Capital A/c",
            company.FindGroupByName("Capital Account")!.Id, Money.Zero, openingIsDebit: false);
        company.AddLedger(capital);

        var receiptType = company.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Receipt).Id;
        new LedgerService(company).Post(new Voucher(
            Guid.NewGuid(), receiptType, company.BooksBeginFrom,
            new[]
            {
                new EntryLine(cash.Id, Money.FromRupees(100m), DrCr.Debit),
                new EntryLine(capital.Id, Money.FromRupees(100m), DrCr.Credit),
            }));
    }

    /// <summary>
    /// A failed save must leave the in-memory company exactly as the book on disk has it — <b>all eleven
    /// fields</b>, not the four this test used to check. Every master screen here mutates the shared aggregate
    /// and only then persists, so without the rollback an operational failure (a second instance holding the
    /// write lock, a read-only file) leaves the session holding values the database does not have — a
    /// divergence nothing on screen would reveal.
    /// <para>Measured before this was widened: deleting the Country, both dates and all four currency lines
    /// from <c>Restore</c> was green. A failed save would have left the session holding seven wrong
    /// values.</para>
    /// <para><b>The failure is induced by an UNOPENABLE company file, not by a lock.</b> The lock this test
    /// originally used — a <c>FileShare.None</c> hold — only shuts SQLite out on Windows; see the comment at
    /// the Act below. What is asserted here is the BEHAVIOUR (a reportable save failure rolls every assigned
    /// field back), so the mechanism is chosen to be the one that fails identically on all three
    /// platforms.</para>
    /// <para><i>Mutation that reddens it:</i> delete ANY line from the screen's <c>Restore</c>, or the restore
    /// call from its catch block.</para>
    /// </summary>
    [Fact]
    public void A_failed_save_restores_every_profile_field_it_had_assigned()
    {
        var vm = CreateThroughScreen("Rollback Co", f =>
        {
            f.MailingName = "Rollback Traders";
            f.Address = "9 Old Road";
            PickState(f, "Goa");
            f.Country = "Bharat";
            f.Pin = "403001";
            f.FinancialYearStartText = "01-Apr-2019";
            f.BooksBeginFromText = "01-Jul-2019";
            f.BaseCurrencySymbol = "$";
            f.BaseCurrencyName = "USD";
            f.DecimalPlacesText = "3";
            f.DecimalUnitName = "Cent";
        });
        var company = vm.Company!;

        vm.ShowAlterCompany();
        var form = vm.AlterCompany!;
        form.MailingName = "New Traders";
        form.Address = "11 New Road";
        PickState(form, "Kerala");
        form.Country = "Bharatvarsha";
        form.Pin = "682001";
        form.FinancialYearStartText = "01-Apr-2021";
        form.BooksBeginFromText = "15-Aug-2021";
        form.BaseCurrencySymbol = "€";
        form.BaseCurrencyName = "EUR";
        form.DecimalPlacesText = "4";
        form.DecimalUnitName = "Centime";

        // ---- Make the save fail. ----
        // 🔴 THIS USED TO BE A FileShare.None HOLD ON THE .db, AND THAT IS A WINDOWS-ISM. On Windows a
        // share-mode denial really does shut every other writer out. On Unix, .NET emulates FileShare with an
        // advisory flock(), while SQLite locks with fcntl(F_SETLK) POSIX record locks — two completely
        // independent lock spaces that do not conflict. So on Linux and macOS SQLite opened the "held" file,
        // the save SUCCEEDED, Accept() returned true, and the eleven rollback assertions below were never
        // reached. The product was right; the obstruction was not portable.
        //
        // A DIRECTORY standing where the .db must be is portable — no lock, no permission bit, no dependence
        // on the runner's uid (a root runner ignores a read-only attribute; nothing ignores EISDIR). The store
        // constructor cannot open it on any platform, so SQLITE_CANTOPEN -> SqliteException -> DbException ->
        // SaveFailure.IsReportable -> Restore + Refuse. The same idiom is used, and green on ubuntu today, by
        // An_operational_failure_during_creation_is_reported_on_the_form_not_thrown above.
        var path = _storage.PathForName("Rollback Co");
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        File.Delete(path);
        Directory.CreateDirectory(path);

        Assert.False(form.Accept());
        Assert.False(string.IsNullOrWhiteSpace(form.Message));
        Assert.True(form.MessageIsError);

        Assert.Equal("Rollback Traders", company.MailingName);
        Assert.Equal("9 Old Road", company.Address);
        Assert.Equal("Goa", company.State);
        Assert.Equal("Bharat", company.Country);
        Assert.Equal("403001", company.Pin);
        Assert.Equal(new DateOnly(2019, 4, 1), company.FinancialYearStart);
        Assert.Equal(new DateOnly(2019, 7, 1), company.BooksBeginFrom);
        Assert.Equal("$", company.BaseCurrencySymbol);
        Assert.Equal("USD", company.BaseCurrencyName);
        Assert.Equal(3, company.DecimalPlaces);
        Assert.Equal("Cent", company.DecimalUnitName);
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
        }
        catch (IOException) { /* best effort */ }
    }
}
