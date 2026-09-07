using System;
using Apex.Desktop.Services;
using Apex.Ledger.Domain;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Apex.Desktop.ViewModels;

/// <summary>
/// 🔴 <b>THE PASSWORD POLICY SCREEN (census row 16.2), reached at Alt+K (Company) &gt; Password Policy.</b>
/// Vendor, verbatim (help.tallysolutions.com/manage-users-in-tallyprime/): <i>"Press Alt+K (Company) &gt;
/// Password Policy"</i>, and the feature's own description — <i>"Through the Password Policy feature, you can
/// enable supplementary choices for passwords, including enhancing password strength and setting password
/// expiration intervals."</i>
///
/// <para>🔴 <b>WHAT IS CLONED AND WHAT IS OURS, kept apart on purpose.</b> The route, the screen name and the
/// two CAPABILITIES are the vendor's. <b>The vendor publishes no field captions or defaults for this screen</b>,
/// so the two knobs below — a minimum length for "strength", a day count for "expiration intervals" — are OUR
/// concretisation of those capabilities and are recorded as a divergence in <c>docs/invented-vs-cloned.md</c>.
/// Presenting them as cloned captions would be the "clone, never invent" failure this project keeps catching.
/// The screen says so itself in <see cref="SourceNotice"/> rather than hiding it in a comment.</para>
///
/// <para>Both knobs default OFF, which is what every company was before this row existed.</para>
/// </summary>
public sealed partial class PasswordPolicyViewModel : ViewModelBase
{
    private readonly CompanyStorage _storage;
    private readonly Company _company;

    public string Title => "Password Policy";

    /// <summary>The company this policy belongs to.</summary>
    public string CompanyName => _company.Name;

    /// <summary>🔴 The honest sourcing note, on the screen. See the type remarks.</summary>
    public const string SourceNoticeText =
        "The reference product documents this screen's two capabilities but not its individual settings, so the "
        + "minimum length and the expiry interval below are this application's own.";

    /// <summary>🔴 The honest sourcing note, on the screen. See the type remarks.</summary>
    public string SourceNotice => SourceNoticeText;

    /// <summary>🔴 The lockout consequence, verbatim from the domain constant — a policy that expires passwords
    /// makes it MORE likely somebody is locked out, so it belongs on this screen too.</summary>
    public string LockoutNotice => SecurityControl.LockoutWarningNotice;

    /// <summary>Minimum password length. 0 = no minimum (the default). OURS, not a vendor caption.</summary>
    [ObservableProperty] private int _minimumLength;

    /// <summary>Days a password stays valid; 0 = never expires (the default). OURS, not a vendor caption.
    /// Stored as NULL rather than 0 — see <see cref="PasswordPolicy.ExpiryDays"/>.</summary>
    [ObservableProperty] private int _expiryDays;

    /// <summary>A status line shown after Save.</summary>
    [ObservableProperty] private string _message = string.Empty;

    public PasswordPolicyViewModel(CompanyStorage storage, Company company)
    {
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _company = company ?? throw new ArgumentNullException(nameof(company));

        var policy = company.Security.PasswordPolicy;
        _minimumLength = policy.MinimumLength;
        _expiryDays = policy.ExpiryDays ?? 0;
    }

    /// <summary>True when both knobs are in range. Negative values are refused rather than clamped — a silently
    /// clamped policy is a policy the operator did not set.</summary>
    public bool CanSave => MinimumLength >= 0 && ExpiryDays >= 0;

    partial void OnMinimumLengthChanged(int value) => OnPropertyChanged(nameof(CanSave));
    partial void OnExpiryDaysChanged(int value) => OnPropertyChanged(nameof(CanSave));

    /// <summary>
    /// Ctrl+A / Save: writes the policy onto the company and persists it. <b>0 days is stored as NULL</b> — "never
    /// expires" — because a stored 0 would have to mean both "never" and "immediately" and cannot mean both.
    /// </summary>
    public bool Save()
    {
        if (!CanSave)
        {
            Message = "Minimum length and expiry interval cannot be negative.";
            return false;
        }

        _company.Security.PasswordPolicy = new PasswordPolicy
        {
            MinimumLength = MinimumLength,
            ExpiryDays = ExpiryDays > 0 ? ExpiryDays : null,
        };
        _storage.Save(_company);

        Message = _company.Security.PasswordPolicy.IsConfigured
            ? "Password policy saved."
            : "Password policy saved — no minimum length and no expiry.";
        return true;
    }
}
