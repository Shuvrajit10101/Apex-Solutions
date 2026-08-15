using Apex.Ledger.Domain;
using Apex.Ledger.Services;

namespace Apex.Ledger.Reports;

/// <summary>
/// Shared read-only primitives for the GST report projections (phase4-gst-requirements RQ-20..RQ-24; ER-7).
/// Every GST report reads the <b>posted</b> tax straight off the tax <see cref="EntryLine"/>s'
/// <see cref="GstLineTax"/> metadata — the head, the applied rate, the taxable value the tax was computed on,
/// and the line's own <see cref="EntryLine.Amount"/> (the tax) — so the returns never recompute tax; they
/// reconcile to the tax-ledger postings by construction. A voucher's <b>direction</b> (outward vs inward) is
/// derived from its type's base type (DP-11): Sales/Credit-Note ⇒ outward (Output tax), Purchase/Debit-Note ⇒
/// inward (Input tax). Cancelled and post-dated-after-<c>to</c> vouchers are excluded via
/// <see cref="LedgerBalances.CountsAsOf(Voucher, DateOnly, VoucherBaseType?)"/> — the same filter the balances
/// use — so a report over the tax lines foots to the ledger postings.
/// </summary>
public static class GstReportSupport
{
    /// <summary>
    /// The GST direction implied by a voucher base type (DP-11), or <c>null</c> for a base type that never
    /// carries GST (contra/payment/receipt/journal/order/inventory/payroll). Sales &amp; Credit-Note are
    /// <b>outward</b> (an outward supply, Output tax → GSTR-1 / GSTR-3B §3.1); Purchase &amp; Debit-Note are
    /// <b>inward</b> (Input tax / ITC → GSTR-3B §4).
    /// </summary>
    public static GstTaxDirection? DirectionOf(VoucherBaseType baseType) => baseType switch
    {
        VoucherBaseType.Sales or VoucherBaseType.CreditNote => GstTaxDirection.Output,
        VoucherBaseType.Purchase or VoucherBaseType.DebitNote => GstTaxDirection.Input,
        _ => null,
    };

    /// <summary>
    /// Enumerates the posted vouchers that carry GST in the window <c>[from, to]</c> on the requested
    /// <paramref name="direction"/> (outward or inward), already filtered for cancelled / optional / provisional
    /// / post-dated-after-<paramref name="to"/> (via <see cref="LedgerBalances.CountsAsOf(Voucher, DateOnly,
    /// VoucherBaseType?)"/>) and the lower date bound. Each yielded voucher has at least one tax
    /// (<see cref="GstLineTax"/>) line. GST-off companies yield nothing.
    /// </summary>
    public static IEnumerable<(Voucher Voucher, VoucherType Type)> PostedGstVouchers(
        Company company, DateOnly from, DateOnly to, GstTaxDirection direction)
    {
        foreach (var pair in PostedDirectionalVouchers(company, from, to, direction))
            if (pair.Voucher.Lines.Any(l => l.HasGst))
                yield return pair;
    }

    /// <summary>
    /// Enumerates <b>all</b> posted vouchers in the window <c>[from, to]</c> on the requested
    /// <paramref name="direction"/> — including exempt/nil supplies that carry <b>no</b> tax line — already
    /// filtered for cancelled / optional / provisional / post-dated-after-<paramref name="to"/> and the lower
    /// date bound. GSTR-1 uses this so exempt outward supplies still appear in the HSN summary and exempt
    /// bucket; the taxable ones are the subset with a tax line. GST-off companies yield nothing.
    /// </summary>
    public static IEnumerable<(Voucher Voucher, VoucherType Type)> PostedDirectionalVouchers(
        Company company, DateOnly from, DateOnly to, GstTaxDirection direction)
    {
        if (!company.GstEnabled) yield break;

        foreach (var v in company.Vouchers)
        {
            if (v.Date < from) continue;
            var type = company.FindVoucherType(v.TypeId);
            if (type is null) continue;
            if (DirectionOf(type.BaseType) != direction) continue;
            if (!LedgerBalances.CountsAsOf(v, to, type.BaseType)) continue; // cancelled/post-dated/date filter
            yield return (v, type);
        }
    }

    /// <summary>The party ledger's <b>recorded</b> GST State code on a voucher, or <c>null</c> when the voucher has no
    /// party, the party has no GST block, or the block records no State. Deliberately NOT whitespace-normalised — the
    /// stored value is what every consumer must see, and <see cref="RoutingOf(Company, string?)"/> is the one place
    /// that decides what a blank one means.</summary>
    private static string? PartyStateCodeOf(Company company, Voucher voucher) =>
        voucher.PartyId is Guid pid && company.FindLedger(pid)?.PartyGst?.StateCode is { } code ? code : null;

    /// <summary>
    /// The place-of-supply state code for a voucher (DP-7): the party ledger's recorded GST state, falling back
    /// to the company home state for a walk-in with no recorded state. Used to label GSTR-1 rows.
    /// <para>This IS the IGST s.10(1)(ca) ladder — "the location as per the address of the said person recorded in the
    /// invoice, and the location of the supplier where the address … is not recorded" — so it is the DERIVATION.
    /// A document that has already been ISSUED needs <see cref="IssuedPlaceOfSupply"/> instead, which reconciles this
    /// ladder against the tax the voucher actually posted.</para>
    /// </summary>
    public static string? PlaceOfSupply(Company company, Voucher voucher) =>
        PartyStateCodeOf(company, voucher) ?? company.Gst?.HomeStateCode;

    /// <summary>
    /// <b>THE ONE intra/inter routing rule (drift lock D8).</b> <c>true</c> = inter-state (IGST), <c>false</c> =
    /// intra-state (CGST+SGST), and <b><c>null</c> = the book cannot route this supply at all</b> because it does not
    /// declare its own home State.
    ///
    /// <para><b>Why <c>null</c> and not <c>false</c>.</b> The statute closes every gap on the RECIPIENT's side by
    /// falling back to "the location of the supplier" — IGST s.10(1)(ca) for goods to an unregistered person,
    /// s.12(2)(b)(ii) for domestic services, the s.13(2) proviso for cross-border services. It has no answer for a
    /// missing SUPPLIER location, because a registered supplier always has one (the first two digits of its GSTIN
    /// <i>are</i> its State code). "No home State" is therefore a data-integrity scenario, not a statutory one, and
    /// <c>false</c> would not be the statute's default applied to an unknown — it would be the positive assertion
    /// "the place of supply is the home State" made while the home State is precisely what is missing. Sourced in
    /// <c>docs/diverged-rules-de-place-of-supply-grounding.md</c> §4–§5.</para>
    ///
    /// <para><b>The shape is a nullable <c>bool</c> for the same reason
    /// <see cref="PostedForwardRouting"/> is</b> — see its note: that method used to be a plain <c>bool</c> with "no
    /// tax leg" collapsing into "intra-state", and the fix was to admit a third answer rather than invent a default.
    /// This is the identical problem one rung up, and a nullable composes with it at the print path with no
    /// conversion layer.</para>
    ///
    /// <para><b>Unchanged from <c>GstService.IsInterState</c>, the rule this is extracted from:</b> a
    /// null/blank/whitespace party State is <c>false</c> (intra) — the s.10(1)(ca) fallback with a KNOWN supplier
    /// location, DP-8 — and the comparison is <see cref="StringComparison.Ordinal"/> on the untrimmed codes.</para>
    ///
    /// <para><b>🔴 CHANGED from the OTHER copy this replaces — <c>EWayBillService</c>'s private
    /// <c>IsInterState</c> — on a SECOND axis, and it changes a statutory e-Way answer. Stated here because the
    /// first draft of this note said "unchanged" flatly and that was true of only one of the two.</b> That copy read
    /// <see cref="PlaceOfSupply"/>, whose <c>StateCode is { } code</c> pattern matches a <b>non-null EMPTY or
    /// WHITESPACE</b> string, and then compared it unequal to the home code — so a party State of <c>""</c> or
    /// <c>"   "</c> answered <b>inter-state</b> there while answering <b>intra-state</b> here. Reachable: the
    /// canonical-XML import writes <c>PartyGstDetails.StateCode</c> straight from an attribute value with no
    /// empty-to-null step. <b>The new answer is taken deliberately, not inherited:</b> s.10(1)(ca) fixes the place of
    /// supply at the supplier's own location when the recipient's address is not recorded, so an unrecorded recipient
    /// State is a <i>determined</i> intra-state supply, not an unknown — that is what the paragraph above is about,
    /// and the e-Way copy was the one departing from it. The consequence is measured and pinned by
    /// <c>EWayBlankPartyStateRoutingTests</c>: on a company that exempts intra-state e-Way, a ₹59,000 movement to a
    /// blank-State party is <c>NotRequired</c> where the deleted copy answered <c>Required</c>.</para>
    /// </summary>
    public static bool? RoutingOf(Company company, string? partyStateCode)
    {
        ArgumentNullException.ThrowIfNull(company);
        var home = company.Gst?.HomeStateCode;
        if (home is null) return null;                                     // cannot route — no supplier location
        if (string.IsNullOrWhiteSpace(partyStateCode)) return false;       // DP-8 / s.10(1)(ca): the supplier's State
        return !string.Equals(home, partyStateCode, StringComparison.Ordinal);
    }

    /// <summary>The routing implied by a VOUCHER's party master. Same rule, same three answers.</summary>
    public static bool? RoutingOf(Company company, Voucher voucher)
    {
        ArgumentNullException.ThrowIfNull(company);
        ArgumentNullException.ThrowIfNull(voucher);
        return RoutingOf(company, PartyStateCodeOf(company, voucher));
    }

    /// <summary>
    /// The buyer's State code an ISSUED document may truthfully state — the party's live master reconciled to the tax
    /// the voucher actually POSTED. The tax on an issued document is history; the party's State is a live, editable
    /// master, and read independently they can contradict each other.
    /// <list type="bullet">
    /// <item><b>No forward tax leg posted</b> (<see cref="PostedForwardRouting"/> is <c>null</c>) ⇒ the master
    /// verbatim: there is nothing posted for the document to contradict (F1).</item>
    /// <item><b>The master agrees with the posted routing</b> — the ordinary case, and every document whose party was
    /// never edited ⇒ the master verbatim.</item>
    /// <item><b>The book cannot route at all</b> (<see cref="RoutingOf(Company, string?)"/> is <c>null</c>) ⇒ the
    /// master verbatim: with no home State no contradiction can be DETECTED, and none may be manufactured either. The
    /// buyer's recorded State is a fact about the buyer, not about the supplier's location.</item>
    /// <item><b>Posted INTRA, master says inter</b> ⇒ the home State: CGST+SGST asserts the place of supply IS the
    /// supplier's State, so it is fully recoverable.</item>
    /// <item><b>Posted INTER, master says intra</b> ⇒ <c>null</c>: the buyer was in SOME other State, but IGST does
    /// not record WHICH, so nothing is stated rather than a home-State value the posted IGST would deny.</item>
    /// </list>
    /// </summary>
    public static string? IssuedBuyerStateCode(Company company, Voucher voucher)
    {
        ArgumentNullException.ThrowIfNull(company);
        ArgumentNullException.ThrowIfNull(voucher);
        var live = PartyStateCodeOf(company, voucher);
        if (PostedForwardRouting(voucher) is not { } posted) return live;   // nothing posted to contradict (F1)
        if (RoutingOf(company, live) is not { } derived) return live;       // unrouteable book — nothing to check
        if (derived == posted) return live;                                 // consistent — the master verbatim
        return posted ? null : company.Gst?.HomeStateCode;
    }

    /// <summary>
    /// <b>The place of supply an ISSUED document states</b> — <see cref="IssuedBuyerStateCode"/> with the
    /// s.10(1)(ca) supplier-location fallback layered on where the posted tax does not deny it, then reduced to a code
    /// the State master can actually NAME. The printed invoice and the filed GSTR-1 row BOTH read this, so one supply
    /// can no longer carry two places of supply.
    ///
    /// <para><b>🔴 WHY THE LAST STEP EXISTS — sharing the value was not enough on its own.</b> The two consumers
    /// render the same string differently: the print path passes it through
    /// <see cref="Domain.IndianState.FromCode"/>, an EXACT dictionary lookup that does not trim, while GSTR-1 files
    /// the raw string. A party State recorded as <c>"19 "</c> (a trailing space) against a home of <c>"19"</c>
    /// therefore posted IGST (the routing comparison is <see cref="StringComparison.Ordinal"/> and untrimmed, §3.1's
    /// whitespace defect), printed <b>nothing</b>, and filed <b>"19 "</b> — the supplier's own State modulo a space,
    /// on an IGST-bearing document, which NIC validation 24 makes self-refuting, in a code the master does not
    /// contain at all. Handing both consumers one value only MOVED the divergence from the routing comparer into the
    /// rendering comparer.
    /// <br/><b>So the shared rule stops at a code the book can name.</b> Anything else — padded, truncated,
    /// mis-keyed — is not a place of supply, and stating nothing is the same answer the print path already gives it.
    /// <b>What this deliberately does NOT do is trim</b>: trimming here would silently flip
    /// <c>GstService.IsInterState("19 ")</c> from inter to intra and re-route the TAX, which is a posting decision and
    /// belongs to input validation on the master, not to a print/return reconciliation. The underlying defect — that a
    /// padded code is accepted onto a party master at all — is untouched and remains open.</para>
    ///
    /// <para><b>This CORRECTS a filed figure.</b> GSTR-1 used to label such a voucher with the raw
    /// <see cref="PlaceOfSupply"/> derivation, so clearing (or "correcting") a party's State after an IGST invoice
    /// was issued made the return name the <b>supplier's own State</b> as the place of supply on a document bearing
    /// IGST — which NIC e-invoice validation 24 ("the state code of the Supplier GSTIN and POS will decide whether
    /// the supply type is Interstate or Intrastate") makes self-refuting — while the reprint of the SAME voucher
    /// printed nothing at all.</para>
    ///
    /// <para><b>The blank stays blank, and that is the correct answer here.</b> Once the party State is cleared,
    /// which State the buyer was in exists NOWHERE in the book: IGST asserts "not the supplier's State" and never
    /// which one. Recovering it needs the party State SNAPSHOTTED onto the voucher at posting — a schema change and a
    /// slice of its own. What this method fixes is the two answers, not the missing fact.</para>
    /// </summary>
    public static string? IssuedPlaceOfSupply(Company company, Voucher voucher)
    {
        // A code the State master cannot name is not a recorded address — it takes the same rung of the s.10(1)(ca)
        // ladder as no address at all, rather than being filed verbatim and printed blank.
        var buyer = IssuedBuyerStateCode(company, voucher);
        if (IsStatableStateCode(buyer)) return buyer;

        // No usable buyer State ⇒ the statutory fallback is the supplier's own location, available unless the posted
        // IGST denies it (an inter-state supply's place of supply is by definition NOT the supplier's State) or the
        // book's own home code is equally unnameable.
        var home = company.Gst?.HomeStateCode;
        return PostedForwardRouting(voucher) is true || !IsStatableStateCode(home) ? null : home;
    }

    /// <summary>
    /// Can a document — printed or filed — NAME this State code? True for a domestic State/UT in
    /// <see cref="Domain.IndianState.All"/> and for the two overseas codes the NIC State master defines
    /// (<see cref="IsOverseasStateCode"/>: 96 and 99, both "OTHER COUNTRIES", neither in <c>IndianState.All</c>).
    /// Everything else — <c>null</c>, blank, whitespace-padded, mis-keyed — is not a place of supply.
    /// <para>Deliberately NOT public and deliberately NOT a validator: it does not say a code is <i>correct</i>, only
    /// that the two consumers of <see cref="IssuedPlaceOfSupply"/> would agree on what it means. The overseas limb is
    /// included so that reducing an unnameable code to <c>null</c> cannot silently drop an export's 96/99 out of a
    /// filed return.</para>
    /// </summary>
    private static bool IsStatableStateCode(string? code) =>
        Domain.IndianState.FromCode(code) is not null || IsOverseasStateCode(code);

    /// <summary>
    /// <b>The ONE home for resolving a stock item's HSN/SAC</b> (drift lock D7): the item's GST block wins, then
    /// the Phase-3 <see cref="StockItem.HsnSacCode"/> field, and <c>null</c> means the item declares <b>no</b>
    /// HSN/SAC at all.
    ///
    /// <para><b>The divergence this replaces.</b> The two-level fallback
    /// <c>item?.Gst?.HsnSac ?? item?.HsnSacCode</c> was written out by hand at four call sites — the GSTR-1 HSN
    /// summary, the INV-01 e-invoice payload, the e-Way Bill payload and the printed invoice — so the resolution
    /// ORDER (GST block over legacy field) was four independent copies of one rule, free to drift apart.</para>
    ///
    /// <para><b>Why this returns <c>null</c> instead of a sentinel, and why the sentinels stay different.</b>
    /// The four consumers legitimately render "absent" differently and <b>must</b> keep doing so, so the shared
    /// rule stops at resolution and hands the caller the absence to spell:</para>
    /// <list type="bullet">
    /// <item>The GSTR-1 HSN summary buckets by HSN for a <b>human-read report</b> and labels the unclassified
    /// bucket <c>(none)</c> — a blank row key would read as a rendering fault.</item>
    /// <item>The NIC INV-01 and EWB-01 payloads file <c>HsnCd: ""</c> because the schema types the field as a
    /// string and the department's own convention for "not declared" is the empty string; filing the literal
    /// text <c>(none)</c> into a statutory code field would be a malformed submission.</item>
    /// <item>The printed invoice leaves the HSN column blank, because a Rule-46 document omits a field it has no
    /// value for rather than printing a placeholder.</item>
    /// </list>
    /// <para>These are four correct answers to four different questions, so unifying the <i>sentinel</i> would
    /// break three of them. What was genuinely duplicated — and is now unified — is the <i>resolution order</i>.</para>
    /// </summary>
    public static string? HsnSacOf(StockItem? item) => item?.Gst?.HsnSac ?? item?.HsnSacCode;

    /// <summary>
    /// True iff a GST state code denotes an <b>overseas</b> place of supply — i.e. the supply leaves India, so it is an
    /// export rather than a domestic supply. <b>The ONE copy of this rule</b> (W0-8): the e-invoice supply-category
    /// resolver, the B2C dynamic-QR suppressor and the e-Way sub-supply router all call this, because a state code
    /// cannot mean "overseas" on one path and "domestic" on another.
    ///
    /// <para><b>Sourced (R7).</b> The official state-code master at <c>https://einvoice1.gst.gov.in/Others/MasterCodes</c>
    /// (State Codes table) lists <b>96 = OTHER COUNTRIES</b>, <b>97 = Other Territory</b> and <b>99 = OTHER
    /// COUNTRIES</b>. Only 96 and 99 are overseas.</para>
    ///
    /// <para><b>🔴 This CORRECTS a shipped defect on both halves.</b> Three call sites tested
    /// <c>stateCode is "96" or "97"</c>, which (a) mis-classified <b>97</b> as an export when "Other Territory" is a
    /// <b>domestic</b> GST territory — §2(114)(g) of the CGST Act lists "other territory" among the States/Union
    /// territories, and it is the place of supply used for India's continental shelf and exclusive economic zone — and
    /// (b) missed <b>99</b>, a genuine overseas code, entirely. The corrected rule therefore both narrows (97 is no
    /// longer an export) and widens (99 now is).</para>
    ///
    /// <para><b>Reachability caveat (CORRECTED, W0-15).</b> <see cref="Domain.IndianState.All"/> carries 97 but
    /// neither 96 nor 99. This note used to add that <c>PartyGstDetails.EnsureValid</c> "rejects any code outside that
    /// list", implying a live guard; <b>that is false</b> — <c>PartyGstDetails.EnsureValid</c> has <b>no caller
    /// anywhere in <c>src/</c></b> (the import builds a <c>PartyGstDetails</c> without it, and the ledger master screen
    /// validates the State through the picker instead), so nothing rejects a 96/99 party State at the master boundary.
    /// What actually confines the code list is the <b>UI</b>: the State picker offers <c>IndianState.All</c> and
    /// nothing else. Adding 96/99 to <c>IndianState.All</c> was
    /// deliberately NOT done here: <c>Gstin.Validate</c> checks a GSTIN's leading two digits against the same list, and
    /// widening it would start accepting GSTINs beginning "96"/"99", which do not exist. That is a separate slice.</para>
    /// </summary>
    public static bool IsOverseasStateCode(string? stateCode) => stateCode is "96" or "99";

    /// <summary>
    /// True iff a voucher is an <b>outward reverse-charge supply</b> (Phase 9 slice 2; RQ-7): an outward supply whose
    /// sales ledger carries <see cref="StockItemGstDetails.ReverseChargeApplicable"/> — the <b>recipient</b> pays the tax,
    /// so the invoice bears none. Such a supply belongs <b>only</b> in GSTR-1 Table 4B / the 3.1(d)-value bucket, never in
    /// the exempt/nil/non-GST outward bucket (it would otherwise be double-represented). A pure read over the posted lines'
    /// ledgers; a company with no such supply always returns false (byte-identical, ER-13).
    ///
    /// <para><b>"ANY line" is deliberate, and it is the safe direction (W0-1 follow-up).</b> A sale mixing a
    /// reverse-charge leg with a wholly exempt leg answers TRUE, so the print router takes TAX INVOICE for the WHOLE
    /// document. That is correct: §2(98) defines reverse charge as "the liability to pay tax by the recipient …
    /// <b>instead of the supplier</b>", so the RCM leg is a <b>taxable</b> supply; §31(3)(c) reserves the bill of
    /// supply for a supply of <b>exempted</b> goods or services or a §10 dealer, and a supply containing a taxable leg
    /// is neither; and Rule 46(p) requires a Rule-46 tax invoice to state "whether the tax is payable on reverse
    /// charge basis". Rule 46A's combined "invoice-cum-bill of supply" is <b>permissive</b> ("may be issued") and
    /// confined to an <b>unregistered</b> recipient, so it cannot make the bill of supply the required document.
    /// Answering FALSE for the mixed shape would instead demote it to a bill of supply and contradict the app's own
    /// GSTR-1, which files the same voucher in Table 4B as a taxable reverse-charge outward supply. Pinned by
    /// <c>BillOfSupplyPosAndPostingGuardTests.A_partly_reverse_charge_partly_exempt_sale_is_a_tax_invoice_for_the_whole_document</c>.</para>
    /// </summary>
    public static bool IsOutwardReverseChargeSupply(Company company, Voucher voucher)
    {
        foreach (var line in voucher.Lines)
            if (company.FindLedger(line.LedgerId)?.SalesPurchaseGst is { ReverseChargeApplicable: true })
                return true;
        return false;
    }

    /// <summary>
    /// The §34 credit/debit-note link annotating a voucher (Phase 9 slice 2b; RQ-24), or <c>null</c> when the voucher is
    /// not a formalised §34 note. A CDN-linked voucher is a first-class §34 document projected by its own outward table
    /// (GSTR-1 Table 9B, signed by <see cref="GstCreditDebitNoteLink.CdnType"/>) and folded — signed — into the output-tax
    /// buckets, so the ordinary GSTR-1/3B invoice sweeps <b>exclude</b> it (it is never double-counted, mirroring the RCM
    /// and outward-4B exclusions). A company with no §34 note always returns <c>null</c> (byte-identical, ER-13).
    /// </summary>
    public static GstCreditDebitNoteLink? CdnLinkFor(Company company, Voucher voucher) =>
        company.CreditDebitNoteLinks.FirstOrDefault(l => l.CdnVoucherId == voucher.Id);

    /// <summary>
    /// The §10 / Rule 5(f) declaration a composition dealer's <b>Bill of Supply</b> must bear (Phase 9 slice 3; RQ-10;
    /// ER-11: de-branded, never "Tally"). Printed in place of the CGST/SGST/IGST tax columns (a composition supply
    /// carries none).
    /// </summary>
    public const string BillOfSupplyDeclaration = "Composition taxable person, not eligible to collect tax on supplies";

    /// <summary>
    /// The printed title of an outward supply documented under <b>CGST Rule 46</b> (a tax invoice). The single source
    /// the print projector <b>and</b> the <c>InvoicePdf</c> renderer both read, so the printed title and the PDF
    /// metadata can never disagree, and neither can drift from <see cref="BillOfSupplyTitle"/>'s counterpart.
    /// (FIX-W1g: the renderer used to re-spell both literals itself, which made this doc comment false as written.)
    /// </summary>
    public const string TaxInvoiceTitle = "TAX INVOICE";

    /// <summary>
    /// The printed title of an outward supply documented under <b>CGST Rule 49</b> (a bill of supply) — required by
    /// CGST Act §31(3)(c) "instead of a tax invoice" from a registered person supplying exempted goods or services, or
    /// paying tax under §10 (composition).
    /// </summary>
    public const string BillOfSupplyTitle = "BILL OF SUPPLY";

    /// <summary>
    /// <b>The §10 (COMPOSITION) limb of CGST Act §31(3)(c), and ONLY that limb</b> (Phase 9 slice 3; RQ-10): an
    /// outward supply (<see cref="VoucherBaseType.Sales"/>) of a company whose GST is <b>enabled</b> as Composition
    /// (<c>Gst is { Enabled: true, RegistrationType: Composition }</c>). A <b>derived</b> property (no stored flag),
    /// mirroring <see cref="IsOutwardReverseChargeSupply"/>. The <c>Enabled: true</c> gate keeps the badge consistent
    /// with the report gating: a company that enabled GST as Composition and then toggled GST OFF (the F11 disable
    /// branch clears <see cref="GstConfig.Enabled"/> but retains <c>RegistrationType = Composition</c>) renders an
    /// ordinary voucher — matching CMP-08 / GSTR-4 / the Composition-Returns menu, which all hide when GST is off. A
    /// Regular/Unregistered or GST-off company always returns false (byte-identical, ER-13).
    ///
    /// <para><b>🔴 W0-9 — this is NOT the question "is this document a bill of supply?".</b> It used to be named
    /// <c>IsBillOfSupply</c>, which is exactly why a second, wider predicate of the same name grew in
    /// <c>Apex.Desktop.Services.VoucherPrintProjector</c> and the two silently disagreed for a whole document class.
    /// The document question is <see cref="IsBillOfSupply"/>, which carries BOTH limbs of §31(3)(c). Call THIS one
    /// only where the <b>§10 scheme itself</b> is the subject.</para>
    ///
    /// <para><b>The call sites, enumerated — there are FIVE, in two groups</b> (an earlier note said "exactly two, both
    /// about the Rule 5(1)(f) declaration", which was wrong on both counts and would have made a maintainer treat the
    /// other three as mistakes):
    /// <list type="number">
    /// <item><c>VoucherPrintProjector.TopDeclarationFor</c> and <c>VoucherDetailViewModel.BillOfSupplyDeclaration</c> —
    /// the composition-specific <b>Rule 5(1)(f)</b> declaration ("Composition taxable person, not eligible to collect
    /// tax on supplies"), which a REGULAR dealer's exempt bill of supply must never bear because he is not a
    /// composition taxable person.</item>
    /// <item><see cref="IsBillOfSupply"/>'s limb 1, <see cref="IsCompositionSupplyCarryingForwardTax"/> and
    /// <see cref="IsBillOfSupplyForFiling"/> — three places where the §10 <b>scheme</b> is the subject rather than the
    /// declaration: §31(3)(c)'s second limb, the §10(4) contradiction, and the R12 filing ruling. All three would be
    /// WRONG with the wide predicate; see the note on <see cref="IsCompositionSupplyCarryingForwardTax"/> for what
    /// substituting it there would silently switch off.</item>
    /// </list></para>
    /// </summary>
    public static bool IsCompositionBillOfSupply(Company company, Voucher voucher)
    {
        if (company.Gst is not { Enabled: true, RegistrationType: GstRegistrationType.Composition }) return false;
        var type = company.FindVoucherType(voucher.TypeId);
        return type?.BaseType == VoucherBaseType.Sales;
    }

    /// <summary>
    /// The <b>outward supply value</b> of a composition sale (or sale-return note), split (Total, Taxable) by GST
    /// taxability (Phase 9 slice 3; RQ-10/RQ-16; ER-9). A composition voucher carries <b>no tax lines</b>, so turnover
    /// is read from the posted stock/sales <b>value</b>, never from tax lines (<see cref="InvoiceTaxableValue"/> reads
    /// tax lines ⇒ returns 0 and must NOT be used for turnover). An item-invoice sale reads the item-line values, each
    /// classified by its stock item's <see cref="StockItemGstDetails.IsTaxable"/> (falling back to the voucher's
    /// sales-ledger GST block, else treated as taxable). An as-voucher sale sums the sales/income legs on the
    /// <b>sales-natural side</b> — CREDIT for a Sales bill, DEBIT for a sale-return <see cref="VoucherBaseType.CreditNote"/>
    /// (which reverses the sale) — so the party/cash counter-leg is never counted and a return is valued (and classified)
    /// off its own sales ledger, mirroring <see cref="Gstr1"/>'s sign-by-base-type read. Each leg is classified by its
    /// ledger's <see cref="Domain.Ledger.SalesPurchaseGst"/>; the <b>Taxable</b> component counts only an <b>explicitly</b>
    /// taxable leg (an unclassified leg is treated as non-taxable, so it never over-includes an exempt as-voucher sale
    /// into a taxable-only base — finding #1). Reads posted amounts only; the <b>sign</b> (a return nets down) is applied
    /// by the caller.
    /// </summary>
    public static (Money Total, Money Taxable) OutwardSupplyValue(Company company, Voucher voucher, VoucherBaseType baseType)
    {
        if (voucher.HasInventoryLines)
        {
            var total = 0m; var taxable = 0m;
            foreach (var il in voucher.InventoryLines)
            {
                var v = il.Value.Amount;
                total += v;
                if (LineIsTaxable(company, il, voucher)) taxable += v;
            }
            return (new Money(total), new Money(taxable));
        }

        // As-voucher supply: the supply value is the sales/income legs on the sales-natural side (CREDIT for a Sales
        // bill; DEBIT for a sale-return Credit Note, which reverses the sale). Reading the sales side — rather than
        // always the credit legs — keeps the party/cash counter-leg out and reads a return off its reversed sales leg.
        // A Duties & Taxes leg (defensive — none exist for composition) is excluded so it can never inflate turnover.
        var supplySide = baseType == VoucherBaseType.CreditNote ? DrCr.Debit : DrCr.Credit;
        var t = 0m; var tx = 0m;
        foreach (var line in voucher.Lines)
        {
            if (line.Side != supplySide) continue;
            var ledger = company.FindLedger(line.LedgerId);
            if (ledger is null || ClassificationRules.IsDutiesAndTaxesLedger(ledger, company)) continue;
            var v = line.Amount.Amount;
            t += v;
            // TAXABLE component: count only an EXPLICITLY-taxable sales/income leg (finding #1). An unclassified leg
            // (no GST block) is NOT assumed taxable — that would over-include an exempt as-voucher sale into the
            // taxable-only base (Trader / §10(2A)). Total-turnover sub-types read `Total`, so an exempt sale still
            // counts for them (base-rule-aware, not a blanket flip).
            if (ledger.SalesPurchaseGst?.IsTaxable ?? false) tx += v;
        }
        return (new Money(t), new Money(tx));
    }

    /// <summary>Classifies one item-invoice line as a taxable supply: by the stock item's GST taxability, falling back
    /// to any sales-ledger GST block on the voucher, else treated as taxable (conservative for the taxable-base
    /// sub-types).</summary>
    private static bool LineIsTaxable(Company company, VoucherInventoryLine il, Voucher voucher)
    {
        if (company.FindStockItem(il.StockItemId)?.Gst is { } g) return g.IsTaxable;
        foreach (var line in voucher.Lines)
            if (company.FindLedger(line.LedgerId)?.SalesPurchaseGst is { } spg) return spg.IsTaxable;
        return true;
    }

    /// <summary>
    /// The integrated-rate basis points a tax line represents, for rate-wise grouping. A CGST/SGST line carries
    /// the <b>half</b> rate on its <see cref="GstLineTax.RateBasisPoints"/> (900 for an 18% intra supply), so we
    /// double it to recover the integrated slab (1800); an IGST line already carries the full rate.
    ///
    /// <para><b>🔴 W0-10 review (findings #1/#6/#8) — DOUBLING THE HALF IS LOSSY, AND THE LOST BIT IS RECOVERED FROM
    /// THE POSTED TAX.</b> <c>GstService.ComputeInvoiceTax</c> stamps the intra heads with
    /// <c>halfBp = integratedBp / 2</c> using INTEGER division, so an ODD integrated rate loses a basis point on the
    /// way in: 25 bp (0.25%, rough diamonds — a real surviving rate this app itself seeds a history row for) is stamped
    /// as 12 and doubled back to 24. Two consequences, both measured on 60.125 Nos @ ₹786.64 = ₹47,296.73 intra:
    /// the printed breakup row read <c>"0.24%"</c> beside a posted CGST 59.12 + SGST 59.12 that 0.24% cannot produce
    /// (it yields ₹113.51, not ₹118.24) — a self-contradicting CGST Rule 46(m) particular; and a 25 bp group and a
    /// 24 bp group on one invoice COLLAPSED into a single row keyed 24, whose taxable was the max of the two bases, so
    /// one whole rate group vanished from the breakup.</para>
    ///
    /// <para><b>The recovery is exact arithmetic, not a heuristic.</b> Integer truncation leaves exactly two
    /// candidates — <c>2h</c> and <c>2h+1</c> — and the POSTED tax on this very line discriminates them: we re-run the
    /// engine's own <see cref="GstService.ComputeLineTax"/> split on the leg's own declared taxable value and keep the
    /// candidate that reproduces <paramref name="postedTax"/> to the paisa. <b><c>2h</c> wins every tie and every
    /// no-match</b>, so every even rate, every IGST line and every crafted leg whose money explains nothing behaves
    /// byte-identically to before (ER-13); only a leg whose money can ONLY be the odd rate moves. This is a READ of
    /// posted data, never a recompute of it: no master is consulted and no printed money changes.</para>
    ///
    /// <para><b>All four readers share it, so the document, the return and the payloads cannot disagree</b> —
    /// <see cref="ReadPostedRateGroups"/> (the printed breakup), <see cref="InvoiceTaxableValue"/>,
    /// <c>Gstr1.ReadInvoiceRateGroups</c>, <c>EInvoiceJson.ReadRateGroups</c> and <c>EWayBillJson.ReadRateGroups</c>.
    /// The engine-side loss itself is NOT cured here: making <see cref="GstLineTax"/> carry the integrated rate outright
    /// is a persisted-schema change with a migration, recorded as a plan.md carry-forward. Pinned by
    /// <c>ItemInvoicePostedTaxTests.An_odd_basis_point_rate_prints_the_rate_the_supply_actually_bore</c> and
    /// <c>…Two_rate_groups_one_basis_point_apart_do_not_collapse_into_one_row</c>.</para>
    /// </summary>
    /// <param name="gst">The posted leg's GST metadata — the head, the (possibly halved) rate and the taxable value.</param>
    /// <param name="postedTax">That same leg's posted <see cref="EntryLine.Amount"/>, the tax it actually carries.</param>
    public static int IntegratedRateOf(GstLineTax gst, Money postedTax)
    {
        if (gst.TaxHead == GstTaxHead.Integrated) return gst.RateBasisPoints;
        var doubled = gst.RateBasisPoints * 2;
        if (gst.TaxHead is not (GstTaxHead.Central or GstTaxHead.State)) return doubled;
        // `doubled` first: it is the historical answer, so it wins every tie and every unexplained leg (ER-13).
        return Reproduces(doubled) || !Reproduces(doubled + 1) ? doubled : doubled + 1;

        bool Reproduces(int integratedBp)
        {
            var split = GstService.ComputeLineTax(gst.TaxableValue, integratedBp, interState: false);
            var head = gst.TaxHead == GstTaxHead.Central ? split.Cgst : split.Sgst;
            return head.Amount == postedTax.Amount;
        }
    }

    /// <summary>
    /// The taxable value attributable to a voucher's supply: the sum, <b>over each distinct integrated rate
    /// group</b>, of the max taxable value across that group's tax lines. A voucher now posts one tax line per
    /// (head, rate) group, so within one rate group the CGST and SGST lines each record the <b>same</b> group
    /// taxable subtotal (taking the max dedups the two intra heads); an IGST group has a single line. Summing the
    /// per-rate maxes yields the whole-invoice taxable value for a multi-rate invoice (e.g. 1000@18% + 500@5% ⇒
    /// 1500) while still not double-counting the CGST+SGST legs of any one rate group. A single-rate invoice
    /// reduces to the previous "max taxable across tax lines". A voucher with no tax line contributes zero.
    /// <b>Compensation-Cess lines are excluded</b> (Phase 9 slice 1): a cess line records the SAME taxable value on
    /// its own (doubled) cess-rate key, so counting it would double the CGST/SGST taxable value and inject a phantom
    /// rate group into GSTR-1/3B. Cess is a ring-fenced own-column charge, never a CGST/SGST/IGST rate group (ER-2).
    /// </summary>
    public static Money InvoiceTaxableValue(Voucher voucher)
    {
        var maxByRate = new Dictionary<int, decimal>();
        foreach (var line in voucher.Lines)
        {
            if (line.Gst is not { } g) continue;
            if (g.TaxHead == GstTaxHead.Cess) continue; // ring-fenced cess is not a CGST/SGST/IGST rate group
            if (g.IsReverseCharge) continue;            // Phase 9 slice 2: RCM lines are their own buckets, not forward taxable value
            var rate = IntegratedRateOf(g, line.Amount);
            var cur = maxByRate.TryGetValue(rate, out var m) ? m : 0m;
            if (g.TaxableValue.Amount > cur) maxByRate[rate] = g.TaxableValue.Amount;
        }
        return new Money(maxByRate.Values.Sum());
    }

    /// <summary>
    /// The total posted <b>Compensation-Cess</b> on a voucher — the sum of the <see cref="GstTaxHead.Cess"/>,
    /// non-reverse-charge tax-line amounts (Phase 9 slice 5; ER-9). A <b>pure read of the posted lines</b>: because S1's
    /// cess compute already dropped non-tobacco cess on/after 22-Sep-2025 (no cess line posted), this is <b>date-aware by
    /// construction</b> with <b>zero</b> date logic — reading the posted lines IS the date-aware mechanism (risk #1). This
    /// single implementation is shared by <c>EInvoiceJson</c> and the e-Way consignment-value / <c>EWayBillJson</c> writers
    /// so the two can never drift. A voucher with no posted cess line returns <see cref="Money.Zero"/>.
    /// </summary>
    public static Money PostedCessTotal(Voucher voucher)
    {
        var cess = 0m;
        foreach (var line in voucher.Lines)
            if (line.Gst is { TaxHead: GstTaxHead.Cess, IsReverseCharge: false })
                cess += line.Amount.Amount;
        return new Money(cess);
    }

    /// <summary>
    /// The total posted <b>forward</b> GST (CGST + SGST + IGST) on a voucher — the sum of the non-cess,
    /// non-reverse-charge tax-line amounts (Phase 9 slice 5; ER-9). A pure read of the posted lines, mirroring the head
    /// exclusions of <see cref="InvoiceTaxableValue"/> (ring-fenced cess and RCM lines never inflate the forward tax). Used
    /// by the e-Way consignment-value engine; a voucher with no forward tax returns <see cref="Money.Zero"/>.
    /// </summary>
    public static Money PostedForwardTaxTotal(Voucher voucher)
    {
        var tax = 0m;
        foreach (var line in voucher.Lines)
        {
            if (line.Gst is not { } g || g.IsReverseCharge) continue;
            if (g.TaxHead is GstTaxHead.Central or GstTaxHead.State or GstTaxHead.Integrated)
                tax += line.Amount.Amount;
        }
        return new Money(tax);
    }

    /// <summary>
    /// True iff a voucher posts at least one <b>forward</b> GST tax line (a non-reverse-charge CGST/SGST/IGST line) —
    /// i.e. it is a <b>regular tax-scheme</b> supply whose assessable value lives on its tax lines
    /// (<see cref="InvoiceTaxableValue"/>). A supply with <b>no</b> forward tax line — a Composition dealer's Bill of
    /// Supply, an exempt-only movement, or any other no-tax goods movement — carries its value only on the posted
    /// stock/sales lines, so its consignment value must be read from <see cref="OutwardSupplyValue"/> instead (Phase 9
    /// slice 5, finding #1). Mirrors the head/RCM exclusions of <see cref="PostedForwardTaxTotal"/> exactly, so the two
    /// can never disagree on what "carries forward tax" means. A voucher with no tax line returns <c>false</c>.
    /// </summary>
    public static bool HasForwardTaxLines(Voucher voucher)
    {
        foreach (var line in voucher.Lines)
        {
            if (line.Gst is not { } g || g.IsReverseCharge) continue;
            if (g.TaxHead is GstTaxHead.Central or GstTaxHead.State or GstTaxHead.Integrated)
                return true;
        }
        return false;
    }

    /// <summary>
    /// True iff a voucher posted at least one <b>forward</b> (non-reverse-charge) Compensation-Cess line — the twin of
    /// <see cref="HasForwardTaxLines"/> for the ring-fenced cess head, stated as a question so callers need not
    /// compare money. Mirrors the head/RCM exclusions of <see cref="PostedCessTotal"/> so the two can never disagree
    /// on WHICH lines they read.
    ///
    /// <para><b>⚠️ NOT the same predicate as <c>PostedCessTotal(voucher) != Money.Zero</c></b> (W0-1 follow-up, review
    /// finding #7 — the doc comment used to claim it was "the exact predicate" that expresses, which is false). This
    /// answers on the <b>EXISTENCE</b> of a forward cess line; <see cref="PostedCessTotal"/> answers on the <b>SUM</b>
    /// of their amounts. Two forward cess legs that net to zero (or one zero-amount leg on imported/crafted data) make
    /// this <c>true</c> while the total is <see cref="Money.Zero"/>. Pinned by
    /// <c>GstForwardTaxPredicateTests.A_cess_line_exists_even_when_the_posted_cess_sums_to_zero</c>.</para>
    ///
    /// <para><b>🔴 W0-10 review (finding #7) — THE WARNING IS RESTATED AGAINST ITS SURVIVING CONSUMER.</b> It used to
    /// say that substituting the sum for the existence would flip <c>VoucherPrintProjector.HasPostedForwardCess</c>
    /// from "use the POSTED cess" to "re-resolve cess LIVE from the master", reintroducing the F4 defect. <b>That call
    /// site no longer exists</b>: W0-10 deleted the member along with every live cess resolve on the print path, so a
    /// reader chasing it would either conclude the warning was obsolete and merge the two predicates, or waste the
    /// search. The warning still stands, for a different consumer. <see cref="CarriesForwardTax"/> reads this
    /// predicate, and <see cref="IsBillOfSupply"/>'s first gate reads <em>that</em> — so for a voucher whose forward
    /// cess legs net to zero, swapping in <c>PostedCessTotal(…) != Money.Zero</c> would silently re-classify the
    /// DOCUMENT KIND, titling as a BILL OF SUPPLY (and filing NIC <c>BIL</c>) a movement that recorded cess legs. The
    /// two are not interchangeable; the question is "did this voucher record a forward cess leg?", never "do they add
    /// up to something?".</para>
    ///
    /// <para><b>🔴 W0-9 review (finding #3) — AND THE ROUTE IS NARROWER THAN THAT PARAGRAPH SAID, SO IT IS NOW PINNED
    /// RATHER THAN ASSERTED.</b> <see cref="CarriesForwardTax"/> is a THREE-WAY disjunction, and its third disjunct
    /// <see cref="PostsToAnOrdinaryOutputTaxLedger"/> answers off the LEDGER. On every <b>shipped</b> path a posted
    /// cess line lands on a ledger the company CLASSIFIES as Output Cess, because <c>GstService.ComputeInvoiceTax</c>
    /// calls <c>EnsureCessLedgers</c> (<c>if (totalCess != 0m) …</c>) immediately before it emits one — so that disjunct
    /// holds <see cref="CarriesForwardTax"/> true whatever this predicate answers, and the document kind does NOT move
    /// for any voucher this app posts. The substitution bites only where the cess legs are tagged but do NOT land on a
    /// classified GST tax ledger: imported or hand-keyed data, which the canonical importer accepts
    /// (<c>&lt;gst&gt;</c> is optional per entryLine and the ledger it names need carry no classification). That is a
    /// real shape, and it is now pinned in the direction that matters — the DOCUMENT KIND — by
    /// <c>GstForwardTaxPredicateTests.A_netting_cess_pair_off_the_tax_ledgers_still_decides_the_document_kind</c>.
    /// A warning whose only cited pin cannot demonstrate it is how a maintainer talks himself past it.</para>
    ///
    /// <para><b>🔴 W0-9 TAIL review (findings #1/#3) — WHY THE OLD PIN CANNOT DEMONSTRATE THE FLIP, STATED CORRECTLY.</b>
    /// The paragraph above previously said the old pin
    /// <c>GstForwardTaxPredicateTests.A_cess_line_exists_even_when_the_posted_cess_sums_to_zero</c> posts its netting
    /// pair to "the company's own Output Cess ledger". <b>It does not, and the sentence was false about its own cited
    /// fixture.</b> <c>GstService.EnableGst</c> seeds Central/State/Integrated for each direction and <b>never</b> the
    /// Cess pair (<c>EnsureCessLedgers</c> is lazy — <c>SeedAdvancedGst</c>, <c>ComputeInvoiceTax</c>, the deposit /
    /// reversal / set-off / RCM paths), and that test's company calls only <c>EnableGst</c>; so its
    /// <c>FindTaxLedger(Cess, Output)</c> returns <c>null</c>, its <c>??</c> fallback builds an <b>UNCLASSIFIED</b>
    /// ledger named "Output Cess", and <see cref="PostsToAnOrdinaryOutputTaxLedger"/> is <b>FALSE</b> for it. The real
    /// reason it cannot show the flip is elsewhere: its voucher carries no stock lines and no v49 accounting-invoice
    /// flag, so <see cref="IsTaxInvoice"/> is false and <see cref="IsBillOfSupply"/> returns false at limb 2's
    /// <c>if (!IsTaxInvoice(…)) return false;</c> gate whatever <see cref="CarriesForwardTax"/> answers. Both facts are
    /// now <b>asserted inside that test</b> rather than described here, so a maintainer who checks the pin finds the
    /// stated mechanism reproducing instead of concluding the whole warning is stale.</para>
    /// </summary>
    public static bool HasPostedForwardCessLines(Voucher voucher)
    {
        foreach (var line in voucher.Lines)
            if (line.Gst is { TaxHead: GstTaxHead.Cess, IsReverseCharge: false })
                return true;
        return false;
    }

    /// <summary>
    /// True iff a voucher posts at least one line to one of the company's own <b>ordinary Output</b> GST ledgers —
    /// Output CGST / SGST / IGST / Cess, identified by the ledger's
    /// <see cref="Domain.Ledger.GstClassification"/> rather than by any line metadata. The same
    /// ledger-classification read <see cref="RcmLines"/> already performs.
    ///
    /// <para><b>Two exclusions, both load-bearing.</b> <see cref="GstTaxDirection.Input"/> ledgers are ITC — tax the
    /// business PAID, not tax it collected from a recipient. The dedicated <b>RCM Output</b> ledgers
    /// (<c>IsReverseCharge: true</c>, also <c>Direction: Output</c>) are the §49(4) liability the RECIPIENT bears, so
    /// they are not a supplier's collection either; excluding them mirrors the RCM exclusion
    /// <see cref="HasForwardTaxLines"/> and <see cref="PostedForwardTaxTotal"/> already apply. A company with no GST
    /// tax ledgers at all — every Composition company the app itself creates — always returns false, byte-identical
    /// (ER-13).</para>
    /// </summary>
    public static bool PostsToAnOrdinaryOutputTaxLedger(Company company, Voucher voucher)
    {
        // Driven from the LEDGER side, not the line side: there are at most four such ledgers, so this is one pass over
        // the masters plus a line scan only for those four — never `FindLedger` (a linear scan) once per line. It
        // matters because the consumers include `VoucherDetailViewModel.DocumentLabel` /
        // `BillOfSupplyDeclaration`, which are XAML-bound properties re-read on render.
        foreach (var ledger in company.Ledgers)
        {
            if (ledger.GstClassification is not
                { IsReverseCharge: false, Direction: GstTaxDirection.Output } cls) continue;
            if (cls.TaxHead is not (GstTaxHead.Central or GstTaxHead.State
                                    or GstTaxHead.Integrated or GstTaxHead.Cess)) continue;
            foreach (var line in voucher.Lines)
                if (line.LedgerId == ledger.Id) return true;
        }
        return false;
    }

    /// <summary>
    /// <b>🔴 W0-10 review (finding #5) — every rupee of GST the general ledger carries must be VISIBLE to the printer.</b>
    /// True iff the forward tax this voucher posted to the company's own ordinary Output GST ledgers is fully
    /// accounted for by tagged <see cref="GstLineTax"/> legs — i.e. the LEDGER-side total
    /// (<see cref="PostsToAnOrdinaryOutputTaxLedger"/>'s ledger set) equals the METADATA-side total
    /// (<see cref="PostedForwardTaxTotal"/> + <see cref="PostedCessTotal"/>).
    ///
    /// <para><b>Why it exists.</b> Since W0-10 the item pass derives 100% of its tax from <see cref="EntryLine.Gst"/>
    /// metadata, so a Sales item voucher whose Output CGST/SGST legs carry none prints a Grand Total short of the
    /// posted party leg by the WHOLE tax. Reachable without tampering: <c>CanonicalXml</c> makes <c>&lt;gst&gt;</c>
    /// optional on an entryLine (<c>ImportPlan.BuildGstLineTax</c> returns null when it is absent) and the shipped
    /// Sales As-Voucher screen builds every leg with no <c>gst:</c> argument at all. Measured: a voucher billing
    /// ₹55,810.14 with hand-typed Cr Output CGST 4,256.71 / Cr Output SGST 4,256.70 printed a TAX INVOICE demanding
    /// ₹47,296.73 — the same ₹8,513.41 understatement class W0-10 exists to prevent, reached from the other side.
    /// Before W0-10 the live <c>ComputeInvoiceTax</c> reconstructed the tax from the item masters and the document
    /// happened to foot, so the switch REVERSED the direction of failure for this shape.</para>
    ///
    /// <para><b>Deliberately NARROWER than the item-path footing guard plan.md defers</b> (carry-forward (b), sequenced
    /// after the TCS row). A full "printed Grand Total == posted party leg" refusal cannot land yet: §206C TCS rides
    /// the party debit and <c>InvoicePrintData</c> has no TCS field, so it would stop every TCS invoice printing as a
    /// tax invoice — a real regression traded for a crafted-data one. This asks only about the company's own GST
    /// ledgers, and <b>TCS Payable is not one</b>, so a §206C sale cannot trip it. Pinned in both directions by
    /// <c>ItemInvoicePostedTaxTests.An_item_invoice_whose_output_tax_legs_carry_no_metadata_prints_as_the_plain_voucher</c>
    /// and <c>…A_tcs_bearing_invoice_still_prints_as_a_tax_invoice_and_pins_the_known_shortfall</c>.</para>
    ///
    /// <para>Every genuine invoice satisfies it by construction — the accept paths, the POS cart and
    /// <c>CreditDebitNoteService</c> all post the engine's own <c>TaxLines</c>, each stamped — so this is
    /// byte-identical on every shipped path (ER-13). The conservative direction on a false is the same one
    /// <see cref="ServiceInvoiceFoots"/> (F2) takes: the voucher is not an invoice document at all and prints as the
    /// plain Dr/Cr voucher, which states every posted leg exactly.</para>
    /// </summary>
    public static bool PostedOutputTaxIsFullyTagged(Company company, Voucher voucher)
    {
        ArgumentNullException.ThrowIfNull(company);
        ArgumentNullException.ThrowIfNull(voucher);

        // Ledger-driven, exactly as PostsToAnOrdinaryOutputTaxLedger: at most four ledgers, one masters pass.
        var onLedgers = 0m;
        foreach (var ledger in company.Ledgers)
        {
            if (ledger.GstClassification is not
                { IsReverseCharge: false, Direction: GstTaxDirection.Output } cls) continue;
            if (cls.TaxHead is not (GstTaxHead.Central or GstTaxHead.State
                                    or GstTaxHead.Integrated or GstTaxHead.Cess)) continue;
            foreach (var line in voucher.Lines)
                if (line.LedgerId == ledger.Id) onLedgers += line.Amount.Amount;
        }

        // The metadata side, from the SAME two reads the projector's money comes from — so "visible to the printer"
        // means literally that, and the two can never drift apart.
        var tagged = PostedForwardTaxTotal(voucher).Amount + PostedCessTotal(voucher).Amount;
        return onLedgers == tagged;
    }

    /// <summary>
    /// <b>W0-1 follow-up (review finding #1) — "carries forward tax" is a question about the GENERAL LEDGER, not about
    /// metadata.</b> True iff <paramref name="voucher"/> records forward (non-reverse-charge) GST collected from the
    /// recipient, by <b>either</b> route: a tagged tax line (<see cref="HasForwardTaxLines"/> /
    /// <see cref="HasPostedForwardCessLines"/>, which read <see cref="EntryLine.Gst"/>), <b>or</b> a plain untagged
    /// posting to one of the company's own ordinary Output tax ledgers
    /// (<see cref="PostsToAnOrdinaryOutputTaxLedger"/>).
    ///
    /// <para><b>Why the second route exists.</b> Only the GST-engine accept paths stamp
    /// <see cref="GstLineTax"/>. The shipped Sales <b>As-Voucher</b> screen does not: it builds every leg as a plain
    /// <c>new EntryLine(ledgerId, amount, side, …)</c> with no <c>gst:</c> argument, and its particulars picker is the
    /// unfiltered company ledger list. A composition dealer could therefore hand-key <c>Cr Output CGST / Cr Output
    /// SGST</c> and be invisible to every metadata-only predicate — so the §10(4) posting guard accepted the very
    /// entry it exists to refuse, and the document then routed as a BILL OF SUPPLY bearing the Rule 5(1)(f)
    /// declaration that he may not collect tax, printed above entry rows reading Output CGST / Output SGST.</para>
    ///
    /// <para><b>Any line, not only a credit</b> — deliberately, and it is the safe direction. The sibling
    /// <see cref="HasForwardTaxLines"/> is side-agnostic too, no shipped path posts a Sales-side DEBIT to an Output
    /// GST head, and both consumers fail SAFE on a true: the posting guard refuses an anomalous entry, and the print
    /// router falls back to the plain Dr/Cr voucher, which states every posted leg exactly.</para>
    ///
    /// <para>Sources: CGST Act §10(4), §31(3)(c), §32(2) —
    /// <c>https://cbic-gst.gov.in/pdf/CGST-Act-Updated-30092020.pdf</c>.</para>
    /// </summary>
    public static bool CarriesForwardTax(Company company, Voucher voucher)
    {
        ArgumentNullException.ThrowIfNull(company);
        ArgumentNullException.ThrowIfNull(voucher);
        return HasForwardTaxLines(voucher)
            || HasPostedForwardCessLines(voucher)
            || PostsToAnOrdinaryOutputTaxLedger(company, voucher);
    }

    /// <summary>
    /// <b>W0-1 — the §10 CONTRADICTION, in one place.</b> True iff <paramref name="voucher"/> is a composition
    /// dealer's outward supply (<see cref="IsCompositionBillOfSupply"/>) that NONETHELESS carries posted <b>forward</b>
    /// CGST/SGST/IGST or Compensation Cess.
    ///
    /// <para><b>🔴 THE CREF ABOVE IS LOAD-BEARING, AND IT USED TO BE WRONG.</b> It read
    /// <see cref="IsBillOfSupply"/> while the body correctly called <see cref="IsCompositionBillOfSupply"/> — so a
    /// maintainer "correcting" the body to match the prose would have made this method <b>identically FALSE</b>:
    /// <see cref="IsBillOfSupply"/>'s FIRST gate is <c>if (CarriesForwardTax(…)) return false;</c>, so the conjunction
    /// <c>IsBillOfSupply(…) &amp;&amp; CarriesForwardTax(…)</c> can never be true for any voucher whatsoever. A
    /// predicate that is constantly false raises no error and breaks no compile — it just silently switches off every
    /// consumer at once: the <c>VoucherValidator</c> §10(4) POSTING guard would accept the entry it exists to refuse,
    /// <c>VoucherPrintProjector.ProjectInvoice</c>'s structural refusal would stop firing, and the measured
    /// ₹47,296.73-printed-against-₹55,810.14-posted understatement (a Grand Total short by the whole ₹8,513.41) would
    /// re-open. The two predicates are NOT interchangeable here and never can be: this one asks "is he a §10 dealer?",
    /// the wide one asks "may a bill of supply be issued?" — and the second already answers no whenever the first
    /// conjunct's partner answers yes.</para>
    ///
    /// <para>Such a voucher asserts two incompatible things at once. CGST Act §31(3)(c) makes his document a bill of
    /// supply unconditionally ("shall issue, <i>instead of a tax invoice</i>"), while §10(4) says he "shall not
    /// collect any tax from the recipient on supplies made by him" — so the tax that IS in the GL cannot lawfully sit
    /// on any document he issues. §32(2) forbids a registered person collecting tax otherwise than as the Act allows.
    /// A TAX INVOICE is the exact document §31(3)(c) denies him; a BILL OF SUPPLY shows no tax, so its total would
    /// fall short of the posted party leg.</para>
    ///
    /// <para><b>This is the single definition three layers now share</b> — the posting guard
    /// (<c>VoucherValidator</c>, which refuses the entry outright), the document-kind predicate
    /// (<see cref="IsTaxInvoice"/>) and the projector's own structural refusal
    /// (<c>VoucherPrintProjector.ProjectInvoice</c>). Copies of a routing rule are how this defect class keeps being
    /// reborn (the POS receipt was the fourth instance), so there is exactly one <b>of this predicate</b>. (W0-8: the
    /// fifth instance, <c>EWayBillService</c>'s NIC Part-A <c>docType</c>, routes through
    /// <see cref="IsBillOfSupply"/> too. The e-invoice INV-01 <c>DocDtls.Typ</c> deliberately does NOT join them: it is
    /// a different, three-value code domain.)</para>
    ///
    /// <para><b>W0-9 — the qualification the previous note carried is now DISCHARGED.</b> That note had to warn that
    /// "a bill-of-supply movement files BIL, not INV" was FALSE for half the document class, because
    /// <see cref="IsBillOfSupply"/> was then the §10 limb only while <c>VoucherPrintProjector</c> held the §31(3)(c)
    /// wholly-exempt limb — so a REGULAR dealer's exempt movement printed BILL OF SUPPLY and filed <c>INV</c>. The
    /// exempt limb has been lifted into this layer, so <see cref="IsBillOfSupply"/> is now the WHOLE section and the
    /// sentence holds unqualified. <b>This predicate is deliberately narrower</b>: it is the §10 contradiction, so it
    /// reads <see cref="IsCompositionBillOfSupply"/> — a regular dealer's exempt supply is not a §10 supply and
    /// §10(4) has nothing to say about it.</para>
    ///
    /// <para><b>W0-1 follow-up (review finding #1):</b> "carries forward tax" reads
    /// <see cref="CarriesForwardTax"/>, which answers off the GENERAL LEDGER — a plain untagged credit to an Output
    /// CGST/SGST/IGST/Cess ledger counts. The metadata-only version missed the entire As-Voucher entry path, which
    /// stamps no <see cref="GstLineTax"/> at all.</para>
    ///
    /// <para>Sources: CGST Act §31(3)(c), §10(4), §32(2) —
    /// <c>https://cbic-gst.gov.in/pdf/CGST-Act-Updated-30092020.pdf</c>.</para>
    /// </summary>
    public static bool IsCompositionSupplyCarryingForwardTax(Company company, Voucher voucher)
    {
        ArgumentNullException.ThrowIfNull(company);
        ArgumentNullException.ThrowIfNull(voucher);
        return IsCompositionBillOfSupply(company, voucher) && CarriesForwardTax(company, voucher);
    }

    // ================================================================ W0-9: the ONE bill-of-supply rule

    /// <summary>
    /// <b>W0-9 — THE statutory document kind of an outward supply, in ONE place.</b> True iff this voucher must be
    /// issued as a <b>bill of supply</b> rather than a tax invoice.
    ///
    /// <para>CGST Act §31(3)(c): "a registered person supplying <b>exempted</b> goods or services or both <b>or</b>
    /// paying tax under the provisions of <b>section 10</b> shall issue, <i>instead of a tax invoice</i>, a bill of
    /// supply". Two limbs:</para>
    /// <list type="number">
    /// <item><b>The §10 limb</b> — a composition dealer (<see cref="IsCompositionBillOfSupply"/>). §10(4) bars him
    /// from collecting "any tax from the recipient on supplies made by him".</item>
    /// <item><b>The exempt limb</b> — a supply <b>every</b> line of which is explicitly Exempt / Nil-rated / Non-GST.
    /// §2(47) defines an exempt supply as one which "attracts nil rate of tax or which may be wholly exempt from tax
    /// under section 11 … and <b>includes non-taxable supply</b>", so all three taxabilities take this limb. A supply
    /// carrying even one taxable line is a tax invoice (Rule 46A's combined "invoice-cum-bill of supply" is permissive
    /// and confined to an unregistered recipient).</item>
    /// </list>
    ///
    /// <para><b>🔴 WHY THIS LIVES HERE, AND WHAT MOVING IT FIXED.</b> Until W0-9 the exempt limb lived in
    /// <c>Apex.Desktop.Services.VoucherPrintProjector</c> — a project <c>Apex.Ledger</c> cannot reference — while the
    /// §10 limb lived here. So there were TWO predicates named <c>IsBillOfSupply</c> and they disagreed: the printed
    /// document read the wide (Desktop) one, and the e-Way Bill Part-A <c>docType</c> read the narrow (engine) one.
    /// A REGULAR dealer's wholly-exempt goods movement — the commoner of the two shapes by far — therefore printed
    /// <b>BILL OF SUPPLY</b> on paper while the EWB-01 declared <c>docType "INV"</c>, a Tax Invoice: one consignment,
    /// two mutually exclusive statutory claims, with the wrong one on the government filing. The root cause was
    /// LAYERING, not oversight, so the fix has exactly one direction — the rule moves DOWN to where every consumer can
    /// reach it. <b>Do not add a third copy, and do not teach any consumer its own exempt test.</b></para>
    ///
    /// <para><b>Two conservative gates, both structural.</b> An <b>unresolved</b> line (no GST master anywhere,
    /// <see cref="GstService.IsUnresolved"/>) is NOT read as exempt — silence is not an exemption, and reading it as
    /// one would strip the tax breakup off a genuinely taxable supply. And a voucher that <b>posted</b> forward
    /// CGST/SGST/IGST or Compensation Cess can never be a bill of supply whatever its registration says: the document
    /// must state the debt the GL actually recorded, and a bill of supply shows no tax, so titling it one would print a
    /// Grand Total short of the posted party leg.</para>
    ///
    /// <para>Sources: CGST Act §31(3)(c), §2(47), §2(98), §10(4) —
    /// <c>https://cbic-gst.gov.in/pdf/CGST-Act-Updated-30092020.pdf</c>.</para>
    /// </summary>
    public static bool IsBillOfSupply(Company company, Voucher voucher)
    {
        ArgumentNullException.ThrowIfNull(company);
        ArgumentNullException.ThrowIfNull(voucher);

        // FIX-W1a — §31(3)(c) binds "A REGISTERED PERSON". Both limbs therefore require GST to be ON. Limb 1 carries
        // this gate already (IsCompositionBillOfSupply tests `Gst is { Enabled: true, … }`); limb 2 shipped without it,
        // so a company that enabled GST, classified an item Exempt, then switched GST OFF in F11 — the disable branch
        // clears Enabled but RETAINS the config and every master — printed a document titled BILL OF SUPPLY and badged
        // the drill "Bill of Supply", naming a GST statutory document for a business that is not registered under GST
        // at all and whose GST menu is hidden. One gate at the top so the two limbs can never drift apart on it again.
        if (!company.GstEnabled) return false;

        // A document that carries posted forward tax states that tax, whatever else is true of the supplier.
        //
        // W0-1 follow-up (review finding #1): this reads CarriesForwardTax, which answers off the GENERAL LEDGER — a
        // plain credit to an Output CGST/SGST/IGST/Cess ledger counts, tagged or not. The two metadata-only predicates
        // it used to call require `line.Gst is not null`, which ONLY the GST-engine accept paths stamp: the shipped
        // Sales As-Voucher screen stamps none, so a composition dealer's hand-keyed Cr Output CGST / Cr Output SGST
        // sailed through this gate and the drilled voucher badged itself "Bill of Supply" with Rule 5(1)(f)'s "not
        // eligible to collect tax on supplies" printed directly above entry rows reading Output CGST 4,256.71 /
        // Output SGST 4,256.70. The exact false statutory statement FIX-W1e removed.
        if (CarriesForwardTax(company, voucher)) return false;

        // Limb 1 — §10 (composition). Applies to every outward Sales voucher, invoice-format or not.
        if (IsCompositionBillOfSupply(company, voucher)) return true;

        // FIX-W1b — an outward REVERSE-CHARGE supply is a TAXABLE supply, so §31(3)(c) does not reach it. §2(98)
        // defines reverse charge as "the liability to pay tax by the recipient … INSTEAD OF THE SUPPLIER": the tax is
        // due, it merely moves. §31(3)(c) reserves the bill of supply for an exempted supply or a §10 dealer, and
        // neither describes him — so the document stays a Rule-46 TAX INVOICE (which Rule 46(p) additionally requires
        // to state "whether the tax is payable on reverse charge basis"; that clause is a separate slice).
        //
        // It must be tested BELOW limb 1 (a §10 dealer issues a bill of supply either way) and ABOVE limb 2, because
        // limb 2 classifies purely by declared taxability: the app's own — and only reachable — shape for an outward
        // RCM sale is a sales ledger flagged `ReverseChargeApplicable` and declared Nil-rated/Exempt so it posts no
        // forward tax (Gstr1.cs:246), which limb 2 would read as "wholly exempt" and mis-title. That would put the
        // paper in direct contradiction with the app's own return, where ComputeRcm4BOutwardValue files the SAME
        // voucher in GSTR-1 Table 4B as a TAXABLE reverse-charge outward supply and Gstr1 deliberately keeps it OUT
        // of the exempt bucket (Gstr1.cs:249) — one voucher, two mutually exclusive statutory claims.
        if (IsOutwardReverseChargeSupply(company, voucher)) return false;

        // Limb 2 — wholly exempt / nil-rated / non-GST. Only a document-producing supply has lines to classify.
        if (!IsTaxInvoice(company, voucher)) return false;
        return IsServiceAccountingInvoice(company, voucher)
            ? IsWhollyExemptServiceSupply(company, voucher)
            : IsWhollyExemptItemSupply(company, voucher);
    }

    // ================================================================ W0-9 review — FILING is not PRINTING

    /// <summary>
    /// <b>W0-9 review fix — the document kind an OUTWARD movement <i>FILES</i>, which is not always the one it
    /// <i>PRINTS</i>.</b> True iff the NIC e-Way Part-A must declare <c>BIL</c> (Bill of Supply) rather than
    /// <c>INV</c> (Tax Invoice) for this outward movement. It differs from <see cref="IsBillOfSupply"/> in <b>exactly
    /// one</b> shape, deliberately: a §10 (composition) outward supply that NONETHELESS carries posted forward tax.
    ///
    /// <para><b>🔴 A RECORDED USER RULING (R12, taken 2026-08-14): file <c>BIL</c>. Dealer status decides.</b> Before
    /// W0-9 this shape filed <c>BIL</c>, because the engine predicate the e-Way path read was the §10 limb alone. W0-9
    /// routed the filing through the unified <see cref="IsBillOfSupply"/>, whose FIRST gate is
    /// <see cref="CarriesForwardTax"/> — and that gate exists for a <b>PRINT-MONEY</b> reason, stated in its own
    /// comment: a bill of supply shows no tax, so titling a tax-carrying document one "would print a Grand Total short
    /// of the posted party leg". The flip to <c>INV</c> was therefore an accident of layering: a money gate was handed
    /// authority over a <b>filing field that carries no money at all</b> — a three-letter document-kind declaration on
    /// the EWB-01.</para>
    ///
    /// <para><b>The statutory ground.</b> CGST Act §31(3)(c) is <b>unconditional</b> for a §10 person: he "shall issue,
    /// <i>instead of a tax invoice</i>, a bill of supply". Nothing in the section is contingent on what he collected —
    /// and §10(4) separately bars him from collecting "any tax from the recipient on supplies made by him", so posted
    /// forward tax on his books is an <b>unlawful fact about the ledger</b>, never a re-characterisation of the
    /// document. Filing <c>INV</c> would declare to the portal the exact document the section denies him. So the §10
    /// half of the decision routes off <see cref="IsCompositionBillOfSupply"/> alone.</para>
    ///
    /// <para><b>The ruling is confined to the §10 limb, and the asymmetry is the point.</b> A REGULAR dealer's
    /// wholly-exempt supply that posted forward tax still files <c>INV</c> (it fails both disjuncts). Nothing bars a
    /// regular dealer from collecting tax, so his posted tax is positive evidence that the supply was not exempt and
    /// the document was a tax invoice; §10(4) bars a composition dealer absolutely, so his cannot carry that meaning.
    /// Pinned by <c>OneBillOfSupplyRuleTests.The_ruling_does_not_reach_a_regular_dealers_exempt_supply_that_posted_tax</c>.</para>
    ///
    /// <para><b>The PRINT is untouched, and must be.</b> <see cref="IsBillOfSupply"/> still answers <c>false</c> for
    /// the shape and <see cref="IsTaxInvoice"/> still answers <c>false</c>, so
    /// <c>VoucherPrintProjector.ProjectInvoice</c> keeps refusing it structurally and it prints as the plain Dr/Cr
    /// voucher. There is no contradiction between paper and filing here, because no statutory <b>title</b> is printed
    /// at all — the divergence is between a filing that must name a document kind and a document that is never issued.
    /// </para>
    ///
    /// <para><b>Edge/legacy data only.</b> <c>VoucherValidator.EnsureValid</c> refuses to POST the shape on every entry
    /// path (the §10(4) guard), so it is reachable only on a book that already contains it — the Regular dealer who
    /// posts taxed sales and later opts into composition in F11, then files an old movement.</para>
    ///
    /// <para>Sources: CGST Act §31(3)(c), §10(4), §32(2) —
    /// <c>https://cbic-gst.gov.in/pdf/CGST-Act-Updated-30092020.pdf</c>.</para>
    /// </summary>
    public static bool IsBillOfSupplyForFiling(Company company, Voucher voucher)
    {
        ArgumentNullException.ThrowIfNull(company);
        ArgumentNullException.ThrowIfNull(voucher);
        return IsCompositionBillOfSupply(company, voucher) || IsBillOfSupply(company, voucher);
    }

    /// <summary>
    /// <b>W0-9 review fix — the INWARD document kind, which was the SIXTH copy of the rule.</b> True iff the supplier's
    /// document for this inward goods movement was a <b>bill of supply</b> rather than a tax invoice, so the e-Way
    /// Part-A must file <c>BIL</c>. <c>EWayBillService.PartACodesFor</c>'s Purchase arm used to hardcode
    /// <c>("I","1","INV")</c> — a document kind decided from the BASE TYPE alone, three lines below the outward arm
    /// W0-9 had just routed through the shared rule.
    ///
    /// <para><b>The defect was live, not theoretical.</b> Sales and Purchase are the only two limbs of
    /// <c>PartACodesFor</c> that can execute in the shipped app. A Regular dealer buying wholly-exempt goods
    /// inter-state above the ₹50,000 threshold filed a <b>Tax Invoice</b> for a consignment that can only have
    /// travelled on a bill of supply. <b>Exemption is a property of the GOODS, not of the counterparty</b>, so the
    /// supplier was bound by §31(3)(c)'s first limb exactly as the outward rule binds us; and NIC's own mapping carries
    /// <c>Inward | 1 Supply | BIL</c> (From = Other GSTIN/URP, To = Self), so no code-domain constraint ever forced the
    /// wrong value.</para>
    ///
    /// <para><b>Both limbs of §31(3)(c), seen from the buyer's side.</b> The section binds the SUPPLIER, so this reads
    /// what the buyer's own books can honestly say about him: (1) a party the masters record as a <b>composition</b>
    /// dealer issues a bill of supply whatever the goods, since §31(3)(c) is unconditional for him and §10(4) means he
    /// charged nothing; (2) a supply <b>every</b> line of which resolves to an explicit non-taxable taxability is an
    /// exempt supply under §2(47) ("attracts nil rate … or … wholly exempt … and includes non-taxable supply"),
    /// whoever the supplier is. The very same <see cref="IsWhollyExemptItemSupply"/> the outward limb uses answers (2),
    /// so the two directions can never disagree about what "wholly exempt" means.</para>
    ///
    /// <para><b>Three conservative gates, mirroring the outward rule.</b> GST must be ON (a statutory GST document is
    /// not named for a business that is not registered). An <b>unresolved</b> line is not read as exempt — silence is
    /// not an exemption. And a movement whose books record <b>any</b> GST tax at all
    /// (<see cref="RecordsAnyGstTax"/> — tagged metadata, or a plain posting to any of the company's GST tax ledgers)
    /// keeps <c>INV</c>: recorded tax is evidence the supplier charged it, so his document was a tax invoice however
    /// our own item master is classified. This also keeps a reverse-charge inward supply on <c>INV</c>, which is
    /// correct — §2(98) moves the liability to the recipient "instead of the supplier", it does not extinguish it, and
    /// a Rule-46 tax invoice (or a §31(3)(f) self-invoice) is the document either way.</para>
    ///
    /// <para><b>Not modelled: imports.</b> An import purchase should file <c>2</c> Import + <c>BOE</c>; the app holds
    /// no Bill of Entry document, so it stays on the ordinary inward row rather than claim a customs document it does
    /// not have. Unchanged by this fix — see <c>EWayBillService.PartACodesFor</c>.</para>
    ///
    /// <para>Sources: CGST Act §31(3)(c), §2(47), §2(98), §10(4) —
    /// <c>https://cbic-gst.gov.in/pdf/CGST-Act-Updated-30092020.pdf</c>; NIC Supply-Type/Document-Type mapping —
    /// <c>https://docs.ewaybillgst.gov.in/apidocs/sub-docType-mapping.html</c>.</para>
    /// </summary>
    public static bool IsInwardBillOfSupply(Company company, Voucher voucher)
    {
        ArgumentNullException.ThrowIfNull(company);
        ArgumentNullException.ThrowIfNull(voucher);

        if (!company.GstEnabled) return false;
        if (company.FindVoucherType(voucher.TypeId)?.BaseType != VoucherBaseType.Purchase) return false;

        // Recorded tax ⇒ the supplier charged it ⇒ his document was a tax invoice, whatever our masters declare.
        if (RecordsAnyGstTax(company, voucher)) return false;

        // Limb 1 — §10, from the buyer's side: the counterparty is a composition dealer.
        if (voucher.PartyId is Guid pid
            && company.FindLedger(pid)?.PartyGst is { RegistrationType: GstRegistrationType.Composition })
            return true;

        // Limb 2 — wholly exempt / nil-rated / non-GST goods. Exemption belongs to the goods, not the counterparty.
        return IsWhollyExemptItemSupply(company, voucher);
    }

    /// <summary>
    /// True iff a voucher records <b>any</b> GST tax at all, by either route: a line carrying <see cref="GstLineTax"/>
    /// metadata (any head, any direction, forward or reverse-charge), or a plain untagged posting to any ledger the
    /// company classifies as a GST tax ledger (<see cref="Domain.Ledger.GstClassification"/>).
    ///
    /// <para><b>Deliberately wider than <see cref="CarriesForwardTax"/></b>, which asks the narrower question "did this
    /// document COLLECT forward tax from the recipient?" and therefore excludes Input ledgers and the RCM Output
    /// ledgers. On the INWARD side the question is the opposite one — "is there any sign the supplier's document was a
    /// tax invoice?" — and an Input CGST debit, or an RCM leg, is exactly that sign. Substituting
    /// <see cref="CarriesForwardTax"/> here would let a purchase carrying full ITC file <c>BIL</c>.</para>
    ///
    /// <para>Driven from the LEDGER side for the classification half (there are only a handful of GST tax ledgers), the
    /// same shape <see cref="PostsToAnOrdinaryOutputTaxLedger"/> uses.</para>
    /// </summary>
    private static bool RecordsAnyGstTax(Company company, Voucher voucher)
    {
        foreach (var line in voucher.Lines)
            if (line.Gst is not null) return true;

        foreach (var ledger in company.Ledgers)
        {
            if (ledger.GstClassification is null) continue;
            foreach (var line in voucher.Lines)
                if (line.LedgerId == ledger.Id) return true;
        }
        return false;
    }

    /// <summary>The exempt limb for an ITEM supply: at least one stock line, and every one of them resolves to an
    /// explicit non-taxable taxability. An unresolved line disqualifies (see <see cref="IsBillOfSupply"/>).
    /// <para><b>Direction-neutral, and shared on purpose.</b> The outward limb of <see cref="IsBillOfSupply"/> and the
    /// inward limb of <see cref="IsInwardBillOfSupply"/> both call it, because "wholly exempt" is a statement about the
    /// GOODS (§2(47)) and cannot be allowed to mean one thing on a sale and another on a purchase.</para></summary>
    private static bool IsWhollyExemptItemSupply(Company company, Voucher voucher)
    {
        if (voucher.InventoryLines.Count == 0) return false;
        var gst = new GstService(company);
        // Resolve the value ledger EXACTLY as VoucherPrintProjector.ProjectInvoice does — `partyLedger?.Id`, not the
        // raw `voucher.PartyId`. They differ when the party ledger no longer exists: a dangling PartyId would still
        // exclude the party's own line from the fallback here while ProjectInvoice (passing null) would admit it, so
        // the two could resolve different rates and the printed title could contradict the printed breakup.
        var partyLedger = voucher.PartyId is Guid pid ? company.FindLedger(pid) : null;
        var valueLedger = ResolveValueLedger(company, voucher, partyLedger?.Id);
        foreach (var il in voucher.InventoryLines)
        {
            var res = gst.ResolveRate(company.FindStockItem(il.StockItemId), valueLedger, voucher.Date);
            if (res.IsTaxable || GstService.IsUnresolved(res)) return false;
        }
        return true;
    }

    /// <summary>The exempt limb for a SERVICE (Accounting Invoice) supply: at least one service-income leg, and every
    /// one of them declares a non-taxable supply. A <b>zero-rated</b> (0%, LUT/export) ledger declares
    /// <c>IsTaxable = true</c>, so it correctly stays a tax invoice — a zero-rated supply is a taxable supply, not an
    /// exempt one.
    /// <para><b>SETTLED, USER-RATIFIED (R12, 2026-08-10) — a wholly exempt SERVICE sale is a BILL OF SUPPLY.</b> W0-1
    /// inverted the shipped behaviour of a wholly-exempt Accounting Invoice (TAX INVOICE ⇒ BILL OF SUPPLY) on the
    /// slice's own authority, and the pre-existing test that was written to pin the old behaviour stayed green through
    /// the inversion because it asserted a different predicate. The user has now explicitly ratified the new
    /// behaviour, so it is a decision of record rather than an unreviewed side effect. The ground is CGST Act
    /// §31(3)(c), which binds "a registered person supplying exempted goods <b>or services</b> or both" — the exempt
    /// limb reaches services on the face of the section, and §2(47) defines an exempt supply as one which "attracts
    /// nil rate of tax or which may be wholly exempt from tax under section 11 … and includes non-taxable supply".
    /// Source: <c>https://cbic-gst.gov.in/pdf/CGST-Act-Updated-30092020.pdf</c>.</para>
    /// <para><b>FIX-W1d — what happens to a leg carrying NO GST block at all.</b> The comment here used to claim such a
    /// leg "is treated as taxable (silence is not an exemption)", the item limb's rule. That is not what this code
    /// does, and the next reader could have deleted a guard on the strength of it: this loop never SEES such a leg,
    /// because <see cref="Gstr1.ServiceLegs"/> skips every line whose ledger has <c>SalesPurchaseGst is null</c>. On
    /// this method's own terms a plain income leg is therefore invisible, and an invoice mixing one exempt SAC leg
    /// with one plain income leg would return TRUE. The protection is structural but lives ELSEWHERE and one conjunct
    /// earlier: <see cref="ServiceInvoiceFoots"/> rejects any voucher whose projected total (built from the SAC legs
    /// alone) misses the posted party leg, so such a voucher is not an <see cref="IsServiceAccountingInvoice"/> at all
    /// and is demoted to the plain Dr/Cr print before this method is ever consulted.</para></summary>
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

    // ================================================================ W0-9: the document-kind predicates it needs

    /// <summary>
    /// True iff <paramref name="voucher"/> should be issued as a GST <b>invoice document</b> (tax invoice or, per
    /// <see cref="IsBillOfSupply"/>, bill of supply) rather than a plain Dr/Cr voucher: a Sales voucher carrying
    /// item-invoice stock lines, <b>or</b> a Sales <b>accounting (service) invoice</b>
    /// (<see cref="IsServiceAccountingInvoice"/>). Purchase item-invoices and every other voucher print as the plain
    /// voucher (RQ-10).
    ///
    /// <para><b>W0-9 — moved down from <c>VoucherPrintProjector</c> unchanged</b>, because
    /// <see cref="IsBillOfSupply"/>'s exempt limb gates on it and that rule now serves the engine as well as the
    /// printer. <c>VoucherPrintProjector.IsTaxInvoice</c> is now a pure forward to this.</para>
    ///
    /// <para><b>FIX-W1c — the §10 CONTRADICTION is NEITHER document.</b> A composition dealer's outward supply
    /// carrying POSTED forward CGST/SGST/IGST (or cess) asserts two incompatible things at once: §31(3)(c) makes his
    /// document a bill of supply unconditionally ("shall issue, INSTEAD OF a tax invoice"), while §10(4) says he
    /// "shall not collect any tax from the recipient" — so the tax that IS in the GL cannot lawfully be on any document
    /// he issues. Picking either paper silently launders the contradiction: a TAX INVOICE is the exact document
    /// §31(3)(c) forbids him, and a BILL OF SUPPLY shows no tax, so its Grand Total would fall short of the posted
    /// party leg. So the voucher is not projected as an invoice at all and prints as the plain Dr/Cr voucher, which
    /// states the posted legs exactly as recorded.</para>
    ///
    /// <para><b>W0-1 follow-up — the POSTING is refused at ACCEPT (R12 user decision, 2026-08-10), and this branch is
    /// RE-DOCUMENTED rather than removed.</b> <c>VoucherValidator.EnsureValid</c> rejects the shape outright on every
    /// entry path, citing §10(4) by name, so no NEW voucher can reach here. The branch is not dead code and must not be
    /// deleted: the posting guard is deliberately scoped to entry, because <c>SqliteCompanyStore.Load</c> re-posts
    /// every stored voucher and a guard that refused to LOAD would make a book that ALREADY contains the shape
    /// unopenable. So an existing (or imported) anomalous voucher still opens, still reads and still reaches this
    /// predicate — and this is what makes it printable at all. The reachable route: a Regular dealer posts a taxed sale
    /// and later opts into composition (F11 GST is idempotent and checks no existing voucher), and every one of his
    /// old, lawfully-issued tax invoices hits this branch on REPRINT.</para>
    /// </summary>
    public static bool IsTaxInvoice(Company company, Voucher voucher)
    {
        ArgumentNullException.ThrowIfNull(company);
        ArgumentNullException.ThrowIfNull(voucher);
        var type = company.FindVoucherType(voucher.TypeId);
        if (type?.BaseType != VoucherBaseType.Sales) return false;
        // The §10 contradiction, from the ONE shared definition — the same one the posting guard and
        // ProjectInvoice's structural refusal read, so the three can never drift apart.
        if (IsCompositionSupplyCarryingForwardTax(company, voucher)) return false;
        // W0-10 review (finding #5): the ITEM limb's own reconciliation. Since the item pass takes 100% of its tax
        // from GstLineTax metadata, a voucher carrying Output GST the metadata cannot see would print a Grand Total
        // short of the posted party leg — so it is not an invoice document at all. See PostedOutputTaxIsFullyTagged
        // for why this is narrower than (and does not pre-empt) plan.md's deferred item-path footing guard.
        if (voucher.HasInventoryLines) return PostedOutputTaxIsFullyTagged(company, voucher);
        return IsServiceAccountingInvoice(company, voucher);
    }

    /// <summary>
    /// True iff a <b>ledger-only Sales</b> voucher is a SERVICE (Accounting Invoice) sale — the one ledger-only shape
    /// that may print as a tax invoice. Six conjuncts, all structural:
    /// <list type="number">
    /// <item>its type's base type is <see cref="VoucherBaseType.Sales"/> (checked HERE, not only in
    /// <see cref="IsTaxInvoice"/>, so calling <c>VoucherPrintProjector.ProjectInvoice</c> directly on a ledger-only
    /// PURCHASE cannot divert into the service projection);</item>
    /// <item>it carries no stock lines (<see cref="Voucher.HasInventoryLines"/>) — an item invoice takes the item
    /// projection; and</item>
    /// <item>it was <b>posted from the Accounting Invoice entry mode</b>
    /// (<see cref="Voucher.IsAccountingInvoice"/>, schema v49) and carries at least one SAC-bearing service-income leg
    /// (<see cref="Gstr1.ServiceLegs"/>), so the document has a line to print.</item>
    /// </list>
    ///
    /// <para><b>W0-9 — moved down from <c>VoucherPrintProjector</c> unchanged</b>: it is a conjunct of
    /// <see cref="IsBillOfSupply"/>'s exempt limb, which the e-Way engine now reads.</para>
    ///
    /// <para><b>The persisted flag is the whole gate — it replaced an inference that was wrong in both directions.</b>
    /// The gate used to key on "posts a forward GST tax leg carrying <see cref="GstLineTax"/> metadata"
    /// (<see cref="HasForwardTaxLines"/>). That silently excluded two shapes that ARE valid Rule-46 tax invoices: a
    /// <b>zero-rated</b> (0%, LUT/export) service invoice and a <b>wholly-exempt</b> one, neither of which posts any
    /// tax leg. And its safety property (an existing hand-keyed As-Voucher sale types its Output CGST/SGST by hand, as
    /// plain <see cref="EntryLine"/>s with no <see cref="GstLineTax"/>, so it failed the gate) rested on "no OTHER path
    /// currently stamps <c>GstLineTax</c> on a ledger-only Sales voucher" — true of today's code, not a structural
    /// property of the data. Keying on what the user actually DID at posting time fixes both.</para>
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
    /// voucher a pre-slice build produced. Every genuine service invoice foots by construction (the accept path builds
    /// the party leg from exactly those three sums), so this is byte-identical on the real path (ER-13).</para>
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
    /// resolved from the company default at print time, and a live resolve is exactly what the projector refuses to do
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
    /// <c>VoucherPrintProjector.ProjectServiceInvoice</c> adds up (it prints no round-off — the accounting-invoice
    /// accept path computes its tax with <c>applyInvoiceRoundOff: false</c> and posts no round-off leg), so this is a
    /// genuine end-to-end reconciliation of the projection against the GL, not a restatement of it.
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
        projected += PostedCessTotal(voucher).Amount;

        return projected == partyLeg;
    }

    // ================================================================ W0-9: the posted-tax reads both layers share

    /// <summary>One posted (integrated-rate) forward tax group of a voucher — the rate in basis points and the
    /// taxable value and per-head tax the voucher actually POSTED for it.</summary>
    /// <param name="Rate">The integrated rate in basis points (1800 for an 18% supply, intra or inter).</param>
    /// <param name="Taxable">The taxable value the group's legs recorded (deduped across the CGST+SGST pair).</param>
    /// <param name="Cgst">Σ posted Central tax in the group.</param>
    /// <param name="Sgst">Σ posted State tax in the group.</param>
    /// <param name="Igst">Σ posted Integrated tax in the group.</param>
    public readonly record struct PostedRateGroup(
        int Rate, decimal Taxable, decimal Cgst, decimal Sgst, decimal Igst);

    /// <summary>
    /// The per-(integrated rate) posted tax of a voucher, read straight off its <see cref="GstLineTax"/> legs — the
    /// print-side twin of <c>Gstr1.ReadInvoiceRateGroups</c>, with the same head exclusions so the printed breakup and
    /// the filed return can never disagree: the ring-fenced Compensation Cess is not a CGST/SGST/IGST rate row (it
    /// records the SAME taxable value on its own doubled key and would inject a phantom row), and reverse-charge legs
    /// are their own bucket, not forward tax. Within one rate group every leg records the same group taxable, so the
    /// max dedups the intra CGST+SGST pair. Ordered by rate for determinism.
    /// <para><b>W0-9 — moved down from <c>VoucherPrintProjector</c> unchanged.</b> It is a conjunct of
    /// <see cref="IsServiceAccountingInvoice"/>, which <see cref="IsBillOfSupply"/> needs; the projector's service
    /// pass reads the same list, so there is still exactly one implementation.</para>
    /// </summary>
    public static List<PostedRateGroup> ReadPostedRateGroups(Voucher voucher)
    {
        ArgumentNullException.ThrowIfNull(voucher);
        var byRate = new Dictionary<int, (decimal Taxable, decimal Cgst, decimal Sgst, decimal Igst)>();
        foreach (var line in voucher.Lines)
        {
            if (line.Gst is not { } g || g.IsReverseCharge) continue;
            if (g.TaxHead == GstTaxHead.Cess) continue;
            var rate = IntegratedRateOf(g, line.Amount);
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
            .Select(kv => new PostedRateGroup(
                kv.Key, kv.Value.Taxable, kv.Value.Cgst, kv.Value.Sgst, kv.Value.Igst))
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
    public static bool? PostedForwardRouting(Voucher voucher)
    {
        ArgumentNullException.ThrowIfNull(voucher);
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

    /// <summary>The sales value ledger for rate resolution: the posted line ledger carrying a Sales/Purchase GST
    /// block, else the first non-party, non-tax ledger on the voucher.
    /// <para><b>W0-9 — moved down from <c>VoucherPrintProjector</c> unchanged</b>, because
    /// <see cref="IsBillOfSupply"/>'s exempt limb needs it.</para>
    /// <para><b>🔴 W0-10 review (finding #7) — THE STATED REASON HAS CHANGED AND THE SUBTLETY HAS NOT.</b> This note
    /// used to say the ledger choice must match "<c>ProjectInvoice</c>'s rate resolution", or the printed title could
    /// contradict the printed breakup. <b>W0-10 removed that call site entirely</b> — the projector resolves no rate at
    /// all any more; every printed figure is read off the posted legs. The surviving consumer is
    /// <see cref="IsWhollyExemptItemSupply"/>, the exempt limb of <see cref="IsBillOfSupply"/>, which is direction-
    /// neutral and shared with the inward (<see cref="IsInwardBillOfSupply"/>) side — so what must not drift is that
    /// the OUTWARD and INWARD limbs classify one voucher's value ledger the same way. <b>The <c>partyId</c> exclusion
    /// is load-bearing and must not be "simplified" away</b>: without it the party ledger itself can be returned as the
    /// fallback value ledger, and a party carries no <c>SalesPurchaseGst</c>, so the taxability read would silently
    /// answer from the wrong master.</para></summary>
    public static Domain.Ledger? ResolveValueLedger(Company company, Voucher voucher, Guid? partyId)
    {
        ArgumentNullException.ThrowIfNull(company);
        ArgumentNullException.ThrowIfNull(voucher);
        Domain.Ledger? fallback = null;
        foreach (var l in voucher.Lines)
        {
            var led = company.FindLedger(l.LedgerId);
            if (led is null) continue;
            if (led.SalesPurchaseGst is not null) return led;
            if (led.Id != partyId && led.GstClassification is null && fallback is null) fallback = led;
        }
        return fallback;
    }

    /// <summary>One posted reverse-charge tax line in a report window (Phase 9 slice 2; RQ-7).</summary>
    /// <param name="Voucher">The voucher the RCM line was posted on (a Purchase for an inward RCM supply).</param>
    /// <param name="Gst">The line's GST detail (head, rate, taxable value; carries the RCM tag + scheme).</param>
    /// <param name="Amount">The posted tax amount (paisa-exact).</param>
    /// <param name="IsOutputLiability">True ⇒ the RCM Output liability leg (→ GSTR-3B 3.1(d)); false ⇒ the ITC leg.</param>
    /// <param name="Scheme">The ITC bucket for the ITC leg (ImportOfServices → 4A(2), OtherRcm → 4A(3)); <c>null</c> on the liability leg.</param>
    public readonly record struct RcmLine(
        Voucher Voucher, GstLineTax Gst, Money Amount, bool IsOutputLiability, RcmItcScheme? Scheme);

    /// <summary>
    /// Enumerates every posted <b>reverse-charge</b>-tagged tax line in the window <c>[from, to]</c> (Phase 9 slice 2;
    /// RQ-7), a pure projection over the posted lines' <see cref="GstLineTax.IsReverseCharge"/> tag — never a recompute
    /// (ER-9). RCM breaks the 1:1 base-type→direction map (a Purchase yields an Output liability), so this scans <b>all</b>
    /// directions, filtered for cancelled / optional / provisional / post-dated-after-<paramref name="to"/> (via
    /// <see cref="LedgerBalances.CountsAsOf(Voucher, DateOnly, VoucherBaseType?)"/>) and the lower date bound. A line
    /// posting to an <c>IsReverseCharge</c> classification ledger is the output liability (→ 3.1(d)); an RCM-tagged line on
    /// an ordinary Input ledger is the ITC (→ 4A(2)/4A(3)). GST-off companies yield nothing.
    /// </summary>
    public static IEnumerable<RcmLine> RcmLines(Company company, DateOnly from, DateOnly to)
    {
        if (!company.GstEnabled) yield break;

        foreach (var v in company.Vouchers)
        {
            if (v.Date < from) continue;
            var type = company.FindVoucherType(v.TypeId);
            if (type is null) continue;
            if (!LedgerBalances.CountsAsOf(v, to, type.BaseType)) continue; // cancelled/post-dated/date filter
            foreach (var line in v.Lines)
            {
                if (line.Gst is not { IsReverseCharge: true } g) continue;
                var isOutput = company.FindLedger(line.LedgerId)?.GstClassification is { IsReverseCharge: true };
                yield return new RcmLine(v, g, line.Amount, isOutput, g.RcmScheme);
            }
        }
    }
}
