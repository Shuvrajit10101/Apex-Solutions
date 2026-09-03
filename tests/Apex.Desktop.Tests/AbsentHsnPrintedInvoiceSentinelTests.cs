using System;
using System.Collections.Generic;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Apex.Desktop.Services;

namespace Apex.Desktop.Tests;

/// <summary>
/// <b>The fourth D7 consumer: the printed invoice leaves an undeclared HSN column BLANK.</b>
///
/// <para>The D7 resolution order is unified in <see cref="GstReportSupport.HsnSacOf"/>; the SENTINEL each consumer
/// uses for the absent case is deliberately different, and that divergence had no test at all — the only guard
/// was <c>Assert.Equal("(none)", absent ?? "(none)")</c>, which is <c>x == (null ?? x)</c> and therefore true for
/// any x, asserted without calling a single consumer.</para>
///
/// <para>The other three consumers (the GSTR-1 Table-12 bucket label <c>"(none)"</c> and the NIC INV-01 / EWB-01
/// <c>HsnCd: ""</c>) are pinned in <c>tests/Apex.Ledger.Tests/AbsentHsnSentinelsPerConsumerTests.cs</c>. This one
/// lives here because <see cref="VoucherPrintProjector"/> is in Apex.Desktop. A Rule-46 tax invoice omits a field
/// it has no value for rather than printing a placeholder, so swapping this <c>??</c> operand for the report's
/// <c>"(none)"</c> would print the literal text "(none)" in the HSN column of a customer's tax invoice.</para>
///
/// <para>The sale is ₹2,04,317.63 — odd to the paisa.</para>
/// </summary>
public sealed class AbsentHsnPrintedInvoiceSentinelTests
{
    private const string GstinHome = "27AAPFU0939F1ZV";
    private static readonly DateOnly FyStart = new(2025, 4, 1);
    private static readonly DateOnly SaleDate = new(2025, 4, 10);
    private const decimal Taxable = 2_04_317.63m;

    private static Apex.Ledger.Domain.Ledger Add(Company c, string name, string groupName, bool openingIsDebit)
    {
        var l = new Apex.Ledger.Domain.Ledger(
            Guid.NewGuid(), name, c.FindGroupByName(groupName)!.Id, Money.Zero, openingIsDebit);
        c.AddLedger(l);
        return l;
    }

    /// <summary>
    /// A GST company whose stock item declares NO HSN by either route, and one posted item invoice for it.
    /// </summary>
    private static (Company Company, Voucher Sale) Build()
    {
        var c = CompanyFactory.CreateSeeded("Absent-HSN Print Co", FyStart);
        var gst = new GstService(c);
        gst.EnableGst(new GstConfig
        {
            HomeStateCode = "27", Gstin = GstinHome, RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart, Periodicity = GstReturnPeriodicity.Monthly,
        });

        var inv = new InventoryService(c);
        var grp = inv.CreateStockGroup("Goods");
        var nos = inv.CreateSimpleUnit("Nos", "Numbers", unitQuantityCode: "NOS");
        var widget = inv.CreateStockItem("Widget", grp.Id, nos.Id);
        // Rated and taxable, but NO HsnSac — and the legacy HsnSacCode is left null.
        widget.Gst = new StockItemGstDetails { Taxability = GstTaxability.Taxable, RateBasisPoints = 1800 };
        Assert.Null(GstReportSupport.HsnSacOf(widget)); // the fixture's premise, asserted not assumed
        inv.AddOpeningBalance(widget.Id, c.MainLocation!.Id, 100m, Money.FromRupees(500m));

        var sales = Add(c, "Sales", "Sales Accounts", false);
        var party = Add(c, "Local Debtor", "Sundry Debtors", true);
        party.PartyGst = new PartyGstDetails
        { RegistrationType = GstRegistrationType.Regular, Gstin = GstinHome, StateCode = "27" };

        var tax = gst.ComputeInvoiceTax(
            new[] { new GstService.TaxableLine(Money.FromRupees(Taxable), 1800, null) },
            interState: false, GstTaxDirection.Output);

        var legs = new List<EntryLine>
        {
            new(party.Id, new Money(Taxable + tax.TotalTax.Amount + tax.TotalCess.Amount), DrCr.Debit),
            new(sales.Id, Money.FromRupees(Taxable), DrCr.Credit),
        };
        legs.AddRange(tax.TaxLines);

        var post = new LedgerService(c);
        var salesType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id;
        var sale = post.Post(new Voucher(
            Guid.NewGuid(), salesType, SaleDate, legs, partyId: party.Id,
            inventoryLines: new[]
            {
                new VoucherInventoryLine(widget.Id, c.MainLocation!.Id, 1m, Money.FromRupees(Taxable)),
            }));

        return (c, sale);
    }

    /// <summary>
    /// The printed tax invoice's HSN column is EMPTY for an item that declares no HSN — never the GSTR-1 report's
    /// "(none)" label, and never any other placeholder.
    /// </summary>
    [Fact]
    public void ThePrintedInvoiceLeavesAnUndeclaredHsnColumnBlank()
    {
        var (company, sale) = Build();

        var print = VoucherPrintProjector.ProjectInvoice(company, sale);
        var row = Assert.Single(print.Items);

        Assert.Equal(string.Empty, row.HsnSac);
        Assert.DoesNotContain("none", row.HsnSac, StringComparison.OrdinalIgnoreCase);
        // The row is the real posted line, not an empty stub — so the assertion above is about the sentinel.
        Assert.Equal(Money.FromRupees(Taxable), row.TaxableValue);
    }

    /// <summary>
    /// The blank is specific to ABSENCE: a declared HSN still prints verbatim, so a guard that blanked the column
    /// unconditionally would pass the test above while breaking every real invoice.
    /// </summary>
    [Fact]
    public void ADeclaredHsnStillPrintsVerbatim()
    {
        var (company, sale) = Build();
        var item = company.StockItems.Single(i => i.Name == "Widget");
        item.Gst = new StockItemGstDetails
        { HsnSac = "84713010", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800 };

        var row = Assert.Single(VoucherPrintProjector.ProjectInvoice(company, sale).Items);

        Assert.Equal("84713010", row.HsnSac);
    }
}
