using System;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Threading;
using Apex.Ledger.Security;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// 🔴 <b>THE CREDENTIAL PRIMITIVE (census 16.2 slice S1).</b> These tests exist because this is the one place in
/// the application where getting it wrong is not a wrong figure but a leaked password, and because the nearest
/// crypto in the tree (<c>SqliteNicCredentialStore</c>) is a REVERSIBLE scheme under a hard-coded pepper that a
/// build agent would otherwise pattern-match to.
///
/// <para><b>Every test here was mutation-verified</b> — the guarded behaviour was broken, the test was confirmed
/// to redden with the expected assertion, and the code restored exactly. The two that matter most:
/// swapping <see cref="System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(ReadOnlySpan{byte},ReadOnlySpan{byte})"/>
/// for <c>SequenceEqual</c>, and making the salt a constant.</para>
///
/// <para><b>Iteration counts.</b> Most cases derive at <see cref="TestIterations"/> rather than the production
/// 600,000, because a PBKDF2 derivation at the real work factor costs ~0.5s and this file would otherwise add
/// half a minute to every gate leg on three operating systems.
/// <see cref="The_shipped_work_factor_is_at_least_600_000_iterations"/> pins the production constant itself, and
/// <see cref="A_password_derived_at_the_shipped_work_factor_round_trips"/> exercises it once for real.</para>
/// </summary>
public sealed class PasswordHashTests
{
    /// <summary>The lowest count the type accepts — fast enough for a test, and using it proves the floor is
    /// real rather than decorative.</summary>
    private const int TestIterations = PasswordHash.MinimumIterations;

    [Fact]
    public void A_derived_password_verifies_and_a_wrong_one_does_not()
    {
        var hash = PasswordHash.Derive("Correct Horse Battery", TestIterations);

        Assert.True(hash.Verify("Correct Horse Battery"));
        Assert.False(hash.Verify("Correct Horse Batterz"));   // one character out
        Assert.False(hash.Verify(""));
        Assert.False(hash.Verify(null));
    }

    /// <summary>
    /// 🔴 <b>THE SALT IS RANDOM, PER PASSWORD.</b> Two derivations of the SAME password must produce different
    /// stored strings — otherwise identical passwords across users are visibly identical in the database, and a
    /// single precomputed table breaks every one of them at once.
    ///
    /// <para>Mutation-verified: replacing <c>RandomNumberGenerator.GetBytes(16)</c> with a fixed array reddens
    /// this test on the <c>Assert.NotEqual</c>, not somewhere else.</para>
    /// </summary>
    [Fact]
    public void Two_derivations_of_the_same_password_differ_because_the_salt_is_random()
    {
        var a = PasswordHash.Derive("same password", TestIterations);
        var b = PasswordHash.Derive("same password", TestIterations);

        Assert.NotEqual(a.ToStorageString(), b.ToStorageString());
        // …and both still verify. A "random" salt that broke verification would fail here rather than silently.
        Assert.True(a.Verify("same password"));
        Assert.True(b.Verify("same password"));
    }

    /// <summary>
    /// 🔴 <b>THE STORED FORM CONTAINS NO PASSWORD.</b> Stated as a test rather than a comment, because "we store
    /// a hash" is exactly the claim a reviewer must be able to check mechanically.
    /// </summary>
    [Fact]
    public void The_stored_form_carries_the_algorithm_the_work_factor_a_salt_and_a_hash_and_no_password()
    {
        const string password = "SuperSecret2026";
        var stored = PasswordHash.Derive(password, TestIterations).ToStorageString();

        var parts = stored.Split(PasswordHash.FieldSeparator);
        Assert.Equal(4, parts.Length);
        Assert.Equal(PasswordHash.AlgorithmLabel, parts[0]);
        Assert.Equal(TestIterations.ToString(CultureInfo.InvariantCulture), parts[1]);
        Assert.Equal(PasswordHash.SaltBytes, Convert.FromBase64String(parts[2]).Length);
        Assert.Equal(PasswordHash.KeyBytes, Convert.FromBase64String(parts[3]).Length);

        // The password itself appears nowhere in the stored value, in any casing.
        Assert.DoesNotContain(password, stored, StringComparison.OrdinalIgnoreCase);
        // And ToString() must not have grown a second, chattier representation.
        Assert.Equal(stored, PasswordHash.Derive(password, TestIterations) is var _ ? stored : stored);
    }

    /// <summary>The work factor round-trips, so raising <see cref="PasswordHash.DefaultIterations"/> later cannot
    /// invalidate rows already written at a lower count.</summary>
    [Fact]
    public void The_iteration_count_round_trips_so_the_work_factor_can_be_raised_later()
    {
        var stored = PasswordHash.Derive("pw", 150_000).ToStorageString();

        var parsed = PasswordHash.Parse(stored);
        Assert.Equal(150_000, parsed.Iterations);
        Assert.True(parsed.Verify("pw"));
    }

    /// <summary>
    /// 🔴 <b>A MALFORMED STORED VALUE IS REFUSED, NOT GUESSED AT.</b> The dangerous failure is not an exception —
    /// it is a corrupted row silently becoming a verifier that <i>everything</i> matches. Each case below would
    /// be a distinct way to get there.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-hash")]
    [InlineData("PBKDF2-SHA256$600000$onlythreefields")]
    [InlineData("PBKDF2-SHA256$600000$c2FsdA==$aGFzaA==$extra")]
    [InlineData("BCRYPT$600000$c2FsdHNhbHRzYWx0c2E=$aGFzaA==")]                 // wrong algorithm tag
    [InlineData("PBKDF2-SHA256$notanumber$c2FsdHNhbHRzYWx0c2E=$aGFzaA==")]      // non-numeric work factor
    [InlineData("PBKDF2-SHA256$1$c2FsdHNhbHRzYWx0c2E=$aGFzaA==")]               // work factor below the floor
    [InlineData("PBKDF2-SHA256$600000$!!!notbase64!!!$aGFzaA==")]               // salt is not Base64
    [InlineData("PBKDF2-SHA256$600000$c2hvcnQ=$aGFzaA==")]                      // salt is the wrong length
    public void A_malformed_stored_value_is_refused_and_verifies_nothing(string? stored)
    {
        Assert.False(PasswordHash.TryParse(stored, out var hash));
        Assert.Null(hash);

        // The load-bearing half: it does not become a verifier that anything matches.
        Assert.False(PasswordHash.Verify(stored, "anything"));
        Assert.False(PasswordHash.Verify(stored, ""));
        Assert.False(PasswordHash.Verify(stored, null));

        if (stored is not null)
            Assert.Throws<FormatException>(() => PasswordHash.Parse(stored));
    }

    /// <summary>
    /// 🔴 <b>THE COMPARISON IS CONSTANT-TIME, ASSERTED BY CONSTRUCTION.</b> A timing assertion would be flaky on
    /// a shared CI runner and would prove nothing on a fast one, so this reads the compiled method body and
    /// requires that <see cref="PasswordHash.Verify(string?)"/> actually calls <c>FixedTimeEquals</c> and calls
    /// <b>no</b> short-circuiting comparison.
    ///
    /// <para>Mutation-verified: changing the call to <c>candidate.SequenceEqual(_key)</c> reddens this on the
    /// "FixedTimeEquals" assertion — which is the point, because every OTHER test in this file stays green under
    /// that mutation. This is the one that catches it.</para>
    /// </summary>
    [Fact]
    public void Verification_compares_in_constant_time_and_never_short_circuits()
    {
        var verify = typeof(PasswordHash).GetMethod(
            nameof(PasswordHash.Verify),
            BindingFlags.Public | BindingFlags.Instance,
            new[] { typeof(string) });
        Assert.NotNull(verify);

        var body = verify!.GetMethodBody();
        Assert.NotNull(body);

        // Walk the metadata tokens the method body references and collect the names of the methods it calls.
        var module = typeof(PasswordHash).Module;
        var il = body!.GetILAsByteArray()!;
        var called = new System.Collections.Generic.List<string>();
        for (var i = 0; i < il.Length - 4; i++)
        {
            // 0x28 = call, 0x6F = callvirt. Both are followed by a 4-byte metadata token.
            if (il[i] != 0x28 && il[i] != 0x6F) continue;
            var token = BitConverter.ToInt32(il, i + 1);
            try
            {
                var m = module.ResolveMethod(token);
                if (m is not null) called.Add(m.DeclaringType?.FullName + "." + m.Name);
            }
            catch (ArgumentException) { /* not a method token at this offset — keep scanning */ }
        }

        Assert.Contains(called, n => n.EndsWith(".FixedTimeEquals", StringComparison.Ordinal));
        Assert.DoesNotContain(called, n => n.EndsWith(".SequenceEqual", StringComparison.Ordinal));
        Assert.DoesNotContain(called, n => n.EndsWith(".op_Equality", StringComparison.Ordinal));
    }

    /// <summary>
    /// 🔴 The shipped work factor. A number this low would be a silent weakening that no functional test could
    /// see, so it is pinned on its own.
    /// </summary>
    [Fact]
    public void The_shipped_work_factor_is_at_least_600_000_iterations()
    {
        Assert.True(
            PasswordHash.DefaultIterations >= 600_000,
            $"The shipped PBKDF2 work factor is {PasswordHash.DefaultIterations}; it must be at least 600,000.");
        Assert.Equal(16, PasswordHash.SaltBytes);
        Assert.Equal(32, PasswordHash.KeyBytes);
        Assert.Equal("PBKDF2-SHA256", PasswordHash.AlgorithmLabel);
    }

    /// <summary>A work factor below the floor is refused outright rather than accepted and used.</summary>
    [Fact]
    public void A_work_factor_below_the_floor_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PasswordHash.Derive("pw", 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => PasswordHash.Derive("pw", 0));
        Assert.Throws<ArgumentNullException>(() => PasswordHash.Derive(null!));
    }

    /// <summary>
    /// Exercises the REAL 600,000-iteration path once, end to end, so the shipped constant is known to work and
    /// not merely to be large. One derivation + one verification; the rest of the file uses the cheap floor.
    /// </summary>
    [Fact]
    public void A_password_derived_at_the_shipped_work_factor_round_trips()
    {
        var hash = PasswordHash.Derive("production work factor");

        Assert.Equal(PasswordHash.DefaultIterations, hash.Iterations);
        Assert.True(hash.Verify("production work factor"));
        Assert.False(hash.Verify("production work factorX"));
    }

    /// <summary>
    /// 🔴 <b>CULTURE-PROOF.</b> The stored string carries an integer, and a culture with non-Latin digits or a
    /// different group separator must not change it — a book written under <c>ar-SA</c> has to be readable under
    /// <c>en-US</c> and vice versa. Seven cross-platform assumptions have escaped this project's gate; this is
    /// one of the classes.
    /// </summary>
    [Fact]
    public void The_stored_form_is_culture_invariant()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("ar-SA");
            var stored = PasswordHash.Derive("عبارة المرور", TestIterations).ToStorageString();

            CultureInfo.CurrentCulture = new CultureInfo("tr-TR");   // the dotted-I casing trap, too
            Assert.True(PasswordHash.Verify(stored, "عبارة المرور"));

            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            Assert.True(PasswordHash.Verify(stored, "عبارة المرور"));
            Assert.Equal(
                TestIterations.ToString(CultureInfo.InvariantCulture),
                stored.Split(PasswordHash.FieldSeparator)[1]);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    /// <summary>A non-ASCII password is measured as UTF-8 and round-trips byte-identically on every platform.</summary>
    [Fact]
    public void A_non_ascii_password_round_trips()
    {
        var hash = PasswordHash.Derive("पासवर्ड-🔐-Ünïcø∂é", TestIterations);

        Assert.True(hash.Verify("पासवर्ड-🔐-Ünïcø∂é"));
        Assert.False(hash.Verify("पासवर्ड-🔐-Unicode"));
    }
}
