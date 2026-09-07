using System;
using System.Text;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Io;
using Apex.Ledger.Services;
using Domain = Apex.Ledger.Domain;

namespace Apex.Desktop.Tests;

/// <summary>
/// 🔴 <b>RULING 18 on the MAINSTREAM report headings — the case a review proved was still broken after the seam
/// was built.</b>
///
/// <para><c>MultiAccountPrintProjector</c> was given a <see cref="PrintReport.TitleCarriesMasterName"/> flag and a
/// source comment claiming its heading was "the ONE heading in the product that concatenates a product-authored
/// label with a MASTER NAME the user typed". It was not. <see cref="ReportsViewModel"/> builds three headings of
/// exactly that shape — Group Summary, Group Vouchers and Ledger Monthly Summary — and none of them was flagged,
/// so a customer ledger named after the vendor was still printed and exported under a mangled name from four
/// shipped egress points (Export, Email compose, Print preview, WhatsApp share). The comment was worse than the
/// omission: it told the next reader the seam was finished.</para>
///
/// <para>The fix runs the provenance from the producer that KNOWS — <see cref="ReportsViewModel"/> — through both
/// projectors, so one flag decides the PDF and all four tabular formats and they cannot drift. These tests walk
/// that whole path with a ledger and a group whose names actually carry the token; a clean fixture would prove
/// nothing at all here, which is precisely how this shipped green the first time.</para>
/// </summary>
public sealed class ReportTitleMasterNameSeamTests
{
    private static readonly DateOnly FyStart = new(2025, 4, 1);
    private const string BrandedLedger = "Tally Traders Pvt Ltd";
    private const string BrandedGroup = "Tally Distributors";
    private const string OurCompany = "Bright Retail Co";   // OURS, deliberately CLEAN

    private static string AsLatin1(byte[] bytes) => Encoding.Latin1.GetString(bytes);
    private static string Utf8(byte[] bytes) => new UTF8Encoding(false).GetString(bytes).TrimStart('﻿');

    /// <summary>A company holding one branded party ledger, filed under one branded sub-group of Sundry Debtors.</summary>
    private static (Company C, Guid LedgerId, Guid GroupId) Books()
    {
        var c = CompanyFactory.CreateSeeded(OurCompany, FyStart);

        var debtors = c.FindGroupByName("Sundry Debtors")!;
        var group = new Domain.Group(Guid.NewGuid(), BrandedGroup, debtors.Nature, parentId: debtors.Id);
        c.AddGroup(group);

        var party = new Domain.Ledger(
            Guid.NewGuid(), BrandedLedger, group.Id, Money.FromRupees(25_000m), openingIsDebit: true);
        c.AddLedger(party);

        return (c, party.Id, group.Id);
    }

    // ------------------------------------------------------------------ the producer states the provenance

    [Fact]
    public void The_ledger_monthly_summary_flags_its_heading_as_carrying_a_master_name()
    {
        var (c, ledgerId, _) = Books();
        var vm = new ReportsViewModel(c, ReportKind.LedgerMonthlySummary, scopeMasterId: ledgerId);

        Assert.Contains(BrandedLedger, vm.Title, StringComparison.Ordinal);
        Assert.True(vm.TitleCarriesMasterName,
            "the heading concatenates our label with the ledger's own name; unflagged, every downstream format "
          + "scrubs the party's name out of the heading of the report about them.");
    }

    [Theory]
    [InlineData(ReportKind.GroupSummary)]
    [InlineData(ReportKind.GroupVouchers)]
    public void The_group_reports_flag_their_heading_as_carrying_a_master_name(ReportKind kind)
    {
        var (c, _, groupId) = Books();
        var vm = new ReportsViewModel(c, kind, scopeMasterId: groupId);

        Assert.Contains(BrandedGroup, vm.Title, StringComparison.Ordinal);
        Assert.True(vm.TitleCarriesMasterName);
    }

    /// <summary>
    /// The fail-safe default, and the reason the flag is RESET on every rebuild rather than only set: a report
    /// whose heading this product authored keeps the ER-11 guard. Forgetting the flag must scrub our own text
    /// (harmless), never leak the brand.
    /// </summary>
    [Fact]
    public void A_product_authored_heading_is_not_flagged()
    {
        var (c, _, _) = Books();
        var vm = new ReportsViewModel(c, ReportKind.TrialBalance);

        Assert.False(vm.TitleCarriesMasterName);
    }

    /// <summary>
    /// 🔴 The flag must be re-decided on EVERY rebuild, in both directions. A view model that had shown a
    /// master-name report and is then switched to a product-authored one must not keep waiving the guard — that
    /// would leak the brand out of an F12 title override on the next report the user opens. This is what the
    /// reset before the builder switch buys, and it is asserted through <see cref="ReportsViewModel.Show"/>,
    /// the real rebuild entry point the shell uses (assigning <c>Kind</c> alone rebuilds nothing at all — Title
    /// and Rows would be equally stale).
    /// </summary>
    [Fact]
    public void Switching_from_a_master_name_report_to_a_product_authored_one_takes_the_guard_back()
    {
        var (c, ledgerId, _) = Books();
        var vm = new ReportsViewModel(c, ReportKind.LedgerMonthlySummary, scopeMasterId: ledgerId);
        Assert.True(vm.TitleCarriesMasterName);

        vm.Show(ReportKind.TrialBalance);

        Assert.False(vm.TitleCarriesMasterName);

        // ...and back again, so the reset cannot be "fixed" by hard-coding false.
        vm.Show(ReportKind.LedgerMonthlySummary);
        Assert.True(vm.TitleCarriesMasterName);
    }

    // ------------------------------------------------------------------ end of every pipe

    /// <summary>
    /// The printed page: the party's name reaches the PDF heading in full. This is the assertion the review
    /// proved failing — its probe measured <c>pdfBrandCount=0</c>, i.e. the name mangled on the page.
    /// </summary>
    [Fact]
    public void The_party_name_reaches_the_pdf_heading_in_full()
    {
        var (c, ledgerId, _) = Books();
        var vm = new ReportsViewModel(c, ReportKind.LedgerMonthlySummary, scopeMasterId: ledgerId);

        string s = AsLatin1(ReportPdf.Render(ReportPrintProjector.Project(vm), new PageConfig()));

        Assert.Contains(BrandedLedger, s, StringComparison.Ordinal);
    }

    /// <summary>
    /// And all four tabular formats, which the review measured as <c>htmlHasFullName=False</c>,
    /// <c>xmlHasFullName=False</c>, <c>jsonHasFullName=False</c>. One theory over every format so a new one
    /// cannot be added on the scrubbing path unnoticed.
    /// </summary>
    [Theory]
    [InlineData("html")]
    [InlineData("xml")]
    [InlineData("json")]
    public void The_party_name_reaches_the_heading_of_every_tabular_export(string format)
    {
        var (c, ledgerId, _) = Books();
        var vm = new ReportsViewModel(c, ReportKind.LedgerMonthlySummary, scopeMasterId: ledgerId);
        var export = ReportTabularProjector.Project(vm);

        string text = format switch
        {
            "html" => Utf8(HtmlReportWriter.Write(export)),
            "xml" => Utf8(XmlReportWriter.Write(export)),
            _ => Utf8(JsonReportWriter.Write(export)),
        };

        Assert.Contains(BrandedLedger, text, StringComparison.Ordinal);
    }

    /// <summary>
    /// The other direction, on the same pipe: a Trial Balance heading the OPERATOR branded through an F12 title
    /// override is still scrubbed in the PDF and in every tabular format. Without this, "fixing" ruling 18 by
    /// simply never de-branding a title would pass every test above.
    ///
    /// <para>Each assertion targets the TITLE specifically, not the whole document. That is not a weakening: the
    /// Trial Balance body legitimately lists <c>Tally Traders Pvt Ltd</c> as a party row, and it MUST — a blanket
    /// "the token appears nowhere" assertion here would be asserting the very defect ruling 18 exists to close.
    /// </para>
    /// </summary>
    [Fact]
    public void A_product_authored_heading_carrying_the_brand_is_still_scrubbed_everywhere()
    {
        var (c, _, _) = Books();
        var vm = new ReportsViewModel(c, ReportKind.TrialBalance) { Title = "Tally Trial Balance" };

        string pdf = AsLatin1(ReportPdf.Render(ReportPrintProjector.Project(vm), new PageConfig()));
        var export = ReportTabularProjector.Project(vm);

        Assert.False(vm.TitleCarriesMasterName);

        // The PDF heading band and the /Title metadata are both scrubbed, and scrubbed is not blanked.
        Assert.DoesNotContain("Tally Trial Balance", pdf, StringComparison.Ordinal);
        Assert.Contains("(Trial Balance) Tj", pdf, StringComparison.Ordinal);
        Assert.Contains("/Title (Trial Balance", pdf, StringComparison.Ordinal);

        // Each tabular format's own title slot.
        Assert.Contains("<title>Trial Balance</title>", Utf8(HtmlReportWriter.Write(export)), StringComparison.Ordinal);
        Assert.Contains("<Report title=\"Trial Balance\">", Utf8(XmlReportWriter.Write(export)), StringComparison.Ordinal);
        Assert.Contains("\"title\": \"Trial Balance\"", Utf8(JsonReportWriter.Write(export)), StringComparison.Ordinal);

        // And the party row is still there, verbatim — the body was never the thing being guarded.
        Assert.Contains(BrandedLedger, Utf8(HtmlReportWriter.Write(export)), StringComparison.Ordinal);
    }
}
