using System;
using System.Collections.Generic;

namespace Apex.Desktop.ViewModels;

/// <summary>
/// WI-1 — the kind of master an on-the-fly "create" (Alt+C, or the picker's own Create row) should open.
/// <para><see cref="None"/> is the INERT value: a voucher field that references an enum (Dr/Cr side, bill
/// Ref-Type, bank transaction type) or an existing <em>voucher</em> (the §34-CDN original invoice, an
/// outstanding advance) has no creatable master behind it, so Alt+C must do NOTHING there rather than open a
/// wrong screen. Inertness is the default: a field is inert unless it is explicitly tagged in the XAML.</para>
/// </summary>
public enum MasterCreateKind
{
    /// <summary>No creatable master behind this field — Alt+C is inert.</summary>
    None = 0,
    Ledger,
    AccountGroup,
    CostCategory,
    CostCentre,
    StockItem,
    StockGroup,
    StockCategory,
    Unit,
    Godown,
}

/// <summary>
/// WI-1 — the FIELD → MASTER-KIND dispatch table, in ONE place. A voucher-entry picker is tagged in the XAML
/// with a stable field id (see <c>Views.CreateField.Master</c>); this maps that id to the master screen Alt+C
/// opens for it. Keeping the table here (not in the view) means the dispatch is a plain unit-testable lookup
/// rather than something only a rendered window can answer.
/// <para>The corpus grounds the feature on the Ledger field ("Alt+C … in place of the Ledger field or select
/// Create option under List of Ledger Accounts"; Study Guide ~2046–47); the remaining rows extend the same
/// affordance to the other master-backed pickers on the voucher-entry screens.</para>
/// </summary>
public static class MasterCreateFields
{
    /// <summary>A party/ledger picker (plain Dr/Cr line, invoice party, stock leg, additional-cost ledger).</summary>
    public const string Ledger = "Ledger";

    /// <summary>The item-invoice / stock-voucher party picker (a <c>PartyOption</c> wrapper around a ledger).</summary>
    public const string Party = "Party";

    /// <summary>The item-invoice value-leg (Sales/Purchases accounts) ledger picker.</summary>
    public const string StockLedger = "StockLedger";

    public const string CostCategory = "CostCategory";
    public const string CostCentre = "CostCentre";
    public const string StockItem = "StockItem";
    public const string Godown = "Godown";
    public const string Unit = "Unit";
    public const string StockGroup = "StockGroup";
    public const string StockCategory = "StockCategory";
    public const string AccountGroup = "AccountGroup";

    private static readonly IReadOnlyDictionary<string, MasterCreateKind> Map =
        new Dictionary<string, MasterCreateKind>(StringComparer.OrdinalIgnoreCase)
        {
            // Every ledger-shaped field creates a LEDGER — the party, the stock (Sales/Purchases) leg and the
            // additional-cost ledger are all ledgers, differing only in which group they sit under.
            [Ledger] = MasterCreateKind.Ledger,
            [Party] = MasterCreateKind.Ledger,
            [StockLedger] = MasterCreateKind.Ledger,
            [CostCategory] = MasterCreateKind.CostCategory,
            [CostCentre] = MasterCreateKind.CostCentre,
            [StockItem] = MasterCreateKind.StockItem,
            [Godown] = MasterCreateKind.Godown,
            [Unit] = MasterCreateKind.Unit,
            [StockGroup] = MasterCreateKind.StockGroup,
            [StockCategory] = MasterCreateKind.StockCategory,
            // WI-7 shipped the accounting-Group master in S2; Alt+C on an Under-group field REUSES it.
            [AccountGroup] = MasterCreateKind.AccountGroup,
        };

    /// <summary>
    /// The master kind for a tagged field id — <see cref="MasterCreateKind.None"/> for an unknown/blank id, so
    /// an untagged (enum / voucher-reference) field is inert by default rather than opening a wrong screen.
    /// </summary>
    public static MasterCreateKind KindFor(string? fieldId)
        => fieldId is not null && Map.TryGetValue(fieldId, out var kind) ? kind : MasterCreateKind.None;

    /// <summary>
    /// The canonical field id for a kind — the inverse of <see cref="KindFor"/>. Used by the entry points that
    /// have a KIND but no focused control (the Alt+C button bar, a picker's pinned Create row), so they run the
    /// very same dispatch as the key rather than a parallel path that could drift from it.
    /// </summary>
    public static string FieldIdFor(MasterCreateKind kind) => kind switch
    {
        MasterCreateKind.Ledger => Ledger,
        MasterCreateKind.AccountGroup => AccountGroup,
        MasterCreateKind.CostCategory => CostCategory,
        MasterCreateKind.CostCentre => CostCentre,
        MasterCreateKind.StockItem => StockItem,
        MasterCreateKind.StockGroup => StockGroup,
        MasterCreateKind.StockCategory => StockCategory,
        MasterCreateKind.Unit => Unit,
        MasterCreateKind.Godown => Godown,
        _ => "",
    };

    /// <summary>The human noun for the create screen a kind opens (used in the button-bar label).</summary>
    public static string NounFor(MasterCreateKind kind) => kind switch
    {
        MasterCreateKind.Ledger => "Ledger",
        MasterCreateKind.AccountGroup => "Group",
        MasterCreateKind.CostCategory => "Cost Category",
        MasterCreateKind.CostCentre => "Cost Centre",
        MasterCreateKind.StockItem => "Stock Item",
        MasterCreateKind.StockGroup => "Stock Group",
        MasterCreateKind.StockCategory => "Stock Category",
        MasterCreateKind.Unit => "Unit",
        MasterCreateKind.Godown => "Godown",
        _ => "",
    };
}
