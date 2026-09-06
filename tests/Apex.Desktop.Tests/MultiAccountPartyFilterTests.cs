using System;
using System.IO;
using System.Linq;
using System.Text;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Apex.Ledger;
using Apex.Ledger.Domain;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.Tests;

/// <summary>
/// <b>W2-32 / census 12.7 — a letter is addressed to a PERSON, so it may only be produced for a party account.</b>
///
/// <para><b>🔴 THE DEFECT THIS FILE EXISTS FOR, stated as it was measured.</b> The Multi-Account Printing panel
/// loaded <b>every ledger in the company</b> with no filter, and <c>Select All</c> ticked all of them. Two
/// keystrokes on the DEFAULT path — switch the radio to <i>Reminder Letter</i>, press <i>Select All</i>, press
/// <i>Print</i> — therefore produced, on the Robert fixture, eleven letters reading <c>To: Cash</c>,
/// <c>To: Freight Income</c>, <c>To: Office Rent</c> … each with a <b>Total outstanding of 0.00</b>; and under
/// <i>Confirmation of Accounts</i> each of those gained a <c>Confirmed by ____________________ Date ____________</c>
/// signature line. Every figure on them was correct. There was simply nobody to send them to, and no balance any
/// outside party could confirm — a document the books cannot support, issued from an unmodified installation.</para>
///
/// <para><b>The rule now enforced</b> — one predicate, <see cref="MultiAccountPrintProjector.MayProduce"/>:
/// <list type="bullet">
///   <item>A <c>LedgerAccount</c> statement is meaningful for any account and is unrestricted.</item>
///   <item>A <c>ConfirmationOfAccounts</c> is offered for <b>party accounts</b> — under Sundry Debtors or Sundry
///     Creditors, by group ANCESTRY, the same walk <c>LedgerMasterViewModel.IsUnderParty</c> performs. It is
///     direction-neutral, so both sides qualify.</item>
///   <item>🔴 A <c>ReminderLetter</c> is offered for the <b>RECEIVABLE side only</b> — Sundry Debtors. This was
///     the SECOND defect on the same surface, found by review after the party filter shipped: the letter's body
///     is unconditionally receivable-direction ("still outstanding" … "settled at your earliest convenience"),
///     so offering it for a Sundry Creditor produced a demand for payment addressed to somebody we owe, over the
///     bills they raised on us. The party filter above cannot catch it, because a creditor IS a party.</item>
/// </list>
/// There are two independent gates, and both are tested here: the panel does not OFFER an ineligible row for a
/// kind, and <see cref="MultiAccountPrintProjector.Project"/> — which is public — refuses one even if handed it
/// directly.</para>
///
/// <para>The Robert fixture is the right instrument: thirteen ledgers, of which exactly two are parties
/// (<c>Global Traders</c> under Sundry Debtors, <c>HP Diesel Station</c> under Sundry Creditors). So "the filter
/// works" and "the filter filters nothing" are eleven documents apart, not a rounding difference.</para>
/// </summary>
public sealed class MultiAccountPartyFilterTests
{
    private const string Debtor = "Global Traders";
    private const string Creditor = "HP Diesel Station";

    private static MainWindowViewModel Shell(out string tempDir)
    {
        tempDir = Path.Combine(Path.GetTempPath(), "ApexMultiParty_" + Guid.NewGuid().ToString("N"));
        var vm = new MainWindowViewModel(new CompanyStorage(tempDir));
        vm.LoadRobertDemo();
        vm.OpenMultiAccountPrint();
        return vm;
    }

    private static string AsLatin1(byte[] bytes) => Encoding.Latin1.GetString(bytes);

    // ============================================================ what the panel OFFERS

    /// <summary>
    /// The unrestricted kind is genuinely unrestricted: a ledger-account statement is offered for every account,
    /// including the nominal ones. Without this the filter could be "hide everything" and still look correct.
    /// </summary>
    [Fact]
    public void A_ledger_account_statement_is_offered_for_every_account_including_nominal_ones()
    {
        var vm = Shell(out _);
        var panel = vm.MultiAccountPrint!;

        Assert.Equal(MultiAccountDocumentKind.LedgerAccount, panel.DocumentKind);
        Assert.Contains(panel.Accounts, a => a.Name == "Cash");
        Assert.Contains(panel.Accounts, a => a.Name == "Freight Income");
        Assert.Contains(panel.Accounts, a => a.Name == Debtor);
        Assert.True(panel.Accounts.Count >= 13,
            $"the Robert fixture has thirteen ledgers; the statement kind offered {panel.Accounts.Count}");
    }

    /// <summary>
    /// The confirmation of accounts is DIRECTION-NEUTRAL — "please confirm that this balance agrees with your
    /// books" says nothing about who owes whom — so both sides are offered it.
    /// </summary>
    [Fact]
    public void A_confirmation_of_accounts_is_offered_for_both_sides_and_nothing_else()
    {
        var vm = Shell(out _);
        var panel = vm.MultiAccountPrint!;

        panel.DocumentKind = MultiAccountDocumentKind.ConfirmationOfAccounts;

        Assert.Equal(
            new[] { Debtor, Creditor }.OrderBy(n => n, StringComparer.Ordinal).ToArray(),
            panel.Accounts.Select(a => a.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray());
    }

    /// <summary>
    /// 🔴 <b>THE WRONG-DIRECTION DOCUMENT.</b> The reminder letter is NOT direction-neutral: its body reads
    /// "the following amounts still outstanding" and asks that they "be settled at your earliest convenience".
    /// The panel used to offer it for every party — both Sundry Debtors and Sundry Creditors — so an operator
    /// could print, on the default path, a demand for payment addressed to a supplier <b>WE owe</b>, itemising
    /// the bills <b>THEY</b> raised on us. Every figure on it was right; the whole document pointed the wrong
    /// way. Only the receivable side is offered it now.
    /// </summary>
    [Fact]
    public void A_reminder_letter_is_offered_for_the_receivable_side_only()
    {
        var vm = Shell(out _);
        var panel = vm.MultiAccountPrint!;

        panel.DocumentKind = MultiAccountDocumentKind.ReminderLetter;

        Assert.Equal(new[] { Debtor }, panel.Accounts.Select(a => a.Name).ToArray());

        // Non-vacuity: the creditor really is a party — it is offered the confirmation on the very next line —
        // so this is a DIRECTION filter and not the party filter tested above doing the work twice.
        panel.DocumentKind = MultiAccountDocumentKind.ConfirmationOfAccounts;
        Assert.Contains(panel.Accounts, a => a.Name == Creditor);
    }

    /// <summary>
    /// A row that leaves the offered set must leave the JOB with it. Otherwise an operator who ticked Cash for a
    /// statement and then switched to Reminder Letter would print a letter to Cash from a row he can no longer
    /// see to untick — the same defect, made invisible.
    /// </summary>
    [Fact]
    public void A_row_that_leaves_the_offered_set_leaves_the_job_with_it()
    {
        var vm = Shell(out _);
        var panel = vm.MultiAccountPrint!;

        var cash = panel.Accounts.Single(a => a.Name == "Cash");
        cash.IsSelected = true;
        Assert.Equal(1, panel.SelectedCount);

        panel.DocumentKind = MultiAccountDocumentKind.ReminderLetter;

        Assert.False(cash.IsSelected, "the hidden row is still in the job — it can no longer be unticked");
        Assert.Equal(0, panel.SelectedCount);
        Assert.Empty(panel.BuildJob());
    }

    // ============================================================ what actually gets PRINTED

    /// <summary>
    /// 🔴 <b>THE BITE, asserted on the PRINTED BYTES rather than on any flag.</b> The exact two-keystroke path
    /// the defect was measured on: switch to Reminder Letter, Select All, Print — then read the PDF the operator
    /// would be holding. A view-model assertion would not have caught the original defect either, because every
    /// flag involved was correct; what was wrong was the paper.
    /// </summary>
    [Fact]
    public void Select_all_and_print_a_reminder_letter_never_addresses_a_nominal_account()
    {
        var vm = Shell(out _);
        var panel = vm.MultiAccountPrint!;

        panel.DocumentKind = MultiAccountDocumentKind.ReminderLetter;
        panel.SelectAll();
        vm.PrintMultiAccountJob();

        Assert.Equal(Screen.PrintPreview, vm.CurrentScreen);
        string pdf = AsLatin1(vm.PrintPreview!.PdfBytes);

        // The counterparty the books CAN support: somebody who owes us.
        Assert.Contains("To: " + Debtor, pdf, StringComparison.Ordinal);

        // 🔴 And NOT the supplier. A reminder demanding settlement, addressed to a party WE owe, over the bills
        // THEY raised on us, is a document the books cannot support — asserted on the printed bytes because that
        // is the only place the defect ever appeared.
        Assert.DoesNotContain("To: " + Creditor, pdf, StringComparison.Ordinal);
        // Non-vacuity for that negative: a run that produced NO letters at all would satisfy it. The letter's
        // own heading proves the job was really rendered.
        Assert.Contains("Reminder Letter", pdf, StringComparison.Ordinal);

        // The ones they cannot. Each of these was on the paper before the filter existed.
        Assert.DoesNotContain("To: Cash", pdf, StringComparison.Ordinal);
        Assert.DoesNotContain("To: Freight Income", pdf, StringComparison.Ordinal);
        Assert.DoesNotContain("To: Office Rent", pdf, StringComparison.Ordinal);
        Assert.DoesNotContain("To: Truck", pdf, StringComparison.Ordinal);
        Assert.DoesNotContain("To: Robert's Capital", pdf, StringComparison.Ordinal);
    }

    /// <summary>
    /// The same for the confirmation of accounts, whose signature line is the part that makes a nominal-account
    /// copy indefensible: nobody can sign "Confirmed by" for Profit &amp; Loss A/c.
    /// </summary>
    [Fact]
    public void Select_all_and_print_a_confirmation_never_asks_a_nominal_account_to_sign()
    {
        var vm = Shell(out _);
        var panel = vm.MultiAccountPrint!;

        panel.DocumentKind = MultiAccountDocumentKind.ConfirmationOfAccounts;
        panel.SelectAll();
        vm.PrintMultiAccountJob();

        string pdf = AsLatin1(vm.PrintPreview!.PdfBytes);

        Assert.Contains("Confirmed by", pdf, StringComparison.Ordinal);   // the document is real
        Assert.Contains("To: " + Debtor, pdf, StringComparison.Ordinal);
        Assert.DoesNotContain("To: Cash", pdf, StringComparison.Ordinal);
        Assert.DoesNotContain("To: Profit", pdf, StringComparison.Ordinal);
        Assert.DoesNotContain("To: Insurance", pdf, StringComparison.Ordinal);
    }

    // ============================================================ the second, independent gate

    /// <summary>
    /// <see cref="MultiAccountPrintProjector.Project"/> is <c>public</c>. The panel is not the only possible
    /// caller, so the projector refuses a non-party id for the two letters on its own — and still produces the
    /// statement for that same id, proving the refusal is scoped to the document kind and is not a blanket ban.
    /// </summary>
    [Fact]
    public void The_projector_refuses_a_non_party_id_for_a_letter_even_when_handed_it_directly()
    {
        var company = DemoData.BuildRobert("Projector Gate Fixture");
        var cash = company.Ledgers.Single(l => l.Name == "Cash");
        var debtor = company.Ledgers.Single(l => l.Name == Debtor);
        var creditor = company.Ledgers.Single(l => l.Name == Creditor);
        var ids = new[] { cash.Id, debtor.Id, creditor.Id };
        var from = company.BooksBeginFrom;
        var asOf = new DateOnly(2025, 3, 31);

        // 🔴 The reminder letter is receivable-side only, so BOTH Cash and the supplier are dropped and only the
        // debtor survives — and the surviving letter is addressed to him, not merely counted.
        var letters = MultiAccountPrintProjector.Project(
            company, ids, MultiAccountDocumentKind.ReminderLetter, from, asOf);
        Assert.Single(letters);
        Assert.Contains(letters[0].Rows, r => r.Cells.Count > 0 && r.Cells[0] == "To: " + Debtor);
        Assert.DoesNotContain(letters[0].Rows, r => r.Cells.Count > 0 && r.Cells[0] == "To: " + Creditor);

        // The confirmation is direction-neutral, so BOTH parties survive it — proving the reminder's extra
        // refusal above is a direction gate and not the party gate counted twice.
        var confirmations = MultiAccountPrintProjector.Project(
            company, ids, MultiAccountDocumentKind.ConfirmationOfAccounts, from, asOf);
        Assert.Equal(2, confirmations.Count);

        // …and the SAME three ids all produce a statement, so the gate is per-kind, not per-account.
        var statements = MultiAccountPrintProjector.Project(
            company, ids, MultiAccountDocumentKind.LedgerAccount, from, asOf);
        Assert.Equal(3, statements.Count);
    }

    // ============================================================ the drift lock

    /// <summary>
    /// <b>Two walks, one answer.</b> <see cref="MultiAccountPrintProjector.IsPartyAccount"/> and the ledger
    /// master's own <c>IsPartyGroup</c> both decide "is this a party?" by walking group ancestry to Sundry
    /// Debtors / Sundry Creditors. They are separate code, so they can drift — and a drift would mean the ledger
    /// master shows the bill-wise prompts for a ledger this panel refuses to write to, or the reverse. This walks
    /// EVERY group in the seeded company and requires the two to agree on all of them.
    /// </summary>
    [Fact]
    public void The_party_test_agrees_with_the_ledger_master_own_party_group_test_on_every_group()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "ApexPartyDrift_" + Guid.NewGuid().ToString("N"));
        try
        {
            var company = DemoData.BuildRobert("Party Drift Fixture");
            var master = new LedgerMasterViewModel(company, new CompanyStorage(tempDir), () => { });

            Assert.NotEmpty(master.Groups);
            int partyGroups = 0;

            foreach (var group in master.Groups.ToArray())
            {
                master.SelectedGroup = group;
                bool masterSaysParty = master.IsPartyGroup;

                // A ledger under that group, never added to the company — IsPartyAccount reads only GroupId.
                var probe = new DomainLedger(Guid.NewGuid(), "Probe", group.Id, Money.Zero, true);
                bool projectorSaysParty = MultiAccountPrintProjector.IsPartyAccount(company, probe);

                Assert.True(masterSaysParty == projectorSaysParty,
                    $"group '{group.Name}': the ledger master says party={masterSaysParty} but the print "
                  + $"projector says party={projectorSaysParty} — the two ancestry walks have drifted.");

                if (masterSaysParty) partyGroups++;
            }

            // Non-vacuity: a run in which nothing was a party would satisfy every equality above.
            Assert.True(partyGroups >= 2,
                $"only {partyGroups} party group(s) were seen; the seeded chart must carry Sundry Debtors and "
              + "Sundry Creditors, or this test proves nothing");
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }
}
