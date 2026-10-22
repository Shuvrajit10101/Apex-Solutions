using System;
using System.Collections.Generic;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Desktop.Views;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.Tests;

/// <summary>
/// 🔴 <b>A DISCLOSURE THAT IS ELLIPSISED IS NOT A DISCLOSURE — AND THE CHARACTER BUDGET THAT WAS SUPPOSED
/// TO GUARANTEE IT WAS DERIVED FROM THE WRONG GRID.</b>
///
/// <para>W-BS scopes the shared surface's five GST reports to the primary registration and discloses the
/// narrowing on the face of the report. Its own width check asserts the note is within
/// <c>ReportsViewModel.GstRegistrationScopeNoteBudget</c> (142 characters), a budget derived from the
/// <b>GSTR-1</b> grid: <c>MinWidth="950"</c> with the header cell carrying <c>Grid.ColumnSpan="9"</c>.
/// It then applies that one budget to all three <c>Rows</c>-based GST reports. <b>Tax Analysis does not
/// have that shape.</b> Its item template gave the header cell <b>one</b> column of
/// <c>*,120,120,120,150,150</c> — a 170px star at the grid's own floor, <b>175px measured on a realised
/// 1440px window</b> — with <c>TextTrimming="CharacterEllipsis"</c> and a fixed <c>Height="24"</c>. The
/// 98-character note was cut to roughly its first 25 characters, losing the GSTIN it exists to name. The
/// branch's budget test passed throughout.</para>
///
/// <para>🔴 <b>THE ASSERTION HERE IS GEOMETRIC, NOT TYPOGRAPHIC, AND THAT IS DELIBERATE.</b> Headless
/// resolves this font ~1.82x wider than the repository's own measured advance (the 13-character reference
/// <c>"GSTIN / Desc."</c> measures 156px here against the 85.8px the axaml records), so ANY assertion in
/// absolute pixels would pin a wrong answer and would condemn GSTR-1, which is in fact fine. So: a
/// single-line trimming cell must <b>span its row</b>, and a wrapping cell must fit the height it was
/// actually given. Both are pure layout, and both are read off the <b>realised control</b>.</para>
///
/// <para><b>The fixture is deliberately unlike the branch's</b> — three registrations rather than two,
/// Karnataka/Tamil Nadu/Delhi rather than Maharashtra/Gujarat, 12% rather than 18%, ₹7,300 / ₹2,900 /
/// ₹1,100 — so no figure here can collide with one of theirs and pass for the wrong reason.</para>
/// </summary>
public sealed class GstScopeDisclosureReadabilityTests
{
    private const string Karnataka = "29";
    private const string TamilNadu = "33";
    private const string Delhi = "07";
    private static readonly DateOnly Fy = new(2024, 4, 1);

    private static string G(string st, string pan)
    { var b = st + pan + "1Z"; return b + Gstin.ComputeCheckDigit(b + "0"); }

    private static readonly string GPrim = G(Karnataka, "AAPFU0939F");
    private static readonly string GTn = G(TamilNadu, "AABCC1206D");
    private static readonly string GDl = G(Delhi, "AADCS1206D");
    private static readonly string GCustKa = G(Karnataka, "AAACS1206D");

    private static DomainLedger Add(Company c, string n, string g, bool dr)
    {
        var grp = c.FindGroupByName(g)!;
        var l = new DomainLedger(Guid.NewGuid(), n, grp.Id, Money.Zero, dr);
        c.AddLedger(l); return l;
    }

    private static void Sale(LedgerService led, GstService gst, Guid typeId, DomainLedger sales,
                             DomainLedger party, decimal val, DateOnly d, int no, Guid? reg)
    {
        var tax = gst.ComputeInvoiceTax(
            new[] { new GstService.TaxableLine(Money.FromRupees(val), 1200) }, false, GstTaxDirection.Output);
        var lines = new List<EntryLine>
        {
            new(party.Id, Money.FromRupees(val + tax.TotalTax.Amount), DrCr.Debit),
            new(sales.Id, Money.FromRupees(val), DrCr.Credit),
        };
        lines.AddRange(tax.TaxLines);
        var v = new Voucher(Guid.NewGuid(), typeId, d, lines, number: no, partyId: party.Id);
        if (reg is { } r) v.GstRegistrationId = r;
        led.Post(v);
    }

    /// Three registrations, 12%, amounts 7300 / 2900 / 1100 — nothing collides with the branch's fixture.
    private static Company ThreeRegBook()
        => Configure(CompanyFactory.CreateSeeded("GstScopeDisc " + Guid.NewGuid().ToString("N")[..6], Fy));

    private static Company Configure(Company c)
    {
        c.FinancialYearStart = Fy;
        c.BooksBeginFrom = Fy;
        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = Karnataka, Gstin = GPrim,
            RegistrationType = GstRegistrationType.Regular, ApplicableFrom = Fy,
            Periodicity = GstReturnPeriodicity.Monthly,
        });
        var sales = Add(c, "Sales", "Sales Accounts", false);
        sales.SalesPurchaseGst = new StockItemGstDetails
        { HsnSac = "998314", Taxability = GstTaxability.Taxable, RateBasisPoints = 1200,
          SupplyType = GstSupplyType.Services };

        var tn = new GstRegistration(Guid.NewGuid(), "Tamil Nadu Registration", TamilNadu, GTn,
                                     GstRegistrationType.Regular, Fy);
        var dl = new GstRegistration(Guid.NewGuid(), "Delhi Registration", Delhi, GDl,
                                     GstRegistrationType.Regular, Fy);
        c.Gst!.AddRegistration(tn); c.Gst!.AddRegistration(dl); c.Gst!.EnsureValid();

        var cust = Add(c, "Bengaluru Customer", "Sundry Debtors", true);
        cust.PartyGst = new PartyGstDetails
        { RegistrationType = GstRegistrationType.Regular, Gstin = GCustKa, StateCode = Karnataka };

        var gst = new GstService(c); var led = new LedgerService(c);
        var t = c.VoucherTypes.First(x => x.BaseType == VoucherBaseType.Sales).Id;
        Sale(led, gst, t, sales, cust, 7_300m, new DateOnly(2024, 4, 3), 11, null);
        Sale(led, gst, t, sales, cust, 2_900m, new DateOnly(2024, 4, 4), 12, tn.Id);
        Sale(led, gst, t, sales, cust, 1_100m, new DateOnly(2024, 4, 5), 13, dl.Id);
        return c;
    }

    /// <summary>Reachability on a THREE-registration book, proved by construction: the projection happens
    /// inside the constructor and these three kinds have no handler around them.</summary>
    [Theory]
    [InlineData(ReportKind.TaxAnalysis)]
    [InlineData(ReportKind.Gstr1)]
    [InlineData(ReportKind.Gstr3b)]
    public void Each_grid_gst_report_opens_on_a_three_registration_book(ReportKind kind)
    {
        var vm = new ReportsViewModel(ThreeRegBook(), kind);
        Assert.NotEmpty(vm.Rows);
    }

    /// <summary>The narrowing is real: 7,300 @ 12% ⇒ 438.00 per head. The fold would read 612.00, Tamil Nadu
    /// alone 174.00 and Delhi alone 66.00 — four distinct numbers, only one of which can pass.</summary>
    [Fact]
    public void Gstr3b_shows_only_the_primary_registrations_tax()
    {
        var vm = new ReportsViewModel(ThreeRegBook(), ReportKind.Gstr3b);
        var all = string.Join("\n", vm.Rows.Select(r =>
            $"{r.Col1}|{r.Col2}|{r.Col3}|{r.Col4}|{r.Col5}|{r.Col6}"));
        // 7,300 @ 12% -> CGST 438.00 / SGST 438.00. The fold would be 612.00; TN alone 174.00.
        Assert.Contains("438.00", all);
        Assert.DoesNotContain("612.00", all);
        Assert.DoesNotContain("174.00", all);
        Assert.DoesNotContain("66.00", all);
    }

    /// <summary>The disclosure must be READABLE where it is actually rendered — see the class doc for why
    /// this is asserted as geometry rather than in pixels of text.</summary>
    [AvaloniaTheory]
    [InlineData(ReportKind.TaxAnalysis)]
    [InlineData(ReportKind.Gstr1)]
    [InlineData(ReportKind.Gstr3b)]
    // The two matrix annual returns disclose through the footnote panel instead of a grid row. Included
    // here because nothing else reads that disclosure off a REALISED control — the branch's own check
    // reads the PayrollFootnotes collection, which cannot tell a rendered footnote from an unrendered one.
    [InlineData(ReportKind.Gstr9AnnualReturn)]
    [InlineData(ReportKind.Gstr9cReconciliation)]
    public void The_scope_disclosure_is_readable_on_the_grid_that_renders_it(ReportKind kind)
    {
        var storage = new CompanyStorage(System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "GstScopeDisc_" + Guid.NewGuid().ToString("N")));
        var mv = new MainWindowViewModel(storage);
        mv.NewCompanyName = "GstScopeDisc " + Guid.NewGuid().ToString("N")[..6];
        mv.CreateCompany();
        Configure(mv.Company!);
        mv.ShowGateway();
        mv.OpenReport(kind);

        var win = new MainWindow { DataContext = mv };
        win.MinWidth = 640; win.MinHeight = 480;
        win.Width = 1440; win.Height = 1080;
        win.Show();
        win.MinWidth = 640; win.MinHeight = 480;
        win.Width = 1440; win.Height = 1080;
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();

        var cells = win.GetVisualDescendants().OfType<TextBlock>()
                       .Where(t => (t.Text ?? "").Contains("Scoped to GSTIN", StringComparison.Ordinal)
                                   && t.Bounds.Width > 0 && t.IsEffectivelyVisible)
                       .ToList();
        Assert.True(cells.Count > 0, $"{kind}: the disclosure never realised — vacuous.");

        foreach (var tb in cells)
        {
            var rowGrid = tb.GetVisualAncestors().OfType<Grid>()
                            .First(g => g.ColumnDefinitions.Count > 1);

            if (tb.TextWrapping == TextWrapping.Wrap)
            {
                var wrapProbe = new TextBlock
                {
                    Text = tb.Text, FontSize = tb.FontSize, FontFamily = tb.FontFamily,
                    FontWeight = tb.FontWeight, TextWrapping = TextWrapping.Wrap,
                };
                wrapProbe.Measure(new Size(Math.Max(tb.Bounds.Width, 1), double.PositiveInfinity));
                Assert.True(
                    wrapProbe.DesiredSize.Height <= tb.Bounds.Height + 0.5,
                    $"{kind}: the wrapped disclosure needs {wrapProbe.DesiredSize.Height:F0}px of height "
                    + $"but the row gave {tb.Bounds.Height:F0}px.");
                continue;
            }

            var span = tb.Bounds.Width / rowGrid.Bounds.Width;
            Assert.True(
                span >= 0.97,
                $"{kind}: the single-line trimming disclosure occupies only {tb.Bounds.Width:F0}px of its "
                + $"{rowGrid.Bounds.Width:F0}px row ({span * 100:F0}%), so it is ellipsised inside one "
                + $"column instead of spanning the grid. Text was {tb.Text!.Length} characters.");
        }
    }
}
