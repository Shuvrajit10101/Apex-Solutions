using System;
using System.IO;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;

namespace Apex.Desktop.Tests;

/// <summary>
/// <b>The Form-12BB screen must reject a sub-paisa figure BEFORE it touches the shared Company aggregate.</b>
///
/// <para><b>The defect this pins.</b> The "one rule, one home" slice replaced the SQLite store's nine payroll
/// tax-declaration writes — which had used a truncating <c>(long)(x * 100m)</c> cast — with the THROWING
/// <c>Paisa.FromDecimal</c>. That is the right call at a persistence boundary, but the front-line guard the rest
/// of the codebase pairs it with was never added here: <c>TaxDeclarationViewModel.TryMoney</c> accepted any
/// non-negative decimal, unlike <c>LedgerMasterViewModel</c>, <c>StockItemMasterViewModel</c>,
/// <c>BatchMasterViewModel</c>, <c>TdsStatPaymentViewModel</c> and ~15 other typed-amount paths, all of which
/// check <see cref="Money.IsPaisaExact"/>. <c>Money</c>'s own doc names this exact division of labour: a domain
/// boundary rejects the sub-paisa amount up front "instead of letting the paisa store raise a raw persistence
/// exception".</para>
///
/// <para><b>Why the consequence was worse than a bad error message — this is the part that bites.</b>
/// <c>Save()</c> calls <c>_company.AddTaxDeclaration(decl)</c> BEFORE <c>_storage.Save(_company)</c>, and neither
/// <c>TaxDeclaration</c> (plain settable data) nor <c>Company.AddTaxDeclaration</c> (remove-and-add) validates
/// anything. So the bad declaration entered the SHARED in-memory aggregate, the store then threw, the catch
/// reported the message and returned false — and the poisoned declaration STAYED. Every later save of that same
/// Company then threw from the store, including from the ~90 call sites that have no try/catch at all
/// (<c>BudgetMasterViewModel.Save</c>, <c>BankReconciliationViewModel</c>, …), so an unrelated screen crashed
/// with an unhandled <c>InvalidOperationException</c> long after the Form-12BB entry that caused it. Before the
/// slice the same keystroke merely truncated ₹0.005 away and everything kept working.</para>
///
/// <para>Fixture is ODD sub-paisa (₹1,500.555 into §80C) — a round figure is paisa-exact and asserts nothing.</para>
/// </summary>
public sealed class TaxDeclarationSubPaisaGuardTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public TaxDeclarationSubPaisaGuardTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexTaxDeclSubPaisa_" + Guid.NewGuid().ToString("N"));
        _storage = new CompanyStorage(_tempDir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch (IOException) { /* a held SQLite handle must not fail the test */ }
    }

    /// <summary>A payroll company with one employee, saved once so the baseline is known good.</summary>
    private (Company Company, Guid EmployeeId) NewPayrollCompany()
    {
        var company = CompanyFactory.CreateSeeded("SubPaisa Declaration Co", new DateOnly(2025, 4, 1));
        var payroll = new PayrollService(company);
        var group = payroll.CreateEmployeeGroup("Staff");
        var employee = payroll.CreateEmployee("Sanjay Kumar", group.Id, employeeNumber: "E-001");

        _storage.Save(company); // the baseline: this company saves cleanly
        return (company, employee.Id);
    }

    private TaxDeclarationViewModel NewScreen(Company company) =>
        new(company, _storage, onChanged: () => { });

    // ================================================================ the guard itself

    /// <summary>
    /// A sub-paisa §80C figure is refused at the screen, with a message naming the field — not surfaced as a raw
    /// persistence exception, and not silently truncated.
    /// </summary>
    [Fact]
    public void ASubPaisaDeclarationIsRefusedWithAFieldNamedMessage()
    {
        var (company, _) = NewPayrollCompany();
        var screen = NewScreen(company);
        screen.SelectedEmployee = screen.Employees.First();
        screen.Section80CText = "1500.555";

        Assert.False(screen.Save());
        Assert.NotNull(screen.Message);
        Assert.Contains("80C", screen.Message!, StringComparison.Ordinal);
        Assert.Contains("paisa", screen.Message!, StringComparison.OrdinalIgnoreCase);
    }

    // ================================================================ the assertion that actually bites

    /// <summary>
    /// <b>The rejected declaration never entered the aggregate.</b> This is the assertion that fails without the
    /// guard — the message test above passes either way, because the store's own throw is also caught and shown.
    /// A second, entirely unrelated save of the SAME Company instance must still succeed; without the front-line
    /// check the poisoned declaration is still in <c>_company</c> and this throws.
    /// </summary>
    [Fact]
    public void SubPaisaDeclarationIsRejectedAndNeverEntersTheCompany()
    {
        var (company, employeeId) = NewPayrollCompany();
        var screen = NewScreen(company);
        screen.SelectedEmployee = screen.Employees.First();
        screen.Section80CText = "1500.555";

        Assert.False(screen.Save());

        // 1. The aggregate is clean — nothing was written for this employee.
        Assert.Null(company.FindTaxDeclaration(employeeId));

        // 2. …and it STAYS usable: an unrelated save of the same instance still succeeds. This is the step that
        //    threw an unhandled InvalidOperationException before the guard, from a screen with no try/catch.
        var ex = Record.Exception(() => _storage.Save(company));
        Assert.Null(ex);
    }

    /// <summary>
    /// A user who corrects the figure and saves again ends up with the corrected declaration.
    ///
    /// <para><b>This one does NOT bite, and that is recorded rather than implied.</b> Under a mutation removing the
    /// guard it still passes, because <c>Company.AddTaxDeclaration</c> is remove-and-add keyed on the employee id:
    /// the corrected save REPLACES the poisoned declaration before the store sees it, so the retry path was never
    /// the broken one. The damage was to OTHER saves that happen between the rejection and a correction — which is
    /// what <see cref="SubPaisaDeclarationIsRejectedAndNeverEntersTheCompany"/> pins. This test is kept as a
    /// straightforward round-trip guard, not as evidence for the defect.</para>
    /// </summary>
    [Fact]
    public void CorrectingTheFigureAfterARejectionSavesCleanly()
    {
        var (company, employeeId) = NewPayrollCompany();
        var screen = NewScreen(company);
        screen.SelectedEmployee = screen.Employees.First();

        screen.Section80CText = "1500.555";
        Assert.False(screen.Save());

        screen.Section80CText = "1500.55";
        Assert.True(screen.Save());

        Assert.Equal(new Money(1500.55m), company.FindTaxDeclaration(employeeId)!.Section80C);
    }

    // ================================================================ the guard is not over-eager

    /// <summary>
    /// A paisa-exact declaration — including one with genuine odd paisa — still saves and round-trips. A guard
    /// that rejected everything would pass the tests above while breaking the screen.
    /// </summary>
    [Fact]
    public void APaisaExactDeclarationStillSavesAndRoundTrips()
    {
        var (company, employeeId) = NewPayrollCompany();
        var screen = NewScreen(company);
        screen.SelectedEmployee = screen.Employees.First();
        screen.Section80CText = "1500.57";   // odd paisa, exactly representable
        screen.Section80DText = "24999.99";

        Assert.True(screen.Save());

        var stored = company.FindTaxDeclaration(employeeId);
        Assert.NotNull(stored);
        Assert.Equal(new Money(1500.57m), stored!.Section80C);
        Assert.Equal(new Money(24999.99m), stored.Section80D);

        var reloaded = _storage.Load(_storage.ListCompanies().Single(e => e.Name == company.Name));
        var persisted = reloaded.FindTaxDeclaration(employeeId);
        Assert.NotNull(persisted);
        Assert.Equal(new Money(1500.57m), persisted!.Section80C);
        Assert.Equal(new Money(24999.99m), persisted.Section80D);
    }

    /// <summary>
    /// Every one of the nine declared-figure fields carries the guard, not just §80C — the store writes all nine
    /// through the throwing conversion, so a gap in any one of them reopens the same aggregate-poisoning path.
    /// </summary>
    [Theory]
    [InlineData("80C")]
    [InlineData("80D")]
    [InlineData("80CCD(1B)")]
    [InlineData("80CCD(2)")]
    [InlineData("HRA exemption")]
    [InlineData("§24(b) interest")]
    [InlineData("other income")]
    [InlineData("previous-employer salary")]
    [InlineData("previous-employer TDS")]
    public void EveryDeclaredFigureFieldRefusesASubPaisaAmount(string fieldLabel)
    {
        var (company, employeeId) = NewPayrollCompany();
        var screen = NewScreen(company);
        screen.SelectedEmployee = screen.Employees.First();

        const string SubPaisa = "1500.555";
        switch (fieldLabel)
        {
            case "80C": screen.Section80CText = SubPaisa; break;
            case "80D": screen.Section80DText = SubPaisa; break;
            case "80CCD(1B)": screen.Section80CCD1BText = SubPaisa; break;
            case "80CCD(2)": screen.Section80CCD2EmployerText = SubPaisa; break;
            case "HRA exemption": screen.HouseRentAllowanceExemptText = SubPaisa; break;
            case "§24(b) interest": screen.HomeLoanInterest24bText = SubPaisa; break;
            case "other income": screen.OtherIncomeText = SubPaisa; break;
            case "previous-employer salary": screen.PreviousEmployerSalaryText = SubPaisa; break;
            case "previous-employer TDS": screen.PreviousEmployerTdsText = SubPaisa; break;
            default: throw new InvalidOperationException($"unmapped field '{fieldLabel}'");
        }

        Assert.False(screen.Save());
        Assert.Contains(fieldLabel, screen.Message!, StringComparison.Ordinal);
        Assert.Null(company.FindTaxDeclaration(employeeId));
        Assert.Null(Record.Exception(() => _storage.Save(company)));
    }
}
