using System;
using System.Collections.Generic;
using System.Linq;

namespace Apex.Ledger.Domain;

/// <summary>
/// The vendor's <b>Password Policy</b> settings (census 16.2), reached at <b>Alt+K (Company) &gt; Password
/// Policy</b>.
///
/// <para><b>R7 — what is attested and what is OURS, stated separately because they are not the same.</b> The
/// vendor names the FEATURE and its two capabilities, verbatim
/// (help.tallysolutions.com/manage-users-in-tallyprime/): <i>"Through the Password Policy feature, you can enable
/// supplementary choices for passwords, including enhancing password strength and setting password expiration
/// intervals."</i> <b>The vendor does NOT publish the individual field captions or their default values.</b> So
/// the two knobs below are OUR concretisation of the two capabilities the vendor names — a minimum length for
/// "strength", a day count for "expiration intervals" — and they are recorded as a divergence in
/// <c>docs/invented-vs-cloned.md</c> rather than presented as cloned captions. Both default to OFF, which is what
/// every existing company was.</para>
/// </summary>
public sealed class PasswordPolicy
{
    /// <summary>The minimum number of characters a new password must have. <b>0 = no minimum</b> (the default,
    /// and what every pre-v56 company was). OURS, not a vendor caption — see the type remarks.</summary>
    public int MinimumLength { get; set; }

    /// <summary>How many days a password stays valid, or <c>null</c> = never expires (the default). OURS, not a
    /// vendor caption — see the type remarks.</summary>
    public int? ExpiryDays { get; set; }

    /// <summary>True when this policy is doing anything at all. A policy that is off is byte-identical to a
    /// pre-v56 company (ER-13).</summary>
    public bool IsConfigured => MinimumLength > 0 || ExpiryDays is > 0;

    /// <summary>Whether <paramref name="password"/> satisfies <see cref="MinimumLength"/>. Length is counted in
    /// text elements, not UTF-16 code units, so an emoji or a Devanagari cluster counts as one character on every
    /// platform rather than two on some.</summary>
    public bool SatisfiesStrength(string? password)
    {
        if (MinimumLength <= 0) return true;
        if (password is null) return false;
        var elements = new System.Globalization.StringInfo(password).LengthInTextElements;
        return elements >= MinimumLength;
    }

    /// <summary>
    /// Whether a password set at <paramref name="setOn"/> has expired as of <paramref name="asOf"/>. Never
    /// expired when <see cref="ExpiryDays"/> is null/zero, or when no password has been set.
    /// <paramref name="asOf"/> is a parameter, not a clock read — the domain stays deterministic.
    /// </summary>
    public bool IsExpired(DateTimeOffset? setOn, DateTimeOffset asOf)
    {
        if (ExpiryDays is not > 0) return false;
        if (setOn is not { } when) return false;
        return asOf >= when.AddDays(ExpiryDays.Value);
    }
}

/// <summary>
/// 🔴 <b>SECURITY CONTROL (census row 16.2)</b> — the company's user access control: the master switch, the
/// security levels, the users holding them, and the password policy. Hung off
/// <see cref="Company.Security"/>, always present and empty-by-default, so a company that never enables access
/// control serialises byte-identically to a pre-v56 company (ER-13).
///
/// <para><b>R7 — the route and the gate, verbatim.</b> <i>"Press Alt+K (Company) &gt; Users and Passwords. The
/// Users for Company screen will appear."</i> and <i>"Press Alt+K (Company) &gt; Password Policy"</i>
/// (help.tallysolutions.com/manage-users-in-tallyprime/); the company gate is
/// <i>"press F12 (Configure) &gt; set … Use User Access Control … to Yes"</i>
/// (help.tallysolutions.com/tallyvault-for-company-tally/) — <see cref="Company.UseUserAccessControl"/>. The two
/// seeded level names are the same page's <i>"TallyPrime provides two default security levels: owner and data
/// entry operator."</i></para>
///
/// <para>🔴 <b>THE DIVERGENCE THIS ROW MUST DECLARE, AND THE SCREEN SAYS IT TOO.</b> A local desktop application
/// storing its own users in its own SQLite file <b>cannot enforce access control against anyone who can read that
/// file</b>. This is a control between colleagues at the same keyboard; it is NOT a control against file access,
/// disk access, or a copied database. The reference product has the same property. Grading this row "COMPLETE"
/// without that sentence in front of the operator would be a promise the architecture cannot keep — hence
/// <see cref="ArchitecturalLimitNotice"/>, which the Users screen renders and a test pins.</para>
///
/// <para>🔴 <b>THE IRRECOVERABLE-LOCKOUT CONSEQUENCE, SAID PLAINLY.</b> Passwords are stored as one-way
/// PBKDF2-HMAC-SHA256 verifiers (<see cref="Security.PasswordHash"/>) and there is no reset path, no master key
/// and no back door. <see cref="LastOwnerGuard"/> keeps at least one Owner user in EXISTENCE, but nothing can
/// recover that Owner's forgotten PASSWORD. The vendor is in the same position and says so: if both the Admin and
/// the user password are forgotten the only remedy is to <i>"restore earlier backup in which you have not
/// mentioned the password"</i>
/// (help.tallysolutions.com/tally-prime/access-control-data-security/data-security-faq/).</para>
///
/// <para>Pure data + pure verbs — framework-, DB-, clock- and RNG-free except where a caller passes the moment in.</para>
/// </summary>
public sealed class SecurityControl
{
    /// <summary>The first seeded level's name. The vendor's prose spells it lower-case in a sentence
    /// (<i>"owner and data entry operator"</i>); the screen shows it capitalised, which is what an operator reads,
    /// so that is what is seeded.</summary>
    public const string OwnerLevelName = "Owner";

    /// <summary>The second seeded level's name — the vendor's <i>"data entry operator"</i>, capitalised as the
    /// screen shows it.</summary>
    public const string DataEntryLevelName = "Data Entry Operator";

    /// <summary>The Owner level's rights, quoted from the vendor page so the seeded description is not invented:
    /// <i>"access all data, manage security settings, create and update user profiles"</i>.</summary>
    public const string OwnerRightsDescription =
        "Access all data, manage security settings, create and update user profiles.";

    /// <summary>The Data Entry Operator level's rights, quoted from the vendor page: <i>"record vouchers, view
    /// specific reports, and perform related tasks"</i>.</summary>
    public const string DataEntryRightsDescription =
        "Record vouchers, view specific reports, and perform related tasks.";

    /// <summary>
    /// 🔴 The architectural limit, in one sentence the operator actually reads. See the type remarks for why this
    /// is a constant with a test on it rather than a comment in a design document.
    /// </summary>
    public const string ArchitecturalLimitNotice =
        "This controls who may work at this keyboard. It does not protect the company file itself — anyone who "
        + "can read the file on disk can read the data, whatever is set here.";

    /// <summary>
    /// 🔴 <b>THE SCOPE LIMIT OF THIS SLICE, ON THE SCREEN.</b> Users, levels and facility rules are captured,
    /// stored and answerable — <see cref="SecurityLevel.IsAllowed"/> and
    /// <see cref="SecurityLevel.AllowsVoucherDate"/> decide correctly, and <see cref="Authenticate"/> verifies
    /// correctly — but <b>nothing in the application yet CONSULTS them</b>: there is no sign-in step, and no
    /// report, master or voucher screen asks a level for permission before opening. Saying so on the screen is
    /// the difference between a configuration screen and a security theatre; an operator who restricted a report
    /// here and was not told would believe it was restricted.
    ///
    /// <para>This is exactly why census row 16.2 is graded <b>PARTIAL</b> and not COMPLETE, and it is the next
    /// slice's work, not a defect in this one.</para>
    /// </summary>
    public const string EnforcementNotice =
        "Users, levels and access rules are saved here, but this build does not yet ask for a sign-in or check "
        + "these rules before opening a screen. Setting a rule records it; it does not block anyone yet.";

    /// <summary>
    /// 🔴 The lockout consequence, in one sentence the operator reads BEFORE they set a password. Passwords are
    /// stored one-way and cannot be recovered by anyone, including us.
    /// </summary>
    public const string LockoutWarningNotice =
        "Passwords are stored one-way and cannot be recovered. If the last Owner's password is lost, this company "
        + "cannot be opened again — restore a backup taken before the password was set.";

    /// <summary>The security levels defined for this company, in the order they were created.</summary>
    public List<SecurityLevel> Levels { get; } = new();

    /// <summary>The users defined for this company, in the order they were created.</summary>
    public List<CompanyUser> Users { get; } = new();

    /// <summary>The company's password policy. Always present; off by default.</summary>
    public PasswordPolicy PasswordPolicy { get; set; } = new();

    /// <summary>True when nothing has been configured — no level, no user, no policy. Such a company persists
    /// byte-identically to a pre-v56 one (ER-13).</summary>
    public bool IsEmpty => Levels.Count == 0 && Users.Count == 0 && !PasswordPolicy.IsConfigured;

    /// <summary>The message <see cref="RemoveUser"/> and <see cref="ChangeUserLevel"/> refuse with. Named once so
    /// the verbs, the screen and the tests cannot drift apart.</summary>
    public const string LastOwnerGuard =
        "This is the last user holding an Owner level. Removing or demoting it would leave nobody able to manage "
        + "users, and passwords cannot be recovered. Create another Owner user first.";

    /// <summary>
    /// Seeds the two default levels the vendor ships — <see cref="OwnerLevelName"/> and
    /// <see cref="DataEntryLevelName"/> — if and only if this control has none. Idempotent: calling it on a
    /// company that already has levels changes nothing, so enabling access control twice cannot duplicate them.
    /// Returns the levels now present.
    /// </summary>
    public IReadOnlyList<SecurityLevel> SeedDefaultLevels()
    {
        if (Levels.Count > 0) return Levels;

        Levels.Add(new SecurityLevel(OwnerLevelName, isOwner: true));
        // Days Allowed for Back Dated Vouchers defaults to 0 on both — the vendor's default, meaning "no
        // back-dating", NOT "unlimited". Nothing else is pre-restricted: a seeded level with a facility rule the
        // vendor does not publish would be an invented restriction.
        Levels.Add(new SecurityLevel(DataEntryLevelName));
        return Levels;
    }

    /// <summary>The level of <paramref name="id"/>, or <c>null</c>.</summary>
    public SecurityLevel? LevelById(Guid id) => Levels.FirstOrDefault(l => l.Id == id);

    /// <summary>The level named <paramref name="name"/> (case-insensitive, invariant), or <c>null</c>.</summary>
    public SecurityLevel? LevelByName(string? name) =>
        name is null ? null : Levels.FirstOrDefault(l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>The user named <paramref name="name"/> (case-insensitive, invariant), or <c>null</c>.</summary>
    public CompanyUser? UserByName(string? name) =>
        name is null ? null : Users.FirstOrDefault(u => string.Equals(u.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>The users currently holding an <see cref="SecurityLevel.IsOwner"/> level.</summary>
    public IReadOnlyList<CompanyUser> OwnerUsers =>
        Users.Where(u => LevelById(u.SecurityLevelId) is { IsOwner: true }).ToList();

    /// <summary>
    /// Adds a level. Refuses a blank name, a duplicate name (case-insensitive) and a negative day allowance —
    /// each refusal returns <c>false</c> with the reason in <paramref name="message"/> rather than throwing,
    /// because the caller is a screen.
    /// </summary>
    public bool AddLevel(SecurityLevel level, out string message)
    {
        ArgumentNullException.ThrowIfNull(level);

        if (string.IsNullOrWhiteSpace(level.Name))
        {
            message = "A security level needs a name.";
            return false;
        }
        if (LevelByName(level.Name) is not null)
        {
            message = $"A security level named '{level.Name.Trim()}' already exists.";
            return false;
        }
        if (level.DaysAllowedForBackDatedVouchers < 0)
        {
            message = "Days Allowed for Back Dated Vouchers cannot be negative.";
            return false;
        }

        level.Name = level.Name.Trim();
        Levels.Add(level);
        message = $"Security level '{level.Name}' created.";
        return true;
    }

    /// <summary>
    /// Adds a user holding <paramref name="user"/>'s level. Refuses a blank name, a duplicate name
    /// (case-insensitive) and a level that does not exist in this company.
    /// </summary>
    public bool AddUser(CompanyUser user, out string message)
    {
        ArgumentNullException.ThrowIfNull(user);

        if (string.IsNullOrWhiteSpace(user.Name))
        {
            message = "A user needs a name.";
            return false;
        }
        if (UserByName(user.Name) is not null)
        {
            message = $"A user named '{user.Name.Trim()}' already exists.";
            return false;
        }
        if (LevelById(user.SecurityLevelId) is null)
        {
            message = "That security level does not exist in this company.";
            return false;
        }

        user.Name = user.Name.Trim();
        Users.Add(user);
        message = $"User '{user.Name}' created.";
        return true;
    }

    /// <summary>
    /// 🔴 Removes a user, <b>refusing to remove the last one holding an Owner level</b>. The safeguard is real
    /// rather than cosmetic precisely because passwords cannot be recovered: with no Owner user left, nobody can
    /// ever create one.
    /// </summary>
    public bool RemoveUser(Guid userId, out string message)
    {
        var user = Users.FirstOrDefault(u => u.Id == userId);
        if (user is null)
        {
            message = "No such user.";
            return false;
        }

        var owners = OwnerUsers;
        if (owners.Count == 1 && owners[0].Id == userId)
        {
            message = LastOwnerGuard;
            return false;
        }

        Users.Remove(user);
        message = $"User '{user.Name}' removed.";
        return true;
    }

    /// <summary>
    /// 🔴 Moves a user to another level, <b>refusing to demote the last Owner</b> — the same safeguard as
    /// <see cref="RemoveUser"/>, and it must be here too: demoting the sole Owner locks the company just as
    /// completely as deleting them, which is exactly the hole a delete-only guard leaves.
    /// </summary>
    public bool ChangeUserLevel(Guid userId, Guid newLevelId, out string message)
    {
        var user = Users.FirstOrDefault(u => u.Id == userId);
        if (user is null)
        {
            message = "No such user.";
            return false;
        }
        if (LevelById(newLevelId) is not { } target)
        {
            message = "That security level does not exist in this company.";
            return false;
        }

        var owners = OwnerUsers;
        if (!target.IsOwner && owners.Count == 1 && owners[0].Id == userId)
        {
            message = LastOwnerGuard;
            return false;
        }

        user.SecurityLevelId = newLevelId;
        message = $"User '{user.Name}' moved to '{target.Name}'.";
        return true;
    }

    /// <summary>
    /// Sets <paramref name="user"/>'s password after checking it against <see cref="PasswordPolicy"/>. Refuses a
    /// password shorter than the policy's minimum; never says what the stored password was, because it cannot.
    /// </summary>
    public bool SetUserPassword(
        Guid userId,
        string password,
        DateTimeOffset setOn,
        out string message,
        int iterations = Security.PasswordHash.DefaultIterations)
    {
        var user = Users.FirstOrDefault(u => u.Id == userId);
        if (user is null)
        {
            message = "No such user.";
            return false;
        }
        if (!PasswordPolicy.SatisfiesStrength(password))
        {
            message = $"The password policy requires at least {PasswordPolicy.MinimumLength} characters.";
            return false;
        }

        user.SetPassword(password ?? string.Empty, setOn, iterations);
        message = $"Password set for '{user.Name}'.";
        return true;
    }

    /// <summary>
    /// Signs a user in: name match (case-insensitive), active, and a constant-time password check. Returns the
    /// user on success and <c>null</c> otherwise, with a DELIBERATELY UNIFORM failure message — "unknown user"
    /// and "wrong password" must not be distinguishable, or the screen becomes a user-name oracle.
    /// </summary>
    public CompanyUser? Authenticate(string? userName, string? password, out string message)
    {
        var user = UserByName(userName);
        var ok = user is { IsActive: true } && user.VerifyPassword(password);
        if (!ok)
        {
            message = "That user name and password do not match.";
            return null;
        }

        message = string.Empty;
        return user;
    }
}
