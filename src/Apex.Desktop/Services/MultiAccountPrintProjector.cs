using System;
using System.Collections.Generic;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Io;
using Apex.Ledger.Reports;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.Services;

/// <summary>
/// The document a multi-account print job produces, one per selected account (W2-32).
///
/// <para><b>REACHABLE as of W2-32's finishing pass.</b> The caller is
/// <see cref="Apex.Desktop.ViewModels.MultiAccountPrintViewModel"/>, which is now opened from
/// <b>Reports → Statements of Accounts → Multi-Account Printing</b> and printed with <c>Ctrl+A</c>; that type's
/// header names every link of the route, and <c>MultiAccountPrintReachabilityTests</c> walks it from the menu.
/// It first shipped with the projector and the view model both correct and both reachable by nobody (filed as
/// <c>T2-40</c>) — naming a census row in a doc comment records what code is FOR, never that a row has moved.</para>
///
/// <para>🔴 <b>DOCUMENT CHARACTER (T0-11).</b> <b>None of these three is a tax invoice or a bill of supply, and
/// none may ever become one.</b> Entitlement to ISSUE a tax invoice is CGST s31(1), which puts it on the
/// SUPPLIER of the supply it evidences; these are statements and letters ABOUT an account, issued to a
/// counterparty about a balance, and they evidence no supply at all. They are therefore rendered through
/// <see cref="ReportPdf"/> and never through <c>InvoicePdf</c> — whose refusal to head a recipient-side document
/// "TAX INVOICE" is a safety property this slice does not touch, weaken, or route around.</para>
///
/// <para><b>DIVERGENCE, OURS (ruling 9).</b> The three title strings below — "Ledger Account", "Reminder Letter",
/// "Confirmation of Accounts" — and the body wording, column sets and layout of each are OURS. The corpus is
/// gone (ruling 14) and no admissible source states the reference product's wording or shape for any of them, so
/// they are a documented divergence and can never join the compared set. The <b>figures</b> are not a divergence:
/// every one comes from the same <see cref="LedgerBook"/> / <see cref="Outstandings"/> projection the on-screen
/// reports read, so a printed statement reconciles to the books exactly.</para>
/// </summary>
public enum MultiAccountDocumentKind
{
    /// <summary>The account statement: opening, every posting with a running balance, closing.</summary>
    LedgerAccount,

    /// <summary>A letter to a party listing its overdue open bills and the total due.</summary>
    ReminderLetter,

    /// <summary>A balance-confirmation request: the closing balance as at a date plus the open bills behind it.</summary>
    ConfirmationOfAccounts,
}

/// <summary>
/// Projects a SET of selected accounts into a set of <see cref="PrintReport"/> documents for the multi-account
/// print job (W2-32 / census 12.6). Pure and Avalonia-free: it reads the engine's projections and shapes
/// columns/rows, and never touches disk, dialogs, OS-print or the clock (ER-12). The "as at" date is passed in
/// by the caller for the same reason.
///
/// <para>Amounts are formatted through <see cref="IndianFormat"/>, exactly as the on-screen reports format them,
/// so a printed figure is the figure the grid shows (RQ-15). Text is folded to ASCII through the shared
/// projector helper because the PDF writer's standard-14 faces render an unmapped glyph as '?'.</para>
/// </summary>
public static class MultiAccountPrintProjector
{
    /// <summary>
    /// 🔴 <b>THE ONE PLACE A DATE BECOMES TEXT ON THESE DOCUMENTS — and it must never read the ambient culture.</b>
    ///
    /// <para>Every date this projector prints used to be formatted with a bare
    /// <c>DateOnly.ToString("dd-MM-yyyy")</c>. A bare custom format string reads
    /// <see cref="System.Globalization.CultureInfo.CurrentCulture"/>, and <b>CurrentCulture carries a
    /// CALENDAR</b>: measured, the Robert fixture's period printed <c>01-04-2563</c> on a Thai machine
    /// (<c>th-TH</c>, Buddhist era — the year moves by 543) and <c>08-08-1441</c> on <c>ar-SA</c> (Umm al-Qura —
    /// the day and month move too), against the <c>01-04-2020</c> the books actually carry. Same figures, wrong
    /// dates: a confirmation of accounts asking a counterparty to confirm a balance "as at" a date that is not the
    /// date of the balance.</para>
    ///
    /// <para>No CI leg could see it — ubuntu, windows and macos all resolve to an English/invariant ambient
    /// culture — and the projector had no caller at all until this slice gave it a menu route, so this is the pass
    /// in which those sites became paper. Every other printed document already formats through the invariant
    /// culture (<c>VoucherPrintProjector</c>, nine sites); this projector was the sole exception.
    /// <c>MultiAccountPrintCultureTests</c> pins it by comparing rendered bytes across four locales.</para>
    /// </summary>
    private static string Day(DateOnly date) =>
        date.ToString("dd-MM-yyyy", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The heading each document kind carries. OURS (ruling 9) — see <see cref="MultiAccountDocumentKind"/>.</summary>
    public static string TitleFor(MultiAccountDocumentKind kind) => kind switch
    {
        MultiAccountDocumentKind.ReminderLetter => "Reminder Letter",
        MultiAccountDocumentKind.ConfirmationOfAccounts => "Confirmation of Accounts",
        _ => "Ledger Account",
    };

    /// <summary>The label the print panel and the preview column show for a job of this kind.</summary>
    public static string JobTitleFor(MultiAccountDocumentKind kind) => kind switch
    {
        MultiAccountDocumentKind.ReminderLetter => "Reminder Letters",
        MultiAccountDocumentKind.ConfirmationOfAccounts => "Confirmation of Accounts",
        _ => "Ledger Accounts",
    };

    /// <summary>
    /// 🔴 <b>Whether a document ADDRESSED TO A COUNTERPARTY may be produced for this account.</b>
    ///
    /// <para>A <see cref="MultiAccountDocumentKind.LedgerAccount"/> statement is meaningful for ANY account — an
    /// operator legitimately prints the Sales or Office Rent ledger. The other two are not statements, they are
    /// <b>letters to a person</b>: the reminder letter opens "To: …" and asks for settlement, and the confirmation
    /// of accounts asks that person to confirm the balance and carries a "Confirmed by ____ Date ____" signature
    /// line. Addressing either to <c>Cash</c>, <c>Sales Account</c> or <c>Profit &amp; Loss A/c</c> produces a
    /// document the books cannot support — there is no counterparty to send it to and no balance anybody outside
    /// the company could confirm. That is what this predicate refuses.</para>
    ///
    /// <para><b>The test is group ANCESTRY — under Sundry Debtors or Sundry Creditors</b> — walked exactly as
    /// <c>LedgerMasterViewModel.IsUnderParty</c> walks it when the ledger master decides whether a ledger is a
    /// party (that is the same walk which defaults "Maintain balances bill-by-bill" on). It is deliberately NOT
    /// <see cref="Apex.Ledger.Domain.Ledger.MaintainBillByBill"/>: bill-wise tracking is a bookkeeping OPTION an
    /// operator may switch off on a real debtor, and switching it off must not make that debtor unreachable by a
    /// reminder. <c>MultiAccountPartyFilterTests</c> locks this against the ledger master's own
    /// <c>IsPartyGroup</c>, so the two walks cannot drift apart silently.</para>
    ///
    /// <para>The 64-step guard mirrors the ledger master's: a cyclic parent chain terminates rather than hangs.</para>
    ///
    /// <para><b>It is a COUNTERPARTY test, not a DIRECTION test.</b> Both sides are parties, and the confirmation
    /// of accounts is direction-neutral, so this is the right gate for that document. The reminder letter is not
    /// direction-neutral — see <see cref="IsReceivableAccount"/>.</para>
    /// </summary>
    public static bool IsPartyAccount(Company company, DomainLedger ledger) =>
        IsUnderEither(company, ledger, "Sundry Debtors", "Sundry Creditors");

    /// <summary>
    /// 🔴 <b>Whether this account is on the RECEIVABLE side — under Sundry Debtors — which is the only side the
    /// reminder letter's own text can be true of.</b>
    ///
    /// <para><see cref="IsPartyAccount"/> admits Sundry CREDITORS too, and for the confirmation of accounts that
    /// is correct: "please confirm that this balance agrees with your books" says nothing about who owes whom, so
    /// a supplier can be asked it exactly as a customer can. The reminder letter cannot. Its body is
    /// unconditionally receivable-direction — it states "the following amounts still outstanding" over
    /// <see cref="Outstandings.OpenBillsFor"/> and then asks that "the overdue amounts above could be settled at
    /// your earliest convenience". Addressed to a Sundry Creditor that is a demand for payment sent to somebody
    /// <b>WE owe</b>, over the bills <b>THEY</b> raised on us: every figure on it correct, and the whole document
    /// pointing the wrong way. The books cannot support it, so it is not produced.</para>
    ///
    /// <para><b>OURS (ruling 9).</b> The corpus is gone (ruling 14) and no admissible source states the reference
    /// product's eligibility rule for this letter, so the restriction is our decision — taken because the wording
    /// at <c>ReminderLetter</c> is ours too and is receivable-direction. If the wording is ever made
    /// direction-aware, this gate is what should widen, not the letter that quietly points both ways.</para>
    ///
    /// <para>Deliberately GROUP ancestry, not the sign of the balance. A debtor who happens to sit in credit
    /// (a customer in advance) is still somebody we invoice, and a supplier we have advanced money to is still
    /// not; and a balance flips with the next voucher, so a sign test would make eligibility for a letter change
    /// under the operator between opening the panel and pressing Print.</para>
    /// </summary>
    public static bool IsReceivableAccount(Company company, DomainLedger ledger) =>
        IsUnderEither(company, ledger, "Sundry Debtors", null);

    /// <summary>The shared ancestry walk. <paramref name="second"/> may be null to test a single group.</summary>
    private static bool IsUnderEither(Company company, DomainLedger ledger, string first, string? second)
    {
        ArgumentNullException.ThrowIfNull(company);
        ArgumentNullException.ThrowIfNull(ledger);

        var g = company.FindGroup(ledger.GroupId);
        var guard = 0;
        while (g is not null && guard++ < 64)
        {
            if (g.Name.Equals(first, StringComparison.OrdinalIgnoreCase) ||
                (second is not null && g.Name.Equals(second, StringComparison.OrdinalIgnoreCase)))
                return true;
            g = g.ParentId is { } pid ? company.FindGroup(pid) : null;
        }
        return false;
    }

    // There is deliberately no `AddressesACounterparty(kind)` helper any more. It existed to answer "does an
    // eligibility gate apply here at all?", and both of its callers now ask MayProduce directly — which answers
    // the sharper question and cannot give the two letters the same answer when they no longer have one. Left in
    // place it would have been a public method with zero references: the failure mode this file's own history
    // records (T2-40), and the one review has just finished removing one layer up.

    /// <summary>
    /// 🔴 <b>THE ONE ELIGIBILITY RULE — may a document of this kind be produced for this account?</b> Both gates
    /// (the panel's offered list and <see cref="Project"/>'s own refusal) ask this, so they cannot drift.
    ///
    /// <list type="bullet">
    ///   <item><see cref="MultiAccountDocumentKind.LedgerAccount"/> — ANY account. A statement is meaningful for
    ///     Sales or Office Rent; an operator legitimately prints those.</item>
    ///   <item><see cref="MultiAccountDocumentKind.ConfirmationOfAccounts"/> — any PARTY
    ///     (<see cref="IsPartyAccount"/>). Direction-neutral, so both sides qualify.</item>
    ///   <item><see cref="MultiAccountDocumentKind.ReminderLetter"/> — RECEIVABLE-side parties only
    ///     (<see cref="IsReceivableAccount"/>). The letter demands payment; see that predicate.</item>
    /// </list>
    /// </summary>
    public static bool MayProduce(MultiAccountDocumentKind kind, Company company, DomainLedger ledger) => kind switch
    {
        MultiAccountDocumentKind.ReminderLetter => IsReceivableAccount(company, ledger),
        MultiAccountDocumentKind.ConfirmationOfAccounts => IsPartyAccount(company, ledger),
        _ => true,
    };

    /// <summary>
    /// Builds one document per id in <paramref name="ledgerIds"/>, in the order given. An id that names no
    /// ledger is skipped rather than throwing — a stale selection must not make the whole job unprintable.
    ///
    /// <para>🔴 <b>And an id that names an INELIGIBLE account is skipped for the two counterparty letters</b>
    /// (<see cref="MayProduce"/> — any party for the confirmation, receivable-side parties only for the reminder).
    /// The panel already offers only eligible accounts for those kinds, so this is the second of two independent
    /// gates: this method is <c>public</c>, and the two things it must never do are hand back a "Reminder Letter"
    /// addressed "To: Cash", or one addressed to a Sundry Creditor demanding settlement of money we owe, because
    /// some caller passed the wrong set. Skipping — rather than throwing — matches the unknown-id rule directly
    /// above it: one bad id must not make the rest of a job unprintable, and a job that ends up empty opens no
    /// preview at all.</para>
    /// </summary>
    public static IReadOnlyList<PrintReport> Project(
        Company company,
        IReadOnlyList<Guid> ledgerIds,
        MultiAccountDocumentKind kind,
        DateOnly from,
        DateOnly asOf)
    {
        ArgumentNullException.ThrowIfNull(company);
        ArgumentNullException.ThrowIfNull(ledgerIds);

        var documents = new List<PrintReport>(ledgerIds.Count);
        foreach (var id in ledgerIds)
        {
            var ledger = FindLedger(company, id);
            if (ledger is null) continue;
            if (!MayProduce(kind, company, ledger)) continue;
            documents.Add(kind switch
            {
                MultiAccountDocumentKind.ReminderLetter => ReminderLetter(company, ledger, asOf),
                MultiAccountDocumentKind.ConfirmationOfAccounts => Confirmation(company, ledger, asOf),
                _ => LedgerAccount(company, ledger, from, asOf),
            });
        }
        return documents;
    }

    private static DomainLedger? FindLedger(Company company, Guid id)
    {
        foreach (var l in company.Ledgers)
            if (l.Id == id) return l;
        return null;
    }

    // ------------------------------------------------------------------ the ledger account statement

    private static PrintReport LedgerAccount(Company company, DomainLedger ledger, DateOnly from, DateOnly asOf)
    {
        var book = LedgerBook.Build(company, ledger.Id, from, asOf);
        var rows = new List<PrintRow>
        {
            PrintRow.Header(
                "Opening Balance", string.Empty, string.Empty,
                book.OpeningSide == DrCr.Debit ? IndianFormat.Amount(book.OpeningAmount) : string.Empty,
                book.OpeningSide == DrCr.Credit ? IndianFormat.Amount(book.OpeningAmount) : string.Empty,
                IndianFormat.Signed(book.OpeningAmount, book.OpeningSide)),
        };

        foreach (var r in book.Rows)
        {
            rows.Add(new PrintRow(
                Day(r.Date),
                ReportPrintProjector.Ascii(r.CounterParticulars ?? string.Empty),
                ReportPrintProjector.Ascii(r.VoucherTypeName)
                    + (string.IsNullOrEmpty(r.FormattedNumber) ? string.Empty : " " + r.FormattedNumber),
                r.Debit.Amount == 0m ? string.Empty : IndianFormat.Amount(r.Debit),
                r.Credit.Amount == 0m ? string.Empty : IndianFormat.Amount(r.Credit),
                IndianFormat.Signed(r.RunningAmount, r.RunningSide)));
        }

        rows.Add(PrintRow.Total(
            "Closing Balance", string.Empty, string.Empty, string.Empty, string.Empty,
            IndianFormat.Signed(book.ClosingAmount, book.ClosingSide)));

        return new PrintReport
        {
            Title = TitleFor(MultiAccountDocumentKind.LedgerAccount) + " - " + ReportPrintProjector.Ascii(ledger.Name),
            Subtitle = ReportPrintProjector.Ascii(company.Name)
                + "  -  " + Day(from) + " to " + Day(asOf),
            Columns = new[]
            {
                new PrintColumn("Date", 1.2, CellAlign.Left),
                new PrintColumn("Particulars", 3.0, CellAlign.Left),
                new PrintColumn("Voucher", 1.6, CellAlign.Left),
                new PrintColumn("Debit", 1.4, CellAlign.Right),
                new PrintColumn("Credit", 1.4, CellAlign.Right),
                new PrintColumn("Balance", 1.6, CellAlign.Right),
            },
            Rows = rows,
        };
    }

    // ------------------------------------------------------------------ the reminder letter
    //
    // 🔴 EVERY SENTENCE BELOW IS RECEIVABLE-DIRECTION and none of it is conditional: "amounts still outstanding"
    // over this party's open bills, then a request that they "be settled". That is why the letter is gated on
    // IsReceivableAccount and not on IsPartyAccount — over a Sundry Creditor the same wording becomes a demand
    // for payment aimed at somebody we owe. If this text is ever made direction-aware, widen the gate; do not
    // leave the text pointing one way and the gate the other.

    private static PrintReport ReminderLetter(Company company, DomainLedger ledger, DateOnly asOf)
    {
        var bills = Outstandings.OpenBillsFor(company, ledger, asOf);
        var rows = new List<PrintRow>
        {
            PrintRow.Header("To: " + ReportPrintProjector.Ascii(ledger.Name), string.Empty, string.Empty, string.Empty),
            new PrintRow("Our records show the following amounts still outstanding as at "
                + Day(asOf) + ".", string.Empty, string.Empty, string.Empty),
            new PrintRow(string.Empty, string.Empty, string.Empty, string.Empty),
            PrintRow.Header("Reference", "Bill Date", "Due Date", "Amount Pending"),
        };

        decimal total = 0m;
        int overdue = 0;
        foreach (var b in bills)
        {
            total += b.Pending.Amount;
            if (b.OverdueDays(asOf) > 0) overdue++;
            rows.Add(new PrintRow(
                ReportPrintProjector.Ascii(b.Reference),
                Day(b.Date),
                Day(b.DueDate),
                IndianFormat.Amount(b.Pending)));
        }

        rows.Add(PrintRow.Total("Total outstanding", string.Empty, string.Empty,
            IndianFormat.AmountAlways(total)));
        rows.Add(new PrintRow(string.Empty, string.Empty, string.Empty, string.Empty));
        rows.Add(new PrintRow(
            overdue > 0
                // OURS (ruling 9): the wording is ours. It states a fact from the books and asks for settlement;
                // it makes no legal demand and threatens nothing, because nothing in this application establishes
                // an entitlement to do either.
                ? "We would be grateful if the overdue amounts above could be settled at your earliest convenience."
                : "This is a statement of the amounts currently open on your account; no amount is overdue.",
            string.Empty, string.Empty, string.Empty));
        rows.Add(new PrintRow(string.Empty, string.Empty, string.Empty, string.Empty));
        rows.Add(new PrintRow("For " + ReportPrintProjector.Ascii(company.Name), string.Empty, string.Empty, string.Empty));

        return new PrintReport
        {
            Title = TitleFor(MultiAccountDocumentKind.ReminderLetter),
            Subtitle = ReportPrintProjector.Ascii(company.Name) + "  -  as at " + Day(asOf),
            Columns = new[]
            {
                new PrintColumn("Reference", 3.0, CellAlign.Left),
                new PrintColumn("Bill Date", 1.4, CellAlign.Left),
                new PrintColumn("Due Date", 1.4, CellAlign.Left),
                new PrintColumn("Amount Pending", 1.8, CellAlign.Right),
            },
            Rows = rows,
        };
    }

    // ------------------------------------------------------------------ the confirmation of accounts

    private static PrintReport Confirmation(Company company, DomainLedger ledger, DateOnly asOf)
    {
        var balance = LedgerBalances.Closing(company, ledger, asOf);
        var bills = Outstandings.OpenBillsFor(company, ledger, asOf);

        var rows = new List<PrintRow>
        {
            PrintRow.Header("To: " + ReportPrintProjector.Ascii(ledger.Name), string.Empty, string.Empty, string.Empty),
            new PrintRow("Our books show the following balance on your account as at "
                + Day(asOf) + ".", string.Empty, string.Empty, string.Empty),
            new PrintRow(string.Empty, string.Empty, string.Empty, string.Empty),
            PrintRow.Total("Balance per our books", string.Empty, string.Empty,
                IndianFormat.SignedAlways(balance.Amount, balance.Side)),
            new PrintRow(string.Empty, string.Empty, string.Empty, string.Empty),
        };

        if (bills.Count > 0)
        {
            rows.Add(PrintRow.Header("Reference", "Bill Date", "Due Date", "Amount Pending"));
            foreach (var b in bills)
                rows.Add(new PrintRow(
                    ReportPrintProjector.Ascii(b.Reference),
                    Day(b.Date),
                    Day(b.DueDate),
                    IndianFormat.Amount(b.Pending)));
            rows.Add(new PrintRow(string.Empty, string.Empty, string.Empty, string.Empty));
        }

        // OURS (ruling 9): the confirmation wording is ours. It asks the counterparty to confirm or state its own
        // figure — it asserts nothing about which figure is right.
        rows.Add(new PrintRow(
            "Please confirm that this balance agrees with your books, or advise us of the difference.",
            string.Empty, string.Empty, string.Empty));
        rows.Add(new PrintRow(string.Empty, string.Empty, string.Empty, string.Empty));
        rows.Add(new PrintRow("For " + ReportPrintProjector.Ascii(company.Name), string.Empty, string.Empty, string.Empty));
        rows.Add(new PrintRow("Confirmed by ____________________   Date ____________",
            string.Empty, string.Empty, string.Empty));

        return new PrintReport
        {
            Title = TitleFor(MultiAccountDocumentKind.ConfirmationOfAccounts),
            Subtitle = ReportPrintProjector.Ascii(company.Name) + "  -  as at " + Day(asOf),
            Columns = new[]
            {
                new PrintColumn("Reference", 3.0, CellAlign.Left),
                new PrintColumn("Bill Date", 1.4, CellAlign.Left),
                new PrintColumn("Due Date", 1.4, CellAlign.Left),
                new PrintColumn("Amount Pending", 1.8, CellAlign.Right),
            },
            Rows = rows,
        };
    }
}
