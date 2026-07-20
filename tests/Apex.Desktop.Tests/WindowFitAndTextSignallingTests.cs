using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Apex.Desktop.ViewModels;
using Apex.Desktop.Views;

namespace Apex.Desktop.Tests;

/// <summary>
/// DURABLE LOCKS for the three defects fixed in the T1/T2/T3 pass: the window that could not fit a real
/// screen, and the two families of text that were cut off with no cue that anything was missing.
///
/// <para><b>T1 — the window was bigger than the desktop.</b> The shell declared
/// <c>MinWidth="1120"</c> DIP. A DIP is a physical pixel divided by the display scale, so the DIP width of
/// a desktop SHRINKS as the user raises Windows' scaling. On a 1366x768 laptop — still ordinary hardware —
/// Windows' own DEFAULT of 125% leaves 1092.8 DIP of desktop width, which is LESS than the app's declared
/// minimum. Avalonia does not refuse and does not scroll: it CLAMPS the window up to the minimum, so the
/// shell was handed a 1120-DIP client area on a 1092.8-DIP screen and 27.2 DIP (34 physical px) of the
/// right edge sat off the display. At 150% the overflow was 209.3 DIP wide and 48 DIP tall — and the
/// bottom 48 DIP is where the button bar lives. A window edge past a screen edge is not scrollable
/// content; there is no gesture that recovers it. Measured, with the identical window at
/// <c>MinWidth = 0</c>, the same sizes were accepted EXACTLY, which proves the app's own minimum was the
/// entire cause. The same clamp hit 1024x768 at 100% scaling, overflowing by 96 DIP with no DPI involved
/// at all. The fix lowers the minimum to 1024 DIP, which covers 1366x768 @125%, 1024x768 @100% and every
/// 1920x1080 scaling. (1366x768 @150% needs 910x475 and is deliberately out of scope: supporting it
/// requires <c>MinHeight &lt;= 470</c>, which costs roughly two data rows on every report pane.)
/// </para>
///
/// <para><b>T2 / T3 — silently cut text.</b> Two families lost characters with no ellipsis and no
/// tooltip, so a truncated value read as a complete one. (a) ComboBoxes: 192 of ~199 supply their own
/// <c>ItemTemplate</c> and only 4 mentioned <c>TextTrimming</c>, so a long selected value — a 55-character
/// party ledger, a bank name — was cut mid-glyph. Measured on the populated fixture at seven window
/// widths, 39 ComboBox values were silently cut at 1024 DIP and 13 at 1920. (b) The centered company-name
/// subtitle that sits under the title on ~59 screens: 28 of those sites declared NEITHER
/// <c>TextTrimming</c> NOR <c>TextWrapping</c>, so the 66-character fixture company name was hard-cut on
/// 70 screens at 1024 DIP, 21 at 1280 and still 3 at 1920. Both are now signalled — measured after the
/// fix, silent cuts are ZERO at every one of the seven widths in both families.</para>
///
/// <para><b>Why these assertions and not width assertions.</b> The app is globally monospace and the
/// COMMITTED headless harness resolves no font — it substitutes a stub roughly 82% wider than the real
/// glyphs. Any assertion on a measured text width would therefore be meaningless on CI and would have to
/// be re-tuned every time the harness changed. So nothing here measures text. The locks are (1) the
/// declared window geometry and the size the platform actually hands back, and (2) the STRUCTURAL
/// property that a control which can overflow declares how it will signal that — both of which hold
/// identically with or without a font, and neither of which uses <c>TextLayout</c>, <c>TextRuns</c> or
/// <c>CaptureRenderedFrame</c>.</para>
/// </summary>
public sealed class WindowFitAndTextSignallingTests
{
    private static readonly XNamespace Av = "https://github.com/avaloniaui";

    private static string RepoFile(string relative, [CallerFilePath] string thisFile = "")
    {
        var repoRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));
        return Path.Combine(repoRoot, relative.Replace('/', Path.DirectorySeparatorChar));
    }

    private static XDocument Load(string relative)
    {
        var path = RepoFile(relative);
        Assert.True(File.Exists(path), $"'{path}' not found.");
        return XDocument.Load(path, LoadOptions.SetLineInfo);
    }

    private static int Line(XElement e) => ((System.Xml.IXmlLineInfo)e).LineNumber;

    // =============================================================================================
    // T1 — the shell must be able to fit a real desktop.
    // =============================================================================================

    /// <summary>
    /// The smallest DIP desktop this app intends to support. 1366x768 at Windows' default 125% scaling
    /// yields 1092.8 x 576 DIP of WORK AREA (panel minus taskbar); 1024x768 at 100% yields 1024 x 728.
    /// The declared window minimum must fit inside BOTH, or the platform clamps the window larger than
    /// the screen and the overflow is unreachable.
    /// </summary>
    private const double SmallestSupportedDipWidth = 1024.0;
    private const double SmallestSupportedDipHeight = 576.0;

    [Fact]
    public void Shell_minimum_size_fits_the_smallest_supported_desktop()
    {
        var win = Load("src/Apex.Desktop/Views/MainWindow.axaml").Root!;
        Assert.True(win.Name == Av + "Window", "MainWindow.axaml root is no longer a Window.");

        var minW = double.Parse(win.Attribute("MinWidth")!.Value, CultureInfo.InvariantCulture);
        var minH = double.Parse(win.Attribute("MinHeight")!.Value, CultureInfo.InvariantCulture);

        Assert.True(minW <= SmallestSupportedDipWidth,
            $"MainWindow declares MinWidth={minW} DIP, which EXCEEDS the {SmallestSupportedDipWidth} DIP "
            + "width of the smallest supported desktop (1366x768 @125% = 1092.8 DIP, 1024x768 @100% = 1024 DIP). "
            + "Avalonia clamps the window UP to this minimum, so the shell is handed a client area wider than "
            + "the screen and the surplus sits off the right edge with no scrollbar and no way to reach it.");

        Assert.True(minH <= SmallestSupportedDipHeight,
            $"MainWindow declares MinHeight={minH} DIP, which EXCEEDS the {SmallestSupportedDipHeight} DIP "
            + "height of the smallest supported desktop's work area (1366x768 @125%). The bottom of the window "
            + "— where the button bar lives — would sit below the screen edge.");
    }

    /// <summary>
    /// The behavioural half of T1: at 125% scaling on a 1366x768 panel the shell must ACCEPT the 1092.8 DIP
    /// desktop instead of being clamped up to a wider one. This is the exact case that failed — before the
    /// fix the same call returned 1120.0.
    /// </summary>
    [AvaloniaTheory]
    [InlineData(1092.8, 576.0)]   // 1366x768 @125% work area — the T1 case
    [InlineData(1024.0, 728.0)]   // 1024x768 @100% — clamped by 96 DIP before the fix
    [InlineData(1280.0, 682.7)]   // 1920x1080 @150%
    public void Shell_accepts_the_smallest_supported_desktop_without_being_clamped_wider(double w, double h)
    {
        var win = new MainWindow { Width = w, Height = h };
        try
        {
            win.Show();
            win.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            Assert.True(win.ClientSize.Width <= w + 0.5,
                $"Asked for a {w} DIP client area; the platform returned {win.ClientSize.Width:F1}. The window "
                + "is being clamped WIDER than the desktop it has to fit, so the difference sits off-screen.");
            Assert.True(win.ClientSize.Height <= h + 0.5,
                $"Asked for a {h} DIP client height; the platform returned {win.ClientSize.Height:F1}.");
        }
        finally { win.Close(); }
    }

    /// <summary>
    /// The startup work-area fit — <c>MainWindow.FitToWorkArea</c>. This is what stops the 1440x900 XAML
    /// default from opening off the bottom of a 1366x768 panel, whose work area is only ~1366x720 DIP at
    /// 100% scaling: before it, the app opened 74 DIP wider and ~180 DIP taller than the screen could show,
    /// putting the bottom button bar off-screen on first launch with no DPI scaling involved at all.
    ///
    /// <para><b>Why the pure function and not a real window.</b> The headless test platform reports no
    /// screen work area, so the calling method returns early and a window-level test passes identically
    /// whether or not the logic is present — it would be a vacuous test. (Verified by mutation: deleting
    /// the caller's guard entirely left a window-level assertion GREEN.) Exercising the decision directly
    /// is the only way this can actually bite.</para>
    /// </summary>
    [Theory]
    // A caller's EXPLICIT size is never touched — the guard that protects every sized layout test in this
    // suite. Asked 1920x1080 on a small screen: unchanged, because it is not the XAML default.
    [InlineData(1920, 1080, /*xaml*/ 1440, 900, /*work*/ 1366, 720, /*scale*/ 1.0, /*expect*/ 1920, 1080)]
    [InlineData(1280, 720, 1440, 900, 1366, 720, 1.0, 1280, 720)]
    // The XAML default on a 1366x768 panel at 100% — the defect: 1440x900 must come down to 1366x720.
    [InlineData(1440, 900, 1440, 900, 1366, 720, 1.0, 1366, 720)]
    // The XAML default on a 1920x1080 panel — comfortably fits, so it is left alone.
    [InlineData(1440, 900, 1440, 900, 1920, 1032, 1.0, 1440, 900)]
    // DIP conversion: a 1366x768 panel at 125% is only 1092.8x576 DIP of work area.
    [InlineData(1440, 900, 1440, 900, 1366, 720, 1.25, 1092.8, 576)]
    // Never below the declared minimum, even on a desktop smaller than it (out of support scope).
    [InlineData(1440, 900, 1440, 900, 800, 480, 1.0, 1024, 576)]
    public void The_startup_fit_shrinks_only_the_default_size_and_never_below_the_minimum(
        double curW, double curH, double xamlW, double xamlH,
        double workW, double workH, double scaling, double expectW, double expectH)
    {
        var fitted = MainWindow.FitToWorkArea(
            new Size(curW, curH), new Size(xamlW, xamlH), new Size(workW, workH),
            scaling, new Size(1024, 576));

        Assert.Equal(expectW, fitted.Width, 1);
        Assert.Equal(expectH, fitted.Height, 1);

        // It may only ever SHRINK — growing a window to fill the work area would be a different (and
        // unwanted) behaviour, and would fight the user's own sizing.
        Assert.True(fitted.Width <= curW + 0.001 && fitted.Height <= curH + 0.001,
            $"The fit grew the window from {curW}x{curH} to {fitted.Width}x{fitted.Height}.");
    }

    // =============================================================================================
    // T3 — the centered company-name subtitle must declare how it signals an overflow.
    // =============================================================================================

    /// <summary>
    /// Every centered subtitle TextBlock must declare <c>TextTrimming</c> (show an ellipsis) or
    /// <c>TextWrapping</c> (flow onto a second line). Declaring NEITHER is the defect: the glyphs past the
    /// slot edge are dropped with no cue, so a cut company name reads as the whole company name.
    ///
    /// <para>The subtitle family is identified exactly as it is written throughout the file — a centered
    /// TextBlock in the muted subtitle grey. That signature is what makes this a lock rather than a
    /// snapshot: a NEW subtitle added later in the same house style is caught automatically, because the
    /// assertion is "none of them may lack a signal", not "these 28 sites are fixed".</para>
    /// </summary>
    [Fact]
    public void Every_centered_subtitle_declares_how_it_signals_an_overflow()
    {
        var doc = Load("src/Apex.Desktop/Views/MainWindow.axaml");

        var subtitles = doc.Root!.DescendantsAndSelf()
            .Where(e => e.Name == Av + "TextBlock"
                        && e.Attribute("HorizontalAlignment")?.Value == "Center"
                        && e.Attribute("Foreground")?.Value == "#555555")
            .ToList();

        Assert.True(subtitles.Count >= 55,
            $"Only {subtitles.Count} centered subtitle TextBlocks found (expected ~59). The subtitle "
            + "signature has changed, so this lock is no longer watching the family it was written for — "
            + "re-derive the selector rather than lowering this number.");

        var unsignalled = subtitles
            .Where(e => e.Attribute("TextTrimming") is null && e.Attribute("TextWrapping") is null)
            .ToList();

        Assert.True(unsignalled.Count == 0,
            $"{unsignalled.Count} centered subtitle TextBlock(s) declare neither TextTrimming nor "
            + "TextWrapping, so text past the slot edge is dropped with no cue. Add "
            + "TextTrimming=\"CharacterEllipsis\" (or TextWrapping=\"Wrap\" where the extra line is "
            + "affordable):\n"
            + string.Join("\n", unsignalled.Take(12).Select(e => $"  MainWindow.axaml({Line(e)})")));
    }

    /// <summary>
    /// T4 — the ellipsis needs an ESCAPE HATCH. An ellipsis is honest but not sufficient on its own: it
    /// tells the user characters are missing while giving no way to read them. A ComboBox is exempt
    /// because opening its dropdown reveals the full value; a static subtitle has no such gesture, so the
    /// project's own precedent for this class (the cascade menu row at MainWindow.axaml:331) pairs the
    /// trimming with <c>ToolTip.Tip</c> bound to the same source.
    ///
    /// <para><b>Why this family and not all 28 trimmed subtitles.</b> Measured at 1024 DIP — the worst
    /// supported width — the page-pane subtitle slot is 638.0 DIP, which at Consolas 12 (0.55 em advance
    /// = 6.6 DIP per glyph) holds about 96 characters. Only bindings that carry the USER'S OWN company
    /// name can reach that: <c>Subtitle</c> is composed as "{company name} — {qualifier}" everywhere it
    /// is set, and already measures 87 characters on the fixture with the SHORTEST qualifier ("as at
    /// 30-Apr-2020") — the longest qualifier in the codebase is 103 characters on its own, so the string
    /// reaches ~169 characters and roughly 72 of them are dropped. <c>CompanyName</c> is the raw,
    /// unbounded name. The remaining trimmed subtitles bind app-authored closed sets — a report title
    /// (longest "Age Analysis of Expiring Batches", 32 chars ≈ 211 DIP) or a formatted rupee amount
    /// (≈ 46 chars ≈ 303 DIP) — which cannot fill the slot, so a tooltip there would only restate text
    /// the user can already read in full. They are deliberately NOT covered.</para>
    ///
    /// <para><b>Why the tooltip must repeat the Text binding exactly.</b> A tooltip carrying a DIFFERENT
    /// value than the trimmed text is worse than none: it would answer "what was cut?" with something
    /// else. Asserting equality of the two binding expressions is what makes this a lock on the escape
    /// hatch rather than on the mere presence of an attribute.</para>
    /// </summary>
    [Fact]
    public void Every_trimmed_company_name_subtitle_offers_a_tooltip_with_the_same_value()
    {
        var doc = Load("src/Apex.Desktop/Views/MainWindow.axaml");

        // The company-name-bearing bindings. Both resolve to a value the USER types, so both are
        // unbounded in length and both are cut on the narrowest supported window.
        var companyNameBindings = new[] { "{Binding Subtitle}", "{Binding CompanyName}" };

        var atRisk = doc.Root!.DescendantsAndSelf()
            .Where(e => e.Name == Av + "TextBlock"
                        && e.Attribute("HorizontalAlignment")?.Value == "Center"
                        && e.Attribute("Foreground")?.Value == "#555555"
                        && e.Attribute("TextTrimming") is not null
                        && companyNameBindings.Contains(e.Attribute("Text")?.Value))
            .ToList();

        Assert.True(atRisk.Count >= 15,
            $"Only {atRisk.Count} trimmed company-name subtitles found (expected 15). The subtitle "
            + "signature or the binding names have changed, so this lock is no longer watching the family "
            + "it was written for — re-derive the selector rather than lowering this number.");

        var mute = atRisk.Where(e => e.Attribute("ToolTip.Tip") is null).ToList();

        Assert.True(mute.Count == 0,
            $"{mute.Count} trimmed company-name subtitle(s) carry an ellipsis but NO tooltip, so the user "
            + "is told characters are missing with no way to read them — a static subtitle has no "
            + "dropdown to open. Add ToolTip.Tip bound to the same source as Text (the precedent is the "
            + "cascade menu row at MainWindow.axaml:331):\n"
            + string.Join("\n", mute.Take(12).Select(e => $"  MainWindow.axaml({Line(e)})")));

        var mismatched = atRisk
            .Where(e => e.Attribute("ToolTip.Tip")!.Value != e.Attribute("Text")!.Value)
            .ToList();

        Assert.True(mismatched.Count == 0,
            $"{mismatched.Count} subtitle tooltip(s) do not bind the same value as the text they explain, "
            + "so hovering a truncated name would reveal a DIFFERENT string:\n"
            + string.Join("\n", mismatched.Take(12).Select(
                e => $"  MainWindow.axaml({Line(e)}): Text={e.Attribute("Text")!.Value} "
                     + $"ToolTip.Tip={e.Attribute("ToolTip.Tip")!.Value}")));
    }

    // =============================================================================================
    // T2 / RC-5 — a ComboBox value must signal an overflow.
    // =============================================================================================

    /// <summary>
    /// The single app-level style that gives all ~199 ComboBoxes an ellipsis. A per-site fix was rejected:
    /// 192 of them supply their own <c>ItemTemplate</c>, so a per-site sweep both misses sites today and
    /// misses every ComboBox added tomorrow.
    /// </summary>
    [Fact]
    public void ComboBox_values_are_given_a_trimming_style_at_application_level()
    {
        var doc = Load("src/Apex.Desktop/App.axaml");

        var style = doc.Root!.DescendantsAndSelf()
            .FirstOrDefault(e => e.Name == Av + "Style"
                                 && (e.Attribute("Selector")?.Value ?? "").Contains("ComboBox")
                                 && (e.Attribute("Selector")?.Value ?? "").Contains("TextBlock"));

        Assert.True(style is not null,
            "App.axaml no longer carries a 'ComboBox TextBlock' Style. Without it, ~199 ComboBoxes — 192 of "
            + "which supply their own ItemTemplate — cut long values mid-glyph with no ellipsis, so a "
            + "truncated ledger or bank name reads as a complete one.");

        var setter = style!.Descendants().FirstOrDefault(e => e.Name == Av + "Setter"
                                                              && e.Attribute("Property")?.Value == "TextTrimming");
        Assert.True(setter is not null,
            $"MainWindow's ComboBox style at App.axaml({Line(style)}) no longer sets TextTrimming.");
        Assert.Equal("CharacterEllipsis", setter!.Attribute("Value")?.Value);
    }

    /// <summary>
    /// The behavioural half: the style must actually REACH the TextBlock inside a live ComboBox. A style
    /// present in App.axaml but not matching (a changed selector, a template that re-parents its content
    /// out of the ComboBox subtree) would leave the defect wide open while the static assertion above still
    /// passed. Asserts the resolved property only — no text, no font, no measurement.
    /// </summary>
    [AvaloniaFact]
    public void The_trimming_style_reaches_the_TextBlock_inside_a_live_ComboBox()
    {
        var win = new Window { Width = 400, Height = 200 };
        var combo = new ComboBox
        {
            Width = 120,
            ItemsSource = new[] { "Bank of Maharashtra — Bhosari MIDC Branch, Pune" },
        };
        win.Content = combo;
        try
        {
            win.Show();
            combo.SelectedIndex = 0;
            win.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            win.UpdateLayout();

            var texts = combo.GetVisualDescendants().OfType<TextBlock>().ToList();
            Assert.True(texts.Count > 0, "The ComboBox realised no TextBlock, so this lock proves nothing.");
            Assert.All(texts, tb => Assert.Equal(TextTrimming.CharacterEllipsis, tb.TextTrimming));
        }
        finally { win.Close(); }
    }
}
