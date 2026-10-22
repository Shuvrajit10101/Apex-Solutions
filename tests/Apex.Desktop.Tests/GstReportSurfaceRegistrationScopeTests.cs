using System;
using System.Collections.Generic;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Xunit;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.Tests;

/// <summary>
/// 🔴 <b>THE FIVE GST REPORTS ON THE SHARED REPORT SURFACE WERE UNREACHABLE ON A MULTI-REGISTRATION BOOK, AND
/// THREE OF THEM TOOK THE SHELL DOWN RATHER THAN SHOWING A MESSAGE (census 6.23, row 6.8's unscoped-builder
/// sweep).</b>
///
/// <para><b>The defect, in two grades.</b> <c>GstReportSupport.EnsureRegistrationScoped</c> refuses any GST
/// projection that does not name a registration once a company holds a second one — deliberately, because a
/// return folded over two GSTINs is a wrong filed document. All five of this surface's GST reports called their
/// engine with no registration:
/// <list type="bullet">
///   <item><b>GSTR-9</b> and <b>GSTR-9C</b> are dispatched through <c>RunStatutoryForm</c>, which catches the
///   refusal — so the whole annual return was replaced by the refusal text. A hard stop, never a wrong
///   figure.</item>
///   <item><b>Tax Analysis</b>, <b>GSTR-1</b> and <b>GSTR-3B</b> are dispatched WITHOUT it, and
///   <c>ReportsViewModel</c> builds inside its own constructor, so the <c>InvalidOperationException</c> left
///   <c>Show()</c> unhandled and escaped <c>MainWindowViewModel.OpenReport</c>. <b>That is strictly worse than
///   the two above and was not previously recorded.</b> Pinned by
///   <see cref="The_three_grid_gst_reports_open_on_a_multi_registration_book_instead_of_throwing"/>, which on
///   <c>main</c> fails with that exception out of the constructor — the construction itself is the assertion.</item>
/// </list></para>
///
/// <para>🔴 <b>THE ASSERTIONS ARE ON THE REALISED REPORT, NEVER ON AN INTERNAL.</b> Every figure below is read
/// out of <c>Rows</c> / <c>PayrollRows</c> — the collections the grid and the matrix bind — and the registration
/// disclosure is read as the row text an operator sees. Nothing asserts on a property this test set.</para>
///
/// <para>🔴 <b>AND THE NARROWING IS ITSELF PINNED, NOT JUST THE REACHABILITY.</b> Scoping a report to the
/// primary registration would be worthless — and would re-create the very defect the engine's refusal exists to
/// prevent — if the figures still folded in the branch. The fixture therefore posts a sale under EACH
/// registration at DIFFERENT values, so the primary's own figure, the branch's figure and their sum are three
/// distinct numbers and only one of them can pass:
/// Maharashtra primary ₹1,000 @ 18% ⇒ CGST 90 / SGST 90; the Gujarat branch ₹4,000 @ 18% ⇒ CGST 360 / SGST 360.
/// The report must read <b>90.00</b> — not 360.00, and not 450.00.</para>
///
/// <para><b>ER-13 is asserted too</b> (<see cref="A_single_registration_books_grid_is_row_for_row_what_it_was"/>):
/// on a one-GSTIN book the scope argument selects the same vouchers and resolves the same GSTIN as no argument
/// did, so no disclosure row appears and the grid is row-for-row unchanged. That is the half of the change that
/// must be invisible.</para>
///
/// <para><b>Sources (R7).</b> The refusal and the primary-is-the-default convention are internal (census 6.23)
/// and are cited as internal. The multi-registration gesture the surface still lacks is the vendor's own: "If
/// you have multiple registrations, select the required GST Registration" —
/// <c>help.tallysolutions.com/upload-gstr-1/</c>, quoted in <c>GstOfflineReturnsViewModel.BuildJson</c> where it
/// was opened by content. <b>No claim is made here that the reference product scopes THIS surface to the
/// primary;</b> that is ours, disclosed on the face of the report and recorded as a divergence.</para>
/// </summary>
public sealed class GstReportSurfaceRegistrationScopeTests
{
    private const string Maharashtra = "27";
    private const string Gujarat = "24";

    private static readonly DateOnly FyStart = new(2024, 4, 1);
    private static readonly DateOnly PrimarySaleDate = new(2024, 4, 5);
    private static readonly DateOnly BranchSaleDate = new(2024, 4, 9);

    private static string GstinFor(string stateCode, string pan)
    {
        var body = stateCode + pan + "1Z";
        return body + Gstin.ComputeCheckDigit(body + "0");
    }

    private static readonly string GstinPrimary = GstinFor(Maharashtra, "AAPFU0939F");
    private static readonly string GstinBranch = GstinFor(Gujarat, "AABCC1206D");
    private static readonly string GstinDebtorMh = GstinFor(Maharashtra, "AAACS1206D");
    private static readonly string GstinDebtorGj = GstinFor(Gujarat, "AADCS1206D");

    /// <summary>The three GST reports whose kinds are dispatched without <c>RunStatutoryForm</c>.</summary>
    public static IEnumerable<object[]> GridGstKinds() => new[]
    {
        new object[] { ReportKind.TaxAnalysis },
        new object[] { ReportKind.Gstr1 },
        new object[] { ReportKind.Gstr3b },
    };

    /// <summary>The two matrix annual returns re-homed by PR #173.</summary>
    public static IEnumerable<object[]> MatrixGstKinds() => new[]
    {
        new object[] { ReportKind.Gstr9AnnualReturn },
        new object[] { ReportKind.Gstr9cReconciliation },
    };

    // ============================================================ reachability

    /// <summary>
    /// 🔴 <b>CONSTRUCTING THE REPORT IS THE ASSERTION.</b> <c>ReportsViewModel</c> projects inside its own
    /// constructor and these three kinds have no handler around them, so on <c>main</c> this line throws
    /// <c>InvalidOperationException</c> ("<i>This company holds multiple GST registrations…</i>") and the test
    /// fails by name. The row check afterwards stops it passing for the wrong reason — a report that opened and
    /// rendered nothing is not a fixed report.
    /// </summary>
    [Theory]
    [MemberData(nameof(GridGstKinds))]
    public void The_three_grid_gst_reports_open_on_a_multi_registration_book_instead_of_throwing(ReportKind kind)
    {
        var (company, _) = TwoRegistrationBook($"Scope Grid {kind}");

        var reports = new ReportsViewModel(company, kind);

        Assert.NotEmpty(reports.Rows);
        Assert.DoesNotContain(
            reports.Rows.Select(r => r.Col1),
            text => text.Contains("holds multiple GST registrations", StringComparison.Ordinal));
    }

    /// <summary>
    /// The two matrix annual returns: the refusal was CAUGHT here, so the failure shape on <c>main</c> is an
    /// empty matrix under <c>PayrollEmptyNote</c> carrying the refusal text, not an exception. Both halves are
    /// asserted, because a projection that merely stopped setting the note would satisfy one of them.
    /// </summary>
    [Theory]
    [MemberData(nameof(MatrixGstKinds))]
    public void The_two_annual_returns_render_on_a_multi_registration_book_instead_of_refusing(ReportKind kind)
    {
        var (company, _) = TwoRegistrationBook($"Scope Matrix {kind}");

        var reports = new ReportsViewModel(company, kind);

        Assert.NotEmpty(reports.PayrollRows);
        Assert.False(reports.IsPayrollEmpty);
        Assert.DoesNotContain("holds multiple GST registrations", reports.PayrollEmptyNote ?? string.Empty);
    }

    // ============================================================ the figures are ONE registration's

    /// <summary>
    /// 🔴 <b>THE WRONG-MONEY GUARD.</b> GSTR-1's grand-total CGST cell must carry the primary registration's
    /// ₹90.00 alone. The branch's ₹360.00 and the unscoped fold's ₹450.00 are both named in the failure message,
    /// so a regression says which of the two mistakes it made.
    /// </summary>
    [Fact]
    public void Gstr1_on_the_shared_surface_totals_only_the_primary_registrations_tax()
    {
        var (company, _) = TwoRegistrationBook("Scope Gstr1 Money");

        var reports = new ReportsViewModel(company, ReportKind.Gstr1);
        var total = reports.Rows.LastOrDefault(r => r.IsTotal && r.Col1.StartsWith("Grand Total", StringComparison.Ordinal));

        Assert.NotNull(total);
        Assert.Equal("90.00", total!.Col6);   // 360.00 ⇒ scoped to the wrong one; 450.00 ⇒ not scoped at all
        Assert.Equal("90.00", total.Col7);
    }

    /// <summary>
    /// The same guard on GSTR-3B's own 3.1(a) CGST line — a different engine leg (<c>Gstr3b.ReadSide</c>) read
    /// through a different facade, so agreement between the two is evidence rather than a shared path.
    /// </summary>
    [Fact]
    public void Gstr3b_on_the_shared_surface_reports_only_the_primary_registrations_outward_tax()
    {
        var (company, _) = TwoRegistrationBook("Scope Gstr3b Money");

        var reports = new ReportsViewModel(company, ReportKind.Gstr3b);
        var texts = reports.Rows.Select(r => string.Join("|", r.Col1, r.Col2, r.Col3, r.Col4, r.Col5,
            r.Col6, r.Col7, r.Col8, r.Col9, r.Particulars, r.Amount)).ToList();

        Assert.Contains(texts, t => t.Contains("90.00", StringComparison.Ordinal));
        Assert.DoesNotContain(texts, t => t.Contains("450.00", StringComparison.Ordinal));
    }

    /// <summary>
    /// GSTR-9 Part II Table 4 is the annual fold of the twelve monthly GSTR-3Bs, so it is the deepest reach of
    /// the scope: <c>Gstr9.Build</c> → <c>Gstr3b.Build</c> → <c>PostedGstVouchers</c>. ₹90.00 for the year.
    /// </summary>
    [Fact]
    public void Gstr9_table_4_on_the_shared_surface_folds_only_the_primary_registration()
    {
        var (company, _) = TwoRegistrationBook("Scope Gstr9 Money");

        var reports = new ReportsViewModel(company, ReportKind.Gstr9AnnualReturn);
        var row = reports.PayrollRows.FirstOrDefault(
            r => r.Cells.Count > 1 && r.Cells[0].Text == "4  Outward tax — CGST");

        Assert.NotNull(row);
        Assert.Equal("90.00", row!.Cells[1].Text);
    }

    // ============================================================ the narrowing is disclosed

    /// <summary>
    /// 🔴 <b>A NARROWED REPORT THAT DOES NOT SAY SO IS A REPORT AN OPERATOR WILL READ AS THE WHOLE COMPANY.</b>
    /// The disclosure must be IN the grid (not only in a subtitle the printed copy may crop), must name the
    /// GSTIN actually folded, and must say where the other registration's return comes from.
    /// </summary>
    [Theory]
    [MemberData(nameof(GridGstKinds))]
    public void A_grid_gst_report_discloses_which_registration_it_is_scoped_to(ReportKind kind)
    {
        var (company, _) = TwoRegistrationBook($"Scope Disclose {kind}");

        var reports = new ReportsViewModel(company, kind);
        var note = reports.Rows.FirstOrDefault(r => r.Col1.Contains("Scoped to GSTIN", StringComparison.Ordinal));

        Assert.NotNull(note);
        Assert.Contains(GstinPrimary, note!.Col1, StringComparison.Ordinal);
        Assert.DoesNotContain(GstinBranch, note.Col1, StringComparison.Ordinal);
        Assert.Contains("GST Offline Returns", note.Col1, StringComparison.Ordinal);
    }

    /// <summary>
    /// The matrix returns disclose it as a footnote rather than a row, because the footnote panel lives inside
    /// the matrix body and a two-column label/amount row would put a sentence under an "Amount" heading.
    /// </summary>
    [Theory]
    [MemberData(nameof(MatrixGstKinds))]
    public void An_annual_return_discloses_which_registration_it_is_scoped_to(ReportKind kind)
    {
        var (company, _) = TwoRegistrationBook($"Scope Footnote {kind}");

        var reports = new ReportsViewModel(company, kind);

        Assert.True(reports.HasPayrollFootnotes);
        Assert.Contains(
            reports.PayrollFootnotes,
            f => f.Contains("Scoped to GSTIN", StringComparison.Ordinal)
              && f.Contains(GstinPrimary, StringComparison.Ordinal));
    }

    // ============================================================ ER-13

    /// <summary>
    /// <b>ER-13.</b> A one-GSTIN book must be untouched: naming the primary selects the same vouchers
    /// (<c>RegistrationOf</c> reads a null voucher registration as the primary) and resolves the same GSTIN
    /// (<c>FindRegistration</c> normalises null and <c>PrimaryId</c> alike). Asserted as the WHOLE grid, cell for
    /// cell, against the same book built with the scope passed explicitly as <c>null</c> through the engine — and
    /// with no disclosure row anywhere, because there is nothing to disclose.
    /// </summary>
    [Fact]
    public void A_single_registration_books_grid_is_row_for_row_what_it_was()
    {
        var company = SingleRegistrationBook("Scope Er13");

        var reports = new ReportsViewModel(company, ReportKind.Gstr1);

        Assert.DoesNotContain(
            reports.Rows.Select(r => r.Col1),
            text => text.Contains("Scoped to GSTIN", StringComparison.Ordinal));

        // The engine read with NO scope and with the primary named must be the same projection, so the grid the
        // report built from the scoped call must still foot to the unscoped engine's own totals.
        // The same window the report derives by default: books-begin → the last posted voucher date.
        var unscoped = Gstr1.Build(company, company.BooksBeginFrom, PrimarySaleDate);
        var total = reports.Rows.Last(r => r.IsTotal && r.Col1.StartsWith("Grand Total", StringComparison.Ordinal));

        Assert.Equal(IndianFormat.AmountAlways(unscoped.TotalCgst), total.Col6);
        Assert.Equal(IndianFormat.AmountAlways(unscoped.TotalSgst), total.Col7);
        Assert.Equal(IndianFormat.AmountAlways(unscoped.TotalIgst), total.Col8);
    }

    /// <summary>
    /// <b>The engine's refusal is NOT weakened — only the call sites were fixed.</b> A guard that had been
    /// softened instead of satisfied would make every test above pass for the wrong reason, so the refusal is
    /// re-asserted directly against an unscoped build on the same book.
    /// </summary>
    [Fact]
    public void The_engines_own_refusal_still_stands_for_a_caller_that_names_nothing()
    {
        var (company, _) = TwoRegistrationBook("Scope Guard Intact");

        var ex = Assert.Throws<InvalidOperationException>(
            () => Gstr1.Build(company, FyStart, new DateOnly(2025, 3, 31)));

        Assert.Contains("multiple GST registrations", ex.Message, StringComparison.Ordinal);
    }

    // ============================================================ fixture

    /// <summary>
    /// A Maharashtra primary Regular registration and a Gujarat Regular branch, each with one intra-State B2B
    /// sale at 18%: the primary ₹1,000 (CGST/SGST 90 each) and the branch ₹4,000 (CGST/SGST 360 each). The two
    /// values are deliberately unequal and neither is half the other, so "the primary's figure", "the branch's
    /// figure" and "both folded together" are three different numbers.
    /// <para>The Sales ledger carries an SAC so GSTR-1's Table 12 and GSTR-9's Table 17 have a real body row
    /// rather than a bare foot — the trap <c>RehomedReportSurfaceBjTests</c> records having hidden a defect from
    /// a five-thousand-test suite.</para>
    /// </summary>
    private static (Company Company, GstRegistration Branch) TwoRegistrationBook(string name)
    {
        var c = NewGstCompany(name);

        var branch = new GstRegistration(
            Guid.NewGuid(), "Gujarat Registration", Gujarat, GstinBranch,
            GstRegistrationType.Regular, FyStart);
        c.Gst!.AddRegistration(branch);
        c.Gst!.EnsureValid();

        var gst = new GstService(c);
        var ledgers = new LedgerService(c);
        var sales = c.Ledgers.First(l => l.Name == "Sales");

        var debtorMh = AddLedger(c, "Maharashtra Debtor", "Sundry Debtors", true);
        debtorMh.PartyGst = new PartyGstDetails
        { RegistrationType = GstRegistrationType.Regular, Gstin = GstinDebtorMh, StateCode = Maharashtra };
        var debtorGj = AddLedger(c, "Gujarat Debtor", "Sundry Debtors", true);
        debtorGj.PartyGst = new PartyGstDetails
        { RegistrationType = GstRegistrationType.Regular, Gstin = GstinDebtorGj, StateCode = Gujarat };

        var salesType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id;

        PostIntraSale(ledgers, gst, salesType, sales, debtorMh, 1_000m, PrimarySaleDate, 1, null);
        PostIntraSale(ledgers, gst, salesType, sales, debtorGj, 4_000m, BranchSaleDate, 2, branch.Id);

        return (c, branch);
    }

    /// <summary>The same shape with ONE registration and the primary's sale only, for the ER-13 check.</summary>
    private static Company SingleRegistrationBook(string name)
    {
        var c = NewGstCompany(name);

        var gst = new GstService(c);
        var ledgers = new LedgerService(c);
        var sales = c.Ledgers.First(l => l.Name == "Sales");

        var debtorMh = AddLedger(c, "Maharashtra Debtor", "Sundry Debtors", true);
        debtorMh.PartyGst = new PartyGstDetails
        { RegistrationType = GstRegistrationType.Regular, Gstin = GstinDebtorMh, StateCode = Maharashtra };

        var salesType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id;
        PostIntraSale(ledgers, gst, salesType, sales, debtorMh, 1_000m, PrimarySaleDate, 1, null);

        return c;
    }

    private static Company NewGstCompany(string name)
    {
        var c = CompanyFactory.CreateSeeded(name + " " + Guid.NewGuid().ToString("N")[..6], FyStart);
        c.BooksBeginFrom = FyStart;

        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = Maharashtra,
            Gstin = GstinPrimary,
            RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart,
            Periodicity = GstReturnPeriodicity.Monthly,
        });

        var sales = AddLedger(c, "Sales", "Sales Accounts", false);
        sales.SalesPurchaseGst = new StockItemGstDetails
        {
            HsnSac = "998313", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800,
            SupplyType = GstSupplyType.Services,
        };
        return c;
    }

    /// <summary>Posts one intra-State outward B2B invoice of <paramref name="value"/> at 18% under
    /// <paramref name="registrationId"/> (null ⇒ the primary, which is how every pre-v61 voucher reads).</summary>
    private static void PostIntraSale(
        LedgerService ledgers, GstService gst, Guid typeId, DomainLedger sales, DomainLedger party,
        decimal value, DateOnly date, int number, Guid? registrationId)
    {
        var tax = gst.ComputeInvoiceTax(
            new[] { new GstService.TaxableLine(Money.FromRupees(value), 1800) }, false, GstTaxDirection.Output);
        var lines = new List<EntryLine>
        {
            new(party.Id, Money.FromRupees(value + tax.TotalTax.Amount), DrCr.Debit),
            new(sales.Id, Money.FromRupees(value), DrCr.Credit),
        };
        lines.AddRange(tax.TaxLines);

        var v = new Voucher(Guid.NewGuid(), typeId, date, lines, number: number, partyId: party.Id);
        if (registrationId is { } r) v.GstRegistrationId = r;
        ledgers.Post(v);
    }

    private static DomainLedger AddLedger(Company c, string name, string groupName, bool openingIsDebit)
    {
        var group = c.FindGroupByName(groupName) ?? throw new InvalidOperationException($"No group '{groupName}'.");
        var l = new DomainLedger(Guid.NewGuid(), name, group.Id, Money.Zero, openingIsDebit);
        c.AddLedger(l);
        return l;
    }
}
