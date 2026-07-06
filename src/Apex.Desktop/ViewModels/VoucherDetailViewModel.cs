using System;
using System.Collections.ObjectModel;
using Apex.Ledger;
using Apex.Ledger.Domain;
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
    private readonly Voucher _voucher;

    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private string _subtitle = string.Empty;

    /// <summary>The voucher's stable id — the identity the header/tests key on.</summary>
    public Guid VoucherId { get; }

    /// <summary>True iff Print (P/Ctrl+P) on this drill should produce the GST <b>tax invoice</b> (a Sales
    /// item-invoice) rather than the plain Dr/Cr voucher (RQ-10/RQ-11 routing).</summary>
    public bool IsTaxInvoice => VoucherPrintProjector.IsTaxInvoice(_company, _voucher);

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

        var type = company.FindVoucherType(voucher.TypeId);
        var typeName = type?.Name ?? "(unknown)";
        var flags = string.Empty;
        if (voucher.Cancelled) flags += "  (Cancelled)";
        if (voucher.Optional) flags += "  (Optional)";
        if (voucher.PostDated) flags += "  (Post-dated)";

        Title = $"{typeName} No. {voucher.Number}";
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

    private static string FormatDate(DateOnly d) => d.ToString("dd-MMM-yyyy");
}
