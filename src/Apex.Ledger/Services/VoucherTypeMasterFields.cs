using Apex.Ledger.Domain;

namespace Apex.Ledger.Services;

/// <summary>
/// v67 (census row <b>2.4</b>) — the <b>eleven</b> remaining attested Voucher Type fields, carried as ONE
/// value so <see cref="VoucherTypeService.Create"/> and <see cref="VoucherTypeService.Alter"/> do not grow to
/// eighteen positional parameters apiece.
///
/// <para>🔴 <b>EVERY CAPTION BELOW IS VERBATIM</b> from
/// <c>help.tallysolutions.com/voucher-types-tally/</c> ("How to Create, Configure &amp; Manage Voucher Types in
/// TallyPrime"), opened BY CONTENT on 2026-10-16. Before v67 every one of these was a <b>zero-hit search over
/// <c>src/</c></b> — which is precisely the list census row 2.4's cell names as missing.</para>
///
/// <para>🔴 <b>EVERY DEFAULT IS THE BEHAVIOUR THIS BUILD ALREADY HAD</b> — <c>false</c> for the five flags and
/// <c>null</c> for the six text/id fields — so <see cref="Default"/> creates a voucher type byte-identical to a
/// pre-v67 one, and a caller that does not care passes nothing (ER-13).</para>
///
/// <para>⚠️ <b>WHAT v67 DOES AND DOES NOT DO WITH THESE, SAID PLAINLY SO NO READER MISTAKES A PERSISTED FLAG
/// FOR A WORKING FEATURE.</b> v67 captures, stores and round-trips the operator's ELECTION on each field, and
/// surfaces it on the Voucher Type master. It does <b>not</b> add the voucher-entry effective-date field, does
/// not open a new voucher as Optional, does not change any accounting allocation, does not send a WhatsApp
/// message, does not generate a payment link or QR code, and does not change what the print path emits. Those
/// are each their own slice, and claiming otherwise would be the "overstated closure" failure this project has
/// already shipped three times.</para>
/// </summary>
/// <param name="UseEffectiveDates">Vendor: <i>"Use effective dates for vouchers"</i>.</param>
/// <param name="OptionalByDefault">Vendor: <i>"Make this voucher type as 'Optional' by default"</i>.</param>
/// <param name="DefaultAccountingAllocations">Vendor: <i>"Enable default accounting allocations"</i>.</param>
/// <param name="WhatsAppAfterSaving">Vendor: <i>"WhatsApp voucher after saving"</i>. 🔴 NOTHING IS SENT — this
/// product's WhatsApp surface is an offline document/link builder (census 14.10).</param>
/// <param name="GeneratePaymentLinkAfterSaving">Vendor: <i>"Generate Payment link/QR code after saving
/// voucher"</i>. 🔴 No link and no QR code is generated anywhere in this product.</param>
/// <param name="DefaultPrintTemplate">Vendor: <i>"Default template to print"</i>.</param>
/// <param name="DefaultPrintTitle">Vendor: <i>"Default title to print"</i>.</param>
/// <param name="DefaultBankLedgerId">Vendor: <i>"Default bank"</i> — the bank <see cref="Domain.Ledger"/>.</param>
/// <param name="DefaultJurisdiction">Vendor: <i>"Default jurisdiction"</i>.</param>
/// <param name="PrintDeclaration">Vendor: <i>"Set/alter declaration"</i>.</param>
/// <param name="PrintTermsAndConditions">Vendor: <i>"Set/Alter Terms and Conditions"</i>.</param>
public sealed record VoucherTypeMasterFields(
    bool UseEffectiveDates = false,
    bool OptionalByDefault = false,
    bool DefaultAccountingAllocations = false,
    bool WhatsAppAfterSaving = false,
    bool GeneratePaymentLinkAfterSaving = false,
    string? DefaultPrintTemplate = null,
    string? DefaultPrintTitle = null,
    Guid? DefaultBankLedgerId = null,
    string? DefaultJurisdiction = null,
    string? PrintDeclaration = null,
    string? PrintTermsAndConditions = null)
{
    /// <summary>Every field on the behaviour this build had before v67. Shared instance, because the record is
    /// immutable and every caller that passes nothing means exactly this.</summary>
    public static readonly VoucherTypeMasterFields Default = new();

    /// <summary>Reads the eleven fields off an existing type, so an alter that changes one thing can write the
    /// other ten back unchanged.</summary>
    public static VoucherTypeMasterFields From(VoucherType type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return new VoucherTypeMasterFields(
            type.UseEffectiveDates,
            type.OptionalByDefault,
            type.DefaultAccountingAllocations,
            type.WhatsAppAfterSaving,
            type.GeneratePaymentLinkAfterSaving,
            type.DefaultPrintTemplate,
            type.DefaultPrintTitle,
            type.DefaultBankLedgerId,
            type.DefaultJurisdiction,
            type.PrintDeclaration,
            type.PrintTermsAndConditions);
    }

    /// <summary>
    /// Writes all eleven onto <paramref name="type"/>. Every text field is trimmed, and <b>blank clears
    /// it</b> — an empty box on the master must mean "no default", not "keep the old one", which is how every
    /// other optional text field on this product reads a cleared value.
    /// </summary>
    public void ApplyTo(VoucherType type)
    {
        ArgumentNullException.ThrowIfNull(type);
        type.UseEffectiveDates = UseEffectiveDates;
        type.OptionalByDefault = OptionalByDefault;
        type.DefaultAccountingAllocations = DefaultAccountingAllocations;
        type.WhatsAppAfterSaving = WhatsAppAfterSaving;
        type.GeneratePaymentLinkAfterSaving = GeneratePaymentLinkAfterSaving;
        type.DefaultPrintTemplate = Blank(DefaultPrintTemplate);
        type.DefaultPrintTitle = Blank(DefaultPrintTitle);
        type.DefaultBankLedgerId = DefaultBankLedgerId;
        type.DefaultJurisdiction = Blank(DefaultJurisdiction);
        type.PrintDeclaration = Blank(PrintDeclaration);
        type.PrintTermsAndConditions = Blank(PrintTermsAndConditions);
    }

    private static string? Blank(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
