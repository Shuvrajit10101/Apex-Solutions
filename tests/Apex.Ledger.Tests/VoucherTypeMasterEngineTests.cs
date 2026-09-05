using System;
using System.Linq;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// <b>W2-03 — the Voucher Type master's ENGINE half (census 2.4, 5.10, 5.11).</b>
///
/// <para><b>R7 — fidelity, and the two categories are kept strictly apart.</b>
/// <list type="bullet">
///   <item><b>ATTESTED (help.tallysolutions.com, fetched 2026-09-05):</b> the Voucher Type screen offers a
///     <i>Method of Voucher Numbering</i> of <i>"Automatic, Automatic (Manual Override), Manual, or Multi-user
///     Auto"</i> (voucher-types page), and the dedicated numbering-methods page adds the fifth: <i>"you can also
///     disable the voucher numbering by selecting the None option"</i>. Also attested there: <i>"Provide narration
///     for each ledger in voucher"</i> and <i>"Enable Print voucher after saving to automatically open the Voucher
///     Printing screen"</i>, and that alteration is reached through <i>Alter Master &gt; Voucher Type</i>.</item>
///   <item><b>OURS — the vendor pages are silent (ruling 9, documented divergence):</b> that a <b>predefined</b>
///     voucher type cannot be DELETED (it may be renamed, reconfigured and deactivated); that a type named by any
///     posted voucher, inventory voucher or scenario cannot be deleted; that a type's <b>base kind is immutable
///     once created</b>; and every refusal message string. Nothing here may be re-labelled as fidelity.</item>
/// </list></para>
/// </summary>
public class VoucherTypeMasterEngineTests
{
    private static readonly DateOnly FyStart = new(2024, 4, 1);

    private static Company Seed() => CompanyFactory.CreateSeeded("VType Co", FyStart, FyStart);
    /// <summary>
    /// Both new methods NUMBER AUTOMATICALLY when the caller supplied none — that is what makes them usable
    /// end-to-end rather than a label. Hand-derived: a freshly seeded book has no Journal posted, so the first
    /// automatic number is 1.
    /// </summary>
    [Theory]
    [InlineData("Automatic")]
    [InlineData("AutomaticManualOverride")]
    [InlineData("MultiUserAuto")]
    public void An_auto_numbering_method_assigns_the_next_number_when_the_caller_supplies_none(string method)
    {
        var c = Seed();
        var journal = c.FindVoucherTypeByName("Journal")!;
        journal.Numbering = Enum.Parse<NumberingMethod>(method);

        var v = PostJournal(c, number: 0);

        Assert.Equal(1, v.Number);
    }

    /// <summary>
    /// <b>Manual</b> keeps the number the operator typed, and <b>None</b> leaves it at zero so
    /// <c>VoucherNumberFormatter.Render</c> yields the empty string. Hand-derived from the attested definitions:
    /// Manual = <i>"Manually enter the voucher number in each voucher"</i>; None = <i>"disable the voucher
    /// numbering"</i>.
    /// </summary>
    [Fact]
    public void Manual_keeps_the_typed_number_and_None_leaves_it_unnumbered()
    {
        var manualCo = Seed();
        manualCo.FindVoucherTypeByName("Journal")!.Numbering = NumberingMethod.Manual;
        Assert.Equal(77, PostJournal(manualCo, number: 77).Number);

        var noneCo = Seed();
        var noneType = noneCo.FindVoucherTypeByName("Journal")!;
        noneType.Numbering = NumberingMethod.None;
        var unnumbered = PostJournal(noneCo, number: 0);
        Assert.Equal(0, unnumbered.Number);
        Assert.Equal("", VoucherNumberFormatter.Render(noneType, unnumbered.Number, unnumbered.Date));
    }

    // ─────────────────────────────────────────────── the two attested user flags (census 5.11)
    // ─────────────────────────────────────────────── create / alter / delete / activate (census 2.4)

    [Fact]
    public void Create_adds_an_active_user_type_with_the_chosen_method_and_flags()
    {
        var c = Seed();
        var before = c.VoucherTypes.Count;

        var created = new VoucherTypeService(c).Create(
            "Export Sales", VoucherBaseType.Sales, NumberingMethod.Manual,
            abbreviation: "ExpS", printAfterSaving: true, provideNarrationForEachLedger: true);

        Assert.Equal(before + 1, c.VoucherTypes.Count);
        Assert.Equal("Export Sales", created.Name);
        Assert.Equal(VoucherBaseType.Sales, created.BaseType);
        Assert.Equal(NumberingMethod.Manual, created.Numbering);
        Assert.Equal("ExpS", created.Abbreviation);
        Assert.True(created.PrintAfterSaving);
        Assert.True(created.ProvideNarrationForEachLedger);
        Assert.True(created.IsActive);
        Assert.False(created.IsPredefined);
    }

    [Fact]
    public void Create_refuses_a_blank_name_and_a_duplicate_name_regardless_of_case()
    {
        var c = Seed();
        var svc = new VoucherTypeService(c);

        Assert.Throws<ArgumentException>(() =>
            svc.Create("   ", VoucherBaseType.Sales, NumberingMethod.Automatic));

        var ex = Assert.Throws<InvalidOperationException>(() =>
            svc.Create("jOuRnAl", VoucherBaseType.Journal, NumberingMethod.Automatic));
        Assert.Contains("Journal", ex.Message);
    }

    /// <summary>A PREDEFINED type is fully configurable — this is the route that flips
    /// <see cref="VoucherType.IsActive"/>, which is the only way the shipped payroll voucher types can ever
    /// post (T1-4).</summary>
    [Fact]
    public void Alter_reconfigures_a_predefined_type_including_its_active_flag()
    {
        var c = Seed();
        var journal = c.FindVoucherTypeByName("Journal")!;
        var svc = new VoucherTypeService(c);

        svc.Alter(journal.Id, "Journal Voucher", NumberingMethod.MultiUserAuto, "JV",
            isActive: false, printAfterSaving: true, provideNarrationForEachLedger: false);

        Assert.Equal("Journal Voucher", journal.Name);
        Assert.Equal(NumberingMethod.MultiUserAuto, journal.Numbering);
        Assert.Equal("JV", journal.Abbreviation);
        Assert.False(journal.IsActive);
        Assert.True(journal.PrintAfterSaving);
        Assert.True(journal.IsPredefined);   // altering never un-seeds a type
    }

    /// <summary>OURS (ruling 9): the base kind is immutable, because changing it would silently re-interpret
    /// the accounting direction of every voucher already posted under the type.</summary>
    [Fact]
    public void Alter_never_changes_the_base_kind()
    {
        var c = Seed();
        var journal = c.FindVoucherTypeByName("Journal")!;
        new VoucherTypeService(c).Alter(journal.Id, "Journal", NumberingMethod.Automatic, null,
            isActive: true, printAfterSaving: false, provideNarrationForEachLedger: false);

        Assert.Equal(VoucherBaseType.Journal, journal.BaseType);
        Assert.Null(typeof(VoucherTypeService)
            .GetMethods()
            .Where(m => m.Name == "Alter")
            .SelectMany(m => m.GetParameters())
            .FirstOrDefault(p => p.ParameterType == typeof(VoucherBaseType)));
    }

    [Fact]
    public void Alter_refuses_a_name_already_taken_by_another_type()
    {
        var c = Seed();
        var journal = c.FindVoucherTypeByName("Journal")!;
        var ex = Assert.Throws<InvalidOperationException>(() =>
            new VoucherTypeService(c).Alter(journal.Id, "Payment", NumberingMethod.Automatic, null,
                isActive: true, printAfterSaving: false, provideNarrationForEachLedger: false));
        Assert.Contains("Payment", ex.Message);
    }

    /// <summary>Renaming a type to its OWN name (the operator edited some other field) is not a collision.</summary>
    [Fact]
    public void Alter_allows_a_type_to_keep_its_own_name()
    {
        var c = Seed();
        var journal = c.FindVoucherTypeByName("Journal")!;
        new VoucherTypeService(c).Alter(journal.Id, "Journal", NumberingMethod.Manual, null,
            isActive: true, printAfterSaving: false, provideNarrationForEachLedger: false);
        Assert.Equal(NumberingMethod.Manual, journal.Numbering);
    }

    [Fact]
    public void Delete_removes_an_unused_user_type()
    {
        var c = Seed();
        var svc = new VoucherTypeService(c);
        var created = svc.Create("Scrap Sales", VoucherBaseType.Sales, NumberingMethod.Automatic);
        var before = c.VoucherTypes.Count;

        svc.Delete(created.Id);

        Assert.Equal(before - 1, c.VoucherTypes.Count);
        Assert.Null(c.FindVoucherType(created.Id));
    }

    /// <summary>OURS (ruling 9): the vendor pages do not say whether a predefined type may be removed. Refusing
    /// is the safe half — the 24 seeds are what every F-key route resolves against.</summary>
    [Fact]
    public void Delete_refuses_a_predefined_type_and_says_deactivate_instead()
    {
        var c = Seed();
        var journal = c.FindVoucherTypeByName("Journal")!;
        var ex = Assert.Throws<InvalidOperationException>(() => new VoucherTypeService(c).Delete(journal.Id));
        Assert.Contains("Journal", ex.Message);
        Assert.Contains("deactivate", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>OURS: a type named by a posted voucher cannot be deleted — the refusal counts the documents, the
    /// shape every other <c>MasterDeletionRules</c> guard uses.</summary>
    [Fact]
    public void Delete_refuses_a_type_that_a_posted_voucher_names_and_counts_them()
    {
        var c = Seed();
        var svc = new VoucherTypeService(c);
        var custom = svc.Create("Branch Journal", VoucherBaseType.Journal, NumberingMethod.Automatic);
        PostJournal(c, number: 0, typeId: custom.Id);
        PostJournal(c, number: 0, typeId: custom.Id);

        var ex = Assert.Throws<InvalidOperationException>(() => svc.Delete(custom.Id));
        Assert.Contains("Branch Journal", ex.Message);
        Assert.Contains("2", ex.Message);
    }

    /// <summary>The single-purpose activation verb — this is what a "Show Inactive → activate" gesture calls,
    /// and it is the route that makes the shipped payroll voucher types postable (T1-4).</summary>
    [Fact]
    public void SetActive_flips_the_flag_so_a_deactivated_type_can_be_reached_again()
    {
        var c = Seed();
        var journal = c.FindVoucherTypeByName("Journal")!;
        var svc = new VoucherTypeService(c);

        svc.SetActive(journal.Id, false);
        Assert.False(journal.IsActive);
        Assert.Null(VoucherTypeResolver.ResolveForEntry(c, VoucherBaseType.Journal));

        svc.SetActive(journal.Id, true);
        Assert.True(journal.IsActive);
        Assert.Same(journal, VoucherTypeResolver.ResolveForEntry(c, VoucherBaseType.Journal));
    }

    // ─────────────────────────────────────────────── helpers

    private static Voucher PostJournal(Company c, int number, Guid? typeId = null)
    {
        var party = c.FindLedgerByName("Acme Traders") ?? AddLedger(c, "Acme Traders", "Sundry Debtors");
        var sales = c.FindLedgerByName("Sales") ?? AddLedger(c, "Sales", "Sales Accounts");
        var type = typeId ?? c.FindVoucherTypeByName("Journal")!.Id;

        var v = new Voucher(Guid.NewGuid(), type, new DateOnly(2024, 4, 10), new[]
        {
            new EntryLine(party.Id, Money.FromRupees(5000m), DrCr.Debit),
            new EntryLine(sales.Id, Money.FromRupees(5000m), DrCr.Credit),
        })
        { Number = number };

        return new LedgerService(c).Post(v);
    }

    private static Domain.Ledger AddLedger(Company c, string name, string group)
    {
        var l = new Domain.Ledger(Guid.NewGuid(), name, c.FindGroupByName(group)!.Id, Money.Zero,
            openingIsDebit: true);
        c.AddLedger(l);
        return l;
    }
}
