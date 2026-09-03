using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Apex.Persistence.Sqlite.Tests;

/// <summary>
/// 🔴 <b>THE FENCE ON <c>SqliteCompanyStore.Remove</c> — Phase 10.11 S4, plan.md §5 decision D-7.</b>
/// This method is <b>FENCED, DELIBERATELY NOT FIXED</b>, and this class is what makes the fence a test rather than
/// a comment.
///
/// <para><b>What is wrong with it.</b> It deletes <c>bill_allocations</c> → <c>cost_allocations</c> →
/// <c>bank_allocations</c> → <c>entry_lines</c> → <c>vouchers</c>, and leaves <b>FIVE</b> other child tables that
/// also hang off a voucher untouched: <c>tds_lines</c>, <c>tcs_lines</c>, <c>payroll_lines</c>,
/// <c>voucher_inventory_lines</c> and <c>pos_tender_allocations</c>. <c>DeleteCompanyRows</c>, in the same class,
/// handles all five.</para>
///
/// <para><b>Why it is fenced and not repaired.</b> It is off the live path today, which is why the gap has never
/// bitten — and it is exactly the method a "delete a voucher" feature reaches for. A working-looking
/// <c>Remove</c> would INVITE routing voucher deletion through it instead of through whole-company <c>Save</c>,
/// which is the only path the entire aggregate round-trips on. Making it look safe is what would put it on the
/// live path. S4's delete verb removes the voucher from the in-memory aggregate and calls <c>Save</c>; it never
/// calls this.</para>
///
/// <para><b>What this test therefore asserts, and why that is not "asserting a bug".</b> It pins the CURRENT,
/// KNOWN-INCOMPLETE behaviour so that anyone who "fixes" <c>Remove</c> without removing the fence goes red and
/// lands on decision D-7 instead of quietly re-opening the invitation. If a later slice decides to complete the
/// method, THIS test is the one that must change, and changing it is then the correct act.</para>
/// </summary>
public sealed class VoucherRemoveFenceTests
{
    private static readonly DateOnly FyStart = new(2024, 4, 1);
    private static readonly DateOnly On = new(2024, 4, 10);

    private static Apex.Ledger.Domain.Ledger AddLedger(Company c, string name, Guid groupId, bool debit)
    {
        var l = new Apex.Ledger.Domain.Ledger(Guid.NewGuid(), name, groupId, Money.Zero, debit);
        c.AddLedger(l);
        return l;
    }

    /// <summary>A company holding ONE item-invoice Purchase — an accounting voucher that owns a
    /// <c>voucher_inventory_lines</c> row, i.e. one of the five child tables <c>Remove</c> forgets.</summary>
    private static (Company Company, Guid VoucherId) SeedItemInvoicePurchase()
    {
        var c = CompanyFactory.CreateSeeded("Remove Fence Co", FyStart);
        var masters = new InventoryService(c);
        var grp = masters.CreateStockGroup("Goods");
        var nos = masters.CreateSimpleUnit("Nos", "Numbers");
        var item = masters.CreateStockItem("Widget", grp.Id, nos.Id);
        var main = c.MainLocation!.Id;

        var purchases = AddLedger(c, "Purchases", c.FindGroupByName("Purchase Accounts")!.Id, true);
        var creditor = AddLedger(c, "Creditor", c.FindGroupByName("Sundry Creditors")!.Id, false);
        var purchaseType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Purchase).Id;

        var v = new Voucher(Guid.NewGuid(), purchaseType, On, new[]
            {
                new EntryLine(purchases.Id, Money.FromRupees(1200m), DrCr.Debit),
                new EntryLine(creditor.Id, Money.FromRupees(1200m), DrCr.Credit),
            },
            inventoryLines: new[] { new VoucherInventoryLine(item.Id, main, 10m, Money.FromRupees(120m)) });
        new LedgerService(c).Post(v);

        return (c, v.Id);
    }

    /// <summary>
    /// 🔴 <b>THE FENCE.</b> <c>Remove</c> cannot delete an item-invoice voucher at all: <c>PRAGMA foreign_keys</c>
    /// is ON for every connection the store opens and <c>voucher_inventory_lines.voucher_id</c> is a plain
    /// <c>REFERENCES vouchers(id)</c> with no <c>ON DELETE</c> action, so the un-deleted child row makes the
    /// <c>DELETE FROM vouchers</c> fail and the whole transaction roll back.
    ///
    /// <para><b>Read the second half of this test — it is the more important half.</b> The FK failure means the
    /// method does not silently orphan rows here; it fails loudly. But it fails loudly on a TDS-deducted payment, a
    /// TCS invoice, a payroll voucher, an item-invoice AND a POS bill — five whole voucher families that are simply
    /// undeletable through this path. That, and not data corruption, is the concrete shape of the incompleteness,
    /// and it is why the fence says "deletion goes through <c>Save</c>".</para>
    /// </summary>
    [Fact]
    public void Remove_cannot_delete_an_item_invoice_voucher_because_it_forgets_the_stock_lines()
    {
        var dbPath = TempDbFile.NewPath("apex-remove-fence");
        try
        {
            var (company, voucherId) = SeedItemInvoicePurchase();

            using var store = new SqliteCompanyStore(dbPath);
            store.Save(company);

            // The child row exists, so the FK has something to protect.
            Assert.Equal(1, CountRows(dbPath, "voucher_inventory_lines", voucherId));

            // 🔴 THE FENCE: Remove does not handle voucher_inventory_lines, so the FK refuses the voucher delete.
            var ex = Assert.Throws<SqliteException>(() => store.Remove(company.Id, voucherId));
            Assert.Contains("FOREIGN KEY", ex.Message, StringComparison.OrdinalIgnoreCase);

            // The transaction rolled back: the voucher and its stock line are both still there.
            Assert.Equal(1, CountRows(dbPath, "vouchers", voucherId, idColumn: "id"));
            Assert.Equal(1, CountRows(dbPath, "voucher_inventory_lines", voucherId));
        }
        finally
        {
            TempDbFile.Delete(dbPath);
        }
    }

    /// <summary>
    /// The CONTRAST that proves the fence is about the five forgotten tables and not about <c>Remove</c> being
    /// broken outright: on a plain Journal — no stock lines, no TDS/TCS lines, no payroll lines, no POS tenders —
    /// <c>Remove</c> works exactly as its name says, and takes the entry lines with it.
    ///
    /// <para>Without this control the test above would be consistent with "Remove never works", and a reader could
    /// conclude the method is simply dead rather than <b>selectively</b> incomplete — which is the fact that makes
    /// it dangerous, because the family that works is the family someone will test it on.</para>
    /// </summary>
    [Fact]
    public void Remove_does_work_on_a_plain_journal_which_is_exactly_why_the_gap_is_dangerous()
    {
        var dbPath = TempDbFile.NewPath("apex-remove-fence-ok");
        try
        {
            var c = CompanyFactory.CreateSeeded("Remove Fence Journal Co", FyStart);
            var party = AddLedger(c, "Acme Traders", c.FindGroupByName("Sundry Debtors")!.Id, true);
            var sales = AddLedger(c, "Sales", c.FindGroupByName("Sales Accounts")!.Id, false);
            var journal = c.FindVoucherTypeByName("Journal")!;
            var v = new Voucher(Guid.NewGuid(), journal.Id, On, new[]
            {
                new EntryLine(party.Id, Money.FromRupees(5000m), DrCr.Debit),
                new EntryLine(sales.Id, Money.FromRupees(5000m), DrCr.Credit),
            });
            new LedgerService(c).Post(v);

            using var store = new SqliteCompanyStore(dbPath);
            store.Save(c);
            Assert.Equal(2, CountRows(dbPath, "entry_lines", v.Id));

            store.Remove(c.Id, v.Id);

            Assert.Equal(0, CountRows(dbPath, "vouchers", v.Id, idColumn: "id"));
            Assert.Equal(0, CountRows(dbPath, "entry_lines", v.Id));
        }
        finally
        {
            TempDbFile.Delete(dbPath);
        }
    }

    private static int CountRows(string dbPath, string table, Guid voucherId, string idColumn = "voucher_id")
    {
        using var cn = new SqliteConnection($"Data Source={dbPath}");
        cn.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM {table} WHERE {idColumn} = $vid;";
        cmd.Parameters.AddWithValue("$vid", voucherId.ToString("D"));
        return Convert.ToInt32(cmd.ExecuteScalar());
    }
}
