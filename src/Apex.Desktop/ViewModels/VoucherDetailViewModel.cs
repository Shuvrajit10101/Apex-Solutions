using System;
using System.Collections.ObjectModel;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;
using Apex.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Apex.Desktop.ViewModels;

/// <summary>
/// The RQ-7 voucher drill target: a read-only view of a single voucher opened when a Day Book row — or a
/// ledger-vouchers row inside a drilled <see cref="LedgerVouchersViewModel"/> — is drilled into (Enter). It
/// shows the voucher header (type, number, date, party, narration, any status flags) and its balanced Dr/Cr
/// entry lines with the totals. It is a terminal (non-drillable) leaf column in the cascade — read-only, so it
/// never mutates the books; UI-toolkit-free so it is unit-testable.
/// </summary>
public sealed partial class VoucherDetailViewModel : ViewModelBase
{
    private readonly Company _company;

    /// <summary>
    /// The voucher this pane projects. 🔴 <b>NOT <c>readonly</c>, and that is the whole of the S5d/S5e review's
    /// stale-pane fix.</b> <c>LedgerService.Replace</c> puts a <b>new</b> <see cref="Voucher"/> object at the same
    /// index (<c>Company.ReplaceVoucherInternal</c>), so after an alteration this field held a DISCARDED object —
    /// not an aliased one that would have self-updated. Re-pointed by <see cref="Refresh"/> alone; every read
    /// below (the rows, the header, both print projections, the e-mail attachment) goes through it, which is why
    /// re-pointing it once is enough.
    /// </summary>
    private Voucher _voucher;

    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private string _subtitle = string.Empty;

    /// <summary>The voucher's stable id — the identity the header/tests key on.</summary>
    public Guid VoucherId { get; }

    /// <summary>True iff Print (P/Ctrl+P) on this drill should produce the GST <b>tax invoice</b> (a Sales
    /// item-invoice) rather than the plain Dr/Cr voucher (RQ-10/RQ-11 routing).</summary>
    public bool IsTaxInvoice => VoucherPrintProjector.IsTaxInvoice(_company, _voucher);

    /// <summary>True iff this voucher must be issued as a <b>Bill of Supply</b> rather than a tax invoice — CGST Act
    /// §31(3)(c), <b>both</b> limbs: a composition dealer's outward supply (§10) <i>and</i> a wholly exempt / nil-rated
    /// / non-GST supply by any registered dealer. W0-1 widened this from the §10 limb alone: it now routes through
    /// <see cref="VoucherPrintProjector.IsBillOfSupply"/>, the SAME predicate the printed document uses, so the badge
    /// on screen and the title on paper can never disagree. A Regular company's taxable supply is unchanged (ER-13).</summary>
    public bool IsBillOfSupply => VoucherPrintProjector.IsBillOfSupply(_company, _voucher);

    /// <summary>The document label the header shows: "Bill of Supply" for a composition or wholly-exempt supply,
    /// "Tax Invoice" for a Regular Sales item/service invoice, else empty (a plain voucher shows only its type name).</summary>
    public string DocumentLabel => IsBillOfSupply ? "Bill of Supply" : IsTaxInvoice ? "Tax Invoice" : string.Empty;

    /// <summary>The declaration CGST Rule 5(1)(f) requires at the top of a <b>composition</b> taxable person's Bill of
    /// Supply (de-branded, ER-11); empty otherwise — including on a <b>regular</b> dealer's exempt Bill of Supply, which
    /// takes the other limb of §31(3)(c) and must not claim composition status.
    /// <para><b>FIX-W1e — this must agree with <see cref="DocumentLabel"/>, and W0-1 briefly let it disagree.</b> The
    /// two properties were split across two different predicates (this one on the §10-only limb — since W0-9 spelled
    /// <c>GstReportSupport.IsCompositionBillOfSupply</c> — and the badge on the whole-of-§31(3)(c) rule),
    /// and MainWindow.axaml renders them one under the other in the same Border, binding this TextBlock's visibility
    /// to the STRING rather than to the document kind. Reachable with no import or tampering: post a taxed sale as a
    /// Regular dealer, then switch Registration Type to Composition in the F11 GST config (which is idempotent and
    /// checks no existing voucher) and re-drill the old sale — the pane read badge "Tax Invoice" with "Composition
    /// taxable person, not eligible to collect tax on supplies" printed directly beneath it, on a document that
    /// demonstrably DID collect tax, while the PDF carried no declaration at all. It is now the CONJUNCTION of both
    /// predicates, which is exactly what <c>ProjectInvoice</c> stamps into
    /// <c>InvoicePrintData.TopDeclaration</c> (<c>billOfSupply ? TopDeclarationFor(…) : empty</c>), so the screen and
    /// the paper cannot differ.</para></summary>
    public string BillOfSupplyDeclaration =>
        IsBillOfSupply && GstReportSupport.IsCompositionBillOfSupply(_company, _voucher)
            ? GstReportSupport.BillOfSupplyDeclaration
            : string.Empty;

    /// <summary>Builds the print-preview VM for this voucher: a tax-invoice preview when it is a Sales
    /// item-invoice, else the plain voucher preview. The Io renderer is chosen by the projection kind.</summary>
    public PrintPreviewViewModel BuildPrintPreview() =>
        IsTaxInvoice
            ? new PrintPreviewViewModel(VoucherPrintProjector.ProjectInvoice(_company, _voucher))
            : new PrintPreviewViewModel(VoucherPrintProjector.ProjectVoucher(_company, _voucher));

    /// <summary>The entry-line rows (Particulars = ledger name, Debit / Credit columns), plus a totals row.</summary>
    public ObservableCollection<ReportRow> Rows { get; } = new();

    public VoucherDetailViewModel(Company company, Voucher voucher)
    {
        if (company is null) throw new ArgumentNullException(nameof(company));
        if (voucher is null) throw new ArgumentNullException(nameof(voucher));

        _company = company;
        _voucher = voucher;
        VoucherId = voucher.Id;

        Project();
    }

    /// <summary>
    /// 🔴 <b>Re-projects this pane from the voucher the books hold NOW.</b> Called by the alteration doors'
    /// <c>onSaved</c> (<c>MainWindowViewModel.ShowVoucherAlteration</c> and <c>ShowPosBillAlteration</c>) once the
    /// replacement is committed.
    ///
    /// <para><b>The defect this closes (S5d/S5e review, C2 — MAJOR / fidelity).</b> Everything this pane shows was
    /// built ONCE in the constructor, and <c>onSaved</c> refreshed the report and the register only. So after an
    /// alteration raised FROM this column the operator was left standing on a pane that still showed the
    /// SUPERSEDED figures — and it is not a cosmetic pane: <c>MainWindowViewModel.OpenPrintPreview</c> takes the
    /// <see cref="Screen.VoucherDetail"/> branch and calls <see cref="BuildPrintPreview"/>, and
    /// <c>EmailComposeViewModel.RenderVoucherPdf</c> attaches the identical bytes. A tax invoice contradicting the
    /// book, under the same live document number, went out to the counterparty with nothing on screen saying so.
    /// Reproduced on a plain Journal, so it is not an item-invoice defect: the staleness is the snapshot, and it
    /// hits every voucher family and BOTH print projections.</para>
    ///
    /// <para><b>Not only the money.</b> <see cref="Title"/>, <see cref="Subtitle"/> (date + party) and the
    /// (Cancelled)/(Optional)/(Post-dated) flags were built in the same constructor pass, so an alteration that
    /// moved the DATE — which <c>Replace</c> permits with a <c>DateChanged</c> warning rather than a refusal —
    /// left the pane and the printed header showing the old date. <see cref="Project"/> rebuilds all of it.</para>
    ///
    /// <para><b>A voucher that is GONE is deliberately left alone.</b> A DELETION does not come through here — the
    /// pane is deliberately not popped on delete (<c>RefreshDeletionSurface</c>: the operator is left looking at
    /// the detail for a voucher that is gone, and Esc/Left returns to the register) — and re-projecting nothing
    /// would empty the pane underneath them, which is the work-loss class that exception exists to avoid. So the
    /// no-longer-present case is a NO-OP, not a clear.</para>
    /// </summary>
    public void Refresh()
    {
        if (_company.FindVoucher(VoucherId) is not { } current) return;
        _voucher = current;
        Rows.Clear();
        Project();
        // The four computed header properties read _voucher through a getter, so they are not raised by the
        // [ObservableProperty] setters above and must be announced by hand — the badge, the declaration and the
        // print routing all turn on masters the alteration may have moved.
        OnPropertyChanged(nameof(IsTaxInvoice));
        OnPropertyChanged(nameof(IsBillOfSupply));
        OnPropertyChanged(nameof(DocumentLabel));
        OnPropertyChanged(nameof(BillOfSupplyDeclaration));
    }

    /// <summary>Builds the header and the Dr/Cr rows from <see cref="_voucher"/>. The constructor's body, split out
    /// so <see cref="Refresh"/> cannot drift from it — one projection, two callers.</summary>
    private void Project()
    {
        var company = _company;
        var voucher = _voucher;

        var type = company.FindVoucherType(voucher.TypeId);
        var typeName = type?.Name ?? "(unknown)";
        var flags = string.Empty;
        if (voucher.Cancelled) flags += "  (Cancelled)";
        if (voucher.Optional) flags += "  (Optional)";
        if (voucher.PostDated) flags += "  (Post-dated)";

        Title = $"{typeName} No. {company.FormatVoucherNumber(voucher)}";
        var party = voucher.PartyId is Guid pid ? company.FindLedger(pid)?.Name : null;
        var partyClause = string.IsNullOrEmpty(party) ? string.Empty : $"  —  {party}";
        Subtitle = $"{FormatDate(voucher.Date)}{partyClause}{flags}";

        foreach (var line in voucher.Lines)
        {
            var name = company.FindLedger(line.LedgerId)?.Name ?? "(unknown)";
            Rows.Add(new ReportRow
            {
                Particulars = name,
                Debit = line.Side == DrCr.Debit ? IndianFormat.Amount(line.Amount) : string.Empty,
                Credit = line.Side == DrCr.Credit ? IndianFormat.Amount(line.Amount) : string.Empty,
                IsTwoColumn = true,
            });
        }

        Rows.Add(new ReportRow
        {
            Particulars = "Total",
            Debit = IndianFormat.AmountAlways(voucher.TotalDebit),
            Credit = IndianFormat.AmountAlways(voucher.TotalCredit),
            IsTwoColumn = true,
            IsTotal = true,
        });

        if (!string.IsNullOrWhiteSpace(voucher.Narration))
            Rows.Add(new ReportRow { Particulars = "Narration: " + voucher.Narration, IsHeader = true });
    }

    private static string FormatDate(DateOnly d) => ApexDate.Format(d);
}
