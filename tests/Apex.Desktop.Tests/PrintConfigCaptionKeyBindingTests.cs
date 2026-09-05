using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Apex.Desktop.Tests;

/// <summary>
/// <b>A panel may not advertise a key it does not bind.</b>
///
/// <para><b>🔴 WHY THIS FILE EXISTS.</b> W2-31 added three knob-group captions to the F12 print-config panel —
/// "Print format (F8)", "Paper (F9)" and "Copies and pages (F5 / F10)" — while adding <b>no key routing at all</b>
/// for that panel: the string <c>PrintConfigPanel</c> does not appear anywhere in
/// <c>MainWindow.axaml.cs</c>. Every one of the four advertised keys therefore fell through to the global
/// F-key switch, and <b>F10 was the damaging one</b>: with the panel open it reaches
/// <c>case Key.F10 … vm.ShowOtherVouchersMenu()</c> and navigates the operator out of the print preview
/// altogether. A caption that promises a print action and delivers a navigation away is worse than a dead
/// caption, because the operator learns the wrong model of the panel and loses their place.
///
/// <para>The captions were corrected to name no key. That is the honest end of the fix available here: under
/// design ruling R14 the vendor corpus is gone, and <c>help.tallysolutions.com</c> was not consulted for what
/// these four keys ought to do inside a print-config panel, so <b>inventing bindings would be a fidelity
/// guess dressed as a feature</b>. Binding them stays an open item; this lock makes it impossible to
/// re-advertise a key without binding it first.</para>
///
/// <para><b>The lock is written as a subset rule, not as "no keys allowed"</b>, so it keeps biting rather than
/// becoming an obstacle the moment someone does the real work: the instant the code-behind routes a key while
/// the panel is open, that key becomes legal to advertise, and this test starts guarding the pairing instead of
/// forbidding it.</para>
/// </summary>
public sealed class PrintConfigCaptionKeyBindingTests
{
    private static readonly XNamespace Av = "https://github.com/avaloniaui";
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static string RepoRoot([CallerFilePath] string thisFile = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));

    private static string AxamlPath() => Path.Combine(RepoRoot(), "src", "Apex.Desktop", "Views", "MainWindow.axaml");

    private static string CodeBehindPath()
        => Path.Combine(RepoRoot(), "src", "Apex.Desktop", "Views", "MainWindow.axaml.cs");

    /// <summary>
    /// The print-config panel's own markup: the <c>DataTemplate</c> bound to <c>PrintConfigViewModel</c>. Located
    /// by <c>x:DataType</c> and never by position, so re-ordering the overlays cannot silently empty the scan.
    /// </summary>
    private static XElement PanelTemplate()
    {
        var doc = XDocument.Load(AxamlPath());
        var template = doc.Descendants(Av + "DataTemplate")
                          .SingleOrDefault(e => (string?)e.Attribute(X + "DataType") == "vm:PrintConfigViewModel");
        Assert.True(template is not null,
            "the PrintConfigViewModel DataTemplate was not found in MainWindow.axaml — this scan has gone blind "
          + "and would pass vacuously; re-point it at the panel's new markup.");
        return template!;
    }

    /// <summary>Every function key named by a caption inside the panel, e.g. "Copies and pages (F5 / F10)".</summary>
    private static IReadOnlyList<string> AdvertisedKeys()
        => PanelTemplate()
            .Descendants(Av + "TextBlock")
            .Select(t => (string?)t.Attribute("Text") ?? string.Empty)
            .SelectMany(text => Regex.Matches(text, @"\bF([1-9]|1[0-2])\b").Select(m => m.Value))
            .Distinct()
            .OrderBy(k => k.Length).ThenBy(k => k)
            .ToList();

    /// <summary>
    /// Every function key the code-behind routes <b>while the print-config panel is open</b>. Detected by a
    /// <c>Key.Fn</c> appearing on a line that also mentions <c>PrintConfigPanel</c> — the only way the handler
    /// can know the panel is up. Today this is empty, which is exactly the finding.
    /// </summary>
    private static IReadOnlyList<string> RoutedKeys()
        => File.ReadAllLines(CodeBehindPath())
            .Where(line => line.Contains("PrintConfigPanel", System.StringComparison.Ordinal))
            .SelectMany(line => Regex.Matches(line, @"\bKey\.(F(?:[1-9]|1[0-2]))\b").Select(m => m.Groups[1].Value))
            .Distinct()
            .ToList();

    /// <summary>
    /// <b>THE OPERATOR-FACING ASSERTION.</b> Every key the panel advertises must be a key the panel routes.
    /// </summary>
    [Fact]
    public void The_panel_advertises_no_function_key_it_does_not_route()
    {
        var advertised = AdvertisedKeys();
        var routed = RoutedKeys();
        var unbound = advertised.Where(k => !routed.Contains(k)).ToList();

        Assert.True(unbound.Count == 0,
            $"the print-config panel advertises {unbound.Count} function key(s) it does not bind: "
          + string.Join(", ", unbound)
          + ". With the panel open these fall through to the global F-key switch — F10 in particular reaches "
          + "ShowOtherVouchersMenu() and navigates out of the print preview. Either route the key while "
          + "PrintConfigPanel is open, or take the key out of the caption.");
    }

    /// <summary>
    /// Non-vacuity: the scan really is reading the panel's captions. Without this, a renamed template or a
    /// caption moved into a binding would reduce the guard above to nothing while staying green.
    /// </summary>
    [Fact]
    public void The_caption_scan_actually_reads_the_panel()
    {
        var captions = PanelTemplate()
            .Descendants(Av + "TextBlock")
            .Select(t => (string?)t.Attribute("Text") ?? string.Empty)
            .Where(s => s.Length > 0)
            .ToList();

        Assert.True(captions.Count >= 5,
            $"only {captions.Count} literal caption(s) found in the print-config panel — the scan has gone blind.");
        // "Copies and pages" was split into "Copies" (offered on every kind — every renderer honours the count)
        // and "Pages" (gated on SupportsPageKnobs, because only ReportPdf honours a range).
        Assert.Contains(captions, c => c.Contains("Print format", System.StringComparison.Ordinal));
        Assert.Contains(captions, c => c.Contains("Paper", System.StringComparison.Ordinal));
        Assert.Contains(captions, c => c.Contains("Copies", System.StringComparison.Ordinal));
        Assert.Contains(captions, c => c.Contains("Pages", System.StringComparison.Ordinal));
    }
}
