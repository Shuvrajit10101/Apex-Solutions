using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Apex.Desktop.Converters;

namespace Apex.Desktop.Views;

/// <summary>
/// WI-2 — teaches EVERY <see cref="ComboBox"/> in the app to search by the item's display text instead of
/// its <c>ToString()</c>.
/// <para>
/// WHY A CLASS HANDLER AND NOT A STYLE. Four mechanisms were measured in a headless probe; only this one
/// works:
/// </para>
/// <list type="number">
/// <item><c>ItemTemplate</c> — paints the row only; type-to-jump ignores it entirely.</item>
/// <item>A <c>Style</c> on <c>ComboBoxItem</c> setting <c>TextSearch.Text</c> — <b>no effect</b>: the
/// ComboBox resolves search text from the ITEM, never from the generated container.</item>
/// <item>A <c>Style</c> on <c>ComboBox</c> setting <c>TextSearch.TextBinding</c> — <b>never applies</b>: the
/// property's value is itself a <see cref="BindingBase"/>, so a setter treats it as a binding to evaluate
/// rather than the literal object to store (<c>TextSearch.GetTextBinding</c> stayed null).</item>
/// <item>Setting <c>TextSearch.TextBinding</c> directly on the instance — <b>works</b>. Registering it as a
/// class handler on <c>ItemsSourceProperty</c> applies it to every ComboBox the app will ever create, from
/// one place, with no per-call-site XAML edit across the ~380 pickers (and it re-applies if a picker's
/// ItemsSource is swapped at runtime).</item>
/// </list>
/// <para>
/// Applying it unconditionally is safe: <see cref="PickerDisplayTextConverter"/> falls back to
/// <c>ToString()</c>, so enum-backed pickers (Dr/Cr, bank transaction type, …) keep the exact behaviour
/// they have today, while entity-backed pickers gain name search.
/// </para>
/// </summary>
public static class PickerTextSearch
{
    private static bool _registered;

    /// <summary>
    /// Registers the app-wide search-text binding. Idempotent, so tests that boot the app more than once in
    /// a process do not stack duplicate handlers.
    /// </summary>
    public static void Register()
    {
        if (_registered) return;
        _registered = true;

        ComboBox.ItemsSourceProperty.Changed.AddClassHandler<ComboBox>(
            (comboBox, _) => TextSearch.SetTextBinding(comboBox, new Binding(".")
            {
                Converter = PickerDisplayTextConverter.Instance,
            }));
    }
}
