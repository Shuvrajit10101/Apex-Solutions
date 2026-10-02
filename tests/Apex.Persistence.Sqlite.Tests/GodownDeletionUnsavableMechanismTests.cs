using System;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Microsoft.Data.Sqlite;
using Xunit;
using Domain = Apex.Ledger.Domain;

namespace Apex.Persistence.Sqlite.Tests;

/// <summary>
/// 🔴 <b>WAVE 29 / TRACK U1 — THE GODOWN HALF OF THE "UNSAVABLE BOOK" MECHANISM, DEMONSTRATED AGAINST THE REAL
/// STORE RATHER THAN ASSERTED.</b>
///
/// <para><c>MasterDeletionForeignKeyCoverageTests</c> proves the guard set EQUALS the schema set, which is the
/// structural claim. This file proves the CONSEQUENCE for the one master wave 29 made deletable whose foreign-key
/// surface was the widest: nine of the eleven columns that declare <c>REFERENCES godowns(id)</c> were counted by
/// nothing at all before this wave, because <c>InventoryService.DeleteGodown</c> guarded only the default
/// location, child godowns and opening balances.</para>
///
/// <para><b>Why that is severe and not cosmetic.</b> <c>SqliteCompanyStore</c> executes
/// <c>PRAGMA foreign_keys = ON</c> on every connection and <c>Save</c> is a delete-all + full re-insert. So a
/// sibling row the guard failed to count does not dangle quietly: the very next <c>Save</c> raises
/// <c>SQLITE_CONSTRAINT_FOREIGNKEY</c> with the godown already gone from the in-memory aggregate — and every
/// later save on the open company throws too. The book on screen can never be written again by any screen, and
/// the operator's only route out is to discard the session.</para>
///
/// <para>The subject here is <c>voucher_inventory_lines.godown_id</c> — a POSTED item-invoice line, the single
/// most ordinary way a godown becomes referenced, and one the pre-wave service did not look at.</para>
/// </summary>
public sealed class GodownDeletionUnsavableMechanismTests
{
    private static readonly DateOnly FyStart = new(2025, 4, 1);

    /// <summary>
    /// 🔴 THE MECHANISM. The guard REFUSES (that is the fix); bypassing it the way an uncounted column silently
    /// did reproduces the permanent failure exactly.
    /// </summary>
    [Fact]
    public void An_uncounted_godown_reference_really_does_make_the_company_unsavable()
    {
        var dbPath = TempDbFile.NewPath("apex-w29-godown-fk");
        try
        {
            var (company, site) = SeedOneItemInvoiceInASecondGodown();

            using var store = new SqliteCompanyStore(dbPath);
            store.Save(company);                                   // the baseline writes fine

            // The W29 guard refuses it — this is the assertion that the fix is present.
            var refusal = Assert.Throws<InvalidOperationException>(
                () => MasterDeletionRules.EnsureGodownDeletable(company, site));
            Assert.Contains("Site A", refusal.Message);

            // …bypassed, exactly the way the pre-wave service's nine uncounted columns silently allowed.
            company.RemoveGodown(site);
            Assert.Null(company.FindGodown(site.Id));

            var first = Assert.Throws<SqliteException>(() => store.Save(company));
            Assert.Contains("FOREIGN KEY", first.Message, StringComparison.OrdinalIgnoreCase);

            // …and the open company can never be saved again, which is the part that makes it severe.
            Assert.Throws<SqliteException>(() => store.Save(company));
        }
        finally { TempDbFile.Delete(dbPath); }
    }

    /// <summary>
    /// The positive control the mechanism test needs: with the blocking line removed the guard PERMITS the
    /// delete, the delete happens, <c>Save</c> succeeds, and the book round-trips without the godown. Without
    /// this the test above could pass against a store that cannot save anything.
    /// </summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void A_permitted_godown_delete_saves_and_round_trips()
    {
        var dbPath = TempDbFile.NewPath("apex-w29-godown-control");
        try
        {
            var (company, site) = SeedOneItemInvoiceInASecondGodown();
            using (var store = new SqliteCompanyStore(dbPath)) store.Save(company);

            // Remove the only reason the guard refuses — the posted invoice.
            foreach (var v in company.Vouchers.Where(v => v.HasInventoryLines).ToList())
                new LedgerService(company).Delete(v.Id);

            MasterDeletionRules.EnsureGodownDeletable(company, site);     // permitted
            company.RemoveGodown(site);

            using (var store = new SqliteCompanyStore(dbPath)) store.Save(company);

            using var reopened = new SqliteCompanyStore(dbPath);
            var loaded = reopened.Load(company.Id)!;
            Assert.Null(loaded.FindGodown(site.Id));
            // The DEFAULT location is untouched — the guard refuses it separately and nothing here removed it.
            Assert.NotNull(loaded.MainLocation);
        }
        finally { TempDbFile.Delete(dbPath); }
    }

    // ===================================================================== fixture

    private static (Company Company, Godown Site) SeedOneItemInvoiceInASecondGodown()
    {
        var c = CompanyFactory.CreateSeeded("W29 Godown Co", FyStart, FyStart);
        var on = FyStart.AddDays(5);

        var party = new Domain.Ledger(Guid.NewGuid(), "Acme Traders",
            c.FindGroupByName("Sundry Debtors")!.Id, Money.Zero, openingIsDebit: true);
        c.AddLedger(party);
        var sales = new Domain.Ledger(Guid.NewGuid(), "Sales",
            c.FindGroupByName("Sales Accounts")!.Id, Money.Zero, openingIsDebit: false);
        c.AddLedger(sales);

        var inv = new InventoryService(c);
        var site = inv.CreateGodown("Site A");
        var group = inv.CreateStockGroup("W29 Group");
        var unit = inv.CreateSimpleUnit("Nos", "Numbers");
        var item = inv.CreateStockItem("Widget", group.Id, unit.Id);

        var salesType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales);
        new LedgerService(c).Post(new Voucher(
            Guid.NewGuid(), salesType.Id, on,
            new[]
            {
                new EntryLine(party.Id, Money.FromRupees(1000m), DrCr.Debit),
                new EntryLine(sales.Id, Money.FromRupees(1000m), DrCr.Credit),
            },
            partyId: party.Id,
            // voucher_inventory_lines.godown_id — one of the nine columns no guard used to count.
            inventoryLines: new[] { new VoucherInventoryLine(item.Id, site.Id, 1m, Money.FromRupees(1000m)) }));

        return (c, site);
    }
}
