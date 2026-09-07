using System;
using System.Linq;
using Apex.Ledger.Domain;
using Apex.Ledger.Security;
using Xunit;

namespace Apex.Ledger.Tests;

/// <summary>
/// 🔴 <b>SECURITY CONTROL — the domain (census 16.2 slice S2).</b> Levels, the Disallow/Allow rules, users, and
/// the two safeguards that make the row honest rather than decorative: <b>deny beats allow</b> and <b>the last
/// Owner cannot be deleted or demoted</b>.
///
/// <para><b>R7.</b> The two seeded names, the six access types and the level captions are all quoted from
/// help.tallysolutions.com/manage-users-in-tallyprime/ — see <see cref="SecurityControl"/> and
/// <see cref="SecurityAccessType"/> for the citations.</para>
///
/// <para>Every derivation here uses <see cref="TestIterations"/>, not the production 600,000 — see
/// <c>PasswordHashTests</c> for why.</para>
/// </summary>
public sealed class SecurityControlDomainTests
{
    private const int TestIterations = PasswordHash.MinimumIterations;
    private static readonly DateTimeOffset When = new(2026, 4, 1, 9, 0, 0, TimeSpan.FromHours(5.5));

    private static SecurityControl Seeded()
    {
        var control = new SecurityControl();
        control.SeedDefaultLevels();
        return control;
    }

    // ─────────────────────────────────────────────────────────── the two seeded levels

    /// <summary>
    /// 🔴 The vendor's two default levels, by name, verbatim. Mutation-verified: nulling either seeded name
    /// reddens THIS test on the name assertion — not with a NullReferenceException somewhere downstream.
    /// </summary>
    [Fact]
    public void Enabling_access_control_seeds_exactly_the_two_vendor_default_levels()
    {
        var control = Seeded();

        Assert.Equal(2, control.Levels.Count);
        Assert.Equal("Owner", control.Levels[0].Name);
        Assert.Equal("Data Entry Operator", control.Levels[1].Name);

        // The rights split, quoted from the vendor page.
        Assert.True(control.Levels[0].IsOwner);
        Assert.False(control.Levels[1].IsOwner);

        // The vendor's default for the back-dating allowance is 0 — "no back-dating", not "unlimited".
        Assert.Equal(0, control.Levels[0].DaysAllowedForBackDatedVouchers);
        Assert.Equal(0, control.Levels[1].DaysAllowedForBackDatedVouchers);

        // Nothing is pre-restricted: a seeded facility rule the vendor does not publish would be invented.
        Assert.Empty(control.Levels[1].Rules);
    }

    /// <summary>Seeding is idempotent — enabling the gate twice cannot produce four levels.</summary>
    [Fact]
    public void Seeding_twice_does_not_duplicate_the_default_levels()
    {
        var control = Seeded();
        control.SeedDefaultLevels();

        Assert.Equal(2, control.Levels.Count);
    }

    /// <summary>An untouched control is empty, so a company that never enables access control persists
    /// byte-identically to a pre-v56 one (ER-13).</summary>
    [Fact]
    public void An_untouched_control_is_empty_so_an_existing_company_is_unchanged()
    {
        var control = new SecurityControl();

        Assert.True(control.IsEmpty);
        Assert.Empty(control.Levels);
        Assert.Empty(control.Users);
        Assert.False(control.PasswordPolicy.IsConfigured);
    }

    // ─────────────────────────────────────────────────────────── the six access types and the rule algebra

    /// <summary>The six access types the vendor's screen offers, and no seventh.</summary>
    [Fact]
    public void Six_access_types_are_offered_exactly_as_the_vendor_lists_them()
    {
        Assert.Equal(
            new[] { "FullAccess", "Create", "Alter", "Display", "Print", "Preview" },
            Enum.GetNames<SecurityAccessType>());

        // The ordinals are PERSISTED (security_level_rules.access_type). Renumbering would re-interpret every
        // stored rule in every existing book, so they are pinned here as well as documented.
        Assert.Equal(0, (int)SecurityAccessType.FullAccess);
        Assert.Equal(5, (int)SecurityAccessType.Preview);
    }

    /// <summary>🔴 A level with no rules denies NOTHING — the load-bearing half, because the opposite reading
    /// would silently lock a freshly created level out of everything with no screen showing it.</summary>
    [Fact]
    public void A_level_with_no_rules_denies_nothing()
    {
        var level = new SecurityLevel("Clerk");

        foreach (var access in Enum.GetValues<SecurityAccessType>())
            Assert.True(level.IsAllowed("Day Book", access));
    }

    /// <summary>🔴 Deny beats allow, whatever the order and however many rows allow.</summary>
    [Fact]
    public void Deny_beats_allow_regardless_of_order()
    {
        var allowFirst = new SecurityLevel("Clerk");
        allowFirst.Rules.Add(new SecurityAccessRule("Balance Sheet", SecurityAccessType.Display, disallowed: false));
        allowFirst.Rules.Add(new SecurityAccessRule("Balance Sheet", SecurityAccessType.Display, disallowed: true));

        var denyFirst = new SecurityLevel("Clerk");
        denyFirst.Rules.Add(new SecurityAccessRule("Balance Sheet", SecurityAccessType.Display, disallowed: true));
        denyFirst.Rules.Add(new SecurityAccessRule("Balance Sheet", SecurityAccessType.Display, disallowed: false));

        Assert.False(allowFirst.IsAllowed("Balance Sheet", SecurityAccessType.Display));
        Assert.False(denyFirst.IsAllowed("Balance Sheet", SecurityAccessType.Display));
    }

    /// <summary>A Full Access disallow covers every access type below it; a narrower one covers only itself.</summary>
    [Fact]
    public void A_full_access_rule_covers_every_access_type_and_a_narrow_one_covers_only_itself()
    {
        var broad = new SecurityLevel("Clerk");
        broad.Rules.Add(new SecurityAccessRule("Trial Balance", SecurityAccessType.FullAccess, disallowed: true));

        var narrow = new SecurityLevel("Clerk");
        narrow.Rules.Add(new SecurityAccessRule("Trial Balance", SecurityAccessType.Print, disallowed: true));

        foreach (var access in Enum.GetValues<SecurityAccessType>())
            Assert.False(broad.IsAllowed("Trial Balance", access));

        Assert.False(narrow.IsAllowed("Trial Balance", SecurityAccessType.Print));
        Assert.True(narrow.IsAllowed("Trial Balance", SecurityAccessType.Display));
        Assert.True(narrow.IsAllowed("Trial Balance", SecurityAccessType.Preview));
    }

    /// <summary>Facility matching is case-insensitive and culture-invariant — a rule typed in one casing must
    /// hold under a Turkish culture too, where "I".ToLower() is not "i".</summary>
    [Fact]
    public void Facility_matching_is_case_insensitive_and_culture_invariant()
    {
        var original = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("tr-TR");
            var level = new SecurityLevel("Clerk");
            level.Rules.Add(new SecurityAccessRule("INVENTORY BOOKS", SecurityAccessType.FullAccess, true));

            Assert.False(level.IsAllowed("Inventory Books", SecurityAccessType.Display));
            Assert.False(level.IsAllowed("inventory books", SecurityAccessType.Display));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = original;
        }
    }

    /// <summary>🔴 No rule can restrict an Owner level. That is what makes the last-Owner safeguard meaningful:
    /// an Owner nobody could reach anything with would be no safeguard at all.</summary>
    [Fact]
    public void An_owner_level_cannot_be_restricted_by_a_rule()
    {
        var owner = new SecurityLevel("Owner", isOwner: true);
        owner.Rules.Add(new SecurityAccessRule("Balance Sheet", SecurityAccessType.FullAccess, disallowed: true));

        Assert.True(owner.IsAllowed("Balance Sheet", SecurityAccessType.FullAccess));
    }

    // ─────────────────────────────────────────────────────────── back-dating limits

    /// <summary>The vendor's two back-dating fields, both enforced, either able to refuse on its own.</summary>
    [Fact]
    public void Back_dating_is_bounded_by_the_day_allowance_and_by_the_cut_off_date()
    {
        var today = new DateOnly(2026, 6, 30);
        var level = new SecurityLevel("Data Entry Operator")
        {
            DaysAllowedForBackDatedVouchers = 7,
            CutOffDateForBackDatedVouchers = new DateOnly(2026, 6, 1),
        };

        Assert.True(level.AllowsVoucherDate(today, today));                          // today
        Assert.True(level.AllowsVoucherDate(new DateOnly(2026, 6, 23), today));      // 7 days back — the edge
        Assert.False(level.AllowsVoucherDate(new DateOnly(2026, 6, 22), today));     // 8 days back
        Assert.False(level.AllowsVoucherDate(new DateOnly(2026, 5, 31), today));     // before the cut-off
        Assert.True(level.AllowsVoucherDate(new DateOnly(2026, 7, 5), today));       // forward-dated
    }

    /// <summary>The default allowance of 0 means "no back-dating", not "unlimited" — the difference is a real
    /// product behaviour and the vendor's default is 0.</summary>
    [Fact]
    public void The_default_day_allowance_of_zero_means_no_back_dating()
    {
        var today = new DateOnly(2026, 6, 30);
        var level = new SecurityLevel("Data Entry Operator");

        Assert.True(level.AllowsVoucherDate(today, today));
        Assert.False(level.AllowsVoucherDate(today.AddDays(-1), today));
    }

    // ─────────────────────────────────────────────────────────── users

    [Fact]
    public void A_user_holds_exactly_one_level_and_a_duplicate_name_is_refused()
    {
        var control = Seeded();
        var owner = control.Levels[0];

        Assert.True(control.AddUser(new CompanyUser("asha", owner.Id), out _));
        Assert.False(control.AddUser(new CompanyUser("ASHA", owner.Id), out var message));
        Assert.Contains("already exists", message, StringComparison.OrdinalIgnoreCase);
        Assert.Single(control.Users);

        Assert.False(control.AddUser(new CompanyUser("  ", owner.Id), out _));
        Assert.False(control.AddUser(new CompanyUser("bala", Guid.NewGuid()), out var noLevel));
        Assert.Contains("does not exist", noLevel, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 🔴 <b>THE LAST OWNER CANNOT BE DELETED.</b> Passwords are one-way, so with no Owner left nobody can ever
    /// create one — the company would be administratively dead while still opening.
    /// </summary>
    [Fact]
    public void The_last_owner_user_cannot_be_removed()
    {
        var control = Seeded();
        var owner = control.Levels[0];
        var clerkLevel = control.Levels[1];

        var admin = new CompanyUser("admin", owner.Id);
        Assert.True(control.AddUser(admin, out _));
        var clerk = new CompanyUser("clerk", clerkLevel.Id);
        Assert.True(control.AddUser(clerk, out _));

        Assert.False(control.RemoveUser(admin.Id, out var refused));
        Assert.Equal(SecurityControl.LastOwnerGuard, refused);
        Assert.Contains(admin, control.Users);

        // The non-owner goes without argument.
        Assert.True(control.RemoveUser(clerk.Id, out _));

        // A SECOND owner unblocks the first.
        var second = new CompanyUser("admin2", owner.Id);
        Assert.True(control.AddUser(second, out _));
        Assert.True(control.RemoveUser(admin.Id, out _));
        Assert.Single(control.Users);
    }

    /// <summary>
    /// 🔴 <b>AND THE LAST OWNER CANNOT BE DEMOTED.</b> A delete-only guard leaves exactly this hole: demoting the
    /// sole Owner locks the company just as completely as deleting them.
    /// </summary>
    [Fact]
    public void The_last_owner_user_cannot_be_demoted()
    {
        var control = Seeded();
        var owner = control.Levels[0];
        var clerkLevel = control.Levels[1];

        var admin = new CompanyUser("admin", owner.Id);
        Assert.True(control.AddUser(admin, out _));

        Assert.False(control.ChangeUserLevel(admin.Id, clerkLevel.Id, out var refused));
        Assert.Equal(SecurityControl.LastOwnerGuard, refused);
        Assert.Equal(owner.Id, admin.SecurityLevelId);

        // Moving the sole Owner to ANOTHER owner level is fine — the constraint is on owners, not on identity.
        var secondOwner = new SecurityLevel("Proprietor", isOwner: true);
        Assert.True(control.AddLevel(secondOwner, out _));
        Assert.True(control.ChangeUserLevel(admin.Id, secondOwner.Id, out _));
        Assert.Equal(secondOwner.Id, admin.SecurityLevelId);
    }

    // ─────────────────────────────────────────────────────────── passwords, R13

    /// <summary>🔴 A set password is stored as a one-way verifier and the plaintext appears nowhere on the user.</summary>
    [Fact]
    public void A_user_password_is_stored_only_as_a_one_way_verifier()
    {
        var user = new CompanyUser("asha", Guid.NewGuid());
        Assert.False(user.HasPassword);

        user.SetPassword("Monsoon#2026", When, TestIterations);

        Assert.True(user.HasPassword);
        Assert.NotNull(user.StoredPasswordVerifier);
        Assert.StartsWith(PasswordHash.AlgorithmLabel, user.StoredPasswordVerifier!, StringComparison.Ordinal);
        Assert.DoesNotContain("Monsoon#2026", user.StoredPasswordVerifier!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(When, user.PasswordSetOn);

        Assert.True(user.VerifyPassword("Monsoon#2026"));
        Assert.False(user.VerifyPassword("monsoon#2026"));
        Assert.False(user.VerifyPassword(null));
    }

    /// <summary>🔴 A user with NO password verifies the empty string and NOTHING else — never "anything matches",
    /// which is the failure mode that turns an absent password into an open door.</summary>
    [Fact]
    public void A_user_with_no_password_matches_only_the_empty_string()
    {
        var user = new CompanyUser("guest", Guid.NewGuid());

        Assert.True(user.VerifyPassword(""));
        Assert.False(user.VerifyPassword("anything"));
        Assert.False(user.VerifyPassword(null));
    }

    /// <summary>A corrupted verifier is refused on restore rather than becoming a user nothing (or everything)
    /// can sign in as.</summary>
    [Fact]
    public void Restoring_a_malformed_verifier_is_refused()
    {
        var user = new CompanyUser("asha", Guid.NewGuid());

        Assert.Throws<FormatException>(() => user.RestorePasswordVerifier("garbage", When));
        Assert.False(user.HasPassword);

        user.RestorePasswordVerifier(null, null);   // "no password set" is legitimate
        Assert.False(user.HasPassword);
    }

    /// <summary>
    /// 🔴 Sign-in fails with the SAME message for an unknown user and a wrong password — otherwise the screen is
    /// a user-name oracle that tells an attacker which names exist.
    /// </summary>
    [Fact]
    public void Authentication_does_not_distinguish_an_unknown_user_from_a_wrong_password()
    {
        var control = Seeded();
        var admin = new CompanyUser("admin", control.Levels[0].Id);
        Assert.True(control.AddUser(admin, out _));
        admin.SetPassword("right", When, TestIterations);

        Assert.NotNull(control.Authenticate("admin", "right", out var ok));
        Assert.Empty(ok);

        Assert.Null(control.Authenticate("admin", "wrong", out var wrongPassword));
        Assert.Null(control.Authenticate("nosuchuser", "right", out var unknownUser));
        Assert.Equal(unknownUser, wrongPassword);

        // A deactivated user is refused with the same message, too.
        admin.IsActive = false;
        Assert.Null(control.Authenticate("admin", "right", out var inactive));
        Assert.Equal(unknownUser, inactive);
    }

    // ─────────────────────────────────────────────────────────── password policy

    [Fact]
    public void The_password_policy_enforces_a_minimum_length_and_an_expiry_interval()
    {
        var policy = new PasswordPolicy();
        Assert.False(policy.IsConfigured);
        Assert.True(policy.SatisfiesStrength(""));                 // off by default
        Assert.False(policy.IsExpired(When, When.AddYears(10)));   // never expires by default

        policy.MinimumLength = 8;
        policy.ExpiryDays = 90;
        Assert.True(policy.IsConfigured);
        Assert.False(policy.SatisfiesStrength("short"));
        Assert.True(policy.SatisfiesStrength("longenough"));
        Assert.False(policy.SatisfiesStrength(null));

        Assert.False(policy.IsExpired(When, When.AddDays(89)));
        Assert.True(policy.IsExpired(When, When.AddDays(90)));
        Assert.False(policy.IsExpired(setOn: null, When.AddDays(900)));   // no password set → nothing to expire
    }

    /// <summary>Length is counted in TEXT ELEMENTS, so an emoji is one character everywhere rather than two on
    /// some platforms — a cross-platform trap this project has been bitten by in other shapes.</summary>
    [Fact]
    public void Minimum_length_counts_text_elements_not_utf16_code_units()
    {
        var policy = new PasswordPolicy { MinimumLength = 4 };

        Assert.False(policy.SatisfiesStrength("🔐🔐"));    // 2 elements, 4 UTF-16 units
        Assert.True(policy.SatisfiesStrength("🔐🔐🔐🔐"));  // 4 elements
    }

    /// <summary>The policy gates <see cref="SecurityControl.SetUserPassword"/>, so a screen cannot bypass it.</summary>
    [Fact]
    public void The_policy_refuses_a_password_below_the_minimum_length()
    {
        var control = Seeded();
        control.PasswordPolicy.MinimumLength = 10;
        var admin = new CompanyUser("admin", control.Levels[0].Id);
        Assert.True(control.AddUser(admin, out _));

        Assert.False(control.SetUserPassword(admin.Id, "short", When, out var refused, TestIterations));
        Assert.Contains("10", refused, StringComparison.Ordinal);
        Assert.False(admin.HasPassword);

        Assert.True(control.SetUserPassword(admin.Id, "longenoughpassword", When, out _, TestIterations));
        Assert.True(admin.VerifyPassword("longenoughpassword"));
    }

    /// <summary>
    /// 🔴 The two notices this row is obliged to state are DOMAIN CONSTANTS with content, so the screen and the
    /// documentation cannot drift apart and neither can be quietly emptied. And — a hard project rule — neither
    /// names the reference product.
    /// </summary>
    [Fact]
    public void The_architectural_limit_and_the_lockout_warning_are_stated_and_carry_no_vendor_brand()
    {
        Assert.Contains("does not protect the company file", SecurityControl.ArchitecturalLimitNotice,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("cannot be recovered", SecurityControl.LockoutWarningNotice,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("cannot be opened again", SecurityControl.LockoutWarningNotice,
            StringComparison.OrdinalIgnoreCase);

        // 🔴 And the scope limit: the rules are RECORDED, not yet ENFORCED. Nothing in src/ consults a level
        // before opening a screen, and there is no sign-in step — so the screen says so, and this pins it.
        Assert.Contains("does not yet ask for a sign-in", SecurityControl.EnforcementNotice,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("does not block anyone yet", SecurityControl.EnforcementNotice,
            StringComparison.OrdinalIgnoreCase);

        foreach (var notice in new[]
                 {
                     SecurityControl.ArchitecturalLimitNotice,
                     SecurityControl.LockoutWarningNotice,
                     SecurityControl.EnforcementNotice,
                     SecurityControl.LastOwnerGuard,
                     SecurityControl.OwnerLevelName,
                     SecurityControl.DataEntryLevelName,
                 })
            Assert.DoesNotContain("Tally", notice, StringComparison.OrdinalIgnoreCase);
    }
}
