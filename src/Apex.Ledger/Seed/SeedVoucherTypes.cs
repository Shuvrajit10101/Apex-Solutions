using Apex.Ledger.Domain;

namespace Apex.Ledger.Seed;

/// <summary>
/// The 24 predefined voucher types (catalog §4): 16 accounting/inventory core types +
/// 8 additional. Payroll &amp; Job-Work types are inactive until their F11 feature is on
/// (verification §A15).
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
        new("Physical Stock",  VoucherBaseType.PhysicalStock, "F10",     "Phys",  true),
        new("Sales Order",     VoucherBaseType.SalesOrder,    "Ctrl+F8", "SOrd",  true),
        new("Purchase Order",  VoucherBaseType.PurchaseOrder, "Ctrl+F9", "POrd",  true),
        new("Delivery Note",   VoucherBaseType.DeliveryNote,  "Alt+F8",  "DNote", true),
        new("Receipt Note",    VoucherBaseType.ReceiptNote,   "Alt+F9",  "RNote", true),
        new("Rejection Out",   VoucherBaseType.RejectionOut,  "Ctrl+F5", "RejOut", true),
        new("Rejection In",    VoucherBaseType.RejectionIn,   "Ctrl+F6", "RejIn", true),

        // --- 8 additional predefined types ---
        new("Memorandum",         VoucherBaseType.Memorandum,       null, "Memo",   true),
        new("Reversing Journal",  VoucherBaseType.ReversingJournal, null, "Rev Jrnl", true),
        new("Job Work In Order",  VoucherBaseType.JobWorkInOrder,   null, "JWIn",   false),
        new("Material In",        VoucherBaseType.MaterialIn,       null, "MatIn",  false),
        new("Job Work Out Order", VoucherBaseType.JobWorkOutOrder,  null, "JWOut",  false),
        new("Material Out",       VoucherBaseType.MaterialOut,      null, "MatOut", false),
        new("Attendance",         VoucherBaseType.Attendance,       null, "Attd",   false),
        new("Payroll",            VoucherBaseType.Payroll,          "Ctrl+F4", "Pay", false),
    };

    /// <summary>Count guard: exactly 24.</summary>
    public const int Count = 24;

    /// <summary>
    /// Builds the 24 predefined <see cref="VoucherType"/> instances. Each carries its
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
