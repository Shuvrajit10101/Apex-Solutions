using System;
using System.Collections.Generic;
using System.Linq;
using Apex.Ledger;
using Apex.Ledger.Domain;
using Apex.Ledger.Io;
using Apex.Ledger.Reports;
using Apex.Ledger.Services;

namespace Apex.Desktop.Services;

/// <summary>
/// Projects a posted <see cref="Voucher"/> (with its <see cref="Company"/> context) into the framework-agnostic
/// print DTOs the <c>Apex.Ledger.Io</c> renderers consume (RQ-10 / RQ-11): a <see cref="VoucherPrintData"/> for a
/// plain accounting voucher, or an <see cref="InvoicePrintData"/> for a Sales voucher run in item-invoice (or
/// accounting-invoice) mode — which renders as a GST <b>tax invoice</b> or, per <see cref="IsBillOfSupply"/>, as the
/// <b>bill of supply</b> CGST Act §31(3)(c) requires of a composition or wholly-exempt supply (W0-1 / census T0-7).
/// The mapping is pure and Avalonia-free — it only resolves GUID→name masters, formats dates
/// and quantities to display strings, and runs the item lines through <see cref="GstService"/> so the printed
/// CGST/SGST/IGST reconcile to the posted tax ledgers to the paisa. It never touches disk, dialogs, OS-print or
/// the clock (ER-12): the whole IO path stays in <c>Apex.Ledger.Io</c>. No brand text is ever introduced.
///
/// <para><b>KNOWN, DELIBERATELY OUT OF SCOPE HERE (recorded so the next reader does not re-discover them):</b></para>
/// <list type="bullet">
/// <item><b>F7 — printing after GST is switched off throws.</b> <see cref="ProjectInvoice"/> opens with
/// <c>GstService.IsInterState</c>, which raises "GST is not enabled (no home state) — cannot route a supply" when the
/// company has no home State. Reachable on HEAD too (any item invoice posted while GST was on, reprinted after it was
/// switched off); the service-invoice slice merely widens the set of vouchers that reach it. Fixing it means deciding
/// what a de-GSTed company should print for an already-issued tax invoice — a separate change, not a print-path
/// patch.</item>
/// <item><b>F6 — <c>SchemaDowngrade.V49ToV48</c> loses the vouchers PK / NOT-NULLs / index</b> when it rebuilds the
/// table. This is the SAME pre-existing idiom as <c>V48ToV47</c> and <c>V47ToV46</c>, and <c>SchemaDowngrade</c> is
/// referenced nowhere in <c>src/</c> (test-only, to prove forward-migration parity), so nothing shipped reads a
/// downgraded database. Changing the idiom means changing all three together.</item>
/// </list>
/// </summary>
public static class VoucherPrintProjector
{
    /// <summary>
    /// True iff <paramref name="voucher"/> should print as a GST <b>tax invoice</b> rather than a plain voucher:
    /// a Sales voucher carrying item-invoice stock lines, <b>or</b> a Sales <b>accounting (service) invoice</b>
    /// (<see cref="IsServiceAccountingInvoice"/>). Purchase item-invoices and every other voucher print as the plain
    /// Dr/Cr voucher (RQ-10).
    /// </summary>
    public static bool IsTaxInvoice(Company company, Voucher voucher)
    {
        ArgumentNullException.ThrowIfNull(company);
        ArgumentNullException.ThrowIfNull(voucher);
        var type = company.FindVoucherType(voucher.TypeId);
        if (type?.BaseType != VoucherBaseType.Sales) return false;
        return voucher.HasInventoryLines || IsServiceAccountingInvoice(company, voucher);
    }

    /// <summary>
    /// True iff a <b>ledger-only Sales</b> voucher is a SERVICE (Accounting Invoice) sale — the one ledger-only shape
    /// that may print as a tax invoice. Three conjuncts, all structural:
    /// <list type="number">
    /// <item>its type's base type is <see cref="VoucherBaseType.Sales"/> (checked HERE, not only in
    /// <see cref="IsTaxInvoice"/>, so calling <see cref="ProjectInvoice"/> directly on a ledger-only PURCHASE cannot
    /// divert into the service projection);</item>
    /// <item>it carries no stock lines (<see cref="Voucher.HasInventoryLines"/>) — an item invoice takes the item
    /// projection; and</item>
    /// <item>it was <b>posted from the Accounting Invoice entry mode</b>
    /// (<see cref="Voucher.IsAccountingInvoice"/>, schema v49) and carries at least one SAC-bearing service-income leg
    /// (<see cref="Gstr1.ServiceLegs"/>), so the document has a line to print.</item>
    /// </list>
    ///
    /// <para><b>The persisted flag is the whole gate — it replaced an inference that was wrong in both directions.</b>
    /// The gate used to key on "posts a forward GST tax leg carrying <see cref="GstLineTax"/> metadata"
    /// (<see cref="GstReportSupport.HasForwardTaxLines"/>). That silently excluded two shapes that ARE valid Rule-46
    /// tax invoices: a <b>zero-rated</b> (0%, LUT/export) service invoice and a <b>wholly-exempt</b> one, neither of
    /// which posts any tax leg — both printed as plain vouchers. And its safety property (an existing hand-keyed
    /// As-Voucher sale types its Output CGST/SGST by hand, as plain <c>EntryLine</c>s with no <see cref="GstLineTax"/>,
    /// so it failed the gate) rested on "no OTHER path currently stamps <c>GstLineTax</c> on a ledger-only Sales
    /// voucher" — true of today's code, not a structural property of the data. Keying on what the user actually DID at
    /// posting time fixes both: hand-keyed vouchers are excluded because the flag is false, and every real service
    /// invoice is included whatever its tax.</para>
    ///
    /// <para>Existing (pre-v49) data reads flag = false ⇒ every already-posted voucher prints exactly as it did
    /// before (ER-13).</para>
    ///
    /// <para><b>Conjunct 4 (F2) — the FOOTING INVARIANT.</b> The flag is a plain boolean that round-trips through
    /// export/import (correctly — an exported service invoice must not silently downgrade to a plain voucher), so a
    /// crafted canonical file can set it on a voucher this app never posted from the Accounting Invoice screen.
    /// Measured: exporting a hand-keyed GST sale, inserting <c>isAccountingInvoice="true"</c> and re-importing parsed
    /// with 0 errors and applied cleanly — and the voucher then printed a TAX INVOICE understating the posted debt by
    /// the whole tax (Taxable 5,000 / Tax 0 / Grand 5,000 against a posted party leg of 5,900), with an empty rate
    /// breakup. So the flag alone is not enough: the projection must also RECONCILE. <see cref="ServiceInvoiceFoots"/>
    /// requires the projected Grand Total (Σ service legs + Σ posted forward tax + Σ posted forward cess) to equal the
    /// posted party leg; when it does not, the voucher is NOT a service tax invoice and prints as the plain Dr/Cr
    /// voucher a pre-slice build produced. It is a structural guard on the DOCUMENT ITSELF, so it also catches any
    /// future divergence between what a service invoice posts and what this projector reads — not just the crafted
    /// import that motivated it. Every genuine service invoice foots by construction (the accept path builds the party
    /// leg from exactly those three sums), so this is byte-identical on the real path (ER-13).</para>
    ///
    /// <para><b>Conjuncts 5 (F9) and 6 (F11)</b> extend the same idea from the TOTALS to the two statements the totals
    /// cannot police — see <see cref="TaxedLegsCarryTheirTax"/> (a taxable supply may not be billed at NIL GST; the
    /// route is ordinary use, not tampering) and <see cref="RateBreakupReconciles"/> (the printed rate ROW must
    /// reconcile to the invoice's own taxable total). Every genuine service invoice satisfies both by construction.</para>
    /// </summary>
    public static bool IsServiceAccountingInvoice(Company company, Voucher voucher)
    {
        ArgumentNullException.ThrowIfNull(company);
        ArgumentNullException.ThrowIfNull(voucher);
        if (company.FindVoucherType(voucher.TypeId)?.BaseType != VoucherBaseType.Sales) return false;
        if (voucher.HasInventoryLines) return false;
        if (!voucher.IsAccountingInvoice) return false;
        if (!Gstr1.ServiceLegs(company, voucher).Any()) return false;
        if (!TaxedLegsCarryTheirTax(company, voucher)) return false;
        if (!RateBreakupReconciles(company, voucher)) return false;
        return ServiceInvoiceFoots(company, voucher);
    }

    /// <summary>
    /// <b>Conjunct 5 (F9) — a TAXABLE supply may not be billed at NIL GST.</b> A voucher that posted no forward tax leg
    /// at all states "this supply bore no GST". That is TRUE of a zero-rated (0%, LUT/export) or wholly-exempt supply —
    /// both are genuine Rule-46 tax invoices, and admitting them is exactly what F1/FIX-0 exist for. It is FALSE of a
    /// leg whose ledger declares a TAXABLE supply at a NON-ZERO rate: such a document declares a taxable SAC supply,
    /// charges nothing for it, and prints an EMPTY rate breakup. It contradicts itself, so it is not projected as a tax
    /// invoice and prints as the plain Dr/Cr voucher it was posted as.
    ///
    /// <para><b>The reachable route is ordinary use, not tampering</b> (measured): post a service invoice while GST is
    /// OFF — the Accounting-Invoice screen is available on any Sales voucher, <c>IsAccountingGstInvoice</c> is false, so
    /// two legs post and the flag is stamped — then register for GST and classify the income ledger. The SAME
    /// already-issued voucher then printed <c>label="Tax Invoice" sac=998311 taxable=5000 tax=0 rows=0 grand=5000</c>.
    /// <see cref="ServiceInvoiceFoots"/> cannot catch it: a document that charges no tax foots trivially.</para>
    ///
    /// <para>The discriminator is what the ledger DECLARES (<see cref="Gstr1.IsNonTaxableServiceLedger"/> plus a
    /// non-zero declared rate), never "no tax was posted" — that reading would demote the zero-rated and exempt
    /// invoices F1 restored. A taxable ledger declaring no rate of its own is left alone: its rate would have to be
    /// resolved from the company default at print time, and a live resolve is exactly what this projector refuses to do
    /// with money.</para>
    /// </summary>
    private static bool TaxedLegsCarryTheirTax(Company company, Voucher voucher)
    {
        if (PostedForwardRouting(voucher) is not null) return true; // forward tax was posted — nothing to object to
        foreach (var (ledger, _) in Gstr1.ServiceLegs(company, voucher))
            if (!Gstr1.IsNonTaxableServiceLedger(ledger) && ledger.SalesPurchaseGst is { RateBasisPoints: > 0 })
                return false;
        return true;
    }

    /// <summary>
    /// <b>Conjunct 6 (F11) — the printed rate ROW must reconcile too, not just the totals.</b>
    /// <see cref="ReadPostedRateGroups"/> takes the rate label and the taxable value VERBATIM off the
    /// <see cref="GstLineTax"/> metadata, and <see cref="ServiceInvoiceFoots"/> constrains only the TOTALS (it sums the
    /// tax AMOUNTS, never the declared bases). Measured: a flagged voucher whose tax legs are stamped
    /// <c>GstLineTax(head, 250, TaxableValue = 10,00,000)</c> FOOTS (5,000 + 900 = 5,900) yet printed
    /// <c>rows = 5% / taxable = 10,00,000 / cgst = 450</c> under a totals band saying ₹5,000 — a breakup row
    /// contradicting the document it sits on.
    ///
    /// <para>The bound is <b>≤</b>, not <b>=</b>, and that is load-bearing: an exempt leg and a zero-rated leg each
    /// carry value into the invoice taxable total while posting no tax line, so a genuine partly-exempt invoice has
    /// rate rows summing to strictly LESS than its taxable total (measured 10,000 of 15,000). Equality would demote it.
    /// Both figures are read from POSTED data only — the leg amounts and the leg metadata — so no live master can move
    /// this verdict.</para>
    /// </summary>
    private static bool RateBreakupReconciles(Company company, Voucher voucher)
    {
        var invoiceTaxable = 0m;
        foreach (var (_, value) in Gstr1.ServiceLegs(company, voucher)) invoiceTaxable += value;

        var breakupTaxable = 0m;
        foreach (var g in ReadPostedRateGroups(voucher))
        {
            if (g.Taxable < 0m) return false;  // a negative base could otherwise mask an inflated one in the sum
            breakupTaxable += g.Taxable;
        }
        return breakupTaxable <= invoiceTaxable;
    }

    /// <summary>
    /// The F2 footing invariant: does the service projection's Grand Total equal the amount the voucher actually
    /// recorded against the party? A Rule-46 tax invoice states the debt; a document that states a different figure
    /// from the one the books carry is worse than no document. The three sums are exactly the ones
    /// <see cref="ProjectServiceInvoice"/> adds up (it prints no round-off — the accounting-invoice accept path
    /// computes its tax with <c>applyInvoiceRoundOff: false</c> and posts no round-off leg), so this is a genuine
    /// end-to-end reconciliation of the projection against the GL, not a restatement of it.
    /// <para>A voucher with no party (hence no party leg to state a debt against) cannot be a tax invoice at all.</para>
    /// </summary>
    private static bool ServiceInvoiceFoots(Company company, Voucher voucher)
    {
        if (voucher.PartyId is not Guid partyId) return false;

        var partyLeg = 0m;
        var sawPartyLeg = false;
        foreach (var line in voucher.Lines)
            if (line.LedgerId == partyId) { partyLeg += line.Amount.Amount; sawPartyLeg = true; }
        if (!sawPartyLeg) return false;

        var projected = 0m;
        foreach (var (_, value) in Gstr1.ServiceLegs(company, voucher)) projected += value;
        foreach (var g in ReadPostedRateGroups(voucher)) projected += g.Cgst + g.Sgst + g.Igst;
        projected += PostedCess(voucher).Amount;

        return projected == partyLeg;
    }

    // ---------------------------------------------------------------- RQ-10: plain voucher

    /// <summary>
    /// Projects a voucher into a <see cref="VoucherPrintData"/> for <c>VoucherPdf</c>: company/title header,
    /// No/Date/Party line, the Dr/Cr posting lines (ledger names resolved) and the narration. Dates are
    /// formatted here so the renderer stays clock-free.
    /// </summary>
    public static VoucherPrintData ProjectVoucher(Company company, Voucher voucher)
    {
        ArgumentNullException.ThrowIfNull(company);
        ArgumentNullException.ThrowIfNull(voucher);

        var type = company.FindVoucherType(voucher.TypeId);
        var party = voucher.PartyId is Guid pid ? company.FindLedger(pid)?.Name : null;

        var lines = new List<VoucherPrintLine>(voucher.Lines.Count);
        foreach (var l in voucher.Lines)
            lines.Add(new VoucherPrintLine
            {
                LedgerName = ReportPrintProjector.Ascii(company.FindLedger(l.LedgerId)?.Name ?? "(unknown)"),
                IsDebit = l.Side == DrCr.Debit,
                Amount = l.Amount,
            });

        return new VoucherPrintData
        {
            CompanyName = ReportPrintProjector.Ascii(CompanyDisplayName(company)),
            VoucherTypeName = ReportPrintProjector.Ascii(type?.Name ?? string.Empty),
            VoucherNumber = company.FormatVoucherNumber(voucher),
            DateText = voucher.Date.ToString("dd-MM-yyyy", System.Globalization.CultureInfo.InvariantCulture),
            PartyName = ReportPrintProjector.Ascii(party ?? string.Empty),
            // Counterparty captured field (numbering §8): the other party's number, labelled per base type. Blank
            // when none was captured ⇒ nothing prints ⇒ byte-identical (ER-13).
            ReferenceNo = ReportPrintProjector.Ascii(voucher.ReferenceNo ?? string.Empty),
            ReferenceCaption = ReferenceCaption(type),
            ReferenceDateText = voucher.ReferenceDate is { } rd
                ? rd.ToString("dd-MM-yyyy", System.Globalization.CultureInfo.InvariantCulture)
                : string.Empty,
            Lines = lines,
            Narration = ReportPrintProjector.Ascii(voucher.Narration ?? string.Empty),
        };
    }

    // ---------------------------------------------------------------- W0-1: which document is this, in law?

    /// <summary>
    /// <b>W0-1 (census T0-7) — the statutory document kind of an outward supply.</b> True iff this voucher must be
    /// issued as a <b>bill of supply</b> rather than a tax invoice.
    ///
    /// <para>CGST Act §31(3)(c): "a registered person supplying <b>exempted</b> goods or services or both <b>or</b>
    /// paying tax under the provisions of <b>section 10</b> shall issue, <i>instead of a tax invoice</i>, a bill of
    /// supply". Two limbs, and the app previously honoured neither in print — every document was titled TAX INVOICE.
    /// </para>
    ///
    /// <list type="number">
    /// <item><b>The §10 limb</b> — a composition dealer, decided by <see cref="GstReportSupport.IsBillOfSupply"/>
    /// (unchanged engine predicate: an outward Sales supply of a GST-enabled Composition company). §10(4) bars him
    /// from collecting "any tax from the recipient on supplies made by him".</item>
    /// <item><b>The exempt limb</b> — a supply <b>every</b> line of which is explicitly Exempt / Nil-rated / Non-GST.
    /// §2(47) defines an exempt supply as one which "attracts nil rate of tax or which may be wholly exempt from tax
    /// under section 11 … and <b>includes non-taxable supply</b>", so all three taxabilities take this limb. A supply
    /// carrying even one taxable line is a tax invoice (Rule 46A's combined "invoice-cum-bill of supply" is permissive
    /// and confined to an unregistered recipient — see the mixed-supply test).</item>
    /// </list>
    ///
    /// <para><b>Two conservative gates, both structural.</b> An <b>unresolved</b> line (no GST master anywhere,
    /// <see cref="GstService.IsUnresolved"/>) is NOT read as exempt — silence is not an exemption, and reading it as
    /// one would strip the tax breakup off a genuinely taxable supply. And a voucher that <b>posted</b> forward
    /// CGST/SGST/IGST or Compensation Cess can never be a bill of supply whatever its registration says: the document
    /// must state the debt the GL actually recorded, and a bill of supply shows no tax, so titling it one would print a
    /// Grand Total short of the posted party leg — the exact class of defect FIX-1 / FIX-F10 exist to prevent. Neither
    /// gate can fire on a real path (a composition company's <c>ComputeInvoiceTax</c> short-circuits to no tax lines,
    /// and an exempt-only supply produces none), so every existing document is byte-identical (ER-13).</para>
    /// </summary>
    public static bool IsBillOfSupply(Company company, Voucher voucher)
    {
        ArgumentNullException.ThrowIfNull(company);
        ArgumentNullException.ThrowIfNull(voucher);

        // A document that carries posted forward tax states that tax, whatever else is true of the supplier.
        if (GstReportSupport.HasForwardTaxLines(voucher) || HasPostedForwardCess(voucher)) return false;

        // Limb 1 — §10 (composition). Applies to every outward Sales voucher, invoice-format or not.
        if (GstReportSupport.IsBillOfSupply(company, voucher)) return true;

        // Limb 2 — wholly exempt / nil-rated / non-GST. Only a document-producing supply has lines to classify.
        if (!IsTaxInvoice(company, voucher)) return false;
        return IsServiceAccountingInvoice(company, voucher)
            ? IsWhollyExemptServiceSupply(company, voucher)
            : IsWhollyExemptItemSupply(company, voucher);
    }

    /// <summary>The exempt limb for an ITEM invoice: at least one stock line, and every one of them resolves to an
    /// explicit non-taxable taxability. An unresolved line disqualifies (see <see cref="IsBillOfSupply"/>).</summary>
    private static bool IsWhollyExemptItemSupply(Company company, Voucher voucher)
    {
        if (voucher.InventoryLines.Count == 0) return false;
        var gst = new GstService(company);
        // Resolve the value ledger EXACTLY as ProjectInvoice does — `partyLedger?.Id`, not the raw `voucher.PartyId`.
        // They differ when the party ledger no longer exists: a dangling PartyId would still exclude the party's own
        // line from the fallback here while ProjectInvoice (passing null) would admit it, so the two could resolve
        // different rates and the printed title could contradict the printed breakup.
        var partyLedger = voucher.PartyId is Guid pid ? company.FindLedger(pid) : null;
        var valueLedger = ResolveValueLedger(company, voucher, partyLedger?.Id);
        foreach (var il in voucher.InventoryLines)
        {
            var res = gst.ResolveRate(company.FindStockItem(il.StockItemId), valueLedger, voucher.Date);
            if (res.IsTaxable || GstService.IsUnresolved(res)) return false;
        }
        return true;
    }

    /// <summary>The exempt limb for a SERVICE (Accounting Invoice) supply: every service-income leg's ledger declares a
    /// non-taxable supply. A ledger with no GST block at all is treated as taxable (silence is not an exemption), and a
    /// <b>zero-rated</b> (0%, LUT/export) ledger declares <c>IsTaxable = true</c>, so it correctly stays a tax
    /// invoice — a zero-rated supply is a taxable supply, not an exempt one.</summary>
    private static bool IsWhollyExemptServiceSupply(Company company, Voucher voucher)
    {
        bool any = false;
        foreach (var (ledger, _) in Gstr1.ServiceLegs(company, voucher))
        {
            any = true;
            if (!Gstr1.IsNonTaxableServiceLedger(ledger)) return false;
        }
        return any;
    }

    /// <summary>The Rule 5(1)(f) wording a <b>composition</b> taxable person must carry "at the top of the bill of
    /// supply issued by him", or blank. Gated on the §10 limb alone: a regular dealer's exempt bill of supply must NOT
    /// bear it — he is not a composition taxable person.</summary>
    private static string TopDeclarationFor(Company company, Voucher voucher) =>
        GstReportSupport.IsBillOfSupply(company, voucher) ? GstReportSupport.BillOfSupplyDeclaration : string.Empty;

    // ---------------------------------------------------------------- RQ-11: tax invoice

    /// <summary>
    /// Projects a Sales item-invoice voucher into an <see cref="InvoicePrintData"/> GST tax invoice for
    /// <c>InvoicePdf</c>: the seller (company) and buyer (party) name/address/GSTIN/State blocks, the item rows
    /// (Sr resolved by row order, Description/HSN from the stock item, Qty/Rate formatted), the per-rate GST
    /// breakup and the money totals — all paisa-exact figures the <see cref="GstService"/> produced, so the
    /// printed tax reconciles to the posted tax ledgers. Intra vs inter is routed from the party's recorded
    /// State vs the company home State.
    /// </summary>
    public static InvoicePrintData ProjectInvoice(Company company, Voucher voucher)
    {
        ArgumentNullException.ThrowIfNull(company);
        ArgumentNullException.ThrowIfNull(voucher);

        var gst = new GstService(company);
        var partyLedger = voucher.PartyId is Guid pid ? company.FindLedger(pid) : null;
        var partyState = partyLedger?.PartyGst?.StateCode;
        bool interState = gst.IsInterState(partyState);

        // A SERVICE (Accounting Invoice) sale has no stock lines, so the item pass below would project an EMPTY
        // invoice. It takes its own projection, built entirely from the POSTED legs. The item path is untouched.
        // `interState` is handed across as the LIVE routing, used only where the voucher posted no forward tax leg at
        // all and there is therefore nothing posted for the document to contradict (F1).
        if (IsServiceAccountingInvoice(company, voucher))
            return ProjectServiceInvoice(company, voucher, partyLedger, interState);

        // The sales value ledger drives rate resolution (item → ledger → company). It is the posted entry line
        // whose ledger carries a Sales/Purchase GST block; fall back to the first non-party, non-tax ledger.
        var valueLedger = ResolveValueLedger(company, voucher, partyLedger?.Id);

        var items = new List<InvoiceItemRow>(voucher.InventoryLines.Count);
        var taxableByRate = new List<(int Bp, decimal Taxable)>();
        // FIX-1: the taxable lines are now carried PER LINE (not pre-aggregated per rate) so each can hand its own
        // resolved Compensation Cess to the engine — cess is per-line by construction (a Specific cess is per UNIT,
        // an RSP-factor cess is per unit of retail price), so a rate-aggregated line cannot express it. The engine
        // still groups by rate internally, on the same subtotals, so every CGST/SGST/IGST figure and the round-off
        // are unchanged for a cess-free invoice (ER-13).
        var taxableLines = new List<GstService.TaxableLine>(voucher.InventoryLines.Count);
        // Σ of EVERY item line's value — rated AND exempt/nil/non-GST/unresolved. This is the invoice's goods
        // (taxable-value) total that the Grand Total must foot to; the per-rate `taxableByRate` only drives the
        // GST tax, which is charged on rated lines alone (exempt/nil lines contribute their value at 0 tax).
        decimal totalGoodsValue = 0m;
        // FIX-4 (F4): decided ONCE, before the loop — did this voucher POST forward Compensation-Cess legs? If it did,
        // they are the cess this document charges, and the live master is never consulted (so it can neither move a
        // printed figure nor throw). Only a voucher that posted NO cess leg falls back to a live resolve.
        bool hasPostedCess = HasPostedForwardCess(voucher);

        foreach (var il in voucher.InventoryLines)
        {
            var item = company.FindStockItem(il.StockItemId);
            // WI-10 Gap 2: label the quantity with the unit the LINE is actually stated in, not the item's base
            // unit — the printed quantity IS the line quantity, and the printed Rate is per that same unit, so
            // "2 Doz @ ₹10.00 = ₹20.00" reads correctly and foots. Falling back to the item's base unit keeps a
            // line that carries no unit byte-identical to before (ER-13). Printing "2 Nos @ ₹10 = ₹20" would be
            // internally consistent arithmetic on a QUANTITY THAT IS NOT WHAT MOVED (24 Nos did) — a document
            // the buyer, the auditor and the e-way bill would all read differently.
            var unit = il.UnitId is { } lineUnitId
                ? company.FindUnit(lineUnitId)?.Symbol
                : item is not null ? company.FindUnit(item.BaseUnitId)?.Symbol : null;
            var qtyText = IndianFormat.Quantity(il.Quantity);
            if (!string.IsNullOrEmpty(unit)) qtyText += " " + unit;

            items.Add(new InvoiceItemRow
            {
                Description = ReportPrintProjector.Ascii(item?.Name ?? "(item)"),
                HsnSac = ReportPrintProjector.Ascii(item?.Gst?.HsnSac ?? item?.HsnSacCode ?? string.Empty),
                QuantityText = ReportPrintProjector.Ascii(qtyText),
                RateText = IndianFormat.Amount(il.Rate),
                TaxableValue = il.Value,
            });
            totalGoodsValue += il.Value.Amount;

            // F4: resolve the rate AS OF THE VOUCHER DATE, exactly as the accept path does — the un-dated overload
            // used to be paired with a DATED cess resolve two lines below, so a company carrying dated rate-history
            // windows printed one rate and posted another (a pre-22-Sep-2025 invoice reprinted at the GST 2.0 rate).
            // With no rate-history rows the dated overload returns the base result unchanged (ER-13).
            var res = gst.ResolveRate(item, valueLedger, voucher.Date);
            if (!res.IsTaxable || GstService.IsUnresolved(res)) continue; // Exempt/Nil/Non-GST/unresolved ⇒ no tax
            AccumulateRate(taxableByRate, res.RateBasisPoints, il.Value.Amount);
            // FIX-1/F4: the ring-fenced Compensation Cess. The POSTED legs win (read after the loop); only a voucher
            // that posted none resolves live — and that resolve is wrapped, because it is deliberately FAIL-FAST for an
            // RSP-factor cess with no declared Retail Sale Price and reprinting an ISSUED document must never depend on
            // a live master still being valid. null ⇒ no cess ⇒ every figure below is byte-identical (ER-13).
            var cess = hasPostedCess ? null : ResolveCessOrNone(gst, item, valueLedger, voucher.Date, il.BilledQuantity);
            taxableLines.Add(new GstService.TaxableLine(il.Value, res.RateBasisPoints, cess));
        }

        // Compute the whole-invoice tax once (all taxable lines, one call) so the head totals match the engine exactly,
        // then compute each rate group's tax separately for the per-rate breakup rows.
        //
        // FIX-F10 (HIGH, money, pre-existing): `applyInvoiceRoundOff` is FALSE here because it is false on the ACCEPT
        // path (`VoucherEntryViewModel.AcceptItemInvoice` uses the 3-arg overload, whose default is false). It used to
        // be true, so PRINT invented a nearest-rupee round-off that NO voucher ever posts — `GstService.InvoiceTax`
        // .RoundOffLine is produced here and consumed NOWHERE in src/. Measured on 7 Widgets @ ₹133.33 @18%: taxable
        // 933.31 + CGST 84.00 + SGST 84.00 = a POSTED party leg of 1101.31, against a printed Round Off −0.31 and a
        // Grand Total of 1101.00. Up to ±0.50 on every invoice whose taxable+tax is not a whole rupee — the printed
        // demand differed from the recorded debt, and paying the printed figure left a permanent paisa residue in the
        // debtor ledger. A whole-rupee invoice (the common case, and the only case the pre-existing tests exercised)
        // rounded to zero, which is why this survived. Nothing about POSTING changes: already-posted vouchers keep
        // every recorded amount; this is the document catching up with the books.
        var invoiceTax = gst.ComputeInvoiceTax(taxableLines, interState, GstTaxDirection.Output);

        // F4: prefer the POSTED cess, as the service path already does — so re-rating an item's cess after posting
        // cannot move the printed Grand Total off the debt the GL recorded. Zero on every cess-free invoice, and equal
        // to the engine's figure on every ordinary reprint (ER-13).
        var totalCess = hasPostedCess ? PostedCess(voucher) : invoiceTax.TotalCess;
        // FIX-F10: the printed round-off is the one the voucher POSTED — never one invented at print time. Zero for
        // every voucher this app posts (no path posts a Round-Off leg), so the Grand Total equals the party leg; a
        // crafted/imported document that DOES carry one still states its own debt.
        var roundOff = PostedRoundOff(company, voucher);

        var taxRows = new List<InvoiceTaxRow>(taxableByRate.Count);
        foreach (var (bp, taxable) in taxableByRate)
        {
            var lt = GstService.ComputeLineTax(new Money(taxable), bp, interState);
            taxRows.Add(new InvoiceTaxRow
            {
                RateLabel = RateLabel(bp),
                TaxableValue = new Money(taxable),
                Cgst = lt.Cgst,
                Sgst = lt.Sgst,
                Igst = lt.Igst,
            });
        }

        var buyerState = ConsistentBuyerStateCode(company, partyLedger, interState);
        // W0-1 (T0-7): which document is this, in law? A bill of supply carries NO tax breakup (CGST Rule 49 prescribes
        // no rate and no tax-amount particular), so the per-rate rows and the per-head totals are dropped. That is not
        // cosmetic here: `taxRows` above is built with the STATIC GstService.ComputeLineTax, which is NOT
        // composition-gated — so a composition dealer's document printed CGST/SGST that was never charged, never posted
        // and not in its own Grand Total (measured: a CGST 4,256.71 + SGST 4,256.70 row under a Grand Total of
        // 47,296.73). The per-head totals are already zero on both limbs (ComputeInvoiceTax short-circuits for
        // composition; an exempt-only supply feeds it no taxable line), so zeroing them cannot lose money — and
        // `IsBillOfSupply` refuses the classification outright when forward tax WAS posted.
        bool billOfSupply = IsBillOfSupply(company, voucher);
        return new InvoicePrintData
        {
            DocumentTitle = billOfSupply ? GstReportSupport.BillOfSupplyTitle : GstReportSupport.TaxInvoiceTitle,
            IsBillOfSupply = billOfSupply,
            TopDeclaration = billOfSupply ? TopDeclarationFor(company, voucher) : string.Empty,
            Seller = SellerBlock(company),
            Buyer = BuyerBlock(company, partyLedger, buyerState),
            InvoiceNumber = company.FormatVoucherNumber(voucher),
            InvoiceDateText = voucher.Date.ToString("dd-MM-yyyy", System.Globalization.CultureInfo.InvariantCulture),
            // Counterparty captured field (numbering §8): on a Sales tax invoice this is the buyer's "Reference No.".
            // Blank when none was captured ⇒ nothing prints ⇒ byte-identical (ER-13).
            ReferenceNo = ReportPrintProjector.Ascii(voucher.ReferenceNo ?? string.Empty),
            ReferenceCaption = ReferenceCaption(company.FindVoucherType(voucher.TypeId)),
            ReferenceDateText = voucher.ReferenceDate is { } rd
                ? rd.ToString("dd-MM-yyyy", System.Globalization.CultureInfo.InvariantCulture)
                : string.Empty,
            PlaceOfSupply = PlaceOfSupply(company, buyerState, interState),
            IsInterState = interState,
            Items = items,
            TaxRows = billOfSupply ? Array.Empty<InvoiceTaxRow>() : taxRows,
            // The taxable/goods total = sum of ALL line values (rated + exempt/nil), so exempt lines are never
            // silently dropped from the Grand Total (GrandTotal = TotalTaxable + TotalTax + TotalCess + RoundOff).
            // On a bill of supply this IS the Grand Total — Rule 49(g)'s "value of supply".
            TotalTaxable = new Money(totalGoodsValue),
            TotalCgst = billOfSupply ? Money.Zero : invoiceTax.TotalCgst,
            TotalSgst = billOfSupply ? Money.Zero : invoiceTax.TotalSgst,
            TotalIgst = billOfSupply ? Money.Zero : invoiceTax.TotalIgst,
            // FIX-1: the ring-fenced Compensation Cess is part of what the customer OWES. Omitting it made the printed
            // Grand Total understate the posted party leg by the whole cess (measured 1,180 printed vs 1,300 posted).
            // Zero on every cess-free invoice ⇒ byte-identical (ER-13). F4: the POSTED legs win over a live resolve.
            // W0-1: a bill of supply bears no cess either — and `IsBillOfSupply` refuses that classification whenever
            // cess WAS posted, so this can only ever zero a figure that is already zero.
            TotalCess = billOfSupply ? Money.Zero : totalCess,
            RoundOff = roundOff,
            Narration = ReportPrintProjector.Ascii(voucher.Narration ?? string.Empty),
        };
    }

    // ---------------------------------------------------------------- service (Accounting Invoice) tax invoice

    /// <summary>
    /// Projects a Sales <b>accounting (service) invoice</b> into an <see cref="InvoicePrintData"/> GST tax invoice —
    /// the service mirror of the item pass above. The seller/buyer/place-of-supply blocks are the SAME master reads;
    /// what differs is where the lines and the tax come from:
    /// <list type="bullet">
    /// <item><b>Lines</b> — one printed row per service-income leg (<see cref="Gstr1.ServiceLegs"/>), described by its
    /// ledger, carrying the SAC that <c>Gstr1</c>'s Table-12 row and the e-invoice <c>HsnCd</c> already use
    /// (<see cref="Gstr1.ServiceSacOf"/>) so the document, the return and the payload cannot disagree, and valued at
    /// the posted leg amount. A service has neither a quantity nor a per-unit rate, so those cells print blank.</item>
    /// <item><b>Tax</b> — read verbatim off the posted <see cref="GstLineTax"/> legs
    /// (<see cref="ReadPostedRateGroups"/>), <b>never recomputed</b>. Re-rating the service ledger's master after
    /// posting therefore cannot move a printed figure: the invoice the customer holds always states the tax the GL
    /// actually carries.</item>
    /// <item><b>Intra vs inter</b> — decided by which HEAD was posted <b>whenever a forward tax leg exists</b>, not
    /// re-derived from the party's (editable) State, for the same reason; the printed buyer State and Place of Supply
    /// are then forced to AGREE with that posted routing (<see cref="ConsistentBuyerStateCode"/>), so editing the
    /// party's State after posting can no longer produce a document that states CGST+SGST under an inter-state Place
    /// of Supply. <b>When the voucher posts NO forward tax leg at all</b> — a zero-rated (LUT/export) or wholly-exempt
    /// supply — the buyer block, the Place of Supply and the routing all come from the party's recorded State instead
    /// (F1, below).</item>
    /// </list>
    ///
    /// <para><b>F1 (blocker, fixed) — "no tax posted" is NOT "intra-state".</b> Routing on
    /// <see cref="PostedForwardRouting"/> as a plain boolean made a no-tax invoice read "intra", and the FIX-3
    /// reconciliation then rewrote the document to match that phantom: measured on a ₹1,00,000 LUT export to a Gujarat
    /// party, the invoice printed the SELLER's own State as the BUYER's State <i>and</i> as the Place of Supply,
    /// BLANKED the buyer's real GSTIN (its 24… prefix "contradicted" the fabricated 27) and declared itself
    /// intra-state, so <c>InvoicePdf</c> rendered CGST+SGST 0.00 rows instead of IGST. Four false statements on an
    /// issued document — and precisely the two shapes FIX-0 exists to admit, so FIX-3 and FIX-0 collided head-on.
    /// The posted tax can only overrule the master where the posted tax SAYS something: with no forward tax leg there
    /// is nothing for the document to contradict, so the party's recorded State is authoritative and is printed in
    /// full (State, real GSTIN, Place of Supply) with the routing taken from it. Wherever forward tax legs DO exist
    /// the posted-tax-wins behaviour is untouched — that is what FIX-3 is for.</para>
    ///
    /// <para><b>Known behaviour (F8):</b> the printed Description and SAC are read LIVE from the service ledger's
    /// master (<see cref="Gstr1.ServiceSacOf"/>), not snapshotted onto the voucher — so renaming a ledger or changing
    /// its SAC changes what a HISTORICAL invoice reprints. This is deliberate and shared with GSTR-1 Table 12 and the
    /// e-invoice <c>HsnCd</c>, which read the same live master: snapshotting here alone would make the reprinted
    /// document disagree with the filed return. The MONEY is never live — tax and values are posted-only (above) — so
    /// no figure can move; only the descriptive text can. Snapshotting all three together is a separate change.</para>
    /// <para>No round-off is printed: the accounting-invoice accept path computes its tax with
    /// <c>applyInvoiceRoundOff: false</c> and posts no round-off leg, so <c>GrandTotal</c> = Σ service legs + Σ posted
    /// tax foots to the posted party leg exactly.</para>
    /// </summary>
    /// <param name="livePartyInterState">The routing derived from the party's RECORDED State vs the company home State
    /// (<c>GstService.IsInterState</c>). Used only when the voucher posted no forward tax leg at all (F1).</param>
    private static InvoicePrintData ProjectServiceInvoice(
        Company company, Voucher voucher, Apex.Ledger.Domain.Ledger? partyLedger, bool livePartyInterState)
    {
        var items = new List<InvoiceItemRow>();
        // Σ of EVERY service leg — taxed AND exempt/nil — so an exempt line is never silently dropped from the
        // Grand Total (the same rule the item pass keeps with `totalGoodsValue`).
        decimal totalServiceValue = 0m;
        foreach (var (ledger, value) in Gstr1.ServiceLegs(company, voucher))
        {
            items.Add(new InvoiceItemRow
            {
                Description = ReportPrintProjector.Ascii(ledger.Name),
                HsnSac = ReportPrintProjector.Ascii(Gstr1.ServiceSacOf(ledger) ?? string.Empty),
                QuantityText = string.Empty, // a service carries no quantity …
                RateText = string.Empty,     // … and no per-unit rate
                TaxableValue = new Money(value),
            });
            totalServiceValue += value;
        }

        var groups = ReadPostedRateGroups(voucher);
        // F1: the POSTED routing when the voucher posted forward tax; otherwise (zero-rated / wholly-exempt) there is
        // no posted statement to honour, so the party's recorded State is authoritative.
        var postedRouting = PostedForwardRouting(voucher);
        bool interState = postedRouting ?? livePartyInterState;

        var taxRows = new List<InvoiceTaxRow>(groups.Count);
        decimal totalCgst = 0m, totalSgst = 0m, totalIgst = 0m;
        foreach (var g in groups)
        {
            taxRows.Add(new InvoiceTaxRow
            {
                RateLabel = RateLabel(g.Rate),
                TaxableValue = new Money(g.Taxable),
                Cgst = new Money(g.Cgst),
                Sgst = new Money(g.Sgst),
                Igst = new Money(g.Igst),
            });
            totalCgst += g.Cgst; totalSgst += g.Sgst; totalIgst += g.Igst;
        }

        // F1: reconcile the printed State to the POSTED tax only where posted tax exists. With none, the party's own
        // recorded State is printed verbatim — the real State, the real GSTIN (ConsistentBuyerGstin keeps a GSTIN
        // whose prefix matches the State it is printed under) and the matching Place of Supply.
        var buyerState = postedRouting is { } posted
            ? ConsistentBuyerStateCode(company, partyLedger, posted)
            : partyLedger?.PartyGst?.StateCode;
        // W0-1 (T0-7): the same §31(3)(c) routing as the item pass. On this path a bill of supply is necessarily
        // tax-free already (`IsBillOfSupply` refuses the classification when forward tax or cess was posted, and every
        // figure here is read from the POSTED legs), so the suppressions below can only ever drop empty rows and zero
        // figures — a taxed service invoice is byte-identical (ER-13).
        bool billOfSupply = IsBillOfSupply(company, voucher);
        return new InvoicePrintData
        {
            DocumentTitle = billOfSupply ? GstReportSupport.BillOfSupplyTitle : GstReportSupport.TaxInvoiceTitle,
            IsBillOfSupply = billOfSupply,
            TopDeclaration = billOfSupply ? TopDeclarationFor(company, voucher) : string.Empty,
            Seller = SellerBlock(company),
            Buyer = BuyerBlock(company, partyLedger, buyerState),
            InvoiceNumber = company.FormatVoucherNumber(voucher),
            InvoiceDateText = voucher.Date.ToString("dd-MM-yyyy", System.Globalization.CultureInfo.InvariantCulture),
            ReferenceNo = ReportPrintProjector.Ascii(voucher.ReferenceNo ?? string.Empty),
            ReferenceCaption = ReferenceCaption(company.FindVoucherType(voucher.TypeId)),
            ReferenceDateText = voucher.ReferenceDate is { } rd
                ? rd.ToString("dd-MM-yyyy", System.Globalization.CultureInfo.InvariantCulture)
                : string.Empty,
            PlaceOfSupply = PlaceOfSupply(company, buyerState, interState),
            IsInterState = interState,
            Items = items,
            TaxRows = billOfSupply ? Array.Empty<InvoiceTaxRow>() : taxRows,
            TotalTaxable = new Money(totalServiceValue),
            TotalCgst = billOfSupply ? Money.Zero : new Money(totalCgst),
            TotalSgst = billOfSupply ? Money.Zero : new Money(totalSgst),
            TotalIgst = billOfSupply ? Money.Zero : new Money(totalIgst),
            // FIX-1: the ring-fenced Compensation Cess, read off the POSTED cess legs like every other figure here —
            // part of what the customer owes, so it must reach the Grand Total (measured before this: printed 11,800
            // vs a posted party leg of 13,000). Zero on every cess-free invoice ⇒ byte-identical (ER-13).
            TotalCess = billOfSupply ? Money.Zero : PostedCess(voucher),
            RoundOff = Money.Zero,
            Narration = ReportPrintProjector.Ascii(voucher.Narration ?? string.Empty),
        };
    }

    /// <summary>
    /// The per-(integrated rate) posted tax of a voucher, read straight off its <see cref="GstLineTax"/> legs — the
    /// print-side twin of <c>Gstr1.ReadInvoiceRateGroups</c>, with the same head exclusions so the printed breakup and
    /// the filed return can never disagree: the ring-fenced Compensation Cess is not a CGST/SGST/IGST rate row (it
    /// records the SAME taxable value on its own doubled key and would inject a phantom row), and reverse-charge legs
    /// are their own bucket, not forward tax. Within one rate group every leg records the same group taxable, so the
    /// max dedups the intra CGST+SGST pair. Ordered by rate for determinism.
    /// </summary>
    private static List<(int Rate, decimal Taxable, decimal Cgst, decimal Sgst, decimal Igst)> ReadPostedRateGroups(Voucher voucher)
    {
        var byRate = new Dictionary<int, (decimal Taxable, decimal Cgst, decimal Sgst, decimal Igst)>();
        foreach (var line in voucher.Lines)
        {
            if (line.Gst is not { } g || g.IsReverseCharge) continue;
            if (g.TaxHead == GstTaxHead.Cess) continue;
            var rate = GstReportSupport.IntegratedRateOf(g);
            var acc = byRate.TryGetValue(rate, out var cur) ? cur : default;
            switch (g.TaxHead)
            {
                case GstTaxHead.Central: acc.Cgst += line.Amount.Amount; break;
                case GstTaxHead.State: acc.Sgst += line.Amount.Amount; break;
                case GstTaxHead.Integrated: acc.Igst += line.Amount.Amount; break;
                default: continue;
            }
            if (g.TaxableValue.Amount > acc.Taxable) acc.Taxable = g.TaxableValue.Amount;
            byRate[rate] = acc;
        }
        return byRate
            .OrderBy(kv => kv.Key)
            .Select(kv => (kv.Key, kv.Value.Taxable, kv.Value.Cgst, kv.Value.Sgst, kv.Value.Igst))
            .ToList();
    }

    /// <summary>
    /// The intra/inter routing the voucher's POSTED forward tax states — <c>true</c> = Integrated (inter-state),
    /// <c>false</c> = Central/State (intra-state), and <b><c>null</c> = the voucher posted no forward tax leg at all</b>
    /// and therefore states NOTHING about the routing (F1).
    ///
    /// <para>This used to be a plain <c>bool</c>, with "no tax leg" collapsing into "intra-state". That is a
    /// falsehood, not a default: a zero-rated (LUT/export) supply and a wholly-exempt one both post no tax leg
    /// (<c>ComputeAccountingInvoiceGst</c> skips a non-taxable line, and <c>GstService.AddHead</c> early-returns on a
    /// zero amount), and calling them intra-state made the document restate the SELLER's State as the buyer's, blank
    /// the buyer's real GSTIN and print CGST+SGST rows on an export. The three-valued answer keeps the posted tax
    /// authoritative wherever it actually spoke, and stays silent where it did not.</para>
    ///
    /// <para>The ring-fenced Compensation Cess is NOT a routing signal — it posts to a single Cess head regardless of
    /// intra/inter — and reverse-charge legs are the recipient's liability, not this document's forward tax.</para>
    /// </summary>
    private static bool? PostedForwardRouting(Voucher voucher)
    {
        var sawIntraHead = false;
        foreach (var line in voucher.Lines)
        {
            if (line.Gst is not { IsReverseCharge: false } g) continue;
            switch (g.TaxHead)
            {
                case GstTaxHead.Integrated: return true;
                case GstTaxHead.Central:
                case GstTaxHead.State: sawIntraHead = true; break;
            }
        }
        return sawIntraHead ? false : null;
    }

    /// <summary>True iff the voucher posted at least one FORWARD Compensation-Cess leg (F4) — the signal that the cess
    /// this document charges is recorded in the GL and must never be re-resolved from the live master.</summary>
    private static bool HasPostedForwardCess(Voucher voucher)
    {
        foreach (var line in voucher.Lines)
            if (line.Gst is { IsReverseCharge: false, TaxHead: GstTaxHead.Cess }) return true;
        return false;
    }

    /// <summary>
    /// <see cref="GstService.ResolveCess"/>, degraded to "no cess" instead of throwing (F3). The engine is
    /// deliberately FAIL-FAST for an RSP-factor cess whose item declares no Retail Sale Price — correct at ACCEPT
    /// time, where the accept path catches it and refuses to post a silent ₹0. At PRINT time it is not: reprinting an
    /// already-issued document must never depend on a live master still being valid, and this call site is only ever
    /// reached for a voucher that posted NO cess leg, i.e. one whose recorded cess is zero anyway.
    /// </summary>
    private static GstService.CessCharge? ResolveCessOrNone(
        GstService gst, StockItem? item, Apex.Ledger.Domain.Ledger? valueLedger, DateOnly date, decimal quantity)
    {
        try
        {
            return gst.ResolveCess(item, valueLedger, date, quantity);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// Σ the voucher's POSTED forward Compensation-Cess legs (FIX-1). Cess is ring-fenced out of the CGST/SGST/IGST
    /// rate rows (a cess leg records the same group taxable on its OWN cess-rate key, so it would inject a phantom
    /// rate row — see <see cref="ReadPostedRateGroups"/>), but it is unquestionably part of what the customer OWES:
    /// the accept path adds it into the party leg, so an invoice that prints the rate rows and omits the cess prints
    /// a Grand Total lower than the debt it recorded. Reverse-charge legs are excluded — that liability is the
    /// recipient's, never charged on the supplier's invoice. Zero for every cess-free voucher.
    /// </summary>
    private static Money PostedCess(Voucher voucher)
    {
        var total = 0m;
        foreach (var line in voucher.Lines)
            if (line.Gst is { IsReverseCharge: false, TaxHead: GstTaxHead.Cess })
                total += line.Amount.Amount;
        return new Money(total);
    }

    /// <summary>
    /// The voucher's POSTED invoice round-off (FIX-F10), signed the way <c>InvoicePrintData.GrandTotal</c> adds it:
    /// positive when the round-off RAISED the party total to the rupee, negative when it shaved it. On a sale the party
    /// is a debit, so a Round-Off CREDIT raised the total; on a purchase the party is a credit, so the sides invert —
    /// the same convention <c>GstService.RoundOffSide</c> posts with.
    ///
    /// <para>Zero for every voucher this app posts: nothing in <c>src/</c> ever posts a Round-Off leg (the accept paths
    /// call <c>ComputeInvoiceTax</c> without <c>applyInvoiceRoundOff</c>, so <c>InvoiceTax.RoundOffLine</c> is always
    /// null and is read by nobody). Reading the posted leg rather than hardcoding zero is what makes the rule "print
    /// the round-off that was POSTED" rather than "print no round-off", so a crafted or imported document that genuinely
    /// settles at the rupee still prints a Grand Total equal to its party leg.</para>
    ///
    /// <para><b>Deliberately NOT used by the service path.</b> <see cref="ServiceInvoiceFoots"/> is a security guard,
    /// and admitting a Round-Off leg into it would hand crafted data a free plug for any shortfall. A service voucher
    /// carrying one simply fails to foot and prints as the plain voucher it is — the conservative direction.</para>
    /// </summary>
    private static Money PostedRoundOff(Company company, Voucher voucher)
    {
        if (company.FindLedgerByName(GstService.RoundOffLedgerName)?.Id is not Guid roundOffId) return Money.Zero;
        var partyIsCredit = company.FindVoucherType(voucher.TypeId)?.BaseType == VoucherBaseType.Purchase;

        var total = 0m;
        foreach (var line in voucher.Lines)
        {
            if (line.LedgerId != roundOffId) continue;
            var raisedThePartyTotal = partyIsCredit ? line.Side == DrCr.Debit : line.Side == DrCr.Credit;
            total += raisedThePartyTotal ? line.Amount.Amount : -line.Amount.Amount;
        }
        return new Money(total);
    }

    /// <summary>
    /// The buyer's State code to PRINT, forced to agree with the tax the voucher actually posted (FIX-3).
    ///
    /// <para>The tax on an issued document is history; the party's State is a LIVE, editable master. Read
    /// independently they can contradict each other, and the printed document then contradicts ITSELF: edit a party
    /// from Maharashtra to Gujarat after posting an intra-state sale and the invoice reprints CGST+SGST (correctly —
    /// the posted tax is never recomputed) under an INTER-state Place of Supply and a Gujarat buyer. No such document
    /// is valid: CGST+SGST asserts the place of supply IS the supplier's State.</para>
    ///
    /// <para>So: when the live State agrees with the posted routing — the ordinary case, and every invoice whose
    /// party was never edited — it is printed unchanged (byte-identical, ER-13). When it contradicts:</para>
    /// <list type="bullet">
    /// <item><b>posted INTRA</b> ⇒ the buyer was in the company's home State (that is what CGST+SGST means), so the
    /// home State is printed. Fully recoverable.</item>
    /// <item><b>posted INTER</b> ⇒ the buyer was in SOME other State, but which one is not recoverable from the GL
    /// (IGST does not record it), so nothing is printed rather than a home-State value that would contradict the
    /// IGST. Blank is a Rule-46 omission; a self-contradicting document is a Rule-46 <i>falsehood</i>.</item>
    /// </list>
    /// <para>The tax itself is never recomputed here — only the descriptive State is reconciled to it.</para>
    /// </summary>
    private static string? ConsistentBuyerStateCode(
        Company company, Apex.Ledger.Domain.Ledger? party, bool postedInterState)
    {
        var live = party?.PartyGst?.StateCode;
        var home = company.Gst?.HomeStateCode;
        var liveIsInterState =
            !string.IsNullOrWhiteSpace(live) && !string.IsNullOrWhiteSpace(home) &&
            !string.Equals(live.Trim(), home.Trim(), StringComparison.OrdinalIgnoreCase);

        if (liveIsInterState == postedInterState) return live;   // consistent — print the master verbatim
        return postedInterState ? null : home;                   // intra ⇒ the home State; inter ⇒ unrecoverable
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>The counterparty-reference label per base type (numbering §8): "Supplier Invoice No." on a Purchase
    /// (the other party's number is the supplier's invoice number), "Reference No." on every other type.</summary>
    private static string ReferenceCaption(VoucherType? type) =>
        type?.BaseType == VoucherBaseType.Purchase ? "Supplier Invoice No." : "Reference No.";

    private static void AccumulateRate(List<(int Bp, decimal Taxable)> acc, int bp, decimal taxable)
    {
        for (int i = 0; i < acc.Count; i++)
            if (acc[i].Bp == bp) { acc[i] = (bp, acc[i].Taxable + taxable); return; }
        acc.Add((bp, taxable));
    }

    /// <summary>The sales value ledger for rate resolution: the posted line ledger carrying a Sales/Purchase GST
    /// block, else the first non-party, non-tax ledger on the voucher.</summary>
    private static Apex.Ledger.Domain.Ledger? ResolveValueLedger(Company company, Voucher voucher, Guid? partyId)
    {
        Apex.Ledger.Domain.Ledger? fallback = null;
        foreach (var l in voucher.Lines)
        {
            var led = company.FindLedger(l.LedgerId);
            if (led is null) continue;
            if (led.SalesPurchaseGst is not null) return led;
            if (led.Id != partyId && led.GstClassification is null && fallback is null) fallback = led;
        }
        return fallback;
    }

    /// <summary>The rate label for the breakup group (e.g. 1800 bp -> "18%"); trims a trailing ".00".</summary>
    private static string RateLabel(int bp)
    {
        decimal pct = bp / 100m;
        var s = pct.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        return s + "%";
    }

    private static string CompanyDisplayName(Company company) =>
        string.IsNullOrWhiteSpace(company.MailingName) ? company.Name : company.MailingName;

    private static InvoicePartyBlock SellerBlock(Company company) => new()
    {
        Name = ReportPrintProjector.Ascii(CompanyDisplayName(company)),
        AddressLines = SplitAddress(company.Address),
        Gstin = ReportPrintProjector.Ascii(company.Gst?.Gstin ?? string.Empty),
        StateText = StateText(company.Gst?.HomeStateCode),
    };

    /// <summary>
    /// The printed invoice's recipient block. The name is the party's <b>Mailing Name</b> when one was captured
    /// (Tally's "Mailing Name (auto, editable)" convention), else the ledger's own Name; the address lines come
    /// from the WI-4 Mailing Details block through the same <see cref="SplitAddress"/> the seller uses.
    /// <para>Before v45 this hardcoded <c>Array.Empty&lt;string&gt;()</c> with a comment explaining that a party
    /// ledger had no address field — so every invoice this app printed carried a blank recipient address. The
    /// field now exists, and <c>InvoicePdf</c> already renders whatever lines it is given.</para>
    /// </summary>
    /// <param name="stateCode">The State code to print — <see cref="ConsistentBuyerStateCode"/>'s verdict, which is
    /// the party's own live code except where it would contradict the posted tax (FIX-3).</param>
    private static InvoicePartyBlock BuyerBlock(
        Company company, Apex.Ledger.Domain.Ledger? party, string? stateCode) => new()
    {
        Name = ReportPrintProjector.Ascii(
            string.IsNullOrWhiteSpace(party?.Mailing?.MailingName)
                ? party?.Name ?? string.Empty
                : party!.Mailing!.MailingName!),
        AddressLines = SplitAddress(BuyerAddressText(party)),
        Gstin = ReportPrintProjector.Ascii(ConsistentBuyerGstin(party, stateCode)),
        StateText = StateText(stateCode),
    };

    /// <summary>
    /// The buyer GSTIN to print (FIX-3). A GSTIN's first two characters ARE its State code, so re-stating a party's
    /// GSTIN on a historical invoice whose printed State had to be reconciled to the posted tax would put the
    /// contradiction straight back on the document — "GSTIN: 24…" under "State: Maharashtra (27)". The check only
    /// runs when the State actually had to be overridden (<paramref name="stateCode"/> differs from the party's own),
    /// so an untouched invoice prints its GSTIN verbatim (ER-13); and when the party's GSTIN still matches the
    /// reconciled State — only the State field was edited — the GSTIN is the historically correct one and is kept.
    /// </summary>
    private static string ConsistentBuyerGstin(Apex.Ledger.Domain.Ledger? party, string? stateCode)
    {
        var gstin = party?.PartyGst?.Gstin;
        if (string.IsNullOrWhiteSpace(gstin)) return string.Empty;

        var live = party?.PartyGst?.StateCode;
        // No override happened ⇒ nothing to reconcile (the overwhelmingly common path).
        if (string.Equals(live?.Trim(), stateCode?.Trim(), StringComparison.OrdinalIgnoreCase)) return gstin;

        // Overridden: keep the GSTIN only if its own State prefix agrees with the State we are printing.
        if (string.IsNullOrWhiteSpace(stateCode) || gstin.Trim().Length < 2) return string.Empty;
        return gstin.Trim().StartsWith(stateCode.Trim(), StringComparison.OrdinalIgnoreCase) ? gstin : string.Empty;
    }

    /// <summary>
    /// The buyer's printable address text: the Mailing Details address, with the PIN code appended as its own
    /// final line when one was captured (the CA's "along with PIN code" — a recipient block without it is not a
    /// complete postal address). Blank when the party has no mailing block, which reproduces the pre-v45 output.
    /// </summary>
    private static string? BuyerAddressText(Apex.Ledger.Domain.Ledger? party)
    {
        var mailing = party?.Mailing;
        if (mailing is null) return null;

        var lines = new List<string>(mailing.AddressLines);
        if (!string.IsNullOrWhiteSpace(mailing.Country)) lines.Add(mailing.Country.Trim());
        if (!string.IsNullOrWhiteSpace(mailing.Pincode)) lines.Add("PIN: " + mailing.Pincode.Trim());
        return lines.Count == 0 ? null : string.Join("\n", lines);
    }

    /// <summary>Place of supply = the buyer's State — the reconciled one (<see cref="ConsistentBuyerStateCode"/>), so
    /// it can never name a State the posted tax contradicts (FIX-3). Falls back to the company home State for a B2C
    /// recipient with no recorded State (DP-8) — but only on an INTRA-state supply, where the home State is what
    /// CGST+SGST already asserts; on an inter-state supply a home-State fallback would contradict the posted IGST, so
    /// the field is left blank instead.</summary>
    private static string PlaceOfSupply(Company company, string? buyerStateCode, bool postedInterState)
    {
        var code = buyerStateCode;
        if (string.IsNullOrWhiteSpace(code) && !postedInterState) code = company.Gst?.HomeStateCode;
        return StateText(code);
    }

    /// <summary>"West Bengal (19)" for a recognised code; blank when unset/unrecognised.</summary>
    private static string StateText(string? code)
    {
        var st = IndianState.FromCode(code);
        return st is null ? string.Empty : ReportPrintProjector.Ascii($"{st.Name} ({st.Code})");
    }

    /// <summary>Splits a free-text address into printable lines (newline- or comma-separated); empty when blank.</summary>
    private static IReadOnlyList<string> SplitAddress(string? address)
    {
        if (string.IsNullOrWhiteSpace(address)) return Array.Empty<string>();
        var parts = address
            .Replace("\r\n", "\n").Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length > 0
            ? parts.Select(ReportPrintProjector.Ascii).ToArray()
            : Array.Empty<string>();
    }
}
