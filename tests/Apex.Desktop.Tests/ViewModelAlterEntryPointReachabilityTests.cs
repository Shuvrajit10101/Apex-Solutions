using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Apex.Desktop.ViewModels;
using Xunit;

namespace Apex.Desktop.Tests;

/// <summary>
/// 🔴 <b>THE STANDING REACHABILITY INVARIANT.</b> No screen-opening static factory on a view model may ship with
/// <b>zero production callers</b>. The set it polices is DERIVED, not listed — so the next one cannot slip past by
/// not being on anybody's list.
///
/// <para><b>Why this exists, and why it is not a fourth per-screen test.</b> The defect has now shipped TWICE, one
/// file apart. <c>StockItemMasterViewModel.ForAlter</c> shipped with no production caller while its own tests
/// called it directly — proving the mechanism and nothing about reachability — and
/// <see cref="StockItemAlterReachabilityTests"/> was written to close it. Then
/// <c>VoucherEntryViewModel.ForAlter</c> shipped the same way, in a codebase that already contained that test.
/// A per-screen lock only ever covers the screen somebody remembered to write it for; this one covers the SHAPE.</para>
///
/// <para><b>WHAT "REACHABLE" MEANS HERE — read this before trusting or changing the test.</b> An entry point is
/// reachable iff <b>some method compiled into the shipped <c>Apex.Desktop</c> assembly, declared outside the entry
/// point's own outermost type, emits a call to it</b>. "Some method" is every method, constructor, property
/// accessor, lambda and local function in the assembly — including <c>MainWindow</c>'s tunnel key handler and the
/// <c>Click=</c> handlers XAML binds to, which is what makes this a statement about operator routes rather than
/// about tests. It is decided by reading IL, not source text, so a <c>&lt;see cref="X.ForAlter"/&gt;</c> in a doc
/// comment cannot be mistaken for a call site (there are several of those in this repository, and a naive text
/// scan would have declared the voucher factory reachable on the strength of them).</para>
///
/// <para><b>WHAT IT DOES NOT COVER — stated, because the next reader will need to know.</b>
/// <list type="bullet">
///   <item><b>It is not TRANSITIVE.</b> It proves somebody calls the factory; it does not prove that caller is
///     itself reachable from a keystroke. A screen whose only caller is a method nothing else calls passes here.
///     The per-screen driving tests (<see cref="StockItemAlterReachabilityTests"/>,
///     <see cref="VoucherAlterReachabilityTests"/>) are what close that half, by pressing real keys on a real
///     window. Both halves are needed; neither subsumes the other.</item>
///   <item>Calls made by reflection, by <c>dynamic</c>, or from a different assembly are invisible to it.</item>
///   <item>It reads exactly one assembly, <c>Apex.Desktop</c> — the shipped desktop app. Test assemblies are
///     never scanned, which is the entire point: a factory whose only callers are tests must FAIL.</item>
/// </list></para>
///
/// <para><b>THE SET IT DERIVES TODAY, recorded so a later reader can see the net rather than infer it —</b>
/// five members: <c>AccountGroupMasterViewModel.ForAlter</c>, <c>LedgerMasterViewModel.ForAlter</c>,
/// <c>StockItemMasterViewModel.ForAlter</c>, <c>VoucherEntryViewModel.ForAlter</c> — and
/// <c>MenuItemViewModel.Header</c>, which is an honest OVER-inclusion: it is a static factory returning a
/// view model, but a menu ROW rather than a screen. It is left in rather than excluded by name.
/// Over-inclusion costs nothing here (it is called from production, so it passes) and every exclusion rule
/// is one more place the next unreachable factory can hide. <b>This paragraph is documentation, not
/// configuration</b> — nothing below reads it, and adding a factory does not require touching it.</para>
/// </summary>
public sealed class ViewModelAlterEntryPointReachabilityTests
{
    private const string ViewModelsNamespace = "Apex.Desktop.ViewModels";

    /// <summary>The SHIPPED assembly — resolved from a production type, never from this test assembly.</summary>
    private static readonly Assembly Desktop = typeof(MainWindowViewModel).Assembly;

    // ============================================================ deriving the set

    /// <summary>
    /// <b>THE SHAPE.</b> A <c>public static</c> method, declared on a type in <see cref="ViewModelsNamespace"/>,
    /// whose return type <em>is a screen</em>: a <c>*ViewModel</c> in that namespace, or a type in that namespace
    /// that CARRIES one on a public instance property (the <see cref="VoucherAlterationOpen"/> shape — its
    /// <c>Entry</c> property is the screen, and a shape test that only looked at the return type's own name would
    /// have missed the very factory this invariant was written for).
    ///
    /// <para>Property accessors and operators are excluded (<c>IsSpecialName</c>): a getter is not a factory.
    /// Generic-parameter and by-ref returns are excluded because no screen factory has one.</para>
    /// </summary>
    private static bool ReturnsAScreen(Type? type, bool allowWrapper)
    {
        if (type is null || type.IsByRef || type.IsGenericParameter) return false;
        if (type.Namespace != ViewModelsNamespace) return false;
        if (type.Name.EndsWith("ViewModel", StringComparison.Ordinal)) return true;

        // One level of unwrapping only — enough for the two-sided result type, and it cannot recurse forever.
        return allowWrapper
            && type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                   .Any(p => ReturnsAScreen(p.PropertyType, allowWrapper: false));
    }

    /// <summary>Every method in the shipped assembly that matches the shape above.</summary>
    private static List<MethodInfo> ScreenFactories() =>
        ViewModelTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Where(m => !m.IsSpecialName && ReturnsAScreen(m.ReturnType, allowWrapper: true))
            .OrderBy(m => m.DeclaringType!.Name, StringComparer.Ordinal)
            .ThenBy(m => m.Name, StringComparer.Ordinal)
            .ToList();

    private static IEnumerable<Type> ViewModelTypes() =>
        TypesOf(Desktop).Where(t => t.Namespace == ViewModelsNamespace);

    private static IEnumerable<Type> TypesOf(Assembly assembly)
    {
        try { return assembly.GetTypes(); }
        // A partially-loadable assembly would silently shrink the set to nothing, which is exactly the
        // "passes because it found nothing" failure this class must not have — so it is loud.
        catch (ReflectionTypeLoadException ex)
        {
            throw new InvalidOperationException(
                $"Could not enumerate the types of {assembly.GetName().Name}, so this invariant could not be "
                + "evaluated: " + string.Join(" | ", ex.LoaderExceptions.Select(e => e?.Message)), ex);
        }
    }

    private static string Name(MethodInfo m) => $"{m.DeclaringType!.Name}.{m.Name}";

    // ============================================================ deciding reachability (an IL read)

    /// <summary>Every IL opcode, keyed by the value the byte stream carries, so operand lengths are read from the
    /// runtime's own table rather than from a table this test would have to keep correct by hand.</summary>
    private static readonly Dictionary<short, OpCode> OpcodeTable = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(f => f.FieldType == typeof(OpCode))
        .Select(f => (OpCode)f.GetValue(null)!)
        .ToDictionary(op => op.Value);

    /// <summary>The metadata tokens of every method this IL body CALLS (or takes a function pointer to, or loads a
    /// token for) — <c>call</c>, <c>callvirt</c>, <c>newobj</c>, <c>ldftn</c>, <c>ldvirtftn</c> and
    /// <c>ldtoken</c> all carry an inline method/token operand.</summary>
    private static IEnumerable<int> ReferencedMethodTokens(byte[] il)
    {
        var pos = 0;
        while (pos < il.Length)
        {
            short value;
            if (il[pos] == 0xFE)
            {
                if (pos + 1 >= il.Length) yield break;
                value = unchecked((short)((0xFE << 8) | il[pos + 1]));
                pos += 2;
            }
            else
            {
                value = il[pos];
                pos += 1;
            }

            // An unknown opcode means the walk has lost alignment; stopping is the only honest answer, and it
            // errs towards UNREACHABLE (a failing invariant), never towards a false pass.
            if (!OpcodeTable.TryGetValue(value, out var op)) yield break;

            int operandSize;
            switch (op.OperandType)
            {
                case OperandType.InlineNone: operandSize = 0; break;
                case OperandType.ShortInlineBrTarget:
                case OperandType.ShortInlineI:
                case OperandType.ShortInlineVar: operandSize = 1; break;
                case OperandType.InlineVar: operandSize = 2; break;
                case OperandType.InlineI8:
                case OperandType.InlineR: operandSize = 8; break;
                case OperandType.InlineSwitch:
                    if (pos + 4 > il.Length) yield break;
                    operandSize = 4 + (BitConverter.ToInt32(il, pos) * 4);
                    break;
                default: operandSize = 4; break;   // every remaining InlineXxx / ShortInlineR is four bytes
            }

            if (pos + operandSize > il.Length) yield break;

            if (op.OperandType is OperandType.InlineMethod or OperandType.InlineTok)
                yield return BitConverter.ToInt32(il, pos);

            pos += operandSize;
        }
    }

    /// <summary>The outermost enclosing type — a lambda compiles into a nested display class, and a call from
    /// inside one is still a call from the type that wrote the lambda.</summary>
    private static Type Outermost(Type type)
    {
        while (type.DeclaringType is { } outer) type = outer;
        return type;
    }

    private static IEnumerable<MethodBase> AllMethodBodies(Assembly assembly) =>
        TypesOf(assembly).SelectMany(t => t
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                        | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Cast<MethodBase>()
            .Concat(t.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                                      | BindingFlags.Static | BindingFlags.DeclaredOnly)));

    /// <summary>
    /// Whether <paramref name="caller"/>'s IL references <paramref name="target"/>.
    ///
    /// <para>🔴 <b>Two comparisons, and the second is not optional.</b> A call INSIDE the defining assembly carries
    /// a <c>MethodDef</c> token equal to <see cref="MemberInfo.MetadataToken"/>, so the cheap equality decides it.
    /// A call from ANOTHER assembly (every call from this test project) carries a <c>MemberRef</c> token whose
    /// value is unrelated, and only resolving it through the caller's own module maps it back. Dropping the
    /// resolving half would silently make cross-assembly callers invisible — which would not weaken the lock (the
    /// production scan is same-assembly) but WOULD make
    /// <see cref="Callers_in_the_test_assembly_do_not_count_as_production_callers"/> vacuous.</para>
    /// </summary>
    private static bool References(MethodBase caller, MethodInfo target)
    {
        byte[]? il;
        try { il = caller.GetMethodBody()?.GetILAsByteArray(); }
        catch (Exception) { return false; }          // abstract / extern / unmanaged — no body to read
        if (il is null || il.Length == 0) return false;

        foreach (var token in ReferencedMethodTokens(il))
        {
            if (caller.Module == target.Module && token == target.MetadataToken) return true;

            MethodBase? resolved = null;
            try
            {
                resolved = caller.Module.ResolveMethod(
                    token,
                    caller.DeclaringType is { IsGenericType: true } dt ? dt.GetGenericArguments() : null,
                    caller.IsGenericMethodDefinition ? caller.GetGenericArguments() : null);
            }
            // Expected and frequent: the token is a field/type/string token, or names a method this context
            // cannot resolve. Either way it is not the target.
            catch (ArgumentException) { }
            catch (BadImageFormatException) { }
            catch (MissingMethodException) { }

            if (resolved is not null
                && resolved.MetadataToken == target.MetadataToken
                && resolved.Module == target.Module) return true;
        }

        return false;
    }

    /// <summary>
    /// The call sites of <paramref name="target"/> inside <paramref name="assembly"/>, excluding anything declared
    /// on the target's own outermost type.
    ///
    /// <para>The declaring type is excluded on purpose. A factory calling itself, or being called by a sibling on
    /// the same class, is not an operator route — it is the same island. That is precisely the state
    /// <c>VoucherEntryViewModel.ForAlter</c> was in.</para>
    /// </summary>
    private static List<string> CallSitesIn(Assembly assembly, MethodInfo target)
    {
        var home = Outermost(target.DeclaringType!);
        return AllMethodBodies(assembly)
            .Where(m => m.DeclaringType is not null && Outermost(m.DeclaringType) != home)
            .Where(m => References(m, target))
            .Select(m => $"{m.DeclaringType!.Name}.{m.Name}")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>The call sites that COUNT — those in the shipped desktop assembly and nowhere else.</summary>
    private static List<string> ProductionCallSites(MethodInfo target) => CallSitesIn(Desktop, target);

    // ============================================================ the invariant

    /// <summary>
    /// 🔴 <b>THE LOCK.</b> Every screen-opening static factory on a view model has at least one production caller.
    ///
    /// <para><b>PROVEN NON-VACUOUS BY MUTATION — three runs, all recorded, all restored byte-for-byte.</b>
    /// <list type="number">
    ///   <item><b>It reddens for an entry point that is genuinely unreachable.</b> A throwaway
    ///     <c>public static LedgerMasterViewModel? ForNothing(Company, CompanyStorage)</c> was added to
    ///     <c>LedgerMasterViewModel</c> with no caller anywhere. This test went RED naming
    ///     <c>LedgerMasterViewModel.ForNothing</c>, and green again once it was removed.</item>
    ///   <item>🔴 <b>It reddens for THE defect this slice fixed — the decisive proof.</b> The single production
    ///     call to <c>VoucherEntryViewModel.ForAlter</c> (in
    ///     <c>MainWindowViewModel.ShowVoucherAlteration</c>) was replaced with a stand-in of the same signature,
    ///     reproducing the state the code actually shipped in. This test went RED naming
    ///     <c>VoucherEntryViewModel.ForAlter</c> — while the S5b and S5c suites, which call that factory
    ///     directly, stayed green. That is the whole failure mode, caught.</item>
    ///   <item><b>It passes for the ones that ARE wired, and cannot pass by finding nothing.</b> The set is
    ///     asserted non-empty here, and cross-checked against an independent by-NAME query in
    ///     <see cref="The_shape_predicate_finds_every_ForAlter_factory_in_the_assembly"/> — so a shape predicate
    ///     narrowed until it selects nothing fails there rather than passing here in silence.</item>
    /// </list></para>
    /// </summary>
    [Fact]
    public void Every_screen_opening_view_model_factory_has_a_production_caller()
    {
        var factories = ScreenFactories();

        // A pass with an empty set is the failure this class exists to catch, so it is asserted, not assumed.
        Assert.NotEmpty(factories);

        var unreachable = factories
            .Where(f => ProductionCallSites(f).Count == 0)
            .Select(Name)
            .ToList();

        Assert.True(unreachable.Count == 0,
            "These view-model screen factories have NO production caller — no operator can reach them, and every "
            + "caller is a test:\n  " + string.Join("\n  ", unreachable)
            + "\nWire each one to a real route (a MainWindow key arm, a Click handler, or another production "
            + "path) and add a driving test that presses the key on a real window. Do not add it to an "
            + "allow-list: this set is derived precisely so that it cannot have one.");
    }

    /// <summary>
    /// The NON-VACUITY cross-check, standing in the suite rather than only in a mutation log. It asks the same
    /// question by an independent query — <b>by NAME</b> — and requires the shape predicate to agree.
    ///
    /// <para>If somebody narrows <see cref="ReturnsAScreen"/> until it selects nothing (or stops seeing the
    /// wrapper shape that <c>VoucherEntryViewModel.ForAlter</c> returns), the lock above would go green having
    /// examined nothing. This test goes red instead, and names the factory that fell out of the set.</para>
    /// </summary>
    [Fact]
    public void The_shape_predicate_finds_every_ForAlter_factory_in_the_assembly()
    {
        var byName = ViewModelTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Where(m => m.Name == "ForAlter")
            .ToList();

        Assert.NotEmpty(byName);   // the convention itself must still exist for this cross-check to mean anything

        var byShape = ScreenFactories();
        var missed = byName.Where(m => !byShape.Contains(m)).Select(Name).ToList();

        Assert.True(missed.Count == 0,
            "The shape predicate no longer selects these ForAlter factories, so the reachability lock is not "
            + "examining them:\n  " + string.Join("\n  ", missed));
    }

    /// <summary>
    /// The reachability decision is a fact about PRODUCTION code, so it must be blind to this test assembly. This
    /// pins that directly: <c>VoucherEntryViewModel.ForAlter</c> is called from several files under
    /// <c>tests/Apex.Desktop.Tests</c>, and those calls must contribute nothing.
    ///
    /// <para>Without this, the whole invariant could be satisfied by a test calling the factory — which is
    /// literally the state both shipped defects were in.</para>
    /// </summary>
    [Fact]
    public void Callers_in_the_test_assembly_do_not_count_as_production_callers()
    {
        var forAlter = typeof(VoucherEntryViewModel)
            .GetMethod(nameof(VoucherEntryViewModel.ForAlter), BindingFlags.Public | BindingFlags.Static)!;

        // This very test assembly calls it — VoucherAlterationTestBook, VoucherAlterForAlterTests,
        // VoucherAlterRefusalTests. The scanner CAN see them (so the assertion below is about the SCOPE of the
        // invariant, not about a blind spot in the reader) …
        var inTests = CallSitesIn(typeof(ViewModelAlterEntryPointReachabilityTests).Assembly, forAlter);
        Assert.NotEmpty(inTests);

        // … and none of them is counted, because the invariant only ever reads Apex.Desktop.
        var inProduction = ProductionCallSites(forAlter);
        Assert.NotEmpty(inProduction);
        Assert.Empty(inProduction.Intersect(inTests, StringComparer.Ordinal));
        Assert.All(inProduction, site => Assert.False(site.Contains("Test", StringComparison.Ordinal), site));
    }
}
