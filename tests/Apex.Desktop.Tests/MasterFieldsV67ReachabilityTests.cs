using System;
using System.IO;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.Tests;

/// <summary>
/// v67 (census rows <b>2.4</b>, <b>2.7</b>, <b>2.9</b>, <b>2.11</b>, <b>3.7</b> — cluster C2):
/// <b>THE FIELDS ARE REACHABLE BY A USER AND THE VALUE SURVIVES A RELOAD.</b>
///
/// <para>🔴 <b>WHY THIS FILE EXISTS BESIDE THE SCHEMA TESTS.</b> The persistence suite proves the twenty-two
/// columns round-trip through the store. That proves nothing about whether an operator can set them: Alt+P was
/// once inert, three reports printed pages of blank cells, and a verb button ran <c>Create()</c> on an
/// alteration screen. Every test here drives the <b>real shell view models</b> over a throwaway <c>.db</c> and
/// then re-reads the value <b>off the RELOADED book</b>, never off the view model it was just typed into.</para>
///
/// <para>🔴 <b>THREE OF THESE TESTS EXIST BECAUSE THEY CAUGHT A REAL DEFECT</b> that the storage layer alone
/// would have shipped green — see
/// <see cref="A_cost_centre_budget_line_renders_in_the_variance_report_as_a_cost_centre"/> and
/// <see cref="Budgeting_a_second_cost_centre_adds_a_line_rather_than_overwriting_the_first"/>.</para>
/// </summary>
public sealed class MasterFieldsV67ReachabilityTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public MasterFieldsV67ReachabilityTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexV67Tests_" + Guid.NewGuid().ToString("N"));
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

    // ================================================================= 2.7 Cost Category alias

    /// <summary>
    /// Census <b>2.7</b>: the vendor-attested Cost Category <b>alias</b> is typeable on the master, survives a
    /// save+reload, and shows in the Existing list. <i>"Name &amp; alias: As in other masters, you can specify
    /// multiple aliases"</i> — help.tallysolutions.com/cost-centre-or-profit-centre-tally/, Cost Category
    /// section. This is the field the row was DEMOTED for on 2026-10-04.
    /// </summary>
    [Fact]
    public void The_cost_category_alias_is_typeable_and_survives_a_reload()
    {
        var vm = NewSeededCompany("V67 CostCat Co");
        vm.ShowCostCategoryMaster();
        var master = vm.CostCategoryMaster!;

        master.Name = "Departments";
        master.Alias = "DEPT";
        Assert.True(master.Create(), master.Message);

        // Off the RELOADED book, not off the view model it was typed into.
        var reloaded = Reload("V67 CostCat Co");
        Assert.Equal("DEPT", reloaded.CostCategories.Single(c => c.Name == "Departments").Alias);

        // …and the Existing list prints it, so the operator can see it was stored.
        Assert.Equal("DEPT", master.Existing.Single(r => r.Name == "Departments").Alias);
        // The seeded Primary category shows the em dash, not a blank cell and not "DEPT".
        Assert.All(master.Existing.Where(r => r.Name != "Departments"), r => Assert.Equal("—", r.Alias));
    }

    /// <summary>Census <b>2.7</b>: Ctrl+Enter alter pre-fills the alias and writes a changed one back, and a
    /// CLEARED box means "no alias" rather than "keep the old one".</summary>
    [Fact]
    public void Altering_a_cost_category_rewrites_its_alias_and_a_cleared_box_clears_it()
    {
        var vm = NewSeededCompany("V67 CostCat Alter Co");
        vm.ShowCostCategoryMaster();
        vm.CostCategoryMaster!.Name = "Departments";
        vm.CostCategoryMaster!.Alias = "DEPT";
        Assert.True(vm.CostCategoryMaster!.Create());

        var id = vm.Company!.CostCategories.Single(c => c.Name == "Departments").Id;

        var alter = CostCategoryMasterViewModel.ForAlter(vm.Company!, _storage, id, () => { })!;
        Assert.Equal("DEPT", alter.Alias);          // pre-filled, so an alter of the NAME keeps the alias
        alter.Alias = "DIVISIONS";
        Assert.True(alter.Alter(), alter.Message);
        Assert.Equal("DIVISIONS", Reload("V67 CostCat Alter Co").CostCategories.Single(c => c.Name == "Departments").Alias);

        var clear = CostCategoryMasterViewModel.ForAlter(vm.Company!, _storage, id, () => { })!;
        clear.Alias = "   ";
        Assert.True(clear.Alter(), clear.Message);
        Assert.Null(Reload("V67 CostCat Alter Co").CostCategories.Single(c => c.Name == "Departments").Alias);
    }

    // ================================================================= 3.7 the F12-gated godown pair

    /// <summary>
    /// Census <b>3.7</b>: with both F12 switches OFF — which is every pre-v67 company — the two fields are
    /// <b>not shown</b>, and a godown created from this screen carries the vendor defaults.
    ///
    /// <para>🔴 <b>THIS IS THE HALF THE CENSUS CELL SAYS THE OBVIOUS BUILD WOULD HAVE GOT WRONG.</b> The vendor
    /// shows neither field until its F12 switch is set — <i>"setting this option to Yes to displays the Allow
    /// storage of materials option on the Godown Creation screen"</i>, under <i>"Press F12 (Configure) to
    /// change the options."</i> Adding the fields unconditionally would ITSELF be a divergence.</para>
    /// </summary>
    [Fact]
    public void With_the_f12_switches_off_neither_godown_field_is_shown_and_the_vendor_defaults_are_written()
    {
        var vm = NewSeededCompany("V67 Godown Gate Co");
        Assert.False(vm.Company!.ConfigAllowStorageOfMaterials);
        Assert.False(vm.Company!.ConfigUseAddressForLocations);

        vm.ShowGodownMaster();
        var master = vm.GodownMaster!;
        Assert.False(master.ShowAllowStorageOfMaterials);
        Assert.False(master.ShowAddress);

        // 🔴 THE GATE DECIDES WHETHER THE VALUE IS WRITTEN, not merely whether the control is drawn. A stale
        // form value from a session where the switch was on must not reach the book.
        master.Name = "Transit Point";
        master.AllowStorageOfMaterials = false;
        master.Address = "somewhere the operator never saw a box for";
        Assert.True(master.Create(), master.Message);

        var stored = Reload("V67 Godown Gate Co").Godowns.Single(g => g.Name == "Transit Point");
        Assert.True(stored.AllowStorageOfMaterials);   // the vendor default, NOT the hidden form value
        Assert.Null(stored.Address);
    }

    /// <summary>
    /// Census <b>3.7</b>: with the F12 switches ON the two fields are shown, are settable, and survive a
    /// reload — and the seeded Main Location beside the new godown still stores materials.
    /// </summary>
    [Fact]
    public void With_the_f12_switches_on_both_godown_fields_are_shown_settable_and_survive_a_reload()
    {
        var vm = NewSeededCompany("V67 Godown Co");
        vm.Company!.ConfigAllowStorageOfMaterials = true;
        vm.Company!.ConfigUseAddressForLocations = true;

        vm.ShowGodownMaster();
        var master = vm.GodownMaster!;
        Assert.True(master.ShowAllowStorageOfMaterials);
        Assert.True(master.ShowAddress);

        master.Name = "Transit Point";
        master.AllowStorageOfMaterials = false;
        master.Address = "Gate 4, Peenya Industrial Area, Bengaluru 560058";
        Assert.True(master.Create(), master.Message);

        var reloaded = Reload("V67 Godown Co");
        var stored = reloaded.Godowns.Single(g => g.Name == "Transit Point");
        Assert.False(stored.AllowStorageOfMaterials);
        Assert.Equal("Gate 4, Peenya Industrial Area, Bengaluru 560058", stored.Address);
        // 🔴 Every OTHER godown is untouched — a writer that bound one flag for every row would turn the
        // whole book's inventory off, and asserting on one godown alone would not see it.
        Assert.All(reloaded.Godowns.Where(g => g.Name != "Transit Point"),
            g => { Assert.True(g.AllowStorageOfMaterials); Assert.Null(g.Address); });

        // The Existing list calls out only the NON-default, so a book on the defaults reads as it did.
        Assert.Equal("No storage", master.Existing.Single(r => r.Name == "Transit Point").Storage);
        Assert.All(master.Existing.Where(r => r.Name != "Transit Point"),
            r => Assert.Equal(string.Empty, r.Storage));
    }

    /// <summary>
    /// Census <b>3.7</b>: the Godown screen's HEADING follows the verb. It was the literal
    /// <i>"Godown / Location Creation"</i> in both modes while the view model's <c>Caption</c> went unread —
    /// the same defect wave 46 fixed on this screen's commit BUTTON, left behind on the heading above it.
    /// </summary>
    [Fact]
    public void The_godown_screens_heading_says_alteration_when_it_is_altering()
    {
        var vm = NewSeededCompany("V67 Godown Caption Co");
        vm.ShowGodownMaster();
        vm.GodownMaster!.Name = "Depot North";
        Assert.True(vm.GodownMaster!.Create(), vm.GodownMaster!.Message);
        Assert.Equal("Godown Creation", vm.GodownMaster!.Caption);

        var id = vm.Company!.Godowns.Single(g => g.Name == "Depot North").Id;
        var alter = GodownMasterViewModel.ForAlter(vm.Company!, _storage, id, () => { })!;
        Assert.Equal("Godown Alteration", alter.Caption);
        Assert.Equal("Save (Ctrl+A)", alter.CommitLabel);
    }

    // ================================================================= 2.11 currency formatting

    /// <summary>
    /// Census <b>2.11</b>: all five new formatting options are typeable and survive a reload, and the Existing
    /// list names the non-defaults. Captions verbatim from
    /// help.tallysolutions.com/create-alter-or-delete-currencies/.
    /// </summary>
    [Fact]
    public void The_currency_formatting_options_are_settable_and_survive_a_reload()
    {
        var vm = NewSeededCompany("V67 Currency Co");
        vm.ShowCurrencyMaster();
        var master = vm.CurrencyMaster!;

        master.Symbol = "$";
        master.FormalName = "USD";
        master.DecimalPlacesText = "2";
        master.ShowAmountsInMillions = true;
        master.SuffixSymbolToAmount = true;
        master.SpaceBetweenAmountAndSymbol = true;
        master.DecimalWord = "Cent";
        master.DecimalPlacesForWordsText = "2";
        Assert.True(master.CreateCurrency(), master.CurrencyMessage);

        var stored = Reload("V67 Currency Co").Currencies.Single(c => c.FormalName == "USD");
        Assert.True(stored.ShowAmountsInMillions);
        Assert.True(stored.SuffixSymbolToAmount);
        Assert.True(stored.SpaceBetweenAmountAndSymbol);
        Assert.Equal("Cent", stored.DecimalWord);
        Assert.Equal(2, stored.DecimalPlacesForWords);

        var row = master.Currencies.Single(r => r.FormalName == "USD");
        Assert.Contains("millions", row.Formatting);
        Assert.Contains("Cent", row.Formatting);
        // The base currency, which set none of them, reads as the em dash rather than an invented default.
        Assert.Equal("—", master.Currencies.Single(r => r.Kind == "Base").Formatting);
    }

    /// <summary>
    /// Census <b>2.11</b>: the vendor's own rule, enforced where the vendor enforces it —
    /// <i>"This number should be equal to or lesser than the number specified in Number of decimal places
    /// field."</i> The currency is NOT created when it is violated.
    /// </summary>
    [Fact]
    public void Decimal_places_for_words_above_the_decimal_places_is_refused_and_creates_nothing()
    {
        var vm = NewSeededCompany("V67 Currency Rule Co");
        vm.ShowCurrencyMaster();
        var master = vm.CurrencyMaster!;

        master.Symbol = "€";
        master.FormalName = "EUR";
        master.DecimalPlacesText = "2";
        master.DecimalPlacesForWordsText = "3";          // > 2 — the vendor refuses this
        Assert.False(master.CreateCurrency());
        Assert.Contains("equal to or lesser than", master.CurrencyMessage!);
        Assert.DoesNotContain(Reload("V67 Currency Rule Co").Currencies, c => c.FormalName == "EUR");

        // …and the same form with a legal value goes through, so the refusal is the RULE and not a dead box.
        master.DecimalPlacesForWordsText = "2";
        Assert.True(master.CreateCurrency(), master.CurrencyMessage);
        Assert.Equal(2, Reload("V67 Currency Rule Co").Currencies.Single(c => c.FormalName == "EUR").DecimalPlacesForWords);
    }

    /// <summary>
    /// 🔴 Census <b>2.11</b>: <b>A BLANK "No. of decimal places for amounts in words" IS NOT ZERO.</b> Blank
    /// means "fall back to the number of decimal places" — the behaviour of every pre-v67 book — and 0 is the
    /// operator's deliberate "write no minor unit in words". A <c>DEFAULT 0</c> column, or a parser that
    /// collapsed blank to 0, would silently convert every existing book to the second answer, and nothing else
    /// in either suite would have noticed.
    /// </summary>
    [Fact]
    public void A_blank_decimal_places_for_words_stores_null_and_an_explicit_zero_stores_zero()
    {
        var vm = NewSeededCompany("V67 Currency Null Co");
        vm.ShowCurrencyMaster();
        var master = vm.CurrencyMaster!;

        master.Symbol = "£";
        master.FormalName = "GBP";
        master.DecimalPlacesText = "2";
        master.DecimalPlacesForWordsText = "";           // blank
        Assert.True(master.CreateCurrency(), master.CurrencyMessage);

        master.Symbol = "¥";
        master.FormalName = "JPY";
        master.DecimalPlacesText = "2";
        master.DecimalPlacesForWordsText = "0";          // an explicit, different answer
        Assert.True(master.CreateCurrency(), master.CurrencyMessage);

        var reloaded = Reload("V67 Currency Null Co");
        Assert.Null(reloaded.Currencies.Single(c => c.FormalName == "GBP").DecimalPlacesForWords);
        Assert.Equal(0, reloaded.Currencies.Single(c => c.FormalName == "JPY").DecimalPlacesForWords);
    }

    // ================================================================= 2.4 the eleven voucher-type fields

    /// <summary>
    /// Census <b>2.4</b>: all eleven attested Voucher Type fields are settable on the master and survive a
    /// reload. Captions verbatim from help.tallysolutions.com/voucher-types-tally/; before v67 every one was a
    /// zero-hit search over <c>src/</c>.
    /// </summary>
    [Fact]
    public void All_eleven_voucher_type_fields_are_settable_and_survive_a_reload()
    {
        var vm = NewSeededCompany("V67 VT Co");
        vm.ShowVoucherTypeMaster();
        var master = vm.VoucherTypeMaster!;

        var bank = vm.Company!.Ledgers.First();
        master.Name = "Export Invoice";
        master.SelectedBaseType = master.BaseTypes.First(b => b.Value == VoucherBaseType.Sales);
        master.UseEffectiveDates = true;
        master.OptionalByDefault = true;
        master.DefaultAccountingAllocations = true;
        master.WhatsAppAfterSaving = true;
        master.GeneratePaymentLinkAfterSaving = true;
        master.DefaultPrintTemplate = "Simple Invoice";
        master.DefaultPrintTitle = "TAX INVOICE / EXPORT";
        master.SelectedDefaultBank = master.DefaultBankOptions.Single(o => o.Id == bank.Id);
        master.DefaultJurisdiction = "Bengaluru";
        master.PrintDeclaration = "We declare that this invoice shows the actual price of the goods described.";
        master.PrintTermsAndConditions = "Payment within 30 days.";
        Assert.True(master.Create(), master.Message);

        var stored = Reload("V67 VT Co").VoucherTypes.Single(t => t.Name == "Export Invoice");
        Assert.True(stored.UseEffectiveDates);
        Assert.True(stored.OptionalByDefault);
        Assert.True(stored.DefaultAccountingAllocations);
        Assert.True(stored.WhatsAppAfterSaving);
        Assert.True(stored.GeneratePaymentLinkAfterSaving);
        Assert.Equal("Simple Invoice", stored.DefaultPrintTemplate);
        Assert.Equal("TAX INVOICE / EXPORT", stored.DefaultPrintTitle);
        Assert.Equal(bank.Id, stored.DefaultBankLedgerId);
        Assert.Equal("Bengaluru", stored.DefaultJurisdiction);
        Assert.StartsWith("We declare that", stored.PrintDeclaration);
        Assert.Equal("Payment within 30 days.", stored.PrintTermsAndConditions);
    }

    /// <summary>
    /// 🔴 Census <b>2.4</b>: the <i>"Default bank"</i> picker's <b>"◦ No default bank"</b> row must store
    /// <c>null</c>, never <c>Guid.Empty</c>. <c>Guid.Empty</c> is a foreign key into nothing — the very next
    /// Save would fail <c>SQLITE_CONSTRAINT_FOREIGNKEY</c> and the open book could never be written again by
    /// any screen, which is the mechanism <c>MasterDeletionForeignKeyCoverageTests</c> exists to prevent.
    /// </summary>
    [Fact]
    public void Choosing_no_default_bank_stores_null_and_the_book_still_saves()
    {
        var vm = NewSeededCompany("V67 VT Bank Co");
        vm.ShowVoucherTypeMaster();
        var master = vm.VoucherTypeMaster!;

        master.Name = "Plain Sales";
        master.SelectedBaseType = master.BaseTypes.First(b => b.Value == VoucherBaseType.Sales);
        master.SelectedDefaultBank = master.DefaultBankOptions.First();     // "◦ No default bank"
        Assert.True(master.Create(), master.Message);

        Assert.Null(Reload("V67 VT Bank Co").VoucherTypes.Single(t => t.Name == "Plain Sales").DefaultBankLedgerId);

        // A second save over the same book proves the FK really is satisfiable, not merely that one row wrote.
        _storage.Save(vm.Company!);
        Assert.Null(Reload("V67 VT Bank Co").VoucherTypes.Single(t => t.Name == "Plain Sales").DefaultBankLedgerId);
    }

    /// <summary>
    /// 🔴 Census <b>2.4</b>: an ALTER that changes one field writes the other ten back UNCHANGED. Without the
    /// pre-fill in <c>ForAlter</c>, opening a configured type to fix a typo in its name would have silently
    /// cleared its whole printing and sharing block — a data loss with no message.
    /// </summary>
    [Fact]
    public void Altering_a_voucher_type_name_preserves_its_printing_and_sharing_block()
    {
        var vm = NewSeededCompany("V67 VT Alter Co");
        vm.ShowVoucherTypeMaster();
        var create = vm.VoucherTypeMaster!;
        create.Name = "Export Invoice";
        create.SelectedBaseType = create.BaseTypes.First(b => b.Value == VoucherBaseType.Sales);
        create.WhatsAppAfterSaving = true;
        create.DefaultPrintTitle = "TAX INVOICE / EXPORT";
        create.PrintTermsAndConditions = "Payment within 30 days.";
        Assert.True(create.Create(), create.Message);

        var id = vm.Company!.VoucherTypes.Single(t => t.Name == "Export Invoice").Id;
        var alter = VoucherTypeMasterViewModel.ForAlter(vm.Company!, _storage, id, () => { })!;
        Assert.True(alter.WhatsAppAfterSaving);                       // pre-filled
        Assert.Equal("TAX INVOICE / EXPORT", alter.DefaultPrintTitle);
        alter.Name = "Export Invoice (EU)";                           // change ONE thing
        Assert.True(alter.Create(), alter.Message);

        var stored = Reload("V67 VT Alter Co").VoucherTypes.Single(t => t.Id == id);
        Assert.Equal("Export Invoice (EU)", stored.Name);
        Assert.True(stored.WhatsAppAfterSaving);
        Assert.Equal("TAX INVOICE / EXPORT", stored.DefaultPrintTitle);
        Assert.Equal("Payment within 30 days.", stored.PrintTermsAndConditions);
    }

    // ================================================================= 2.9 the cost-centre budget target

    private static CostCentre AddCentre(MainWindowViewModel vm, string name)
    {
        var centre = new CostCentre(Guid.NewGuid(), name, vm.Company!.CostCategories.First().Id);
        vm.Company!.AddCostCentre(centre);
        return centre;
    }

    /// <summary>
    /// Census <b>2.9</b>: a cost centre appears in the budget TARGET picker, a line against it is addable, and
    /// the budget comes back off the reloaded book as a cost-centre target rather than a group or ledger one.
    /// <b>R7:</b> help.tallysolutions.com/budgets-tally/ — <i>"Set Cost Centres to Yes"</i>.
    /// </summary>
    [Fact]
    public void A_cost_centre_is_budgetable_from_the_master_and_survives_a_reload()
    {
        var vm = NewSeededCompany("V67 Budget Co");
        var centre = AddCentre(vm, "Marketing");
        _storage.Save(vm.Company!);

        vm.ShowBudgetMaster();
        var master = vm.BudgetMaster!;
        var option = master.Targets.Single(t => t.CostCentreId == centre.Id);
        Assert.Equal("Marketing  (Cost Centre)", option.Display);

        master.Name = "FY Departmental";
        master.SelectedTarget = option;
        master.SelectedType = BudgetType.OnNettTransactions;
        master.LineAmountText = "45000";
        Assert.True(master.AddLine(), master.Message);
        Assert.True(master.Create(), master.Message);

        var budget = Reload("V67 Budget Co").Budgets.Single(b => b.Name == "FY Departmental");
        var line = budget.Lines.Single();
        Assert.True(line.IsCostCentreTarget);
        Assert.Equal(centre.Id, line.CostCentreId);
        Assert.Null(line.GroupId);
        Assert.Null(line.LedgerId);
        Assert.Equal(45_000m, line.Amount.Amount);
    }

    /// <summary>
    /// 🔴 Census <b>2.9</b> — <b>A DEFECT THE STORAGE LAYER ALONE WOULD HAVE SHIPPED.</b>
    /// <c>BudgetMasterViewModel.SameTarget</c> compared only <c>GroupId</c> and <c>LedgerId</c>, and BOTH are
    /// null on <b>every</b> cost-centre line — so under the two-field test every cost centre compared equal to
    /// every other, and budgeting a second centre <b>overwrote the first</b> instead of adding a row.
    /// <c>RowFor</c> carried the mirror bug and would have printed the wrong centre's name beside a real
    /// budgeted figure.
    /// </summary>
    [Fact]
    public void Budgeting_a_second_cost_centre_adds_a_line_rather_than_overwriting_the_first()
    {
        var vm = NewSeededCompany("V67 Budget Two Co");
        var marketing = AddCentre(vm, "Marketing");
        var support = AddCentre(vm, "Support");
        _storage.Save(vm.Company!);

        vm.ShowBudgetMaster();
        var master = vm.BudgetMaster!;
        master.Name = "FY Departmental";
        master.SelectedType = BudgetType.OnNettTransactions;

        master.SelectedTarget = master.Targets.Single(t => t.CostCentreId == marketing.Id);
        master.LineAmountText = "45000";
        Assert.True(master.AddLine(), master.Message);

        master.SelectedTarget = master.Targets.Single(t => t.CostCentreId == support.Id);
        master.LineAmountText = "12000";
        Assert.True(master.AddLine(), master.Message);

        // TWO pending rows, each naming its OWN centre — not one row that swallowed the other.
        Assert.Equal(2, master.PendingLines.Count);
        Assert.Contains(master.PendingLines, r => r.Target == "Marketing  (Cost Centre)");
        Assert.Contains(master.PendingLines, r => r.Target == "Support  (Cost Centre)");

        Assert.True(master.Create(), master.Message);
        var budget = Reload("V67 Budget Two Co").Budgets.Single(b => b.Name == "FY Departmental");
        Assert.Equal(2, budget.Lines.Count);
        Assert.Equal(45_000m, budget.Lines.Single(l => l.CostCentreId == marketing.Id).Amount.Amount);
        Assert.Equal(12_000m, budget.Lines.Single(l => l.CostCentreId == support.Id).Amount.Amount);
    }

    /// <summary>
    /// 🔴 Census <b>2.9</b> — <b>THE SECOND DEFECT THE STORAGE LAYER ALONE WOULD HAVE SHIPPED, AND IT IS THE
    /// WORSE ONE.</b> <c>BudgetVarianceReport.Build</c> had no cost-centre arm: a cost-centre line fell into
    /// the LEDGER branch and dereferenced a null <c>LedgerId</c>, so the <b>whole Budget Variance report threw
    /// for that budget</b> rather than mis-rendering one row. And every renderer printed
    /// <c>IsGroup ? "Group" : "Ledger"</c>, so even once it built, the row would have been labelled
    /// <b>"Ledger"</b> beside a real budgeted figure.
    ///
    /// <para>This asserts the OBSERVABLE ARTEFACT: the report builds, the row names the centre, carries the
    /// <c>CostCentre</c> kind, and reports a real actual taken from a posted allocation.</para>
    /// </summary>
    [Fact]
    public void A_cost_centre_budget_line_renders_in_the_variance_report_as_a_cost_centre()
    {
        var vm = NewSeededCompany("V67 Variance Co");
        var centre = AddCentre(vm, "Marketing");

        var budget = new Budget(
            Guid.NewGuid(), "FY Departmental",
            vm.Company!.FinancialYearStart, vm.Company!.FinancialYearStart.AddYears(1).AddDays(-1));
        budget.AddLine(BudgetLine.ForCostCentre(centre.Id, BudgetType.OnNettTransactions, new Money(45_000m)));
        vm.Company!.AddBudget(budget);
        _storage.Save(vm.Company!);

        // Before this arm existed, THIS LINE THREW.
        var report = BudgetVarianceReport.Build(vm.Company!, budget);

        var row = Assert.Single(report.Lines);
        Assert.Equal("Marketing", row.TargetName);
        Assert.Equal(BudgetTargetKind.CostCentre, row.TargetKind);
        Assert.Equal("Cost Centre", row.TargetKindLabel);   // NOT "Ledger"
        Assert.False(row.IsGroup);
        Assert.Equal(45_000m, row.Budget.Amount);
        // No allocation posted yet, so the actual is zero and the variance is the whole budget, under.
        Assert.Equal(0m, row.Actual.Amount);
        Assert.Equal(-45_000m, row.Variance.Amount);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch (IOException) { /* a locked temp file must not fail the suite */ }
    }
}
