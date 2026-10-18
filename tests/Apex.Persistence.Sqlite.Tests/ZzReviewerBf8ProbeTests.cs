using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Xunit;
using Domain = Apex.Ledger.Domain;

namespace Apex.Persistence.Sqlite.Tests;

/// <summary>
/// REVIEWER PROBE (wave-3 adversarial review of claude/apex-bf-masters-v67). My OWN fixture, deliberately
/// unlike the branch's: the default bank is put on a PRE-EXISTING seeded voucher type (not one created by
/// VoucherTypeService), the book carries a posted voucher, and the reopen happens through a fresh store
/// handle. Used to reproduce BF-8 independently and to prove the child-clear by mutation. Delete before
/// merge.
/// </summary>
public sealed class ZzReviewerBf8ProbeTests
{
    private static Company Seed(out Guid salesTypeId, out Guid bankId)
    {
        var c = CompanyFactory.CreateSeeded("Reviewer BF8 Co", new DateOnly(2024, 4, 1));

        var bank = new Domain.Ledger(
            Guid.NewGuid(), "Axis Bank A/c", c.FindGroupByName("Bank Accounts")!.Id,
            Money.Zero, openingIsDebit: true);
        c.AddLedger(bank);
        bankId = bank.Id;

        // A PRE-EXISTING seeded type, altered in place — not a freshly created one.
        var sales = c.FindVoucherTypeByName("Sales")!;
        sales.DefaultBankLedgerId = bank.Id;
        salesTypeId = sales.Id;

        // And a real posted voucher, so the failing transaction would take real data with it.
        var cash = c.FindLedgerByName("Cash")!;
        cash.OpeningBalance = Money.FromRupees(500000m);
        cash.OpeningIsDebit = true;
        var journal = c.FindVoucherTypeByName("Journal")!;
        new LedgerService(c).Post(new Voucher(Guid.NewGuid(), journal.Id, new DateOnly(2024, 4, 10), new[]
        {
            new EntryLine(bank.Id, Money.FromRupees(2500m), DrCr.Debit),
            new EntryLine(cash.Id, Money.FromRupees(2500m), DrCr.Credit),
        }));
        return c;
    }

    [Fact]
    public void Reviewer_a_preexisting_type_carrying_a_default_bank_saves_repeatedly()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"apex-rev-bf8-{Guid.NewGuid():N}.db");
        try
        {
            var company = Seed(out var typeId, out var bankId);
            using (var store = new SqliteCompanyStore(dbPath))
            {
                store.Save(company);
                store.Save(company);
            }
            // Reopen through a FRESH handle, mutate something unrelated, save twice more.
            using (var store = new SqliteCompanyStore(dbPath))
            {
                var reloaded = store.Load(store.ListCompanies().Single().Id);
                Assert.Equal(bankId, reloaded.FindVoucherType(typeId)!.DefaultBankLedgerId);
                reloaded.FindLedgerByName("Cash")!.OpeningBalance = Money.FromRupees(600000m);
                store.Save(reloaded);
                store.Save(reloaded);

                var again = store.Load(store.ListCompanies().Single().Id);
                Assert.Equal(bankId, again.FindVoucherType(typeId)!.DefaultBankLedgerId);
                Assert.Equal(Money.FromRupees(600000m), again.FindLedgerByName("Cash")!.OpeningBalance);
                Assert.Single(again.Vouchers);
            }
        }
        finally { TempDbFile.Delete(dbPath); }
    }

}
