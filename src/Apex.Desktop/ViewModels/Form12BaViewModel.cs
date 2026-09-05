using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Apex.Desktop.ViewModels;

/// <summary>One selectable employee on the Form 12BA screen — the same set the Form 16 screen offers (the Annexure-II
/// set), because 12BA is that certificate's annexure and is issued to the same person for the same year.</summary>
public sealed partial class Form12BaEmployeeOptionVm : ViewModelBase
{
    public Guid EmployeeId { get; init; }
    public string Pan { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string GrossSalary { get; init; } = string.Empty;

    [ObservableProperty] private bool _isHighlighted;
}

/// <summary>
/// The <b>Form 12BA</b> (from FY 2026-27, <b>Form 123</b>) statement-of-perquisites screen — Reports → Statutory
/// Reports → Payroll → Form 12BA, immediately after the Form 16 row, because <b>this is Form 16's annexure</b> and is
/// issued to the same employee for the same year off the same data path (census row 6.42).
///
/// <para><b>🔴 THE CENTRAL FACT ABOUT THIS SCREEN, STATED BEFORE ANYTHING ELSE: THIS BOOK MAINTAINS NO PERQUISITE
/// RECORDS AT ALL, SO THE PERQUISITE TABLE IS ALWAYS EMPTY, AND THE SCREEN SAYS SO IN WORDS.</b> That is not a bug and
/// it is not a stub — it is the only honest rendering available. Measured across the whole tree: <c>perquisite</c>
/// appears exactly once in <c>src/</c> and it is a form-number label
/// (<c>StatuteVocabulary.cs</c>, <c>["12BA"] = "123"</c>); <see cref="Form24QAnnexureIIRow"/> — the record Form 16
/// Part B is built from — carries no perquisite field and no §17(2) split; <see cref="PayHeadType"/> has ten members
/// and none is a perquisite (<c>Reimbursements</c> is a payment TO the employee, not a taxable perquisite, and
/// mapping it would be inventing); and <see cref="PayrollLineCategory"/> cannot tell a perquisite from an allowance.
/// Of the prescribed perquisite natures we can populate <b>zero</b>. Capturing them is a schema change, which this
/// track has no budget for.</para>
///
/// <para><b>The screen therefore prints an empty state, NEVER a column of zeros.</b> A nil figure and an unmeasured
/// figure look identical on paper and one of them is a lie: an employer who signed a 12BA showing "0.00" against
/// eighteen perquisite natures would be certifying that none was provided. See <see cref="EmptyStateText"/>.</para>
///
/// <para><b>The one genuinely computed thing on this screen</b> is the Rule 26A(2)(b) applicability test: the form is
/// required only where salary paid or payable exceeds <b>₹1,50,000</b>, and the employee's gross salary is a figure
/// this book <i>does</i> hold. See <see cref="ThresholdText"/> and <see cref="IsFormDue"/>.</para>
///
/// <para><b>🔴 SOURCING — READ THIS BEFORE CHANGING ANY CAPTION ON THIS SCREEN.</b>
/// <list type="bullet">
/// <item><b>[V — the issuing authority's own document title]</b> The form is published by the department as
/// <i>"Form No. 123 (Earlier Form No. 12BA)"</i> (title of <c>incometaxindia.gov.in/documents/d/guest/fn-123</c>),
/// which independently confirms the <c>["12BA"] = "123"</c> mapping this screen's title uses.</item>
/// <item><b>[V-secondary — UNVERIFIED AGAINST THE PRIMARY FORM]</b> the three column captions in
/// <see cref="PrescribedColumns"/>, the fourth (computed) column, the rule reference <b>26A(2)(b)</b> and the
/// <b>₹1,50,000</b> threshold. <b>Retrieval of the official form PDF FAILED on every route attempted, twice, and
/// that failure is recorded here rather than papered over:</b> <c>incometaxindia.gov.in</c> returns HTTP 403 to
/// WebFetch for the Rules pages, the <c>/documents/d/guest/*</c> route serves a <b>file download</b> instead of a
/// page in the browser pane, the legacy <c>/forms/income-tax rules/*.pdf</c> slugs now 404, and
/// <c>/communications/notification/notification_15_2021.pdf</c> also 403s. These captions therefore come from the
/// department's own search-result text, not from the form itself.</item>
/// </list>
/// 🔴 <b>Consequently this screen deliberately does NOT enumerate the ~18 numbered perquisite natures.</b> Printing a
/// numbered statutory list we could not read from the statute is exactly the <c>SeedTdsTcsRates</c> mistake this
/// project has already had to strip out of shipped code — and since every row would be empty anyway, the list would
/// buy nothing but a false claim of fidelity.</para>
///
/// <para>Gated: only reachable when §192 salary TDS is enabled (<see cref="Company.SalaryTdsEnabled"/>), the same
/// gate the Form 16 row carries (ER-13). MVVM boundary: engine only, no Avalonia types (headlessly testable);
/// deterministic (no clock/RNG).</para>
/// </summary>
public sealed partial class Form12BaViewModel : ViewModelBase
{
    /// <summary>
    /// The Rule 26A(2)(b) salary threshold above which the statement is required, <b>in rupees</b>: ₹1,50,000.
    /// <b>[V-secondary]</b> — see the type remarks; the primary form could not be retrieved.
    /// </summary>
    public const decimal ThresholdRupees = 150_000m;

    /// <summary>
    /// The prescribed column captions of the perquisite table, as far as they could be sourced. <b>[V-secondary],
    /// and the fourth is a computed column, not a captured one.</b> They are rendered as table headers so an operator
    /// can see the shape of the statement even though this book can fill none of it.
    /// </summary>
    public static readonly string[] PrescribedColumns =
    {
        "Nature of perquisites (see rule 3)",
        "Value of perquisite as per rules (Rs.)",
        "Amount, if any, recovered from the employee (Rs.)",
        "Amount of perquisite chargeable to tax (Col. 3 − Col. 4) (Rs.)",
    };

    /// <summary>The empty state, in the operator's own words. <b>This sentence is the slice's honesty and must not be
    /// softened into "no perquisites were provided" — the book cannot know that.</b></summary>
    public const string EmptyStateText =
        "No perquisite records exist for this employee. Perquisite capture under section 17(2) is not maintained in " +
        "this book, so this statement cannot be completed here — the rows above are the prescribed shape, not a nil " +
        "return. Do not read an empty table as a declaration that no perquisite was provided.";

    private readonly Company _company;

    [ObservableProperty] private string _title = "Form 12BA — Statement of Perquisites";
    [ObservableProperty] private string _subtitle = string.Empty;

    // Employer (issuer) block — same source as Form 16's deductor block.
    [ObservableProperty] private string _employerTan = string.Empty;
    [ObservableProperty] private string _responsiblePerson = string.Empty;
    [ObservableProperty] private string _periodCaptionShort = "AY";
    [ObservableProperty] private string _periodValue = string.Empty;

    // Employee (recipient) block.
    [ObservableProperty] private string _employeeName = "—";
    [ObservableProperty] private string _employeePan = "—";
    [ObservableProperty] private string _employeeDesignation = "—";
    [ObservableProperty] private string _grossSalaryText = "0.00";

    /// <summary>True when this employee's gross salary exceeds the Rule 26A(2)(b) threshold, i.e. the statement is
    /// required at all. The <b>one</b> figure on this screen the book genuinely computes.</summary>
    [ObservableProperty] private bool _isFormDue;

    /// <summary>The threshold verdict in words, with the figure that decides it.</summary>
    [ObservableProperty] private string _thresholdText = string.Empty;

    [ObservableProperty] private bool _isEmpty = true;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private int _highlightedIndex = -1;

    private Form12BaFyOption? _selectedYear;
    private Form12BaEmployeeOptionVm? _selectedEmployee;

    /// <summary>The financial years the statement can be built for (the company FY + the two prior).</summary>
    public ObservableCollection<Form12BaFyOption> FinancialYears { get; } = new();

    /// <summary>The employees with §192 salary activity in the selected FY (the Annexure-II set).</summary>
    public ObservableCollection<Form12BaEmployeeOptionVm> Employees { get; } = new();

    /// <summary>The prescribed column captions, bindable. Always four; never populated with rows.</summary>
    public ObservableCollection<string> Columns { get; } = new(PrescribedColumns);

    /// <summary>The empty state, bindable (a compiled XAML binding cannot resolve a <c>const</c>).</summary>
    public string EmptyState => EmptyStateText;

    public Form12BaViewModel(Company company)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));

        var fyStart = company.FinancialYearStart.Year;
        for (var y = fyStart; y >= fyStart - 2; y--)
            FinancialYears.Add(new Form12BaFyOption { StartYear = y });

        _selectedYear = FinancialYears.FirstOrDefault();
        Rebuild();
    }

    /// <summary>The selected financial year; changing it rebuilds the employee list and the title's form number.</summary>
    public Form12BaFyOption? SelectedYear
    {
        get => _selectedYear;
        set { if (SetProperty(ref _selectedYear, value)) Rebuild(); }
    }

    /// <summary>The selected employee (the statement is issued to this person).</summary>
    public Form12BaEmployeeOptionVm? SelectedEmployee
    {
        get => _selectedEmployee;
        set
        {
            if (!SetProperty(ref _selectedEmployee, value)) return;
            Project();
            var idx = value is null ? -1 : Employees.IndexOf(value);
            if (HighlightedIndex != idx) HighlightedIndex = idx;
        }
    }

    /// <summary>The Form 16 the statement annexes (rebuilt on selection change). Null until an employee is picked.</summary>
    public Form16? Certificate { get; private set; }

    /// <summary>(Re)builds the employee list for the selected FY and re-projects the first employee's statement.</summary>
    public void Rebuild()
    {
        var fyStart = SelectedYear?.StartYear ?? _company.FinancialYearStart.Year;

        // The form number is FY-gated exactly as Form 16 and Form 24Q are: the department publishes the document as
        // "Form No. 123 (Earlier Form No. 12BA)", and the Miller cascade keeps the parent menu row visible beside
        // this heading, so an ungated heading would contradict the menu that opened it in a single glance.
        Title = $"Form {StatuteVocabulary.FormLabel("12BA", fyStart)} — Statement of Perquisites";
        PeriodCaptionShort = StatuteVocabulary.PeriodCaptionShort(fyStart);
        PeriodValue = StatuteVocabulary.PeriodLabel(fyStart);
        Subtitle = $"{_company.Name}  —  FY {fyStart}-{(fyStart + 1) % 100:00}  "
                 + $"({PeriodCaptionShort} {PeriodValue})  ·  annexure to Form {StatuteVocabulary.FormLabel("16", fyStart)}";

        var rows = Form24Q.BuildAnnexureII(_company, fyStart);

        Employees.Clear();
        foreach (var r in rows)
            Employees.Add(new Form12BaEmployeeOptionVm
            {
                EmployeeId = r.EmployeeId,
                Pan = string.IsNullOrEmpty(r.Pan) ? "PANNOTAVBL" : r.Pan!,
                Name = r.EmployeeName,
                GrossSalary = IndianFormat.AmountAlways(r.GrossSalary),
            });

        HighlightedIndex = Employees.Count > 0 ? 0 : -1;
        SelectedEmployee = Employees.FirstOrDefault();
        Project();
    }

    /// <summary>Moves the employee highlight (Up/Down within the page); wraps.</summary>
    public void MoveHighlight(int direction)
    {
        if (Employees.Count == 0) { HighlightedIndex = -1; return; }
        var i = HighlightedIndex < 0 ? (direction > 0 ? -1 : 0) : HighlightedIndex;
        HighlightedIndex = (i + direction + Employees.Count) % Employees.Count;
    }

    partial void OnHighlightedIndexChanged(int value)
    {
        for (var i = 0; i < Employees.Count; i++)
            Employees[i].IsHighlighted = i == value;
        if (value >= 0 && value < Employees.Count) SelectedEmployee = Employees[value];
    }

    private void Project()
    {
        var sel = SelectedEmployee;
        if (sel is null)
        {
            Certificate = null;
            EmployerTan = "—"; ResponsiblePerson = "—";
            EmployeeName = "—"; EmployeePan = "—"; EmployeeDesignation = "—";
            GrossSalaryText = IndianFormat.AmountAlways(Money.Zero);
            IsFormDue = false;
            ThresholdText = "No employee has section 192 salary activity this year — nothing to annexe.";
            IsEmpty = true;
            StatusText = ThresholdText;
            return;
        }

        var fyStart = SelectedYear?.StartYear ?? _company.FinancialYearStart.Year;
        var cert = Form16.Build(_company, sel.EmployeeId, fyStart);
        Certificate = cert;

        EmployerTan = string.IsNullOrEmpty(cert.Deductor.Tan) ? "—" : cert.Deductor.Tan;
        ResponsiblePerson = BuildResponsibleLine(cert.Deductor);
        EmployeeName = cert.EmployeeName;
        EmployeePan = string.IsNullOrWhiteSpace(cert.EmployeePan) ? "PANNOTAVBL" : cert.EmployeePan!;

        var emp = _company.FindEmployee(sel.EmployeeId);
        EmployeeDesignation = string.IsNullOrWhiteSpace(emp?.Designation) ? "—" : emp!.Designation!;

        var gross = cert.PartB?.GrossSalary ?? Money.Zero;
        GrossSalaryText = IndianFormat.AmountAlways(gross);

        // Rule 26A(2)(b): the statement is required where salary paid or payable EXCEEDS ₹1,50,000. Strictly
        // greater-than — a salary of exactly ₹1,50,000 does not cross it. [V-secondary; see the type remarks.]
        IsFormDue = gross.Amount > ThresholdRupees;
        ThresholdText = IsFormDue
            ? $"Salary ₹{GrossSalaryText} exceeds the ₹{IndianFormat.AmountAlways(new Money(ThresholdRupees))} " +
              "threshold of rule 26A(2)(b) — this statement is required for this employee."
            : $"Salary ₹{GrossSalaryText} does not exceed the ₹{IndianFormat.AmountAlways(new Money(ThresholdRupees))} " +
              "threshold of rule 26A(2)(b) — this statement is not required for this employee.";

        // There is nothing that could make this false today: no perquisite record exists anywhere in the engine.
        // It is a property rather than a literal so the day a perquisite store lands, one assignment lights the
        // table up and the empty state disappears on its own.
        IsEmpty = true;
        StatusText = ThresholdText + "  Perquisite values are not maintained in this book — see the note below.";
    }

    private static string BuildResponsibleLine(Form24QDeductor d)
    {
        var name = string.IsNullOrWhiteSpace(d.ResponsiblePersonName) ? "—" : d.ResponsiblePersonName!;
        var parts = new List<string> { name };
        if (!string.IsNullOrWhiteSpace(d.ResponsiblePersonDesignation)) parts.Add(d.ResponsiblePersonDesignation!);
        if (!string.IsNullOrWhiteSpace(d.ResponsiblePersonPan)) parts.Add("PAN " + d.ResponsiblePersonPan);
        return string.Join("  ·  ", parts);
    }
}

/// <summary>A selectable financial year on the Form 12BA screen (its 01-Apr start year + the "2025-26" label).</summary>
public sealed class Form12BaFyOption
{
    public int StartYear { get; init; }
    public string Label => $"{StartYear}-{(StartYear + 1) % 100:00}";
    public override string ToString() => Label;
}
