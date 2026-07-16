using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Apex.Ledger.Domain;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Apex.Desktop.ViewModels;

/// <summary>One e-Invoice IRP artefact status row (doc no + lifecycle state + IRN/QR/Ack).</summary>
public sealed class EInvoiceStatusRowVm
{
    public string DocNo { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string Irn { get; init; } = string.Empty;
    public string Qr { get; init; } = string.Empty;
    public string AckNo { get; init; } = string.Empty;
    public string AckDate { get; init; } = string.Empty;
}

/// <summary>One e-Way Bill artefact status row (doc no + lifecycle state + EWB no + validity + vehicle).</summary>
public sealed class EWayStatusRowVm
{
    public string DocNo { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string EwbNumber { get; init; } = string.Empty;
    public string ValidUpto { get; init; } = string.Empty;
    public string Vehicle { get; init; } = string.Empty;
    public string Distance { get; init; } = string.Empty;
}

/// <summary>
/// The <b>e-Invoice / e-Way status</b> report page (Reports → Statutory Reports → GST Returns (Advanced) →
/// e-Invoice / e-Way Status; Phase 9 UI-1; RQ-5/RQ-6). A read-only listing of the company's per-voucher e-invoice
/// (<see cref="EInvoiceRecord"/>) and e-Way Bill (<see cref="EWayBillRecord"/>) artefacts: the document number, the
/// lifecycle status, and the portal-issued identifiers (IRN / signed-QR / Ack for e-invoice; EWB number / validity /
/// vehicle for e-Way). Generation actions arrive in UI-2 — this screen only surfaces status. Empty when neither is
/// used (ER-13). MVVM boundary: domain only, no Avalonia types; deterministic (no clock).
/// </summary>
public sealed partial class EInvoiceEWayStatusReportViewModel : ViewModelBase
{
    private readonly Company _company;

    [ObservableProperty] private string _title = "e-Invoice / e-Way Status";
    [ObservableProperty] private string _subtitle = string.Empty;
    [ObservableProperty] private string _eInvoiceStatusText = string.Empty;
    [ObservableProperty] private string _eWayStatusText = string.Empty;
    [ObservableProperty] private bool _hasEInvoices;
    [ObservableProperty] private bool _hasEWayBills;

    /// <summary>The e-Invoice IRP artefacts (one per covered outward document).</summary>
    public ObservableCollection<EInvoiceStatusRowVm> EInvoices { get; } = new();

    /// <summary>The e-Way Bill artefacts (one per covered goods-movement document).</summary>
    public ObservableCollection<EWayStatusRowVm> EWayBills { get; } = new();

    public EInvoiceEWayStatusReportViewModel(Company company)
    {
        _company = company ?? throw new ArgumentNullException(nameof(company));
        Rebuild();
    }

    /// <summary>(Re)builds the e-invoice + e-Way status listings from the company's stored artefacts.</summary>
    public void Rebuild()
    {
        EInvoices.Clear();
        EWayBills.Clear();

        foreach (var e in _company.EInvoiceRecords.OrderBy(r => r.DocumentNumberUpper, StringComparer.Ordinal))
            EInvoices.Add(new EInvoiceStatusRowVm
            {
                DocNo = e.DocumentNumberUpper,
                Status = e.Status.ToString(),
                Irn = Short(e.Irn),
                Qr = string.IsNullOrWhiteSpace(e.SignedQr) ? "—" : "Signed",
                AckNo = e.AckNo ?? "—",
                AckDate = e.AckDate?.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture) ?? "—",
            });

        foreach (var w in _company.EWayBillRecords.OrderBy(r => r.DocumentNumberUpper, StringComparer.Ordinal))
            EWayBills.Add(new EWayStatusRowVm
            {
                DocNo = w.DocumentNumberUpper,
                Status = w.Status.ToString(),
                EwbNumber = w.EwbNumber ?? "—",
                ValidUpto = w.ValidUpto?.ToString("dd-MMM-yyyy HH:mm", CultureInfo.InvariantCulture) ?? "—",
                Vehicle = string.IsNullOrWhiteSpace(w.VehicleNumber) ? "—" : w.VehicleNumber!,
                Distance = w.DistanceKm > 0 ? $"{w.DistanceKm} km" : "—",
            });

        HasEInvoices = EInvoices.Count > 0;
        HasEWayBills = EWayBills.Count > 0;
        Subtitle = $"{_company.Name}  —  status only (generation actions arrive in a later slice)";
        EInvoiceStatusText = HasEInvoices
            ? $"{EInvoices.Count} e-invoice(s): {EInvoices.Count(r => r.Status == nameof(EInvoiceStatus.Generated))} generated, " +
              $"{EInvoices.Count(r => r.Status == nameof(EInvoiceStatus.Pending))} pending."
            : "No e-invoices raised.";
        EWayStatusText = HasEWayBills
            ? $"{EWayBills.Count} e-Way Bill(s): {EWayBills.Count(r => r.Status == nameof(EWayStatus.Generated))} generated, " +
              $"{EWayBills.Count(r => r.Status == nameof(EWayStatus.Pending))} pending."
            : "No e-Way Bills raised.";
    }

    /// <summary>Shortens a 64-char IRN for the grid (first 12… last 6), or "—" when not yet issued.</summary>
    private static string Short(string? irn)
    {
        if (string.IsNullOrWhiteSpace(irn)) return "—";
        return irn.Length <= 20 ? irn : $"{irn[..12]}…{irn[^6..]}";
    }
}
