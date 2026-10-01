using System.IO;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Desktop.Tests;

/// <summary>
/// 🔴 <b>The "the portal will reject it" pre-flight reassured without validating.</b>
///
/// <para>The Generate e-Way Bill screen writes the EWB-01 and then tells the operator, in its own banner,
/// either "Upload it to the portal, then record the EWB number it returns." or which members the portal will
/// reject. It built that verdict from <c>EWayBillJson.MissingMandatory</c> alone, which only ever asks whether
/// a value EXISTS — it checked nine members that have a domain source and <b>no FORMAT constraint at all</b>.
/// So a six-character vehicle number, which NIC's own schema refuses outright
/// (<c>"vehicleNo": { "minLength": 7 }</c>, verified by content on the published generate-eway-bill page),
/// produced a payload the operator was told to upload. A pre-flight that reassures without validating is worse
/// than none: the operator stops checking.</para>
///
/// <para>These drive the REAL view model through the REAL Ctrl+A entry point
/// (<c>PrepareAndWriteJson</c>, which <c>MainWindowViewModel</c> dispatches Ctrl+A to at the
/// <c>Screen.GenerateEWayBill</c> case) and assert the banner the screen actually renders — it is bound at
/// <c>MainWindow.axaml</c> inside the <c>GenerateEWayBillViewModel</c> DataTemplate with
/// <c>TextWrapping="Wrap"</c>, so a longer banner cannot clip.</para>
/// </summary>
public class GenerateEWayBillPreFlightTests
{
    private static readonly DateOnly FyStart = new(2026, 4, 1);
    private static readonly DateOnly SaleDate = new(2026, 9, 10);
    private const string GstinMaharashtra = "27AAPFU0939F1ZV";

    /// <summary>🔴 THE DEFECT. A malformed-but-present vehicle number must be refused, by name, in the banner.</summary>
    [Fact]
    public void A_vehicle_number_the_schema_refuses_is_named_in_the_banner()
    {
        var vm = Screen(vehicleNumber: "MH1AB1");   // 6 characters; the schema needs 7..15

        Assert.True(vm.PrepareAndWriteJson(), vm.Message);

        Assert.Contains("The portal will reject it", vm.Message!, StringComparison.Ordinal);
        Assert.Contains("vehicleNo", vm.Message!, StringComparison.Ordinal);
        // 🔴 And it must NOT still be telling the operator to upload it. This is the half that made the old
        // banner actively harmful rather than merely incomplete.
        Assert.DoesNotContain("Upload it to the portal", vm.Message!, StringComparison.Ordinal);
    }

    /// <summary>The mirror: a well-formed consignment still gets the plain go-ahead, so the pre-flight cannot cry
    /// wolf on the ordinary path and train the operator to ignore it.</summary>
    [Fact]
    public void A_well_formed_consignment_is_still_told_to_upload()
    {
        var vm = Screen();

        Assert.True(vm.PrepareAndWriteJson(), vm.Message);

        Assert.Contains("Upload it to the portal", vm.Message!, StringComparison.Ordinal);
        Assert.DoesNotContain("The portal will reject it", vm.Message!, StringComparison.Ordinal);
    }

    /// <summary>A distance past NIC's stated maximum is refused too. The published page is internally
    /// inconsistent — the schema description reads "Distance (&lt;4000 km)" while the Data Structure table reads
    /// "Max Value = 4000" — so only STRICTLY above 4000 is refused, the reading that cannot refuse a legal bill.</summary>
    [Fact]
    public void A_distance_past_the_schema_maximum_is_named_in_the_banner()
    {
        var vm = Screen(distanceKm: "4001");

        Assert.True(vm.PrepareAndWriteJson(), vm.Message);
        Assert.Contains("transDistance", vm.Message!, StringComparison.Ordinal);
        Assert.DoesNotContain("Upload it to the portal", vm.Message!, StringComparison.Ordinal);
    }

    // ================================================================ fixture

    /// <summary>The screen over a book holding one covered intra-State goods movement, with Part-B keyed in and
    /// the first row highlighted — the state the operator is in when he presses Ctrl+A. Diskless: the write seam
    /// is injected, so nothing touches the filesystem.</summary>
    private static GenerateEWayBillViewModel Screen(
        string vehicleNumber = "MH12AB1234", string distanceKm = "250")
    {
        var company = BuildBook();
        // A per-test temp directory, never the real Documents default: CompanyStorage creates its directory in
        // the constructor and PrepareAndWriteJson calls Save, so the default would write into the user's book store.
        var dir = Path.Combine(Path.GetTempPath(), "apex-a5-preflight-" + Guid.NewGuid().ToString("N"));
        var vm = new GenerateEWayBillViewModel(
            company, new CompanyStorage(dir), onChanged: null,
            folder: "C:/nowhere", today: SaleDate, now: () => new DateTimeOffset(SaleDate.ToDateTime(TimeOnly.MinValue)),
            writeBytes: (_, _) => { });

        Assert.NotEmpty(vm.Rows);           // the movement is covered and reachable on the screen
        vm.HighlightedIndex = 0;
        vm.VehicleNumber = vehicleNumber;
        vm.DistanceKmText = distanceKm;
        vm.ShipToGstin = GstinMaharashtra;  // mandatory for a voucher dated on/after 01-Aug-2026
        return vm;
    }

    private static Company BuildBook()
    {
        var c = CompanyFactory.CreateSeeded("Pre-flight Co", FyStart);
        c.Address = "Unit 4, Fort Industrial Estate\nBallard Pier";
        c.Pin = "400001";

        var gst = new GstService(c);
        gst.EnableGst(new GstConfig
        {
            HomeStateCode = "27", Gstin = GstinMaharashtra,
            RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = FyStart, Periodicity = GstReturnPeriodicity.Monthly,
            EWayBillEnabled = true, EWayApplicableFrom = FyStart,
        });

        var inv = new InventoryService(c);
        var grp = inv.CreateStockGroup("Goods");
        var nos = inv.CreateSimpleUnit("Nos", "Numbers", unitQuantityCode: "NOS");
        var item = inv.CreateStockItem("Widget", grp.Id, nos.Id);
        item.Gst = new StockItemGstDetails
        {
            HsnSac = "847130", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800,
        };
        var main = c.MainLocation!.Id;
        inv.AddOpeningBalance(item.Id, main, 1000m, Money.FromRupees(20m));

        var sales = Add(c, "Sales", "Sales Accounts", false);
        var buyer = Add(c, "Buyer", "Sundry Debtors", true);
        buyer.PartyGst = new PartyGstDetails
        {
            RegistrationType = GstRegistrationType.Regular, Gstin = GstinMaharashtra, StateCode = "27",
        };
        buyer.Mailing = new PartyMailingDetails
        {
            MailingName = "Buyer Trading Co",
            Address = "12 Residency Road\nShivajinagar",
            Country = "India",
            Pincode = "400012",
        };

        // ₹50,000 @ 18% ⇒ a consignment value of ₹59,000, which STRICTLY exceeds the ₹50,000 threshold, so the
        // movement is Rule-138 covered and the screen lists it.
        var tax = gst.ComputeInvoiceTax(
            new[] { new GstService.TaxableLine(Money.FromRupees(50_000m), 1800) },
            interState: false, GstTaxDirection.Output);

        var lines = new List<EntryLine>
        {
            new(buyer.Id, new Money(50_000m + tax.TaxLines.Sum(l => l.Amount.Amount)), DrCr.Debit),
            new(sales.Id, new Money(50_000m), DrCr.Credit),
        };
        lines.AddRange(tax.TaxLines);

        new LedgerService(c).Post(new Voucher(Guid.NewGuid(),
            c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id, SaleDate, lines, partyId: buyer.Id,
            inventoryLines: new[] { new VoucherInventoryLine(item.Id, main, 2500m, Money.FromRupees(20m)) }));

        return c;
    }

    private static Ledger.Domain.Ledger Add(Company c, string name, string groupName, bool openingIsDebit)
    {
        var l = new Ledger.Domain.Ledger(
            Guid.NewGuid(), name, c.FindGroupByName(groupName)!.Id, Money.Zero, openingIsDebit);
        c.AddLedger(l);
        return l;
    }
}
