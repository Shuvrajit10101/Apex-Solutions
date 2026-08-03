using Apex.Ledger.Domain;

namespace Apex.Ledger.Seed;

/// <summary>
/// The 23 predefined voucher types (catalog §4): 16 accounting/inventory core types +
/// 7 additional. Payroll &amp; Job-Work types are inactive until their F11 feature is on
/// (verification §A15). The catalog lists 24 — the 24th, <b>Attendance</b>, is deliberately NOT seeded here
/// because nothing in the product posts a voucher of that kind (decision D24 option B; see the note beside the
/// Payroll row).
/// </summary>
public static class SeedVoucherTypes
{
    private readonly record struct Def(
        string Name,
        VoucherBaseType BaseType,
        string? Shortcut,
        string? Abbreviation,
        bool IsActive);

    private static readonly Def[] Definitions =
    {
        // --- 16 accounting/inventory core types ---
        new("Contra",          VoucherBaseType.Contra,        "F4",      "Cntra", true),
        new("Payment",         VoucherBaseType.Payment,       "F5",      "Pymt",  true),
        new("Receipt",         VoucherBaseType.Receipt,       "F6",      "Rcpt",  true),
        new("Journal",         VoucherBaseType.Journal,       "F7",      "Jrnl",  true),
        new("Sales",           VoucherBaseType.Sales,         "F8",      "Sale",  true),
        new("Purchase",        VoucherBaseType.Purchase,      "F9",      "Purc",  true),
        new("Credit Note",     VoucherBaseType.CreditNote,    "Alt+F6",  "C/Note", true),
        new("Debit Note",      VoucherBaseType.DebitNote,     "Alt+F5",  "D/Note", true),
        new("Stock Journal",   VoucherBaseType.StockJournal,  "Alt+F7",  "Stk Jrnl", true),
        // Physical Stock is Ctrl+F7 — TallyPrime's official keyboard-shortcut reference gives
        // "To open Physical Stock | Ctrl+F7" and reserves F10 for the voucher/master list. This seed said
        // "F10", which in this app opens the Other Vouchers menu, so the type advertised a key that did
        // something else while Ctrl+F7 was bound to nothing (decision X1).
        // NOTE FOR EXISTING COMPANIES: this string is PERSISTED per company (voucher_types.default_shortcut),
        // so a company created before this change still carries "F10". The Ctrl+F7 BINDING works for it all the
        // same (the key handler routes by base kind, not by this string), and the stale value is repaired on the
        // way in by VoucherTypeResolver.RepairSupersededSeedShortcuts (called from CompanyStorage.Load) — which
        // is why no schema version was cut for it. Add a row THERE, not only here, whenever a seeded shortcut
        // changes again: the Day-Book Alt+A picker renders this string verbatim, and "F10" is a LIVE key in this
        // app (it opens the Other Vouchers menu), so a stale value advertises a key that opens another screen.
        new("Physical Stock",  VoucherBaseType.PhysicalStock, "Ctrl+F7", "Phys",  true),
        new("Sales Order",     VoucherBaseType.SalesOrder,    "Ctrl+F8", "SOrd",  true),
        new("Purchase Order",  VoucherBaseType.PurchaseOrder, "Ctrl+F9", "POrd",  true),
        new("Delivery Note",   VoucherBaseType.DeliveryNote,  "Alt+F8",  "DNote", true),
        new("Receipt Note",    VoucherBaseType.ReceiptNote,   "Alt+F9",  "RNote", true),
        new("Rejection Out",   VoucherBaseType.RejectionOut,  "Ctrl+F5", "RejOut", true),
        new("Rejection In",    VoucherBaseType.RejectionIn,   "Ctrl+F6", "RejIn", true),

        // --- 7 additional predefined types ---
        new("Memorandum",         VoucherBaseType.Memorandum,       null, "Memo",   true),
        new("Reversing Journal",  VoucherBaseType.ReversingJournal, null, "Rev Jrnl", true),
        new("Job Work In Order",  VoucherBaseType.JobWorkInOrder,   null, "JWIn",   false),
        new("Material In",        VoucherBaseType.MaterialIn,       null, "MatIn",  false),
        new("Job Work Out Order", VoucherBaseType.JobWorkOutOrder,  null, "JWOut",  false),
        new("Material Out",       VoucherBaseType.MaterialOut,      null, "MatOut", false),
        // NO "Attendance" ROW. It was dead master data: nothing in the product ever posted a Voucher of base kind
        // Attendance — the Attendance / Production screen writes AttendanceEntry rows through
        // PayrollAttendanceService and needs no voucher type at all — so the row existed only to prop up a
        // "24 of 24 predefined types" claim that was not true (decision D24 option B).
        // The ENUM MEMBER VoucherBaseType.Attendance STAYS, and must: voucher_types.base_type is persisted as the
        // enum ORDINAL, so deleting the member would renumber Payroll from 22 to 21 and every existing company's
        // stored Payroll type would load as an Attendance type. (The gap-decisions doc's option B says "remove
        // the dead seed row AND the enum member" — the second half is not safe, see the stream report.)
        new("Payroll",            VoucherBaseType.Payroll,          "Ctrl+F4", "Pay", false),
    };

    /// <summary>Count guard: exactly 23 (was 24 — the dead Attendance row is gone; see above).</summary>
    public const int Count = 23;

    /// <summary>
    /// Builds the 23 predefined <see cref="VoucherType"/> instances. Each carries its
    /// <see cref="VoucherType.AffectsAccounts"/> / <see cref="VoucherType.AffectsStock"/> effect flags,
    /// stamped from the canonical <see cref="VoucherEffects"/> classification of its base kind (catalog §10;
    /// phase3-inventory-requirements §2.2): PO/SO affect neither; GRN/Rejection-In are stock-inward;
    /// Delivery/Rejection-Out are stock-outward; Stock Journal transfers; Physical Stock adjusts; the
    /// accounting kinds affect accounts.
    /// </summary>
    public static IReadOnlyList<VoucherType> Build()
    {
        var result = new List<VoucherType>(Definitions.Length);
        foreach (var d in Definitions)
        {
            result.Add(new VoucherType(
                Guid.NewGuid(),
                d.Name,
                d.BaseType,
                NumberingMethod.Automatic,
                d.Shortcut,
                d.Abbreviation,
                d.IsActive,
                isPredefined: true,
                affectsAccounts: VoucherEffects.AffectsAccounts(d.BaseType),
                affectsStock: VoucherEffects.AffectsStock(d.BaseType)));
        }

        if (result.Count != Count)
            throw new InvalidOperationException($"Seed produced {result.Count} voucher types; expected {Count}.");

        return result;
    }
}
