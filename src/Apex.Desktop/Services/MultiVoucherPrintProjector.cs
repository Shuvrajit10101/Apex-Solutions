using System;
using System.Collections.Generic;
using Apex.Ledger.Domain;
using Apex.Ledger.Io;
using Apex.Ledger.Reports;

namespace Apex.Desktop.Services;

/// <summary>
/// Projects a <b>RANGE of vouchers</b> into a document SET for one collated print job — the multi-voucher half
/// of census row 12.6 ("Multi-account printing / multi-voucher (range) printing").
///
/// <para><b>Vendor grounding, quoted by content.</b> The reference product reaches this from the print menu and
/// configures it by voucher type: <i>"Press <b>Alt+P</b> (Print) &gt; <b>Others</b> &gt; select
/// <b>Multi-Voucher</b> &gt; press Enter"</i> and <i>"Type or select <b>Multi-Voucher Printing</b> &gt; and press
/// Enter"</i>, after which <i>"Press <b>C</b> (Configure) &gt; type or select <b>Voucher Type</b> and press
/// Enter."</i>
/// (<c>help.tallysolutions.com/faq-e-way-bill-tally/</c>;
/// <c>help.tallysolutions.com/gcc-sales-and-print-fta-tally/</c>). The party and period axes are attested on the
/// vendor's own release notes: <i>"You can also print, export, or e-mail multi-voucher reports for a specific
/// party and period."</i>
/// (<c>help.tallysolutions.com/tally-prime/release-notes-tally-prime/release-notes-tallyprime-2-1/</c>).</para>
///
/// <para><b>Those three axes are the whole selection surface, and no fourth was invented.</b> Voucher type,
/// party and period are each quoted above. Nothing else is offered, because nothing else is attested — a
/// sorting knob or a "skip cancelled" toggle would be ours, not the reference product's.</para>
///
/// <para>🔴 <b>Each voucher prints EXACTLY AS IT PRINTS ALONE.</b> A voucher is carried in the print model its
/// single print uses — the routing is the one <c>VoucherDetailViewModel.BuildPrintPreview</c> applies: an item
/// invoice (<see cref="PrintedDocumentClass.RendersItemDetail"/>) through
/// <see cref="VoucherPrintProjector.ProjectInvoice"/>, anything else through
/// <see cref="VoucherPrintProjector.ProjectVoucher"/> — and <c>VoucherSetPdf</c> draws each through the SAME
/// renderer body (<c>InvoicePdf</c> / <c>VoucherPdf</c>) onto one job. The first cut of this class built its own
/// three-column Dr/Cr <see cref="PrintReport"/> per voucher and claimed it "mirrors the single-voucher preview
/// cell for cell"; an item Sales invoice then printed alone with its item, HSN and GSTINs and in the batch with
/// none of them (A12 review w6, F2). The one divergence from the single print: a payment drawn by cheque prints
/// its VOUCHER here, not the cheque leaf its single print routes to — a batch of payment vouchers onto cheque
/// stationery is not a thing anyone asks for, and cheque printing has its own register.</para>
///
/// <para><b>No clock.</b> The period is supplied by the caller (ER-12), so the projection is deterministic in
/// tests and cannot disagree with the books about what "this year" means.</para>
/// </summary>
public static class MultiVoucherPrintProjector
{
    /// <summary>The heading the preview column carries for a multi-voucher job.</summary>
    public const string JobTitle = "Multi-Voucher Printing";

    /// <summary>
    /// The vouchers a job would cover, in print order (date, then voucher number, then id) — the RANGE this row
    /// is named for. A null <paramref name="typeId"/> means every voucher type; a null
    /// <paramref name="partyId"/> means every party, including vouchers with no party at all. A party matches by
    /// <see cref="InvolvesParty"/>.
    ///
    /// <para>🔴 <b>The bounds are INCLUSIVE at both ends</b>, because an operator who asks for 1-Apr to 30-Apr
    /// means April. An exclusive upper bound would silently drop the last day of every month-end run, and the
    /// loss would be invisible on the paper — there is no row saying "30-Apr omitted".</para>
    ///
    /// <para>🔴 <b>The final tie-break on <see cref="Voucher.Id"/> is load-bearing, not tidiness.</b> Two
    /// vouchers of different types can share a date and a number (each type numbers independently), so date and
    /// number alone do not totally order the set. Without a deterministic third key the same range would print
    /// in a different sheet order on different runs, and a reprint would not match the first copy.</para>
    /// </summary>
    public static IReadOnlyList<Voucher> Select(
        Company company,
        Guid? typeId,
        Guid? partyId,
        DateOnly from,
        DateOnly to)
    {
        ArgumentNullException.ThrowIfNull(company);

        var chosen = new List<Voucher>();
        foreach (var v in company.Vouchers)
        {
            if (v.Date < from || v.Date > to) continue;
            if (typeId is Guid t && v.TypeId != t) continue;
            if (partyId is Guid p && !InvolvesParty(v, p)) continue;
            chosen.Add(v);
        }

        chosen.Sort((a, b) =>
        {
            int c = a.Date.CompareTo(b.Date);
            if (c != 0) return c;
            c = a.Number.CompareTo(b.Number);
            if (c != 0) return c;
            return a.Id.CompareTo(b.Id);
        });

        return chosen;
    }

    /// <summary>
    /// 🔴 <b>Whether <paramref name="voucher"/> is one of <paramref name="partyId"/>'s vouchers: its recorded party,
    /// OR any posting line on that party's ledger.</b> <see cref="Voucher.PartyId"/> alone is NOT enough, because
    /// the plain accounting grid never sets it — a Receipt, Payment or Journal keyed there records its party only
    /// as a posting line (<c>VoucherEntryViewModel</c>: <i>"never keyed on the plain grid"</i>). Matching on
    /// <see cref="Voucher.PartyId"/> alone printed "No vouchers in this range" for a party's own receipts
    /// (A12 review w6, F3). <see cref="Voucher.PartyId"/> is still honoured for the vouchers that do carry it.
    /// </summary>
    public static bool InvolvesParty(Voucher voucher, Guid partyId)
    {
        ArgumentNullException.ThrowIfNull(voucher);
        if (voucher.PartyId == partyId) return true;
        foreach (var line in voucher.Lines)
            if (line.LedgerId == partyId) return true;
        return false;
    }

    /// <summary>
    /// Projects the chosen vouchers into the job a <c>PrintKind.VoucherSet</c> preview prints — one document per
    /// voucher, in the voucher's own single-print model, each starting a fresh sheet.
    /// </summary>
    public static IReadOnlyList<VoucherSetDocument> Project(Company company, IReadOnlyList<Voucher> vouchers)
    {
        ArgumentNullException.ThrowIfNull(company);
        ArgumentNullException.ThrowIfNull(vouchers);

        var documents = new List<VoucherSetDocument>(vouchers.Count);
        foreach (var v in vouchers)
            documents.Add(GstReportSupport.ClassifyPrintedDocument(company, v).RendersItemDetail
                ? VoucherSetDocument.Of(VoucherPrintProjector.ProjectInvoice(company, v))
                : VoucherSetDocument.Of(VoucherPrintProjector.ProjectVoucher(company, v)));
        return documents;
    }
}
