using System.Text.RegularExpressions;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Microsoft.Data.Sqlite;
using Xunit;
using Domain = Apex.Ledger.Domain;

namespace Apex.Persistence.Sqlite.Tests;

/// <summary>
/// 🔴 <b>THE STRUCTURAL HALF OF THE PHASE 10.11 S4 DELETE GUARDS.</b> The guards live in the engine and cannot see
/// the schema; the schema lives here and cannot see the guards. This file is the join, and it exists because the
/// first cut of <see cref="MasterDeletionRules"/> counted <b>five</b> of the sixteen columns that reference
/// <c>vouchers(id)</c> and <b>five</b> of the eleven that reference <c>stock_items(id)</c> — the lists were
/// transcribed by hand from a prose table in the design record, and every omission was reachable.
///
/// <para><b>Why an omission is severe and not cosmetic.</b> <c>SqliteCompanyStore</c> executes
/// <c>PRAGMA foreign_keys = ON</c> on every connection and <c>Save</c> is a <b>delete-all + full re-insert</b>. A
/// sibling row the guard failed to count therefore does not dangle quietly: the very next Save raises
/// <c>SQLITE_CONSTRAINT_FOREIGNKEY</c> with the master already gone from the in-memory aggregate, and every later
/// save on the open company throws too. The book on screen can never be written again by any screen.
/// <see cref="An_unguarded_child_row_really_does_make_the_company_unsavable"/> demonstrates that mechanism against
/// the real store rather than asserting it.</para>
/// </summary>
public sealed class MasterDeletionForeignKeyCoverageTests
{
    private static readonly DateOnly FyStart = new(2025, 4, 1);

    // ===================================================================== the coverage assertion

    /// <summary>
    /// 🔴 <b>THE GUARD SET EQUALS THE SCHEMA SET.</b> Every <c>REFERENCES vouchers|ledgers|groups|stock_items(id)</c>
    /// column declared in <see cref="Schema.CreateV1"/> must appear in exactly one of
    /// <see cref="MasterDeletionRules.GuardedForeignKeyColumns"/> (a guard counts it) or
    /// <see cref="MasterDeletionRules.ForeignKeyColumnsThatDieWithTheirParent"/> (the row is written from the
    /// parent's own graph and leaves with it), and nothing may be declared that the schema does not have.
    ///
    /// <para>This is the test that makes a future table a DECISION rather than a silent hole: add a column with a
    /// new <c>REFERENCES vouchers(id)</c> and this goes red until someone puts it in a bucket. The failure message
    /// names the columns on both sides, so the reader is told what to decide rather than that something is
    /// wrong.</para>
    /// </summary>
    [Fact]
    public void Every_foreign_key_into_a_deletable_master_is_either_guarded_or_a_child()
    {
        var declared = ForeignKeyColumnsIn(Schema.CreateV1);
        Assert.NotEmpty(declared);   // otherwise the parser silently proves nothing

        var accounted = new HashSet<string>(
            MasterDeletionRules.GuardedForeignKeyColumns
                .Concat(MasterDeletionRules.ForeignKeyColumnsThatDieWithTheirParent),
            StringComparer.Ordinal);

        var unaccounted = declared.Except(accounted, StringComparer.Ordinal).OrderBy(s => s, StringComparer.Ordinal);
        var phantom = accounted.Except(declared, StringComparer.Ordinal).OrderBy(s => s, StringComparer.Ordinal);

        Assert.True(!unaccounted.Any(),
            "These schema foreign keys point at a deletable master and NO delete guard counts them. Each one is a "
            + "permitted delete that makes the open company unsavable. Decide a bucket for each: "
            + string.Join(", ", unaccounted));
        Assert.True(!phantom.Any(),
            "These columns are declared as covered by a delete guard but no longer exist in Schema.CreateV1: "
            + string.Join(", ", phantom));
    }

    /// <summary>
    /// The parser's own control: it must find the columns a reader can verify by eye, in the shapes the schema
    /// actually uses (NOT NULL, NULL, a trailing comment, and the last column of a table with no trailing comma).
    /// Without this the coverage test above could pass by finding nothing.
    /// </summary>
    [Fact]
    public void The_schema_parser_finds_the_columns_a_reader_can_check_by_hand()
    {
        var declared = ForeignKeyColumnsIn(Schema.CreateV1);

        Assert.Contains("entry_lines.voucher_id", declared);
        Assert.Contains("gst_cdn_links.original_invoice_voucher_id", declared);
        Assert.Contains("companies.profit_and_loss_head_id", declared);
        Assert.Contains("pay_heads.under_group_id", declared);
        Assert.Contains("bom_lines.component_stock_item_id", declared);
        Assert.Contains("rcm_documents.supplier_ledger_id", declared);
        Assert.Contains("job_work_order_lines.component_stock_item_id", declared);

        // …and it must NOT invent one: reorder_definitions.target_id carries no REFERENCES clause, which is why the
        // reorder half of the stock-item debt really is a soft dangler and is deliberately left uncounted.
        Assert.DoesNotContain("reorder_definitions.target_id", declared);
    }

    /// <summary>
    /// Every <c>table.column</c> naming a foreign key into <c>vouchers</c>, <c>ledgers</c>, <c>groups</c> or
    /// <c>stock_items</c> in a CREATE-TABLE script. Column definitions are one per line in this schema, so the
    /// column name is the first token on the line and the enclosing table is the most recent CREATE TABLE.
    /// </summary>
    private static HashSet<string> ForeignKeyColumnsIn(string sql)
    {
        var found = new HashSet<string>(StringComparer.Ordinal);
        var table = string.Empty;
        var create = new Regex(@"CREATE\s+TABLE\s+(?:IF\s+NOT\s+EXISTS\s+)?([A-Za-z_][A-Za-z0-9_]*)",
                               RegexOptions.IgnoreCase);
        var reference = new Regex(@"^\s*([A-Za-z_][A-Za-z0-9_]*)\b[^-]*?REFERENCES\s+(vouchers|ledgers|groups|stock_items)\s*\(\s*id\s*\)",
                                  RegexOptions.IgnoreCase);

        foreach (var line in sql.Split('\n'))
        {
            var c = create.Match(line);
            if (c.Success) { table = c.Groups[1].Value; continue; }

            var r = reference.Match(line);
            if (r.Success && table.Length > 0) found.Add($"{table}.{r.Groups[1].Value}");
        }

        return found;
    }

    // ===================================================================== the mechanism, demonstrated

    /// <summary>
    /// 🔴 <b>WHY THE GUARDS EXIST, PROVED AGAINST THE REAL STORE.</b> Remove a voucher while a sibling row still
    /// holds its Guid — the state every uncounted category produced — and <c>Save</c> raises
    /// <c>SQLITE_CONSTRAINT_FOREIGNKEY</c>. The delete is already committed to the in-memory aggregate at that
    /// point, so the book on screen is permanently ahead of the file: the SECOND save, which touches nothing at
    /// all, throws exactly the same way.
    ///
    /// <para>This is a test ABOUT the mechanism, so it deliberately bypasses <see cref="MasterDeletionRules"/> and
    /// calls <c>LedgerService.Delete</c> directly. It would also catch a future change that quietly dropped
    /// <c>PRAGMA foreign_keys = ON</c> and left the guards looking optional.</para>
    /// </summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void An_unguarded_child_row_really_does_make_the_company_unsavable()
    {
        var dbPath = TempDbFile.NewPath("apex-delete-fk-mechanism");
        try
        {
            var (company, voucherId) = SeedOneJournalWithAnRcmDocument();

            using var store = new SqliteCompanyStore(dbPath);
            store.Save(company);                                   // the baseline writes fine

            // The guard would refuse this — that is the whole point of it.
            Assert.Throws<InvalidOperationException>(
                () => MasterDeletionRules.EnsureVoucherDeletable(company, company.FindVoucher(voucherId)!));

            new LedgerService(company).Delete(voucherId);           // …bypassed, the way an uncounted category was
            Assert.Null(company.FindVoucher(voucherId));            // already gone from memory

            var first = Assert.Throws<SqliteException>(() => store.Save(company));
            Assert.Contains("FOREIGN KEY", first.Message, StringComparison.OrdinalIgnoreCase);

            // …and the open company can never be saved again, which is the part that makes it severe.
            Assert.Throws<SqliteException>(() => store.Save(company));
        }
        finally { TempDbFile.Delete(dbPath); }
    }

    /// <summary>
    /// The positive control the mechanism test needs: with the blocking row removed the guard PERMITS the delete,
    /// the delete happens, and <c>Save</c> succeeds and round-trips. Without this the test above could pass against
    /// a store that cannot save anything.
    /// </summary>
    [Fact]
    [Trait("Category", "RoundTrip")]
    public void A_permitted_delete_saves_and_round_trips()
    {
        var dbPath = TempDbFile.NewPath("apex-delete-fk-control");
        try
        {
            var (company, voucherId) = SeedOneJournalWithAnRcmDocument();

            using (var store = new SqliteCompanyStore(dbPath)) store.Save(company);

            foreach (var d in company.RcmDocuments.Where(d => d.SourceVoucherId == voucherId).ToList())
                company.RemoveRcmDocument(d);

            MasterDeletionRules.EnsureVoucherDeletable(company, company.FindVoucher(voucherId)!);   // permitted
            new LedgerService(company).Delete(voucherId);

            using (var store = new SqliteCompanyStore(dbPath)) store.Save(company);

            using var reopened = new SqliteCompanyStore(dbPath);
            var loaded = reopened.Load(company.Id)!;
            Assert.Null(loaded.FindVoucher(voucherId));
            Assert.Empty(loaded.Vouchers);
        }
        finally { TempDbFile.Delete(dbPath); }
    }

    // ===================================================================== fixture

    private static (Company Company, Guid VoucherId) SeedOneJournalWithAnRcmDocument()
    {
        var c = CompanyFactory.CreateSeeded("FK Guard Co", FyStart, FyStart);
        var on = FyStart.AddDays(5);

        var party = new Domain.Ledger(Guid.NewGuid(), "Acme Traders",
            c.FindGroupByName("Sundry Debtors")!.Id, Money.Zero, openingIsDebit: true);
        c.AddLedger(party);
        var sales = new Domain.Ledger(Guid.NewGuid(), "Sales",
            c.FindGroupByName("Sales Accounts")!.Id, Money.Zero, openingIsDebit: false);
        c.AddLedger(sales);

        var journal = c.FindVoucherTypeByName("Journal")!;
        var v = new Voucher(Guid.NewGuid(), journal.Id, on, new[]
        {
            new EntryLine(party.Id, Money.FromRupees(5000m), DrCr.Debit),
            new EntryLine(sales.Id, Money.FromRupees(5000m), DrCr.Credit),
        });
        new LedgerService(c).Post(v);

        // rcm_documents.source_voucher_id — one of the ten columns no guard used to count.
        c.AddRcmDocument(new RcmDocument(Guid.NewGuid(), RcmDocumentKind.SelfInvoice, v.Id, 1, on));

        return (c, v.Id);
    }
}
