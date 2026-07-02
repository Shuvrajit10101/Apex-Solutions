using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;

namespace Apex.Desktop.Services;

/// <summary>
/// Builds the "Robert" demo company from an <b>embedded</b> copy of <c>robert.json</c>
/// (Assets/robert.json, shipped as an EmbeddedResource) so the running app never depends
/// on the tests/ folder. Mirrors the engine's fixture loader: seed a company, layer the
/// fixture masters, resolve names → ids, and post all 13 vouchers through
/// <see cref="LedgerService"/> (the real posting path).
/// </summary>
public static class DemoData
{
    /// <summary>The default demo company name.</summary>
    public const string DefaultName = "Robert Transport Services";

    /// <summary>
    /// Creates and fully posts the Robert demo company in memory. Optionally overrides the
    /// company name (used so repeated "Load Robert Demo" clicks don't collide on disk).
    /// </summary>
    public static Company BuildRobert(string? overrideName = null)
    {
        using var doc = JsonDocument.Parse(ReadEmbeddedRobertJson());
        var root = doc.RootElement;

        var companyEl = root.GetProperty("company");
        var fyStart = ParseDate(companyEl.GetProperty("financialYearStart").GetString()!);
        var books = ParseDate(companyEl.GetProperty("booksBeginFrom").GetString()!);
        var name = overrideName ?? companyEl.GetProperty("name").GetString()!;

        var company = CompanyFactory.CreateSeeded(name, fyStart, books);

        var masters = root.GetProperty("masters");

        // Groups: every fixture group must already exist among the 28 seeds (by name).
        foreach (var g in masters.GetProperty("groups").EnumerateArray())
        {
            var gname = g.GetProperty("name").GetString()!;
            _ = company.FindGroupByName(gname)
                ?? throw new InvalidOperationException($"Demo group '{gname}' is not a predefined seed group.");
        }

        // Ledgers: add each under its named group (reuse the seeded Cash ledger if present).
        foreach (var l in masters.GetProperty("ledgers").EnumerateArray())
        {
            var lname = l.GetProperty("name").GetString()!;
            var underName = l.GetProperty("under").GetString()!;
            var opening = l.GetProperty("openingBalance").GetDecimal();
            var side = l.GetProperty("openingSide").GetString()!;
            var isDebit = string.Equals(side, "Debit", StringComparison.OrdinalIgnoreCase);

            var group = company.FindGroupByName(underName)
                ?? throw new InvalidOperationException($"Demo ledger '{lname}' references unknown group '{underName}'.");

            var existing = company.FindLedgerByName(lname);
            if (existing is not null)
            {
                existing.GroupId = group.Id;
                existing.OpeningBalance = Money.FromRupees(opening);
                existing.OpeningIsDebit = isDebit;
            }
            else
            {
                company.AddLedger(new Apex.Ledger.Domain.Ledger(
                    Guid.NewGuid(), lname, group.Id, Money.FromRupees(opening), isDebit));
            }
        }

        // Vouchers: resolve type + ledger names, post through the service (real invariants).
        var service = new LedgerService(company);
        foreach (var v in root.GetProperty("vouchers").EnumerateArray())
        {
            var typeName = v.GetProperty("type").GetString()!;
            var type = company.FindVoucherTypeByName(typeName)
                ?? throw new InvalidOperationException($"Demo voucher references unknown type '{typeName}'.");
            var date = ParseDate(v.GetProperty("date").GetString()!);
            var narration = v.TryGetProperty("narration", out var n) ? n.GetString() : null;
            var number = v.TryGetProperty("no", out var no) ? no.GetInt32() : 0;

            var lines = new List<EntryLine>();
            foreach (var line in v.GetProperty("lines").EnumerateArray())
            {
                var ledgerName = line.GetProperty("ledger").GetString()!;
                var ledger = company.FindLedgerByName(ledgerName)
                    ?? throw new InvalidOperationException($"Demo voucher line references unknown ledger '{ledgerName}'.");
                var drcr = string.Equals(line.GetProperty("drCr").GetString(), "Debit", StringComparison.OrdinalIgnoreCase)
                    ? DrCr.Debit : DrCr.Credit;
                var amount = Money.FromRupees(line.GetProperty("amount").GetDecimal());
                lines.Add(new EntryLine(ledger.Id, amount, drcr));
            }

            var voucher = new Voucher(Guid.NewGuid(), type.Id, date, lines, number: number, narration: narration);
            service.Post(voucher);
        }

        return company;
    }

    private static string ReadEmbeddedRobertJson()
    {
        var asm = Assembly.GetExecutingAssembly();
        // Embedded resource logical name is "<RootNamespace>.Assets.robert.json".
        var resourceName = Array.Find(
            asm.GetManifestResourceNames(),
            n => n.EndsWith("Assets.robert.json", StringComparison.OrdinalIgnoreCase)
                 || n.EndsWith("robert.json", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Embedded resource 'robert.json' not found in Apex.Desktop.");

        using var stream = asm.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Could not open embedded resource '{resourceName}'.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static DateOnly ParseDate(string s) => DateOnly.Parse(s, CultureInfo.InvariantCulture);
}
