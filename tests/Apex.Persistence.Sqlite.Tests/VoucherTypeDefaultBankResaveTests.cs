using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Xunit;
using Domain = Apex.Ledger.Domain;

namespace Apex.Persistence.Sqlite.Tests;

/// <summary>
/// 🔴 <b>BF-8 — v67's <c>voucher_types.default_bank_ledger_id</c> made a book PERMANENTLY UNSAVABLE on the
/// SECOND save.</b>
///
/// <para><b>The mechanism.</b> <c>Save</c> is delete-all-then-reinsert, and <c>DeleteCompanyRows</c> runs
/// <c>DELETE FROM ledgers</c> <b>before</b> <c>DELETE FROM voucher_types</c>. The FIRST save of a company
/// that names a default bank is fine — the database holds no such row yet, so nothing references the ledger
/// being deleted. The SECOND save finds the saved <c>voucher_types</c> row pointing at the ledger it is
/// about to delete, SQLite refuses with <i>FOREIGN KEY constraint failed</i>, and the whole transaction —
/// every master and every voucher — rolls back. <b>From that moment the operator cannot save the company at
/// all, by any route, and nothing tells them why.</b></para>
///
/// <para><b>Why nothing caught it.</b> Every existing v67 test saved such a book exactly ONCE. The two FK
/// columns v67 added were both put through <c>MasterDeletionForeignKeyCoverageTests</c>, which guards
/// DELETING THE MASTER — a different question from the store's own delete ORDER during an ordinary save.
/// <c>budget_lines.cost_centre_id</c>, the other new FK, is safe only by luck of ordering: budget_lines is
/// deleted long before cost_centres.</para>
///
/// <para><b>The fix</b> is the one the file already uses twice for the same shape (the POS and v47 numbering
/// child-clears): null the reference out immediately before the <c>ledgers</c> delete. A save rewrites the
/// row from the aggregate straight afterwards, so no value is lost.</para>
/// </summary>
public sealed class VoucherTypeDefaultBankResaveTests
{
    private static Company SeedWithADefaultBank(out Guid typeId, out Guid bankId)
    {
        var c = CompanyFactory.CreateSeeded("Default Bank Co", new DateOnly(2024, 4, 1));

        var bank = new Domain.Ledger(
            Guid.NewGuid(), "HDFC Current A/c", c.FindGroupByName("Bank Accounts")!.Id,
            Money.Zero, openingIsDebit: true);
        c.AddLedger(bank);
        bankId = bank.Id;

        var created = new VoucherTypeService(c).Create(
            "Export Invoice", VoucherBaseType.Sales, NumberingMethod.Automatic, abbreviation: null,
            printAfterSaving: false, provideNarrationForEachLedger: false,
            fields: VoucherTypeMasterFields.Default with { DefaultBankLedgerId = bank.Id });
        typeId = created.Id;
        return c;
    }

    /// <summary>
    /// 🔴 <b>THE REGRESSION.</b> Two ordinary saves of the same book. Before the fix the second one threw
    /// <c>SqliteException : SQLite Error 19: 'FOREIGN KEY constraint failed'</c> and rolled the whole company
    /// back. Reverting the <c>UPDATE voucher_types SET default_bank_ledger_id = NULL</c> clear in
    /// <c>DeleteCompanyRows</c> reddens exactly here.
    /// </summary>
    [Fact]
    public void A_voucher_type_with_a_default_bank_can_be_saved_more_than_once()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"apex-vtbank-{Guid.NewGuid():N}.db");
        try
        {
            var company = SeedWithADefaultBank(out var typeId, out var bankId);

            using (var store = new SqliteCompanyStore(dbPath))
            {
                store.Save(company);                       // first save: always worked
                store.Save(company);                       // second save: this is the one that threw
                store.Save(company);                       // and a third, because "twice" is not a rule
            }

            // The value survives all three — the clear is a delete-order manoeuvre, not data loss.
            using (var store = new SqliteCompanyStore(dbPath))
            {
                var reloaded = store.Load(store.ListCompanies().Single().Id);
                Assert.Equal(bankId, reloaded.FindVoucherType(typeId)!.DefaultBankLedgerId);
                Assert.Equal("HDFC Current A/c", reloaded.FindLedger(bankId)!.Name);
            }
        }
        finally { TempDbFile.Delete(dbPath); }
    }

    /// <summary>
    /// The same book re-saved after an UNRELATED edit — the realistic path, where the operator renames a
    /// ledger and the save that carries the rename is the one that dies.
    /// </summary>
    [Fact]
    public void An_unrelated_edit_still_saves_on_a_book_that_names_a_default_bank()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"apex-vtbank2-{Guid.NewGuid():N}.db");
        try
        {
            var company = SeedWithADefaultBank(out var typeId, out var bankId);
            using var store = new SqliteCompanyStore(dbPath);
            store.Save(company);

            company.FindLedgerByName("Cash")!.OpeningBalance = Money.FromRupees(5000m);
            store.Save(company);

            var reloaded = store.Load(store.ListCompanies().Single().Id);
            Assert.Equal(Money.FromRupees(5000m), reloaded.FindLedgerByName("Cash")!.OpeningBalance);
            Assert.Equal(bankId, reloaded.FindVoucherType(typeId)!.DefaultBankLedgerId);
        }
        finally { TempDbFile.Delete(dbPath); }
    }

    /// <summary>
    /// Clearing the default bank, then saving again, leaves it cleared — the clear-and-rewrite must not
    /// resurrect a value the operator removed.
    /// </summary>
    [Fact]
    public void Clearing_the_default_bank_survives_a_resave()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"apex-vtbank3-{Guid.NewGuid():N}.db");
        try
        {
            var company = SeedWithADefaultBank(out var typeId, out _);
            using var store = new SqliteCompanyStore(dbPath);
            store.Save(company);

            company.FindVoucherType(typeId)!.DefaultBankLedgerId = null;
            store.Save(company);
            store.Save(company);

            var reloaded = store.Load(store.ListCompanies().Single().Id);
            Assert.Null(reloaded.FindVoucherType(typeId)!.DefaultBankLedgerId);
        }
        finally { TempDbFile.Delete(dbPath); }
    }
}
