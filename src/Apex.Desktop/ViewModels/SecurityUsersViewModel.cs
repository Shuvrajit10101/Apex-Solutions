using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Apex.Desktop.Services;
using Apex.Ledger.Domain;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Apex.Desktop.ViewModels;

/// <summary>One row of the Users list — the user, the level they hold, and whether a password is set.</summary>
public sealed class SecurityUserRow
{
    public SecurityUserRow(CompanyUser user, string levelName)
    {
        User = user;
        LevelName = levelName;
    }

    public CompanyUser User { get; }

    /// <summary>The user name as typed.</summary>
    public string UserName => User.Name;

    /// <summary>The name of the security level this user holds.</summary>
    public string LevelName { get; }

    /// <summary>🔴 Says only WHETHER a password is set — never anything about it. There is no member anywhere
    /// that could reveal one; the stored value is a one-way PBKDF2 verifier.</summary>
    public string PasswordState => User.HasPassword ? "Set" : "Not set";

    /// <summary>"Yes"/"No" — whether the user may sign in.</summary>
    public string ActiveState => User.IsActive ? "Yes" : "No";
}

/// <summary>One row of the security-level list.</summary>
public sealed class SecurityLevelRow
{
    public SecurityLevelRow(SecurityLevel level, int userCount)
    {
        Level = level;
        UserCount = userCount;
    }

    public SecurityLevel Level { get; }

    public string LevelName => Level.Name;

    /// <summary>"Owner" for a level that reaches everything, otherwise a blank — the operator's cue that a level
    /// cannot be restricted.</summary>
    public string OwnerState => Level.IsOwner ? "Owner" : string.Empty;

    /// <summary>The vendor's "Days Allowed for Back Dated Vouchers", rendered culture-invariantly.</summary>
    public string BackDatedDays =>
        Level.DaysAllowedForBackDatedVouchers.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>How many users hold this level.</summary>
    public int UserCount { get; }
}

/// <summary>
/// 🔴 <b>THE "USERS FOR COMPANY" SCREEN (census row 16.2), reached at Alt+K (Company) &gt; Users and
/// Passwords.</b> Vendor, verbatim (help.tallysolutions.com/manage-users-in-tallyprime/): <i>"Press Alt+K
/// (Company) &gt; Users and Passwords. The Users for Company screen will appear."</i>
///
/// <para>It hosts the master switch (<see cref="UseUserAccessControl"/> — the vendor's F12 "Use User Access
/// Control"), the two seeded security levels (<b>Owner</b> and <b>Data Entry Operator</b>), the users holding
/// them, and the two notices this row is obliged to put in front of the operator.</para>
///
/// <para>🔴 <b>R13 — NO PASSWORD EVER LEAVES THIS SCREEN, AND NONE IS HELD BY IT AFTER SAVE.</b>
/// <see cref="NewPassword"/> is a transient form field; the moment <see cref="CreateUser"/> or
/// <see cref="SetPasswordForSelectedUser"/> runs, it is hashed into a one-way PBKDF2-HMAC-SHA256 verifier
/// (<c>Apex.Ledger.Security.PasswordHash</c>) and the field is CLEARED. The list shows only whether a password
/// is set. There is no "show password", no reveal, no recovery — because there is nothing to reveal.</para>
///
/// <para>🔴 <b>THE TWO NOTICES ARE NOT DECORATION.</b>
/// <see cref="SecurityControl.ArchitecturalLimitNotice"/> says that this controls who works at the keyboard and
/// NOT who can read the file, which is the honest limit of a local SQLite application.
/// <see cref="SecurityControl.LockoutWarningNotice"/> says a lost Owner password locks the company for good.
/// Both are rendered on the screen and both are pinned by tests, because "Security Control · COMPLETE" without
/// them reads as a promise the architecture cannot keep.</para>
/// </summary>
public sealed partial class SecurityUsersViewModel : ViewModelBase
{
    private readonly CompanyStorage _storage;
    private readonly Company _company;
    private readonly Func<DateTimeOffset> _now;

    public string Title => "Users for Company";

    /// <summary>The company these users belong to.</summary>
    public string CompanyName => _company.Name;

    /// <summary>🔴 The architectural limit, verbatim from the domain constant so the screen and the doc cannot
    /// drift. See the type remarks.</summary>
    public string LimitNotice => SecurityControl.ArchitecturalLimitNotice;

    /// <summary>🔴 The irrecoverable-lockout warning, verbatim from the domain constant. See the type remarks.</summary>
    public string LockoutNotice => SecurityControl.LockoutWarningNotice;

    /// <summary>🔴 The scope limit of this slice: the rules are RECORDED, not yet ENFORCED. See
    /// <see cref="SecurityControl.EnforcementNotice"/> for why that sentence is on the screen and not only in a
    /// comment.</summary>
    public string EnforcementNotice => SecurityControl.EnforcementNotice;

    /// <summary>The vendor's F12 "Use User Access Control" gate, edited here because this is the screen the
    /// operator is on when they need it. Turning it ON seeds the two default levels if there are none.</summary>
    [ObservableProperty] private bool _useUserAccessControl;

    /// <summary>The security levels defined for this company.</summary>
    public ObservableCollection<SecurityLevelRow> Levels { get; } = new();

    /// <summary>The users defined for this company.</summary>
    public ObservableCollection<SecurityUserRow> Users { get; } = new();

    /// <summary>The level a new user will be created under.</summary>
    [ObservableProperty] private SecurityLevelRow? _selectedLevel;

    /// <summary>The user the password/remove verbs act on.</summary>
    [ObservableProperty] private SecurityUserRow? _selectedUser;

    /// <summary>The name for a new user.</summary>
    [ObservableProperty] private string _newUserName = string.Empty;

    /// <summary>🔴 A TRANSIENT form field. Hashed and cleared on save; never persisted as typed, never echoed
    /// back, never logged. See the type remarks.</summary>
    [ObservableProperty] private string _newPassword = string.Empty;

    /// <summary>The name for a new security level.</summary>
    [ObservableProperty] private string _newLevelName = string.Empty;

    /// <summary>The vendor's "Days Allowed for Back Dated Vouchers" for a new level. Default 0 — the vendor's
    /// default, and it means "no back-dating", not "unlimited".</summary>
    [ObservableProperty] private int _newLevelBackDatedDays;

    /// <summary>A status line shown after every verb — success or the reason it was refused.</summary>
    [ObservableProperty] private string _message = string.Empty;

    /// <summary>
    /// Shell ctor. <paramref name="now"/> is injected rather than read from the clock so the screen stays
    /// deterministic under test and does not vary with the machine's timezone.
    /// </summary>
    public SecurityUsersViewModel(CompanyStorage storage, Company company, Func<DateTimeOffset>? now = null)
    {
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _company = company ?? throw new ArgumentNullException(nameof(company));
        _now = now ?? (() => DateTimeOffset.UtcNow);

        _useUserAccessControl = company.UseUserAccessControl;
        Refresh();
    }

    /// <summary>The company's security aggregate — what every verb here mutates.</summary>
    private SecurityControl Security => _company.Security;

    /// <summary>Rebuilds both lists from the aggregate, preserving the selected rows by identity where possible.</summary>
    private void Refresh()
    {
        var selectedLevelId = SelectedLevel?.Level.Id;
        var selectedUserId = SelectedUser?.User.Id;

        Levels.Clear();
        foreach (var level in Security.Levels)
            Levels.Add(new SecurityLevelRow(
                level, Security.Users.Count(u => u.SecurityLevelId == level.Id)));

        Users.Clear();
        foreach (var user in Security.Users)
            Users.Add(new SecurityUserRow(user, Security.LevelById(user.SecurityLevelId)?.Name ?? string.Empty));

        SelectedLevel = Levels.FirstOrDefault(r => r.Level.Id == selectedLevelId) ?? Levels.FirstOrDefault();
        SelectedUser = Users.FirstOrDefault(r => r.User.Id == selectedUserId) ?? Users.FirstOrDefault();
    }

    /// <summary>
    /// Turning the gate ON seeds the two vendor-default levels if none exist — <b>Owner</b> and <b>Data Entry
    /// Operator</b> — and is idempotent, so toggling it twice cannot duplicate them. Turning it OFF removes
    /// nothing: the users and levels stay, which is what lets an operator switch the feature off and back on
    /// without losing their configuration.
    /// </summary>
    partial void OnUseUserAccessControlChanged(bool value)
    {
        _company.UseUserAccessControl = value;
        if (value) Security.SeedDefaultLevels();
        Refresh();
        Save();
        Message = value
            ? "User access control is on. The two default security levels are available."
            : "User access control is off. Users and levels are kept.";
    }

    /// <summary>Creates a security level from the form. Refuses a blank or duplicate name.</summary>
    public bool CreateLevel()
    {
        var level = new SecurityLevel(NewLevelName ?? string.Empty)
        {
            DaysAllowedForBackDatedVouchers = NewLevelBackDatedDays,
        };
        if (!Security.AddLevel(level, out var message))
        {
            Message = message;
            return false;
        }

        NewLevelName = string.Empty;
        NewLevelBackDatedDays = 0;
        Refresh();
        SelectedLevel = Levels.FirstOrDefault(r => r.Level.Id == level.Id) ?? SelectedLevel;
        Save();
        Message = message;
        return true;
    }

    /// <summary>
    /// Creates a user under <see cref="SelectedLevel"/> with <see cref="NewPassword"/>.
    ///
    /// <para>🔴 The password is hashed at once and <see cref="NewPassword"/> is CLEARED on the success path — the
    /// view model does not hold a plaintext password past this call.</para>
    /// </summary>
    public bool CreateUser()
    {
        if (SelectedLevel is not { } levelRow)
        {
            Message = "Select a security level for the new user.";
            return false;
        }

        var typed = NewPassword ?? string.Empty;
        if (!Security.PasswordPolicy.SatisfiesStrength(typed))
        {
            Message = $"The password policy requires at least {Security.PasswordPolicy.MinimumLength} characters.";
            return false;
        }

        var user = new CompanyUser(NewUserName ?? string.Empty, levelRow.Level.Id);
        if (!Security.AddUser(user, out var message))
        {
            Message = message;
            return false;
        }

        // 🔴 A BLANK BOX MEANS "NO PASSWORD", NOT "the empty password". The two behave identically at sign-in
        // (both accept only an empty string), but the LIST reports them differently — and calling a user with a
        // blank box "Set" would tell the operator a password protects that account when none does. A realised-
        // tree test caught exactly that. The policy check above still runs first, so a company with a minimum
        // length cannot create a passwordless user by leaving the box empty.
        if (typed.Length > 0) user.SetPassword(typed, _now());

        NewUserName = string.Empty;
        NewPassword = string.Empty;   // 🔴 the plaintext leaves the view model here and nowhere else
        Refresh();
        SelectedUser = Users.FirstOrDefault(r => r.User.Id == user.Id) ?? SelectedUser;
        Save();
        Message = message;
        return true;
    }

    /// <summary>
    /// Replaces <see cref="SelectedUser"/>'s password with <see cref="NewPassword"/>. This is the ONLY remedy for
    /// a forgotten password and it needs someone who can already reach this screen — there is no self-service
    /// reset, because a one-way verifier admits none.
    /// </summary>
    public bool SetPasswordForSelectedUser()
    {
        if (SelectedUser is not { } row)
        {
            Message = "Select a user first.";
            return false;
        }

        var typed = NewPassword ?? string.Empty;

        // 🔴 A blank box REMOVES the password rather than setting an empty one — the same reading as
        // CreateUser, so the list's "Set"/"Not set" stays truthful. The message says so out loud, because
        // "Set Password" with an empty box removing one is a consequence the operator must see. The policy is
        // still consulted first: a company with a minimum length refuses the blank rather than removing.
        if (typed.Length == 0 && Security.PasswordPolicy.SatisfiesStrength(typed))
        {
            row.User.ClearPassword();
            Refresh();
            Save();
            Message = $"Password removed for '{row.UserName}' — that user now signs in with no password.";
            return true;
        }

        var ok = Security.SetUserPassword(row.User.Id, typed, _now(), out var message);
        if (ok)
        {
            NewPassword = string.Empty;   // 🔴 cleared on success
            Refresh();
            Save();
        }
        Message = message;
        return ok;
    }

    /// <summary>Removes <see cref="SelectedUser"/>, refusing on the last Owner (see
    /// <see cref="SecurityControl.LastOwnerGuard"/>).</summary>
    public bool RemoveSelectedUser()
    {
        if (SelectedUser is not { } row)
        {
            Message = "Select a user first.";
            return false;
        }

        var ok = Security.RemoveUser(row.User.Id, out var message);
        if (ok)
        {
            Refresh();
            Save();
        }
        Message = message;
        return ok;
    }

    /// <summary>Ctrl+A: persists the company. Every verb above already saves; this is the explicit door.</summary>
    public bool Save()
    {
        _storage.Save(_company);
        return true;
    }
}
