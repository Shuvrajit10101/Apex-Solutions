using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Xunit;
using Domain = Apex.Ledger.Domain;

namespace Apex.Persistence.Sqlite.Tests;

/// <summary>
/// Phase 10.11 S5a, §6.5 <b>clause 4 through the STORE</b> — the half the engine suite cannot reach.
///
/// <para><b>Why this file exists.</b> Clause 4 preserves the voucher's list index, and its whole justification is
/// that the index is <i>persisted</i>: <c>SqliteCompanyStore.ReadVouchers</c> selects <c>ORDER BY rowid</c> and
/// <c>Load</c> re-posts in that order, so the in-memory position survives save → load. Every shipped index test
/// was IN-MEMORY, and the canonical-export tests never touch the store — so a <c>Replace</c> that got the index
/// right in memory and wrong on disk would have passed the entire gate. §7.2 T-1 names this explicitly:
/// <i>"the second half is not ceremony"</i>.</para>
///
/// <para>Four shapes, because the one that actually protects clause 4 is the SAME-DATED one (distinct dates would
/// be re-sorted into the right order by accident, and would prove nothing).</para>
/// </summary>
public sealed class VoucherReplaceRoundTripTests
{
    private static readonly DateOnly Books = new(2024, 4, 1);
    private static readonly Money Amended = Money.FromRupees(99999.99m);

    private sealed record Shape(string Name, Func<int, DateOnly> DateOf, int ReplaceAt);

    public static TheoryData<string> Shapes() =>
        new() { "distinct-dates@9", "same-date@9", "same-date@0", "descending-dates@5" };

    private static Shape Resolve(string name) => name switch
    {
        "distinct-dates@9" => new Shape(name, n => Books.AddDays(n + 1), 9),
        "same-date@9" => new Shape(name, _ => Books.AddDays(5), 9),
        "same-date@0" => new Shape(name, _ => Books.AddDays(5), 0),
        _ => new Shape(name, n => Books.AddDays(30 - n), 5),
    };

    [Theory]
    [MemberData(nameof(Shapes))]
    [Trait("Category", "RoundTrip")]
    public void A_replaced_voucher_keeps_its_list_position_across_save_and_load(string shapeName)
    {
        var shape = Resolve(shapeName);
        var dbPath = TempDbFile.NewPath("apex-replace-roundtrip");
        try
        {
            var company = CompanyFactory.CreateSeeded("Replace Round Trip Co", Books, Books);

            Domain.Ledger Add(string name, string groupName, bool debit)
            {
                var l = new Domain.Ledger(
                    Guid.NewGuid(), name, company.FindGroupByName(groupName)!.Id, Money.Zero, openingIsDebit: debit);
                company.AddLedger(l);
                return l;
            }

            var sales = Add("Sales", "Sales Accounts", false);
            var customer = Add("A Customer", "Sundry Debtors", true);
            var salesType = company.FindVoucherTypeByName("Sales")!;
            var service = new LedgerService(company);

            Voucher Invoice(Guid id, DateOnly date, Money amount) => new(
                id, salesType.Id, date,
                new[]
                {
                    new EntryLine(customer.Id, amount, DrCr.Debit),
                    new EntryLine(sales.Id, amount, DrCr.Credit),
                },
                partyId: customer.Id);

            var ids = new List<Guid>();
            for (var n = 0; n < 11; n++)
            {
                var id = Guid.NewGuid();
                ids.Add(id);
                service.Post(Invoice(id, shape.DateOf(n), Money.FromRupees(1234.55m + (n * 101.37m))));
            }

            var targetId = ids[shape.ReplaceAt];
            var orderBefore = company.Vouchers.Select(v => v.Id).ToList();
            var numberBefore = company.FindVoucher(targetId)!.Number;

            service.Replace(targetId, Invoice(targetId, company.FindVoucher(targetId)!.Date, Amended));

            Assert.Equal(orderBefore, company.Vouchers.Select(v => v.Id).ToList());

            using (var store = new SqliteCompanyStore(dbPath)) store.Save(company);
            using var reopened = new SqliteCompanyStore(dbPath);
            var loaded = reopened.Load(company.Id)!;

            // The ORDER survived the round trip — this is the assertion clause 4's justification rests on…
            Assert.Equal(orderBefore, loaded.Vouchers.Select(v => v.Id).ToList());

            // …and so did the INDEX of the altered voucher specifically, its number, and its amended amount.
            Assert.Equal(shape.ReplaceAt, loaded.Vouchers.ToList().FindIndex(v => v.Id == targetId));
            Assert.Equal(numberBefore, loaded.FindVoucher(targetId)!.Number);
            Assert.Equal(Amended, loaded.FindVoucher(targetId)!.TotalDebit);
        }
        finally
        {
            TempDbFile.Delete(dbPath);
        }
    }

    /// <summary>
    /// The §3.4 bank date is a fact written onto a POSTED line by a later human action — so a carry-forward that
    /// only worked in memory would be undone by the next save/load. Pinned end to end.
    /// </summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void A_carried_bank_reconciliation_date_survives_the_round_trip()
    {
        var dbPath = TempDbFile.NewPath("apex-replace-bankdate-roundtrip");
        try
        {
            var company = CompanyFactory.CreateSeeded("Bank Round Trip Co", Books, Books);

            Domain.Ledger Add(string name, string groupName, decimal opening, bool debit)
            {
                var l = new Domain.Ledger(
                    Guid.NewGuid(), name, company.FindGroupByName(groupName)!.Id, Money.FromRupees(opening),
                    openingIsDebit: debit);
                company.AddLedger(l);
                return l;
            }

            var bank = Add("HDFC Current", "Bank Accounts", 500000m, true);
            var supplier = Add("A Supplier", "Sundry Creditors", 100000m, false);
            var paymentType = company.FindVoucherTypeByName("Payment")!;
            var service = new LedgerService(company);
            var paymentId = Guid.NewGuid();
            var ticked = Books.AddDays(9);

            Voucher Payment(Money amount, string narration) => new(
                paymentId, paymentType.Id, Books.AddDays(4),
                new[]
                {
                    new EntryLine(supplier.Id, amount, DrCr.Debit),
                    new EntryLine(
                        bank.Id, amount, DrCr.Credit,
                        bankAllocation: new BankAllocation(
                            BankTransactionType.ChequeOrDD, "445566", Books.AddDays(4), null)),
                },
                narration: narration);

            service.Post(Payment(Money.FromRupees(47239.55m), "cheque to supplier"));
            Assert.True(Apex.Ledger.Reports.BankReconciliation.SetBankDate(company, paymentId, bank.Id, ticked));

            service.Replace(paymentId, Payment(Money.FromRupees(47239.55m), "narration corrected"), out var warnings);
            Assert.Empty(warnings);

            using (var store = new SqliteCompanyStore(dbPath)) store.Save(company);
            using var reopened = new SqliteCompanyStore(dbPath);
            var loaded = reopened.Load(company.Id)!;

            var line = loaded.FindVoucher(paymentId)!.Lines.Single(l => l.BankAllocation is not null);
            Assert.Equal(ticked, line.BankAllocation!.BankDate);
            Assert.Equal("narration corrected", loaded.FindVoucher(paymentId)!.Narration);
        }
        finally
        {
            TempDbFile.Delete(dbPath);
        }
    }
}
