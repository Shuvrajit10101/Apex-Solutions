using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace Apex.Desktop.Tests;

/// <summary>
/// STRUCTURAL REACH ASSERTIONS over <c>src/Apex.Desktop</c> — the claims that behaviour tests cannot make,
/// because they are about code that does NOT exist rather than code that misbehaves.
///
/// <para><b>1. The company postal block must be typeable.</b> CGST Rule 46(a) requires the supplier's
/// <i>name, address and GSTIN</i> on every tax invoice. The printer has rendered the address since the print
/// half shipped, and the domain and the schema have carried the fields since v1 — but nothing in the desktop
/// layer ever ASSIGNED them, so the address half was unreachable from the product and every printed invoice
/// carried a blank supplier address. "There is no way to type it" is a defect a projection test cannot see:
/// it needs a scan for an assignment site.</para>
///
/// <para><b>1b. …and BOTH capture sites must keep assigning it.</b> The floor test above is satisfied by ONE
/// assignment site anywhere, and measurement showed that is far too weak: the four postal members had THREE
/// company-named assignment sites — the creation capture, the alteration capture, and the alteration screen's
/// private rollback helper — so deleting either real capture left the floor test green, and so did deleting
/// the whole of <c>Apply</c>. A rollback helper is not a way to type anything. The second test therefore
/// names the two methods that ARE the capture and requires each of them to assign all four. Renaming one is
/// a LOUD failure with this list in the message, which is the correct trade against a guard that silently
/// stops guarding.</para>
///
/// <para><b>2. The one guarded store opener must stay the only one.</b> <c>CompanyStorage.Save</c> calls
/// <c>Company.EnsureValid()</c> — the shared six-digit PIN rule, and since W0-2b the books-begin invariant
/// too — and it is the single write choke point the whole desktop layer funnels through, because it is the
/// only place that constructs a <c>SqliteCompanyStore</c>. A screen that opened its own store would route
/// around that guard silently, so the choke point itself is pinned here.</para>
///
/// <para>All three scan source text rather than reflect over types: an assignment site and a constructor call
/// are facts about the code, not about a running object graph.</para>
/// </summary>
public sealed class CompanyCaptureReachTests
{
    /// <summary>Resolves the repository root from THIS source file's location (the shape
    /// <c>XamlLayoutInvariantTests</c> uses), so the scan needs no build-time copy step and no working
    /// directory.</summary>
    private static string RepoRoot([CallerFilePath] string thisFile = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));

    private static string DesktopSourceRoot() => Path.Combine(RepoRoot(), "src", "Apex.Desktop");

    private static IEnumerable<(string Path, string Text)> DesktopSources()
    {
        var root = DesktopSourceRoot();
        Assert.True(Directory.Exists(root), $"src/Apex.Desktop not found at '{root}'.");
        foreach (var path in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            // Skip generated obj/bin output — it mirrors the same sources and would double-count.
            var rel = Path.GetRelativePath(root, path).Replace('\\', '/');
            if (rel.StartsWith("obj/", StringComparison.Ordinal) || rel.StartsWith("bin/", StringComparison.Ordinal))
                continue;
            yield return (rel, File.ReadAllText(path));
        }
    }

    private static string ReadDesktopSource(string relativePath)
    {
        var full = Path.Combine(DesktopSourceRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(full), $"'{relativePath}' is named by this test but does not exist at '{full}'.");
        return File.ReadAllText(full);
    }

    /// <summary>
    /// Blanks out <c>//</c> and <c>/* */</c> comments, preserving length and line breaks so a reported line
    /// number still points at the right line. STRING LITERALS ARE LEFT INTACT — a type name inside a string is
    /// exactly the reflection route the choke-point scan has to catch, not a false positive to suppress.
    /// </summary>
    private static string BlankComments(string text)
    {
        // The literal skipper below understands ordinary quoted strings and their backslash escapes, and
        // NOTHING ELSE. A verbatim (@"…") or raw ("""…""") literal would be mis-parsed, and a mis-parse here
        // could blank real code and turn a scan silently green — the one failure mode a guard must not have.
        // src/Apex.Desktop contains neither today (measured 2026-08-17: zero occurrences of each), so this
        // fails LOUDLY the day one is introduced instead of quietly losing its teeth.
        Assert.True(!text.Contains("@\"", StringComparison.Ordinal) && !text.Contains("\"\"\"", StringComparison.Ordinal),
            "This file now contains a verbatim or raw string literal. CompanyCaptureReachTests.BlankComments "
            + "cannot parse those; teach it to before adding one, or its scans stop meaning anything.");

        var chars = text.ToCharArray();
        var i = 0;
        while (i < chars.Length)
        {
            var c = chars[i];
            if (c == '"' || c == '\'')
            {
                var quote = c;
                i++;
                while (i < chars.Length && chars[i] != quote)
                {
                    if (chars[i] == '\\') i++;      // skip the escaped character
                    i++;
                }
                i++;
                continue;
            }
            if (c == '/' && i + 1 < chars.Length && chars[i + 1] == '/')
            {
                while (i < chars.Length && chars[i] != '\n') { chars[i] = ' '; i++; }
                continue;
            }
            if (c == '/' && i + 1 < chars.Length && chars[i + 1] == '*')
            {
                while (i < chars.Length && !(chars[i] == '*' && i + 1 < chars.Length && chars[i + 1] == '/'))
                {
                    if (chars[i] != '\n') chars[i] = ' ';
                    i++;
                }
                if (i < chars.Length) { chars[i] = ' '; i++; }
                if (i < chars.Length) { chars[i] = ' '; i++; }
                continue;
            }
            i++;
        }
        return new string(chars);
    }

    /// <summary>
    /// The four postal members the Rule 46(a) supplier block is built from. <c>MailingName</c> is the
    /// printed supplier NAME (the printer falls back to it before <c>Company.Name</c>), so it belongs to the
    /// same reachability claim.
    /// </summary>
    private static readonly string[] PostalMembers = { "Address", "State", "Pin", "MailingName" };

    /// <summary>
    /// Any assignment to one of the postal members, with its receiver identifier captured.
    ///
    /// <para><b>The receiver test is the whole point.</b> <c>PartyMailingDetails</c> carries members with the
    /// SAME names, and the ledger master assigns them — e.g. <c>mailing.MailingName = null;</c> in
    /// <c>LedgerMasterViewModel</c>. Those are the PARTY mailing block, not the company's, and counting them
    /// would make this test pass while the company postal block stayed untypeable. A previous corpus-grounding
    /// pass recorded that exact confusion, so it is excluded by construction rather than by an allow-list.</para>
    /// </summary>
    private static Regex AssignmentTo(string member) =>
        new(@"(?<![A-Za-z0-9_.])([A-Za-z_][A-Za-z0-9_]*)\." + member + @"\s*=(?!=)", RegexOptions.Compiled);

    /// <summary>
    /// <b>The naming convention this test contracts on:</b> a variable holding the company aggregate is called
    /// <c>company</c> / <c>_company</c> / something ending in "Company". A source scan cannot resolve types, so
    /// the receiver's NAME is what separates a company postal assignment from the identically-named party
    /// mailing one. Assign the company's postal block through a receiver named for what it holds.
    /// </summary>
    private static bool IsCompanyReceiver(string identifier) =>
        identifier.EndsWith("company", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// THE FLOOR — at least one assignment site exists anywhere in the desktop layer.
    ///
    /// <para><b>What it deliberately cannot tell you:</b> whether that site is a CAPTURE. A source scan sees
    /// <c>company.Address = …</c> and cannot know whether the right-hand side came from a control the operator
    /// typed into or from a snapshot being rolled back. That distinction is
    /// <see cref="Both_company_capture_methods_still_assign_every_postal_member"/>'s job, and this test's
    /// message no longer implies otherwise.</para>
    /// </summary>
    [Fact]
    public void The_company_postal_block_has_at_least_one_assignment_site_in_the_desktop_layer()
    {
        var sources = DesktopSources().Select(s => (s.Path, Text: BlankComments(s.Text))).ToList();
        var missing = new List<string>();

        foreach (var member in PostalMembers)
        {
            var rx = AssignmentTo(member);
            var hits = sources
                .SelectMany(s => rx.Matches(s.Text)
                    .Where(m => IsCompanyReceiver(m.Groups[1].Value))
                    .Select(m => $"{s.Path}:{LineOf(s.Text, m.Index)}"))
                .ToList();

            if (hits.Count == 0) missing.Add($"Company.{member}");
        }

        Assert.True(
            missing.Count == 0,
            "CGST Rule 46(a) requires the supplier's name, address and GSTIN on every tax invoice. The printer "
            + "renders the postal block and the schema stores it, but these members have NO assignment site "
            + "anywhere in src/Apex.Desktop, so a user cannot type them and every invoice prints a blank "
            + "supplier address: " + string.Join(", ", missing));
    }

    /// <summary>
    /// The two methods that ARE the company postal capture, named because a count cannot distinguish them
    /// from a rollback helper.
    /// <list type="bullet">
    /// <item><c>MainWindowViewModel.CreateCompany</c> — the creation path, which assigns onto the freshly
    /// seeded aggregate.</item>
    /// <item><c>CompanyProfileViewModel.Apply</c> — the alteration path, which assigns onto the open one.</item>
    /// </list>
    /// If either is renamed or moved, update this table in the same commit; the failure message says so.
    /// </summary>
    private static readonly (string File, string Signature, string Description)[] CaptureMethods =
    {
        ("ViewModels/MainWindowViewModel.cs", "public void CreateCompany()",
         "the Company Creation capture"),
        ("ViewModels/CompanyProfileViewModel.cs",
         "private void Apply(Company company, string? pin, DateOnly? fyStart, DateOnly? books, int? decimalPlaces)",
         "the Company Alteration capture"),
    };

    /// <summary>
    /// 🔴 THE RESOLUTION THE FLOOR TEST LACKS. Measured before this existed: <c>Company.MailingName</c>,
    /// <c>.Address</c>, <c>.State</c> and <c>.Pin</c> each had exactly three company-named assignment sites —
    /// the creation capture, the alteration capture, and the alteration screen's private <c>Restore</c>
    /// rollback helper — so deleting ANY ONE of them left the floor test green, including deleting the whole
    /// of <c>Apply</c>. A test with three independent satisfiers protects none of them.
    /// </summary>
    [Fact]
    public void Both_company_capture_methods_still_assign_every_postal_member()
    {
        var failures = new List<string>();

        foreach (var (file, signature, description) in CaptureMethods)
        {
            var body = MethodBody(BlankComments(ReadDesktopSource(file)), signature, file);
            foreach (var member in PostalMembers)
            {
                var assigned = AssignmentTo(member).Matches(body)
                    .Any(m => IsCompanyReceiver(m.Groups[1].Value));
                if (!assigned)
                    failures.Add($"{file} — {description} — never assigns Company.{member}");
            }
        }

        Assert.True(
            failures.Count == 0,
            "A company postal member lost its CAPTURE site. The floor test cannot see this: the same member is "
            + "also assigned by the alteration screen's rollback helper, which is not a way for anyone to type "
            + "anything. If a capture method was deliberately renamed or moved, update CaptureMethods in this "
            + "file in the same commit.\n  - " + string.Join("\n  - ", failures));
    }

    /// <summary>
    /// The body of the method whose signature line is <paramref name="signature"/>, by brace matching. Fails
    /// loudly rather than silently returning nothing, because a silent miss would make the caller vacuous.
    /// </summary>
    private static string MethodBody(string text, string signature, string file)
    {
        var at = text.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(at >= 0,
            $"'{signature}' was not found in {file}. If the method was renamed or its parameter list changed, "
            + "update CompanyCaptureReachTests.CaptureMethods in the same commit.");

        var open = text.IndexOf('{', at);
        Assert.True(open >= 0, $"'{signature}' in {file} has no body.");

        var depth = 0;
        for (var i = open; i < text.Length; i++)
        {
            if (text[i] == '{') depth++;
            else if (text[i] == '}')
            {
                depth--;
                if (depth == 0) return text[(open + 1)..i];
            }
        }

        Assert.Fail($"'{signature}' in {file} has an unbalanced body.");
        return string.Empty;
    }

    /// <summary>
    /// The choke point stays a choke point.
    ///
    /// <para><b>This scans for the TYPE NAME, not for <c>new</c>.</b> A <c>new … SqliteCompanyStore(</c>
    /// pattern was measured against seven injected bypasses and three walked straight past it:
    /// <c>new global::Apex.Persistence.Sqlite.SqliteCompanyStore(p)</c> (the namespace-qualifier group cannot
    /// match <c>::</c>), a <c>using</c> alias plus <c>new Store(p)</c>, and
    /// <c>Activator.CreateInstance(typeof(…SqliteCompanyStore), p)</c> — the last two never contain <c>new</c>
    /// beside the type name at all. A fourth, <c>new SqliteCompanyStore /*x*/ (p)</c>, is closed by blanking
    /// comments. Every one of them still has to NAME the type, in code or in a string, so naming it is what is
    /// scanned for. Comments are blanked first so a doc comment mentioning the class is not an offender;
    /// string literals are NOT, because <c>Type.GetType("…SqliteCompanyStore")</c> is a real route.</para>
    ///
    /// <para><b>The one blind spot that remains, stated rather than left to be discovered:</b> a name assembled
    /// at run time from fragments (<c>"Sqlite" + "CompanyStore"</c>). Closing that needs a Roslyn symbol check,
    /// which is a package dependency this test project does not carry; it is recorded here so nobody reads
    /// this test as total.</para>
    /// </summary>
    [Fact]
    public void Every_desktop_save_path_goes_through_the_one_guarded_store_opener()
    {
        var offenders = new List<string>();
        var rx = new Regex(@"(?<![A-Za-z0-9_])SqliteCompanyStore(?![A-Za-z0-9_])", RegexOptions.Compiled);

        foreach (var (path, text) in DesktopSources())
        {
            if (string.Equals(path, "Services/CompanyStorage.cs", StringComparison.OrdinalIgnoreCase)) continue;

            var code = BlankComments(text);
            foreach (Match m in rx.Matches(code))
                offenders.Add($"{path}:{LineOf(code, m.Index)}");
        }

        Assert.True(
            offenders.Count == 0,
            "CompanyStorage.Save is the desktop layer's single write choke point and the only place that calls "
            + "Company.EnsureValid(). Naming the store type anywhere else in src/Apex.Desktop is how that guard "
            + "gets routed around — by `new`, by an alias, or by reflection — so a bad PIN typed into a screen "
            + "would reach the database unvalidated. If a site is legitimately not a store construction, it "
            + "belongs in a comment (comments are not scanned), not in code. Offending sites: "
            + string.Join(", ", offenders));
    }

    private static int LineOf(string text, int index) =>
        text.Take(index).Count(c => c == '\n') + 1;
}
