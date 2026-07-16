using System;
using System.IO;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Services;
using Apex.Desktop.Services;
using Apex.Desktop.ViewModels;
using Xunit;
using DomainLedger = Apex.Ledger.Domain.Ledger;

namespace Apex.Desktop.Tests;

/// <summary>
/// Phase 9 UI-3 — the <b>§34 credit/debit-note voucher-entry UI</b> (<see cref="VoucherEntryViewModel"/>; RQ-24; ER-12;
/// DP-27). The engine already nets §34 notes through GSTR-1 / the amendment tables; what the shipped screen lacked was
/// the <b>original-invoice reference</b> that makes a note a §34 note at all. These prove the picker captures it into a
/// real <see cref="GstCreditDebitNoteLink"/>, that ER-12 (a note is never free-floating) and the §34(2) 30-November
/// cut-off are both refused cleanly, and that the §34 details are opt-in so an ordinary note posts unchanged (ER-13).
/// </summary>
public sealed class CreditDebitNoteVoucherEntryViewModelTests : IDisposable
{
    private const string GstinMaharashtra = "27AAPFU0939F1ZV";
    private static readonly DateOnly FyStart = new(2024, 4, 1);
    private static readonly DateOnly InvoiceDate = new(2024, 6, 10);   // FY 2024-25 ⇒ §34(2) cut-off 30-Nov-2025
    private static readonly DateOnly NoteDate = new(2024, 7, 5);
    private static readonly DateOnly PastCutOff = new(2025, 12, 1);    // one day past 30-Nov-2025

    private readonly string _tempDir;
    private readonly CompanyStorage _storage;

    public CreditDebitNoteVoucherEntryViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ApexCdnVoucherTests_" + Guid.NewGuid().ToString("N"));
        _storage = new CompanyStorage(_tempDir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch { /* best-effort cleanup */ }
    }

    private static DomainLedger AddLedger(Company c, string name, string groupName, bool openingIsDebit)
    {
        var l = new DomainLedger(Guid.NewGuid(), name, c.FindGroupByName(groupName)!.Id, Money.Zero, openingIsDebit);
        c.AddLedger(l);
        return l;
    }

    /// <summary>A Regular-GST company carrying one posted Sales invoice (the original supply a note can adjust).</summary>
    private (MainWindowViewModel Vm, Voucher Original, DomainLedger Customer, DomainLedger Sales) GstCompanyWithInvoice(
        string name, bool enableGst = true)
    {
        var vm = new MainWindowViewModel(_storage);
        vm.NewCompanyName = name;
        vm.CreateCompany();
        var c = vm.Company!;
        c.FinancialYearStart = FyStart;
        c.BooksBeginFrom = FyStart;

        if (enableGst)
            new GstService(c).EnableGst(new GstConfig
            {
                HomeStateCode = "27",
                Gstin = GstinMaharashtra,
                RegistrationType = GstRegistrationType.Regular,
                ApplicableFrom = FyStart,
                Periodicity = GstReturnPeriodicity.Monthly,
            });

        var customer = AddLedger(c, "Acme Ltd", "Sundry Debtors", true);
        var sales = c.FindLedgerByName("Sales") ?? AddLedger(c, "Sales", "Sales Accounts", false);

        // The original outward supply: Dr Acme 11,800 / Cr Sales 11,800.
        var salesType = c.VoucherTypes.First(t => t.BaseType == VoucherBaseType.Sales).Id;
        var original = new LedgerService(c).Post(new Voucher(
            Guid.NewGuid(), salesType, InvoiceDate,
            new[]
            {
                new EntryLine(customer.Id, Money.FromRupees(11800m), DrCr.Debit),
                new EntryLine(sales.Id, Money.FromRupees(11800m), DrCr.Credit),
            },
            partyId: customer.Id));

        return (vm, original, customer, sales);
    }

    /// <summary>Opens a Credit/Debit Note and types the ordinary return legs (Dr Sales / Cr Acme for a credit note).</summary>
    private static VoucherEntryViewModel OpenNote(
        MainWindowViewModel vm, DomainLedger dr, DomainLedger cr, decimal amount,
        VoucherBaseType type = VoucherBaseType.CreditNote, DateOnly? date = null)
    {
        vm.OpenVoucher(type);
        var e = vm.VoucherEntry!;
        e.Date = date ?? NoteDate;
        e.Lines[0].SelectedLedger = dr;
        e.Lines[0].Side = DrCr.Debit;
        e.Lines[0].AmountText = amount.ToString(System.Globalization.CultureInfo.InvariantCulture);
        e.Lines[1].SelectedLedger = cr;
        e.Lines[1].Side = DrCr.Credit;
        e.Lines[1].AmountText = amount.ToString(System.Globalization.CultureInfo.InvariantCulture);
        e.Recalculate();
        return e;
    }

    // ================================================================ ER-13 — §34 details are opt-in

    /// <summary>ER-13: the §34 details are opt-in, so an ordinary credit note on a GST company creates <b>no</b> link and
    /// posts exactly as before. (Not every note on a GST company is a §34 note — an exempt or inter-branch adjustment
    /// is not.)</summary>
    [Fact]
    public void Ordinary_note_creates_no_link_and_posts_unchanged()
    {
        var (vm, _, customer, sales) = GstCompanyWithInvoice("Plain Note Co");
        var c = vm.Company!;

        var e = OpenNote(vm, sales, customer, 1180m);

        Assert.True(e.CanBeSection34Note);   // the opt-in checkbox is offered…
        Assert.False(e.IsSection34Note);     // …but off by default
        Assert.False(e.ShowSection34Details);
        Assert.True(e.Accept());

        Assert.Empty(c.CreditDebitNoteLinks);
    }

    /// <summary>ER-13: a GST-off company never even offers the §34 opt-in.</summary>
    [Fact]
    public void Section34_optin_is_absent_on_a_gst_off_company()
    {
        var (vm, _, customer, sales) = GstCompanyWithInvoice("No-GST Note Co", enableGst: false);

        var e = OpenNote(vm, sales, customer, 1180m);

        Assert.False(e.CanBeSection34Note);
        Assert.False(e.ShowSection34Details);
        Assert.True(e.Accept());
        Assert.Empty(vm.Company!.CreditDebitNoteLinks);
    }

    // ================================================================ happy path — the link is captured

    /// <summary>
    /// Picking the original invoice captures a real <see cref="GstCreditDebitNoteLink"/> against the posted note: the
    /// voucher link (what Table 9B / 9C read), the denormalised number + date, the §34 direction derived from the base
    /// type, the reason and the 9B target.
    /// </summary>
    [Fact]
    public void Credit_note_links_the_picked_original_invoice()
    {
        var (vm, original, customer, sales) = GstCompanyWithInvoice("Linked Note Co");
        var c = vm.Company!;

        var e = OpenNote(vm, sales, customer, 1180m);
        e.IsSection34Note = true;
        e.SelectedCdnOriginalInvoice = e.CdnOriginalInvoices.Single(o => o.Invoice?.Id == original.Id);
        e.SelectedCdnReasonCode = "01 Sales return";

        // The §34(2) advisory names the engine's own cut-off (30-Nov of the FY following the 10-Jun-2024 supply).
        Assert.Contains("30-Nov-2025", e.CdnSummary);
        Assert.Contains("within the limit", e.CdnSummary);
        Assert.False(e.CdnPastTimeLimit);

        Assert.True(e.Accept());

        var note = c.Vouchers.OrderBy(v => v.Number).Last();
        var link = Assert.Single(c.CreditDebitNoteLinks);
        Assert.Equal(note.Id, link.CdnVoucherId);
        Assert.Equal(CdnType.Credit, link.CdnType);              // derived from the CreditNote base type
        Assert.Equal(original.Id, link.OriginalInvoiceVoucherId); // the link Table 9B / 9C reads
        Assert.Equal(original.Number.ToString(), link.OriginalInvoiceNumber);
        Assert.Equal(InvoiceDate, link.OriginalInvoiceDate);      // the §34(2) FY basis
        Assert.Equal("01 Sales return", link.ReasonCode);
        Assert.True(link.Is9BTarget);
    }

    /// <summary>A <b>Debit</b> Note carries CdnType.Debit and is uncapped — §34(2) is a credit-note-only cut-off, so a
    /// debit note raised long after the original supply is accepted without any override.</summary>
    [Fact]
    public void Debit_note_is_typed_debit_and_is_not_capped_by_section_34_2()
    {
        var (vm, original, customer, sales) = GstCompanyWithInvoice("Debit Note Co");
        var c = vm.Company!;

        // Dated well past the credit-note cut-off — a debit note has no issuance limit.
        var e = OpenNote(vm, customer, sales, 500m, VoucherBaseType.DebitNote, PastCutOff);
        e.IsSection34Note = true;
        e.SelectedCdnOriginalInvoice = e.CdnOriginalInvoices.Single(o => o.Invoice?.Id == original.Id);
        e.SelectedCdnReasonCode = "04 Correction in invoice";

        Assert.Equal(CdnType.Debit, e.Section34Type);
        Assert.False(e.ShowCdnOverride);                 // no override affordance is needed — nothing to override
        Assert.Contains("No §34(2) issuance cut-off", e.CdnSummary);
        Assert.False(e.CdnPastTimeLimit);

        Assert.True(e.Accept());

        var link = Assert.Single(c.CreditDebitNoteLinks);
        Assert.Equal(CdnType.Debit, link.CdnType);
        Assert.Equal(original.Id, link.OriginalInvoiceVoucherId);
    }

    /// <summary>A consolidated / unregistered note carries no voucher link but MUST carry a typed original-invoice
    /// number (ER-12's second limb) — and its typed date still drives the §34(2) basis.</summary>
    [Fact]
    public void Consolidated_note_links_by_typed_reference()
    {
        var (vm, _, customer, sales) = GstCompanyWithInvoice("Consolidated Note Co");
        var c = vm.Company!;

        var e = OpenNote(vm, sales, customer, 1180m);
        e.IsSection34Note = true;
        e.SelectedCdnOriginalInvoice = e.CdnOriginalInvoices.Single(o => o.IsConsolidated);

        Assert.True(e.ShowCdnConsolidatedFields); // the typed fields appear only for a consolidated reference

        e.CdnOriginalInvoiceNumber = "INV-1042";
        e.CdnOriginalInvoiceDateText = "10-Jun-2024";
        e.SelectedCdnReasonCode = "02 Post-supply discount";
        e.CdnIs9BTarget = false; // an unregistered CDN is not a 9B target

        Assert.True(e.Accept());

        var link = Assert.Single(c.CreditDebitNoteLinks);
        Assert.Null(link.OriginalInvoiceVoucherId);
        Assert.Equal("INV-1042", link.OriginalInvoiceNumber);
        Assert.Equal(InvoiceDate, link.OriginalInvoiceDate);
        Assert.False(link.Is9BTarget);
    }

    // ================================================================ the guards surface cleanly

    /// <summary>ER-12: a §34 note with <b>no original selected</b> is refused — a note is never a free-floating tax
    /// delta. Nothing is posted and no link is created.</summary>
    [Fact]
    public void Note_with_no_original_selected_is_refused()
    {
        var (vm, _, customer, sales) = GstCompanyWithInvoice("Free-Floating Note Co");
        var c = vm.Company!;
        var vouchersBefore = c.Vouchers.Count;

        var e = OpenNote(vm, sales, customer, 1180m);
        e.IsSection34Note = true;
        e.SelectedCdnReasonCode = "01 Sales return";
        // …but no original invoice picked (the "(none selected)" sentinel is still in force).

        Assert.False(e.Accept());
        Assert.Contains("never a free-floating tax delta", e.Message);
        Assert.Empty(c.CreditDebitNoteLinks);
        Assert.Equal(vouchersBefore, c.Vouchers.Count); // nothing posted
    }

    /// <summary>A §34 note with no reason is refused (the link record requires one).</summary>
    [Fact]
    public void Note_with_no_reason_is_refused()
    {
        var (vm, original, customer, sales) = GstCompanyWithInvoice("No-Reason Note Co");
        var c = vm.Company!;

        var e = OpenNote(vm, sales, customer, 1180m);
        e.IsSection34Note = true;
        e.SelectedCdnOriginalInvoice = e.CdnOriginalInvoices.Single(o => o.Invoice?.Id == original.Id);

        Assert.False(e.Accept());
        Assert.Contains("§34 reason", e.Message);
        Assert.Empty(c.CreditDebitNoteLinks);
    }

    /// <summary>
    /// §34(2): a liability-reducing credit note declared after 30-November of the FY following the original supply is
    /// refused — and the refusal names the engine's own cut-off. Ticking the Override then lets it through, proving the
    /// documented escape actually works.
    /// </summary>
    [Fact]
    public void Credit_note_past_the_section_34_2_cutoff_is_refused_until_overridden()
    {
        var (vm, original, customer, sales) = GstCompanyWithInvoice("Late Note Co");
        var c = vm.Company!;

        var e = OpenNote(vm, sales, customer, 1180m, VoucherBaseType.CreditNote, PastCutOff);
        e.IsSection34Note = true;
        e.SelectedCdnOriginalInvoice = e.CdnOriginalInvoices.Single(o => o.Invoice?.Id == original.Id);
        e.SelectedCdnReasonCode = "01 Sales return";

        Assert.True(e.CdnPastTimeLimit);
        Assert.Contains("PAST the 30-Nov-2025 declaration cut-off", e.CdnSummary);
        Assert.False(e.Accept());
        Assert.Contains("past the §34(2) declaration cut-off", e.Message);
        Assert.Empty(c.CreditDebitNoteLinks);

        // The override affordance must be reachable in exactly this state — and must work.
        Assert.True(e.ShowCdnOverride);
        e.CdnOverrideTimeLimit = true;
        Assert.True(e.Accept());
        Assert.Single(c.CreditDebitNoteLinks);
    }

    /// <summary>
    /// §34(2) cannot be verified without the original supply date, so a liability-reducing credit note whose consolidated
    /// reference carries no date is refused (mirroring the engine's own guard) rather than silently bypassing the
    /// cut-off. Crucially the Override affordance is still on screen in this state — a guard whose only stated escape is
    /// invisible is a dead end.
    /// </summary>
    [Fact]
    public void Credit_note_without_an_original_date_is_refused_but_the_override_is_reachable()
    {
        var (vm, _, customer, sales) = GstCompanyWithInvoice("Dateless Note Co");
        var c = vm.Company!;

        var e = OpenNote(vm, sales, customer, 1180m);
        e.IsSection34Note = true;
        e.SelectedCdnOriginalInvoice = e.CdnOriginalInvoices.Single(o => o.IsConsolidated);
        e.CdnOriginalInvoiceNumber = "INV-9001"; // a reference, but no date
        e.SelectedCdnReasonCode = "07 Others";

        Assert.False(e.CdnPastTimeLimit);   // "past the limit" is FALSE here — the limit is simply unverifiable…
        Assert.True(e.ShowCdnOverride);     // …so the override must NOT be gated on CdnPastTimeLimit
        Assert.False(e.Accept());
        Assert.Contains("requires the original supply date", e.Message);
        Assert.Empty(c.CreditDebitNoteLinks);

        e.CdnOverrideTimeLimit = true;
        Assert.True(e.Accept());
        var link = Assert.Single(c.CreditDebitNoteLinks);
        Assert.Null(link.OriginalInvoiceDate);
    }

    /// <summary>
    /// The §34(2) advisory follows the <b>voucher date</b> (Phase 9 UI-3 fix 6). It was wired only to its own field
    /// handlers, never to the header date — so re-dating a note past the cut-off left the panel still asserting "within
    /// the limit" on a note Accept would refuse. An advisory that disagrees with the accept gate is worse than none.
    /// </summary>
    [Fact]
    public void The_section_34_2_advisory_refreshes_when_the_note_date_changes()
    {
        var (vm, original, customer, sales) = GstCompanyWithInvoice("Re-dated Note Co");

        var e = OpenNote(vm, sales, customer, 1180m, VoucherBaseType.CreditNote, NoteDate);
        e.IsSection34Note = true;
        e.SelectedCdnOriginalInvoice = e.CdnOriginalInvoices.Single(o => o.Invoice?.Id == original.Id);
        e.SelectedCdnReasonCode = "01 Sales return";

        // Well within the limit at the opening date.
        Assert.False(e.CdnPastTimeLimit);
        Assert.Contains("within the limit", e.CdnSummary);

        // The operator re-dates the note past the 30-Nov-2025 cut-off — the advisory must follow.
        e.Date = PastCutOff;
        Assert.True(e.CdnPastTimeLimit);
        Assert.Contains("PAST the 30-Nov-2025 declaration cut-off", e.CdnSummary);

        // …and it must agree with what Accept actually does.
        Assert.False(e.Accept());
        Assert.Contains("past the §34(2) declaration cut-off", e.Message);

        // Re-dating back inside the limit clears it again (the advisory is live in both directions).
        e.Date = NoteDate;
        Assert.False(e.CdnPastTimeLimit);
        Assert.Contains("within the limit", e.CdnSummary);
        Assert.True(e.Accept());
    }
}
