using Apex.Ledger.Io;

namespace Apex.Desktop.ViewModels;

/// <summary>
/// Builds the <see cref="PageConfig"/> the TDS/TCS certificate + control-chart PDFs (<see cref="Form16APdf"/>,
/// <see cref="Form27DPdf"/>, <see cref="Form27APdf"/>) render through — the same A4 page the GST tax-invoice / report
/// PDFs use. The footer carries only the de-branded product name (never "Tally") and no clock (page-number only), so
/// the certificate bytes stay deterministic and byte-stable.
/// </summary>
public static class CertificatePages
{
    /// <summary>An A4 certificate page whose running footer names the company (already de-branded by the writers).</summary>
    public static PageConfig Build(string companyName) => new()
    {
        HeaderText = companyName,
        FooterText = "Apex Solutions  -  Page {page} of {pages}",
    };
}
