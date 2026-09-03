using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Io;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.Tests;

/// <summary>
/// The shared harness for the Phase 10.11 S5b alteration tests: a REAL seeded company on a throwaway <c>.db</c>,
/// with vouchers posted through the REAL <see cref="VoucherEntryViewModel"/> Accept path rather than hand-built.
///
/// <para><b>Why posting through the screen matters here more than usual.</b> S5b's whole claim is that the
/// rehydration is the INVERSE of the four line writers. A hand-built <see cref="Voucher"/> would let a test
/// construct a shape the screen cannot produce and then "prove" the inverse works on it. Every fixture below
/// therefore goes out through <c>Accept</c> and comes back through <c>ForAlter</c>.</para>
///
/// <para><b>Odd values are the house rule</b> (a 50-paisa defect once survived six round-number assertions), so
/// the amounts here carry paise.</para>
/// </summary>
internal sealed class AlterationBook : IDisposable
{
    public string Dir { get; }
    public CompanyStorage Storage { get; }
    public MainWindowViewModel Shell { get; }
    public Company Company => Shell.Company!;

    private AlterationBook(string dir, CompanyStorage storage, MainWindowViewModel shell)
    {
        Dir = dir;
        Storage = storage;
        Shell = shell;
    }

    public static AlterationBook New(string tag)
    {
        var dir = Path.Combine(Path.GetTempPath(), "ApexAlter_" + tag + "_" + Guid.NewGuid().ToString("N"));
        var storage = new CompanyStorage(dir);
        var shell = new MainWindowViewModel(storage);
        shell.NewCompanyName = "Alter " + tag;
        shell.CreateCompany();
        return new AlterationBook(dir, storage, shell);
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(Dir)) Directory.Delete(Dir, recursive: true);
        }
        catch (IOException) { /* best effort */ }
        catch (UnauthorizedAccessException) { /* best effort */ }
    }

    // ------------------------------------------------------------------ masters

    public DomainLedger Ledger(
        string name,
        string groupName,
        bool billWise = false,
        bool? costApplicable = null,
        Guid? currencyId = null)
    {
        var group = Company.FindGroupByName(groupName)
                    ?? throw new InvalidOperationException($"No seeded group '{groupName}'.");
        var ledger = new DomainLedger(
            Guid.NewGuid(), name, group.Id, Money.Zero, openingIsDebit: true,
            maintainBillByBill: billWise,
            costCentresApplicable: costApplicable,
            currencyId: currencyId);
        Company.AddLedger(ledger);
        return ledger;
    }

    public (CostCategory Category, CostCentre Centre) CostAxis(string categoryName, string centreName)
    {
        var category = new CostCategory(Guid.NewGuid(), categoryName);
        Company.AddCostCategory(category);
        var centre = new CostCentre(Guid.NewGuid(), centreName, category.Id);
        Company.AddCostCentre(centre);
        return (category, centre);
    }

    public Currency ForeignCurrency(string symbol = "$", string code = "USD")
    {
        var currency = new Currency(Guid.NewGuid(), symbol, code);
        Company.AddCurrency(currency);
        return currency;
    }

    /// <summary>
    /// Turns GST on for this book (home state 27, Regular), so the families whose off-line side effect is a GST
    /// record — the Rule-50/51 advance engine and the §34 note link — can be posted through the REAL screens
    /// instead of hand-built. Finding L1-01 is the reason this exists: the shipped advance-adjustment refusal test
    /// hand-built a <see cref="GstAdvanceReceipt"/> carrying the JOURNAL's id in a field the product only ever
    /// fills with an INVOICE's id, and so "proved" a refusal that never fired on anything the screen can produce.
    /// </summary>
    public void EnableGst() =>
        new Apex.Ledger.Services.GstService(Company).EnableGst(new GstConfig
        {
            HomeStateCode = "27",
            Gstin = "27AAPFU0939F1ZV",
            RegistrationType = GstRegistrationType.Regular,
            ApplicableFrom = Company.FinancialYearStart,
            Periodicity = GstReturnPeriodicity.Monthly,
        });

    /// <summary>
    /// Turns TDS on for this book and wires the correct (Tally) model: the EXPENSE ledger is Is-TDS-Applicable and
    /// carries the default Nature of Payment (the expense drives applicability AND the section), while the PARTY
    /// carries a <see cref="DeducteeType"/> and a PAN (the party drives only the RATE). Returns the two ledgers.
    /// </summary>
    public (DomainLedger Expense, DomainLedger Deductee) EnableTds(
        string sectionCode = "194J(b)", string? pan = "AAPFU0939F",
        string expenseName = "Professional Fees", string partyName = "Acme Consultants")
    {
        new Apex.Ledger.Services.TdsTcsService(Company).EnableTds(new TdsConfig { Tan = "MUMA12345B" });
        var expense = Ledger(expenseName, "Indirect Expenses");
        var party = Ledger(partyName, "Sundry Creditors");
        var nature = Company.FindNatureOfPaymentByCode(sectionCode)
                     ?? throw new InvalidOperationException($"No seeded Nature of Payment '{sectionCode}'.");
        expense.TdsApplicable = true;
        expense.TdsNatureOfPaymentId = nature.Id;
        party.DeducteeType = DeducteeType.Firm;
        party.PartyPan = pan;
        return (expense, party);
    }

    public VoucherType Type(VoucherBaseType baseType) =>
        Company.VoucherTypes.First(t => t.BaseType == baseType && !t.IsPosSales && !t.IsStatPaymentType);

    public DateOnly On(int daysAfterYearStart = 5) => Company.FinancialYearStart.AddDays(daysAfterYearStart);

    // ------------------------------------------------------------------ posting

    /// <summary>A fresh entry screen for <paramref name="type"/>, opened in the plain Dr/Cr grid.</summary>
    public VoucherEntryViewModel Entry(VoucherType type, DateOnly? date = null)
    {
        var entry = new VoucherEntryViewModel(
            Company, type, Storage, onSaved: () => { }, onCancelled: () => { }, date ?? On());
        // Every fixture keys Dr/Cr explicitly, so the plain grid is the mode under test. (Single Entry is a
        // re-render of these same lines and is exercised on its own, by SeedAlterationMode's tests.)
        entry.Mode = VoucherEntryMode.AsVoucher;
        return entry;
    }

    public VoucherEntryViewModel Entry(VoucherBaseType baseType, DateOnly? date = null) =>
        Entry(Type(baseType), date);

    /// <summary>Fills the plain grid with <paramref name="legs"/> (adding rows as needed) and accepts.</summary>
    public Voucher Post(
        VoucherType type,
        DateOnly date,
        IEnumerable<(DomainLedger Ledger, DrCr Side, string Amount)> legs,
        string? narration = null,
        Action<VoucherEntryViewModel>? configure = null)
    {
        var entry = Entry(type, date);
        var rows = legs.ToList();
        while (entry.Lines.Count < rows.Count) entry.AddLine();
        for (var i = 0; i < rows.Count; i++)
        {
            entry.Lines[i].SelectedLedger = rows[i].Ledger;
            entry.Lines[i].Side = rows[i].Side;
            entry.Lines[i].AmountText = rows[i].Amount;
        }
        if (narration is not null) entry.Narration = narration;
        configure?.Invoke(entry);

        if (!entry.Accept())
            throw new InvalidOperationException($"Fixture post refused: {entry.Message}");

        return Company.Vouchers.Last(v => v.TypeId == type.Id);
    }

    public Voucher Post(
        VoucherBaseType baseType,
        DateOnly date,
        IEnumerable<(DomainLedger Ledger, DrCr Side, string Amount)> legs,
        string? narration = null,
        Action<VoucherEntryViewModel>? configure = null) =>
        Post(Type(baseType), date, legs, narration, configure);

    /// <summary>
    /// A minimal balanced two-leg voucher of <paramref name="baseType"/> on ordinary (non-party, non-bank,
    /// non-forex, non-bill-wise) ledgers — the cheapest possible SIMPLE specimen for a family.
    /// </summary>
    public Voucher PostPlainPair(VoucherBaseType baseType, decimal amount = 12345.67m, string? narration = null)
    {
        var dr = Company.FindLedgerByName($"Dr Leg {baseType}")
                 ?? Ledger($"Dr Leg {baseType}", "Indirect Expenses");
        var cr = Company.FindLedgerByName($"Cr Leg {baseType}")
                 ?? Ledger($"Cr Leg {baseType}", "Indirect Incomes");
        var text = amount.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return Post(baseType, On(),
            new[] { (dr, DrCr.Debit, text), (cr, DrCr.Credit, text) }, narration);
    }

    // ------------------------------------------------------------------ altering

    public VoucherAlterationOpen ForAlter(Guid voucherId) =>
        VoucherEntryViewModel.ForAlter(Company, voucherId, Storage, onSaved: () => { }, onCancelled: () => { });

    /// <summary>
    /// 🔴 <b>The ER-13 instrument for an alteration.</b> A raw <c>.db</c> byte comparison is unachievable for ANY
    /// book — <c>entry_lines.id</c> is <c>INTEGER PRIMARY KEY AUTOINCREMENT</c> and <c>sqlite_sequence</c> never
    /// reuses ids, so a delete-all + full re-insert renumbers those surrogate ids on every save and an UNTOUCHED
    /// book's bytes differ from themselves. The canonical export carries the semantic model and no surrogate ids,
    /// which is why it is the correct instrument (design §8.3).
    /// </summary>
    public byte[] Export() => CanonicalXml.Export(Company);

    /// <summary>The same export taken from the PERSISTED file, so a round trip is proved on disk and not only in
    /// memory.</summary>
    public byte[] ExportReloaded()
    {
        var entry = Storage.ListCompanies().Single(e => e.Name == Company.Name);
        return CanonicalXml.Export(Storage.Load(entry));
    }
}
