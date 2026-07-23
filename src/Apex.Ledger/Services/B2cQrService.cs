using System.Globalization;
using Apex.Ledger.Domain;
using Apex.Ledger.Reports;

namespace Apex.Ledger.Services;

/// <summary>
/// The self-generated <b>B2C dynamic (UPI) QR</b> payload carried on a qualifying B2C invoice (Phase 9 slice 4b; RQ-28;
/// Notn 14/2020-CT; ER-15). It is a payment QR the <b>supplier</b> builds — it carries the payee UPI VPA, payee name,
/// the invoice amount and a reference so the buyer can pay digitally. It is entirely separate from the IRP-signed
/// e-Invoice QR: it has <b>no IRN</b> (there is no <c>Irn</c> member, by construction) and never enters the IRN/IRP
/// flow (§2.12 excludes B2C from e-invoicing entirely).
/// </summary>
/// <param name="UpiUri">The deterministic UPI deep-link string (<c>upi://pay?pa=…&amp;pn=…&amp;am=…&amp;cu=INR&amp;tn=…</c>).</param>
/// <param name="PayeeVpa">The payee UPI VPA the payment is addressed to.</param>
/// <param name="PayeeName">The payee name shown in the QR.</param>
/// <param name="Amount">The full invoice payable the buyer settles — the invoice's authoritative settlement leg (the party
/// ledger's debit, or the cash/bank receipt for a walk-in sale), inclusive of tax, ring-fenced Cess, invoice round-off,
/// freight/other charges and TCS. Never a reconstruction (ER-9).</param>
/// <param name="Reference">The invoice document reference cross-linked into the payment (the UPI <c>tn</c> note).</param>
public sealed record B2cQrPayload(string UpiUri, string PayeeVpa, string PayeeName, Money Amount, string Reference);

/// <summary>
/// The <b>B2C dynamic-QR projection</b> (Phase 9 slice 4b; RQ-28; Notn 14/2020-CT; ER-15). A pure, framework-/clock-/
/// DB-free service over a <see cref="Company"/> that builds — on demand, with no persistence — the self-generated UPI
/// payment QR for a qualifying B2C outward sale.
/// <para>
/// <b>ER-15 structural:</b> this service is entirely separate from the e-Invoice / IRP world. It lives in
/// <c>Apex.Ledger</c>, which does <b>not</b> reference <c>Apex.Ledger.Io</c>, so it cannot even name
/// <c>IGstPortalConnector</c> / <c>Inv01Request</c> / the offline connector; and it references no
/// <c>EInvoiceService</c> / <c>EInvoiceRecord</c> / IRN type. <see cref="B2cQrPayload"/> carries no <c>Irn</c> field.
/// A B2C supply is excluded from e-invoicing (<c>EInvoiceService.CoverageOf</c> returns Excluded), so the two paths
/// cannot be conflated.
/// </para>
/// <para>
/// <b>AATO gate:</b> the company's annual aggregate turnover is not part of the persisted ledger model, so it is
/// supplied by the caller (the composition root) to <see cref="BuildFor"/>. The QR is gated strictly <b>above</b> the
/// configured <see cref="GstConfig.B2cQrAatoThreshold"/> (default ₹500 cr, DP-28).
/// </para>
/// </summary>
public sealed class B2cQrService
{
    private readonly Company _company;

    public B2cQrService(Company company) => _company = company ?? throw new ArgumentNullException(nameof(company));

    /// <summary>
    /// The self-generated B2C dynamic-QR payload for <paramref name="voucher"/>, or <c>null</c> when the invoice does
    /// not qualify. Yields a payload only when: B2C dynamic QR is enabled (with a payee UPI id + name); the supplier's
    /// <paramref name="annualAggregateTurnover"/> is strictly above the ₹500 cr threshold; the voucher is an outward
    /// <see cref="VoucherBaseType.Sales"/> invoice; and the recipient is B2C (a walk-in with no party, a party with no
    /// GST block, or an unregistered/consumer party — never a registered GSTIN-bearing recipient, and never an overseas
    /// place of supply, which is an export). A disabled company, a below-threshold turnover, a B2B recipient or a
    /// non-sale voucher all yield <c>null</c>. The payload is deterministic and de-branded (ER-11).
    /// </summary>
    public B2cQrPayload? BuildFor(Voucher voucher, Money annualAggregateTurnover)
    {
        ArgumentNullException.ThrowIfNull(voucher);

        var gst = _company.Gst;
        if (gst is not { Enabled: true, B2cDynamicQrEnabled: true }) return null;
        if (string.IsNullOrWhiteSpace(gst.B2cQrUpiId) || string.IsNullOrWhiteSpace(gst.B2cQrPayeeName)) return null;

        // The payee VPA must be a well-formed UPI id (name@handle, unreserved chars only). A malformed VPA (a space,
        // '&', '?', …) would corrupt or inject into the UPI deep link, so it yields no QR — defence in depth with the
        // fail-fast in GstConfig.EnsureValid (finding #3). A directly-mutated config can still carry a bad VPA here.
        if (!GstConfig.IsValidUpiVpa(gst.B2cQrUpiId)) return null;

        // Gated strictly ABOVE the ₹500 cr AATO threshold (Notn 14/2020-CT; "> ₹500 crore"). At/below ⇒ no QR.
        if (annualAggregateTurnover.Amount <= gst.B2cQrAatoThreshold.Amount) return null;

        // Only an outward sale invoice bears a payment QR (a purchase / journal / sale-return note does not).
        var type = _company.FindVoucherType(voucher.TypeId);
        if (type is null || type.BaseType != VoucherBaseType.Sales) return null;

        // The recipient must be B2C. A registered (GSTIN-bearing) recipient is B2B — its document carries the IRP QR
        // instead. An overseas place of supply (GST convention 96 = Other Country, 97 = Other Territory) is an export,
        // not a domestic UPI payment. A walk-in cash sale (no party) or a party with no GST block is B2C.
        var partyGst = voucher.PartyId is Guid pid ? _company.FindLedger(pid)?.PartyGst : null;
        // Honour the stated contract "never a registered GSTIN-bearing recipient": a party that carries a GSTIN is B2B
        // even if its RegistrationType still defaults to Unregistered (IsB2C is OR-based and would leak it through) —
        // finding #2.
        if (!string.IsNullOrWhiteSpace(partyGst?.Gstin)) return null;
        if (partyGst is { IsB2C: false }) return null;
        if (partyGst?.StateCode is "96" or "97") return null;

        var amount = InvoiceSettlementAmount(voucher, type.BaseType);
        var reference = DocumentReference(voucher);
        var uri = BuildUpiUri(gst.B2cQrUpiId!, gst.B2cQrPayeeName!, amount, reference);
        return new B2cQrPayload(uri, gst.B2cQrUpiId!, gst.B2cQrPayeeName!, amount, reference);
    }

    /// <summary>
    /// The full invoice payable the buyer settles — the invoice's <b>authoritative settlement leg</b>, read straight off
    /// the posted voucher (never a reconstruction, ER-9). The old "outward supply value + Σ tax" reconstruction silently
    /// dropped every settlement leg that carries no <c>GstLineTax</c> — invoice round-off, freight / packing / insurance,
    /// discounts and TCS — so <c>am=</c> did not equal what the buyer owes (finding #1). Instead:
    /// <list type="bullet">
    ///   <item>A sale <b>with a party</b>: the party ledger's <b>debit</b> IS the receivable — taxable + tax + ring-fenced
    ///     Cess + round-off + charges − discounts + TCS, all netted by double entry.</item>
    ///   <item>A no-party POS <b>walk-in cash sale</b>: the cash/bank settlement <b>debit</b>(s) carry the amount received.</item>
    ///   <item>Degenerate fallback (neither present): reconstruct the print-path grand total — outward supply value + Σ tax
    ///     + signed round-off — matching <c>InvoicePrintData.GrandTotal</c> (= TotalTaxable + TotalTax + RoundOff).</item>
    /// </list>
    /// </summary>
    private Money InvoiceSettlementAmount(Voucher voucher, VoucherBaseType baseType)
    {
        // (1) A sale WITH a party: the party ledger's DEBIT is the full receivable inclusive of tax/cess/round-off/charges/TCS.
        if (voucher.PartyId is Guid pid)
        {
            var partyDebit = 0m; var found = false;
            foreach (var line in voucher.Lines)
                if (line.LedgerId == pid && line.Side == DrCr.Debit) { partyDebit += line.Amount.Amount; found = true; }
            if (found) return new Money(partyDebit);
        }

        // (2) A no-party walk-in cash sale: the cash/bank settlement debit(s) carry the full amount the buyer pays.
        var cashBank = 0m; var settled = false;
        foreach (var line in voucher.Lines)
        {
            if (line.Side != DrCr.Debit) continue;
            var ledger = _company.FindLedger(line.LedgerId);
            if (ledger is not null && ClassificationRules.IsCashOrBankLedger(ledger, _company)) { cashBank += line.Amount.Amount; settled = true; }
        }
        if (settled) return new Money(cashBank);

        // (3) Degenerate fallback: reconstruct the print-path grand total (taxable + Σ tax + signed round-off).
        var supply = GstReportSupport.OutwardSupplyValue(_company, voucher, baseType).Total.Amount;
        var tax = 0m;
        foreach (var line in voucher.Lines)
            if (line.Gst is not null)
                tax += line.Amount.Amount;
        return new Money(supply + tax + RoundOffContribution(voucher));
    }

    /// <summary>The signed contribution of the invoice Round-Off leg to the grand total: a round-<b>up</b> posts as a
    /// CREDIT to Round Off (income) and <b>adds</b> to the payable; a round-<b>down</b> posts as a DEBIT (expense) and
    /// <b>subtracts</b> — so the contribution is (credit − debit) on the Round Off ledger, matching how the print path
    /// nets <c>RoundOff</c> into the grand total. Zero when the company has no Round Off ledger or the voucher has no
    /// round-off leg.</summary>
    private decimal RoundOffContribution(Voucher voucher)
    {
        if (_company.FindLedgerByName(GstService.RoundOffLedgerName) is not { } roundOff) return 0m;
        var contribution = 0m;
        foreach (var line in voucher.Lines)
            if (line.LedgerId == roundOff.Id)
                contribution += line.Side == DrCr.Credit ? line.Amount.Amount : -line.Amount.Amount;
        return contribution;
    }

    /// <summary>The invoice document reference cross-linked into the payment — the voucher's <b>rendered</b> number
    /// (numbering-design-v2 §2.2/§2.3: the ONE policy, prefix/suffix and all), or its id when the number renders empty
    /// (an unnumbered voucher). Deterministic payload.</summary>
    private string DocumentReference(Voucher voucher)
    {
        var rendered = _company.FormatVoucherNumber(voucher);
        return rendered.Length > 0 ? rendered : voucher.Id.ToString("N");
    }

    /// <summary>Builds the deterministic UPI deep link. The VPA is kept literal (a UPI id carries its own <c>@bank</c>
    /// suffix); the payee name and reference are URI-escaped; the amount is invariant-culture, two-decimal rupees.</summary>
    private static string BuildUpiUri(string vpa, string payee, Money amount, string reference)
    {
        var am = amount.Amount.ToString("0.00", CultureInfo.InvariantCulture);
        return $"upi://pay?pa={vpa}&pn={Uri.EscapeDataString(payee)}&am={am}&cu=INR&tn={Uri.EscapeDataString(reference)}";
    }
}
