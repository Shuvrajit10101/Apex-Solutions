using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Apex.Ledger.Domain;
using CommunityToolkit.Mvvm.ComponentModel;
using Apex.Desktop.Services;

namespace Apex.Desktop.ViewModels;

/// <summary>One e-Invoice IRP artefact status row (doc no + lifecycle state + IRN/QR/Ack).</summary>
public sealed class EInvoiceStatusRowVm
{
    public string DocNo { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    /// <summary>The IRN <b>elided for the grid</b> (first 12… last 6) — display only, never an export cell.</summary>
    public string Irn { get; init; } = string.Empty;

    /// <summary>
    /// 🔴 The <b>full, unelided</b> IRN, carried for export. <see cref="Irn"/> is shortened to fit the grid, and
    /// a 64-character statutory identifier with its middle replaced by "…" is not merely cosmetic once it leaves
    /// the screen: it cannot be looked up on the portal, and it still LOOKS like a value, so a reader has no way
    /// to tell it was truncated. The snapshot mirrors the grid (RQ-15) in every other cell; this one deliberately
    /// exports the underlying identifier instead.
    /// </summary>
    public string IrnFull { get; init; } = string.Empty;

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
public sealed partial class EInvoiceEWayStatusReportViewModel : ViewModelBase, IMasterListExportSource
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
                IrnFull = string.IsNullOrWhiteSpace(e.Irn) ? "—" : e.Irn!,
                Qr = string.IsNullOrWhiteSpace(e.SignedQr) ? "—" : "Signed",
                AckNo = e.AckNo ?? "—",
                AckDate = e.AckDate is { } ad ? ApexDate.Format(ad) : "—",
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

    /// <summary>
    /// <b>Census 6.19 — the snapshot that gives this screen an exit.</b> Named in 6.19's "e-Invoice/e-Way Status"
    /// clause among the six screens deriving from <see cref="ViewModelBase"/> alone, which is exactly what
    /// <c>TopMasterExportSource()</c> looks for when gating E / Alt+E and P / Ctrl+P. This method is the
    /// adoption; no new machinery was needed.
    ///
    /// <para>🔴 <b>The IRN column exports <see cref="EInvoiceStatusRowVm.IrnFull"/>, NOT the elided
    /// <see cref="EInvoiceStatusRowVm.Irn"/> the grid shows.</b> This is the one deliberate departure from
    /// "the export mirrors the grid" (RQ-15), and it is deliberate because the grid elides a 64-character
    /// identifier to fit a column. On screen that is fine — the full value is a click away. In a CSV handed to
    /// someone else it is a dead identifier that still looks live, and no reader could tell. The status column
    /// is what makes the row meaningful anyway: a Pending row has no IRN at all.</para>
    ///
    /// <para><b>e-Invoices and e-Way Bills share one column set under a section label.</b> They are two
    /// different portal artefacts against the same documents, and the reason to open this screen is to see which
    /// documents are missing one of them — a question neither half answers alone.</para>
    /// </summary>
    public MasterListSnapshot ToMasterListSnapshot()
    {
        var rows = new List<IReadOnlyList<string>>(EInvoices.Count + EWayBills.Count + 4);

        static IReadOnlyList<string> Section(string label) => new[]
        {
            label, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty,
        };

        rows.Add(Section("e-Invoice (IRP)"));
        foreach (var r in EInvoices)
            rows.Add(new[] { "e-Invoice", r.DocNo, r.Status, r.IrnFull, r.Qr, r.AckNo, r.AckDate });
        if (!string.IsNullOrWhiteSpace(EInvoiceStatusText))
            rows.Add(Section(EInvoiceStatusText));

        rows.Add(Section("e-Way Bill"));
        foreach (var r in EWayBills)
            rows.Add(new[] { "e-Way Bill", r.DocNo, r.Status, r.EwbNumber, r.ValidUpto, r.Vehicle, r.Distance });
        if (!string.IsNullOrWhiteSpace(EWayStatusText))
            rows.Add(Section(EWayStatusText));

        return new MasterListSnapshot(
            Title,
            new[]
            {
                MasterListColumn.Text("Artefact"),
                MasterListColumn.Text("Document No."),
                MasterListColumn.Text("Status"),
                MasterListColumn.Text("IRN / EWB No."),
                MasterListColumn.Text("Signed QR / Valid Upto"),
                MasterListColumn.Text("Ack No. / Vehicle"),
                MasterListColumn.Text("Ack Date / Distance"),
            },
            rows);
    }
}
