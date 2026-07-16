using System.Text;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Domain = Apex.Ledger.Domain;

namespace Apex.Ledger.Io.Tests;

/// <summary>
/// Phase-9 slice-7a <b>Io fold-in</b> gate (PR-7 losslessness; RQ-23; ER-13): a company with a posted Rule-88A set-off
/// (the Alt+J stat-adjustment Journal + its Table-6.1 rows + the <c>gst_adjustment_kind</c> line tag), a PMT-06 challan
/// and a DRC-03 <b>exports and re-imports paisa + count exact in JSON AND XML</b>, both byte-stable, and into a fresh
/// (differently-Guid'd) company through the engine-routed CompanyImportService (every voucher FK re-mapped). Plus: the
/// import is <b>all-or-nothing</b> — a batch with a set-off row whose voucher_id is dangling rejects the WHOLE batch —
/// and a company that never sets off / pays / reverses carries <b>empty</b> S7a collections (ER-13).
/// </summary>
public sealed class CanonicalSetOffRoundTripTests
{
    private static readonly DateOnly Fy = new(2024, 4, 1);
    private static readonly DateOnly D = new(2024, 4, 20);

    private static Company BuildSetOffCompany()
    {
        var c = CompanyFactory.CreateSeeded("SetOff Io Co", Fy, Fy);
        var gst = new GstService(c);
        gst.EnableGst(new GstConfig
        {
            HomeStateCode = "27", Gstin = "27AAPFU0939F1ZV", RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = Fy, Periodicity = GstReturnPeriodicity.Monthly,
        });
        var bank = new Domain.Ledger(Guid.NewGuid(), "Bank", c.FindGroupByName("Bank Accounts")!.Id, Money.Zero, true);
        c.AddLedger(bank);

        var alloc = GstSetOffService.Allocate(new GstSetOffService.SetOffDemand(90000, 90000, 0, 0, 0, 90000, 90000, 0, 0));
        new GstSetOffService(c).PostSetOff("2024-04", alloc, D);

        var deposit = new GstDepositService(c);
        deposit.PostPmt06(GstTaxHead.Central, GstMinorHead.Tax, Money.FromRupees(500m), bank, D, "24CPIN00000001111", "CIN1", "BRN1");
        deposit.PostDrc03("Rule 37", "2024-04", D, 1000, 1000, 0, 0, 300, GstDepositService.PaymentMethod.Bank, bank,
            drc03aDemandRef: "AD-9");
        return c;
    }

    [Fact]
    public void Json_round_trips_byte_stable_with_setoff_challan_and_drc03()
    {
        var c = BuildSetOffCompany();
        var first = CanonicalJson.Export(c);
        var (model, errors) = CanonicalJson.Parse(first);
        Assert.Empty(errors);
        Assert.Equal(first, CanonicalJson.Export(model!)); // byte-for-byte
        AssertProjection(model!);
        AssertNoTally(first);
    }

    [Fact]
    public void Xml_round_trips_byte_stable_with_setoff_challan_and_drc03()
    {
        var c = BuildSetOffCompany();
        var first = CanonicalXml.Export(c);
        var (model, errors) = CanonicalXml.Parse(first);
        Assert.Empty(errors);
        Assert.Equal(first, CanonicalXml.Export(model!));
        AssertProjection(model!);
        AssertNoTally(first);
    }

    [Fact]
    public void Json_and_xml_carry_identical_setoff_payload()
    {
        var c = BuildSetOffCompany();
        var (jm, je) = CanonicalJson.Parse(CanonicalJson.Export(c));
        var (xm, xe) = CanonicalXml.Parse(CanonicalXml.Export(c));
        Assert.Empty(je);
        Assert.Empty(xe);
        Assert.Equal(CanonicalJson.Export(jm!), CanonicalJson.Export(xm!));
    }

    [Fact]
    public void Export_import_into_fresh_company_reconciles_setoff_challan_and_drc03_json_and_xml()
    {
        var source = BuildSetOffCompany();

        foreach (var bytes in new[] { CanonicalJson.Export(source), CanonicalXml.Export(source) })
        {
            var (model, errors) = bytes[0] == (byte)'{' ? CanonicalJson.Parse(bytes) : CanonicalXml.Parse(bytes);
            Assert.Empty(errors);

            var fresh = CompanyFactory.CreateSeeded("Fresh SetOff Co", Fy, Fy);
            var result = new CompanyImportService(fresh).Apply(model!);
            Assert.True(result.Applied, string.Join("; ", result.Errors));

            // The Alt+J stat-adjustment type + the two Table-6.1 rows + the gst_adjustment_kind tag reconcile.
            var statAdj = Assert.Single(fresh.VoucherTypes, t => t.IsGstStatAdjustmentType);
            Assert.Equal(VoucherBaseType.Journal, statAdj.BaseType);
            Assert.Equal(2, fresh.GstSetoffLines.Count);
            Assert.All(fresh.GstSetoffLines, l => Assert.Equal(l.CreditHead, l.LiabilityHead));
            var setoffVoucher = fresh.Vouchers.Single(v => v.TypeId == statAdj.Id);
            Assert.All(setoffVoucher.Lines, l => Assert.Equal(GstAdjustmentKind.SetOff, l.Gst!.Adjustment));

            // The challan re-mapped to the target's re-minted deposit voucher.
            var ch = Assert.Single(fresh.GstChallans);
            Assert.Equal("24CPIN00000001111", ch.Cpin);
            Assert.Equal(Money.FromRupees(500m), ch.Amount);
            Assert.NotNull(fresh.FindVoucher(ch.VoucherId));

            // The DRC-03 reconciles (cause, per-head + flag-only interest, DRC-03A link).
            var drc = Assert.Single(fresh.GstDrc03s);
            Assert.Equal("Rule 37", drc.Cause);
            Assert.Equal(2000, drc.TotalTaxPaisa);
            Assert.Equal(300, drc.InterestPaisa);
            Assert.Equal("AD-9", drc.Drc03aDemandRef);
        }
    }

    [Fact]
    public void A_batch_with_a_dangling_setoff_voucher_ref_rejects_the_whole_import()
    {
        var (model, errors) = CanonicalJson.Parse(CanonicalJson.Export(BuildSetOffCompany()));
        Assert.Empty(errors);

        // Tamper: point a set-off row at a voucher that is NOT in the batch. The record is the audit of a posted
        // voucher that is always co-exported, so a dangling ref is a corrupt batch ⇒ the WHOLE import rolls back.
        var bad = model! with
        {
            Payload = model.Payload with
            {
                GstSetoffLines = new[] { model.Payload.GstSetoffLines[0] with { VoucherId = Guid.NewGuid() } },
            },
        };

        var fresh = CompanyFactory.CreateSeeded("Reject Co", Fy, Fy);
        var result = new CompanyImportService(fresh).Apply(bad);
        Assert.False(result.Applied);
        // All-or-nothing: nothing from the batch landed (no set-off lines, no challans).
        Assert.Empty(fresh.GstSetoffLines);
        Assert.Empty(fresh.GstChallans);
    }

    [Fact]
    public void A_company_that_never_sets_off_carries_empty_s7a_collections()
    {
        // A plain GST company with no set-off / challan / DRC-03: the S7a collections are empty and the export
        // round-trips byte-stable (ER-13 — no S7a record content when the feature is unused).
        var c = CompanyFactory.CreateSeeded("Plain GST Co", Fy, Fy);
        new GstService(c).EnableGst(new GstConfig
        {
            HomeStateCode = "27", Gstin = "27AAPFU0939F1ZV", RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = Fy, Periodicity = GstReturnPeriodicity.Monthly,
        });

        var json = CanonicalJson.Export(c);
        var (model, errors) = CanonicalJson.Parse(json);
        Assert.Empty(errors);
        Assert.Empty(model!.Payload.GstSetoffLines);
        Assert.Empty(model.Payload.GstChallans);
        Assert.Empty(model.Payload.GstDrc03s);
        Assert.Empty(model.Payload.ItcReversals);
        Assert.Equal(json, CanonicalJson.Export(model)); // byte-stable
        // The empty collections carry no record content (only the empty container, exactly like every prior slice).
        var text = Encoding.UTF8.GetString(json);
        Assert.DoesNotContain("\"gstSetoffLine\"", text); // no element items, only the "gstSetoffLines": [] container
        Assert.DoesNotContain("\"cpin\"", text);
    }

    [Fact]
    public void Export_import_reconciles_posted_reversals_reclaim_and_credit_note_json_and_xml()
    {
        // Phase-9 slice-7b: a company with posted ITC reversals — a Rule-37 reversal (4B2), its reclaim (4D1, a
        // reclaim_of_id self-FK), and a credit-note reversal (4B1, the new CreditNote rule) — must export + re-import
        // into a fresh, differently-Guid'd company through the engine-routed importer, count + paisa exact, in JSON AND
        // XML, with the reclaim self-FK re-mapped through the import's reversal-id map.
        var (source, reversalId) = BuildReversalCompany();

        foreach (var bytes in new[] { CanonicalJson.Export(source), CanonicalXml.Export(source) })
        {
            var (model, errors) = bytes[0] == (byte)'{' ? CanonicalJson.Parse(bytes) : CanonicalXml.Parse(bytes);
            Assert.Empty(errors);
            AssertNoTally(bytes);
            Assert.Equal(3, model!.Payload.ItcReversals.Count);

            var fresh = CompanyFactory.CreateSeeded("Fresh Reversal Co", Fy, Fy);
            var result = new CompanyImportService(fresh).Apply(model);
            Assert.True(result.Applied, string.Join("; ", result.Errors));

            Assert.Equal(3, fresh.ItcReversals.Count);
            var rev = Assert.Single(fresh.ItcReversals, x => x.Rule == ItcReversalRule.Rule37 && x.ReclaimOfId is null);
            Assert.Equal(Table4bBucket.Table4B2, rev.Table4bBucket);
            Assert.Equal(1_800_000, rev.IgstPaisa);

            var rec = Assert.Single(fresh.ItcReversals, x => x.ReclaimOfId is not null);
            Assert.Equal(rev.Id, rec.ReclaimOfId);                 // the reclaim self-FK re-mapped to the re-minted reversal
            Assert.Equal(Table4bBucket.Table4D1, rec.Table4bBucket);

            var note = Assert.Single(fresh.ItcReversals, x => x.Rule == ItcReversalRule.CreditNote);
            Assert.Equal(Table4bBucket.Table4B1, note.Table4bBucket); // the new CreditNote rule name round-tripped
            Assert.Equal(30_000, note.IgstPaisa);
        }

        _ = reversalId; // (the source id differs from the re-minted target id — the reclaim link is what must reconcile)
    }

    private static (Company Company, Guid ReversalId) BuildReversalCompany()
    {
        var c = CompanyFactory.CreateSeeded("Reversal Io Co", Fy, Fy);
        var gst = new GstService(c);
        gst.EnableGst(new GstConfig
        {
            HomeStateCode = "27", Gstin = "27AAPFU0939F1ZV", RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = Fy, Periodicity = GstReturnPeriodicity.Monthly,
        });
        var bank = new Domain.Ledger(Guid.NewGuid(), "Bank", c.FindGroupByName("Bank Accounts")!.Id, Money.Zero, true);
        c.AddLedger(bank);
        var purchases = new Domain.Ledger(Guid.NewGuid(), "Purchases", c.FindGroupByName("Purchase Accounts")!.Id, Money.Zero, true);
        c.AddLedger(purchases);

        var tax = gst.ComputeInvoiceTax(
            new[] { new GstService.TaxableLine(new Money(100000m), 1800) }, interState: true, GstTaxDirection.Input);
        var lines = new List<EntryLine>
        {
            new(purchases.Id, new Money(100000m), DrCr.Debit),
            new(bank.Id, new Money(100000m + tax.TotalTax.Amount), DrCr.Credit),
        };
        lines.AddRange(tax.TaxLines);
        var vid = new LedgerService(c).Post(new Voucher(
            Guid.NewGuid(), c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase).Id, D, lines)).Id;

        var svc = new GstReversalService(c);
        var reversal = svc.PostRule37(vid, "2024-04", D);
        svc.Reclaim(reversal.Id, "2024-09", D);
        svc.PostReversal(ItcReversalRule.CreditNote, "2024-10",
            new GstReversalService.ReversalAmount(0, 0, 30_000, 0), D, sourceVoucherId: vid);
        return (c, reversal.Id);
    }

    private static void AssertProjection(CanonicalModel model)
    {
        Assert.Equal(2, model.Payload.GstSetoffLines.Count);
        Assert.All(model.Payload.GstSetoffLines, l => Assert.True(l.AmountPaisa > 0));
        var ch = Assert.Single(model.Payload.GstChallans);
        Assert.Equal(50_000L, ch.AmountPaisa);          // ₹500 = 50,000 paisa
        Assert.Equal("24CPIN00000001111", ch.Cpin);
        var drc = Assert.Single(model.Payload.GstDrc03s);
        Assert.Equal(300L, drc.InterestPaisa);
        Assert.Contains(model.Payload.VoucherTypes, t => t.IsGstStatAdjustment);
    }

    private static void AssertNoTally(byte[] bytes) =>
        Assert.DoesNotContain("tally", Encoding.UTF8.GetString(bytes), StringComparison.OrdinalIgnoreCase);
}
