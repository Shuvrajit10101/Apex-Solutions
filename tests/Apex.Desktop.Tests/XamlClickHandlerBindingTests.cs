using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Apex.Desktop.Tests;

/// <summary>
/// 🔴 <b>EVERY <c>Click="…"</c> IN THE WINDOW RESOLVES TO A DECLARED HANDLER.</b>
///
/// <para><b>The hazard this closes, and it is a real one this slice walked into.</b> A XAML <c>Click=</c> binds to
/// its handler <b>by name, at RUNTIME</b>. Rename the method without renaming the binding — or the reverse — and
/// the solution still <b>compiles clean with zero warnings</b>, the app still starts, and the failure appears only
/// when a user clicks the button. Nothing in this repository checked it: before this file there was no test
/// anywhere that read a <c>Click=</c> attribute.</para>
///
/// <para><b>Why it lands in Phase 10.11 S4.</b> S4 renamed <c>OnCancelVoucherClick</c> →
/// <c>OnAbandonVoucherEntryClick</c>, because S3 had renamed the view-model verb <c>CancelVoucher</c> →
/// <c>AbandonEntry</c> and left the XAML handler carrying the old word — while <b>Alt+X</b> now means "cancel a
/// POSTED voucher" and <b>Alt+D</b> means "delete one". Three meanings were sharing two names in the one slice
/// where "discard what I am typing", "void a posted document" and "remove a posted document" have to be exact. The
/// rename is the fix; <b>this test is what makes the next such rename safe</b>, because a half-rename is now a red
/// test instead of a dead button.</para>
///
/// <para><b>What it deliberately does NOT do.</b> It does not check the handler's BODY, its signature beyond the
/// name, or that the button is reachable — those are the jobs of the behavioural suites. It is a name-resolution
/// check, which is exactly the class of failure the compiler cannot see.</para>
/// </summary>
public sealed class XamlClickHandlerBindingTests
{
    /// <summary>The repository root — the directory holding <c>Apex.slnx</c>.</summary>
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Apex.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string ViewsDirectory() => Path.Combine(RepoRoot(), "src", "Apex.Desktop", "Views");

    [Fact]
    public void Every_Click_handler_named_in_XAML_is_declared_in_its_code_behind()
    {
        var views = Directory.GetFiles(ViewsDirectory(), "*.axaml", SearchOption.AllDirectories);
        Assert.NotEmpty(views);

        var checkedPairs = 0;
        var missing = new List<string>();

        foreach (var view in views)
        {
            var codeBehindPath = view + ".cs";
            if (!File.Exists(codeBehindPath)) continue;

            var xaml = File.ReadAllText(view);
            var codeBehind = File.ReadAllText(codeBehindPath);
            var viewName = Path.GetFileName(view);

            // Only NAME references matter — an inline `{Binding …}` command is resolved by the binding engine and
            // is not a code-behind method, so it is excluded rather than reported as missing.
            foreach (Match m in Regex.Matches(xaml, @"Click\s*=\s*""([^""{}]+)"""))
            {
                var handler = m.Groups[1].Value.Trim();
                if (handler.Length == 0) continue;

                checkedPairs++;
                // The declaration form used throughout: `private void OnFooClick(object? sender, RoutedEventArgs e)`.
                if (!Regex.IsMatch(codeBehind, @"\b(?:private|protected|internal|public)\s[^;{}]*\b"
                                               + Regex.Escape(handler) + @"\s*\("))
                    missing.Add($"{viewName} binds Click=\"{handler}\" but {Path.GetFileName(codeBehindPath)} "
                                + "declares no such method");
            }
        }

        // Non-vacuity: a scan that silently matched nothing would pass this test while proving nothing at all.
        Assert.True(checkedPairs >= 50,
            $"Only {checkedPairs} Click bindings were scanned — the scan has stopped reading the views.");

        Assert.True(missing.Count == 0,
            $"{missing.Count} Click binding(s) name a handler that does not exist. A XAML Click= binds by NAME at "
            + "RUNTIME, so each of these compiles clean and fails only when a user clicks:"
            + Environment.NewLine + string.Join(Environment.NewLine, missing));
    }

    /// <summary>
    /// The bite proof for the scan above: a synthetic half-rename — a handler name that appears in no code-behind —
    /// must be reported. Without this the regex could be silently over-permissive and the test would be decoration.
    /// </summary>
    [Fact]
    public void The_scan_bites_on_a_synthetic_half_rename()
    {
        const string xaml = """<Button Content="X" Click="OnHandlerThatWasNeverWritten"/>""";
        const string codeBehind = """
            private void OnSomethingElseClick(object? sender, RoutedEventArgs e) => Vm?.Nothing();
            """;

        var handler = Regex.Match(xaml, @"Click\s*=\s*""([^""{}]+)""").Groups[1].Value;
        Assert.Equal("OnHandlerThatWasNeverWritten", handler);

        Assert.DoesNotMatch(@"\b(?:private|protected|internal|public)\s[^;{}]*\b"
                            + Regex.Escape(handler) + @"\s*\(", codeBehind);

        // …and the same matcher accepts the handler that IS declared, so it is not simply refusing everything.
        Assert.Matches(@"\b(?:private|protected|internal|public)\s[^;{}]*\b"
                       + Regex.Escape("OnSomethingElseClick") + @"\s*\(", codeBehind);
    }

    /// <summary>
    /// The specific rename S4 performed, pinned by name in both directions: the OLD name must be gone from BOTH
    /// files and the NEW name present in BOTH. The general scan above would stay green on a "rename" that changed
    /// neither file, and green on one that changed both to the wrong word; this one states what the slice actually
    /// did, so a later revert to "cancel" wording on an ABANDON verb is caught with its reason attached.
    /// </summary>
    [Fact]
    public void The_voucher_entry_Cancel_button_binds_the_ABANDON_verb_by_that_name()
    {
        var xaml = File.ReadAllText(Path.Combine(ViewsDirectory(), "MainWindow.axaml"));
        var codeBehind = File.ReadAllText(Path.Combine(ViewsDirectory(), "MainWindow.axaml.cs"));

        Assert.Contains("Click=\"OnAbandonVoucherEntryClick\"", xaml);
        Assert.Contains("void OnAbandonVoucherEntryClick(", codeBehind);

        // The old name survives NOWHERE — not as a binding, not as a declaration. (The code-behind's doc comment
        // quotes it inside a <c>…</c> tag to explain the rename, which is why this asserts on the two ACTIVE
        // forms rather than on the bare word.)
        Assert.DoesNotContain("Click=\"OnCancelVoucherClick\"", xaml);
        Assert.DoesNotContain("void OnCancelVoucherClick(", codeBehind);
    }
}
