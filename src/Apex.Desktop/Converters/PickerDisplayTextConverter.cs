using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using Avalonia.Data.Converters;

namespace Apex.Desktop.Converters;

/// <summary>
/// WI-2 — resolves the <b>search/display text</b> for one item in a domain-bound picker.
/// <para>
/// THE BUG THIS FIXES (measured, not assumed). Avalonia's type-to-jump derives an item's search text from
/// <c>TextSearch.Text</c>, falling back to <c>item.ToString()</c>. A picker's <c>ItemTemplate</c> does NOT
/// participate — it only paints the row. Every Apex domain entity (<c>Ledger</c>, <c>StockItem</c>,
/// <c>Godown</c>, <c>Unit</c>, <c>Employee</c>, <c>Group</c>, …) is a plain POCO with no <c>ToString</c>
/// override, so every item in a ledger picker reported the SAME search text — the type name
/// <c>"Apex.Ledger.Domain.Ledger"</c>. Two consequences, both observed in a headless probe:
/// </para>
/// <list type="bullet">
/// <item>Typing <b>"A"</b> prefix-matches <i>"<b>A</b>pex.Ledger.Domain.Ledger"</i> for EVERY item, so the
/// scan from index 0 selects <b>whatever sits first in the list</b>. With the list ordered
/// <c>Zenith Traders | Aarti Steel | Amar Textiles</c>, typing "A" for "Aarti Steel" selected
/// <b>Zenith Traders</b> — a wrong-ledger selection that posts money to the wrong account.</item>
/// <item>Typing any other letter matches nothing, so type-ahead silently does nothing at all.</item>
/// </list>
/// <para>
/// The fix is deliberately <b>UI-side</b>: the domain stays free of presentation concerns (no
/// <c>ToString</c> override on an engine entity), and this converter is applied once, via a style, to
/// every picker container. Resolution order: an explicit case for each known domain entity, then a cached
/// reflected <c>Name</c> property (so a picker over a type not listed here still searches by name rather
/// than regressing to the type name), then <c>ToString()</c> — which keeps enum pickers (<c>DrCr</c>,
/// <c>BankTransactionType</c>) behaving exactly as they do today.
/// </para>
/// <para>
/// Ledgers additionally append their <c>Alias</c> when present, so two parties sharing a visible prefix stay
/// distinguishable in the list. Note the limit this does NOT overcome: Avalonia's text search is a prefix
/// match over ONE string per item, so search follows the NAME — an alias that is not also a prefix of the
/// name will not jump to the row. Alias-prefix search would need a custom search adapter and is out of scope
/// here.
/// </para>
/// </summary>
public sealed class PickerDisplayTextConverter : IValueConverter
{
    public static readonly PickerDisplayTextConverter Instance = new();

    /// <summary>Cache of the reflected public <c>Name</c> getter per item type (null = no such property).</summary>
    private static readonly ConcurrentDictionary<Type, PropertyInfo?> NameProperties = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Resolve(value);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    /// <summary>
    /// The search/display text for <paramref name="item"/>. Public so a plain unit test can assert the
    /// mapping without standing up a window.
    /// </summary>
    public static string Resolve(object? item)
    {
        switch (item)
        {
            case null:
                return string.Empty;

            case string s:
                return s;

            // A ledger searches by name, plus its alias when it has one: the operator may know a party by
            // either, and the alias is what keeps two same-prefix parties apart in the list.
            case Apex.Ledger.Domain.Ledger ledger:
                return string.IsNullOrWhiteSpace(ledger.Alias)
                    ? ledger.Name
                    : $"{ledger.Name} ({ledger.Alias})";
        }

        // Any other domain entity with a Name (StockItem, Godown, Unit, Employee, Group, …). Reflection is
        // cached per type and only ever runs on a keystroke/paint, never in a posting path.
        var type = item.GetType();
        var nameProperty = NameProperties.GetOrAdd(type, static t =>
        {
            var property = t.GetProperty("Name", BindingFlags.Public | BindingFlags.Instance);
            return property is not null && property.PropertyType == typeof(string) && property.CanRead
                ? property
                : null;
        });

        if (nameProperty?.GetValue(item) is string name && !string.IsNullOrWhiteSpace(name))
            return name;

        // Enums and value types keep their existing (already-correct) ToString behaviour.
        return item.ToString() ?? string.Empty;
    }
}
