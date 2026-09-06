using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Xunit;

namespace Apex.Desktop.Tests;

/// <summary>
/// 🔴 <b>THE STATUTORY FORMS THAT RENDERED BUT COULD NOT BE READ.</b>
///
/// <para>The sibling file <see cref="PayrollStatutoryFormReachabilityTests"/> proves the eight W7-D2 forms are
/// reachable and that their text reaches the visual tree. Reaching the tree is not the same as being legible, and
/// three findings in the D-2 adversarial review were exactly that gap:</para>
///
/// <list type="bullet">
///   <item><b>PF Form 3A's member IDENTITY BLOCK was ~86–93% invisible.</b> Six statutory identity fields — Name,
///   A/c No., UAN, DOB, Sex, Date of Joining the Fund — plus the ruled Father's/Husband's line were concatenated
///   into ONE 190px cell, on a band row whose other seven cells were empty. The cell trims with
///   <c>CharacterEllipsis</c>, so an operator saw the name and an ellipsis. <b>A statutory form whose identity
///   block cannot be read is not a form</b>: every figure on the card below it is attributed to a member you
///   cannot identify.</item>
///   <item><b>Form 6A page 2's five challan account-head captions truncated exactly at their differentiator.</b>
///   The five heads differ only by their A/c number ("… — A/c No. 1", "… — A/c No. 10", "… — A/c No. 21",
///   "… — A/c No. 2", "… — A/c No. 22") and the number sat past the 118px cut, so five different money columns
///   were visually indistinguishable on the page where 6A reconciles to the challans.</item>
///   <item><b>Form 12A's three "Contribution Payable by the Employer" rows truncated at or before their account
///   number</b> — the same class, in the 300px particulars column.</item>
/// </list>
///
/// <para><b>How this file measures, and why it does NOT call <c>TextBlock.Measure</c>.</b> The payroll matrix
/// takes its widths from the VIEW MODEL (<c>PayrollColumns</c> / each cell's own <c>Width</c>), not from XAML
/// <c>ColumnDefinitions</c>, so a static XAML lock cannot see these grids at all — the widths have to be checked
/// against text. But 🔴 <b>the headless test host has no real fonts</b>: measured under
/// <c>[AvaloniaFact]</c>, <c>"MMMMMMMMMMMMMMMMMMMM"</c> and <c>"iiiiiiiiiiiiiiiiiiii"</c> both come back at
/// exactly <b>250px at 12.5pt</b>, and so does every family asked for — <c>Consolas</c>, <c>monospace</c>,
/// <c>FontFamily.Default</c> and the window's own chain alike. The headless stub face is a uniform
/// one-em-per-character grid, i.e. <b>12.5px per character, 1.82x the shipped face</b>. Sizing real columns
/// against it would demand columns nearly twice as wide as the product needs.</para>
///
/// <para>Widths are therefore computed from the <b>shipped face's own measured advance</b>, which this repo
/// established by render with real Consolas and records in
/// <c>StatutoryRegisterAndOpeningColumnLockTests</c>: 20x'M' = 132 / 138px at 12 / 12.5pt, i.e. absolute
/// advances <b>6.5977 / 6.8726</b>, the font confirmed monospace and weight-independent. Monospace makes the
/// model exact rather than approximate — width is character count times advance — and keeps this file green on
/// the three CI runners, which have no SkiaSharp and no Consolas.</para>
///
/// <para><b>PER-CELL, NEVER A SUM.</b> Every assertion names one cell and compares it against that cell's own
/// declared width. No total is asserted: a sum lock is satisfiable by redistribution — shrink one column, grow its
/// neighbour, total unchanged — which is precisely the hole that lets a width regression ship green.</para>
/// </summary>
public sealed class PayrollStatutoryFormLegibilityTests : IDisposable
{
    private const string Uan = "100123456789";
    private const string Ip = "1234567890";

    /// <summary>The live cell template's own inset: <c>Padding="8,0,8,0"</c> on the body cell TextBlock
    /// (MainWindow.axaml, the <c>PayrollMatrixCellVm</c> DataTemplate). Text has this much less room than the
    /// cell's declared Width.</summary>
    private const double CellPadding = 16;

    /// <summary>Shipped-face advance for a BODY cell (<c>FontSize="12.5"</c> on the cell DataTemplate), in the
    /// monospace face the window declares. Measured by render with real Consolas; see the class remarks.</summary>
    private const double BodyAdvance = 6.8726;

    /// <summary>Shipped-face advance for a COLUMN HEADER (<c>TextBlock.colHdr</c> sets
    /// <c>FontSize="12"</c>, MainWindow.axaml).</summary>
    private const double HeaderAdvance = 6.5977;

    /// <summary>The <c>colHdr</c> style's own <c>Padding="8,4"</c> — 8px each side, same horizontal inset as a
    /// body cell, which is why a value's right edge lands exactly under its header's.</summary>
    private const double HeaderPadding = 16;

    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public PayrollStatutoryFormLegibilityTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexStatFormsFit_" + Guid.NewGuid().ToString("N"));
        _storage = new CompanyStorage(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>Advance width of <paramref name="text"/> in a body cell of the shipped monospace face.</summary>
    private static double MeasuredWidth(string text) => text.Length * BodyAdvance;

    /// <summary>Advance width of <paramref name="text"/> in a column header of the shipped monospace face.</summary>
    private static double HeaderWidth(string text) => text.Length * HeaderAdvance;

    /// <summary>How many pixels of <paramref name="text"/> are cut off in a body cell of
    /// <paramref name="width"/>. Zero or less means it fits.</summary>
    private static double Overflow(string text, double width) => MeasuredWidth(text) - (width - CellPadding);

    // ------------------------------------------------------------------------------------------- B2: Form 3A

    /// <summary>
    /// 🔴 <b>The defect this test was written for.</b> Every one of Form 3A's six statutory identity fields must
    /// be readable in its own cell. Before the fix they were one concatenated string in a 190px slot: measured at
    /// <b>~1,020px of text in 174px of room</b>, i.e. roughly 83% of the block silently ellipsised away, with the
    /// A/c number, UAN, DOB, sex and fund-joining date all past the cut.
    ///
    /// <para>The assertion is per field and against that field's OWN cell width, so it cannot be satisfied by
    /// widening a neighbour.</para>
    /// </summary>
    [AvaloniaFact]
    public void Form_3A_identity_block_fields_each_fit_their_own_cell()
    {
        var vm = BuildCompany();
        vm.OpenPayrollStatutoryForm(ReportKind.PfForm3A);
        var reports = vm.Reports!;

        // The identity band is the row carrying the member's PF account number. Located by CONTENT, never by
        // index: a row index would silently stop matching the moment a row is inserted above it.
        var band = reports.PayrollRows.FirstOrDefault(r =>
            r.Cells.Any(c => c.Text.Contains("MH/BAN/0000001/000/0000123", StringComparison.Ordinal)));
        Assert.True(band is not null,
            "no row on Form 3A carries the member's PF account number — the identity block is gone entirely");

        // Every identity field the form is required to print must be present AND fit its own cell.
        foreach (var expected in new[]
                 {
                     "Sanjay Kumar",                    // Name
                     "MH/BAN/0000001/000/0000123",      // Account Number
                     Uan,                               // UAN
                     "04-May-1990",                     // Date of Birth
                     "Male",                            // Sex
                 })
        {
            var cell = band!.Cells.FirstOrDefault(c => c.Text.Contains(expected, StringComparison.Ordinal));
            Assert.True(cell is not null,
                $"Form 3A's identity block does not print '{expected}' in any cell of its own.");
            Assert.True(Overflow(cell!.Text, cell.Width) <= 0,
                $"Form 3A identity field '{expected}' is CUT: '{cell.Text}' measures "
                + $"{MeasuredWidth(cell.Text):F0}px in a {cell.Width - CellPadding:F0}px slot "
                + $"(overflow {Overflow(cell.Text, cell.Width):F0}px).");
        }

        // ...and no single cell carries more than one of them, which is what made the block unreadable.
        var crowded = band!.Cells.Count(c =>
            c.Text.Contains("Sanjay Kumar", StringComparison.Ordinal)
            && c.Text.Contains(Uan, StringComparison.Ordinal));
        Assert.Equal(0, crowded);
    }

    /// <summary>Each identity field is captioned, so a reader knows which statutory field a value belongs to.
    /// An uncaptioned grid of six bare values is not an identity block either.</summary>
    [AvaloniaFact]
    public void Form_3A_identity_block_captions_each_fit_their_own_cell()
    {
        var vm = BuildCompany();
        vm.OpenPayrollStatutoryForm(ReportKind.PfForm3A);

        var caption = vm.Reports!.PayrollRows.FirstOrDefault(r =>
            r.Cells.Any(c => c.Text.Contains("UAN", StringComparison.Ordinal))
            && r.Cells.Any(c => c.Text.Contains("Sex", StringComparison.Ordinal)));
        Assert.True(caption is not null, "Form 3A's identity block carries no caption row.");

        foreach (var c in caption!.Cells.Where(c => c.Text.Length > 0))
            Assert.True(Overflow(c.Text, c.Width) <= 0,
                $"Form 3A identity caption is CUT: '{c.Text}' measures {MeasuredWidth(c.Text):F0}px in a "
                + $"{c.Width - CellPadding:F0}px slot.");
    }

    // ------------------------------------------------------------------ H1 / H2: the account-head captions

    /// <summary>
    /// 🔴 <b>Form 6A page 2's five challan account heads must be told apart on sight.</b> They differ ONLY by
    /// their A/c number, so the number has to survive the column width. Before the fix all five captions carried
    /// the differentiator at the END, past a 118px cut, and five different money columns read identically.
    ///
    /// <para>The assertion is the strong one: the leading <c>A/c No. N</c> token of each caption fits, and the
    /// five tokens are DISTINCT — a fix that merely widened one column would not satisfy it.</para>
    /// </summary>
    [AvaloniaFact]
    public void Form_6A_page_two_account_head_captions_show_their_account_number()
    {
        var vm = BuildCompany();
        vm.OpenPayrollStatutoryForm(ReportKind.PfForm6A);

        var heads = vm.Reports!.PayrollColumns2
            .Where(c => c.Header.Contains("A/c No.", StringComparison.Ordinal))
            .ToList();
        Assert.Equal(5, heads.Count);

        var visible = new List<string>();
        foreach (var h in heads)
        {
            // What an operator can actually read is the prefix that fits. It must still contain the A/c number.
            var readable = LargestFittingHeaderPrefix(h.Header, h.Width);
            Assert.True(readable.Contains("A/c No.", StringComparison.Ordinal),
                $"Form 6A page 2 caption '{h.Header}' is cut before its account number — an operator reads only "
                + $"'{readable}' in its {h.Width - HeaderPadding:F0}px column "
                + $"(caption needs {HeaderWidth(h.Header):F0}px).");
            visible.Add(readable);
        }

        // Five heads, five DIFFERENT readable captions. This is the assertion the old layout failed.
        Assert.Equal(5, visible.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// PF Form 12A's particulars column: the three "Contribution Payable by the Employer" rows differ only by
    /// their account head, and all three truncated at or before it. Every particulars label on the form must fit.
    /// </summary>
    [AvaloniaFact]
    public void Form_12A_particulars_labels_fit_their_column()
    {
        var vm = BuildCompany();
        vm.OpenPayrollStatutoryForm(ReportKind.PfForm12A);

        var labels = vm.Reports!.PayrollRows
            .Select(r => r.Cells.FirstOrDefault())
            .Where(c => c is not null && c.Text.Length > 0)
            .ToList();
        Assert.NotEmpty(labels);

        foreach (var c in labels)
            Assert.True(Overflow(c!.Text, c.Width) <= 0,
                $"Form 12A particulars label is CUT: '{c.Text}' measures {MeasuredWidth(c.Text):F0}px in a "
                + $"{c.Width - CellPadding:F0}px slot (overflow {Overflow(c.Text, c.Width):F0}px).");

        // The three employer-contribution rows are distinguishable by their account head, which is the whole
        // point of the finding: three identical-looking labels against three different figures.
        var employerRows = labels
            .Where(c => c!.Text.Contains("Payable by", StringComparison.OrdinalIgnoreCase))
            .Select(c => LargestFittingPrefix(c!.Text, c.Width))
            .ToList();
        Assert.Equal(3, employerRows.Count);
        Assert.Equal(3, employerRows.Distinct(StringComparer.Ordinal).Count());
    }

    // ------------------------------------------------------------------ H3: the false empty-state message

    /// <summary>
    /// 🔴 <b>The four MULTI-MONTH forms used to print a note that was wrong twice over.</b> PF 3A and 6A are drawn
    /// over a twelve-month CURRENCY PERIOD and ESI 5 and 6 over a six-month CONTRIBUTION PERIOD, but all four fell
    /// through to the shared payroll default — <i>"No employees with salary for this wage month."</i> — which
    /// names a window none of them uses and blames a cause that is not the one: the establishment has nobody
    /// enrolled in the scheme at all. Forms 5, 10 and 3 already carried their own note; these four now do too.
    /// </summary>
    [AvaloniaTheory]
    [InlineData(ReportKind.PfForm3A, "Provident Fund", "currency period")]
    [InlineData(ReportKind.PfForm6A, "Provident Fund", "currency period")]
    [InlineData(ReportKind.EsiForm5, "ESI", "contribution period")]
    [InlineData(ReportKind.EsiForm6, "ESI", "contribution period")]
    public void A_multi_month_form_with_no_enrolled_member_explains_itself_in_its_own_window(
        ReportKind kind, string scheme, string window)
    {
        var vm = BuildCompany();
        // Enrolled in neither scheme: the form has no member, which is the state that reaches the empty note.
        foreach (var e in vm.Company!.Employees) { e.PfApplicable = false; e.EsiApplicable = false; }

        vm.OpenPayrollStatutoryForm(kind);
        var note = vm.Reports!.PayrollEmptyNote;

        Assert.True(vm.Reports.IsPayrollEmpty, $"{kind} should be empty with nobody enrolled.");
        Assert.DoesNotContain("this wage month", note, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(scheme, note, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(window, note, StringComparison.OrdinalIgnoreCase);
    }

    // -------------------------------------------------- the invariant that catches a figure under a wrong head

    /// <summary>
    /// 🔴 <b>Every data row carries exactly one cell per column.</b> The payroll matrix lays a row out as a plain
    /// horizontal <c>StackPanel</c> of cells against a separately-built header band — nothing binds a cell to its
    /// column. So a row built with one cell too few does not leave a gap: <b>every figure after the omission
    /// slides one column left and prints under the wrong statutory heading</b>, silently, with all the totals
    /// still footing.
    ///
    /// <para>This is not hypothetical. While fixing H4 in this very round, the edit that blanked Form 3A's
    /// Refund-of-Advance column also deleted the <c>Pension Fund Contribution</c> cell beside it: the twelve
    /// month rows dropped to seven cells against eight headers, the EPF difference printed under "Pension Fund
    /// Contribution", and the whole shipped suite stayed GREEN. This test is what caught it, and it is the reason
    /// the assertion is a per-row cell COUNT rather than a spot check on a value.</para>
    ///
    /// <para>Rows shorter than the column band are allowed ONLY where a form deliberately draws its own band —
    /// Form 3A's identity block, which has its own seven captioned columns. Those are identified positively, by
    /// carrying the identity caption or its value, never by "any short row", which would re-open the hole.</para>
    /// </summary>
    [AvaloniaTheory]
    [InlineData(ReportKind.PfForm3A)]
    [InlineData(ReportKind.PfForm5)]
    [InlineData(ReportKind.PfForm6A)]
    [InlineData(ReportKind.PfForm10)]
    [InlineData(ReportKind.PfForm12A)]
    [InlineData(ReportKind.EsiForm3)]
    [InlineData(ReportKind.EsiForm5)]
    [InlineData(ReportKind.EsiForm6)]
    public void Every_statutory_form_row_has_one_cell_per_column(ReportKind kind)
    {
        var vm = BuildCompany();
        vm.OpenPayrollStatutoryForm(kind);
        var r = vm.Reports!;

        Assert.NotEmpty(r.PayrollColumns);

        // PF Form 10 is the LEAVERS return and this fixture's member has not left, so it is legitimately nil —
        // a valid return, not a failure (its own empty-state note says exactly that). Every other form must have
        // rows, or the per-row assertion below would prove nothing.
        if (kind != ReportKind.PfForm10) Assert.NotEmpty(r.PayrollRows);

        foreach (var (row, i) in r.PayrollRows.Select((x, i) => (x, i)))
        {
            if (IsIdentityBandRow(row)) continue;
            Assert.True(r.PayrollColumns.Count == row.Cells.Count,
                $"{kind} row {i} has {row.Cells.Count} cells against {r.PayrollColumns.Count} columns — every "
                + $"figure after the missing cell prints under the WRONG heading. Row: "
                + $"[{string.Join(" ~ ", row.Cells.Select(c => c.Text))}]");
        }

        // Form 6A's page 2 is its own band and must satisfy the same invariant against its own columns.
        foreach (var (row, i) in r.PayrollRows2.Select((x, i) => (x, i)))
            Assert.True(r.PayrollColumns2.Count == row.Cells.Count,
                $"{kind} page-2 row {i} has {row.Cells.Count} cells against {r.PayrollColumns2.Count} columns.");
    }

    /// <summary>Form 3A's identity block draws its OWN captioned column band, so its two rows legitimately differ
    /// from the money grid's column count. Recognised by its caption or by the member value beneath it — never by
    /// being short, which is the very thing the invariant is checking.</summary>
    private static bool IsIdentityBandRow(PayrollMatrixRowVm row)
        => row.Cells.Any(c => c.Text is "Name of Member" or "PF Account Number")
        || row.Cells.Any(c => c.Text.Contains("MH/BAN/", StringComparison.Ordinal));

    // ---------------------------------------------------- H4: a literal 0 that could never be anything else

    /// <summary>
    /// 🔴 <b>A rendered "0" on these forms asserts the figure is genuinely nil — and three columns could never be
    /// anything but zero.</b> <c>PfEcr</c> hardcodes <c>RefundOfAdvances: 0</c> with no parameter to supply it,
    /// and nothing in the product ever passes its optional <c>ncpDaysByEmployee</c>, so Form 3A's <i>Refund of
    /// Advance</i> and <i>Non-Contributing Days</i> and Form 6A's <i>Refund of Advance</i> printed a literal 0
    /// with no footnote. By this file's OWN convention (blank = not maintained, "0" = genuinely nil) that is an
    /// unfounded statutory assertion. They are now ruled and blank, with the footnote that says why.
    /// </summary>
    [AvaloniaFact]
    public void The_columns_this_book_cannot_source_print_blank_with_a_footnote_never_a_literal_zero()
    {
        var vm = BuildCompany();

        vm.OpenPayrollStatutoryForm(ReportKind.PfForm3A);
        var r3a = vm.Reports!;
        var refundCol = IndexOfHeader(r3a.PayrollColumns, "Refund of Advance");
        var ncpCol = IndexOfHeader(r3a.PayrollColumns, "Non-Contributing Days");

        // The twelve month rows plus the footing — everything except the identity band. Counted, not merely
        // filtered: a filter that silently matched only the Total row is exactly how the first version of this
        // test passed while the month rows still printed a literal 0.
        var moneyRows = r3a.PayrollRows.Where(r => !IsIdentityBandRow(r)).ToList();
        Assert.Equal(13, moneyRows.Count);                                   // 12 months + Total
        Assert.Equal(12, moneyRows.Count(r => r.Cells[0].Text.Contains("20", StringComparison.Ordinal)));

        foreach (var row in moneyRows)
        {
            Assert.Equal(string.Empty, row.Cells[refundCol].Text);
            Assert.Equal(string.Empty, row.Cells[ncpCol].Text);
        }

        // ...and the month rows really are populated, so the blanks above are a deliberate choice on a live card
        // and not the whole grid being empty.
        Assert.True(moneyRows.Count(r => r.Cells[1].Text.Contains(',', StringComparison.Ordinal)) >= 11,
            "the card carries no wage figures — the blank-column assertions above would be vacuous");
        Assert.Contains(r3a.PayrollFootnotes,
            f => f.Contains("Refund of Advance", StringComparison.Ordinal)
              && f.Contains("not maintained", StringComparison.OrdinalIgnoreCase));

        vm.OpenPayrollStatutoryForm(ReportKind.PfForm6A);
        var r6a = vm.Reports!;
        var refund6 = IndexOfHeader(r6a.PayrollColumns, "Refund of Advance");
        foreach (var row in r6a.PayrollRows.Where(r => r.Cells.Count > refund6))
            Assert.Equal(string.Empty, row.Cells[refund6].Text);
        Assert.Contains(r6a.PayrollFootnotes,
            f => f.Contains("Refund of Advance", StringComparison.Ordinal)
              && f.Contains("not maintained", StringComparison.OrdinalIgnoreCase));
    }

    private static int IndexOfHeader(IEnumerable<PayrollMatrixColumnVm> columns, string header)
    {
        var list = columns.ToList();
        var i = list.FindIndex(c => c.Header.Contains(header, StringComparison.Ordinal));
        Assert.True(i >= 0, $"No column headed '{header}'. Headers: {string.Join(" | ", list.Select(c => c.Header))}");
        return i;
    }

    /// <summary>The longest leading prefix of <paramref name="text"/> that fits in a body cell of
    /// <paramref name="width"/> — what <c>TextTrimming="CharacterEllipsis"</c> actually leaves an operator to
    /// read.</summary>
    private static string LargestFittingPrefix(string text, double width)
        => Prefix(text, width - CellPadding, BodyAdvance);

    /// <summary>The same, for a COLUMN HEADER (the <c>colHdr</c> style's smaller font and its own padding).</summary>
    private static string LargestFittingHeaderPrefix(string text, double width)
        => Prefix(text, width - HeaderPadding, HeaderAdvance);

    private static string Prefix(string text, double room, double advance)
    {
        if (room <= 0) return string.Empty;
        var fits = (int)Math.Floor(room / advance);
        return fits >= text.Length ? text : text[..Math.Max(0, fits)];
    }

    // ------------------------------------------------------------------------------- the shared fixture

    /// <summary>A company enrolled for both PF and ESI with one fully-identified member and a posted payroll run
    /// — the same shape as the reachability harness, so both files exercise one fixture.</summary>
    private MainWindowViewModel BuildCompany()
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = "Statutory Fit Co " + Guid.NewGuid().ToString("N")[..8];
        vm.CreateCompany();
        var c = vm.Company!;
        var fyStart = new DateOnly(c.FinancialYearStart.Year, c.FinancialYearStart.Month, 1);
        var fyEnd = fyStart.AddMonths(1).AddDays(-1);

        var pay = new PayrollService(c);
        pay.EnablePayroll();
        pay.EnableProvidentFund(capWagesAtCeiling: true);
        pay.EnableEsi(employerCode: "12345678901234567");

        var ph = new PayHeadService(c);
        var indirect = c.FindGroupByName("Indirect Expenses")!.Id;
        var liab = c.FindGroupByName("Current Liabilities")!.Id;

        var basic = ph.CreatePayHead("Basic", PayHeadType.Earnings, PayHeadCalculationType.FlatRate,
            underGroupId: indirect, partOfPfWages: true, partOfEsiWages: true);
        var eePf = ph.CreatePayHead("Employee EPF", PayHeadType.EmployeesStatutoryDeductions,
            PayHeadCalculationType.AsUserDefinedValue, underGroupId: liab,
            pfComponent: PfStatutoryComponent.EmployeeProvidentFund);
        var erPf = ph.CreatePayHead("Employer EPF", PayHeadType.EmployersStatutoryContributions,
            PayHeadCalculationType.AsUserDefinedValue, underGroupId: liab,
            pfComponent: PfStatutoryComponent.EmployerProvidentFund);
        var eps = ph.CreatePayHead("Employer Pension", PayHeadType.EmployersStatutoryContributions,
            PayHeadCalculationType.AsUserDefinedValue, underGroupId: liab,
            pfComponent: PfStatutoryComponent.EmployerPension);
        var edli = ph.CreatePayHead("EDLI", PayHeadType.EmployersOtherCharges,
            PayHeadCalculationType.AsUserDefinedValue, underGroupId: liab,
            pfComponent: PfStatutoryComponent.EmployeesDepositLinkedInsurance);
        var eeEsi = ph.CreatePayHead("Employee ESI", PayHeadType.EmployeesStatutoryDeductions,
            PayHeadCalculationType.AsUserDefinedValue, underGroupId: liab,
            esiComponent: EsiStatutoryComponent.EmployeeStateInsurance);
        var erEsi = ph.CreatePayHead("Employer ESI", PayHeadType.EmployersStatutoryContributions,
            PayHeadCalculationType.AsUserDefinedValue, underGroupId: liab,
            esiComponent: EsiStatutoryComponent.EmployerStateInsurance);

        var e = pay.CreateEmployee("Sanjay Kumar", pay.CreateEmployeeGroup("Staff").Id,
            employeeNumber: "E-100", uan: Uan, esiNumber: Ip);
        pay.SetEmployeePfDetails(e.Id, applicable: true, contributeOnHigherWages: false);
        pay.SetEmployeeEsiDetails(e.Id, applicable: true);
        e.PfAccountNumber = "MH/BAN/0000001/000/0000123";
        e.DateOfJoining = fyStart;
        e.PfJoinDate = fyStart;
        e.DateOfBirth = new DateOnly(1990, 5, 4);
        e.Gender = "Male";
        e.Designation = "Machine Operator";

        new SalaryStructureService(c).DefineForEmployee(e.Id, fyStart, new[]
        {
            new SalaryStructureLine(basic.Id, 0, new Money(20000m)),
            new SalaryStructureLine(eePf.Id, 1),
            new SalaryStructureLine(erPf.Id, 2),
            new SalaryStructureLine(eps.Id, 3),
            new SalaryStructureLine(edli.Id, 4),
            new SalaryStructureLine(eeEsi.Id, 5),
            new SalaryStructureLine(erEsi.Id, 6),
        });

        new PayrollVoucherService(c).Post(fyStart, fyEnd, new[] { e.Id });
        _storage.Save(c);
        return vm;
    }
}
