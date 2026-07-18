using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Apex.Desktop.Views;

/// <summary>
/// WI-1 — marks a voucher-entry picker as backed by a creatable MASTER, so Alt+C ("create on the fly") knows
/// WHICH creation screen to open for the field the operator is standing in.
/// <para>The value is a stable field id from <see cref="ViewModels.MasterCreateFields"/>; the id → master-kind
/// dispatch lives in the view model so it stays a plain unit-testable lookup. An UNTAGGED control carries no
/// id, which resolves to <see cref="ViewModels.MasterCreateKind.None"/> — that is how enum pickers (Dr/Cr,
/// Ref-Type, bank transaction type) and voucher-reference pickers (the §34-CDN original invoice, an
/// outstanding advance) stay INERT under Alt+C instead of opening a wrong screen.</para>
/// </summary>
public static class CreateField
{
    /// <summary>The field id (e.g. "Ledger", "StockItem") this picker creates a master for.</summary>
    public static readonly AttachedProperty<string?> MasterProperty =
        AvaloniaProperty.RegisterAttached<Control, string?>("Master", typeof(CreateField));

    public static string? GetMaster(Control control) => control.GetValue(MasterProperty);

    public static void SetMaster(Control control, string? value) => control.SetValue(MasterProperty, value);

    /// <summary>
    /// The field id of the tagged picker the key event came from: walks the control tree UP from the key
    /// source to the first element carrying a <see cref="MasterProperty"/> — so Alt+C targets the field the
    /// operator is actually standing in, not a screen-wide default. Null when the key came from outside any
    /// tagged field (Alt+C is then inert on the voucher).
    /// <para>Also returns the tagged control's <see cref="StyledElement.DataContext"/>: that object is the
    /// CALLER the newly-created master must be written back into (the specific line/row view model), which is
    /// what makes the round-trip land in the right row rather than the first one.</para>
    /// </summary>
    public static (string? FieldId, object? Caller) Focused(RoutedEventArgs e)
    {
        for (var c = e.Source as StyledElement; c is not null; c = c.Parent)
        {
            if (c is Control control && GetMaster(control) is { Length: > 0 } id)
                return (id, control.DataContext);
        }
        return (null, null);
    }
}
