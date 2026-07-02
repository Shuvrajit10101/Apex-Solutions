using System.IO;
using System.Text.Json;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;

namespace Apex.Ledger.Tests;

/// <summary>
/// Loads a Robert/Bright study fixture (masters + vouchers, referencing masters by name)
/// into a real seeded <see cref="Company"/>, resolving name → id, then posts every voucher
/// through <see cref="LedgerService"/>. This is the regression harness of design §9: the
/// engine, not the fixture, computes the statements the tests then assert.
/// </summary>
public static class FixtureLoader
{
    private static readonly string FixturesDir = Path.Combine(AppContext.BaseDirectory, "Fixtures");

    public sealed record LoadedFixture(Company Company, LedgerService Service, DateOnly AsOf, JsonElement Expected);

    public static LoadedFixture Load(string fileName)
    {
        var path = Path.Combine(FixturesDir, fileName);
        if (!File.Exists(path))
            throw new FileNotFoundException($"Fixture not found: {path}");

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;

        var companyEl = root.GetProperty("company");
        var fyStart = ParseDate(companyEl.GetProperty("financialYearStart").GetString()!);
        var books = ParseDate(companyEl.GetProperty("booksBeginFrom").GetString()!);

        // A fully seeded company (28 groups / 2 ledgers / 24 voucher types), then the
        // fixture's own masters layered on top.
        var company = CompanyFactory.CreateSeeded(companyEl.GetProperty("name").GetString()!, fyStart, books);

        // Groups: every fixture group must already exist in the 28 seeds (by name). We assert
        // the resolved nature matches so the fixture's classification lines up with the engine.
        var masters = root.GetProperty("masters");
        foreach (var g in masters.GetProperty("groups").EnumerateArray())
        {
            var name = g.GetProperty("name").GetString()!;
            var group = company.FindGroupByName(name)
                ?? throw new InvalidOperationException($"Fixture group '{name}' is not a predefined seed group.");
            var declared = MapNature(g.GetProperty("nature").GetString()!);
            if (group.Nature != declared)
                throw new InvalidOperationException(
                    $"Fixture group '{name}' nature {declared} disagrees with seed {group.Nature}.");
        }

        // Ledgers: add each fixture ledger under its named group. If a ledger name collides
        // with a predefined ledger (Cash), reuse the seeded one but set its opening.
        foreach (var l in masters.GetProperty("ledgers").EnumerateArray())
        {
            var name = l.GetProperty("name").GetString()!;
            var underName = l.GetProperty("under").GetString()!;
            var opening = l.GetProperty("openingBalance").GetDecimal();
            var side = l.GetProperty("openingSide").GetString()!;
            var isDebit = string.Equals(side, "Debit", StringComparison.OrdinalIgnoreCase);

            var group = company.FindGroupByName(underName)
                ?? throw new InvalidOperationException($"Ledger '{name}' references unknown group '{underName}'.");

            var existing = company.FindLedgerByName(name);
            if (existing is not null)
            {
                existing.GroupId = group.Id;
                existing.OpeningBalance = Money.FromRupees(opening);
                existing.OpeningIsDebit = isDebit;
            }
            else
            {
                company.AddLedger(new Domain.Ledger(
                    Guid.NewGuid(), name, group.Id, Money.FromRupees(opening), isDebit));
            }
        }

        // Vouchers: resolve type + ledger names, build entry lines, post through the service.
        var service = new LedgerService(company);
        foreach (var v in root.GetProperty("vouchers").EnumerateArray())
        {
            var typeName = v.GetProperty("type").GetString()!;
            var type = company.FindVoucherTypeByName(typeName)
                ?? throw new InvalidOperationException($"Voucher references unknown type '{typeName}'.");
            var date = ParseDate(v.GetProperty("date").GetString()!);
            var narration = v.TryGetProperty("narration", out var n) ? n.GetString() : null;
            var number = v.TryGetProperty("no", out var no) ? no.GetInt32() : 0;

            var lines = new List<EntryLine>();
            foreach (var line in v.GetProperty("lines").EnumerateArray())
            {
                var ledgerName = line.GetProperty("ledger").GetString()!;
                var ledger = company.FindLedgerByName(ledgerName)
                    ?? throw new InvalidOperationException($"Voucher line references unknown ledger '{ledgerName}'.");
                var drcr = string.Equals(line.GetProperty("drCr").GetString(), "Debit", StringComparison.OrdinalIgnoreCase)
                    ? DrCr.Debit : DrCr.Credit;
                var amount = Money.FromRupees(line.GetProperty("amount").GetDecimal());
                lines.Add(new EntryLine(ledger.Id, amount, drcr));
            }

            var voucher = new Voucher(Guid.NewGuid(), type.Id, date, lines, number: number, narration: narration);
            service.Post(voucher);
        }

        var expected = root.GetProperty("expected");
        var asOf = ParseDate(expected.GetProperty("asOf").GetString()!);
        return new LoadedFixture(company, service, asOf, expected.Clone());
    }

    private static DateOnly ParseDate(string s) => DateOnly.Parse(s, System.Globalization.CultureInfo.InvariantCulture);

    private static GroupNature MapNature(string s) => s switch
    {
        "Assets" or "Asset" => GroupNature.Asset,
        "Liabilities" or "Liability" => GroupNature.Liability,
        "Income" or "Incomes" => GroupNature.Income,
        "Expenses" or "Expense" => GroupNature.Expense,
        _ => throw new InvalidOperationException($"Unknown fixture nature '{s}'."),
    };
}
