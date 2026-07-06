using System;
using System.Collections.Generic;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Io;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// PR-4 HARD GATE — the canonical lossless round-trip through <see cref="CompanyImportService"/> (engine-routed
/// apply). A rich "Bright"-style trading company (seeded + custom ledgers with opening balances, GST enabled with
/// its tax ledgers + a GSTIN party, inventory masters + opening stock, an accounting voucher, and an item-invoice
/// Purchase with a hand-built GST line) is exported to the canonical envelope (JSON and XML) and imported into a
/// FRESH, differently-Guid'd company. The gate asserts the fresh company reconciles to the source <b>to the
/// paisa</b> across Trial Balance, Balance Sheet, P&amp;L, Stock Summary and the GST returns (GSTR-1 + GSTR-3B),
/// and that master/voucher counts match. A deliberately-corrupted import (an unbalanced voucher; a voucher
/// referencing a missing ledger) is rejected with a clear message and leaves the target UNCHANGED. Duplicate-policy
/// behaviours are pinned too.
/// </summary>
public class CompanyImportRoundTripTests
{
    private static readonly DateOnly AsOf = new(2022, 3, 31);
    private static readonly DateOnly From = new(2021, 4, 1);

    // ------------------------------------------------------------------ PR-4 (a): JSON lossless round-trip

    [Fact]
    public void Bright_json_round_trip_reconciles_to_the_paisa_in_a_fresh_company()
    {
        var source = BuildRichCompany();
        var bytes = CanonicalJson.Export(source);

        var (model, errors) = CanonicalJson.Parse(bytes);
        Assert.Empty(errors);
        Assert.NotNull(model);

        var fresh = CompanyFactory.CreateSeeded("Import Target", From, From);
        var result = new CompanyImportService(fresh).Apply(model!, DuplicatePolicy.Skip);

        Assert.True(result.Applied, string.Join(" | ", result.Errors));
        AssertReconciles(source, fresh);
    }

    /// <summary>
    /// Regression pin for the reserved Profit &amp; Loss head duplication defect: exporting a company and importing
    /// into a FRESH seeded target must reproduce the EXACT same group count. The seeded target already carries the
    /// 28 predefined groups + the reserved P&amp;L head (stored outside the 28); the old bug re-created that head as a
    /// 29th real group, yielding 29 in <see cref="Company.Groups"/>. The fix reuses it by name.
    /// </summary>
    [Fact]
    public void Round_trip_into_fresh_target_reuses_the_reserved_pnl_head_and_keeps_the_group_count_exact()
    {
        var source = BuildRichCompany();
        var (model, errors) = CanonicalJson.Parse(CanonicalJson.Export(source));
        Assert.Empty(errors);

        // The source is a plain seeded company: 28 predefined groups + the reserved P&L head.
        Assert.Equal(28, source.Groups.Count);
        Assert.NotNull(source.ProfitAndLossHead);

        var fresh = CompanyFactory.CreateSeeded("Import Target", From, From);
        Assert.Equal(28, fresh.Groups.Count); // seeded baseline before import

        var result = new CompanyImportService(fresh).Apply(model!, DuplicatePolicy.Skip);
        Assert.True(result.Applied, string.Join(" | ", result.Errors));

        // EXACT same group count — no 29th duplicate P&L group leaked in.
        Assert.Equal(source.Groups.Count, fresh.Groups.Count);
        Assert.Equal(28, fresh.Groups.Count);
        Assert.NotNull(fresh.ProfitAndLossHead);
        // The head is the seeded instance, still outside the 28 (never re-created into Groups).
        Assert.DoesNotContain(fresh.Groups, g =>
            string.Equals(g.Name, "Profit & Loss A/c", StringComparison.OrdinalIgnoreCase));
    }

    // ------------------------------------------------------------------ PR-4 (b): XML lossless round-trip

    [Fact]
    public void Bright_xml_round_trip_reconciles_to_the_paisa_in_a_fresh_company()
    {
        var source = BuildRichCompany();
        var bytes = CanonicalXml.Export(source);

        var (model, errors) = CanonicalXml.Parse(bytes);
        Assert.Empty(errors);
        Assert.NotNull(model);

        var fresh = CompanyFactory.CreateSeeded("Import Target", From, From);
        var result = new CompanyImportService(fresh).Apply(model!, DuplicatePolicy.Skip);

        Assert.True(result.Applied, string.Join(" | ", result.Errors));
        AssertReconciles(source, fresh);
    }

    [Fact]
    public void Json_and_xml_produce_identical_fresh_companies()
    {
        var source = BuildRichCompany();

        var (jm, je) = CanonicalJson.Parse(CanonicalJson.Export(source));
        var (xm, xe) = CanonicalXml.Parse(CanonicalXml.Export(source));
        Assert.Empty(je);
        Assert.Empty(xe);

        var fromJson = CompanyFactory.CreateSeeded("J", From, From);
        var fromXml = CompanyFactory.CreateSeeded("X", From, From);
        Assert.True(new CompanyImportService(fromJson).Apply(jm!, DuplicatePolicy.Skip).Applied);
        Assert.True(new CompanyImportService(fromXml).Apply(xm!, DuplicatePolicy.Skip).Applied);

        // Both fresh companies reconcile against the same source ⇒ they reconcile against each other.
        AssertReconciles(fromJson, fromXml);
    }

    // ------------------------------------------------------------------ PR-4 (c): corrupted imports are rejected

    [Fact]
    public void Unbalanced_voucher_is_rejected_and_target_is_unchanged()
    {
        var source = BuildRichCompany();
        var (model, _) = CanonicalJson.Parse(CanonicalJson.Export(source));

        // Corrupt one voucher: bump a single debit line by 1 paisa so Σ Dr != Σ Cr.
        var corrupted = CorruptVoucher(model!, v => v with
        {
            Lines = BumpFirstDebit(v.Lines, +1),
        });

        var fresh = CompanyFactory.CreateSeeded("Import Target", From, From);
        var before = Snapshot(fresh);

        var result = new CompanyImportService(fresh).Apply(corrupted, DuplicatePolicy.Skip);

        Assert.False(result.Applied);
        Assert.Contains(result.Errors, e => e.Contains("unbalanced", StringComparison.OrdinalIgnoreCase));
        AssertUnchanged(before, fresh);
    }

    [Fact]
    public void Voucher_referencing_a_missing_ledger_is_rejected_and_target_is_unchanged()
    {
        var source = BuildRichCompany();
        var (model, _) = CanonicalJson.Parse(CanonicalJson.Export(source));

        // Point one line at a ledger id that is neither imported nor present in the target.
        var ghost = Guid.NewGuid();
        var corrupted = CorruptVoucher(model!, v => v with
        {
            Lines = v.Lines.Select((l, i) => i == 0 ? l with { LedgerId = ghost } : l).ToList(),
        });

        var fresh = CompanyFactory.CreateSeeded("Import Target", From, From);
        var before = Snapshot(fresh);

        var result = new CompanyImportService(fresh).Apply(corrupted, DuplicatePolicy.Skip);

        Assert.False(result.Applied);
        Assert.Contains(result.Errors, e => e.Contains("ledger", StringComparison.OrdinalIgnoreCase));
        AssertUnchanged(before, fresh);
    }

    [Fact]
    public void Nothing_is_applied_when_any_record_is_invalid()
    {
        var source = BuildRichCompany();
        var (model, _) = CanonicalJson.Parse(CanonicalJson.Export(source));
        var corrupted = CorruptVoucher(model!, v => v with { Lines = BumpFirstDebit(v.Lines, +5) });

        var fresh = CompanyFactory.CreateSeeded("Import Target", From, From);
        var result = new CompanyImportService(fresh).Apply(corrupted, DuplicatePolicy.Skip);

        Assert.False(result.Applied);
        // Not one master leaked in: the fresh company still has ONLY its seeded ledgers (no "Bright's Capital").
        Assert.Null(fresh.FindLedgerByName("Bright's Capital"));
        Assert.Null(fresh.FindStockItemByName("Widget"));
        Assert.Empty(fresh.Vouchers);
    }

    // --------------------------- PR-4 (c2): a mid-APPLY engine rejection into a NON-EMPTY, GST-on company rolls
    //                                         back EVERYTHING it touched and leaves the pre-existing data 100% intact.

    /// <summary>
    /// The critical rollback-correctness gate. Unlike the pre-flight-rejected corruptions above (which never mutate
    /// the target), this batch is fully well-formed at pre-flight — it resolves, balances, and pairs — so
    /// <see cref="ImportPlan.Execute"/> DOES run and DOES mutate the company (company header, then the import's
    /// masters + vouchers) before the engine rejects the very last voucher: an over-delivery Delivery Note whose
    /// no-negative-stock guard (DP-7) throws only at post time. The import must therefore exercise the true
    /// <see cref="ApplyJournal.Rollback"/> path. The target it imports into is NON-EMPTY and already has GST enabled
    /// (its 6 auto-created tax ledgers + Round Off + a live GST config + custom ledgers with opening balances). The
    /// gate asserts that after the rolled-back import <b>none</b> of that pre-existing data moved: the GST config is
    /// the same instance, every tax ledger + Round Off still exists with its original group/opening, the custom
    /// ledgers keep their openings, and every master/voucher count is exactly what it was before.
    /// </summary>
    [Fact]
    public void Failed_apply_into_a_nonempty_gst_company_leaves_all_preexisting_data_intact()
    {
        // A fully-populated, GST-enabled target — the same rich company the round-trip uses.
        var target = BuildRichCompany();
        var gstOnTarget = new GstService(target);

        // Capture the pre-existing state we must not disturb.
        var priorGstConfig = target.Gst;                    // exact instance
        var priorGstin = target.Gst!.Gstin;
        var cgstOut = gstOnTarget.FindTaxLedger(GstTaxHead.Central, GstTaxDirection.Output)!;
        var sgstIn = gstOnTarget.FindTaxLedger(GstTaxHead.State, GstTaxDirection.Input)!;
        var roundOff = target.FindLedgerByName(GstService.RoundOffLedgerName)!;
        var priorRoundOffGroup = roundOff.GroupId;
        var capital = target.FindLedgerByName("Bright's Capital")!;
        var priorCapitalOpening = capital.SignedOpening;
        var priorLedgerIds = target.Ledgers.Select(l => l.Id).OrderBy(g => g).ToList();
        var before = Snapshot(target);
        var priorVoucherIds = target.Vouchers.Select(v => v.Id).OrderBy(g => g).ToList();
        var priorInvVoucherCount = target.InventoryVouchers.Count;

        // Build a NEW, self-contained import batch (fresh Guids, no collision with the target's masters) that adds a
        // brand-new item with a small opening, then over-DELIVERS it. Everything resolves & balances at pre-flight,
        // so Execute runs and mutates the company; the Delivery Note's outward over-draw is only caught by the engine.
        var model = BuildOverDeliveryBatch();

        var result = new CompanyImportService(target).Apply(model, DuplicatePolicy.Skip);

        // The engine rejected it and the service rolled the whole batch back.
        Assert.False(result.Applied);
        Assert.Contains(result.Errors, e => e.Contains("rolled back", StringComparison.OrdinalIgnoreCase));

        // 1) The GST config is the very same instance, untouched — NOT wiped and NOT replaced.
        Assert.Same(priorGstConfig, target.Gst);
        Assert.True(target.GstEnabled);
        Assert.Equal(priorGstin, target.Gst!.Gstin);

        // 2) Every pre-existing GST tax ledger + Round Off still exists, with its original group — the old bug
        //    deleted exactly these on a failed batch.
        Assert.NotNull(gstOnTarget.FindTaxLedger(GstTaxHead.Central, GstTaxDirection.Output));
        Assert.Same(cgstOut, gstOnTarget.FindTaxLedger(GstTaxHead.Central, GstTaxDirection.Output));
        Assert.Same(sgstIn, gstOnTarget.FindTaxLedger(GstTaxHead.State, GstTaxDirection.Input));
        Assert.NotNull(target.FindLedgerByName(GstService.RoundOffLedgerName));
        Assert.Equal(priorRoundOffGroup, target.FindLedgerByName(GstService.RoundOffLedgerName)!.GroupId);

        // 3) Custom ledgers keep their exact opening balances (no overlay/merge leaked through).
        Assert.Equal(priorCapitalOpening, target.FindLedgerByName("Bright's Capital")!.SignedOpening);

        // 4) The import's brand-new master never leaked in.
        Assert.Null(target.FindStockItemByName("Gadget"));
        Assert.Null(target.FindLedgerByName("Some New Ledger"));

        // 5) Every count and every id set is byte-identical to before the failed import.
        Assert.Equal(before, Snapshot(target));
        Assert.Equal(priorInvVoucherCount, target.InventoryVouchers.Count);
        Assert.Equal(priorLedgerIds, target.Ledgers.Select(l => l.Id).OrderBy(g => g).ToList());
        Assert.Equal(priorVoucherIds, target.Vouchers.Select(v => v.Id).OrderBy(g => g).ToList());
    }

    // ------------------------------------------------------------------ PR-4 (d): duplicate policy

    [Fact]
    public void Skip_policy_reuses_existing_masters_and_reconciles()
    {
        var source = BuildRichCompany();
        var (model, _) = CanonicalJson.Parse(CanonicalJson.Export(source));

        // Import once into a fresh company, then import the SAME model again with Skip: the second import must
        // apply (skipping every master it already created) and the company still reconciles to the source.
        var fresh = CompanyFactory.CreateSeeded("Import Target", From, From);
        Assert.True(new CompanyImportService(fresh).Apply(model!, DuplicatePolicy.Skip).Applied);

        var (model2, _) = CanonicalJson.Parse(CanonicalJson.Export(source));
        var second = new CompanyImportService(fresh).Apply(model2!, DuplicatePolicy.Skip);

        Assert.True(second.Applied, string.Join(" | ", second.Errors));
        Assert.True(second.MastersReused > 0);
        Assert.Equal(0, second.MastersCreated);
    }

    [Fact]
    public void RejectBatch_policy_rejects_when_a_master_already_exists()
    {
        var source = BuildRichCompany();
        var (model, _) = CanonicalJson.Parse(CanonicalJson.Export(source));

        var fresh = CompanyFactory.CreateSeeded("Import Target", From, From);
        Assert.True(new CompanyImportService(fresh).Apply(model!, DuplicatePolicy.Skip).Applied);
        var vouchersAfterFirst = fresh.Vouchers.Count;

        var (model2, _) = CanonicalJson.Parse(CanonicalJson.Export(source));
        var second = new CompanyImportService(fresh).Apply(model2!, DuplicatePolicy.RejectBatch);

        Assert.False(second.Applied);
        Assert.Contains(second.Errors, e => e.Contains("reject-batch", StringComparison.OrdinalIgnoreCase));
        // Reject leaves the company exactly as the first import left it — no duplicate vouchers.
        Assert.Equal(vouchersAfterFirst, fresh.Vouchers.Count);
    }

    [Fact]
    public void MergeOpeningBalance_policy_adds_the_incoming_opening()
    {
        var source = BuildRichCompany();
        var (model, _) = CanonicalJson.Parse(CanonicalJson.Export(source));

        var fresh = CompanyFactory.CreateSeeded("Import Target", From, From);
        Assert.True(new CompanyImportService(fresh).Apply(model!, DuplicatePolicy.Skip).Applied);

        var capitalBefore = fresh.FindLedgerByName("Bright's Capital")!.SignedOpening;

        var (model2, _) = CanonicalJson.Parse(CanonicalJson.Export(source));
        var merged = new CompanyImportService(fresh).Apply(model2!, DuplicatePolicy.MergeOpeningBalance);

        Assert.True(merged.Applied, string.Join(" | ", merged.Errors));
        var capitalAfter = fresh.FindLedgerByName("Bright's Capital")!.SignedOpening;
        // Capital opening is a credit (−150000); merging the same import doubles the magnitude.
        Assert.Equal(capitalBefore * 2m, capitalAfter);
    }

    // ================================================================== reconciliation helpers

    private static void AssertReconciles(Company expected, Company actual)
    {
        // EVERY master-collection count must be EQUAL source==target after the round-trip. A single silent
        // duplicate (e.g. the reserved Profit & Loss head re-created as a 29th group) or a dropped master fails
        // here — this is the structural half of the PR-4 gate, complementing the paisa-exact report reconciliation
        // below. Groups is asserted explicitly because the P&L-head duplication bug lived precisely in that count.
        AssertMasterCountsEqual(expected, actual);

        // Per-LINE sub-object counts (bill / cost / bank / forex / GST allocations on accounting entry lines, the
        // item lines on accounting vouchers, and the allocation/order/physical/destination lines on inventory
        // vouchers) must also survive one-for-one — so a dropped or duplicated sub-object fails the gate too.
        AssertLineSubObjectCountsEqual(expected, actual);

        Assert.Equal(expected.GstEnabled, actual.GstEnabled);

        // A forex line survives with its currency, foreign amount and rate.
        var foreignExp = actual.FindLedgerByName("Foreign Consulting");
        Assert.NotNull(foreignExp);
        var usd = actual.FindCurrencyByName("USD")!;
        Assert.Equal(usd.Id, foreignExp!.CurrencyId);
        var forexLine = actual.Vouchers.SelectMany(v => v.Lines).Single(l => l.Forex is not null);
        Assert.Equal(usd.Id, forexLine.Forex!.CurrencyId);
        Assert.Equal(Money.FromRupees(100m), forexLine.Forex.ForexAmount);
        Assert.Equal(75m, forexLine.Forex.Rate);

        // A cost allocation survives against the fresh company's own cost category/centre ids.
        var centre = actual.FindCostCentreByName("Sales Dept")!;
        var costLine = actual.Vouchers.SelectMany(v => v.Lines).Single(l => l.HasCostAllocations);
        Assert.Equal(centre.Id, Assert.Single(costLine.CostAllocations).CentreId);

        // A bank allocation survives.
        var bankLine = actual.Vouchers.SelectMany(v => v.Lines).Single(l => l.HasBankAllocation);
        Assert.Equal(BankTransactionType.ChequeOrDD, bankLine.BankAllocation!.TransactionType);
        Assert.Equal(new DateOnly(2021, 4, 8), bankLine.BankAllocation.BankDate);

        // The inventory voucher moved 5 widgets inward.
        var invAlloc = Assert.Single(Assert.Single(actual.InventoryVouchers).Allocations);
        Assert.Equal(5m, invAlloc.Quantity);
        Assert.Equal(StockDirection.Inward, invAlloc.Direction);

        // Trial Balance — every ledger balance, to the paisa (matched by ledger name).
        var tbe = TrialBalance.Build(expected, AsOf);
        var tba = TrialBalance.Build(actual, AsOf);
        Assert.Equal(tbe.TotalDebit, tba.TotalDebit);
        Assert.Equal(tbe.TotalCredit, tba.TotalCredit);
        var byNameA = tba.Rows.ToDictionary(r => r.LedgerName, StringComparer.Ordinal);
        foreach (var row in tbe.Rows)
        {
            Assert.True(byNameA.TryGetValue(row.LedgerName, out var a), $"Missing TB row '{row.LedgerName}'.");
            Assert.Equal(row.Debit, a!.Debit);
            Assert.Equal(row.Credit, a.Credit);
        }

        // Balance Sheet.
        var bse = BalanceSheet.Build(expected, AsOf);
        var bsa = BalanceSheet.Build(actual, AsOf);
        Assert.Equal(bse.TotalAssets, bsa.TotalAssets);
        Assert.Equal(bse.TotalLiabilities, bsa.TotalLiabilities);
        Assert.Equal(bse.NetProfitInCapital, bsa.NetProfitInCapital);

        // P&L.
        var ple = ProfitAndLoss.Build(expected, AsOf);
        var pla = ProfitAndLoss.Build(actual, AsOf);
        Assert.Equal(ple.TotalIncome, pla.TotalIncome);
        Assert.Equal(ple.TotalExpenses, pla.TotalExpenses);
        Assert.Equal(ple.NetProfit, pla.NetProfit);

        // Stock Summary.
        var sse = StockSummary.Build(expected, AsOf);
        var ssa = StockSummary.Build(actual, AsOf);
        Assert.Equal(sse.TotalClosingValue, ssa.TotalClosingValue);
        Assert.Equal(sse.Rows.Count, ssa.Rows.Count);

        // GST — GSTR-3B net tax + GSTR-1 taxable, to the paisa.
        var g3e = Gstr3b.Build(expected, From, AsOf);
        var g3a = Gstr3b.Build(actual, From, AsOf);
        Assert.Equal(g3e.TotalOutwardTax, g3a.TotalOutwardTax);
        Assert.Equal(g3e.TotalItc, g3a.TotalItc);
        Assert.Equal(g3e.TotalNetPayable, g3a.TotalNetPayable);
    }

    /// <summary>Asserts EVERY master collection has the identical count source==target — the structural guard that
    /// makes a silent duplicate (P&amp;L head) or drop fail. Groups includes the reserved P&amp;L head (which lives
    /// outside <see cref="Company.Groups"/>) so the assertion sees the true group total on both sides.</summary>
    private static void AssertMasterCountsEqual(Company e, Company a)
    {
        Assert.Equal(GroupTotal(e), GroupTotal(a));
        Assert.Equal(e.Ledgers.Count, a.Ledgers.Count);
        Assert.Equal(e.VoucherTypes.Count, a.VoucherTypes.Count);
        Assert.Equal(e.CostCategories.Count, a.CostCategories.Count);
        Assert.Equal(e.CostCentres.Count, a.CostCentres.Count);
        Assert.Equal(e.Currencies.Count, a.Currencies.Count);
        Assert.Equal(e.ExchangeRates.Count, a.ExchangeRates.Count);
        Assert.Equal(e.Budgets.Count, a.Budgets.Count);
        Assert.Equal(e.Scenarios.Count, a.Scenarios.Count);
        Assert.Equal(e.StockGroups.Count, a.StockGroups.Count);
        Assert.Equal(e.StockCategories.Count, a.StockCategories.Count);
        Assert.Equal(e.Units.Count, a.Units.Count);
        Assert.Equal(e.Godowns.Count, a.Godowns.Count);
        Assert.Equal(e.StockItems.Count, a.StockItems.Count);
        Assert.Equal(e.StockOpeningBalances.Count, a.StockOpeningBalances.Count);
        Assert.Equal(e.Vouchers.Count, a.Vouchers.Count);
        Assert.Equal(e.InventoryVouchers.Count, a.InventoryVouchers.Count);
    }

    /// <summary>The true number of groups, counting the reserved Profit &amp; Loss head that sits outside the 28.</summary>
    private static int GroupTotal(Company c) => c.Groups.Count + (c.ProfitAndLossHead is not null ? 1 : 0);

    /// <summary>Asserts every per-entry-line and per-voucher sub-object collection reconciles by aggregate count.</summary>
    private static void AssertLineSubObjectCountsEqual(Company e, Company a)
    {
        var el = e.Vouchers.SelectMany(v => v.Lines).ToList();
        var al = a.Vouchers.SelectMany(v => v.Lines).ToList();

        Assert.Equal(el.Sum(l => l.BillAllocations.Count), al.Sum(l => l.BillAllocations.Count));
        Assert.Equal(el.Sum(l => l.CostAllocations.Count), al.Sum(l => l.CostAllocations.Count));
        Assert.Equal(el.Count(l => l.HasBankAllocation), al.Count(l => l.HasBankAllocation));
        Assert.Equal(el.Count(l => l.HasForex), al.Count(l => l.HasForex));
        Assert.Equal(el.Count(l => l.HasGst), al.Count(l => l.HasGst));

        // Accounting-voucher inventory (item-invoice) lines.
        Assert.Equal(e.Vouchers.Sum(v => v.InventoryLines.Count), a.Vouchers.Sum(v => v.InventoryLines.Count));

        // Inventory-voucher line collections (allocations / order / physical / stock-journal destination).
        Assert.Equal(e.InventoryVouchers.Sum(v => v.Allocations.Count), a.InventoryVouchers.Sum(v => v.Allocations.Count));
        Assert.Equal(e.InventoryVouchers.Sum(v => v.DestinationAllocations.Count),
                     a.InventoryVouchers.Sum(v => v.DestinationAllocations.Count));
        Assert.Equal(e.InventoryVouchers.Sum(v => v.OrderLines.Count), a.InventoryVouchers.Sum(v => v.OrderLines.Count));
        Assert.Equal(e.InventoryVouchers.Sum(v => v.PhysicalLines.Count), a.InventoryVouchers.Sum(v => v.PhysicalLines.Count));
    }

    private sealed record CompanySnapshot(
        int Ledgers, int Groups, int VoucherTypes, int StockItems, int Vouchers, int OpeningBalances, bool Gst);

    private static CompanySnapshot Snapshot(Company c) => new(
        c.Ledgers.Count, c.Groups.Count, c.VoucherTypes.Count, c.StockItems.Count, c.Vouchers.Count,
        c.StockOpeningBalances.Count, c.GstEnabled);

    private static void AssertUnchanged(CompanySnapshot before, Company after)
        => Assert.Equal(before, Snapshot(after));

    // ================================================================== corruption helpers

    private static CanonicalModel CorruptVoucher(CanonicalModel model, Func<VoucherDto, VoucherDto> mutate)
    {
        var vouchers = model.Payload.Vouchers.ToList();
        vouchers[0] = mutate(vouchers[0]);
        return model with { Payload = model.Payload with { Vouchers = vouchers } };
    }

    private static IReadOnlyList<EntryLineDto> BumpFirstDebit(IReadOnlyList<EntryLineDto> lines, long deltaPaisa)
    {
        var copy = lines.ToList();
        var idx = copy.FindIndex(l => l.Side == nameof(DrCr.Debit));
        copy[idx] = copy[idx] with { AmountPaisa = copy[idx].AmountPaisa + deltaPaisa };
        return copy;
    }

    // ================================================================== the rich source company

    private const string CompanyPan = "AAPFU0939F";

    /// <summary>
    /// A rich Bright-style trading company built entirely through the domain services, exercising every field the
    /// canonical envelope must round-trip. (Mirrors the Io.Tests fixture, replicated here because that fixture is
    /// internal to the Io.Tests assembly.)
    /// </summary>
    private static Company BuildRichCompany()
    {
        var company = CompanyFactory.CreateSeeded("Bright Traders", From, From);
        company.MailingName = "Bright Traders Pvt Ltd";
        company.Address = "12 MG Road";
        company.State = "Maharashtra";
        company.Pin = "400001";

        var gst = new GstService(company);
        gst.EnableGst(new GstConfig
        {
            Gstin = MakeGstin("27", CompanyPan, '1'),
            HomeStateCode = "27",
            RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = From,
            Periodicity = GstReturnPeriodicity.Monthly,
        });

        var capital = company.FindGroupByName("Capital Account")!;
        var cashInHand = company.FindGroupByName("Cash-in-Hand")!;
        var salesGrp = company.FindGroupByName("Sales Accounts")!;
        var debtors = company.FindGroupByName("Sundry Debtors")!;
        var stockInHand = company.FindGroupByName("Stock-in-Hand")!;

        var capitalLedger = new Domain.Ledger(Guid.NewGuid(), "Bright's Capital", capital.Id,
            Money.FromRupees(150000m), openingIsDebit: false);
        var cash = company.FindLedgerByName("Cash")!;
        cash.OpeningBalance = Money.FromRupees(20000m);
        cash.OpeningIsDebit = true;
        cash.GroupId = cashInHand.Id;

        var salesLedger = new Domain.Ledger(Guid.NewGuid(), "Sales", salesGrp.Id, Money.Zero, openingIsDebit: false,
            salesPurchaseGst: new StockItemGstDetails
            {
                HsnSac = "998877", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800,
                SupplyType = GstSupplyType.Services,
            });

        var party = new Domain.Ledger(Guid.NewGuid(), "Ram & Co", debtors.Id, Money.Zero, openingIsDebit: true,
            maintainBillByBill: true, defaultCreditPeriodDays: 30,
            partyGst: new PartyGstDetails
            {
                RegistrationType = GstRegistrationType.Regular,
                Gstin = MakeGstin("27", "AAQCS1234K", '1'),
                StateCode = "27",
            });

        var stockLedger = new Domain.Ledger(Guid.NewGuid(), "Closing Stock", stockInHand.Id,
            Money.FromRupees(25000m), openingIsDebit: true);

        company.AddLedger(capitalLedger);
        company.AddLedger(salesLedger);
        company.AddLedger(party);
        company.AddLedger(stockLedger);

        var inv = new InventoryService(company);
        var sg = inv.CreateStockGroup("Electronics");
        var nos = inv.CreateSimpleUnit("Nos", "Numbers", decimalPlaces: 0);
        var mainGodown = company.MainLocation!;
        var item = inv.CreateStockItem("Widget", sg.Id, nos.Id);
        item.Gst = new StockItemGstDetails
        {
            HsnSac = "84713010", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800,
            SupplyType = GstSupplyType.Goods,
        };
        item.ReorderLevel = 5m;
        item.MinimumOrderQuantity = 10m;
        inv.AddOpeningBalance(item.Id, mainGodown.Id, 100m, Money.FromRupees(150m), batchLabel: "B1");

        var service = new LedgerService(company);

        var receiptType = company.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Receipt);
        service.Post(new Voucher(Guid.NewGuid(), receiptType.Id, new DateOnly(2021, 4, 2),
            new[]
            {
                new EntryLine(cash.Id, Money.FromRupees(5000m), DrCr.Debit),
                new EntryLine(capitalLedger.Id, Money.FromRupees(5000m), DrCr.Credit),
            },
            number: 1, narration: "Additional capital introduced"));

        var purchaseType = company.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase);
        var purchaseLedger = new Domain.Ledger(Guid.NewGuid(), "Purchase",
            company.FindGroupByName("Purchase Accounts")!.Id, Money.Zero, openingIsDebit: true,
            salesPurchaseGst: new StockItemGstDetails
            {
                HsnSac = "84713010", Taxability = GstTaxability.Taxable, RateBasisPoints = 1800,
                SupplyType = GstSupplyType.Goods,
            });
        company.AddLedger(purchaseLedger);
        var cgstIn = gst.FindTaxLedger(GstTaxHead.Central, GstTaxDirection.Input)!;
        var sgstIn = gst.FindTaxLedger(GstTaxHead.State, GstTaxDirection.Input)!;

        var taxable = Money.FromRupees(2000m);
        var purchaseLines = new[]
        {
            new EntryLine(purchaseLedger.Id, taxable, DrCr.Debit),
            new EntryLine(cgstIn.Id, Money.FromRupees(180m), DrCr.Debit,
                gst: new GstLineTax(GstTaxHead.Central, 900, taxable)),
            new EntryLine(sgstIn.Id, Money.FromRupees(180m), DrCr.Debit,
                gst: new GstLineTax(GstTaxHead.State, 900, taxable)),
            new EntryLine(party.Id, Money.FromRupees(2360m), DrCr.Credit),
        };
        var invLines = new[]
        {
            new VoucherInventoryLine(item.Id, mainGodown.Id, 10m, Money.FromRupees(200m)),
        };
        service.Post(new Voucher(Guid.NewGuid(), purchaseType.Id, new DateOnly(2021, 4, 5),
            purchaseLines, number: 1, narration: "Bought 10 widgets from Ram & Co", partyId: party.Id,
            inventoryLines: invLines));

        // ---- previously-DROPPED data types: cost, bank, forex, currency+rate, budget, scenario, inventory voucher.
        //      Every voucher stays balanced so the reconciliation still holds to the paisa after the fresh import.
        var category = new CostCategory(Guid.NewGuid(), "Departments", allocateRevenueItems: true,
            allocateNonRevenueItems: true);
        company.AddCostCategory(category);
        var centre = new CostCentre(Guid.NewGuid(), "Sales Dept", category.Id);
        company.AddCostCentre(centre);

        var indirectExp = company.FindGroupByName("Indirect Expenses")!;
        var salary = new Domain.Ledger(Guid.NewGuid(), "Salary", indirectExp.Id, Money.Zero, openingIsDebit: true,
            costCentresApplicable: true);
        company.AddLedger(salary);

        var bankGrp = company.FindGroupByName("Bank Accounts")!;
        var bank = new Domain.Ledger(Guid.NewGuid(), "HDFC Bank", bankGrp.Id, Money.FromRupees(50000m),
            openingIsDebit: true, enableChequePrinting: true, chequePrintingBankName: "HDFC Bank");
        company.AddLedger(bank);

        var paymentType = company.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Payment);
        service.Post(new Voucher(Guid.NewGuid(), paymentType.Id, new DateOnly(2021, 4, 6),
            new[]
            {
                new EntryLine(salary.Id, Money.FromRupees(3000m), DrCr.Debit,
                    costAllocations: new[] { new CostAllocation(category.Id, centre.Id, Money.FromRupees(3000m)) }),
                new EntryLine(bank.Id, Money.FromRupees(3000m), DrCr.Credit,
                    bankAllocation: new BankAllocation(BankTransactionType.ChequeOrDD, "000123",
                        instrumentDate: new DateOnly(2021, 4, 6), bankDate: new DateOnly(2021, 4, 8))),
            },
            number: 1, narration: "April salary by cheque"));

        var usd = new Currency(Guid.NewGuid(), "$", "USD", decimalPlaces: 2);
        company.AddCurrency(usd);
        company.AddExchangeRate(new ExchangeRate(Guid.NewGuid(), usd.Id, new DateOnly(2021, 4, 1),
            standardRate: 75m, sellingRate: 75.5m, buyingRate: 74.5m));
        var foreignExp = new Domain.Ledger(Guid.NewGuid(), "Foreign Consulting", indirectExp.Id, Money.Zero,
            openingIsDebit: true, currencyId: usd.Id);
        company.AddLedger(foreignExp);
        service.Post(new Voucher(Guid.NewGuid(), paymentType.Id, new DateOnly(2021, 4, 7),
            new[]
            {
                new EntryLine(foreignExp.Id, Money.FromRupees(7500m), DrCr.Debit,
                    forex: new ForexInfo(usd.Id, Money.FromRupees(100m), 75m)),
                new EntryLine(cash.Id, Money.FromRupees(7500m), DrCr.Credit),
            },
            number: 2, narration: "US consulting fee"));

        var budget = new Budget(Guid.NewGuid(), "FY Budget", new DateOnly(2021, 4, 1), new DateOnly(2022, 3, 31));
        budget.AddLine(BudgetLine.ForLedger(salary.Id, BudgetType.OnNettTransactions, Money.FromRupees(36000m)));
        company.AddBudget(budget);

        company.AddScenario(new Scenario(Guid.NewGuid(), "Provisional", includeActuals: true,
            includedTypeIds: new[] { paymentType.Id }));

        var receiptNoteType = company.VoucherTypes.First(t => t.BaseType == VoucherBaseType.ReceiptNote);
        new InventoryPostingService(company).Post(new InventoryVoucher(Guid.NewGuid(), receiptNoteType.Id,
            new DateOnly(2021, 4, 9),
            new[] { new InventoryAllocation(item.Id, mainGodown.Id, 5m, StockDirection.Inward,
                rate: Money.FromRupees(200m), batchLabel: "B2") },
            number: 1, narration: "GRN for 5 widgets"));

        return company;
    }

    /// <summary>
    /// A small, self-contained canonical batch that is well-formed at pre-flight (all refs resolve by name against a
    /// seeded target, every accounting voucher balances) but whose FINAL inventory voucher — a Delivery Note that
    /// draws out more stock than the batch put in — is rejected by the engine's no-negative-stock guard only at post
    /// time. Importing this into ANY seeded company therefore drives the true rollback path.
    /// </summary>
    private static CanonicalModel BuildOverDeliveryBatch()
    {
        var src = CompanyFactory.CreateSeeded("Delivery Source", From, From);
        var inv = new InventoryService(src);
        var sg = inv.CreateStockGroup("Gadgets");
        var nos = inv.CreateSimpleUnit("Nos", "Numbers", decimalPlaces: 0);
        var main = src.MainLocation!;
        var gadget = inv.CreateStockItem("Gadget", sg.Id, nos.Id);
        inv.AddOpeningBalance(gadget.Id, main.Id, 3m, Money.FromRupees(100m));

        // A harmless custom ledger, so the batch also creates a fresh accounting master that rollback must remove.
        src.AddLedger(new Domain.Ledger(Guid.NewGuid(), "Some New Ledger",
            src.FindGroupByName("Indirect Expenses")!.Id, Money.Zero, openingIsDebit: true));

        // Over-deliver: only 3 Gadgets on hand, deliver 10 ⇒ the engine's DP-7 guard throws at Post time.
        var deliveryType = src.VoucherTypes.First(t => t.BaseType == VoucherBaseType.DeliveryNote);
        var voucher = new InventoryVoucher(Guid.NewGuid(), deliveryType.Id, new DateOnly(2021, 4, 10),
            new[] { new InventoryAllocation(gadget.Id, main.Id, 10m, StockDirection.Outward,
                rate: Money.FromRupees(100m)) },
            number: 99, narration: "Over-delivery that must be rejected at post time");

        // The domain would reject Post here too — so inject the voucher into the exported model directly rather than
        // posting it into the source. Export the (valid) source, then append the over-delivery inventory voucher DTO.
        var (model, errors) = CanonicalJson.Parse(CanonicalJson.Export(src));
        Assert.Empty(errors);
        var ivDto = new InventoryVoucherDto
        {
            Id = voucher.Id,
            TypeId = deliveryType.Id,
            Number = 99,
            Date = "2021-04-10",
            Narration = "Over-delivery that must be rejected at post time",
            Allocations = new[]
            {
                new InventoryAllocationDto
                {
                    StockItemId = gadget.Id, GodownId = main.Id, Quantity = 10m,
                    Direction = nameof(StockDirection.Outward), RatePaisa = 10_000L,
                },
            },
        };
        var ivs = model!.Payload.InventoryVouchers.Append(ivDto).ToList();
        return model with { Payload = model.Payload with { InventoryVouchers = ivs } };
    }

    private static string MakeGstin(string stateCode, string pan, char entity)
    {
        var body = stateCode + pan + entity + "Z";
        var check = Gstin.ComputeCheckDigit(body + "0");
        return body + check;
    }
}
