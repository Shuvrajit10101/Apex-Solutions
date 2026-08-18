using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Apex.Ledger.Tests.Support;
using Xunit;

namespace Apex.Persistence.Sqlite.Tests;

/// <summary>
/// 🔴 <b>The half of the S5a equivalence test that MEMORY CANNOT PROVE</b> (design §7.2, T-1 second half).
///
/// <para>Clause 4 of the <c>Replace</c> contract preserves the voucher's <b>index in
/// <c>Company.Vouchers</c></b>, on the grounds that the index is PERSISTED: <c>ReadVouchers</c> selects
/// <c>ORDER BY rowid</c> and <c>Load</c> re-posts in that order, so the in-memory list position survives
/// save→load and is the Day Book order of same-dated vouchers. That claim is about the STORE, so an in-memory
/// assertion cannot establish it — <i>"a Replace that got the index right in memory and wrong on disk would
/// pass the first assertion and fail the second."</i> These tests drive a real SQLite file.</para>
///
/// <para>This is why the Sqlite project's count moves in a slice that ships no persistence code: the assertion,
/// not the production change, is what lives here.</para>
/// </summary>
public sealed class VoucherReplaceRoundTripTests
{
    private static readonly DateOnly Books = new(2024, 4, 1);
    private static readonly DateOnly AsOf = new(2025, 3, 31);
    private static readonly Money WrongTotal = Money.FromRupees(184733.45m);
    private static readonly Money RightTotal = Money.FromRupees(184731.95m);

    private sealed class Book
    {
        public required Company Company { get; init; }
        public required LedgerService Service { get; init; }
        public required Apex.Ledger.Domain.Ledger Customer { get; init; }
        public required Apex.Ledger.Domain.Ledger Sales { get; init; }
        public required VoucherType SalesType { get; init; }
        public required Guid TenthId { get; init; }
    }

    /// <summary>
    /// Eleven sales invoices, ALL ON THE SAME DATE, so the only thing that can order them in the Day Book is
    /// the list position — which is precisely the property under test.
    /// </summary>
    private static Book Build(Money tenthTotal, Guid tenthId)
    {
        var company = CompanyFactory.CreateSeeded("Replace RT Co", Books, Books);

        var sales = new Apex.Ledger.Domain.Ledger(
            Guid.NewGuid(), "Sales", company.FindGroupByName("Sales Accounts")!.Id, Money.Zero, false);
        company.AddLedger(sales);
        var customer = new Apex.Ledger.Domain.Ledger(
            Guid.NewGuid(), "A Customer", company.FindGroupByName("Sundry Debtors")!.Id, Money.Zero, true);
        company.AddLedger(customer);

        var book = new Book
        {
            Company = company,
            Service = new LedgerService(company),
            Customer = customer,
            Sales = sales,
            SalesType = company.FindVoucherTypeByName("Sales")!,
            TenthId = tenthId,
        };

        for (var n = 1; n <= 11; n++)
        {
            var amount = n == 10 ? tenthTotal : Money.FromRupees(1234.55m + (n * 101.37m));
            book.Service.Post(Invoice(book, n == 10 ? tenthId : Guid.NewGuid(), amount, $"Invoice {n}"));
        }

        return book;
    }

    private static Voucher Invoice(Book book, Guid id, Money amount, string narration)
        => new(
            id, book.SalesType.Id, Books.AddDays(3),
            new[]
            {
                new EntryLine(book.Customer.Id, amount, DrCr.Debit),
                new EntryLine(book.Sales.Id, amount, DrCr.Credit),
            },
            narration: narration,
            partyId: book.Customer.Id);

    [Fact]
    public void An_altered_voucher_keeps_its_number_and_its_list_position_through_save_and_load()
    {
        var dbPath = TempDbFile.NewPath("replace-roundtrip");
        try
        {
            var tenthId = Guid.NewGuid();
            var altered = Build(WrongTotal, tenthId);
            altered.Service.Replace(tenthId, Invoice(altered, tenthId, RightTotal, "Invoice 10"));

            using (var store = new SqliteCompanyStore(dbPath)) store.Save(altered.Company);

            using var reopened = new SqliteCompanyStore(dbPath);
            var loaded = reopened.Load(altered.Company.Id)!;

            var index = loaded.Vouchers.ToList().FindIndex(v => v.Id == tenthId);
            Assert.Equal(9, index);
            Assert.Equal(10, loaded.Vouchers[index].Number);
            Assert.Equal(RightTotal, loaded.Vouchers[index].TotalDebit);
            Assert.Equal(11, loaded.Vouchers.Count);

            // The neighbours came back in the same order too — an index preserved in memory but written to
            // the end of the file would show up right here.
            Assert.Equal(9, loaded.Vouchers[8].Number);
            Assert.Equal(11, loaded.Vouchers[10].Number);
        }
        finally
        {
            TempDbFile.Delete(dbPath);
        }
    }

    [Fact]
    public void The_altered_book_and_a_directly_posted_book_agree_on_every_derived_figure_after_a_round_trip()
    {
        var alteredPath = TempDbFile.NewPath("replace-altered");
        var directPath = TempDbFile.NewPath("replace-direct");
        try
        {
            var tenthId = Guid.NewGuid();

            var altered = Build(WrongTotal, tenthId);
            altered.Service.Replace(tenthId, Invoice(altered, tenthId, RightTotal, "Invoice 10"));
            var direct = Build(RightTotal, tenthId);

            using (var store = new SqliteCompanyStore(alteredPath)) store.Save(altered.Company);
            using (var store = new SqliteCompanyStore(directPath)) store.Save(direct.Company);

            using var alteredStore = new SqliteCompanyStore(alteredPath);
            using var directStore = new SqliteCompanyStore(directPath);
            var alteredBack = alteredStore.Load(altered.Company.Id)!;
            var directBack = directStore.Load(direct.Company.Id)!;

            Assert.Equal(
                DerivedStateSnapshot.Snapshot(directBack, AsOf),
                DerivedStateSnapshot.Snapshot(alteredBack, AsOf));
        }
        finally
        {
            TempDbFile.Delete(alteredPath);
            TempDbFile.Delete(directPath);
        }
    }

    [Fact]
    public void A_rejected_alteration_round_trips_to_the_untouched_original()
    {
        var dbPath = TempDbFile.NewPath("replace-rejected");
        try
        {
            var tenthId = Guid.NewGuid();
            var book = Build(WrongTotal, tenthId);

            var unbalanced = new Voucher(
                tenthId, book.SalesType.Id, Books.AddDays(3),
                new[]
                {
                    new EntryLine(book.Customer.Id, RightTotal, DrCr.Debit),
                    new EntryLine(book.Sales.Id, RightTotal - Money.FromRupees(1m), DrCr.Credit),
                });
            Assert.Throws<UnbalancedVoucherException>(() => book.Service.Replace(tenthId, unbalanced));

            using (var store = new SqliteCompanyStore(dbPath)) store.Save(book.Company);
            using var reopened = new SqliteCompanyStore(dbPath);
            var loaded = reopened.Load(book.Company.Id)!;

            var index = loaded.Vouchers.ToList().FindIndex(v => v.Id == tenthId);
            Assert.Equal(9, index);
            Assert.Equal(10, loaded.Vouchers[index].Number);
            Assert.Equal(WrongTotal, loaded.Vouchers[index].TotalDebit);
        }
        finally
        {
            TempDbFile.Delete(dbPath);
        }
    }

    [Fact]
    public void The_carried_bank_reconciliation_date_survives_the_round_trip()
    {
        var dbPath = TempDbFile.NewPath("replace-bankdate");
        try
        {
            var company = CompanyFactory.CreateSeeded("Bank RT Co", Books, Books);
            var bank = new Apex.Ledger.Domain.Ledger(
                Guid.NewGuid(), "HDFC Current", company.FindGroupByName("Bank Accounts")!.Id,
                Money.FromRupees(500000m), true);
            company.AddLedger(bank);
            var supplier = new Apex.Ledger.Domain.Ledger(
                Guid.NewGuid(), "A Supplier", company.FindGroupByName("Sundry Creditors")!.Id,
                Money.FromRupees(100000m), false);
            company.AddLedger(supplier);

            var service = new LedgerService(company);
            var paymentType = company.FindVoucherTypeByName("Payment")!;
            var paymentId = Guid.NewGuid();
            var amount = Money.FromRupees(47239.55m);

            Voucher Payment(string? narration) => new(
                paymentId, paymentType.Id, Books.AddDays(4),
                new[]
                {
                    new EntryLine(supplier.Id, amount, DrCr.Debit),
                    new EntryLine(
                        bank.Id, amount, DrCr.Credit,
                        bankAllocation: new BankAllocation(
                            BankTransactionType.ChequeOrDD, "445566", new DateOnly(2024, 4, 5))),
                },
                narration: narration);

            service.Post(Payment("cheque to supplier"));
            Apex.Ledger.Reports.BankReconciliation.SetBankDate(
                company, paymentId, bank.Id, new DateOnly(2024, 4, 9));

            service.Replace(paymentId, Payment("narration changed, nothing else"));

            using (var store = new SqliteCompanyStore(dbPath)) store.Save(company);
            using var reopened = new SqliteCompanyStore(dbPath);
            var loaded = reopened.Load(company.Id)!;

            var line = loaded.FindVoucher(paymentId)!.Lines.Single(l => l.LedgerId == bank.Id);
            Assert.Equal(new DateOnly(2024, 4, 9), line.BankAllocation!.BankDate);
        }
        finally
        {
            TempDbFile.Delete(dbPath);
        }
    }
}
