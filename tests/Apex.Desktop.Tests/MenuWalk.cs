using System.Linq;
using Apex.Desktop.ViewModels;

namespace Apex.Desktop.Tests;

/// <summary>
/// T2-169: the one bounded way for a test to arrow the highlight to a menu label. <see cref="MainWindowViewModel.MoveDown"/>
/// WRAPS, so the old inline shape <c>while (vm.Menu[vm.SelectedIndex].Label != label) vm.MoveDown();</c> spun for ever
/// the moment a label was renamed or dropped from the column, hanging the whole gate instead of failing one test.
/// This walk takes at most <c>Menu.Count</c> steps (one full lap of the column) and then fails NAMING the label and
/// listing what the column does hold.
/// </summary>
internal static class MenuWalk
{
    /// <summary>Arrows Down until the highlighted row's label is <paramref name="label"/>; fails after one lap.</summary>
    public static void ArrowTo(MainWindowViewModel vm, string label)
    {
        var lap = vm.Menu.Count;
        for (var steps = 0; vm.Menu[vm.SelectedIndex].Label != label; steps++)
        {
            if (steps >= lap)
            {
                var held = string.Join(" | ", vm.Menu.Where(m => m.IsSelectable).Select(m => m.Label));
                Assert.Fail($"'{label}' is not reachable by arrowing through the open menu column " +
                            $"({lap} rows, one full lap walked). The column holds: {held}");
            }
            vm.MoveDown();
        }
    }

    /// <summary><see cref="ArrowTo"/> then Enter.</summary>
    public static void ArrowToAndActivate(MainWindowViewModel vm, string label)
    {
        ArrowTo(vm, label);
        vm.ActivateSelected();
    }
}
