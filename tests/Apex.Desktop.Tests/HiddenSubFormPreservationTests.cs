using System;
using System.IO;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.Tests;

/// <summary>
/// 🔴 THE HIDDEN-SUB-FORM RULE, locked per gated block on the Ledger master.
///
/// <para><b>The defect these lock (measured, not inferred).</b> <c>LedgerMasterViewModel.TryBuildInto</c> BUILT
/// each conditionally-rendered block inside an <c>if (Show…)</c> guard but WROTE it unconditionally. A sub-form
/// that never rendered captured nothing, so the write pushed a DEFAULT over data the operator could not see and
/// had no way to re-enter — silent destruction, no error, no prompt.</para>
///
/// <para>Measured by altering ONLY the address of a party carrying full GST details in a company whose GST flag
/// was off, with the guard removed: the stored <c>Gstin</c> came back <c>null</c>, <c>RegistrationType</c> came
/// back <c>Unregistered</c> and <c>IsBodyCorporate</c> came back <c>false</c>, while the State survived only
/// because the <c>MailingStateCode</c> write below re-materialised a FRESH block carrying just that. So the GSTIN
/// vanished from invoice printing (<c>VoucherPrintProjector.BuyerBlock</c> reads <c>party?.PartyGst?.Gstin</c>)
/// and the party silently flipped Regular → Unregistered, i.e. <b>B2B → B2C for GSTR-1 classification</b>.</para>
///
/// <para>The sweep found the same unconditional-assign shape on three more gated blocks. The
/// <b>Method of Appropriation</b> one is the widest-reaching of all: its gate includes
/// <c>ShowConfiguration</c>, the F12 toggle, which starts <b>false</b> on every freshly-opened screen — so every
/// alter of an additional-cost ledger wiped it unless the operator happened to press F12 first, with no unusual
/// company configuration required at all.</para>
/// </summary>
public sealed class HiddenSubFormPreservationTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public HiddenSubFormPreservationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexHiddenSubForm_" + Guid.NewGuid().ToString("N"));
        _storage = new CompanyStorage(_tempDir);
    }

    private MainWindowViewModel NewSeededCompany(string name)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = name;
        vm.CreateCompany();
        Assert.Equal(Screen.Gateway, vm.CurrentScreen);
        return vm;
    }

    private Company Reload(string companyName)
    {
        var entry = _storage.ListCompanies().Single(e => e.Name == companyName);
        return _storage.Load(entry);
    }

    /// <summary>
    /// A structurally valid party GSTIN for West Bengal (state code 19), with its Luhn-mod-36 check digit
    /// COMPUTED by the engine rather than hard-coded — a wrong literal would fail validation on save and turn
    /// these preservation tests into vacuous ones that never reach the assertion they exist for.
    /// </summary>
    private static readonly string PartyGstin = "19AAAAA0000A1Z" + Gstin.ComputeCheckDigit("19AAAAA0000A1Z0");

    private static void EnableGst(Company company) =>
        company.Gst = new GstConfig { Enabled = true, HomeStateCode = "19", RegistrationType = GstRegistrationType.Regular };

    /// <summary>
    /// Creates a Sundry-Debtor party carrying a FULL GST block — GSTIN, Regular registration, State 19, plus the
    /// <c>IsBodyCorporate</c> RCM qualifier that the master screen cannot show at all.
    /// </summary>
    private Guid CreateFullGstParty(MainWindowViewModel vm, string name)
    {
        vm.ShowLedgerMaster();
        var m = vm.LedgerMaster!;
        m.SelectedGroup = m.Groups.First(g => g.Name == "Sundry Debtors");
        m.Name = name;
        Assert.True(m.ShowPartyGst, "GST must be enabled for this setup to capture a party GST block.");
        m.PartyGstin = PartyGstin;
        m.PartyRegistrationType = m.PartyRegistrationTypes.First(t => t.Value == GstRegistrationType.Regular);
        m.PartyState = m.PartyStates.First(s => s.Code == "19");
        Assert.True(m.Create(), m.Message);

        var ledger = vm.Company!.FindLedgerByName(name)!;
        // Set the qualifier the screen has no field for — the whole point of the asymmetry.
        ledger.PartyGst!.IsBodyCorporate = true;
        _storage.Save(vm.Company!);
        return ledger.Id;
    }

    // ================================================================= (1) THE PRIORITY TEST — party GST

    /// <summary>
    /// 🔴 THE DEFECT-2 LOCK. A party carries full GST details; the company's GST flag is then switched OFF (F11
    /// toggled off after setup, or an Io import into a non-GST company). Altering ONLY the address — the WI-4
    /// happy path, with the GST fields not even on screen — must leave the ENTIRE GST block untouched.
    ///
    /// <para><b>This test bites.</b> Restoring the unconditional <c>target.PartyGst = partyGst;</c> in
    /// <c>TryBuildInto</c> fails it on the GSTIN assertion with <c>Actual: (null)</c> — verified by doing exactly
    /// that against a checksummed backup and restoring byte-exact, not assumed.</para>
    /// </summary>
    [Fact]
    public void Altering_a_party_while_GST_is_disabled_preserves_the_whole_GST_block()
    {
        const string companyName = "GST Off Alter Co";
        var vm = NewSeededCompany(companyName);
        EnableGst(vm.Company!);

        var id = CreateFullGstParty(vm, "Naresh Traders");

        // …and now GST goes OFF. The party's GST block is still stored; it is merely unreachable on screen.
        vm.Company!.Gst!.Enabled = false;
        _storage.Save(vm.Company!);
        Assert.False(vm.Company!.GstEnabled);

        vm.ShowLedgerAlter(id);
        var alter = vm.LedgerMaster!;
        Assert.False(alter.ShowPartyGst);          // the sub-form is genuinely hidden — that is the hazard
        Assert.True(alter.ShowMailingDetails);     // the address IS on screen, and is all the operator touches

        alter.MailingAddress = "12 Park Street\nKolkata";
        alter.MailingPincode = "700019";
        Assert.True(alter.Alter(), alter.Message);

        // The edit the operator MADE stuck…
        var saved = Reload(companyName).FindLedger(id)!;
        Assert.Equal("12 Park Street\nKolkata", saved.Mailing!.Address);
        Assert.Equal("700019", saved.Mailing!.Pincode);

        // …and every field they could NOT see survived it.
        Assert.NotNull(saved.PartyGst);
        Assert.Equal(PartyGstin, saved.PartyGst!.Gstin);
        Assert.Equal(GstRegistrationType.Regular, saved.PartyGst!.RegistrationType);
        Assert.Equal("19", saved.PartyGst!.StateCode);
        Assert.Equal("19", saved.MailingStateCode);
        Assert.True(saved.PartyGst!.IsBodyCorporate);
    }

    /// <summary>
    /// The control that isolates the bug to the GST-disabled branch: the SAME edit with GST enabled always
    /// preserved everything, which is why the defect went unnoticed.
    /// </summary>
    [Fact]
    public void Altering_a_party_while_GST_is_ENABLED_also_preserves_the_whole_GST_block()
    {
        const string companyName = "GST On Alter Co";
        var vm = NewSeededCompany(companyName);
        EnableGst(vm.Company!);

        var id = CreateFullGstParty(vm, "Naresh Traders");

        vm.ShowLedgerAlter(id);
        var alter = vm.LedgerMaster!;
        Assert.True(alter.ShowPartyGst);
        alter.MailingAddress = "12 Park Street\nKolkata";
        Assert.True(alter.Alter(), alter.Message);

        var saved = Reload(companyName).FindLedger(id)!;
        Assert.Equal(PartyGstin, saved.PartyGst!.Gstin);
        Assert.Equal(GstRegistrationType.Regular, saved.PartyGst!.RegistrationType);
        Assert.Equal("19", saved.PartyGst!.StateCode);
        Assert.True(saved.PartyGst!.IsBodyCorporate);
    }

    /// <summary>
    /// The residual asymmetry inside the GST branch itself: the screen cannot show <c>IsBodyCorporate</c>, so
    /// clearing the three fields it CAN show must not drop the block (and the qualifier with it).
    /// </summary>
    [Fact]
    public void Clearing_every_visible_GST_field_does_not_drop_an_invisible_RCM_qualifier()
    {
        const string companyName = "RCM Qualifier Co";
        var vm = NewSeededCompany(companyName);
        EnableGst(vm.Company!);

        var id = CreateFullGstParty(vm, "Naresh Traders");

        vm.ShowLedgerAlter(id);
        var alter = vm.LedgerMaster!;
        alter.PartyGstin = string.Empty;
        alter.PartyRegistrationType = alter.PartyRegistrationTypes.First(t => t.Value == GstRegistrationType.Unregistered);
        alter.PartyState = null;
        Assert.True(alter.Alter(), alter.Message);

        var saved = Reload(companyName).FindLedger(id)!;
        // The visible fields DID clear — the operator's edit is honoured…
        Assert.Null(saved.PartyGst?.Gstin);
        Assert.Null(saved.MailingStateCode);
        // …but the qualifier they never saw is still there.
        Assert.NotNull(saved.PartyGst);
        Assert.True(saved.PartyGst!.IsBodyCorporate);
    }

    // ================================================================= (2) THE SWEEP — the other gated blocks

    /// <summary>
    /// 🔴 THE WIDEST-REACHING ONE. "Method of Appropriation" renders only when the F12 configuration is on AND the
    /// ledger sits under Direct Expenses. <c>ShowConfiguration</c> starts <b>false</b> on every freshly-opened
    /// screen, so before the fix EVERY alter of an additional-cost ledger that did not press F12 first silently
    /// reset its method to null, demoting it to a plain expense that no longer loads onto purchase item lines.
    ///
    /// <para><b>This test bites.</b> Reverting the guard to a bare <c>target.MethodOfAppropriation = …;</c> fails
    /// it with <c>Actual: (null)</c>.</para>
    /// </summary>
    [Fact]
    public void Altering_an_additional_cost_ledger_without_opening_F12_preserves_its_appropriation_method()
    {
        const string companyName = "Appropriation Alter Co";
        var vm = NewSeededCompany(companyName);

        vm.ShowLedgerMaster();
        var create = vm.LedgerMaster!;
        create.SelectedGroup = create.Groups.First(g => g.Name == "Direct Expenses");
        create.Name = "Freight Inward";
        create.ShowConfiguration = true;                       // F12 — the field only exists once opened
        Assert.True(create.ShowAppropriation);
        create.SelectedMethod = create.MethodChoices.First(m => m.Value == MethodOfAppropriation.ByValue);
        Assert.True(create.Create(), create.Message);

        var id = vm.Company!.FindLedgerByName("Freight Inward")!.Id;
        Assert.Equal(MethodOfAppropriation.ByValue, vm.Company!.FindLedger(id)!.MethodOfAppropriation);

        // Re-open for alter and rename it — WITHOUT pressing F12, exactly as an operator would.
        vm.ShowLedgerAlter(id);
        var alter = vm.LedgerMaster!;
        Assert.False(alter.ShowConfiguration);                 // F12 closed — the field is NOT on screen
        Assert.False(alter.ShowAppropriation);
        alter.Name = "Freight Inward (Sea)";
        Assert.True(alter.Alter(), alter.Message);

        var saved = Reload(companyName).FindLedger(id)!;
        Assert.Equal("Freight Inward (Sea)", saved.Name);
        Assert.Equal(MethodOfAppropriation.ByValue, saved.MethodOfAppropriation);
    }

    /// <summary>
    /// The party default Price Level renders only when the F11 "multiple price levels" flag is on. Switching that
    /// flag off must not strip the default from every party the next time one is altered.
    ///
    /// <para><b>This test bites.</b> Reverting the guard to a bare <c>target.DefaultPriceLevelId = …;</c> fails it
    /// with <c>Actual: (null)</c>.</para>
    /// </summary>
    [Fact]
    public void Altering_a_party_while_price_levels_are_disabled_preserves_its_default_price_level()
    {
        const string companyName = "Price Level Alter Co";
        var vm = NewSeededCompany(companyName);

        vm.Company!.EnableMultiplePriceLevels = true;
        var level = new PriceLevel(Guid.NewGuid(), "Wholesale");
        vm.Company!.AddPriceLevel(level);
        _storage.Save(vm.Company!);

        vm.ShowLedgerMaster();
        var create = vm.LedgerMaster!;
        create.SelectedGroup = create.Groups.First(g => g.Name == "Sundry Debtors");
        create.Name = "Bright Traders";
        Assert.True(create.ShowDefaultPriceLevel);
        create.SelectedPriceLevel = create.PriceLevelChoices.First(p => p.PriceLevelId == level.Id);
        Assert.True(create.Create(), create.Message);

        var id = vm.Company!.FindLedgerByName("Bright Traders")!.Id;
        Assert.Equal(level.Id, vm.Company!.FindLedger(id)!.DefaultPriceLevelId);

        // The F11 flag goes off; the stored default is now unreachable on screen.
        vm.Company!.EnableMultiplePriceLevels = false;
        _storage.Save(vm.Company!);

        vm.ShowLedgerAlter(id);
        var alter = vm.LedgerMaster!;
        Assert.False(alter.ShowDefaultPriceLevel);
        alter.MailingAddress = "9 Camac Street";
        Assert.True(alter.Alter(), alter.Message);

        Assert.Equal(level.Id, Reload(companyName).FindLedger(id)!.DefaultPriceLevelId);
    }

    /// <summary>
    /// The Mailing block is gated on the group alone (it is not F11-gated), so it is the mildest of the four —
    /// but it shares the shape, and a non-party group's alter must not wipe an address either.
    ///
    /// <para><b>This test bites.</b> Reverting the guard to a bare <c>target.Mailing = mailing;</c> fails it with
    /// <c>Assert.NotNull() Failure: Value is null</c>.</para>
    /// </summary>
    [Fact]
    public void Regrouping_a_party_out_of_the_party_groups_preserves_its_stored_mailing_block()
    {
        const string companyName = "Mailing Regroup Co";
        var vm = NewSeededCompany(companyName);

        vm.ShowLedgerMaster();
        var create = vm.LedgerMaster!;
        create.SelectedGroup = create.Groups.First(g => g.Name == "Sundry Debtors");
        create.Name = "Robert Transport";
        create.MailingAddress = "4 Strand Road\nKolkata";
        create.MailingPincode = "700001";
        Assert.True(create.Create(), create.Message);

        var id = vm.Company!.FindLedgerByName("Robert Transport")!.Id;
        Assert.Equal("700001", vm.Company!.FindLedger(id)!.Mailing!.Pincode);

        vm.ShowLedgerAlter(id);
        var alter = vm.LedgerMaster!;
        alter.SelectedGroup = alter.Groups.First(g => g.Name == "Indirect Expenses");
        Assert.False(alter.ShowMailingDetails);                // the block is off screen for a non-party group
        Assert.True(alter.Alter(), alter.Message);

        var saved = Reload(companyName).FindLedger(id)!;
        Assert.NotNull(saved.Mailing);
        Assert.Equal("4 Strand Road\nKolkata", saved.Mailing!.Address);
        Assert.Equal("700001", saved.Mailing!.Pincode);
    }

    // ================================================================= (3) CREATE IS UNCHANGED (ER-13)

    /// <summary>
    /// The guards must not change CREATE: a brand-new ledger already has every gated field at its default, so
    /// skipping the write is byte-identical to writing the default. A plain non-party ledger created in a
    /// non-GST company must still come out completely bare.
    /// </summary>
    [Fact]
    public void Creating_a_plain_ledger_still_writes_no_gated_block_at_all()
    {
        const string companyName = "Bare Create Co";
        var vm = NewSeededCompany(companyName);

        vm.ShowLedgerMaster();
        var create = vm.LedgerMaster!;
        create.SelectedGroup = create.Groups.First(g => g.Name == "Indirect Expenses");
        create.Name = "Office Rent";
        Assert.True(create.Create(), create.Message);

        var saved = Reload(companyName).FindLedgerByName("Office Rent")!;
        Assert.Null(saved.PartyGst);
        Assert.Null(saved.Mailing);
        Assert.Null(saved.MailingStateCode);
        Assert.Null(saved.MethodOfAppropriation);
        Assert.Null(saved.DefaultPriceLevelId);
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
        }
        catch { /* best effort */ }
    }
}
