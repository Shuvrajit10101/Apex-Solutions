using System;
using System.Collections.Generic;
using System.Linq;

namespace Apex.Ledger.Domain;

/// <summary>
/// One row of a security level's <b>"Disallow/Allow the following facilities"</b> list: a named facility, the
/// <see cref="SecurityAccessType"/> it is graded at, and whether the row DISALLOWS it or ALLOWS it.
///
/// <para><b>R7.</b> The screen caption and the six access types are the vendor's
/// (help.tallysolutions.com/manage-users-in-tallyprime/). <b>The facility NAMES are ours and are free text</b>,
/// deliberately: the vendor's facility list is a product-specific enumeration of ITS report and master names, and
/// inventing a fixed list of ours and calling it attested would be exactly the "clone, never invent" failure this
/// project keeps catching. An operator names the facility they are restricting.</para>
/// </summary>
public sealed class SecurityAccessRule
{
    /// <summary>The facility this row grades — a report or master name, matched case-insensitively. Free text
    /// (see the type remarks for why it is not an enum).</summary>
    public string Facility { get; set; } = string.Empty;

    /// <summary>The access type the row grades <see cref="Facility"/> at.</summary>
    public SecurityAccessType Access { get; set; } = SecurityAccessType.FullAccess;

    /// <summary><c>true</c> = a DISALLOW row (the vendor's left column), <c>false</c> = an ALLOW row.</summary>
    public bool Disallowed { get; set; }

    public SecurityAccessRule() { }

    public SecurityAccessRule(string facility, SecurityAccessType access, bool disallowed)
    {
        Facility = facility ?? throw new ArgumentNullException(nameof(facility));
        Access = access;
        Disallowed = disallowed;
    }
}

/// <summary>
/// A <b>security level</b> (census 16.2) — the named role a company user is assigned to, carrying the back-dating
/// limits and the Disallow/Allow facility list that decide what that user may reach.
///
/// <para><b>R7 — the field captions are the vendor's, verbatim</b>
/// (help.tallysolutions.com/manage-users-in-tallyprime/): <b>"Use Basic Facilities of"</b>, <b>"Days Allowed for
/// Back Dated Vouchers"</b>, <b>"Cut-off date for Backdated vouchers"</b>, and <b>"Disallow/Allow the following
/// facilities"</b> over the six access types. The two seeded level names come from the same page: <i>"TallyPrime
/// provides two default security levels: owner and data entry operator."</i> — see
/// <see cref="SecurityControl.OwnerLevelName"/> and <see cref="SecurityControl.DataEntryLevelName"/>.</para>
///
/// <para>🔴 <b>DENY BEATS ALLOW, and a level with NO rules denies NOTHING.</b> Those two sentences are the whole
/// of <see cref="IsAllowed"/>. The second is the load-bearing half: a freshly created level restricts nothing
/// until the operator writes a rule, so an empty list can never be read as "locked out of everything" — which
/// would be a silent lockout no screen shows.</para>
///
/// <para>⚠️ <b><see cref="IsOwner"/> is not a rule and cannot be overridden by one.</b> An Owner level reaches
/// everything; that is what makes "the last Owner cannot be deleted or demoted"
/// (<see cref="SecurityControl"/>) a real safeguard rather than a label.</para>
///
/// <para>Pure data + one pure predicate — framework-, DB-, clock- and RNG-free.</para>
/// </summary>
public sealed class SecurityLevel
{
    /// <summary>Stable identity; what <see cref="CompanyUser.SecurityLevelId"/> points at and what the
    /// <c>security_levels</c> row is keyed by (schema v56).</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The level's name as the operator sees it ("Owner", "Data Entry Operator", or one they type).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// <c>true</c> for a level that reaches everything and administers security. Exactly the vendor's owner:
    /// <i>"access all data, manage security settings, create and update user profiles"</i>. Not expressible as a
    /// rule, and no rule can take it away — see the type remarks.
    /// </summary>
    public bool IsOwner { get; set; }

    /// <summary>The vendor's <b>"Use Basic Facilities of"</b> — the name of a level this one inherits its base
    /// rights from, or <c>null</c> for none. Captured and persisted; <b>this build does not resolve the
    /// inheritance</b> when answering <see cref="IsAllowed"/>, and that divergence is recorded in
    /// <c>docs/invented-vs-cloned.md</c> rather than papered over.</summary>
    public string? UseBasicFacilitiesOf { get; set; }

    /// <summary>The vendor's <b>"Days Allowed for Back Dated Vouchers"</b>. <b>Default 0</b>, which is the
    /// vendor's default and means "no back-dating" — not "unlimited". Negative values are refused by
    /// <see cref="SecurityControl.AddLevel"/>.</summary>
    public int DaysAllowedForBackDatedVouchers { get; set; }

    /// <summary>The vendor's <b>"Cut-off date for Backdated vouchers"</b> — the earliest date this level may key
    /// a voucher on, or <c>null</c> for none.</summary>
    public DateOnly? CutOffDateForBackDatedVouchers { get; set; }

    /// <summary>The <b>"Disallow/Allow the following facilities"</b> list, in the order the operator entered it.</summary>
    public List<SecurityAccessRule> Rules { get; } = new();

    public SecurityLevel() { }

    public SecurityLevel(string name, bool isOwner = false)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        IsOwner = isOwner;
    }

    /// <summary>
    /// 🔴 <b>THE DECISION.</b> May this level reach <paramref name="facility"/> at <paramref name="access"/>?
    ///
    /// <para>An <see cref="IsOwner"/> level: always yes. Otherwise the rules are scanned for rows naming this
    /// facility (case-insensitive, invariant) that COVER this access type — a row graded
    /// <see cref="SecurityAccessType.FullAccess"/> covers every access type, a row graded anything else covers
    /// only itself. <b>If any covering row disallows, the answer is no</b> (deny beats allow), regardless of
    /// order and regardless of how many rows allow. With no covering row at all the answer is yes — a level
    /// denies only what it says it denies.</para>
    /// </summary>
    public bool IsAllowed(string facility, SecurityAccessType access)
    {
        if (IsOwner) return true;
        if (string.IsNullOrWhiteSpace(facility)) return true;

        var covering = Rules.Where(r =>
            string.Equals(r.Facility, facility, StringComparison.OrdinalIgnoreCase)
            && (r.Access == access || r.Access == SecurityAccessType.FullAccess));

        // Deny beats allow: one disallowing row settles it. No covering row at all → nothing was restricted.
        return !covering.Any(r => r.Disallowed);
    }

    /// <summary>
    /// Whether a voucher dated <paramref name="voucherDate"/> is inside this level's back-dating limits as of
    /// <paramref name="today"/>. Both the day allowance and the cut-off date must pass; either alone can refuse.
    /// <paramref name="today"/> is a PARAMETER and not <c>DateTime.Today</c> — a clock read inside the domain is
    /// untestable and culture/timezone-dependent.
    /// </summary>
    public bool AllowsVoucherDate(DateOnly voucherDate, DateOnly today)
    {
        if (IsOwner) return true;
        if (CutOffDateForBackDatedVouchers is { } cutOff && voucherDate < cutOff) return false;
        if (voucherDate >= today) return true;             // not back-dated at all
        return today.DayNumber - voucherDate.DayNumber <= DaysAllowedForBackDatedVouchers;
    }
}
