using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;

namespace Apex.Ledger.Io.Tests;

/// <summary>
/// Numbering slice S2 (numbering-design-v2 §7, §9 S2) — the duplicate guard and the import-preserves-number
/// invariant, exercised through the Io import path. The guard lives in <see cref="VoucherValidator"/>, which the
/// Io import path reaches via <see cref="LedgerService.Post"/>, so a colliding number is rejected on BOTH the
/// interactive create path and on import (it cannot be smuggled in). And because <c>Post</c>'s <c>Number ≤ 0</c>
/// assignment guard is untouched, an imported Automatic voucher keeps its stored number — gaps and all.
/// </summary>
public sealed class VoucherNumberDuplicateImportTests
{
    private static readonly DateOnly FyStart = new(2025, 4, 1);

    [Fact]
    public void PreventDuplicate_rejects_onCreateAndImport()
    {
        // ---- source A: a Manual "DupJrnl" type with the guard OFF, so it can hold two colliding #5s to export ----
        var a = CompanyFactory.CreateSeeded("Dup Source", FyStart);
        var aType = new VoucherType(Guid.NewGuid(), "DupJrnl", VoucherBaseType.Journal,
            numbering: NumberingMethod.Manual, preventDuplicate: false);
        a.AddVoucherType(aType);
        var aExp = Add(a, "Misc Exp", "Indirect Expenses", true);
        var aSvc = new LedgerService(a);
        aSvc.Post(Journal(aType.Id, 5, aExp, a.FindLedgerByName("Cash")!));
        aSvc.Post(Journal(aType.Id, 5, aExp, a.FindLedgerByName("Cash")!)); // second #5 — allowed (guard off in A)
        var model = ParseExport(a);

        // ---- create path: a guarded target rejects the second #5 directly ----
        var (b, bTypeId) = FreshGuarded();
        var bExp = Add(b, "Misc Exp", "Indirect Expenses", true);
        var bSvc = new LedgerService(b);
        bSvc.Post(Journal(bTypeId, 5, bExp, b.FindLedgerByName("Cash")!));
        var createEx = Assert.Throws<InvalidVoucherException>(() =>
            bSvc.Post(Journal(bTypeId, 5, bExp, b.FindLedgerByName("Cash")!)));
        Assert.Contains("already exists", createEx.Message);

        // ---- import path: the SAME guard bites on import (reused-by-name guarded type) ⇒ nothing applied ----
        var (b2, _) = FreshGuarded();
        var result = new CompanyImportService(b2).Apply(model!);
        Assert.False(result.Applied);
        Assert.Contains(result.Errors, e => e.Contains("already exists", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(b2.Vouchers); // rolled back — no half-import
    }

    [Fact]
    public void Import_preservesStoredNumbers_forAutomaticType()
    {
        var a = CompanyFactory.CreateSeeded("Gap Source", FyStart);
        var jrnlType = a.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Journal).Id; // seeded ⇒ Automatic
        var exp = Add(a, "Misc Exp", "Indirect Expenses", true);
        var cash = a.FindLedgerByName("Cash")!;
        var svc = new LedgerService(a);
        svc.Post(Journal(jrnlType, 0, exp, cash)); // auto #1
        var v2 = svc.Post(Journal(jrnlType, 0, exp, cash)); // auto #2
        svc.Post(Journal(jrnlType, 0, exp, cash)); // auto #3
        svc.Delete(v2.Id); // delete #2 ⇒ a gap: {1, 3}
        Assert.Equal(new[] { 1, 3 }, Numbers(a, VoucherBaseType.Journal));

        var model = ParseExport(a);
        var b = CompanyFactory.CreateSeeded("Gap Target", FyStart);
        var result = new CompanyImportService(b).Apply(model!);
        Assert.True(result.Applied, string.Join("; ", result.Errors));

        // Every stored number survives the round-trip — the gap is preserved, nothing renumbered.
        Assert.Equal(new[] { 1, 3 }, Numbers(b, VoucherBaseType.Journal));
    }

    // ---------------------------------------------------------------- helpers

    private static (Company Company, Guid TypeId) FreshGuarded()
    {
        var c = CompanyFactory.CreateSeeded("Guarded Target", FyStart);
        var t = new VoucherType(Guid.NewGuid(), "DupJrnl", VoucherBaseType.Journal,
            numbering: NumberingMethod.Manual, preventDuplicate: true);
        c.AddVoucherType(t);
        return (c, t.Id);
    }

    private static CanonicalModel ParseExport(Company c)
    {
        var (model, errors) = CanonicalJson.Parse(CanonicalJson.Export(c));
        Assert.Empty(errors);
        Assert.NotNull(model);
        return model!;
    }

    private static int[] Numbers(Company c, VoucherBaseType baseType) =>
        c.Vouchers.Where(v => c.FindVoucherType(v.TypeId)?.BaseType == baseType)
            .Select(v => v.Number).OrderBy(n => n).ToArray();

    private static Voucher Journal(Guid typeId, int number, Domain.Ledger dr, Domain.Ledger cr) =>
        new(Guid.NewGuid(), typeId, FyStart, new List<EntryLine>
        {
            new(dr.Id, Money.FromRupees(100m), DrCr.Debit),
            new(cr.Id, Money.FromRupees(100m), DrCr.Credit),
        }, number: number);

    private static Domain.Ledger Add(Company c, string name, string groupName, bool openingIsDebit)
    {
        var l = new Domain.Ledger(Guid.NewGuid(), name, c.FindGroupByName(groupName)!.Id, Money.Zero, openingIsDebit);
        c.AddLedger(l);
        return l;
    }
}
