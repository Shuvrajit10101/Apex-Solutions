using System.Text;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;

namespace Apex.Ledger.Io.Tests;

/// <summary>
/// WI-10 Gap 2 (schema v46) — the <b>item-invoice line unit</b> Io fold-in gate. An item-invoice line entered
/// in a compound unit ("2 Doz @ ₹10") exports and re-imports exact in JSON <i>and</i> XML, byte-stably, and
/// into a fresh (differently-Guid'd) company through the engine-routed <see cref="CompanyImportService"/> —
/// where the unit must be RE-MAPPED onto the target company's own unit, not carried as a dangling id.
///
/// <para><b>ER-13 is the sharp edge here.</b> The canonical JSON writer is configured
/// <c>DefaultIgnoreCondition = Never</c>, so a naively-added <c>UnitId</c> property would emit
/// <c>"unitId": null</c> on <i>every item line of every existing company</i> and change the bytes of exports
/// that have nothing to do with this feature.
/// <see cref="Er13_an_invoice_with_no_line_unit_exports_byte_identically"/> pins that.</para>
///
/// <para>The <b>money</b> is asserted on both sides of the round trip: the line's value is ₹20, per the LINE
/// unit — an Io layer that "helpfully" normalised the quantity or the rate would silently move it by the
/// conversion factor and the import would post a different invoice than the one exported.</para>
/// </summary>
public sealed class CanonicalItemLineUnitRoundTripTests
{
    private static readonly DateOnly FyStart = new(2025, 4, 1);

    /// <summary>A company holding one Nos-measured "Egg", a Doz-Nos compound unit, and a sale of 2 Doz @ ₹10.</summary>
    private static Company BuildCompanyWithUnitCarryingInvoice()
    {
        var c = Seed("Dozen Traders");

        var egg = c.FindStockItemByName("Egg")!;
        var dozNos = c.FindUnitByName("Doz-Nos")!;
        var customer = new Domain.Ledger(
            Guid.NewGuid(), "Naresh Traders", c.FindGroupByName("Sundry Debtors")!.Id, Money.Zero, true);
        var sales = new Domain.Ledger(
            Guid.NewGuid(), "Sales", c.FindGroupByName("Sales Accounts")!.Id, Money.Zero, false);
        c.AddLedger(customer);
        c.AddLedger(sales);

        new LedgerService(c).Post(new Voucher(
            Guid.NewGuid(),
            c.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Sales && t.IsActive).Id,
            FyStart,
            new[]
            {
                new EntryLine(customer.Id, Money.FromRupees(20m), DrCr.Debit),
                new EntryLine(sales.Id, Money.FromRupees(20m), DrCr.Credit),
            },
            partyId: customer.Id,
            inventoryLines: new[]
            {
                new VoucherInventoryLine(egg.Id, c.MainLocation!.Id, 2m, Money.FromRupees(10m),
                    StockDirection.Outward, null, null, dozNos.Id),
            }));

        return c;
    }

    private static Company Seed(string name)
    {
        var c = CompanyFactory.CreateSeeded(name, FyStart);
        var inv = new InventoryService(c);
        var grp = inv.CreateStockGroup("Goods");
        var nos = inv.CreateSimpleUnit("Nos", "Numbers");
        var doz = inv.CreateSimpleUnit("Doz", "Dozens");
        inv.CreateCompoundUnit("Doz-Nos", "Dozen of 12 Numbers", doz.Id, nos.Id, 12);
        var egg = inv.CreateStockItem("Egg", grp.Id, nos.Id);
        inv.AddOpeningBalance(egg.Id, c.MainLocation!.Id, 240m, Money.FromRupees(1m));
        return c;
    }

    private static Company Fresh() => Seed("Fresh Dozen Co");

    // ================================================================= lossless round-trip

    [Fact]
    public void Json_round_trips_byte_stable_and_lossless()
    {
        var c = BuildCompanyWithUnitCarryingInvoice();
        var first = CanonicalJson.Export(c);

        var (model, errors) = CanonicalJson.Parse(first);
        Assert.Empty(errors);
        Assert.NotNull(model);
        Assert.Equal(first, CanonicalJson.Export(model!));   // byte-stable

        var target = Fresh();
        Assert.True(new CompanyImportService(target).Apply(model!, DuplicatePolicy.Skip).Applied);
        AssertLineUnitSurvived(target);

        Assert.DoesNotContain("Tally", Encoding.UTF8.GetString(first), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Xml_round_trips_byte_stable_and_lossless()
    {
        var c = BuildCompanyWithUnitCarryingInvoice();
        var first = CanonicalXml.Export(c);

        var (model, errors) = CanonicalXml.Parse(first);
        Assert.Empty(errors);
        Assert.NotNull(model);
        Assert.Equal(first, CanonicalXml.Export(model!));     // byte-stable

        var target = Fresh();
        Assert.True(new CompanyImportService(target).Apply(model!, DuplicatePolicy.Skip).Applied);
        AssertLineUnitSurvived(target);
    }

    [Fact]
    public void Json_and_Xml_carry_the_identical_payload()
    {
        // The two writers must agree — a fold-in that reached one serialiser and not the other would make an
        // export's fidelity depend on which button the user pressed.
        var c = BuildCompanyWithUnitCarryingInvoice();

        var (jsonModel, jsonErrors) = CanonicalJson.Parse(CanonicalJson.Export(c));
        var (xmlModel, xmlErrors) = CanonicalXml.Parse(CanonicalXml.Export(c));
        Assert.Empty(jsonErrors);
        Assert.Empty(xmlErrors);

        var fromJson = Fresh();
        var fromXml = Fresh();
        Assert.True(new CompanyImportService(fromJson).Apply(jsonModel!, DuplicatePolicy.Skip).Applied);
        Assert.True(new CompanyImportService(fromXml).Apply(xmlModel!, DuplicatePolicy.Skip).Applied);

        AssertLineUnitSurvived(fromJson);
        AssertLineUnitSurvived(fromXml);
    }

    /// <summary>
    /// The imported line still carries a line unit — RE-MAPPED onto the TARGET company's own Doz-Nos, not the
    /// source company's id — and the money and the stock effect are both unchanged: ₹20 billed, 24 Nos moved.
    /// </summary>
    private static void AssertLineUnitSurvived(Company target)
    {
        var salesType = target.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Sales && t.IsActive);
        var voucher = target.Vouchers.Single(v => v.TypeId == salesType.Id);
        var il = Assert.Single(voucher.InventoryLines);

        var targetDozNos = target.FindUnitByName("Doz-Nos")!;
        Assert.Equal(targetDozNos.Id, il.UnitId);

        Assert.Equal(2m, il.Quantity);
        Assert.Equal(Money.FromRupees(10m), il.Rate);
        Assert.Equal(Money.FromRupees(20m), il.Value);

        // The stock effect the imported document produces is the same one the source produced: 240 − 24.
        var egg = target.FindStockItemByName("Egg")!;
        Assert.Equal(240m - 24m,
            new InventoryLedger(target).OnHand(egg.Id, target.MainLocation!.Id, FyStart.AddYears(1)));
    }

    // ================================================================= ER-13

    [Fact]
    public void Er13_an_invoice_with_no_line_unit_exports_byte_identically()
    {
        // A company whose item lines carry no unit must serialise EXACTLY as it did before v46 — no
        // "unitId": null key in JSON, no unitId="" attribute in XML. Without the WhenWritingNull attribute on
        // VoucherInventoryLineDto.UnitId, the JSON assertion below fails on every item line ever exported.
        var c = Seed("No Unit Co");
        var egg = c.FindStockItemByName("Egg")!;
        var customer = new Domain.Ledger(
            Guid.NewGuid(), "Plain Party", c.FindGroupByName("Sundry Debtors")!.Id, Money.Zero, true);
        var sales = new Domain.Ledger(
            Guid.NewGuid(), "Sales", c.FindGroupByName("Sales Accounts")!.Id, Money.Zero, false);
        c.AddLedger(customer);
        c.AddLedger(sales);

        new LedgerService(c).Post(new Voucher(
            Guid.NewGuid(),
            c.VoucherTypes.Single(t => t.BaseType == VoucherBaseType.Sales && t.IsActive).Id,
            FyStart,
            new[]
            {
                new EntryLine(customer.Id, Money.FromRupees(240m), DrCr.Debit),
                new EntryLine(sales.Id, Money.FromRupees(240m), DrCr.Credit),
            },
            partyId: customer.Id,
            inventoryLines: new[]
            {
                new VoucherInventoryLine(egg.Id, c.MainLocation!.Id, 24m, Money.FromRupees(10m),
                    StockDirection.Outward),
            }));

        var json = Encoding.UTF8.GetString(CanonicalJson.Export(c));
        var xml = Encoding.UTF8.GetString(CanonicalXml.Export(c));

        Assert.DoesNotContain("\"unitId\":", json, StringComparison.Ordinal);
        Assert.DoesNotContain("unitId=", xml, StringComparison.Ordinal);

        // Belt-and-braces: a company that DOES carry a line unit emits the key, so the assertions above are
        // detecting a real absence rather than passing because the writer never emits it at all.
        var withUnit = Encoding.UTF8.GetString(CanonicalJson.Export(BuildCompanyWithUnitCarryingInvoice()));
        var withUnitXml = Encoding.UTF8.GetString(CanonicalXml.Export(BuildCompanyWithUnitCarryingInvoice()));
        Assert.Contains("\"unitId\":", withUnit, StringComparison.Ordinal);
        Assert.Contains("unitId=", withUnitXml, StringComparison.Ordinal);
    }
}
