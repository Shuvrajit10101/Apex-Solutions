using Apex.Ledger.Domain;

namespace Apex.Ledger.Io;

/// <summary>
/// Bridges a best-effort flat <see cref="CsvImportResult"/> (masters/vouchers authored by <b>name</b>, §DP-5) into
/// the canonical <see cref="CanonicalModel"/> the engine-routed <see cref="CompanyImportService"/> applies — so the
/// CSV path flows through the SAME validate-before-apply / transactional / engine-routed pipeline as JSON and XML
/// (RQ-20..24), rather than a second, divergent apply. Kept in the Io layer (ER-12) so the thin Avalonia layer holds
/// no import logic: the UI parses, bridges, and calls <see cref="CompanyImportService.Apply"/> uniformly.
/// <para>
/// It is <b>target-aware</b>: a name that already exists in <paramref name="target"/> (a seeded group like
/// "Capital Account", a seed voucher type like "Receipt", an existing ledger) resolves to that master's real id, so
/// <see cref="CompanyImportService"/>'s name-based re-mapping is idempotent; a name that is new gets a fresh id and
/// is created through the engine. The company header + payload for kinds CSV does not carry (units, stock, GST) are
/// left empty, and the target's own header is preserved (CSV imports data <i>into</i> an open company, it does not
/// redefine it). Never throws — a structural problem returns <c>(null, errors)</c>.
/// </para>
/// </summary>
public static class CsvCanonicalBridge
{
    /// <summary>
    /// Projects <paramref name="csv"/> into a canonical model resolved against <paramref name="target"/>. Any parse
    /// errors already collected in <paramref name="csv"/> are surfaced first (reject-batch); a group/ledger/voucher
    /// referencing a group or voucher type that is neither in the CSV nor present in the target is reported here so
    /// the caller rejects the batch before the engine stage.
    /// </summary>
    public static (CanonicalModel? Model, IReadOnlyList<string> Errors) ToModel(CsvImportResult csv, Company target)
    {
        ArgumentNullException.ThrowIfNull(csv);
        ArgumentNullException.ThrowIfNull(target);

        var errors = new List<string>(csv.Errors);
        if (errors.Count > 0) return (null, errors);

        // name (case-insensitive) → the id every reference to that name will use. A name already in the target maps
        // to the target's real id; a name new to this import gets a fresh id and will be created by the engine.
        var groupId = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        var ledgerId = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

        Guid GroupIdFor(string name) =>
            target.FindGroupByName(name) is { } g ? g.Id
            : groupId.TryGetValue(name, out var id) ? id
            : (groupId[name] = Guid.NewGuid());

        Guid LedgerIdFor(string name) =>
            target.FindLedgerByName(name) is { } l ? l.Id
            : ledgerId.TryGetValue(name, out var id) ? id
            : (ledgerId[name] = Guid.NewGuid());

        // ---- Groups (only NEW ones become DTOs; an existing target group is referenced, not re-created) ----
        var groupDtos = new List<GroupDto>();
        foreach (var g in csv.Groups)
        {
            if (target.FindGroupByName(g.Name) is not null)
            {
                groupId[g.Name] = target.FindGroupByName(g.Name)!.Id; // reference the existing group by its id
                continue;
            }
            Guid? parent = null;
            if (!string.IsNullOrWhiteSpace(g.Under))
            {
                if (target.FindGroupByName(g.Under) is null &&
                    !csv.Groups.Any(x => string.Equals(x.Name, g.Under, StringComparison.OrdinalIgnoreCase)))
                {
                    errors.Add($"Group '{g.Name}' is under '{g.Under}', which is neither in the CSV nor in the company.");
                    continue;
                }
                parent = GroupIdFor(g.Under!);
            }
            if (!TryParseNature(g.Nature, parent, target, csv, out var nature, out var natureError))
            {
                errors.Add($"Group '{g.Name}': {natureError}");
                continue;
            }
            groupDtos.Add(new GroupDto
            {
                Id = GroupIdFor(g.Name), Name = g.Name, Nature = nature.ToString(),
                ParentId = parent, IsPredefined = false,
            });
        }

        // ---- Ledgers (only NEW ones become DTOs) ----
        var ledgerDtos = new List<LedgerDto>();
        foreach (var l in csv.Ledgers)
        {
            if (target.FindLedgerByName(l.Name) is not null)
            {
                ledgerId[l.Name] = target.FindLedgerByName(l.Name)!.Id;
                continue;
            }
            if (target.FindGroupByName(l.Under) is null &&
                !csv.Groups.Any(x => string.Equals(x.Name, l.Under, StringComparison.OrdinalIgnoreCase)))
            {
                errors.Add($"Ledger '{l.Name}' is under '{l.Under}', which is neither in the CSV nor in the company.");
                continue;
            }
            ledgerDtos.Add(new LedgerDto
            {
                Id = LedgerIdFor(l.Name), Name = l.Name, GroupId = GroupIdFor(l.Under),
                OpeningBalancePaisa = l.OpeningBalancePaisa, OpeningIsDebit = l.OpeningIsDebit,
                IsPredefined = false,
            });
        }

        // ---- Vouchers (referenced ledgers/type must resolve; balance is re-checked by the engine) ----
        var voucherDtos = new List<VoucherDto>();
        var voucherNumber = 0;
        foreach (var v in csv.Vouchers)
        {
            var type = target.FindVoucherTypeByName(v.Type);
            if (type is null)
            {
                errors.Add($"Voucher '{v.Ref}' references voucher type '{v.Type}', which the company does not have.");
                continue;
            }
            foreach (var line in v.Lines)
                if (target.FindLedgerByName(line.Ledger) is null &&
                    !csv.Ledgers.Any(x => string.Equals(x.Name, line.Ledger, StringComparison.OrdinalIgnoreCase)))
                    errors.Add($"Voucher '{v.Ref}' references ledger '{line.Ledger}', which is neither in the CSV nor in the company.");

            voucherDtos.Add(new VoucherDto
            {
                Id = Guid.NewGuid(), TypeId = type.Id, Number = ++voucherNumber, Date = v.Date,
                Narration = string.IsNullOrWhiteSpace(v.Narration) ? null : v.Narration,
                Lines = v.Lines.Select(cl => new EntryLineDto
                {
                    LedgerId = LedgerIdFor(cl.Ledger), AmountPaisa = cl.AmountPaisa,
                    Side = cl.IsDebit ? nameof(DrCr.Debit) : nameof(DrCr.Credit),
                }).ToList(),
            });
        }

        if (errors.Count > 0) return (null, errors);

        // Preserve the OPEN company's own header — CSV brings data in, it does not redefine the company.
        var model = new CanonicalModel
        {
            FormatVersion = CanonicalMapper.FormatVersion,
            SchemaVersion = CanonicalMapper.SchemaVersion,
            Company = HeaderOf(target),
            Payload = new PayloadDto
            {
                Groups = groupDtos,
                Ledgers = ledgerDtos,
                Vouchers = voucherDtos,
            },
        };
        return (model, errors);
    }

    /// <summary>The nature of a CSV group: read from its Nature column (canonical or fixture spelling), else inherited
    /// from an existing/parent group, else an error (a primary group must declare its nature).</summary>
    private static bool TryParseNature(
        string? natureText, Guid? parentId, Company target, CsvImportResult csv,
        out GroupNature nature, out string error)
    {
        error = string.Empty;
        nature = GroupNature.Asset;

        if (!string.IsNullOrWhiteSpace(natureText))
        {
            var normalized = natureText.Trim().TrimEnd('s'); // "Assets" → "Asset", etc.
            if (Enum.TryParse(normalized, ignoreCase: true, out nature)) return true;
            error = $"nature '{natureText}' is not one of Asset/Liability/Income/Expense.";
            return false;
        }

        // No nature given — inherit from the parent group (target's, if the parent exists there).
        if (parentId is { } pid && target.FindGroup(pid) is { } pg)
        {
            nature = pg.Nature;
            return true;
        }
        error = "a primary group (no parent) must declare a Nature.";
        return false;
    }

    private static CompanyDto HeaderOf(Company c) => new()
    {
        Id = c.Id,
        Name = c.Name,
        MailingName = c.MailingName,
        Address = c.Address,
        Country = c.Country,
        State = c.State,
        Pin = c.Pin,
        FinancialYearStart = c.FinancialYearStart.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
        BooksBeginFrom = c.BooksBeginFrom.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
        BaseCurrencySymbol = c.BaseCurrencySymbol,
        BaseCurrencyName = c.BaseCurrencyName,
        DecimalPlaces = c.DecimalPlaces,
        DecimalUnitName = c.DecimalUnitName,
        Gst = null,
    };
}
