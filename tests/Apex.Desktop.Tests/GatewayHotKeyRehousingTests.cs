using System;
using System.Linq;
using Apex.Desktop.ViewModels;
using Xunit;

namespace Apex.Desktop.Tests;

/// <summary>
/// WI-9 follow-on — <see cref="GatewayColumn.AssignHotKeys"/> must not starve a row of its accelerator merely
/// because the row was ADDED LATE.
///
/// <para>🔴 <b>The defect these tests were written against, measured on the real Gateway.</b> Adding the
/// "Dashboard" group under the Reports header left it with no hotkey at all, and
/// <c>MenuHotKeyAndAcceptTests.Every_gateway_row_gets_a_unique_hotkey_drawn_from_its_own_label</c> went red
/// with <c>row 'Dashboard' got no hotkey</c>. Every one of D/A/S/H/B/R was already taken — but each holder had
/// a spare letter of its own it could have moved to. The exhaustion was an artefact of the greedy claim
/// ORDER, not a real shortage, so the honest fix is to re-house the holders rather than to rename the row or
/// to lower the assertion.</para>
///
/// <para>These tests drive <see cref="GatewayColumn"/> directly rather than through the window, because the
/// property under test is a pure function of (row order, labels) and nothing about it needs a realised
/// visual tree. The reachability of the Dashboard row itself is locked separately, on the real window, in
/// <c>DashboardReachabilityTests</c>.</para>
/// </summary>
public class GatewayHotKeyRehousingTests
{
    private static MenuItemViewModel Row(string label) =>
        new(label, () => { }, string.Empty, isSubItem: true, kind: MenuItemKind.Page);

    private static GatewayColumn Column(params string[] labels)
    {
        var col = new GatewayColumn("Test");
        foreach (var l in labels) col.Add(Row(l));
        col.AssignHotKeys();
        return col;
    }

    private static char? KeyOf(GatewayColumn col, string label) =>
        col.Items.First(i => i.Label == label).HotKey;

    /// <summary>
    /// 🔴 <b>THE STARVING COLUMN — the whole point of this file, and it is calibrated.</b> Under the greedy
    /// pass alone, each of the first six rows claims its own initial (D, A, S, H, B, R), and "Dashboard" —
    /// added last — then finds every letter of its label gone: D, A, S, H, B and R are all held, and O is
    /// reserved. It comes away with nothing.
    ///
    /// <para>But the shortage is not real. "Day Book" can sit on A, and "Alter Company" on L, which frees D
    /// for "Dashboard" — a valid assignment serving ALL SEVEN rows exists, and the rescue pass has to find it.
    /// <b>A smaller column does not test this.</b> The first draft of this file used just
    /// <c>("Day Book", "Dashboard")</c>, where A is still free, so "Dashboard" was served greedily and the
    /// test passed with the rescue pass deleted — a vacuous guard, caught by mutating the source.</para>
    /// </summary>
    private static GatewayColumn StarvingColumn() =>
        Column("Day Book", "Alter Company", "Statements", "Help", "Banking", "Reports", "Dashboard");

    /// <summary>
    /// 🔴 <b>THE LOCK.</b> The late row must still come away with an accelerator, because one can be freed.
    /// </summary>
    [Fact]
    public void A_row_added_late_still_gets_an_accelerator_when_one_can_be_freed()
    {
        var col = StarvingColumn();

        var dashboard = col.Items.First(i => i.Label == "Dashboard");
        Assert.True(dashboard.HasHotKey,
            "'Dashboard' got no hotkey even though a valid assignment exists — the greedy pass starved it.");

        // Every other row keeps one too: the rescue must re-house the holders, never dispossess them.
        Assert.All(col.Items.Where(i => i.IsSelectable),
                   i => Assert.True(i.HasHotKey, $"row '{i.Label}' lost its hotkey to the rescue pass"));
    }

    /// <summary>Every accelerator must still be a letter that actually occurs in that row's own label.</summary>
    [Fact]
    public void A_rehoused_row_keeps_a_letter_from_its_own_label()
    {
        var col = StarvingColumn();

        foreach (var item in col.Items.Where(i => i.IsSelectable))
        {
            Assert.True(item.HasHotKey, $"row '{item.Label}' got no hotkey");
            Assert.InRange(item.HotKeyIndex, 0, item.Label.Length - 1);
            Assert.Equal(item.Label[item.HotKeyIndex], item.HotKey!.Value);
            Assert.True(char.IsLetter(item.HotKey!.Value));
        }
    }

    /// <summary>Uniqueness is the whole point of the assignment and survives the rescue pass.</summary>
    [Fact]
    public void Rehousing_never_hands_the_same_letter_to_two_rows()
    {
        var col = StarvingColumn();

        var letters = col.Items.Where(i => i.HasHotKey)
                               .Select(i => char.ToUpperInvariant(i.HotKey!.Value))
                               .ToList();

        Assert.Equal(7, letters.Count);                              // all seven served
        Assert.Equal(letters.Count, letters.Distinct().Count());     // and no letter twice
    }

    /// <summary>
    /// 🔴 O and Y are reserved because they are already bare-letter arms elsewhere (Import / Export Data). The
    /// rescue pass walks a graph of letters and must never wander into one of them — a painted accelerator
    /// that silently does something else is worse than a missing one.
    /// </summary>
    [Fact]
    public void Rehousing_never_hands_out_a_reserved_letter()
    {
        // Every label here is built only from O and Y, so the ONLY letters the pass could reach are reserved.
        var col = Column("Oy", "Yo");

        Assert.All(col.Items.Where(i => i.IsSelectable),
                   i => Assert.False(i.HasHotKey,
                       $"row '{i.Label}' was handed a reserved letter"));
    }

    /// <summary>
    /// The contract is "no row goes without a letter unless none is possible" — NOT "every row always gets
    /// one". Two rows whose labels are the single same letter genuinely cannot both be served, and the second
    /// must still come away empty rather than duplicating the first.
    /// </summary>
    [Fact]
    public void A_genuinely_impossible_row_still_gets_nothing()
    {
        var col = Column("A", "A");

        Assert.True(col.Items[0].HasHotKey);
        Assert.False(col.Items[1].HasHotKey);
    }

    /// <summary>
    /// The rescue pass must not disturb a column that was already fully served: those columns are the vast
    /// majority, and their painted letters are what users have learned.
    /// </summary>
    [Fact]
    public void A_column_that_already_fits_keeps_exactly_the_letters_it_had()
    {
        var col = Column("Create", "Vouchers", "Banking", "Day Book");

        Assert.Equal('C', KeyOf(col, "Create"));
        Assert.Equal('V', KeyOf(col, "Vouchers"));
        Assert.Equal('B', KeyOf(col, "Banking"));
        Assert.Equal('D', KeyOf(col, "Day Book"));
    }

    /// <summary>A data-driven column filters on a bare letter, so it must still hand out no hotkeys at all.</summary>
    [Fact]
    public void A_data_driven_column_is_untouched_by_the_rescue_pass()
    {
        var col = new GatewayColumn("Ledgers") { Kind = GatewayColumnKind.DataDriven };
        col.Add(Row("Day Book"));
        col.Add(Row("Dashboard"));
        col.AssignHotKeys();

        Assert.All(col.Items, i => Assert.False(i.HasHotKey));
    }
}
