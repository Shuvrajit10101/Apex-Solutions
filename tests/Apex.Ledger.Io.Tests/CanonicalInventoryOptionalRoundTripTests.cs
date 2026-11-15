using System.Text;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;

namespace Apex.Ledger.Io.Tests;

/// <summary>
/// Census 5.7 (schema v69) — an <b>Optional</b> stock/order voucher (Ctrl+L; help.tallysolutions.com/tally-prime/
/// accounting/accounting-entry-tally/: <i>"Any voucher in TallyPrime can be marked as Optional by pressing Ctrl+L
/// (Optional) during voucher entry or in alteration mode"</i>) round-trips through canonical JSON <b>and</b> XML
/// into a FRESH company, byte-stably, and the imported book moves no stock for it; a book with no Optional stock
/// voucher exports with no <c>optional</c> key on any inventory voucher (ER-13).
///
/// <para><b>Mutations:</b> dropping <c>Optional = v.Optional</c> from <c>CanonicalMapper.MapInventoryVoucher</c>,
/// or <c>imported.Optional = v.Optional</c> from <c>ImportPlan</c>, reddens
/// <see cref="An_optional_stock_voucher_roundTrips_through_json_and_xml_and_moves_no_stock"/>; dropping the XML
/// reader's <c>Optional = Bool(e, "optional")</c> reddens its XML half only.</para>
/// </summary>
public sealed class CanonicalInventoryOptionalRoundTripTests
{
    private static readonly DateOnly FyStart = new(2025, 4, 1);
    private static readonly DateOnly AsOf = new(2025, 9, 30);

    private static Company BuildBook(bool optional)
    {
        var c = CompanyFactory.CreateSeeded("Optional Stock Traders", FyStart);
        var masters = new InventoryService(c);
        var item = masters.CreateStockItem("Optional Widget", masters.CreateStockGroup("Optional Goods").Id,
            masters.CreateSimpleUnit("OpNos", "Optional Numbers").Id);
        masters.AddOpeningBalance(item.Id, c.MainLocation!.Id, 5m, Money.FromRupees(20.03m));
        var receiptNote = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.ReceiptNote);
        new InventoryPostingService(c).Post(new InventoryVoucher(Guid.NewGuid(), receiptNote.Id,
            new DateOnly(2025, 5, 2),
            new[] { new InventoryAllocation(item.Id, c.MainLocation!.Id, 11m, StockDirection.Inward,
                Money.FromRupees(21.07m)) },
            number: 0, narration: "provisional receipt", partyId: null, cancelled: false, postDated: false)
        { Optional = optional });
        return c;
    }

    [Fact]
    public void An_optional_stock_voucher_roundTrips_through_json_and_xml_and_moves_no_stock()
    {
        var c = BuildBook(optional: true);

        var json = CanonicalJson.Export(c);
        var (jsonModel, jsonErrors) = CanonicalJson.Parse(json);
        Assert.Empty(jsonErrors);
        Assert.Equal(json, CanonicalJson.Export(jsonModel!));   // byte-stable
        AssertOptionalSurvived(ImportInto(jsonModel!));

        var xml = CanonicalXml.Export(c);
        var (xmlModel, xmlErrors) = CanonicalXml.Parse(xml);
        Assert.Empty(xmlErrors);
        Assert.Equal(xml, CanonicalXml.Export(xmlModel!));      // byte-stable
        AssertOptionalSurvived(ImportInto(xmlModel!));
    }

    [Fact]
    public void A_book_without_an_optional_stock_voucher_emits_no_optional_key_on_any_inventory_voucher()
    {
        var plainXml = Encoding.UTF8.GetString(CanonicalXml.Export(BuildBook(optional: false)));
        var optXml = Encoding.UTF8.GetString(CanonicalXml.Export(BuildBook(optional: true)));
        static string Head(string xml) { var i = xml.IndexOf("<inventoryVoucher ", StringComparison.Ordinal);
            return xml[i..xml.IndexOf('>', i)]; }
        Assert.DoesNotContain("optional=", Head(plainXml), StringComparison.Ordinal);
        Assert.Contains("optional=\"true\"", Head(optXml), StringComparison.Ordinal);

        static int Keys(string s) => s.Split("\"optional\"").Length - 1;
        var plainJson = Encoding.UTF8.GetString(CanonicalJson.Export(BuildBook(optional: false)));
        var optJson = Encoding.UTF8.GetString(CanonicalJson.Export(BuildBook(optional: true)));
        Assert.Equal(Keys(plainJson) + 1, Keys(optJson));
    }

    private static Company ImportInto(CanonicalModel model)
    {
        var target = CompanyFactory.CreateSeeded("Fresh Optional Stock Co", FyStart);
        Assert.True(new CompanyImportService(target).Apply(model, DuplicatePolicy.Skip).Applied);
        return target;
    }

    private static void AssertOptionalSurvived(Company target)
    {
        var v = Assert.Single(target.InventoryVouchers);
        Assert.True(v.Optional);
        Assert.Equal("provisional receipt", v.Narration);
        var item = target.StockItems.Single(i => i.Name == "Optional Widget");
        // The imported Optional receipt moves no stock: only the opening 5 is on hand.
        Assert.Equal(5m, new InventoryLedger(target).OnHand(item.Id, target.MainLocation!.Id, AsOf));
    }
}
