namespace Apex.Ledger.Domain;

/// <summary>
/// The vendor's <b>"Type of Dealer"</b> on the company VAT configuration and on a party ledger's VAT Details
/// (census 15.1; schema v59).
///
/// <para><b>R7 — ATTESTED, AND DELIBERATELY ONLY TWO MEMBERS.</b> The vendor page
/// <c>help.tallysolutions.com/tally-prime/getting-started/configuring-vat-masters-tally/</c> carries the field
/// label <i>"Type of Dealer"</i> on the Ledger Master, and
/// <c>help.tallysolutions.com/tally-prime/vat-masters/india-vat-party-ledger-tally/</c> reaches it as
/// <i>"Select the Type of Dealer from the Type of Dealer list"</i>. The only two VALUES either page attests are
/// <b>Regular</b> and <b>Composite</b> — the vendor states that the <i>"VAT TIN (Composition)"</i> field "will
/// be skipped when selecting the Type of Dealer as Regular, and can only be entered when the Type of Dealer is
/// selected as Composite".</para>
///
/// <para>🔴 <b>"Unregistered" and "Consumer" are NOT members, and their absence is a decision, not an
/// oversight.</b> They are the values one would expect from the reference product by analogy with its GST
/// registration types, but <b>no vendor page reached for this slice publishes the list</b>. Inventing the two
/// missing members would put dealer categories into a shipped statutory screen on no source at all — the exact
/// fault this project has already had to strip out of shipped tax code. If a vendor page later publishes the
/// full list, extend this enum; until then the product offers what is attested.</para>
/// </summary>
public enum VatDealerType
{
    /// <summary>Vendor value <i>"Regular"</i>. The dealer files ordinary VAT returns and takes input credit.</summary>
    Regular = 0,

    /// <summary>
    /// Vendor value <i>"Composite"</i> — the dealer on the State's VAT composition scheme.
    ///
    /// <para>⚠️ <b>This member is the ONLY part of census row 15.4 (VAT Composition) that ships</b>, and it ships
    /// because it is the one piece of that row a vendor page attests: the dealer TYPE. The composition SCHEME
    /// itself — its rate, its base, its return — reached no vendor page in this slice's sourcing, so no rate and
    /// no return is computed for a Composite dealer anywhere in this build. Recording the dealer type is a fact
    /// about the party; computing a composition tax from an unsourced rate would be a fabrication.</para>
    /// </summary>
    Composite = 1,
}

/// <summary>
/// The VAT return periodicity a company elects (census 15.1; schema v59).
///
/// <para><b>R7 — ATTESTED.</b>
/// <c>help.tallysolutions.com/tally-prime/vat-masters/india-vat-configuring-accounts-only-company-tally/</c>
/// describes the Company VAT Details screen as carrying a "Periodicity selection (<b>Monthly</b> or
/// <b>Quarterly</b> for e-VAT Annexures)". Those two are the whole enum; nothing else is attested.</para>
/// </summary>
public enum VatReturnPeriodicity
{
    /// <summary>Monthly.</summary>
    Monthly = 0,

    /// <summary>Quarterly.</summary>
    Quarterly = 1,
}

/// <summary>
/// The CST declaration forms a dealer receives from, or issues to, the other side of an inter-State transaction
/// (census 15.6; schema v59).
///
/// <para><b>R7 — ATTESTED, VERBATIM, AND THE DESCRIPTIONS ARE THE VENDOR'S OWN.</b> Every member and every
/// summary below is quoted from <c>help.tallysolutions.com/tally-prime/reports/vat-declaration-forms-tally/</c>,
/// which enumerates exactly these seven. The order is that page's order.</para>
///
/// <para>🔴 <b>These forms are alive only because their goods are.</b> The CST Act 1956 survives for the goods
/// GST never absorbed — see <see cref="NonGstGoodsClass"/>. A declaration form captured against ordinary GST
/// goods would be meaningless, which is why the Forms Receivable / Forms Issuable reports scope themselves the
/// same way every other screen in this area does.</para>
/// </summary>
public enum CstDeclarationForm
{
    /// <summary>Vendor: <i>"Basic Form used by the Registered Dealers in the course of Interstate trade or
    /// commerce"</i>. The form the concessional inter-State rate is claimed against.</summary>
    FormC = 0,

    /// <summary>Vendor: <i>"used for making subsequent sales in the course of interstate sale/purchase by the
    /// first or original purchaser"</i>.</summary>
    FormE1 = 1,

    /// <summary>Vendor: <i>"used for claiming the exemption from payment of CST"</i> on goods sold while in
    /// transit.</summary>
    FormE2 = 2,

    /// <summary>Vendor: <i>"used for claiming exemptions on the interstate movement of goods as Stock/Branch
    /// Transfers"</i>.</summary>
    FormF = 3,

    /// <summary>Vendor: <i>"used by the seller for claiming the exemption on making penultimate sales
    /// (immediately preceding sale to exports)"</i>.</summary>
    FormH = 4,

    /// <summary>Vendor: <i>"used for claiming the exemption from CST on the sales made to any Special Economic
    /// Zone (SEZ)"</i>.</summary>
    FormI = 5,

    /// <summary>Vendor: <i>"used for claiming the exemption of CST in case of Interstate sales made to any
    /// United Nations, Diplomatic Missions"</i>.</summary>
    FormJ = 6,
}

/// <summary>Operator-facing captions and the vendor's one-line description for each
/// <see cref="CstDeclarationForm"/>. One place, so the picker, the two reports and the tests agree.</summary>
public static class CstDeclarationForms
{
    /// <summary>The seven forms in the vendor page's order — what a picker binds to.</summary>
    public static IReadOnlyList<CstDeclarationForm> All { get; } = new[]
    {
        CstDeclarationForm.FormC,
        CstDeclarationForm.FormE1,
        CstDeclarationForm.FormE2,
        CstDeclarationForm.FormF,
        CstDeclarationForm.FormH,
        CstDeclarationForm.FormI,
        CstDeclarationForm.FormJ,
    };

    /// <summary>The short caption shown in the Form Type column of the two Declaration Forms reports.</summary>
    public static string Caption(CstDeclarationForm form) => form switch
    {
        CstDeclarationForm.FormC => "Form C",
        CstDeclarationForm.FormE1 => "Form E1",
        CstDeclarationForm.FormE2 => "Form E2",
        CstDeclarationForm.FormF => "Form F",
        CstDeclarationForm.FormH => "Form H",
        CstDeclarationForm.FormI => "Form I",
        CstDeclarationForm.FormJ => "Form J",
        _ => form.ToString(),
    };

    /// <summary>The vendor's own one-line description, shown beside the caption in the form picker so the
    /// operator picks the right form rather than the first letter they recognise.</summary>
    public static string Description(CstDeclarationForm form) => form switch
    {
        CstDeclarationForm.FormC => "Basic form used by registered dealers in the course of inter-State trade or commerce",
        CstDeclarationForm.FormE1 => "Subsequent sales in the course of inter-State sale/purchase by the first purchaser",
        CstDeclarationForm.FormE2 => "Exemption from payment of CST on goods sold while in transit",
        CstDeclarationForm.FormF => "Exemption on the inter-State movement of goods as stock/branch transfers",
        CstDeclarationForm.FormH => "Exemption on penultimate sales, immediately preceding a sale to exports",
        CstDeclarationForm.FormI => "Exemption from CST on sales made to a Special Economic Zone (SEZ)",
        CstDeclarationForm.FormJ => "Exemption from CST on inter-State sales to the United Nations or a diplomatic mission",
        _ => string.Empty,
    };
}
