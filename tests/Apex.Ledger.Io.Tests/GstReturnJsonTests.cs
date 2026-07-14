using System.Text;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;

namespace Apex.Ledger.Io.Tests;

/// <summary>
/// Phase-9 slice-3 offline-JSON writer gate (RQ-16; R7). The composition CMP-08 / GSTR-4 offline JSON is deterministic
/// (identical bytes on repeat), carries the government envelope (<c>gstin</c> + <c>fp</c>), reports money as integer
/// paisa (ER-10), and is de-branded (no "Tally"). The exact GSTN offline-tool JSON keys are A14-gated (flagged via
/// <c>schemaStatus</c>) — the projection records are correct regardless.
/// </summary>
public sealed class GstReturnJsonTests
{
    private const string GstinMaharashtra = "27AAPFU0939F1ZV";
    private static readonly DateOnly FyStart = new(2025, 4, 1);
    private static readonly DateOnly Q1From = new(2025, 4, 1);
    private static readonly DateOnly Q1To = new(2025, 6, 30);
    private static readonly DateOnly FyEnd = new(2026, 3, 31);

    private static Company BuildComposition()
    {
        var c = CompanyFactory.CreateSeeded("Composition Co", FyStart);
        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = "27", Gstin = GstinMaharashtra, RegistrationType = GstRegistrationType.Composition,
            CompositionSubType = CompositionSubType.Trader, ApplicableFrom = FyStart, Periodicity = GstReturnPeriodicity.Quarterly,
        });
        var sales = new Domain.Ledger(Guid.NewGuid(), "Sales", c.FindGroupByName("Sales Accounts")!.Id, Money.Zero, false)
        {
            SalesPurchaseGst = new StockItemGstDetails { Taxability = GstTaxability.Taxable, RateBasisPoints = 1800 },
        };
        c.AddLedger(sales);
        var party = new Domain.Ledger(Guid.NewGuid(), "Walk-in", c.FindGroupByName("Sundry Debtors")!.Id, Money.Zero, true);
        c.AddLedger(party);
        new LedgerService(c).Post(new Voucher(Guid.NewGuid(), c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id, new DateOnly(2025, 4, 5), new[]
        {
            new EntryLine(party.Id, Money.FromRupees(100001m), DrCr.Debit),
            new EntryLine(sales.Id, Money.FromRupees(100001m), DrCr.Credit),
        }, partyId: party.Id));
        return c;
    }

    [Fact]
    public void Cmp08_json_is_deterministic_and_carries_the_envelope()
    {
        var c = BuildComposition();
        var a = GstReturnJson.Cmp08(c, Q1From, Q1To);
        var b = GstReturnJson.Cmp08(c, Q1From, Q1To);
        Assert.Equal(a, b); // deterministic

        var json = Encoding.UTF8.GetString(a);
        Assert.Contains("\"gstin\": \"27AAPFU0939F1ZV\"", json);
        Assert.Contains("\"fp\": \"062025\"", json);                  // MMYYYY, quarter end
        Assert.Contains("\"tbl3i_out_cgst_paisa\": 50001", json);     // CGST 500.01 → paisa
        Assert.Contains("\"tbl3i_out_sgst_paisa\": 50000", json);     // SGST 500.00 → paisa
        Assert.DoesNotContain("Tally", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Gstr4_json_is_deterministic_and_rolls_up_four_quarters()
    {
        var c = BuildComposition();
        var a = GstReturnJson.Gstr4(c, FyStart, FyEnd);
        Assert.Equal(a, GstReturnJson.Gstr4(c, FyStart, FyEnd));

        var json = Encoding.UTF8.GetString(a);
        Assert.Contains("\"gstin\": \"27AAPFU0939F1ZV\"", json);
        Assert.Contains("\"fp\": \"032026\"", json);                  // FY end month
        Assert.Contains("\"tbl5_quarters\"", json);
        Assert.Contains("\"tbl6_annual_comp_tax_paisa\": 100001", json); // 1000.01 → paisa
        Assert.DoesNotContain("Tally", json, StringComparison.OrdinalIgnoreCase);
    }
}
